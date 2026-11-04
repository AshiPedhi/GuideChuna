using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 실습 씬 UI 판의 실제 크기(m) · 켜짐 · 배율을 잰다 — 읽기 전용 (2026-10-29 사용자 «상단 메뉴 · 정보 창 · 리모컨 크기 조정» 전 실측).
/// 판마다 캔버스(CanvasRoot)의 월드 크기 = 캔버스 px × lossyScale 이다.
/// </summary>
public static class TrainingUiSizeProbe
{
    [MenuItem("GuideChuna/시험/실습 UI 판 크기 재기 (읽기 전용 · TrainingScene)")]
    public static void Run()
    {
        var group = GameObject.Find("UI Group");
        if (group == null) { Debug.LogWarning("[실습UI크기] UI Group 없음"); return; }
        var sb = new StringBuilder("[실습UI크기]\n");
        var eye = GameObject.Find("CenterEyeAnchor");
        foreach (Transform c in group.transform)
        {
            sb.Append($"■ {c.name} {(c.gameObject.activeSelf ? "켜짐" : "꺼짐")} · 로컬배율 {c.localScale.x:F3} · 위치 {c.position:F2}");
            if (eye != null) sb.Append($" · 눈에서 {Vector3.Distance(eye.transform.position, c.position):F2}m");
            sb.Append('\n');
            foreach (var cv in c.GetComponentsInChildren<Canvas>(true))
            {
                if (cv.transform.parent != null && cv.transform.parent.GetComponentInParent<Canvas>(true) != null) continue;   // 중첩 캔버스는 뺀다
                var r = (RectTransform)cv.transform;
                Vector3 s = r.lossyScale;
                sb.Append($"    캔버스 {Path(cv.transform, c)} {r.rect.width:F0}×{r.rect.height:F0}px × {s.x:F5} = {r.rect.width * s.x:F3}×{r.rect.height * s.y:F3}m{(cv.gameObject.activeInHierarchy ? "" : " (꺼짐)")}\n");
            }
            foreach (var mb in c.GetComponents<MonoBehaviour>())
            {
                if (mb == null) continue;
                var so = new SerializedObject(mb); var it = so.GetIterator(); bool first = true;
                var line = new StringBuilder();
                while (it.NextVisible(first))
                {
                    first = false;
                    if (it.propertyType == SerializedPropertyType.Float) line.Append($" {it.name}={it.floatValue:G4}");
                    else if (it.propertyType == SerializedPropertyType.Boolean) line.Append($" {it.name}={it.boolValue}");
                }
                if (line.Length > 0) sb.Append($"    [{mb.GetType().Name}]{line}\n");
            }
        }
        Debug.Log(sb.ToString());
    }

    // 문서용 캡처(10-29 «각 UI마다 캡처 · 글꼴 크기 표») — 지금 쓰는 판(상단 바 A · 리모컨 A · 진행 가이드 · 정보 창 셋)
    [MenuItem("GuideChuna/시험/실습 UI 판 캡처 · 글꼴 표 (TrainingScene)")]
    public static void CaptureAndFonts()
    {
        var group = GameObject.Find("UI Group");
        if (group == null) { Debug.LogWarning("[실습UI글꼴] UI Group 없음"); return; }
        string[] names = { "상단 바 A (시안)", "설정 리모컨 A (시안)", "진행Root", "정보 창 - 진행 및 결과", "정보 창 - 골격", "정보 창 - 영상" };
        var sb = new StringBuilder("[실습UI글꼴] (판 | 글꼴 | 크기 | 이름«글자»)\n");
        foreach (var n in names)
        {
            var t = group.transform.Find(n);
            if (t == null) { sb.Append($"  {n} 없음\n"); continue; }
            // 정보 창은 안의 페이지(Mask 바로 아래 · ignoreLayout)를 잠깐 켠다 — UIPanelPreviewCapture.CaptureAll과 같은 규칙
            GameObject page = null;
            if (n.StartsWith("정보 창 - "))
            {
                var back = t.GetComponentInChildren<UnityEngine.UI.Mask>(true);
                if (back != null)
                    foreach (Transform c in back.transform)
                        if (c.name != "GradientEffect" && c.GetComponent<UnityEngine.UI.LayoutElement>() is var le && le != null && le.ignoreLayout) { page = c.gameObject; break; }
            }
            bool pageWas = page != null && page.activeSelf;
            if (page != null) page.SetActive(true);
            UIPanelPreviewCapture.Capture(t.gameObject, "실습_" + n.Replace(" ", ""));
            foreach (var tx in t.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                if (!tx.gameObject.activeInHierarchy && !(t.gameObject.activeSelf == false)) continue;
                string s = (tx.text ?? "").Replace('\n', ' ').Trim();
                if (s.Length == 0) continue;
                if (s.Length > 16) s = s.Substring(0, 16) + "…";
                sb.Append($"  {n} | {(tx.font != null ? tx.font.name : "?")} | {tx.fontSize:0.#} | {tx.name}«{s}»{(tx.gameObject.activeInHierarchy ? "" : " (꺼짐)")}\n");
            }
            if (page != null) page.SetActive(pageWas);
        }
        Debug.Log(sb.ToString());
    }

    static string Path(Transform t, Transform root)
    {
        var s = t.name;
        for (var x = t.parent; x != null && x != root; x = x.parent) s = x.name + "/" + s;
        return s;
    }
}
