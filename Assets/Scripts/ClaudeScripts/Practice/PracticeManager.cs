using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 연습 모드 관리자
/// 기존 UI 사용, 버튼 하이라이트, 홀드 감지 연동
/// </summary>
public class PracticeManager : MonoBehaviour
{
    [Header("=== Step 1: UI 옮기기 ===")]
    [SerializeField] private Transform grabbableUI;
    private Vector3 uiInitialPosition;
    private Quaternion uiInitialRotation;

    [Header("=== Step 2: 난이도 버튼 ===")]
    [Tooltip("난이도 토글들 (아무거나 클릭하면 반응 확인)")]
    [SerializeField] private List<Toggle> difficultyToggles;
    [Tooltip("실습모드 토글 (클릭하면 다음 단계로)")]
    [SerializeField] private Toggle practiceModeToggle;

    [Header("=== Step 3: 정보패널 토글 (순서대로 하이라이트) ===")]
    [Tooltip("순서대로 눌러야 할 토글들")]
    [SerializeField] private List<Toggle> panelToggles;

    [Header("=== Step 4: 환자 위치 조정 ===")]
    [SerializeField] private Toggle settingsToggle;
    [SerializeField] private Transform patientTransform;

    [Header("=== Step 5: 홀드 연습 ===")]
    [Tooltip("가이드 핸드 토글")]
    [SerializeField] private Toggle guideHandToggle;
    [Tooltip("ChunaPathEvaluator (홀드 감지용)")]
    [SerializeField] private ChunaPathEvaluator chunaPathEvaluator;

    [Header("=== 완료 팝업 ===")]
    [Tooltip("완료 시 표시할 팝업 (ExitPopupController 등)")]
    [SerializeField] private GameObject completionPopup;
    [Tooltip("또는 ExitPopupController 직접 연결")]
    [SerializeField] private ExitPopupController exitPopupController;

    [Header("=== 하이라이트 설정 ===")]
    [SerializeField] private float blinkInterval = 0.5f;
    [SerializeField] private Color highlightColor = new Color(1f, 0.8f, 0.2f, 1f);

    [Header("=== 설정 ===")]
    [SerializeField] private float stepTransitionDelay = 1.0f;
    [SerializeField] private bool showDebugLogs = true;

    // 상태
    private int currentStep = 0;
    private int currentCount = 0;
    private bool isStepActive = false;
    private bool difficultyClicked = false;

    // 하이라이트
    private ToggleHighlighter toggleHighlighter;
    private Dictionary<Toggle, Color> originalToggleColors = new Dictionary<Toggle, Color>();
    private Coroutine blinkCoroutine;
    private int currentHighlightIndex = 0;

    // 홀드 연습
    private bool isWaitingForHold = false;

    // 상수
    private const int TOTAL_STEPS = 5;
    private const int HOLD_REQUIRED_COUNT = 3;
    private const int UI_GRAB_REQUIRED = 3;

    void Start()
    {
        Initialize();
    }

    private void Initialize()
    {
        // 충돌 컴포넌트 비활성화
        DisableConflictingComponents();

        // 초기 위치 저장
        if (grabbableUI != null)
        {
            uiInitialPosition = grabbableUI.position;
            uiInitialRotation = grabbableUI.rotation;
        }

        // ToggleHighlighter 컴포넌트 추가
        toggleHighlighter = gameObject.AddComponent<ToggleHighlighter>();

        // 토글 리스너 설정
        SetupToggleListeners();

        // 연습 시작
        StartStep(0);

        if (showDebugLogs)
            Debug.Log("[Practice] Initialized - 연습 모드 시작");
    }

    private void DisableConflictingComponents()
    {
        // ScenarioManager 비활성화
        var scenarioManager = FindFirstObjectByType<ScenarioManager>();
        if (scenarioManager != null)
        {
            scenarioManager.enabled = false;
            if (showDebugLogs) Debug.Log("[Practice] ScenarioManager disabled");
        }

        // ScenarioConditionManager 비활성화
        var conditionManager = FindFirstObjectByType<ScenarioConditionManager>();
        if (conditionManager != null)
        {
            conditionManager.enabled = false;
            if (showDebugLogs) Debug.Log("[Practice] ScenarioConditionManager disabled");
        }

        // HandPoseTrainingController 비활성화
        var trainingController = FindFirstObjectByType<HandPoseTrainingController>();
        if (trainingController != null)
        {
            trainingController.enabled = false;
            if (showDebugLogs) Debug.Log("[Practice] HandPoseTrainingController disabled");
        }

        // TrainingResultTracker 비활성화
        var resultTracker = FindFirstObjectByType<TrainingResultTracker>();
        if (resultTracker != null)
        {
            resultTracker.enabled = false;
            if (showDebugLogs) Debug.Log("[Practice] TrainingResultTracker disabled");
        }

        // QuizPanel 비활성화
        var quizPanel = FindFirstObjectByType<QuizPanel>();
        if (quizPanel != null)
        {
            quizPanel.gameObject.SetActive(false);
            if (showDebugLogs) Debug.Log("[Practice] QuizPanel disabled");
        }
    }

