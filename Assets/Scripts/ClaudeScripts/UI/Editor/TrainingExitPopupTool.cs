using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using K = HzUIKit;

/// <summary>
/// 실습 나가기 팝업을 로비 팝업 규격으로 새로 조립한다 (2026-10-30 사용자 «나가는 팝업에 버튼이 안 눌린다 · 글씨 크기랑 규격 로비 쪽 팝업들이랑 맞추라니까»).
/// 규격 = 로비 «로그인 안내 V0.9»(LobbyV09Tool.BuildLoginNotice): UISet 판(자체 손 · 레이 표면) · 바탕 #303030 · 모서리 24 · 안 여백 24
///   · (! 원은 10-30 뺌) · 제목 20 Bold · 본문 14 보조색 · 버튼 높이 48 알약 · 글자 14 Bold · 주 행동 = 흰 알약.
/// 옛 팝업(정보패널/CanvasRoot/Exit)은 옛 정보 패널 안에 있어 패널을 통째로 옮기고 형제를 꺼야 했다 — 그 숨김이 레이 표면까지 꺼서 버튼이 안 눌렸다.
/// 새 판은 «UI Group» 바로 아래 독립 판이라 옛 패널을 건드리지 않는다(InfoWindowArranger가 판만 상단 메뉴 아래로 옮긴다).
/// 배선: ExitPopupController(popupPanel · 제목 · 본문 · 취소/다시하기/메인으로 토글) · InfoPanelController.exitConfirmPopup · InfoWindowArranger.exitConfirmPopup.
/// 옛 팝업은 지우지 않고 꺼 둔다(프로젝트 방침). 여러 번 돌려도 같은 결과(같은 이름 판을 지우고 다시 만든다).
/// </summary>
public static class TrainingExitPopupTool
{
    const string RootName = "나가기 팝업 V1";
    const float W = 480, Pad = 24, BtnH = 48, Gap = 8;
    // ★10-30 사용자 «팝업에 아이콘은 빼고» — ! 원(48 + 틈 12)을 빼고 제목을 위 여백에 붙인다
    const float TitleY = 28, BodyY = TitleY + 32, BodyH = 66;   // 본문 세 줄(14 · 줄 22)
    const float BtnY = BodyY + BodyH + 24, H = BtnY + BtnH + Pad;

    [MenuItem("GuideChuna/UI 개편/10-30 나가기 팝업 로비 규격으로 새로 (TrainingScene)")]
    public static void Build()
    {
        var group = GameObject.Find("UI Group");
        var ctrl = Object.FindFirstObjectByType<ExitPopupController>(FindObjectsInactive.Include);
        var panel = Object.FindFirstObjectByType<InfoPanelController>(FindObjectsInactive.Include);
        var arr = Object.FindFirstObjectByType<InfoWindowArranger>(FindObjectsInactive.Include);
        if (group == null || ctrl == null || panel == null || arr == null)
        { Debug.LogWarning($"[나가기V1] UI Group {(group != null)} · ExitPopupController {(ctrl != null)} · InfoPanelController {(panel != null)} · InfoWindowArranger {(arr != null)} — TrainingScene이 아니다"); return; }

        var cso = new SerializedObject(ctrl);
        var old = cso.FindProperty("popupPanel").objectReferenceValue as GameObject;
        string oldTitle = cso.FindProperty("popupTitle").stringValue, oldBody = cso.FindProperty("popupMessage").stringValue;

        // 처음 자리 · 배율 기준 = 지금 배선된 팝업(옛 Exit 또는 지난번 V1) — 지우기 전에 읽어 둔다(다시 돌려도 같은 결과)
        bool hasPose = old != null;
        Vector3 pos = hasPose ? old.transform.position : Vector3.zero;
        Quaternion rot = hasPose ? old.transform.rotation : Quaternion.identity;
        var oldCanvas = old == null ? null : old.name == RootName ? old.GetComponentInChildren<Canvas>(true) : old.GetComponentInParent<Canvas>(true);
        float scale = oldCanvas != null ? oldCanvas.transform.lossyScale.x : 0.0005f;
        if (old != null && old.name == RootName) old = null;   // 지난번 V1은 아래에서 지운다 — 꺼 둘 옛 팝업이 아니다

        foreach (Transform t in group.transform) if (t.name == RootName) { Undo.DestroyObjectImmediate(t.gameObject); break; }
        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, RootName);
        root.layer = group.layer;
        root.transform.SetParent(group.transform, false);
        // 실제 자리는 열릴 때 InfoWindowArranger가 상단 메뉴 아래로 잡는다
        if (hasPose) root.transform.SetPositionAndRotation(pos, rot);
        root.transform.localScale = Vector3.one;   // ★ExitPopupController가 열고 닫을 때 배율 1로 되돌린다 — 배율은 CanvasRoot에만

