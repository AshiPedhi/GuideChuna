using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 손 동작 평가 모드
/// </summary>
public enum EvaluationMode
{
    [Tooltip("기존 프레임 기반 평가 - 프레임 순서대로 따라가기")]
    FrameBased,

    [Tooltip("체크포인트 기반 평가 - 경로상의 체크포인트 통과")]
    CheckpointBased
}

/// <summary>
/// Inspector에서 직접 편집 가능한 시나리오 매니저
/// 모드 선택 정보 저장 기능 추가
/// </summary>
public class ScenarioManager : MonoBehaviour
{
    [Header("=== 프로토타입 시나리오 데이터 ===")]
    [Tooltip("프로토타입용 시나리오 (Inspector에서 직접 편집)")]
    [SerializeField] private ScenarioData prototypeScenario;

    [Header("=== CSV 로드 설정 ===")]
    [Tooltip("CSV 파일을 사용할지 여부")]
    [SerializeField] private bool useCSVData = false;

    [Tooltip("CSV 파일 이름 (Resources/Scenarios/ 폴더)")]
    [SerializeField] private string csvFileName = "ScenarioData";

    [Header("=== 손 동작 평가 모드 ===")]
    [Tooltip("평가 모드 선택: FrameBased(기존 방식) / CheckpointBased(체크포인트 기반)")]
    [SerializeField] private EvaluationMode evaluationMode = EvaluationMode.CheckpointBased;

    [Header("=== HandPose 시스템 (기존 프레임 기반) ===")]
    [Tooltip("HandPoseTrainingController (자동 찾기)")]
    [SerializeField] private HandPoseTrainingController trainingController;

    [Tooltip("HandPoseTrainingControllerBridge (자동 찾기/생성)")]
    [SerializeField] private HandPoseTrainingControllerBridge trainingControllerBridge;

    [Header("=== 체크포인트 기반 평가 시스템 (새로운 방식) ===")]
    [Tooltip("ChunaPathEvaluator (자동 찾기)")]
    [SerializeField] private ChunaPathEvaluator chunaPathEvaluator;

    [Tooltip("ChunaPathEvaluatorBridge (자동 찾기/생성)")]
    [SerializeField] private ChunaPathEvaluatorBridge chunaPathEvaluatorBridge;

    [Tooltip("ScenarioConditionManager (자동 찾기)")]
    [SerializeField] private ScenarioConditionManager conditionManager;

    [Header("=== UI 자동 배치 ===")]
    [Tooltip("ScenarioUIPositioner (자동 찾기)")]
    [SerializeField] private ScenarioUIPositioner uiPositioner;

    [Header("=== 퀴즈 패널 ===")]
    [Tooltip("퀴즈 패널 (학습 완료 후 표시)")]
    [SerializeField] private QuizPanel quizPanel;

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

    // 프로퍼티
    public ScenarioData CurrentScenario => currentScenario;
    public PhaseData CurrentPhase => currentPhase;
    public StepData CurrentStep => currentStep;
    public SubStepData CurrentSubStep => currentSubStep;
    public bool IsLastSubStep => currentSubStepIndex >= currentStep.subSteps.Count - 1;
    public bool IsLastStep => currentStepIndex >= currentPhase.steps.Count - 1;
    public bool IsLastPhase => currentPhaseIndex >= currentScenario.phases.Count - 1;

    // 모드 정보 프로퍼티
    public string SelectedMode => selectedMode;
    public string SelectedDifficulty => selectedDifficulty;

    private void Awake()
    {
        eventSystem = ScenarioEventSystem.Instance;

        // ✅ ConditionManager 찾기
        if (conditionManager == null)
        {
            conditionManager = FindObjectOfType<ScenarioConditionManager>();
        }

        // ✅ UI Positioner 찾기
        if (uiPositioner == null)
        {
            uiPositioner = FindObjectOfType<ScenarioUIPositioner>();
            if (uiPositioner != null)
            {
                Debug.Log("[ScenarioManager] ✅ ScenarioUIPositioner 자동 찾기 성공");
            }
        }

        // ✅ QuizPanel 찾기
        if (quizPanel == null)
        {
            quizPanel = FindObjectOfType<QuizPanel>();
            if (quizPanel != null)
            {
                Debug.Log("[ScenarioManager] ✅ QuizPanel 자동 찾기 성공");
            }
        }

        // ✅ HandPose 시스템 초기화
        InitializeHandPoseSystem();
    }

