using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 시간 표시 형식
/// </summary>
public enum TimeDisplayFormat
{
    /// <summary>현재 / 필요 (예: "1.5 / 3.0")</summary>
    CurrentSlashRequired,
    /// <summary>남은 시간 (예: "1.5s")</summary>
    RemainingTime,
    /// <summary>현재 시간만 (예: "1.5s")</summary>
    CurrentTimeOnly,
    /// <summary>퍼센트 (예: "50%")</summary>
    Percentage,
    /// <summary>카운트다운 정수 (예: "2")</summary>
    CountdownInteger
}

/// <summary>
/// 홀드 진행 상황을 Image의 fillAmount로 시각화하는 컴포넌트
///
/// 사용법:
/// 1. 이 컴포넌트를 아무 GameObject에 추가
/// 2. Path Evaluator에 ChunaPathEvaluator 참조 할당
/// 3. Fill Image에 Image (Type: Filled) 할당
/// 4. (선택) Time Text에 TextMeshProUGUI 할당하여 시간 표시
/// </summary>
public class HoldProgressIndicator : MonoBehaviour
{
    [Header("=== 참조 ===")]
    [Tooltip("ChunaPathEvaluator 참조")]
    [SerializeField] private ChunaPathEvaluator pathEvaluator;

    [Tooltip("fillAmount를 업데이트할 Image (Image Type을 Filled로 설정해야 함)")]
    [SerializeField] private Image fillImage;

    [Header("=== 시간 표시 (TextMeshPro) ===")]
    [Tooltip("시간을 표시할 TextMeshProUGUI")]
    [SerializeField] private TextMeshProUGUI timeText;

    [Tooltip("시간 표시 형식")]
    [SerializeField] private TimeDisplayFormat timeDisplayFormat = TimeDisplayFormat.CurrentSlashRequired;

    [Tooltip("완료 시 표시할 텍스트 (비어있으면 시간 계속 표시)")]
    [SerializeField] private string completedText = "완료!";

    [Tooltip("홀드하지 않을 때 표시할 텍스트 (비어있으면 0으로 표시)")]
    [SerializeField] private string idleText = "";

    [Tooltip("소수점 자릿수")]
    [SerializeField] private int decimalPlaces = 1;

    [Header("=== 옵션 ===")]
    [Tooltip("홀드 시작 시 오브젝트 활성화")]
    [SerializeField] private bool showOnlyWhenHolding = false;

    [Tooltip("홀드 완료 시 1로 유지할 시간 (초)")]
    [SerializeField] private float completedDisplayDuration = 0.5f;

    [Tooltip("부드러운 fillAmount 전환")]
    [SerializeField] private bool smoothFill = true;

    [Tooltip("fillAmount 전환 속도")]
    [SerializeField] private float fillSpeed = 5f;

    [Header("=== 색상 변경 (선택) ===")]
    [Tooltip("진행률에 따라 색상 변경")]
    [SerializeField] private bool useColorGradient = false;

    [Tooltip("시작 색상 (0%)")]
    [SerializeField] private Color startColor = new Color(1f, 0.5f, 0f, 1f);

    [Tooltip("완료 색상 (100%)")]
    [SerializeField] private Color endColor = new Color(0f, 1f, 0f, 1f);

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = false;

    // 상태
    private float targetFillAmount = 0f;
    private float currentFillAmount = 0f;
    private bool isHoldCompleted = false;
    private float completedTimer = 0f;
    private GameObject fillImageObject;

    // 시간 표시용
    private float lastCurrentTime = 0f;
    private float lastRequiredTime = 0f;

    private void Awake()
    {
        if (fillImage != null)
        {
            fillImageObject = fillImage.gameObject;
        }
    }

    private void OnEnable()
    {
        if (pathEvaluator != null)
        {
            pathEvaluator.OnHoldProgressChanged += OnHoldProgressChanged;
            pathEvaluator.OnHoldCompleted += OnHoldCompleted;
        }

        // 초기화
        SetFillAmount(0f);
        UpdateTimeText(0f, 0f);

        if (showOnlyWhenHolding && fillImageObject != null)
        {
            fillImageObject.SetActive(false);
        }
    }

    private void OnDisable()
    {
        if (pathEvaluator != null)
        {
            pathEvaluator.OnHoldProgressChanged -= OnHoldProgressChanged;
            pathEvaluator.OnHoldCompleted -= OnHoldCompleted;
        }
    }

    private void Update()
    {
        // 완료 후 표시 시간 처리
        if (isHoldCompleted)
        {
            completedTimer += Time.deltaTime;
            if (completedTimer >= completedDisplayDuration)
            {
                isHoldCompleted = false;
                completedTimer = 0f;
                targetFillAmount = 0f;

                if (showOnlyWhenHolding && fillImageObject != null)
                {
                    fillImageObject.SetActive(false);
                }
            }
        }

        // 부드러운 fillAmount 전환
        if (smoothFill && fillImage != null)
        {
            currentFillAmount = Mathf.Lerp(currentFillAmount, targetFillAmount, Time.deltaTime * fillSpeed);
            fillImage.fillAmount = currentFillAmount;

            if (useColorGradient)
            {
                fillImage.color = Color.Lerp(startColor, endColor, currentFillAmount);
            }
        }
    }

