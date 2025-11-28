using System;
using UnityEngine;

/// <summary>
/// ChunaPathEvaluator와 시나리오 시스템 연결 브릿지
/// HandPoseTrainingControllerBridge와 동일한 인터페이스 제공
///
/// 목적:
/// - 체크포인트 기반 추나 시술 평가 시스템과 시나리오 시스템 연동
/// - SubStep마다 CSV를 로드하여 새로운 체크포인트 생성
/// - 모든 체크포인트 통과 시 완료 이벤트 발생
///
/// 사용법:
/// 1. ChunaPathEvaluator가 있는 GameObject에 이 컴포넌트 추가
/// 2. ScenarioManager가 HandleCheckpointTracking에서 LoadFromCSV 호출
/// 3. 체크포인트 통과 시 자동으로 진행률 갱신 및 완료 처리
/// </summary>
[RequireComponent(typeof(ChunaPathEvaluator))]
public class ChunaPathEvaluatorBridge : MonoBehaviour
{
    [Header("=== ChunaPathEvaluator 참조 ===")]
    [Tooltip("자동으로 찾아서 연결됩니다")]
    [SerializeField] private ChunaPathEvaluator pathEvaluator;

    [Header("=== 진행률 추적 설정 ===")]
    [Tooltip("목표 진행률 (0.0~1.0). 이 진행률에 도달하면 OnProgressThresholdReached 이벤트 발생")]
    [SerializeField] private float progressThreshold = 0.8f;

    [Tooltip("시퀀스 완료 시 이벤트 발생 여부")]
    [SerializeField] private bool enableSequenceCompletedEvent = true;

    [Tooltip("진행률 목표 달성 시 이벤트 발생 여부")]
    [SerializeField] private bool enableProgressThresholdEvent = true;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = true;

    // 이벤트 (시나리오 시스템 연동용 - HandPoseTrainingControllerBridge와 동일한 인터페이스)
    public event Action OnSequenceCompleted;
    public event Action OnProgressThresholdReached;

    // 진행률 추적 상태
    private bool hasProgressThresholdBeenReached = false;
    private bool hasSequenceCompleted = false;
    private bool isTracking = false;

    // 이전 진행률 추적
    private float lastProgress = 0f;

    void Awake()
    {
        // ChunaPathEvaluator 자동 찾기
        if (pathEvaluator == null)
        {
            pathEvaluator = GetComponent<ChunaPathEvaluator>();
        }

        if (pathEvaluator == null)
        {
            Debug.LogError("[ChunaPathEvaluatorBridge] ChunaPathEvaluator를 찾을 수 없습니다!");
        }
        else
        {
            if (showDebugLogs)
                Debug.Log($"[ChunaPathEvaluatorBridge] ChunaPathEvaluator 연결 완료: {pathEvaluator.name}");

            // 이벤트 구독
            pathEvaluator.OnEvaluationCompleted += OnEvaluationCompletedHandler;
            pathEvaluator.OnProgressChanged += OnProgressChangedHandler;
            pathEvaluator.OnCheckpointPassed += OnCheckpointPassedHandler;
        }
    }

    void OnDestroy()
    {
        // 이벤트 구독 해제
        if (pathEvaluator != null)
        {
            pathEvaluator.OnEvaluationCompleted -= OnEvaluationCompletedHandler;
            pathEvaluator.OnProgressChanged -= OnProgressChangedHandler;
            pathEvaluator.OnCheckpointPassed -= OnCheckpointPassedHandler;
        }
    }

    // ========== 이벤트 핸들러 ==========

    /// <summary>
    /// 평가 완료 이벤트 핸들러
    /// </summary>
    private void OnEvaluationCompletedHandler(ChunaPathEvaluator.EvaluationSession session)
    {
        if (!enableSequenceCompletedEvent) return;
        if (hasSequenceCompleted) return;  // 중복 방지

        hasSequenceCompleted = true;

        if (showDebugLogs)
        {
            Debug.Log($"<color=green>[ChunaPathEvaluatorBridge] ===== 평가 완료! =====</color>");
            Debug.Log($"  - 통과: {session.passedCheckpoints}/{session.totalCheckpoints}");
            Debug.Log($"  - 평균 유사도: {session.averageSimilarity:P0}");
            Debug.Log($"  - 최종 점수: {session.finalScore:F0}점 ({session.grade})");
        }

        OnSequenceCompleted?.Invoke();
    }

