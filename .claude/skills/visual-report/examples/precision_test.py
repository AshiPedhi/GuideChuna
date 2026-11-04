import sys, math
sys.path.insert(0, r'C:/UnityProjects/GuideChuna/.claude/skills/visual-report')
from report_kit import *

O = sys.argv[1]

# ── 실측 자료(기기 logcat 2026-10-15) ──
LINES = [  # 이름, 아래 끝 높이 mm, 위 끝 높이 mm, 옆 어긋남 mm, 수직에서 기울기 °
    ('10:34 선 1', -6, 1202, 13.0, 0.62), ('10:35 선 2', -3, 1193, 4.0, 0.19),
    ('10:46 선 1', 670, 1192, 3.7, 0.40), ('10:46 선 2', 684, 1191, 2.9, 0.33)]
DESK = [716.5, 716.5, 716.3, 716.8, 715.3, 717.3]  # 점 7·8·9·11·13·14 높이 mm


def fig_lines():
    """세로 모서리 넷 — 회색 = 모서리(수직), 주황 = 찍은 선. 가로 · 세로 같은 비율(실제 그대로)."""
    H, top, bot, mag = 360, 60, 300, 1
    pw = W / 4
    yv = lambda h: bot - h / 1210 * (bot - top)
    g = [t(10, 24, '회색 = 실제 모서리(수직) · 주황 = 찍은 선 · 가로 · 세로 같은 비율로 그림(실제 그대로)', '#555', 12)]
    k = (bot - top) / 1210 * mag  # 세로와 같은 축척(px/mm) — 키우지 않는다(키우면 실제보다 크게 어긋나 보여 오해를 산다)
    for i, (name, ylo, yhi, off, tilt) in enumerate(LINES):
        cx = pw * i + pw / 2
        g.append(f"<line x1='{cx}' x2='{cx}' y1='{top-6}' y2='{bot+6}' stroke='{GREY}' stroke-width='10' stroke-linecap='round'/>")
        g.append(f"<line x1='{cx}' x2='{cx + off*k:.1f}' y1='{yv(ylo):.1f}' y2='{yv(yhi):.1f}' stroke='{ORANGE}' stroke-width='3'/>")
        g.append(f"<circle cx='{cx}' cy='{yv(ylo):.1f}' r='4' fill='{ORANGE}'/><circle cx='{cx + off*k:.1f}' cy='{yv(yhi):.1f}' r='4' fill='{ORANGE}'/>")
        g.append(t(cx - 10, yv(yhi) + 4, f"{yhi}", MUTED, 11, 'end') + t(cx + 10, yv(ylo) + 4, f"{ylo}", MUTED, 11))
        g.append(t(cx, bot + 30, name, INK, 12, 'middle', True))
        g.append(t(cx, bot + 48, f"기울기 {tilt}° · 옆 {off}mm", GREEN if tilt < 1 else RED, 12, 'middle', True))
    g.append(f"<line x1='0' x2='{W}' y1='{yv(0):.1f}' y2='{yv(0):.1f}' stroke='#888' stroke-dasharray='4 4'/>" + t(W - 4, yv(0) - 4, '바닥 (0mm)', MUTED, 11, 'end'))
    return svg(H, ''.join(g))


def fig_principle():
    """원리 그림 — 눈과 카메라 자리가 달라 가까운 물체가 밀려 보인다(실측 아님)."""
    H = 300
    eye, cam, obj, far = (110, 120), (150, 175), (420, 205), 700
    # 카메라 → 실제 물체 → 추정 깊이(far)까지 연장한 점
    k = (far - cam[0]) / (obj[0] - cam[0]); ghost = (far, cam[1] + (obj[1] - cam[1]) * k)
    # 눈에서 ghost를 본 방향이 물체 근처에서 지나는 자리
    k2 = (obj[0] - eye[0]) / (ghost[0] - eye[0]); seen = (obj[0], eye[1] + (ghost[1] - eye[1]) * k2)
    g = [f"<ellipse cx='120' cy='150' rx='70' ry='80' fill='#F3F4F6' stroke='#bbb'/>",
         f"<circle cx='{eye[0]}' cy='{eye[1]}' r='9' fill='{BLUE}'/>" + t(eye[0] - 14, eye[1] - 16, '눈', BLUE, 13, 'end', True),
         f"<rect x='{cam[0]-9}' y='{cam[1]-7}' width='18' height='14' fill='#555'/>" + t(cam[0] - 14, cam[1] + 30, '패스스루 카메라', '#555', 12, 'end', True),
         f"<line x1='{cam[0]}' y1='{cam[1]}' x2='{ghost[0]:.0f}' y2='{ghost[1]:.0f}' stroke='#555' stroke-dasharray='5 4'/>",
         f"<line x1='{far}' x2='{far}' y1='40' y2='280' stroke='{GRID}' stroke-width='3'/>" + t(far, 30, '영상을 펴는 추정 깊이', MUTED, 11, 'middle'),
         f"<line x1='{eye[0]}' y1='{eye[1]}' x2='{ghost[0]:.0f}' y2='{ghost[1]:.0f}' stroke='{RED}' stroke-width='2'/>",
         f"<line x1='{eye[0]}' y1='{eye[1]}' x2='{obj[0]}' y2='{obj[1]}' stroke='{GREEN}' stroke-width='2.5'/>",
         f"<circle cx='{obj[0]}' cy='{obj[1]}' r='10' fill='{GREEN}'/>" + t(obj[0] + 14, obj[1] + 22, '실제 촉 · 가상 점이 그려지는 자리', GREEN, 12, 'start', True),
         f"<circle cx='{seen[0]}' cy='{seen[1]:.0f}' r='10' fill='none' stroke='{RED}' stroke-width='2.5' stroke-dasharray='3 2'/>" + t(obj[0] + 14, seen[1] - 6, '패스스루 화면에서 컨트롤러가 보이는 방향', RED, 12, 'start', True),
         t(10, 290, '원리 그림(실측 아님). 물체가 가까울수록, 추정 깊이와 실제 거리가 다를수록 두 방향이 더 벌어진다.', '#555', 11)]
    return svg(H, ''.join(g))


