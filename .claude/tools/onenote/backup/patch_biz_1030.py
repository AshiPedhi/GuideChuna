# 사업화 10월 5주차 10/30 퇴근 — 로딩 · 나레이션 순서 · 팝업 3종 로비 규격 · 실습 UI · 상부승모근 ROM 양식 각도기 시안 · 본 각도 판정.
# 기존 글은 건드리지 않는다(라이브 XML을 그대로 고쳐 쓴다). 쓰기 전 라이브 428줄 · 그림 30 = 10/29 퇴근 쓰기 결과 + 사용자가 10/30 16:35에 쓴 4줄(Horizon OS v2.9 메모 2 · 끝 메모 2 — 보존).
# 10/30 칸(빈 칸)에 줄 · 세부 진행항목 표 끝에 새 묶음 · 10/29 그림 표 바로 뒤에 그림 ㉙ ~ ㉛.
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
    "[10/30] 오전 PC 꺼짐 뒤 재개 — 키스토어 비밀번호 다시 넣고 빌드 · 로딩 화면(자리 잡을 때까지 가림 · 진행 막대) · 로비 판 2.2배 뒤로 + 높이 고정 → 1.5배 키움",
    "[10/30] 로딩 창이 걷히기 전에 음성 안내가 먼저 나오던 것 → 가림막이 다 걷힌 뒤 나레이션 시작(13개 술기 공통 · 기기 로그로 0.88초 기다림 확인)",
    "[10/30] 팝업 — 실습 나가기 버튼이 안 눌린 원인(실측: 뒷배경 숨김이 레이 · 손 표면까지 끔) → 로비 규격 독립 판으로 새로 · 로비 종료 · 로그아웃도 같은 규격 · 아이콘 뺌 · 기울기 없이 정면(그림 ㉙ ~ ㉛)",
    "[10/30] 실습 UI — 상단 메뉴 0.38m · 켜짐 = 흰 알약 + 켬/끔 소리 · 정보 창 3.2m · 창 사이 틈 3cm로 키움 · 바닥 0.5m 격자 · 관제 미러링 [영상 녹화]",
    "[10/30] 상부승모근 시안 — ROM 양식 각도기(환자 본에서 실제 각도 · 적정/초과 띠 · 머리 쪽 각도 + 유지 게이지) · 적정 범위를 본 각도(°)로 판정(A안) · 경고음이 안 빨라지던 원인 고침",
    "[10/30] Quest 3S · Quest 3 설치(16:13 판) · 정보 창 틈 · 머리 표시 고정은 빌드 전 · 커밋 0",
]

GROUP = ("UI 개편\n(10/30 · 팝업 · 실습 · 각도기)", [
    ("완료", "[10/30] 로딩 화면 · 나레이션 순서", [
        "로딩 가림막이 실습 씬 자동 맞춤 · 상단 메뉴 배치가 끝날 때까지 덮고 0.35초에 걷힘(상한 5초) · 진행 막대는 실제 진행(씬 읽기 · 머리 추적 · 자리 잡기)",
        "음성 안내가 가림막 뒤에서 먼저 나옴 → 원인: 시나리오는 씬이 켜지자마자 시작, 가림막은 그 뒤 자동 맞춤을 기다림 → 나레이션만 가림막이 걷힐 때까지 기다림",
        "로비 판: 2.2배 뒤로(1.32m) · 머리 높이 따라가기 끔(시작 때 한 번) → 1.5배 키움(거리 그대로)"]),
    ("완료", "[10/30] 팝업 3종 로비 규격(그림 ㉙ ~ ㉛)", [
        "실습 나가기 버튼이 안 눌림 — 씬에서 확인: 팝업이 옛 정보 패널 안에 있어 «뒷배경 숨김»이 형제인 레이 · 손 표면까지 꺼 버림",
        "실습 나가기 · 로비 종료 · 로그아웃을 로그인 안내와 같은 판으로 새로 조립(제목 20 · 본문 14 · 버튼 48 알약 · OS 글자 크기 대응 · 아이콘 없음) · 옛 팝업은 꺼 둠",
        "로비 두 팝업은 기울기 없이 수직 · 실습 나가기는 상단 메뉴 아래에서 사용자 쪽을 정면으로 봄",
        "로비 종료: 로그인 안 했으면 [로그아웃]이 숨고 두 버튼이 폭을 나눔 · 문구 · 버튼 글자는 옛 팝업 그대로"]),
    ("완료", "[10/30] 실습 UI · 바닥 · 미러링", [
        "상단 메뉴 0.45 → 0.38m(터치 거리) · 버튼 바탕을 덮던 애니메이터를 꺼서 켜짐이 흰 알약으로 보임 · 켬/끔 소리 다르게",
        "정보 창: 3.6m → 3.2m · 배율 4.74 · 계산(2R·tan(θ/2) − 틈)으로 옆 창과 틈 42cm → 3cm",
        "암실 바닥 0.5m 격자 · 바탕이 어둠으로 사라지는 주변부에도 선이 옅게 남음",
        "추나 관제 미러링에 [영상 녹화](PC 다운로드 폴더 · mp4/webm) · 해상도는 안 올림(사용자 취소)"]),
    ("진행중", "[10/30] 상부승모근 ROM 양식 각도기 시안", [
        "ROM 각도기(머리를 지나는 면 · 눈금 · 지침 · 숫자)를 상부승모근 회전 · 측굴 단계에 띄움 · 각도는 환자 본에서 직접 잼(종전 = 클립 진행 비율 × 90°)",
        "실측: 클립 끝 실제 각도 환측회전 58.5° · 전부측굴 65.8° · 중부측굴 60.4° · 스트레칭 전부 76.9° · 중부 71.6° / 종전 판정 «45°»(비율 0.5)는 실제 회전 30.7° · 측굴 36.8°",
        "A안 적용: 적정 범위를 본 각도(°)로 정하면 클립에서 그 각도가 되는 시점을 재서 판정 · 경고음 · 표시에 씀(처음 값 = 비율 × 90 → 27~45° · 45~63°)",
        "적정(초록) · 초과(빨강) 띠 · 머리 쪽 «좌측굴 32도 적정» + 유지 게이지 · 표시는 머리를 안 따라감",
        "경고음이 천천히 움직이면 안 빨라지던 원인(진행 이벤트가 직전 프레임 대비 1%) 고침 · 첫 단계 각도 가이드 안 뜨던 것은 추정 원인 막고 로그 심음"]),
    ("대기", "[10/30] 남은 것", [
        "각도 기준: 자료(XR-현훈추나 임상실습 메뉴얼 v2.1)에 상부승모근 적정 · 위험 각도는 없음 — 경추 정상 측굴 40~45°만 있음 → 교수님 확인 또는 45° 상한으로 정할지 결정",
        "빌드 전: 정보 창 틈 3cm · 머리 표시 고정(실습 씬 저장 대기)",
        "기기 확인: 팝업 버튼 · 딤 · 각도기 띠 · 다른 술기(공용 평가기를 고침)",
        "오늘 커밋 0 — 기능별로 나눠 커밋해야 함"]),
])

