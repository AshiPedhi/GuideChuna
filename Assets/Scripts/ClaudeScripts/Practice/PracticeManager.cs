using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 연습 모드 관리자
/// 실제 시나리오 흐름을 따라 모든 토글을 순서대로 점멸시켜 사용자가 따라하도록 유도
///
/// 진행 순서:
/// 1. UI 잡고 옮기기 (3회)
/// 2. 난이도 토글 → 실습모드 토글 (시나리오 시작, 가이드 패널 표시)
/// 3. 정보패널 토글 순차 점멸 (근골격 → 전문가영상 → 수행결과 → 설정)
/// 4. 설정 패널에서 환자 위치 조정
/// 5. 가이드 핸드 토글 → 홀드 연습 (3사이클)
/// 6. 완료 팝업
/// </summary>
public class PracticeManager : MonoBehaviour
{
    [Header("=== Step 1: UI 옮기기 ===")]
    [SerializeField] private Transform grabbableUI;
    private Vector3 uiInitialPosition;
    private Quaternion uiInitialRotation;

    [Header("=== UI 컨트롤러 참조 ===")]
    [Tooltip("InfoPanelController (모드선택, 콘텐츠/메뉴 토글)")]
    [SerializeField] private InfoPanelController infoPanelController;

    [Tooltip("ScenarioGuideUIController (가이드 패널, 시작 토글)")]
    [SerializeField] private ScenarioGuideUIController scenarioGuideUIController;

    [Tooltip("PracticeSettingsController (설정 패널)")]
    [SerializeField] private PracticeSettingsController practiceSettingsController;

    [Header("=== Step 2: 모드 선택 토글 ===")]
    [Tooltip("난이도 토글들 (Beginner, Intermediate, Advanced)")]
    [SerializeField] private List<Toggle> difficultyToggles;
    [Tooltip("실습모드 토글")]
    [SerializeField] private Toggle practiceToggle;
    [Tooltip("평가모드 토글")]
    [SerializeField] private Toggle evaluationToggle;

    [Header("=== Step 3: 콘텐츠/메뉴 토글 ===")]
    [Tooltip("근골격 토글")]
    [SerializeField] private Toggle skeletonToggle;
    [Tooltip("전문가 영상 토글")]
    [SerializeField] private Toggle expertVideoToggle;
    [Tooltip("수행결과 토글")]
    [SerializeField] private Toggle resultToggle;
    [Tooltip("설정 토글")]
    [SerializeField] private Toggle settingsToggle;
    [Tooltip("메인메뉴 토글")]
    [SerializeField] private Toggle mainMenuToggle;

    [Header("=== Step 4: 환자 위치 조정 ===")]
    [SerializeField] private Transform patientTransform;

    [Header("=== Step 5: 가이드/홀드 ===")]
    [Tooltip("시작 토글 (ScenarioGuideUIController)")]
    [SerializeField] private Toggle startToggle;
    [Tooltip("가이드 패널 GameObject")]
    [SerializeField] private GameObject scenarioProgressUI;
    [Tooltip("ChunaPathEvaluator (홀드 감지용)")]
    [SerializeField] private ChunaPathEvaluator chunaPathEvaluator;

    [Header("=== 완료 팝업 ===")]
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

    // 순차 토글 하이라이트
    private List<Toggle> sequentialToggles = new List<Toggle>();
    private int currentToggleIndex = 0;
    private Dictionary<Toggle, Color> originalToggleColors = new Dictionary<Toggle, Color>();
    private Coroutine blinkCoroutine;

    // 홀드 연습
    private bool isWaitingForHold = false;

    // 상수
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

        // 컨트롤러 자동 검색
        FindControllers();

        // 초기 위치 저장
        if (grabbableUI != null)
        {
            uiInitialPosition = grabbableUI.position;
            uiInitialRotation = grabbableUI.rotation;
        }

        // 토글 참조 자동 연결
        AutoConnectToggles();

        // 연습 시작
        StartStep(0);

