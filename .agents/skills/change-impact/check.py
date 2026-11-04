"""바꾼 것이 어디까지 번지는지 정적으로 잰다 — 읽기 전용. 아무 파일도 안 고친다.

2026-09-03 신설. 이 스킬이 잡는 네 가지는 전부 <b>그날 실제로 물린 것</b>이다.
전부 "에러 없이 조용히 틀리는" 종류라 Play로만 잡히던 것들이다.
"""
import io
import os
import re
import subprocess
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
SCRIPTS = os.path.join(ROOT, "Assets", "Scripts")
SCEN_DIR = os.path.join(ROOT, "Assets", "Resources", "Scenarios")
NARR_DIR = os.path.join(ROOT, "Assets", "Resources", "Narrations")
ESCAPE_MARK = chr(92) + "u"

# 씬·프리팹을 다 읽으면 느리다. 실제로 쓰는 것만 본다.
SCENE_GLOBS = ("Assets/Scenes/TrainingScene.unity", "Assets/Scenes/lobby.unity")


def unescape(s):
    if ESCAPE_MARK not in s:
        return s
    try:
        return s.encode().decode("unicode_escape")
    except Exception:
        return s


def run(args):
    try:
        p = subprocess.run(args, cwd=ROOT, capture_output=True, text=True,
                           encoding="utf-8", errors="replace")
        return p.stdout or ""
    except Exception:
        return ""


def git(*args):
    return run(["git", "-c", "core.quotepath=off", *args])


_scene_cache = {}


def scene_text(path):
    if path not in _scene_cache:
        full = os.path.join(ROOT, path)
        if not os.path.exists(full):
            _scene_cache[path] = ""
        else:
            _scene_cache[path] = io.open(full, encoding="utf-8", errors="replace").read()
    return _scene_cache[path]


def all_scene_text():
    return "\n".join(scene_text(p) for p in SCENE_GLOBS)


def guid_of(cs_path):
    meta = os.path.join(ROOT, cs_path + ".meta")
    if not os.path.exists(meta):
        return None
    m = re.search(r"guid: ([0-9a-f]+)", io.open(meta, encoding="utf-8", errors="replace").read())
    return m.group(1) if m else None


def changed_files(base):
    """base 대비 바뀐 파일. base가 없으면 워킹 트리 + 마지막 커밋을 본다."""
    if base:
        out = git("diff", "--name-only", base)
    else:
        out = git("diff", "--name-only", "HEAD") + git("diff", "--name-only", "--cached")
        if not out.strip():
            out = git("diff", "--name-only", "HEAD~1", "HEAD")
    seen, files = set(), []
    for line in out.splitlines():
        line = line.strip()
        if line and line not in seen:
            seen.add(line)
            files.append(line)
    return files


def diff_text(base, path):
    if base:
        return git("diff", base, "--", path)
    d = git("diff", "HEAD", "--", path)
    return d if d.strip() else git("diff", "HEAD~1", "HEAD", "--", path)


# ── ① 직렬화 충돌 ────────────────────────────────────────────────────────
# 2026-09-03: routeReadoutToGuideUI를 기본값 true로 넣었더니 그날 씬이 저장되며 1로 굳었다.
# 그 뒤 코드 기본값을 false로 되돌려도 씬 값이 이겨서(규칙 7) 화면이 통째로 비었다.
def check_serialized(base, cs_files, out):
    scene = all_scene_text()
    hits = []
    for path in cs_files:
        d = diff_text(base, path)
        if not d:
            continue
        # 이번 diff에서 <b>추가되거나 바뀐</b> SerializeField 필드 이름만 본다
        fields = set()
        with_default = set()
        for line in d.splitlines():
            if not line.startswith("+") or line.startswith("+++"):
                continue
            md = re.search(r"\s(\w+)\s*=\s*[^=]", line)
            if md:
                with_default.add(md.group(1))
            m = re.search(r"(?:\[SerializeField\][^;\n]*?|public\s+)"
                          r"(?:readonly\s+)?[\w<>\[\]\.]+\s+(\w+)\s*(?:=|;)", line)
            if m:
                fields.add(m.group(1))
            m2 = re.search(r"\[SerializeField\]\s*private\s+[\w<>\[\]\.]+\s+(\w+)", line)
            if m2:
                fields.add(m2.group(1))
        # ★<b>초기값이 붙은 필드</b>만 본다. 그게 "코드 기본값이 안 먹는" 실제 사고 형태다.
        #   초기값 없는 슬롯(참조 배선용)은 원래 인스펙터가 채우는 것이라 경고가 아니다.
        for f in sorted(fields):
            if len(f) < 4 or f not in with_default:
                continue
            if re.search(r"^\s+" + re.escape(f) + r":", scene, re.M):
                hits.append((path, f))
    if hits:
        out.append("[위험] ★씬에 이미 직렬화된 필드를 건드렸다 — <b>코드 기본값이 안 먹는다</b>(규칙 7)")
        for path, f in hits:
            out.append(f"        {f}   ({os.path.basename(path)})")
        out.append("        → 인스펙터에서 직접 바꾸거나, <b>신규 이름의 오버라이드 필드</b>를 판다.")
        out.append("        → 되돌릴 여지가 있는 스위치는 기본값을 false로 두거나 런타임 플래그로 둔다.")
    return len(hits)


