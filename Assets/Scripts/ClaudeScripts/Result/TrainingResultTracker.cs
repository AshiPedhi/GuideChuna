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
    [Tooltip("효과음 AudioSource")]
    [SerializeField] private AudioSource warningAudioSource;

    [Tooltip("접근 경고 비프음 클립")]
    [SerializeField] private AudioClip approachBeepClip;

    [Tooltip("한계 초과 경고음 클립")]
    [SerializeField] private AudioClip limitExceededClip;

    [Tooltip("홀드 완료 효과음 (딩동)")]
    [SerializeField] private AudioClip holdCompleteClip;

    [Tooltip("효과음 활성화")]
    [SerializeField] private bool enableWarningAudio = true;

    [Tooltip("비프음 시작 구간 (홀드 끝 범위의 몇 % 전부터)")]
    [Range(0f, 0.3f)]
    [SerializeField] private float beepStartOffset = 0.1f;

    [Tooltip("최소 비프 간격 (초)")]
    [SerializeField] private float minBeepInterval = 0.1f;

    [Tooltip("최대 비프 간격 (초)")]
    [SerializeField] private float maxBeepInterval = 0.8f;

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
        // 경고음은 충돌체 감지(isTracking) 상태일 때만 체크
        if (pathEvaluator == null) return;
        if (!isTracking) return;

        // 현재 프레임 진행률 체크 (경고음용)
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

        // 경고 횟수 증가
        if (resultData != null && !string.IsNullOrEmpty(currentPhaseName) && !string.IsNullOrEmpty(currentStepName))
        {
            resultData.RecordWarning(currentPhaseName, currentStepName);

            if (showDebugLogs)
                ChunaLogger.Log($"<color=red>[TrainingResultTracker] 경고 기록: {currentPhaseName}/{currentStepName} (비율: {ratio:P0})</color>");

            OnWarningRecorded?.Invoke(currentPhaseName, currentStepName);
        }

        // 한계 초과 경고음
        if (!isOverLimit)
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
        // 충돌체 감지(isTracking) 상태일 때만 경고음용 비율 업데이트
        if (!isTracking) return;
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

        // EvaluationSession 스코어링 결과를 TrainingResultData에 반영
        if (isTracking && resultData != null && session != null
            && !string.IsNullOrEmpty(currentPhaseName) && !string.IsNullOrEmpty(currentStepName))
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

        // ChunaPathEvaluator의 홀드 범위 가져오기
        float holdEndRatio = pathEvaluator.CurrentMidHoldEnd;
        float beepStartRatio = holdEndRatio - beepStartOffset;

        if (frameRatio >= holdEndRatio)
        {
            // 한계 초과
            isOverLimit = true;
            isInApproachZone = false;
            currentApproachRatio = 1f;
        }
        else if (frameRatio >= beepStartRatio)
        {
            // 접근 구간
            isOverLimit = false;
            isInApproachZone = true;
            // beepStartRatio~holdEndRatio를 0~1로 정규화
            currentApproachRatio = (frameRatio - beepStartRatio) / beepStartOffset;
        }
        else
        {
            // 안전 구간
            isOverLimit = false;
            isInApproachZone = false;
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

        // 피치 조절 (가까울수록 높은 음)
        warningAudioSource.pitch = Mathf.Lerp(0.8f, 1.5f, currentApproachRatio);
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
        if (!enableWarningAudio || !IsFeedbackSoundEnabled() || warningAudioSource == null) return;

        if (limitExceededClip != null)
        {
            warningAudioSource.pitch = 1f;
            warningAudioSource.PlayOneShot(limitExceededClip, 1f);
        }
        else if (approachBeepClip != null)
        {
            // limitExceededClip이 없으면 approachBeepClip을 큰 소리로 재생
            warningAudioSource.pitch = 1.2f;
            warningAudioSource.PlayOneShot(approachBeepClip, 1f);
        }

        if (showDebugLogs)
            ChunaLogger.Log("<color=red>[TrainingResultTracker] 한계 초과 경고음!</color>");
    }

    /// <summary>
    /// 홀드 완료 효과음 재생 (딩동)
    /// </summary>
    private void PlayHoldCompleteSound()
    {
        if (!enableWarningAudio || !IsFeedbackSoundEnabled() || warningAudioSource == null) return;

        if (holdCompleteClip != null)
        {
            warningAudioSource.pitch = 1f;
            warningAudioSource.PlayOneShot(holdCompleteClip, 1f);
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
        if (warningAudioSource == null || approachBeepClip == null) yield break;

        warningAudioSource.pitch = 1.5f;
        warningAudioSource.PlayOneShot(approachBeepClip, 0.8f);
        yield return new WaitForSeconds(0.15f);
        warningAudioSource.pitch = 1.8f;
        warningAudioSource.PlayOneShot(approachBeepClip, 0.8f);
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

    /// <summary>
    /// Step 시작
    /// </summary>
    public void StartStep(string stepName)
    {
        if (!isTracking || resultData == null) return;

        currentStepName = stepName;
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
    /// 훈련 종료
    /// </summary>
    public TrainingResultData FinishTracking()
    {
        if (!isTracking || resultData == null) return null;

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

        resultData.RecordWarning(currentPhaseName, currentStepName);

        if (showDebugLogs)
            ChunaLogger.Log($"<color=red>[TrainingResultTracker] 경고 수동 기록: {currentPhaseName}/{currentStepName}</color>");

        OnWarningRecorded?.Invoke(currentPhaseName, currentStepName);
    }
}
