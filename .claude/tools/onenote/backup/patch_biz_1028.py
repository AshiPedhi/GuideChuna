# 사업화 10월 5주차 10/28 퇴근 — UI 개편 구현(진입 메뉴 · 로그인 한 판 · 로비 V1.5 · 글꼴 · OS 크기 맞춤 · 비교 문서).
# 기존 글은 건드리지 않는다(라이브 XML을 그대로 고쳐 쓴다). 쓰기 전 라이브 299줄 · 그림 19 = 10/27 퇴근 쓰기 결과(사용자 수정 없음).
# 10/28 칸(빈 칸)에 줄 · 세부 진행항목 표(10/26 환자 팔 IK로 시작) 끝에 새 묶음 · V1.4 그림 표 바로 뒤에 그림 ⑲ ~ ㉒.
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
    "[10/28] 시나리오 진입 메뉴를 Quest 설정 창 꼴로 — 행마다 제목 · 설명 · 오른쪽 드롭다운(난이도 · 환경) · [실습]/[평가] 누르면 바로 진입(그림 ⑳)",
    "[10/28] 로그인 한 판 구현 — 왼쪽 조 2열(최대 15조 스크롤 없이) · 오른쪽 이름 격자 · 이름 누르면 로그인 · 로그인 전 카드를 누르면 안내 없이 로그인 판(그림 ㉑)",
    "[10/28] 로비 V1.5 — 카드 5열 · 가로세로 같은 비율로 키움 · 여백 상하좌우 24 · 로고 키움 · 도움말 · 패스스루 버튼 뺌 · KoPubWorld돋움 3굵기(그림 ⑲)",
    "[10/28] Quest OS «텍스트 크기» · «화면 크기»를 앱이 읽어 자동으로 맞춤(로비 · 실습) — 기기에서 적용 확인 · 단계별 비교 문서 V0.1(그림 ㉒)",
    "[10/28] 기기에서 찾아 고친 것 — 카드 글자 밑에 사진이 깔림 · 카드 하이라이트 사라짐 · 팝업 X 원이 안 보임 / 팝업 딤 · 말풍선 추가 · 미커밋 · 일부 기기 미확인",
]

GROUP = ("UI 개편\n(10/28 · 구현)", [
    ("완료", "[10/28] 시나리오 진입 메뉴 — Quest 설정 창 꼴(사용자 «타이틀 아래 설명 · 우측 드롭박스»)", [
        "행 카드 안에 두 행: 제목 16 · 설명 14 · 오른쪽 드롭다운 알약(난이도 초급 · 중급 · 상급 / 환경 VR · XR 준비 중)",
        "[실습](흰 알약) · [평가](옅은 알약) 누르면 바로 진입 · 평가도 난이도 칸은 그대로 보임(사용자)",
        "드롭다운은 같은 판 안에서 펼침(레이 입력이 끊기지 않게) · 기기 녹화로 레이 조작 확인"]),
    ("완료", "[10/28] 로그인 한 판", [
        "왼쪽 조 목록 2열(인원수 표시 없음 · 15조까지 스크롤 없이 — 사용자와 2열 vs 페이지 버튼 중 2열로 정함)",
        "오른쪽 이름 격자 4열 · 이름 누르면 바로 로그인 · 이전 조 자동 선택",
        "로그인 안 한 채 카드를 누르면 «로그인하세요» 안내 없이 바로 로그인 판(사용자 «안내가 굳이 필요 없다»)",
        "팝업이 너무 가깝게 뜬다(사용자) → 로비 판 앞 8cm로 맞춤"]),
    ("완료", "[10/28] 로비 V1.5 — 배치 · 글꼴", [
        "카드 가로 5개 · 판 1024 × 686 → 1256 × 841(가로세로 비율 그대로 — 사용자 «옆으로만 길어지면 이상하니까»)",
        "여백 상하좌우 24(고치기 전 좌 20 · 우 0 · 상 14였음 — 재서 맞춤) · 로고 2배 · 상단 타이틀 글자 뺌 · 도움말 · 패스스루 버튼 뺌(기본이 패스스루)",
        "X 버튼 = 40 원(56 × 40이라 타원이었음) · 로그인 버튼 = 글자 폭 알약(190 고정이었음) · 팝업 X도 같은 꼴",
        "글꼴 KoPubWorld돋움: Bold = 제목(구역 제목 24 등) · Medium = 라벨(카드 제목 18 · 버튼 · 탭) · Light = 회색 보조 글자(카드 정보줄 14 · 설명)",
        "카드 정보줄은 한 줄 · 넘치면 «…» · 손(레이)을 올리면 전체 문장 말풍선",
        "팝업(로그인 · 시나리오)이 뜨면 뒤 로비를 반투명 검정으로 덮음(딤) · 그동안 카드 안 눌림"]),
    ("완료", "[10/28] OS 텍스트 · 화면 크기 자동 맞춤(로비 · 실습)", [
        "Quest 3S에서 adb로 잼: 텍스트 크기 = font_scale(1.0 · 1.15 · 1.5) · 화면 크기 = 화면 밀도(200 · 256 · 280) · 설정을 바꾸면 OS가 앱을 다시 켬",
        "앱이 시작할 때 읽어 글자 크기 × 텍스트 배율 · 판 크기 × 화면 배율(과하지 않게 상한)",
        "기기 로그: 글자 × 1.50 · 판 × 1.40 · 로비 판 5개 · 글자 138개 적용 — 사용자가 헤드셋에서 확인"]),
    ("진행중", "[10/28] 단계별 비교 문서 V0.1 — 무엇을 키우고 줄일지 판단용(그림 ㉒)", [
        "텍스트 1.15배 · 화면 1.28 · 1.4배 = 겹침 없음 / 텍스트 1.5배(최대)에서 세 곳 깨짐: 카드 정보줄 사라짐 · 시나리오 설명이 아래 행을 덮음 · 긴 조 이름 두 줄",
        "화면 1.4배에서는 로비 판이 시야를 거의 다 채움",
        "작게 · 중간 · 크게 · 매우크게가 각각 어느 값인지는 아직 안 잼(헤드셋에서 단계마다 읽을 것)",
        "D:/CoCoChuna_로비UI_OS크기별비교_V0.1_20261028.pdf"]),
    ("완료", "[10/28] 기기에서 찾아 고친 것", [
        "카드 글자 밑에 사진이 깔림 — Meta 버튼 애니메이션이 카드 바탕(사진 들어 있음)을 불투명하게 칠함 · 에디터에선 안 보임 → 바탕은 그리지 않고 누르는 범위만 남김",
        "그 바탕이 맡던 카드 하이라이트가 사라짐(사용자) → 카드 뒤에 따로 둔 판으로 호버 · 누름 표시",
        "팝업 X에 원이 안 보임(사용자) — 바탕이 투명이라 색을 곱해도 투명 → 로비 X와 같은 회색 원 색"]),
    ("대기", "[10/28] 답 대기 · 남은 것", [
        "텍스트 1.5배 세 곳을 어떻게 손볼지(문서 정리 표 참고)",
        "팝업 X 원 색 수정은 아직 저장 · 빌드 전 · 말풍선 · 하이라이트 · 딤은 기기 미확인",
        "실습 씬 글꼴은 아직 예전 글꼴 · 실습 정보 창 기본 크기",
        "미커밋 · 테스트용 «모든 카드 보이기»는 배포 전에 끔"]),
])

