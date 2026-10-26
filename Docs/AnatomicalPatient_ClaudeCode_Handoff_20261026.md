# 해부학 환자 리그 — Claude Code 전달 및 c9 연결 검토

작성: 2026-10-26. 코드 기준 `b5eccb3`, 해부학 동작 범위 교정 `1ff6933`.
둘 다 이 문서에서 Play 검증된 정상 기준으로 취급하지 않는다.
이번 작업은 저장된 씬과 코드의 조회 및 전달문 작성이다. 실제 환자 교체, 씬 저장, 코드 수정은 하지 않았다.
실행 중인 Claude Code 세션으로 메시지를 직접 전송한 것은 아니다.

## 결론과 사용자 목표

사용자는 c9의 외형을 유지하면서 어깨·팔·목의 분절과 피부 움직임을 개선하고,
최종적으로 시술자가 직접 잡고 움직이는 환자를 원한다. 정해진 시연 애니메이션은 확인용이다.
기존 교육 진행에 필요한 오브젝트가 c9 아래 많으므로 통째 삭제 후 prefab 교체를 하지 않는다.
**기존 c9의 기능 계층·참조를 보존하고 해부학 출력 리그를 연결하는 방식**으로 이행을 검토한다.
구조상 가능한 경로를 확인했으며, 실제 통합·Play·Quest 적합성은 아직 검증하지 않았다.

## 현재 구현과 읽을 파일

경로는 프로젝트 루트 `C:/UnityProjects/GuideChuna` 기준이다.

| 대상 | 경로 / API |
|---|---|
| 생성 결과 영수증 | `Assets/Generated/AnatomicalPatient/LatestBuild.json` |
| 보존된 실험 모델 | `Assets/Generated/AnatomicalPatient/Build_20261022_174000/AnatomicalPatient_c9.prefab` |
| 구현·한계 기록 | `Assets/Generated/AnatomicalPatient/README.txt` |
| 출력 리그 | `Assets/Scripts/ClaudeScripts/Patient/AnatomicalPatientRig.cs` |
| 13개 관절 시연의 공통 경로 | `Assets/Scripts/ClaudeScripts/Patient/AnatomicalMotionSequence.cs` |
| 생성과 스킨 재바인딩 | `Assets/Editor/AnatomicalPatientBuild.Generate.cs`, `AnatomicalSkinBuilder.cs`, `AnatomicalMeshBuilder.cs` |
| 독립 미리보기·영상 캡처 | `Assets/Editor/AnatomicalMotionStudyWindow.cs` |
| 출력 분절 검증 | `Assets/Editor/AnatomicalPatientBuild.MotionVerification.cs` |
| 영상 기준점 계측/공통 경로 투영 검사 | `Assets/Editor/AnatomicalPatientBuild.VideoRange.cs` |

- `AnatomicalPatientRig.Pose`: 양팔 `ArmPose`와 목 굴곡·측굴·회전.
- `Evaluate(Pose)`: 관절 입력으로 최종 출력 본과 피부를 계산한다. FK 시연이며 힘 기반 동역학이 아니다.
- `SetInputSource(InputSource.Animation)` 후 평가하면 저장된 source 본을 읽는다.
  Animation 경로는 source의 어깨·팔꿈치·손목 위치를 보존하므로 기존 IK의 자세 선택을 개선하지 않는다.
- `SetAutomaticEvaluation(false)` + 명시적 `Evaluate` 또는 자동 LateUpdate 중 한 가지 소유 경로를 선택한다.
- `Configure(...)`는 중립 자세를 측정하고 rest 데이터를 저장하는 생성/설정용이다. 움직이는 중에 매 프레임 호출하지 않는다.
- source/output은 서로 다른 본이다. 입력 본에 출력 자세를 되먹임하지 않는다.

어깨 거상은 0=아래/90=수평/180=위, 거상면은 0=앞/90=옆이다.
상완/전완 축 회전의 부호는 근위→원위 축 기준이며 좌우 임상 부호와 섞지 않는다.
손목 +굴곡은 손바닥 쪽, +편위는 엄지 쪽으로 실제 c9 손가락 기준을 사용한다.
손목 축은 전완 회전을 따라가고, 굴곡 후 이동한 축에서 편위를 적용한다.
견갑골/쇄골과 요골/척골은 별도 출력이며, 목은 7분절에 근사 분배한다.
어깨 리듬·관절 중심·근육 형상·피부 보정은 제작용 근사이며 임상 검증 모델이 아니다.

