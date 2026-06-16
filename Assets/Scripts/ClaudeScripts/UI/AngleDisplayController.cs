using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 각도표시 프리팹 제어기
/// ChunaPathEvaluator의 프레임 진행률에 맞춰 Axis 각도를 제어
/// Arc 모드: 원호형 각도 표시 (기존)
/// Linear 모드: 직선형 가동범위 표시 (대흉근, 흉쇄유돌근 등)
/// </summary>
public class AngleDisplayController : BaseUIPanel
{
    [Header("=== 표시 모드 ===")]
    [Tooltip("Arc: 원호형 각도 표시, Linear: 직선형 가동범위 표시")]
    [SerializeField] private DisplayMode displayMode = DisplayMode.Arc;

    [Header("=== 참조 ===")]
    [Tooltip("ChunaPathEvaluator 참조 (자동 찾기)")]
    [SerializeField] private ChunaPathEvaluator pathEvaluator;

    [Tooltip("회전할 Axis Transform (Arc 모드)")]
    [SerializeField] private Transform axisTransform;

    [Header("=== 각도 설정 (Arc 모드) ===")]
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

    [Header("=== Arc 모드 ===")]
    [Tooltip("Arc 모드: 배경 트랙 이미지")]
    [SerializeField] private Image arcTrackImage;

    [Tooltip("홀드 시작점 표시 이미지 (fillAmount로 제어)")]
    [SerializeField] private Image holdStartImage;

    [Tooltip("홀드 끝점 표시 이미지 (fillAmount로 제어)")]
    [SerializeField] private Image holdEndImage;


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

    [Header("=== Linear 모드 ===")]
    [Tooltip("Linear 모드: 배경 트랙 이미지")]
    [SerializeField] private Image linearTrackImage;

    [Tooltip("Linear 모드: 홀드 시작점 표시 이미지 (fillAmount로 제어)")]
    [SerializeField] private Image linearHoldStartImage;

    [Tooltip("Linear 모드: 홀드 끝점 표시 이미지 (fillAmount로 제어)")]
    [SerializeField] private Image linearHoldEndImage;

    [Tooltip("Linear 모드: 현재 위치 마커")]
    [SerializeField] private RectTransform linearMarker;

    [Tooltip("Linear 모드: 트랙 시작 위치 (로컬 좌표)")]
    [SerializeField] private float linearTrackStart = 0f;

    [Tooltip("Linear 모드: 트랙 끝 위치 (로컬 좌표)")]
    [SerializeField] private float linearTrackEnd = 200f;

    [Tooltip("Linear 모드: 이동 축 (X=수평, Y=수직)")]
    [SerializeField] private LinearAxis linearAxis = LinearAxis.Y;

    [Header("=== 프리셋 ===")]
    [Tooltip("프리셋 기준 트랜스폼 (할당 시 상대 좌표로 저장/적용, 미할당 시 월드 좌표)")]
    [SerializeField] private Transform presetReferenceTransform;
    [SerializeField] private List<AngleDisplayPreset> presets = new List<AngleDisplayPreset>();

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = false;

    // 프리셋 상태
    private string currentPresetName;

    // 상태
    private float currentAngle;
    private float currentProgress;
    private bool isInitialized;
    private bool isExtendedMode = false;  // 확장 모드 (스트레칭/재평가)
    private bool isStretchingMode = false;  // 스트레칭 모드 추적 (홀드 범위 구분용)
    private float currentHoldStart;
    private float currentHoldEnd;

    protected override GameObject GetPanelObject() => displayTarget != null ? displayTarget : gameObject;

    // 이벤트
    public event Action<float> OnAngleChanged;

    public enum DisplayMode
    {
        [Tooltip("원호형 각도 표시 (기존 방식)")]
        Arc,

        [Tooltip("직선형 가동범위 표시 (대흉근, 흉쇄유돌근 등)")]
        Linear
    }

    public enum RotationAxis
    {
        X,
        Y,
        Z
    }

    public enum LinearAxis
    {
        [Tooltip("수평 이동")]
        X,

