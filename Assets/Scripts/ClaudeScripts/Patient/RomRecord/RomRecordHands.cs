using Oculus.Interaction.Input;
using UnityEngine;

/// <summary>
/// 실측 기록의 손 입력 — 손 끝 위치와 <b>핀치</b>를 읽는다(2026-09-18 신설).
///
/// ★손은 ISDK <see cref="Hand"/>의 관절 자세로 읽는다(<c>GetJointPose</c>, 월드 좌표).
///   손 끝 뼈를 이름(XRHand_*Tip)으로 찾으면 합성 손·비주얼 손에 같은 이름이 겹쳐 엉뚱한 것을 잡는다(09-18 로비 리그 실측).
///   좌우는 <see cref="Hand.Handedness"/>로 가른다 — 뼈 이름의 접두로 가르지 않는다.
///
/// ★핀치(2026-09-18 사용자 확정)
///   · 엄지–검지, 엄지–중지 <b>둘 다</b> 받는다(시스템 메뉴 제스처가 엄지–검지라 겹칠 수 있어서).
///   · 오므리면 시작, 펴면 끝. 끝날 때는 <b>펴기 직전 자리</b>를 돌려준다 — 펴는 동작 자체가 손끝을 움직인다.
///   · 시스템 제스처(손바닥을 얼굴로 향하고 핀치)가 진행 중이면 받지 않는다.
///   · 찍는 손 제한은 없다 — 압박하는 손은 핀치로 읽힐 만큼 좁게 잡지 않는다(사용자).
/// </summary>
public class RomRecordHands
{
    public class PinchState
    {
        public bool closed;
        public bool middle;              // 이번 핀치가 엄지–중지인가
        public float closeTime;
        public Vector3 current;          // 지금 핀치 자리(두 손가락 끝의 중간점)
        // ★펴기 직전 자리를 돌려주려고 최근 자리를 모아 둔다. 고정 길이 — 매 프레임 할당하지 않는다.
        public readonly Vector3[] ring = new Vector3[48];
        public readonly float[] ringTime = new float[48];
        public int ringHead;
        public int ringCount;

        // ★진단(2026-09-21 신설). 09-21 증상 "위에서 잡으면 핀치가 안 잡힌다"를 로그로 가르려고 둔다.
        //   막는 자리가 넷인데 셋이 조용해서 원인을 못 갈랐다 — 이제 막은 쪽이 스스로 말한다.
        public string blockReason;   // 지금 <b>시작</b>을 막고 있는 것(null이면 안 막힘)
        public float gap = -1f;      // 엄지–검지/중지 중 가까운 쪽 거리(m). 추적이 없으면 -1
        public bool highConfidence;
        public bool tracked;
        public Vector3 tip;          // 엄지 끝(진단용 — 손 높이를 재는 데 쓴다)
    }

    private Hand left, right;
    private OVRHand leftOvr, rightOvr;
    private float nextFind;

    public readonly PinchState LeftPinch = new PinchState();
    public readonly PinchState RightPinch = new PinchState();

    // ★★09-21 실측: 두 판 연속으로 «덜 오므림»이 막은 이유 1위였다(63회·72회).
    //   손가락을 붙였다고 생각해도 추적이 1.5cm 안으로 안 들어온 것이다 — 문턱을 넓힌다.
    //   ★오므림 판정만 넓히고 <b>짧은 핀치·낮은 신뢰도 거르기는 그대로</b> 둔다(오핀치 방패는 그쪽이다).
    public float closeDistance = 0.022f;   // 이보다 가까우면 오므림
    public float openDistance = 0.042f;    // 이보다 멀면 폄(사이는 그대로 — 떨림으로 깜박이지 않게)
    public float releaseLookback = 0.15f;  // 펴기 직전 이만큼 앞의 자리를 쓴다(초)
    // ★09-18 첫 기기 실행 로그: 의도하지 않은 핀치가 한 판에 30번 넘게 잡혔다("이미 다 찍었다" 21·미간 재지정 9).
    //   손가락이 가려지면 추적이 엄지·손가락 끝을 붙여 버리는 순간이 있다(추정). 두 가지로 거른다.
    public float minHold = 0.12f;          // 이보다 짧게 오므렸다 편 것은 핀치가 아니다(초)
    public bool requireHighConfidence = true;   // 추적 신뢰도가 낮을 때 오므린 것은 받지 않는다
    public int IgnoredShort { get; private set; }
    public int IgnoredLowConfidence { get; private set; }

    public bool HasLeft => left != null;
    public bool HasRight => right != null;

    /// <summary>손을 찾는다. 런타임에 늦게 생길 수 있어 못 찾으면 0.5초마다 다시 본다.</summary>
    public void Resolve()
    {
        if (left != null && right != null) return;
        if (Time.unscaledTime < nextFind) return;
        nextFind = Time.unscaledTime + 0.5f;

        foreach (var h in Object.FindObjectsByType<Hand>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            // ★Hand는 필터·합성 단계마다 하나씩 있다. 가장 윗단(데이터 원천에 가까운 것)을 쓰려고
            //   이름이 LeftHand/RightHand인 것을 먼저 고른다(OVRInteraction/OVRHands/LeftHand — 로비 리그 실측).
            if (h.Handedness == Handedness.Left && (left == null || h.name == "LeftHand")) left = h;
            if (h.Handedness == Handedness.Right && (right == null || h.name == "RightHand")) right = h;
        }
        foreach (var o in Object.FindObjectsByType<OVRHand>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (o.name.StartsWith("Left")) leftOvr = o;
            else if (o.name.StartsWith("Right")) rightOvr = o;
        }

        if (left != null && right != null)
            ChunaLogger.Log($"<color=cyan>[실측기록] 손 연결 — 왼 '{left.name}' · 오른 '{right.name}' · " +
                            $"시스템제스처 감시 {(leftOvr != null ? "O" : "★없음")}/{(rightOvr != null ? "O" : "★없음")}</color>");
    }

