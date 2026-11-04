# 사업화 11월 1주차 11/3 기록 고쳐 쓰기(2026-11-03 사용자 «캡처 내용이 너무 대충 · 이미지가 너무 커서 문서가 길어진다 · 새 UI 내용 더 · 가이드 파지 ≠ 실제 파지»).
# ★patch_biz_1103.py가 넣은 세 덩어리만 바꾼다 — 11/3 칸 · 세부표(«세부 진행항목 — 제2늑골 교정 단계 Play(11/3)» 다음 표) · 캡처 표(«화면 캡처 — 제2늑골 Play(11/3)» 다음 표).
# ★캡처는 한 칸에 두 장씩(폭 470pt) — 예전 판은 한 줄에 한 장 800pt라 페이지가 길었다.
# 사용: patch_biz_1103b.py <입력.xml> <출력.xml>
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


DAY = [
    "[11/3] 제2늑골 — 교정 동작을 전문가 영상(1인칭 · 3인칭)에 맞춰 다시 만듦: 굽힌 팔을 머리 쪽으로 당기며 폈다가 다시 굽힘 ×3(V04)",
    "[11/3] 진행도 = 녹화 궤적의 가장 가까운 점 · 가이드손이 사용자보다 조금 앞에서 따라감 · 누운 환자 리모컨을 머리 쪽 베드 끝 위로",
    "[11/3] 새 UI «환자 옆 안내판» — 환자 가슴 위 사용자 쪽에 단계 · 할 일 · 유지 시간 · 진행 % · 반복 횟수(13개 술기 공용 · 그림 ①②)",
    "[11/3] Play 1판: 교정 3단계 끝까지 · 직접 3회 왕복 6/6 · 3-7 67점(C, 직전 35점) · 종합 86.7점(A)(그림 ③④)",
    "[11/3] 드러난 문제: 가이드손 잡는 모양 ≠ 실제 잡는 모양(그림 ⑤) · 안내판 각도 · 글 넘침 · 결과 안 보임(그림 ⑥) → 안내판 2차 수정(그림 ⑦ · Play 미확인) · 커밋 0",
]

DETAIL = [
  ("제2늑골 교정 단계\n(11/3)", [
    ("완료", "[11/3] 교정 동작 V04(그림 ⑧)", [
      "시작 = 3-4 «팔 외전» 끝 자세(팔꿈치 굽힘 · 손이 머리 쪽) → 머리 쪽으로 당기며 폄 → 다시 굽혀 돌아옴",
      "손 높이(어깨 기준) +7.0 → +10.7 → +8.2cm — 가운데만 약 4cm 솟는 포물선(리그 실측) · 끝 자세 오차 0.3°"]),
    ("완료", "[11/3] 진행도 · 가이드손 · 리모컨", [
      "진행도: 직선 투영 → 녹화 궤적 가장 가까운 점(3-5 · 3-6 · 3-7) — 직선은 궤적이 휘면 멈추거나 되돌아갔음",
      "가이드손: 한 번 재생 후 사용자 진행보다 12% 앞에서 따라감 · 파지 거리 159 → 106mm",
      "리모컨: 누운 환자는 머리 쪽 베드 끝 위 · 높이 1.24m(눈 1.67m)"]),
    ("진행중", "[11/3] 새 UI — 환자 옆 안내판(그림 ①②⑥⑦)", [
      "왜: 시술 중 시선은 환자 손 · 팔에 있는데 지시 · 유지 시간 · 진행은 정면 큰 창에만 있었다 → 환자 곁에 한 장 더(정면 창은 그대로)",
      "자리: 환자 가슴 위 30cm · 사용자 쪽 30cm · 단계 시작 때 한 번 놓음(따라다니지 않음) · 상부승모근 · 경추ROM은 숨김",
      "보여 주는 것: 단계 이름 · 지시 · 유지 막대(남은 초) · 진행 막대(%) · 반복 n/3회 — 값은 정면 창 · 판정 값을 그대로 읽음",
      "Play 1판: 19단계 모두 자리 잡음 · 유지 · 진행 · 반복 줄 정상 — 다만 비스듬히 누움 · 글 넘침 · 글자 작음(약 0.9cm)",
      "2차 수정(Play 미확인): 15° 넘게 어긋나면 사용자 쪽으로 다시 향함 · 안내판 버튼(정면 [시작 · 계속하기 · 종료]와 같은 동작 · 손가락으로 누름) · 채점 직후 점수 8초 · 끝나면 결과 카드 · 할 일 한 줄로 짧게 · 글자 1.4cm"]),
    ("진행중", "[11/3] 가이드손 잡는 모양 ≠ 실제(그림 ⑤)", [
      "가이드: 주먹 쥔 채 손등이 위 · 환자 손목 등 쪽에 얹혀 몇 cm 떠 있음",
      "사용자 실제(3-7): 편 손을 C자로 손목을 감싸 쥠 · 한 번은 환자 손바닥 아래에서 악수처럼 받침 · 손 방향 약 90° 다름(눈대중)",
      "원인(추정): 가이드 파지를 예전 «제2늑골 교정1» 녹화의 한 프레임에서 떼어 붙임 — 거리만 줄였고 손 모양 · 방향은 안 맞춤",
      "고칠 방법(컨펌 대기): A 실제 잡은 손 기록해 쓰기 · B 새 동작으로 가이드 다시 녹화 · C 전문가 영상에서 읽기"]),
    ("대기", "[11/3] 문서 · 남은 확인", [
      "문서: D:/CoCoChuna_제2늑골_교정단계_Play진행보고_V0.2_20261103.pdf",
      "미확인: 2차 안내판 전부 · 다른 술기 안내판 · 종료 직후 정면 창이 빈 원인"]),
  ]),
]

