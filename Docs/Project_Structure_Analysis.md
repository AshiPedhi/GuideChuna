# GuideChuna 프로젝트 구조 분석서

> **프로젝트명**: GuideChuna (추나 시술 VR 훈련 시스템)
> **플랫폼**: Meta Quest (Oculus) VR / XR
> **개발 환경**: Unity (Meta XR SDK 81.0.0)
> **프로젝트 규모**: 약 1,500+ 파일, C# 스크립트 99개 (ClaudeScripts)

---

## 1. 프로젝트 개요

추나 시술(Chuna Manual Therapy)을 VR 환경에서 학습/실습/평가할 수 있는 의료 교육 시뮬레이터.
사용자는 VR 헤드셋(Meta Quest)을 착용하고, 가상 환자에게 추나 시술을 수행하며
핸드트래킹 기반으로 실시간 평가를 받는다.

---

## 2. Assets 폴더 구조

```
Assets/
├── Scripts/
│   ├── ClaudeScripts/          ← 핵심 로직 (99개 C# 파일)
│   │   ├── Auth/               ← 인증 시스템 (11개)
│   │   ├── ChunaData/          ← 추나 평가 시스템 (29개)
│   │   │   ├── Helpers/        ← 평가 헬퍼 (8개)
│   │   │   └── Editor/         ← 에디터 도구 (2개)
│   │   ├── Scenario/           ← 시나리오 시스템 (14개)
│   │   ├── PoseData/           ← 핸드 포즈 데이터 (12개)
│   │   │   └── Editor/         ← 에디터 도구 (1개)
│   │   ├── Practice/           ← 실습 모드 (9개)
│   │   │   └── Editor/         ← 에디터 도구 (1개)
│   │   ├── UI/                 ← UI 시스템 (24개)
│   │   ├── Result/             ← 결과 추적 (2개)
│   │   ├── Patient/            ← 환자 모델 관리 (2개)
│   │   │   └── Editor/         ← 에디터 도구 (1개)
│   │   ├── Training/           ← 난이도 시스템 (2개)
│   │   ├── Recording/          ← 듀얼 카메라 녹화 (1개)
│   │   ├── Utils/              ← 유틸리티 (2개)
│   │   ├── WebView/            ← 웹뷰 (3개)
│   │   └── HandDateEditor/     ← 핸드 데이터 에디터
│   │
│   └── Mirroring/
│       └── RenderManager.cs    ← VR 미러링 관리
│
├── _JDH/Script/                ← 레거시 스크립트 (20+개)
├── _NJS/Scripts/               ← 손 트래킹 스크립트 (3개)
│
├── Resources/
│   ├── Scenarios/              ← 시나리오 CSV 파일들
│   ├── HandPoseData/           ← 핸드 포즈 CSV (54개)
│   ├── Videos/                 ← 가이드 영상
│   └── Narrations/             ← 음성 가이드
│       ├── Beginner/           ← 초급 (30개 mp3)
│       └── Intermediate/       ← 중급 (30개 wav)
│
├── Scenes/                     ← 씬 파일들 (18개)
├── Prefabs/                    ← 프리팹들
├── Plugins/
│   └── Demigiant/DOTween/      ← DOTween 애니메이션 라이브러리
├── Samples/
│   └── Unity Render Streaming/ ← 렌더 스트리밍 샘플 (27개)
│
├── ApiConfig.cs                ← API 설정 (ScriptableObject)
└── ObjectPool.cs               ← 오브젝트 풀링 유틸리티
```

---

## 3. 씬(Scene) 목록 및 용도

| 씬 파일 | 용도 |
|--------|------|
| `lobby.unity` | 메인 로비 (사용자/시나리오 선택) |
| `LoadingScene.unity` | 로딩 화면 (비동기 씬 로드) |
| `Practice_Scene.unity` | 실습 모드 (튜토리얼 7단계) |
| `Chuna_Play.unity` | 기본 추나 시술 씬 |
| `Chuna_Chest.unity` | 흉부(대흉근) 추나 씬 |
| `Chuna_SCM_new.unity` | 흉쇄유돌근(SCM) 추나 씬 |
| `Chuna_Scalene_new.unity` | 사각근(Scalene) 추나 씬 |
| `Chuna_upper_Seat.unity` | 상부승모근 추나 (좌식) |
| `Chuna_upper_Seat_XR.unity` | 상부승모근 추나 XR 버전 |
| `Chuna_lavo_Seat.unity` | 하부 추나 (좌식) |
| `Scenario_1.unity` ~ `Scenario_5.unity` | 시나리오 기반 훈련 (각각 별도 씬) |
| `AuthMain Copy.unity` | 인증 메인 (테스트/백업) |
| `SampleScene.unity` | 샘플/테스트 씬 |

