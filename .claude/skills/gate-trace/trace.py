"""표시물이 안 뜰 때, 조건식을 분해해 <b>어느 항이 안 참인지</b> 정적으로 짚는다 — 읽기 전용.

2026-09-08에 게이트를 <b>덜 푼</b> 사고가 하루에 4건 났고, 그중 3건이 같은 파일 같은 조건식이었다.
`bool on = A && B && C`에서 A만 풀고 B·C를 안 봤다. 전부 정적으로 잡히는 종류다.
"""
import io
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
SCRIPTS = os.path.join(ROOT, "Assets", "Scripts")
SCENE_GLOBS = ("Assets/Scenes/TrainingScene.unity", "Assets/Scenes/lobby.unity")

# ── 소스 적재 ────────────────────────────────────────────────────────────
_sources = None


def sources():
    """{상대경로: 본문}. Assets/Scripts 전체를 한 번만 읽는다."""
    global _sources
    if _sources is None:
        _sources = {}
        for base, _, files in os.walk(SCRIPTS):
            for f in files:
                if not f.endswith(".cs"):
                    continue
                full = os.path.join(base, f)
                rel = os.path.relpath(full, ROOT).replace("\\", "/")
                _sources[rel] = io.open(full, encoding="utf-8", errors="replace").read()
    return _sources


_scene = None


def scene_text():
    global _scene
    if _scene is None:
        parts = []
        for p in SCENE_GLOBS:
            full = os.path.join(ROOT, p)
            if os.path.exists(full):
                parts.append(io.open(full, encoding="utf-8", errors="replace").read())
        _scene = "\n".join(parts)
    return _scene


def scene_value(field):
    """씬에 직렬화된 값. 없으면 None.

    ★같은 필드가 인스턴스마다 다를 수 있어 <b>발견된 값을 전부</b> 돌려준다.
    """
    vals = re.findall(r"^\s+" + re.escape(field) + r":[ \t]*(.+)$", scene_text(), re.M)
    return [v.strip() for v in vals] if vals else None


def line_of(text, idx):
    return text.count("\n", 0, idx) + 1


# ── 메서드 경계 ──────────────────────────────────────────────────────────
METHOD_RE = re.compile(
    r"^[ \t]*(?:\[[^\]]*\][ \t]*)*"
    r"(?:public|private|protected|internal|static|virtual|override|sealed|async|\s)*"
    r"[\w<>\[\],\.\?]+[ \t]+(\w+)[ \t]*\([^;{]*\)[ \t]*$", re.M)


def method_index(text):
    """[(줄번호, 이름)] — 선언 순서대로."""
    out = []
    for m in METHOD_RE.finditer(text):
        out.append((line_of(text, m.start()), m.group(1)))
    return out


def method_at(text, line, idx=None):
    idx = idx if idx is not None else method_index(text)
    name = "?"
    for ln, nm in idx:
        if ln <= line:
            name = nm
        else:
            break
    return name


def in_editor_only(text, idx):
    """그 위치가 #if UNITY_EDITOR 안인가. 규칙 10 — 빌드에서만 터지는 자리다."""
    depth = 0
    editor = False
    for m in re.finditer(r"^[ \t]*#(if|else|elif|endif)([^\n]*)$", text[:idx], re.M):
        kind, rest = m.group(1), m.group(2)
        if kind == "if":
            depth += 1
            if "UNITY_EDITOR" in rest:
                editor = True
        elif kind == "endif":
            depth -= 1
            if depth <= 0:
                editor = False
    return editor


# ── 식 분해 ──────────────────────────────────────────────────────────────
def split_top(expr, op):
    """괄호·문자열 밖의 op 로만 자른다."""
    parts, buf, depth, i = [], [], 0, 0
    quote = None
    while i < len(expr):
        c = expr[i]
        if quote:
            if c == "\\":
                buf.append(c)
                i += 1
                if i < len(expr):
                    buf.append(expr[i])
                    i += 1
                continue
            if c == quote:
                quote = None
            buf.append(c)
            i += 1
            continue
        if c in "\"'":
            quote = c
            buf.append(c)
            i += 1
            continue
        if c in "([":
            depth += 1
        elif c in ")]":
            depth -= 1
        if depth == 0 and expr.startswith(op, i):
            parts.append("".join(buf))
            buf = []
            i += len(op)
            continue
        buf.append(c)
        i += 1
    parts.append("".join(buf))
    return [p.strip() for p in parts if p.strip()]


