using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 경추 ROM 각도기. <b>해부학적 면을 반투명 판으로 머리에 관통시키고</b>, 그 면 위에
/// 경추부 축을 중심으로 눈금과 지침을 그린다.
///
///   굴곡·신전 → 시상면(청록)   좌우측굴 → 관상면(주황)   좌우회전 → 횡단면(초록)
///
/// ★움직임 평면이 곧 해부학적 면이다. <see cref="CervicalRomDriver.CurrentWorldAxis"/>에
///   수직인 평면이 그 면이고, 압박 각도를 재는 평면과 정확히 같다. 그래서 재는 것과
///   보여 주는 것이 어긋날 수 없다.
///
/// 눈금은 0°(중립)에서 정상각까지 그린다. 채움은 세 구간이다 —
///   0 → 능동 한계      환자가 스스로 간 데까지
///   능동 → 압박 한계    시술자가 밀어서 더 간 데까지
///   압박 → 정상각      부족각. 이게 기록할 값이다.
///
/// 렌더링은 이 프로젝트 관례를 따른다 — <b>Sprites/Default</b>. 알파 블렌드가 셰이더에
/// 고정돼 있어 맞출 상태가 없고, UI가 늘 쓰므로 빌드에서 스트립되지 않는다
/// (커스텀 셰이더가 빌드에서 죽은 xray 전례, Standard Fade가 조용히 불투명해진 전례를 피한다).
/// </summary>
[RequireComponent(typeof(Transform))]
public class CervicalRomPlaneGauge : MonoBehaviour
{
    [Header("=== 참조 (비우면 자동 탐색) ===")]
    [SerializeField] private CervicalRomDriver driver;

    [Tooltip("눈금 숫자 폰트. Assets/_NJS/Noto_Sans_KR/NotoSansKR-Bold 를 넣는다.\n" +
             "★Resources 밖에 있어 코드가 런타임에 못 찾는다 — 인스펙터에서 직접 할당해야 한다.\n" +
             "비워 두면 TMP 기본 폰트(LiberationSans)가 쓰여 한글이 깨진다.")]
    [SerializeField] private TMP_FontAsset font;

    [Header("=== 면 판 ===")]
    [Tooltip("해부학적 면을 반투명 판으로 그린다. 끄면 눈금만 남는다.")]
    [SerializeField] private bool showPlane = true;

    [Tooltip("판 크기를 눈금·숫자에 맞춰 자동으로 잡는다.\n" +
             "★끄고 손으로 잡으면 숫자가 판 밖으로 삐져나간다 — 숫자는 반지름 + 오프셋 자리에 놓이므로\n" +
             "  판이 그보다 커야 한다. 켜 두면 반지름을 바꿔도 판이 따라 커진다.")]
    [SerializeField] private bool autoFitPlane = true;

    [Tooltip("자동 맞춤일 때 눈금 바깥으로 더 둘 여백 (m)")]
    [SerializeField] private float planeMargin = 0.10f;

    [Tooltip("판의 크기 (m). x=면 안 가로, y=면 안 세로. autoFitPlane이 꺼져 있을 때만 쓴다.")]
    [SerializeField] private Vector2 planeSize = new Vector2(0.95f, 1.05f);

    [Tooltip("판을 회전 중심에서 위로 얼마나 올릴지 (m). 경추 밑동이 아니라 머리에 걸치게 한다.\n" +
             "★면 '안'에서 미는 값이다. 머리 밖으로 빼는 건 아래 면수직 오프셋이다.")]
    [SerializeField] private float planeLift = 0.18f;

    [Header("=== 면수직 오프셋 (머리 밖으로 빼기) ===")]
    // ★각도기 전체를 면에 <b>수직으로</b> 밀어낸다. 회전축과 나란한 평행이동이라
    //   어떤 각도도 바뀌지 않는다 — 지침이 가리키는 값은 그대로다.
    //
    // ★회전축 부호를 기준으로 밀면 안 된다. AxisOf가 방향마다 부호를 뒤집기 때문에
    //   (굴곡 −X / 신전 +X, 우측굴 −Z / 좌측굴 +Z) 같은 값을 넣어도 굴곡은 오른쪽,
    //   신전은 왼쪽으로 간다. 그래서 몸통의 해부학 축을 기준으로 잡는다.
    //
    //   시상면(굴곡·신전)  torso.right    — 시술자가 서는 환자 우측으로 뺀다
    //   관상면(좌우측굴)   torso.forward  — 환자 앞쪽으로 뺀다
    //   횡단면(좌우회전)   torso.up       — 위아래로 뺀다

