using UnityEngine;

/// <summary>
/// 경추 ROM 시나리오의 단계를 <see cref="CervicalRomDriver"/>에 물린다.
///
/// CSV의 stepName으로 방향을 정하고, 이름이 '압박'으로 끝나는지로 구간을 가른다.
///   굴곡      2행 — 지시 / 동작        → BeginActive
///   굴곡압박  3행 — 지시 / 유지 / 복귀 → SetOverpressure → ReturnToNeutral
///
/// ★손을 떼면 멈춘다. "환자 움직임을 손이 따라간다"는 규약이 여기서 성립한다.
///
/// 압박은 <b>손이 실제로 민 각도</b>로 들어간다. 목이 도는 중심을 기준으로
/// 손끝 중점이 회전축 둘레로 돈 각을 재서, 남은 여유 구간에 대비시킨다.
/// 손이 멈추면 머리도 멈추고, 손을 떼면 그 자리에서 멈춘다.
/// </summary>
public class CervicalRomScenarioBridge : MonoBehaviour
{
    [Header("=== 참조 (비우면 자동 탐색) ===")]
    [SerializeField] private ScenarioManager scenarioManager;
    [SerializeField] private CervicalRomDriver driver;
    [SerializeField] private ChunaPathEvaluator evaluator;
    [Tooltip("엄지·검지 파지 판정기. 있으면 접촉 게이트를 이쪽으로 본다.")]
    [SerializeField] private CervicalGripJudge gripJudge;

    [Tooltip("면 각도기. 압박 유지 단계에서 방향 화살표를 켜는 데 쓴다. 비우면 자동 탐색한다.")]
    [SerializeField] private CervicalRomPlaneGauge planeGauge;

    [Tooltip("표준자세 체크리스트. 없으면 자세정렬 단계를 게이트하지 않는다.")]
    [SerializeField] private PostureChecklistUI postureChecklist;

    [Header("=== 대상 시나리오 ===")]
    [Tooltip("이 이름의 시나리오에서만 동작한다. 다른 술기에는 개입하지 않는다.")]
    [SerializeField] private string scenarioName = "경추ROM측정";

    /// <summary>압박 진행률을 어느 각으로 뽑을지.</summary>
    public enum OverpressureSource
    {
        /// <summary>두 손을 잇는 벡터가 돈 각. ★손목만 틀어도 잡힌다.</summary>
        HandPairRotation,
        /// <summary>손끝 중점이 회전 중심 둘레로 돈 각. 손을 실제로 옮겨야 잡힌다.</summary>
        HandMidpointArc,
    }

    [Header("=== 압박 ===")]
    [Tooltip("압박 진행률의 소스.\n" +
             "중점호   — 손끝 중점이 목 밑동 둘레로 돈 각. 지렛대 약 0.21m.\n" +
             "손쌍회전 — 두 손을 잇는 벡터가 돈 각. 지렛대가 두 손 간격(약 0.11m)뿐이다.\n" +
             "★2026-08-25 실측으로 중점호를 기본으로 했다. 손쌍회전은 지렛대가 절반이라\n" +
             "  손 트래킹 1cm 지터가 5.2°로 증폭된다 — 여유 구간이 7.5~13°인데 그 태반이다.\n" +
             "  실제 로그에서도 손쌍회전은 ±5°로 요동쳤고 중점호는 ±3° 안에 머물렀다.\n" +
             "둘 다 매 프레임 재서 로그에 같이 찍는다. Play 중에 바꿔 가며 비교하면 된다.")]
    [SerializeField] private OverpressureSource overpressureSource = OverpressureSource.HandMidpointArc;

    [Tooltip("압박 유지 substep에서 0 → 1까지 가는 데 걸리는 시간(초).\n" +
             "★손끝을 하나도 못 찾았을 때만 쓰는 폴백이다.")]
    [SerializeField] private float overpressureRampSeconds = 3f;

    [Tooltip("압박 각도의 지터를 걸러내는 시간(초). 0이면 끈다.\n" +
             "손 트래킹이 떨리면 여유 구간(7~13°)에 비해 무시 못 할 각이 실려 게이지가 튄다.")]
    [SerializeField] private float overpressureSmoothTime = 0.15f;

    [Tooltip("파지 미끄러짐 허용치(m). 0이면 검사하지 않는다.\n" +
             "★머리는 강체다 — 양손이 제대로 잡고 있으면 '두 손 간격'과 '회전 중심까지의 반지름'이\n" +
             "  압박 내내 보존된다. 제자리에서 손목만 틀면 손이 머리 위를 미끄러지므로 둘이 깨진다.\n" +
             "  이 값을 넘으면 진행을 인정하지 않는다(되돌리지는 않고 그 자리에서 멈춘다).\n" +
             "실측 참고: 정상적으로 밀 때 반지름은 0.21→0.23m(2cm), 두 손 간격은 0.10→0.11m로 움직였다.")]
    [SerializeField] private float gripSlipTolerance = 0.04f;

    [Tooltip("압박 한계에 닿은 뒤 그 자리에서 버텨야 하는 시간(초).\n" +
             "★끝느낌은 '닿는 순간'이 아니라 '버티는 동안' 읽는 것이다. 닿자마자 넘기면\n" +
             "  스치듯 지나가도 통과된다. 손이 물러나면 타이머는 0으로 되돌아간다.")]
    [SerializeField] private float overpressureHoldSeconds = 3f;

    [Tooltip("이 진행률 이상이면 '압박 한계에 닿았다'로 보고 유지 타이머를 센다.\n" +
             "1.0으로 두면 손 지터 한 번에 타이머가 끊긴다.")]
    [Range(0.8f, 1f)] [SerializeField] private float overpressureHoldThreshold = 0.97f;

    [Tooltip("가이드손을 켜고 끌 때 접촉 상태를 이만큼 붙잡는다(초).\n" +
             "★손 트래킹이 한 프레임 튀어도 가이드손이 파르르 떨지 않게 한다. 0이면 끈다.")]
    [SerializeField] private float guideToggleDebounce = 0.25f;

    [Tooltip("목표에 못 닿은 채 이 시간이 지나면 '다음' 버튼을 띄운다(초).\n" +
             "★자동으로 넘기지는 않는다 — 넘길지 말지는 사람이 정한다.")]
    [SerializeField] private float stallButtonSeconds = 20f;

    [Tooltip("목표에 못 닿아도 이 시간이 지나면 <b>자동으로</b> 넘긴다(초).\n" +
             "★0이면 자동 진행하지 않는다(기본값). 위의 '다음' 버튼으로만 넘어간다.")]
    [SerializeField] private float stallTimeoutSeconds = 0f;

    [Header("=== 진행 UI 자동 배치 ===")]
    // ★두 번만 옮긴다.
    //   ① 시상면 파지(시술자가 환자 <b>측면</b>에 선다) → 좌·우 포인트 중 한쪽으로. 최초 1회.
    //   ② 관상면·횡단면 파지(다시 <b>정면</b>을 본다)   → 정면 포인트로.
    //   둘 다 사람이 중간에 손으로 옮겨 놨더라도 <b>무시하고</b> 그 포인트로 데려간다.
    [Tooltip("옮길 UI 루트. 보통 '진행Root'. 비우면 이 기능이 꺼진다.")]
    [SerializeField] private Transform progressRoot;