# ── ② 파급 범위 ──────────────────────────────────────────────────────────
# 공유 컴포넌트를 고치면 13개 술기가 같이 흔들린다. 씬 인스턴스 수와 참조 파일로 잰다.
def check_blast(cs_files, out):
    risky = 0
    for path in cs_files:
        name = os.path.splitext(os.path.basename(path))[0]
        g = guid_of(path)
        scene = all_scene_text()
        inst = len(re.findall(r"guid: " + g, scene)) if g else 0

        refs = []
        for dirpath, _, names in os.walk(SCRIPTS):
            for n in names:
                if not n.endswith(".cs") or n == os.path.basename(path):
                    continue
                full = os.path.join(dirpath, n)
                try:
                    body = io.open(full, encoding="utf-8", errors="replace").read()
                except Exception:
                    continue
                if re.search(r"\b" + re.escape(name) + r"\b", body):
                    refs.append(os.path.relpath(full, ROOT).replace("\\", "/"))

        runtime_added = bool(re.search(r"AddComponent<" + re.escape(name) + r">", all_sources()))

        tag = ""
        if inst == 0 and not runtime_added and refs:
            tag = "  ★씬 인스턴스 0개인데 참조는 있다 — 죽은 배선일 수 있다"
            risky += 1
        elif inst == 0 and runtime_added:
            tag = "  (런타임 AddComponent — 인스펙터엔 Play 중에만 보인다)"
        elif len(refs) >= 5:
            tag = "  ★공유 컴포넌트 — 다른 술기까지 번진다"
            risky += 1

        out.append(f"  {name}: 씬 인스턴스 {inst}개 · 참조 {len(refs)}개{tag}")
        if len(refs) >= 5:
            out.append("        " + ", ".join(os.path.basename(r) for r in refs[:8])
                       + (" …" if len(refs) > 8 else ""))
    return risky


_src_cache = {}


def all_sources():
    if "all" not in _src_cache:
        buf = []
        for dirpath, _, names in os.walk(SCRIPTS):
            for n in names:
                if n.endswith(".cs"):
                    try:
                        buf.append(io.open(os.path.join(dirpath, n),
                                           encoding="utf-8", errors="replace").read())
                    except Exception:
                        pass
        _src_cache["all"] = "\n".join(buf)
    return _src_cache["all"]


# ── ③ 이벤트 수신자 부재 ─────────────────────────────────────────────────
# 2026-09-03: RequestButtonStateUpdate를 보내는데 받는 ScenarioUIController가 씬에 0개였다.
# 보내는 쪽만 보고 "구현돼 있다"고 단정해서 틀렸다.
def check_events(out):
    src = all_sources()
    scene = all_scene_text()
    problems = 0

    # 구독자: `eventSystem.OnX += Handler` 형태에서 그 파일의 클래스가 씬에 있는지 본다
    subs = {}   # 이벤트명 -> [(클래스, 씬인스턴스수)]
    for dirpath, _, names in os.walk(SCRIPTS):
        for n in names:
            if not n.endswith(".cs"):
                continue
            full = os.path.join(dirpath, n)
            rel = os.path.relpath(full, ROOT).replace("\\", "/")
            try:
                body = io.open(full, encoding="utf-8", errors="replace").read()
            except Exception:
                continue
            found = re.findall(r"\.(On\w+)\s*\+=", body)
            if not found:
                continue
            cls = os.path.splitext(n)[0]
            g = guid_of(rel)
            inst = len(re.findall(r"guid: " + g, scene)) if g else 0
            runtime = bool(re.search(r"AddComponent<" + re.escape(cls) + r">", src))
            for ev in set(found):
                subs.setdefault(ev, []).append((cls, inst, runtime))

    for ev, lst in sorted(subs.items()):
        alive = [c for c in lst if c[1] > 0 or c[2]]
        if alive:
            continue
        problems += 1
        who = ", ".join(f"{c}(씬 {i}개)" for c, i, _ in lst)
        out.append(f"[위험] ★이벤트 '{ev}' 구독자가 <b>씬에 하나도 없다</b> → 보내도 아무 일이 안 일어난다")
        out.append(f"        구독 코드: {who}")
    return problems


