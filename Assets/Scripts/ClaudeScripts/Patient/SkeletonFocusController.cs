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
    [Header("=== 두개골 자동 숨김 (술기 무관 시) ===")]
    [Tooltip("두개골 술기가 아닌 시나리오(늑골·흉추·근육)에서 ★두개골만 자동으로 숨긴다(기본 켬).\n" +
             "리듬(CRI) 표시를 시나리오 이름으로 자동 OFF 하는 것과 같은 규약이다 — 줄을 채울 필요가 없다.\n" +
             "★골격이 거슬린다고 오브젝트를 통째로 비활성화하면 안 된다: 이 컴포넌트는 <b>렌더러를 껐다 켜는</b> " +
             "방식이고 자기가 끈 것만 되돌리므로, 꺼 둔 오브젝트는 다시 켜 주지 않아 다른 시나리오에서도 " +
             "골격이 영영 안 보이게 된다(2026-08-13 실사용).")]
    [SerializeField] private bool hideSkullOutsideCranial = true;

    [Tooltip("숨길 두개골 오브젝트. 비우면 부위 이름으로 자동으로 찾는다.\n" +
             "★'skull'로 찾으면 안 된다 — 분리 두개골은 skeletal_system 바로 밑의 개별 뼈이고, " +
             "이름에 skull이 든 것은 안 쓰는 통짜 구버전(skull_Old)뿐이라 그것만 꺼져서 화면은 그대로였다(08-13).")]
    [SerializeField] private List<Transform> skullRoots = new List<Transform>();

    /// <summary>★사용자가 분리 두개골을 묶어 둔 오브젝트 이름(08-13). 이게 있으면 이것만 숨긴다.</summary>
    private const string SkullGroupName = "두개골 분할";

    /// <summary>두개골을 이루는 부위 이름(부분 일치, 대소문자 무시). 위 그룹이 없을 때만 쓰는 폴백.
    /// 모델 이름이 '한글(부위)_영문'이라 영문 쪽으로 잡는다.
    /// ★설골(hyoid)·경추(cervical)는 두개골이 아니므로 넣지 않는다.</summary>
    private static readonly string[] SkullPartKeywords =
    {
        "frontal bone", "occipital bone", "parietal bone", "temporal bone", "sphenoid bone",
        "zygomatic bone", "maxilla", "jaw", "nasal bone", "upper teeth", "lower teeth",
        "skull",          // 통짜 구버전(skull_Old)도 같이 숨긴다 — 켜져 있으면 어차피 두개골이다
        "두개골 분할"      // 사용자가 따로 묶어 둔 경우
    };

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
    private readonly List<Transform> scopes = new List<Transform>();
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
        // ★반드시 Apply <b>뒤</b>에 — Apply 안의 RestoreAll이 줄 때문에 껐던 렌더러를 되살리므로,
        //   먼저 숨기면 그 프레임에 두개골이 다시 켜진다.
        ApplySkullVisibility(scenarioName);
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

        FocusEntry e = FindBest(lastScenario, lastPhase, lastStep, poseNo);
        if (showDebugLogs)
            ChunaLogger.Log($"<color=cyan>[SkeletonFocus] 자세 {poseNo} → " +
                            $"{(e == null ? "★맞는 줄 없음(전체 표시)" : e.Describe() + $" 뼈 {(e.showBones == null ? 0 : e.showBones.Count)}개")}" +
                            $"  (시나리오='{lastScenario}' 국면='{lastPhase}' 단계='{lastStep}')</color>");
        Apply(e);
        ApplySkullVisibility(lastScenario);   // Apply 뒤 — ApplyStep과 같은 이유
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

        List<Transform> roots = ResolveScopes();
        if (roots.Count == 0)
        {
            ChunaLogger.LogWarning("[SkeletonFocus] 표시할 뼈가 한 줄도 배정되지 않았습니다 — " +
                                   "각 줄의 Show Bones에 뼈를 드래그해 넣으세요.");
            return;
        }

        int shown = 0, hidden = 0;
        foreach (Transform root in roots)
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

    // === 두개골 자동 숨김 (술기 무관 시) ===

    /// <summary>두개골 때문에 끈 렌더러. 줄 단위 복원(disabledRenderers)과 <b>따로</b> 관리한다 —
    /// 섞으면 단계가 바뀔 때마다 두개골이 되살아났다 다시 꺼지며 깜빡인다.</summary>
    private readonly List<Renderer> skullDisabled = new List<Renderer>();
    private bool skullHidden;
    private bool skullRootsResolved;

    /// <summary>
    /// 두개골 술기가 아닌 시나리오에서 두개골만 숨긴다.
    /// ★문제 상황(08-13): 늑골·흉추 실습 중에도 두개골이 계속 떠 있었다. 줄로 일일이 막는 대신
    /// 시나리오 이름으로 판단한다 — 리듬(CRI) 표시를 두개골 전용으로 자동 OFF 하는 것과 같은 규약.
    /// </summary>
    private void ApplySkullVisibility(string scenarioName)
    {
        bool wantHidden = hideSkullOutsideCranial &&
                          !string.IsNullOrEmpty(scenarioName) &&
                          scenarioName.IndexOf("두개골", StringComparison.Ordinal) < 0;

        if (!wantHidden)
        {
            if (!skullHidden) return;
            skullHidden = false;
            foreach (Renderer r in skullDisabled)
                if (r != null) r.enabled = true;
            skullDisabled.Clear();
            if (showDebugLogs) ChunaLogger.Log($"[SkeletonFocus] '{scenarioName}' — 두개골 표시 복원");
            return;
        }

        // ★상태가 그대로여도 매번 다시 훑는다 — 직전 단계에서 줄 때문에 꺼졌던 두개골 렌더러를
        //   Apply의 RestoreAll이 되살려 놓았을 수 있다(그때 그 렌더러는 우리 목록에 없다).
        int added = 0;
        foreach (Transform root in ResolveSkullRoots())
        {
            if (root == null) continue;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled) continue;   // 이미 꺼진 것(원래 꺼둔 것 포함)은 건드리지 않는다
                r.enabled = false;
                skullDisabled.Add(r);
                added++;
            }
        }

        bool first = !skullHidden;
        skullHidden = true;
        if (showDebugLogs && (first || added > 0))
            ChunaLogger.Log($"<color=cyan>[SkeletonFocus] '{scenarioName}'은 두개골 술기가 아니므로 " +
                            $"두개골 렌더러 {added}개를 숨겼습니다(누적 {skullDisabled.Count}).</color>");
    }

    /// <summary>두개골 루트 목록. 비어 있으면 이름에 'skull'이 든 오브젝트를 1회 탐색해 채운다.</summary>
    private List<Transform> ResolveSkullRoots()
    {
        if (skullRootsResolved) return skullRoots;
        skullRootsResolved = true;

        skullRoots.RemoveAll(t => t == null);
        if (skullRoots.Count > 0) return skullRoots;

        // 자동 탐색: 두개골 부위 이름으로 찾는다(분리 두개골은 skeletal_system 바로 밑의 개별 뼈다).
        // 안쪽 것은 담지 않는다 — 이미 담은 오브젝트의 자손이면 부모를 끌 때 같이 꺼진다.
        // 줄에 뼈가 하나도 배정 안 됐으면 범위를 못 구하므로 씬 전체에서 찾는다(1회).
        List<Transform> searchRoots = ResolveScopes();
        if (searchRoots.Count == 0)
        {
            searchRoots = new List<Transform>();
            foreach (Transform t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t != null && t.parent == null) searchRoots.Add(t);
        }

        // ★1순위: 사용자가 분리 두개골을 묶어 둔 오브젝트('두개골 분할'). 있으면 그것만 쓴다 —
        //   부위 이름 훑기는 다른 계층의 턱·치아까지 집을 수 있어 정확도가 떨어진다.
        foreach (Transform scope in searchRoots)
        {
            if (scope == null) continue;
            foreach (Transform t in scope.GetComponentsInChildren<Transform>(true))
                if (t.name.IndexOf(SkullGroupName, StringComparison.OrdinalIgnoreCase) >= 0 &&
                    !skullRoots.Contains(t))
                    skullRoots.Add(t);
        }
        if (skullRoots.Count > 0)
        {
            if (showDebugLogs)
                ChunaLogger.Log($"[SkeletonFocus] 두개골 그룹 '{SkullGroupName}' {skullRoots.Count}개를 찾았습니다.");
            return skullRoots;
        }

        // 2순위: 부위 이름으로 개별 뼈를 모은다(그룹으로 안 묶여 있는 프로젝트 상태 대비).
        foreach (Transform scope in searchRoots)
        {
            if (scope == null) continue;
            foreach (Transform t in scope.GetComponentsInChildren<Transform>(true))
            {
                if (!IsSkullPart(t.name)) continue;
                bool insideAlready = false;
                foreach (Transform have in skullRoots)
                    if (have != null && t.IsChildOf(have)) { insideAlready = true; break; }
                if (!insideAlready) skullRoots.Add(t);
            }
        }

        if (skullRoots.Count == 0)
            ChunaLogger.LogWarning("[SkeletonFocus] 두개골을 찾지 못했습니다 — " +
                                   "Skull Roots에 두개골 오브젝트를 직접 넣으세요(부위 이름이 모델과 다르면 자동 탐색이 안 됩니다).");
        else if (showDebugLogs)
        {
            string names = "";
            foreach (Transform t in skullRoots) names += Describe(t) + "  ";
            ChunaLogger.Log($"[SkeletonFocus] 두개골 루트 {skullRoots.Count}개 = {names}");
        }
        return skullRoots;
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

    /// <summary>
    /// 시나리오·국면·단계에 맞는 것 중 가장 구체적인 줄. 없으면 null.
    ///
    /// ★2026-08-12 수정 — 자세(poseNo)가 안 맞으면 <b>자세를 무시하고 다시 찾는다</b>.
    /// 예전에는 못 찾으면 그대로 null을 돌려줬고, 그러면 Apply가 <c>RestoreAll()</c>로
    /// <b>골격 전체를 도로 켰다</b>. PJ처럼 줄이 poseNo 1·2로만 있는 국면은
    /// 단계 진입 시점(poseNo=0)에 매칭이 없어서 <b>늑골·흉추가 계속 보였다</b>(사용자 보고).
    /// 자세별 줄만 있으면 그중 첫 자세 줄을 기본으로 쓴다 — 전체 표시로 튀는 것보다 훨씬 낫다.
    /// </summary>
    private FocusEntry FindBest(string scenarioName, string phaseName, string stepName, int poseNo)
    {
        if (string.IsNullOrWhiteSpace(scenarioName)) return null;

        FocusEntry best = Search(scenarioName, phaseName, stepName, poseNo, ignorePose: false);
        if (best == null)
            best = Search(scenarioName, phaseName, stepName, poseNo, ignorePose: true);

        // ★국면 줄이 없으면 <b>그 시나리오의 줄</b>을 그대로 쓴다.
        //   늑골·흉추는 <b>처음 할당한 골격이 전 과정에서 계속 보이면 되는</b> 술기라
        //   국면별로 뼈를 나눌 필요가 없다 — 줄 하나만 만들어 두는 것이 정상 배선이다.
        //   (두개골은 진단·교정·재평가에서 보여야 할 뼈가 달라 국면 줄을 나눠 쓴다.)
        //   예전에는 여기서 null을 돌려줘 '골격 전체 표시'로 빠졌고, 그 탓에 흉추 실습인데
        //   늑골·두개골 뼈까지 전부 켜졌다(2026-08-12).
        if (best == null)
            best = FirstOfScenario(scenarioName);

        return best;
    }

    /// <summary>국면·단계를 따지지 않고 그 시나리오의 첫 줄을 돌려준다(최후의 폴백).</summary>
    private FocusEntry FirstOfScenario(string scenarioName)
    {
        foreach (FocusEntry e in entries)
            if (e != null && e.HasRule && Same(e.scenarioName, scenarioName))
                return e;
        return null;
    }

    private FocusEntry Search(string scenarioName, string phaseName, string stepName, int poseNo, bool ignorePose)
    {
        FocusEntry best = null;
        int tied = 0;
        foreach (FocusEntry e in entries)
        {
            if (e == null || !e.HasRule) continue;   // ★비어 있는 줄은 무시 — 더 넓은 줄을 따른다
            if (!Same(e.scenarioName, scenarioName)) continue;
            if (!string.IsNullOrWhiteSpace(e.phaseName) && !Same(e.phaseName, phaseName)) continue;
            if (!string.IsNullOrWhiteSpace(e.stepName) && !Same(e.stepName, stepName)) continue;
            if (!ignorePose && e.poseNo > 0 && e.poseNo != poseNo) continue;

            if (best == null) { best = e; continue; }
            if (e.Specificity > best.Specificity) { best = e; tied = 0; continue; }
            if (e.Specificity == best.Specificity)
            {
                // 같은 구체성인데 자세를 무시하고 고르는 중이면 낮은 자세 번호(=첫 자세)를 쓴다.
                if (ignorePose && e.poseNo < best.poseNo) { best = e; continue; }
                // ★자세 번호까지 같은 줄이 둘 이상이면 앞의 것이 조용히 이긴다 —
                //   PJ 진단이 좌·우 두 줄을 모두 poseNo 1로 두는 바람에 좌측 측두골·관골이
                //   영영 안 나왔다(2026-08-12). 다시는 조용히 묻히지 않게 경고한다.
                if (!ignorePose && e.poseNo == best.poseNo) tied++;
            }
        }

        if (tied > 0 && !warnedTie)
        {
            warnedTie = true;
            ChunaLogger.LogWarning(
                $"[SkeletonFocus] '{scenarioName}/{phaseName}/{stepName}' 자세 {poseNo}에 같은 조건의 줄이 " +
                $"{tied + 1}개 있습니다 — 앞의 줄만 적용되고 나머지는 무시됩니다.\n" +
                "   메뉴 'GuideChuna/골격 포커스 — 중복 자세 번호 정리'로 번호를 매겨 주세요.");
        }
        return best;
    }

    private bool warnedTie;

    /// <summary>숨김 범위 = <b>모든 줄에 배정된 뼈들의 공통 부모</b>. 배정이 하나도 없으면 null.
    /// 한 번만 계산한다(배정이 런타임에 바뀌지 않으므로).</summary>
    /// <summary>
    /// 숨김 범위 = 배정된 뼈들이 속한 <b>골격 루트(skeletal_system)들</b>.
    ///
    /// ★2026-08-12 버그 수정 — 예전에는 배정된 뼈 전체의 '공통 부모' 하나를 범위로 삼았다.
    /// 그런데 이 씬의 골격은 <b>두 계층으로 나뉘어 있다</b>:
    ///     두개골  : c9/c8/…/CC_Base_Head/skeletal_system/뒤통수뼈…
    ///     흉추·늑골: c9/근육골격/skeletal_system/thoracic_spine…
    /// 그래서 공통 부모가 <b>c9 — 환자 모델 통째</b>가 되어, '보일 뼈가 없는 가지'로 판정된
    /// <b>환자 메시까지 전부 렌더러가 꺼졌다</b>(PM·PJ 실행 중 환자가 사라진 원인).
    ///
    /// 이제는 뼈마다 자기가 속한 골격 루트를 찾아 그 안에서만 숨긴다 →
    /// 골격 밖(환자 피부·옷·눈)은 어떤 경우에도 건드리지 않는다.
    /// </summary>
    private List<Transform> ResolveScopes()
    {
        if (scopeResolved) return scopes;
        scopeResolved = true;
        scopes.Clear();

        foreach (FocusEntry e in entries)
        {
            if (e?.showBones == null) continue;
            foreach (Transform t in e.showBones)
            {
                if (t == null) continue;
                Transform root = SkeletonRootOf(t);
                if (root != null && !scopes.Contains(root)) scopes.Add(root);
            }
        }

        if (showDebugLogs)
        {
            string names = scopes.Count == 0 ? "(배정 없음)" : "";
            foreach (Transform s in scopes) names += Describe(s) + "  ";
            ChunaLogger.Log($"[SkeletonFocus] 숨김 범위 {scopes.Count}개 = {names}");
        }
        return scopes;
    }

    /// <summary>이 이름이 두개골 부위인가(부분 일치).</summary>
    private static bool IsSkullPart(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        foreach (string k in SkullPartKeywords)
            if (name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    /// <summary>이 뼈가 속한 골격 루트 — 가장 바깥쪽 skeletal_system. 없으면 바로 위 부모.</summary>
    private static Transform SkeletonRootOf(Transform bone)
    {
        Transform found = null;
        for (Transform t = bone; t != null; t = t.parent)
            if (t.name.IndexOf("skeletal_system", StringComparison.OrdinalIgnoreCase) >= 0)
                found = t;                       // 계속 올라가며 갱신 → 최종적으로 가장 바깥 것
        return found != null ? found : bone.parent;
    }

    private static string Describe(Transform t) =>
        t == null ? "(없음)" : (t.parent != null ? t.parent.name + "/" + t.name : t.name);

    private static bool Same(string a, string b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
        a.Trim().Equals(b.Trim(), StringComparison.OrdinalIgnoreCase);
}
