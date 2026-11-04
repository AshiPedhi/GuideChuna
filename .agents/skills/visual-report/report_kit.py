"""한눈에 읽히는 보고 문서 — 그림·틀 도구.

HTML(인라인 SVG + base64 사진) 한 장을 만들고 Chrome으로 캡처 · PDF를 뽑는다.
matplotlib가 없는 환경이라 그림은 전부 SVG로 직접 그린다.

쓰는 법(문서 생성기 맨 위):
    import sys; sys.path.insert(0, r'C:/UnityProjects/GuideChuna/.claude/skills/visual-report')
    from report_kit import *
    body = title(...) + key(...) + h2(...) + how(...) + figure(paired_bars(...), '캡션') + ...
    save(body, '문서 제목', out_dir, 'CoCoChuna_경추ROM_<주제>_V0.1_20261015')
"""
import base64, os, subprocess

CHROME = r'C:/Program Files/Google/Chrome/Application/chrome.exe'
FONT = "font-family='Malgun Gothic'"
W = 760  # SVG 폭(문서 칼럼)

# 색 — 역할마다 하나. 좋음/주의/나쁨은 이 넷만 쓴다.
GREEN, LGREEN, AMBER, RED = '#1E8449', '#82C99A', '#F0B54A', '#D9534F'
BLUE, GREY, ORANGE, INK, MUTED, GRID = '#2F6FD6', '#B8BEC6', '#E67E22', '#1a1a1a', '#777', '#E3E6EA'

CSS = """@page{size:A4;margin:15mm 14mm}body{font-family:'Malgun Gothic',sans-serif;font-size:10.5pt;line-height:1.6;color:#1a1a1a;max-width:800px;margin:auto;background:#fff}
h1{font-size:19pt;margin:0 0 2px}h2{font-size:13.5pt;margin:22px 0 8px;padding-bottom:3px;border-bottom:2px solid #FF6B05;break-after:avoid}
.by{color:#666;margin:0 0 12px;font-size:9.5pt}.key{background:#F4F7FB;border-left:4px solid #2F6FD6;padding:10px 14px;margin:10px 0 4px}
.how{background:#F7F7F2;border-left:4px solid #999;padding:7px 12px;margin:6px 0;font-size:9.8pt;break-after:avoid}
table{border-collapse:collapse;width:100%;margin:8px 0;font-size:9.5pt;break-inside:avoid}th,td{border:1px solid #ccc;padding:5px 8px;text-align:left;vertical-align:top}th{background:#f3f3f3}
table.num td:nth-child(n+3){text-align:right}figure{margin:8px 0;break-inside:avoid}figure svg{width:100%;height:auto}figcaption{font-size:9pt;color:#555;text-align:center}
.shot{position:relative;break-inside:avoid;margin:6px 0}.shot img{width:100%;display:block;border-radius:4px}
.pin{position:absolute;transform:translate(-50%,-50%);width:22px;height:22px;border-radius:50%;background:#FF6B05;color:#fff;font-weight:700;font-size:12px;text-align:center;line-height:22px;border:2px solid #fff;box-shadow:0 0 3px #000}
.pair{display:flex;gap:10px;break-inside:avoid}.pair>div{flex:1}.pair .cap{font-size:9pt;color:#444;margin:3px 0;text-align:center}
.cards{display:flex;gap:8px;margin:8px 0;break-inside:avoid}.cards div{flex:1;background:#FFF4EC;border:1px solid #F5C9A6;border-radius:6px;padding:7px 9px;font-size:9.6pt}.cards b{color:#C0560A}
.foot{font-size:9pt;color:#666;margin-top:16px;border-top:1px solid #ddd;padding-top:6px}
code{font-size:9pt;background:#f3f3f3;padding:0 3px}"""


# ── 글 틀 ──
def title(t, by): return f"<h1>{t}</h1><p class='by'>{by}</p>"
def key(text): return f"<div class='key'><b>한 줄 결론</b> — {text}</div>"
def how(text): return f"<div class='how'><b>읽는 법</b> — {text}</div>"
def h2(t): return f"<h2>{t}</h2>"
def p(t): return f"<p>{t}</p>"
def ul(items): return '<ul>' + ''.join(f'<li>{i}</li>' for i in items) + '</ul>'
def foot(t): return f"<p class='foot'>{t}</p>"
def figure(svg_or_html, cap=''): return f"<figure>{svg_or_html}<figcaption>{cap}</figcaption></figure>"
def cards(items):  # [(굵은 머리, 설명)] — 순서 · 단계
    return "<div class='cards'>" + ''.join(f"<div><b>{i+1}. {h}</b><br>{t}</div>" for i, (h, t) in enumerate(items)) + "</div>"


