using UnityEngine;

/// <summary>
/// 경추 ROM의 파지 판정. 손바닥이 아니라 <b>엄지·검지 끝</b>으로 집는 것을 본다.
///
/// 접촉점은 하나가 아니라 <b>쌍</b>이다. 면마다 잡는 곳이 다르기 때문이다.
///   시상면(굴곡·신전)   이마 · 뒤통수
///   관상면·횡단면       좌 측두부 · 우 측두부
///
/// ★어느 손이 어디를 잡는지는 따지지 않는다. 두 접촉점이 <b>서로 다른 손</b>에 각각
///   잡히면 성립이다(시술자가 좌우 어느 쪽에 서든 되게).
/// </summary>
public class CervicalGripJudge : MonoBehaviour
{
    public enum GripPair
    {
        None,
        Sagittal,   // 이마 · 뒤통수
        Lateral,    // 좌 측두 · 우 측두
    }

    [Header("=== 접촉점 (비우면 만들기 메뉴로 생성) ===")]
    [SerializeField] private Transform forehead;
    [SerializeField] private Transform occiput;
    [SerializeField] private Transform temporalLeft;
    [SerializeField] private Transform temporalRight;

    [Header("=== 손가락 끝 (비우면 이름으로 자동 탐색) ===")]
    [SerializeField] private Transform leftThumbTip;
    [SerializeField] private Transform leftIndexTip;
    [SerializeField] private Transform rightThumbTip;
    [SerializeField] private Transform rightIndexTip;

    [Header("=== 판정 ===")]
    [Tooltip("접촉점 반경(m). 손가락 끝이 이 안에 들어오면 닿은 것으로 본다.")]
    [SerializeField] private float grabRadius = 0.05f;

    [Tooltip("켜면 엄지와 검지가 <b>둘 다</b> 반경 안에 있어야 한다. 끄면 둘 중 하나로 인정.")]
    [SerializeField] private bool requireBothFingers = true;

    [Header("=== 표시 ===")]
    [Tooltip("접촉점 구체를 보이게 할지. 기본은 꺼 둔다 — 판정만 하고 눈에는 안 띄게 한다.
" +
             "접촉점 위치를 맞출 때만 잠깐 켜면 된다.")]
    [SerializeField] private bool showSpheres = false;
    [SerializeField] private Color idleColor = new Color(1f, 0.45f, 0.45f, 0.55f);
    [SerializeField] private Color grippedColor = new Color(0.3f, 1f, 0.45f, 0.75f);

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = false;

    private GripPair currentPair = GripPair.None;
    private int retriesLeft = 5;
    private Renderer aRenderer, bRenderer;
    private MaterialPropertyBlock block;

    /// <summary>두 접촉점이 서로 다른 손에 각각 잡혔는가.</summary>
    public bool IsGripped { get; private set; }

    /// <summary>지금 보고 있는 쌍.</summary>
    public GripPair CurrentPair => currentPair;

    private void Awake()
    {
        AutoFindFingerTips();
        block = new MaterialPropertyBlock();
        SetPair(GripPair.None);
    }

    /// <summary>어느 쌍을 볼지 정한다. 해당 접촉점만 켜진다.</summary>
    public void SetPair(GripPair pair)
    {
        currentPair = pair;
        IsGripped = false;

        Show(forehead, pair == GripPair.Sagittal);
        Show(occiput, pair == GripPair.Sagittal);
        Show(temporalLeft, pair == GripPair.Lateral);
        Show(temporalRight, pair == GripPair.Lateral);

        aRenderer = PairA != null ? PairA.GetComponentInChildren<Renderer>() : null;
        bRenderer = PairB != null ? PairB.GetComponentInChildren<Renderer>() : null;

        if (showDebugLogs)
            ChunaLogger.Log($"<color=cyan>[GripJudge] 접촉점 전환: {pair}</color>");
    }

    private Transform PairA => currentPair == GripPair.Sagittal ? forehead
                             : currentPair == GripPair.Lateral ? temporalLeft : null;
    private Transform PairB => currentPair == GripPair.Sagittal ? occiput
                             : currentPair == GripPair.Lateral ? temporalRight : null;

    private void Update()
    {
        if (currentPair == GripPair.None || PairA == null || PairB == null)
        {
            IsGripped = false;
            return;
        }

        bool leftOnA = HandOn(PairA, leftThumbTip, leftIndexTip);
        bool leftOnB = HandOn(PairB, leftThumbTip, leftIndexTip);
        bool rightOnA = HandOn(PairA, rightThumbTip, rightIndexTip);
        bool rightOnB = HandOn(PairB, rightThumbTip, rightIndexTip);

        // ★손 좌우를 따지지 않는다. 서로 다른 손이 두 점을 하나씩 잡으면 성립.
        bool gripped = (leftOnA && rightOnB) || (rightOnA && leftOnB);

        if (gripped != IsGripped)
        {
            IsGripped = gripped;
            if (showDebugLogs)
            {
                ChunaLogger.Log($"<color={(gripped ? "green" : "yellow")}>[GripJudge] {currentPair} 파지 " +
                                $"{(gripped ? "성립" : "해제")} — A(왼{leftOnA}/오{rightOnA}) B(왼{leftOnB}/오{rightOnB})</color>");
            }
        }

        Tint(aRenderer, leftOnA || rightOnA);
        Tint(bRenderer, leftOnB || rightOnB);
    }

