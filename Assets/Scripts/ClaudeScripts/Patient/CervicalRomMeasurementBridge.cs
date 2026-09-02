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

    [SerializeField] private bool showDebugLogs = true;

    private string advancedKey;      // 이미 넘긴 substep 표식
    private float lastAdvanceTime = -99f;
    private bool active;
    private bool warnedNoMeasure;

    private ScenarioGuideUIController guideUI;
    private bool resultToggleShown;

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
            EnterRealWorld();
            ChunaLogger.Log("<color=cyan>[실측Bridge] 실측모드 진입 — 측정기를 초기화했다.</color>");
        }

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

    private void OnDisable()
    {
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
        if (measure == null || !measure.UsePracticeGauge) return;
        if (planeGauge == null)
        {
            ChunaLogger.LogWarning("[실측Bridge] CervicalRomPlaneGauge를 못 찾았습니다 — 각도기 없이 진행합니다.");
            return;
        }

        planeGauge.SetSource(measure);
        planeGauge.SetRealityLook(true);
        if (showDebugLogs) ChunaLogger.Log("<color=cyan>[실측Bridge] 각도기를 실측 출처로 바꿨다(판·채움 끔).</color>");
    }

    /// <summary>각도기를 교육모드 상태로 되돌린다.</summary>
    private void RestorePlaneGauge()
    {
        if (planeGauge == null) return;
        if (!planeGauge.HasExternalSource) return;   // 우리가 안 끼웠으면 안 건드린다

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

        // ★결과도 브리지가 넘기지 않는다. 대신 [다음] 토글을 띄워 사람이 읽고 넘기게 한다.
        //   토글은 stepNo 0에서만 자동으로 뜨는데 '결과'는 stepNo 12라 안 뜬다 —
        //   그래서 종전에는 여기서 통째로 멈췄다.
        if (stepName == "결과")
        {
            ShowResultNextToggle();
            return false;
        }

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
