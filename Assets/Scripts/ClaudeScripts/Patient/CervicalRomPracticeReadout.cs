using UnityEngine;
using TMPro;

/// <summary>
/// 실습(교육)모드의 <b>정보 디스플레이</b>. 실측모드와 같은 모양으로 <b>손 옆에</b> 띄운다.
///
/// <code>
///       굴곡  30도
///   진행 ■■■■■■□□□□ 3초
///  ●굴곡 ▶신전 ○좌측굴 ○우측굴 ○좌회전 ○우회전
/// </code>
///
/// ★<b>값을 새로 세지 않는다.</b> 셋 다 이미 있는 것을 읽어 그린다 —
///   각도는 <see cref="CervicalRomDriver.CurrentAngle"/>(대본 각),
///   게이지는 <see cref="ScenarioGuideUIController.ProgressRatio01"/>(ProgressCircle이 그리는 그 값),
///   방향 목록은 <see cref="CervicalRomDriver.GetMeasurement"/>다.
///   타이머를 새로 만들면 화면과 실제가 어긋난다 — 08-24에 실제로 겪은 형태다.
///
/// ★<b>ProgressCircle을 대신하지 않는다.</b> 같은 값을 두 자리에 그린다(2026-09-07 사용자 지시:
///   "프로그레스 타이머랑 손에 따라오는 정보의 게이지 둘 다 쓸 수 있게").
///   그래서 진행Root의 표시 규칙은 <b>한 줄도 건드리지 않았다</b> — 그건 13개 술기가 같이 쓴다.
///
/// ★실측모드에서는 스스로 접는다. 그쪽은 <see cref="CervicalRomRealityMeasure"/>가 같은 자리에
///   같은 모양을 그린다 — 둘 다 켜 두면 같은 글이 두 군데 뜬다(09-03에 지적받은 그 가독성 문제다).
///
/// ★★<b>붙이는 방법이 두 가지고, 성질이 다르다</b>(2026-09-07 정정):
///   ① <b>씬에 올린다</b>(권장) — 인스펙터에서 높이·글씨·배경을 <b>바꿔 저장하면 유지된다.</b>
///      ★그 대신 <b>씬 값이 코드 기본값을 이긴다</b>(규칙 7). 이후 아래 숫자를 고쳐도 안 먹는다.
///   ② 안 올려 두면 <see cref="CervicalRomScenarioBridge"/>가 <b>런타임에 붙인다</b>(예비 경로).
///      그때는 Play를 멈추면 인스턴스가 사라져 <b>인스펙터로 맞춘 값이 저장되지 않는다</b> —
///      매 Play마다 아래 코드 기본값으로 되돌아간다. 튜닝하려면 ①로 간다.
///   ★브리지는 <b>이미 있으면 새로 안 붙인다</b>. 그래서 씬에 하나 올려 두면 그게 그대로 쓰인다.
/// </summary>
public class CervicalRomPracticeReadout : MonoBehaviour
{
    [Header("=== 참조 (비우면 자동 탐색) ===")]
    [SerializeField] private CervicalRomDriver driver;
    [SerializeField] private CervicalGripJudge gripJudge;
    [SerializeField] private ScenarioGuideUIController guideUI;
    [Tooltip("이 브리지가 '지금 경추ROM 안인가'를 알려준다. 아니면 접는다.")]
    [SerializeField] private CervicalRomScenarioBridge bridge;
    [Tooltip("이게 켜져 있으면(=실측모드) 이 표시는 접는다.")]
    [SerializeField] private CervicalRomRealityMeasure realityMeasure;

    [Header("=== 표시 ===")]
    [SerializeField] private bool show = true;
    [Tooltip("첫 줄(방향 이름 + 현재 각도)을 그릴지.\n" +
             "★실습 각도기(CervicalRomPlaneGauge)가 <b>이미 현재각 숫자를 눈금 위에 그린다.</b>\n" +
             "  그래서 이 줄을 켜면 각도가 두 군데 뜬다 — 실측모드도 지금 같은 상태다.\n" +
             "  거슬리면 이것만 끈다. 게이지와 방향 목록은 남는다.")]
    [SerializeField] private bool showAngleLine = true;

    [Tooltip("방향 목록(●굴곡 ▶신전 ○측굴…)을 같이 그린다.\n" +
             "★<b>기본은 끔</b>이다(2026-09-08 사용자 지시: '정보는 현재각도 유지 게이지만 남기고 비활성화').\n" +
             "★<b>끄면 자세정렬·파지 단계에서 정보창이 빈다</b> — 그 단계에는 방향도 게이지도 없어\n" +
             "  이 줄이 유일하게 남던 줄이었다. 그 자리의 안내는 진행 칸이 맡는다.")]
    [SerializeField] private bool showDirectionRow;

    // ★★2026-09-15 사용자 요구 ③: "평가모드에서 가이드핸드가 없으니까 이게 파지가 제대로 된건지에
    //   대한 확신이 안 서서, 환자 압박할 때 손인식의 문제인지 접촉점 인식 문제인지 구분이 안 간다."
    //
    // ★이건 <b>답을 알려 주는 것이 아니다</b>. 어디를 어떻게 잡아야 하는지는 여전히 안 알려 준다 —
    //   지금 <b>시스템이 그 손을 인식하고 있는가</b>만 말한다. 09-14에 정한 원칙과 같은 줄이다
    //   ("유지 시간은 과제의 조건이지 답이 아니다"). 그래서 실습·평가 <b>둘 다</b>에 띄운다(사용자 지시).
    [Tooltip("파지 중인 손을 좌·우로 나눠 초록 체크로 보여준다. 쌍이 정해진 단계에서만 뜬다.")]
    [SerializeField] private bool showGripChecks = true;
    [Tooltip("★머리 본을 못 찾았을 때만 쓰는 예비값 — 목 뿌리 위로 이만큼 띄운다(m). " +
             "★2026-09-15부터 정상 경로는 이 값을 안 쓴다. readoutHeight·readoutOutward가 자리를 정한다.")]
    [SerializeField] private float headRise = 0.32f;

