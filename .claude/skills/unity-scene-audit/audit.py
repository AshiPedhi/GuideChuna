"""Unity 씬/프리팹 YAML 정적 조회 — 읽기 전용.

절대 쓰지 않는다. 씬 수정은 Unity 에디터에서만 한다.
(한글이 \\uXXXX로 이스케이프돼 있어 텍스트 치환하면 글자가 깨진다.)
"""
import glob
import io
import os
import re
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
DEFAULT_SCENE = os.path.join(ROOT, "Assets", "Scenes", "TrainingScene.unity")
ESCAPE_MARK = chr(92) + "u"          # ★ python re에서 \u 패턴은 쓸 수 없다


def unescape(s):
    if ESCAPE_MARK not in s:
        return s
    try:
        return s.encode().decode("unicode_escape")
    except Exception:
        return s


class Scene:
    def __init__(self, path):
        text = io.open(path, encoding="utf-8", errors="replace").read()
        self.objs, self.name, self.active, self.tr = {}, {}, {}, {}
        for block in re.split(r"\n--- ", text)[1:]:
            m = re.match(r"!u!(\d+) &(\d+)", block)
            if m:
                self.objs[m.group(2)] = (m.group(1), block)
        for fid, (cls, b) in self.objs.items():
            if cls == "1":
                n = re.search(r"^  m_Name: (.*)$", b, re.M)
                self.name[fid] = unescape(n.group(1).strip()) if n else "?"
                a = re.search(r"^  m_IsActive: (\d)", b, re.M)
                self.active[fid] = a.group(1) if a else "?"
            elif cls == "4":
                go = re.search(r"m_GameObject: \{fileID: (\d+)\}", b)
                fa = re.search(r"m_Father: \{fileID: (-?\d+)\}", b)
                lp = re.search(r"m_LocalPosition: \{x: ([-\d.eE+]+), y: ([-\d.eE+]+), z: ([-\d.eE+]+)\}", b)
                ls = re.search(r"m_LocalScale: \{x: ([-\d.eE+]+), y: ([-\d.eE+]+), z: ([-\d.eE+]+)\}", b)
                if go:
                    self.tr[fid] = dict(
                        go=go.group(1), fa=fa.group(1) if fa else "0",
                        lp=[float(x) for x in lp.groups()] if lp else [0.0, 0.0, 0.0],
                        ls=[float(x) for x in ls.groups()] if ls else [1.0, 1.0, 1.0])
        self.go2tr = {d["go"]: t for t, d in self.tr.items()}
        self.kids = {}
        for t, d in self.tr.items():
            self.kids.setdefault(d["fa"], []).append(t)

    def transform_of(self, fid):
        return fid if fid in self.tr else self.go2tr.get(fid)

    def chain(self, t):
        out, guard = [], 0
        while t and t in self.tr and guard < 80:
            out.append(t)
            if self.tr[t]["fa"] == "0":
                break
            t = self.tr[t]["fa"]
            guard += 1
        return out

    def path(self, t):
        return "/".join(reversed([self.name.get(self.tr[x]["go"], "?") for x in self.chain(t)]))

    def world_y(self, t):
        """회전 무시 근사값. 부모 스케일만 반영한다."""
        c, y = self.chain(t), 0.0
        for i, node in enumerate(c):
            v = self.tr[node]["lp"][1]
            for pa in c[i + 1:]:
                v *= self.tr[pa]["ls"][1]
            y += v
        return y

    def label(self, fid):
        t = self.transform_of(fid)
        if t:
            return f"{self.name.get(self.tr[t]['go'], '?')} (active={self.active.get(self.tr[t]['go'])})"
        cls, b = self.objs.get(fid, ("?", ""))
        return f"[class {cls}]"


def script_map():
    """스크립트 guid -> 파일 경로."""
    out = {}
    for pattern in ("Assets/Scripts/**/*.cs.meta", "Assets/_JDH/**/*.cs.meta"):
        for m in glob.glob(os.path.join(ROOT, pattern), recursive=True):
            head = io.open(m, encoding="utf-8", errors="replace").read(300)
            g = re.search(r"guid: (\w+)", head)
            if g:
                out[g.group(1)] = os.path.relpath(m[:-5], ROOT).replace("\\", "/")
    return out


def fields_of(block):
    body = block.split("m_EditorClassIdentifier:")[-1]
    return [l for l in body.splitlines()[1:] if l.strip()]


