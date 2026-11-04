using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 실습 UI 크기 · 자리 조정 (2026-10-29 사용자 지시) — 여러 번 돌려도 같은 결과(곱하지 않고 값으로 놓는다). ★저장은 사람이 한다.
///   ① 상단 메뉴: 손으로 닿는 거리(수평 0.45m · 위 30° 그대로 → 눈보다 0.26m 위) · 버튼 높이 2배(알약 90 → 180px · 폭은 그대로 —
///      폭까지 2배면 바가 1.7m가 된다. 사용자 답 «버튼 크기 2배» = «메뉴 높이 2배»의 뜻)
///   ② 정면 정보 창: 3배(0.46×0.31 → 1.38×0.94m) · 1.2 → 2.4m 뒤로(사용자 선택). 좌우 간격 38°는 그대로 — 3배 판의 폭이 2.4m에서 32°라 6° 띈다(계산)
///   ③ 설정 리모컨: 절반(0.70×0.15 → 0.35×0.075m) · 조금 더 아래(0.3 → 0.4m)
/// 실측 근거: «실습 UI 판 크기 재기» 10-29 15:2x 로그.
/// </summary>
public static class TrainingUi1029Tool
{
    const float BarDistance = 0.45f, PillH = 180f, PillH0 = 90f;
    const float WindowScale = 3f, WindowRadius = 2.4f;
    const float RemoteScale = 0.5f, RemoteBelow = 0.4f;

