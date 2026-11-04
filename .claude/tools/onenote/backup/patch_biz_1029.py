# 사업화 10월 5주차 10/29 퇴근 — 로비 손봄 · OS 글자 대응 · 실습 UI 크기 · 진행 현황 · 상단 메뉴 · 로비 배경(블렌더).
# 기존 글은 건드리지 않는다(라이브 XML을 그대로 고쳐 쓴다). 쓰기 전 라이브 362줄 · 그림 24 = 10/28 퇴근 쓰기 결과 + 사용자가 10/29 17:07에 붙인 그림 1(보존).
# 10/29 칸(빈 칸)에 줄 · 세부 진행항목 표 끝에 새 묶음 · 10/28 그림 표 바로 뒤에 그림 ㉓ ~ ㉘.
# 사용: patch_biz_1028.py <입력.xml> <출력.xml> <그림 폴더>
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
    "[10/29] 로비 — 시나리오 목록 · 로그인 조/이름 목록을 살짝 어두운 바탕 상자로 묶음 · 카드 하이라이트 양옆 잘림 고침(실측) · 시작 탭 = 전체 · 드롭다운 폭 줄임",
    "[10/29] 진입 메뉴 — «학습 모드 선택» 제목 · 설명 + [실습]/[평가] 버튼 두껍게(그림 ㉖) · OS 글자 크기가 커지면 팝업 판이 세로로 늘어남(진입 메뉴 · 로그인 · 로그인 안내)",
    "[10/29] 실습 UI — 상단 메뉴 손 닿는 거리 · 버튼 높이 2배 · 아이콘 위/글자 아래 · 헤드셋 높이 따라감 · 반투명(그림 ㉕) / 정보 창 3배 · 2.4m / 리모컨 절반",
    "[10/29] 실습 시작 화면 = «진행 현황»(13개 술기) · 진행 가이드를 그 창 안에 겹쳐 그림 · 창은 빈 칸으로 펼침(이미 뜬 창은 안 움직임) · 관제 미러링 아래 줄 되살림",
    "[10/29] 로비 배경 — 블렌더 설치 · 현대식 한방병원 + 도시를 내려다보는 테라스 시안 V0.3 → 360° 그림으로 로비 하늘에 적용(그림 ㉓ ㉔) · Quest 3S 설치 · 미커밋",
]

GROUP = ("UI 개편\n(10/29 · 로비 · 실습 · 배경)", [
    ("완료", "[10/29] 로비 목록 바탕 상자 · 하이라이트 잘림", [
        "시나리오 목록 전체 · 로그인 조 목록 · 이름 목록을 판보다 살짝 어두운 바탕 상자로 묶음(사용자 «줄이 아니라 백그라운드로 다른 색»)",
        "카드 하이라이트가 1열 왼쪽 · 5열 오른쪽에서 6씩 잘림 — 재 보니 목록 가림 범위가 카드 5열 폭과 같았음 → 상자 안쪽 여백 12 · 잘림 0(재는 도구로 확인)",
        "로비 판 1256 × 841 → 1280 × 857 · 로그인 판 800 × 520 → 828 × 544(칸 크기 그대로 · 사용자 A안)",
        "시작 탭이 ROM진단으로 저장돼 있었음 → 처음 켜질 때는 늘 «전체»"]),
    ("완료", "[10/29] 진입 메뉴 · OS 글자 크기 대응", [
        "«학습 모드 선택» 제목 · 설명 추가 · [실습]/[평가] 높이 52 → 88 · 드롭다운 폭 176 → 128",
        "OS 글자가 커지면 넘치는 줄만큼 판을 세로로 늘림 — 진입 메뉴 · 로그인 판 · 로그인 안내(글자 ×1.5에서 판 +129 · +30 · +59)",
        "OS가 키운 몫만 늘림(기본 크기에선 그대로) · 에디터 넘침 검사 · 단계별 캡처도 기기와 같은 계산"]),
    ("완료", "[10/29] 실습 UI 크기 · 자리(사용자 지시)", [
        "상단 메뉴: 0.45m(손 닿는 거리) · 버튼 높이 2배 · 아이콘 위 / 글자 아래 · 바탕 반투명",
        "상단 메뉴가 머리 뒤 · 이상한 높이에 생김 → 기기 로그로 원인 확인: 씬 시작 0.1초에 추적 전 머리 값(높이 1.15m)으로 자리를 잡았음 → 1.5초 뒤 다시 잡음 · 높이는 헤드셋을 계속 따라감",
        "정면 정보 창 3배(1.38 × 0.94m) · 2.4m 뒤로 · 설정 리모컨 절반 · 조금 아래 · 바탕 반투명"]),
    ("완료", "[10/29] 진행 현황 · 창 펼침 규칙", [
        "실습 시작 화면 = «진행 현황»(전 «수행결과») — 13개 술기 공통",
        "진행 가이드(단계 · 지시문 · 버튼)를 진행 현황 창 위쪽에 겹쳐 그림 · 창을 닫으면 같이 숨음",
        "새로 연 창은 정면 → 왼쪽 → 오른쪽 중 빈 칸으로 · 이미 뜬 창은 안 밀림(사용자 정정)",
        "관제 미러링 아래 줄(배터리 · 정보 · 해상도) 되살림"]),
    ("진행중", "[10/29] 로비 배경 — 현대식 한방병원 풍경(블렌더)", [
        "Quest 홈 배경은 Meta 자산이라 가져올 수 없음 → 블렌더 4.5 LTS 설치 · 직접 만듦",
        "시안 V0.1 원형 실내 → V0.2 밝은 톤 + 한의원 요소 → V0.3 트인 테라스에서 곡선 기와지붕의 현대식 한방병원 · 한옥 마을 · 도시 · 산을 내려다봄(사용자 «현대에 어우러진 · 케데헌 장면처럼»)",
        "360° 그림 한 장으로 로비 하늘에 깔고 패스스루는 끔(성능 부담 거의 없음) · 정면 방향은 캡처로 재서 맞춤",
        "남은 것: 발밑 데크 깊이감 · 빌딩 창 · 한방병원이 로비 판에 가림 — 헤드셋 확인 대기"]),
    ("대기", "[10/29] 남은 것", [
        "Quest 3에는 마지막 판(상단 메뉴 반투명 · 로비 배경) 미설치",
        "Play · 기기 확인: 진행 현황 겹침 · 다른 술기 · 반투명 정도 · 상단 메뉴 높이 따라감",
        "오늘 커밋 0 · 미푸시 113 — 기능별로 나눠 커밋해야 함",
        "문서: UI 글꼴 정리 V0.2(D:/CoCoChuna_UI_글꼴정리_V0.2_20261029.docx · pdf)"]),
])

