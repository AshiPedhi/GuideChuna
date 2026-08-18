using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 훈련 결과 추적 매니저
/// ScenarioManager 및 ChunaPathEvaluator와 연동하여 결과 데이터를 수집
///
/// 기능:
/// - 각 SubStep의 완료/스킵 상태 추적
/// - 유사도 평균값 기록
/// - 경고 횟수 (리밋 범위 초과) 카운트
/// - 경고음 시스템 (홀드 범위 접근 시 빨라지는 비프, 초과 시 경고음)
/// </summary>
public class TrainingResultTracker : MonoBehaviour
{
    [Header("=== 참조 ===")]
    [Tooltip("ChunaPathEvaluator 참조 (자동 찾기)")]
    [SerializeField] private ChunaPathEvaluator pathEvaluator;

    [Header("=== 타이머 설정 ===")]
    [Tooltip("SubStep 스킵 판정 시간 (초)")]
    [SerializeField] private float skipTimeThreshold = 20f;

    [Header("=== 효과음 설정 ===")]
    [Tooltip("접근 비프 전용 AudioSource")]
    [SerializeField] private AudioSource warningAudioSource;

    [Tooltip("한계 초과/홀드 완료 전용 AudioSource (접근 비프와 분리 — 소리 겹침/pitch 영향 방지)")]
    [SerializeField] private AudioSource exceededAudioSource;

    [Tooltip("접근 경고 비프음 클립")]
    [SerializeField] private AudioClip approachBeepClip;

    [Tooltip("한계 초과 경고음 클립")]
    [SerializeField] private AudioClip limitExceededClip;

    [Tooltip("홀드 완료 효과음 (딩동)")]
    [SerializeField] private AudioClip holdCompleteClip;

    [Tooltip("효과음 활성화")]
    [SerializeField] private bool enableWarningAudio = true;

    [Tooltip("최소 비프 간격 (초) — 적정범위 끝(midHoldEnd)에 가까워졌을 때. 클립 길이보다 크게 유지 권장")]
    [SerializeField] private float minBeepInterval = 0.15f;

    [Tooltip("최대 비프 간격 (초) — 적정범위 진입 시(midHoldStart)")]
    [SerializeField] private float maxBeepInterval = 0.8f;

    [Tooltip("끝 접근 시 pitch 상승 최댓값 (1.0 고정이면 1.0)")]
    [Range(1f, 2f)]
    [SerializeField] private float maxApproachPitch = 1.5f;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = true;

    // 결과 데이터
    private TrainingResultData resultData;

    // 현재 추적 상태
    private bool isTracking = false;
    private string currentPhaseName = "";
    private string currentStepName = "";
    private float subStepStartTime = 0f;

    // 유사도 추적
    private float accumulatedSimilarity = 0f;
    private int similaritySampleCount = 0;

    // 경고음 관련
    private Coroutine approachBeepCoroutine;
    private bool isInApproachZone = false;
    private bool isOverLimit = false;
    private float currentApproachRatio = 0f;  // 접근 비율 (0~1, 1에 가까울수록 한계)
    private float lastBeepTime = 0f;

    // 이벤트
    public event Action<TrainingResultData> OnTrainingCompleted;
    public event Action<string, string> OnWarningRecorded;  // phaseName, stepName

    void Awake()
    {
        if (pathEvaluator == null)
            pathEvaluator = FindFirstObjectByType<ChunaPathEvaluator>();

        if (warningAudioSource == null)
            warningAudioSource = GetComponent<AudioSource>();

        // AudioSource 자동 생성
        if (warningAudioSource == null)
        {
            warningAudioSource = gameObject.AddComponent<AudioSource>();
            warningAudioSource.playOnAwake = false;
            warningAudioSource.spatialBlend = 0f;  // 2D 사운드
        }

        if (exceededAudioSource == null)
        {
            exceededAudioSource = gameObject.AddComponent<AudioSource>();
            exceededAudioSource.playOnAwake = false;
            exceededAudioSource.spatialBlend = 0f;
        }

        // 초기화 상태 로그
        if (showDebugLogs)
        {
            ChunaLogger.Log($"<color=cyan>[TrainingResultTracker] Awake 초기화</color>");
            ChunaLogger.Log($"  - pathEvaluator: {(pathEvaluator != null ? "✓" : "✗ NULL")}");
            ChunaLogger.Log($"  - warningAudioSource: {(warningAudioSource != null ? "✓" : "✗ NULL")}");
            ChunaLogger.Log($"  - approachBeepClip: {(approachBeepClip != null ? "✓" : "✗ 미할당")}");
            ChunaLogger.Log($"  - holdCompleteClip: {(holdCompleteClip != null ? "✓" : "✗ 미할당")}");
            ChunaLogger.Log($"  - enableWarningAudio: {enableWarningAudio}");
        }
    }

