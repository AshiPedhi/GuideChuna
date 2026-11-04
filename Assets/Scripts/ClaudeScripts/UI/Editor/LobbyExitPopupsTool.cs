using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using K = HzUIKit;

/// <summary>
/// 로비 종료 · 로그아웃 팝업을 로비 팝업 규격으로 새로 조립한다 (2026-10-30 사용자 «로비 종료 팝업도 우리 텍스트 · 디스플레이 규격 적용됐나» → «그렇게 해 줘»
/// · «로그아웃 팝업 각도는 기울이지 말고 정면으로»).
/// 규격 = «로그인 안내 V0.9»(LobbyV09Tool.BuildLoginNotice): UISet 판 · 바탕 #303030 · 모서리 24 · 안 여백 24 · (! 원은 10-30 뺌) · 제목 20 Bold · 본문 14 보조색
///   · 버튼 높이 48 알약 · 글자 14 Bold · 주 행동 = 흰 알약(왼쪽 — 로그인 안내 [로그인][닫기]와 같은 순서) · OS 글자 크기 대응(PopupTextFit).
/// ★방향 = 로비 판의 좌우 방향만 따르고 <b>기울기는 뺀다</b>(수직으로 선다). 자리 = 로비 판 가운데에서 사용자 쪽으로 0.08m(로그인 안내와 같다).
/// ★문구 · 버튼 글자는 옛 팝업에서 그대로 읽는다(새로 짓지 않는다). 종료 팝업 본문은 LobbyAuthUI_Complete가 로그인 상태에 따라 바꾼다.
/// ★종료 팝업의 [로그아웃만]은 로그인 안 했으면 코드가 숨긴다 — 버튼 줄은 HorizontalLayoutGroup이라 숨기면 남은 버튼이 다시 줄을 맞춘다.
/// 배선: LobbyAuthUI_Complete의 exitConfirmationPopup · exitYes/No/LogoutOnlyToggle · exitMessageLabel · logoutConfirmationPopup · logoutCancel/ConfirmToggle.
/// 옛 팝업은 지우지 않고 꺼 둔다. 여러 번 돌려도 같은 결과(같은 이름 판을 지우고 다시 만든다 · 옛 문구는 처음 읽은 것을 판 이름으로 찾지 않고 배선된 옛 팝업에서 읽는다).
/// </summary>
public static class LobbyExitPopupsTool
{
    const string ExitName = "종료 팝업 V1", LogoutName = "로그아웃 팝업 V1";
    const float W = 448, Pad = 24, BtnH = 48, Gap = 8;
    // ★10-30 사용자 «팝업에 아이콘은 빼고» — ! 원(48 + 틈 12)을 빼고 제목을 위 여백에 붙인다
    const float TitleY = 28, BodyY = TitleY + 32, BodyH = 44;
    const float BtnY = BodyY + BodyH + 24, H = BtnY + BtnH + Pad;

