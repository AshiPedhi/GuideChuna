using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 시나리오·단계별로 "이 뼈들만 보이게" 하는 컨트롤러.
/// (2026-08-05 회의: 필요 골격 중심적 표시 / 08-11 사용자: 단계마다 보이는 뼈가 달라야 한다)
///
/// ★사용법은 하나뿐이다 — <b>줄마다 시나리오·국면·단계를 적고, 보일 뼈를 씬에서 드래그해 넣는다.</b>
///   예) 두개골OM교정 / 평가 / 진단  → 후두골
///       두개골OM교정 / 교정 / 파지  → 후두골, 좌·우 측두골, 관골
///
/// ★숨김 범위는 자동이다. 모든 줄에 할당된 뼈들의 <b>공통 부모</b>를 범위로 잡고,
/// 그 안에서 지금 줄에 없는 것을 끈다. 그래서 루트를 지정하는 칸이 없다.
///
/// ★<see cref="AnatomyMuscleController"/>와 달리 <b>목록에 없는 시나리오·단계는 아예 건드리지 않는다</b>
/// — 골격 표시를 켜도 기존 시나리오가 그대로 보인다(무회귀).
/// </summary>
public class SkeletonFocusController : MonoBehaviour
{
    [Serializable]
    public class FocusEntry
    {
        [Tooltip("시나리오 이름 (ScenarioConfig.scenarioName과 같은 값)")]
        public string scenarioName;

        [Tooltip("선택 — 이 국면에서만. 비우면 시나리오 전체.  CSV의 phase 열: 평가 / 교정 / 재평가")]
        public string phaseName;

        [Tooltip("선택 — 이 단계에서만. 비우면 국면 전체.  CSV의 stepName 열: 진단 / 파지 / 견착 / 호흡 …")]
        public string stepName;

        [Tooltip("선택 — 진단처럼 한 단계 안에서 자세를 번갈아 잡는 경우, 그 자세에서만 적용.\n" +
                 "0 = 자세 무관(기본) / 1 = 첫 번째 자세 / 2 = 두 번째 자세 …\n" +
                 "예) PJ 진단은 좌·우를 번갈아 잡으므로 1=좌측 뼈, 2=우측 뼈로 나눌 수 있다.")]
        public int poseNo = 0;

        [Tooltip("이 단계에서 ★보일 뼈. 씬에서 드래그해 넣는다. 자식까지 같이 보인다.\n" +
                 "여기 없는 뼈는 숨긴다(숨김 범위 = 전체 줄에 배정된 뼈들의 공통 부모).\n" +
                 "★비워 두면 이 줄은 '설정 안 함'이 되어 더 넓은 줄(국면 → 시나리오)을 따른다. " +
                 "그것도 없으면 골격 전체가 보인다. 즉 필요한 줄만 채우면 된다.")]
        public List<Transform> showBones = new List<Transform>();

        [Tooltip("켜면 이 단계에서 골격을 ★전부 숨긴다(보일 뼈가 하나도 없는 단계용).\n" +
                 "비어 있는 줄과 구분하기 위한 칸이다 — 빈 줄은 '설정 안 함'이라 상위 줄을 따른다.")]
        public bool hideAllBones = false;

        /// <summary>이 줄이 실제로 무언가를 지시하는가(비어 있고 전부숨김도 아니면 무시).</summary>
        public bool HasRule => hideAllBones || (showBones != null && showBones.Count > 0);

        /// <summary>구체적일수록 우선 — 자세 지정 > 단계 지정 > 국면 지정 > 시나리오만.</summary>
        public int Specificity =>
            (poseNo > 0 ? 4 : 0) +
            (!string.IsNullOrWhiteSpace(stepName) ? 2 : 0) +
            (!string.IsNullOrWhiteSpace(phaseName) ? 1 : 0);

        public string Describe() =>
            scenarioName +
            (string.IsNullOrWhiteSpace(phaseName) ? "" : " / " + phaseName.Trim()) +
            (string.IsNullOrWhiteSpace(stepName) ? "" : " / " + stepName.Trim());
    }

    [Header("=== 시나리오·단계별 표시 ===")]
    [SerializeField] private List<FocusEntry> entries = new List<FocusEntry>();

