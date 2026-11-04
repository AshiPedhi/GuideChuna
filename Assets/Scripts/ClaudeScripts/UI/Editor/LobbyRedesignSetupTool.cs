using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로비 개편을 열린 lobby 씬에 조립한다 (2026-10-27 사용자 요청 1 · 2 · 4 · 시안 V0.7 그림 1 · 3).
///
/// ③ 카드 = 사진 칸 / 글자 띠 분리 — 카드는 UISet 타일 프리팹이라 글자를 «밖으로 옮기기»가 막힌다(TrainingScene에서 확인).
///    그래서 타일 안에 «사진» 칸을 새로 넣고(위 68%), 타일 바탕은 어두운 판으로 바꾸고, 글자는 아래 띠로 내린다. 카드 클릭 동작은 그대로.
/// ④ 머리줄 = 이름 · 도움말(?) · 패스스루(◐) · 종료(✕ 기존 그대로). 새 버튼은 기존 ✕ 버튼을 복제해 같은 UISet 모양을 쓴다.
///    도움말 = 기존 «조작법 익히기»(조작 연습 씬) 동작. 옛 사용자 아이콘 버튼은 이름 버튼이 같은 동작을 하므로 끈다.
/// ⑤ 모드 팝업 = UISet 판 + 버튼 — VR 실습 · VR 평가 · XR 실습 · XR 평가(준비 중 · 눌리지 않음) · 난이도 3칸 · 닫기.
///
/// ★비파괴: 이미 한 단계는 다시 하지 않는다(이름표로 판단). Undo가 걸린다. 저장은 사람이 한다.
/// </summary>
public static class LobbyRedesignSetupTool
{
    const string PrimaryGuid = "708bb04189b8488418aa725e7f94bbe8";
    const string SecondaryGuid = "b9b87f4caa5968745bb0a1ee2a51f575";
    const string BackplateGuid = "6b196de96e7d16b4297c1980179ae439";
    const string TileToggleGuid = "5292db714b0f87045a7dfd9be93b1be5";
    const string KoreanFontGuid = "561f4e50cf36d5a498aa8dd400160e55";
    static readonly Color TileColor = new Color(0.17f, 0.18f, 0.21f, 1f);
    static readonly Color SubColor = new Color(0.66f, 0.69f, 0.73f, 1f);

