"""Export edited ASR drafts as SRTs and Unity TextAssets; dry-run by default.

Word boundaries come from the raw ASR. Changed phrases use interpolated word
boundaries within their original speech interval, so playback review is required.
All outputs are new files and existing outputs are never overwritten.
"""

from __future__ import annotations

import argparse
from copy import deepcopy
from difflib import SequenceMatcher
import hashlib
import json
from pathlib import Path
import re
import textwrap
import zipfile

from transcribe import ROOT, stamp
from verify import unity_parse, verify_one


TOKEN = re.compile(r"\[[^\]]+\]|\S+")


def key(word: str) -> str:
    return re.sub(r"[^0-9a-z가-힣]", "", word.lower())


def align(row: dict, text: str) -> list[dict]:
    old = [word for word in row["words"] if word["word"].strip()]
    new = TOKEN.findall(text)
    if not old or not new:
        raise ValueError("Missing original word timestamps or edited text")
    result = []
    matcher = SequenceMatcher(None, [key(w["word"]) for w in old], [key(w) for w in new], autojunk=False)
    for operation, i, j, a, b in matcher.get_opcodes():
        if operation == "delete":
            continue
        if operation == "equal":
            result.extend({"text": text, "start": word["start"], "end": word["end"]}
                          for text, word in zip(new[a:b], old[i:j]))
            continue
        if i < j:
            start, end = old[i]["start"], old[j - 1]["end"]
        else:
            start = old[i - 1]["end"] if i else row["start"]
            end = old[i]["start"] if i < len(old) else row["end"]
        end = max(start, end)
        weight = sum(len(word) for word in new[a:b])
        passed = 0
        for text in new[a:b]:
            t0 = start + (end - start) * passed / weight
            passed += len(text)
            t1 = start + (end - start) * passed / weight
            result.append({"text": text, "start": t0, "end": t1})
    for word in result:
        word["start"] = max(row["start"], min(row["end"], word["start"]))
        word["end"] = max(word["start"], min(row["end"], word["end"]))
    return result


