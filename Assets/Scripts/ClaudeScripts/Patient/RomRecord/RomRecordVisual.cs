using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 실측 기록의 표시물 — 3축, 기준축, 대추·미간, 단면 각도기, 동작 마커, 안내판(2026-09-18 신설).
/// ★그리기만 한다. 각은 <see cref="RomRecordGeometry"/>가 낸 값을 받아 쓴다(미리보기가 따로 계산하지 않는다 — 규칙 9).
/// ★위치 갱신은 값이 바뀔 때만 부른다. 매 프레임 하는 것은 글자를 눈 쪽으로 돌리는 것뿐이다.
/// </summary>
public class RomRecordVisual
{
    private const int RingSegments = 72;
    private const int TickCount = 36;      // 10°마다
    private const int TickLabelCount = 12; // 30°마다
    public const int MaxMarks = 4;
    /// <summary>
    /// 바늘 개수(2026-09-21에 2 → 4). 측굴·회전은 <b>좌 능동·좌 압박·우 능동·우 압박</b> 넷이 필요한데
    /// 배열이 2개라 <c>SetNeedle(2, ...)</c>·<c>SetNeedle(3, ...)</c>이 조용히 무시되고 있었다(사용자 지적).
    /// ★짝수 = 능동, 홀수 = 압박. 좌우는 바늘이 뻗는 <b>방향</b>으로 갈리므로 색으로 구분하지 않는다.
    /// </summary>
    public const int NeedleCount = 4;

    private Transform root;
    private TMP_FontAsset font;
    private Material lineMat;
    private readonly List<TextMeshPro> facing = new List<TextMeshPro>();

    private LineRenderer axisUp, axisFwd, axisRight, pivotAxis, neutralLine, ring, zeroLine;
    // ★회전 단계의 <b>원통 벽</b>용(2026-09-21). 평면 눈금판일 때는 꺼 둔다.
    //   ring = 위 테두리 겸 평면 원 · ringLower = 아래 테두리 · zeroRadial = 중심에서 벽까지 그은 0° 안내선.
    private LineRenderer ringLower, zeroRadial;
    // ★바늘은 <b>넷</b>이다(09-21 개정, 애초엔 둘이었다) — 능동과 압박을 나란히 놓고 눈으로 비교하는데,
    //   측굴·회전은 좌우가 따로 있어 2개로는 절반만 나왔다. <see cref="NeedleCount"/> 참고.
    private readonly LineRenderer[] needles = new LineRenderer[NeedleCount];
    private readonly Transform[] needleGrips = new Transform[NeedleCount];
    private readonly TextMeshPro[] needleLabels = new TextMeshPro[NeedleCount];
    private TextMeshPro labUp, labDown, labFwd, labBack, labRight, labLeft, panel;
    private Transform c7Dot, glabDot, liveDot, pivotDot;
    private readonly LineRenderer[] ticks = new LineRenderer[TickCount];
    private readonly TextMeshPro[] tickLabels = new TextMeshPro[TickLabelCount];
    private readonly Transform[] markDots = new Transform[MaxMarks];
    private readonly LineRenderer[] markLines = new LineRenderer[MaxMarks];
    private readonly TextMeshPro[] markLabels = new TextMeshPro[MaxMarks];
    private GameObject dialRoot;
    // ★마지막으로 그린 각도기의 단면 법선. 바늘 글자를 <b>선에서 옆으로</b> 비키는 데 쓴다(09-21).
    private Vector3 dialNormal = Vector3.up;

    // ★글자 크기는 TMP 폰트 크기(스케일 1)다. 기기에서 확인된 기존 값: 각도 판독 0.05 · 축·눈금 글자 0.03
    //   (CervicalRomPlaneGauge·PracticeReadout, TrainingScene). 09-18 첫 판은 1.1을 넣어 20~30배 컸다 —
    //   실측랩 코드의 labelSize 1.2를 그대로 따라 썼다.
    public float textSize = 0.05f;     // 축 끝 글자·마커 글자(눈금 숫자는 0.8배)
    public float panelSize = 0.045f;   // 안내판

