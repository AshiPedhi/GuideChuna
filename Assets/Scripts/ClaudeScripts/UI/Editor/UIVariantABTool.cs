using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI 개편 A안 · B안 비교 (2026-10-27 사용자 «A랑 B 둘 다 구현해서 비교하게 해줘»).
///   A = 시안 V0.7의 색 · 모양 그대로(파랑 · 주황 · 초록, 가로 알약 버튼)
///   B = Meta UISet 기본 색 · 모양 + 시안의 구성 요소(구분선 등)만
///
/// [실습 씬] ① 설정 리모컨 A를 하나 더 만든다(기존 리모컨 = B) · ② 상단 바 A(알약 버튼 · 진짜 토글을 대신 누름)를 만든다(기존 바 = B · 구분선 추가).
/// [바꾸기] «UI 시안 A 적용» / «B 적용» — 열린 씬에 있는 A/B를 한꺼번에 바꾼다(로비 모드 팝업 · 리모컨 배선 · 상단 바).
///   상단 바 B를 숨길 때 진짜 토글은 끄지 않는다(InfoPanelController가 쥐고 있다) — 캔버스만 안 보이게 하고 손 · 레이 판정을 끈다.
/// ★저장은 사람이 한다.
/// </summary>
public static class UIVariantABTool
{
    const string RemoteA = "설정 리모컨 A (시안)", RemoteB = "설정 리모컨 B (Meta 기본)";
    const string BarA = "상단 바 A (시안)", BarB = "상단 바 B (Meta 기본)";
    const string BackplateGuid = "6b196de96e7d16b4297c1980179ae439";
    const string SecondaryGuid = "b9b87f4caa5968745bb0a1ee2a51f575";
    const string KoreanFontGuid = "561f4e50cf36d5a498aa8dd400160e55";
    static readonly Color Blue = new Color(0.18f, 0.48f, 0.96f, 1f);
    static readonly Color Green = new Color(0.12f, 0.52f, 0.29f, 1f);