### 시연과 자유 조작을 혼동하지 않을 것

제2늑골은 공통 `Sample(rest, Motion.Rib2Study, seconds, left)` → `Evaluate(Pose)` 경로다.
원본 3인칭 60.25/62.25/63.25초의 크게 펴기→발 쪽 이동→깊게 굽혀 복귀를 반영했다.
처음의 4초 간격 판독은 큰 움직임을 놓쳤고, 작게만 움직여야 한다는 해석은 정정했다.
관절각은 2D 표지와 c9 분절 길이를 가정 카메라/깊이로 맞춘 한 가지 3D 제작안이다.
영상에서 실제 3D 각도나 힘을 복원한 것이 아니다. 8초씩 3회 반복은 느린 확인용 시간이다.

영상은 8fps, 편집기 미리보기는 최대 12fps다. 이것을 VR 실시간 성능으로 해석하지 않는다.
일반 손 목표 탐색 실험 `AnatomicalArmTargetStudySolver`는 미완성이다.
10-23 기록에 최대 약 492mm 손 위치 잔차와 프레임별 큰 회전 변화가 남았다.
시연이 정상이라는 사실로 임의 손 위치·회전의 자유 조작 솔버가 완성됐다고 판단하지 않는다.

## 저장된 TrainingScene에서 확인한 결합

대상: `Assets/Scenes/TrainingScene.unity`의 현재 워킹 트리 파일.
저장 전 Editor 상태와 런타임 생성물은 이번에 조회하지 않았다.
정적 상세: `Temp/model-audit/c9-dependencies.json`의 `refined` 항목.

| 항목 | 확인값 |
|---|---|
| c9 루트 GO / Transform | `919132148920932801` / `543450652859335547` |
| Animator | `5866666021072361729` |
| 루트 부착 코드 | `CervicalRomDriver`, `CervicalGripJudge`, `CervicalRomPlaneGauge` |
| 명시적으로 저장된 하위 GO | 2,675개 |
| 직접 소유 MonoBehaviour / Collider / SMR | 229 / 184 / 883개 |
| 주요 분기 | `c8` 578개, `근육골격` 1,024개, `정보패널용` 1,024개 |
| 식별 가능한 외부 컴포넌트 → c9 내부 참조 | 10개 컴포넌트, 63개 참조 항목 |

숫자는 중첩 prefab 내용을 전부 확장한 실제 런타임 총수가 아니다.
외부 참조 수에서 부모 Transform, 중첩 UI prefab 3개, 내부 stripped 참조,
저장 파일에 소유 GO가 없는 SkullBoneColorizer 블록을 제외했다.

보존할 중요한 연결:

- `PatientPositionManager`의 환자 루트와 부모 이동 홀더.
- `ScenarioManager`의 Animator·각도 표시·양팔 회전축.
- `PracticeHandRecorder`와 `ChunaPathEvaluator`의 `HandGuideAxis` Transform `359949757`.
- 머리/어깨/가슴/팔/무릎 Collider와 머리 기준점 `3010130398466078172`.
- 기존 본 아래 술기 컨트롤러 7개, `GripPointTarget` 44개, 머리 접촉점 4개.
- `SkeletonFocusController`, `CranialHeadXray`, `AnatomyMuscleController`, `PracticeSettingsController`의 표시 참조.

## 단순 prefab 대체로 해결되지 않는 것

1. **씬 기능이 새 prefab에 없다.** 생성기는 씬 c9가 아닌 원본 FBX를 복제한다.
   입력 Animator controller는 비어 있고 Manual/LateUpdate·layer 30·실험실 UI가 기본이다.
   이를 그대로 실습 환자 자리에 놓으면 교육·파지·접촉 배선을 대신하지 못한다.