    // ── 눈으로 맞추는 값(2026-09-21 신설) ────────────────────────────
    // ★전부 <b>기기 미검증 추정값</b>이다. Session이 Build 전에 대입해 주면 그 값이 쓰인다.
    //   각도기가 패스스루(실제 방) 위에 뜨는 탓에 흰색은 너무 튄다 — 회색 계열로 낮췄다(사용자 지시).
    //   글자에는 검은 외곽선이 있어(Label) 중간 회색이어도 밝은 벽에서 읽힌다.
    public Color dialColor = new Color(0.60f, 0.62f, 0.65f, 0.55f);       // 눈금판 원 · 원통 테두리
    public Color dialTickColor = new Color(0.64f, 0.66f, 0.69f, 0.60f);   // 10°·30° 눈금
    public Color dialZeroColor = new Color(0.88f, 0.90f, 0.93f, 0.95f);   // ★눈금 0은 기준이라 조금 더 또렷하게
    public Color dialLabelColor = new Color(0.80f, 0.82f, 0.85f, 0.95f);  // 눈금 숫자
    public float dialLineWidth = 0.002f;    // 원·테두리 굵기(0.004 → 0.003 → 0.002)
    public float dialTickWidth = 0.0013f;   // 눈금 굵기(0.003 → 0.002 → 0.0013)
    public float dialZeroWidth = 0.0025f;   // 눈금 0 굵기(0.005 → 0.004 → 0.0025)

    // ── 선 굵기(2026-09-21 신설) ─────────────────────────────────────
    // ★"바늘 전체적으로 모든 선들은 좀더 얇게"(사용자 지시)라 <b>바늘만이 아니라</b> 3축·기준축·중립선·
    //   마커선까지 한 벌로 낮췄다. 전부 <b>기기 미검증 추정값</b>으로, 종전의 약 0.6배다.
    //   계산 근거(추정): Quest 3은 눈에서 0.5m 거리에서 1mm가 대략 0.11°다. 각해상도가 20~25ppd쯤이니
    //   1.3mm면 2~3픽셀은 남아 선으로는 보인다 — 이보다 더 줄이면 끊겨 보일 위험이 있다.
    // ★Build에서 한 번만 읽는 값이다(각도기 굵기와 달리 다시 입히지 않는다). Session은 Build 전에 대입해라.
    public float axisWidth = 0.0025f;        // 3축(연직·전후·좌우). 옛 0.004
    public float pivotAxisWidth = 0.0035f;   // 기준축(목 중앙). 옛 0.006
    public float neutralLineWidth = 0.002f;  // 중립선(기준점→미간). 옛 0.003
    public float markLineWidth = 0.002f;     // 마커선(기준점→찍은 점). 옛 0.003

    // 바늘
    // ★손잡이 구체는 <b>보이는 크기만</b>이다. 잡는 판정 반경은 Session이 따로 들고 있다
    //   (<c>RomRecordSession.needleGrabRadius</c> = 0.1m). 구체를 줄여도 잡기는 그대로다.
    //   09-21 이전에는 "잡아야 하는 것이라 줄이지 않는다"고 적어 뒀는데, 사용자가 "니들 끝 구체도 너무 커"라고
    //   지적했다 — 잡기 반경과 보이는 크기가 애초에 다른 값이라 그 판단이 틀렸다.
    public float needleWidth = 0.002f;        // 0.007 → 0.003 → 0.002
    public float needleGripSize = 0.018f;     // 평소. 옛 0.035(지름 3.5cm는 눈금을 가렸다)
    public float needleGripHeldSize = 0.024f; // 잡고 있는 동안. 옛 0.045

