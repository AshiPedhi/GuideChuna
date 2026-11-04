# 사업화 11월 1주차 11/3 퇴근 덧붙임(2026-11-03 저녁) — 오후 작업: 안내판 3 · 4차 · 전문가 영상 13개 · 정면 영상 조작 막대.
# ★기존 줄은 안 바꾼다 — 11/3 칸 끝에 줄을 더하고, «화면 캡처 — 제2늑골 Play(11/3)» 표 바로 뒤에 새 머리줄 + 세부표 + 그림 1장을 넣는다.
# 사용: patch_biz_1103c.py <입력.xml> <출력.xml>
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
    "[11/3 오후] 안내판 피드백 2회 반영(3 · 4차): 단계 설명 · 진행률 · 단계별 점수 · 결과 카드 뺌 → 유지 시간 · 반복 횟수 + 아이콘 버튼 [다음] [영상]만 · 작게(판 22cm) · 사용자 앞에서 느슨히 따라옴",
    "[11/3 오후] 전문가 영상(3인칭) 13개 술기에 맞춰 넣음 · 단계별 영상 구간을 CSV에 넣음(확신 높음 · 보통만) · 핵심 동작 16단계에서만 판 안 작은 영상(반복 재생)",
    "[11/3 오후] 정면 «전문가 영상 보기» 탭에 재생 앱식 조작 막대(재생 막대 · ⟲5 · ▶ · 5⟳ · 반복 · 속도 · 소리) · 빌드 오류 0 · Play 미확인 · 커밋 0",
]

DETAIL = [
  ("안내판 · 전문가 영상\n(11/3 오후)", [
    ("완료", "[11/3 오후] 안내판 4차 — 작게 · 따라옴(Play 미확인)", [
      "사용자 피드백: «UI · 버튼이 너무 커서 앞을 가린다» · «사이즈 줄이고 일정 범위 안에서 앞에 따라오게» · «텍스트 말고 아이콘» · «매 과정 점수 필요 없다 · 결과는 정면 큰 화면에»",
      "크기: 판 40 → 22cm · 글자 1.4 → 0.8cm · 버튼 18×8cm 글 → 5cm 아이콘",
      "자리: 눈앞 0.6m · 15° 아래 · 고개를 30° 넘게 돌리거나 0.4m 넘게 움직일 때만 천천히 따라감(환자 가슴 위 고정은 폐기)",
      "내용: 유지 시간 · 반복 횟수만 — 단계 설명 · 진행률 · 단계별 점수 · 끝 결과 카드 뺌"]),
    ("완료", "[11/3 오후] 전문가 영상 13개 · 단계 구간", [
      "3인칭 촬영본을 720p로 줄여 술기별 1개(제2늑골 = «제2늑골 교정기법» 첫 영상 · 사용자 선택)",
      "단계별 시작 · 끝 시각을 영상에서 읽어 CSV에 넣음 — 확신 높음 · 보통만(사용자 B) · 141줄",
      "사각근: 예전 구간이 상부승모근 것을 복사한 값(영상 길이 넘음)이라 지움 → 전체 영상 재생"]),
    ("완료", "[11/3 오후] 안내판 안 작은 영상(Play 미확인)", [
      "«너무 다 보여 주려 한다» → 핵심 동작 16단계에서만 [영상] 아이콘(목록 파일 하나로 넣고 뺌)",
      "옆에 뜨던 큰 창 폐기 → 판 안 16×9cm · 그 단계 구간만 반복 재생 · 재생 막대 · ⟲5 · ▶/❚❚ · ✕ · 소리 끔(나레이션과 겹침)"]),
    ("완료", "[11/3 오후] 정면 전문가 영상 탭 조작 막대(Play 미확인)", [
      "영상 아래 띠: 재생 막대 · ⟲5 · ▶/❚❚ · 5⟳ · 시간 · 반복 · 1×/0.5× · 소리",
      "반복을 끄면 구간 끝에서 멈춤(예전엔 구간이 지워졌음) · 13개 술기 공용"]),
    ("대기", "[11/3 오후] 남은 확인", [
      "Play: 따라오기 체감 · 아이콘 크기 · 판 안 영상 · 정면 막대(레이 · 손가락) · 소리 끔 · 다른 술기 1판",
      "가이드손 잡는 모양 고칠 방법 A/B/C 컨펌 대기 · 종료 직후 정면 창이 빈 원인 · 커밋 대기(git 잠금 파일)"]),
  ]),
]

ICON = "D:/추나녹화/20261103/캡처V2/09_안내판_미디어아이콘13종.png"


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


def icon_table():
    rows = [[cell([oe("날짜", bold=True)], "#F2F2F2"), cell([oe("화면", bold=True)], "#F2F2F2")],
            [cell([oe("11/3")]), cell([image_oe(ICON, 700.0),
                                       oe("⑨ 안내판 · 정면 영상 조작 아이콘 13종(그림 파일 · 헤드셋 화면 아님)", bold=True),
                                       oe("재생 · 일시정지 · ⟲5 · 5⟳ · 반복 켬/끔 · 1×/0.5× · 소리 켬/끔 · 닫기 · 다음 · 영상 — Play 캡처는 아직 없음")])]]
    return oe_table(table([48.0, 720.0], rows))


tree = ET.parse(sys.argv[1])
root = tree.getroot()

# ① 11/3 칸 끝에 덧붙임
scheds = []
for tb in root.iter(N + "Table"):
    rows = tb.findall(N + "Row")
    if rows and [cell_text(c) for c in rows[0].findall(N + "Cell")] == ["일", "수행항목"]:
        scheds.append((tb, rows))
if len(scheds) != 5:
    raise SystemExit(f"일정표가 {len(scheds)}개다(5개 기대) — 멈춘다")
target = None
for row in scheds[0][1][1:]:
    cs = row.findall(N + "Cell")
    if cell_text(cs[0]) == "11/3":
        target = cs
if target is None or not cell_text(target[1]).startswith("[11/3] 제2늑골"):
    raise SystemExit("11/3 칸이 예상과 다르다 — 멈춘다")
if "[11/3 오후]" in cell_text(target[1]):
    raise SystemExit("이미 덧붙였다 — 멈춘다")
kids = target[1].find(N + "OEChildren")
for line in DAY_ADD:
    kids.append(frag(oe(line)))

# ② 캡처 표 뒤에 새 구역
done = False
for oec in root.iter(N + "OEChildren"):
    lst = list(oec)
    for i, o in enumerate(lst):
        if own(o) == "화면 캡처 — 제2늑골 Play(11/3)" and i + 1 < len(lst) and lst[i + 1].find(N + "Table") is not None:
            at = list(oec).index(lst[i + 1])
            oec.insert(at + 1, frag(oe("세부 진행항목 — 안내판 · 전문가 영상(11/3 오후)", bullet=True)))
            oec.insert(at + 2, frag(detail_table(DETAIL)))
            oec.insert(at + 3, frag(oe("화면 — 조작 아이콘(11/3 오후)", bullet=True)))
            oec.insert(at + 4, frag(icon_table()))
            done = True
            break
    if done:
        break
if not done:
    raise SystemExit("캡처 표를 못 찾았다 — 멈춘다")

s = ET.tostring(root, encoding="unicode")
open(sys.argv[2], "w", encoding="utf-8").write('<?xml version="1.0"?>' + s)
print("ok")
