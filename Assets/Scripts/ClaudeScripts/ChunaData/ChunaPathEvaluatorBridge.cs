using System;
using UnityEngine;

/// <summary>
/// ChunaPathEvaluator와 시나리오 시스템 연결 브릿지
/// - SubStep마다 CSV 로드 → 평가 시작
/// - MidHold 완료 시 OnProgressThresholdReached 이벤트 발생
/// </summary>
[RequireComponent(typeof(ChunaPathEvaluator))]
public class ChunaPathEvaluatorBridge : MonoBehaviour
{
    [Header("=== ChunaPathEvaluator 참조 ===")]
    [Tooltip("자동으로 찾아서 연결됩니다")]
    [SerializeField] private ChunaPathEvaluator pathEvaluator;

    [Header("=== 진행률 추적 설정 ===")]
    [Tooltip("목표 진행률 (0.0~1.0)")]
    [SerializeField] private float progressThreshold = 0.8f;

    [Tooltip("시퀀스 완료 시 이벤트 발생 여부")]
    [SerializeField] private bool enableSequenceCompletedEvent = true;

    [Tooltip("진행률 목표 달성 시 이벤트 발생 여부")]
    [SerializeField] private bool enableProgressThresholdEvent = true;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = true;

    // 이벤트
    public event Action OnSequenceCompleted;
    public event Action OnProgressThresholdReached;

    // 진행률 추적 상태
    private bool hasProgressThresholdBeenReached = false;
    private bool hasSequenceCompleted = false;
    private bool isTracking = false;

    void Awake()
    {
        if (pathEvaluator == null)
            pathEvaluator = GetComponent<ChunaPathEvaluator>();

        if (pathEvaluator == null)
        {
            ChunaLogger.LogError("[ChunaPathEvaluatorBridge] ChunaPathEvaluator를 찾을 수 없습니다!");
        }
        else
        {
            if (showDebugLogs)
                ChunaLogger.Log($"[ChunaPathEvaluatorBridge] ChunaPathEvaluator 연결 완료: {pathEvaluator.name}");

            pathEvaluator.OnEvaluationCompleted += OnEvaluationCompletedHandler;
            pathEvaluator.OnMidHoldComplete += OnMidHoldCompleteHandler;
        }
    }

    void OnDestroy()
    {
        if (pathEvaluator != null)
        {
            pathEvaluator.OnEvaluationCompleted -= OnEvaluationCompletedHandler;
            pathEvaluator.OnMidHoldComplete -= OnMidHoldCompleteHandler;
        }
    }

    // ========== 이벤트 핸들러 ==========

    private void OnEvaluationCompletedHandler(ChunaPathEvaluator.EvaluationSession session)
    {
        if (!enableSequenceCompletedEvent) return;
        if (hasSequenceCompleted) return;

        hasSequenceCompleted = true;

        if (showDebugLogs)
        {
            ChunaLogger.Log($"<color=green>[ChunaPathEvaluatorBridge] ===== 평가 완료! =====</color>");
            ChunaLogger.Log($"  - 평균 유사도: {session.averageSimilarity:P0}");
            ChunaLogger.Log($"  - 최종 점수: {session.finalScore:F0}점 ({session.grade})");
        }

        OnSequenceCompleted?.Invoke();
    }

    private void OnMidHoldCompleteHandler()
    {
        if (!isTracking) return;
        if (!enableProgressThresholdEvent) return;
        if (hasProgressThresholdBeenReached) return;

        hasProgressThresholdBeenReached = true;

        float frameProgress = pathEvaluator != null ? pathEvaluator.GetCurrentProgress() : 0f;

        ChunaLogger.Log($"<color=green>[ChunaPathEvaluatorBridge] ===== MidHold 완료! =====</color>");
        ChunaLogger.Log($"  - 프레임 진행률: {frameProgress * 100:F1}%");

        OnProgressThresholdReached?.Invoke();
    }

    // ========== Public API ==========

    public void LoadFromCSV(string csvFileName)
    {
        if (pathEvaluator == null)
        {
            ChunaLogger.LogError("[ChunaPathEvaluatorBridge] ChunaPathEvaluator가 없습니다!");
            return;
        }

        if (showDebugLogs)
            ChunaLogger.Log($"<color=cyan>[ChunaPathEvaluatorBridge] CSV 로드: {csvFileName}</color>");

        pathEvaluator.LoadAndGenerateCheckpoints(csvFileName);
        pathEvaluator.StartEvaluation();
        StartTracking();
    }

    public void StartTracking()
    {
        isTracking = true;
        hasProgressThresholdBeenReached = false;
        hasSequenceCompleted = false;

        if (showDebugLogs)
            ChunaLogger.Log($"[ChunaPathEvaluatorBridge] 추적 시작 (목표: {progressThreshold * 100:F0}%)");
    }

    public void StopTracking()
    {
        isTracking = false;

        if (pathEvaluator != null && pathEvaluator.IsEvaluating)
            pathEvaluator.StopEvaluation();

        if (showDebugLogs)
            ChunaLogger.Log($"[ChunaPathEvaluatorBridge] 추적 중지");
    }

    public void SetProgressThreshold(float threshold)
    {
        progressThreshold = Mathf.Clamp01(threshold);
    }

    public bool HasReachedProgressThreshold() => hasProgressThresholdBeenReached;
    public bool HasCompletedSequence() => hasSequenceCompleted;

    public void ResetTracking()
    {
        hasProgressThresholdBeenReached = false;
        hasSequenceCompleted = false;

        if (pathEvaluator != null)
            pathEvaluator.ResetEvaluation();

        if (showDebugLogs)
            ChunaLogger.Log($"[ChunaPathEvaluatorBridge] 추적 상태 초기화");
    }

    public ChunaPathEvaluator.EvaluationSession GetCurrentSession() => pathEvaluator?.GetCurrentSession();
    public ChunaPathEvaluator GetEvaluator() => pathEvaluator;
}
