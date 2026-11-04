# 사업화 11월 페이지(사용자 양식 · 빈 틀) 1주차에 11/02 퇴근 기록을 넣는다 — 형성평가 이슈 점검 · 상부승모근 6° 원인 · 추나 베드 색 · 제2늑골 영상 → c9 동작 · 앱 연결.
# ★기존 글은 건드리지 않는다. 1주차 일정표가 머리줄만 있는지 확인하고, 아니면 멈춘다(틀 = patch_biz_1026.py).
# 사용: patch_biz_1102.py <입력.xml> <출력.xml>
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


DAY = ("11/2", [
    "[11/2] 1차 형성평가 회의록(8/19) 크리티컬 이슈 점검 — 흉추 신전 스킵은 해결 · 두개골 호흡 정지 통과 · 용어 · 전문가 영상은 미해결(두개골은 후순위로)",
    "[11/2] 상부승모근 — 회전 뒤 측굴 각도기가 0°가 아니라 약 6°에서 시작하는 원인 확인(머리 숙임 + 회전 상태를 측굴처럼 잼) · 수정 방법 컨펌 대기",
    "[11/2] 추나 베드 매트리스 색을 머리 쿠션과 같은 어두운 갈색으로(그림 ①)",
    "[11/2] 제2늑골 — ChatGPT 영상 분석 · 해부학 리그 재구성(V03)의 환자 팔 동작을 실습 환자(c9)용 애니메이션으로 옮김 · 팔 비틀림 수정 · 3인칭 · 1인칭 원본과 비교(그림 ②)",
    "[11/2] 제2늑골 교정 3단계(올리기 · 내리기 · 직접 3회)에 새 동작 연결 — 가이드손이 환자 손을 잡은 채 따라가게 새 녹화(그림 ③) · Play 미검증 · 커밋 0",
])

DETAIL = [
  ("형성평가 이슈 점검\n(11/2)", [
    ("완료", "[11/2] 8/19 회의록 결정사항 대조(코드 · CSV · 커밋)", [
      "해결: 흉추 신전 스킵하면 체위가 안 바뀌던 것(10-19 Play 로그 확인)",
      "미해결: 두개골 호흡 구간을 정지 자세로 통과 · 호흡 시간 6초 고정 · 방향 용어(족방 · 두방) CSV 4개 · 복잡추나 전문가 영상 0개 · CRI 펄스",
      "확인 필요: 패널 이동 · X버튼(10월 UI 개편으로 대체) · 힘 방향 화살표 · 필요 골격만 표시 · 접촉점 위치",
      "남은 동작 오류: 복와위 서 있음(수정 · Play 미검증) · 흉추 신전 지시 행 강제 전환 · 팔 터치 저절로 넘어감",
      "사용자 결정: 두개골 관련은 후순위"]),
  ]),
  ("상부승모근 각도기\n(11/2)", [
    ("대기", "[11/2] 측굴 시작 각도 약 6°", [
      "원인: 측굴 클립 시작 자세가 머리를 숙이고(Nod) 회전한(Turn) 상태라, 머리 위 방향을 측굴 면에 투영하면 측굴처럼 잰다(전부 측굴 5.6° · 중부 측굴 0.0° — 차이는 회전뿐)",
      "제안: 단계 시작 자세 각도를 빼서 0°부터 재기(각도기 한 파일) — 컨펌 대기",
      "같이 본 것: 회전 클립 끝(Turn +1.5)과 측굴 클립 시작(+0.7)이 달라 넘어갈 때 머리가 절반 되돌아감"]),
  ]),
  ("추나 베드 · 제2늑골 환자 동작\n(11/2)", [
    ("완료", "[11/2] 추나 베드 색(그림 ①)", [
      "밝은 갈색 매트리스 재질을 머리 쿠션과 같은 어두운 갈색 재질로 바꿈(실습 씬만 · 재질 파일은 그대로 — 다른 씬 영향 없음) · 실습 씬 저장함 · 기기는 다음 빌드부터"]),
    ("완료", "[11/2] 영상 분석 · V03 재구성 검토", [
      "ChatGPT 영상 분석(10/23 그림 ㊻ ㊼): 팔꿈치 펴짐 37 ~ 61° ↔ 깊은 굽힘 138 ~ 142° · 위팔 135° ↔ 42° — 크게 펴기 → 발 쪽 → 깊게 굽혀 복귀",
      "V03(10/27 · 미커밋): 3인칭 56.5 ~ 64.7초 팔 한 주기를 해부학 리그로 재구성 · 환자 팔은 합격권 · 가이드 손은 불합격 · 실습 앱에는 미연결이었음"]),
    ("완료", "[11/2] 실습 환자(c9) 애니메이션으로 옮김(그림 ②)", [
      "좌우를 재서 맞춤: 앱이 움직이는 팔(본 이름 Right)과 V03 팔 모두 해부학상 왼팔 → 그대로 옮김",
      "근육 값을 프레임마다 풀고 저장된 클립을 다시 재생해 잼: 위팔 방향 오차 평균 1.2° · 아래팔 0.1° · 팔꿈치 20 ~ 141.5°",
      "팔 비틀림 수정: 손바닥 방향을 맞추느라 전완 비틀림이 한계까지 찼던 것 → 정상 범위로 묶음(대신 손바닥 방향이 평균 29° 덜 바깥쪽)",
      "비교 영상: D:/CoCoChuna_제2늑골_V03클립비교_3인칭1인칭_20261102.mp4(윗줄 3인칭 · 아랫줄 1인칭) · 1인칭은 눈 비교만, 후반 약 7.5초는 원본과 타이밍이 어긋남"]),
    ("진행중", "[11/2] 앱 연결 — 교정 3단계(그림 ③)", [
      "올리기 · 내리기 · 직접 3회 단계를 새 클립(펴짐 20° → 굽힘 141.5° · 2.6초 구간)과 새 가이드손 녹화로 바꿈 · 기존 클립 · 녹화는 지우지 않음",
      "가이드손: 기존 녹화에서 시술자 왼손이 환자 손을 잡은 자리를 재서, 그 파지 그대로 새 팔을 따라가게 만듦(손가락 모양은 기존 그대로)",
      "점검: 배선 결함 0 · 빌드 오류 0 · 렌더로 손이 환자 손을 잡고 따라가는 것 확인",
      "Play 미검증 — 볼 것: 올리기 시작 때 팔이 튀는지 · 직접 3회에서 횟수가 세지는지(새 경로가 휘어 있음)"]),
  ]),
]