    [MenuItem("GuideChuna/UI 개편/③ 로비 카드 사진 · 글자 분리 (lobby)")]
    public static void SplitCards()
    {
        int n = 0;
        foreach (var card in Object.FindObjectsByType<LobbyItemCard>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var bg = card.transform.Find("Content/Background");
            if (bg == null || bg.Find("사진") != null) continue;
            var bgImg = bg.GetComponent<Image>();
            if (bgImg == null || bgImg.sprite == null) continue;

            var photo = new GameObject("사진", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(photo, "사진");
            photo.layer = bg.gameObject.layer;
            photo.transform.SetParent(bg, false);
            photo.transform.SetAsFirstSibling();   // 글자보다 먼저 그린다
            var rt = (RectTransform)photo.transform;
            rt.anchorMin = new Vector2(0f, 0.34f); rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(6, 0); rt.offsetMax = new Vector2(-6, -6);
            photo.AddComponent<LayoutElement>().ignoreLayout = true;
            var img = photo.AddComponent<Image>();
            img.sprite = bgImg.sprite; img.preserveAspect = false; img.raycastTarget = false;
            var round = bg.GetComponent<RoundedBoxUIProperties>();
            UseRoundMaterial(img, bgImg);
            var r2 = photo.AddComponent<RoundedBoxUIProperties>();
            r2.borderRadius = round != null ? round.borderRadius * 0.75f : new Vector4(12, 12, 12, 12);

            Undo.RecordObject(bgImg, "바탕");
            bgImg.sprite = null; bgImg.color = TileColor;

            var elements = bg.Find("Elements");
            if (elements != null)
            {
                var vlg = elements.GetComponent<VerticalLayoutGroup>();
                if (vlg != null) { Undo.RecordObject(vlg, "글자 띠"); vlg.childAlignment = TextAnchor.LowerLeft; vlg.padding.bottom = 8; vlg.childForceExpandHeight = false; }
                foreach (Transform c in elements)
                    if (c.name.StartsWith("Space")) { Undo.RecordObject(c.gameObject, "칸"); c.gameObject.SetActive(false); }
                var title = elements.Find("Label")?.GetComponent<TMP_Text>();
                var sub = elements.Find("Label (1)")?.GetComponent<TMP_Text>();
                if (title != null) { Undo.RecordObject(title, "제목"); title.color = Color.white; title.fontStyle |= FontStyles.Bold; }
                if (sub != null) { Undo.RecordObject(sub, "부제"); sub.color = SubColor; }
            }
            n++;
        }
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log($"[LobbySetup] 카드 {n}개 사진 · 글자 분리 · ★저장은 Ctrl+S");
    }

    [MenuItem("GuideChuna/UI 개편/③ 로비 카드 사진 모서리 둥글게 (lobby)")]
    public static void RoundCardPhotos()
    {
        int n = 0;
        foreach (var card in Object.FindObjectsByType<LobbyItemCard>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var bg = card.transform.Find("Content/Background");
            var photo = bg != null ? bg.Find("사진")?.GetComponent<Image>() : null;
            if (photo == null) continue;
            Undo.RecordObject(photo, "둥근 재질");
            UseRoundMaterial(photo, bg.GetComponent<Image>());
            n++;
        }
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log($"[LobbySetup] 카드 사진 {n}개 둥근 재질");
    }

    [MenuItem("GuideChuna/UI 개편/④ 로비 머리줄 — 이름 · 도움말 · 패스스루 (lobby)")]
    public static void Header()
    {
        var exitBtn = FindAction(LobbyMenuActions.MenuAction.ExitViaBrain);
        if (exitBtn == null) { Debug.LogWarning("[LobbySetup] 종료(✕) 버튼을 못 찾았다"); return; }
        var row = exitBtn.transform.parent;
        if (row.Find("이름") != null) { Debug.LogWarning("[LobbySetup] 머리줄은 이미 했다"); return; }
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(KoreanFontGuid));
        var userIcon = FindAction(LobbyMenuActions.MenuAction.UserIconViaBrain);
        Sprite userSprite = userIcon != null ? userIcon.transform.Find("Content/Background/Elements/Icon")?.GetComponent<Image>()?.sprite : null;

        int exitIndex = exitBtn.transform.GetSiblingIndex();
        // 이름(누르면 로그인 · 로그아웃 — 옛 사용자 아이콘과 같은 동작)
        var name = Clone(exitBtn.gameObject, row, "이름", exitIndex++);
        SetAction(name, LobbyMenuActions.MenuAction.UserIconViaBrain);
        ShowLabel(name, "로그인", font, 22, userSprite);
        var lbl = name.AddComponent<LobbyUserNameLabel>();
        var lso = new SerializedObject(lbl);
        lso.FindProperty("label").objectReferenceValue = name.transform.Find("Content/Background/Elements/Text/Label").GetComponent<TMP_Text>();
        lso.ApplyModifiedPropertiesWithoutUndo();
        // 도움말(?) = 조작법 익히기
        var help = Clone(exitBtn.gameObject, row, "도움말", exitIndex++);
        SetAction(help, LobbyMenuActions.MenuAction.OpenPracticeScene);
        ShowLabel(help, "?", font, 30, null);
        // 패스스루(◐)
        var pt = Clone(exitBtn.gameObject, row, "패스스루", exitIndex++);
        Object.DestroyImmediate(pt.GetComponent<LobbyMenuActions>());
        ShowLabel(pt, "◐", font, 30, null);
        var ptc = pt.AddComponent<LobbyPassthroughToggle>();
        var pso = new SerializedObject(ptc);
        var room = FindRoot("RoomEnvironment");
        pso.FindProperty("roomEnvironment").objectReferenceValue = room;
        pso.FindProperty("stateGraphic").objectReferenceValue = pt.transform.Find("Content/Background/Elements/Text/Label").GetComponent<Graphic>();
        pso.ApplyModifiedPropertiesWithoutUndo();
        // 옛 사용자 아이콘은 끈다(이름 버튼이 같은 일을 한다)
        if (userIcon != null) { Undo.RecordObject(userIcon.gameObject, "옛 아이콘"); userIcon.gameObject.SetActive(false); }

        FitHeader(row);
        EditorSceneManager.MarkSceneDirty(exitBtn.gameObject.scene);
        Debug.Log($"[LobbySetup] 머리줄: 이름 · ? · ◐ 추가 · ✕ 그대로 · 옛 사용자 아이콘 끔 · 방 {(room != null ? room.name : "없음")} · ★저장은 Ctrl+S");
    }

