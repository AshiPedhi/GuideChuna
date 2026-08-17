using UnityEngine;

/// <summary>
/// 두개골 교정 술기용 IScenarioCondition 3종.
/// ScenarioConditionManager의 ConditionCheckRoutine이 IsConditionMet()를 폴링하며,
/// 완료 시 완료피드백 + NextSubStep 레일을 자동으로 탄다(기존 시스템 재사용).
/// ScenarioManager가 conditionType("cranialGrip"/"cranialPressure"/"cranialDepthBreath")을 보고 생성·등록한다.
///
/// 부수효과(영점 저장/호흡 윈도우 시작)는 **생성자가 아니라 첫 IsConditionMet 폴 시점**에 수행한다.
/// (나레이션이 붙은 substep은 나레이션 재생 후에야 폴링이 시작되므로, 생성자에서 하면 호흡이 조기 시작됨)
/// </summary>

/// <summary>
/// ⓪ 진단 substep: 정해진 자세로 양손 파지점을 잡고 일정 시간 **유지**하면 완료.
///
/// 단계 정의는 컨트롤러의 diagnosisStages에 있고, CSV의 conditionParams(=단계 ID)로 고른다.
///   · OM 진단1    : 양손 측두부 감싸기(양손 손바닥)   — 3초 유지
///   · OM 진단2    : 양손 후두부 모아 베개(양손 손바닥) — 8초 유지
///   · PM·PJ 진단1 : 자세 2개(ⓐ왼손 후두부+오른손 3점 / ⓑ왼손 3점+오른손 후두부) 각 3초, 순서 무관
///
/// 압력·깊이·포즈는 보지 않고 트리거 접촉만 본다(진단은 촉진일 뿐, 교정압 없음).
/// 유지 도중 파지가 풀리면 누적이 0으로 초기화된다(짧은 추적 튐은 gripGraceSeconds만큼 유예).
///
/// diagnosisStages가 비었거나 단계 ID를 못 찾으면 **레거시 양손 터치 판정으로 폴백**한다
/// (구버전 배선 시나리오가 그대로 동작하도록).
/// </summary>
/// <summary>
/// 머리(HMD)가 <b>아래로 순간 하강</b>하는 것을 잡아낸다 — 바디드롭·순간 교정 판정용.
///
/// ★손으로 '누르는 깊이'는 VR에서 신뢰할 수 없다(반력이 없고 트래킹 오차가 변위와 비슷하다).
/// 반면 체중을 싣는 동작은 시술자가 실제로 몸을 낮추므로 헤드셋이 눈에 띄게 내려간다.
/// 아무 움직임이나 잡히지 않도록 세 가지를 동시에 본다:
///   ① <b>파지가 성립</b>해 있어야 한다 — 손을 뗀 채 몸만 숙이는 건 무시
///   ② <b>아래 방향</b>이어야 한다 — 고개를 들거나 옆으로 움직이는 건 안 잡힌다
///   ③ 빠른 하강이 <b>최소 변위</b>만큼 누적돼야 한다 — 미세한 떨림 배제(올라가면 리셋)
/// </summary>
public class HeadThrustDetector
{
    private readonly CranialAdjustmentController controller;
    private readonly float speed;      // m/s (0이면 속도를 보지 않는다)
    private readonly float minDrop;    // m
    private readonly bool useHands;    // true면 머리 대신 <b>양손 손바닥 평균 높이</b>를 본다
    private float lastY = float.NaN, lastT, accumulated;
    private bool pressed;              // 눌러 들어간 상태인가(되돌아 나오면 발동)
    private float rise;                // 되돌아 올라온 양

    /// <param name="speed">아래로 이 속도(m/s) 이상일 때만 누적. 0이면 속도 무관(손 방식 기본).</param>
    /// <param name="useHands">true면 손 깊이로 판정 — <b>들어갔다 나오는</b> 것을 본다.</param>
    public HeadThrustDetector(CranialAdjustmentController controller, float speed, float minDrop,
                              bool useHands = false)
    {
        this.controller = controller;
        this.speed = speed;
        this.useHands = useHands;
        this.minDrop = minDrop > 0f ? minDrop : (useHands ? 0.02f : 0.03f);
    }

    /// <summary>마지막 판정에서 잰 하강 속도(m/s)·누적 하강(m) — 로그·튜닝용.</summary>
    public float LastSpeed { get; private set; }
    public float Accumulated => accumulated;