    void Start()
    {
        SubscribeEvents();

        if (showDebugLogs && pathEvaluator == null)
        {
            ChunaLogger.LogWarning("<color=orange>[TrainingResultTracker] pathEvaluator가 없어서 이벤트를 구독할 수 없습니다!</color>");
        }
    }

    void OnDestroy()
    {
        UnsubscribeEvents();
        StopAllCoroutines();
    }

    void Update()
    {
        if (!ShouldPlayWarning()) return;
        CheckApproachWarning();
    }

    // ========== 이벤트 구독 ==========

    private void SubscribeEvents()
    {
        if (pathEvaluator != null)
        {
            pathEvaluator.OnLimitWarning += HandleLimitWarning;
            pathEvaluator.OnUserFrameChanged += HandleUserFrameChanged;
            pathEvaluator.OnSimilarityUpdated += HandleSimilarityUpdated;
            pathEvaluator.OnEvaluationCompleted += HandleEvaluationCompleted;
            pathEvaluator.OnStartHoldComplete += HandleHoldComplete;
            pathEvaluator.OnMidHoldComplete += HandleHoldComplete;
        }
    }

    private void UnsubscribeEvents()
    {
        if (pathEvaluator != null)
        {
            pathEvaluator.OnLimitWarning -= HandleLimitWarning;
            pathEvaluator.OnUserFrameChanged -= HandleUserFrameChanged;
            pathEvaluator.OnSimilarityUpdated -= HandleSimilarityUpdated;
            pathEvaluator.OnEvaluationCompleted -= HandleEvaluationCompleted;
            pathEvaluator.OnStartHoldComplete -= HandleHoldComplete;
            pathEvaluator.OnMidHoldComplete -= HandleHoldComplete;
        }
    }

    // ========== 이벤트 핸들러 ==========

    /// <summary>
    /// 리밋 경고 핸들러
    /// </summary>
    private void HandleLimitWarning(float ratio)
    {
        // 충돌체 감지(isTracking) 상태일 때만 경고 처리
        if (!isTracking) return;

        // 경고 횟수 증가 (통계 기록은 ShouldPlayWarning 무관하게 진행). 가이드 step은 제외
        if (resultData != null && !string.IsNullOrEmpty(currentPhaseName) && !string.IsNullOrEmpty(currentStepName) && !IsGuideStepName(currentStepName))
        {
            resultData.RecordWarning(currentPhaseName, currentStepName);

            if (showDebugLogs)
                ChunaLogger.Log($"<color=red>[TrainingResultTracker] 경고 기록: {currentPhaseName}/{currentStepName} (비율: {ratio:P0})</color>");

            OnWarningRecorded?.Invoke(currentPhaseName, currentStepName);
        }

        // 한계 초과 경고음 — guideOnly/skipMidHold 단계에선 차단
        if (!isOverLimit && ShouldPlayWarning())
        {
            isOverLimit = true;
            PlayLimitExceededSound();
        }
    }

    /// <summary>
    /// 사용자 프레임 변경 핸들러 (접근 경고 체크용)
    /// </summary>
    private void HandleUserFrameChanged(int currentFrame, int totalFrames, float ratio)
    {
        // guideOnly/skipMidHold 단계에선 접근 경고 자체를 발생시키지 않음
        if (!ShouldPlayWarning()) return;
        UpdateApproachRatio(ratio);
    }