SHOT_DIR = "D:/추나녹화/20261103/캡처V2"
# 두 장씩 한 줄 — (파일, 제목, 설명)
SHOTS = [
  [("01_새UI_환자옆안내판_위치.png", "① 새 UI 환자 옆 안내판 · 리모컨(0:08)", "빨간 상자 = 새로 넣은 것"),
   ("02_새UI_안내판_표시내용.png", "② 안내판 줄 — 유지 · 진행 % · 반복", "값은 정면 창 · 판정 값 그대로")],
  [("03_3-5_3-6_가이드손_따라하기.png", "③ 3-5 당겨 펴기 → 3-6 굽혀 돌아오기", "가이드손(초록)을 따라 함 · 칸 아래 % = 로그 진행"),
   ("04_3-7_직접반복.png", "④ 3-7 직접 3회 — 끝 · 처음 왕복", "왕복 6/6 · 67점(C)")],
  [("08_가이드손_파지_vs_실제파지.png", "⑤ 가이드손 잡는 모양 vs 실제 잡은 모양", "위 = 가이드(주먹 · 손목 위) · 아래 = 사용자(손목 감싸 쥠)"),
   ("05_남은문제_안내판_결과.png", "⑥ 드러난 문제 — 각도 · 글 넘침 · 결과 표시", "종료 직후 정면 창이 빔(로그엔 86.7점 A)")],
  [("07_안내판_2차_구성_시안.png", "⑦ 안내판 2차 구성(시안 · 헤드셋 화면 아님)", "할 일 한 줄 · 버튼 · 점수 · 결과 카드"),
   ("06_V04_당겨펴기_졸라맨.png", "⑧ 새 팔 동작 V04(리그 실측 졸라맨)", "굽힌 팔에서 머리 쪽으로 당기며 폄")],
]
IMG_W = 470.0


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
    rows = [[cell([oe("날짜", bold=True)], "#F2F2F2"), cell([oe("화면", bold=True)], "#F2F2F2"), cell([oe("화면", bold=True)], "#F2F2F2")]]
    for pair in SHOTS:
        cs = [cell([oe("11/3")])]
        for fname, ttl, cap in pair:
            cs.append(cell([image_oe(os.path.join(SHOT_DIR, fname), IMG_W), oe(ttl, bold=True), oe(cap)]))
        rows.append(cs)
    return oe_table(table([48.0, 490.0, 490.0], rows))


tree = ET.parse(sys.argv[1])
root = tree.getroot()

# ① 11/3 칸
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
kids = target[1].find(N + "OEChildren")
for o in list(kids):
    kids.remove(o)
for line in DAY:
    kids.append(frag(oe(line)))


# ②③ 머리줄 다음 표를 바꾼다
def replace_after(head, new_xml):
    for oec in root.iter(N + "OEChildren"):
        lst = list(oec)
        for i, o in enumerate(lst):
            if own(o) == head and i + 1 < len(lst) and lst[i + 1].find(N + "Table") is not None:
                oec.remove(lst[i + 1])
                oec.insert(i + 1, frag(new_xml))
                return
    raise SystemExit(f"«{head}» 다음 표를 못 찾았다 — 멈춘다")


replace_after("세부 진행항목 — 제2늑골 교정 단계 Play(11/3)", detail_table(DETAIL))
replace_after("화면 캡처 — 제2늑골 Play(11/3)", shots_table())

s = ET.tostring(root, encoding="unicode")
open(sys.argv[2], "w", encoding="utf-8").write('<?xml version="1.0"?>' + s)
print("ok")
