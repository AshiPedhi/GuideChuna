using UnityEngine;
using TMPro;
using TunaEvaluation;

/// <summary>
/// 추나 시술 평가 실시간 피드백 UI
/// 안전 위반 경고, 현재 점수, 체크포인트 상태 표시
/// </summary>
public class TunaEvaluationUI : MonoBehaviour
{
    [Header("=== 실시간 점수 표시 ===")]
    [SerializeField] private TextMeshProUGUI currentScoreText;
    [SerializeField] private TextMeshProUGUI gradeText;

    [Header("=== 카테고리별 점수 ===")]
    [SerializeField] private TextMeshProUGUI pathScoreText;
    [SerializeField] private TextMeshProUGUI safetyScoreText;
    [SerializeField] private TextMeshProUGUI accuracyScoreText;
    [SerializeField] private TextMeshProUGUI stabilityScoreText;

    [Header("=== 경고 표시 ===")]
    [SerializeField] private GameObject warningPanel;
    [SerializeField] private TextMeshProUGUI warningText;
    [SerializeField] private float warningDisplayTime = 3f;

    [Header("=== 체크포인트 표시 ===")]
    [SerializeField] private TextMeshProUGUI checkpointText;

    [Header("=== 평가기 참조 ===")]
    [SerializeField] private TunaEvaluator evaluator;

    private float warningTimer = 0f;
    private bool showingWarning = false;

    void Start()
    {
        if (warningPanel != null)
            warningPanel.SetActive(false);
    }

    void Update()
    {
        // 경고 타이머
        if (showingWarning)
        {
            warningTimer -= Time.deltaTime;
            if (warningTimer <= 0f)
            {
                HideWarning();
            }
        }

        // 실시간 점수 업데이트
        if (evaluator != null && evaluator.IsEvaluating())
        {
            UpdateScoreDisplay();
        }
    }

    /// <summary>
    /// 점수 표시 업데이트
    /// </summary>
    private void UpdateScoreDisplay()
    {
        var result = evaluator.GetCurrentResult();
        if (result == null) return;

        // 현재 점수 계산 (임시)
        float currentTotal = result.score.pathComplianceScore +
                             result.score.safetyScore +
                             result.score.accuracyScore +
                             result.score.stabilityScore;

        // 총점 표시
        if (currentScoreText != null)
        {
            currentScoreText.text = $"{currentTotal:F1}/{result.score.MaxTotalScore}";
        }

        // 등급 표시
        if (gradeText != null)
        {
            gradeText.text = result.score.Grade;
        }

        // 카테고리별 점수
        if (pathScoreText != null)
        {
            pathScoreText.text = $"경로: {result.score.pathComplianceScore:F0}/{result.score.maxPathScore}";
        }

        if (safetyScoreText != null)
        {
            safetyScoreText.text = $"안전: {result.score.safetyScore:F0}/{result.score.maxSafetyScore}";
        }

        if (accuracyScoreText != null)
        {
            accuracyScoreText.text = $"정확: {result.score.accuracyScore:F0}/{result.score.maxAccuracyScore}";
        }

        if (stabilityScoreText != null)
        {
            stabilityScoreText.text = $"안정: {result.score.stabilityScore:F0}/{result.score.maxStabilityScore}";
        }

        // 체크포인트 진행도
        if (checkpointText != null)
        {
            checkpointText.text = $"체크포인트: {result.score.checkpointsPassed}/{result.score.totalCheckpoints}";
        }
    }

    /// <summary>
    /// 안전 위반 경고 표시 (외부에서 호출)
    /// </summary>
    public void ShowSafetyWarning(string message)
    {
        if (warningPanel != null)
        {
            warningPanel.SetActive(true);
        }

        if (warningText != null)
        {
            warningText.text = $"⚠️ {message}";
        }

        showingWarning = true;
        warningTimer = warningDisplayTime;
    }

    /// <summary>
    /// 경고 숨기기
    /// </summary>
    private void HideWarning()
    {
        if (warningPanel != null)
        {
            warningPanel.SetActive(false);
        }

        showingWarning = false;
    }

    /// <summary>
    /// 체크포인트 통과 알림
    /// </summary>
    public void ShowCheckpointPassed(string segmentName)
    {
        if (checkpointText != null)
        {
            checkpointText.text = $"✓ {segmentName} 통과!";
        }
    }

    /// <summary>
    /// UI 초기화
    /// </summary>
    public void ResetUI()
    {
        if (currentScoreText != null) currentScoreText.text = "0/100";
        if (gradeText != null) gradeText.text = "F";
        if (pathScoreText != null) pathScoreText.text = "경로: 0/40";
        if (safetyScoreText != null) safetyScoreText.text = "안전: 0/30";
        if (accuracyScoreText != null) accuracyScoreText.text = "정확: 0/20";
        if (stabilityScoreText != null) stabilityScoreText.text = "안정: 0/10";
        if (checkpointText != null) checkpointText.text = "체크포인트: 0/0";

        HideWarning();
    }
}
