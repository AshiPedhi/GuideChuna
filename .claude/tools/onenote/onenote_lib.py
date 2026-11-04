# OneNote 페이지 XML 조립 도우미 — RISE-추나ROM 월별 페이지 양식(2026-09-18)
# 양식: 1열 큰 표 · 주차 머리줄(회색) · 내용 줄에 "• 금주 일정" 표(일|수행항목) + "• 세부 진행항목" 표(분류|상태|항목|세부)
from xml.sax.saxutils import escape

NS = "http://schemas.microsoft.com/office/onenote/2013/onenote"
STATUS_SHADE = {"완료": "#E2F0D9", "진행중": "#FFF2CC", "대기": "#FCE4D6"}
GROUP_SHADE = "#F2F2F2"


def T(s):
    return f"<one:T><![CDATA[{s}]]></one:T>"


def oe(text, children=None, bullet=False, bold=False):
    body = f"<span style='font-weight:bold'>{escape(text)}</span>" if bold else escape(text)
    x = "<one:OE>"
    if bullet:
        x += '<one:List><one:Bullet bullet="2" fontSize="11.0"/></one:List>'
    x += f"<one:T><![CDATA[{body}]]></one:T>"
    if children:
        x += "<one:OEChildren>" + "".join(children) + "</one:OEChildren>"
    return x + "</one:OE>"


def oe_table(tbl):
    return f"<one:OE>{tbl}</one:OE>"


def cell(items, shade=None):
    a = f' shadingColor="{shade}"' if shade else ""
    return f"<one:Cell{a}><one:OEChildren>" + "".join(items) + "</one:OEChildren></one:Cell>"


def table(cols, rows):
    c = "".join(f'<one:Column index="{i}" width="{w}" isLocked="true"/>' for i, w in enumerate(cols))
    return (f'<one:Table bordersVisible="true"><one:Columns>{c}</one:Columns>'
            + "".join("<one:Row>" + "".join(r) + "</one:Row>" for r in rows) + "</one:Table>")


def schedule(days):
    rows = [[cell([oe("일")]), cell([oe("수행항목")])]]
    for d, lines in days:
        rows.append([cell([oe(d)]), cell([oe(l) for l in lines] or [oe("")])])
    return oe_table(table([37.11, 1020.59], rows))


def _sub_oe(s):
    # ★(머리, [세부…])는 머리 아래 들여쓴 목록 — 월별 생성기의 sub_oe와 같다(09-22 도입, 09-28 라이브러리에도).
    #   문자열은 종전과 똑같이 나온다.
    if isinstance(s, tuple):
        head, kids = s
        return oe(head, [oe(k, bullet=True) for k in kids], bullet=True)
    return oe(s, bullet=True)


def detail_table(sections):
    """세부 진행항목 표. sections = [(분류, [(상태, 항목, [세부...]), ...]), ...]
    분류는 묶음 첫 줄에만 적고 음영을 준다(OneNote XML은 셀 병합을 안 써서). 분류 안의 줄바꿈(\\n)은 두 줄로."""
    rows = [[cell([oe("분류", bold=True)], GROUP_SHADE), cell([oe("상태", bold=True)], GROUP_SHADE),
             cell([oe("항목", bold=True)], GROUP_SHADE), cell([oe("세부 내용", bold=True)], GROUP_SHADE)]]
    for group, items in sections:
        for k, (status, title, subs) in enumerate(items):
            rows.append([
                cell([oe(x) for x in (group.split("\n") if k == 0 else [""])], GROUP_SHADE if k == 0 else None),
                cell([oe(status)], STATUS_SHADE.get(status)),
                cell([oe(title)]),
                cell([_sub_oe(s) for s in subs] or [oe("")]),
            ])
    return oe_table(table([95.0, 48.0, 300.0, 614.7], rows))


def week(name, days, extra=()):
    return [[cell([oe(name)], "#F2F2F2")], [cell([oe("금주 일정", bullet=True), schedule(days)] + list(extra))]]
