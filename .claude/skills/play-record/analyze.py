# -*- coding: utf-8 -*-
"""
플레이 영상 ↔ Editor.log 대조 분석 (2026-11-03 사용자 지시 «영상 분석을 촘촘히 · 로그와 맞춰서»).

하는 일
  1. 로그 사건(단계 시작·완료 · 접촉 바뀜 · 애니 건너뜀 · 진행률 되돌아감 · 리모컨 · 예외)을 영상 시각에 붙인다.
  2. 평소 구간은 성기게(기본 5초), 사건 앞뒤는 촘촘히(기본 초당 4장) 프레임을 뽑는다.
  3. 장마다 «영상 시각 · 단계 · 안내 문구 · 진행률 · 양손 접촉 · 그 순간 사건» 이름표를 붙여 판으로 묶는다.
  4. 사건 시간표(timeline.md)를 쓴다. ★에이전트는 이것과 판을 같이 읽는다.

시각 맞추기
  · 녹화기(record.py)가 녹화 중 확인할 때마다 Editor.log 크기를 적은 «<영상>.logmap.json»이 있으면 그것으로 맞춘다(실측).
  · 없으면(11-03 이전 녹화) 마지막 «Reloading assemblies for play mode» ~ 첫 «OnApplicationQuit»를
    영상 0초 ~ 끝에 바이트 비례로 편다. ★로그가 쏟아지는 구간에서 몇 초씩 어긋난다 — 시간표 머리에 «추정»이라 적는다.

쓰는 법
  PY=/c/Users/USER/AppData/Local/Python/bin/python.exe
  PYTHONIOENCODING=utf-8 "$PY" .claude/skills/play-record/analyze.py                 # 마지막 녹화
  PYTHONIOENCODING=utf-8 "$PY" .claude/skills/play-record/analyze.py D:/추나녹화/20261103/101852_플레이.mp4
  옵션: --sparse 5 --dense-fps 4 --pad 1.5 --max-frames 180 --from 2:00 --to 2:40 --no-frames
"""
import argparse
import bisect
import glob
import json
import os
import re
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from record import FFMPEG, DEFAULT_OUT, EDITOR_LOG, NOWIN  # noqa: E402

FFPROBE = os.path.join(os.path.dirname(FFMPEG), "ffprobe.exe")
FONT = r"C:\Windows\Fonts\malgun.ttf"
FONT_B = r"C:\Windows\Fonts\malgunbd.ttf"

TAG = re.compile(r"</?color[^>]*>")
R_STEP_NO = re.compile(r"^\[Event\] SubStep 시작: (\S+)")
R_STEP_DONE = re.compile(r"^\[Event\] SubStep 완료: (\S+)")
R_GUIDE = re.compile(r"^\[GuideVideo\] SubStep 시작: (.+?), 가이드 스텝:")
R_PHASE = re.compile(r"^\[TrainingResultTracker\] SubStep 시작: (.+)$")
R_SCORE = re.compile(r"^\[TrainingResultTracker\] SubStep 완료: (.+)$")
R_CONTACT = re.compile(r"^\[(왼손|오른손)\] (접촉|미접촉) 대상:(\S+)")
R_PROG = re.compile(r"^\[Progress\] (\d+) %")
R_SKIP = re.compile(r"^\[Animation Skip\]")
R_REMOTE = re.compile(r"^\[PositionRemote\] (리모컨 소환|환자 기준).*")
R_GUIDEHAND = re.compile(r"^\[GuideHand\] (.+)")
R_EXC = re.compile(r"^[A-Za-z.]*Exception: ")
R_SCEN = re.compile(r"^\[Event\] 시나리오 시작: (.+)")


