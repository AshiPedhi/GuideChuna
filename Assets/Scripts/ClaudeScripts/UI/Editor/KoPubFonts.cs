using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// KoPubWorld돋움 Bold · Medium · Light (2026-10-28 사용자 «이 세 개로 각 텍스트 영역에 맞게»).
///
/// [글꼴 자산] ★정적(Static) 아틀라스다 — 동적 자산은 글자가 들어올 때마다 asset이 바뀌어 LFS가 터진다(NotoSansKR-Bold 134MB · C 드라이브 사고).
///   글자 = charset.txt(ASCII · KS X 1001 한글 2,350 · 호환 자모 · 기호 · 로비 씬 글자). 없는 글자는 NotoSansKR-Bold로 넘어간다(대체 글꼴).
/// [역할] Meta 타이포 규칙(제목 Strong = 굵게 · 본문 보통 · 보조 정보 흐리게)을 세 굵기로 옮긴다:
///   Bold   = 제목 — 로비 머리 제목 · 20 이상(구역 제목 24 · 대화상자 제목 20 ~)
///   Light  = 보조 설명 — 회색 글자(#A3A3A3 계열: 카드 정보줄 · 설명 · 안내)
///   Medium = 나머지 라벨 — 카드 제목 · 버튼 · 탭 · 조 · 이름 · 드롭다운
///   ★굵게(B) 표시는 지운다 — 진짜 굵기를 쓰므로 가짜 굵게가 겹치면 뭉개진다.
///   ★새 글꼴에 없는 글자가 든 글자칸(◐ · ✕ 같은 기호 아이콘)은 건드리지 않는다.
/// </summary>
public static class KoPubFonts
{
    public const string Dir = "Assets/Fonts/KoPubWorld";
    static readonly string[] Weights = { "Bold", "Medium", "Light" };

    static TMP_FontAsset bold, medium, light;
    public static TMP_FontAsset Bold => bold != null ? bold : bold = Load("Bold");
    public static TMP_FontAsset Medium => medium != null ? medium : medium = Load("Medium");
    public static TMP_FontAsset Light => light != null ? light : light = Load("Light");
    public static bool Ready => Bold != null && Medium != null && Light != null;

    static string AssetPath(string w) => $"{Dir}/KoPubWorld Dotum {w} SDF.asset";
    static TMP_FontAsset Load(string w) => AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath(w));

    [MenuItem("GuideChuna/UI 개편/KoPubWorld돋움 글꼴 자산 만들기 (정적)")]
    public static void Create()
    {
        string chars = File.ReadAllText($"{Dir}/charset.txt");
        var fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(HzUIKit.KoreanFontGuid));
        foreach (var w in Weights)
        {
            var ttf = AssetDatabase.LoadAssetAtPath<Font>($"{Dir}/KoPubWorld Dotum {w}.ttf");
            if (ttf == null) { Debug.LogWarning($"[KoPub] {w} ttf 없음"); continue; }
            string path = AssetPath(w);
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path) != null) AssetDatabase.DeleteAsset(path);

            var fa = TMP_FontAsset.CreateFontAsset(ttf, 44, 5, GlyphRenderMode.SDFAA, 4096, 2048, AtlasPopulationMode.Dynamic, true);
            fa.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(fa, path);
            fa.material.name = fa.name + " Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            fa.TryAddCharacters(chars, out string missing);
            int t = 0;
            foreach (var tex in fa.atlasTextures)
            {
                if (tex == null || AssetDatabase.Contains(tex)) { t++; continue; }
                tex.name = fa.name + " Atlas" + (t == 0 ? "" : " " + t);
                AssetDatabase.AddObjectToAsset(tex, fa);
                t++;
            }
            fa.atlasPopulationMode = AtlasPopulationMode.Static;   // ★이후 글자가 들어와도 asset이 안 바뀐다
            if (fallback != null) fa.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { fallback };
            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
            Debug.Log($"[KoPub] {fa.name} — 글자 {fa.characterTable.Count}개 · 아틀라스 {fa.atlasTextures.Length}장 4096×2048 · 빠진 글자 {(missing ?? "").Length}개 {(string.IsNullOrEmpty(missing) ? "" : "«" + (missing.Length > 40 ? missing.Substring(0, 40) + "…" : missing) + "»")} · 대체 {(fallback != null ? fallback.name : "없음")}");
        }
        bold = medium = light = null;
    }

    public enum Role { Bold, Medium, Light }

    /// <summary>글자칸의 역할을 정한다(위 규칙). forceBold = 로비 머리 제목처럼 크기는 작아도 제목인 것.</summary>
    public static Role RoleOf(TMP_Text t, bool forceBold = false)
    {
        if (forceBold || t.fontSize >= 20f) return Role.Bold;
        Color.RGBToHSV(t.color, out _, out float s, out float v);
        if (s < 0.15f && v > 0.5f && v < 0.8f) return Role.Light;   // 회색 보조 글자(#A3A3A3 = v 0.64)
        return Role.Medium;
    }

    /// <summary>역할에 맞는 굵기를 입힌다. 바꿨으면 true · 글자가 없어 건너뛰면 false.</summary>
    public static bool Apply(TMP_Text t, bool forceBold = false)
    {
        if (t == null || !Ready) return false;
        var role = RoleOf(t, forceBold);
        var fa = role == Role.Bold ? Bold : role == Role.Light ? Light : Medium;
        string s = t.text ?? "";
        if (s.Length > 0 && !fa.HasCharacters(StripTags(s), out _, false, false)) return false;   // 기호 아이콘 등 — 종전 글꼴 그대로
        t.font = fa;
        t.fontSharedMaterial = fa.material;
        t.fontStyle &= ~FontStyles.Bold;
        return true;
    }

    static string StripTags(string s) => System.Text.RegularExpressions.Regex.Replace(s, "<[^>]*>", "").Replace("\n", "").Replace("\r", "").Replace("\t", "").Replace("​", "");
}