def main():
    args = sys.argv[1:]
    if not args:
        sys.exit(__doc__ + "\n명령: roots | find | tree | comp | script | ref")
    scene_arg = args[0]
    scene_path = DEFAULT_SCENE if scene_arg == "-" else (
        scene_arg if os.path.isabs(scene_arg) else os.path.join(ROOT, scene_arg))
    if not os.path.exists(scene_path):
        sys.exit(f"씬 없음: {scene_path}")
    cmd = args[1] if len(args) > 1 else "roots"
    rest = args[2:]
    s = Scene(scene_path)
    print(f"# {os.path.relpath(scene_path, ROOT)} · 오브젝트 {len(s.objs)}개\n")

    if cmd == "roots":
        for t, d in sorted(s.tr.items(), key=lambda kv: s.name.get(kv[1]["go"], "")):
            if d["fa"] == "0":
                print(f"  {s.name.get(d['go'], '?'):34} [{d['go']}] active={s.active.get(d['go'])} pos={d['lp']}")

    elif cmd == "find":
        pat = rest[0]
        for fid, n in sorted(s.name.items(), key=lambda kv: kv[1]):
            if re.search(pat, n, re.I):
                t = s.go2tr.get(fid)
                if t:
                    print(f"  {n:32} [{fid}] active={s.active.get(fid)} worldY≈{s.world_y(t):7.3f}  {s.path(t)}")

    elif cmd == "tree":
        t = s.transform_of(rest[0])
        depth = int(rest[1]) if len(rest) > 1 else 2
        if not t:
            sys.exit("transform을 못 찾았다 (프리팹 인스턴스면 stripped라 트리가 없다)")

        def rec(node, lvl):
            d = s.tr[node]
            print("  " * lvl + f"- {s.name.get(d['go'], '?')} [{d['go']}] active={s.active.get(d['go'])} lp={d['lp']}")
            if lvl < depth:
                for c in s.kids.get(node, []):
                    rec(c, lvl + 1)
        rec(t, 0)

    elif cmd == "comp":
        fid = rest[0]
        cls, b = s.objs.get(fid, (None, None))
        if cls != "1":
            sys.exit("GameObject가 아니다")
        smap = script_map()
        print(f"  {s.name.get(fid)}  ({s.path(s.transform_of(fid))})")
        for c in re.findall(r"- component: \{fileID: (\d+)\}", b):
            ccls, cb = s.objs.get(c, ("?", ""))
            if ccls == "114":
                g = re.search(r"m_Script: \{fileID: \d+, guid: (\w+)", cb)
                gu = g.group(1) if g else "?"
                print(f"    114 {smap.get(gu, 'guid:' + gu)}")
            else:
                print(f"    {ccls}")

    elif cmd == "script":
        key = rest[0]
        smap = script_map()
        guids = [g for g, p in smap.items() if key == g or os.path.basename(p)[:-3] == key]
        if not guids:
            sys.exit(f"스크립트를 못 찾았다: {key}")
        for fid, (cls, b) in s.objs.items():
            if cls != "114" or not any(g in b for g in guids):
                continue
            go = re.search(r"m_GameObject: \{fileID: (\d+)\}", b)
            gid = go.group(1) if go else None
            stripped = "stripped" in s.objs[fid][1][:80] or gid == "0"
            where = s.path(s.transform_of(gid)) if gid and s.transform_of(gid) else "(프리팹 인스턴스 — stripped)"
            print(f"  &{fid}  {where}{'  ※stripped' if stripped else ''}")
            for line in fields_of(b):
                print("   " + unescape(line))
            print()

    elif cmd == "ref":
        target = rest[0]
        smap = script_map()
        hits = 0
        for fid, (cls, b) in s.objs.items():
            if cls != "114" or f"fileID: {target}}}" not in b:
                continue
            g = re.search(r"m_Script: \{fileID: \d+, guid: (\w+)", b)
            gu = g.group(1) if g else "?"
            go = re.search(r"m_GameObject: \{fileID: (\d+)\}", b)
            gid = go.group(1) if go else None
            field = [l.strip() for l in b.splitlines() if f"fileID: {target}}}" in l]
            print(f"  {smap.get(gu, 'guid:' + gu)}  on {s.name.get(gid, '?')}")
            for f in field:
                print(f"      {unescape(f)}")
            hits += 1
        print(f"\n  참조 {hits}건")

    else:
        sys.exit(f"모르는 명령: {cmd}")


if __name__ == "__main__":
    main()
