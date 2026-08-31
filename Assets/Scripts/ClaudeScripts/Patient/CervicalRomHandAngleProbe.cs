using UnityEngine;
using TMPro;

/// <summary>
/// 실습(교육)모드에서 <b>손으로 잰 각</b>을 환자 머리 위에 띄운다. 대본 각과 나란히 보여 준다.
///
/// ★측정 로직은 실측모드와 같다 — 양손 파지 벡터를 그 동작의 회전면에 투영해 회전각을 읽는다.
///   다른 점은 기준틀을 <b>가상 환자의 몸통에서 그냥 가져온다</b>는 것뿐이다.
///   실제 환자는 어깨를 짚어 축을 세워야 하지만, 여기선 모델이 있으니 그럴 필요가 없다.
///
/// 용도는 A-12(실측 각도 정확도 검증)다. 대본 각(정답)과 손 측정값을 같은 화면에서 비교한다.
/// 판정에는 아무 영향이 없다 — 읽기만 한다.
/// </summary>
public class CervicalRomHandAngleProbe : MonoBehaviour
{
    [Header("=== 참조 (비우면 자동 탐색) ===")]
    [SerializeField] private CervicalRomDriver driver;
    [SerializeField] private CervicalGripJudge gripJudge;
    [Tooltip("이 위에 띄운다. 비우면 CC_Base_Head를 찾고, 없으면 드라이버의 회전 중심을 쓴다.")]
    [SerializeField] private Transform head;

    [Header("=== 표시 ===")]
    [SerializeField] private bool show = true;
    [Tooltip("머리 위로 이만큼(m).")]
    [SerializeField] private float heightAboveHead = 0.28f;
    [Tooltip("실시간 손 측정각 글씨 크기(m). ★이 컴포넌트는 씬에 없고 런타임에 붙으므로 이 기본값이 그대로 먹는다.")]
    [SerializeField] private float fontSize = 0.085f;
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private Color measuredColor = new Color(1f, 0.85f, 0.20f);
    [SerializeField] private Color scriptedColor = new Color(0.65f, 0.80f, 1f);
    [SerializeField] private Color deltaColor = new Color(0.55f, 0.90f, 0.65f);

    [Header("=== 측정 ===")]
    [Tooltip("파지 벡터 저역통과 시간상수(초). 0이면 생값.")]
    [SerializeField] private float smoothing = 0.08f;
    [Tooltip("면 성분이 이 비율보다 작으면 그 파지로는 못 재는 것이다.")]
    [SerializeField] private float minPerpRatio = 0.34f;
    [Tooltip("0점을 다시 잡는 기준 — 대본 각이 이보다 작으면 지금을 중립으로 본다(도).")]
    [SerializeField] private float neutralResetAngle = 1.5f;

    [SerializeField] private bool showDebugLogs = false;

    private Transform root;
    private TextMeshPro label;

    private CervicalRomDriver.Direction zeroedFor = CervicalRomDriver.Direction.None;
    private Vector3 v0;
    private Vector3 vNow;
    private bool vNowValid;

    private int shownMeasured = int.MinValue;
    private int shownScripted = int.MinValue;
    private readonly System.Text.StringBuilder sb = new System.Text.StringBuilder(96);

    /// <summary>지금 읽히는 손 측정각(도, 크기). 못 재면 false.</summary>
    public bool TryGetHandAngle(out float degrees, out float perpRatio)
    {
        degrees = 0f; perpRatio = 0f;
        if (driver == null || !vNowValid) return false;
        if (zeroedFor == CervicalRomDriver.Direction.None) return false;

        Vector3 axis = driver.CurrentWorldAxis;
        if (axis.sqrMagnitude < 1e-8f) return false;

        Vector3 a = Vector3.ProjectOnPlane(v0, axis);
        Vector3 b = Vector3.ProjectOnPlane(vNow, axis);
        if (a.sqrMagnitude < 1e-8f || b.sqrMagnitude < 1e-8f) return false;

        perpRatio = a.magnitude / Mathf.Max(1e-6f, v0.magnitude);
        degrees = Mathf.Abs(Vector3.SignedAngle(a, b, axis));
        return true;
    }

