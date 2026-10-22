// ★시험 전용(0단계) · 2026-10-22
//   목적: 씬 뷰에서 목표 구를 끌면 환자 팔이 2관절 IK로 따라가는지만 본다.
//   범위 밖: 손 파지로 목표 잡기 · 판정 · 끝느낌 진동 · 놓으면 복귀 · 어깨 원뿔 한계 · 중심선 클램프.
//   붙이는 법: Play 중 메뉴 GuideChuna/시험/환자 팔 IK 0단계 (PatientArmIkProbeMenu).
//   씬·프리팹에 올리지 않는다. 런타임에 붙으므로 Play를 멈추면 사라지고, 인스펙터 튜닝도 저장되지 않는다.
//   기존 공유 컴포넌트(ScenarioManager·ConditionManager 등)를 부르지도 고치지도 않는다.
//
// ── 1단계 이후 설계 메모(구현 안 함) ──
//   · 목표 = 사용자 손(파지 중일 때만). 손 떨림은 목표 쪽에서 평활화한다(IK 안에서 하지 않는다).
//   · 어깨 원뿔 한계: S공간에서 (팔꿈치 − 어깨) 방향을 기준 방향 원뿔 안으로 자른 뒤 IK를 푼다.
//   · 중심선 클램프: 몸통 중앙면(정중면)의 어느 쪽인지 «부호»가 필요하다. c8 음수 스케일 때문에
//     본 이름으로 좌우를 정하지 말고 rom-frame-verify처럼 재는 도구로 먼저 정한다(CLAUDE.md 규칙 9).
//   · 끝느낌: 원뿔 경계 근처에서 목표와 실제 손 사이 거리로 진동·저항 표시를 낸다.
//   · 복귀: 놓으면 weight를 1 → 0으로 내린다(애니가 매 프레임 덮어쓴다는 전제. 아래 «멱등성» 참고).
using UnityEngine;

/// <summary>
/// 환자 팔(위팔 → 아래팔 → 손)에 해석적 2관절 IK를 LateUpdate에서 덧얹는다.
/// <see cref="CervicalRomDriver"/>와 같은 결 — Animator가 포즈를 쓴 뒤 뼈를 직접 돌린다.
///
/// ★음수 스케일 처리 (2026-10-22 실측 근거)
///   환자 c9/c8 중 <b>c8의 localScale X가 −1.1</b>이고 팔 체인 전체가 그 밑이다
///   (bone_axes.py: CC_Base_R_Upperarm·Forearm·Hand 누적스케일 (−1.1, 1.1, 1.1)).
///   Unity의 Transform.rotation은 스케일 부호를 무시하고 로컬 회전을 곱한 값이라,
///   거울 밑에서 <c>bone.rotation = q * bone.rotation</c>으로 «월드 q»를 주면 실제 화면에서는
///   거울에 비친 회전(축이 반사되고 방향이 뒤집힌 회전)이 된다.
///   CervicalRomDriver.AxisOf에 «부호는 Play에서 눈으로 확인해 뒤집었다(08-24)»가 남아 있는 것이 그 흔적이다.
///   → 그래서 여기서는 월드가 아니라 <b>위팔의 부모 공간(S)</b>에서 푼다.
///     · 위치는 S.InverseTransformPoint로 옮긴다 — 행렬 전체(거울 포함)를 역으로 거치므로 정확하다.
///     · 회전은 S 기준 상대 회전 rel = Inverse(S.rotation) * bone.rotation에 FromToRotation을 곱한다.
///       S 아래(위팔·아래팔·손)에 음수 스케일이 없으면 이 공간에는 거울이 없다 — 시작 때 검사하고, 있으면 멈춘다.
///   이렇게 하면 거울이 S 위 어디에 있든(몇 개든) 방향 부호를 추론할 필요가 없다.
///
/// ★멱등성: 해는 «목표 위치로 가는 최소 회전»이라, Animator가 그 프레임에 팔을 덮어쓰지 않아도
///   weight=1이면 같은 자세로 수렴한다(누적 폭주 없음). weight&lt;1이면 덮어쓰지 않는 경우 매 프레임
///   조금씩 더 가서 결국 weight=1처럼 된다 — 0단계에서는 로그로만 확인한다.
/// </summary>
[DefaultExecutionOrder(10000)] // 다른 LateUpdate(목뼈 드라이버 등)가 끝난 뒤에 얹는다
public class PatientArmIkProbe : MonoBehaviour
{
    public enum ArmSide { Right = 0, Left = 1 }

