# 선 실측(RomLineScene)의 단계별 안내 음성을 만든다 — edge-tts, 기존 술기 나레이션과 같은 목소리.
#   사용: python romline_narration.py            → 만들 파일만 보여 준다(dry-run)
#         python romline_narration.py --write    → 없는 파일만 만든다
#         python romline_narration.py --write --force [--only m0_p1,done]   → 문구를 고쳤을 때 덮어쓴다
#   출력: Assets/Resources/RomLine/Narration/m{동작}_p{단계}.mp3 · m{동작}_done.mp3 · done.mp3
#         동작 0 굴곡 · 1 신전 · 2 우측굴 · 3 좌측굴 · 4 우회전 · 5 좌회전 / 단계 0 중립 · 1 능동 · 2 압박
#   ★RomLineSession이 이 이름으로 읽는다(Resources.Load "RomLine/Narration/m0_p0"). 이름을 바꾸면 조용히 무음이 된다.
#   ★지우고 다시 만들지 않는다 — 같은 경로에 덮어써야 Unity가 guid를 유지한다. .meta는 Unity가 만든다.
#   ★단면 용어(시상면·관상면·횡단면)는 말하지 않는다(CLAUDE.md, 09-11 사용자 지시).
import argparse, asyncio, os, sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Resources", "RomLine", "Narration")
VOICE = "ko-KR-SunHiNeural"

# ★10-13 사용자 지시: 음성은 화면의 안내 글 그대로다 — 어느 점을 찍으라는 말, 자세 설명을 하지 않는다.
#   화면 글은 RomLineSession.Refresh가 만든다(«굴곡 · 중립을 측정하세요» · «굴곡 능동 ○° · 압박 ○°» + «중립으로 돌아간 뒤 트리거 = 기록»). 한쪽을 고치면 다른 쪽도 고친다.
MOTIONS = ["굴곡", "신전", "우측굴", "좌측굴", "우회전", "좌회전"]   # RomLineSession.MotionName과 같다
PHASES = ["중립", "능동", "압박"]


def texts():
    out = {}
    for m, name in enumerate(MOTIONS):
        for p, phase in enumerate(PHASES):
            out["m%d_p%d" % (m, p)] = "%s, %s을 측정하세요." % (name, phase)
        out["m%d_done" % m] = "%s 측정 완료. 중립으로 돌아간 뒤 트리거로 확인하세요." % name
    out["done"] = "모든 측정 완료."
    return out


async def synth(text, path):
    import edge_tts
    tmp = path + ".part"
    await edge_tts.Communicate(text, VOICE).save(tmp)
    os.replace(tmp, path)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--write", action="store_true")
    ap.add_argument("--force", action="store_true")
    ap.add_argument("--only", default="")
    a = ap.parse_args()
    only = {x.strip() for x in a.only.split(",") if x.strip()}
    made = skipped = 0
    for name, text in texts().items():
        if only and name not in only:
            continue
        path = os.path.join(OUT, name + ".mp3")
        exists = os.path.exists(path)
        if exists and not a.force:
            print("건너뜀(있음)", name, "—", text)
            skipped += 1
            continue
        print("만듦" if a.write else "만들 것", name, "—", text)
        if a.write:
            os.makedirs(OUT, exist_ok=True)
            asyncio.run(synth(text, path))
            made += 1
    print("끝 — 만든 것 %d · 건너뛴 것 %d%s" % (made, skipped, "" if a.write else " (dry-run: --write를 붙여야 만든다)"))


if __name__ == "__main__":
    sys.exit(main())
