using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 손 포즈 유사도 피드백 UI
/// 유사도에 따라 손 이미지 색상 변경 (빨강 → 노랑 → 초록)
/// + 진행률, 유사도, 연속 성공 정보 표시
/// </summary>
public class HandFeedbackUI : MonoBehaviour
{
    [Header("=== 손 이미지 ===")]
    [Tooltip("왼손 이미지")]
    [SerializeField] private Image leftHandImage;

    [Tooltip("오른손 이미지")]
    [SerializeField] private Image rightHandImage;

    [Header("=== 정보 텍스트 ===")]
    [Tooltip("왼손 진행률 텍스트 (Progress: 5/20)")]
    [SerializeField] private TextMeshProUGUI leftProgressText;

    [Tooltip("왼손 유사도 텍스트 (Match: 85%)")]
    [SerializeField] private TextMeshProUGUI leftSimilarityText;

    [Tooltip("왼손 연속 성공 텍스트 (Hold: 3/5)")]
    [SerializeField] private TextMeshProUGUI leftHoldText;

    [Tooltip("오른손 진행률 텍스트")]
    [SerializeField] private TextMeshProUGUI rightProgressText;

    [Tooltip("오른손 유사도 텍스트")]
    [SerializeField] private TextMeshProUGUI rightSimilarityText;

    [Tooltip("오른손 연속 성공 텍스트")]
    [SerializeField] private TextMeshProUGUI rightHoldText;

    [Header("=== 색상 설정 ===")]
    [Tooltip("낮은 유사도 색상 (빨강)")]
    [SerializeField] private Color lowSimilarityColor = new Color(1f, 0.2f, 0.2f, 1f);  // 빨강

    [Tooltip("중간 유사도 색상 (노랑)")]
    [SerializeField] private Color mediumSimilarityColor = new Color(1f, 1f, 0.2f, 1f); // 노랑

    [Tooltip("높은 유사도 색상 (초록)")]
    [SerializeField] private Color highSimilarityColor = new Color(0.2f, 1f, 0.2f, 1f); // 초록

    [Header("=== 유사도 임계값 ===")]
    [Tooltip("낮음→중간 임계값 (0~1)")]
    [SerializeField][Range(0f, 1f)] private float lowToMediumThreshold = 0.4f;

    [Tooltip("중간→높음 임계값 (0~1)")]
    [SerializeField][Range(0f, 1f)] private float mediumToHighThreshold = 0.7f;

    [Header("=== 초기 상태 ===")]
    [Tooltip("시작 시 손 이미지 숨김")]
    [SerializeField] private bool hideOnStart = false;

    [Tooltip("기본 색상 (유사도 0일 때)")]
    [SerializeField] private Color defaultColor = new Color(0.5f, 0.5f, 0.5f, 1f);

    /// <summary>
    /// 왼손 유사도 업데이트
    /// </summary>
    /// <param name="similarity">유사도 (0~1)</param>
    public void UpdateLeftHandSimilarity(float similarity)
    {
        if (leftHandImage == null) return;

        Color targetColor = GetColorForSimilarity(similarity);
        leftHandImage.color = targetColor;

        Debug.Log($"[HandFeedback] 왼손 유사도: {similarity:F2} → 색상: {targetColor}");
    }

    /// <summary>
    /// 왼손 전체 정보 업데이트 (진행률, 유사도, 연속 성공)
    /// </summary>
    /// <param name="similarity">유사도 (0~1)</param>
    /// <param name="currentProgress">현재 진행 프레임</param>
    /// <param name="totalFrames">전체 프레임 수</param>
    /// <param name="consecutiveCount">연속 성공 카운트</param>
    /// <param name="requiredCount">필요한 연속 프레임 수</param>
    public void UpdateLeftHandInfo(float similarity, int currentProgress, int totalFrames, int consecutiveCount, int requiredCount)
    {
        // 이미지 색상 업데이트
        UpdateLeftHandSimilarity(similarity);

        // 진행률 텍스트
        if (leftProgressText != null)
        {
            leftProgressText.text = $"Progress: {currentProgress}/{totalFrames}";
        }

        // 유사도 텍스트
        if (leftSimilarityText != null)
        {
            leftSimilarityText.text = $"Match: {similarity * 100:F0}%";
        }

        // 연속 성공 텍스트 (별 표시)
        if (leftHoldText != null)
        {
            // 필요 수를 넘지 않도록 제한
            int displayCount = Mathf.Min(consecutiveCount, requiredCount);
            string stars = GenerateStarString(displayCount, requiredCount);
            leftHoldText.text = $"Hold: {stars} {displayCount}/{requiredCount}";
        }
    }

    /// <summary>
    /// 오른손 유사도 업데이트
    /// </summary>
    /// <param name="similarity">유사도 (0~1)</param>
    public void UpdateRightHandSimilarity(float similarity)
    {
        if (rightHandImage == null) return;

        Color targetColor = GetColorForSimilarity(similarity);
        rightHandImage.color = targetColor;

        Debug.Log($"[HandFeedback] 오른손 유사도: {similarity:F2} → 색상: {targetColor}");
    }

