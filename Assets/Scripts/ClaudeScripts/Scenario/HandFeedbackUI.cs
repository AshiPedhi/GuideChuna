using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 실시간 손 유사도 피드백 UI
/// 현재 손 위치와 가장 가까운 가이드 핸드 데이터의 유사도를 표시
/// 유사도에 따라 손 이미지 색상 변경 (빨강 → 노랑 → 초록)
/// </summary>
public class HandFeedbackUI : MonoBehaviour
{
    [Header("=== 손 이미지 ===")]
    [Tooltip("왼손 이미지")]
    [SerializeField] private Image leftHandImage;

    [Tooltip("오른손 이미지")]
    [SerializeField] private Image rightHandImage;

    [Header("=== 유사도 텍스트 ===")]
    [Tooltip("왼손 유사도 텍스트")]
    [SerializeField] private TextMeshProUGUI leftSimilarityText;

    [Tooltip("오른손 유사도 텍스트")]
    [SerializeField] private TextMeshProUGUI rightSimilarityText;

    [Header("=== ChunaPathEvaluator 연결 ===")]
    [Tooltip("ChunaPathEvaluator 참조 (없으면 자동 검색)")]
    [SerializeField] private ChunaPathEvaluator pathEvaluator;

    [Header("=== 색상 설정 ===")]
    [Tooltip("낮은 유사도 색상 (빨강)")]
    [SerializeField] private Color lowSimilarityColor = new Color(1f, 0.2f, 0.2f, 1f);

    [Tooltip("중간 유사도 색상 (노랑)")]
    [SerializeField] private Color mediumSimilarityColor = new Color(1f, 1f, 0.2f, 1f);

    [Tooltip("높은 유사도 색상 (초록)")]
    [SerializeField] private Color highSimilarityColor = new Color(0.2f, 1f, 0.2f, 1f);

    [Header("=== 유사도 임계값 ===")]
    [Tooltip("낮음→중간 임계값 (0~1)")]
    [SerializeField][Range(0f, 1f)] private float lowToMediumThreshold = 0.4f;

    [Tooltip("중간→높음 임계값 (0~1)")]
    [SerializeField][Range(0f, 1f)] private float mediumToHighThreshold = 0.7f;

    [Header("=== 표시 설정 ===")]
    [Tooltip("시작 시 UI 숨김")]
    [SerializeField] private bool hideOnStart = true;

    [Tooltip("기본 색상 (평가 중이 아닐 때)")]
    [SerializeField] private Color defaultColor = new Color(0.5f, 0.5f, 0.5f, 1f);

    [Tooltip("유사도 숫자 표시 여부")]
    [SerializeField] private bool showSimilarityPercent = true;

    // 내부 상태
    private bool isActive = false;
    private float currentLeftSimilarity = 0f;
    private float currentRightSimilarity = 0f;

    void Awake()
    {
        // ChunaPathEvaluator 자동 찾기
        if (pathEvaluator == null)
        {
            pathEvaluator = FindObjectOfType<ChunaPathEvaluator>();
            if (pathEvaluator != null)
            {
                Debug.Log("[HandFeedbackUI] ChunaPathEvaluator 자동 연결 성공");
            }
        }
    }

    void Start()
    {
        if (hideOnStart)
        {
            SetUIVisible(false);
        }
        else
        {
            ResetToDefault();
        }
    }

    void OnEnable()
    {
        // 이벤트 구독
        if (pathEvaluator != null)
        {
            pathEvaluator.OnSimilarityUpdated += OnSimilarityUpdated;
            pathEvaluator.OnEvaluationStarted += OnEvaluationStarted;
            pathEvaluator.OnEvaluationCompleted += OnEvaluationCompleted;
            Debug.Log("[HandFeedbackUI] ChunaPathEvaluator 이벤트 구독");
        }
    }

    void OnDisable()
    {
        // 이벤트 구독 해제
        if (pathEvaluator != null)
        {
            pathEvaluator.OnSimilarityUpdated -= OnSimilarityUpdated;
            pathEvaluator.OnEvaluationStarted -= OnEvaluationStarted;
            pathEvaluator.OnEvaluationCompleted -= OnEvaluationCompleted;
        }
    }

    /// <summary>
    /// 평가 시작 시 호출
    /// </summary>
    private void OnEvaluationStarted()
    {
        isActive = true;
        SetUIVisible(true);
        ResetToDefault();
        Debug.Log("[HandFeedbackUI] 평가 시작 - UI 활성화");
    }

    /// <summary>
    /// 평가 완료 시 호출
    /// </summary>
    private void OnEvaluationCompleted(ChunaPathEvaluator.EvaluationSession session)
    {
        isActive = false;
        // 평가 완료 후에도 UI 유지 (마지막 유사도 표시)
        Debug.Log("[HandFeedbackUI] 평가 완료");
    }

    /// <summary>
    /// 유사도 업데이트 이벤트 핸들러
    /// </summary>
    private void OnSimilarityUpdated(float leftSimilarity, float rightSimilarity)
    {
        if (!isActive) return;

        currentLeftSimilarity = leftSimilarity;
        currentRightSimilarity = rightSimilarity;

        UpdateLeftHandDisplay(leftSimilarity);
        UpdateRightHandDisplay(rightSimilarity);
    }