def make_cues(rows: list[dict], edits: list[str]) -> list[dict]:
    if len(rows) != len(edits):
        raise ValueError(f"Raw/edited segment counts differ: {len(rows)} / {len(edits)}")
    cues = []
    for row, text in zip(rows, edits):
        words = align(row, text)
        groups, group = [], []
        for word in words:
            candidate = " ".join(w["text"] for w in group + [word])
            if group and (len(candidate) > 53 or word["end"] - group[0]["start"] > 5.7
                          or word["start"] - group[-1]["end"] > 1.3):
                groups.append(group)
                group = []
            group.append(word)
            if word["text"].endswith((".", "?", "!")):
                groups.append(group)
                group = []
        if group:
            groups.append(group)
        # Insertions can have zero-length boundaries. For that segment only,
        # allocate its cues within the original sentence interval by text length.
        boundaries_valid = all(g[-1]["end"] > g[0]["start"] for g in groups)
        boundaries_valid &= all(groups[i][0]["start"] > groups[i - 1][0]["start"]
                                for i in range(1, len(groups)))
        total = sum(sum(len(w["text"]) for w in g) for g in groups)
        passed = 0
        for group in groups:
            text = " ".join(w["text"] for w in group)
            if boundaries_valid:
                start, end = group[0]["start"], group[-1]["end"]
            else:
                weight = sum(len(w["text"]) for w in group)
                start = row["start"] + (row["end"] - row["start"]) * passed / total
                passed += weight
                end = row["start"] + (row["end"] - row["start"]) * passed / total
            lines = textwrap.wrap(text, width=30, break_long_words=False, break_on_hyphens=False)
            if len(lines) > 2 or any(len(line) > 30 for line in lines):
                raise ValueError(f"Subtitle too wide: {text!r}")
            cues.append({"start": start, "end": end, "text": "\n".join(lines)})
    for i, cue in enumerate(cues):
        if i + 1 < len(cues):
            cue["end"] = min(cue["end"], cues[i + 1]["start"] - 0.001)
        cue["start"] = round(cue["start"], 3)
        cue["end"] = round(cue["end"], 3)
        if cue["end"] <= cue["start"]:
            raise ValueError(f"Invalid edited cue timing: {cue}")
    return cues


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--delivery", type=Path, default=ROOT / "Captures/ExpertSubtitles_20261104")
    parser.add_argument("--edits", type=Path, default=Path(__file__).with_name("edits.json"))
    parser.add_argument("--write", action="store_true")
    args = parser.parse_args()
    edits = json.loads(args.edits.read_text(encoding="utf-8"))
    pending: dict[Path, bytes] = {}
    inventory, points, transcript = [], [], []
    for name, texts in edits["videos"].items():
        source = args.delivery / edits["primary_source"] / f"{name}.raw.json"
        raw = json.loads(source.read_text(encoding="utf-8"))
        rows = deepcopy(raw["segments"])
        for i in range(len(rows) - 1):
            rows[i]["end"] = min(rows[i]["end"], rows[i + 1]["start"])
        if name == "두개골PJ교정":
            # Recover the short opening phrase omitted by large-v3. The medium
            # pass contains it at 0--2.08s; retain that pass's first word timings.
            alternative = json.loads((args.delivery / f"{name}.raw.json").read_text(encoding="utf-8"))
            rows[0] = deepcopy(alternative["segments"][0])
            rows[0]["end"] = rows[1]["start"]
            texts = ["보여드리도록 하겠습니다. " + texts[0], *texts[1:]]
        cues = make_cues(rows, texts)
        blocks = [f"{i}\n{stamp(c['start'])} --> {stamp(c['end'])}\n{c['text']}\n"
                  for i, c in enumerate(cues, 1)]
        data = ("\n".join(blocks) + "\n").encode("utf-8")
        if len(unity_parse(data.decode("utf-8"))) != len(cues):
            raise ValueError(f"Unity parser mismatch: {name}")
        pending[args.delivery / f"{name}.srt"] = data
        pending[ROOT / "Assets/Resources/Videos" / f"{name}_자막.txt"] = data
        inventory.append({
            "name": name, "cues": len(cues), "sha256": hashlib.sha256(data).hexdigest(),
            "source_video": raw["source"], "source_size": raw["source_size"],
            "source_mtime_ns": raw["source_mtime_ns"], "duration": raw["duration"],
            "raw_transcript": str(source.relative_to(ROOT)),
        })
        transcript.append(f"=== {name} ===\n" + "".join(
            f"{stamp(c['start'])} --> {stamp(c['end'])}\n{c['text']}\n\n" for c in cues))
        for cue in cues:
            if "확인 필요]" in cue["text"].replace("\n", " "):
                points.append({"video": name, "start": cue["start"], "end": cue["end"],
                               "reason": cue["text"].replace("\n", " ")})
    points.extend(edits["review_notes"])
    points.sort(key=lambda p: (p["video"], p["start"]))
    inventory_report = {
        "date": "2026-11-04", "status": edits["status"], "video_count": len(inventory),
        "total_cues": sum(row["cues"] for row in inventory), "review_points": len(points),
        "human_audio_review": False, "unity_play_verified": False,
        "timing": "ASR word timestamps; edited phrases interpolate within their original speech intervals.",
        "files": inventory, "needs_review": points,
    }
    pending[args.delivery / "manifest.json"] = (json.dumps(inventory_report, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    pending[args.delivery / "전체_자막_검토.txt"] = (edits["status"] + "\n\n" + "\n".join(transcript)).encode("utf-8")
    pending[args.delivery / "청취_확인_목록.tsv"] = ("영상\t시작\t끝\t확인 내용\n" + "".join(
        f"{p['video']}\t{stamp(p['start'])}\t{stamp(p['end'])}\t{p['reason']}\n" for p in points)).encode("utf-8")
    readme = (
        "복잡추나 전문가 영상 7종 — 자막 초안 (2026-11-04)\n\n"
        "프로젝트 영상의 실제 음성으로 전사했습니다. Whisper medium과 large-v3 결과를 대조하고 "
        "전문 용어·조사·중복 표현을 정리한 편집 자막입니다. 기존 CSV는 용어 확인에만 참고했습니다.\n"
        "사람의 청취 검수와 Unity Play 검증은 아직 하지 않았습니다. "
        "불확실한 방향·수치·구절은 자막 안에 [청취 확인 필요]로 표시했습니다. "
        "청취_확인_목록.tsv에 구간별 확인 사항이 있습니다.\n\n"
        "SRT 7개: 각 원본 MP4와 함께 재생하면 됩니다. 자막을 영상에 영구 삽입하지 않았습니다.\n"
        "앱용 사본: Assets/Resources/Videos/{시나리오명}_자막.txt (SRT와 내용 동일).\n"
        "Unity 확인 절차: 로비의 복잡추나에서 해당 술기를 열고 전문가 영상을 재생하세요. "
        "처음·중간·끝 및 확인 목록의 시각으로 이동해 문구와 동기를 확인하세요. "
        "로그의 [GuideVideo] 자막 로드 줄수도 확인하세요. 7종 모두 반복하세요.\n"
        "현재 프로젝트에 있는 3인칭 영상 7종 기준입니다. 별도 1인칭 영상의 시간축은 검증하지 않았습니다.\n\n"
        "원본 MP4, 씬, 프리팹, C# 코드, 시나리오 CSV는 이 작업에서 수정하지 않았습니다.\n"
    )
    pending[args.delivery / "README.txt"] = readme.encode("utf-8")
    archive = args.delivery / "복잡추나_전문가영상_자막_7종_초안.zip"
    for path in [*pending, archive]:
        if path.exists():
            raise FileExistsError(path)
    print(json.dumps({"videos": inventory_report["video_count"], "cues": inventory_report["total_cues"],
                      "review_points": len(points), "new_files": len(pending) + 1,
                      "write": args.write}, ensure_ascii=False))
    if not args.write:
        return
    for path, data in pending.items():
        path.parent.mkdir(parents=True, exist_ok=True)
        with path.open("xb") as output:
            output.write(data)
    failures = [result for name in edits["videos"]
                if (result := verify_one(ROOT, args.delivery, name))["errors"]]
    if failures:
        raise ValueError(json.dumps(failures, ensure_ascii=False))
    with zipfile.ZipFile(archive, "x", compression=zipfile.ZIP_DEFLATED) as output:
        for path in pending:
            if path.parent == args.delivery and path.suffix in {".srt", ".tsv", ".txt"}:
                output.write(path, arcname=path.name)
    print(f"Created {archive}")


if __name__ == "__main__":
    main()
