using UnityEngine;

/// <summary>
/// 경추 ROM의 파지 판정. 엄지·검지 콜라이더가 접촉점 콜라이더에 닿았는지만 본다.
///
/// 접촉점은 쌍이다. 면마다 잡는 곳이 다르다.
///   시상면(굴곡·신전)   이마 · 뒤통수
///   관상면·횡단면       좌 측두 · 우 측두
///
/// ★어느 손이 어디를 잡는지는 따지지 않는다. 두 점을 <b>서로 다른 손</b>이 하나씩
///   집으면 성립이다.
/// </summary>
public class CervicalGripJudge : MonoBehaviour, ChunaPathEvaluator.IHandContactSource
{
    public enum GripPair
    {
        None,
        Sagittal,   // 이마 · 뒤통수
        Lateral,    // 좌 측두 · 우 측두
    }

    [Header("=== 접촉점 (씬에서 직접 위치를 잡는다) ===")]
    [SerializeField] private GripContactPoint forehead;
    [SerializeField] private GripContactPoint occiput;
    [SerializeField] private GripContactPoint temporalLeft;
    [SerializeField] private GripContactPoint temporalRight;

    [Header("=== 손끝 (직접 넣는 쪽이 확실하다) ===")]
    [Tooltip("여기에 넣으면 자동 탐색을 하지 않고 이것만 쓴다.\n" +
             "OpenXR 리그면 OVRLeftHandVisual/OpenXRLeftHand/.../XRHand_ThumbTip 같은 것들이다.")]
    [SerializeField] private Transform leftThumbTip;
    [SerializeField] private Transform leftIndexTip;
    [SerializeField] private Transform rightThumbTip;
    [SerializeField] private Transform rightIndexTip;

    // ★★손바닥 (2026-09-04 회의 지시 — "어깨를 짚을 때는 엄지 말고 손바닥 기준으로")
    //
    //   어깨를 짚는 동작은 <b>손바닥을 어깨에 얹는</b> 것이다. 그런데 지금까지는 어깨 기준선도
    //   엄지·검지 파지점으로 잡고 있었다(CervicalRomRealityMeasure.TryGetHands).
    //   엄지 끝은 손바닥에서 8~10cm 떨어져 있어 S라인이 그만큼 어긋난다.
    //
    // ★<b>본 이름은 추측하지 않는다</b>(규칙 5·9). 리그마다 다르다 —
    //   OpenXR은 XRHand_Palm이 있는 리그도 있고 없는 리그도 있으며, 없으면 손목이 제일 가깝다.
    //   후보를 순서대로 찾고 <b>실제로 물린 본 이름을 로그에 찍는다.</b>
    //   09-02에 엄지 본(XRHand_ThumbTip)을 이 방법으로 확정했다.
    [Header("=== 손바닥 (어깨 짚기용, 2026-09-04) ===")]
    [Tooltip("여기에 넣으면 자동 탐색을 안 한다. 비우면 Palm → Wrist → Middle1 순으로 찾는다.\n" +
             "★어느 본이 물렸는지는 Play 첫 프레임에 로그로 찍힌다 — 그걸 보고 확정한다.")]
    [SerializeField] private Transform leftPalm;
    [SerializeField] private Transform rightPalm;

    [Header("=== 판정 ===")]
    [Tooltip("손끝 콜라이더 반경(m).")]
    [SerializeField] private float fingerTipRadius = 0.012f;

    [Tooltip("끄면 엄지와 검지 중 하나만 닿아도 인정한다.\n" +
             "★손끝 출처가 '엄지 단독'이면 이 값은 안 본다.")]
    [SerializeField] private bool requireBothFingers = true;

    /// <summary>파지점과 접촉 판정을 어느 손끝으로 볼 것인가.</summary>
    public enum FingerSource
    {
        ThumbAndIndex,   // 엄지·검지 둘 다 (종전)
        ThumbOnly,       // 엄지만
    }

    // ★★2026-09-02 사용자: "엄지 검지로 하려고 했는데 엄지는 확실하게 유지가 되거든?
    //   근데 검지 중지 쪽 머리 뒤쪽으로 넘어가는 애들은 각도따라 안 보이기도 해서
    //   엄지만으로 판정하는 거 테스트 해봐야겠어."
    //
    // ★검지·중지는 <b>머리 뒤로 넘어가는 손가락</b>이라 헤드셋 시야에서 각도에 따라 사라진다.
    //   가려진 손가락을 <b>보정</b>하는 것보다, 애초에 <b>안 쓰는</b> 것이 깨끗하다.
    //   엄지는 늘 시술자 쪽(앞)에 있어 가려지지 않는다.
    //
    // ★엄지 단독이면 <b>두 곳</b>을 같이 바꿔야 테스트가 성립한다:
    //     ①파지점(각도를 재는 위치)   ②접촉 판정(파지가 성립했는가)
    //   ②를 안 바꾸면 가려진 검지가 접촉점에 안 닿아서 <b>파지 자체가 영영 안 잡힌다.</b>
    [Tooltip("파지점과 접촉 판정을 어느 손끝으로 볼 것인가.\n" +
             "ThumbOnly = 엄지만 본다. 검지는 아예 안 쓴다 — 머리 뒤로 넘어가 가려지는 손가락이라서다.\n" +
             "ThumbAndIndex = 종전대로 둘 다 본다(가려지면 성한 쪽으로 이어간다).\n" +
             "★되돌리려면 이 값 하나만 바꾸면 된다.")]
    [SerializeField] private FingerSource fingerSource = FingerSource.ThumbOnly;

    private bool ThumbOnly => fingerSource == FingerSource.ThumbOnly;

    /// <summary>지금 엄지만 보고 있는가. 측정기가 파지 간격 기준을 고르는 데 쓴다.</summary>
    public bool IsThumbOnly => ThumbOnly;

    [Tooltip("인스펙터에 배정된 손끝을 무시하고 Play에서 다시 찾는다.\n" +
             "★배정된 것이 실제 손끝이 아니라 손목·손바닥 쪽 뼈면, 손목만 틀어도 파지 지점이 움직여\n" +
             "  두 손 사이 직선의 기울기가 흔들린다(2026-08-28 사용자 지적). 그때 켠다.")]
    [SerializeField] private bool ignoreAssignedTips = false;