def strip_parens(e):
    e = e.strip()
    while e.startswith("(") and e.endswith(")"):
        inner = e[1:-1]
        if split_top(inner, ")") and inner.count("(") == inner.count(")"):
            # 바깥 괄호가 실제로 짝인지 확인
            d = 0
            ok = True
            for i, c in enumerate(inner):
                if c == "(":
                    d += 1
                elif c == ")":
                    d -= 1
                if d < 0:
                    ok = False
                    break
            if ok and d == 0:
                e = inner.strip()
                continue
        break
    return e


def split_ternary(e):
    """A ? B : C 를 최상위에서만 자른다. 아니면 None."""
    depth = 0
    q = None
    qmark = -1
    i = 0
    while i < len(e):
        c = e[i]
        if q:
            if c == "\\":
                i += 2
                continue
            if c == q:
                q = None
            i += 1
            continue
        if c in "\"'":
            q = c
            i += 1
            continue
        if c in "([":
            depth += 1
        elif c in ")]":
            depth -= 1
        elif depth == 0 and c == "?" and not e.startswith("??", i) and qmark < 0:
            if i + 1 < len(e) and e[i + 1] == ".":
                i += 1
                continue
            qmark = i
        elif depth == 0 and c == ":" and qmark >= 0:
            if e.startswith("::", i):
                i += 2
                continue
            return e[:qmark].strip(), e[qmark + 1:i].strip(), e[i + 1:].strip()
        i += 1
    return None


IDENT = re.compile(r"^[A-Za-z_]\w*$")


def leaf_symbol(term):
    """항에서 추적할 이름을 뽑는다. (이름, 부정여부, 종류)"""
    t = strip_parens(term)
    neg = False
    while t.startswith("!"):
        neg = not neg
        t = strip_parens(t[1:])
    if IDENT.match(t):
        return t, neg, "flag"
    m = re.match(r"^(?:this\.)?([A-Za-z_]\w*)$", t)
    if m:
        return m.group(1), neg, "flag"
    m = re.match(r"^([A-Za-z_][\w\.]*)\s*\(", t)
    if m:
        return m.group(1).split(".")[-1], neg, "call"
    m = re.match(r"^[A-Za-z_][\w\.]*\.(\w+)$", t)
    if m:
        return m.group(1), neg, "member"
    return None, neg, "expr"


# ── 정의 찾기 ────────────────────────────────────────────────────────────
DEF_KINDS = ("local", "field", "property")


def find_definitions(name, prefer=None):
    """이름의 정의를 모은다: 지역 bool 대입 / 필드 선언 / 표현식 프로퍼티."""
    out = []
    for rel, text in sources().items():
        if prefer and prefer not in rel:
            continue
        if name not in text:
            continue
        idx = method_index(text)
        # bool x = <식>;  (지역변수 · 초기값 있는 필드 둘 다 걸린다)
        for m in re.finditer(
                r"^([ \t]*)(?:\[[^\]]*\][ \t]*)*(?:public|private|protected|internal|static|readonly|\s)*"
                r"bool[ \t]+" + re.escape(name) + r"[ \t]*=[ \t]*([^;]+);", text, re.M):
            expr = " ".join(m.group(2).split())
            ln = line_of(text, m.start())
            kind = "field" if re.search(r"\[SerializeField\]|public|private static", m.group(0)) and \
                len(m.group(1)) <= 4 and "(" not in expr[:0] and ln < (idx[0][0] if idx else 1 << 30) else "local"
            out.append(dict(kind=kind, file=rel, line=ln, expr=expr,
                            method=method_at(text, ln, idx),
                            serialized="[SerializeField]" in m.group(0) or
                                       re.search(r"\bpublic\b", m.group(0)) is not None))
        # bool X => <식>;
        for m in re.finditer(
                r"^[ \t]*(?:public|private|protected|internal|static|\s)*bool[ \t]+"
                + re.escape(name) + r"[ \t]*=>[ \t]*([^;]+);", text, re.M):
            out.append(dict(kind="property", file=rel, line=line_of(text, m.start()),
                            expr=" ".join(m.group(1).split()), method=name, serialized=False))
        # 선언만 (초기값 없음)
        for m in re.finditer(
                r"^[ \t]*((?:\[[^\]]*\][ \t]*)*)(?:public|private|protected|internal|static|\s)*bool[ \t]+"
                + re.escape(name) + r"[ \t]*;", text, re.M):
            out.append(dict(kind="decl", file=rel, line=line_of(text, m.start()), expr=None,
                            method=None, serialized="SerializeField" in m.group(1)))
    return out


ASSIGN_RE_T = r"(?<![=!<>+\-*/&|^])\b{0}\s*=(?!=)\s*([^;]+);"


