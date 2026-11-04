using System.Collections.Generic;
using System.Text;
using Oculus.Interaction.Surfaces;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 11-04 사용자 피드백을 열린 TrainingScene에 적용한다.
///   ① 상단 버튼 순서 = 근골격 · 진행 현황 · 전문가 영상 | 설정 · 시작 · 홈으로 ([시작]은 «알약 설정»을 복제해 새로 만든다)
///   ② 버튼마다 동작(<see cref="TopBarProxyButton.PillAction"/>) · 가리키면 뜨는 설명
///   ③ 홈으로 팝업에서 [다시하기]를 끄고 [취소] · [홈으로] 둘로 다시 놓는다 · 본문의 «다시하기» 문구를 뺀다
/// ★비파괴 · 여러 번 돌려도 같다: [시작]이 이미 있으면 복제하지 않는다. 지우는 것은 없다(다시하기는 꺼 둔다 — 프로젝트 방침).
/// ★저장은 사람이 한다(Ctrl+S). 되돌릴 때는 저장하지 않고 씬을 다시 연다.
/// ★바뀐 직렬화 값은 이 도구가 다 적는다 — 코드 기본값으로는 안 바뀐다(규칙 7).
/// </summary>
public static class TopBar1104Tool
{
    const string BarA = "상단 바 A (시안)";
    const string Skel = "알약 근골격", Video = "알약 전문가영상", Result = "알약 수행결과", Divider = "구분선",
                 Settings = "알약 설정", StartPill = "알약 시작", Exit = "알약 실습종료";
    const float ExitPad = 24f, ExitGap = 8f;   // TrainingExitPopupTool 규격

    [MenuItem("GuideChuna/UI 개편/11-04 상단 메뉴 순서 · 시작 버튼 · 설명 · 홈으로 팝업 2버튼 (TrainingScene)")]
    public static void Apply()
    {
        var group = GameObject.Find("UI Group");
        var bar = group != null ? group.transform.Find(BarA) : null;
        var plate = bar != null ? bar.Find("판(UISet)/CanvasRoot/UIBackplate") as RectTransform : null;
        if (plate == null) { Debug.LogWarning("[상단1104] '상단 바 A (시안)/…/UIBackplate'가 없다 — TrainingScene이 아니다"); return; }
        var canvasRoot = plate.parent as RectTransform;
        var sb = new StringBuilder("[상단1104] 상단 메뉴 · 홈으로 팝업\n");
        Undo.SetCurrentGroupName("11-04 상단 메뉴");
        int undoGroup = Undo.GetCurrentGroup();

        RectTransform Get(string n) => plate.Find(n) as RectTransform;
        var settings = Get(Settings);
        if (settings == null) { Debug.LogWarning("[상단1104] '알약 설정'이 없다 — 중단"); return; }

        // ── [시작] 만들기(설정 복제) ──
        var start = Get(StartPill);
        bool startIsNew = start == null;
        if (start == null)
        {
            var go = Object.Instantiate(settings.gameObject, plate);
            go.name = StartPill;
            Undo.RegisterCreatedObjectUndo(go, "시작 버튼");
            start = (RectTransform)go.transform;
            var icon = FindDeep(start, "Icon");
            if (icon != null) icon.gameObject.SetActive(false);   // 톱니 아이콘이 따라온다 — 글자(시작 · 타이머)만 둔다
            sb.Append("  ① '알약 설정'을 복제해 '알약 시작' 만듦 · 아이콘 끔\n");
        }
        else sb.Append("  ① '알약 시작' 이미 있음 — 복제 안 함\n");
        StartIcon(sb, start);

        // ── 동작 · 설명 · 글자 ──
        SetPill(sb, Get(Skel), TopBarProxyButton.PillAction.HoverOnly, "환자 근골격 모습을 보여 줍니다", null, keepTarget: true);
        SetPill(sb, Get(Result), TopBarProxyButton.PillAction.HoverOnly, "지금 단계와 진행 상황을 보여 줍니다", "진행 현황", keepTarget: true);
        SetPill(sb, Get(Video), TopBarProxyButton.PillAction.ExpertVideoControls, "영상 조작 메뉴를 켜고 끕니다", "전문가 영상", keepTarget: true);
        SetPill(sb, settings, TopBarProxyButton.PillAction.ToggleTarget, "실습 설정을 엽니다", null, keepTarget: true);
        SetPill(sb, start, TopBarProxyButton.PillAction.StartTimer, "실습을 시작합니다 · 누르면 수행 시간이 흐릅니다", "시작", keepTarget: false);
        SetPill(sb, Get(Exit), TopBarProxyButton.PillAction.ToggleTarget, "실습을 마치고 로비로 갑니다", "홈으로", keepTarget: true);

        // ── 순서 · 자리 ──
        Reorder(sb, plate, canvasRoot, bar, new[] { Skel, Result, Video, Divider, Settings, StartPill, Exit }, startIsNew);

        // ── 상단 바 바로 아래 판 둘: 미디어 조작(전문가 영상 아래) · [다음](시작 · 타이머 아래) ──
        SubPlate(sb, bar, canvasRoot, Get(Video), ExpertVideoControlBar.PlateName, MediaW, MediaH);
        var startRect = Get(StartPill);
        SubPlate(sb, bar, canvasRoot, startRect, TopBarNextPlate.PlateName, startRect != null ? startRect.rect.width : 300f, MediaH);

        // ── 홈으로 팝업 ──
        ExitPopupTwoButtons(sb);

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(plate.gameObject.scene);
        Debug.Log(sb.Append("★저장은 Ctrl+S").ToString());
    }