    [Tooltip("시상면(굴곡·신전)을 환자 좌우로 미는 거리 (m).\n" +
             "절차상 시술자가 환자 우측 측면에 서므로 양수면 그쪽으로 나온다.\n" +
             "★어느 쪽이 우측인지는 실측하지 않았다 — 반대로 나오면 부호를 뒤집으면 된다.")]
    [SerializeField] private float sagittalNormalOffset = 0.30f;

    [Tooltip("관상면(좌우측굴)을 환자 앞뒤로 미는 거리 (m). 양수면 앞쪽이다.\n" +
             "★앞뒤 부호도 미실측이다. 반대면 뒤집는다.")]
    [SerializeField] private float coronalNormalOffset = 0.32f;

    [Tooltip("횡단면(좌우회전)을 위아래로 미는 거리 (m).\n" +
             "목에 걸친 지금 모습이 보기 좋다고 하셔서 기본 0이다.")]
    [SerializeField] private float transverseNormalOffset = 0f;

    [Tooltip("판의 불투명도. 이미지처럼 옅게 깔린다.")]
    [Range(0f, 1f)] [SerializeField] private float planeAlpha = 0.18f;

    [Header("=== 색 (해부학 도해 관례) ===")]
    [Tooltip("시상면 — 굴곡·신전")]
    [SerializeField] private Color sagittalColor = new Color(0.31f, 0.78f, 0.80f);
    [Tooltip("관상면 — 좌·우 측굴")]
    [SerializeField] private Color coronalColor = new Color(0.96f, 0.63f, 0.36f);
    [Tooltip("횡단면 — 좌·우 회전")]
    [SerializeField] private Color transverseColor = new Color(0.48f, 0.78f, 0.48f);

    [Header("=== 눈금 ===")]
    [Tooltip("눈금 호의 반지름 (m).\n" +
             "★손끝 중점이 회전 중심에서 약 0.21m다(실측). 그보다 밖에 둬야 손에 안 가린다.")]
    [SerializeField] private float scaleRadius = 0.34f;

    [Tooltip("주눈금 간격 (도). 숫자가 붙는다.")]
    [SerializeField] private float majorStep = 10f;

    [Tooltip("보조눈금 간격 (도). 숫자는 안 붙는다.")]
    [SerializeField] private float minorStep = 5f;

    [Tooltip("미세눈금 간격 (도). 0이면 안 그린다.\n" +
             "반지름 0.34m에서 1° 간격이면 눈금 사이가 약 6mm라 촘촘하되 뭉개지지는 않는다(계산값).")]
    [SerializeField] private float microStep = 1f;

    [Tooltip("주눈금 길이 (m)")] [SerializeField] private float majorTickLength = 0.036f;
    [Tooltip("보조눈금 길이 (m)")] [SerializeField] private float minorTickLength = 0.022f;
    [Tooltip("미세눈금 길이 (m)")] [SerializeField] private float microTickLength = 0.011f;
    [Tooltip("눈금 굵기 (m)")] [SerializeField] private float tickWidth = 0.0032f;

    [Header("=== 채움 ===")]
    [Tooltip("능동 구간 — 환자가 스스로 간 데까지")]
    [SerializeField] private Color activeFillColor = new Color(0.29f, 0.56f, 0.89f, 0.55f);
    [Tooltip("압박 구간 — 시술자가 밀어서 더 간 데까지")]
    [SerializeField] private Color pressFillColor = new Color(0.95f, 0.55f, 0.18f, 0.60f);
    [Tooltip("부족각 — 정상각까지 남은 만큼. 결과로 읽을 값이다.")]
    [SerializeField] private Color deficitFillColor = new Color(0.55f, 0.55f, 0.58f, 0.30f);

