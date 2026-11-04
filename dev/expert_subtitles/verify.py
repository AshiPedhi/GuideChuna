"""Read-only validation of complex-Chuna subtitles and their Unity copies.

Uses only Python's standard library and ffprobe. The optional JSON report is
created exclusively: an existing report is never replaced.
"""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
import re
import struct
import subprocess
import sys


ROOT = Path(__file__).resolve().parents[2]
NAMES = (
    "두개골OM교정",
    "두개골PM교정",
    "두개골PJ교정",
    "앙와위_흉추_신전변위",
    "복와위_하부흉추_굴곡변위",
    "제1늑골 교정기법",
    "제2늑골_상방변위",
)
STAMP = r"[0-9]{2}:[0-5][0-9]:[0-5][0-9],[0-9]{3}"
TIME_LINE = re.compile(rf"^({STAMP}) --> ({STAMP})$")
FLOAT = re.compile(r"^[+-]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][+-]?[0-9]+)?$")


def single(value: float) -> float:
    """Round an arithmetic step to System.Single precision."""
    try:
        return struct.unpack("f", struct.pack("f", value))[0]
    except OverflowError:
        return math.copysign(math.inf, value)


def unity_time(value: str) -> float | None:
    """GuideVideoController.TryTime for numeric subtitle timestamps.

    The strict SRT validator below rejects nonstandard timestamp spellings;
    this mirror also accepts the controller's colon-separated numeric forms.
    """
    acc = 0.0
    for part in value.strip().replace(",", ".").split(":"):
        part = part.strip()
        if not FLOAT.fullmatch(part):
            return None
        acc = single(single(acc * 60.0) + single(float(part)))
    return acc


def unity_parse(text: str) -> list[tuple[float, float, str]]:
    """Mirror ParseSrt block selection, body trimming, and start-time sort.

    Source: GuideVideoController.cs ParseSrt/TryTime, read 2026-11-04.
    This verifies parsing compatibility, not Unity playback or UI rendering.
    """
    rows = []
    for block in text.replace("\r", "").split("\n\n"):
        if not block:
            continue
        lines = block.strip("\n").split("\n")
        time_index = 1 if len(lines) > 1 and "-->" not in lines[0] else 0
        if time_index >= len(lines):
            continue
        parts = lines[time_index].split("-->")
        if len(parts) != 2:
            continue
        start, end = (unity_time(part) for part in parts)
        if start is None or end is None:
            continue
        rows.append((start, end, "\n".join(line.strip() for line in lines[time_index + 1:])))
    return sorted(rows, key=lambda row: row[0])


def stamp_millis(value: str) -> int:
    hours, minutes, tail = value.split(":")
    seconds, millis = tail.split(",")
    return ((int(hours) * 60 + int(minutes)) * 60 + int(seconds)) * 1000 + int(millis)