---

## 4. 핵심 시스템별 상세 분석

### 4.1 인증 시스템 (Auth/ - 11개 파일)

```
인증 흐름:
[사용자 입력] → AuthenticationService_Final → AWS Lambda API → 응답
                     ↓
              AuthEvents (로그인 성공/실패 이벤트)
                     ↓
              LoginStateStore (PlayerPrefs에 상태 저장)
                     ↓
              LobbyAuthUI_Complete (로비 UI 갱신)
                     ↓
              RenderManager.SetMirroringData() (미러링 시작)
```

| 파일 | 역할 |
|------|------|
| `AuthenticationService_Final.cs` | AWS API 기반 디바이스/사용자 인증 서비스 |
| `AuthFlowManager.cs` | 로그인 흐름 전체 관리 (단계별 진행) |
| `LoginStateStore.cs` | 로그인 상태 PlayerPrefs 저장/복구 |
| `LobbyAuthUI_Complete.cs` | 로비 UI 전체 제어 (로그인~시나리오 선택) |
| `LobbyPopupHandler.cs` | 로비 팝업 처리 |
| `GradeSelectionHandler.cs` | 난이도 선택 UI 처리 |
| `UserSelectionHandler.cs` | 사용자 선택 UI 처리 |
| `ScenarioCardButton.cs` | 시나리오 카드 버튼 컴포넌트 |
| `AuthDataClasses.cs` | 인증 관련 데이터 클래스 (MirroringData 등) |
| `AuthEvents.cs` | 인증 이벤트 정의 |
| `IAuthenticationInterfaces.cs` | 인증 서비스 인터페이스 (테스트 가능) |

**API 설정**:
- 엔드포인트: AWS Lambda (ap-northeast-2 서울)
- 타임아웃: 15초
- 인증 데이터: Device ID, Device UUID, User Login ID, Password

---

### 4.2 시나리오 시스템 (Scenario/ - 14개 파일)

```
데이터 구조:
ScenarioCollection
 └─ ScenarioData (시나리오 - 예: "상부승모근")
     └─ PhaseData[] (페이즈 - 예: "시작하기", "진단", "전부", "중부", "후부", "종료")
         └─ StepData[] (스텝 - 예: "가이드", "평가", "제한장벽", "등척성운동", "스트레칭")
             └─ SubStepData[] (서브스텝 - 세부 동작 단위)
                  ├─ textInstruction     (화면 안내 텍스트)
                  ├─ voiceInstruction    (음성 나레이션 파일명)
                  ├─ handTrackingFileName(핸드 포즈 기준 CSV)
                  ├─ patientAnimationClip(환자 애니메이션)
                  ├─ conditionType       (진행 조건: HandPose/Duration/Manual/Narration)
                  ├─ contactTarget       (접촉 감지 부위: Head/HeadAndShoulder/Chest)
                  ├─ pivotTarget         (피벗 포인트: Neck/LeftShoulder/RightShoulder)
                  ├─ videoStartTime/End  (가이드 영상 구간)
                  └─ movementType        (position/rotation)
```

**CSV 구조** (`Resources/Scenarios/*.csv`):
```
scenarioNo, scenarioName, phase, stepName, stepNo, subStepNo, duration,
textInstruction, voiceInstruction, handTrackingFileName, conditionType,
conditionParams, patientAnimationClip, movementType, videoStartTime,
videoEndTime, contactTarget, pivotTarget, pivotPlaneAxis, invertAngle
```

| 파일 | 역할 |
|------|------|
| `ScenarioManager.cs` | 중앙 시나리오 관리자 (CSV 로드 → 진행 → UI 연동) |
| `ScenarioCSVLoader.cs` | CSV 파싱 (UTF-8/EUC-KR 자동 감지, RFC 4180 준수) |
| `ScenarioDataClasses.cs` | 데이터 클래스 (ScenarioData, PhaseData, StepData, SubStepData) |
| `ScenarioConditionManager.cs` | 조건 판정 매니저 (HandPose, Duration, Animation, Manual) |
| `ScenarioConditionSetup.cs` | Inspector에서 조건 등록 헬퍼 |
| `ScenarioEventSystem.cs` | 시나리오 이벤트 시스템 (Phase/Step/SubStep 변경 이벤트) |
| `ScenarioUIController.cs` | 시나리오 UI 제어 |
| `ScenarioGuideUIController.cs` | 가이드 UI 전용 제어 (stepName, Phase 이미지, 진행 원형) |
| `ScenarioUIPositioner.cs` | 헤드셋 위치 기준 UI 자동 배치 |
| `ScenarioSystemInitializer.cs` | 시나리오 시스템 초기화 (컴포넌트 자동 찾기) |
| `GuideVideoController.cs` | 가이드 영상 구간 재생 (분:초 형식 지원) |
| `HandFeedbackUI.cs` | 손 위치 피드백 UI |