    static void SetPill(StringBuilder sb, RectTransform pill, TopBarProxyButton.PillAction action, string tip, string label, bool keepTarget)
    {
        if (pill == null) { sb.Append("  ★버튼 하나를 못 찾음\n"); return; }
        var proxy = pill.GetComponent<TopBarProxyButton>();
        if (proxy == null) { sb.Append($"  ★{pill.name}에 TopBarProxyButton 없음\n"); return; }
        var so = new SerializedObject(proxy);
        so.FindProperty("action").enumValueIndex = (int)action;
        so.FindProperty("hoverTip").stringValue = tip;
        if (!keepTarget) so.FindProperty("target").objectReferenceValue = null;
        so.ApplyModifiedProperties();
        string old = null;
        if (label != null)
        {
            foreach (var t in pill.GetComponentsInChildren<TMP_Text>(true))
            {
                if (t.name != "Label") continue;
                old = t.text;
                var tso = new SerializedObject(t); tso.FindProperty("m_text").stringValue = label; tso.ApplyModifiedProperties();
                PrefabUtility.RecordPrefabInstancePropertyModifications(t);
            }
        }
        sb.Append($"  ② {pill.name}: 동작 {action} · 설명 «{tip}»{(label != null ? $" · 글자 «{old}» → «{label}»" : "")}\n");
    }

