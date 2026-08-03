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
public class DiagnosisHoldCondition : IScenarioCondition
{
    private readonly CranialAdjustmentController controller;
    private readonly string stageId;
    private bool usingStage;
    private bool started = false;

    public DiagnosisHoldCondition(CranialAdjustmentController controller, string stageId)
    {
        this.controller = controller;
        this.stageId = string.IsNullOrWhiteSpace(stageId) ? "" : stageId.Trim();

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
    }

    public bool IsConditionMet()
    {
        if (controller == null) return false;
        if (!started) { TryStart(); return false; }   // 첫 폴(나레이션 후) 시점에 타이머 시작
        return usingStage ? controller.DiagnosisStageComplete : controller.BothHandsTouched;
    }

    public string GetConditionDescription() =>
        usingStage ? $"진단 자세 유지 대기 ({stageId})" : "진단 촉진(양손 접촉) 대기";
}

/// <summary>
/// ① 파지 substep: 양손 파이브핑거홀드(트리거+포즈) 성립 시 완료.
/// 영점은 여기서 저장하지 않는다 — ②a(PressureCondition) 진입 시 파지 유지 재확인 후 저장.
/// </summary>
public class GripPointCondition : IScenarioCondition
{
    private readonly CranialAdjustmentController controller;

    public GripPointCondition(CranialAdjustmentController controller)
    {
        this.controller = controller;
        this.controller?.BeginGripPhase();
    }

    public bool IsConditionMet()
    {
        return controller != null && controller.BothGripped;
    }

    public string GetConditionDescription() => "두개골 파지(양손 파이브핑거홀드) 대기";
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

    public PressureCondition(CranialAdjustmentController controller, float holdDuration = 1.0f, float graceTime = 0.5f)
    {
        this.controller = controller;
        this.holdDuration = holdDuration;
        this.graceTime = graceTime;
    }

    private void TrySaveZero()
    {
        if (controller == null || zeroSaved) return;
        if (!controller.BothGripped) return;   // 파지 유지 재확인 후 영점 저장
        controller.SaveZeroPoints();           // 깊이 판정 OFF면 컨트롤러가 알아서 무시
        zeroSaved = true;
    }

    /// <summary>이 단계의 유지 조건. 기본 = 파지 유지, 깊이 판정을 켠 경우만 적정 텐션 존.</summary>
    private bool Holding =>
        controller.UseDepthJudging ? controller.BothInGoodZone : controller.BothGripped;

    public bool IsConditionMet()
    {
        if (controller == null) return false;

        // 첫 폴(나레이션 후) 시점에 휴식 위치를 영점으로 저장. 파지가 잠깐 풀렸으면 다음 폴에서 재시도.
        // (깊이 판정 OFF면 SaveZeroPoints가 no-op이라 파지만 확인하고 지나간다.)
        if (!zeroSaved) { TrySaveZero(); return false; }

        // 디버그: 완료는 시키지 않음 → 자유 관찰
        if (controller.DebugFreezePressureStep) return false;

        if (Holding)
        {
            leftZoneAt = -1f;
            if (heldSince < 0f) heldSince = Time.time;
            return Time.time - heldSince >= holdDuration;
        }

        // 유지 이탈(파지 풀림 / 존 이탈): graceTime 이내의 짧은 흔들림이면 타이머(heldSince) 보존, 초과하면 리셋.
        // (완료 판정은 위 Holding 분기에서만 나므로, 유예 중에는 완료되지 않고 복귀를 기다린다.)
        if (heldSince >= 0f)
        {
            if (leftZoneAt < 0f) leftZoneAt = Time.time;
            if (Time.time - leftZoneAt <= graceTime) return false;   // 아직 유예 중 → 리셋 안 함
        }
        heldSince = -1f;
        leftZoneAt = -1f;
        return false;
    }

    public string GetConditionDescription() =>
        controller != null && controller.UseDepthJudging
            ? "압력·방향 적용 (양손 적정 텐션 유지) 대기"
            : "압력·방향 적용 (양손 파지 유지) 대기";
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
    private bool started = false;

    /// <param name="gripGate">true면 호흡 1회 인정 조건이 '이마 견착 자세'가 아니라
    /// <b>양손 파지 성립</b>이 된다. 손이 시야에 남는 술기(PM)용 — 시간만 흘러도 카운트가
    /// 오르지 않고, 파지점에 제대로 대고 있어야 호흡이 세어진다.</param>
    public BreathingCondition(CranialAdjustmentController controller, bool gripGate = false)
    {
        this.controller = controller;
        this.gripGate = gripGate;
    }

    private void TryStart()
    {
        if (controller == null || started) return;

        // 견착 국면: 압력은 어깨-이마 밀착 상태에서 적용되어 손 추적이 불가하므로
        // 손 판정 없이 바로 호흡 윈도우 시작(게이트 = 자세 프록시 + N회 호흡).
        // gripGate면 대신 양손 파지 유지가 게이트가 된다.
        controller.StartBreathingWindow(gripGate);
        started = true;
    }

    public bool IsConditionMet()
    {
        if (controller == null) return false;
        if (!started) { TryStart(); return false; }   // 첫 폴(나레이션 후) 시점에 호흡 윈도우 시작
        return controller.BreathingComplete;
    }

    public string GetConditionDescription() => "호흡 3회 동기화 대기";
}
