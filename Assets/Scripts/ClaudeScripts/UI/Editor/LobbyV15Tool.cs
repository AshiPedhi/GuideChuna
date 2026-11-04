using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로비 V1.5 (2026-10-28 사용자): 로고 키움 · 제목 가운데 · 도움말 · 패스스루 버튼 뺌 · KoPubWorld돋움 3종 · 카드 가로 5개(세로도 같은 비율).
/// ★저장은 사람이 한다(Ctrl+S).
/// </summary>
public static class LobbyV15Tool
{
    const string Plate = "ContentUIExample-HorizonOS1/CanvasRoot/UIBackplate+VerticalLayoutGroup";

    static Transform Lobby()
    {
        var auth = Object.FindFirstObjectByType<LobbyAuthUI_Complete>(FindObjectsInactive.Include);
        return auth != null ? auth.transform : null;
    }

    const int Cols = 5;            // 사용자 «가로로 5개»
    const float LogoH = 48f;       // 로고 높이(종전 Icon 실측은 로그) — «로고 사이즈 키우고»
    const float TitleW = 600f;
    const float TitleSize = 24f;
    const float SectionSize = 24f;   // 10-28 사용자 «구역 제목 20으로 축소» → 같은 날 «축소한 거 되돌려» 24
    const float InfoSize = 14f;      // 10-28 사용자 «카드 정보줄 12로 축소» → 같은 날 되돌림 14
    const float CardTitleSize = 18f; // 10-28 사용자 «카드 제목 2픽셀 키워줘» 16 → 18
    const int Margin = 24;           // 10-28 사용자 «로비 UI 상하 좌우 전체 마진 맞춰줘» — 판 모서리 반지름(24)과 같게
    const int NamePad = 8;           // 이름 버튼 안쪽 여백(+ Elements 들여쓰기 6 = 아이콘까지 14)
    const float PlateAspect = 1024f / 686f;   // 판 가로세로 비율(원래 판) — «가로 늘어난 만큼 세로도»
    const float HoverOut = 6f;       // 카드 하이라이트 판이 카드보다 사방으로 큰 정도(가로 간격 12의 절반)
    const float BoxPad = 12f;        // 10-29 목록 테두리 상자 안쪽 여백 — HoverOut보다 커야 하이라이트가 마스크에 안 잘린다
    const float DimAlpha = 0.55f;    // 팝업 딤 검정 불투명도
    static readonly Color XNormal = new Color(0.2941177f, 0.2941177f, 0.2941177f, 1f);    // UISet ToggleButton_Dark «Normal»
    static readonly Color XRest = new Color(0.2941177f, 0.2941177f, 0.2941177f, 0f);      // 10-30 팝업 X 평소 = 투명(가리킬 때만 원)
    static readonly Color XHover =new Color(0.3647059f, 0.3647059f, 0.3647059f, 1f);     // «Highlighted»
    static readonly Color XPress = new Color(0.4352942f, 0.4352942f, 0.4352942f, 1f);     // «Pressed»
    static readonly Color XDisabled = new Color(0.2666667f, 0.2666667f, 0.2666667f, 1f);  // «Disabled»
    const bool ShowTitle = false;    // 10-28 사용자 «상단 중앙 타이틀 텍스트 없애줘»(그 앞엔 «좀 더 키워줘» → 24 Bold)

