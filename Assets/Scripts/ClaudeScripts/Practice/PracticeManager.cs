using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 연습 모드 관리자 (튜토리얼)
/// 모든 토글을 순서대로 점멸시켜 사용자가 따라하도록 유도
///
/// 진행 순서:
/// 1. UI 잡고 옮기기 (3회)
/// 2. 난이도 토글 (중급→상급→초급) → 실습모드 토글
/// 3. 콘텐츠 토글 (비디오→결과→골격)
/// 4. 설정 토글 → 자동조절 → 환자위치조절
/// 5. 환자 위치 조정 → 설정 닫기 → 시작 토글
/// 6. 핸드 가이드 홀드 연습 (3사이클)
/// 7. 나가기(메인메뉴) 토글 → 완료
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
    [Tooltip("난이도 토글들 (순서: 초급, 중급, 상급)")]
    [SerializeField] private List<Toggle> difficultyToggles;
    [Tooltip("실습모드 토글")]
    [SerializeField] private Toggle practiceToggle;
    [Tooltip("평가모드 토글")]
    [SerializeField] private Toggle evaluationToggle;

    [Header("=== Step 3: 콘텐츠 토글 ===")]
    [Tooltip("근골격 토글")]
    [SerializeField] private Toggle skeletonToggle;
    [Tooltip("전문가 영상 토글")]
    [SerializeField] private Toggle expertVideoToggle;
    [Tooltip("수행결과 토글")]
    [SerializeField] private Toggle resultToggle;

    [Header("=== Step 4: 설정 패널 토글 ===")]
    [Tooltip("설정 토글 (정보패널)")]
    [SerializeField] private Toggle settingsToggle;
    [Tooltip("자동조절 토글 (설정패널 내)")]
    [SerializeField] private Toggle autoAdjustToggle;
    [Tooltip("환자위치조절 토글 (설정패널 내)")]
    [SerializeField] private Toggle patientPositionToggle;

    [Header("=== Step 5: 시작 ===")]
    [Tooltip("시작 토글 (ScenarioGuideUIController)")]
    [SerializeField] private Toggle startToggle;
    [Tooltip("가이드 패널 GameObject")]
    [SerializeField] private GameObject scenarioProgressUI;

    [Header("=== Step 6: 홀드 연습 ===")]
    [Tooltip("ChunaPathEvaluator (홀드 감지용)")]
    [SerializeField] private ChunaPathEvaluator chunaPathEvaluator;
    [Tooltip("환자 Transform (위치 변경 감지용)")]
    [SerializeField] private Transform patientTransform;

    [Header("=== Step 7: 나가기 ===")]
    [Tooltip("메인메뉴 토글")]
    [SerializeField] private Toggle mainMenuToggle;
    [Tooltip("완료 팝업")]
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
    private HashSet<Toggle> clickedToggles = new HashSet<Toggle>();
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

        // ★ 초기 상태 설정 (난이도: 초급 ON, 콘텐츠: 골격 ON)
        InitializeDefaultToggleStates();

        // ★ 가이드 패널 처음부터 표시
        ShowGuidePanel();

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

        // ★ 가이드 패널 자동 검색 (정확한 이름으로)
        if (scenarioProgressUI == null)
        {
            var allObjects = FindObjectsByType<GameObject>(FindObjectsSortMode.None);

            // 1차: 정확한 이름
            foreach (var obj in allObjects)
            {
                if (obj.name == "가이드패널Root" || obj.name == "가이드패널")
                {
                    scenarioProgressUI = obj;
                    if (showDebugLogs)
                        Debug.Log($"[Practice] 가이드 패널 찾음: {obj.name}");
                    break;
                }
            }

            // 2차: 패턴 매칭
            if (scenarioProgressUI == null)
            {
                foreach (var obj in allObjects)
                {
                    string name = obj.name.ToLower();
                    if (name.Contains("guide") && name.Contains("panel"))
                    {
                        scenarioProgressUI = obj;
                        if (showDebugLogs)
                            Debug.Log($"[Practice] 가이드 패널 찾음 (패턴): {obj.name}");
                        break;
                    }
                }
            }
        }
    }

    private void AutoConnectToggles()
    {
        var allToggles = FindObjectsByType<Toggle>(FindObjectsSortMode.None);

        // 난이도 토글 초기화
        if (difficultyToggles == null)
            difficultyToggles = new List<Toggle>();

        Toggle beginnerToggle = null;
        Toggle intermediateToggle = null;
        Toggle advancedToggle = null;

        foreach (var toggle in allToggles)
        {
            string name = toggle.name.ToLower();

            // 난이도 토글 (개별 저장)
            if (name.Contains("beginner") || name.Contains("초급"))
                beginnerToggle = toggle;
            else if (name.Contains("intermediate") || name.Contains("중급"))
                intermediateToggle = toggle;
            else if (name.Contains("advanced") || name.Contains("상급"))
                advancedToggle = toggle;
            // 모드 토글
            else if (name.Contains("practice") && (name.Contains("toggle") || name.Contains("mode")))
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
            else if (name.Contains("skeleton") || name.Contains("골격"))
            {
                if (skeletonToggle == null)
                    skeletonToggle = toggle;
            }
            else if (name.Contains("expert") || name.Contains("video") || name.Contains("영상"))
            {
                if (expertVideoToggle == null)
                    expertVideoToggle = toggle;
            }
            else if (name.Contains("result") || name.Contains("결과"))
            {
                if (resultToggle == null)
                    resultToggle = toggle;
            }
            // 메뉴 토글
            else if (name.Contains("setting") || name.Contains("설정"))
            {
                // 설정 패널 내의 토글과 구분
                if (toggle.transform.parent != null &&
                    !toggle.transform.parent.name.ToLower().Contains("setting"))
                {
                    if (settingsToggle == null)
                        settingsToggle = toggle;
                }
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
            // 설정 패널 내 토글
            else if (name.Contains("custom") || name.Contains("자동") || name.Contains("맞춤"))
            {
                if (autoAdjustToggle == null)
                    autoAdjustToggle = toggle;
            }
            else if (name.Contains("patient") && name.Contains("position"))
            {
                if (patientPositionToggle == null)
                    patientPositionToggle = toggle;
            }
        }

        // ★ 난이도 토글 순서: 중급 → 상급 → 초급 (초급이 기본 ON이므로 마지막)
        difficultyToggles.Clear();
        if (intermediateToggle != null) difficultyToggles.Add(intermediateToggle);
        if (advancedToggle != null) difficultyToggles.Add(advancedToggle);
        if (beginnerToggle != null) difficultyToggles.Add(beginnerToggle);

        if (showDebugLogs)
        {
            Debug.Log($"[Practice] 난이도 토글: {difficultyToggles.Count}개 (중급→상급→초급 순서)");
            Debug.Log($"[Practice] 콘텐츠 토글: skeleton={skeletonToggle != null}, video={expertVideoToggle != null}, result={resultToggle != null}");
            Debug.Log($"[Practice] 설정 토글: settings={settingsToggle != null}, autoAdjust={autoAdjustToggle != null}, patientPos={patientPositionToggle != null}");
        }
    }

    /// <summary>
    /// 가이드 패널 표시
    /// </summary>
    private void ShowGuidePanel()
    {
        if (scenarioProgressUI != null)
        {
            scenarioProgressUI.SetActive(true);
            if (showDebugLogs)
                Debug.Log($"[Practice] 가이드 패널 표시됨: {scenarioProgressUI.name}");
        }
        else
        {
            Debug.LogWarning("[Practice] ⚠ 가이드 패널을 찾을 수 없습니다! Inspector에서 scenarioProgressUI를 수동 할당해주세요.");
        }
    }

    #region 토글 상태 관리

    /// <summary>
    /// 초기 상태 설정: 난이도=초급 ON, 콘텐츠=골격 ON
    /// ToggleGroup을 우회하지 않고 정상적으로 설정
    /// </summary>
    private void InitializeDefaultToggleStates()
    {
        // 난이도: 초급만 ON (ToggleGroup이 나머지 처리)
        foreach (var toggle in difficultyToggles)
        {
            if (toggle != null)
            {
                bool isBeginner = toggle.name.ToLower().Contains("beginner") ||
                                  toggle.name.Contains("초급");
                toggle.isOn = isBeginner;
            }
        }

        // 모드: 모두 OFF
        if (practiceToggle != null) practiceToggle.isOn = false;
        if (evaluationToggle != null) evaluationToggle.isOn = false;

        // 콘텐츠: 골격만 ON
        if (skeletonToggle != null) skeletonToggle.isOn = true;
        if (expertVideoToggle != null) expertVideoToggle.isOn = false;
        if (resultToggle != null) resultToggle.isOn = false;

        // 설정/메뉴: OFF
        if (settingsToggle != null) settingsToggle.isOn = false;
        if (mainMenuToggle != null) mainMenuToggle.isOn = false;
        if (startToggle != null) startToggle.isOn = false;

        if (showDebugLogs)
            Debug.Log("[Practice] 초기 상태: 난이도=초급 ON, 콘텐츠=골격 ON");
    }

    /// <summary>
    /// 모든 토글 비활성화 (메인메뉴 제외)
    /// </summary>
    private void DisableAllTogglesExceptMainMenu()
    {
        foreach (var toggle in difficultyToggles)
            if (toggle != null) toggle.interactable = false;

        if (practiceToggle != null) practiceToggle.interactable = false;
        if (evaluationToggle != null) evaluationToggle.interactable = false;
        if (skeletonToggle != null) skeletonToggle.interactable = false;
        if (expertVideoToggle != null) expertVideoToggle.interactable = false;
        if (resultToggle != null) resultToggle.interactable = false;
        if (settingsToggle != null) settingsToggle.interactable = false;
        if (startToggle != null) startToggle.interactable = false;
        if (autoAdjustToggle != null) autoAdjustToggle.interactable = false;
        if (patientPositionToggle != null) patientPositionToggle.interactable = false;

        // 메인메뉴는 항상 활성화 (언제든 나갈 수 있게)
        if (mainMenuToggle != null) mainMenuToggle.interactable = true;
    }

    /// <summary>
    /// 특정 토글들만 활성화
    /// </summary>
    private void EnableOnlyTheseToggles(params Toggle[] togglesToEnable)
    {
        DisableAllTogglesExceptMainMenu();
        foreach (var toggle in togglesToEnable)
            if (toggle != null) toggle.interactable = true;
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
        // ScenarioManager 비활성화 (시나리오 진행 방지)
        var scenarioManager = FindFirstObjectByType<ScenarioManager>();
        if (scenarioManager != null)
        {
            scenarioManager.enabled = false;
            if (showDebugLogs) Debug.Log("[Practice] ScenarioManager disabled");
        }

        var conditionManager = FindFirstObjectByType<ScenarioConditionManager>();
        if (conditionManager != null)
        {
            conditionManager.enabled = false;
            if (showDebugLogs) Debug.Log("[Practice] ScenarioConditionManager disabled");
        }

        var trainingController = FindFirstObjectByType<HandPoseTrainingController>();
        if (trainingController != null)
        {
            trainingController.enabled = false;
            if (showDebugLogs) Debug.Log("[Practice] HandPoseTrainingController disabled");
        }

        var resultTracker = FindFirstObjectByType<TrainingResultTracker>();
        if (resultTracker != null)
        {
            resultTracker.enabled = false;
            if (showDebugLogs) Debug.Log("[Practice] TrainingResultTracker disabled");
        }

        var quizPanel = FindFirstObjectByType<QuizPanel>();
        if (quizPanel != null)
        {
            quizPanel.gameObject.SetActive(false);
            if (showDebugLogs) Debug.Log("[Practice] QuizPanel disabled");
        }

        var lateralFlexionDetector = FindFirstObjectByType<LateralFlexionDetector>();
        if (lateralFlexionDetector != null)
        {
            lateralFlexionDetector.enabled = false;
            if (showDebugLogs) Debug.Log("[Practice] LateralFlexionDetector disabled");
        }

        // ChunaPathEvaluator는 Step 6에서만 활성화
        if (chunaPathEvaluator != null)
        {
            chunaPathEvaluator.enabled = false;
            if (showDebugLogs) Debug.Log("[Practice] ChunaPathEvaluator disabled (Step 6에서 활성화)");
        }
    }

    #region Step Management

    private void StartStep(int step)
    {
        currentStep = step;
        currentCount = 0;
        isStepActive = true;

        if (showDebugLogs)
            Debug.Log($"[Practice] ═══ Step {step + 1} 시작 ═══");

        switch (step)
        {
            case 0: StartStep1_UIGrab(); break;
            case 1: StartStep2_DifficultyAndMode(); break;
            case 2: StartStep3_ContentToggles(); break;
            case 3: StartStep4_SettingsPanel(); break;
            case 4: StartStep5_PositionAndStart(); break;
            case 5: StartStep6_HoldPractice(); break;
            case 6: StartStep7_Exit(); break;
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
        DisableAllTogglesExceptMainMenu();
        ResetUIPosition();

        if (showDebugLogs)
            Debug.Log($"[Practice] Step 1: UI를 잡아서 옮겨보세요 (0/{UI_GRAB_REQUIRED})");
    }

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

    #region Step 2: 난이도 선택 (중급→상급→초급) → 실습모드

    private void StartStep2_DifficultyAndMode()
    {
        sequentialToggles.Clear();

        // ★ 난이도: 중급 → 상급 → 초급 순서 (초급은 이미 ON이므로 마지막)
        foreach (var toggle in difficultyToggles)
        {
            if (toggle != null)
                sequentialToggles.Add(toggle);
        }

        // 실습모드 토글
        if (practiceToggle != null)
            sequentialToggles.Add(practiceToggle);

        if (sequentialToggles.Count == 0)
        {
            if (showDebugLogs) Debug.Log("[Practice] Step 2: 토글 없음, 건너뜀");
            CompleteCurrentStep();
            return;
        }

        currentToggleIndex = 0;
        SaveOriginalColors(sequentialToggles);
        SetupSequentialToggleListeners();
        EnableCurrentToggleOnly();
        StartHighlightToggle(0);

        if (showDebugLogs)
            Debug.Log($"[Practice] Step 2: 난이도(중급→상급→초급) → 실습모드 (총 {sequentialToggles.Count}개)");
    }

    #endregion

    #region Step 3: 콘텐츠 토글 (비디오→결과→골격)

    private void StartStep3_ContentToggles()
    {
        sequentialToggles.Clear();

        // ★ 순서: 비디오 → 결과 → 골격 (골격은 이미 ON이므로 마지막)
        if (expertVideoToggle != null)
            sequentialToggles.Add(expertVideoToggle);
        if (resultToggle != null)
            sequentialToggles.Add(resultToggle);
        if (skeletonToggle != null)
            sequentialToggles.Add(skeletonToggle);

        if (sequentialToggles.Count == 0)
        {
            if (showDebugLogs) Debug.Log("[Practice] Step 3: 콘텐츠 토글 없음, 건너뜀");
            CompleteCurrentStep();
            return;
        }

        currentToggleIndex = 0;
        SaveOriginalColors(sequentialToggles);
        SetupSequentialToggleListeners();
        EnableCurrentToggleOnly();
        StartHighlightToggle(0);

        if (showDebugLogs)
            Debug.Log($"[Practice] Step 3: 콘텐츠(비디오→결과→골격) (총 {sequentialToggles.Count}개)");
    }

    #endregion

    #region Step 4: 설정 패널 (설정→자동조절→환자위치조절)

    private void StartStep4_SettingsPanel()
    {
        sequentialToggles.Clear();

        // 설정 토글 → 자동조절 → 환자위치조절
        if (settingsToggle != null)
            sequentialToggles.Add(settingsToggle);
        if (autoAdjustToggle != null)
            sequentialToggles.Add(autoAdjustToggle);
        if (patientPositionToggle != null)
            sequentialToggles.Add(patientPositionToggle);

        if (sequentialToggles.Count == 0)
        {
            if (showDebugLogs) Debug.Log("[Practice] Step 4: 설정 토글 없음, 건너뜀");
            CompleteCurrentStep();
            return;
        }

        currentToggleIndex = 0;
        SaveOriginalColors(sequentialToggles);
        SetupSequentialToggleListeners();
        EnableCurrentToggleOnly();
        StartHighlightToggle(0);

        if (showDebugLogs)
            Debug.Log($"[Practice] Step 4: 설정→자동조절→환자위치조절 (총 {sequentialToggles.Count}개)");
    }

    #endregion

    #region Step 5: 환자 위치 조절 → 설정 닫기 → 시작

    private void StartStep5_PositionAndStart()
    {
        if (showDebugLogs)
            Debug.Log("[Practice] Step 5: 환자 위치를 조정하세요...");

        // 환자 위치 변경 대기
        StartCoroutine(WaitForPatientPositionThenContinue());
    }

    private IEnumerator WaitForPatientPositionThenContinue()
    {
        // 환자 위치 변경 대기 (또는 타임아웃)
        if (patientTransform != null)
        {
            Vector3 startPos = patientTransform.position;
            float timeout = 30f;
            float elapsed = 0f;

            while (elapsed < timeout)
            {
                if (Vector3.Distance(patientTransform.position, startPos) > 0.01f)
                {
                    if (showDebugLogs)
                        Debug.Log("[Practice] 환자 위치 변경 감지!");
                    break;
                }
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        yield return new WaitForSeconds(0.5f);

        // 2. 설정 토글 점멸 (닫기)
        if (showDebugLogs)
            Debug.Log("[Practice] Step 5-2: 설정 패널을 닫아주세요");

        sequentialToggles.Clear();
        if (settingsToggle != null)
            sequentialToggles.Add(settingsToggle);

        if (sequentialToggles.Count > 0)
        {
            currentToggleIndex = 0;
            SaveOriginalColors(sequentialToggles);
            SetupSequentialToggleListeners();
            EnableCurrentToggleOnly();
            StartHighlightToggle(0);

            // 설정 토글 클릭 대기
            yield return new WaitUntil(() => currentToggleIndex >= sequentialToggles.Count || !isStepActive);
        }

        if (!isStepActive) yield break;

        yield return new WaitForSeconds(0.5f);

        // 3. 시작 토글 점멸
        if (showDebugLogs)
            Debug.Log("[Practice] Step 5-3: 시작 토글을 눌러주세요");

        sequentialToggles.Clear();
        if (startToggle != null)
            sequentialToggles.Add(startToggle);

        if (sequentialToggles.Count > 0)
        {
            currentToggleIndex = 0;
            SaveOriginalColors(sequentialToggles);
            SetupSequentialToggleListeners();
            EnableCurrentToggleOnly();
            StartHighlightToggle(0);

            // 시작 토글 클릭 대기
            yield return new WaitUntil(() => currentToggleIndex >= sequentialToggles.Count || !isStepActive);
        }

        if (isStepActive)
            CompleteCurrentStep();
    }

    public void OnPatientPositionChanged()
    {
        if (currentStep != 4 || !isStepActive) return;
        if (showDebugLogs)
            Debug.Log("[Practice] 환자 위치 변경됨!");
    }

    #endregion

    #region Step 6: 홀드 연습 (3사이클) - 시나리오 없이 직접 실행

    private void StartStep6_HoldPractice()
    {
        currentCount = 0;
        isWaitingForHold = true;

        DisableAllTogglesExceptMainMenu();

        // ★ ChunaPathEvaluator만 활성화 (시나리오 진행 없이)
        if (chunaPathEvaluator != null)
        {
            chunaPathEvaluator.enabled = true;
            chunaPathEvaluator.OnPhaseChanged += OnEvaluationPhaseChanged;
            if (showDebugLogs)
                Debug.Log("[Practice] ChunaPathEvaluator 활성화 - 핸드 가이드 시작");
        }

        if (showDebugLogs)
        {
            Debug.Log($"[Practice] Step 6: 측굴 동작 연습 (0/{HOLD_REQUIRED_COUNT} 사이클)");
            Debug.Log("[Practice] 시작홀드 → 이동 → 중간홀드 → 완료");
        }
    }

    private void OnEvaluationPhaseChanged(ChunaPathEvaluator.EvaluationPhase newPhase)
    {
        if (currentStep != 5 || !isStepActive || !isWaitingForHold) return;

        if (showDebugLogs)
            Debug.Log($"[Practice] 평가 단계: {newPhase}");

        if (newPhase == ChunaPathEvaluator.EvaluationPhase.Completed)
        {
            currentCount++;
            if (showDebugLogs)
                Debug.Log($"[Practice] ★ 사이클 완료! ({currentCount}/{HOLD_REQUIRED_COUNT})");

            if (currentCount >= HOLD_REQUIRED_COUNT)
            {
                if (chunaPathEvaluator != null)
                    chunaPathEvaluator.OnPhaseChanged -= OnEvaluationPhaseChanged;

                isWaitingForHold = false;
                CompleteCurrentStep();
            }
        }
    }

    public void OnCycleCompleted()
    {
        if (currentStep != 5 || !isStepActive) return;

        currentCount++;
        if (showDebugLogs)
            Debug.Log($"[Practice] 사이클 완료! ({currentCount}/{HOLD_REQUIRED_COUNT})");

        if (currentCount >= HOLD_REQUIRED_COUNT)
        {
            if (chunaPathEvaluator != null)
                chunaPathEvaluator.OnPhaseChanged -= OnEvaluationPhaseChanged;
            isWaitingForHold = false;
            CompleteCurrentStep();
        }
    }

    #endregion

    #region Step 7: 나가기 (메인메뉴 점멸)

    private void StartStep7_Exit()
    {
        sequentialToggles.Clear();

        if (mainMenuToggle != null)
            sequentialToggles.Add(mainMenuToggle);

        if (sequentialToggles.Count == 0)
        {
            ShowCompletionPopup();
            return;
        }

        currentToggleIndex = 0;
        SaveOriginalColors(sequentialToggles);
        SetupSequentialToggleListeners();
        EnableCurrentToggleOnly();
        StartHighlightToggle(0);

        if (showDebugLogs)
            Debug.Log("[Practice] Step 7: 나가기(메인메뉴) 토글을 눌러주세요");
    }

    #endregion

    #region 순차 토글 하이라이트 시스템

    private void SaveOriginalColors(List<Toggle> toggles)
    {
        originalToggleColors.Clear();
        clickedToggles.Clear();

        foreach (var toggle in toggles)
        {
            if (toggle != null)
            {
                var image = GetToggleImage(toggle);
                if (image != null)
                    originalToggleColors[toggle] = image.color;
            }
        }
    }

    private Image GetToggleImage(Toggle toggle)
    {
        if (toggle == null) return null;

        if (toggle.targetGraphic != null && toggle.targetGraphic is Image targetImage)
            return targetImage;

        return toggle.GetComponentInChildren<Image>();
    }

    private void SetupSequentialToggleListeners()
    {
        for (int i = 0; i < sequentialToggles.Count; i++)
        {
            int index = i;
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
            clickedToggles.Add(clickedToggle);

            if (showDebugLogs)
                Debug.Log($"[Practice] ✓ 토글 클릭: {clickedToggle.name} ({index + 1}/{sequentialToggles.Count})");

            currentToggleIndex++;

            if (currentToggleIndex >= sequentialToggles.Count)
            {
                StopBlinking();

                // Step 5 (홀드 연습)에서는 별도 처리
                if (currentStep == 5)
                {
                    // StartWaitingForHold는 Step 6에서
                }
                else if (currentStep != 4) // Step 5 (위치+시작)는 코루틴에서 처리
                {
                    CompleteCurrentStep();
                }
            }
            else
            {
                StartHighlightToggle(currentToggleIndex);
            }
        }
    }

    private void StartHighlightToggle(int index)
    {
        if (index >= sequentialToggles.Count) return;

        currentToggleIndex = index;
        StopBlinking();
        EnableCurrentToggleOnly();

        var currentToggle = sequentialToggles[index];
        if (currentToggle != null)
        {
            blinkCoroutine = StartCoroutine(BlinkToggle(currentToggle));

            if (showDebugLogs)
                Debug.Log($"[Practice] 점멸 시작: {currentToggle.name} ({index + 1}/{sequentialToggles.Count})");
        }
    }

    private IEnumerator BlinkToggle(Toggle toggle)
    {
        var image = GetToggleImage(toggle);
        if (image == null) yield break;

        Color originalColor = originalToggleColors.ContainsKey(toggle) ? originalToggleColors[toggle] : image.color;
        bool isHighlight = false;

        while (true)
        {
            isHighlight = !isHighlight;
            image.color = isHighlight ? highlightColor : originalColor;
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

        foreach (var kvp in originalToggleColors)
        {
            if (kvp.Key != null && !clickedToggles.Contains(kvp.Key))
            {
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

        if (exitPopupController != null)
        {
            exitPopupController.ShowPopup();
        }
        else
        {
            var popup = FindFirstObjectByType<ExitPopupController>();
            if (popup != null)
                popup.ShowPopup();
            else
                Debug.LogWarning("[Practice] 완료 팝업을 찾을 수 없습니다!");
        }
    }

    #endregion

    #region Public Methods

    public void RestartPractice()
    {
        StopAllCoroutines();
        StopBlinking();
        currentStep = 0;
        currentCount = 0;
        isStepActive = false;
        isWaitingForHold = false;
        sequentialToggles.Clear();
        clickedToggles.Clear();

        StartStep(0);

        if (showDebugLogs)
            Debug.Log("[Practice] 연습 재시작");
    }

    public int GetCurrentStep() => currentStep;
    public int GetCurrentCount() => currentCount;
    public bool IsActive() => isStepActive;

    #endregion

    void OnDestroy()
    {
        StopBlinking();

        if (chunaPathEvaluator != null)
            chunaPathEvaluator.OnPhaseChanged -= OnEvaluationPhaseChanged;
    }
}