2. **반사 좌표계.** c8 Transform `4609038970809143896`은 scale `(-1,1,1)`이다.
   c9 루트는 scale 1.1 및 X축 약 -177도 회전이다.
   `Rig.TryRead`는 rigSpace 대비 음수 determinant 또는 shear를 거부한다.
   공통 rigSpace나 중간 기준틀을 실측하여 좌우/손바닥/팔꿈치/목 축을 검증해야 한다.
   원본·출력 사이 localRotation 복사나 임의 축 부호 반전으로 맞히지 않는다.
3. **가시성 관리자.** `PracticeSettingsController.CacheRenderers`는 비활성 자식도 수집한다.
   기존 Renderer를 한 번 끄기만 하면 설정 전환 때 다시 켜질 수 있다.
4. **투시 대상 구분.** `CranialHeadXray`는 Patient 아래 Renderer를 캐시하고
   `skeletal_system` 조상명을 보고 뼈를 제외한다. 새 `AnatomyMeshes/Skeleton`은 자동 호환되지 않는다.
   피부/골격/근육/의복을 명시 분류하고 캐시 등록 시점과 상태 복원을 연결한다.
5. **근육·골격 표시 경로.** `AnatomyMuscleController`는 상대 경로 `Find()`로 두 모델을 매칭하고,
   `SkeletonFocusController`는 `showBones` 직접 참조와 `skeletal_system` 범위를 사용한다.
   기존 가지를 지우거나 이름만 복제하면 표시 매핑을 보존할 수 없다.
6. **파지·판정과 최종 피부의 위치 일치.** 손/머리/근육/골격 앵커는 실제 출력 자세를 봐야 한다.
   목 재분배나 견갑골 운동 뒤 입력 본 위치가 최종 표면 위치와 같은지 실측한다.
   입력과 다른 곳에 보이는 접촉점으로 기존 판정을 계속 돌리지 않는다.

근거 위치(작성 시 줄 번호):

- `AnatomicalPatientBuild.Generate.cs:38,75`: FBX 복제와 실험용 기본 설정.
- `AnatomicalPatientRig.cs:197,327,419,797,829`: 설정·Animation 입력·source 위치·feedback 방지·좌표계 검증.
- `AnatomicalSkinBuilder.cs:168`: private mesh, bindposes, bones, rootBone 재바인딩.
- `PatientArmIkProbe.cs:23,181,685,1054`: 반사 좌표계·능동/수동 범위·놓기·초과각 함수.
- `PracticeSettingsController.cs:165,179,724`: 렌더러 캐시와 가시성.
- `CranialHeadXray.cs:479,497,553`: 투시 대상 수집과 뼈 제외.
- `AnatomyMuscleController.cs:114,158`, `SkeletonFocusController.cs:478`: 표시 경로.
- `CervicalRomDriver.cs:769`, `ChunaPathEvaluator.cs:3374`: 기존 머리 본 소비자.

위 파일 중 폴더가 생략된 것은 `Assets/Scripts/ClaudeScripts/` 아래 기존 파일이다.

## 권장 이행 순서 — 아직 미구현

1. 현재 미커밋 씬/설정/라이트맵을 보존한다. 기존 씬을 저장하지 말고 Unity Editor에서 별도 시험 복제본을 만든다.
   기존 작업자가 동시에 같은 파일/브리지 큐를 수정하지 않게 역할을 나눈다.
2. 기존 c9 루트·Animator·입력 본·기능 노드의 식별자와 외부 참조를 유지한다.
   새 출력 본/피부를 붙이고 source 본을 명시적으로 매핑한다. 중복 Animator는 추가하지 않는다.
   기존 Renderer 컴포넌트의 식별자를 유지하는 private mesh/bones 재바인딩도 우선 검토한다.
3. 원본과 출력의 기준 자세·미러·축·월드 치수를 계측한다. 기존 generation을 활성 씬에 재실행하지 않는다.
   입력 Animation 모드의 외형 연결부터 시험한다. 현재 실행 순서는 손 목표 9000 → 팔 IK 10000 → 출력 20000이다.
4. 피부/골격/근육 표시와 투시 정책을 연결하고, 파지/접촉/가이드 앵커의 최종 출력 매핑을 만든다.
   원본 c9 전체 또는 c8 전체를 비활성화하여 기능까지 끄지 않는다.