        var back = K.Plate(root, W, H, scale);
        var title = K.Text(back, "제목", Pad, TitleY, W - 2 * Pad, 28, string.IsNullOrEmpty(oldTitle) ? "실습 종료" : oldTitle, 20, K.Text1, true, TextAlignmentOptions.Center);
        var body = K.Text(back, "본문", Pad, BodyY, W - 2 * Pad, BodyH, oldBody, 14, K.Text2, false, TextAlignmentOptions.Center, true);

        // ★11-04 사용자 «홈으로 팝업에 다시하기 삭제, 취소랑 홈으로 2가지 기능만» — 두 버튼(종전 10-30 = 취소 · 다시하기 · 메인으로 셋)
        float bw = (W - 2 * Pad - Gap) / 2f;
        var cancel = Pill(back, "취소", Pad, BtnY, bw, K.Secondary, K.Text1);
        Toggle retry = null;
        var main = Pill(back, "홈으로", Pad + bw + Gap, BtnY, bw, K.White, K.Ink);

        var fit = root.AddComponent<PopupTextFit>();   // OS 글자 크기 대응(로비 팝업과 같다)
        var fso = new SerializedObject(fit);
        fso.FindProperty("back").objectReferenceValue = back;
        fso.FindProperty("canvasRoot").objectReferenceValue = back.parent;
        fso.ApplyModifiedPropertiesWithoutUndo();

        cso.FindProperty("popupPanel").objectReferenceValue = root;
        cso.FindProperty("titleText").objectReferenceValue = title;
        cso.FindProperty("messageText").objectReferenceValue = body;
        cso.FindProperty("cancelToggle").objectReferenceValue = cancel;
        cso.FindProperty("retryToggle").objectReferenceValue = retry;
        cso.FindProperty("mainMenuToggle").objectReferenceValue = main;
        cso.ApplyModifiedProperties();
        var pso = new SerializedObject(panel); pso.FindProperty("exitConfirmPopup").objectReferenceValue = root; pso.ApplyModifiedProperties();
        var aso = new SerializedObject(arr); aso.FindProperty("exitConfirmPopup").objectReferenceValue = root; aso.ApplyModifiedProperties();

        if (old != null && old != root) { Undo.RecordObject(old, "나가기V1"); old.SetActive(false); }
        root.SetActive(false);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log($"[나가기V1] {W} × {H} 판 · 제목 20 · 본문 14 · 버튼 {bw:F0} × {BtnH} 둘(취소 · 홈으로) · 배율 {scale:F5} m/px · 배선 3곳 교체 · 옛 팝업 '{(old != null ? old.name : "none")}' 꺼 둠 · ★저장은 Ctrl+S");
    }

    [MenuItem("GuideChuna/UI 개편/10-30 나가기 팝업 V1 정면 미리보기 (TrainingScene)")]
    public static void Preview()
    {
        var group = GameObject.Find("UI Group");
        var root = group != null ? group.transform.Find(RootName) : null;
        if (root == null) { Debug.LogWarning("[나가기V1] 판이 없다 — 먼저 조립"); return; }
        bool was = root.gameObject.activeSelf; root.gameObject.SetActive(true);
        Debug.Log($"[나가기V1] 미리보기 → {UIPanelPreviewCapture.Capture(root.gameObject, "Training_나가기V1")}");
        root.gameObject.SetActive(was);
    }

    // 로비 [로그인]과 같은 모멘터리 토글(ExitPopupController가 Toggle로 받는다) — 알약 48 · 글자 14 Bold · 가리키면 밝아짐
    static Toggle Pill(RectTransform back, string label, float x, float y, float w, Color fill, Color ink)
    {
        var bg = K.Box(back, label, x, y, w, BtnH, fill, BtnH / 2f);
        var t = bg.gameObject.AddComponent<Toggle>();
        t.targetGraphic = bg; t.isOn = false;
        var cb = t.colors; cb.normalColor = Color.white; cb.highlightedColor = new Color(0.92f, 0.92f, 0.92f); cb.pressedColor = new Color(0.8f, 0.8f, 0.8f);
        cb.selectedColor = Color.white; cb.disabledColor = new Color(1, 1, 1, 0.35f); cb.fadeDuration = 0.08f; t.colors = cb;
        K.Text(bg.transform, "글자", 0, 0, w, BtnH, label, 14, ink, true, TextAlignmentOptions.Center);
        return t;
    }
}