def fig_tracking():
    """위에서 본 추적 범위 — 이번 한 판 로그의 경향(범위를 정해 잰 것 아님)."""
    H, cx, cy, R = 330, 300, 250, 200
    def pt(deg, r): a = math.radians(deg - 90); return cx + r * math.cos(a), cy + r * math.sin(a)
    def sector(a0, a1, r, col, op):
        x0, y0 = pt(a0, r); x1, y1 = pt(a1, r)
        return f"<path d='M{cx},{cy} L{x0:.1f},{y0:.1f} A{r},{r} 0 {1 if a1-a0>180 else 0},1 {x1:.1f},{y1:.1f} Z' fill='{col}' fill-opacity='{op}'/>"
    g = [sector(-60, 60, R, GREEN, 0.18), sector(88, 272, R * 0.55, RED, 0.15),
         f"<circle cx='{cx}' cy='{cy}' r='16' fill='#555'/>" + t(cx, cy + 34, '헤드셋', '#555', 12, 'middle', True),
         f"<line x1='{cx}' y1='{cy}' x2='{cx}' y2='{cy-R-10}' stroke='#555' stroke-dasharray='4 4'/>" + t(cx, cy - R - 16, '보는 방향', '#555', 11, 'middle'),
         t(cx, cy - R * 0.55, '잘 잡힘', GREEN, 15, 'middle', True), t(cx, cy - R * 0.55 + 18, '정면 ±60° · 1.27m에서도', GREEN, 12, 'middle'),
         t(cx, cy + 75, '놓침 · 헤드셋 뒤·옆', RED, 13, 'middle', True), t(cx, cy + 92, '좌우 ±88~176°', RED, 11, 'middle')]
    for d in (-60, 60):
        x, y = pt(d, R + 14); g.append(t(x, y, f"{d:+d}°", MUTED, 11, 'middle'))
    notes = [('머리 위', '위아래 +53~+84°'), ('링이 헤드셋 반대쪽', '윗면 방향 −0.32 ~ −0.90'), ('멀고 비스듬함', '1.2~1.7m · 아래 −45~−50°'), ('빠르게 움직인 직후', '속도 1~2m/s')]
    for i, (a, b) in enumerate(notes):
        y = 60 + i * 52
        g.append(f"<rect x='540' y='{y-18}' width='210' height='42' rx='6' fill='#FDECEA'/>" + t(550, y, a, RED, 13, 'start', True) + t(550, y + 17, b, '#555', 11))
    g.append(t(540, 30, '그 밖에 놓친 자리', '#555', 12, 'start', True))
    return svg(H, ''.join(g))


