"""실측 기록(RomMarkerScene) 기준점 안내 음성을 만든다(2026-09-22 신설).

사용자 09-22: "기준점을 한번 잡으면 다음 기준점은 뭘 설정해야 할지 나레이션으로 안내해 줘.
자동으로 탭 넘기지 말고 내가 누를 수 있게."
→ RomRecordSession이 Resources/RomRecordUI/Voice/{이름}.mp3를 읽어 차례로 들려준다.

★목소리는 시나리오 나레이션과 같다(edge-tts ko-KR-SunHiNeural — narration-gen 기본값).
★기존 파일은 덮지 않는다(--force로만). 문구를 고쳤으면 --force로 다시 만든다.

  "C:/Users/USER/AppData/Local/Python/pythoncore-3.14-64/python.exe" .claude/tools/rom_record_voice.py [--force]
"""
import argparse
import asyncio
import os
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "Resources", "RomRecordUI", "Voice")
VOICE = "ko-KR-SunHiNeural"

# ★이름은 RomRecordSession의 VoiceNames와 짝이다 — 바꾸면 둘 다 바꾼다.
LINES = {
    "arm_c7":    "대추에 핀치하세요.",
    "arm_ear":   "외이도에 핀치하세요.",
    "arm_glab":  "중립 자세에서 미간에 핀치하세요.",
    "done_c7":   "대추를 설정했습니다.",
    "done_ear":  "외이도를 설정했습니다.",
    "done_glab": "미간을 설정했습니다.",
    "next_c7":   "다음은 대추입니다. 대추 버튼을 누르세요.",
    "next_ear":  "다음은 외이도입니다. 외이도 버튼을 누르세요.",
    "next_glab": "다음은 미간입니다. 미간 버튼을 누르세요.",
    "all_done":  "기준점 설정이 끝났습니다.",
    "locked":    "다시 찍으려면 버튼을 먼저 누르세요.",
}


async def make(name, text, force):
    import edge_tts
    path = os.path.join(OUT, name + ".mp3")
    if os.path.exists(path) and not force:
        print("건너뜀(있음) " + name)
        return
    tmp = path + ".tmp"
    await edge_tts.Communicate(text, VOICE).save(tmp)
    os.replace(tmp, path)
    print("만듦 %s — %s" % (name, text))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--force", action="store_true", help="있는 파일도 다시 만든다")
    a = ap.parse_args()
    os.makedirs(OUT, exist_ok=True)
    for name, text in LINES.items():
        asyncio.run(make(name, text, a.force))
    return 0


if __name__ == "__main__":
    sys.exit(main())