    /// <summary>
    /// 유사도 업데이트 핸들러
    /// </summary>
    private void HandleSimilarityUpdated(float leftSimilarity, float rightSimilarity)
    {
        if (!isTracking) return;

        // 평균 유사도 누적 (두 손 평균)
        float avgSimilarity = (leftSimilarity + rightSimilarity) / 2f;
        accumulatedSimilarity += avgSimilarity;
        similaritySampleCount++;
    }

    /// <summary>
    /// 평가 완료 핸들러
    /// </summary>
    private void HandleEvaluationCompleted(ChunaPathEvaluator.EvaluationSession session)
    {
        // 현재 SubStep 완료 처리
        if (isTracking && !string.IsNullOrEmpty(currentStepName))
        {
            RecordSubStepCompletion(true, false);
        }

        // EvaluationSession 스코어링 결과를 TrainingResultData에 반영. 가이드 step은 제외
        if (isTracking && resultData != null && session != null
            && !string.IsNullOrEmpty(currentPhaseName) && !string.IsNullOrEmpty(currentStepName) && !IsGuideStepName(currentStepName))
        {
            var step = resultData.GetOrCreateStepResult(currentPhaseName, currentStepName);
            step.finalScore = session.finalScore;
            step.grade = session.grade;
            step.limitViolationCount = session.limitViolationCount;
            step.totalTimeInWarning = session.totalTimeInWarning;
            step.totalTimeExceeded = session.totalTimeExceeded;

            // 좌/우 개별 유사도
            step.leftAverageSimilarity = session.leftAverageSimilarity;
            step.rightAverageSimilarity = session.rightAverageSimilarity;

            // 유사도 안정성
            step.similarityStdDev = session.similarityStdDev;
            step.minSimilarity = session.minSimilarity;
            step.maxSimilarity = session.maxSimilarity;

            // 안전성
            step.peakExceededRatio = session.peakExceededRatio;

            // 시계열 유사도 데이터 (웹뷰 그래프용)
            step.similarityTimeline = new System.Collections.Generic.List<TrainingResultData.SimilarityTimePoint>();
            foreach (var snapshot in session.metricsHistory)
            {
                step.similarityTimeline.Add(new TrainingResultData.SimilarityTimePoint
                {
                    time = snapshot.timestamp,
                    leftSimilarity = snapshot.leftSimilarity,
                    rightSimilarity = snapshot.rightSimilarity
                });
            }

            // session의 가중 평균 유사도로 덮어쓰기 (자체 누적값보다 정확)
            if (session.averageSimilarity > 0f)
                step.averageSimilarity = session.averageSimilarity;

            if (showDebugLogs)
                ChunaLogger.Log($"<color=green>[TrainingResultTracker] 스코어링 결과 저장: {currentStepName} → {session.finalScore:F0}점 ({session.grade}) | L:{session.leftAverageSimilarity:P0} R:{session.rightAverageSimilarity:P0} σ:{session.similarityStdDev:F3}</color>");
        }

        // 경고음 중지
        StopApproachBeep();
        isOverLimit = false;
    }

    /// <summary>
    /// 홀드 완료 핸들러 (시작 홀드 / 중간 홀드 완료 시)
    /// </summary>
    private void HandleHoldComplete()
    {
        // 경고음 중지 (추적 여부와 관계없이)
        StopApproachBeep();
        isOverLimit = false;
        isInApproachZone = false;

        // 홀드 완료 효과음 재생 (추적 여부와 관계없이)
        PlayHoldCompleteSound();

        if (showDebugLogs)
            ChunaLogger.Log("<color=green>[TrainingResultTracker] 홀드 완료 - 딩동!</color>");
    }

    // ========== 접근 경고 시스템 ==========

