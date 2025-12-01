using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 추나 훈련 피드백 UI (체크포인트 기반)
///
/// 표시 내용:
/// - 손 이미지 색상 (유사도/경로 이탈 기반)
/// - 체크포인트 진행률
/// - 현재 유사도
/// - 감점 정보
///
/// 각 단계별 결과를 저장하여 최종 결과표에서 확인 가능
/// </summary>
public class ChunaFeedbackUI : MonoBehaviour
{
    [Header("=== 평가 시스템 참조 ===")]
    [SerializeField] private ChunaPathEvaluator pathEvaluator;
    [SerializeField] private ChunaPathEvaluatorBridge evaluatorBridge;
    [SerializeField] private DeductionRecord deductionRecord;

    [Header("=== 손 이미지 (유사도 색상) ===")]
    [SerializeField] private Image leftHandImage;
    [SerializeField] private Image rightHandImage;

    [Header("=== 진행률 UI ===")]
    [Tooltip("체크포인트 진행률 텍스트 (예: 3/10)")]
    [SerializeField] private Text progressText;
    [SerializeField] private Slider progressSlider;
    [SerializeField] private Text currentCheckpointText;

    [Header("=== 유사도 UI ===")]
    [SerializeField] private Text leftSimilarityText;
    [SerializeField] private Text rightSimilarityText;
    [SerializeField] private Text averageSimilarityText;

    [Header("=== 점수 UI ===")]
    [SerializeField] private Text scoreText;
    [SerializeField] private Text gradeText;
    [SerializeField] private Text deductionText;

    [Header("=== 경로 이탈 경고 ===")]
    [SerializeField] private GameObject pathDeviationWarning;
    [SerializeField] private Text pathDeviationText;
    [SerializeField] private Image pathDeviationIndicator;

    [Header("=== 색상 설정 (유사도 기반) ===")]
    [SerializeField] private Color lowSimilarityColor = new Color(1f, 0.2f, 0.2f, 1f);   // 빨강
    [SerializeField] private Color mediumSimilarityColor = new Color(1f, 1f, 0.2f, 1f);  // 노랑
    [SerializeField] private Color highSimilarityColor = new Color(0.2f, 1f, 0.2f, 1f);  // 초록
    [SerializeField] private Color defaultHandColor = new Color(0.5f, 0.5f, 0.5f, 1f);

    [Header("=== 유사도 임계값 ===")]
    [SerializeField][Range(0f, 1f)] private float lowThreshold = 0.4f;
    [SerializeField][Range(0f, 1f)] private float highThreshold = 0.7f;

    [Header("=== 경로 이탈 설정 ===")]
    [Tooltip("경로 이탈 경고 거리 (미터)")]
    [SerializeField] private float pathDeviationWarningDistance = 0.15f;
    [SerializeField] private Color onPathColor = new Color(0.2f, 0.8f, 0.2f);
    [SerializeField] private Color offPathColor = new Color(1f, 0.3f, 0.3f);

    [Header("=== 업데이트 설정 ===")]
    [SerializeField] private float updateInterval = 0.1f;

    // 현재 상태
    private float leftSimilarity = 0f;
    private float rightSimilarity = 0f;
    private float lastUpdateTime;
    private bool isActive = false;

    // 단계별 결과 저장
    private List<StepResult> stepResults = new List<StepResult>();
    private StepResult currentStepResult;

    /// <summary>
    /// 단계별 결과 데이터
    /// </summary>
    [System.Serializable]
    public class StepResult
    {
        public string stepName;
        public string csvFileName;
        public DateTime startTime;
        public DateTime endTime;
        public float duration;
        public int totalCheckpoints;
        public int passedCheckpoints;
        public float averageSimilarity;
        public float finalScore;
        public string grade;
        public int violationCount;
        public float totalDeduction;
        public List<CheckpointResult> checkpointResults = new List<CheckpointResult>();

        [System.Serializable]
        public class CheckpointResult
        {
            public int index;
            public string name;
            public float similarity;
            public bool passed;
            public float timeToPass;
        }
    }

    void Awake()
    {
        FindReferences();
    }

    void Start()
    {
        ConnectEvents();
        Initialize();
    }