    [Tooltip("환자 좌측 포인트. 시상면(굴곡·신전)에서 시술자가 환자 우측에 서면 여기로 간다.")]
    [SerializeField] private Transform sidePointLeft;

    [Tooltip("환자 우측 포인트. 시술자가 환자 좌측에 서면 여기로 간다.")]
    [SerializeField] private Transform sidePointRight;

    [Tooltip("정면 포인트. 측굴·회전으로 넘어갈 때 여기로 돌아온다.")]
    [SerializeField] private Transform frontPoint;

    [Tooltip("시술자가 선 쪽의 <b>반대편</b> 포인트로 간다. 화면이 시술자 몸에 가리지 않게 하는 것이다.\n" +
             "★반대로 나오면 이 체크를 끈다. 각도기의 autoSideByViewer와 같은 규약이다.")]
    [SerializeField] private bool placeOppositeToViewer = true;

    [Tooltip("정중선에 이만큼 가까우면 좌우를 판단하지 않고 좌측 포인트를 쓴다 (m).")]
    [SerializeField] private float progressSideDeadZone = 0.08f;

    [Tooltip("측면 배치를 트리거할 단계 이름(시상면 파지).")]
    [SerializeField] private string sideGripStepName = "시상면 파지";

    [Tooltip("표준자세 체크리스트를 띄울 단계 이름.")]
    [SerializeField] private string postureStepName = "자세정렬";

    [Tooltip("정면 복귀를 트리거할 단계 이름들.")]
    [SerializeField] private string[] frontGripStepNames = { "관상면 파지", "횡단면 파지" };

    [Header("=== 단계 효과음 (2026-08-28 신규) ===")]
    [Tooltip("파지 성립·단계 완료를 소리로 가른다. ★신규 필드라 씬 값이 없어 코드 기본값이 먹는다.")]
    [SerializeField] private bool playStepCues = true;
    [Tooltip("유지 타이머가 도는 동안 1초마다 틱. 마지막 1초는 높은 소리.")]
    [SerializeField] private bool playHoldTick = true;
    [SerializeField, Range(0f, 1f)] private float stepCueVolume = 0.7f;
    [SerializeField, Range(0f, 1f)] private float holdTickVolume = 0.5f;
    [Tooltip("비우면 Resources/Audio/StepComplete를 쓴다.")]
    [SerializeField] private AudioClip gripCueClip;
    [SerializeField] private AudioClip stepDoneClip;
    [SerializeField] private AudioClip holdTickClip;
    [SerializeField] private AudioClip holdTickLastClip;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = true;

    [Tooltip("압박 구간에서 파지 상태·두 방식의 회전각·진행률을 주기적으로 찍는다.\n" +
             "'밀어도 반응이 없다'가 파지 때문인지 각도 때문인지 여기서 갈린다.\n" +
             "원인을 잡고 나면 끈다.")]
    [SerializeField] private bool logOverpressure = true;

    [Tooltip("진단 로그 간격(초). 0이면 매 프레임 — 프레임을 잡아먹으니 임시로만 쓴다.")]
    [SerializeField] private float overpressureLogInterval = 0.25f;

    private string lastStepKey;
    private string advancedKey;      // 같은 substep을 두 번 넘기지 않게
    private float stepEnteredTime;
    private float leftTouchHold, rightTouchHold;   // 접촉 디바운스 잔여시간(초)
    private bool loggedHideLeft, loggedHideRight;  // 상태가 바뀔 때만 로그
    private bool lastWasGripStep;    // 직전 단계가 파지였는가(벗어날 때 재생을 끝내려고)
    private bool postureGateHeld;    // 체크리스트 때문에 드라이버를 세워 두고 있는가
    private string gaugePrimedKey;   // 이 파지 단계에서 각도기를 이미 띄웠는가
    private bool gripCuePlayed;      // 이 파지 단계에서 파지음을 이미 울렸는가
    private int lastTickSecond = -1; // 유지 타이머 틱 — 초가 바뀔 때만 운다
    private AudioSource cueSource;
    private AudioClip defaultCueClip;
    private bool stallButtonShown;   // 이 substep에서 다음 버튼을 이미 띄웠는가
    private bool warnedNotTarget;
    private float overpressureProgress;
    private ScenarioGuideUIController guideUI;   // ProgressCircle을 직접 그리기 위해 잡아 둔다
    private bool sideProgressPlaced;       // 측면 배치를 이미 했는가(최초 1회 규약)
    private float overpressureHeldTime;    // 압박 한계에서 버틴 시간(초). 목표는 overpressureHoldSeconds.
    private bool active;

    // ── 압박 기준점. 두 방식을 각각 따로 잡는다. ──
    private bool arcStarted;             // ①중점호 기준을 잡았는가
    private Vector3 arcStartArm;         // 그때의 회전 중심→손끝중점 벡터
    private float arcRadius;             // 그 벡터의 길이(m). 짧으면 각이 튄다.
    private float arcStartRadius;        // 압박 시작 시점의 반지름. 강체 구속 검사 기준.
    private float pairStartSpan;         // 압박 시작 시점의 두 손 간격. 같은 용도.
    private bool pairStarted;            // ②손쌍회전 기준을 잡았는가
    private Vector3 pairStartVector;     // 그때의 A손→B손 벡터
    private float pairSpan;              // 두 손 사이 거리(m)
    private float sweptSmoothed;         // 지터를 거른 회전각
    private float sweptVelocity;         // SmoothDamp용
    private float lastPressLogTime = -99f;

    private void Awake()
    {
        if (scenarioManager == null) scenarioManager = FindFirstObjectByType<ScenarioManager>();
        if (driver == null) driver = FindFirstObjectByType<CervicalRomDriver>();
        if (evaluator == null) evaluator = FindFirstObjectByType<ChunaPathEvaluator>();
        if (gripJudge == null) gripJudge = FindFirstObjectByType<CervicalGripJudge>();
        if (planeGauge == null) planeGauge = FindFirstObjectByType<CervicalRomPlaneGauge>(FindObjectsInactive.Include);
        if (postureChecklist == null) postureChecklist = FindFirstObjectByType<PostureChecklistUI>(FindObjectsInactive.Include);

        EnsureRomCompanions();

        if (driver == null)
        {
            ChunaLogger.LogWarning("[ROM Bridge] CervicalRomDriver를 찾지 못했습니다. 환자에 붙였는지 확인하세요.");
            enabled = false;
            return;
        }

        // ★시작할 때 상태를 남긴다. 조용히 아무것도 안 하는 상태를 구분할 수 없으면
        //   '컴포넌트를 안 붙였다'와 '붙였는데 대상이 아니다'가 똑같아 보인다.
        ChunaLogger.Log($"<color=cyan>[ROM Bridge] 시작 — 대상 시나리오 '{scenarioName}' · " +
                        $"드라이버 {(driver != null ? driver.name : "없음")} · " +
                        $"판정기 {(evaluator != null ? "있음" : "없음(접촉 게이트 없이 진행)")} · " +
                        $"시나리오매니저 {(scenarioManager != null ? "있음" : "★없음")}</color>");
    }

