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
/// ⓪ 진단 촉진 substep: 양손으로 환자 뒤통수(후두)를 감싸 양손 손바닥이 모두 닿으면 완료.
/// 압력·깊이·포즈를 보지 않고 트리거 접촉만 판정한다(진단 단계는 촉진일 뿐, 교정압 없음).
/// 왼손은 교정과 동일한 후두 Palm(leftGrips) 재사용, 오른손은 후두 우측 Palm(diagnosisRightGrips)을 쓴다.
/// </summary>
public class DiagnosisTouchCondition : IScenarioCondition
{
    private readonly CranialAdjustmentController controller;

    public DiagnosisTouchCondition(CranialAdjustmentController controller)
    {
        this.controller = controller;
        this.controller?.BeginDiagnosisPhase();
    }

    public bool IsConditionMet()
    {
        return controller != null && controller.BothHandsTouched;
    }

    public string GetConditionDescription() => "진단 촉진(양손 다섯 손가락 터치) 대기";
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
/// ② 압력 학습(조절) substep: 견착 前 손 추적이 되는 동안 압력 감각을 익히는 단계.
/// 첫 폴 시점에 파지 유지 확인→영점 저장(휴식 위치=깊이 0). 이후 양손이 적정 텐션 존을
/// holdDuration 유지하면 완료. 손가락별 색 피드백으로 "너무 세게 주지 않기"를 학습한다.
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
        controller.SaveZeroPoints();
        zeroSaved = true;
    }

    public bool IsConditionMet()
    {
        if (controller == null) return false;

        // 첫 폴(나레이션 후) 시점에 휴식 위치를 영점으로 저장. 파지가 잠깐 풀렸으면 다음 폴에서 재시도.
        if (!zeroSaved) { TrySaveZero(); return false; }

        // 디버그: 영점은 저장(압력 색 표시)하되 완료는 시키지 않음 → 자유 관찰
        if (controller.DebugFreezePressureStep) return false;

        if (controller.BothInGoodZone)
        {
            leftZoneAt = -1f;
            if (heldSince < 0f) heldSince = Time.time;
            return Time.time - heldSince >= holdDuration;
        }

        // 적정존 이탈: graceTime 이내의 짧은 흔들림이면 타이머(heldSince) 보존, 초과하면 리셋.
        // (완료 판정은 위 BothInGoodZone 분기에서만 나므로, 유예 중에는 완료되지 않고 존 복귀를 기다린다.)
        if (heldSince >= 0f)
        {
            if (leftZoneAt < 0f) leftZoneAt = Time.time;
            if (Time.time - leftZoneAt <= graceTime) return false;   // 아직 유예 중 → 리셋 안 함
        }
        heldSince = -1f;
        leftZoneAt = -1f;
        return false;
    }

    public string GetConditionDescription() => "압력·방향 적용 (양손 적정 텐션 유지) 대기";
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
    private bool started = false;

    public BreathingCondition(CranialAdjustmentController controller)
    {
        this.controller = controller;
    }

    private void TryStart()
    {
        if (controller == null || started) return;

        // 견착 국면: 압력은 어깨-이마 밀착 상태에서 적용되어 손 추적이 불가하므로
        // 손 판정 없이 바로 호흡 윈도우 시작(게이트 = 자세 프록시 + 3회 호흡).
        controller.StartBreathingWindow();
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
