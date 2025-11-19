using System;
using UnityEngine;

/// <summary>
/// HandPosePlayer 이벤트 브리지
/// HandPosePlayer 원본을 수정하지 않고 시나리오 시스템과 연동하기 위한 브리지 컴포넌트
/// 
/// 사용법:
/// 1. HandPosePlayer가 있는 GameObject에 이 컴포넌트 추가
/// 2. 자동으로 HandPosePlayer를 찾아 연결
/// 3. ScenarioConditionManager의 HandPoseCondition이 이 이벤트를 구독
/// 
/// 원본 HandPosePlayer.cs는 수정하지 않음
/// 
/// [수정 내역]
/// - 사용자 진행률 추적 추가: 재생 인덱스가 아닌 사용자가 실제로 따라한 프레임 기준으로 진행률 계산
/// - PlaybackWithComparison 모드 지원: 재생과 비교를 독립적으로 처리
/// </summary>
[RequireComponent(typeof(HandPosePlayer))]
public class HandPosePlayerEventBridge : MonoBehaviour
{
    [Header("=== HandPosePlayer 참조 ===")]
    [Tooltip("자동으로 찾아서 연결됩니다")]
    [SerializeField] private HandPosePlayer handPosePlayer;

    [Header("=== 진행률 추적 설정 ===")]
    [Tooltip("목표 진행률 (0.0~1.0). 이 진행률에 도달하면 OnProgressThresholdReached 이벤트 발생")]
    [SerializeField] private float progressThreshold = 0.8f;

    [Tooltip("시퀀스 완료 시 이벤트 발생 여부")]
    [SerializeField] private bool enableSequenceCompletedEvent = true;

    [Tooltip("진행률 목표 달성 시 이벤트 발생 여부")]
    [SerializeField] private bool enableProgressThresholdEvent = true;

    [Tooltip("진행률 체크 간격 (초)")]
    [SerializeField] private float checkInterval = 0.1f;

    [Tooltip("사용자 진행률 추적 사용 (true 권장: 실제로 따라한 부분만 카운트)")]
    [SerializeField] private bool useUserProgressTracking = true;

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

    // PlaybackWithComparison 모드 플래그
    private bool isPlaybackWithComparisonMode = false;

    void Awake()
    {
        // HandPosePlayer 자동 찾기
        if (handPosePlayer == null)
        {
            handPosePlayer = GetComponent<HandPosePlayer>();
        }

        if (handPosePlayer == null)
        {
            Debug.LogError("[HandPoseEventBridge] HandPosePlayer를 찾을 수 없습니다!");
        }
        else
        {
            if (showDebugLogs)
                Debug.Log($"[HandPoseEventBridge] HandPosePlayer 연결 완료: {handPosePlayer.name}");
        }
    }

    void Update()
    {
        // 트래킹이 활성화되지 않았으면 아무것도 하지 않음
        if (!isTracking) return;

        // HandPosePlayer 확인
        if (handPosePlayer == null) return;

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
            Debug.Log($"[HandPoseEventBridge] 진행률: {currentProgress * 100:F1}% (목표: {progressThreshold * 100:F0}%)");
        }

        // 진행률 목표 달성 체크
        if (enableProgressThresholdEvent && !hasProgressThresholdBeenReached)
        {
            if (currentProgress >= progressThreshold)
            {
                hasProgressThresholdBeenReached = true;
                Debug.Log($"<color=green>[HandPoseEventBridge] ✓ 진행률 목표 달성! ({currentProgress * 100:F1}%)</color>");
                OnProgressThresholdReached?.Invoke();
            }
        }