    void Update()
    {
        if (!isActive) return;

        if (Time.time - lastUpdateTime >= updateInterval)
        {
            lastUpdateTime = Time.time;
            UpdateDisplay();
        }
    }

    void OnDestroy()
    {
        DisconnectEvents();
    }

    /// <summary>
    /// 참조 자동 찾기
    /// </summary>
    private void FindReferences()
    {
        if (pathEvaluator == null)
            pathEvaluator = FindObjectOfType<ChunaPathEvaluator>();

        if (evaluatorBridge == null)
            evaluatorBridge = FindObjectOfType<ChunaPathEvaluatorBridge>();

        if (deductionRecord == null)
            deductionRecord = FindObjectOfType<DeductionRecord>();
    }

    /// <summary>
    /// 이벤트 연결
    /// </summary>
    private void ConnectEvents()
    {
        if (pathEvaluator != null)
        {
            pathEvaluator.OnEvaluationStarted += OnEvaluationStarted;
            pathEvaluator.OnEvaluationCompleted += OnEvaluationCompleted;
            pathEvaluator.OnCheckpointPassed += OnCheckpointPassed;
            pathEvaluator.OnProgressChanged += OnProgressChanged;
        }

        if (deductionRecord != null)
        {
            deductionRecord.OnScoreChanged += OnScoreChanged;
        }
    }

    /// <summary>
    /// 이벤트 연결 해제
    /// </summary>
    private void DisconnectEvents()
    {
        if (pathEvaluator != null)
        {
            pathEvaluator.OnEvaluationStarted -= OnEvaluationStarted;
            pathEvaluator.OnEvaluationCompleted -= OnEvaluationCompleted;
            pathEvaluator.OnCheckpointPassed -= OnCheckpointPassed;
            pathEvaluator.OnProgressChanged -= OnProgressChanged;
        }

        if (deductionRecord != null)
        {
            deductionRecord.OnScoreChanged -= OnScoreChanged;
        }
    }

    /// <summary>
    /// 초기화
    /// </summary>
    private void Initialize()
    {
        ResetHandColors();

        if (pathDeviationWarning != null)
            pathDeviationWarning.SetActive(false);

        UpdateScoreDisplay(100f);
        UpdateProgressDisplay(0, 0);
    }

    /// <summary>
    /// 표시 업데이트
    /// </summary>
    private void UpdateDisplay()
    {
        if (pathEvaluator == null || !pathEvaluator.IsEvaluating) return;

        // 유사도 텍스트 업데이트
        UpdateSimilarityTexts();
    }

    // ========== 손 이미지 색상 업데이트 ==========

    /// <summary>
    /// 왼손 유사도 업데이트
    /// </summary>
    public void UpdateLeftHandSimilarity(float similarity)
    {
        leftSimilarity = similarity;

        if (leftHandImage != null)
        {
            leftHandImage.color = GetColorForSimilarity(similarity);
        }

        if (leftSimilarityText != null)
        {
            leftSimilarityText.text = $"{similarity * 100:F0}%";
        }
    }

    /// <summary>
    /// 오른손 유사도 업데이트
    /// </summary>
    public void UpdateRightHandSimilarity(float similarity)
    {
        rightSimilarity = similarity;

        if (rightHandImage != null)
        {
            rightHandImage.color = GetColorForSimilarity(similarity);
        }

        if (rightSimilarityText != null)
        {
            rightSimilarityText.text = $"{similarity * 100:F0}%";
        }
    }

    /// <summary>
    /// 양손 유사도 동시 업데이트
    /// </summary>
    public void UpdateBothHandsSimilarity(float left, float right)
    {
        UpdateLeftHandSimilarity(left);
        UpdateRightHandSimilarity(right);

        float average = (left + right) / 2f;
        if (averageSimilarityText != null)
        {
            averageSimilarityText.text = $"평균: {average * 100:F0}%";
        }
    }