    // ── 가려진 손가락 이어가기 (2026-09-02) ──────────────────────────────
    // 2026-09-01 사용자: "신전에서 뒤통수 쪽 손이 많이 가려져서 인식이 안 되거나 하고,
    //   그러다 보니 위치 어긋남이 생기기도 해."
    //
    // ★<b>튐의 기계적 원인</b>(2026-09-02 실측): 파지점은 (엄지+검지)/2인데,
    //   종전 검사는 <c>null</c>(=배선이 비었는가)만 봤다. 추적이 끊긴 손가락도 Transform은
    //   마지막 위치를 계속 내놓기 때문에, <b>가려진 검지의 낡은 위치가 절반의 무게로</b>
    //   중점에 들어간다. 그래서 파지점이 몇 cm 옆으로 옮겨 앉는다.
    //
    // ★신뢰도 신호가 이 층에 없어서 <b>강체 전제</b>로 자체 판별한다.
    //   파지 중엔 엄지-검지 간격이 거의 일정하다 — 간격이 갑자기 어긋나면 둘 중 하나가 튄 것이고,
    //   그 프레임에 <b>더 많이 움직인 쪽</b>이 범인이다. 배선도 패키지도 안 늘린다.
    //   (같은 원리가 이미 CervicalRomRealityMeasure.AcceptHand에서 <b>손</b> 단위로 돌고 있다.
    //    이건 그걸 <b>손가락</b> 단위로 한 층 내린 것이다.)
    //
    // ★★<b>한계 — 솔직히 적어 둔다.</b> 이 판별은 <b>급한 변화</b>만 잡는다.
    //   두 손가락이 같이 가려져 <b>같이</b> 천천히 미끄러지면 못 가른다.
    //   그건 진짜 신뢰도 신호만 잡을 수 있다.
    // ★[나중에] OVRHand 신뢰도를 끼우려면 <see cref="JudgeFingers"/> <b>안에서만</b> 손대면 된다.
    //   2026-09-02 실측: 씬에 LeftOVRHand·RightOVRHand가 활성으로 있고(OVRHand·OVRSkeleton 배선됨),
    //   Oculus.VR.dll에 GetFingerConfidence·IsDataHighConfidence가 있다.
    //   ★다만 씬에 OVRManager가 <b>없어서</b> 이 OpenXR 구성에서 값이 나오는지는 <b>Play 미확인</b>이다.
    //   확인되기 전에는 이 기하 판별이 단독으로 돈다.

    [Header("=== 가려진 손가락 이어가기 ===")]
    [Tooltip("한 손가락이 가려져 튀어도 성한 손가락 하나로 파지점을 이어간다.\n" +
             "끄면 종전대로 — 둘 다 있으면 무조건 중점을 쓴다(튐이 그대로 들어온다).")]
    [SerializeField] private bool singleFingerFallback = true;

    [Tooltip("파지 중 손가락이 낼 수 있다고 보는 최대 속도(m/s). 이보다 빨리 뛴 손가락은 추적이 튄 것으로 본다.\n" +
             "★0 이하면 속도 검사를 끈다.")]
    [SerializeField] private float maxFingerSpeed = 1.2f;

    [Tooltip("엄지-검지 간격이 기준에서 이만큼 벗어나면 한쪽이 튄 것으로 본다(m).\n" +
             "★너무 좁으면 손을 조금만 고쳐 잡아도 걸리고, 너무 넓으면 튐을 놓친다. 실측으로 정할 값이다.")]
    [SerializeField] private float fingerGapTolerance = 0.025f;

    [Tooltip("간격 기준이 지금 간격을 따라가는 시간상수(초).\n" +
             "★짧으면 미끄러지는 것까지 따라가 버려 검사가 죽고, 길면 손을 고쳐 잡았을 때 오래 버벅인다.")]
    [SerializeField] private float gapFollowSeconds = 0.5f;

    [Tooltip("한 손가락만으로 이어갈 수 있는 최대 시간(초).\n" +
             "★넘으면 <b>포기하지 않고</b> 지금 두 손가락을 새 기준으로 다시 잡는다 — " +
             "손을 진짜로 고쳐 잡았을 때 영영 못 따라가는 걸 막는다(AcceptHand의 maxRejectSeconds와 같은 얼개).")]
    [SerializeField] private float singleFingerMaxSeconds = 4f;

    [Header("=== 표시 ===")]
    [Tooltip("접촉점 구체를 보이게 할지. 위치를 잡을 때만 켜면 된다.")]
    [SerializeField] private bool showSpheres = false;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = false;

    private GripPair currentPair = GripPair.None;
    private Collider headCollider;

    /// <summary>두 접촉점이 서로 다른 손에 각각 잡혔는가.</summary>
    public bool IsGripped { get; private set; }

    /// <summary>쌍이 정해진 동안에만 판정을 가져간다. None이면 기존 판정이 그대로 돈다.</summary>
    public bool IsActive => currentPair != GripPair.None;

    public GripPair CurrentPair => currentPair;

    /// <summary>
    /// 지금 잡고 있는 손끝 4개의 중점(월드). 압박 구간에서 손이 얼마나 밀었는지 재는 기준이다.
    /// 손끝을 아직 못 찾았으면 false.
    /// ★매 프레임 도는 경로다. 배열을 새로 만들지 않는다(VR 프레임 예산).
    /// </summary>
    public bool TryGetGripMidpoint(out Vector3 midpoint)
    {
        midpoint = Vector3.zero;
        int n = 0;
        bool useIndex = !ThumbOnly;   // ★엄지 단독이면 검지는 여기서도 안 섞는다
        if (leftThumbTip != null) { midpoint += leftThumbTip.position; n++; }
        if (useIndex && leftIndexTip != null) { midpoint += leftIndexTip.position; n++; }
        if (rightThumbTip != null) { midpoint += rightThumbTip.position; n++; }
        if (useIndex && rightIndexTip != null) { midpoint += rightIndexTip.position; n++; }
        if (n == 0) return false;
        midpoint /= n;
        return true;
    }

    /// <summary>
    /// 엄지·검지 <b>파지 지점</b>(두 끝의 중점, 월드). 각도 측정은 이걸 쓴다.
    ///
    /// ★TryGetHandCluster와 달리 <b>둘 다 있어야</b> 준다. 하나만 잡히면 중점이 그 손가락 쪽으로
    ///   훌쩍 옮겨 가는데, 그게 두 손 사이 직선의 기울기를 통째로 흔든다(2026-08-28 사용자 지적).
    ///   측정은 조용히 틀린 값보다 없는 값이 낫다.
    /// </summary>
    public bool TryGetPinchPoint(GripFingerTip.Side side, out Vector3 pinch)
    {
        pinch = Vector3.zero;
        if (side == GripFingerTip.Side.Left)
        {
            UpdateFingerTrack(ref trackL, leftThumbTip, leftIndexTip, "왼");
            if (!trackL.pinchValid) return false;
            pinch = trackL.pinch;
        }
        else
        {
            UpdateFingerTrack(ref trackR, rightThumbTip, rightIndexTip, "오른");
            if (!trackR.pinchValid) return false;
            pinch = trackR.pinch;
        }
        return true;
    }