        // 시퀀스 완료 체크
        if (enableSequenceCompletedEvent && !hasSequenceCompleted)
        {
            bool completed = CheckSequenceCompleted();

            if (completed)
            {
                hasSequenceCompleted = true;
                Debug.Log($"<color=green>[HandPoseEventBridge] ✓ 시퀀스 완료!</color>");
                OnSequenceCompleted?.Invoke();
            }
        }
    }

    // ========== 진행률 계산 메서드 ==========

    /// <summary>
    /// 현재 진행률 가져오기
    /// useUserProgressTracking이 true면 사용자 진행률 사용 (권장)
    /// false면 재생 진행률 사용
    /// </summary>
    public float GetCurrentProgress()
    {
        if (handPosePlayer == null) return 0f;

        // 사용자 진행률 추적 모드
        if (useUserProgressTracking)
        {
            var playbackState = handPosePlayer.GetPlaybackState();
            int totalFrames = playbackState.totalFrames;

            if (totalFrames == 0) return 0f;

            var userProgress = handPosePlayer.GetUserProgress();
            float leftProgress = (float)userProgress.leftProgress / totalFrames;
            float rightProgress = (float)userProgress.rightProgress / totalFrames;

            return Mathf.Max(leftProgress, rightProgress);
        }
        // 재생 진행률 모드
        else
        {
            float leftProgress = handPosePlayer.GetLeftHandProgress();
            float rightProgress = handPosePlayer.GetRightHandProgress();
            return Mathf.Max(leftProgress, rightProgress);
        }
    }

    /// <summary>
    /// 시퀀스 완료 여부 체크
    /// </summary>
    private bool CheckSequenceCompleted()
    {
        if (handPosePlayer == null) return false;

        // 사용자 진행률 추적 모드
        if (useUserProgressTracking)
        {
            var userProgress = handPosePlayer.GetUserProgress();
            return userProgress.leftCompleted && userProgress.rightCompleted;
        }
        // 재생 진행률 모드
        else
        {
            var playbackState = handPosePlayer.GetPlaybackState();
            bool leftComplete = !playbackState.leftPlaying || playbackState.leftFrame >= playbackState.totalFrames - 1;
            bool rightComplete = !playbackState.rightPlaying || playbackState.rightFrame >= playbackState.totalFrames - 1;
            return leftComplete && rightComplete;
        }
    }

    // ========== Public API ==========

    /// <summary>
    /// CSV 파일 로드 및 추적 시작
    /// ScenarioActionHandler에서 호출
    /// </summary>
    public void LoadFromCSV(string csvFileName)
    {
        if (handPosePlayer == null)
        {
            Debug.LogError("[HandPoseEventBridge] HandPosePlayer가 없습니다!");
            return;
        }

        if (showDebugLogs)
            Debug.Log($"[HandPoseEventBridge] CSV 로드: {csvFileName}");

        // HandPosePlayer의 StartPlaybackFromCSV 메서드 호출
        handPosePlayer.StartPlaybackFromResourcesCSV(csvFileName);

        // 추적 시작
        StartTracking();
    }

    /// <summary>
    /// PlaybackWithComparison 모드 활성화
    /// 가이드 핸드는 루프 재생, 비교는 현재 프레임 참조
    /// </summary>
    public void EnablePlaybackWithComparison()
    {
        if (handPosePlayer == null)
        {
            Debug.LogError("[HandPoseEventBridge] HandPosePlayer가 없습니다!");
            return;
        }

        isPlaybackWithComparisonMode = true;

        // 리플레이 손 표시 활성화
        handPosePlayer.SetReplayHandsVisible(true);

        // PlaybackWithComparison 모드 활성화
        handPosePlayer.EnablePlaybackWithComparison();

        if (showDebugLogs)
            Debug.Log($"[HandPoseEventBridge] PlaybackWithComparison 모드 활성화");
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
            Debug.Log($"[HandPoseEventBridge] 추적 시작 (목표: {progressThreshold * 100:F0}%, 추적 모드: {(useUserProgressTracking ? "사용자 진행률" : "재생 진행률")})");
    }

    /// <summary>
    /// 추적 중지
    /// </summary>
    public void StopTracking()
    {
        isTracking = false;

        if (showDebugLogs)
            Debug.Log($"[HandPoseEventBridge] 추적 중지");
    }

    /// <summary>
    /// 진행률 목표치 설정
    /// </summary>
    public void SetProgressThreshold(float threshold)
    {
        progressThreshold = Mathf.Clamp01(threshold);

        if (showDebugLogs)
            Debug.Log($"[HandPoseEventBridge] 진행률 목표 설정: {progressThreshold * 100:F0}%");
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
            Debug.Log($"[HandPoseEventBridge] 추적 상태 초기화");
    }

    /// <summary>
    /// 사용자 진행률 추적 모드 설정
    /// </summary>
    public void SetUseUserProgressTracking(bool use)
    {
        useUserProgressTracking = use;

        if (showDebugLogs)
            Debug.Log($"[HandPoseEventBridge] 추적 모드 변경: {(use ? "사용자 진행률" : "재생 진행률")}");
    }
}