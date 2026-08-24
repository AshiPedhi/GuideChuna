using UnityEngine;

/// <summary>
/// 경추 ROM 6방향을 목뼈에 직접 얹어 돌린다.
///
/// 애니메이션 클립으로 돌리면 두 가지가 걸린다 —
///  · 아바타 human 배열에 <b>CC_Base_NeckTwist02가 없어</b> 휴머노이드가 그 뼈를 모른다.
///    각도가 관절 2개에만 몰려 꺾이는 느낌이 난다(2026-08-24 실측: 전 클립에서 Neck2 = 0.0°).
///  · 각도를 근육값(-1~1)으로 다루게 돼 "45도"를 직접 지정할 수 없다.
///
/// 그래서 Animator가 포즈를 쓴 뒤(LateUpdate) 목뼈 3개에 회전을 <b>덧얹는다</b>.
/// 시작 자세(앉은 자세·팔)는 기존 대기 클립이 그대로 잡아 준다.
///
/// 진행은 두 구간으로 나뉜다.
///  · 능동  환자가 스스로 가는 데까지. 정상각에서 <see cref="overpressureAngle"/>만큼 못 미친다.
///  · 압박  시술자가 손으로 더 미는 구간. 능동 끝점부터 정상각까지.
/// </summary>
public class CervicalRomDriver : MonoBehaviour
{
    public enum Direction
    {
        None,
        Flexion,        // 굴곡
        Extension,      // 신전
        LateralRight,   // 우측굴
        LateralLeft,    // 좌측굴
        RotationRight,  // 우회전
        RotationLeft,   // 좌회전
    }

    [Header("=== 뼈 ===")]
    [Tooltip("기준 몸통. 회전축을 이 트랜스폼 기준으로 잡는다. 비우면 CC_Base_Spine02를 찾는다.")]
    [SerializeField] private Transform torso;

    [Tooltip("위에서부터 순서대로 목뼈. 비우면 CC_Base_NeckTwist01 / 02 / Head를 찾는다.")]
    [SerializeField] private Transform[] neckChain = new Transform[3];

    [Tooltip("각 뼈가 나눠 가질 비율. 합이 1이 되게 정규화한다.\n" +
             "아래쪽 뼈에 조금, 머리에 많이 주면 실제 경추 움직임에 가깝다.")]
    [SerializeField] private float[] boneWeights = { 0.30f, 0.25f, 0.45f };

    [Header("=== 각도 (임상 기준) ===")]
    [Tooltip("굴곡 정상 각도")] [SerializeField] private float flexionNormal = 45f;
    [Tooltip("신전 정상 각도")] [SerializeField] private float extensionNormal = 90f;
    [Tooltip("측굴 정상 각도 (좌우 공통)")] [SerializeField] private float lateralNormal = 45f;
    [Tooltip("회전 정상 각도 (좌우 공통)")] [SerializeField] private float rotationNormal = 90f;

    [Tooltip("압박으로 더 미는 양. 능동은 정상각에서 이만큼 못 미친 지점까지 간다.")]
    [SerializeField] private float overpressureAngle = 7f;

    [Header("=== 진행 ===")]
    [Tooltip("능동 구간을 자동으로 진행시킬 속도 (도/초). 0이면 외부에서 직접 넣는다.")]
    [SerializeField] private float activeSpeed = 12f;

    [Tooltip("중립으로 돌아오는 속도 (도/초)")]
    [SerializeField] private float returnSpeed = 20f;

    [Tooltip("각도 변화를 부드럽게 하는 시간 (초). 0이면 즉시 반영.")]
    [SerializeField] private float smoothTime = 0.08f;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = false;

    private Direction currentDirection = Direction.None;
    private float targetAngle;          // 목표 각도 (도)
    private float appliedAngle;         // 실제로 얹고 있는 각도 (도)
    private float angleVelocity;        // SmoothDamp용
    private bool autoAdvance;           // 능동 구간 자동 진행 중인가
    private float normalizedWeightSum;

    /// <summary>현재 얹고 있는 각도(도).</summary>
    public float CurrentAngle => appliedAngle;

    /// <summary>현재 방향의 정상 각도(도).</summary>
    public float NormalAngle => NormalAngleOf(currentDirection);

    /// <summary>능동 구간의 끝점. 정상각에서 압박량만큼 못 미친 각도.</summary>
    public float ActiveTargetAngle => Mathf.Max(0f, NormalAngle - overpressureAngle);

    /// <summary>능동 끝점에 도달했는가. 압박 단계로 넘어가도 되는 시점이다.</summary>
    public bool ActiveReached => currentDirection != Direction.None &&
                                 appliedAngle >= ActiveTargetAngle - 0.5f;

    /// <summary>정상각 대비 부족한 각도. 평가 단계에서 기록할 값이다.</summary>
    public float DeficitAngle => Mathf.Max(0f, NormalAngle - appliedAngle);

    private void Awake()
    {
        AutoFindBones();
        NormalizeWeights();
    }

    /// <summary>방향을 정하고 능동 구간을 시작한다. 자동으로 능동 끝점까지 간다.</summary>
    public void BeginActive(Direction direction)
    {
        currentDirection = direction;
        autoAdvance = true;
        targetAngle = ActiveTargetAngle;

        if (showDebugLogs)
        {
            ChunaLogger.Log($"<color=cyan>[CervicalROM] 능동 시작: {direction} → " +
                            $"{ActiveTargetAngle:F0}° (정상 {NormalAngle:F0}°)</color>");
        }
    }

    /// <summary>
    /// 압박 구간. 0이면 능동 끝점, 1이면 정상각이다.
    /// 시술자 손의 진행률을 그대로 넣으면 된다.
    /// </summary>
    public void SetOverpressure(float progress01)
    {
        if (currentDirection == Direction.None) return;

        autoAdvance = false;
        targetAngle = Mathf.Lerp(ActiveTargetAngle, NormalAngle, Mathf.Clamp01(progress01));
    }

