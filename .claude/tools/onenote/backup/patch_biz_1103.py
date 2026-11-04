# 사업화 11월 1주차에 11/03 제2늑골 Play 진행 보고를 넣는다(틀 = patch_biz_1102.py).
# ★기존 글은 건드리지 않는다 — 11/3 칸이 비어 있는지 확인하고, 11/2 화면 캡처 표 바로 뒤에 세부표 · 캡처 표를 덧붙인다.
# ★사용자가 11/3 10:16~10:32에 쓴 «프로토콜 - 흉곽부 추나(2nd rib 교정)» + 그림 5장은 그대로 둔다.
# 사용: patch_biz_1103.py <입력.xml> <출력.xml>
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


DAY = ("11/3", [
    "[11/3] 제2늑골 — 교정 동작을 전문가 영상(1인칭 · 3인칭)에 맞춰 다시 만듦: 굽힌 팔을 머리 쪽으로 당기며 폈다가 다시 굽힘 ×3(V04 · 그림 ⑤)",
    "[11/3] 진행도를 «녹화 궤적의 가장 가까운 점» 기준으로 바꿈 · 가이드손이 사용자보다 조금 앞에서 따라감 · 가이드손 파지 159 → 106mm",
    "[11/3] 누운 환자 리모컨을 머리 쪽 베드 끝 위로(그림 ①) · 환자 옆 안내판 신설(단계 · 지시 · 유지 시간 · 진행 % · 반복 횟수)",
    "[11/3] Play 1판: 교정 3단계 끝까지 진행 · 직접 3회 왕복 6/6 · 3-7 채점 67점(C, 직전 판 35점) · 종합 86.7점(A)(그림 ②~④)",
    "[11/3] 남은 과제: 안내판 각도 · 안내판에서 버튼 누르기 · 결과값 표시 안 보임 · 안내 문구 짧게(반영 중) · 커밋 0",
])

DETAIL = [
  ("제2늑골 교정 단계\n(11/3)", [
    ("완료", "[11/3] 교정 동작 V04(그림 ⑤)", [
      "시작 = 3-4 «팔 외전» 끝 자세(팔꿈치 굽힘 · 손이 머리 쪽) → 머리 쪽으로 당기며 폄 → 다시 굽혀 돌아옴",
      "손 높이(어깨 기준) +7.0 → +10.7 → +8.2cm — 가운데만 약 4cm 솟는 포물선(리그 실측) · 끝 자세 오차 0.3°",
      "앞서 3인칭만 보고 «편 팔을 위아래로»로 잘못 이해했던 것을 1인칭 영상과 직전 클립 끝 자세로 바로잡음"]),
    ("완료", "[11/3] 진행도 · 가이드손 · 리모컨", [
      "진행도: 직선 투영 → 녹화 궤적 가장 가까운 점(pathProgress · 3-5 · 3-6 · 3-7) — 직선은 궤적이 휘면 멈추거나 되돌아갔음",
      "가이드손: 한 번 재생 후 사용자 진행보다 12% 앞에서 따라감 · 파지는 기존 녹화에서 환자 손에 가장 가까운 프레임(106mm · 악수 잡기는 아직)",
      "리모컨: 누운 환자는 머리 쪽 베드 끝 위 · 높이 1.24m(눈 1.67m) — 직전 판은 1.07m로 가슴 앞이었음(씬 값 변경 · 저장 필요)"]),
    ("진행중", "[11/3] 환자 옆 안내판 · Play 1판(그림 ②~④)", [
      "3-5 5.9초 · 3-6 5.4초 · 3-7 23.1초 · 3-7 구간 터치 6/6(끝 96 · 97% / 시작 12 ~ 14%) · 67점(C) · 왼손 95% · 오른손 51%",
      "안내판이 단계 시작 때 한 번 놓여 팔 쪽으로 옮기면 비스듬히 보임 → 사용자 쪽으로 돌리기",
      "버튼은 정면 큰 화면에만 있음 → 안내판에서 누르게 · 종료 후 결과값이 안 보임(그림 ④) → 안내판에 표시",
      "지시문 전체를 그대로 보여 줘 길다 → 할 일을 짧게",
      "문서: D:/CoCoChuna_제2늑골_교정단계_Play진행보고_V0.1_20261103.pdf · Play 1판 결과 · 다른 술기 안내판은 미확인"]),
  ]),
]

