using System.Text;
using Oculus.Interaction.Surfaces;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 10-30 사용자 지시 — 실습 상단 메뉴 · 설정 리모컨 (TrainingScene · 여러 번 돌려도 같은 결과).
///   ① 상단 메뉴 버튼: 알약(타원) → 둥근 사각형(모서리 <see cref="PillRadius"/>) · 글자 = Medium 16 고정(«가까이 당긴 만큼 너무 크면 안 된다»)
///   ② 상단 메뉴 자리: 높이 따라감 끔 — 처음 생성될 때 헤드셋 기준으로 높이 · 앞뒤 고정(TopBarPlacer.followHeadHeight = false)
///   ③ 설정 리모컨: [자동 맞춤] · [완료]를 오른쪽에 위아래로 모으고 · 판 폭을 내용에 맞춰 줄이고 · 배율 0.5 → <see cref="RemoteScale"/>
/// </summary>
public static class TrainingUi1030Tool
{
    const float PillRadius = 32f;      // 버튼 높이 180의 약 1/6 — 판 모서리도 같은 꼴로(★추정값 · 헤드셋에서 보고 조정)
    const float LabelSize = 16f;
    const string MediumFontPath = "Assets/Fonts/KoPubWorld/KoPubWorld Dotum Medium SDF.asset";
    const float RemoteScale = 0.4f;    // 10-29 0.5 → «좀 더 줄여줘»
    const float DirShift = -130f;      // 방향 버튼 묶음을 왼쪽으로 — 오른쪽에 [자동 맞춤] · [완료] 칸을 낸다
    const float SideW = 200f, RowH = 88f, RowTop = 44f, RowBottom = -76f, Gap = 20f, EdgePad = 30f;

    [MenuItem("GuideChuna/UI 개편/10-30 상단 메뉴 둥근 사각 · 글자 16 · 리모컨 정리 (TrainingScene)")]
    public static void Apply()
    {
        var group = GameObject.Find("UI Group");
        if (group == null) { Debug.LogWarning("[실습UI1030] UI Group 없음 — TrainingScene이 아니다"); return; }
        var sb = new StringBuilder("[실습UI1030] 적용\n");
        var medium = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MediumFontPath);
        if (medium == null) sb.Append("  ★Medium 글꼴 없음 — 글꼴은 그대로\n");

        // ① · ② 상단 메뉴
        foreach (var p in group.GetComponentsInChildren<TopBarPlacer>(true))
        {
            var so = new SerializedObject(p);
            so.FindProperty("followHeadHeight").boolValue = false;
            so.ApplyModifiedProperties();
            sb.Append($"  ② {p.name} 높이 따라감 → 끔(처음 자리 고정)\n");
        }
        var barA = group.transform.Find("상단 바 A (시안)");
        var back = barA != null ? FindDeep(barA, "UIBackplate") : null;
        if (back != null)
        {
            var brb = back.GetComponent<RoundedBoxUIProperties>();
            if (brb != null) { Undo.RecordObject(brb, "실습UI1030"); brb.borderRadius = Vector4.one * (PillRadius + 8f); }
            int n = 0;
            foreach (Transform pill in back)
            {
                if (!pill.name.StartsWith("알약")) continue;
                foreach (var rb in pill.GetComponentsInChildren<RoundedBoxUIProperties>(true)) { Undo.RecordObject(rb, "실습UI1030"); rb.borderRadius = Vector4.one * PillRadius; }
                var lab = pill.Find("Content/Background/Elements/Text/Label")?.GetComponent<TMP_Text>();
                if (lab != null)
                {
                    // ★10-30 실측: 속성 대입(enableAutoSizing = false)은 저장 후 다시 열자 프리팹 값(켬 · 최대 38)으로 돌아가 글자가 36으로 커졌다
                    //   → 직렬화 필드로 직접 쓴다(프리팹 덮어쓰기로 남는다)
                    var so = new SerializedObject(lab);
                    so.FindProperty("m_enableAutoSizing").boolValue = false;
                    so.FindProperty("m_fontSize").floatValue = LabelSize;
                    so.FindProperty("m_fontSizeBase").floatValue = LabelSize;
                    so.FindProperty("m_fontSizeMax").floatValue = LabelSize;
                    if (medium != null) { so.FindProperty("m_fontAsset").objectReferenceValue = medium; so.FindProperty("m_sharedMaterial").objectReferenceValue = medium.material; }
                    so.FindProperty("m_fontStyle").intValue = (int)FontStyles.Normal;
                    so.ApplyModifiedProperties();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(lab);
                }
                n++;
            }
            sb.Append($"  ① 상단 바 A 버튼 {n}개 → 둥근 사각(모서리 {PillRadius}) · 글자 {LabelSize} {(medium != null ? "Medium" : "")} · 판 모서리 {PillRadius + 8f}\n");
        }
        else sb.Append("  ★상단 바 A 판을 못 찾음\n");