        [Tooltip("수직 이동")]
        Y
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

    [System.Serializable]
    public class AngleDisplayPreset
    {
        public string presetName;           // "LateralFlexion", "LeftRotation", "RightRotation"

        [Header("표시 모드")]
        public DisplayMode displayMode;     // Arc 또는 Linear

        [Header("위치")]
        public Vector3 position;            // referenceTransform 있으면 로컬, 없으면 월드
        public Vector3 rotation;            // referenceTransform 있으면 로컬, 없으면 월드
        public Vector3 scale = Vector3.one; // 로컬 스케일 (좌우반전 등)

        [Header("각도 설정 (Arc 모드)")]
        public RotationAxis rotationAxis;   // X, Y, Z
        public float startAngle;
        public float endAngle;
        public bool invertAngle;

        [Header("Linear 모드")]
        public LinearAxis linearAxis;       // X 또는 Y
        public float linearTrackStart;
        public float linearTrackEnd = 200f;

        [Header("동기화")]
        public SyncSource syncSource;
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
        if (pathEvaluator != null)
        {
            SyncHoldRangeWithEvaluator();
            SyncAngleOffsetWithEvaluator();
        }
    }

    /// <summary>
    /// ChunaPathEvaluator의 확장 모드와 홀드 범위 동기화
    /// ★ 같은 모드 내에서도 substep 전환으로 회전↔위치 단계가 바뀌면 hold 범위 값이 달라짐 → 값 자체도 비교
    /// </summary>
    private void SyncHoldRangeWithEvaluator()
    {
        bool evaluatorExtendedMode = pathEvaluator.IsExtendedLimitMode;
        bool evaluatorStretchingMode = pathEvaluator.IsStretchingMode;
        // ★ 디스플레이용 절대값 사용 (스트레칭도 재평가와 동일 좌표계)
        float displayHoldStart = pathEvaluator.DisplayMidHoldStart;
        float displayHoldEnd = pathEvaluator.DisplayMidHoldEnd;

        bool modeChanged = isExtendedMode != evaluatorExtendedMode || isStretchingMode != evaluatorStretchingMode;
        bool valueChanged = Mathf.Abs(currentHoldStart - displayHoldStart) > 0.001f
                         || Mathf.Abs(currentHoldEnd - displayHoldEnd) > 0.001f;

        if (modeChanged || valueChanged)
        {
            isStretchingMode = evaluatorStretchingMode;
            UpdateHoldRange(evaluatorExtendedMode);
        }
    }