    // 글자를 선·원에서 비켜 놓는 양 — ★글자가 선 위에 얹히면 둘 다 안 읽힌다(09-21 사용자 지적).
    public float tickLabelOut = 1.20f;      // 눈금 숫자를 원 밖으로 미는 배수(옛 1.12 — 원에 붙어 있었다)
    public float needleLabelAlong = 0.55f;  // 바늘 방향으로 나가는 거리(반지름 배수)
    public float needleLabelSide = 0.24f;   // ★바늘에 <b>수직</b>으로 비키는 거리(반지름 배수).
                                            //   능동(짝수)은 +쪽, 압박(홀수)은 -쪽으로 갈라 쌍끼리 안 겹친다.
    public float needleLabelAlongStep = 0.22f; // ★좌우 쌍(0·1 / 2·3)을 <b>반지름으로도</b> 어긋나게 하는 양.
                                               //   좌우 바늘이 둘 다 0°에 가까우면 각으로는 안 갈라져서
                                               //   수직 비킴만으로는 넷 중 둘이 겹친다(09-21 개정).

    // 회전(횡단면) 각도기를 <b>원통 벽</b>으로 세운다 — 수평 원판은 보는 높이에 따라 납작해져 안 보인다.
    // ★보이는 모양만 바꾸는 것이다. 각 계산(RomRecordGeometry.PlaneAngle)은 그대로 횡단면 투영이다.
    public bool cylinderOnHorizontalDial = true;
    public float cylinderHeight = 0.20f;          // ★추정 — 사람 머리 높이 어림(후보 0.15~0.25m). 기기 미검증
    public float cylinderMinorHeightRatio = 0.35f;// 10° 세로선은 이만큼만(30°는 위아래 테두리까지 꽉)
    public float cylinderLabelRise = 0.03f;       // 숫자를 위 테두리보다 이만큼 더 위에
    // ★원통 벽을 <b>보이는 크기만</b> 키우는 배수(09-21 사용자 지시 "원통형 지름좀 키우고").
    //   들어오는 radius는 회전중심에서 미간까지(머리 반지름쯤 = 0.1m 안팎)라 벽이 머리에 바짝 붙는다.
    //   ★각 계산은 전혀 건드리지 않는다 — 각은 RomRecordGeometry가 내고, 바늘·손잡이도 Session이 주는
    //     원래 radius/handleRadius를 그대로 쓴다. 여기서 커지는 것은 <b>테두리·세로눈금·숫자</b>뿐이다.
    //   기본 1.6은 <b>기기 미검증 추정</b>이다(머리 반지름 0.1m 기준으로 벽이 0.16m가 되어 귀에서 6cm쯤 뜬다).
    public float cylinderRadiusScale = 1.6f;

    public static readonly Color UpColor = new Color(0.35f, 0.95f, 0.45f);
    public static readonly Color FwdColor = new Color(0.35f, 0.65f, 1f);
    public static readonly Color RightColor = new Color(1f, 0.4f, 0.4f);
    public static readonly Color PivotColor = new Color(1f, 0.85f, 0.2f);
    public static readonly Color ActiveColor = new Color(1f, 0.6f, 0.15f);
    public static readonly Color PassiveColor = new Color(0.95f, 0.35f, 0.95f);
    public static readonly Color NeedleColor = new Color(0.2f, 1f, 0.85f);      // 바늘 — 마커 색과 겹치지 않게 청록
    public static readonly Color NeedleHeldColor = new Color(1f, 1f, 0.35f);    // 잡고 있는 동안