    /// <summary>이번 프레임에 순간 하강이 성립했는가. 성립하면 누적을 비워 연속 발동을 막는다.</summary>
    public bool Detect(bool gripped)
    {
        bool ok = useHands ? controller != null && controller.TryGetHandDepth(out lastSample)
                           : controller != null && controller.TryGetHeadHeight(out lastSample);
        if (!ok) return false;
        float y = lastSample;

        float now = Time.time;
        float dt = float.IsNaN(lastY) ? 0f : now - lastT;
        float drop = float.IsNaN(lastY) ? 0f : lastY - y;   // 양수 = 내려감
        lastY = y;
        lastT = now;

        if (!gripped) { accumulated = 0f; pressed = false; rise = 0f; return false; }
        if (dt <= 0f) return false;

        LastSpeed = drop / dt;

        // ── 손 방식: <b>눌러 들어갔다 되돌아 나오면</b> 발동 ──────────────────
        //   속도를 요구하지 않으므로 천천히 눌러도 잡힌다. 되돌아 나오는 것까지 봐야
        //   '자세를 낮춘 채 유지'가 아니라 '한 번 눌렀다 뗀' 동작으로 구분된다.
        if (useHands)
        {
            if (!pressed)
            {
                if (drop > 0f) accumulated += drop;
                else if (drop < 0f) accumulated = Mathf.Max(0f, accumulated + drop);
                if (accumulated >= minDrop) { pressed = true; rise = 0f; }
                return false;
            }

            if (drop < 0f) rise += -drop;              // 되돌아 올라온 양
            if (rise < minDrop * 0.5f) return false;

            accumulated = 0f; pressed = false; rise = 0f;
            return true;
        }

        // ── 머리 방식: 빠른 하강이 최소 변위만큼 쌓이면 발동 ─────────────────
        if (LastSpeed >= speed) accumulated += drop;
        else if (LastSpeed <= 0f) accumulated = 0f;   // 올라가면 리셋 — 왕복으로 못 채우게

        if (accumulated < minDrop) return false;
        accumulated = 0f;
        return true;
    }

    private float lastSample;
}

public class DiagnosisHoldCondition : IScenarioCondition
{
    private readonly CranialAdjustmentController controller;
    private readonly string stageId;
    private bool usingStage;
    private bool started = false;

    /// <summary>0보다 크면 <b>양손 포개짐</b>으로 판정한다 — 파지점 하나에 두 손을 모으는 진단용.
    /// CSV: <c>stack=0.10;finger=thumb</c> (두 엄지가 10cm 이내로 모이고 그 자리가 파지점 근처)</summary>
    private readonly float stackGap;
    private readonly CranialFinger stackFinger;
    private float stackHeldSince = -1f;

    /// <param name="holdSeconds">CSV의 hold= 값(초). 0이면 CranialAdjustmentController.DefaultDiagnosisHoldSeconds(3초).</param>
    public DiagnosisHoldCondition(CranialAdjustmentController controller, string stageId, float holdSeconds = 0f,
                                  float stackGap = 0f, CranialFinger stackFinger = CranialFinger.Palm)
    {
        this.controller = controller;
        this.stageId = string.IsNullOrWhiteSpace(stageId) ? "" : stageId.Trim();
        this.stackGap = stackGap;
        this.stackFinger = stackFinger;

        // ★반드시 PrepareDiagnosisStage보다 먼저 — 준비 시점에 진행 표시가 이 값으로 계산된다.
        controller?.SetDiagnosisHoldOverride(holdSeconds);

        // 표시는 즉시 — 나레이션이 흐르는 동안 어디를 잡아야 하는지 보여준다.
        // 유지 타이머는 나레이션이 끝난 뒤(첫 폴)에 시작한다(TryStart).
        usingStage = controller != null && controller.PrepareDiagnosisStage(this.stageId);

        if (!usingStage && controller != null)
        {
            if (controller.HasDiagnosisStages)
                ChunaLogger.LogWarning($"[DiagnosisHoldCondition] 진단 단계 '{this.stageId}'를 찾지 못했습니다 " +
                    "(CSV conditionParams와 컨트롤러 diagnosisStages의 stageId가 다릅니다) — 레거시 접촉 판정으로 폴백합니다.");
            controller.BeginDiagnosisPhase();   // 레거시 경로
        }
    }

