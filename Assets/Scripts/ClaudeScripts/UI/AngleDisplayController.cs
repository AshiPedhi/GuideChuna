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

    [Header("=== 오프셋 설정 ===")]
    [Tooltip("각도 표시 오프셋 (실제 시작 각도가 0이 아닐 때 사용)")]
    [SerializeField] private float angleDisplayOffset = 0f;

    [Tooltip("애니메이션 시작 오프셋 (0~1, 애니메이션의 어느 지점을 0%로 볼지)")]
    [Range(0f, 1f)]
    [SerializeField] private float animationStartOffset = 0f;

    [Tooltip("애니메이션 끝 오프셋 (0~1, 애니메이션의 어느 지점을 100%로 볼지)")]
    [Range(0f, 1f)]
    [SerializeField] private float animationEndOffset = 1f;

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

    [Header("=== 홀드 범위 표시 (fillAmount) ===")]
    [Tooltip("홀드 시작점 표시 이미지 (fillAmount로 제어)")]
    [SerializeField] private Image holdStartImage;

    [Tooltip("홀드 끝점 표시 이미지 (fillAmount로 제어)")]
    [SerializeField] private Image holdEndImage;

    [Tooltip("fillAmount 배율 (90도=0.25, 180도=0.5, 360도=1.0)")]
    [SerializeField] private float fillAmountScale = 0.25f;

    [Tooltip("ChunaPathEvaluator와 홀드 범위 동기화")]
    [SerializeField] private bool syncHoldRangeWithEvaluator = true;

    [Tooltip("ChunaPathEvaluator와 애니메이션 시작 오프셋 동기화 (스트레칭 모드 자동 적용)")]
    [SerializeField] private bool syncAnimationStartWithEvaluator = true;

    [Tooltip("일반 모드 홀드 시작 비율 (0~1)")]
    [SerializeField] private float normalHoldStart = 0.3f;

    [Tooltip("일반 모드 홀드 끝 비율 (0~1)")]
    [SerializeField] private float normalHoldEnd = 0.5f;

    [Tooltip("확장 모드 홀드 시작 비율 (스트레칭/재평가)")]
    [SerializeField] private float extendedHoldStart = 0.5f;

    [Tooltip("확장 모드 홀드 끝 비율 (스트레칭/재평가)")]
    [SerializeField] private float extendedHoldEnd = 0.65f;

    [Header("=== 동기화 모드 ===")]
    [Tooltip("동기화 소스")]
    [SerializeField] private SyncSource syncSource = SyncSource.UserHandFrame;

    [Header("=== 환자 애니메이션 (PatientAnimation 모드) ===")]
    [Tooltip("환자 모델의 Animator (PatientAnimation 모드용, 자동 찾기)")]
    [SerializeField] private Animator patientAnimator;

    [Header("=== 자동 표시/숨김 ===")]
    [Tooltip("평가 시작 시 자동으로 표시, 완료 시 자동으로 숨김")]
    [SerializeField] private bool autoShowHide = true;

    [Tooltip("시작 시 숨김 상태로 시작")]
    [SerializeField] private bool hideOnStart = true;

    [Tooltip("표시/숨김할 대상 게임오브젝트 (비어있으면 자기 자신)")]
    [SerializeField] private GameObject displayTarget;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = false;

    // 상태
    private float currentAngle;
    private float currentProgress;
    private bool isInitialized;
    private bool isExtendedMode = false;  // 확장 모드 (스트레칭/재평가)
    private float currentHoldStart;
    private float currentHoldEnd;

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
        Progress,

        [Tooltip("환자 애니메이션 진행률 (0~1)")]
        PatientAnimation
    }

    void Start()
    {
        // displayTarget이 비어있으면 자기 자신으로 설정
        if (displayTarget == null)
            displayTarget = gameObject;

        // 시작 시 숨김
        if (hideOnStart)
            Hide();

        Initialize();
    }

    void Update()
    {
        // PatientAnimation 모드일 때만 Update에서 처리
        if (syncSource == SyncSource.PatientAnimation && isInitialized)
        {
            UpdateFromPatientAnimation();
        }

        // ChunaPathEvaluator와 홀드 범위 동기화
        if (syncHoldRangeWithEvaluator && pathEvaluator != null)
        {
            SyncHoldRangeWithEvaluator();
        }

        // ChunaPathEvaluator와 애니메이션 시작 오프셋 동기화 (스트레칭 모드)
        if (syncAnimationStartWithEvaluator && pathEvaluator != null)
        {
            SyncAnimationStartWithEvaluator();
        }
    }

    /// <summary>
    /// ChunaPathEvaluator의 확장 모드와 홀드 범위 동기화
    /// </summary>
    private void SyncHoldRangeWithEvaluator()
    {
        bool evaluatorExtendedMode = pathEvaluator.IsExtendedLimitMode;

        // 모드가 변경되었을 때만 업데이트
        if (isExtendedMode != evaluatorExtendedMode)
        {
            UpdateHoldRange(evaluatorExtendedMode);
        }
    }

    /// <summary>
    /// ChunaPathEvaluator의 스트레칭 모드와 애니메이션 시작 오프셋 동기화
    /// 스트레칭 모드면 애니메이션 시작 오프셋을 0.3으로 설정
    /// </summary>
    private void SyncAnimationStartWithEvaluator()
    {
        float evaluatorStartRatio = pathEvaluator.CurrentStartRatio;

        // 오프셋이 변경되었을 때만 업데이트
        if (Mathf.Abs(animationStartOffset - evaluatorStartRatio) > 0.01f)
        {
            animationStartOffset = evaluatorStartRatio;

            if (showDebugLogs)
            {
                string mode = pathEvaluator.IsStretchingMode ? "스트레칭" : "일반";
                Debug.Log($"<color=cyan>[AngleDisplayController] 애니메이션 시작 오프셋 동기화: {animationStartOffset:P0} ({mode} 모드)</color>");
            }
        }
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

        // PatientAnimation 모드일 때 Animator 찾기
        if (syncSource == SyncSource.PatientAnimation && patientAnimator == null)
        {
            GameObject patient = GameObject.FindGameObjectWithTag("Patient");
            if (patient != null)
            {
                patientAnimator = patient.GetComponent<Animator>();
            }
        }

        // PatientAnimation 모드는 PathEvaluator 없어도 됨
        if (syncSource == SyncSource.PatientAnimation)
        {
            isInitialized = axisTransform != null && patientAnimator != null;
        }
        else
        {
            isInitialized = pathEvaluator != null && axisTransform != null;
        }

        if (!isInitialized)
        {
            if (syncSource == SyncSource.PatientAnimation)
                Debug.LogWarning("[AngleDisplayController] 초기화 실패 - Axis 또는 Patient Animator를 찾을 수 없습니다.");
            else
                Debug.LogWarning("[AngleDisplayController] 초기화 실패 - PathEvaluator 또는 Axis를 찾을 수 없습니다.");
        }
        else
        {
            // 초기 각도 설정
            SetAngle(startAngle);

            // 홀드 범위 초기화
            UpdateHoldRange(false);

            if (showDebugLogs)
                Debug.Log("<color=green>[AngleDisplayController] 초기화 완료</color>");
        }
    }

    /// <summary>
    /// 홀드 범위 업데이트 (fillAmount 적용)
    /// </summary>
    private void UpdateHoldRange(bool extended)
    {
        isExtendedMode = extended;
        currentHoldStart = extended ? extendedHoldStart : normalHoldStart;
        currentHoldEnd = extended ? extendedHoldEnd : normalHoldEnd;

        // fillAmount 적용 (배율 곱하기: 90도=0.25)
        if (holdStartImage != null)
        {
            holdStartImage.fillAmount = currentHoldStart * fillAmountScale;
        }

        if (holdEndImage != null)
        {
            holdEndImage.fillAmount = currentHoldEnd * fillAmountScale;
        }

        if (showDebugLogs)
        {
            string mode = extended ? "확장(스트레칭/재평가)" : "일반";
            Debug.Log($"<color=cyan>[AngleDisplayController] 홀드 범위 업데이트: {mode} ({currentHoldStart:P0}~{currentHoldEnd:P0}, fillAmount: {currentHoldStart * fillAmountScale:F3}~{currentHoldEnd * fillAmountScale:F3})</color>");
        }
    }

    private void SubscribeEvents()
    {
        if (pathEvaluator != null)
        {
            pathEvaluator.OnUserFrameChanged += HandleUserFrameChanged;
            pathEvaluator.OnProgressChanged += HandleProgressChanged;

            // 자동 표시/숨김 이벤트 구독
            if (autoShowHide)
            {
                pathEvaluator.OnEvaluationStarted += HandleEvaluationStarted;
                pathEvaluator.OnEvaluationCompleted += HandleEvaluationCompleted;
            }
        }
    }

    private void UnsubscribeEvents()
    {
        if (pathEvaluator != null)
        {
            pathEvaluator.OnUserFrameChanged -= HandleUserFrameChanged;
            pathEvaluator.OnProgressChanged -= HandleProgressChanged;

            // 자동 표시/숨김 이벤트 구독 해제
            pathEvaluator.OnEvaluationStarted -= HandleEvaluationStarted;
            pathEvaluator.OnEvaluationCompleted -= HandleEvaluationCompleted;
        }
    }

    /// <summary>
    /// 평가 시작 핸들러 - 자동 표시
    /// </summary>
    private void HandleEvaluationStarted()
    {
        if (autoShowHide)
        {
            Show();
            if (showDebugLogs)
                Debug.Log("<color=green>[AngleDisplayController] 평가 시작 - 표시</color>");
        }
    }

    /// <summary>
    /// 평가 완료 핸들러 - 자동 숨김
    /// </summary>
    private void HandleEvaluationCompleted(ChunaPathEvaluator.EvaluationSession session)
    {
        if (autoShowHide)
        {
            Hide();
            if (showDebugLogs)
                Debug.Log("<color=orange>[AngleDisplayController] 평가 완료 - 숨김</color>");
        }
    }

    /// <summary>
    /// 환자 애니메이션 기반 업데이트 (PatientAnimation 모드)
    /// </summary>
    private void UpdateFromPatientAnimation()
    {
        if (patientAnimator == null) return;

        AnimatorStateInfo stateInfo = patientAnimator.GetCurrentAnimatorStateInfo(0);
        float normalizedTime = Mathf.Clamp01(stateInfo.normalizedTime);

        // ★ 애니메이션 시작/끝 오프셋 적용
        // animationStartOffset ~ animationEndOffset 구간을 0~1로 매핑
        float mappedRatio = RemapAnimationTime(normalizedTime);

        UpdateAngleFromRatio(mappedRatio);
    }

    /// <summary>
    /// 애니메이션 시간을 오프셋 적용하여 리매핑
    /// </summary>
    private float RemapAnimationTime(float normalizedTime)
    {
        // 오프셋이 기본값이면 그대로 반환
        if (animationStartOffset <= 0f && animationEndOffset >= 1f)
            return normalizedTime;

        // animationStartOffset 이전이면 0
        if (normalizedTime <= animationStartOffset)
            return 0f;

        // animationEndOffset 이후면 1
        if (normalizedTime >= animationEndOffset)
            return 1f;

        // 구간 내에서 0~1로 리매핑
        float range = animationEndOffset - animationStartOffset;
        if (range <= 0f) return 0f;

        return (normalizedTime - animationStartOffset) / range;
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

        // ★ 각도 표시 오프셋 적용
        targetAngle += angleDisplayOffset;

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
    /// 각도 표시 UI 보이기
    /// </summary>
    public void Show()
    {
        if (displayTarget != null)
            displayTarget.SetActive(true);
    }

    /// <summary>
    /// 각도 표시 UI 숨기기
    /// </summary>
    public void Hide()
    {
        if (displayTarget != null)
            displayTarget.SetActive(false);
    }

    /// <summary>
    /// 현재 표시 상태
    /// </summary>
    public bool IsVisible => displayTarget != null && displayTarget.activeSelf;

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

    /// <summary>
    /// 환자 Animator 설정 (PatientAnimation 모드용)
    /// </summary>
    public void SetPatientAnimator(Animator animator)
    {
        patientAnimator = animator;
        if (syncSource == SyncSource.PatientAnimation)
        {
            isInitialized = axisTransform != null && patientAnimator != null;
        }
    }

    // ========== 홀드 범위 API ==========

    /// <summary>
    /// 확장 모드 설정 (스트레칭/재평가)
    /// </summary>
    public void SetExtendedMode(bool extended)
    {
        UpdateHoldRange(extended);
    }

    /// <summary>
    /// 홀드 범위 직접 설정
    /// </summary>
    public void SetHoldRange(float holdStart, float holdEnd)
    {
        currentHoldStart = Mathf.Clamp01(holdStart);
        currentHoldEnd = Mathf.Clamp01(holdEnd);

        // fillAmount 적용 (배율 곱하기)
        if (holdStartImage != null)
            holdStartImage.fillAmount = currentHoldStart * fillAmountScale;

        if (holdEndImage != null)
            holdEndImage.fillAmount = currentHoldEnd * fillAmountScale;

        if (showDebugLogs)
            Debug.Log($"<color=cyan>[AngleDisplayController] 홀드 범위 수동 설정: {currentHoldStart:P0}~{currentHoldEnd:P0}</color>");
    }

    /// <summary>
    /// 일반/확장 모드 홀드 범위 설정값 변경
    /// </summary>
    public void SetHoldRangePresets(float normalStart, float normalEnd, float extStart, float extEnd)
    {
        normalHoldStart = Mathf.Clamp01(normalStart);
        normalHoldEnd = Mathf.Clamp01(normalEnd);
        extendedHoldStart = Mathf.Clamp01(extStart);
        extendedHoldEnd = Mathf.Clamp01(extEnd);

        // 현재 모드에 맞게 다시 적용
        UpdateHoldRange(isExtendedMode);
    }

    /// <summary>
    /// 현재 홀드 시작 비율 가져오기
    /// </summary>
    public float GetCurrentHoldStart() => currentHoldStart;

    /// <summary>
    /// 현재 홀드 끝 비율 가져오기
    /// </summary>
    public float GetCurrentHoldEnd() => currentHoldEnd;

    /// <summary>
    /// 현재 확장 모드 여부
    /// </summary>
    public bool IsExtendedMode => isExtendedMode;

    // ========== 오프셋 API ==========

    /// <summary>
    /// 각도 표시 오프셋 설정
    /// 실제 시작 각도가 0이 아닐 때 사용 (예: 시작 위치가 15도면 15 입력)
    /// </summary>
    public void SetAngleDisplayOffset(float offset)
    {
        angleDisplayOffset = offset;
        if (showDebugLogs)
            Debug.Log($"<color=cyan>[AngleDisplayController] 각도 표시 오프셋: {offset}°</color>");
    }

    /// <summary>
    /// 애니메이션 구간 오프셋 설정
    /// 애니메이션의 특정 구간만 사용할 때 (예: 20%~80% 구간만 사용)
    /// </summary>
    public void SetAnimationOffsets(float startOffset, float endOffset)
    {
        animationStartOffset = Mathf.Clamp01(startOffset);
        animationEndOffset = Mathf.Clamp01(endOffset);

        if (showDebugLogs)
            Debug.Log($"<color=cyan>[AngleDisplayController] 애니메이션 구간: {animationStartOffset:P0}~{animationEndOffset:P0}</color>");
    }

    /// <summary>
    /// 현재 각도 표시 오프셋
    /// </summary>
    public float AngleDisplayOffset => angleDisplayOffset;

    /// <summary>
    /// 현재 애니메이션 시작 오프셋
    /// </summary>
    public float AnimationStartOffset => animationStartOffset;

    /// <summary>
    /// 현재 애니메이션 끝 오프셋
    /// </summary>
    public float AnimationEndOffset => animationEndOffset;

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

    [ContextMenu("Test Hold Range - Normal Mode")]
    private void TestHoldRangeNormal() => UpdateHoldRange(false);

    [ContextMenu("Test Hold Range - Extended Mode")]
    private void TestHoldRangeExtended() => UpdateHoldRange(true);
#endif
}