    public void Build(Transform parent, TMP_FontAsset f, Material mat)
    {
        font = f;
        lineMat = mat;
        root = new GameObject("[실측기록] 표시").transform;
        root.SetParent(parent, false);

        axisUp = Line("연직축", UpColor, axisWidth);
        axisFwd = Line("전후축", FwdColor, axisWidth);
        axisRight = Line("좌우축", RightColor, axisWidth);
        pivotAxis = Line("기준축(목 중앙)", PivotColor, pivotAxisWidth);
        neutralLine = Line("중립선", Color.white, neutralLineWidth);

        labUp = Label("위", textSize, UpColor);
        labDown = Label("아래", textSize * 0.8f, UpColor);
        labFwd = Label("환자 앞", textSize, FwdColor);
        labBack = Label("환자 뒤", textSize * 0.8f, FwdColor);
        labRight = Label("환자 오른쪽", textSize, RightColor);
        labLeft = Label("환자 왼쪽", textSize * 0.8f, RightColor);

        pivotDot = Dot("기준점", PivotColor, 0.012f);
        c7Dot = Dot("대추", new Color(0.3f, 0.9f, 1f), 0.016f);
        glabDot = Dot("미간(중립)", Color.white, 0.016f);
        liveDot = Dot("핀치 중", new Color(1f, 1f, 0.3f), 0.014f);

        dialRoot = new GameObject("각도기");
        dialRoot.transform.SetParent(root, false);
        ring = Line("눈금판(위 테두리)", dialColor, dialLineWidth, dialRoot.transform);
        ring.positionCount = RingSegments + 1;
        // ★원통 벽의 아래 테두리. 평면 눈금판일 때는 꺼 둔다 — 개수를 고정해 미리 만든다(런타임 생성 금지).
        ringLower = Line("눈금판(아래 테두리)", dialColor, dialLineWidth, dialRoot.transform);
        ringLower.positionCount = RingSegments + 1;
        zeroLine = Line("눈금 0", dialZeroColor, dialZeroWidth, dialRoot.transform);
        zeroRadial = Line("눈금 0(중심→벽)", dialZeroColor, dialZeroWidth, dialRoot.transform);
        for (int i = 0; i < TickCount; i++) ticks[i] = Line("눈금", dialTickColor, dialTickWidth, dialRoot.transform);
        for (int i = 0; i < TickLabelCount; i++)
        {
            int deg = i * 30;
            tickLabels[i] = Label((deg <= 180 ? deg : 360 - deg).ToString(), textSize * 0.8f, dialLabelColor, dialRoot.transform);
        }

        // ★바늘(2026-09-21) — 각도기 중심에서 뻗은 지침. 끝의 손잡이를 잡아 그 단면 안에서만 돌린다.
        //   ★넷을 <b>여기서 미리 다 만든다</b>(런타임 생성 금지). 안 쓰는 것은 SetNeedle(i, false, ...)로 꺼 둔다.
        //   짝수 = 능동색 · 홀수 = 압박색. 좌우는 바늘 방향으로 갈리므로 색을 더 나누지 않는다.
        for (int i = 0; i < NeedleCount; i++)
        {
            bool act = i % 2 == 0;
            Color c = act ? ActiveColor : PassiveColor;
            // 이름은 Build에서 한 번만 만든다(매 프레임 문자열 결합 금지 — 코드 컨벤션).
            needles[i] = Line(act ? "바늘(능동)" + i : "바늘(압박)" + i, c, needleWidth);
            needleGrips[i] = Dot("바늘 손잡이" + i, c, needleGripSize);
            needleLabels[i] = Label("", textSize * 1.4f, c);
        }

        for (int i = 0; i < MaxMarks; i++)
        {
            markDots[i] = Dot("마커", ActiveColor, 0.016f);
            markLines[i] = Line("마커선", ActiveColor, markLineWidth);
            markLabels[i] = Label("", textSize, ActiveColor);
        }

        panel = Label("", panelSize, Color.white);
        panel.alignment = TextAlignmentOptions.TopLeft;
        panel.rectTransform.sizeDelta = new Vector2(panelSize * 16f, panelSize * 12f);

        SetLive(false, Vector3.zero);
        for (int i = 0; i < NeedleCount; i++) SetNeedle(i, false, Vector3.zero, Vector3.up, 0.15f, 0.2f, null, false);
        SetDial(false, Vector3.zero, Vector3.up, Vector3.forward, 0.15f);
        for (int i = 0; i < MaxMarks; i++) SetMark(i, false, Vector3.zero, Vector3.zero, null, ActiveColor);
    }

