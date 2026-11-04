# 사업화 10월 5주차에 10/26 오후 기록(실제 사람 가이드 적용 제안 · ChatGPT 문서 확인 · 모델 구현 방침)을 덧붙인다.
# ★오전 패치(patch_biz_1026.py) 결과 위에 덧붙인다 — 기존 글은 건드리지 않고, 기대한 구조가 아니면 멈춘다.
# 사용: patch_biz_1026b.py <입력.xml> <출력.xml>
import sys, os, base64, re, html
HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)
from onenote_lib import oe, oe_table, cell, table, detail_table, NS
from xml.etree import ElementTree as ET
N = "{" + NS + "}"
ET.register_namespace("one", NS)


def own(o):
    s = "".join(x.text or "" for x in o.findall(N + "T"))
    return re.sub("<[^>]+>", "", html.unescape(s)).strip()


def cell_text(c):
    return " ".join(own(o) for o in c.iter(N + "OE")).strip()


def frag(xml_str):
    return ET.fromstring(f'<root xmlns:one="{NS}">{xml_str}</root>')[0]


DAY_ADD = [
    "[10/26 오후] ChatGPT 전달 문서 2건 확인 — 해부학 리그 c9 연결 검토 요청(목표가 «직접 잡고 움직이는 환자»라 오전 판정과 방향이 다름 → 연결 검토 보류) · 실제 사람 적용 설계안 V0.1",
    "[10/26 오후] 실제 사람에게 추나 가이드 적용(추가 장비 없음) — 제안 V0.1 → 추천안 V0.2 문서(아래 세부 진행항목 · 그림 ④ ⑤)",
    "[10/26 오후] 방침 — 환자 모델은 술기 영상 분석 → 관절에 맞춘 애니메이션으로 구현(자유 조작은 연구만) · 실제 사람 1단계 시험 = 상부승모근 전부(前部)",
]

GROUP = ("실제 사람 가이드\n적용(10/26 · 제안)", [
    ("완료", "[10/26] ChatGPT 전달 문서 확인", [
        "해부학 리그 전달문(Docs/AnatomicalPatient_ClaudeCode_Handoff_20261026.md) — 적힌 파일 10개 실재 · c8 좌우 반전 · b5eccb3 설명이 코드와 맞음(대조)",
        "목표가 «시술자가 직접 잡고 움직이는 환자»라 오전 판정(자유 조작은 연구만)과 다름 → c9 연결 검토는 보류",
        "실제 사람 적용 설계안 V0.1(Docs/RealPersonGuide_V0.1_20261026) — 시작 파지 등록 + 지도자 목표 등록"]),
    ("완료", "[10/26] 지금 판정이 무엇을 기준으로 하나 점검(코드)", [
        "시작 = 가상환자 충돌체에 손이 닿고 양손이 3초 멈춤",
        "가이드 손 · 경로 근접도 = 녹화를 가상환자 가슴 기준으로 되살림",
        "진행률 = 가상환자 회전 중심 → 손 각도 ÷ 녹화한 끝 각도",
        "보조수 흔들림 = 자기 시작 자리에서 15cm — 사람과 무관",
        "→ 환자 모델만 끄면 판정 기준이 실제 사람과 다른 자리에 남는다"]),
    ("완료", "[10/26] 제안 V0.1", [
        "시작 파지 2초로 숨은 가상환자 전체(충돌체 · 회전 중심 · 가이드 손)를 실제 손 자리로 옮김",
        "손 여유 한 값 · 각도는 녹화 끝의 % 배율(좁게 / 보통 / 넓게) · ChatGPT 안과 비교",
        "D:/CoCoChuna_추나가이드_실제사람_맞추기_제안_V0.1_20261026.pdf"]),
    ("완료", "[10/26] 추천안 V0.2 — «사람마다 목 길이 · 어깨 너비가 다른데 여유로 다 되나»", [
        "여유로는 흔들림(손 떨림 · 피부)만 받는다 — 체형 차이는 못 받는다",
        "회전 중심이 2cm 어긋나면 잰 각이 −16% ~ +23% 달라진다(계산 · 그림 ④)",
        "각도는 양손을 잇는 선의 회전으로 잰다 — 회전 중심 · 목 길이를 몰라도 된다(그림 ⑤)",
        "판정을 모델에서 떼어 «기준점 + 관절 움직임 + 목표 %»로 정의 — 가상환자는 모델 뼈, 실제 사람은 짚어서 기준점을 받는다",
        "조작: 체형 맞추기(사람당 1회 · 어깨 끝 → 귀 앞) · 시작 파지 2초 · [다시 맞추기]",
        "D:/CoCoChuna_추나가이드_실제사람_추천안_V0.2_20261026.pdf"]),
    ("대기", "[10/26] 1단계 시험 — 상부승모근 전부(前部)", [
        "환측회전(양손 선 수평 회전) · 전부측굴(양손 선 기울기) · 등척성운동(시작 파지 맞춤 · 유지만)",
        "전부에서 통하면 중부 → 후부 순으로 넓힌다",
        "먼저 확인할 것: 파지할 때 손이 머리 밑에 들어가 헤드셋 카메라에 가려지는지",
        "미구현 · Play 미검증"]),
    ("완료", "[10/26] 환자 모델 구현 방침", [
        "각 술기 영상을 분석해 관절에 맞는 애니메이션으로 구현한다",
        "관절을 자유롭게 잡고 움직이는 IK는 연구로만 둔다(오전 판정)"]),
])

