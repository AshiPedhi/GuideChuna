using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 로딩 가림막 설정 에셋 만들기 (2026-10-30) — Resources/LoadingOverlay/LoadingOverlayConfig.asset.
/// 글꼴 = 로비와 같은 KoPub(제목 Bold · 안내 Medium) · 셰이더 = ZTest Always 사본 · 로고 = 코코추나 로고. 여러 번 돌려도 같은 결과.
/// </summary>
public static class LoadingOverlaySetupTool
{
    const string Dir = "Assets/Resources/LoadingOverlay";
    const string Path_ = Dir + "/LoadingOverlayConfig.asset";
    const string LogoPath = "Assets/_JDH/ui png/코코추나 로고.png";
    const string RoundedMat = "Packages/com.meta.xr.sdk.interaction/Runtime/Sample/Materials/RoundedBoxUI.mat";

    [MenuItem("GuideChuna/UI 개편/10-30 로딩 화면 설정 만들기")]
    public static void Create()
    {
        Directory.CreateDirectory(Dir);
        var cfg = AssetDatabase.LoadAssetAtPath<LoadingOverlayConfig>(Path_);
        if (cfg == null) { cfg = ScriptableObject.CreateInstance<LoadingOverlayConfig>(); AssetDatabase.CreateAsset(cfg, Path_); }
        if (!KoPubFonts.Ready) KoPubFonts.Create();
        cfg.titleFont = KoPubFonts.Bold;
        cfg.bodyFont = KoPubFonts.Medium;
        cfg.veilShader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Art/OnTopUI/LoadingVeil.shader");
        cfg.roundedBoxOnTop = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Art/OnTopUI/RoundedBoxUI_OnTop.shader");
        cfg.tmpOnTop = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Art/OnTopUI/TMP_SDF_Mobile_OnTop.shader");
        cfg.roundedBoxMaterial = AssetDatabase.LoadAssetAtPath<Material>(RoundedMat);
        var imp = AssetImporter.GetAtPath(LogoPath) as TextureImporter;
        if (imp != null && imp.textureType != TextureImporterType.Sprite) { imp.textureType = TextureImporterType.Sprite; imp.SaveAndReimport(); }
        cfg.logo = AssetDatabase.LoadAssetAtPath<Sprite>(LogoPath);
        EditorUtility.SetDirty(cfg); AssetDatabase.SaveAssets();
        Debug.Log($"[LoadingOverlay] 설정 — 글꼴 {(cfg.titleFont != null)}/{(cfg.bodyFont != null)} · 가림막 셰이더 {(cfg.veilShader != null)} · 판 {(cfg.roundedBoxOnTop != null)}/{(cfg.roundedBoxMaterial != null)} · 글자 {(cfg.tmpOnTop != null)} · 로고 {(cfg.logo != null)} → {Path_}");
    }
}
