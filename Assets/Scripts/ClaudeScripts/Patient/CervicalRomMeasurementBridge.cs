using System.Collections.Generic;
using UnityEngine;
using ChunaTraining;

/// <summary>
/// 실측모드 전용 브리지. CSV의 <b>phase = 실측</b> 단계와 <see cref="CervicalRomRealityMeasure"/>를 잇는다.
///
/// ★교육모드의 <see cref="CervicalRomScenarioBridge"/>와 <b>완전히 별개</b>다.
///   그쪽은 대본 각도를 모델에 얹는 물건이고, 이쪽은 실제 사람을 잰다.
///   모드는 DifficultyManager가 가르고, 어느 단계를 돌릴지는 ScenarioConfig의
///   measurementPhases 화이트리스트가 이미 걸러 준다 — 여기서 또 거르지 않는다.
///
/// 진행 방식 — 측정기가 '양손 정지'로 스스로 확정하고, 브리지는 그 결과를 보고 substep을 넘긴다.
///   기준정렬 : 십자축이 잡히면      → 다음
///   파지     : 0점이 잡히면          → 다음
///   능동     : 능동 끝점이 기록되면  → 다음
///   압박     : 압박 끝점이 기록되면  → 다음
///   중립복귀 : 중립 근처로 돌아오면  → 다음
/// </summary>
public class CervicalRomMeasurementBridge : MonoBehaviour
{
    [SerializeField] private ScenarioManager scenarioManager;
    [SerializeField] private CervicalRomRealityMeasure measure;
    [SerializeField] private ChunaPathEvaluator evaluator;

    [Tooltip("표준자세 체크리스트. 실측에서는 안 쓴다(2026-09-01) — 교육모드용으로 남는다.")]
    [SerializeField] private PostureChecklistUI checklist;

    [Tooltip("실습 각도기. 실측에 들어오면 출처를 측정기로 갈아 끼우고, 나갈 때 되돌린다.\n" +
             "★비우면 자동 탐색. 못 찾으면 각도기 없이 진행한다(경고만 남긴다).")]
    [SerializeField] private CervicalRomPlaneGauge planeGauge;

    [Header("=== 현실 전환 (2026-08-31) ===")]
    [Tooltip("패스스루·환자 표시를 쥔 컨트롤러. 비우면 자동 탐색.")]
    [SerializeField] private PracticeSettingsController practiceSettings;

    [Tooltip("실측 중에 <b>추가로</b> 숨길 오브젝트.\n" +
             "★배경(방 모델) 안에 있는 것은 패스스루가 배경째 끄므로 여기 넣을 필요가 없다.")]
    [SerializeField] private GameObject[] alsoHideInMeasurement;

    [Tooltip("이름으로 찾아 숨길 오브젝트. 배선 없이 동작한다.\n\n" +
             "기본값 '추나 테이블' = Assets/_JDH/추나 테이블.fbx 인스턴스로,\n" +
             "위치초기화 오브젝트/GameObject/ChunaObject 아래에 있다. 배경 밖이라 패스스루로는 안 꺼진다.\n\n" +
             "★이름이 바뀌면 조용히 못 찾는다 — 그때는 경고 로그가 뜨니 그걸 보고 고칠 것.")]
    [SerializeField] private string[] hideByNameInMeasurement = { "추나 테이블" };

    [Tooltip("이 이름의 시나리오에서만 돈다.")]
    [SerializeField] private string scenarioName = "경추ROM측정";

    [Tooltip("중립 복귀로 인정할 각(도).")]
    [SerializeField] private float neutralTolerance = 6f;

    [Tooltip("한 substep을 넘긴 뒤 이만큼은 다시 안 넘긴다(초). 연속 진행 방지.")]
    [SerializeField] private float advanceCooldown = 0.6f;

    // ── 진행 UI를 손 근처로 (2026-09-03) ─────────────────────────────────
    // 2026-09-03 사용자: "정보를 진행Root에 표시하되, 그 값이 지금 정보처럼 손 근처에 따라왔으면 좋겠다.
    //   지금 자리보다 좀 더 뒤로, 크게, 높이는 살짝 낮춰서 고개를 안 올려도 보이게."
    //
    // ★교육 브리지(CervicalRomScenarioBridge)도 같은 진행Root를 옮긴다 — 다만 그쪽은
    //   실측이면 손을 떼도록 고쳤다(PlaceProgressUI). 두 곳이 같은 트랜스폼을 밀면
    //   프레임마다 서로 되돌린다.
    // ★<b>우리가 옮긴 것만 우리가 되돌린다</b> — 진입할 때 원래 자리를 적어 두고 나갈 때 복원한다.
    //   (07-27 xray 사고가 켠 쪽과 끄는 쪽이 달라서 생긴 형태였다.)
    // ★전부 신규 필드라 씬에 값이 없다 → 코드 기본값이 그대로 먹는다(규칙 7).

    [Header("=== 진행 UI 손 추종 (2026-09-03) ===")]
    [Tooltip("옮길 UI 루트. 비우면 교육 브리지의 슬롯 → 이름('진행Root') 순으로 찾는다.")]
    [SerializeField] private Transform progressRoot;

    // ★★2026-09-03 컨펌 결과: <b>진행Root는 기존대로 둔다</b>("너무 어수선하고 비효율적").
    //   추종·글 밀어넣기 둘 다 기본으로 끈다. 코드는 남겨 둔다 — 다시 켜 보고 싶을 때가 있다.
    //   실측 정보는 종전대로 <b>손 옆 월드 텍스트</b>로 돌아간다
    //   (CervicalRomRealityMeasure.routeReadoutToGuideUI = false).

    [Tooltip("★기본 꺼짐(2026-09-03 컨펌) — 진행Root를 안 건드린다. 켜면 헤드셋을 게으르게 따라온다.")]
    [SerializeField] private bool followProgressRoot;

    // ── 진행Root를 정보패널의 '결과' 페이지 안에 넣는다 (2026-09-04 회의) ──
    //
    // 사용자: "결과창을 현재 진행창으로 바꿔서, 처음에 모드 선택한 후에 진행창을 띄워서 보여주라는 거였는데."
    // ★"정보패널 위에 올려라"는 <b>물리적으로 Y축 위</b>가 아니라 <b>그 안에 넣어라</b>였다.
    //   09-04에 한 번 물리적으로 띄웠다가 지적받고 걷어냈다.
    //
    // ★★<b>ROM 실측에서만</b> 한다(사용자 확정). InfoPanelController는 13개 술기가 공유하므로
    //   그쪽 흐름(모드 선택 → 근골격 페이지)은 <b>한 줄도 안 바꿨다</b> —
    //   밖에서 부를 수단만 열고, 부르는 것은 여기서만 한다.
    //
    // ★진행Root는 <b>월드 Transform</b>이다(RectTransform 아님. 씬 실측: 부모 = 'UI Group').
    //   결과 페이지는 캔버스 안의 RectTransform이라 스케일 단위가 다르다 →
    //   부모의 lossyScale을 <b>상쇄</b>해 원래 보이던 크기를 유지한다. 안 하면 글자가 거대해지거나 사라진다.
    // ★넣은 쪽이 뺀다 — 원래 부모·로컬 TRS를 적어 두고 나갈 때 되돌린다.

    [Tooltip("★진행Root(스텝 안내 + [다음] 버튼)를 정보패널의 <b>결과 페이지 안</b>에 넣는다.\n" +
             "ROM 실측에서만 돈다. 다른 술기는 종전 그대로다.")]
    [SerializeField] private bool dockProgressIntoResultPage = true;

    [Tooltip("결과 페이지 기준 로컬 위치. 페이지 한가운데가 0,0,0이다.")]
    [SerializeField] private Vector3 dockLocalPosition = Vector3.zero;