    [MenuItem("GuideChuna/UI 개편/V1.5 로비 적용 — 로고 · 제목 가운데 · 버튼 정리 · 5열 · KoPub (lobby)")]
    public static void Apply()
    {
        var lobby = Lobby();
        if (lobby == null) { Debug.LogWarning("[V15Lobby] 로비가 아니다"); return; }
        var plate = (RectTransform)lobby.Find(Plate);
        var head = (RectTransform)plate.Find("Horizontal");
        var sb = new StringBuilder("[V15Lobby] 적용\n");
        float w0 = plate.rect.width, h0 = plate.rect.height;

        // ① 카드 5열 — 늘어난 폭(칸 1 + 간격 1)만큼 판 · 머리줄 · 탭 줄 · 목록 · 구역을 같이 늘린다
        float dW = 0f;
        var grids = new System.Collections.Generic.List<GridLayoutGroup>();
        foreach (var g in plate.GetComponentsInChildren<GridLayoutGroup>(true))
            if (g.GetComponentInChildren<LobbyItemCard>(true) != null) grids.Add(g);
        if (grids.Count > 0 && grids[0].constraintCount != Cols)
        {
            var g0 = grids[0];
            dW = (g0.cellSize.x + g0.spacing.x) * (Cols - g0.constraintCount);
            foreach (var g in grids) { Undo.RecordObject(g, "V15"); g.constraintCount = Cols; Widen((RectTransform)g.transform, dW); }
            foreach (Transform row in plate)
                if (row != plate.Find("GradientEffect")) Widen((RectTransform)row, dW);   // 머리줄 · 검색줄(꺼짐) · 카테고리 · 실습 시나리오
            var content = plate.Find("실습 시나리오/Viewport/Content");
            if (content != null) foreach (Transform sec in content) Widen((RectTransform)sec, dW);   // 구역(SectionTitle*) — 폭은 직접 잡는다(ctrl width 꺼짐)
            sb.Append($"  ① 카드 {grids.Count}줄 → {Cols}열 · 폭 +{dW}\n");
        }
        else sb.Append($"  ① 이미 {Cols}열 — 폭 그대로\n");

        // ② 로고 키움 — 아이콘 높이 LogoH(비율 유지) · 머리줄도 그만큼 높인다(아래 줄이 같이 내려간다)
        var icon = (RectTransform)head.Find("CompanyIcon/Content/Background/Elements/Icon");
        var iconBox = (RectTransform)head.Find("CompanyIcon");
        float dHead = 0f;
        if (icon != null)
        {
            Vector2 s0 = icon.sizeDelta;
            float k = s0.y > 0 ? LogoH / s0.y : 1f;
            if (!Mathf.Approximately(k, 1f))
            {
                Undo.RecordObject(icon, "V15"); icon.sizeDelta = s0 * k;
                var le = icon.GetComponent<LayoutElement>(); if (le != null && le.preferredWidth > 0) { Undo.RecordObject(le, "V15"); le.preferredWidth = icon.sizeDelta.x; }
                foreach (var r in new[] { iconBox, (RectTransform)iconBox.Find("Content/Background") })
                {
                    Undo.RecordObject(r, "V15");
                    r.sizeDelta = new Vector2(Mathf.Max(r.sizeDelta.x, icon.sizeDelta.x + 16), Mathf.Max(r.sizeDelta.y, LogoH));
                }
                dHead = Mathf.Max(0f, LogoH - 40f) - Mathf.Max(0f, s0.y - 40f);
                Undo.RecordObject(head, "V15"); head.sizeDelta += new Vector2(0, dHead);
            }
            sb.Append($"  ② 로고 {s0} → {icon.sizeDelta} · 머리줄 +{dHead}\n");
        }
        var hlg = head.GetComponent<HorizontalLayoutGroup>();
        Undo.RecordObject(hlg, "V15");
        hlg.childAlignment = TextAnchor.MiddleRight;   // 10-28 버튼(이름 · 나가기)은 오른쪽 끝 — 이름 폭이 바뀌어도 X가 끝에 붙는다
        hlg.padding = new RectOffset(0, 0, 0, 0);

        // ③ 도움말 · 패스스루 뺌(끈다 — 지우지 않는다). ★씬 기본이 이미 패스스루(층 보임 · 카메라 배경 투명 · 방 꺼짐)라 버튼 Start가 없어도 같다
        foreach (var n in new[] { "도움말", "패스스루" })
        {
            var b = head.Find(n);
            if (b != null && b.gameObject.activeSelf) { Undo.RecordObject(b.gameObject, "V15"); b.gameObject.SetActive(false); }
        }

        // ④ 제목(꺼짐 · 판 가운데) · 로고는 줄 배치에서 빼서 왼쪽 끝에 고정 · 빈칸(space)은 끈다(오른쪽 정렬이 대신한다)
        var title = (RectTransform)head.Find("Title");
        var tle = title.GetComponent<LayoutElement>() ?? Undo.AddComponent<LayoutElement>(title.gameObject);
        Undo.RecordObject(tle, "V15"); tle.ignoreLayout = true;
        Undo.RecordObject(title, "V15");
        title.anchorMin = title.anchorMax = title.pivot = new Vector2(0.5f, 0.5f);
        title.sizeDelta = new Vector2(TitleW, 40f);
        var tt = title.GetComponent<TMP_Text>(); if (tt != null) { Undo.RecordObject(tt, "V15"); tt.alignment = TextAlignmentOptions.Center; tt.enableAutoSizing = false; tt.fontSize = TitleSize; }
        if (title.gameObject.activeSelf != ShowTitle) { Undo.RecordObject(title.gameObject, "V15"); title.gameObject.SetActive(ShowTitle); }

        var logoLe = iconBox.GetComponent<LayoutElement>() ?? Undo.AddComponent<LayoutElement>(iconBox.gameObject);
        Undo.RecordObject(logoLe, "V15"); logoLe.ignoreLayout = true;
        Undo.RecordObject(iconBox, "V15");
        iconBox.anchorMin = iconBox.anchorMax = new Vector2(0f, 0.5f); iconBox.pivot = new Vector2(0f, 0.5f);
        iconBox.anchoredPosition = new Vector2(-(iconBox.sizeDelta.x - icon.sizeDelta.x) / 2f, 0f);   // 로고 그림 왼쪽 = 줄 왼쪽(상자 안쪽 여백만큼 당긴다)
        var space = head.Find("space");
        if (space != null && space.gameObject.activeSelf) { Undo.RecordObject(space.gameObject, "V15"); space.gameObject.SetActive(false); }

        // X 버튼 = 원 — 56×40에 반지름 20이라 타원이었다(10-28 사용자 «아이콘 테두리가 타원으로 균형이 안 맞아»)
        var xBtn = (RectTransform)head.Find("Logout");
        var xBg = xBtn != null ? xBtn.Find("Content/Background") as RectTransform : null;
        if (xBtn != null) { Undo.RecordObject(xBtn, "V15"); xBtn.sizeDelta = new Vector2(40f, 40f); }
        if (xBg != null) { Undo.RecordObject(xBg, "V15"); xBg.sizeDelta = new Vector2(xBg.sizeDelta.x, 40f); }
        sb.Append($"  ④ 로고 왼쪽 고정 · 버튼 오른쪽 정렬 · X 40×40(원)\n");

        // ⑤ 세로도 같은 비율 — 판 높이 = 원래 높이 × (새 폭 / 원래 폭). 머리줄이 늘어난 몫은 빼고 목록 칸에 준다
        if (dW > 0f)
        {
            float dH = h0 * dW / w0 - dHead;
            var list = (RectTransform)plate.Find("실습 시나리오");
            Undo.RecordObject(list, "V15"); list.sizeDelta += new Vector2(0, dH);
            sb.Append($"  ⑤ 목록 높이 +{dH:F0}\n");
        }

        // ⑤' 구역 제목 · 카드 정보줄 크기
        int secN = 0, infoN = 0;
        foreach (var t in lobby.GetComponentsInChildren<TMP_Text>(true))
        {
            if (t.GetComponentInParent<LobbyItemCard>(true) != null)
            {
                if (t.name == "Label (1)" && t.transform.parent != null && t.transform.parent.name == "Elements") { Undo.RecordObject(t, "V15"); t.fontSize = InfoSize; PrefabUtility.RecordPrefabInstancePropertyModifications(t); infoN++; }
                else if (t.name == "Label" && t.transform.parent != null && t.transform.parent.name == "Elements") { Undo.RecordObject(t, "V15"); t.fontSize = CardTitleSize; PrefabUtility.RecordPrefabInstancePropertyModifications(t); }
                continue;
            }
            if (t.GetComponentInParent<Toggle>(true) != null) continue;
            string x = t.text.Trim();
            if (x.StartsWith("단순추나") || x.StartsWith("복잡추나") || x == "ROM진단") { Undo.RecordObject(t, "V15"); t.fontSize = SectionSize; secN++; }
        }
        sb.Append($"  ⑤' 구역 제목 {secN}개 → {SectionSize} · 카드 정보줄 {infoN}개 → {InfoSize} · 카드 제목 → {CardTitleSize}\n");

        // ⑥ 글꼴 — KoPubWorld돋움 세 굵기(KoPubFonts 규칙)
        if (!KoPubFonts.Ready) KoPubFonts.Create();
        int[] cnt = new int[3]; int skip = 0;
        foreach (var t in lobby.GetComponentsInChildren<TMP_Text>(true))
        {
            bool isHeadTitle = t.transform == title;
            var role = KoPubFonts.RoleOf(t, isHeadTitle);
            Undo.RecordObject(t, "V15");
            if (KoPubFonts.Apply(t, isHeadTitle)) { cnt[(int)role]++; PrefabUtility.RecordPrefabInstancePropertyModifications(t); }
            else { skip++; sb.Append($"    건너뜀(글자 없음) «{t.text}» {t.name}\n"); }
        }
        sb.Append($"  ⑥ 글꼴 Bold {cnt[0]} · Medium {cnt[1]} · Light {cnt[2]} · 건너뜀 {skip}\n");
        // 머리줄 이름 버튼(«로그인» / 사용자 이름) = 버튼 라벨 양식 Medium 16(사용자 10-28 — 22라 Bold로 잡혔던 것)
        var nameBtn = head.Find("이름");
        if (nameBtn != null)
            foreach (var t in nameBtn.GetComponentsInChildren<TMP_Text>(true))
            {
                Undo.RecordObject(t, "V15");
                t.enableAutoSizing = false; t.fontSize = 16f;
                t.font = KoPubFonts.Medium; t.fontSharedMaterial = KoPubFonts.Medium.material; t.fontStyle &= ~FontStyles.Bold;
                PrefabUtility.RecordPrefabInstancePropertyModifications(t);
                sb.Append($"    이름 버튼 «{t.text}» → Medium 16\n");
            }
        // 이름 버튼 = 글자 폭 알약 — 190 고정에 가운데 정렬이라 «여백 타원이 너무 크고 아이콘이 가운데에 붙었다»(10-28).
        //   아이콘을 왼쪽 안쪽 여백(6 + 8)에 붙이고 폭은 글자에 맞춘다. 실행 중에도 이름이 바뀌면 같은 Fit()을 탄다
        var nameLabel = nameBtn != null ? nameBtn.GetComponent<LobbyUserNameLabel>() : null;
        if (nameLabel != null)
        {
            var nEl = nameBtn.Find("Content/Background/Elements");
            var nHlg = nEl != null ? nEl.GetComponent<HorizontalLayoutGroup>() : null;
            if (nHlg != null) { Undo.RecordObject(nHlg, "V15"); nHlg.childAlignment = TextAnchor.MiddleLeft; nHlg.padding = new RectOffset(NamePad, NamePad, 0, 0); PrefabUtility.RecordPrefabInstancePropertyModifications(nHlg); }
            var so = new SerializedObject(nameLabel);
            so.FindProperty("pill").objectReferenceValue = nameBtn;
            so.FindProperty("pillBackground").objectReferenceValue = nameBtn.Find("Content/Background")?.GetComponent<LayoutElement>();
            so.FindProperty("textBox").objectReferenceValue = nEl != null ? nEl.Find("Text") : null;
            so.FindProperty("fixedWidth").floatValue = 6f * 2 + NamePad * 2 + 24f + 8f;   // Elements 들여쓰기 6×2 · 안쪽 여백 · 아이콘 24 · 사이 8
            if (so.FindProperty("label").objectReferenceValue == null) so.FindProperty("label").objectReferenceValue = nEl?.Find("Text/Label")?.GetComponent<TMP_Text>();
            so.ApplyModifiedProperties();
            Undo.RecordObject(nameBtn, "V15");
            nameLabel.Fit();
            sb.Append($"    이름 버튼 알약 폭 {((RectTransform)nameBtn).sizeDelta.x:F0}(«로그인» 기준 · 이름에 따라 바뀜)\n");
        }
        foreach (var p in new[] { "Assets/Prefabs/UI/LoginV09/조항목_V14.prefab", "Assets/Prefabs/UI/LoginV09/이름타일_V14.prefab" })
        {
            var root = PrefabUtility.LoadPrefabContents(p);
            int m = 0; foreach (var t in root.GetComponentsInChildren<TMP_Text>(true)) if (KoPubFonts.Apply(t)) m++;
            PrefabUtility.SaveAsPrefabAsset(root, p); PrefabUtility.UnloadPrefabContents(root);
            sb.Append($"    프리팹 {Path(p)} 글자 {m}개\n");
        }

        // ⑦ 카드 글자 쌓기 — 정보줄이 두 줄이면 한 줄 높이 칸에서 위로 넘쳐 제목을 덮었다(가운데 정렬 · 높이 고정).
        //    사진 바로 아래(8)부터 제목 → 정보줄 순으로 위에서 쌓고, 칸 높이는 글자가 정한다(스토어 카드 실측 순서)
        int cards = 0;
        var tip = EnsureTooltip((RectTransform)plate.parent);
        foreach (var card in lobby.GetComponentsInChildren<LobbyItemCard>(true))
        {
            var bg = card.transform.Find("Content/Background") as RectTransform;
            var el = bg != null ? bg.Find("Elements") as RectTransform : null;
            var photo = bg != null ? bg.Find("사진") as RectTransform : null;
            var vlg = el != null ? el.GetComponent<VerticalLayoutGroup>() : null;
            if (vlg == null || photo == null) continue;
            Undo.RecordObject(vlg, "V15");
            // 실측(10-28): 글자 폭이 152로 고정(카드 232)이라 일찍 두 줄로 감겼고, 칸 63 < 필요 82(제목 24.7 + 정보 두 줄 43.1 + 위 여백 12)라 겹쳤다
            vlg.childAlignment = TextAnchor.UpperLeft; vlg.childControlHeight = true; vlg.childForceExpandHeight = false; vlg.spacing = 2;
            vlg.childControlWidth = true; vlg.childForceExpandWidth = true; vlg.padding.top = 0;
            Undo.RecordObject(el, "V15");
            el.anchorMin = new Vector2(0, 0); el.anchorMax = new Vector2(1, 1); el.pivot = new Vector2(0.5f, 1f);
            el.offsetMin = new Vector2(0, -12f); el.offsetMax = new Vector2(0, -(photo.rect.height + 8f));   // 아래로 12 더(줄 간격 24 안) — 두 줄 정보줄 자리
            PrefabUtility.RecordPrefabInstancePropertyModifications(vlg); PrefabUtility.RecordPrefabInstancePropertyModifications(el);
            // ★카드 바탕 안 그리기 — UISet 애니메이터(ToggleButton_Dark «Normal»)가 실행 중에 Content/Background 색을 알파 1 회색으로 칠해,
            //   사진과 같은 이미지가 카드 전체(232×201)로 글자 밑에 깔렸다(10-28 기기에서 발견 · 에디터는 애니메이터가 안 돌아 투명).
            //   끄면(enabled=false) 글자 칸을 눌러도 안 눌린다 → 채움 0(그리기만 0)으로 두고 레이 판정은 칸 전체 그대로. 애니메이터는 색만 건드린다.
            var bgImg = bg.GetComponent<Image>();
            if (bgImg != null)
            {
                Undo.RecordObject(bgImg, "V15");
                bgImg.type = Image.Type.Filled; bgImg.fillMethod = Image.FillMethod.Horizontal; bgImg.fillAmount = 0f;
                PrefabUtility.RecordPrefabInstancePropertyModifications(bgImg);
            }
            // 정보줄 = 한 줄 · 넘치면 …(10-28 사용자) — 전체는 손을 올리면 말풍선(LobbyCardTooltip → LobbyTooltip)
            var sub = el.Find("Label (1)")?.GetComponent<TMP_Text>();
            if (sub != null)
            {
                Undo.RecordObject(sub, "V15");
                sub.textWrappingMode = TextWrappingModes.NoWrap; sub.overflowMode = TextOverflowModes.Ellipsis;
                PrefabUtility.RecordPrefabInstancePropertyModifications(sub);
                var ct = card.GetComponent<LobbyCardTooltip>() ?? Undo.AddComponent<LobbyCardTooltip>(card.gameObject);
                var cso = new SerializedObject(ct);
                cso.FindProperty("detail").objectReferenceValue = sub;
                cso.FindProperty("tooltip").objectReferenceValue = tip;
                cso.ApplyModifiedProperties();
            }
            // 호버 · 누름 하이라이트 — 카드 뒤 둥근 판(카드보다 사방 HoverOut 크게) · 첫 형제라 사진 · 글자 뒤에 그린다
            var oldHl = card.transform.Find("호버 바탕");
            if (oldHl != null) Undo.DestroyObjectImmediate(oldHl.gameObject);
            var cr = (RectTransform)card.transform;
            var hl = HzUIKit.Box(cr, "호버 바탕", -HoverOut, -HoverOut, cr.rect.width + 2 * HoverOut, cr.rect.height + 2 * HoverOut, Color.white, 16);
            Undo.RegisterCreatedObjectUndo(hl.gameObject, "V15");
            // 카드 크기를 따라가게 늘림(stretch) — 고정 크기로 두면 만들 때 꺼져 있던 카드(시험용 숨김 카드)는 크기가 틀렸다(10-29 실측 197.6 · 다른 카드 244)
            var hr = hl.rectTransform; hr.anchorMin = Vector2.zero; hr.anchorMax = Vector2.one; hr.pivot = new Vector2(0.5f, 0.5f);
            hr.offsetMin = -Vector2.one * HoverOut; hr.offsetMax = Vector2.one * HoverOut;
            hl.raycastTarget = false;
            hl.enabled = false;   // 실행 때 LobbyCardHover.Awake가 알파 0으로 켠다(에디터 · 캡처에선 안 보이게)
            hl.transform.SetAsFirstSibling();
            var hov = card.GetComponent<LobbyCardHover>() ?? Undo.AddComponent<LobbyCardHover>(card.gameObject);
            var hso = new SerializedObject(hov);
            hso.FindProperty("highlight").objectReferenceValue = hl;
            hso.ApplyModifiedProperties();
            cards++;
        }
        sb.Append($"  ⑦ 카드 {cards}장 글자 = 사진 아래 8부터 제목 → 정보줄 한 줄(넘치면 … · 손 올리면 말풍선)\n");

        LayoutRebuilder.ForceRebuildLayoutImmediate(plate);

        // ⑨ 바깥 여백 상하좌우 M — «로비 UI 상하 좌우 전체 마진 맞춰줘»(10-28). 재 보니 좌 20(로고)·24(카드) · 우 0(X)·36(카드) · 상 14 · 하 24였다.
        //    눈에 보이는 끝(로고 그림 · X 원 · 카드 · 목록 아래)을 기준으로 맞춘다. 목록 오른쪽 스크롤 자리(inset)는 오른쪽 여백 안에 넣는다
        // ⑬(10-29) 목록 전체 = 테두리 상자 하나 — 상자 끝이 여백 Margin에 오고 카드는 상자 안쪽 BoxPad만큼 들어간다(A안: 카드 크기 그대로 · 판을 키움).
        //    마스크(Viewport)도 상자 안쪽까지만 — 안쪽 여백 12 > 하이라이트 6이라 1열 왼쪽 · 5열 오른쪽이 안 잘린다(10-29 실측: 종전 마스크 폭 = 카드 5열 폭이라 6씩 잘렸다)
        {
            var list = (RectTransform)plate.Find("실습 시나리오");
            var vp = (RectTransform)list.Find("Viewport");
            Undo.RecordObject(vp, "V15");
            vp.anchorMin = Vector2.zero; vp.anchorMax = Vector2.one; vp.pivot = new Vector2(0f, 1f);
            // ★마스크 = 상자 전체 · 카드는 Content 안쪽 여백으로 들인다. 마스크까지 BoxPad로 줄이면 마스크 폭 = 카드 5열 폭이 되어 그대로 잘린다(10-29 첫 적용에서 실측)
            vp.offsetMin = vp.offsetMax = Vector2.zero;
            var cvl = vp.Find("Content")?.GetComponent<VerticalLayoutGroup>();
            if (cvl != null) { Undo.RecordObject(cvl, "V15"); int ip = Mathf.RoundToInt(BoxPad); cvl.padding = new RectOffset(ip, ip, ip, ip); }
            var limg = list.GetComponent<Image>();
            Undo.RecordObject(limg, "V15");
            limg.enabled = true; limg.sprite = null; limg.type = Image.Type.Simple; limg.material = HzUIKit.RoundMat; limg.color = HzUIKit.FrameColor; limg.raycastTarget = true;   // 끌기 판정은 상자 전체(종전 Image 그대로)
            var lrb = list.GetComponent<RoundedBoxUIProperties>() ?? Undo.AddComponent<RoundedBoxUIProperties>(list.gameObject);
            Undo.RecordObject(lrb, "V15"); lrb.borderRadius = Vector4.one * HzUIKit.FrameRadius;
            var oldInner = list.Find("안");   // 10-29 첫 판(선 + 안쪽 판 색)의 안쪽 겹 — 바탕 상자로 바꾸며 뺀다
            if (oldInner != null) Undo.DestroyObjectImmediate(oldInner.gameObject);
            sb.Append($"  ⑬ 목록 바탕 상자 {ColorUtility.ToHtmlStringRGB(HzUIKit.FrameColor)} · 안쪽 여백 {BoxPad} · 마스크 = 상자 전체\n");
        }

        {
            var list = (RectTransform)plate.Find("실습 시나리오");
            var vp = (RectTransform)list.Find("Viewport");
            float inset = 0f;   // 10-29 스크롤 막대 자리 대신 상자 안쪽 여백(⑬)이 양쪽에 같게 든다
            var gg = grids[0];
            float wc = gg.constraintCount * gg.cellSize.x + (gg.constraintCount - 1) * gg.spacing.x + 2 * BoxPad;   // 줄 폭 = 상자 폭(카드 5열 + 안쪽 여백 양쪽)
            var pv = plate.GetComponent<VerticalLayoutGroup>();
            Undo.RecordObject(pv, "V15");
            pv.padding = new RectOffset(Margin, Margin, Margin, Margin);
            float others = 0f; int nRows = 0;
            foreach (Transform row in plate)
            {
                var le = row.GetComponent<LayoutElement>(); if (le != null && le.ignoreLayout) continue;
                var rr = (RectTransform)row;
                Undo.RecordObject(rr, "V15");
                if (row == list) { nRows++; continue; }
                rr.sizeDelta = new Vector2(wc, row == head ? LogoH : rr.sizeDelta.y);
                if (row.gameObject.activeSelf) { others += rr.sizeDelta.y; nRows++; }
            }
            float plateW = Margin + wc + inset + (Margin - Mathf.RoundToInt(inset));
            float plateH = plateW / PlateAspect;
            float listH = plateH - (pv.padding.top + pv.padding.bottom + others + pv.spacing * (nRows - 1));
            list.sizeDelta = new Vector2(wc + inset, listH);
            var listContent = vp.Find("Content") as RectTransform;   // 스크롤 내용이 x 0.8에 놓여 카드 여백이 24.8 / 23.2로 틀어졌다(실측)
            if (listContent != null) { Undo.RecordObject(listContent, "V15"); listContent.anchoredPosition = new Vector2(0f, listContent.anchoredPosition.y); }
            LayoutRebuilder.ForceRebuildLayoutImmediate(plate);
            sb.Append($"  ⑨ 여백 {Margin} · 줄 폭 {wc} (+스크롤 자리 {inset:F0}) · 목록 높이 {listH:F0}\n");
        }
        Vector3 pc = head.InverseTransformPoint(plate.TransformPoint(plate.rect.center));
        title.anchoredPosition = new Vector2(pc.x - head.rect.center.x, 0f);   // 제목(꺼져 있어도) 판 한가운데

        // ⑧ 캔버스 = 판 크기 — ★레이 클릭 범위(ISDK_RayInteraction/Surface)가 캔버스 크기를 따른다. 판만 키우면 늘어난 오른쪽 · 아래 카드가 안 눌린다
        var canvasRt = (RectTransform)plate.parent;
        Undo.RecordObject(canvasRt, "V15");
        canvasRt.sizeDelta = new Vector2(plate.rect.width, plate.rect.height + 2f);   // 종전도 판 + 2(1024×688 / 686)
        LayoutRebuilder.ForceRebuildLayoutImmediate(canvasRt);
        var ray = canvasRt.Find("ISDK_RayInteraction") as RectTransform;
        sb.Append($"  ⑧ 캔버스 {canvasRt.rect.width:F0}×{canvasRt.rect.height:F0} · 레이 범위 {(ray != null ? $"{ray.rect.width:F0}×{ray.rect.height:F0}" : "없음")} · 판 중심 {canvasRt.InverseTransformPoint(plate.position)}\n");

        // ⑪ 팝업 X = 로비 X와 같은 꼴 — 40×40 원(아이콘 24 · 안쪽 8) · 오른쪽 여백 Margin · 제목 줄 세로 가운데(10-28 사용자)
        //    종전 = 48×48 · 가장자리 16(LobbyV09Tool.IconBtn)
        foreach (var pn in new[] { "모드 선택 팝업", "로그인 한 판 V1.4", "로그인 안내 V0.9" })
        {
            var pr = lobby.Find(pn); if (pr == null) continue;
            foreach (var b in pr.GetComponentsInChildren<Button>(true))
            {
                if (b.name != "닫기") continue;
                var br = (RectTransform)b.transform; var back = (RectTransform)br.parent;
                var ttl = back.Find("제목") as RectTransform;
                float cy = ttl != null ? -ttl.anchoredPosition.y + ttl.rect.height / 2f : Margin + 20f;   // 제목 줄 가운데(위에서)
                if (cy - 20f > Margin + 20f) cy = Margin + 20f;   // 제목이 아이콘 아래 등 낮게 있으면(로그인 안내) 위 여백 24에 둔다
                Undo.RecordObject(br, "V15");
                br.sizeDelta = new Vector2(40f, 40f);
                br.anchoredPosition = new Vector2(back.rect.width - Margin - 40f, -(cy - 20f));
                var rb = b.GetComponent<RoundedBoxUIProperties>(); if (rb != null) { Undo.RecordObject(rb, "V15"); rb.borderRadius = Vector4.one * 20f; }
                // 원 색 = 로비 X(UISet ToggleButton_Dark 애니메이터 Content/Background 실측) — 평소 #4B4B4B · 가리킴 #5D5D5D · 누름 #6F6F6F.
                //   종전엔 바탕 알파 0 × 색 곱하기라 가리켜도 원이 안 보였다(10-28 사용자 «팝업 X 테두리가 없는데?»)
                var bimg = b.targetGraphic as Image;
                if (bimg != null) { Undo.RecordObject(bimg, "V15"); bimg.color = Color.white; }
                Undo.RecordObject(b, "V15");
                var cbk = b.colors;
                // 10-30 사용자 «로비 X처럼 평소엔 하이라이트 없다가 호버되면 나타나게» — 평소 · 선택 = 투명(XNormal 알파 0)
                cbk.normalColor = XRest; cbk.selectedColor = XRest; cbk.highlightedColor = XHover; cbk.pressedColor = XPress; cbk.disabledColor = XDisabled;
                cbk.colorMultiplier = 1f; cbk.fadeDuration = 0.1f;
                b.colors = cbk; b.transition = Selectable.Transition.ColorTint;
                var ic = br.Find("아이콘") as RectTransform;
                if (ic != null) { Undo.RecordObject(ic, "V15"); ic.anchoredPosition = new Vector2(8f, -8f); ic.sizeDelta = new Vector2(24f, 24f); }
                sb.Append($"  ⑪ {pn} X → 40×40 원 · 오른쪽 {Margin} · 위 {cy - 20f:F0}(제목 가운데 {cy:F0})\n");
            }
        }

        // ⑫ 팝업 딤 — 로그인 · 시나리오 팝업이 뜨면 로비 판 위에 반투명 검정(판과 같은 둥근 모서리) · 켜진 동안 카드 안 눌림
        {
            const string DimName = "팝업 딤";
            var oldDim = plate.Find(DimName); if (oldDim != null) Undo.DestroyObjectImmediate(oldDim.gameObject);
            var dim = HzUIKit.Box(plate, DimName, 0, 0, plate.rect.width, plate.rect.height, new Color(0f, 0f, 0f, DimAlpha), 24);
            Undo.RegisterCreatedObjectUndo(dim.gameObject, "V15");
            var dr = dim.rectTransform; dr.anchorMin = Vector2.zero; dr.anchorMax = Vector2.one; dr.offsetMin = dr.offsetMax = Vector2.zero;
            dim.raycastTarget = true;   // 켜졌을 때 뒤 카드를 막는다(꺼지면 CanvasGroup이 레이를 끈다)
            dr.SetAsLastSibling();
            dim.gameObject.AddComponent<CanvasGroup>();
            var dmr = dim.gameObject.AddComponent<LobbyDimmer>();
            var watch = new System.Collections.Generic.List<GameObject>();
            var authC = lobby.GetComponent<LobbyAuthUI_Complete>();
            var gsp = new SerializedObject(authC).FindProperty("gradeSelectionPanel");
            if (gsp != null && gsp.objectReferenceValue is GameObject g1) watch.Add(g1);
            foreach (var mp in lobby.GetComponentsInChildren<LobbyModePopup>(true))
                if (new SerializedObject(mp).FindProperty("panel").objectReferenceValue is GameObject g2) watch.Add(g2);
            var dso = new SerializedObject(dmr);
            var wp = dso.FindProperty("watch"); wp.arraySize = watch.Count;
            for (int i = 0; i < watch.Count; i++) wp.GetArrayElementAtIndex(i).objectReferenceValue = watch[i];
            dso.ApplyModifiedProperties();
            dmr.Snap();
            sb.Append($"  ⑫ 팝업 딤 검정 {DimAlpha:F2} · 지켜보는 팝업 {watch.Count}개({string.Join(", ", watch.ConvertAll(w => w.name))})\n");
        }

        // ⑩ 눈에 보이는 끝까지 실제 여백(재기)
        {
            var list = (RectTransform)plate.Find("실습 시나리오");
            var g0 = grids[0].transform;
            RectTransform firstCard = null, lastCard = null;
            foreach (Transform c in g0) if (c.gameObject.activeSelf) { if (firstCard == null) firstCard = (RectTransform)c; lastCard = (RectTransform)c; }
            Rect pr = plate.rect;
            Rect logo = InPlate(plate, icon), x = InPlate(plate, xBg), c0 = InPlate(plate, firstCard), c1 = InPlate(plate, lastCard), l = InPlate(plate, list);
            sb.Append($"  ⑩ 실제 여백 — 좌 로고 {logo.xMin - pr.xMin:F1} · 카드 {c0.xMin - pr.xMin:F1} / 우 X {pr.xMax - x.xMax:F1} · 카드(5번째) {pr.xMax - c1.xMax:F1} / 상 로고 {pr.yMax - logo.yMax:F1} · X {pr.yMax - x.yMax:F1} / 하 목록 {l.yMin - pr.yMin:F1}\n");
        }

        sb.Append($"  판 {w0:F0}×{h0:F0} → {plate.rect.width:F0}×{plate.rect.height:F0} (비율 {w0 / h0:F3} → {plate.rect.width / plate.rect.height:F3})\n  ★저장은 Ctrl+S");
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(lobby.gameObject.scene);
        Debug.Log(sb.ToString());
    }