    private void TryStart()
    {
        if (controller == null || started) return;
        if (usingStage) controller.BeginDiagnosisStage(stageId);   // 여기서부터 유지 타이머 카운트
        started = true;

        // ★"타이머가 멈추고 사라진 뒤 진행이 안 된다"(08-11) 추적용 —
        //   여기서 usingStage=false면 유지 타이머 자체가 없는 레거시 접촉 판정으로 도는 것이고,
        //   그 경우 게이트는 leftGrips + diagnosisRightGrips라 배선이 다르면 영영 안 넘어간다.
        ChunaLogger.Log($"<color=orange>[DiagnosisHoldCondition] 판정 시작 — 단계='{stageId}' " +
                        $"경로={(usingStage ? "스테이지 유지 타이머" : "★레거시 양손 접촉(유지 타이머 없음)")} " +
                        $"유지={controller.StageHoldSeconds:F1}초</color>");
    }

    public bool IsConditionMet()
    {
        if (controller == null) return false;
        if (!started) { TryStart(); return false; }   // 첫 폴(나레이션 후) 시점에 타이머 시작

        // ★파지점 하나에 두 손(엄지 등)을 모으는 진단 — 손별 파지점을 둘 만들 필요가 없다.
        //   유지 시간은 여기서 직접 잰다(스테이지 자세 타이머를 쓰지 않으므로).
        if (stackGap > 0f)
        {
            float need = controller.StageHoldSeconds;
            bool on = controller.HandsStackedAt(controller.DiagnosisStackTarget,
                                                stackGap, stackGap * 1.5f, stackFinger);

            if (!on) { stackHeldSince = -1f; controller.ReportHoldProgress(need, need); return false; }

            if (stackHeldSince < 0f) stackHeldSince = Time.time;
            float elapsed = Time.time - stackHeldSince;
            controller.ReportHoldProgress(need - elapsed, need);
            return elapsed >= need;
        }

        return usingStage ? controller.DiagnosisStageComplete : controller.BothHandsTouched;
    }

    public string GetConditionDescription() =>
        stackGap > 0f
            ? $"양손 {stackFinger} 포개짐 유지 대기 ({stackGap * 100f:F0}cm 이내)"
            : usingStage ? $"진단 자세 유지 대기 ({stageId})" : "진단 촉진(양손 접촉) 대기";
}

/// <summary>
/// ① 파지 substep: 양손 파이브핑거홀드(트리거+포즈) 성립 시 완료.
/// 영점은 여기서 저장하지 않는다 — ②a(PressureCondition) 진입 시 파지 유지 재확인 후 저장.
/// </summary>
public class GripPointCondition : IScenarioCondition
{
    private readonly CranialAdjustmentController controller;
    private readonly CranialAdjustmentController.JudgeHand hand;

    /// <summary>0보다 크면 <b>양손 포개짐</b>으로 판정한다 — 두 손바닥 사이 허용 간격(m).
    /// CSV: <c>stack=0.08</c> (두 손이 8cm 이내로 붙고, 그 자리가 파지점 근처여야 성립)</summary>
    private readonly float stackGap;

    public GripPointCondition(CranialAdjustmentController controller,
                              CranialAdjustmentController.JudgeHand hand = CranialAdjustmentController.JudgeHand.양손,
                              float stackGap = 0f)
    {
        this.controller = controller;
        this.hand = hand;
        this.stackGap = stackGap;
        this.controller?.BeginGripPhase();
    }

    public bool IsConditionMet()
    {
        if (controller == null) return false;
        if (stackGap > 0f)
            return controller.HandsStackedAt(controller.StackTarget, stackGap, stackGap * 1.5f);
        return controller.GrippedBy(hand);
    }

    // ★문구에 '두개골'·'파이브핑거홀드'를 쓰지 않는다 — 이 조건은 늑골·흉추도 공용으로 쓰고,
    //   판정은 '리그에 등록된 양손 파지점이 전부 접촉했는가'일 뿐 손가락 수와 무관하다.
    //   옛 문구가 제1늑골 로그에 "두개골 파지(양손 파이브핑거홀드)"로 찍혀 혼란을 줬다(2026-08-12).
    public string GetConditionDescription() =>
        stackGap > 0f
            ? $"양손 포개짐 대기 (손 간격 {stackGap * 100f:F0}cm 이내 · 파지점 근처)"
            : hand == CranialAdjustmentController.JudgeHand.양손
                ? "파지 성립 대기 (양손 파지점 전부 접촉)"
                : $"파지 성립 대기 ({hand} 파지점 접촉)";
}