    private void SetupToggleListeners()
    {
        // 난이도 토글 - 아무거나 클릭하면 반응
        foreach (var toggle in difficultyToggles)
        {
            if (toggle != null)
                toggle.onValueChanged.AddListener((_) => OnDifficultyToggleClicked());
        }

        // 실습모드 토글 - 다음 단계로
        if (practiceModeToggle != null)
            practiceModeToggle.onValueChanged.AddListener((_) => OnPracticeModeToggleClicked());

        // 설정 토글
        if (settingsToggle != null)
            settingsToggle.onValueChanged.AddListener((_) => OnSettingsToggleClicked());

        // 가이드 핸드 토글
        if (guideHandToggle != null)
            guideHandToggle.onValueChanged.AddListener((_) => OnGuideHandToggleClicked());
    }

    // Update 불필요 - 이벤트 기반으로 동작

    #region Step Management

    private void StartStep(int step)
    {
        currentStep = step;
        currentCount = 0;
        isStepActive = true;

        if (showDebugLogs)
            Debug.Log($"[Practice] === Step {step + 1} 시작 ===");

        switch (step)
        {
            case 0: StartStep1_UIGrab(); break;
            case 1: StartStep2_DifficultyButton(); break;
            case 2: StartStep3_PanelButtons(); break;
            case 3: StartStep4_PatientPosition(); break;
            case 4: StartStep5_HoldPractice(); break;
            default: ShowCompletionPopup(); break;
        }
    }

    private void CompleteCurrentStep()
    {
        isStepActive = false;

        if (showDebugLogs)
            Debug.Log($"[Practice] Step {currentStep + 1} 완료!");

        StartCoroutine(TransitionToNextStep());
    }

    private IEnumerator TransitionToNextStep()
    {
        yield return new WaitForSeconds(stepTransitionDelay);
        StartStep(currentStep + 1);
    }

    #endregion

    #region Step 1: UI 옮기기 (3회)

    private void StartStep1_UIGrab()
    {
        ResetUIPosition();

        if (showDebugLogs)
            Debug.Log($"[Practice] Step 1: UI를 잡아서 옮겨보세요 (0/{UI_GRAB_REQUIRED})");
    }

    /// <summary>
    /// UI Grab 완료 시 외부에서 호출 (UIGrabDetector에서)
    /// </summary>
    public void OnUIGrabReleased()
    {
        if (currentStep != 0 || !isStepActive) return;

        currentCount++;
        if (showDebugLogs)
            Debug.Log($"[Practice] UI 옮기기: {currentCount}/{UI_GRAB_REQUIRED}");

        if (currentCount >= UI_GRAB_REQUIRED)
        {
            CompleteCurrentStep();
        }
        else
        {
            ResetUIPosition();
        }
    }

    private void ResetUIPosition()
    {
        if (grabbableUI != null)
        {
            grabbableUI.position = uiInitialPosition;
            grabbableUI.rotation = uiInitialRotation;
        }
    }

    #endregion

    #region Step 2: 난이도 토글 + 실습모드 토글

    private void StartStep2_DifficultyButton()
    {
        difficultyClicked = false;

        if (showDebugLogs)
            Debug.Log("[Practice] Step 2: 난이도 토글을 눌러보세요. 그 다음 실습모드 토글을 누르세요.");
    }

    private void OnDifficultyToggleClicked()
    {
        if (currentStep != 1 || !isStepActive) return;

        if (!difficultyClicked)
        {
            difficultyClicked = true;
            if (showDebugLogs)
                Debug.Log("[Practice] 난이도 토글 반응 확인! 이제 실습모드 토글을 누르세요.");
        }
    }

    private void OnPracticeModeToggleClicked()
    {
        if (currentStep != 1 || !isStepActive) return;

        if (showDebugLogs)
            Debug.Log("[Practice] 실습모드 토글 클릭 - 다음 단계로!");

        CompleteCurrentStep();
    }

    #endregion

    #region Step 3: 정보패널 토글 (순서대로 하이라이트)

