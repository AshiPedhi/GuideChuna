---
name: play-record
description: 메타 링크로 하는 테스트 플레이를 PC 화면에서 영상으로 남긴다(OneNote 업무일지에 붙일 기록용). Play가 시작되면 자동으로 녹화를 걸고, Play가 끝나면 끝맺은 뒤 남길지 지울지 사용자에게 묻는다. 녹화한 영상에서 필요한 구간을 다시 영상으로 잘라낸다. "녹화해 줘", "플레이 기록 남겨", "이번 판 영상으로 떠 줘", "그 장면 잘라 줘"에 쓴다.
---

# 플레이 녹화

★**Unity 프로젝트 밖의 도구다.** 앱에 코드를 넣지 않는다(2026-09-21 사용자 지시).
★**남기는 것은 영상이다.** 스크린샷이 아니다 — 용도가 OneNote 업무일지 기록이다.

```bash
PY=/c/Users/USER/AppData/Local/Python/bin/python.exe
R=.claude/skills/play-record/record.py

PYTHONIOENCODING=utf-8 "$PY" $R status                      # 브리지·Play·창·ffmpeg 확인
PYTHONIOENCODING=utf-8 "$PY" $R crop                        # ★어디를 긁을지만 확인(녹화 안 함)
PYTHONIOENCODING=utf-8 "$PY" $R crop --png=/d/tmp/a.png    # 그 자리를 한 장 찍어 눈으로 본다
PYTHONIOENCODING=utf-8 "$PY" $R watch --label=실측랩         # Play를 기다렸다 녹화(★백그라운드로 띄운다)
PYTHONIOENCODING=utf-8 "$PY" $R watch --whole               # Game 뷰 대신 Unity 창 전체를 찍는다
PYTHONIOENCODING=utf-8 "$PY" $R stop                        # 돌고 있는 watch를 끝맺는다
PYTHONIOENCODING=utf-8 "$PY" $R list                        # 지금까지 녹화 목록
PYTHONIOENCODING=utf-8 "$PY" $R clip --from=1:20 --to=1:45 --label=나가기오눌림
PYTHONIOENCODING=utf-8 "$PY" $R rm                          # 마지막 녹화를 지운다
```

기본 저장 위치는 `D:\추나녹화\YYYYMMDD\HHMMSS_라벨.mp4`다(`--out`으로 바꾼다).

## 쓰는 순서

★★**녹화를 걸까요? 라고 묻지 않는다**(2026-09-21 사용자 지시). 사용자가 Play를 누르면 이미 걸려 있어야 한다.

★★★**2026-09-22부터 자동이다 — 에이전트가 띄우지 않는다.** `.claude/settings.json`의 **SessionStart 훅**이
`record.py ensure`를 부르고, ensure가 **상주 녹화(`daemon`)** 를 하나만 띄운다(심장박동 파일로 중복 방지).
daemon은 Play를 기다려 찍고, 끝나면 다시 기다리기를 **멈춤 신호가 올 때까지** 되풀이한다(대기 시간 제한 없음).
Claude 세션이 끝나도 살아 있다(떼어 낸 프로세스). 로그는 `Temp/play-record/daemon.log`.
- 틀렸던 것: 09-21~22엔 «에이전트가 세션마다 `watch`를 백그라운드로 띄운다»였다. 09-22에 하루 종일 안 띄워
  사용자가 "플레이하면 자동으로 녹화하게끔 하라고 했잖아"라고 했다. **기억에 기대는 자동은 자동이 아니다.**
- 확인은 `status`(첫 줄이 «상주 녹화 O»). 없으면 `ensure`. 끄려면 `stop`.
- ★daemon이 부르는 powershell·ffmpeg·python에는 전부 창 만들지 않기(`CREATE_NO_WINDOW`)를 건다 —
  콘솔 없는 프로세스가 부르면 1.5초마다 검은 창이 번쩍인다.

1. (옛 방식, 수동) `watch`를 `run_in_background`로 띄운다 — daemon이 도는 동안에는 **띄우지 않는다**(두 개가 같이 찍는다).
2. 사용자가 Play를 누르면 브리지가 알려 주고 녹화가 걸린다. 내가 Play를 눌러 줄 일은 없다.
3. Play를 멈추면 끝맺고 다음 Play를 다시 기다린다. 새 파일은 `list`로 본다.
4. ★★**10초 미만은 묻지 않고 지운다**(`--min-seconds`, 기본 10). 잘못 눌렀다 곧 끈 판이다.
5. ★**10초를 넘긴 판만 남길지 지울지 묻는다**(사용자 지시 09-21). 묻기 전에 몇 초·몇 MB인지 확인해
   같이 말한다. 지우라고 하면 `rm`.
6. 필요한 장면이 있으면 `clip`으로 **영상 구간**을 뽑는다. 로그의 시각과 맞춰 구간을 고른다.

## 어떻게 도는가

- **Play 감지**는 에이전트 브리지에 물어본다(`unity-live` 스킬과 같은 파일 큐, `"playing"` 필드).
  Unity가 닫혀 있거나 스크립트를 컴파일 중이면 응답이 없다 — `status`가 그것을 말해 준다.