    private void Update()
    {
        if (scenarioManager == null || driver == null) return;

        StepData step = scenarioManager.CurrentStep;
        SubStepData sub = scenarioManager.CurrentSubStep;
        if (step == null || sub == null) return;

        if (!IsTargetScenario())
        {
            if (active)
            {
                active = false;
                driver.Paused = false;
                // ★술기를 벗어날 때만 가이드 손을 머리뼈에서 뗀다. 안 떼면 다음 술기까지
                //   손이 환자 머리에 매달려 따라다닌다.
                evaluator?.ReleaseGuideHandHoldInternal();
                planeGauge?.SetPressGuide(false);   // 화살표를 켠 채 나가면 다음 술기까지 남는다
                planeGauge?.ClearSticky();          // 각도기도 같이 접는다
                driver.ClearPrimedDirection();
            }
            if (!warnedNotTarget)
            {
                warnedNotTarget = true;
                ScenarioData data = scenarioManager.CurrentScenario;
                ChunaLogger.Log($"<color=yellow>[ROM Bridge] 대상 시나리오가 아니라 개입하지 않는다 — " +
                                $"현재 '{(data != null ? data.scenarioName : "(없음)")}' vs 설정 '{scenarioName}'</color>");
            }
            return;
        }
        if (!active)
        {
            active = true;
            Log($"대상 시나리오 진입 — 여기서부터 목 각도를 굴린다");
        }

        string key = $"{step.stepName}#{sub.subStepNo}";
        if (key != lastStepKey)
        {
            // ★파지를 벗어나는 순간 재생을 마지막 프레임으로 땡겨 끝낸다.
            //   그래야 위치를 쓰는 코드가 사라지고 머리뼈 자식으로서 고개를 따라간다.
            if (lastWasGripStep && !IsGripStep(step.stepName))
            {
                evaluator?.ForceFinishGuideHoldInternal();
            }
            lastWasGripStep = IsGripStep(step.stepName);

            // ★자세정렬 단계에서만 표준자세 체크리스트를 띄운다(2026-08-27 회의 결정).
            //   세 줄을 전부 체크해야 다음으로 넘어간다 — 게이팅은 UpdatePostureGate가 한다.
            UpdatePostureGate(step.stepName);

            lastStepKey = key;
            stepEnteredTime = Time.time;
            stallButtonShown = false;
            ApplyGripPair(step.stepName);
            OnSubStepEntered(step.stepName, sub.subStepNo);
        }

        // ★파지가 성립하는 <b>순간</b> 각도기를 띄운다(2026-08-27 회의 결정).
        //   여기는 매 프레임 도는 자리다 — 처음엔 단계 진입 블록에 넣었다가
        //   "진입 시점엔 아직 손을 안 잡았다"는 이유로 영영 안 불렸다(2026-08-28 Play 지적).
        UpdateGaugeOnGrip(step.stepName);

        AdvanceOverpressure(step.stepName, sub.subStepNo);
        DriveProgressCircle(step.stepName, sub);
        SuppressGuideForTouchedHands(step.stepName, sub.subStepNo);

        // 손을 떼면 그 자리에서 멈춘다.
        // ★중립 복귀(x.3)도 예외가 아니다 — 예전엔 복귀만 파지를 면제했는데,
        //   사용자 지시로 복귀도 파지를 유지해야 움직이도록 바꿨다(2026-08-26).
        //   시술자가 손을 댄 채로 따라 내려오는 게 실제 술기다.
        driver.Paused = !BothHandsTouching();

        TryAdvanceWhenDone(step.stepName, sub.subStepNo, key);
    }

    /// <summary>
    /// 동작·압박·복귀 substep은 <b>타이머가 아니라 목표 도달로</b> 끝낸다.
    /// CSV의 duration을 0으로 두면 AutoPlay가 스스로 완료하지 않으므로 여기서만 넘긴다
    /// (ScenarioConditionManager의 subStepToken 가드가 중복 진행을 막는다).
    /// </summary>
    private void TryAdvanceWhenDone(string stepName, int subStepNo, string key)
    {
        if (advancedKey == key) return;
        if (DirectionOf(stepName) == CervicalRomDriver.Direction.None) return;

        // ★x.1은 지시(나레이션) substep이다. 능동이든 압박이든 여기서는 판정하지 않는다 —
        //   나레이션이 끝나면 기존 파이프라인이 넘긴다.
        //   압박 x.1을 아래 '복귀' 가지로 흘려보냈다가 머리가 중립이라는 이유로
        //   지시가 통째로 건너뛰어졌다(2026-08-25 실측: "굴곡압박 1 진행 — 중립 복귀 완료").
        if (subStepNo < 2) return;

        bool isOverpressure = stepName.EndsWith("압박", System.StringComparison.Ordinal);
        bool done;
        string reason;

        if (!isOverpressure)
        {
            done = driver.ActiveReached;
            reason = $"능동 끝점 도달 {driver.CurrentAngle:F0}°";
        }
        else if (subStepNo == 2)
        {
            // ★압박 한계에 닿았다고 바로 넘기지 않는다 — 그 자리에서 버텨야 끝느낌을 읽는다.
            //   손이 도로 물러나면 타이머는 0으로 되돌아간다(AdvanceOverpressure에서 관리).
            done = overpressureHeldTime >= overpressureHoldSeconds;
            reason = $"압박 유지 완료 {driver.CurrentAngle:F0}° " +
                     $"({overpressureHoldSeconds:F0}초 유지 · 부족각 {driver.DeficitAngle:F1}°)";
        }
        else
        {
            done = driver.AtNeutral;
            reason = "중립 복귀 완료";
        }

        // ★막혔을 때 <b>자동으로 넘기지 않는다</b>. '다음' 버튼을 띄우고 사람이 정한다
        //   (2026-08-26 사용자 지시: "자동진행이 아니라 다음 버튼이 활성화 되야 한다").
        //   공용 20초 폴백(ScenarioConditionManager.progressTimeout)은 <b>PassiveStretch에서는 안 돈다</b> —
        //   그 경로가 currentCondition=null·StopConditionCheck()로 조건 폴링을 아예 꺼 버리기 때문이다.
        //   그래서 여기서 직접 띄운다.
        if (!done)
        {
            if (!stallButtonShown && stepEnteredTime > 0f
                && Time.time - stepEnteredTime > stallButtonSeconds)
            {
                stallButtonShown = true;
                ChunaLogger.LogWarning($"<color=orange>[ROM Bridge] {stepName} {subStepNo}가 " +
                                       $"{stallButtonSeconds:F0}초 동안 목표에 못 닿았다 — '다음' 버튼을 띄운다. " +
                                       $"현재 {driver.CurrentAngle:F0}° / 목표 {driver.ActiveTargetAngle:F0}°</color>");
                if (guideUI == null) guideUI = FindFirstObjectByType<ScenarioGuideUIController>(FindObjectsInactive.Include);
                guideUI?.ClearExternalProgress();
                guideUI?.EnableStartToggle();
            }

            // stallTimeoutSeconds가 0 이하면 자동 진행은 아예 하지 않는다(기본값).
            if (stallTimeoutSeconds > 0f && stepEnteredTime > 0f
                && Time.time - stepEnteredTime > stallTimeoutSeconds)
            {
                ChunaLogger.LogWarning($"<color=orange>[ROM Bridge] {stepName} {subStepNo}가 " +
                                       $"{stallTimeoutSeconds:F0}초 동안 목표에 못 닿아 넘긴다. " +
                                       $"현재 {driver.CurrentAngle:F0}° / 목표 {driver.ActiveTargetAngle:F0}°</color>");
                done = true;
                reason = "정체 타임아웃";
            }
            else
            {
                return;
            }
        }

        advancedKey = key;
        Log($"{stepName} {subStepNo} 진행 — {reason}");
        PlayStepCue(stepDoneClip, $"{stepName} {subStepNo} 완료");

        // ★측정값을 남긴다. 방향이 바뀌면 각도가 사라지는데, 경추ROM은 채점 지표가 없어
        //   (PassiveStretch는 0점) 이 각도가 곧 결과다. 목표에 못 닿고 타임아웃으로
        //   넘어갔어도 그때 도달한 각이 그 판의 측정값이다.
        if (!isOverpressure) driver.RecordActiveReached();
        else if (subStepNo == 2) driver.RecordPassiveReached();

        // ★AutoPlay가 돌고 있으면 그쪽을 끝내 준다. 직접 NextSubStep을 부르면 안 된다.
        //   경추ROM의 동작 substep은 애니도 duration도 없어 AutoPlay가 스스로 못 끝난다.
        //   그대로 두면 다음 substep의 나레이션이 WaitForAutoPlayComplete()에서 무한 대기하고,
        //   여기서 NextSubStep까지 부르면 파이프라인 진행과 겹쳐 두 번 넘어간다.
        //   완료 처리만 하면 기존 경로(OnAutoPlayCompleted · ConditionManager)가 알아서 넘긴다.
        if (evaluator != null && evaluator.IsAutoPlayMode)
        {
            evaluator.CompleteAutoPlayExternally();
            return;
        }

        scenarioManager.NextSubStep();
    }