    [Tooltip("어느 팔인가(Humanoid 기준). 제2늑골 클립이 움직이는 쪽은 Right다(2026-10-22 클립 커브 실측). 시작 때만 읽는다.")]
    [SerializeField] private ArmSide side = ArmSide.Right;

    [Tooltip("IK 가중치. 0 = 애니 그대로, 1 = 목표에 완전히 붙음.")]
    [Range(0f, 1f)]
    [SerializeField] private float weight = 1f;

    [Tooltip("손 목표. 비어 있으면 Play 시작 때 손 위치에 빨간 구를 만든다.")]
    [SerializeField] private Transform target;

    [Tooltip("팔꿈치가 향할 쪽. 비어 있으면 Play 시작 때 지금 팔꿈치 바깥쪽에 파란 구를 만든다.")]
    [SerializeField] private Transform hint;

    [Tooltip("위팔을 제 축으로 비틀어 팔꿈치 굽힘면을 힌트 쪽에 맞춘다. 끄면 아래팔이 경첩이 아닌 방향으로도 꺾일 수 있다.")]
    [SerializeField] private bool alignElbowTwist = true;

    [Tooltip("자동으로 만드는 구의 지름(m).")]
    [SerializeField] private float handleSize = 0.05f;

    [Tooltip("진행 로그 간격(초).")]
    [SerializeField] private float logInterval = 1f;

    private Animator animator;
    private Transform upper, lower, hand, space;
    private bool ready;
    private float nextLogTime;

    // 로그·기즈모용 마지막 값(매 프레임 float만 갱신한다 — 문자열은 로그 시점에만 만든다)
    private float lastTargetDistance, lastReach, lastElbowAngle;
    private bool lastReachable;

    /// <summary>메뉴가 AddComponent 직후(Start 전에) 팔을 고를 때 쓴다.</summary>
    public ArmSide Side { get => side; set { if (!ready) side = value; } }