    [Tooltip("패널 면에서 앞으로 이만큼 뺀다(m). 같은 평면에 두면 깜빡인다.")]
    [SerializeField] private float dockForwardOffset = 0.05f;

    [Tooltip("페이지를 이 비율까지 채운다. 1이면 딱 맞고, 0.92면 가장자리에 여백이 남는다.\n" +
             "★배율은 <b>계산</b>한다 — 진행창과 페이지의 실제 크기를 재서 정한다.")]
    [Range(0.3f, 1f)] [SerializeField] private float dockFillRatio = 0.92f;

    // 우리가 넣었는가 — 넣은 쪽이 뺀다.
    private bool progressDocked;
    private Transform dockHomeParent;
    private Vector3 dockHomeLocalPos;
    private Quaternion dockHomeLocalRot;
    private Vector3 dockHomeLocalScale;
    private InfoPanelController infoPanel;

    [Tooltip("헤드셋 앞으로 이만큼 띄운다(m).\n" +
             "★<b>이게 크기 손잡이다.</b> 캔버스를 키우지 않고 거리로만 조절한다\n" +
             "  (2026-09-03 사용자: '스케일은 건들지 말아봐').\n" +
             "★실측으로 좁힌 값 — 0.65는 각도기(환자 머리 0.5~0.8m)를 가렸고,\n" +
             "  1.15는 스케일 없이는 너무 작았다. 그 사이다.")]
    [SerializeField] private float followDistance = 0.9f;

    [Tooltip("눈높이에서 이만큼 <b>내린다</b>(m). 양수면 내려간다.\n" +
             "★고개를 올리지 않아도 보이게 하는 값이다 — 환자 머리보다 아래에 와야 한다.")]
    [SerializeField] private float followDrop = 0.30f;

    [Tooltip("★<b>기본 1 = 크기를 안 건드린다</b>(2026-09-03 사용자 지시: '스케일은 건들지 말아봐').\n" +
             "1이 아닐 때만 원래 크기에 이 값을 곱한다.\n" +
             "★멀어서 작아 보이면 <b>여기가 아니라 followDistance</b>를 줄이는 게 맞다 —\n" +
             "  캔버스를 키우면 글씨·여백 비율이 씬 설정과 어긋난다.")]
    [SerializeField] private float progressScale = 1f;

    // ── 게으른 추종 (2026-09-03 사용자 지시) ──────────────────────────────
    // "헤드셋에 딱 붙어서 따라오면 드드득하고 계속 움직여 잔상이 남고 눈이 피로하다.
    //  회전에 맞춰 일정 범위를 정하고, 벗어날 때만 부드럽게 따라올 것."
    // → 데드존 안에서는 <b>완전히 정지</b>한다. 벗어나면 SmoothDamp로 따라가고,
    //   안착 각까지 들어오면 다시 멈춘다(히스테리시스). 매 프레임 미세하게 움직이지 않는다.

    [Tooltip("헤드셋 정면에서 이 각도(도) 안에 있으면 <b>움직이지 않는다</b>.\n" +
             "★이게 잔상의 해법이다 — 조금이라도 따라 움직이면 계속 떨린다.")]
    [Range(5f, 60f)] [SerializeField] private float followDeadZoneDeg = 25f;

    [Tooltip("따라가기 시작하면 이 각도(도) 안에 들어올 때까지 간다. 데드존보다 작아야 한다.")]
    [Range(1f, 30f)] [SerializeField] private float followSettleDeg = 6f;

    [Tooltip("자리가 이만큼(m) 어긋나도 따라간다. 고개 회전이 아니라 <b>몸이 움직인</b> 경우다.")]
    [SerializeField] private float followMoveDeadZone = 0.35f;

    [Tooltip("따라가는 부드러움(초). 클수록 천천히 쫓아온다. 0.3~0.5가 눈이 편하다.")]
    [SerializeField] private float followSmoothTime = 0.35f;

    [Tooltip("★<b>바라보는 각</b>은 자리와 따로 맞춘다 — 판은 제자리에 있어도 정면은 늘 사용자 쪽이다.\n" +
             "이 각(도)보다 어긋나야 돌기 시작한다. 회전은 자리 이동과 달리 잔상을 안 남기므로\n" +
             "자리 데드존(followDeadZoneDeg)보다 훨씬 좁게 둘 수 있다.")]
    [Range(0.5f, 15f)] [SerializeField] private float faceDeadZoneDeg = 3f;

    [Tooltip("바라보는 각을 맞추는 부드러움(초).")]
    [SerializeField] private float faceSmoothTime = 0.25f;

    [Tooltip("실측 정보를 진행 UI의 지시문 칸에 써 넣는다.\n" +
             "★기본 꺼짐(2026-09-03 컨펌) — 정보는 손 옆 월드 텍스트로 돌아갔다.\n" +
             "  진행Root에 얹으니 화면이 어수선하다는 판단이다.")]
    [SerializeField] private bool pushReadoutToGuideUI;

    // ── 결과창 자동 진행 (2026-09-04 회의) ────────────────────────────────
    // "시나리오가 끝나면 다음 버튼 누르지 않아도 자동으로 결과창이 나오게 했으면 한다."
    // ★신규 필드라 코드 기본값이 그대로 먹는다(규칙 7).

    [Tooltip("★결과 단계를 [다음] 없이 자동으로 넘긴다(2026-09-04 회의).\n" +
             "끄면 종전대로 [다음] 토글을 띄워 사람이 누르게 한다.")]
    [SerializeField] private bool autoAdvanceResult = true;

    private bool resultShown;

    [SerializeField] private bool showDebugLogs = true;

    private string advancedKey;      // 이미 넘긴 substep 표식
    private float lastAdvanceTime = -99f;
    private bool active;
    private bool warnedNoMeasure;

    private ScenarioGuideUIController guideUI;
    private ScenarioConditionManager conditionManager;
    private bool resultToggleShown;

    // 진행 UI — 우리가 옮긴 것만 우리가 되돌린다.
    private bool progressCaptured;
    private Vector3 progressHomePos;
    private Quaternion progressHomeRot;
    private Vector3 progressHomeScale;
    private bool followPlaced;         // 이번 추종에서 한 번은 갖다 놨는가
    private bool followChasing;        // 데드존을 벗어나 쫓는 중인가
    private Vector3 followVelocity;    // SmoothDamp용

    // 준비 단계 토글 잠금 — 잠근 쪽이 푼다.
    private bool readyToggleLocked;

    // 현실 전환 — 우리가 바꾼 것만 우리가 되돌린다.
    private bool realWorldApplied;
    private bool passthroughWasOn;
    private readonly List<GameObject> hiddenByUs = new List<GameObject>(4);

    private void Awake()
    {
        if (scenarioManager == null) scenarioManager = FindFirstObjectByType<ScenarioManager>();
        if (measure == null) measure = FindFirstObjectByType<CervicalRomRealityMeasure>(FindObjectsInactive.Include);
        if (evaluator == null) evaluator = FindFirstObjectByType<ChunaPathEvaluator>();
        if (checklist == null) checklist = FindFirstObjectByType<PostureChecklistUI>(FindObjectsInactive.Include);
        if (planeGauge == null) planeGauge = FindFirstObjectByType<CervicalRomPlaneGauge>(FindObjectsInactive.Include);
        if (practiceSettings == null) practiceSettings = FindFirstObjectByType<PracticeSettingsController>(FindObjectsInactive.Include);

        ChunaLogger.Log($"<color=cyan>[실측Bridge] 시작 — 측정기 {(measure != null ? "있음" : "★없음")} · " +
                        $"시나리오매니저 {(scenarioManager != null ? "있음" : "★없음")}</color>");
    }

