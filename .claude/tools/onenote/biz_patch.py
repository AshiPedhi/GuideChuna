# 사업화 월별 페이지(사용자가 쓰는 페이지)의 <b>빈 칸에만</b> 내용을 넣는다(2026-09-18).
# ★기존 글은 건드리지 않는다. 빈 칸이 아니면 멈춘다.
# 사용:
#   biz_patch.py fill  <입력.xml> <출력.xml> <날짜> <줄1> [줄2 ...]      — 일정표에서 그 날짜 줄의 수행항목 칸이 비어 있으면 채운다
#   biz_patch.py week3 <입력.xml> <출력.xml>                              — 9월 3주차: 일정표 채우기 + 세부 진행항목 표 덧붙이기
#   biz_patch.py texts <xml>                                              — 문단 글 목록(검증용)
import sys, os, re, html, runpy, copy
from xml.etree import ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from onenote_lib import NS, oe, oe_table, detail_table

N = "{" + NS + "}"
ET.register_namespace("one", NS)


def own(o):
    s = "".join(x.text or "" for x in o.findall(N + "T"))
    return re.sub("<[^>]+>", "", html.unescape(s)).strip()


def cell_text(c):
    return " ".join(own(o) for o in c.iter(N + "OE")).strip()


def schedules(root):
    for tb in root.iter(N + "Table"):
        rows = tb.findall(N + "Row")
        if rows and [cell_text(c) for c in rows[0].findall(N + "Cell")] == ["일", "수행항목"]:
            yield tb, rows


def set_cell(c, lines):
    """빈 칸의 첫 문단 글을 바꾸고, 나머지 줄은 문단을 복사해 덧붙인다."""
    if cell_text(c):
        raise SystemExit(f"빈 칸이 아니다 — 멈춘다: '{cell_text(c)[:40]}'")
    kids = c.find(N + "OEChildren")
    first = kids.find(N + "OE")
    t = first.find(N + "T")
    if t is None:
        t = ET.SubElement(first, N + "T")
    t.text = html.escape(lines[0])
    for line in lines[1:]:
        o = copy.deepcopy(first)
        o.attrib.pop("objectID", None)
        for k in list(o.attrib):
            if k in ("creationTime", "lastModifiedTime", "author", "authorInitials", "lastModifiedBy", "lastModifiedByInitials"):
                o.attrib.pop(k)
        o.find(N + "T").text = html.escape(line)
        kids.append(o)


def frag(xml_str):
    return ET.fromstring(f'<root xmlns:one="{NS}">{xml_str}</root>')[0]


def load(p):
    return ET.parse(p)


def save(tree, p):
    s = ET.tostring(tree.getroot(), encoding="unicode")
    open(p, "w", encoding="utf-8").write('<?xml version="1.0"?>' + s)


def texts(p):
    return [own(o) for o in ET.parse(p).getroot().iter(N + "OE") if own(o)]


cmd = sys.argv[1]
if cmd == "texts":
    for t in texts(sys.argv[2]):
        print(t)
    sys.exit(0)

tree = load(sys.argv[2])
root = tree.getroot()

if cmd == "fill":
    day, lines = sys.argv[4], sys.argv[5:]
    hit = 0
    for tb, rows in schedules(root):
        for row in rows[1:]:
            cs = row.findall(N + "Cell")
            if cell_text(cs[0]) == day:
                set_cell(cs[1], lines)
                hit += 1
    if hit != 1:
        raise SystemExit(f"'{day}' 줄이 {hit}개다 — 하나가 아니라 멈춘다")

elif cmd == "week3":
    # 9월 생성기의 3주차 데이터를 그대로 쓴다(두 페이지 내용이 어긋나지 않게)
    old_argv = sys.argv
    sys.argv = ["x", "{T}", os.devnull]
    g = runpy.run_path(os.path.join(HERE, "month_page_2026_09.py"))
    sys.argv = old_argv
    W3, DETAIL3 = dict(g["W3"]), g["DETAIL3"]

    target = None
    for tb, rows in schedules(root):
        firsts = [cell_text(r.findall(N + "Cell")[0]) for r in rows[1:]]
        if firsts[:2] == ["14", "15"]:
            target = (tb, rows)
    if target is None:
        raise SystemExit("3주차 일정표(14·15)를 못 찾았다 — 멈춘다")
    tb, rows = target
    days = ["14", "15", "16", "17", "18"]
    for row, d in zip(rows[1:], days):
        cs = row.findall(N + "Cell")
        if not cell_text(cs[0]):
            set_cell(cs[0], [d])
        set_cell(cs[1], W3["9/" + d] or ["—"])

    # 일정표 문단 바로 뒤에 "세부 진행항목" + 표를 덧붙인다
    parent = None
    for oec in root.iter(N + "OEChildren"):
        for i, o in enumerate(list(oec)):
            if o.find(N + "Table") is tb:
                parent, idx = oec, i
    if parent is None:
        raise SystemExit("일정표 문단의 부모를 못 찾았다 — 멈춘다")
    parent.insert(idx + 1, frag(oe("세부 진행항목", bullet=True)))
    parent.insert(idx + 2, frag(detail_table(DETAIL3)))
