using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>안내판 글꼴 묶음(<see cref="SideGuideFonts"/>) 자산을 만든다 — 여러 번 눌러도 같은 자산의 참조만 다시 채운다(비파괴).</summary>
public static class SideGuideFontsTool
{
    const string Path = "Assets/Resources/UI/SideGuideFonts.asset";

    [MenuItem("GuideChuna/UI 개편/안내판 글꼴 묶음 만들기 (KoPub 세 굵기 참조)")]
    public static void Make()
    {
        if (!KoPubFonts.Ready) { Debug.LogWarning("[안내판글꼴] KoPub 글꼴 자산이 없다 — «KoPubWorld돋움 글꼴 자산 만들기» 먼저"); return; }
        Directory.CreateDirectory("Assets/Resources/UI");
        var a = AssetDatabase.LoadAssetAtPath<SideGuideFonts>(Path);
        bool made = a == null;
        if (made) { a = ScriptableObject.CreateInstance<SideGuideFonts>(); AssetDatabase.CreateAsset(a, Path); }
        a.bold = KoPubFonts.Bold; a.medium = KoPubFonts.Medium; a.light = KoPubFonts.Light;
        EditorUtility.SetDirty(a); AssetDatabase.SaveAssets();
        Debug.Log($"[안내판글꼴] {(made ? "만듦" : "다시 채움")} {Path} · {a.bold.name} / {a.medium.name} / {a.light.name}");
    }
}
