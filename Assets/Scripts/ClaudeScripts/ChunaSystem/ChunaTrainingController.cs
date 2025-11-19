using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// 추나 통합 트레이닝 시스템 - 완전 보완 버전
/// 모든 보호 수준 문제 해결, 누락된 메서드 구현
/// </summary>
public class ChunaTrainingController : MonoBehaviour
{
    [Header("=== 모드 설정 ===")]
    [SerializeField] private TrainingMode currentMode = TrainingMode.Education;
    [SerializeField] private bool canSwitchModeDuringSession = true;

    public enum TrainingMode
    {
        Education,  // 교육 모드 (가이드 따라하기)
        Evaluation  // 평가 모드 (동작 평가)
    }

    [Header("=== 핵심 컴포넌트 ===")]
    [SerializeField] private HandPosePlayer handPosePlayer;
    [SerializeField] private ChunaEducationGuideSystem guideSystem;
    [SerializeField] private GameObject educationGuideHands;
    [SerializeField] private Material guideHandMaterial;
    [SerializeField] private DotTimelineController dotTimeline;

    [Header("=== 교육 모드 설정 ===")]
    [SerializeField] private float educationPlaybackSpeed = 0.5f;
    [SerializeField] private bool showGhostHands = true;
    [SerializeField] private bool showTrajectoryPath = true;
    [SerializeField] private float ghostHandAlpha = 0.4f;
    [SerializeField] private Color correctPositionColor = Color.green;
    [SerializeField] private Color incorrectPositionColor = Color.red;

    [Header("=== 교육 모드 피드백 ===")]
    [SerializeField] private GameObject directionArrows;
    [SerializeField] private LineRenderer leftHandPath;
    [SerializeField] private LineRenderer rightHandPath;
    [SerializeField] private float pathShowDuration = 2.0f;

    [Header("=== 평가 모드 설정 ===")]
    [SerializeField] private float evaluationHoldTime = 1.0f;
    [SerializeField] private float positionThreshold = 0.05f;
    [SerializeField] private float rotationThreshold = 15f;
    [SerializeField] private float similarityThreshold = 0.7f;

    [Header("=== 스텝 데이터 ===")]
    public List<TrainingStepData> trainingSteps = new List<TrainingStepData>();

    [Header("=== UI 참조 ===")]
    [SerializeField] private GameObject modeSelectionPanel;
    [SerializeField] private Button educationModeButton;
    [SerializeField] private Button evaluationModeButton;
    [SerializeField] private TextMeshProUGUI modeIndicatorText;
    [SerializeField] private TextMeshProUGUI instructionText;
    [SerializeField] private TextMeshProUGUI progressText;
    [SerializeField] private GameObject educationControlPanel;
    [SerializeField] private GameObject evaluationScorePanel;
    [SerializeField] private Slider speedSlider;
    [SerializeField] private TextMeshProUGUI speedText;

    // 상태 변수
    private int currentStepIndex = 0;
    private bool isRunning = false;
    private float currentHoldTime = 0f;
    private List<Vector3> recordedPath = new List<Vector3>();

    // 교육 모드 전용
    private GameObject activeLeftGuide;
    private GameObject activeRightGuide;
    private Coroutine educationCoroutine;
    private bool isShowingGuide = false;

    // 평가 모드 전용
    private float evaluationScore = 0f;
    private int passedSteps = 0;
    private float totalAccuracy = 0f;

    // 이벤트
    public UnityEvent<TrainingMode> OnModeChanged = new UnityEvent<TrainingMode>();
    public UnityEvent<int> OnStepStarted = new UnityEvent<int>();
    public UnityEvent<int> OnStepCompleted = new UnityEvent<int>();
    public UnityEvent OnTrainingCompleted = new UnityEvent();
    public UnityEvent OnPlaybackComplete = new UnityEvent();

    // 공개 프로퍼티
    public TrainingMode CurrentMode => currentMode;
    public int CurrentStepIndex => currentStepIndex;
    public bool IsRunning => isRunning;
    public float CurrentEvaluationScore => evaluationScore;

