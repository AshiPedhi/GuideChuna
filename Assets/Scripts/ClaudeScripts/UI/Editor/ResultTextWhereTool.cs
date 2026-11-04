using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 진단(읽기 전용 · 11-04): 실습 결과 글자(<see cref="TrainingResultTextDisplay"/>.resultText)가 씬 어디에 있는지,
/// 위로 올라가며 켜짐 · 크기 · 배율을 Editor.log에 찍는다. 사용자 «결과창이 안 보여 — 실습 결과 어디로 간 거야».
/// 아무것도 바꾸지 않는다.
/// </summary>
public static class ResultTextWhereTool
{
    [MenuItem("GuideChuna/진단/결과 글자 자리 찍기 (TrainingScene)")]
    public static void Dump()
    {
        var disp = Object.FindFirstObjectByType<TrainingResultTextDisplay>(FindObjectsInactive.Include);
        if (disp == null) { Debug.LogWarning("[결과자리] TrainingResultTextDisplay 없음"); return; }
        var so = new SerializedObject(disp);
        var text = so.FindProperty("resultText").objectReferenceValue as TMP_Text;
        var panel = so.FindProperty("resultPanel").objectReferenceValue as GameObject;
        var info = Object.FindFirstObjectByType<InfoPanelController>(FindObjectsInactive.Include);
        var page = info != null ? new SerializedObject(info).FindProperty("resultPage").objectReferenceValue as GameObject : null;
        var sb = new StringBuilder("[결과자리]\n");
        sb.Append($"  resultPanel = {Path(panel)}\n  InfoPanel.resultPage = {Path(page)} · 같은 오브젝트 {(panel != null && panel == page)}\n");
        if (text == null) { Debug.Log(sb.Append("  ★resultText 없음").ToString()); return; }
        sb.Append($"  글자 «{Short(text.text)}» · 크기 {text.fontSize} · rect {text.rectTransform.rect.size} · 색 a={text.color.a:F2} · enabled {text.enabled}\n");
        for (var t = text.transform; t != null; t = t.parent)
        {
            var rt = t as RectTransform;
            var cg = t.GetComponent<CanvasGroup>();
            sb.Append($"    {(t.gameObject.activeSelf ? "켬" : "★끔")} '{t.name}' scale {t.localScale.x:F4}{(rt != null ? $" rect {rt.rect.width:F0}×{rt.rect.height:F0}" : "")}{(cg != null ? $" CanvasGroup α {cg.alpha:F2}" : "")}{(t.GetComponent<Canvas>() != null ? " [Canvas]" : "")}\n");
        }
        if (page != null)
        {
            sb.Append("  진행 현황 창 자식(위 = 먼저 그림 · 아래 = 위에 덮음):\n");
            foreach (Transform c in page.transform)
            {
                var img = c.GetComponent<UnityEngine.UI.Graphic>();
                sb.Append($"    [{c.GetSiblingIndex()}] {(c.gameObject.activeSelf ? "켬" : "끔")} '{c.name}'{(img != null ? $" {img.GetType().Name} α {img.color.a:F2}" : "")}\n");
            }
        }
        Debug.Log(sb.ToString());
    }