    static string Path(string p) => System.IO.Path.GetFileNameWithoutExtension(p);

    static readonly Vector3[] C4 = new Vector3[4];
    static Rect InPlate(RectTransform plate, RectTransform r)
    {
        if (r == null) return new Rect();
        r.GetWorldCorners(C4);
        Vector3 a = plate.InverseTransformPoint(C4[0]), b = plate.InverseTransformPoint(C4[2]);
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    // 카드 세부정보 말풍선 — 판 캔버스 맨 위(마지막 형제)에 하나 · 다시 돌리면 새로 만든다(카드 배선도 매번 다시)
    static LobbyTooltip EnsureTooltip(RectTransform canvasRt)
    {
        const string Name = "카드 상세 말풍선";
        var old = canvasRt.Find(Name);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        var holder = new GameObject(Name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(holder, "V15");
        holder.layer = canvasRt.gameObject.layer;
        holder.transform.SetParent(canvasRt, false);
        var hr = (RectTransform)holder.transform;
        hr.anchorMin = Vector2.zero; hr.anchorMax = Vector2.one; hr.offsetMin = hr.offsetMax = Vector2.zero;
        holder.AddComponent<LayoutElement>().ignoreLayout = true;
        hr.SetAsLastSibling();

        var box = HzUIKit.Box(hr, "상자", 0, 0, 200, 32, HzUIKit.Hex("#4A4A4A"), 8);
        box.raycastTarget = false;
        var br = (RectTransform)box.transform;
        br.anchorMin = br.anchorMax = new Vector2(0.5f, 0.5f); br.pivot = new Vector2(0f, 1f);
        var lbl = HzUIKit.Text(br, "글자", 0, 0, 176, 16, "", 14, HzUIKit.Text1, false, TextAlignmentOptions.TopLeft, true);
        var lr = lbl.rectTransform;
        lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one; lr.pivot = new Vector2(0f, 1f);
        lr.offsetMin = new Vector2(12f, 8f); lr.offsetMax = new Vector2(-12f, -8f);
        if (KoPubFonts.Ready) { lbl.font = KoPubFonts.Light; lbl.fontSharedMaterial = KoPubFonts.Light.material; }   // 보조 정보 = Light 14

        var tip = holder.AddComponent<LobbyTooltip>();
        var so = new SerializedObject(tip);
        so.FindProperty("box").objectReferenceValue = br;
        so.FindProperty("label").objectReferenceValue = lbl;
        so.ApplyModifiedProperties();
        box.gameObject.SetActive(false);
        return tip;
    }

    static void Widen(RectTransform r, float dW)
    {
        if (r == null || dW == 0f) return;
        Undo.RecordObject(r, "V15");
        if (Mathf.Approximately(r.anchorMin.x, r.anchorMax.x)) r.sizeDelta += new Vector2(dW, 0);   // 늘어나는(stretch) 칸은 부모를 따라간다
    }

    // ── OS 텍스트 크기 넘침 검사(10-28 사용자 «텍스트 · 화면 둘 다 최대인데 박스 안 삐져나가게 대응이 되나?») ──
    // ★기기와 같은 OsDisplayScale.ApplyFont를 탄다(미리보기가 따로 계산하면 거짓말을 한다 — CLAUDE.md 규칙 9).
    // 화면 크기는 판 전체 배율이라 글자/칸 비율이 안 바뀐다 → 글자 배율만 본다. 재고 나면 전부 되돌린다.
    [MenuItem("GuideChuna/UI 개편/V1.5 OS 글자 최대 넘침 검사 (lobby)")]
    public static void CheckOverflowAll() { CheckOverflow(1f); CheckOverflow(1.15f); CheckOverflow(1.5f); }   // 1.0 = 기준(원래부터 넘친 것 구분)

    static readonly string[] Panels = { "ContentUIExample-HorizonOS1", "모드 선택 팝업", "로그인 안내 V0.9", "로그인 한 판 V1.4" };

    static void CheckOverflow(float k)
    {
        var lobby = Lobby();
        if (lobby == null) return;
        var acts = new System.Collections.Generic.List<(GameObject go, bool on)>();
        var roots = new System.Collections.Generic.List<Transform>();
        foreach (var n in Panels)
        {
            var r = lobby.Find(n); if (r == null) continue;
            roots.Add(r);
            foreach (var go in new[] { r.gameObject, r.Find("판")?.gameObject })
                if (go != null) { acts.Add((go, go.activeSelf)); go.SetActive(true); }
        }
        var texts = new System.Collections.Generic.List<TMP_Text>();
        var snap = new System.Collections.Generic.List<(float s, float mn, float mx)>();
        foreach (var r in roots)
            foreach (var t in r.GetComponentsInChildren<TMP_Text>(false))
            {
                if (string.IsNullOrWhiteSpace(t.text) || t.text == "​") continue;
                texts.Add(t); snap.Add((t.fontSize, t.fontSizeMin, t.fontSizeMax));
            }
        var sb = new StringBuilder($"[V15Lobby] OS 글자 ×{k:F2} 넘침 검사 — 글자 {texts.Count}개\n");
        var fits = new System.Collections.Generic.List<PopupTextFit>();
        try
        {
            OsDisplayScale.EditorSimulate(k, 1f);
            foreach (var t in texts) OsDisplayScale.ApplyFont(t);
            Canvas.ForceUpdateCanvases();
            foreach (var r in roots) foreach (var c in r.GetComponentsInChildren<Canvas>(false)) LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)c.transform);
            // 판 세로 맞춤(PopupTextFit) — 기기와 같은 Fit()을 탄다. 끝나면 finally에서 원래 자리로
            foreach (var r in roots) foreach (var f in r.GetComponentsInChildren<PopupTextFit>(true)) { fits.Add(f); float d = f.Fit(); if (d > 0f) sb.Append($"  ↕ {Where(f.transform, lobby)} 판 높이 +{d:F0}\n"); }
            Canvas.ForceUpdateCanvases();
            int bad = 0;
            foreach (var t in texts)
            {
                t.ForceMeshUpdate();
                Bounds b = t.textBounds; Rect rc = t.rectTransform.rect;
                float dx = Mathf.Max(rc.xMin - b.min.x, b.max.x - rc.xMax), dy = Mathf.Max(rc.yMin - b.min.y, b.max.y - rc.yMax);
                if (dx > 1f || dy > 1f)
                {
                    bad++;
                    sb.Append($"  ✗ 글자 «{Short(t.text)}» {Where(t.transform, lobby)} — 크기 {t.fontSize:F1} · 칸 {rc.width:F0}×{rc.height:F0} · 넘침 가로 {Mathf.Max(0, dx):F0} 세로 {Mathf.Max(0, dy):F0}\n");
                }
            }
            // 줄 레이아웃이 글자 높이를 정하는 칸(카드 글자 등) — 칸 안 내용이 칸보다 크면 겹치거나 밀려난다
            foreach (var r in roots)
                foreach (var v in r.GetComponentsInChildren<VerticalLayoutGroup>(false))
                {
                    if (!v.childControlHeight || v.GetComponentInChildren<TMP_Text>(false) == null) continue;
                    var rt = (RectTransform)v.transform;
                    float pref = LayoutUtility.GetPreferredHeight(rt);
                    if (pref > rt.rect.height + 1f && v.GetComponent<ContentSizeFitter>() == null)
                    {
                        bad++;
                        sb.Append($"  ✗ 칸 {Where(v.transform, lobby)} — 내용 {pref:F0} > 칸 {rt.rect.height:F0}\n");
                    }
                }
            sb.Append(bad == 0 ? "  넘침 없음\n" : $"  넘침 {bad}곳\n");
        }
        finally
        {
            OsDisplayScale.EditorSimulate(1f, 1f);
            foreach (var f in fits) f.ResetLayout();
            for (int i = 0; i < texts.Count; i++) { texts[i].fontSize = snap[i].s; texts[i].fontSizeMin = snap[i].mn; texts[i].fontSizeMax = snap[i].mx; }
            for (int i = acts.Count - 1; i >= 0; i--) acts[i].go.SetActive(acts[i].on);
            Canvas.ForceUpdateCanvases();
        }
        Debug.Log(sb.ToString());
    }