    [Tooltip("채움 부채꼴의 바깥 반지름 (m). 눈금 안쪽에 깔린다.")]
    [SerializeField] private float fillRadius = 0.28f;

    [Tooltip("부족각 구간을 처음부터 보여줄지. 끄면 압박이 끝난 뒤에 드러난다.")]
    [SerializeField] private bool showDeficitFromStart = true;

    [Header("=== 지침 ===")]
    [Tooltip("현재 각도를 가리키는 바늘 색")]
    [SerializeField] private Color needleColor = new Color(0.10f, 0.10f, 0.12f, 0.95f);
    [Tooltip("지침 굵기 (m)")] [SerializeField] private float needleWidth = 0.0055f;

    [Header("=== 숫자 ===")]
    [Tooltip("눈금 숫자 크기")] [SerializeField] private float tickLabelSize = 0.030f;
    [Tooltip("현재 각도 숫자 크기")] [SerializeField] private float readoutSize = 0.055f;
    [Tooltip("숫자를 눈금에서 얼마나 바깥에 둘지 (m)")]
    [SerializeField] private float labelOffset = 0.038f;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = false;

    // ── 생성물 ────────────────────────────────────────────────────────────
    private Transform root;              // 면과 함께 도는 부모. 회전 중심에 붙는다.
    private MeshFilter staticFilter;     // 판 + 눈금 (방향이 바뀔 때만 다시 만든다)
    private MeshFilter dynamicFilter;    // 채움 + 지침 (각도가 변하면 다시 만든다)
    private Mesh staticMesh;
    private Mesh dynamicMesh;
    private Material sharedMaterial;
    private readonly List<TextMeshPro> tickLabels = new List<TextMeshPro>();
    private TextMeshPro readout;

    // ── 메시 버퍼. 매 프레임 새로 만들지 않는다(VR 프레임 예산). ──────────
    private readonly List<Vector3> verts = new List<Vector3>(512);
    private readonly List<Color> colors = new List<Color>(512);
    private readonly List<int> tris = new List<int>(1024);

    // ── 마지막으로 그린 상태. 바뀌었을 때만 다시 만든다. ─────────────────
    private CervicalRomDriver.Direction builtDirection = CervicalRomDriver.Direction.None;
    private float builtNormal = -1f;
    private float lastDrawnAngle = float.NaN;
    private float lastDrawnActive = float.NaN;
    private float lastDrawnPassive = float.NaN;
    private int lastReadoutDegrees = int.MinValue;

    private void Awake()
    {
        if (driver == null) driver = FindFirstObjectByType<CervicalRomDriver>();
        if (driver == null)
        {
            ChunaLogger.LogWarning("[ROM 각도기] CervicalRomDriver를 찾지 못했습니다. 각도기를 끕니다.");
            enabled = false;
            return;
        }
        if (font == null)
        {
            ChunaLogger.LogWarning("[ROM 각도기] 폰트가 비어 있습니다 — TMP 기본 폰트라 한글이 깨집니다. " +
                                   "인스펙터의 font에 Assets/_NJS/Noto_Sans_KR/NotoSansKR-Bold 를 넣으세요.");
        }
        BuildHierarchy();
        SetVisible(false);
    }

    private void OnDestroy()
    {
        if (staticMesh != null) Destroy(staticMesh);
        if (dynamicMesh != null) Destroy(dynamicMesh);
        if (sharedMaterial != null) Destroy(sharedMaterial);
    }