    /// <summary>
    /// 접근 비율 업데이트 (경고음 제어용)
    /// </summary>
    private void UpdateApproachRatio(float frameRatio)
    {
        if (pathEvaluator == null) return;

        // 적정범위 (midHoldStart ~ midHoldEnd) 기준:
        //   - 구간 진입 시 비프 시작, 끝(midHoldEnd)에 가까울수록 interval↓ pitch↑
        //   - midHoldEnd 초과는 OnLimitWarning이 처리
        float holdStart = pathEvaluator.CurrentMidHoldStart;
        float holdEnd = pathEvaluator.CurrentMidHoldEnd;

        if (frameRatio >= holdStart && frameRatio < holdEnd)
        {
            // 적정범위 안 — 지속 비프, 끝에 가까울수록 긴박감 상승
            isInApproachZone = true;
            isOverLimit = false;
            float span = Mathf.Max(0.001f, holdEnd - holdStart);
            currentApproachRatio = Mathf.Clamp01((frameRatio - holdStart) / span);
        }
        else if (frameRatio >= holdEnd)
        {
            // 적정범위 초과 — 비프 중지 (초과 경고는 HandleLimitWarning에서)
            isInApproachZone = false;
            currentApproachRatio = 1f;
        }
        else
        {
            // 적정범위 진입 전 — 무음, 초과 플래그도 해제
            isInApproachZone = false;
            isOverLimit = false;
            currentApproachRatio = 0f;
        }
    }

    /// <summary>
    /// 접근 경고 체크 및 비프음 재생
    /// </summary>
    private void CheckApproachWarning()
    {
        if (!enableWarningAudio) return;

        if (isOverLimit)
        {
            // 한계 초과 시 경고음 재생 (아직 안 울렸으면)
            // HandleLimitWarning에서 한 번만 울리도록 처리됨
            StopApproachBeep();
            return;
        }

        if (isInApproachZone)
        {
            // 접근 구간: 비프 간격 계산 (가까울수록 빠름)
            float beepInterval = Mathf.Lerp(maxBeepInterval, minBeepInterval, currentApproachRatio);

            if (Time.time - lastBeepTime >= beepInterval)
            {
                PlayApproachBeep();
                lastBeepTime = Time.time;

                if (showDebugLogs && Time.frameCount % 30 == 0)
                    ChunaLogger.Log($"<color=yellow>[TrainingResultTracker] 접근 경고 비프 (비율: {currentApproachRatio:P0}, 간격: {beepInterval:F2}s)</color>");
            }
        }
        else
        {
            // 안전 구간: 비프 중지
            StopApproachBeep();
        }
    }

    /// <summary>
    /// 접근 비프음 재생
    /// </summary>
    private void PlayApproachBeep()
    {
        if (!enableWarningAudio || !IsFeedbackSoundEnabled()) return;
        if (warningAudioSource == null || approachBeepClip == null) return;

        // 끝에 가까울수록 pitch 상승 (날카로운 느낌). minBeepInterval을 클립 길이보다 크게 유지하여 겹침 방지
        warningAudioSource.pitch = Mathf.Lerp(1f, maxApproachPitch, currentApproachRatio);
        warningAudioSource.PlayOneShot(approachBeepClip, 0.5f + currentApproachRatio * 0.5f);
    }

    /// <summary>
    /// 접근 비프음 중지
    /// </summary>
    private void StopApproachBeep()
    {
        if (approachBeepCoroutine != null)
        {
            StopCoroutine(approachBeepCoroutine);
            approachBeepCoroutine = null;
        }
    }

    /// <summary>
    /// 한계 초과 경고음 재생
    /// </summary>
    private void PlayLimitExceededSound()
    {
        if (!enableWarningAudio || !IsFeedbackSoundEnabled()) return;

        // 접근 비프와 분리된 AudioSource 사용 (pitch/겹침 영향 차단)
        var src = exceededAudioSource != null ? exceededAudioSource : warningAudioSource;
        if (src == null) return;

        if (limitExceededClip != null)
        {
            src.pitch = 1f;
            src.PlayOneShot(limitExceededClip, 1f);
        }
        else if (approachBeepClip != null)
        {
            // limitExceededClip이 없으면 approachBeepClip을 큰 소리로 재생
            src.pitch = 1.2f;
            src.PlayOneShot(approachBeepClip, 1f);
        }

        if (showDebugLogs)
            ChunaLogger.Log("<color=red>[TrainingResultTracker] 한계 초과 경고음!</color>");
    }

