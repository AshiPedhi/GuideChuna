using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 각도표시 프리팹 제어기
/// ChunaPathEvaluator의 프레임 진행률에 맞춰 Axis 각도를 제어
/// </summary>
public class AngleDisplayController : MonoBehaviour
{
    [Header("=== 참조 ===")]
    [Tooltip("ChunaPathEvaluator 참조 (자동 찾기)")]
    [SerializeField] private ChunaPathEvaluator pathEvaluator;

    [Tooltip("회전할 Axis Transform")]
    [SerializeField] private Transform axisTransform;

    [Header("=== 각도 설정 ===")]
    [Tooltip("시작 각도 (프레임 0%)")]
    [SerializeField] private float startAngle = 0f;

    [Tooltip("끝 각도 (프레임 100%)")]
    [SerializeField] private float endAngle = 90f;

    [Tooltip("회전 축 (Local)")]
    [SerializeField] private RotationAxis rotationAxis = RotationAxis.Z;

    [Tooltip("각도 반전")]
    [SerializeField] private bool invertAngle = false;

    [Header("=== 표시 설정 ===")]
    [Tooltip("현재 각도 텍스트 (선택)")]
    [SerializeField] private TextMeshProUGUI angleText;

    [Tooltip("각도 표시 포맷")]
    [SerializeField] private string angleFormat = "{0:F0}°";

    [Tooltip("경고 각도 (이 각도 이상이면 경고색)")]
    [SerializeField] private float warningAngle = 30f;

    [Tooltip("위험 각도 (이 각도 이상이면 위험색)")]
    [SerializeField] private float dangerAngle = 45f;

    [Header("=== 색상 ===")]
    [SerializeField] private Color normalColor = Color.green;
    [SerializeField] private Color warningColor = Color.yellow;
    [SerializeField] private Color dangerColor = Color.red;

    [Tooltip("색상 적용 대상 이미지")]
    [SerializeField] private Image statusImage;

    [Header("=== 동기화 모드 ===")]
    [Tooltip("동기화 소스")]
    [SerializeField] private SyncSource syncSource = SyncSource.UserHandFrame;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = false;

    // 상태
    private float currentAngle;
    private float currentProgress;
    private bool isInitialized;

    // 이벤트
    public event Action<float> OnAngleChanged;

    public enum RotationAxis
    {
        X,
        Y,
        Z
    }

    public enum SyncSource
    {
        [Tooltip("사용자 손의 프레임 위치")]
        UserHandFrame,

        [Tooltip("가이드 핸드 프레임")]
        GuideHandFrame,

        [Tooltip("진행률 (Progress)")]
        Progress
    }

    void Start()
    {
        Initialize();
    }

    void OnEnable()
    {
        SubscribeEvents();
    }

    void OnDisable()
    {
        UnsubscribeEvents();
    }

    /// <summary>
    /// 초기화
    /// </summary>
    public void Initialize()
    {
        if (pathEvaluator == null)
        {
            pathEvaluator = FindObjectOfType<ChunaPathEvaluator>();
        }

        if (axisTransform == null)
        {
            // 자식에서 Axis 찾기
            axisTransform = transform.Find("Axis");
        }

        isInitialized = pathEvaluator != null && axisTransform != null;

        if (!isInitialized)
        {
            Debug.LogWarning("[AngleDisplayController] 초기화 실패 - PathEvaluator 또는 Axis를 찾을 수 없습니다.");
        }
        else
        {
            // 초기 각도 설정
            SetAngle(startAngle);

            if (showDebugLogs)
                Debug.Log("<color=green>[AngleDisplayController] 초기화 완료</color>");
        }
    }

    private void SubscribeEvents()
    {
        if (pathEvaluator != null)
        {
            pathEvaluator.OnUserFrameChanged += HandleUserFrameChanged;
            pathEvaluator.OnProgressChanged += HandleProgressChanged;
        }
    }

    private void UnsubscribeEvents()
    {
        if (pathEvaluator != null)
        {
            pathEvaluator.OnUserFrameChanged -= HandleUserFrameChanged;
            pathEvaluator.OnProgressChanged -= HandleProgressChanged;
        }
    }

    /// <summary>
    /// 사용자 프레임 변경 핸들러
    /// </summary>
    private void HandleUserFrameChanged(int currentFrame, int totalFrames, float ratio)
    {
        if (syncSource != SyncSource.UserHandFrame)
            return;

        UpdateAngleFromRatio(ratio);
    }