    /// <summary>
    /// 손바닥 위치(월드). ★<b>어깨를 짚을 때만</b> 쓴다 — 파지·각도 측정은 종전대로 엄지다.
    /// 2026-09-04 회의: "어깨를 짚을 때는 엄지 말고 손바닥 기준으로 해줘."
    /// </summary>
    public bool TryGetPalmPoint(GripFingerTip.Side side, out Vector3 palm)
    {
        palm = Vector3.zero;
        Transform t = ResolvePalm(side);
        if (t == null) return false;
        palm = t.position;
        return true;
    }

    /// <summary>손바닥 본을 찾는다. 한 번 찾으면 붙들고, 그때 <b>무엇이 물렸는지 로그로 남긴다.</b></summary>
    private Transform ResolvePalm(GripFingerTip.Side side)
    {
        bool left = side == GripFingerTip.Side.Left;
        Transform assigned = left ? leftPalm : rightPalm;
        if (assigned != null) return assigned;

        if (left ? palmSearchedL : palmSearchedR) return null;   // 한 번 실패했으면 매 프레임 뒤지지 않는다

        ChunaPathEvaluator evaluator = FindFirstObjectByType<ChunaPathEvaluator>();
        string field = left ? "playerLeftHand" : "playerRightHand";
        System.Reflection.FieldInfo info = evaluator == null ? null : typeof(ChunaPathEvaluator).GetField(
            field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Component hand = info != null ? info.GetValue(evaluator) as Component : null;

        if (hand == null)
        {
            if (left) palmSearchedL = true; else palmSearchedR = true;
            ChunaLogger.LogWarning($"[GripJudge] {field}가 비어 있어 손바닥을 못 찾습니다 — " +
                                   $"인스펙터의 '{(left ? "leftPalm" : "rightPalm")}'에 직접 넣어 주세요.");
            return null;
        }

        // ★후보 순서에 근거가 있다. Palm이 있으면 그게 정확하고, 없으면 손목이 손바닥에 제일 가깝다.
        //   Middle1(중지 밑마디)은 구형 리그에서 손바닥 대용으로 쓰던 자리다(두개골 술기 전례).
        Transform found = FindBone(hand.transform, "palm")
                       ?? FindBone(hand.transform, "wrist")
                       ?? FindBone(hand.transform, "middle1");

        if (found != null)
        {
            if (left) leftPalm = found; else rightPalm = found;
            ChunaLogger.Log($"<color=cyan>[GripJudge] {(left ? "왼" : "오른")}손바닥 = " +
                            $"{PathOf(found)}</color>");
        }
        else
        {
            if (left) palmSearchedL = true; else palmSearchedR = true;
            ChunaLogger.LogWarning($"[GripJudge] {hand.name} 아래에서 손바닥(palm/wrist/middle1)을 못 찾았습니다 — " +
                                   $"인스펙터의 '{(left ? "leftPalm" : "rightPalm")}'에 직접 넣어 주세요.");
        }
        return found;
    }

    private bool palmSearchedL, palmSearchedR;

    /// <summary>손 루트 아래에서 이름에 keyword가 들어간 첫 본. ★끝마디 접미사를 안 본다.</summary>
    private static Transform FindBone(Transform root, string keyword)
    {
        if (root.name.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) >= 0) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform f = FindBone(root.GetChild(i), keyword);
            if (f != null) return f;
        }
        return null;
    }

    /// <summary>이 손의 엄지·검지가 지금 각각 믿을 만한가. 진단 표시용이다.</summary>
    public void GetFingerValidity(GripFingerTip.Side side, out bool thumbOk, out bool indexOk)
    {
        if (side == GripFingerTip.Side.Left)
        {
            UpdateFingerTrack(ref trackL, leftThumbTip, leftIndexTip, "왼");
            thumbOk = trackL.thumbOk; indexOk = trackL.indexOk;
        }
        else
        {
            UpdateFingerTrack(ref trackR, rightThumbTip, rightIndexTip, "오른");
            thumbOk = trackR.thumbOk; indexOk = trackR.indexOk;
        }
    }

    /// <summary>진단용 — 한 손가락으로 이어간 총 시간(초)과 기준 재설정 횟수.</summary>
    public void GetFingerDiagnostics(GripFingerTip.Side side, out float soloTotalSeconds, out int rebaselines)
    {
        FingerTrack t = side == GripFingerTip.Side.Left ? trackL : trackR;
        soloTotalSeconds = t.soloTotal;
        rebaselines = t.rebaselines;
    }

    // ================= 손가락 유효성 =================

    /// <summary>
    /// 한 손의 엄지·검지 추적 상태. ★구조체다 — 매 프레임 도는 경로라 할당을 만들지 않는다.
    /// </summary>
    private struct FingerTrack
    {
        public int frame;                    // 이 프레임에 이미 계산했는가
        public bool started;
        public Vector3 lastThumb, lastIndex;

        public float gapRef;                 // 둘 다 성했을 때의 엄지-검지 간격
        public bool gapRefValid;

        // ★<b>로컬</b> 오프셋이다. 월드로 들고 있으면 머리가 도는 동안 어긋난다 —
        //   신전에서 45도를 도는데 3cm 오프셋을 월드로 업고 가면 2cm 넘게 틀어진다.
        public Vector3 thumbLocalOffset, indexLocalOffset;
        public bool offsetValid;

        public bool thumbOk, indexOk;
        public float soloSeconds;            // 지금 한 손가락으로 버틴 시간
        public float soloTotal;              // 진단용 누적
        public int rebaselines;              // 기준을 다시 잡은 횟수

        public Vector3 pinch;
        public bool pinchValid;
    }

    private FingerTrack trackL, trackR;

    /// <summary>
    /// 이 손의 파지점을 이 프레임에 한 번 정한다.
    /// ★<b>프레임 캐시를 쓴다</b> — 한 프레임에 여러 곳(측정기·게이지·압박)이 부르는데,
    ///   Update에서 계산하면 실행 순서에 따라 한 프레임 낡은 값을 주게 된다.
    /// </summary>
    private void UpdateFingerTrack(ref FingerTrack t, Transform thumb, Transform index, string sideName)
    {
        if (t.frame == Time.frameCount) return;
        t.frame = Time.frameCount;

        // ★엄지 단독 — 검지를 아예 안 본다.
        //   보정도 이어가기도 없다. 가려지지 않는 점 하나를 <b>그대로</b> 쓰는 게 이 모드의 전부다.
        //   ★손 단위 튐 거르기는 위층(CervicalRomRealityMeasure.AcceptHand)이 이미 하고 있다.
        //     여기서 한 번 더 거르면 이중으로 버려 진행이 멎을 수 있어, 여기서는 통과시킨다.
        //     (속도 초과는 thumbOk에 남겨 로그로만 드러낸다.)
        if (ThumbOnly)
        {
            t.indexOk = false;
            t.gapRefValid = false;
            t.offsetValid = false;

            if (thumb == null) { t.thumbOk = false; t.pinchValid = false; t.started = false; return; }

            Vector3 pThumb = thumb.position;
            float dtThumb = Mathf.Max(1e-4f, Time.deltaTime);

            t.thumbOk = !t.started || maxFingerSpeed <= 0f
                        || (pThumb - t.lastThumb).magnitude / dtThumb <= maxFingerSpeed;

            t.started = true;
            t.lastThumb = pThumb;
            t.pinch = pThumb;
            t.pinchValid = true;
            return;
        }

        if (thumb == null || index == null)
        {
            // 배선이 비었다 — 종전과 같이 '못 읽음'이다. 한 쪽만 있으면 그거라도 준다.
            t.thumbOk = thumb != null; t.indexOk = index != null;
            t.pinchValid = thumb != null || index != null;
            if (t.pinchValid) t.pinch = thumb != null ? thumb.position : index.position;
            t.started = false;
            return;
        }

        Vector3 pT = thumb.position, pI = index.position;
        float dt = Mathf.Max(1e-4f, Time.deltaTime);

        if (!t.started)
        {
            t.started = true;
            Rebaseline(ref t, thumb, index, pT, pI, dt, snap: true);
            return;
        }

        float vThumb = (pT - t.lastThumb).magnitude / dt;
        float vIndex = (pI - t.lastIndex).magnitude / dt;
        t.lastThumb = pT; t.lastIndex = pI;

        JudgeFingers(ref t, pT, pI, vThumb, vIndex, out bool thumbOk, out bool indexOk);
        t.thumbOk = thumbOk; t.indexOk = indexOk;

        if (thumbOk && indexOk)
        {
            if (t.soloSeconds > 0f && showDebugLogs)
            {
                ChunaLogger.Log($"<color=#7ad67a>[GripJudge/{sideName}손] 두 손가락 복귀 " +
                                $"(한 손가락으로 {t.soloSeconds:F1}초 이어감)</color>");
            }
            t.soloSeconds = 0f;
            // ★평소 경로다. 간격 기준은 천천히만 따라간다.
            Rebaseline(ref t, thumb, index, pT, pI, dt, snap: false);
            return;
        }

        // 둘 다 못 믿거나, 이어가기를 껐거나, 업고 갈 오프셋이 아직 없으면 — 못 읽는 것으로 넘긴다.
        // ★위층(CervicalRomRealityMeasure)이 '손을 못 읽음'으로 받아 홀드 타이머를 <b>얼린다</b>.
        //   조용히 틀린 값보다 없는 값이 낫다.
        if (!singleFingerFallback || !t.offsetValid || (!thumbOk && !indexOk))
        {
            t.pinchValid = false;
            return;
        }

        t.soloSeconds += dt;
        t.soloTotal += dt;

        // ★<b>포기하지 않고 다시 잡는다.</b> 손을 진짜로 고쳐 잡으면 간격이 영영 달라지는데,
        //   그때 계속 한 손가락만 쓰면 낡은 오프셋을 무한정 업고 간다.
        if (t.soloSeconds > singleFingerMaxSeconds)
        {
            t.rebaselines++;
            if (showDebugLogs)
            {
                ChunaLogger.Log($"<color=yellow>[GripJudge/{sideName}손] 한 손가락으로 " +
                                $"{singleFingerMaxSeconds:F1}초를 넘겼다 — 지금 두 손가락을 새 기준으로 다시 잡는다 " +
                                $"(누적 {t.rebaselines}회)</color>");
            }
            t.soloSeconds = 0f;
            t.thumbOk = t.indexOk = true;
            // ★여기는 <b>즉시</b> 새 간격을 받는다 — 고쳐 잡았다고 인정하는 자리다.
            Rebaseline(ref t, thumb, index, pT, pI, dt, snap: true);
            return;
        }

        // ★성한 손가락의 <b>로컬</b> 오프셋을 업고 간다.
        //   그냥 그 손가락 위치를 쓰면 중점→단일로 바뀌는 순간 핀치폭의 절반만큼 계단이 진다.
        t.pinch = thumbOk ? pT + thumb.rotation * t.thumbLocalOffset
                          : pI + index.rotation * t.indexLocalOffset;
        t.pinchValid = true;
    }

    /// <summary>
    /// ★<b>유효성 판단은 여기 하나뿐이다.</b> 나중에 OVRHand 신뢰도를 쓰게 되면
    /// 이 함수 안에서만 조건을 더하면 되고, 부르는 쪽은 손댈 것이 없다.
    /// </summary>
    private void JudgeFingers(ref FingerTrack t, Vector3 pT, Vector3 pI,
                              float vThumb, float vIndex, out bool thumbOk, out bool indexOk)
    {
        bool fastThumb = maxFingerSpeed > 0f && vThumb > maxFingerSpeed;
        bool fastIndex = maxFingerSpeed > 0f && vIndex > maxFingerSpeed;

        thumbOk = !fastThumb;
        indexOk = !fastIndex;

        if (!thumbOk || !indexOk) return;

        // 속도로는 안 걸렸는데 간격이 어긋났다 — 강체라면 있을 수 없다. 더 움직인 쪽을 범인으로 본다.
        float gap = Vector3.Distance(pT, pI);
        if (t.gapRefValid && fingerGapTolerance > 0f
            && Mathf.Abs(gap - t.gapRef) > fingerGapTolerance)
        {
            if (vThumb >= vIndex) thumbOk = false;
            else indexOk = false;
        }
    }

    /// <summary>
    /// 둘 다 믿을 만한 지금을 기준으로 삼는다 — 간격·오프셋·파지점을 한꺼번에 다시 잡는다.
    ///
    /// ★<b>간격 기준만 천천히 따라간다</b>(<paramref name="snap"/>이 false일 때).
    ///   지금 값으로 매 프레임 덮으면 gapRef가 늘 <b>직전 프레임</b> 값이 돼서,
    ///   간격 검사가 "프레임 간 변화량" 검사로 쪼그라든다 — 그건 속도 검사가 이미 하는 일이라
    ///   <b>없느니만 못하다</b>(2026-09-02에 쓰자마자 밟았다).
    ///   천천히 따라가야 ①손을 진짜 고쳐 잡으면 1초쯤 뒤 새 간격을 받아들이고
    ///   ②가려져 미끄러지는 것은 기준에서 벌어져 잡힌다.
    /// </summary>
    private void Rebaseline(ref FingerTrack t, Transform thumb, Transform index,
                            Vector3 pT, Vector3 pI, float dt, bool snap)
    {
        Vector3 pinch = (pT + pI) * 0.5f;
        float gap = Vector3.Distance(pT, pI);

        t.lastThumb = pT; t.lastIndex = pI;
        if (snap || !t.gapRefValid)
        {
            t.gapRef = gap;
        }
        else
        {
            float k = 1f - Mathf.Exp(-dt / Mathf.Max(1e-4f, gapFollowSeconds));
            t.gapRef = Mathf.Lerp(t.gapRef, gap, k);
        }
        t.gapRefValid = true;
        t.thumbLocalOffset = Quaternion.Inverse(thumb.rotation) * (pinch - pT);
        t.indexLocalOffset = Quaternion.Inverse(index.rotation) * (pinch - pI);
        t.offsetValid = true;
        t.thumbOk = t.indexOk = true;
        t.pinch = pinch;
        t.pinchValid = true;
    }

    /// <summary>
    /// 한 손의 <b>핀치 폭</b> — 엄지 끝과 검지 끝 사이 거리(m).
    ///
    /// ★실측(ROM 평가)에서 "허공에서 손이 멈춘 것"과 "환자 머리를 잡은 것"을 가르는 데 쓴다.
    ///   손을 펴고 있으면 8~15cm쯤 벌어지고, 무언가를 집고 있으면 그 두께만큼만 벌어진다.
    ///   손끝 위치만 보는 판정에는 이 정보가 없어서 정지 시간만으로 확정할 수밖에 없었다.
    /// </summary>
    public bool TryGetPinchWidth(GripFingerTip.Side side, out float width)
    {
        Transform thumb = side == GripFingerTip.Side.Left ? leftThumbTip : rightThumbTip;
        Transform index = side == GripFingerTip.Side.Left ? leftIndexTip : rightIndexTip;

        width = 0f;
        if (thumb == null || index == null) return false;

        // ★한 손가락이 튄 상태면 폭은 <b>뜻이 없다</b>. 그 값으로 게이트를 걸면 오판정이 된다.
        GetFingerValidity(side, out bool thumbOk, out bool indexOk);
        if (!thumbOk || !indexOk) return false;

        width = Vector3.Distance(thumb.position, index.position);
        return true;
    }

    /// <summary>
    /// 한 손이 잡고 있는 지점(엄지·검지 중점, 월드).
    /// ★파지점과 <b>같은 판별</b>을 탄다(2026-09-02). 압박 판정도 튄 손가락을 물면 같이 틀어진다 —
    ///   여기만 종전 평균을 쓰면 각도는 성한데 압박만 조용히 어긋난다.
    /// </summary>
    public bool TryGetHandCluster(GripFingerTip.Side side, out Vector3 cluster)
    {
        if (TryGetPinchPoint(side, out cluster)) return true;

        // 파지점이 못 나오는 경우(배선이 한쪽만 있거나 둘 다 못 믿을 때)에도
        // 종전처럼 있는 것만으로 평균을 낸다 — 이 경로는 접촉 판정에도 쓰여서 조용히 죽으면 안 된다.
        Transform thumb = side == GripFingerTip.Side.Left ? leftThumbTip : rightThumbTip;
        Transform index = side == GripFingerTip.Side.Left ? leftIndexTip : rightIndexTip;

        cluster = Vector3.zero;
        int n = 0;
        if (thumb != null) { cluster += thumb.position; n++; }
        if (index != null) { cluster += index.position; n++; }
        if (n == 0) return false;
        cluster /= n;
        return true;
    }

    /// <summary>
    /// 접촉점 A를 잡은 손 → 접촉점 B를 잡은 손 벡터(월드).
    ///
    /// ★<b>이 벡터는 머리와 같이 돈다.</b> 손을 옮기지 않고 손목만 틀어도 각이 잡히므로,
    ///   중점의 이동 거리로 재는 방식이 못 잡는 동작을 잡는다.
    /// ★좌우 손이 바뀌어도 부호가 뒤집히지 않는다 — 손이 아니라 <b>접촉점 순서</b>로 방향을 잡는다.
    ///   (왼손→오른손으로 잡으면 시술자가 손을 반대로 대는 순간 압박이 음수가 된다.)
    /// </summary>
    public bool TryGetContactPairVector(out Vector3 pairVector)
    {
        pairVector = Vector3.zero;

        GripContactPoint a = PairA, b = PairB;
        if (a == null || b == null) return false;

        GripFingerTip.Side aSide, bSide;
        if (a.LeftGripping && b.RightGripping)
        {
            aSide = GripFingerTip.Side.Left; bSide = GripFingerTip.Side.Right;
        }
        else if (a.RightGripping && b.LeftGripping)
        {
            aSide = GripFingerTip.Side.Right; bSide = GripFingerTip.Side.Left;
        }
        else
        {
            return false;   // 아직 두 점을 서로 다른 손으로 잡지 않았다
        }

        if (!TryGetHandCluster(aSide, out Vector3 pa)) return false;
        if (!TryGetHandCluster(bSide, out Vector3 pb)) return false;

        pairVector = pb - pa;
        return pairVector.sqrMagnitude > 1e-6f;
    }

    /// <summary>진단용 — 두 접촉점을 각각 어느 손이 집고 있는지. 로그에 그대로 쓴다.</summary>
    public bool TryGetGripState(out bool aLeft, out bool aRight, out bool bLeft, out bool bRight)
    {
        aLeft = aRight = bLeft = bRight = false;

        GripContactPoint a = PairA, b = PairB;
        if (a == null || b == null) return false;

        aLeft = a.LeftGripping; aRight = a.RightGripping;
        bLeft = b.LeftGripping; bRight = b.RightGripping;
        return true;
    }

    /// <summary>진단용 — 지금 쌍의 접촉점 이름.</summary>
    public string PairAName => PairA != null ? PairA.name : "(없음)";

    /// <summary>진단용 — 지금 쌍의 접촉점 이름.</summary>
    public string PairBName => PairB != null ? PairB.name : "(없음)";

    private GripContactPoint PairA => currentPair == GripPair.Sagittal ? forehead
                                    : currentPair == GripPair.Lateral ? temporalLeft : null;
    private GripContactPoint PairB => currentPair == GripPair.Sagittal ? occiput
                                    : currentPair == GripPair.Lateral ? temporalRight : null;

    private void Awake()
    {
        // ★프레임 캐시의 초기값. 0으로 두면 첫 프레임(frameCount 0)을 '이미 계산했다'고 본다.
        trackL.frame = trackR.frame = -1;

        SetPair(GripPair.None);

        // 판정 경로에 직접 꽂는다. 이걸 안 하면 AutoPlay 게이트·게이지·표시구가
        // 전부 기존 손바닥 판정을 따라간다.
        ChunaPathEvaluator evaluator = FindFirstObjectByType<ChunaPathEvaluator>();
        if (evaluator != null)
        {
            evaluator.SetExternalContactSource(this);
            ChunaLogger.Log("<color=cyan>[GripJudge] 접촉 판정을 가져왔다 — 엄지·검지가 두 점에 닿아야 인정한다.</color>");
        }
        else
        {
            ChunaLogger.LogWarning("[GripJudge] ChunaPathEvaluator를 찾지 못해 판정을 넘겨받지 못했습니다.");
        }
    }

    /// <summary>어느 쌍을 볼지 정한다. 쓰는 쌍만 켜고, 그 쌍은 렌더러를 끈다.</summary>
    public void SetPair(GripPair pair)
    {
        currentPair = pair;
        IsGripped = false;

        // ★판정을 가져간 동안에는 머리 구체를 끈다. 접촉점과 겹쳐 있어 방해가 된다.
        //   해제하면 원래대로 돌려놓는다.
        SetHeadColliderEnabled(pair == GripPair.None);

        Apply(forehead, pair == GripPair.Sagittal);
        Apply(occiput, pair == GripPair.Sagittal);
        Apply(temporalLeft, pair == GripPair.Lateral);
        Apply(temporalRight, pair == GripPair.Lateral);

        if (showDebugLogs) ChunaLogger.Log($"<color=cyan>[GripJudge] 접촉점 전환: {pair}</color>");
    }

    private void OnDisable()
    {
        SetHeadColliderEnabled(true);   // 꺼둔 채로 남기지 않는다
    }

    /// <summary>
    /// ★손끝 표식은 런타임에 붙인다.
    ///   OpenXR 손 리그(XRHand_*)는 Play에서 만들어지므로 에디터 메뉴로는 붙일 수 없다.
    ///   에디터에서 붙이면 구형 OculusHand(b_l_*)만 잡히고, Play에서 붙이면 Play가
    ///   끝날 때 사라진다(2026-08-24). 그래서 매번 시작할 때 스스로 붙인다.
    /// </summary>
    private System.Collections.IEnumerator Start()
    {
        if (ignoreAssignedTips)
        {
            leftThumbTip = leftIndexTip = rightThumbTip = rightIndexTip = null;
            ChunaLogger.Log("<color=cyan>[GripJudge] 인스펙터 손끝 배정을 무시하고 다시 찾는다.</color>");
        }

        for (int attempt = 0; attempt < 30; attempt++)
        {
            int ready = 0;
            ready += AttachTip(GripFingerTip.Side.Left, GripFingerTip.Finger.Thumb);
            ready += AttachTip(GripFingerTip.Side.Left, GripFingerTip.Finger.Index);
            ready += AttachTip(GripFingerTip.Side.Right, GripFingerTip.Finger.Thumb);
            ready += AttachTip(GripFingerTip.Side.Right, GripFingerTip.Finger.Index);

            if (ready == 4)
            {
                // ★무엇을 물었는지 경로째 남긴다. '엄지·검지 끝'이 아니라 손목 쪽 뼈를 물면
                //   손목만 틀어도 파지 지점이 움직여 각도가 흔들린다 — 그때 여기서 바로 보인다.
                ChunaLogger.Log("<color=cyan>[GripJudge] 손끝 표식 4개 준비 완료.\n" +
                                $"  L엄지 {PathOf(leftThumbTip)}\n" +
                                $"  L검지 {PathOf(leftIndexTip)}\n" +
                                $"  R엄지 {PathOf(rightThumbTip)}\n" +
                                $"  R검지 {PathOf(rightIndexTip)}</color>");
                yield break;
            }
            yield return new WaitForSeconds(0.5f);   // 손 리그가 생길 때까지 기다린다
        }

        ChunaLogger.LogWarning("[GripJudge] 손끝 표식을 다 붙이지 못했습니다 — " +
                               "인스펙터의 leftThumbTip / leftIndexTip / rightThumbTip / rightIndexTip에 " +
                               "손끝 트랜스폼을 직접 넣어 주세요.");
    }

    /// <summary>손끝 뼈에 표식과 트리거 콜라이더를 붙인다. 이미 있으면 그대로 둔다.</summary>
    private int AttachTip(GripFingerTip.Side side, GripFingerTip.Finger finger)
    {
        Transform bone = FindTipUnderPlayerHand(side, finger, quiet: true);
        if (bone == null) return 0;

        // 찾은 손끝을 보관한다. 압박 구간에서 중점을 내는 데 쓴다.
        if (side == GripFingerTip.Side.Left)
        {
            if (finger == GripFingerTip.Finger.Thumb) leftThumbTip = bone; else leftIndexTip = bone;
        }
        else
        {
            if (finger == GripFingerTip.Finger.Thumb) rightThumbTip = bone; else rightIndexTip = bone;
        }

        GripFingerTip tip = bone.GetComponent<GripFingerTip>();
        if (tip == null)
        {
            tip = bone.gameObject.AddComponent<GripFingerTip>();
            tip.Configure(side, finger);
            if (showDebugLogs)
                ChunaLogger.Log($"<color=cyan>[GripJudge] {bone.name} ← {side} {finger} 표식</color>");
        }
        else
        {
            tip.Configure(side, finger);
        }

        Collider col = bone.GetComponent<Collider>();
        if (col == null)
        {
            SphereCollider sc = bone.gameObject.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = fingerTipRadius;
        }
        else if (!col.isTrigger)
        {
            col.isTrigger = true;
        }

        return 1;
    }

    private void Update()
    {
        GripContactPoint a = PairA, b = PairB;
        if (a == null || b == null)
        {
            IsGripped = false;
            return;
        }

        // ★모드를 매 프레임 밀어 넣는다 — Play 중에 인스펙터에서 fingerSource를 바꿔도 바로 듣게.
        //   대입 두 개뿐이라 프레임 예산에 영향이 없다.
        a.ThumbOnly = b.ThumbOnly = ThumbOnly;

        // 서로 다른 손이 두 점을 하나씩 집으면 성립.
        bool gripped = (a.LeftGripping && b.RightGripping) || (a.RightGripping && b.LeftGripping);

        if (gripped != IsGripped)
        {
            IsGripped = gripped;
            if (showDebugLogs)
            {
                ChunaLogger.Log($"<color={(gripped ? "green" : "yellow")}>[GripJudge] {currentPair} 파지 " +
                                $"{(gripped ? "성립" : "해제")} — " +
                                $"A(왼{a.LeftGripping}/오{a.RightGripping}) B(왼{b.LeftGripping}/오{b.RightGripping})</color>");
            }
        }
    }

    /// <summary>
    /// ChunaPathEvaluator가 들고 있는 머리 구체(patientHeadCollider)를 켜고 끈다.
    /// 파지 판정을 이쪽이 가져간 동안에는 그 구체가 필요 없고, 접촉점과 겹쳐 방해만 된다.
    /// </summary>
    private void SetHeadColliderEnabled(bool enabled)
    {
        if (headCollider == null)
        {
            ChunaPathEvaluator evaluator = FindFirstObjectByType<ChunaPathEvaluator>();
            if (evaluator == null) return;

            System.Reflection.FieldInfo info = typeof(ChunaPathEvaluator).GetField(
                "patientHeadCollider",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            headCollider = info != null ? info.GetValue(evaluator) as Collider : null;
            if (headCollider == null) return;
        }

        if (headCollider.enabled == enabled) return;

        headCollider.enabled = enabled;
        ChunaLogger.Log($"<color=cyan>[GripJudge] 머리 구체({headCollider.name}) {(enabled ? "복구" : "끔")}</color>");
    }

    private void Apply(GripContactPoint p, bool inUse)
    {
        if (p == null) return;

        if (p.gameObject.activeSelf != inUse) p.gameObject.SetActive(inUse);
        if (!inUse) return;

        p.RequireBothFingers = requireBothFingers;
        p.ThumbOnly = ThumbOnly;
        foreach (Renderer r in p.GetComponentsInChildren<Renderer>(true)) r.enabled = showSpheres;
    }

#if UNITY_EDITOR
    [ContextMenu("접촉점·손끝 콜라이더 만들기")]
    private void CreateColliders()
    {
        Transform head = FindDeep(transform.root, "CC_Base_Head");
        if (head == null)
        {
            ChunaLogger.LogWarning("[GripJudge] CC_Base_Head를 찾지 못했습니다.");
            return;
        }

        forehead = MakePoint(forehead, head, "접촉점_이마");
        occiput = MakePoint(occiput, head, "접촉점_뒤통수");
        temporalLeft = MakePoint(temporalLeft, head, "접촉점_좌측두");
        temporalRight = MakePoint(temporalRight, head, "접촉점_우측두");

        int tips = 0;
        tips += MakeTip(GripFingerTip.Side.Left, GripFingerTip.Finger.Thumb);
        tips += MakeTip(GripFingerTip.Side.Left, GripFingerTip.Finger.Index);
        tips += MakeTip(GripFingerTip.Side.Right, GripFingerTip.Finger.Thumb);
        tips += MakeTip(GripFingerTip.Side.Right, GripFingerTip.Finger.Index);

        ChunaLogger.Log($"[GripJudge] 접촉점 4개 · 손끝 콜라이더 {tips}개 준비했습니다.\n" +
                        "접촉점은 전부 머리뼈 원점에 있으니 씬에서 이마·뒤통수·좌우 측두로 옮기세요. " +
                        "showSpheres를 켜면 보입니다.");
    }

    /// <summary>
    /// ★배치는 건드리지 않는다. 이미 잡아 둔 위치·회전·크기를 그대로 두고
    ///   콜라이더 종류와 설정만 고친다. 다시 배치하게 만들지 않기 위한 메뉴다.
    /// </summary>
    [ContextMenu("콜라이더 점검·수리 (배치는 그대로)")]
    private void RepairColliders()
    {
        int fixedCount = 0;
        foreach (GripContactPoint p in new[] { forehead, occiput, temporalLeft, temporalRight })
        {
            if (p == null) continue;
            fixedCount += RepairPoint(p);
        }

        // 손끝은 이미 만들어 두었으면 건드리지 않는다. 없을 때만 채운다.
        int tips = 0;
        tips += EnsureTip(GripFingerTip.Side.Left, GripFingerTip.Finger.Thumb);
        tips += EnsureTip(GripFingerTip.Side.Left, GripFingerTip.Finger.Index);
        tips += EnsureTip(GripFingerTip.Side.Right, GripFingerTip.Finger.Thumb);
        tips += EnsureTip(GripFingerTip.Side.Right, GripFingerTip.Finger.Index);

        ChunaLogger.Log($"[GripJudge] 수리 완료 — 접촉점 {fixedCount}건 고침, 손끝 {tips}개 확인. 배치는 그대로 두었습니다.");
    }

    private int RepairPoint(GripContactPoint p)
    {
        int changed = 0;
        GameObject go = p.gameObject;

        // ★c8의 X축 -1 스케일 아래에서는 BoxCollider가 무효다. 캡슐로 갈아끼운다.
        //   크기는 트랜스폼 스케일이 들고 있으므로 위치·회전·스케일은 건드리지 않는다.
        BoxCollider box = go.GetComponent<BoxCollider>();
        if (box != null)
        {
            Vector3 size = box.size;
            Vector3 center = box.center;
            DestroyImmediate(box);

            CapsuleCollider cap = go.AddComponent<CapsuleCollider>();
            cap.isTrigger = true;
            cap.center = center;
            cap.radius = Mathf.Max(size.x, size.z) * 0.5f;
            cap.height = size.y;
            cap.direction = 1;   // Y축
            changed++;
            ChunaLogger.Log($"  {go.name}: BoxCollider → CapsuleCollider (음수 스케일에서 박스는 무효)");
        }

        Collider col = go.GetComponent<Collider>();
        if (col == null)
        {
            CapsuleCollider cap = go.AddComponent<CapsuleCollider>();
            cap.isTrigger = true;
            changed++;
            ChunaLogger.Log($"  {go.name}: 콜라이더가 없어 캡슐을 넣었습니다.");
        }
        else if (!col.isTrigger)
        {
            col.isTrigger = true;
            changed++;
            ChunaLogger.Log($"  {go.name}: Is Trigger를 켰습니다.");
        }

        Rigidbody rb = go.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            changed++;
            ChunaLogger.Log($"  {go.name}: 트리거가 뜨도록 kinematic Rigidbody를 넣었습니다.");
        }
        else if (!rb.isKinematic)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            changed++;
            ChunaLogger.Log($"  {go.name}: Rigidbody를 kinematic으로 바꿨습니다.");
        }

        return changed;
    }

    /// <summary>손끝에 표식과 트리거가 없을 때만 넣는다. 이미 있으면 그대로 둔다.</summary>
    private int EnsureTip(GripFingerTip.Side side, GripFingerTip.Finger finger)
    {
        Transform bone = FindTipUnderPlayerHand(side, finger);
        if (bone == null)
        {
            ChunaLogger.LogWarning($"[GripJudge] 손끝 뼈를 찾지 못했습니다: {side} {finger}");
            return 0;
        }

        GripFingerTip tip = bone.GetComponent<GripFingerTip>();
        if (tip == null)
        {
            tip = bone.gameObject.AddComponent<GripFingerTip>();
            tip.Configure(side, finger);
            ChunaLogger.Log($"  {bone.name}: {side} {finger} 표식을 넣었습니다.");
        }

        Collider col = bone.GetComponent<Collider>();
        if (col == null)
        {
            SphereCollider sc = bone.gameObject.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = 0.012f;
            ChunaLogger.Log($"  {bone.name}: 콜라이더를 넣었습니다.");
        }
        else if (!col.isTrigger)
        {
            col.isTrigger = true;
            ChunaLogger.Log($"  {bone.name}: Is Trigger를 켰습니다.");
        }

        return 1;
    }

    private GripContactPoint MakePoint(GripContactPoint existing, Transform parent, string name)
    {
        if (existing != null) return existing;

        // ★캡슐을 쓴다. c8에 X축 -1 스케일이 걸려 있어 그 밑에서는 BoxCollider가 무효가 된다
        //   (2026-08-24 실측: 기존 파지점 44개가 전부 Capsule 33 · Sphere 11, Box는 0개다).
        //   길쭉해서 이마·측두부 같은 면에도 구체보다 잘 맞는다. 크기·회전은 씬에서 맞춘다.
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localScale = new Vector3(0.05f, 0.035f, 0.05f);

        CapsuleCollider col = go.GetComponent<CapsuleCollider>();
        col.isTrigger = true;

        // 트리거는 한쪽에 Rigidbody가 있어야 뜬다. 손이 아니라 이쪽에 붙인다 —
        // Meta 손 리그에 Rigidbody를 넣으면 상호작용 SDK와 얽힌다.
        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        return go.AddComponent<GripContactPoint>();
    }

    private int MakeTip(GripFingerTip.Side side, GripFingerTip.Finger finger)
    {
        Transform bone = FindTipUnderPlayerHand(side, finger);
        if (bone == null)
        {
            ChunaLogger.LogWarning($"[GripJudge] 손끝 뼈를 찾지 못했습니다: {side} {finger}");
            return 0;
        }

        GripFingerTip tip = bone.GetComponent<GripFingerTip>();
        if (tip == null) tip = bone.gameObject.AddComponent<GripFingerTip>();
        tip.Configure(side, finger);

        SphereCollider col = bone.GetComponent<SphereCollider>();
        if (col == null) col = bone.gameObject.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = 0.012f;

        return 1;
    }