/// <summary>
/// ② 압력 조절 substep: 견착 前 손 추적이 되는 동안 누르는 방향·세기를 안내하는 단계.
///
/// ★ 판정 = **파지 유지**(양손 파지점 전부 접촉)를 holdDuration 동안 지속. 깊이(누른 정도)는 보지 않는다.
///   VR엔 반력이 없어 "얼마나 눌렀나"가 실제 술기의 저항감을 대변하지 못하므로 판정에서 뺐다.
///   화면 안내(나레이션·지시문)로 힘 조절을 가르치고, 통과는 자세 유지로만 본다.
///   컨트롤러의 useDepthJudging을 켜면 예전처럼 적정 텐션 존 판정으로 돌아간다.
///
/// 실제 교정 압력은 다음 ③ 견착 국면에서 적용(손 추적 불가라 미판정).
/// </summary>
public class PressureCondition : IScenarioCondition
{
    private readonly CranialAdjustmentController controller;
    private readonly float holdDuration;
    private readonly float graceTime;   // 적정존 순간 이탈(손떨림)을 이 시간까지는 허용 → 타이머 유지
    private bool zeroSaved = false;
    private float heldSince = -1f;
    private float leftZoneAt = -1f;     // 적정존을 벗어난 시각(-1 = 존 안)

    /// <summary>
    /// 0보다 크면 <b>머리(HMD)가 이 높이만큼 내려가야</b> 유지로 인정한다. 단위 m.
    ///
    /// ★손으로 '누르는 깊이'는 VR에서 신뢰할 수 없다(반력이 없고 트래킹 오차가 변위와 비슷하다).
    /// 반면 체중을 싣는 동작은 <b>시술자가 실제로 몸을 낮추므로 헤드셋이 눈에 띄게 내려간다</b> —
    /// 흉추 신전의 바디드롭·마지막 압박처럼 '눌렀는지'를 봐야 하는 단계에서 이걸 쓴다.
    /// CSV: <c>conditionParams=1;headDrop=0.06;xray</c> (1초 유지 · 6cm 하강)
    /// </summary>
    private readonly float headDrop;
    private float headBaseline = float.NaN;

    /// <summary>
    /// 0보다 크면 <b>순간 하강(쓰러스트)</b>으로 판정한다 — 이 속도(m/s) 이상으로 머리가 내려가면 즉시 통과.
    ///
    /// ★바디드롭·순간 교정은 '버티는' 동작이 아니라 '한순간'이다. 유지 시간을 요구하면
    /// 술기와 어긋난다(사용자 지적). 대신 아무 움직임이나 잡히지 않도록 세 가지를 동시에 본다:
    ///   ① <b>파지가 성립해 있어야 한다</b> — 손이 파지점을 벗어나 있으면 무시
    ///   ② <b>아래 방향</b>이어야 한다 — 고개를 들거나 옆으로 움직이는 건 안 잡힌다
    ///   ③ 빠른 하강이 <b>최소 변위</b>만큼 누적돼야 한다 — 미세한 떨림 배제
    /// </summary>
    private readonly float headThrust;
    private readonly HeadThrustDetector thrust;
    private float lastY = float.NaN, lastT;
    private float thrustDrop;

    /// <summary>
    /// true면 <b>이마 견착까지 돼야</b> 유지로 인정한다(CSV <c>conditionParams=…;brace</c>).
    ///
    /// ★2026-08-17 사용자 보고: "이마 견착도 안 했는데 혼자 넘어가더라."
    /// 원인 = <c>brace</c> 토큰이 <see cref="ShoulderBraceGuide"/> <b>마커 표시만</b> 켰고 판정에는
    /// 아무 영향이 없었다. 이 조건은 <see cref="Holding"/>이 파지만 봤기 때문에,
    /// 앞 단계에서 이미 잡고 있던 파지가 그대로 유지되어 견착 단계가 즉시 통과됐다.
    /// </summary>
    private readonly bool requireBrace;

    /// <summary>
    /// ★<b>견착 중에는 파지 추적이 끊긴다.</b> 머리를 숙여 팔을 이마에 붙이면 손이 헤드셋 시야 밖으로
    /// 나가서 파지가 false로 떨어진다(2026-08-17 사용자 보고: "머리 숙인 채로는 진행이 안 되고
    /// 머리를 올리고 파지하니까 그제서야 된다").
    ///
    /// 그래서 <b>이 단계에서 파지가 한 번이라도 성립했으면, 견착이 유지되는 동안은 유지된 것으로 본다.</b>
    /// 견착이 풀리면 래치도 풀린다 — 손을 놓고 버티는 것으로 통과되지 않는다.
    ///
    /// ★이 래치가 없으면 견착 판정과 파지 판정이 서로를 막아 <b>영영 못 넘어간다</b>
    /// (숙이면 파지가 끊기고, 파지하려고 들면 견착이 풀린다).
    /// </summary>
    private bool gripLatched;