    [System.Serializable]
    public class TrainingStepData
    {
        public string stepName = "Step";
        public string description = "동작 설명";
        public string motionDataFile = "HandPose_Step1";

        [Header("교육 모드 설정")]
        public float educationSpeed = 1.0f;
        public bool waitForUserReady = true;
        public string voiceGuidance = "";

        [Header("평가 모드 설정")]
        public float requiredHoldTime = 1.0f;
        public float customThreshold = 0.7f;
        public bool evaluateBothHands = true;
    }

    private void Awake()
    {
        InitializeComponents();
        ValidateComponents();
        SetupUI();
    }

    private void InitializeComponents()
    {
        // 컴포넌트 자동 찾기
        if (handPosePlayer == null)
            handPosePlayer = FindObjectOfType<HandPosePlayer>();

        if (guideSystem == null)
            guideSystem = FindObjectOfType<ChunaEducationGuideSystem>();

        if (dotTimeline == null)
            dotTimeline = FindObjectOfType<DotTimelineController>();
    }

    private void ValidateComponents()
    {
        if (handPosePlayer == null)
        {
            Debug.LogError("[ChunaTraining] HandPosePlayer가 없습니다!");
        }
        else
        {
            // HandPosePlayer 기본 설정
            ConfigureHandPosePlayer();
        }

        if (guideSystem == null)
        {
            Debug.LogWarning("[ChunaTraining] ChunaEducationGuideSystem이 없습니다. 가이드 기능이 제한됩니다.");
        }
    }

    private void ConfigureHandPosePlayer()
    {
        if (handPosePlayer == null) return;

        // public 메서드를 통한 설정
        handPosePlayer.SetThresholds(positionThreshold, rotationThreshold, similarityThreshold);
        handPosePlayer.SetReplayHandsVisible(true);
        handPosePlayer.SetReplayHandAlpha(0.5f);
        handPosePlayer.SetDebugGizmos(true);
        handPosePlayer.SetPositionLines(true);
    }

