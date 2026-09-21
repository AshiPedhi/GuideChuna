using System.Collections.Generic;
using System.Text;
using Oculus.Interaction.Input;
using TMPro;
using UnityEngine;

/// <summary>
/// ★경추 ROM 실측 기록 프로그램 — 본체(2026-09-18 신설, 실측 전용 씬 <c>RomMarkerScene</c>에서 돈다).
///
/// 설계(2026-09-18 사용자와 확정 — 메모리 rom-marker-lab)
///   · <b>패스스루 전용</b>. 실제 사람 대상. 센서가 재는 도구가 아니라 <b>진단 기록 보조 + 3축 시각화</b>.
///   · 시작하면 지면 연직·수평 3축이 뜬다 → Y 회전·높이 조절 또는 대추 지정으로 자리를 잡고 → <b>환자를 축에 정렬</b>시킨다.
///   · 대추·미간(중립) 지정. 기준축은 목 둘레 중앙으로 앞뒤 보정할 수 있다(미간은 그대로).
///   · 4단계: 굴곡·신전(1회째 능동, 2회째 수동), 측굴·회전(4회, 좌우 자동, 홀수 능동·짝수 수동).
///   · 값 = 중립선에서 움직인 양(양수). 각도기 눈금 0은 단면 연직(회전은 정면).
///   · 마커: 핀치를 오므리면 생기고 펴면 고정. 동작 마커는 1° 단위 수정, 대추·미간은 mm 단위.
///   · 대추 기준 각과 목 중앙 기준 각을 <b>둘 다</b> 기록한다 — 어느 쪽이 실측에 가까운지 나중에 판별한다.
///
/// ★기존 실측(경추ROM실측 시나리오)·실측랩과 <b>서로 참조하지 않는다</b>. 이 씬 전용이다.
/// ★기록 줄은 <c>Debug.Log</c>로 남긴다 — <c>ChunaLogger.Log</c>는 릴리스 빌드에서 잘린다(Conditional).
/// </summary>
public class RomRecordSession : MonoBehaviour
{
    [Header("=== 시작 자리 ===")]
    [Tooltip("시작할 때 3축을 눈앞 이만큼(m) 앞에 둔다.")]
    [SerializeField] private float startDistance = 0.7f;
    [Tooltip("시작할 때 3축을 눈높이보다 이만큼(m) 낮게 둔다.")]
    [SerializeField] private float startBelowEye = 0.3f;
    [Tooltip("3축 선 길이(m, 중심에서 한쪽).")]
    [SerializeField] private float axisLength = 0.25f;

    [Header("=== 크기(글자는 TMP 폰트 크기, 스케일 1) ===")]
    [Tooltip("축 끝 글자·마커 각도 글자·눈금 숫자(0.8배). ★기존 각도기 판독 0.05(기기 확인값).\n" +
             "09-18: 1.1(너무 큼) → 0.03(\"각도기도 텍스트도 너무 줄였다\") → 0.05.")]
    [SerializeField] private float textSize = 0.05f;
    [Tooltip("안내판(단계·지시·기록 요약) 글자.")]
    [SerializeField] private float panelTextSize = 0.045f;
    [Tooltip("손목 판의 버튼 글자·머리줄 글자.")]
    [SerializeField] private float buttonLabelSize = 0.03f;

    [Header("=== 손목 판 — Meta UI Set(2026-09-21) ===")]
    // ★09-21 기기 판정: Meta UI 판은 <b>퇴화</b>였다(사용자) — 버튼 크기가 들쭉날쭉하고, 배치가 헐겁고,
    //   무엇보다 <b>누르는 손에 판이 가려</b> 안 보였다. 그래서 기본을 끔으로 내린다.
    //   지우지는 않는다 — 크기·배치 문제를 풀면 글자 크기 이점(Meta 표준 7mm)이 여전히 크다.
    [Tooltip("Meta Interaction SDK의 UI Set 프리팹으로 판을 만든다. ★09-21 기기 확인에서 퇴화로 판정돼 기본은 끔이다.")]
    [SerializeField] private bool useMetaUI = false;
    [Tooltip("Meta 판의 칸 너비(캔버스 px). 캔버스 스케일이 0.0005라 1px = 0.5mm다 — 84px ≈ 4.2cm.")]
    [SerializeField] private float metaCellPx = 84f;
    [Tooltip("Meta 판의 줄 높이(px).")]
    [SerializeField] private float metaRowPx = 68f;
    [Tooltip("Meta 판의 버튼 높이(px).")]
    [SerializeField] private float metaButtonPx = 56f;
    [Tooltip("Meta 판의 글자 크기(pt). ★Meta 표준은 14 — 캔버스 스케일을 곱하면 7mm다.")]
    [SerializeField] private float metaLabelPt = 15f;

    [Header("=== 각도기·바늘 모양(2026-09-21) ===")]
    // ★사용자 지시 09-21: "각도기도 하얀색 말고 회색 계열로 조금 시선 분산 덜 되게 · 바늘 선 얇게 ·
    //   각도 텍스트는 직선이나 원이 안 겹치게 · 회전 각도는 원통형으로 머리 둘레에 벽면처럼."
    // ★여기 값은 전부 <b>눈으로 맞출 것</b>이라 인스펙터로 뺀다(기기 미검증).
    [Tooltip("눈금판 원·원통 테두리 색.")]
    [SerializeField] private Color dialColor = new Color(0.60f, 0.62f, 0.65f, 0.55f);
    [Tooltip("눈금 색.")]
    [SerializeField] private Color dialTickColor = new Color(0.64f, 0.66f, 0.69f, 0.60f);
    [Tooltip("눈금 0 색. ★기준선이라 다른 눈금보다 또렷하게 둔다.")]
    [SerializeField] private Color dialZeroColor = new Color(0.88f, 0.90f, 0.93f, 0.95f);
    [Tooltip("눈금 숫자 색.")]
    [SerializeField] private Color dialLabelColor = new Color(0.80f, 0.82f, 0.85f, 0.95f);
    [Tooltip("바늘 선 굵기(m). 09-21에 0.007에서 줄였다.")]
    [SerializeField] private float needleWidth = 0.003f;
    [Tooltip("눈금 숫자를 원 밖으로 미는 배수(반지름 기준).")]
    [SerializeField] private float tickLabelOut = 1.20f;
    [Tooltip("회전 단계 원통 벽의 높이(m, 위아래 합). ★추정값이다 — 머리에 맞는지 눈으로 볼 것.")]
    [SerializeField] private float cylinderHeight = 0.30f;
    [Tooltip("원통 벽의 반지름 배수. 1이면 각도기 반지름(회전중심→미간)과 같아 머리에 바짝 붙는다.\n" +
             "★09-21 사용자 지시로 키웠다. 눈으로 맞출 값이다.")]
    [SerializeField] private float cylinderRadiusScale = 2.2f;

    [Header("=== 판 모양(2026-09-21) ===")]
    // ★판 디자인은 진행Root·설정 팝업을 재서 그대로 옮겼다(RomRecordWristMenu 주석에 실측값이 있다).
    //   그 값들은 코드에 두고, <b>눈으로 자주 만질 둘만</b> 여기로 뺀다.
    [Tooltip("판 배경의 불투명도. ★실측(저쪽 UI)은 1이지만 패스스루에서 답답하면 0.8쯤으로 내린다.")]
    [Range(0.2f, 1f)] [SerializeField] private float menuPlateAlpha = 1f;
    [Tooltip("버튼에 항목 색(tint)을 섞는 정도. 0이면 저쪽 UI처럼 완전 무채색이 된다.")]
    [Range(0f, 1f)] [SerializeField] private float menuTintStrength = 0.5f;

    [Header("=== 판 자리(2026-09-21) ===")]
    // ★사용자 지시 09-21: "UI 동작할 때 간섭이 너무 많다 — 진행ROOT 패널처럼 따로 분리해야겠다."
    //   손목을 따라다니면 <b>기록하는 손과 판이 같은 자리</b>에 있어 서로 막는다.
    //   09-21 로그 실측에서 핀치를 막은 주범이 판 자신이었다(판 근처 3,084 · 다가옴 3,708 프레임).
    [Tooltip("판을 손목이 아니라 <b>허공에 고정</b>한다(진행Root 방식). 끄면 종전처럼 양 손목을 따라다닌다.")]
    [SerializeField] private bool menuFixedInSpace = true;
    [Tooltip("고정 판을 처음 놓을 자리 — 눈앞 거리(m).")]
    [SerializeField] private float menuFixedDistance = 0.55f;
    [Tooltip("고정 판을 처음 놓을 자리 — 눈높이보다 아래로(m). ★09-21: 0.3은 너무 낮아 조작이 힘들었다.")]
    [SerializeField] private float menuFixedBelowEye = 0.14f;
    [Tooltip("고정 판을 처음 놓을 자리 — 정면에서 옆으로(m). 양수면 오른쪽. ★3축과 겹치지 않게 비켜 둔다.")]
    [SerializeField] private float menuFixedSide = -0.38f;
    [Tooltip("고정 판을 잡아 끌 수 있는 반경(m). 판 중심에서 이 안을 핀치로 오므리면 판이 따라온다.")]
    [SerializeField] private float menuGrabRadius = 0.16f;

    [Header("=== 손목 판(종전 Quad 판) ===")]
    [Tooltip("판의 칸 너비(m). 판은 6칸 너비다.")]
    [SerializeField] private float menuCellWidth = 0.042f;
    [Tooltip("판의 줄 높이(m).")]
    [SerializeField] private float menuRowHeight = 0.034f;
    [Tooltip("버튼 높이(m).")]
    [SerializeField] private float menuButtonHeight = 0.026f;
    [Tooltip("손목 메뉴를 손목에서 위로 띄우는 높이(m).")]
    [SerializeField] private float menuLift = 0.06f;
    [Tooltip("누르는 손 검지가 판에서 이 거리(m) 안으로 오면 판이 손목을 따라가지 않고 멈춘다(가림으로 튀는 것 방지).")]
    [SerializeField] private float menuHoldDistance = 0.15f;
    [Tooltip("버튼·핀치 소리 크기(0이면 무음).")]
    [Range(0f, 1f)] [SerializeField] private float soundVolume = 0.6f;

    [Header("=== 조정 단위 ===")]
    [SerializeField] private float yawStepDeg = 1f;
    [SerializeField] private float heightStepMm = 5f;
    [Tooltip("대추·미간을 버튼으로 옮기는 단위(mm).")]
    [SerializeField] private float nudgeStepMm = 1f;
    [Tooltip("기준축(목 중앙)을 앞뒤로 옮기는 단위(mm).")]
    [SerializeField] private float neckStepMm = 5f;
    [SerializeField] private float adjustStepDeg = 1f;

    [Header("=== 잡아 끌기(2026-09-21 신설) ===")]
    // ★09-21 로그 실측: 기준선 세팅에서 핀치가 17번 거부됐다("기준선 세팅 중에는 찍지 않는다").
    //   사용자는 3축을 손으로 잡아 옮기려 한 것이었다 — 5mm 버튼으로만 올리게 해 둬 한참 걸렸다.
    [Tooltip("3축(기준선)을 잡을 수 있는 반경(m). 이 안에서 핀치를 오므리면 3축이 손을 따라온다.")]
    [SerializeField] private float axesGrabRadius = 0.25f;
    [Tooltip("대추·미간 점을 잡을 수 있는 반경(m).")]
    [SerializeField] private float dotGrabRadius = 0.08f;

