using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using K = HzUIKit;

/// <summary>
/// 로비 V0.9 조립 (2026-10-27 사용자 «Meta 파랑으로 하고 V0.9대로 구현» · 시안 V0.9 그림 1 ~ 4).
///   ① 모드 선택 팝업 640 × 480  ② 로그인 안내 대화상자 384 × 260  ③ 조 · 이름 선택 판 640 × 560 + 타일 프리팹  ④ 카드 · 구역 글자 크기
/// 옛 판(로그인 안내 · 조 · 이름)은 지우지 않는다 — 배선만 새 판으로 옮기고 꺼 둔다. 옛 모드 팝업(A/B)은 이 도구가 만든 것이라 지우고 새로 만든다.
/// 자리는 로비 판 앞 8cm · 같은 방향 · 로비 판과 같은 실제 배율. ★저장은 사람이 한다.
/// </summary>
public static class LobbyV09Tool
{
    const string PrefabDir = "Assets/Prefabs/UI/LoginV09";

    [MenuItem("GuideChuna/UI 개편/V0.9 로비 전부 조립 (lobby)")]
    public static void BuildAll()
    {
        var auth = Object.FindFirstObjectByType<LobbyAuthUI_Complete>(FindObjectsInactive.Include);
        if (auth == null) { Debug.LogWarning("[V09Lobby] 로비가 아니다"); return; }
        BuildModePopup(auth);
        BuildLoginNotice(auth);
        BuildLoginOnePanel(auth);   // 10-28 두 단계 판(BuildSelectPanels) → 한 판
        RestyleCards();
        EditorSceneManager.MarkSceneDirty(auth.gameObject.scene);
        Debug.Log("[V09Lobby] 로비 V0.9 조립 끝 · ★저장은 Ctrl+S");
    }

    // 모드 팝업만 다시 짠다(10-28 설정 창 꼴) — 로그인 판 · 카드는 건드리지 않는다
    [MenuItem("GuideChuna/UI 개편/모드 팝업만 다시 조립 — 설정 창 꼴 (lobby)")]
    public static void BuildModePopupOnly()
    {
        var auth = Object.FindFirstObjectByType<LobbyAuthUI_Complete>(FindObjectsInactive.Include);
        if (auth == null) { Debug.LogWarning("[V09Lobby] 로비가 아니다"); return; }
        BuildModePopup(auth);
        EditorSceneManager.MarkSceneDirty(auth.gameObject.scene);
        Debug.Log("[V09Lobby] 모드 팝업만 다시 조립 끝 · ★저장은 Ctrl+S");
    }

    // 로그인 한 판(10-28 · 시안 V1.4) + 로그인 안내 — ★둘 다 로비 판 앞 8cm(모드 팝업과 같은 자리 · 사용자 «로그인 팝업 너무 가깝다»)
    [MenuItem("GuideChuna/UI 개편/로그인 한 판 + 안내 다시 조립 (lobby)")]
    public static void BuildLoginOnly()
    {
        var auth = Object.FindFirstObjectByType<LobbyAuthUI_Complete>(FindObjectsInactive.Include);
        if (auth == null) { Debug.LogWarning("[V09Lobby] 로비가 아니다"); return; }
        BuildLoginNotice(auth);
        BuildLoginOnePanel(auth);
        LogEyeDistance("로비 판", LobbyPanel(auth));
        EditorSceneManager.MarkSceneDirty(auth.gameObject.scene);
        Debug.Log("[V09Lobby] 로그인 한 판 + 안내 다시 조립 끝 · ★저장은 Ctrl+S");
    }

    const float PopupForward = 0.08f;   // 로비 판 앞(모드 팝업과 같음)

    // 눈 ↔ 판 거리(실측 로그) — 에디터의 CenterEyeAnchor 기준(기기에선 머리 위치에 따라 다르다)
    static void LogEyeDistance(string name, Transform t)
    {
        var eye = GameObject.Find("CenterEyeAnchor");
        if (eye == null) { Debug.Log($"[V09Lobby] {name}: CenterEyeAnchor 없음 — 거리 못 잼"); return; }
        Debug.Log($"[V09Lobby] {name}: 눈에서 {Vector3.Distance(eye.transform.position, t.position):F2} m");
    }

