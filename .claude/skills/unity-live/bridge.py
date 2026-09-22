"""열려 있는 Unity 에디터를 조회하고 인스펙터 값을 고친다.

받는 쪽은 `Assets/Editor/ChunaAgentBridge.cs`. 파일 큐로 주고받는다(포트를 안 쓴다).
2026-09-09 신설. Unity 공식 `com.unity.pipeline`이 이 프로젝트(.NET Standard)에서
컴파일 에러 117건을 내서, 필요한 조회·수정 기능만 직접 만들었다.

★기존 씬은 저장 안 한다. 값만 바꾸고 dirty 표시까지다 — 사람이 보고 Ctrl+S 한다(절대규칙 2).
  예외(2026-09-18 사용자 허가): scene-copy로 브리지가 새로 만든 씬만 저장·삭제·추가한다.
★모든 수정은 Undo 가 걸린다. 에디터에서 Ctrl+Z 로 되돌아간다.
"""
import io
import json
import os
import sys
import time

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
DIR = os.path.join(ROOT, "Temp", "chuna-bridge")
REQ = os.path.join(DIR, "req.txt")
RESP = os.path.join(DIR, "resp.json")
# ★요청·응답 파일이 하나뿐이라 두 호출이 겹치면 서로의 응답을 지우거나 요청을 덮어쓴다.
#   2026-09-22 상주 녹화(play-record daemon)가 1.5초마다 ping을 보내기 시작하자 내 refresh가 «응답 없음»이 됐다.
#   → 한 번에 한 요청만 오가게 잠근다. 죽은 프로세스가 남긴 잠금은 LOCK_STALE초 뒤 걷어 낸다.
LOCK = os.path.join(DIR, "lock")
LOCK_STALE = 200

USAGE = """쓰는 법 — 조회
  bridge.py ping
  bridge.py refresh                                            # 에셋 다시 읽기(밖에서 .cs·CSV를 고쳤을 때)
  bridge.py find  --comp=<컴포넌트이름>
  bridge.py hierarchy [--path=Root/Child] [--filter=이름조각] [--comp=이름] [--depth=2]
  bridge.py get   --path=Root/Child/Obj [--comp=이름] [--depth=1]
  bridge.py get   --comp=<컴포넌트이름>          # 그 컴포넌트 붙은 오브젝트 전부

쓰는 법 — 수정  (★저장은 안 한다. 사람이 Ctrl+S)
  bridge.py set --item="경로|컴포넌트|프로퍼티|값"
  bridge.py set --item="A|Foo|a|1" --item="B|Bar|b|true"      # 여러 개 한 번에
  bridge.py set --item="..." --dry=1                          # 뭐가 바뀔지만 본다

  값 형식
    bool     true / false / 1 / 0
    숫자      12  ·  0.35
    enum     이름 또는 인덱스
    Vector3  "0.1,1.2,0.3"
    Color    "1,0,0,1" 또는 "#FF0000"
    참조      씬 경로 "Root/Obj"  ·  "Root/Obj|컴포넌트"  ·  에셋 "Assets/…"  ·  none
    활성      --item="경로|GameObject|active|false"

쓰는 법 — 씬 편집 (2026-09-18. ★브리지가 scene-copy로 만든 씬에서만. 기존 씬은 저장·삭제 불가)
  bridge.py scene-list                                         # 열린 씬·저장 안 한 변경·소유 씬·Build Settings
  bridge.py scene-copy --src=Assets/Scenes/lobby.unity --dst=Assets/Scenes/새씬.unity   # 대상 있으면 거부
  bridge.py scene-open --path=Assets/Scenes/새씬.unity [--mode=additive]    # 저장 안 한 씬이 있으면 거부
  bridge.py delete   --path=Root/Obj
  bridge.py add-go   --name=이름 [--parent=Root/Obj] [--pos="x,y,z"]
  bridge.py add-comp --path=Root/Obj --type=컴포넌트이름
  bridge.py scene-save [--path=Assets/Scenes/새씬.unity]
  bridge.py build-add --path=Assets/Scenes/새씬.unity

쓰는 법 — 캡처·재생
  bridge.py capture [--view=scene|game] [--label=이름] [--frame=Root/Obj] [--w=1600 --h=900]
                                                               # → <프로젝트>/Captures/날짜/시각_이름.png
  bridge.py play  /  bridge.py stop

옵션
  --timeout=15    응답을 기다릴 초 (기본 15)
"""