    public PressureCondition(CranialAdjustmentController controller, float holdDuration = 1.0f,
                             float graceTime = 0.5f, float headDrop = 0f, float headThrust = 0f,
                             bool thrustByHands = false, bool requireBrace = false)
    {
        this.controller = controller;
        this.holdDuration = holdDuration;
        this.graceTime = graceTime;
        this.headDrop = headDrop;
        this.headThrust = headThrust;
        this.requireBrace = requireBrace;
        // ★단계에 들어오는 순간 이미 파지 중이면 래치를 미리 걸어 둔다.
        //   앞 단계(파지·견착)에서 잡고 들어오는 흐름이라, 여기서 안 걸면 첫 프레임에
        //   손이 시야 밖이었을 때 영영 래치가 안 걸린다.
        this.gripLatched = requireBrace && controller != null && controller.BothGripped;
        if (headThrust > 0f || thrustByHands)
            thrust = new HeadThrustDetector(controller, headThrust, headDrop, thrustByHands);

        // ★판정 대상이 '파지 유지'이므로 교정 파지점이 켜져 있어야 한다.
        //   앞의 안내 substep에서 파지점이 꺼졌을 수 있다(판정 없는 단계는 구체를 감춘다) →
        //   여기서 다시 켜지 않으면 영영 성립하지 않는다(제1늑골 3.2에서 20초 폴백, 2026-08-12).
        this.controller?.ShowCorrectionGrips();
    }

    private void TrySaveZero()
    {
        if (controller == null || zeroSaved) return;
        if (!controller.BothGripped) return;   // 파지 유지 재확인 후 영점 저장
        controller.SaveZeroPoints();           // 깊이 판정 OFF면 컨트롤러가 알아서 무시
        zeroSaved = true;
    }

    /// <summary>이 단계의 유지 조건. 기본 = 파지 유지, 깊이 판정을 켠 경우만 적정 텐션 존.
    /// headDrop이 지정되면 <b>머리가 그만큼 내려가 있어야</b> 인정한다.</summary>
    private bool Holding
    {
        get
        {
            bool grip = controller.UseDepthJudging ? controller.BothInGoodZone : controller.BothGripped;

            if (requireBrace)
            {
                // ★견착 단계(brace)는 파지만으로는 안 된다 — 이마에 삼각근을 붙여야 인정.
                //   stabilizer가 씬에 없으면 IsPostureEngaged가 true를 주므로 예전처럼 동작한다.
                if (!controller.IsPostureEngaged)
                {
                    gripLatched = false;   // 견착이 풀리면 파지 래치도 푼다
                    Trace("견착 미성립 — 이마에 밀착해야 유지로 인정");
                    return false;
                }

                // 견착 중에는 손이 시야 밖이라 파지가 끊긴다 → 한 번 성립했으면 유지로 본다.
                if (grip) gripLatched = true;
                else if (gripLatched)
                {
                    grip = true;
                    Trace("견착 유지 중 — 파지 추적이 끊겼지만 래치로 유지 인정");
                }
            }

            if (headDrop <= 0f) return grip;

            if (!controller.TryGetHeadHeight(out float y))
            {
                Trace($"HMD를 못 찾음 — 파지만 판정 (파지={grip})");
                return grip;
            }

            // 영점은 <b>파지와 무관하게</b> 단계 진입 시 잡는다. 파지가 성립할 때까지 기다리면
            // 이미 몸을 낮춘 자세가 영점이 되어 '더 내려가야' 하는 상태가 된다.
            if (float.IsNaN(headBaseline)) headBaseline = y;

            // 영점보다 더 올라가면 갱신한다(자세를 고쳐 잡는 동안 기준이 어긋나지 않게).
            if (y > headBaseline) headBaseline = y;

            float drop = headBaseline - y;
            bool dropped = drop >= headDrop;
            if (!grip || !dropped)
                Trace($"파지={grip} 머리 하강={drop * 100f:F1}cm / 필요 {headDrop * 100f:F0}cm");
            return grip && dropped;
        }
    }

