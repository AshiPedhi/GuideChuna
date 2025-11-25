using System;
using UnityEngine;

/// <summary>
/// HandPoseTrainingController와 시나리오 시스템 연결 브리지
/// HandPosePlayerEventBridge와 동일한 인터페이스 제공
///
/// 목적:
/// - 기존 시나리오 시스템과 호환성 유지
/// - 새로운 모듈식 HandPoseTrainingController 사용
/// - ScenarioConditionManager와 자동 연동
///
/// 사용법:
/// 1. HandPoseTrainingController가 있는 GameObject에 이 컴포넌트 추가
/// 2. 자동으로 HandPoseTrainingController를 찾아 연결
/// 3. ScenarioManager가 자동으로 이 브리지를 사용
/// </summary>
[RequireComponent(typeof(HandPoseTrainingController))]
public class HandPoseTrainingControllerBridge : MonoBehaviour
{
    [Header("=== HandPoseTrainingController 참조 ===")]
    [Tooltip("자동으로 찾아서 연결됩니다")]
    [SerializeField] private HandPoseTrainingController trainingController;

    [Header("=== 진행률 추적 설정 ===")]
    [Tooltip("목표 진행률 (0.0~1.0). 이 진행률에 도달하면 OnProgressThresholdReached 이벤트 발생")]
    [SerializeField] private float progressThreshold = 0.8f;

    [Tooltip("시퀀스 완료 시 이벤트 발생 여부")]
    [SerializeField] private bool enableSequenceCompletedEvent = true;

    [Tooltip("진행률 목표 달성 시 이벤트 발생 여부")]
    [SerializeField] private bool enableProgressThresholdEvent = true;

    [Tooltip("진행률 체크 간격 (초)")]
    [SerializeField] private float checkInterval = 0.1f;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = true;

    // 이벤트 (시나리오 시스템 연동용)
    public event Action OnSequenceCompleted;
    public event Action OnProgressThresholdReached;

    // 진행률 추적 상태
    private bool hasProgressThresholdBeenReached = false;
    private bool hasSequenceCompleted = false;
    private bool isTracking = false;
    private float lastCheckTime = 0f;

    // 이전 진행률 추적
    private float lastProgress = 0f;

    void Awake()
    {
        // HandPoseTrainingController 자동 찾기
        if (trainingController == null)
        {
            trainingController = GetComponent<HandPoseTrainingController>();
        }

        if (trainingController == null)
        {
            Debug.LogError("[TrainingControllerBridge] HandPoseTrainingController를 찾을 수 없습니다!");
        }
        else
        {
            if (showDebugLogs)
                Debug.Log($"[TrainingControllerBridge] HandPoseTrainingController 연결 완료: {trainingController.name}");

            // 이벤트 구독
            trainingController.OnUserProgressCompleted += OnUserProgressCompletedHandler;
            trainingController.OnSequenceCompleted += OnSequenceCompletedHandler;
        }
    }

    void OnDestroy()
    {
        // 이벤트 구독 해제
        if (trainingController != null)
        {
            trainingController.OnUserProgressCompleted -= OnUserProgressCompletedHandler;
            trainingController.OnSequenceCompleted -= OnSequenceCompletedHandler;
        }
    }

    void Update()
    {
        // 트래킹이 활성화되지 않았으면 아무것도 하지 않음
        if (!isTracking) return;

        // HandPoseTrainingController 확인
        if (trainingController == null) return;

        // 체크 간격 확인
        if (Time.time - lastCheckTime < checkInterval) return;
        lastCheckTime = Time.time;

        // 진행률 확인
        float currentProgress = GetCurrentProgress();

        // 진행 중인지 확인 (진행률이 변경되고 있는지)
        bool isProgressing = currentProgress > lastProgress;
        lastProgress = currentProgress;

        if (showDebugLogs && isProgressing)
        {
            Debug.Log($"[TrainingControllerBridge] 진행률: {currentProgress * 100:F1}% (목표: {progressThreshold * 100:F0}%)");
        }

        // 진행률 목표 달성 체크
        if (enableProgressThresholdEvent && !hasProgressThresholdBeenReached)
        {
            if (currentProgress >= progressThreshold)
            {
                hasProgressThresholdBeenReached = true;
                Debug.Log($"<color=green>[TrainingControllerBridge] ✓ 진행률 목표 달성! ({currentProgress * 100:F1}%)</color>");
                OnProgressThresholdReached?.Invoke();
            }
        }
    }

    // ========== 이벤트 핸들러 ==========

    /// <summary>
    /// 사용자 진행 완료 이벤트 핸들러
    /// </summary>
    private void OnUserProgressCompletedHandler()
    {
        hasSequenceCompleted = true;
        if (showDebugLogs)
            Debug.Log($"<color=green>[TrainingControllerBridge] ✓ 사용자 진행 완료!</color>");
        OnSequenceCompleted?.Invoke();
    }

    /// <summary>
    /// 시퀀스 완료 이벤트 핸들러
    /// </summary>
    private void OnSequenceCompletedHandler()
    {
        if (hasSequenceCompleted)
            return;  // 이미 완료 이벤트 발생했으면 중복 방지

        hasSequenceCompleted = true;
        if (showDebugLogs)
            Debug.Log($"<color=green>[TrainingControllerBridge] ✓ 시퀀스 완료!</color>");
        OnSequenceCompleted?.Invoke();
    }

    // ========== 진행률 계산 메서드 ==========

    /// <summary>
    /// 현재 진행률 가져오기
    /// </summary>
    public float GetCurrentProgress()
    {
        if (trainingController == null) return 0f;

        var playbackState = trainingController.GetPlaybackState();
        int totalFrames = playbackState.totalFrames;

        if (totalFrames == 0) return 0f;

        var userProgress = trainingController.GetUserProgress();
        float leftProgress = (float)userProgress.leftProgress / totalFrames;
        float rightProgress = (float)userProgress.rightProgress / totalFrames;

        return Mathf.Max(leftProgress, rightProgress);
    }

    // ========== Public API ==========

    /// <summary>
    /// CSV 파일 로드 및 추적 시작
    /// ScenarioManager의 HandleHandPoseTracking에서 호출
    /// </summary>
    public void LoadFromCSV(string csvFileName)
    {
        if (trainingController == null)
        {
            Debug.LogError("[TrainingControllerBridge] HandPoseTrainingController가 없습니다!");
            return;
        }

        if (showDebugLogs)
            Debug.Log($"[TrainingControllerBridge] CSV 로드: {csvFileName}");

        // HandPoseTrainingController의 LoadAndStartTraining 메서드 호출
        trainingController.LoadAndStartTraining(csvFileName);

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
        lastCheckTime = Time.time;

        if (showDebugLogs)
            Debug.Log($"[TrainingControllerBridge] 추적 시작 (목표: {progressThreshold * 100:F0}%)");
    }

    /// <summary>
    /// 추적 중지
    /// </summary>
    public void StopTracking()
    {
        isTracking = false;

        if (showDebugLogs)
            Debug.Log($"[TrainingControllerBridge] 추적 중지");
    }

    /// <summary>
    /// 진행률 목표치 설정
    /// </summary>
    public void SetProgressThreshold(float threshold)
    {
        progressThreshold = Mathf.Clamp01(threshold);

        if (showDebugLogs)
            Debug.Log($"[TrainingControllerBridge] 진행률 목표 설정: {progressThreshold * 100:F0}%");
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

        if (showDebugLogs)
            Debug.Log($"[TrainingControllerBridge] 추적 상태 초기화");
    }
}