    // ★★2026-09-15 사용자 지시: "정보창 머리 위 말고 측면이랑 뒤통수에 붙이자. 너무 높다.
    //   딱 손 사이에 들어오면 좋은데 모델에 안 가려지게."
    //
    // ★<b>손이 잡은 두 점을 피한 자리가 곧 답이다.</b>
    //     시상면 쌍(이마·뒤통수)을 잡으면 → <b>옆면</b>이 빈다. 시술자도 옆에 서 있다.
    //     관상·횡단 쌍(좌·우 측두)을 잡으면 → <b>뒤통수</b>가 빈다. 시술자도 뒤에 서 있다.
    //   두 경우 모두 판이 <b>두 손 사이</b>에 놓인다 — 시상면이면 앞뒤 가운데(z=0),
    //   회전이면 좌우 가운데(x=0)다.
    // ★<b>가림은 "사용자가 있는 쪽으로 낸다"로 푼다.</b> 종전에는 높이(headRise)로 풀었고
    //   그래서 너무 높아졌다. 머리 반대편으로 내면 머리에 가리므로, 부호는 <b>카메라에 가까운 쪽</b>으로 고른다.
    //   ★홀더 스케일이 음수라 <b>방향 벡터로 부호를 고르면 뒤집힌다</b> —
    //     두 후보의 <b>월드 좌표를 직접 비교</b>한다(TransformPoint는 스케일을 탄다).
    // ★머리 본 로컬 실측(2026-09-15): 이마 z=+0.064 · 뒤통수 z=−0.068 · 측두 x=±0.05 ·
    //   접촉점 높이 y≈0.08~0.096. 그래서 머리 중심 높이를 0.09로 둔다. 추론이 아니라 잰 값이다.
    [Tooltip("머리 본 기준 높이(m). 머리 '위'가 아니라 머리 중심 높이다. 실측 접촉점 높이가 0.08~0.096이다.")]
    [SerializeField] private float readoutHeight = 0.11f;

    [Tooltip("머리 중심에서 바깥으로 이만큼(m). 옆면 또는 뒤통수 쪽으로 낸다. " +
             "작으면 머리에 파묻히고, 크면 손에서 멀어진다. 실측 머리 반지름이 5~7cm다.")]
    [SerializeField] private float readoutOutward = 0.1f;
    // ★[폐기 2026-09-07] pullToViewer·minDistance는 <b>카메라 위치</b>를 읽어 자리를 정하던 값이다.
    //   사용자 지시로 걷어냈다 — "사용자랑 거리 재지 말고 축에 따라가게 하고 방향만 회전시켜."
    //   가림은 headRise(높이)로 푼다. 되살리지 말 것.
    [Tooltip("비우면 CC_Base_Head를 찾는다.")]
    [SerializeField] private Transform headBone;
    [Tooltip("글씨 크기(m). 실측은 readoutSize 0.05 × readoutScaleOverride 2.6이다.")]
    [SerializeField] private float fontSize = 0.05f;
    [SerializeField] private float fontScale = 2.6f;
    [SerializeField] private TMP_FontAsset font;

    [Tooltip("게이지 앞에 붙는 말. 실측은 '정지'지만 실습에서 차는 것은 정지가 아니라 " +
             "파지 유지·압박 유지·시간이라 '진행'으로 적는다.")]
    [SerializeField] private string gaugeLabel = "진행";

    [Header("=== 안정화 ===")]
    [Tooltip("자리 저역통과 시간상수(초).\n" +
             "★2026-09-07부터 기본 0이다 — 자리가 환자 머리에 붙어 있어 떨릴 일이 없다.\n" +
             "  손을 따라가던 시절에는 필요했지만, 지금 걸면 굴곡·회전 중에 글자만 뒤늦게 따라온다.")]
    [SerializeField] private float positionSmoothing;

    [Header("=== 배경 ===")]
    [Tooltip("글자 뒤에 반투명 판을 깐다. 회색 글씨가 밝은 배경에 묻히는 것을 막는다.")]
    [SerializeField] private bool showBackdrop = true;
    [SerializeField] private Color backdropColor = new Color(0.06f, 0.07f, 0.10f, 0.72f);
    [Tooltip("글자 상자보다 이만큼 넉넉하게(글자 로컬 단위). 좌우.")]
    [SerializeField] private float backdropPadX = 5f;
    [Tooltip("글자 상자보다 이만큼 넉넉하게(글자 로컬 단위). 위아래.")]
    [SerializeField] private float backdropPadY = 3f;

    // ★2026-09-15 사용자 요청: "테두리 살짝만 곡선으로".
    // ★판은 Quad가 아니라 <b>모서리를 깎은 사각형 메시</b>를 직접 만든다.
    //   Quad에 스케일을 비균등으로 주면(가로가 세로보다 길다) 둥근 모서리가 <b>타원으로 늘어난다</b>.
    //   그래서 <b>실제 크기대로 메시를 만들고 스케일은 1로 둔다</b> — 모서리 반지름이 일정해진다.
    [Tooltip("판 모서리 둥글기. 글자 단위계다(패딩과 같은 단위). 0이면 각진 사각형.")]
    [SerializeField] private float backdropCornerRadius = 1.5f;

    [SerializeField] private bool showDebugLogs = false;

    private Transform root;
    private Transform holder;
    private TextMeshPro label;
    private Transform backdrop;
    private Camera cam;




    // 중립에서의 목→머리 축. "지금 얼마나 기울었나"를 이 기준으로 잰다.
    private Vector3 neckAxis0 = Vector3.up;
    private bool neckAxisCaptured;


    // 화면에 이미 얹은 것. 같은 내용이면 문자열을 새로 만들지 않는다(VR 프레임 예산).
    private int shownDirection = int.MinValue;
    private int shownAngle = int.MinValue;
    private int shownGauge = int.MinValue;
    private int shownRemain = int.MinValue;
    private int shownMask = int.MinValue;

    private readonly System.Text.StringBuilder sb = new System.Text.StringBuilder(160);
    private bool shownBacking;   // 직전에 그린 '되돌림' 경고 상태(변경 감지용)

    // ★파지 체크 줄의 직전 상태. ★★변경 감지 키에 <b>반드시</b> 넣어야 한다 —
    //   각도·게이지가 그대로인 프레임에 손만 떨어지면 다시 그리지 않아 체크가 안 바뀐다
    //   (바로 위 shownBacking이 같은 이유로 들어가 있다).
    private int shownGrip = int.MinValue;

    // ── 안 뜰 때 <b>스스로 이유를 말한다</b> (2026-09-07) ──────────────────
    // ★"안 나온다"를 추측으로 고치면 왕복한다. 막은 게이트를 그 자리에서 이름으로 찍는다.
    //   그리기 시작하면 저절로 조용해진다 — 잘 도는 동안은 한 줄도 안 남는다.
    private float nextSilentLog;
    private string lastSilentReason;
    private bool loggedFirstDraw;

