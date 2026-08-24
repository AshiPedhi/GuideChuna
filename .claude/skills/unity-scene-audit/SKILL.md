---
name: unity-scene-audit
description: Unity 씬·프리팹 YAML을 Play 없이 조회한다. 오브젝트 트리, 컴포넌트의 직렬화된 필드값, fileID 참조 역추적, 월드 좌표를 뽑는다. "씬에 배선됐나", "이 필드에 뭐가 물려 있나", "그 오브젝트 어디 있나", "리그가 몇 개냐" 같은 질문이나 씬 관련 결함을 추적할 때 사용한다.
---

# 씬 정적 조회

**읽기 전용이다.** 씬·프리팹·에셋 파일에 절대 쓰지 않는다.
씬 YAML은 한글이 `\uXXXX`로 이스케이프돼 있어 `sed`로 고치면 대문자 변환에 먹혀 **글자가 깨진다.** 씬 수정은 Unity 에디터에서만 한다.

## 실행

```bash
PYTHONIOENCODING=utf-8 "C:/Users/USER/AppData/Local/Python/pythoncore-3.14-64/python.exe" \
  .claude/skills/unity-scene-audit/audit.py <씬경로> <명령> [인자]
```

| 명령 | 하는 일 |
|---|---|
| `roots` | 루트 오브젝트 목록 (활성 여부·좌표) |
| `find <정규식>` | 이름으로 검색 → fileID·경로·활성·월드Y |
| `tree <fileID> [깊이]` | 하위 트리 |
| `comp <fileID>` | 그 오브젝트의 컴포넌트 목록 + 스크립트 파일 경로 |
| `script <스크립트명\|guid>` | 그 스크립트가 붙은 인스턴스 전부 + 직렬화 필드값 |
| `ref <fileID>` | 그 fileID를 참조하는 컴포넌트 역추적 |

씬 기본값은 `Assets/Scenes/TrainingScene.unity` (실습 시나리오가 도는 씬).

## 예시

```bash
audit.py - script PracticeSettingsController      # 배선된 필드값 전부
audit.py - find '배경|Lights'                      # 이름으로 찾기
audit.py - ref 4378015526650632203                 # 이 오브젝트를 누가 참조하나
audit.py - tree 1033700871 3                       # 하위 3단계
```

## 알려진 함정 (스크립트에 반영돼 있음)

- **python `re`에서 `\u` 패턴을 못 쓴다.** 이스케이프 마커는 `chr(92)+"u"`로 만든다.
- 콘솔이 cp949라 한글이 깨진다 → `PYTHONIOENCODING=utf-8` 필수.
- bash의 `python`은 스토어 스텁이라 깨진다 → 풀경로.
- 프리팹 인스턴스 안의 컴포넌트는 `stripped`로 나오고 `m_GameObject: 0`이다. 값은 프리팹 자산이나 `m_Modifications`에 있다.
- 월드 좌표는 **회전을 무시한 근사값**이다. 부모 스케일만 반영한다. 깊은 본 체인에서는 믿지 말 것.
- 시나리오 인덱스는 PJ 삽입으로 밀려 있다. 옛 기록의 idx는 틀리니 **매번 실측**한다.
