using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 골격 부위 조사 + 시나리오별 "필요 골격만 표시" 초기 설정.
/// ★비파괴 — 오브젝트를 지우거나 재생성하지 않고, 이미 있는 항목은 건너뛴다.
/// </summary>
public static class SkeletonFocusSetupTool
{
    /// <summary>시나리오별 남길 부위 기본값. 해부 모델이 <b>부위 단위</b>라 뼈 단위 지정은 불가능하다.</summary>
    private static readonly (string scenario, string[] keep)[] Defaults =
    {
        ("두개골교정",            new[] { "skull", "cervical_spine" }),
        ("두개골PM교정",          new[] { "skull", "cervical_spine" }),
        ("두개골PJ교정",          new[] { "skull", "cervical_spine" }),
        ("앙와위_흉추_신전변위",  new[] { "thoracic_spine" }),
    };

    [MenuItem("GuideChuna/골격 부위 목록 보기")]
    private static void ListParts()
    {
        var roots = FindRoots();
        var sb = new StringBuilder();
        sb.AppendLine($"골격 루트 {roots.Count}개");

        foreach (Transform root in roots)
        {
            sb.AppendLine($"\n■ {Path(root)}  (자식 {root.childCount}개)");
            var names = new List<string>();
            foreach (Transform c in root) names.Add(c.name + (c.gameObject.activeSelf ? "" : " [꺼짐]"));
            names.Sort();
            foreach (string n in names) sb.AppendLine("   · " + n);
        }

        if (roots.Count == 0)
            sb.AppendLine("\n'skeletal_system' 오브젝트를 못 찾았습니다. " +
                          "해부 모델이 씬에 있는지 확인하고, SkeletonFocusController의 Skeleton Roots에 직접 넣으세요.");

        Debug.Log("[골격 부위 목록]\n" + sb);
    }

    [MenuItem("GuideChuna/골격 포커스 기본 설정 채우기")]
    private static void FillDefaults()
    {
        var focus = Object.FindFirstObjectByType<SkeletonFocusController>(FindObjectsInactive.Include);
        if (focus == null)
        {
            EditorUtility.DisplayDialog("골격 포커스",
                "씬에 SkeletonFocusController가 없습니다.\n\n" +
                "빈 GameObject를 만들어 컴포넌트를 추가한 뒤 다시 실행하세요.", "확인");
            return;
        }

        var so = new SerializedObject(focus);
        SerializedProperty entries = so.FindProperty("entries");

        var existing = new HashSet<string>();
        for (int i = 0; i < entries.arraySize; i++)
        {
            string s = entries.GetArrayElementAtIndex(i).FindPropertyRelative("scenarioName").stringValue;
            if (!string.IsNullOrWhiteSpace(s)) existing.Add(s.Trim());
        }

        int added = 0;
        foreach ((string scenario, string[] keep) in Defaults)
        {
            if (existing.Contains(scenario)) continue;   // 이미 있으면 손대지 않는다

            entries.InsertArrayElementAtIndex(entries.arraySize);
            SerializedProperty e = entries.GetArrayElementAtIndex(entries.arraySize - 1);
            e.FindPropertyRelative("scenarioName").stringValue = scenario;

            SerializedProperty parts = e.FindPropertyRelative("keepParts");
            parts.ClearArray();
            for (int i = 0; i < keep.Length; i++)
            {
                parts.InsertArrayElementAtIndex(i);
                parts.GetArrayElementAtIndex(i).stringValue = keep[i];
            }
            added++;
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(focus);

        Debug.Log($"[골격 포커스] 항목 {added}개 추가 (이미 있던 것은 건너뜀). " +
                  "부위 이름이 맞는지는 'GuideChuna/골격 부위 목록 보기'로 확인하세요.");
    }

    private static List<Transform> FindRoots()
    {
        var roots = new List<Transform>();
        foreach (Transform t in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (t == null || !t.gameObject.scene.IsValid()) continue;
            if (t.name.Equals("skeletal_system", System.StringComparison.OrdinalIgnoreCase))
                roots.Add(t);
        }
        return roots;
    }

    private static string Path(Transform t)
    {
        string p = t.name;
        while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }
}
