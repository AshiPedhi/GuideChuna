using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

/// <summary>
/// 시나리오 가이드 UI 전용 컨트롤러 (시나리오 진행 UI)
///
/// [역할]
/// - stepName 표시
/// - Phase 이미지 (중부/전부/후부) 진행 상태 표시
/// - 시작 토글 제어 (가이드 스텝에서만 표시)
/// - 진행 원형 표시 (Duration)
/// - 가이드 콘텐츠 섹션 제어 (피드백 표시 시 숨김/복원)
///
/// [참고]
/// - 가이드 영상 토글은 InfoPanelController에서 처리합니다.
/// - 피드백 UI는 별도의 StepFeedbackUI 컴포넌트에서 처리합니다.
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

    [Header("=== 가이드 콘텐츠 섹션 ===")]
    [Tooltip("안내 콘텐츠 섹션 (피드백 시 숨김/복원용)")]
    [SerializeField] private GameObject guideContentSection;

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

    void Awake()
    {
        eventSystem = ScenarioEventSystem.Instance;
        scenarioManager = FindFirstObjectByType<ScenarioManager>();

        // 시작 토글 이벤트 연결
        if (startToggle != null)
        {
            startToggle.onValueChanged.AddListener(OnStartToggleChanged);
        }
    }

    void OnEnable()
    {
        if (eventSystem == null) eventSystem = ScenarioEventSystem.Instance;

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

        // 시작 토글 리스너 해제 (PracticeManager 간섭 방지)
        if (startToggle != null)
        {
            startToggle.onValueChanged.RemoveListener(OnStartToggleChanged);
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

        ChunaLogger.Log($"[GuideUI] Phase 변경: {currentPhaseName}");
    }

    /// <summary>
    /// Step 변경 시 호출
    /// </summary>
    private void OnStepChanged(StepData step)
    {
        UpdateStepName(step);
        UpdateStartToggleVisibility(step);

        ChunaLogger.Log($"[GuideUI] Step 변경: {step.stepName}");
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
                ChunaLogger.Log("[GuideUI] SubStep 시작 - 시작 토글 숨김 (가이드 스텝 아님)");
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
    /// - ★ 나래이션이 있으면 ProgressCircle 숨김 (나래이션 완료까지 대기하므로 duration 의미 없음)
    /// </summary>
    private void HandleProgressCircleVisibility(SubStepData subStep)
    {
        if (progressCircleObject == null) return;

        // 가이드 스텝에서는 항상 ProgressCircle 숨김 (duration이 있어도 무시)
        if (scenarioManager != null && scenarioManager.CurrentStep != null && scenarioManager.CurrentStep.IsGuideStep())
        {
            HideProgressCircle();
            ChunaLogger.Log("[GuideUI] 가이드 스텝 - ProgressCircle 숨김 (duration 무시)");
            return;
        }

        // ★ 나래이션이 있는 경우 ProgressCircle 숨김
        // (나래이션 완료까지 대기하므로 duration 타이머가 의미 없음)
        if (subStep.HasNarration())
        {
            HideProgressCircle();
            ChunaLogger.Log("[GuideUI] 나래이션 존재 - ProgressCircle 숨김 (나래이션 완료까지 대기)");
            return;
        }

        // ★ HandTracking이 있는 경우 HoldProgressIndicator가 홀드 상태를 표시하므로 ProgressCircle 숨김
        // (등척성운동 등에서 홀드 타이머와 Duration 타이머가 겹치는 문제 해결)
        if (subStep.HasHandTracking())
        {
            HideProgressCircle();
            ChunaLogger.Log("[GuideUI] HandTracking 존재 - ProgressCircle 숨김 (HoldProgressIndicator가 홀드 표시)");
            return;
        }

        // Duration이 있는 경우 ProgressCircle 표시 (HandTracking, 나래이션 없는 경우만)
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

        ChunaLogger.Log($"[GuideUI] 진행 시작: {duration}초");
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

        ChunaLogger.Log($"[GuideUI] 진행 완료");
    }

    /// <summary>
    /// ProgressCircle 숨김
    /// </summary>
    private void HideProgressCircle()
    {
        if (progressCircleObject == null) return;

        isProgressActive = false;
        progressCircleObject.SetActive(false);

        ChunaLogger.Log($"[GuideUI] ProgressCircle 숨김");
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
            // ★ 난이도 설정에서 텍스트 설명 비활성화 시 빈 문자열
            bool showDesc = ChunaTraining.DifficultyManager.Instance == null
                || ChunaTraining.DifficultyManager.Instance.ShowStepDescription;
            descriptionText.text = showDesc ? description : "";
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
            bool isFirstPhase = scenarioManager.CurrentScenario != null
                && scenarioManager.CurrentPhase == scenarioManager.CurrentScenario.phases[0];
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
        // 토글이 켜졌을 때만 다음 단계로 진행 (ScenarioManager가 활성화 상태일 때만)
        if (isOn && scenarioManager != null && scenarioManager.enabled && scenarioManager.CurrentStep != null)
        {
            ChunaLogger.Log("[GuideUI] 시작 토글 클릭 - 다음 SubStep으로 진행");
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
        if (startToggleObject == null) return;

        ChunaLogger.Log("<color=yellow>[GuideUI] 시작 토글 강제 활성화 (20초 타임아웃)</color>");

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
            ChunaLogger.Log("[GuideUI] 시작 토글 초기화 (off)");
        }
    }

    // [REMOVED] 가이드 영상 토글 관련 코드는 InfoPanelController로 이전됨

    // ========== ★ 가이드 콘텐츠 섹션 제어 (분리된 피드백 UI용) ==========

    /// <summary>
    /// 가이드 콘텐츠 섹션 숨김 (피드백 표시 전 호출)
    /// </summary>
    public void HideGuideContent()
    {
        if (guideContentSection != null)
        {
            guideContentSection.SetActive(false);
            ChunaLogger.Log("[GuideUI] 가이드 콘텐츠 숨김");
        }
    }

    /// <summary>
    /// 가이드 콘텐츠 섹션 복원 (피드백 완료 후 호출)
    /// </summary>
    public void ShowGuideContent()
    {
        if (guideContentSection != null)
        {
            guideContentSection.SetActive(true);
            ChunaLogger.Log("[GuideUI] 가이드 콘텐츠 복원");
        }
    }

    /// <summary>
    /// 가이드 콘텐츠 섹션 참조 반환
    /// </summary>
    public GameObject GuideContentSection => guideContentSection;
}