import csv, sys, statistics as st

o = sys.argv[1]
rows = list(csv.DictReader(open(o + '/angles.csv', encoding='utf-8')))
for r in rows:
    r['diff'] = float(r['diff']); r['plane'] = float(r['plane']); r['direct'] = float(r['direct']); r['tilt_max'] = float(r['tilt_max'])
M = [('굴곡', '옆면'), ('신전', '옆면'), ('우측굴', '앞면'), ('좌측굴', '앞면'), ('우회전', '바닥면'), ('좌회전', '바닥면')]
S = []
for m, pl in M:
    s = [r for r in rows if r['motion'] == m]
    df = [abs(r['diff']) for r in s]
    S.append(dict(m=m, pl=pl, n=len(s), rows=s,
                  p=st.mean(r['plane'] for r in s), d=st.mean(r['direct'] for r in s),
                  b=[sum(x <= 1 for x in df), sum(1 < x <= 2 for x in df), sum(2 < x <= 5 for x in df), sum(x > 5 for x in df)],
                  med=st.median(r['diff'] for r in s), mx=max(df)))

FONT = "font-family='Malgun Gothic'"
W = 760
GREEN, LGREEN, AMBER, RED, BLUE, GREY, ORANGE = '#1E8449', '#82C99A', '#F0B54A', '#D9534F', '#2F6FD6', '#B8BEC6', '#E67E22'


def svg(h, inner):
    return f"<svg viewBox='0 0 {W} {h}' xmlns='http://www.w3.org/2000/svg' {FONT} font-size='13'>{inner}</svg>"


# ── 그림 1: 동작별 평균 나란히 ──
def fig_bars():
    rowh, x0, x1 = 58, 120, 640
    H = 60 + rowh * len(S) + 30
    sc = lambda v: x0 + v / 100 * (x1 - x0)
    g = [f"<rect x='{x0}' y='14' width='14' height='14' fill='{GREY}'/><text x='{x0+20}' y='26'>면 각 (지금 쓰는 값)</text>",
         f"<rect x='{x0+200}' y='14' width='14' height='14' fill='{BLUE}'/><text x='{x0+220}' y='26'>직접 각</text>",
         f"<text x='{W-10}' y='44' text-anchor='end' fill='#777' font-size='11'>평균 차이</text>"]
    for v in range(0, 101, 20):
        g.append(f"<line x1='{sc(v)}' x2='{sc(v)}' y1='44' y2='{H-26}' stroke='#E3E6EA'/><text x='{sc(v)}' y='{H-8}' text-anchor='middle' fill='#777'>{v}°</text>")
    for i, s in enumerate(S):
        y = 50 + i * rowh
        g.append(f"<text x='{x0-10}' y='{y+22}' text-anchor='end' font-weight='600'>{s['m']}</text><text x='{x0-10}' y='{y+38}' text-anchor='end' fill='#777' font-size='11'>{s['n']}건</text>")
        g.append(f"<rect x='{x0}' y='{y+6}' width='{sc(s['p'])-x0:.1f}' height='18' fill='{GREY}'/><text x='{sc(s['p'])+6:.1f}' y='{y+20}' fill='#444'>{s['p']:.1f}°</text>")
        g.append(f"<rect x='{x0}' y='{y+26}' width='{sc(s['d'])-x0:.1f}' height='18' fill='{BLUE}'/><text x='{sc(s['d'])+6:.1f}' y='{y+40}' fill='{BLUE}'>{s['d']:.1f}°</text>")
        diff = s['d'] - s['p']
        g.append(f"<text x='{W-10}' y='{y+31}' text-anchor='end' font-weight='700' fill='{RED if abs(diff) >= 2 else GREEN}' font-size='15'>{diff:+.1f}°</text>")
    return svg(H, ''.join(g))


