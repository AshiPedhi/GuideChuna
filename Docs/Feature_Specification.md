# GuideChuna 기능 명세서

> **프로젝트명**: GuideChuna — VR 추나요법 의료 훈련 시뮬레이터
> **플랫폼**: Meta Quest (Android APK)
> **엔진**: Unity 6.0.2f1 / C# / Meta XR SDK 81.0.0
> **작성일**: 2026-03-13

---

## 목차

1. [프로그램 전체 흐름도](#1-프로그램-전체-흐름도)
2. [기능별 상세 플로우](#2-기능별-상세-플로우)
   - 2.1 [인증 구간 — 디바이스 인증 · 로그인 · 로그아웃](#21-인증-구간--디바이스-인증--로그인--로그아웃)
   - 2.2 [씬 전환 구간 — 로딩 · Camera X 보존](#22-씬-전환-구간--로딩--camera-x-보존)
   - 2.3 [모드/난이도 선택 구간](#23-모드난이도-선택-구간)
   - 2.4 [시나리오 로딩 & 진행 구간](#24-시나리오-로딩--진행-구간)
   - 2.5 [추나 평가 엔진 구간 — 실시간 손 동작 평가](#25-추나-평가-엔진-구간--실시간-손-동작-평가)
   - 2.6 [결과 집계 & 업로드 구간](#26-결과-집계--업로드-구간)
   - 2.7 [연습 모드(튜토리얼) 구간](#27-연습-모드튜토리얼-구간)
   - 2.8 [녹화 시스템 구간](#28-녹화-시스템-구간)
   - 2.9 [InfoPanel 페이지 전환 구간](#29-infopanel-페이지-전환-구간)
3. [모듈별 기능 명세](#3-모듈별-기능-명세)
   - 3.1 [인증 시스템 (Auth)](#31-인증-시스템-auth)
   - 3.2 [시나리오 관리 (Scenario)](#32-시나리오-관리-scenario)
   - 3.3 [추나 평가 엔진 (ChunaData)](#33-추나-평가-엔진-chunadata)
   - 3.4 [손 포즈 데이터 (PoseData)](#34-손-포즈-데이터-posedata)
   - 3.5 [환자 모델 관리 (Patient)](#35-환자-모델-관리-patient)
   - 3.6 [연습 모드 (Practice)](#36-연습-모드-practice)
   - 3.7 [훈련 결과 (Result)](#37-훈련-결과-result)
   - 3.8 [모드 선택 & 난이도 관리 (Training)](#38-모드-선택--난이도-관리-training)
   - 3.9 [UI 시스템 (UI)](#39-ui-시스템-ui)
   - 3.10 [녹화 시스템 (Recording)](#310-녹화-시스템-recording)
   - 3.11 [웹뷰 (WebView)](#311-웹뷰-webview)
   - 3.12 [유틸리티 (Utils)](#312-유틸리티-utils)
4. [API 엔드포인트 명세](#4-api-엔드포인트-명세)
5. [데이터 구조 명세](#5-데이터-구조-명세)
6. [현재 기능 현황 요약](#6-현재-기능-현황-요약)
7. [추가 필요 기능 분석](#7-추가-필요-기능-분석)
8. [확장 로드맵 제안](#8-확장-로드맵-제안)

---

## 1. 프로그램 전체 흐름도

### 1.1 메인 흐름 (Main Flow)

> **핵심 구조**: 로비 씬이 **중앙 허브** 역할을 하며, 시나리오 수행 후 로그인 상태를 유지한 채 로비로 복귀합니다.
> 사용자 변경은 "로그아웃 → 재로그인"으로만 가능합니다 (별도 사용자 전환 기능 없음).

```
[앱 실행]
   │
   ▼
┌─────────────────────────────────────────────────────────┐
│                 로비 씬 (lobby.unity)                    │
│                      ◆ 중앙 허브 ◆                      │
│                                                         │
│  ┌─ 1) 디바이스 인증 (Start → 자동 실행) ────────────┐  │
│  │   POST /device/auth/chuna (DeviceUUID 기반)        │  │
│  │   └─ 성공 시: orgID, 라이선스 정보 획득            │  │
│  │   └─ GET /user/userlist/{orgID} ← 목록 자동 로드   │  │
│  │   └─ 재진입 시: isFirstLaunch=false → 저장된        │  │
│  │      로그인 정보 복원 (PlayerPrefs)                 │  │
│  └────────────────────────────────────────────────────┘  │
│                                                         │
│  ┌─ 2) 사용자 아이콘 클릭 ───────────────────────────┐  │
│  │                                                    │  │
│  │   [미로그인 상태 — "Guest"]                        │  │
│  │   │  학년 선택 (GradeSelectionHandler)             │  │
│  │   │  └─ 사용자 선택 (UserSelectionHandler)         │  │
│  │   │     └─ POST /device/logon                      │  │
│  │   │        ├─ 로그인 상태 PlayerPrefs 저장         │  │
│  │   │        ├─ 미러링(Camera X) 활성화              │  │
│  │   │        └─ 시나리오 카드 활성화 (alpha 1.0)     │  │
│  │   │                                                │  │
│  │   [로그인 상태 — 사용자명 표시]                    │  │
│  │     └─ 로그아웃 확인 팝업 표시                     │  │
│  │        └─ 설문(SurveyPanel) → POST /device/logoff  │  │
│  │           └─ 상태 초기화 → "Guest" 복귀            │  │
│  │              └─ 다른 사용자로 재로그인 가능         │  │
│  └────────────────────────────────────────────────────┘  │
│                                                         │
│  ┌─ 3) 시나리오 카드 클릭 ───────────────────────────┐  │
│  │   미로그인 시: "로그인이 필요합니다" 팝업 → 차단   │  │
│  │   로그인 시:  PlayerPrefs에 시나리오 인덱스 저장    │  │
│  │              → SceneLoader("TrainingScene") 호출    │  │
│  └───────────────────────────────┬────────────────────┘  │
│                                  │                       │
│  ※ 앱 종료 시: OnApplicationQuit()에서 자동 로그오프    │
│    (3초 타임아웃, 실패해도 로컬 상태 클리어)            │
│                                                         │
└──────────────────────────────────┬──────────────────────┘
                                   │
                    ┌──────────────┴──────────────┐
                    ▼                              ▼
┌──────────────────────────────┐  ┌──────────────────────────┐
│  시나리오 훈련 모드           │  │  연습 모드 (Practice)    │
│  (TrainingScene.unity)       │  │  (Practice_Scene.unity)  │
│                              │  │                          │
│  ScenarioBootstrapper        │  │  PracticeManager         │
│  └─ PlayerPrefs에서          │  │  ├─ UI 조작 연습         │
│     시나리오 인덱스 로드      │  │  ├─ 설정 탐색 안내       │
│  └─ ScenarioConfig 주입      │  │  ├─ 손 제스처 연습       │
│                              │  │  └─ 진행도 추적          │
│  ┌────────────────────────┐  │  │                          │
│  │ ★ 모드/난이도 선택 ★   │  │  └────────────┬─────────────┘
│  │ (InfoPanelController)  │  │               │
│  │                        │  │               │
│  │ [모드 선택]            │  │               │
│  │  ○ 실습(Practice)      │  │               │
│  │    "안내에 따라 학습"   │  │               │
│  │  ○ 평가(Evaluation)    │  │               │
│  │    "학습 내용 테스트"   │  │               │
│  │                        │  │               │
│  │ [난이도 선택]          │  │               │
│  │  ○ 초급자(Beginner)    │  │               │
│  │    가이드 손 ON(0.8)   │  │               │
│  │    경로 표시 ON        │  │               │
│  │    상세 나레이션       │  │               │
│  │    유사도 임계값 0.5   │  │               │
│  │  ○ 중급자(Intermediate)│  │               │
│  │    가이드 손 ON(0.5)   │  │               │
│  │    경로 표시 OFF       │  │               │
│  │    간단 나레이션       │  │               │
│  │    유사도 임계값 0.65  │  │               │
│  │  ○ 상급자(Advanced)    │  │               │
│  │    가이드 손 OFF       │  │               │
│  │    모든 보조 OFF       │  │               │
│  │    사전평가 모드 ON    │  │               │
│  │    유사도 임계값 0.75  │  │               │
│  │                        │  │               │
│  │ → 모드 선택 시 자동    │  │               │
│  │   StartSimulation()    │  │               │
│  │   PlayerPrefs 저장     │  │               │
│  └────────────────────────┘  │               │
│                              │               │
│  CSV 시나리오 데이터 로드     │               │
│  CSV 손 포즈 데이터 로드      │               │
│                              │               │
│  [훈련 실행 루프]            │               │
│  DifficultyManager 프리셋    │               │
│  적용 상태로 진행             │               │
│  Phase(단계) 진행            │               │
│   └─ Step(스텝)              │               │
│      └─ SubStep(서브스텝)    │               │
│         ├─ 음성/텍스트       │               │
│         ├─ 가이드 표시       │               │
│         │  (난이도별 차등)   │               │
│         ├─ 손 동작 추적      │               │
│         ├─ 유사도 평가       │               │
│         ├─ 한계값 검출       │               │
│         └─ 피드백 UI         │               │
│            (난이도별 차등)    │               │
│                              │               │
│  [시나리오 완료]             │               │
│  ├─ 결과 집계                │               │
│  │  (유사도, 경고, 시간)     │               │
│  │  + 모드/난이도 정보 포함  │               │
│  ├─ 결과 UI 표시             │               │
│  ├─ POST /result/chuna       │               │
│  └─ 퀴즈 (선택)             │               │
│     POST /quiz/...           │               │
│                              │               │
│  [종료 경로]                 │               │
│  ├─ "메인으로" 버튼          │               │
│  │  (ExitPopupController)    │               │
│  └─ 메인 메뉴 토글           │               │
│     (InfoPanelController)    │               │
└────────────┬─────────────────┘               │
             │                                  │
             ▼                                  ▼
      SceneLoader("Lobby", useLoadingScene: true)
        ├─ Camera X를 DontDestroyOnLoad으로 보존
        ├─ LoadingScene 경유
        ├─ 로비 씬 재로드
        ├─ Camera X 복원 + 미러링 재연결
        └─ ★ 로그인 상태 유지된 채 로비 복귀 ★
             │
             ▼
      ┌──────────────────────────────────────┐
      │         로비 복귀 후 선택지           │
      │                                      │
      │  A) 다른 시나리오 선택 → 바로 진행   │
      │  B) 사용자 아이콘 → 로그아웃         │
      │     → 다른 사용자로 재로그인         │
      │  C) 앱 종료                          │
      └──────────────────────────────────────┘
```

### 1.2 로비 재진입 시 상태 복원 로직

| 조건 | 동작 | 관련 코드 |
|------|------|-----------|
| **최초 실행** (`isFirstLaunch=true`) | `ClearLoginInfo()` → Guest 상태로 시작 | `LobbyAuthUI_Complete.Start()` |
| **시나리오 후 복귀** (`isFirstLaunch=false`) | `LoadSavedLoginInfo()` → 로그인 상태 복원 | `LoginStateStore` |
| **강제 종료 후 재실행** | `isFirstLaunch=true` (static 초기값) → 로그인 정보 삭제 | 안전 복구 메커니즘 |

### 1.3 상태 저장 계층

| 저장소 | 생명주기 | 저장 항목 |
|--------|----------|-----------|
| **static 변수** (`isFirstLaunch`) | 앱 실행 중 유지, 재시작 시 초기화 | 최초 실행 여부 |
| **메모리 변수** (`currentUsername` 등) | 씬 전환 시 소멸 | 현재 로그인 사용자, orgID |
| **PlayerPrefs** | 앱 재시작 후에도 유지 | LOGIN_USERNAME, LOGIN_USERID, DEVICE_SN |

### 1.2 씬(Scene) 구성

| 씬 이름 | 역할 | 주요 매니저 |
|----------|------|-------------|
| `lobby.unity` | 메인 로비, 인증, 시나리오 선택 | AuthenticationService, LobbyAuthUI_Complete |
| `Scenario_1~5.unity` | 시나리오별 훈련 환경 | ScenarioManager, ChunaPathEvaluator |
| `Practice_Scene.unity` | 튜토리얼/연습 모드 | PracticeManager |
| `TrainingScene.unity` | 훈련 평가 전용 | ChunaPathEvaluator, TrainingResultTracker |
| `LoadingScene.unity` | 씬 전환 로딩 화면 | SceneLoader |

---

## 2. 기능별 상세 플로우

> 전체 흐름을 **기능 구간별로 분리**하여 각 구간의 내부 동작을 메서드 단위로 상세하게 정리합니다.

### 2.1 인증 구간 — 디바이스 인증 · 로그인 · 로그아웃

#### 2.1.1 디바이스 인증 플로우

> 앱 실행 또는 로비 재진입 시 자동 실행됩니다.

```
LobbyAuthUI_Complete.Start()
  │
  ├─ [최초 실행?] isFirstLaunch == true
  │    ├─ isFirstLaunch = false (이후 씬 재진입 시 false 유지)
  │    └─ loginStateStore.ClearLoginInfo() ← PlayerPrefs 클리어
  │
  ├─ [재진입?] isFirstLaunch == false
  │    └─ LoadSavedLoginInfo() ← PlayerPrefs에서 복원
  │         └─ currentUsername, currentUserID 세팅
  │
  ├─ LoadSavedDeviceSN() ← 저장된 디바이스 SN 로드
  │
  └─ AuthenticateDevice().Forget() [async 시작]
       │
       ▼
  AuthFlowManager.AuthenticateDeviceWithRetry(savedDeviceSN, maxRetries=2)
       │
       └─ AuthenticateDeviceInternal(savedDeviceSN, null, retryCount=0)
            │
            ├─ AuthenticationService.AuthenticateDeviceAsync(deviceSN=null)
            │    ├─ deviceSN이 null → SystemInfo.deviceUniqueIdentifier 사용
            │    ├─ POST /device/auth/chuna  { deviceSN, deviceUUID }
            │    ├─ ★ AuthEvents.TriggerAuthenticationStarted()
            │    └─ 응답: DeviceResponseData { orgID, mgtNo, licCHUNA }
            │
            ├─ [성공] ★ AuthEvents.TriggerAuthenticationSuccess()
            │    ├─ currentDeviceSN = deviceSN
            │    ├─ currentOrgID = orgID
            │    ├─ loginStateStore.SaveDeviceSN(deviceSN)
            │    │
            │    ├─ [라이선스 확인] licCHUNA <= 0 → ShowLicenseError() → 종료
            │    │
            │    └─ [사용자 목록 로드]
            │         ├─ GET /user/userlist/{orgID}
            │         ├─ ★ AuthEvents.TriggerUserListLoadCompleted()
            │         └─ authFlowManager.OrganizeByGrade(users)
            │              └─ usersByGrade = Dictionary<grade, List<UserData>>
            │
            ├─ [실패 — "등록된 장치입니다" 오류]
            │    └─ UUID 앞 10자리로 재시도 (retryCount 리셋)
            │
            └─ [실패 — 기타 오류]
                 ├─ retryCount < 2 → 1초 대기 후 재시도
                 └─ retryCount >= 2 → ShowAuthenticationError()
```

#### 2.1.2 사용자 로그인 플로우

> 사용자 아이콘 클릭 시 (미로그인 상태) 실행됩니다.

```
OnUserIconClicked() [사용자 아이콘 버튼]
  │
  ├─ [미로그인] currentUsername == empty
  │    │
  │    ▼
  │  gradeSelectionHandler.ShowPanel(usersByGrade)
  │    │
  │    ▼ 사용자: 학년 선택
  │  OnGradeSelected(grade)
  │    ├─ gradeSelectionHandler.Hide()
  │    └─ userSelectionHandler.ShowPanel(usersByGrade[grade])
  │         │
  │         ▼ 사용자: 사용자 선택
  │       OnUserSelected(userId, username)
  │         │
  │         ▼
  │       PerformLogin(userId, username) [async]
  │         │
  │         ├─ authFlowManager.PerformLogin(deviceSN, username)
  │         │    └─ AuthenticationService.LogonAsync(deviceSN, username, "VR_CHUNA")
  │         │         ├─ POST /device/logon
  │         │         │  { deviceSN, status:"LOGON", runUser, runContents,
  │         │         │    deviceInfo: "localIP/SN끝3자리" }
  │         │         ├─ ★ AuthEvents.TriggerLoginStarted()
  │         │         └─ 응답: MirroringData { serverIP, portNo, videoQuality }
  │         │
  │         ├─ [상태 저장]
  │         │    ├─ currentUsername = username
  │         │    ├─ currentUserID = userId
  │         │    └─ loginStateStore.SaveLoginInfo(username, userId)
  │         │
  │         ├─ [UI 업데이트]
  │         │    ├─ userNameText.text = username
  │         │    ├─ SetScenarioCardsVisualState(true) ← alpha 1.0
  │         │    └─ 안내 메시지: "시나리오를 선택하세요"
  │         │
  │         └─ [미러링 활성화]
  │              ├─ mirroringCameraObject.SetActive(true)
  │              └─ 0.1초 후 RenderManager.SetMirroringData()
  │
  └─ [이미 로그인됨] → 로그아웃 플로우로 분기 (2.1.3)
```

#### 2.1.3 사용자 로그아웃 플로우

> 사용자 아이콘 클릭 시 (로그인 상태) 실행됩니다.

```
OnUserIconClicked() [이미 로그인 상태]
  │
  ▼
popupHandler.ShowLogoutConfirmPopup()
  │
  ▼ 사용자: 확인 클릭
OnLogoutConfirm()
  │
  ├─ [설문 패널 존재?]
  │    ├─ Yes → surveyPanel.ShowSurveyPanel(callback: PerformLogout)
  │    │         └─ 설문 완료 후 콜백 → PerformLogout()
  │    └─ No → PerformLogout() 바로 실행
  │
  ▼
PerformLogoutAsync()
  │
  ├─ authFlowManager.PerformLogout(deviceSN, username)
  │    └─ AuthenticationService.LogoffAsync()
  │         ├─ POST /device/logoff
  │         │  { deviceSN, status:"LOGOFF", runUser, deviceInfo:"" }
  │         └─ ★ AuthEvents.TriggerLogoutCompleted()
  │
  ├─ ※ API 실패해도 무시 (catch → LogWarning)
  │
  ├─ DeactivateMirroring()
  │    ├─ RenderManager.StopMirroring()
  │    └─ mirroringCameraObject.SetActive(false)
  │
  └─ ClearUserInfo()
       ├─ currentUsername = ""
       ├─ currentUserID = 0
       ├─ loginStateStore.ClearLoginInfo() ← PlayerPrefs 삭제
       ├─ userNameText.text = "Guest"
       ├─ SetScenarioCardsVisualState(false) ← alpha 0.5
       └─ 안내 메시지: "로그인을 하세요"
```

#### 2.1.4 앱 종료 시 자동 로그아웃

```
OnApplicationQuit()
  │
  ├─ [로그인 상태?] currentUsername != empty
  │    ├─ authService.LogoffAsync() ← 동기 대기 (3초 타임아웃)
  │    ├─ 실패해도 무시 (catch → LogWarning)
  │    └─ loginStateStore.ClearLoginInfo()
  │
  └─ Application.Quit()
```

---

### 2.2 씬 전환 구간 — 로딩 · Camera X 보존

> 모든 씬 전환은 `SceneLoader`를 통해 이루어지며, Camera X(미러링 카메라)를 보존합니다.

```
SceneLoader.LoadScene(sceneName, useLoadingScene=true)
  │
  ▼
LoadSceneWithLoading(sceneName)
  │
  ├─ 1) PreserveCameraX()
  │      ├─ "Camera X" GameObject 검색
  │      ├─ parent를 SceneLoader로 이동 (DontDestroyOnLoad 영역)
  │      └─ preservedCameraX에 참조 저장
  │
  ├─ 2) LoadingScene 로드 (즉시)
  │      └─ 로딩 화면 표시
  │
  ├─ 3) 타겟 씬 비동기 로드
  │      ├─ allowSceneActivation = false
  │      ├─ progress 0.9f 될 때까지 대기
  │      ├─ Resources.UnloadUnusedAssets()
  │      ├─ GC.Collect()
  │      ├─ 1 프레임 대기
  │      └─ allowSceneActivation = true
  │
  ├─ 4) asyncLoad.isDone 대기
  │
  └─ 5) RestoreCameraX()
         ├─ parent = null (루트로 복원)
         ├─ 0.5초 후 RenderManager.TryReconnect()
         └─ preservedCameraX 참조 해제
```

#### 훈련 → 로비 복귀 경로 (2가지)

```
경로 A: ExitPopupController
  └─ "메인으로" 토글 → ExecuteMainMenu()
       └─ SceneLoader.LoadScene("Lobby", useLoadingScene: true)

경로 B: InfoPanelController
  └─ mainMenuToggle ON → 확인 팝업 → OnExitConfirm()
       └─ ReturnToLobby() → SceneLoader.LoadScene("Lobby")

경로 C: ExitPopupController — "다시하기"
  └─ ExecuteRetry() → SceneLoader.ReloadCurrentScene()
       └─ 현재 씬 재로드 (모드/난이도 재선택부터)
```

---

### 2.3 모드/난이도 선택 구간

> TrainingScene 로드 직후, 시나리오 시작 전에 실행됩니다.

```
[TrainingScene 로드]
  │
  ▼
ScenarioBootstrapper.Awake() [ExecutionOrder = -100]
  ├─ PlayerPrefs.GetInt(SelectedScenario) → selectedIndex
  ├─ scenarioConfigs[selectedIndex] → ScenarioConfig
  ├─ ScenarioManager.SetScenarioConfig(config)
  ├─ AnatomyMuscleController.ApplyScenario(config.scenarioName)
  ├─ ScenarioConditionManager.SetNarrationScenarioFolder(folder)
  └─ InfoPanelController.SetDefaultPositionPreset(preset)
  │
  ▼
ScenarioBootstrapper.Start()
  └─ ScenarioManager.ApplyAnimatorController()
       └─ patientAnimator.runtimeAnimatorController = config.animatorController
  │
  ▼
InfoPanelController.InitializePanel()
  └─ ShowContentPage(ContentPage.ModeSelection)
       └─ modeSelectionPage 활성화
  │
  ▼
InitializeModeSelection()
  ├─ 기본 난이도: Beginner (초급자)
  ├─ 기본 모드: None (미선택)
  └─ DifficultyManager.Instance.SetDifficulty(Beginner)
  │
  ▼
┌──────────────────────────────────────────────────┐
│  사용자 선택 UI                                   │
│                                                   │
│  [난이도 선택] (선택적 — 기본값 초급자)           │
│   ├─ ○ 초급자  "최초 학습자를 위한 레벨입니다"   │
│   ├─ ○ 중급자  "실습 경험자를 위한 레벨입니다"   │
│   └─ ○ 상급자  "숙련자를 위한 레벨입니다"        │
│        │                                          │
│        └─ OnDifficultyToggleChanged(level)        │
│             └─ DifficultyManager.SetDifficulty()  │
│                  └─ 프리셋 적용 + 이벤트 발생     │
│                                                   │
│  [모드 선택] (필수 — 선택 시 즉시 시작)           │
│   ├─ ○ 실습  "안내에 따라 과정 학습"             │
│   └─ ○ 평가  "학습 내용 테스트"                  │
│        │                                          │
│        └─ OnModeToggleChanged(mode) ★트리거★      │
│             ├─ selectedMode = mode                 │
│             ├─ OnModeSelected?.Invoke()            │
│             └─ StartSimulation() ← 자동 시작!     │
└──────────────────────────────────────────────────┘
  │
  ▼
StartSimulation()
  ├─ ScenarioManager.SetModeInfo(모드텍스트, 난이도텍스트)
  │    └─ selectedMode, selectedDifficulty 저장
  ├─ PlayerPrefs.SetString(SelectedMode, ...)
  ├─ PlayerPrefs.SetString(SelectedDifficulty, ...)
  └─ ScenarioManager.StartScenario()
       └─ [시나리오 로딩 & 진행 구간으로 이동 (2.4)]
```

---

### 2.4 시나리오 로딩 & 진행 구간

#### 2.4.1 시나리오 로딩

```
ScenarioManager.StartScenario()
  │
  ├─ LoadFromCSV()
  │    └─ ScenarioCSVLoader.LoadScenarios(csvFileName)
  │         ├─ Resources.Load<TextAsset>("Scenarios/{csvFileName}")
  │         └─ ParseCSV(csvText) → ScenarioCollection
  │              └─ ScenarioData { phases[ ] }
  │                   └─ PhaseData { steps[ ] }
  │                        └─ StepData { subSteps[ ] }
  │                             └─ SubStepData { 음성, 텍스트, CSV, 조건... }
  │
  ├─ 초기 인덱스 설정
  │    ├─ currentPhaseIndex = 0
  │    ├─ currentStepIndex = 0
  │    └─ currentSubStepIndex = 0
  │
  ├─ ResultTracker.StartTracking(selectedMode, selectedDifficulty)
  ├─ ResultTracker.StartPhase(currentPhase.phaseName)
  ├─ ResultTracker.StartStep(currentStep.stepName)
  │
  └─ ★ EventSystem.ScenarioStarted(currentScenario)
       └─ InfoPanelController → ShowContentPage(Skeleton)
```

#### 2.4.2 SubStep 실행 루프

```
★ EventSystem.SubStepStarted(subStep)
  │
  ▼
ScenarioManager.OnSubStepStartedForHandPose(subStep)
  │
  ├─ ApplyContactTarget(subStep)
  │    └─ ChunaPathEvaluator.SetContactTarget(Head/Shoulder/Chest)
  │
  ├─ UpdateAngleDisplayVisibility(subStep)
  │    └─ AngleDisplayController.ApplyPreset(handDataName)
  │
  ├─ [handTrackingFileName 존재?]
  │    │
  │    ├─ Yes → HandleCheckpointBasedTracking()
  │    │         ├─ ChunaPathEvaluator.SetPatientAnimationFromSubStep(subStep)
  │    │         ├─ ChunaPathEvaluator.SetExtendedLimitModeFromNames()
  │    │         │    └─ EvaluationModeConfigurator.SetFromNames(stepName, handFileName)
  │    │         │         ├─ "측굴" → 회전축 Z
  │    │         │         ├─ "회전" → 회전축 Y
  │    │         │         ├─ "환측" → 회전 방향 반전
  │    │         │         └─ "스트레칭"/"재평가" → 확장 한계 모드
  │    │         │
  │    │         ├─ ChunaPathEvaluatorBridge.LoadFromCSV(handTrackingFileName)
  │    │         │    └─ ChunaPathEvaluator.LoadAndGenerateCheckpoints()
  │    │         │         └─ ChunaPathEvaluator.StartEvaluation()
  │    │         │              └─ [평가 엔진 구간으로 이동 (2.5)]
  │    │         │
  │    │         └─ ScenarioConditionManager.RegisterCondition(
  │    │              phaseName, stepName, subStepNo,
  │    │              new CheckpointPoseCondition(bridge) )
  │    │
  │    └─ No (patientAnimation만 존재)
  │         └─ HandleAutoPlayAnimation(subStep)
  │              └─ ChunaPathEvaluator.StartAutoPlayFromSubStep()
  │                   └─ 완료 시 → EventSystem.OnAutoPlayCompleted
  │
  ▼
ScenarioConditionManager.OnSubStepStarted(subStep)
  └─ ProcessConditionByType(subStep)
       │
       ├─ [HandPose 조건]
       │    ├─ [나레이션 있음?] → 나레이션 재생 → 완료 후 StartConditionCheck()
       │    └─ [나레이션 없음] → StartConditionCheck() 바로 시작
       │         └─ CheckConditionLoop() [0.5초마다 반복]
       │              ├─ currentCondition.IsConditionMet()? → OnConditionCompleted()
       │              └─ 20초 타임아웃 → 수동 진행 버튼 활성화
       │
       ├─ [Duration 조건]
       │    └─ AutoProgressWithoutAlert(duration) → 지정 시간 후 자동 진행
       │
       ├─ [Manual 조건]
       │    └─ 수동 진행 버튼 활성화 → 사용자 클릭 대기
       │
       └─ [Narration 조건]
            └─ 나레이션 재생 → 완료 시 자동 진행
```

#### 2.4.3 단계 진행 체인

```
조건 충족!
  │
  ▼
OnConditionCompleted()
  ├─ 2초 대기 (completionDelay)
  └─ ScenarioManager.NextSubStep()
       │
       ├─ [다음 SubStep 있음?]
       │    ├─ currentSubStepIndex++
       │    └─ ★ EventSystem.SubStepStarted(newSubStep)
       │         └─ [2.4.2 루프 반복]
       │
       └─ [SubStep 모두 완료]
            └─ NextStep()
                 │
                 ├─ [다음 Step 있음?]
                 │    ├─ currentStepIndex++
                 │    ├─ ResultTracker.StartStep(newStep.stepName)
                 │    ├─ ★ EventSystem.StepChanged(newStep)
                 │    └─ ★ EventSystem.SubStepStarted(firstSubStep)
                 │
                 └─ [Step 모두 완료]
                      └─ NextPhase()
                           │
                           ├─ [다음 Phase 있음?]
                           │    ├─ currentPhaseIndex++
                           │    ├─ ResultTracker.StartPhase(newPhase)
                           │    ├─ ★ EventSystem.PhaseChanged(newPhase)
                           │    └─ ★ EventSystem.SubStepStarted(firstSubStep)
                           │
                           └─ [Phase 모두 완료]
                                └─ CompleteScenario()
                                     └─ [결과 집계 구간 (2.6)]
```

---

### 2.5 추나 평가 엔진 구간 — 실시간 손 동작 평가

> `ChunaPathEvaluator`가 매 프레임 사용자의 손 동작을 기록된 동작과 비교합니다.

#### 2.5.1 평가 시작

```
ChunaPathEvaluator.StartEvaluation()
  ├─ isEvaluating = true
  └─ Update() 루프 시작 [매 프레임]
```

#### 2.5.2 프레임별 평가 루프

```
Update() [매 프레임]
  │
  ├─ [접촉 감지] 손이 환자에 접촉?
  │    │
  │    ├─ No → 대기 (평가 시작 안 됨)
  │    │
  │    └─ Yes → trainingStarted = true
  │         │
  │         ▼
  │       GetCurrentUserHandFrame()
  │         │
  │         ▼
  │       HandPoseComparator.Compare(userHand, guideFrame[frameIndex])
  │         │
  │         ├─ [손목 비교] (가중치 70%)
  │         │    ├─ 위치 오차 = Distance(userWrist, guideWrist)
  │         │    │    └─ 임계값: 0.08m (8cm)
  │         │    └─ 회전 오차 = Angle(userRot, guideRot)
  │         │         └─ 임계값: 25°
  │         │
  │         ├─ [관절 비교] (가중치 30%)
  │         │    └─ 각 관절(0~26)의 localPosition, localRotation 비교
  │         │
  │         └─ 유사도 = (손목유사도 × 0.7) + (관절유사도 × 0.3)
  │              └─ 범위: 0.0 ~ 1.0
  │
  ├─ [0.1초마다 메트릭 기록]
  │    ├─ ★ OnUserFrameChanged(currentFrame, totalFrames, ratio)
  │    │    └─ TrainingResultTracker.UpdateApproachRatio(ratio)
  │    │
  │    └─ ★ OnSimilarityUpdated(leftSim, rightSim)
  │         └─ TrainingResultTracker.HandleSimilarityUpdated()
  │              ├─ accumulatedSimilarity += (left + right) / 2
  │              └─ similaritySampleCount++
  │
  ├─ [체크포인트 통과?]
  │    └─ userFrameIndex >= checkpoint.frameIndex
  │         └─ ★ OnCheckpointPassed(checkpoint, similarity)
  │
  ├─ [한계값 검출] ChunaLimitChecker.UpdateLimitStatus()
  │    │
  │    ├─ ratio < 0.3  → Safe ✅
  │    ├─ ratio < 0.5  → Warning ⚠️
  │    ├─ ratio >= 0.5 → Danger 🔴
  │    └─ ratio >= 1.0 → Exceeded ⛔
  │         └─ ★ OnLimitWarning(ratio)
  │              └─ TrainingResultTracker.HandleLimitWarning()
  │                   └─ warningCount++
  │
  ├─ [홀드 구간 도달?] userFrameIndex >= holdStartFrame
  │    └─ CheckHoldCompletion()
  │         ├─ 속도 < holdVelocityThreshold
  │         ├─ 홀드 타이머 >= requiredHoldTime (1.5~2.0초)
  │         └─ ★ OnStartHoldComplete()
  │              └─ TrainingResultTracker.PlayHoldCompleteSound() [딩동]
  │
  └─ [모든 체크포인트 통과 또는 타임아웃]
       └─ ★ OnEvaluationCompleted(EvaluationSession)
            └─ ChunaPathEvaluatorBridge.OnEvaluationCompletedHandler()
                 └─ ★ OnSequenceCompleted()
                      └─ CheckpointPoseCondition.isCompleted = true
                           └─ [조건 충족 → 2.4.3 단계 진행]
```

#### 2.5.3 평가 모드 설정 (EvaluationModeConfigurator)

```
SetFromNames(stepName, handDataName) [SubStep 시작 시]
  │
  ├─ stepName 파싱:
  │    ├─ "재평가" → isReEvaluation
  │    ├─ "스트레칭" → isStretching
  │    └─ "가이드" → isGuideMode
  │
  ├─ handDataName 파싱:
  │    ├─ "환측" → 회전 방향 반전 (환측)
  │    ├─ "건측" → 회전 방향 정방향 (건측)
  │    ├─ "측굴" → 회전 감지축 = Z
  │    └─ "회전" → 회전 감지축 = Y
  │
  └─ 모드 조합:
       ├─ 회전 → 기본 모드 (한계 범위 0.3~0.5)
       ├─ 측굴 + 스트레칭 → 확장 한계 모드 + 스트레칭 범위
       ├─ 측굴 + 재평가 → 확장 한계 모드 + 재평가 범위
       └─ 그 외 → 기본 모드
```

---

### 2.6 결과 집계 & 업로드 구간

#### 2.6.1 실시간 결과 누적

```
[시나리오 진행 중 — 이벤트 기반 누적]

TrainingResultTracker 이벤트 구독:
  │
  ├─ OnSimilarityUpdated → accumulatedSimilarity 누적
  ├─ OnLimitWarning → warningCount 증가
  ├─ OnUserFrameChanged → 프레임 진행률 업데이트
  └─ OnStartHoldComplete → 홀드 완료 사운드
```

#### 2.6.2 시나리오 완료 & 결과 표시

```
CompleteScenario()
  │
  ├─ TrainingResultTracker.FinishTracking()
  │    ├─ 마지막 SubStep 완료 처리
  │    ├─ resultData.FinalizeResult()
  │    │    ├─ overallSimilarity = 전체 Step 평균 유사도
  │    │    ├─ totalTime = 전체 Phase/Step 소요 시간 합산
  │    │    ├─ totalWarningCount = 전체 경고 합산
  │    │    └─ overallGrade 산출 (A/B/C 등급)
  │    │
  │    └─ ★ OnTrainingCompleted(resultData)
  │
  ├─ isScenarioCompleted = true
  │
  ├─ ★ EventSystem.ScenarioCompleted(scenario)
  │    └─ InfoPanelController.OnScenarioCompleted()
  │         ├─ ShowContentPage(ContentPage.Result)
  │         └─ DynamicResultTableUI.ForceRefresh()
  │
  ├─ ShowResultPanel()
  │    └─ 결과 테이블 표시:
  │         ├─ 모드 / 난이도
  │         ├─ 전체 유사도 / 소요 시간
  │         ├─ 전체 경고 / 스킵 횟수
  │         └─ Phase별 → Step별 상세 결과
  │
  └─ ShowQuizPanel()
       └─ QuizPanel.ShowQuizPanel()
            └─ POST /quiz/{contentType} → 퀴즈 데이터 로드

TrainingResultData 최종 구조:
  ├─ selectedMode: "실습" / "평가"
  ├─ selectedDifficulty: "초급자" / "중급자" / "상급자"
  ├─ totalTime (초)
  ├─ overallSimilarity (0~1)
  ├─ totalWarningCount
  ├─ totalSkipCount
  └─ phaseResults[]
       └─ stepResults[]
            ├─ stepName
            ├─ completed / skipped
            ├─ avgSimilarity
            ├─ warningCount
            └─ totalTime
```

#### 2.6.3 결과 서버 업로드

```
[로비 복귀 시 또는 시나리오 완료 시]
  └─ AuthenticationService.PostResultAsync()
       └─ POST /result/chuna
            ├─ ResultData { 사용자, 시나리오, 모드, 결과 메트릭 }
            └─ 응답: String (성공/실패)
```

---

### 2.7 연습 모드(튜토리얼) 구간

> Practice_Scene에서 실행되며, 7단계 순차적 튜토리얼을 제공합니다.

```
PracticeManager.Initialize()
  ├─ 충돌 컴포넌트 비활성화 (ScenarioManager, ResultTracker 등)
  ├─ 초기 UI 상태 저장
  ├─ 모든 토글 비활성화 (주메뉴/난이도 제외)
  └─ StartStep(0)
  │
  ▼

┌─ STEP 1: UI 옮기기 (3회) ────────────────────────────┐
│  "UI를 손으로 잡아서 3번 옮겨보세요"                    │
│  ├─ UIGrabDetector.OnUIGrabReleased() 이벤트 감지      │
│  ├─ currentCount++ → UpdateCountText(count, 3)         │
│  └─ count >= 3 → CompleteCurrentStep()                 │
└───────────────────────────────────────────────────────┘
  ▼
┌─ STEP 2: 난이도 토글(3개) → 실습모드 선택 ───────────┐
│  "난이도를 하나씩 눌러보세요 → 실습 모드를 선택하세요"  │
│  ├─ 초급자/중급자/상급자 토글 하이라이트 → 순서대로 클릭│
│  ├─ 3개 완료 → 실습 모드 토글 하이라이트               │
│  └─ 실습 모드 클릭 → Skeleton 페이지로 전환            │
└───────────────────────────────────────────────────────┘
  ▼
┌─ STEP 3: 콘텐츠 토글 (비디오→결과→골격) ─────────────┐
│  "비디오, 결과, 골격 토글을 순서대로 눌러보세요"        │
│  ├─ expertVideoToggle 하이라이트 → 클릭               │
│  ├─ resultToggle 하이라이트 → 클릭                     │
│  └─ skeletonToggle 하이라이트 → 클릭                   │
└───────────────────────────────────────────────────────┘
  ▼
┌─ STEP 4: 설정 토글 (설정→자동조절→환자위치) ─────────┐
│  "설정 관련 토글을 순서대로 눌러보세요"                  │
│  ├─ settingsToggle → 클릭 (설정 패널 열기)             │
│  ├─ autoAdjustToggle → 클릭                            │
│  └─ patientPositionToggle → 클릭                       │
└───────────────────────────────────────────────────────┘
  ▼
┌─ STEP 5: 환자 위치 조정 → 설정 닫기 → 시작 ─────────┐
│  ├─ 환자 위치(Transform) 변경 대기 (30초 타임아웃)     │
│  ├─ settingsToggle OFF (설정 닫기)                     │
│  └─ startToggle ON (시작 버튼 클릭)                    │
└───────────────────────────────────────────────────────┘
  ▼
┌─ STEP 6: 핸드 가이드 홀드 연습 (3 사이클) ───────────┐
│  "가이드 손을 따라 동작하고 홀드하세요"                  │
│  ├─ ChunaPathEvaluator 활성화                          │
│  ├─ 평가용 CSV 로드 ("중부측굴_trimmed")               │
│  ├─ AngleDisplayController 표시                        │
│  │                                                     │
│  │  [사이클 루프 × 3회]                                │
│  │  ├─ StartEvaluation() → 사용자 동작 수행           │
│  │  ├─ EvaluationPhase.Completed 이벤트 수신           │
│  │  ├─ currentCount++ → UpdateCountText(count, 3)     │
│  │  ├─ count < 3 → 0.5초 대기 → ResetEvaluation()    │
│  │  └─ count >= 3 → CompleteCurrentStep()             │
│  │                                                     │
│  └─ AngleDisplayController 숨김                        │
└───────────────────────────────────────────────────────┘
  ▼
┌─ STEP 7: 나가기 ─────────────────────────────────────┐
│  "메인 메뉴 버튼을 눌러 종료하세요"                     │
│  ├─ mainMenuToggle 하이라이트                          │
│  └─ 클릭 → ExitPopupController 종료 팝업              │
│       ├─ "취소" → 훈련 계속                            │
│       ├─ "다시하기" → 현재 씬 재로드                   │
│       └─ "메인으로" → 로비 씬으로 이동                 │
└───────────────────────────────────────────────────────┘
```

---

### 2.8 녹화 시스템 구간

> 듀얼 카메라로 훈련 과정을 프레임 단위로 녹화합니다.

```
[시나리오 시작 이벤트]
  │
  ▼
DualCameraRecorder.OnScenarioStarted(scenarioData)
  └─ StartRecording()
       ├─ 세션 폴더 생성: {basePath}/Recordings/{시나리오}_{모드}_{yyyyMMdd_HHmmss}/
       ├─ RenderTexture 초기화 (recordWidth × recordHeight, ARGB32)
       ├─ recordCamera1.enabled = true
       ├─ recordCamera2.enabled = true
       └─ 녹화 시작 (isRecording = true)
  │
  ▼
[Update 루프 — 녹화 중]
  │
  ├─ captureInterval (1/recordFPS) 마다:
  │    └─ CaptureFrame()
  │         ├─ Camera1: AsyncGPUReadback.Request(rt1)
  │         │    └─ 콜백 → EncodeAndSaveFrame()
  │         │         ├─ Texture2D 생성 → JPG/PNG 인코딩
  │         │         └─ saveQueue에 Enqueue
  │         │
  │         └─ Camera2: 동일 처리
  │
  ├─ [백그라운드 스레드] SaveThreadLoop()
  │    └─ saveQueue.Dequeue() → File.WriteAllBytes()
  │         └─ Camera{N}/frame_{000000}.jpg
  │
  └─ maxRecordDuration 초과 → StopRecording()
  │
  ▼
[시나리오 완료 또는 씬 언로드]
  └─ StopRecording()
       ├─ isRecording = false
       ├─ WaitForSaveComplete() ← 큐 비워질 때까지 대기
       ├─ Camera1/Camera2 비활성화
       └─ ★ OnRecordingStopped(sessionPath)

최종 파일 구조:
  {basePath}/Recordings/
    └─ {시나리오}_{모드}_{날짜시간}/
        ├─ Camera1/
        │   ├─ frame_000000.jpg
        │   ├─ frame_000001.jpg
        │   └─ ...
        └─ Camera2/
            ├─ frame_000000.jpg
            └─ ...
```

---

### 2.9 InfoPanel 페이지 전환 구간

> TrainingScene 내 InfoPanelController가 관리하는 콘텐츠 페이지 전환 흐름입니다.

```
┌─────────────────────────────────────────────────────────┐
│  InfoPanelController 페이지 상태 머신                     │
│                                                          │
│  ┌─────────────┐                                         │
│  │ ModeSelection│ ← 초기 상태                            │
│  │ (모드/난이도) │                                        │
│  └──────┬───────┘                                        │
│         │ 모드 선택 시                                    │
│         ▼                                                │
│  ┌─────────────┐     ┌──────────────┐    ┌───────────┐  │
│  │  Skeleton    │ ◄─► │ ExpertVideo  │ ◄─►│  Result   │  │
│  │ (근골격 표시)│     │ (전문가 영상)│    │ (수행결과)│  │
│  └──────┬───────┘     └──────────────┘    └─────┬─────┘  │
│         │                                       │        │
│         │ ← 사용자 토글로 자유 전환 가능 →      │        │
│         │                                       │        │
│         └──────────── 시나리오 진행 중 ──────────┘        │
│                            │                             │
│                            │ 시나리오 완료 시             │
│                            ▼                             │
│                     ┌───────────┐                        │
│                     │  Result   │ ← 자동 전환 (최종)     │
│                     │ (최종결과)│                        │
│                     └───────────┘                        │
│                                                          │
│  [메뉴 토글 그룹] (별도)                                 │
│  ├─ settingsToggle → 설정 팝업 열기/닫기                │
│  └─ mainMenuToggle → 종료 확인 팝업                     │
│       ├─ 확인 → ReturnToLobby()                         │
│       └─ 취소 → 팝업 닫기                               │
└─────────────────────────────────────────────────────────┘

페이지별 콘텐츠:
  ├─ Skeleton: 근골격 RenderTexture (해부학 시각화)
  ├─ ExpertVideo: 전문가 시범 영상 (CSV의 videoStart/EndTime 구간)
  └─ Result: DynamicResultTableUI (Phase/Step별 점수표)
```

---

## 3. 모듈별 기능 명세

### 3.1 인증 시스템 (Auth)

**디렉토리**: `Assets/Scripts/ClaudeScripts/Auth/`

#### 기능 목록

| ID | 기능명 | 설명 | 상태 |
|----|--------|------|------|
| AUTH-01 | 디바이스 인증 | DeviceUUID 기반 자동 디바이스 인증 | ✅ 완료 |
| AUTH-02 | 사용자 목록 조회 | 소속 기관(orgID)의 사용자 목록 조회 | ✅ 완료 |
| AUTH-03 | 학년 필터링 | 학년(grade)별 사용자 필터링 | ✅ 완료 |
| AUTH-04 | 로그온/로그오프 | 세션 기반 사용자 로그인/로그아웃 | ✅ 완료 |
| AUTH-05 | 인증 재시도 로직 | 실패 시 자동 재시도 (AuthFlowManager) | ✅ 완료 |
| AUTH-06 | 디바이스 상태 업데이트 | 실행 상태 서버 보고 | ✅ 완료 |
| AUTH-07 | Mock 인증 서비스 | 테스트용 Mock 서비스 (MockAuthenticationService) | ✅ 완료 |
| AUTH-08 | 로그인 상태 로컬 저장 | LoginStateStore로 PlayerPrefs 저장 | ✅ 완료 |

#### 핵심 클래스

```
AuthenticationService (MonoBehaviour, Singleton)
  ├── IAuthenticationService (인터페이스)
  ├── AuthFlowManager (인증 흐름 오케스트레이션)
  ├── LobbyAuthUI_Complete (인증 UI)
  ├── UserSelectionHandler (사용자 선택 팝업)
  ├── GradeSelectionHandler (학년 필터)
  ├── LoginStateStore (로컬 상태 저장)
  └── AuthEvents (이벤트 시스템)
```

#### 이벤트

- `LoginStarted` — 로그인 프로세스 시작
- `LoginSuccess` — 로그인 성공
- `AuthenticationFailed` — 인증 실패
- `LogoffCompleted` — 로그오프 완료

---

### 3.2 시나리오 관리 (Scenario)

**디렉토리**: `Assets/Scripts/ClaudeScripts/Scenario/`

#### 기능 목록

| ID | 기능명 | 설명 | 상태 |
|----|--------|------|------|
| SCN-01 | CSV 시나리오 로딩 | Resources/Scenarios/*.csv 파일 파싱 | ✅ 완료 |
| SCN-02 | Phase/Step/SubStep 계층 구조 | 3단계 계층적 시나리오 구성 | ✅ 완료 |
| SCN-03 | 시나리오 진행 제어 | 자동/수동 단계 전환 | ✅ 완료 |
| SCN-04 | 조건 기반 전환 | 다양한 전환 조건 지원 | ✅ 완료 |
| SCN-05 | 이벤트 브로드캐스팅 | ScenarioEventSystem 통한 이벤트 전파 | ✅ 완료 |
| SCN-06 | 애니메이터 컨트롤러 전환 | 시나리오별 애니메이션 컨트롤러 교체 | ✅ 완료 |
| SCN-07 | ScenarioConfig 설정 | ScriptableObject 기반 시나리오 설정 | ✅ 완료 |

#### 조건 타입 (ConditionType)

| 조건 | 설명 |
|------|------|
| `None` | 즉시 진행 |
| `HandPose` | 사용자 손 포즈가 기준과 일치할 때 |
| `PatientAnimation` | 환자 애니메이션 재생 완료 시 |
| `Narration` | 나레이션 재생 완료 시 |
| `Duration` | 지정 시간 경과 시 |
| `Manual` | 사용자 수동 진행 |

#### 핵심 클래스

```
ScenarioManager (Singleton)
  ├── ScenarioCSVLoader (CSV 파싱)
  ├── ScenarioConfig (ScriptableObject)
  ├── ScenarioEventSystem (이벤트 허브)
  ├── ScenarioConditionManager (조건 관리)
  ├── CheckpointPoseCondition (포즈 조건)
  └── ScenarioConditionSetup (조건 설정)
```

#### 데이터 구조 계층

```
ScenarioData
  └── List<PhaseData>         ← 전부/중부/후부
        └── List<StepData>     ← 개별 수기 단계
              └── List<SubStepData>  ← 세부 동작
                    ├── voiceInstruction (음성 안내)
                    ├── textInstruction (텍스트 안내)
                    ├── handTrackingFileName (손 동작 CSV)
                    ├── contactTarget (접촉 부위)
                    ├── pivotTarget (회전축)
                    └── conditionType (전환 조건)
```

---

### 3.3 추나 평가 엔진 (ChunaData)

**디렉토리**: `Assets/Scripts/ClaudeScripts/ChunaData/`

#### 기능 목록

| ID | 기능명 | 설명 | 상태 |
|----|--------|------|------|
| CHN-01 | 체크포인트 기반 경로 평가 | 기록된 손 동작과 실시간 비교 | ✅ 완료 |
| CHN-02 | 유사도 점수 산출 | 쿼터니언 기반 회전 비교 (0~100%) | ✅ 완료 |
| CHN-03 | 한계값 검증 | 경추/손목 회전 한계 초과 검출 | ✅ 완료 |
| CHN-04 | 접촉 감지 | 손-환자 접촉 감지 (Head/Shoulder/Chest) | ✅ 완료 |
| CHN-05 | 회전 감지 | X/Y/Z축 회전 각도 실시간 추적 | ✅ 완료 |
| CHN-06 | 자동 재생 모드 | 기록된 동작 자동 재생 (가이드) | ✅ 완료 |
| CHN-07 | 가이드 모드 | 투명 가이드 손 표시 | ✅ 완료 |
| CHN-08 | 실시간 피드백 | 유사도/경고/위반 실시간 UI | ✅ 완료 |
| CHN-09 | 위반 등급 분류 | minor/moderate/severe/dangerous 4단계 | ✅ 완료 |
| CHN-10 | 머리 찌르기 감지 | 손가락으로 환자 머리 찌르기 감지 | ✅ 완료 |
| CHN-11 | 측면 굴곡 감지 | 경추 측면 굴곡 감지 | ✅ 완료 |

#### 핵심 클래스

```
ChunaPathEvaluator (핵심 평가 엔진)
  ├── ChunaPathEvaluatorBridge (CSV ↔ 평가기 연결)
  ├── ChunaLimitChecker (한계값 검증)
  ├── HandPoseComparator (포즈 유사도 비교)
  ├── CheckpointGenerator (체크포인트 생성)
  ├── ChunaLimitData (ScriptableObject — 한계값 정의)
  ├── PatientHeadPokeDetector (찌르기 감지)
  ├── PatientHeadTouchDetector (접촉 감지)
  ├── HandCollisionDetector (충돌 감지)
  ├── LateralFlexionDetector (측면 굴곡 감지)
  └── ChunaFeedbackUI (실시간 피드백 UI)
```

#### 한계값 파라미터 (ChunaLimitData)

| 파라미터 | 설명 |
|----------|------|
| `maxNeckFlexion / Extension` | 경추 굴곡/신전 최대 각도 |
| `maxNeckRotationLeft / Right` | 경추 좌/우 회전 최대 각도 |
| `maxNeckLateralFlexionLeft / Right` | 경추 좌/우 측면 굴곡 최대 각도 |
| `maxWristFlexion / Extension` | 손목 굴곡/신전 최대 각도 |
| `violationDeduction (minor~dangerous)` | 위반 등급별 감점 |
| `warningThreshold` | 경고 임계값 |

---

### 3.4 손 포즈 데이터 (PoseData)

**디렉토리**: `Assets/Scripts/ClaudeScripts/PoseData/`

#### 기능 목록

| ID | 기능명 | 설명 | 상태 |
|----|--------|------|------|
| PSE-01 | 손 포즈 녹화 | 실시간 손 동작 CSV 녹화 | ✅ 완료 |
| PSE-02 | 손 포즈 로딩 | CSV 파일에서 프레임별 로딩 | ✅ 완료 |
| PSE-03 | 손 포즈 재생 | 녹화된 동작 시각적 재생 | ✅ 완료 |
| PSE-04 | 스켈레톤 매핑 | 손 스켈레톤 → 아바타 스켈레톤 매핑 | ✅ 완료 |
| PSE-05 | 프레임 메타데이터 | 타임스탬프, 프레임 번호 관리 | ✅ 완료 |

#### 핵심 클래스

```
HandPoseRecorder → HandDataRecorder → CSV 파일 생성
HandPoseDataLoader → CSV 파일 → HandTransformData 리스트
PosePlayer → HandTransformData → 시각적 재생
HandTransformMapper → 손 스켈레톤 ↔ 아바타 매핑
TunaMotionData → 프레임 데이터 구조
```

---

### 3.5 환자 모델 관리 (Patient)

**디렉토리**: `Assets/Scripts/ClaudeScripts/Patient/`

#### 기능 목록

| ID | 기능명 | 설명 | 상태 |
|----|--------|------|------|
| PAT-01 | 환자 위치 프리셋 | 앉기/눕기 등 다양한 자세 | ✅ 완료 |
| PAT-02 | 해부학적 근육 시각화 | 대상 근육 하이라이트 표시 | ✅ 완료 |
| PAT-03 | 근육 그룹 제어 | 흉쇄유돌근, 상부승모근, 사각근 등 | ✅ 완료 |

---

### 3.6 연습 모드 (Practice)

**디렉토리**: `Assets/Scripts/ClaudeScripts/Practice/`

#### 기능 목록

| ID | 기능명 | 설명 | 상태 |
|----|--------|------|------|
| PRC-01 | 단계별 튜토리얼 | 순차적 안내 기반 학습 | ✅ 완료 |
| PRC-02 | UI 그랩 연습 | VR UI 그랩 & 이동 연습 | ✅ 완료 |
| PRC-03 | 설정 탐색 안내 | 설정 메뉴 탐색 가이드 | ✅ 완료 |
| PRC-04 | 손 제스처 연습 | 기본 손 제스처 훈련 | ✅ 완료 |
| PRC-05 | 하이라이트 가이드 | 토글/버튼 하이라이트 표시 | ✅ 완료 |
| PRC-06 | 진행도 추적 | 연습 단계별 완료 상태 관리 | ✅ 완료 |

---

### 3.7 훈련 결과 (Result)

**디렉토리**: `Assets/Scripts/ClaudeScripts/Result/`

#### 기능 목록

| ID | 기능명 | 설명 | 상태 |
|----|--------|------|------|
| RST-01 | 다단계 결과 추적 | Phase/Step/SubStep별 메트릭 기록 | ✅ 완료 |
| RST-02 | 유사도 점수 집계 | 평균 유사도 점수 산출 | ✅ 완료 |
| RST-03 | 경고/위반 집계 | 경고 횟수, 위반 유형별 집계 | ✅ 완료 |
| RST-04 | 수행 시간 기록 | 각 단계별 소요 시간 기록 | ✅ 완료 |
| RST-05 | 서버 결과 업로드 | POST /result/chuna 서버 전송 | ✅ 완료 |
| RST-06 | 결과 테이블 UI | ChunaResultTableUI 표 형태 표시 | ✅ 완료 |

#### 결과 데이터 구조

```
TrainingResultData
  └── List<PhaseResult>
        ├── phaseName
        ├── averageSimilarity (0~100%)
        └── List<StepResult>
              ├── stepName
              ├── averageSimilarity
              ├── completionStatus (Complete/Partial/Skipped)
              ├── warningCount
              ├── skippedCount
              └── totalTime
```

---

### 3.8 모드 선택 & 난이도 관리 (Training)

**디렉토리**: `Assets/Scripts/ClaudeScripts/Training/`
**UI 담당**: `Assets/Scripts/ClaudeScripts/UI/InfoPanelController.cs`

#### 기능 목록

| ID | 기능명 | 설명 | 상태 |
|----|--------|------|------|
| TRN-01 | 모드 선택 UI | 실습(Practice) / 평가(Evaluation) 토글 선택 | ✅ 완료 |
| TRN-02 | 난이도 선택 UI | 초급자 / 중급자 / 상급자 토글 선택 | ✅ 완료 |
| TRN-03 | 난이도 프리셋 관리 | DifficultySettings — 난이도별 가이드/UI/임계값 프리셋 | ✅ 완료 |
| TRN-04 | 난이도 런타임 적용 | DifficultyManager (Singleton) — 프리셋을 런타임에 적용 | ✅ 완료 |
| TRN-05 | 모드/난이도 PlayerPrefs 저장 | SelectedMode, SelectedDifficulty 저장 | ✅ 완료 |
| TRN-06 | 모드 선택 시 자동 시작 | OnModeToggleChanged → StartSimulation() 즉시 호출 | ✅ 완료 |
| TRN-07 | 결과에 모드/난이도 포함 | ScenarioManager.SetModeInfo() → ResultTracker에 전달 | ✅ 완료 |

#### 모드 타입 (ModeType)

| 모드 | 설명 | UI 라벨 |
|------|------|---------|
| `Practice` | 실습 모드 — 안내 가이드 제공 | "안내에 따라 과정 학습" |
| `Evaluation` | 평가 모드 — 학습 성과 테스트 | "학습 내용 테스트" |

#### 난이도 프리셋 비교표

| 항목 | 초급자 (Beginner) | 중급자 (Intermediate) | 상급자 (Advanced) |
|------|-------------------|----------------------|-------------------|
| **가이드 손 표시** | ✅ ON (투명도 0.8) | ✅ ON (투명도 0.5) | ❌ OFF |
| **이동 경로 표시** | ✅ ON | ❌ OFF | ❌ OFF |
| **목표 위치 표시** | ✅ ON | ✅ ON | ❌ OFF |
| **색상 피드백** | ✅ ON | ✅ ON | ❌ OFF |
| **나레이션** | 상세 안내 (BeginnerGuided) | 간단 지시 (IntermediateSimple) | 간단 지시 |
| **힌트 오디오** | ✅ ON | ❌ OFF | ❌ OFF |
| **단계 설명** | ✅ ON | ✅ ON | ❌ OFF |
| **진행 바** | ✅ ON | ✅ ON | ❌ OFF |
| **위치 정보** | ✅ ON | ❌ OFF | ❌ OFF |
| **유사도 퍼센트** | ✅ ON | ✅ ON | ❌ OFF |
| **상세 점수** | ✅ ON | ✅ ON | ❌ OFF |
| **오류 하이라이트** | ✅ ON | ❌ OFF | ❌ OFF |
| **자동 단계 전환** | ✅ ON | ✅ ON | ❌ OFF |
| **재시도 가이드** | ✅ ON | ❌ OFF | ❌ OFF |
| **유사도 임계값** | 0.50 | 0.65 | 0.75 |
| **홀드 시간** | 1.5초 | 2.0초 | 2.0초 |
| **시도 횟수 추적** | ❌ OFF | ❌ OFF | ✅ ON |
| **사전 평가 모드** | ❌ OFF | ❌ OFF | ✅ ON |

#### 핵심 클래스

```
InfoPanelController (모드/난이도 선택 UI)
  ├── ModeType enum (Practice / Evaluation)
  ├── ContentPage enum (ModeSelection / Skeleton / ExpertVideo / Result)
  ├── practiceToggle, evaluationToggle (모드 토글)
  ├── beginnerToggle, intermediateToggle, advancedToggle (난이도 토글)
  ├── OnModeToggleChanged() → StartSimulation()
  └── OnDifficultyToggleChanged() → DifficultyManager.SetDifficulty()

DifficultyManager (Singleton — 런타임 난이도 관리)
  ├── SetDifficulty(DifficultyLevel)
  ├── GetPreset(DifficultyLevel) → DifficultyPreset
  ├── ShowGuideHands, GuideHandOpacity (편의 프로퍼티)
  ├── SimilarityThreshold, RequiredHoldTime (평가 파라미터)
  ├── IsPreEvaluationMode (상급자 전용)
  └── OnDifficultyChanged (이벤트)

DifficultySettings (난이도 프리셋 정의)
  ├── DifficultyLevel enum (Beginner / Intermediate / Advanced)
  ├── NarrationType enum (BeginnerGuided / IntermediateSimple)
  └── DifficultyPreset (가이드/UI/평가 파라미터 구조체)
```

#### 선택 흐름 (TrainingScene 내)

```
TrainingScene 로드
  │
  ▼
InfoPanelController.InitializePanel()
  └─ modeSelectionPage 표시
     └─ 기본값: 난이도=초급자, 모드=None (미선택)
  │
  ▼
사용자: 난이도 토글 선택 (선택적)
  └─ DifficultyManager.SetDifficulty(level)
  │
  ▼
사용자: 모드 토글 선택 (필수)
  ├─ "실습" 또는 "평가"
  └─ → OnModeToggleChanged()
       ├─ ScenarioManager.SetModeInfo(모드, 난이도)
       ├─ PlayerPrefs 저장 (SelectedMode, SelectedDifficulty)
       └─ StartSimulation() ← 시나리오 즉시 시작
```

---

### 3.9 UI 시스템 (UI)

**디렉토리**: `Assets/Scripts/ClaudeScripts/UI/`

#### 기능 목록

| ID | 기능명 | 설명 | 상태 |
|----|--------|------|------|
| UI-01 | 씬 전환 로딩 | 로딩 화면 포함 씬 전환 | ✅ 완료 |
| UI-02 | 각도 표시 | 실시간 회전 각도 표시 | ✅ 완료 |
| UI-03 | 진행률 타임라인 | 도트 기반 진행 상태 표시 | ✅ 완료 |
| UI-04 | 퀴즈 패널 | 훈련 후 퀴즈 표시 | ✅ 완료 |
| UI-05 | 종료 팝업 | 종료 확인 팝업 | ✅ 완료 |
| UI-06 | 연습 설정 컨트롤러 | 연습 모드 설정 UI | ✅ 완료 |
| UI-07 | 결과 요약 UI | TunaResultUI 결과 화면 | ✅ 완료 |
| UI-08 | 단계 피드백 UI | 실시간 단계별 피드백 | ✅ 완료 |

---

### 3.10 녹화 시스템 (Recording)

**디렉토리**: `Assets/Scripts/ClaudeScripts/Recording/`

#### 기능 목록

| ID | 기능명 | 설명 | 상태 |
|----|--------|------|------|
| REC-01 | 듀얼 카메라 녹화 | 메인 + 디테일 카메라 동시 녹화 | ✅ 완료 |
| REC-02 | 훈련 세션 녹화 | 훈련 과정 영상 녹화 | ✅ 완료 |

---

### 3.11 웹뷰 (WebView)

**디렉토리**: `Assets/Scripts/ClaudeScripts/WebView/`

#### 기능 목록

| ID | 기능명 | 설명 | 상태 |
|----|--------|------|------|
| WEB-01 | VR 내 웹 브라우저 | 인앱 웹 브라우저 | ✅ 완료 |
| WEB-02 | VR 입력 처리 | 웹뷰 전용 VR 입력 | ✅ 완료 |
| WEB-03 | 가상 키보드 | 시스템 키보드 연동 | ✅ 완료 |

---

### 3.12 유틸리티 (Utils)

**디렉토리**: `Assets/Scripts/ClaudeScripts/Utils/`

#### 기능 목록

| ID | 기능명 | 설명 | 상태 |
|----|--------|------|------|
| UTL-01 | 중앙화된 로깅 | ChunaLogger — 레벨별 로그 관리 | ✅ 완료 |
| UTL-02 | 설정 키 관리 | PrefsKeys — PlayerPrefs 키 상수화 | ✅ 완료 |
| UTL-03 | 오브젝트 풀링 | ObjectPool\<T\> — 재사용 가능한 객체 풀 | ✅ 완료 |

---

## 4. API 엔드포인트 명세

**Base URL**: `https://qpqjpivcg1.execute-api.ap-northeast-2.amazonaws.com`

| # | 엔드포인트 | 메서드 | 요청 데이터 | 응답 데이터 | 용도 |
|---|-----------|--------|-------------|-------------|------|
| 1 | `/device/auth/chuna` | POST | DeviceUUID | DeviceResponseData | 디바이스 인증 |
| 2 | `/user/userlist/{orgID}` | GET | — | UserData[] | 사용자 목록 조회 |
| 3 | `/device/logon` | POST | LogonData | MirroringData | 사용자 로그온 |
| 4 | `/device/logoff` | POST | LogoffData | String | 사용자 로그오프 |
| 5 | `/device/update/runstatus` | POST | RunStatus | JSON | 디바이스 상태 업데이트 |
| 6 | `/device/regist/reset` | POST | DeviceRequest | String | 디바이스 초기화 |
| 7 | `/quiz/{contentType}` | POST | RequestQuizData | QuizData[] | 퀴즈 데이터 조회 |
| 8 | `/result/chuna` | POST | ResultData | String | 훈련 결과 업로드 |

---

## 5. 데이터 구조 명세

### 4.1 CSV — 시나리오 정의

```
파일 위치: Resources/Scenarios/{시나리오명}.csv

Phase | Step | SubStep | Duration | VoiceInstruction | HandTrackingFile |
ConditionType | PatientAnimationClip | VideoStartTime | VideoEndTime |
ContactTarget | PivotTarget
```

### 4.2 CSV — 손 포즈 데이터

```
파일 위치: Resources/HandPoseData/{파일명}.csv

Frame | Time |
LeftWristPos_X | LeftWristPos_Y | LeftWristPos_Z |
LeftWristRot_X | LeftWristRot_Y | LeftWristRot_Z | LeftWristRot_W |
RightWristPos_X | RightWristPos_Y | RightWristPos_Z |
RightWristRot_X | RightWristRot_Y | RightWristRot_Z | RightWristRot_W
```

### 4.3 ScriptableObject 설정 파일

| ScriptableObject | 용도 |
|-----------------|------|
| `ScenarioConfig` | 시나리오명, 애니메이터, 환자 프리셋 |
| `ChunaLimitData` | 수기별 관절 한계값, 위반 감점, 경고 임계값 |
| `DifficultySettings` | 난이도 수준별 파라미터 |
| `ApiConfig` | API Base URL, 타임아웃 |

### 4.4 로컬 저장소 (PlayerPrefs)

| 키 (PrefsKeys) | 용도 |
|----------------|------|
| 디바이스 SN | 디바이스 고유 식별 |
| 마지막 선택 사용자 | 자동 로그인 편의 |
| 훈련 이력 캐시 | 오프라인 데이터 |
| UI 환경설정 | 사용자 UI 선호도 |

---

## 6. 현재 기능 현황 요약

### 완성도 매트릭스

| 모듈 | 기능 수 | 완료 | 완성도 | 비고 |
|------|---------|------|--------|------|
| 인증 (Auth) | 8 | 8 | 100% | Mock 서비스 포함 |
| 시나리오 (Scenario) | 7 | 7 | 100% | 5개 시나리오 운영 |
| 추나 평가 (ChunaData) | 11 | 11 | 100% | 핵심 평가 엔진 |
| 손 포즈 (PoseData) | 5 | 5 | 100% | 녹화/재생 완비 |
| 환자 (Patient) | 3 | 3 | 100% | 프리셋 기반 |
| 연습 (Practice) | 6 | 6 | 100% | 튜토리얼 완비 |
| 결과 (Result) | 6 | 6 | 100% | 서버 연동 포함 |
| 모드/난이도 (Training) | 7 | 7 | 100% | 모드 선택 + 3단계 난이도 프리셋 |
| UI | 8 | 8 | 100% | VR 최적화 |
| 녹화 (Recording) | 2 | 2 | 100% | 듀얼 카메라 |
| 웹뷰 (WebView) | 3 | 3 | 100% | TLabWebView |
| 유틸리티 (Utils) | 3 | 3 | 100% | 로깅/설정 |
| **전체** | **69** | **69** | **100%** | — |

### 아키텍처 패턴

| 패턴 | 적용 위치 |
|------|-----------|
| Singleton | AuthenticationService, ScenarioManager, ScenarioEventSystem |
| Observer/Event | AuthEvents, ScenarioEventSystem |
| Interface Segregation | IAuthenticationService, IAuthUI, IObjectPool\<T\> |
| Bridge | ChunaPathEvaluatorBridge |
| State Machine | Scenario Phase/Step/SubStep 진행 |
| ScriptableObject Config | ScenarioConfig, ChunaLimitData, DifficultySettings |
| Object Pool | ObjectPool\<T\> |
| Data-Driven (CSV) | 시나리오/손 포즈 외부 데이터 |

---

## 7. 추가 필요 기능 분석

### 6.1 즉시 필요 (High Priority)

| # | 기능명 | 필요 사유 | 관련 모듈 |
|---|--------|-----------|-----------|
| H-01 | **오프라인 모드** | 네트워크 불안정 환경 대비, 로컬 캐시 기반 인증 및 결과 큐잉 | Auth, Result |
| H-02 | **결과 이력 조회** | 과거 훈련 기록 조회/비교 기능 부재 — 학습 진행 상황 파악 불가 | Result, UI |
| H-03 | **멀티 사용자 세션 관리** | 동시 여러 사용자 교대 훈련 시 빠른 전환 지원 | Auth |
| H-04 | **에러 복구 메커니즘** | 훈련 중 앱 크래시 시 진행 상태 자동 저장/복구 | Scenario, Result |
| H-05 | **접근성 설정** | 시력/청력 보조 옵션 (자막 크기, 색상 대비, 음량 조절) | UI, Practice |

### 6.2 단기 개선 (Medium Priority)

| # | 기능명 | 필요 사유 | 관련 모듈 |
|---|--------|-----------|-----------|
| M-01 | **다국어 지원 (i18n)** | 현재 한국어 하드코딩 — 글로벌 확장 불가 | 전체 |
| M-02 | **통계 대시보드** | 학생별/학년별/시나리오별 통계 시각화 | Result, UI |
| M-03 | **시나리오 에디터** | CSV 수동 편집 의존 → 인앱/에디터 도구로 시나리오 제작 | Scenario |
| M-04 | **손 포즈 리샘플링 도구 개선** | 현재 Editor 전용 → 런타임 리샘플링 지원 | PoseData |
| M-05 | **난이도 자동 조절** | 사용자 수행도 기반 적응형 난이도 | Training |
| M-06 | **음성 안내 TTS 연동** | 사전 녹음 의존 → 동적 TTS로 확장성 향상 | Scenario |

### 6.3 장기 보강 (Low Priority)

| # | 기능명 | 필요 사유 | 관련 모듈 |
|---|--------|-----------|-----------|
| L-01 | **단위 테스트 체계** | Test Framework 있으나 실제 테스트 코드 부족 | 전체 |
| L-02 | **CI/CD 파이프라인** | 빌드/테스트 자동화 미구축 | DevOps |
| L-03 | **원격 모니터링** | 디바이스 상태/로그 원격 수집 | Utils |
| L-04 | **설정 동기화** | 디바이스 간 사용자 설정 동기화 | Utils, Auth |

---

## 8. 확장 로드맵 제안

### Phase 1 — 학습 분석 & 피드백 고도화

| # | 확장 기능 | 설명 | 기대 효과 |
|---|----------|------|-----------|
| E-01 | **AI 기반 동작 분석** | 머신러닝 모델로 손 동작 패턴 분석, 개인화 피드백 | 학습 효율 30%+ 향상 |
| E-02 | **학습 경로 추천** | 과거 성적 기반 맞춤 시나리오/난이도 추천 | 맞춤형 학습 경험 |
| E-03 | **상세 리플레이 기능** | 훈련 전 과정을 3인칭 시점으로 리플레이 | 자기 객관화, 오류 인지 |
| E-04 | **비교 분석 뷰** | 전문가 동작 vs 학습자 동작 나란히 비교 | 차이점 시각적 확인 |
| E-05 | **실시간 음성 피드백** | 동작 수행 중 실시간 음성 코칭 (TTS 기반) | 몰입감 유지 학습 |

### Phase 2 — 협업 & 멀티플레이어

| # | 확장 기능 | 설명 | 기대 효과 |
|---|----------|------|-----------|
| E-06 | **교수자 원격 모니터링** | 교수자가 PC/태블릿에서 학생 훈련 실시간 관찰 | 원격 교육 가능 |
| E-07 | **멀티플레이어 협업 훈련** | 2인 이상 동시 훈련 (교수-학생 / 학생-학생) | 실습 교육 확장 |
| E-08 | **실시간 주석/마킹** | 교수자가 학생 VR 화면에 실시간 마킹/지시 | 즉각적 교정 |
| E-09 | **훈련 세션 공유** | 우수 훈련 세션 녹화 공유 및 학습 자료화 | 모범 사례 전파 |

### Phase 3 — 콘텐츠 확장

| # | 확장 기능 | 설명 | 기대 효과 |
|---|----------|------|-----------|
| E-10 | **추가 수기 시나리오** | 경추 외 흉추/요추/골반 추나 수기 추가 | 교육 범위 확장 |
| E-11 | **환자 다양성** | 체형/연령/증상별 다양한 환자 모델 | 실전 대응력 향상 |
| E-12 | **합병증 시뮬레이션** | 시술 중 이상 반응 시나리오 (과도한 힘, 부정확 위치) | 안전 교육 |
| E-13 | **해부학 학습 모드** | 3D 해부학 교육 모드 (근육/인대/뼈 레이어 분리 표시) | 기초 해부학 학습 |
| E-14 | **평가 시험 모드** | 가이드 없는 실전 평가 모드 (자격시험 시뮬레이션) | 시험 대비 |

### Phase 4 — 플랫폼 & 인프라

| # | 확장 기능 | 설명 | 기대 효과 |
|---|----------|------|-----------|
| E-15 | **관리자 웹 포탈** | 학생 관리, 시나리오 관리, 통계 대시보드 웹 앱 | 관리 편의성 |
| E-16 | **LMS 연동** | 기존 학습관리시스템(LMS) 연동 (SCORM/xAPI) | 기존 인프라 활용 |
| E-17 | **멀티 플랫폼 지원** | Apple Vision Pro, PICO 등 추가 VR 플랫폼 | 시장 확장 |
| E-18 | **클라우드 세이브** | 훈련 데이터/설정 클라우드 동기화 | 디바이스 독립성 |
| E-19 | **햅틱 피드백 연동** | 햅틱 글러브 연동 (촉감 피드백) | 실감도 극대화 |
| E-20 | **분석 API** | 훈련 데이터 분석용 외부 API 제공 | 연구/논문 활용 |

### 확장 우선순위 매트릭스

```
영향력 (높음)
  │
  │  E-01 AI분석    E-06 원격모니터링
  │  E-14 시험모드   E-10 추가시나리오
  │  E-03 리플레이   E-15 관리포탈
  │
  │  E-05 음성피드백  E-07 멀티플레이어
  │  E-02 경로추천    E-11 환자다양성
  │  E-04 비교분석    E-16 LMS연동
  │
  │  E-13 해부학     E-08 실시간마킹
  │  E-12 합병증     E-17 멀티플랫폼
  │  E-18 클라우드    E-19 햅틱
  │
  └──────────────────────────────────► 구현 난이도 (높음)
     (낮음)
```

---

> **문서 끝** — 이 문서는 GuideChuna 프로젝트의 현재 기능을 체계적으로 정리하고, 향후 확장 방향을 제시하기 위해 작성되었습니다.