        // ③ 설정 리모컨
        foreach (var rm in group.GetComponentsInChildren<PatientPositionRemote>(true))
        {
            Undo.RecordObject(rm.transform, "실습UI1030"); rm.transform.localScale = Vector3.one * RemoteScale;
            RectTransform auto = null, done = null; Transform content = null;
            foreach (var r in rm.GetComponentsInChildren<RectTransform>(true))
            {
                if (r.name == "버튼 자동 맞춤") auto = r;
                else if (r.name == "버튼 완료") done = r;
            }
            if (auto == null || done == null) { sb.Append($"  ★{rm.name} [자동 맞춤] · [완료]를 못 찾음 — 배율만\n"); continue; }
            content = auto.parent;

            // 이미 돌린 판이면 방향 묶음을 다시 밀지 않는다 — [자동 맞춤]이 위 줄 높이면 정리된 것
            bool done1030 = Mathf.Abs(auto.sizeDelta.y - RowH) < 0.5f;
            if (!done1030)
                foreach (RectTransform c in content)
                {
                    if (c == auto || c == done || c.name == "글자 위치 조정") continue;
                    if (!c.name.StartsWith("버튼 ") && !c.name.StartsWith("글자 ")) continue;
                    Undo.RecordObject(c, "실습UI1030"); c.anchoredPosition += new Vector2(DirShift, 0f);
                }
            // 방향 묶음 오른쪽 끝 → 그 옆에 위아래로
            float right = float.MinValue, left = float.MaxValue;
            foreach (RectTransform c in content)
            {
                if (c == auto || c == done) continue;
                if (!c.name.StartsWith("버튼 ") && !c.name.StartsWith("글자 ")) continue;
                if (c.name.StartsWith("버튼 ")) right = Mathf.Max(right, c.anchoredPosition.x + c.sizeDelta.x / 2f);
                left = Mathf.Min(left, c.anchoredPosition.x - c.sizeDelta.x / 2f);
            }
            float x = right + Gap + SideW / 2f;
            SetButton(auto, new Vector2(x, RowTop), new Vector2(SideW, RowH));
            SetButton(done, new Vector2(x, RowBottom), new Vector2(SideW, RowH));

            // 판 폭을 내용에 맞추고 가운데로 — 손 · 레이가 닿는 영역(BoundsClipper)도 같은 비율로
            float contentRight = x + SideW / 2f;
            float newW = (contentRight - left) + 2f * EdgePad;
            float shift = -(contentRight + left) / 2f;
            foreach (RectTransform c in content)
            {
                if (!c.name.StartsWith("버튼 ") && !c.name.StartsWith("글자 ")) continue;
                Undo.RecordObject(c, "실습UI1030"); c.anchoredPosition += new Vector2(shift, 0f);
            }
            var plateBack = FindDeep(rm.transform, "UIBackplate") as RectTransform;
            var canvasRoot = FindDeep(rm.transform, "CanvasRoot") as RectTransform;
            float oldW = plateBack != null ? plateBack.sizeDelta.x : newW;
            foreach (var r in new[] { plateBack, canvasRoot })
                if (r != null) { Undo.RecordObject(r, "실습UI1030"); r.sizeDelta = new Vector2(newW, r.sizeDelta.y); }
            if (oldW > 1f && Mathf.Abs(oldW - newW) > 0.5f)
                foreach (var cl in rm.GetComponentsInChildren<BoundsClipper>(true))
                {
                    Undo.RecordObject(cl, "실습UI1030");
                    var s = cl.Size; cl.Size = new Vector3(s.x * newW / oldW, s.y, s.z);
                    EditorUtility.SetDirty(cl);
                }
            sb.Append($"  ③ {rm.name} 배율 → {RemoteScale} · [자동 맞춤] · [완료] 오른쪽 위아래(x {x + shift:F0}) · 판 폭 {oldW:F0} → {newW:F0}px\n");
        }

