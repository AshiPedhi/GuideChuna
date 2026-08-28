using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// 실측(현실 환자) 모드의 경추 ROM 각도 측정.
///
/// 가상 모드의 <see cref="CervicalRomDriver"/>는 각도를 <b>명령</b>한다(대본대로 모델을 몬다).
/// 이쪽은 반대로 각도를 <b>측정</b>한다. 모델도 접촉점 콜라이더도 쓰지 않고 손 두 개만 본다.
///
/// 원리 — 양손 파지 벡터 V의 회전각을 읽는다.
///   ★V는 그 동작의 회전축과 <b>수직</b>일 때만 각이 나온다. 축과 나란하면 감도 0이다.
///     이마·후두 파지(전후축) → 굴곡·신전 · 회전 ○ / 측굴 ×
///     측두부  파지(좌우축) → 측굴 · 회전 ○      / 굴곡·신전 ×
///     술기가 원래 그렇게 잡으므로 네 동작이 전부 커버된다.
///   ★각은 <b>면에 투영해서</b> 잰다. 투영각은 파지가 축과 정확히 수직이 아니어도
///     값이 줄지 않는다(투영이 축 성분을 지운다). 줄어드는 건 감도(잡음 여유)뿐이라
///     편향이 없다 — 폐기한 midT-C 방식과 갈리는 지점이다.
///
/// 전제 — 표준자세는 정렬(t0) 시점에 고정된 것으로 본다. <b>몸통 보상 분리 로직은 없다.</b>
///        t0 이후 몸통이 움직이면 조용히 값에 섞인다. 시작 전에 고지하는 것으로 갈음한다.
///
/// 부호 — 기록값은 <b>크기</b>다. 어느 방향인지는 단계가 이미 알고 있어 부호가 필요 없고,
///        부호를 쓰면 좌우 손이 바뀌었을 때 뒤집히는 함정이 생긴다. 로그에는 부호를 남긴다.
/// </summary>
public class CervicalRomRealityMeasure : MonoBehaviour
{
    public enum Stage
    {
        Idle,             // 대기
        AwaitShoulders,   // 양손을 환자 양 어깨에 - 기준축을 세운다
        AwaitNeutral,     // 중립에서 머리를 파지 - 0점을 잡는다
        Active,           // 환자 능동
        Passive,          // 시술자 압박(끝 느낌)
        Done,             // 이 방향 측정 끝
    }

    [Header("=== 손 소스 ===")]
    [Tooltip("비워 두면 씬에서 찾는다. 접촉점(콜라이더)은 안 쓰고 손끝 위치만 가져온다.")]
    [SerializeField] private CervicalGripJudge gripJudge;

    [Tooltip("판정기를 안 쓰고 직접 넣고 싶을 때. 둘 다 채워야 이쪽을 쓴다.")]
    [SerializeField] private Transform leftHandOverride;
    [SerializeField] private Transform rightHandOverride;

    [Header("=== 측정 대상 ===")]
    [SerializeField] private CervicalRomDriver.Direction direction = CervicalRomDriver.Direction.Flexion;

    [Header("=== 캡처 ===")]
    [Tooltip("양손이 이 속도 아래로 이만큼 머무르면 '정지'로 본다(초).")]
    [SerializeField] private float holdSeconds = 1.5f;

    [Tooltip("정지로 인정할 손 속도(m/s).")]
    [SerializeField] private float holdSpeedThreshold = 0.03f;

    [Tooltip("정지만으로 다음 단계로 넘어간다. 끄면 키/메뉴로만 넘어간다.")]
    [SerializeField] private bool advanceOnHold = true;

    [Tooltip("능동·압박 끝점을 정지로 확정할 때, 최소 이만큼은 움직였어야 한다(도).\n" +
             "이게 없으면 중립에서 가만히 있다가 0도로 확정돼 버린다.")]
    [SerializeField] private float minAngleToMark = 5f;

    [Header("=== 건전성 검사 ===")]
    [Tooltip("파지 벡터의 면 성분이 이 비율보다 작으면 '이 파지로는 못 잰다'로 본다.\n" +
             "0.34 = 축과 20도 이내. 감도가 0에 가까워 잡음만 읽힌다.")]
    [SerializeField] private float minPerpRatio = 0.34f;

    [Tooltip("머리는 강체라 양손 거리가 보존돼야 한다. 이 비율을 넘게 변하면 파지가 미끄러진 것.")]
    [SerializeField] private float slipTolerance = 0.15f;

    [Tooltip("파지 벡터 저역통과 시간상수(초). 0이면 생값.")]
    [SerializeField] private float smoothing = 0.08f;

    [Header("=== 표시 ===")]
    [SerializeField] private bool showReadout = true;
    [SerializeField] private bool showAxes = true;

    [Tooltip("단계 진행·정지 게이지·방향 목록을 같이 보여준다.\n" +
             "★정지로 넘어가는 구조라 게이지가 없으면 홀드가 먹고 있는지 알 수가 없다.")]
    [SerializeField] private bool showProgress = true;

    [Header("=== 각도기 (180도 반원) ===")]
    [Tooltip("★실습모드의 CervicalRomPlaneGauge와 무관한 자체 구현이다.\n" +
             "그쪽은 13개 술기가 공유하는 파일이라 건드리지 않는다.")]
    [SerializeField] private bool showGauge = true;

