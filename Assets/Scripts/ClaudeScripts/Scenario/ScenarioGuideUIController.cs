using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using ChunaTraining;  // DifficultyManager

/// <summary>
/// 시나리오 가이드 UI 전용 컨트롤러 (시나리오 진행 UI)
///
/// [역할]
/// - stepName 표시
/// - Phase 이미지 (중부/전부/후부) 진행 상태 표시
/// - 시작 토글 제어 (가이드 스텝에서만 표시)
/// - 진행 원형 표시 (Duration)
/// - ★ 단계 완료 피드백 표시 (유사도 기반)
///
/// [참고]
/// - 가이드 영상 토글은 InfoPanelController에서 처리합니다.
/// </summary>
public class ScenarioGuideUIController : MonoBehaviour
{
    [Header("=== Step Name 표시 ===")]
    [SerializeField] private TextMeshProUGUI stepNameText;

    [Header("=== Phase 이미지 (진행 상태 표시) ===")]
    [SerializeField] private Image middlePhaseImage;  // 중부 / 늑골 / 2번째 세부 부위
    [SerializeField] private Image frontPhaseImage;   // 전부 / 쇄골 / 1번째 세부 부위
    [SerializeField] private Image backPhaseImage;    // 후부 / 흉골 / 3번째 세부 부위

    [Header("=== Phase 이미지 색상 ===")]
    [SerializeField] private Color activePhaseColor = new Color(0.3f, 0.6f, 1f, 1f);     // 활성화된 Phase (파란색)
    [SerializeField] private Color inactivePhaseColor = new Color(0.5f, 0.5f, 0.5f, 1f); // 비활성 Phase (회색)

    [Header("=== Phase 이름 매핑 (세부 부위) ===")]
    [Tooltip("첫 번째 세부 부위 Phase 이름들 (예: 전부, 쇄골)")]
    [SerializeField] private string[] firstPhaseNames = new string[] { "전부", "쇄골" };

    [Tooltip("두 번째 세부 부위 Phase 이름들 (예: 중부, 늑골)")]
    [SerializeField] private string[] secondPhaseNames = new string[] { "중부", "늑골" };

    [Tooltip("세 번째 세부 부위 Phase 이름들 (예: 후부, 흉골)")]
    [SerializeField] private string[] thirdPhaseNames = new string[] { "후부", "흉골" };

    [Tooltip("Phase 이미지를 숨길 Phase 이름들 (예: 시작하기, 진단, 종료)")]
    [SerializeField] private string[] hidePhaseNames = new string[] { "시작하기", "진단", "가이드", "종료" };

    [Header("=== 시작 토글 ===")]
    [SerializeField] private GameObject startToggleObject;
    [SerializeField] private Toggle startToggle;
    [SerializeField] private TextMeshProUGUI startToggleText;

    [Header("=== 설명 텍스트 ===")]
    [SerializeField] private TextMeshProUGUI descriptionText;

    [Header("=== 진행 원형 표시 (Duration) ===")]
    [Tooltip("ProgressCircle 프리팹 루트 GameObject")]
    [SerializeField] private GameObject progressCircleObject;

    [Tooltip("원형 진행 표시 Image (FillAmount 조절용)")]
    [SerializeField] private Image progressCircleFillImage;

    [Tooltip("남은 시간 표시 텍스트")]
    [SerializeField] private TextMeshProUGUI durationText;

    [Tooltip("완료 표시 텍스트")]
    [SerializeField] private TextMeshProUGUI completeText;

    [Tooltip("완료 아이콘")]
    [SerializeField] private GameObject completeIcon;

    [Header("=== ★ 단계 완료 피드백 UI ===")]
    [Tooltip("안내 콘텐츠 섹션 (피드백 시 숨김)")]
    [SerializeField] private GameObject guideContentSection;

    [Tooltip("피드백 섹션 (피드백 시 표시)")]
    [SerializeField] private GameObject feedbackSection;

    [Tooltip("현재 유사도 텍스트 (큰 글씨)")]
    [SerializeField] private TextMeshProUGUI feedbackSimilarityText;

    [Tooltip("목표 유사도 텍스트")]
    [SerializeField] private TextMeshProUGUI feedbackTargetText;

