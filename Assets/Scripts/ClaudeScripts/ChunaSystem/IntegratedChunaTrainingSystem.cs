using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 시나리오 시스템과 추나 트레이닝 시스템 통합
/// ScenarioManager의 계층 구조를 활용하면서 HandPosePlayer와 연동
/// </summary>
public class IntegratedChunaTrainingSystem : MonoBehaviour
{
    [Header("=== 핵심 컴포넌트 ===")]
    [Tooltip("CSV 또는 Inspector 프로토타입 데이터 사용")]
    [SerializeField] private ScenarioManager scenarioManager;
    [SerializeField] private HandPosePlayer handPosePlayer;
    [SerializeField] private ChunaEducationGuideSystem guideSystem;

    [Header("=== 데이터 소스 확인 ===")]
    [SerializeField] private bool showDataSourceInfo = true;

    [Header("=== 모드 설정 ===")]
    [SerializeField] private TrainingMode currentMode = TrainingMode.Education;
    [SerializeField] private bool autoDetectMode = true;  // Step 이름으로 자동 모드 전환

    public enum TrainingMode
    {
        Education,  // 교육 모드
        Evaluation  // 평가 모드
    }

    [Header("=== 모션 데이터 매핑 ===")]
    [Tooltip("Step 이름과 CSV 파일 매핑, 또는 customAction으로 매핑")]
    [SerializeField] private List<MotionMapping> motionMappings = new List<MotionMapping>();

    [System.Serializable]
    public class MotionMapping
    {
        [Header("매핑 기준 (하나만 설정)")]
        [Tooltip("Step 이름으로 매핑")]
        public string stepName;

        [Tooltip("또는 customAction으로 매핑")]
        public string customAction;

        [Tooltip("또는 Step번호 + SubStep번호 조합")]
        public int stepNo = -1;
        public int subStepNo = -1;

        [Header("모션 데이터")]
        [Tooltip("HandPose CSV 파일명")]
        public string motionDataFile;

        [Header("평가 설정")]
        public float requiredHoldTime = 2.0f;
        public float customThreshold = 0.7f;
        public bool evaluateBothHands = true;

        // 매핑 매칭 헬퍼 메서드
        public bool Matches(StepData step, SubStepData subStep)
        {
            // Step 이름으로 매칭
            if (!string.IsNullOrEmpty(stepName) && step.stepName == stepName)
                return true;

            // Step/SubStep 번호 조합으로 매칭
            if (stepNo >= 0 && step.stepNo == stepNo)
            {
                if (subStepNo >= 0 && subStep != null && subStep.subStepNo == subStepNo)
                    return true;
                if (subStepNo < 0) // SubStep 번호 무시하고 Step만으로 매칭
                    return true;
            }

            return false;
        }
    }

    [Header("=== 평가 설정 ===")]
    [SerializeField] private float defaultHoldTime = 2.0f;
    [SerializeField] private float defaultThreshold = 0.7f;

    // 이벤트 시스템
    private ScenarioEventSystem eventSystem;

    // 상태 관리
    private bool isProcessingStep = false;
    private Coroutine currentCoroutine;
    private float currentStepScore = 0f;
    private int totalSteps = 0;
    private int completedSteps = 0;
    private float totalScore = 0f;

    // 현재 처리 중인 데이터
    private StepData currentStep;
    private SubStepData currentSubStep;

    // 이벤트
    public UnityEvent<TrainingMode> OnModeChanged = new UnityEvent<TrainingMode>();
    public UnityEvent<float> OnStepScoreCalculated = new UnityEvent<float>();
    public UnityEvent<float> OnOverallScoreUpdated = new UnityEvent<float>();
    public UnityEvent OnTrainingCompleted = new UnityEvent();

    // 공개 프로퍼티
    public TrainingMode CurrentMode => currentMode;
    public bool IsProcessing => isProcessingStep;
    public float CurrentScore => currentStepScore;
    public float OverallScore => totalSteps > 0 ? totalScore / totalSteps : 0f;

    private void Awake()
    {
        InitializeComponents();
        SubscribeToEvents();
        SetupDefaultMappings();
    }

