using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using K = HzUIKit;

/// <summary>
/// 실습 씬 V0.9 조립 (2026-10-27 · 시안 V0.9 그림 5 · 6).
///   ⑤ 상단 바 632 × 104 — 선반 토글 112 × 80(아이콘 24 위 · 글자 14 아래) · 콘텐츠 3 | 메뉴 2 · 켜진 것 Meta 파랑
///   ⑥ 설정 리모컨 824 × 204 — 손으로 누르는 버튼 60 · [자동 맞춤] 흰 · [완료] Meta 파랑 · 닫기 없음
/// 상단 바 버튼은 진짜 토글(옛 바 B 안 · InfoPanelController가 쥔다)을 대신 누른다 — 옛 바는 안 보이게 · 손 판정만 끈다.
/// 이 도구가 만든 옛 리모컨(A/B) · 옛 상단 바 A는 지우고 새로 만든다. ★저장은 사람이 한다.
/// 판 실제 배율(m/px): 상단 바 0.001(앞 1.0m) · 리모컨 0.0006(앞 0.5m) — ★헤드셋에서 확인 · 조정할 값.
/// </summary>
public static class TrainingV09Tool
{
    const float BarScale = 0.001f, RemoteScale = 0.0006f;

    [MenuItem("GuideChuna/UI 개편/V0.9 실습 씬 조립 (TrainingScene)")]
    public static void BuildAll()
    {
        var info = Object.FindFirstObjectByType<InfoPanelController>(FindObjectsInactive.Include);
        var settings = Object.FindFirstObjectByType<PracticeSettingsController>(FindObjectsInactive.Include);
        var group = GameObject.Find("UI Group");
        if (info == null || settings == null || group == null) { Debug.LogWarning("[V09Train] TrainingScene이 아니다"); return; }
        BuildRemote(info, settings, group.transform);
        BuildBar(info, group.transform);
        EditorSceneManager.MarkSceneDirty(info.gameObject.scene);
        Debug.Log("[V09Train] 실습 씬 V0.9 조립 끝 · ★저장은 Ctrl+S");
    }

    static void Kill(Transform parent, System.Func<string, bool> match)
    {
        var dead = new List<GameObject>();
        foreach (Transform t in parent) if (match(t.name)) dead.Add(t.gameObject);
        foreach (var d in dead) Undo.DestroyObjectImmediate(d);
    }

    // ── ⑥ 설정 리모컨 ──
    static void BuildRemote(InfoPanelController info, PracticeSettingsController settings, Transform group)
    {
        Kill(group, n => n.StartsWith(PositionRemoteSetupTool.RootName));
        var root = new GameObject(PositionRemoteSetupTool.RootName);
        Undo.RegisterCreatedObjectUndo(root, "리모컨");
        root.transform.SetParent(group, false);
        var remote = root.AddComponent<PatientPositionRemote>();
        var rso = new SerializedObject(remote);
        rso.FindProperty("practiceSettings").objectReferenceValue = settings;
        rso.FindProperty("infoPanel").objectReferenceValue = info;
        rso.ApplyModifiedPropertiesWithoutUndo();

        const float W = 824, H = 204, BH = 60;
        var back = K.Plate(root, W, H, RemoteScale);
        K.Text(back, "제목", 24, 24, 200, 20, "위치 조정", 14, K.Text2);
        float y1 = 24 + 20 + 8, y2 = y1 + BH + 8, x = 24;
        var auto = K.Btn(back, "자동 맞춤", x, y1, 132, 2 * BH + 8, "자동 맞춤", K.Style.Secondary); x += 132 + 16;
        UnityEventTools.AddPersistentListener(auto.onClick, remote.AutoFit);
        K.Text(back, "높이", x, 24, 112, 20, "높이", 14, K.Text2, false, TextAlignmentOptions.Center);
        Dir(back, remote, PatientPositionRemote.Direction.Up, x, y1, "▲", "위");
        Dir(back, remote, PatientPositionRemote.Direction.Down, x, y2, "▼", "아래"); x += 112 + 16;
        K.Text(back, "앞뒤 좌우", x, 24, 352, 20, "앞뒤 · 좌우", 14, K.Text2, false, TextAlignmentOptions.Center);
        Dir(back, remote, PatientPositionRemote.Direction.Forward, x + 120, y1, "▲", "앞");
        Dir(back, remote, PatientPositionRemote.Direction.Left, x, y2, "◀", "좌");
        Dir(back, remote, PatientPositionRemote.Direction.Back, x + 120, y2, "▼", "뒤");
        Dir(back, remote, PatientPositionRemote.Direction.Right, x + 240, y2, "▶", "우"); x += 3 * 112 + 16 + 16;
        var done = K.Btn(back, "완료", x, y1, 132, 2 * BH + 8, "완료", K.Style.Primary);   // 주 행동 = 흰 알약(실측)
        UnityEventTools.AddPersistentListener(done.onClick, remote.Done);

        var iso = new SerializedObject(info);
        Undo.RecordObject(info, "설정 배선");
        iso.FindProperty("settingsPopup").objectReferenceValue = root;
        iso.ApplyModifiedProperties();
        root.SetActive(false);
        Debug.Log($"[V09Train] ⑥ 리모컨 {W} × {H}px × {RemoteScale} = {W * RemoteScale:F2} × {H * RemoteScale:F2}m · 버튼 60 · 설정 버튼 → 이 리모컨");
    }