elif cmd == "week4":
    # 9월 4주차(9/21~25). 3주차와 같은 자리·같은 방식이되, 날짜 칸까지 비어 있어
    # 날짜로는 표를 못 고른다 — 문서 순서로 네 번째 일정표를 쓰고, 통째로 비었는지 확인한다.
    old_argv = sys.argv
    sys.argv = ["x", "{T}", os.devnull]
    g = runpy.run_path(os.path.join(HERE, "month_page_2026_09.py"))
    sys.argv = old_argv
    W4, DETAIL4 = dict(g["W4"]), g["DETAIL4"]

    scheds = list(schedules(root))
    if len(scheds) != 5:
        raise SystemExit(f"일정표가 {len(scheds)}개다(5개 기대) — 멈춘다")
    tb, rows = scheds[3]
    if any(cell_text(c) for r in rows[1:] for c in r.findall(N + "Cell")):
        raise SystemExit("4주차 일정표가 비어 있지 않다 — 멈춘다")

    days = ["21", "22", "23", "24", "25"]
    for row, d in zip(rows[1:], days):
        cs = row.findall(N + "Cell")
        set_cell(cs[0], [d])
        lines = W4.get("9/" + d) or []
        if lines:
            set_cell(cs[1], lines)

    parent = None
    for oec in root.iter(N + "OEChildren"):
        for i, o in enumerate(list(oec)):
            if o.find(N + "Table") is tb:
                parent, idx = oec, i
    if parent is None:
        raise SystemExit("일정표 문단의 부모를 못 찾았다 — 멈춘다")
    parent.insert(idx + 1, frag(oe("세부 진행항목", bullet=True)))
    parent.insert(idx + 2, frag(detail_table(DETAIL4)))

elif cmd == "week5":
    # 9월 5주차(9/28~30, 2026-09-28 신설). 4주차와 같은 방식 — 날짜 칸까지 비어 있어 문서 순서로 다섯 번째 일정표를 쓰고,
    # 통째로 비었는지 확인한다. ★행은 5개지만 9월은 30일까지라 날짜는 세 행에만 넣는다(10/1·10/2는 10월 페이지 몫).
    old_argv = sys.argv
    sys.argv = ["x", "{T}", os.devnull]
    g = runpy.run_path(os.path.join(HERE, "month_page_2026_09.py"))
    sys.argv = old_argv
    W5, DETAIL5 = dict(g["W5"]), g["DETAIL5"]

    scheds = list(schedules(root))
    if len(scheds) != 5:
        raise SystemExit(f"일정표가 {len(scheds)}개다(5개 기대) — 멈춘다")
    tb, rows = scheds[4]
    if any(cell_text(c) for r in rows[1:] for c in r.findall(N + "Cell")):
        raise SystemExit("5주차 일정표가 비어 있지 않다 — 멈춘다")

    days = ["28", "29", "30"]
    for row, d in zip(rows[1:], days):
        cs = row.findall(N + "Cell")
        set_cell(cs[0], [d])
        lines = W5.get("9/" + d) or []
        if lines:
            set_cell(cs[1], lines)

    parent = None
    for oec in root.iter(N + "OEChildren"):
        for i, o in enumerate(list(oec)):
            if o.find(N + "Table") is tb:
                parent, idx = oec, i
    if parent is None:
        raise SystemExit("일정표 문단의 부모를 못 찾았다 — 멈춘다")
    parent.insert(idx + 1, frag(oe("세부 진행항목", bullet=True)))
    parent.insert(idx + 2, frag(detail_table(DETAIL5)))

else:
    raise SystemExit("모르는 명령: " + cmd)

save(tree, sys.argv[3])
print("ok")