    /// <summary>
    /// 왼손 UI 업데이트
    /// </summary>
    private void UpdateLeftHandDisplay(float similarity)
    {
        // 이미지 색상
        if (leftHandImage != null)
        {
            leftHandImage.color = GetColorForSimilarity(similarity);
        }

        // 유사도 텍스트
        if (leftSimilarityText != null && showSimilarityPercent)
        {
            leftSimilarityText.text = $"{similarity * 100:F0}%";
        }
    }

    /// <summary>
    /// 오른손 UI 업데이트
    /// </summary>
    private void UpdateRightHandDisplay(float similarity)
    {
        // 이미지 색상
        if (rightHandImage != null)
        {
            rightHandImage.color = GetColorForSimilarity(similarity);
        }

        // 유사도 텍스트
        if (rightSimilarityText != null && showSimilarityPercent)
        {
            rightSimilarityText.text = $"{similarity * 100:F0}%";
        }
    }

    /// <summary>
    /// 유사도에 따른 색상 계산 (빨강 → 노랑 → 초록)
    /// </summary>
    private Color GetColorForSimilarity(float similarity)
    {
        similarity = Mathf.Clamp01(similarity);

        if (similarity < lowToMediumThreshold)
        {
            // 빨강 → 노랑 보간
            float t = similarity / lowToMediumThreshold;
            return Color.Lerp(lowSimilarityColor, mediumSimilarityColor, t);
        }
        else if (similarity < mediumToHighThreshold)
        {
            // 노랑 → 초록 보간
            float t = (similarity - lowToMediumThreshold) / (mediumToHighThreshold - lowToMediumThreshold);
            return Color.Lerp(mediumSimilarityColor, highSimilarityColor, t);
        }
        else
        {
            // 초록 유지
            return highSimilarityColor;
        }
    }

    /// <summary>
    /// UI 표시/숨김
    /// </summary>
    public void SetUIVisible(bool visible)
    {
        if (leftHandImage != null)
            leftHandImage.gameObject.SetActive(visible);
        if (rightHandImage != null)
            rightHandImage.gameObject.SetActive(visible);
        if (leftSimilarityText != null)
            leftSimilarityText.gameObject.SetActive(visible);
        if (rightSimilarityText != null)
            rightSimilarityText.gameObject.SetActive(visible);
    }

    /// <summary>
    /// 기본 상태로 초기화
    /// </summary>
    public void ResetToDefault()
    {
        currentLeftSimilarity = 0f;
        currentRightSimilarity = 0f;

        if (leftHandImage != null)
            leftHandImage.color = defaultColor;
        if (rightHandImage != null)
            rightHandImage.color = defaultColor;

        if (leftSimilarityText != null)
            leftSimilarityText.text = "--%";
        if (rightSimilarityText != null)
            rightSimilarityText.text = "--%";
    }

    /// <summary>
    /// 수동으로 유사도 업데이트 (외부에서 호출 가능)
    /// </summary>
    public void UpdateSimilarity(float leftSimilarity, float rightSimilarity)
    {
        currentLeftSimilarity = leftSimilarity;
        currentRightSimilarity = rightSimilarity;

        UpdateLeftHandDisplay(leftSimilarity);
        UpdateRightHandDisplay(rightSimilarity);
    }

    /// <summary>
    /// 왼손만 유사도 업데이트
    /// </summary>
    public void UpdateLeftHandSimilarity(float similarity)
    {
        currentLeftSimilarity = similarity;
        UpdateLeftHandDisplay(similarity);
    }

    /// <summary>
    /// 오른손만 유사도 업데이트
    /// </summary>
    public void UpdateRightHandSimilarity(float similarity)
    {
        currentRightSimilarity = similarity;
        UpdateRightHandDisplay(similarity);
    }

    /// <summary>
    /// 활성화 상태 설정
    /// </summary>
    public void SetActive(bool active)
    {
        isActive = active;
        SetUIVisible(active);
        if (!active)
        {
            ResetToDefault();
        }
    }

    /// <summary>
    /// 현재 왼손 유사도
    /// </summary>
    public float CurrentLeftSimilarity => currentLeftSimilarity;

    /// <summary>
    /// 현재 오른손 유사도
    /// </summary>
    public float CurrentRightSimilarity => currentRightSimilarity;

#if UNITY_EDITOR
    [ContextMenu("Test - Low Similarity (Red)")]
    private void TestLowSimilarity()
    {
        UpdateSimilarity(0.2f, 0.2f);
    }

    [ContextMenu("Test - Medium Similarity (Yellow)")]
    private void TestMediumSimilarity()
    {
        UpdateSimilarity(0.55f, 0.55f);
    }

    [ContextMenu("Test - High Similarity (Green)")]
    private void TestHighSimilarity()
    {
        UpdateSimilarity(0.9f, 0.9f);
    }

    [ContextMenu("Test - Reset")]
    private void TestReset()
    {
        ResetToDefault();
    }
#endif
}