    // ── 3축 · 기준축 ──────────────────────────────────────────────────
    public void SetFrame(Vector3 pivot, float yawDeg, float len)
    {
        Vector3 u = RomRecordGeometry.Up, f = RomRecordGeometry.Forward(yawDeg), r = RomRecordGeometry.Right(yawDeg);
        Seg(axisUp, pivot - u * len, pivot + u * len);
        Seg(axisFwd, pivot - f * len, pivot + f * len);
        Seg(axisRight, pivot - r * len, pivot + r * len);
        Seg(pivotAxis, pivot - u * (len * 1.6f), pivot + u * (len * 1.6f));
        pivotDot.position = pivot;

        labUp.transform.position = pivot + u * (len + 0.02f);
        labDown.transform.position = pivot - u * (len + 0.02f);
        labFwd.transform.position = pivot + f * (len + 0.03f);
        labBack.transform.position = pivot - f * (len + 0.03f);
        labRight.transform.position = pivot + r * (len + 0.04f);
        labLeft.transform.position = pivot - r * (len + 0.04f);
    }

    public void SetLandmarks(bool hasC7, Vector3 c7, bool hasGlab, Vector3 glab, Vector3 pivot)
    {
        Show(c7Dot, hasC7);
        if (hasC7) c7Dot.position = c7;
        Show(glabDot, hasGlab);
        Show(neutralLine, hasGlab);
        if (hasGlab)
        {
            glabDot.position = glab;
            Seg(neutralLine, pivot, glab);
        }
    }

    // ── 각도기 ───────────────────────────────────────────────────────
    /// <summary>
    /// normal 단면에 zeroDir을 0으로 하는 눈금판을 그린다. 눈금 숫자는 0~180 양쪽 대칭(부호 없음).
    /// ★단면이 <b>수평</b>이면(= 회전 단계, 법선이 연직) 원통 벽으로 그린다 — 수평 원판은 보는 높이에 따라
    ///   납작해져 안 보인다(09-21 사용자 지시). 굴곡·신전·측굴은 종전대로 평면 눈금판이다.
    ///   ★모양만 바꾸는 것이다. 각은 여전히 <see cref="RomRecordGeometry.PlaneAngle"/>이 단면에 투영해 낸다.
    /// </summary>
    public void SetDial(bool on, Vector3 center, Vector3 normal, Vector3 zeroDir, float radius)
        => SetDial(on, center, normal, zeroDir, radius,
                   cylinderOnHorizontalDial && IsHorizontalPlane(normal));

    /// <summary>원통이냐 평면이냐를 부르는 쪽이 직접 정하는 판(자동 판별을 안 쓰고 싶을 때).</summary>
    public void SetDial(bool on, Vector3 center, Vector3 normal, Vector3 zeroDir, float radius, bool cylinder)
    {
        if (dialRoot.activeSelf != on) dialRoot.SetActive(on);
        if (!on) return;

        // ★바늘 글자를 선에서 비켜 놓으려면 단면 법선이 필요한데 SetNeedle에는 안 들어온다 —
        //   여기서 기억해 둔다. 끄는 동안 단면은 안 바뀌므로 드래그 중에도 이 값이 맞다.
        dialNormal = normal.sqrMagnitude > 1e-8f ? normal.normalized : Vector3.up;

        Vector3 zp = Vector3.ProjectOnPlane(zeroDir, dialNormal);
        if (zp.sqrMagnitude < 1e-8f) zp = Vector3.ProjectOnPlane(Vector3.forward, dialNormal);
        Vector3 z = zp.normalized;
        Vector3 o = Vector3.Cross(dialNormal, z).normalized;

        ApplyDialStyle();
        if (cylinder) DrawCylinderDial(center, z, o, radius);
        else DrawFlatDial(center, z, o, radius);
    }

    /// <summary>법선이 연직에 가까우면 그 단면은 수평이다(= 회전). 굴곡·신전·측굴의 법선은 수평이라 안 걸린다.</summary>
    private static bool IsHorizontalPlane(Vector3 normal)
    {
        if (normal.sqrMagnitude < 1e-8f) return false;
        return Mathf.Abs(Vector3.Dot(normal.normalized, Vector3.up)) > 0.9f;
    }

