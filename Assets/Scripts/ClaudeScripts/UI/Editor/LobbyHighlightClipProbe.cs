using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 로비 카드 하이라이트(«호버 바탕»)가 목록 마스크(실습 시나리오/Viewport)에 잘리는지 잰다 — 읽기 전용.
/// 10-29 사용자 «어떤 건 제대로 덮이고 어떤 건 하이라이트가 잘려». 카드마다 마스크 밖으로 나간 변과 길이를 찍는다.
/// </summary>
public static class LobbyHighlightClipProbe
{
    [MenuItem("GuideChuna/시험/로비 카드 하이라이트 잘림 재기 (읽기 전용)")]
    public static void Run()
    {
        var vpGo = GameObject.Find("OVRCameraRig/TrackingSpace/UIAxis/UI (1)/로비UI/ContentUIExample-HorizonOS1/CanvasRoot/UIBackplate+VerticalLayoutGroup/실습 시나리오/Viewport");
        if (vpGo == null) { Debug.LogWarning("[하이라이트잘림] Viewport 없음"); return; }
        var vp = (RectTransform)vpGo.transform;
        var sb = new StringBuilder($"[하이라이트잘림] 마스크 {vp.rect.width:F1}×{vp.rect.height:F1}\n");
        var corners = new Vector3[4];
        int n = 0, clipped = 0;
        foreach (var hover in vp.GetComponentsInChildren<LobbyCardHover>(true))
        {
            var hl = hover.transform.Find("호버 바탕") as RectTransform;
            if (hl == null) { sb.Append($"  {Label(hover)} — 호버 바탕 없음\n"); continue; }
            hl.GetWorldCorners(corners);
            Vector2 min = vp.InverseTransformPoint(corners[0]), max = vp.InverseTransformPoint(corners[2]);
            Rect r = vp.rect;
            float l = r.xMin - min.x, rt = max.x - r.xMax, b = r.yMin - min.y, t = max.y - r.yMax;
            n++;
            var cut = new StringBuilder();
            if (l > 0.01f) cut.Append($" 왼쪽 {l:F1}");
            if (rt > 0.01f) cut.Append($" 오른쪽 {rt:F1}");
            if (t > 0.01f) cut.Append($" 위 {t:F1}");
            if (b > 0.01f) cut.Append($" 아래 {b:F1}");
            bool outAll = min.y > r.yMax || max.y < r.yMin;   // 스크롤 밖(통째로 안 보임)은 따로 표시
            if (cut.Length > 0) clipped++;
            sb.Append($"  {Label(hover)} 하이라이트 x {min.x:F1}~{max.x:F1} · y {min.y:F1}~{max.y:F1} →{(cut.Length > 0 ? " 잘림" + cut : " 안 잘림")}{(outAll ? " (스크롤 밖)" : "")}\n");
        }
        sb.Append($"  카드 {n}장 중 잘림 {clipped}장");
        Debug.Log(sb.ToString());
    }

    static string Label(Component c)
    {
        var t = c.transform;
        return $"{t.parent.parent.name}#{t.GetSiblingIndex()}";
    }
}