    static void Dir(Transform back, PatientPositionRemote remote, PatientPositionRemote.Direction d, float x, float y, string glyph, string label)
    {
        var b = K.Btn(back, label, x, y, 112, 60, label, K.Style.Secondary, glyph);
        var hold = b.gameObject.AddComponent<PatientPositionRemoteButton>();
        var so = new SerializedObject(hold);
        so.FindProperty("remote").objectReferenceValue = remote;
        so.FindProperty("direction").enumValueIndex = (int)d;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ── ⑤ 상단 바 ──
    static void BuildBar(InfoPanelController info, Transform group)
    {
        // 진짜 토글이 든 옛 바(B)
        Transform barB = group.Find("상단 바 B (Meta 기본)") ?? group.Find("상단 바");
        if (barB == null) { Debug.LogWarning("[V09Train] 진짜 토글이 든 옛 상단 바가 없다 — 먼저 «② 상단 바 + 정보 창 만들기»"); return; }
        Kill(group, n => n == "상단 바 A (시안)" || n == "상단 바 V0.9");
        var iso = new SerializedObject(info);
        Toggle[] real = {
            iso.FindProperty("skeletonToggle").objectReferenceValue as Toggle, iso.FindProperty("expertVideoToggle").objectReferenceValue as Toggle,
            iso.FindProperty("resultToggle").objectReferenceValue as Toggle, iso.FindProperty("settingsToggle").objectReferenceValue as Toggle,
            iso.FindProperty("mainMenuToggle").objectReferenceValue as Toggle };
        string[] labels = { "근골격", "전문가 영상", "수행결과", "설정", "메인으로" };

        const float TW = 112, TH = 80;
        float W = 12 + 5 * TW + 4 * 8 + 16 + 12, H = 12 + TH + 12;
        var root = new GameObject("상단 바 V0.9");
        Undo.RegisterCreatedObjectUndo(root, "상단 바");
        root.transform.SetParent(group, false);
        root.AddComponent<TopBarPlacer>();
        var back = K.Plate(root, W, H, BarScale);
        float x = 12;
        for (int i = 0; i < 5; i++)
        {
            if (i == 3) { var d = K.Box(back, "구분선", x + 7, 24, 2, 56, new Color(1, 1, 1, 0.25f), 1); d.raycastTarget = false; x += 16; }
            var t = real[i];
            var bg = K.Box(back, "선반 " + labels[i], x, 12, TW, TH, K.Tile, 16);
            var b = bg.gameObject.AddComponent<Button>(); b.targetGraphic = bg; b.transition = Selectable.Transition.None;
            Sprite icon = null;
            if (t != null) foreach (var img in t.GetComponentsInChildren<Image>(true)) if (img.name.Contains("Icon") && img.sprite != null) { icon = img.sprite; break; }
            if (icon != null)
            {
                var ic = K.Rect(bg.transform, "아이콘", TW / 2 - 12, 14, 24, 24).gameObject.AddComponent<Image>();
                ic.sprite = icon; ic.color = K.Text1; ic.raycastTarget = false; ic.preserveAspect = true;
            }
            var lab = K.Text(bg.transform, "글자", 0, 14 + 24 + 8, TW, 22, labels[i], 14, K.Text1, true, TextAlignmentOptions.Center);
            var proxy = bg.gameObject.AddComponent<TopBarProxyButton>();
            var pso = new SerializedObject(proxy);
            pso.FindProperty("target").objectReferenceValue = t;
            pso.FindProperty("background").objectReferenceValue = bg;
            pso.FindProperty("onColor").colorValue = K.White;   // 켜진 것 = 흰 알약 + 어두운 글자(실측)
            pso.FindProperty("offColor").colorValue = K.Tile;
            var fg = pso.FindProperty("foreground"); fg.arraySize = 2;
            fg.GetArrayElementAtIndex(0).objectReferenceValue = lab;
            fg.GetArrayElementAtIndex(1).objectReferenceValue = bg.transform.Find("아이콘")?.GetComponent<Image>();
            pso.FindProperty("foreOn").colorValue = K.Ink;
            pso.FindProperty("foreOff").colorValue = K.Text1;
            pso.ApplyModifiedPropertiesWithoutUndo();
            x += TW + 8;
        }
        // 옛 바: 진짜 토글은 살려 두고 보이지 않게 · 손 판정만 끈다
        foreach (var c in barB.GetComponentsInChildren<Canvas>(true)) { Undo.RecordObject(c, "숨김"); c.enabled = false; }
        foreach (var m in barB.GetComponentsInChildren<Oculus.Interaction.PointableCanvas>(true)) { Undo.RecordObject(m, "숨김"); m.enabled = false; }
        foreach (var m in barB.GetComponentsInChildren<Oculus.Interaction.PokeInteractable>(true)) { Undo.RecordObject(m, "숨김"); m.enabled = false; }
        foreach (var m in barB.GetComponentsInChildren<Oculus.Interaction.RayInteractable>(true)) { Undo.RecordObject(m, "숨김"); m.enabled = false; }
        Debug.Log($"[V09Train] ⑤ 상단 바 {W} × {H}px × {BarScale} = {W * BarScale:F2} × {H * BarScale:F2}m · 선반 112 × 80 · 켜진 것 Meta 파랑 · 옛 바 '{barB.name}' 숨김(토글은 살아 있음)");
    }

    [MenuItem("GuideChuna/UI 개편/V0.9 실습 씬 판 미리보기 찍기 (TrainingScene)")]
    public static void Capture()
    {
        var group = GameObject.Find("UI Group"); if (group == null) return;
        foreach (var n in new[] { PositionRemoteSetupTool.RootName, "상단 바 V0.9" })
        {
            var t = group.transform.Find(n); if (t == null) continue;
            UIPanelPreviewCapture.Capture(t.gameObject, "V09_" + n.Replace(" ", ""));
        }
    }
}
