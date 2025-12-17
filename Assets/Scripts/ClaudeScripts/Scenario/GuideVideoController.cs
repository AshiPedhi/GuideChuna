using UnityEngine;
using UnityEngine.Video;
using System;

/// <summary>
/// 가이드 영상 구간 재생 컨트롤러
///
/// 기능:
/// - VideoPlayer를 사용하여 특정 구간만 재생
/// - 분:초 형식의 시간 지원 (SubStepData와 연동)
/// - 구간 반복 재생 지원
/// - ScenarioEventSystem 이벤트와 연동
///
/// 사용법:
/// 1. VideoPlayer 컴포넌트가 있는 오브젝트에 추가
/// 2. CSV에 videoStartTime, videoEndTime 추가 (예: "0:30", "1:45")
/// </summary>
[RequireComponent(typeof(VideoPlayer))]
public class GuideVideoController : MonoBehaviour
{
    [Header("=== 컴포넌트 참조 ===")]
    [SerializeField] private VideoPlayer videoPlayer;

    [Header("=== 재생 설정 ===")]
    [Tooltip("구간 끝에 도달하면 반복 재생")]
    [SerializeField] private bool loopSegment = true;

    [Tooltip("구간 시작/끝에 여유 시간 추가 (초)")]
    [SerializeField] private float timeBuffer = 0.1f;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLog = true;

    // 현재 재생 구간
    private float currentStartTime = 0f;
    private float currentEndTime = 0f;
    private bool isPlayingSegment = false;

    // 이벤트
    public event Action OnSegmentStarted;
    public event Action OnSegmentEnded;
    public event Action<float> OnSegmentProgress;  // 0~1 진행률

    private void Awake()
    {
        if (videoPlayer == null)
            videoPlayer = GetComponent<VideoPlayer>();
    }

    private void OnEnable()
    {
        // ScenarioEventSystem 이벤트 구독
        ScenarioEventSystem.Instance.OnSubStepStarted += OnSubStepStarted;
        ScenarioEventSystem.Instance.OnStepCompleted += OnStepCompleted;
    }

    private void OnDisable()
    {
        // ScenarioEventSystem 이벤트 해제
        ScenarioEventSystem.Instance.OnSubStepStarted -= OnSubStepStarted;
        ScenarioEventSystem.Instance.OnStepCompleted -= OnStepCompleted;
    }

    private void Update()
    {
        if (!isPlayingSegment || videoPlayer == null || !videoPlayer.isPlaying)
            return;

        // 현재 재생 시간 체크
        double currentTime = videoPlayer.time;

        // 진행률 이벤트
        if (currentEndTime > currentStartTime)
        {
            float progress = Mathf.Clamp01((float)(currentTime - currentStartTime) / (currentEndTime - currentStartTime));
            OnSegmentProgress?.Invoke(progress);
        }

        // 구간 끝에 도달
        if (currentTime >= currentEndTime - timeBuffer)
        {
            if (loopSegment)
            {
                // 반복 재생
                videoPlayer.time = currentStartTime;

                if (showDebugLog)
                    Debug.Log($"[GuideVideo] 구간 반복: {FormatTime(currentStartTime)} ~ {FormatTime(currentEndTime)}");
            }
            else
            {
                // 정지
                StopSegment();
            }
        }
    }

    /// <summary>
    /// SubStep 시작 시 호출 - 영상 구간 재생
    /// </summary>
    private void OnSubStepStarted(SubStepData subStep)
    {
        if (subStep == null) return;

        if (subStep.HasVideoSegment())
        {
            float startTime = subStep.GetVideoStartSeconds();
            float endTime = subStep.GetVideoEndSeconds();
            PlaySegment(startTime, endTime);
        }
        else
        {
            // 영상 구간이 없으면 정지
            StopSegment();
        }
    }

    /// <summary>
    /// Step 완료 시 호출
    /// </summary>
    private void OnStepCompleted(StepData step)
    {
        StopSegment();
    }

    /// <summary>
    /// 특정 구간 재생 (초 단위)
    /// </summary>
    public void PlaySegment(float startSeconds, float endSeconds)
    {
        if (videoPlayer == null)
        {
            Debug.LogError("[GuideVideo] VideoPlayer가 없습니다!");
            return;
        }

        if (startSeconds >= endSeconds)
        {
            Debug.LogWarning($"[GuideVideo] 잘못된 구간: {startSeconds} >= {endSeconds}");
            return;
        }

        currentStartTime = startSeconds;
        currentEndTime = endSeconds;
        isPlayingSegment = true;

        // 시작 위치로 이동 후 재생
        videoPlayer.time = currentStartTime;
        videoPlayer.Play();

        OnSegmentStarted?.Invoke();

        if (showDebugLog)
            Debug.Log($"<color=green>[GuideVideo] 구간 재생: {FormatTime(currentStartTime)} ~ {FormatTime(currentEndTime)}</color>");
    }

    /// <summary>
    /// 특정 구간 재생 (분:초 형식)
    /// </summary>
    public void PlaySegment(string startTime, string endTime)
    {
        float start = ParseTimeToSeconds(startTime);
        float end = ParseTimeToSeconds(endTime);
        PlaySegment(start, end);
    }

    /// <summary>
    /// 구간 재생 정지
    /// </summary>
    public void StopSegment()
    {
        if (!isPlayingSegment) return;

        isPlayingSegment = false;

        if (videoPlayer != null)
            videoPlayer.Pause();

        OnSegmentEnded?.Invoke();

        if (showDebugLog)
            Debug.Log("[GuideVideo] 구간 재생 정지");
    }

    /// <summary>
    /// 일시정지
    /// </summary>
    public void PauseSegment()
    {
        if (videoPlayer != null)
            videoPlayer.Pause();
    }

    /// <summary>
    /// 재개
    /// </summary>
    public void ResumeSegment()
    {
        if (videoPlayer != null && isPlayingSegment)
            videoPlayer.Play();
    }

    /// <summary>
    /// 반복 재생 설정
    /// </summary>
    public void SetLoop(bool loop)
    {
        loopSegment = loop;
    }

    /// <summary>
    /// 현재 재생 중인지 확인
    /// </summary>
    public bool IsPlaying => isPlayingSegment && videoPlayer != null && videoPlayer.isPlaying;

    /// <summary>
    /// 현재 구간 진행률 (0~1)
    /// </summary>
    public float GetSegmentProgress()
    {
        if (!isPlayingSegment || videoPlayer == null || currentEndTime <= currentStartTime)
            return 0f;

        return Mathf.Clamp01((float)(videoPlayer.time - currentStartTime) / (currentEndTime - currentStartTime));
    }

    /// <summary>
    /// 분:초 형식을 초 단위로 변환
    /// </summary>
    private float ParseTimeToSeconds(string timeStr)
    {
        if (string.IsNullOrEmpty(timeStr)) return 0f;

        string[] parts = timeStr.Split(':');
        if (parts.Length == 2)
        {
            if (int.TryParse(parts[0], out int minutes) && int.TryParse(parts[1], out int seconds))
            {
                return minutes * 60f + seconds;
            }
        }
        else if (parts.Length == 1)
        {
            if (float.TryParse(parts[0], out float seconds))
            {
                return seconds;
            }
        }

        return 0f;
    }

    /// <summary>
    /// 초를 분:초 형식으로 변환 (디버그용)
    /// </summary>
    private string FormatTime(float seconds)
    {
        int mins = (int)(seconds / 60f);
        int secs = (int)(seconds % 60f);
        return $"{mins}:{secs:D2}";
    }
}
