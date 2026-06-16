# VR 추나요법 교육 시뮬레이터(GuideChuna) 연구개발 노트

**작성 기준일**: 2026-06-08
**대상 기간**: 2026년 4월 ~ 6월 (3월은 배경으로 요약)
**플랫폼**: Unity 6 (6000.2.1f1), Meta Quest 핸드트래킹(OVR / Oculus Interaction SDK)
**앱 개요**: 가이드 포즈 녹화 → 플레이어 손 포즈 실시간 비교 → 유사도 점수 산출 방식의 VR 추나요법 교육 앱

---

## 0. 시스템 개요 및 사용 기술

| 구분 | 내용 |
|------|------|
| 엔진 | Unity 6 (6000.2.1f1) |
| 입력 | Meta Quest 핸드트래킹 (OVR / Oculus Interaction SDK) |
| 평가 원리 | 가이드 손 포즈 CSV 녹화 → 플레이어 손 포즈 프레임 비교 → 유사도 + 가동범위 준수 채점 |
| 데이터 | 시나리오 CSV(단계/조건/애니메이션/핸드데이터), ScenarioConfig(ScriptableObject) |
| 결과 | 로컬 CSV 저장 + 백엔드 서버 전송(REST) + 웹뷰 결과 조회 |
| 부가 | PC 미러링(Unity Render Streaming + WebSocket), TLab WebView(결과/퀴즈) |
| 대상 시나리오 | 상부승모근 / 견갑거근 / 사각근 / 흉쇄유돌근 / 대흉근 (근막요법 5종) |

### 모드 체계
- **실습모드**: 초급(가이드 풀 표시) / 중급(가이드 반투명) / 상급(가이드 없음, 모의평가)
- **평가모드**: 가이드 없음 + 최소 나래이션, 공식 점수 기록
- **시나리오 단계 종류**: 가이드(시작/종료 안내) · 진단 · 제한장벽확인 · 등척성운동 · 스트레칭 · 재평가
- **동작 타입**: rotation(회전) / position(위치) / 빈값(가이드만 표시)

### 3월 배경 (아키텍처 토대)
- **통합 씬 데이터 드리븐 아키텍처**(3/4): 시나리오별 씬을 분리하지 않고, 단일 TrainingScene이 CSV/ScriptableObject 데이터로 구동되도록 전환. 이후 모든 4~6월 작업의 기반.
- 기준점 로컬 좌표 시스템 통합(3/10), HandPoseResampler 환자 추적/좌표변환 탭(3/6, 3/10), AngleDisplayController 리팩터링(3/24), Phase별 나래이션 로드(3/17), 대흉근 pivot 기반 외전 대응(3/26~27), 오른손 하드코딩 수정·GuideOnly 완료조건(3/27), 리밋체커↔적정범위 끝 자동 매핑·스코어링 파이프라인 통합(3/31).

---

## 1. 채점/유사도 평가 시스템

### 1-1. 필요 기능
가이드 손 포즈와 플레이어 손 포즈를 실시간 비교하여 "주동수(시술 손)의 정확도 + 보조수의 보조 품질 + 가동범위 준수"를 종합 점수로 환산. 단계 종류(회전/위치/등척성/스트레칭)별로 적합한 채점 방식 필요.

### 1-2. 개발 과정 / 사용 기술
- `HandPoseComparator`: 손바닥 중심 위치 + palm orientation + 손모양 비교 (`CompareRightPose` 풀 비교 / `CompareRightPoseSimplified` 간소화 비교)
- `EvaluationScoringEngine`: 최종 점수 산출 (유사도 + 가동범위 준수 + 통계: 좌/우 평균, 표준편차, min/max, peakExceededRatio)
- `EvaluationPhaseManager`: WaitingForStart → StartHold → Moving → MidHold → Completed 단계 머신
- 결과 데이터 확장(4/1): StepResult에 좌/우 개별 유사도·표준편차·시계열(`SimilarityTimePoint`) 추가

### 1-3. 발생 이슈 → 해결방법

