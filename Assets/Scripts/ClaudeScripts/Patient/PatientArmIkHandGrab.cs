// ★시험 전용 · 2026-10-22
//   핸드 트래킹으로 환자 손목을 잡아 PatientArmIkProbe의 목표를 옮긴다(사용자 지시: «내가 직접 잡아서 옮기고 동작해 보고 싶다»).
//   · 잡기: 내 손(손목 · 엄지 · 검지 · 중지 끝 중 가장 가까운 점)이 환자 손 본에서 grabRadius 안일 때 주먹을 쥐거나 핀치한다.
//   · 따라오기: 잡는 순간 «내 손목 ↔ 목표»의 위치 · 회전 차이를 저장하고, 그 뒤로는 목표가 내 손목을 그대로 따른다.
//     → 손목을 젖히거나 비틀면 probe의 «잡은 손 회전»(팔 비틀기 · 손목 꺾기)이 그대로 반응한다.
//   · 놓기: 손을 펴면 놓는다. ★10-22 사용자 선택 B — 놓은 자세 그대로 머문다(애니로 되돌리지 않는다).
//   · 표시: 목표 구 색 — 잡을 수 있는 거리 = 초록 · 잡는 중 = 주황 · 그 밖 = 원래 색.
//   손 입력은 실측 기록이 쓰는 RomRecordHands(ISDK Hand 관절 · 핀치 · 오핀치 거르기)를 그대로 쓴다.
//   붙이는 법: 메뉴 GuideChuna/시험/환자 팔 IK 0단계(PatientArmIkProbeMenu)가 probe와 함께 환자에 붙인다. 씬에 올리지 않는다.
using Oculus.Interaction.Input;
using UnityEngine;

[DefaultExecutionOrder(9000)] // probe(10000)의 LateUpdate보다 먼저 — 목표는 Update에서 옮긴다
public class PatientArmIkHandGrab : MonoBehaviour
{
    [Tooltip("내 손이 환자 손 본에서 이 거리(m) 안이어야 잡힌다.")]
    [SerializeField] private float grabRadius = 0.10f;

    [Tooltip("주먹 판정 — 검지 · 중지 · 약지 끝이 내 손목에서 평균 이 거리(m)보다 가까우면 쥔 것. 편 손은 약 0.17m.")]
    [SerializeField] private float fistDistance = 0.11f;

    [Tooltip("주먹을 편 것으로 보는 거리(m). 쥠 ~ 폄 사이는 그대로 둔다(떨림으로 깜박이지 않게).")]
    [SerializeField] private float fistOpenDistance = 0.13f;

    [Tooltip("핀치(엄지–검지 · 중지)로도 잡는다.")]
    [SerializeField] private bool allowPinch = true;

    private readonly RomRecordHands hands = new RomRecordHands();

    private class HandState
    {
        public PatientArmIkProbe held;
        public Vector3 posOffset;      // 내 손목 로컬에서 본 목표 자리
        public Quaternion rotOffset;   // 내 손목 기준 목표 회전
        public bool fist;
        public bool wasClosed;
    }
    private readonly HandState left = new HandState(), right = new HandState();
    private PatientArmIkProbe[] probes = new PatientArmIkProbe[0];
    private float nextProbeScan;
    private bool loggedHands;

    private static readonly Color NearColor = new Color(0.25f, 0.95f, 0.4f);
    private static readonly Color HeldColor = new Color(1f, 0.55f, 0.1f);
    private static readonly Color IdleColor = new Color(1f, 0.25f, 0.2f);   // probe가 만든 목표 구 색과 같다

    private void Update()
    {
        hands.Resolve();
        if (!loggedHands && hands.HasLeft && hands.HasRight)
        {
            loggedHands = true;
            Debug.Log($"[팔IK잡기] 손을 찾았다 — 환자 손목 {grabRadius * 100f:F0}cm 안에서 주먹(또는 핀치)으로 잡고, 펴면 놓는다(놓은 자세 그대로).");
        }
        if (Time.unscaledTime >= nextProbeScan)
        {
            nextProbeScan = Time.unscaledTime + 0.5f;
            probes = GetComponents<PatientArmIkProbe>();
        }

        for (int i = 0; i < probesNear.Length; i++) probesNear[i] = false;
        UpdateHand(true, left);
        UpdateHand(false, right);
        PaintTargets();
    }

    private readonly bool[] probesNear = new bool[4];