def probe_duration(video: Path) -> float:
    result = subprocess.run(
        ["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "json", str(video)],
        capture_output=True, text=True, encoding="utf-8", timeout=30, check=True,
    )
    duration = float(json.loads(result.stdout)["format"]["duration"])
    if not math.isfinite(duration) or duration <= 0:
        raise ValueError(f"Invalid video duration: {duration}")
    return duration


def verify_one(root: Path, delivery: Path, name: str) -> dict:
    asset = root / "Assets/Resources/Videos" / f"{name}_자막.txt"
    srt = delivery / f"{name}.srt"
    video = root / "Assets/Resources/Videos" / f"{name}.mp4"
    result = {
        "name": name, "asset": str(asset), "srt": str(srt), "video": str(video),
        "duration_seconds": None, "cue_count": 0, "unity_cue_count": 0,
        "identical_delivery_copy": False, "errors": [], "warnings": [],
    }
    errors, warnings = result["errors"], result["warnings"]
    for label, path in (("Unity subtitle", asset), ("delivery SRT", srt), ("video", video)):
        if not path.is_file():
            errors.append(f"Missing {label}: {path}")
    if video.is_file():
        try:
            result["duration_seconds"] = probe_duration(video)
        except (OSError, ValueError, KeyError, subprocess.SubprocessError) as exc:
            errors.append(f"ffprobe failed: {exc}")
    if not asset.is_file():
        return result
    data = asset.read_bytes()
    if srt.is_file():
        result["identical_delivery_copy"] = data == srt.read_bytes()
        if not result["identical_delivery_copy"]:
            errors.append("Unity subtitle and delivery SRT differ byte for byte")
    if data.startswith(b"\xef\xbb\xbf"):
        errors.append("UTF-8 BOM is present")
    try:
        text = data.decode("utf-8")
    except UnicodeDecodeError as exc:
        errors.append(f"Invalid UTF-8: {exc}")
        return result
    normalized = text.replace("\r", "")
    blocks = normalized.strip("\n").split("\n\n") if normalized.strip("\n") else []
    if not blocks:
        errors.append("No subtitle cues")
    strict_rows = []
    previous_start = previous_end = None
    duration = result["duration_seconds"]
    for expected, block in enumerate(blocks, 1):
        lines = block.split("\n")
        label = f"Cue {expected}"
        if len(lines) < 3:
            errors.append(f"{label}: expected index, timing, and subtitle text")
            continue
        if lines[0] != str(expected):
            errors.append(f"{label}: index is {lines[0]!r}, expected {expected}")
        match = TIME_LINE.fullmatch(lines[1])
        if not match:
            errors.append(f"{label}: invalid canonical SRT timing {lines[1]!r}")
            continue
        start, end = (stamp_millis(value) for value in match.groups())
        body_lines = lines[2:]
        body = "\n".join(line.strip() for line in body_lines)
        if not body.strip() or any(not line.strip() for line in body_lines):
            errors.append(f"{label}: empty subtitle text or empty body line")
        if not 0 <= start < end:
            errors.append(f"{label}: requires 0 <= start < end")
        if duration is not None and end / 1000 > duration:
            errors.append(f"{label}: ends at {end / 1000:.3f}s after video duration {duration:.6f}s")
        if previous_start is not None and start <= previous_start:
            errors.append(f"{label}: start time is not strictly increasing")
        if previous_end is not None and start < previous_end:
            errors.append(f"{label}: overlaps previous cue by {(previous_end - start) / 1000:.3f}s")
        if len(body_lines) > 2:
            warnings.append(f"{label}: {len(body_lines)} lines exceeds 2")
        for line_number, line in enumerate(body_lines, 1):
            if len(line) > 30:
                warnings.append(f"{label}, line {line_number}: {len(line)} characters exceeds 30")
        strict_rows.append((start / 1000, end / 1000, body))
        previous_start, previous_end = start, end
    result["cue_count"] = len(strict_rows)
    parsed = unity_parse(text)
    result["unity_cue_count"] = len(parsed)
    if len(parsed) != len(strict_rows):
        errors.append(f"Unity parser reads {len(parsed)} cues; strict SRT parser reads {len(strict_rows)}")
    elif not errors:
        for index, (actual, expected) in enumerate(zip(parsed, strict_rows), 1):
            if actual[2] != expected[2] or abs(actual[0] - expected[0]) > 0.0001 or abs(actual[1] - expected[1]) > 0.0001:
                errors.append(f"Cue {index}: Unity parsing changes timing or subtitle text")
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=ROOT, help="Project root (default: this repository)")
    parser.add_argument("--delivery", type=Path, help="SRT folder (default: ROOT/Captures/ExpertSubtitles_20261104)")
    parser.add_argument("--report", type=Path, help="Create a new JSON report; never overwrite a file")
    args = parser.parse_args()
    if args.report is not None and args.report.exists():
        parser.error(f"Report already exists: {args.report}")
    root = args.root.resolve()
    delivery = args.delivery.resolve() if args.delivery is not None else root / "Captures/ExpertSubtitles_20261104"
    files = [verify_one(root, delivery, name) for name in NAMES]
    report = {
        "root": str(root), "delivery": str(delivery),
        "expected_files": len(NAMES), "total_cues": sum(row["cue_count"] for row in files),
        "error_count": sum(len(row["errors"]) for row in files),
        "warning_count": sum(len(row["warnings"]) for row in files),
        "unity_play_verified": False, "human_audio_review": False,
        "files": files,
    }
    report["passed"] = report["error_count"] == 0
    rendered = json.dumps(report, ensure_ascii=False, indent=2)
    if args.report is not None:
        with args.report.open("x", encoding="utf-8", newline="\n") as output:
            output.write(rendered + "\n")
    print(rendered)
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    sys.exit(main())