    /// <summary>
    /// HandPose 시스템 초기화 (프레임 기반 + 체크포인트 기반)
    /// </summary>
    private void InitializeHandPoseSystem()
    {
        Debug.Log("<color=cyan>[ScenarioManager] 손 동작 평가 시스템 초기화 중...</color>");
        Debug.Log($"<color=cyan>[ScenarioManager] 현재 평가 모드: {evaluationMode}</color>");

        // ========== 프레임 기반 시스템 초기화 ==========
        if (evaluationMode == EvaluationMode.FrameBased)
        {
            // HandPoseTrainingController 찾기
            if (trainingController == null)
            {
                trainingController = FindObjectOfType<HandPoseTrainingController>();
            }

            if (trainingController == null)
            {
                Debug.LogError("[ScenarioManager] HandPoseTrainingController를 찾을 수 없습니다! 씬에 추가해주세요.");
                return;
            }

            // HandPoseTrainingControllerBridge 찾기 또는 생성
            trainingControllerBridge = trainingController.GetComponent<HandPoseTrainingControllerBridge>();

            if (trainingControllerBridge == null)
            {
                Debug.Log("[ScenarioManager] HandPoseTrainingControllerBridge가 없어서 자동으로 추가합니다.");
                trainingControllerBridge = trainingController.gameObject.AddComponent<HandPoseTrainingControllerBridge>();
            }

            Debug.Log("<color=green>[ScenarioManager] ✓ 프레임 기반 평가 시스템 초기화 완료!</color>");
        }

        // ========== 체크포인트 기반 시스템 초기화 ==========
        if (evaluationMode == EvaluationMode.CheckpointBased)
        {
            // ChunaPathEvaluator 찾기
            if (chunaPathEvaluator == null)
            {
                chunaPathEvaluator = FindObjectOfType<ChunaPathEvaluator>();
            }

            if (chunaPathEvaluator == null)
            {
                Debug.LogWarning("[ScenarioManager] ChunaPathEvaluator를 찾을 수 없습니다. 체크포인트 기반 평가가 비활성화됩니다.");
                Debug.LogWarning("[ScenarioManager] 프레임 기반 모드로 전환합니다.");
                evaluationMode = EvaluationMode.FrameBased;
                InitializeHandPoseSystem(); // 재귀 호출로 프레임 기반 초기화
                return;
            }

            // ChunaPathEvaluatorBridge 찾기 또는 생성
            chunaPathEvaluatorBridge = chunaPathEvaluator.GetComponent<ChunaPathEvaluatorBridge>();

            if (chunaPathEvaluatorBridge == null)
            {
                Debug.Log("[ScenarioManager] ChunaPathEvaluatorBridge가 없어서 자동으로 추가합니다.");
                chunaPathEvaluatorBridge = chunaPathEvaluator.gameObject.AddComponent<ChunaPathEvaluatorBridge>();
            }

            Debug.Log("<color=green>[ScenarioManager] ✓ 체크포인트 기반 평가 시스템 초기화 완료!</color>");
        }
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
    }

    /// <summary>
    /// 모드와 난이도 정보 설정
    /// </summary>
    public void SetModeInfo(string mode, string difficulty)
    {
        selectedMode = mode;
        selectedDifficulty = difficulty;

        Debug.Log($"[ScenarioManager] 모드 설정: {mode}, 난이도: {difficulty}");
    }

    /// <summary>
    /// 시나리오 시작 (프로토타입 또는 CSV)
    /// </summary>
    public void StartScenario()
    {
        Debug.Log("<color=cyan>═══════════════════════════════════</color>");
        Debug.Log("<color=cyan>[ScenarioManager] StartScenario() 호출됨</color>");
        Debug.Log($"<color=yellow>[ScenarioManager] useCSVData: {useCSVData}</color>");
        Debug.Log($"<color=yellow>[ScenarioManager] csvFileName: {csvFileName}</color>");

        if (useCSVData)
        {
            Debug.Log("<color=yellow>[ScenarioManager] CSV 데이터 로드 시도 중...</color>");
            LoadFromCSV();
        }
        else
        {
            if (prototypeScenario == null)
            {
                LogError("프로토타입 시나리오가 설정되지 않았습니다!");
                return;
            }

            Debug.Log("<color=yellow>[ScenarioManager] 프로토타입 시나리오 시작 중...</color>");
            StartScenario(prototypeScenario);
        }
    }