    /// <summary>
    /// 유사도에 따른 색상 반환
    /// </summary>
    private Color GetColorForSimilarity(float similarity)
    {
        similarity = Mathf.Clamp01(similarity);

        if (similarity < lowThreshold)
        {
            float t = similarity / lowThreshold;
            return Color.Lerp(lowSimilarityColor, mediumSimilarityColor, t);
        }
        else if (similarity < highThreshold)
        {
            float t = (similarity - lowThreshold) / (highThreshold - lowThreshold);
            return Color.Lerp(mediumSimilarityColor, highSimilarityColor, t);
        }
        else
        {
            return highSimilarityColor;
        }
    }

    /// <summary>
    /// 손 색상 초기화
    /// </summary>
    public void ResetHandColors()
    {
        if (leftHandImage != null)
            leftHandImage.color = defaultHandColor;

        if (rightHandImage != null)
            rightHandImage.color = defaultHandColor;
    }

    // ========== 경로 이탈 표시 ==========

    /// <summary>
    /// 경로 이탈 상태 업데이트
    /// </summary>
    public void UpdatePathDeviation(bool isOnPath, float deviation)
    {
        if (pathDeviationWarning != null)
        {
            pathDeviationWarning.SetActive(!isOnPath);
        }

        if (pathDeviationText != null)
        {
            pathDeviationText.text = isOnPath ? "경로 유지" : $"경로 이탈: {deviation * 100:F0}cm";
        }

        if (pathDeviationIndicator != null)
        {
            pathDeviationIndicator.color = isOnPath ? onPathColor : offPathColor;
        }
    }

    // ========== 진행률 표시 ==========

    /// <summary>
    /// 진행률 표시 업데이트
    /// </summary>
    private void UpdateProgressDisplay(int current, int total)
    {
        if (progressText != null)
        {
            progressText.text = total > 0 ? $"{current}/{total}" : "0/0";
        }

        if (progressSlider != null)
        {
            progressSlider.value = total > 0 ? (float)current / total : 0f;
        }
    }

    /// <summary>
    /// 현재 체크포인트 표시
    /// </summary>
    private void UpdateCurrentCheckpoint(string checkpointName)
    {
        if (currentCheckpointText != null)
        {
            currentCheckpointText.text = checkpointName;
        }
    }

    // ========== 유사도 텍스트 ==========

    private void UpdateSimilarityTexts()
    {
        if (leftSimilarityText != null)
        {
            leftSimilarityText.text = $"L: {leftSimilarity * 100:F0}%";
        }

        if (rightSimilarityText != null)
        {
            rightSimilarityText.text = $"R: {rightSimilarity * 100:F0}%";
        }

        float avg = (leftSimilarity + rightSimilarity) / 2f;
        if (averageSimilarityText != null)
        {
            averageSimilarityText.text = $"평균: {avg * 100:F0}%";
        }
    }

    // ========== 점수 표시 ==========

    private void UpdateScoreDisplay(float score)
    {
        if (scoreText != null)
        {
            scoreText.text = $"{Mathf.RoundToInt(score)}점";
        }

        if (gradeText != null)
        {
            gradeText.text = GetGradeFromScore(score);
        }
    }

    private void UpdateDeductionDisplay(float totalDeduction)
    {
        if (deductionText != null)
        {
            deductionText.text = totalDeduction > 0 ? $"-{totalDeduction:F1}" : "";
        }
    }

    private string GetGradeFromScore(float score)
    {
        if (score >= 95f) return "S";
        if (score >= 90f) return "A+";
        if (score >= 85f) return "A";
        if (score >= 80f) return "B+";
        if (score >= 75f) return "B";
        if (score >= 70f) return "C+";
        if (score >= 65f) return "C";
        if (score >= 60f) return "D";
        return "F";
    }

    // ========== 이벤트 핸들러 ==========

    private void OnEvaluationStarted()
    {
        isActive = true;

        // 새 단계 결과 시작
        currentStepResult = new StepResult
        {
            startTime = DateTime.Now,
            checkpointResults = new List<StepResult.CheckpointResult>()
        };

        if (pathEvaluator != null)
        {
            var session = pathEvaluator.GetCurrentSession();
            if (session != null)
            {
                currentStepResult.csvFileName = session.procedureName;
                currentStepResult.totalCheckpoints = session.totalCheckpoints;
            }
        }

        Initialize();
        Debug.Log("[ChunaFeedbackUI] 평가 시작 - 결과 기록 시작");
    }

