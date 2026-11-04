# 사업화 10월 페이지(사용자 양식) 5주차에 10/26 환자 팔 IK 연구 기록을 넣는다(2026-10-26 사용자 지시 «분석 · 테스트 내용을 원노트에»).
# ★사용자 판정(10-26): 관절을 자유롭게 잡고 움직이는 대응은 지금 추나 실습에 넣기엔 너무 고도화된 기능 — 연구만 하고 실습 적용은 하지 않는다.
# ★기존 글은 건드리지 않는다. 5주차 일정표가 머리줄만 있는지 확인하고, 아니면 멈춘다.
# 사용: patch_biz_1026.py <입력.xml> <출력.xml>
import sys, os, base64, copy
HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)
from onenote_lib import oe, oe_table, cell, table, detail_table

from xml.etree import ElementTree as ET
from onenote_lib import NS
N = "{" + NS + "}"
ET.register_namespace("one", NS)
import re, html


def own(o):
    s = "".join(x.text or "" for x in o.findall(N + "T"))
    return re.sub("<[^>]+>", "", html.unescape(s)).strip()


def cell_text(c):
    return " ".join(own(o) for o in c.iter(N + "OE")).strip()


def frag(xml_str):
    return ET.fromstring(f'<root xmlns:one="{NS}">{xml_str}</root>')[0]


DAY = ("10/26", [
    "[10/26] 환자 팔 IK(연구) — 능동 한계(근긴장)와 수동 한계(관절 구조)를 나누어 다루는지 점검 → 나누지 않는다(관절마다 벽 하나 · 외부 힘 개념 없음)",
    "[10/26] 어깨 외회전 · 수평 외전 시연(앙와위 · 팔 옆 90° · 팔꿈치 90°) — 외회전 90°는 맞고, 한계 넘김 · 수평 외전에서 문제(아래 세부 진행항목 · 캡처 ① ~ ③)",
    "[10/26] 능동 · 수동 가동범위 문헌 조사 — 명세 V0.1",
    "[10/26] 능동 · 수동 한계 구조를 시험 코드에 구현 — 능동 한계 너머는 끄는 만큼 구조 한계로 다가가고, 구조 한계는 넘지 않는다(락) · 가까워지면 부상 위험 상태",
    "[10/26] 판정 — 관절을 자유롭게 잡고 움직이며 반응하는 기능은 지금 추나 실습에 넣기엔 너무 고도화된 기능 → 연구로만 두고 실습에는 적용하지 않는다",
])

DETAIL = [
  ("환자 팔 IK\n능동 · 수동 한계\n(10/26 · 연구)", [
    ("완료", "[10/26] 지금 한계 구조 점검", [
      "관절마다 한계가 하나(벽)다 — 값은 대부분 수동(구조) 값이고 능동 한계 개념이 없다",
      "손으로 잡으면 환자 손이 내 손 자리만 따라가고 한계에서 잘린다 — 세게 끌든 살살 끌든 결과가 같다(외부 힘 개념 없음)",
      "10/23에 넣은 외회전 끝 «지수형 비용»은 팔꿈치 자리를 고를 때만 쓰인다(힘에 대한 저항이 아니다)"]),
    ("완료", "[10/26] 어깨 외회전 · 수평 외전 시연(에디터 Play · 오른팔 · 9단계 32초)", [
      "외회전 90°(손이 머리 쪽 침대로) → 잰 회전 −90°로 맞다(캡처 ①)",
      "외회전 140°(한계를 넘겨 요청) → −112°에서 버티지 않고 어깨가 뒤로 54° 빠지며 팔이 침대 아래로 떨어졌다(손 오차 227mm · 캡처 ②) — 한계에서 버티지 않고 다른 관절로 피한다",
      "수평 외전 30° · 60° → 위팔이 거상 49 ~ 53°로 발 쪽으로 내려가 재현되지 않았다(캡처 ③) — 시연이 손 자리만 주고 팔꿈치 자리는 IK가 고르기 때문",
      "영상: D:/CoCoChuna_환자팔IK_어깨외회전수평외전시연_20261026.mp4 (왼쪽 = 발 쪽에서 · 오른쪽 = 바깥 옆에서)"]),
    ("완료", "[10/26] 능동 · 수동 가동범위 문헌 조사", [
      "같은 사람을 두 방법으로 재면 수동이 능동보다 몇 도만 더 간다",
      "팔꿈치 · 아래팔: +3 ~ 5°(Zwerus 2019 · 352명)",
      "어깨 회전: +5 ~ 17°(McCully 2005 · 16명) — 팔 옆 외회전 67 / 72 · 90° 외전 외회전 107 / 118 · 90° 외전 내회전 73 / 90(능동 / 수동)",
      "어깨 굴곡 · 외전: 유의한 차이 없음(Sabari 1998 · 초록) · 손목은 같은 사람 자료를 못 찾음",
      "지금 코드 값은 대부분 수동 값이다(굴곡 156 · 외전 146은 Inui 수동 · 팔꿈치 · 아래팔은 CDC · JOA 수동)",
      "끝범위로 다가가는 모양은 쌍곡형 포화를 추천(수동 한계를 넘지 않는다)",
      "명세: D:/CoCoChuna_환자팔_능동수동가동범위_명세_V0.1_20261026.md"]),
    ("완료", "[10/26] 능동 · 수동 한계 구조 구현(시험 코드 · 커밋 b5eccb3)", [
      "기존 한계 = 수동(구조) · 능동 = 수동 − 틈(어깨 경계 · 외회전 · 내회전 · 팔꿈치 · 아래팔 · 손목)",
      "능동 한계까지는 그대로 따라오고, 그 너머는 끄는 만큼 구조 한계로 다가간다 · 손을 늦추면 능동 범위로 돌아온다",
      "구조 한계는 넘지 않는다(락) — 실습 중 관절이 «빠져» 진행이 막히는 일이 없게(사용자 지시)",
      "구조 한계에 가까워지면(틈의 80%) 부상 위험 상태 — 표시만 하고 진행은 막지 않는다(화면 연결 안 함)",
      "틈 값은 0으로 둠(조사 값 반영 전) · Play 미검증"]),
    ("대기", "[10/26] 실습 적용 판정 — 연구로만 둔다", [
      "관절을 자유롭게 잡고 움직이며 반응하는 기능은 지금 추나 실습에 넣기엔 너무 고도화된 기능이라는 판정",
      "시험 메뉴 · 시험 코드로만 남긴다(실습 씬에는 안 붙어 있다 · 13개 술기 영향 없음)",
      "다시 꺼낼 때 할 것: 조사 값을 틈에 넣고 시연 다시 · 넘길 때 어깨가 빠지는 «도망» 고치기 · 수평 외전 시연 경로 · 부상 위험 화면 표시"]),
  ]),
]