    private void StartStep3_PanelButtons()
    {
        currentHighlightIndex = 0;

        if (panelToggles == null || panelToggles.Count == 0)
        {
            if (showDebugLogs)
                Debug.Log("[Practice] Step 3: 패널 토글이 없어서 건너뜁니다.");
            CompleteCurrentStep();
            return;
        }

        // 원본 색상 저장
        originalToggleColors.Clear();
        foreach (var toggle in panelToggles)
        {
            if (toggle != null)
            {
                var image = toggle.GetComponent<Image>();
                if (image != null)
                    originalToggleColors[toggle] = image.color;
            }
        }

        if (showDebugLogs)
            Debug.Log($"[Practice] Step 3: 하이라이트된 토글을 순서대로 눌러보세요 (총 {panelToggles.Count}개)");

        // 첫 토글 하이라이트 시작
        StartHighlightToggle(0);
    }

    private void StartHighlightToggle(int index)
    {
        if (index >= panelToggles.Count)
        {
            // 모든 토글 완료
            CompleteCurrentStep();
            return;
        }

        currentHighlightIndex = index;

        // 이전 토글 색상 복원
        StopBlinking();

        // 현재 토글 점멸 시작
        var currentToggle = panelToggles[index];
        if (currentToggle != null)
        {
            // 클릭 리스너 추가
            currentToggle.onValueChanged.AddListener((_) => OnHighlightedToggleClicked(currentToggle));
            blinkCoroutine = StartCoroutine(BlinkToggle(currentToggle));

            if (showDebugLogs)
                Debug.Log($"[Practice] 토글 하이라이트: {currentToggle.name} ({index + 1}/{panelToggles.Count})");
        }
    }

    private IEnumerator BlinkToggle(Toggle toggle)
    {
        var image = toggle.GetComponent<Image>();
        if (image == null) yield break;

        Color originalColor = originalToggleColors.ContainsKey(toggle) ? originalToggleColors[toggle] : image.color;
        bool isOn = false;

        while (true)
        {
            isOn = !isOn;
            image.color = isOn ? highlightColor : originalColor;
            yield return new WaitForSeconds(blinkInterval);
        }
    }

    private void StopBlinking()
    {
        if (blinkCoroutine != null)
        {
            StopCoroutine(blinkCoroutine);
            blinkCoroutine = null;
        }

        // 모든 토글 원래 색상 복원
        foreach (var kvp in originalToggleColors)
        {
            if (kvp.Key != null)
            {
                var image = kvp.Key.GetComponent<Image>();
                if (image != null)
                    image.color = kvp.Value;
            }
        }
    }

    private void OnHighlightedToggleClicked(Toggle clickedToggle)
    {
        if (currentStep != 2 || !isStepActive) return;

        // 현재 하이라이트된 토글인지 확인
        if (currentHighlightIndex < panelToggles.Count && panelToggles[currentHighlightIndex] == clickedToggle)
        {
            if (showDebugLogs)
                Debug.Log($"[Practice] 토글 클릭 완료: {clickedToggle.name}");

            // 다음 토글로
            StartHighlightToggle(currentHighlightIndex + 1);
        }
    }

    #endregion

    #region Step 4: 환자 위치 조정

    private void StartStep4_PatientPosition()
    {
        if (showDebugLogs)
            Debug.Log("[Practice] Step 4: 설정 토글을 눌러 환자 위치를 조정해보세요");
    }

    private void OnSettingsToggleClicked()
    {
        if (currentStep != 3 || !isStepActive) return;

        // 설정 창이 열리면 환자 위치 변경 감지 시작
        StartCoroutine(WaitForPatientPositionChange());
    }

    private IEnumerator WaitForPatientPositionChange()
    {
        if (patientTransform == null)
        {
            yield return new WaitForSeconds(1f);
            CompleteCurrentStep();
            yield break;
        }

        Vector3 startPos = patientTransform.position;
        float timeout = 30f;
        float elapsed = 0f;

        while (elapsed < timeout)
        {
            if (Vector3.Distance(patientTransform.position, startPos) > 0.01f)
            {
                if (showDebugLogs)
                    Debug.Log("[Practice] 환자 위치 변경 감지!");
                CompleteCurrentStep();
                yield break;
            }
            elapsed += Time.deltaTime;
            yield return null;
        }

        // 타임아웃 시에도 완료 처리
        CompleteCurrentStep();
    }

    /// <summary>
    /// 외부에서 환자 위치 변경 시 호출
    /// </summary>
    public void OnPatientPositionChanged()
    {
        if (currentStep != 3 || !isStepActive) return;

        if (showDebugLogs)
            Debug.Log("[Practice] 환자 위치 변경됨!");
        CompleteCurrentStep();
    }

    #endregion