SHOT_DIR = "D:/추나녹화/20261103/캡처"
SHOTS = [
  ("01_리모컨_머리쪽_안내판.png", "① 누운 환자 리모컨 — 머리 쪽 베드 끝 위(0:08)", "리모컨 옆에 환자 옆 안내판이 함께 보인다."),
  ("02_3-5_가이드손_진행.png", "② 3-5 당겨 펴기 — 가이드손이 환자 손을 잡고 진행 54%(1:12)", "안내판 마지막 줄이 진행 %다."),
  ("03_3-7_직접반복.png", "③ 3-7 직접 3회 — 가이드손 없음(1:30)", "왕복 6/6이 세어져 넘어갔다 · 67점(C)."),
  ("04_종료_결과화면.png", "④ 종료 — 왼쪽 종료 안내(1:58) · 오른쪽 직후 정면 화면", "정면 화면이 비어 보인다. 로그에는 종합 86.7점(A)이 찍혀 있다."),
  ("05_V04_당겨펴기_졸라맨.png", "⑤ 새 팔 동작 V04 — 시작 → 끝(리그 실측 졸라맨)", "굽힌 팔에서 머리 쪽으로 당기며 편다 · 끝 팔꿈치 약 9°."),
]


def png_size(path):
    with open(path, "rb") as f:
        head = f.read(24)
    if head[:8] != b"\x89PNG\r\n\x1a\n":
        raise SystemExit(f"PNG가 아니다: {path}")
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
        rows.append([cell([oe("11/3")]), cell([image_oe(os.path.join(SHOT_DIR, fname), 800.0), oe(title, bold=True), oe(cap)])])
    return oe_table(table([48.0, 1010.0], rows))


tree = ET.parse(sys.argv[1])
root = tree.getroot()
scheds = []
for tb in root.iter(N + "Table"):
    rows = tb.findall(N + "Row")
    if rows and [cell_text(c) for c in rows[0].findall(N + "Cell")] == ["일", "수행항목"]:
        scheds.append((tb, rows))
if len(scheds) != 5:
    raise SystemExit(f"일정표가 {len(scheds)}개다(5개 기대) — 멈춘다")
tb, rows = scheds[0]
target = None
for row in rows[1:]:
    cs = row.findall(N + "Cell")
    if cell_text(cs[0]) == DAY[0]:
        target = cs
if target is None:
    raise SystemExit("11/3 줄이 없다 — 멈춘다")
if cell_text(target[1]):
    raise SystemExit("11/3 칸이 비어 있지 않다 — 멈춘다")
kids = target[1].find(N + "OEChildren")
for o in list(kids):
    kids.remove(o)
for line in DAY[1]:
    kids.append(frag(oe(line)))

# 11/2 화면 캡처 표(머리 «화면 캡처 — 추나 베드 · 제2늑골 환자 동작(11/2)») 바로 뒤
parent = None
for oec in root.iter(N + "OEChildren"):
    lst = list(oec)
    for i, o in enumerate(lst):
        if own(o).startswith("화면 캡처 — 추나 베드") and i + 1 < len(lst) and lst[i + 1].find(N + "Table") is not None:
            parent, idx = oec, i + 1
if parent is None:
    raise SystemExit("11/2 화면 캡처 표를 못 찾았다 — 멈춘다")
parent.insert(idx + 1, frag(oe("세부 진행항목 — 제2늑골 교정 단계 Play(11/3)", bullet=True)))
parent.insert(idx + 2, frag(detail_table(DETAIL)))
parent.insert(idx + 3, frag(oe("화면 캡처 — 제2늑골 Play(11/3)", bullet=True)))
parent.insert(idx + 4, frag(shots_table()))

s = ET.tostring(root, encoding="unicode")
open(sys.argv[2], "w", encoding="utf-8").write('<?xml version="1.0"?>' + s)
print("ok")