    public bool TryJoint(bool isLeft, HandJointId id, out Vector3 pos)
    {
        pos = default;
        Hand h = isLeft ? left : right;
        if (h == null || !h.IsTrackedDataValid) return false;
        if (!h.GetJointPose(id, out Pose p)) return false;
        pos = p.position;
        return true;
    }

    public bool TryWrist(bool isLeft, out Pose pose)
    {
        pose = default;
        Hand h = isLeft ? left : right;
        if (h == null || !h.IsTrackedDataValid) return false;
        return h.GetJointPose(HandJointId.HandWristRoot, out pose);
    }

    public bool SystemGesture(bool isLeft)
    {
        OVRHand o = isLeft ? leftOvr : rightOvr;
        return o != null && o.IsSystemGestureInProgress;
    }

    /// <summary>
    /// 한 손의 핀치를 갱신한다. 반환: 0 변화 없음 · 1 방금 오므림 · 2 방금 폄(released에 고정 자리) · 3 취소(손을 놓침)
    /// · 4 너무 짧아 무시.
    /// ★<paramref name="blockedBy"/>가 null이 아니면(손목 버튼 근처 등) 새로 시작하지 않는다 — 그 문구가 막은 이유로 기록된다.
    /// </summary>
    public int UpdatePinch(bool isLeft, string blockedBy, out Vector3 released)
    {
        released = default;
        PinchState s = isLeft ? LeftPinch : RightPinch;

        bool okT = TryJoint(isLeft, HandJointId.HandThumbTip, out Vector3 t);
        bool okI = TryJoint(isLeft, HandJointId.HandIndexTip, out Vector3 i);
        bool okM = TryJoint(isLeft, HandJointId.HandMiddleTip, out Vector3 m);

        Hand h = isLeft ? left : right;
        s.tracked = okT && (okI || okM);
        s.highConfidence = h != null && h.IsHighConfidence;
        s.tip = okT ? t : s.tip;

        if (!s.tracked || SystemGesture(isLeft))
        {
            s.gap = -1f;
            s.blockReason = !s.tracked ? "손 끝 추적 끊김" : "시스템 제스처 중";
            // ★잡고 있는데 손을 놓쳤거나 시스템 제스처가 시작됐다 — 엉뚱한 자리에 고정하지 않고 취소한다.
            if (s.closed) { s.closed = false; return 3; }
            return 0;
        }

        float dI = okI ? Vector3.Distance(t, i) : float.MaxValue;
        float dM = okM ? Vector3.Distance(t, m) : float.MaxValue;
        s.gap = Mathf.Min(dI, dM);

        if (!s.closed)
        {
            if (blockedBy != null) { s.blockReason = blockedBy; return 0; }
            float d = s.gap;
            if (d > closeDistance)
            {
                // ★거의 오므렸는데 문턱을 못 넘은 것만 이유로 남긴다. 손을 편 상태는 정상이라 조용히 둔다.
                s.blockReason = d < closeDistance * 2.5f ? "덜 오므림" : null;
                return 0;
            }
            if (requireHighConfidence && h != null && !h.IsHighConfidence)
            {
                IgnoredLowConfidence++;
                s.blockReason = "추적 신뢰 낮음";
                return 0;
            }
            s.blockReason = null;
            s.closed = true;
            s.middle = dM < dI;
            s.closeTime = Time.unscaledTime;
            s.ringCount = 0;
            s.current = (t + (s.middle ? m : i)) * 0.5f;
            Push(s);
            return 1;
        }

        // 잡고 있는 동안 — 오므린 그 손가락만 본다(다른 손가락이 우연히 가까워져도 흔들리지 않게).
        float dNow = s.middle ? dM : dI;
        Vector3 f = s.middle ? m : i;
        if (dNow < openDistance)
        {
            s.current = (t + f) * 0.5f;
            Push(s);
            return 0;
        }

        // 폈다 — 펴기 직전 자리를 돌려준다.
        s.closed = false;
        if (Time.unscaledTime - s.closeTime < minHold) { IgnoredShort++; s.blockReason = "너무 짧음"; return 4; }
        released = Lookback(s, Time.unscaledTime - releaseLookback);
        return 2;
    }

    private static void Push(PinchState s)
    {
        s.ring[s.ringHead] = s.current;
        s.ringTime[s.ringHead] = Time.unscaledTime;
        s.ringHead = (s.ringHead + 1) % s.ring.Length;
        if (s.ringCount < s.ring.Length) s.ringCount++;
    }

    /// <summary>시각 when 이전의 가장 늦은 자리. ★오므린 직후보다 앞으로는 안 간다 — 그 전 자리는 핀치가 아니다.</summary>
    private static Vector3 Lookback(PinchState s, float when)
    {
        if (s.ringCount == 0) return s.current;
        Vector3 best = s.ring[(s.ringHead - s.ringCount + s.ring.Length) % s.ring.Length];   // 가장 오래된 것
        for (int k = 0; k < s.ringCount; k++)
        {
            int idx = (s.ringHead - s.ringCount + k + s.ring.Length) % s.ring.Length;
            if (s.ringTime[idx] <= when) best = s.ring[idx];
            else break;
        }
        return best;
    }
}
