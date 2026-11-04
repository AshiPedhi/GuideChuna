# 사업화 11월 1주차 11/4 기록(2026-11-04 저녁) — 실습 화면 개편(상단 메뉴 · 개요 · 창 상시 · 미디어 메뉴) + Play 3판에서 나온 문제 고침.
# ★기존 줄은 안 바꾼다 — 빈 11/4 칸에 줄을 넣고, «화면 — 조작 아이콘(11/3 오후)» 표 바로 뒤에 새 머리줄 + 세부표 + 캡처 표(두 장씩 470pt)를 넣는다.
# 사용: patch_biz_1104.py <입력.xml> <출력.xml>
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
    "[11/4] 실습 화면 개편(사용자 피드백 6건): 상단 메뉴 순서 = 근골격 · 진행 현황 · 전문가 영상 | 설정 · 시작 · 홈으로 · [시작]을 누르면 그 자리에 실습 수행 시간 · 버튼을 가리키면 설명 창",
    "[11/4] 실습 씬에 들어가면 창 3개(근골격 · 진행 현황 · 영상)가 늘 켜짐 · 시작 전 «실습 개요»(술기명 · 진행 단계) · 개요에서 설정까지 마치고 [시작] 한 번 · 홈으로 팝업 = 취소 · 홈으로 2개(그림 ①)",
    "[11/4] 전문가 영상: [전문가 영상] 버튼 = 상단 메뉴 바로 아래 조작 메뉴 켜기/끄기(처음부터 · 재생 · 막대 · 1인칭/3인칭) · 기본 음소거 + 시간 맞춘 자막(자막 파일은 아직 0개) · [다음]은 [시작] 아래로(그림 ②⑥)",
    "[11/4] Play 3판에서 찾아 고침: 결과창 빈 화면(결과 글자가 창 밖 878px 위로 밀려 잘림 · 그림 ③④) · [시작] 때 위치 초기화 · 팔 3회 87%에서 셈 · 재평가에 «반복 3/3» 남음(그림 ⑤) · 근골격 카메라가 환자를 안 따라감",
    "[11/4] 빌드 오류 0 · Play 재확인 대기 · 커밋 0(되돌릴 기준점만 떠 둠)",
]

DETAIL = [
  ("실습 화면 개편\n(11/4 · 사용자 피드백)", [
    ("완료", "[11/4] 상단 메뉴 · [시작] 타이머 · 설명 창", [
      "순서: 근골격 · 진행 현황 · 전문가 영상 | 설정 · 시작 · 홈으로 — [시작]은 새로 만듦(재생 아이콘)",
      "[시작] → 실습 시작 + 그 자리에 수행 시간(m:ss · 결과 기록과 같은 시간) · 버튼을 가리키면 아래에 한 줄 설명",
      "효과음: 10/30에 넣은 «팡팡» 소리(ui_open · 두 번 타격) 뺌 → 기존 UI 소리만"]),
    ("완료", "[11/4] 창 3개 상시 · 실습 개요 · [시작] 한 번", [
      "들어가면 근골격(왼쪽) · 진행 현황(정면) · 영상(오른쪽)이 다 켜지고 상단 메뉴로 끄지 않음",
      "시작 전 진행 현황 창에 개요(술기명 · 진행 단계 — 목표 · 주의사항은 내용 대기) · 개요에서 [설정] 위치까지 맞추고 [시작]",
      "CSV 첫 «[시작]을 누르세요» 단계는 상단 [시작]이 대신 넘김 · [시작] 때 위치 초기화가 다시 돌아 맞춘 위치가 날아가던 것 고침"]),
    ("완료", "[11/4] 전문가 영상 조작 · 자막 · [다음]", [
      "[전문가 영상] 버튼 = 상단 메뉴 바로 아래 조작 메뉴 켜기/끄기 · 처음부터 · 재생/일시정지 · 막대 · 시간 · 1인칭/3인칭(1인칭 영상 있는 술기만)",
      "기본 음소거 · 자막 = 영상 시간에 맞춘 SRT(Videos/{이름}_자막.txt) — ★파일 0개 · 받아쓰기(Whisper) 설치 여부 대기",
      "[다음] = [시작 · 타이머] 바로 아래 흰 알약(정면 [계속하기] · [종료]와 같은 경로) · 옆 안내판 [다음] · [영상] 뺌 → «전문가 영상을 참고하세요» 안내만",
      "에디터 Play에서 [계속하기]가 5초에 뜨던 것 = 씬의 «에디터 시험용 대기 5초» → 20초로"]),
    ("완료", "[11/4] Play에서 찾은 문제 고침", [
      "결과창 빈 화면: 결과 글자(246자)는 매번 들어갔는데 글자 상자가 창 밖 위로 878px 밀려 판에 잘림 → 자리 바로잡음(에디터에서 보임 확인)",
      "팔 3회: 끝 접촉이 87%에서 세짐 → 92% 넘게 펴야 셈 · 3회 끝나고 1.5초 자세 유지 뒤 중립으로",
      "재평가 단계에 «반복 3/3» 남음(앞 단계 값 잔류) → 반복 단계에서만 표시 · 옆 안내판이 고개 숙임(위아래 시선)도 따라오게",
      "근골격 창 빈 화면: 골격 모델은 환자 이동 홀더 안이라 리모컨을 따라가는데 카메라는 안 따라감 → 모델에 묶고 모델 중심을 바라보게"]),
    ("대기", "[11/4] 남은 것", [
      "Play 재확인(결과창 · 근골격 · 팔 3회 · [시작] 한 번) · 다른 술기 1판 · 가이드손 위치 손보기(사용자 요청)",
      "자막 초안(받아쓰기 도구) · 개요 목표 · 주의사항 문구 · 기능별 커밋"]),
  ]),
]

