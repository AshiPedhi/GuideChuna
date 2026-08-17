using UnityEngine;
using Oculus.Interaction.Input;   // HandJointId

/// <summary>
/// 시술자 <b>손 트래킹 모델 위에</b> 두상골(pisiform) 접촉 자리를 표시한다 (표시 전용 — 판정하지 않는다).
///
/// ★왜 필요한가(2026-08-18 사용자 요구): 복와위 하부흉추는 손바닥이나 손끝이 아니라
/// <b>손날 아래쪽 두상골</b>을 횡돌기에 걸어야 하는데, 어디가 두상골인지 말로만 설명하면 감이 안 온다.
/// 실제로 닿아야 하는 자리를 <b>내 손 위에</b> 찍어 주면 한 번에 이해된다.
///
/// ■ 자리 잡는 법
/// 두상골에 대응하는 손 관절이 <b>없다</b>(손끝만 관절로 나온다). 그래서 두 관절 사이를 보간한다:
///   <c>손목(HandWristRoot) → 새끼 MCP(HandPinky1)</c> 선분의 <see cref="ratio"/> 지점 + 손바닥 쪽 오프셋.
/// 손 크기·SDK 버전에 따라 조금씩 다르므로 <see cref="ratio"/>·<see cref="localOffset"/>으로 맞춘다.
///
/// ■ 언제 보이나
/// <see cref="showOnConditionTypes"/>에 든 conditionType substep에서만. 기본 = 파지(<c>cranialGrip</c>).
/// 시나리오 구분은 따로 하지 않는다 — 이 컴포넌트가 리그 하위에 있으면 리그가 꺼질 때 같이 꺼진다.
///
/// ■ 마커는 <b>런타임에 손목 관절의 자식으로 붙인다</b>
/// 손 관절은 런타임 생성이라 에디터에서 부모로 지정할 수 없다. 그래서 손이 처음 잡히는 순간
/// <c>SetParent(손목)</c> 해서 <b>손 모델의 일부로 만든다</b> — 그 뒤로는 손을 그대로 따라간다.
/// (이 프로젝트가 파지용 손끝 콜라이더를 손가락 뼈 밑에 만드는 것과 같은 방식이다.)
///
/// ★손목 로컬 좌표로 고정하는 것이 매 프레임 손목↔새끼를 다시 보간하는 것보다 <b>정확하다</b> —
/// 두상골은 손바닥에 고정된 해부학적 지점이라 손가락을 굽혀도 움직이지 않아야 하는데,
/// 매 프레임 보간하면 새끼손가락을 굽힐 때마다 표시가 딸려 움직인다.
/// </summary>
[DisallowMultipleComponent]
public class PisiformHighlight : MonoBehaviour
{
    [Header("=== 표시할 손 ===")]
    [Tooltip("양손 다 두상골로 접촉하므로 기본은 양손이다.")]
    [SerializeField] private bool showLeft = true;
    [SerializeField] private bool showRight = true;

    [Tooltip("왼손 마커(손날 아래 두상골 자리). 도구가 만들어 넣는다.")]
    [SerializeField] private Transform leftMarker;
    [SerializeField] private Transform rightMarker;

    [Header("=== 자리 미세 조정 ===")]
    [Tooltip("손목 → 새끼 MCP 선분에서의 위치. 0=손목, 1=새끼 MCP.\n" +
             "두상골은 손목 바로 앞 새끼 쪽이라 0.25~0.35 정도가 맞는다.")]
    [SerializeField, Range(0f, 1f)] private float ratio = 0.3f;

    [Tooltip("손목 로컬 기준 추가 오프셋(m). 손바닥 면 쪽으로 살짝 내려 붙일 때 쓴다.\n" +
             "★Play 중에 돌리면 바로 반영된다 — 손을 보면서 맞추세요.")]
    [SerializeField] private Vector3 localOffset = Vector3.zero;

    [Tooltip("마커를 손목 회전에 맞춘 뒤 추가로 돌릴 각도.")]
    [SerializeField] private Vector3 localEuler = Vector3.zero;

