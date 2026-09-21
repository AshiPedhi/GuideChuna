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
PYTHONIOENCODING=utf-8 "$PY" $R watch --label=실측랩         # Play를 기다렸다 녹화(★백그라운드로 띄운다)
PYTHONIOENCODING=utf-8 "$PY" $R stop                        # 돌고 있는 watch를 끝맺는다
PYTHONIOENCODING=utf-8 "$PY" $R list                        # 지금까지 녹화 목록
PYTHONIOENCODING=utf-8 "$PY" $R clip --from=1:20 --to=1:45 --label=나가기오눌림
PYTHONIOENCODING=utf-8 "$PY" $R rm                          # 마지막 녹화를 지운다
```

기본 저장 위치는 `D:\추나녹화\YYYYMMDD\HHMMSS_라벨.mp4`다(`--out`으로 바꾼다).

## 쓰는 순서

1. 사용자가 "이제 테스트해 볼게" / "링크로 들어간다"고 하면 **먼저** `watch`를 백그라운드로 띄운다.
   Bash 도구의 `run_in_background`를 쓴다 — 포그라운드로 띄우면 Play가 끝날 때까지 막힌다.
2. 사용자가 Play를 누르면 브리지가 알려 주고 녹화가 걸린다. 내가 Play를 눌러 줄 일은 없다.
3. Play를 멈추면 watch가 스스로 끝맺고 파일 경로를 찍는다.
4. ★**끝나면 남길지 지울지 사용자에게 묻는다**(사용자 지시 09-21). 묻기 전에 `list`로 크기를 확인해
   "몇 분 몇 MB짜리"인지 같이 말한다. 지우라고 하면 `rm`.
5. 필요한 장면이 있으면 `clip`으로 **영상 구간**을 뽑는다. 로그의 시각과 맞춰 구간을 고른다.

## 어떻게 도는가

- **Play 감지**는 에이전트 브리지에 물어본다(`unity-live` 스킬과 같은 파일 큐, `"playing"` 필드).
  Unity가 닫혀 있거나 스크립트를 컴파일 중이면 응답이 없다 — `status`가 그것을 말해 준다.
- **화면**은 ffmpeg `gdigrab`으로 **Unity 에디터 창**을 긁는다. 창 제목에 씬 이름이 들어가 매번
  바뀌므로 녹화를 시작할 때 실제 제목을 읽어 쓴다.
- ★**gdigrab은 화면에 보이는 것을 긁는다.** Unity 창이 다른 창에 가리면 가린 창이 찍힌다.
  녹화 중에는 Unity 창을 덮지 말라고 사용자에게 알린다.
- Game 뷰만 잘라 내고 싶으면 `--crop=w:h:x:y`를 준다(레이아웃이 바뀌면 좌표도 바뀐다).

## 함정 (2026-09-21에 실제로 밟았다)

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
