using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using TLab.WebView;

/// <summary>
/// CSV 기반 시나리오 매니저
/// 모드 선택 정보 저장 기능 포함
/// </summary>
public class ScenarioManager : MonoBehaviour
{
    [Header("=== CSV 로드 설정 ===")]
    [Tooltip("CSV 파일 이름 (Resources/Scenarios/ 폴더)")]
    [SerializeField] private string csvFileName = "ScenarioData";

    [Header("=== 평가 시스템 ===")]
    [Tooltip("ChunaPathEvaluator (자동 찾기)")]
    [SerializeField] private ChunaPathEvaluator chunaPathEvaluator;

    [Tooltip("ChunaPathEvaluatorBridge (자동 찾기/생성)")]
    [SerializeField] private ChunaPathEvaluatorBridge chunaPathEvaluatorBridge;

    [Tooltip("ScenarioConditionManager (자동 찾기)")]
    [SerializeField] private ScenarioConditionManager conditionManager;

    [Tooltip("CranialAdjustmentController (두개골 교정 술기, 없으면 자동 찾기)")]
    [SerializeField] private CranialAdjustmentController cranialController;

    [Tooltip("힘의 방향 화살표 관리자 (없으면 자동 찾기, 씬에 없으면 기능 자체가 꺼진다)")]
    [SerializeField] private ForceArrowDirector forceArrowDirector;
    private bool forceArrowLookupDone;

    [Tooltip("두경부(cranial) 시나리오일 때 비활성화할 기존 머리 판정 콜라이더들 " +
             "(비두경부 손접촉 감지용, 머리 본체에 부착됨). 두경부 시술 중 cross-talk 방지. " +
             "비두경부 시나리오에선 자동 재활성. PatientHeadTouchDetector/PokeDetector의 콜라이더를 연결.")]
    [SerializeField] private Collider[] nonCranialHeadColliders;

    [Header("=== UI 자동 배치 ===")]
    [Tooltip("ScenarioUIPositioner (자동 찾기)")]
    [SerializeField] private ScenarioUIPositioner uiPositioner;

    [Header("=== 환자 모델 ===")]
    [Tooltip("환자 모델의 Animator (시나리오별로 Controller 자동 전환)")]
    [SerializeField] private Animator patientAnimator;

    [Header("=== 각도 표시 UI ===")]
    [Tooltip("각도 표시 컨트롤러 (프리셋으로 전환)")]
    [SerializeField] private AngleDisplayController angleDisplay;

    [Header("=== 퀴즈 패널 ===")]
    [Tooltip("퀴즈 패널 (학습 완료 후 표시)")]
    [SerializeField] private QuizPanel quizPanel;

    [Header("=== 실습 완료 UI ===")]
    [Tooltip("실습 결과 패널 (완료 시 표시, 없으면 건너뜀)")]
    [SerializeField] private GameObject resultPanel;
    [Tooltip("결과 패널의 WebView Browser (퀴즈+결과 페이지 로드용)")]
    [SerializeField] private Browser resultBrowser;
    [Tooltip("결과/퀴즈 웹페이지 URL")]
    [SerializeField] private string resultWebUrl = "https://claude.ai/public/artifacts/5c91cdd3-2017-49bd-b743-7595a5810d72";
    [Tooltip("페이지 로드 후 CSS zoom 값 (1=원본, 0.7=70%로 축소). 0 이하면 미적용")]
    [Range(0f, 2f)]
    [SerializeField] private float resultWebZoom = 0.7f;
    [Tooltip("zoom 주입 전 페이지 로드 대기 시간(초)")]
    [SerializeField] private float resultWebZoomDelay = 2f;
    [Tooltip("페이지 외곽 스크롤(빈 공간으로 끝없이 스크롤되는 현상) 차단")]
    [SerializeField] private bool resultWebLockBodyScroll = true;
    [Tooltip("종료 확인 팝업 컨트롤러")]
    [SerializeField] private ExitPopupController exitPopupController;

    [Header("=== 결과 추적 ===")]
    [Tooltip("훈련 결과 추적기 (자동 찾기)")]
    [SerializeField] private TrainingResultTracker resultTracker;

    [Header("=== 피벗 포인트 ===")]
    [Tooltip("목 피벗 Transform (경추 중심)")]
    [SerializeField] private Transform neckPivot;
    [Tooltip("왼쪽 어깨 피벗 Transform")]
    [SerializeField] private Transform leftShoulderPivot;
    [Tooltip("오른쪽 어깨 피벗 Transform")]
    [SerializeField] private Transform rightShoulderPivot;
    [Tooltip("왼팔 상완 피벗 Transform (대흉근 등)")]
    [SerializeField] private Transform leftUpperArmPivot;
    [Tooltip("오른팔 상완 피벗 Transform (대흉근 등)")]
    [SerializeField] private Transform rightUpperArmPivot;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLog = true;

    // 현재 진행 상태
    private ScenarioData currentScenario;
    private PhaseData currentPhase;
    private StepData currentStep;
    private SubStepData currentSubStep;

    // NextSubStep 이중 호출 방지용 — 마지막으로 진행시킨 substep (NextSubStep 주석 참조)
    private SubStepData advancedFromSubStep;

    // 인덱스
    private int currentPhaseIndex = 0;
    private int currentStepIndex = 0;
    private int currentSubStepIndex = 0;

    // 이벤트 시스템
    private ScenarioEventSystem eventSystem;

    // 선택된 모드 정보
    private string selectedMode = "";
    private string selectedDifficulty = "";

    // 시나리오 완료 상태
    private bool isScenarioCompleted = false;

    // 현재 Config 참조
    private ScenarioConfig currentConfig;


    // 프로퍼티
    public ScenarioData CurrentScenario => currentScenario;
    public PhaseData CurrentPhase => currentPhase;
    public StepData CurrentStep => currentStep;
    public SubStepData CurrentSubStep => currentSubStep;
    public bool IsLastSubStep => currentSubStepIndex >= currentStep.subSteps.Count - 1;
    public bool IsLastStep => currentStepIndex >= currentPhase.steps.Count - 1;
    public bool IsLastPhase => currentPhaseIndex >= currentScenario.phases.Count - 1;
    public bool IsScenarioCompleted => isScenarioCompleted;

    // 모드 정보 프로퍼티
    public string SelectedMode => selectedMode;
    public string SelectedDifficulty => selectedDifficulty;

    // 결과 추적 프로퍼티
    public TrainingResultTracker ResultTracker => resultTracker;
    public TrainingResultData CurrentResultData => resultTracker?.GetResultData();

    private void Awake()
    {
        eventSystem = ScenarioEventSystem.Instance;

        // ✅ ConditionManager 찾기
        if (conditionManager == null)
        {
            conditionManager = FindFirstObjectByType<ScenarioConditionManager>();
        }

        if (uiPositioner == null)
            uiPositioner = FindFirstObjectByType<ScenarioUIPositioner>();

        if (quizPanel == null)
            quizPanel = FindFirstObjectByType<QuizPanel>();

        if (exitPopupController == null)
            exitPopupController = FindFirstObjectByType<ExitPopupController>();

        if (resultTracker == null)
            resultTracker = FindFirstObjectByType<TrainingResultTracker>();

        // 평가 도중 "메인으로/다시하기"로 나가면 미완료(중도 종료)로 기록
        if (exitPopupController != null)
        {
            exitPopupController.OnMainMenuSelected.AddListener(SaveIncompleteResultIfTracking);
            exitPopupController.OnRetrySelected.AddListener(SaveIncompleteResultIfTracking);
        }

        // HandPose 시스템 초기화
        InitializeHandPoseSystem();
    }

    /// <summary>
    /// ChunaPathEvaluator 시스템 초기화
    /// </summary>
    private void InitializeHandPoseSystem()
    {
        if (chunaPathEvaluator == null)
            chunaPathEvaluator = FindFirstObjectByType<ChunaPathEvaluator>();

        if (chunaPathEvaluator == null)
        {
            ChunaLogger.LogWarning("[ScenarioManager] ChunaPathEvaluator를 찾을 수 없습니다.");
            return;
        }

        chunaPathEvaluatorBridge = chunaPathEvaluator.GetComponent<ChunaPathEvaluatorBridge>();
        if (chunaPathEvaluatorBridge == null)
            chunaPathEvaluatorBridge = chunaPathEvaluator.gameObject.AddComponent<ChunaPathEvaluatorBridge>();

        // 시나리오 컨피그 임계점 오버라이드 (Bootstrapper보다 늦게 초기화되는 경우 대비)
        if (currentConfig != null)
            chunaPathEvaluator.ApplyEvaluationThresholds(currentConfig);
    }

    private void OnEnable()
    {
        // ✅ SubStepStarted 이벤트 구독 (HandPose 자동 처리용)
        if (eventSystem != null)
        {
            eventSystem.OnSubStepStarted += OnSubStepStartedForHandPose;
        }
    }

    private void OnDisable()
    {
        // ✅ 이벤트 구독 해제
        if (eventSystem != null)
        {
            eventSystem.OnSubStepStarted -= OnSubStepStartedForHandPose;
        }

        // ★ AutoPlay 이벤트 구독 해제 (메모리 누수 방지)
        if (chunaPathEvaluator != null)
        {
            chunaPathEvaluator.OnAutoPlayCompleted -= OnAutoPlayCompletedHandler;
        }
    }

    /// <summary>
    /// ScenarioBootstrapper에서 호출 — ScenarioConfig 데이터를 주입
    /// scenarioName이 CSV 파일명과 Animator Controller 경로에 공용으로 사용됨
    /// </summary>
    public void SetScenarioConfig(ScenarioConfig config)
    {
        if (config == null) return;

        csvFileName = config.scenarioName;
        currentConfig = config;

        // 평가 임계점 오버라이드 적용
        if (chunaPathEvaluator != null)
        {
            chunaPathEvaluator.ApplyEvaluationThresholds(config);
        }

        ChunaLogger.Log($"[ScenarioManager] ScenarioConfig 적용: scenarioName={config.scenarioName}");
    }

    /// <summary>
    /// 모드와 난이도 정보 설정
    /// </summary>
    public void SetModeInfo(string mode, string difficulty)
    {
        selectedMode = mode;
        selectedDifficulty = difficulty;

        ChunaLogger.Log($"[ScenarioManager] 모드 설정: {mode}, 난이도: {difficulty}");
    }

    /// <summary>
    /// 시나리오 시작 (CSV 기반)
    /// </summary>
    public void StartScenario()
    {
        if (showDebugLog)
            ChunaLogger.Log($"[ScenarioManager] StartScenario() csvFileName: {csvFileName}");

        LoadFromCSV();
    }

