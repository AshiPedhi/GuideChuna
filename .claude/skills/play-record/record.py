"""메타 링크 테스트 플레이를 PC 화면에서 영상으로 남긴다(2026-09-21 신설).

★이 도구는 <b>Unity 프로젝트 밖</b>이다. 앱에 코드를 넣지 않는다(사용자 지시 09-21).
  쓰임새는 OneNote 업무일지에 붙일 <b>영상</b> 기록이다 — 스크린샷이 아니다.

돌아가는 얼개
  · Play 감지는 에이전트 브리지에 물어본다(`Temp/chuna-bridge/resp.json`의 `"playing"`).
    브리지가 안 떠 있으면(Unity가 닫혔거나 스크립트 컴파일 중) 감지가 안 된다 — `status`가 말해 준다.
  · 화면은 ffmpeg `gdigrab`으로 <b>Unity 창</b>을 긁는다. 창 제목은 씬 이름이 들어가 매번 바뀌므로
    녹화를 시작할 때 실제 창 제목을 읽어 쓴다.
  · ★gdigrab은 <b>화면에 보이는 것</b>을 긁는다. Unity 창이 다른 창에 가리면 가린 창이 찍힌다.

★함정(09-21에 실제로 밟았다)
  · 창 너비·높이가 홀수면 libx264가 통째로 실패한다("Generic error in an external library",
    결과 0바이트). `scale=trunc(iw/2)*2:trunc(ih/2)*2`를 반드시 건다.
  · 구간을 뽑을 때 `-ss`를 <b>입력 앞</b>에 두면 프레임이 밀린다. 입력 뒤에 두고 다시 인코딩한다.
  · mp4는 끝맺음(moov)을 써야 열린다. 그래서 ffmpeg를 죽이지 않고 stdin에 'q'를 보내 끝낸다.
    → 시작·감시·종료를 <b>한 프로세스</b>(watch)가 다 한다. 밖에서 멈출 때는 신호 파일을 쓴다.
"""
import argparse
import json
import os
import subprocess
import sys
import time

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
BRIDGE_RESP = os.path.join(ROOT, "Temp", "chuna-bridge", "resp.json")
BRIDGE_PY = os.path.join(ROOT, ".claude", "skills", "unity-live", "bridge.py")
STATE_DIR = os.path.join(ROOT, "Temp", "play-record")
STATE = os.path.join(STATE_DIR, "state.json")
STOP_FLAG = os.path.join(STATE_DIR, "stop")

DEFAULT_OUT = r"D:\추나녹화"
FFMPEG = r"C:\Users\USER\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-8.1.2-full_build\bin\ffmpeg.exe"


def log(msg):
    print(msg, flush=True)


# ── Unity 창·Play 상태 ────────────────────────────────────────────────
def unity_window_title():
    """지금 Unity 에디터 창의 제목. 없으면 None."""
    ps = ("Get-Process Unity -ErrorAction SilentlyContinue | "
          "Where-Object { $_.MainWindowTitle -ne '' } | "
          "Select-Object -First 1 -ExpandProperty MainWindowTitle")
    try:
        # ★encoding을 안 주면 파이썬이 콘솔 기본(cp949)으로 풀려다 한글에서 터진다(09-21에 밟음)
        out = subprocess.run(["powershell", "-NoProfile", "-NonInteractive", "-Command", ps],
                             capture_output=True, text=True, timeout=20,
                             encoding="utf-8", errors="replace")
        t = (out.stdout or "").strip()
        return t or None
    except Exception:
        return None


def is_playing(timeout=20):
    """브리지에 물어 Play 중인지 본다. (재생중, 브리지응답여부)"""
    env = dict(os.environ, PYTHONIOENCODING="utf-8")
    try:
        subprocess.run([sys.executable, BRIDGE_PY, "ping", "--timeout=%d" % timeout],
                       capture_output=True, text=True, timeout=timeout + 10, env=env,
                       encoding="utf-8", errors="replace")
    except Exception:
        return False, False
    try:
        with open(BRIDGE_RESP, "r", encoding="utf-8") as f:
            data = json.load(f)
        return bool(data.get("playing")), True
    except Exception:
        return False, False


# ── 상태 파일 ─────────────────────────────────────────────────────────
def save_state(d):
    os.makedirs(STATE_DIR, exist_ok=True)
    with open(STATE, "w", encoding="utf-8") as f:
        json.dump(d, f, ensure_ascii=False, indent=1)


