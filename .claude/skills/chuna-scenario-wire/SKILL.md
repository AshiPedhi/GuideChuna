---
name: chuna-scenario-wire
description: 추나 시나리오의 CSV·ScenarioConfig·나레이션·애니 클립·손 녹화 정합을 Play 없이 점검한다. 무음 나레이션, 없는 애니 클립, 오타 conditionType, PassiveStretch 게이트가 죽는 배선, phase 화이트리스트 불일치를 찾는다. 시나리오를 고쳤거나 새로 만든 뒤, 또는 "단계가 안 넘어간다 / 소리가 안 난다 / 판정이 안 된다"를 추적할 때 사용한다.
---

# 시나리오 배선 점검

**읽기 전용이다.** 아무 파일도 고치지 않는다.

```bash
PYTHONIOENCODING=utf-8 "C:/Users/USER/AppData/Local/Python/pythoncore-3.14-64/python.exe" \
  .claude/skills/chuna-scenario-wire/check.py [시나리오명|--all]
```

## 잡는 것

| # | 항목 | 왜 |
|---|---|---|
| 1 | 초급·중급 나레이션 누락 | 없으면 **에러 없이 무음**이다. 플레이해서 귀로 듣기 전엔 안 잡힌다 |
| 2 | 상급·평가 `stepName` 클립 누락 | 상급·평가는 가이드 스텝이 아니면 CSV voice를 무시하고 `stepName` 클립만 본다. 없으면 무음 |
| 3 | **`PassiveStretch` + `voiceInstruction` 동시** | ★나레이션이 접촉 게이트를 죽인다. 지시(voice)와 동작(condition)을 다른 substep으로 쪼개야 한다 |
| 4 | 모르는 `conditionType` | ★오타는 에러가 아니라 **조용히 파지 조건**이 된다. 코드의 `switch`에서 실제 목록을 읽어 대조한다 |
| 5 | 없는 `patientAnimationClip` | 클립명이 틀리면 동작이 재생되지 않고 단계가 멈춘다 |
| 6 | 없는 `handTrackingFileName` | ★파일이 없으면 판정이 조용히 죽고 **직전 녹화가 남아 오판정**한다 (08-03) |
| 7 | phase 화이트리스트 불일치 | `educationPhases`·`measurementPhases`·`evaluationPhases`에 CSV에 없는 이름이 있으면 필터 결과가 0개가 된다 |
| 8 | CSV ↔ Config 짝 | 한쪽만 있는 시나리오 |

## 읽는 법

- `[없음]` = 확실한 결함. 고쳐야 한다.
- `[주의]` = 의도적일 수 있다. 예를 들어 실측 단계는 초급 고정이라 상급 클립이 없어도 정상이다.

## 함정

- 콘솔 cp949 → `PYTHONIOENCODING=utf-8` 필수.
- bash `python`은 스토어 스텁 → 풀경로.
- 나레이션 폴더는 `ScenarioConfig.narrationSubFolder`, 비면 `scenarioName`. YAML에서 값을 읽을 때 `\s*`를 쓰면 **개행까지 먹어 다음 줄을 캡처한다** — `[ \t]*`를 써야 한다.
