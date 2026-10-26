# 제2늑골 · Unity 시연 초안

2026-10-26. 기존 c9 환자 모델을 사용한 별도 Unity 씬에 시작 파지와 팔 움직임 한 주기를 구현했다. 영상·텍스트 전용으로 만든 앞선 초안을 가져왔으며, 트래킹 녹화는 동작 입력으로 사용하지 않았다. 추가 장비 없이 에디터에서 미리 볼 수 있다.

[시연 씬](C:/UnityProjects/GuideChuna/Assets/Demos/Rib2VideoTextScene/V01/Rib2VideoTextDemo.unity) · [씬 미리보기 영상](C:/UnityProjects/GuideChuna/Captures/Rib2UnityScene_20261026/scene_preview_final.mp4) · [원본과 Unity 나란히 보기](C:/UnityProjects/GuideChuna/Captures/Rib2UnityScene_20261026/source_vs_unity.mp4)

![시작 파지](C:/UnityProjects/GuideChuna/Captures/Rib2UnityScene_20261026/start_pose_final.png)

## 바로 보는 방법

현재 Unity에 새 씬을 추가로 열어 두었다. Hierarchy에서 `Rib2 Video Text Demonstration`을 선택하면 Inspector의 **시연 미리보기**에 버튼이 나온다.

1. **시작 파지**: 0초 자세로 돌아간다.
2. **전체 재생**: 시작 파지와 준비 동작을 포함한 8.23초를 본다.
3. **팔 한 주기**: 3.75초부터 마지막 관찰 프레임까지 본다.
4. **일시정지**, **0.5배**, **시간(초)** 슬라이더로 자세를 천천히 살펴본다.

Inspector 미리보기는 Play 없이 동작한다. Scene 뷰에서 시점을 바꿔 볼 수 있고 Game 뷰에는 별도 시연 카메라와 조작 UI가 있다. Game 뷰 버튼의 실제 클릭 동작은 아래 Play 확인 대상이다.

청록 손은 환자의 팔을 움직이는 손, 주황 손은 흉곽을 지지하는 손이다. 복제 환자의 팔은 청록 손 파지점에 연결했다. 팔뼈 길이는 모델에서 측정한 길이를 유지한다. 원본 CSV와 시나리오 조건은 수정하지 않았다.

## 자료와 재구성 범위

- 원본 영상: `D:/전문가 영상 촬영본/3인칭 시점 카메라/제2늑골 교정기법.MOV`, 56.50–64.75초 구간.
- 동작 입력: [영상·텍스트 전용 CSV](C:/UnityProjects/GuideChuna/Captures/Rib2VideoTextOnly_V0.1_20261026/video_text_demo.csv)와 같은 폴더의 `source_annotations.json`. 새 씬 Data 폴더 복사본과 SHA256이 같다.
- 248개 표본, 양손 각 26개 관절. 손목 궤적은 영상 관찰점으로 제작한 2.5D 초안이고, 깊이는 가정했다. 손가락 모양도 작성된 파지 초안이다.
- 환자의 팔꿈치는 영상의 9개 관찰점을 참고하여 배치했다. 영상의 투영 좌표를 모델 좌표로 옮겼기 때문에 실제 팔꿈치 굽힘·손목 회전·접촉 깊이와 차이가 날 수 있다.
- 흉곽 접촉 위치의 X/Z는 상흉곽 근사 위치다. 그 위치의 표면 높이는 복제 모델 메시에서 측정했다. 제2늑골 해부학적 위치를 자동 검출한 것은 아니다.
- 복제 모델 팔 길이에 맞추는 과정에서 손 파지 위치가 최대 **3.09cm** 이동한다. 표시용 도달거리 보정이며, 조건 통과 각도와 무관하다.
- 나란히 보기 영상은 왼쪽이 원본, 오른쪽이 이 Unity 씬이다. 같은 시작 시각을 사용했다. 오른쪽은 Editor에서 실제 런타임 포즈 함수를 호출하고 현재 본을 CPU BakeMesh로 렌더한 기록이다. Play 녹화는 아니다.

## 저장된 새 컴포넌트

새 씬 루트에 아래 세 컴포넌트만 추가했으며 필요한 참조는 연결해 두었다. 기존 공유 컴포넌트 코드는 고치지 않았다.