- **화면**은 ffmpeg `gdigrab`으로 **Game 뷰가 있는 자리만** 긁는다.
  기본 동작이 Game 뷰다 — 사용자가 원하는 것은 Game 화면이지 Scene 뷰가 아니다(09-21 지적).
  1. 브리지 `gameview`가 Game 뷰를 앞으로 꺼내고 화면 좌표(`rect x,y,w,h`)를 알려 준다.
  2. `record.py`가 DPI 배율을 곱해 물리 픽셀로 바꾸고,
     `-i desktop -offset_x -offset_y -video_size`로 **바탕화면의 그 자리**를 긁는다.
- `--whole`을 주면 **Unity 창 전체**를 찍는다(Scene 뷰·인스펙터·콘솔까지). 배선을 보여 줄 때 쓴다.
- `--crop=w:h:x:y`는 그 위에 다시 거는 수동 잘라내기다(보통 쓸 일이 없다).
- ★**어디를 긁을지 의심스러우면 `crop`부터 본다.** `--png=`를 주면 그 자리를 한 장 찍어 준다 —
  **눈으로 보는 것**이 제일 빠르다(09-21에 이걸로 툴바 26px이 남은 걸 잡았다).
- ★**gdigrab은 화면에 보이는 것을 긁는다.** 그 자리를 다른 창이 덮으면 덮은 창이 찍힌다.
  녹화 중에는 Game 뷰를 덮지 말라고 사용자에게 알린다.

## 함정 (2026-09-21에 실제로 밟았다)

- ★★**창 제목으로 긁으면 Game 뷰가 아예 안 잡힐 수 있다.** 이 PC는 모니터가 둘이고
  (DISPLAY1 0~2559 · DISPLAY2 2560~5119) **Game 뷰가 두 번째 Unity 창에 들어 있다**(실측 x 3881).
  gdigrab `title=`은 메인 Unity 창의 **클라이언트 영역만** 긁는다(실측 `0,43 2560x1349` —
  ffmpeg가 스스로 `2560x1349x32 at (0,0)`이라 찍고, `GetClientRect`와 딱 같다.
  `GetWindowRect`는 `-8,-8~2568,1400`이라 **다르다**). 그래서 그 창을 아무리 잘라도
  Game 화면이 **영영 안 나온다**. → 창이 아니라 **바탕화면의 그 자리**를 긁는다.
- ★★**브리지가 빼 주는 툴바 21px로는 모자란다 — 실측 47px이다.** 21로 한 장 찍어
  줄마다 밝기를 재 보니 **0~25줄이 아직 툴바(회색)**였고 **26줄부터 순검정**(게임 그림)이었다.
  21 + 26 = 47. `record.py`가 `--toolbar=47`로 불러 바로잡는다(`GAME_TOOLBAR_PX`).
  아래·좌·우는 덜어낼 것이 없었다(끝줄·양끝칸이 전부 순검정).
- **DPI 배율**: Unity `EditorWindow.position`은 **논리 좌표**, gdigrab은 **물리 픽셀**이다.
  실측(09-21 이 PC): `GetDpiForWindow` 96 · `LOGPIXELSX` 96 · `HORZRES` 2560 = `DESKTOPHORZRES` 2560
  → **배율 1.0이라 보정이 0이었다.** 코드에 배율을 곱하는 길은 넣어 뒀지만
  **100%가 아닌 화면에서는 미검증이다**(이 PC로는 잴 수가 없다).
- ★**좌표를 읽는 PowerShell은 DPI 인지로 올려야 한다**(`SetThreadDpiAwarenessContext(-4)`).
  안 그러면 배율이 걸린 화면에서 가상화된(논리) 좌표가 돌아와 조용히 어긋난다. **미검증**.
- ★★**창 너비·높이가 홀수면 libx264가 통째로 실패한다.** 오류는 엉뚱하게 나오고
  (`Generic error in an external library`) **결과 파일이 0바이트**다. 도구가
  `scale=trunc(iw/2)*2:trunc(ih/2)*2`를 항상 걸어 막아 둔다 — 빼지 말 것.
- ★**mp4는 끝맺음(moov)을 써야 열린다.** ffmpeg를 죽이면 파일이 깨진다.
  그래서 stdin에 `q`를 보내 스스로 끝내게 한다 — 시작·감시·종료를 한 프로세스(watch)가 한다.
  밖에서 멈출 때는 `stop`이 신호 파일을 남기고, watch가 그것을 보고 끝맺는다.
- ★**구간을 뽑을 때 `-ss`를 입력 앞에 두면 프레임이 밀린다.** 입력 뒤에 두고 다시 인코딩한다
  (복잡추나 매뉴얼 캡처에서 이미 한 번 밟은 함정이다).
- 파이썬은 **풀경로**로 부른다(`C:\Users\USER\AppData\Local\Python\bin\python.exe`).
  `python`은 마이크로소프트 스토어 스텁이라 아무것도 안 한다.
- 콘솔이 cp949라 `PYTHONIOENCODING=utf-8`을 붙인다.