5. 자유 조작 단계에서는 손 목표와 저항으로 해부학 `Pose`를 결정하는 솔버를 붙인다.
   새 모드에서는 해당 관절을 기존 IK와 새 솔버가 동시에 제어하지 않게 한다.
   판정 소비자와 앵커는 명시적인 최종 출력 제공자를 보게 하고, source 본으로 출력을 되먹임하지 않는다.
6. 기본값 변경 대신 실제 직렬화 값을 확인한다. 신규 inspector 필드가 필요하면 위치와 설정값을 알린다.
   되돌림은 이번 연결에서 변경한 것만 복원한다.

## 장력 구현 상태와 후속 설계

새 해부학 리그의 근육은 형상 표현이며 힘/장력 계산이 없다.
최신 기존 c9 IK `b5eccb3`에는 능동/수동 간격과 지수 접근이 있지만,
실제 외력 대신 목표 초과각을 사용한다. 코드상 gap 기본값은 0이며 실제 실행 값은 별도 확인 대상이다.
놓기는 IK weight 감쇠로 쉬는 애니메이션 자세에 복귀하는 방식이다.
이들을 근육 장력이나 중력 적분이 완료된 상태로 설명하지 않는다.

후속 방향: 근육 경로 길이/모멘트암 → 수동 탄성·속도 의존 저항 → 중력·관성 → 필요 시 활성도.
손 추적은 실제 N 단위 힘을 측정하지 않으므로 목표-모델 사이 변위로 만든 가상 힘은 추정값이다.
어깨·팔꿈치를 지나는 근육의 결합을 표현하려면 각 관절 끝에 독립 스프링만 두는 것보다
같은 근육의 길이를 여러 관절각에서 함께 구하는 구조가 필요하다.
참고: [OpenSim Millard 근육 모델](https://opensimconfluence.atlassian.net/wiki/spaces/OpenSim/pages/53090555/Millard%2B2012%2BMuscle%2BModels).

## 검증과 완료 기준

10-23 독립 모델 기록: 관절 궤적 3,146시점 + 손목 조합 162시점, 반복 복귀,
팔꿈치 방향, 피부/골격 손바닥 정렬 및 Runtime/Editor 빌드 통과.
가정 카메라의 실제 출력 투영도 통과했지만 임상적 3D 복원 검증은 아니다.
이 기록은 현재 TrainingScene 교체/자유 파지 통합/장력/Quest 성능의 합격 근거가 아니다.

구현 시 AGENTS.md의 `.cs` 빌드·새 경고 확인·change-impact를 돌리고,
씬/프리팹은 텍스트 수정하지 않는다. 새 파일은 Unity 임포트/프로젝트 생성 뒤 빌드한다.
Play는 사람이 수행한다. 현재 문서 작성에서는 빌드/Play를 새로 실행하지 않았다.

사람이 확인할 순서:

1. 복제 시험 씬에서 원위치·환자 자세 전환·나레이션·교육 단계 진행 확인.
2. 기본/투시/근육/골격/설정 왕복에서 중복 피부·분홍 머티리얼·잘못 숨는 뼈 확인.
3. 양팔과 목에서 원위치, 느린 이동, 빠른 이동, 방향 반전, 놓기·다시 잡기 확인.
4. 보이는 손/머리/표면과 파지·접촉·각도 판정·녹화 기준의 위치 일치 확인.
5. 공용 표시/판정 컴포넌트를 바꾸면 제2늑골 외 다른 술기와 경추 ROM도 확인.
6. Quest 목표 성능을 기기에서 측정한 뒤 원래 실습 환자에 적용 판단.

### Claude Code에 보낼 짧은 요청문

> `Docs/AnatomicalPatient_ClaudeCode_Handoff_20261026.md`를 읽고 현재 c9 기능 계층과 참조를 보존하는 연결안을 검토해 줘. 독립 FK 시연, 기존 손 목표 IK, 아직 없는 장력을 구분해 줘. 실제 교체는 먼저 별도 시험 복제본에서 검증하고, 기존 c9·씬·수작업 파지점을 통째로 삭제하거나 재생성하지 마.
