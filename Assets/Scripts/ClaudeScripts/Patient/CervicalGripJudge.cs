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

    [Header("=== 판정 ===")]
    [Tooltip("끄면 엄지와 검지 중 하나만 닿아도 인정한다.")]
    [SerializeField] private bool requireBothFingers = true;

    [Header("=== 표시 ===")]
    [Tooltip("접촉점 구체를 보이게 할지. 위치를 잡을 때만 켜면 된다.")]
    [SerializeField] private bool showSpheres = false;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = false;

    private GripPair currentPair = GripPair.None;

    /// <summary>두 접촉점이 서로 다른 손에 각각 잡혔는가.</summary>
    public bool IsGripped { get; private set; }

    /// <summary>쌍이 정해진 동안에만 판정을 가져간다. None이면 기존 판정이 그대로 돈다.</summary>
    public bool IsActive => currentPair != GripPair.None;

    public GripPair CurrentPair => currentPair;

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

        Apply(forehead, pair == GripPair.Sagittal);
        Apply(occiput, pair == GripPair.Sagittal);
        Apply(temporalLeft, pair == GripPair.Lateral);
        Apply(temporalRight, pair == GripPair.Lateral);

        if (showDebugLogs) ChunaLogger.Log($"<color=cyan>[GripJudge] 접촉점 전환: {pair}</color>");
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
        tips += MakeTip("b_l_thumb_null", GripFingerTip.Side.Left, GripFingerTip.Finger.Thumb);
        tips += MakeTip("b_l_index_null", GripFingerTip.Side.Left, GripFingerTip.Finger.Index);
        tips += MakeTip("b_r_thumb_null", GripFingerTip.Side.Right, GripFingerTip.Finger.Thumb);
        tips += MakeTip("b_r_index_null", GripFingerTip.Side.Right, GripFingerTip.Finger.Index);

        ChunaLogger.Log($"[GripJudge] 접촉점 4개 · 손끝 콜라이더 {tips}개 준비했습니다.\n" +
                        "접촉점은 전부 머리뼈 원점에 있으니 씬에서 이마·뒤통수·좌우 측두로 옮기세요. " +
                        "showSpheres를 켜면 보입니다.");
    }

    private GripContactPoint MakePoint(GripContactPoint existing, Transform parent, string name)
    {
        if (existing != null) return existing;

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localScale = Vector3.one * 0.06f;

        SphereCollider col = go.GetComponent<SphereCollider>();
        col.isTrigger = true;

        // 트리거는 한쪽에 Rigidbody가 있어야 뜬다. 손이 아니라 이쪽에 붙인다 —
        // Meta 손 리그에 Rigidbody를 넣으면 상호작용 SDK와 얽힌다.
        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        return go.AddComponent<GripContactPoint>();
    }

    private int MakeTip(string boneName, GripFingerTip.Side side, GripFingerTip.Finger finger)
    {
        Transform bone = FindDeepInScene(boneName);
        if (bone == null)
        {
            ChunaLogger.LogWarning($"[GripJudge] 손끝 뼈를 찾지 못했습니다: {boneName}");
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

    private static Transform FindDeepInScene(string name)
    {
        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (t.name == name) return t;
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