def fmt(t):
    m = int(t // 60)
    return "%d:%04.1f" % (m, t - m * 60)


def parse_time(s):
    if s is None:
        return None
    if ":" in s:
        m, x = s.split(":", 1)
        return int(m) * 60 + float(x)
    return float(s)


def latest_video():
    vids = [p for p in glob.glob(os.path.join(DEFAULT_OUT, "*", "*.mp4")) if "_분석" not in p]
    return max(vids, key=os.path.getmtime) if vids else None


def duration(video):
    out = subprocess.run([FFPROBE, "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", video],
                         capture_output=True, text=True, creationflags=NOWIN).stdout.strip()
    return float(out)


def build_map(video, dur, raw):
    """(바이트 목록, 시각 목록, 추정 여부, 설명)"""
    lm = video + ".logmap.json"
    if os.path.exists(lm):
        s = json.load(open(lm, encoding="utf-8"))["samples"]
        s = [x for x in s if x[1] >= 0]
        return [x[1] for x in s], [x[0] for x in s], False, "로그 시각표(실측 · 확인 간격 ≈1.5초)"
    a = raw.rfind("Reloading assemblies for play mode.".encode())
    if a < 0:
        a = 0
    b = raw.find(b"OnApplicationQuit", a)
    if b < 0:
        b = len(raw)
    return [a, b], [0.0, dur], True, "★추정 — 시각표 없는 옛 녹화라 Play 구간 바이트를 영상 길이에 비례로 폈다(몇 초 어긋날 수 있다)"


def apply_anchors(raw, offs, ts, anchors, dur):
    """--anchor '로그 글귀@영상시각' — 화면에서 확인한 순간으로 시각을 맞춘다(옛 녹화용). 구간별 직선으로 잇는다."""
    if not anchors:
        return offs, ts
    seg0, seg1 = offs[0], offs[-1]
    pts = []
    for an in anchors:
        pat, _, tv = an.rpartition("@")
        i = raw.find(pat.encode("utf-8"), seg0, seg1)
        if i < 0:
            print("★기준점 글귀를 Play 구간에서 못 찾았다 — %s" % pat); continue
        pts.append((i, parse_time(tv)))
    if not pts:
        return offs, ts
    pts.sort()
    o = [seg0] + [p[0] for p in pts] + [seg1]
    t = [0.0] + [p[1] for p in pts] + [dur]     # Play 시작 = 0초 · 끝 = 영상 끝, 사이는 기준점끼리 직선
    return o, t


def to_time(off, offs, ts):
    if off <= offs[0]:
        return ts[0]
    if off >= offs[-1]:
        return ts[-1]
    i = bisect.bisect_right(offs, off)
    o0, o1, t0, t1 = offs[i - 1], offs[i], ts[i - 1], ts[i]
    return t0 if o1 == o0 else t0 + (t1 - t0) * (off - o0) / (o1 - o0)


def scan(raw, offs, ts):
    """사건 목록과 상태 변화 목록."""
    start, end = offs[0], offs[-1]
    events, states = [], []      # events: (t, 종류, 글, 중요)   states: (t, 키, 값)
    pos = start
    prog_max = 0
    last_prog = None
    seen = {}
    for bline in raw[start:end].split(b"\n"):
        off = pos
        pos += len(bline) + 1
        line = TAG.sub("", bline.decode("utf-8", "replace")).strip()
        if not line or line[0] not in "[NA":
            if not R_EXC.match(line):
                continue
        t = to_time(off, offs, ts)

        def ev(kind, text, important=True):
            key = (kind, text)
            if key in seen and t - seen[key] < 1.0:
                return
            seen[key] = t
            events.append((t, kind, text, important))

        m = R_SCEN.match(line)
        if m:
            ev("시나리오", m.group(1)); continue
        m = R_STEP_NO.match(line)
        if m:
            states.append((t, "번호", m.group(1))); prog_max = 0; last_prog = None
            ev("단계 시작", "SubStep " + m.group(1)); continue
        m = R_STEP_DONE.match(line)
        if m:
            ev("단계 완료", "SubStep " + m.group(1)); continue
        m = R_GUIDE.match(line)
        if m:
            states.append((t, "안내", m.group(1))); continue
        m = R_PHASE.match(line)
        if m:
            states.append((t, "단계", m.group(1))); continue
        m = R_SCORE.match(line)
        if m:
            ev("채점", m.group(1)); continue
        m = R_CONTACT.match(line)
        if m:
            states.append((t, m.group(1), m.group(2) + "·" + m.group(3)))
            ev("접촉", "%s %s (%s)" % (m.group(1), m.group(2), m.group(3))); continue
        m = R_PROG.match(line)
        if m:
            p = int(m.group(1))
            states.append((t, "진행", p))
            if p >= 100 and (last_prog or 0) < 100:
                ev("진행 100%", "도달")
            if prog_max - p >= 10:
                ev("진행 되돌아감", "%d%% → %d%%" % (prog_max, p))
                prog_max = p
            prog_max = max(prog_max, p)
            last_prog = p
            continue
        if R_SKIP.match(line):
            ev("애니 건너뜀", "접촉 없음으로 멈춤"); continue
        m = R_REMOTE.match(line)
        if m:
            ev("리모컨", line[len("[PositionRemote] "):][:120]); continue
        m = R_GUIDEHAND.match(line)
        if m:
            ev("가이드손", m.group(1), important=False); continue
        if R_EXC.match(line):
            ev("예외", line[:140])
    return events, states


def state_at(states, t):
    st = {}
    for (ts_, k, v) in states:
        if ts_ > t:
            break
        st[k] = v
    return st


def pick_times(events, dur, a, t0, t1):
    times = set()
    x = t0
    while x <= t1:
        times.add(round(x, 2)); x += a.sparse
    dense = set()
    for (t, kind, _, imp) in events:
        if not imp or t < t0 or t > t1:
            continue
        k = -a.pad
        while k <= a.pad + 1e-6:
            y = round(min(max(t + k, 0), dur - 0.05), 2)
            dense.add(y); k += 1.0 / a.dense_fps
    # 너무 많으면 촘촘한 쪽을 솎는다
    dense = sorted(dense)
    room = max(a.max_frames - len(times), 0)
    if len(dense) > room and room > 0:
        step = len(dense) / float(room)
        dense = [dense[int(i * step)] for i in range(room)]
    elif room == 0:
        dense = []
    out = sorted(times | set(dense))
    merged = []
    for y in out:                      # 0.1초 안의 중복 제거
        if not merged or y - merged[-1] >= 0.1:
            merged.append(y)
    return merged, set(round(v, 2) for v in times)


def extract(video, t, path, width):
    subprocess.run([FFMPEG, "-v", "error", "-y", "-ss", "%.2f" % t, "-i", video, "-frames:v", "1",
                    "-vf", "scale=%d:-2" % width, path], creationflags=NOWIN)
    return os.path.exists(path)


def label_frames(items, outdir, width, per_sheet=6, cols=3):
    from PIL import Image, ImageDraw, ImageFont
    f = ImageFont.truetype(FONT, 15)
    fb = ImageFont.truetype(FONT_B, 17)
    sheets = []
    tiles = []
    for it in items:
        im = Image.open(it["png"]).convert("RGB")
        band = 74
        canvas = Image.new("RGB", (im.width, im.height + band), (20, 20, 20))
        canvas.paste(im, (0, band))
        d = ImageDraw.Draw(canvas)
        col = (255, 90, 90) if it["hit"] else (255, 220, 80)
        d.text((6, 2), "%s  %s" % (fmt(it["t"]), it["step"]), font=fb, fill=col)
        d.text((6, 25), it["guide"][:46], font=f, fill=(230, 230, 230))
        d.text((6, 47), it["state"][:60], font=f, fill=(150, 220, 255))
        if it["hit"]:
            d.rectangle([0, band, canvas.width - 1, canvas.height - 1], outline=(255, 60, 60), width=3)
            d.text((8, band + 6), it["hit"][:50], font=fb, fill=(255, 80, 80), stroke_width=2, stroke_fill=(0, 0, 0))
        tiles.append(canvas)
    for i in range(0, len(tiles), per_sheet):
        group = tiles[i:i + per_sheet]
        w, h = group[0].width, group[0].height
        rows = (len(group) + cols - 1) // cols
        sheet = Image.new("RGB", (w * cols, h * rows), (0, 0, 0))
        for j, tl in enumerate(group):
            sheet.paste(tl, ((j % cols) * w, (j // cols) * h))
        p = os.path.join(outdir, "판_%03d_%s.png" % (i // per_sheet + 1, fmt(items[i]["t"]).replace(":", "m")))
        sheet.save(p)
        sheets.append((p, items[i]["t"], items[min(i + per_sheet, len(items)) - 1]["t"]))
    return sheets


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("video", nargs="?")
    ap.add_argument("--sparse", type=float, default=5.0)
    ap.add_argument("--dense-fps", type=float, default=4.0)
    ap.add_argument("--pad", type=float, default=1.5)
    ap.add_argument("--max-frames", type=int, default=180)
    ap.add_argument("--width", type=int, default=560)
    ap.add_argument("--from", dest="t_from")
    ap.add_argument("--to", dest="t_to")
    ap.add_argument("--no-frames", action="store_true")
    ap.add_argument("--anchor", action="append", help="'로그 글귀@m:ss' — 시각표 없는 옛 녹화 보정(여러 개)")
    a = ap.parse_args()

    video = a.video or latest_video()
    if not video or not os.path.exists(video):
        print("★영상이 없다 — %s" % video); return 1
    dur = duration(video)
    raw = open(EDITOR_LOG, "rb").read()
    offs, ts, estimated, how = build_map(video, dur, raw)
    if a.anchor:
        offs, ts = apply_anchors(raw, offs, ts, a.anchor, dur)
        how += " + 기준점 %d개(화면 확인)" % len(a.anchor)
    events, states = scan(raw, offs, ts)
    t0 = parse_time(a.t_from) or 0.0
    t1 = min(parse_time(a.t_to) or dur, dur)

    stem = os.path.splitext(os.path.basename(video))[0]
    outdir = os.path.join(os.path.dirname(video), stem + "_분석")
    os.makedirs(outdir, exist_ok=True)
    for old in glob.glob(os.path.join(outdir, "*.png")):
        os.remove(old)

    # 시간표
    lines = ["# %s — 사건 시간표" % os.path.basename(video), "",
             "- 영상 %.1f초 · 시각 맞추기: %s" % (dur, how), ""]
    counts = {}
    for (t, kind, text, imp) in events:
        counts[kind] = counts.get(kind, 0) + 1
    lines.append("- 사건 수: " + " · ".join("%s %d" % kv for kv in sorted(counts.items(), key=lambda x: -x[1])))
    lines += ["", "| 영상 | 단계 | 사건 | 내용 |", "|---|---|---|---|"]
    for (t, kind, text, imp) in events:
        if t < t0 or t > t1:
            continue
        st = state_at(states, t)
        lines.append("| %s | %s | %s | %s |" % (fmt(t), st.get("단계", ""), kind, text.replace("|", "/")))

    sheets = []
    if not a.no_frames:
        times, sparse_set = pick_times(events, dur, a, t0, t1)
        items = []
        for k, t in enumerate(times):
            png = os.path.join(outdir, "_f%04d.png" % k)
            if not extract(video, t, png, a.width):
                continue
            st = state_at(states, t)
            hits = [kind + " " + text for (te, kind, text, imp) in events if imp and abs(te - t) <= 0.13]
            state = "진행 %s%% · 왼손 %s · 오른손 %s" % (st.get("진행", "-"), st.get("왼손", "-"), st.get("오른손", "-"))
            items.append({"t": t, "png": png, "step": st.get("단계", "") + "  #" + str(st.get("번호", "")),
                          "guide": st.get("안내", ""), "state": state, "hit": hits[0] if hits else ""})
        sheets = label_frames(items, outdir, a.width)
        for it in items:
            os.remove(it["png"])
        lines += ["", "## 판(6장씩 · 빨간 테두리 = 사건 순간)", ""]
        lines += ["- %s ~ %s — %s" % (fmt(s1), fmt(s2), os.path.basename(p)) for (p, s1, s2) in sheets]

    md = os.path.join(outdir, "timeline.md")
    open(md, "w", encoding="utf-8").write("\n".join(lines) + "\n")
    print("[분석] %s" % how)
    print("[분석] 사건 %d개 · 판 %d장 → %s" % (len(events), len(sheets), outdir))
    print("[분석] 시간표 %s" % md)
    return 0


if __name__ == "__main__":
    sys.exit(main())