    private void Update()
    {
        if (scenarioManager == null) return;

        if (!IsMeasurementMode() || !IsTargetScenario())
        {
            if (active)
            {
                active = false;
                advancedKey = null;
                resultToggleShown = false;
                // ★교육모드로 돌아가면 실측 표시물을 걷는다. 켠 채로 두면 화면이 겹친다.
                if (measure != null) measure.enabled = false;
                if (checklist != null) checklist.SetVisible(false);

                // ★끼운 쪽이 되돌린다. 각도기를 실측 출처에 걸어 둔 채 나가면
                //   교육모드가 대본 각도 대신 실측값을 그린다(07-27 xray 사고와 같은 형태).
                RestorePlaneGauge();
                RestoreProgressRoot();
                UndockProgressRoot();
                UnlockReadyToggle(false);
                if (measure != null) measure.SetFrozen(false);   // 켠 쪽이 끈다

                ExitRealWorld();
            }
            return;
        }

        // 실측모드에 들어와 있는 동안만 측정기를 켠다(교육 브리지가 꺼진 채로 붙여 둔다).
        if (measure != null && !measure.enabled) measure.enabled = true;

        if (measure == null)
        {
            if (!warnedNoMeasure)
            {
                warnedNoMeasure = true;
                ChunaLogger.LogWarning("[실측Bridge] CervicalRomRealityMeasure가 씬에 없습니다 — 실측이 진행되지 않습니다.");
            }
            return;
        }

        StepData step = scenarioManager.CurrentStep;
        SubStepData sub = scenarioManager.CurrentSubStep;
        if (step == null || sub == null) return;

        if (!active)
        {
            active = true;
            resultToggleShown = false;
            measure.ResetAll();
            if (checklist != null) checklist.ResetChecks();
            ApplyPlaneGauge();
            DockProgressIntoResultPage();   // ★진행창을 정보패널 결과 페이지 안에(2026-09-04, ROM 한정)
            EnterRealWorld();
            ChunaLogger.Log("<color=cyan>[실측Bridge] 실측모드 진입 — 측정기를 초기화했다.</color>");
        }

        TickDockAssert();

        string name = step.stepName;
        int subNo = sub.subStepNo;
        string key = $"{name}#{subNo}";

        ApplyDirectionFor(name);

        // ★압박 방향 화살표 — 실측 substep은 x.1 능동 / x.2 압박 / x.3 복귀다.
        //   압박(x.2)에서만 켠다. 교육 브리지가 하는 것과 같은 규칙이다.
        //   ★실측에 화살표가 더 필요하다 — 정해진 끝점이 없어서 "이 방향으로 더"가 유일한 유도다.
        if (planeGauge != null && planeGauge.HasExternalSource)
        {
            bool pressing = subNo == 2 && DirectionOf(name) != CervicalRomDriver.Direction.None;
            planeGauge.SetPressGuide(pressing);
        }

        // ★실측에서는 체크리스트를 안 띄운다(2026-09-01 사용자 지시).
        //   준비 단계가 '표준자세 항목 확인'에서 '양어깨를 짚어 중심선 세우기'로 바뀌었다.
        //   ★교육모드의 체크리스트는 그대로다 — 그쪽은 CervicalRomScenarioBridge가 쥔다.
        if (checklist != null) checklist.SetVisible(false);

        // ★★진행 UI를 손 근처로 데려오는 것은 <b>재는 단계에서만</b> 한다(2026-09-03 사용자 Play).
        //   ①'준비'부터 따라오면 [다음] 토글이 손을 따라 움직여 <b>누를 수가 없다</b>.
        //     버튼이 손에 붙어 같이 도망가는 꼴이다.
        //   ②측정값이 지시문 칸을 덮어써서 <b>안내문이 뭔지 알 수가 없다</b>.
        //     준비 단계에서 읽어야 할 것은 "양어깨에 올려 중심선을 잡으세요"지 각도가 아니다.
        //   ③'결과'도 같다 — 다 잰 뒤에 측정값이 실시간으로 흔들리면 결과가 아니라 진행 중으로 보인다.
        //   → 두 단계에서는 제자리로 돌려놓고 CSV 지시문을 그대로 보여 준다.
        // ★단계마다 규약이 다르다(2026-09-03 사용자 지시).
        //   준비 : 자리는 <b>기존 진행Root 그대로</b>. 안내 멘트가 끝나면 파지 현황으로 바꾼다.
        //   측정 : 헤드셋을 게으르게 따라간다 + 측정 정보.
        //   결과 : 자리도 글도 원래대로. 측정용 숫자를 띄우지 않는다.
        // ★★<b>재는 단계를 화이트리스트로 정한다</b>(2026-09-03 재수정).
        //   종전엔 "준비·결과만 빼고 전부"였는데, 그러면 <b>'가이드'(환자 위치 설정) 단계까지</b>
        //   추종에 걸려 화면이 따라다니고 측정값이 안내문을 덮었다.
        //   2026-09-03 사용자: "환자 위치설정하는 거 나올 때도 텍스트 나오게 하라니까?"
        //   빼는 목록을 늘리는 방식은 새 단계가 생길 때마다 또 샌다 — 넣는 목록으로 뒤집는다.
        bool measuringStep = DirectionOf(name) != CervicalRomDriver.Direction.None
                             || name.EndsWith("파지", System.StringComparison.Ordinal);

        // ★★안내문을 누가 그릴지 <b>런타임에</b> 정한다(2026-09-03).
        //   종전에는 측정기의 routeReadoutToGuideUI(직렬화 필드)로 갈랐는데, 그게 씬에 1로 굳어
        //   코드 기본값을 false로 바꿔도 안 먹었다(규칙 7). 그 바람에 손 옆에도 진행Root에도
        //   안 그려져 화면이 통째로 비었다 — 사용자: "왜 아무것도 안 보이는데."
        //   런타임 대입은 직렬화를 안 타므로 씬 값에 지지 않는다.
        measure.ClaimReadout(pushReadoutToGuideUI);

        if (measuringStep)
        {
            FollowProgressRoot();
            PushReadout();
        }
        else
        {
            RestoreProgressRoot();

            // ★준비 단계 — 멘트가 끝난 뒤에만 파지 현황을 띄운다.
            //   멘트 중에 숫자로 덮으면 "무엇을 하라는 건지" 읽을 시간이 없다.
            //   자리는 안 옮긴다 — 토글이 제자리에 있어야 누를 수 있다.
            if (name == "준비" && !IsNarrationPlaying()) PushReadout();
        }

        // ★결과 단계에서는 측정을 얼린다. 안 그러면 손을 내리는 순간 0점이 풀리고,
        //   거기서 잠깐 멈추면 <b>마지막 방향을 다시 재기 시작한다</b>(09-03 사용자: 좌회전이 계속 다시 측정됨).
        bool done = name == "결과";
        measure.SetFrozen(done);

        // ★다 재고 나면 각도기를 접는다(2026-09-03 사용자 지시).
        //   결과를 읽는 자리에 눈금과 바늘이 남아 있으면 아직 재는 중으로 보인다.
        //   ★쓰고 있는 각도기 쪽을 접는다 — 실측 전용이면 측정기, 빌려 쓰는 중이면 실습 각도기.
        measure.SetGaugeHidden(done);
        if (planeGauge != null && measure.UsePracticeGauge) planeGauge.SetForceHidden(done);

        // ★★준비 단계의 [다음] 토글을 잠근다(2026-09-03).
        //   아래 IsSatisfied가 ReferenceReady로 막고 있었는데도 넘어가던 이유가 여기다 —
        //   <b>진행 경로가 둘</b>이었다. 실측 '준비' 행의 stepNo가 0이라
        //   StepData.IsGuideStep()이 참이 되고, ScenarioGuideUIController가
        //   [다음] 토글을 무조건 띄운다. 그 토글은 아무 조건 없이 NextSubStep()을 부른다.
        //   교육 '자세정렬'(역시 stepNo 0)에서 이 구멍이 안 보였던 건 PostureChecklistUI가
        //   토글을 잠가 뒀기 때문이고, 실측은 체크리스트를 안 띄우므로 잠금도 같이 사라졌다.
        //   ★어깨를 안 짚고 넘어가면 기준틀 없이 재게 되어 <b>조용히 틀린 각</b>이 나온다.
        UpdateReadyToggleLock(name);

        if (key == advancedKey) return;
        if (Time.time - lastAdvanceTime < advanceCooldown) return;

        if (!IsSatisfied(name, subNo)) return;

        advancedKey = key;
        lastAdvanceTime = Time.time;
        if (showDebugLogs) ChunaLogger.Log($"<color=cyan>[실측Bridge] {key} 완료 — 다음 단계</color>");

        // ★AutoPlay가 물고 있으면 그쪽을 끝내 준다. 직접 NextSubStep을 부르면 두 번 넘어간다.
        //   교육 브리지에서 밟은 함정과 같은 것이다.
        if (evaluator != null && evaluator.IsAutoPlayMode)
        {
            evaluator.CompleteAutoPlayExternally();
            return;
        }
        scenarioManager.NextSubStep();
    }