    /// <summary>
    /// ★LateUpdate에서 그린다. 드라이버가 목뼈에 각도를 얹은 <b>뒤</b>라야
    ///   지침과 실제 머리가 같은 프레임에서 맞는다.
    /// </summary>
    private void LateUpdate()
    {
        CervicalRomDriver.Direction dir = driver.CurrentDirection;
        if (dir == CervicalRomDriver.Direction.None || driver.Pivot == null || driver.Torso == null)
        {
            SetVisible(false);
            return;
        }

        Vector3 axis = driver.CurrentWorldAxis;
        if (axis.sqrMagnitude < 1e-6f) { SetVisible(false); return; }
        axis.Normalize();

        // 0°가 어디를 가리키는가 — 머리에 고정된 기준 방향이다.
        //   굴곡·신전·측굴은 머리 꼭대기(위)가 기울고, 회전은 코끝(앞)이 돈다.
        Vector3 zeroDir = IsRotation(dir) ? driver.Torso.forward : driver.Torso.up;
        zeroDir = Vector3.ProjectOnPlane(zeroDir, axis);
        if (zeroDir.sqrMagnitude < 1e-6f) { SetVisible(false); return; }
        zeroDir.Normalize();

        // 면 안의 두 기저. root를 이 자세로 두면 이하 계산이 전부 로컬로 끝난다.
        // ★면수직 오프셋은 회전축과 나란한 평행이동이라 각도에 영향이 없다.
        //   각도기를 머리 밖으로 빼도 지침이 가리키는 값은 그대로다.
        root.SetPositionAndRotation(driver.Pivot.position + NormalOffsetOf(dir),
                                    Quaternion.LookRotation(axis, zeroDir));
        SetVisible(true);

        float normal = driver.NormalAngle;
        if (dir != builtDirection || !Mathf.Approximately(normal, builtNormal))
        {
            BuildStatic(dir, normal);
            builtDirection = dir;
            builtNormal = normal;
        }

        float angle = driver.CurrentAngle;
        float activeLimit = driver.ActiveTargetAngle;
        float passiveLimit = driver.PassiveLimitAngle;

        // 0.25° 미만 변화는 무시한다. 눈에 안 보이는데 메시만 다시 만든다.
        if (Changed(angle, lastDrawnAngle) || Changed(activeLimit, lastDrawnActive)
                                           || Changed(passiveLimit, lastDrawnPassive))
        {
            BuildDynamic(angle, activeLimit, passiveLimit, normal);
            lastDrawnAngle = angle;
            lastDrawnActive = activeLimit;
            lastDrawnPassive = passiveLimit;
        }

        UpdateReadout(angle, normal);
    }

    private static bool Changed(float now, float before)
        => float.IsNaN(before) || Mathf.Abs(now - before) >= 0.25f;

    /// <summary>이 각도가 그 간격의 눈금 자리인가. 눈금이 겹쳐 그려지는 걸 막는다.</summary>
    private static bool OnStep(float degrees, float step)
        => step > 0f && Mathf.Abs(degrees % step) < 0.001f;

    private static bool IsRotation(CervicalRomDriver.Direction d)
        => d == CervicalRomDriver.Direction.RotationLeft || d == CervicalRomDriver.Direction.RotationRight;

    /// <summary>
    /// 각도기를 면에 수직으로 얼마나 밀어낼지(월드).
    /// ★몸통의 해부학 축을 쓴다 — 회전축은 방향마다 부호가 뒤집혀 기준으로 못 쓴다.
    /// </summary>
    private Vector3 NormalOffsetOf(CervicalRomDriver.Direction d)
    {
        Transform t = driver.Torso;
        if (t == null) return Vector3.zero;

        switch (d)
        {
            case CervicalRomDriver.Direction.Flexion:
            case CervicalRomDriver.Direction.Extension:
                return t.right * sagittalNormalOffset;       // 환자 좌우 — 시술자는 우측에 선다
            case CervicalRomDriver.Direction.LateralLeft:
            case CervicalRomDriver.Direction.LateralRight:
                return t.forward * coronalNormalOffset;      // 환자 앞뒤
            default:
                return t.up * transverseNormalOffset;        // 위아래
        }
    }

    private Color PlaneColorOf(CervicalRomDriver.Direction d)
    {
        switch (d)
        {
            case CervicalRomDriver.Direction.Flexion:
            case CervicalRomDriver.Direction.Extension:
                return sagittalColor;
            case CervicalRomDriver.Direction.LateralLeft:
            case CervicalRomDriver.Direction.LateralRight:
                return coronalColor;
            default:
                return transverseColor;
        }
    }

    private string PlaneNameOf(CervicalRomDriver.Direction d)
    {
        switch (d)
        {
            case CervicalRomDriver.Direction.Flexion:
            case CervicalRomDriver.Direction.Extension:
                return "시상면";
            case CervicalRomDriver.Direction.LateralLeft:
            case CervicalRomDriver.Direction.LateralRight:
                return "관상면";
            default:
                return "횡단면";
        }
    }

