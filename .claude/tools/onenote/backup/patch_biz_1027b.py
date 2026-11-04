# 사업화 10월 5주차 10/27 퇴근 — 오후 작업(실제 Quest 스토어 디자인 분석 · 시안 V1.1 → V1.4)을 덧붙인다.
# 기존 글 · 사용자가 17:03에 붙인 «Meta Horizon OS v2.7 UI» 메모는 건드리지 않는다(라이브 XML을 그대로 고쳐 쓴다).
# 10/27 칸 끝에 줄 추가 · 세부 진행항목 표(10/26 환자 팔 IK로 시작) 끝에 새 묶음 · V0.8 그림 표 바로 뒤에 그림 ⑫ ~ ⑱.
# 사용: patch_biz_1027b.py <입력.xml> <출력.xml> <그림 폴더>
import sys, os, re, html, base64
HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)
from onenote_lib import oe, detail_table, oe_table, table, cell, NS
from xml.etree import ElementTree as ET
N = "{" + NS + "}"
ET.register_namespace("one", NS)
SHOT_DIR = sys.argv[3]


def own(o):
    s = "".join(x.text or "" for x in o.findall(N + "T"))
    return re.sub("<[^>]+>", "", html.unescape(s)).strip()


def cell_text(c):
    return " ".join(own(o) for o in c.iter(N + "OE")).strip()


def frag(xml_str):
    return ET.fromstring(f'<root xmlns:one="{NS}">{xml_str}</root>')[0]


DAY = [
    "[10/27 오후] 실제 Quest 3S를 USB로 연결해 기기 스토어 화면을 직접 찍어 디자인 분석 — 틀은 흑백 · 강조 = 흰 알약 · 카드 바탕 · 구분선 없음 · 색은 사진과 뜻에만(그림 ⑫)",
    "[10/27 오후] 시안 V1.1 → V1.4(사용자 결정 반영) — 로비 격자 유지 · 로그인 한 판(이름 누르면 로그인) · 진입 전 메뉴(난이도 → 환경 → [실습]/[평가] 누르면 진입) — 그림 ⑬ ~ ⑱",
    "[10/27 오후] 구현은 시안 확정 뒤 다시 맞춤 — 지금 에디터 로비는 V1.0 단계(회색 판 · 흰 알약) · 실습 씬 미적용 · 미커밋 · Play 미검증",
]

GROUP = ("UI 개편\n(10/27 오후 · 실제 Quest)", [
    ("완료", "[10/27 오후] 실제 Quest 화면 디자인 분석 — «SDK 기준은 실제 로비와 다르다 · 실제 UI를 분석하라»(사용자)", [
        "사용자 Quest 3S를 USB로 연결 · Quest 자체 스크린샷(adb)으로 스토어 첫 화면을 찍고 창 네 모서리로 원근을 펴서 1024 × 640으로 분석",
        "앞서 쓴 2024년 스토어 그림(남색 판 · 4열)은 구형 UI라 버림(사용자 지적)",
        "틀은 흑백(판 #363636 → 아래로 어둡게) · 파랑 같은 강조색 없음",
        "고른 탭 · 주 버튼 · 꼬리표 = 흰 알약 + 거의 검은 글자 / 안 고른 탭 = 글자만",
        "카드 바탕 · 구분선 · 테두리 · 그림자 없음 — 여백과 글자 굵기로 묶음 · 색은 사진과 뜻(할인 초록 · 브랜드 보라)에만",
        "D:/CoCoChuna_Quest스토어_디자인분석_V1.0_20261027.pdf"]),
    ("완료", "[10/27 오후] 결정 사항(사용자)", [
        "강조색 = Meta 파랑 대신 실제 화면대로 흰 알약",
        "로비: 배너 없음 · 카드는 지금처럼 격자(4열) · 배지 없음",
        "로그인: 한 판 — 왼쪽 조 목록 · 오른쪽 넓은 학생 격자 · 이름을 누르면 바로 로그인(단계 넘김 · 뒤로 버튼 없음)",
        "시나리오 진입 전 메뉴: ① 실습 난이도(초급 · 중급 · 상급) → ② 실습 환경(VR 가상환자 · XR 실제 사람 = 준비 중) → ③ 실습 모드 [실습] · [평가]",
        "[실습] · [평가] 중 하나를 누르면 바로 진입 — 시작 버튼 없음",
        "시안 문서는 새 판을 내면 이전 판을 지운다(헷갈림 방지)"]),
    ("진행중", "[10/27 오후] 시안 V1.4 — 실제 Quest 디자인판(그림 ⑬ ~ ⑱)", [
        "로비 · 진입 전 메뉴 · 로그인 안내 · 로그인(조 · 이름 한 판) · 실습 상단 바 · 설정 리모컨",
        "D:/CoCoChuna_UI개편_시안_V1.4_20261027.pdf"]),
    ("대기", "[10/27 오후] 답 대기", [
        "[평가]일 때 ① 난이도 처리 — 지금 앱은 평가에 난이도를 쓰지 않는다",
        "흉추교정2 부제 «골곡» 표기 확인(받은 목록 그대로 씀)"]),
    ("대기", "[10/27 오후] 구현 — 시안 확정 뒤", [
        "에디터 로비는 V1.0 단계 — V1.4로 다시 맞춤(로그인 한 판 · 진입 전 메뉴 3줄은 새로 만듦)",
        "실습 씬(상단 바 · 리모컨)은 아직 씬에 적용 안 함",
        "미커밋 · Play 미검증"]),
])

