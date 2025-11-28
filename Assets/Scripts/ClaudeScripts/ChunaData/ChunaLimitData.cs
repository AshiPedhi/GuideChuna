using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 추나 시술별 제한 한계 데이터
/// 각 시술의 허용 가능한 범위를 정의
/// ScriptableObject로 에디터에서 쉽게 수정 가능
/// </summary>
[CreateAssetMenu(fileName = "ChunaLimitData", menuName = "Chuna/Limit Data", order = 1)]
public class ChunaLimitData : ScriptableObject
{
    [Header("=== 시술 정보 ===")]
    [SerializeField] private string procedureName = "기본 시술";
    [SerializeField] private ChunaType procedureType = ChunaType.IsometricExercise;
    [SerializeField] [TextArea(2, 4)] private string description = "";

    [Header("=== 목 회전 한계 (도 단위) ===")]
    [Tooltip("목 굴곡(앞으로 숙이기) 최대 각도")]
    [SerializeField] [Range(0f, 90f)] private float maxNeckFlexion = 45f;

    [Tooltip("목 신전(뒤로 젖히기) 최대 각도")]
    [SerializeField] [Range(0f, 90f)] private float maxNeckExtension = 45f;

    [Tooltip("목 좌측 회전 최대 각도")]
    [SerializeField] [Range(0f, 90f)] private float maxNeckRotationLeft = 60f;

    [Tooltip("목 우측 회전 최대 각도")]
    [SerializeField] [Range(0f, 90f)] private float maxNeckRotationRight = 60f;

    [Tooltip("목 좌측 측굴 최대 각도")]
    [SerializeField] [Range(0f, 90f)] private float maxNeckLateralFlexionLeft = 40f;

    [Tooltip("목 우측 측굴 최대 각도")]
    [SerializeField] [Range(0f, 90f)] private float maxNeckLateralFlexionRight = 40f;

    [Header("=== 손목 회전 한계 (도 단위) ===")]
    [Tooltip("손목 굴곡 최대 각도")]
    [SerializeField] [Range(0f, 90f)] private float maxWristFlexion = 80f;

    [Tooltip("손목 신전 최대 각도")]
    [SerializeField] [Range(0f, 90f)] private float maxWristExtension = 70f;

    [Tooltip("손목 내전(요측편위) 최대 각도")]
    [SerializeField] [Range(0f, 45f)] private float maxWristRadialDeviation = 20f;

    [Tooltip("손목 외전(척측편위) 최대 각도")]
    [SerializeField] [Range(0f, 45f)] private float maxWristUlnarDeviation = 30f;

    [Tooltip("손목 회내(pronation) 최대 각도")]
    [SerializeField] [Range(0f, 90f)] private float maxWristPronation = 80f;

    [Tooltip("손목 회외(supination) 최대 각도")]
    [SerializeField] [Range(0f, 90f)] private float maxWristSupination = 80f;

    [Header("=== 손 위치 한계 (미터 단위) ===")]
    [Tooltip("손 전방 이동 최대 거리")]
    [SerializeField] [Range(0f, 1f)] private float maxHandForwardDistance = 0.5f;

    [Tooltip("손 후방 이동 최대 거리")]
    [SerializeField] [Range(0f, 1f)] private float maxHandBackwardDistance = 0.3f;

    [Tooltip("손 측면 이동 최대 거리")]
    [SerializeField] [Range(0f, 1f)] private float maxHandLateralDistance = 0.4f;

    [Tooltip("손 상하 이동 최대 거리")]
    [SerializeField] [Range(0f, 1f)] private float maxHandVerticalDistance = 0.4f;

    [Header("=== 힘/압력 한계 ===")]
    [Tooltip("적용 가능한 최대 힘 (N)")]
    [SerializeField] [Range(0f, 100f)] private float maxAppliedForce = 50f;

    [Tooltip("권장 힘 범위 최소값")]
    [SerializeField] [Range(0f, 50f)] private float recommendedForceMin = 10f;