    /// <summary>
    /// 머리줄 줄(HorizontalLayoutGroup)은 자식 폭을 정하지 않는다 — 새 버튼 폭만큼 제목 칸을 줄여야 ✕가 판 밖으로 안 밀린다.
    /// </summary>
    [MenuItem("GuideChuna/UI 개편/④ 로비 머리줄 폭 맞추기 (lobby)")]
    public static void FitHeaderMenu()
    {
        var exitBtn = FindAction(LobbyMenuActions.MenuAction.ExitViaBrain);
        if (exitBtn != null) { FitHeader(exitBtn.transform.parent); EditorSceneManager.MarkSceneDirty(exitBtn.gameObject.scene); }
    }

    static void FitHeader(Transform row)
    {
        var exitRt = row.Find("Logout") as RectTransform;
        float iconW = exitRt != null ? exitRt.rect.width : 48f;
        float added = 0;
        foreach (var (n, w) in new[] { ("이름", 190f), ("도움말", iconW), ("패스스루", iconW) })
        {
            var rt = row.Find(n) as RectTransform;
            if (rt == null) continue;
            if (Mathf.Abs(rt.sizeDelta.x - w) > 0.5f && n == "이름") { Undo.RecordObject(rt, "폭"); rt.sizeDelta = new Vector2(w, rt.sizeDelta.y); }
            var le = rt.Find("Content/Background")?.GetComponent<LayoutElement>();
            if (le != null) { Undo.RecordObject(le, "폭"); le.preferredWidth = w; le.flexibleWidth = 0; }
            if (rt.gameObject.activeSelf) added += w;
        }
        var old = row.Find("로그인") as RectTransform;   // 끈 옛 아이콘 폭은 돌려받는다
        if (old != null && !old.gameObject.activeSelf) added -= old.rect.width;
        var title = row.Find("Title") as RectTransform;
        if (title != null && !title.name.EndsWith("*"))
        {
            float before = title.sizeDelta.x;
            var marker = title.GetComponent<LobbyHeaderFitMarker>();
            if (marker == null)
            {
                Undo.RecordObject(title, "제목 폭");
                title.sizeDelta = new Vector2(before - added, title.sizeDelta.y);
                Undo.AddComponent<LobbyHeaderFitMarker>(title.gameObject);   // 두 번 줄이지 않게
                Debug.Log($"[LobbySetup] 머리줄 폭: 새 버튼 {added:0}px만큼 제목 칸 {before:0} → {title.sizeDelta.x:0}");
            }
            else Debug.Log("[LobbySetup] 머리줄 폭은 이미 맞췄다");
        }
    }

    // ── ⑤ 모드 팝업 — 시안 A(시안 V0.7 색 그대로) · B(Meta 기본 색 · 시안의 구성 요소만) 두 벌 ──
    static readonly Color Blue = new Color(0.18f, 0.48f, 0.96f, 1f);
    static readonly Color Orange = new Color(0.90f, 0.49f, 0.13f, 1f);