    private void UpdateHand(bool isLeft, HandState st)
    {
        string handName = isLeft ? "왼손" : "오른손";
        if (!hands.TryWrist(isLeft, out Pose wrist))
        {
            if (st.held != null)
            {
                Debug.Log($"[팔IK잡기] {handName} 추적이 끊겼다 — {st.held.SideLabel}을 놓는다(자세 그대로)");
                Release(st);
            }
            st.wasClosed = false;
            return;
        }

        // 쥠 판정 — 주먹(세 손가락 끝 ↔ 손목 평균 거리, 문턱 두 개) 또는 핀치
        if (hands.TryJoint(isLeft, HandJointId.HandIndexTip, out Vector3 ti) &&
            hands.TryJoint(isLeft, HandJointId.HandMiddleTip, out Vector3 tm) &&
            hands.TryJoint(isLeft, HandJointId.HandRingTip, out Vector3 tr))
        {
            float avg = (Vector3.Distance(ti, wrist.position) + Vector3.Distance(tm, wrist.position) + Vector3.Distance(tr, wrist.position)) / 3f;
            if (avg < fistDistance) st.fist = true;
            else if (avg > fistOpenDistance) st.fist = false;
        }
        bool pinch = false;
        if (allowPinch)
        {
            hands.UpdatePinch(isLeft, null, out _);
            pinch = (isLeft ? hands.LeftPinch : hands.RightPinch).closed;
        }
        bool closed = st.fist || pinch;

        // 가장 가까운 환자 손(다른 손이 잡지 않은 것)
        PatientArmIkProbe nearest = null;
        int nearestIdx = -1;
        float best = grabRadius;
        for (int i = 0; i < probes.Length; i++)
        {
            PatientArmIkProbe p = probes[i];
            if (p == null || !p.IsReady || p.Target == null || p.HandBone == null) continue;
            if (p == (isLeft ? right.held : left.held)) continue;
            float d = MinHandDistance(isLeft, wrist.position, p.HandBone.position);
            if (d < best) { best = d; nearest = p; nearestIdx = i; }
        }
        if (nearest != null && st.held == null && nearestIdx < probesNear.Length) probesNear[nearestIdx] = true;

        if (st.held == null && closed && !st.wasClosed && nearest != null)
        {
            // 잡는 순간 목표를 환자 손 자리로 옮겨 놓고(목표가 팔 길이 밖에 남아 있었어도 튀지 않게) 차이를 저장한다
            Transform t = nearest.Target;
            t.position = nearest.HandBone.position;
            Quaternion inv = Quaternion.Inverse(wrist.rotation);
            st.posOffset = inv * (t.position - wrist.position);
            st.rotOffset = inv * t.rotation;
            st.held = nearest;
            // ★10-23 사용자 지적(«해부학적 실제 신체 움직임을 적용하라고 했는데 왜 마음대로 고정시켜놓은건데»): 10-22의 견인(잡는 동안 팔꿈치를 편 채 · Traction)을
            //   해부학 명세를 넣을 때 다시 보지 않고 남겨 둬, 손을 어깨 23cm까지 당겨도 팔꿈치가 166°로 펴져 있었다(Link 로그).
            //   → 견인을 켜지 않는다. 팔꿈치는 잡은 손 자리대로 굽는다(굴곡 한계 145 안).
            Debug.Log($"[팔IK잡기] {handName}으로 {nearest.SideLabel}을 잡았다({(st.fist ? "주먹" : "핀치")} · 거리 {best * 100f:F1}cm)");
        }
        else if (st.held != null && !closed)
        {
            Debug.Log($"[팔IK잡기] {handName}을 폈다 — {st.held.SideLabel}을 놓는다(놓은 자세 그대로 · 사용자 선택 B)");
            Release(st);
        }

        if (st.held != null && st.held.Target != null)
        {
            st.held.Target.SetPositionAndRotation(wrist.position + wrist.rotation * st.posOffset, wrist.rotation * st.rotOffset);
        }
        st.wasClosed = closed;
    }

    /// <summary>
    /// 놓는다 — 견인을 끄고 목표를 지금 환자 손 자리로 옮긴다. 견인 중에는 목표(내 손)가 팔 길이보다 가까울 수 있어서,
    /// 그대로 두면 견인을 끄는 순간 팔꿈치가 굽는다 — «놓은 자세 그대로»(B)를 지키려고 목표를 손에 맞춘다.
    /// </summary>
    private static void Release(HandState st)
    {
        if (st.held != null)
        {
            st.held.Traction = false;
            if (st.held.Target != null && st.held.HandBone != null) st.held.Target.position = st.held.HandBone.position;
        }
        st.held = null;
    }

    /// <summary>내 손의 손목 · 엄지 · 검지 · 중지 끝 중 환자 손에 가장 가까운 거리.</summary>
    private float MinHandDistance(bool isLeft, Vector3 wristPos, Vector3 patientHand)
    {
        float d = Vector3.Distance(wristPos, patientHand);
        if (hands.TryJoint(isLeft, HandJointId.HandThumbTip, out Vector3 a)) d = Mathf.Min(d, Vector3.Distance(a, patientHand));
        if (hands.TryJoint(isLeft, HandJointId.HandIndexTip, out Vector3 b)) d = Mathf.Min(d, Vector3.Distance(b, patientHand));
        if (hands.TryJoint(isLeft, HandJointId.HandMiddleTip, out Vector3 c)) d = Mathf.Min(d, Vector3.Distance(c, patientHand));
        return d;
    }

    // 목표 구 색 — 상태가 바뀔 때만 칠한다(매 프레임 머티리얼을 만들지 않는다: probe가 만든 인스턴스 머티리얼의 색만 바꾼다)
    private readonly int[] painted = new int[4];
    private void PaintTargets()
    {
        for (int i = 0; i < probes.Length && i < painted.Length; i++)
        {
            PatientArmIkProbe p = probes[i];
            if (p == null || p.Target == null) continue;
            int state = (left.held == p || right.held == p) ? 2 : probesNear[i] ? 1 : 0;
            if (state == painted[i]) continue;   // 처음(0 = 원래 색)은 probe가 이미 칠해 두었다
            Renderer r = p.Target.GetComponent<Renderer>();
            if (r != null) r.material.color = state == 2 ? HeldColor : state == 1 ? NearColor : IdleColor;
            painted[i] = state;
        }
    }
}