    /// <summary>
    /// 특정 시나리오 시작
    /// </summary>
    public void StartScenario(ScenarioData scenario)
    {
        if (scenario == null || scenario.phases.Count == 0)
        {
            LogError("유효하지 않은 시나리오 데이터입니다!");
            return;
        }

        currentScenario = scenario;
        currentPhaseIndex = 0;
        currentStepIndex = 0;
        currentSubStepIndex = 0;
        isScenarioCompleted = false;  // 시나리오 시작 시 완료 상태 초기화
        advancedFromSubStep = null;   // 재시작 시 이중진행 가드 해제

        currentPhase = currentScenario.phases[0];
        currentStep = currentPhase.steps[0];
        currentSubStep = currentStep.subSteps[0];

        // ★ 시나리오별 Animator Controller 전환
        SwitchAnimatorController();

        // ★ 시나리오별 근육 표시 갱신
        string muscleScenarioName = currentConfig != null ? currentConfig.scenarioName : scenario.scenarioName;
        AnatomyMuscleController muscleController = FindFirstObjectByType<AnatomyMuscleController>();
        if (muscleController != null)
            muscleController.ApplyScenario(muscleScenarioName);

        // ★ 두경부 추나(두개골 교정) 리그 토글: 이 시나리오에 cranial substep이 있을 때만 활성화
        //   (같은 TrainingScene 공유 — 비두경부 시나리오에서 머리 트리거 cross-talk 방지)
        ApplyCranialRigForScenario(currentScenario);

        // 시나리오 구조 디버그 출력
        if (showDebugLog)
        {
            ChunaLogger.Log($"[ScenarioManager] 시나리오: {scenario.scenarioName}, Phase: {currentScenario.phases.Count}개");
            for (int pi = 0; pi < currentScenario.phases.Count; pi++)
            {
                var phase = currentScenario.phases[pi];
                ChunaLogger.Log($"  [{pi}] {phase.phaseName} (Steps: {phase.steps.Count})");
            }
        }

        // UI 자동 배치 — 메뉴 단계에서 이미 배치가 끝났으면(HasPositioned) 그대로 두어 시나리오 시작 시 UI가 또
        // 재조정되어 움직이는 것을 방지. 트래킹이 끝내 안 잡혀 미배치 상태로 시작한 예외 케이스만 이때 배치.
        if (uiPositioner != null)
        {
            uiPositioner.EnsurePositionedWhenReady();
        }

        // 결과 추적 시작
        if (resultTracker != null)
        {
            resultTracker.StartTracking(selectedMode, selectedDifficulty, currentConfig != null ? currentConfig.scenarioName : "");
            resultTracker.StartPhase(currentPhase.phaseName);
            resultTracker.StartStep(currentStep.stepName);
        }

        // 이벤트 발생
        eventSystem.ScenarioStarted(currentScenario);
        eventSystem.PhaseChanged(currentPhase);
        eventSystem.StepChanged(currentStep);
        eventSystem.SubStepStarted(currentSubStep);

        UpdateUI();
        UpdateProgress();

        Log($"시나리오 시작: {currentScenario.scenarioName} (모드: {selectedMode}, 난이도: {selectedDifficulty})");
    }

    /// <summary>
    /// CSV에서 로드
    /// </summary>
    private void LoadFromCSV()
    {
        ScenarioCSVLoader loader = GetComponent<ScenarioCSVLoader>();
        if (loader == null)
            loader = gameObject.AddComponent<ScenarioCSVLoader>();

        ScenarioCollection collection = loader.LoadScenarios(csvFileName);

        if (collection == null || collection.scenarios.Count == 0)
        {
            LogError($"CSV 로드 실패: Resources/Scenarios/{csvFileName}.csv");
            return;
        }

        ScenarioData scenario = collection.scenarios[0];
        ApplyEvaluationPhaseFilter(scenario);
        StartScenario(scenario);
    }

    /// <summary>
    /// 평가모드에서 ScenarioConfig.evaluationPhases 화이트리스트에 없는 phase 제거.
    /// 비어있거나 평가모드가 아니면 무동작.
    /// </summary>
    private void ApplyEvaluationPhaseFilter(ScenarioData scenario)
    {
        if (scenario == null || currentConfig == null) return;
        if (!currentConfig.HasEvaluationPhaseFilter) return;

        var dm = ChunaTraining.DifficultyManager.Instance;
        if (dm == null || !dm.IsEvaluationMode) return;

        int before = scenario.phases.Count;
        scenario.phases = scenario.phases
            .Where(p => currentConfig.IsEvaluationPhaseAllowed(p.phaseName))
            .ToList();

        ChunaLogger.Log($"<color=cyan>[ScenarioManager] 평가모드 phase 필터: {before} → {scenario.phases.Count}개 ({string.Join(", ", currentConfig.evaluationPhases)})</color>");

        RemoveRedundantLeadingGuide(scenario);
    }

    /// <summary>
    /// 평가모드 phase 필터로 앞 phase(예: 전부)가 빠지면, 다음 작업 phase(예: 중부)의
    /// 선두 '가이드' step(원래는 전환용 "버튼을 눌러 진행" 안내)이 시작 버튼 바로 뒤에 붙어
    /// 사용자가 버튼을 두 번 눌러야 하는 문제가 생김.
    /// 첫 phase가 '시작 버튼 전용'(작업 step 없는 가이드 전용)일 때에 한해,
    /// 두 번째 phase의 선두 가이드 step만 제거해 시작 직후 곧장 진입하도록 함.
    /// 시작 버튼/종료 화면/실제 phase 간 전환 가이드는 보존.
    /// </summary>
    private void RemoveRedundantLeadingGuide(ScenarioData scenario)
    {
        if (scenario.phases.Count < 2) return;

        var first = scenario.phases[0];
        bool firstIsGuideOnly = first.steps != null && first.steps.Count > 0
                                && first.steps.All(s => s.IsGuideStep());
        if (!firstIsGuideOnly) return;

        var second = scenario.phases[1];
        if (second.steps == null || second.steps.Count == 0) return;

        bool secondHasWork = second.steps.Any(s => !s.IsGuideStep());
        if (secondHasWork && second.steps[0].IsGuideStep())
        {
            var removed = second.steps[0];
            second.steps.RemoveAt(0);
            ChunaLogger.Log($"<color=cyan>[ScenarioManager] 평가모드 시작 직후 중복 가이드 제거: {second.phaseName} - '{removed.stepName}'</color>");
        }
    }

    /// <summary>
    /// 다음 SubStep으로 진행
    /// </summary>
    public void NextSubStep()
    {
        // ★ 이중 진행 방지: 나레이션+환자애니(게이트 없음) substep은 완료를 두 경로가 각각 감지한다 —
        //   ⓐ ConditionManager.PlayNarrationThenApplyDuration (나레이션·AutoPlay 완료 후 진행)
        //   ⓑ ScenarioManager.OnAutoPlayCompletedHandler → WaitForNarrationThenNextStep
        //   둘 다 호출하면 substep이 한 칸 건너뛴다. substep당 1회만 진행시킨다.
        if (currentSubStep != null && ReferenceEquals(advancedFromSubStep, currentSubStep))
        {
            if (showDebugLog)
                ChunaLogger.Log("[ScenarioManager] NextSubStep 중복 호출 무시 (이 substep은 이미 진행됨)");
            return;
        }
        advancedFromSubStep = currentSubStep;

        if (showDebugLog)
            ChunaLogger.Log($"[ScenarioManager] NextSubStep: Phase={currentPhase?.phaseName}, Step={currentStep?.stepName}, SubStep={currentSubStepIndex}/{currentStep?.subSteps?.Count}");

        if (currentSubStep != null)
        {
            eventSystem.SubStepCompleted(currentSubStep);
        }

        // 다음 SubStep이 있으면 진행
        if (currentSubStepIndex < currentStep.subSteps.Count - 1)
        {
            currentSubStepIndex++;
            currentSubStep = currentStep.subSteps[currentSubStepIndex];

            eventSystem.SubStepStarted(currentSubStep);
            UpdateUI();
            UpdateProgress();

            Log($"SubStep {currentSubStep.subStepNo}: {currentSubStep.voiceInstruction}");
            return;
        }

        // SubStep 끝 -> Step 완료
        NextStep();
    }

    /// <summary>
    /// 다음 Step으로 진행
    /// </summary>
    private void NextStep()
    {
        eventSystem.StepCompleted(currentStep);

        // 다음 Step이 있으면 진행
        if (currentStepIndex < currentPhase.steps.Count - 1)
        {
            currentStepIndex++;
            currentSubStepIndex = 0;

            currentStep = currentPhase.steps[currentStepIndex];
            currentSubStep = currentStep.subSteps[0];

            // ✅ 결과 추적: Step 시작
            if (resultTracker != null)
            {
                resultTracker.StartStep(currentStep.stepName);
            }

            eventSystem.StepChanged(currentStep);
            eventSystem.SubStepStarted(currentSubStep);
            UpdateUI();
            UpdateProgress();

            Log($"Step 변경: {currentStep.stepName}");
            return;
        }

        // Step 끝 -> Phase 완료
        NextPhase();
    }

    /// <summary>
    /// 다음 Phase로 진행
    /// </summary>
    private void NextPhase()
    {
        eventSystem.PhaseCompleted(currentPhase);

        // 다음 Phase가 있으면 진행
        if (currentPhaseIndex < currentScenario.phases.Count - 1)
        {
            currentPhaseIndex++;
            currentStepIndex = 0;
            currentSubStepIndex = 0;

            currentPhase = currentScenario.phases[currentPhaseIndex];
            currentStep = currentPhase.steps[0];
            currentSubStep = currentStep.subSteps[0];

            // ✅ 결과 추적: Phase 및 Step 시작
            if (resultTracker != null)
            {
                resultTracker.StartPhase(currentPhase.phaseName);
                resultTracker.StartStep(currentStep.stepName);
            }

            eventSystem.PhaseChanged(currentPhase);
            eventSystem.StepChanged(currentStep);
            eventSystem.SubStepStarted(currentSubStep);
            UpdateUI();
            UpdateProgress();

            Log($"Phase 변경: {currentPhase.phaseName}");
            return;
        }

        // Phase 끝 -> 시나리오 완료
        CompleteScenario();
    }

    /// <summary>
    /// 시나리오 완료
    /// </summary>
    /// <summary>두개골 단계에서 모은 지표를 결과에 기록한다(모인 게 없으면 무동작).
    /// 두개골은 유사도·리밋 채점이 성립하지 않아 이 경로가 유일한 지표 기록이다.</summary>
    private void FlushCranialMetrics()
    {
        if (resultTracker == null || cranialController == null) return;
        if (!cranialController.HasPendingCranialMetrics) return;
        resultTracker.RecordCranialStep(cranialController.ConsumeCranialMetrics());
    }

    private void CompleteScenario()
    {
        // ✅ 결과 추적 종료 및 데이터 저장
        if (resultTracker != null)
        {
            FlushCranialMetrics();   // 마지막 두개골 단계 지표를 놓치지 않게 종료 전에 기록
            var finalResult = resultTracker.FinishTracking();
            if (showDebugLog && finalResult != null)
                ChunaLogger.Log($"[ScenarioManager] 훈련 결과: 시간={TrainingResultData.FormatTime(finalResult.totalTime)}, 유사도={finalResult.overallSimilarity:P0}, 경고={finalResult.totalWarningCount}, 스킵={finalResult.totalSkipCount}");
        }

        // ★ 시나리오 완료 상태 설정
        isScenarioCompleted = true;

        eventSystem.ScenarioCompleted(currentScenario);
        Log($"시나리오 완료: {currentScenario.scenarioName}");

        // ★ 실습 결과 패널 표시 (할당되어 있으면)
        ShowResultPanel();

        // 퀴즈 패널 표시
        ShowQuizPanel();
    }