    [Tooltip("0도를 가운데 두고 좌우로 이만큼씩. 90이면 반원 180도가 된다.")]
    [SerializeField] private float gaugeHalfSpan = 90f;

    [SerializeField] private float gaugeRadius = 0.30f;
    [SerializeField] private float majorStep = 10f;
    [SerializeField] private float minorStep = 5f;
    [SerializeField] private float microStep = 1f;
    [SerializeField] private float majorTickLength = 0.055f;
    [SerializeField] private float minorTickLength = 0.035f;
    [SerializeField] private float microTickLength = 0.018f;
    [SerializeField] private float tickWidth = 0.0035f;
    [SerializeField] private float gaugeLabelSize = 0.035f;
    [SerializeField] private float gaugeLabelOffset = 0.028f;

    [SerializeField] private Color tickColor = new Color(0.85f, 0.90f, 1f, 0.95f);
    [SerializeField] private Color microColor = new Color(0.60f, 0.68f, 0.80f, 0.55f);
    [SerializeField] private Color zeroLineColor = new Color(1f, 1f, 1f, 0.85f);
    [SerializeField] private Color needleColor = new Color(1f, 0.85f, 0.20f);
    [SerializeField] private Color activeMarkColor = new Color(0.35f, 0.85f, 1f);
    [SerializeField] private Color passiveMarkColor = new Color(1f, 0.45f, 0.35f);
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private float readoutSize = 0.05f;
    [Tooltip("어깨 중점에서 이만큼 올린 곳을 기준점으로 쓴다(표시 전용, 각도에는 영향 없음).")]
    [SerializeField] private float pivotRise = 0.10f;
    [SerializeField] private float axisLength = 0.18f;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = true;
    [Tooltip("에디터 Play에서 키로 진행한다. VR에서는 정지로 넘어간다.")]
    [SerializeField] private bool useKeyboard = true;

    // --- 상태 ---
    private Stage stage = Stage.Idle;
    private bool frameReady;
    private bool neutralReady;

    private Vector3 axRight, axUp, axFwd;   // t0에 세운 십자축 (환자 기준틀)
    private Vector3 pivot;                  // 표시 기준점
    private float shoulderWidth;

    private Vector3 v0;                     // 중립 파지 벡터
    private float len0;
    private Vector3 vNow;                   // 저역통과를 거친 현재 파지 벡터
    private bool vNowValid;

    private Vector3 prevLeft, prevRight;
    private bool prevValid;
    private float holdTimer;
    private float peakAngle;                // 이 단계에서 본 최대 각 - minAngleToMark 판정용

    // ★active·passive는 <b>부호 있는</b> 각이다. 각도기 지침이 어느 쪽으로 가는지에 쓴다.
    //   기록·표시에 나가는 값은 Mathf.Abs를 거친 크기다.
    private struct Result { public float active, passive; public bool hasActive, hasPassive; }
    private readonly Result[] results = new Result[7];

    // --- 표시 오브젝트 ---
    private Transform root;
    private TextMeshPro readout;
    private LineRenderer lineRight, lineUp, lineFwd, lineNeutral, lineNow;
    private LineRenderer needle, activeMark, passiveMark;
    private Material sharedMaterial;

    // --- 각도기 ---
    private Mesh gaugeMesh;
    private MeshFilter gaugeFilter;
    private readonly List<Vector3> gVerts = new List<Vector3>(2048);
    private readonly List<int> gTris = new List<int>(3072);
    private readonly List<Color> gCols = new List<Color>(2048);
    private readonly List<TextMeshPro> gaugeLabels = new List<TextMeshPro>(24);
    private CervicalRomDriver.Direction builtDirection = CervicalRomDriver.Direction.None;
    private int builtStamp = -1;    // 정렬·0점을 다시 잡으면 올라간다
    private int frameStamp;

    // ★Update에서 문자열을 새로 만들지 않는다(VR 프레임 예산).
    //   보여줄 값이 바뀐 프레임에만 다시 만든다.
    private readonly System.Text.StringBuilder sb = new System.Text.StringBuilder(160);
    private Stage shownStage = (Stage)(-1);
    private int shownAngle = int.MinValue;
    private int shownWarn = -1;
    private int shownHold = -99;    // 정지 게이지는 10칸으로 양자화해 그 칸이 바뀔 때만 다시 만든다
    private int shownMask = -1;     // 어느 방향이 끝났는지 비트마스크

    public Stage CurrentStage => stage;
    public bool FrameReady => frameReady;
    public bool NeutralReady => neutralReady;
    public CervicalRomDriver.Direction MeasuredDirection => direction;

    public bool HasActive(CervicalRomDriver.Direction d) => results[(int)d].hasActive;
    public bool HasPassive(CervicalRomDriver.Direction d) => results[(int)d].hasPassive;

    /// <summary>브리지가 단계에 맞춰 방향을 지정한다. 0점은 파지가 바뀌므로 다시 잡는다.</summary>
    public void SetDirection(CervicalRomDriver.Direction d, bool keepNeutral = false)
    {
        if (direction == d && (keepNeutral || !neutralReady)) return;
        direction = d;
        if (!keepNeutral)
        {
            neutralReady = false;
            stage = Stage.AwaitNeutral;
        }
        holdTimer = 0f; peakAngle = 0f;
        frameStamp++;
        Mark($"-> {Label(direction)}. {GripHintFor(direction)} 파지 후 중립에서 정지하세요.");
    }