def table(head, rows, cls=''):
    h = ''.join(f'<th>{x}</th>' for x in head)
    r = ''.join('<tr>' + ''.join(f'<td>{x}</td>' for x in row) + '</tr>' for row in rows)
    return f"<table class='{cls}'><tr>{h}</tr>{r}</table>"


def img64(path):
    ext = os.path.splitext(path)[1].lower().strip('.')
    return f"data:image/{'jpeg' if ext in ('jpg', 'jpeg') else ext};base64," + base64.b64encode(open(path, 'rb').read()).decode()


def pins(path, marks):
    """사진 위에 번호 핀. marks = [(x%, y%, '1')]. 핀은 대상 <b>옆</b>에 둔다(글자를 가리지 않게)."""
    s = ''.join(f"<span class='pin' style='left:{x}%;top:{y}%'>{n}</span>" for x, y, n in marks)
    return f"<div class='shot'><img src='{img64(path)}'>{s}</div>"


def pair(left, lcap, right, rcap):
    """사진 둘을 나란히 — «보이는 것 vs 실제» 같은 대비."""
    return f"<div class='pair'><div>{left}<p class='cap'>{lcap}</p></div><div>{right}<p class='cap'>{rcap}</p></div></div>"


def svg(h, inner): return f"<svg viewBox='0 0 {W} {h}' xmlns='http://www.w3.org/2000/svg' {FONT} font-size='13'>{inner}</svg>"
def t(x, y, s, fill=INK, size=13, anchor='start', bold=False):
    return f"<text x='{x:.1f}' y='{y:.1f}' fill='{fill}' font-size='{size}' text-anchor='{anchor}'{' font-weight=\"700\"' if bold else ''}>{s}</text>"
def fmt0(v, unit=''): return ('0' if v == 0 else format(v, '+g')) + unit


# ── 그림 1: 두 값 나란히(항목별 평균 등) ──
def paired_bars(items, a_name, b_name, vmax, unit='°', warn=2.0):
    """items = [(이름, 보조 글자, a값, b값)]. 오른쪽에 b−a를 크게, |차|≥warn이면 빨강."""
    rowh, x0, x1 = 58, 120, 640
    H = 60 + rowh * len(items) + 30
    sc = lambda v: x0 + v / vmax * (x1 - x0)
    g = [f"<rect x='{x0}' y='14' width='14' height='14' fill='{GREY}'/>" + t(x0 + 20, 26, a_name),
         f"<rect x='{x0+220}' y='14' width='14' height='14' fill='{BLUE}'/>" + t(x0 + 240, 26, b_name),
         t(W - 10, 44, '차이', MUTED, 11, 'end')]
    for k in range(6):
        v = vmax * k / 5
        g.append(f"<line x1='{sc(v):.1f}' x2='{sc(v):.1f}' y1='44' y2='{H-26}' stroke='{GRID}'/>" + t(sc(v), H - 8, f"{v:g}{unit}", MUTED, 13, 'middle'))
    for i, (name, sub, a, b) in enumerate(items):
        y = 50 + i * rowh
        g.append(t(x0 - 10, y + 22, name, INK, 13, 'end', True) + t(x0 - 10, y + 38, sub, MUTED, 11, 'end'))
        g.append(f"<rect x='{x0}' y='{y+6}' width='{sc(a)-x0:.1f}' height='18' fill='{GREY}'/>" + t(sc(a) + 6, y + 20, f"{a:.1f}{unit}", '#444'))
        g.append(f"<rect x='{x0}' y='{y+26}' width='{sc(b)-x0:.1f}' height='18' fill='{BLUE}'/>" + t(sc(b) + 6, y + 40, f"{b:.1f}{unit}", BLUE))
        d = b - a
        g.append(t(W - 10, y + 31, f"{d:+.1f}{unit}", RED if abs(d) >= warn else GREEN, 15, 'end', True))
    return svg(H, ''.join(g))


