using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 로비 판 거리 2.2배 · 높이 따라가기 끔 (2026-10-30 사용자 «로비 메뉴 지금보다 2.2배 정도 뒤로 · 높이 따라오는 기능 빼 줘» · 크기는 그대로(사용자 선택)).
/// 판(로비 판 UIBackplate)의 UIAxis 기준 수평 거리 D를 재서 UI (1)을 판 방향으로 (배율 − 1) × D만큼 민다 — 판 · 팝업이 모두 그 아래라 같이 간다.
/// 높이: 옛 UIHight(매 프레임 머리 − 0.1m)를 끄고 UIHeightOnce(시작 때 한 번)를 붙인다. ★한 번만 민다 — UIHeightOnce가 이미 있으면 거리는 건너뛴다.
/// </summary>
public static class LobbyDistanceTool
{
    const float Factor = 2.2f;
    const string Axis = "OVRCameraRig/TrackingSpace/UIAxis";
    const string Holder = "UI (1)";
    const string PlatePath = "로비UI/ContentUIExample-HorizonOS1/CanvasRoot/UIBackplate+VerticalLayoutGroup";

    [MenuItem("GuideChuna/UI 개편/10-30 로비 판 거리 2.2배 · 높이 한 번만 (lobby)")]
    public static void Apply()
    {
        var axis = GameObject.Find(Axis);
        var holder = axis != null ? axis.transform.Find(Holder) : null;
        var plate = holder != null ? holder.Find(PlatePath) : null;
        if (axis == null || holder == null || plate == null) { Debug.LogWarning($"[로비거리] UIAxis {(axis != null)} · {Holder} {(holder != null)} · 판 {(plate != null)} — 로비가 아니다"); return; }

        bool already = axis.GetComponent<UIHeightOnce>() != null;
        var old = axis.GetComponent<UIHight>();
        if (old != null && old.enabled) { Undo.RecordObject(old, "로비거리"); old.enabled = false; }
        if (!already) Undo.AddComponent<UIHeightOnce>(axis);

        string moved = "건너뜀(이미 민 판)";
        if (!already)
        {
            Vector3 d = axis.transform.InverseTransformPoint(plate.position); d.y = 0f;   // UIAxis 기준 수평
            float D = d.magnitude;
            if (D > 1e-3f)
            {
                Vector3 push = axis.transform.TransformVector(d.normalized * (Factor - 1f) * D);
                Undo.RecordObject(holder, "로비거리");
                holder.position += push;
                moved = $"{D:F2}m → {D * Factor:F2}m";
            }
        }
        EditorSceneManager.MarkSceneDirty(axis.scene);
        Debug.Log($"[로비거리] 판 거리 {moved} · 높이 따라가기(UIHight) 끔 · UIHeightOnce {(already ? "있음" : "붙임")} · ★저장은 Ctrl+S");
    }
}