SHOT_DIR = "D:/추나녹화/20261026/캡처"
SHOTS = [
  ("01_외회전90.png", "① 외회전 90° — 맞음", "아래팔이 천장 → 머리 쪽 침대로. 잰 회전 −90°."),
  ("02_외회전140_어깨빠짐.png", "② 외회전 140° 요청 — 어깨가 뒤로 빠짐", "회전 −112°에서 버티지 않고 어깨가 뒤로 54° 빠져 팔이 침대 아래로 떨어졌다."),
  ("03_수평외전60_재현안됨.png", "③ 수평 외전 60° 요청 — 재현 안 됨", "위팔이 침대 모서리 아래로 가지 않고 거상 49° 쪽으로 내려갔다."),
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
        rows.append([cell([oe("10/26")]), cell([image_oe(os.path.join(SHOT_DIR, fname), 900.0), oe(title, bold=True), oe(cap)])])
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
tb, rows = scheds[4]
if any(cell_text(c) for r in rows[1:] for c in r.findall(N + "Cell")):
    raise SystemExit("5주차 일정표가 비어 있지 않다 — 멈춘다")


def set_cell(c, lines):
    """빈 칸의 문단을 지우고 새 문단을 넣는다(칸 자체 · 너비는 그대로)."""
    kids = c.find(N + "OEChildren")
    for o in list(kids):
        kids.remove(o)
    for line in lines:
        kids.append(frag(oe(line)))


# 빈 행 다섯에 날짜 10/26 ~ 10/30 · 내용은 10/26만(이 페이지 1주차의 «8/3 ~ 8/7» 틀은 건드리지 않는다)
for row, d in zip(rows[1:], ["10/26", "10/27", "10/28", "10/29", "10/30"]):
    cs = row.findall(N + "Cell")
    set_cell(cs[0], [d])
    if d == DAY[0]:
        set_cell(cs[1], DAY[1])

parent = None
for oec in root.iter(N + "OEChildren"):
    for i, o in enumerate(list(oec)):
        if o.find(N + "Table") is tb:
            parent, idx = oec, i
if parent is None:
    raise SystemExit("일정표 문단의 부모를 못 찾았다 — 멈춘다")
parent.insert(idx + 1, frag(oe("세부 진행항목", bullet=True)))
parent.insert(idx + 2, frag(detail_table(DETAIL)))
parent.insert(idx + 3, frag(oe("화면 캡처 — 어깨 외회전 · 수평 외전 시연(10/26)", bullet=True)))
parent.insert(idx + 4, frag(shots_table()))

s = ET.tostring(root, encoding="unicode")
open(sys.argv[2], "w", encoding="utf-8").write('<?xml version="1.0"?>' + s)
print("ok")