#endif

    // ★런타임에서도 쓰인다 — AttachTip이 부른다.
    //   여태 #if UNITY_EDITOR 안에 있어서 빌드할 때만 CS0103으로 터졌다.
    //   에디터에서는 멀쩡히 돌아 안 보였다(2026-08-26). UnityEditor API는 안 쓴다.
    /// <summary>
    /// ★이름으로 씬 전체를 뒤지면 안 된다. 손 리그가 여러 벌이라 같은 이름이 52개까지 나오고
    ///   (OVR 손·가이드 손·녹화 손), 그중 아무거나 잡으면 실제 트래킹되는 손이 아닐 수 있다.
    ///   2026-08-24 실측: 그래서 왼손 2개에만 표식이 붙고 오른손은 하나도 안 붙었다.
    ///   판정기가 들고 있는 손(playerLeftHand / playerRightHand) 아래에서만 찾는다.
    /// </summary>
    private Transform FindTipUnderPlayerHand(GripFingerTip.Side side, GripFingerTip.Finger finger, bool quiet = false)
    {
        // ★인스펙터에 넣어 두면 그것만 쓴다. 이름 규칙이 리그마다 달라
        //   자동 탐색으로 헤매느니 직접 지정하는 쪽이 확실하다.
        Transform assigned =
            side == GripFingerTip.Side.Left
                ? (finger == GripFingerTip.Finger.Thumb ? leftThumbTip : leftIndexTip)
                : (finger == GripFingerTip.Finger.Thumb ? rightThumbTip : rightIndexTip);
        if (assigned != null) return assigned;

        ChunaPathEvaluator evaluator = FindFirstObjectByType<ChunaPathEvaluator>();
        if (evaluator == null) return null;

        string field = side == GripFingerTip.Side.Left ? "playerLeftHand" : "playerRightHand";
        System.Reflection.FieldInfo info = typeof(ChunaPathEvaluator).GetField(
            field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Component hand = info != null ? info.GetValue(evaluator) as Component : null;
        if (hand == null)
        {
            if (!quiet)
                ChunaLogger.LogWarning($"[GripJudge] ChunaPathEvaluator의 {field}가 비어 있습니다 — 인스펙터에 손끝을 직접 넣어 주세요.");
            return null;
        }

        // ★뼈 이름의 좌우 접두(b_l_ / b_r_)로 가르면 안 된다.
        //   이 프로젝트의 오른손 리그도 뼈 이름이 b_l_* 이다(2026-08-24 실측:
        //   HANDR1/Model/l_handMeshNode/b_l_wrist. b_r_wrist는 씬 전체에 1곳뿐).
        //   좌우는 어느 손 루트 아래인지로만 가르고, 이름은 손가락 종류만 본다.
        string keyword = finger == GripFingerTip.Finger.Thumb ? "thumb" : "index";

        // ★실제로 도는 손은 OpenXR 리그다 — XRHand_ThumbTip / XRHand_IndexTip.
        //   b_l_* / b_r_* 는 비활성인 구형 OculusHand 리그의 이름이라 거기 붙이면
        //   화면의 손과 무관한 곳에서 판정이 난다(2026-08-24).
        Transform found = FindTip(hand.transform, keyword, "Tip")     // OpenXR 리그
                       ?? FindTip(hand.transform, keyword, "_null")   // 구형 리그 끝마디
                       ?? FindTip(hand.transform, keyword, "3");      // 구형 리그 마지막 관절

        if (found == null && !quiet)
        {
            ChunaLogger.LogWarning($"[GripJudge] {hand.name} 아래에서 {side} {finger} 끝을 찾지 못했습니다 — " +
                                   $"인스펙터의 '{(side == GripFingerTip.Side.Left ? "left" : "right")}" +
                                   $"{(finger == GripFingerTip.Finger.Thumb ? "ThumbTip" : "IndexTip")}'에 " +
                                   "손끝 트랜스폼을 직접 넣어 주세요.");
        }
        return found;
    }

    /// <summary>진단용 — 트랜스폼의 하이어라키 경로.</summary>
    private static string PathOf(Transform t)
    {
        if (t == null) return "(없음)";
        string s = t.name;
        Transform p = t.parent;
        int guard = 0;
        while (p != null && guard++ < 6) { s = p.name + "/" + s; p = p.parent; }
        return s;
    }

    /// <summary>손 루트 아래에서 이름에 keyword가 들어가고 suffix로 끝나는 뼈를 찾는다.</summary>
    private static Transform FindTip(Transform root, string keyword, string suffix)
    {
        string n = root.name;
        if (n.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) >= 0 &&
            n.EndsWith(suffix, System.StringComparison.Ordinal))
        {
            return root;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform f = FindTip(root.GetChild(i), keyword, suffix);
            if (f != null) return f;
        }
        return null;
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform f = FindDeep(root.GetChild(i), name);
            if (f != null) return f;
        }
        return null;
    }
}