    static string Short(string s) { s = s.Replace('\n', ' '); return s.Length > 16 ? s.Substring(0, 16) + "…" : s; }
    static string Where(Transform t, Transform stop)
    {
        string p = t.name; int n = 0;
        for (var x = t.parent; x != null && x != stop && n < 3; x = x.parent, n++) p = x.name + "/" + p;
        return p;
    }

    // 굵기 · 크기 표 — 어느 판 어느 글자에 무엇을 썼는지(사용자 10-28 «어느 부분에 볼드 미디움 라이트 폰트사이즈 뭐 쓴 건지»)
    [MenuItem("GuideChuna/UI 개편/V1.5 로비 글꼴 표 (lobby)")]
    public static void FontTable()
    {
        var lobby = Lobby();
        if (lobby == null) return;
        var rows = new System.Collections.Generic.SortedDictionary<string, System.Collections.Generic.List<string>>();
        foreach (var t in lobby.GetComponentsInChildren<TMP_Text>(true))
        {
            string w = t.font == null ? "?" : t.font.name.Contains("Bold") && t.font.name.Contains("KoPub") ? "1 Bold" : t.font.name.Contains("Medium") ? "2 Medium" : t.font.name.Contains("Light") ? "3 Light" : "9 " + t.font.name;
            string panel = t.transform.parent == null ? "" : t.transform.IsChildOf(lobby.Find("ContentUIExample-HorizonOS1")) ? "로비판" : "?";
            for (var x = t.transform; x != null && x.parent != lobby; x = x.parent) panel = x.parent != null && x.parent == lobby ? x.name : panel;
            foreach (Transform c in lobby) if (t.transform.IsChildOf(c)) panel = c.name;
            string key = $"{w} | {t.fontSize:00} | {panel}";
            string txt = (t.text ?? "").Replace('\n', ' ').Replace('\r', ' ');
            if (txt.Length > 14) txt = txt.Substring(0, 14) + "…";
            if (!rows.TryGetValue(key, out var l)) rows[key] = l = new System.Collections.Generic.List<string>();
            l.Add($"{t.name}«{txt}»");
        }
        var sb = new StringBuilder("[V15Lobby] 글꼴 표 (굵기 | 크기 | 판 → 개수 : 예)\n");
        foreach (var kv in rows)
            sb.Append(kv.Key).Append(" → ").Append(kv.Value.Count).Append(" : ").Append(string.Join(" / ", kv.Value.GetRange(0, Mathf.Min(6, kv.Value.Count)))).Append('\n');
        Debug.Log(sb.ToString());
    }