    /// <summary>판 안 알약 · 구분선을 새 순서로 다시 놓는다. 알약 사이 · 구분선 옆 틈은 지금 값을 그대로 쓴다. 판 폭과 손 · 레이 영역도 같이 맞춘다.</summary>
    static void Reorder(StringBuilder sb, RectTransform plate, RectTransform canvasRoot, Transform bar, string[] order, bool startIsNew)
    {
        var items = new List<RectTransform>();
        foreach (var n in order) { var r = plate.Find(n) as RectTransform; if (r != null) items.Add(r); else sb.Append($"  ★'{n}' 없음 — 자리에서 뺌\n"); }

        // 지금 자리 — 이번에 새로 만든 [시작]은 설정과 같은 자리에 겹쳐 있어 재기에서 뺀다.
        //   ★이미 있던 [시작](두 번째 실행)은 넣는다 — 빼면 지금 폭을 덜 재서 판이 또 넓어진다
        var current = new List<RectTransform>();
        foreach (RectTransform c in plate)
            if (c.gameObject.activeSelf && items.Contains(c) && !(startIsNew && c.name == StartPill)) current.Add(c);
        current.Sort((a, b) => Left(a).CompareTo(Left(b)));
        if (current.Count < 2) { sb.Append("  ★자리를 잴 버튼이 모자람 — 순서 바꾸기 건너뜀\n"); return; }
        float pillGap = -1f, dividerGap = -1f;
        for (int i = 0; i + 1 < current.Count; i++)
        {
            float g = Left(current[i + 1]) - Right(current[i]);
            bool nearDivider = current[i].name == Divider || current[i + 1].name == Divider;
            if (nearDivider) { if (dividerGap < 0f) dividerGap = g; }
            else if (pillGap < 0f) pillGap = g;
        }
        if (pillGap < 0f) pillGap = 16f;
        if (dividerGap < 0f) dividerGap = pillGap;
        float oldL = Left(current[0]), oldR = Right(current[current.Count - 1]);
        float center = (oldL + oldR) / 2f;

        float total = 0f;
        for (int i = 0; i < items.Count; i++)
        {
            total += items[i].rect.width;
            if (i + 1 < items.Count) total += (items[i].name == Divider || items[i + 1].name == Divider) ? dividerGap : pillGap;
        }
        float delta = total - (oldR - oldL);

        // 판 폭 먼저(자식 앵커가 판 크기를 따르므로) → 그 다음 자식 자리
        float oldW = plate.sizeDelta.x;
        if (Mathf.Abs(delta) > 0.5f)
        {
            foreach (var r in new[] { plate, canvasRoot })
            { Undo.RecordObject(r, "상단1104"); r.sizeDelta = new Vector2(r.sizeDelta.x + delta, r.sizeDelta.y); }
            float newW = plate.sizeDelta.x;
            if (oldW > 1f)
                foreach (var cl in bar.GetComponentsInChildren<BoundsClipper>(true))
                { Undo.RecordObject(cl, "상단1104"); var s = cl.Size; cl.Size = new Vector3(s.x * newW / oldW, s.y, s.z); EditorUtility.SetDirty(cl); }
        }

        // ★자리는 «최종 판 폭» 기준 anchoredPosition으로 준다. 알약 앵커가 판 왼쪽 가장자리라, localPosition으로 주면
        //   판 폭 변경이 그 뒤에 반영되면서 늘어난 폭의 절반만큼 밀린다(11-04 첫 실행 실측: +150px · [홈으로]가 잘림).
        //   가운데도 «지금 알약 무리의 가운데»가 아니라 판 가운데로 — 이미 밀린 줄을 다시 돌려도 바로잡힌다.
        float finalW = plate.sizeDelta.x;
        float pp = plate.pivot.x;
        center = (0.5f - pp) * finalW;
        float cursor = center - total / 2f;
        for (int i = 0; i < items.Count; i++)
        {
            var r = items[i];
            Undo.RecordObject(r, "상단1104");
            float pivotX = cursor - r.rect.xMin;   // 판 pivot 기준 알약 pivot 자리
            if (Mathf.Approximately(r.anchorMin.x, r.anchorMax.x))
                r.anchoredPosition = new Vector2(pivotX - (r.anchorMin.x - pp) * finalW, r.anchoredPosition.y);
            else { var lp = r.localPosition; lp.x = pivotX; r.localPosition = lp; }   // 가로로 늘어나는 앵커면(없을 것) 종전 방식
            r.SetSiblingIndex(plate.childCount - 1);   // 형제 순서도 같은 순서로(읽는 사람이 헷갈리지 않게)
            cursor += r.rect.width;
            if (i + 1 < items.Count) cursor += (r.name == Divider || items[i + 1].name == Divider) ? dividerGap : pillGap;
        }
        sb.Append($"  ③ 순서 {string.Join(" · ", order)} · 알약 틈 {pillGap:F0} · 구분선 틈 {dividerGap:F0} · 폭 {oldR - oldL:F0} → {total:F0}px · 판 {oldW:F0} → {plate.sizeDelta.x:F0}px\n");
    }

    [MenuItem("GuideChuna/UI 개편/11-04 상단 메뉴 A 정면 미리보기 (TrainingScene)")]
    public static void Preview()
    {
        var group = GameObject.Find("UI Group");
        var bar = group != null ? group.transform.Find(BarA) : null;
        if (bar == null) { Debug.LogWarning("[상단1104] 상단 바 A 없음"); return; }
        // 미리보기에서만 아래 판 둘을 잠깐 켠다(빈 판 — 버튼은 Play에서 채워진다)
        var subs = new[] { bar.Find(ExpertVideoControlBar.PlateName), bar.Find(TopBarNextPlate.PlateName) };
        var was = new bool[subs.Length];
        for (int i = 0; i < subs.Length; i++) if (subs[i] != null) { was[i] = subs[i].gameObject.activeSelf; subs[i].gameObject.SetActive(true); }
        Debug.Log($"[상단1104] 미리보기 → {UIPanelPreviewCapture.Capture(bar.gameObject, "Training_상단메뉴A_1104")}");
        for (int i = 0; i < subs.Length; i++) if (subs[i] != null) subs[i].gameObject.SetActive(was[i]);
    }