    // ★마지막으로 방향을 잡은 단면. 이게 바뀔 때만 다시 잡는다(매 프레임 안 돈다).
    private int aimedPlane = -1;

    /// <summary>방향 → 단면. 0 없음 · 1 시상면(굴곡·신전) · 2 관상면(측굴) · 3 횡단면(회전).</summary>
    private static int PlaneOf(CervicalRomDriver.Direction d)
    {
        switch (d)
        {
            case CervicalRomDriver.Direction.Flexion:
            case CervicalRomDriver.Direction.Extension:      return 1;
            case CervicalRomDriver.Direction.LateralLeft:
            case CervicalRomDriver.Direction.LateralRight:   return 2;
            case CervicalRomDriver.Direction.RotationLeft:
            case CervicalRomDriver.Direction.RotationRight:  return 3;
            default:                                         return 0;
        }
    }

    private void ReportSilent(string reason)
    {
        if (reason == lastSilentReason && Time.unscaledTime < nextSilentLog) return;
        lastSilentReason = reason;
        nextSilentLog = Time.unscaledTime + 2f;
        ChunaLogger.LogWarning($"<color=orange>[실습표시] 안 그린다 — {reason}</color>");
    }

    private void Awake()
    {
        if (driver == null) driver = FindFirstObjectByType<CervicalRomDriver>();
        if (gripJudge == null) gripJudge = FindFirstObjectByType<CervicalGripJudge>();
        if (font == null) font = KoreanFontResolver.Resolve();

        // ★붙었다는 것부터 남긴다. 안 붙은 것과 붙었는데 안 그리는 것이 화면에선 똑같아 보인다
        //   (브리지가 시작 로그를 남기는 이유와 같다).
        ChunaLogger.Log($"<color=cyan>[실습표시] 붙었다 — 드라이버 {(driver != null ? "있음" : "★없음")} · " +
                        $"파지판정 {(gripJudge != null ? "있음" : "★없음")} · " +
                        $"폰트 {(font != null ? font.name : "★없음")}</color>");
    }

    private void OnDisable() => Teardown();
    private void OnDestroy() => Teardown();