    [Header("=== 언제 보일지 ===")]
    [Tooltip("이 conditionType의 substep에서만 표시한다. 비우면 리그가 켜져 있는 동안 항상.")]
    [SerializeField] private string[] showOnConditionTypes = { "cranialGrip" };

    [Header("=== 맥동 ===")]
    [Tooltip("발광 세기의 최저/최고. 두 값을 같게 두면 맥동이 멈춘다.")]
    [SerializeField, Range(0f, 4f)] private float minIntensity = 0.5f;
    [SerializeField, Range(0f, 4f)] private float maxIntensity = 1.8f;
    [SerializeField, Range(0f, 3f)] private float pulsePerSecond = 1.1f;

    [Tooltip("강조 색. 화살표·타겟과 같은 계열의 녹색. 아래 역할을 지정하면 그쪽이 이긴다.")]
    [SerializeField] private Color color = new Color(0.149f, 1f, 0.318f, 1f);

    [Tooltip("★손별 역할. 두상골은 양손에 다 나오는데 한쪽은 주동수, 한쪽은 보조수인 경우가 많다.\n" +
             "색 값은 HandRole.cs의 전역 규약에서 온다 — 여기서는 역할만 고른다.\n" +
             "'기존색 유지'(기본)면 위의 강조 색을 양손에 똑같이 쓴다(무회귀).")]
    [SerializeField] private HandRole.Role leftRole = HandRole.Role.기존색유지;
    [SerializeField] private HandRole.Role rightRole = HandRole.Role.기존색유지;

    /// <summary>그 손이 쓸 색 — 역할을 지정했으면 규약 색, 아니면 공용 강조 색.</summary>
    private Color ColorFor(HandRole.Role role) =>
        HandRole.UsesRoleColor(role) ? HandRole.ColorOf(role) : color;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool debugLog = false;

    private CranialAdjustmentController rig;
    private bool visible;
    private float phase;
    private Renderer leftRenderer, rightRenderer;

    private void Awake()
    {
        rig = GetComponentInParent<CranialAdjustmentController>(true);
        if (rig == null) rig = FindFirstObjectByType<CranialAdjustmentController>(FindObjectsInactive.Include);

        if (leftMarker != null) leftRenderer = leftMarker.GetComponentInChildren<Renderer>(true);
        if (rightMarker != null) rightRenderer = rightMarker.GetComponentInChildren<Renderer>(true);
        SetVisible(false);
    }

    private void OnEnable()
    {
        var ev = ScenarioEventSystem.Instance;
        if (ev != null) ev.OnSubStepStarted += HandleSubStepStarted;
    }

    private void OnDisable()
    {
        var ev = ScenarioEventSystem.Instance;
        if (ev != null) ev.OnSubStepStarted -= HandleSubStepStarted;
        SetVisible(false);
    }

    private void HandleSubStepStarted(SubStepData subStep)
    {
        bool on = Matches(subStep);
        SetVisible(on);
        if (debugLog && on)
            ChunaLogger.Log($"[PisiformHighlight] 두상골 표시 ON — '{subStep?.conditionType}'");
    }