    /// <summary>
    /// 오른손 전체 정보 업데이트 (진행률, 유사도, 연속 성공)
    /// </summary>
    /// <param name="similarity">유사도 (0~1)</param>
    /// <param name="currentProgress">현재 진행 프레임</param>
    /// <param name="totalFrames">전체 프레임 수</param>
    /// <param name="consecutiveCount">연속 성공 카운트</param>
    /// <param name="requiredCount">필요한 연속 프레임 수</param>
    public void UpdateRightHandInfo(float similarity, int currentProgress, int totalFrames, int consecutiveCount, int requiredCount)
    {
        // 이미지 색상 업데이트
        UpdateRightHandSimilarity(similarity);

        // 진행률 텍스트
        if (rightProgressText != null)
        {
            rightProgressText.text = $"Progress: {currentProgress}/{totalFrames}";
        }

        // 유사도 텍스트
        if (rightSimilarityText != null)
        {
            rightSimilarityText.text = $"Match: {similarity * 100:F0}%";
        }

        // 연속 성공 텍스트 (별 표시)
        if (rightHoldText != null)
        {
            // 필요 수를 넘지 않도록 제한
            int displayCount = Mathf.Min(consecutiveCount, requiredCount);
            string stars = GenerateStarString(displayCount, requiredCount);
            rightHoldText.text = $"Hold: {stars} {displayCount}/{requiredCount}";
        }
    }

    /// <summary>
    /// 양손 유사도 동시 업데이트
    /// </summary>
    /// <param name="leftSimilarity">왼손 유사도 (0~1)</param>
    /// <param name="rightSimilarity">오른손 유사도 (0~1)</param>
    public void UpdateBothHandsSimilarity(float leftSimilarity, float rightSimilarity)
    {
        UpdateLeftHandSimilarity(leftSimilarity);
        UpdateRightHandSimilarity(rightSimilarity);
    }

    /// <summary>
    /// 유사도에 따른 색상 계산
    /// </summary>
    /// <param name="similarity">유사도 (0~1)</param>
    /// <returns>계산된 색상</returns>
    private Color GetColorForSimilarity(float similarity)
    {
        similarity = Mathf.Clamp01(similarity);

        if (similarity < lowToMediumThreshold)
        {
            // 빨강 → 노랑 보간 (0 ~ lowToMediumThreshold)
            float t = similarity / lowToMediumThreshold;
            return Color.Lerp(lowSimilarityColor, mediumSimilarityColor, t);
        }
        else if (similarity < mediumToHighThreshold)
        {
            // 노랑 → 초록 보간 (lowToMediumThreshold ~ mediumToHighThreshold)
            float t = (similarity - lowToMediumThreshold) / (mediumToHighThreshold - lowToMediumThreshold);
            return Color.Lerp(mediumSimilarityColor, highSimilarityColor, t);
        }
        else
        {
            // 초록 유지 (mediumToHighThreshold ~ 1)
            return highSimilarityColor;
        }
    }

    /// <summary>
    /// 왼손 이미지 표시/숨김
    /// </summary>
    public void ShowLeftHand(bool show)
    {
        if (leftHandImage != null)
        {
            leftHandImage.gameObject.SetActive(show);
        }
    }

    /// <summary>
    /// 오른손 이미지 표시/숨김
    /// </summary>
    public void ShowRightHand(bool show)
    {
        if (rightHandImage != null)
        {
            rightHandImage.gameObject.SetActive(show);
        }
    }

    /// <summary>
    /// 양손 이미지 표시/숨김
    /// </summary>
    public void ShowBothHands(bool show)
    {
        ShowLeftHand(show);
        ShowRightHand(show);
    }

    /// <summary>
    /// 왼손 색상 초기화
    /// </summary>
    public void ResetLeftHandColor()
    {
        if (leftHandImage != null)
        {
            leftHandImage.color = defaultColor;
        }
    }

    /// <summary>
    /// 오른손 색상 초기화
    /// </summary>
    public void ResetRightHandColor()
    {
        if (rightHandImage != null)
        {
            rightHandImage.color = defaultColor;
        }
    }

    /// <summary>
    /// 양손 색상 초기화
    /// </summary>
    public void ResetBothHandsColor()
    {
        ResetLeftHandColor();
        ResetRightHandColor();
    }

    void Start()
    {
        if (hideOnStart)
        {
            ShowBothHands(false);
        }
        else
        {
            ResetBothHandsColor();
        }
    }

    /// <summary>
    /// 연속 성공 카운트를 별 문자열로 변환 (⭐⭐⭐○○)
    /// </summary>
    /// <param name="current">현재 카운트</param>
    /// <param name="required">필요한 카운트</param>
    /// <returns>별 문자열</returns>
    private string GenerateStarString(int current, int required)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        // 현재까지 채운 별
        for (int i = 0; i < current && i < required; i++)
        {
            sb.Append("⭐");
        }

        // 아직 채워지지 않은 별
        for (int i = current; i < required; i++)
        {
            sb.Append("○");
        }

        return sb.ToString();
    }

#if UNITY_EDITOR
    /// <summary>
    /// Inspector에서 테스트용
    /// </summary>
    [ContextMenu("Test - Low Similarity (Red)")]
    private void TestLowSimilarity()
    {
        UpdateBothHandsSimilarity(0.2f, 0.2f);
    }

    [ContextMenu("Test - Medium Similarity (Yellow)")]
    private void TestMediumSimilarity()
    {
        UpdateBothHandsSimilarity(0.55f, 0.55f);
    }

    [ContextMenu("Test - High Similarity (Green)")]
    private void TestHighSimilarity()
    {
        UpdateBothHandsSimilarity(0.9f, 0.9f);
    }

    [ContextMenu("Test - Reset Colors")]
    private void TestResetColors()
    {
        ResetBothHandsColor();
    }
#endif
}