    [Header("=== 바늘 각도기(2026-09-21 신설) ===")]
    // ★사용자 지시 09-21: "미간 찍는 게 빡세면 각도기에 니들을 하나 그어서 그 끝을 잡고
    //   그 면에서만 회전하도록". ★점찍기를 <b>대체하지 않는다</b> — 둘 다 두고 따로 테스트한다.
    [Tooltip("동작 단계에서 바늘을 띄운다. 손목 판의 [바늘] 버튼으로도 껐다 켤 수 있다.")]
    [SerializeField] private bool needleEnabled = true;
    [Tooltip("바늘 손잡이를 잡을 수 있는 반경(m).")]
    [SerializeField] private float needleGrabRadius = 0.1f;
    [Tooltip("손잡이를 눈금판보다 이만큼(m) 더 밖에 둔다. ★0이면 손잡이가 머리 표면에 놓여 사람 머리에 묻힌다(09-21 사용자 지적).")]
    [SerializeField] private float needleHandleOut = 0.16f;
    [Tooltip("미간을 아직 안 찍었을 때 쓸 각도기 반지름(m). 미간이 있으면 중심에서 미간까지를 쓴다.")]
    [SerializeField] private float needleFallbackRadius = 0.18f;

    [Header("=== 나가기 ===")]
    [Tooltip("[나가기]를 이만큼(초) 누르고 있어야 나간다. ★09-21: 지탱하던 손이 스쳐 나가기가 눌렸다.")]
    [SerializeField] private float exitHoldSeconds = 1f;

    [Header("=== 기준축(목 중앙) ===")]
    [Tooltip("대추에서 환자 앞쪽으로 기준축을 옮기는 거리(mm). 사람마다 달라 정해진 값이 없다 — 버튼으로 맞춘다.\n" +
             "★0이면 대추 위치 그대로다.")]
    [SerializeField] private float neckOffsetMm = 0f;

    [Header("=== 좌우 ===")]
    [Tooltip("측굴·회전의 좌우가 반대로 기록되면 켠다. ★Play에서 환자 오른쪽으로 기울여 '우'가 찍히는지 먼저 본다.")]
    [SerializeField] private bool flipSides = false;

    [Header("=== 단면 표시(2026-09-21 신설) ===")]
    [Tooltip("3축이 이루는 단면을 사람 크기만큼 사각형으로 보여 준다.")]
    [SerializeField] private bool showSectionPlane = true;
    [Tooltip("찍은 점에서 단면까지 수직선을 그어 삼각면으로 보여 준다.\n" +
             "★각은 단면에 투영해 재므로, 벗어난 만큼이 «각에 못 쓴 성분»이다. 그걸 눈에 보이게 한다.")]
    [SerializeField] private bool showOffPlaneTriangle = true;

    [Header("=== 현실에 가려지기(2026-09-21 신설) ===")]
    // ★사용자 09-21: "패스스루에서 오브젝트가 현실 위에 붕 떠 있다 — 사람 위에 십자선을 그어도
    //   몸통을 관통하는 십자선이 아니라 그냥 위에 올려진 십자선 같다."
    //   정체는 <b>폐색이 없는 것</b>이다. 가상 물체가 현실 물체 뒤로 안 들어가고 늘 덧그려지니
    //   뇌가 "몸 안"이 아니라 "몸 앞 유리창의 그림"으로 읽는다.
    // ★Meta Depth API로 가린다. 다만 <b>완전히 지우지 않고 흐리게</b> 남긴다 — 기준축·십자선은
    //   몸을 관통해 보여야 하는 것이라 사라지면 안 된다(사용자: "아예 약간 흐린색으로").
    // ★Quest 3/3S 전용이다. 안 되는 기기에서는 키워드가 안 켜져 <b>종전과 똑같이</b> 보인다.
    [Tooltip("표시물이 현실 물체에 가려지게 한다(Quest 3/3S). 끄면 종전처럼 늘 덧그려진다.")]
    [SerializeField] private bool useDepthOcclusion = true;
    [Tooltip("가려진 부분에 남길 진하기. 0이면 완전히 사라지고, 1이면 폐색이 없는 것과 같다.\n" +
             "★0.15~0.25가 «몸 안을 지나간다»로 읽히는 구간이다(추정 — 눈으로 맞출 값).")]
    [Range(0f, 1f)] [SerializeField] private float occludedAlpha = 0.18f;

    [Header("=== 씬 ===")]
    [Tooltip("시작할 때 패스스루를 켠다. 이 씬은 패스스루 전용이다.")]
    [SerializeField] private bool enablePassthroughOnStart = true;
    [SerializeField] private string lobbySceneName = "lobby";   // ★실제 종료 팝업(ExitPopupController)이 쓰는 이름과 같게

    // ── 상태 ─────────────────────────────────────────────────────────
    // ★대추부터 시작한다(2026-09-21 사용자 제안). 대추를 찍으면 3축이 그 자리로 가고,
    //   정면은 미간을 찍을 때 대추→미간으로 정해진다 — 기준선 세팅을 먼저 할 이유가 없다.
    //   [이전]으로 기준선 세팅에 갈 수는 있다(3축을 손으로 다듬고 싶을 때).
    private RomRecordStep step = RomRecordStep.Landmarks;
    private float yaw;
    private Vector3 frameOrigin;           // 대추를 찍기 전 3축 자리
    private bool hasC7, hasGlab;
    private Vector3 c7, glab;
    private int landmarkTarget;            // 0 대추 · 1 미간 · 2 목 중앙
    private readonly List<RomRecordMark>[] marks =
    {
        new List<RomRecordMark>(), new List<RomRecordMark>(), new List<RomRecordMark>(), new List<RomRecordMark>(),
    };

    private readonly RomRecordHands hands = new RomRecordHands();
    // ★판 구현이 둘이다(09-21) — Meta UI Set(기본)과 종전 월드 Quad 판. useMetaUI로 고른다.
    private IRomRecordMenu leftMenu, rightMenu;
    private readonly RomRecordVisual view = new RomRecordVisual();
    private Transform eye;
    private bool placed;
    private bool dirty = true;
    private bool livePinchLeft;            // 지금 오므리고 있는 손(한 번에 하나만 받는다)
    private bool liveActive;
    // ★바늘(09-21) — 단계마다 <b>넷</b>이다. 굴곡·신전은 둘(능동·압박)만 쓰고,
    //   측굴·회전은 넷을 다 쓴다(좌 능동·좌 압박·우 능동·우 압박 — 사용자 09-21: "좌우 총 4개가 필요").
    //   짝수가 능동, 홀수가 압박이다. 좌우는 바늘이 가리키는 쪽으로 저절로 갈린다.
    // ★★<b>바늘이 곧 값이다</b>(09-21 "손이 너무 많이 가면 안 돼") — 따로 [기록]을 누르지 않는다.
    //   0°에 세워 둔 바늘을 끌어내면 그게 그 항목의 기록이고, 다시 끌면 고쳐진다.
    private const int NeedlePerStep = 4;
    private readonly Vector3[] needleDir = new Vector3[4 * NeedlePerStep];
    private readonly bool[] needlePlaced = new bool[4 * NeedlePerStep];
    private readonly bool[] needleMoved = new bool[4 * NeedlePerStep];   // 0°에서 끌어냈나 = 기록됐나
    private bool needleOn = true;
    // ★잡아 끌기(09-21) — 0 3축 · 1 대추 · 2 미간 · 3 능동 바늘 · 4 압박 바늘 · -1 아무것도 안 잡음(«찍기»로 간다)
    private int dragTarget = -1;
    private Vector3 dragGrabbedAt, dragStartValue;
    // ★진단 로그 자기 침묵 — <b>손별로</b> 따로 센다(09-21 수정).
    //   처음엔 하나로 뒀는데 왼손·오른손을 번갈아 처리하니 «마지막 이유»가 매번 바뀌어
    //   억제가 통째로 죽었다 — 한 판에 11,000줄이 쏟아졌다. 로그가 로그를 못 보게 만든 꼴이다.
    private readonly float[] nextPinchLog = new float[2];
    private readonly Dictionary<string, int> pinchBlockCount = new Dictionary<string, int>();
    private readonly StringBuilder sb = new StringBuilder(512);
    private float lastPressTime = -99f;
    private bool menuHoldL, menuHoldR;
    private AudioSource audioSrc;
    private AudioClip sndPress, sndRepeat, sndPinch, sndUndo, sndDeny;

    private static readonly string[] StepTitle = { "기준선 세팅", "대추·미간", "굴곡", "신전", "측굴", "회전", "완료" };

    private void Start()
    {
        if (enablePassthroughOnStart) EnablePassthrough();

        TMP_FontAsset font = KoreanFontResolver.Resolve();
        Material mat = BuildDisplayMaterial();

        needleOn = needleEnabled;   // ★런타임 토글([바늘] 버튼)이 이 값을 이어받는다
        view.textSize = textSize;
        view.panelSize = panelTextSize;
        // ★색·굵기는 매 SetDial/SetNeedle에서 다시 입혀지므로 여기서 한 번 넘기면 된다(09-21).
        view.dialColor = dialColor;
        view.dialTickColor = dialTickColor;
        view.dialZeroColor = dialZeroColor;
        view.dialLabelColor = dialLabelColor;
        view.needleWidth = needleWidth;
        view.tickLabelOut = tickLabelOut;
        view.cylinderHeight = cylinderHeight;
        view.cylinderRadiusScale = cylinderRadiusScale;
        view.Build(transform, font, mat);
        if (useMetaUI)
        {
            var lm = new RomRecordMenuUI();
            var rm = new RomRecordMenuUI();
            foreach (var menu in new[] { lm, rm })
            {
                menu.cellPx = metaCellPx;
                menu.rowPx = metaRowPx;
                menu.buttonPx = metaButtonPx;
                menu.labelPt = metaLabelPt;
                menu.headerPt = metaLabelPt;
                menu.lift = menuLift;
            }
            leftMenu = lm;
            rightMenu = rm;
        }
        else
        {
            var lm = new RomRecordWristMenu();
            var rm = new RomRecordWristMenu();
            foreach (var menu in new[] { lm, rm })
            {
                menu.labelSize = buttonLabelSize;
                menu.headerSize = buttonLabelSize;
                menu.cellW = menuCellWidth;
                menu.rowH = menuRowHeight;
                menu.buttonH = menuButtonHeight;
                menu.lift = menuLift;
                // ★판 색은 실측값을 쓰되 불투명도만 여기서 조절한다(패스스루에서 답답할 수 있어서).
                Color top = menu.plateTopColor, bot = menu.plateBottomColor;
                top.a = bot.a = menuPlateAlpha;
                menu.plateTopColor = top;
                menu.plateBottomColor = bot;
                menu.buttonTintStrength = menuTintStrength;
            }
            leftMenu = lm;
            rightMenu = rm;
        }
        leftMenu.FixedInSpace = menuFixedInSpace;
        leftMenu.Build(transform, menuFixedInSpace ? "조작 판" : "왼손목 메뉴", 14, font, mat);
        // ★고정 모드면 판은 <b>하나</b>다(둘을 허공에 띄우면 서로 가린다). 그때는 오른쪽 자리에
        //   같은 판을 다시 가리켜 둔다 — SetLayout·Near 같은 곳에서 널을 만지지 않게.
        if (menuFixedInSpace)
        {
            rightMenu = leftMenu;
        }
        else
        {
            rightMenu.FixedInSpace = false;
            rightMenu.Build(transform, "오른손목 메뉴", 14, font, mat);
        }
        Debug.Log($"[실측기록] 판 — {(useMetaUI ? "Meta UI Set" : "Quad")} · " +
                  $"{(menuFixedInSpace ? "허공 고정(진행Root 방식)" : "양 손목 추종")}");
        ApplyStepButtons();
        BuildSounds();

        Debug.Log("[실측기록] 시작 — 기준선 세팅부터. 좌우 뒤집기 " + (flipSides ? "켬" : "끔") + $" · 목 중앙 보정 {neckOffsetMm:F0}mm · " +
                  $"글자 {textSize}/{panelTextSize}/버튼 {buttonLabelSize} · 칸 {menuCellWidth * 100f:F1}×{menuRowHeight * 100f:F1}cm · 소리 {soundVolume:F1}");
    }