    /// <summary>
    /// 평가 도중 종료 시 미완료(중도 종료) 결과를 기록.
    /// - 공식 평가일 때만 저장 (연습모드는 경고만 하고 기록 안 함 — 무결성/감사 대상 아님)
    /// - 이미 완료(정상 저장)했거나 추적 중이 아니면 무시 (중복 저장 방지)
    /// - FinishTracking(false) → isCompleted=false로 저장, OnTrainingCompleted 발화(로컬 CSV/서버 동일 경로)
    /// 호출처: ExitPopup '메인으로'/'다시하기', 앱 종료 안전망
    /// </summary>
    public void SaveIncompleteResultIfTracking()
    {
        if (isScenarioCompleted) return;                       // 정상 완주는 CompleteScenario에서 이미 저장
        if (resultTracker == null || !resultTracker.IsTracking) return;
        if (!resultTracker.IsOfficialEvaluation) return;       // 연습모드는 경고만, 기록 안 함

        var partial = resultTracker.FinishTracking(false);
        if (showDebugLog && partial != null)
            ChunaLogger.Log($"[ScenarioManager] ⚠ 평가 중도 종료 — 미완료로 기록: 시간={TrainingResultData.FormatTime(partial.totalTime)}, 유사도={partial.overallSimilarity:P0}");
    }

    /// <summary>
    /// 앱 종료 시 안전망 — 평가 진행 중이면 미완료로 기록 (best-effort, 크래시는 못 잡음)
    /// </summary>
    private void OnApplicationQuit()
    {
        SaveIncompleteResultIfTracking();
    }

    /// <summary>
    /// 실습 결과 패널 표시 (실습 완료 후)
    /// </summary>
    private void ShowResultPanel()
    {
        if (resultPanel != null)
        {
            resultPanel.SetActive(true);
            ChunaLogger.Log("[ScenarioManager] 실습 결과 패널 표시");

            if (resultBrowser != null && !string.IsNullOrEmpty(resultWebUrl))
            {
                resultBrowser.LoadUrl(resultWebUrl);
                ChunaLogger.Log($"[ScenarioManager] 결과 웹뷰 로드: {resultWebUrl}");

                bool needZoom = resultWebZoom > 0f && Mathf.Abs(resultWebZoom - 1f) > 0.001f;
                if (needZoom || resultWebLockBodyScroll)
                {
                    StartCoroutine(InjectWebZoomCoroutine());
                }
            }
        }
        else
        {
            ChunaLogger.Log("[ScenarioManager] 실습 결과 패널이 없어 건너뜁니다.");
        }
    }

    /// <summary>
    /// 결과 웹뷰에 CSS zoom 주입 (페이지를 축소해서 스크롤 없이 끼워 맞춤)
    /// </summary>
    private System.Collections.IEnumerator InjectWebZoomCoroutine()
    {
        yield return new WaitForSeconds(resultWebZoomDelay);
        if (resultBrowser == null) yield break;

        var sb = new System.Text.StringBuilder();

        if (resultWebZoom > 0f && Mathf.Abs(resultWebZoom - 1f) > 0.001f)
        {
            string zoomStr = resultWebZoom.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            sb.Append($"document.documentElement.style.zoom='{zoomStr}';");
            sb.Append($"document.body.style.zoom='{zoomStr}';");
        }

        if (resultWebLockBodyScroll)
        {
            // 강력 버전: 모든 스크롤 가능 요소를 찾아 잠그고, 1초마다 반복 (React 재렌더링 대응)
            sb.Append(@"(function(){
var css='html,body,#__next,#root,main,[class*=""scroll""],[class*=""overflow""]{overflow:hidden!important;overscroll-behavior:none!important;height:100vh!important;max-height:100vh!important;}html,body{position:fixed!important;width:100%!important;top:0!important;left:0!important;margin:0!important;}*{overscroll-behavior:none!important;}';
var s=document.createElement('style');s.id='__lockScroll';s.innerHTML=css;document.head.appendChild(s);
function lockAll(){
  try{
    document.querySelectorAll('*').forEach(function(el){
      var cs=getComputedStyle(el);
      if(/(auto|scroll)/.test(cs.overflow+cs.overflowY+cs.overflowX)){
        el.style.overflow='hidden';
        el.style.overscrollBehavior='none';
      }
    });
    window.scrollTo(0,0);
    document.documentElement.scrollTop=0;
    document.body.scrollTop=0;
  }catch(e){}
}
lockAll();
setInterval(lockAll,1000);
window.addEventListener('scroll',function(){window.scrollTo(0,0);},{passive:false});
})();");
        }