    /// <summary>목록이 비어 있으면 '항상', 아니면 conditionType 일치일 때만.</summary>
    private bool Matches(SubStepData subStep)
    {
        if (showOnConditionTypes == null || showOnConditionTypes.Length == 0) return true;
        string t = subStep != null ? (subStep.conditionType ?? "").Trim() : "";
        if (t.Length == 0) return false;
        foreach (string s in showOnConditionTypes)
            if (!string.IsNullOrWhiteSpace(s) &&
                s.Trim().Equals(t, System.StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private void SetVisible(bool on)
    {
        visible = on;
        if (leftMarker != null) leftMarker.gameObject.SetActive(on && showLeft);
        if (rightMarker != null) rightMarker.gameObject.SetActive(on && showRight);
    }

    /// <summary>손을 따라가는 갱신은 LateUpdate에서 — 손 트래킹이 Update에서 자세를 갱신한 뒤여야 한다.</summary>
    private void LateUpdate()
    {
        if (!visible || rig == null) return;

        if (showLeft) Place(CranialAdjustmentController.JudgeHand.왼손, leftMarker);
        if (showRight) Place(CranialAdjustmentController.JudgeHand.오른손, rightMarker);

        Pulse();
    }

    /// <summary>
    /// 마커를 손목 관절 자식으로 붙이고(최초 1회), 손목 로컬 좌표로 두상골 자리를 잡는다.
    /// 인스펙터에서 비율·오프셋을 돌리면 바로 반영되도록 로컬 좌표는 매 프레임 다시 쓴다(비용 없음).
    /// </summary>
    private void Place(CranialAdjustmentController.JudgeHand hand, Transform marker)
    {
        if (marker == null) return;

        Transform wrist = rig.GetHandJoint(hand, HandJointId.HandWristRoot);
        Transform pinky = rig.GetHandJoint(hand, HandJointId.HandPinky1);

        // 손이 아직 안 잡히면 숨긴다 — 원점(0,0,0)에 마커가 덩그러니 떠 있는 것을 막는다.
        if (wrist == null || pinky == null)
        {
            if (marker.gameObject.activeSelf) marker.gameObject.SetActive(false);
            return;
        }
        if (!marker.gameObject.activeSelf) marker.gameObject.SetActive(true);

        // ★손 모델의 자식으로 편입 — 한 번만. 이후로는 손을 그대로 따라간다.
        if (marker.parent != wrist)
        {
            marker.SetParent(wrist, worldPositionStays: false);
            if (debugLog)
                ChunaLogger.Log($"[PisiformHighlight] {marker.name} → 손목 '{wrist.name}' 자식으로 부착");
        }

        // 손목 로컬 기준 두상골 자리: 손목→새끼 MCP 방향으로 ratio만큼 간 지점 + 오프셋.
        Vector3 localPinky = wrist.InverseTransformPoint(pinky.position);
        marker.localPosition = localPinky * ratio + localOffset;
        marker.localRotation = Quaternion.Euler(localEuler);
    }

    /// <summary>맥동 — 켜져 있는 동안 밝기가 오간다(TargetAreaHighlight와 같은 방식).</summary>
    private void Pulse()
    {
        float intensity = maxIntensity;
        if (pulsePerSecond > 0.001f)
        {
            phase += Time.deltaTime * pulsePerSecond;
            if (phase > 1f) phase -= 1f;
            float wave = (Mathf.Sin(phase * 2f * Mathf.PI) + 1f) * 0.5f;
            intensity = Mathf.Lerp(minIntensity, maxIntensity, wave);
        }

        Apply(leftRenderer, ColorFor(leftRole), intensity);
        Apply(rightRenderer, ColorFor(rightRole), intensity);
    }

    private static void Apply(Renderer r, Color c, float intensity)
    {
        if (r == null) return;
        Material m = r.material;          // 인스턴스(공유 머티리얼 보호)
        if (m == null) return;
        if (m.HasProperty("_Color")) m.color = c;
        if (m.HasProperty("_EmissionColor"))
            m.SetColor("_EmissionColor", new Color(c.r * intensity, c.g * intensity, c.b * intensity, 1f));
    }

    /// <summary>에디터 도구가 만든 마커를 연결한다.</summary>
    public void SetMarkers(Transform left, Transform right)
    {
        leftMarker = left;
        rightMarker = right;
        leftRenderer = left != null ? left.GetComponentInChildren<Renderer>(true) : null;
        rightRenderer = right != null ? right.GetComponentInChildren<Renderer>(true) : null;
    }

    /// <summary>점검용 요약.</summary>
    public string Describe() =>
        $"두상골 표시 — 왼손 {(leftMarker != null ? "O" : "★없음")} / 오른손 {(rightMarker != null ? "O" : "★없음")} · " +
        $"손목→새끼 {ratio:0.00} 지점 · 표시 단계 {(showOnConditionTypes == null || showOnConditionTypes.Length == 0 ? "항상" : string.Join(",", showOnConditionTypes))}";
}