    private void InitializeComponents()
    {
        // 컴포넌트 자동 찾기
        if (scenarioManager == null)
            scenarioManager = FindObjectOfType<ScenarioManager>();

        if (handPosePlayer == null)
            handPosePlayer = FindObjectOfType<HandPosePlayer>();

        if (guideSystem == null)
            guideSystem = FindObjectOfType<ChunaEducationGuideSystem>();

        eventSystem = ScenarioEventSystem.Instance;

        // HandPosePlayer 설정
        if (handPosePlayer != null)
        {
            ConfigureHandPosePlayer();
        }
    }

    private void ConfigureHandPosePlayer()
    {
        // public 메서드로 설정
        handPosePlayer.SetThresholds(0.05f, 15f, defaultThreshold);
        handPosePlayer.SetReplayHandsVisible(true);
        handPosePlayer.SetReplayHandAlpha(0.5f);
        //handPosePlayer.SetDebugGizmos(true);
    }

    private void SubscribeToEvents()
    {
        if (eventSystem != null)
        {
            // ScenarioManager의 이벤트 구독
            eventSystem.OnStepChanged += OnStepChanged;
            eventSystem.OnSubStepStarted += OnSubStepStarted;
            eventSystem.OnSubStepCompleted += OnSubStepCompleted;
            eventSystem.OnScenarioCompleted += OnScenarioCompleted;
            eventSystem.OnPhaseChanged += OnPhaseChanged;
        }
    }