    private void OnHoldProgressChanged(float currentTime, float requiredTime)
    {
        lastCurrentTime = currentTime;
        lastRequiredTime = requiredTime;

        if (requiredTime <= 0f)
        {
            targetFillAmount = 0f;
            UpdateTimeText(0f, 0f);
            return;
        }

        float progress = Mathf.Clamp01(currentTime / requiredTime);

        if (showDebugLogs)
        {
            Debug.Log($"[HoldProgressIndicator] Progress: {progress:P0} ({currentTime:F2}s / {requiredTime:F2}s)");
        }

        // 홀드 시작 시 오브젝트 활성화
        if (showOnlyWhenHolding && fillImageObject != null)
        {
            bool shouldShow = currentTime > 0f || isHoldCompleted;
            if (fillImageObject.activeSelf != shouldShow)
            {
                fillImageObject.SetActive(shouldShow);
            }
        }

        SetFillAmount(progress);
        UpdateTimeText(currentTime, requiredTime);
    }

    private void OnHoldCompleted()
    {
        if (showDebugLogs)
        {
            Debug.Log("[HoldProgressIndicator] Hold Completed!");
        }

        isHoldCompleted = true;
        completedTimer = 0f;
        SetFillAmount(1f);

        // 완료 텍스트 표시
        if (timeText != null && !string.IsNullOrEmpty(completedText))
        {
            timeText.text = completedText;
        }
    }

    private void SetFillAmount(float amount)
    {
        targetFillAmount = amount;

        if (!smoothFill && fillImage != null)
        {
            currentFillAmount = amount;
            fillImage.fillAmount = amount;

            if (useColorGradient)
            {
                fillImage.color = Color.Lerp(startColor, endColor, amount);
            }
        }
    }

    private void UpdateTimeText(float currentTime, float requiredTime)
    {
        if (timeText == null) return;

        // 홀드하지 않을 때 idle 텍스트 표시
        if (currentTime <= 0f && !isHoldCompleted)
        {
            if (!string.IsNullOrEmpty(idleText))
            {
                timeText.text = idleText;
            }
            else
            {
                timeText.text = FormatTime(0f, requiredTime);
            }
            return;
        }

        // 완료 상태면 completedText 유지
        if (isHoldCompleted && !string.IsNullOrEmpty(completedText))
        {
            return;
        }

        timeText.text = FormatTime(currentTime, requiredTime);
    }

    private string FormatTime(float currentTime, float requiredTime)
    {
        string format = decimalPlaces switch
        {
            0 => "F0",
            1 => "F1",
            2 => "F2",
            _ => "F1"
        };

        switch (timeDisplayFormat)
        {
            case TimeDisplayFormat.CurrentSlashRequired:
                return $"{currentTime.ToString(format)} / {requiredTime.ToString(format)}";

            case TimeDisplayFormat.RemainingTime:
                float remaining = Mathf.Max(0f, requiredTime - currentTime);
                return $"{remaining.ToString(format)}s";

            case TimeDisplayFormat.CurrentTimeOnly:
                return $"{currentTime.ToString(format)}s";

            case TimeDisplayFormat.Percentage:
                float percent = requiredTime > 0f ? (currentTime / requiredTime) * 100f : 0f;
                return $"{percent.ToString("F0")}%";

            case TimeDisplayFormat.CountdownInteger:
                int countdownValue = Mathf.CeilToInt(requiredTime - currentTime);
                return countdownValue.ToString();

            default:
                return $"{currentTime.ToString(format)} / {requiredTime.ToString(format)}";
        }
    }

    /// <summary>
    /// 런타임에서 fillImage 설정
    /// </summary>
    public void SetFillImage(Image image)
    {
        fillImage = image;
        fillImageObject = image != null ? image.gameObject : null;
        SetFillAmount(0f);
    }

    /// <summary>
    /// 런타임에서 PathEvaluator 설정
    /// </summary>
    public void SetPathEvaluator(ChunaPathEvaluator evaluator)
    {
        // 기존 이벤트 해제
        if (pathEvaluator != null)
        {
            pathEvaluator.OnHoldProgressChanged -= OnHoldProgressChanged;
            pathEvaluator.OnHoldCompleted -= OnHoldCompleted;
        }

        pathEvaluator = evaluator;

        // 새 이벤트 구독
        if (pathEvaluator != null && enabled)
        {
            pathEvaluator.OnHoldProgressChanged += OnHoldProgressChanged;
            pathEvaluator.OnHoldCompleted += OnHoldCompleted;
        }

        SetFillAmount(0f);
    }

    /// <summary>
    /// 현재 진행률 반환 (0~1)
    /// </summary>
    public float GetCurrentProgress()
    {
        return currentFillAmount;
    }

    /// <summary>
    /// 강제로 진행률 리셋
    /// </summary>
    public void ResetProgress()
    {
        isHoldCompleted = false;
        completedTimer = 0f;
        SetFillAmount(0f);
        UpdateTimeText(0f, lastRequiredTime);

        if (showOnlyWhenHolding && fillImageObject != null)
        {
            fillImageObject.SetActive(false);
        }
    }

    /// <summary>
    /// 런타임에서 timeText 설정
    /// </summary>
    public void SetTimeText(TextMeshProUGUI text)
    {
        timeText = text;
        UpdateTimeText(lastCurrentTime, lastRequiredTime);
    }

    /// <summary>
    /// 시간 표시 형식 변경
    /// </summary>
    public void SetTimeDisplayFormat(TimeDisplayFormat format)
    {
        timeDisplayFormat = format;
        UpdateTimeText(lastCurrentTime, lastRequiredTime);
    }
}