    /// <summary>
    /// 홀드 완료 효과음 재생 (딩동)
    /// </summary>
    private void PlayHoldCompleteSound()
    {
        if (!enableWarningAudio || !IsFeedbackSoundEnabled()) return;

        // 접근 비프와 분리된 AudioSource 사용
        var src = exceededAudioSource != null ? exceededAudioSource : warningAudioSource;
        if (src == null) return;

        if (holdCompleteClip != null)
        {
            src.pitch = 1f;
            src.PlayOneShot(holdCompleteClip, 1f);
        }
        else if (approachBeepClip != null)
        {
            // holdCompleteClip이 없으면 approachBeepClip을 높은 음으로 2번 재생
            StartCoroutine(PlayDoubleBeep());
        }
    }

    /// <summary>
    /// 홀드 완료 대체 효과음 (비프 2번)
    /// </summary>
    private IEnumerator PlayDoubleBeep()
    {
        var src = exceededAudioSource != null ? exceededAudioSource : warningAudioSource;
        if (src == null || approachBeepClip == null) yield break;

        src.pitch = 1.5f;
        src.PlayOneShot(approachBeepClip, 0.8f);
        yield return new WaitForSeconds(0.15f);
        src.pitch = 1.8f;
        src.PlayOneShot(approachBeepClip, 0.8f);
    }

    // ========== Public API ==========

    /// <summary>
    /// 훈련 시작 (모드 선택 후 호출)
    /// </summary>
    public void StartTracking(string mode, string difficulty, string scenarioName = "")
    {
        resultData = new TrainingResultData();
        resultData.selectedMode = mode;
        resultData.selectedDifficulty = difficulty;
        resultData.scenarioName = scenarioName;

        // 사용자 정보 (PlayerPrefs에서 로드)
        var loginStore = new LoginStateStore();
        var loginInfo = loginStore.LoadLoginInfo();
        if (loginInfo.hasData)
        {
            resultData.userName = loginInfo.username;
            resultData.userId = loginInfo.userID;
        }

        var dm = ChunaTraining.DifficultyManager.Instance;
        resultData.isOfficialEvaluation = dm != null && dm.IsOfficialScore;
        resultData.isPreEvaluation = dm != null && dm.IsPreEvaluationMode;

        // 시도 횟수: 시나리오+모드(평가/연습)별 회차를 PlayerPrefs에서 증가시켜 기록
        resultData.attemptNumber = TrainingAttemptStore.IncrementAndGet(scenarioName, resultData.isOfficialEvaluation);

        // ★ 시간 무제한 모드 시 스킵 판정 비활성화 (매우 큰 값)
        if (dm != null && dm.UnlimitedTime)
            skipTimeThreshold = float.MaxValue;

        isTracking = true;
        currentPhaseName = "";
        currentStepName = "";

        // 경고음 상태 초기화
        isInApproachZone = false;
        isOverLimit = false;
        currentApproachRatio = 0f;

        if (showDebugLogs)
            ChunaLogger.Log($"<color=green>[TrainingResultTracker] 훈련 추적 시작: {mode} / {difficulty}</color>");
    }

    /// <summary>
    /// Phase 시작 (전부/중부/후부)
    /// </summary>
    public void StartPhase(string phaseName)
    {
        if (!isTracking || resultData == null) return;

        currentPhaseName = phaseName;
        resultData.GetOrCreatePhaseResult(phaseName);

        if (showDebugLogs)
            ChunaLogger.Log($"<color=cyan>[TrainingResultTracker] Phase 시작: {phaseName}</color>");
    }

    // 가이드 step(stepNo=0, stepName="가이드")은 시작/종료 버튼 안내일 뿐 평가 대상이 아니므로
    // 결과(유사도/점수/경고)에서 제외 — StepResult 자체를 만들지 않아 종합 유사도/learnLevel2/CSV/결과표에서 자동 제외됨
    private const string GuideStepName = "가이드";
    private static bool IsGuideStepName(string stepName) => stepName == GuideStepName;

    /// <summary>
    /// Step 시작
    /// </summary>
    public void StartStep(string stepName)
    {
        if (!isTracking || resultData == null) return;

        currentStepName = stepName;

        // 가이드 step은 결과 기록 제외 (StepResult 미생성)
        if (IsGuideStepName(stepName))
        {
            if (showDebugLogs)
                ChunaLogger.Log($"<color=grey>[TrainingResultTracker] 가이드 step 결과 제외: {stepName}</color>");
            return;
        }

        resultData.GetOrCreateStepResult(currentPhaseName, stepName);

        if (showDebugLogs)
            ChunaLogger.Log($"<color=cyan>[TrainingResultTracker] Step 시작: {stepName}</color>");
    }