def load_state():
    try:
        with open(STATE, "r", encoding="utf-8") as f:
            return json.load(f)
    except Exception:
        return {}


# ── 녹화 ──────────────────────────────────────────────────────────────
def out_path(out_dir, label):
    day = time.strftime("%Y%m%d")
    name = time.strftime("%H%M%S") + ("_" + label if label else "") + ".mp4"
    d = os.path.join(out_dir, day)
    os.makedirs(d, exist_ok=True)
    return os.path.join(d, name)


def start_ffmpeg(title, path, fps, crop):
    vf = "scale=trunc(iw/2)*2:trunc(ih/2)*2"   # ★홀수 해상도면 libx264가 통째로 죽는다
    if crop:
        vf = "crop=%s,%s" % (crop, vf)
    cmd = [FFMPEG, "-y", "-hide_banner", "-loglevel", "warning",
           "-f", "gdigrab", "-framerate", str(fps), "-i", "title=%s" % title,
           "-vf", vf, "-c:v", "libx264", "-preset", "veryfast", "-crf", "23",
           "-pix_fmt", "yuv420p", path]
    return subprocess.Popen(cmd, stdin=subprocess.PIPE, stdout=subprocess.DEVNULL,
                            stderr=subprocess.PIPE)


def stop_ffmpeg(proc):
    """★죽이지 않는다 — 'q'를 보내 스스로 끝맺게 한다(그래야 mp4가 열린다)."""
    try:
        proc.stdin.write(b"q")
        proc.stdin.flush()
    except Exception:
        pass
    try:
        proc.wait(timeout=20)
    except Exception:
        proc.terminate()
        try:
            proc.wait(timeout=10)
        except Exception:
            proc.kill()


