# -*- coding: utf-8 -*-
"""씬 YAML에서 본 체인을 곱해 월드 TRS를 낸다.
audit.py의 월드좌표는 회전을 무시한 근사라 본 체인에서는 못 쓴다 — 여기서는 제대로 곱한다.
읽기 전용. 씬에 쓰지 않는다.
"""
import re, sys, math

SCENE = "Assets/Scenes/TrainingScene.unity"
txt = open(SCENE, encoding="utf-8").read()

# 유니코드 이스케이프 마커 (파이썬 소스에 직접 못 쓴다 — CLAUDE.md 텍스트도구 함정)
U = chr(92) + "u"

def unesc(s):
    out, i = [], 0
    while i < len(s):
        if s[i:i+2] == U and i + 6 <= len(s):
            try:
                out.append(chr(int(s[i+2:i+6], 16))); i += 6; continue
            except ValueError:
                pass
        out.append(s[i]); i += 1
    return "".join(out)

# --- 블록 단위로 쪼갠다 ---
blocks = {}   # fileID -> (classline, body)
for m in re.finditer(r"--- !u!(\d+) &(\d+)[^\n]*\n(.*?)(?=\n--- !u!|\Z)", txt, re.S):
    blocks[m.group(2)] = (m.group(1), m.group(3))

transforms = {}   # transform fileID -> dict
gonames = {}      # gameobject fileID -> name
for fid, (cls, body) in blocks.items():
    if cls == "1":
        mm = re.search(r"^[ \t]*m_Name:[ \t]*(.*)$", body, re.M)
        if mm: gonames[fid] = unesc(mm.group(1).strip().strip('"'))
    elif cls == "4":
        def vec(key, d):
            mm = re.search(r"^[ \t]*" + key + r":[ \t]*\{x:[ \t]*([-\d.eE+]+),[ \t]*y:[ \t]*([-\d.eE+]+),[ \t]*z:[ \t]*([-\d.eE+]+)(?:,[ \t]*w:[ \t]*([-\d.eE+]+))?\}", body, re.M)
            if not mm: return d
            v = [float(mm.group(i)) for i in (1,2,3)]
            if mm.group(4) is not None: v.append(float(mm.group(4)))
            return v
        go = re.search(r"^[ \t]*m_GameObject:[ \t]*\{fileID:[ \t]*(\d+)\}", body, re.M)
        fa = re.search(r"^[ \t]*m_Father:[ \t]*\{fileID:[ \t]*(\d+)\}", body, re.M)
        transforms[fid] = dict(
            go=go.group(1) if go else "0",
            father=fa.group(1) if fa else "0",
            pos=vec("m_LocalPosition", [0,0,0]),
            rot=vec("m_LocalRotation", [0,0,0,1]),
            scl=vec("m_LocalScale", [1,1,1]),
        )

byname = {}
for tid, t in transforms.items():
    n = gonames.get(t["go"])
    if n: byname.setdefault(n, []).append(tid)

# --- 쿼터니언/행렬 ---
def qmul(a, b):
    ax,ay,az,aw = a; bx,by,bz,bw = b
    return [aw*bx+ax*bw+ay*bz-az*by,
            aw*by-ax*bz+ay*bw+az*bx,
            aw*bz+ax*by-ay*bx+az*bw,
            aw*bw-ax*bx-ay*by-az*bz]

def qrot(q, v):
    x,y,z,w = q; vx,vy,vz = v
    tx = 2*(y*vz - z*vy); ty = 2*(z*vx - x*vz); tz = 2*(x*vy - y*vx)
    return [vx + w*tx + (y*tz - z*ty),
            vy + w*ty + (z*tx - x*tz),
            vz + w*tz + (x*ty - y*tx)]

def chain(tid):
    ch = []
    seen = set()
    while tid and tid != "0" and tid in transforms and tid not in seen:
        seen.add(tid); ch.append(tid); tid = transforms[tid]["father"]
    return list(reversed(ch))   # 루트 → 대상

def world(tid):
    q = [0,0,0,1]; p = [0,0,0]; s = [1,1,1]
    for t in chain(tid):
        tr = transforms[t]
        lp = [tr["pos"][i]*s[i] for i in range(3)]
        rp = qrot(q, lp)
        p = [p[i]+rp[i] for i in range(3)]
        q = qmul(q, tr["rot"])
        s = [s[i]*tr["scl"][i] for i in range(3)]
    return p, q, s

def axes(q):
    return dict(right=qrot(q,[1,0,0]), up=qrot(q,[0,1,0]), fwd=qrot(q,[0,0,1]))

def dot(a,b): return sum(a[i]*b[i] for i in range(3))
def sub(a,b): return [a[i]-b[i] for i in range(3)]
def norm(a):
    L = math.sqrt(dot(a,a)); return [x/L for x in a] if L > 1e-9 else a
def fmt(v): return "(%.3f, %.3f, %.3f)" % tuple(v[:3])

if __name__ == "__main__":
    names = sys.argv[1:] if len(sys.argv) > 1 else []
    if not names:
        print("이름 인자를 줘. 예: bone_axes.py CC_Base_Spine02 CC_Base_Head")
        sys.exit(0)
    for n in names:
        ids = byname.get(n, [])
        if not ids:
            print(f"{n}: 씬에 없다"); continue
        for tid in ids:
            p, q, s = world(tid)
            path = "/".join(gonames.get(transforms[t]["go"], "?") for t in chain(tid))
            a = axes(q)
            print(f"\n■ {n}  [tid {tid}]")
            print(f"   경로 {path}")
            print(f"   월드위치 {fmt(p)}   누적스케일 {fmt(s)}")
            print(f"   right {fmt(norm(a['right']))}   up {fmt(norm(a['up']))}   forward {fmt(norm(a['fwd']))}")