    [MenuItem("GuideChuna/UI 개편/⑤ 로비 모드 팝업 A · B 만들기 (lobby)")]
    public static void ModePopupBoth()
    {
        // 이 도구가 만든 팝업만 지우고 다시 만든다(이름으로 판단) — 다른 오브젝트는 건드리지 않는다
        foreach (var p in Object.FindObjectsByType<LobbyModePopup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (p.gameObject.name.StartsWith("모드 선택 팝업")) Undo.DestroyObjectImmediate(p.gameObject);
        var a = BuildModePopup(true);
        var b = BuildModePopup(false);
        if (a == null || b == null) return;
        b.SetActive(false);   // 기본 = A. 바꾸기는 «UI 시안 A/B 바꾸기» 메뉴
        Debug.Log("[LobbySetup] 모드 팝업 A(시안) · B(Meta 기본) 생성 — 지금 켜진 것 A");
    }

    static GameObject BuildModePopup(bool variantA)
    {
        var auth = Object.FindFirstObjectByType<LobbyAuthUI_Complete>(FindObjectsInactive.Include);
        if (auth == null) { Debug.LogWarning("[LobbySetup] 로비가 아니다"); return null; }
        var lobbyPanel = auth.transform.Find("ContentUIExample-HorizonOS1");
        var backplate = Load(BackplateGuid); var primary = Load(PrimaryGuid); var secondary = Load(SecondaryGuid); var tileToggle = Load(TileToggleGuid);
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(KoreanFontGuid));
        var exitBtn = FindAction(LobbyMenuActions.MenuAction.ExitViaBrain);
        Sprite closeSprite = exitBtn != null ? exitBtn.transform.Find("Content/Background/Elements/Icon")?.GetComponent<Image>()?.sprite : null;

        const float W = 1000, H = 660;
        var root = new GameObject(variantA ? "모드 선택 팝업 A (시안)" : "모드 선택 팝업 B (Meta 기본)");
        Undo.RegisterCreatedObjectUndo(root, "모드 팝업");
        root.transform.SetParent(auth.transform, false);
        var popupComp = root.AddComponent<LobbyModePopup>();

        var panel = (GameObject)PrefabUtility.InstantiatePrefab(backplate, root.transform);
        panel.name = "판(UISet)";
        if (lobbyPanel != null) panel.transform.SetPositionAndRotation(lobbyPanel.position - lobbyPanel.forward * 0.08f, lobbyPanel.rotation);
        var canvasRoot = (RectTransform)PositionRemoteSetupTool.FindDeep(panel.transform, "CanvasRoot");
        canvasRoot.sizeDelta = new Vector2(W, H);
        var back = (RectTransform)PositionRemoteSetupTool.FindDeep(panel.transform, "UIBackplate");
        back.anchorMin = back.anchorMax = new Vector2(0.5f, 0.5f); back.anchoredPosition = Vector2.zero; back.sizeDelta = new Vector2(W, H);
        var lobbyCanvas = lobbyPanel != null ? lobbyPanel.GetComponentInChildren<Canvas>(true) : null;
        if (lobbyCanvas != null) canvasRoot.localScale *= lobbyCanvas.transform.lossyScale.x / canvasRoot.lossyScale.x;

        // 머리: 사진 · 제목 · 안내 · 닫기(✕ 아이콘 = 로비 종료 버튼과 같은 그림)
        var thumbGo = new GameObject("사진", typeof(RectTransform));
        thumbGo.transform.SetParent(back, false);
        var th = (RectTransform)thumbGo.transform; th.anchorMin = th.anchorMax = new Vector2(0.5f, 0.5f);
        th.anchoredPosition = new Vector2(-W / 2 + 40 + 75, H / 2 - 80); th.sizeDelta = new Vector2(150, 100);
        thumbGo.AddComponent<LayoutElement>().ignoreLayout = true;
        var thumb = thumbGo.AddComponent<Image>(); thumb.color = Color.white; thumb.raycastTarget = false;
        thumbGo.AddComponent<RoundedBoxUIProperties>().borderRadius = new Vector4(12, 12, 12, 12);
        var backImg = back.GetComponent<Image>();
        UseRoundMaterial(thumb, backImg);

        var titleGo = new GameObject("제목", typeof(RectTransform));
        titleGo.transform.SetParent(back, false);
        var trt = (RectTransform)titleGo.transform; trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0.5f);
        trt.anchoredPosition = new Vector2(-W / 2 + 210 + 300, H / 2 - 62); trt.sizeDelta = new Vector2(600, 56);
        titleGo.AddComponent<LayoutElement>().ignoreLayout = true;
        var title = titleGo.AddComponent<TextMeshProUGUI>();
        title.text = "시나리오"; title.fontSize = 38; title.fontStyle = FontStyles.Bold; title.color = Color.white; title.alignment = TextAlignmentOptions.Left;
        title.textWrappingMode = TextWrappingModes.NoWrap;
        if (font != null) title.font = font;
        PositionRemoteSetupTool.Label(back, "모드를 고르세요", font, 24, new Vector2(-W / 2 + 210 + 300, H / 2 - 108), new Vector2(600, 34), TextAlignmentOptions.Left);
        var close = PositionRemoteSetupTool.MakeButton(back, secondary, "", font, new Vector2(W / 2 - 60, H / 2 - 60), new Vector2(72, 72));
        var ci = close.transform.Find("Content/Background/Elements/Icon");
        if (ci != null && closeSprite != null) { ci.gameObject.SetActive(true); ci.GetComponent<Image>().sprite = closeSprite; }
        var ct = close.transform.Find("Content/Background/Elements/Text"); if (ct != null) ct.gameObject.SetActive(false);