SHOTS = [
    ("0_스토어분석.png", "⑫ 실제 Quest 스토어 첫 화면(10/27 · 사용자 기기 · 원근 보정)", "주황 번호 = 구성요소(고른 탭 · 검색 · 주 버튼 · 카드 · 배지 · 할인 가격 등) — 분석 문서 표와 같은 번호"),
    ("1_로비.png", "⑬ 로비 — 배너 없음 · 격자 카드", "고른 탭 흰 알약 · 나머지 글자만 · 카드 바탕 없음 · 판 안 세로 스크롤"),
    ("2_진입전메뉴.png", "⑭ 시나리오 진입 전 메뉴", "① 난이도 → ② 환경(XR 준비 중) → ③ [실습] · [평가] 누르면 바로 진입"),
    ("3_로그인안내.png", "⑮ 로그인 안내", "글자와 알약만 · [로그인] 흰 알약 · [닫기] 옅은 알약"),
    ("4_로그인.png", "⑯ 로그인 — 조 · 이름 한 판", "왼쪽 조 목록 · 오른쪽 학생 격자 · 이름 누르면 바로 로그인 · 긴 이름 두 줄"),
    ("5_상단바.png", "⑰ 실습 상단 바", "어두운 반투명 알약 · 켜진 것 흰 알약 · 나머지 글자만"),
    ("6_리모컨.png", "⑱ 설정 리모컨", "[완료] 흰 알약 · 나머지 옅은 알약 · 닫기 없음"),
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
        rows.append([cell([oe("10/27")]), cell([image_oe(os.path.join(SHOT_DIR, fname), 700.0), oe(title, bold=True), oe(cap)])])
    return oe_table(table([48.0, 1010.0], rows))


tree = ET.parse(sys.argv[1])
root = tree.getroot()

# 1) 5주차 일정표 10/27 칸 끝에 덧붙임(낮에 쓴 4줄이 있어야 한다)
scheds = [tb for tb in root.iter(N + "Table")
          if (r := tb.findall(N + "Row")) and [cell_text(c) for c in r[0].findall(N + "Cell")] == ["일", "수행항목"]]
if len(scheds) != 5:
    raise SystemExit(f"일정표가 {len(scheds)}개다(5개 기대) — 멈춘다")
row = [r for r in scheds[4].findall(N + "Row")[1:] if cell_text(r.findall(N + "Cell")[0]) == "10/27"]
if len(row) != 1:
    raise SystemExit("5주차 10/27 행을 못 찾았다 — 멈춘다")
c1 = row[0].findall(N + "Cell")[1]
if "[10/27] UI 개편 요청 6건" not in cell_text(c1) or "[10/27 오후]" in cell_text(c1):
    raise SystemExit("10/27 칸이 낮에 쓴 상태가 아니다 — 멈춘다")
kids = c1.find(N + "OEChildren")
for line in DAY:
    kids.append(frag(oe(line)))

# 2) 세부 진행항목 표 끝에 새 묶음
dts = [tb for tb in root.iter(N + "Table")
       if (r := tb.findall(N + "Row")) and [cell_text(c) for c in r[0].findall(N + "Cell")] == ["분류", "상태", "항목", "세부 내용"]
       and "능동 · 수동 한계" in cell_text(r[1].findall(N + "Cell")[0]) if len(r) > 1]
if len(dts) != 1:
    raise SystemExit(f"세부 진행항목 표가 {len(dts)}개다(1개 기대) — 멈춘다")
new_rows = frag(detail_table([GROUP])).find(N + "Table").findall(N + "Row")[1:]
for r in new_rows:
    dts[0].append(r)

# 3) V0.8 그림 표 바로 뒤에 그림 ⑫ ~ ⑱
parent = None
for oec in root.iter(N + "OEChildren"):
    kids2 = list(oec)
    for i, o in enumerate(kids2):
        if "UI 개편 시안 V0.8" in own(o).replace(" ", " ") and i + 1 < len(kids2) and kids2[i + 1].find(N + "Table") is not None:
            parent, idx = oec, i + 1
if parent is None:
    raise SystemExit("V0.8 그림 표를 못 찾았다 — 멈춘다")
parent.insert(idx + 1, frag(oe("그림 — 실제 Quest 스토어 분석 · UI 개편 시안 V1.4(10/27 오후)", bullet=True)))
parent.insert(idx + 2, frag(shots_table()))

s = ET.tostring(root, encoding="unicode")
open(sys.argv[2], "w", encoding="utf-8").write('<?xml version="1.0"?>' + s)
print("ok", len(new_rows), "rows ·", len(SHOTS), "그림")