    [Tooltip("피드백 메시지 텍스트")]
    [SerializeField] private TextMeshProUGUI feedbackMessageText;

    [Tooltip("피드백 프로그레스 바 Fill")]
    [SerializeField] private Image feedbackProgressFill;

    [Header("=== 피드백 설정 ===")]
    [Tooltip("피드백 표시 시간 (초)")]
    [SerializeField] private float feedbackDisplayDuration = 2.5f;

    [Tooltip("초과 달성 색상")]
    [SerializeField] private Color excellentColor = new Color(0.2f, 0.9f, 0.3f);

    [Tooltip("목표 달성 색상")]
    [SerializeField] private Color goodColor = new Color(0.4f, 0.8f, 0.4f);

    [Tooltip("근접 색상")]
    [SerializeField] private Color closeColor = new Color(1f, 0.8f, 0.2f);

    [Tooltip("미달 색상")]
    [SerializeField] private Color poorColor = new Color(1f, 0.4f, 0.3f);

    [Header("=== 피드백 메시지 ===")]
    [SerializeField] private string excellentMessage = "훌륭해요!";
    [SerializeField] private string goodMessage = "잘했어요!";
    [SerializeField] private string closeMessage = "거의 다 됐어요!";
    [SerializeField] private string poorMessage = "더 연습해봐요";

    // [REMOVED] 가이드 영상 토글은 InfoPanelController로 이전됨

    [Header("=== 홀드 연동 (ChunaPathEvaluator) ===")]
    [Tooltip("홀드 상태와 연동할 ChunaPathEvaluator (없으면 시간 기반으로 진행)")]
    [SerializeField] private ChunaPathEvaluator pathEvaluator;

    [Tooltip("홀드 중일 때만 시간 진행 (pathEvaluator 필요)")]
    [SerializeField] private bool requireHoldForProgress = true;

    private ScenarioEventSystem eventSystem;
    private ScenarioManager scenarioManager;
    private string currentPhaseName = "";

    // ProgressCircle 관련 상태
    private bool isProgressActive = false;
    private float currentDuration = 0f;
    private float elapsedTime = 0f;

    // 홀드 상태 (ChunaPathEvaluator 연동)
    private bool isCurrentlyHolding = false;

    // 피드백 관련 상태
    private Coroutine feedbackCoroutine;
    private bool isFeedbackShowing = false;

    /// <summary>
    /// 피드백 표시 완료 콜백 (ScenarioConditionManager에서 대기용)
    /// </summary>
    public System.Action OnFeedbackComplete;

    void Awake()
    {
        eventSystem = ScenarioEventSystem.Instance;
        scenarioManager = FindObjectOfType<ScenarioManager>();

        // 시작 토글 이벤트 연결
        if (startToggle != null)
        {
            startToggle.onValueChanged.AddListener(OnStartToggleChanged);
        }
    }

    void OnEnable()
    {
        // 이벤트 구독
        eventSystem.OnPhaseChanged += OnPhaseChanged;
        eventSystem.OnStepChanged += OnStepChanged;
        eventSystem.OnSubStepStarted += OnSubStepStarted;

        // ChunaPathEvaluator 홀드 이벤트 구독
        if (pathEvaluator != null)
        {
            pathEvaluator.OnHoldProgressChanged += OnHoldProgressChanged;
            pathEvaluator.OnHoldCompleted += OnHoldCompleted;
        }
    }

    void OnDisable()
    {
        // 이벤트 구독 해제
        eventSystem.OnPhaseChanged -= OnPhaseChanged;
        eventSystem.OnStepChanged -= OnStepChanged;
        eventSystem.OnSubStepStarted -= OnSubStepStarted;

        // ChunaPathEvaluator 홀드 이벤트 구독 해제
        if (pathEvaluator != null)
        {
            pathEvaluator.OnHoldProgressChanged -= OnHoldProgressChanged;
            pathEvaluator.OnHoldCompleted -= OnHoldCompleted;
        }
    }

    /// <summary>
    /// 홀드 진행 상태 변경 시 호출
    /// </summary>
    private void OnHoldProgressChanged(float currentTime, float requiredTime)
    {
        // 홀드 중인지 판단 (currentTime > 0이면 홀드 중)
        isCurrentlyHolding = currentTime > 0f;
    }

