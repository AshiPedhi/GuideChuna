using UnityEngine;
using UnityEngine.Video;
using System;
using System.Collections;

/// <summary>
/// 가이드 영상 구간 재생 컨트롤러
///
/// 기능:
/// - VideoPlayer를 사용하여 특정 구간만 재생
/// - 분:초 형식의 시간 지원 (SubStepData와 연동)
/// - 구간 반복 재생 지원 (1초 대기 후 반복)
/// - ScenarioEventSystem 이벤트와 연동
/// - 시나리오 이름으로 영상 자동 로드
/// - 패널에서 가이드 활성화/비활성화 제어
///
/// 사용법:
/// 1. VideoPlayer 컴포넌트가 있는 오브젝트에 추가
/// 2. Resources/Videos/ 폴더에 시나리오 이름과 동일한 영상 파일 저장 (예: "상부승모근.mp4")
/// 3. CSV에 videoStartTime, videoEndTime 추가 (예: "0-30", "1-45" - 엑셀 호환용 "-" 구분자)
/// 4. 패널에서 SetGuideEnabled(true/false) 호출하여 재생 제어
/// </summary>
[RequireComponent(typeof(VideoPlayer))]
public class GuideVideoController : MonoBehaviour
{
    [Header("=== 컴포넌트 참조 ===")]
    [SerializeField] private VideoPlayer videoPlayer;

    [Header("=== 영상 설정 ===")]
    [Tooltip("영상 파일 경로 (Resources 폴더 기준, 예: Videos/상부승모근)")]
    [SerializeField] private string videoFolderPath = "Videos";

    [Tooltip("시나리오 시작 시 자동으로 영상 로드 (재생은 안 함)")]
    [SerializeField] private bool autoLoadOnScenarioStart = true;

    [Header("=== 재생 설정 ===")]
    [Tooltip("구간 끝에 도달하면 반복 재생")]
    [SerializeField] private bool loopSegment = true;

    [Tooltip("반복 재생 전 대기 시간 (초)")]
    [SerializeField] private float loopDelaySeconds = 1f;

    [Tooltip("구간 시작/끝에 여유 시간 추가 (초)")]
    [SerializeField] private float timeBuffer = 0.1f;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLog = true;

    // 현재 재생 구간
    private float currentStartTime = 0f;
    private float currentEndTime = 0f;
    private bool isPlayingSegment = false;
    private string currentVideoName = "";

    // 가이드 활성화 상태
    private bool isGuideEnabled = false;
    private SubStepData pendingSubStep = null;  // 가이드 활성화 시 재생할 SubStep

    // 반복 재생 대기 코루틴
    private Coroutine loopDelayCoroutine = null;
    private bool isWaitingForLoop = false;

    // 이벤트
    public event Action OnSegmentStarted;
    public event Action OnSegmentEnded;
    public event Action<float> OnSegmentProgress;  // 0~1 진행률
    public event Action<string> OnVideoLoaded;     // 영상 로드 완료
    public event Action<bool> OnGuideEnabledChanged;  // 가이드 활성화 상태 변경

    /// <summary>
    /// 가이드 활성화 상태
    /// </summary>
    public bool IsGuideEnabled => isGuideEnabled;

    private void Awake()
    {
        if (videoPlayer == null)
            videoPlayer = GetComponent<VideoPlayer>();
    }

    private void OnEnable()
    {
        // ScenarioEventSystem 이벤트 구독
        ScenarioEventSystem.Instance.OnScenarioStarted += OnScenarioStarted;
        ScenarioEventSystem.Instance.OnSubStepStarted += OnSubStepStarted;
        ScenarioEventSystem.Instance.OnStepCompleted += OnStepCompleted;
    }

    private void OnDisable()
    {
        // ScenarioEventSystem 이벤트 해제
        ScenarioEventSystem.Instance.OnScenarioStarted -= OnScenarioStarted;
        ScenarioEventSystem.Instance.OnSubStepStarted -= OnSubStepStarted;
        ScenarioEventSystem.Instance.OnStepCompleted -= OnStepCompleted;

        // 코루틴 정리
        if (loopDelayCoroutine != null)
        {
            StopCoroutine(loopDelayCoroutine);
            loopDelayCoroutine = null;
        }
    }

