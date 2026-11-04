"""Offline transcription of the seven complex-Chuna expert videos.

Defaults to a read-only plan. --write creates new raw transcripts exclusively;
it never replaces an existing file or changes a video, scene, or scenario CSV.
Install faster-whisper separately and pass a downloaded local model directory.
"""

from __future__ import annotations

import argparse
from dataclasses import asdict
import importlib.metadata
import json
import os
from pathlib import Path
import subprocess
import time


ROOT = Path(__file__).resolve().parents[2]
NAMES = [
    "제2늑골_상방변위",
    "제1늑골 교정기법",
    "앙와위_흉추_신전변위",
    "복와위_하부흉추_굴곡변위",
    "두개골OM교정",
    "두개골PM교정",
    "두개골PJ교정",
]
GLOSSARY = (
    "추나 교정기법. 후두골, 관골궁, 유양돌기, 파리에탈 노치, "
    "주동수, 보조수, 견착, 두상골, 극돌기, 횡돌기, "
    "두방, 족방, 제1늑골, 제2늑골, 굴곡변위, 신전변위. "
    "occiput, temporal bone, zygomatic arch, mastoid, MCP joint, "
    "OM suture, parietomastoid suture, parietal notch, petrojugular, "
    "subluxation, five-finger hold, body drop, pisiform, deltoid part."
)


def stamp(seconds: float) -> str:
    millis = round(seconds * 1000)
    hours, millis = divmod(millis, 3_600_000)
    minutes, millis = divmod(millis, 60_000)
    secs, millis = divmod(millis, 1000)
    return f"{hours:02}:{minutes:02}:{secs:02},{millis:03}"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--only", choices=NAMES)
    parser.add_argument("--device", choices=["cpu", "cuda"], default="cpu")
    parser.add_argument("--audio-filter", help="Optional ffmpeg audio filter for a comparison pass")
    parser.add_argument("--context", action="store_true", help="Keep previous text as decoding context")
    parser.add_argument("--hotwords", help="Vocabulary hints repeated in each decoding window")
    parser.add_argument("--write", action="store_true")
    args = parser.parse_args()
    names = [args.only] if args.only else NAMES
    for name in names:
        source = ROOT / "Assets/Resources/Videos" / f"{name}.mp4"
        if not source.is_file():
            raise FileNotFoundError(source)
        for suffix in [".raw.json", ".raw.srt"]:
            target = args.output / f"{name}{suffix}"
            if target.exists():
                raise FileExistsError(target)
        print(f"PLAN {source.name}", flush=True)
    if not args.write:
        print("Dry run. Add --write to transcribe.", flush=True)
        return
    # A local model path and local_files_only prevent network access at runtime.
    from faster_whisper import WhisperModel
    import numpy as np

    started = time.monotonic()
    model = WhisperModel(
        str(args.model.resolve()),
        device=args.device,
        compute_type="int8" if args.device == "cpu" else "int8_float16",
        cpu_threads=min(8, os.cpu_count() or 1),
        local_files_only=True,
    )
    print(f"MODEL loaded in {time.monotonic() - started:.1f}s", flush=True)
    args.output.mkdir(parents=True, exist_ok=True)
    for name in names:
        source = ROOT / "Assets/Resources/Videos" / f"{name}.mp4"
        probe = json.loads(subprocess.check_output([
            "ffprobe", "-v", "error", "-show_entries",
            "format=duration:stream=index,codec_type,codec_name,sample_rate,channels",
            "-of", "json", str(source),
        ], encoding="utf-8"))
        began = time.monotonic()
        print(f"START {name}", flush=True)
        # Decode with the installed ffmpeg; this also fixes the exact audio track
        # and avoids depending on changes to PyAV's container-opening API.
        decode_command = [
            "ffmpeg", "-v", "error", "-i", str(source), "-map", "0:a:0",
            "-vn",
        ]
        if args.audio_filter:
            decode_command.extend(["-af", args.audio_filter])
        decode_command.extend(["-ar", "16000", "-ac", "1", "-f", "f32le", "pipe:1"])
        pcm = subprocess.check_output(decode_command)
        audio = np.frombuffer(pcm, dtype=np.float32).copy()
        segments, info = model.transcribe(
            audio, language="ko", task="transcribe", beam_size=5,
            word_timestamps=True, vad_filter=True,
            vad_parameters={"min_silence_duration_ms": 500},
            condition_on_previous_text=args.context, initial_prompt=GLOSSARY,
            hotwords=args.hotwords,
        )
        rows = []
        for segment in segments:
            rows.append(asdict(segment))
            print(
                f"  {stamp(segment.start)} --> {stamp(segment.end)} {segment.text}",
                flush=True,
            )
        payload = {
            "source": str(source.relative_to(ROOT)),
            "source_size": source.stat().st_size,
            "source_mtime_ns": source.stat().st_mtime_ns,
            "probe": probe,
            "model": str(args.model.resolve()),
            "faster_whisper_version": importlib.metadata.version("faster-whisper"),
            "language": info.language,
            "duration": info.duration,
            "duration_after_vad": info.duration_after_vad,
            "initial_prompt": GLOSSARY,
            "audio_filter": args.audio_filter,
            "condition_on_previous_text": args.context,
            "hotwords": args.hotwords,
            "human_audio_review": False,
            "segments": rows,
        }
        with (args.output / f"{name}.raw.json").open("x", encoding="utf-8", newline="\n") as f:
            json.dump(payload, f, ensure_ascii=False, indent=2)
            f.write("\n")
        with (args.output / f"{name}.raw.srt").open("x", encoding="utf-8", newline="\n") as f:
            for number, row in enumerate(rows, 1):
                f.write(f"{number}\n{stamp(row['start'])} --> {stamp(row['end'])}\n{row['text'].strip()}\n\n")
        print(f"DONE {name}: {len(rows)} segments, {time.monotonic() - began:.1f}s", flush=True)
    print(f"ALL DONE in {time.monotonic() - started:.1f}s", flush=True)


if __name__ == "__main__":
    main()
