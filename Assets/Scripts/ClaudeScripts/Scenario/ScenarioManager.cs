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
    private void CompleteScenario()
    {
        // ✅ 결과 추적 종료 및 데이터 저장
        if (resultTracker != null)
        {
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
            resultTracker.StartSubStep(currentPhase.phaseName, currentStep.stepName);
        }

        // ★ Phase별 임계점 오버라이드 적용 (사각근 전부/중부/후부 등)
        ApplyPhaseThresholdOverride();

        // ★ 접촉 감지 부위 설정 (시나리오 CSV의 contactTarget 컬럼)
        ApplyContactTarget(subStep);

        // ★ 각도 표시 UI 제어 (회전/측굴 단계에서만 표시)
        UpdateAngleDisplayVisibility(subStep);

        bool isPassiveStretch = !string.IsNullOrEmpty(subStep.conditionType) &&
                                subStep.conditionType.Trim().Equals("PassiveStretch", System.StringComparison.OrdinalIgnoreCase);

        // ★ 두개골 교정 술기 분기 (신규 conditionType — 기존 시나리오에는 없는 값)
        string cranialType = subStep.conditionType?.Trim() ?? "";
        bool isCranial = IsCranialConditionType(cranialType);

        if (isCranial)
            HandleCranial(subStep, cranialType);
        else if (isPassiveStretch)
            HandlePassiveStretch(subStep);
        else if (!string.IsNullOrEmpty(subStep.handTrackingFileName))
            HandleHandPoseTracking(subStep);
        else if (subStep.HasPatientAnimation())
            HandleAutoPlayAnimation(subStep);

        // ★ 호흡 HUD(링)는 견착·호흡(③ cranialDepthBreath) substep에서만 활성.
        //   그 외 모든 substep 진입 시 끈다(활성화는 BreathingCondition→StartBreathingWindow가 담당).
        if (cranialController != null &&
            !cranialType.Equals("cranialDepthBreath", System.StringComparison.OrdinalIgnoreCase))
        {
            cranialController.HideBreathingHud();
        }

        // ★ 모든 evaluator 설정 완료 후 각도 디스플레이 홀드 범위 강제 갱신
        angleDisplay?.ForceRefreshHoldRange();
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
        chunaPathEvaluator.StartGuideHandPlaybackInternal();

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
        return allRigs[0];    // ③ 폴백
    }

    /// <summary>
    /// cranial 조건 타입(cranialGrip/cranialPressure/cranialDepthBreath) 여부
    /// </summary>
    private static bool IsCranialConditionType(string conditionType)
    {
        if (string.IsNullOrEmpty(conditionType)) return false;
        return conditionType.Equals("cranialTouch", System.StringComparison.OrdinalIgnoreCase) ||
               conditionType.Equals("cranialGrip", System.StringComparison.OrdinalIgnoreCase) ||
               conditionType.Equals("cranialPressure", System.StringComparison.OrdinalIgnoreCase) ||
               conditionType.Equals("cranialDepthBreath", System.StringComparison.OrdinalIgnoreCase);
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

        // ★ 가이드 손(녹화) 표시: cranial 스텝에 handTrackingFileName이 있으면 기존 가이드핸드 재생을 그대로 띄운다.
        //   판정은 cranial 구체 게이트가 담당하고, 가이드 손은 순수 시각 안내(손가락별 파지점 없이 '구체 1개 + 가이드손' 방식).
        //   녹화 데이터가 양손이면 양손 그림자 손이 모두 재생된다.
        if (subStep.HasHandTracking() && chunaPathEvaluatorBridge != null)
        {
            chunaPathEvaluatorBridge.LoadFromCSV(subStep.handTrackingFileName);
            if (chunaPathEvaluator != null) chunaPathEvaluator.StartGuideHandPlaybackInternal();
            if (showDebugLog)
                ChunaLogger.Log($"<color=cyan>[ScenarioManager] Cranial 가이드손 재생: {subStep.handTrackingFileName}</color>");
        }

        IScenarioCondition condition;
        string label;
        if (conditionType.Equals("cranialTouch", System.StringComparison.OrdinalIgnoreCase))
        {
            condition = new DiagnosisTouchCondition(cranialController);
            label = "Touch(⓪ 진단 촉진)";
        }
        else if (conditionType.Equals("cranialDepthBreath", System.StringComparison.OrdinalIgnoreCase))
        {
            condition = new BreathingCondition(cranialController);
            label = "Breath(②b 호흡)";
        }
        else if (conditionType.Equals("cranialPressure", System.StringComparison.OrdinalIgnoreCase))
        {
            condition = new PressureCondition(cranialController);
            label = "Pressure(②a 압력·방향)";
        }
        else
        {
            condition = new GripPointCondition(cranialController);
            label = "Grip(① 파지)";
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