    /// <summary>각도를 직접 지정한다(도).</summary>
    public void SetAngle(Direction direction, float degrees)
    {
        currentDirection = direction;
        autoAdvance = false;
        targetAngle = Mathf.Max(0f, degrees);
    }

    /// <summary>중립으로 되돌린다.</summary>
    public void ReturnToNeutral()
    {
        autoAdvance = false;
        targetAngle = 0f;
    }

    private void LateUpdate()
    {
        // Animator가 포즈를 쓴 다음에 덧얹어야 한다. Update에서 하면 애니메이션이 덮어쓴다.
        if (currentDirection == Direction.None) return;

        if (autoAdvance && activeSpeed > 0f)
        {
            targetAngle = Mathf.MoveTowards(targetAngle, ActiveTargetAngle, activeSpeed * Time.deltaTime);
        }

        if (smoothTime > 0f)
        {
            appliedAngle = Mathf.SmoothDamp(appliedAngle, targetAngle, ref angleVelocity, smoothTime);
        }
        else
        {
            appliedAngle = targetAngle;
        }

        if (targetAngle <= 0f && appliedAngle < 0.05f)
        {
            appliedAngle = 0f;
            currentDirection = Direction.None;
            return;
        }

        ApplyRotation(appliedAngle);
    }

    /// <summary>목뼈 3개에 각도를 나눠 얹는다. 뿌리부터 처리해야 합이 맞는다.</summary>
    private void ApplyRotation(float degrees)
    {
        if (torso == null || Mathf.Approximately(degrees, 0f)) return;

        Vector3 axis = AxisOf(currentDirection);
        if (axis == Vector3.zero) return;

        Vector3 worldAxis = torso.TransformDirection(axis);

        for (int i = 0; i < neckChain.Length; i++)
        {
            Transform bone = neckChain[i];
            if (bone == null) continue;

            float weight = i < boneWeights.Length ? boneWeights[i] : 0f;
            if (weight <= 0f) continue;

            // 월드 축 기준으로 덧회전. 부모를 먼저 돌리면 자식이 따라오므로
            // 뿌리→끝 순서로 처리하면 최종 머리 각도가 정확히 degrees가 된다.
            bone.rotation = Quaternion.AngleAxis(degrees * weight / normalizedWeightSum, worldAxis) * bone.rotation;
        }
    }

    /// <summary>
    /// 방향별 회전축 (몸통 로컬 기준).
    /// ★2026-08-24 클립 실측에서 나온 값이다 — 굴곡·신전 x축, 측굴 z축, 회전 y축.
    /// </summary>
    private static Vector3 AxisOf(Direction d)
    {
        switch (d)
        {
            case Direction.Flexion:        return Vector3.right;      // x +
            case Direction.Extension:      return Vector3.left;       // x −
            case Direction.LateralRight:   return Vector3.back;       // z −
            case Direction.LateralLeft:    return Vector3.forward;    // z +
            case Direction.RotationRight:  return Vector3.up;         // y +
            case Direction.RotationLeft:   return Vector3.down;       // y −
            default:                       return Vector3.zero;
        }
    }

    private float NormalAngleOf(Direction d)
    {
        switch (d)
        {
            case Direction.Flexion:       return flexionNormal;
            case Direction.Extension:     return extensionNormal;
            case Direction.LateralRight:
            case Direction.LateralLeft:   return lateralNormal;
            case Direction.RotationRight:
            case Direction.RotationLeft:  return rotationNormal;
            default:                      return 0f;
        }
    }

    private void NormalizeWeights()
    {
        normalizedWeightSum = 0f;
        for (int i = 0; i < boneWeights.Length && i < neckChain.Length; i++)
        {
            if (neckChain[i] != null) normalizedWeightSum += boneWeights[i];
        }
        if (normalizedWeightSum <= 0f) normalizedWeightSum = 1f;
    }

    private void AutoFindBones()
    {
        if (torso == null) torso = FindDeep(transform, "CC_Base_Spine02");

        string[] names = { "CC_Base_NeckTwist01", "CC_Base_NeckTwist02", "CC_Base_Head" };
        if (neckChain == null || neckChain.Length < names.Length)
        {
            neckChain = new Transform[names.Length];
        }
        for (int i = 0; i < names.Length; i++)
        {
            if (neckChain[i] == null) neckChain[i] = FindDeep(transform, names[i]);
        }

        if (torso == null || neckChain[0] == null || neckChain[2] == null)
        {
            ChunaLogger.LogWarning("[CervicalROM] 목 사슬을 찾지 못했습니다. " +
                                   "환자 루트에 붙였는지, 뼈 이름이 CC_Base_* 인지 확인하세요.");
        }
    }

    private static Transform FindDeep(Transform root, string boneName)
    {
        if (root.name == boneName) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeep(root.GetChild(i), boneName);
            if (found != null) return found;
        }
        return null;
    }

#if UNITY_EDITOR
    [ContextMenu("테스트 — 굴곡 능동")]
    private void TestFlexion() => BeginActive(Direction.Flexion);

    [ContextMenu("테스트 — 신전 능동")]
    private void TestExtension() => BeginActive(Direction.Extension);

    [ContextMenu("테스트 — 우회전 능동")]
    private void TestRotationRight() => BeginActive(Direction.RotationRight);

    [ContextMenu("테스트 — 압박 100%")]
    private void TestOverpressure() => SetOverpressure(1f);

    [ContextMenu("테스트 — 중립 복귀")]
    private void TestNeutral() => ReturnToNeutral();
#endif
}