**조건 타입 (conditionType)**:
| 타입 | 설명 | 자동 결정 조건 |
|------|------|---------------|
| `HandPose` | 핸드 포즈 유사도 기반 | handTrackingFileName이 있으면 |
| `Duration` | 시간 경과 | duration > 0 |
| `Manual` | 수동 진행 (토글) | 가이드 Step 또는 기본값 |
| `Narration` | 나레이션 완료 대기 | voiceInstruction만 있으면 |

---

### 4.3 추나 시술 평가 시스템 (ChunaData/ - 29개 파일)

```
평가 흐름:
[손 충돌 감지] → ChunaPathEvaluator
                    ├─ 체크포인트 통과 판정
                    ├─ 유사도 계산 (0~100점)
                    ├─ 리밋 범위 초과 판정 → 경고
                    ├─ 각도 근접도 평가
                    └─ 완료 이벤트 발생
                         ↓
               ChunaPathEvaluatorBridge
                    ↓ (시나리오 시스템 연동)
               ScenarioConditionManager → 다음 SubStep 진행
```

**핵심 클래스들**:

| 파일 | 역할 |
|------|------|
| `ChunaPathEvaluator.cs` | **핵심** - 체크포인트 기반 추나 시술 실시간 평가 (v2) |
| `ChunaPathEvaluatorBridge.cs` | 평가기 ↔ 시나리오 시스템 연결 브릿지 |
| `ChunaLimitChecker.cs` | 안전 범위(리밋) 초과 체크 |
| `ChunaLimitData.cs` | ScriptableObject 기반 제한값 정의 (목/손목 회전, 손 위치, 속도) |
| `ChunaMotionDataManager.cs` | CSV 기반 모션 데이터 관리 |
| `CheckpointGenerator.cs` | 체크포인트 생성 유틸리티 |
| `PathCheckpoint.cs` | 단일 체크포인트 데이터 구조 |
| `ChunaFeedbackUI.cs` | 실시간 피드백 UI |
| `ChunaResultSummaryUI.cs` | 결과 요약 UI |
| `PatientHeadPokeDetector.cs` | 환자 머리 찌르기 감지 |
| `PatientHeadTouchDetector.cs` | 환자 머리 접촉 감지 |

**Helpers/ (8개)**:

| 파일 | 역할 |
|------|------|
| `EvaluationPhaseManager.cs` | 평가 단계(Phase) 관리 |
| `EvaluationScoringEngine.cs` | 채점 엔진 |
| `EvaluationModeConfigurator.cs` | 평가 모드 설정 |
| `GuideHandPlaybackController.cs` | 가이드 손 재생 제어 |
| `HandCollisionDetector.cs` | 손 충돌 감지 (Sphere/Box/PalmOnly 모드) |
| `AutoPlayHandler.cs` | 자동 재생 핸들러 |

**손 충돌 모드**:
- `Sphere`: 구형 충돌체 기반
- `Box`: 손바닥 + 손가락 박스 충돌체
- `PalmOnly`: 손바닥만 감지

**접촉 감지 부위 (ContactTarget)**:
- `Head`: 머리 (경추)
- `HeadAndShoulder`: 머리+어깨 (상부승모근) - 기본값
- `Chest`: 흉부 (대흉근)

---

### 4.4 핸드 포즈 데이터 시스템 (PoseData/ - 12개 파일)

| 파일 | 역할 |
|------|------|
| `HandPoseDataLoader.cs` | CSV 기반 핸드 포즈 데이터 로드 |
| `HandPoseComparator.cs` | 포즈 유사도 계산 (점수: 0~100) |
| `HandTransformMapper.cs` | OVR 손 본 → Transform 매핑 |
| `HandPoseRecorder.cs` | 포즈 기록기 |
| `PracticeHandRecorder.cs` | 실습 포즈 기록기 |
| `HandDataRecorder.cs` | 원시 데이터 기록기 |
| `PoseRecorder.cs` | 포즈 녹화기 |
| `PosePlayer.cs` | 포즈 재생기 |
| `TunaMotionData.cs` | 추나 동작 데이터 구조 (구간, 안전 범위, 체크포인트) |
| `ObjectController.cs` | 오브젝트 제어 |

**핸드 포즈 CSV 파일들** (`Resources/HandPoseData/` - 54개):
```
건측회전.csv, 환측회전.csv, 측굴.csv, 등척성운동.csv
사각근 건측회전.csv, 사각근 전부측굴.csv, 사각근 환측회전.csv
전부_스트레칭_핸드데이터_측굴.csv, 전부_재평가_핸드데이터_환측회전.csv
2_전부측굴전체범위_trimmed.csv ...
```