    [MenuItem("GuideChuna/UI 개편/10-30 로비 종료 · 로그아웃 팝업 로비 규격으로 새로 (lobby)")]
    public static void Build()
    {
        var auth = Object.FindFirstObjectByType<LobbyAuthUI_Complete>(FindObjectsInactive.Include);
        var lp = auth != null ? auth.transform.Find("ContentUIExample-HorizonOS1") : null;
        if (auth == null || lp == null) { Debug.LogWarning("[로비팝업V1] LobbyAuthUI_Complete · 로비 판이 없다 — lobby가 아니다"); return; }
        var so = new SerializedObject(auth);

        // 옛 문구 — 지금 배선된 팝업에서 읽는다(지난번 V1이면 그 판에서 읽어 같은 문구를 유지)
        var oldExit = so.FindProperty("exitConfirmationPopup").objectReferenceValue as GameObject;
        var oldLogout = so.FindProperty("logoutConfirmationPopup").objectReferenceValue as GameObject;
        string exitTitle = ReadTitle(oldExit, "종료"), logoutTitle = ReadTitle(oldLogout, "로그아웃");
        string exitBody = ReadBody(oldExit, "종료하시겠습니까?"), logoutBody = ReadBody(oldLogout, "로그아웃하시겠습니까?");
        string yes = Label(so, "exitYesToggle", "종료"), no = Label(so, "exitNoToggle", "취소"), only = Label(so, "exitLogoutOnlyToggle", "로그아웃만");
        string lCancel = Label(so, "logoutCancelToggle", "취소"), lOk = Label(so, "logoutConfirmToggle", "로그아웃");

        float local = LocalScale(auth, lp);
        var sb = new System.Text.StringBuilder("[로비팝업V1]\n");

        // 종료: [종료] [로그아웃만] [취소] — 본문은 코드가 로그인 상태로 바꾼다
        var exitRoot = NewRoot(auth, lp, ExitName);
        var eb = Body(exitRoot, local, exitTitle, exitBody, out var exitLabel);
        var exitRow = Row(eb);
        var tYes = Pill(exitRow, yes, K.White, K.Ink);
        var tOnly = Pill(exitRow, only, K.Secondary, K.Text1);
        var tNo = Pill(exitRow, no, K.Secondary, K.Text1);
        so.FindProperty("exitConfirmationPopup").objectReferenceValue = exitRoot;
        so.FindProperty("exitYesToggle").objectReferenceValue = tYes;
        so.FindProperty("exitNoToggle").objectReferenceValue = tNo;
        so.FindProperty("exitLogoutOnlyToggle").objectReferenceValue = tOnly;
        so.FindProperty("exitMessageLabel").objectReferenceValue = exitLabel;
        sb.Append($"  종료 «{exitTitle}» / «{exitBody}» · [{yes}][{only}][{no}]\n");

        // 로그아웃: [로그아웃] [취소]
        var logoutRoot = NewRoot(auth, lp, LogoutName);
        var lb = Body(logoutRoot, local, logoutTitle, logoutBody, out _);
        var logoutRow = Row(lb);
        var tOk = Pill(logoutRow, lOk, K.White, K.Ink);
        var tCancel = Pill(logoutRow, lCancel, K.Secondary, K.Text1);
        so.FindProperty("logoutConfirmationPopup").objectReferenceValue = logoutRoot;
        so.FindProperty("logoutConfirmToggle").objectReferenceValue = tOk;
        so.FindProperty("logoutCancelToggle").objectReferenceValue = tCancel;
        sb.Append($"  로그아웃 «{logoutTitle}» / «{logoutBody}» · [{lOk}][{lCancel}]\n");
        so.ApplyModifiedProperties();

        foreach (var old in new[] { oldExit, oldLogout })
            if (old != null && old != exitRoot && old != logoutRoot && old.name != ExitName && old.name != LogoutName)
            { Undo.RecordObject(old, "로비팝업V1"); old.SetActive(false); sb.Append($"  옛 팝업 '{old.name}' 꺼 둠\n"); }
        // 딤(LobbyDimmer)은 지켜볼 팝업 목록에 있는 것만 본다 — 새 판을 넣고, 옛 판 · 지워진 칸(지난번 V1)은 새 판으로 바꾼다
        foreach (var dim in Object.FindObjectsByType<LobbyDimmer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var dso = new SerializedObject(dim);
            var w = dso.FindProperty("watch");
            var keep = new List<Object>();
            for (int i = 0; i < w.arraySize; i++)
            {
                var o = w.GetArrayElementAtIndex(i).objectReferenceValue;
                if (o == null || o == oldExit || o == oldLogout) continue;
                keep.Add(o);
            }
            keep.Add(exitRoot); keep.Add(logoutRoot);
            w.arraySize = keep.Count;
            for (int i = 0; i < keep.Count; i++) w.GetArrayElementAtIndex(i).objectReferenceValue = keep[i];
            dso.ApplyModifiedProperties();
            sb.Append($"  딤 '{dim.name}' 지켜볼 팝업 {keep.Count}개(새 판 2개 포함)\n");
        }
        exitRoot.SetActive(false); logoutRoot.SetActive(false);
        EditorSceneManager.MarkSceneDirty(auth.gameObject.scene);
        Debug.Log(sb.Append($"  {W} × {H} · 배율 {local:F5} · 수직(기울기 없음) · ★저장은 Ctrl+S").ToString());
    }

    [MenuItem("GuideChuna/UI 개편/10-30 로비 종료 · 로그아웃 팝업 V1 정면 미리보기 (lobby)")]
    public static void Preview()
    {
        var auth = Object.FindFirstObjectByType<LobbyAuthUI_Complete>(FindObjectsInactive.Include);
        if (auth == null) return;
        foreach (var n in new[] { ExitName, LogoutName })
        {
            var t = auth.transform.Find(n); if (t == null) continue;
            bool was = t.gameObject.activeSelf; t.gameObject.SetActive(true);
            Debug.Log($"[로비팝업V1] 미리보기 → {UIPanelPreviewCapture.Capture(t.gameObject, "Lobby_" + n.Replace(" ", ""))}");
            t.gameObject.SetActive(was);
        }
    }