**이슈 A — 보조수 점수 패딩 (2026-05-27)**
- 증상: 모든 단손 단계에서 왼손(보조수)이 항상 100% → 점수가 실제 기법과 무관하게 ~58점 깔림
- 원인: `CompareLeftPoseSimplified`가 포즈 비교가 아니라 "손바닥 아래+주먹 안 쥠"만 체크 → 손만 대충 두면 100%
- 해결: **접촉 기반 분리 채점** 도입. `MetricsSnapshot.leftTouching`(접촉 샘플링) 신설 → `supportQuality = 접촉유지비율 × avgLeft`. 가중치 **주동수 45 + 보조수 15 + 가동범위 40**으로 재설계. 접촉 안 하면 점수 하락(패딩 제거)

**이슈 B — 등척성운동 유사도 0% (2026-05-28)**
- 증상: 등척성운동이 항상 0%로 종합 평균을 끌어내림
- 원인: 등척성은 `startHoldOnly` 모드라 Moving/MidHold를 안 거침 → 유사도 스냅샷 0개 → 포즈 비교가 한 번도 실행 안 됨 (데이터는 존재)
- 해결: `ChunaPathEvaluator.Update()` 메트릭 기록 조건에 `startHoldOnly && StartHold` 추가 → 홀드 중 유사도 채점. **홀드유지 40점** = `holdQuality = Clamp01(요구홀드/실제소요시간)`로 분기 산출 (흔들림 없이 버틸수록 만점)

**이슈 C — 채점 평탄화 / 손바닥 뒤집어도 80점 (2026-05-28)**
- 증상: 손바닥을 완전히 반대로 돌려도 80점
- 원인: `CompareRightPoseSimplified`가 palm orientation 미체크 → 손목 위치만 맞으면 ~95%
- 해결: 가중치 위치70/모양30 → **위치50 + 방향30 + 모양20**. 위치 dropoff 가파르게(`1-err×0.3`→`1-err×0.5`), palm rotation 비교(`Quaternion.Angle/180`) 추가. 손바닥 뒤집기 ~95%→~70%로 변별력 확보

**이슈 D — 가이드 단계가 유사도 끌어내림 (2026-05-27)**
- 증상: 시작/종료 가이드(stepNo=0)가 0% StepResult로 기록돼 종합 유사도 23%로 폭락
- 해결: `TrainingResultTracker`에 `IsGuideStepName` 가드 추가 → 가이드 단계는 StepResult 미생성/미기록. 종합 23%→~69% 정상화

---

## 2. 평가모드 시스템

### 2-1. 필요 기능
실습모드와 분리된 "공식 평가" 모드. 가이드 없이 최소 나래이션만 제공, 시나리오별 특정 phase만 평가, 공식 점수를 서버에 기록.

### 2-2. 개발 과정 / 사용 기술
- 평가모드 추가(4/1): `DifficultyLevel.Evaluation`, `NarrationType.EvaluationMinimal`, `DifficultyManager.IsOfficialScore`
- 난이도 프리셋 16개 항목 실제 시스템 연결, 미사용 6개 삭제(4/1)
- `ScenarioConfig.evaluationPhases`: 인스펙터 한 줄로 평가 phase 화이트리스트 지정(5/26)

### 2-3. 발생 이슈 → 해결방법

**이슈 A — 평가모드 미완성 발견 (2026-05-26)**
- 6개 결함 식별. 핵심: 평가 토글이 `selectedMode` 문자열만 set하고 `DifficultyManager.SetDifficulty(Evaluation)` 호출 누락 → 평가 나래이션/가이드 OFF/임계값이 전혀 발동 안 함
- 해결: `InfoPanelController.OnModeToggleChanged`에서 모드별 DifficultyManager 동기화 강제

**이슈 B — evaluationPhases 미직렬화 (2026-05-27)**
- 증상: "중부만" 설정해도 전부/중부/후부 다 진행
- 원인: 필드는 5/26에 추가됐지만 ScenarioConfig **에셋이 필드 추가 이전 상태로 disk 저장** → 런타임 null → 필터 무동작
- 해결: `상부승모근.asset`에 `evaluationPhases: [시작,중부,종료]` 직접 기록.
- **교훈**: 필드 추가 후 기존 에셋은 인스펙터에서 한 번 건드려 재직렬화해야 disk 반영됨 (구버전 asset = 조용한 null)

**이슈 C — 시작 직후 중복 가이드 (2026-05-27)**
- 증상: 평가 시작 버튼 뒤에 "중부 시작" 전환 가이드가 또 떠서 버튼 2번 클릭
- 해결: `ScenarioManager.RemoveRedundantLeadingGuide` — 첫 phase가 가이드 전용일 때 둘째 phase 선두 가이드만 제거