    /// <summary>
    /// 특정 시나리오 시작
    /// </summary>
    public void StartScenario(ScenarioData scenario)
    {
        Debug.Log("<color=yellow>[ScenarioManager] StartScenario(ScenarioData) 호출됨</color>");

        if (scenario == null || scenario.phases.Count == 0)
        {
            LogError("유효하지 않은 시나리오 데이터입니다!");
            return;
        }

        Debug.Log($"<color=green>[ScenarioManager] ✓ 시나리오 데이터 유효성 확인 완료</color>");
        Debug.Log($"<color=green>  - 시나리오: {scenario.scenarioName}</color>");
        Debug.Log($"<color=green>  - Phase 수: {scenario.phases.Count}</color>");

        currentScenario = scenario;
        currentPhaseIndex = 0;
        currentStepIndex = 0;
        currentSubStepIndex = 0;

        currentPhase = currentScenario.phases[0];
        currentStep = currentPhase.steps[0];
        currentSubStep = currentStep.subSteps[0];

        Debug.Log($"<color=yellow>[ScenarioManager] 초기 상태 설정 완료</color>");
        Debug.Log($"<color=yellow>  - Phase: {currentPhase.phaseName}</color>");
        Debug.Log($"<color=yellow>  - Step: {currentStep.stepName}</color>");
        Debug.Log($"<color=yellow>  - SubStep: {currentSubStep.subStepNo}</color>");

        // UI 자동 배치
        if (uiPositioner != null)
        {
            Debug.Log("<color=magenta>[ScenarioManager] UI 자동 배치 시작...</color>");
            uiPositioner.PositionUIElements();
            Debug.Log("<color=magenta>[ScenarioManager] ✓ UI 자동 배치 완료</color>");
        }
        else
        {
            Debug.LogWarning("<color=orange>[ScenarioManager] ScenarioUIPositioner가 없어 UI 자동 배치를 건너뜁니다.</color>");
        }

        // 이벤트 발생
        Debug.Log("<color=cyan>[ScenarioManager] 이벤트 시스템 호출 중...</color>");
        eventSystem.ScenarioStarted(currentScenario);
        eventSystem.PhaseChanged(currentPhase);
        eventSystem.StepChanged(currentStep);
        eventSystem.SubStepStarted(currentSubStep);
        Debug.Log("<color=cyan>[ScenarioManager] ✓ 이벤트 시스템 호출 완료</color>");

        UpdateUI();
        UpdateProgress();

        Log($"시나리오 시작: {currentScenario.scenarioName} (모드: {selectedMode}, 난이도: {selectedDifficulty})");
        Debug.Log("<color=green>[ScenarioManager] ✓✓✓ 시나리오 시작 완료! ✓✓✓</color>");
        Debug.Log("<color=cyan>═══════════════════════════════════</color>");
    }

    /// <summary>
    /// CSV에서 로드
    /// </summary>
    private void LoadFromCSV()
    {
        Debug.Log("<color=yellow>[ScenarioManager] CSV 로더 확인 중...</color>");

        ScenarioCSVLoader loader = GetComponent<ScenarioCSVLoader>();
        if (loader == null)
        {
            Debug.Log("<color=yellow>[ScenarioManager] CSV 로더가 없어서 추가 중...</color>");
            loader = gameObject.AddComponent<ScenarioCSVLoader>();
        }

        Debug.Log($"<color=yellow>[ScenarioManager] CSV 파일 로드 시도: Resources/Scenarios/{csvFileName}.csv</color>");
        ScenarioCollection collection = loader.LoadScenarios(csvFileName);

        if (collection == null || collection.scenarios.Count == 0)
        {
            LogError("CSV 로드 실패!");
            Debug.LogError($"<color=red>[ScenarioManager] ❌ CSV 파일 경로 확인: Assets/Resources/Scenarios/{csvFileName}.csv</color>");
            Debug.LogError($"<color=red>[ScenarioManager] ❌ 파일이 존재하는지, 파일명이 정확한지 확인하세요!</color>");
            return;
        }

        Debug.Log($"<color=green>[ScenarioManager] ✓ CSV 로드 성공! {collection.scenarios.Count}개 시나리오 발견</color>");
        StartScenario(collection.scenarios[0]);
    }

