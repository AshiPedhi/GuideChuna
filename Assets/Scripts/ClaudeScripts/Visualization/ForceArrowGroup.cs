using UnityEngine;

/// <summary>
/// 한 단계에서 같이 켜지는 힘의 방향 화살표 묶음.
///
/// ★온오프 단위는 substep이 아니라 <b>단계(동작)</b>다 — 사용자가 가이드손에서 반복해 지적한 규칙과 같다.
/// 그래서 기본 매칭은 stepName 하나이고, 한 단계 안에서도 화살표가 바뀌어야 할 때만 subStepNo를 채운다.
/// 중간 substep에 지정을 빠뜨려 화살표가 껐다 켜지며 깜빡이는 사고(xray의 restoreEachSubStep과 같은 함정)를
/// 구조적으로 피하기 위한 선택이다.
///
/// ★배치 자유 — 그룹은 <b>자기 GameObject가 아니라 화살표 하나하나를 켜고 끈다.</b>
/// 화살표는 환자 애니메이션을 따라가야 해서 보통 파지점·머리 본의 자식으로 두는데,
/// 그룹이 자기 자신만 토글하면 그렇게 떨어져 있는 화살표를 제어할 수 없다.
///   · 화살표를 그룹 자식으로 두면 → 자동 수집된다(Arrows 비워 두기).
///   · 화살표를 파지점 자식으로 두면 → Arrows 슬롯에 끌어다 넣는다.
/// 어느 쪽이든 리그가 비활성이면 화살표도 같이 꺼지므로 다른 시나리오에 새어 나가지 않는다.
/// </summary>
public class ForceArrowGroup : MonoBehaviour
{
    [Header("=== 언제 보일지 ===")]
    [Tooltip("기본 '교정국면 파지제외' = 잠금·견착·호흡·교정에만 표시(파지·준비 제외). " +
             "아래 칸은 '특정 단계만'일 때만 쓴다.")]
    [SerializeField] private ForceArrowBase.ShowScope showWhen = ForceArrowBase.ShowScope.교정국면_파지제외;

    [Tooltip("'특정 단계만'일 때 쓸 단계 이름(CSV stepName). 예: 호흡유도 / 교정")]
    [SerializeField] private string stepName = "";

    [Tooltip("0 = 그 단계 전체. 1 이상이면 그 subStep에서만 표시.")]
    [SerializeField] private int subStepNo = 0;

    [Tooltip("여러 subStep에서 켜려면 쉼표로 나열한다. 예: 7,9,11\n" +
             "★당기고 밀기를 반복하는 단계용 — 방향이 반대인 화살표 2개를 만들어 " +
             "하나는 7,9,11(올리기) 다른 하나는 8,10,12(내리기)로 두면 번갈아 켜진다.\n" +
             "채우면 위의 subStepNo 대신 이 목록을 쓴다.")]
    [SerializeField] private string subStepNos = "";

    [Tooltip("선택. 채우면 국면(phase)까지 일치해야 표시한다.")]
    [SerializeField] private string phaseName = "";

    [Tooltip("선택. 비우면 부모 리그(CranialAdjustmentController)의 시나리오를 자동으로 물려받는다.\n" +
             "★이게 없으면 OM 화살표가 PJ 실습 중에도 켜진다(08-12 수정).")]
    [SerializeField] private string scenarioName = "";

    [Tooltip("이 단계에서 켤 화살표. 비우면 자식에서 자동 수집한다. " +
             "화살표를 파지점 등 다른 곳의 자식으로 뒀다면 여기에 끌어다 넣어야 한다.")]
    [SerializeField] private ForceArrowBase[] arrows;

    /// <summary>자식 화살표 목록(비어 있으면 자동 수집). 비활성 상태에서도 읽을 수 있어야 하므로 지연 수집한다.</summary>
    public ForceArrowBase[] Arrows
    {
        get
        {
            if (arrows == null || arrows.Length == 0)
                arrows = GetComponentsInChildren<ForceArrowBase>(true);
            return arrows;
        }
    }

    /// <summary>이 그룹의 화살표를 켜고 끈다(그룹 GameObject가 아니라 화살표 각각을 토글).</summary>
    public void SetShown(bool on)
    {
        foreach (ForceArrowBase a in Arrows)
        {
            if (a == null) continue;
            if (a.gameObject.activeSelf != on) a.gameObject.SetActive(on);
        }
    }

    /// <summary>이 그룹이 속한 시나리오. 비었으면 부모 리그에서 물려받는다(캐시).</summary>
    public string ResolvedScenario
    {
        get
        {
            if (scenarioCached) return resolvedScenario;
            scenarioCached = true;
            resolvedScenario = scenarioName;
            if (string.IsNullOrWhiteSpace(resolvedScenario))
            {
                var rig = GetComponentInParent<CranialAdjustmentController>(true);
                if (rig != null) resolvedScenario = rig.ScenarioName;
            }
            return resolvedScenario;
        }
    }

    private string resolvedScenario;
    private bool scenarioCached;

    public bool Matches(string scenario, string phase, string step, int subNo)
    {
        if (!ForceArrowBase.ScenarioMatch(this, ResolvedScenario, scenario)) return false;

        // ★목록이 있으면 그것이 이긴다 — 단계 매칭은 subStep 무관(0)으로 보고, subStep은 목록으로 판단.
        if (!string.IsNullOrWhiteSpace(subStepNos))
            return ForceArrowBase.ScopeMatch(showWhen, stepName, 0, phaseName, phase, step, subNo) &&
                   ListContains(subStepNos, subNo);

        return ForceArrowBase.ScopeMatch(showWhen, stepName, subStepNo, phaseName, phase, step, subNo);
    }

    /// <summary>"7,9,11" 형태의 목록에 이 subStep 번호가 들어 있는가.</summary>
    private static bool ListContains(string list, int subNo)
    {
        foreach (string tok in list.Split(','))
            if (int.TryParse(tok.Trim(), out int v) && v == subNo) return true;
        return false;
    }

    /// <summary>인스펙터에서 무엇과 매칭되는지 한눈에 보이도록.</summary>
    public string DescribeMatch() =>
        $"[{(string.IsNullOrWhiteSpace(ResolvedScenario) ? "모든 시나리오" : ResolvedScenario)}] " +
        ForceArrowBase.Describe(showWhen, stepName, subStepNo, phaseName) +
        (string.IsNullOrWhiteSpace(subStepNos) ? "" : $"  (subStep {subStepNos})");
}
