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

        // 그룹에 안 속한 화살표는 자기 설정으로 개별 관리한다 — 그룹은 여러 개를 묶고 싶을 때만 쓰는 선택 사항.
        // 기본값이 '교정 국면 전체'라 아무것도 안 적어도 파지~교정 내내 보인다.
        loose.Clear();
        foreach (ForceArrowBase a in Resources.FindObjectsOfTypeAll<ForceArrowBase>())
        {
            if (a == null || !a.gameObject.scene.IsValid()) continue;
            if (a.GetComponentInParent<ForceArrowGroup>(true) != null) continue;   // 그룹이 관리
            loose.Add(a);
        }

        if (debugLog)
            ChunaLogger.Log($"[ForceArrowDirector] 그룹 {resolved.Count}개 / 개별 화살표 {loose.Count}개 수집");
    }

    /// <summary>substep 진입 시 호출. 매칭되는 그룹만 켠다.</summary>
    public void ShowFor(string phaseName, string stepName, int subStepNo)
    {
        Collect();

        int shown = 0;
        foreach (ForceArrowGroup g in resolved)
        {
            if (g == null) continue;
            bool match = g.Matches(phaseName, stepName, subStepNo);
            if (match) shown++;
            g.SetShown(match);
        }

        foreach (ForceArrowBase a in loose)
        {
            if (a == null) continue;
            bool match = a.Matches(phaseName, stepName, subStepNo);
            if (match) shown++;
            if (a.gameObject.activeSelf != match) a.gameObject.SetActive(match);
        }

        if (debugLog && shown > 0)
            ChunaLogger.Log($"[ForceArrowDirector] {phaseName}/{stepName}.{subStepNo} → 그룹 {shown}개 표시");
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