    [MenuItem("GuideChuna/UI 개편/V1.5 로비 치수 재기 (lobby)")]
    public static void Measure()
    {
        var lobby = Lobby();
        if (lobby == null) { Debug.LogWarning("[V15Lobby] 로비가 아니다"); return; }
        var plate = lobby.Find(Plate);
        var sb = new StringBuilder("[V15Lobby] 치수\n");
        void Rt(Transform t, int depth)
        {
            var r = t as RectTransform;
            var le = t.GetComponent<LayoutElement>();
            sb.Append(new string(' ', depth * 2)).Append(t.name).Append(t.gameObject.activeSelf ? "" : " [꺼짐]");
            if (r != null) sb.Append($" size {r.rect.width:F0}×{r.rect.height:F0} pos {r.anchoredPosition} aMin {r.anchorMin} aMax {r.anchorMax}");
            if (le != null) sb.Append($" LE(ign {le.ignoreLayout} min {le.minWidth}/{le.minHeight} pref {le.preferredWidth}/{le.preferredHeight} flex {le.flexibleWidth}/{le.flexibleHeight})");
            var hlg = t.GetComponent<HorizontalOrVerticalLayoutGroup>();
            if (hlg != null) sb.Append($" LG(pad {hlg.padding.left},{hlg.padding.right},{hlg.padding.top},{hlg.padding.bottom} sp {hlg.spacing} ctrl {hlg.childControlWidth}/{hlg.childControlHeight} exp {hlg.childForceExpandWidth}/{hlg.childForceExpandHeight} al {hlg.childAlignment})");
            var g = t.GetComponent<GridLayoutGroup>();
            if (g != null) sb.Append($" GRID(cell {g.cellSize} sp {g.spacing} cols {g.constraintCount} pad {g.padding.left},{g.padding.right})");
            var csf = t.GetComponent<ContentSizeFitter>();
            if (csf != null) sb.Append($" CSF({csf.horizontalFit}/{csf.verticalFit})");
            var img = t.GetComponent<Image>();
            if (img != null && img.sprite != null) sb.Append($" sprite {img.sprite.name}");
            var rb = t.GetComponent<RoundedBoxUIProperties>();
            if (rb != null) sb.Append($" R {rb.borderRadius}");
            var sr = t.GetComponent<ScrollRect>();
            if (sr != null) sb.Append($" SCROLL(vbar {(sr.verticalScrollbar != null ? sr.verticalScrollbar.name + " " + sr.verticalScrollbarVisibility + " sp " + sr.verticalScrollbarSpacing : "없음")})");
            var tx = t.GetComponent<TMP_Text>();
            if (tx != null) sb.Append($" TMP «{tx.text}» {tx.font?.name} {tx.fontSize} pref {tx.preferredHeight:F1} lines {tx.textInfo?.lineCount} valign {tx.verticalAlignment} y {((RectTransform)t).anchoredPosition.y:F1}");
            sb.Append('\n');
        }
        void Walk(Transform t, int depth, int max)
        {
            Rt(t, depth);
            if (depth >= max || (depth > 1 && t.GetComponent<LobbyItemCard>() != null)) return;
            foreach (Transform c in t) Walk(c, depth + 1, max);
        }
        Rt(lobby.Find("ContentUIExample-HorizonOS1"), 0);
        Rt(lobby.Find("ContentUIExample-HorizonOS1/CanvasRoot"), 0);
        Walk(plate, 0, 1);
        Walk(plate.Find("Horizontal"), 1, 2);
        Walk(plate.Find("Horizontal/이름"), 1, 7);
        Walk(plate.Find("Horizontal/Logout"), 1, 7);
        Walk(plate.Find("카테고리"), 1, 2);
        Walk(plate.Find("실습 시나리오"), 1, 5);
        // 카드 한 장
        foreach (var cd in lobby.GetComponentsInChildren<LobbyItemCard>(true))
            if (cd.GetComponentInChildren<TMP_Text>(true)?.text.StartsWith("두개골(OM)") == true || cd.GetComponentInChildren<TMP_Text>(true)?.text.StartsWith("상부승모근") == true) Walk(cd.transform, 1, 6);
        // 글자 — 크기 · 굵기 · 색 · 글꼴 분포
        var fonts = new System.Collections.Generic.Dictionary<string, int>();
        foreach (var t in lobby.GetComponentsInChildren<TMP_Text>(true))
        {
            string k = $"{(t.font != null ? t.font.name : "null")} {t.fontSize:F0} {(t.fontStyle & FontStyles.Bold) != 0} #{ColorUtility.ToHtmlStringRGB(t.color)}";
            fonts[k] = fonts.TryGetValue(k, out int c) ? c + 1 : 1;
        }
        sb.Append("글자(글꼴 크기 굵게 색 → 개수)\n");
        foreach (var kv in fonts) sb.Append("  ").Append(kv.Key).Append(" → ").Append(kv.Value).Append('\n');
        Debug.Log(sb.ToString());
    }
}