    /// <summary>
    /// 진행률 변경 이벤트 핸들러
    /// </summary>
    private void OnProgressChangedHandler(int current, int total)
    {
        if (!isTracking) return;

        float progress = total > 0 ? (float)current / total : 0f;

        if (showDebugLogs && progress > lastProgress)
        {
            Debug.Log($"[ChunaPathEvaluatorBridge] 진행률: {progress * 100:F1}% ({current}/{total}) (목표: {progressThreshold * 100:F0}%)");
        }

        lastProgress = progress;

        // 진행률 목표 달성 체크
        if (enableProgressThresholdEvent && !hasProgressThresholdBeenReached)
        {
            if (progress >= progressThreshold)
            {
                hasProgressThresholdBeenReached = true;
                Debug.Log($"<color=green>[ChunaPathEvaluatorBridge] 진행률 목표 달성! ({progress * 100:F1}%)</color>");
                OnProgressThresholdReached?.Invoke();
            }
        }
    }

    /// <summary>
    /// 체크포인트 통과 이벤트 핸들러
    /// </summary>
    private void OnCheckpointPassedHandler(PathCheckpoint checkpoint, float similarity)
    {
        if (showDebugLogs)
        {
            Debug.Log($"<color=cyan>[ChunaPathEvaluatorBridge] 체크포인트 통과: {checkpoint.CheckpointName} (유사도: {similarity:P0})</color>");
        }
    }

    // ========== 진행률 계산 메서드 ==========

    /// <summary>
    /// 현재 진행률 가져오기
    /// </summary>
    public float GetCurrentProgress()
    {
        if (pathEvaluator == null) return 0f;
        return pathEvaluator.GetProgress();
    }

    // ========== Public API ==========

    /// <summary>
    /// CSV 파일 로드 및 체크포인트 기반 평가 시작
    /// ScenarioManager에서 호출
    /// </summary>
    public void LoadFromCSV(string csvFileName)
    {
        if (pathEvaluator == null)
        {
            Debug.LogError("[ChunaPathEvaluatorBridge] ChunaPathEvaluator가 없습니다!");
            return;
        }

        if (showDebugLogs)
            Debug.Log($"<color=cyan>[ChunaPathEvaluatorBridge] CSV 로드 및 체크포인트 생성: {csvFileName}</color>");

        // 체크포인트 생성 및 평가 시작
        pathEvaluator.LoadAndGenerateCheckpoints(csvFileName);
        pathEvaluator.StartEvaluation();

        // 추적 시작
        StartTracking();
    }

    /// <summary>
    /// 추적 시작
    /// </summary>
    public void StartTracking()
    {
        isTracking = true;
        hasProgressThresholdBeenReached = false;
        hasSequenceCompleted = false;
        lastProgress = 0f;

        if (showDebugLogs)
            Debug.Log($"[ChunaPathEvaluatorBridge] 추적 시작 (목표: {progressThreshold * 100:F0}%)");
    }

    /// <summary>
    /// 추적 중지
    /// </summary>
    public void StopTracking()
    {
        isTracking = false;

        if (pathEvaluator != null && pathEvaluator.IsEvaluating)
        {
            pathEvaluator.StopEvaluation();
        }

        if (showDebugLogs)
            Debug.Log($"[ChunaPathEvaluatorBridge] 추적 중지");
    }

    /// <summary>
    /// 진행률 목표치 설정
    /// </summary>
    public void SetProgressThreshold(float threshold)
    {
        progressThreshold = Mathf.Clamp01(threshold);

        if (showDebugLogs)
            Debug.Log($"[ChunaPathEvaluatorBridge] 진행률 목표 설정: {progressThreshold * 100:F0}%");
    }

    /// <summary>
    /// 진행률 목표 달성 여부
    /// </summary>
    public bool HasReachedProgressThreshold()
    {
        return hasProgressThresholdBeenReached;
    }

    /// <summary>
    /// 시퀀스 완료 여부
    /// </summary>
    public bool HasCompletedSequence()
    {
        return hasSequenceCompleted;
    }

    /// <summary>
    /// 추적 상태 초기화
    /// </summary>
    public void ResetTracking()
    {
        hasProgressThresholdBeenReached = false;
        hasSequenceCompleted = false;
        lastProgress = 0f;

        if (pathEvaluator != null)
        {
            pathEvaluator.ResetEvaluation();
        }

        if (showDebugLogs)
            Debug.Log($"[ChunaPathEvaluatorBridge] 추적 상태 초기화");
    }

    /// <summary>
    /// 현재 평가 세션 가져오기
    /// </summary>
    public ChunaPathEvaluator.EvaluationSession GetCurrentSession()
    {
        return pathEvaluator?.GetCurrentSession();
    }

    /// <summary>
    /// ChunaPathEvaluator 참조 가져오기
    /// </summary>
    public ChunaPathEvaluator GetEvaluator()
    {
        return pathEvaluator;
    }
}