    /// <summary>
    /// 홀드 완료 시 호출
    /// </summary>
    private void OnHoldCompleted()
    {
        isCurrentlyHolding = false;
    }

    void Update()
    {
        // 진행 원형 표시가 활성화된 경우 시간 업데이트
        if (isProgressActive && currentDuration > 0)
        {
            // 홀드 연동이 필요한 경우: 홀드 중일 때만 시간 진행
            bool canProgress = true;
            if (requireHoldForProgress && pathEvaluator != null)
            {
                canProgress = isCurrentlyHolding;
            }

            if (canProgress)
            {
                elapsedTime += Time.deltaTime;
            }

            // 남은 시간 계산
            float remainingTime = Mathf.Max(0f, currentDuration - elapsedTime);
            float progress = Mathf.Clamp01(remainingTime / currentDuration);

            // UI 업데이트
            UpdateProgressCircle(remainingTime, progress);

            // 시간이 다 되면 완료 상태로 전환
            if (remainingTime <= 0f)
            {
                CompleteProgress();
            }
        }
    }

    /// <summary>
    /// Phase 변경 시 호출
    /// </summary>
    private void OnPhaseChanged(PhaseData phase)
    {
        currentPhaseName = phase.phaseName;
        UpdatePhaseImages();

        Debug.Log($"[GuideUI] Phase 변경: {currentPhaseName}");
    }

    /// <summary>
    /// Step 변경 시 호출
    /// </summary>
    private void OnStepChanged(StepData step)
    {
        /*if(step.stepName == "등척성운동")
        {
            startToggleObject.SetActive(true);
            startToggle.isOn = false;
        }*/
        UpdateStepName(step);
        UpdateStartToggleVisibility(step);

        Debug.Log($"[GuideUI] Step 변경: {step.stepName}");
    }

    /// <summary>
    /// SubStep 시작 시 호출
    /// </summary>
    private void OnSubStepStarted(SubStepData subStep)
    {
        UpdateDescription(subStep.textInstruction);

        // ★ 시작 토글 숨기기 (20초 타임아웃으로 활성화된 경우 포함)
        // 가이드 스텝이 아니면 무조건 숨김
        if (startToggleObject != null && scenarioManager != null && scenarioManager.CurrentStep != null)
        {
            if (!scenarioManager.CurrentStep.IsGuideStep())
            {
                startToggleObject.SetActive(false);
                Debug.Log("[GuideUI] SubStep 시작 - 시작 토글 숨김 (가이드 스텝 아님)");
            }
        }

        // 시작 토글 초기화 (다음 SubStep으로 넘어갔으므로)
        ResetStartToggle();

        // Duration이 있는 경우 ProgressCircle 활성화
        HandleProgressCircleVisibility(subStep);
    }

    /// <summary>
    /// ProgressCircle 표시 여부 처리
    /// - duration > 0: ProgressCircle 표시
    /// - 토글이 표시되면 ProgressCircle 숨김
    /// - ★ HandTracking이 있으면 HoldProgressIndicator가 표시하므로 ProgressCircle 숨김
    /// </summary>
    private void HandleProgressCircleVisibility(SubStepData subStep)
    {
        if (progressCircleObject == null) return;

        // 가이드 스텝에서는 항상 ProgressCircle 숨김 (duration이 있어도 무시)
        if (scenarioManager != null && scenarioManager.CurrentStep != null && scenarioManager.CurrentStep.IsGuideStep())
        {
            HideProgressCircle();
            Debug.Log("[GuideUI] 가이드 스텝 - ProgressCircle 숨김 (duration 무시)");
            return;
        }

        // ★ HandTracking이 있는 경우 HoldProgressIndicator가 홀드 상태를 표시하므로 ProgressCircle 숨김
        // (등척성운동 등에서 홀드 타이머와 Duration 타이머가 겹치는 문제 해결)
        if (subStep.HasHandTracking())
        {
            HideProgressCircle();
            Debug.Log("[GuideUI] HandTracking 존재 - ProgressCircle 숨김 (HoldProgressIndicator가 홀드 표시)");
            return;
        }

        // Duration이 있는 경우 ProgressCircle 표시 (HandTracking이 없는 경우만)
        if (subStep.duration > 0)
        {
            StartProgress(subStep.duration);
        }
        else
        {
            HideProgressCircle();
        }
    }