SHOTS = [
    ("19_로비.png", "⑲ 로비 V1.5 — 카드 5열 · 여백 24 · KoPubWorld돋움(에디터 미리보기)", "제목 = Bold · 카드 제목 = Medium · 회색 정보줄 = Light · 긴 정보줄은 «…»"),
    ("20_진입메뉴.png", "⑳ 시나리오 진입 메뉴 — Quest 설정 창 꼴", "행 = 제목 · 설명 · 오른쪽 드롭다운 · [실습]/[평가] 누르면 진입 · X = 로비 X와 같은 원 · 사진 칸은 에디터에서 비어 보임"),
    ("21_로그인.png", "㉑ 로그인 한 판", "왼쪽 조 2열(15조까지 스크롤 없음) · 오른쪽 이름 격자 · 이름 누르면 로그인 · 조 · 이름은 미리보기용 가짜"),
    ("22_텍스트15.png", "㉒ OS 텍스트 1.5배(최대)일 때 — 비교 문서에서", "시나리오 설명이 두 줄로 감겨 아래 «환경» 행을 덮는다 · 뒤 로비는 딤으로 어두워짐"),
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
        rows.append([cell([oe("10/28")]), cell([image_oe(os.path.join(SHOT_DIR, fname), 700.0), oe(title, bold=True), oe(cap)])])
    return oe_table(table([48.0, 1010.0], rows))


tree = ET.parse(sys.argv[1])
root = tree.getroot()

# 1) 5주차 일정표 10/28 칸(비어 있어야 한다)
scheds = [tb for tb in root.iter(N + "Table")
          if (r := tb.findall(N + "Row")) and [cell_text(c) for c in r[0].findall(N + "Cell")] == ["일", "수행항목"]]
if len(scheds) != 5:
    raise SystemExit(f"일정표가 {len(scheds)}개다(5개 기대) — 멈춘다")
row = [r for r in scheds[4].findall(N + "Row")[1:] if cell_text(r.findall(N + "Cell")[0]) == "10/28"]
if len(row) != 1:
    raise SystemExit("5주차 10/28 행을 못 찾았다 — 멈춘다")
c1 = row[0].findall(N + "Cell")[1]
if cell_text(c1):
    raise SystemExit("10/28 칸이 비어 있지 않다 — 멈춘다")
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
        if "UI 개편 시안 V1.4" in own(o) and i + 1 < len(kids2) and kids2[i + 1].find(N + "Table") is not None:
            parent, idx = oec, i + 1
if parent is None:
    raise SystemExit("V1.4 그림 표를 못 찾았다 — 멈춘다")
parent.insert(idx + 1, frag(oe("그림 — UI 개편 구현 · 로비 V1.5 · OS 크기 비교(10/28)", bullet=True)))
parent.insert(idx + 2, frag(shots_table()))

s = ET.tostring(root, encoding="unicode")
open(sys.argv[2], "w", encoding="utf-8").write('<?xml version="1.0"?>' + s)
print("ok", len(new_rows), "rows ·", len(SHOTS), "그림")