    /// <summary>
    /// SubStep 시작
    /// </summary>
    public void StartSubStep(string phaseName, string stepName)
    {
        if (!isTracking) return;

        // 이전 SubStep 완료 처리
        if (!string.IsNullOrEmpty(currentStepName))
        {
            // 이전 SubStep이 있었으면 스킵 여부 체크
            float elapsed = Time.time - subStepStartTime;
            bool skipped = elapsed >= skipTimeThreshold;
            RecordSubStepCompletion(!skipped, skipped);
        }

        currentPhaseName = phaseName;
        currentStepName = stepName;
        subStepStartTime = Time.time;

        // 유사도 추적 초기화
        accumulatedSimilarity = 0f;
        similaritySampleCount = 0;

        // 경고 상태 초기화
        isOverLimit = false;

        if (showDebugLogs)
            ChunaLogger.Log($"<color=cyan>[TrainingResultTracker] SubStep 시작: {phaseName}/{stepName}</color>");
    }

    /// <summary>
    /// SubStep 완료 기록
    /// </summary>
    public void RecordSubStepCompletion(bool completed, bool skipped)
    {
        if (!isTracking || resultData == null) return;
        if (string.IsNullOrEmpty(currentPhaseName) || string.IsNullOrEmpty(currentStepName)) return;

        // 가이드 step은 결과 기록 제외 (유사도 누적만 비우고 반환)
        if (IsGuideStepName(currentStepName))
        {
            accumulatedSimilarity = 0f;
            similaritySampleCount = 0;
            return;
        }

        // 평균 유사도 계산
        float avgSimilarity = similaritySampleCount > 0 ? accumulatedSimilarity / similaritySampleCount : 0f;

        // 결과 데이터에 기록
        resultData.RecordSubStepCompletion(currentPhaseName, currentStepName, completed, skipped, avgSimilarity);

        // Step 소요 시간 갱신
        var step = resultData.GetOrCreateStepResult(currentPhaseName, currentStepName);
        step.totalTime += Time.time - subStepStartTime;

        if (showDebugLogs)
        {
            string status = skipped ? "스킵" : "완료";
            ChunaLogger.Log($"<color=yellow>[TrainingResultTracker] SubStep {status}: {currentStepName} (유사도: {avgSimilarity:P0})</color>");
        }

        // 초기화
        accumulatedSimilarity = 0f;
        similaritySampleCount = 0;
    }

    /// <summary>
    /// SubStep 스킵 기록
    /// </summary>
    public void RecordSubStepSkip()
    {
        RecordSubStepCompletion(false, true);
    }

