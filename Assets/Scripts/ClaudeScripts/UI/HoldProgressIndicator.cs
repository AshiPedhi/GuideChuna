using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 홀드 진행 상황을 Image의 fillAmount로 시각화하는 컴포넌트
///
/// 사용법:
/// 1. 이 컴포넌트를 아무 GameObject에 추가
/// 2. Path Evaluator에 ChunaPathEvaluator 참조 할당
/// 3. Fill Image에 Image (Type: Filled) 할당
/// </summary>
public class HoldProgressIndicator : MonoBehaviour
{
    [Header("=== 참조 ===")]
    [Tooltip("ChunaPathEvaluator 참조")]
    [SerializeField] private ChunaPathEvaluator pathEvaluator;

    [Tooltip("fillAmount를 업데이트할 Image (Image Type을 Filled로 설정해야 함)")]
    [SerializeField] private Image fillImage;

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
        if (requiredTime <= 0f)
        {
            targetFillAmount = 0f;
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

        if (showOnlyWhenHolding && fillImageObject != null)
        {
            fillImageObject.SetActive(false);
        }
    }
}
