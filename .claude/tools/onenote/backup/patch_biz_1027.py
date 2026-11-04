# 사업화 10월 5주차 10/27 — UI 개편 요청 · 시안(V0.1 → V0.8) · Meta UI 규격 실측 · 구현 상태를 적는다.
# ★10/27 칸이 비어 있어야 한다(아니면 멈춘다). 10/26 세부 진행항목 표 끝에 새 묶음을 덧붙이고, 그 표 바로 뒤에 시안 V0.8 그림 6장을 넣는다.
# 사용: patch_biz_1027.py <입력.xml> <출력.xml> <그림 폴더>
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
    "[10/27] UI 개편 요청 6건(사용자) — 로비(Meta Quest UI · 패스스루 기본) · 로그인 · 조/이름 선택 · 한방병원 배경 · 모드 선택(VR/XR × 실습/평가) · 실습 상단 바 + 정보 디스플레이 펼침 · 실습 안 현실모드/골격/모델 버튼 삭제 · 설정 = 위치 조정 리모컨",
    "[10/27] 시안 V0.1 → V0.7(결정 반영) → Meta UI(UISet) 규격 실측 → 시안 V0.8(Meta 규격판) — 아래 세부 진행항목 · 그림 ⑥ ~ ⑪",
    "[10/27] 명칭 통일 — 복잡추나 7종을 사용자 목록으로(흉추교정1 · 2 · 늑골교정1 · 2) · 카드 · 실습 제목 · 결과표 · 미러링이 한 표(ScenarioDisplayNames)를 씀",
    "[10/27] 구현은 에디터에서 진행 중(Play 미검증 · 미커밋) — 리모컨 · 상단 바 · 펼침 · 모드 팝업 · 로비 카드/머리줄 · 한방병원 홀 기본형 · A/B 비교안",
]