        // 모드 4칸 — 이름 + 설명(UISet 버튼의 부제 칸)
        Button Mode(string name, string desc, bool xr, Vector2 pos)
        {
            var b = PositionRemoteSetupTool.MakeButton(back, xr ? secondary : primary, name, font, pos, new Vector2(440, 150));
            var sub = b.transform.Find("Content/Background/Elements/Text/Subtitle")?.GetComponent<TMP_Text>();
            if (sub != null) { sub.gameObject.SetActive(true); sub.text = desc; if (font != null) sub.font = font; sub.fontSize = 22; sub.alignment = TextAlignmentOptions.Center; }
            var label = b.transform.Find("Content/Background/Elements/Text/Label")?.GetComponent<TMP_Text>();
            if (label != null) label.fontSize = 34;
            var textVlg = b.transform.Find("Content/Background/Elements/Text")?.GetComponent<VerticalLayoutGroup>();
            if (textVlg != null) { textVlg.childControlHeight = true; textVlg.childForceExpandHeight = false; textVlg.spacing = 6; textVlg.childAlignment = TextAnchor.MiddleCenter; }
            var bg = b.transform.Find("Content/Background")?.GetComponent<Image>();
            if (variantA)
            {
                // A: VR = 파랑 판 · 흰 글자 / XR = 주황 테두리 / 왼쪽에 VR · XR 원 표시
                if (!xr && bg != null) { bg.color = Blue; if (label != null) label.color = Color.white; if (sub != null) sub.color = new Color(1, 1, 1, 0.8f); }
                if (xr && bg != null) { var o = bg.gameObject.AddComponent<Outline>(); o.effectColor = Orange; o.effectDistance = new Vector2(3, -3); }
                var badge = new GameObject(xr ? "XR 표시" : "VR 표시", typeof(RectTransform));
                badge.transform.SetParent(b.transform, false);
                var br = (RectTransform)badge.transform; br.anchorMin = br.anchorMax = new Vector2(0f, 0.5f);
                br.anchoredPosition = new Vector2(52, 0); br.sizeDelta = new Vector2(64, 64);
                badge.AddComponent<LayoutElement>().ignoreLayout = true;
                var bi = badge.AddComponent<Image>(); bi.color = xr ? Orange : new Color(1, 1, 1, 0.25f); bi.raycastTarget = false;
                badge.AddComponent<RoundedBoxUIProperties>().borderRadius = new Vector4(32, 32, 32, 32);
                UseRoundMaterial(bi, backImg);
                PositionRemoteSetupTool.Label(badge.transform, xr ? "XR" : "VR", font, 22, Vector2.zero, new Vector2(64, 64), TextAlignmentOptions.Center);
            }
            if (xr)
            {
                b.interactable = false;
                var tag = new GameObject("준비 중 배지", typeof(RectTransform));
                tag.transform.SetParent(b.transform, false);
                var tr = (RectTransform)tag.transform; tr.anchorMin = tr.anchorMax = new Vector2(1f, 1f);
                tr.anchoredPosition = new Vector2(-62, -28); tr.sizeDelta = new Vector2(100, 34);
                tag.AddComponent<LayoutElement>().ignoreLayout = true;
                var ti = tag.AddComponent<Image>(); ti.color = variantA ? Orange : new Color(1, 1, 1, 0.18f); ti.raycastTarget = false;
                tag.AddComponent<RoundedBoxUIProperties>().borderRadius = new Vector4(17, 17, 17, 17);
                UseRoundMaterial(ti, backImg);
                PositionRemoteSetupTool.Label(tag.transform, "준비 중", font, 20, Vector2.zero, new Vector2(100, 34), TextAlignmentOptions.Center);
                var cg = b.gameObject.AddComponent<CanvasGroup>(); cg.alpha = 0.55f;
            }
            return b;
        }
        var vrP = Mode("VR 실습", "가상환자 · 안내 따라 하기", false, new Vector2(-235, 70));
        var vrE = Mode("VR 평가", "가상환자 · 안내 없이 채점", false, new Vector2(235, 70));
        var xrP = Mode("XR 실습", "실제 사람 · 안내 따라 하기", true, new Vector2(-235, -100));
        var xrE = Mode("XR 평가", "실제 사람 · 안내 없이", true, new Vector2(235, -100));

