using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 화살표·하이라이트의 <b>주동수/보조수 역할 색</b>을 씬 전체에 채운다.
///
/// ★왜 색이 안 나왔나 (2026-08-18 실측)
///   354e057이 색 규약(HandRole.cs)과 colorRole 필드를 신설하면서 기본값을 <b>기존색유지</b>로 뒀다
///   (커밋 메모: "기본값이 전부 '기존색 유지'라 지금 씬은 아무것도 안 바뀐다"). 무회귀를 위한 선택이었지만
///   그 뒤로 값을 채우지 않아 대부분의 표시물이 여전히 옛 색이다.
///
/// ★역할 판정 근거 — 추측하지 않는다
///   ① 오브젝트 이름에 '주동수/보조수/환자'가 있으면 그대로 쓴다(복와위 화살표가 그렇다).
///   ② 두개골 OM·PJ는 CSV 지시문이 못박고 있다 —
///      "보조수 왼손은 후두골을 받치고, 주동수 오른손은 관골궁을 파지" → 왼손=보조수 / 오른손=주동수.
///      그래서 이 두 리그에 한해 leftGrips 아래=보조수, rightGrips 아래=주동수로 채운다.
///   ③ 복와위 두상골 표시(PisiformHighlight)는 화살표 배치로 확정된다 —
///      주동수(후방→전방)가 왼손(족방수) 파지점에, 보조수(두방→족방)가 오른손(두방수)에 붙어 있다.
///   ★그 밖에 근거가 없는 것은 <b>건드리지 않고 목록으로 보고</b>한다(제1늑골은 왼손이 주동수라
///     '왼손=보조수' 규칙을 전 술기에 적용하면 틀린다).
///
/// ★비파괴: 이미 역할이 지정된 것은 덮어쓰지 않는다. 전부 Undo로 되돌아간다.
/// </summary>
public static class HandRoleColorTool
{
    [MenuItem("GuideChuna/주동수·보조수 색 일괄 적용")]
    public static void Apply()
    {
        var log = new StringBuilder();
        log.AppendLine("[주동수·보조수 색 일괄 적용]");

        int changed = 0;
        var unresolved = new List<string>();

        // ── 화살표
        foreach (ForceArrowBase a in Resources.FindObjectsOfTypeAll<ForceArrowBase>())
        {
            if (a == null || !a.gameObject.scene.IsValid()) continue;
            if (ReadRole(a, "colorRole") != HandRole.Role.기존색유지) continue;   // 이미 지정됨 — 건드리지 않는다

            HandRole.Role r = FromName(a.name);
            if (r == HandRole.Role.기존색유지) r = FromCranialSide(a.transform);

            if (r == HandRole.Role.기존색유지)
            {
                unresolved.Add($"화살표  {a.name}  [{RigNameOf(a.transform)}]");
                continue;
            }

            if (WriteRole(a, "colorRole", r)) { changed++; log.AppendLine($"  ✓ {a.name} → {r}"); }
        }

        // ── 두상골 표시(손별 역할 2칸)
        foreach (PisiformHighlight p in Resources.FindObjectsOfTypeAll<PisiformHighlight>())
        {
            if (p == null || !p.gameObject.scene.IsValid()) continue;
            string rig = RigNameOf(p.transform);

            // 복와위는 화살표 배치로 확정된다(주동수=왼손 족방수 / 보조수=오른손 두방수).
            if (rig.Contains("복와위"))
            {
                bool a1 = WriteRole(p, "leftRole", HandRole.Role.주동수);
                bool a2 = WriteRole(p, "rightRole", HandRole.Role.보조수);
                if (a1 || a2) { changed++; log.AppendLine($"  ✓ {p.name} → 왼손=주동수 / 오른손=보조수 [{rig}]"); }
            }
            else if (ReadRole(p, "leftRole") == HandRole.Role.기존색유지)
                unresolved.Add($"두상골표시  {p.name}  [{rig}]");
        }

        // ── 타겟 부위 하이라이트 — 근거가 없어 자동 지정하지 않는다
        foreach (TargetAreaHighlight h in Resources.FindObjectsOfTypeAll<TargetAreaHighlight>())
        {
            if (h == null || !h.gameObject.scene.IsValid()) continue;
            if (ReadRole(h, "role") == HandRole.Role.기존색유지)
                unresolved.Add($"부위하이라이트  {h.name}  (인스펙터에서 role 지정 필요)");
        }

        log.AppendLine($"\n  적용 {changed}건");
        if (unresolved.Count > 0)
        {
            log.AppendLine($"\n  ★근거가 없어 건드리지 않은 것 {unresolved.Count}건 — 인스펙터에서 직접 지정할 것:");
            foreach (string u in unresolved) log.AppendLine("     · " + u);
        }
        log.AppendLine("\n씬 저장을 잊지 말 것. 되돌리려면 Ctrl+Z.");
        Debug.Log(log.ToString());
    }

