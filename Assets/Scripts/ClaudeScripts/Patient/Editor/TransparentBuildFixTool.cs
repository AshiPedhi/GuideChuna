using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 빌드에서만 반투명이 안 먹는 문제를 고친다.
///
/// ★증상(2026-08-13 사용자): 에디터·Link에서는 파지 표시가 반투명인데, <b>빌드하면 생으로(불투명) 보인다.</b>
/// ★원인: 이 프로젝트는 런타임에 Standard 머티리얼을 <c>_Mode=3</c>(Fade)으로 바꿔 반투명을 만든다
///   (GripPointTarget·ShoulderBraceGuide·CranialHeadXray 등). 그런데 <b>빌드에 포함된 머티리얼 중
///   Fade를 쓰는 것이 하나도 없으면 Unity가 그 셰이더 배리언트(_ALPHABLEND_ON)를 스트립</b>한다.
///   에디터에는 모든 배리언트가 살아 있어 증상이 드러나지 않는다.
/// ★조치: 필요한 배리언트를 ShaderVariantCollection에 담아 GraphicsSettings의 Preloaded Shaders에 등록한다.
///   그러면 어떤 오브젝트가 런타임에 Fade로 바꾸든 배리언트가 빌드에 들어 있다.
///
/// 같은 계열의 선례: 커스텀 xray 셰이더가 빌드에서 스트립돼 xray가 죽었고, Always Included Shaders
/// 등록으로 고쳤다(07-30). 그때는 셰이더 자체, 이번은 <b>배리언트</b>가 빠진 것이다.
/// </summary>
public static class TransparentBuildFixTool
{
    private const string CollectionPath = "Assets/Resources/BuildShaderVariants.shadervariants";

    [MenuItem("GuideChuna/빌드/반투명 문제 수정 (셰이더 배리언트 등록)")]
    public static void Fix()
    {
        Shader standard = Shader.Find("Standard");
        if (standard == null)
        {
            EditorUtility.DisplayDialog("빌드 반투명 수정", "Standard 셰이더를 찾지 못했습니다.", "확인");
            return;
        }

        var svc = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(CollectionPath);
        if (svc == null)
        {
            EnsureFolder("Assets/Resources");
            svc = new ShaderVariantCollection();
            AssetDatabase.CreateAsset(svc, CollectionPath);
        }

        // 런타임에 켜는 키워드 조합. Fade(_ALPHABLEND_ON)가 핵심이고,
        // Transparent 모드(_ALPHAPREMULTIPLY_ON)도 같이 담아 둔다(다른 코드가 쓸 수 있다).
        var keywordSets = new List<string[]>
        {
            new[] { "_ALPHABLEND_ON" },
            new[] { "_ALPHAPREMULTIPLY_ON" },
            new[] { "_ALPHATEST_ON" },
            new string[0],
        };

        // 이 프로젝트는 Built-in 파이프라인이다 → Forward 경로의 패스들을 담는다.
        var passes = new[] { PassType.ForwardBase, PassType.ForwardAdd, PassType.ShadowCaster };

        int added = 0, already = 0, failed = 0;
        foreach (var keywords in keywordSets)
        {
            foreach (var pass in passes)
            {
                try
                {
                    var v = new ShaderVariantCollection.ShaderVariant(standard, pass, keywords);
                    if (svc.Contains(v)) { already++; continue; }
                    svc.Add(v);
                    added++;
                }
                catch (System.ArgumentException)
                {
                    // 그 패스에 없는 조합 — 무시한다(셰이더마다 지원 패스가 다르다).
                    failed++;
                }
            }
        }

        EditorUtility.SetDirty(svc);
        AssetDatabase.SaveAssets();

        bool registered = RegisterPreloaded(svc);

        string msg =
            $"배리언트 {added}개 추가 (이미 있음 {already} / 해당 없음 {failed})\n" +
            $"컬렉션: {CollectionPath}\n" +
            $"Preloaded Shaders 등록: {(registered ? "완료" : "이미 등록돼 있음")}\n\n" +
            "★이제 다시 빌드하세요. 이 설정은 빌드에만 영향을 주므로 에디터에서는 차이가 안 보입니다.\n" +
            "여전히 불투명하면 그 오브젝트가 Standard가 아닌 다른 셰이더를 쓰는 것이니 알려주세요.";
        Debug.Log("[TransparentBuildFix] " + msg);
        EditorUtility.DisplayDialog("빌드 반투명 문제 수정", msg, "확인");
    }

    /// <summary>GraphicsSettings의 Preloaded Shaders 목록에 컬렉션을 넣는다(중복 방지).</summary>
    private static bool RegisterPreloaded(ShaderVariantCollection svc)
    {
        var gs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
        if (gs == null || gs.Length == 0) return false;

        var so = new SerializedObject(gs[0]);
        var list = so.FindProperty("m_PreloadedShaders");
        if (list == null) return false;

        for (int i = 0; i < list.arraySize; i++)
            if (list.GetArrayElementAtIndex(i).objectReferenceValue == svc) return false;   // 이미 있음

        list.InsertArrayElementAtIndex(list.arraySize);
        list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = svc;
        so.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
        return true;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }
}
