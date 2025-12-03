using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>
/// 사용자 손이 현재 위치한 프레임을 표시하는 UI
/// </summary>
public class UserFrameDisplay : MonoBehaviour
{
    [Header("=== 참조 ===")]
    [SerializeField] private ChunaPathEvaluator pathEvaluator;

    [Header("=== UI 요소 ===")]
    [Tooltip("프레임 텍스트 (예: '프레임: 45 / 120')")]
    [SerializeField] private TextMeshProUGUI frameText;

    [Tooltip("비율 텍스트 (예: '38%')")]
    [SerializeField] private TextMeshProUGUI ratioText;

    [Tooltip("진행 바 (선택)")]
    [SerializeField] private Image progressBar;

    [Header("=== 경고 설정 ===")]
    [Tooltip("경고 시작 비율 (0~1)")]
    [SerializeField] private float warningRatio = 0.5f;

    [Tooltip("일반 색상")]
    [SerializeField] private Color normalColor = Color.white;

    [Tooltip("경고 색상")]
    [SerializeField] private Color warningColor = new Color(1f, 0.5f, 0f);  // 주황색

    [Tooltip("위험 색상")]
    [SerializeField] private Color dangerColor = Color.red;

    [Header("=== 표시 설정 ===")]
    [Tooltip("프레임 표시 형식")]
    [SerializeField] private FrameDisplayFormat displayFormat = FrameDisplayFormat.FrameAndRatio;

    public enum FrameDisplayFormat
    {
        FrameOnly,          // "45 / 120"
        RatioOnly,          // "38%"
        FrameAndRatio,      // "45 / 120 (38%)"
        FrameWithWarning    // "45 / 120 ⚠" (50% 초과 시)
    }

    private int lastFrameIndex = -1;

    void Start()
    {
        if (pathEvaluator == null)
        {
            pathEvaluator = FindObjectOfType<ChunaPathEvaluator>();
        }

        if (pathEvaluator != null)
        {
            pathEvaluator.OnUserFrameChanged += OnUserFrameChanged;
        }

        // 초기 상태
        UpdateDisplay(0, 1, 0f);
    }

    void OnDestroy()
    {
        if (pathEvaluator != null)
        {
            pathEvaluator.OnUserFrameChanged -= OnUserFrameChanged;
        }
    }

    private void OnUserFrameChanged(int currentFrame, int totalFrames, float ratio)
    {
        if (currentFrame == lastFrameIndex) return;
        lastFrameIndex = currentFrame;

        UpdateDisplay(currentFrame, totalFrames, ratio);
    }

    private void UpdateDisplay(int currentFrame, int totalFrames, float ratio)
    {
        // 색상 결정
        Color textColor = normalColor;
        if (ratio >= 0.7f)
            textColor = dangerColor;
        else if (ratio >= warningRatio)
            textColor = warningColor;

        // 프레임 텍스트 업데이트
        if (frameText != null)
        {
            string text = "";
            switch (displayFormat)
            {
                case FrameDisplayFormat.FrameOnly:
                    text = $"{currentFrame} / {totalFrames}";
                    break;
                case FrameDisplayFormat.RatioOnly:
                    text = $"{ratio:P0}";
                    break;
                case FrameDisplayFormat.FrameAndRatio:
                    text = $"{currentFrame} / {totalFrames} ({ratio:P0})";
                    break;
                case FrameDisplayFormat.FrameWithWarning:
                    text = ratio >= warningRatio
                        ? $"{currentFrame} / {totalFrames} !"
                        : $"{currentFrame} / {totalFrames}";
                    break;
            }

            frameText.text = text;
            frameText.color = textColor;
        }

        // 비율 텍스트 업데이트 (별도 텍스트가 있는 경우)
        if (ratioText != null)
        {
            ratioText.text = $"{ratio:P0}";
            ratioText.color = textColor;
        }

        // 진행 바 업데이트
        if (progressBar != null)
        {
            progressBar.fillAmount = ratio;
            progressBar.color = textColor;
        }
    }

    /// <summary>
    /// 수동으로 프레임 정보 업데이트
    /// </summary>
    public void SetFrame(int currentFrame, int totalFrames)
    {
        float ratio = totalFrames > 0 ? (float)currentFrame / totalFrames : 0f;
        UpdateDisplay(currentFrame, totalFrames, ratio);
    }

    /// <summary>
    /// 경고 비율 설정
    /// </summary>
    public void SetWarningRatio(float ratio)
    {
        warningRatio = Mathf.Clamp01(ratio);
    }
}