GROUP = ("UI 개편\n(10/27 · 시안)", [
    ("완료", "[10/27] 요청 6건 + 추가 지시", [
        "① 로비: Meta Quest 기본 UI · 배경 기본 패스스루 · 카드 = 사진/글자 분리 · 우측 상단 이름 · 도움말 · 패스스루 켬/끔 · 종료 ✕(기존 유지)",
        "② 로그인: 안내 팝업 위치 · 디자인 · 사용자 상태 = 이름 · 조/이름 선택 개편(조 · 이름이 계속 늘어남 대응)",
        "③ 실습 배경: 한의과 · 한방병원 느낌의 넓은 공간(가까운 곳은 채우지 않음) · 참고 서울한방진흥센터",
        "④ 로비에서 시나리오 선택 → 모드 팝업 4종: VR 실습 · VR 평가 · XR 실습 · XR 평가(XR = 실제 사람)",
        "⑤ 실습 상단 바(눈높이 위 30° · 고정) · 정보 디스플레이는 배타 토글 대신 최근 것 정면 · 나머지 좌우로 펼침",
        "⑥ 실습 안 현실 모드 · 골격 표시 · 모델 표시 버튼 삭제",
        "추가: 설정 = 위치 조정 리모컨(자동 맞춤 · 위 · 아래 · 좌 · 우 · 앞 · 뒤 · 완료 · 사용자 앞 약간 아래 · 정보창과 별개 · 위아래로 넓지 않게 · 닫기 ✕ 없음)",
        "추가: 전반 동작 · 넘침 처리 · 디자인은 Meta Quest를 따른다(넘치면 판 안 세로 스크롤)"]),
    ("완료", "[10/27] 결정 사항", [
        "XR 칸 = «준비 중»으로 막음 · 난이도 = 모드 팝업에서 고름 · 펼침 = 새 판 정면 · 먼저 켠 판 왼쪽 → 오른쪽",
        "거리(제안값 확정): 로비 1.2m · 로그인 팝업 0.9m · 상단 바 앞 1.0m 위 30° · 리모컨 앞 0.5m 아래 0.3m",
        "로그인: 검색 없음(«찾는 게 빠르다») · 최근 사용자 표시 없음 · 긴 이름 말줄임 금지(두 줄) · 동명이인은 입력할 때 구분",
        "리모컨 방향 = 움직이는 모델 자신의 축 · 짧게 1cm · 길게 계속",
        "로비 검색 없음 · 탭 · 숨긴 카드 13개는 그대로",
        "배경 에셋 분담: 홀 껍데기 · 조명 · 기둥 = Claude(Unity) / 창살 창 · 서까래 · 약장 · 단독 추나 베드 · 먼 소품 = ChatGPT(Blender)"]),
    ("완료", "[10/27] Meta UI(UISet) 규격 실측 — «Meta UI를 분석하고 진행»(사용자)", [
        "SDK v81 UISet 프리팹 48종을 띄워 크기 · 여백 · 모서리 · 글자 · 색을 잼(5,871줄)",
        "8px 격자(여백 24 · 간격 8 · 아이콘 24) · 버튼 높이 40 · 글자 14/11 · 알약 모서리 · 타일 176 × 100 · 대화상자 폭 368",
        "Quest 다크 테마엔 파란 강조색이 없다 — 주 버튼 흰색 · 보조 버튼 회색",
        "이전 적용이 «느낌만 비슷»했던 원인 = 버튼 · 글자가 Meta보다 2 ~ 4배 컸다",
        "D:/CoCoChuna_MetaUI규격실측_V0.1_20261027.pdf"]),
    ("진행중", "[10/27] 시안 V0.8 — Meta 규격판(그림 ⑥ ~ ⑪)", [
        "로비: Meta 앱 화면 1024 × 688 · 카드 사진 176 × 99 + 제목 14 / 부제 11(두 줄) · 탭 2 · 이름 · ? · ◐ · ✕",
        "로그인 안내: Meta 대화상자 그대로(폭 368 · 아이콘 48 · 제목 20 · 버튼 2개 156 × 40)",
        "조 · 이름: 한 판 두 단계 600 × 520 · 조 타일 176 × 100 · 이름 타일 176 × 64 · 3열 · 세로 스크롤",
        "모드 팝업: 600 × 456 · 모드 타일 270 × 100 · XR 흐림 + 주황 «준비 중» · 고른 난이도 = 흰 버튼",
        "상단 바: Meta 선반 토글 101 × 72 × 5 + 구분선 · 리모컨: 높이 40 버튼 두 줄(736 × 160)",
        "D:/CoCoChuna_UI개편_시안_V0.8_20261027.pdf"]),
    ("대기", "[10/27] 색 결정 → V0.8 그대로 구현", [
        "Meta 색(고른 것 = 흰 버튼) 또는 색 남긴 안(VR 파랑 · XR 주황 · 켜진 버튼 파랑) — 모드 팝업 · 상단 바",
        "결정 뒤: V0.8 숫자 그대로 구현 → 시안과 실제 캡처를 겹쳐 비교"]),
    ("진행중", "[10/27] 구현(에디터 · Play 미검증 · 미커밋)", [
        "실습 씬: 설정 리모컨 · 상단 바 · 정보 창 3개 펼침 · 옛 설정 버튼 3개는 리모컨으로 대체",
        "로비: 카드 사진/글자 분리 · 머리줄(이름 · ? · ◐ · ✕) · 모드 팝업 · 명칭 통일",
        "A/B 비교안 두 벌(시안 색 / Meta 기본 색) — D:/CoCoChuna_UI개편_AB비교_V0.1_20261027.pdf",
        "한방병원 홀 기본형 프리팹(20 × 10 × 5m · 삼각형 1,488) — 추나 베드 분리 뒤 씬에 배치",
        "남은 것: V0.8 규격으로 다시 맞추기 · 로그인 안내 · 조/이름 판 디자인 적용"]),
])