    /// <summary>
    /// 결과 단계에서 [다음] 토글을 띄운다. 한 번만 띄운다 —
    /// <see cref="IsSatisfied"/>는 매 프레임 불리므로 그냥 부르면 토글 상태를 계속 리셋한다.
    /// </summary>
    private void ShowResultNextToggle()
    {
        if (resultToggleShown) return;
        resultToggleShown = true;

        if (guideUI == null) guideUI = FindFirstObjectByType<ScenarioGuideUIController>(FindObjectsInactive.Include);
        if (guideUI == null)
        {
            ChunaLogger.LogWarning("[실측Bridge] ScenarioGuideUIController가 없어 결과 단계에서 넘어갈 수단이 없습니다.");
            return;
        }

        guideUI.EnableStartToggle("다음");
        ChunaLogger.Log("<color=cyan>[실측Bridge] 결과 단계 — [다음] 토글을 띄웠다.</color>");
    }

    /// <summary>진행 UI 컨트롤러. 없으면 한 번만 찾는다.</summary>
    private ScenarioGuideUIController GuideUI
    {
        get
        {
            if (guideUI == null) guideUI = FindFirstObjectByType<ScenarioGuideUIController>(FindObjectsInactive.Include);
            return guideUI;
        }
    }

    // ── 진행 UI 손 추종 ───────────────────────────────────────────────────

    /// <summary>옮길 진행Root. 인스펙터 → 교육 브리지 슬롯 → 이름 순으로 찾는다.</summary>
    private Transform ResolveProgressRoot()
    {
        if (progressRoot != null) return progressRoot;

        // ★이름으로 찾기보다 교육 브리지의 슬롯을 먼저 본다 — 이름은 바뀌면 조용히 죽는다(규칙 8).
        var eduBridge = FindFirstObjectByType<CervicalRomScenarioBridge>(FindObjectsInactive.Include);
        if (eduBridge != null && eduBridge.ProgressRoot != null)
        {
            progressRoot = eduBridge.ProgressRoot;
            return progressRoot;
        }

        GameObject byName = GameObject.Find("진행Root");
        if (byName != null)
        {
            progressRoot = byName.transform;
            return progressRoot;
        }

        return null;
    }

    /// <summary>
    /// 진행Root를 <b>헤드셋</b> 앞에 게으르게 붙인다.
    ///
    /// ★손이 아니라 헤드셋을 따라간다(2026-09-03 사용자 지시). 손을 따라가면
    ///   [다음] 토글이 손에 붙어 같이 도망가고, 파지 중에는 화면이 환자 머리에 겹친다.
    /// ★데드존 안에서는 <b>한 프레임도 안 움직인다</b>. 조금씩이라도 따라 움직이면
    ///   VR에서 잔상이 남아 눈이 피로하다 — 사용자가 "드드득한다"고 한 게 그것이다.
    /// </summary>
    private void FollowProgressRoot()
    {
        if (!followProgressRoot) return;

        Transform root = ResolveProgressRoot();
        if (root == null) return;

        // ★원래 자리를 한 번만 적어 둔다. 이걸 안 하면 실측을 한 번 돌 때마다 UI가 떠내려간다.
        // ★<b>로컬</b>로 적는다 — 진행Root는 'UI Group'의 자식이고, 그 부모를 ScenarioUIPositioner가
        //   헤드셋 기준으로 옮긴다(설정의 위치 초기화로 실측 중에도 다시 옮길 수 있다).
        //   월드로 적어 두면 부모가 옮겨진 뒤 옛 자리로 되돌아가 부모와 어긋난다.
        if (!progressCaptured)
        {
            progressCaptured = true;
            progressHomePos = root.localPosition;
            progressHomeRot = root.localRotation;
            progressHomeScale = root.localScale;
        }

        // ★1이면 아예 안 건드린다. 씬이 정해 둔 크기가 정답이라는 뜻이다.
        if (Mathf.Abs(progressScale - 1f) > 0.001f)
        {
            Vector3 want = progressHomeScale * Mathf.Max(0.01f, progressScale);
            if ((root.localScale - want).sqrMagnitude > 1e-8f) root.localScale = want;
        }

        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 head = cam.transform.position;
        Vector3 fwd = cam.transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-6f) return;      // 정확히 위/아래를 볼 때 — 그 프레임은 건너뛴다
        fwd.Normalize();

        Vector3 target = head + fwd * followDistance + Vector3.down * followDrop;

        // ★처음 붙일 때는 그냥 갖다 놓는다. 먼 데서 미끄러져 오면 그게 더 어지럽다.
        if (!followPlaced)
        {
            followPlaced = true;
            followChasing = false;
            followVelocity = Vector3.zero;
            root.SetPositionAndRotation(target, UprightLook(target, head));
            return;
        }

        // 지금 UI가 헤드셋 정면에서 몇 도 벗어나 있나 (수평 성분만 — 고개를 끄덕이는 건 무시한다)
        Vector3 toUi = root.position - head;
        toUi.y = 0f;
        float yawErr = toUi.sqrMagnitude > 1e-6f ? Vector3.Angle(toUi, fwd) : 0f;
        float posErr = Vector3.Distance(root.position, target);

        // 데드존 밖으로 나가면 쫓기 시작한다. 안에 있으면 <b>아무것도 하지 않는다</b>.
        if (!followChasing && (yawErr > followDeadZoneDeg || posErr > followMoveDeadZone))
            followChasing = true;

        if (followChasing)
        {
            root.position = Vector3.SmoothDamp(root.position, target, ref followVelocity,
                                               Mathf.Max(0.01f, followSmoothTime));

            // 안착하면 멈춘다. 데드존보다 좁게 잡아야 경계에서 붙었다 떨어졌다 하지 않는다.
            if (yawErr < followSettleDeg && posErr < followMoveDeadZone * 0.4f)
            {
                followChasing = false;
                followVelocity = Vector3.zero;
            }
        }