    [MenuItem("GuideChuna/UI 개편/10-29 실습 UI 크기 · 자리 적용 (TrainingScene)")]
    public static void Apply()
    {
        var group = GameObject.Find("UI Group");
        if (group == null) { Debug.LogWarning("[실습UI1029] UI Group 없음 — TrainingScene이 아니다"); return; }
        var sb = new StringBuilder("[실습UI1029] 적용\n");

        // ① 상단 메뉴
        foreach (var p in group.GetComponentsInChildren<TopBarPlacer>(true))
        {
            var so = new SerializedObject(p);
            so.FindProperty("distance").floatValue = BarDistance;
            so.ApplyModifiedProperties();
            sb.Append($"  ① {p.name} 거리 → {BarDistance}m\n");
        }
        var barA = group.transform.Find("상단 바 A (시안)");
        if (barA != null)
        {
            int n = 0;
            var back = FindDeep(barA, "UIBackplate") as RectTransform;
            var canvas = FindDeep(barA, "CanvasRoot") as RectTransform;
            float H = PillH + 40f;
            foreach (var r in new[] { back, canvas })
                if (r != null) { Undo.RecordObject(r, "실습UI1029"); r.sizeDelta = new Vector2(r.sizeDelta.x, H); }
            var brb = back != null ? back.GetComponent<RoundedBoxUIProperties>() : null;
            if (brb != null) { Undo.RecordObject(brb, "실습UI1029"); brb.borderRadius = Vector4.one * (H / 2f); }
            if (back != null)
                foreach (Transform pill in back)
                {
                    if (pill.name.StartsWith("구분선")) { var d = (RectTransform)pill; Undo.RecordObject(d, "실습UI1029"); d.sizeDelta = new Vector2(d.sizeDelta.x, PillH - 20f); continue; }
                    if (!pill.name.StartsWith("알약")) continue;
                    // 알약 높이 90이던 칸(버튼 · 내용 · 바탕)만 180으로 — 아이콘 · 글자 칸은 그대로
                    foreach (var r in pill.GetComponentsInChildren<RectTransform>(true))
                        if (Mathf.Abs(r.sizeDelta.y - PillH0) < 0.5f || Mathf.Abs(r.sizeDelta.y - PillH) < 0.5f) { Undo.RecordObject(r, "실습UI1029"); r.sizeDelta = new Vector2(r.sizeDelta.x, PillH); }
                    foreach (var le in pill.GetComponentsInChildren<LayoutElement>(true))
                        if (Mathf.Abs(le.preferredHeight - PillH0) < 0.5f || Mathf.Abs(le.minHeight - PillH0) < 0.5f)
                        { Undo.RecordObject(le, "실습UI1029"); if (le.preferredHeight > 0) le.preferredHeight = PillH; if (le.minHeight > 0) le.minHeight = PillH; }
                    foreach (var rb in pill.GetComponentsInChildren<RoundedBoxUIProperties>(true)) { Undo.RecordObject(rb, "실습UI1029"); rb.borderRadius = Vector4.one * (PillH / 2f); }
                    n++;
                }
            // ①' 아이콘 위 · 글자 아래로 쌓는다(10-29 사용자 «아이콘이랑 글씨가 중앙에 너무 몰려 있다» — 높이만 2배라 가로 한 줄이 가운데에 작게 남았다)
            //     아이콘 64 · 글자 38(넘치면 자동으로 줄임 — OS 글자 ×1.5에서 «전문가영상»이 칸을 넘지 않게). 줄 배치(HLG)는 끄고 숫자로 놓는다
            const float IconS = 64f, LabelSize = 38f, LabelH = 56f;
            if (back != null)
                foreach (Transform pill in back)
                {
                    if (!pill.name.StartsWith("알약")) continue;
                    var el = pill.Find("Content/Background/Elements") as RectTransform;
                    if (el == null) continue;
                    var hlg = el.GetComponent<HorizontalLayoutGroup>();
                    if (hlg != null) { Undo.RecordObject(hlg, "실습UI1029"); hlg.enabled = false; }
                    Undo.RecordObject(el, "실습UI1029");
                    el.anchorMin = Vector2.zero; el.anchorMax = Vector2.one; el.offsetMin = el.offsetMax = Vector2.zero;
                    float pw = ((RectTransform)pill).sizeDelta.x;
                    void Put(Transform t, Vector2 size, float y)
                    {
                        var r = t as RectTransform; if (r == null) return;
                        Undo.RecordObject(r, "실습UI1029");
                        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
                        r.sizeDelta = size; r.anchoredPosition = new Vector2(0f, y);
                    }
                    var icon = el.Find("Icon"); if (icon != null) Put(icon, new Vector2(IconS, IconS), 28f);
                    var gap = el.Find("Gap"); if (gap != null && gap.gameObject.activeSelf) { Undo.RecordObject(gap.gameObject, "실습UI1029"); gap.gameObject.SetActive(false); }
                    var text = el.Find("Text"); if (text != null) Put(text, new Vector2(pw - 32f, LabelH), -40f);
                    var vlg = text != null ? text.GetComponent<VerticalLayoutGroup>() : null;
                    if (vlg != null) { Undo.RecordObject(vlg, "실습UI1029"); vlg.childAlignment = TextAnchor.MiddleCenter; vlg.childControlWidth = vlg.childControlHeight = true; vlg.childForceExpandWidth = vlg.childForceExpandHeight = true; }
                    var lab = text != null ? text.Find("Label")?.GetComponent<TMPro.TMP_Text>() : null;
                    if (lab != null)
                    {
                        Undo.RecordObject(lab, "실습UI1029");
                        lab.alignment = TMPro.TextAlignmentOptions.Center;
                        lab.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
                        lab.fontSize = LabelSize; lab.enableAutoSizing = true; lab.fontSizeMin = 20f; lab.fontSizeMax = LabelSize;
                    }
                }
            if (canvas != null) LayoutRebuilder.ForceRebuildLayoutImmediate(canvas);
            sb.Append($"  ① 상단 바 A 알약 {n}개 높이 {PillH0} → {PillH}px · 판 높이 {H}px({H * (canvas != null ? canvas.lossyScale.y : 0f):F3}m) · 아이콘 {IconS} 위 · 글자 {LabelSize} 아래\n");
        }

        // ② 정보 창
        var arr = group.GetComponentInChildren<InfoWindowArranger>(true);
        if (arr != null)
        {
            var so = new SerializedObject(arr);
            so.FindProperty("radius").floatValue = WindowRadius;
            var slots = so.FindProperty("slots");
            for (int i = 0; i < slots.arraySize; i++)
            {
                var f = slots.GetArrayElementAtIndex(i).FindPropertyRelative("frame").objectReferenceValue as Transform;
                if (f == null) continue;
                Undo.RecordObject(f, "실습UI1029"); f.localScale = Vector3.one * WindowScale;
                sb.Append($"  ② {f.name} 배율 → {WindowScale}\n");
            }
            so.ApplyModifiedProperties();
            sb.Append($"  ② 정보 창 거리 → {WindowRadius}m · 좌우 {so.FindProperty("sideAngle").floatValue}°\n");
        }

        // ③ 설정 리모컨
        foreach (var rm in group.GetComponentsInChildren<PatientPositionRemote>(true))
        {
            Undo.RecordObject(rm.transform, "실습UI1029"); rm.transform.localScale = Vector3.one * RemoteScale;
            var so = new SerializedObject(rm);
            so.FindProperty("spawnBelow").floatValue = RemoteBelow;
            so.ApplyModifiedProperties();
            sb.Append($"  ③ {rm.name} 배율 → {RemoteScale} · 아래 → {RemoteBelow}m\n");
        }

        // ④ 진행 현황 — 진행 가이드를 진행 현황 창 위쪽에 겹쳐 그린다(ProgressGuideDock) · 표시 글자 «수행결과» · «진행 및 결과» → «진행 현황»
        //    (오브젝트 이름은 안 바꾼다 — 이름으로 찾는 코드가 있을 수 있다. 글자만 바꾼다 · 코드에서 이 글자로 맞추는 곳 없음 확인 10-29)
        var host = arr != null ? arr.gameObject : null;
        var root = group.transform.Find("진행Root");
        var pCanvas = root != null ? FindDeep(root, "CanvasRoot") as RectTransform : null;
        var winFrame = group.transform.Find("정보 창 - 진행 및 결과");
        var wCanvas = winFrame != null ? FindDeep(winFrame, "CanvasRoot") as RectTransform : null;
        if (host != null && root != null && pCanvas != null && wCanvas != null)
        {
            var dock = host.GetComponent<ProgressGuideDock>() ?? Undo.AddComponent<ProgressGuideDock>(host);
            var so = new SerializedObject(dock);
            so.FindProperty("progressRoot").objectReferenceValue = root;
            so.FindProperty("progressCanvas").objectReferenceValue = pCanvas;
            so.FindProperty("windowCanvas").objectReferenceValue = wCanvas;
            so.ApplyModifiedProperties();
            sb.Append($"  ④ 진행 가이드 겹침(ProgressGuideDock @ {host.name}) · 진행 {pCanvas.name} → 창 {winFrame.name}\n");
        }
        else sb.Append($"  ④ ★진행 가이드 겹침 배선 실패 — host {host != null} · 진행Root {root != null} · 진행 캔버스 {pCanvas != null} · 창 캔버스 {wCanvas != null}\n");
        int renamed = 0;
        foreach (var t in group.GetComponentsInChildren<TMPro.TMP_Text>(true))
        {
            string x = t.text != null ? t.text.Trim() : "";
            if (x == "수행결과" || x == "수행 결과" || x == "진행 및 결과") { Undo.RecordObject(t, "실습UI1029"); t.text = "진행 현황"; renamed++; sb.Append($"    글자 «{x}» → «진행 현황» ({t.transform.parent?.name}/{t.name})\n"); }
        }
        sb.Append($"  ④ 표시 글자 {renamed}곳 → «진행 현황»\n");

        // ⑥ 상단 메뉴 · 리모컨 바탕만 반투명(10-29 사용자 «반투명으로 다른 UI · 패널과 구분») — 버튼 · 글자는 불투명 그대로
        //    판 = UIBackplate(RoundedBoxUI · Mask) + 그 위 GradientEffect(UISet 회색 그라디언트 겹). 둘 다 알파만 바꾼다(색은 그대로)
        const float PlateAlpha = 0.33f;   // 두 겹(판 + 그라디언트)이 겹쳐 합쳐진 불투명도 = 1 − (1 − 0.33)² ≈ 0.55 (첫 적용 0.55 × 2겹 ≈ 0.8이라 거의 불투명해 보였다)
        foreach (var n in new[] { "상단 바 A (시안)", "설정 리모컨 A (시안)", "설정 리모컨 B (Meta 기본)" })
        {
            var t = group.transform.Find(n); if (t == null) continue;
            var back = FindDeep(t, "UIBackplate");
            if (back == null) continue;
            int k = 0;
            foreach (var img in new[] { back.GetComponent<Image>(), back.Find("GradientEffect")?.GetComponent<Image>() })
            {
                if (img == null) continue;
                Undo.RecordObject(img, "실습UI1029");
                var c = img.color; c.a = PlateAlpha; img.color = c; k++;
            }
            sb.Append($"  ⑥ {n} 바탕 알파 → {PlateAlpha} ({k}겹)\n");
        }

        EditorSceneManager.MarkSceneDirty(group.scene);
        sb.Append("  ★저장은 Ctrl+S (환자 피부가 분홍이면 저장하지 말 것)");
        Debug.Log(sb.ToString());
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var r = FindDeep(c, name); if (r != null) return r; }
        return null;
    }
}
