using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 결과 테이블의 한 행 UI 컴포넌트
/// SubStep 프리팹에 붙여서 사용
/// </summary>
public class ResultTableRowUI : MonoBehaviour
{
    [Header("=== 항목명 ===")]
    [SerializeField] private TextMeshProUGUI itemNameText;

    [Header("=== 전부 (Front) ===")]
    [SerializeField] private TextMeshProUGUI frontDurationText;
    [SerializeField] private TextMeshProUGUI frontSimilarityText;

    [Header("=== 중부 (Middle) ===")]
    [SerializeField] private TextMeshProUGUI middleDurationText;
    [SerializeField] private TextMeshProUGUI middleSimilarityText;

    [Header("=== 후부 (Back) ===")]
    [SerializeField] private TextMeshProUGUI backDurationText;
    [SerializeField] private TextMeshProUGUI backSimilarityText;

    [Header("=== 배경 (선택) ===")]
    [SerializeField] private Image backgroundImage;

    /// <summary>
    /// 데이터 설정
    /// </summary>
    public void SetData(SubStepResultData data)
    {
        if (data == null) return;

        // 항목명
        if (itemNameText != null)
            itemNameText.text = data.subStepName ?? "";

        // 전부
        if (frontDurationText != null)
            frontDurationText.text = FormatDuration(data.frontDuration);
        if (frontSimilarityText != null)
            frontSimilarityText.text = FormatSimilarity(data.frontSimilarity);

        // 중부
        if (middleDurationText != null)
            middleDurationText.text = FormatDuration(data.middleDuration);
        if (middleSimilarityText != null)
            middleSimilarityText.text = FormatSimilarity(data.middleSimilarity);

        // 후부
        if (backDurationText != null)
            backDurationText.text = FormatDuration(data.backDuration);
        if (backSimilarityText != null)
            backSimilarityText.text = FormatSimilarity(data.backSimilarity);
    }

    /// <summary>
    /// 배경 색상 설정
    /// </summary>
    public void SetBackgroundColor(Color color)
    {
        if (backgroundImage != null)
            backgroundImage.color = color;
    }

    /// <summary>
    /// 시간 포맷
    /// </summary>
    private string FormatDuration(float? duration)
    {
        if (!duration.HasValue || duration.Value <= 0)
            return "";
        return duration.Value.ToString("F1");
    }

    /// <summary>
    /// 유사도 포맷
    /// </summary>
    private string FormatSimilarity(float? similarity)
    {
        if (!similarity.HasValue || similarity.Value <= 0)
            return "";
        return $"{similarity.Value:F0}%";
    }
}