**이슈 D — 평가 결과 텍스트 가독성 (2026-05-28)**
- step당 4줄 → 한 화면에 안 들어감. **2줄 컴팩트**로 압축(O/△/X·σ 제거, "리밋"→"위험 범위 초과" 한글화). 모드 분기(`isOfficialEvaluation`): 평가=상세 / 실습=phase별 한 줄 요약

---

## 3. 결과 데이터 및 서버 전송

### 3-1. 필요 기능
훈련 결과를 로컬 CSV로 저장 + 백엔드 서버로 전송 → 웹뷰 결과 페이지에서 조회. 시도 횟수 누적, 중도 종료 처리.

### 3-2. 개발 과정 / 사용 기술
- `TrainingResultData` → CSV 로컬 저장(`TrainingResultExporter`) → `ResultData` 매핑 → `AuthenticationService.PostResultAsync` 전송(`TrainingResultUploader`)
- 결과 페이지: 서버 조회형 (`ScenarioManager.resultWebUrl`)

### 3-3. 발생 이슈 → 해결방법

**이슈 A — 서버 전송 미구현 (2026-05-26)**
- `PostResultAsync` 정의만 있고 호출처 0건 → `TrainingResultUploader` 신설. `OnTrainingCompleted` 구독 → `ResultData` 매핑(subject="VR현훈추나" 등 고정값 + learnLevel2에 step별 상세 직렬화 ~750자)
- orgID 영속화: 메모리 변수에만 있던 orgID를 `LoginStateStore.SaveOrgID`로 PlayerPrefs 저장 → 씬 전환 후 접근 가능

**이슈 B — Uploader 미배치 (2026-05-27)**
- 코드/구독은 정상이나 컴포넌트가 어떤 씬에도 없어 서버 전송 0 (로컬 CSV는 정상). 사용자가 인스펙터로 `ScenarioSystem` 오브젝트에 추가

**이슈 C — attemptNumber 항상 0 (2026-05-27)**
- 필드만 있고 set 0회 → `TrainingAttemptStore`(PlayerPrefs 정적 헬퍼) 신설. `StartTracking`에서 시작 시 증가, 시나리오+모드별(평가/연습) 버킷 분리

**이슈 D — 중도 종료 시 무기록 (2026-06-05)**
- 증상: `FinishTracking()`(정상 완주)만 저장 → 중도 종료 시 무기록 + "점수 나쁘면 나가서 회피" 구멍 + attemptNumber만 오르는 비대칭
- 해결(사용자 결정 "둘 다", 6파일): `isCompleted` 플래그 추가, `SaveIncompleteResultIfTracking()`(공식평가+추적중일 때만 미완료 저장), ExitPopup 경고 메시지, learnLevel1=`(평가·미완료)` 표기. 연습모드는 경고만
- 부수: CSV 헤더-데이터 컬럼 어긋남(SubSteps/Completed/Skipped 3컬럼 vs 1필드) 함께 수정

---

## 4. 경고음 시스템 (적정범위 비프 + 초과 경고)

### 4-1. 필요 기능
가동 적정범위 진입 시 비프 시작 → 끝에 가까울수록 긴박감(interval 단축+pitch 상승) → 위험범위 초과 시 경고음 1회. 가이드 전용/홀드 스킵 단계에서는 무음.

### 4-2. 발생 이슈 → 해결방법

**이슈 A — 경고음 시스템 정리 (2026-04-16)**
- 결함1: 가이드 전용 단계에서도 비프 울림 → `ShouldPlayWarning()` = `isTracking && !IsGuideOnlyMode && !IsSkipMidHold` 게이트
- 결함2: 단일 AudioSource로 접근/초과 혼재 → `exceededAudioSource` 분리
- 결함3: 초과 판정 이중화 → `OnLimitWarning`으로 일원화

**이슈 B — 비프 구간 재설계 (2026-04-16)**
- 적정범위 진입 즉시 비프 시작, 끝에 가까울수록 interval↓ + pitch↑. `beepStartOffset` 삭제, `maxApproachPitch` 추가

**이슈 C — 경계 복귀 시 비프 안 남 (2026-04-17)**
- 원인: `isOverLimit=false` 해제가 적정범위 진입 전 분기에서만 발생 → 초과→복귀 경로에서 미해제
- 해결: 적정범위 안 분기에도 `isOverLimit=false` 추가