        // ★★<b>바라보는 각은 자리와 따로 맞춘다</b>(2026-09-03 사용자 지시).
        //   "사용자를 바라보는 각도로는 맞춰 줘" — 판이 제자리에 서 있더라도 <b>정면은 늘 사용자 쪽</b>이어야 한다.
        //   자리를 데드존으로 묶어 두면, 시술자가 옆으로 걸었을 때 판만 비스듬히 남아 글씨가 찌그러져 보인다.
        //   ★자리와 달리 회전은 잔상을 안 만든다 — 판이 이동하지 않고 제자리에서 각만 도는 것이라
        //     시야에 흐르는 궤적이 남지 않는다. 그래서 데드존을 훨씬 좁게(기본 3도) 둘 수 있다.
        //   ★그래도 <b>완전히 멈추는 구간</b>은 둔다. 매 프레임 미세하게 돌면 그것도 떨림이다.
        Quaternion wantRot = UprightLook(root.position, head);
        if (Quaternion.Angle(root.rotation, wantRot) > faceDeadZoneDeg)
        {
            float k = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.01f, faceSmoothTime));
            root.rotation = Quaternion.Slerp(root.rotation, wantRot, k);
        }
    }

    /// <summary>
    /// <b>세로축(Y) 회전만</b> 준다. 판은 언제나 똑바로 서고 수평을 유지한다.
    ///
    /// ★2026-09-03 사용자: "고개 기울였다고 같이 기울면 안 된다. 평행 유지한 채로."
    ///   LookRotation에 위아래 성분이 섞인 방향을 넣으면 판이 앞뒤로 눕는다 —
    ///   진행Root는 눈높이보다 아래(followDrop)에 있으므로 반드시 그렇게 된다.
    ///   수평 성분만 남기면 기울기(pitch)도 좌우 기울임(roll)도 0이 된다.
    ///   ScenarioUIPositioner도 같은 규약을 쓴다(lookDirection.y = 0).
    /// </summary>
    private static Quaternion UprightLook(Vector3 uiPos, Vector3 headPos)
    {
        Vector3 look = uiPos - headPos;
        look.y = 0f;
        if (look.sqrMagnitude < 1e-6f) return Quaternion.identity;
        return Quaternion.LookRotation(look, Vector3.up);
    }

    /// <summary>진행Root를 원래 자리·크기로 되돌린다.</summary>
    private void RestoreProgressRoot()
    {
        if (!progressCaptured) return;
        progressCaptured = false;

        Transform root = progressRoot;
        if (root == null) return;

        root.localPosition = progressHomePos;
        root.localRotation = progressHomeRot;
        root.localScale = progressHomeScale;
        followPlaced = false;
        followChasing = false;
        followVelocity = Vector3.zero;

        // ★지시문 칸도 되돌린다. 안 되돌리면 실측을 나간 뒤에도 마지막 측정값이 남아 있다 —
        //   모드만 바꾸면 substep 전환이 없어서 아무도 다시 안 써 준다.
        var label = GuideUI != null ? GuideUI.DescriptionLabel : null;
        if (label != null && scenarioManager != null && scenarioManager.CurrentSubStep != null)
            label.text = scenarioManager.CurrentSubStep.textInstruction;

        if (showDebugLogs) ChunaLogger.Log("<color=cyan>[실측Bridge] 진행 UI를 원래 자리로 되돌렸다.</color>");
    }

    /// <summary>안내 멘트가 울리고 있는가. 조건매니저를 못 찾으면 '안 울린다'로 본다.</summary>
    private bool IsNarrationPlaying()
    {
        if (conditionManager == null)
            conditionManager = FindFirstObjectByType<ScenarioConditionManager>(FindObjectsInactive.Include);
        return conditionManager != null && conditionManager.IsNarrationPlaying;
    }

    /// <summary>측정기가 만든 안내문을 진행 UI의 지시문 칸에 쓴다.</summary>
    private void PushReadout()
    {
        if (!pushReadoutToGuideUI) return;
        if (measure == null || !measure.RouteReadoutToGuideUI) return;

        var label = GuideUI != null ? GuideUI.DescriptionLabel : null;
        if (label == null) return;

        string text = measure.ReadoutText;
        if (string.IsNullOrEmpty(text)) return;

        // ★<b>지금 칸에 뭐가 들어 있는지</b>를 보고 정한다. "내가 마지막에 뭘 넣었나"로 판단하면
        //   substep이 바뀌는 순간 ScenarioGuideUIController가 CSV 지시문으로 덮어쓰는데
        //   측정값은 그대로라 다시 안 넣게 되고, 그 단계 내내 CSV 문구가 남는다.
        // ★같은 문자열이면 대입하지 않는다 — TMP는 대입할 때마다 메시를 다시 만든다(VR 프레임 예산).
        if (string.Equals(label.text, text, System.StringComparison.Ordinal)) return;
        label.text = text;
    }

    // ── 준비 단계 토글 잠금 ───────────────────────────────────────────────

    /// <summary>
    /// '준비'(양어깨 짚기)를 안 끝냈으면 [다음] 토글을 못 누르게 한다.
    /// ★잠근 쪽이 푼다 — 다른 단계로 넘어가거나 실측을 나가면 반드시 되돌린다.
    /// </summary>
    private void UpdateReadyToggleLock(string stepName)
    {
        bool wantLock = stepName == "준비" && measure != null && !measure.ReferenceReady;

        if (!wantLock)
        {
            // ★<b>아직 '준비'에 있을 때만</b> 다시 띄운다. 단계가 넘어간 뒤에 띄우면
            //   가이드 스텝이 아닌 자리에 [다음]이 나타난다 —
            //   ScenarioGuideUIController가 단계 전환에서 이미 걷어간 것을 우리가 도로 꺼내는 꼴이다.
            UnlockReadyToggle(stepName == "준비");
            return;
        }

        var toggle = GuideUI != null ? GuideUI.NextToggle : null;
        if (toggle == null) return;

        if (!readyToggleLocked)
        {
            readyToggleLocked = true;
            if (showDebugLogs)
                ChunaLogger.Log("<color=cyan>[실측Bridge] 준비 단계 — 어깨 기준선을 잡을 때까지 [다음]을 감춘다.</color>");
        }

        // ★★<b>잠그는 것만으로는 안 됐다</b>(2026-09-04 지적 — "다음버튼 안 나오게 하라니까 나오네").
        //   ①회색이라도 버튼이 그대로 보이고, ②ScenarioConditionManager가 나레이션 끝에
        //   보내는 '활성' 신호가 ScenarioGuideUIController.OnButtonStateUpdateRequested를 타고
        //   interactable을 <b>도로 true로 돌려놓는다.</b> 매 프레임 false를 써도 그 프레임 뒤에 뒤집힌다.
        //   → 아예 감춘다. 감추면 위 신호가 들어와도 화면에 없다.
        toggle.interactable = false;
        if (GuideUI != null) GuideUI.SetStartToggleVisible(false);
    }

    /// <param name="restoreVisible">
    /// 감춘 [다음]을 다시 띄울지. ★<b>아직 '준비' 단계일 때만</b> 참이다 —
    /// 단계가 넘어간 뒤라면 화면 주인은 ScenarioGuideUIController이므로 건드리지 않는다.
    /// </param>
    private void UnlockReadyToggle(bool restoreVisible)
    {
        if (!readyToggleLocked) return;
        readyToggleLocked = false;

        // ★우리가 감춘 것만 우리가 되돌린다(07-27 xray 사고와 같은 형태를 피한다).
        var toggle = GuideUI != null ? GuideUI.NextToggle : null;
        if (toggle != null) toggle.interactable = true;
        if (restoreVisible && GuideUI != null) GuideUI.SetStartToggleVisible(true);
        if (showDebugLogs)
            ChunaLogger.Log($"<color=cyan>[실측Bridge] [다음] 잠금 해제 (다시 띄움={restoreVisible})</color>");
    }

    private void OnDisable()
    {
        RestoreProgressRoot();
        UndockProgressRoot();
        UnlockReadyToggle(false);
        ExitRealWorld();
        resultToggleShown = false;
    }

    // ── 현실 전환 ─────────────────────────────────────────────────────────
    // ★실측은 "환자도 추나 베드도 배경도 없이 현실 위에 UI만 띄우고 실제 환자를 재는" 모드다
    //   (2026-08-31 사용자 정의). 배경·베드는 패스스루가 배경째 끄면서 사라지고,
    //   여기서 따로 치울 것은 가상 환자다.
    //
    // ★현실 모드(패스스루)는 설정의 스위치일 뿐이라 그쪽이 모드를 알 필요가 없다.
    //   대신 <b>우리가 켠 것만 우리가 되돌린다</b> — 사용자가 미리 켜 둔 패스스루는 나갈 때 그대로 둔다.
    //   상태를 켠 쪽과 끄는 쪽이 다르면 반드시 샌다(07-27 xray 사고가 그 형태였다).

    private void EnterRealWorld()
    {
        if (realWorldApplied) return;
        realWorldApplied = true;

        if (practiceSettings != null)
        {
            passthroughWasOn = practiceSettings.IsRealityModeOn;
            if (!passthroughWasOn) practiceSettings.SetRealityMode(true);
            practiceSettings.SetPatientBodyVisible(false);
        }
        else
        {
            ChunaLogger.LogWarning("[실측Bridge] PracticeSettingsController가 없어 패스스루·환자 숨김을 못 겁니다.");
        }

        hiddenByUs.Clear();
        if (alsoHideInMeasurement != null)
            foreach (GameObject go in alsoHideInMeasurement) HideOne(go);

        if (hideByNameInMeasurement != null)
        {
            foreach (string name in hideByNameInMeasurement)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                GameObject go = GameObject.Find(name.Trim());
                if (go == null)
                {
                    // ★조용히 넘어가면 "왜 아직 보이지"를 못 찾는다. 이름이 바뀌었을 수 있다.
                    ChunaLogger.LogWarning($"[실측Bridge] '{name}'을(를) 씬에서 못 찾아 숨기지 못했습니다 — " +
                                           "이름이 바뀌었는지 확인하세요.");
                    continue;
                }
                HideOne(go);
            }
        }

        ChunaLogger.Log($"<color=cyan>[실측Bridge] 현실 전환 — 패스스루 {(passthroughWasOn ? "이미 켜져 있었음" : "켬")} · " +
                        $"가상 환자 숨김 · 추가 숨김 {hiddenByUs.Count}개</color>");
    }

    /// <summary>원래 켜져 있던 것만 끈다. 끈 것만 기억했다가 나갈 때 되돌린다.</summary>
    private void HideOne(GameObject go)
    {
        if (go == null || !go.activeSelf) return;
        go.SetActive(false);
        hiddenByUs.Add(go);
    }

    private void ExitRealWorld()
    {
        if (!realWorldApplied) return;
        realWorldApplied = false;

        if (practiceSettings != null)
        {
            practiceSettings.SetPatientBodyVisible(true);
            if (!passthroughWasOn) practiceSettings.SetRealityMode(false);
        }

        foreach (GameObject go in hiddenByUs)
            if (go != null) go.SetActive(true);
        hiddenByUs.Clear();

        ChunaLogger.Log("<color=cyan>[실측Bridge] 현실 전환 해제 — 가상 환자·숨긴 오브젝트를 되돌렸다.</color>");
    }

    /// <summary>단계 이름으로 측정 방향을 정한다. 파지·준비 단계는 방향을 안 건드린다.</summary>
    private void ApplyDirectionFor(string stepName)
    {
        CervicalRomDriver.Direction d = DirectionOf(stepName);

        // ★★<b>파지 단계에서 방향을 미리 맞춰 둔다</b>(2026-09-02).
        //   종전에는 파지 단계에서 DirectionOf가 None이라 SetDirection을 아예 안 불렀다.
        //   그래서 <b>직전 방향이 그대로 남은 채</b> 중립을 잡았고(씬에 direction=6 좌회전이
        //   직렬화돼 있어 첫 파지는 늘 좌회전 기준이었다), 다음 방향으로 넘어가는 순간
        //   파지 그룹이 달라 중립이 버려졌다 — <b>방금 잡았는데 또 잡으라고 하는</b> 증상이다.
        //   2026-09-02 사용자: "이거 왜 파지 하고 파지를 한번 더해? 중립 파지 했잖아."
        //   ★기준틀(axRight·axFwd)도 파지 그룹으로 갈리므로, 틀린 틀로 잡았다가 버리는 낭비였다.
        //   파지 단계에서 그 그룹의 <b>첫 방향</b>으로 맞춰 두면 중립이 그대로 이어진다.
        if (d == CervicalRomDriver.Direction.None) d = GripStepDirection(stepName);

        if (d != CervicalRomDriver.Direction.None) measure.SetDirection(d);
    }

    /// <summary>
    /// 파지 단계가 준비하는 <b>그룹의 첫 방향</b>. CSV 순서를 따른다.
    /// ★단계 이름으로 매칭하므로 CSV의 stepName을 바꾸면 여기도 바꾼다(규칙 8).
    /// </summary>
    private static CervicalRomDriver.Direction GripStepDirection(string stepName)
    {
        switch (stepName)
        {
            case "시상면 파지": return CervicalRomDriver.Direction.Flexion;       // 굴곡 → 신전
            case "관상면 파지": return CervicalRomDriver.Direction.LateralRight;  // 우측굴 → 좌측굴 → 우회전 → 좌회전
            default:            return CervicalRomDriver.Direction.None;
        }
    }

    /// <summary>
    /// 실습 각도기를 실측 출처로 갈아 끼우고 실측 외형으로 바꾼다.
    /// ★판·채움을 끈다 — 실제 환자를 가린다. 눈금·지침·도달 마커·압박 방향 화살표는 그대로 온다.
    ///   실측에 화살표가 더 필요하다는 게 이걸 하는 이유의 절반이다(정해진 끝점이 없어
    ///   "이 방향으로 더"가 유일한 유도다).
    /// </summary>
    private void ApplyPlaneGauge()
    {
        if (measure == null) return;
        if (planeGauge == null)
        {
            ChunaLogger.LogWarning("[실측Bridge] CervicalRomPlaneGauge를 못 찾았습니다 — 각도기 없이 진행합니다.");
            return;
        }

        // ★각도기는 <b>둘 다</b> 띄운다(2026-09-03 지시). 실습 각도기도 출처를 측정기로 물려
        //   같은 값을 가리키게 한다 — 안 물리면 교육 드라이버를 읽어 다른 각을 그린다.
        //   겹치지 않게 실습 각도기를 위로 올리는 것은 측정기의 practiceGaugeRise가 한다.
        // ★★<b>안 끼우는 것과 끄는 것은 다르다</b>(2026-09-04 Play 지적 — "각도기가 뜸").
        //   종전에는 여기서 그냥 return했다. 그러면 각도기를 <b>우리 출처로 안 바꿀</b> 뿐,
        //   씬의 CervicalRomPlaneGauge는 그대로 살아서 <b>교육 드라이버의 대본 각</b>을 계속 그린다.
        //   간결 표시에서 A_2가 사라지지 않은 진범이 이것이다.
        //   → 안 쓸 거면 <b>명시적으로 접는다.</b> 접은 것은 우리가 편다(RestorePlaneGauge).
        if (!measure.UsePracticeGauge)
        {
            planeGauge.SetForceHidden(true);
            gaugeHiddenByUs = true;
            if (showDebugLogs) ChunaLogger.Log("<color=cyan>[실측Bridge] 간결 표시 — 실습 각도기를 접었다.</color>");
            return;
        }

        planeGauge.SetSource(measure);
        planeGauge.SetRealityLook(true);
        if (showDebugLogs) ChunaLogger.Log("<color=cyan>[실측Bridge] 각도기를 실측 출처로 바꿨다(판·채움 끔).</color>");
    }

    /// <summary>간결 표시에서 실습 각도기를 우리가 접었는가. 접은 쪽이 편다.</summary>
    private bool gaugeHiddenByUs;

    // ── 진행Root ↔ 정보패널 결과 페이지 ───────────────────────────────────

    private InfoPanelController InfoPanel
    {
        get
        {
            if (infoPanel == null) infoPanel = FindFirstObjectByType<InfoPanelController>(FindObjectsInactive.Include);
            return infoPanel;
        }
    }

    /// <summary>
    /// 진행Root를 결과 페이지 <b>자리</b>로 데려가고, 그 페이지를 띄운다(2026-09-04, ROM 실측 한정).
    ///
    /// ★★<b>부모를 바꾸지 않는다</b>(2026-09-04 재수정). 처음엔 결과 페이지의 자식으로 넣고
    ///   lossyScale을 상쇄해 크기를 맞추려 했는데, 결과가 "구석탱이에 작게 보이지도 않게" 였다.
    ///   진행Root는 <b>월드 Transform</b>이고 결과 페이지는 <b>캔버스 안 RectTransform</b>이다 —
    ///   캔버스 안에 월드 UI 트리를 끼우면 앵커·피벗·중첩 캔버스가 겹쳐 자리도 크기도 무너진다.
    ///   계산으로 이길 수 있는 종류가 아니다.
    ///
    /// → <b>자리만 맞춘다.</b> 결과 페이지가 화면에서 차지하는 그 자리에 진행Root를 겹쳐 놓는다.
    ///   보기에는 "패널 안에 들어가 있는" 것과 같고, 스케일은 <b>한 번도 안 건드린다</b>
    ///   (2026-09-03 사용자: "스케일은 건들지 말아봐").
    /// </summary>
    private void DockProgressIntoResultPage()
    {
        if (!dockProgressIntoResultPage || progressDocked) return;

        Transform root = ResolveProgressRoot();
        InfoPanelController panel = InfoPanel;
        GameObject page = panel != null ? panel.ResultPageObject : null;

        if (root == null || page == null)
        {
            // ★조용히 넘어가지 않는다 — 아무 일도 안 일어나면 원인을 못 찾는다.
            ChunaLogger.LogWarning("[실측Bridge] 진행Root 또는 정보패널 결과 페이지를 못 찾아 " +
                                   "진행창을 못 옮겼습니다. 진행Root는 원래 자리에 둡니다.");
            return;
        }

        // ★원래 <b>월드</b> 자리를 적어 둔다. 부모를 안 바꾸므로 로컬이 아니라 월드로 되돌린다.
        dockHomeParent = root.parent;
        dockHomeLocalPos = root.localPosition;
        dockHomeLocalRot = root.localRotation;
        dockHomeLocalScale = root.localScale;
        progressDocked = true;

        FitProgressToPage(root, page);

        panel.ShowResultPageExternally();
        PlaceProgressAtResultPage();

        ChunaLogger.Log("<color=cyan>[실측Bridge] 진행창을 정보패널 결과 페이지 자리로 옮기고 그 페이지를 띄웠다.</color>");
    }

    /// <summary>
    /// 진행창을 결과 페이지 <b>크기에 맞춰 키운다</b>(2026-09-04 — "가운데에 완전 쪼끄맣게 나오잖아").
    ///
    /// ★손으로 넣는 배율이 아니라 <b>계산</b>이다. 두 물건의 실제 크기를 재서 비율을 구한다 —
    ///   씬 실측으로는 진행 캔버스가 1024×310 @0.0005 = <b>0.512m × 0.155m</b>이고,
    ///   결과 페이지는 캔버스 스케일이 달라 훨씬 크다. 그래서 가운데 점처럼 보였다.
    /// ★<b>가로세로 같은 배율</b>이라 글씨 비율이 안 망가진다 — 통째로 커지므로 폰트도 같이 커진다.
    ///   (폰트만 따로 키우면 줄바꿈·여백이 씬 설정과 어긋난다.)
    /// ★넣은 쪽이 되돌린다 — 원래 localScale은 dockHomeLocalScale에 적어 뒀다.
    /// </summary>
    private void FitProgressToPage(Transform root, GameObject page)
    {
        if (!(page.transform is RectTransform pageRect)) return;

        Vector3 pl = pageRect.lossyScale;
        float pageW = Mathf.Abs(pageRect.rect.width * pl.x);
        float pageH = Mathf.Abs(pageRect.rect.height * pl.y);
        if (pageW < 1e-4f || pageH < 1e-4f) return;

        // 진행창이 실제로 차지하는 월드 크기 — 자식 RectTransform 중 가장 큰 것을 본다.
        float rootW = 0f, rootH = 0f;
        RectTransform[] rects = root.GetComponentsInChildren<RectTransform>(true);
        for (int i = 0; i < rects.Length; i++)
        {
            Vector3 rl = rects[i].lossyScale;
            rootW = Mathf.Max(rootW, Mathf.Abs(rects[i].rect.width * rl.x));
            rootH = Mathf.Max(rootH, Mathf.Abs(rects[i].rect.height * rl.y));
        }
        if (rootW < 1e-4f || rootH < 1e-4f)
        {
            ChunaLogger.LogWarning("[실측Bridge] 진행창 크기를 못 재서 배율을 그대로 둡니다.");
            return;
        }

        // ★긴 쪽이 페이지를 넘지 않게 <b>작은 비율</b>을 쓴다. 여백은 dockFillRatio가 준다.
        float k = Mathf.Min(pageW / rootW, pageH / rootH) * Mathf.Clamp(dockFillRatio, 0.1f, 1f);
        root.localScale = dockHomeLocalScale * k;

        ChunaLogger.Log($"<color=cyan>[실측Bridge] 진행창 배율 {k:F2}배 " +
                        $"(진행 {rootW:F2}×{rootH:F2}m → 페이지 {pageW:F2}×{pageH:F2}m)</color>");
    }

    /// <summary>
    /// 진행Root를 결과 페이지가 있는 자리에 겹쳐 놓는다. ★크기는 FitProgressToPage가 한 번만 정한다.
    /// 패널을 손으로 잡아 옮기면 따라가야 하므로 매 프레임 맞춘다.
    /// </summary>
    private void PlaceProgressAtResultPage()
    {
        if (!progressDocked) return;

        Transform root = progressRoot;
        GameObject page = InfoPanel != null ? InfoPanel.ResultPageObject : null;
        if (root == null || page == null) return;

        Transform t = page.transform;

        // ★★<b>RectTransform의 position은 '피벗' 자리다</b>(2026-09-04 재수정).
        //   이 페이지의 피벗이 <b>왼쪽 아래</b>라, transform.position을 그대로 쓰면
        //   진행창이 패널 왼쪽 아래 구석에 가서 붙는다 — 사용자 화면의 축 기즈모가 그 자리였다.
        //   → rect.center를 거쳐 <b>보이는 한가운데</b>를 구한다. 피벗이 어디든 맞는다.
        Vector3 basePos = t is RectTransform rt ? rt.TransformPoint(rt.rect.center) : t.position;

        // ★★<b>사용자 쪽으로 당긴다</b>(2026-09-04 지적 — "반대 방향을 밀면 어떡해").
        //   종전에는 t.forward로 밀었는데, 패널의 forward가 <b>사용자 반대쪽</b>을 본다.
        //   ★방향은 짐작하지 않는다(규칙 9) — <b>카메라 위치를 재서</b> 그쪽으로 당긴다.
        //     패널을 어느 방향으로 돌려 놓든 항상 사용자 쪽이 된다.
        Camera cam = Camera.main;
        Vector3 toUser = cam != null ? (cam.transform.position - basePos) : -t.forward;
        toUser = toUser.sqrMagnitude > 1e-6f ? toUser.normalized : -t.forward;

        root.SetPositionAndRotation(
            basePos + t.rotation * dockLocalPosition + toUser * dockForwardOffset, t.rotation);
    }

    /// <summary>
    /// 정보패널을 손으로 잡아 옮기면 진행창도 따라간다. ★자리만 맞춘다.
    /// ★페이지 전환은 <b>여기서 안 한다</b> — InfoPanelController가 시나리오 시작에서
    ///   실측이면 결과 페이지로 바로 켜 준다(2026-09-04). 붙들 이유가 없어졌다.
    /// </summary>
    private void TickDockAssert() => PlaceProgressAtResultPage();

    /// <summary>진행Root를 원래 부모·자리로 되돌린다. ★넣은 쪽이 뺀다.</summary>
    private void UndockProgressRoot()
    {
        if (!progressDocked) return;
        progressDocked = false;

        Transform root = progressRoot;
        if (root == null) return;

        // ★부모를 안 바꿨으므로 로컬 자리만 되돌린다. 스케일은 애초에 안 건드렸다.
        if (dockHomeParent != null && root.parent != dockHomeParent) root.SetParent(dockHomeParent, false);
        root.localPosition = dockHomeLocalPos;
        root.localRotation = dockHomeLocalRot;
        root.localScale = dockHomeLocalScale;   // ★키운 쪽이 되돌린다

        ChunaLogger.Log("<color=cyan>[실측Bridge] 진행Root를 원래 부모로 되돌렸다.</color>");
    }

    /// <summary>각도기를 교육모드 상태로 되돌린다.</summary>
    private void RestorePlaneGauge()
    {
        if (planeGauge == null) return;

        // ★우리가 접었으면 <b>먼저</b> 편다. 아래 HasExternalSource 가드보다 앞에 와야 한다 —
        //   간결 표시에서는 출처를 안 끼우므로, 그 가드에 걸리면 각도기가 교육모드에서
        //   <b>영영 접힌 채</b>로 남는다(07-27 xray 사고와 같은 형태).
        if (gaugeHiddenByUs)
        {
            planeGauge.SetForceHidden(false);
            gaugeHiddenByUs = false;
            if (showDebugLogs) ChunaLogger.Log("<color=cyan>[실측Bridge] 접었던 실습 각도기를 다시 폈다.</color>");
        }

        if (!planeGauge.HasExternalSource) return;   // 우리가 안 끼웠으면 안 건드린다

        planeGauge.SetForceHidden(false);            // 접은 쪽이 편다
        planeGauge.SetPressGuide(false);
        planeGauge.ClearSticky();
        planeGauge.SetRealityLook(false);
        planeGauge.SetSource(null);
        if (showDebugLogs) ChunaLogger.Log("<color=cyan>[실측Bridge] 각도기를 교육 출처로 되돌렸다.</color>");
    }

    private static CervicalRomDriver.Direction DirectionOf(string stepName)
    {
        switch (stepName)
        {
            case "굴곡":   return CervicalRomDriver.Direction.Flexion;
            case "신전":   return CervicalRomDriver.Direction.Extension;
            case "우측굴": return CervicalRomDriver.Direction.LateralRight;
            case "좌측굴": return CervicalRomDriver.Direction.LateralLeft;
            case "우회전": return CervicalRomDriver.Direction.RotationRight;
            case "좌회전": return CervicalRomDriver.Direction.RotationLeft;
            default:       return CervicalRomDriver.Direction.None;
        }
    }

    /// <summary>이 substep을 넘겨도 되는가.</summary>
    private bool IsSatisfied(string stepName, int subNo)
    {
        // ★준비 단계 = <b>양손을 환자 양어깨에 올려 중심선을 세운다</b>(2026-09-01 사용자 지시).
        //   종전의 표준자세 체크리스트는 실측에서 뺐다. 어깨선·중심선이 그 역할을 대신한다 —
        //   체크리스트 3번 항목("검사하는 동안 어깨가 돌아가지 않게 한다")은 말로 확인받는 것보다
        //   기준선이 떠 있는 편이 실제로 보인다.
        //   ★교육모드는 그대로 체크리스트를 쓴다.
        if (stepName == "준비") return measure.ReferenceReady;

        // ★★<b>결과창은 자동으로 넘어간다</b>(2026-09-04 회의 지시 —
        //   "시나리오가 끝나면 다음 버튼 누르지 않아도 자동으로 결과창이 나오게").
        //
        //   종전에는 [다음] 토글을 띄워 사람이 눌러야 넘어갔다. 토글은 stepNo 0에서만 자동으로
        //   뜨는데 '결과'는 stepNo 12라 안 떠서, 그걸 메우려고 강제로 띄우던 자리다.
        //   ★autoAdvanceResult를 끄면 종전(사람이 누르는 토글)으로 돌아간다 — 코드는 남겨 둔다.
        // ★★<b>결과 = 시나리오를 여기서 끝낸다</b>(2026-09-04, 두 번 물린 끝에 정리).
        //   ①넘기면 CSV 다음 행 '종료 | 가이드'로 가서 <b>[메인] 버튼</b>이 뜬다.
        //   ②안 넘기면 CompleteScenario가 안 불려 <b>결과표가 빈 채</b>로 남는다
        //     (결과는 resultTracker.FinishTracking()에서만 확정된다).
        //   → 넘기지 말고 <b>끝낸다.</b> 그래야 표가 채워지고 종료 안내도 안 뜬다.
        //   ★ScenarioCompleted 이벤트가 나가면 InfoPanelController가
        //     결과 페이지 전환·표 갱신·진행UI 숨김을 <b>전부</b> 한다. 우리가 또 할 필요가 없다.
        if (stepName == "결과")
        {
            if (!autoAdvanceResult) { ShowResultNextToggle(); return false; }

            if (!resultShown)
            {
                resultShown = true;

                // ★우리가 옮긴 진행창은 우리가 되돌린다. 끄는 것은 InfoPanelController가 한다.
                UndockProgressRoot();

                scenarioManager.CompleteScenarioExternally();
                ChunaLogger.Log("<color=cyan>[실측Bridge] 측정 끝 — 시나리오를 끝내고 결과를 확정했다.</color>");
            }
            return false;   // ★넘기지 않는다. 종료 안내로 가면 안 된다.
        }
        resultShown = false;

        // ★'기준정렬'(양손을 어깨에) 단계는 없앴다(2026-08-31).
        //   기준축을 파지선에서 세우므로 어깨를 짚을 이유가 사라졌다.
        //   옛 CSV가 남아 있어도 죽지 않게 파지와 같은 조건으로 흘려보낸다.
        if (stepName == "기준정렬" || stepName.EndsWith("파지")) return measure.NeutralReady;

        CervicalRomDriver.Direction d = DirectionOf(stepName);
        if (d == CervicalRomDriver.Direction.None) return false;

        switch (subNo)
        {
            case 1: return measure.HasActive(d);

            // ★★압박은 <b>자율</b>이다(2026-09-02 사용자 지시).
            //   압박을 하면 그 값으로 넘어가고, 안 하고 중립으로 돌아오면 <b>생략</b>으로 넘어간다.
            //   종전에는 HasPassive만 봐서, 압박이 안 잡히면 그 방향에서 영영 못 나갔다 —
            //   09-01 신전이 그 자리에서 막혔고 수동 0.0으로 남았다.
            case 2:
                if (measure.HasPassive(d)) return true;
                if (measure.IsBackToNeutral(neutralTolerance))
                {
                    measure.MarkPassiveSkipped();
                    return true;
                }
                return false;

            case 3: return measure.IsBackToNeutral(neutralTolerance);
            default: return false;
        }
    }

    private bool IsMeasurementMode()
        => DifficultyManager.Instance != null && DifficultyManager.Instance.IsMeasurementMode;

    private bool IsTargetScenario()
    {
        ScenarioData data = scenarioManager.CurrentScenario;
        return data != null && !string.IsNullOrEmpty(data.scenarioName)
               && data.scenarioName.Contains(scenarioName);
    }
}