    /// <summary>
    /// 다음 SubStep으로 진행
    /// </summary>
    public void NextSubStep()
    {
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
        eventSystem.ScenarioCompleted(currentScenario);
        Log($"시나리오 완료: {currentScenario.scenarioName}");

        // 퀴즈 패널 표시
        ShowQuizPanel();
    }

    /// <summary>
    /// 퀴즈 패널 표시 (학습 완료 후)
    /// </summary>
    private void ShowQuizPanel()
    {
        if (quizPanel != null)
        {
            Debug.Log("[ScenarioManager] 퀴즈 패널 표시");
            quizPanel.ShowQuizPanel();
        }
        else
        {
            Debug.LogWarning("[ScenarioManager] QuizPanel이 없어 퀴즈를 건너뜁니다.");
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

    // === 디버그 헬퍼 ===


    // ========== HandPose 자동 처리 (통합된 ActionHandler 기능) ==========

    /// <summary>
    /// SubStep 시작 시 HandPose 자동 처리
    /// ScenarioActionHandler의 기능을 통합
    /// </summary>
    private void OnSubStepStartedForHandPose(SubStepData subStep)
    {
        // ✅ CSV의 handTrackingFileName 자동 처리
        if (!string.IsNullOrEmpty(subStep.handTrackingFileName))
        {
            HandleHandPoseTracking(subStep);
        }
    }

    /// <summary>
    /// HandPose 트래킹 자동 처리 (CSV 기반)
    /// 평가 모드에 따라 프레임 기반 또는 체크포인트 기반으로 동작
    /// </summary>
    private void HandleHandPoseTracking(SubStepData subStep)
    {
        if (conditionManager == null)
        {
            Debug.LogError("[ScenarioManager] ScenarioConditionManager를 찾을 수 없습니다!");
            return;
        }

        string phaseName = currentPhase.phaseName;
        string stepName = currentStep.stepName;
        int subStepNo = subStep.subStepNo;

        // ========== 체크포인트 기반 평가 ==========
        if (evaluationMode == EvaluationMode.CheckpointBased)
        {
            HandleCheckpointBasedTracking(subStep, phaseName, stepName, subStepNo);
        }
        // ========== 프레임 기반 평가 (기존 방식) ==========
        else
        {
            HandleFrameBasedTracking(subStep, phaseName, stepName, subStepNo);
        }
    }

    /// <summary>
    /// 체크포인트 기반 손 동작 평가 처리
    /// 각 SubStep마다 CSV를 로드하고 체크포인트를 생성
    /// </summary>
    private void HandleCheckpointBasedTracking(SubStepData subStep, string phaseName, string stepName, int subStepNo)
    {
        if (chunaPathEvaluator == null || chunaPathEvaluatorBridge == null)
        {
            Debug.LogError("[ScenarioManager] ChunaPathEvaluator 또는 Bridge를 찾을 수 없습니다!");
            Debug.LogWarning("[ScenarioManager] 프레임 기반 모드로 전환합니다.");
            HandleFrameBasedTracking(subStep, phaseName, stepName, subStepNo);
            return;
        }

        Debug.Log($"<color=cyan>[ScenarioManager] ========== 체크포인트 기반 평가 시작 ==========</color>");
        Debug.Log($"<color=cyan>[ScenarioManager] CSV 파일: {subStep.handTrackingFileName}</color>");

        // 1. CSV 로드 및 체크포인트 생성 + 평가 시작
        chunaPathEvaluatorBridge.LoadFromCSV(subStep.handTrackingFileName);

        // 2. 환자 애니메이션 설정 (시나리오 데이터에서)
        if (subStep.HasPatientAnimation())
        {
            chunaPathEvaluator.SetPatientAnimationFromSubStep(subStep);
            Debug.Log($"<color=magenta>[ScenarioManager] 환자 애니메이션 설정: {subStep.patientAnimationClip}</color>");
        }

        // 3. CheckpointPoseCondition 생성
        CheckpointPoseCondition condition = new CheckpointPoseCondition(
            chunaPathEvaluatorBridge,
            subStep.handTrackingFileName,
            conditionManager
        );

        // 4. ScenarioConditionManager에 조건 등록
        conditionManager.RegisterCondition(phaseName, stepName, subStepNo, condition);

        Debug.Log($"<color=green>[ScenarioManager] ✓ CheckpointPoseCondition 등록 완료!</color>");
        Debug.Log($"<color=green>  - Phase: {phaseName}</color>");
        Debug.Log($"<color=green>  - Step: {stepName}</color>");
        Debug.Log($"<color=green>  - SubStep: {subStepNo}</color>");
        Debug.Log($"<color=green>  - CSV: {subStep.handTrackingFileName}</color>");
        Debug.Log($"<color=green>  - 체크포인트 수: {chunaPathEvaluator.TotalCheckpoints}</color>");
    }

    /// <summary>
    /// 프레임 기반 손 동작 평가 처리 (기존 방식)
    /// </summary>
    private void HandleFrameBasedTracking(SubStepData subStep, string phaseName, string stepName, int subStepNo)
    {
        if (trainingController == null || trainingControllerBridge == null)
        {
            Debug.LogError("[ScenarioManager] HandPoseTrainingController 또는 Bridge를 찾을 수 없습니다!");
            return;
        }

        Debug.Log($"<color=yellow>[ScenarioManager] 프레임 기반 HandPose 트래킹 시작: {subStep.handTrackingFileName}</color>");

        // 1. CSV 로드 및 훈련 시작
        trainingControllerBridge.LoadFromCSV(subStep.handTrackingFileName);

        // 2. HandPoseCondition 생성
        HandPoseCondition condition = new HandPoseCondition(
            trainingControllerBridge,
            subStep.handTrackingFileName,
            conditionManager
        );

        // 3. ScenarioConditionManager에 조건 등록
        conditionManager.RegisterCondition(phaseName, stepName, subStepNo, condition);

        Debug.Log($"<color=green>[ScenarioManager] ✓ HandPoseCondition 등록 완료!</color>");
        Debug.Log($"<color=green>  - Phase: {phaseName}</color>");
        Debug.Log($"<color=green>  - Step: {stepName}</color>");
        Debug.Log($"<color=green>  - SubStep: {subStepNo}</color>");
        Debug.Log($"<color=green>  - CSV: {subStep.handTrackingFileName}</color>");
    }

    private void Log(string message)
    {
        if (showDebugLog)
        {
            Debug.Log($"[ScenarioManager] {message}");
        }
    }

    private void LogError(string message)
    {
        Debug.LogError($"[ScenarioManager] {message}");
    }

    private void OnDestroy()
    {
        eventSystem?.Clear();
    }

    // === Inspector 편집 도우미 ===

    [ContextMenu("📝 빈 시나리오 생성")]
    private void CreateEmptyScenario()
    {
        prototypeScenario = new ScenarioData
        {
            scenarioNo = 1,
            scenarioName = "새 시나리오",
            phases = new List<PhaseData>()
        };

        Debug.Log("빈 시나리오가 생성되었습니다. Inspector에서 편집하세요.");
    }

    [ContextMenu("➕ Phase 추가")]
    private void AddPhase()
    {
        if (prototypeScenario == null)
        {
            Debug.LogError("먼저 시나리오를 생성하세요!");
            return;
        }

        prototypeScenario.phases.Add(new PhaseData
        {
            phaseName = "새 Phase",
            steps = new List<StepData>()
        });

        Debug.Log("Phase가 추가되었습니다.");
    }

    [ContextMenu("📊 시나리오 정보 출력")]
    private void PrintScenarioInfo()
    {
        if (prototypeScenario == null)
        {
            Debug.LogError("시나리오가 없습니다!");
            return;
        }

        Debug.Log($"=== {prototypeScenario.scenarioName} ===");
        Debug.Log($"Phase 수: {prototypeScenario.phases.Count}");

        foreach (var phase in prototypeScenario.phases)
        {
            Debug.Log($"  - {phase.phaseName}: {phase.steps.Count} Steps");

            foreach (var step in phase.steps)
            {
                Debug.Log($"    - {step.stepName}: {step.subSteps.Count} SubSteps");
            }
        }
    }
}