    [Tooltip("권장 힘 범위 최대값")]
    [SerializeField] [Range(0f, 100f)] private float recommendedForceMax = 30f;

    [Header("=== 속도 한계 ===")]
    [Tooltip("최대 이동 속도 (m/s)")]
    [SerializeField] [Range(0f, 2f)] private float maxMovementSpeed = 0.5f;

    [Tooltip("최대 회전 속도 (deg/s)")]
    [SerializeField] [Range(0f, 180f)] private float maxRotationSpeed = 60f;

    [Header("=== 감점 설정 ===")]
    [Tooltip("경미한 위반 감점")]
    [SerializeField] [Range(0f, 10f)] private float minorViolationDeduction = 1f;

    [Tooltip("중간 위반 감점")]
    [SerializeField] [Range(0f, 20f)] private float moderateViolationDeduction = 5f;

    [Tooltip("심각한 위반 감점")]
    [SerializeField] [Range(0f, 50f)] private float severeViolationDeduction = 15f;

    [Tooltip("위험 위반 감점")]
    [SerializeField] [Range(0f, 100f)] private float dangerousViolationDeduction = 30f;

    [Header("=== 경고 임계값 ===")]
    [Tooltip("경고 시작 비율 (한계의 몇 % 도달 시 경고)")]
    [SerializeField] [Range(0.5f, 0.95f)] private float warningThresholdRatio = 0.8f;

    [Tooltip("위험 시작 비율 (한계의 몇 % 도달 시 위험)")]
    [SerializeField] [Range(0.85f, 1f)] private float dangerThresholdRatio = 0.95f;

    [Header("=== 되돌리기 설정 ===")]
    [Tooltip("자동 되돌리기 활성화")]
    [SerializeField] private bool enableAutoRevert = true;

    [Tooltip("되돌리기 시작 비율 (한계의 몇 % 초과 시 되돌리기)")]
    [SerializeField] [Range(0.9f, 1.2f)] private float revertTriggerRatio = 1.0f;

    [Tooltip("되돌리기 목표 비율 (한계의 몇 % 위치로 복귀)")]
    [SerializeField] [Range(0.5f, 0.9f)] private float revertTargetRatio = 0.7f;

    [Tooltip("되돌리기 보간 속도")]
    [SerializeField] [Range(1f, 10f)] private float revertLerpSpeed = 3f;

    [Header("=== 관절별 세부 한계 ===")]
    [SerializeField] private List<JointLimit> jointLimits = new List<JointLimit>();

    // ========== 프로퍼티 ==========
    public string ProcedureName => procedureName;
    public ChunaType ProcedureType => procedureType;
    public string Description => description;

    // 목 한계
    public float MaxNeckFlexion => maxNeckFlexion;
    public float MaxNeckExtension => maxNeckExtension;
    public float MaxNeckRotationLeft => maxNeckRotationLeft;
    public float MaxNeckRotationRight => maxNeckRotationRight;
    public float MaxNeckLateralFlexionLeft => maxNeckLateralFlexionLeft;
    public float MaxNeckLateralFlexionRight => maxNeckLateralFlexionRight;

    // 손목 한계
    public float MaxWristFlexion => maxWristFlexion;
    public float MaxWristExtension => maxWristExtension;
    public float MaxWristRadialDeviation => maxWristRadialDeviation;
    public float MaxWristUlnarDeviation => maxWristUlnarDeviation;
    public float MaxWristPronation => maxWristPronation;
    public float MaxWristSupination => maxWristSupination;

    // 손 위치 한계
    public float MaxHandForwardDistance => maxHandForwardDistance;
    public float MaxHandBackwardDistance => maxHandBackwardDistance;
    public float MaxHandLateralDistance => maxHandLateralDistance;
    public float MaxHandVerticalDistance => maxHandVerticalDistance;

    // 힘/압력 한계
    public float MaxAppliedForce => maxAppliedForce;
    public float RecommendedForceMin => recommendedForceMin;
    public float RecommendedForceMax => recommendedForceMax;