SHOT_DIR = "D:/추나녹화/20261104/캡처"
SHOTS = [
  [("01_개요화면_상단메뉴.png", "① 실습 개요 · 새 상단 메뉴(0:28)", "시작 전 정면 창 = 개요 · 상단 6버튼(빨간 상자)"),
   ("02_시작타이머_아래_다음.png", "② [시작] → 수행 시간 · 그 아래 [다음](2:00)", "정면 [종료]와 같은 버튼 — 상단 메뉴 아래로 옮김")],
  [("03_결과창_빈화면_고치기전.png", "③ 결과창 빈 화면 — 고치기 전(2:12)", "로그엔 결과 246자 · 글자 상자가 창 밖 위로 밀림"),
   ("04_결과글자_자리바로잡은뒤_에디터.png", "④ 결과 글자 자리 바로잡은 뒤(에디터 시험 문구)", "헤드셋 화면 아님 · Play 재확인 대기")],
  [("05_재평가_반복3회_남음.png", "⑤ 재평가에 «반복 3/3» 남음 — 고치기 전(1:56)", "유지 막대 아래 주황 줄 = 앞 단계 값"),
   ("06_상단메뉴_다음판_미디어판.png", "⑥ 상단 메뉴 아래 판 둘(에디터 미리보기)", "왼쪽 = 미디어 조작 · 오른쪽 = [다음](실행 중 채워짐)")],
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
        cs = [cell([oe("11/4")])]
        for fname, ttl, cap in pair:
            cs.append(cell([image_oe(os.path.join(SHOT_DIR, fname), IMG_W), oe(ttl, bold=True), oe(cap)]))
        rows.append(cs)
    return oe_table(table([48.0, 490.0, 490.0], rows))


tree = ET.parse(sys.argv[1])
root = tree.getroot()

# ① 빈 11/4 칸에 줄 넣기
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
    if cell_text(cs[0]) == "11/4":
        target = cs
if target is None:
    raise SystemExit("11/4 칸이 없다 — 멈춘다")
if cell_text(target[1]) != "":
    raise SystemExit("11/4 칸이 비어 있지 않다(사용자가 썼을 수 있다) — 멈춘다")
kids = target[1].find(N + "OEChildren")
for o in list(kids):   # 빈 줄(글 없음)만 있는 것을 확인했다 — 그 빈 줄을 오늘 줄로 바꾼다
    kids.remove(o)
for line in DAY:
    kids.append(frag(oe(line)))

# ② «화면 — 조작 아이콘(11/3 오후)» 표 뒤에 새 구역
done = False
for oec in root.iter(N + "OEChildren"):
    lst = list(oec)
    for i, o in enumerate(lst):
        if own(o) == "화면 — 조작 아이콘(11/3 오후)" and i + 1 < len(lst) and lst[i + 1].find(N + "Table") is not None:
            at = list(oec).index(lst[i + 1])
            oec.insert(at + 1, frag(oe("세부 진행항목 — 실습 화면 개편(11/4)", bullet=True)))
            oec.insert(at + 2, frag(detail_table(DETAIL)))
            oec.insert(at + 3, frag(oe("화면 캡처 — 실습 화면 개편 · 제2늑골 Play(11/4)", bullet=True)))
            oec.insert(at + 4, frag(shots_table()))
            done = True
            break
    if done:
        break
if not done:
    raise SystemExit("11/3 오후 아이콘 표를 못 찾았다 — 멈춘다")

s = ET.tostring(root, encoding="unicode")
open(sys.argv[2], "w", encoding="utf-8").write('<?xml version="1.0"?>' + s)
print("ok")