    /// <summary>색·굵기를 다시 입힌다. 값이 바뀔 때만 불리는 경로라 매 프레임 부담이 아니다(할당 없음).</summary>
    private void ApplyDialStyle()
    {
        ring.startColor = ring.endColor = dialColor;
        ring.widthMultiplier = dialLineWidth;
        ringLower.startColor = ringLower.endColor = dialColor;
        ringLower.widthMultiplier = dialLineWidth;
        zeroLine.startColor = zeroLine.endColor = dialZeroColor;
        zeroLine.widthMultiplier = dialZeroWidth;
        zeroRadial.startColor = zeroRadial.endColor = dialZeroColor;
        zeroRadial.widthMultiplier = dialZeroWidth;
        for (int i = 0; i < TickCount; i++)
        {
            ticks[i].startColor = ticks[i].endColor = dialTickColor;
            ticks[i].widthMultiplier = dialTickWidth;
        }
        for (int i = 0; i < TickLabelCount; i++) tickLabels[i].color = dialLabelColor;
    }

    /// <summary>평면 눈금판(굴곡·신전·측굴) — 종전 모양. 숫자만 원에서 더 밖으로 뺐다.</summary>
    private void DrawFlatDial(Vector3 center, Vector3 z, Vector3 o, float radius)
    {
        Show(ringLower, false);
        Show(zeroRadial, false);
        for (int k = 0; k <= RingSegments; k++)
        {
            float a = k * (2f * Mathf.PI / RingSegments);
            ring.SetPosition(k, center + (z * Mathf.Cos(a) + o * Mathf.Sin(a)) * radius);
        }
        for (int i = 0; i < TickCount; i++)
        {
            float a = i * 10f * Mathf.Deg2Rad;
            Vector3 d = z * Mathf.Cos(a) + o * Mathf.Sin(a);
            float inner = i % 3 == 0 ? 0.86f : 0.93f;
            Seg(ticks[i], center + d * (radius * inner), center + d * radius);
        }
        for (int i = 0; i < TickLabelCount; i++)
        {
            float a = i * 30f * Mathf.Deg2Rad;
            tickLabels[i].transform.position = center + (z * Mathf.Cos(a) + o * Mathf.Sin(a)) * (radius * tickLabelOut);
        }
        Seg(zeroLine, center, center + z * radius);
    }

    /// <summary>
    /// 원통 벽(회전) — 환자 머리 둘레를 감싸는 벽처럼 세운다. 어느 높이에서 봐도 세로선이 보인다.
    /// ★세로는 <b>월드 연직</b> 그대로 세운다(기저를 외적으로 엮지 않는다 — 규칙 9).
    /// ★LineRenderer는 Build에서 만들어 둔 것만 쓴다: ring=위 테두리 · ringLower=아래 테두리 ·
    ///   ticks[36]=세로선(10°마다, 30°마다 꽉) · zeroLine=0° 기둥 · zeroRadial=중심에서 벽까지.
    /// </summary>
    private void DrawCylinderDial(Vector3 center, Vector3 z, Vector3 o, float radius)
    {
        Show(ringLower, true);
        Show(zeroRadial, true);
        Vector3 up = Vector3.up;
        float half = cylinderHeight * 0.5f;
        Vector3 top = up * half, bot = up * -half;
        // ★벽 반지름만 키운다(09-21). 눈금 숫자·세로선·0° 줄이 전부 이 값을 따라가야 벽에 붙어 있는다.
        //   각 계산에는 안 쓰인다 — 여기 들어온 radius는 그리기용으로만 쓰인다.
        float wall = radius * Mathf.Max(0.1f, cylinderRadiusScale);

        for (int k = 0; k <= RingSegments; k++)
        {
            float a = k * (2f * Mathf.PI / RingSegments);
            Vector3 p = center + (z * Mathf.Cos(a) + o * Mathf.Sin(a)) * wall;
            ring.SetPosition(k, p + top);
            ringLower.SetPosition(k, p + bot);
        }
        for (int i = 0; i < TickCount; i++)
        {
            float a = i * 10f * Mathf.Deg2Rad;
            Vector3 p = center + (z * Mathf.Cos(a) + o * Mathf.Sin(a)) * wall;
            float h = (i % 3 == 0 ? half : half * cylinderMinorHeightRatio);
            Seg(ticks[i], p - up * h, p + up * h);
        }
        for (int i = 0; i < TickLabelCount; i++)
        {
            float a = i * 30f * Mathf.Deg2Rad;
            Vector3 p = center + (z * Mathf.Cos(a) + o * Mathf.Sin(a)) * wall;
            tickLabels[i].transform.position = p + up * (half + cylinderLabelRise);
        }
        // 0°는 벽에 세운 기둥 하나로 또렷하게, 중심에서 벽까지 한 줄을 더 그어 어느 쪽이 0인지 보이게 한다.
        Vector3 zp = center + z * wall;
        Seg(zeroLine, zp + bot, zp + top);
        Seg(zeroRadial, center, zp);
    }