# ── 그림 2: 한 줄 점 그래프(분산) ──
def strip(groups, lo, hi, band, axis_label, left_word, right_word, step=5, unit='°', center=0.0):
    """groups = [(이름, 보조 글자, [값...], 색)]. 기준 띠 ±band(초록) · 검은 막대 = 중앙값 · 점 하나 = 한 건."""
    x0, x1, rowh, ytop = 120, 730, 46, 100
    H = ytop + rowh * len(groups) + 50
    sc = lambda v: x0 + (v - lo) / (hi - lo) * (x1 - x0)
    g = [f"<line x1='{x0}' x2='{x0+18}' y1='20' y2='20' stroke='#111' stroke-width='2.5'/>" + t(x0 + 24, 24, '검은 막대 = 중앙값', '#333', 12),
         f"<circle cx='{x0+170}' cy='20' r='4' fill='{BLUE}' fill-opacity='0.55'/>" + t(x0 + 180, 24, '점 하나 = 한 건', '#333', 12),
         t(sc(center - band) - 6, 60, '← ' + left_word, '#555', 12, 'end') + t(sc(center + band) + 6, 60, right_word + ' →', '#555', 12),
         f"<rect x='{sc(center-band):.1f}' y='{ytop-8}' width='{sc(center+band)-sc(center-band):.1f}' height='{rowh*len(groups)+8}' fill='{GREEN}' fill-opacity='0.13'/>",
         t(sc(center), ytop - 14, f"±{band:g}{unit} 안", GREEN, 12, 'middle', True)]
    v = lo
    while v <= hi + 1e-9:
        g.append(f"<line x1='{sc(v):.1f}' x2='{sc(v):.1f}' y1='{ytop-8}' y2='{ytop+rowh*len(groups)}' stroke='{'#888' if abs(v-center) < 1e-9 else GRID}'/>"
                 + t(sc(v), ytop + rowh * len(groups) + 18, fmt0(v - center if center else v, unit) if not center else f"{v:g}", MUTED, 12, 'middle'))
        v += step
    g.append(t(x1, H - 8, axis_label, '#555', 12, 'end'))
    for i, (name, sub, vals, col) in enumerate(groups):
        yc = ytop + i * rowh + rowh / 2
        if i % 2: g.append(f"<rect x='{x0}' y='{yc-rowh/2:.1f}' width='{x1-x0}' height='{rowh}' fill='#F6F7F9' fill-opacity='0.7'/>")
        g.append(t(x0 - 10, yc - 2, name, INK, 13, 'end', True) + t(x0 - 10, yc + 13, sub, MUTED, 11, 'end'))
        for k, val in enumerate(vals):
            jit = ((k * 37) % 11 - 5) * 2.6
            g.append(f"<circle cx='{sc(val):.1f}' cy='{yc+jit:.1f}' r='4.5' fill='{col}' fill-opacity='0.55' stroke='{col}' stroke-width='0.6'/>")
        md = sorted(vals)[len(vals) // 2] if len(vals) % 2 else sum(sorted(vals)[len(vals)//2-1:len(vals)//2+1]) / 2
        g.append(f"<line x1='{sc(md):.1f}' x2='{sc(md):.1f}' y1='{yc-17:.1f}' y2='{yc+17:.1f}' stroke='#111' stroke-width='2.5'/>")
    return svg(H, ''.join(g))


# ── 그림 3: 구간별 비율(100% 누적 막대) ──
def stacked(items, labels, colors=(GREEN, LGREEN, AMBER, RED)):
    """items = [(이름, [구간별 개수...])]."""
    x0, x1 = 120, 700
    H = 60 + 44 * len(items) + 10
    g = [f"<rect x='{x0+k*140}' y='14' width='14' height='14' fill='{colors[k]}'/>" + t(x0 + k * 140 + 20, 26, labels[k]) for k in range(len(labels))]
    for i, (name, counts) in enumerate(items):
        y, n, cx = 50 + i * 44, sum(counts), x0
        g.append(t(x0 - 10, y + 20, name, INK, 13, 'end', True))
        for k, c in enumerate(counts):
            w = c / n * (x1 - x0)
            if w > 0:
                g.append(f"<rect x='{cx:.1f}' y='{y+4}' width='{w:.1f}' height='24' fill='{colors[k]}'/>")
                if w > 34: g.append(t(cx + w / 2, y + 21, f"{round(100*c/n)}%", '#fff' if k in (0, 3) else '#222', 12, 'middle'))
            cx += w
        g.append(t(x1 + 8, y + 21, f"{n}건", '#555', 12))
    return svg(H, ''.join(g))


# ── 저장 · 확인 ──
def save(body, doc_title, out_dir, name, to_d=True, shot_h=3600):
    """HTML → 캡처(out_dir/name.png, 글자 겹침을 눈으로 확인) → D:/name.pdf + .html."""
    html = f"<!doctype html><html lang='ko'><head><meta charset='utf-8'><title>{doc_title}</title><style>{CSS}</style></head><body>{body}</body></html>"
    hp = os.path.join(out_dir, name + '.html')
    open(hp, 'w', encoding='utf-8').write(html)
    url = 'file:///' + hp.replace('\\', '/')
    subprocess.run([CHROME, '--headless=new', '--disable-gpu', '--hide-scrollbars', f'--window-size=820,{shot_h}',
                    f'--screenshot={os.path.join(out_dir, name + ".png")}', url], capture_output=True)
    if to_d:
        subprocess.run([CHROME, '--headless=new', '--disable-gpu', '--no-pdf-header-footer', f'--print-to-pdf=D:/{name}.pdf', url], capture_output=True)
        open(f'D:/{name}.html', 'w', encoding='utf-8').write(html)
    return hp
