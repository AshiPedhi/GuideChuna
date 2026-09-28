"""실측 기록 판의 아이콘 버튼 그림을 만든다(2026-09-28 사용자: "버튼도 글씨 말고 아이콘으로 — 글씨는 조잡해").

기존 탭 그림(Assets/Resources/RomRecordUI/Icons/rotation.png 등)과 같은 꼴이다:
256px RGBA · 투명 바탕 · 연회색 선 + 청록 강조.

사용법 (기본은 dry-run — 무엇을 쓸지만 말한다):
  python rom_record_icons.py            # 목록만
  python rom_record_icons.py --write    # 없는 파일만 쓴다
  python rom_record_icons.py --write --force   # 있는 것도 덮는다
"""
import math
import os
import sys

from PIL import Image, ImageDraw, ImageOps

OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "Resources", "RomRecordUI", "Icons")
SIZE = 256
SS = 4                                   # 4배로 그려 줄인다(가장자리를 부드럽게)
GRAY = (235, 235, 235, 255)
CYAN = (77, 225, 240, 255)
W = 14 * SS                              # 선 굵기


def canvas():
    im = Image.new("RGBA", (SIZE * SS, SIZE * SS), (0, 0, 0, 0))
    return im, ImageDraw.Draw(im)


def P(x, y):
    """0~256 좌표를 확대 좌표로."""
    return (x * SS, y * SS)


def arrow(d, tail, head, color, w=W, head_len=58, head_w=46):
    """꼬리→머리 화살표. 머리는 채운 삼각형."""
    tx, ty = tail
    hx, hy = head
    ang = math.atan2(hy - ty, hx - tx)
    bx, by = hx - math.cos(ang) * head_len, hy - math.sin(ang) * head_len
    d.line([P(tx, ty), P(bx, by)], fill=color, width=w)
    nx, ny = -math.sin(ang), math.cos(ang)
    d.polygon([P(hx, hy), P(bx + nx * head_w / 2, by + ny * head_w / 2), P(bx - nx * head_w / 2, by - ny * head_w / 2)],
              fill=color)


def chevrons(d, n, right):
    """‹ · « · ⋘ — 개수가 크기를 말한다(0.1° · 1° · 5°)."""
    gap = 44
    total = (n - 1) * gap
    for i in range(n):
        cx = 128 - total / 2 + i * gap
        s = 1 if right else -1
        pts = [P(cx - s * 26, 70), P(cx + s * 26, 128), P(cx - s * 26, 186)]
        d.line(pts, fill=CYAN, width=W, joint="curve")


def floor(d):
    """원근 바닥(사다리꼴) — «앞·뒤»를 위·아래와 가른다."""
    d.polygon([P(88, 70), P(168, 70), P(214, 200), P(42, 200)], outline=GRAY, width=6 * SS)


def yaw(d, ccw):
    """↻ / ↺ — 끊긴 원 + 끝의 화살촉. ★09-28 첫 판은 납작한 타원이라 «눈»처럼 보였다 — 둥근 원으로 다시 그렸다.
    PIL 각은 3시 방향에서 시계 방향으로 는다. 시계 방향(↻)을 그리고, 반시계(↺)는 좌우로 뒤집는다(호출부)."""
    cx, cy, r = 128, 136, 74
    a0, a1 = -20, 215                       # 위쪽이 비어 있다(화살촉이 그 틈을 가리킨다)
    d.arc([P(cx - r, cy - r), P(cx + r, cy + r)], start=a0, end=a1 + 4, fill=CYAN, width=W)
    a = math.radians(a1)
    px, py = cx + r * math.cos(a), cy + r * math.sin(a)
    tx, ty = -math.sin(a), math.cos(a)      # 시계 방향 접선
    nx, ny = math.cos(a), math.sin(a)       # 바깥쪽
    tip = (px + tx * 52, py + ty * 52)
    d.polygon([P(*tip), P(px + nx * 38, py + ny * 38), P(px - nx * 38, py - ny * 38)], fill=CYAN)
    d.ellipse([P(cx - 10, cy - 10), P(cx + 10, cy + 10)], fill=GRAY)   # 가운데 = 도는 축


def check(d):
    d.line([P(62, 132), P(108, 180), P(196, 80)], fill=CYAN, width=18 * SS, joint="curve")


ICONS = {
    # 기준점 위치 조정 판
    "nudge_up": lambda d: arrow(d, (128, 208), (128, 44), CYAN),
    "nudge_down": lambda d: arrow(d, (128, 48), (128, 212), CYAN),
    "nudge_left": lambda d: arrow(d, (212, 128), (44, 128), CYAN),
    "nudge_right": lambda d: arrow(d, (44, 128), (212, 128), CYAN),
    "nudge_fwd": lambda d: (floor(d), arrow(d, (128, 190), (128, 60), CYAN, head_len=44, head_w=36)),
    "nudge_back": lambda d: (floor(d), arrow(d, (128, 76), (128, 214), CYAN, head_len=52, head_w=48)),
    "yaw_left": lambda d: yaw(d, True),     # ↺ — 그린 뒤 좌우로 뒤집는다(MIRROR)
    "yaw_right": lambda d: yaw(d, False),   # ↻
    # 바늘 각도 판 — 화살표 개수가 크기(0.1° · 1° · 5°)
    "deg_dec1": lambda d: chevrons(d, 1, False),
    "deg_dec2": lambda d: chevrons(d, 2, False),
    "deg_dec3": lambda d: chevrons(d, 3, False),
    "deg_inc1": lambda d: chevrons(d, 1, True),
    "deg_inc2": lambda d: chevrons(d, 2, True),
    "deg_inc3": lambda d: chevrons(d, 3, True),
    "confirm": check,
}
MIRROR = {"yaw_left"}


def main():
    write = "--write" in sys.argv
    force = "--force" in sys.argv
    out = os.path.normpath(OUT)
    for name, draw in ICONS.items():
        path = os.path.join(out, name + ".png")
        exists = os.path.exists(path)
        if exists and not force:
            print(f"건너뜀(있음) {name}.png")
            continue
        if not write:
            print(f"[dry-run] 쓸 것 {name}.png")
            continue
        im, d = canvas()
        draw(d)
        if name in MIRROR:
            im = ImageOps.mirror(im)
        im = im.resize((SIZE, SIZE), Image.LANCZOS)
        im.save(path)
        print(f"썼다 {name}.png")


if __name__ == "__main__":
    main()