    /// <summary>
    /// ProgressCircle을 ROM 전용 타이머로 직접 그린다.
    ///
    /// ★기본 규칙으로는 둘 다 안 뜬다 —
    ///   파지 유지: 손 녹화(handTrackingFileName)를 걸어 둔 순간 <c>HasHandTracking()</c>에 걸려 숨겨진다.
    ///   압박 유지: duration이 0이라 애초에 안 뜬다(duration을 주면 AutoPlay가 판정과 무관하게 밀어 버린다).
    ///   그래서 값을 바깥에서 밀어넣는다. 다른 술기는 이 경로를 타지 않는다.
    /// </summary>
    private void DriveProgressCircle(string stepName, SubStepData sub)
    {
        if (guideUI == null)
        {
            guideUI = FindFirstObjectByType<ScenarioGuideUIController>(FindObjectsInactive.Include);
            if (guideUI == null) return;
        }

        bool isOverpressure = !string.IsNullOrEmpty(stepName)
                              && stepName.EndsWith("압박", System.StringComparison.Ordinal);

        // ① 압박 종단점 유지 — 한계에 닿아 버티는 동안만 센다. 손이 물러나면 0으로 되돌아간다.
        if (isOverpressure && sub.subStepNo == 2 && overpressureHoldSeconds > 0f)
        {
            float remain = overpressureHoldSeconds - overpressureHeldTime;
            guideUI.SetExternalProgress(remain, overpressureHoldSeconds);
            PlayHoldTick(remain);
            return;
        }

        // ② 파지 유지 — 양손 접촉 게이트가 열려 있는 동안만 찬다.
        //    AutoPlay 진행률이 곧 게이트가 열려 있던 시간이라 그대로 쓴다.
        // ★파지는 substep 1에서 안내와 유지가 같이 돈다(voiceGate). subStepNo를 보지 않는다.
        if (!isOverpressure && sub.duration > 0
            && IsGripStep(stepName) && evaluator != null
            && evaluator.TryGetAutoPlayProgress(out float autoPlay01))
        {
            float remain = sub.duration * (1f - autoPlay01);
            guideUI.SetExternalProgress(remain, sub.duration);
            PlayHoldTick(remain);
            return;
        }

        guideUI.ClearExternalProgress();
        lastTickSecond = -1;   // 타이머가 없는 구간에서는 카운터를 비운다
    }

    /// <summary>
    /// 시술자가 이미 손을 댄 쪽의 가이드 손을 끈다. 떼면 다시 켜진다.
    ///
    /// ★이걸 부르는 곳이 여태 <b>두개골 컨트롤러뿐</b>이었다. 경추ROM에는 아무도 없어서
    ///   손을 대도 가이드가 그대로 겹쳐 보였다(2026-08-26 사용자 지적).
    /// ★파지점 두 개(A·B)에 어느 손이 닿았는지로 본다 — 한 손이 둘 중 아무 곳에나 닿으면
    ///   그 손은 '제자리에 갖다 댄' 것으로 친다.
    /// </summary>
    private void SuppressGuideForTouchedHands(string stepName, int subStepNo)
    {
        if (evaluator == null) return;

        // ★가이드 손을 감추는 구간 = <b>평가 설명 단계</b>(시상면평가·관상면평가·횡단면평가).
        //   중립 복귀에서는 계속 보여야 한다 — 손을 대고 따라 내려오는 구간이기 때문이다.
        //   (2026-08-26: 처음에 중립 복귀로 잘못 걸었다가 사용자 정정.)
        bool returning = !string.IsNullOrEmpty(stepName)
                         && stepName.EndsWith("평가", System.StringComparison.Ordinal);

        bool leftTouching, rightTouching;
        if (returning)
        {
            leftTouching = rightTouching = true;   // 숨김 쪽으로 몰아넣는다
            leftTouchHold = rightTouchHold = guideToggleDebounce;
        }
        else if (gripJudge != null && gripJudge.TryGetGripState(out bool aLeft, out bool aRight,
                                                                out bool bLeft, out bool bRight))
        {
            leftTouching = aLeft || bLeft;
            rightTouching = aRight || bRight;
        }
        else
        {
            leftTouching = evaluator.IsLeftHandTouchingPatient;
            rightTouching = evaluator.IsRightHandTouchingPatient;
        }

        // ★깜빡임 방지 — 접촉 판정이 한 프레임 튀어도 바로 켜고 끄지 않는다.
        //   붙었다 떨어졌다 하는 트래킹 특성상 그냥 두면 가이드손이 파르르 떤다.
        leftTouchHold = leftTouching ? guideToggleDebounce : Mathf.Max(0f, leftTouchHold - Time.deltaTime);
        rightTouchHold = rightTouching ? guideToggleDebounce : Mathf.Max(0f, rightTouchHold - Time.deltaTime);

        bool hideLeft = leftTouchHold > 0f;
        bool hideRight = rightTouchHold > 0f;

        evaluator.SuppressGuideHandInternal(true, hideLeft);
        evaluator.SuppressGuideHandInternal(false, hideRight);

        // ★표시의 <b>주인은 여기 하나</b>다. 예전에는 평가기·시나리오매니저·고정 코루틴이
        //   제각각 켜고 꺼서 순서에 따라 결과가 달라졌다(2026-08-26 "on/off가 원활하지 않다").
        //   붙여 놓은 동안에는 접촉 상태만 보고 매 프레임 못박는다.
        //   SetVisible은 렌더러를 캐시하므로 매 프레임 불러도 싸다.
        evaluator.ForceGuideHandVisible(true, !hideLeft);
        evaluator.ForceGuideHandVisible(false, !hideRight);

        if (showDebugLogs && (hideLeft != loggedHideLeft || hideRight != loggedHideRight))
        {
            loggedHideLeft = hideLeft; loggedHideRight = hideRight;
            string why = returning ? "숨김(평가 설명)" : "숨김(접촉)";
            Log($"가이드손 — 왼손 {(hideLeft ? why : "표시")} · 오른손 {(hideRight ? why : "표시")}");
        }
    }