# ── 그림 2: 동작별 한 줄 점 그래프(분산) ──
def fig_strip():
    x0, x1, lo, hi = 120, 730, -10, 28
    rowh, ytop = 46, 100
    H = ytop + rowh * len(S) + 50
    sc = lambda v: x0 + (v - lo) / (hi - lo) * (x1 - x0)
    g = []
    # ±1° 띠 · 0선
    g.append(f"<rect x='{sc(-1):.1f}' y='{ytop-8}' width='{sc(1)-sc(-1):.1f}' height='{rowh*len(S)+8}' fill='{GREEN}' fill-opacity='0.13'/>")
    g.append(f"<text x='{sc(0):.1f}' y='{ytop-14}' text-anchor='middle' fill='{GREEN}' font-weight='700' font-size='12'>±1° 안 = 두 값이 같다</text>")
    g.append(f"<text x='{sc(-1)-6:.1f}' y='60' text-anchor='end' fill='#555' font-size='12'>← 직접 각이 더 작다</text><text x='{sc(1)+6:.1f}' y='60' fill='#555' font-size='12'>직접 각이 더 크다 →</text>")
    for v in range(-10, 29, 5):
        g.append(f"<line x1='{sc(v):.1f}' x2='{sc(v):.1f}' y1='{ytop-8}' y2='{ytop+rowh*len(S)}' stroke='{'#888' if v == 0 else '#E3E6EA'}'/><text x='{sc(v):.1f}' y='{ytop+rowh*len(S)+18}' text-anchor='middle' fill='#777'>{'0' if v == 0 else format(v, '+d')}°</text>")
    g.append(f"<text x='{x1}' y='{H-8}' text-anchor='end' fill='#555' font-size='12'>직접 각 − 면 각 (°)</text>")
    for i, s in enumerate(S):
        yc = ytop + i * rowh + rowh / 2
        if i % 2: g.append(f"<rect x='{x0}' y='{yc-rowh/2:.1f}' width='{x1-x0}' height='{rowh}' fill='#F6F7F9' fill-opacity='0.7'/>")
        col = ORANGE if '회전' in s['m'] else BLUE
        g.append(f"<text x='{x0-10}' y='{yc-2:.1f}' text-anchor='end' font-weight='600'>{s['m']}</text><text x='{x0-10}' y='{yc+13:.1f}' text-anchor='end' fill='#777' font-size='11'>{s['n']}건 · 1° 안 {round(100*s['b'][0]/s['n'])}%</text>")
        for k, r in enumerate(s['rows']):
            jit = ((k * 37) % 11 - 5) * 2.6
            g.append(f"<circle cx='{sc(r['diff']):.1f}' cy='{yc+jit:.1f}' r='4' fill='{col}' fill-opacity='0.55' stroke='{col}' stroke-width='0.6'/>")
        md = s['med']
        g.append(f"<line x1='{sc(md):.1f}' x2='{sc(md):.1f}' y1='{yc-17:.1f}' y2='{yc+17:.1f}' stroke='#111' stroke-width='2.5'/>")
    g.append(f"<line x1='{x0}' x2='{x0+18}' y1='20' y2='20' stroke='#111' stroke-width='2.5'/><text x='{x0+24}' y='24' font-size='12' fill='#333'>검은 막대 = 중앙값</text>")
    g.append(f"<circle cx='{x0+170}' cy='20' r='4' fill='{BLUE}' fill-opacity='0.55'/><text x='{x0+180}' y='24' font-size='12' fill='#333'>점 하나 = 측정 한 건</text>")
    return svg(H, ''.join(g))