---

### 4.5 실습 모드 시스템 (Practice/ - 9개 파일)

```
PracticeManager 7단계:
1. UI 옮기기 → 2. 난이도 선택 → 3. 콘텐츠 설명
4. 설정 확인 → 5. 시작 → 6. 홀드 연습 → 7. 나가기
```

| 파일 | 역할 |
|------|------|
| `PracticeManager.cs` | 실습 7단계 진행 관리 |
| `PracticeSceneSetup.cs` | 실습 씬 환경 설정 |
| `PracticeUI.cs` | 실습 UI 제어 |
| `PatientPositionDetector.cs` | 환자 위치 감지 |
| `LateralFlexionDetector.cs` | 측굴 동작 감지 |
| `ButtonHighlighter.cs` | 버튼 하이라이트 효과 |
| `ToggleHighlighter.cs` | 토글 하이라이트 효과 |
| `UIGrabDetector.cs` | UI 잡기 감지 |

---

### 4.6 UI 시스템 (UI/ - 24개 파일)

| 파일 | 역할 |
|------|------|
| `InfoPanelController.cs` | **대형** - 정보 패널 종합 제어 (41KB) |
| `PracticeSettingsController.cs` | **대형** - 실습 설정 패널 (35KB) |
| `AngleDisplayController.cs` | 각도 표시 (측굴/좌회전/우회전) |
| `StepFeedbackUI.cs` | 스텝 피드백 UI |
| `HandFeedbackUI.cs` | 손 피드백 UI |
| `HoldProgressIndicator.cs` | 홀드 진행 인디케이터 |
| `DynamicResultTableUI.cs` | 동적 결과 테이블 |
| `ChunaResultTableUI.cs` | 추나 결과 테이블 |
| `TunaResultUI.cs` | 튜나 결과 UI |
| `ResultTableRowUI.cs` | 결과 테이블 행 컴포넌트 |
| `DotTimeline.cs` | 점 타임라인 (진행도 표시) |
| `DotTimelineController.cs` | 타임라인 제어 |
| `ExitPopupController.cs` | 종료 팝업 |
| `SettingsPopupController.cs` | 설정 팝업 |
| `SimulationStartController.cs` | 시뮬레이션 시작 제어 |
| `QuizPanel.cs` | 퀴즈 패널 |
| `SurveyPanel.cs` | 설문 패널 |
| `BaseUIPanel.cs` | UI 패널 베이스 클래스 |
| `SceneLoader.cs` | 씬 전환 관리 (로딩 화면 포함) |
| `UserFrameDisplay.cs` | 사용자 정보 표시 프레임 |

---

### 4.7 결과 추적 시스템 (Result/ - 2개 파일)

```
TrainingResultTracker
 ├─ SubStep별 완료/스킵 상태 추적
 ├─ 유사도 평균값 계산
 ├─ 경고 횟수 기록 (리밋 초과)
 └─ 효과음 재생 (접근 경고, 초과 경고, 완료)

TrainingResultData
 └─ PhaseResult[]
     └─ StepResult[]
         ├─ 이름, 상태 (O/△/X)
         ├─ 평균 유사도
         ├─ SubStep 통계
         └─ 경고 횟수
```

---

### 4.8 난이도 시스템 (Training/ - 2개 파일)

| 파일 | 역할 |
|------|------|
| `DifficultyManager.cs` | 싱글톤 난이도 관리자 |
| `DifficultySettings.cs` | 난이도 프리셋 정의 |

```
난이도 레벨:
- Beginner (초급): 가이드 핸드 표시, 나레이션 상세, 평가 임계값 낮음
- Intermediate (중급): 가이드 일부 표시, 나레이션 간략
- Advanced (상급): 가이드 없음, 평가 임계값 높음
```

---

### 4.9 환자 모델 관리 (Patient/ - 2개 파일)

| 파일 | 역할 |
|------|------|
| `PatientPositionManager.cs` | 프리셋 기반 환자/침대 위치 관리 |
| `PatientPositionManagerEditor.cs` | 에디터 확장 (위치 캡처 버튼) |

**프리셋 시스템**:
```
PositionPreset
 ├─ presetName (예: "Seated", "Supine", "SideLying")
 ├─ patientPosition / patientRotation
 ├─ bedPosition / bedRotation / bedActive
 ├─ cameraPosition / cameraRotation
 └─ skeletonModelPosition / skeletonModelRotation
```

---

### 4.10 기타 시스템