    [Header("=== 숨기는 방식 ===")]
    [Tooltip("켜면(기본) 렌더러만 꺼서 보이지 않게 한다 — 오브젝트·콜라이더는 살아 있어 xray 등과 간섭이 없다.\n" +
             "끄면 GameObject를 통째로 비활성화한다.")]
    [SerializeField] private bool hideByRendererOnly = true;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = true;

    /// <summary>이 컴포넌트가 끈 것만 기억한다 — 복원할 때 원래 꺼져 있던 것을 켜지 않기 위해.</summary>
    private readonly List<Renderer> disabledRenderers = new List<Renderer>();
    private readonly List<GameObject> turnedOff = new List<GameObject>();

    /// <summary>지금 적용 중인 줄. substep마다 호출되므로 같은 줄이면 계층을 다시 훑지 않는다.</summary>
    private FocusEntry appliedEntry;
    private Transform scope;
    private bool scopeResolved;

    /// <summary>시나리오 진입 시(국면·단계 없이) 호출.</summary>
    public void ApplyScenario(string scenarioName) => ApplyStep(scenarioName, null, null);

    /// <summary>substep 진입마다 호출. 맞는 줄이 없으면 숨겼던 것을 되돌리고 아무것도 하지 않는다.</summary>
    public void ApplyStep(string scenarioName, string phaseName, string stepName)
    {
        lastScenario = scenarioName;
        lastPhase = phaseName;
        lastStep = stepName;
        lastPose = 0;                     // 단계가 바뀌면 자세는 처음부터
        Apply(FindBest(scenarioName, phaseName, stepName, 0));
    }

    /// <summary>
    /// ★진단처럼 한 단계 안에서 자세를 번갈아 잡는 경우, 자세가 바뀔 때 호출한다(1부터).
    /// 자세용 줄이 없으면 단계·국면 줄이 그대로 유지된다 — 즉 안 나눠도 그만이다.
    /// (PJ 진단·재평가가 좌·우를 번갈아 잡는다 — 파지점이 바뀌는 것과 골격을 같이 맞추기 위한 훅)
    /// </summary>
    public void SetPose(int poseNo)
    {
        if (poseNo == lastPose) return;
        lastPose = poseNo;
        Apply(FindBest(lastScenario, lastPhase, lastStep, poseNo));
    }

    private string lastScenario, lastPhase, lastStep;
    private int lastPose;

    private void Apply(FocusEntry entry)
    {
        string scenarioName = lastScenario, phaseName = lastPhase, stepName = lastStep;
        if (entry != null && entry == appliedEntry) return;
        appliedEntry = entry;

        RestoreAll();

        if (entry == null)
        {
            if (showDebugLogs)
                ChunaLogger.Log($"[SkeletonFocus] '{scenarioName}/{phaseName}/{stepName}' 지정 없음 — 골격 전체 표시");
            return;
        }

        Transform root = ResolveScope();
        if (root == null)
        {
            ChunaLogger.LogWarning("[SkeletonFocus] 표시할 뼈가 한 줄도 배정되지 않았습니다 — " +
                                   "각 줄의 Show Bones에 뼈를 드래그해 넣으세요.");
            return;
        }

        int shown = 0, hidden = 0;
        foreach (Transform child in root)
            Walk(child, entry, ref shown, ref hidden);

        if (showDebugLogs)
            ChunaLogger.Log($"<color=cyan>[SkeletonFocus] {entry.Describe()} → 표시 {shown} / 숨김 {hidden}</color>");
    }

    /// <summary>
    /// 트리를 내려가며 숨긴다.
    /// ★재귀인 이유: 통짜 <c>skull</c> 안에 분리된 두개골이 들어 있는 구조(08-11 실측)에서
    /// skull을 통째로 끄면 그 안의 후두골·측두골까지 같이 꺼지기 때문이다.
    ///   · 보일 뼈다              → 자식까지 그대로 둔다
    ///   · 자손 중에 보일 게 있다 → <b>이 오브젝트의 렌더러만</b> 끄고 자식으로 내려간다
    ///   · 아무것도 없다          → 이 가지를 통째로 숨긴다
    /// </summary>
    private void Walk(Transform node, FocusEntry entry, ref int shown, ref int hidden)
    {
        if (node == null) return;

        if (IsShown(node, entry)) { shown++; return; }

        if (HasShownDescendant(node, entry))
        {
            if (HideRenderersOn(node)) hidden++;
            foreach (Transform child in node)
                Walk(child, entry, ref shown, ref hidden);
            return;
        }

        if (HideBranch(node)) hidden++;
    }