    private void Awake()
    {
        if (driver == null) driver = FindFirstObjectByType<CervicalRomDriver>();
        if (gripJudge == null) gripJudge = FindFirstObjectByType<CervicalGripJudge>();
        if (font == null) font = KoreanFontResolver.Resolve();

        // ★[임시 · A-12] 지난 판 기록을 지우고, 결과 화면에 부록을 대는 제공자를 꽂는다.
        //   static이라 씬을 다시 열어도 값이 남는다 — 여기서 지워야 이번 판만 나온다.
        CervicalRomHandAngleLog.Clear();
        TrainingResultData.RomAppendixProvider = CervicalRomHandAngleLog.BuildAppendix;
    }

    // ★[임시 · A-12] 드라이버가 측정값을 남기는 순간을 듣는다. 그 순간의 손 각을 같이 찍어 둔다.
    private void OnEnable()
    {
        if (driver != null) driver.OnMeasurementRecorded += HandleMeasurementRecorded;
    }

    private void OnDisable()
    {
        if (driver != null) driver.OnMeasurementRecorded -= HandleMeasurementRecorded;
        Teardown();
    }

    private void OnDestroy() => Teardown();

    /// <summary>
    /// ★[임시 · A-12] 대본이 능동·수동 끝점을 기록한 <b>그 프레임의</b> 손 측정각을 남긴다.
    /// 나중에 다시 읽으면 이미 각이 변해 있어 의미가 없다.
    /// </summary>
    private void HandleMeasurementRecorded(CervicalRomDriver.Direction dir, bool isActive)
    {
        bool ok = TryGetHandAngle(out float measured, out float perp);
        CervicalRomHandAngleLog.Record(dir, isActive,
                                       measurable: ok && perp >= minPerpRatio,
                                       handDegrees: measured,
                                       scriptedDegrees: driver != null ? driver.CurrentAngle : 0f);

        if (showDebugLogs)
            ChunaLogger.Log($"<color=cyan>[손각도] {dir} {(isActive ? "능동" : "수동")} 기록 — " +
                            $"손 {(ok ? measured.ToString("F1") : "--")}° / 대본 {(driver != null ? driver.CurrentAngle : 0f):F1}°</color>");
    }

    private void LateUpdate()
    {
        // ★Awake 때 드라이버가 아직 없었으면 여기서 잡고 그때 구독한다.
        //   런타임에 붙는 컴포넌트라 순서를 장담할 수 없다.
        if (driver == null)
        {
            driver = FindFirstObjectByType<CervicalRomDriver>();
            if (driver == null) { Teardown(); return; }
            driver.OnMeasurementRecorded += HandleMeasurementRecorded;
        }

        CervicalRomDriver.Direction dir = driver.CurrentDirection;
        if (dir == CervicalRomDriver.Direction.None) { Teardown(); return; }

        // --- 손 벡터 ---
        bool has = TryGetPair(out Vector3 raw);
        if (has)
        {
            if (!vNowValid || smoothing <= 0f) vNow = raw;
            else
            {
                float k = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(1e-4f, smoothing));
                vNow = Vector3.Lerp(vNow, raw, k);
            }
            vNowValid = true;
        }
        else vNowValid = false;

        // --- 0점 ---
        // ★방향이 바뀌었거나, 대본 각이 아직 중립이면 지금을 0으로 잡는다.
        //   실측모드처럼 사람이 정렬을 잡아 주지 않으므로 여기서 알아서 잡는다.
        if (vNowValid && (zeroedFor != dir || driver.CurrentAngle <= neutralResetAngle))
        {
            v0 = vNow;
            if (zeroedFor != dir && showDebugLogs)
                ChunaLogger.Log($"<color=cyan>[손각도] {dir} 0점 재설정</color>");
            zeroedFor = dir;
        }

        // ★표시를 꺼도 0점·손 벡터 추적은 계속 돌아야 한다 —
        //   결과 부록에 남길 값이 이 추적에서 나온다. 끄는 건 화면뿐이다.
        if (!show) { Teardown(); return; }