**이슈 D — MidHold 초과 경고 누락 (2026-05-26)**
- 원인: MidHold 단계에 over-limit 체크가 없어 초과→복귀 시 경고음 안 바뀜 (4/17 수정 후에도 MidHold 경로에 잔존)
- 해결: `UpdateMidHold` 도입부에 `UpdateMoving`과 동일한 `FireOnLimitWarning` 패턴 추가

**이슈 E — 경고 카운트 폭주 (2026-05-28)**
- `OnLimitWarning`이 매 프레임 발화(level-triggered) → 7초×60fps=~420회. "위험 범위 초과"(edge-triggered)와 같은 사건이라 **표시만 숨김** (내부 이벤트는 경고음이 의존하므로 유지)

---

## 5. 적정범위/홀드 로직 (각도표시·진행도)

### 5-1. 필요 기능
사용자 손 진행도(axis)와 가이드/환자 애니메이션, 적정범위 마커가 좌표계상 정확히 일치해야 함. 회전/위치/스트레칭/재평가 단계별로 적정범위 기준이 달라짐.

### 5-2. 발생 이슈 → 해결방법

**이슈 A — 스트레칭/재평가 적정범위 버그 (2026-04-09, 견갑거근)**
- 재평가 회전 적정범위가 30~50%가 아닌 50~70%로 늘어남 → `isRotationStep` 프로퍼티 추가, 회전이면 일반 midHold 비율 사용
- axis와 마커 어긋남 → `currentAngleDisplayOffset`를 항상 0으로 (오프셋은 가이드/환자 시각용일 뿐 사용자 진행도에 영향 X)
- 스트레칭 진입 시 환자 애니메이션 frame 0 점프 → 호출 순서 변경(모드 설정을 애니메이션 재생 앞으로) + `Play(state, 0, currentStartRatio)` + progress remap

**이슈 B — 적정범위 좌표계 불일치 (2026-04-10, 전 시나리오)**
- 진행도(상대 이동량)와 midHold 비율(전체 절대값)이 다른 좌표계라 0.5 대신 0.65에서 인식
- 해결: 스트레칭 모드 시 `currentMidHoldStart/End`에서 `stretchingStart`(0.30) 차감 → 상대↔절대 일치

**이슈 C — substep 전환 시 디스플레이 미갱신 (2026-04-10, 4/13)**
- `SyncHoldRangeWithEvaluator`가 flag 변경만 감지 → 같은 단계 내 회전→위치 전환 누락
- 해결: `ForceRefreshHoldRange()` public 메서드 + hold 범위 값 자체 비교 추가

**이슈 D — 누운 환자(사각근) 회전 감지 불안정 (2026-04-13)**
- 누운 환자는 회전 축이 척추 방향(Z축)인데 `Vector3.forward`가 감지 축과 평행 → SignedAngle 수치 불안정
- 해결: `ScenarioConfig.overrideRotationAxis` + `lyingRotationAxis(Z)`, `GetRotationMeasurementVector()`로 측정 벡터 `Vector3.up` 전환 + `invertRotationDirection` 토글

**이슈 E — MidHold "훑고 지나가기" (2026-04-20)**
- 적정범위가 넓어 빠르게 지나가도 홀드 충족 → 인지 전에 단계 완료
- 해결: 손 velocity 대신 **진행도 변화율(ratio/s)** 기준. `pauseProgressVelocity` 이상이면 타이머 일시정지(리셋 아님, phaseHoldTime 보존). 범위 이탈/드리프트는 기존대로 리셋

---

## 6. 가이드핸드 좌표계 (환자 위치 추종)

### 6-1. 필요 기능
가이드 손이 생성된 후 사용자가 환자 위치를 조정하면 손도 함께 따라와야 함.

### 6-2. 발생 이슈 → 해결방법