    private bool HandOn(Transform target, Transform thumb, Transform index)
    {
        if (target == null) return false;

        bool thumbIn = thumb != null &&
                       (thumb.position - target.position).sqrMagnitude <= grabRadius * grabRadius;
        bool indexIn = index != null &&
                       (index.position - target.position).sqrMagnitude <= grabRadius * grabRadius;

        return requireBothFingers ? (thumbIn && indexIn) : (thumbIn || indexIn);
    }

    /// <summary>
    /// 쓰는 쌍만 켠다.
    ///   · 지금 판정하지 않는 접촉점 → <b>오브젝트째 끈다</b>(SetActive false).
    ///   · 판정하는 접촉점 → 오브젝트는 켜 두고 <b>렌더러만 끈다</b>.
    ///     위치를 읽어야 판정이 되지만 눈에는 안 보이게 한다(시선 분산 방지).
    ///     showSpheres를 켜면 위치를 맞출 때 볼 수 있다.
    /// </summary>
    private void Show(Transform t, bool inUse)
    {
        if (t == null) return;

        if (t.gameObject.activeSelf != inUse) t.gameObject.SetActive(inUse);
        if (!inUse) return;

        foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
            r.enabled = showSpheres;
    }

    private void Tint(Renderer r, bool on)
    {
        if (r == null || !showSpheres) return;
        block.Clear();
        block.SetColor("_Color", on ? grippedColor : idleColor);
        r.SetPropertyBlock(block);
    }

    /// <summary>Meta 손 리그의 엄지·검지 끝을 이름으로 찾는다.</summary>
    private void AutoFindFingerTips()
    {
        if (leftThumbTip != null && leftIndexTip != null &&
            rightThumbTip != null && rightIndexTip != null) return;

        foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            string n = t.name;
            if (n.Length < 6 || n[0] != 'b') continue;   // Meta 손뼈는 b_l_ / b_r_ 로 시작한다

            bool isLeft = n.StartsWith("b_l_", System.StringComparison.Ordinal);
            bool isRight = n.StartsWith("b_r_", System.StringComparison.Ordinal);
            if (!isLeft && !isRight) continue;

            // ★끝마디(_null)를 우선한다. thumb3는 마지막 관절이라 손끝보다 안쪽이다.
            //   순회 순서를 믿을 수 없으므로 _null이 나오면 덮어쓴다.
            bool isTipNull = n.EndsWith("_null", System.StringComparison.Ordinal);

            if (n.Contains("thumb"))
            {
                if (!isTipNull && !n.EndsWith("thumb3", System.StringComparison.Ordinal)) continue;
                if (isLeft && (leftThumbTip == null || isTipNull)) leftThumbTip = t;
                if (isRight && (rightThumbTip == null || isTipNull)) rightThumbTip = t;
            }
            else if (n.Contains("index"))
            {
                if (!isTipNull && !n.EndsWith("index3", System.StringComparison.Ordinal)) continue;
                if (isLeft && (leftIndexTip == null || isTipNull)) leftIndexTip = t;
                if (isRight && (rightIndexTip == null || isTipNull)) rightIndexTip = t;
            }
        }

        if (leftThumbTip == null || leftIndexTip == null || rightThumbTip == null || rightIndexTip == null)
        {
            if (retriesLeft > 0)
            {
                retriesLeft--;
                Invoke(nameof(AutoFindFingerTips), 1f);   // 손 리그가 늦게 생기는 경우가 있다
                return;
            }
            ChunaLogger.LogWarning("[GripJudge] 엄지·검지 끝을 다 찾지 못했습니다 — " +
                $"왼엄지{(leftThumbTip != null)} 왼검지{(leftIndexTip != null)} " +
                $"오엄지{(rightThumbTip != null)} 오검지{(rightIndexTip != null)}. " +
                "인스펙터에서 직접 지정하세요.");
            return;
        }

        if (showDebugLogs)
        {
            ChunaLogger.Log($"<color=cyan>[GripJudge] 손끝 찾음 — {leftThumbTip.name} · {leftIndexTip.name} · " +
                            $"{rightThumbTip.name} · {rightIndexTip.name}</color>");
        }
    }

#if UNITY_EDITOR
    [ContextMenu("접촉점 4개 만들기 (이마·뒤통수·좌측두·우측두)")]
    private void CreateTargets()
    {
        Transform head = FindDeep(transform.root, "CC_Base_Head");
        if (head == null)
        {
            ChunaLogger.LogWarning("[GripJudge] CC_Base_Head를 찾지 못했습니다.");
            return;
        }

        // 머리 로컬 기준 대략 위치. 눈으로 보고 인스펙터에서 맞추면 된다.
        forehead = forehead != null ? forehead : Make(head, "접촉점_이마", new Vector3(0f, 0.06f, 0.09f));
        occiput = occiput != null ? occiput : Make(head, "접촉점_뒤통수", new Vector3(0f, 0.04f, -0.10f));
        temporalLeft = temporalLeft != null ? temporalLeft : Make(head, "접촉점_좌측두", new Vector3(-0.08f, 0.04f, 0.01f));
        temporalRight = temporalRight != null ? temporalRight : Make(head, "접촉점_우측두", new Vector3(0.08f, 0.04f, 0.01f));

        ChunaLogger.Log("[GripJudge] 접촉점 4개 생성 — 위치는 인스펙터에서 맞추세요.");
    }

    private Transform Make(Transform parent, string name, Vector3 localPos)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = Vector3.one * (grabRadius * 2f);
        Object.DestroyImmediate(go.GetComponent<Collider>());   // 판정은 거리로 한다
        return go.transform;
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