        // 난이도(실습만)
        PositionRemoteSetupTool.Label(back, "난이도(실습)", font, 24, new Vector2(-360, -250), new Vector2(200, 40), TextAlignmentOptions.Left);
        var groupGo = new GameObject("난이도", typeof(RectTransform), typeof(ToggleGroup));
        groupGo.transform.SetParent(back, false);
        groupGo.AddComponent<LayoutElement>().ignoreLayout = true;
        var group = groupGo.GetComponent<ToggleGroup>(); group.allowSwitchOff = false;
        Toggle[] diff = new Toggle[3];
        string[] names = { "초급", "중급", "상급" };
        for (int i = 0; i < 3; i++)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(tileToggle, back);
            go.name = "난이도 " + names[i];
            var r = (RectTransform)go.transform; r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = new Vector2(-90 + i * 190, -250); r.sizeDelta = new Vector2(170, 64);
            var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>(); le.ignoreLayout = true;
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true)) if (t.name == "Icon" || t.name == "Label (1)" || t.name.StartsWith("Space")) t.gameObject.SetActive(false);
            foreach (var tmp in go.GetComponentsInChildren<TMP_Text>(true)) { tmp.text = names[i]; if (font != null) tmp.font = font; tmp.fontSize = 24; tmp.alignment = TextAlignmentOptions.Center; }
            foreach (var hlg in go.GetComponentsInChildren<HorizontalOrVerticalLayoutGroup>(true)) { hlg.childControlHeight = true; hlg.childForceExpandHeight = true; hlg.childAlignment = TextAnchor.MiddleCenter; }
            diff[i] = go.GetComponent<Toggle>();
            diff[i].group = group; diff[i].isOn = i == 0;
            if (variantA)
            {
                var tint = go.AddComponent<ToggleTint>();
                var tso = new SerializedObject(tint);
                tso.FindProperty("target").objectReferenceValue = go.transform.Find("Content/Background")?.GetComponent<Image>();
                tso.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        var so = new SerializedObject(popupComp);
        so.FindProperty("panel").objectReferenceValue = panel;
        so.FindProperty("titleText").objectReferenceValue = title;
        so.FindProperty("thumbnail").objectReferenceValue = thumb;
        // ★10-28 팝업이 설정 창 꼴(드롭다운)로 바뀌어 XR 버튼 · 난이도 토글 필드가 없어졌다 — 이 옛 A/B 판은 실습 · 평가만 물린다(난이도 = 초급 고정)
        so.FindProperty("practiceButton").objectReferenceValue = vrP;
        so.FindProperty("evaluationButton").objectReferenceValue = vrE;
        if (xrP != null) xrP.interactable = false;
        if (xrE != null) xrE.interactable = false;
        so.FindProperty("closeButton").objectReferenceValue = close;
        so.ApplyModifiedPropertiesWithoutUndo();

        panel.SetActive(false);
        EditorSceneManager.MarkSceneDirty(root.scene);
        return root;
    }

    /// <summary>
    /// 로비 카드 제목 · 부제를 화면 이름 표(<see cref="ScenarioDisplayNames"/>)로 맞춘다 (10-27 «명칭 통일»).
    /// ★카드 번호 → 술기는 짐작하지 않는다 — TrainingScene의 ScenarioBootstrapper.scenarioConfigs 배열(씬 파일)을 읽어 번호마다 내부 이름을 얻는다.
    /// 씬을 따로 지정한 카드(실측 기록 씬)는 표 대상이 아니라 건드리지 않는다. 바꾼 것은 전부 로그로 남긴다.
    /// </summary>
    [MenuItem("GuideChuna/UI 개편/⑥ 로비 카드 명칭 통일 (lobby)")]
    public static void UnifyCardNames()
    {
        string scenePath = "Assets/Scenes/TrainingScene.unity";
        var text = System.IO.File.ReadAllText(scenePath);
        int at = text.IndexOf("  scenarioConfigs:\n", System.StringComparison.Ordinal);
        if (at < 0) at = text.IndexOf("  scenarioConfigs:\r\n", System.StringComparison.Ordinal);
        if (at < 0) { Debug.LogWarning("[LobbySetup] TrainingScene에서 scenarioConfigs를 못 찾았다"); return; }
        var names = new System.Collections.Generic.List<string>();
        foreach (var line in text.Substring(at).Split('\n'))
        {
            if (names.Count > 0 && !line.TrimStart().StartsWith("- {")) break;
            var m = System.Text.RegularExpressions.Regex.Match(line, @"guid: ([0-9a-f]{32})");
            if (!m.Success) continue;
            var cfg = AssetDatabase.LoadAssetAtPath<ScenarioConfig>(AssetDatabase.GUIDToAssetPath(m.Groups[1].Value));
            names.Add(cfg != null ? cfg.scenarioName : null);
        }
        Debug.Log($"[LobbySetup] TrainingScene 시나리오 배열 {names.Count}개: {string.Join(" · ", names)}");

        int changed = 0;
        foreach (var btn in Object.FindObjectsByType<ScenarioLaunchButton>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var so = new SerializedObject(btn);
            int idx = so.FindProperty("scenarioIndex").intValue;
            string sceneOverride = so.FindProperty("sceneOverride").stringValue;
            if (!string.IsNullOrWhiteSpace(sceneOverride) || idx < 0 || idx >= names.Count) continue;
            string key = names[idx];
            if (!ScenarioDisplayNames.Has(key)) { Debug.LogWarning($"[LobbySetup] 카드 {idx} '{key}' — 이름 표에 없다, 그대로 둔다"); continue; }
            var el = btn.transform.Find("Content/Background/Elements");
            var title = el?.Find("Label")?.GetComponent<TMP_Text>();
            var sub = el?.Find("Label (1)")?.GetComponent<TMP_Text>();
            string t = ScenarioDisplayNames.Title(key), s = ScenarioDisplayNames.Subtitle(key);
            if (title != null && title.text != t) { Undo.RecordObject(title, "명칭"); Debug.Log($"[LobbySetup] 카드 {idx} 제목 '{title.text.Replace("\n", "⏎")}' → '{t}'"); title.text = t; changed++; }
            if (sub != null && sub.text != s) { Undo.RecordObject(sub, "명칭"); Debug.Log($"[LobbySetup] 카드 {idx} 부제 '{sub.text.Replace("\n", "⏎")}' → '{s}'"); sub.text = s; changed++; }
            var card = btn.GetComponent<LobbyItemCard>();
            if (card != null)
            {
                var cso = new SerializedObject(card);
                var dn = cso.FindProperty("displayName");
                if (dn != null && !string.IsNullOrEmpty(dn.stringValue) && dn.stringValue != t) { dn.stringValue = t; cso.ApplyModifiedProperties(); }
            }
        }
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log($"[LobbySetup] 명칭 통일 — 글자 {changed}곳 바꿈 · ★저장은 Ctrl+S");
    }

    /// <summary>모드 팝업 판만 찍는다(꺼져 있어도).</summary>
    [MenuItem("GuideChuna/UI 개편/로비 모드 팝업 미리보기 찍기")]
    public static void CapturePopup()
    {
        foreach (var p in Object.FindObjectsByType<LobbyModePopup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var panel = p.transform.Find("판(UISet)");
            if (panel == null) continue;
            var pso = new SerializedObject(p);
            var thumb = pso.FindProperty("thumbnail").objectReferenceValue as Image;
            var title = pso.FindProperty("titleText").objectReferenceValue as TMP_Text;
            var card = Object.FindFirstObjectByType<LobbyItemCard>(FindObjectsInactive.Include);
            var photo = card != null ? card.transform.Find("Content/Background/사진")?.GetComponent<Image>() : null;
            Sprite oldS = thumb != null ? thumb.sprite : null; string oldT = title != null ? title.text : null;
            if (thumb != null && photo != null) thumb.sprite = photo.sprite;      // 미리보기용으로 첫 카드 사진 · 이름을 잠깐 넣는다
            if (title != null && card != null) title.text = card.DisplayName;
            bool rootWas = p.gameObject.activeSelf; p.gameObject.SetActive(true);
            UIPanelPreviewCapture.Capture(panel.gameObject, p.gameObject.name.Contains(" A") ? "로비_모드팝업_A" : "로비_모드팝업_B");
            p.gameObject.SetActive(rootWas);
            if (thumb != null) thumb.sprite = oldS;
            if (title != null) title.text = oldT;
        }
    }

    // ── 도우미 ──
    /// <summary>★RoundedBoxUIProperties는 UISet 둥근 재질이 있어야 모서리가 둥글다(재질 없이 붙이면 네모로 나온다 — 10-27 캡처로 확인).</summary>
    internal static void UseRoundMaterial(Image img, Image source)
    {
        if (img != null && source != null && source.material != null) img.material = source.material;
    }

    static GameObject Load(string guid) => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));

    static LobbyMenuActions FindAction(LobbyMenuActions.MenuAction a)
    {
        foreach (var m in Object.FindObjectsByType<LobbyMenuActions>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var so = new SerializedObject(m);
            if (so.FindProperty("action").enumValueIndex == (int)a && m.GetComponentInParent<LobbyAuthUI_Complete>(true) != null
                && m.transform.parent != null && m.transform.parent.name == "Horizontal") return m;
        }
        return null;
    }

    static GameObject Clone(GameObject src, Transform parent, string name, int index)
    {
        var go = Object.Instantiate(src, parent);
        Undo.RegisterCreatedObjectUndo(go, name);
        go.name = name;
        go.transform.SetSiblingIndex(index);
        return go;
    }

    static void SetAction(GameObject go, LobbyMenuActions.MenuAction a)
    {
        var m = go.GetComponent<LobbyMenuActions>();
        var so = new SerializedObject(m);
        so.FindProperty("action").enumValueIndex = (int)a;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // 버튼 안 글자를 켠다. icon이 있으면 아이콘도 같이(이름), 없으면 아이콘을 끄고 글자만(? · ◐)
    static void ShowLabel(GameObject go, string text, TMP_FontAsset font, float size, Sprite icon)
    {
        var el = go.transform.Find("Content/Background/Elements");
        var iconT = el.Find("Icon");
        if (iconT != null) { iconT.gameObject.SetActive(icon != null); if (icon != null) iconT.GetComponent<Image>().sprite = icon; }
        var gap = el.Find("Gap"); if (gap != null) gap.gameObject.SetActive(icon != null);
        var textT = el.Find("Text"); if (textT != null) textT.gameObject.SetActive(true);
        var label = el.Find("Text/Label")?.GetComponent<TMP_Text>();
        if (label != null) { label.text = text; if (font != null) label.font = font; label.fontSize = size; label.textWrappingMode = TextWrappingModes.NoWrap; }
    }

    static GameObject FindRoot(string name)
    {
        foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == name) return go;
        return null;
    }
}