    private void SetupUI()
    {
        // 모드 선택 버튼
        if (educationModeButton != null)
            educationModeButton.onClick.AddListener(() => SelectMode(TrainingMode.Education));

        if (evaluationModeButton != null)
            evaluationModeButton.onClick.AddListener(() => SelectMode(TrainingMode.Evaluation));

        // 속도 조절 슬라이더
        if (speedSlider != null)
        {
            speedSlider.onValueChanged.AddListener(OnSpeedChanged);
            speedSlider.value = educationPlaybackSpeed;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 공개 메서드 (Public Methods)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// 트레이닝 시작
    /// </summary>
    public void StartTraining()
    {
        if (handPosePlayer == null)
        {
            Debug.LogError("[ChunaTraining] HandPosePlayer가 설정되지 않아 시작할 수 없습니다!");
            return;
        }

        if (modeSelectionPanel != null)
        {
            modeSelectionPanel.SetActive(true);
            ShowModeSelection();
        }
        else
        {
            StartWithMode(currentMode);
        }
    }

    /// <summary>
    /// 모드 선택
    /// </summary>
    public void SelectMode(TrainingMode mode)
    {
        currentMode = mode;
        OnModeChanged?.Invoke(mode);

        if (modeSelectionPanel != null)
            modeSelectionPanel.SetActive(false);

        Time.timeScale = 1f;

        StartWithMode(mode);
    }

    /// <summary>
    /// 일시정지
    /// </summary>
    public void PausePlayback()
    {
        if (handPosePlayer != null)
        {
            handPosePlayer.PausePlayback();  // HandPosePlayer의 실제 메서드
            Time.timeScale = 0f;
        }
    }

    /// <summary>
    /// 재개
    /// </summary>
    public void ResumePlayback()
    {
        if (handPosePlayer != null)
        {
            handPosePlayer.ResumePlayback();
            Time.timeScale = currentMode == TrainingMode.Education ? educationPlaybackSpeed : 1f;
        }
    }

    /// <summary>
    /// 정지
    /// </summary>
    public void StopTraining()
    {
        isRunning = false;

        if (educationCoroutine != null)
        {
            StopCoroutine(educationCoroutine);
            educationCoroutine = null;
        }

        if (handPosePlayer != null)
        {
            handPosePlayer.StopPlayback();
        }

        if (guideSystem != null)
        {
            guideSystem.StopGuiding();
        }

        Time.timeScale = 1f;
    }

    /// <summary>
    /// 모드 전환
    /// </summary>
    public void SwitchMode()
    {
        if (!canSwitchModeDuringSession) return;

        StopTraining();

        currentMode = currentMode == TrainingMode.Education ?
                     TrainingMode.Evaluation : TrainingMode.Education;

        StartWithMode(currentMode);
    }

    /// <summary>
    /// 현재 정확도 가져오기 (커스텀 구현)
    /// </summary>
    public float GetCurrentAccuracy()
    {
        if (handPosePlayer == null) return 0f;

        float leftSim = handPosePlayer.GetLeftSimilarity();
        float rightSim = handPosePlayer.GetRightSimilarity();

        return (leftSim + rightSim) / 2f;
    }

    /// <summary>
    /// 현재 포즈 비교 (커스텀 구현)
    /// </summary>
    public float CompareCurrentPose(out bool passed)
    {
        float accuracy = GetCurrentAccuracy();
        passed = accuracy >= similarityThreshold;
        return accuracy;
    }

    /// <summary>
    /// 왼손 정확도 가져오기
    /// </summary>
    public float GetLeftHandSimilarity()
    {
        return handPosePlayer != null ? handPosePlayer.GetLeftSimilarity() : 0f;
    }

    /// <summary>
    /// 오른손 정확도 가져오기
    /// </summary>
    public float GetRightHandSimilarity()
    {
        return handPosePlayer != null ? handPosePlayer.GetRightSimilarity() : 0f;
    }

    /// <summary>
    /// 왼손 위치 오차 가져오기
    /// </summary>
    public float GetLeftHandPositionError()
    {
        return handPosePlayer != null ? handPosePlayer.GetLeftHandPositionError() : 0f;
    }

    /// <summary>
    /// 오른손 위치 오차 가져오기
    /// </summary>
    public float GetRightHandPositionError()
    {
        return handPosePlayer != null ? handPosePlayer.GetRightHandPositionError() : 0f;
    }

    // ═══════════════════════════════════════════════════════════════
    // 내부 메서드 (Private Methods)
    // ═══════════════════════════════════════════════════════════════

    private void ShowModeSelection()
    {
        Time.timeScale = 0f;

        if (modeIndicatorText != null)
        {
            modeIndicatorText.text = "트레이닝 모드를 선택하세요";
        }
    }

    private void StartWithMode(TrainingMode mode)
    {
        Debug.Log($"[ChunaTraining] 트레이닝 시작: {mode} 모드");

        isRunning = true;
        currentStepIndex = 0;
        evaluationScore = 0f;
        passedSteps = 0;
        totalAccuracy = 0f;

        UpdateModeUI(mode);

        // 도트 타임라인 초기화
        if (dotTimeline != null && trainingSteps.Count > 0)
        {
            dotTimeline.SetTotalSteps(trainingSteps.Count);
            dotTimeline.SetCurrentStep(0);
        }

        if (mode == TrainingMode.Education)
        {
            StartEducationMode();
        }
        else
        {
            StartEvaluationMode();
        }
    }

    private void StartEducationMode()
    {
        Debug.Log("[ChunaTraining] 교육 모드 시작 - 가이드를 따라하세요!");

        // UI 설정
        if (educationControlPanel != null)
            educationControlPanel.SetActive(true);
        if (evaluationScorePanel != null)
            evaluationScorePanel.SetActive(false);

        // 가이드 손 생성
        CreateGuideHands();

        // 가이드 시스템 시작
        if (guideSystem != null && trainingSteps.Count > 0)
        {
            guideSystem.StartGuiding(trainingSteps[0].motionDataFile);
        }

        // 교육 루프 시작
        if (educationCoroutine != null)
            StopCoroutine(educationCoroutine);
        educationCoroutine = StartCoroutine(EducationModeLoop());
    }

    private void StartEvaluationMode()
    {
        Debug.Log("[ChunaTraining] 평가 모드 시작 - 정확하게 따라하세요!");

        // UI 설정
        if (educationControlPanel != null)
            educationControlPanel.SetActive(false);
        if (evaluationScorePanel != null)
            evaluationScorePanel.SetActive(true);

        // 가이드 시스템 정지
        if (guideSystem != null)
        {
            guideSystem.StopGuiding();
        }

        // 평가 루프 시작
        StartCoroutine(EvaluationModeLoop());
    }

    /// <summary>
    /// 교육 모드 루프
    /// </summary>
    private IEnumerator EducationModeLoop()
    {
        while (isRunning && currentStepIndex < trainingSteps.Count)
        {
            var step = trainingSteps[currentStepIndex];

            // 스텝 시작 이벤트
            OnStepStarted?.Invoke(currentStepIndex);
            UpdateStepUI(step);

            // 음성 안내
            if (!string.IsNullOrEmpty(step.voiceGuidance))
            {
                PlayVoiceGuidance(step.voiceGuidance);
            }

            // 사용자 준비 대기
            if (step.waitForUserReady)
            {
                ShowInstruction("준비되면 시작합니다...");
                yield return new WaitForSeconds(2f);
            }

            // 재생 속도 설정
            Time.timeScale = step.educationSpeed * educationPlaybackSpeed;

            // 가이드 표시
            if (showGhostHands && guideSystem != null)
            {
                guideSystem.StartGuiding(step.motionDataFile);
            }

            // 모션 재생
            Debug.Log($"[ChunaTraining] '{step.motionDataFile}' 재생 시작");
            handPosePlayer.StartPlaybackFromCSV(step.motionDataFile);

            // 경로 표시
            if (showTrajectoryPath)
            {
                ShowMotionPath();
            }

            // 재생 완료 대기
            yield return StartCoroutine(WaitForMotionComplete());

            ShowInstruction($"Step {currentStepIndex + 1} 완료!");

            // 스텝 완료 이벤트
            OnStepCompleted?.Invoke(currentStepIndex);
            currentStepIndex++;

            // 도트 타임라인 업데이트
            if (dotTimeline != null)
            {
                dotTimeline.SetCurrentStep(currentStepIndex);
            }

            yield return new WaitForSeconds(1f);
        }

        CompleteTraining();
    }

    /// <summary>
    /// 평가 모드 루프
    /// </summary>
    private IEnumerator EvaluationModeLoop()
    {
        while (isRunning && currentStepIndex < trainingSteps.Count)
        {
            var step = trainingSteps[currentStepIndex];

            // 스텝 시작
            OnStepStarted?.Invoke(currentStepIndex);
            UpdateStepUI(step);

            // 시범 보여주기
            ShowInstruction($"다음 동작을 관찰하세요: {step.stepName}");
            handPosePlayer.StartPlaybackFromCSV(step.motionDataFile);
            yield return StartCoroutine(WaitForMotionComplete());

            // 평가 시작
            ShowInstruction("동작을 따라하세요!");

            // 다시 재생 (비교용)
            handPosePlayer.StartPlaybackFromCSV(step.motionDataFile);

            float holdTime = 0f;
            bool stepPassed = false;
            float bestSimilarity = 0f;

            // 평가 루프
            while (holdTime < step.requiredHoldTime &&
                   (handPosePlayer.IsLeftHandPlaying() || handPosePlayer.IsRightHandPlaying()))
            {
                // 정확도 계산
                float leftSim = handPosePlayer.GetLeftSimilarity();
                float rightSim = handPosePlayer.GetRightSimilarity();
                float avgSim = (leftSim + rightSim) / 2f;

                if (avgSim > bestSimilarity)
                {
                    bestSimilarity = avgSim;
                }

                // 피드백 업데이트
                UpdateEvaluationFeedback(leftSim, rightSim);

                // 임계값 체크
                bool meetsThreshold = step.evaluateBothHands ?
                    (leftSim >= step.customThreshold && rightSim >= step.customThreshold) :
                    (leftSim >= step.customThreshold || rightSim >= step.customThreshold);

                if (meetsThreshold)
                {
                    holdTime += Time.deltaTime;
                    UpdateProgressBar(holdTime / step.requiredHoldTime);
                }
                else
                {
                    holdTime = 0f;
                    UpdateProgressBar(0f);
                }

                yield return null;
            }

            // 평가 결과
            stepPassed = (holdTime >= step.requiredHoldTime);

            if (stepPassed)
            {
                passedSteps++;
                ShowInstruction($"Step {currentStepIndex + 1} 통과! (정확도: {bestSimilarity * 100:F1}%)");
            }
            else
            {
                ShowInstruction($"Step {currentStepIndex + 1} 재시도 필요 (최고 정확도: {bestSimilarity * 100:F1}%)");
            }

            // 점수 계산
            totalAccuracy += bestSimilarity;
            evaluationScore = (totalAccuracy / (currentStepIndex + 1)) * 100f;

            OnStepCompleted?.Invoke(currentStepIndex);
            UpdateScoreDisplay();

            currentStepIndex++;

            if (dotTimeline != null)
            {
                dotTimeline.SetCurrentStep(currentStepIndex);
            }

            yield return new WaitForSeconds(1f);
        }

        CompleteTraining();
    }

    /// <summary>
    /// 가이드 손 생성
    /// </summary>
    private void CreateGuideHands()
    {
        if (educationGuideHands != null)
        {
            educationGuideHands.SetActive(true);

            if (guideHandMaterial != null)
            {
                ApplyMaterialToHands(educationGuideHands, guideHandMaterial);
            }
        }
        else
        {
            Debug.LogWarning("[ChunaTraining] 교육용 가이드 손 모델이 설정되지 않았습니다!");
        }
    }

    /// <summary>
    /// 모션 경로 표시
    /// </summary>
    private void ShowMotionPath()
    {
        if (leftHandPath != null && recordedPath.Count > 0)
        {
            leftHandPath.positionCount = recordedPath.Count;
            leftHandPath.SetPositions(recordedPath.ToArray());
            leftHandPath.enabled = true;

            _ = HidePathAfterDelay();
        }
    }

    private async UniTask HidePathAfterDelay()
    {
        await UniTask.Delay(TimeSpan.FromSeconds(pathShowDuration));

        if (leftHandPath != null) leftHandPath.enabled = false;
        if (rightHandPath != null) rightHandPath.enabled = false;
    }

    /// <summary>
    /// 모션 완료 대기
    /// </summary>
    private IEnumerator WaitForMotionComplete()
    {
        if (handPosePlayer != null)
        {
            while (handPosePlayer.IsLeftHandPlaying() || handPosePlayer.IsRightHandPlaying())
            {
                yield return null;
            }
        }
        else
        {
            yield return new WaitForSeconds(3f);
        }

        OnPlaybackComplete?.Invoke();
    }

    /// <summary>
    /// 트레이닝 완료
    /// </summary>
    private void CompleteTraining()
    {
        isRunning = false;

        Debug.Log("[ChunaTraining] 트레이닝 완료!");

        // 정리
        if (handPosePlayer != null)
        {
            handPosePlayer.StopPlayback();
        }

        if (guideSystem != null)
        {
            guideSystem.StopGuiding();
        }

        // 결과 표시
        if (currentMode == TrainingMode.Education)
        {
            ShowInstruction("교육이 완료되었습니다!");
        }
        else
        {
            float finalScore = (float)passedSteps / trainingSteps.Count * 100f;
            ShowInstruction($"평가 완료! 통과율: {finalScore:F0}%, 평균 정확도: {evaluationScore:F1}%");
        }

        OnTrainingCompleted?.Invoke();

        Time.timeScale = 1f;
    }

    // ═══════════════════════════════════════════════════════════════
    // UI 업데이트 메서드
    // ═══════════════════════════════════════════════════════════════

    private void UpdateModeUI(TrainingMode mode)
    {
        if (modeIndicatorText != null)
        {
            modeIndicatorText.text = mode == TrainingMode.Education ?
                "교육 모드" : "평가 모드";
        }
    }

    private void UpdateStepUI(TrainingStepData step)
    {
        if (progressText != null)
        {
            progressText.text = $"Step {currentStepIndex + 1}/{trainingSteps.Count}: {step.stepName}";
        }

        if (instructionText != null)
        {
            instructionText.text = step.description;
        }
    }

    private void ShowInstruction(string text)
    {
        if (instructionText != null)
        {
            instructionText.text = text;
        }
        Debug.Log($"[ChunaTraining] {text}");
    }

    private void UpdateProgressBar(float progress)
    {
        if (evaluationScorePanel != null)
        {
            var progressImage = evaluationScorePanel.GetComponentInChildren<Image>();
            if (progressImage != null)
            {
                progressImage.fillAmount = progress;
            }
        }
    }

    private void UpdateEvaluationFeedback(float leftSim, float rightSim)
    {
        if (instructionText != null)
        {
            float leftError = handPosePlayer.GetLeftHandPositionError();
            float rightError = handPosePlayer.GetRightHandPositionError();

            instructionText.text = $"왼손: {leftSim * 100:F0}% (오차: {leftError * 100:F1}cm)\n" +
                                 $"오른손: {rightSim * 100:F0}% (오차: {rightError * 100:F1}cm)";
        }
    }

    private void UpdateScoreDisplay()
    {
        if (progressText != null)
        {
            progressText.text = $"점수: {evaluationScore:F1}% | 통과: {passedSteps}/{trainingSteps.Count}";
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 유틸리티 메서드
    // ═══════════════════════════════════════════════════════════════

    private void OnSpeedChanged(float value)
    {
        educationPlaybackSpeed = value;

        if (currentMode == TrainingMode.Education && isRunning)
        {
            Time.timeScale = value;
        }

        if (speedText != null)
        {
            speedText.text = $"속도: {value:F1}x";
        }
    }

    private void ApplyMaterialToHands(GameObject hands, Material material)
    {
        Renderer[] renderers = hands.GetComponentsInChildren<Renderer>();
        foreach (var renderer in renderers)
        {
            renderer.material = material;
        }
    }

    private void PlayVoiceGuidance(string text)
    {
        Debug.Log($"[ChunaTraining] 음성 안내: {text}");
        // TTS 또는 오디오 클립 재생
    }

    private void OnDestroy()
    {
        Time.timeScale = 1f;
        StopAllCoroutines();

        if (handPosePlayer != null)
        {
            handPosePlayer.StopPlayback();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 디버그/테스트 메서드
    // ═══════════════════════════════════════════════════════════════

    [ContextMenu("Test Current Accuracy")]
    private void TestCurrentAccuracy()
    {
        bool passed;
        float accuracy = CompareCurrentPose(out passed);
        Debug.Log($"[ChunaTraining] 현재 정확도: {accuracy * 100:F1}% - {(passed ? "통과" : "실패")}");
    }

    [ContextMenu("Print System Status")]
    private void PrintSystemStatus()
    {
        Debug.Log($"=== ChunaTraining 시스템 상태 ===");
        Debug.Log($"모드: {currentMode}");
        Debug.Log($"실행 중: {isRunning}");
        Debug.Log($"현재 스텝: {currentStepIndex}/{trainingSteps.Count}");
        Debug.Log($"평가 점수: {evaluationScore:F1}%");
        Debug.Log($"HandPosePlayer: {(handPosePlayer != null ? "연결됨" : "없음")}");
        Debug.Log($"GuideSystem: {(guideSystem != null ? "연결됨" : "없음")}");
    }
}