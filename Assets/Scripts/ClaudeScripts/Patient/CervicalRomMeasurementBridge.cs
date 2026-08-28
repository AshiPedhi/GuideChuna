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

    [Tooltip("표준자세 체크리스트. 없으면 준비 단계를 게이트하지 않는다.")]
    [SerializeField] private PostureChecklistUI checklist;

    [Tooltip("이 이름의 시나리오에서만 돈다.")]
    [SerializeField] private string scenarioName = "경추ROM측정";

    [Tooltip("중립 복귀로 인정할 각(도).")]
    [SerializeField] private float neutralTolerance = 6f;

    [Tooltip("한 substep을 넘긴 뒤 이만큼은 다시 안 넘긴다(초). 연속 진행 방지.")]
    [SerializeField] private float advanceCooldown = 0.6f;

    [SerializeField] private bool showDebugLogs = true;

    private string advancedKey;      // 이미 넘긴 substep 표식
    private float lastAdvanceTime = -99f;
    private bool active;
    private bool warnedNoMeasure;

    private void Awake()
    {
        if (scenarioManager == null) scenarioManager = FindFirstObjectByType<ScenarioManager>();
        if (measure == null) measure = FindFirstObjectByType<CervicalRomRealityMeasure>(FindObjectsInactive.Include);
        if (evaluator == null) evaluator = FindFirstObjectByType<ChunaPathEvaluator>();
        if (checklist == null) checklist = FindFirstObjectByType<PostureChecklistUI>(FindObjectsInactive.Include);

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
                // ★교육모드로 돌아가면 실측 표시물을 걷는다. 켠 채로 두면 화면이 겹친다.
                if (measure != null) measure.enabled = false;
                if (checklist != null) checklist.SetVisible(false);
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
            measure.ResetAll();
            if (checklist != null) checklist.ResetChecks();
            ChunaLogger.Log("<color=cyan>[실측Bridge] 실측모드 진입 — 측정기를 초기화했다.</color>");
        }

        string name = step.stepName;
        int subNo = sub.subStepNo;
        string key = $"{name}#{subNo}";

        ApplyDirectionFor(name);

        // 준비 단계에서만 체크리스트를 띄운다. 벗어나면 접는다.
        if (checklist != null) checklist.SetVisible(name == "준비");

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

    /// <summary>단계 이름으로 측정 방향을 정한다. 파지·준비 단계는 방향을 안 건드린다.</summary>
    private void ApplyDirectionFor(string stepName)
    {
        CervicalRomDriver.Direction d = DirectionOf(stepName);
        if (d != CervicalRomDriver.Direction.None) measure.SetDirection(d);
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
        // ★준비 단계는 표준자세 세 줄을 <b>전부 체크</b>해야 넘어간다(2026-08-27 회의 결정).
        //   체크리스트가 없으면 게이트하지 않는다 — 없다고 진행이 막히면 원인을 못 찾는다.
        if (stepName == "준비") return checklist == null ? false : checklist.AllChecked;
        if (stepName == "결과") return false;

        if (stepName == "기준정렬") return measure.FrameReady;

        if (stepName.EndsWith("파지")) return measure.NeutralReady;

        CervicalRomDriver.Direction d = DirectionOf(stepName);
        if (d == CervicalRomDriver.Direction.None) return false;

        switch (subNo)
        {
            case 1: return measure.HasActive(d);
            case 2: return measure.HasPassive(d);
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