    // ── 면 안의 좌표 ──────────────────────────────────────────────────────
    // root의 로컬에서 axis = +z, zeroDir = +y다(LookRotation(axis, zeroDir)).
    // 그래서 각도 θ의 방향은 z축 둘레로 y를 돌린 것이고, 면은 xy 평면이 된다.

    private static Vector3 Dir(float degrees)
    {
        // ★x가 음수다. root가 LookRotation(axis, zeroDir)라 로컬 +z=axis · +y=zeroDir이고,
        //   머리는 Quaternion.AngleAxis(θ, axis)로 돌아간다. 그 회전을 로컬로 쓰면
        //   (0,1,0) → (−sinθ, cosθ, 0)이다. 예전엔 +sin을 써서 눈금과 지침이
        //   머리와 반대로 돌았다 — 사용자가 "좌우 앞뒤가 반대"로 본 것이 이것이다.
        float r = degrees * Mathf.Deg2Rad;
        return new Vector3(-Mathf.Sin(r), Mathf.Cos(r), 0f);
    }

    /// <summary>판·눈금. 방향이나 정상각이 바뀔 때만 다시 만든다.</summary>
    private void BuildStatic(CervicalRomDriver.Direction dir, float normal)
    {
        verts.Clear(); colors.Clear(); tris.Clear();

        Color plane = PlaneColorOf(dir);

        if (showPlane)
        {
            Color fill = plane; fill.a = planeAlpha;
            // ★자동 맞춤이면 숫자 자리(반지름 + 오프셋)보다 바깥까지 판을 넓힌다.
            //   손으로 잡으면 숫자가 판을 넘어간다 — 사용자가 본 그 현상이다.
            float hx, hy;
            if (autoFitPlane)
            {
                hx = hy = scaleRadius + labelOffset + planeMargin;
            }
            else
            {
                hx = planeSize.x * 0.5f;
                hy = planeSize.y * 0.5f;
            }
            AddQuad(new Vector3(-hx, planeLift - hy, 0f), new Vector3(hx, planeLift - hy, 0f),
                    new Vector3(hx, planeLift + hy, 0f), new Vector3(-hx, planeLift + hy, 0f), fill);
        }

        Color tick = plane; tick.a = 0.95f;
        Color micro = plane; micro.a = 0.55f;   // 미세눈금은 옅게 — 촘촘해서 진하면 띠로 뭉친다
        Color normalMark = new Color(0.10f, 0.10f, 0.12f, 0.95f);

        // 미세 → 보조 → 주 순으로 겹쳐 그린다. 굵은 눈금이 위에 온다.
        if (microStep > 0f)
        {
            for (float a = 0f; a <= normal + 0.001f; a += microStep)
            {
                if (OnStep(a, minorStep) || OnStep(a, majorStep)) continue;
                AddTick(a, microTickLength, tickWidth * 0.7f, micro);
            }
        }
        for (float a = 0f; a <= normal + 0.001f; a += minorStep)
        {
            if (OnStep(a, majorStep)) continue;
            AddTick(a, minorTickLength, tickWidth, tick);
        }
        for (float a = 0f; a <= normal + 0.001f; a += majorStep)
        {
            AddTick(a, majorTickLength, tickWidth * 1.35f, tick);
        }
        // 정상각은 눈금 사이에 안 떨어질 수 있다(예: 45°와 10° 간격). 따로 굵게 긋는다.
        AddTick(normal, majorTickLength * 1.3f, tickWidth * 1.8f, normalMark);

        Upload(staticMesh, staticFilter);
        BuildTickLabels(normal, plane);

        if (showDebugLogs)
        {
            ChunaLogger.Log($"<color=cyan>[ROM 각도기] {PlaneNameOf(dir)} — {dir} · 0~{normal:F0}° · " +
                            $"주눈금 {majorStep:F0}° · 반지름 {scaleRadius:F2}m</color>");
        }
    }

