using Oculus.Interaction.Surfaces;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 설정 리모컨(<see cref="PatientPositionRemote"/>)을 열린 TrainingScene에 조립한다 (2026-10-27 · 시안 V0.7 그림 6).
///
/// Meta UISet 부품을 그대로 쓴다 — 판 = <c>EmptyUIBackplateWithCanvas</c>(손 · 레이 상호작용 포함),
/// 버튼 = <c>Primary/SecondaryButton_IconAndLabel_UnityUIButton</c>. 색 · 모서리 · 눌림 반응은 부품 값 그대로다.
///
/// ★비파괴: 같은 이름(<see cref="RootName"/>)이 이미 있으면 아무것도 만들지 않는다.
/// ★배선 하나만 바꾼다: <c>InfoPanelController.settingsPopup</c> = 이 리모컨(옛 값 <c>Setting</c>은 로그로 남긴다).
///   옛 설정 창(<c>Setting</c>)은 지우지 않는다 — 열리지 않을 뿐이다. 그 안의 현실 모드 · 골격 표시 · 모델 표시 버튼도 같이 안 보이게 된다(요청 6).
/// ★Undo가 걸린다. 저장은 사람이 한다(Ctrl+S).
/// </summary>
public static class PositionRemoteSetupTool
{
    public const string RootName = "설정 리모컨";
    const string BackplateGuid = "6b196de96e7d16b4297c1980179ae439";   // EmptyUIBackplateWithCanvas
    const string PrimaryGuid = "708bb04189b8488418aa725e7f94bbe8";     // PrimaryButton_IconAndLabel_UnityUIButton
    const string SecondaryGuid = "b9b87f4caa5968745bb0a1ee2a51f575";   // SecondaryButton_IconAndLabel_UnityUIButton
    const string KoreanFontGuid = "561f4e50cf36d5a498aa8dd400160e55";  // NotoSansKR-Bold — 씬 UI가 가장 많이 쓰는 글꼴

    // 판 크기(캔버스 px). UISet 캔버스 배율 0.0005 → 1400 × 300 px = 0.70 × 0.15 m
    const float W = 1400f, H = 300f;

    [MenuItem("GuideChuna/UI 개편/① 설정 리모컨 만들기 (TrainingScene)")]
    public static void Build()
    {
        if (FindExisting() != null) { Debug.LogWarning($"[PositionRemoteSetup] '{RootName}'이 이미 씬에 있다(꺼진 것 포함). 새로 만들지 않는다 — 다시 만들려면 «다시 만들기»"); return; }

        var info = Object.FindFirstObjectByType<InfoPanelController>(FindObjectsInactive.Include);
        var settings = Object.FindFirstObjectByType<PracticeSettingsController>(FindObjectsInactive.Include);
        if (info == null || settings == null) { Debug.LogWarning("[PositionRemoteSetup] InfoPanelController · PracticeSettingsController를 못 찾았다. TrainingScene을 열고 다시."); return; }

        var backplate = Load(BackplateGuid); var primary = Load(PrimaryGuid); var secondary = Load(SecondaryGuid);
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(KoreanFontGuid));
        if (backplate == null || primary == null || secondary == null) { Debug.LogWarning("[PositionRemoteSetup] UISet 프리팹을 못 찾았다(Meta Interaction SDK)."); return; }

        Undo.SetCurrentGroupName("설정 리모컨 만들기");
        int group = Undo.GetCurrentGroup();