| 컴포넌트 | 저장 내용 |
|---|---|
| `Rib2VideoTextScenePlayer` | 새 motionCsv, motionFrame, 복제 양손, leftGripAnchor, 양손 retargetEuler `(0,0,180)`, autoPlay `false` |
| `Rib2DemoPatientArm` | 복제 오른팔 본 3개, wristTarget, elbowHint, motionFrame, player, guideHandRoot, 관찰 시각·위치 9개 |
| `Rib2DemoSceneControls` | player, arm, 슬라이더, 시간 표기, 0.5배 표기 |

PatientArm의 숨김 직렬화 데이터 `referencePoseRecorded=true`와 `upperRest/forearmRest/handRest`는 원본 FBX 기준 회전을 한 번 저장한 것이다. 사용자 튜닝 항목이 아니다. 이를 보존하여 씬을 다시 열어도 팔 회전이 달라지지 않게 했다. 새 bool의 코드 기본값은 false이며, 에디터에서 이 새 씬 인스턴스에 실제 값을 기록했다.

## 확인 결과

| 검증 | 결과 |
|---|---|
| 런타임 `dotnet build --no-incremental` | 오류 0, 기존 경고 14, 새 파일 경고 0 |
| Editor `dotnet build --no-incremental` | 오류 0, 기존 경고 15, 새 파일 경고 0 |
| Unity Inspector 미리보기 버튼 | 실제 버튼 클릭 후 시간·팔·손 변화 확인 |
| 동일 런타임 함수로 248개 자세 평가 | 손목 솔버 최대 잔차 약 0.00125mm, 팔뼈 길이 변화 0.001mm 미만 |
| 새 씬 저장·닫기·다시 열기 | 5개 자세의 메시 정점 331,710개 비교, 최대 차이 0m |
| 새 씬 배선 | 런타임 내부 참조 17개, UI 콜백 5개 유효 |
| 기존 파일 보존 | 원본 손 CSV 4개, 제2늑골 CSV·Config, TrainingScene·lobby, 영상 전용 입력 2개 등 총 10개 해시 동일 |
| Play·Quest·실제 사람 접촉 정확도 | **미검증** |

[배선 점검](C:/UnityProjects/GuideChuna/Captures/Rib2UnityScene_20261026/scene_audit.md), [표본 측정](C:/UnityProjects/GuideChuna/Captures/Rib2UnityScene_20261026/sweep_result.json), [보존 해시](C:/UnityProjects/GuideChuna/Captures/Rib2UnityScene_20261026/preservation_verified.json), [재로드 비교](C:/UnityProjects/GuideChuna/Captures/Rib2UnityScene_20261026/reload_verification.txt)에 근거를 남겼다.

`change-impact`도 실행했다. 도구는 TrainingScene/lobby만 검색하므로 새 씬의 컴포넌트를 0개로 보고한다. 새 씬을 별도 audit하여 실제 배선을 확인했다. `leftGuideHand/rightGuideHand/time` 경고는 다른 클래스와의 이름 중복이며 기존 직렬화 값을 변경한 경우가 아니다. 기존 ScenarioUIController 이벤트와 나레이션 시각 경고도 보고되었지만 이번 커밋의 변경 대상이 아니다. 별도 시연의 새 CSV는 Scenario CSV가 아니며 기존 나레이션 문구 변경은 없다. Unity가 생성한 씬·메타·머티리얼의 공백 경고는 텍스트 치환하지 않았다.

## 남은 Play 확인 절차

1. 기존 TrainingScene의 미저장 작업은 사용자가 관리한 뒤, 새 `Rib2VideoTextDemo` 씬만 연다. 지금은 원래 씬의 미저장 상태를 보존하려고 추가로 열어 두었다.
2. Play를 누르고 Game 뷰에서 **시작 파지 → 팔 한 주기 → 재생/정지 → 0.5배 → 슬라이더**를 확인한다.
3. 청록 손과 환자 손목 연결, 팔 메시 회전, 주황 손의 흉곽 위치를 확인한다. Play를 종료하고 씬을 다시 열어 시작 자세도 확인한다.
4. 문제가 있으면 Console의 `[Rib2SceneDemo]`, `[Rib2SceneArm]` 로그를 확인한다.

이번 씬은 PC 카메라로 보는 시연 초안이다. Quest용 XR 조작과 실습 판정 연동은 추가하지 않았다. 기존 통과 조건과 실습 UI는 보존했고, 이 별도 씬에는 검토용 조작 UI를 만들었다. 기준 커밋은 `eaeb1c3`이며 **기준(Play 미검증)**으로 기록한다.
