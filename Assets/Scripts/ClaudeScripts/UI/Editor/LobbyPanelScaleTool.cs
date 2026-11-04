using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 로비 판 크기 1.5배 (2026-10-30 사용자 «화면 UI 좀 더 키워도 되겠다» → 로비 판 · 1.5배 선택).
/// UI (1)의 배율을 1.5배로 하고, 판(UIBackplate) 중심의 월드 자리는 그대로 둔다 — 거리 1.32m(LobbyDistanceTool)가 유지된다.
/// 판 · 팝업이 모두 UI (1) 아래라 같이 커진다. ★한 번만 — 배율이 이미 기준(0.3)보다 크면 건너뛴다.
/// </summary>
public static class LobbyPanelScaleTool
{
    const float Factor = 1.5f;
    const float BaseScale = 0.3f;
    const string Axis = "OVRCameraRig/TrackingSpace/UIAxis";
    const string Holder = "UI (1)";
    const string PlatePath = "로비UI/ContentUIExample-HorizonOS1/CanvasRoot/UIBackplate+VerticalLayoutGroup";

    [MenuItem("GuideChuna/UI 개편/10-30 로비 판 크기 1.5배 (lobby)")]
    public static void Apply()
    {
        var axis = GameObject.Find(Axis);
        var holder = axis != null ? axis.transform.Find(Holder) : null;
        var plate = holder != null ? holder.Find(PlatePath) : null;
        if (holder == null || plate == null) { Debug.LogWarning($"[로비크기] {Holder} {(holder != null)} · 판 {(plate != null)} — 로비가 아니다"); return; }

        float s = holder.localScale.x;
        if (s > BaseScale * 1.05f) { Debug.Log($"[로비크기] 건너뜀 — 배율이 이미 {s:F3}(기준 {BaseScale})"); return; }

        Vector3 before = plate.position;
        Undo.RecordObject(holder, "로비크기");
        holder.localScale = holder.localScale * Factor;
        holder.position += before - plate.position;   // 판 중심 자리 유지
        EditorSceneManager.MarkSceneDirty(holder.gameObject.scene);
        Debug.Log($"[로비크기] 배율 {s:F3} → {holder.localScale.x:F3} · 판 중심 {before} 유지 · UI (1) 로컬 {holder.localPosition} · ★저장은 Ctrl+S");
    }
}