    /// <summary>
    /// ★두개골 술기 단계의 지표를 현재 Step에 기록한다.
    ///
    /// 두개골 단계는 손 포즈 유사도·각도 리밋을 쓰지 않아 기존 경로로는 점수가 0으로만 남는다
    /// (유사도 샘플은 평가 phase가 Moving/MidHold일 때만 쌓이고, OnEvaluationCompleted도 오지 않는다).
    /// 그래서 컨트롤러가 모은 '자세 성립·유지' 지표를 여기서 직접 넣고, 점수·등급도 같이 채운다.
    ///
    /// 호출 시점 = 다음 SubStep의 <see cref="StartSubStep"/> **직전**(그래야 방금 끝난 단계에 붙는다).
    /// </summary>
    public void RecordCranialStep(TrainingResultData.CranialMetrics metrics)
    {
        if (!isTracking || resultData == null || metrics == null) return;

        // ★지표를 모으기 시작한 시점의 국면·단계에 붙인다. 기록은 '다음 substep 진입' 때 일어나므로
        //   여기서 currentPhaseName/currentStepName을 읽으면 한 칸 뒤 단계에 붙는다
        //   (PJ에서 평가는 점수가 없고 재평가에만 점수가 나오던 원인 — 2026-08-12).
        string phase = !string.IsNullOrEmpty(metrics.phaseName) ? metrics.phaseName : currentPhaseName;
        string stepName = !string.IsNullOrEmpty(metrics.stepName) ? metrics.stepName : currentStepName;

        if (string.IsNullOrEmpty(phase) || string.IsNullOrEmpty(stepName)) return;
        if (IsGuideStepName(stepName)) return;

        var step = resultData.GetOrCreateStepResult(phase, stepName);

        // 같은 Step에 여러 SubStep이 있으면(예: 진단1·진단2) 마지막 것만 남지 않게 누적한다.
        if (step.cranial == null)
        {
            step.cranial = metrics;
        }
        else
        {
            var c = step.cranial;
            c.elapsedSeconds += metrics.elapsedSeconds;
            c.posesRequired += metrics.posesRequired;
            c.posesCompleted += metrics.posesCompleted;
            c.holdSeconds += metrics.holdSeconds;
            c.gripDropouts += metrics.gripDropouts;
            c.holdResets += metrics.holdResets;
            c.breathsRequired += metrics.breathsRequired;
            c.breathsCompleted += metrics.breathsCompleted;
            c.breathFailures += metrics.breathFailures;
            c.breathGripDropouts += metrics.breathGripDropouts;
            c.earlyThrusts += metrics.earlyThrusts;
            c.lateThrusts += metrics.lateThrusts;
            c.postureSeconds += metrics.postureSeconds;
            // 손모양 유사도 — 표본 수로 가중 평균한다(단순 평균을 내면 표본 1개짜리 단계가 과대평가된다).
            int total = c.similaritySamples + metrics.similaritySamples;
            if (total > 0)
                c.poseSimilarity = (c.poseSimilarity * c.similaritySamples
                                    + metrics.poseSimilarity * metrics.similaritySamples) / total;
            c.similaritySamples = total;
            if (c.firstContactSeconds < 0f) c.firstContactSeconds = metrics.firstContactSeconds;
            if (metrics.breathHoldRatio > 0f) c.breathHoldRatio = metrics.breathHoldRatio;
            // 점수는 누적값으로 다시 계산한다
            c.score = (metrics.score + c.score) * 0.5f;
            c.grade = EvaluationScoringEngine.GetGradeFromScore(c.score);
        }

        // 기존 결과 UI가 읽는 점수·등급도 채워 준다(비워 두면 0점/F로 보인다).
        step.finalScore = step.cranial.score;
        step.grade = step.cranial.grade;

        // ★유사도도 채운다 — 두개골 계열은 HandPose 평가 파이프라인을 안 타서 averageSimilarity가
        //   줄곧 0이었고, 결과표에 '유사도 0%'로 찍혔다(사용자 지적 2026-08-18).
        //   이제 가이드 클립 마지막 프레임과의 손모양 유사도를 그 칸에 넣는다(0~1, UI는 :P0로 찍는다).
        //   ★표본이 없으면(제2늑골처럼 유지형이 아닌 술기·클립 없는 단계) 건드리지 않는다 —
        //     0을 덮어써서 '0%'로 보이게 만드느니, 채점에서 빠진 것과 같이 비워 두는 편이 정직하다.
        if (step.cranial.similaritySamples > 0)
            step.averageSimilarity = Mathf.Clamp01(step.cranial.poseSimilarity);

        if (showDebugLogs)
            ChunaLogger.Log($"<color=yellow>[TrainingResultTracker] 두개골 지표 기록: {phase}/{stepName} " +
                             $"자세 {step.cranial.posesCompleted}/{step.cranial.posesRequired} " +
                             $"유지 {step.cranial.holdSeconds:F1}s 이탈 {step.cranial.gripDropouts}회 " +
                             $"→ {step.cranial.score:F0}점({step.cranial.grade})</color>");
    }