**이슈 — 가이드핸드 베이크로 환자 이동 추종 불가 (2026-05-21, 전 시나리오)**
- 원인: `ConvertFramesToWorldSpace()`가 CSV 로드 시 1회 frame을 월드좌표로 **베이크** → 이후 환자 이동/회전해도 손은 고정
- 사용자 가설("부모-자식 묶으면?")은 단독 무효 (`Root.position` 월드 직접 할당이라 부모 무시)
- 해결: 베이크 제거, frame을 referenceTransform 기준 로컬로 유지하고 **6개 사용처**(가이드 재생/유사도 비교/축 회전/이동량/피벗각도/첫프레임)에서 매 프레임 `refPos + refRot * x` 변환. 손가락 joint는 손목 Root 자식이라 자동 추종(무수정)
- 별건: 견갑거근 뒤쪽 표시(referenceTransform 루트 폴백)는 4/8에 목 본 직접 할당 워크어라운드로 가려진 상태 (미수정, 워크어라운드 의존)

---

## 7. UI 헤드셋 배치 (Quest Link 트래킹 지연)

### 7-1. 필요 기능
시나리오 UI 그룹(메뉴+시나리오 UI)이 사용자 헤드셋 높이/방향 앞에 안정적으로 배치되어야 함. 고정 전 시선 추종 없이 한 번에 안착.

### 7-2. 발생 이슈 → 해결방법

**이슈 A — UI가 고정 위치에 박힘 (2026-06-01)**
- 원인 추적: PracticeSettings(시나리오 중 토글)는 정상인데 UI(Start 직후)는 비정상 → **호출 시점 차이**. **Quest Link 에디터에서 OVR이 카메라에 트래킹 데이터 적용까지 1~5초 지연** → Start 시점 headsetTransform=(0,0,0)에 박힘. 빌드 단독 실행은 첫 프레임부터 정상
- 해결: `ScenarioManager.StartScenario`에서 시나리오 시작 시점(OVR 안정 보장)에 강제 재배치 + Camera.main 폴백
- **교훈**: "에디터에선 안 되는데 빌드에선 됨" 패턴 시 OVR 트래킹 활성화 타이밍 의심 우선

**이슈 B — 모드 선택 후 UI 날아감 (2026-06-02)**
- 원인: 6/01 강제 재배치가 단일 프레임 스냅샷(안전망 0개)이라 비정상 Y 프레임에 잠겨 날아감
- 해결: 헤드셋 Y 정상범위 가드(0.5~2.5m) + **추종 제거**(기본 위치 대기) → 연속 4프레임 안정(<0.03m) 후 1회 배치·잠금. `EnsurePositionedWhenReady()`로 시나리오 시작 시 중복 재배치 차단

---

## 8. 맞춤 설정 / 환자 위치 조절

### 8-1. 필요 기능
사용자가 환자/침대 위치를 자기 눈앞 목표 지점으로 맞추거나 직접 그랩으로 미세 조정.

### 8-2. 발생 이슈 → 해결방법

**이슈 A — 부모 pivot 어긋남 (2026-06-01)**
- 부모 pivot과 자식(실제 환자) 시각 중심이 다르면 목표 지점과 어긋남
- 해결: `pivotOffset = actualTarget.position - targetObject.position` 보정. Scene뷰 Gizmo에 현재/예상 Bounds 와이어큐브 추가

**이슈 B — 맞춤 설정 누적 밀림 (2026-06-05)**
- 증상: 위치설정으로 옮긴 뒤 재맞춤 시 옮긴 만큼 밀림 (사용자 보고 정확, 착각 아님)
- 원인: `ReplacePos.Update()`가 매 프레임 환자그룹 `all`의 월드위치를 직접 덮어씀 → 부모 기준 로컬 오프셋이 drift → 재맞춤 시 ROOT만 옮겨 delta만큼 밀림
- 해결: `Start`에서 authored 로컬 포즈 캐싱 → `OnCustomPositioning` 도입부에서 위치설정 강제종료 + 로컬 포즈 복원(회전 drift 포함)

**부가 도구**: PracticeManager 환자 위치 에디터 미리보기(4/15) — SerializeField + Scene뷰 Gizmo/Handle + `previewHeadsetY` 슬라이더(런타임 적응형 로직은 유지, 에디터 프리뷰 전용)

---

## 9. 미러링 ANR (PC 화면공유 강제종료)

### 9-1. 필요 기능
강사 PC로 학습자 화면 미러링(Unity Render Streaming + WebSocket). 잘못된 IP/PC 미실행 시에도 앱이 죽지 않아야 함.

### 9-2. 개발 과정 / 사용 기술
- `RenderManager.cs`: `RunConnectionLoopAsync` → `sm.Run()`(SignalingManager WebSocket 연결)
- serverIP는 백엔드 `/device/logon` 응답에서 동적 수신 (PC가 자기 IP를 서버에 등록하는 구조)

