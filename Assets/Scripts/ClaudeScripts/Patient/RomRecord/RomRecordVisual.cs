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
    // ★★눈금을 1°마다 긋는다(09-21 사용자 지시). 360개를 LineRenderer로 두면 드로콜이 그만큼 늘어난다 —
    //   <b>선분 메시 하나</b>로 굽는다(정점 720). 굽는 것은 SetDial 때뿐이고 매 프레임 하는 일은 없다.
    private const int TickCount = 360;     // 1°마다
    private const int TickLabelCount = 12; // 30°마다 숫자
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
    // ★손잡이 크기는 <b>세 상태</b>에서 온다(잡는 중 · 잡히는 자리 · 잠김). SetNeedle과 SetGrabHover가
    //   서로 다른 때에 불려서, 마지막에 부른 쪽이 크기를 덮어쓰면 하이라이트가 조용히 사라진다.
    //   → 상태를 기억해 두고 <see cref="NeedleGripSizeOf"/> 한 곳에서만 크기를 낸다.
    private readonly LineRenderer[] needleFinEdges = new LineRenderer[NeedleCount];   // 면의 테두리(사각형 네 변)
    private readonly bool[] needleHeld = new bool[NeedleCount];
    private readonly bool[] needleFinShown = new bool[NeedleCount];   // 지금 면으로 그렸나(하이라이트가 알파를 지켜야 한다)
    private readonly bool[] needleLockedShown = new bool[NeedleCount];
    private TextMeshPro labUp, labDown, labFwd, labBack, labRight, labLeft, panel;
    private Transform c7Dot, glabDot, liveDot, pivotDot, earDot;
    // ★눈금은 선분 메시 하나다(LineRenderer 배열이 아니다). 정점·인덱스는 미리 잡아 재활용한다.
    private Mesh tickMesh;
    private Renderer tickRenderer;
    private Vector3[] tickVerts;
    private bool cylinderLabelsActive;   // 지금 원통 모드인가(뒤쪽 숫자를 가릴지 판단)
    private Vector3 dialCenterForLabels; // 원통 중심 — 숫자가 앞쪽인지 재는 기준
    private readonly TextMeshPro[] tickLabels = new TextMeshPro[TickLabelCount];
    private readonly Transform[] markDots = new Transform[MaxMarks];
    private readonly LineRenderer[] markLines = new LineRenderer[MaxMarks];
    private readonly TextMeshPro[] markLabels = new TextMeshPro[MaxMarks];
    // ★삼각면(2026-09-21 사용자 지시) — 찍은 점에서 단면으로 <b>수직으로 내린 선</b>과,
    //   중심에서 그 발까지의 선. 둘을 더하면 (중심 · 발 · 점)이 삼각형이 된다.
    //   ★이건 장식이 아니다. 각은 <b>단면에 투영해서</b> 재므로, 점이 단면에서 벗어난 만큼이
    //   그대로 «재지 못한 성분»이다. 그 벗어남을 눈에 보이게 하는 것이 이 삼각형이다.
    private readonly LineRenderer[] markPerp = new LineRenderer[MaxMarks];   // 점 → 단면 위의 발
    private readonly LineRenderer[] markBase = new LineRenderer[MaxMarks];   // 중심 → 발
    private readonly TextMeshPro[] markOffLabels = new TextMeshPro[MaxMarks];
    // ★단면 사각형 — 3축이 이루는 면을 사람 크기만큼 네모로 보여 준다(테두리 + 옅은 격자).
    private LineRenderer sectionRect;
    private readonly LineRenderer[] sectionGrid = new LineRenderer[4];
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
    // ── 눈금(2026-09-21 사용자 지시: 1° 단위로 채우고 숫자를 키운다) ──
    [Tooltip("1°·5°·10°·30° 눈금 길이(반지름 배수). 30°가 가장 길어 눈이 단위를 읽는 사다리가 된다.")]
    public float tickLen1 = 0.025f;
    public float tickLen5 = 0.05f;
    public float tickLen10 = 0.09f;
    public float tickLen30 = 0.14f;
    [Tooltip("눈금 숫자 크기 = textSize × 이 값. ★09-21에 0.8에서 키웠다.")]
    public float tickLabelScale = 1.3f;
    [Tooltip("원통에서 <b>카메라 반대편</b> 숫자를 숨긴다 — 앞뒤가 겹쳐 읽을 수가 없었다(09-21).")]
    public bool hideFarCylinderLabels = true;

    public float tickLabelOut = 1.20f;      // 눈금 숫자를 원 밖으로 미는 배수(옛 1.12 — 원에 붙어 있었다)
    // ★면 안은 <b>색이 거의 없다</b>(2026-09-23 사용자 정정: "단면 내부 색 거의 없이 반투명하게").
    //   면이 어디 있는지만 알면 되고, 모양과 각은 <b>테두리</b>가 말한다 — 채움이 진하면 환자가 가린다.
    //   0.05는 기기 미검증 추정이다(처음 0.16은 아직 진했다).
    public float finFillAlpha = 0.05f;
    public float needleLabelAlong = 0.55f;  // 바늘 방향으로 나가는 거리(반지름 배수)
    public float needleLabelSide = 0.34f;   // ★바늘에 <b>수직</b>으로 비키는 거리(반지름 배수). 09-21에 0.24에서 넓혔다.
                                            //   능동(짝수)은 +쪽, 압박(홀수)은 -쪽으로 갈라 쌍끼리 안 겹친다.
    // ★09-21 녹화 실측: 0.22로는 네 이름표가 위아래로 거의 붙어 못 읽었다 — 넓힌다(여전히 기기 미검증).
    // ── 단면 사각형(2026-09-21) ──────────────────────────────────────
    [Tooltip("단면 사각형의 가로 반폭(m). 사람 어깨~몸통 범위를 덮게 잡는다.")]
    public float sectionHalfWidth = 0.35f;
    [Tooltip("단면 사각형의 세로 반높이(m).")]
    public float sectionHalfHeight = 0.45f;
    public float sectionLineWidth = 0.002f;
    [Tooltip("단면에서 이만큼(m) 넘게 벗어나면 벗어난 양을 숫자로 적는다. ★각은 단면에 투영해 재므로 그만큼이 «못 잰 성분»이다.")]
    public float offPlaneWarnMeters = 0.02f;

    public float needleLabelAlongStep = 0.42f; // ★좌우 쌍(0·1 / 2·3)을 <b>반지름으로도</b> 어긋나게 하는 양.
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
    // ★09-21 사용자: "회전 각도기 원통 사이즈 좀 키워 줘" — 1.6에서 올렸다(여전히 기기에서 눈으로 맞출 값).
    public float cylinderRadiusScale = 2.2f;

    public static readonly Color UpColor = new Color(0.35f, 0.95f, 0.45f);
    public static readonly Color FwdColor = new Color(0.35f, 0.65f, 1f);
    public static readonly Color RightColor = new Color(1f, 0.4f, 0.4f);
    public static readonly Color PivotColor = new Color(1f, 0.85f, 0.2f);
    public static readonly Color EarColor = new Color(0.45f, 1f, 0.6f);   // 외이도 — 대추(하늘)·미간(흰)과 겹치지 않게 연두
    // ★능동·압박은 <b>같은 주황 계열의 밝기·채도</b>로 가른다(2026-09-22 사용자: "각도 색상 밝기나 채도로").
    //   능동 = 연하게(밝고 채도 낮게), 압박 = 진하게. 압박이 더 많이 간 값이라 «더 진함»으로 읽힌다.
    //   종전엔 주황·자홍 두 색이었다. 바늘·마커 글자에서 한글(능동·압박)을 빼서 색이 유일한 구분이다.
    //   ★판의 탭 아래 숫자도 같은 색이다(RomRecordSession.HexActive/HexPassive) — 바꾸면 둘 다 바꾼다.
    // ★2026-09-23 정정: 종전 능동색(1, 0.82, 0.60)은 <b>채도가 0.4로 떨어져</b> 다른 색(살구)으로 보였다.
    //   사용자: "같은 색상에서 명도를 조절하라는 거였는데." → 색상 H=24.5°·채도 S=0.98을 <b>고정</b>하고
    //   명도 V만 1.00 ↔ 0.62로 가른다. 압박이 어두운 쪽이다(더 많이 간 값 = 더 진하다).
    public static readonly Color ActiveColor = new Color(1f, 0.42f, 0.02f);        // #FF6B05 · V=1.00
    public static readonly Color PassiveColor = new Color(0.62f, 0.26f, 0.012f);   // #9E4203 · V=0.62
    // ★단면 사각형·삼각면(2026-09-21). 표시물이지 판독값이 아니라 눈에 덜 띄게 둔다.
    public static readonly Color SectionColor = new Color(0.45f, 0.55f, 0.70f, 0.45f);
    public static readonly Color SectionGridColor = new Color(0.45f, 0.55f, 0.70f, 0.22f);
    public static readonly Color OffPlaneColor = new Color(1f, 0.85f, 0.35f, 0.85f);   // 단면에서 벗어난 양 — 노랑 경고 계열
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
        earDot = Dot("외이도", EarColor, 0.016f);   // ★09-22 — 찍은 자리(실제 축은 정중면으로 옮긴 점 — 노란 기준점)
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
        BuildTickMesh();
        for (int i = 0; i < TickLabelCount; i++)
        {
            int deg = i * 30;
            // ★09-21에 0.8배에서 키웠다(사용자: "각도기 숫자 좀 키워 주고"). ApplyDialStyle이 매번 다시 입힌다.
            tickLabels[i] = Label((deg <= 180 ? deg : 360 - deg).ToString(), textSize * tickLabelScale, dialLabelColor, dialRoot.transform);
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
            needleFinEdges[i] = Line(act ? "바늘 면 테두리(능동)" + i : "바늘 면 테두리(압박)" + i, c, needleWidth);
            needleFinEdges[i].positionCount = 5;   // 사각형 네 귀퉁이 + 처음으로 돌아오기
            needleGrips[i] = Dot("바늘 손잡이" + i, c, needleGripSize);
            needleLabels[i] = Label("", textSize * 1.4f, c);
        }

        for (int i = 0; i < MaxMarks; i++)
        {
            markDots[i] = Dot("마커", ActiveColor, 0.016f);
            markLines[i] = Line("마커선", ActiveColor, markLineWidth);
            markLabels[i] = Label("", textSize, ActiveColor);
            markPerp[i] = Line("단면까지 수직", OffPlaneColor, markLineWidth);
            markBase[i] = Line("단면 위 밑변", OffPlaneColor, markLineWidth);
            markOffLabels[i] = Label("", textSize * 0.7f, OffPlaneColor);
        }

        // ★단면 사각형 — 테두리 하나와 가로·세로 가운데 줄 둘(면이 어디 있는지만 알면 된다).
        //   면을 통째로 칠하면 패스스루가 가려져 오히려 방해가 된다.
        sectionRect = Line("단면 테두리", SectionColor, sectionLineWidth);
        sectionRect.positionCount = 5;                       // 네 귀퉁이 + 닫기
        for (int i = 0; i < sectionGrid.Length; i++)
            sectionGrid[i] = Line("단면 눈금줄", SectionGridColor, sectionLineWidth * 0.7f);

        panel = Label("", panelSize, Color.white);
        panel.alignment = TextAlignmentOptions.TopLeft;
        panel.rectTransform.sizeDelta = new Vector2(panelSize * 16f, panelSize * 12f);
        // ★09-22부터 안내판을 안 쓴다(측정값은 조작 판 탭 아래로). 꺼 둔다 — 켜 두면 월드 원점에 남아
        //   추적 전(눈도 원점)에 FaceCamera가 영벡터를 돌려 «viewing vector is zero»를 매 프레임 34만 번 찍었다(09-22 로그).
        panel.gameObject.SetActive(false);

        SetLive(false, Vector3.zero);
        for (int i = 0; i < NeedleCount; i++) SetNeedle(i, false, Vector3.zero, Vector3.up, 0.15f, 0.2f, null, false);
        SetDial(false, Vector3.zero, Vector3.up, Vector3.forward, 0.15f);
        for (int i = 0; i < MaxMarks; i++) SetMark(i, false, Vector3.zero, Vector3.zero, null, ActiveColor);
        SetSectionPlane(false, Vector3.zero, Vector3.up, Vector3.up);
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

    // ── 잡기 하이라이트(2026-09-23 사용자 지시) ──────────────────────
    // ★★"모든 오브젝트는 그랩되기 전에 «지금 잡으면 잡힌다»는 하이라이트가 필수다."
    //   ★판정은 여기서 새로 하지 않는다 — Session이 <b>실제로 잡을 때 쓰는 FindDragTarget</b>을 그대로
    //   태워 번호만 넘겨 준다(규칙 9 — 따로 계산하면 하이라이트가 거짓말을 한다).
    //   번호 약속은 Session.FindDragTarget과 같다: 0 3축 · 1 대추 · 2 미간 · 3~6 바늘 · 8 외이도 · 9 판 · -1 없음.
    public static readonly Color GrabHoverColor = new Color(1f, 1f, 0.55f);
    public float grabHoverScale = 1.7f;

    private int hoverTarget = -1;

    /// <summary>지금 손을 오므리면 잡힐 것을 밝힌다. ★값이 <b>바뀔 때만</b> 손댄다(매 프레임 불린다).</summary>
    public void SetGrabHover(int target)
    {
        if (target == hoverTarget) return;
        int old = hoverTarget;
        hoverTarget = target;     // ★먼저 바꾼다 — ApplyHover가 이 값을 보고 색을 고른다
        ApplyHover(old);
        ApplyHover(target);
    }

    private void ApplyHover(int t)
    {
        if (t < 0) return;
        bool on = t == hoverTarget;
        if (t >= 3 && t <= 6)
        {
            int i = t - 3;
            if (needleGrips[i] == null) return;
            needleGrips[i].localScale = Vector3.one * NeedleGripSizeOf(i);
            Color c = needleHeld[i] ? NeedleHeldColor
                    : on ? GrabHoverColor
                    : i % 2 == 0 ? ActiveColor : PassiveColor;
            needleGrips[i].GetComponent<Renderer>().material.color = c;
            // ★★면으로 그린 바늘은 <b>채움 알파를 지키면서</b> 색만 바꾼다(2026-09-23 사용자:
            //   "하이라이트 할 때 단면이 불투명하게 꽉 차 버려서"). 종전엔 여기서 불투명한 하이라이트 색을
            //   그대로 덮어써, 손을 가져가는 순간 면이 꽉 찼다 — <b>SetNeedle이 준 알파를 잃은 것</b>이다.
            //   밝아지는 것은 <b>테두리와 손잡이</b>가 맡는다. 그것만으로 "잡힌다"가 읽힌다.
            if (needleFinShown[i])
            {
                Color fill = c;
                fill.a = finFillAlpha;
                needles[i].startColor = needles[i].endColor = fill;
                needleFinEdges[i].startColor = needleFinEdges[i].endColor = c;
            }
            else
            {
                needles[i].startColor = needles[i].endColor = c;
            }
            return;
        }
        Transform dot = t == 0 ? pivotDot : t == 1 ? c7Dot : t == 2 ? glabDot : t == 8 ? earDot : null;
        if (dot == null) return;                      // 9(판)는 판 자신이 테두리로 말한다 — Session이 부른다
        float baseSize = t == 0 ? 0.012f : 0.016f;    // Build에서 만든 크기와 같다
        dot.localScale = Vector3.one * (on ? baseSize * grabHoverScale : baseSize);
        dot.GetComponent<Renderer>().material.color =
            on ? GrabHoverColor
            : t == 0 ? PivotColor
            : t == 1 ? new Color(0.3f, 0.9f, 1f)
            : t == 2 ? Color.white
            : EarColor;
    }

    private float NeedleGripSizeOf(int i)
        => needleHeld[i] ? needleGripHeldSize
         : hoverTarget == 3 + i ? needleGripSize * grabHoverScale
         : needleLockedShown[i] ? needleGripSize * 0.55f    // 잠긴 바늘은 작게 — 더 안 잡힌다는 표시다
         : needleGripSize;

    public void SetLandmarks(bool hasC7, Vector3 c7, bool hasEar, Vector3 ear, bool hasGlab, Vector3 glab, Vector3 pivot)
    {
        Show(c7Dot, hasC7);
        if (hasC7) c7Dot.position = c7;
        Show(earDot, hasEar);
        if (hasEar) earDot.position = ear;
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
        if (tickRenderer != null) tickRenderer.material.color = dialTickColor;
        for (int i = 0; i < TickLabelCount; i++)
        {
            tickLabels[i].color = dialLabelColor;
            tickLabels[i].fontSize = textSize * tickLabelScale;
        }
    }

    /// <summary>평면 눈금판(굴곡·신전·측굴) — 종전 모양. 숫자만 원에서 더 밖으로 뺐다.</summary>
    /// <summary>
    /// 그 각도의 눈금 길이(반지름 배수). 1°는 아주 짧고 30°가 가장 길다 — 눈이 단위를 읽는 사다리다.
    /// </summary>
    private float TickLengthOf(int deg)
        => deg % 30 == 0 ? tickLen30
         : deg % 10 == 0 ? tickLen10
         : deg % 5 == 0 ? tickLen5
         : tickLen1;

    /// <summary>눈금 선분 메시를 만든다(Build에서 한 번). 정점은 재활용하고 인덱스는 고정이다.</summary>
    private void BuildTickMesh()
    {
        var go = new GameObject("눈금");
        go.transform.SetParent(dialRoot.transform, false);
        tickMesh = new Mesh { name = "눈금선분" };
        tickMesh.MarkDynamic();
        tickVerts = new Vector3[TickCount * 2];
        var idx = new int[TickCount * 2];
        for (int i = 0; i < idx.Length; i++) idx[i] = i;
        tickMesh.vertices = tickVerts;
        tickMesh.SetIndices(idx, MeshTopology.Lines, 0);   // ★선분 목록 — 삼각형이 아니다
        go.AddComponent<MeshFilter>().sharedMesh = tickMesh;
        tickRenderer = go.AddComponent<MeshRenderer>();
        if (lineMat != null) tickRenderer.sharedMaterial = lineMat;
        tickRenderer.material.color = dialTickColor;
        tickRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tickRenderer.receiveShadows = false;
    }

    /// <summary>바뀐 정점을 메시에 올린다. ★SetDial 때만 부른다 — 매 프레임 하지 않는다.</summary>
    private void UploadTickMesh()
    {
        if (tickMesh == null) return;
        tickMesh.vertices = tickVerts;
        tickMesh.RecalculateBounds();
    }

    private void DrawFlatDial(Vector3 center, Vector3 z, Vector3 o, float radius)
    {
        cylinderLabelsActive = false;
        Show(ringLower, false);
        Show(zeroRadial, false);
        for (int k = 0; k <= RingSegments; k++)
        {
            float a = k * (2f * Mathf.PI / RingSegments);
            ring.SetPosition(k, center + (z * Mathf.Cos(a) + o * Mathf.Sin(a)) * radius);
        }
        // ★1°마다 긋는다. 5°·10°·30°로 길이를 달리해 눈이 단위를 읽을 수 있게 한다.
        for (int i = 0; i < TickCount; i++)
        {
            float a = i * Mathf.Deg2Rad;
            Vector3 d = z * Mathf.Cos(a) + o * Mathf.Sin(a);
            float inner = 1f - TickLengthOf(i);
            tickVerts[i * 2] = center + d * (radius * inner);
            tickVerts[i * 2 + 1] = center + d * radius;
        }
        UploadTickMesh();
        for (int i = 0; i < TickLabelCount; i++)
        {
            float a = i * 30f * Mathf.Deg2Rad;
            tickLabels[i].transform.position = center + (z * Mathf.Cos(a) + o * Mathf.Sin(a)) * (radius * tickLabelOut);
            if (!tickLabels[i].gameObject.activeSelf) tickLabels[i].gameObject.SetActive(true);
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
        // ★1°마다 세로선. 5°·10°·30°로 길이를 달리한다(길이 비율은 평면과 같은 표를 쓴다).
        for (int i = 0; i < TickCount; i++)
        {
            float a = i * Mathf.Deg2Rad;
            Vector3 p = center + (z * Mathf.Cos(a) + o * Mathf.Sin(a)) * wall;
            float h = half * Mathf.Clamp01(TickLengthOf(i) / 0.14f) * cylinderMinorHeightRatio;
            if (i % 30 == 0) h = half;                       // 30°는 위아래 테두리까지 꽉
            tickVerts[i * 2] = p - up * h;
            tickVerts[i * 2 + 1] = p + up * h;
        }
        UploadTickMesh();
        for (int i = 0; i < TickLabelCount; i++)
        {
            float a = i * 30f * Mathf.Deg2Rad;
            Vector3 p = center + (z * Mathf.Cos(a) + o * Mathf.Sin(a)) * wall;
            tickLabels[i].transform.position = p + up * (half + cylinderLabelRise);
        }
        cylinderLabelsActive = true;   // ★뒤쪽 숫자는 FaceCamera에서 가린다(원통은 앞뒤가 겹친다)
        dialCenterForLabels = center;
        // 0°는 벽에 세운 기둥 하나로 또렷하게, 중심에서 벽까지 한 줄을 더 그어 어느 쪽이 0인지 보이게 한다.
        Vector3 zp = center + z * wall;
        Seg(zeroLine, zp + bot, zp + top);
        Seg(zeroRadial, center, zp);
    }

    // ── 동작 마커 ────────────────────────────────────────────────────
    /// <summary>
    /// 마커 하나. <paramref name="planeNormal"/>이 0이 아니면 <b>삼각면</b>도 함께 그린다(2026-09-21).
    ///
    /// ★삼각면이 뜻하는 것: 각은 점을 <b>단면에 투영해서</b> 잰다. 그러니 점이 단면에서 벗어난 만큼은
    ///   <b>재지 못한 성분</b>이다. (중심 · 단면 위의 발 · 실제 점) 세 점을 이으면 그 벗어남이 눈에 보인다.
    ///   밑변이 실제로 각에 쓰인 선이고, 수직선이 버려진 양이다.
    /// </summary>
    public void SetMark(int i, bool on, Vector3 pos, Vector3 pivot, string label, Color c,
                        Vector3 planeCenter = default, Vector3 planeNormal = default)
    {
        if (i < 0 || i >= MaxMarks) return;
        Show(markDots[i], on);
        Show(markLines[i], on);
        if (markLabels[i].gameObject.activeSelf != on) markLabels[i].gameObject.SetActive(on);

        bool tri = on && planeNormal.sqrMagnitude > 1e-6f;
        Show(markPerp[i], tri);
        Show(markBase[i], tri);

        if (!on)
        {
            if (markOffLabels[i].gameObject.activeSelf) markOffLabels[i].gameObject.SetActive(false);
            return;
        }

        markDots[i].position = pos;
        markDots[i].GetComponent<Renderer>().material.color = c;
        markLines[i].startColor = markLines[i].endColor = c;
        Seg(markLines[i], pivot, pos);
        markLabels[i].color = c;
        if (label != null) markLabels[i].text = label;
        markLabels[i].transform.position = pos + Vector3.up * 0.03f;

        if (!tri)
        {
            if (markOffLabels[i].gameObject.activeSelf) markOffLabels[i].gameObject.SetActive(false);
            return;
        }

        // 단면 위로 내린 발 — 점에서 법선 방향 성분을 뺀 자리다.
        Vector3 n = planeNormal.normalized;
        float off = Vector3.Dot(pos - planeCenter, n);
        Vector3 foot = pos - n * off;
        Seg(markPerp[i], pos, foot);       // 버려진 성분(수직)
        Seg(markBase[i], pivot, foot);     // 실제로 각에 쓰인 선(단면 안)

        // ★벗어남이 눈에 띌 만큼일 때만 숫자를 적는다. 늘 적으면 화면이 시끄럽다.
        float mm = Mathf.Abs(off);
        bool warn = mm > offPlaneWarnMeters;
        if (markOffLabels[i].gameObject.activeSelf != warn) markOffLabels[i].gameObject.SetActive(warn);
        if (warn)
        {
            markOffLabels[i].text = (mm * 100f).ToString("F0") + "cm 벗어남";
            markOffLabels[i].transform.position = (pos + foot) * 0.5f;
        }
    }

    /// <summary>
    /// 3축이 이루는 <b>단면 사각형</b>(2026-09-21 사용자 지시). 사람 크기만큼만 네모로 보여 준다.
    /// ★면을 칠하지 않고 테두리와 가운데 줄만 긋는다 — 패스스루를 가리면 오히려 방해가 된다.
    /// </summary>
    public void SetSectionPlane(bool on, Vector3 center, Vector3 normal, Vector3 upHint)
    {
        Show(sectionRect, on);
        for (int i = 0; i < sectionGrid.Length; i++) Show(sectionGrid[i], on);
        if (!on) return;

        // 단면 안의 두 방향을 잡는다. ★upHint를 면에 투영해 세로로 삼는다 — 그래야 사각형이 눕지 않는다.
        Vector3 n = normal.normalized;
        Vector3 up = Vector3.ProjectOnPlane(upHint, n);
        if (up.sqrMagnitude < 1e-6f) up = Vector3.ProjectOnPlane(Vector3.up, n);
        if (up.sqrMagnitude < 1e-6f) up = Vector3.ProjectOnPlane(Vector3.forward, n);
        up.Normalize();
        Vector3 right = Vector3.Cross(n, up).normalized;

        Vector3 hw = right * sectionHalfWidth, hh = up * sectionHalfHeight;
        sectionRect.SetPosition(0, center - hw - hh);
        sectionRect.SetPosition(1, center + hw - hh);
        sectionRect.SetPosition(2, center + hw + hh);
        sectionRect.SetPosition(3, center - hw + hh);
        sectionRect.SetPosition(4, center - hw - hh);

        Seg(sectionGrid[0], center - hw, center + hw);              // 가로 가운데
        Seg(sectionGrid[1], center - hh, center + hh);              // 세로 가운데
        Seg(sectionGrid[2], center - hw + hh * 0.5f, center + hw + hh * 0.5f);
        Seg(sectionGrid[3], center - hw - hh * 0.5f, center + hw - hh * 0.5f);
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
                          string label, bool held, Vector3 fin = default, bool finHang = false, bool locked = false)
        => SetNeedle(i, on, center, dir, dialNormal, dialRadius, handleRadius, label, held, fin, finHang, locked);

    /// <summary>단면 법선을 직접 주는 판. 기억해 둔 법선(<see cref="SetDial"/>)을 안 믿고 싶을 때 쓴다.</summary>
    public void SetNeedle(int i, bool on, Vector3 center, Vector3 dir, Vector3 normal, float dialRadius,
                          float handleRadius, string label, bool held, Vector3 fin = default,
                          bool finHang = false, bool locked = false)
    {
        if (i < 0 || i >= NeedleCount) return;
        Show(needles[i], on);
        Show(needleGrips[i], on);
        if (!on) Show(needleFinEdges[i], false);
        if (needleLabels[i].gameObject.activeSelf != on) needleLabels[i].gameObject.SetActive(on);
        needleHeld[i] = held;
        needleLockedShown[i] = locked;
        needleFinShown[i] = on && fin.sqrMagnitude > 1e-8f;
        if (!on) return;

        Color baseC = i % 2 == 0 ? ActiveColor : PassiveColor;   // 짝수 능동 · 홀수 압박(좌우는 방향이 가른다)
        Color c = held ? NeedleHeldColor : hoverTarget == 3 + i ? GrabHoverColor : baseC;
        // ★손잡이는 눈금판보다 <b>더 밖</b>에 둔다. 눈금 반지름에 두면 실제 사람 머리 안에 묻혀
        //   잡을 수가 없다(09-21 사용자 지적 — 내 설계 오류였다).
        Vector3 tip = center + dir * handleRadius;
        float finLen = fin.magnitude;
        if (finLen > 1e-4f)
        {
            // ★바늘은 <b>사각형 면</b>이다(2026-09-23 사용자 2차 지시).
            //   "크고 넓은 모양 말고 그냥 사각형 · 면 전체가 진하게 차 있어서 환자 얼굴이 안 보여 ·
            //    반투명하게 그리고 테두리만 진하게 · 회전 말고 모든 측정 과정 전부 · 환자 머리 크기 정도만"
            //   → 채움은 선 폭을 눕혀 쓰고(새 메시를 안 만든다) <b>알파만 낮춘다</b>. 테두리는 따로 그린 네 변이다.
            //   ★fin은 <b>면이 뻗는 방향×크기</b>다. 회전은 연직(윗 테두리에서 아래로),
            //     나머지는 그 단면 <b>안</b>에서 바늘에 수직이다(사용자: "측정 단면 안에 눕게").
            Vector3 half = fin * 0.5f;
            Vector3 off = finHang ? -half : Vector3.zero;   // 회전만 한쪽으로 쏠린다(윗 테두리에 건다)
            Vector3 a = center + off, b = tip + off;
            Seg(needles[i], a, b);
            needles[i].alignment = LineAlignment.TransformZ;
            Vector3 fnrm = Vector3.Cross(dir, fin);   // 이 법선이면 면이 fin 쪽으로 눕는다
            if (fnrm.sqrMagnitude > 1e-8f)
                needles[i].transform.rotation = Quaternion.LookRotation(fnrm.normalized, Vector3.up);
            needles[i].widthMultiplier = finLen;

            // 테두리 — 네 귀퉁이를 한 선으로 돈다(진하게).
            var e = needleFinEdges[i];
            Show(e, true);
            e.SetPosition(0, a - half); e.SetPosition(1, b - half);
            e.SetPosition(2, b + half); e.SetPosition(3, a + half); e.SetPosition(4, a - half);
            e.startColor = e.endColor = c;
            e.widthMultiplier = needleWidth;

            Color fill = c;
            fill.a = finFillAlpha;   // ★안은 반투명 — 환자 얼굴이 비쳐야 한다
            needles[i].startColor = needles[i].endColor = fill;
            needleGrips[i].position = tip + off;
            needleGrips[i].GetComponent<Renderer>().material.color = c;
            needleGrips[i].localScale = Vector3.one * NeedleGripSizeOf(i);
            SetNeedleLabel(i, c, label, center + off, dir, dialRadius, normal);
            return;
        }
        Show(needleFinEdges[i], false);
        Seg(needles[i], center, tip);
        needles[i].alignment = LineAlignment.View;
        needles[i].widthMultiplier = needleWidth;
        needles[i].startColor = needles[i].endColor = c;
        needleGrips[i].position = tip;
        needleGrips[i].GetComponent<Renderer>().material.color = c;
        needleGrips[i].localScale = Vector3.one * NeedleGripSizeOf(i);
        SetNeedleLabel(i, c, label, center, dir, dialRadius, normal);
    }

    private void SetNeedleLabel(int i, Color c, string label, Vector3 center, Vector3 dir,
                                float dialRadius, Vector3 normal)
    {
        needleLabels[i].color = c;
        if (label != null) needleLabels[i].text = label;
        // ★글자를 바늘 선 위에 그대로 얹으면 선과 겹쳐 둘 다 안 읽힌다(09-21 사용자 지적).
        //   단면 안에서 바늘에 <b>수직</b>으로 비킨다. 넷이 서로 안 겹치게 두 손잡이를 같이 쓴다:
        //   ① 수직 비킴 — 능동(짝수) +쪽 · 압박(홀수) −쪽. 한 쌍(좌 또는 우) 안에서 갈라 준다.
        //   ② 반지름 어긋냄 — 쌍 번호(i/2)만큼 밖으로 더 민다. 좌우 바늘이 <b>둘 다 0°에 가까우면</b>
        //      각으로는 안 갈라지므로 ①만으로는 0과 2, 1과 3이 겹친다. 그때를 ②가 막는다.
        // ★★비키는 방향은 <b>바늘 방향과 무관</b>해야 한다(2026-09-23 사용자: "측굴 보는데 능동이 밑에 있고
        //   압박이 위로 가 있어서 뒤집어진 것처럼 보여서 헷갈려").
        //   종전엔 Cross(법선, 바늘방향)이라 <b>바늘이 어디를 가리키느냐에 따라 부호가 뒤집혔다</b> —
        //   좌 바늘과 우 바늘에서 능동·압박의 위아래가 서로 반대가 됐다.
        //   → 그 단면 안의 <b>연직</b>으로 고정한다. 능동은 늘 위, 압박은 늘 아래다.
        //   ★회전(수평 단면)은 면 안에 연직이 없다 — 그때는 월드 연직으로 비킨다(면 밖이어도 위아래가 갈린다).
        Vector3 side = Vector3.ProjectOnPlane(Vector3.up, normal);
        if (side.sqrMagnitude < 1e-6f) side = Vector3.up;
        side = side.normalized * (i % 2 == 0 ? 1f : -1f);
        float along = needleLabelAlong + (i / 2) * needleLabelAlongStep;        // i/2는 정수 나눗셈(쌍 번호 0·1)
        needleLabels[i].transform.position =
            center + dir * (dialRadius * along) + side * (dialRadius * needleLabelSide);
    }

    public void SetPanel(Vector3 pos, string text)
    {
        if (!panel.gameObject.activeSelf) panel.gameObject.SetActive(true);
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

        // ★★원통에서는 앞뒤 숫자가 한 줄에 겹쳐 «90 120 30 0 150»처럼 읽을 수가 없었다(09-21 녹화 실측).
        //   <b>카메라 반대편 숫자를 끈다</b> — 어차피 벽 너머라 읽을 일이 없다.
        //   ★매 프레임 도는 자리라 새로 할당하지 않는다(내적 하나와 SetActive뿐이다).
        if (cylinderLabelsActive && hideFarCylinderLabels)
        {
            Vector3 toEye = eye.position - dialCenterForLabels;
            toEye.y = 0f;   // 지움: 위아래 성분. 원통은 세로로 서 있어 «앞뒤»는 수평에서만 갈린다.
            for (int i = 0; i < TickLabelCount; i++)
            {
                var lab = tickLabels[i];
                Vector3 d = lab.transform.position - dialCenterForLabels;
                d.y = 0f;
                bool near = Vector3.Dot(d, toEye) > 0f;   // 카메라와 같은 쪽이면 보인다
                if (lab.gameObject.activeSelf != near) lab.gameObject.SetActive(near);
            }
        }

        for (int i = 0; i < facing.Count; i++)
        {
            var t = facing[i];
            if (!t.gameObject.activeInHierarchy) continue;
            Vector3 away = t.transform.position - eye.position;
            if (away.sqrMagnitude < 1e-8f) continue;   // ★눈과 같은 자리면 돌릴 방향이 없다(추적 전 원점 — 09-22)
            t.transform.rotation = Quaternion.LookRotation(away, Vector3.up);
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