    private void Update()
    {
        if (!isPlayingSegment || videoPlayer == null || !videoPlayer.isPlaying || isWaitingForLoop)
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
            if (loopSegment && isGuideEnabled)
            {
                // 1초 대기 후 반복 재생
                if (loopDelayCoroutine != null)
                    StopCoroutine(loopDelayCoroutine);

                loopDelayCoroutine = StartCoroutine(LoopWithDelay());
            }
            else
            {
                // 정지
                StopSegment();
            }
        }
    }

    /// <summary>
    /// 대기 후 반복 재생 코루틴
    /// </summary>
    private IEnumerator LoopWithDelay()
    {
        isWaitingForLoop = true;

        // 일시정지
        if (videoPlayer != null)
            videoPlayer.Pause();

        if (showDebugLog)
            Debug.Log($"[GuideVideo] 구간 완료, {loopDelaySeconds}초 후 반복 재생...");

        yield return new WaitForSeconds(loopDelaySeconds);

        // 가이드가 여전히 활성화 상태인지 확인
        if (isGuideEnabled && isPlayingSegment)
        {
            // 시작 위치로 이동 후 재생
            videoPlayer.time = currentStartTime;
            videoPlayer.Play();

            if (showDebugLog)
                Debug.Log($"[GuideVideo] 구간 반복: {FormatTime(currentStartTime)} ~ {FormatTime(currentEndTime)}");
        }

        isWaitingForLoop = false;
        loopDelayCoroutine = null;
    }

    /// <summary>
    /// 가이드 활성화/비활성화 설정 (패널에서 호출)
    /// </summary>
    public void SetGuideEnabled(bool enabled)
    {
        if (isGuideEnabled == enabled) return;

        isGuideEnabled = enabled;

        if (showDebugLog)
            Debug.Log($"<color=yellow>[GuideVideo] 가이드 {(enabled ? "활성화" : "비활성화")}</color>");

        if (enabled)
        {
            // 가이드 활성화 - 대기 중인 SubStep이 있으면 재생
            if (pendingSubStep != null && pendingSubStep.HasVideoSegment())
            {
                float startTime = pendingSubStep.GetVideoStartSeconds();
                float endTime = pendingSubStep.GetVideoEndSeconds();
                PlaySegmentInternal(startTime, endTime);
            }
        }
        else
        {
            // 가이드 비활성화 - 재생 중지
            StopSegment();
        }

        OnGuideEnabledChanged?.Invoke(enabled);
    }

    /// <summary>
    /// 가이드 토글
    /// </summary>
    public void ToggleGuide()
    {
        SetGuideEnabled(!isGuideEnabled);
    }

    /// <summary>
    /// 시나리오 시작 시 호출 - 영상 로드만 (재생 안 함)
    /// </summary>
    private void OnScenarioStarted(ScenarioData scenario)
    {
        if (!autoLoadOnScenarioStart || scenario == null) return;

        LoadVideoByName(scenario.scenarioName);
    }

    /// <summary>
    /// 시나리오 이름으로 영상 로드 (Resources 폴더에서)
    /// </summary>
    public bool LoadVideoByName(string scenarioName)
    {
        if (videoPlayer == null)
        {
            Debug.LogError("[GuideVideo] VideoPlayer가 없습니다!");
            return false;
        }

        string videoPath = $"{videoFolderPath}/{scenarioName}";
        VideoClip clip = Resources.Load<VideoClip>(videoPath);

        if (clip != null)
        {
            videoPlayer.clip = clip;
            currentVideoName = scenarioName;

            if (showDebugLog)
                Debug.Log($"<color=green>[GuideVideo] 영상 로드 성공: {videoPath}</color>");

            OnVideoLoaded?.Invoke(scenarioName);
            return true;
        }
        else
        {
            Debug.LogWarning($"[GuideVideo] 영상을 찾을 수 없습니다: Resources/{videoPath}");
            Debug.LogWarning($"[GuideVideo] 영상 파일을 Assets/Resources/{videoFolderPath}/ 폴더에 넣어주세요!");
            return false;
        }
    }

    /// <summary>
    /// URL로 영상 로드 (스트리밍용)
    /// </summary>
    public void LoadVideoByUrl(string url)
    {
        if (videoPlayer == null)
        {
            Debug.LogError("[GuideVideo] VideoPlayer가 없습니다!");
            return;
        }

        videoPlayer.source = VideoSource.Url;
        videoPlayer.url = url;

        if (showDebugLog)
            Debug.Log($"[GuideVideo] URL 영상 설정: {url}");
    }

    /// <summary>
    /// SubStep 시작 시 호출 - 구간 정보 저장 (가이드 활성화 시에만 재생)
    /// </summary>
    private void OnSubStepStarted(SubStepData subStep)
    {
        if (subStep == null) return;

        // 현재 SubStep 저장
        pendingSubStep = subStep;

        // 대기 중인 반복 재생 취소
        if (loopDelayCoroutine != null)
        {
            StopCoroutine(loopDelayCoroutine);
            loopDelayCoroutine = null;
            isWaitingForLoop = false;
        }

        // 가이드 활성화 상태일 때만 재생
        if (isGuideEnabled && subStep.HasVideoSegment())
        {
            float startTime = subStep.GetVideoStartSeconds();
            float endTime = subStep.GetVideoEndSeconds();
            PlaySegmentInternal(startTime, endTime);
        }
        else if (isPlayingSegment)
        {
            // 가이드 비활성화 상태면 정지
            StopSegment();
        }
    }

    /// <summary>
    /// Step 완료 시 호출
    /// </summary>
    private void OnStepCompleted(StepData step)
    {
        pendingSubStep = null;
        StopSegment();
    }

    /// <summary>
    /// 특정 구간 재생 (외부 호출용 - 가이드 활성화 필요)
    /// </summary>
    public void PlaySegment(float startSeconds, float endSeconds)
    {
        if (!isGuideEnabled)
        {
            if (showDebugLog)
                Debug.LogWarning("[GuideVideo] 가이드가 비활성화 상태입니다. SetGuideEnabled(true)를 먼저 호출하세요.");
            return;
        }

        PlaySegmentInternal(startSeconds, endSeconds);
    }

    /// <summary>
    /// 특정 구간 재생 (내부용)
    /// </summary>
    private void PlaySegmentInternal(float startSeconds, float endSeconds)
    {
        if (videoPlayer == null)
        {
            Debug.LogError("[GuideVideo] VideoPlayer가 없습니다!");
            return;
        }

        if (videoPlayer.clip == null && string.IsNullOrEmpty(videoPlayer.url))
        {
            Debug.LogWarning("[GuideVideo] 로드된 영상이 없습니다!");
            return;
        }

        if (startSeconds >= endSeconds)
        {
            Debug.LogWarning($"[GuideVideo] 잘못된 구간: {startSeconds} >= {endSeconds}");
            return;
        }

        // 대기 중인 반복 재생 취소
        if (loopDelayCoroutine != null)
        {
            StopCoroutine(loopDelayCoroutine);
            loopDelayCoroutine = null;
            isWaitingForLoop = false;
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
    /// 특정 구간 재생 (분:초 또는 분-초 형식)
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
        // 대기 중인 반복 재생 취소
        if (loopDelayCoroutine != null)
        {
            StopCoroutine(loopDelayCoroutine);
            loopDelayCoroutine = null;
            isWaitingForLoop = false;
        }

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
        if (videoPlayer != null && isPlayingSegment && isGuideEnabled)
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
    /// 반복 대기 시간 설정
    /// </summary>
    public void SetLoopDelay(float seconds)
    {
        loopDelaySeconds = Mathf.Max(0f, seconds);
    }

    /// <summary>
    /// 현재 재생 중인지 확인
    /// </summary>
    public bool IsPlaying => isPlayingSegment && videoPlayer != null && videoPlayer.isPlaying;

    /// <summary>
    /// 현재 로드된 영상 이름
    /// </summary>
    public string CurrentVideoName => currentVideoName;

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
    /// 지원 형식: "1:30", "1-30", "90" (초 단위)
    /// </summary>
    private float ParseTimeToSeconds(string timeStr)
    {
        if (string.IsNullOrEmpty(timeStr)) return 0f;

        timeStr = timeStr.Trim();

        // ":" 또는 "-" 구분자 지원
        char[] separators = { ':', '-' };
        string[] parts = timeStr.Split(separators);

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