    /// <summary>중립 근처로 돌아왔는가 — 복귀 substep을 넘길 조건이다.</summary>
    public bool IsBackToNeutral(float toleranceDeg)
        => TryGetAngle(out float deg, out _, out _) && deg <= toleranceDeg;

    /// <summary>지금 읽히는 각(도). 크기다. 못 재는 상태면 false.</summary>
    public bool TryGetAngle(out float degrees, out float perpRatio, out float signed)
    {
        degrees = 0f; perpRatio = 0f; signed = 0f;
        if (!neutralReady || !vNowValid) return false;

        Vector3 axis = AxisFor(direction);
        if (axis.sqrMagnitude < 1e-8f) return false;

        Vector3 a = Vector3.ProjectOnPlane(v0, axis);
        Vector3 b = Vector3.ProjectOnPlane(vNow, axis);
        if (a.sqrMagnitude < 1e-8f || b.sqrMagnitude < 1e-8f) return false;

        perpRatio = a.magnitude / Mathf.Max(1e-6f, v0.magnitude);
        signed = Vector3.SignedAngle(a, b, axis);
        degrees = Mathf.Abs(signed);
        return true;
    }

    /// <summary>파지가 미끄러졌는가 - 머리가 강체라 양손 거리는 보존돼야 한다.</summary>
    public bool IsSlipping(out float ratio)
    {
        ratio = 1f;
        if (!neutralReady || !vNowValid || len0 < 1e-4f) return false;
        ratio = vNow.magnitude / len0;
        return Mathf.Abs(ratio - 1f) > slipTolerance;
    }

    private Vector3 AxisFor(CervicalRomDriver.Direction d)
    {
        switch (d)
        {
            // 굴곡·신전 = 좌우축 둘레 / 측굴 = 전후축 둘레 / 회전 = 수직축 둘레
            case CervicalRomDriver.Direction.Flexion:
            case CervicalRomDriver.Direction.Extension:      return axRight;
            case CervicalRomDriver.Direction.LateralLeft:
            case CervicalRomDriver.Direction.LateralRight:   return axFwd;
            case CervicalRomDriver.Direction.RotationLeft:
            case CervicalRomDriver.Direction.RotationRight:  return axUp;
            default:                                         return Vector3.zero;
        }
    }

    private void Awake()
    {
        if (gripJudge == null) gripJudge = FindFirstObjectByType<CervicalGripJudge>();
        if (font == null) font = KoreanFontResolver.Resolve();
        ResetAll();
    }

    private void OnDestroy() => TearDownVisuals();

    /// <summary>★꺼지면 표시물을 걷는다. 교육모드에서 실측 리드아웃이 남아 있으면 안 된다.</summary>
    private void OnDisable() => TearDownVisuals();

    // ================= 진행 =================

    [ContextMenu("0 - 처음부터")]
    public void ResetAll()
    {
        stage = Stage.AwaitShoulders;
        frameReady = neutralReady = false;
        vNowValid = prevValid = false;
        holdTimer = 0f; peakAngle = 0f;
        for (int i = 0; i < results.Length; i++) results[i] = default;
        Mark("처음부터 - 양손을 환자 양 어깨에 대고 정지하세요.");
    }

    [ContextMenu("1 - 어깨 정렬 캡처")]
    public void CaptureShoulderFrame()
    {
        if (!TryGetHands(out Vector3 l, out Vector3 r))
        {
            Warn("손을 못 찾았습니다 - gripJudge가 손끝을 아직 못 붙였거나 씬에 없습니다.");
            return;
        }

        Vector3 line = r - l;
        axUp = Vector3.up;                                   // XR 월드는 중력 정렬이다
        Vector3 ml = Vector3.ProjectOnPlane(line, axUp);
        if (ml.sqrMagnitude < 1e-6f)
        {
            Warn("어깨선이 수직에 가깝습니다 - 양손을 좌우로 벌려 어깨에 대세요.");
            return;
        }

        axRight = ml.normalized;
        axFwd = Vector3.Cross(axRight, axUp).normalized;     // Unity: Cross(right, up) = forward
        shoulderWidth = line.magnitude;
        pivot = (l + r) * 0.5f + axUp * pivotRise;

        frameReady = true;
        neutralReady = false;
        stage = Stage.AwaitNeutral;
        holdTimer = 0f;
        frameStamp++;
        Mark($"기준축 고정 - 어깨폭 {shoulderWidth * 100f:F0}cm. 이제 중립에서 머리를 파지하고 정지하세요.");
    }

    [ContextMenu("2 - 중립(0점) 캡처")]
    public void CaptureNeutral()
    {
        if (!frameReady) { Warn("어깨 정렬이 먼저입니다."); return; }
        if (!TryGetHands(out Vector3 l, out Vector3 r)) { Warn("손을 못 찾았습니다."); return; }

        v0 = r - l;
        len0 = v0.magnitude;
        if (len0 < 0.03f) { Warn($"양손이 너무 붙어 있습니다({len0 * 100f:F0}cm)."); return; }

        vNow = v0; vNowValid = true;
        neutralReady = true;
        stage = Stage.Active;
        holdTimer = 0f; peakAngle = 0f;
        frameStamp++;

        TryGetAngle(out _, out float perp, out _);
        string verdict = perp < minPerpRatio
            ? $"★이 파지로는 {Label(direction)}을(를) 못 잽니다(면 성분 {perp:F2}). 파지를 바꾸세요."
            : $"측정 가능(면 성분 {perp:F2}).";
        Mark($"0점 고정 - 파지폭 {len0 * 100f:F0}cm. {verdict}");
    }