    // ── ③' 로그인 한 판: 왼쪽 조 목록(세로 · 고른 조 흰 알약) + 오른쪽 이름 격자(4열) · 이름 누르면 바로 로그인 ──
    static void BuildLoginOnePanel(LobbyAuthUI_Complete auth)
    {
        // 10-29 사용자 «가운데 경계 줄 대신 두 리스트를 각각 감싼 테두리» — 목록 칸 크기는 그대로 두고 상자 안쪽 여백(BoxPad)만큼 판을 키운다(로비와 같은 A안)
        //   종전 800 × 520 · 세로 구분선 · GapX 24
        const float Pad = 24, TopY = 88, ColW = 208, GradeH = 44, GradeGap = 6, GapX = 16, Gap = 12, NameH = 64, BoxPad = 12, GridW = 508, ListH = 408;
        const int Cols = 4;
        const float GBoxW = ColW + 2 * BoxPad, NBoxX = Pad + GBoxW + GapX, NBoxW = GridW + 2 * BoxPad, BoxH = ListH + 2 * BoxPad;
        const float W = NBoxX + NBoxW + Pad, H = TopY + BoxH + Pad;   // 828 × 544
        const float GridX = NBoxX + BoxPad;   // 스크롤 막대(+4 · 폭 4)는 오른쪽 안쪽 여백 12 안에 든다
        float nameW = Mathf.Floor((GridW - (Cols - 1) * Gap) / Cols);

        Directory.CreateDirectory(PrefabDir);
        var gradeItem = MakeGradeItemPrefab("조항목_V14", (ColW - GradeGap) / 2f, GradeH);
        var nameTile = MakeNameTilePrefab("이름타일_V14", nameW, NameH);

        foreach (var n in new[] { "조 선택 V0.9", "이름 선택 V0.9" })   // 이 도구가 만든 옛 두 단계 판은 지운다
            foreach (Transform t in auth.transform) if (t.name == n) { Undo.DestroyObjectImmediate(t.gameObject); break; }
        var root = NewRoot(auth, "로그인 한 판 V1.4", PopupForward);
        var back = K.Plate(root, W, H, Local(auth));
        K.Text(back, "제목", Pad, 24, 400, 30, "로그인", 24, K.Text1, true);
        K.Text(back, "안내", Pad, 58, 500, 22, "조를 고르고 이름을 누르세요", 14, K.Text2);
        var close = IconBtn(back, "닫기", W - 24 - 40, 19, CloseSprite(), "X");   // 10-28 오른쪽 24 · 제목 줄(24 + 30/2) 가운데
        UnityEventTools.AddPersistentListener(close.onClick, auth.CloseSelectionPanels);
        K.Frame(back, "조 목록 바탕", Pad, TopY, GBoxW, BoxH);      // 목록보다 먼저 = 뒤에 그린다
        K.Frame(back, "이름 목록 바탕", NBoxX, TopY, NBoxW, BoxH);

        var gContent = ScrollList(back, "조 목록", Pad + BoxPad, TopY + BoxPad, ColW, ListH, new Vector2((ColW - GradeGap) / 2f, GradeH), 2, GradeGap, false);   // 10-28 2열 · 15조(8줄 = 394)까지 스크롤 없이
        var uContent = ScrollList(back, "이름 목록", GridX, TopY + BoxPad, GridW, ListH, new Vector2(nameW, NameH), Cols, Gap, true);
        AddTextFit(root, back);

        var so = new SerializedObject(auth);
        foreach (var f in new[] { "gradeSelectionPanel", "userSelectionPanel" })
        {
            var old = so.FindProperty(f).objectReferenceValue as GameObject;
            if (old != null && old != root) { Undo.RecordObject(old, "옛 판"); old.SetActive(false); }
        }
        // ★두 판에 같은 오브젝트 = 한 판(LobbyAuthUI_Complete.IsOnePanelLogin)
        so.FindProperty("gradeSelectionPanel").objectReferenceValue = root;
        so.FindProperty("gradeSelectionBackground").objectReferenceValue = null;
        so.FindProperty("gradeBackButton").objectReferenceValue = null;
        so.FindProperty("gradeContentContainer").objectReferenceValue = gContent;
        so.FindProperty("gradeButtonPrefab").objectReferenceValue = gradeItem;
        so.FindProperty("userSelectionPanel").objectReferenceValue = root;
        so.FindProperty("userSelectionBackground").objectReferenceValue = null;
        so.FindProperty("userBackButton").objectReferenceValue = null;
        so.FindProperty("userContentContainer").objectReferenceValue = uContent;
        so.FindProperty("userButtonPrefab").objectReferenceValue = nameTile;
        so.ApplyModifiedProperties();
        LogEyeDistance("로그인 한 판", root.transform);
        root.SetActive(false);
        Debug.Log($"[V09Lobby] ③' 로그인 한 판 {W} × {H} · 조 목록 2열 {(ColW - GradeGap) / 2f} × {GradeH} · 이름 {Cols}열 {nameW} × {NameH} · 로비 판 앞 {PopupForward} m");
    }

    // OS 텍스트 크기로 글자가 칸을 넘치면 판을 세로로 늘린다(10-29 사용자) — host = 켜고 끄는 오브젝트(열 때마다 한 번)
    static void AddTextFit(GameObject host, RectTransform back)
    {
        var fit = host.AddComponent<PopupTextFit>();
        var fso = new SerializedObject(fit);
        fso.FindProperty("back").objectReferenceValue = back;
        fso.FindProperty("canvasRoot").objectReferenceValue = back.parent;
        fso.ApplyModifiedPropertiesWithoutUndo();
    }

    // 판 안 세로 스크롤 목록(Quest: 가는 막대) — 반환 = 버튼이 들어갈 Content
    static RectTransform ScrollList(RectTransform back, string name, float x, float y, float w, float h, Vector2 cell, int cols, float gap, bool bar)
    {
        var view = K.Rect(back, name, x, y, w, h);
        view.gameObject.AddComponent<RectMask2D>();
        var hit = view.gameObject.AddComponent<Image>(); hit.color = new Color(1, 1, 1, 0f);   // 끌기 판정용
        var content = K.Rect(view, "Content", 0, 0, w, 10);
        var grid = content.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = cell; grid.spacing = new Vector2(gap, gap);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = cols;
        var sr = view.gameObject.AddComponent<ScrollRect>();
        sr.content = content; sr.horizontal = false; sr.vertical = true; sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 20;
        if (bar)
        {
            var track = K.Box(back, name + " 스크롤 막대", x + w + 4, y, 4, h, K.Secondary, 2);
            var sb = track.gameObject.AddComponent<Scrollbar>(); sb.direction = Scrollbar.Direction.BottomToTop;
            var area = K.Rect(track.transform, "Sliding Area", 0, 0, 4, h);
            var handle = K.Box(area, "Handle", 0, 0, 4, 120, K.Text2, 2);
            var hrt = (RectTransform)handle.transform; hrt.anchorMin = Vector2.zero; hrt.anchorMax = Vector2.one; hrt.offsetMin = hrt.offsetMax = Vector2.zero;
            sb.handleRect = hrt; sb.targetGraphic = handle;
            sr.verticalScrollbar = sb; sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }
        return content;
    }