        EditorSceneManager.MarkSceneDirty(group.scene);
        Debug.Log(sb.Append("★저장은 Ctrl+S").ToString());
    }

    // 10-30 사용자 «실습 내부 폰트랑 반투명 적용 안 돼 있네»
    //   반투명: 판 위 GradientEffect(LinearGradientUI.shader:140)가 «그라디언트 + Image 색»으로 색을 <b>더해</b> Image 알파를 낮춰도 늘 불투명이었다(셰이더 실측).
    //           → 그라디언트 겹은 끄고 · 판(RoundedBoxUI — 알파를 그대로 쓴다) 하나로 알파 PlateAlpha
    //   글꼴: 로비와 같은 KoPubWorld돋움 세 굵기 규칙(KoPubFonts.Apply) — UI Group 아래 글자 전부
    const float PlateAlpha = 0.55f;   // 10-29 목표(두 겹 합 ≈ 0.55)를 판 한 겹으로

    [MenuItem("GuideChuna/UI 개편/10-30 실습 반투명 고침 · 글꼴 KoPub (TrainingScene)")]
    public static void ApplyTranslucentAndFont()
    {
        var group = GameObject.Find("UI Group");
        if (group == null) { Debug.LogWarning("[실습UI1030] UI Group 없음 — TrainingScene이 아니다"); return; }
        var sb = new StringBuilder("[실습UI1030] 반투명 · 글꼴\n");
        foreach (var n in new[] { "상단 바 A (시안)", "설정 리모컨 A (시안)", "설정 리모컨 B (Meta 기본)" })
        {
            var t = group.transform.Find(n); if (t == null) continue;
            var back = FindDeep(t, "UIBackplate"); if (back == null) continue;
            var img = back.GetComponent<Image>();
            if (img != null) { Undo.RecordObject(img, "실습UI1030"); var c = img.color; c.a = PlateAlpha; img.color = c; PrefabUtility.RecordPrefabInstancePropertyModifications(img); }
            var grad = back.Find("GradientEffect")?.GetComponent<Image>();
            if (grad != null) { Undo.RecordObject(grad, "실습UI1030"); grad.enabled = false; PrefabUtility.RecordPrefabInstancePropertyModifications(grad); }
            sb.Append($"  ⑥ {n} 판 알파 → {PlateAlpha} · 그라디언트 겹 {(grad != null ? "끔" : "없음")}\n");
        }

        if (!KoPubFonts.Ready) KoPubFonts.Create();
        int changed = 0, total = 0;
        foreach (var tmp in group.GetComponentsInChildren<TMP_Text>(true))
        {
            total++;
            Undo.RecordObject(tmp, "실습UI1030");
            if (KoPubFonts.Apply(tmp)) { changed++; PrefabUtility.RecordPrefabInstancePropertyModifications(tmp); }
        }
        sb.Append($"  ⑦ 글꼴 KoPub — UI Group 글자 {total}개 중 {changed}개 바꿈\n");

        EditorSceneManager.MarkSceneDirty(group.scene);
        Debug.Log(sb.Append("★저장은 Ctrl+S (환자 피부가 분홍이면 저장하지 말 것)").ToString());
    }

    // 10-30 사용자 «상단 메뉴 좌우로 너무 넓어 잘 안 들어온다 — 전체 크기 반» · «자동 맞춤 · 완료 버튼 흰색이라 안 보인다»
    //   흰색 원인(실측): 버튼 Animator(PrimaryButton_Dark)가 실행 중에 Background 색을 흰색으로 덮는다 — 에디터 미리보기는 애니메이터가 안 돌아 파랑 · 초록으로 보였다.
    //   → 두 버튼만 Animator를 끄고 색 틴트(Background 색 × 틴트)로 — 평소 0.9 · 가리킴 1.0 · 누름 0.75
    const float BarRootScale = 0.5f;
    static readonly Color AutoBlue = new Color(0.18f, 0.47f, 0.96f, 1f), DoneGreen = new Color(0.12f, 0.53f, 0.29f, 1f);

    [MenuItem("GuideChuna/UI 개편/10-30 상단 메뉴 반 크기 · 리모컨 버튼 색 (TrainingScene)")]
    public static void ApplyHalfBarAndRemoteColors()
    {
        var group = GameObject.Find("UI Group");
        if (group == null) { Debug.LogWarning("[실습UI1030] UI Group 없음 — TrainingScene이 아니다"); return; }
        var sb = new StringBuilder("[실습UI1030] 상단 메뉴 크기 · 리모컨 버튼 색\n");
        foreach (var n in new[] { "상단 바 A (시안)", "상단 바 B (Meta 기본)" })
        {
            var t = group.transform.Find(n); if (t == null) continue;
            Vector3 old = t.localScale;
            Undo.RecordObject(t, "실습UI1030"); t.localScale = Vector3.one * BarRootScale;
            sb.Append($"  ⑧ {n} 배율 {old.x:F3} → {BarRootScale}\n");
        }
        foreach (var rm in group.GetComponentsInChildren<PatientPositionRemote>(true))
            foreach (var b in rm.GetComponentsInChildren<Button>(true))
            {
                bool auto = b.name == "버튼 자동 맞춤", done = b.name == "버튼 완료";
                if (!auto && !done) continue;
                var anim = b.GetComponent<Animator>();
                if (anim != null) { Undo.RecordObject(anim, "실습UI1030"); anim.enabled = false; PrefabUtility.RecordPrefabInstancePropertyModifications(anim); }
                var bg = b.transform.Find("Content/Background")?.GetComponent<Image>();
                if (bg != null) { Undo.RecordObject(bg, "실습UI1030"); bg.color = auto ? AutoBlue : DoneGreen; PrefabUtility.RecordPrefabInstancePropertyModifications(bg); }
                foreach (var tmp in b.GetComponentsInChildren<TMP_Text>(true))
                { Undo.RecordObject(tmp, "실습UI1030"); tmp.color = Color.white; PrefabUtility.RecordPrefabInstancePropertyModifications(tmp); }
                var so = new SerializedObject(b);
                so.FindProperty("m_Transition").intValue = (int)Selectable.Transition.ColorTint;
                so.FindProperty("m_TargetGraphic").objectReferenceValue = bg;
                so.FindProperty("m_Colors.m_NormalColor").colorValue = new Color(0.9f, 0.9f, 0.9f, 1f);
                so.FindProperty("m_Colors.m_HighlightedColor").colorValue = Color.white;
                so.FindProperty("m_Colors.m_PressedColor").colorValue = new Color(0.75f, 0.75f, 0.75f, 1f);
                so.FindProperty("m_Colors.m_SelectedColor").colorValue = new Color(0.9f, 0.9f, 0.9f, 1f);
                so.FindProperty("m_Colors.m_ColorMultiplier").floatValue = 1f;
                so.ApplyModifiedProperties();
                PrefabUtility.RecordPrefabInstancePropertyModifications(b);
                sb.Append($"  ⑨ {rm.name}/{b.name} Animator 끔 · 바탕 {(auto ? "파랑" : "초록")} · 색 틴트\n");
            }
        EditorSceneManager.MarkSceneDirty(group.scene);
        Debug.Log(sb.Append("★저장은 Ctrl+S").ToString());
    }

    // 10-30 사용자 «리모컨은 무조건 환자보다 앞에» · «정보 패널 1.5배 뒤로» · «실습 메뉴 텍스트 전부 로비와 같은 규격»
    //   로비 규격(LobbyV09/V15 실측): 대화상자 제목 20 Bold · 버튼 16 Medium(모드 팝업 버튼 290×88에 16) · 구역 이름 · 설명 14 · 보조 11
    const float WindowRadiusNew = 3.6f;   // 10-29 2.4 × 1.5
    const float TitleSize = 20f, ButtonSize = 16f, BodySize = 14f;

    [MenuItem("GuideChuna/UI 개편/10-30 리모컨 앞에 그리기 · 정보 창 3.6m · 텍스트 로비 규격 (TrainingScene)")]
    public static void ApplyOnTopDistanceText()
    {
        var group = GameObject.Find("UI Group");
        if (group == null) { Debug.LogWarning("[실습UI1030] UI Group 없음 — TrainingScene이 아니다"); return; }
        var sb = new StringBuilder("[실습UI1030] 앞에 그리기 · 거리 · 텍스트\n");

        var arr = group.GetComponentInChildren<InfoWindowArranger>(true);
        if (arr != null)
        {
            var so = new SerializedObject(arr);
            float old = so.FindProperty("radius").floatValue;
            so.FindProperty("radius").floatValue = WindowRadiusNew; so.ApplyModifiedProperties();
            sb.Append($"  ⑩ 정보 창 거리 {old} → {WindowRadiusNew}m\n");
        }

        foreach (var rm in group.GetComponentsInChildren<PatientPositionRemote>(true))
        {
            if (rm.GetComponent<AlwaysOnTopUI>() == null) Undo.AddComponent<AlwaysOnTopUI>(rm.gameObject);
            int t = 0, b = 0, s = 0;
            foreach (var tmp in rm.GetComponentsInChildren<TMP_Text>(true))
            {
                float size = tmp.GetComponentInParent<Selectable>(true) != null ? ButtonSize : tmp.name == "글자 위치 조정" ? TitleSize : BodySize;
                if (size == ButtonSize) b++; else if (size == TitleSize) t++; else s++;
                SetSize(tmp, size, size == TitleSize);
            }
            sb.Append($"  ⑪ {rm.name} 늘 앞에 그리기 · 제목 {t}개 {TitleSize} · 버튼 {b}개 {ButtonSize} · 구역 {s}개 {BodySize}\n");
        }

        // 나가기 팝업: 버튼 글자 16 · 가장 큰 글자(제목) 20 · 나머지 14
        var exitSo = arr != null ? new SerializedObject(arr).FindProperty("exitConfirmPopup").objectReferenceValue as GameObject : null;
        if (exitSo != null)
        {
            var texts = exitSo.GetComponentsInChildren<TMP_Text>(true);
            TMP_Text title = null;
            foreach (var tmp in texts) if (tmp.GetComponentInParent<Selectable>(true) == null && (title == null || tmp.fontSize > title.fontSize)) title = tmp;
            foreach (var tmp in texts)
            {
                float size = tmp.GetComponentInParent<Selectable>(true) != null ? ButtonSize : tmp == title ? TitleSize : BodySize;
                sb.Append($"    나가기 «{Short(tmp.text)}» {tmp.fontSize:F0} → {size}\n");
                SetSize(tmp, size, tmp == title);
            }
        }

        EditorSceneManager.MarkSceneDirty(group.scene);
        Debug.Log(sb.Append("★저장은 Ctrl+S").ToString());
    }

    // 10-30 사용자 «화살표 버튼 텍스트는 지우고 · 왼쪽엔 버튼이 없는데 위치 조정 글자 때문에 빈 공간이 크다»
    [MenuItem("GuideChuna/UI 개편/10-30 리모컨 화살표만 · 제목 빼고 폭 줄이기 (TrainingScene)")]
    public static void ApplyRemoteArrowsOnly()
    {
        var group = GameObject.Find("UI Group");
        if (group == null) { Debug.LogWarning("[실습UI1030] UI Group 없음 — TrainingScene이 아니다"); return; }
        var sb = new StringBuilder("[실습UI1030] 리모컨 화살표만\n");
        foreach (var rm in group.GetComponentsInChildren<PatientPositionRemote>(true))
        {
            int n = 0;
            foreach (var hold in rm.GetComponentsInChildren<PatientPositionRemoteButton>(true))
                foreach (var tmp in hold.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (tmp.name == "Subtitle") continue;
                    string arrows = "";
                    foreach (char ch in tmp.text) if ("▲▼◀▶".IndexOf(ch) >= 0) arrows += ch;
                    if (arrows.Length == 0 || arrows == tmp.text) continue;
                    var so = new SerializedObject(tmp); so.FindProperty("m_text").stringValue = arrows; so.ApplyModifiedProperties();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(tmp); n++;
                }
            RectTransform content = null;
            foreach (var r in rm.GetComponentsInChildren<RectTransform>(true))
                if (r.name == "글자 위치 조정") { Undo.RecordObject(r.gameObject, "실습UI1030"); r.gameObject.SetActive(false); content = (RectTransform)r.parent; }
            if (content == null) { sb.Append($"  ★{rm.name} 제목을 못 찾음\n"); continue; }

            // 켜진 버튼 · 글자만으로 폭을 다시 잡고 가운데로 — 손 · 레이 영역(BoundsClipper)도 같은 비율
            float left = float.MaxValue, right = float.MinValue;
            foreach (RectTransform c in content)
            {
                if (!c.gameObject.activeSelf || !(c.name.StartsWith("버튼 ") || c.name.StartsWith("글자 "))) continue;
                left = Mathf.Min(left, c.anchoredPosition.x - c.sizeDelta.x / 2f); right = Mathf.Max(right, c.anchoredPosition.x + c.sizeDelta.x / 2f);
            }
            float newW = right - left + 2f * EdgePad, shift = -(right + left) / 2f;
            foreach (RectTransform c in content)
                if (c.name.StartsWith("버튼 ") || c.name.StartsWith("글자 ")) { Undo.RecordObject(c, "실습UI1030"); c.anchoredPosition += new Vector2(shift, 0f); }
            var plateBack = FindDeep(rm.transform, "UIBackplate") as RectTransform;
            var canvasRoot = FindDeep(rm.transform, "CanvasRoot") as RectTransform;
            float oldW = plateBack != null ? plateBack.sizeDelta.x : newW;
            foreach (var r in new[] { plateBack, canvasRoot })
                if (r != null) { Undo.RecordObject(r, "실습UI1030"); r.sizeDelta = new Vector2(newW, r.sizeDelta.y); }
            if (oldW > 1f && Mathf.Abs(oldW - newW) > 0.5f)
                foreach (var cl in rm.GetComponentsInChildren<BoundsClipper>(true))
                { Undo.RecordObject(cl, "실습UI1030"); var s = cl.Size; cl.Size = new Vector3(s.x * newW / oldW, s.y, s.z); EditorUtility.SetDirty(cl); }
            sb.Append($"  ⑫ {rm.name} 화살표만 {n}개 · 제목 «위치 조정» 끔 · 판 폭 {oldW:F0} → {newW:F0}px\n");
        }
        EditorSceneManager.MarkSceneDirty(group.scene);
        Debug.Log(sb.Append("★저장은 Ctrl+S").ToString());
    }

    static string Short(string s) => string.IsNullOrEmpty(s) ? "" : s.Length > 12 ? s.Substring(0, 12) + "…" : s.Replace("\n", " ");

    static void SetSize(TMP_Text tmp, float size, bool bold)
    {
        var so = new SerializedObject(tmp);
        so.FindProperty("m_enableAutoSizing").boolValue = false;
        so.FindProperty("m_fontSize").floatValue = size;
        so.FindProperty("m_fontSizeBase").floatValue = size;
        var fa = bold ? KoPubFonts.Bold : KoPubFonts.Medium;
        if (fa != null && (string.IsNullOrEmpty(tmp.text) || fa.HasCharacters(tmp.text, out _, false, false)))
        { so.FindProperty("m_fontAsset").objectReferenceValue = fa; so.FindProperty("m_sharedMaterial").objectReferenceValue = fa.material; }
        so.ApplyModifiedProperties();
        PrefabUtility.RecordPrefabInstancePropertyModifications(tmp);
    }

    static void SetButton(RectTransform r, Vector2 pos, Vector2 size)
    {
        Undo.RecordObject(r, "실습UI1030");
        r.anchoredPosition = pos; r.sizeDelta = size;
        var bg = r.Find("Content/Background");
        if (bg != null && bg.TryGetComponent<LayoutElement>(out var le)) { Undo.RecordObject(le, "실습UI1030"); le.minHeight = size.y; le.preferredHeight = size.y; }
        foreach (var tmp in r.GetComponentsInChildren<TMP_Text>(true))
        {
            if (tmp.name == "Subtitle") continue;
            Undo.RecordObject(tmp, "실습UI1030"); tmp.fontSize = 32;   // 방향 버튼(높이 88)과 같은 크기
        }
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var f = FindDeep(c, name); if (f != null) return f; }
        return null;
    }
}