    /// <summary>
    /// 진행 시작
    /// </summary>
    private void StartProgress(int duration)
    {
        if (progressCircleObject == null) return;

        currentDuration = duration;
        elapsedTime = 0f;
        isProgressActive = true;

        progressCircleObject.SetActive(true);

        // 완료 상태 숨김
        if (completeText != null)
            completeText.gameObject.SetActive(false);
        if (completeIcon != null)
            completeIcon.SetActive(false);

        // Duration 텍스트 표시
        if (durationText != null)
            durationText.gameObject.SetActive(true);

        Debug.Log($"[GuideUI] 진행 시작: {duration}초");
    }

    /// <summary>
    /// 진행 원형 UI 업데이트
    /// </summary>
    private void UpdateProgressCircle(float remainingTime, float progress)
    {
        // FillAmount 업데이트
        if (progressCircleFillImage != null)
        {
            progressCircleFillImage.fillAmount = progress;
        }

        // 남은 시간 텍스트 업데이트 (정수로 표시)
        if (durationText != null)
        {
            int seconds = Mathf.CeilToInt(remainingTime);
            durationText.text = seconds.ToString();
        }
    }

    /// <summary>
    /// 진행 완료
    /// </summary>
    private void CompleteProgress()
    {
        isProgressActive = false;

        // Duration 텍스트 숨김
        if (durationText != null)
            durationText.gameObject.SetActive(false);

        // 완료 상태 표시
        if (completeText != null)
            completeText.gameObject.SetActive(true);
        if (completeIcon != null)
            completeIcon.SetActive(true);

        Debug.Log($"[GuideUI] 진행 완료");
    }

    /// <summary>
    /// ProgressCircle 숨김
    /// </summary>
    private void HideProgressCircle()
    {
        if (progressCircleObject == null) return;

        isProgressActive = false;
        progressCircleObject.SetActive(false);

        Debug.Log($"[GuideUI] ProgressCircle 숨김");
    }

    /// <summary>
    /// Step 이름 업데이트
    /// - 가이드 스텝(stepNo == 0)은 stepName만 표시
    /// - 다른 스텝은 "stepNo. stepName" 형식으로 표시
    /// </summary>
    private void UpdateStepName(StepData step)
    {
        if (stepNameText != null)
        {
            // 가이드 스텝이 아닌 경우 stepNo 추가
            if (step.IsGuideStep())
            {
                stepNameText.text = step.stepName;
            }
            else
            {
                stepNameText.text = $"{step.stepNo}. {step.stepName}";
            }
        }
    }

    /// <summary>
    /// 설명 텍스트 업데이트
    /// </summary>
    private void UpdateDescription(string description)
    {
        if (descriptionText != null)
        {
            descriptionText.text = description;
        }
    }

    /// <summary>
    /// Phase 이미지 색상 업데이트
    /// 시나리오별 Phase 이름에 맞춰 동적으로 처리
    /// </summary>
    private void UpdatePhaseImages()
    {
        // Phase 이미지를 숨겨야 하는 Phase인지 확인
        if (ShouldHidePhaseImages())
        {
            HideAllPhaseImages();
            return;
        }

        // Phase 이미지 표시 및 색상 업데이트
        ShowAllPhaseImages();

        // 첫 번째 세부 부위 (전부, 쇄골 등)
        if (frontPhaseImage != null)
        {
            UpdateImageColor(frontPhaseImage, IsPhaseNameMatch(firstPhaseNames));
        }

        // 두 번째 세부 부위 (중부, 늑골 등)
        if (middlePhaseImage != null)
        {
            UpdateImageColor(middlePhaseImage, IsPhaseNameMatch(secondPhaseNames));
        }

        // 세 번째 세부 부위 (후부, 흉골 등)
        if (backPhaseImage != null)
        {
            UpdateImageColor(backPhaseImage, IsPhaseNameMatch(thirdPhaseNames));
        }
    }