    /// <summary>
    /// 순간 하강 판정. 파지가 성립한 상태에서 <b>아래로</b> headThrust(m/s) 이상 빠르게 움직이고,
    /// 그 빠른 하강이 최소 변위만큼 누적되면 통과한다.
    /// </summary>
    private bool CheckThrust()
    {
        bool grip = controller.UseDepthJudging ? controller.BothInGoodZone : controller.BothGripped;
        if (!controller.TryGetHeadHeight(out float y))
        {
            Trace("HMD를 못 찾아 순간 하강을 판정할 수 없습니다");
            return false;
        }

        float now = Time.time;
        float dt = float.IsNaN(lastY) ? 0f : now - lastT;
        float drop = float.IsNaN(lastY) ? 0f : lastY - y;   // 양수 = 내려감
        lastY = y;
        lastT = now;

        // ① 파지를 놓으면 누적을 버린다 — 손을 뗀 채 몸만 숙이는 걸 막는다.
        if (!grip) { thrustDrop = 0f; Trace("파지가 풀려 순간 하강 누적 초기화"); return false; }
        if (dt <= 0f) return false;

        float v = drop / dt;                                // 아래 방향 속도(m/s)
        if (v >= headThrust) thrustDrop += drop;             // ② 빠른 하강만 쌓는다
        else if (v <= 0f) thrustDrop = 0f;                   // ③ 올라가면 리셋(왕복으로 못 채우게)

        // 최소 변위 — headDrop을 적어 두면 그 값, 없으면 3cm
        float need = headDrop > 0f ? headDrop : 0.03f;
        if (thrustDrop >= need)
        {
            ChunaLogger.Log($"<color=green>[Pressure] 순간 하강 감지 — {thrustDrop * 100f:F1}cm " +
                            $"(속도 {v:0.##}m/s ≥ {headThrust:0.##})</color>");
            return true;
        }

        Trace($"파지={grip} 하강속도={v:0.##}m/s (필요 {headThrust:0.##}) 누적={thrustDrop * 100f:F1}cm / {need * 100f:F0}cm");
        return false;
    }

    private float nextTraceTime;

    /// <summary>0.5초에 한 번만 남기는 진단 로그 — 임계값 튜닝용.</summary>
    private void Trace(string msg)
    {
        if (Time.time < nextTraceTime) return;
        nextTraceTime = Time.time + 0.5f;
        ChunaLogger.Log($"<color=orange>[Pressure] {msg}</color>");
    }

    /// <summary>지금 머리가 얼마나 내려가 있는지(m). 표시·튜닝용.</summary>
    public float HeadDropNow =>
        (!float.IsNaN(headBaseline) && controller != null && controller.TryGetHeadHeight(out float y))
            ? headBaseline - y : 0f;

    public bool IsConditionMet()
    {
        if (controller == null) return false;

        // 첫 폴(나레이션 후) 시점에 휴식 위치를 영점으로 저장. 파지가 잠깐 풀렸으면 다음 폴에서 재시도.
        // (깊이 판정 OFF면 SaveZeroPoints가 no-op이라 파지만 확인하고 지나간다.)
        if (!zeroSaved)
        {
            // ★영점을 못 잡았어도 <b>타이머는 띄운다</b>(2026-08-12).
            //   예전엔 여기서 그냥 빠져나가 ReportHoldProgress가 한 번도 안 불렸고,
            //   파지가 성립하기 전까지 "몇 초를 유지해야 하는지"가 화면에 아예 안 나왔다
            //   (등척성 5초 단계에서 타이머가 안 보인다는 지적).
            controller.ReportHoldProgress(holdDuration, holdDuration);
            TrySaveZero();
            if (!zeroSaved) Trace($"파지 대기 중 (양손 파지 성립 안 됨)");
            return false;
        }

        // ★순간 교정 판정 — 유지 시간을 요구하지 않는다.
        //   손 방식이면 '눌러 들어갔다 나오면', 머리 방식이면 '휙 내려가면' 통과.
        if (thrust != null)
        {
            bool grip = controller.UseDepthJudging ? controller.BothInGoodZone : controller.BothGripped;
            if (thrust.Detect(grip))
            {
                ChunaLogger.Log("<color=green>[Pressure] 순간 교정 감지</color>");
                return true;
            }
            Trace($"파지={grip} — 눌렀다 떼는 동작 대기 중");
            return false;
        }

        // 디버그: 완료는 시키지 않음 → 자유 관찰
        if (controller.DebugFreezePressureStep) return false;

        if (Holding)
        {
            leftZoneAt = -1f;
            if (heldSince < 0f) heldSince = Time.time;
            float elapsed = Time.time - heldSince;
            controller.ReportHoldProgress(holdDuration - elapsed, holdDuration);   // 진단과 같은 타이머 표시
            return elapsed >= holdDuration;
        }

        // 유지 이탈(파지 풀림 / 존 이탈): graceTime 이내의 짧은 흔들림이면 타이머(heldSince) 보존, 초과하면 리셋.
        // (완료 판정은 위 Holding 분기에서만 나므로, 유예 중에는 완료되지 않고 복귀를 기다린다.)
        if (heldSince >= 0f)
        {
            if (leftZoneAt < 0f) leftZoneAt = Time.time;
            if (Time.time - leftZoneAt <= graceTime)
            {
                // 유예 중에도 타이머는 계속 보여 준다(손 떨림에 표시가 깜빡이지 않도록).
                controller.ReportHoldProgress(holdDuration - (Time.time - heldSince), holdDuration);
                return false;
            }
        }
        controller.ReportHoldProgress(holdDuration, holdDuration);   // 리셋 — 타이머도 원위치
        heldSince = -1f;
        leftZoneAt = -1f;
        return false;
    }