    // ── 실습 씬: 리모컨 A ──
    [MenuItem("GuideChuna/UI 개편/A·B ① 설정 리모컨 A 만들기 (TrainingScene)")]
    public static void BuildRemoteA()
    {
        var b = FindRoot(PositionRemoteSetupTool.RootName) ?? FindRoot(RemoteB);
        if (b == null) { Debug.LogWarning("[UIVariant] 기존 설정 리모컨이 없다 — 먼저 «① 설정 리모컨 만들기»"); return; }
        if (FindRoot(RemoteA) != null) { Debug.LogWarning("[UIVariant] 리모컨 A가 이미 있다"); return; }
        Undo.RecordObject(b, "이름"); b.name = RemoteB;
        var a = Object.Instantiate(b, b.transform.parent);
        Undo.RegisterCreatedObjectUndo(a, "리모컨 A");
        a.name = RemoteA;
        // 복제본의 버튼이 복제본 리모컨을 부르게 다시 잇는다(Instantiate는 내부 참조를 복제본으로 돌려 준다 — 로그로 확인)
        var remoteA = a.GetComponent<PatientPositionRemote>();
        foreach (var hold in a.GetComponentsInChildren<PatientPositionRemoteButton>(true))
        {
            var so = new SerializedObject(hold);
            if (so.FindProperty("remote").objectReferenceValue != remoteA) Debug.LogWarning($"[UIVariant] {hold.name}이 원본 리모컨을 가리킨다");
        }
        foreach (var btn in a.GetComponentsInChildren<Button>(true))
        {
            string n = btn.name;
            var bg = btn.transform.Find("Content/Background")?.GetComponent<Image>();
            var label = btn.transform.Find("Content/Background/Elements/Text/Label")?.GetComponent<TMP_Text>();
            if (n.Contains("자동 맞춤") && bg != null) { bg.color = Blue; if (label != null) label.color = Color.white; }
            if (n.Contains("완료") && bg != null) { bg.color = Green; if (label != null) label.color = Color.white; }
            // [완료] · [자동 맞춤]의 클릭 배선이 원본 리모컨을 부르면 고친다
            var so = new SerializedObject(btn);
            var calls = so.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
            for (int i = 0; calls != null && i < calls.arraySize; i++)
            {
                var t = calls.GetArrayElementAtIndex(i).FindPropertyRelative("m_Target");
                if (t.objectReferenceValue is PatientPositionRemote r && r != remoteA) { t.objectReferenceValue = remoteA; Debug.Log($"[UIVariant] {n} 클릭 → 리모컨 A로 다시 이음"); }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        a.SetActive(false);
        EditorSceneManager.MarkSceneDirty(a.scene);
        Debug.Log("[UIVariant] 리모컨 A(자동 맞춤 파랑 · 완료 초록) 생성 · 기존 것은 B로 이름 바꿈");
    }

    // ── 실습 씬: 상단 바 A (알약 버튼) ──
    [MenuItem("GuideChuna/UI 개편/A·B ② 상단 바 A 만들기 (TrainingScene)")]
    public static void BuildBarA()
    {
        var b = FindRoot("상단 바") ?? FindRoot(BarB);
        if (b == null) { Debug.LogWarning("[UIVariant] 기존 상단 바가 없다 — 먼저 «② 상단 바 + 정보 창 만들기»"); return; }
        var oldA = FindRoot(BarA);
        if (oldA != null) { Undo.DestroyObjectImmediate(oldA); Debug.Log("[UIVariant] 옛 상단 바 A 지우고 다시 만든다"); }
        Undo.RecordObject(b, "이름"); b.name = BarB;
        var row = b.GetComponentInChildren<HorizontalLayoutGroup>(true)?.transform;
        var toggles = new List<Toggle>();
        foreach (Transform c in FindDeep(b.transform, "버튼 줄") ?? row) { var t = c.GetComponent<Toggle>(); if (t != null) toggles.Add(t); }
        if (toggles.Count == 0) { Debug.LogWarning("[UIVariant] 상단 바 B에서 토글을 못 찾았다"); return; }

        // B에도 시안의 구분선(콘텐츠 3 | 메뉴 2)을 넣는다
        var rowB = toggles[0].transform.parent;
        if (rowB.Find("구분선") == null && toggles.Count >= 4)
        {
            var d = new GameObject("구분선", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            Undo.RegisterCreatedObjectUndo(d, "구분선");
            d.transform.SetParent(rowB, false);
            d.transform.SetSiblingIndex(toggles[2].transform.GetSiblingIndex() + 1);
            d.GetComponent<Image>().color = new Color(1, 1, 1, 0.25f);
            var le = d.GetComponent<LayoutElement>(); le.preferredWidth = 3; le.preferredHeight = 100;
        }

        var backplate = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(BackplateGuid));
        var secondary = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(SecondaryGuid));
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(KoreanFontGuid));
        float scale = b.GetComponentInChildren<Canvas>(true).transform.lossyScale.x;

        // 알약 버튼 크기(px) — 시안 그림 4-2 비율
        float pillW = 300, pillH = 90, gap = 18, divider = 40;
        float W = 40 + toggles.Count * pillW + (toggles.Count - 1) * gap + divider + 40, H = pillH + 40;
        var root = new GameObject(BarA);
        Undo.RegisterCreatedObjectUndo(root, "상단 바 A");
        root.transform.SetParent(b.transform.parent, false);
        root.AddComponent<TopBarPlacer>();
        var plate = (GameObject)PrefabUtility.InstantiatePrefab(backplate, root.transform);
        plate.name = "판(UISet)";
        var canvasRoot = (RectTransform)FindDeep(plate.transform, "CanvasRoot");
        canvasRoot.localScale = Vector3.one * scale; canvasRoot.sizeDelta = new Vector2(W, H);
        var back = (RectTransform)FindDeep(plate.transform, "UIBackplate");
        back.anchorMin = back.anchorMax = new Vector2(0.5f, 0.5f); back.anchoredPosition = Vector2.zero; back.sizeDelta = new Vector2(W, H);
        var backImg = back.GetComponent<Image>();
        if (backImg != null) back.gameObject.AddComponent<RoundedBoxUIProperties>().borderRadius = new Vector4(H / 2, H / 2, H / 2, H / 2);

        float x = -W / 2 + 40 + pillW / 2;
        for (int i = 0; i < toggles.Count; i++)
        {
            if (i == 3) { // 구분선
                var d = new GameObject("구분선", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                d.transform.SetParent(back, false);
                d.GetComponent<LayoutElement>().ignoreLayout = true;
                var drt = (RectTransform)d.transform; drt.anchorMin = drt.anchorMax = new Vector2(0.5f, 0.5f);
                drt.anchoredPosition = new Vector2(x - pillW / 2 - gap / 2 + divider / 2 - gap / 2, 0); drt.sizeDelta = new Vector2(3, pillH - 20);
                d.GetComponent<Image>().color = new Color(1, 1, 1, 0.25f);
                x += divider;
            }
            var t = toggles[i];
            // ★토글 안 첫 글자는 UISet 자리표시 «Label»일 수 있다(10-27 캡처) — 켜져 있고 «Label»이 아닌 글자를 쓴다
            string label = t.name;
            foreach (var tx in t.GetComponentsInChildren<TMP_Text>(true))
                if (tx.gameObject.activeInHierarchy && !string.IsNullOrWhiteSpace(tx.text) && tx.text.Trim() != "Label") { label = tx.text.Trim(); break; }
            Sprite icon = null;
            foreach (var img in t.GetComponentsInChildren<Image>(true)) if (img.name.Contains("Icon") && img.sprite != null) { icon = img.sprite; break; }
            var btn = PositionRemoteSetupTool.MakeButton(back, secondary, label, font, new Vector2(x, 0), new Vector2(pillW, pillH));
            btn.name = "알약 " + label;
            var el = btn.transform.Find("Content/Background/Elements");
            var iconT = el?.Find("Icon");
            if (iconT != null && icon != null)
            {
                iconT.gameObject.SetActive(true);
                var ii = iconT.GetComponent<Image>(); ii.sprite = icon; ii.preserveAspect = true;
                var ile = iconT.GetComponent<LayoutElement>() ?? iconT.gameObject.AddComponent<LayoutElement>();
                ile.preferredWidth = 40; ile.preferredHeight = 40; ile.minWidth = 40; ile.minHeight = 40; ile.flexibleHeight = 0;
                var gapT = el.Find("Gap"); if (gapT != null) gapT.gameObject.SetActive(true);
            }
            var elHlg = el != null ? el.GetComponent<HorizontalLayoutGroup>() : null;
            if (elHlg != null) { elHlg.childForceExpandHeight = false; elHlg.childControlHeight = true; elHlg.childAlignment = TextAnchor.MiddleCenter; elHlg.spacing = 10; }
            var lab = btn.transform.Find("Content/Background/Elements/Text/Label")?.GetComponent<TMP_Text>();
            if (lab != null) { lab.fontSize = 30; lab.alignment = TextAlignmentOptions.Left; }
            var bgT = btn.transform.Find("Content/Background");
            var bgImg = bgT != null ? bgT.GetComponent<Image>() : null;
            var rb = bgT != null ? bgT.GetComponent<RoundedBoxUIProperties>() : null;
            if (rb != null) rb.borderRadius = new Vector4(pillH / 2, pillH / 2, pillH / 2, pillH / 2);
            var proxy = btn.gameObject.AddComponent<TopBarProxyButton>();
            var pso = new SerializedObject(proxy);
            pso.FindProperty("target").objectReferenceValue = t;
            pso.FindProperty("background").objectReferenceValue = bgImg;
            pso.ApplyModifiedPropertiesWithoutUndo();
            x += pillW + gap;
        }
        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log($"[UIVariant] 상단 바 A(알약 {toggles.Count}개 · 구분선 · 켜짐 파랑) 생성 · 판 {W * scale:F2}×{H * scale:F2}m · 기존 바는 B(구분선 추가)");
        Apply(true);
    }

    // ── 바꾸기 ──
    [MenuItem("GuideChuna/UI 개편/UI 시안 A 적용 (열린 씬)")] public static void ApplyA() => Apply(true);
    [MenuItem("GuideChuna/UI 개편/UI 시안 B 적용 (열린 씬)")] public static void ApplyB() => Apply(false);

    public static void Apply(bool a)
    {
        var log = new System.Text.StringBuilder($"[UIVariant] {(a ? "A(시안)" : "B(Meta 기본)")} 적용 —");
        // 로비 모드 팝업: 켜진 루트 하나가 Instance가 된다
        foreach (var p in Object.FindObjectsByType<LobbyModePopup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            bool isA = p.gameObject.name.Contains(" A ");
            if (!p.gameObject.name.StartsWith("모드 선택 팝업")) continue;
            Undo.RecordObject(p.gameObject, "A/B"); p.gameObject.SetActive(isA == a);
            log.Append($" 팝업 {(isA ? "A" : "B")} {(isA == a ? "켬" : "끔")} ·");
        }
        // 실습 씬: 설정 버튼이 여는 리모컨
        var info = Object.FindFirstObjectByType<InfoPanelController>(FindObjectsInactive.Include);
        var ra = FindRoot(RemoteA); var rb = FindRoot(RemoteB);
        if (info != null && ra != null && rb != null)
        {
            var so = new SerializedObject(info);
            Undo.RecordObject(info, "A/B");
            so.FindProperty("settingsPopup").objectReferenceValue = a ? ra : rb;
            so.ApplyModifiedProperties();
            log.Append($" 설정 → {(a ? RemoteA : RemoteB)} ·");
        }
        // 실습 씬: 상단 바 — A는 통째로 켜고 끈다, B는 진짜 토글이 들어 있어 캔버스 · 손 판정만 끈다
        var ba = FindRoot(BarA); var bb = FindRoot(BarB);
        if (ba != null && bb != null)
        {
            Undo.RecordObject(ba, "A/B"); ba.SetActive(a);
            foreach (var c in bb.GetComponentsInChildren<Canvas>(true)) { Undo.RecordObject(c, "A/B"); c.enabled = !a; }
            foreach (var m in bb.GetComponentsInChildren<Oculus.Interaction.PointableCanvas>(true)) { Undo.RecordObject(m, "A/B"); m.enabled = !a; }
            foreach (var m in bb.GetComponentsInChildren<Oculus.Interaction.PokeInteractable>(true)) { Undo.RecordObject(m, "A/B"); m.enabled = !a; }
            foreach (var m in bb.GetComponentsInChildren<Oculus.Interaction.RayInteractable>(true)) { Undo.RecordObject(m, "A/B"); m.enabled = !a; }
            log.Append($" 상단 바 {(a ? "A" : "B")} 보임 ·");
        }
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log(log.Append(" ★저장은 Ctrl+S").ToString());
    }

    /// <summary>실습 씬 A/B 판을 전부 찍는다(Captures/UIPreview/AB_*.png).</summary>
    [MenuItem("GuideChuna/UI 개편/A·B 실습 씬 판 찍기 (TrainingScene)")]
    public static void CaptureTraining()
    {
        foreach (var n in new[] { RemoteA, RemoteB, BarA, BarB })
        {
            var g = FindRoot(n);
            if (g == null) continue;
            var canvases = g.GetComponentsInChildren<Canvas>(true);
            var was = new bool[canvases.Length];
            for (int i = 0; i < canvases.Length; i++) { was[i] = canvases[i].enabled; canvases[i].enabled = true; }
            UIPanelPreviewCapture.Capture(g, "AB_" + n.Replace(" ", "").Replace("(", "_").Replace(")", ""));
            for (int i = 0; i < canvases.Length; i++) canvases[i].enabled = was[i];
        }
    }

    static GameObject FindRoot(string name)
    {
        var group = GameObject.Find("UI Group");
        if (group == null) return null;
        var t = group.transform.Find(name);
        return t != null ? t.gameObject : null;
    }

    static Transform FindDeep(Transform t, string name) => PositionRemoteSetupTool.FindDeep(t, name);
}