    /// <summary>
    /// Phase 이미지를 숨겨야 하는지 확인
    /// </summary>
    private bool ShouldHidePhaseImages()
    {
        foreach (string hideName in hidePhaseNames)
        {
            if (currentPhaseName == hideName)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 현재 Phase가 주어진 이름 목록과 일치하는지 확인
    /// </summary>
    private bool IsPhaseNameMatch(string[] phaseNames)
    {
        foreach (string phaseName in phaseNames)
        {
            if (currentPhaseName == phaseName)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 모든 Phase 이미지 숨김
    /// </summary>
    private void HideAllPhaseImages()
    {
        if (frontPhaseImage != null)
            frontPhaseImage.gameObject.SetActive(false);

        if (middlePhaseImage != null)
            middlePhaseImage.gameObject.SetActive(false);

        if (backPhaseImage != null)
            backPhaseImage.gameObject.SetActive(false);
    }

    /// <summary>
    /// 모든 Phase 이미지 표시
    /// </summary>
    private void ShowAllPhaseImages()
    {
        if (frontPhaseImage != null)
            frontPhaseImage.gameObject.SetActive(true);

        if (middlePhaseImage != null)
            middlePhaseImage.gameObject.SetActive(true);

        if (backPhaseImage != null)
            backPhaseImage.gameObject.SetActive(true);
    }

    /// <summary>
    /// 이미지 색상 업데이트
    /// </summary>
    private void UpdateImageColor(Image image, bool isActive)
    {
        if (image == null) return;

        image.color = isActive ? activePhaseColor : inactivePhaseColor;
    }

    /// <summary>
    /// 시작 토글 표시 여부 업데이트
    /// - 토글이 표시될 때 ProgressCircle 숨김
    /// </summary>
    private void UpdateStartToggleVisibility(StepData step)
    {
        if (startToggleObject == null) return;

        // 가이드 스텝(stepNo == 0)에서만 시작 토글 표시
        bool shouldShow = step.IsGuideStep();
        startToggleObject.SetActive(shouldShow);

        // 토글 텍스트 업데이트
        if (shouldShow && startToggleText != null)
        {
            // 첫 번째 가이드인지 확인
            bool isFirstPhase = scenarioManager.CurrentPhase == scenarioManager.CurrentScenario.phases[0];
            startToggleText.text = isFirstPhase ? "시작" : "다음";
        }

        // 토글 상태 초기화 (꺼진 상태로)
        if (shouldShow && startToggle != null)
        {
            startToggle.isOn = false;
        }

        // 토글이 표시되면 ProgressCircle 숨김
        if (shouldShow)
        {
            HideProgressCircle();
        }
    }

    /// <summary>
    /// 시작 토글 변경 시
    /// </summary>
    private void OnStartToggleChanged(bool isOn)
    {
        // 토글이 켜졌을 때만 다음 단계로 진행
        if (isOn && scenarioManager != null)
        {
            Debug.Log("[GuideUI] 시작 토글 클릭 - 다음 SubStep으로 진행");
            scenarioManager.NextSubStep();

            // 토글 상태 초기화 (다음 클릭을 위해)
            startToggle.isOn = false;
        }
    }

    /// <summary>
    /// 수동으로 Step 이름 설정 (stepNo 없이 이름만)
    /// </summary>
    public void SetStepName(string stepName)
    {
        if (stepNameText != null)
        {
            stepNameText.text = stepName;
        }
    }

    /// <summary>
    /// 수동으로 Step 이름 설정 (StepData 사용)
    /// </summary>
    public void SetStepName(StepData step)
    {
        UpdateStepName(step);
    }

    /// <summary>
    /// 수동으로 Phase 설정
    /// </summary>
    public void SetCurrentPhase(string phaseName)
    {
        currentPhaseName = phaseName;
        UpdatePhaseImages();
    }

    /// <summary>
    /// Phase 이미지 색상 설정
    /// </summary>
    public void SetPhaseColors(Color activeColor, Color inactiveColor)
    {
        activePhaseColor = activeColor;
        inactivePhaseColor = inactiveColor;
        UpdatePhaseImages();
    }

    /// <summary>
    /// 수동으로 ProgressCircle 표시
    /// </summary>
    public void ShowProgressCircle(int duration)
    {
        StartProgress(duration);
    }

    /// <summary>
    /// 수동으로 ProgressCircle 숨김
    /// </summary>
    public void ForceHideProgressCircle()
    {
        HideProgressCircle();
    }

    /// <summary>
    /// 시작 토글 활성화 (20초 타임아웃 시 HandPosePlayer에서 호출)
    /// </summary>
    public void EnableStartToggle()
    {
        //if (startToggleObject == null) return;

        Debug.Log("<color=yellow>[GuideUI] 시작 토글 강제 활성화 (20초 타임아웃)</color>");

        // ProgressCircle 숨김
        HideProgressCircle();

        // 시작 토글 표시
        startToggleObject.SetActive(true);

        // 토글 텍스트 변경
        if (startToggleText != null)
        {
            startToggleText.text = "계속하기";
        }

        // 토글 상태 초기화
        if (startToggle != null)
        {
            startToggle.isOn = false;
        }
    }

    /// <summary>
    /// 시작 토글 상태 초기화 (항상 off로 리셋)
    /// </summary>
    public void ResetStartToggle()
    {
        if (startToggle != null)
        {
            startToggle.isOn = false;
            Debug.Log("[GuideUI] 시작 토글 초기화 (off)");
        }
    }

    // [REMOVED] 가이드 영상 토글 관련 코드는 InfoPanelController로 이전됨

    // ========== ★ 단계 완료 피드백 시스템 ==========

    /// <summary>
    /// 피드백 등급
    /// </summary>
    private enum FeedbackGrade
    {
        Excellent,  // 초과 달성 (목표 + 10% 이상)
        Good,       // 목표 달성
        Close,      // 근접 (80~99%)
        Poor        // 미달 (80% 미만)
    }

    /// <summary>
    /// 피드백 표시 (가이드 콘텐츠 숨기고 피드백 표시 → 2초 후 복귀)
    /// </summary>
    /// <param name="currentSimilarity">현재 유사도 (0~1)</param>
    /// <param name="targetSimilarity">목표 유사도 (0~1), null이면 DifficultyManager에서 가져옴</param>
    /// <returns>피드백 표시 시간 (초)</returns>
    public float ShowFeedback(float currentSimilarity, float? targetSimilarity = null)
    {
        // 목표 유사도 결정
        float target = targetSimilarity ?? GetTargetSimilarity();

        Debug.Log($"<color=cyan>[GuideUI] 피드백 표시: {currentSimilarity:P0} / 목표 {target:P0}</color>");

        // 이전 코루틴 중지
        if (feedbackCoroutine != null)
        {
            StopCoroutine(feedbackCoroutine);
        }

        // 피드백 표시 시작
        feedbackCoroutine = StartCoroutine(ShowFeedbackRoutine(currentSimilarity, target));

        return feedbackDisplayDuration;
    }

    /// <summary>
    /// DifficultyManager에서 목표 유사도 가져오기
    /// </summary>
    private float GetTargetSimilarity()
    {
        if (DifficultyManager.Instance != null)
        {
            return DifficultyManager.Instance.SimilarityThreshold;
        }

        // 기본값: 중급 기준
        return 0.65f;
    }

    /// <summary>
    /// 피드백 표시 코루틴
    /// 가이드 콘텐츠 숨김 → 피드백 표시 → 대기 → 피드백 숨김 → 가이드 콘텐츠 복귀
    /// </summary>
    private IEnumerator ShowFeedbackRoutine(float currentSimilarity, float targetSimilarity)
    {
        isFeedbackShowing = true;

        // 1. 가이드 콘텐츠 숨기기
        if (guideContentSection != null)
        {
            guideContentSection.SetActive(false);
        }

        // 2. 피드백 섹션 표시
        if (feedbackSection != null)
        {
            feedbackSection.SetActive(true);
        }

        // 3. 피드백 등급 결정
        FeedbackGrade grade = GetFeedbackGrade(currentSimilarity, targetSimilarity);

        // 4. 색상 및 메시지 설정
        Color feedbackColor = GetGradeColor(grade);
        string feedbackMessage = GetGradeMessage(grade);

        // 5. UI 업데이트 (애니메이션)
        yield return StartCoroutine(AnimateFeedback(currentSimilarity, targetSimilarity, feedbackColor, feedbackMessage));

        // 6. 표시 유지
        yield return new WaitForSeconds(feedbackDisplayDuration);

        // 7. 피드백 숨기고 가이드 콘텐츠 복귀
        HideFeedback();

        // 8. 완료 콜백 호출
        OnFeedbackComplete?.Invoke();
    }

    /// <summary>
    /// 피드백 애니메이션 (숫자 카운트업 + 프로그레스 바)
    /// </summary>
    private IEnumerator AnimateFeedback(float currentSimilarity, float targetSimilarity, Color color, string message)
    {
        float countUpDuration = 0.5f;
        float elapsed = 0f;

        // 초기값
        if (feedbackSimilarityText != null)
        {
            feedbackSimilarityText.text = "0%";
            feedbackSimilarityText.color = color;
        }

        if (feedbackProgressFill != null)
        {
            feedbackProgressFill.fillAmount = 0f;
            feedbackProgressFill.color = color;
        }

        if (feedbackTargetText != null)
        {
            feedbackTargetText.text = $"목표 {targetSimilarity:P0}";
        }

        if (feedbackMessageText != null)
        {
            feedbackMessageText.text = "";
        }

        // 카운트업 애니메이션
        while (elapsed < countUpDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / countUpDuration);

            // 이징 (ease out)
            float easedT = 1f - Mathf.Pow(1f - t, 3f);

            float displayValue = Mathf.Lerp(0f, currentSimilarity, easedT);

            if (feedbackSimilarityText != null)
            {
                feedbackSimilarityText.text = $"{displayValue:P0}";
            }

            if (feedbackProgressFill != null)
            {
                feedbackProgressFill.fillAmount = displayValue;
            }

            yield return null;
        }

        // 최종값 설정
        if (feedbackSimilarityText != null)
        {
            feedbackSimilarityText.text = $"{currentSimilarity:P0}";
        }

        if (feedbackProgressFill != null)
        {
            feedbackProgressFill.fillAmount = currentSimilarity;
        }

        // 피드백 메시지 표시 (약간의 딜레이)
        yield return new WaitForSeconds(0.2f);

        if (feedbackMessageText != null)
        {
            feedbackMessageText.text = message;
            feedbackMessageText.color = color;
        }
    }

    /// <summary>
    /// 피드백 숨기고 가이드 콘텐츠 복귀
    /// </summary>
    public void HideFeedback()
    {
        // 피드백 섹션 숨김
        if (feedbackSection != null)
        {
            feedbackSection.SetActive(false);
        }

        // 가이드 콘텐츠 복귀
        if (guideContentSection != null)
        {
            guideContentSection.SetActive(true);
        }

        isFeedbackShowing = false;

        Debug.Log("[GuideUI] 피드백 숨김, 가이드 콘텐츠 복귀");
    }

    /// <summary>
    /// 피드백 등급 결정
    /// </summary>
    private FeedbackGrade GetFeedbackGrade(float current, float target)
    {
        if (target <= 0) target = 0.65f;  // 0 방지

        float ratio = current / target;

        if (ratio >= 1.1f)  // 목표 + 10% 이상
            return FeedbackGrade.Excellent;
        else if (ratio >= 1.0f)  // 목표 달성
            return FeedbackGrade.Good;
        else if (ratio >= 0.8f)  // 80% 이상
            return FeedbackGrade.Close;
        else  // 80% 미만
            return FeedbackGrade.Poor;
    }

    /// <summary>
    /// 등급별 색상
    /// </summary>
    private Color GetGradeColor(FeedbackGrade grade)
    {
        switch (grade)
        {
            case FeedbackGrade.Excellent: return excellentColor;
            case FeedbackGrade.Good: return goodColor;
            case FeedbackGrade.Close: return closeColor;
            case FeedbackGrade.Poor: return poorColor;
            default: return goodColor;
        }
    }

    /// <summary>
    /// 등급별 메시지
    /// </summary>
    private string GetGradeMessage(FeedbackGrade grade)
    {
        switch (grade)
        {
            case FeedbackGrade.Excellent: return excellentMessage;
            case FeedbackGrade.Good: return goodMessage;
            case FeedbackGrade.Close: return closeMessage;
            case FeedbackGrade.Poor: return poorMessage;
            default: return goodMessage;
        }
    }

    /// <summary>
    /// 피드백 표시 중인지 확인
    /// </summary>
    public bool IsFeedbackShowing => isFeedbackShowing;
}