    /// <summary>
    /// 파지 단계에서 양손이 접촉하는 순간 각도기를 띄운다. 한 단계에 한 번만 부른다.
    /// ★방향만 잡고 움직이지 않는다 — 동작은 다음 단계의 BeginActive가 시작한다.
    /// </summary>
    private void UpdateGaugeOnGrip(string stepName)
    {
        if (!IsGripStep(stepName)) { gaugePrimedKey = null; gripCuePlayed = false; return; }

        CervicalRomDriver.Direction d = FirstDirectionAfterGrip(stepName);
        if (d == CervicalRomDriver.Direction.None) return;

        // ★각도기는 파지 단계에 들어가는 <b>즉시</b> 띄운다(2026-08-28 사용자 지적).
        //   IsGripped(엄지·검지가 접촉점 구체에 정확히 닿음)를 기다리게 해 놨더니
        //   손을 대도 각도기가 안 떴다. 판정은 진행용이고, 표시는 기다릴 이유가 없다.
        if (gaugePrimedKey != stepName)
        {
            gaugePrimedKey = stepName;
            driver.PrepareDirection(d);
        }

        // 소리는 실제로 잡혔을 때만 — 그건 판정이 맞다.
        if (!gripCuePlayed && BothHandsTouching())
        {
            gripCuePlayed = true;
            PlayStepCue(gripCueClip != null ? gripCueClip : Resources.Load<AudioClip>("Audio/RomGrip"),
                        "파지 성립");
        }
    }

    /// <summary>남은 초가 바뀌는 순간에만 한 번 운다. 마지막 1초는 다른 소리.</summary>
    private void PlayHoldTick(float remaining)
    {
        if (!playHoldTick) { lastTickSecond = -1; return; }

        int sec = Mathf.CeilToInt(remaining);
        if (sec == lastTickSecond) return;
        lastTickSecond = sec;
        if (sec <= 0) return;   // 0초 = 완료 — 완료음이 담당한다

        AudioClip clip = sec <= 1 && holdTickLastClip != null ? holdTickLastClip : holdTickClip;
        if (clip == null) clip = Resources.Load<AudioClip>(sec <= 1 ? "Audio/RomTickLast" : "Audio/RomTick");
        if (clip == null) return;

        EnsureCueSource();
        cueSource.PlayOneShot(clip, holdTickVolume);
    }

    private void EnsureCueSource()
    {
        if (cueSource != null) return;
        cueSource = gameObject.GetComponent<AudioSource>();
        if (cueSource == null)
        {
            cueSource = gameObject.AddComponent<AudioSource>();
            cueSource.playOnAwake = false;
            cueSource.spatialBlend = 0f;   // 2D — 손이 시야를 벗어나도 들려야 한다
        }
    }

    /// <summary>
    /// 단계 구분을 소리로도 가른다(2026-08-28 사용자 지시).
    /// ★유지 타이머 틱은 두개골에서 쓰던 공용 <see cref="HoldTickAudio"/>를 그대로 쓴다 —
    ///   Resources/Audio/TimerTick·TimerTickLast가 이미 있고 소리가 통일된다.
    /// </summary>
    private void PlayStepCue(AudioClip clip, string reason)
    {
        if (!playStepCues) return;

        AudioClip use = clip;
        if (use == null)
        {
            if (defaultCueClip == null) defaultCueClip = Resources.Load<AudioClip>("Audio/RomStepDone");
            use = defaultCueClip;
        }
        if (use == null) return;

        EnsureCueSource();
        cueSource.PlayOneShot(use, stepCueVolume);
        Log($"효과음 — {reason}");
    }

    /// <summary>
    /// 경추ROM에 필요한 컴포넌트를 런타임에 붙인다. ★씬에서 사람이 배치할 게 없어야 한다.
    ///
    /// 씬에 직렬화하지 않으므로 씬 파일이 바뀌지 않고, 이미 있으면 그대로 쓴다.
    /// ★실측 측정기는 <b>꺼진 채로</b> 붙인다 — 켜는 건 실측모드에 들어갈 때 실측 브리지가 한다.
    ///   켜 두면 교육모드에서 실측 리드아웃과 각도기가 같이 떠 화면이 겹친다.
    /// </summary>
    private void EnsureRomCompanions()
    {
        if (postureChecklist == null)
        {
            postureChecklist = gameObject.AddComponent<PostureChecklistUI>();
            Log("표준자세 체크리스트를 붙였다(런타임).");
        }

        var measure = FindFirstObjectByType<CervicalRomRealityMeasure>(FindObjectsInactive.Include);
        if (measure == null)
        {
            measure = gameObject.AddComponent<CervicalRomRealityMeasure>();
            measure.enabled = false;   // 실측모드에 들어갈 때 켜진다
            Log("실측 측정기를 붙였다(런타임, 꺼진 상태).");
        }

        if (FindFirstObjectByType<CervicalRomMeasurementBridge>(FindObjectsInactive.Include) == null)
        {
            gameObject.AddComponent<CervicalRomMeasurementBridge>();
            Log("실측 브리지를 붙였다(런타임).");
        }

        // 실습모드에서 손 측정각을 머리 위에 띄우는 검증용 프로브(A-12). 판정에는 관여하지 않는다.
        if (FindFirstObjectByType<CervicalRomHandAngleProbe>(FindObjectsInactive.Include) == null)
        {
            gameObject.AddComponent<CervicalRomHandAngleProbe>();
            Log("손각도 프로브를 붙였다(런타임).");
        }
    }

    /// <summary>
    /// 표준자세 체크리스트를 켜고, 다 체크되기 전에는 진행을 막는다.
    ///
    /// ★막는 방법은 드라이버 일시정지다 — AutoPlay·나레이션 파이프라인을 건드리지 않는다.
    ///   체크리스트가 씬에 없으면 아무것도 하지 않는다(없다고 진행이 막히면 원인을 못 찾는다).
    /// </summary>
    private void UpdatePostureGate(string stepName)
    {
        if (postureChecklist == null) return;

        bool onPostureStep = stepName == postureStepName;
        postureChecklist.SetVisible(onPostureStep);

        if (onPostureStep && !postureChecklist.AllChecked) driver.Paused = true;
        else if (postureGateHeld) driver.Paused = false;

        postureGateHeld = onPostureStep && !postureChecklist.AllChecked;
    }

