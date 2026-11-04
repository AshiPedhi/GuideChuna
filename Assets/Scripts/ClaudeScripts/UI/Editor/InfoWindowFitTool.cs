using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 실습 정보 창(골격 · 영상 · 진행 및 결과) — 조금 앞으로 당기고, 옆 창과의 틈이 얇은 테두리 정도만 남게 키운다
/// (2026-10-30 사용자 «정보 패널 앞을 좀만 당겨 오고 양옆 패널 간 빈 공간이 거의 없게 얇은 베젤 정도가 생기게 맞춰서 키워 줘»).
/// 계산: 창은 머리(펼침 기준)에서 반지름 R인 원에 접한 평평한 판이고 이웃 창과 sideAngle(θ)만큼 떨어진다.
///   가장자리가 맞닿는 폭 = 2R·tan(θ/2) → 거기서 틈(Bezel)을 뺀 폭이 되게 창마다 배율을 정한다(창마다 판 픽셀 폭이 달라 배율이 다를 수 있다).
/// ★OS 화면 크기(OsDisplayScale.Density)가 1이 아니면 기기에서 간격이 같이 벌어진다 — 이 계산은 Density 1 기준이다.
/// </summary>
public static class InfoWindowFitTool
{
    const float Radius = 3.2f;    // 종전 3.6 — «좀만 당겨»
    const float Bezel = 0.03f;    // 창 사이 틈 3cm

    [MenuItem("GuideChuna/UI 개편/10-30 정보 창 당기기 · 틈 3cm로 키우기 (TrainingScene)")]
    public static void Apply()
    {
        var arr = Object.FindFirstObjectByType<InfoWindowArranger>(FindObjectsInactive.Include);
        if (arr == null) { Debug.LogWarning("[정보창맞춤] InfoWindowArranger 없음 — TrainingScene이 아니다"); return; }
        var so = new SerializedObject(arr);
        float oldR = so.FindProperty("radius").floatValue, theta = so.FindProperty("sideAngle").floatValue;
        float target = 2f * Radius * Mathf.Tan(theta * 0.5f * Mathf.Deg2Rad) - Bezel;
        var sb = new System.Text.StringBuilder($"[정보창맞춤] 반지름 {oldR} → {Radius}m · 좌우 {theta}° · 맞닿는 폭 {target + Bezel:F3}m − 틈 {Bezel}m = 판 폭 {target:F3}m\n");

        var slots = so.FindProperty("slots");
        for (int i = 0; i < slots.arraySize; i++)
        {
            var f = slots.GetArrayElementAtIndex(i).FindPropertyRelative("frame").objectReferenceValue as Transform;
            if (f == null) continue;
            var plate = Widest(f);
            if (plate == null) { sb.Append($"  ★{f.name} 판을 못 찾음\n"); continue; }
            float w = WorldWidth(plate);
            if (w < 1e-4f) { sb.Append($"  ★{f.name} 판 폭 0\n"); continue; }
            Undo.RecordObject(f, "정보창맞춤");
            float k = target / w;
            Vector3 before = f.localScale;
            f.localScale = before * k;
            sb.Append($"  {f.name}: 판 '{plate.name}' {w:F3}m → {target:F3}m · 배율 {before.x:F2} → {f.localScale.x:F2}\n");
        }
        so.FindProperty("radius").floatValue = Radius;
        so.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(arr.gameObject.scene);
        Debug.Log(sb.Append("★저장은 Ctrl+S").ToString());
    }

    // 창 안에서 가장 넓은 판 — UIBackplate가 있으면 그것(겉 판)
    static RectTransform Widest(Transform frame)
    {
        RectTransform best = null; float bw = 0f;
        foreach (var r in frame.GetComponentsInChildren<RectTransform>(true))
        {
            float w = WorldWidth(r);
            if (r.name.StartsWith("UIBackplate") && w > 1e-4f) { if (best == null || !best.name.StartsWith("UIBackplate") || w > bw) { best = r; bw = w; } continue; }
            if (best != null && best.name.StartsWith("UIBackplate")) continue;
            if (w > bw) { best = r; bw = w; }
        }
        return best;
    }

    static readonly Vector3[] C = new Vector3[4];
    static float WorldWidth(RectTransform r) { r.GetWorldCorners(C); return Vector3.Distance(C[1], C[2]); }
}