    private void LateUpdate()
    {
        // ★런타임에 붙는 컴포넌트라 순서를 장담할 수 없다. 여기서 늦게 잡는다.
        if (driver == null)
        {
            driver = FindFirstObjectByType<CervicalRomDriver>();
            if (driver == null) { ReportSilent("드라이버가 씬에 없다"); Teardown(); return; }
        }
        if (guideUI == null)
            guideUI = FindFirstObjectByType<ScenarioGuideUIController>(FindObjectsInactive.Include);
        if (realityMeasure == null)
            realityMeasure = FindFirstObjectByType<CervicalRomRealityMeasure>(FindObjectsInactive.Include);

        // ★실측모드면 접는다 — 그쪽이 같은 자리에 같은 모양을 그린다.
        if (!show)
        {
            ReportSilent("show 가 꺼져 있다");
            Teardown();
            return;
        }
        // ★★<b>2026-09-07 정정 — 여기서 틀렸다.</b> 종전에는 <c>realityMeasure.isActiveAndEnabled</c>로
        //   "실측인가"를 <b>간접 추론</b>했다. 그런데 측정기는 <b>씬에 m_Enabled: 1로 굳어 있어</b>
        //   실습에서도 켜져 있다(브리지의 enabled=false는 런타임에 새로 붙일 때만 도는 코드다).
        //   그래서 실습 내내 "실측이다"로 읽고 접었다 — 화면이 통째로 비었다.
        //   → 그리는지 마는지는 <b>그리는 쪽에 묻는다.</b> 측정기 자신이 Update에서 쓰는 그 조건이다.
        if (realityMeasure != null && realityMeasure.IsDrawing)
        {
            ReportSilent("실측 측정기가 그리는 중이다(실측모드 또는 미리보기)");
            Teardown();
            return;
        }

        // ★★<b>여기가 09-07에 "실습에서 안 나온다"의 자리였다.</b>
        //   종전에는 "방향도 없고 게이지도 없으면 접는다"로 막았는데, 실습 경추ROM은
        //   자세정렬·파지 단계에서 <b>방향이 None이고 진행 게이지도 안 뜬다.</b>
        //   그래서 술기 초반 내내 아무것도 안 그렸다 — 사람 눈에는 "기능이 안 들어갔다"로 보인다.
        //   → 접는 기준을 <b>시나리오</b>로 바꾼다. 경추ROM 안이면 최소한 방향 목록은 늘 보인다.
        //   ★이 가드가 꼭 있어야 한다 — 동반 컴포넌트는 브리지 Awake에서 무조건 붙어
        //     13개 술기 어디서나 살아 있다(그래서 종전 가드가 조용히 그 역할을 겸하고 있었다).
        if (bridge == null) bridge = FindFirstObjectByType<CervicalRomScenarioBridge>(FindObjectsInactive.Include);
        if (bridge == null || !bridge.RomScenarioActive)
        {
            ReportSilent(bridge == null ? "ROM 브리지가 씬에 없다" : "경추ROM 시나리오가 아니다");
            Teardown();
            return;
        }

        CervicalRomDriver.Direction dir = driver.CurrentDirection;
        bool hasGauge = guideUI != null && guideUI.ProgressVisible;

        if (root == null) Build();
        if (label == null) return;

        // ★머리 안의 홀더에 자식으로 붙인다. 자리·기울기는 여기서 끝난다 — Unity가 물려준다.
        if (!TryAttach())
        {
            ReportSilent("머리 본도 목 뿌리도 못 찾았다");
            Teardown();
            return;
        }
        lastSilentReason = null;

        if (cam == null) cam = Camera.main;

        // ★★내가 매 프레임 정하는 것은 <b>홀더 로컬 y축 둘레의 한 각</b>뿐이다.
        //   로컬로 계산하므로 기울기에는 손이 안 닿는다 — 그건 부모(머리)가 이미 준 값이다.
        // ★★★<b>2026-09-07 — 매 프레임 따라가기를 뺐다(사용자 지시).</b>
        //   "프레임 드랍 걸리네. 그냥 <b>진단하는 단면 따라 방향 한 번씩만</b> 바꿔서 보여주는 걸로."
        //   → 단면(시상면·관상면·횡단면)이 바뀔 때 <b>그 순간 한 번만</b> 사용자를 향해 잡고, 그대로 둔다.
        //     자리와 기울기는 부모자식이 공짜로 물려주므로 매 프레임 할 일이 <b>0</b>이 된다.
        //   ★신전에서 동작이 안 하던 것도 같이 사라진다 — 매 프레임 겨누는 계산 자체를 안 하므로.
        // ★★★<b>2026-09-07 최종 — 각도기와 같은 자세를 쓴다(사용자 지시).</b>
        //   "매 단계마다 잡으란 소리가 아니라 <b>시상면 각도기처럼 표시하라</b>."
        //   → 자세를 내가 계산하지 않는다. <see cref="CervicalRomPlaneGauge.GaugeRoot"/>의 자세를
        //     그대로 베낀다. 각도기는 이미 <b>면 안에 누워</b> 있고(LookRotation(회전축, 0도방향)),
        //     면이 바뀔 때 좌우를 정하는 규칙까지 갖고 있다. 그걸 통째로 물려받는다.
        //   ★따로 계산하면 <b>각도기와 정보창이 다른 면을 보게 되고</b>, 그게 더 헷갈린다
        //     (규칙 9 — 같은 함수를 타야 한다).
        //   ★매 프레임 안 돈다. 각도기 자세는 면이 바뀔 때만 다시 서므로 여기서도 그때만 베낀다.
        // ★★★<b>2026-09-07 최종 — 단면마다 시술자가 서는 자리가 정해져 있다(사용자 지시).</b>
        //   <b>시상면(굴곡·신전)</b>  : 시술자는 환자 <b>좌/우 옆</b>에 선다 → 판은 <b>좌우축</b>을 향한다.
        //   <b>관상면(측굴)·횡단면(회전)</b> : 시술자는 <b>뒤통수 쪽</b>에 선다 → 거기서 <b>90도 돌아</b>
        //                                    <b>전후축</b>을 향한다.
        //   ★축은 내가 만들지 않는다 — 드라이버가 각 방향의 회전축을 이미 갖고 있다.
        //     굴곡의 회전축 = 좌우축, 측굴의 회전축 = 전후축이다. 그대로 가져다 쓴다.
        //   ★좌우 중 어느 쪽인지는 <b>사용자가 실제로 있는 쪽</b>으로 정한다(부호만 뒤집는다).
        //   ★★<b>2026-09-15 정정 — 글씨가 뒤집혀 보이던 자리다.</b>
        //     종전에는 ①자세를 <b>면이 바뀔 때만</b> 잡고 ②그 축의 부호를 <b>여기서 따로</b> 골랐다.
        //     그런데 판을 놓는 자리(<see cref="ReadoutLocalOffset"/>)도 자기 나름대로 부호를 고른다.
        //     둘이 어긋나면 <b>판 뒷면을 보게 되어 글자가 거울처럼 보인다.</b>
        //   → 부호를 <b>두 번 고르지 않는다.</b> 놓을 때 고른 그 방향을 그대로 쓴다
        //     (규칙 9 — 한 값이 두 곳에 쓰이면 손잡이를 나누지 말고 하나로 모은다).
        //   → 그리고 <b>자리가 바뀌면 자세도 다시 잡는다.</b> 면이 안 바뀌어도 사용자가 반대편으로
        //     돌아가면 판은 따라 넘어가므로, 그때 자세를 안 고치면 곧바로 뒷면을 보게 된다.
        int planeNow = PlaneOf(driver.CurrentDirection);
        if (cam != null && readoutOutwardWorld.sqrMagnitude > 1e-8f
            && (planeNow != aimedPlane || readoutSideSign != aimedSideSign))
        {
            aimedPlane = planeNow;
            aimedSideSign = readoutSideSign;

            // readoutOutwardWorld = 머리 중심 → 판. 즉 <b>사용자 쪽</b>이다.
            // 글자는 똑바로 선다(up = 월드 수직). forward는 사용자 반대쪽 — 이 프로젝트 규약이다.
            root.rotation = Quaternion.LookRotation(-readoutOutwardWorld.normalized, Vector3.up);

            if (showDebugLogs)
                ChunaLogger.Log($"<color=cyan>[실습표시] 단면 {planeNow} " +
                                $"({(planeNow == 1 ? "시상면·옆" : "관상/횡단면·뒤")}) " +
                                $"부호 {readoutSideSign} — 자세를 잡았다.</color>");
        }

        // --- 글 ---
        // ★게이지는 <b>남은 비율</b>로 들어온다(1=시작). 채운 칸은 진행률이므로 뒤집어 센다.
        int gaugeStep = hasGauge
            ? Mathf.Clamp(Mathf.RoundToInt((1f - guideUI.ProgressRatio01) * 10f), 0, 10)
            : -1;
        if (hasGauge && guideUI.ProgressCompleted) gaugeStep = 10;

        int remain = hasGauge ? Mathf.CeilToInt(guideUI.ProgressRemaining) : -1;
        int angle = Mathf.RoundToInt(driver.CurrentAngle);
        int mask = DoneMask();

        // ★압박 중 손을 중립 쪽으로 되돌리고 있는가(2026-09-14). 밀면 꺼진다.
        bool backing = bridge != null && bridge.OverpressureBacking;

        // ★파지 체크(2026-09-15). 쌍이 정해진 단계에서만 뜬다 — 자세정렬처럼 잡을 것이 없는
        //   단계에서는 grip이 -1이라 줄 자체가 없다.
        int grip = GripState();

        // ★★변경 감지 키에 <b>경고 상태도</b> 넣는다. 안 넣으면 각도·게이지가 그대로인 프레임에
        //   경고가 켜져도 <b>다시 그리지 않아</b> 화면에 안 뜨다.
        if ((int)dir == shownDirection && angle == shownAngle && gaugeStep == shownGauge
            && remain == shownRemain && mask == shownMask && backing == shownBacking
            && grip == shownGrip) return;

        shownDirection = (int)dir; shownAngle = angle;
        shownGauge = gaugeStep; shownRemain = remain; shownMask = mask; shownBacking = backing;
        shownGrip = grip;

        sb.Clear();

        // ★맨 위에 둔다. 압박 중에 제일 자주 흘깃 보는 줄이고, 한 단계 내내 계속 떠 있어
        //   아래 줄들을 밀어 출렁이게 하지 않는다(경고를 맨 아래 둔 것과 같은 이유).
        if (grip >= 0)
        {
            sb.Append("<size=70%>");
            AppendGripChecks(grip);
            sb.Append("</size>");
        }

        if (showAngleLine && dir != CervicalRomDriver.Direction.None)
        {
            // ★파지 줄이 위에 붙었으므로 줄을 바꾼다. 09-15에 이걸 빠뜨려 '파지'와 각도가
            //   한 줄에 붙어 나왔다 — 종전엔 각도가 첫 줄이라 개행이 필요 없었다.
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(Label(dir));
            sb.Append("  ");
            sb.Append(angle);
            sb.Append('도');
        }

        if (gaugeStep >= 0)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append("<size=70%>");
            AppendGauge(gaugeStep, remain);
            sb.Append("</size>");
        }