    /// <summary>파지 단계인가. 이름이 '파지'로 끝나면 파지로 본다(시상면·관상면·횡단면 파지).</summary>
    private bool IsGripStep(string stepName)
        => !string.IsNullOrEmpty(stepName) && stepName.EndsWith("파지", System.StringComparison.Ordinal);

    /// <summary>
    /// 그 파지 다음에 오는 첫 동작 방향. 각도기를 미리 띄우는 데만 쓴다.
    /// ★면이 정해지면 방향도 정해진다 — 시상면은 굴곡부터, 관상·횡단은 우측부터다.
    /// </summary>
    private static CervicalRomDriver.Direction FirstDirectionAfterGrip(string stepName)
    {
        if (string.IsNullOrEmpty(stepName)) return CervicalRomDriver.Direction.None;
        if (stepName.StartsWith("시상면")) return CervicalRomDriver.Direction.Flexion;
        if (stepName.StartsWith("관상면")) return CervicalRomDriver.Direction.LateralRight;
        if (stepName.StartsWith("횡단면")) return CervicalRomDriver.Direction.RotationRight;
        return CervicalRomDriver.Direction.None;
    }

    /// <summary>
    /// 진행 UI를 파지 단계에 맞춰 옮긴다.
    ///
    /// ★사람이 손으로 옮겨 놨더라도 무시하고 포인트로 데려간다 — "그 자리에 있어야 보인다"가
    ///   목적이라 직전 조정을 존중하면 목적을 못 이룬다(2026-08-26 사용자 지시).
    /// ★측면 배치는 <b>최초 1회</b>다. 매번 다시 보면 시술자가 정중선 근처에서 움직일 때
    ///   화면이 좌우로 깜빡인다. 각도기의 면 배치와 같은 이유·같은 규약이다.
    /// </summary>
    private void PlaceProgressUI(string stepName, int subStepNo)
    {
        if (string.IsNullOrEmpty(stepName) || subStepNo != 1) return;

        // ★조용히 넘어가지 않는다. 슬롯이 비어 있는 것과 조건이 안 맞는 것이
        //   똑같이 '아무 일도 안 일어남'으로 보여 원인을 못 찾는다(2026-08-26).
        bool isSideGrip = stepName == sideGripStepName;
        bool isFrontGrip = false;
        for (int i = 0; frontGripStepNames != null && i < frontGripStepNames.Length; i++)
            if (stepName == frontGripStepNames[i]) { isFrontGrip = true; break; }

        if (!isSideGrip && !isFrontGrip) return;   // 옮길 단계가 아니다 — 정상

        if (progressRoot == null)
        {
            ChunaLogger.LogWarning($"<color=orange>[ROM Bridge] '{stepName}'에서 진행 UI를 옮기려 했지만 " +
                                   "progressRoot 슬롯이 비어 있다 — 인스펙터에서 '진행Root'를 넣어야 한다.</color>");
            return;
        }

        // ── ② 정면 복귀 — 측굴·회전으로 넘어가는 파지 ──
        for (int i = 0; frontGripStepNames != null && i < frontGripStepNames.Length; i++)
        {
            if (stepName != frontGripStepNames[i]) continue;
            if (frontPoint == null)
            {
                Log("진행 UI 정면 포인트가 비어 있어 옮기지 않는다");
                return;
            }
            progressRoot.SetPositionAndRotation(frontPoint.position, frontPoint.rotation);
            sideProgressPlaced = false;   // 다음 시상면이 오면 다시 한 번 옮길 수 있게
            Log($"진행 UI → 정면 포인트 ('{stepName}')");
            return;
        }

        // ── ① 측면 배치 — 시상면 파지. 최초 1회만. ──
        if (stepName != sideGripStepName || sideProgressPlaced) return;

        Transform torso = driver.Torso;
        if (torso == null) return;

        Transform target = ChooseSidePoint(torso);
        if (target == null)
        {
            Log("진행 UI 좌·우 포인트가 비어 있어 옮기지 않는다");
            return;
        }

        progressRoot.SetPositionAndRotation(target.position, target.rotation);
        sideProgressPlaced = true;
        Log($"진행 UI → 측면 포인트 '{target.name}' (최초 1회)");
    }

    /// <summary>시술자가 선 쪽을 보고 좌·우 포인트 중 하나를 고른다.</summary>
    private Transform ChooseSidePoint(Transform torso)
    {
        if (sidePointLeft == null || sidePointRight == null)
            return sidePointLeft != null ? sidePointLeft : sidePointRight;

        Camera cam = Camera.main;
        if (cam == null) return sidePointLeft;

        // 환자 몸통의 좌우축에 시술자 위치를 투영한다. 양수면 환자 우측에 서 있다.
        float side = Vector3.Dot(cam.transform.position - torso.position, torso.right);
        if (Mathf.Abs(side) < progressSideDeadZone) return sidePointLeft;   // 정중선 근처 — 아무 쪽이나 뽑히지 않게 고정

        bool viewerOnPatientRight = side > 0f;
        bool useLeft = placeOppositeToViewer ? viewerOnPatientRight : !viewerOnPatientRight;
        return useLeft ? sidePointLeft : sidePointRight;
    }

    private void OnSubStepEntered(string stepName, int subStepNo)
    {
        PlaceProgressUI(stepName, subStepNo);

        CervicalRomDriver.Direction dir = DirectionOf(stepName);
        if (dir == CervicalRomDriver.Direction.None) return;

        bool isOverpressure = stepName.EndsWith("압박", System.StringComparison.Ordinal);

        if (!isOverpressure)
        {
            planeGauge?.SetPressGuide(false);   // 능동·지시 단계에는 화살표가 없다
            // 능동 — 지시(x.1) 다음 동작(x.2)에서 움직이기 시작한다.
            if (subStepNo >= 2)
            {
                driver.BeginActive(dir);
                Log($"능동 시작 {stepName} → {driver.ActiveTargetAngle:F0}° "
                    + $"(최대 {driver.MaxAngle:F0}° · 기능장애 {driver.CurrentDysfunction:F1}°)");
            }
            return;
        }

        // 압박 — 유지(x.2)에서 밀고, 복귀(x.3)에서 중립으로 돌아온다.
        if (subStepNo == 2)
        {
            overpressureProgress = 0f;
            overpressureHeldTime = 0f;
            driver.HoldElapsed = 0f;
            driver.HoldTarget = overpressureHoldSeconds;
            arcStarted = false;    // 손 기준점을 이 단계에서 다시 잡는다
            pairStarted = false;
            // ★밀기 시작하는 지금부터 방향 화살표를 켠다. 능동 구간에는 안 뜬다 —
            //   능동은 환자가 스스로 가는 구간이라 시술자에게 줄 지시가 없다.
            planeGauge?.SetPressGuide(true);
            sweptSmoothed = 0f;
            sweptVelocity = 0f;
            Log($"압박 시작 {stepName} — {driver.CurrentAngle:F0}° 에서 압박 한계 {driver.PassiveLimitAngle:F0}° 까지 " +
                $"(밀 양 {driver.CurrentPassiveGain:F1}° · 최대 {driver.MaxAngle:F0}° · "
                + $"예상 부족각 {driver.MaxAngle - driver.PassiveLimitAngle:F1}° · 소스 {overpressureSource})");
        }
        else if (subStepNo >= 3)
        {
            // 복귀로 넘어가면 유지 타이머는 화면에서 치운다.
            overpressureHeldTime = 0f;
            driver.HoldElapsed = 0f;
            driver.HoldTarget = 0f;
            planeGauge?.SetPressGuide(false);   // 복귀는 밀라는 구간이 아니다
            driver.ReturnToNeutral();
            Log($"중립 복귀 {stepName} (부족각 {driver.DeficitAngle:F1}°)");
        }
    }