        if (showDebugLogs)
            Debug.Log("[Practice] Initialized - 연습 모드 시작");
    }

    private void FindControllers()
    {
        if (infoPanelController == null)
            infoPanelController = FindFirstObjectByType<InfoPanelController>();

        if (scenarioGuideUIController == null)
            scenarioGuideUIController = FindFirstObjectByType<ScenarioGuideUIController>();

        if (practiceSettingsController == null)
            practiceSettingsController = FindFirstObjectByType<PracticeSettingsController>();

        if (chunaPathEvaluator == null)
            chunaPathEvaluator = FindFirstObjectByType<ChunaPathEvaluator>();

        if (exitPopupController == null)
            exitPopupController = FindFirstObjectByType<ExitPopupController>();
    }

    private void AutoConnectToggles()
    {
        // InfoPanelController에서 토글 참조 가져오기 (Reflection 또는 public 프로퍼티 필요)
        // 현재는 Inspector에서 수동 연결 필요
        // 토글들이 비어있으면 씬에서 이름으로 검색
        var allToggles = FindObjectsByType<Toggle>(FindObjectsSortMode.None);

        foreach (var toggle in allToggles)
        {
            string name = toggle.name.ToLower();

            // 난이도 토글
            if (name.Contains("beginner") || name.Contains("초급"))
            {
                if (!difficultyToggles.Contains(toggle))
                    difficultyToggles.Add(toggle);
            }
            else if (name.Contains("intermediate") || name.Contains("중급"))
            {
                if (!difficultyToggles.Contains(toggle))
                    difficultyToggles.Add(toggle);
            }
            else if (name.Contains("advanced") || name.Contains("상급"))
            {
                if (!difficultyToggles.Contains(toggle))
                    difficultyToggles.Add(toggle);
            }
            // 모드 토글
            else if (name.Contains("practice") && name.Contains("toggle"))
            {
                if (practiceToggle == null)
                    practiceToggle = toggle;
            }
            else if (name.Contains("evaluation") && name.Contains("toggle"))
            {
                if (evaluationToggle == null)
                    evaluationToggle = toggle;
            }
            // 콘텐츠 토글
            else if (name.Contains("skeleton"))
            {
                if (skeletonToggle == null)
                    skeletonToggle = toggle;
            }
            else if (name.Contains("expert") || name.Contains("video"))
            {
                if (expertVideoToggle == null)
                    expertVideoToggle = toggle;
            }
            else if (name.Contains("result"))
            {
                if (resultToggle == null)
                    resultToggle = toggle;
            }
            // 메뉴 토글
            else if (name.Contains("setting"))
            {
                if (settingsToggle == null)
                    settingsToggle = toggle;
            }
            else if (name.Contains("main") && name.Contains("menu"))
            {
                if (mainMenuToggle == null)
                    mainMenuToggle = toggle;
            }
            // 시작 토글
            else if (name.Contains("start") && name.Contains("toggle"))
            {
                if (startToggle == null)
                    startToggle = toggle;
            }
        }

        if (showDebugLogs)
            Debug.Log($"[Practice] 토글 자동 연결 완료 - 난이도: {difficultyToggles.Count}개");
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
            case 1: StartStep2_ModeSelection(); break;
            case 2: StartStep3_ContentToggles(); break;
            case 3: StartStep4_PatientPosition(); break;
            case 4: StartStep5_HoldPractice(); break;
            default: ShowCompletionPopup(); break;
        }
    }

    private void CompleteCurrentStep()
    {
        isStepActive = false;
        StopBlinking();

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

    #region Step 2: 모드 선택 (난이도 → 실습모드)

    private void StartStep2_ModeSelection()
    {
        // 순차 토글 리스트 구성: 난이도 하나 → 실습모드
        sequentialToggles.Clear();

        // 난이도 토글 중 첫 번째 (Beginner)
        if (difficultyToggles.Count > 0 && difficultyToggles[0] != null)
        {
            sequentialToggles.Add(difficultyToggles[0]);
        }

        // 실습모드 토글
        if (practiceToggle != null)
        {
            sequentialToggles.Add(practiceToggle);
        }

        if (sequentialToggles.Count == 0)
        {
            if (showDebugLogs)
                Debug.Log("[Practice] Step 2: 모드 선택 토글이 없어서 건너뜁니다.");
            CompleteCurrentStep();
            return;
        }

        currentToggleIndex = 0;
        SaveOriginalColors(sequentialToggles);
        SetupSequentialToggleListeners();

        if (showDebugLogs)
            Debug.Log($"[Practice] Step 2: 모드 선택 - 토글을 순서대로 눌러주세요 (총 {sequentialToggles.Count}개)");

        StartHighlightToggle(0);
    }

    #endregion

    #region Step 3: 콘텐츠/메뉴 토글 순차 점멸

    private void StartStep3_ContentToggles()
    {
        // 가이드 패널 표시 (시나리오 시작 시뮬레이션)
        if (scenarioProgressUI != null)
        {
            scenarioProgressUI.SetActive(true);
            if (showDebugLogs)
                Debug.Log("[Practice] 가이드 패널 표시");
        }

        // 콘텐츠/메뉴 토글 interactable 활성화
        EnableContentMenuToggles();

        // 순차 토글 리스트 구성: 근골격 → 전문가영상 → 수행결과 → 설정
        sequentialToggles.Clear();

        if (skeletonToggle != null)
            sequentialToggles.Add(skeletonToggle);
        if (expertVideoToggle != null)
            sequentialToggles.Add(expertVideoToggle);
        if (resultToggle != null)
            sequentialToggles.Add(resultToggle);
        if (settingsToggle != null)
            sequentialToggles.Add(settingsToggle);

        if (sequentialToggles.Count == 0)
        {
            if (showDebugLogs)
                Debug.Log("[Practice] Step 3: 콘텐츠 토글이 없어서 건너뜁니다.");
            CompleteCurrentStep();
            return;
        }

        currentToggleIndex = 0;
        SaveOriginalColors(sequentialToggles);
        SetupSequentialToggleListeners();

        if (showDebugLogs)
            Debug.Log($"[Practice] Step 3: 콘텐츠/메뉴 토글 - 순서대로 눌러주세요 (총 {sequentialToggles.Count}개)");

        StartHighlightToggle(0);
    }

    private void EnableContentMenuToggles()
    {
        // 콘텐츠 토글 활성화
        if (skeletonToggle != null) skeletonToggle.interactable = true;
        if (expertVideoToggle != null) expertVideoToggle.interactable = true;
        if (resultToggle != null) resultToggle.interactable = true;

        // 메뉴 토글 활성화
        if (settingsToggle != null) settingsToggle.interactable = true;
        if (mainMenuToggle != null) mainMenuToggle.interactable = true;
    }

    #endregion

    #region Step 4: 환자 위치 조정

    private void StartStep4_PatientPosition()
    {
        if (showDebugLogs)
            Debug.Log("[Practice] Step 4: 설정 패널에서 환자 위치를 조정해보세요");

        // 설정 패널이 열려있는지 확인
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

        // 시작 토글 점멸 (가이드 핸드 토글로 이동)
        sequentialToggles.Clear();

        if (startToggle != null)
        {
            sequentialToggles.Add(startToggle);
        }

        if (sequentialToggles.Count > 0)
        {
            currentToggleIndex = 0;
            SaveOriginalColors(sequentialToggles);
            SetupSequentialToggleListeners();
            StartHighlightToggle(0);

            if (showDebugLogs)
                Debug.Log("[Practice] Step 5: 시작 토글을 눌러 홀드 연습을 시작하세요");
        }
        else
        {
            // 시작 토글 없으면 바로 홀드 대기
            StartWaitingForHold();
        }
    }

    private void StartWaitingForHold()
    {
        isWaitingForHold = true;

        // ChunaPathEvaluator 이벤트 구독
        if (chunaPathEvaluator != null)
        {
            chunaPathEvaluator.OnPhaseChanged += OnEvaluationPhaseChanged;
        }

        if (showDebugLogs)
        {
            Debug.Log($"[Practice] 홀드 연습 시작 - 전체 동작을 수행하세요 (0/{HOLD_REQUIRED_COUNT})");
            Debug.Log("[Practice] 사이클: 시작홀드 → 이동 → 중간홀드 → 완료");
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

    #region 순차 토글 하이라이트 시스템

    private void SaveOriginalColors(List<Toggle> toggles)
    {
        originalToggleColors.Clear();
        foreach (var toggle in toggles)
        {
            if (toggle != null)
            {
                var image = toggle.GetComponent<Image>();
                if (image != null)
                    originalToggleColors[toggle] = image.color;
            }
        }
    }

    private void SetupSequentialToggleListeners()
    {
        // 각 토글에 리스너 추가
        for (int i = 0; i < sequentialToggles.Count; i++)
        {
            int index = i; // 클로저용 로컬 변수
            var toggle = sequentialToggles[i];
            if (toggle != null)
            {
                toggle.onValueChanged.AddListener((isOn) => OnSequentialToggleClicked(toggle, index, isOn));
            }
        }
    }

    private void OnSequentialToggleClicked(Toggle clickedToggle, int index, bool isOn)
    {
        if (!isStepActive) return;

        // 현재 하이라이트된 토글이고 켜졌을 때만 처리
        if (index == currentToggleIndex && isOn)
        {
            if (showDebugLogs)
                Debug.Log($"[Practice] 토글 클릭 완료: {clickedToggle.name} ({index + 1}/{sequentialToggles.Count})");

            // 다음 토글로
            currentToggleIndex++;

            if (currentToggleIndex >= sequentialToggles.Count)
            {
                // 모든 토글 완료
                StopBlinking();

                // Step 5에서 시작 토글 클릭 후 홀드 대기로 전환
                if (currentStep == 4)
                {
                    StartWaitingForHold();
                }
                else
                {
                    CompleteCurrentStep();
                }
            }
            else
            {
                // 다음 토글 하이라이트
                StartHighlightToggle(currentToggleIndex);
            }
        }
    }

    private void StartHighlightToggle(int index)
    {
        if (index >= sequentialToggles.Count) return;

        // 이전 점멸 중지
        StopBlinking();

        // 현재 토글 점멸 시작
        var currentToggle = sequentialToggles[index];
        if (currentToggle != null)
        {
            blinkCoroutine = StartCoroutine(BlinkToggle(currentToggle));

            if (showDebugLogs)
                Debug.Log($"[Practice] 토글 하이라이트: {currentToggle.name} ({index + 1}/{sequentialToggles.Count})");
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
        sequentialToggles.Clear();

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

        // 이벤트 구독 해제
        if (chunaPathEvaluator != null)
        {
            chunaPathEvaluator.OnPhaseChanged -= OnEvaluationPhaseChanged;
        }
    }
}
