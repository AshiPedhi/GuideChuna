using UnityEngine;
using UnityEngine.Video;
using System;
using System.Collections;

/// <summary>
/// 가이드 영상 구간 재생 컨트롤러
///
/// 기능:
/// - 패널의 토글 버튼으로 가이드 영상 활성화/비활성화
/// - 시나리오 시작 전 & 가이드 단계: 전체 영상 재생
/// - 타임라인이 있는 단계: 해당 구간만 재생 + 1초 딜레이 루프
/// - 시나리오 이름으로 영상 자동 로드
///
/// 사용법:
/// 1. VideoPlayer 컴포넌트가 있는 오브젝트에 추가
/// 2. Resources/Videos/ 폴더에 시나리오 이름과 동일한 영상 파일 저장 (예: "상부승모근.mp4")
/// 3. CSV에 videoStartTime, videoEndTime 추가 (예: "0-30", "1-45" - 엑셀 호환용 "-" 구분자)
/// 4. 패널에서 SetGuideEnabled(true/false) 또는 ToggleGuide() 호출
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
    [Tooltip("구간 재생 시 반복 재생")]
    [SerializeField] private bool loopSegment = true;

    [Tooltip("반복 재생 전 대기 시간 (초)")]
    [SerializeField] private float loopDelaySeconds = 1f;

    [Tooltip("구간 시작/끝에 여유 시간 추가 (초)")]
    [SerializeField] private float timeBuffer = 0.1f;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLog = true;

    // 현재 재생 상태
    private float currentStartTime = 0f;
    private float currentEndTime = 0f;
    private bool isPlayingSegment = false;      // 구간 재생 중
    private bool isPlayingFullVideo = false;    // 전체 영상 재생 중
    private string currentVideoName = "";

    // 가이드 활성화 상태
    private bool isGuideEnabled = false;
    private SubStepData pendingSubStep = null;  // 현재 SubStep
    private bool isInGuideStep = false;         // 가이드 스텝 여부
    private bool hasScenarioStarted = false;    // 시나리오 시작 여부

    // 반복 재생 대기 코루틴
    private Coroutine loopDelayCoroutine = null;
    private bool isWaitingForLoop = false;

    // ScenarioManager 참조
    private ScenarioManager scenarioManager;

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

        // ★ 자동 재생 방지 - 반드시 토글로 활성화해야 재생
        if (videoPlayer != null)
        {
            videoPlayer.playOnAwake = false;
            videoPlayer.Stop();
        }

        scenarioManager = FindFirstObjectByType<ScenarioManager>();
    }

    private void OnEnable()
    {
        // ScenarioEventSystem 이벤트 구독
        try
        {
            ScenarioEventSystem.Instance.OnScenarioStarted += OnScenarioStarted;
            ScenarioEventSystem.Instance.OnStepChanged += OnStepChanged;
            ScenarioEventSystem.Instance.OnSubStepStarted += OnSubStepStarted;
            ScenarioEventSystem.Instance.OnStepCompleted += OnStepCompleted;
            ScenarioEventSystem.Instance.OnScenarioCompleted += OnScenarioCompleted;
        }
        catch (System.Exception e)
        {
            ChunaLogger.LogError($"[GuideVideo] 이벤트 구독 실패: {e.Message}");
        }
    }

    private void OnDisable()
    {
        // ScenarioEventSystem 이벤트 해제
        try
        {
            ScenarioEventSystem.Instance.OnScenarioStarted -= OnScenarioStarted;
            ScenarioEventSystem.Instance.OnStepChanged -= OnStepChanged;
            ScenarioEventSystem.Instance.OnSubStepStarted -= OnSubStepStarted;
            ScenarioEventSystem.Instance.OnStepCompleted -= OnStepCompleted;
            ScenarioEventSystem.Instance.OnScenarioCompleted -= OnScenarioCompleted;
        }
        catch (System.Exception e)
        {
            ChunaLogger.LogWarning($"[GuideVideo] 이벤트 해제 실패: {e.Message}");
        }

        // 코루틴 정리
        StopAllPlayback();
    }

    private void Update()
    {
        // 구간 재생 중일 때만 체크
        if (!isPlayingSegment || videoPlayer == null || isWaitingForLoop)
            return;

        try
        {
            if (!videoPlayer.isPlaying)
                return;

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
                    // ★11-03: 반복을 끄면 구간 끝에서 멈춘다(구간은 유지 — 조작 막대가 구간을 계속 쓴다). 예전엔 StopSegment로 구간을 지웠다.
                    videoPlayer.Pause();
                }
            }
        }
        catch (System.Exception e)
        {
            ChunaLogger.LogWarning($"[GuideVideo] Update 오류: {e.Message}");
            isPlayingSegment = false;
        }
    }

    /// <summary>
    /// 대기 후 반복 재생 코루틴
    /// </summary>
    private IEnumerator LoopWithDelay()
    {
        isWaitingForLoop = true;

        if (videoPlayer != null)
            videoPlayer.Pause();

        if (showDebugLog)
            ChunaLogger.Log($"[GuideVideo] 구간 완료, {loopDelaySeconds}초 후 반복 재생...");

        yield return new WaitForSeconds(loopDelaySeconds);

        // 가이드가 여전히 활성화 상태인지 확인
        if (isGuideEnabled && isPlayingSegment)
        {
            videoPlayer.time = currentStartTime;
            videoPlayer.Play();

            if (showDebugLog)
                ChunaLogger.Log($"[GuideVideo] 구간 반복: {FormatTime(currentStartTime)} ~ {FormatTime(currentEndTime)}");
        }

        isWaitingForLoop = false;
        loopDelayCoroutine = null;
    }

    #region 가이드 활성화/비활성화 (패널에서 호출)

    /// <summary>
    /// 가이드 활성화/비활성화 설정 (패널의 토글 버튼에서 호출)
    /// </summary>
    public void SetGuideEnabled(bool enabled)
    {
        if (isGuideEnabled == enabled) return;

        isGuideEnabled = enabled;

        if (showDebugLog)
            ChunaLogger.Log($"<color=yellow>[GuideVideo] 가이드 {(enabled ? "활성화" : "비활성화")}</color>");

        if (enabled)
        {
            // 가이드 활성화 - 현재 상태에 따라 재생
            PlayBasedOnCurrentState();
        }
        else
        {
            // 가이드 비활성화 - 모든 재생 중지
            StopAllPlayback();
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
    /// 현재 상태에 따라 적절한 재생 시작
    /// </summary>
    private void PlayBasedOnCurrentState()
    {
        // 시나리오 시작 전 또는 가이드 스텝이면 전체 영상 재생
        if (!hasScenarioStarted || isInGuideStep)
        {
            if (showDebugLog)
                ChunaLogger.Log("[GuideVideo] 시나리오 시작 전/가이드 단계 - 전체 영상 재생");
            PlayFullVideo();
        }
        // 타임라인이 있는 단계면 구간 재생
        else if (pendingSubStep != null && pendingSubStep.HasVideoSegment())
        {
            if (showDebugLog)
                ChunaLogger.Log("[GuideVideo] 타임라인 있는 단계 - 구간 재생");
            float startTime = pendingSubStep.GetVideoStartSeconds();
            float endTime = pendingSubStep.GetVideoEndSeconds();
            PlaySegmentInternal(startTime, endTime);
        }
        // 타임라인이 없는 단계면 전체 영상 재생
        else
        {
            if (showDebugLog)
                ChunaLogger.Log("[GuideVideo] 타임라인 없는 단계 - 전체 영상 재생");
            PlayFullVideo();
        }
    }

    #endregion

    #region 전체 영상 재생

    /// <summary>
    /// 전체 영상 재생 (처음부터 끝까지)
    /// </summary>
    private void PlayFullVideo()
    {
        if (videoPlayer == null)
        {
            ChunaLogger.LogWarning("[GuideVideo] VideoPlayer가 없습니다!");
            return;
        }

        if (videoPlayer.clip == null && string.IsNullOrEmpty(videoPlayer.url))
        {
            ChunaLogger.LogWarning("[GuideVideo] 로드된 영상이 없습니다!");
            return;
        }

        // 기존 재생 중지
        StopAllPlayback();

        isPlayingFullVideo = true;
        isPlayingSegment = false;

        // VideoPlayer 설정
        videoPlayer.isLooping = true;  // 전체 영상은 자동 루프
        videoPlayer.time = 0;
        videoPlayer.Play();

        if (showDebugLog)
            ChunaLogger.Log("<color=green>[GuideVideo] 전체 영상 재생 시작 (루프)</color>");
    }

    #endregion

    #region 구간 재생

    /// <summary>
    /// 특정 구간 재생 (내부용)
    /// </summary>
    private void PlaySegmentInternal(float startSeconds, float endSeconds)
    {
        if (videoPlayer == null)
        {
            ChunaLogger.LogError("[GuideVideo] VideoPlayer가 없습니다!");
            return;
        }

        if (videoPlayer.clip == null && string.IsNullOrEmpty(videoPlayer.url))
        {
            ChunaLogger.LogWarning("[GuideVideo] 로드된 영상이 없습니다!");
            return;
        }

        if (startSeconds >= endSeconds)
        {
            ChunaLogger.LogWarning($"[GuideVideo] 잘못된 구간: {startSeconds} >= {endSeconds}");
            return;
        }

        // 기존 재생 중지
        StopAllPlayback();

        currentStartTime = startSeconds;
        currentEndTime = endSeconds;
        isPlayingSegment = true;
        isPlayingFullVideo = false;

        // VideoPlayer 설정
        videoPlayer.isLooping = false;  // 구간 재생은 수동 루프
        videoPlayer.time = currentStartTime;
        videoPlayer.Play();

        OnSegmentStarted?.Invoke();

        if (showDebugLog)
            ChunaLogger.Log($"<color=green>[GuideVideo] 구간 재생: {FormatTime(currentStartTime)} ~ {FormatTime(currentEndTime)}</color>");
    }

    /// <summary>
    /// 특정 구간 재생 (외부 호출용 - 가이드 활성화 필요)
    /// </summary>
    public void PlaySegment(float startSeconds, float endSeconds)
    {
        if (!isGuideEnabled)
        {
            if (showDebugLog)
                ChunaLogger.LogWarning("[GuideVideo] 가이드가 비활성화 상태입니다.");
            return;
        }

        PlaySegmentInternal(startSeconds, endSeconds);
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

    #endregion

    #region 재생 중지

    /// <summary>
    /// 모든 재생 중지
    /// </summary>
    private void StopAllPlayback()
    {
        // 코루틴 정리
        if (loopDelayCoroutine != null)
        {
            StopCoroutine(loopDelayCoroutine);
            loopDelayCoroutine = null;
        }
        isWaitingForLoop = false;

        // 재생 상태 초기화
        isPlayingSegment = false;
        isPlayingFullVideo = false;

        // VideoPlayer 정지
        if (videoPlayer != null && videoPlayer.enabled)
        {
            videoPlayer.Pause();
            videoPlayer.isLooping = false;
        }

        OnSegmentEnded?.Invoke();

        if (showDebugLog)
            ChunaLogger.Log("[GuideVideo] 재생 중지");
    }

    /// <summary>
    /// 구간 재생 정지 (외부 호출용)
    /// </summary>
    public void StopSegment()
    {
        StopAllPlayback();
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
        if (videoPlayer != null && isGuideEnabled && (isPlayingSegment || isPlayingFullVideo))
            videoPlayer.Play();
    }

    #endregion

    #region 이벤트 핸들러

    /// <summary>
    /// 시나리오 시작 시 호출 - 영상 로드만 (재생 안 함)
    /// </summary>
    private void OnScenarioStarted(ScenarioData scenario)
    {
        try
        {
            hasScenarioStarted = true;

            if (!autoLoadOnScenarioStart || scenario == null) return;

            LoadVideoByName(scenario.scenarioName);
        }
        catch (System.Exception e)
        {
            ChunaLogger.LogWarning($"[GuideVideo] 시나리오 시작 처리 실패: {e.Message}");
        }
    }

    /// <summary>
    /// Step 변경 시 호출
    /// </summary>
    private void OnStepChanged(StepData step)
    {
        try
        {
            if (step == null) return;

            // 가이드 스텝 여부 업데이트
            isInGuideStep = step.IsGuideStep();

            if (showDebugLog)
                ChunaLogger.Log($"[GuideVideo] Step 변경: {step.stepName}, 가이드 스텝: {isInGuideStep}");

            // 가이드 활성화 상태면 재생 방식 변경
            if (isGuideEnabled)
            {
                PlayBasedOnCurrentState();
            }
        }
        catch (System.Exception e)
        {
            ChunaLogger.LogWarning($"[GuideVideo] Step 변경 처리 실패: {e.Message}");
        }
    }

    /// <summary>
    /// SubStep 시작 시 호출 - 구간 정보 저장
    /// </summary>
    private void OnSubStepStarted(SubStepData subStep)
    {
        try
        {
            if (subStep == null) return;

            // 현재 SubStep 저장
            pendingSubStep = subStep;

            // 가이드 스텝 여부 업데이트 (ScenarioManager에서 확인)
            if (scenarioManager != null && scenarioManager.CurrentStep != null)
            {
                isInGuideStep = scenarioManager.CurrentStep.IsGuideStep();
            }

            if (showDebugLog)
                ChunaLogger.Log($"[GuideVideo] SubStep 시작: {subStep.textInstruction}, 가이드 스텝: {isInGuideStep}, 영상 구간: {subStep.HasVideoSegment()}");

            // 가이드 활성화 상태면 재생 방식 변경
            if (isGuideEnabled)
            {
                PlayBasedOnCurrentState();
            }
        }
        catch (System.Exception e)
        {
            ChunaLogger.LogWarning($"[GuideVideo] SubStep 처리 실패: {e.Message}");
        }
    }

    /// <summary>
    /// Step 완료 시 호출
    /// </summary>
    private void OnStepCompleted(StepData step)
    {
        // Step 완료 시 특별한 처리 없음 (다음 Step에서 처리)
    }

    /// <summary>
    /// 시나리오 완료 시 호출
    /// </summary>
    private void OnScenarioCompleted(ScenarioData scenario)
    {
        hasScenarioStarted = false;
        pendingSubStep = null;
        isInGuideStep = false;
        StopAllPlayback();
    }

    #endregion

    #region 영상 로드

    /// <summary>
    /// 시나리오 이름으로 영상 로드 (Resources 폴더에서)
    /// </summary>
    public bool LoadVideoByName(string scenarioName)
    {
        try
        {
            if (videoPlayer == null)
            {
                ChunaLogger.LogWarning("[GuideVideo] VideoPlayer가 없습니다!");
                return false;
            }

            if (string.IsNullOrEmpty(scenarioName))
            {
                ChunaLogger.LogWarning("[GuideVideo] 시나리오 이름이 비어있습니다!");
                return false;
            }

            string videoPath = $"{videoFolderPath}/{scenarioName}";
            VideoClip clip = Resources.Load<VideoClip>(videoPath);

            // ★11-04 1인칭 영상(같은 폴더 «{이름}_1인칭») — 있으면 전환 버튼이 뜬다. 1인칭을 고른 채면 그쪽을 건다.
            firstPersonClip = Resources.Load<VideoClip>(videoPath + FirstPersonSuffix);
            thirdPersonClip = clip;
            if (useFirstPerson && firstPersonClip != null) clip = firstPersonClip;

            if (clip != null)
            {
                videoPlayer.clip = clip;
                currentVideoName = scenarioName;
                ApplyMute();
                LoadSubtitles();

                // ★ 로드 후 자동 재생 방지 - 토글 활성화 전까지 재생 안 함
                videoPlayer.Stop();

                if (showDebugLog)
                    ChunaLogger.Log($"<color=green>[GuideVideo] 영상 로드 성공: {videoPath} (재생 대기 중 - 토글로 활성화 필요)</color>");

                OnVideoLoaded?.Invoke(scenarioName);
                return true;
            }
            else
            {
                if (showDebugLog)
                {
                    ChunaLogger.LogWarning($"[GuideVideo] 영상을 찾을 수 없습니다: Resources/{videoPath}");
                    ChunaLogger.LogWarning($"[GuideVideo] 영상 파일을 Assets/Resources/{videoFolderPath}/ 폴더에 넣어주세요!");
                }
                return false;
            }
        }
        catch (System.Exception e)
        {
            ChunaLogger.LogWarning($"[GuideVideo] 영상 로드 실패: {e.Message}");
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
            ChunaLogger.LogError("[GuideVideo] VideoPlayer가 없습니다!");
            return;
        }

        videoPlayer.source = VideoSource.Url;
        videoPlayer.url = url;

        if (showDebugLog)
            ChunaLogger.Log($"[GuideVideo] URL 영상 설정: {url}");
    }

    #endregion

    #region 재생 조작 (11-03 · ExpertVideoControlBar)

    /// <summary>정면 전문가 영상 탭 조작 막대가 쓴다.</summary>
    public VideoPlayer Player => videoPlayer;
    public bool LoopEnabled => loopSegment;
    public bool IsActive => isGuideEnabled && (isPlayingSegment || isPlayingFullVideo);

    /// <summary>지금 재생 범위 — 구간 재생이면 그 구간, 전체 재생이면 0 ~ 길이.</summary>
    public bool TryGetRange(out float start, out float end)
    {
        start = 0f; end = 0f;
        if (videoPlayer == null || !IsActive) return false;
        if (isPlayingSegment) { start = currentStartTime; end = currentEndTime; return end > start; }
        end = videoPlayer.length > 0 ? (float)videoPlayer.length : (videoPlayer.clip != null ? (float)videoPlayer.clip.length : 0f);
        return end > 0f;
    }

    /// <summary>범위 안으로만 옮긴다. 반복 대기 중이면 대기를 끊는다.</summary>
    public void SeekTo(float seconds)
    {
        if (!TryGetRange(out float s, out float e)) return;
        if (loopDelayCoroutine != null) { StopCoroutine(loopDelayCoroutine); loopDelayCoroutine = null; }
        isWaitingForLoop = false;
        videoPlayer.time = Mathf.Clamp(seconds, s, Mathf.Max(s, e - 0.1f));
        if (showDebugLog) ChunaLogger.Log($"[GuideVideo] 이동 → {FormatTime((float)videoPlayer.time)}");
    }

    /// <summary>재생 중이면 멈추고, 멈춰 있으면(구간 끝 포함) 다시 튼다.</summary>
    public void TogglePause()
    {
        if (videoPlayer == null || !IsActive) return;
        if (videoPlayer.isPlaying || isWaitingForLoop)
        {
            if (loopDelayCoroutine != null) { StopCoroutine(loopDelayCoroutine); loopDelayCoroutine = null; }
            isWaitingForLoop = false;
            videoPlayer.Pause();
        }
        else
        {
            if (TryGetRange(out float s, out float e) && videoPlayer.time >= e - timeBuffer - 0.05f) videoPlayer.time = s;
            videoPlayer.Play();
        }
    }

    /// <summary>구간 반복 · 전체 영상 반복을 같이 켜고 끈다.</summary>
    public void SetLoopAll(bool loop)
    {
        loopSegment = loop;
        if (isPlayingFullVideo && videoPlayer != null) videoPlayer.isLooping = loop;
    }

    // ── 1인칭 · 3인칭 (11-04 사용자 «1인칭 3인칭 스위치 버튼») ──
    // ★CSV 구간(videoStartTime · videoEndTime)은 <b>한 벌</b>이다 — 두 영상이 같은 시간축이라고 본다(같은 순간을 동시에 찍은 영상).
    //   시간축이 다르면 바꿀 때 구간이 엉뚱한 동작을 가리킨다 → 그때는 영상을 맞춰 자르거나 구간을 두 벌로 나눠야 한다.
    private const string FirstPersonSuffix = "_1인칭";
    private VideoClip firstPersonClip, thirdPersonClip;
    private bool useFirstPerson;
    private Coroutine swapCoroutine;

    /// <summary>지금 영상에 1인칭 판이 있는가(없으면 전환 버튼을 숨긴다).</summary>
    public bool HasFirstPerson => firstPersonClip != null && thirdPersonClip != null;
    public bool IsFirstPerson => useFirstPerson && HasFirstPerson;

    /// <summary>1인칭 ↔ 3인칭. 재생 자리 · 구간 · 재생 중 여부는 그대로 잇는다.</summary>
    public void SetFirstPerson(bool on)
    {
        if (videoPlayer == null || !HasFirstPerson || useFirstPerson == on) return;
        useFirstPerson = on;
        var next = on ? firstPersonClip : thirdPersonClip;
        if (videoPlayer.clip == next) return;
        double t = videoPlayer.time;
        bool wasPlaying = videoPlayer.isPlaying || isWaitingForLoop;
        if (swapCoroutine != null) StopCoroutine(swapCoroutine);
        swapCoroutine = StartCoroutine(SwapClip(next, t, wasPlaying));
    }

    private IEnumerator SwapClip(VideoClip next, double t, bool resume)
    {
        bool looping = videoPlayer.isLooping;
        videoPlayer.clip = next;
        videoPlayer.isLooping = looping;
        videoPlayer.Prepare();
        float until = Time.unscaledTime + 3f;
        while (!videoPlayer.isPrepared && Time.unscaledTime < until) yield return null;
        if (!videoPlayer.isPrepared) ChunaLogger.LogWarning($"[GuideVideo] ★{(useFirstPerson ? "1인칭" : "3인칭")} 영상 준비가 3초 안에 안 끝났다");
        videoPlayer.time = Math.Min(t, Math.Max(0.0, next.length - 0.1));
        ApplyMute();
        LoadSubtitles();   // 1인칭 판은 자막도 따로(«{이름}_1인칭_자막») — 없으면 3인칭 자막을 쓴다
        if (resume) videoPlayer.Play(); else videoPlayer.Pause();
        swapCoroutine = null;
        if (showDebugLog) ChunaLogger.Log($"[GuideVideo] 시점 전환 → {(useFirstPerson ? "1인칭" : "3인칭")} · {FormatTime((float)t)} · 재생 {resume}");
    }

    // ── 음소거 · 자막 (11-04 사용자 «전문가 영상은 기본 음소거 상태에서 자막으로 내용 대체» · 자막 A안 = 시간 맞춘 자막 파일) ──
    // ★자막 파일 = Resources/Videos/{이름}_자막.txt (SRT 형식 그대로 — Unity는 .srt를 TextAsset으로 안 읽는다). 1인칭은 {이름}_1인칭_자막.txt.
    // ★파일이 없으면 자막 없이 돈다(경고 한 번).
    private bool muted = true;
    private float[] subStart = new float[0], subEnd = new float[0];
    private string[] subText = new string[0];
    private string subLoadedFor;

    public bool Muted => muted;
    public bool HasSubtitles => subText.Length > 0;

    public void SetMuted(bool m) { muted = m; ApplyMute(); }

    private void ApplyMute()
    {
        if (videoPlayer == null) return;
        for (ushort i = 0; i < videoPlayer.audioTrackCount; i++)
        {
            videoPlayer.SetDirectAudioMute(i, muted);
            var src = videoPlayer.GetTargetAudioSource(i); if (src != null) src.mute = muted;
        }
        // 트랙 수는 준비가 끝나야 나온다 — 0이면 출력 자체를 끈다(Direct · AudioSource 모두)
        if (videoPlayer.audioTrackCount == 0 && muted) videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
        else if (!muted && videoPlayer.audioOutputMode == VideoAudioOutputMode.None) videoPlayer.audioOutputMode = VideoAudioOutputMode.Direct;
    }

    private void LoadSubtitles()
    {
        string baseName = string.IsNullOrEmpty(currentVideoName) ? "" : $"{videoFolderPath}/{currentVideoName}";
        string key = baseName + (IsFirstPerson ? FirstPersonSuffix : "");
        if (key == subLoadedFor) return;
        subLoadedFor = key;
        var ta = string.IsNullOrEmpty(baseName) ? null : Resources.Load<TextAsset>(key + "_자막");
        if (ta == null && IsFirstPerson) ta = Resources.Load<TextAsset>(baseName + "_자막");
        ParseSrt(ta != null ? ta.text : null);
        ChunaLogger.Log($"[GuideVideo] 자막 {(ta != null ? $"'{ta.name}' {subText.Length}줄" : $"★없음(Resources/{key}_자막.txt) — 자막 없이 재생")} · 소리 {(muted ? "끔" : "켬")}");
    }

    /// <summary>지금 재생 시각의 자막(없으면 빈 문자열). ★할당 없음 — 읽어 둔 문자열을 그대로 돌려준다.</summary>
    public string CurrentSubtitle
    {
        get
        {
            if (subText.Length == 0 || videoPlayer == null) return "";
            float t = (float)videoPlayer.time;
            int lo = 0, hi = subStart.Length - 1, found = -1;
            while (lo <= hi) { int mid = (lo + hi) >> 1; if (subStart[mid] <= t) { found = mid; lo = mid + 1; } else hi = mid - 1; }
            return found >= 0 && t <= subEnd[found] ? subText[found] : "";
        }
    }

    private void ParseSrt(string text)
    {
        var s = new System.Collections.Generic.List<float>(); var e = new System.Collections.Generic.List<float>(); var x = new System.Collections.Generic.List<string>();
        if (!string.IsNullOrEmpty(text))
        {
            var blocks = text.Replace("\r", "").Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var blk in blocks)
            {
                var lines = blk.Trim('\n').Split('\n');
                int ti = lines.Length > 1 && lines[0].IndexOf("-->", StringComparison.Ordinal) < 0 ? 1 : 0;   // 번호 줄은 있어도 없어도
                if (ti >= lines.Length) continue;
                var parts = lines[ti].Split(new[] { "-->" }, StringSplitOptions.None);
                if (parts.Length != 2 || !TryTime(parts[0], out float a) || !TryTime(parts[1], out float b)) continue;
                var body = new System.Text.StringBuilder();
                for (int i = ti + 1; i < lines.Length; i++) { if (body.Length > 0) body.Append('\n'); body.Append(lines[i].Trim()); }
                s.Add(a); e.Add(b); x.Add(body.ToString());
            }
        }
        // 시작 시각 순으로 셋을 같이 정렬한다(찾기가 이진 탐색이라)
        var idx = new int[s.Count];
        for (int i = 0; i < idx.Length; i++) idx[i] = i;
        Array.Sort(idx, (p, q) => s[p].CompareTo(s[q]));
        subStart = new float[idx.Length]; subEnd = new float[idx.Length]; subText = new string[idx.Length];
        for (int i = 0; i < idx.Length; i++) { subStart[i] = s[idx[i]]; subEnd[i] = e[idx[i]]; subText[i] = x[idx[i]]; }
    }

    /// <summary>«00:01:02,345» · «01:02.3» · «62.3» 모두 받는다.</summary>
    private static bool TryTime(string str, out float sec)
    {
        sec = 0f;
        str = str.Trim().Replace(',', '.');
        var p = str.Split(':');
        float acc = 0f;
        for (int i = 0; i < p.Length; i++)
        {
            if (!float.TryParse(p[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) return false;
            acc = acc * 60f + v;
        }
        sec = acc;
        return true;
    }

    #endregion

    #region Public 유틸리티

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
    public bool IsPlaying => (isPlayingSegment || isPlayingFullVideo) && videoPlayer != null && videoPlayer.isPlaying;

    /// <summary>
    /// 현재 로드된 영상 이름
    /// </summary>
    public string CurrentVideoName => currentVideoName;

    /// <summary>
    /// 현재 구간 진행률 (0~1) - 구간 재생 시에만 유효
    /// </summary>
    public float GetSegmentProgress()
    {
        if (!isPlayingSegment || videoPlayer == null || currentEndTime <= currentStartTime)
            return 0f;

        return Mathf.Clamp01((float)(videoPlayer.time - currentStartTime) / (currentEndTime - currentStartTime));
    }

    #endregion

    #region Private 유틸리티

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

    #endregion
}