    /// <summary>
    /// 훈련 종료
    /// </summary>
    /// <param name="completed">정상 완주 여부. false면 중도 종료(미완료)로 기록 — 정식 점수와 분리됨.</param>
    public TrainingResultData FinishTracking(bool completed = true)
    {
        if (!isTracking || resultData == null) return null;

        resultData.isCompleted = completed;

        // 현재 진행 중인 SubStep 완료 처리
        if (!string.IsNullOrEmpty(currentStepName))
        {
            float elapsed = Time.time - subStepStartTime;
            bool skipped = elapsed >= skipTimeThreshold;
            RecordSubStepCompletion(!skipped, skipped);
        }

        // 최종 통계 계산
        resultData.FinalizeResult();

        isTracking = false;

        // 경고음 정리
        StopApproachBeep();

        if (showDebugLogs)
            ChunaLogger.Log($"<color=green>[TrainingResultTracker] 훈련 추적 종료\n{resultData}</color>");

        OnTrainingCompleted?.Invoke(resultData);

        return resultData;
    }

    /// <summary>
    /// 현재 결과 데이터 가져오기
    /// </summary>
    public TrainingResultData GetResultData()
    {
        return resultData;
    }

    /// <summary>
    /// 추적 중인지 여부
    /// </summary>
    public bool IsTracking => isTracking;

    /// <summary>
    /// 현재 추적 세션이 공식 평가인지 여부 (중도 종료 시 미완료 기록 대상 판단용)
    /// </summary>
    public bool IsOfficialEvaluation => resultData != null && resultData.isOfficialEvaluation;

    /// <summary>
    /// 현재 Phase 이름
    /// </summary>
    public string CurrentPhaseName => currentPhaseName;

    /// <summary>
    /// 현재 Step 이름
    /// </summary>
    public string CurrentStepName => currentStepName;

    /// <summary>
    /// PathEvaluator 설정
    /// </summary>
    public void SetPathEvaluator(ChunaPathEvaluator evaluator)
    {
        UnsubscribeEvents();
        pathEvaluator = evaluator;
        SubscribeEvents();
    }

    /// <summary>
    /// 난이도 프리셋의 PlayFeedbackSound 설정 확인
    /// </summary>
    private bool IsFeedbackSoundEnabled()
    {
        var dm = ChunaTraining.DifficultyManager.Instance;
        return dm == null || dm.PlayFeedbackSound;
    }

    /// <summary>
    /// 현재 단계에서 경고음(접근 비프/초과 경고)을 재생해야 하는지 판정
    /// - guideOnly: 시각 데모 전용 (손 포즈/애니메이션만 재생, 판정 없음) → 경고음 차단
    /// - skipMidHold: 직선 가동범위 모드 (홀드 판정 없음, 대흉근/흉쇄유돌근 등) → 경고음 차단
    /// 추적(isTracking) 중이고 위 두 모드가 아닌 경우에만 경고음 활성화
    /// </summary>
    private bool ShouldPlayWarning()
    {
        if (!isTracking) return false;
        if (pathEvaluator == null) return false;
        if (pathEvaluator.IsGuideOnlyMode) return false;
        if (pathEvaluator.IsSkipMidHold) return false;
        return true;
    }

    /// <summary>
    /// 경고음 활성화/비활성화
    /// </summary>
    public void SetWarningAudioEnabled(bool enabled)
    {
        enableWarningAudio = enabled;
        if (!enabled)
        {
            StopApproachBeep();
        }
    }

    /// <summary>
    /// 스킵 판정 시간 설정
    /// </summary>
    public void SetSkipTimeThreshold(float seconds)
    {
        skipTimeThreshold = Mathf.Max(1f, seconds);
    }

    // ========== 경고 수동 기록 ==========

    /// <summary>
    /// 경고 수동 기록 (ChunaPathEvaluator 외부에서 호출 시)
    /// </summary>
    public void RecordWarningManually()
    {
        if (!isTracking || resultData == null) return;
        if (string.IsNullOrEmpty(currentPhaseName) || string.IsNullOrEmpty(currentStepName)) return;
        if (IsGuideStepName(currentStepName)) return; // 가이드 step 제외

        resultData.RecordWarning(currentPhaseName, currentStepName);

        if (showDebugLogs)
            ChunaLogger.Log($"<color=red>[TrainingResultTracker] 경고 수동 기록: {currentPhaseName}/{currentStepName}</color>");

        OnWarningRecorded?.Invoke(currentPhaseName, currentStepName);
    }
}