    private void OnEvaluationCompleted(ChunaPathEvaluator.EvaluationSession session)
    {
        isActive = false;

        if (currentStepResult != null && session != null)
        {
            // 결과 저장
            currentStepResult.endTime = DateTime.Now;
            currentStepResult.duration = session.duration;
            currentStepResult.passedCheckpoints = session.passedCheckpoints;
            currentStepResult.averageSimilarity = session.averageSimilarity;
            currentStepResult.finalScore = session.finalScore;
            currentStepResult.grade = session.grade;
            currentStepResult.violationCount = session.limitViolationCount;

            // 체크포인트별 결과 복사
            foreach (var record in session.checkpointRecords)
            {
                currentStepResult.checkpointResults.Add(new StepResult.CheckpointResult
                {
                    index = record.index,
                    name = record.name,
                    similarity = record.similarity,
                    passed = true,  // 터치된 체크포인트는 통과로 간주
                    timeToPass = record.touchTime
                });
            }

            // 리스트에 추가
            stepResults.Add(currentStepResult);

            Debug.Log($"[ChunaFeedbackUI] 단계 결과 저장 완료 (총 {stepResults.Count}개 단계)");
            Debug.Log($"  - 점수: {currentStepResult.finalScore:F0} ({currentStepResult.grade})");
            Debug.Log($"  - 체크포인트: {currentStepResult.passedCheckpoints}/{currentStepResult.totalCheckpoints}");
        }

        // 점수 표시 업데이트
        UpdateScoreDisplay(session?.finalScore ?? 100f);
    }

    private void OnCheckpointPassed(PathCheckpoint checkpoint, float similarity)
    {
        UpdateCurrentCheckpoint($"✓ {checkpoint.CheckpointName}");

        // 손 색상 업데이트
        UpdateBothHandsSimilarity(similarity, similarity);
    }

    private void OnProgressChanged(int current, int total)
    {
        UpdateProgressDisplay(current, total);
    }

    private void OnScoreChanged(float newScore)
    {
        UpdateScoreDisplay(newScore);

        if (deductionRecord != null)
        {
            float deduction = 100f - newScore;
            UpdateDeductionDisplay(deduction);
        }
    }

    // ========== 결과 조회 API ==========

    /// <summary>
    /// 모든 단계 결과 가져오기
    /// </summary>
    public List<StepResult> GetAllStepResults()
    {
        return new List<StepResult>(stepResults);
    }

    /// <summary>
    /// 총합 결과 계산
    /// </summary>
    public TotalResult CalculateTotalResult()
    {
        var total = new TotalResult();

        if (stepResults.Count == 0)
        {
            total.grade = "N/A";
            return total;
        }

        foreach (var step in stepResults)
        {
            total.totalSteps++;
            total.totalCheckpoints += step.totalCheckpoints;
            total.passedCheckpoints += step.passedCheckpoints;
            total.totalDuration += step.duration;
            total.totalScore += step.finalScore;
            total.totalViolations += step.violationCount;
            total.totalSimilarity += step.averageSimilarity;
        }

        total.averageScore = total.totalScore / total.totalSteps;
        total.averageSimilarity = total.totalSimilarity / total.totalSteps;
        total.grade = GetGradeFromScore(total.averageScore);

        return total;
    }

    /// <summary>
    /// 결과 초기화
    /// </summary>
    public void ClearResults()
    {
        stepResults.Clear();
        currentStepResult = null;
        Debug.Log("[ChunaFeedbackUI] 모든 단계 결과 초기화");
    }

    /// <summary>
    /// 단계 이름 설정 (현재 진행 중인 단계)
    /// </summary>
    public void SetCurrentStepName(string stepName)
    {
        if (currentStepResult != null)
        {
            currentStepResult.stepName = stepName;
        }
    }

    /// <summary>
    /// 총합 결과 데이터
    /// </summary>
    [System.Serializable]
    public class TotalResult
    {
        public int totalSteps;
        public int totalCheckpoints;
        public int passedCheckpoints;
        public float totalDuration;
        public float totalScore;
        public float averageScore;
        public float totalSimilarity;
        public float averageSimilarity;
        public int totalViolations;
        public string grade;
    }
}
