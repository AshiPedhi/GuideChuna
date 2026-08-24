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

    [Header("=== 압박 여유 구간 ===")]
    [Tooltip("압박으로 더 미는 양. 능동은 정상각에서 이만큼 못 미친 지점까지 간다.\n" +
             "randomizePerLoad를 켜면 이 값 대신 아래 범위에서 방향마다 따로 뽑는다.")]
    [SerializeField] private float overpressureAngle = 7f;

    [Tooltip("시나리오를 불러올 때마다 여유 구간을 방향별로 다시 뽑는다.\n" +
             "매번 같은 지점에서 멈추면 끝느낌을 느끼는 게 아니라 위치를 외우게 된다.\n" +
             "방향마다 값이 다르므로 좌우 비대칭도 자연스럽게 생긴다.")]
    [SerializeField] private bool randomizePerLoad = true;

    [Tooltip("여유 구간을 뽑을 범위 (도). x=최소, y=최대")]
    [SerializeField] private Vector2 overpressureRange = new Vector2(5f, 15f);

    [Tooltip("0이 아니면 그 값을 시드로 써서 매번 같은 값이 나온다. 재현이 필요할 때만 쓴다.")]
    [SerializeField] private int randomSeed = 0;

    [Header("=== 진행 ===")]
    [Tooltip("능동 구간 진행 속도 (도/초). 굴곡 38°면 약 2.5초.")]
    [SerializeField] private float activeSpeed = 15f;

    [Tooltip("중립으로 돌아오는 속도 (도/초). 갈 때보다 조금 느린 편이 자연스럽다.")]
    [SerializeField] private float returnSpeed = 12f;

    [Tooltip("각도 변화를 부드럽게 하는 시간 (초). " +
             "★속도를 만드는 값이 아니라 방향이 바뀌는 순간의 각을 없애는 값이다. " +
             "크게 올리면 손 움직임과 머리가 어긋난다.")]
    [SerializeField] private float smoothTime = 0.08f;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = false;

    private const int DirectionCount = 7;   // None 포함. Direction을 인덱스로 쓴다.

    private float[] gaps;                   // 방향별 여유 구간(도). 시나리오 로드 때 뽑는다.
    private Direction currentDirection = Direction.None;
    private float targetAngle;          // 최종 목표 각도 (도)
    private float commandedAngle;       // 속도 제한을 거친 중간 목표 (도)
    private float appliedAngle;         // 실제로 얹고 있는 각도 (도)
    private float angleVelocity;        // SmoothDamp용
    private float ramifySpeed;          // 지금 적용할 진행 속도 (도/초). 0이면 즉시 추종.
    private float normalizedWeightSum;

    /// <summary>현재 얹고 있는 각도(도).</summary>
    public float CurrentAngle => appliedAngle;

    /// <summary>현재 방향의 정상 각도(도).</summary>
    public float NormalAngle => NormalAngleOf(currentDirection);

    /// <summary>능동 구간의 끝점. 정상각에서 이번 판의 여유 구간만큼 못 미친 각도.</summary>
    public float ActiveTargetAngle => Mathf.Max(0f, NormalAngle - GapOf(currentDirection));

    /// <summary>이번 판에 뽑힌 여유 구간(도). 방향마다 다르다.</summary>
    public float CurrentGap => GapOf(currentDirection);

    /// <summary>능동 끝점에 도달했는가. 압박 단계로 넘어가도 되는 시점이다.</summary>
    public bool ActiveReached => currentDirection != Direction.None &&
                                 appliedAngle >= ActiveTargetAngle - 0.5f;

    /// <summary>정상각 대비 부족한 각도. 평가 단계에서 기록할 값이다.</summary>
    public float DeficitAngle => Mathf.Max(0f, NormalAngle - appliedAngle);

    private void Awake()
    {
        AutoFindBones();
        NormalizeWeights();
        RandomizeGaps();
    }

    /// <summary>
    /// 여유 구간을 방향별로 다시 뽑는다. 시나리오를 불러올 때 한 번 부르면 된다.
    /// randomizePerLoad가 꺼져 있으면 고정값(overpressureAngle)으로 채운다.
    /// </summary>
    public void RandomizeGaps()
    {
        if (gaps == null || gaps.Length != DirectionCount) gaps = new float[DirectionCount];

        if (!randomizePerLoad)
        {
            for (int i = 0; i < gaps.Length; i++) gaps[i] = overpressureAngle;
            return;
        }

        float min = Mathf.Min(overpressureRange.x, overpressureRange.y);
        float max = Mathf.Max(overpressureRange.x, overpressureRange.y);

        // 시드를 주면 재현된다. 0이면 매번 다르게 뽑는다.
        Random.State previous = default;
        bool seeded = randomSeed != 0;
        if (seeded)
        {
            previous = Random.state;
            Random.InitState(randomSeed);
        }

        for (int i = 0; i < gaps.Length; i++) gaps[i] = Random.Range(min, max);

        if (seeded) Random.state = previous;

        if (showDebugLogs)
        {
            ChunaLogger.Log($"<color=cyan>[CervicalROM] 여유 구간 재추첨 ({min:F0}~{max:F0}°) — " +
                            $"굴곡 {gaps[(int)Direction.Flexion]:F1}° · 신전 {gaps[(int)Direction.Extension]:F1}° · " +
                            $"우측굴 {gaps[(int)Direction.LateralRight]:F1}° · 좌측굴 {gaps[(int)Direction.LateralLeft]:F1}° · " +
                            $"우회전 {gaps[(int)Direction.RotationRight]:F1}° · 좌회전 {gaps[(int)Direction.RotationLeft]:F1}°</color>");
        }
    }

    private float GapOf(Direction d)
    {
        if (d == Direction.None) return 0f;
        if (gaps == null || (int)d >= gaps.Length) return overpressureAngle;
        return gaps[(int)d];
    }

    /// <summary>방향을 정하고 능동 구간을 시작한다. 자동으로 능동 끝점까지 간다.</summary>
    public void BeginActive(Direction direction)
    {
        currentDirection = direction;
        targetAngle = ActiveTargetAngle;
        ramifySpeed = activeSpeed;       // ★즉시 대입하면 안 된다. 속도로 밀어야 부드럽다.

        if (showDebugLogs)
        {
            ChunaLogger.Log($"<color=cyan>[CervicalROM] 능동 시작: {direction} → " +
                            $"{ActiveTargetAngle:F0}° (정상 {NormalAngle:F0}°, 여유 {CurrentGap:F1}°)</color>");
        }
    }

    /// <summary>
    /// 압박 구간. 0이면 능동 끝점, 1이면 정상각이다.
    /// 시술자 손의 진행률을 그대로 넣으면 된다.
    /// </summary>
    public void SetOverpressure(float progress01)
    {
        if (currentDirection == Direction.None) return;

        // 압박은 시술자 손을 즉시 따라가야 한다. 속도 제한을 걸면 손과 머리가 어긋난다.
        ramifySpeed = 0f;
        targetAngle = Mathf.Lerp(ActiveTargetAngle, NormalAngle, Mathf.Clamp01(progress01));
    }

    /// <summary>각도를 직접 지정한다(도).</summary>
    public void SetAngle(Direction direction, float degrees)
    {
        currentDirection = direction;
        ramifySpeed = 0f;
        targetAngle = Mathf.Max(0f, degrees);
    }

    /// <summary>중립으로 되돌린다. returnSpeed로 천천히 내려온다.</summary>
    public void ReturnToNeutral()
    {
        targetAngle = 0f;
        ramifySpeed = returnSpeed;
    }

    private void LateUpdate()
    {
        // Animator가 포즈를 쓴 다음에 덧얹어야 한다. Update에서 하면 애니메이션이 덮어쓴다.
        if (currentDirection == Direction.None) return;

        // 1단계 — 속도 제한. 능동·복귀는 도/초로 밀고, 압박은 손을 즉시 따라간다.
        if (ramifySpeed > 0f)
        {
            commandedAngle = Mathf.MoveTowards(commandedAngle, targetAngle, ramifySpeed * Time.deltaTime);
        }
        else
        {
            commandedAngle = targetAngle;
        }

        // 2단계 — 감쇠. 방향이 바뀌는 순간의 각을 없애는 용도지 속도를 만드는 용도가 아니다.
        if (smoothTime > 0f)
        {
            appliedAngle = Mathf.SmoothDamp(appliedAngle, commandedAngle, ref angleVelocity, smoothTime);
        }
        else
        {
            appliedAngle = commandedAngle;
        }

        if (targetAngle <= 0f && commandedAngle <= 0f && appliedAngle < 0.05f)
        {
            appliedAngle = 0f;
            commandedAngle = 0f;
            angleVelocity = 0f;
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
            // ★부호는 Play에서 눈으로 확인해 뒤집었다(2026-08-24). 클립 실측의 축 부호를
            //   그대로 옮겼더니 굴곡·신전이 반대로 나왔다. 측굴·회전은 그대로 맞았다.
            case Direction.Flexion:        return Vector3.left;       // x −
            case Direction.Extension:      return Vector3.right;      // x +
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
    [ContextMenu("테스트 — 1 굴곡 능동")]
    private void TestFlexion() => BeginActive(Direction.Flexion);

    [ContextMenu("테스트 — 2 신전 능동")]
    private void TestExtension() => BeginActive(Direction.Extension);

    [ContextMenu("테스트 — 3 우측굴 능동")]
    private void TestLateralRight() => BeginActive(Direction.LateralRight);

    [ContextMenu("테스트 — 4 좌측굴 능동")]
    private void TestLateralLeft() => BeginActive(Direction.LateralLeft);

    [ContextMenu("테스트 — 5 우회전 능동")]
    private void TestRotationRight() => BeginActive(Direction.RotationRight);

    [ContextMenu("테스트 — 6 좌회전 능동")]
    private void TestRotationLeft() => BeginActive(Direction.RotationLeft);

    [ContextMenu("테스트 — 압박 100%")]
    private void TestOverpressure() => SetOverpressure(1f);

    [ContextMenu("테스트 — 중립 복귀")]
    private void TestNeutral() => ReturnToNeutral();

    [ContextMenu("테스트 — 여유 구간 다시 뽑기")]
    private void TestRandomize()
    {
        bool prev = showDebugLogs;
        showDebugLogs = true;
        RandomizeGaps();
        showDebugLogs = prev;
    }
#endif
}
