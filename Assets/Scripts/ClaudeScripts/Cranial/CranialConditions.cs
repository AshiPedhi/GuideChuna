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
/// ②a 압력·방향 substep: 첫 폴 시점에 파지 유지 확인→영점 저장(현재 휴식 위치 = 깊이 0).
/// 이후 양손이 적정 텐션 존(올바른 깊이·방향)을 holdDuration 동안 유지하면 완료.
/// 임상 3단계(힘의 방향 적용 + 자세 안정화)에 대응. 호흡은 다음 substep에서.
/// </summary>
public class PressureCondition : IScenarioCondition
{
    private readonly CranialAdjustmentController controller;
    private readonly float holdDuration;
    private bool zeroSaved = false;
    private float heldSince = -1f;

    public PressureCondition(CranialAdjustmentController controller, float holdDuration = 1.0f)
    {
        this.controller = controller;
        this.holdDuration = holdDuration;
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

        if (controller.BothInGoodZone)
        {
            if (heldSince < 0f) heldSince = Time.time;
            return Time.time - heldSince >= holdDuration;
        }
        heldSince = -1f;   // 적정존 이탈 시 유지 타이머 리셋
        return false;
    }

    public string GetConditionDescription() => "압력·방향 적용 (양손 적정 텐션 유지) 대기";
}

/// <summary>
/// ②b 호흡 substep: 첫 폴 시점에 호흡 윈도우 시작(영점은 ②a 것을 유지 — 재저장하지 않음).
/// 호흡 3회 동안 적정 텐션 유지비율 ≥ 임계 충족 시 완료. 임상 4단계(호흡 동기화)에 대응.
/// ②a를 건너뛰어 영점이 없으면 파지 유지 시 lazy 저장(끊김 방어).
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

        // 영점이 없으면(②a 생략/실패 대비) 파지 유지 시 여기서 lazy 저장
        if (!controller.HasZeroPoints)
        {
            if (!controller.BothGripped) return;
            controller.SaveZeroPoints();
        }

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