| 시스템 | 파일 | 역할 |
|--------|------|------|
| **유틸리티** | `ChunaLogger.cs` | 조건부 로깅 (릴리스 빌드에서 자동 제거) |
| **유틸리티** | `PrefsKeys.cs` | PlayerPrefs 키 상수 모음 |
| **녹화** | `DualCameraRecorder.cs` | 듀얼 카메라 녹화 |
| **웹뷰** | `WebViewBrowserUI.cs` | 웹뷰 UI |
| **웹뷰** | `VRWebViewInput.cs` | VR 웹뷰 입력 |
| **웹뷰** | `SystemKeyboardBridge.cs` | 시스템 키보드 연동 |
| **미러링** | `RenderManager.cs` | VR 미러링 화면 송출 (Render Streaming) |
| **API** | `ApiConfig.cs` | AWS API 설정 (ScriptableObject) |
| **풀링** | `ObjectPool.cs` | 게임 오브젝트 풀링 유틸리티 |

---

## 5. 레거시/외부 스크립트

### 5.1 _JDH/Script/ (레거시 - 20+개)

| 파일 | 역할 |
|------|------|
| `NeckVRControllerOptimized.cs` | 목 VR 제어 (최적화 버전) |
| `ChunaAnimCtrl.cs` | 추나 애니메이션 제어 |
| `GameManager.cs` | 게임 매니저 |
| `EventManager.cs` | 이벤트 매니저 |
| 기타 카메라/UI 제어 등 | |

### 5.2 _NJS/Scripts/ (손 트래킹 - 3개)

| 파일 | 역할 |
|------|------|
| `HandTrackingFingerMap.cs` | 손가락 매핑 |
| `HTFinger.cs` | 손가락 구조체 |
| `LocalFingerIK.cs` | 로컬 손가락 IK 제어 |

---

## 6. 외부 패키지 의존성

```json
"com.meta.xr.sdk.all": "81.0.0"        // Meta Quest XR SDK (전체)
"com.unity.xr.openxr": "1.15.1"        // OpenXR 런타임
"com.unity.xr.management": "4.5.3"     // XR 관리
"com.cysharp.unitask": "UniTask"        // 비동기 처리 (async/await)
"com.tlabaltoh.webview": "TLabWebView"  // VR 웹뷰
"com.unity.textmeshpro": "TextMesh Pro" // 텍스트 렌더링
"com.unity.inputsystem": "1.14.2"      // 입력 시스템
"com.unity.ai.navigation": "2.0.8"     // AI 네비게이션
```

**플러그인**:
- **DOTween** (Demigiant): 애니메이션 트위닝 라이브러리 (8개 모듈)
- **Unity Render Streaming**: WebSocket 기반 원격 화면 송출 (27개 샘플)

---

## 7. 데이터 저장 방식 정리

| 데이터 종류 | 저장 방식 | 위치 |
|------------|----------|------|
| 시나리오 진행 데이터 | CSV | `Resources/Scenarios/*.csv` |
| 핸드 포즈 기준 데이터 | CSV | `Resources/HandPoseData/*.csv` (54개) |
| 나레이션 오디오 | MP3/WAV | `Resources/Narrations/Beginner/` (30개), `Intermediate/` (30개) |
| 가이드 영상 | MP4 | `Resources/Videos/` |
| 리밋 제한값 | ScriptableObject | `ChunaLimitData` 에셋 |
| API 설정 | ScriptableObject | `ApiConfig` 에셋 |
| 환자 위치 프리셋 | Inspector (직렬화) | `PatientPositionManager` 컴포넌트 |
| 로그인 정보 | PlayerPrefs | 런타임 (기기 저장) |
| 선택 모드/난이도 | PlayerPrefs | 런타임 (기기 저장) |

### PlayerPrefs 키 목록 (PrefsKeys.cs)

```
Auth:     DEVICE_SN, LOGIN_USERNAME, LOGIN_USERID
Settings: MasterVolume, BGMVolume, SFXVolume, QualityLevel,
          ResolutionIndex, Fullscreen, VSync,
          MouseSensitivity, InvertYAxis, ShowHints
Sim:      SelectedMode, SelectedDifficulty
```

---

## 8. 의존 관계 다이어그램