# ── 그림 3: 구간별 비율 ──
def fig_stack():
    cols = [GREEN, LGREEN, AMBER, RED]; labs = ['1° 이내', '1~2°', '2~5°', '5° 넘음']
    x0, x1 = 120, 700
    H = 60 + 44 * len(S) + 10
    g = []
    for k in range(4):
        g.append(f"<rect x='{x0+k*140}' y='14' width='14' height='14' fill='{cols[k]}'/><text x='{x0+k*140+20}' y='26'>{labs[k]}</text>")
    for i, s in enumerate(S):
        y = 50 + i * 44
        g.append(f"<text x='{x0-10}' y='{y+20}' text-anchor='end' font-weight='600'>{s['m']}</text>")
        cx = x0
        for k, c in enumerate(s['b']):
            w = c / s['n'] * (x1 - x0)
            if w > 0:
                g.append(f"<rect x='{cx:.1f}' y='{y+4}' width='{w:.1f}' height='24' fill='{cols[k]}'/>")
                if w > 34:
                    g.append(f"<text x='{cx+w/2:.1f}' y='{y+21}' text-anchor='middle' fill='{'#fff' if k in (0, 3) else '#222'}' font-size='12'>{round(100*c/s['n'])}%</text>")
            cx += w
        g.append(f"<text x='{x1+8}' y='{y+21}' fill='#555' font-size='12'>{s['n']}건</text>")
    return svg(H, ''.join(g))


# ── 그림 4: 왜 벌어지나(면 밖 기울기 vs 차이) — 읽는 글자를 영역에 붙인다 ──
def fig_why():
    x0, x1, y0, y1 = 70, 730, 40, 330
    tmax, lo, hi = 45, -10, 28
    sx = lambda t: x0 + t / tmax * (x1 - x0)
    sy = lambda d: y1 - (d - lo) / (hi - lo) * (y1 - y0)
    H = y1 + 50
    g = []
    g.append(f"<rect x='{x0}' y='{sy(1.5):.1f}' width='{sx(10)-x0:.1f}' height='{sy(-1.5)-sy(1.5):.1f}' fill='{GREEN}' fill-opacity='0.13'/>")
    for v in range(-10, 29, 5):
        g.append(f"<line x1='{x0}' x2='{x1}' y1='{sy(v):.1f}' y2='{sy(v):.1f}' stroke='{'#888' if v == 0 else '#E3E6EA'}'/><text x='{x0-8}' y='{sy(v)+4:.1f}' text-anchor='end' fill='#777'>{'0' if v == 0 else format(v, '+d')}°</text>")
    for t in range(0, 46, 10):
        g.append(f"<text x='{sx(t):.1f}' y='{y1+18}' text-anchor='middle' fill='#777'>{t}°</text>")
    g.append(f"<line x1='{sx(10):.1f}' x2='{sx(10):.1f}' y1='{y0}' y2='{y1}' stroke='#999' stroke-dasharray='4 4'/>")
    g.append(f"<text x='{x1}' y='{y1+40}' text-anchor='end' fill='#555' font-size='12'>잰 선이 그 동작의 면 밖으로 기운 정도 (°)</text><text x='{x0-60}' y='{y0-14}' fill='#555' font-size='12'>직접 각 − 면 각 (°)</text>")
    for r in rows:
        col = ORANGE if '회전' in r['motion'] else BLUE
        g.append(f"<circle cx='{sx(r['tilt_max']):.1f}' cy='{sy(r['diff']):.1f}' r='4' fill='{col}' fill-opacity='0.55'/>")
    # 영역 글자
    g.append(f"<text x='{sx(1):.1f}' y='{sy(5.5):.1f}' fill='{GREEN}' font-weight='700' font-size='13'>① 선이 면 안 → 차이 ≈ 0</text><text x='{sx(1):.1f}' y='{sy(3.8):.1f}' fill='{GREEN}' font-size='11'>굴곡·신전·측굴 142건 · 최대 3.1°</text>")
    g.append(f"<text x='{sx(23):.1f}' y='{sy(21):.1f}' fill='{BLUE}' font-weight='700' font-size='13'>② 선이 면 밖으로 크게 기움 → 차이 큼</text><text x='{sx(23):.1f}' y='{sy(19.2):.1f}' fill='{BLUE}' font-size='11'>면 밖 20~42° 기운 6건 · 10~26°</text>")
    g.append(f"<text x='{sx(29):.1f}' y='{sy(-6.5):.1f}' fill='{ORANGE}' font-weight='700' font-size='13'>③ 회전 — 대부분 직접 각이 작다</text><text x='{sx(29):.1f}' y='{sy(-8.2):.1f}' fill='{ORANGE}' font-size='11'>선이 바닥면에서 약 19° 기울어 있음</text>")
    return svg(H, ''.join(g))