SHOTS = [
    ("1_로비.png", "⑥ 로비 — Meta 앱 화면 규격(V0.8)", "카드 = 사진 176 × 99 + 제목 14 / 부제 11 · 치수(주황) = Meta 캔버스 px"),
    ("2_로그인안내.png", "⑦ 로그인 안내 — Meta 대화상자", "폭 368 · 아이콘 48 · 제목 20 · 버튼 156 × 40"),
    ("3_조이름선택.png", "⑧ 조 · 이름 선택 — 한 판 두 단계", "타일 3열 · 넘치면 세로 스크롤 · 검색 없음 · 긴 이름 두 줄"),
    ("4_모드팝업.png", "⑨ 모드 선택 팝업 — 오른쪽 작은 그림 = 색 남긴 안", "XR 두 칸 «준비 중»(막음) · 난이도는 실습만"),
    ("5_상단바.png", "⑩ 실습 상단 바 — 위 Meta 색 · 아래 색 남긴 안", "Meta 선반 토글 101 × 72 · 콘텐츠 3 | 메뉴 2"),
    ("6_리모컨.png", "⑪ 설정 리모컨", "높이 40 버튼 두 줄 · 닫기 없음 · 사용자 앞 0.5m · 아래 0.3m"),
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
        rows.append([cell([oe("10/27")]), cell([image_oe(os.path.join(SHOT_DIR, fname), 700.0), oe(title, bold=True), oe(cap)])])
    return oe_table(table([48.0, 1010.0], rows))


tree = ET.parse(sys.argv[1])
root = tree.getroot()

# 1) 5주차 일정표 10/27 칸(빈 칸이어야 한다)
scheds = [tb for tb in root.iter(N + "Table")
          if (r := tb.findall(N + "Row")) and [cell_text(c) for c in r[0].findall(N + "Cell")] == ["일", "수행항목"]]
if len(scheds) != 5:
    raise SystemExit(f"일정표가 {len(scheds)}개다(5개 기대) — 멈춘다")
row = [r for r in scheds[4].findall(N + "Row")[1:] if cell_text(r.findall(N + "Cell")[0]) == "10/27"]
if len(row) != 1:
    raise SystemExit("5주차 10/27 행을 못 찾았다 — 멈춘다")
c1 = row[0].findall(N + "Cell")[1]
if cell_text(c1):
    raise SystemExit(f"10/27 칸이 비어 있지 않다 — 멈춘다: {cell_text(c1)[:40]}")
kids = c1.find(N + "OEChildren")
first = kids.find(N + "OE")
kids.remove(first)
for line in DAY:
    kids.append(frag(oe(line)))

# 2) 세부 진행항목 표(10/26 · 환자 팔 IK로 시작하는 표) 끝에 새 묶음
dts = [tb for tb in root.iter(N + "Table")
       if (r := tb.findall(N + "Row")) and [cell_text(c) for c in r[0].findall(N + "Cell")] == ["분류", "상태", "항목", "세부 내용"]
       and "능동 · 수동 한계" in cell_text(r[1].findall(N + "Cell")[0]) if len(r) > 1]
if len(dts) != 1:
    raise SystemExit(f"세부 진행항목 표가 {len(dts)}개다(1개 기대) — 멈춘다")
new_rows = frag(detail_table([GROUP])).find(N + "Table").findall(N + "Row")[1:]
for r in new_rows:
    dts[0].append(r)

# 3) 세부 진행항목 표 바로 뒤에 그림 ⑥ ~ ⑪
parent = None
for oec in root.iter(N + "OEChildren"):
    for i, o in enumerate(list(oec)):
        if o.find(N + "Table") is dts[0]:
            parent, idx = oec, i
if parent is None:
    raise SystemExit("세부 진행항목 표의 부모를 못 찾았다 — 멈춘다")
parent.insert(idx + 1, frag(oe("그림 — UI 개편 시안 V0.8 · Meta 규격판(10/27)", bullet=True)))
parent.insert(idx + 2, frag(shots_table()))

s = ET.tostring(root, encoding="unicode")
open(sys.argv[2], "w", encoding="utf-8").write('<?xml version="1.0"?>' + s)
print("ok", len(new_rows), "rows ·", len(SHOTS), "그림")
