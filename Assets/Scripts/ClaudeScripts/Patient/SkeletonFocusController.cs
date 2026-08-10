using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 시나리오별 "필요 골격 중심적 표시" — 지정한 부위만 남기고 나머지 골격을 끈다.
/// (2026-08-05 회의: 두개골 = 파지 위치로 필요한 골격만 / 흉추 신전 = 흉추 외 다른 골격 비활성화)
///
/// ★해부 모델의 골격은 <b>부위 단위</b>다(실측: skull · cervical_spine · thoracic_spine ·
/// lumbar_spine · sacrum · sternum · thorax · clavicle · scapula …).
/// 두개골이 측두골·후두골로 쪼개져 있지 않으므로 <b>뼈 단위 표시는 이 컴포넌트로 불가능</b>하다
/// — 분할된 모델이 들어오면 keepParts에 이름만 추가하면 그대로 동작한다.
///
/// ★<see cref="AnatomyMuscleController"/>의 muscleGroups를 재사용하지 않은 이유:
/// 그쪽은 "매칭 안 되는 그룹은 전부 끈다"라서 골격을 그룹에 넣는 순간
/// <b>다른 시나리오에서도 그 뼈가 꺼진다</b>(골격 표시를 켜면 머리 없는 해골이 된다).
/// 여기서는 <b>목록에 없는 시나리오는 아예 건드리지 않는다</b> — 기존 동작 무회귀.
/// </summary>
public class SkeletonFocusController : MonoBehaviour
{
    [Serializable]
    public class FocusEntry
    {
        [Tooltip("시나리오 이름 (ScenarioConfig.scenarioName과 일치)")]
        public string scenarioName;

        [Tooltip("남길 골격 부위 오브젝트 이름들. 예: skull, cervical_spine\n" +
                 "대소문자·앞뒤 공백 무시. 여기 없는 골격 부위는 꺼진다.")]
        public List<string> keepParts = new List<string>();
    }

    [Header("=== 골격 루트 ===")]
    [Tooltip("골격 부위 오브젝트들이 들어 있는 루트(해부 모델). 여러 벌(투시용·관찰용)이면 전부 넣는다. " +
             "비우면 시작 시 skeletal_system 이름으로 찾아본다.")]
    [SerializeField] private List<Transform> skeletonRoots = new List<Transform>();

    [Header("=== 시나리오별 표시 ===")]
    [SerializeField] private List<FocusEntry> entries = new List<FocusEntry>();

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = true;

    /// <summary>이 컴포넌트가 끈 오브젝트만 기억한다 — 복원할 때 원래 꺼져 있던 것을 켜지 않기 위해.</summary>
    private readonly List<GameObject> turnedOff = new List<GameObject>();
    private bool rootsResolved;

    private void Awake() => ResolveRoots();

    private void ResolveRoots()
    {
        if (rootsResolved) return;
        rootsResolved = true;

        skeletonRoots.RemoveAll(t => t == null);
        if (skeletonRoots.Count > 0) return;

        foreach (Transform t in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (t == null || !t.gameObject.scene.IsValid()) continue;
            if (t.name.Equals("skeletal_system", StringComparison.OrdinalIgnoreCase))
                skeletonRoots.Add(t);
        }

        if (showDebugLogs)
            ChunaLogger.Log($"[SkeletonFocus] 골격 루트 {skeletonRoots.Count}개 " +
                            (skeletonRoots.Count == 0 ? "— 자동 탐색 실패, 인스펙터에서 지정 필요" : "확보"));
    }

    /// <summary>시나리오 진입 시 호출. 목록에 없는 시나리오면 이전 상태를 복원하고 끝낸다.</summary>
    public void ApplyScenario(string scenarioName)
    {
        ResolveRoots();
        RestoreAll();

        FocusEntry entry = Find(scenarioName);
        if (entry == null)
        {
            if (showDebugLogs)
                ChunaLogger.Log($"[SkeletonFocus] '{scenarioName}' 미지정 — 골격 전체 표시 유지");
            return;
        }

        int hidden = 0, kept = 0;
        foreach (Transform root in skeletonRoots)
        {
            if (root == null) continue;
            foreach (Transform child in root)   // 부위는 루트 바로 아래 1단계에 있다
            {
                bool keep = Contains(entry.keepParts, child.name);
                if (keep) { kept++; continue; }
                if (!child.gameObject.activeSelf) continue;   // 원래 꺼져 있던 건 건드리지 않는다
                child.gameObject.SetActive(false);
                turnedOff.Add(child.gameObject);
                hidden++;
            }
        }

        if (showDebugLogs)
            ChunaLogger.Log($"<color=cyan>[SkeletonFocus] '{scenarioName}' → 남김 {kept}개 / 숨김 {hidden}개</color>");
    }

    /// <summary>이 컴포넌트가 끈 것만 되돌린다.</summary>
    public void RestoreAll()
    {
        foreach (GameObject go in turnedOff)
            if (go != null) go.SetActive(true);
        turnedOff.Clear();
    }

    private FocusEntry Find(string scenarioName)
    {
        if (string.IsNullOrWhiteSpace(scenarioName)) return null;
        foreach (FocusEntry e in entries)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.scenarioName)) continue;
            if (e.scenarioName.Trim().Equals(scenarioName.Trim(), StringComparison.OrdinalIgnoreCase))
                return e;
        }
        return null;
    }

    private static bool Contains(List<string> list, string name)
    {
        if (list == null) return false;
        foreach (string s in list)
            if (!string.IsNullOrWhiteSpace(s) &&
                s.Trim().Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
