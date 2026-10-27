"""Package a completed private Unity capture, never touch a source scene/CSV."""
from pathlib import Path
import argparse
import json
import subprocess
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "Captures/Rib2HandsReview_V02_20261027"
FFMPEG = Path("C:/Users/USER/AppData/Local/Microsoft/WinGet/Packages/Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe/ffmpeg-8.1.2-full_build/bin/ffmpeg.exe")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("render_directory")
    args = parser.parse_args()
    render = Path(args.render_directory).resolve()
    if not render.is_relative_to(OUT):
        raise RuntimeError("Only this review's new render folder can be packaged")
    receipt = json.loads((render / "receipt.json").read_text(encoding="utf-8"))
    if not receipt.get("success"):
        raise RuntimeError("Unity capture did not pass")
    frames = sorted(render.glob("guide_*.jpg"))
    if len(frames) != 248:
        raise RuntimeError(f"Expected248 rendered frames, found{len(frames)}")
    for name in ("guide.mp4", "comparison.mp4", "keyposes.jpg", "review.html"):
        if (OUT / name).exists():
            raise FileExistsError(OUT / name)
    subprocess.run([str(FFMPEG), "-hide_banner", "-loglevel", "error", "-n", "-framerate", "30", "-i", str(render / "guide_%05d.jpg"), "-c:v", "libx264", "-crf", "20", "-pix_fmt", "yuv420p", "-movflags", "+faststart", str(OUT / "guide.mp4")], check=True)
    subprocess.run([str(FFMPEG), "-hide_banner", "-loglevel", "error", "-n", "-framerate", "30", "-i", str(OUT / "source_frames/source_%05d.jpg"), "-framerate", "30", "-i", str(render / "guide_%05d.jpg"), "-filter_complex", "[0:v]scale=960:540,pad=960:600:0:30:color=0x111827[a];[a][1:v]hstack=inputs=2[v]", "-map", "[v]", "-frames:v", "248", "-c:v", "libx264", "-crf", "20", "-pix_fmt", "yuv420p", "-movflags", "+faststart", str(OUT / "comparison.mp4")], check=True)
    font = ImageFont.truetype("C:/Windows/Fonts/malgun.ttf", 25)
    sheet = Image.new("RGB", (1440, 5 * 500), "#111827")
    draw = ImageDraw.Draw(sheet)
    for i, (index, label) in enumerate([(0, "시작 파지"), (112, "팔 펼침"), (172, "발 쪽 이동"), (202, "깊게 굽힘"), (247, "다시 펼침")]):
        y = i * 500
        draw.text((20, y + 12), f"{label} · 초안 {index / 30:.2f}s / 원본 {56.5 + index / 30:.2f}s", fill="white", font=font)
        for x, path in [(0, OUT / f"source_frames/source_{index:05d}.jpg"), (720, frames[index])]:
            image = Image.open(path).convert("RGB")
            image.thumbnail((710, 420))
            sheet.paste(image, (x + (720 - image.width) // 2, y + 60 + (420 - image.height) // 2))
    sheet.save(OUT / "keyposes.jpg", quality=90)
    html = '''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>제2늑골 양손 가이드 초안</title><style>body{margin:0;background:#111827;color:#e5e7eb;font:17px system-ui,sans-serif}main{max-width:1550px;margin:auto;padding:24px}video{width:100%;background:#070b12;border-radius:12px}button,a{display:inline-block;padding:11px 16px;margin:8px 8px 8px 0;border:0;border-radius:8px;background:#334155;color:white;cursor:pointer;text-decoration:none}p{line-height:1.7}.cyan{color:#35d3ea}.orange{color:#ffae48}small{color:#b4becf}img{width:100%;max-width:1100px}</style><main><h1>제2늑골 · 양손 가이드 초안</h1><p>왼쪽은 원본 영상, 오른쪽은 별도 메모리 복제본입니다. <span class="cyan">청록 = 시술자 왼손 · 환자 손/손목 유도</span> / <span class="orange">주황 = 시술자 오른손 · 흉곽 지지</span></p><video id="v" controls preload="metadata" playsinline src="comparison.mp4"></video><div><button onclick="v.pause();v.currentTime=0">시작 파지</button><button onclick="v.currentTime=3.75;v.play()">팔 한 주기</button><button onclick="v.paused?v.play():v.pause()">재생 / 정지</button><button onclick="v.playbackRate=.5">0.5배</button><button onclick="v.playbackRate=1">1배</button><a href="guide.mp4">초안만 보기</a><a href="keyposes.jpg">주요 자세 비교</a></div><p><small>56.50–64.75초의 안정된 시작 파지와 팔 한 주기입니다. 접근·해제까지의 전체 시술이 아닙니다. 영상/텍스트 기반 제작안이며 깊이·가려진 손가락·접촉 위치는 가정이 포함됩니다. 기존 손 녹화 데이터와 실습 시나리오에 연결하지 않았습니다. 실제 접촉·힘·임상 정확성·Quest Play는 미검증입니다.</small></p><p>Unity: GuideChuna → 시험 → 제2늑골 → 양손 영상 가이드 초안 (독립 검토). Play 없이 재생·정지·시점 변경이 가능합니다.</p><img loading="lazy" src="keyposes.jpg" alt="원본과 초안 주요 자세 비교"></main><script>const v=document.getElementById('v');</script></html>'''
    (OUT / "review.html").write_text(html, encoding="utf-8")
    (OUT / "package_receipt.json").write_text(json.dumps({"renderDirectory": str(render), "renderedFrames": len(frames), "fps": 30, "videoDurationSeconds": 248 / 30, "guideVideo": "guide.mp4", "comparisonVideo": "comparison.mp4", "sourceToDraftTimeOffsetSeconds": 56.5, "clinicalAccuracyClaim": False}, indent=2), encoding="utf-8")
    print(json.dumps({"success": True, "folder": str(OUT), "frames": len(frames)}))


if __name__ == "__main__":
    main()