def cmd_watch(a):
    """Play가 시작되기를 기다렸다가 녹화하고, Play가 끝나면 끝맺는다."""
    if os.path.exists(STOP_FLAG):
        os.remove(STOP_FLAG)
    deadline = time.time() + a.timeout
    log("[녹화] Play를 기다린다 — 최대 %d분 · %.1f초마다 확인" % (a.timeout // 60, a.poll))

    proc = None
    path = None
    started = 0.0
    warned_bridge = False

    try:
        while True:
            if os.path.exists(STOP_FLAG):
                log("[녹화] 멈추라는 신호를 받았다")
                break
            if time.time() > deadline:
                log("[녹화] 기다리는 시간이 다 됐다 — 끝낸다")
                break

            playing, alive = is_playing()
            if not alive and not warned_bridge:
                warned_bridge = True
                log("[녹화] ★브리지가 응답하지 않는다 — Unity가 닫혔거나 컴파일 중이다. 계속 기다린다")
            if alive:
                warned_bridge = False

            if playing and proc is None:
                title = unity_window_title()
                if not title:
                    log("[녹화] ★Unity 창을 못 찾았다 — 녹화를 못 시작한다")
                    break
                path = out_path(a.out, a.label)
                proc = start_ffmpeg(title, path, a.fps, a.crop)
                started = time.time()
                save_state({"path": path, "title": title, "started": started})
                log("[녹화] 시작 — %s" % path)
                log("        창 '%s'" % title)
            elif not playing and proc is not None:
                stop_ffmpeg(proc)
                secs = time.time() - started
                log("[녹화] Play가 끝났다 — %.0f초 기록" % secs)
                proc = None
                if not a.repeat:
                    break

            time.sleep(a.poll)
    finally:
        if proc is not None:
            stop_ffmpeg(proc)

    if path and os.path.exists(path):
        mb = os.path.getsize(path) / 1048576.0
        log("[녹화] 파일 — %s (%.1f MB)" % (path, mb))
        log("[녹화] ★이 영상을 남길지 지울지 사용자에게 물어야 한다")
    elif path:
        log("[녹화] ★파일이 없다 — %s" % path)
    return 0


def cmd_stop(a):
    os.makedirs(STATE_DIR, exist_ok=True)
    with open(STOP_FLAG, "w", encoding="utf-8") as f:
        f.write(time.strftime("%Y-%m-%d %H:%M:%S"))
    log("[녹화] 멈춤 신호를 남겼다 — 돌고 있는 watch가 다음 확인 때 끝맺는다")
    return 0


def cmd_status(a):
    playing, alive = is_playing()
    title = unity_window_title()
    st = load_state()
    log("브리지 %s · Play %s" % ("응답 O" if alive else "★응답 없음", "중" if playing else "아니오"))
    log("Unity 창 %s" % (title or "★없음"))
    if st.get("path"):
        p = st["path"]
        ok = os.path.exists(p)
        log("마지막 녹화 %s%s" % (p, "" if ok else " ★파일 없음"))
    log("ffmpeg %s" % ("O" if os.path.exists(FFMPEG) else "★없음"))
    return 0


def cmd_list(a):
    if not os.path.isdir(a.out):
        log("녹화 폴더가 없다 — %s" % a.out)
        return 0
    rows = []
    for day in sorted(os.listdir(a.out)):
        d = os.path.join(a.out, day)
        if not os.path.isdir(d):
            continue
        for f in sorted(os.listdir(d)):
            if f.lower().endswith((".mp4", ".mkv")):
                p = os.path.join(d, f)
                rows.append((p, os.path.getsize(p) / 1048576.0))
    if not rows:
        log("녹화가 없다 — %s" % a.out)
    for p, mb in rows[-a.limit:]:
        log("%8.1f MB  %s" % (mb, p))
    return 0


def cmd_clip(a):
    """구간을 <b>영상</b>으로 뽑는다(스샷이 아니다). ★-ss는 입력 뒤에 둔다 — 앞에 두면 프레임이 밀린다."""
    src = a.src or load_state().get("path")
    if not src or not os.path.exists(src):
        log("★원본이 없다 — %s" % src)
        return 1
    base = os.path.splitext(os.path.basename(src))[0]
    name = "%s_%s-%s%s.mp4" % (base, a.frm.replace(":", ""), a.to.replace(":", ""),
                               "_" + a.label if a.label else "")
    dst = os.path.join(os.path.dirname(src), name)
    cmd = [FFMPEG, "-y", "-hide_banner", "-loglevel", "warning", "-i", src,
           "-ss", a.frm, "-to", a.to,
           "-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-pix_fmt", "yuv420p"]
    if a.scale:
        cmd += ["-vf", "scale=%s:-2" % a.scale]
    cmd.append(dst)
    r = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if r.returncode != 0 or not os.path.exists(dst):
        log("★잘라내기 실패 — %s" % (r.stderr or "")[-400:])
        return 1
    log("[클립] %s (%.1f MB)" % (dst, os.path.getsize(dst) / 1048576.0))
    return 0


def cmd_rm(a):
    src = a.src or load_state().get("path")
    if not src or not os.path.exists(src):
        log("★지울 것이 없다 — %s" % src)
        return 1
    os.remove(src)
    log("[녹화] 지웠다 — %s" % src)
    return 0


def main():
    p = argparse.ArgumentParser(description="테스트 플레이를 영상으로 남긴다")
    p.add_argument("cmd", choices=["watch", "stop", "status", "list", "clip", "rm"])
    p.add_argument("--out", default=DEFAULT_OUT, help="녹화를 둘 폴더(기본 %s)" % DEFAULT_OUT)
    p.add_argument("--label", default="", help="파일 이름에 붙일 말")
    p.add_argument("--fps", type=int, default=20)
    p.add_argument("--poll", type=float, default=1.5, help="Play 상태를 확인하는 간격(초)")
    p.add_argument("--timeout", type=int, default=3600, help="Play를 기다릴 최대 시간(초)")
    p.add_argument("--repeat", action="store_true", help="한 판만 찍고 끝내지 않고 계속 기다린다")
    p.add_argument("--crop", default="", help="ffmpeg crop 식(예: 1280:720:100:50)")
    p.add_argument("--src", default="", help="clip·rm이 쓸 원본(없으면 마지막 녹화)")
    p.add_argument("--from", dest="frm", default="0", help="clip 시작(초 또는 MM:SS)")
    p.add_argument("--to", default="10", help="clip 끝(초 또는 MM:SS)")
    p.add_argument("--scale", default="", help="clip 가로 크기(예: 1280)")
    p.add_argument("--limit", type=int, default=20)
    a = p.parse_args()

    if not os.path.exists(FFMPEG):
        log("★ffmpeg가 없다 — %s" % FFMPEG)
        return 1
    return {"watch": cmd_watch, "stop": cmd_stop, "status": cmd_status,
            "list": cmd_list, "clip": cmd_clip, "rm": cmd_rm}[a.cmd](a)


if __name__ == "__main__":
    sys.exit(main())