    /// <summary>결과 글자에 시험 문구를 넣고 진행 현황 창을 켜서 찍는다 → 원래대로 되돌린다. 글자 · 글꼴 · 재질 정보도 찍는다.</summary>
    [MenuItem("GuideChuna/진단/결과 글자 시험 표시 찍기 (TrainingScene)")]
    public static void TestRender()
    {
        var disp = Object.FindFirstObjectByType<TrainingResultTextDisplay>(FindObjectsInactive.Include);
        var text = disp != null ? new SerializedObject(disp).FindProperty("resultText").objectReferenceValue as TMP_Text : null;
        if (text == null) { Debug.LogWarning("[결과자리] resultText 없음"); return; }
        var page = text.transform.parent.gameObject;
        var window = page.transform.root;   // UI Group
        Transform win = text.transform;
        while (win.parent != null && !win.parent.name.StartsWith("UI Group")) win = win.parent;   // «정보 창 - 진행 및 결과»
        string oldText = text.text; bool oldPage = page.activeSelf;
        text.text = "제2늑골 상방변위 연습 완료!\n전부 · 교정 · 재평가 시험 문구 123";
        page.SetActive(true);
        text.ForceMeshUpdate();
        var rt0 = text.rectTransform; var cr = text.canvasRenderer;
        Debug.Log($"[결과자리] 글자 오브젝트 · 레이어 {LayerMask.LayerToName(text.gameObject.layer)}({text.gameObject.layer}) · 창 레이어 {LayerMask.LayerToName(page.layer)}({page.layer}) · 로컬 회전 {rt0.localEulerAngles} · 배율 {rt0.localScale} · 앵커 {rt0.anchorMin}-{rt0.anchorMax} · 위치 {rt0.anchoredPosition} · 피벗 {rt0.pivot} · maskable {text.maskable} · cull {cr.cull} · 투명 메시 컬 {cr.cullTransparentMesh} · 부모 CanvasGroup {(page.GetComponentInParent<CanvasGroup>() != null ? page.GetComponentInParent<CanvasGroup>().alpha.ToString() : "없음")}");
        var fa = text.font; var mat = text.fontSharedMaterial;
        Debug.Log($"[결과자리] 시험 표시 · 글꼴 {(fa != null ? fa.name : "없음")} · 한글 '완' 있음 {(fa != null && fa.HasCharacter('완', true, true))} · 재질 {(mat != null ? mat.name + " / " + mat.shader.name : "없음")} · 그린 글자 수 {text.textInfo.characterCount} · 보이는 글자 {text.textInfo.characterCount - CountInvisible(text)} · 색 {text.color} · 정렬 {text.alignment} · 넘침 {text.overflowMode}");
        Debug.Log($"[결과자리] 찍음 → {UIPanelPreviewCapture.Capture(win.gameObject, "Training_결과글자시험")} · 글자 z {text.rectTransform.localPosition.z} · 아틀라스 {(fa != null && fa.atlasTexture != null ? fa.atlasTexture.width + "×" + fa.atlasTexture.height : "없음")} · 모드 {(fa != null ? fa.atlasPopulationMode.ToString() : "-")}");
        // 개요 화면(잘 보임)과 같은 글꼴로 바꿔 한 번 더 — 보이면 글꼴 asset 문제다
        var okFont = KoreanFontResolver.Resolve();
        if (okFont != null && okFont != fa)
        {
            text.font = okFont; text.ForceMeshUpdate();
            Debug.Log($"[결과자리] 글꼴 '{okFont.name}'로 바꿔 찍음 → {UIPanelPreviewCapture.Capture(win.gameObject, "Training_결과글자시험_글꼴바꿈")}");
            text.font = fa;
        }
        text.text = oldText; page.SetActive(oldPage);
    }

    /// <summary>
    /// ★11-04 사용자 «실습 결과가 안 나와». 실측: 결과 글자(«진행 및 결과/Text (TMP)»)의 앵커는 창 전체인데 위치가 (46.5, 878.5) —
    ///   창(595px) 밖 위로 밀려 판 마스크에 잘렸다(글자는 매번 246자 들어가 있었다). 위치만 (0, 0)으로 — 크기(사방 여백)는 그대로.
    /// </summary>
    [MenuItem("GuideChuna/진단/결과 글자 자리 바로잡기 (TrainingScene)")]
    public static void FixPosition()
    {
        var disp = Object.FindFirstObjectByType<TrainingResultTextDisplay>(FindObjectsInactive.Include);
        var text = disp != null ? new SerializedObject(disp).FindProperty("resultText").objectReferenceValue as TMP_Text : null;
        if (text == null) { Debug.LogWarning("[결과자리] resultText 없음"); return; }
        var rt = text.rectTransform;
        var old = rt.anchoredPosition;
        Undo.RecordObject(rt, "결과 글자 자리");
        rt.anchoredPosition = Vector2.zero;
        PrefabUtility.RecordPrefabInstancePropertyModifications(rt);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(text.gameObject.scene);
        Debug.Log($"[결과자리] 위치 {old} → (0, 0) · 크기 {rt.rect.width:F0}×{rt.rect.height:F0} · ★저장은 Ctrl+S");
    }

    static int CountInvisible(TMP_Text t)
    {
        int n = 0;
        for (int i = 0; i < t.textInfo.characterCount; i++) if (!t.textInfo.characterInfo[i].isVisible) n++;
        return n;
    }

    static string Path(GameObject g)
    {
        if (g == null) return "없음";
        var s = g.name;
        for (var t = g.transform.parent; t != null; t = t.parent) s = t.name + "/" + s;
        return s;
    }

    static string Short(string s) => string.IsNullOrEmpty(s) ? "(빈 글자)" : (s.Length > 30 ? s.Substring(0, 30) + "…" : s).Replace("\n", " ");
}