    [ContextMenu("3 - 능동 끝점")]
    public void MarkActiveEnd()
    {
        if (!neutralReady) { Warn("중립 캡처가 먼저입니다."); return; }
        if (!TryGetAngle(out float deg, out _, out float signed)) { Warn("각을 못 읽습니다."); return; }

        int i = (int)direction;
        results[i].active = signed; results[i].hasActive = true;   // 부호째 담는다 — 지침이 어느 쪽인지
        stage = Stage.Passive;
        holdTimer = 0f; peakAngle = 0f;
        Mark($"{Label(direction)} 능동 {deg:F1}도 (부호 {signed:+0.0;-0.0}). 이제 끝 느낌까지 압박하세요.");
    }

    [ContextMenu("4 - 압박 끝점")]
    public void MarkPassiveEnd()
    {
        if (!neutralReady) { Warn("중립 캡처가 먼저입니다."); return; }
        if (!TryGetAngle(out float deg, out _, out float signed)) { Warn("각을 못 읽습니다."); return; }

        int i = (int)direction;
        results[i].passive = signed; results[i].hasPassive = true;
        stage = Stage.Done;
        holdTimer = 0f;

        float gain = results[i].hasActive ? deg - Mathf.Abs(results[i].active) : float.NaN;
        Mark($"{Label(direction)} 수동 {deg:F1}도 (부호 {signed:+0.0;-0.0}) · 능동 대비 {gain:F1}도. " +
             "다음 방향으로 넘기거나 재파지 후 0점을 다시 잡으세요.");
    }

    [ContextMenu("5 - 다음 방향")]
    public void NextDirection()
    {
        int n = (int)direction;
        n = n >= 6 ? 1 : n + 1;
        direction = (CervicalRomDriver.Direction)n;

        // ★0점은 방향마다 다시 잡는다. 파지가 바뀌면 v0가 통째로 달라진다.
        neutralReady = false;
        stage = Stage.AwaitNeutral;
        holdTimer = 0f; peakAngle = 0f;
        frameStamp++;
        Mark($"-> {Label(direction)}. {GripHintFor(direction)} 파지 후 중립에서 정지하세요.");
    }

    [ContextMenu("9 - 결과 찍기")]
    public void DumpResults()
    {
        sb.Clear();
        sb.AppendLine("<color=cyan>[실측ROM] 측정 결과</color>");
        for (int i = 1; i <= 6; i++)
        {
            Result r = results[i];
            if (!r.hasActive && !r.hasPassive) continue;
            string a = r.hasActive ? $"{Mathf.Abs(r.active):F1}도" : "-";
            string p = r.hasPassive ? $"{Mathf.Abs(r.passive):F1}도" : "-";
            string d = (r.hasActive && r.hasPassive)
                ? $"{Mathf.Abs(r.passive) - Mathf.Abs(r.active):F1}도" : "-";
            sb.AppendLine($"  {Label((CervicalRomDriver.Direction)i)}  능동 {a} · 수동 {p} · 차이 {d}");
        }
        ChunaLogger.Log(sb.ToString());
    }

    // ================= 매 프레임 =================

    private void Update()
    {
        // ★실측모드가 아니면 아무것도 하지 않는다(2026-08-28 사용자 지적).
        //   브리지가 켜고 끄는 것에만 기대면, 브리지가 없거나 순서가 어긋난 순간
        //   교육모드 화면에 실측 안내와 각도기가 끼어든다. 스스로도 막는다.
        if (!IsMeasurementMode())
        {
            if (root != null) TearDownVisuals();
            return;
        }

        bool has = TryGetHands(out Vector3 l, out Vector3 r);

        if (has)
        {
            Vector3 raw = r - l;
            if (!vNowValid || smoothing <= 0f)
            {
                vNow = raw;
            }
            else
            {
                float k = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(1e-4f, smoothing));
                vNow = Vector3.Lerp(vNow, raw, k);
            }
            vNowValid = true;
        }
        else
        {
            vNowValid = false;
        }