        Draw(dir);
    }

    private void Draw(CervicalRomDriver.Direction dir)
    {
        if (root == null) Build();
        if (label == null) return;

        bool ok = TryGetHandAngle(out float measured, out float perp);
        float scripted = driver.CurrentAngle;

        int mi = ok ? Mathf.RoundToInt(measured) : int.MinValue + 1;
        int si = Mathf.RoundToInt(scripted);
        if (mi != shownMeasured || si != shownScripted)
        {
            shownMeasured = mi; shownScripted = si;

            sb.Clear();
            sb.Append("<size=70%>").Append(LabelOf(dir)).Append("</size>\n");
            if (!ok)
            {
                sb.Append("<color=#909090>손 --</color>");
            }
            else if (perp < minPerpRatio)
            {
                sb.Append($"<color=#ffcc55>이 파지로는 못 잼 ({perp:F2})</color>");
            }
            else
            {
                sb.Append($"<color=#{ToHex(measuredColor)}>손 {mi}°</color>");
                sb.Append($"   <color=#{ToHex(scriptedColor)}>대본 {si}°</color>");
                sb.Append($"\n<size=75%><color=#{ToHex(deltaColor)}>차 {Mathf.Abs(mi - si)}°</color></size>");
            }
            label.text = sb.ToString();
        }

        Transform anchor = ResolveHead();
        if (anchor == null) return;
        root.position = anchor.position + Vector3.up * heightAboveHead;

        Camera cam = Camera.main;
        if (cam != null)
            root.rotation = Quaternion.LookRotation(root.position - cam.transform.position, Vector3.up);
    }

    private bool TryGetPair(out Vector3 v)
    {
        v = Vector3.zero;
        if (gripJudge == null) return false;
        // ★엄지·검지 파지 지점만 쓴다. 손목·손바닥은 안 본다(2026-08-28 사용자 지시).
        if (!gripJudge.TryGetPinchPoint(GripFingerTip.Side.Left, out Vector3 l)) return false;
        if (!gripJudge.TryGetPinchPoint(GripFingerTip.Side.Right, out Vector3 r)) return false;
        v = r - l;
        return v.sqrMagnitude > 1e-6f;
    }

    private Transform ResolveHead()
    {
        if (head != null) return head;
        if (driver == null) return null;

        Transform torso = driver.Torso;
        if (torso != null)
        {
            Transform found = FindDeep(torso.root, "CC_Base_Head");
            if (found != null) { head = found; return head; }
        }
        head = driver.Pivot;
        return head;
    }

    private static Transform FindDeep(Transform t, string name)
    {
        if (t == null) return null;
        if (t.name == name) return t;
        for (int i = 0; i < t.childCount; i++)
        {
            Transform f = FindDeep(t.GetChild(i), name);
            if (f != null) return f;
        }
        return null;
    }

    private void Build()
    {
        var go = new GameObject("손각도프로브") { hideFlags = HideFlags.DontSave };
        root = go.transform;

        var t = new GameObject("표시") { hideFlags = HideFlags.DontSave };
        t.transform.SetParent(root, false);
        label = t.AddComponent<TextMeshPro>();
        if (font != null) label.font = font;
        label.fontSize = fontSize * 100f;
        label.transform.localScale = Vector3.one * 0.01f;
        label.alignment = TextAlignmentOptions.Center;
        label.fontStyle = FontStyles.Bold;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
    }

    private void Teardown()
    {
        if (root != null)
        {
            if (Application.isPlaying) Destroy(root.gameObject); else DestroyImmediate(root.gameObject);
        }
        root = null; label = null;
        shownMeasured = int.MinValue; shownScripted = int.MinValue;
    }

    private static string ToHex(Color c)
        => ColorUtility.ToHtmlStringRGB(c);

    private static string LabelOf(CervicalRomDriver.Direction d)
    {
        switch (d)
        {
            case CervicalRomDriver.Direction.Flexion:       return "굴곡";
            case CervicalRomDriver.Direction.Extension:     return "신전";
            case CervicalRomDriver.Direction.LateralRight:  return "우측굴";
            case CervicalRomDriver.Direction.LateralLeft:   return "좌측굴";
            case CervicalRomDriver.Direction.RotationRight: return "우회전";
            case CervicalRomDriver.Direction.RotationLeft:  return "좌회전";
            default:                                        return "-";
        }
    }
}