    public string GetConditionDescription() =>
        headDrop > 0f
            ? $"파지 유지 + 머리 {headDrop * 100f:F0}cm 하강(체중 싣기) 대기"
            : controller != null && controller.UseDepthJudging
                ? "적정 텐션 유지 대기"
                : "파지 유지 대기";
}

/// <summary>
/// ③ 견착·호흡 substep: 어깨-이마 밀착(견착) 자세로 실제 교정 압력을 적용하며 호흡 3회.
/// 이 국면은 상체를 숙여 손이 FOV를 벗어나 손 추적이 불가하므로 손 판정을 하지 않는다 →
/// 게이트 = 자세 프록시(헤드셋-이마 근접) 유지비율 ≥ 임계 × 3회 호흡.
/// 첫 폴 시점에 호흡 윈도우를 시작한다(파지 유지/깊이 영점 불필요, 앞 ② 압력 학습과 무관하게 시작).
/// </summary>
public class BreathingCondition : IScenarioCondition
{
    private readonly CranialAdjustmentController controller;
    private readonly bool gripGate;
    private readonly int breaths;
    private readonly float inhaleSec, exhaleSec, firstCycleScale;
    private readonly BreathingSyncHUD.StartPhase startPhase;
    private bool started = false;

    /// <param name="gripGate">true면 호흡 1회 인정 조건이 '이마 견착 자세'가 아니라
    /// <b>양손 파지 성립</b>이 된다. 손이 시야에 남는 술기(PM)용 — 시간만 흘러도 카운트가
    /// 오르지 않고, 파지점에 제대로 대고 있어야 호흡이 세어진다.</param>
    /// <param name="breaths">이 substep의 호흡 횟수. 0이면 리그 오버라이드를 따른다.</param>
    /// <param name="firstCycleScale">첫 주기 길이 배수(PJ 교정의 '처음 한 번은 크게'). 0이면 배수 없음.</param>
    public BreathingCondition(CranialAdjustmentController controller, bool gripGate = false,
                              int breaths = 0, float inhaleSec = 0f, float exhaleSec = 0f,
                              BreathingSyncHUD.StartPhase startPhase = BreathingSyncHUD.StartPhase.Keep,
                              float firstCycleScale = 0f,
                              float thrustSpeed = 0f, float thrustMinDrop = 0f, bool thrustByHands = false,
                              float lateWindow = 3f)
    {
        this.lateWindow = lateWindow > 0f ? lateWindow : 3f;
        this.controller = controller;
        this.gripGate = gripGate;
        this.breaths = breaths;
        this.inhaleSec = inhaleSec;
        this.exhaleSec = exhaleSec;
        this.startPhase = startPhase;
        this.firstCycleScale = firstCycleScale;

        this.thrustSpeed = thrustSpeed;
        this.thrustByHands = thrustByHands;
        if (thrustSpeed > 0f || thrustByHands)
            thrust = new HeadThrustDetector(controller, thrustSpeed, thrustMinDrop, thrustByHands);
    }

    /// <summary>
    /// 0보다 크면 <b>호흡 유도와 순간 교정을 한 단계에서</b> 처리한다.
    ///
    /// ★단계를 나누면 흐름이 끊긴다(사용자 지시) — "다 내쉬면 누른다"는 하나의 동작이다.
    /// 호흡을 끝까지 따라간 뒤 순간 하강이 오면 완료, <b>다 내쉬기 전에 누르면 감점</b>하고
    /// 계속 기다린다. 성급한 쓰러스트를 그 자리에서 잡아 주는 것이 이 술기의 학습 목표다.
    /// </summary>
    private readonly float thrustSpeed;
    private readonly bool thrustByHands;
    private readonly HeadThrustDetector thrust;
    private int earlyThrusts;
    private float nextThrustLog;