        if (sb.Length > 0)
        {
            resultBrowser.EvaluateJS(sb.ToString());
            ChunaLogger.Log($"[ScenarioManager] 결과 웹뷰 CSS 주입: zoom={resultWebZoom}, lockScroll={resultWebLockBodyScroll}");
        }
    }

    /// <summary>
    /// 종료 팝업 표시 (다음 버튼 클릭 시 - 실습 완료 후)
    /// </summary>
    public void ShowExitPopup()
    {
        if (exitPopupController != null)
        {
            exitPopupController.ShowPopup();
            ChunaLogger.Log("[ScenarioManager] 종료 확인 팝업 표시");
        }
        else
        {
            ChunaLogger.LogWarning("[ScenarioManager] ExitPopupController가 없습니다.");
        }
    }

    /// <summary>
    /// 퀴즈 패널 표시 (학습 완료 후)
    /// </summary>
    private void ShowQuizPanel()
    {
        if (quizPanel != null)
        {
            ChunaLogger.Log("[ScenarioManager] 퀴즈 패널 표시");
            quizPanel.ShowQuizPanel();
        }
        else
        {
            ChunaLogger.LogWarning("[ScenarioManager] QuizPanel이 없어 퀴즈를 건너뜁니다.");
        }
    }

    /// <summary>
    /// UI 업데이트 요청
    /// </summary>
    private void UpdateUI()
    {
        string buttonText = IsLastSubStep && IsLastStep && IsLastPhase ? "완료" : "다음";

        eventSystem.RequestUIUpdate(
            currentScenario.scenarioName,
            currentSubStep.voiceInstruction,
            buttonText
        );
    }

    /// <summary>
    /// 진행도 업데이트 요청
    /// </summary>
    private void UpdateProgress()
    {
        int totalSteps = 0;
        int completedSteps = 0;

        foreach (var phase in currentScenario.phases)
        {
            totalSteps += phase.steps.Count;
        }

        for (int i = 0; i < currentPhaseIndex; i++)
        {
            completedSteps += currentScenario.phases[i].steps.Count;
        }

        completedSteps += currentStepIndex;

        eventSystem.RequestProgressUpdate(completedSteps, totalSteps);
    }

    /// <summary>
    /// 특정 Phase로 이동
    /// </summary>
    public void JumpToPhase(string phaseName)
    {
        int phaseIndex = currentScenario.phases.FindIndex(p => p.phaseName == phaseName);

        if (phaseIndex == -1)
        {
            LogError($"Phase를 찾을 수 없습니다: {phaseName}");
            return;
        }

        currentPhaseIndex = phaseIndex;
        currentStepIndex = 0;
        currentSubStepIndex = 0;

        currentPhase = currentScenario.phases[currentPhaseIndex];
        currentStep = currentPhase.steps[0];
        currentSubStep = currentStep.subSteps[0];

        eventSystem.PhaseChanged(currentPhase);
        eventSystem.StepChanged(currentStep);
        eventSystem.SubStepStarted(currentSubStep);
        UpdateUI();
        UpdateProgress();
    }

    // ========== HandPose 자동 처리 ==========

    /// <summary>
    /// SubStep 시작 시 HandPose 자동 처리
    /// ScenarioActionHandler의 기능을 통합
    /// </summary>
    private void OnSubStepStartedForHandPose(SubStepData subStep)
    {
        if (showDebugLog)
            ChunaLogger.Log($"[ScenarioManager] SubStep #{subStep?.subStepNo}: hand='{subStep?.handTrackingFileName ?? ""}', anim='{subStep?.patientAnimationClip ?? ""}'");

        // 결과 추적: SubStep 시작 기록
        if (resultTracker != null && currentPhase != null && currentStep != null)
        {
            // ★두개골 지표는 StartSubStep 직전에 넣어야 '방금 끝난 단계'에 붙는다
            //   (StartSubStep이 이전 SubStep의 완료 처리를 하면서 phase/step 이름을 갱신하기 때문).
            FlushCranialMetrics();

            resultTracker.StartSubStep(currentPhase.phaseName, currentStep.stepName);
        }

        // ★ Phase별 임계점 오버라이드 적용 (사각근 전부/중부/후부 등)
        ApplyPhaseThresholdOverride();

        // ★ 접촉 감지 부위 설정 (시나리오 CSV의 contactTarget 컬럼)
        ApplyContactTarget(subStep);

        // ★ 애니를 지정하지 않은 단계(파지 등)는 직전 자세를 그대로 붙잡는다.
        //   이 처리가 없으면 남아 있는 직전 클립 이름을 다른 코드가 Play(...,0f)로 다시 틀어
        //   "단계 들어가자마자 직전 동작만 시작 자세로 풀리는" 현상이 생긴다
        //   (무릎은 올라간 채 팔만 내려가고, 기대기만 풀려 깍지 낀 자세로 돌아감).
        if (chunaPathEvaluator != null)
        {
            if (subStep.HasPatientAnimation())
            {
                chunaPathEvaluator.ReleasePoseHold();
            }
            else
            {
                chunaPathEvaluator.ClearPatientAnimationBinding();
                chunaPathEvaluator.HoldCurrentPose();
            }
        }

        // ★ 각도 표시 UI 제어 (회전/측굴 단계에서만 표시)
        UpdateAngleDisplayVisibility(subStep);

        bool isPassiveStretch = !string.IsNullOrEmpty(subStep.conditionType) &&
                                subStep.conditionType.Trim().Equals("PassiveStretch", System.StringComparison.OrdinalIgnoreCase);

        // ★ 두개골 교정 술기 분기 (신규 conditionType — 기존 시나리오에는 없는 값)
        string cranialType = subStep.conditionType?.Trim() ?? "";
        bool isCranial = IsCranialConditionType(cranialType);

        // ★손 녹화가 없는 substep은 <b>일단 가이드손을 숨기고</b> 시작한다(2026-08-13).
        //   가이드손은 '로드된 마지막 녹화'를 계속 들고 있어서, 명시적으로 끄지 않으면
        //   진단에서 쓴 녹화가 파지·교정 단계까지 그대로 떠 있었다(사용자 지적: "양 엄지 진단이
        //   끝났는데 계속 나온다"). 두개골 단계는 아래 HandleCranial이 자기 가이드를 다시 켜므로
        //   여기서 꺼도 무해하다 — 켤 단계만 켜지는 구조가 된다.
        if (chunaPathEvaluator != null && string.IsNullOrEmpty(subStep.handTrackingFileName))
            chunaPathEvaluator.HideGuideHandKeepHeldInternal();

        if (isCranial)
            HandleCranial(subStep, cranialType);
        else if (isPassiveStretch)
            HandlePassiveStretch(subStep);
        else if (!string.IsNullOrEmpty(subStep.handTrackingFileName))
            HandleHandPoseTracking(subStep);
        else if (subStep.HasPatientAnimation() && cranialController == null)
            HandleAutoPlayAnimation(subStep);
        else if (subStep.HasPatientAnimation() && chunaPathEvaluator != null)
        {
            // ★두개골 계열에서 '판정도 손 녹화도 없는데 애니만 있는 단계'(PJ 전환 등).
            //   예전엔 여기서도 AutoPlay 평가 파이프라인을 돌렸는데, 그 안에서 가이드손이
            //   <b>처음부터 다시 재생</b>되어 "전환하니까 가이드손이 시작 자세로 튀어나온다"가 됐다
            //   (2026-08-12 사용자 지적). 두개골의 진행 게이트는 조건이지 AutoPlay 완료가 아니므로
            //   여기서는 <b>클립만 재생</b>하고 가이드손은 마지막 자세를 유지한 채 숨긴다.
            //   ★anim=/animSpeed= 도 여기서 읽는다 — 판정 없는 '이어서 마저 재생' 단계가 있다
            //     (흉추 신전: 바디드롭 뒤 나머지 프레임을 마저 내린다).
            float spd = ParseTokenFloat(subStep.conditionParams, "animspeed=", 1f);
            if (TryParseAnimRange(subStep.conditionParams, out float af, out float at))
                chunaPathEvaluator.PlayPatientAnimationRange(subStep.patientAnimationClip.Trim(), af, at, spd);
            else
                chunaPathEvaluator.SetPatientAnimation(subStep.patientAnimationClip.Trim(),
                                                       AnimationPlayMode.AutoPlay);
            chunaPathEvaluator.HideGuideHandKeepHeldInternal();
        }
        else if (chunaPathEvaluator != null)
        {
            // ★판정도 손 녹화도 없는 단계 = 나레이션이 끝나면 그냥 넘어가는 안내 구간
            //   (진단 결과·전환 설명 등). 여기서 앞 단계의 가이드손이 남아 있으면
            //   "지금 이 동작을 따라 하라"는 잘못된 신호가 된다 → 숨긴다(08-11 사용자 지시).
            //   ★단 '이 클립은 이미 봤다'는 기록은 지우지 않는다 — 지우면 다음 단계에서 같은 동작이
            //     처음부터 다시 재생돼 "어디서는 초기화되고 어디서는 유지되는" 들쭉날쭉함이 생긴다.
            chunaPathEvaluator.HideGuideHandKeepHeldInternal();
        }

        // ★ 호흡 HUD(링)는 견착·호흡(③ cranialDepthBreath) substep에서만 활성.
        //   그 외 모든 substep 진입 시 끈다(활성화는 BreathingCondition→StartBreathingWindow가 담당).
        if (cranialController != null &&
            !cranialType.Equals("cranialDepthBreath", System.StringComparison.OrdinalIgnoreCase))
        {
            cranialController.HideBreathingHud();
        }

        // ★ 진단 단계(유지 타이머·호흡 유도 문구)는 cranialTouch substep에서만 살아 있다.
        //   그 외 substep 진입 시 정리 — 안 하면 진단3(안내 전용) 이후로도 타이머가 계속 돌고
        //   진단 파지 구체가 화면에 남는다. (진단1→진단2 전환은 BeginDiagnosisStage가 알아서 재시작)
        if (cranialController != null &&
            !cranialType.Equals("cranialTouch", System.StringComparison.OrdinalIgnoreCase))
        {
            cranialController.EndDiagnosisStage();
        }

        // ★ 두개골 조건이 아닌 substep(진단3·재평가·시작/종료 안내 등)에서는 파지 구체를 정리한다.
        //   파지 단계에서 켠 교정 파지점을 끄는 곳이 없어 재평가·종료까지 화면에 남아 있었다.
        //   ★단 '교정' 국면 안에서는 교정 파지점을 남긴다 — 파지를 유지한 채 교정하는 단계인데
        //     조건 타입이 HandPose라는 이유로 구체를 지우면 어디를 잡고 있어야 하는지 안 보인다.
        //   ★교정 국면 안에서는 교정 파지점을 <b>계속 남긴다</b> — 파지를 유지한 채 진행하는 구간이라
        //     접촉 판정이 살아 있어야 하고, 학습자도 어디를 잡고 있어야 하는지 봐야 한다.
        //     (2026-08-12에 '판정하는 substep에서만 표시'로 좁혔다가, 안내 행을 지나면
        //      cranialPressure가 영영 성립하지 않는 회귀가 나서 되돌렸다.)
        if (cranialController != null && !isCranial)
        {
            bool inCorrectionPhase = currentPhase != null &&
                                     !string.IsNullOrEmpty(currentPhase.phaseName) &&
                                     currentPhase.phaseName.Contains("교정");
            cranialController.HideGripPoints(keepCorrectionGrips: inCorrectionPhase);
        }

        // ★ 힘의 방향 화살표: 이 단계에 배정된 그룹만 켠다.
        //   그룹이 없는 단계(진단·재평가·안내)는 자동으로 아무것도 안 보인다 —
        //   촉진으로 좌우를 비교하는 단계에 방향 힌트를 주지 않기 위한 규칙과 일치한다.
        //   ★시나리오까지 넘긴다 — 안 넘기면 OM·PM 화살표가 PJ 실습 중에도 켜진다(08-12 수정).
        ResolveForceArrowDirector();
        forceArrowDirector?.ShowFor(
            currentConfig != null ? currentConfig.scenarioName : currentScenario?.scenarioName,
            currentPhase?.phaseName, currentStep?.stepName, subStep.subStepNo);

        // ★ 이마 견착 위치 가이드: conditionParams에 brace가 있는 substep에서만 어깨 댈 자리를 표시한다.
        //   어깨는 트래킹 소스가 없어 접촉을 판정하지 않는다 — 자리를 보여주면 거기 대느라 상체가 숙여지고,
        //   그 숙임은 기존 프록시(헤드셋-이마 근접)가 이미 보고 있다.
        //   ★자세 안정화의 활성 여부로 켜면 안 된다: 지금 두개골 호흡은 전부 gripGate라 그 값이 늘 false다.
        cranialController?.SetBraceGuideVisible(HasFlagToken(subStep.conditionParams, "brace"));

        // ★ 골격 표시: 단계마다 보여야 할 뼈가 다르다(두개골 = 진단·교정·재평가가 서로 다름).
        //   해당 항목이 없는 단계는 이전 표시를 되돌리고 전체를 보여준다(무회귀).
        ResolveSkeletonFocus();
        skeletonFocus?.ApplyStep(
            currentConfig != null ? currentConfig.scenarioName : currentScenario?.scenarioName,
            currentPhase?.phaseName, currentStep?.stepName);

        // ★ 모든 evaluator 설정 완료 후 각도 디스플레이 홀드 범위 강제 갱신
        angleDisplay?.ForceRefreshHoldRange();
    }

    /// <summary>화살표 관리자를 한 번만 찾는다(씬에 없으면 계속 null — 기능이 꺼진 상태로 동작).</summary>
    private void ResolveForceArrowDirector()
    {
        if (forceArrowDirector != null || forceArrowLookupDone) return;
        forceArrowLookupDone = true;
        forceArrowDirector = FindFirstObjectByType<ForceArrowDirector>(FindObjectsInactive.Include);
    }

    private SkeletonFocusController skeletonFocus;
    private bool skeletonFocusLookupDone;

    /// <summary>골격 포커스도 한 번만 찾는다(없으면 기능 OFF).</summary>
    private void ResolveSkeletonFocus()
    {
        if (skeletonFocus != null || skeletonFocusLookupDone) return;
        skeletonFocusLookupDone = true;
        skeletonFocus = FindFirstObjectByType<SkeletonFocusController>(FindObjectsInactive.Include);
    }

    /// <summary>
    /// ★ PassiveStretch 처리: 보조수 접촉 게이팅 + 환자 애니메이션 자동 재생 + 가이드 손 표시
    /// 주동수 없는 스트레칭 단계 (흉쇄유돌근 등). 유사도 평가 없음, 애니메이션 완료 = 단계 완료
    /// </summary>
    private void HandlePassiveStretch(SubStepData subStep)
    {
        if (chunaPathEvaluator == null)
        {
            ChunaLogger.LogWarning("[ScenarioManager] ChunaPathEvaluator가 없어서 PassiveStretch를 사용할 수 없습니다!");
            return;
        }

        string stepName = currentStep?.stepName ?? "";

        // 스트레칭 모드 (가이드 시작 위치 결정에 사용)
        chunaPathEvaluator.SetExtendedLimitModeFromNames(stepName, subStep.handTrackingFileName);

        // 가이드 손 데이터 로드 (보조수 가이드 표시용, 유사도 평가 미등록)
        if (!string.IsNullOrEmpty(subStep.handTrackingFileName) && chunaPathEvaluatorBridge != null)
        {
            chunaPathEvaluatorBridge.LoadFromCSV(subStep.handTrackingFileName);
        }

        // Gated AutoPlay 시작 (ChunaPathEvaluator가 conditionType으로 gated 자동 판단)
        chunaPathEvaluator.StartAutoPlayFromSubStep(subStep);

        // StartHold 없이 즉시 가이드 손 재생 (주동수 없음)
        // ★구간을 (0,1)로 명시하고 루프를 끈다.
        //   인자 없는 오버로드는 runtimeGuideStartRatio/EndRatio(기본 0~0.4)와 loopGuideHands(=1)를 쓴다
        //   → 클립 앞 40%만 무한 반복된다. 가이드손 규약은 "전체를 1회"다.
        // ★이 단계에 손 녹화가 있을 때만 재생한다(2026-08-13).
        //   예전엔 무조건 재생해서, 녹화가 없는 단계에서는 <b>직전에 로드된 남의 녹화</b>가 그대로 떴다.
        //   (제2늑골 '팔 외전'에서 진단 녹화의 왼손 가이드가 남아 있던 원인 — 사용자 지적.
        //    같은 함정이 08-03에도 판정 쪽에서 나왔다: 파일이 없으면 직전 녹화가 남아 오판정.)
        if (!string.IsNullOrEmpty(subStep.handTrackingFileName))
            chunaPathEvaluator.StartGuideHandPlaybackInternal(0f, 1f, false);
        else
            chunaPathEvaluator.HideGuideHandKeepHeldInternal();

        // AutoPlay 완료 시 SubStep 완료 처리
        chunaPathEvaluator.OnAutoPlayCompleted -= OnAutoPlayCompletedHandler;
        chunaPathEvaluator.OnAutoPlayCompleted += OnAutoPlayCompletedHandler;

        if (showDebugLog)
            ChunaLogger.Log($"<color=cyan>[ScenarioManager] PassiveStretch 시작: {subStep.handTrackingFileName} / {subStep.patientAnimationClip}</color>");
    }

    /// <summary>
    /// ★ 시나리오에 cranial 조건 substep이 있으면 CranialRig(CranialAdjustmentController 루트)를 활성화,
    /// 없으면 비활성화. CranialAdjustmentController는 두경부 전용 오브젝트들의 루트에 배치할 것.
    /// </summary>
    private void ApplyCranialRigForScenario(ScenarioData scenario)
    {
        // A안(술기별 리그 분리): 씬의 두개골 리그를 전부 모아, 현재 시나리오 이름과 일치하는 리그만 활성화하고
        // 나머지(OM/PM 등)는 비활성화한다. 이렇게 해야 서로 다른 술기의 파지 구체가 씬에서 겹치지 않는다.
        var allRigs = FindObjectsByType<CranialAdjustmentController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (allRigs == null || allRigs.Length == 0) { cranialController = null; return; }  // 두경부 리그 없음 (비두경부 빌드에서 정상)

        bool hasCranial = ScenarioHasCranialCondition(scenario);
        string scenName = currentConfig != null ? currentConfig.scenarioName
                        : (scenario != null ? scenario.scenarioName : "");
        CranialAdjustmentController target = hasCranial ? ResolveCranialController(scenName, allRigs) : null;

        foreach (var rig in allRigs)
        {
            if (rig == null) continue;
            bool active = hasCranial && rig == target;
            if (rig.gameObject.activeSelf != active) rig.gameObject.SetActive(active);
        }
        cranialController = target;   // HandleCranial이 이 시나리오 동안 재사용

        // 반대방향 cross-talk 차단: 두경부일 때 기존 머리 판정 콜라이더(비두경부 손접촉 감지용)를
        // 비활성화. 비두경부 시나리오에선 !hasCranial=true로 다시 켜짐(원래 용도 복원).
        if (nonCranialHeadColliders != null)
        {
            foreach (var col in nonCranialHeadColliders)
                if (col != null) col.enabled = !hasCranial;
        }

        // 시나리오(재)시작 시 래칭 상태 초기화 — 이전 run의 BreathingComplete/리듬 대칭 상태 누수 방지.
        if (hasCranial && target != null)
            target.ResetAll();

        if (showDebugLog)
            ChunaLogger.Log($"[ScenarioManager] CranialRig {(hasCranial ? $"활성화({(target != null ? target.ScenarioName : "?")})" : "비활성화")}: {scenario.scenarioName}");
    }

    /// <summary>
    /// 씬의 여러 두개골 리그 중 시나리오 이름이 일치하는 컨트롤러를 선택한다(A안: 술기별 리그 분리).
    /// 우선순위: ① ScenarioName 정확 일치 → ② ScenarioName이 빈 '레거시 기본'(기존 OM 리그) → ③ 첫 번째.
    /// (기존 OM 리그는 ScenarioName을 비워두면 씬 수정 없이 그대로 폴백으로 선택된다.)
    /// </summary>
    private CranialAdjustmentController ResolveCranialController(string scenarioName, CranialAdjustmentController[] allRigs = null)
    {
        if (allRigs == null)
            allRigs = FindObjectsByType<CranialAdjustmentController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (allRigs == null || allRigs.Length == 0) return null;

        foreach (var rig in allRigs)
            if (rig != null && !string.IsNullOrEmpty(rig.ScenarioName) && rig.ScenarioName == scenarioName)
                return rig;   // ① 정확 일치
        foreach (var rig in allRigs)
            if (rig != null && string.IsNullOrEmpty(rig.ScenarioName))
                return rig;   // ② 레거시 기본(이름 미설정 = 기존 OM)
        // ③ 맞는 리그가 없다 → ★<b>아무 리그나 갖다 쓰지 않는다.</b>
        //   예전에는 allRigs[0]으로 폴백했는데, 그러면 <b>전혀 다른 술기의 파지점이 화면에 뜬다</b>.
        //   2026-08-12에 제1늑골을 개명하면서 씬 리그의 scenarioName만 옛 이름으로 남자
        //   두개골 OM 리그가 대신 켜져서 "제1늑골인데 두개골 파지점이 나온다"가 됐다.
        //   경고만으로는 묻힌다 — 틀린 파지점을 보여 주느니 안 보여 주는 편이 낫다.
        var names = new System.Text.StringBuilder();
        foreach (var r in allRigs)
            if (r != null) names.Append($"'{r.ScenarioName}' ");

        ChunaLogger.LogError(
            $"[ScenarioManager] 시나리오 '{scenarioName}'에 맞는 파지점 리그가 없습니다 — 파지점을 표시하지 않습니다.\n" +
            $"   씬에 있는 리그: {names}\n" +
            $"   ★씬 리그의 scenarioName을 '{scenarioName}'과 똑같이 맞추세요(대소문자·띄어쓰기까지).\n" +
            "   확인: 메뉴 GuideChuna/시나리오 배선 점검 (읽기 전용)");
        return null;
    }

    /// <summary>
    /// cranial 조건 타입(cranialGrip/cranialPressure/cranialDepthBreath) 여부
    /// </summary>
    /// <summary>
    /// conditionParams에서 <c>anim=시작:끝</c>(정규화 0~1)을 읽는다. 없으면 false.
    /// 예: <c>anim=0:0.5</c> = 클립의 앞 절반만 재생하고 멈춘다.
    /// </summary>
    private static bool TryParseAnimRange(string prms, out float from, out float to)
    {
        from = 0f; to = 1f;
        foreach (string tok in SplitParams(prms))
        {
            string t = tok.Trim();
            if (!t.StartsWith("anim=", System.StringComparison.OrdinalIgnoreCase)) continue;

            string[] parts = t.Substring(5).Split(':');
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (parts.Length == 2 &&
                float.TryParse(parts[0], System.Globalization.NumberStyles.Float, inv, out from) &&
                float.TryParse(parts[1], System.Globalization.NumberStyles.Float, inv, out to))
                return true;

            ChunaLogger.LogWarning($"[ScenarioManager] anim 구간을 못 읽었습니다: '{t}' (형식: anim=0:0.5)");
            return false;
        }
        return false;
    }

    /// <summary>conditionParams의 <c>hand=left|right|both</c>를 읽는다(없으면 양손).
    /// 판정과 가이드손 표시가 <b>같은 토큰</b>을 쓴다 — 두 곳이 어긋나면
    /// "오른손만 판정하는데 양손 가이드가 뜨는" 상태가 된다.</summary>
    private static CranialAdjustmentController.JudgeHand ParseJudgeHand(string prms)
    {
        string p = (prms ?? "").ToLowerInvariant();
        if (p.Contains("hand=left")) return CranialAdjustmentController.JudgeHand.왼손;
        if (p.Contains("hand=right")) return CranialAdjustmentController.JudgeHand.오른손;
        return CranialAdjustmentController.JudgeHand.양손;
    }

    /// <summary>conditionParams의 <c>finger=thumb|index|middle|ring|pinky|palm</c>. 없으면 손바닥.</summary>
    private static CranialFinger ParseFinger(string prms)
    {
        string p = (prms ?? "").ToLowerInvariant();
        if (p.Contains("finger=thumb")) return CranialFinger.Thumb;
        if (p.Contains("finger=index")) return CranialFinger.Index;
        if (p.Contains("finger=middle")) return CranialFinger.Middle;
        if (p.Contains("finger=ring")) return CranialFinger.Ring;
        if (p.Contains("finger=pinky")) return CranialFinger.Pinky;
        return CranialFinger.Palm;
    }

    /// <summary>conditionParams에서 <c>키=값</c> 형태의 실수를 읽는다. 없으면 기본값.</summary>
    private static float ParseTokenFloat(string prms, string keyLower, float fallback)
    {
        foreach (string tok in SplitParams(prms))
        {
            string t = tok.Trim();
            if (!t.ToLowerInvariant().StartsWith(keyLower)) continue;
            if (float.TryParse(t.Substring(keyLower.Length), System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out float v))
                return v;
        }
        return fallback;
    }

    /// <summary>conditionParams를 ';'로 쪼갠다(빈 토큰 제거).</summary>
    private static string[] SplitParams(string prms)
    {
        if (string.IsNullOrEmpty(prms)) return System.Array.Empty<string>();
        return prms.Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// 플래그가 아닌 첫 토큰을 돌려준다(진단 단계 ID처럼 '값'으로 쓰이는 토큰).
    /// xray·gripGate처럼 동작을 켜는 플래그는 건너뛴다.
    /// </summary>
    private static readonly System.Collections.Generic.HashSet<string> CranialFlagTokens =
        new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        { "xray", "gripgate", "touchonce", "bothhands", "palmsupport", "startholdonly", "guideonly", "skipmidhold",
          "brace" };

    /// <summary>플래그 토큰이 있는가(대소문자 무시). 값 없이 켜고 끄는 표시용 토큰에 쓴다.</summary>
    private static bool HasFlagToken(string prms, string flag)
    {
        foreach (string tok in SplitParams(prms))
            if (tok.Trim().Equals(flag, System.StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static string FirstNonFlagToken(string prms)
    {
        foreach (string tok in SplitParams(prms))
        {
            string t = tok.Trim();
            // 'key=value' 형태(호흡 규격 등)는 값 토큰이 아니다 — 단계 ID로 오인하면 폴백이 걸린다.
            if (t.Length > 0 && t.IndexOf('=') < 0 && !CranialFlagTokens.Contains(t)) return t;
        }
        return "";
    }

    /// <summary>conditionParams에서 'key=value' 토큰의 값을 읽는다(없으면 defaultValue).
    /// 호흡 규격을 substep 단위로 주기 위한 것 — 예: "gripGate;breaths=3;inhale=3;exhale=5;firstScale=1.6".
    /// ★리그 오버라이드 하나로는 한 술기 안에서 국면마다 다른 호흡을 표현할 수 없어 도입했다(PJ 교정).</summary>
    private static float NamedParam(string prms, string key, float defaultValue = 0f)
    {
        foreach (string tok in SplitParams(prms))
        {
            int eq = tok.IndexOf('=');
            if (eq <= 0) continue;
            if (!tok.Substring(0, eq).Trim().Equals(key, System.StringComparison.OrdinalIgnoreCase)) continue;
            if (float.TryParse(tok.Substring(eq + 1).Trim(), System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out float v))
                return v;
        }
        return defaultValue;
    }

    /// <summary>conditionParams의 "start=inhale|exhale"을 읽는다(없으면 Keep = 리그 값 유지).</summary>
    private static BreathingSyncHUD.StartPhase NamedStartPhase(string prms)
    {
        foreach (string tok in SplitParams(prms))
        {
            int eq = tok.IndexOf('=');
            if (eq <= 0) continue;
            if (!tok.Substring(0, eq).Trim().Equals("start", System.StringComparison.OrdinalIgnoreCase)) continue;
            string v = tok.Substring(eq + 1).Trim();
            if (v.Equals("inhale", System.StringComparison.OrdinalIgnoreCase)) return BreathingSyncHUD.StartPhase.Inhale;
            if (v.Equals("exhale", System.StringComparison.OrdinalIgnoreCase)) return BreathingSyncHUD.StartPhase.Exhale;
        }
        return BreathingSyncHUD.StartPhase.Keep;
    }

    private static bool IsCranialConditionType(string conditionType)
    {
        if (string.IsNullOrEmpty(conditionType)) return false;
        return conditionType.Equals("cranialTouch", System.StringComparison.OrdinalIgnoreCase) ||
               conditionType.Equals("cranialGrip", System.StringComparison.OrdinalIgnoreCase) ||
               conditionType.Equals("cranialPressure", System.StringComparison.OrdinalIgnoreCase) ||
               conditionType.Equals("cranialDepthBreath", System.StringComparison.OrdinalIgnoreCase) ||
               conditionType.Equals("cranialGlide", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 시나리오 전체 substep을 스캔해 cranial 조건 포함 여부 반환
    /// </summary>
    private static bool ScenarioHasCranialCondition(ScenarioData scenario)
    {
        if (scenario == null) return false;
        foreach (var phase in scenario.phases)
            foreach (var step in phase.steps)
                foreach (var sub in step.subSteps)
                    if (IsCranialConditionType(sub.conditionType?.Trim()))
                        return true;
        return false;
    }

    /// <summary>
    /// ★ 두개골 교정 술기 조건 등록 (conditionType="cranialGrip"/"cranialPressure"/"cranialDepthBreath")
    /// IScenarioCondition을 ConditionManager에 등록만 하면 폴링/완료피드백/NextSubStep 레일을 그대로 탄다.
    /// </summary>
    private void HandleCranial(SubStepData subStep, string conditionType)
    {
        if (conditionManager == null)
        {
            ChunaLogger.LogError("[ScenarioManager] ScenarioConditionManager를 찾을 수 없습니다! (Cranial)");
            return;
        }

        if (cranialController == null)   // 보통 ApplyCranialRigForScenario가 이미 선택해 둠(폴백 경로)
            cranialController = ResolveCranialController(currentConfig != null ? currentConfig.scenarioName : "");

        if (cranialController == null)
        {
            ChunaLogger.LogError("[ScenarioManager] CranialAdjustmentController를 씬에서 찾을 수 없습니다!");
            return;
        }

        // ★ 이번 단계에서 쓸 손 — 판정과 가이드손 표시가 같은 토큰(hand=)을 쓴다.
        //   한 손씩 순서대로 대는 술기에서 반대 손 가이드가 떠 있으면 어느 손을 대라는지 알 수 없다.
        var handScope = ParseJudgeHand(subStep.conditionParams);
        cranialController.SetGuideHandScope(handScope);
        //   ★가이드손 '이미 봤다' 판정에도 손 범위를 넣는다 — 오른손만 보여 준 뒤 양손을 보여 줄 때
        //     키가 같으면 새로 보이는 왼손이 멈춘 채로 나타난다.
        chunaPathEvaluator?.SetGuideScopeTag(handScope.ToString());

        // ★ 환자 애니메이션: 다른 시나리오와 똑같이 CSV의 patientAnimationClip으로 켜고 끈다.
        //   (예: 진단 단계에 '굴곡신전'을 넣으면 환자가 호흡하고, 다음 단계에 'idle'을 넣으면 멈춘다)
        //   두개골 단계는 진행 게이트가 조건(cranialTouch 등)이므로 AutoPlay 완료가 단계를 넘기면 안 된다
        //   → StartAutoPlayFromSubStep(평가·진행 로직 포함)이 아니라 '클립 재생'만 한다.
        if (chunaPathEvaluator != null && subStep.HasPatientAnimation())
        {
            // ★conditionParams에 playOnGrip이 있으면 단계 진입이 아니라 '파지가 성립하는 순간' 재생한다.
            //   (제1늑골: 왼손 검지 측면이 늑골 파지점에 닿으면 그때 머리가 신전·우측 병진)
            bool playOnGrip = !string.IsNullOrEmpty(subStep.conditionParams) &&
                              subStep.conditionParams.ToLower().Contains("playongrip");
            float animSpeed = ParseTokenFloat(subStep.conditionParams, "animspeed=", 1f);

            if (playOnGrip)
            {
                // 구간(anim=)이 같이 적혀 있으면 접촉 시 그 구간만 재생한다.
                bool ranged = TryParseAnimRange(subStep.conditionParams, out float gf, out float gt);
                chunaPathEvaluator.ArmPatientAnimationForDeferredStart(
                    subStep.patientAnimationClip.Trim(), gf, gt, ranged, animSpeed);

                // ★어느 손이 닿으면 시작할지 — playOnGrip=right / left / both (기본 left)
                var trigger = CranialAdjustmentController.GripAnimTrigger.왼손;
                string prm = (subStep.conditionParams ?? "").ToLowerInvariant();
                if (prm.Contains("playongrip=right")) trigger = CranialAdjustmentController.GripAnimTrigger.오른손;
                else if (prm.Contains("playongrip=both")) trigger = CranialAdjustmentController.GripAnimTrigger.양손;
                cranialController.ArmAnimationOnGrip(chunaPathEvaluator, trigger);
            }
            else if (TryParseAnimRange(subStep.conditionParams, out float from, out float to))
            {
                // ★conditionParams의 anim=시작:끝 → 클립의 그 구간만 재생하고 멈춘다.
                //   한 동작을 여러 단계에 나눠 보여줄 때 쓴다(흉추 신전: 절반만 일으켰다가 나중에 끝까지).
                cranialController.DisarmAnimationOnGrip();
                chunaPathEvaluator.PlayPatientAnimationRange(
                    subStep.patientAnimationClip.Trim(), from, to, animSpeed);
            }
            else
            {
                cranialController.DisarmAnimationOnGrip();
                chunaPathEvaluator.SetPatientAnimation(subStep.patientAnimationClip.Trim(),
                                                       AnimationPlayMode.AutoPlay);
            }
            if (showDebugLog)
                ChunaLogger.Log($"<color=cyan>[ScenarioManager] Cranial 환자 애니 {(playOnGrip ? "파지 대기" : "재생")}: {subStep.patientAnimationClip}</color>");
        }

        // ★ 가이드 손(녹화) 표시: cranial 스텝에 handTrackingFileName이 있으면 기존 가이드핸드 재생을 그대로 띄운다.
        //   판정은 cranial 구체 게이트가 담당하고, 가이드 손은 순수 시각 안내(손가락별 파지점 없이 '구체 1개 + 가이드손' 방식).
        //   녹화 데이터가 양손이면 양손 그림자 손이 모두 재생된다.
        if (subStep.HasHandTracking() && chunaPathEvaluator != null)
        {
            // ★같은 동작이 이어지는 단계에서는 다시 재생하지 않는다(08-11 사용자 지시).
            //   재로드도 건너뛴다 — 프레임을 다시 읽으면 유지 중인 마지막 자세가 깨진다.
            string guideClip = subStep.handTrackingFileName.Trim();
            bool held = chunaPathEvaluator.IsGuideClipHeld(guideClip);

            if (!held)
            {
                // ★★프레임만 읽는다. 예전에는 Bridge.LoadFromCSV를 썼는데 그 안에서
                //   StartEvaluation() + StartTracking()이 같이 돌아 <b>유사도 평가 파이프라인</b>이 켜졌다.
                //   그 부작용이 두개골 술기의 증상 전부였다(08-11 확인):
                //     · 진단·파지 진입 때 "파지 위치 2초"(StartHold)가 먼저 돌고
                //     · 유사도 진행률(47% / 목표 50%)이 뜨며
                //     · <b>왼손만 대도 임계값을 넘겨 파지 단계가 넘어갔다</b>(양손 파지 게이트를 건너뜀).
                //   두개골 판정은 파지점 구체(cranialGrip)와 유지 타이머가 전부이므로 평가를 켜지 않는다.
                if (chunaPathEvaluator.IsEvaluating) chunaPathEvaluator.StopEvaluation();
                chunaPathEvaluator.LoadAndGenerateCheckpoints(guideClip);
            }

            {
                if (held)
                {
                    // 이미 끝까지 본 동작 — 마지막 자세로 세워만 둔다.
                    chunaPathEvaluator.ShowGuideHandLastFrameInternal();
                }
                else
                {
                    // ★두개골 가이드손 = 클립 전체를 1회 재생(루프 없음).
                    //   ⓐ 구간: 기본값이 0~0.4라 그냥 두면 앞 40%만 재생되고 끊긴다. 전체 재생은 원래
                    //      conditionParams에 "guideOnly"가 있을 때만 열리는데, 두개골은 그 칸을
                    //      진단 단계 ID(진단1/진단2)·유지 초로 쓰므로 그 경로를 탈 수 없다.
                    //   ⓑ 루프: 시연을 한 번 보여주면 충분하고, 계속 돌면 파지 위치를 가린다.
                    //   ★클립 이름을 넘기는 진입점을 쓴다 — 그래야 다음 단계에서 '같은 동작'인지 판별된다.
                    chunaPathEvaluator.PlayGuideHandOnceInternal(guideClip, 0f, 1f);
                }

                // 이후 제어는 컨트롤러가 '동작(자세)마다' 켜고 끈다
                // (자세가 바뀌면 그 동작을 재생 / 손을 대면 숨기고 떼면 마지막 자세로 되살림).
                cranialController.ArmGuideHandAutoHide(chunaPathEvaluator, guideClip);
            }
            if (showDebugLog)
                ChunaLogger.Log($"<color=cyan>[ScenarioManager] Cranial 가이드손 {(held ? "유지(재생 안 함)" : "재생")}: {guideClip}</color>");
        }

        // 평가 지표 수집 시작(이 단계에서 자세 성립·유지·이탈·호흡을 모은다).
        cranialController.BeginCranialMetrics(
            string.IsNullOrEmpty(subStep.conditionParams)
                ? currentStep.stepName
                : $"{currentStep.stepName}({subStep.conditionParams})",
            currentPhase?.phaseName, currentStep?.stepName);

        IScenarioCondition condition;
        string label;
        if (conditionType.Equals("cranialTouch", System.StringComparison.OrdinalIgnoreCase))
        {
            // conditionParams = 진단 단계 ID(예: 진단1/진단2). 비면 컨트롤러의 첫 단계를 쓴다.
            // ★파라미터는 ';'로 여러 토큰이 올 수 있다(xray 등) → 플래그가 아닌 첫 토큰만 단계 ID로 쓴다.
            // hold= 로 자세 유지 시간을 CSV에서 조절한다(0/미지정이면 스테이지 값).
            // ★stack=0.10;finger=thumb → 파지점 하나에 두 손(엄지 등)을 모으는 방식으로 판정.
            float dStack = ParseTokenFloat(subStep.conditionParams, "stack=", 0f);
            CranialFinger dFinger = ParseFinger(subStep.conditionParams);

            condition = new DiagnosisHoldCondition(cranialController,
                                                   FirstNonFlagToken(subStep.conditionParams),
                                                   NamedParam(subStep.conditionParams, "hold"),
                                                   dStack, dFinger);
            label = dStack > 0f ? $"Touch(양손 {dFinger} 포개짐 {dStack * 100f:F0}cm)"
                                : "Touch(⓪ 진단 자세 유지)";
        }
        else if (conditionType.Equals("cranialGlide", System.StringComparison.OrdinalIgnoreCase))
        {
            // ★척추를 손가락으로 두방→족방 한 번 쓸어내리는 촉지 진단(2026-08-18 사용자 요구).
            //   판정은 전부 SpineGlideGuide가 한다 — 여기서는 리그 하위의 그 컴포넌트를 찾아 넘길 뿐이다.
            //   ★리그 안에서 찾는다: 다른 술기 리그의 구간을 집어 오면 엉뚱한 곳을 훑어야 통과된다.
            var glide = cranialController.GetComponentInChildren<SpineGlideGuide>(true);
            condition = new SpineGlideCondition(glide);
            label = glide != null ? $"Glide({glide.DescribeSegment()})" : "Glide(★미배선 — 즉시 통과)";
        }
        else if (conditionType.Equals("cranialDepthBreath", System.StringComparison.OrdinalIgnoreCase))
        {
            // conditionParams에 "gripGate"가 있으면 호흡 1회를 인정하는 조건이
            // '이마 견착 자세'가 아니라 '양손 파지 성립'이 된다(PM처럼 손이 보이는 술기용).
            bool gripGate = !string.IsNullOrEmpty(subStep.conditionParams) &&
                            subStep.conditionParams.ToLower().Contains("gripgate");

            // substep별 호흡 규격(비면 리그 오버라이드로 폴백). PJ 교정처럼 한 술기 안에서
            // 국면마다 호흡이 다른 경우에만 쓴다 — OM·PM은 CSV에 안 적어 기존 동작 그대로다.
            int breaths = Mathf.RoundToInt(NamedParam(subStep.conditionParams, "breaths"));
            float inhaleSec = NamedParam(subStep.conditionParams, "inhale");
            float exhaleSec = NamedParam(subStep.conditionParams, "exhale");
            float firstScale = NamedParam(subStep.conditionParams, "firstScale");
            BreathingSyncHUD.StartPhase startPhase = NamedStartPhase(subStep.conditionParams);

            // ★headThrust=가 함께 적히면 '호흡 유도 + 순간 교정'을 한 단계에서 처리한다.
            //   다 내쉰 뒤 누르면 완료, 그 전에 누르면 감점하고 계속 기다린다.
            float bThrust = ParseTokenFloat(subStep.conditionParams, "headthrust=", 0f);
            float bDrop = ParseTokenFloat(subStep.conditionParams, "headdrop=", 0f);
            // ★handThrust=0.02 → 머리 대신 <b>손이 눌러 들어갔다 나오는</b> 것으로 판정.
            //   헤드셋 하강은 몸을 크게 낮춰야 해서 실제 술기 동작보다 과하다(사용자 지적).
            float bHand = ParseTokenFloat(subStep.conditionParams, "handthrust=", 0f);
            bool byHands = bHand > 0f;
            if (byHands) bDrop = bHand;

            // ★late=3 → 다 내쉰 뒤 이 시간(초) 안에 누르면 정상. 넘기면 감점(진행은 계속).
            float lateWin = ParseTokenFloat(subStep.conditionParams, "late=", 3f);

            condition = new BreathingCondition(cranialController, gripGate,
                                               breaths, inhaleSec, exhaleSec, startPhase, firstScale,
                                               bThrust, bDrop, byHands, lateWin);
            string spec = breaths > 0 ? $" {breaths}회" : "";
            label = byHands
                ? $"Breath(호흡{spec} → 날숨 끝 손 누름 {bHand * 100f:F0}cm)"
                : bThrust > 0f
                    ? $"Breath(호흡{spec} → 날숨 끝 순간 교정 {bThrust:0.##}m/s)"
                    : gripGate ? $"Breath(호흡{spec} — 파지 유지 게이트)" : $"Breath(②b 호흡{spec} — 견착 자세 게이트)";
        }
        else if (conditionType.Equals("cranialPressure", System.StringComparison.OrdinalIgnoreCase))
        {
            // 판정 = 파지(접촉) 유지. conditionParams에 숫자를 넣으면 그 초만큼 유지해야 통과한다(비면 기본 1초).
            // ★PM 교정·호흡이 이 조건을 쓴다 — 호흡 완료로 자동 진행하면 이마 견착 프록시가 성립해야 해서
            //   PM(견착 없는 술기)에선 영영 안 넘어갔다. 그래서 '접촉 유지'로 통과시킨다.
            // ★파라미터에 ';'로 다른 토큰(xray 등)이 섞일 수 있다 → 숫자로 읽히는 토큰만 골라 쓴다.
            //   통짜로 파싱하면 "5;xray"가 실패해 기본 1초로 떨어진다(등척수축 5초가 1초가 됨).
            float holdSec = 1.0f;
            foreach (string tok in SplitParams(subStep.conditionParams))
            {
                if (float.TryParse(tok, System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out float parsed) &&
                    parsed > 0f)
                {
                    holdSec = parsed;
                    break;
                }
            }

            // ★headDrop=0.06 → '파지 유지 + 머리가 6cm 내려감'. 체중을 싣는 동작(바디드롭·마지막 압박)용.
            float headDrop = 0f;
            foreach (string tok in SplitParams(subStep.conditionParams))
            {
                string t = tok.Trim();
                if (!t.StartsWith("headdrop=", System.StringComparison.OrdinalIgnoreCase)) continue;
                float.TryParse(t.Substring(9), System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out headDrop);
                break;
            }

            // ★headThrust=0.25 → '휙' 내려가는 순간 통과(유지 시간 없음). 바디드롭·순간 교정용.
            float headThrust = ParseTokenFloat(subStep.conditionParams, "headthrust=", 0f);
            // ★handThrust=0.02 → 손이 눌러 들어갔다 나오는 것으로 판정(머리보다 적게 움직여도 잡힌다).
            float handThrust = ParseTokenFloat(subStep.conditionParams, "handthrust=", 0f);
            bool byHands = handThrust > 0f;
            if (byHands) headDrop = handThrust;

            // ★brace 토큰이 붙은 유지 단계는 '이마 견착'까지 돼야 통과한다(2026-08-17).
            //   그전에는 brace가 마커 표시만 켰고 판정에는 아무 영향이 없어서,
            //   견착을 안 해도 파지만 유지되면 단계가 그냥 넘어갔다(사용자 보고).
            bool requireBrace = HasFlagToken(subStep.conditionParams, "brace");

            condition = new PressureCondition(cranialController, holdSec, 0.5f, headDrop, headThrust, byHands,
                                              requireBrace);
            label = byHands
                ? $"Pressure(손 누름 {handThrust * 100f:F0}cm — 들어갔다 나오기)"
                : headThrust > 0f
                    ? $"Pressure(순간 하강 {headThrust:0.##}m/s · 최소 {(headDrop > 0f ? headDrop : 0.03f) * 100f:F0}cm)"
                    : headDrop > 0f
                        ? $"Pressure(파지 유지 {holdSec:0.#}초 + 머리 {headDrop * 100f:F0}cm 하강)"
                        : $"Pressure(파지 유지 {holdSec:0.#}초)";
        }
        else
        {
            // ★hand=left|right|both → 한 손만 판정한다.
            //   흉추 신전은 보조수(두방수)가 먼저 머리를 받쳐 환자를 들어야 주동수(족방수) 주먹이
            //   등 밑에 들어간다 — 양손을 동시에 요구하면 물리적으로 성립할 수 없다.
            var jh = ParseJudgeHand(subStep.conditionParams);
            // ★stack=0.08 → 손끝 관절이 아니라 '양손이 한 지점에 포개졌는가'로 판정한다.
            //   두상골처럼 관절 매핑이 없는 접촉점을 쓰는 술기용(복와위 양손두상골).
            float stackGap = ParseTokenFloat(subStep.conditionParams, "stack=", 0f);

            condition = new GripPointCondition(cranialController, jh, stackGap);
            label = stackGap > 0f ? $"Grip(양손 포개짐 {stackGap * 100f:F0}cm)" : $"Grip(① 파지 — {jh})";
        }

        conditionManager.RegisterCondition(currentPhase.phaseName, currentStep.stepName, subStep.subStepNo, condition);

        if (showDebugLog)
            ChunaLogger.Log($"<color=magenta>[ScenarioManager] Cranial 조건 등록: {label}</color>");
    }

    /// <summary>
    /// ★ 환자 애니메이션 AutoPlay 처리 (핸드데이터 없이 애니메이션만 자동 재생)
    /// </summary>
    private void HandleAutoPlayAnimation(SubStepData subStep)
    {
        if (chunaPathEvaluator == null)
        {
            ChunaLogger.LogWarning("[ScenarioManager] ChunaPathEvaluator가 없어서 AutoPlay를 사용할 수 없습니다!");
            return;
        }

        string stepName = currentStep?.stepName ?? "";

        // 스트레칭/재평가 단계인 경우 확장 제한 모드 활성화
        // ★ 환측/건측 감지는 핸드데이터 이름 우선
        chunaPathEvaluator.SetExtendedLimitModeFromNames(stepName, subStep.handTrackingFileName);

        // AutoPlay 시작
        chunaPathEvaluator.StartAutoPlayFromSubStep(subStep);

        // AutoPlay 완료 시 SubStep 완료 처리
        chunaPathEvaluator.OnAutoPlayCompleted -= OnAutoPlayCompletedHandler;
        chunaPathEvaluator.OnAutoPlayCompleted += OnAutoPlayCompletedHandler;
    }

    /// <summary>
    /// AutoPlay 완료 핸들러
    /// ★ 나래이션 완료를 기다린 후 진행
    /// </summary>
    private void OnAutoPlayCompletedHandler()
    {
        // 이벤트 구독 해제
        if (chunaPathEvaluator != null)
        {
            chunaPathEvaluator.OnAutoPlayCompleted -= OnAutoPlayCompletedHandler;

            // 가이드 모드일 경우 자동 진행하지 않고 토글 대기
            if (chunaPathEvaluator.IsGuideMode)
            {
                if (showDebugLog)
                    ChunaLogger.Log("[ScenarioManager] 가이드 모드 - 토글 대기");
                return;
            }
        }

        // ★ 자동 진행 비활성화 시 토글 대기 (상급/평가 모드)
        var dm = ChunaTraining.DifficultyManager.Instance;
        if (dm != null && !dm.AutoAdvanceStep)
        {
            if (showDebugLog)
                ChunaLogger.Log("[ScenarioManager] AutoAdvanceStep=false - 토글 대기");
            return;
        }

        // ★ conditionManager가 이미 AutoPlay 완료를 기다리고 있으면 이중 진행 방지
        if (conditionManager != null && conditionManager.IsWaitingForAutoPlay)
        {
            if (showDebugLog)
                ChunaLogger.Log("[ScenarioManager] conditionManager가 AutoPlay 대기 처리 중 - 이중 진행 방지");
            return;
        }

        // ★ 나래이션 완료를 기다린 후 다음 SubStep으로 진행
        if (conditionManager != null)
        {
            conditionManager.WaitForNarrationThenNextStep();
        }
        else
        {
            // conditionManager 없으면 바로 진행 (fallback)
            NextSubStep();
        }
    }

    /// <summary>
    /// HandPose 트래킹 자동 처리 (CSV 기반, ChunaPathEvaluator 사용)
    /// </summary>
    private void HandleHandPoseTracking(SubStepData subStep)
    {
        if (conditionManager == null)
        {
            ChunaLogger.LogError("[ScenarioManager] ScenarioConditionManager를 찾을 수 없습니다!");
            return;
        }

        string phaseName = currentPhase.phaseName;
        string stepName = currentStep.stepName;
        int subStepNo = subStep.subStepNo;

        HandleCheckpointBasedTracking(subStep, phaseName, stepName, subStepNo);
    }

    /// <summary>
    /// ChunaPathEvaluator 기반 손 동작 평가 처리
    /// 각 SubStep마다 CSV를 로드하고 체크포인트를 생성
    /// </summary>
    private void HandleCheckpointBasedTracking(SubStepData subStep, string phaseName, string stepName, int subStepNo)
    {
        if (chunaPathEvaluator == null || chunaPathEvaluatorBridge == null)
        {
            ChunaLogger.LogError("[ScenarioManager] ChunaPathEvaluator 또는 Bridge를 찾을 수 없습니다!");
            return;
        }

        // ★ 스트레칭/재평가 단계 모드를 먼저 설정 (SetPatientAnimation에서 시작 위치 결정에 사용됨)
        chunaPathEvaluator.SetExtendedLimitModeFromNames(stepName, subStep.handTrackingFileName);

        // ★ 누운 환자 회전 감지 오버라이드 (축 + 측정 벡터 + 방향 반전)
        //   SetExtendedLimitModeFromNames가 회전→Y축 + 건측/환측 방향을 먼저 설정
        //   축이 Y→Z로 바뀌면 SignedAngle 부호 체계도 바뀌므로 방향도 토글 필요
        bool isLyingRotationOverride = currentConfig != null && currentConfig.overrideRotationAxis &&
            !string.IsNullOrEmpty(subStep.movementType) && subStep.movementType == "rotation";
        chunaPathEvaluator.SetUseAlternateMeasurementVector(isLyingRotationOverride);
        if (isLyingRotationOverride)
        {
            chunaPathEvaluator.SetRotationDetectionAxis(currentConfig.lyingRotationAxis);
            // ★ Y→Z 축 전환으로 부호가 뒤집히므로 기존 방향 반전 토글
            bool currentInvert = chunaPathEvaluator.InvertRotationDirection;
            chunaPathEvaluator.SetInvertRotationDirection(!currentInvert);
            if (showDebugLog)
                ChunaLogger.Log($"<color=magenta>[ScenarioManager] 누운 환자 회전 오버라이드: 축={currentConfig.lyingRotationAxis}, 측정벡터=up, 방향반전={!currentInvert}(토글전:{currentInvert})</color>");
        }

        // 1. 환자 애니메이션 설정 (StartEvaluation 전에 설정해야 첫 프레임 표시됨)
        chunaPathEvaluator.SetPatientAnimationFromSubStep(subStep);

        // ★ 피벗 설정 적용 (CSV의 pivotTarget 기반)
        ApplyPivotTarget(subStep);

        // 2. CSV 로드 및 체크포인트 생성 + 평가 시작
        chunaPathEvaluatorBridge.LoadFromCSV(subStep.handTrackingFileName);

        // 3. CheckpointPoseCondition 생성
        CheckpointPoseCondition condition = new CheckpointPoseCondition(
            chunaPathEvaluatorBridge,
            subStep.handTrackingFileName,
            conditionManager
        );

        // 4. ScenarioConditionManager에 조건 등록
        conditionManager.RegisterCondition(phaseName, stepName, subStepNo, condition);

        if (showDebugLog)
            ChunaLogger.Log($"[ScenarioManager] 핸드데이터 평가 등록: {subStep.handTrackingFileName}");
    }

    /// <summary>
    /// CSV의 pivotTarget 값을 기반으로 ChunaPathEvaluator에 피벗 설정 적용
    /// </summary>
    private void ApplyPivotTarget(SubStepData subStep)
    {
        if (subStep == null || !subStep.HasPivotTarget())
        {
            // pivotTarget 미지정 시 피벗 리셋 → 직선 거리 기반으로 fallback
            chunaPathEvaluator.SetPivotSettings(null, 0f, ChunaPathEvaluator.RotationDetectionAxis.Z, false);
            return;
        }

        Transform pivot = null;
        switch (subStep.pivotTarget.Trim().ToLower())
        {
            case "neck":
                pivot = neckPivot;
                break;
            case "leftshoulder":
                pivot = leftShoulderPivot;
                break;
            case "rightshoulder":
                pivot = rightShoulderPivot;
                break;
            case "leftupperarm":
                pivot = leftUpperArmPivot;
                break;
            case "rightupperarm":
                pivot = rightUpperArmPivot;
                break;
        }

        if (pivot != null)
        {
            var axis = subStep.GetPivotPlaneAxis() ?? ChunaPathEvaluator.RotationDetectionAxis.Z;
            bool invert = subStep.GetInvertAngle();
            chunaPathEvaluator.SetPivotSettings(pivot, 0f, axis, invert); // 0f = 자동 계산
            if (showDebugLog)
                ChunaLogger.Log($"[ScenarioManager] 피벗 설정 적용: {subStep.pivotTarget} → {pivot.name}, 축:{axis}, 반전:{invert}");
        }
        else
        {
            ChunaLogger.LogWarning($"[ScenarioManager] 피벗 Transform 미할당: {subStep.pivotTarget}. Inspector에서 할당하세요.");
        }
    }

    private void Log(string message)
    {
        if (showDebugLog)
        {
            ChunaLogger.Log($"[ScenarioManager] {message}");
        }
    }

    private void LogError(string message)
    {
        ChunaLogger.LogError($"[ScenarioManager] {message}");
    }

    private void OnDestroy()
    {
        eventSystem?.Clear();

        if (exitPopupController != null)
        {
            exitPopupController.OnMainMenuSelected.RemoveListener(SaveIncompleteResultIfTracking);
            exitPopupController.OnRetrySelected.RemoveListener(SaveIncompleteResultIfTracking);
        }
    }

    // ========== 각도 표시 UI 제어 ==========

    /// <summary>
    /// SubStep의 핸드데이터 이름에 따라 각도 표시 UI 표시/숨김
    /// 핸드 데이터가 있을 때만 표시
    /// </summary>
    private void UpdateAngleDisplayVisibility(SubStepData subStep)
    {
        string handDataName = subStep?.handTrackingFileName ?? "";

        if (showDebugLog)
        {
            ChunaLogger.Log($"<color=yellow>[ScenarioManager] UpdateAngleDisplayVisibility 호출</color>");
            ChunaLogger.Log($"  - handTrackingFileName: '{handDataName}'");
        }

        // ★ 핸드 데이터가 없으면 각도 표시 안 함
        if (string.IsNullOrEmpty(handDataName))
        {
            angleDisplay?.Hide();
            if (showDebugLog)
                ChunaLogger.Log($"<color=orange>[ScenarioManager] 각도 표시 UI 숨김: 핸드 데이터 없음</color>");
            return;
        }

        // 핸드 데이터 이름과 동일한 프리셋을 찾아 적용 (시나리오 접두사 우선 매칭)
        string scenarioName = currentConfig != null ? currentConfig.scenarioName : "";
        if (angleDisplay != null && angleDisplay.ApplyPreset(scenarioName, handDataName))
        {
            ChunaLogger.Log($"<color=green>[ScenarioManager] 각도 표시 UI 프리셋 적용: {handDataName} (시나리오: {scenarioName})</color>");
        }
        else
        {
            angleDisplay?.Hide();
            if (showDebugLog)
                ChunaLogger.Log($"<color=orange>[ScenarioManager] 각도 표시 UI 숨김: '{handDataName}' 프리셋 없음</color>");
        }
    }


    // ========== Animator Controller 전환 ==========

    /// <summary>
    /// 현재 Config의 Animator Controller를 적용 (Bootstrapper에서도 호출)
    /// </summary>
    public void ApplyAnimatorController()
    {
        SwitchAnimatorController();
    }

    private void SwitchAnimatorController()
    {
        if (patientAnimator == null)
        {
            var patient = GameObject.FindGameObjectWithTag("Patient");
            if (patient != null)
                patientAnimator = patient.GetComponentInChildren<Animator>();
        }

        if (patientAnimator == null)
        {
            ChunaLogger.LogWarning("[ScenarioManager] patientAnimator를 찾을 수 없어 Controller 전환 건너뜀");
            return;
        }

        if (currentConfig != null && currentConfig.animatorController != null)
        {
            patientAnimator.runtimeAnimatorController = currentConfig.animatorController;
            if (showDebugLog)
                ChunaLogger.Log($"<color=green>[ScenarioManager] Animator Controller 전환: {currentConfig.animatorController.name}</color>");
        }
        else
        {
            ChunaLogger.LogWarning("[ScenarioManager] ScenarioConfig에 animatorController가 할당되지 않았습니다.");
        }
    }

    // ========== Contact Target 설정 ==========

    /// <summary>
    /// SubStep의 contactTarget 설정을 ChunaPathEvaluator에 적용
    /// </summary>
    /// <summary>
    /// Phase별 회전 임계점 오버라이드 적용
    /// movementType이 rotation이고 phaseOverride가 있으면 회전 임계점 교체
    /// rotation이 아니면 기본값 복원
    /// </summary>
    private void ApplyPhaseThresholdOverride()
    {
        if (currentConfig == null || chunaPathEvaluator == null || currentPhase == null) return;
        if (currentConfig.phaseOverrides == null || currentConfig.phaseOverrides.Length == 0) return;

        bool isRotation = currentSubStep != null &&
            !string.IsNullOrEmpty(currentSubStep.movementType) &&
            currentSubStep.movementType.ToLower() == "rotation";

        if (isRotation)
        {
            var phaseOverride = currentConfig.FindPhaseOverride(currentPhase.phaseName);
            if (phaseOverride != null)
            {
                chunaPathEvaluator.ApplyPhaseRotationThresholds(phaseOverride);
                return;
            }
        }

        // rotation이 아니거나 매칭되는 phase 오버라이드가 없으면 기본값 복원
        chunaPathEvaluator.RestoreDefaultThresholds(currentConfig);
    }

    private void ApplyContactTarget(SubStepData subStep)
    {
        if (chunaPathEvaluator == null) return;

        if (subStep.IsPostureGuideStep())
        {
            // 자세지시: ScenarioConfig의 postureGuideContactTarget 사용
            ContactTarget target = currentConfig != null ? currentConfig.postureGuideContactTarget : ContactTarget.LeftArm;
            chunaPathEvaluator.SetContactTarget(target);
            if (showDebugLog)
                ChunaLogger.Log($"<color=cyan>[ScenarioManager] 접촉 감지 부위 (자세지시): {target}</color>");
        }
        else
        {
            // 기본: 주동수 + 보조수 동시 체크
            ContactTarget primary = currentConfig != null ? currentConfig.primaryContactTarget : ContactTarget.Head;
            ContactTarget assist = currentConfig != null ? currentConfig.assistContactTarget : ContactTarget.Shoulder;

            // CSV에서 명시적으로 부위를 지정한 경우 그것만 사용
            ContactTarget? explicitTarget = subStep.GetContactTargetOrNull();
            if (explicitTarget.HasValue)
            {
                chunaPathEvaluator.SetContactTarget(explicitTarget.Value);
                if (showDebugLog)
                    ChunaLogger.Log($"<color=cyan>[ScenarioManager] 접촉 감지 부위 (명시): {explicitTarget.Value}</color>");
            }
            else
            {
                // 주동수 + 보조수 동시 체크
                chunaPathEvaluator.SetContactTargets(primary, assist);
                if (showDebugLog)
                    ChunaLogger.Log($"<color=cyan>[ScenarioManager] 접촉 감지 부위 (주동수+보조수): {primary} + {assist}</color>");
            }
        }
    }
}