    #region Step 5: 홀드 연습 (전체 사이클 3회)

    private void StartStep5_HoldPractice()
    {
        currentCount = 0;
        isWaitingForHold = false;

        // ChunaPathEvaluator 이벤트 구독
        if (chunaPathEvaluator != null)
        {
            chunaPathEvaluator.OnPhaseChanged += OnEvaluationPhaseChanged;
        }

        if (showDebugLogs)
            Debug.Log($"[Practice] Step 5: 가이드 핸드를 켜고 전체 동작을 수행하세요 (0/{HOLD_REQUIRED_COUNT})");
        Debug.Log("[Practice] 사이클: 시작홀드 → 이동 → 중간홀드 → 완료");
    }

    private void OnGuideHandToggleClicked()
    {
        if (currentStep != 4 || !isStepActive) return;

        // 토글이 켜질 때만 반응
        if (guideHandToggle != null && guideHandToggle.isOn)
        {
            isWaitingForHold = true;

            if (showDebugLogs)
                Debug.Log("[Practice] 가이드 핸드 활성화 - 전체 동작(홀드→이동→홀드→완료)을 수행하세요");
        }
    }

    /// <summary>
    /// ChunaPathEvaluator 단계 변경 이벤트 핸들러
    /// </summary>
    private void OnEvaluationPhaseChanged(ChunaPathEvaluator.EvaluationPhase newPhase)
    {
        if (currentStep != 4 || !isStepActive || !isWaitingForHold) return;

        if (showDebugLogs)
            Debug.Log($"[Practice] 평가 단계 변경: {newPhase}");

        // Completed 상태가 되면 1회 사이클 완료
        if (newPhase == ChunaPathEvaluator.EvaluationPhase.Completed)
        {
            currentCount++;
            if (showDebugLogs)
                Debug.Log($"[Practice] ★ 사이클 {currentCount}/{HOLD_REQUIRED_COUNT} 완료!");

            if (currentCount >= HOLD_REQUIRED_COUNT)
            {
                // 이벤트 구독 해제
                if (chunaPathEvaluator != null)
                {
                    chunaPathEvaluator.OnPhaseChanged -= OnEvaluationPhaseChanged;
                }

                isWaitingForHold = false;
                CompleteCurrentStep();
            }
            else
            {
                // 다음 사이클을 위해 리셋
                if (showDebugLogs)
                    Debug.Log($"[Practice] 다음 사이클을 시작하세요... ({currentCount}/{HOLD_REQUIRED_COUNT})");
            }
        }
    }

    /// <summary>
    /// 외부에서 사이클 완료 시 호출 (수동)
    /// </summary>
    public void OnCycleCompleted()
    {
        if (currentStep != 4 || !isStepActive) return;

        currentCount++;
        if (showDebugLogs)
            Debug.Log($"[Practice] 사이클 완료! ({currentCount}/{HOLD_REQUIRED_COUNT})");

        if (currentCount >= HOLD_REQUIRED_COUNT)
        {
            if (chunaPathEvaluator != null)
            {
                chunaPathEvaluator.OnPhaseChanged -= OnEvaluationPhaseChanged;
            }
            isWaitingForHold = false;
            CompleteCurrentStep();
        }
    }

    #endregion

    #region Completion

    private void ShowCompletionPopup()
    {
        isStepActive = false;

        if (showDebugLogs)
            Debug.Log("[Practice] ★★★ 모든 연습 완료! ★★★");

        // ExitPopupController 사용
        if (exitPopupController != null)
        {
            exitPopupController.ShowPopup();
        }
        // 또는 직접 팝업 활성화
        else if (completionPopup != null)
        {
            completionPopup.SetActive(true);
        }
        else
        {
            // 팝업이 없으면 ExitPopupController 찾아서 사용
            var popup = FindFirstObjectByType<ExitPopupController>();
            if (popup != null)
            {
                popup.ShowPopup();
            }
            else
            {
                Debug.LogWarning("[Practice] 완료 팝업을 찾을 수 없습니다!");
            }
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 연습 재시작
    /// </summary>
    public void RestartPractice()
    {
        StopAllCoroutines();
        StopBlinking();
        currentStep = 0;
        currentCount = 0;
        isStepActive = false;
        isWaitingForHold = false;

        StartStep(0);

        if (showDebugLogs)
            Debug.Log("[Practice] 연습 재시작");
    }

    /// <summary>
    /// 현재 단계 정보
    /// </summary>
    public int GetCurrentStep() => currentStep;
    public int GetCurrentCount() => currentCount;
    public bool IsActive() => isStepActive;

    #endregion

    void OnDestroy()
    {
        StopBlinking();
    }
}