def find_writes(name):
    """그 이름을 참으로/거짓으로 만드는 자리를 전부 모은다."""
    writes = []
    for rel, text in sources().items():
        if name not in text:
            continue
        idx = method_index(text)
        for m in re.finditer(ASSIGN_RE_T.format(re.escape(name)), text):
            val = " ".join(m.group(1).split())
            ln = line_of(text, m.start())
            # a = b = false; 같은 연쇄 대입에서 오른쪽 끝값을 본다
            tail = val
            while re.match(r"^[A-Za-z_]\w*\s*=(?!=)", tail):
                tail = tail.split("=", 1)[1].strip()
            head = text.rfind(chr(10), 0, m.start()) + 1
            decl = re.match(r"[ \t]*(?:\[[^\]]*\][ \t]*)*"
                            r"(?:public|private|protected|internal|static|readonly|const|\s)*"
                            r"bool[ \t]+" + re.escape(name) + r"[ \t]*=", text[head:m.end()])
            writes.append(dict(file=rel, line=ln, val=val, tail=tail,
                               method=method_at(text, ln, idx),
                               decl=bool(decl),
                               editor=in_editor_only(text, m.start())))
    return writes


def classify(writes):
    """선언 기본값은 <b>런타임에 켜는 자리가 아니다</b> — 씬 값이 이기므로 따로 센다(규칙 7)."""
    run = [w for w in writes if not w.get("decl")]
    dcl = [w for w in writes if w.get("decl")]
    t = [w for w in run if w["tail"] == "true"]
    f = [w for w in run if w["tail"] == "false"]
    x = [w for w in run if w["tail"] not in ("true", "false")]
    return t, f, x, dcl


# ── 출력 ────────────────────────────────────────────────────────────────
BAR = "─" * 74


def report_leaf(name, neg, kind, depth, out, seen):
    pad = "   " * depth
    mark = "!" if neg else " "
    if kind == "call":
        defs = [d for d in find_definitions(name)]
        out.append(f"{pad}{mark}{name}()  — 호출. 메서드 본문을 직접 봐야 한다")
        return
    if name in seen:
        out.append(f"{pad}{mark}{name}  (위에서 이미 봤다)")
        return
    seen.add(name)

    writes = find_writes(name)
    t, f, o, dcl = classify(writes)
    sv = scene_value(name)
    defs = find_definitions(name)
    serialized = any(d.get("serialized") for d in defs)

    flags = []
    if sv is not None:
        uniq = sorted(set(sv))
        flags.append("씬값 " + ",".join(uniq[:4]) + ("…" if len(uniq) > 4 else ""))
    # ★선언 기본값·씬 값이 있으면 "켜는 코드가 없다"가 아니라 <b>처음부터 켜져 있다</b>는 뜻이다.
    if not t and not o and not dcl and sv is None:
        flags.append("★참으로 만드는 자리 없음")
    if t and all(w["editor"] for w in t):
        flags.append("★참이 되는 자리가 전부 #if UNITY_EDITOR (규칙 10)")
    if neg:
        flags.insert(0, "★<b>거짓</b>이어야 통과한다")
        if not t and not o:
            flags = [x for x in flags if "참으로 만드는 자리 없음" not in x]
            flags.append("참으로 만드는 자리가 없다 = 이 항은 안 막는다")
    suffix = ("   [" + " · ".join(flags) + "]") if flags else ""
    out.append(f"{pad}{mark}{name}{suffix}")

    for w in dcl:
        out.append(f"{pad}    ·선언 기본값 = {w['tail']}   "
                   f"{os.path.basename(w['file'])}:{w['line']}"
                   + ("   ★씬 값이 이긴다" if sv is not None else ""))
    for w in t:
        ed = " #if UNITY_EDITOR" if w["editor"] else ""
        out.append(f"{pad}    ↑참  {os.path.basename(w['file'])}:{w['line']}  {w['method']}(){ed}")
    for w in o:
        out.append(f"{pad}    ↑식  {os.path.basename(w['file'])}:{w['line']}  {w['method']}()  = {w['tail'][:56]}")
    if f:
        where = ", ".join(sorted({w["method"] + "()" for w in f}))[:100]
        out.append(f"{pad}    ↓거짓 {len(f)}곳  {where}")
    if sv is not None and serialized:
        out.append(f"{pad}    ★씬에 굳어 있다 — 코드 기본값이 안 먹는다(규칙 7). 인스펙터에서 바꿔야 한다.")

    # 씬 값이 전부 1이면 "이미 켜져 있다"로 본다 — 막는 항 후보에서 뺀다.
    scene_on = sv is not None and all(v.strip() in ("1", "true") for v in sv)
    return dict(name=name, neg=neg, true_sites=len(t), expr_sites=len(o),
                false_sites=len(f), scene=sv, serialized=serialized,
                scene_on=scene_on, decl_default=(dcl[0]["tail"] if dcl else None),
                editor_only=bool(t) and all(w["editor"] for w in t))