SHOT_DIR = r"C:/Users/USER/AppData/Local/Temp/claude/C--UnityProjects-GuideChuna/5056d731-6a54-4ddd-a8ac-2446d04b86bc/scratchpad/realperson"
SHOTS = [
    ("on_fig1.png", "④ 회전 중심이 어긋나면 잰 각이 달라진다(계산)", "손이 중심에서 10cm · 실제 30° — 중심을 2cm 깊게 잡으면 25°, 얕게 잡으면 37°."),
    ("on_fig2.png", "⑤ 양손 선은 머리와 같은 각도로 돈다(원리 그림)", "회전 중심이 어디든 두 손을 잇는 선이 30° 돈다 — 체형과 무관하게 각도를 잰다."),
]


def png_size(path):
    with open(path, "rb") as f:
        head = f.read(24)
    return int.from_bytes(head[16:20], "big"), int.from_bytes(head[20:24], "big")


def image_oe(path, w):
    pw, ph = png_size(path)
    h = round(w * ph / pw, 1)
    data = base64.b64encode(open(path, "rb").read()).decode("ascii")
    return ('<one:OE><one:Image format="png">'
            f'<one:Size width="{w}" height="{h}" isSetByUser="true"/>'
            f'<one:Data>{data}</one:Data></one:Image></one:OE>')


def shots_table():
    rows = [[cell([oe("날짜", bold=True)], "#F2F2F2"), cell([oe("화면", bold=True)], "#F2F2F2")]]
    for fname, title, cap in SHOTS:
        rows.append([cell([oe("10/26")]), cell([image_oe(os.path.join(SHOT_DIR, fname), 800.0), oe(title, bold=True), oe(cap)])])
    return oe_table(table([48.0, 1010.0], rows))


tree = ET.parse(sys.argv[1])
root = tree.getroot()

# 1) 5주차 일정표 10/26 칸에 덧붙임
scheds = [tb for tb in root.iter(N + "Table")
          if (r := tb.findall(N + "Row")) and [cell_text(c) for c in r[0].findall(N + "Cell")] == ["일", "수행항목"]]
if len(scheds) != 5:
    raise SystemExit(f"일정표가 {len(scheds)}개다(5개 기대) — 멈춘다")
row = [r for r in scheds[4].findall(N + "Row")[1:] if cell_text(r.findall(N + "Cell")[0]) == "10/26"]
if len(row) != 1:
    raise SystemExit("5주차 10/26 행을 못 찾았다 — 멈춘다")
kids = row[0].findall(N + "Cell")[1].find(N + "OEChildren")
if "[10/26 오후]" in cell_text(row[0].findall(N + "Cell")[1]):
    raise SystemExit("이미 오후 기록이 있다 — 멈춘다")
for line in DAY_ADD:
    kids.append(frag(oe(line)))

# 2) 오전에 넣은 세부 진행항목 표(환자 팔 IK 묶음)에 새 묶음 행을 덧붙임
dts = [tb for tb in root.iter(N + "Table")
       if (r := tb.findall(N + "Row")) and [cell_text(c) for c in r[0].findall(N + "Cell")] == ["분류", "상태", "항목", "세부 내용"]
       and "능동 · 수동 한계" in cell_text(r[1].findall(N + "Cell")[0]) if len(r) > 1]
if len(dts) != 1:
    raise SystemExit(f"10/26 세부 진행항목 표가 {len(dts)}개다(1개 기대) — 멈춘다")
new_rows = frag(detail_table([GROUP])).find(N + "Table").findall(N + "Row")[1:]
for r in new_rows:
    dts[0].append(r)

# 3) 오전 캡처 표 바로 뒤에 그림 ④ ⑤
cap_tb = [tb for tb in root.iter(N + "Table") if re.sub(r"\s", "","".join(own(o) for o in tb.iter(N + "OE"))).count("재현안됨") >= 1 and "날짜" in cell_text(tb.findall(N + "Row")[0].findall(N + "Cell")[0])]
if len(cap_tb) != 1:
    raise SystemExit("오전 캡처 표를 못 찾았다 — 멈춘다")
parent = None
for oec in root.iter(N + "OEChildren"):
    for i, o in enumerate(list(oec)):
        if o.find(N + "Table") is cap_tb[0]:
            parent, idx = oec, i
if parent is None:
    raise SystemExit("캡처 표 문단의 부모를 못 찾았다 — 멈춘다")
parent.insert(idx + 1, frag(oe("그림 — 실제 사람 가이드 추천안 V0.2(10/26)", bullet=True)))
parent.insert(idx + 2, frag(shots_table()))

s = ET.tostring(root, encoding="unicode")
open(sys.argv[2], "w", encoding="utf-8").write('<?xml version="1.0"?>' + s)
print("ok", len(new_rows), "rows")
