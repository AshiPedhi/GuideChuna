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
    private HashSet<Toggle> clickedToggles = new HashSet<Toggle>(); // ★ 클릭된 토글 (색상 복원 제외)
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

        // ★ 모든 콘텐츠 토글 초기 상태 OFF로 설정 (최초에 skeleton이 on인 문제 해결)
        InitializeAllToggleStates();

        // ★ 가이드 패널 처음부터 표시 (멘트 및 카운트 표시용)
        if (scenarioProgressUI != null)
        {
            scenarioProgressUI.SetActive(true);
            if (showDebugLogs)
                Debug.Log("[Practice] 가이드 패널 표시 (연습 안내용)");
        }
        else
        {
            Debug.LogWarning("[Practice] 가이드 패널(scenarioProgressUI)을 찾을 수 없습니다!");
        }

        // ★ 모든 토글 초기 비활성화 (메인메뉴 제외)
        DisableAllTogglesExceptMainMenu();

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

        // ★ 가이드 패널 자동 검색 (실제 UI 캔버스 오브젝트 - "가이드패널Root" 또는 "가이드패널")
        // 주의: ScenarioGuideUIController는 GameManager에 있지만, 실제 UI 캔버스는 별도 오브젝트임
        if (scenarioProgressUI == null)
        {
            var allObjects = FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            foreach (var obj in allObjects)
            {
                string name = obj.name;
                // 정확한 이름 매칭 우선
                if (name == "가이드패널Root" || name == "가이드패널")
                {
                    scenarioProgressUI = obj;
                    break;
                }
            }

            // 정확한 이름 없으면 패턴으로 검색
            if (scenarioProgressUI == null)
            {
                foreach (var obj in allObjects)
                {
                    string nameLower = obj.name.ToLower();
                    if (nameLower.Contains("가이드패널") || nameLower.Contains("guidepanel"))
                    {
                        scenarioProgressUI = obj;
                        break;
                    }
                }
            }

            if (scenarioProgressUI != null && showDebugLogs)
                Debug.Log($"[Practice] 가이드 패널 자동 검색됨: {scenarioProgressUI.name}");
            else if (showDebugLogs)
                Debug.LogWarning("[Practice] 가이드 패널을 찾을 수 없습니다! (가이드패널Root 또는 가이드패널 오브젝트 필요)");
        }
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

    #region 토글 Interactable 제어

    /// <summary>
    /// 모든 토글 비활성화 (메인메뉴 제외 - 언제든 나갈 수 있게)
    /// </summary>
    private void DisableAllTogglesExceptMainMenu()
    {
        // 난이도 토글
        foreach (var toggle in difficultyToggles)
        {
            if (toggle != null) toggle.interactable = false;
        }

        // 모드 토글
        if (practiceToggle != null) practiceToggle.interactable = false;
        if (evaluationToggle != null) evaluationToggle.interactable = false;

        // 콘텐츠 토글
        if (skeletonToggle != null) skeletonToggle.interactable = false;
        if (expertVideoToggle != null) expertVideoToggle.interactable = false;
        if (resultToggle != null) resultToggle.interactable = false;

        // 메뉴 토글 (설정은 비활성화, 메인메뉴는 항상 활성화)
        if (settingsToggle != null) settingsToggle.interactable = false;
        if (mainMenuToggle != null) mainMenuToggle.interactable = true; // ★ 항상 활성화

        // 시작 토글
        if (startToggle != null) startToggle.interactable = false;
    }

    /// <summary>
    /// 특정 토글들만 활성화 (현재 단계에서 필요한 토글)
    /// </summary>
    private void EnableOnlyTheseToggles(params Toggle[] togglesToEnable)
    {
        DisableAllTogglesExceptMainMenu();

        foreach (var toggle in togglesToEnable)
        {
            if (toggle != null) toggle.interactable = true;
        }
    }

    /// <summary>
    /// 모든 토글 초기 상태 설정 (시작 시 모두 OFF)
    /// </summary>
    private void InitializeAllToggleStates()
    {
        // 난이도 토글 모두 off
        foreach (var toggle in difficultyToggles)
        {
            if (toggle != null) toggle.SetIsOnWithoutNotify(false);
        }

        // 모드 토글 off
        if (practiceToggle != null) practiceToggle.SetIsOnWithoutNotify(false);
        if (evaluationToggle != null) evaluationToggle.SetIsOnWithoutNotify(false);

        // 콘텐츠 토글 모두 off
        if (skeletonToggle != null) skeletonToggle.SetIsOnWithoutNotify(false);
        if (expertVideoToggle != null) expertVideoToggle.SetIsOnWithoutNotify(false);
        if (resultToggle != null) resultToggle.SetIsOnWithoutNotify(false);

        // 메뉴 토글 off
        if (settingsToggle != null) settingsToggle.SetIsOnWithoutNotify(false);
        if (mainMenuToggle != null) mainMenuToggle.SetIsOnWithoutNotify(false);

        // 시작 토글 off
        if (startToggle != null) startToggle.SetIsOnWithoutNotify(false);

        if (showDebugLogs)
            Debug.Log("[Practice] 모든 토글 초기 상태 OFF로 설정");
    }

    /// <summary>
    /// 콘텐츠 토글 상태 초기화 (모두 off → skeleton만 on)
    /// </summary>
    private void ResetContentToggleStates()
    {
        // 모든 콘텐츠 토글 off
        if (skeletonToggle != null) skeletonToggle.SetIsOnWithoutNotify(false);
        if (expertVideoToggle != null) expertVideoToggle.SetIsOnWithoutNotify(false);
        if (resultToggle != null) resultToggle.SetIsOnWithoutNotify(false);
        if (settingsToggle != null) settingsToggle.SetIsOnWithoutNotify(false);

        // skeleton만 on
        if (skeletonToggle != null) skeletonToggle.SetIsOnWithoutNotify(true);

        if (showDebugLogs)
            Debug.Log("[Practice] 콘텐츠 토글 상태 초기화 (skeleton만 on)");
    }

    /// <summary>
    /// 현재 순차 토글만 활성화
    /// </summary>
    private void EnableCurrentToggleOnly()
    {
        if (currentToggleIndex >= 0 && currentToggleIndex < sequentialToggles.Count)
        {
            Toggle currentToggle = sequentialToggles[currentToggleIndex];
            EnableOnlyTheseToggles(currentToggle);
        }
    }

    #endregion

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

        // ★ LateralFlexionDetector 비활성화 (연습 모드에서 간섭 방지)
        var lateralFlexionDetector = FindFirstObjectByType<LateralFlexionDetector>();
        if (lateralFlexionDetector != null)
        {
            lateralFlexionDetector.enabled = false;
            if (showDebugLogs) Debug.Log("[Practice] LateralFlexionDetector disabled");
        }

        // ★ ChunaPathEvaluator는 Step 5 홀드 연습 전까지 비활성화
        if (chunaPathEvaluator != null)
        {
            chunaPathEvaluator.enabled = false;
            if (showDebugLogs) Debug.Log("[Practice] ChunaPathEvaluator disabled (will enable at Step 5)");
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

        // ★ Step 3 완료 시 콘텐츠 토글 상태 초기화
        if (currentStep == 2)
        {
            OnStep3Completed();
        }

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
        // Step 1에서는 토글 조작 불필요 - 모두 비활성화 (메인메뉴 제외)
        DisableAllTogglesExceptMainMenu();

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

    #region Step 2: 모드 선택 (난이도 3개 → 실습모드)

    private void StartStep2_ModeSelection()
    {
        // ★ 순차 토글 리스트 구성: 난이도 3개(초급→중급→상급) → 실습모드
        sequentialToggles.Clear();

        // ★ 모든 난이도 토글 추가 (초급, 중급, 상급 순서대로)
        foreach (var diffToggle in difficultyToggles)
        {
            if (diffToggle != null)
            {
                sequentialToggles.Add(diffToggle);
            }
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

        // ★ 현재 단계 토글만 활성화 (첫 번째: 난이도 토글)
        EnableCurrentToggleOnly();

        currentToggleIndex = 0;
        SaveOriginalColors(sequentialToggles);
        SetupSequentialToggleListeners();

        if (showDebugLogs)
            Debug.Log($"[Practice] Step 2: 난이도(초급→중급→상급) → 실습모드 순서로 눌러주세요 (총 {sequentialToggles.Count}개)");

        StartHighlightToggle(0);
    }

    #endregion

    #region Step 3: 콘텐츠/메뉴 토글 순차 점멸

    private void StartStep3_ContentToggles()
    {
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

        // ★ 현재 단계 토글만 활성화 (첫 번째: skeleton)
        EnableCurrentToggleOnly();

        currentToggleIndex = 0;
        SaveOriginalColors(sequentialToggles);
        SetupSequentialToggleListeners();

        if (showDebugLogs)
            Debug.Log($"[Practice] Step 3: 콘텐츠/메뉴 토글 - 순서대로 눌러주세요 (총 {sequentialToggles.Count}개)");

        StartHighlightToggle(0);
    }

    /// <summary>
    /// Step 3 완료 후 호출 - 콘텐츠 토글 상태 초기화
    /// </summary>
    private void OnStep3Completed()
    {
        // ★ 모든 콘텐츠 토글 off → skeleton만 on 으로 초기화
        ResetContentToggleStates();
    }

    #endregion

    #region Step 4: 환자 위치 조정

    private void StartStep4_PatientPosition()
    {
        // ★ 설정 토글만 활성화 (환자 위치 조정을 위해)
        EnableOnlyTheseToggles(settingsToggle);

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
            // ★ 시작 토글만 활성화
            EnableCurrentToggleOnly();

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

        // ★ 홀드 연습 중에는 토글 조작 불필요 (메인메뉴만 활성화)
        DisableAllTogglesExceptMainMenu();

        // ★ ChunaPathEvaluator 활성화 (Step 5에서만 사용)
        if (chunaPathEvaluator != null)
        {
            chunaPathEvaluator.enabled = true;
            chunaPathEvaluator.OnPhaseChanged += OnEvaluationPhaseChanged;
            if (showDebugLogs)
                Debug.Log("[Practice] ChunaPathEvaluator 활성화");
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
        clickedToggles.Clear(); // ★ 클릭된 토글 목록도 초기화

        foreach (var toggle in toggles)
        {
            if (toggle != null)
            {
                // ★ 토글의 targetGraphic 또는 하위 Image 사용
                var image = GetToggleImage(toggle);
                if (image != null)
                    originalToggleColors[toggle] = image.color;
            }
        }
    }

    /// <summary>
    /// 토글의 점멸용 이미지 가져오기 (targetGraphic 우선, 없으면 하위 Image)
    /// </summary>
    private Image GetToggleImage(Toggle toggle)
    {
        if (toggle == null) return null;

        // 1. targetGraphic이 Image인 경우
        if (toggle.targetGraphic != null && toggle.targetGraphic is Image targetImage)
        {
            return targetImage;
        }

        // 2. 하위 자식에서 Image 찾기
        return toggle.GetComponentInChildren<Image>();
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
            // ★ 클릭된 토글 기록 (색상 복원 제외용)
            clickedToggles.Add(clickedToggle);

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

        currentToggleIndex = index;

        // 이전 점멸 중지
        StopBlinking();

        // ★ 현재 토글만 활성화 (다른 토글은 비활성화)
        EnableCurrentToggleOnly();

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
        // ★ 토글의 targetGraphic 또는 하위 Image 사용
        var image = GetToggleImage(toggle);
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

        // 모든 토글 원래 색상 복원 (★ 클릭된 토글은 제외 - 토글 자체 색상 관리에 맡김)
        foreach (var kvp in originalToggleColors)
        {
            if (kvp.Key != null && !clickedToggles.Contains(kvp.Key))
            {
                // ★ 토글의 targetGraphic 또는 하위 Image 사용
                var image = GetToggleImage(kvp.Key);
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
        clickedToggles.Clear(); // ★ 클릭된 토글 목록도 초기화

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