```
┌─────────────────────────────────────────────────────────────────┐
│                        사용자 흐름                               │
│                                                                 │
│  [lobby.unity]                                                  │
│   AuthFlowManager                                               │
│     ├─ AuthenticationService_Final → AWS Lambda API              │
│     ├─ UserSelectionHandler                                     │
│     ├─ GradeSelectionHandler                                    │
│     └─ ScenarioCardButton → SceneLoader.LoadScene("Chuna_xxx") │
│                                                                 │
│  [LoadingScene.unity]                                           │
│   SceneLoader (비동기 로드)                                      │
│                                                                 │
│  [Chuna_xxx.unity] (시나리오별 씬)                               │
│   ScenarioSystemInitializer                                     │
│     └─ ScenarioManager                                          │
│          ├─ ScenarioCSVLoader → CSV 파싱                        │
│          ├─ ScenarioConditionManager → 조건 판정                │
│          ├─ ScenarioEventSystem → 이벤트 발행                   │
│          ├─ ScenarioGuideUIController → UI 갱신                 │
│          ├─ GuideVideoController → 영상 구간 재생               │
│          └─ ChunaPathEvaluatorBridge                            │
│               └─ ChunaPathEvaluator → 실시간 평가               │
│                    ├─ HandCollisionDetector                     │
│                    ├─ ChunaLimitChecker                         │
│                    ├─ EvaluationScoringEngine                   │
│                    └─ TrainingResultTracker → 결과 기록          │
│                                                                 │
│   PatientPositionManager → 환자/침대 위치 설정                   │
│   DifficultyManager → 난이도 적용                                │
│   RenderManager → VR 미러링 (Render Streaming)                  │
│                                                                 │
│  [결과 화면]                                                     │
│   DynamicResultTableUI / ChunaResultTableUI                     │
│   QuizPanel / SurveyPanel                                       │
│     └─ SceneLoader.LoadScene("lobby") → 로비 복귀               │
└─────────────────────────────────────────────────────────────────┘
```

---

## 9. 기존 스크립트 ↔ ClaudeScripts 의존 관계

```
기존 스크립트 (3개)
├─ ApiConfig.cs (독립적 - ScriptableObject)
├─ ObjectPool.cs (독립적 - 정적 유틸리티)
└─ RenderManager.cs
    ├─ depends on → ChunaLogger (ClaudeScripts/Utils)
    └─ depends on → MirroringData (ClaudeScripts/Auth/AuthDataClasses)

역방향:
├─ LobbyAuthUI_Complete → RenderManager.instance.SetMirroringData()
└─ SceneLoader → RenderManager.instance.IsConnected / TryReconnect()
```

---

## 10. 리소스 상세

### 10.1 캐릭터 모델
- `CC_Assets/환자_20대_남/` - Character Creator 남성 환자 모델
- `MedicXR CPX PhysicalExamination's PatientModel/` - 의료용 환자 모델
- `Models/AnatomyV3/` - 3D 해부학 모델

### 10.2 환경 에셋
- `01. Lobby/` - 로비 씬 리소스
- `03. Dental office/` - 치과 오피스 환경
- `04. Hospital room/` - 병원 침실 환경
- 한글 폴더: `냉장고/`, `병원 옷장/`, `병원 침대/`, `침대 테이블/`

### 10.3 프리팹
| 프리팹 | 용도 |
|--------|------|
| `Prefabs/ScenarioUI.prefab` | 시나리오 UI 전체 (30KB) |
| `Prefabs/DotTimeline.prefab` | 진행도 타임라인 |
| `Prefabs/Auth/Button (4).prefab` | 시나리오 카드 버튼 |
| `Prefabs/Auth/GradeTap.prefab` | 난이도 탭 |
| `Prefabs/Auth/UserButton.prefab` | 사용자 선택 버튼 |
| `Prefabs/Auth/Content.prefab` | 콘텐츠 컨테이너 |

---

## 11. 성능 최적화 특징

| 항목 | 기법 |
|------|------|
| 비동기 처리 | UniTask (async/await) |
| 애니메이션 | DOTween 라이브러리 |
| 로깅 | ChunaLogger - 릴리스 빌드에서 자동 제거 |
| 메모리 | ObjectPool - 게임 오브젝트 재사용 |
| 충돌체 | 손 충돌 크기 조정 (0.1~2.0배) |
| CSV | 인코딩 자동 감지 (UTF-8/EUC-KR) |
| 네트워크 | 재시도 로직 (최대 3회, 타임아웃 10~15초) |

---

## 12. 주요 기능 완성도

| 기능 | 담당 클래스 | 상태 |
|------|-----------|------|
| 로그인/인증 | AuthenticationService_Final | 완성 |
| 사용자 선택 | UserSelectionHandler | 완성 |
| 난이도 선택 | GradeSelectionHandler | 완성 |
| 시나리오 로드/진행 | ScenarioManager, ScenarioCSVLoader | 완성 |
| 추나 시술 평가 | ChunaPathEvaluator (v2) | 완성 |
| 포즈 유사도 비교 | HandPoseComparator | 완성 |
| 결과 추적/표시 | TrainingResultTracker | 완성 |
| 실습 모드 | PracticeManager (7단계) | 완성 |
| 영상 가이드 | GuideVideoController | 완성 |
| 음성 가이드 | Narrations (60개 클립) | 완성 |
| 환자 위치 프리셋 | PatientPositionManager | 완성 |
| 난이도 관리 | DifficultyManager | 완성 |
| VR 미러링 | RenderManager | 완성 |
| 씬 전환 | SceneLoader | 완성 |

