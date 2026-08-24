"""시나리오 CSV -> 나레이션 mp3 생성 (edge-tts).

기본은 dry-run. 실제로 쓰려면 --write.
기존 파일은 건드리지 않는다(--force로만 덮어씀). 절대 삭제하지 않는다.
"""
import argparse
import asyncio
import csv
import glob
import io
import os
import re
import sys
import tempfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
SCENARIO_DIR = os.path.join(ROOT, "Assets", "Resources", "Scenarios")
CONFIG_DIR = os.path.join(ROOT, "Assets", "Resources", "ScenarioConfigs")
NARRATION_ROOT = os.path.join(ROOT, "Assets", "Resources", "Narrations")
DEFAULT_VOICE = "ko-KR-SunHiNeural"

# 초급/중급은 시나리오 폴더 + voiceInstruction, 상급/평가는 평면 + stepName
FLAT_LEVELS = ("Advanced", "Evaluation")


def unescape(s):
    """Unity YAML의 \\uXXXX 이스케이프를 되돌린다 (re에서 \\u 패턴은 못 쓴다)."""
    mark = chr(92) + "u"
    if mark not in s:
        return s
    try:
        return s.encode().decode("unicode_escape")
    except Exception:
        return s


def narration_folder(scenario):
    """ScenarioConfig.narrationSubFolder, 비면 scenarioName."""
    path = os.path.join(CONFIG_DIR, scenario + ".asset")
    if not os.path.exists(path):
        return scenario
    text = io.open(path, encoding="utf-8", errors="replace").read()
    # ★ \s*를 쓰면 개행까지 먹어 다음 줄을 캡처한다 (2026-08-24에 실제로 밟음)
    m = re.search(r"^  narrationSubFolder:[ \t]*(.*)$", text, re.M)
    if m:
        value = unescape(m.group(1).strip()).strip('"')
        if value:
            return value
    return scenario


def read_rows(scenario):
    path = os.path.join(SCENARIO_DIR, scenario + ".csv")
    if not os.path.exists(path):
        sys.exit(f"CSV 없음: {path}")
    with io.open(path, encoding="utf-8-sig", newline="") as f:
        return list(csv.DictReader(f))


def collect(rows, text_column):
    """voiceInstruction -> 읽을 문장. 충돌은 따로 모아 보고한다."""
    clips, conflicts, missing_text = {}, [], []
    for r in rows:
        name = (r.get("voiceInstruction") or "").strip()
        if not name:
            continue
        text = (r.get(text_column) or "").strip()
        if not text:
            missing_text.append(name)
            continue
        if name in clips and clips[name] != text:
            conflicts.append((name, clips[name], text))
            continue
        clips.setdefault(name, text)
    return clips, conflicts, missing_text


def collect_steps(rows, text_column):
    """상급·평가용: stepName -> 그 step의 첫 문장."""
    steps = {}
    for r in rows:
        step = (r.get("stepName") or "").strip()
        text = (r.get(text_column) or "").strip()
        if step and text and step not in steps:
            steps[step] = text
    return steps


def safe_target(level, folder, name):
    """Narrations 밖으로 나가는 경로는 거부한다."""
    if level in FLAT_LEVELS:
        target = os.path.join(NARRATION_ROOT, level, name + ".mp3")
    else:
        target = os.path.join(NARRATION_ROOT, level, folder, name + ".mp3")
    resolved = os.path.abspath(target)
    if not resolved.startswith(os.path.abspath(NARRATION_ROOT) + os.sep):
        sys.exit(f"거부: Narrations 밖으로 나가는 경로다 -> {resolved}")
    return resolved


async def synth(text, path, voice):
    """임시 파일에 받은 뒤 옮긴다. 중간에 끊겨도 반쪽 파일이 안 남는다."""
    import edge_tts
    directory = os.path.dirname(path)
    os.makedirs(directory, exist_ok=True)
    fd, tmp = tempfile.mkstemp(suffix=".mp3", dir=directory)
    os.close(fd)
    try:
        await edge_tts.Communicate(text, voice).save(tmp)
        if os.path.getsize(tmp) == 0:
            raise RuntimeError("생성된 파일이 0바이트다")
        os.replace(tmp, path)          # 삭제가 아니라 교체 — guid 유지
    finally:
        if os.path.exists(tmp):
            os.remove(tmp)


