using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 시나리오 UI 컨트롤러
/// UI 표시만 전담 (로직 없음)
/// </summary>
public class ScenarioUIController : MonoBehaviour
{
    [Header("UI Elements")]
    [SerializeField] private TextMeshProUGUI scenarioNameText;
    [SerializeField] private TextMeshProUGUI stepDescriptionText;
    [SerializeField] private Button nextButton;
    [SerializeField] private TextMeshProUGUI nextButtonText;

    [Header("Progress UI (Optional)")]
    [Tooltip("도트 타임라인 - 없으면 진행도 표시 안 함")]
    [SerializeField] private DotTimelineController dotTimeline;

    [Header("Optional UI")]
    [SerializeField] private TextMeshProUGUI phaseText;
    [SerializeField] private GameObject loadingIndicator;

    [Header("Button Visual Feedback")]
    [SerializeField] private Color enabledColor = new Color(0.2f, 0.8f, 1f);
    [SerializeField] private Color disabledColor = new Color(0.5f, 0.5f, 0.5f);

    private ScenarioEventSystem eventSystem;
    private ScenarioManager scenarioManager;

    private void Awake()
    {
        eventSystem = ScenarioEventSystem.Instance;
        scenarioManager = FindObjectOfType<ScenarioManager>();

        // 버튼 이벤트 연결
        if (nextButton != null)
        {
            nextButton.onClick.AddListener(OnNextButtonClick);
        }
    }

    private void OnEnable()
    {
        // 이벤트 구독
        eventSystem.OnUIUpdateRequested += UpdateUI;
        eventSystem.OnProgressUpdateRequested += UpdateProgress;
        eventSystem.OnButtonStateUpdateRequested += UpdateButtonState;
        eventSystem.OnScenarioStarted += OnScenarioStarted;
        eventSystem.OnScenarioCompleted += OnScenarioCompleted;
        eventSystem.OnPhaseChanged += OnPhaseChanged;
    }

    private void OnDisable()
    {
        // 이벤트 구독 해제
        eventSystem.OnUIUpdateRequested -= UpdateUI;
        eventSystem.OnProgressUpdateRequested -= UpdateProgress;
        eventSystem.OnButtonStateUpdateRequested -= UpdateButtonState;
        eventSystem.OnScenarioStarted -= OnScenarioStarted;
        eventSystem.OnScenarioCompleted -= OnScenarioCompleted;
        eventSystem.OnPhaseChanged -= OnPhaseChanged;
    }

    /// <summary>
    /// UI 업데이트
    /// </summary>
    private void UpdateUI(string scenarioName, string stepDescription, string buttonText)
    {
        if (scenarioNameText != null)
        {
            scenarioNameText.text = scenarioName;
        }

        if (stepDescriptionText != null)
        {
            stepDescriptionText.text = stepDescription;
        }

        if (nextButtonText != null)
        {
            nextButtonText.text = buttonText;
        }
    }

    /// <summary>
    /// 진행도 업데이트
    /// </summary>
    private void UpdateProgress(int current, int total)
    {
        if (dotTimeline != null)
        {
            dotTimeline.SetTotalSteps(total);
            dotTimeline.SetCurrentStep(current);
        }
    }

    /// <summary>
    /// 버튼 상태 업데이트
    /// </summary>
    private void UpdateButtonState(bool isEnabled)
    {
        if (nextButton != null)
        {
            nextButton.interactable = isEnabled;

            // 비주얼 피드백
            var buttonImage = nextButton.GetComponent<Image>();
            if (buttonImage != null)
            {
                buttonImage.color = isEnabled ? enabledColor : disabledColor;
            }

            Debug.Log($"[UI] 다음 버튼 상태: {(isEnabled ? "활성화" : "비활성화")}");
        }
    }

    /// <summary>
    /// 시나리오 시작 시
    /// </summary>
    private void OnScenarioStarted(ScenarioData scenario)
    {
        if (loadingIndicator != null)
        {
            loadingIndicator.SetActive(false);
        }

        gameObject.SetActive(true);
    }

    /// <summary>
    /// 시나리오 완료 시
    /// </summary>
    private void OnScenarioCompleted(ScenarioData scenario)
    {
        Debug.Log("[UI] 시나리오 완료 UI 표시");
        // 완료 UI 표시 또는 숨김
        // gameObject.SetActive(false);
    }

    /// <summary>
    /// Phase 변경 시
    /// </summary>
    private void OnPhaseChanged(PhaseData phase)
    {
        if (phaseText != null)
        {
            phaseText.text = phase.phaseName;
        }
    }

    /// <summary>
    /// 다음 버튼 클릭 시
    /// </summary>
    private void OnNextButtonClick()
    {
        if (scenarioManager != null)
        {
            scenarioManager.NextSubStep();
        }
    }

    /// <summary>
    /// 수동 UI 업데이트 (디버그용)
    /// </summary>
    public void ManualUpdateUI(string scenarioName, string description)
    {
        if (scenarioNameText != null) scenarioNameText.text = scenarioName;
        if (stepDescriptionText != null) stepDescriptionText.text = description;
    }
}