    [MenuItem("GuideChuna/주동수·보조수 색 점검 (읽기 전용)")]
    public static void Audit()
    {
        var sb = new StringBuilder();
        sb.AppendLine("[주동수·보조수 색 점검]");
        int unset = 0;
        foreach (ForceArrowBase a in Resources.FindObjectsOfTypeAll<ForceArrowBase>())
        {
            if (a == null || !a.gameObject.scene.IsValid()) continue;
            HandRole.Role r = ReadRole(a, "colorRole");
            if (r == HandRole.Role.기존색유지) unset++;
            sb.AppendLine($"  {(r == HandRole.Role.기존색유지 ? "★미지정" : "  " + r)}  {a.name}  [{RigNameOf(a.transform)}]");
        }
        sb.AppendLine($"\n  미지정 {unset}개");
        Debug.Log(sb.ToString());
    }

    // ─────────────────────────── 판정 ───────────────────────────

    private static HandRole.Role FromName(string name)
    {
        if (string.IsNullOrEmpty(name)) return HandRole.Role.기존색유지;
        if (name.Contains("환자")) return HandRole.Role.환자;
        if (name.Contains("주동수")) return HandRole.Role.주동수;
        if (name.Contains("보조수")) return HandRole.Role.보조수;
        return HandRole.Role.기존색유지;
    }

    /// <summary>두개골 OM·PJ 한정 — CSV가 "보조수 왼손 / 주동수 오른손"으로 못박은 두 리그에서만
    /// 파지점 소속으로 역할을 정한다. (제1늑골은 왼손이 주동수라 이 규칙을 쓰면 안 된다.)</summary>
    private static HandRole.Role FromCranialSide(Transform t)
    {
        var rig = t.GetComponentInParent<CranialAdjustmentController>(true);
        if (rig == null) return HandRole.Role.기존색유지;

        string n = rig.name;
        bool isOmOrPj = n.Contains("OM") || n.Contains("PJ");
        if (!isOmOrPj) return HandRole.Role.기존색유지;

        if (IsUnder(t, GripTransforms(rig, "leftGrips"))) return HandRole.Role.보조수;
        if (IsUnder(t, GripTransforms(rig, "rightGrips"))) return HandRole.Role.주동수;
        return HandRole.Role.기존색유지;
    }

    private static List<Transform> GripTransforms(CranialAdjustmentController rig, string arrayName)
    {
        var list = new List<Transform>();
        SerializedProperty arr = new SerializedObject(rig).FindProperty(arrayName);
        if (arr == null || !arr.isArray) return list;
        for (int i = 0; i < arr.arraySize; i++)
        {
            var g = arr.GetArrayElementAtIndex(i).objectReferenceValue as GripPointTarget;
            if (g != null) list.Add(g.transform);
        }
        return list;
    }

    private static bool IsUnder(Transform t, List<Transform> roots)
    {
        for (Transform p = t; p != null; p = p.parent)
            for (int i = 0; i < roots.Count; i++)
                if (roots[i] == p) return true;
        return false;
    }

    private static string RigNameOf(Transform t)
    {
        var rig = t.GetComponentInParent<CranialAdjustmentController>(true);
        return rig != null ? rig.name : "(리그 밖)";
    }

    // ─────────────────────────── 직렬화 ───────────────────────────

    private static HandRole.Role ReadRole(Object o, string field)
    {
        SerializedProperty p = new SerializedObject(o).FindProperty(field);
        return p == null ? HandRole.Role.기존색유지 : (HandRole.Role)p.enumValueIndex;
    }

    private static bool WriteRole(Object o, string field, HandRole.Role r)
    {
        var so = new SerializedObject(o);
        SerializedProperty p = so.FindProperty(field);
        if (p == null || p.enumValueIndex == (int)r) return false;
        p.enumValueIndex = (int)r;
        so.ApplyModifiedProperties();
        if (o is Component c)
        {
            EditorUtility.SetDirty(c);
            if (PrefabUtility.IsPartOfPrefabInstance(c))
                PrefabUtility.RecordPrefabInstancePropertyModifications(c);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
        }
        return true;
    }
}