### 9-3. 발생 이슈 → 해결방법

**이슈 — 미러링 ANR 강제종료 (2026-04-16 진단 → 2026-05-18 메커니즘 확정 → 2026-06-05 수정)**
- 증상: 잘못된 IP/PC 미실행/다른 네트워크 시 선택 직후 1분 이내 강제종료
- 메커니즘 확정(5/18): `sm.Run()`의 WebSocket 초기 connect가 **메인 스레드 블록** → Quest ANR(5초 임계) → OS 강제종료(30~60초, TCP SYN 타임아웃과 일치)
- 원인 정밀화(6/5): "튕긴 뒤 재진입하면 정상" → **stale serverIP 매칭 오류**가 트리거 (밴드/네트워크 전환으로 PC IP 변경 → 서버 저장 IP 어긋남)
- 해결(후보 A, `RenderManager.cs`): `sm.Run()` 직전 `ProbeReachableAsync` 도달성 프로브 추가. `UniTask.RunOnThreadPool` + `TcpClient.ConnectAsync().AttachExternalCancellation(3s CTS)` (스레드풀 안전). 닿으면 진행/안 닿으면 스킵→재시도→Failed(앱 유지)
- 남은 과제(클라 통제 밖): 백엔드 PC IP 최신화, 로그아웃 시 off 전송(C), 연결 후 헬스체크(E)

---

## 10. Android 권한 정리 (Meta 스토어 심사)

### 10-1. 필요 기능
실제 미사용 권한(CAMERA/RECORD_AUDIO 등)을 머지 매니페스트에서 제거하여 Meta 스토어 심사 통과.

### 10-2. 발생 이슈 → 해결방법

**이슈 — v1.26 리젝 → v1.27/v1.28 대응 (2026-05-13~14)**
- 배경: v1.25 통과 후 v1.26에서 신규 자동주입 권한으로 리젝 (코드는 1st commit부터 있었으나 어떤 빌드 설정 변경이 자동주입을 새로 트리거, ProjectSettings가 LFS라 정확한 트리거 추적 불가)
- v1.27(1차): TLab WebView Scripting Define 2개 제거(`UNITYWEBVIEW_ANDROID_ENABLE_CAMERA`/`_MICROPHONE`) → TLab 측 권한 제거
- 자체 빌드 검증(Meta 응답 전): CAMERA/RECORD_AUDIO/MODIFY_AUDIO_SETTINGS/BLUETOOTH 4개 잔존 발견
- v1.28(2차, 선제): `BidirectionalSample.cs` 삭제(Assembly-CSharp 트리거) + AndroidManifest `tools:node="remove"` 8개(패키지 dll 트리거) + Camera X `m_AutoRequestUserAuthorization=0`
- **교훈**: 코드 그대로여도 빌드 설정 한 줄로 권한 자동주입 발생 가능 → 매 릴리스 머지 매니페스트 diff 검증. Assembly-CSharp 트리거=코드 수정, dll 트리거=`tools:node="remove"`

---

## 11. HandRecord 전용 도구 (가이드 데이터 녹화)

### 11-1. 필요 기능
시나리오와 분리된 패스스루 전용 빌드에서 가이드 손 포즈를 녹화/관리하는 도구.

### 11-2. 개발 과정 / 사용 기술
- `PracticeHandRecorder`: 녹화 시작/종료 토글 + 시나리오 선택 드롭다운, 파일명 `{phase}_{step}_핸드데이터_{시나리오}_{타임스탬프}`(5/18)
- 패스스루 전용 모드: `startWithRealityMode`(패스스루 강제), `startInSettingsMode`(설정 메뉴를 메인 UI로)(5/18)
- HandRecord 전용 APK 빌드 도구(`HandRecordBuildScript.cs`): `.handrecord` 접미사 별도 패키지, try/finally 무조건 원복(5/18)
- 침대 반투명 토글 + 위치설정 자동 종료 + 기준점 재정렬 도구(`HandPoseRecenterWindow`)(5/19)

### 11-3. 발생 이슈 → 해결방법
**패스스루 침대 반투명 (2026-05-19)**: Standard 셰이더를 코드로 `_Mode=3` 설정해도 빌드 variant 문제로 backbuffer alpha 전달 실패 → **에디터에서 미리 `Fade` 모드 + Albedo Alpha 0.5로 설정**해야 빌드에서 동작

