# GuideChuna 프로젝트 문서

> **최종 업데이트:** 2026-02-23
> **플랫폼:** Meta Quest VR (Oculus/OpenXR)
> **목적:** 추나요법(Korean Manual Therapy) 의료 교육 시뮬레이션

---

## 목차

1. [프로젝트 개요](#1-프로젝트-개요)
2. [디렉토리 구조](#2-디렉토리-구조)
3. [핵심 시스템 상세](#3-핵심-시스템-상세)
4. [씬(Scene) 구성](#4-씬scene-구성)
5. [데이터 및 리소스](#5-데이터-및-리소스)
6. [핵심 워크플로우](#6-핵심-워크플로우)
7. [외부 의존성](#7-외부-의존성)
8. [성능 최적화](#8-성능-최적화)

---

## 1. 프로젝트 개요

GuideChuna는 **Meta Quest VR 환경**에서 동작하는 추나요법 교육 시뮬레이션 애플리케이션입니다.

- **총 스크립트:** 89개 C# 파일 (`Assets/Scripts/ClaudeScripts/`)
- **총 씬:** 17개
- **핵드포즈 CSV 데이터:** 30개 이상
- **아키텍처:** 이벤트 기반 모듈형 설계

### 주요 기능
- CSV 기반 시나리오 관리 (시나리오 → 페이즈 → 스텝 → 서브스텝 4단계 계층구조)
- 실시간 핸드 포즈 추적 및 유사도 평가
- 난이도별 가이드 핸드 표시 및 내레이션 제공
- 훈련 결과 추적 및 피드백
- AWS 기반 인증 시스템

---

## 2. 디렉토리 구조

```
C:\UnityProjects\GuideChuna\
├── Assets/
│   ├── Scripts/ClaudeScripts/         # 핵심 애플리케이션 로직 (89개 스크립트)
│   │   ├── Auth/                      # 인증 및 로비
│   │   ├── ChunaData/                 # 핸드포즈 평가 및 모션 데이터
│   │   │   └── Helpers/               # 평가 보조 시스템
│   │   ├── PoseData/                  # 포즈 데이터 로딩/비교/녹화
│   │   ├── Practice/                  # 연습 모드
│   │   ├── Recording/                 # 카메라 녹화
│   │   ├── Result/                    # 결과 추적
│   │   ├── Scenario/                  # 시나리오 관리 (핵심)
│   │   ├── Training/                  # 난이도 관리
│   │   ├── UI/                        # UI 컨트롤러
│   │   ├── Utils/                     # 유틸리티
│   │   └── WebView/                   # 웹뷰
│   ├── Scenes/                        # 17개 유니티 씬
│   ├── Resources/                     # 리소스 파일
│   │   ├── HandPoseData/              # 핸드포즈 CSV 데이터
│   │   ├── Narrations/                # 내레이션 오디오
│   │   ├── Scenarios/                 # 시나리오 CSV
│   │   └── Videos/                    # 시범 영상
│   ├── Prefabs/                       # UI 프리팹
│   ├── Materials/                     # 3D 머티리얼
│   └── Plugins/                       # DOTween, Oculus 등
├── ProjectSettings/                   # 유니티 프로젝트 설정
└── Packages/                          # 패키지 매니페스트
```

---

## 3. 핵심 시스템 상세

### 3.1 시나리오 시스템 (`Scenario/`)

CSV 파일로 구동되는 시나리오 관리 시스템으로, 프로젝트의 핵심 모듈입니다.

| 클래스 | 파일 | 역할 |
|--------|------|------|
| `ScenarioDataClasses` | ScenarioDataClasses.cs | 4단계 계층 데이터 구조 정의 (`ScenarioData` → `PhaseData` → `StepData` → `SubStepData`) |
| `ScenarioCSVLoader` | ScenarioCSVLoader.cs | RFC 4180 표준 CSV 파서, UTF-8/EUC-KR 인코딩 자동 감지, 20개 이상 컬럼 파싱 |
| `ScenarioManager` | ScenarioManager.cs | 시나리오 진행 오케스트레이터, 페이즈/스텝/서브스텝 전환, Animator 컨트롤러 자동 전환 |
| `ScenarioConditionManager` | ScenarioConditionManager.cs | 조건 검사 및 스텝 진행 (`IScenarioCondition` 인터페이스), 6가지 조건 타입 지원 |
| `ScenarioEventSystem` | ScenarioEventSystem.cs | 싱글톤 이벤트 허브, 모든 모듈 간 느슨한 결합 담당 |

**시나리오 CSV 구조 (20개 컬럼):**
```
scenarioNo, scenarioName, phase, stepName, stepNo, subStepNo, duration,
textInstruction, voiceInstruction, handTrackingFileName, conditionType, conditionParams,
patientAnimationClip, movementType, videoStartTime, videoEndTime,
contactTarget, pivotTarget, pivotPlaneAxis, invertAngle
```

**지원하는 조건 타입 (conditionType):**
- `HandPose` - 핸드포즈 유사도 기반
- `Duration` - 시간 기반
- `Manual` - 수동 버튼 클릭
- `Narration` - 내레이션 완료 대기
- `PatientAnimation` - 환자 애니메이션 완료 대기
- `None` - 즉시 통과

---

### 3.2 핸드포즈 추적 및 평가 시스템 (`ChunaData/`)

실시간으로 사용자의 손 동작을 추적하고 기준 데이터와 비교하여 평가합니다.

| 클래스 | 파일 | 역할 |
|--------|------|------|
| `ChunaPathEvaluator` | ChunaPathEvaluator.cs | 핵심 평가 엔진 (200+ Inspector 속성), 체크포인트 기반 경로 평가 |
| `ChunaPathEvaluatorBridge` | ChunaPathEvaluatorBridge.cs | CSV 데이터 로딩 → 평가 엔진 연결 브릿지 |
| `ChunaLimitChecker` | ChunaLimitChecker.cs | 관절 가동 범위 검증 (Warning 30%, Danger 50%) |
| `ChunaMotionDataManager` | ChunaMotionDataManager.cs | 싱글톤 모션 데이터 관리, 캐싱 및 검증 |
| `CheckpointGenerator` | CheckpointGenerator.cs | 핸드포즈 데이터에서 평가 체크포인트 자동 생성 |

**평가 단계 (Evaluation Phases):**
```
Idle → WaitingForStart → StartHold → Moving → MidHold → Completed
```

**충돌 감지 모드:** Sphere, Box, PalmOnly

**접촉 대상 (Contact Target):** Head, HeadAndShoulder, Chest

---

### 3.3 핸드포즈 데이터 시스템 (`PoseData/`)

| 클래스 | 파일 | 역할 |
|--------|------|------|
| `HandPoseDataLoader` | HandPoseDataLoader.cs | CSV 기반 핸드포즈 로딩, UTF-8/EUC-KR 지원 |
| `HandPoseComparator` | HandPoseComparator.cs | 포즈 유사도 분석 (Wrist 40% + Rotation 40% + Joints 20%), 3프레임 스무딩 |
| `HandPoseRecorder` | HandPoseRecorder.cs | 사용자 핸드포즈 CSV 녹화 |

**유사도 가중치:**
- 손목 위치(Wrist Position): 40%
- 손목 회전(Rotation): 40%
- 관절 포즈(Joint Pose): 20%

---

### 3.4 평가 보조 시스템 (`ChunaData/Helpers/`)

| 클래스 | 파일 | 역할 |
|--------|------|------|
| `HandCollisionDetector` | HandCollisionDetector.cs | 충돌 기반 접촉 감지 (Sphere/Box/PalmOnly) |
| `EvaluationScoringEngine` | EvaluationScoringEngine.cs | 유사도 점수 계산 알고리즘 |
| `EvaluationModeConfigurator` | EvaluationModeConfigurator.cs | CSV 기반 평가 모드 설정 적용 |
| `AutoPlayHandler` | AutoPlayHandler.cs | 환자 모델 애니메이션 자동 재생 |
| `GuideHandPlaybackController` | GuideHandPlaybackController.cs | 가이드 핸드 시각화 (루프, 투명도 제어) |
| `EvaluationPhaseManager` | EvaluationPhaseManager.cs | 평가 단계 전환 로직, 속도 기반 홀드 감지 |

---

### 3.5 인증 및 로비 시스템 (`Auth/`)

| 클래스 | 파일 | 역할 |
|--------|------|------|
| `AuthenticationService_Final` | AuthenticationService_Final.cs | AWS API 연동 인증 (ap-northeast-2), 디바이스 SN+UUID 인증 |
| `IAuthenticationService` | IAuthenticationInterfaces.cs | 인증 서비스 인터페이스 |
| `AuthDataClasses` | AuthDataClasses.cs | 인증 데이터 모델 |
| `AuthFlowManager` | AuthFlowManager.cs | 인증 흐름 오케스트레이션 |
| `LoginStateStore` | LoginStateStore.cs | 인증 상태 영속화 |
| `LobbyAuthUI_Complete` | LobbyAuthUI_Complete.cs | 로비 인증 UI |
| `ScenarioCardButton` | ScenarioCardButton.cs | 시나리오 선택 카드 UI |
| `LobbyPopupHandler` | LobbyPopupHandler.cs | 팝업 다이얼로그 |

---

### 3.6 결과 추적 시스템 (`Result/`)

| 클래스 | 파일 | 역할 |
|--------|------|------|
| `TrainingResultTracker` | TrainingResultTracker.cs | 서브스텝별 메트릭 수집 (유사도, 경고, 스킵), 경고 음향 효과 |
| `TrainingResultData` | TrainingResultData.cs | 결과 데이터 구조 (총 시간, 완료 상태, 스텝별 유사도 등) |

---

### 3.7 연습 모드 (`Practice/`)

| 클래스 | 파일 | 역할 |
|--------|------|------|
| `PracticeManager` | PracticeManager.cs | 7단계 가이드 튜토리얼, 3사이클 반복 연습, 난이도 선택 |
| `PracticeSceneSetup` | PracticeSceneSetup.cs | 연습 씬 초기화 |
| `PracticeUI` | PracticeUI.cs | 연습 모드 UI |
| `ButtonHighlighter` | ButtonHighlighter.cs | 버튼 하이라이트 효과 |
| `ToggleHighlighter` | ToggleHighlighter.cs | 토글 하이라이트 효과 |
| `UIGrabDetector` | UIGrabDetector.cs | 핸드 UI 그랩 감지 |
| `PatientPositionDetector` | PatientPositionDetector.cs | 환자 위치 변경 모니터링 |
| `LateralFlexionDetector` | LateralFlexionDetector.cs | 측굴 동작 감지 |

---

### 3.8 난이도 관리 시스템 (`Training/`)

| 클래스 | 파일 | 역할 |
|--------|------|------|
| `DifficultyManager` | DifficultyManager.cs | 싱글톤 난이도 프리셋, 이벤트 기반 난이도 변경 |
| `DifficultySettings` | DifficultySettings.cs | 난이도 프리셋 데이터 구조 |

**난이도 레벨:**
| 레벨 | 가이드 핸드 | 내레이션 | 시도 추적 | 사전평가 |
|-------|-----------|---------|----------|---------|
| Beginner (초급) | 표시 | 상세 | 비활성 | 비활성 |
| Intermediate (중급) | 일부 표시 | 간략 | 활성 | 활성 |
| Advanced (고급) | 미표시 | 최소 | 활성 | 활성 |

---

### 3.9 UI 시스템 (`UI/`)

| 클래스 | 파일 | 역할 |
|--------|------|------|
| `ScenarioUIController` | ScenarioUIController.cs | 시나리오 진행 메인 UI, 단계 설명/버튼 상태 관리 |
| `DotTimelineController` | DotTimelineController.cs | 도트 타임라인 진행 표시 |
| `SceneLoader` | SceneLoader.cs | 비동기 씬 로딩, 카메라 위치 보존 |
| `QuizPanel` | QuizPanel.cs | 훈련 후 퀴즈 UI |
| `SurveyPanel` | SurveyPanel.cs | 사용자 설문 |
| `BaseUIPanel` | BaseUIPanel.cs | 재사용 가능 패널 베이스 클래스 |
| `InfoPanelController` | InfoPanelController.cs | 멀티탭 컨텐츠 (Video/Results/Skeleton) |
| `AngleDisplayController` | AngleDisplayController.cs | 실시간 각도 시각화 |
| `HoldProgressIndicator` | HoldProgressIndicator.cs | 홀드 진행 표시 |
| `StepFeedbackUI` | StepFeedbackUI.cs | 스텝별 유사도 피드백 |
| `TunaResultUI` | TunaResultUI.cs | 결과 표시 |
| `ChunaResultTableUI` | ChunaResultTableUI.cs | 결과 테이블 |
| `DynamicResultTableUI` | DynamicResultTableUI.cs | 동적 결과 테이블 |
| `ExitPopupController` | ExitPopupController.cs | 종료 확인 팝업 |
| `SettingsPopupController` | SettingsPopupController.cs | 설정 UI |
| `SimulationStartController` | SimulationStartController.cs | 훈련 시작 UI |

---

### 3.10 유틸리티 (`Utils/`)

| 클래스 | 파일 | 역할 |
|--------|------|------|
| `ChunaLogger` | ChunaLogger.cs | 조건부 로깅 (`[Conditional]` 어트리뷰트), 릴리즈 빌드 자동 제거, 프레임 제한 로깅 |
| `PrefsKeys` | PrefsKeys.cs | PlayerPrefs 키 중앙 관리 |

---

### 3.11 웹뷰 및 녹화 (`WebView/`, `Recording/`)

| 클래스 | 파일 | 역할 |
|--------|------|------|
| `WebViewBrowserUI` | WebViewBrowserUI.cs | 인앱 웹 브라우저 |
| `VRWebViewInput` | VRWebViewInput.cs | VR 키보드 입력 |
| `SystemKeyboardBridge` | SystemKeyboardBridge.cs | 시스템 키보드 연동 |
| `DualCameraRecorder` | DualCameraRecorder.cs | 멀티 카메라 녹화 |

---

## 4. 씬(Scene) 구성

| 씬 | 용도 |
|----|------|
| `AuthMain Copy.unity` | 인증/로비 화면 |
| `lobby.unity` | 메인 로비/메뉴 |
| `LoadingScene.unity` | 로딩 화면 |
| `Practice_Scene.unity` | 튜토리얼/연습 모드 |
| `Scenario_1.unity` ~ `Scenario_5.unity` | 훈련 시나리오 (5개) |
| `Chuna_Chest.unity` | 흉추 시나리오 |
| `Chuna_lavo_Seat.unity` | 좌위 환자 자세 |
| `Chuna_upper_Seat.unity` | 상체 좌위 |
| `Chuna_upper_Seat_XR.unity` | 상체 좌위 (XR) |
| `Chuna_Scalene_new.unity` | 사각근(Scalene) |
| `Chuna_SCM_new.unity` | 흉쇄유돌근(SCM) |
| `Chuna_Play.unity` | 메인 훈련 씬 |
| `SampleScene.unity` | 개발/테스트 씬 |

---

## 5. 데이터 및 리소스

### 5.1 핸드포즈 CSV 데이터 (`Resources/HandPoseData/`)
- **30개 이상** 파일
- **포맷:** `frameIndex, handType, jointId, localPos(x,y,z), localRot(x,y,z,w), rootPos(x,y,z), rootRot(x,y,z,w), timestamp`
- **대상 술기:**
  - 측굴 (Lateral Flexion)
  - 환측회전 / 건측회전 (Rotation)
  - 등척성운동 (Isometric Exercise)
  - 스트레칭/재평가 변형

### 5.2 시나리오 CSV (`Resources/Scenarios/`)
- 마스터 CSV 파일로 시나리오 구조 정의
- 20개 컬럼의 상세 시나리오 데이터

### 5.3 내레이션 오디오 (`Resources/Narrations/`)
- 초급(Beginner), 중급(Intermediate), 연습(Practice) 레벨별 오디오
- 서브스텝과 동기화

### 5.4 시범 영상 (`Resources/Videos/`)
- 전문가 시범 영상 (시간 세그먼트 포함)

---

## 6. 핵심 워크플로우

### 6.1 시나리오 실행 흐름

```
[로비에서 난이도/모드 선택]
        ↓
ScenarioManager.StartScenario()
        ↓
ScenarioCSVLoader → CSV 파싱 → 계층 데이터 조립
        ↓
┌─────────────────────────────────────┐
│  각 SubStep에 대해:                   │
│  1. UI 업데이트 (설명, 버튼)           │
│  2. ContactTarget/Pivot 설정 적용     │
│  3. 내레이션 재생                     │
│  4. 핸드포즈 CSV 로딩                 │
│  5. 환자 애니메이션 시작               │
│  6. 조건 검사 (HandPose/Duration 등)  │
│  7. 결과 기록                        │
│  8. 진행률 UI 업데이트                │
└─────────────────────────────────────┘
        ↓
[완료 시: 결과 패널 + 퀴즈 패널 표시]
```

### 6.2 핸드포즈 평가 흐름

```
ChunaPathEvaluator: CSV 핸드포즈 데이터 로딩
        ↓
CheckpointGenerator: 프레임 데이터에서 체크포인트 생성
        ↓
실시간 비교: 사용자 손 vs 가이드 핸드
        ↓
HandPoseComparator: 유사도 계산
  ├── Wrist Position: 40%
  ├── Rotation: 40%
  └── Joint Pose: 20%
        ↓
충돌 감지 → 접촉 상태 트리거
        ↓
홀드 완료 시 단계 전환
        ↓
프레임별 유사도 기록 → TrainingResultData 집계
```

### 6.3 데이터 흐름 전체 구조

```
CSV (Scenario)
     ↓
ScenarioCSVLoader → ScenarioManager
     ↓
ScenarioEventSystem (이벤트 브로드캐스트)
     ├→ ScenarioUIController (UI 업데이트)
     ├→ ScenarioConditionManager (조건 검사)
     ├→ TrainingResultTracker (메트릭 기록)
     └→ ChunaPathEvaluator (핸드 트래킹)
          ├→ HandPoseComparator (유사도 계산)
          ├→ ChunaLimitChecker (가동범위 검증)
          ├→ GuideHandPlaybackController (가이드 핸드 표시)
          └→ AutoPlayHandler (환자 애니메이션)
```

---

## 7. 외부 의존성

| 패키지 | 용도 |
|--------|------|
| **Oculus Interaction SDK** | 핸드 트래킹 및 입력 |
| **Meta XR SDK** | VR 플랫폼 통합 |
| **TextMesh Pro** | UI 텍스트 렌더링 |
| **DOTween** | 애니메이션 트위닝 |
| **Newtonsoft.Json (JSON.NET)** | JSON 직렬화 |
| **UniTask (Cysharp)** | Async/Await 지원 |
| **TLabWebView** | 인앱 웹 브라우징 |

---

## 8. 성능 최적화

| 기법 | 설명 |
|------|------|
| **ChunaLogger** | `[Conditional]` 컴파일 → Release 빌드에서 로그 자동 제거 |
| **SceneLoader** | `WaitForSeconds` 캐싱으로 GC 할당 감소 |
| **HandCollisionDetector** | 형상 기반 감지 (Sphere/Box) 선택으로 성능 최적화 |
| **Coroutine 캐싱** | 반복 사용되는 `WaitForSeconds` 객체 재활용 |
| **프레임 기반 체크포인트** | 연속 레이캐스트 대신 프레임 기반 검사 |
| **유사도 샘플링** | 매 프레임이 아닌 간격 기반 샘플링 |

---

## 부록: 전체 스크립트 목록

### Scenario/ (5개)
- `ScenarioDataClasses.cs`, `ScenarioCSVLoader.cs`, `ScenarioManager.cs`
- `ScenarioConditionManager.cs`, `ScenarioEventSystem.cs`

### ChunaData/ (5개 + Helpers 6개)
- `ChunaPathEvaluator.cs`, `ChunaPathEvaluatorBridge.cs`, `ChunaLimitChecker.cs`
- `ChunaMotionDataManager.cs`, `CheckpointGenerator.cs`, `PathCheckpoint.cs`
- Helpers: `HandCollisionDetector.cs`, `EvaluationScoringEngine.cs`, `EvaluationModeConfigurator.cs`, `AutoPlayHandler.cs`, `GuideHandPlaybackController.cs`, `EvaluationPhaseManager.cs`

### PoseData/ (5개)
- `HandPoseDataLoader.cs`, `HandPoseComparator.cs`, `HandPoseRecorder.cs`
- `PoseRecorder.cs`, `PosePlayer.cs`, `ObjectController.cs`

### Auth/ (8개)
- `AuthenticationService_Final.cs`, `IAuthenticationInterfaces.cs`, `AuthDataClasses.cs`
- `AuthFlowManager.cs`, `LoginStateStore.cs`, `LobbyAuthUI_Complete.cs`
- `ScenarioCardButton.cs`, `LobbyPopupHandler.cs`

### Result/ (2개)
- `TrainingResultTracker.cs`, `TrainingResultData.cs`

### Practice/ (9개)
- `PracticeManager.cs`, `PracticeSceneSetup.cs`, `PracticeUI.cs`
- `ButtonHighlighter.cs`, `ToggleHighlighter.cs`, `UIGrabDetector.cs`
- `PatientPositionDetector.cs`, `LateralFlexionDetector.cs`
- `Editor/PracticeSceneEditor.cs`

### Training/ (2개)
- `DifficultyManager.cs`, `DifficultySettings.cs`

### UI/ (16개)
- `ScenarioUIController.cs`, `DotTimelineController.cs`, `SceneLoader.cs`
- `QuizPanel.cs`, `SurveyPanel.cs`, `BaseUIPanel.cs`, `InfoPanelController.cs`
- `AngleDisplayController.cs`, `HoldProgressIndicator.cs`, `StepFeedbackUI.cs`
- `TunaResultUI.cs`, `ChunaResultTableUI.cs`, `DynamicResultTableUI.cs`
- `ExitPopupController.cs`, `SettingsPopupController.cs`, `SimulationStartController.cs`

### Utils/ (2개)
- `ChunaLogger.cs`, `PrefsKeys.cs`

### WebView/ (3개)
- `WebViewBrowserUI.cs`, `VRWebViewInput.cs`, `SystemKeyboardBridge.cs`

### Recording/ (1개)
- `DualCameraRecorder.cs`
