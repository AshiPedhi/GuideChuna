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

    [Tooltip("목표에 못 닿아도 이 시간이 지나면 넘긴다(초). 세션이 영영 멈추는 걸 막는 안전장치다.")]
    [SerializeField] private float stallTimeoutSeconds = 30f;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = true;

    private string lastStepKey;
    private string advancedKey;      // 같은 substep을 두 번 넘기지 않게
    private float stepEnteredTime;
    private bool warnedNotTarget;
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
            if (active) { active = false; driver.Paused = false; }
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
            lastStepKey = key;
            stepEnteredTime = Time.time;
            OnSubStepEntered(step.stepName, sub.subStepNo);
        }

        AdvanceOverpressure(step.stepName, sub.subStepNo);

        // 손을 떼면 그 자리에서 멈춘다.
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

        bool isOverpressure = stepName.EndsWith("압박", System.StringComparison.Ordinal);
        bool done;
        string reason;

        if (!isOverpressure)
        {
            if (subStepNo < 2) return;
            done = driver.ActiveReached;
            reason = $"능동 끝점 도달 {driver.CurrentAngle:F0}°";
        }
        else if (subStepNo == 2)
        {
            done = overpressureProgress >= 1f;
            reason = $"압박 완료 {driver.CurrentAngle:F0}° (부족각 {driver.DeficitAngle:F1}°)";
        }
        else
        {
            done = driver.AtNeutral;
            reason = "중립 복귀 완료";
        }

        // 안전장치 — 무언가 막혀도 세션이 영영 멈추지 않게 한다.
        if (!done)
        {
            if (stepEnteredTime > 0f && Time.time - stepEnteredTime > stallTimeoutSeconds)
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
        scenarioManager.NextSubStep();
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
