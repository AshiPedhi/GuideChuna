"""시나리오 CSV·Config·나레이션·클립·손녹화 정합 점검 — 읽기 전용."""
import csv
import glob
import io
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
SCEN = os.path.join(ROOT, "Assets", "Resources", "Scenarios")
CONF = os.path.join(ROOT, "Assets", "Resources", "ScenarioConfigs")
NARR = os.path.join(ROOT, "Assets", "Resources", "Narrations")
CONDITION_SRC = os.path.join(ROOT, "Assets", "Scripts", "ClaudeScripts",
                             "Scenario", "ScenarioConditionManager.cs")
ESCAPE_MARK = chr(92) + "u"
FLAT_LEVELS = ("Advanced", "Evaluation")


def unescape(s):
    if ESCAPE_MARK not in s:
        return s
    try:
        return s.encode().decode("unicode_escape")
    except Exception:
        return s


def known_condition_types():
    """코드의 switch에서 실제 처리되는 conditionType을 읽어온다."""
    if not os.path.exists(CONDITION_SRC):
        return set()
    text = io.open(CONDITION_SRC, encoding="utf-8", errors="replace").read()
    body = text.split("switch (conditionType)", 1)
    types = set(re.findall(r'case "([^"]+)"', body[1])) if len(body) > 1 else set()
    types |= set(re.findall(r'conditionType == "([^"]+)"', text))
    types.add("")
    return types


def config_of(scenario):
    path = os.path.join(CONF, scenario + ".asset")
    if not os.path.exists(path):
        return None
    text = io.open(path, encoding="utf-8", errors="replace").read()

    def one(field):
        # ★ \s*는 개행까지 먹는다. [ \t]*를 쓸 것.
        m = re.search(r"^  %s:[ \t]*(.*)$" % field, text, re.M)
        return unescape(m.group(1).strip()).strip('"') if m else ""

    def array(field):
        m = re.search(r"^  %s:[ \t]*\n((?:  - .*\n)*)" % field, text, re.M)
        if not m:
            return []
        return [unescape(x.strip()).strip('"') for x in re.findall(r"^  - (.*)$", m.group(1), re.M)]

    return dict(folder=one("narrationSubFolder") or scenario,
                education=array("educationPhases"),
                measurement=array("measurementPhases"),
                evaluation=array("evaluationPhases"))


def anim_clips():
    return {os.path.basename(p)[:-5] for p in glob.glob(os.path.join(ROOT, "Assets", "**", "*.anim"), recursive=True)}


def hand_recordings():
    out = set()
    for ext in ("*.json", "*.csv", "*.txt"):
        for p in glob.glob(os.path.join(ROOT, "Assets", "Resources", "**", ext), recursive=True):
            out.add(os.path.basename(p).rsplit(".", 1)[0])
    return out