def acquire_lock(timeout):
    deadline = time.time() + timeout
    while True:
        try:
            fd = os.open(LOCK, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
            os.write(fd, str(os.getpid()).encode())
            os.close(fd)
            return True
        except FileExistsError:
            try:
                if time.time() - os.path.getmtime(LOCK) > LOCK_STALE:
                    os.remove(LOCK)
                    continue
            except OSError:
                continue
            if time.time() > deadline:
                return False
            time.sleep(0.05)


def release_lock():
    try:
        os.remove(LOCK)
    except OSError:
        pass


def send(cmd, args, items, timeout):
    os.makedirs(DIR, exist_ok=True)
    if not acquire_lock(timeout):
        print("★다른 브리지 호출이 %d초 넘게 잠금을 쥐고 있다(%s)" % (timeout, LOCK))
        return None
    try:
        return _send(cmd, args, items, timeout)
    finally:
        release_lock()


def _send(cmd, args, items, timeout):
    rid = str(int(time.time() * 1000))
    body = ["id=" + rid, "cmd=" + cmd]
    for k, v in args.items():
        body.append(f"{k}={v}")
    # ★item 은 여러 줄이다 — 한 요청으로 여러 값을 고친다.
    for it in items:
        body.append("item=" + it)

    # 묵은 응답을 먼저 치운다. 안 그러면 직전 답을 이번 답으로 읽는다.
    if os.path.exists(RESP):
        try:
            os.remove(RESP)
        except OSError:
            pass

    io.open(REQ, "w", encoding="utf-8", newline="\n").write("\n".join(body) + "\n")

    deadline = time.time() + timeout
    while time.time() < deadline:
        if os.path.exists(RESP):
            # ★에디터가 .tmp 에 쓰고 File.Move 로 옮기므로 반쪽 파일은 안 보인다.
            #   그래도 옮기는 순간과 겹칠 수 있어 실패하면 한 번 더 본다.
            try:
                d = json.load(io.open(RESP, encoding="utf-8"))
            except Exception:
                time.sleep(0.03)
                continue
            if d.get("id") == rid:
                return d
        time.sleep(0.03)
    return None


def main():
    argv = sys.argv[1:]
    if not argv or argv[0].startswith("-"):
        print(USAGE)
        return 2

    cmd = argv[0]
    args = {}
    items = []
    timeout = 15.0
    for a in argv[1:]:
        if a.startswith("--timeout="):
            timeout = float(a.split("=", 1)[1])
        elif a.startswith("--item="):
            items.append(a.split("=", 1)[1])
        elif a.startswith("--") and "=" in a:
            k, v = a[2:].split("=", 1)
            args[k] = v

    # ★Unity가 백그라운드면 파일 감시가 멈춰 refresh가 바뀐 파일을 못 본다(2026-09-18 실측).
    #   git으로 바뀐 스크립트·데이터를 찾아 경로로 넘겨 강제 임포트하게 한다. 씬·프리팹은 넘기지 않는다(열린 씬을 건드리지 않게).
    if cmd == "refresh" and "paths" not in args:
        try:
            import subprocess
            out = subprocess.run(["git", "-c", "core.quotepath=off", "status", "--porcelain", "-uall", "--", "Assets"],
                                 cwd=ROOT, capture_output=True).stdout.decode("utf-8", "replace")
            keep = (".cs", ".csv", ".asset", ".json", ".txt", ".shader", ".mat")
            paths = []
            for line in out.splitlines():
                p = line[3:].strip().strip('"')
                if " -> " in p:
                    p = p.split(" -> ", 1)[1]
                if p.endswith(keep) and not p.startswith("Assets/Results/"):   # 실행 결과 CSV는 코드가 아니다
                    paths.append(p)
            if paths:
                args["paths"] = ";".join(paths)
                print(f"(바뀐 파일 {len(paths)}개를 강제 임포트로 넘긴다)")
        except Exception as e:
            print(f"(git으로 바뀐 파일을 못 찾았다 — 일반 refresh만 한다: {e})")

    if cmd == "set" and not items and "path" not in args:
        print("★set 에는 --item=\"경로|컴포넌트|프로퍼티|값\" 이 필요하다.\n")
        print(USAGE)
        return 2

    d = send(cmd, args, items, timeout)

    if d is None:
        print("★응답이 없다. 셋 중 하나다:")
        print("  ① Unity가 백그라운드라 에디터 루프가 안 돈다 → <b>Unity 창을 한 번 클릭</b>하고 다시.")
        print("  ② 브리지가 꺼져 있다 → 메뉴 `GuideChuna/에이전트 브리지/켜기`.")
        print("  ③ 컴파일 에러로 Unity가 세이프 모드다 → 콘솔을 본다.")
        print(f"  (요청 파일: {REQ})")
        return 1

    if not d.get("ok"):
        print("★에러: " + str(d.get("error")))
        return 1

    if d.get("playing"):
        print("※ 지금 Play 중이다. 보이는 값은 런타임 상태다 — 씬에 저장된 값과 다를 수 있다.\n")
    text = d.get("text") or ""
    print(text if text.strip() else "(빈 응답)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