    private void SetupDefaultMappings()
    {
        // 기본 매핑이 비어있으면 추가
        if (motionMappings.Count == 0)
        {
            // Step 이름 기반 매핑
            motionMappings.Add(new MotionMapping
            {
                stepName = "평가",
                motionDataFile = "UpperTrapezius_Evaluation",
                requiredHoldTime = 2.0f,
                customThreshold = 0.7f
            });

            motionMappings.Add(new MotionMapping
            {
                stepName = "세판상박회인",
                motionDataFile = "UpperTrapezius_ROM_Test",
                requiredHoldTime = 3.0f,
                customThreshold = 0.65f
            });

            motionMappings.Add(new MotionMapping
            {
                stepName = "등척성운동",
                motionDataFile = "UpperTrapezius_Isometric",
                requiredHoldTime = 5.0f,
                customThreshold = 0.75f
            });

            // CustomAction 기반 매핑 예시
            motionMappings.Add(new MotionMapping
            {
                customAction = "HandPose_Evaluation",
                motionDataFile = "UpperTrapezius_Evaluation",
                requiredHoldTime = 2.0f,
                customThreshold = 0.7f
            });

            // Step/SubStep 번호 기반 매핑 예시
            motionMappings.Add(new MotionMapping
            {
                stepNo = 1,
                subStepNo = 0,
                motionDataFile = "UpperTrapezius_Position1",
                requiredHoldTime = 3.0f,
                customThreshold = 0.7f
            });
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 공개 메서드
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 시나리오 시작
    /// </summary>
    public void StartScenario()
    {
        if (scenarioManager == null)
        {
            Debug.LogError("[IntegratedSystem] ScenarioManager가 없습니다!");
            return;
        }

        if (handPosePlayer == null)
        {
            Debug.LogError("[IntegratedSystem] HandPosePlayer가 없습니다!");
            return;
        }

        // 데이터 소스 확인
        if (showDataSourceInfo)
        {
            CheckDataSource();
        }

        // 초기화
        totalSteps = 0;
        completedSteps = 0;
        totalScore = 0f;

        // 시나리오 시작
        scenarioManager.StartScenario();
    }

    /// <summary>
    /// 모드 설정
    /// </summary>
    public void SetMode(TrainingMode mode)
    {
        if (currentMode != mode)
        {
            currentMode = mode;
            OnModeChanged?.Invoke(mode);
            Debug.Log($"[IntegratedSystem] 모드 변경: {mode}");
        }
    }

    /// <summary>
    /// 다음 단계로
    /// </summary>
    public void NextStep()
    {
        if (scenarioManager != null && !isProcessingStep)
        {
            scenarioManager.NextSubStep();
        }
    }

    /// <summary>
    /// 현재 정확도 가져오기
    /// </summary>
    public float GetCurrentAccuracy()
    {
        if (handPosePlayer == null) return 0f;

        float leftSim = handPosePlayer.GetLeftSimilarity();
        float rightSim = handPosePlayer.GetRightSimilarity();

        return (leftSim + rightSim) / 2f;
    }

    /// <summary>
    /// 트레이닝 중지
    /// </summary>
    public void StopTraining()
    {
        isProcessingStep = false;

        if (currentCoroutine != null)
        {
            StopCoroutine(currentCoroutine);
            currentCoroutine = null;
        }

        if (handPosePlayer != null)
        {
            handPosePlayer.StopPlayback();
        }

        if (guideSystem != null)
        {
            guideSystem.StopGuiding();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 이벤트 핸들러
    // ═══════════════════════════════════════════════════════════════

    private void OnPhaseChanged(PhaseData phase)
    {
        Debug.Log($"[IntegratedSystem] Phase 변경: {phase.phaseName}");

        // Phase 이름으로 모드 자동 감지
        if (autoDetectMode)
        {
            if (phase.phaseName.Contains("평가"))
            {
                SetMode(TrainingMode.Evaluation);
            }
            else if (phase.phaseName.Contains("교육") || phase.phaseName.Contains("연습"))
            {
                SetMode(TrainingMode.Education);
            }
        }
    }

    private void OnStepChanged(StepData step)
    {
        Debug.Log($"[IntegratedSystem] Step 변경: {step.stepName}");

        // 현재 Step 저장
        currentStep = step;

        // Step 이름으로 모드 자동 감지
        if (autoDetectMode)
        {
            if (step.stepName.Contains("평가"))
            {
                SetMode(TrainingMode.Evaluation);
            }
            else if (step.stepName.Contains("교육") || step.stepName.Contains("학습"))
            {
                SetMode(TrainingMode.Education);
            }
        }

        totalSteps++;
    }

    private void OnSubStepStarted(SubStepData substep)
    {
        if (substep == null) return;

        Debug.Log($"[IntegratedSystem] SubStep 시작: Step={currentStep?.stepName}, SubStep #{substep.subStepNo}");

        // 현재 SubStep 저장
        currentSubStep = substep;

        // 진행 중인 코루틴 중지
        if (currentCoroutine != null)
        {
            StopCoroutine(currentCoroutine);
            currentCoroutine = null;
        }

        // 모션 매핑 찾기
        MotionMapping mapping = FindMotionMapping(currentStep, substep);

        if (mapping != null)
        {
            Debug.Log($"[IntegratedSystem] 모션 매핑 발견: {mapping.motionDataFile}");

            // CSV 파일 로드 및 재생
            handPosePlayer.LoadCSV(mapping.motionDataFile);
            handPosePlayer.StartPlayback();

            // 모드에 따른 처리
            if (currentMode == TrainingMode.Education)
            {
                currentCoroutine = StartCoroutine(EducationModeProcess(mapping));
            }
            else
            {
                currentCoroutine = StartCoroutine(EvaluationModeProcess(mapping));
            }
        }
        else
        {
            HandleNonMotionSubStep(substep);
        }

        // SubStep 타이머 처리
        if (substep.duration > 0)
        {
            StartCoroutine(SubStepTimer(substep.duration));
        }

        // 음성 가이드
        if (!string.IsNullOrEmpty(substep.voiceInstruction))
        {
            PlayVoiceGuidance(substep.voiceInstruction);
        }
    }

    private void OnSubStepCompleted(SubStepData substep)
    {
        Debug.Log($"[IntegratedSystem] SubStep 완료: SubStep #{substep.subStepNo}");

        // 진행 중인 처리 중지
        if (currentCoroutine != null)
        {
            StopCoroutine(currentCoroutine);
            currentCoroutine = null;
        }

        isProcessingStep = false;
    }

    private void OnScenarioCompleted(ScenarioData scenario)
    {
        Debug.Log($"[IntegratedSystem] 시나리오 완료: {scenario.scenarioName}");

        // 최종 점수 계산
        float finalScore = OverallScore;
        Debug.Log($"[IntegratedSystem] 최종 점수: {finalScore * 100:F1}%");

        OnTrainingCompleted?.Invoke();

        // 초기화
        StopTraining();
    }

    // ═══════════════════════════════════════════════════════════════
    // 처리 코루틴
    // ═══════════════════════════════════════════════════════════════

    private IEnumerator EducationModeProcess(MotionMapping mapping)
    {
        isProcessingStep = true;

        // 가이드 시스템 시작
        if (guideSystem != null)
        {
            guideSystem.StartGuiding(mapping.motionDataFile);
        }

        // 재생 완료 대기
        while (handPosePlayer.IsLeftHandPlaying() || handPosePlayer.IsRightHandPlaying())
        {
            // 실시간 정확도 체크 (교육용)
            float accuracy = GetCurrentAccuracy();

            if (Time.frameCount % 30 == 0)  // 0.5초마다
            {
                //Debug.Log($"[IntegratedSystem] 교육 중 정확도: {accuracy * 100:F1}%");
            }

            yield return null;
        }

        // 가이드 중지
        if (guideSystem != null)
        {
            guideSystem.StopGuiding();
        }

        isProcessingStep = false;
        completedSteps++;

        Debug.Log($"[IntegratedSystem] 교육 완료");
    }

    private IEnumerator EvaluationModeProcess(MotionMapping mapping)
    {
        isProcessingStep = true;

        float holdTime = 0f;
        float bestSimilarity = 0f;

        // 평가 시작
        while (holdTime < mapping.requiredHoldTime &&
               (handPosePlayer.IsLeftHandPlaying() || handPosePlayer.IsRightHandPlaying()))
        {
            // 실제 정확도 계산
            float leftSim = handPosePlayer.GetLeftSimilarity();
            float rightSim = handPosePlayer.GetRightSimilarity();
            float avgSim = (leftSim + rightSim) / 2f;

            if (avgSim > bestSimilarity)
            {
                bestSimilarity = avgSim;
            }

            // 임계값 체크
            bool meetsThreshold = mapping.evaluateBothHands ?
                (leftSim >= mapping.customThreshold && rightSim >= mapping.customThreshold) :
                (avgSim >= mapping.customThreshold);

            if (meetsThreshold)
            {
                holdTime += Time.deltaTime;
                ShowProgressFeedback(holdTime / mapping.requiredHoldTime);
            }
            else
            {
                holdTime = 0f;
                ShowProgressFeedback(0f);
            }

            yield return null;
        }

        // 평가 결과
        bool stepPassed = (holdTime >= mapping.requiredHoldTime);
        currentStepScore = bestSimilarity;
        totalScore += bestSimilarity;

        ShowEvaluationResult(stepPassed, bestSimilarity);

        // 이벤트 발생
        OnStepScoreCalculated?.Invoke(currentStepScore);
        OnOverallScoreUpdated?.Invoke(OverallScore);

        isProcessingStep = false;
        completedSteps++;

        Debug.Log($"[IntegratedSystem] 평가 완료 - {(stepPassed ? "통과" : "실패")}");
    }

    private void HandleNonMotionSubStep(SubStepData substep)
    {
        if (IsGuideSubStep())
        {
            Debug.Log($"[IntegratedSystem] 가이드 SubStep - UI만 표시");
        }
        else
        {
            Debug.Log($"[IntegratedSystem] SubStep #{substep.subStepNo}에 대한 모션 데이터가 없습니다");
        }

        completedSteps++;
    }

    // ═══════════════════════════════════════════════════════════════
    // 유틸리티 메서드
    // ═══════════════════════════════════════════════════════════════

    private MotionMapping FindMotionMapping(StepData step, SubStepData substep)
    {
        if (step == null) return null;

        foreach (var mapping in motionMappings)
        {
            if (mapping.Matches(step, substep))
            {
                return mapping;
            }
        }

        return null;
    }

    private bool IsGuideSubStep()
    {
        // 현재 Step이 가이드 Step인지 확인 (stepNo == 0)
        if (currentStep != null && currentStep.IsGuideStep())
        {
            return true;
        }

        // Step 이름에 가이드 키워드 체크
        if (currentStep != null)
        {
            string[] guideKeywords = { "안내", "설명", "가이드", "준비", "시작", "종료" };
            foreach (string keyword in guideKeywords)
            {
                if (currentStep.stepName.Contains(keyword))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private IEnumerator SubStepTimer(int duration)
    {
        Debug.Log($"[IntegratedSystem] {duration}초 타이머 시작");

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float progress = elapsed / duration;

            // 프로그레스 업데이트
            if (Time.frameCount % 30 == 0)
            {
                Debug.Log($"[IntegratedSystem] 타이머: {elapsed:F1}/{duration}초");
            }

            yield return null;
        }

        Debug.Log($"[IntegratedSystem] 타이머 완료");
    }

    private void ExecuteCustomAction(string action)
    {
        switch (action)
        {
            case "StartHaptic":
            case "EnableHaptic":
                Debug.Log("[IntegratedSystem] 햅틱 피드백 시작");
                break;

            case "StopHaptic":
            case "DisableHaptic":
                Debug.Log("[IntegratedSystem] 햅틱 피드백 중지");
                break;

            case "ShowArrow":
            case "HighlightArea":
                Debug.Log("[IntegratedSystem] 가이드 화살표 표시");
                break;

            case "CheckPosition":
                float accuracy = GetCurrentAccuracy();
                Debug.Log($"[IntegratedSystem] 손 위치 체크: {accuracy * 100:F1}%");
                break;

            default:
                Debug.LogWarning($"[IntegratedSystem] 알 수 없는 액션: {action}");
                break;
        }
    }

    private void ShowProgressFeedback(float progress)
    {
        // UI 업데이트 또는 이벤트 발생
        //Debug.Log($"[IntegratedSystem] 진행도: {progress * 100:F0}%");
    }

    private void ShowEvaluationResult(bool passed, float accuracy)
    {
        string stepInfo = currentStep != null ? $"Step: {currentStep.stepName}" : "";
        string substepInfo = currentSubStep != null ? $"SubStep #{currentSubStep.subStepNo}" : "";

        string result = passed ? "통과" : "재시도 필요";
        Debug.Log($"[IntegratedSystem] {stepInfo} {substepInfo} 평가 결과: {result} (정확도: {accuracy * 100:F1}%)");
    }

    private void PlayVoiceGuidance(string text)
    {
        Debug.Log($"[IntegratedSystem] 음성: {text}");
        // TTS 또는 오디오 재생
    }

    private void CheckDataSource()
    {
        if (scenarioManager == null) return;

        // 리플렉션으로 ScenarioManager 설정 확인
        var type = scenarioManager.GetType();
        var useCSVField = type.GetField("useCSVData",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (useCSVField != null)
        {
            bool useCSV = (bool)useCSVField.GetValue(scenarioManager);

            if (useCSV)
            {
                Debug.Log("[IntegratedSystem] 📁 CSV 파일에서 시나리오 로드");
            }
            else
            {
                Debug.Log("[IntegratedSystem] ✏️ Inspector 프로토타입 데이터 사용");

                // 프로토타입 데이터 정보
                var prototypeField = type.GetField("prototypeScenario",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (prototypeField != null)
                {
                    var prototypeData = prototypeField.GetValue(scenarioManager) as ScenarioData;
                    if (prototypeData != null)
                    {
                        Debug.Log($"[IntegratedSystem] 시나리오: {prototypeData.scenarioName}");
                        Debug.Log($"[IntegratedSystem] Phase 수: {prototypeData.phases.Count}");
                    }
                }
            }
        }
    }

    private void OnDestroy()
    {
        // 이벤트 구독 해제
        if (eventSystem != null)
        {
            eventSystem.OnStepChanged -= OnStepChanged;
            eventSystem.OnSubStepStarted -= OnSubStepStarted;
            eventSystem.OnSubStepCompleted -= OnSubStepCompleted;
            eventSystem.OnScenarioCompleted -= OnScenarioCompleted;
            eventSystem.OnPhaseChanged -= OnPhaseChanged;
        }

        StopTraining();
    }

    // ═══════════════════════════════════════════════════════════════
    // 디버그/테스트 메서드
    // ═══════════════════════════════════════════════════════════════

    [ContextMenu("Print Mapping Info")]
    private void PrintMappingInfo()
    {
        Debug.Log($"=== Motion 매핑 정보 ===");
        Debug.Log($"총 {motionMappings.Count}개 매핑");

        foreach (var mapping in motionMappings)
        {
            string mappingInfo = "";
            if (!string.IsNullOrEmpty(mapping.stepName))
                mappingInfo = $"Step: '{mapping.stepName}'";
            else if (!string.IsNullOrEmpty(mapping.customAction))
                mappingInfo = $"Action: '{mapping.customAction}'";
            else if (mapping.stepNo >= 0)
                mappingInfo = $"Step#{mapping.stepNo}/SubStep#{mapping.subStepNo}";

            Debug.Log($"  {mappingInfo} → '{mapping.motionDataFile}.csv'");
        }
    }

    [ContextMenu("Test Current Accuracy")]
    private void TestCurrentAccuracy()
    {
        float accuracy = GetCurrentAccuracy();
        Debug.Log($"[IntegratedSystem] 현재 정확도: {accuracy * 100:F1}%");
    }
}