def check(scenario, clips, hands, known):
    csv_path = os.path.join(SCEN, scenario + ".csv")
    rows = list(csv.DictReader(io.open(csv_path, encoding="utf-8-sig")))
    cfg = config_of(scenario)
    issues, notes = [], []

    if cfg is None:
        issues.append(("[없음]", "ScenarioConfig 에셋이 없다"))
        cfg = dict(folder=scenario, education=[], measurement=[], evaluation=[])

    # 1·2 나레이션
    # ★ LoadNarrationClip: 중급 이상은 {phase}_{voice}를 먼저 찾고 없으면 {voice}로 폴백한다.
    #   따라서 중급은 둘 중 하나만 있으면 소리가 난다. 초급은 평문만 본다.
    pairs = {((r.get("phase") or "").strip(), (r.get("voiceInstruction") or "").strip())
             for r in rows if (r.get("voiceInstruction") or "").strip()}
    voices = {v for _p, v in pairs}
    for level in ("Beginner", "Intermediate"):
        d = os.path.join(NARR, level, cfg["folder"])
        own = {os.path.basename(p)[:-4] for p in glob.glob(os.path.join(d, "*.mp3"))}
        # 2차·3차 폴백: Narrations/{난이도}/{clip}, Narrations/{clip} (현재 둘 다 0개지만 규칙상 존재)
        fallback = {os.path.basename(p)[:-4] for p in glob.glob(os.path.join(NARR, level, "*.mp3"))} | \
                   {os.path.basename(p)[:-4] for p in glob.glob(os.path.join(NARR, "*.mp3"))}
        have = own | fallback
        if level == "Beginner":
            miss = sorted(voices - have)
            used = voices & have
        else:
            miss = sorted({v for p, v in pairs if v not in have and f"{p}_{v}" not in have})
            used = {v for _p, v in pairs if v in have} | {f"{p}_{v}" for p, v in pairs if f"{p}_{v}" in have}
        if miss:
            issues.append(("[없음]", f"{level} 나레이션 {len(miss)}개 → 무음: {', '.join(miss)}"))
        extra = sorted(have - used)
        if extra:
            notes.append(f"{level}에 CSV가 안 쓰는 파일 {len(extra)}개: {', '.join(extra)}")
        if level == "Intermediate":
            # 접두 파일이 평문 파일을 가린다 — 문구를 새로 만들었는데 옛 접두 파일이 남아 있으면 옛 음성이 나온다
            shadow = sorted({f"{p}_{v}" for p, v in pairs if f"{p}_{v}" in have and v in have})
            if shadow:
                notes.append(f"중급 접두 파일이 평문 파일을 가림 {len(shadow)}개 "
                             f"(옛 음성이 재생될 수 있다): {', '.join(shadow)}")

    steps = {(r.get("stepName") or "").strip() for r in rows}
    steps.discard("")
    for level in FLAT_LEVELS:
        have = {os.path.basename(p)[:-4] for p in glob.glob(os.path.join(NARR, level, "*.mp3"))}
        miss = sorted(steps - have)
        if miss:
            notes.append(f"{level} stepName 클립 없음 {len(miss)}개 → 그 단계 무음: {', '.join(miss)}")

    # 3 PassiveStretch + voice
    # ★2026-08-26부터 conditionParams에 voiceGate가 있으면 <b>결함이 아니다</b>.
    #   ScenarioConditionManager에 '나레이션 먼저 → 접촉 게이팅 AutoPlay 대기' 분기를 넣었다.
    #   토큰이 없으면 종전대로 나레이션 경로로 빠져 게이트가 죽으므로 규칙은 그대로 둔다.
    bad = [f"{r['stepName']} {r['stepNo']}.{r['subStepNo']}" for r in rows
           if (r.get("conditionType") or "").strip() == "PassiveStretch"
           and (r.get("voiceInstruction") or "").strip()
           and "voicegate" not in (r.get("conditionParams") or "").lower()]
    if bad:
        issues.append(("[없음]", f"PassiveStretch + voiceInstruction 동시 {len(bad)}행 "
                                 f"→ 접촉 게이트가 죽는다(고치려면 conditionParams에 voiceGate): "
                                 f"{', '.join(bad)}"))

    gated = [f"{r['stepName']} {r['stepNo']}.{r['subStepNo']}" for r in rows
             if (r.get("conditionType") or "").strip() == "PassiveStretch"
             and (r.get("voiceInstruction") or "").strip()
             and "voicegate" in (r.get("conditionParams") or "").lower()]
    if gated:
        notes.append(f"PassiveStretch + voice를 voiceGate로 병합한 행 {len(gated)}개 "
                     f"(안내 중에도 게이트 유지): {', '.join(gated)}")

    # 4 모르는 conditionType
    if known:
        unknown = sorted({(r.get("conditionType") or "").strip() for r in rows} - known)
        if unknown:
            issues.append(("[없음]", f"코드가 모르는 conditionType (조용히 파지 조건이 된다): {', '.join(unknown)}"))

    # 5 애니 클립
    miss_clip = sorted({(r.get("patientAnimationClip") or "").strip() for r in rows} - clips - {""})
    if miss_clip:
        issues.append(("[없음]", f"없는 애니 클립: {', '.join(miss_clip)}"))

    # 6 손 녹화
    miss_hand = sorted({(r.get("handTrackingFileName") or "").strip() for r in rows} - hands - {""})
    if miss_hand:
        issues.append(("[없음]", f"없는 손 녹화 → 직전 녹화로 오판정: {', '.join(miss_hand)}"))

    # 7 phase 화이트리스트
    phases = {(r.get("phase") or "").strip() for r in rows}
    for key, label in (("education", "educationPhases"), ("measurement", "measurementPhases"),
                       ("evaluation", "evaluationPhases")):
        listed = cfg[key]
        if listed:
            ghost = [p for p in listed if p not in phases]
            if ghost:
                issues.append(("[없음]", f"{label}에 CSV에 없는 phase: {', '.join(ghost)}"))
            elif not (set(listed) & phases):
                issues.append(("[없음]", f"{label} 필터 결과가 0개가 된다"))
    return rows, issues, notes


def main():
    arg = sys.argv[1] if len(sys.argv) > 1 else "--all"
    names = ([os.path.basename(p)[:-4] for p in sorted(glob.glob(os.path.join(SCEN, "*.csv")))]
             if arg == "--all" else [arg])
    clips, hands, known = anim_clips(), hand_recordings(), known_condition_types()
    print(f"시나리오 {len(names)}개 · 알려진 conditionType {len(known)}종 · 애니 클립 {len(clips)}개\n")
    total = 0
    for n in names:
        rows, issues, notes = check(n, clips, hands, known)
        total += len(issues)
        head = f"{n}  ({len(rows)}행)"
        if not issues and not notes:
            print(f"[정상] {head}")
            continue
        print(f"── {head}")
        for tag, msg in issues:
            print(f"   {tag} {msg}")
        for msg in notes:
            print(f"   [주의] {msg}")
    print(f"\n결함 {total}건")


if __name__ == "__main__":
    main()