SHOTS = [
    ("23_로비배경.png", "㉓ 로비 배경 적용 — 눈높이 정면 · 왼쪽 · 오른쪽 · 뒤(에디터)", "패스스루 끔 · 360° 그림 하늘 · 회색 덩어리는 에디터에서 떠 있는 손 모델"),
    ("24_파노라마.png", "㉔ 로비 배경 360° 그림(블렌더 렌더 · 4096 × 2048)", "가운데 = 정면(현대식 한방병원) · 좌우 = 도시 · 전망탑 · 소나무 화단 · 아래 = 데크"),
    ("25_상단메뉴.png", "㉕ 실습 상단 메뉴 — 버튼 높이 2배 · 아이콘 위 / 글자 아래 · 바탕 반투명", "«수행결과» → «진행 현황»"),
    ("26_진입메뉴.png", "㉖ 시나리오 진입 메뉴 — 학습 모드 선택 · 두꺼운 버튼 · 좁힌 드롭다운", "사진 칸은 에디터에서 비어 보임"),
    ("27_로그인.png", "㉗ 로그인 판 — 조 · 이름 목록을 바탕 상자로 묶음", "가운데 줄 대신 상자 · 조 · 이름은 미리보기용 가짜"),
    ("28_텍스트15.png", "㉘ OS 글자 1.5배 — 진입 메뉴가 세로로 늘어나 넘치지 않음", "10/28 그림 ㉒(설명이 아래 행을 덮음)과 비교"),
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
        rows.append([cell([oe("10/29")]), cell([image_oe(os.path.join(SHOT_DIR, fname), 700.0), oe(title, bold=True), oe(cap)])])
    return oe_table(table([48.0, 1010.0], rows))


tree = ET.parse(sys.argv[1])
root = tree.getroot()

# 1) 5주차 일정표 10/28 칸(비어 있어야 한다)
scheds = [tb for tb in root.iter(N + "Table")
          if (r := tb.findall(N + "Row")) and [cell_text(c) for c in r[0].findall(N + "Cell")] == ["일", "수행항목"]]
if len(scheds) != 5:
    raise SystemExit(f"일정표가 {len(scheds)}개다(5개 기대) — 멈춘다")
row = [r for r in scheds[4].findall(N + "Row")[1:] if cell_text(r.findall(N + "Cell")[0]) == "10/29"]
if len(row) != 1:
    raise SystemExit("5주차 10/29 행을 못 찾았다 — 멈춘다")
c1 = row[0].findall(N + "Cell")[1]
if cell_text(c1):
    raise SystemExit("10/29 칸이 비어 있지 않다 — 멈춘다")
kids = c1.find(N + "OEChildren")
for o in list(kids):   # 빈 OE 하나를 첫 줄로 바꾼다
    kids.remove(o)
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

# 3) V1.4 그림 표 바로 뒤에 그림 ⑲ ~ ㉒
parent = None
for oec in root.iter(N + "OEChildren"):
    kids2 = list(oec)
    for i, o in enumerate(kids2):
        if "UI 개편 구현 · 로비 V1.5 · OS 크기 비교(10/28)" in own(o) and i + 1 < len(kids2) and kids2[i + 1].find(N + "Table") is not None:
            parent, idx = oec, i + 1
if parent is None:
    raise SystemExit("10/28 그림 표를 못 찾았다 — 멈춘다")
parent.insert(idx + 1, frag(oe("그림 — 로비 손봄 · 실습 UI · 로비 배경(10/29)", bullet=True)))
parent.insert(idx + 2, frag(shots_table()))

s = ET.tostring(root, encoding="unicode")
open(sys.argv[2], "w", encoding="utf-8").write('<?xml version="1.0"?>' + s)
print("ok", len(new_rows), "rows ·", len(SHOTS), "그림")