    /// <summary>
    /// ChunaPathEvaluator의 스트레칭 모드와 각도 표시 오프셋 동기화
    /// 스트레칭 애니메이션은 0프레임부터 시작하지만, 이미 30° 각도가 적용된 상태
    /// 예: 스트레칭 오프셋 비율이 0.3이면 27° (0.3 * 90) 오프셋 적용
    /// </summary>
    private void SyncAngleOffsetWithEvaluator()
    {
        // ★ CurrentAngleDisplayOffset 사용 (스트레칭: 0.3, 나머지: 0)
        float evaluatorOffsetRatio = pathEvaluator.CurrentAngleDisplayOffset;

        // 오프셋 비율을 각도로 변환 (예: 0.3 * 90 = 27도)
        float targetAngleOffset = evaluatorOffsetRatio * (endAngle - startAngle);

        // 오프셋이 변경되었을 때만 업데이트 (0.5도 이상 차이)
        if (Mathf.Abs(angleDisplayOffset - targetAngleOffset) > 0.5f)
        {
            angleDisplayOffset = targetAngleOffset;

            // ★ 오프셋 변경 시 홀드 범위 위치도 업데이트 (axis 위치와 동기화)
            UpdateHoldRange(isExtendedMode);

            if (showDebugLogs)
            {
                string mode = pathEvaluator.IsStretchingMode ? "스트레칭" : "일반";
                ChunaLogger.Log($"<color=cyan>[AngleDisplayController] 각도 표시 오프셋 동기화: {angleDisplayOffset:F1}° ({mode} 모드, 오프셋 비율: {evaluatorOffsetRatio:P0})</color>");
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
            pathEvaluator = FindFirstObjectByType<ChunaPathEvaluator>();
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

        // 모드별 초기화 조건 판단
        if (displayMode == DisplayMode.Linear)
        {
            // Linear 모드: axisTransform 불필요, 녹색 구간 또는 마커 필요
            bool hasLinearUI = linearHoldStartImage != null || linearMarker != null;
            if (syncSource == SyncSource.PatientAnimation)
                isInitialized = hasLinearUI && patientAnimator != null;
            else
                isInitialized = hasLinearUI && pathEvaluator != null;
        }
        else if (syncSource == SyncSource.PatientAnimation)
        {
            isInitialized = axisTransform != null && patientAnimator != null;
        }
        else
        {
            isInitialized = pathEvaluator != null && axisTransform != null;
        }

        if (!isInitialized)
        {
            string modeStr = displayMode == DisplayMode.Linear ? "Linear" : "Arc";
            ChunaLogger.LogWarning($"[AngleDisplayController] 초기화 실패 [{modeStr}] - 필요한 참조를 찾을 수 없습니다.");
        }
        else
        {
            // 초기 각도 설정
            SetAngle(startAngle);

            // 홀드 범위 초기화
            UpdateHoldRange(false);

            if (showDebugLogs)
                ChunaLogger.Log("<color=green>[AngleDisplayController] 초기화 완료</color>");
        }
    }

    /// <summary>
    /// 홀드 범위 업데이트 (fillAmount 적용)
    /// ★ ChunaPathEvaluator의 값을 직접 사용 (스트레칭/재평가/일반 자동 구분)
    /// </summary>
    private void UpdateHoldRange(bool extended)
    {
        isExtendedMode = extended;

        // ★ 디스플레이용 절대값 사용 (스트레칭도 재평가와 동일 좌표계)
        if (pathEvaluator != null)
        {
            currentHoldStart = pathEvaluator.DisplayMidHoldStart;
            currentHoldEnd = pathEvaluator.DisplayMidHoldEnd;
        }

        // ★ Arc + Linear 양쪽 모두 갱신 (모드 전환 시 stale 값 방지)
        // Arc 모드
        {
            float angleAtHoldStart = Mathf.Lerp(startAngle, endAngle, currentHoldStart);
            float angleAtHoldEnd = Mathf.Lerp(startAngle, endAngle, currentHoldEnd);

            if (holdStartImage != null)
                holdStartImage.fillAmount = angleAtHoldStart / 360f;
            if (holdEndImage != null)
                holdEndImage.fillAmount = angleAtHoldEnd / 360f;
        }
        // Linear 모드
        UpdateLinearHoldRange();

        if (showDebugLogs)
        {
            string mode = extended ? (pathEvaluator != null && pathEvaluator.IsStretchingMode ? "스트레칭" : "재평가") : "일반";
            ChunaLogger.Log($"<color=cyan>[AngleDisplayController] 홀드 범위 업데이트: {mode} ({currentHoldStart:P0}~{currentHoldEnd:P0})</color>");
        }
    }

    /// <summary>
    /// Linear 모드: 적정 범위 업데이트 (Arc 모드와 동일 방식)
    /// - linearHoldStartImage: fillAmount = currentHoldStart (적정 범위 시작)
    /// - linearHoldEndImage: fillAmount = currentHoldEnd (적정 범위 끝)
    /// - 모드 변경 시 ChunaPathEvaluator에서 값만 갱신 → 구간 자동 이동
    /// </summary>
    private void UpdateLinearHoldRange()
    {
        // 적정 범위 시작 (holdStartImage와 동일 역할)
        if (linearHoldStartImage != null)
        {
            linearHoldStartImage.fillAmount = currentHoldStart;
        }

        // 적정 범위 끝 (holdEndImage와 동일 역할)
        if (linearHoldEndImage != null)
        {
            linearHoldEndImage.fillAmount = currentHoldEnd;
        }

        if (showDebugLogs)
        {
            string mode = isStretchingMode ? "스트레칭" : (isExtendedMode ? "재평가" : "일반");
            ChunaLogger.Log($"<color=cyan>[AngleDisplayController-Linear] 적정 범위: {currentHoldStart:P0}~{currentHoldEnd:P0} ({mode})</color>");
        }
    }

    private void SubscribeEvents()
    {
        if (pathEvaluator != null)
        {
            pathEvaluator.OnUserFrameChanged += HandleUserFrameChanged;

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
                ChunaLogger.Log("<color=green>[AngleDisplayController] 평가 시작 - 표시</color>");
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
                ChunaLogger.Log("<color=orange>[AngleDisplayController] 평가 완료 - 숨김</color>");
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
    /// ★ 스트레칭 + UserHandFrame: 상대 진행도에 stretchingGuideStart 가산 (절대값 변환)
    /// ★ PatientAnimation: normalizedTime이 이미 절대값이므로 가산 안 함
    /// </summary>
    private void UpdateAngleFromRatio(float ratio)
    {
        currentProgress = Mathf.Clamp01(ratio);

        // ★ 스트레칭: 싱크 소스별 절대값 변환 (마커와 동일 좌표계)
        if (isStretchingMode && pathEvaluator != null)
        {
            float start = pathEvaluator.StretchingGuideStart;
            if (syncSource == SyncSource.PatientAnimation)
            {
                // 애니메이션 remap (start + p × (1-start)) 역변환 → 선형 절대값
                float range = 1f - start;
                float relProgress = range > 0.001f ? (currentProgress - start) / range : 0f;
                currentProgress = Mathf.Clamp01(relProgress + start);
            }
            else
            {
                // UserHandFrame 등: 상대값 + 오프셋
                currentProgress = Mathf.Clamp01(currentProgress + start);
            }
        }

        float effectiveStartAngle = startAngle + angleDisplayOffset;
        float targetAngle = Mathf.Lerp(effectiveStartAngle, endAngle, currentProgress);

        if (invertAngle)
            targetAngle = endAngle - (targetAngle - effectiveStartAngle);

        SetAngle(targetAngle);
    }

    /// <summary>
    /// 각도 직접 설정
    /// </summary>
    public void SetAngle(float angle)
    {
        currentAngle = angle;

        if (displayMode == DisplayMode.Linear)
        {
            UpdateLinearDisplay();
        }
        else
        {
            // Arc 모드: Axis 회전 적용
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
        }

        // 이벤트 발생
        OnAngleChanged?.Invoke(currentAngle);

        if (showDebugLogs)
        {
            string modeStr = displayMode == DisplayMode.Linear ? "Linear" : "Arc";
            ChunaLogger.Log($"[AngleDisplayController] [{modeStr}] 값: {currentAngle:F1}° (진행률: {currentProgress:P0})");
        }
    }

    /// <summary>
    /// Linear 모드: 마커 위치 업데이트
    /// </summary>
    private void UpdateLinearDisplay()
    {
        // 마커 위치 이동 (현재 진행률)
        if (linearMarker != null)
        {
            SetLinearPosition(linearMarker, currentProgress);
        }
    }

    /// <summary>
    /// Linear 모드: 비율(0~1)을 트랙 위치로 변환하여 RectTransform에 적용
    /// </summary>
    private void SetLinearPosition(RectTransform target, float ratio)
    {
        float pos = Mathf.Lerp(linearTrackStart, linearTrackEnd, ratio);
        Vector3 localPos = target.anchoredPosition3D;

        if (linearAxis == LinearAxis.Y)
            localPos.y = pos;
        else
            localPos.x = pos;

        target.anchoredPosition3D = localPos;
    }

    /// <summary>
    /// 각도 텍스트 업데이트
    /// </summary>

    // ========== Public API ==========

    /// <summary>
    /// 각도 표시 UI 보이기
    /// </summary>
    public override void Show()
    {
        base.Show();
    }

    /// <summary>
    /// 각도 표시 UI 숨기기
    /// </summary>
    public override void Hide()
    {
        base.Hide();
    }

    /// <summary>
    /// 외부에서 홀드 범위 강제 갱신 (substep 전환 후 evaluator 값이 바뀐 뒤 호출)
    /// </summary>
    public void ForceRefreshHoldRange()
    {
        if (pathEvaluator == null)
        {
            ChunaLogger.LogWarning("[AngleDisplayController] ForceRefreshHoldRange: pathEvaluator null");
            return;
        }

        float before = currentHoldStart;
        isStretchingMode = pathEvaluator.IsStretchingMode;
        UpdateHoldRange(pathEvaluator.IsExtendedLimitMode);

        float arcStartFill = holdStartImage != null ? holdStartImage.fillAmount : -1f;
        float arcEndFill = holdEndImage != null ? holdEndImage.fillAmount : -1f;
        float linStartFill = linearHoldStartImage != null ? linearHoldStartImage.fillAmount : -1f;
        float linEndFill = linearHoldEndImage != null ? linearHoldEndImage.fillAmount : -1f;
        ChunaLogger.Log($"<color=yellow>[AngleDisplayController] ForceRefreshHoldRange: holdRange={currentHoldStart:P0}~{currentHoldEnd:P0} " +
            $"(mode:{displayMode}, stretch:{isStretchingMode}, extended:{isExtendedMode})" +
            $"\n  Arc fillAmount: start={arcStartFill:F4}, end={arcEndFill:F4}" +
            $"\n  Linear fillAmount: start={linStartFill:F4}, end={linEndFill:F4}</color>");
    }

    /// <summary>
    /// 각도 범위 설정
    /// </summary>
    public void SetAngleRange(float start, float end)
    {
        startAngle = start;
        endAngle = end;
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
    /// 현재 표시 모드
    /// </summary>
    public DisplayMode CurrentDisplayMode => displayMode;

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


    // ========== 프리셋 API ==========

    /// <summary>
    /// 프리셋 목록 (Editor 접근용)
    /// </summary>
    public List<AngleDisplayPreset> Presets => presets;

    /// <summary>
    /// 현재 적용된 프리셋 이름
    /// </summary>
    public string CurrentPresetName => currentPresetName;

    /// <summary>
    /// 이름으로 프리셋 찾기
    /// </summary>
    public AngleDisplayPreset FindPreset(string name)
    {
        return presets.Find(p => p.presetName == name);
    }

    /// <summary>
    /// 프리셋 적용 (위치+설정 교체 + Show)
    /// </summary>
    public bool ApplyPreset(string presetName)
    {
        AngleDisplayPreset preset = FindPreset(presetName);
        if (preset == null)
        {
            ChunaLogger.LogWarning($"[AngleDisplayController] 프리셋을 찾을 수 없습니다: {presetName}");
            return false;
        }

        ApplyPreset(preset);
        return true;
    }

    /// <summary>
    /// 시나리오 접두사 우선 매칭: "{scenarioName}_{presetName}" 먼저 찾고, 없으면 "{presetName}" fallback
    /// 같은 핸드데이터를 공유하는 시나리오별로 다른 위치의 프리셋을 사용할 수 있게 해줌
    /// </summary>
    public bool ApplyPreset(string scenarioName, string presetName)
    {
        // 1차: 시나리오 접두사 매칭 (예: "견갑거근_건측회전")
        if (!string.IsNullOrEmpty(scenarioName))
        {
            string prefixedName = $"{scenarioName}_{presetName}";
            AngleDisplayPreset prefixed = FindPreset(prefixedName);
            if (prefixed != null)
            {
                ChunaLogger.Log($"<color=green>[AngleDisplayController] 시나리오별 프리셋 적용: {prefixedName}</color>");
                ApplyPreset(prefixed);
                return true;
            }
        }

        // 2차: 기존 이름 매칭 (예: "건측회전")
        return ApplyPreset(presetName);
    }

    /// <summary>
    /// 프리셋 직접 적용
    /// </summary>
    public void ApplyPreset(AngleDisplayPreset preset)
    {
        currentPresetName = preset.presetName;

        // 표시 모드 적용
        displayMode = preset.displayMode;

        // 위치/회전/스케일 적용
        if (presetReferenceTransform != null)
        {
            transform.position = presetReferenceTransform.TransformPoint(preset.position);
            transform.rotation = presetReferenceTransform.rotation * Quaternion.Euler(preset.rotation);
        }
        else
        {
            transform.position = preset.position;
            transform.rotation = Quaternion.Euler(preset.rotation);
        }
        transform.localScale = preset.scale;

        // 모드별 설정 적용
        if (displayMode == DisplayMode.Linear)
        {
            linearAxis = preset.linearAxis;
            linearTrackStart = preset.linearTrackStart;
            linearTrackEnd = preset.linearTrackEnd;
        }
        else
        {
            rotationAxis = preset.rotationAxis;
            startAngle = preset.startAngle;
            endAngle = preset.endAngle;
            invertAngle = preset.invertAngle;
        }

        // 동기화 소스 적용
        syncSource = preset.syncSource;

        // Arc/Linear UI 토글
        ToggleDisplayModeUI();

        // 각도 리셋
        SetAngle(startAngle);

        // 표시
        Show();

        if (showDebugLogs)
        {
            if (displayMode == DisplayMode.Linear)
                ChunaLogger.Log($"<color=green>[AngleDisplayController] 프리셋 적용 [Linear]: {preset.presetName} (pos={preset.position}, axis={preset.linearAxis}, track={preset.linearTrackStart}~{preset.linearTrackEnd})</color>");
            else
                ChunaLogger.Log($"<color=green>[AngleDisplayController] 프리셋 적용 [Arc]: {preset.presetName} (pos={preset.position}, axis={preset.rotationAxis}, {preset.startAngle}°~{preset.endAngle}°)</color>");
        }
    }

    /// <summary>
    /// 표시 모드에 따라 Arc/Linear UI 요소 토글
    /// </summary>
    private void ToggleDisplayModeUI()
    {
        bool isLinear = displayMode == DisplayMode.Linear;

        // Arc 모드 UI
        if (axisTransform != null)
            axisTransform.gameObject.SetActive(!isLinear);
        if (arcTrackImage != null)
            arcTrackImage.gameObject.SetActive(!isLinear);
        if (holdStartImage != null)
            holdStartImage.gameObject.SetActive(!isLinear);
        if (holdEndImage != null)
            holdEndImage.gameObject.SetActive(!isLinear);

        // Linear 모드 UI
        if (linearTrackImage != null)
            linearTrackImage.gameObject.SetActive(isLinear);
        if (linearHoldStartImage != null)
            linearHoldStartImage.gameObject.SetActive(isLinear);
        if (linearHoldEndImage != null)
            linearHoldEndImage.gameObject.SetActive(isLinear);
        if (linearMarker != null)
            linearMarker.gameObject.SetActive(isLinear);
    }

    /// <summary>
    /// 현재 설정을 프리셋에 캡처
    /// </summary>
    public void CaptureCurrentSettings(AngleDisplayPreset preset)
    {
        preset.displayMode = displayMode;

        if (presetReferenceTransform != null)
        {
            preset.position = presetReferenceTransform.InverseTransformPoint(transform.position);
            preset.rotation = (Quaternion.Inverse(presetReferenceTransform.rotation) * transform.rotation).eulerAngles;
        }
        else
        {
            preset.position = transform.position;
            preset.rotation = transform.rotation.eulerAngles;
        }
        preset.scale = transform.localScale;
        preset.rotationAxis = rotationAxis;
        preset.startAngle = startAngle;
        preset.endAngle = endAngle;
        preset.invertAngle = invertAngle;
        preset.linearAxis = linearAxis;
        preset.linearTrackStart = linearTrackStart;
        preset.linearTrackEnd = linearTrackEnd;
        preset.syncSource = syncSource;
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

    [ContextMenu("Test Hold Range - Normal Mode")]
    private void TestHoldRangeNormal() => UpdateHoldRange(false);

    [ContextMenu("Test Hold Range - Extended Mode")]
    private void TestHoldRangeExtended() => UpdateHoldRange(true);
#endif
}
