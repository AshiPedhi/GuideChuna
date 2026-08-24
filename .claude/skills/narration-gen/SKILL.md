---
name: narration-gen
description: 추나 시나리오 CSV에서 나레이션 mp3를 생성한다. edge-tts로 초급·중급 두 벌을 만들고, 기존 파일은 건드리지 않는다. 시나리오 CSV를 고쳤거나 새 술기를 추가해 나레이션이 필요할 때, 또는 "나레이션 생성/재생성", "무음 단계", "voiceInstruction" 이야기가 나올 때 사용한다.
---

# 나레이션 생성

시나리오 CSV의 `voiceInstruction`(파일명) · `textInstruction`(읽을 문장)으로 mp3를 만든다.

## 실행

```bash
"C:/Users/USER/AppData/Local/Python/pythoncore-3.14-64/python.exe" \
  .claude/skills/narration-gen/generate.py 경추ROM측정
```

**기본은 dry-run이다.** 만들 파일·건너뛸 파일 목록만 출력한다.
실제로 쓰려면 `--write`를 붙인다.

| 옵션 | 뜻 |
|---|---|
| `--write` | 실제 생성 (없으면 dry-run) |
| `--force` | 기존 파일도 덮어씀 (기본은 skip) |
| `--levels Beginner` | 특정 난이도만 (기본 `Beginner,Intermediate`) |
| `--only 파지,굴곡` | 특정 클립만 |
| `--voice ko-KR-SunHiNeural` | 음성 변경 |

## 안전 규칙 (스크립트에 강제돼 있음)

- **삭제하지 않는다.** 덮어쓸 때도 같은 경로에 쓴다 — 지웠다 만들면 Unity가 새 guid를 발급해 임포트 설정이 초기화된다.
- `.meta`는 만들지 않는다. Unity가 에디터 포커스 시 자동 생성한다.
- 출력 경로가 `Assets/Resources/Narrations/` 밖이면 거부한다.
- 임시 파일에 받은 뒤 `os.replace`로 옮긴다. 중간에 끊겨도 반쪽 mp3가 안 남는다.
- 같은 `voiceInstruction`에 서로 다른 `textInstruction`이 붙어 있으면 **생성하지 않고 충돌을 보고**한다.

## 파일명 규칙 — 코드 실측 (2026-08-24)

`ScenarioConditionManager.LoadNarrationClipInternal` 기준.

| 난이도 | 경로 | 파일명 |
|---|---|---|
| 초급 | `Resources/Narrations/Beginner/{시나리오}/` | **`voiceInstruction`** |
| 중급 | `Resources/Narrations/Intermediate/{시나리오}/` | **`{phase}_{voiceInstruction}` 먼저**, 없으면 `voiceInstruction` |
| 상급·평가 | `Resources/Narrations/Advanced/`, `Evaluation/` | **`stepName`** (시나리오 폴더 없음, 평면) |

- `{시나리오}`는 `ScenarioConfig.narrationSubFolder`, 비면 `scenarioName`.
- 못 찾으면 `Narrations/{난이도}/{clipName}` → `Narrations/{clipName}` 순으로 폴백한다. (현재 둘 다 비어 있다)
- ★**중급 접두 파일이 평문 파일을 가린다.** 문구를 바꿔 평문 mp3를 새로 만들어도 옛 `{phase}_{voice}.mp3`가 남아 있으면 **옛 음성이 재생된다.** `chuna-scenario-wire`가 이 겹침을 보고한다.
- ★**상급·평가는 가이드 스텝이 아니면 CSV `voiceInstruction`을 통째로 무시**하고 `stepName` 클립만 본다. 없으면 **에러 없이 무음**이다. 한 step의 두 번째 substep부터는 `{phase}_{stepName}` 키로 중복 재생을 막는다 — 이건 dedup 키일 뿐 **파일명이 아니다.**
- 이 스크립트는 초급·중급만 만든다. 상급·평가 클립은 여러 시나리오가 공유하므로 `--levels Advanced` 로 명시할 때만 `stepName`으로 만든다.

## 생성 후

1. Unity로 포커스를 옮겨 임포트 → `.meta` 생성 확인
2. `git status`로 새 파일 확인 후 커밋
3. 상급·평가 커버리지는 스크립트가 같이 보고한다 (없는 `stepName` 목록)

## 함정

- bash의 `python`은 스토어 스텁이라 깨진다. **반드시 풀경로**를 쓴다.
- edge-tts는 네트워크를 쓴다. 오프라인이면 실패한다 (7.2.7 설치돼 있음).
- 한글 경로라 커밋할 때 `git -c core.quotepath=off`.
