# 사업화 10월 5주차 10/26 — 퇴근 기록(실제 사람 가이드 V0.3 · V0.4 · VR/XR 정의 정정)을 덧붙인다.
# ★오후 패치(patch_biz_1026b.py) 결과 위에 덧붙인다 — 기대한 구조가 아니면 멈춘다.
# 사용: patch_biz_1026c.py <입력.xml> <출력.xml>
import sys, os, re, html
HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)
from onenote_lib import oe, detail_table, NS
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
    "[10/26 저녁] 실제 사람 가이드 제안 1 · 2를 한 문서로 정리(V0.3) → 손 가려짐 반영(V0.4) — 각도는 측정 전 참고 표시만",
    "[10/26 저녁] 정의 정정(사용자) — VR(가상환자) · XR(실제 사람)은 UI · UX만 같게, 실습 진행 로직은 각 환경에 맞게 따로 가도 된다 → 문서 V0.5 개정 예정",
]

GROUP = ("실제 사람 가이드\n(10/26 저녁)", [
    ("완료", "[10/26] 제안 1 · 2 정리 V0.3", [
        "지금 방식 = 설정 «환자위치조절» 핸들로 모델을 사람에 비슷하게 맞춰 놓고 플레이(사용자 정정 반영)",
        "남는 문제 = 위치가 조금씩 다름 + 신체 조건(머리 둘레 · 흉곽 두께) 차이",
        "D:/CoCoChuna_추나가이드_실제사람_제안정리_V0.3_20261026.pdf"]),
    ("완료", "[10/26] 손 가려짐 반영 V0.4 — «회전하면 손이 머리에 가려져 추적이 튄다(ROM 핸드트래킹을 접은 이유)»", [
        "양손 선 각도(제안 2)는 두 손이 다 보여야 해서 가장 약하다 · 컨트롤러는 손 파지라 못 쓴다",
        "대안: A 신뢰도 낮은 프레임 버림 · B 보이는 한 손 손목 회전 · C 각도는 참고만(유지 시간 · [계속하기]로 진행)",
        "측정 전에는 C · 1단계 = 상부승모근 전부(前部)에서 손마다 가려짐부터 잰다",
        "D:/CoCoChuna_추나가이드_실제사람_제안정리_V0.4_20261026.pdf"]),
    ("대기", "[10/26] VR · XR 정의 정정 → 문서 V0.5", [
        "같게: 가이드 손 · 진행 표시 · 안내 · 단계 흐름 · 버튼(UI · UX)",
        "따로: VR은 지금 판정 그대로, XR은 실제 사람 환경에 맞춘 진행 로직",
        "확인할 것: XR 모드 = 실제 사람 실습 모드인지(지금 «현실 모드»는 패스스루 스위치) · 측정 도구 기기/Link",
        "미구현 · Play 미검증"]),
])

tree = ET.parse(sys.argv[1])
root = tree.getroot()

scheds = [tb for tb in root.iter(N + "Table")
          if (r := tb.findall(N + "Row")) and [cell_text(c) for c in r[0].findall(N + "Cell")] == ["일", "수행항목"]]
if len(scheds) != 5:
    raise SystemExit(f"일정표가 {len(scheds)}개다(5개 기대) — 멈춘다")
row = [r for r in scheds[4].findall(N + "Row")[1:] if cell_text(r.findall(N + "Cell")[0]) == "10/26"]
if len(row) != 1:
    raise SystemExit("5주차 10/26 행을 못 찾았다 — 멈춘다")
c1 = row[0].findall(N + "Cell")[1]
if "[10/26 저녁]" in cell_text(c1):
    raise SystemExit("이미 저녁 기록이 있다 — 멈춘다")
kids = c1.find(N + "OEChildren")
for line in DAY_ADD:
    kids.append(frag(oe(line)))

dts = [tb for tb in root.iter(N + "Table")
       if (r := tb.findall(N + "Row")) and [cell_text(c) for c in r[0].findall(N + "Cell")] == ["분류", "상태", "항목", "세부 내용"]
       and "능동 · 수동 한계" in cell_text(r[1].findall(N + "Cell")[0]) if len(r) > 1]
if len(dts) != 1:
    raise SystemExit(f"10/26 세부 진행항목 표가 {len(dts)}개다(1개 기대) — 멈춘다")
new_rows = frag(detail_table([GROUP])).find(N + "Table").findall(N + "Row")[1:]
for r in new_rows:
    dts[0].append(r)

s = ET.tostring(root, encoding="unicode")
open(sys.argv[2], "w", encoding="utf-8").write('<?xml version="1.0"?>' + s)
print("ok", len(new_rows), "rows")