def walk(expr, depth, out, seen, leaves, max_depth, prefer):
    expr = strip_parens(expr)
    tern = split_ternary(expr)
    if tern:
        cond, a, b = tern
        pad = "   " * depth
        # ★조건이 씬에 굳은 플래그면 <b>어느 가지가 사는지</b>가 이미 정해져 있다.
        cname, cneg, ckind = leaf_symbol(cond)
        live = ""
        if cname and ckind == "flag":
            cv = scene_value(cname)
            if cv and all(v.strip() in ("1", "true") for v in cv):
                live = "   ★씬값 1 → " + ("<거짓일 때>" if cneg else "<참일 때>") + " 가지만 산다"
            elif cv and all(v.strip() in ("0", "false") for v in cv):
                live = "   ★씬값 0 → " + ("<참일 때>" if cneg else "<거짓일 때>") + " 가지만 산다"
        out.append(f"{pad}(조건분기) {cond[:60]}{live}")
        out.append(f"{pad}  ├ 참일 때 →")
        walk(a, depth + 2, out, seen, leaves, max_depth, prefer)
        out.append(f"{pad}  └ 거짓일 때 →")
        walk(b, depth + 2, out, seen, leaves, max_depth, prefer)
        walk(cond, depth + 1, out, seen, leaves, max_depth, prefer)
        return

    ands = split_top(expr, "&&")
    if len(ands) > 1:
        for a in ands:
            walk(a, depth, out, seen, leaves, max_depth, prefer)
        return

    ors = split_top(expr, "||")
    if len(ors) > 1:
        pad = "   " * depth
        out.append(f"{pad}(느슨) 아래 중 하나만 참이면 된다 — 막는 항이 아니다")
        for a in ors:
            walk(a, depth + 1, out, seen, leaves, max_depth, prefer)
        return

    name, neg, kind = leaf_symbol(expr)
    if name is None:
        out.append("   " * depth + " " + expr[:70] + "   (식 — 사람이 읽어야 한다)")
        return

    # 중간 bool 이면 한 겹 더 편다
    if depth < max_depth and kind == "flag":
        defs = [d for d in find_definitions(name, prefer)
                if d.get("expr") and d["kind"] in ("local", "property")
                and d["expr"] not in ("true", "false")
                and re.search(r"&&|\|\||\?", d["expr"])]
        if defs:
            d = defs[0]
            pad = "   " * depth
            mark = "!" if neg else " "
            out.append(f"{pad}{mark}{name}  ={d['expr'][:64]}"
                       f"   ({os.path.basename(d['file'])}:{d['line']})")
            walk(d["expr"], depth + 1, out, seen, leaves, max_depth, prefer)
            return

    info = report_leaf(name, neg, kind, depth, out, seen)
    if info:
        leaves.append(info)


def verdict(leaves, out):
    if not leaves:
        return
    out.append("")
    out.append("판정 — ★<b>추정</b>이다. 시간 순서가 아니라 '참이 되기 어려운 순'이다.")

    # ① 영영 안 열리는 항 — 참으로 만드는 코드도, 기본값도, 씬 값도 없다
    for l in leaves:
        if l["neg"] or l["true_sites"] or l["expr_sites"]:
            continue
        if l["decl_default"] == "true" or l["scene_on"]:
            continue
        out.append(f"  [위험] {l['name']} — 참으로 만드는 코드가 <b>어디에도 없다</b>. 이 항은 영영 안 열린다.")

    # ② 규칙 10 — 빌드에서만 안 켜진다
    for l in leaves:
        if l["editor_only"]:
            out.append(f"  [위험] {l['name']} — 참이 되는 자리가 전부 에디터 전용이다. "
                       f"빌드에서만 안 켜진다(규칙 10).")

    # ③ 씬에 굳은 것 — 코드로 못 끈다. 이미 켜져 있으니 <b>막는 항은 아니다</b>.
    frozen = [l for l in leaves if l["serialized"] and l["scene"]]
    if frozen:
        out.append("")
        out.append("  ── 씬에 굳어 있다(규칙 7) — 코드 기본값이 안 먹는다. 끄려면 인스펙터다.")
        for l in frozen:
            uniq = ",".join(sorted(set(l["scene"]))[:4])
            state = "이미 켜져 있다 = 이 항은 안 막는다" if l["scene_on"] else "★이 값이 막고 있을 수 있다"
            out.append(f"     {l['name']} = {uniq}   ({state})")

    # ④ 런타임에 켜져야 하는 항 — 진짜 후보는 여기다
    live = [l for l in leaves
            if not l["neg"] and not l["scene_on"] and (l["true_sites"] or l["expr_sites"])]
    live.sort(key=lambda l: (l["true_sites"] + l["expr_sites"], -l["false_sites"]))
    if live:
        out.append("")
        out.append("  ── 런타임에 켜져야 하는 항 (켜는 자리가 적을수록 먼저 의심한다)")
        for l in live:
            how = f"켜는 자리 {l['true_sites']}곳"
            if l["expr_sites"]:
                how += f" + 값 대입 {l['expr_sites']}곳"
            out.append(f"     {l['name']:<20} {how} / 끄는 자리 {l['false_sites']}곳")
        out.append(f"  → ★<b>{live[0]['name']}</b>부터 로그를 심는다.")

    out.append("")
    out.append("  ★<b>여기까지는 정적이다.</b> 실제로 어느 것이 마지막까지 거짓인지는 "
               "`log-first`로 로그를 심어 확인한다.")


