# 제2늑골 영상·텍스트 기반 별도 시연씬 점검

2026-10-26. 새 시연씬의 최신 저장본과 아래 7개 신규 스크립트를 읽기 전용으로 확인했다. 이 보고서 작성 중 Unity 명령, 씬 저장, 원본 데이터 수정은 하지 않았다.

**결과: 새 씬의 런타임 배선과 UI 콜백에 정적 누락이 없고, 기존 시나리오·통과 조건 데이터를 참조하지 않는다. 저장 후 재로드에 따른 팔 회전 차이도 수정 후 메시 비교에서 0m로 확인됐다.**

## 대상

- 씬: `Assets/Demos/Rib2VideoTextScene/V01/Rib2VideoTextDemo.unity`
- 런타임: `Rib2VideoTextScenePlayer.cs`, `Rib2DemoPatientArm.cs`, `Rib2DemoSceneControls.cs` (`Assets/Scripts/Demos/`)
- 에디터: `Rib2VideoTextDemoSceneBuild.cs`, `Rib2VideoTextDemoSceneTool.cs`, `Rib2VideoTextScenePlayerEditor.cs`, `Rib2DemoSceneControlsBuilder.cs` (`Assets/Editor/`)
- 정적 씬 조회: `.agents/skills/unity-scene-audit/audit.py`

## 씬 배선과 기존 데이터 분리

| 확인 항목 | 결과 |
|---|---|
| 새 시연 런타임 컴포넌트 | Player·PatientArm·Controls 각각 1개 |
| 세 컴포넌트의 로컬 오브젝트 참조 | 17개 모두 이 씬의 fileID로 해결됨 |
| 씬에 명시된 Script GUID | 14종 모두 Assets 또는 PackageCache의 스크립트로 해결됨 |
| 명시적 `m_Script: {fileID: 0}` | 0개 |
| UI persistent callback | 5개 모두 Controls 인스턴스 `1701711979`와 실제 public 메서드에 연결 |
| 콜백 메서드 | `StartPose`, `PlayCycle`, `TogglePlay`, `ToggleHalf`, `Seek` |
| 기존 Resources/Scenarios GUID 참조 | 0개 |
| 기존 Resources/ScenarioConfigs GUID 참조 | 0개 |
| 기존 Resources/HandPoseData GUID 참조 | 0개 |
| 기존 Resources/Animators GUID 참조 | 0개 |
| 동작 CSV | 새 `Assets/Demos/Rib2VideoTextScene/V01/Data/video_text_demo.csv`만 연결 |
| 새 CSV와 영상·텍스트 전용 원본 비교 | SHA256 동일 |

세 런타임 스크립트에는 기존 Scenario/Condition 시스템이나 Resources 동작 파일을 불러오는 호출이 없다. 손 표시는 기존 `HandTransformMapper`를 사용하지만, 원본 가이드 CSV와 기존 판정 경로를 실행하지 않는다. UI Builder가 참조하는 기존 Resources 에셋은 한글 폰트의 읽기 전용 대체 경로다.

새 씬의 Player 인스턴스는 `1701711982`, PatientArm은 `1701711980`이다. Player의 `motionCsv` GUID는 `21fabcd6a8d2b8342aacddc00d7b90a0`이며 새 Data 복사본에 해당한다. 팔꿈치 관찰점은 영상에서 작성된 9개 시각(0–8.25초)으로 저장되어 있다.

Editor Builder는 새 경로에만 씬을 생성하며 기존 출력 덮어쓰기를 거부한다. Tool의 시작 자세 저장은 씬 경로가 `Assets/Demos/Rib2VideoTextScene/`로 시작하는지 검사한다. 기존 TrainingScene을 저장하는 호출은 발견되지 않았다. 이 검사는 코드·배선 확인이며 원본 전체 파일의 보존 해시 검사는 부모 작업의 별도 보존 결과를 따른다.

## 저장 후 재로드 시 팔 회전 보존

최초 검토에서는 `Initialize()`가 현재 본의 localRotation을 비직렬화 rest 회전으로 다시 캡처하는 문제를 확인했다. 시작 자세를 저장하고 재로드하면 기준 회전이 원본 FBX 자세에서 시연 시작 자세로 바뀌어, 중간 프레임의 위팔 roll과 피부 변형이 달라질 수 있었다.

- 수정 전 부모 실측 보고: 5개 자세, BakeMesh 월드 좌표 정점 331,710개 비교에서 최대 약 0.0146m 차이.
- 수정: `referencePoseRecorded`와 세 rest quaternion을 숨김 직렬화 데이터로 보존. `CaptureReferencePose()`는 원본 모델 회전을 한 번만 기록하며, 이미 기록된 상태의 재기록을 거부한다. `Initialize()`는 기록이 없으면 중단하고 현재 자세를 새 기준으로 삼지 않는다.
- Builder는 첫 IK 평가 전에 `CaptureReferencePose(upper, lower, wrist)`를 호출한다.
- 최신 저장씬 확인: `referencePoseRecorded: 1` 및 아래 quaternion 3개가 실제로 저장됨.

```text
upperRest   = ( 0.118644126, -0.0032187789, -0.0066606794, 0.9929093)
forearmRest = (-0.0073476583,-0.000015195037,-0.00066879694,0.9999728)
handRest    = (-0.050943006, -0.0022799068, -0.00277088,   0.99869514)
```

수정 후 `Temp/rib2-scene-demo/reload_verification.txt` 확인 결과: 새 씬만 저장·다시 열어 같은 5개 자세의 정점 331,710개를 비교했고 **최대 차이 0m**였다. 이 결과는 Editor에서 동일 플레이어·솔버를 호출한 비교다.

## Editor 동작 표본 결과

`Temp/rib2-scene-demo/sweep_result.json`에서 확인했다.

| 항목 | 실측 결과 |
|---|---:|
| 표본 수 | 248 |
| 최대 손목 솔버 잔차 | 0.000001245m |
| 최대 팔뼈 길이 변화 | 0.000000775m |
| 원본 표시 궤적의 최대 도달거리 보정 | 0.030868m |
| 오른손 아래 방향 · 월드 아래 방향 점곱 | 1.0 |
| 동작 좌표틀 행렬식 | 1.000000238 |

최대 약 3.09cm의 도달거리 보정은 복제 환자의 팔 길이에 손 파지를 붙이기 위한 시연용 재배치다. 원본 CSV와 통과 조건을 바꾸는 보정이 아니다. 영상 깊이, 정확한 늑골 접촉점, 압력 및 임상 각도를 측정한 결과로 해석하면 안 된다.

## 검증 범위와 남은 확인

정적 참조·콜백·데이터 분리, 최신 rest 직렬화, Editor의 248개 동작 표본과 저장·재로드 후 메시 일치는 확인했다. 실제 Quest Play, 손 추적 입력, 사람 대상 정렬·접촉 정확도와 임상 정확도는 이 점검의 범위 밖이며 미검증이다. Missing Script 결과는 씬 YAML에 명시된 스크립트 참조에 대한 정적 확인이다.

점검 시점 식별값:

```text
scene SHA256:
8914a0c8f20316179904bcc16f170e01c362168773f5937394feb03bb920b804

video_text_demo.csv SHA256:
d2167e63529562f52688ca4865112ad21162b8f65d672b11094d0ee4324e7f23
```