SHOT_DIR = "D:/추나녹화/20261102/캡처"
SHOTS = [
  ("01_추나베드_색.png", "① 추나 베드 매트리스 색 — 왼쪽 전 · 오른쪽 후", "밝은 갈색 매트리스를 머리 쪽과 같은 어두운 갈색으로."),
  ("02_제2늑골_원본대비_1인칭3인칭.png", "② 제2늑골 환자 팔 — 1인칭 원본 · 1인칭 모델 · 3인칭 모델", "위부터 시작 파지 · 크게 편 자세 · 깊게 굽힌 자세. 1인칭 모델 시점은 영상 분석의 카메라 추정값이다."),
  ("03_제2늑골_가이드손_기존새자료.png", "③ 가이드손 — 윗줄 기존 자료 · 아랫줄 새 자료(시작 · 중간 · 끝)", "새 자료는 가이드손이 환자 손을 잡은 채 펴진 팔에서 굽힌 팔까지 따라간다."),
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
        rows.append([cell([oe("11/2")]), cell([image_oe(os.path.join(SHOT_DIR, fname), 800.0), oe(title, bold=True), oe(cap)])])
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
if any(cell_text(c) for r in rows[1:] for c in r.findall(N + "Cell")):
    raise SystemExit("1주차 일정표가 비어 있지 않다 — 멈춘다")


def set_cell(c, lines):
    kids = c.find(N + "OEChildren")
    for o in list(kids):
        kids.remove(o)
    for line in lines:
        kids.append(frag(oe(line)))


for row, d in zip(rows[1:], ["11/2", "11/3", "11/4", "11/5", "11/6"]):
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
parent.insert(idx + 3, frag(oe("화면 캡처 — 추나 베드 · 제2늑골 환자 동작(11/2)", bullet=True)))
parent.insert(idx + 4, frag(shots_table()))

s = ET.tostring(root, encoding="unicode")
open(sys.argv[2], "w", encoding="utf-8").write('<?xml version="1.0"?>' + s)
print("ok")
