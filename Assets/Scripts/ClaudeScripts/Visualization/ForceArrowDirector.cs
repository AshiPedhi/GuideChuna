using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 힘의 방향 화살표 전체를 substep 진입 시점에 켜고 끄는 관리자. 씬에 1개.
///
/// ScenarioManager가 매 substep 시작에서 <see cref="ShowFor"/>를 부른다. 매칭되는 그룹만 켜고
/// 나머지는 끈다 → 진단·재평가처럼 그룹이 없는 단계에서는 자동으로 아무것도 안 보인다
/// (촉진으로 좌우를 비교하는 단계에 힌트를 주지 않기 위한 규칙과 일치).
///
/// ★그룹은 비활성 리그 아래에 있을 수 있어 Awake가 돌지 않는다 → 자기 등록 방식을 못 쓴다.
/// 그래서 Director가 <see cref="Resources.FindObjectsOfTypeAll"/>로 비활성 포함 수집한다
/// (프리팹·에셋이 섞이므로 로드된 씬에 속한 것만 남긴다).
/// </summary>
public class ForceArrowDirector : MonoBehaviour
{
    [Tooltip("비우면 씬 전체에서 비활성 포함 자동 수집한다(권장). 채우면 그 목록만 관리한다.")]
    [SerializeField] private ForceArrowGroup[] groups;

    [Tooltip("켜면 어떤 그룹이 매칭됐는지 로그를 남긴다(배선 확인용).")]
    [SerializeField] private bool debugLog = false;

    private readonly List<ForceArrowGroup> resolved = new List<ForceArrowGroup>();
    /// <summary>그룹에 속하지 않고 스스로 단계를 지정한 화살표들 — 개별로 켜고 끈다.</summary>
    private readonly List<ForceArrowBase> loose = new List<ForceArrowBase>();
    /// <summary>어느 그룹이든 arrows[]로 참조하는 화살표 — 계층상 자식이 아니어도 그룹 소관이다.</summary>
    private readonly HashSet<ForceArrowBase> owned = new HashSet<ForceArrowBase>();
    /// <summary>이번 substep에 각 화살표를 켤지 — 공유 화살표 때문에 모아서 한 번에 적용한다.</summary>
    private readonly Dictionary<ForceArrowBase, bool> want = new Dictionary<ForceArrowBase, bool>();
    private bool collected;

    private void Awake()
    {
        Collect();
        HideAll();
    }

    private void Collect()
    {
        if (collected) return;
        collected = true;

        resolved.Clear();
        if (groups != null && groups.Length > 0)
        {
            foreach (ForceArrowGroup g in groups)
                if (g != null) resolved.Add(g);
        }
        else
        {
            foreach (ForceArrowGroup g in Resources.FindObjectsOfTypeAll<ForceArrowGroup>())
            {
                // 프리팹 에셋·에디터 전용 오브젝트 제외 — 로드된 씬에 속한 것만.
                if (g == null || !g.gameObject.scene.IsValid()) continue;
                resolved.Add(g);
            }
        }

        // ★그룹이 '배열로 참조'하는 화살표도 그룹 소관이다 — 자식이 아니어도.
        //   2026-08-12 버그: 화살표를 파지점 밑에 두고 그룹의 arrows[]에 끌어다 넣은 배선(PJ가 그렇다)에서
        //   GetComponentInParent만 보고 '단독'으로 분류해 버려, 그룹의 단계 지정을 통째로 무시했다.
        //   그래서 PJ 교정 국면 내내 굴곡·신전·외회전·내회전 4개가 한꺼번에 켜졌다.
        owned.Clear();
        foreach (ForceArrowGroup g in resolved)
        {
            if (g == null) continue;
            foreach (ForceArrowBase a in g.Arrows)
                if (a != null) owned.Add(a);
        }

        // 어느 그룹에도 속하지 않은 화살표만 개별 관리한다.
        loose.Clear();
        foreach (ForceArrowBase a in Resources.FindObjectsOfTypeAll<ForceArrowBase>())
        {
            if (a == null || !a.gameObject.scene.IsValid()) continue;
            if (owned.Contains(a)) continue;                                       // 그룹이 배열로 관리
            if (a.GetComponentInParent<ForceArrowGroup>(true) != null) continue;   // 그룹이 계층으로 관리
            loose.Add(a);
        }

        // ★한 번만 찍는 배선 요약 — debugLog와 무관하게 남긴다.
        //   "화살표가 왜 저 시나리오에서 뜨냐"를 로그만 보고 판별할 수 있어야 한다(08-12).
        var sb = new System.Text.StringBuilder();
        sb.Append($"[ForceArrowDirector] 그룹 {resolved.Count}개 / 개별 화살표 {loose.Count}개 수집");
        foreach (ForceArrowGroup g in resolved)
            if (g != null) sb.Append($"\n   [그룹] {g.name} → {g.DescribeMatch()} (화살표 {g.Arrows.Length}개)");
        foreach (ForceArrowBase a in loose)
            if (a != null) sb.Append($"\n   [단독] {a.name} → {a.DescribeMatch()}");
        ChunaLogger.Log(sb.ToString());
    }

    /// <summary>
    /// substep 진입 시 호출. 매칭되는 그룹만 켠다.
    /// ★<paramref name="scenarioName"/>은 2026-08-12에 추가됐다 — 이게 없어서 OM·PM 화살표가
    /// PJ 실습 중에도 같이 켜졌다(기본 스코프가 '교정 국면 전체'라 시나리오를 안 가렸다).
    /// </summary>
    public void ShowFor(string scenarioName, string phaseName, string stepName, int subStepNo)
    {
        Collect();

        // ★화살표 하나를 여러 그룹이 공유할 수 있다(PJ: 굴곡외회전·견착·호흡이 같은 쌍을 쓴다).
        //   그룹마다 바로 SetShown을 부르면 <b>마지막에 처리된 그룹이 이겨</b> 켜져야 할 화살표가 꺼진다.
        //   그래서 원하는 상태를 먼저 모으고(하나라도 켜라면 켠다) 마지막에 한 번만 적용한다.
        want.Clear();

        int shown = 0;
        foreach (ForceArrowGroup g in resolved)
        {
            if (g == null) continue;
            bool match = g.Matches(scenarioName, phaseName, stepName, subStepNo);
            if (match) shown++;
            foreach (ForceArrowBase a in g.Arrows)
            {
                if (a == null) continue;
                want.TryGetValue(a, out bool cur);
                want[a] = cur || match;
            }
        }

        foreach (ForceArrowBase a in loose)
        {
            if (a == null) continue;
            bool match = a.Matches(scenarioName, phaseName, stepName, subStepNo);
            if (match) shown++;
            want.TryGetValue(a, out bool cur);
            want[a] = cur || match;
        }

        foreach (var kv in want)
            if (kv.Key != null && kv.Key.gameObject.activeSelf != kv.Value)
                kv.Key.gameObject.SetActive(kv.Value);

        if (debugLog && shown > 0)
            ChunaLogger.Log($"[ForceArrowDirector] {scenarioName} {phaseName}/{stepName}.{subStepNo} → 매칭 {shown}개");
    }

    /// <summary>시나리오 종료·전환 시 전부 끈다.</summary>
    public void HideAll()
    {
        Collect();
        foreach (ForceArrowGroup g in resolved)
            if (g != null) g.SetShown(false);
        foreach (ForceArrowBase a in loose)
            if (a != null && a.gameObject.activeSelf) a.gameObject.SetActive(false);
    }
}
