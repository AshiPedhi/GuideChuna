using UnityEngine;
using TMPro;
using TunaEvaluation;

/// <summary>
/// 추나 시술 평가 결과 화면
/// 최종 점수, 등급, 상세 내역 표시
/// </summary>
public class TunaResultUI : MonoBehaviour
{
    [Header("=== 결과 패널 ===")]
    [SerializeField] private GameObject resultPanel;

    [Header("=== 점수 표시 ===")]
    [SerializeField] private TextMeshProUGUI totalScoreText;
    [SerializeField] private TextMeshProUGUI gradeText;
    [SerializeField] private TextMeshProUGUI percentageText;

    [Header("=== 카테고리별 점수 ===")]
    [SerializeField] private TextMeshProUGUI pathComplianceText;
    [SerializeField] private TextMeshProUGUI safetyText;
    [SerializeField] private TextMeshProUGUI accuracyText;
    [SerializeField] private TextMeshProUGUI stabilityText;

    [Header("=== 세부 정보 ===")]
    [SerializeField] private TextMeshProUGUI detailsText;

    [Header("=== 버튼 ===")]
    [SerializeField] private UnityEngine.UI.Button closeButton;
    [SerializeField] private UnityEngine.UI.Button retryButton;

    void Start()
    {
        if (resultPanel != null)
            resultPanel.SetActive(false);

        if (closeButton != null)
            closeButton.onClick.AddListener(Hide);

        if (retryButton != null)
            retryButton.onClick.AddListener(OnRetry);
    }

    /// <summary>
    /// 결과 표시
    /// </summary>
    public void ShowResult(EvaluationResult result)
    {
        if (result == null) return;

        if (resultPanel != null)
            resultPanel.SetActive(true);

        // 총점
        if (totalScoreText != null)
        {
            totalScoreText.text = $"{result.score.TotalScore:F1} / {result.score.MaxTotalScore}";
        }

        // 등급
        if (gradeText != null)
        {
            gradeText.text = result.score.Grade;
            gradeText.color = GetGradeColor(result.score.Grade);
        }

        // 퍼센트
        if (percentageText != null)
        {
            percentageText.text = $"{result.score.Percentage:F1}%";
        }

        // 카테고리별 점수
        if (pathComplianceText != null)
        {
            pathComplianceText.text = $"경로 준수도\n{result.score.pathComplianceScore:F1} / {result.score.maxPathScore}\n" +
                                      $"({result.score.framesOnPath}/{result.score.totalFrames} 프레임)";
        }

        if (safetyText != null)
        {
            safetyText.text = $"안전성\n{result.score.safetyScore:F1} / {result.score.maxSafetyScore}\n" +
                              $"(위반: {result.safetyViolations.Count}건)";
        }

        if (accuracyText != null)
        {
            accuracyText.text = $"정확도\n{result.score.accuracyScore:F1} / {result.score.maxAccuracyScore}\n" +
                                $"(체크포인트: {result.score.checkpointsPassed}/{result.score.totalCheckpoints})";
        }

        if (stabilityText != null)
        {
            stabilityText.text = $"안정성\n{result.score.stabilityScore:F1} / {result.score.maxStabilityScore}\n" +
                                 $"(유지: {result.score.totalHoldTime:F1}초)";
        }

        // 세부 정보
        if (detailsText != null)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            // 수행 시간
            sb.AppendLine($"수행 시간: {result.totalDuration:F1}초");
            sb.AppendLine();

            // 안전 위반 내역
            if (result.safetyViolations.Count > 0)
            {
                sb.AppendLine($"<color=red>안전 위반 내역 ({result.safetyViolations.Count}건):</color>");
                int maxDisplay = Mathf.Min(5, result.safetyViolations.Count);
                for (int i = 0; i < maxDisplay; i++)
                {
                    sb.AppendLine($"  • {result.safetyViolations[i]}");
                }
                if (result.safetyViolations.Count > 5)
                {
                    sb.AppendLine($"  ... 외 {result.safetyViolations.Count - 5}건");
                }
                sb.AppendLine();
            }

            // 체크포인트 결과
            if (result.checkpointResults.Count > 0)
            {
                sb.AppendLine("체크포인트 결과:");
                foreach (var checkpoint in result.checkpointResults)
                {
                    string status = checkpoint.passed ? "✓" : "✗";
                    sb.AppendLine($"  {status} {checkpoint.segmentName} (유사도: {checkpoint.similarity * 100:F0}%)");
                }
            }

            detailsText.text = sb.ToString();
        }
    }

    /// <summary>
    /// 등급에 따른 색상 반환
    /// </summary>
    private Color GetGradeColor(string grade)
    {
        switch (grade)
        {
            case "A+":
            case "A":
                return new Color(0.2f, 1f, 0.2f); // 초록
            case "B+":
            case "B":
                return new Color(0.5f, 1f, 0.5f); // 연한 초록
            case "C+":
            case "C":
                return new Color(1f, 1f, 0.2f); // 노랑
            case "D":
                return new Color(1f, 0.6f, 0.2f); // 주황
            default:
                return new Color(1f, 0.2f, 0.2f); // 빨강
        }
    }

    /// <summary>
    /// 결과 화면 숨기기
    /// </summary>
    public void Hide()
    {
        if (resultPanel != null)
            resultPanel.SetActive(false);
    }

    /// <summary>
    /// 재시도 버튼 클릭
    /// </summary>
    private void OnRetry()
    {
        Hide();
        // 재시도 로직은 외부에서 구현
        Debug.Log("[TunaResultUI] 재시도 요청");
    }
}