    // 속도 한계
    public float MaxMovementSpeed => maxMovementSpeed;
    public float MaxRotationSpeed => maxRotationSpeed;

    // 감점 설정
    public float MinorViolationDeduction => minorViolationDeduction;
    public float ModerateViolationDeduction => moderateViolationDeduction;
    public float SevereViolationDeduction => severeViolationDeduction;
    public float DangerousViolationDeduction => dangerousViolationDeduction;

    // 경고 임계값
    public float WarningThresholdRatio => warningThresholdRatio;
    public float DangerThresholdRatio => dangerThresholdRatio;

    // 되돌리기 설정
    public bool EnableAutoRevert => enableAutoRevert;
    public float RevertTriggerRatio => revertTriggerRatio;
    public float RevertTargetRatio => revertTargetRatio;
    public float RevertLerpSpeed => revertLerpSpeed;

    // 관절별 한계
    public List<JointLimit> JointLimits => jointLimits;

    /// <summary>
    /// 특정 관절의 한계값 가져오기
    /// </summary>
    public JointLimit GetJointLimit(int jointId)
    {
        return jointLimits.Find(j => j.jointId == jointId);
    }

    /// <summary>
    /// 위반 심각도에 따른 감점 가져오기
    /// </summary>
    public float GetDeductionForSeverity(ViolationSeverity severity)
    {
        switch (severity)
        {
            case ViolationSeverity.Minor:
                return minorViolationDeduction;
            case ViolationSeverity.Moderate:
                return moderateViolationDeduction;
            case ViolationSeverity.Severe:
                return severeViolationDeduction;
            case ViolationSeverity.Dangerous:
                return dangerousViolationDeduction;
            default:
                return 0f;
        }
    }

    /// <summary>
    /// 한계 비율에 따른 상태 판정
    /// </summary>
    public LimitStatus GetLimitStatus(float currentRatio)
    {
        if (currentRatio >= revertTriggerRatio)
            return LimitStatus.Exceeded;
        if (currentRatio >= dangerThresholdRatio)
            return LimitStatus.Danger;
        if (currentRatio >= warningThresholdRatio)
            return LimitStatus.Warning;
        return LimitStatus.Safe;
    }
}

/// <summary>
/// 추나 시술 종류
/// </summary>
public enum ChunaType
{
    [Tooltip("건측회전 - 건강한 쪽 회전")]
    HealthySideRotation,

    [Tooltip("환측회전 - 아픈 쪽 회전")]
    AffectedSideRotation,

    [Tooltip("등척성운동 - 저항 운동")]
    IsometricExercise,

    [Tooltip("측굴 - 옆으로 굽히기")]
    LateralFlexion
}

/// <summary>
/// 위반 심각도
/// </summary>
public enum ViolationSeverity
{
    [Tooltip("없음")]
    None = 0,

    [Tooltip("경미 - 한계의 80-95%")]
    Minor = 1,

    [Tooltip("중간 - 한계의 95-100%")]
    Moderate = 2,

    [Tooltip("심각 - 한계의 100-110%")]
    Severe = 3,

    [Tooltip("위험 - 한계의 110% 초과")]
    Dangerous = 4
}

/// <summary>
/// 한계 상태
/// </summary>
public enum LimitStatus
{
    [Tooltip("안전 - 한계 내")]
    Safe,

    [Tooltip("경고 - 한계 근접")]
    Warning,

    [Tooltip("위험 - 한계 임박")]
    Danger,

    [Tooltip("초과 - 한계 초과")]
    Exceeded
}

/// <summary>
/// 위반 유형
/// </summary>
public enum ViolationType
{
    None,
    OverFlexion,        // 과굴곡
    OverExtension,      // 과신전
    OverRotation,       // 과회전
    OverLateralFlexion, // 과측굴
    OverTranslation,    // 과이동
    OverSpeed,          // 과속
    OverForce           // 과압력
}

