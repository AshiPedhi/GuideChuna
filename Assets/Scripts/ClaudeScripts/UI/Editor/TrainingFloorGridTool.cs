using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 실습 암실 바닥에 격자 (2026-10-30 사용자 «실습 씬도 바닥에 격자무늬 넣어서 주변부는 조금 티가 나게»).
/// 암실 바닥(TrainingDarkroomTool · 반지름 12m 판 · Unlit/Texture)의 그림만 바꾼다 — 씬은 건드리지 않는다(재질 DarkroomFloor.mat의 텍스처 교체).
/// 바탕 = 종전과 같은 원형 그라디언트(가운데 #292929 → 가장자리 검정). 격자선 = 0.5m 칸 · 바탕보다 늦게 옅어져 바탕이 검게 사라진 주변부에도 선이 남는다
/// (벽 없는 어둠 속에서도 바닥이 이어진다는 느낌). 끝(반지름 95%)에서는 선도 사라져 판 테두리가 안 보인다.
/// </summary>
public static class TrainingFloorGridTool
{
    const string TexPath = "Assets/Art/Darkroom/darkroom_floor_grid.png";
    const string MatPath = "Assets/Art/Darkroom/DarkroomFloor.mat";
    const float FloorRadius = 12f;   // TrainingDarkroomTool.FloorRadius와 같다
    const float Cell = 0.5f;         // 격자 칸(m)
    const int N = 2048;              // 24m / 2048 ≈ 1.2cm/px
    const float LineMeters = 0.02f;  // 선 굵기 2cm

    [MenuItem("GuideChuna/UI 개편/10-30 실습 바닥 격자 (재질만 · TrainingScene)")]
    public static void Apply()
    {
        Directory.CreateDirectory("Assets/Art/Darkroom");
        var tex = new Texture2D(N, N, TextureFormat.RGB24, false);
        var row = new Color32[N];
        float mPerPx = FloorRadius * 2f / N;
        for (int y = 0; y < N; y++)
        {
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float bg = Mathf.Lerp(0.16f, 0f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(r / 0.95f)));   // 종전 바탕 그대로
                // 선까지 거리(m) — 칸 경계에서 가까운 쪽
                float wx = (x + 0.5f) * mPerPx, wy = (y + 0.5f) * mPerPx;
                float ex = Mathf.Abs(wx / Cell - Mathf.Round(wx / Cell)) * Cell, ey = Mathf.Abs(wy / Cell - Mathf.Round(wy / Cell)) * Cell;
                float d = Mathf.Min(ex, ey);
                float line = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - LineMeters * 0.5f) / mPerPx + 0.5f));   // 가장자리 1px 부드럽게
                // 선 밝기: 가운데 +0.07 → 반지름 60%까지 유지 → 95%에서 0 (바탕보다 늦게 사라진다)
                float lineAmt = 0.07f * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((r - 0.6f) / 0.35f)));
                float v = Mathf.Clamp01(bg + line * lineAmt);
                byte b = (byte)Mathf.RoundToInt(v * 255f);
                row[x] = new Color32(b, b, b, 255);
            }
            tex.SetPixels32(0, y, N, 1, row);
        }
        File.WriteAllBytes(TexPath, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(TexPath);
        var imp = (TextureImporter)AssetImporter.GetAtPath(TexPath);
        imp.wrapMode = TextureWrapMode.Clamp; imp.mipmapEnabled = true; imp.filterMode = FilterMode.Trilinear;
        imp.anisoLevel = 8;   // 비스듬히 보는 바닥 선이 번쩍이지 않게
        imp.maxTextureSize = N; imp.textureCompression = TextureImporterCompression.CompressedHQ;
        imp.SaveAndReimport();

        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat == null) { Debug.LogWarning($"[바닥격자] 재질이 없다 — 먼저 «암실» 도구로 바닥을 만든다: {MatPath}"); return; }
        mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexPath);
        EditorUtility.SetDirty(mat); AssetDatabase.SaveAssets();
        Debug.Log($"[바닥격자] {N}px · 칸 {Cell}m · 선 {LineMeters * 100f:F0}cm · 선은 반지름 60%까지 +0.07 → 95%에서 0 · 재질 {MatPath} 텍스처 교체(씬 변경 없음)");
    }
}