    /// <summary>
    /// 진행률 변경 핸들러 (체크포인트 기반)
    /// </summary>
    private void HandleProgressChanged(int current, int total)
    {
        if (syncSource == SyncSource.Progress)
        {
            float ratio = total > 0 ? (float)current / total : 0f;
            UpdateAngleFromRatio(ratio);
        }
        else if (syncSource == SyncSource.GuideHandFrame)
        {
            // 가이드 핸드 프레임 기반
            int totalFrames = pathEvaluator.GetTotalFrameCount();
            if (totalFrames > 0)
            {
                int guideFrame = pathEvaluator.GetCurrentGuideFrameIndex();
                float ratio = (float)guideFrame / (totalFrames - 1);
                UpdateAngleFromRatio(ratio);
            }
        }
    }

    /// <summary>
    /// 비율에서 각도 업데이트
    /// </summary>
    private void UpdateAngleFromRatio(float ratio)
    {
        currentProgress = Mathf.Clamp01(ratio);
        float targetAngle = Mathf.Lerp(startAngle, endAngle, currentProgress);

        if (invertAngle)
            targetAngle = endAngle - (targetAngle - startAngle);

        SetAngle(targetAngle);
    }

    /// <summary>
    /// 각도 직접 설정
    /// </summary>
    public void SetAngle(float angle)
    {
        currentAngle = angle;

        // Axis 회전 적용
        if (axisTransform != null)
        {
            Vector3 rotation = axisTransform.localEulerAngles;

            switch (rotationAxis)
            {
                case RotationAxis.X:
                    rotation.x = angle;
                    break;
                case RotationAxis.Y:
                    rotation.y = angle;
                    break;
                case RotationAxis.Z:
                    rotation.z = angle;
                    break;
            }

            axisTransform.localEulerAngles = rotation;
        }

        // 텍스트 업데이트
        UpdateAngleText();

        // 상태 색상 업데이트
        UpdateStatusColor();

        // 이벤트 발생
        OnAngleChanged?.Invoke(currentAngle);

        if (showDebugLogs)
            Debug.Log($"[AngleDisplayController] 각도: {currentAngle:F1}° (진행률: {currentProgress:P0})");
    }

    /// <summary>
    /// 각도 텍스트 업데이트
    /// </summary>
    private void UpdateAngleText()
    {
        if (angleText != null)
        {
            angleText.text = string.Format(angleFormat, currentAngle);

            // 텍스트 색상도 상태에 따라 변경
            angleText.color = GetStatusColor();
        }
    }

    /// <summary>
    /// 상태 색상 업데이트
    /// </summary>
    private void UpdateStatusColor()
    {
        if (statusImage != null)
        {
            statusImage.color = GetStatusColor();
        }
    }

    /// <summary>
    /// 현재 상태에 따른 색상 반환
    /// </summary>
    private Color GetStatusColor()
    {
        float absAngle = Mathf.Abs(currentAngle);

        if (absAngle >= dangerAngle)
            return dangerColor;
        if (absAngle >= warningAngle)
            return warningColor;
        return normalColor;
    }

    // ========== Public API ==========

    /// <summary>
    /// 각도 범위 설정
    /// </summary>
    public void SetAngleRange(float start, float end)
    {
        startAngle = start;
        endAngle = end;
    }

    /// <summary>
    /// 경고/위험 임계값 설정
    /// </summary>
    public void SetThresholds(float warning, float danger)
    {
        warningAngle = warning;
        dangerAngle = danger;
    }

    /// <summary>
    /// 동기화 소스 설정
    /// </summary>
    public void SetSyncSource(SyncSource source)
    {
        syncSource = source;
    }

    /// <summary>
    /// 현재 각도 가져오기
    /// </summary>
    public float GetCurrentAngle()
    {
        return currentAngle;
    }

    /// <summary>
    /// 현재 진행률 가져오기
    /// </summary>
    public float GetCurrentProgress()
    {
        return currentProgress;
    }

    /// <summary>
    /// PathEvaluator 설정
    /// </summary>
    public void SetPathEvaluator(ChunaPathEvaluator evaluator)
    {
        UnsubscribeEvents();
        pathEvaluator = evaluator;
        SubscribeEvents();
        isInitialized = pathEvaluator != null && axisTransform != null;
    }

    /// <summary>
    /// 초기 각도로 리셋
    /// </summary>
    public void Reset()
    {
        SetAngle(startAngle);
        currentProgress = 0f;
    }

#if UNITY_EDITOR
    /// <summary>
    /// 에디터에서 테스트용
    /// </summary>
    [ContextMenu("Test Angle 0%")]
    private void TestAngle0() => SetAngle(startAngle);

    [ContextMenu("Test Angle 50%")]
    private void TestAngle50() => SetAngle(Mathf.Lerp(startAngle, endAngle, 0.5f));

    [ContextMenu("Test Angle 100%")]
    private void TestAngle100() => SetAngle(endAngle);
#endif
}