/// <summary>
/// 관절별 한계 설정
/// </summary>
[System.Serializable]
public class JointLimit
{
    [Tooltip("관절 ID (HandJointId)")]
    public int jointId;

    [Tooltip("관절 이름")]
    public string jointName;

    [Header("회전 한계 (도)")]
    [Tooltip("X축 최소 회전")]
    public float minRotationX = -45f;
    [Tooltip("X축 최대 회전")]
    public float maxRotationX = 45f;

    [Tooltip("Y축 최소 회전")]
    public float minRotationY = -45f;
    [Tooltip("Y축 최대 회전")]
    public float maxRotationY = 45f;

    [Tooltip("Z축 최소 회전")]
    public float minRotationZ = -45f;
    [Tooltip("Z축 최대 회전")]
    public float maxRotationZ = 45f;

    [Header("위치 한계 (미터)")]
    [Tooltip("X축 최소 위치")]
    public float minPositionX = -0.5f;
    [Tooltip("X축 최대 위치")]
    public float maxPositionX = 0.5f;

    [Tooltip("Y축 최소 위치")]
    public float minPositionY = -0.5f;
    [Tooltip("Y축 최대 위치")]
    public float maxPositionY = 0.5f;

    [Tooltip("Z축 최소 위치")]
    public float minPositionZ = -0.5f;
    [Tooltip("Z축 최대 위치")]
    public float maxPositionZ = 0.5f;

    [Header("가중치")]
    [Tooltip("이 관절의 중요도 (1.0 = 기본)")]
    [Range(0.5f, 3f)]
    public float weight = 1.0f;

    [Tooltip("이 관절 위반 시 즉시 되돌리기")]
    public bool criticalJoint = false;

    /// <summary>
    /// 회전값이 한계 내에 있는지 확인
    /// </summary>
    public bool IsRotationWithinLimits(Vector3 eulerAngles)
    {
        return eulerAngles.x >= minRotationX && eulerAngles.x <= maxRotationX &&
               eulerAngles.y >= minRotationY && eulerAngles.y <= maxRotationY &&
               eulerAngles.z >= minRotationZ && eulerAngles.z <= maxRotationZ;
    }

    /// <summary>
    /// 위치값이 한계 내에 있는지 확인
    /// </summary>
    public bool IsPositionWithinLimits(Vector3 position)
    {
        return position.x >= minPositionX && position.x <= maxPositionX &&
               position.y >= minPositionY && position.y <= maxPositionY &&
               position.z >= minPositionZ && position.z <= maxPositionZ;
    }

    /// <summary>
    /// 회전값의 한계 대비 비율 계산 (가장 큰 비율 반환)
    /// </summary>
    public float GetRotationLimitRatio(Vector3 eulerAngles)
    {
        float ratioX = GetAxisRatio(eulerAngles.x, minRotationX, maxRotationX);
        float ratioY = GetAxisRatio(eulerAngles.y, minRotationY, maxRotationY);
        float ratioZ = GetAxisRatio(eulerAngles.z, minRotationZ, maxRotationZ);
        return Mathf.Max(ratioX, ratioY, ratioZ);
    }

    /// <summary>
    /// 위치값의 한계 대비 비율 계산 (가장 큰 비율 반환)
    /// </summary>
    public float GetPositionLimitRatio(Vector3 position)
    {
        float ratioX = GetAxisRatio(position.x, minPositionX, maxPositionX);
        float ratioY = GetAxisRatio(position.y, minPositionY, maxPositionY);
        float ratioZ = GetAxisRatio(position.z, minPositionZ, maxPositionZ);
        return Mathf.Max(ratioX, ratioY, ratioZ);
    }

    /// <summary>
    /// 단일 축의 한계 대비 비율 계산
    /// </summary>
    private float GetAxisRatio(float value, float min, float max)
    {
        if (value >= 0)
        {
            return max > 0 ? value / max : 0;
        }
        else
        {
            return min < 0 ? value / min : 0;
        }
    }
}