    /// <summary>
    /// ★압박은 <b>손이 실제로 민 만큼</b> 들어간다. 시간으로 채우지 않는다.
    ///   목이 도는 중심을 기준으로 손끝 중점이 회전축 둘레로 몇 도 돌았는지 재고,
    ///   그 각을 남은 여유 구간에 대비시켜 진행률을 만든다. 손이 멈추면 머리도 멈춘다.
    ///   손끝을 못 찾은 경우에만 예전 시간 방식으로 물러난다.
    /// </summary>
    private void AdvanceOverpressure(string stepName, int subStepNo)
    {
        if (subStepNo != 2 || !stepName.EndsWith("압박", System.StringComparison.Ordinal)) return;

        Transform pivot = driver.Pivot;
        Vector3 axis = driver.CurrentWorldAxis;

        if (!BothHandsTouching())
        {
            // 파지가 풀리면 그 자리에서 멈춘다. 왜 멈췄는지는 로그에 남긴다.
            DiagnoseOverpressure("파지 안 잡힘 — 진행도 회전도 멈춰 있다", float.NaN, float.NaN, axis, pivot);
            return;
        }

        // ── 두 방식을 매 프레임 같이 잰다. 하나로 굴리고 둘 다 로그에 남긴다. ──
        float arcAngle = float.NaN;    // ①중점호
        float pairAngle = float.NaN;   // ②손쌍회전
        bool haveGeometry = gripJudge != null && pivot != null && axis != Vector3.zero;

        if (haveGeometry && gripJudge.TryGetGripMidpoint(out Vector3 hand))
        {
            Vector3 arm = Vector3.ProjectOnPlane(hand - pivot.position, axis);
            if (arm.sqrMagnitude > 1e-6f)
            {
                arcRadius = arm.magnitude;
                if (!arcStarted) { arcStarted = true; arcStartArm = arm; arcStartRadius = arcRadius; }
                else arcAngle = Vector3.SignedAngle(arcStartArm, arm, axis);
            }
        }

        if (haveGeometry && gripJudge.TryGetContactPairVector(out Vector3 pair))
        {
            Vector3 flat = Vector3.ProjectOnPlane(pair, axis);
            if (flat.sqrMagnitude > 1e-6f)
            {
                pairSpan = flat.magnitude;
                if (!pairStarted) { pairStarted = true; pairStartVector = flat; pairStartSpan = pairSpan; }
                else pairAngle = Vector3.SignedAngle(pairStartVector, flat, axis);
            }
        }

        float swept = overpressureSource == OverpressureSource.HandPairRotation ? pairAngle : arcAngle;

        if (float.IsNaN(swept))
        {
            // 손끝을 하나도 못 찾은 경우에만 예전 시간 방식으로 물러난다.
            if (!arcStarted && !pairStarted) AdvanceOverpressureByTime();
            DiagnoseOverpressure("기준 잡는 중", arcAngle, pairAngle, axis, pivot);
            return;
        }

        // ★강체 구속 — 머리를 제대로 잡고 돌리면 두 손 간격과 반지름이 보존된다.
        //   제자리에서 손목만 틀면 손이 머리 위를 미끄러져 둘 다 깨진다.
        //   깨진 동안에는 진행을 인정하지 않는다. 되돌리지는 않고 그 자리에서 멈춘다 —
        //   잠깐 미끄러졌다고 밀어 둔 각을 날리면 오히려 더 답답하다.
        if (gripSlipTolerance > 0f)
        {
            float radiusDrift = arcStarted ? Mathf.Abs(arcRadius - arcStartRadius) : 0f;
            float spanDrift = pairStarted ? Mathf.Abs(pairSpan - pairStartSpan) : 0f;

            if (radiusDrift > gripSlipTolerance || spanDrift > gripSlipTolerance)
            {
                DiagnoseOverpressure(
                    $"파지가 미끄러졌다 — 반지름 {radiusDrift * 100f:F1}cm · 두 손 간격 {spanDrift * 100f:F1}cm 변함 " +
                    $"(허용 {gripSlipTolerance * 100f:F0}cm). 머리를 잡고 돌리는 게 아니라 손이 머리 위를 미끄러지고 있다.",
                    arcAngle, pairAngle, axis, pivot);
                return;
            }
        }

        // ★지터를 걸러낸다. 여유 구간이 7~13°라 손 떨림 몇 도가 그대로 게이지에 실린다.
        sweptSmoothed = overpressureSmoothTime > 0f
            ? Mathf.SmoothDamp(sweptSmoothed, swept, ref sweptVelocity, overpressureSmoothTime)
            : swept;

        float gap = driver.CurrentPassiveGain;   // 손이 밀어야 하는 양 = 머리가 더 가는 양

        // 되돌아가는 방향(음수)은 0으로 본다. 민 만큼만 인정한다.
        // ★진행률은 기준점 대비 절대값이라, 손을 되돌리면 머리도 능동 끝점으로 되돌아온다.
        //   시술자가 힘을 빼면 머리가 따라 돌아오는 게 맞으므로 의도된 동작이다.
        overpressureProgress = Mathf.Clamp01(Mathf.Max(0f, sweptSmoothed) / gap);
        driver.SetOverpressure(overpressureProgress);

        AccumulateOverpressureHold();

        DiagnoseOverpressure(null, arcAngle, pairAngle, axis, pivot);
    }

    /// <summary>손끝을 하나도 못 찾았을 때의 폴백. 시간으로 민다.</summary>
    private void AdvanceOverpressureByTime()
    {
        overpressureProgress = overpressureRampSeconds > 0f
            ? Mathf.Clamp01(overpressureProgress + Time.deltaTime / overpressureRampSeconds)
            : 1f;
        driver.SetOverpressure(overpressureProgress);
    }

