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
///     · 회전은 <b>localRotation에만</b> 곱한다(<see cref="RotateInS"/>). 월드 회전(bone.rotation)은 읽지도 쓰지도 않는다.
///       S 아래(위팔·아래팔·손)에 음수 스케일이 없으면 이 공간에는 거울이 없다 — 시작 때 검사하고, 있으면 멈춘다.
///   ★10-22 Play 실측으로 정정: 처음엔 rel = Inverse(S.rotation) * bone.rotation → bone.rotation = S.rotation * (q * rel)로
///     «월드 회전을 거쳐» 넣었는데, 사용자가 «목표를 움직이는 것과 반대로 움직인다»고 했고 로그도 어긋났다
///     (목표가 팔 길이 밖인데 팔꿈치 66~77° — 쭉 펴져 180°에 가까워야 한다). 거울 밑에서 월드 회전이
///     위치(InverseTransformPoint)와 다른 규칙으로 계산되는 것으로 보고(추정), 위치와 같은 로컬 사슬만 쓰도록 바꿨다.
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

    // ── 관절 한계(10-22 사용자 지시: «사람 실제 관절 부위별 한계» — 팔이 너무 뒤까지 돌아간다) ──
    //   값 = 교과서 정상 관절가동범위(AAOS). 어깨 굴곡·외전 180°는 구(球) 전체라 따로 자르지 않는다.
    //   방향(앞·위·바깥)은 본 이름이 아니라 뼈 위치로 잰다 — c8 음수 스케일 때문(CLAUDE.md 규칙 9). 기즈모 화살표로 확인한다.
    [Header("관절 한계 (AAOS 정상 가동범위)")]
    [Tooltip("관절 한계를 건다. 끄면 0단계처럼 손을 목표에 붙이기만 한다.")]
    [SerializeField] private bool applyLimits = true;

    [Tooltip("어깨 신전 — 위팔이 몸 관상면(앞뒤를 가르는 면) 뒤로 갈 수 있는 최대 각(°). 정상 50.")]
    [Range(0f, 90f)]
    [SerializeField] private float shoulderExtensionMax = 50f;

    [Tooltip("어깨 내전 — 위팔이 어깨를 지나는 시상면(좌우를 가르는 면)을 넘어 몸 안쪽으로 갈 수 있는 최대 각(°). 정상 30.")]
    [Range(0f, 90f)]
    [SerializeField] private float shoulderAdductionMax = 30f;

    [Tooltip("어깨 내회전(위팔 비틀기, 아래팔이 배 쪽으로) 최대 각(°). 정상 70.")]
    [Range(0f, 120f)]
    [SerializeField] private float shoulderInternalRotMax = 70f;

    [Tooltip("어깨 외회전(위팔 비틀기, 아래팔이 바깥으로) 최대 각(°). 정상 90.")]
    [Range(0f, 120f)]
    [SerializeField] private float shoulderExternalRotMax = 90f;

    [Tooltip("팔꿈치 굴곡 최대 각(°). 정상 145. 과신전(뒤로 꺾임)은 늘 0이다.")]
    [Range(0f, 170f)]
    [SerializeField] private float elbowFlexionMax = 145f;

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
    private float lastHandErrMm;

    // 몸 기준 축 — 시작 때 뼈 위치로 재서 가슴 본 로컬(행렬 기준, 거울 포함)에 담아 두고 매 프레임 다시 꺼낸다
    private Transform chest;
    private Vector3 frontInChest, upInChest, lateralInChest;
    private bool frameReady;
    // 로그용 마지막 관절 각과 걸린 한계
    private float lastBehindDeg, lastMedialDeg, lastElbowFlexDeg, lastRotDeg;
    private string lastLimitHit = "";   // 걸린 한계가 바뀔 때만 새 문자열(매 프레임 할당 없음)   // 풀고 난 뒤 실제 손 ↔ 풀이가 노린 손 자리(월드 mm). 맞게 돌면 0에 가깝다(10-22 실측 로그)
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

        frameReady = MeasureBodyFrame(right);

        if (target == null) target = MakeHandle("[팔IK] 목표", hand.position, new Color(1f, 0.25f, 0.2f));
        if (hint == null) hint = MakeHandle("[팔IK] 힌트(팔꿈치)", DefaultHintPosition(), new Color(0.25f, 0.5f, 1f));

        // 로컬 회전 변환(RotateInS)은 위팔 localScale이 균일하다고 본다 — 아니면 알린다(멈추지는 않는다)
        Vector3 us = upper.localScale;
        if (Mathf.Abs(us.x - us.y) > 1e-3f || Mathf.Abs(us.x - us.z) > 1e-3f)
            Debug.LogWarning($"[팔IK] 위팔 localScale이 균일하지 않다 {us} — 아래팔 회전이 조금 어긋날 수 있다");

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

    /// <summary>
    /// 몸 기준 축을 뼈 «위치»로 잰다(본 이름 · 본 축을 믿지 않는다 — c8 음수 스케일, measure-dont-guess-signs).
    ///   위   = 엉덩이 → 목
    ///   앞   = 머리 본 → 두 눈 중점 (위 성분을 뺀다)
    ///   바깥 = 반대쪽 위팔 → 이쪽 위팔 (위 · 앞 성분을 뺀다)
    /// 외적으로 서로 엮지 않는다(CLAUDE.md 규칙 9) — 셋 다 따로 잰다.
    /// 가슴 본에 «행렬 기준»(InverseTransformVector)으로 붙여 두어 몸통이 돌면 같이 돈다.
    /// </summary>
    private bool MeasureBodyFrame(bool right)
    {
        Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        Transform neck = animator.GetBoneTransform(HumanBodyBones.Neck);
        Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
        Transform eyeL = animator.GetBoneTransform(HumanBodyBones.LeftEye);
        Transform eyeR = animator.GetBoneTransform(HumanBodyBones.RightEye);
        Transform other = animator.GetBoneTransform(right ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
        chest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
        if (chest == null) chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        if (chest == null) chest = animator.GetBoneTransform(HumanBodyBones.Spine);
        if (neck == null) neck = head;

        if (hips == null || neck == null || head == null || eyeL == null || eyeR == null || other == null || chest == null)
        {
            Debug.LogWarning($"[팔IK] 몸 기준 축을 잴 본이 없다 — 관절 한계를 끈다. hips={NameOf(hips)} neck={NameOf(neck)} head={NameOf(head)} " +
                             $"eyeL={NameOf(eyeL)} eyeR={NameOf(eyeR)} 반대 위팔={NameOf(other)} 가슴={NameOf(chest)}");
            return false;
        }

        Vector3 up = (neck.position - hips.position).normalized;
        Vector3 front = Vector3.ProjectOnPlane((eyeL.position + eyeR.position) * 0.5f - head.position, up).normalized;
        Vector3 lateral = upper.position - other.position;
        lateral = (lateral - Vector3.Dot(lateral, up) * up - Vector3.Dot(lateral, front) * front).normalized;
        if (up.sqrMagnitude < 0.5f || front.sqrMagnitude < 0.5f || lateral.sqrMagnitude < 0.5f)
        {
            Debug.LogWarning("[팔IK] 몸 기준 축 길이가 0이다 — 관절 한계를 끈다.");
            return false;
        }

        upInChest = chest.InverseTransformVector(up);
        frontInChest = chest.InverseTransformVector(front);
        lateralInChest = chest.InverseTransformVector(lateral);
        Debug.Log($"[팔IK] 몸 기준 축(월드) — 앞 {front:F2} · 위 {up:F2} · 바깥 {lateral:F2} · 가슴 본={chest.name} · " +
                  $"재는 본: 엉덩이 {hips.name} · 목 {neck.name} · 머리 {head.name} · 눈 {eyeL.name}/{eyeR.name} · 반대 위팔 {other.name}. " +
                  "기즈모 화살표(초록 = 앞 · 파랑 = 위 · 주황 = 바깥)가 환자 몸과 맞는지 눈으로 확인할 것");
        return true;
    }

    /// <summary>방향 v의 k 성분이 maxDot을 넘으면 k 성분을 maxDot으로 줄이고 나머지 방향은 그대로 둔다(단위 벡터 유지).</summary>
    private static bool ClampAlong(ref Vector3 v, Vector3 k, float maxDot, Vector3 fallbackPerp)
    {
        float dot = Vector3.Dot(v, k);
        if (dot <= maxDot) return false;
        Vector3 perp = v - dot * k;
        if (perp.sqrMagnitude < 1e-10f) perp = fallbackPerp - Vector3.Dot(fallbackPerp, k) * k;
        v = perp.normalized * Mathf.Sqrt(Mathf.Max(0f, 1f - maxDot * maxDot)) + k * maxDot;
        return true;
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
            Debug.LogFormat("[팔IK] 목표 거리 {0:F3}m / 닿는 거리 {1:F3}m ({2}) · 팔꿈치 각 {3:F1}° · 손 오차 {5:F1}mm · weight {4:F2}",
                lastTargetDistance, lastReach, lastReachable ? "닿음" : "못 닿음 — 뻗어서 가리킴",
                lastElbowAngle, weight, lastHandErrMm);
            if (applyLimits && frameReady)
                Debug.LogFormat("[팔IK] 관절 — 어깨 뒤로 {0:F0}°/{4:F0} · 안쪽 {1:F0}°/{5:F0} · 회전 {2:+0;-0}°(+내 {6:F0} / −외 {7:F0}) · 팔꿈치 굴곡 {3:F0}°/{8:F0} · 걸린 한계: {9}",
                    lastBehindDeg, lastMedialDeg, lastRotDeg, lastElbowFlexDeg,
                    shoulderExtensionMax, shoulderAdductionMax, shoulderInternalRotMax, shoulderExternalRotMax, elbowFlexionMax,
                    lastLimitHit.Length > 0 ? lastLimitHit : "없음");
        }
    }

    private void Solve()
    {
        // ── 전부 S(위팔 부모) 로컬 공간에서 푼다. 위 클래스 주석 «음수 스케일 처리» 참고 ──
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
        if (applyLimits)
        {
            // 팔꿈치 굴곡 한계 → 어깨-손 최소 거리(코사인 법칙). 안쪽 각 = 180 − 굴곡
            float inner = (180f - elbowFlexionMax) * Mathf.Deg2Rad;
            minReach = Mathf.Max(minReach, Mathf.Sqrt(l1 * l1 + l2 * l2 - 2f * l1 * l2 * Mathf.Cos(inner)));
        }
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

        if (applyLimits && frameReady) ApplyJointLimits(a, l1, l2, t, ref elbowGoal, ref handGoal);

        // ① 위팔: 팔꿈치를 elbowGoal로
        Quaternion q1 = Quaternion.FromToRotation(b - a, elbowGoal - a);
        RotateInS(upper, q1);

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
                    RotateInS(upper, Quaternion.AngleAxis(twist, axis));
                }
            }
        }

        // ③ 아래팔: 손을 handGoal로
        Vector3 b2 = space.InverseTransformPoint(lower.position);
        Vector3 c2 = space.InverseTransformPoint(hand.position);
        Quaternion q2 = Quaternion.FromToRotation(c2 - b2, handGoal - b2);
        RotateInS(lower, q2);

        // 로그·기즈모용 팔꿈치 각(180° = 쭉 폄)
        Vector3 b3 = space.InverseTransformPoint(lower.position);
        Vector3 c3 = space.InverseTransformPoint(hand.position);
        lastElbowAngle = Vector3.Angle(a - b3, c3 - b3);
        lastHandErrMm = Vector3.Distance(hand.position, space.TransformPoint(handGoal)) * 1000f;
    }

    /// <summary>
    /// 팔꿈치 · 손 목표 자리를 관절 한계 안으로 옮긴다(S공간). 전부 내적만 쓴다 — S공간은 월드에 대해 거울일 수 있어
    /// 외적 · SignedAngle의 부호가 뒤집히기 때문이다(내적은 거울에서도 그대로다).
    ///   ① 위팔 방향: 관상면 뒤로 ≤ 신전 한계, 시상면 안쪽으로 ≤ 내전 한계
    ///   ② 아래팔 방향: 위팔에 대한 굴곡 ≤ 팔꿈치 한계(과신전 없음은 2관절 풀이가 이미 보장)
    ///   ③ 위팔 회전(비틀기): 팔을 내린 자세에서 아래팔이 앞을 볼 때를 0으로, 같은 최소 회전으로 옮긴 «앞» · «안쪽»에 대해
    ///      아래팔이 얼마나 안쪽(+내회전) · 바깥(−외회전)으로 돌았는지 → 한계로 자른다
    /// 한계에 걸리면 손은 목표에서 떨어진다(가장 가까운 허용 자세). 위팔을 잘랐으면 아래팔은 목표 쪽을 다시 겨눈다.
    /// </summary>
    private void ApplyJointLimits(Vector3 a, float l1, float l2, Vector3 t, ref Vector3 elbowGoal, ref Vector3 handGoal)
    {
        Vector3 F = space.InverseTransformVector(chest.TransformVector(frontInChest)).normalized;
        Vector3 U = space.InverseTransformVector(chest.TransformVector(upInChest)).normalized;
        Vector3 L = space.InverseTransformVector(chest.TransformVector(lateralInChest)).normalized;
        // 다시 직교화(가슴 본이 균일 스케일이면 이미 직교 — 수치 오차만 정리). 외적을 쓰지 않는다.
        F = (F - Vector3.Dot(F, U) * U).normalized;
        L = (L - Vector3.Dot(L, U) * U - Vector3.Dot(L, F) * F).normalized;

        string hit = null;

        // ① 위팔 방향
        Vector3 u = (elbowGoal - a).normalized;
        float sinExt = Mathf.Sin(shoulderExtensionMax * Mathf.Deg2Rad);
        bool hitBehind = ClampAlong(ref u, -F, sinExt, -U);
        bool hitMedial = ClampAlong(ref u, -L, Mathf.Sin(shoulderAdductionMax * Mathf.Deg2Rad), -U);
        if (hitMedial) hitBehind |= ClampAlong(ref u, -F, sinExt, -U);   // 둘째가 첫째를 다시 넘겼을 때
        lastBehindDeg = Mathf.Asin(Mathf.Clamp(Vector3.Dot(u, -F), -1f, 1f)) * Mathf.Rad2Deg;
        lastMedialDeg = Mathf.Asin(Mathf.Clamp(Vector3.Dot(u, -L), -1f, 1f)) * Mathf.Rad2Deg;
        if (hitBehind) hit = "어깨 신전";
        if (hitMedial) hit = hit == null ? "어깨 내전" : hit + " · 어깨 내전";

        Vector3 e = a + u * l1;
        // 위팔을 잘랐으면 아래팔은 새 팔꿈치에서 목표를 다시 겨눈다. 안 잘랐으면 2관절 풀이 그대로.
        Vector3 w = (hitBehind || hitMedial) ? (t - e) : (handGoal - elbowGoal);
        w = w.sqrMagnitude > 1e-10f ? w.normalized : u;

        // ② 팔꿈치 굴곡: 아래팔과 위팔 연장선 사이 각 ≤ 한계  ⇔  w · (−u) ≤ cos(180 − 한계)
        bool hitElbow = ClampAlong(ref w, -u, Mathf.Cos((180f - elbowFlexionMax) * Mathf.Deg2Rad), F);
        lastElbowFlexDeg = Vector3.Angle(u, w);
        if (hitElbow) hit = hit == null ? "팔꿈치 굴곡" : hit + " · 팔꿈치 굴곡";

        // ③ 위팔 회전: 내린 팔(−U)에서 u까지 최소 회전으로 «앞» · «안쪽»을 옮겨 0 기준을 만든다
        Quaternion swing = Quaternion.FromToRotation(-U, u);
        Vector3 n = swing * F;      // 회전 0일 때 아래팔이 향하는 쪽
        Vector3 m = swing * -L;     // 내회전 쪽
        float along = Vector3.Dot(w, u);
        Vector3 wp = w - along * u;
        if (wp.sqrMagnitude > 1e-6f)   // 팔이 거의 펴지면 회전이 정의되지 않는다 — 자르지 않는다
        {
            float rot = Mathf.Atan2(Vector3.Dot(wp, m), Vector3.Dot(wp, n)) * Mathf.Rad2Deg;
            float clamped = Mathf.Clamp(rot, -shoulderExternalRotMax, shoulderInternalRotMax);
            if (!Mathf.Approximately(rot, clamped))
            {
                float r = clamped * Mathf.Deg2Rad;
                w = u * along + (n * Mathf.Cos(r) + m * Mathf.Sin(r)) * wp.magnitude;
                string rotName = rot > 0f ? "어깨 내회전" : "어깨 외회전";
                hit = hit == null ? rotName : hit + " · " + rotName;
            }
            lastRotDeg = clamped;
        }

        elbowGoal = e;
        handGoal = e + w.normalized * l2;

        string now = hit ?? "";
        if (now != lastLimitHit)
        {
            lastLimitHit = now;
            Debug.Log("[팔IK] 걸린 한계 → " + (now.Length > 0 ? now : "없음"));
        }
    }

    /// <summary>
    /// S공간 회전 q를 뼈에 가중치만큼 얹는다 — localRotation만 쓴다(10-22 정정, 클래스 주석 참고).
    /// S → bone.parent 사이 로컬 회전의 곱 R로 q를 뼈 부모 공간으로 옮긴다: qLocal = R⁻¹ · q · R.
    /// InverseTransformPoint와 같은 로컬 사슬이므로 거울(음수 스케일)이 S 위 어디에 있든 상관없다.
    /// </summary>
    private void RotateInS(Transform bone, Quaternion q)
    {
        if (weight < 1f) q = Quaternion.Slerp(Quaternion.identity, q, weight);
        Quaternion r = Quaternion.identity;
        for (Transform t = bone.parent; t != space; t = t.parent) r = t.localRotation * r;
        bone.localRotation = (Quaternion.Inverse(r) * q * r) * bone.localRotation;
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

        // 몸 기준 축(어깨에서 0.15m) — 초록 = 앞 · 파랑 = 위 · 주황 = 바깥. 환자 몸과 맞는지 눈으로 확인한다.
        if (frameReady && chest != null)
        {
            Vector3 o = upper.position;
            Vector3 fw = chest.TransformVector(frontInChest).normalized;
            Vector3 uw = chest.TransformVector(upInChest).normalized;
            Vector3 lw = chest.TransformVector(lateralInChest).normalized;
            Gizmos.color = Color.green; Gizmos.DrawLine(o, o + fw * 0.15f);
            Gizmos.color = Color.blue; Gizmos.DrawLine(o, o + uw * 0.15f);
            Gizmos.color = new Color(1f, 0.55f, 0f); Gizmos.DrawLine(o, o + lw * 0.15f);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(o + fw * 0.16f, "앞");
            UnityEditor.Handles.Label(o + uw * 0.16f, "위");
            UnityEditor.Handles.Label(o + lw * 0.16f, "바깥");
#endif
        }

#if UNITY_EDITOR
        // 에디터 전용 글자. 런타임 코드는 이 블록 안의 어떤 함수도 부르지 않는다(CLAUDE.md 규칙 10).
        UnityEditor.Handles.Label(lower.position, $"팔꿈치 {lastElbowAngle:F0}°");
#endif
    }
}
