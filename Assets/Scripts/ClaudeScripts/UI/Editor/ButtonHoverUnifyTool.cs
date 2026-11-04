using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 버튼 가리킴 효과 통일 = 밝아짐 (2026-10-30 사용자 «어떤 건 어두워지고 어떤 건 밝아진다 — 다 밝아지게 · 실습 모드 선택이 어두워져 혼자 이질감»).
/// 색 틴트(ColorTint) 버튼 중 가리킴이 평소보다 어두운 것만 고친다 — 색 틴트는 «바탕색 × 틴트»라 밝게 하려면 평소 틴트가 1보다 작아야 한다.
///   평소 <see cref="Rest"/> · 가리킴 1 · 누름 <see cref="Press"/> · 바탕색은 1/Rest배로 올려 평소 겉보기 색은 그대로 둔다(1을 넘는 성분은 1에서 멈춘다 — 흰 버튼은 더 밝아질 수 없다).
/// UISet 애니메이터 버튼(가리킴 #4B → #5D)과 로비 X(#5D)는 이미 밝아지므로 건드리지 않는다. 여러 번 돌려도 같은 결과(이미 밝아지는 버튼은 건너뛴다).
/// </summary>
public static class ButtonHoverUnifyTool
{
    const float Rest = 0.8f, Press = 0.65f;

    [MenuItem("GuideChuna/UI 개편/10-30 버튼 가리킴 = 밝아짐 통일 (열린 씬)")]
    public static void Apply()
    {
        var sb = new StringBuilder("[버튼가리킴] 밝아짐 통일\n");
        int fixedN = 0, skipped = 0;
        foreach (var s in Object.FindObjectsByType<Selectable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (s.transition != Selectable.Transition.ColorTint) continue;
            var c = s.colors;
            if (Lum(c.highlightedColor) >= Lum(c.normalColor) - 1e-3f) { skipped++; continue; }   // 이미 밝아짐(또는 같음)
            var g = s.targetGraphic;
            if (g != null)
            {
                Undo.RecordObject(g, "가리킴 통일");
                var bc = g.color;
                g.color = new Color(Mathf.Min(1f, bc.r / Rest), Mathf.Min(1f, bc.g / Rest), Mathf.Min(1f, bc.b / Rest), bc.a);
                PrefabUtility.RecordPrefabInstancePropertyModifications(g);
            }
            var so = new SerializedObject(s);
            so.FindProperty("m_Colors.m_NormalColor").colorValue = new Color(Rest, Rest, Rest, 1f);
            so.FindProperty("m_Colors.m_SelectedColor").colorValue = new Color(Rest, Rest, Rest, 1f);
            so.FindProperty("m_Colors.m_HighlightedColor").colorValue = Color.white;
            so.FindProperty("m_Colors.m_PressedColor").colorValue = new Color(Press, Press, Press, 1f);
            so.FindProperty("m_Colors.m_ColorMultiplier").floatValue = 1f;
            so.ApplyModifiedProperties();
            PrefabUtility.RecordPrefabInstancePropertyModifications(s);
            fixedN++;
            if (fixedN <= 40) sb.Append($"  {Path(s.transform)}\n");
        }
        if (fixedN > 0) EditorSceneManager.MarkAllScenesDirty();
        Debug.Log(sb.Append($"  → 고침 {fixedN}개 · 이미 밝아짐 {skipped}개 · ★저장은 Ctrl+S").ToString());
    }

    static float Lum(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

    static string Path(Transform t)
    {
        string p = t.name;
        for (int i = 0; i < 3 && t.parent != null; i++) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }
}