    // ── 동작 마커 ────────────────────────────────────────────────────
    public void SetMark(int i, bool on, Vector3 pos, Vector3 pivot, string label, Color c)
    {
        if (i < 0 || i >= MaxMarks) return;
        Show(markDots[i], on);
        Show(markLines[i], on);
        if (markLabels[i].gameObject.activeSelf != on) markLabels[i].gameObject.SetActive(on);
        if (!on) return;

        markDots[i].position = pos;
        markDots[i].GetComponent<Renderer>().material.color = c;
        markLines[i].startColor = markLines[i].endColor = c;
        Seg(markLines[i], pivot, pos);
        markLabels[i].color = c;
        if (label != null) markLabels[i].text = label;
        markLabels[i].transform.position = pos + Vector3.up * 0.03f;
    }

    public void SetLive(bool on, Vector3 pos)
    {
        Show(liveDot, on);
        if (on) liveDot.position = pos;
    }

    /// <summary>
    /// 바늘(2026-09-21). 각도기 중심에서 <paramref name="dir"/> 쪽으로 뻗고, 끝에 잡는 손잡이가 달린다.
    /// ★손잡이는 <b>바늘보다 조금 더 밖</b>에 둔다 — 눈금과 겹치면 잡으려다 눈금을 가린다.
    /// </summary>
    public void SetNeedle(int i, bool on, Vector3 center, Vector3 dir, float dialRadius, float handleRadius,
                          string label, bool held)
        => SetNeedle(i, on, center, dir, dialNormal, dialRadius, handleRadius, label, held);

