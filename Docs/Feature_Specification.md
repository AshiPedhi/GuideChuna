# GuideChuna 기능 명세서

> **프로젝트명**: GuideChuna — VR 추나요법 의료 훈련 시뮬레이터
> **플랫폼**: Meta Quest (Android APK)
> **엔진**: Unity 6.0.2f1 / C# / Meta XR SDK 81.0.0
> **작성일**: 2026-03-13

---

## 목차

1. [프로그램 전체 흐름도](#1-프로그램-전체-흐름도)
2. [모듈별 기능 명세](#2-모듈별-기능-명세)
   - 2.1 [인증 시스템 (Auth)](#21-인증-시스템-auth)
   - 2.2 [시나리오 관리 (Scenario)](#22-시나리오-관리-scenario)
   - 2.3 [추나 평가 엔진 (ChunaData)](#23-추나-평가-엔진-chunadata)
   - 2.4 [손 포즈 데이터 (PoseData)](#24-손-포즈-데이터-posedata)
   - 2.5 [환자 모델 관리 (Patient)](#25-환자-모델-관리-patient)
   - 2.6 [연습 모드 (Practice)](#26-연습-모드-practice)
   - 2.7 [훈련 결과 (Result)](#27-훈련-결과-result)
   - 2.8 [난이도 관리 (Training)](#28-난이도-관리-training)
   - 2.9 [UI 시스템 (UI)](#29-ui-시스템-ui)
   - 2.10 [녹화 시스템 (Recording)](#210-녹화-시스템-recording)
   - 2.11 [웹뷰 (WebView)](#211-웹뷰-webview)
   - 2.12 [유틸리티 (Utils)](#212-유틸리티-utils)
3. [API 엔드포인트 명세](#3-api-엔드포인트-명세)
4. [데이터 구조 명세](#4-데이터-구조-명세)
5. [현재 기능 현황 요약](#5-현재-기능-현황-요약)
6. [추가 필요 기능 분석](#6-추가-필요-기능-분석)
7. [확장 로드맵 제안](#7-확장-로드맵-제안)

---

## 1. 프로그램 전체 흐름도

### 1.1 메인 흐름 (Main Flow)

```
[앱 실행]
   │
   ▼
[로비 씬 (lobby.unity)]
   │
   ├─ 1) 디바이스 인증 ─── POST /device/auth/chuna
   │     └─ DeviceUUID 기반 → orgID, 라이선스 정보 반환
   │
   ├─ 2) 사용자 목록 조회 ─── GET /user/userlist/{orgID}
   │     └─ 학년(grade)별 필터링
   │
   ├─ 3) 사용자 로그온 ─── POST /device/logon
   │     └─ 미러링(스트리밍) 설정 반환
   │
   ▼
[시나리오 선택]
   │
   ├─ A) 시나리오 훈련 모드 ──────────────────────┐
   │     │                                         │
   │     ▼                                         │
   │   [시나리오 씬 로드 (Scenario_1~5.unity)]     │
   │     │                                         │
   │     ├─ CSV 시나리오 데이터 로드               │
   │     ├─ CSV 손 포즈 데이터 로드                │
   │     │                                         │
   │     ▼                                         │
   │   [훈련 실행 루프]                            │
   │     │                                         │
   │     ├─ Phase(단계) 진행                       │
   │     │   ├─ Step(스텝) 진행                    │
   │     │   │   ├─ SubStep(서브스텝) 실행         │
   │     │   │   │   ├─ 음성/텍스트 안내           │
   │     │   │   │   ├─ 손 동작 가이드 표시        │
   │     │   │   │   ├─ 사용자 손 동작 추적        │
   │     │   │   │   ├─ 실시간 유사도 평가         │
   │     │   │   │   ├─ 한계값 위반 검출           │
   │     │   │   │   └─ 피드백 UI 업데이트         │
   │     │   │   └─ 조건 충족 시 다음 SubStep      │
   │     │   └─ Step 완료                          │
   │     └─ Phase 완료                             │
   │     │                                         │
   │     ▼                                         │
   │   [결과 집계]                                 │
   │     ├─ 유사도 점수, 경고 횟수, 수행 시간      │
   │     ├─ 결과 UI 표시                           │
   │     └─ POST /result/chuna ─── 서버 업로드     │
   │     │                                         │
   │     ▼                                         │
   │   [퀴즈 (선택)]                               │
   │     └─ POST /quiz/{contentType}               │
   │                                               │
   ├─ B) 연습 모드 (Practice) ────────────────────┐
   │     │                                         │
   │     ▼                                         │
   │   [Practice_Scene.unity 로드]                 │
   │     ├─ 단계별 튜토리얼 안내                   │
   │     ├─ UI 조작 연습 (그랩, 이동)              │
   │     ├─ 설정 탐색 연습                         │
   │     ├─ 손 제스처 연습                         │
   │     └─ 완료 후 로비 복귀                      │
   │                                               │
   └─ C) 로그오프 ─── POST /device/logoff          │
         └─ 세션 종료                               │
                                                    │
   ◄────────────── 로비 복귀 ◄─────────────────────┘
```

### 1.2 씬(Scene) 구성

| 씬 이름 | 역할 | 주요 매니저 |
|----------|------|-------------|
| `lobby.unity` | 메인 로비, 인증, 시나리오 선택 | AuthenticationService, LobbyAuthUI_Complete |
| `Scenario_1~5.unity` | 시나리오별 훈련 환경 | ScenarioManager, ChunaPathEvaluator |
| `Practice_Scene.unity` | 튜토리얼/연습 모드 | PracticeManager |
| `TrainingScene.unity` | 훈련 평가 전용 | ChunaPathEvaluator, TrainingResultTracker |
| `LoadingScene.unity` | 씬 전환 로딩 화면 | SceneLoader |

---

## 2. 모듈별 기능 명세

### 2.1 인증 시스템 (Auth)

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

### 2.2 시나리오 관리 (Scenario)

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

### 2.3 추나 평가 엔진 (ChunaData)

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

### 2.4 손 포즈 데이터 (PoseData)

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

### 2.5 환자 모델 관리 (Patient)

**디렉토리**: `Assets/Scripts/ClaudeScripts/Patient/`

#### 기능 목록

| ID | 기능명 | 설명 | 상태 |
|----|--------|------|------|
| PAT-01 | 환자 위치 프리셋 | 앉기/눕기 등 다양한 자세 | ✅ 완료 |
| PAT-02 | 해부학적 근육 시각화 | 대상 근육 하이라이트 표시 | ✅ 완료 |
| PAT-03 | 근육 그룹 제어 | 흉쇄유돌근, 상부승모근, 사각근 등 | ✅ 완료 |

---

### 2.6 연습 모드 (Practice)

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

### 2.7 훈련 결과 (Result)

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

### 2.8 난이도 관리 (Training)

**디렉토리**: `Assets/Scripts/ClaudeScripts/Training/`

#### 기능 목록

| ID | 기능명 | 설명 | 상태 |
|----|--------|------|------|
| TRN-01 | 난이도 설정 | DifficultySettings ScriptableObject | ✅ 완료 |
| TRN-02 | 난이도 매니저 | 난이도 수준 관리 및 적용 | ✅ 완료 |

---

### 2.9 UI 시스템 (UI)

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

### 2.10 녹화 시스템 (Recording)

**디렉토리**: `Assets/Scripts/ClaudeScripts/Recording/`

#### 기능 목록

| ID | 기능명 | 설명 | 상태 |
|----|--------|------|------|
| REC-01 | 듀얼 카메라 녹화 | 메인 + 디테일 카메라 동시 녹화 | ✅ 완료 |
| REC-02 | 훈련 세션 녹화 | 훈련 과정 영상 녹화 | ✅ 완료 |

---

### 2.11 웹뷰 (WebView)

**디렉토리**: `Assets/Scripts/ClaudeScripts/WebView/`

#### 기능 목록

| ID | 기능명 | 설명 | 상태 |
|----|--------|------|------|
| WEB-01 | VR 내 웹 브라우저 | 인앱 웹 브라우저 | ✅ 완료 |
| WEB-02 | VR 입력 처리 | 웹뷰 전용 VR 입력 | ✅ 완료 |
| WEB-03 | 가상 키보드 | 시스템 키보드 연동 | ✅ 완료 |

---

### 2.12 유틸리티 (Utils)

**디렉토리**: `Assets/Scripts/ClaudeScripts/Utils/`

#### 기능 목록

| ID | 기능명 | 설명 | 상태 |
|----|--------|------|------|
| UTL-01 | 중앙화된 로깅 | ChunaLogger — 레벨별 로그 관리 | ✅ 완료 |
| UTL-02 | 설정 키 관리 | PrefsKeys — PlayerPrefs 키 상수화 | ✅ 완료 |
| UTL-03 | 오브젝트 풀링 | ObjectPool\<T\> — 재사용 가능한 객체 풀 | ✅ 완료 |

---

## 3. API 엔드포인트 명세

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

## 4. 데이터 구조 명세

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

## 5. 현재 기능 현황 요약

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
| 난이도 (Training) | 2 | 2 | 100% | 기본 구조 |
| UI | 8 | 8 | 100% | VR 최적화 |
| 녹화 (Recording) | 2 | 2 | 100% | 듀얼 카메라 |
| 웹뷰 (WebView) | 3 | 3 | 100% | TLabWebView |
| 유틸리티 (Utils) | 3 | 3 | 100% | 로깅/설정 |
| **전체** | **64** | **64** | **100%** | — |

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

## 6. 추가 필요 기능 분석

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

## 7. 확장 로드맵 제안

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
