using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// HandPosePlayer의 유사도 정보를 실시간으로 UI에 표시
/// 텍스트, 게이지 바, 색상 피드백 제공
/// </summary>
public class HandPoseSimilarityUI : MonoBehaviour
{
    [Header("=== HandPosePlayer 참조 ===")]
    [SerializeField]
    [Tooltip("유사도 정보를 가져올 HandPosePlayer")]
    private HandPosePlayer handPosePlayer;

    [Header("=== UI 요소 - 왼손 ===")]
    [SerializeField]
    [Tooltip("왼손 유사도 텍스트 (TextMeshPro)")]
    private TextMeshProUGUI leftSimilarityText;

    [SerializeField]
    [Tooltip("왼손 유사도 게이지 바 (Image)")]
    private Image leftSimilarityBar;

    [SerializeField]
    [Tooltip("왼손 위치 오차 텍스트")]
    private TextMeshProUGUI leftPositionErrorText;

    [SerializeField]
    [Tooltip("왼손 회전 오차 텍스트")]
    private TextMeshProUGUI leftRotationErrorText;

    [Header("=== UI 요소 - 오른손 ===")]
    [SerializeField]
    [Tooltip("오른손 유사도 텍스트 (TextMeshPro)")]
    private TextMeshProUGUI rightSimilarityText;

    [SerializeField]
    [Tooltip("오른손 유사도 게이지 바 (Image)")]
    private Image rightSimilarityBar;

    [SerializeField]
    [Tooltip("오른손 위치 오차 텍스트")]
    private TextMeshProUGUI rightPositionErrorText;

    [SerializeField]
    [Tooltip("오른손 회전 오차 텍스트")]
    private TextMeshProUGUI rightRotationErrorText;

    [Header("=== UI 요소 - 전체 ===")]
    [SerializeField]
    [Tooltip("전체 유사도 텍스트")]
    private TextMeshProUGUI overallSimilarityText;

    [SerializeField]
    [Tooltip("전체 유사도 게이지 바")]
    private Image overallSimilarityBar;

    [SerializeField]
    [Tooltip("합격/불합격 상태 텍스트")]
    private TextMeshProUGUI statusText;

    [Header("=== 색상 설정 ===")]
    [SerializeField]
    [Tooltip("낮은 유사도 색상 (빨강)")]
    private Color lowSimilarityColor = new Color(1f, 0.2f, 0.2f);

    [SerializeField]
    [Tooltip("중간 유사도 색상 (노랑)")]
    private Color mediumSimilarityColor = new Color(1f, 1f, 0f);