---

## 13. 확장성 구조

- **ScriptableObject 기반**: ChunaLimitData, ApiConfig로 에디터에서 쉬운 수정
- **CSV 기반 시나리오**: 새 시나리오 추가 시 CSV 파일만 추가하면 됨
- **이벤트 시스템**: ScenarioEventSystem으로 모듈식 확장 가능
- **프리셋 시스템**: PatientPositionManager로 위치 프리셋 관리
- **인터페이스 기반**: IAuthenticationService로 Mock 테스트 가능
- **브릿지 패턴**: ChunaPathEvaluatorBridge로 평가기↔시나리오 느슨한 결합

---

## 14. 현재 구조의 한계점 / 개선 가능 포인트

1. **씬 중복**: 시나리오마다 별도 씬 (18개) → **통합 씬 + 데이터 주입** 방식으로 전환 가능
   - 자세한 설계: `Docs/SingleScene_DataDriven_Architecture.md` 참고
2. **대형 스크립트**: InfoPanelController (41KB), PracticeSettingsController (35KB) → 역할 분리 고려
3. **환자 위치**: Inspector 하드코딩 → ScriptableObject(ScenarioConfig)로 외부화 가능
4. **PlayerPrefs 의존**: 씬 간 데이터 전달에 PlayerPrefs 사용 → 정적 데이터 매니저 고려

---

## 15. 핵심 시스템 상세 동작 흐름

### 15.1 시나리오 진행 흐름

```
1. AuthenticationService (인증)
   └─→ 디바이스 UID 검증 → 사용자 목록 로드

2. ScenarioManager.StartScenario()
   ├─→ ScenarioCSVLoader.LoadScenarios() - CSV 로드
   ├─→ ScenarioEventSystem.ScenarioStarted() 이벤트 발생
   ├─→ SwitchAnimatorController() - 시나리오별 애니메이션 설정
   └─→ ScenarioUIPositioner.PositionUIElements() - UI 배치

3. ScenarioManager.OnSubStepStartedForHandPose()
   ├─→ UpdateAngleDisplayVisibility() - 각도 표시 UI 선택
   ├─→ ApplyContactTarget() - 접촉 감지 부위 설정
   ├─→ HandPose 있는 경우:
   │   ├─→ ChunaPathEvaluatorBridge.LoadFromCSV()
   │   ├─→ CheckpointGenerator - 체크포인트 생성
   │   └─→ ScenarioConditionManager.RegisterCondition() - 진행 조건 등록
   └─→ HandPose 없는 경우 (AutoPlay):
       ├─→ ChunaPathEvaluator.StartAutoPlayFromSubStep()
       └─→ GuideHandPlaybackController - 가이드 손 자동 재생

4. ScenarioConditionManager.ProcessConditionByType()
   ├─→ HandPose: 유사도 평가 완료 대기
   ├─→ Duration: duration 초 후 자동 진행
   ├─→ Narration: 음성 안내 완료 대기
   └─→ Manual: 토글로 수동 진행

5. SubStep 완료 → NextSubStep()
   ├─→ ScenarioEventSystem.SubStepCompleted() 이벤트
   └─→ TrainingResultTracker.RecordSubStepCompletion() - 결과 기록

6. 모든 SubStep 완료 → NextStep() → NextPhase() → CompleteScenario()
   ├─→ TrainingResultTracker.FinishTracking() - 최종 결과 저장
   ├─→ ShowResultPanel() - 결과 패널 표시
   └─→ ShowQuizPanel() - 퀴즈 패널 표시
```

### 15.2 ChunaPathEvaluator 평가 흐름