    private static bool IsShown(Transform node, FocusEntry entry)
    {
        if (entry.hideAllBones || entry.showBones == null) return false;   // 전부 숨김이면 아무것도 안 남는다
        foreach (Transform t in entry.showBones)
            if (t == node) return true;
        return false;
    }

    private static bool HasShownDescendant(Transform node, FocusEntry entry)
    {
        foreach (Transform child in node)
        {
            if (IsShown(child, entry)) return true;
            if (HasShownDescendant(child, entry)) return true;
        }
        return false;
    }

    /// <summary>이 오브젝트에 직접 붙은 렌더러만 끈다(자식은 그대로).</summary>
    private bool HideRenderersOn(Transform node)
    {
        bool any = false;
        foreach (Renderer r in node.GetComponents<Renderer>())
        {
            if (r == null || !r.enabled) continue;
            r.enabled = false;
            disabledRenderers.Add(r);
            any = true;
        }
        return any;
    }

    /// <summary>이 가지를 통째로 숨긴다. 원래 꺼져 있던 것은 건드리지 않는다.</summary>
    private bool HideBranch(Transform node)
    {
        if (!hideByRendererOnly)
        {
            if (!node.gameObject.activeSelf) return false;
            node.gameObject.SetActive(false);
            turnedOff.Add(node.gameObject);
            return true;
        }

        bool any = false;
        foreach (Renderer r in node.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || !r.enabled) continue;
            r.enabled = false;
            disabledRenderers.Add(r);
            any = true;
        }
        return any;
    }

    /// <summary>이 컴포넌트가 끈 것만 되돌린다.</summary>
    public void RestoreAll()
    {
        foreach (Renderer r in disabledRenderers)
            if (r != null) r.enabled = true;
        disabledRenderers.Clear();

        foreach (GameObject go in turnedOff)
            if (go != null) go.SetActive(true);
        turnedOff.Clear();
    }

    /// <summary>시나리오·국면·단계에 맞는 것 중 가장 구체적인 줄. 없으면 null.</summary>
    private FocusEntry FindBest(string scenarioName, string phaseName, string stepName, int poseNo)
    {
        if (string.IsNullOrWhiteSpace(scenarioName)) return null;

        FocusEntry best = null;
        foreach (FocusEntry e in entries)
        {
            if (e == null || !e.HasRule) continue;   // ★비어 있는 줄은 무시 — 더 넓은 줄을 따른다
            if (!Same(e.scenarioName, scenarioName)) continue;
            if (!string.IsNullOrWhiteSpace(e.phaseName) && !Same(e.phaseName, phaseName)) continue;
            if (!string.IsNullOrWhiteSpace(e.stepName) && !Same(e.stepName, stepName)) continue;
            if (e.poseNo > 0 && e.poseNo != poseNo) continue;
            if (best == null || e.Specificity > best.Specificity) best = e;
        }
        return best;
    }

    /// <summary>숨김 범위 = <b>모든 줄에 배정된 뼈들의 공통 부모</b>. 배정이 하나도 없으면 null.
    /// 한 번만 계산한다(배정이 런타임에 바뀌지 않으므로).</summary>
    private Transform ResolveScope()
    {
        if (scopeResolved) return scope;
        scopeResolved = true;

        Transform result = null;
        foreach (FocusEntry e in entries)
        {
            if (e?.showBones == null) continue;
            foreach (Transform t in e.showBones)
            {
                if (t == null) continue;
                if (result == null) { result = t.parent != null ? t.parent : t; continue; }
                Transform a = result;
                while (a != null && !IsAncestorOf(a, t)) a = a.parent;
                result = a != null ? a : result;
            }
        }

        scope = result;
        if (showDebugLogs)
            ChunaLogger.Log($"[SkeletonFocus] 숨김 범위 = {(scope == null ? "(배정 없음)" : scope.name)}");
        return scope;
    }

    private static bool IsAncestorOf(Transform ancestor, Transform node)
    {
        for (Transform t = node; t != null; t = t.parent)
            if (t == ancestor) return true;
        return false;
    }

    private static bool Same(string a, string b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
        a.Trim().Equals(b.Trim(), StringComparison.OrdinalIgnoreCase);
}