        // 판
        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "리모컨");
        var parent = GameObject.Find("UI Group");
        if (parent != null) root.transform.SetParent(parent.transform, false);
        var plate = (GameObject)PrefabUtility.InstantiatePrefab(backplate, root.transform);
        plate.name = "판(UISet EmptyUIBackplateWithCanvas)";
        ResizePlate(plate);
        Transform content = FindDeep(plate.transform, "UIBackplate") ?? FindDeep(plate.transform, "CanvasRoot");

        var remote = Undo.AddComponent<PatientPositionRemote>(root);
        var so = new SerializedObject(remote);
        so.FindProperty("practiceSettings").objectReferenceValue = settings;
        so.FindProperty("infoPanel").objectReferenceValue = info;
        so.ApplyModifiedPropertiesWithoutUndo();

        // 제목 · 묶음 이름
        Label(content, "위치 조정", font, 30, new Vector2(-W / 2 + 110, H / 2 - 34), new Vector2(200, 40), TextAlignmentOptions.Left);
        Label(content, "높이", font, 26, new Vector2(-300, H / 2 - 34), new Vector2(160, 36), TextAlignmentOptions.Center);
        Label(content, "앞뒤 · 좌우", font, 26, new Vector2(120, H / 2 - 34), new Vector2(300, 36), TextAlignmentOptions.Center);

        // 버튼 — 자리 · 크기는 시안 V0.7 그림 6 (가로로 길게 · 위아래 두 줄)
        var auto = MakeButton(content, primary, "자동 맞춤", font, new Vector2(-560, -18), new Vector2(240, 200));
        UnityEditor.Events.UnityEventTools.AddPersistentListener(auto.onClick, remote.AutoFit);
        Dir(content, secondary, "▲ 위", font, new Vector2(-300, 44), new Vector2(160, 88), remote, PatientPositionRemote.Direction.Up);
        Dir(content, secondary, "▼ 아래", font, new Vector2(-300, -76), new Vector2(160, 88), remote, PatientPositionRemote.Direction.Down);
        Dir(content, secondary, "▲ 앞", font, new Vector2(120, 44), new Vector2(170, 88), remote, PatientPositionRemote.Direction.Forward);
        Dir(content, secondary, "◀ 좌", font, new Vector2(-70, -76), new Vector2(150, 88), remote, PatientPositionRemote.Direction.Left);
        Dir(content, secondary, "▼ 뒤", font, new Vector2(120, -76), new Vector2(170, 88), remote, PatientPositionRemote.Direction.Back);
        Dir(content, secondary, "우 ▶", font, new Vector2(310, -76), new Vector2(150, 88), remote, PatientPositionRemote.Direction.Right);
        var done = MakeButton(content, primary, "완료", font, new Vector2(560, -18), new Vector2(240, 200));
        UnityEditor.Events.UnityEventTools.AddPersistentListener(done.onClick, remote.Done);

        // 배선: 설정 버튼이 여는 것 = 이 리모컨
        var infoSo = new SerializedObject(info);
        var popup = infoSo.FindProperty("settingsPopup");
        Object old = popup.objectReferenceValue;
        Undo.RecordObject(info, "설정 팝업 배선");
        popup.objectReferenceValue = root;
        infoSo.ApplyModifiedProperties();

        root.SetActive(false);   // 설정 버튼이 켤 때 OnEnable에서 사용자 앞에 놓인다
        Undo.CollapseUndoOperations(group);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log($"[PositionRemoteSetup] '{RootName}' 생성 · InfoPanelController.settingsPopup: {(old != null ? old.name : "none")} → {RootName} · ★저장은 Ctrl+S");
    }

    /// <summary>이 도구가 만든 리모컨(이름 <see cref="RootName"/>)만 지우고 다시 만든다. 다른 오브젝트는 건드리지 않는다.</summary>
    [MenuItem("GuideChuna/UI 개편/① 설정 리모컨 다시 만들기 (TrainingScene)")]
    public static void Rebuild()
    {
        var old = FindExisting();
        if (old != null) { Undo.DestroyObjectImmediate(old); Debug.Log($"[PositionRemoteSetup] 옛 '{RootName}' 지움"); }
        Build();
    }

    // ★GameObject.Find는 꺼진 오브젝트를 못 찾는다 — 리모컨은 평소 꺼져 있다
    static GameObject FindExisting()
    {
        foreach (var r in Object.FindObjectsByType<PatientPositionRemote>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (r.gameObject.name == RootName) return r.gameObject;
        return null;
    }

    static GameObject Load(string guid) => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));

    static void ResizePlate(GameObject plate)
    {
        var canvasRoot = FindDeep(plate.transform, "CanvasRoot") as RectTransform;
        if (canvasRoot != null) canvasRoot.sizeDelta = new Vector2(W, H);
        var back = FindDeep(plate.transform, "UIBackplate") as RectTransform;
        if (back != null) { back.anchorMin = back.anchorMax = new Vector2(0.5f, 0.5f); back.anchoredPosition = Vector2.zero; back.sizeDelta = new Vector2(W, H); }
        // 손 · 레이가 닿는 영역(클리퍼)도 같은 비율로 늘린다 — 안 늘리면 가운데 500 × 500만 눌린다
        foreach (var c in plate.GetComponentsInChildren<BoundsClipper>(true))
        {
            Vector3 s = c.Size;
            c.Size = new Vector3(s.x * W / 500f, s.y * H / 500f, s.z);
            EditorUtility.SetDirty(c);
        }
    }

    internal static Button MakeButton(Transform parent, GameObject prefab, string text, TMP_FontAsset font, Vector2 pos, Vector2 size)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.name = "버튼 " + text;
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        // ★UISet 판(UIBackplate)에 세로 레이아웃이 있다 — 안 빼면 버튼이 자리를 무시하고 세로로 쌓인다
        var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        le.ignoreLayout = true;
        // ★UISet 버튼은 높이 40px 고정으로 짜여 있다(루트 레이아웃이 높이를 안 늘린다) — 큰 버튼을 쓰려면 높이를 넘겨준다
        foreach (var hlg in go.GetComponentsInChildren<HorizontalLayoutGroup>(true))
        {
            hlg.childControlHeight = true; hlg.childForceExpandHeight = true;
            if (hlg.name == "Elements") hlg.childAlignment = TextAnchor.MiddleCenter;
        }
        var bg = go.transform.Find("Content/Background");
        if (bg != null && bg.TryGetComponent<LayoutElement>(out var bgLe)) { bgLe.minHeight = size.y; bgLe.preferredHeight = size.y; }
        // 아이콘 · 부제는 끄고 본문 글자만 쓴다
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            if (t.name == "Icon" || t.name == "Subtitle") t.gameObject.SetActive(false);
        foreach (var tmp in go.GetComponentsInChildren<TMP_Text>(true))
        {
            if (tmp.name == "Subtitle") continue;
            tmp.text = text;
            if (font != null) tmp.font = font;
            tmp.fontSize = size.y >= 150 ? 40 : 32;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.alignment = TextAlignmentOptions.Center;
        }
        return go.GetComponent<Button>();
    }

    static void Dir(Transform parent, GameObject prefab, string text, TMP_FontAsset font, Vector2 pos, Vector2 size,
                    PatientPositionRemote remote, PatientPositionRemote.Direction d)
    {
        var b = MakeButton(parent, prefab, text, font, pos, size);
        var hold = b.gameObject.AddComponent<PatientPositionRemoteButton>();
        var so = new SerializedObject(hold);
        so.FindProperty("remote").objectReferenceValue = remote;
        so.FindProperty("direction").enumValueIndex = (int)d;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    internal static void Label(Transform parent, string text, TMP_FontAsset font, float size, Vector2 pos, Vector2 box, TextAlignmentOptions align)
    {
        var go = new GameObject("글자 " + text, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = box;
        go.AddComponent<LayoutElement>().ignoreLayout = true;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = size; tmp.alignment = align;
        tmp.color = new Color(1f, 1f, 1f, 0.7f);
        if (font != null) tmp.font = font;
        tmp.raycastTarget = false;
    }

    internal static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var r = FindDeep(c, name); if (r != null) return r; }
        return null;
    }
}