    [SerializeField]
    [Tooltip("높은 유사도 색상 (초록)")]
    private Color highSimilarityColor = new Color(0.2f, 1f, 0.2f);

    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip("중간 유사도 기준점")]
    private float mediumThreshold = 0.5f;

    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip("높은 유사도 기준점")]
    private float highThreshold = 0.7f;

    [Header("=== 표시 설정 ===")]
    [SerializeField]
    [Tooltip("백분율로 표시 (0~100%)")]
    private bool showAsPercentage = true;

    [SerializeField]
    [Tooltip("소수점 자릿수")]
    private int decimalPlaces = 1;

    [SerializeField]
    [Tooltip("게이지 바 부드러운 전환")]
    private bool smoothBarTransition = true;

    [SerializeField]
    [Tooltip("게이지 전환 속도")]
    private float barTransitionSpeed = 5f;

    [SerializeField]
    [Tooltip("UI 업데이트 간격 (초)")]
    private float updateInterval = 0.1f;

    [Header("=== 상세 정보 표시 ===")]
    [SerializeField]
    [Tooltip("위치/회전 오차 표시")]
    private bool showDetailedErrors = true;

    [SerializeField]
    [Tooltip("목표 유사도 표시")]
    private bool showTargetSimilarity = true;

    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip("목표 유사도 값 (참고용)")]
    private float targetSimilarity = 0.7f;

    // 내부 상태
    private float updateTimer = 0f;
    private float currentLeftFillAmount = 0f;
    private float currentRightFillAmount = 0f;
    private float currentOverallFillAmount = 0f;

    void Start()
    {
        // HandPosePlayer 자동 탐색
        if (handPosePlayer == null)
        {
            handPosePlayer = FindObjectOfType<HandPosePlayer>();
            if (handPosePlayer == null)
            {
                Debug.LogError("[HandPoseSimilarityUI] HandPosePlayer를 찾을 수 없습니다!");
                enabled = false;
                return;
            }
        }

        // 초기 UI 설정
        InitializeUI();
        
        Debug.Log("[HandPoseSimilarityUI] 초기화 완료");
    }

    void Update()
    {
        updateTimer += Time.deltaTime;
        
        if (updateTimer >= updateInterval)
        {
            updateTimer = 0f;
            UpdateSimilarityDisplay();
        }
    }

    /// <summary>
    /// UI 초기화
    /// </summary>
    private void InitializeUI()
    {
        // 게이지 바 초기화
        if (leftSimilarityBar != null)
            leftSimilarityBar.fillAmount = 0f;
        
        if (rightSimilarityBar != null)
            rightSimilarityBar.fillAmount = 0f;
        
        if (overallSimilarityBar != null)
            overallSimilarityBar.fillAmount = 0f;

        // 텍스트 초기화
        UpdateText(leftSimilarityText, 0f);
        UpdateText(rightSimilarityText, 0f);
        UpdateText(overallSimilarityText, 0f);
        
        if (statusText != null)
            statusText.text = "준비 중...";
    }

    /// <summary>
    /// 유사도 정보 업데이트 및 표시
    /// </summary>
    private void UpdateSimilarityDisplay()
    {
        if (handPosePlayer == null)
            return;

        // HandPosePlayer로부터 현재 유사도 가져오기
        var result = handPosePlayer.GetCurrentSimilarity();

        // 왼손 표시
        UpdateHandSimilarity(
            result.leftHandSimilarity,
            result.leftHandPositionError,
            result.leftHandRotationError,
            result.leftHandPassed,
            leftSimilarityText,
            leftSimilarityBar,
            leftPositionErrorText,
            leftRotationErrorText,
            ref currentLeftFillAmount
        );

        // 오른손 표시
        UpdateHandSimilarity(
            result.rightHandSimilarity,
            result.rightHandPositionError,
            result.rightHandRotationError,
            result.rightHandPassed,
            rightSimilarityText,
            rightSimilarityBar,
            rightPositionErrorText,
            rightRotationErrorText,
            ref currentRightFillAmount
        );

        // 전체 유사도 표시
        float overallSimilarity = (result.leftHandSimilarity + result.rightHandSimilarity) / 2f;
        UpdateOverallDisplay(overallSimilarity, result.overallPassed);
    }

    /// <summary>
    /// 개별 손 유사도 UI 업데이트
    /// </summary>
    private void UpdateHandSimilarity(
        float similarity,
        float positionError,
        float rotationError,
        bool passed,
        TextMeshProUGUI similarityText,
        Image similarityBar,
        TextMeshProUGUI posErrorText,
        TextMeshProUGUI rotErrorText,
        ref float currentFillAmount)
    {
        // 유사도 텍스트 업데이트
        if (similarityText != null)
        {
            UpdateText(similarityText, similarity);
            similarityText.color = GetColorForSimilarity(similarity);
        }

        // 게이지 바 업데이트
        if (similarityBar != null)
        {
            float targetFillAmount = similarity;
            
            if (smoothBarTransition)
            {
                currentFillAmount = Mathf.Lerp(currentFillAmount, targetFillAmount, 
                    Time.deltaTime * barTransitionSpeed);
            }
            else
            {
                currentFillAmount = targetFillAmount;
            }
            
            similarityBar.fillAmount = currentFillAmount;
            similarityBar.color = GetColorForSimilarity(similarity);
        }

        // 상세 오차 표시
        if (showDetailedErrors)
        {
            if (posErrorText != null)
            {
                posErrorText.text = $"위치: {(positionError * 100f):F1}cm";
                posErrorText.color = positionError < 0.05f ? highSimilarityColor : lowSimilarityColor;
            }

            if (rotErrorText != null)
            {
                rotErrorText.text = $"각도: {rotationError:F1}°";
                rotErrorText.color = rotationError < 15f ? highSimilarityColor : lowSimilarityColor;
            }
        }
    }

    /// <summary>
    /// 전체 유사도 UI 업데이트
    /// </summary>
    private void UpdateOverallDisplay(float overallSimilarity, bool passed)
    {
        // 전체 유사도 텍스트
        if (overallSimilarityText != null)
        {
            UpdateText(overallSimilarityText, overallSimilarity);
            overallSimilarityText.color = GetColorForSimilarity(overallSimilarity);
            
            // 목표 유사도 표시
            if (showTargetSimilarity)
            {
                string targetText = showAsPercentage 
                    ? $" / {(targetSimilarity * 100f):F0}%" 
                    : $" / {targetSimilarity:F2}";
                overallSimilarityText.text += targetText;
            }
        }

        // 전체 게이지 바
        if (overallSimilarityBar != null)
        {
            float targetFillAmount = overallSimilarity;
            
            if (smoothBarTransition)
            {
                currentOverallFillAmount = Mathf.Lerp(currentOverallFillAmount, targetFillAmount, 
                    Time.deltaTime * barTransitionSpeed);
            }
            else
            {
                currentOverallFillAmount = targetFillAmount;
            }
            
            overallSimilarityBar.fillAmount = currentOverallFillAmount;
            overallSimilarityBar.color = GetColorForSimilarity(overallSimilarity);
        }

        // 합격/불합격 상태
        if (statusText != null)
        {
            if (passed)
            {
                statusText.text = "✓ 합격!";
                statusText.color = highSimilarityColor;
            }
            else
            {
                statusText.text = "연습 중...";
                statusText.color = mediumSimilarityColor;
            }
        }
    }

    /// <summary>
    /// 텍스트 포맷 업데이트
    /// </summary>
    private void UpdateText(TextMeshProUGUI textComponent, float value)
    {
        if (textComponent == null)
            return;

        if (showAsPercentage)
        {
            textComponent.text = $"{(value * 100f).ToString($"F{decimalPlaces}")}%";
        }
        else
        {
            textComponent.text = value.ToString($"F{decimalPlaces}");
        }
    }

    /// <summary>
    /// 유사도에 따른 색상 계산
    /// </summary>
    private Color GetColorForSimilarity(float similarity)
    {
        if (similarity < mediumThreshold)
        {
            float t = similarity / mediumThreshold;
            return Color.Lerp(lowSimilarityColor, mediumSimilarityColor, t);
        }
        else if (similarity < highThreshold)
        {
            float t = (similarity - mediumThreshold) / (highThreshold - mediumThreshold);
            return Color.Lerp(mediumSimilarityColor, highSimilarityColor, t);
        }
        else
        {
            return highSimilarityColor;
        }
    }

    /// <summary>
    /// UI 표시/숨김
    /// </summary>
    public void SetUIVisible(bool visible)
    {
        gameObject.SetActive(visible);
    }

    /// <summary>
    /// 목표 유사도 설정
    /// </summary>
    public void SetTargetSimilarity(float target)
    {
        targetSimilarity = Mathf.Clamp01(target);
    }

    /// <summary>
    /// 색상 임계값 설정
    /// </summary>
    public void SetThresholds(float medium, float high)
    {
        mediumThreshold = Mathf.Clamp01(medium);
        highThreshold = Mathf.Clamp01(high);
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        // Inspector에서 값 변경 시 검증
        mediumThreshold = Mathf.Clamp01(mediumThreshold);
        highThreshold = Mathf.Clamp01(highThreshold);
        targetSimilarity = Mathf.Clamp01(targetSimilarity);
        
        if (highThreshold <= mediumThreshold)
        {
            highThreshold = mediumThreshold + 0.1f;
        }
    }
#endif
}