    /// <summary>채움 세 구간 + 지침. 각도가 변하면 다시 만든다.</summary>
    private void BuildDynamic(float angle, float activeLimit, float passiveLimit, float normal)
    {
        verts.Clear(); colors.Clear(); tris.Clear();

        // 채움은 겹치지 않게 구간을 나눠 그린다. 안쪽부터 바깥으로 읽힌다.
        AddSector(0f, Mathf.Min(angle, activeLimit), fillRadius, activeFillColor);

        if (angle > activeLimit)
        {
            AddSector(activeLimit, Mathf.Min(angle, passiveLimit), fillRadius, pressFillColor);
        }

        // 부족각 — 압박 한계에서 정상각까지. 이게 결과로 읽을 값이다.
        bool revealDeficit = showDeficitFromStart || angle >= passiveLimit - 0.5f;
        if (revealDeficit && normal > passiveLimit + 0.05f)
        {
            AddSector(passiveLimit, normal, fillRadius, deficitFillColor);
        }

        AddTick(angle, scaleRadius, needleWidth, needleColor, fromCenter: true);

        Upload(dynamicMesh, dynamicFilter);
    }

    /// <summary>지침 끝의 현재 각도 숫자. 정수가 바뀔 때만 문자열을 새로 만든다.</summary>
    private void UpdateReadout(float angle, float normal)
    {
        if (readout == null) return;

        int degrees = Mathf.RoundToInt(angle);
        if (degrees != lastReadoutDegrees)
        {
            lastReadoutDegrees = degrees;
            // ★문자열 생성은 정수가 바뀔 때만. 매 프레임 만들면 VR에서 GC가 튄다.
            readout.text = $"{degrees}°\n<size=55%>정상 {normal:F0}°</size>";
        }

        Vector3 local = Dir(angle) * (scaleRadius + labelOffset + 0.02f);
        readout.transform.localPosition = local;
        FaceCamera(readout.transform);
    }

    // ── 메시 조립 ─────────────────────────────────────────────────────────

    private void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
    {
        int i = verts.Count;
        verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
        colors.Add(color); colors.Add(color); colors.Add(color); colors.Add(color);
        tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
    }

    /// <summary>눈금 하나. fromCenter면 중심에서 뻗는 지침이 된다.</summary>
    private void AddTick(float degrees, float length, float width, Color color, bool fromCenter = false)
    {
        Vector3 dir = Dir(degrees);
        Vector3 side = new Vector3(dir.y, -dir.x, 0f) * (width * 0.5f);
        Vector3 outer = dir * scaleRadius;
        Vector3 inner = fromCenter ? Vector3.zero : dir * (scaleRadius - length);
        AddQuad(inner - side, outer - side, outer + side, inner + side, color);
    }

    /// <summary>부채꼴. 1도마다 한 조각씩 이어 붙인다.</summary>
    private void AddSector(float fromDeg, float toDeg, float radius, Color color)
    {
        if (toDeg - fromDeg < 0.05f) return;

        int steps = Mathf.Max(1, Mathf.CeilToInt(toDeg - fromDeg));
        float span = (toDeg - fromDeg) / steps;

        int center = verts.Count;
        verts.Add(Vector3.zero);
        colors.Add(color);

        for (int i = 0; i <= steps; i++)
        {
            verts.Add(Dir(fromDeg + span * i) * radius);
            colors.Add(color);
        }
        for (int i = 0; i < steps; i++)
        {
            tris.Add(center);
            tris.Add(center + 1 + i);
            tris.Add(center + 2 + i);
        }
    }

    private void Upload(Mesh mesh, MeshFilter filter)
    {
        mesh.Clear();
        if (verts.Count == 0) { filter.sharedMesh = mesh; return; }
        mesh.SetVertices(verts);
        mesh.SetColors(colors);
        mesh.SetTriangles(tris, 0, false);
        mesh.RecalculateBounds();
        filter.sharedMesh = mesh;
    }

    // ── 하위 오브젝트 ─────────────────────────────────────────────────────

