# GuideChuna — 작업 규칙

Meta Quest용 추나 술기 VR 훈련 앱. Unity **6000.2.1f1**, Built-in 렌더 파이프라인.
아래는 전부 **실측으로 확인된 것만** 적는다. 추정은 적지 않는다.

## 프로젝트 구조 (2026-08-24 실측)

- **Assembly Definition(.asmdef)은 0개다.** 전 스크립트가 `Assembly-CSharp` 하나에 들어간다. 분리 구조를 가정하지 말 것.
- 패키지 매니저는 **없다**(`package.json` 없음). npm/yarn 명령은 존재하지 않는다.
- 자동화 테스트는 **없다**. `com.unity.test-framework`는 설치돼 있지만 테스트 코드가 없어 `-runTests`는 0개를 돌린다.
- 실습 시나리오가 도는 씬은 `Assets/Scenes/TrainingScene.unity`. 로비는 `lobby.unity`.

## 컴파일 확인

```bash
dotnet build Assembly-CSharp.csproj -v q --nologo      # 약 23초, 기존 경고 15건
```

- ★ csproj는 **Unity가 생성한다.** 파일을 새로 추가/삭제했으면 Unity에 포커스를 줘 재생성한 뒤에 빌드해야 반영된다.
- 경고 15건은 기존 상태다. **오류 0개**만 확인하면 된다.
- Unity 에디터가 열려 있으면 `-batchmode` 실행은 프로젝트 락 때문에 실패한다.

## 절대 규칙 (전부 사고 이력에서 나왔다)

1. **씬·프리팹 파일을 텍스트로 수정하지 않는다.** 한글이 `\uXXXX`로 이스케이프돼 있어 `sed` 치환이 대문자 변환에 먹혀 글자가 깨진다. 씬 수정은 Unity 에디터에서만 한다. 조회는 `unity-scene-audit` Skill.
2. **환자 피부가 분홍색이 되면 저장하지 않는다.** xray 임시 머티리얼이 굳는 현상이다. 저장하지 말고 씬을 리로드한다. 저장하면 디스크가 손상된다(07-27 전례).
3. **파괴적 에디터 도구를 재실행하기 전에 파괴성을 확인한다.** 특히 진단 파지점 도구 ①은 사용자 수작업 배치를 날린다 — 재실행 금지, 배선만 고친다.
4. **한글 경로를 다루는 git 명령엔 `git -c core.quotepath=off`를 붙인다.**
5. **원인을 단정하기 전에 실측한다.** 로그·씬 YAML·코드를 먼저 읽는다. 추정으로 고치면 없는 필드를 신설하게 된다(08-12 전례).
6. **사용자가 판정 방식을 직접 지정하면 그대로 구현한다.** 기존 파이프라인에 끼워 맞추지 않는다. 같은 지적을 두 번 받으면 접근이 틀린 것이다.

## 시나리오 시스템

CSV(`Assets/Resources/Scenarios/*.csv`) + `ScenarioConfig`(`Assets/Resources/ScenarioConfigs/*.asset`)로 돈다.

- **새 `conditionType`을 만들지 않는다.** 방향 판정은 이미 있고 부위 중립적이다.
- ★**모르는 `conditionType`은 에러가 아니라 조용히 파지 조건이 된다.** 오타를 못 잡는다.
- ★**`PassiveStretch` 행에 `voiceInstruction`이 있으면 접촉 게이트가 죽는다.** 지시(voice)와 동작(condition)을 다른 substep으로 쪼갠다.
- ★**손 녹화 파일이 없으면 판정이 조용히 죽고 직전 녹화가 남아 오판정한다.**
- ★**나레이션이 없으면 에러 없이 무음이다.** 상급·평가는 `stepName` 클립만 보고, 없으면 그 단계가 통째로 조용하다.
- 시나리오 인덱스는 중간 삽입으로 밀려 있다. **옛 기록의 idx는 틀리니 매번 실측한다.**
- 채점이 나오는 판정 경로는 `HandPose` 하나뿐이다. `PassiveStretch`·`cranial*`은 0점이다.

## 코드 컨벤션

- `[SerializeField] private`를 기본으로 한다. 다만 `ScenarioConfig`처럼 CSV/에디터에서 직접 읽는 데이터 홀더는 기존대로 `public` 필드를 쓴다 — 통일한다고 기존 직렬화를 갈아엎지 않는다.
- **enum에 값을 추가할 때는 반드시 끝에 붙인다.** 씬에 int로 직렬화돼 있어 중간에 끼우면 기존 배선이 조용히 다른 값이 된다.
- `Update()`에서 매 프레임 할당(new, LINQ, 문자열 결합)을 새로 만들지 않는다. VR이라 프레임 예산이 좁다. 기존 코드에 위반이 남아 있어도 지나가면서 고치지 말고, 건드리는 파일에서만 지킨다.

## Skills

| Skill | 언제 |
|---|---|
| `unity-scene-audit` | 씬 배선 확인, 오브젝트/컴포넌트/참조 조회 (읽기 전용) |
| `chuna-scenario-wire` | CSV·Config·나레이션·클립·손녹화 정합 점검 (읽기 전용) |
| `narration-gen` | CSV에서 나레이션 mp3 생성 (기본 dry-run) |

## 작업 방식

1. 코드를 고치면 `dotnet build`로 컴파일을 확인한다.
2. 시나리오 CSV·Config를 고치면 `chuna-scenario-wire`로 정합을 확인한다.
3. Play 검증은 사람이 한다. **"Play 미검증"인 채로 완료라고 말하지 않는다.**