        UpdateHold(has, l, r);
        if (useKeyboard) ReadKeys();
        UpdateVisuals();
    }

    /// <summary>양손이 멈춰 있으면 단계를 넘긴다. VR에서 버튼 없이 진행하는 유일한 손잡이다.</summary>
    private void UpdateHold(bool has, Vector3 l, Vector3 r)
    {
        if (!has || !prevValid)
        {
            prevLeft = l; prevRight = r; prevValid = has; holdTimer = 0f;
            return;
        }

        float dt = Mathf.Max(1e-4f, Time.deltaTime);
        float speed = Mathf.Max((l - prevLeft).magnitude, (r - prevRight).magnitude) / dt;
        prevLeft = l; prevRight = r;

        if (neutralReady && TryGetAngle(out float deg, out _, out _))
            peakAngle = Mathf.Max(peakAngle, deg);

        holdTimer = speed <= holdSpeedThreshold ? holdTimer + dt : 0f;
        if (!advanceOnHold || holdTimer < holdSeconds) return;

        switch (stage)
        {
            case Stage.AwaitShoulders:
                CaptureShoulderFrame();
                break;
            case Stage.AwaitNeutral:
                CaptureNeutral();
                break;
            case Stage.Active:
                if (peakAngle >= minAngleToMark) MarkActiveEnd();
                break;
            case Stage.Passive:
                if (peakAngle >= minAngleToMark) MarkPassiveEnd();
                break;
        }
        holdTimer = 0f;
    }

    private void ReadKeys()
    {
        if (Input.GetKeyDown(KeyCode.F1)) CaptureShoulderFrame();
        else if (Input.GetKeyDown(KeyCode.F2)) CaptureNeutral();
        else if (Input.GetKeyDown(KeyCode.F3)) MarkActiveEnd();
        else if (Input.GetKeyDown(KeyCode.F4)) MarkPassiveEnd();
        else if (Input.GetKeyDown(KeyCode.F5)) NextDirection();
        else if (Input.GetKeyDown(KeyCode.F6)) DumpResults();
        else if (Input.GetKeyDown(KeyCode.F7)) ResetAll();
    }

    /// <summary>지금 실측모드인가. DifficultyManager가 없으면 아니라고 본다(안전한 쪽).</summary>
    private static bool IsMeasurementMode()
        => ChunaTraining.DifficultyManager.Instance != null
           && ChunaTraining.DifficultyManager.Instance.IsMeasurementMode;

    private bool TryGetHands(out Vector3 left, out Vector3 right)
    {
        left = Vector3.zero;
        right = Vector3.zero;

        if (leftHandOverride != null && rightHandOverride != null)
        {
            left = leftHandOverride.position;
            right = rightHandOverride.position;
            return true;
        }

        if (gripJudge == null) return false;
        // ★엄지·검지 파지 지점만 쓴다. 손목·손바닥은 안 본다(2026-08-28 사용자 지시).
        if (!gripJudge.TryGetPinchPoint(GripFingerTip.Side.Left, out Vector3 l)) return false;
        if (!gripJudge.TryGetPinchPoint(GripFingerTip.Side.Right, out Vector3 r)) return false;
        left = l; right = r;
        return true;
    }

    // ================= 표시 =================

    private void UpdateVisuals()
    {
        if (!showReadout && !showAxes && !showGauge) { TearDownVisuals(); return; }
        if (root == null) BuildVisuals();

        bool slip = IsSlipping(out float slipRatio);
        bool measurable = TryGetAngle(out float deg, out float perp, out _);
        int warn = slip ? 2 : (measurable && perp < minPerpRatio ? 1 : 0);

        if (showAxes && frameReady) UpdateAxisLines();
        UpdateGauge();

        if (!showReadout || readout == null) return;

        int shown = measurable ? Mathf.RoundToInt(deg) : int.MinValue + 1;
        int holdStep = HoldBarSteps();
        int mask = DoneMask();
        if (stage == shownStage && shown == shownAngle && warn == shownWarn
            && holdStep == shownHold && mask == shownMask) return;
        shownStage = stage; shownAngle = shown; shownWarn = warn;
        shownHold = holdStep; shownMask = mask;

        sb.Clear();

        if (showProgress)
        {
            sb.Append("<size=70%>");
            AppendStageChain();
            sb.Append("</size>\n");
        }

        switch (stage)
        {
            case Stage.AwaitShoulders:
                sb.Append("1) 양손을 환자 양 어깨에 - 정지");
                break;
            case Stage.AwaitNeutral:
                sb.Append("2) 중립에서 머리를 파지 - 정지");
                break;
            default:
                sb.Append(Label(direction));
                sb.Append("  ");
                if (measurable) { sb.Append(shown); sb.Append('도'); } else sb.Append("--");
                break;
        }

        if (neutralReady)
        {
            Result res = results[(int)direction];
            sb.Append('\n');
            sb.Append(res.hasActive ? $"능동 {Mathf.Abs(res.active):F0}도" : "능동 -");
            sb.Append(res.hasPassive ? $"   수동 {Mathf.Abs(res.passive):F0}도" : "   수동 -");
            if (res.hasActive && res.hasPassive)
                sb.Append($"   차이 {Mathf.Abs(res.passive) - Mathf.Abs(res.active):F0}도");
        }

        if (warn == 2) sb.Append($"\n<color=#ff6b6b>파지 미끄러짐 {(slipRatio - 1f) * 100f:+0;-0}%</color>");
        else if (warn == 1) sb.Append($"\n<color=#ffcc55>이 파지로는 못 잽니다 (면 {perp:F2})</color>");

        if (showProgress)
        {
            sb.Append("\n<size=70%>");
            AppendHoldBar(holdStep);
            sb.Append('\n');
            AppendDirectionRow();
            sb.Append("</size>");
        }

        readout.text = sb.ToString();
        readout.transform.position = pivot + Vector3.up * 0.22f;
        FaceCamera(readout.transform);
    }

    // ---- 진행 표시 ----

    /// <summary>어깨 → 중립 → 능동 → 압박. 지금 어디인지와 뭐가 끝났는지만 본다.</summary>
    private void AppendStageChain()
    {
        Result res = results[(int)direction];
        AppendChainItem("어깨", stage == Stage.AwaitShoulders, frameReady);
        sb.Append(" ▶ ");
        AppendChainItem("중립", stage == Stage.AwaitNeutral, neutralReady);
        sb.Append(" ▶ ");
        AppendChainItem("능동", stage == Stage.Active, res.hasActive);
        sb.Append(" ▶ ");
        AppendChainItem("압박", stage == Stage.Passive, res.hasPassive);
    }

    private void AppendChainItem(string label, bool current, bool done)
    {
        if (current) sb.Append("<color=#ffcc55><b>");
        else if (done) sb.Append("<color=#7ad67a>");
        else sb.Append("<color=#808080>");
        sb.Append(label);
        sb.Append(current ? "</b></color>" : "</color>");
    }

    /// <summary>
    /// 정지 게이지 칸 수(0~10). 매 프레임 문자열을 다시 만들지 않으려고 양자화한다.
    ///   −1 = 지금 정지해도 넘어갈 단계가 아니다 · −2 = 아직 최소 각도만큼 안 움직였다
    /// </summary>
    private int HoldBarSteps()
    {
        if (!advanceOnHold || holdSeconds <= 0f) return -1;
        switch (stage)
        {
            case Stage.AwaitShoulders:
            case Stage.AwaitNeutral:
                break;
            case Stage.Active:
            case Stage.Passive:
                if (peakAngle < minAngleToMark) return -2;
                break;
            default:
                return -1;
        }
        return Mathf.Clamp(Mathf.RoundToInt(holdTimer / holdSeconds * 10f), 0, 10);
    }

    private void AppendHoldBar(int steps)
    {
        if (steps == -1) { sb.Append("<color=#808080>키로 진행</color>"); return; }
        if (steps == -2)
        {
            sb.Append($"<color=#808080>움직임 대기 ({minAngleToMark:F0}도 이상)</color>");
            return;
        }

        sb.Append(steps >= 10 ? "<color=#7ad67a>정지 " : "정지 ");
        for (int i = 0; i < 10; i++) sb.Append(i < steps ? '■' : '□');
        if (steps >= 10) sb.Append("</color>");
    }

    private int DoneMask()
    {
        int m = 0;
        for (int i = 1; i <= 6; i++) if (results[i].hasPassive) m |= 1 << i;
        return m;
    }

    /// <summary>여섯 방향 중 어디까지 왔는지. ● 끝 · ▶ 지금 · ○ 아직.</summary>
    private void AppendDirectionRow()
    {
        for (int i = 1; i <= 6; i++)
        {
            if (i > 1) sb.Append(' ');
            var d = (CervicalRomDriver.Direction)i;

            if (d == direction) sb.Append("<color=#ffcc55>▶");
            else if (results[i].hasPassive) sb.Append("<color=#7ad67a>●");
            else sb.Append("<color=#808080>○");

            sb.Append(Label(d));
            sb.Append("</color>");
        }
    }

    // ---- 180도 각도기 ----
    //
    // ★실습모드의 CervicalRomPlaneGauge와 코드를 한 줄도 공유하지 않는다.
    //   그쪽은 driver의 <b>대본 각도</b>를 그리는 물건이고 13개 술기가 같이 쓴다.
    //   이쪽은 t0 기준틀 위에 손으로 잰 각을 그린다.
    //
    // 0도를 한가운데 두고 좌우로 gaugeHalfSpan(기본 90)씩 — 합쳐서 180도 반원이다.
    // 굴곡을 재는 중이라도 반대쪽(신전 쪽) 눈금이 같이 깔린다.

    private static bool IsRotationDir(CervicalRomDriver.Direction d)
        => d == CervicalRomDriver.Direction.RotationLeft || d == CervicalRomDriver.Direction.RotationRight;

    /// <summary>각도기의 0도가 가리키는 방향. 회전만 정면 기준이고 나머지는 머리 위쪽이다.</summary>
    private Vector3 GaugeZero => IsRotationDir(direction) ? axFwd : axUp;

    /// <summary>각도기 위 한 점의 방향(월드). 부호는 측정 부호와 같은 축을 쓴다.</summary>
    private Vector3 GaugeDir(float degrees)
        => Quaternion.AngleAxis(degrees, AxisFor(direction)) * GaugeZero;

    private static bool OnStep(float degrees, float step)
        => step > 0f && Mathf.Abs(degrees % step) < 0.001f;

    private void UpdateGauge()
    {
        bool on = showGauge && neutralReady && frameReady;

        if (gaugeFilter != null && gaugeFilter.gameObject.activeSelf != on)
            gaugeFilter.gameObject.SetActive(on);
        if (needle != null && needle.enabled != on) needle.enabled = on;
        if (activeMark != null) activeMark.enabled = on;
        if (passiveMark != null) passiveMark.enabled = on;

        if (!on)
        {
            for (int i = 0; i < gaugeLabels.Count; i++)
                if (gaugeLabels[i] != null) gaugeLabels[i].gameObject.SetActive(false);
            return;
        }

        if (direction != builtDirection || frameStamp != builtStamp) RebuildGauge();

        if (TryGetAngle(out _, out _, out float signed))
            SetLine(needle, pivot, pivot + GaugeDir(signed) * gaugeRadius);
        else
            SetLine(needle, pivot, pivot);

        Result r = results[(int)direction];
        SetMark(activeMark, r.hasActive, r.active);
        SetMark(passiveMark, r.hasPassive, r.passive);
    }

    /// <summary>기록된 각에 바깥쪽 굵은 눈금을 남긴다. 지침과 달리 중심까지 안 온다.</summary>
    private void SetMark(LineRenderer lr, bool has, float signedDeg)
    {
        if (lr == null) return;
        if (!has) { SetLine(lr, pivot, pivot); return; }
        Vector3 d = GaugeDir(signedDeg);
        SetLine(lr, pivot + d * (gaugeRadius * 0.72f), pivot + d * (gaugeRadius * 1.08f));
    }

    private void RebuildGauge()
    {
        builtDirection = direction;
        builtStamp = frameStamp;

        gVerts.Clear(); gTris.Clear(); gCols.Clear();

        float half = Mathf.Max(5f, gaugeHalfSpan);
        Vector3 axis = AxisFor(direction);
        if (axis.sqrMagnitude < 1e-8f) return;

        // 미세 → 보조 → 주 순으로 쌓는다. 굵은 눈금이 나중에 와야 위에 보인다.
        if (microStep > 0f)
        {
            for (float a = -half; a <= half + 0.001f; a += microStep)
            {
                if (OnStep(a, minorStep) || OnStep(a, majorStep)) continue;
                AddTickQuad(a, microTickLength, tickWidth * 0.7f, microColor, axis);
            }
        }
        if (minorStep > 0f)
        {
            for (float a = -half; a <= half + 0.001f; a += minorStep)
            {
                if (OnStep(a, majorStep)) continue;
                AddTickQuad(a, minorTickLength, tickWidth, tickColor, axis);
            }
        }
        if (majorStep > 0f)
        {
            for (float a = -half; a <= half + 0.001f; a += majorStep)
                AddTickQuad(a, majorTickLength, tickWidth * 1.4f, tickColor, axis);
        }

        // 0도 기준선 — 중심에서 눈금까지 통짜로 긋는다. 어디가 중립인지가 제일 중요하다.
        AddTickQuad(0f, gaugeRadius, tickWidth * 1.6f, zeroLineColor, axis);

        gaugeMesh.Clear();
        gaugeMesh.SetVertices(gVerts);
        gaugeMesh.SetColors(gCols);
        gaugeMesh.SetTriangles(gTris, 0);
        gaugeMesh.RecalculateBounds();

        RebuildGaugeLabels(half);

        if (showDebugLogs)
            ChunaLogger.Log($"<color=cyan>[실측ROM] 각도기 재생성 — {Label(direction)} · " +
                            $"{-half:F0}~{half:F0}도({half * 2f:F0}도) · 눈금 {gVerts.Count / 4}개</color>");
    }

    private void AddTickQuad(float degrees, float length, float width, Color c, Vector3 axis)
    {
        Vector3 d = GaugeDir(degrees);
        Vector3 side = Vector3.Cross(d, axis);
        if (side.sqrMagnitude < 1e-10f) return;
        side = side.normalized * (width * 0.5f);

        Vector3 outer = pivot + d * gaugeRadius;
        Vector3 inner = pivot + d * (gaugeRadius - length);

        int b = gVerts.Count;
        gVerts.Add(inner - side); gVerts.Add(inner + side);
        gVerts.Add(outer + side); gVerts.Add(outer - side);
        for (int i = 0; i < 4; i++) gCols.Add(c);

        gTris.Add(b); gTris.Add(b + 1); gTris.Add(b + 2);
        gTris.Add(b); gTris.Add(b + 2); gTris.Add(b + 3);
    }

    /// <summary>주눈금 숫자. ★양쪽 다 <b>절댓값</b>으로 적는다 — 각도기지 좌표축이 아니다.</summary>
    private void RebuildGaugeLabels(float half)
    {
        int needed = majorStep > 0f ? Mathf.FloorToInt(half * 2f / majorStep) + 1 : 0;
        while (gaugeLabels.Count < needed)
        {
            var go = new GameObject($"눈금{gaugeLabels.Count}") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(root, false);
            var tm = go.AddComponent<TextMeshPro>();
            if (font != null) tm.font = font;
            tm.fontSize = gaugeLabelSize * 100f;
            tm.transform.localScale = Vector3.one * 0.01f;
            tm.alignment = TextAlignmentOptions.Center;
            tm.textWrappingMode = TextWrappingModes.NoWrap;
            tm.raycastTarget = false;
            gaugeLabels.Add(tm);
        }

        int index = 0;
        for (float a = -half; a <= half + 0.001f && index < gaugeLabels.Count; a += majorStep, index++)
        {
            TextMeshPro tm = gaugeLabels[index];
            tm.gameObject.SetActive(true);
            tm.text = Mathf.Abs(a) < 0.001f ? "0" : $"{Mathf.Abs(a):F0}";
            tm.color = Mathf.Abs(a) < 0.001f ? zeroLineColor : tickColor;
            tm.transform.position = pivot + GaugeDir(a) * (gaugeRadius + gaugeLabelOffset);
        }
        for (int i = index; i < gaugeLabels.Count; i++) gaugeLabels[i].gameObject.SetActive(false);
    }

    private void UpdateAxisLines()
    {
        SetLine(lineRight, pivot, pivot + axRight * axisLength);
        SetLine(lineUp, pivot, pivot + axUp * axisLength);
        SetLine(lineFwd, pivot, pivot + axFwd * axisLength);

        if (neutralReady)
        {
            Vector3 half0 = v0.normalized * (len0 * 0.5f);
            SetLine(lineNeutral, pivot - half0, pivot + half0);
            if (vNowValid)
            {
                Vector3 d = vNow * 0.5f;
                SetLine(lineNow, pivot - d, pivot + d);
            }
        }
        else
        {
            SetLine(lineNeutral, pivot, pivot);
            SetLine(lineNow, pivot, pivot);
        }
    }

    private static void SetLine(LineRenderer lr, Vector3 a, Vector3 b)
    {
        if (lr == null) return;
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);
    }

    private void BuildVisuals()
    {
        var go = new GameObject("RealityMeasureVisuals") { hideFlags = HideFlags.DontSave };
        root = go.transform;
        sharedMaterial = CreateMaterial();

        lineRight = CreateLine("축_좌우", new Color(1f, 0.35f, 0.35f));
        lineUp = CreateLine("축_수직", new Color(0.4f, 1f, 0.45f));
        lineFwd = CreateLine("축_전후", new Color(0.4f, 0.6f, 1f));
        lineNeutral = CreateLine("중립파지", new Color(1f, 1f, 1f, 0.65f));
        lineNow = CreateLine("현재파지", new Color(1f, 0.85f, 0.2f));

        needle = CreateLine("지침", needleColor);
        needle.widthMultiplier = 0.009f;
        activeMark = CreateLine("능동마크", activeMarkColor);
        activeMark.widthMultiplier = 0.008f;
        passiveMark = CreateLine("수동마크", passiveMarkColor);
        passiveMark.widthMultiplier = 0.008f;

        var gm = new GameObject("각도기180") { hideFlags = HideFlags.DontSave };
        gm.transform.SetParent(root, false);
        gaugeFilter = gm.AddComponent<MeshFilter>();
        MeshRenderer gr = gm.AddComponent<MeshRenderer>();
        gr.sharedMaterial = sharedMaterial;
        gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        gr.receiveShadows = false;
        gaugeMesh = new Mesh { name = "RealityGauge180" };
        gaugeMesh.MarkDynamic();
        gaugeFilter.sharedMesh = gaugeMesh;
        builtDirection = CervicalRomDriver.Direction.None;
        builtStamp = -1;

        var t = new GameObject("현재각") { hideFlags = HideFlags.DontSave };
        t.transform.SetParent(root, false);
        readout = t.AddComponent<TextMeshPro>();
        if (font != null) readout.font = font;
        readout.fontSize = readoutSize * 100f;
        readout.transform.localScale = Vector3.one * 0.01f;
        readout.alignment = TextAlignmentOptions.Center;
        readout.fontStyle = FontStyles.Bold;
        readout.textWrappingMode = TextWrappingModes.NoWrap;
        readout.raycastTarget = false;
    }

    private LineRenderer CreateLine(string name, Color c)
    {
        var go = new GameObject(name) { hideFlags = HideFlags.DontSave };
        go.transform.SetParent(root, false);
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.widthMultiplier = 0.006f;
        lr.numCapVertices = 2;
        lr.sharedMaterial = sharedMaterial;
        lr.startColor = lr.endColor = c;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        return lr;
    }

    /// <summary>★게이지와 같은 방식이다. Sprites/Default가 빌드에서 스트립되면 Standard로 떨어진다.</summary>
    private static Material CreateMaterial()
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Standard");
        return new Material(shader) { name = "RealityMeasureMat", renderQueue = 3000 };
    }

    private void TearDownVisuals()
    {
        if (root != null)
        {
            if (Application.isPlaying) Destroy(root.gameObject); else DestroyImmediate(root.gameObject);
        }
        if (sharedMaterial != null)
        {
            if (Application.isPlaying) Destroy(sharedMaterial); else DestroyImmediate(sharedMaterial);
        }
        if (gaugeMesh != null)
        {
            if (Application.isPlaying) Destroy(gaugeMesh); else DestroyImmediate(gaugeMesh);
        }

        root = null; readout = null; sharedMaterial = null;
        lineRight = lineUp = lineFwd = lineNeutral = lineNow = null;
        needle = activeMark = passiveMark = null;
        gaugeMesh = null; gaugeFilter = null;
        gaugeLabels.Clear();
        builtDirection = CervicalRomDriver.Direction.None; builtStamp = -1;
        shownStage = (Stage)(-1); shownAngle = int.MinValue; shownWarn = -1;
        shownHold = -99; shownMask = -1;
    }

    private static void FaceCamera(Transform t)
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        t.rotation = Quaternion.LookRotation(t.position - cam.transform.position, Vector3.up);
    }

    // ================= 잡동사니 =================

    private static string Label(CervicalRomDriver.Direction d)
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

    private static string GripHintFor(CervicalRomDriver.Direction d)
    {
        switch (d)
        {
            case CervicalRomDriver.Direction.Flexion:
            case CervicalRomDriver.Direction.Extension: return "이마·후두";
            default:                                    return "양 측두부";
        }
    }

    private void Mark(string message)
    {
        shownStage = (Stage)(-1);   // 다음 프레임에 표시를 다시 만들게 한다
        if (showDebugLogs) ChunaLogger.Log($"<color=cyan>[실측ROM] {message}</color>");
    }

    private void Warn(string message) => ChunaLogger.LogWarning($"[실측ROM] {message}");
}