near, side = O + '/near.jpg', O + '/side.jpg'
body = (
    title('선 실측 — 컨트롤러 끝점 정밀도 시험', '2026-10-15 · CoCoChuna 경추ROM 선 실측 · Meta Quest 기기 로그 실측')
    + key('화면에서 노란 점이 컨트롤러와 떨어져 보여도, <b>기록되는 점은 실물 촉 자리에 있다</b>. 1.2m 세로 모서리를 찍은 선은 수직에서 1° 안, 바닥 점은 0에서 6mm 안이었다. 측정자는 화면의 점이 아니라 실물 촉을 기준으로 찍으면 된다.')

    + h2('1. 무엇이 문제처럼 보였나')
    + pair(pins(near, [(50, 62, '1'), (80, 40, '2')]), '가까이서 본 화면 — ① 노란 점이 ② 실물 컨트롤러 끝에서 떨어져 보인다',
           pins(side, [(88, 50, '3')]), '옆에서 다시 본 화면 — ③ 찍은 선이 파티션 모서리를 그대로 따라간다')
    + p('컨트롤러를 세워 시야 아래쪽 가까이(헤드셋에서 35~50cm) 들면 생긴다. 이때도 기기는 컨트롤러를 «추적됨»으로 보고 있었다(추정값이 아니다).')

    + h2('2. 시험 — 세로 모서리를 따라 찍기')
    + how('바닥에서 파티션 세로 모서리를 따라 1.2m 위까지 촉을 떼지 않고 올라가 찍었다. 회색 기둥이 실제 모서리, 주황 선이 찍힌 선이다. 실제 비율 그대로 그렸다 — 주황 선이 회색 기둥 안에 거의 겹쳐 보이는 것이 결과다. 아래 초록 글자가 그 차이의 숫자다.')
    + figure(fig_lines(), '4개 선 모두 수직에서 1° 안 · 1.2m에서 옆 어긋남 13mm 이하 · 숫자는 끝점 높이(mm)')
    + ul(['바닥에서 찍은 점 높이는 −6mm · −3mm — 바닥 기준이 맞다.',
          '위쪽 끝은 헤드셋에서 52cm, 눈높이에서 찍혔다. 화면에서 점이 밀려 보이던 바로 그 자리다.',
          '모서리가 정확히 수직인지는 재지 않았다. 위 기울기에는 모서리 자체의 기울기도 섞여 있다(실제 측정 오차는 이보다 같거나 작다).'])

    + h2('3. 시험 — 책상 면에 세워 찍기')
    + how('컨트롤러를 세워 촉을 책상에 대고, 좌우로 38cm를 옮겨 가며 찍은 점 6개의 높이다. 초록 띠(가운데 ±1mm) 안에 모이면 같은 면에 찍힌 것이다.')
    + figure(strip([('책상 면', '6점', DESK, BLUE)], 712, 722, 1.0, '바닥에서 높이 (mm)', '낮다', '높다', step=2, unit='mm', center=716.3),
             '6점 모두 715.3~717.3mm — 위아래 폭 2mm(가운데 716.3mm ±1mm)')

    + h2('4. 왜 어긋나 보이나')
    + how('초록 선 = 눈이 실제 촉을 보는 방향(가상 점은 여기에 그려진다). 빨간 선 = 패스스루 영상 속 컨트롤러가 보이는 방향. 카메라가 눈과 다른 자리에 있어 가까운 물체일수록 두 방향이 벌어진다.')
    + figure(fig_principle(), '점의 값은 추적한 위치로 계산하고, 화면의 실물은 카메라 영상을 다시 그린 것이라 길이 다르다')

    + h2('5. 추적이 끊기는 자리')
    + how('위에서 본 그림이다. 초록 부채꼴 안이면 잘 잡혔고, 빨간 영역과 오른쪽 상자의 자세에서 놓쳤다. 놓친 동안에는 앱이 점을 받지 않는다(끝 구슬이 빨갛게 바뀜).')
    + figure(fig_tracking(), '2026-10-15 10:41~10:46 기기 로그 · 놓침 약 20회 · 이번 한 판에서 본 경향(범위를 정해 잰 것 아님)')
    + p('이것은 앱이 아니라 기기 추적기의 한계다. 헤드셋 카메라가 컨트롤러의 적외선 LED를 보고 위치를 잡는데, 놓친 구간의 시스템 로그에는 맞춘 LED가 0~0.4개로 찍혔다.')

    + h2('6. 측정할 때')
    + cards([('실물 촉 기준', '화면의 노란 점이 아니라 촉 끝을 점에 댄다'), ('정면 시야 안', '컨트롤러를 헤드셋 앞쪽에 둔다'),
             ('링이 헤드셋 쪽', '컨트롤러 윗면이 헤드셋을 보게'), ('멈추고 당기기', '점에 대고 잠깐 멈춘 뒤 트리거')])

    + foot('시험 기기 Meta Quest 3 · 앱 v1.42(선 실측, 커밋 7171a01) · 끝점 값 (−7.9, 10.0, 43.4)mm. 자료: 기기 로그(logcat)와 기기 녹화 3개(10:25 · 10:33 · 10:41). '
           '확인하지 않은 것: 헤드셋을 벗어 두고 찍는 방식(착용 센서로 추적이 멈추는지) · 눈금 자 · 각도기와의 비교 · 환자 머리 주변 실제 자세에서의 추적 범위.')
)
print(save(body, '컨트롤러 끝점 정밀도 시험', O, 'CoCoChuna_경추ROM_선실측_끝점정밀도시험_V0.2_20261015', shot_h=3400))
