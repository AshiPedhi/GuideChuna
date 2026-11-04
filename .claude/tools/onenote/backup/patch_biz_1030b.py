# 사업화 10월 5주차 — 10/30 분 그림 추가(11/02 사용자 «새로 작성된 부분에 대한 이미지가 없네»). 글은 건드리지 않고 ㉛ 표 바로 뒤에 ㉜ ㉝만 넣는다.
# 사용: patch_biz_1030b.py <입력.xml> <출력.xml> <그림 폴더>
import sys, os, re, html, base64
HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, HERE)
from onenote_lib import oe, oe_table, table, cell, NS
from xml.etree import ElementTree as ET
N = "{" + NS + "}"
ET.register_namespace("one", NS)
SHOTS = [
    ("32_암실바닥.png", "㉜ 실습 배경 — 암실 바닥 · 0.5m 격자", "방 모델을 끄고 베드 · 환자만 남긴 배경 · 11/02 에디터 화면으로 찍음(기기 화면 아님)"),
    ("33_로비거리.png", "㉝ 로비 판 거리 — 1.32m로 뒤로 민 화면", "10/30 12:52 에디터 화면 · 이 뒤에 판을 1.5배 키움(그 화면은 캡처 없음)"),
]
def own(o):
    s = "".join(x.text or "" for x in o.findall(N + "T"))
    return re.sub("<[^>]+>", "", html.unescape(s)).strip()
def frag(x):
    return ET.fromstring(f'<root xmlns:one="{NS}">{x}</root>')[0]
def image_oe(path, w):
    with open(path, "rb") as f: d = f.read()
    if d[:8] != b"\x89PNG\r\n\x1a\n": raise SystemExit("PNG가 아니다: " + path)
    pw, ph = int.from_bytes(d[16:20], "big"), int.from_bytes(d[20:24], "big")
    return (f'<one:OE><one:Image format="png"><one:Size width="{w}" height="{round(w*ph/pw,1)}" isSetByUser="true"/>'
            f'<one:Data>{base64.b64encode(d).decode("ascii")}</one:Data></one:Image></one:OE>')
rows = [[cell([oe("날짜", bold=True)], "#F2F2F2"), cell([oe("화면", bold=True)], "#F2F2F2")]]
for f, t, c in SHOTS:
    rows.append([cell([oe("10/30")]), cell([image_oe(os.path.join(sys.argv[3], f), 700.0), oe(t, bold=True), oe(c)])])
tree = ET.parse(sys.argv[1]); root = tree.getroot()
hit = None
for oec in root.iter(N + "OEChildren"):
    k = list(oec)
    for i, o in enumerate(k):
        if "팝업 3종 로비 규격(10/30)" in own(o) and i + 1 < len(k) and k[i + 1].find(N + "Table") is not None:
            hit = (oec, i + 1)
if hit is None: raise SystemExit("10/30 그림 표를 못 찾았다 — 멈춘다")
hit[0].insert(hit[1] + 1, frag(oe("그림 — 실습 배경 · 로비 판 거리(10/30)", bullet=True)))
hit[0].insert(hit[1] + 2, frag(oe_table(table([48.0, 1010.0], rows))))
open(sys.argv[2], "w", encoding="utf-8").write('<?xml version="1.0"?>' + ET.tostring(root, encoding="unicode"))
print("ok", len(SHOTS))
