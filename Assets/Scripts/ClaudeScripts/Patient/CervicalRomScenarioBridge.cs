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
/// 압박 진행률은 지금 <b>접촉 유지 시간</b>으로 낸다. 임시다 —
/// 원래는 시술자 손의 상대 회전각이 소스여야 하는데, 그러려면 손 녹화나
/// 총 회전각 상수가 필요하고 아직 정해지지 않았다.
/// </summary>
public class CervicalRomScenarioBridge : MonoBehaviour
{
    [Header("=== 참조 (비우면 자동 탐색) ===")]
    [SerializeField] private ScenarioManager scenarioManager;
    [SerializeField] private CervicalRomDriver driver;
    [SerializeField] private ChunaPathEvaluator evaluator;

    [Header("=== 대상 시나리오 ===")]
    [Tooltip("이 이름의 시나리오에서만 동작한다. 다른 술기에는 개입하지 않는다.")]
    [SerializeField] private string scenarioName = "경추ROM측정";

    [Header("=== 압박 ===")]
    [Tooltip("압박 유지 substep에서 0 → 1까지 가는 데 걸리는 시간(초).\n" +
             "손의 회전각을 소스로 바꾸기 전까지 쓰는 임시값이다.")]
    [SerializeField] private float overpressureRampSeconds = 3f;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = true;

    private string lastStepKey;
    private float overpressureProgress;
    private bool active;

    private void Awake()
    {
        if (scenarioManager == null) scenarioManager = FindFirstObjectByType<ScenarioManager>();
        if (driver == null) driver = FindFirstObjectByType<CervicalRomDriver>();
        if (evaluator == null) evaluator = FindFirstObjectByType<ChunaPathEvaluator>();

        if (driver == null)
        {
            ChunaLogger.LogWarning("[ROM Bridge] CervicalRomDriver를 찾지 못했습니다. 환자에 붙였는지 확인하세요.");
            enabled = false;
        }
    }

    private void Update()
    {
        if (scenarioManager == null || driver == null) return;

        StepData step = scenarioManager.CurrentStep;
        SubStepData sub = scenarioManager.CurrentSubStep;
        if (step == null || sub == null) return;

        if (!IsTargetScenario())
        {
            if (active) { active = false; driver.Paused = false; }
            return;
        }
        active = true;

        string key = $"{step.stepName}#{sub.subStepNo}";
        if (key != lastStepKey)
        {
            lastStepKey = key;
            OnSubStepEntered(step.stepName, sub.subStepNo);
        }

        AdvanceOverpressure(step.stepName, sub.subStepNo);

        // 손을 떼면 그 자리에서 멈춘다.
        driver.Paused = !BothHandsTouching();
    }

    private void OnSubStepEntered(string stepName, int subStepNo)
    {
        CervicalRomDriver.Direction dir = DirectionOf(stepName);
        if (dir == CervicalRomDriver.Direction.None) return;

        bool isOverpressure = stepName.EndsWith("압박", System.StringComparison.Ordinal);

        if (!isOverpressure)
        {
            // 능동 — 지시(x.1) 다음 동작(x.2)에서 움직이기 시작한다.
            if (subStepNo >= 2)
            {
                driver.BeginActive(dir);
                Log($"능동 시작 {stepName} → {driver.ActiveTargetAngle:F0}° (여유 {driver.CurrentGap:F1}°)");
            }
            return;
        }

        // 압박 — 유지(x.2)에서 밀고, 복귀(x.3)에서 중립으로 돌아온다.
        if (subStepNo == 2)
        {
            overpressureProgress = 0f;
            Log($"압박 시작 {stepName} — {driver.CurrentAngle:F0}° 에서 {driver.NormalAngle:F0}° 까지");
        }
        else if (subStepNo >= 3)
        {
            driver.ReturnToNeutral();
            Log($"중립 복귀 {stepName} (부족각 {driver.DeficitAngle:F1}°)");
        }
    }

    private void AdvanceOverpressure(string stepName, int subStepNo)
    {
        if (subStepNo != 2 || !stepName.EndsWith("압박", System.StringComparison.Ordinal)) return;
        if (!BothHandsTouching()) return;   // 손을 대고 있는 동안에만 밀린다

        if (overpressureRampSeconds > 0f)
        {
            overpressureProgress = Mathf.Clamp01(
                overpressureProgress + Time.deltaTime / overpressureRampSeconds);
        }
        else
        {
            overpressureProgress = 1f;
        }

        driver.SetOverpressure(overpressureProgress);
    }

    private bool BothHandsTouching()
    {
        if (evaluator == null) return true;   // 판정기가 없으면 게이트를 걸지 않는다
        return evaluator.IsLeftHandTouchingPatient && evaluator.IsRightHandTouchingPatient;
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