    /// <summary>단면 법선을 직접 주는 판. 기억해 둔 법선(<see cref="SetDial"/>)을 안 믿고 싶을 때 쓴다.</summary>
    public void SetNeedle(int i, bool on, Vector3 center, Vector3 dir, Vector3 normal, float dialRadius,
                          float handleRadius, string label, bool held)
    {
        if (i < 0 || i >= NeedleCount) return;
        Show(needles[i], on);
        Show(needleGrips[i], on);
        if (needleLabels[i].gameObject.activeSelf != on) needleLabels[i].gameObject.SetActive(on);
        if (!on) return;

        Color baseC = i % 2 == 0 ? ActiveColor : PassiveColor;   // 짝수 능동 · 홀수 압박(좌우는 방향이 가른다)
        Color c = held ? NeedleHeldColor : baseC;
        // ★손잡이는 눈금판보다 <b>더 밖</b>에 둔다. 눈금 반지름에 두면 실제 사람 머리 안에 묻혀
        //   잡을 수가 없다(09-21 사용자 지적 — 내 설계 오류였다).
        Vector3 tip = center + dir * handleRadius;
        Seg(needles[i], center, tip);
        needles[i].startColor = needles[i].endColor = c;
        needles[i].widthMultiplier = needleWidth;
        needleGrips[i].position = tip;
        needleGrips[i].GetComponent<Renderer>().material.color = c;
        needleGrips[i].localScale = Vector3.one * (held ? needleGripHeldSize : needleGripSize);
        needleLabels[i].color = c;
        if (label != null) needleLabels[i].text = label;
        // ★글자를 바늘 선 위에 그대로 얹으면 선과 겹쳐 둘 다 안 읽힌다(09-21 사용자 지적).
        //   단면 안에서 바늘에 <b>수직</b>으로 비킨다. 넷이 서로 안 겹치게 두 손잡이를 같이 쓴다:
        //   ① 수직 비킴 — 능동(짝수) +쪽 · 압박(홀수) −쪽. 한 쌍(좌 또는 우) 안에서 갈라 준다.
        //   ② 반지름 어긋냄 — 쌍 번호(i/2)만큼 밖으로 더 민다. 좌우 바늘이 <b>둘 다 0°에 가까우면</b>
        //      각으로는 안 갈라지므로 ①만으로는 0과 2, 1과 3이 겹친다. 그때를 ②가 막는다.
        Vector3 side = Vector3.Cross(normal, dir);
        if (side.sqrMagnitude < 1e-8f) side = Vector3.Cross(Vector3.up, dir);   // 법선∥바늘인 퇴화 상황 대비
        if (side.sqrMagnitude < 1e-8f) side = Vector3.right;
        side = side.normalized * (i % 2 == 0 ? 1f : -1f);
        float along = needleLabelAlong + (i / 2) * needleLabelAlongStep;        // i/2는 정수 나눗셈(쌍 번호 0·1)
        needleLabels[i].transform.position =
            center + dir * (dialRadius * along) + side * (dialRadius * needleLabelSide);
    }

    public void SetPanel(Vector3 pos, string text)
    {
        panel.transform.position = pos;
        if (text != null) panel.text = text;
    }

    public void SetAxesVisible(bool on)
    {
        Show(axisUp, on); Show(axisFwd, on); Show(axisRight, on); Show(pivotAxis, on); Show(pivotDot, on);
        foreach (var t in new[] { labUp, labDown, labFwd, labBack, labRight, labLeft })
            if (t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
    }

    /// <summary>글자를 눈 쪽으로 돌린다. 매 프레임 부른다 — 할당은 없다.</summary>
    public void FaceCamera(Transform eye)
    {
        if (eye == null) return;
        for (int i = 0; i < facing.Count; i++)
        {
            var t = facing[i];
            if (!t.gameObject.activeInHierarchy) continue;
            t.transform.rotation = Quaternion.LookRotation(t.transform.position - eye.position, Vector3.up);
        }
    }

    // ── 만드는 도구 ──────────────────────────────────────────────────
    private LineRenderer Line(string name, Color c, float width, Transform parent = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent != null ? parent : root, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.useWorldSpace = true;
        lr.widthMultiplier = width;
        lr.sharedMaterial = lineMat;
        lr.startColor = lr.endColor = c;
        lr.numCapVertices = 2;
        return lr;
    }

    private TextMeshPro Label(string text, float size, Color c, Transform parent = null)
    {
        var go = new GameObject("글자 " + text);
        go.transform.SetParent(parent != null ? parent : root, false);
        var t = go.AddComponent<TextMeshPro>();
        if (font != null) t.font = font;
        t.text = text;
        t.fontSize = size;
        t.color = c;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.rectTransform.sizeDelta = new Vector2(size * 10f, size * 2f);
        // ★패스스루(실제 방) 위에서 읽혀야 한다 — 밝은 벽에서도 보이게 외곽선을 준다.
        t.outlineWidth = 0.25f;
        t.outlineColor = new Color32(0, 0, 0, 220);
        facing.Add(t);
        return t;
    }

    private Transform Dot(string name, Color c, float size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(root, false);
        go.transform.localScale = Vector3.one * size;
        var r = go.GetComponent<Renderer>();
        if (lineMat != null) r.sharedMaterial = lineMat;
        r.material.color = c;
        return go.transform;
    }

    private static void Seg(LineRenderer lr, Vector3 a, Vector3 b)
    {
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);
    }

    private static void Show(Component c, bool on)
    {
        if (c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
    }
}