        // ★앞에 아무것도 없으면 개행을 넣지 않는다 — 빈 첫 줄이 생겨 글이 떠 보인다.
        // ★★<b>2026-09-08부터 기본으로 접는다</b>(사용자 지시: "정보는 현재각도 유지 게이지만 남기고 비활성화").
        //   ★<b>그래서 방향도 게이지도 없는 단계(자세정렬·파지)에서는 정보창이 통째로 빈다.</b>
        //     종전에는 이 줄이 그 자리를 메우고 있었다. 빈 판은 FitBackdrop이 접으므로
        //     아무것도 안 뜬다 — 그 단계의 안내는 진행 칸(진행Root)이 말한다.
        //   ★09-07에 "실습에서 안 나온다"를 겪은 자리라 <b>일부러 적어 둔다.</b>
        if (showDirectionRow)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append("<size=70%>");
            AppendDirectionRow(dir);
            sb.Append("</size>");
        }

        // ★경고는 <b>맨 아래</b>에 붙인다 — 위에 끼우면 각도·게이지 줄이 매번 아래로 밀려
        //   글이 출렁거려 읽기 나쁘다.
        if (backing)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append("<size=70%><color=#ffb74d>⚠ 되돌아가고 있습니다 — 압박 방향으로 밀어 주세요</color></size>");
        }

        label.text = sb.ToString();
        FitBackdrop();

        if (!loggedFirstDraw)
        {
            loggedFirstDraw = true;
            ChunaLogger.Log($"<color=cyan>[실습표시] 첫 표시 — \"{label.text.Replace("\n", " / ")}\" @ {root.position}</color>");
        }

        if (showDebugLogs)
            ChunaLogger.Log($"<color=cyan>[실습표시] {dir} {angle}도 · 게이지 {gaugeStep} · 남음 {remain}</color>");
    }

    /// <summary>
    /// ★★<b>2026-09-07 — 환자 머리에 붙인다(사용자 지시).</b>
    /// 종전에는 파지 지점(손)을 따라갔는데, 핸드트래킹 떨림이 그대로 글씨 떨림이 됐다.
    /// 실습에는 <b>기준이 되는 환자 머리가 이미 있다</b> — 흔들릴 이유가 없다.
    ///
    /// ★<b>강체 종속이다</b>(사용자 확정). 머리 로컬 기준으로 잡아 두므로 머리가 숙으면
    ///   글자도 같이 앞으로 넘어간다. <b>글자가 보는 방향만</b> 시술자를 따라 돈다.
    /// ★기준 오프셋은 <b>중립에서</b> 잡는다. 움직이는 중에 잡으면 그 기운 자세가 기준이 돼
    ///   중립으로 돌아왔을 때 글자가 엉뚱한 데 가 있다.
    /// </summary>
    /// <summary>
    /// 표시를 <b>머리 안의 홀더에 자식으로</b> 붙이고, 그 자리에 놓는다.
    ///
    /// ★★<b>2026-09-07 최종 구조</b>(사용자 제안):
    ///   머리 뼈는 반사된 계층이라(lossyScale (-1.1, 1.1, 1.1)) 그 밑에 직접 붙이면
    ///   회전 대입이 깨진다. → <b>부호를 되돌린 홀더</b>를 머리 안에 한 겹 끼우고,
    ///   표시는 그 홀더의 자식이 된다. 홀더 아래는 정상 좌표계다.
    /// ★그래서 <b>자리도 기울기도 Unity가 물려준다.</b> 내가 매 프레임 정하는 것은
    ///   <b>홀더 로컬 y축 둘레의 한 각</b>뿐이다(사용자를 향하는 각).
    /// </summary>
    private bool TryAttach()
    {
        if (root == null) return false;

        Transform head = ResolveHead();
        if (head == null)
        {
            // 머리를 못 찾으면 예비로 목 뿌리 위에 띄운다 — 안 보이는 것보다 낫다.
            Transform pivot = driver != null ? driver.Pivot : null;
            if (pivot == null) return false;
            if (root.parent != null) root.SetParent(null, true);
            root.position = pivot.position + Vector3.up * headRise;
            return true;
        }

        Transform h = EnsureHolder(head);
        if (root.parent != h) root.SetParent(h, false);

        // 홀더 로컬에서 <b>위쪽으로 headRise</b>. 머리가 기울면 이 점이 같이 넘어간다.
        root.localPosition = ReadoutLocalOffset(h);
        root.localScale = Vector3.one;
        return true;
    }

    /// <summary>
    /// 정보창을 홀더 로컬 어디에 놓을지(2026-09-15). <b>머리 위가 아니라 머리 옆 또는 뒤통수</b>다.
    ///
    /// ★<b>어느 쪽인지는 손이 정한다</b> — 지금 잡은 접촉점 쌍의 반대편이 빈 자리다.
    ///   시상면 쌍(이마·뒤통수) → 옆면 · 관상·횡단 쌍(좌·우 측두) → 뒤통수.
    ///   쌍이 아직 없으면 방향의 단면으로 정하고, 그것도 없으면 옆면을 쓴다.
    /// ★<b>부호는 카메라에 가까운 쪽</b>. 반대편으로 내면 머리에 가린다.
    ///   ★★후보 두 개를 <b>월드 좌표로 만들어 비교</b>한다. 홀더 스케일이 음수(−1,1,1)라
    ///     방향 벡터로 고르면 <b>X가 뒤집힌다</b>(TransformDirection은 스케일을 안 타고,
    ///     실제 배치에 쓰는 localPosition은 탄다). 09-15에 이 함정을 미리 막아 둔다.
    /// ★<b>흔들림 방지</b>: 정확히 옆에 섰을 때 두 후보가 엎치락뒤치락하면 판이 떤다.
    ///   지금 쪽이 확실히 더 멀 때만(<see cref="ReadoutFlipMargin"/>) 바꾼다.
    /// </summary>
    private const float ReadoutFlipMargin = 0.05f;   // 5cm 이상 차이 나야 반대쪽으로 넘어간다
    private int readoutSideSign;                     // 0 = 아직 안 정함
    private int aimedSideSign;                       // 자세를 잡을 때 쓴 부호
    private Vector3 readoutOutwardWorld;             // 머리 중심 → 판 (월드). ★자세도 이 값을 쓴다

    private Vector3 ReadoutLocalOffset(Transform h)
    {
        Vector3 baseLocal = Vector3.up * readoutHeight;
        if (h == null) return baseLocal;

        // 뒤통수 쪽인가 옆면인가 — 잡은 쌍이 1순위, 없으면 단면.
        bool back;
        if (gripJudge != null && gripJudge.CurrentPair != CervicalGripJudge.GripPair.None)
            back = gripJudge.CurrentPair == CervicalGripJudge.GripPair.Lateral;
        else
            back = PlaneOf(driver != null ? driver.CurrentDirection : CervicalRomDriver.Direction.None) >= 2;

        // 머리 본 로컬 실측: 전후 = z(+가 얼굴) · 좌우 = x.
        Vector3 axis = back ? Vector3.forward : Vector3.right;

        if (cam != null)
        {
            Vector3 eye = cam.transform.position;
            float dPlus = (h.TransformPoint(baseLocal + axis * readoutOutward) - eye).sqrMagnitude;
            float dMinus = (h.TransformPoint(baseLocal - axis * readoutOutward) - eye).sqrMagnitude;

            int want = dPlus <= dMinus ? 1 : -1;
            if (readoutSideSign == 0)
            {
                readoutSideSign = want;
            }
            else if (want != readoutSideSign)
            {
                // 지금 쪽이 확실히 더 멀 때만 넘어간다.
                float now = readoutSideSign > 0 ? dPlus : dMinus;
                float other = readoutSideSign > 0 ? dMinus : dPlus;
                if (Mathf.Sqrt(now) - Mathf.Sqrt(other) > ReadoutFlipMargin) readoutSideSign = want;
            }
        }
        else if (readoutSideSign == 0)
        {
            readoutSideSign = -1;   // 카메라를 못 찾으면 뒤통수/왼쪽 기본
        }

        Vector3 local = baseLocal + axis * (readoutOutward * readoutSideSign);

        // ★자세를 잡을 때 쓸 방향을 여기서 <b>한 번만</b> 정해 둔다. 저쪽에서 다시 고르면 어긋난다.
        //   ★홀더 스케일이 음수라 방향 벡터가 아니라 <b>두 점의 차</b>로 구한다.
        readoutOutwardWorld = h.TransformPoint(local) - h.TransformPoint(baseLocal);

        return local;
    }

    /// <summary>
    /// 머리 안에 끼우는 <b>부호 보정 홀더</b>. 이것이 표시의 진짜 부모다.
    ///
    /// ★★<b>2026-09-07 사용자 제안</b>: "부모자식 들어간 게 음수라 이상하면, 머리 자체 말고
    ///   <b>별도 오브젝트를 양수로 뽑은 다음 head 안에 넣고</b> 그걸 부모로 두면 기준 좌표가 양수다."
    ///   맞는 방법이다. 머리 뼈는 반사된 계층이라(실측 lossyScale (-1.1, 1.1, 1.1)) 그 밑에서
    ///   회전 대입이 깨지는데, <b>부호를 되돌린 홀더</b>를 한 겹 끼우면 그 아래는 정상 좌표계가 된다.
    /// ★그러면 자리도 기울기도 <b>Unity의 부모자식이 물려준다</b> — 내가 매 프레임 계산할 것이
    ///   <b>축 둘레 한 각뿐</b>이다. 계산이 줄면 틀릴 자리도 준다.
    /// </summary>
    private Transform EnsureHolder(Transform head)
    {
        if (holder != null && holder.parent == head) return holder;

        var go = new GameObject("실습표시_홀더") { hideFlags = HideFlags.DontSave };
        holder = go.transform;
        holder.SetParent(head, false);
        holder.localPosition = Vector3.zero;
        holder.localRotation = Quaternion.identity;

        // ★부호만 되돌린다. 크기는 그대로 둬야 머리와 같은 배율을 탄다.
        Vector3 ls = head.lossyScale;
        holder.localScale = new Vector3(ls.x < 0f ? -1f : 1f,
                                        ls.y < 0f ? -1f : 1f,
                                        ls.z < 0f ? -1f : 1f);
        return holder;
    }

    /// <summary>환자 머리 본. 드라이버의 몸통에서 <c>CC_Base_Head</c>를 찾는다.</summary>
    private Transform ResolveHead()
    {
        if (headBone != null) return headBone;
        if (driver == null) return null;

        Transform torso = driver.Torso;
        if (torso == null) return null;
        headBone = FindDeep(torso.root, "CC_Base_Head");
        return headBone;
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

    /// <summary>
    /// 지금 왼손·오른손이 접촉점에 닿아 있는가. 비트 0 = 왼손, 비트 1 = 오른손.
    /// 쌍이 안 정해진 단계(자세정렬 등)나 표시를 껐으면 <b>-1</b> — 줄 자체를 안 그린다.
    ///
    /// ★<b>어느 접촉점인지는 따지지 않는다.</b> `CervicalGripJudge`가 이미 "두 점을 서로 다른 손이
    ///   하나씩"으로 판정하므로, 여기서 필요한 것은 <b>그 손이 인식되고 있는가</b> 하나다.
    ///   사용자가 가르고 싶어한 것이 정확히 그것이다 — 손인식 문제인가 접촉점 문제인가.
    /// </summary>
    private int GripState()
    {
        if (!showGripChecks || gripJudge == null || !gripJudge.IsActive) return -1;

        if (!gripJudge.TryGetGripState(out bool aLeft, out bool aRight, out bool bLeft, out bool bRight))
            return -1;

        int s = 0;
        if (aLeft || bLeft) s |= 1;
        if (aRight || bRight) s |= 2;
        return s;
    }

    /// <summary>파지 체크 줄. 닿은 손은 초록 체크, 안 닿은 손은 회색 빈 표시.</summary>
    private void AppendGripChecks(int grip)
    {
        sb.Append("파지  ");
        AppendOneCheck("좌", (grip & 1) != 0);
        sb.Append("  ");
        AppendOneCheck("우", (grip & 2) != 0);
    }

    private void AppendOneCheck(string handName, bool ok)
    {
        // ★색만으로 가르지 않는다 — 기호도 같이 바꾼다. 패스스루 배경이 밝으면
        //   초록·회색 차이가 잘 안 보인다.
        // ★★동그라미를 쓴다(2026-09-15 사용자 지적: "다 사각형이라 디자인이 그래").
        //   ★'동그라미 안에 체크'(✅ 등)는 <b>이모지</b>라 NotoSansKR에 글리프가 없을 수 있다 —
        //     없으면 두부(□)로 나와 더 나쁘다. ●/○는 방향 줄에서 이미 쓰고 있어 확실히 나온다.
        sb.Append(ok ? "<color=#7ad67a>" : "<color=#808080>");
        sb.Append(ok ? "● " : "○ ");
        sb.Append(handName);
        sb.Append("</color>");
    }

    private void AppendGauge(int steps, int remain)
    {
        bool full = steps >= 10;
        if (full) sb.Append("<color=#7ad67a>");
        sb.Append(gaugeLabel);
        sb.Append(' ');
        // ★네모 대신 동그라미(2026-09-15 사용자 지적). 칸 수·의미는 그대로다.
        for (int i = 0; i < 10; i++) sb.Append(i < steps ? '●' : '○');
        if (remain > 0)
        {
            sb.Append(' ');
            sb.Append(remain);
            sb.Append('초');
        }
        if (full) sb.Append("</color>");
    }

    /// <summary>여섯 방향 중 어디까지 왔는지. ● 끝 · ▶ 지금 · ○ 아직.</summary>
    private void AppendDirectionRow(CervicalRomDriver.Direction current)
    {
        for (int i = 1; i <= 6; i++)
        {
            if (i > 1) sb.Append(' ');
            var d = (CervicalRomDriver.Direction)i;

            if (d == current) sb.Append("<color=#ffcc55>▶");
            else if (IsDone(d)) sb.Append("<color=#7ad67a>●");
            else sb.Append("<color=#808080>○");

            sb.Append(Label(d));
            sb.Append("</color>");
        }
    }

    /// <summary>
    /// 그 방향을 끝냈는가. ★능동만 남은 것은 아직 끝이 아니다 —
    /// 압박(수동)까지 재야 그 방향이 닫힌다. 실측의 hasPassive와 같은 뜻이다.
    /// </summary>
    private bool IsDone(CervicalRomDriver.Direction d)
    {
        CervicalRomDriver.Measurement m = driver.GetMeasurement(d);
        return m.recorded && m.passive > 0.01f;
    }

    private int DoneMask()
    {
        int m = 0;
        for (int i = 1; i <= 6; i++)
            if (IsDone((CervicalRomDriver.Direction)i)) m |= 1 << i;
        return m;
    }

    private void Build()
    {
        var go = new GameObject("실습정보표시") { hideFlags = HideFlags.DontSave };
        root = go.transform;

        var t = new GameObject("현재각") { hideFlags = HideFlags.DontSave };
        t.transform.SetParent(root, false);
        label = t.AddComponent<TextMeshPro>();
        if (font != null) label.font = font;
        label.fontSize = fontSize * 100f * Mathf.Max(0.1f, fontScale);
        label.transform.localScale = Vector3.one * 0.01f;
        label.alignment = TextAlignmentOptions.Center;
        label.fontStyle = FontStyles.Bold;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;

        if (showBackdrop) BuildBackdrop();

        ResetShown();
    }

    /// <summary>
    /// 글자 뒤 반투명 판. ★<b>글자의 자식</b>으로 넣는다 — 그래야 글자의 0.01 스케일을
    /// 그대로 타서 크기를 글자 로컬 단위로 곧장 줄 수 있다.
    /// ★재질은 씬에 있는 다른 표시물과 <b>같은 방식</b>으로 만든다(Sprites/Default, 없으면 Standard).
    ///   빌드에서 셰이더가 스트립되는 사고가 있었던 자리라 검증된 경로를 그대로 쓴다.
    /// </summary>
    private void BuildBackdrop()
    {
        var go = new GameObject("배경") { hideFlags = HideFlags.DontSave };
        go.AddComponent<MeshFilter>();
        go.AddComponent<MeshRenderer>();

        backdrop = go.transform;
        backdrop.SetParent(label.transform, false);

        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Standard");
        var mr = go.GetComponent<MeshRenderer>();
        mr.material = new Material(sh) { color = backdropColor, renderQueue = 2990 };
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    /// <summary>글이 바뀔 때마다 판을 글자 상자에 맞춘다.</summary>
    private void FitBackdrop()
    {
        if (backdrop == null || label == null) return;

        label.ForceMeshUpdate();
        Bounds b = label.textBounds;
        if (b.size.x < 1e-4f || b.size.y < 1e-4f)
        {
            if (backdrop.gameObject.activeSelf) backdrop.gameObject.SetActive(false);
            return;
        }
        if (!backdrop.gameObject.activeSelf) backdrop.gameObject.SetActive(true);

        // ★스케일은 1로 둔다. 크기는 <b>메시 자체</b>로 만든다 —
        //   비균등 스케일을 주면 둥근 모서리가 타원으로 늘어난다.
        float w = b.size.x + backdropPadX * 2f;
        float h = b.size.y + backdropPadY * 2f;
        RebuildBackdropMesh(w, h);

        backdrop.localScale = Vector3.one;
        // ★글자보다 <b>뒤</b>로 살짝 물린다. 같은 평면에 두면 z-파이팅으로 지글거린다.
        backdrop.localPosition = new Vector3(b.center.x, b.center.y, 0.6f);
        backdrop.localRotation = Quaternion.identity;
    }

    private Mesh backdropMesh;
    private float builtW, builtH, builtR;

    /// <summary>
    /// 모서리를 깎은 사각형 판을 만든다. 크기가 그대로면 다시 만들지 않는다.
    ///
    /// ★<b>부채꼴로 잇는다</b> — 가운데 한 점에서 테두리 점들로 삼각형을 두른다. 가장 단순하고
    ///   볼록한 도형이라 이 방법으로 충분하다.
    /// ★반지름은 <b>짧은 변의 절반</b>을 넘지 못하게 자른다. 안 그러면 모서리끼리 겹쳐 뒤집힌다.
    /// ★재질이 Sprites/Default면 <c>Cull Off</c>라 앞뒤 어느 쪽에서도 보인다 — 감는 방향을 안 따진다.
    /// </summary>
    private void RebuildBackdropMesh(float w, float h)
    {
        float r = Mathf.Clamp(backdropCornerRadius, 0f, Mathf.Min(w, h) * 0.5f);

        // 같은 크기면 다시 만들지 않는다(글이 바뀔 때마다 부르는 경로다).
        if (backdropMesh != null &&
            Mathf.Abs(w - builtW) < 1e-4f && Mathf.Abs(h - builtH) < 1e-4f && Mathf.Abs(r - builtR) < 1e-4f)
            return;

        builtW = w; builtH = h; builtR = r;

        const int Seg = 5;                  // 모서리 한 곳당 나누는 수. 5면 충분히 매끄럽다.
        int perCorner = Seg + 1;
        int rim = perCorner * 4;

        var verts = new Vector3[rim + 1];   // 0번은 한가운데
        var uvs = new Vector2[rim + 1];
        verts[0] = Vector3.zero;
        uvs[0] = new Vector2(0.5f, 0.5f);

        float hw = w * 0.5f - r;
        float hh = h * 0.5f - r;
        var centers = new[]
        {
            new Vector2(hw, hh), new Vector2(-hw, hh), new Vector2(-hw, -hh), new Vector2(hw, -hh),
        };

        int v = 1;
        for (int c = 0; c < 4; c++)
        {
            for (int i = 0; i <= Seg; i++)
            {
                float deg = c * 90f + 90f * i / Seg;
                float rad = deg * Mathf.Deg2Rad;
                float x = centers[c].x + Mathf.Cos(rad) * r;
                float y = centers[c].y + Mathf.Sin(rad) * r;
                verts[v] = new Vector3(x, y, 0f);
                uvs[v] = new Vector2(x / w + 0.5f, y / h + 0.5f);
                v++;
            }
        }

        var tris = new int[rim * 3];
        for (int i = 0; i < rim; i++)
        {
            tris[i * 3] = 0;
            tris[i * 3 + 1] = i + 1;
            tris[i * 3 + 2] = (i + 1) % rim + 1;
        }

        if (backdropMesh == null)
        {
            backdropMesh = new Mesh { name = "실습표시_판", hideFlags = HideFlags.DontSave };
            backdrop.GetComponent<MeshFilter>().sharedMesh = backdropMesh;
        }

        backdropMesh.Clear();
        backdropMesh.vertices = verts;
        backdropMesh.uv = uvs;
        backdropMesh.triangles = tris;
        backdropMesh.RecalculateBounds();
        backdropMesh.RecalculateNormals();
    }

    private void Teardown()
    {
        if (root != null)
        {
            if (Application.isPlaying) Destroy(root.gameObject);
            else DestroyImmediate(root.gameObject);
        }
        // ★우리가 만든 메시는 우리가 치운다. 안 치우면 Play를 껐다 켤 때마다 쌓인다.
        if (backdropMesh != null)
        {
            if (Application.isPlaying) Destroy(backdropMesh); else DestroyImmediate(backdropMesh);
            backdropMesh = null;
        }
        builtW = builtH = builtR = 0f;

        root = null;
        label = null;
        backdrop = null;
        ResetShown();
    }

    private void ResetShown()
    {
        shownDirection = int.MinValue;
        shownAngle = int.MinValue;
        shownGauge = int.MinValue;
        shownRemain = int.MinValue;
        shownMask = int.MinValue;
        shownGrip = int.MinValue;
    }

    // ★[삭제 2026-09-07] SwingAboutWorldUp — 강체 기울기를 빌보드에 <b>곱해서</b> 얹던 방식의 부품이었다.
    //   그 방식이 정면을 딴 데로 돌려 버려(뒤통수 쪽) 폐기했다. 같은 계산을 다시 만들지 말 것.

    /// <summary>
    /// 그림의 <b>빨간 축</b> — 목 뿌리에서 머리로 가는 선이다. 판은 이 축 둘레로만 돈다.
    ///
    /// ★★<b>회전이 아니라 위치 두 점으로 뽑는다</b>(2026-09-07). 이유가 있다:
    ///   머리 뼈는 <b>반사된 계층</b> 아래에 있다(씬 실측: CC_Base_Head lossyScale = (-1.1, 1.1, 1.1),
    ///   범인은 c8의 (-1,1,1)). 반사 행렬에서 뽑은 <c>Transform.rotation</c>은 믿을 수 없다 —
    ///   쿼터니언이 반사를 표현할 수 없어 Unity가 임의로 근사한 값을 준다.
    ///   ★<b>두 점의 차</b>는 순수한 벡터라 반사에 안 물린다. 그래서 이쪽이 옳다.
    /// </summary>
    private bool TryGetNeckAxis(out Vector3 axis)
    {
        axis = Vector3.up;
        Transform head = ResolveHead();
        Transform neck = driver != null ? driver.Pivot : null;
        if (head == null || neck == null) return false;

        Vector3 v = head.position - neck.position;
        if (v.sqrMagnitude < 1e-6f) return false;   // 두 점이 겹치면 축을 못 만든다
        v.Normalize();

        // ★★<b>2026-09-07 — 축을 그대로 쓰면 안 된다.</b> 목→머리 선은 <b>중립에서도 수직이 아니다</b>
        //   (머리 뼈가 목 뿌리보다 앞에 있다. 로그 실측: 머리 (0.003,1.171,0.332)).
        //   그대로 쓰면 판이 <b>처음부터 기울어진 채 굳는다</b> — 사용자가 본 그 증상이다.
        //   → 중립 축을 한 번 적어 두고, <b>거기서 얼마나 돌았는지(변화량)</b>만 월드 수직에 얹는다.
        //     중립이면 그림 왼쪽(수평), 목이 θ만큼 기울면 그림 오른쪽(θ만큼 기움)이 된다.
        if (!neckAxisCaptured)
        {
            neckAxis0 = v;
            neckAxisCaptured = true;
        }
        axis = Quaternion.FromToRotation(neckAxis0, v) * Vector3.up;
        return true;
    }

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
}