    private void Start()
    {
        animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInParent<Animator>();
        if (animator == null) { Debug.LogWarning("[팔IK] Animator가 없다 — 멈춘다. " + name); enabled = false; return; }
        if (!animator.isHuman) { Debug.LogWarning("[팔IK] Animator가 Humanoid가 아니다 — 멈춘다. " + animator.name); enabled = false; return; }

        bool right = side == ArmSide.Right;
        upper = animator.GetBoneTransform(right ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
        lower = animator.GetBoneTransform(right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
        hand = animator.GetBoneTransform(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
        if (upper == null || lower == null || hand == null)
        {
            Debug.LogWarning($"[팔IK] 팔 본을 못 찾았다 — 멈춘다. side={side} upper={NameOf(upper)} lower={NameOf(lower)} hand={NameOf(hand)}");
            enabled = false; return;
        }
        if (!lower.IsChildOf(upper) || !hand.IsChildOf(lower))
        {
            Debug.LogWarning($"[팔IK] 체인이 위팔 → 아래팔 → 손 순서의 부모·자식이 아니다 — 멈춘다. {upper.name}/{lower.name}/{hand.name}");
            enabled = false; return;
        }

        space = upper.parent;
        if (space == null) { Debug.LogWarning("[팔IK] 위팔에 부모가 없다 — 멈춘다."); enabled = false; return; }

        // S(위팔의 부모) 아래 체인에 음수 스케일이 있으면 S공간 풀이가 성립하지 않는다.
        for (Transform t = hand; t != space; t = t.parent)
        {
            Vector3 s = t.localScale;
            if (s.x < 0f || s.y < 0f || s.z < 0f)
            {
                Debug.LogWarning($"[팔IK] 체인 안 {t.name}에 음수 localScale {s} — S공간 풀이 불가, 멈춘다.");
                enabled = false; return;
            }
        }

        if (target == null) target = MakeHandle("[팔IK] 목표", hand.position, new Color(1f, 0.25f, 0.2f));
        if (hint == null) hint = MakeHandle("[팔IK] 힌트(팔꿈치)", DefaultHintPosition(), new Color(0.25f, 0.5f, 1f));

        Vector3 ls = upper.lossyScale;
        float det = ls.x * ls.y * ls.z;
        float l1 = Vector3.Distance(upper.position, lower.position);
        float l2 = Vector3.Distance(lower.position, hand.position);
        Debug.Log($"[팔IK] 시작 — 환자={animator.name} side={side} 본={upper.name} → {lower.name} → {hand.name} " +
                  $"위팔 {l1:F3}m · 아래팔 {l2:F3}m(월드) · 위팔 lossyScale={ls} 부호={(det < 0f ? "음(거울)" : "양")} · " +
                  $"풀이 공간 S={space.name} · weight={weight:F2} · 목표={target.name} · 힌트={hint.name}");

        ready = true;
        nextLogTime = Time.unscaledTime + logInterval;
    }

    private Transform MakeHandle(string handleName, Vector3 worldPos, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = handleName;
        // 콜라이더는 뺀다 — 환자 접촉 판정·손 충돌에 끼어들지 않게.
        Collider col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);
        go.transform.position = worldPos;
        go.transform.localScale = Vector3.one * handleSize;
        Renderer r = go.GetComponent<Renderer>();
        if (r != null) r.material.color = color; // 시작 때 한 번만 인스턴스 머티리얼을 만든다
        return go.transform;
    }

    /// <summary>지금 팔꿈치를 어깨-손 직선에서 바깥으로 0.3m 민 자리. 팔이 곧으면 몸 앞쪽 아무 수직.</summary>
    private Vector3 DefaultHintPosition()
    {
        Vector3 a = upper.position, b = lower.position, c = hand.position;
        Vector3 ac = c - a;
        Vector3 off = (b - a) - Vector3.Project(b - a, ac);
        if (off.sqrMagnitude < 1e-6f) off = Vector3.Cross(ac, Vector3.up);
        if (off.sqrMagnitude < 1e-6f) off = Vector3.Cross(ac, Vector3.forward);
        return b + off.normalized * 0.3f;
    }

    private void LateUpdate()
    {
        if (!ready || target == null) return;
        if (weight > 0f) Solve();

        if (Time.unscaledTime >= nextLogTime)
        {
            nextLogTime = Time.unscaledTime + logInterval;
            Debug.LogFormat("[팔IK] 목표 거리 {0:F3}m / 닿는 거리 {1:F3}m ({2}) · 팔꿈치 각 {3:F1}° · weight {4:F2}",
                lastTargetDistance, lastReach, lastReachable ? "닿음" : "못 닿음 — 뻗어서 가리킴",
                lastElbowAngle, weight);
        }
    }

    private void Solve()
    {
        // ── 전부 S(위팔 부모) 로컬 공간에서 푼다. 위 클래스 주석 «음수 스케일 처리» 참고 ──
        Quaternion sRot = space.rotation;
        Quaternion sInv = Quaternion.Inverse(sRot);

        Vector3 a = space.InverseTransformPoint(upper.position);
        Vector3 b = space.InverseTransformPoint(lower.position);
        Vector3 c = space.InverseTransformPoint(hand.position);
        Vector3 t = space.InverseTransformPoint(target.position);

        float l1 = (b - a).magnitude;
        float l2 = (c - b).magnitude;
        if (l1 < 1e-5f || l2 < 1e-5f) return;

        Vector3 at = t - a;
        float dist = at.magnitude;
        float maxReach = (l1 + l2) * 0.999f;              // 완전히 펴면 acos가 NaN 경계라 살짝 덜 편다
        float minReach = Mathf.Abs(l1 - l2) * 1.001f + 1e-4f;
        float d = Mathf.Clamp(dist, minReach, maxReach);
        Vector3 dir = dist > 1e-5f ? at / dist : (c - a).normalized;

        // 로그용(월드 m로 환산 — S의 스케일 크기만큼 곱한다)
        float sScale = space.lossyScale.magnitude / 1.7320508f;
        lastTargetDistance = dist * sScale;
        lastReach = (l1 + l2) * sScale;
        lastReachable = dist <= maxReach;

        // 팔꿈치가 굽을 쪽(pole) = 힌트를 어깨-목표 축에 수직으로 투영한 방향
        Vector3 pole = hint != null ? space.InverseTransformPoint(hint.position) - a : (b - a);
        pole -= Vector3.Dot(pole, dir) * dir;
        if (pole.sqrMagnitude < 1e-8f) { pole = (b - a) - Vector3.Dot(b - a, dir) * dir; }
        if (pole.sqrMagnitude < 1e-8f) { pole = Vector3.Cross(dir, Vector3.up); }
        if (pole.sqrMagnitude < 1e-8f) { pole = Vector3.Cross(dir, Vector3.right); }
        pole.Normalize();

        // 코사인 법칙 — 어깨각
        float cosA = Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f);
        float sinA = Mathf.Sqrt(1f - cosA * cosA);
        Vector3 elbowGoal = a + dir * (l1 * cosA) + pole * (l1 * sinA);
        Vector3 handGoal = a + dir * d;                   // 못 닿으면 목표 방향으로 뻗은 끝

        // ① 위팔: 팔꿈치를 elbowGoal로
        Quaternion q1 = Quaternion.FromToRotation(b - a, elbowGoal - a);
        RotateInS(upper, q1, sRot, sInv);

        // ② 위팔 비틀기: 위팔 축을 중심으로 돌려 아래팔이 굽힘면(a·elbowGoal·handGoal)에 들어오게
        if (alignElbowTwist)
        {
            Vector3 b1 = space.InverseTransformPoint(lower.position);
            Vector3 c1 = space.InverseTransformPoint(hand.position);
            Vector3 axis = b1 - a;
            if (axis.sqrMagnitude > 1e-10f)
            {
                axis.Normalize();
                Vector3 from = Vector3.ProjectOnPlane(c1 - b1, axis);
                Vector3 to = Vector3.ProjectOnPlane(handGoal - b1, axis);
                // 아래팔이 거의 곧으면 굽힘면이 정의되지 않는다 — 비틀지 않는다
                if (from.sqrMagnitude > 1e-8f && to.sqrMagnitude > 1e-8f)
                {
                    float twist = Vector3.SignedAngle(from, to, axis);
                    RotateInS(upper, Quaternion.AngleAxis(twist, axis), sRot, sInv);
                }
            }
        }

        // ③ 아래팔: 손을 handGoal로
        Vector3 b2 = space.InverseTransformPoint(lower.position);
        Vector3 c2 = space.InverseTransformPoint(hand.position);
        Quaternion q2 = Quaternion.FromToRotation(c2 - b2, handGoal - b2);
        RotateInS(lower, q2, sRot, sInv);

        // 로그·기즈모용 팔꿈치 각(180° = 쭉 폄)
        Vector3 b3 = space.InverseTransformPoint(lower.position);
        Vector3 c3 = space.InverseTransformPoint(hand.position);
        lastElbowAngle = Vector3.Angle(a - b3, c3 - b3);
    }

    /// <summary>
    /// S공간 회전 q를 뼈에 가중치만큼 얹는다.
    /// rel = Inverse(S.rotation) * bone.rotation 은 S 아래 로컬 회전들의 곱이고, S 아래에 거울이 없으니
    /// 이 공간에서 q를 왼쪽에 곱하면 화면에서도 정확히 q만큼 돈다. 되돌릴 때 S.rotation을 다시 곱한다.
    /// </summary>
    private void RotateInS(Transform bone, Quaternion q, Quaternion sRot, Quaternion sInv)
    {
        if (weight < 1f) q = Quaternion.Slerp(Quaternion.identity, q, weight);
        Quaternion rel = sInv * bone.rotation;
        bone.rotation = sRot * (q * rel);
    }

    private static string NameOf(Transform t) => t != null ? t.name : "(없음)";

    private void OnDrawGizmos()
    {
        if (!ready || upper == null || lower == null || hand == null) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(upper.position, lower.position);
        Gizmos.DrawLine(lower.position, hand.position);
        Gizmos.DrawWireSphere(upper.position, 0.02f);
        Gizmos.DrawWireSphere(lower.position, 0.02f);

        if (target != null)
        {
            Gizmos.color = lastReachable ? Color.green : Color.red;
            Gizmos.DrawWireSphere(target.position, handleSize * 0.75f);
            Gizmos.DrawLine(hand.position, target.position);
        }
        if (hint != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(lower.position, hint.position);
        }

#if UNITY_EDITOR
        // 에디터 전용 글자. 런타임 코드는 이 블록 안의 어떤 함수도 부르지 않는다(CLAUDE.md 규칙 10).
        UnityEditor.Handles.Label(lower.position, $"팔꿈치 {lastElbowAngle:F0}°");
#endif
    }
}
