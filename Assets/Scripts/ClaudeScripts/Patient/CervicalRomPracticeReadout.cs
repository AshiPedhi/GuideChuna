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
    [Tooltip("환자 머리 중심 위로 이만큼 띄운다(m). 중립에서 잰 높이다.\n" +
             "★2026-09-07: 0.22 → 0.32. 머리에 가려서 올렸다.")]
    [SerializeField] private float headRise = 0.32f;
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
        //   ★면이 바뀔 때만 잡는다. 매 프레임 안 돈다.
        int planeNow = PlaneOf(driver.CurrentDirection);
        if (cam != null && planeNow != 0 && planeNow != aimedPlane)
        {
            aimedPlane = planeNow;

            Vector3 axis = planeNow == 1
                ? driver.WorldAxisFor(CervicalRomDriver.Direction.Flexion)       // 좌우축
                : driver.WorldAxisFor(CervicalRomDriver.Direction.LateralRight); // 전후축

            if (axis.sqrMagnitude > 1e-8f)
            {
                axis.Normalize();
                // 사용자가 있는 쪽으로 부호를 맞춘다.
                if (Vector3.Dot(axis, cam.transform.position - root.position) < 0f) axis = -axis;

                // 글자는 똑바로 선다(up = 월드 수직). forward는 사용자 반대쪽 — 이 프로젝트 규약이다.
                root.rotation = Quaternion.LookRotation(-axis, Vector3.up);

                if (showDebugLogs)
                    ChunaLogger.Log($"<color=cyan>[실습표시] 단면 {planeNow} " +
                                    $"({(planeNow == 1 ? "시상면·옆" : "관상/횡단면·뒤")}) — 자세를 잡았다.</color>");
            }
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

        if ((int)dir == shownDirection && angle == shownAngle && gaugeStep == shownGauge
            && remain == shownRemain && mask == shownMask) return;

        shownDirection = (int)dir; shownAngle = angle;
        shownGauge = gaugeStep; shownRemain = remain; shownMask = mask;

        sb.Clear();

        if (showAngleLine && dir != CervicalRomDriver.Direction.None)
        {
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
        //   방향도 게이지도 없는 단계(자세정렬·파지)에서는 이 줄만 남는다.
        if (sb.Length > 0) sb.Append('\n');
        sb.Append("<size=70%>");
        AppendDirectionRow(dir);
        sb.Append("</size>");

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
        root.localPosition = Vector3.up * headRise;
        root.localScale = Vector3.one;
        return true;
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

    private void AppendGauge(int steps, int remain)
    {
        bool full = steps >= 10;
        if (full) sb.Append("<color=#7ad67a>");
        sb.Append(gaugeLabel);
        sb.Append(' ');
        for (int i = 0; i < 10; i++) sb.Append(i < steps ? '■' : '□');
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
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "배경";
        go.hideFlags = HideFlags.DontSave;

        var col = go.GetComponent<Collider>();
        if (col != null)
        {
            if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
        }

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

        backdrop.localScale = new Vector3(b.size.x + backdropPadX * 2f,
                                          b.size.y + backdropPadY * 2f, 1f);
        // ★글자보다 <b>뒤</b>로 살짝 물린다. 같은 평면에 두면 z-파이팅으로 지글거린다.
        backdrop.localPosition = new Vector3(b.center.x, b.center.y, 0.6f);
        backdrop.localRotation = Quaternion.identity;
    }

    private void Teardown()
    {
        if (root != null)
        {
            if (Application.isPlaying) Destroy(root.gameObject);
            else DestroyImmediate(root.gameObject);
        }
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