    static GameObject NewRoot(LobbyAuthUI_Complete auth, Transform lp, string name)
    {
        foreach (Transform t in auth.transform) if (t.name == name) { Undo.DestroyObjectImmediate(t.gameObject); break; }
        var root = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(root, name);
        root.layer = auth.gameObject.layer;
        root.transform.SetParent(auth.transform, false);
        // ★기울기 없음 — 로비 판의 좌우 방향만 따른다(사용자 «정면 각도로»)
        Vector3 fwd = Vector3.ProjectOnPlane(lp.forward, Vector3.up);
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
        root.transform.SetPositionAndRotation(lp.position - fwd.normalized * 0.08f, Quaternion.LookRotation(fwd.normalized, Vector3.up));
        root.transform.localScale = Vector3.one;
        return root;
    }

    static float LocalScale(LobbyAuthUI_Complete auth, Transform lp)
    {
        var c = lp.GetComponentInChildren<Canvas>(true);
        float world = c != null ? c.transform.lossyScale.x : 0.00075f;
        return world / Mathf.Max(1e-6f, auth.transform.lossyScale.x);
    }

    static RectTransform Body(GameObject root, float scale, string title, string body, out TextMeshProUGUI bodyText)
    {
        var back = K.Plate(root, W, H, scale);
        K.Text(back, "제목", Pad, TitleY, W - 2 * Pad, 28, title, 20, K.Text1, true, TextAlignmentOptions.Center);
        bodyText = K.Text(back, "본문", Pad, BodyY, W - 2 * Pad, BodyH, body, 14, K.Text2, false, TextAlignmentOptions.Center, true);
        var fit = root.AddComponent<PopupTextFit>();   // OS 글자 크기 대응
        var fso = new SerializedObject(fit);
        fso.FindProperty("back").objectReferenceValue = back;
        fso.FindProperty("canvasRoot").objectReferenceValue = back.parent;
        fso.ApplyModifiedPropertiesWithoutUndo();
        return back;
    }

    // 버튼 줄 — 숨긴 버튼만큼 남은 버튼이 폭을 나눠 갖는다
    static RectTransform Row(RectTransform back)
    {
        var row = K.Rect(back, "버튼 줄", Pad, BtnY, W - 2 * Pad, BtnH);
        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = Gap; h.childControlWidth = h.childControlHeight = true; h.childForceExpandWidth = h.childForceExpandHeight = true;
        return row;
    }

    static Toggle Pill(RectTransform row, string label, Color fill, Color ink)
    {
        var bg = K.Box(row, label, 0, 0, 100, BtnH, fill, BtnH / 2f);
        bg.GetComponent<LayoutElement>().ignoreLayout = false;   // K.Rect가 레이아웃 무시로 만든다 — 줄에 맡긴다
        var t = bg.gameObject.AddComponent<Toggle>();
        t.targetGraphic = bg; t.isOn = false;
        var cb = t.colors; cb.normalColor = Color.white; cb.highlightedColor = new Color(0.92f, 0.92f, 0.92f); cb.pressedColor = new Color(0.8f, 0.8f, 0.8f);
        cb.selectedColor = Color.white; cb.disabledColor = new Color(1, 1, 1, 0.35f); cb.fadeDuration = 0.08f; t.colors = cb;
        var txt = K.Text(bg.transform, "글자", 0, 0, 100, BtnH, label, 14, ink, true, TextAlignmentOptions.Center);
        var rt = (RectTransform)txt.transform;   // 버튼 폭이 줄에서 정해지므로 글자는 버튼을 꽉 채운다
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        return t;
    }

    // ── 옛 문구 읽기 ──
    static string Label(SerializedObject so, string field, string fallback)
    {
        var t = so.FindProperty(field).objectReferenceValue as Toggle;
        var tmp = t != null ? t.GetComponentInChildren<TMP_Text>(true) : null;
        return tmp != null && !string.IsNullOrWhiteSpace(tmp.text) ? tmp.text.Trim() : fallback;
    }

    static List<TMP_Text> PlainTexts(GameObject popup)
    {
        var list = new List<TMP_Text>();
        if (popup == null) return list;
        foreach (var t in popup.GetComponentsInChildren<TMP_Text>(true))
            if (t.GetComponentInParent<Selectable>(true) == null && !string.IsNullOrWhiteSpace(t.text) && t.text.Trim() != "!") list.Add(t);
        return list;
    }

    static string ReadTitle(GameObject popup, string fallback)
    {
        TMP_Text best = null;
        foreach (var t in PlainTexts(popup)) if (best == null || t.fontSize > best.fontSize) best = t;
        return best != null ? best.text.Trim() : fallback;
    }

    static string ReadBody(GameObject popup, string fallback)
    {
        var texts = PlainTexts(popup);
        if (texts.Count < 2) return fallback;
        TMP_Text title = null;
        foreach (var t in texts) if (title == null || t.fontSize > title.fontSize) title = t;
        foreach (var t in texts) if (t != title) return t.text.Trim();
        return fallback;
    }
}