    /// <summary>
    /// 표시물이 쓸 머티리얼을 만든다(2026-09-21). 폐색을 켜면 «현실에 가려지면 흐려지는» 셰이더를 쓰고,
    /// 안 되면 종전 <c>Sprites/Default</c>로 떨어진다.
    /// ★<b>조용히 실패하지 않게</b> 어느 쪽으로 갔는지 반드시 로그에 남긴다 —
    ///   폐색이 안 보일 때 "셰이더를 못 찾은 것"인지 "기기가 지원을 안 하는 것"인지 갈려야 한다.
    /// </summary>
    private Material BuildDisplayMaterial()
    {
        if (useDepthOcclusion)
        {
            var src = Resources.Load<Material>("RomRecordUI/RomRecordOccluded");
            if (src != null)
            {
                var m = new Material(src);   // ★에셋 원본을 건드리지 않게 복제한다
                m.SetFloat("_OccludedAlpha", occludedAlpha);
                bool supported = EnableDepthOcclusion();
                Debug.Log($"[실측기록] 표시 머티리얼 — 폐색 셰이더 · 가려진 곳 {occludedAlpha:F2} · " +
                          $"기기 지원 {(supported ? "O" : "★없음(종전처럼 늘 덧그려진다)")}");
                return m;
            }
            ChunaLogger.LogWarning("[실측기록] ★폐색 머티리얼을 못 찾았다(Resources/RomRecordUI/RomRecordOccluded) — 종전 셰이더로 간다.");
        }

        // ★Sprites/Default는 Always Included에 들어 있다(GraphicsSettings 10753) — 빌드에서도 null이 아니다
        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null)
        {
            ChunaLogger.LogWarning("[실측기록] Sprites/Default 셰이더를 못 찾았다 — 선이 분홍으로 보이면 이것이다.");
            return null;
        }
        Debug.Log("[실측기록] 표시 머티리얼 — 종전 Sprites/Default(폐색 없음)");
        return new Material(sh);
    }

    /// <summary>
    /// 깊이(폐색)를 켠다. 켜졌으면 true. ★지원하지 않는 기기에서 켜면 에러를 뱉으므로 먼저 묻는다.
    /// </summary>
    private bool EnableDepthOcclusion()
    {
        if (!Meta.XR.EnvironmentDepth.EnvironmentDepthManager.IsSupported)
        {
            Debug.Log("[실측기록] 이 기기는 환경 깊이를 지원하지 않는다(Quest 3/3S 전용) — 폐색 없이 간다.");
            return false;
        }
        var mgr = FindAnyObjectByType<Meta.XR.EnvironmentDepth.EnvironmentDepthManager>();
        if (mgr == null) mgr = gameObject.AddComponent<Meta.XR.EnvironmentDepth.EnvironmentDepthManager>();
        mgr.enabled = true;
        // 부드러운 폐색이 기본이다 — 경계가 딱딱하면 오려 붙인 것처럼 보인다.
        mgr.OcclusionShadersMode = Meta.XR.EnvironmentDepth.OcclusionShadersMode.SoftOcclusion;
        // ★손은 깊이에서 빼지 않는다. 시술자 손이 표시물을 가리는 것도 «앞에 있다»는 단서라 그대로 둔다.
        return true;
    }

    // ── 소리 ─────────────────────────────────────────────────────────
    // ★09-18 사용자: "누른 건지 구분이 안 간다. 소리도 애니메이션도 없다."
    //   음원 파일 없이 짧은 음을 코드로 만든다(에셋 배선이 필요 없고 빌드에서 빠질 일이 없다).
    private void BuildSounds()
    {
        audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.playOnAwake = false;
        audioSrc.spatialBlend = 0f;            // 2D — 어디서 눌러도 같은 크기로 들린다
        sndPress = Tone("누름", 1250f, 1250f, 0.045f);
        sndRepeat = Tone("반복", 1600f, 1600f, 0.025f);
        sndPinch = Tone("찍음", 880f, 1320f, 0.11f);      // 올라가는 두 음 — 기록됐다
        sndUndo = Tone("취소", 700f, 440f, 0.09f);        // 내려가는 음 — 지웠다
        sndDeny = Tone("거부", 300f, 300f, 0.12f);        // 낮은 음 — 받지 않았다
    }

    private static AudioClip Tone(string name, float f0, float f1, float seconds)
    {
        const int rate = 44100;
        int n = Mathf.CeilToInt(rate * seconds);
        var data = new float[n];
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            float f = Mathf.Lerp(f0, f1, t);
            phase += 2f * Mathf.PI * f / rate;
            float env = Mathf.Min(1f, t * 40f) * (1f - t) * (1f - t);   // 짧게 올라갔다 빠르게 사라진다(딸깍)
            data[i] = Mathf.Sin(phase) * env * 0.6f;
        }
        var clip = AudioClip.Create("[실측기록] " + name, n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private void Play(AudioClip c)
    {
        if (audioSrc != null && c != null && soundVolume > 0f) audioSrc.PlayOneShot(c, soundVolume);
    }

    private void EnablePassthrough()
    {
        // ★이 씬은 패스스루 전용이라 우리가 켜고 씬이 내려갈 때 같이 사라진다(다른 씬과 공유하는 상태가 아니다).
        var layer = FindFirstObjectByType<OVRPassthroughLayer>(FindObjectsInactive.Include);
        if (layer != null)
        {
            layer.enabled = true;
            layer.hidden = false;
        }
        if (OVRManager.instance != null) OVRManager.instance.isInsightPassthroughEnabled = true;

        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);   // ★알파 0이어야 뒤의 패스스루가 보인다(Underlay)
        }
        Debug.Log($"[실측기록] 패스스루 — 레이어 {(layer != null ? "O" : "★없음")} · OVRManager {(OVRManager.instance != null ? "O" : "★없음")} · 카메라 {(cam != null ? "O" : "★없음")}");
    }

    private void Update()
    {
        if (eye == null && Camera.main != null) eye = Camera.main.transform;
        if (eye == null) return;

        // ★★09-21 실측: 첫 프레임에 자리를 잡으면 <b>헤드셋 추적이 아직 안 붙어</b> 눈이 원점에 있다.
        //   그러면 3축도 조작 판도 발밑 엉뚱한 곳에 선다 — 로그에 판이 (-0.380,-0.300,0.550)로 찍혔는데
        //   그건 눈이 (0,0,0)일 때 나오는 값이었고, 그 판에서 "기준선을 못 잡았다"가 8번 찍혔다.
        //   사람 눈은 바닥에서 최소 30cm 위다 — 그 전까지는 아무것도 놓지 않는다.
        if (!EyeReady)
        {
            // ★★09-21: XR 초기화가 실패하면(로그에 OVRManager 없음·OpenXR 예외) 눈이 영영 원점이다.
            //   그때 그냥 기다리기만 하면 <b>화면이 통째로 비어</b> 무슨 일인지 알 수가 없다 —
            //   실제로 그 판은 2초 만에 꺼졌다. 그래서 얼마간 기다린 뒤에는 그냥 놓고 경고를 남긴다.
            if (trackingWaitStart < 0f) trackingWaitStart = Time.unscaledTime;
            float waited = Time.unscaledTime - trackingWaitStart;
            ReportNotReady(waited);
            if (waited < trackingWaitSeconds) return;
        }
        else
        {
            trackingWaitStart = -1f;
            // ★★09-21 실측: Start에서 물으면 <b>XR이 아직 안 붙어</b> 깊이 서브시스템이 없다
            //   (로그 순서로 확인 — 표시 머티리얼 줄이 시작 줄보다 앞이고, 그 직후 눈 높이가 0.00m다).
            //   그래서 추적이 붙은 뒤에 <b>한 번 더</b> 켜 본다.
            if (useDepthOcclusion && !depthRetried)
            {
                depthRetried = true;
                if (EnableDepthOcclusion())
                    Debug.Log("[실측기록] ★추적이 붙은 뒤 폐색을 켰다 — 이제 현실에 가려진다.");
                else
                    Debug.Log("[실측기록] 추적이 붙은 뒤에도 환경 깊이가 없다 — 이 실행에서는 폐색 없이 간다.");
            }
        }

        if (!placed)
        {
            // 처음 한 번 — 눈앞에, 눈높이보다 조금 아래에 3축을 둔다. 정면은 사용자가 보는 수평 방향.
            Vector3 f = Vector3.ProjectOnPlane(eye.forward, Vector3.up);
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            f.Normalize();
            frameOrigin = eye.position + f * startDistance - Vector3.up * startBelowEye;
            yaw = Quaternion.LookRotation(f, Vector3.up).eulerAngles.y;
            placed = true;
            dirty = true;
        }

        hands.Resolve();
        HandleMenus();
        HandlePinch(true);
        HandlePinch(false);

        if (dirty)
        {
            dirty = false;
            Redraw();
        }
        view.FaceCamera(eye);
    }

    /// <summary>
    /// 헤드셋 추적이 붙었나(2026-09-21 신설). ★붙기 전에는 눈이 원점이라 무엇을 놓든 발밑에 놓인다.
    /// 사람 눈은 바닥에서 최소 30cm 위다 — 그보다 낮으면 아직 추적 전이다.
    /// </summary>
    private bool EyeReady => eye != null && eye.position.y > 0.3f;

    private float nextNotReadyLog;
    private float trackingWaitStart = -1f;
    private bool trackingGaveUp;
    private bool depthRetried;   // 추적이 붙은 뒤 폐색을 한 번 더 켜 봤나
    // ★이만큼 기다려도 추적이 안 붙으면 그냥 놓는다. 화면이 계속 비어 있는 것보다 낫다(09-21).
    private const float trackingWaitSeconds = 8f;

    private void ReportNotReady(float waited)
    {
        if (Time.unscaledTime < nextNotReadyLog) return;
        nextNotReadyLog = Time.unscaledTime + 2f;
        float y = eye != null ? eye.position.y : -1f;
        if (waited < trackingWaitSeconds)
        {
            Debug.Log($"[실측기록] 헤드셋 추적을 기다린다 — 눈 높이 {y:F2}m · {waited:F0}/{trackingWaitSeconds:F0}초 " +
                      "(0.30m를 넘어야 3축과 조작 판을 놓는다)");
            return;
        }
        if (trackingGaveUp) return;
        trackingGaveUp = true;
        // ★한 번만 크게 알린다 — 이 상태면 XR이 안 붙은 것이라 자리가 엉뚱해도 어쩔 수 없다.
        ChunaLogger.LogWarning($"<color=orange>[실측기록] ★추적이 {trackingWaitSeconds:F0}초 동안 안 붙었다" +
                               $"(눈 높이 {y:F2}m) — 그냥 놓는다. XR 초기화 실패일 수 있다" +
                               "(패스스루 로그의 OVRManager 항목을 본다).</color>");
        Debug.Log("[실측기록] ★추적 없이 놓는다 — 자리가 엉뚱하면 조작 판과 3축을 손으로 잡아 끈다.");
    }

    // ── 계산 ─────────────────────────────────────────────────────────
    private Vector3 Pivot => hasC7 ? c7 + RomRecordGeometry.Forward(yaw) * (neckOffsetMm * 0.001f) : frameOrigin;

    /// <summary>
    /// 각도기·바늘이 함께 쓰는 기하(2026-09-21). ★표시·드래그·기록이 <b>같은 함수</b>를 탄다(규칙 9 —
    /// 따로 계산하면 미리보기가 거짓말을 한다).
    /// ★미간이 없어도 뜬다 — 바늘은 미간 찍기를 대신하려고 만든 것이다. 그때는 반지름이 기본값이고
    ///   중립선 기준 각은 못 낸다(눈금 기준 각만 남는다).
    /// </summary>
    private bool DialFrame(out Vector3 center, out Vector3 normal, out Vector3 zero, out float radius)
    {
        center = default;
        normal = Vector3.up;
        zero = Vector3.forward;
        radius = needleFallbackRadius;
        if (!IsMotion(step) || !hasC7) return false;

        Vector3 pivot = Pivot;
        normal = RomRecordGeometry.PlaneNormal(step, yaw);
        zero = RomRecordGeometry.ScaleZero(step, yaw);
        // 회전 눈금판은 미간 높이의 수평면에 둔다 — 회전 중심 높이에 두면 머리에 가려 안 보인다.
        center = step == RomRecordStep.Rotation && hasGlab ? new Vector3(pivot.x, glab.y, pivot.z) : pivot;
        radius = hasGlab
            ? Mathf.Max(0.08f, Vector3.ProjectOnPlane(glab - center, normal).magnitude)
            : needleFallbackRadius;
        return true;
    }

    private static int MotionIndex(RomRecordStep s) => (int)s - (int)RomRecordStep.Flexion;
    private static bool IsMotion(RomRecordStep s) => s >= RomRecordStep.Flexion && s <= RomRecordStep.Rotation;
    private static int Capacity(RomRecordStep s) => s == RomRecordStep.LateralFlexion || s == RomRecordStep.Rotation ? 4 : 2;
    private static bool HasSides(RomRecordStep s) => s == RomRecordStep.LateralFlexion || s == RomRecordStep.Rotation;

    /// <summary>그 마커의 각 — 지금 기준축(목 중앙) 기준, 수정 포함.</summary>
    private float AngleOf(RomRecordStep s, RomRecordMark m, Vector3 pivot)
    {
        Vector3 n = RomRecordGeometry.PlaneNormal(s, yaw);
        return Mathf.Max(0f, RomRecordGeometry.PlaneAngle(pivot, glab, m.raw, n) + m.adjustDeg);
    }

    // ── 손목 메뉴 ────────────────────────────────────────────────────
    private void HandleMenus()
    {
        bool rTip = hands.TryJoint(false, HandJointId.HandIndexTip, out Vector3 rIdx);
        bool lTip = hands.TryJoint(true, HandJointId.HandIndexTip, out Vector3 lIdx);

        // ★허공 고정 모드(09-21) — 판 하나를 세워 두고 <b>양손 검지 어느 쪽으로든</b> 누른다.
        //   판이 손에서 떨어져 있으니 "판 고정·판 근처" 차단이 필요 없다 — 그게 간섭의 주범이었다.
        if (menuFixedInSpace)
        {
            if (!leftMenu.Placed && eye != null)
            {
                Vector3 f = Vector3.ProjectOnPlane(eye.forward, Vector3.up);
                if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
                f.Normalize();
                Vector3 side = Vector3.Cross(Vector3.up, f);   // f를 볼 때 오른쪽
                Vector3 pos = eye.position + f * menuFixedDistance
                            + side * menuFixedSide - Vector3.up * menuFixedBelowEye;
                leftMenu.PlaceAt(pos, eye);
                Debug.Log($"[실측기록] 조작 판을 세웠다 — {Fmt(pos)} (잡아 끌어 옮길 수 있다)");
            }
            leftMenu.Follow(false, Vector3.zero, eye, false);   // 고정 모드에서는 회전만 눈을 따라간다
            menuHoldL = menuHoldR = false;

            // ★★09-21 사용자: "눌렀다 뗐다 하는 것에 이중 삼중으로 다다다다 눌려 버려."
            //   종전엔 같은 판을 <b>양손으로 두 번</b> Poll했다. 그러면 한 손이 버튼 안에 있고 다른 손이 밖일 때
            //   «들어옴/나감» 상태가 매 프레임 뒤집혀, 쿨다운(0.35초)이 풀릴 때마다 한 번씩 계속 발사됐다.
            //   → 판에 <b>더 가까운 손 하나만</b> 본다. 두 손으로 번갈아 누를 일은 없다.
            bool useRight = rTip && (!lTip || leftMenu.Distance(rIdx) <= leftMenu.Distance(lIdx));
            Vector3 pokeTip = useRight ? rIdx : lIdx;
            bool pokeOk = useRight ? rTip : lTip;
            string fid = leftMenu.Poll(pokeTip, pokeOk);
            bool frep = leftMenu.LastRepeat;
            if (fid != null)
            {
                lastPressTime = Time.unscaledTime;
                Play(fid == "undo" ? sndUndo : frep ? sndRepeat : sndPress);
                OnButton(fid);
            }
            leftMenu.Tick();
            return;
        }

        bool lw = hands.TryWrist(true, out Pose lwp);
        bool rw = hands.TryWrist(false, out Pose rwp);

        // ★다가오면 제자리 고정(09-18 사용자 결정). 왼손목 판은 오른 검지가, 오른손목 판은 왼 검지가 누른다.
        //   누르는 손이 판 쪽 손목을 가리면 그 손목 추적이 끊겨 판이 숨었다 튀었다("UI가 도망간다").
        menuHoldL = rTip && leftMenu.Distance(rIdx) < menuHoldDistance;
        menuHoldR = lTip && rightMenu.Distance(lIdx) < menuHoldDistance;
        leftMenu.Follow(lw, lwp.position, eye, menuHoldL);
        rightMenu.Follow(rw, rwp.position, eye, menuHoldR);

        string id = leftMenu.Poll(rIdx, rTip);
        bool repeat = leftMenu.LastRepeat;
        if (id == null) { id = rightMenu.Poll(lIdx, lTip); repeat = rightMenu.LastRepeat; }
        if (id != null)
        {
            lastPressTime = Time.unscaledTime;
            Play(id == "undo" ? sndUndo : repeat ? sndRepeat : sndPress);
            OnButton(id);
        }
        leftMenu.Tick();
        rightMenu.Tick();
    }

    // ★09-21 사용자: "UI 너무 조잡해 · 버튼 디자인도 투박하고."
    //   녹화 프레임에서 보니 채도 높은 초록·노랑·파랑이 나란히 있어 눈이 아팠다.
    //   → <b>어둡고 채도 낮은 한 계열</b>로 낮추고, 고른 상태만 밝아지게 둔다(SetLayout이 흰색과 섞는다).
    //   글자는 흰색이라 어두운 바탕에서 잘 읽힌다.
    private static readonly Color NavTint = new Color(0.24f, 0.29f, 0.38f);
    private static readonly Color ExitTint = new Color(0.40f, 0.20f, 0.22f);
    private static readonly Color AdjTint = new Color(0.24f, 0.26f, 0.30f);
    private static readonly Color TargetTint = new Color(0.30f, 0.27f, 0.20f);
    private static readonly Color UndoTint = new Color(0.33f, 0.25f, 0.20f);
    private static readonly Color ActiveTint = new Color(0.34f, 0.26f, 0.16f);   // 능동 — 마커 주황의 어두운 쪽
    private static readonly Color PressTint = new Color(0.32f, 0.20f, 0.32f);    // 압박 — 마커 자홍의 어두운 쪽
    private static readonly Color NeedleTint = new Color(0.18f, 0.29f, 0.30f);   // 바늘 — 표시물 청록의 어두운 쪽

    /// <summary>
    /// 단계별 판 배치. 0행은 머리줄이고, 판 <b>폭은 쓴 칸 수만큼만</b> 잡힌다(09-21).
    ///
    /// ★09-21 사용자: "빈 공간이 너무 많다 · 패널 크기 자체를 줄여라."
    ///   그래서 ①줄마다 가로를 꽉 채우고 ②줄 수를 줄이고 ③라벨을 짧게 했다.
    ///   ★<b>라벨 길이가 곧 판 폭이다</b> — 한글 한 글자가 대략 글자 크기 하나만큼 넓어서,
    ///   "능동 기록"(4자)은 "능동"(2자)의 두 배 칸을 먹는다. 긴 이름을 쓰면 판이 그만큼 커진다.
    /// ★[나가기]는 모서리를 피해 <b>가운데</b>에 두고 길게 눌러야 먹는다(09-21) — 스친 손에 눌리지 않게.
    /// </summary>
    private void ApplyStepButtons()
    {
        var items = new List<RomMenuItem>(14);
        const float w2 = 1.8f, gap = 0.1f;     // 두 글자 버튼 폭과 사이
        float c1 = 0f, c2 = w2 + gap;      // 두 열이면 충분하다 — 셋째 열을 쓰면 판이 그만큼 넓어진다
        float navRow;

        switch (step)
        {
            case RomRecordStep.Setup:
                items.Add(RomMenuItem.Text("정면", c1, 1, 1.4f));
                items.Add(RomMenuItem.Button("yaw-", "↺", 1.5f, 1, 1f, AdjTint, repeat: true));
                items.Add(RomMenuItem.Button("yaw+", "↻", 2.6f, 1, 1f, AdjTint, repeat: true));
                items.Add(RomMenuItem.Text("높이", c1, 2, 1.4f));
                items.Add(RomMenuItem.Button("h+", "▲", 1.5f, 2, 1f, AdjTint, repeat: true));
                items.Add(RomMenuItem.Button("h-", "▼", 2.6f, 2, 1f, AdjTint, repeat: true));
                navRow = 3;
                break;

            case RomRecordStep.Landmarks:
                items.Add(RomMenuItem.Button("t0", "대추", c1, 1, w2, TargetTint, selected: landmarkTarget == 0));
                items.Add(RomMenuItem.Button("t1", "미간", c2, 1, w2, TargetTint, selected: landmarkTarget == 1));
                items.Add(RomMenuItem.Button("t2", "목중앙", c1, 2, w2, TargetTint, selected: landmarkTarget == 2));
                // ★정면을 다시 잡는 버튼(09-21) — 자동은 처음 한 번뿐이라 여기서 고쳐 잡는다.
                if (hasC7 && hasGlab) items.Add(RomMenuItem.Button("aim", "정면", c2, 2, w2, NavTint));
                if (landmarkTarget == 2)
                {
                    // 목 중앙은 앞·뒤로만 옮긴다(미간은 그대로)
                    items.Add(RomMenuItem.Button("fwd", "앞", c1, 3, w2, AdjTint, repeat: true));
                    items.Add(RomMenuItem.Button("back", "뒤", c2, 3, w2, AdjTint, repeat: true));
                    navRow = 4;
                }
                else
                {
                    // ★★방향 버튼 여섯 개를 없앴다(09-21 "손이 너무 많이 가면 안 돼").
                    //   점은 <b>손으로 잡아 끌면</b> 되므로 그 여섯 줄은 판만 세로로 길게 만들고 시야를 가렸다.
                    navRow = 3;
                }
                break;

            case RomRecordStep.Done:
                navRow = 1;
                break;

            default:   // 굴곡·신전·측굴·회전
                // ★★바늘을 쓰면 버튼이 거의 필요 없다(09-21 "손이 너무 많이 가면 안 돼").
                //   끌어낸 자리가 곧 기록이라 [기록]이 없고, ±1°도 바늘을 다시 끌면 된다.
                items.Add(RomMenuItem.Button("needle", "바늘", c1, 1, w2, NeedleTint, selected: needleOn));
                if (needleOn)
                {
                    items.Add(RomMenuItem.Button("nreset", "다시", c2, 1, w2, UndoTint));
                    navRow = 2;
                }
                else
                {
                    // 점찍기로 쓸 때만 다듬기 버튼을 낸다.
                    items.Add(RomMenuItem.Button("undo", "취소", c2, 1, w2, UndoTint));
                    items.Add(RomMenuItem.Button("adj-", "-1°", c1, 2, w2, AdjTint, repeat: true));
                    items.Add(RomMenuItem.Button("adj+", "+1°", c2, 2, w2, AdjTint, repeat: true));
                    navRow = 3;
                }
                break;
        }

        // ★[이전][다음]을 한 줄에 두고 [나가기]는 그 아래 가운데다(09-21).
        //   한 줄에 셋을 넣으면 판이 6칸을 넘어 오히려 넓어진다 — 판 폭은 <b>가장 넓은 줄</b>이 정한다.
        if (step > RomRecordStep.Setup) items.Add(RomMenuItem.Button("prev", "이전", c1, navRow, w2, NavTint));
        if (step < RomRecordStep.Done) items.Add(RomMenuItem.Button("next", "다음", c2, navRow, w2, NavTint));
        items.Add(RomMenuItem.Button("exit", "나가기", 0.55f, navRow + 1f, 2.6f, ExitTint, holdSeconds: exitHoldSeconds));

        var arr = items.ToArray();
        string head = MenuHeader();
        leftMenu.SetLayout(head, arr);
        // ★고정 모드에서는 둘이 같은 판이다 — 두 번 짜지 않는다.
        if (!ReferenceEquals(rightMenu, leftMenu)) rightMenu.SetLayout(head, arr);
    }

    /// <summary>판 머리줄 — 단계·진행, 동작 단계면 지금까지 찍은 값. 값이 바뀔 때만 만든다.</summary>
    private string MenuHeader()
    {
        sb.Clear();
        sb.Append((int)step + 1).Append("/7  ").Append(StepTitle[(int)step]);
        if (IsMotion(step))
        {
            sb.Append("   ");
            if (needleOn) AppendNeedleValues(sb, step);
            else
            {
                var list = marks[MotionIndex(step)];
                Vector3 pivot = Pivot;
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append(" · ");
                    sb.Append(Kind(list[i])).Append(' ');
                    AppendDeg(sb, step, list[i], pivot);
                }
                if (list.Count == 0) sb.Append('—');
            }
        }
        else if (step == RomRecordStep.Landmarks && landmarkTarget == 2)
        {
            sb.Append("   목중앙 ").Append(neckOffsetMm.ToString("F0")).Append("mm");
        }
        return sb.ToString();
    }

    /// <summary>
    /// 요약 줄에 쓸 각 하나. ★바늘은 눈금 각, 점은 중립선 기준 각이다 — <b>다른 수</b>라서 섞어 적으면 안 된다.
    /// 미간이 없으면 점은 각이 없다(0°가 아니라 «—»다).
    /// </summary>
    /// <summary>
    /// 그 단계의 <b>바늘 값</b>을 붙인다(09-21). ★끌어낸 바늘만 값이다 — 0°에 남은 것은 아직 안 잰 것이다.
    /// ★눈금 0은 단계마다 다르므로(연직 / 회전은 정면) 현재 단계가 아니어도 s로 직접 구한다.
    /// </summary>
    private void AppendNeedleValues(StringBuilder b, RomRecordStep s)
    {
        Vector3 z = RomRecordGeometry.ScaleZero(s, yaw);
        int mi = MotionIndex(s);
        int count = NeedleCountOf(s);
        bool any = false;
        for (int k = 0; k < count; k++)
        {
            int n = mi * NeedlePerStep + k;
            if (!needleMoved[n]) continue;
            b.Append(any ? " · " : "").Append(NeedleName(k)).Append(' ')
             .Append(Vector3.Angle(z, needleDir[n]).ToString("F0")).Append('°');
            any = true;
        }
        if (!any) b.Append('—');
    }

    private void AppendDeg(StringBuilder b, RomRecordStep s, RomRecordMark m, Vector3 pivot)
    {
        if (m.byNeedle) b.Append(m.dialDeg.ToString("F0")).Append('°');
        else if (hasGlab) b.Append(AngleOf(s, m, pivot).ToString("F0")).Append('°');
        else b.Append('—');
    }

    private void OnButton(string id)
    {
        // ★어느 버튼이 눌렸는지 남긴다(09-21). 종전엔 "버튼 누른 직후"만 있고 <b>무엇을 눌렀는지가 없어</b>
        //   5분짜리 판에서 왜 동작 단계로 못 갔는지를 로그로 가를 수가 없었다.
        if (id != "yaw-" && id != "yaw+" && id != "h+" && id != "h-" &&
            id != "fwd" && id != "back" && id != "left" && id != "right" && id != "up" && id != "down" &&
            id != "adj+" && id != "adj-")
            Debug.Log($"[실측기록] 버튼 [{id}] — 단계 {StepTitle[(int)step]}");

        Vector3 f = RomRecordGeometry.Forward(yaw), r = RomRecordGeometry.Right(yaw), u = Vector3.up;
        float mm = nudgeStepMm * 0.001f;
        switch (id)
        {
            case "yaw-": yaw -= yawStepDeg; break;
            case "yaw+": yaw += yawStepDeg; break;
            case "h+": if (!hasC7) frameOrigin += u * (heightStepMm * 0.001f); break;
            case "h-": if (!hasC7) frameOrigin -= u * (heightStepMm * 0.001f); break;
            case "t0": SetTarget(0); break;
            case "t1": SetTarget(1); break;
            case "t2": SetTarget(2); break;
            case "aim":
                frontAimed = false;      // 다시 잡게 풀어 준다
                AimFrontFromLandmarks();
                break;
            case "up": Nudge(u * mm); break;
            case "down": Nudge(-u * mm); break;
            case "fwd": if (landmarkTarget == 2) neckOffsetMm += neckStepMm; else Nudge(f * mm); break;
            case "back": if (landmarkTarget == 2) neckOffsetMm -= neckStepMm; else Nudge(-f * mm); break;
            case "left": Nudge(-r * mm); break;
            case "right": Nudge(r * mm); break;
            case "adj+": AdjustLast(+adjustStepDeg); break;
            case "adj-": AdjustLast(-adjustStepDeg); break;
            case "undo": UndoLast(); break;
            case "needle":
                needleOn = !needleOn;
                lastNeedleDegShown[0] = lastNeedleDegShown[1] = -999;
                Debug.Log("[실측기록] 바늘 " + (needleOn ? "켬" : "끔"));
                ApplyStepButtons();
                break;
            case "nreset": ResetNeedles(); break;
            case "prev": GoTo(step - 1); break;
            case "next": GoTo(step + 1); break;
            case "exit": Exit(); break;
        }
        if (id == "fwd" || id == "back")
            if (landmarkTarget == 2) Debug.Log($"[실측기록] 목 중앙 보정 {neckOffsetMm:F0}mm");
        dirty = true;
    }

    /// <summary>
    /// 환자 정면을 <b>대추 → 미간</b>으로 잡는다(2026-09-21 사용자 제안:
    /// "처음에 그냥 생성 바로 대추혈 찍으면 되는 거 아닌가?").
    ///
    /// ★대추는 목 뒤, 미간은 이마 앞이다. 그 사이 벡터의 <b>수평 성분</b>이 곧 환자가 보는 쪽이다.
    ///   그래서 기준선 세팅 단계에서 정면을 손으로 맞출 필요가 없다 — 두 점을 찍으면 저절로 정해진다.
    /// ★<b>지움</b>: 위아래 성분을 버린다(ProjectOnPlane). 미간이 대추보다 얼마나 높든 정면 방향은
    ///   수평이어야 하므로 의도한 것이다. 대신 <b>환자가 고개를 숙이거나 든 채로 찍으면</b>
    ///   수평 성분이 짧아져 정면이 흔들린다 — 그래서 중립에서 찍으라 하고, ↺↻로 다듬게 남긴다.
    /// ★부호를 추론으로 정하지 않았다(규칙 9). 두 점의 차라 뒤집힐 여지가 없다.
    ///   그래도 <b>Play에서 '환자 앞' 글자가 실제 환자 앞을 가리키는지 눈으로 봐야 한다.</b>
    /// </summary>
    private bool frontAimed;   // 정면을 이미 한 번 잡았나

    private void AimFrontFromLandmarks()
    {
        if (!hasC7 || !hasGlab) return;
        // ★★09-21 실측: 미간을 찍을 때마다 다시 잡았더니 정면이 347→1→97→172→121→258→156→316→133°로
        //   판마다 튀었다. 사용자가 ↺↻로 맞춰 놔도 다시 찍으면 날아가니 악순환이었다.
        //   → <b>처음 한 번만</b> 잡는다. 다시 잡고 싶으면 [정면] 버튼을 누른다.
        if (frontAimed)
        {
            Debug.Log("[실측기록] 정면은 이미 잡혀 있다 — 다시 잡으려면 [정면]을 누른다.");
            return;
        }
        Vector3 flat = Vector3.ProjectOnPlane(glab - c7, Vector3.up);
        if (flat.sqrMagnitude < 1e-4f)
        {
            Debug.Log("[실측기록] ★대추와 미간이 거의 수직으로 겹쳐 정면을 못 잡았다 — ↺↻로 맞춘다.");
            return;
        }
        float before = yaw;
        yaw = Quaternion.LookRotation(flat.normalized, Vector3.up).eulerAngles.y;
        frontAimed = true;
        // ★미간이 대추보다 낮으면 잘못 찍었을 가능성이 크다 — 중립에서는 이마가 목 뒤보다 위다.
        string warn = glab.y < c7.y ? " ★미간이 대추보다 낮다 — 잘못 찍었는지 본다" : "";
        Debug.Log($"[실측기록] 환자 정면을 대추→미간으로 잡았다 — {before:F0}° → {yaw:F0}° " +
                  $"(수평 거리 {flat.magnitude * 100f:F0}cm · ↺↻로 다듬는다){warn}");
    }

    private void SetTarget(int t)
    {
        if (landmarkTarget == t) return;
        landmarkTarget = t;
        ApplyStepButtons();   // 고른 버튼을 밝히고, 목중앙이면 앞·뒤만 남긴다
    }

    private void Nudge(Vector3 d)
    {
        if (landmarkTarget == 0 && hasC7) c7 += d;
        else if (landmarkTarget == 1 && hasGlab) glab += d;
    }

    private void GoTo(RomRecordStep s)
    {
        if (s < RomRecordStep.Setup || s > RomRecordStep.Done) return;
        // ★대추만 필수로 바꿨다(09-21). 바늘은 미간 없이도 재는 수단이라 미간으로 막으면 그 길이 닫힌다.
        //   미간이 없으면 점찍기 각(중립선 기준)은 못 내고 바늘 눈금 각만 남는다 — 안내판이 그것을 말한다.
        if (IsMotion(s) && !hasC7)
        {
            Play(sndDeny);
            Debug.Log("[실측기록] 대추를 먼저 찍는다 — 동작 단계로 못 넘어간다.");
            return;
        }
        if (IsMotion(s) && !hasGlab)
            Debug.Log("[실측기록] ★미간이 없다 — 점찍기는 각이 안 나온다. 바늘로 눈금 각만 기록된다.");
        step = s;
        ApplyStepButtons();
        Debug.Log($"[실측기록] 단계 → {StepTitle[(int)step]}");
        if (step == RomRecordStep.Done) LogSummary();
    }

    private void Exit()
    {
        LogSummary();
        Debug.Log("[실측기록] 나가기 → " + lobbySceneName);
        SceneLoader.LoadScene(lobbySceneName);
    }

    // ── 핀치 ─────────────────────────────────────────────────────────
    private void HandlePinch(bool isLeft)
    {
        // ★한 번에 한 손만. 다른 손이 오므리고 있으면 이 손은 새로 시작하지 않는다.
        bool otherBusy = liveActive && livePinchLeft != isLeft;

        // ★버튼을 누르는 동안과 누른 직후에는 핀치를 받지 않는다(09-18 로그: 의도하지 않은 핀치 30여 번).
        //   판을 누르러 다가오는 중(고정 상태)이면 양손 모두 막는다 — 가려진 손이 핀치로 읽히는 것을 막는다.
        // ★고정 모드에서는 «판 근처»로 막지 않는다(09-21) — 판이 손에서 떨어져 있어 우연히 겹칠 일이 없고,
        //   오히려 판을 <b>잡아 끌려면</b> 판 근처에서 핀치가 돼야 한다. 그 차단이 간섭의 주범이었다.
        bool nearMenu = !menuFixedInSpace
                        && hands.TryJoint(isLeft, HandJointId.HandIndexTip, out Vector3 tip)
                        && (leftMenu.Near(tip, 0.06f) || rightMenu.Near(tip, 0.06f));

        // ★막은 이유를 문구로 넘긴다(09-21) — 막는 자리가 넷인데 셋이 조용해 원인을 못 갈랐다.
        string blockedBy = otherBusy ? "다른 손이 잡는 중"
                         : menuHoldL || menuHoldR ? "판에 손이 다가옴(판 고정 중)"
                         : Time.unscaledTime - lastPressTime < 0.4f ? "버튼 누른 직후"
                         : nearMenu ? "판 근처" : null;

        int ev = hands.UpdatePinch(isLeft, blockedBy, out Vector3 fixedPos);
        var st = isLeft ? hands.LeftPinch : hands.RightPinch;
        ReportPinch(isLeft, st);

        if (ev == 1)
        {
            liveActive = true;
            livePinchLeft = isLeft;
            // ★잡을 것이 있으면 «찍기»가 아니라 «잡아 끌기»다. 없으면 종전대로 찍는다.
            dragTarget = FindDragTarget(st.current, out dragStartValue);
            if (dragTarget >= 0)
            {
                dragGrabbedAt = st.current;
                Play(sndPress);
                Debug.Log($"[실측기록] 잡았다 — {DragName(dragTarget)}");
            }
        }
        if (liveActive && livePinchLeft == isLeft)
        {
            if (st.closed)
            {
                if (dragTarget >= 0) ApplyDrag(st.current);
                else view.SetLive(true, st.current);
            }
            if (ev == 2)
            {
                liveActive = false;
                view.SetLive(false, Vector3.zero);
                if (dragTarget >= 0) EndDrag();
                else OnPinchFixed(fixedPos);
            }
            else if (ev == 3 || ev == 4)
            {
                liveActive = false;
                view.SetLive(false, Vector3.zero);
                // ★놓친 드래그는 되돌리지 않는다 — 옮긴 자리를 그대로 둔다(되돌리면 더 놀란다).
                if (dragTarget >= 0) EndDrag();
                else if (ev == 3) Debug.Log("[실측기록] 핀치 취소 — 손을 놓쳤거나 시스템 제스처가 시작됐다.");
            }
        }
    }

    /// <summary>
    /// ★막힌 이유를 2초에 한 줄만 말한다(자기 침묵 — 잘 잡히면 한 줄도 안 남는다).
    /// "위에서 잡으면 안 잡힌다"(09-21)를 가르려고 <b>손 높이·판까지 거리·신뢰도</b>를 같이 찍는다.
    /// </summary>
    private void ReportPinch(bool isLeft, RomRecordHands.PinchState st)
    {
        string r = st.blockReason;
        if (r == null) return;
        // ★세는 것은 매번, 말하는 것은 손별로 2초에 한 번. 요약에서 <b>종류별 횟수</b>로 본다 —
        //   한 줄씩 쏟아 놓고 눈으로 세면 오판한다(09-21에 11,000줄을 쏟아 놓고 그랬다).
        pinchBlockCount.TryGetValue(r, out int n);
        pinchBlockCount[r] = n + 1;

        int k = isLeft ? 0 : 1;
        if (Time.unscaledTime < nextPinchLog[k]) return;
        nextPinchLog[k] = Time.unscaledTime + 2f;

        float eyeDy = eye != null ? (st.tip.y - eye.position.y) * 100f : 0f;
        float toMenu = Mathf.Min(leftMenu.Distance(st.tip), rightMenu.Distance(st.tip));
        Debug.Log($"[실측기록·핀치] 안 받는다 — {(isLeft ? "왼" : "오른")}손 «{r}» · " +
                  $"간격 {(st.gap >= 0f ? (st.gap * 100f).ToString("F1") + "cm" : "—")} · " +
                  $"신뢰 {(st.highConfidence ? "O" : "★낮음")} · 눈높이 대비 {eyeDy:+0;-0}cm · " +
                  $"판까지 {(toMenu < 9f ? (toMenu * 100f).ToString("F0") + "cm" : "—")}");
    }

    // ── 잡아 끌기(09-21 신설) ─────────────────────────────────────────
    private static string DragName(int t) =>
        t == 0 ? "기준선(3축)" : t == 1 ? "대추" : t == 2 ? "미간" : t == 9 ? "조작 판" : "바늘";

    /// <summary>핀치를 오므린 자리에서 잡을 수 있는 것을 고른다. 없으면 -1(그러면 «찍기»로 간다).</summary>
    private int FindDragTarget(Vector3 p, out Vector3 startValue)
    {
        startValue = default;
        // ★바늘 손잡이가 먼저다(09-21) — 동작 단계에서 바늘 끝을 잡으면 마커를 찍지 않는다.
        //   바늘이 둘이라 <b>가까운 쪽</b>을 잡는다.
        if (NeedleVisible(out Vector3 nc, out _, out _, out _, out float nh, out int mi))
        {
            int count = NeedleCountOf(step);
            // ★이미 끌어낸 바늘부터 본다 — 손잡이가 흩어져 있어 어느 것을 집는지 분명하다.
            int best = -1;
            float bestD = needleGrabRadius;
            for (int k = 0; k < count; k++)
            {
                int n = mi * NeedlePerStep + k;
                if (!needleMoved[n]) continue;
                float d = Vector3.Distance(p, nc + needleDir[n] * nh);
                if (d <= bestD) { bestD = d; best = k; }
            }
            // ★아직 안 끌어낸 것들은 0°에 포개져 있다 — 그중 <b>첫째</b>를 집는다. 그래서 하나씩 꺼내진다.
            if (best < 0)
            {
                for (int k = 0; k < count; k++)
                {
                    int n = mi * NeedlePerStep + k;
                    if (needleMoved[n]) continue;
                    if (Vector3.Distance(p, nc + needleDir[n] * nh) <= needleGrabRadius) { best = k; break; }
                }
            }
            if (best >= 0) return 3 + best;   // 3·4·5·6 = 바늘 0·1·2·3
        }
        // ★조작 판도 잡아 끈다(09-21 고정 모드) — 자리가 마음에 안 들면 옮긴다.
        if (menuFixedInSpace && leftMenu != null && leftMenu.Placed
            && Vector3.Distance(p, leftMenu.Position) <= menuGrabRadius)
        {
            startValue = leftMenu.Position;
            return 9;   // ★바늘이 3~6을 쓴다
        }
        if (step == RomRecordStep.Setup)
        {
            if (Vector3.Distance(p, frameOrigin) > axesGrabRadius) return -1;
            startValue = frameOrigin;
            return 0;
        }
        if (step != RomRecordStep.Landmarks) return -1;

        // 찍혀 있는 점만 잡는다. 아직 안 찍은 것은 종전대로 핀치로 찍는다.
        float dC7 = hasC7 ? Vector3.Distance(p, c7) : float.MaxValue;
        float dGl = hasGlab ? Vector3.Distance(p, glab) : float.MaxValue;
        if (dC7 > dotGrabRadius && dGl > dotGrabRadius) return -1;
        if (dC7 <= dGl) { startValue = c7; return 1; }
        startValue = glab;
        return 2;
    }

    /// <summary>
    /// 바늘이 지금 보이나. 보이면 기하와 <b>손잡이 반지름</b>을 준다. 바늘 둘이 같은 기하를 쓴다.
    /// ★손잡이 반지름은 눈금 반지름보다 <see cref="needleHandleOut"/>만큼 <b>더 밖</b>이다 —
    ///   눈금 반지름은 회전중심에서 미간까지라 그 자리에 손잡이를 두면 <b>사람 머리 안에 묻힌다</b>(09-21).
    /// </summary>
    private bool NeedleVisible(out Vector3 center, out Vector3 normal, out Vector3 zero,
                               out float radius, out float handleRadius, out int mi)
    {
        mi = 0;
        handleRadius = needleFallbackRadius + needleHandleOut;
        if (!needleOn || !DialFrame(out center, out normal, out zero, out radius))
        {
            center = default; normal = Vector3.up; zero = Vector3.forward; radius = needleFallbackRadius;
            return false;
        }
        handleRadius = radius + needleHandleOut;
        mi = MotionIndex(step);
        for (int k = 0; k < NeedlePerStep; k++)
        {
            int n = mi * NeedlePerStep + k;
            if (needlePlaced[n]) continue;
            // ★전부 눈금 0(연직 · 회전은 정면)에 포개 세운다(09-21 사용자: "0도 위치에서 하나씩만 나와야").
            //   겹쳐 있어도 «아직 안 끌어낸 것 중 첫째»를 집으므로 하나씩 꺼내진다.
            needleDir[n] = zero;
            needlePlaced[n] = true;
        }
        return true;
    }

    /// <summary>그 단계에서 쓰는 바늘 수 — 굴곡·신전은 둘, 측굴·회전은 넷(좌우가 있어서).</summary>
    private int NeedleCountOf(RomRecordStep s) => HasSides(s) ? 4 : 2;

    // ★이름을 짧게 쓴다(09-21 녹화 실측) — "능동2/압박2"는 네 글자가 서로 겹쳐 읽을 수가 없었다.
    private static string NeedleName(int k) => k == 0 ? "능1" : k == 1 ? "압1" : k == 2 ? "능2" : "압2";

    private void ApplyDrag(Vector3 p)
    {
        // ★바늘은 델타가 아니라 <b>손이 있는 쪽</b>을 향한다. 단면에 투영해 그 면에서만 돌게 한다.
        if (dragTarget >= 3 && dragTarget <= 6)
        {
            if (NeedleVisible(out Vector3 nc, out Vector3 nn, out _, out _, out _, out int mi))
            {
                int n = mi * NeedlePerStep + (dragTarget - 3);
                needleDir[n] = RomRecordGeometry.OnPlane(p - nc, nn, needleDir[n]);
                needleMoved[n] = true;   // ★끌어낸 순간부터 값이다 — 따로 [기록]을 누르지 않는다
                DrawNeedle();
            }
            return;
        }

        Vector3 v = dragStartValue + (p - dragGrabbedAt);
        switch (dragTarget)
        {
            case 0: frameOrigin = v; break;
            case 1: c7 = v; break;
            case 2: glab = v; break;
            case 9: leftMenu.MoveTo(v); return;   // 판은 표시물과 무관하다 — 다시 그릴 것이 없다
        }
        // ★끄는 동안은 가벼운 갱신만 한다 — 안내판·머리줄 문자열을 매 프레임 새로 만들지 않는다(VR 프레임 예산).
        Vector3 pivot = Pivot;
        view.SetFrame(pivot, yaw, axisLength);
        view.SetLandmarks(hasC7, c7, hasGlab, glab, pivot);
    }

    private void EndDrag()
    {
        if (dragTarget >= 3 && dragTarget <= 6 && NeedleVisible(out _, out _, out Vector3 z, out _, out _, out int mi))
        {
            int k = dragTarget - 3;
            int n = mi * NeedlePerStep + k;
            // ★놓은 자리가 곧 기록이다 — 따로 누를 것이 없다. 다시 끌면 고쳐진다.
            Debug.Log($"[실측기록] 기록 {StepTitle[(int)step]} {NeedleName(k)}(바늘) " +
                      $"눈금 {Vector3.Angle(z, needleDir[n]):F1}° · " +
                      $"{(hasGlab ? "중립 기준 " + NeedleNeutralDeg(n, mi).ToString("F1") + "°" : "미간 없음")}");
        }
        else
        {
            Vector3 at = dragTarget == 0 ? frameOrigin
                       : dragTarget == 1 ? c7
                       : dragTarget == 9 ? leftMenu.Position : glab;
            Debug.Log($"[실측기록] 놓았다 — {DragName(dragTarget)} {Fmt(at)}");
        }
        dragTarget = -1;
        Play(sndPinch);
        dirty = true;
    }

    // ── 바늘(09-21 신설) ─────────────────────────────────────────────
    private readonly int[] lastNeedleDegShown = { -999, -999, -999, -999 };

    /// <summary>그 바늘의 중립선 기준 각(미간이 있을 때만 뜻이 있다).</summary>
    private float NeedleNeutralDeg(int n, int mi)
    {
        if (!hasGlab || !DialFrame(out Vector3 c, out Vector3 nrm, out _, out float r)) return 0f;
        return RomRecordGeometry.PlaneAngle(Pivot, glab, c + needleDir[n] * r, nrm);
    }

    /// <summary>바늘 넷을 그린다. ★끄는 동안 매 프레임 불린다 — 각이 1° 넘게 바뀔 때만 글자를 새로 만든다.</summary>
    private void DrawNeedle()
    {
        if (!NeedleVisible(out Vector3 c, out _, out Vector3 z, out float r, out float h, out int mi))
        {
            for (int k = 0; k < NeedlePerStep; k++)
            {
                view.SetNeedle(k, false, Vector3.zero, Vector3.up, 0.1f, 0.2f, null, false);
                lastNeedleDegShown[k] = -999;
            }
            return;
        }
        int count = NeedleCountOf(step);
        for (int k = 0; k < NeedlePerStep; k++)
        {
            if (k >= count)
            {
                view.SetNeedle(k, false, Vector3.zero, Vector3.up, 0.1f, 0.2f, null, false);
                continue;
            }
            Vector3 dir = needleDir[mi * NeedlePerStep + k];
            int d = Mathf.RoundToInt(Vector3.Angle(z, dir));
            string label = null;
            if (d != lastNeedleDegShown[k])
            {
                lastNeedleDegShown[k] = d;
                // ★아직 0°에 있는 바늘은 이름만 보인다 — 값이 아니라 «꺼내 쓸 것»이라는 표시다.
                label = needleMoved[mi * NeedlePerStep + k] ? NeedleName(k) + " " + d + "°" : NeedleName(k);
            }
            view.SetNeedle(k, true, c, dir, r, h, label, dragTarget == 3 + k);
        }
    }

    /// <summary>
    /// 이 단계의 바늘을 전부 0°로 되돌린다([다시]). ★끌어낸 자리가 곧 기록이라 «지우기»가 이것이다.
    /// </summary>
    private void ResetNeedles()
    {
        if (!NeedleVisible(out _, out _, out Vector3 z, out _, out _, out int mi))
        {
            Play(sndDeny);
            Debug.Log("[실측기록] 바늘이 없다 — 대추를 먼저 찍고 동작 단계로 온다.");
            return;
        }
        for (int k = 0; k < NeedlePerStep; k++)
        {
            int n = mi * NeedlePerStep + k;
            needleDir[n] = z;
            needleMoved[n] = false;
            lastNeedleDegShown[k] = -999;
        }
        Play(sndUndo);
        Debug.Log($"[실측기록] {StepTitle[(int)step]} 바늘을 모두 0°로 되돌렸다.");
        dirty = true;
    }

    private void OnPinchFixed(Vector3 p)
    {
        switch (step)
        {
            case RomRecordStep.Setup:
                Play(sndDeny);
                // ★잡을 만큼 가까이 가지 않았다는 뜻이다(09-21). 3축 근처에서 오므리면 잡혀서 따라온다.
                Debug.Log($"[실측기록] 기준선을 못 잡았다 — 3축 중심에서 {axesGrabRadius * 100f:F0}cm 안에서 오므려야 잡힌다.");
                return;

            case RomRecordStep.Landmarks:
                if (landmarkTarget == 0)
                {
                    c7 = p; hasC7 = true;
                    landmarkTarget = 1;                       // 대추 다음은 미간
                    ApplyStepButtons();
                    Play(sndPinch);
                    Debug.Log($"[실측기록] 대추 {Fmt(c7)}");
                }
                else if (landmarkTarget == 1)
                {
                    glab = p; hasGlab = true;
                    Play(sndPinch);
                    Debug.Log($"[실측기록] 미간(중립) {Fmt(glab)}");
                    AimFrontFromLandmarks();
                }
                else { Play(sndDeny); Debug.Log("[실측기록] 대상이 목 중앙일 때는 앞·뒤 버튼으로 옮긴다(찍지 않는다)."); }
                dirty = true;
                return;

            case RomRecordStep.Done:
                return;
        }

        // ★바늘 모드에서는 점을 찍지 않는다(09-21 실측). 바늘을 쓰는 판에서 손잡이 반경 밖을 오므리면
        //   점이 찍혀 "굴곡은 2개까지다"까지 떴다 — 사용자는 바늘을 맞추려던 참이었다. 둘이 섞이면 안 된다.
        if (needleOn)
        {
            Play(sndDeny);
            Debug.Log("[실측기록] 바늘 모드다 — 0°의 바늘을 끌어 맞춘다(점으로 찍으려면 [바늘]을 끈다).");
            return;
        }

        var list = marks[MotionIndex(step)];
        if (list.Count >= Capacity(step))
        {
            Play(sndDeny);
            Debug.Log($"[실측기록] {StepTitle[(int)step]}은 {Capacity(step)}개까지다 — [취소]로 지우고 다시 찍는다.");
            return;
        }

        var m = new RomRecordMark
        {
            raw = p,
            passive = list.Count % 2 == 1,               // 홀수번째(1,3) 능동 · 짝수번째(2,4) 수동
            side = HasSides(step) ? RomRecordGeometry.Side(glab, p, yaw, flipSides) : 0,
        };
        list.Add(m);
        Play(sndPinch);
        LogMark(step, m);
        dirty = true;
    }

    private void AdjustLast(float d)
    {
        if (!IsMotion(step)) return;
        var list = marks[MotionIndex(step)];
        if (list.Count == 0) return;
        var m = list[list.Count - 1];
        // ★바늘로 맞춘 것은 ±1°로 고치지 않는다 — 바늘을 다시 잡아 맞추는 것이 그 방식의 수정이다.
        if (m.byNeedle)
        {
            Play(sndDeny);
            Debug.Log("[실측기록] 바늘로 기록한 것은 ±1°로 못 고친다 — [취소]하고 바늘을 다시 맞춘다.");
            return;
        }
        if (!hasGlab)
        {
            Play(sndDeny);
            Debug.Log("[실측기록] 미간이 없어 각이 없다 — ±1°로 고칠 것이 없다.");
            return;
        }
        float before = AngleOf(step, m, Pivot);
        m.adjustDeg += d;
        // ★0° 아래로는 안 간다 — 중립을 넘어 반대쪽으로 도는 것은 수정이 아니다.
        float raw = RomRecordGeometry.PlaneAngle(Pivot, glab, m.raw, RomRecordGeometry.PlaneNormal(step, yaw));
        if (raw + m.adjustDeg < 0f) m.adjustDeg = -raw;
        Debug.Log($"[실측기록] 수정 {StepTitle[(int)step]} {Kind(m)} {before:F1}° → {AngleOf(step, m, Pivot):F1}° (누적 {m.adjustDeg:+0;-0;0}°)");
    }

    private void UndoLast()
    {
        if (!IsMotion(step)) return;
        var list = marks[MotionIndex(step)];
        if (list.Count == 0) return;
        var m = list[list.Count - 1];
        list.RemoveAt(list.Count - 1);
        Debug.Log($"[실측기록] 취소 {StepTitle[(int)step]} {Kind(m)}");
    }

    // ── 기록 ─────────────────────────────────────────────────────────
    private static string Kind(RomRecordMark m)
    {
        string side = m.side > 0 ? "우 " : m.side < 0 ? "좌 " : "";
        // ★바늘로 맞춘 것은 표시에서도 구분한다 — 점찍기와 섞이면 비교를 못 한다.
        return side + (m.passive ? "압박" : "능동") + (m.byNeedle ? "·바늘" : "");
    }

    private void LogMark(RomRecordStep s, RomRecordMark m)
    {
        string how = m.byNeedle ? "바늘" : "점";
        if (!hasGlab)
        {
            // ★미간이 없으면 중립선이 없어 중립 기준 각을 못 낸다 — 0을 각인 것처럼 적지 않는다.
            Debug.Log($"[실측기록] 기록 {StepTitle[(int)s]} {Kind(m)} ({how}) 눈금 {m.dialDeg:F1}° · " +
                      $"★미간이 없어 중립 기준 각은 못 낸다 · 자리 {Fmt(m.raw)}");
            return;
        }
        float atPivot = AngleOf(s, m, Pivot);
        float atC7 = Mathf.Max(0f, RomRecordGeometry.PlaneAngle(c7, glab, m.raw, RomRecordGeometry.PlaneNormal(s, yaw)) + m.adjustDeg);
        // ★두 기준을 같이 남긴다 — 대추 기준과 목 중앙 기준 중 어느 쪽이 실측에 가까운지 나중에 판별한다(사용자 09-18).
        Debug.Log($"[실측기록] 기록 {StepTitle[(int)s]} {Kind(m)} ({how}) {atPivot:F1}° (목중앙 {neckOffsetMm:F0}mm 기준) · " +
                  $"대추 기준 {atC7:F1}°{(m.byNeedle ? $" · 눈금 {m.dialDeg:F1}°" : "")} · " +
                  $"자리 {Fmt(m.raw)} · 수정 {m.adjustDeg:+0;-0;0}°");
    }

    private void LogSummary()
    {
        sb.Clear();
        sb.Append("[실측기록] 요약 — 목 중앙 보정 ").Append(neckOffsetMm.ToString("F0")).Append("mm\n");
        for (RomRecordStep s = RomRecordStep.Flexion; s <= RomRecordStep.Rotation; s++)
        {
            sb.Append("  ").Append(StepTitle[(int)s]).Append(": ");
            // ★바늘 값을 먼저 적는다 — 끌어낸 바늘이 곧 기록이다(09-21).
            sb.Append("[바늘] ");
            AppendNeedleValues(sb, s);
            sb.Append("   [점] ");
            var list = marks[MotionIndex(s)];
            if (list.Count == 0) sb.Append("—");
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(" · ");
                var mk = list[i];
                sb.Append(Kind(mk)).Append(' ');
                if (mk.byNeedle) sb.Append("눈금 ").Append(mk.dialDeg.ToString("F1")).Append('°');
                if (!hasGlab)
                {
                    if (!mk.byNeedle) sb.Append('—');
                    continue;
                }
                float c7a = Mathf.Max(0f, RomRecordGeometry.PlaneAngle(c7, glab, mk.raw, RomRecordGeometry.PlaneNormal(s, yaw)) + mk.adjustDeg);
                sb.Append(mk.byNeedle ? " / " : "").Append(AngleOf(s, mk, Pivot).ToString("F1"))
                  .Append("°(대추 ").Append(c7a.ToString("F1")).Append("°)");
            }
            sb.Append('\n');
        }
        // ★조용히 버려진 핀치를 여기서 한 번 센다(09-21) — 얼마나 걸러졌는지 판마다 남는다.
        sb.Append("  무시된 핀치: 너무 짧음 ").Append(hands.IgnoredShort)
          .Append(" · 추적 신뢰 낮음 ").Append(hands.IgnoredLowConfidence).Append('\n');
        if (pinchBlockCount.Count > 0)
        {
            sb.Append("  핀치를 막은 것(프레임 수):");
            foreach (var kv in pinchBlockCount) sb.Append(' ').Append(kv.Key).Append(' ').Append(kv.Value).Append(" ·");
            sb.Append('\n');
        }
        Debug.Log(sb.ToString());
    }

    private static string Fmt(Vector3 v) => $"({v.x:F3}, {v.y:F3}, {v.z:F3})";

    // ── 그리기(값이 바뀔 때만) ────────────────────────────────────────
    private void Redraw()
    {
        Vector3 pivot = Pivot;
        view.SetFrame(pivot, yaw, axisLength);
        view.SetLandmarks(hasC7, c7, hasGlab, glab, pivot);
        // ★대추를 찍기 전에는 3축을 숨긴다(09-21) — 그때 3축은 눈앞 허공의 임시 자리일 뿐이라
        //   보여 봐야 시야만 가린다. 대추를 찍으면 그 자리로 와서 그때부터 뜻이 생긴다.
        view.SetAxesVisible(hasC7 || step == RomRecordStep.Setup);

        // 손목 판 머리줄 — 지금까지 찍은 값(값이 바뀔 때만)
        string head = MenuHeader();
        leftMenu.SetHeader(head);
        if (!ReferenceEquals(rightMenu, leftMenu)) rightMenu.SetHeader(head);

        // ★각도기와 바늘은 같은 기하를 쓴다(DialFrame) — 따로 계산하면 미리보기가 거짓말을 한다(규칙 9).
        bool hasDial = DialFrame(out Vector3 dc, out Vector3 dn, out Vector3 dz, out float dr);
        if (hasDial) view.SetDial(true, dc, dn, dz, dr);
        else view.SetDial(false, Vector3.zero, Vector3.up, Vector3.forward, 0.1f);
        DrawNeedle();

        // ★단면 사각형(09-21) — 지금 재는 면이 어디인지 사람 크기만큼 네모로 보여 준다.
        //   회전은 눈금판이 미간 높이로 올라가 있으므로 사각형도 같은 중심을 쓴다.
        if (hasDial && showSectionPlane)
            view.SetSectionPlane(true, dc, dn, RomRecordGeometry.ScaleZero(step, yaw));
        else
            view.SetSectionPlane(false, Vector3.zero, Vector3.up, Vector3.up);

        var list = IsMotion(step) ? marks[MotionIndex(step)] : null;
        for (int i = 0; i < RomRecordVisual.MaxMarks; i++)
        {
            bool on = list != null && i < list.Count;
            if (!on) { view.SetMark(i, false, Vector3.zero, Vector3.zero, null, Color.white); continue; }
            var m = list[i];
            Vector3 n = RomRecordGeometry.PlaneNormal(step, yaw);
            // ★미간이 없으면 중립선이 없어 돌릴 기준도 없다 — 찍힌 자리를 그대로 둔다.
            Vector3 shown = hasGlab ? RomRecordGeometry.Adjusted(pivot, glab, m.raw, n, m.adjustDeg) : m.raw;
            // ★삼각면(09-21) — 점이 단면에서 벗어난 만큼이 «각에 못 쓴 성분»이다. 바늘은 면에 붙어 있어 뜻이 없다.
            Vector3 triC = !m.byNeedle && showOffPlaneTriangle && hasDial ? dc : Vector3.zero;
            Vector3 triN = !m.byNeedle && showOffPlaneTriangle && hasDial ? n : Vector3.zero;
            // ★바늘은 <b>눈금 각</b>을 보여 준다 — 사용자가 눈으로 읽어 맞춘 값이 그것이다.
            string deg = m.byNeedle ? m.dialDeg.ToString("F0") + "°"
                       : hasGlab ? AngleOf(step, m, pivot).ToString("F0") + "°"
                       : "—";
            string label = Kind(m) + " " + deg;
            view.SetMark(i, true, shown, pivot, label,
                         m.passive ? RomRecordVisual.PassiveColor : RomRecordVisual.ActiveColor, triC, triN);
        }

        view.SetPanel(pivot + Vector3.up * (axisLength + 0.18f), PanelText(pivot));
    }

    private string PanelText(Vector3 pivot)
    {
        sb.Clear();
        sb.Append('[').Append((int)step + 1).Append("/7] ").Append(StepTitle[(int)step]).Append('\n');
        switch (step)
        {
            case RomRecordStep.Setup:
                sb.Append("축을 손으로 잡아 끌어 목 높이에 두고, ↺↻로 '환자 앞'을 맞춘 뒤 [다음]\n");
                break;
            case RomRecordStep.Landmarks:
                sb.Append(!hasC7 ? "대추에 핀치하세요 (여기가 기준점이 됩니다)\n"
                        : !hasGlab ? "중립 자세에서 미간에 핀치하세요 (정면이 함께 정해집니다)\n"
                        : "점을 잡아 끌거나 버튼으로 다듬고 [다음]\n");
                sb.Append(landmarkTarget == 0 ? "대상: 대추" : landmarkTarget == 1 ? "대상: 미간" : "대상: 목중앙").Append(" · 목 중앙 보정 ").Append(neckOffsetMm.ToString("F0")).Append("mm\n");
                break;
            case RomRecordStep.Done:
                sb.Append("기록을 마쳤습니다\n");
                break;
            default:
                if (needleOn)
                {
                    // ★바늘은 끌어낸 자리가 곧 기록이다 — 누를 것이 없다(09-21).
                    int mi2 = MotionIndex(step), cnt = NeedleCountOf(step), done = 0;
                    for (int k = 0; k < cnt; k++) if (needleMoved[mi2 * NeedlePerStep + k]) done++;
                    sb.Append("0°의 바늘을 끌어 각을 맞추세요 (").Append(done).Append('/').Append(cnt).Append(")\n");
                }
                else
                {
                    var list = marks[MotionIndex(step)];
                    int next = list.Count;
                    if (next >= Capacity(step)) sb.Append("다 찍었습니다 — [다음]\n");
                    else sb.Append(next % 2 == 0 ? "능동" : "압박").Append(' ').Append(next + 1)
                           .Append('/').Append(Capacity(step)).Append(" 위치의 미간에 핀치하세요\n");
                }
                if (!hasGlab) sb.Append("★미간 없음 — 바늘 눈금 각만 기록됩니다\n");
                break;
        }
        sb.Append('\n');
        for (RomRecordStep s = RomRecordStep.Flexion; s <= RomRecordStep.Rotation; s++)
        {
            sb.Append(StepTitle[(int)s]).Append("  ");
            if (needleOn) AppendNeedleValues(sb, s);
            else
            {
                var list = marks[MotionIndex(s)];
                if (list.Count == 0) sb.Append('—');
                for (int i = 0; i < list.Count; i++)
                {
                    sb.Append(i > 0 ? " · " : "").Append(Kind(list[i])).Append(' ');
                    AppendDeg(sb, s, list[i], pivot);
                }
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