SHOTS = [
    ("29_실습나가기.png", "㉙ 실습 나가기 팝업 — 로비 규격 독립 판(아이콘 없음)", "취소 · 다시하기 · 메인으로 · 열리면 상단 메뉴 아래에서 사용자 쪽을 정면으로 봄"),
    ("30_로비종료.png", "㉚ 로비 종료 팝업 — 같은 규격", "로그인 안 했으면 [로그아웃]이 숨고 두 버튼이 폭을 나눔"),
    ("31_로그아웃.png", "㉛ 로비 로그아웃 팝업 — 같은 규격", "기울기 없이 수직으로 섬"),
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
        rows.append([cell([oe("10/30")]), cell([image_oe(os.path.join(SHOT_DIR, fname), 700.0), oe(title, bold=True), oe(cap)])])
    return oe_table(table([48.0, 1010.0], rows))


tree = ET.parse(sys.argv[1])
root = tree.getroot()

# 1) 5주차 일정표 10/30 칸(비어 있어야 한다)
scheds = [tb for tb in root.iter(N + "Table")
          if (r := tb.findall(N + "Row")) and [cell_text(c) for c in r[0].findall(N + "Cell")] == ["일", "수행항목"]]
if len(scheds) != 5:
    raise SystemExit(f"일정표가 {len(scheds)}개다(5개 기대) — 멈춘다")
row = [r for r in scheds[4].findall(N + "Row")[1:] if cell_text(r.findall(N + "Cell")[0]) == "10/30"]
if len(row) != 1:
    raise SystemExit("5주차 10/30 행을 못 찾았다 — 멈춘다")
c1 = row[0].findall(N + "Cell")[1]
if cell_text(c1):
    raise SystemExit("10/30 칸이 비어 있지 않다 — 멈춘다")
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

# 3) 10/29 그림 표 바로 뒤에 그림 ㉙ ~ ㉛
parent = None
for oec in root.iter(N + "OEChildren"):
    kids2 = list(oec)
    for i, o in enumerate(kids2):
        if "로비 손봄 · 실습 UI · 로비 배경(10/29)" in own(o) and i + 1 < len(kids2) and kids2[i + 1].find(N + "Table") is not None:
            parent, idx = oec, i + 1
if parent is None:
    raise SystemExit("10/29 그림 표를 못 찾았다 — 멈춘다")
parent.insert(idx + 1, frag(oe("그림 — 팝업 3종 로비 규격(10/30)", bullet=True)))
parent.insert(idx + 2, frag(shots_table()))

s = ET.tostring(root, encoding="unicode")
open(sys.argv[2], "w", encoding="utf-8").write('<?xml version="1.0"?>' + s)
print("ok", len(new_rows), "rows ·", len(SHOTS), "그림")