    // 조 항목 — 바탕 없는 알약(고르면 GradeSelectionHandler.MarkSelected가 흰 알약으로 칠한다) · ★첫 TMP = 조 이름
    static GameObject MakeGradeItemPrefab(string name, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform));
        ((RectTransform)go.transform).sizeDelta = new Vector2(w, h);
        var img = go.AddComponent<Image>(); img.color = new Color(1, 1, 1, 0f); img.material = K.RoundMat;
        go.AddComponent<RoundedBoxUIProperties>().borderRadius = Vector4.one * (h / 2f);
        var b = go.AddComponent<Button>(); b.targetGraphic = img;
        go.AddComponent<LayoutElement>();
        K.Text(go.transform, "Label", 12, 0, w - 24, h, "1조", 16, K.Text1, true, TextAlignmentOptions.Center, true);   // 10-28 인원수 뺌(사용자)
        return SavePrefab(go, name);
    }

    // 이름 타일 — 옅은 칸 · 이름만(긴 이름 두 줄 · 말줄임 금지) · ★첫 TMP = 이름
    static GameObject MakeNameTilePrefab(string name, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform));
        ((RectTransform)go.transform).sizeDelta = new Vector2(w, h);
        var img = go.AddComponent<Image>(); img.color = K.Tile; img.material = K.RoundMat;
        go.AddComponent<RoundedBoxUIProperties>().borderRadius = Vector4.one * 16;
        var b = go.AddComponent<Button>(); b.targetGraphic = img;
        var cb = b.colors; cb.highlightedColor = new Color(1.4f, 1.4f, 1.4f, 1.6f); cb.pressedColor = new Color(1.8f, 1.8f, 1.8f, 2f); cb.colorMultiplier = 1.5f; b.colors = cb;
        go.AddComponent<LayoutElement>();
        K.Text(go.transform, "Label", 12, 6, w - 24, h - 12, "이름", 16, K.Text1, true, TextAlignmentOptions.Center, true);
        return SavePrefab(go, name);
    }

    static GameObject SavePrefab(GameObject go, string name)
    {
        foreach (var le in go.GetComponentsInChildren<LayoutElement>(true)) if (le.gameObject != go) le.ignoreLayout = true;
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabDir}/{name}.prefab");
        Object.DestroyImmediate(go);
        return prefab;
    }

    // ── 자리 · 배율: 로비 판 앞 8cm ──
    static Transform LobbyPanel(LobbyAuthUI_Complete auth) => auth.transform.Find("ContentUIExample-HorizonOS1");
    static float LobbyScale(LobbyAuthUI_Complete auth)
    {
        var c = LobbyPanel(auth)?.GetComponentInChildren<Canvas>(true);
        return c != null ? c.transform.lossyScale.x : 0.00075f;
    }
    static GameObject NewRoot(LobbyAuthUI_Complete auth, string name, float forward = 0.08f)
    {
        foreach (Transform t in auth.transform) if (t.name == name) { Undo.DestroyObjectImmediate(t.gameObject); break; }
        var root = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(root, name);
        root.transform.SetParent(auth.transform, false);
        var lp = LobbyPanel(auth);
        if (lp != null) root.transform.SetPositionAndRotation(lp.position - lp.forward * forward, lp.rotation);
        root.transform.localScale = Vector3.one;
        return root;
    }
    static float Local(LobbyAuthUI_Complete auth) => LobbyScale(auth) / Mathf.Max(1e-6f, auth.transform.lossyScale.x);

    static Sprite CloseSprite()
    {
        foreach (var m in Object.FindObjectsByType<LobbyMenuActions>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (new SerializedObject(m).FindProperty("action").enumValueIndex == (int)LobbyMenuActions.MenuAction.ExitViaBrain)
            {
                var img = m.transform.Find("Content/Background/Elements/Icon")?.GetComponent<Image>();
                if (img != null && img.sprite != null) return img.sprite;
            }
        return null;
    }

    // 테두리 없는 48 × 48 아이콘 버튼(✕ · ‹) — ✕는 로비 종료 버튼 그림(한글 글꼴엔 ✕ 글자가 없다)
    static Button IconBtn(Transform parent, string name, float x, float y, Sprite icon, string glyph)
    {
        var bg = K.Box(parent, name, x, y, 40, 40, Color.white, 20);   // 10-28 로비 X와 같은 40×40 원
        var b = bg.gameObject.AddComponent<Button>(); b.targetGraphic = bg;
        var cb = b.colors;   // 10-28 로비 X 색(UISet ToggleButton_Dark 실측) — 종전 알파 0 바탕은 가리켜도 원이 안 보였다
        cb.normalColor = cb.selectedColor = K.Hex("#4B4B4B"); cb.highlightedColor = K.Hex("#5D5D5D"); cb.pressedColor = K.Hex("#6F6F6F"); cb.disabledColor = K.Hex("#444444");
        cb.colorMultiplier = 1f; cb.fadeDuration = 0.1f; b.colors = cb;
        if (icon != null)
        {
            var ic = K.Rect(bg.transform, "아이콘", 8, 8, 24, 24).gameObject.AddComponent<Image>();
            ic.sprite = icon; ic.color = K.Text1; ic.raycastTarget = false; ic.preserveAspect = true;
        }
        else K.Text(bg.transform, "아이콘", 0, 0, 40, 40, glyph, 24, K.Text1, true, TextAlignmentOptions.Center);
        return b;
    }

    // ── ① 모드 선택 팝업 ──
    static void BuildModePopup(LobbyAuthUI_Complete auth)
    {
        foreach (var p in Object.FindObjectsByType<LobbyModePopup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (p.gameObject.name.StartsWith("모드 선택 팝업")) Undo.DestroyObjectImmediate(p.gameObject);
        // ★10-28 Quest 설정 창 꼴(사용자 «타이틀 아래 설명 · 우측 드롭박스» · 참조 = v2.7 설정 창 Text size 행)
        //   행 카드(#424242 r16) 안에 행 2개(높이 88 · 제목 16 굵게 / 설명 14 보조색) · 오른쪽 드롭다운 알약 176 × 40
        //   아래 [실습](흰 알약) · [평가](보조 알약)를 누르면 바로 진입
        // 10-29 사용자 «실습 · 평가 버튼을 위아래로 두껍게 + 위쪽에 학습 모드 선택 · 설명 텍스트»(시안 확인) — 종전 BtnH 52 · 버튼이 행 카드 바로 아래
        const float W = 640, Pad = 24, RowH = 88, DdW = 128, DdH = 40, BtnH = 88;   // 10-29 사용자 «드롭박스 좌우 공간 많이 남으니 줄여» DdW 176 → 128(가장 긴 «초급» · «VR» 글자 + 화살표)
        const float CardY = Pad + 54 + 24, CardH = 2 * RowH, ModeY = CardY + CardH + 24, ModeDescY = ModeY + 28, BtnY = ModeDescY + 44 + 12, H = BtnY + BtnH + Pad;
        var root = NewRoot(auth, "모드 선택 팝업");
        var comp = root.AddComponent<LobbyModePopup>();
        var holder = new GameObject("판"); holder.transform.SetParent(root.transform, false);
        var back = K.Plate(holder, W, H, Local(auth));

        var thumb = K.Box(back, "사진", Pad, Pad, 96, 54, Color.white, 8); thumb.raycastTarget = false;
        var title = K.Text(back, "제목", 136, Pad, 420, 30, "시나리오", 24, K.Text1, true);
        var sub = K.Text(back, "부제", 136, 58, 440, 22, "", 14, K.Text2);
        var close = IconBtn(back, "닫기", W - 24 - 40, 19, CloseSprite(), "X");   // 10-28 오른쪽 24 · 제목 줄(24 + 30/2) 가운데

        var card = K.Box(back, "행 카드", Pad, CardY, W - 2 * Pad, CardH, K.Tile, 16); card.raycastTarget = false;
        var line = K.Box(back, "구분선", Pad + 24, CardY + RowH, W - 2 * Pad - 48, 1, new Color(1, 1, 1, 0.08f), 0); line.raycastTarget = false;

        // 행 = 제목 · 설명 · 오른쪽 드롭다운. 드롭다운은 판(back) 바로 아래 자식 — 펼칠 때 맨 뒤 형제로 올라 다른 행 위에 그린다
        SettingDropdown Row(int i, string name, string desc, string[] opts, bool[] off)
        {
            float y = CardY + i * RowH;
            K.Text(back, name + " 제목", Pad + 24, y + 18, 340, 24, name, 16, K.Text1, true);
            K.Text(back, name + " 설명", Pad + 24, y + 18 + 26, W - Pad - 24 - DdW - 24 - (Pad + 24), 40, desc, 14, K.Text2, false, TextAlignmentOptions.TopLeft, true);

            float dx = W - Pad - 24 - DdW, dy = y + (RowH - DdH) / 2f;
            var box = K.Rect(back, name + " 드롭다운", dx, dy, DdW, DdH);
            var dd = box.gameObject.AddComponent<SettingDropdown>();
            var head = K.Btn(box, "머리", 0, 0, DdW, DdH, "", K.Style.Secondary);
            Object.DestroyImmediate(head.transform.Find("글자").gameObject);
            var cap = K.Text(head.transform, "글자", 20, 0, DdW - 56, DdH, opts[0], 14, K.Text1, true);
            K.Text(head.transform, "펼침", DdW - 20 - 16, 0, 16, DdH, "▼", 10, K.Text1, false, TextAlignmentOptions.Center);

            const float OptH = 40, ListPad = 8;
            var list = K.Box(box, "목록", 0, DdH + 8, DdW, opts.Length * OptH + 2 * ListPad, K.Hex("#2A2A2A"), 16);
            var btns = new Button[opts.Length];
            for (int k = 0; k < opts.Length; k++)
            {
                btns[k] = K.Btn(list.transform, "항목 " + opts[k], ListPad, ListPad + k * OptH, DdW - 2 * ListPad, OptH, opts[k], K.Style.Borderless);
                if (off[k]) btns[k].gameObject.AddComponent<CanvasGroup>().alpha = 0.4f;   // 막힌 항목(준비 중)은 흐리게
            }
            list.gameObject.SetActive(false);

            var dso = new SerializedObject(dd);
            dso.FindProperty("head").objectReferenceValue = head;
            dso.FindProperty("caption").objectReferenceValue = cap;
            dso.FindProperty("list").objectReferenceValue = list.gameObject;
            var po = dso.FindProperty("options"); po.arraySize = opts.Length;
            var pl = dso.FindProperty("labels"); pl.arraySize = opts.Length;
            var pd = dso.FindProperty("disabled"); pd.arraySize = opts.Length;
            for (int k = 0; k < opts.Length; k++)
            {
                po.GetArrayElementAtIndex(k).objectReferenceValue = btns[k];
                pl.GetArrayElementAtIndex(k).stringValue = opts[k];
                pd.GetArrayElementAtIndex(k).boolValue = off[k];
            }
            dso.FindProperty("selectedFill").colorValue = K.White;
            dso.FindProperty("selectedInk").colorValue = K.Ink;
            dso.FindProperty("normalInk").colorValue = K.Text1;
            dso.ApplyModifiedPropertiesWithoutUndo();
            return dd;
        }
        var diff = Row(0, "난이도", "단계마다 나오는 안내를 얼마나 자세히 들을지 정합니다.",
                       new[] { "초급", "중급", "상급" }, new[] { false, false, false });
        var env = Row(1, "환경", "가상환자(VR)로 할지 실제 사람(XR)과 할지 정합니다.",
                      new[] { "VR", "XR · 준비 중" }, new[] { false, true });

        K.Text(back, "학습 모드 제목", Pad, ModeY, 400, 24, "학습 모드 선택", 16, K.Text1, true);
        K.Text(back, "학습 모드 설명", Pad, ModeDescY, W - 2 * Pad, 44, "실습은 안내를 따라 연습하고,\n평가는 안내 없이 진행해 점수를 남깁니다.", 14, K.Text2, false, TextAlignmentOptions.TopLeft, true);

        float bw = (W - 2 * Pad - 12) / 2f;
        var practice = K.Btn(back, "실습", Pad, BtnY, bw, BtnH, "실습", K.Style.Secondary, null, 16);   // 10-30 사용자 «실습 · 평가 디자인 통일 — 흰 알약이 선택처럼 헷갈린다»
        var evaluation = K.Btn(back, "평가", Pad + bw + 12, BtnY, bw, BtnH, "평가", K.Style.Secondary, null, 16);
        diff.transform.SetAsLastSibling(); env.transform.SetAsLastSibling();   // 목록이 버튼 위에 그려지게

        var so = new SerializedObject(comp);
        so.FindProperty("panel").objectReferenceValue = holder;
        so.FindProperty("titleText").objectReferenceValue = title;
        so.FindProperty("subtitleText").objectReferenceValue = sub;
        so.FindProperty("thumbnail").objectReferenceValue = thumb;
        so.FindProperty("practiceButton").objectReferenceValue = practice;
        so.FindProperty("evaluationButton").objectReferenceValue = evaluation;
        so.FindProperty("difficultyDropdown").objectReferenceValue = diff;
        so.FindProperty("environmentDropdown").objectReferenceValue = env;
        so.FindProperty("closeButton").objectReferenceValue = close;
        so.ApplyModifiedPropertiesWithoutUndo();
        AddTextFit(holder, back);
        holder.SetActive(false);
        Debug.Log($"[V09Lobby] ① 모드 팝업 {W} × {H} — 설정 창 꼴(행 2 · 드롭다운 {DdW} × {DdH} · [실습]/[평가] {bw} × {BtnH})");
    }

    // ── ② 로그인 안내 대화상자 ──
    static void BuildLoginNotice(LobbyAuthUI_Complete auth)
    {
        const float W = 384, H = 260;
        var root = NewRoot(auth, "로그인 안내 V0.9", PopupForward);   // 10-28 0.3 → 0.08m(사용자 «너무 가깝다»)
        var back = K.Plate(root, W, H, Local(auth));
        var circle = K.Box(back, "아이콘", W / 2 - 24, 24, 48, 48, K.Yellow, 24); circle.raycastTarget = false;
        K.Text(circle.transform, "!", 0, 0, 48, 48, "!", 28, K.Ink, true, TextAlignmentOptions.Center);
        K.Text(back, "제목", 24, 24 + 48 + 12, W - 48, 28, "로그인이 필요합니다", 20, K.Text1, true, TextAlignmentOptions.Center);
        K.Text(back, "본문", 24, 24 + 48 + 12 + 32, W - 48, 22, "실습 기록을 남기려면 조와 이름을 고르세요", 14, K.Text2, false, TextAlignmentOptions.Center, true);   // 10-29 줄바꿈 — OS 글자 ×1.5에서 가로 12 넘쳤다
        AddTextFit(root, back);
        float by = H - 24 - 48;
        // [로그인] — 브레인이 Toggle로 받는다(loginRequiredCloseButton) · 모멘터리로 다룬다
        var bg = K.Box(back, "로그인", 24, by, 164, 48, K.Blue, 24);
        var login = bg.gameObject.AddComponent<Toggle>(); login.targetGraphic = bg; login.isOn = false;
        K.Text(bg.transform, "글자", 0, 0, 164, 48, "로그인", 14, K.Ink, true, TextAlignmentOptions.Center);
        var close = K.Btn(back, "닫기", 24 + 164 + 8, by, 164, 48, "닫기", K.Style.Secondary);
        UnityEventTools.AddPersistentListener(close.onClick, auth.CloseLoginRequiredPopup);

        var so = new SerializedObject(auth);
        var old = so.FindProperty("loginRequiredPopup").objectReferenceValue as GameObject;
        if (old != null && old != root) { Undo.RecordObject(old, "옛 팝업"); old.SetActive(false); }
        so.FindProperty("loginRequiredPopup").objectReferenceValue = root;
        so.FindProperty("loginRequiredCloseButton").objectReferenceValue = login;
        so.ApplyModifiedProperties();
        LogEyeDistance("로그인 안내", root.transform);
        root.SetActive(false);
        Debug.Log($"[V09Lobby] ② 로그인 안내 384 × 260 · [로그인] Meta 파랑 · 옛 팝업 '{(old != null ? old.name : "none")}' 꺼 둠");
    }

    // ── ③ 조 · 이름 선택 ──
    static void BuildSelectPanels(LobbyAuthUI_Complete auth)
    {
        Directory.CreateDirectory(PrefabDir);
        var gradeTile = MakeTilePrefab("조타일_V09", 188, 100, false);
        var nameTile = MakeTilePrefab("이름타일_V09", 188, 64, true);
        var g = BuildSelect(auth, "조 선택 V0.9", "로그인", 0, 100, out var gBack, out var gContent);
        var u = BuildSelect(auth, "이름 선택 V0.9", "이름 고르기", 1, 64, out var uBack, out var uContent);

        var so = new SerializedObject(auth);
        foreach (var f in new[] { "gradeSelectionPanel", "userSelectionPanel" })
        {
            var old = so.FindProperty(f).objectReferenceValue as GameObject;
            if (old != null && old != g && old != u) { Undo.RecordObject(old, "옛 판"); old.SetActive(false); }
        }
        so.FindProperty("gradeSelectionPanel").objectReferenceValue = g;
        so.FindProperty("gradeSelectionBackground").objectReferenceValue = null;
        so.FindProperty("gradeBackButton").objectReferenceValue = gBack;
        so.FindProperty("gradeContentContainer").objectReferenceValue = gContent;
        so.FindProperty("gradeButtonPrefab").objectReferenceValue = gradeTile;
        so.FindProperty("userSelectionPanel").objectReferenceValue = u;
        so.FindProperty("userSelectionBackground").objectReferenceValue = null;
        so.FindProperty("userBackButton").objectReferenceValue = uBack;
        so.FindProperty("userContentContainer").objectReferenceValue = uContent;
        so.FindProperty("userButtonPrefab").objectReferenceValue = nameTile;
        so.ApplyModifiedProperties();
        g.SetActive(false); u.SetActive(false);
        Debug.Log("[V09Lobby] ③ 조 · 이름 판 640 × 560(타일 188 × 100 / 188 × 64 · 3열 · 세로 스크롤) · 배선 교체 · 옛 판 꺼 둠");
    }

    static GameObject BuildSelect(LobbyAuthUI_Complete auth, string name, string titleText, int step, float tileH, out Button backBtn, out RectTransform content)
    {
        const float W = 640, H = 560;
        var root = NewRoot(auth, name, 0.30f);
        var back = K.Plate(root, W, H, Local(auth));
        backBtn = IconBtn(back, "뒤로", 12, 12, null, "‹");
        K.Text(back, "제목", 72, 20, W - 144, 32, titleText, 20, K.Text1, true, TextAlignmentOptions.Center);
        var close = IconBtn(back, "닫기", W - 12 - 48, 12, CloseSprite(), "X");
        UnityEventTools.AddPersistentListener(close.onClick, auth.CloseSelectionPanels);
        for (int k = 0; k < 2; k++)   // 단계 알약
        {
            bool on = k <= step;
            var chip = K.Box(back, $"단계 {k + 1}", W / 2 - 100 + k * 104, 68, 96, 32, on ? K.Blue : K.Secondary, 16); chip.raycastTarget = false;
            K.Text(chip.transform, "글자", 0, 0, 96, 32, $"{k + 1} {(k == 0 ? "조" : "이름")}", 14, on ? K.Ink : K.Text1, true, TextAlignmentOptions.Center);
        }
        // 스크롤(Quest: 판 안 세로 스크롤 · 가는 막대)
        var view = K.Rect(back, "목록", 24, 116, 588, H - 116 - 24);   // 3 × 188 + 2 × 12
        view.gameObject.AddComponent<RectMask2D>();
        var hit = view.gameObject.AddComponent<Image>(); hit.color = new Color(1, 1, 1, 0f);   // 끌기 판정용
        content = K.Rect(view, "Content", 0, 0, 588, 10);
        var grid = content.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(188, tileH); grid.spacing = new Vector2(12, 12);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 3;
        var sr = view.gameObject.AddComponent<ScrollRect>();
        sr.content = content; sr.horizontal = false; sr.vertical = true; sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 20;
        var track = K.Box(back, "스크롤 막대", W - 16, 116, 4, H - 140, K.Secondary, 2);
        var bar = track.gameObject.AddComponent<Scrollbar>(); bar.direction = Scrollbar.Direction.BottomToTop;
        var area = K.Rect(track.transform, "Sliding Area", 0, 0, 4, H - 140);
        var handle = K.Box(area, "Handle", 0, 0, 4, 120, K.Text2, 2);
        var hrt = (RectTransform)handle.transform; hrt.anchorMin = Vector2.zero; hrt.anchorMax = Vector2.one; hrt.offsetMin = hrt.offsetMax = Vector2.zero;
        bar.handleRect = hrt; bar.targetGraphic = handle;
        sr.verticalScrollbar = bar; sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        return root;
    }

    // 타일 프리팹 — ★첫 TMP가 이름이어야 한다(GradeSelectionHandler · UserSelectionHandler가 첫 TMP에 글자를 넣는다)
    static GameObject MakeTilePrefab(string name, float w, float h, bool nameTile)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform; rt.sizeDelta = new Vector2(w, h);
        var img = go.AddComponent<Image>(); img.color = K.Tile; img.material = K.RoundMat;
        go.AddComponent<RoundedBoxUIProperties>().borderRadius = Vector4.one * 16;
        var b = go.AddComponent<Button>(); b.targetGraphic = img;
        var cb = b.colors; cb.highlightedColor = new Color(1.4f, 1.4f, 1.4f, 1.6f); cb.pressedColor = new Color(1.8f, 1.8f, 1.8f, 2f); cb.colorMultiplier = 1.5f; b.colors = cb;
        go.AddComponent<LayoutElement>();
        if (!nameTile)
        {
            K.Text(go.transform, "Label", 16, 16, w - 32, 48, "1조", 20, K.Text1, true, TextAlignmentOptions.TopLeft, true);
            K.Text(go.transform, "Sub", 16, h - 16 - 20, w - 32, 20, "", 14, K.Text2);
        }
        else
        {
            K.Text(go.transform, "Label", 58, 6, w - 66, h - 12, "이름", 14, K.Text1, true, TextAlignmentOptions.Left, true);
            var c = K.Box(go.transform, "원", 16, h / 2 - 16, 32, 32, K.Secondary, 16); c.raycastTarget = false;
            K.Text(c.transform, "Initial", 0, 0, 32, 32, "", 14, K.Text1, true, TextAlignmentOptions.Center);
        }
        foreach (var le in go.GetComponentsInChildren<LayoutElement>(true)) if (le.gameObject != go) le.ignoreLayout = true;
        string path = $"{PrefabDir}/{name}.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }

    // ── ④ 카드 · 구역 글자(Horizon OS 최소 14 · 구역 제목 20) ──
    // 스토어 카드(실측 · 1024 폭 환산): 사진 232 × 131(16:9) · 아래 제목 16 · 정보줄 14 두 줄 · 가로 간격 12 · 줄 간격 24 · 4열
    const float CardW = 232f, CardH = 131f + 8 + 22 + 40;
    static void RestyleCards()
    {
        int n = 0;
        var grids = new System.Collections.Generic.HashSet<GridLayoutGroup>();
        foreach (var card in Object.FindObjectsByType<LobbyItemCard>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var g = card.transform.parent != null ? card.transform.parent.GetComponent<GridLayoutGroup>() : null;
            if (g != null) grids.Add(g);
        }
        foreach (var g in grids)
        {
            Undo.RecordObject(g, "V10");
            g.cellSize = new Vector2(CardW, CardH); g.spacing = new Vector2(12, 24);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount; g.constraintCount = 5;   // 10-28 사용자 «가로로 5개»(판 폭은 LobbyV15Tool이 넓힌다)
        }
        foreach (var card in Object.FindObjectsByType<LobbyItemCard>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var bgT = card.transform.Find("Content/Background");
            var bgImg = bgT != null ? bgT.GetComponent<Image>() : null;
            if (bgImg != null) { Undo.RecordObject(bgImg, "V10"); bgImg.color = new Color(1, 1, 1, 0f); }   // ★스토어 카드엔 바탕판이 없다(실측)
            var photo = bgT != null ? bgT.Find("사진") as RectTransform : null;
            if (photo != null)
            {
                Undo.RecordObject(photo, "V10");
                float w = CardW;
                photo.anchorMin = new Vector2(0, 1); photo.anchorMax = new Vector2(1, 1); photo.pivot = new Vector2(0.5f, 1);
                photo.offsetMin = new Vector2(0, -w * 9f / 16f); photo.offsetMax = Vector2.zero;   // 16:9 · 판 폭 그대로
                var rb = photo.GetComponent<RoundedBoxUIProperties>(); if (rb != null) { Undo.RecordObject(rb, "V10"); rb.borderRadius = Vector4.one * 16; }
            }
            // 카드 높이 = 사진(16:9) + 8 + 제목 22 + 정보줄 두 줄 40 — 줄 레이아웃이 이 높이를 쓰게 한다
            if (bgT != null)
            {
                float hNeed = CardH;
                foreach (var tr in new[] { card.transform, bgT })
                {
                    var le = tr.GetComponent<LayoutElement>() ?? Undo.AddComponent<LayoutElement>(tr.gameObject);
                    Undo.RecordObject(le, "V10"); le.minHeight = hNeed; le.preferredHeight = hNeed; le.flexibleHeight = 0;
                }
                var crt = (RectTransform)card.transform; Undo.RecordObject(crt, "V10"); crt.sizeDelta = new Vector2(crt.sizeDelta.x, hNeed);
            }
            var el = bgT != null ? bgT.Find("Elements") : null;
            var title = el?.Find("Label")?.GetComponent<TMP_Text>();
            var sub = el?.Find("Label (1)")?.GetComponent<TMP_Text>();
            var vlg = el != null ? el.GetComponent<VerticalLayoutGroup>() : null;
            if (vlg != null) { Undo.RecordObject(vlg, "V10"); vlg.padding.left = 0; vlg.padding.right = 0; vlg.padding.bottom = 0; vlg.spacing = 2; }
            if (title != null) { Undo.RecordObject(title, "V10"); title.fontSize = 18; title.color = K.Text1; title.fontStyle = FontStyles.Normal; }
            if (sub != null)
            {
                Undo.RecordObject(sub, "V10"); sub.fontSize = 14; sub.color = K.Text2; sub.fontStyle = FontStyles.Normal;
                sub.textWrappingMode = TextWrappingModes.NoWrap; sub.overflowMode = TextOverflowModes.Ellipsis;   // 10-28 한 줄 · 넘치면 …(전체는 말풍선 — LobbyV15Tool)
            }
            n++;
        }
        // 로비 판 = 스토어 창 남색 · 구역 제목 24 굵게
        var lobby = Object.FindFirstObjectByType<LobbyAuthUI_Complete>(FindObjectsInactive.Include);
        var panelImg = lobby != null ? lobby.transform.Find("ContentUIExample-HorizonOS1/CanvasRoot/UIBackplate+VerticalLayoutGroup")?.GetComponent<Image>() : null;
        if (panelImg != null) { Undo.RecordObject(panelImg, "V10"); panelImg.material = K.RoundMat; panelImg.color = K.Panel; }
        int sec = 0;
        if (lobby != null)
            foreach (var t in lobby.GetComponentsInChildren<TMP_Text>(true))
                if (t.text.StartsWith("단순추나") || t.text.StartsWith("복잡추나") || t.text.Trim() == "ROM진단")
                {
                    if (t.GetComponentInParent<LobbyItemCard>(true) != null || t.GetComponentInParent<Toggle>(true) != null) continue;   // 카드 · 탭 글자는 빼고 구역 제목만
                    Undo.RecordObject(t, "V10"); t.fontSize = 24; t.fontStyle = FontStyles.Normal; t.color = K.Text1; sec++;   // 피드 창 구역 제목 = 24 보통 굵기(실측)
                }
        // 탭 = 라이브러리 왼쪽 메뉴처럼 고른 것 흰 알약 + 어두운 글자 — 기존 LobbyTabHighlight의 씬 값만 바꾼다(★규칙 7: 씬 값이 코드 기본값을 이긴다)
        int tabs = 0;
        if (lobby != null)
            foreach (var lb in lobby.GetComponentsInChildren<LobbyTabHighlight>(true))
            {
                var stray = lb.GetComponent<ToggleTint>(); if (stray != null) Undo.DestroyObjectImmediate(stray);   // 앞 시도에서 붙인 것 정리
                if (!lb.enabled) { Undo.RecordObject(lb, "V11"); lb.enabled = true; }
                var anim = lb.GetComponent<Animator>(); if (anim != null && !anim.enabled) { Undo.RecordObject(anim, "V11"); anim.enabled = true; }   // 끄기는 LobbyTabHighlight가 런타임에 한다
                var so = new SerializedObject(lb);
                so.FindProperty("activeColor").colorValue = K.White;
                so.FindProperty("normalColor").colorValue = K.Secondary;
                so.FindProperty("hoverColor").colorValue = K.Hex("#5A5A5A");
                so.FindProperty("tintLabel").boolValue = true;
                so.FindProperty("activeLabelColor").colorValue = K.Ink;
                so.FindProperty("normalLabelColor").colorValue = K.Text1;
                Undo.RecordObject(lb, "V11"); so.ApplyModifiedProperties();
                tabs++;
            }
        Debug.Log($"[V11Lobby] 탭 {tabs}개 = 고른 것 흰 알약(라이브러리 왼쪽 메뉴 실측) · 판 중성 회색");
        Debug.Log($"[V10Lobby] ④ 줄 {grids.Count}개 = 4열 · 칸 {CardW} × {CardH} · 카드 {n}개 = 스토어 카드(바탕 없음 · 16:9 사진 · 제목 16 · 정보줄 14) · 로비 판 남색 · 구역 제목 {sec}개 24");
    }

    [MenuItem("GuideChuna/UI 개편/V0.9 로비 판 미리보기 찍기 (lobby)")]
    public static void CaptureAll()
    {
        var auth = Object.FindFirstObjectByType<LobbyAuthUI_Complete>(FindObjectsInactive.Include);
        if (auth == null) return;
        foreach (var n in new[] { "모드 선택 팝업", "로그인 안내 V0.9", "로그인 한 판 V1.4" })
        {
            var t = auth.transform.Find(n); if (t == null) continue;
            var holder = t.Find("판");
            bool hw = holder != null && holder.gameObject.activeSelf; if (holder != null) holder.gameObject.SetActive(true);
            // 목록 판은 미리보기용 항목을 잠깐 채운다 — 한 판: 첫 격자 = 조 목록(첫 조 흰 알약) · 둘째 = 이름 격자
            var tmp = new System.Collections.Generic.List<GameObject>();
            var grids = t.GetComponentsInChildren<GridLayoutGroup>(true);
            string[] gs = { "1조", "2조", "3조", "4조", "본과3 A조", "6조", "7조", "8조", "9조", "10조", "11조", "12조", "13조", "14조", "15조" };
            string[] us = { "강민준", "고은서", "김도현", "김서연", "남궁민수아", "박지훈", "서하린", "오지민", "윤서준", "이하은", "정우진", "최유나" };
            for (int gi = 0; gi < grids.Length && gi < 2; gi++)
            {
                var content = grids[gi];
                bool gradeList = gi == 0;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{(gradeList ? "조항목_V14" : "이름타일_V14")}.prefab");
                int count = gradeList ? 15 : 12;   // 조는 최대 15(사용자 10-28)
                for (int i = 0; i < count && prefab != null; i++)
                {
                    var o = (GameObject)Object.Instantiate(prefab, content.transform);
                    o.hideFlags = HideFlags.DontSave;
                    o.GetComponentInChildren<TMP_Text>().text = gradeList ? gs[i] : us[i];
                    var s = o.transform.Find("Sub")?.GetComponent<TMP_Text>(); if (s != null) s.text = $"{5 + i % 2}명";
                    if (gradeList && i == 0)   // 고른 조 = 흰 알약(GradeSelectionHandler.MarkSelected와 같은 색)
                    {
                        o.GetComponent<Image>().color = K.White;
                        foreach (var x in o.GetComponentsInChildren<TMP_Text>()) x.color = K.Ink;
                    }
                    tmp.Add(o);
                }
                int rows = Mathf.CeilToInt(count / (float)content.constraintCount);
                var crt = (RectTransform)content.transform; crt.sizeDelta = new Vector2(crt.sizeDelta.x, rows * (content.cellSize.y + content.spacing.y));
            }
            UIPanelPreviewCapture.Capture(t.gameObject, "V09_" + n.Replace(" ", ""));
            foreach (var o in tmp) Object.DestroyImmediate(o);
            if (holder != null) holder.gameObject.SetActive(hw);
        }
    }
}