    /// <summary>
    /// 다 내쉰 뒤 <b>이 시간 안에</b> 눌러야 정상으로 본다(초). 넘기면 '늦은 교정'으로 감점한다.
    ///
    /// ★타이밍 맞추기 게임이 아니므로 날숨 끝에 딱 맞출 필요는 없다. 다만 조작 인식이 한 박자
    /// 늦을 수 있어 여유를 주는 것이지, 한없이 기다려도 된다는 뜻은 아니다(사용자 지시).
    /// </summary>
    private readonly float lateWindow;
    private float exhaleDoneAt = -1f;
    private bool lateCounted;

    private void TryStart()
    {
        if (controller == null || started) return;

        // 견착 국면: 압력은 어깨-이마 밀착 상태에서 적용되어 손 추적이 불가하므로
        // 손 판정 없이 바로 호흡 윈도우 시작(게이트 = 자세 프록시 + N회 호흡).
        // gripGate면 대신 양손 파지 유지가 게이트가 된다.
        controller.StartBreathingWindow(gripGate, breaths, inhaleSec, exhaleSec, startPhase, firstCycleScale);
        started = true;
    }

    public bool IsConditionMet()
    {
        if (controller == null) return false;
        if (!started) { TryStart(); return false; }   // 첫 폴(나레이션 후) 시점에 호흡 윈도우 시작

        bool breathDone = controller.BreathingComplete;
        if (thrust == null) return breathDone;

        // ── 호흡 유도 + 순간 교정을 한 단계에서 ──────────────────────────
        bool hit = thrust.Detect(controller.BothGripped);

        if (!breathDone)
        {
            if (hit)
            {
                // ★다 내쉬기 전에 눌렀다 — 감점하고 계속 기다린다.
                earlyThrusts++;
                controller.ReportEarlyThrust();
                ChunaLogger.LogWarning($"[Breathing] ★너무 이른 교정 — 아직 다 내쉬지 않았습니다 " +
                                       $"({earlyThrusts}회째, 감점)");
            }
            else if (Time.time >= nextThrustLog)
            {
                nextThrustLog = Time.time + 1f;
                ChunaLogger.Log($"<color=cyan>[Breathing] 호흡 따라가는 중 — 다 내쉬면 누르세요</color>");
            }
            return false;
        }

        // 날숨이 끝난 시각을 기록해 '늦음'을 잰다.
        if (exhaleDoneAt < 0f) exhaleDoneAt = Time.time;
        float since = Time.time - exhaleDoneAt;

        if (hit)
        {
            bool late = since > lateWindow;
            if (late) controller.ReportLateThrust();

            ChunaLogger.Log($"<color={(late ? "orange" : "green")}>[Breathing] 순간 교정 감지 " +
                            $"(날숨 끝 +{since:0.#}초{(late ? $" — ★{lateWindow:0.#}초 초과, 감점" : "")})" +
                            $"{(earlyThrusts > 0 ? $" · 이른 교정 {earlyThrusts}회 감점" : "")}</color>");
            return true;
        }

        // 상한을 넘기면 한 번만 감점하고, 그래도 계속 기다린다(진행이 막히면 더 곤란하다).
        if (!lateCounted && since > lateWindow)
        {
            lateCounted = true;
            controller.ReportLateThrust();
            ChunaLogger.LogWarning($"[Breathing] ★교정이 늦습니다 ({lateWindow:0.#}초 초과) — 감점");
        }

        if (Time.time >= nextThrustLog)
        {
            nextThrustLog = Time.time + 1f;
            float left = lateWindow - since;
            ChunaLogger.Log(left > 0f
                ? $"<color=yellow>[Breathing] 다 내쉬었습니다 — 지금 순간 교정하세요 (남은 여유 {left:0.#}초)</color>"
                : "<color=orange>[Breathing] 여유 시간을 넘겼습니다 — 지금이라도 교정하세요</color>");
        }
        return false;
    }

    public string GetConditionDescription() =>
        thrust != null
            ? $"호흡 {(breaths > 0 ? breaths : 1)}회 후 순간 교정 대기 (다 내쉬기 전 누르면 감점)"
            : breaths > 0 ? $"호흡 {breaths}회 동기화 대기" : "호흡 동기화 대기";
}