    /// <summary>
    /// ★압박이 왜 안 도는지 가르는 로그. 한 줄에 관문 네 개를 다 담는다 —
    ///   파지 / 두 방식의 회전각 / 여유 구간 대비 진행률 / 축·중심이 잡혔는지.
    /// 원인을 잡고 나면 <see cref="logOverpressure"/>를 끈다.
    /// </summary>
    private void DiagnoseOverpressure(string note, float arcAngle, float pairAngle, Vector3 axis, Transform pivot)
    {
        if (!logOverpressure) return;
        if (Time.time - lastPressLogTime < overpressureLogInterval) return;
        lastPressLogTime = Time.time;

        string grip = "판정기 없음";
        if (gripJudge != null && gripJudge.TryGetGripState(out bool aL, out bool aR, out bool bL, out bool bR))
        {
            grip = $"{gripJudge.PairAName}(왼{Mark(aL)}/오{Mark(aR)}) {gripJudge.PairBName}(왼{Mark(bL)}/오{Mark(bR)})";
        }

        float gap = driver.CurrentPassiveGain;   // 손이 밀어야 하는 양 = 머리가 더 가는 양
        bool usingPair = overpressureSource == OverpressureSource.HandPairRotation;

        ChunaLogger.Log(
            $"<color=cyan>[ROM 압박] {(note ?? "진행 중")} — 파지 {(BothHandsTouching() ? "O" : "X")}  {grip}\n" +
            $"    ①중점호 {Deg(arcAngle)} (반지름 {arcRadius:F2}m, 시작 대비 {Drift(arcRadius, arcStartRadius, arcStarted)})" +
            $"{(usingPair ? "" : "  ← 사용")}\n" +
            $"    ②손쌍회전 {Deg(pairAngle)} (두 손 간격 {pairSpan:F2}m, 시작 대비 {Drift(pairSpan, pairStartSpan, pairStarted)})" +
            $"{(usingPair ? "  ← 사용" : "")}\n" +
            $"    여유 {gap:F1}° · 진행 {overpressureProgress * 100f:F0}% · " +
            $"머리 {driver.CurrentAngle:F1}° → 압박한계 {driver.PassiveLimitAngle:F0}° (최대 {driver.MaxAngle:F0}°) · " +
            $"축 {(axis == Vector3.zero ? "★없음(방향 None)" : axis.ToString("F2"))} · " +
            $"중심 {(pivot != null ? pivot.name : "★없음")}</color>");
    }

    /// <summary>
    /// 압박 한계에서 버틴 시간을 센다. 손이 물러나 진행률이 문턱 아래로 내려가면 0으로 되돌린다.
    /// ★값을 드라이버에 실어 둔다 — 각도기가 브리지를 몰라도 타이머를 그릴 수 있다.
    /// </summary>
    private void AccumulateOverpressureHold()
    {
        if (overpressureProgress >= overpressureHoldThreshold)
        {
            overpressureHeldTime += Time.deltaTime;
        }
        else if (overpressureHeldTime > 0f)
        {
            if (showDebugLogs)
            {
                ChunaLogger.Log($"<color=yellow>[ROM Bridge] 압박 유지 끊김 — {overpressureHeldTime:F1}초에서 " +
                                $"진행률이 {overpressureProgress:P0}로 내려갔다. 타이머를 되돌린다.</color>");
            }
            overpressureHeldTime = 0f;
        }

        driver.HoldElapsed = overpressureHeldTime;
        driver.HoldTarget = overpressureHoldSeconds;
    }

    private static string Mark(bool on) => on ? "O" : "·";

    private static string Deg(float degrees) => float.IsNaN(degrees) ? "  ——  " : $"{degrees,6:F1}°";

    /// <summary>강체 구속 검사용 — 시작 대비 얼마나 변했는지(cm).</summary>
    private static string Drift(float now, float start, bool started)
        => started ? $"{(now - start) * 100f:+0.0;-0.0;0.0}cm" : "——";

    /// <summary>
    /// 파지가 성립했는가. 엄지·검지 판정기가 있으면 그쪽을 본다 —
    /// 손바닥 접촉이 아니라 두 접촉점을 서로 다른 손이 하나씩 집었는지가 기준이다.
    /// </summary>
    private bool BothHandsTouching()
    {
        if (gripJudge != null) return gripJudge.IsGripped;
        if (evaluator == null) return true;   // 판정기가 없으면 게이트를 걸지 않는다
        return evaluator.IsLeftHandTouchingPatient && evaluator.IsRightHandTouchingPatient;
    }

    /// <summary>단계에 맞는 접촉점 쌍으로 전환한다. 시상면만 이마·뒤통수다.</summary>
    private void ApplyGripPair(string stepName)
    {
        if (gripJudge == null) return;

        // ★'시상면 파지'는 이름으로 판별한다. 예전엔 그냥 '파지'였는데, 실측 phase에도
        //   같은 이름의 다른 단계가 있어 2026-08-26에 면 이름을 붙여 통일했다.
        CervicalGripJudge.GripPair pair;
        if (stepName == "시상면 파지" || stepName == "파지"   // '파지'는 옛 이름 — 남은 CSV 호환용
                               || stepName.StartsWith("굴곡", System.StringComparison.Ordinal)
                               || stepName.StartsWith("신전", System.StringComparison.Ordinal)
                               || stepName == "시상면평가")
            pair = CervicalGripJudge.GripPair.Sagittal;
        else if (DirectionOf(stepName) != CervicalRomDriver.Direction.None
                 || stepName == "관상면 파지" || stepName == "횡단면 파지"
                 || stepName == "관상면파지" || stepName == "횡단면파지"   // 옛 이름 호환
                 || stepName == "관상면평가" || stepName == "횡단면평가")
            pair = CervicalGripJudge.GripPair.Lateral;
        else
            pair = CervicalGripJudge.GripPair.None;

        if (gripJudge.CurrentPair != pair) gripJudge.SetPair(pair);
    }

    private bool IsTargetScenario()
    {
        if (string.IsNullOrEmpty(scenarioName)) return true;
        ScenarioData data = scenarioManager.CurrentScenario;
        return data != null && data.scenarioName == scenarioName;
    }

    /// <summary>CSV stepName → 방향. 이름이 바뀌면 여기도 같이 바꿔야 한다.</summary>
    private static CervicalRomDriver.Direction DirectionOf(string stepName)
    {
        if (string.IsNullOrEmpty(stepName)) return CervicalRomDriver.Direction.None;

        if (stepName.StartsWith("굴곡", System.StringComparison.Ordinal))
            return CervicalRomDriver.Direction.Flexion;
        if (stepName.StartsWith("신전", System.StringComparison.Ordinal))
            return CervicalRomDriver.Direction.Extension;
        if (stepName.StartsWith("우측굴", System.StringComparison.Ordinal))
            return CervicalRomDriver.Direction.LateralRight;
        if (stepName.StartsWith("좌측굴", System.StringComparison.Ordinal))
            return CervicalRomDriver.Direction.LateralLeft;
        if (stepName.StartsWith("우회전", System.StringComparison.Ordinal))
            return CervicalRomDriver.Direction.RotationRight;
        if (stepName.StartsWith("좌회전", System.StringComparison.Ordinal))
            return CervicalRomDriver.Direction.RotationLeft;

        return CervicalRomDriver.Direction.None;
    }

    private void Log(string message)
    {
        if (showDebugLogs) ChunaLogger.Log($"<color=cyan>[ROM Bridge] {message}</color>");
    }
}
