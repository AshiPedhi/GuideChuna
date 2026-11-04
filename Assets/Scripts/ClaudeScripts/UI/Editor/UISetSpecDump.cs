using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Meta UISet 규격 실측 (2026-10-27 사용자 «Meta UI 분석을 하고 진행하면 되잖아»).
///
/// UISet 프리팹을 하나씩 씬 밖 먼 자리(+2000m)에 잠깐 띄워 레이아웃을 돌린 뒤 실제 값을 뽑는다:
///   크기(px) · 레이아웃 여백 · 간격 · 둥근 정도(borderRadius) · 글자 크기 · 굵기 · 정렬 · 색 · 아이콘 크기.
/// 판마다 캡처도 한 장 남긴다. 출력 = &lt;프로젝트&gt;/Captures/UISetSpec/(spec.tsv · *.png).
/// ★열린 씬을 dirty로 만들지 않는다(HideAndDontSave로 띄우고 바로 지운다).
/// </summary>
public static class UISetSpecDump
{
    const string Root = "Packages/com.meta.xr.sdk.interaction/Runtime/Sample/Objects/UISet/Prefabs";

    [MenuItem("GuideChuna/UI 개편/Meta UISet 규격 재기")]
    public static void Dump()
    {
        string outDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Captures", "UISetSpec");
        Directory.CreateDirectory(outDir);
        var sb = new StringBuilder("prefab\tpath\tcomponent\tkey\tvalue\n");
        var guids = AssetDatabase.FindAssets("t:Prefab", new[] { Root });
        int n = 0;
        foreach (var g in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;
            string pname = prefab.name;

            // 캔버스가 없는 부품은 임시 캔버스에 넣어 레이아웃을 돌린다
            var host = new GameObject("UISetSpecHost") { hideFlags = HideFlags.HideAndDontSave };
            host.transform.position = new Vector3(2000, 2000, 2000);
            var inst = (GameObject)Object.Instantiate(prefab, host.transform);
            inst.hideFlags = HideFlags.HideAndDontSave;
            Canvas canvas = inst.GetComponentInChildren<Canvas>(true);
            if (canvas == null)
            {
                var cgo = new GameObject("임시 캔버스", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                cgo.hideFlags = HideFlags.HideAndDontSave;
                cgo.transform.SetParent(host.transform, false);
                var c = cgo.GetComponent<Canvas>(); c.renderMode = RenderMode.WorldSpace;
                ((RectTransform)cgo.transform).sizeDelta = new Vector2(800, 600);
                cgo.transform.localScale = Vector3.one * 0.0005f;
                inst.transform.SetParent(cgo.transform, false);
            }
            foreach (var rt in inst.GetComponentsInChildren<RectTransform>(true)) LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            Canvas.ForceUpdateCanvases();

            foreach (var t in inst.GetComponentsInChildren<Transform>(true))
            {
                string rel = Rel(inst.transform, t);
                if (t is RectTransform r)
                    Row(sb, pname, rel, "Rect", "size", $"{r.rect.width:0.#}×{r.rect.height:0.#}" + (t.gameObject.activeSelf ? "" : " (꺼짐)"));
                foreach (var lg in t.GetComponents<HorizontalOrVerticalLayoutGroup>())
                    Row(sb, pname, rel, lg.GetType().Name, "padding·spacing·align",
                        $"L{lg.padding.left} R{lg.padding.right} T{lg.padding.top} B{lg.padding.bottom} · {lg.spacing} · {lg.childAlignment}");
                var le = t.GetComponent<LayoutElement>();
                if (le != null) Row(sb, pname, rel, "LayoutElement", "min·pref·flex", $"{le.minWidth}/{le.minHeight} · {le.preferredWidth}/{le.preferredHeight} · {le.flexibleWidth}/{le.flexibleHeight}");
                var rb = t.GetComponent<RoundedBoxUIProperties>();
                if (rb != null) Row(sb, pname, rel, "RoundedBox", "radius", rb.borderRadius.ToString("0.#"));
                var img = t.GetComponent<Image>();
                if (img != null) Row(sb, pname, rel, "Image", "color·sprite", $"#{ColorUtility.ToHtmlStringRGBA(img.color)} · {(img.sprite != null ? img.sprite.name : "-")}");
                var tmp = t.GetComponent<TMP_Text>();
                if (tmp != null) Row(sb, pname, rel, "TMP", "size·style·align·color·text",
                    $"{tmp.fontSize} · {tmp.fontStyle} · {tmp.alignment} · #{ColorUtility.ToHtmlStringRGBA(tmp.color)} · «{tmp.text.Replace("\n", "⏎")}» · {(tmp.font != null ? tmp.font.name : "-")}");
            }

            // 캡처 — 판이 있으면 그 캔버스, 없으면 임시 캔버스
            try { UIPanelPreviewCapture.Capture(host, "../UISetSpec/" + pname); } catch { }
            Object.DestroyImmediate(host);
            n++;
        }
        File.WriteAllText(Path.Combine(outDir, "spec.tsv"), sb.ToString(), new UTF8Encoding(false));
        Debug.Log($"[UISetSpec] 프리팹 {n}개 규격 → {outDir}/spec.tsv");
    }

    static void Row(StringBuilder sb, string p, string path, string comp, string key, string val)
        => sb.Append(p).Append('\t').Append(path).Append('\t').Append(comp).Append('\t').Append(key).Append('\t').Append(val).Append('\n');

    static string Rel(Transform root, Transform t)
    {
        if (t == root) return ".";
        string s = t.name;
        while (t.parent != null && t.parent != root) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }
}