# ── ④ 나레이션 낡음 ──────────────────────────────────────────────────────
# 2026-09-03: 텍스트만 바꾸고 narration-gen을 안 돌려 31개가 낡아 있었다.
_mp_time_cache = {}


def mp_commit_time(rel):
    """그 mp3가 마지막으로 커밋된 시각. 미커밋이면 None."""
    if rel not in _mp_time_cache:
        s = git("log", "-1", "--format=%at", "--", rel).strip()
        _mp_time_cache[rel] = int(s) if s.isdigit() else None
    return _mp_time_cache[rel]


def check_narration(out):
    import csv as _csv
    stale = 0
    blame_cache = {}
    for path in sorted(os.listdir(SCEN_DIR)) if os.path.isdir(SCEN_DIR) else []:
        if not path.endswith(".csv"):
            continue
        rel = f"Assets/Resources/Scenarios/{path}"
        scen = os.path.splitext(path)[0]
        full = os.path.join(SCEN_DIR, path)

        if rel not in blame_cache:
            out_txt = git("blame", "--line-porcelain", rel)
            dates, ln, cur = {}, 0, None
            for L in out_txt.splitlines():
                if L.startswith("author-time"):
                    cur = int(L.split()[1])
                elif L.startswith("\t"):
                    ln += 1
                    dates[ln] = cur
            blame_cache[rel] = dates
        dates = blame_cache[rel]

        lines = io.open(full, encoding="utf-8", errors="replace").read().splitlines()
        seen = set()
        bad = []
        for i, L in enumerate(lines[1:], start=2):
            try:
                parts = next(_csv.reader([L]))
            except Exception:
                continue
            if len(parts) < 9:
                continue
            v = parts[8].strip()
            if not v or v in seen:
                continue
            seen.add(v)
            mp = os.path.join(NARR_DIR, "Beginner", scen, v + ".mp3")
            if not os.path.exists(mp):
                continue
            # ★<b>둘 다 git 시각으로</b> 잰다. blame은 커밋 시각인데 mp3를 파일 mtime으로 재면,
            #   "생성 → 커밋" 순서 때문에 방금 만든 클립도 낡은 것으로 잡힌다(2026-09-03에 밟았다).
            mp_rel = os.path.relpath(mp, ROOT).replace("\\", "/")
            mp_t = mp_commit_time(mp_rel)
            if mp_t is None:
                continue          # 아직 커밋 안 된 새 파일 — 판단하지 않는다
            if dates.get(i, 0) > mp_t + 60:
                bad.append(v)
        if bad:
            stale += len(bad)
            out.append(f"[확인] {scen}: 텍스트가 mp3보다 새로운 클립 {len(bad)}개 — <b>안내문과 음성이 어긋난다</b>")
            out.append("        " + ", ".join(bad[:12]) + (" …" if len(bad) > 12 else ""))
            out.append(f"        → narration-gen 으로 재녹음: --only \"{','.join(bad[:6])}...\" --force --write")
    return stale


def main():
    base = None
    args = [a for a in sys.argv[1:] if a]
    only = set()
    for a in args:
        if a.startswith("--only="):
            only = set(a.split("=", 1)[1].split(","))
        elif not a.startswith("--"):
            base = a

    files = changed_files(base)
    cs = [f for f in files if f.endswith(".cs") and f.startswith("Assets/")]

    print(f"바뀐 파일 {len(files)}개 (.cs {len(cs)}개)"
          + (f" · 기준 {base}" if base else " · 기준 워킹트리/마지막 커밋"))
    for f in files[:20]:
        print("   ", f)
    if len(files) > 20:
        print(f"    … 외 {len(files) - 20}개")
    print()

    total = 0
    def want(k):
        return not only or k in only

    if cs and want("serialized"):
        out = []
        total += check_serialized(base, cs, out)
        print("── ① 직렬화 충돌 (규칙 7) ──")
        print("\n".join(out) if out else "  없음")
        print()

    if cs and want("blast"):
        out = []
        total += check_blast(cs, out)
        print("── ② 파급 범위 ──")
        print("\n".join(out) if out else "  없음")
        print()

    if want("events"):
        out = []
        total += check_events(out)
        print("── ③ 이벤트 수신자 ──")
        print("\n".join(out) if out else "  없음")
        print()

    if want("narration"):
        out = []
        total += check_narration(out)
        print("── ④ 나레이션 낡음 ──")
        print("\n".join(out) if out else "  없음")
        print()

    print(f"위험 {total}건")
    return 0


if __name__ == "__main__":
    sys.exit(main())
