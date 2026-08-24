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

    [Header("=== 판정 ===")]
    [Tooltip("손끝 콜라이더 반경(m).")]
    [SerializeField] private float fingerTipRadius = 0.012f;

    [Tooltip("끄면 엄지와 검지 중 하나만 닿아도 인정한다.")]
    [SerializeField] private bool requireBothFingers = true;

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
    /// </summary>
    public bool TryGetGripMidpoint(out Vector3 midpoint)
    {
        midpoint = Vector3.zero;
        int n = 0;
        foreach (Transform t in new[] { leftThumbTip, leftIndexTip, rightThumbTip, rightIndexTip })
        {
            if (t == null) continue;
            midpoint += t.position;
            n++;
        }
        if (n == 0) return false;
        midpoint /= n;
        return true;
    }

    private GripContactPoint PairA => currentPair == GripPair.Sagittal ? forehead
                                    : currentPair == GripPair.Lateral ? temporalLeft : null;
    private GripContactPoint PairB => currentPair == GripPair.Sagittal ? occiput
                                    : currentPair == GripPair.Lateral ? temporalRight : null;

    private void Awake()
    {
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
        for (int attempt = 0; attempt < 30; attempt++)
        {
            int ready = 0;
            ready += AttachTip(GripFingerTip.Side.Left, GripFingerTip.Finger.Thumb);
            ready += AttachTip(GripFingerTip.Side.Left, GripFingerTip.Finger.Index);
            ready += AttachTip(GripFingerTip.Side.Right, GripFingerTip.Finger.Thumb);
            ready += AttachTip(GripFingerTip.Side.Right, GripFingerTip.Finger.Index);

            if (ready == 4)
            {
                ChunaLogger.Log("<color=cyan>[GripJudge] 손끝 표식 4개 준비 완료.</color>");
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
#endif
}
