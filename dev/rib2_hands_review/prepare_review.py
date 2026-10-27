"""Prepare a separate review copy. Existing CSVs/scenes are read-only inputs.

No tracking CSV is parsed. The reused source-only draft has manual video keys,
generic hand bind geometry and authored finger poses. Default is dry-run.
"""
from pathlib import Path
import argparse
import csv
import hashlib
import json
import math
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Captures/Rib2HandsReview_V02_20261027"
BASE = ROOT / "Captures/Rib2VideoTextOnly_V0.1_20261026"


def digest(path):
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def protected_files():
    paths = [
        ROOT / "Assets/Scenes/TrainingScene.unity",
        ROOT / "Assets/Scenes/lobby.unity",
        ROOT / "Assets/Resources/Scenarios/제2늑골_상방변위.csv",
        ROOT / "Assets/Resources/ScenarioConfigs/제2늑골_상방변위.asset",
    ]
    paths.extend((ROOT / "Assets/Resources/HandPoseData").glob("제2*.csv"))
    paths.extend(p for p in (ROOT / "Assets/Demos/Rib2VideoTextScene/V01").rglob("*") if p.is_file())
    paths.extend(p for p in BASE.iterdir() if p.is_file())
    return {p.relative_to(ROOT).as_posix(): digest(p) for p in paths if p.is_file()}


def validate_csv(path):
    frames = {}
    with path.open(encoding="utf-8", newline="") as stream:
        for row in csv.DictReader(stream):
            index, joint = int(row["FrameIndex"]), int(row["JointID"])
            hand = row["HandType"]
            assert hand in ("Left", "Right") and 0 <= joint < 26
            numbers = [float(row[n]) for n in ("LocalPosX", "LocalPosY", "LocalPosZ", "LocalRotX", "LocalRotY", "LocalRotZ", "LocalRotW", "Timestamp")]
            assert all(math.isfinite(v) for v in numbers)
            assert abs(sum(v * v for v in numbers[3:7]) - 1) < 1e-5
            assert abs(float(row["Timestamp"]) - index / 30) < 1e-7
            seen = frames.setdefault(index, set())
            assert (hand, joint) not in seen
            seen.add((hand, joint))
    assert set(frames) == set(range(248))
    assert all(len(seen) == 52 for seen in frames.values())
    return {"frames": len(frames), "jointsPerHand": 26, "fps": 30, "durationSeconds": 247 / 30, "finiteNormalizedPoses": True}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--write", action="store_true")
    parser.add_argument("--verify-preservation", action="store_true")
    args = parser.parse_args()
    if args.verify_preservation:
        before = json.loads((OUT / "preservation_before.json").read_text(encoding="utf-8"))
        after = {name: digest(ROOT / name) if (ROOT / name).is_file() else None for name in before["sha256"]}
        changed = [name for name, value in before["sha256"].items() if after[name] != value]
        result = {"success": not changed, "protectedFiles": len(after), "changed": changed, "sha256": after}
        (OUT / "preservation_verified.json").write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
        print(json.dumps({k: v for k, v in result.items() if k != "sha256"}, ensure_ascii=False))
        if changed:
            raise RuntimeError("Protected inputs changed")
        return
    source_manifest = json.loads((BASE / "reconstruction_manifest.json").read_text(encoding="utf-8"))
    assert source_manifest["tracking_input_used"] is False
    source_csv = BASE / "video_text_demo.csv"
    validation = validate_csv(source_csv)
    for entry in source_manifest["outputs"]:
        if entry["file"] == source_csv.name:
            assert entry["sha256"] == digest(source_csv)
            break
    else:
        raise RuntimeError("CSV missing from source-only provenance manifest")
    draft = {
        "label": "제2늑골 양손 영상 가이드 — 독립 검토 V02",
        "csvPath": (OUT / "data.csv").relative_to(ROOT).as_posix(),
        "sourceFramesDirectory": (OUT / "source_frames").relative_to(ROOT).as_posix(),
        "sourceStartSeconds": 56.5, "fps": 30, "frameCount": 248, "cycleStartSeconds": 3.75,
        "sourceVideo": "D:/전문가 영상 촬영본/3인칭 시점 카메라/제2늑골 교정기법.MOV",
        "provenance": "Read-only reuse of eaeb1c3 video/text-only authored poses; no expert tracking input. Original third-person/bodycam roles rechecked 2026-10-27. New private review window and outputs; existing V01 untouched.",
        "reusedCsv": source_csv.relative_to(ROOT).as_posix(), "csvSha256": digest(source_csv),
        "sourceOnlyManifest": (BASE / "reconstruction_manifest.json").relative_to(ROOT).as_posix(),
        "sourceOnlyManifestSha256": digest(BASE / "reconstruction_manifest.json"),
        "roles": {"Left": "청록: 시술자 왼손, 환자 오른손/손목 유도", "Right": "주황: 시술자 오른손, 상흉곽 지지"},
        "observed": ["양손의 역할", "영상에서 보이는 손목과 손바닥 화면 위치", "시작 파지와 팔 한 주기의 순서"],
        "assumed": ["깊이와 절대 배율", "가려진 손가락과 손 비틀림", "흉곽 접촉의 정확한 해부학 위치", "손가락은 손별 고정 파지 초안"],
        "scope": "Settled starting grip plus one arm cycle, not approach/release or a full procedure. The cloned c9 arm uses presentation two-bone IK, not the anatomical force rig.",
        "limitations": "No motion capture, clinical contact/pressure validation, force model, live free-joint following, grading integration or Quest performance claim.",
        "baselineCommitPlayUnverified": subprocess.check_output(["git", "rev-parse", "--short", "HEAD"], cwd=ROOT, text=True).strip(),
        "validation": validation,
    }
    if args.write:
        if OUT.exists():
            raise FileExistsError("Use a new version; existing review outputs are never overwritten")
        before = {"scope": "Working-tree bytes before this isolated review, including pre-existing unsaved-to-Git differences", "sha256": protected_files()}
        OUT.mkdir(parents=True)
        (OUT / "source_frames").mkdir()
        shutil.copyfile(source_csv, OUT / "data.csv")
        for name in ("source_annotations.json", "text_evidence.json"):
            shutil.copyfile(BASE / name, OUT / name)
        (OUT / "draft.json").write_text(json.dumps(draft, ensure_ascii=False, indent=2), encoding="utf-8")
        (OUT / "preservation_before.json").write_text(json.dumps(before, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(draft, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