```
1. LoadAndGenerateCheckpoints(csvFileName)
   ├─→ HandPoseDataLoader.LoadFromResources() - CSV 파일 로드
   ├─→ CheckpointGenerator.Generate() - 프레임 간격으로 체크포인트 자동 생성
   └─→ 체크포인트 리스트 초기화

2. StartEvaluation()
   ├─→ currentPhase = EvaluationPhase.Idle
   └─→ 사용자 손 추적 시작

3. Update() → CollisionMode 감지
   ├─→ 사용자 손이 환자에 접촉 감지 (Head/HeadAndShoulder/Chest)
   └─→ WaitingForStart → StartHold(requiredHoldTime=3초) 진행

4. StartHold 완료
   ├─→ OnStartHoldComplete 이벤트 발생
   ├─→ phaseManager.StartMoving() - Moving 단계로 전환
   └─→ userHandFrameIndex 초기화

5. Moving 단계
   ├─→ 프레임 진행률 계산:
   │   ├─→ RelativeMovement: (사용자위치 - 시작홀드위치) / (데이터 길이)
   │   └─→ PivotBased: (사용자각도 - 시작각도) / 목표각도
   ├─→ 유사도 계산:
   │   ├─→ HandPoseComparator.Compare()
   │   ├─→ 왼손(30%) + 오른손(70%) 가중 평균
   │   └─→ OnSimilarityUpdated 이벤트
   ├─→ 체크포인트 통과 감지:
   │   ├─→ 현재 프레임 > 체크포인트 프레임
   │   └─→ OnCheckpointPassed 이벤트 발생
   └─→ 리밋 범위 체크:
       ├─→ ChunaLimitChecker.GetStatusFromRatio()
       ├─→ 프레임 비율 >= limitBarrierRatio → OnLimitWarning 이벤트
       └─→ TrainingResultTracker.HandleLimitWarning()

6. 중간 홀드 단계 (MidHold) - 30~50% 구간에서
   ├─→ holdVelocity < threshold 감지 (정지)
   ├─→ 지정된 홀드 시간 유지 완료
   ├─→ OnMidHoldComplete 이벤트 발생
   └─→ ChunaPathEvaluatorBridge.OnMidHoldCompleteHandler()
       └─→ ScenarioConditionManager.NextSubStep() 호출

7. 평가 완료 (전체 프레임 통과)
   ├─→ OnEvaluationCompleted 이벤트 발생
   └─→ EvaluationScoringEngine.CalculateScore()
       ├─→ 유사도 평균값
       ├─→ 경고 횟수
       └─→ 최종 점수 계산
```

### 15.3 조건 처리 흐름

```
OnSubStepStarted(SubStep)
├─→ conditionType = "HandPose"
│   ├─→ HasNarration = true
│   │   └─→ HandleNarrationThenHandPose()
│   │       ├─→ PlayNarration()
│   │       ├─→ 나레이션 완료 대기
│   │       └─→ 평가 시작 (유사도 체크)
│   └─→ HasNarration = false
│       └─→ 즉시 평가 시작
│
├─→ conditionType = "Duration"
│   └─→ duration 초 후 NextSubStep() 호출
│
├─→ conditionType = "Manual"
│   └─→ 토글 버튼으로 수동 진행 대기
│
└─→ conditionType = "Narration"
    ├─→ PlayNarration()
    └─→ 나레이션 완료 후 NextSubStep()
```

---

## 16. CSV 파일 포맷

### 16.1 시나리오 CSV (`Resources/Scenarios/`)

```csv
scenarioNo,scenarioName,phase,stepName,stepNo,subStepNo,duration,
textInstruction,voiceInstruction,handTrackingFileName,conditionType,
conditionParams,patientAnimationClip,movementType,videoStartTime,
videoEndTime,contactTarget,pivotTarget,pivotPlaneAxis,invertAngle
```

예시:
```csv
1,상부승모근,평가,가이드,0,1,0,,,,,,,,,,,
1,상부승모근,평가,세판상박회인,1,1,0,동작 설명,,세판상박회인,HandPose,,세판상박회인_가이드,position,,HeadAndShoulder,Neck,Z,false
```

### 16.2 핸드 포즈 CSV (`Resources/HandPoseData/`)

```csv
frameIndex,handType,jointId,posX,posY,posZ,rotX,rotY,rotZ,rotW,
timestamp,worldPosX,worldPosY,worldPosZ,worldRotX,worldRotY,worldRotZ,worldRotW
```

예시:
```csv
0,Left,0,0.0,0.0,0.0,0.0,0.0,0.0,1.0,0.0,0.0,0.0,0.0,0.0,0.0,0.0,1.0
0,Left,1,-0.023,0.01,0.052,0.0,0.0,0.0,1.0,0.0,0.0,0.0,0.0,0.0,0.0,1.0
```

---

## 17. 핵심 클래스 관계도

```
시나리오 흐름:
  AuthenticationService
        ↓
  AuthFlowManager
        ↓
  ScenarioManager ← CSV ← ScenarioCSVLoader
        ↓
  ScenarioEventSystem (이벤트 중심)
        ↓
  ScenarioConditionManager (진행 조건 관리)
        ↓
  ChunaPathEvaluator (평가 엔진) + ChunaPathEvaluatorBridge
        ↓
  결과 저장 ← TrainingResultTracker ← TrainingResultData

평가 세부:
  ChunaPathEvaluator
    ├─→ HandPoseComparator (유사도 계산)
    ├─→ HandPoseDataLoader (CSV 로드)
    ├─→ CheckpointGenerator (체크포인트 생성)
    ├─→ ChunaLimitChecker (범위 체크)
    ├─→ EvaluationPhaseManager (단계 관리)
    ├─→ EvaluationScoringEngine (점수 계산)
    ├─→ GuideHandPlaybackController (가이드 손)
    └─→ EvaluationModeConfigurator (모드 설정)
```