def coverage_report(rows):
    """상급·평가에서 무음이 되는 stepName을 보고한다."""
    steps = {(r.get("stepName") or "").strip() for r in rows}
    steps.discard("")
    out = []
    for level in FLAT_LEVELS:
        have = {os.path.basename(p)[:-4]
                for p in glob.glob(os.path.join(NARRATION_ROOT, level, "*.mp3"))}
        miss = sorted(steps - have)
        out.append((level, miss))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("scenario", help="시나리오명 (CSV 파일명과 동일)")
    ap.add_argument("--write", action="store_true", help="실제 생성 (없으면 dry-run)")
    ap.add_argument("--force", action="store_true", help="기존 파일도 덮어씀")
    ap.add_argument("--levels", default="Beginner,Intermediate")
    ap.add_argument("--only", default="", help="쉼표로 구분한 클립명만")
    ap.add_argument("--voice", default=DEFAULT_VOICE)
    ap.add_argument("--text-column", default="textInstruction")
    args = ap.parse_args()

    rows = read_rows(args.scenario)
    folder = narration_folder(args.scenario)
    levels = [x.strip() for x in args.levels.split(",") if x.strip()]
    only = {x.strip() for x in args.only.split(",") if x.strip()}

    clips, conflicts, missing_text = collect(rows, args.text_column)
    steps = collect_steps(rows, args.text_column)

    print(f"시나리오 {args.scenario} · CSV {len(rows)}행 · 나레이션 폴더 '{folder}'")
    print(f"음성 {args.voice} · 난이도 {', '.join(levels)}")

    if conflicts:
        print(f"\n★ 충돌 {len(conflicts)}건 — 같은 파일명에 다른 문장이 붙어 있다. 생성하지 않는다.")
        for name, a, b in conflicts:
            print(f"  {name}\n    A: {a}\n    B: {b}")
        sys.exit(1)
    if missing_text:
        print(f"\n주의: voiceInstruction은 있는데 {args.text_column}이 빈 행 {len(missing_text)}개 "
              f"→ 건너뜀 ({', '.join(sorted(set(missing_text)))})")

    plan = []
    for level in levels:
        source = steps if level in FLAT_LEVELS else clips
        for name, text in sorted(source.items()):
            if only and name not in only:
                continue
            target = safe_target(level, folder, name)
            exists = os.path.exists(target)
            action = "덮어씀" if exists and args.force else ("건너뜀" if exists else "생성")
            plan.append((action, level, name, text, target))

    for action in ("생성", "덮어씀", "건너뜀"):
        items = [p for p in plan if p[0] == action]
        if not items:
            continue
        print(f"\n[{action}] {len(items)}개")
        for _, level, name, text, _t in items:
            print(f"  {level:13} {name:16} {text[:50]}")

    print("\n--- 상급·평가 커버리지 ---")
    for level, miss in coverage_report(rows):
        print(f"  {level}: 없는 stepName {len(miss)}개" + (f" — {', '.join(miss)}" if miss else ""))

    todo = [p for p in plan if p[0] in ("생성", "덮어씀")]
    if not args.write:
        print(f"\n※ dry-run이다. 실제로 만들려면 --write 를 붙인다. (대상 {len(todo)}개)")
        return
    if not todo:
        print("\n만들 것이 없다.")
        return

    print(f"\n생성 시작 — {len(todo)}개")
    ok = 0
    for _action, level, name, text, target in todo:
        try:
            asyncio.run(synth(text, target, args.voice))
            ok += 1
            print(f"  OK  {level}/{name}.mp3")
        except Exception as e:
            print(f"  실패 {level}/{name}.mp3 — {e}")
    print(f"\n완료 {ok}/{len(todo)}")
    print("Unity로 포커스를 옮겨 임포트하면 .meta가 생성된다. 이 스크립트는 .meta를 만들지 않는다.")


if __name__ == "__main__":
    main()