    static float Left(RectTransform r) => r.localPosition.x + r.rect.xMin;
    static float Right(RectTransform r) => r.localPosition.x + r.rect.xMax;

    const string StartIconPath = "Assets/Resources/Icons/TopBar/start.png";

    /// <summary>11-04 사용자 «시작 아이콘 없이 글씨만 있으니까 눈에 잘 안 보여» — 재생 삼각형 아이콘을 다른 알약과 같은 자리(Icon)에 넣는다.</summary>
    static void StartIcon(StringBuilder sb, RectTransform start)
    {
        if (AssetImporter.GetAtPath(StartIconPath) == null && System.IO.File.Exists(StartIconPath))
            AssetDatabase.ImportAsset(StartIconPath, ImportAssetOptions.ForceSynchronousImport);   // 밖에서 만든 새 파일 — 아직 안 읽혔으면 여기서
        var imp = AssetImporter.GetAtPath(StartIconPath) as TextureImporter;
        if (imp == null) { sb.Append($"  ★{StartIconPath} 없음 — 아이콘 못 넣음\n"); return; }
        if (imp.textureType != TextureImporterType.Sprite || imp.spriteImportMode != SpriteImportMode.Single)
        {
            imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single; imp.alphaIsTransparency = true;
            imp.SaveAndReimport();
        }
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(StartIconPath);
        if (sprite == null)
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(StartIconPath)) if (o is Sprite s) { sprite = s; break; }
        var icon = FindDeep(start, "Icon");
        var img = icon != null ? icon.GetComponent<Image>() : null;
        if (sprite == null || img == null) { sb.Append($"  ★시작 아이콘 실패 — 스프라이트 {(sprite != null)} · Icon Image {(img != null)}\n"); return; }
        Undo.RecordObject(img, "상단1104"); img.sprite = sprite; img.preserveAspect = true;
        Undo.RecordObject(icon.gameObject, "상단1104"); icon.gameObject.SetActive(true);
        PrefabUtility.RecordPrefabInstancePropertyModifications(img);
        sb.Append("  ① [시작] 아이콘 = 재생 삼각형(Icons/TopBar/start.png) · 켬\n");
    }

    const float MediaW = 900f, MediaH = 110f, MediaGap = 16f;

    /// <summary>
    /// 11-04 오후 사용자 «상단 버튼 누르면 영상 조작하는 메뉴 상단메뉴 바로 아래에 나오게». 상단 바의 자식 UISet 판(자체 손 · 레이 누름 영역).
    /// 버튼은 <see cref="ExpertVideoControlBar"/>가 런타임에 채운다 · 처음엔 꺼 둔다. 우리가 만든 판이라 다시 돌리면 지우고 새로 만든다.
    /// </summary>
    /// <summary>
    /// 상단 바 바로 아래, <paramref name="under"/> 알약의 가로 가운데에 UISet 판(자체 손 · 레이 누름 영역)을 만든다. 처음엔 꺼 둔다.
    /// 우리가 만든 판이라 다시 돌리면 지우고 새로 만든다. 버튼은 런타임 컴포넌트가 채운다.
    /// ★11-04 사용자 «다음으로 넘기는 표시 시작 타이머 밑으로» — 미디어 판(가운데)과 겹치지 않게 미디어는 [전문가 영상] 아래, [다음]은 [시작] 아래.
    /// </summary>
    static void SubPlate(StringBuilder sb, Transform bar, RectTransform barCanvas, RectTransform under, string name, float w, float h)
    {
        var old = bar.Find(name);
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        var root = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(root, name);
        root.layer = bar.gameObject.layer;
        root.transform.SetParent(bar, false);
        root.transform.localScale = Vector3.one;
        root.transform.rotation = barCanvas.parent.rotation;   // 판(UISet)과 같은 방향
        Rect r = barCanvas.rect;
        float cx = r.center.x;
        if (under != null) cx = barCanvas.InverseTransformPoint(under.TransformPoint(under.rect.center)).x;
        root.transform.position = barCanvas.TransformPoint(new Vector3(cx, r.yMin - MediaGap - h * 0.5f, 0f));
        float scale = barCanvas.lossyScale.x / Mathf.Max(1e-6f, root.transform.lossyScale.x);
        HzUIKit.Plate(root, w, h, scale, h * 0.5f);
        root.SetActive(false);
        sb.Append($"  ⑤ '{name}' — '{(under != null ? under.name : "가운데")}' 아래 {MediaGap:F0}px · {w:F0}×{h:F0}px · 처음엔 꺼 둠\n");
    }

    static void ExitPopupTwoButtons(StringBuilder sb)
    {
        var ctrl = Object.FindFirstObjectByType<ExitPopupController>(FindObjectsInactive.Include);
        if (ctrl == null) { sb.Append("  ★ExitPopupController 없음 — 팝업 건너뜀\n"); return; }
        var so = new SerializedObject(ctrl);
        var retry = so.FindProperty("retryToggle").objectReferenceValue as Toggle;
        var cancel = so.FindProperty("cancelToggle").objectReferenceValue as Toggle;
        var main = so.FindProperty("mainMenuToggle").objectReferenceValue as Toggle;
        if (retry != null && retry.gameObject.activeSelf) { Undo.RecordObject(retry.gameObject, "상단1104"); retry.gameObject.SetActive(false); }
        sb.Append($"  ④ 다시하기 {(retry != null ? "꺼 둠" : "없음")}\n");

        if (cancel != null && main != null && cancel.transform.parent is RectTransform back)
        {
            float w = back.rect.width, bw = (w - 2f * ExitPad - ExitGap) / 2f;
            Place((RectTransform)cancel.transform, ExitPad, bw);
            Place((RectTransform)main.transform, ExitPad + bw + ExitGap, bw);
            sb.Append($"  ④ 취소 · 홈으로 두 버튼 {bw:F0}px 폭으로 다시 놓음(판 {w:F0}px)\n");
        }
        else sb.Append("  ★취소/메인으로 토글을 못 찾음 — 자리 그대로\n");

        const string msg = "진행 중인 실습을 마칩니다.<br>로비로 나가려면 홈으로를 누르세요.";
        so.FindProperty("mainMenuToggleText").stringValue = "홈으로";
        so.FindProperty("popupMessage").stringValue = msg;
        // 판에 박힌 본문 글자도 같게(실행 중엔 ExitPopupController가 위 문구로 덮는다 — 에디터 · 미리보기에서도 같은 말을 하게)
        if (so.FindProperty("messageText").objectReferenceValue is TMP_Text body)
        { var bso = new SerializedObject(body); bso.FindProperty("m_text").stringValue = msg; bso.ApplyModifiedProperties(); }
        so.FindProperty("evaluationWarningMessage").stringValue = "지금 나가면 이번 평가는 미완료로 기록됩니다.<br>로비로 나가려면 홈으로를 누르세요.";
        so.ApplyModifiedProperties();
        if (main != null)
            foreach (var t in main.GetComponentsInChildren<TMP_Text>(true))
            { var tso = new SerializedObject(t); tso.FindProperty("m_text").stringValue = "홈으로"; tso.ApplyModifiedProperties(); }
        sb.Append("  ④ 버튼 글자 «메인으로» → «홈으로» · 본문 · 평가 경고 문구에서 «다시하기» 뺌\n");
    }

    /// <summary>나가기 팝업 알약(HzUIKit 좌상단 기준 자리) — 가로 자리 · 폭과 글자 칸 폭을 같이.</summary>
    static void Place(RectTransform r, float x, float w)
    {
        Undo.RecordObject(r, "상단1104");
        r.anchoredPosition = new Vector2(x, r.anchoredPosition.y);
        r.sizeDelta = new Vector2(w, r.sizeDelta.y);
        foreach (RectTransform c in r)
        { Undo.RecordObject(c, "상단1104"); c.sizeDelta = new Vector2(w, c.sizeDelta.y); }
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var f = FindDeep(c, name); if (f != null) return f; }
        return null;
    }
}