---

## 12. 시나리오 CSV 패턴 정립 및 단계별 대응

### 12-1. 필요 기능
5개 시나리오(상부승모근/견갑거근/사각근/흉쇄유돌근/대흉근)의 단계 구조를 CSV로 일관되게 표현.

### 12-2. 발생 이슈 → 해결방법

**phase 빈값 자동 숨김 (2026-04-28)**: phase 컬럼은 부위 구분(전부/중부/후부)이 있을 때만 사용, 그 외 빈값. `ScenarioCSVLoader` lastPhase 폴백 제거 + UI/결과표 빈값 숨김

**PassiveStretch 도입 (2026-04-20, 흉쇄유돌근)**: 보조수 접촉으로 환자 애니메이션 재생 게이팅하는 신규 conditionType. 접촉 이탈 시 speed=0 + elapsed 정지, 애니메이션 완료=단계 완료 (유사도 평가 없음)

**양손 회전 모드 (2026-04-20)**: rotation substep은 "양손으로 잡고 돌리기"로 통일 → 보조수 드리프트 체크 자동 비활성화. CSV 무수정(`movementType`으로 자동 판정)

**견갑거근 자세지시 (2026-04-06)**: 복합 3D 회전을 `Quaternion.Angle`(180°)로 목표 계산 vs `SignedAngle`(27°)로 측정 → 단위 불일치로 15%만 진행. `movementType` 빈값으로 변경(가이드만 표시)

**Method B 불가 확정 (2026-04-08)**: 건측회전 엄지 아래 밀기는 손목 forward가 detection axis와 평행 → 수치 불안정, 구조적 불가. Method A만 표준 지원

---

## 13. 코드 품질 / 리팩터링

- **ChunaPathEvaluator 리팩터링 (4/2)**: 2780→2190줄. `ChunaDataLoader`(CSV 로딩/좌표 변환), `CollisionDetectionManager`(충돌 감지) 분리, `EvaluationModeConfigurator` 확장
- **죽은 코드 정리 (4/16)**: 참조 0인 스크립트 10개 + `ChunaLimitData.asset` 삭제, EditorBuildSettings 고스트 씬 제거
  - **교훈**: 파일 삭제 전 검사는 파일명 대표 클래스뿐 아니라 **파일 내 모든 타입 정의**(enum/class/struct)에 대해 참조 검사 필요 (`ChunaLimitData` 삭제 후 내부 `LimitStatus` enum 미탐지로 컴파일 에러 → 추출 복구)
- **결과 데이터 일원화**: ChunaFeedbackUI/ChunaResultSummaryUI 제거, `GetGradeFromScore` 통합(4/2)
- **AutoPlay 완료 대기 구현 (4/2)**: 나래이션 짧거나 없는 난이도에서 환자 애니메이션 잘림 방지

---

## 14. 핵심 개발 교훈 정리

1. **에디터(Quest Link) vs 빌드 단독 실행 차이**: OVR 트래킹 활성화 타이밍 지연(1~5초)이 "에디터에선 안 되는데 빌드에선 됨" 증상의 흔한 원인
2. **필드 추가 후 기존 에셋 재직렬화 필수**: 구버전 asset은 신규 필드를 조용히 null로 역직렬화
3. **빌드 설정과 Android 권한 자동주입**: 코드 불변이어도 빌드 설정 변화로 권한 신규 주입 가능 → 머지 매니페스트 diff 검증 상시화
4. **메인 스레드 블로킹 = Quest ANR**: 네트워크 동기 호출 전 도달성 프로브로 차단 (스레드풀 + 외부 CTS 타임아웃)
5. **좌표계 일관성**: 상대값(진행도) vs 절대값(범위 마커), 로컬 vs 월드, 베이크 vs 라이브 — 혼용 시 어긋남. 단일 좌표계 통일이 근본 해결
6. **버그 스코프 정밀 표현**: "원래도 됐던 것" vs "이번 수정의 효과"를 구분해서 기록 (회귀 검증과 실제 개선 가치 분리)

---

> 본 노트는 프로젝트 내부 changelog(2026-03 ~ 06)를 기반으로 재구성되었습니다. file:line 인용 및 코드 동작은 작성 시점 기준이며, 최종 보고 전 현재 코드와 대조 권장.