def scan_file(rel, out, min_terms):
    text = sources().get(rel)
    if text is None:
        out.append(f"그런 파일이 없다: {rel}")
        return
    idx = method_index(text)
    found = 0
    for m in re.finditer(r"^[ \t]*bool[ \t]+(\w+)[ \t]*=[ \t]*([^;]+);", text, re.M):
        expr = " ".join(m.group(2).split())
        terms = split_top(expr, "&&")
        if len(terms) < min_terms:
            continue
        ln = line_of(text, m.start())
        found += 1
        out.append(f"  {m.group(1):<24} {len(terms)}항  {os.path.basename(rel)}:{ln}"
                   f"  {method_at(text, ln, idx)}()")
    if not found:
        out.append(f"  {min_terms}항 이상인 조건식이 없다.")


def main():
    args = [a for a in sys.argv[1:]]
    prefer = None
    max_depth = 3
    min_terms = 3
    scan = None
    sym = None
    for a in args:
        if a.startswith("--file="):
            prefer = a.split("=", 1)[1]
        elif a.startswith("--depth="):
            max_depth = int(a.split("=", 1)[1])
        elif a.startswith("--min="):
            min_terms = int(a.split("=", 1)[1])
        elif a.startswith("--scan="):
            scan = a.split("=", 1)[1]
        elif not a.startswith("-"):
            sym = a

    out = []
    if scan:
        rel = scan
        if not rel.startswith("Assets"):
            cands = [r for r in sources() if r.endswith("/" + rel) or os.path.basename(r) == rel]
            if not cands:
                print(f"그런 파일이 없다: {rel}")
                return
            rel = cands[0]
        out.append(BAR)
        out.append(f"게이트 후보 — {rel}  ({min_terms}항 이상)")
        out.append(BAR)
        scan_file(rel, out, min_terms)
        print("\n".join(out))
        return

    if not sym:
        print("쓰는 법: trace.py <표시플래그> [--file=이름조각] [--depth=3]")
        print("         trace.py --scan=<파일.cs> [--min=3]      # 그 파일의 게이트 후보 목록")
        return

    defs = find_definitions(sym, prefer)
    gates = [d for d in defs if d.get("expr") and d["expr"] not in ("true", "false")]
    out.append(BAR)
    out.append(f"게이트 추적 — {sym}")
    out.append(BAR)

    if not gates:
        out.append("조건식으로 된 정의를 못 찾았다. 이 이름은 그냥 플래그일 수 있다.")
        out.append("")
        seen, leaves = set(), []
        info = report_leaf(sym, False, "flag", 0, out, seen)
        if info:
            leaves.append(info)
        verdict(leaves, out)
        print("\n".join(out))
        return

    for d in gates:
        out.append("")
        out.append(f"{os.path.basename(d['file'])}:{d['line']}  {d['method']}()")
        out.append(f"  {sym} = {d['expr']}")
        out.append("")
        terms = split_top(d["expr"], "&&")
        out.append(f"  ★모두 참이어야 하는 항이 {len(terms)}개다. 하나만 풀면 안 뜬다.")
        out.append("")
        seen, leaves = set(), []
        walk(d["expr"], 1, out, seen, leaves, max_depth, prefer)
        verdict(leaves, out)

    print("\n".join(out))


if __name__ == "__main__":
    main()