    private void BuildHierarchy()
    {
        sharedMaterial = CreateMaterial();

        root = new GameObject("경추ROM_각도기").transform;
        root.SetParent(transform, false);

        staticMesh = new Mesh { name = "각도기_판눈금" };
        staticMesh.MarkDynamic();
        staticFilter = CreateLayer("판·눈금", root);

        dynamicMesh = new Mesh { name = "각도기_채움지침" };
        dynamicMesh.MarkDynamic();
        dynamicFilter = CreateLayer("채움·지침", root);

        readout = CreateLabel("현재각도", root, readoutSize);
    }

    private MeshFilter CreateLayer(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var filter = go.AddComponent<MeshFilter>();
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = sharedMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        return filter;
    }

    private TextMeshPro CreateLabel(string name, Transform parent, float size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshPro>();
        if (font != null) text.font = font;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = size * 100f;   // TMP는 월드 단위가 아니라 폰트 크기로 잡는다
        text.transform.localScale = Vector3.one * 0.01f;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }

    /// <summary>주눈금 숫자. 정상각이 바뀔 때만 다시 만든다(굴곡 45° vs 회전 90°).</summary>
    private void BuildTickLabels(float normal, Color color)
    {
        int needed = Mathf.FloorToInt(normal / majorStep) + 1;
        bool normalOnMajor = Mathf.Abs(normal % majorStep) < 0.001f;
        if (!normalOnMajor) needed++;   // 정상각 숫자를 따로 붙인다

        while (tickLabels.Count < needed)
        {
            tickLabels.Add(CreateLabel($"눈금{tickLabels.Count}", root, tickLabelSize));
        }

        Color labelColor = color; labelColor.a = 1f;
        int index = 0;
        for (float a = 0f; a <= normal + 0.001f; a += majorStep, index++)
        {
            PlaceTickLabel(index, a, labelColor, bold: false);
        }
        if (!normalOnMajor)
        {
            PlaceTickLabel(index, normal, new Color(0.10f, 0.10f, 0.12f, 1f), bold: true);
            index++;
        }
        for (int i = index; i < tickLabels.Count; i++) tickLabels[i].gameObject.SetActive(false);
    }

    private void PlaceTickLabel(int index, float degrees, Color color, bool bold)
    {
        if (index >= tickLabels.Count) return;
        TextMeshPro label = tickLabels[index];
        label.gameObject.SetActive(true);
        label.text = bold ? $"<b>{degrees:F0}°</b>" : $"{degrees:F0}";
        label.color = color;
        label.transform.localPosition = Dir(degrees) * (scaleRadius + labelOffset);
    }

    /// <summary>숫자는 늘 보는 사람 쪽을 향한다. 면에 눕히면 옆에서 읽을 수 없다.</summary>
    private static void FaceCamera(Transform t)
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        t.rotation = Quaternion.LookRotation(t.position - cam.transform.position, cam.transform.up);
    }

    private void SetVisible(bool visible)
    {
        if (root == null || root.gameObject.activeSelf == visible) return;
        root.gameObject.SetActive(visible);
    }

    /// <summary>
    /// ★프로젝트 관례 — Sprites/Default. 알파 블렌드가 셰이더에 고정돼 있어
    ///   맞출 상태가 없고, UI가 늘 쓰므로 빌드에서 스트립되지 않는다.
    ///   정점 색을 그대로 곱하므로 머티리얼 하나로 판·눈금·채움을 다 그릴 수 있다.
    /// </summary>
    private Material CreateMaterial()
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            ChunaLogger.LogWarning("[ROM 각도기] Sprites/Default를 찾지 못했습니다. 각도기가 안 보일 수 있습니다.");
            shader = Shader.Find("Standard");
        }
        return new Material(shader) { name = "CervicalRomGaugeMat", renderQueue = 3000 };
    }

    private void LateUpdateLabels()
    {
        for (int i = 0; i < tickLabels.Count; i++)
        {
            if (tickLabels[i].gameObject.activeSelf) FaceCamera(tickLabels[i].transform);
        }
    }

    private void OnEnable() => lastReadoutDegrees = int.MinValue;

    private void Update()
    {
        // 숫자만 매 프레임 보는 사람 쪽으로 돌린다. 메시는 건드리지 않는다.
        if (root != null && root.gameObject.activeSelf) LateUpdateLabels();
    }
}
