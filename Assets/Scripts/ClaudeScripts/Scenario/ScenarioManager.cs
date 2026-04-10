using UnityEngine;
using System.Collections.Generic;
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

        // UI 자동 배치
        if (uiPositioner != null)
            uiPositioner.PositionUIElements();

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

        StartScenario(collection.scenarios[0]);
    }

    /// <summary>
    /// 다음 SubStep으로 진행
    /// </summary>
    public void NextSubStep()
    {
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

        if (!string.IsNullOrEmpty(subStep.handTrackingFileName))
            HandleHandPoseTracking(subStep);
        else if (subStep.HasPatientAnimation())
            HandleAutoPlayAnimation(subStep);

        // ★ 모든 evaluator 설정 완료 후 각도 디스플레이 홀드 범위 강제 갱신
        angleDisplay?.ForceRefreshHoldRange();
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

        // ★ 회전 감지 축 오버라이드 (누운 환자용 — SetExtendedLimitModeFromNames가 Y축으로 설정한 뒤 덮어씀)
        if (currentConfig != null && currentConfig.overrideRotationAxis &&
            !string.IsNullOrEmpty(subStep.movementType) && subStep.movementType == "rotation")
        {
            chunaPathEvaluator.SetRotationDetectionAxis(currentConfig.lyingRotationAxis);
            if (showDebugLog)
                ChunaLogger.Log($"<color=magenta>[ScenarioManager] 회전 감지 축 오버라이드: {currentConfig.lyingRotationAxis}</color>");
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