tbl = ''.join(f"<tr><td>{s['m']}</td><td>{s['pl']}</td><td>{s['n']}</td><td>{s['p']:.1f}</td><td>{s['d']:.1f}</td><td>{s['med']:+.1f}</td><td>{round(100*s['b'][0]/s['n'])}%</td><td>{s['mx']:.1f}</td></tr>" for s in S)
body = f"""
<h1>선 실측 — 직접 각과 면 각 비교</h1>
<p class="by">2026-10-15 · CoCoChuna 경추ROM 선 실측 · Link 시험 측정 199건</p>
<div class="key"><b>한 줄 결론</b> — 굴곡·신전·측굴은 두 값이 거의 같다(측정 10건 중 9건이 1° 이내). 회전은 직접 각이 3~4° 작지만, 회전은 바닥면 각을 그대로 쓰므로 문제가 되지 않는다.</div>

<h2>1. 두 각은 무엇이 다른가</h2>
<table><tr><th></th><th>면 각 (지금 쓰는 값)</th><th>직접 각</th></tr>
<tr><td>계산</td><td>두 선을 그 동작의 면에 내린 뒤 잰 각</td><td>두 선 사이 3차원 각 그대로</td></tr>
<tr><td>쓰는 면</td><td>굴곡·신전 = 옆면 · 측굴 = 앞면 · 회전 = 바닥면</td><td>없음 (환자 방향을 몰라도 나온다)</td></tr>
<tr><td>담는 것</td><td>그 면 안의 움직임만</td><td>면 밖으로 움직인 만큼도 포함</td></tr></table>
<p>두 선이 모두 면 안에 있으면 두 값은 똑같다. 선이 면 밖으로 기울수록 두 값이 벌어진다.</p>

<h2>2. 동작별 평균을 나란히 보면</h2>
<figure>{fig_bars()}<figcaption>막대 = 동작별 평균. 오른쪽 숫자 = 직접 각 − 면 각 (초록 2° 미만 · 빨강 2° 이상)</figcaption></figure>
<p>굴곡·신전·측굴은 두 막대 길이가 거의 같다. 굴곡의 +1.8°는 면 밖으로 크게 기운 몇 건이 평균을 끌어올린 값이다(다음 그림의 오른쪽 끝 점들).</p>

<h2>3. 측정 한 건 한 건은 어디에 있나 (분산)</h2>
<div class="how"><b>읽는 법</b> — 한 줄이 한 동작이고, 점 하나가 측정 한 건이다. 점이 <b>가운데 초록 띠(±1°)</b> 안에 몰려 있을수록 두 값이 같다는 뜻이다. 검은 막대는 그 동작의 중앙값이다.</div>
<figure>{fig_strip()}<figcaption>가로 위치 = 그 측정의 직접 각 − 면 각</figcaption></figure>
<p>굴곡·신전·측굴은 점이 거의 다 초록 띠 안에 있고, 띠 밖으로 멀리 나간 점은 몇 개뿐이다. 회전 두 줄은 띠 왼쪽(−2~−8°)에 모여 있다.</p>

<h2>4. 차이가 몇 도 안에 들었나 (비율)</h2>
<figure>{fig_stack()}<figcaption>|직접 각 − 면 각|을 구간으로 나눈 비율</figcaption></figure>

<h2>5. 왜 벌어지나 (검증)</h2>
<div class="how"><b>읽는 법</b> — 가로는 잰 선이 그 동작의 면 밖으로 기운 정도, 세로는 두 값의 차이다. 점이 세 무리로 나뉘고, 각 무리에 이름을 붙였다.</div>
<figure>{fig_why()}<figcaption>파랑 = 굴곡·신전·측굴 · 주황 = 회전 · 초록 칸 = 면 밖 기울기 10° 이하이면서 차이 ±1.5° 이내</figcaption></figure>
<ul><li><b>①</b> 선이 면 안에 있으면 차이는 0 근처다. 측정 오차가 아니라 두 계산이 같은 값을 내는 경우다.</li>
<li><b>②</b> 차이가 10° 넘는 6건은 모두 잰 선이 면 밖으로 20~42° 기운 경우다. 면 각은 그 면 안의 움직임만, 직접 각은 면 밖 움직임까지 담는다. 그때 목이 다른 방향으로도 움직였는지, 점을 다른 자리에 찍었는지는 기록으로 구분할 수 없다.</li>
<li><b>③</b> 회전을 재는 선은 바닥면에서 약 19° 기울어 있어 직접 각이 늘 작게 나온다. 계산(cos φ = cos²β·cos θ + sin²β, β = 19°)으로도 60° 회전에서 −3.6°, 80°에서 −5.1°가 나와 측정과 같은 크기다.</li></ul>

<h2>6. 정리</h2>
<table><tr><th>동작</th><th>판단</th></tr>
<tr><td>굴곡·신전</td><td>선이 옆면 안에 있으면 직접 각으로 바꿔도 값이 거의 안 바뀐다</td></tr>
<tr><td>측굴</td><td>선이 앞면 안에 있으면 직접 각으로 바꿔도 값이 거의 안 바뀐다</td></tr>
<tr><td>회전</td><td>바닥면 각을 그대로 쓴다. 바닥면은 환자가 어느 쪽을 보든 하나로 정해져 있어, 처음 기준선에서부터의 각이 나온다</td></tr></table>

<h2>부록 — 동작별 숫자</h2>
<table class="num"><tr><th>동작</th><th>면</th><th>측정 수</th><th>면 각 평균 (°)</th><th>직접 각 평균 (°)</th><th>차이 중앙값 (°)</th><th>1° 이내</th><th>차이 최대 (°)</th></tr>{tbl}</table>
<p class="foot">자료: Link로 한 시험의 CSV 12판(2026-10-12 17:03 ~ 10-14 16:30), 순서 측정 199건(다시 찍은 것 포함). 검산: CSV의 점 좌표로 두 각을 다시 계산해 199건 모두 CSV 값과 0.14° 안에서 일치했다. 헤드셋 단독(기기) 기록은 들어 있지 않다. 판마다 무엇을 쟀는지(사람 머리인지 시험용 자리인지)는 CSV에 남지 않아 구분하지 못했다. 어느 값이 참값에 가까운지(각도기 등과의 비교)는 이 문서의 범위가 아니다.</p>
"""
css = """@page{size:A4;margin:15mm 14mm}body{font-family:'Malgun Gothic',sans-serif;font-size:10.5pt;line-height:1.6;color:#1a1a1a;max-width:800px;margin:auto;background:#fff}
h1{font-size:19pt;margin:0 0 2px}h2{font-size:13.5pt;margin:22px 0 8px;padding-bottom:3px;border-bottom:2px solid #FF6B05;break-after:avoid}
.by{color:#666;margin:0 0 12px;font-size:9.5pt}.key{background:#F4F7FB;border-left:4px solid #2F6FD6;padding:10px 14px;margin:10px 0 4px}
.how{background:#F7F7F2;border-left:4px solid #999;padding:7px 12px;margin:6px 0;font-size:9.8pt;break-after:avoid}
table{border-collapse:collapse;width:100%;margin:8px 0;font-size:9.5pt;break-inside:avoid}th,td{border:1px solid #ccc;padding:5px 8px;text-align:left}th{background:#f3f3f3}
table.num td:nth-child(n+3){text-align:right}figure{margin:8px 0;break-inside:avoid}figure svg{width:100%;height:auto}figcaption{font-size:9pt;color:#555;text-align:center}
.foot{font-size:9pt;color:#666;margin-top:16px;border-top:1px solid #ddd;padding-top:6px}"""
open(o + '/cmp.html', 'w', encoding='utf-8').write(
    f"<!doctype html><html lang='ko'><head><meta charset='utf-8'><title>직접 각과 면 각 비교</title><style>{css}</style></head><body>{body}</body></html>")
print('ok')
