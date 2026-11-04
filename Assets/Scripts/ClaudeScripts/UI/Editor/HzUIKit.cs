using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI 개편 공통 조립 도구 — ★실제 Quest 화면 실측(V1.0 · 2026-10-27 · 사용자 «실제 메타퀘스트 로비 · 스토어 UI를 분석하라»).
/// 근거 = Horizon Store 창 캡처 · v2.7 설정 창 캡처(Horizon_OS_v2.7_UI_조사보고서 · UploadVR)를 픽셀로 잼 → 1024 폭으로 환산.
///   스토어 창: 판 #262C42→#242D38(남색 끼) · 모서리 약 24 · 안쪽 32 · 검색 알약 40 · 카드 사진 16:9 모서리 16 · 간격 12 · 구역 제목 24 굵게 · 카드 제목 16 · 정보줄 #A9B4BC
///   설정 창: 고른 칸 = 흰 알약 #F6F6F6(어두운 글자) · 켬 스위치 = 초록 #2AD21B · 행 카드 = 판보다 살짝 밝은 칸 · ★파란 강조는 실제 화면에 없다
/// (아래 V0.9 설명은 공식 문서 최소값 — 실측과 다른 곳은 실측을 따른다)
///
/// [규격] 누르는 칸 ≥ 48 · 손 주요 버튼 60 · 글자 ≥ 14 · Headline 32/24/20 · Body 14 · 모서리 단계 8/12/16/20/24 · 알약 = 높이 반.
/// [색]   판 = UISet 그라디언트(#414141→#272727 근사) · 글자 #F1F4F7 · 보조 #A6A9AD · 흰(고른 것) #F1F4F7 · Meta 파랑 #0064E0(주 행동 · 고른 것)
///        보조 버튼 = 판 위 white15 · 타일 = white10 · 노랑 #F0AA19(경고 · 준비 중).
/// ★판(손 · 레이 상호작용)만 UISet EmptyUIBackplateWithCanvas를 쓰고, 버튼 · 글자는 숫자 그대로 직접 짠다 —
///   UISet 버튼 부품은 높이 40 · 글자 14로 내부 배치가 고정돼 있어 V0.9 숫자를 따르지 못했다(10-27 «느낌만 비슷» 원인).
/// ★둥근 모서리는 RoundedBoxUI 재질이 있어야 보인다(재질 없이 RoundedBoxUIProperties만 붙이면 네모).
/// </summary>
public static class HzUIKit
{
    public const string BackplateGuid = "6b196de96e7d16b4297c1980179ae439";
    public const string RoundMatGuid = "2c59325cff8447d47899d7e518a87833";      // UISet RoundedBoxUI.mat
    public const string KoreanFontGuid = "561f4e50cf36d5a498aa8dd400160e55";   // NotoSansKR-Bold(씬 UI 기본 글꼴)

    // ── 실측(★현재 UI: 2025-10 홈 피드 창 · 2026 설정 창 · 2026 라이브러리/Navigator) ──
    //   2024 스토어 캡처의 남색(#262C42)은 구형 UI라 버렸다(사용자 지적 10-27)
    public static readonly Color Panel = Hex("#303030");       // 피드 창 판 위 #353535 · 아래 #292929 · 설정 창 #343434의 가운데
    public static readonly Color Text1 = Hex("#F0F0F0");
    public static readonly Color Text2 = Hex("#A3A3A3");       // 보조 글자(중성 회색)
    public static readonly Color White = Hex("#F6F6F6");       // 설정 분할 선택의 고른 칸
    public static readonly Color Ink = Hex("#272727");
    public static readonly Color Blue = White;                 // ★실제 화면엔 파란 강조가 없다 — 고른 것 · 주 행동 = 흰 알약(옛 이름 호환)
    public static readonly Color Green = Hex("#2AD21B");       // 켬 스위치
    public static readonly Color Yellow = Hex("#F0AA19");
    public static readonly Color Secondary = Hex("#4A4A4A");   // 판 위 보조 칸(설정 검색 칸 #525252 · 행 카드 #454545 사이)
    public static readonly Color Tile = Hex("#424242");        // 행 카드 · 타일(설정 #454545)
    public static readonly Color Track = Hex("#262626");       // 분할 선택 트랙(판보다 살짝 어두움 · 설정 #323232 대 판 #343434)
    public static readonly Color Photo = Hex("#6E6A64");

    public enum Style { Primary, Cta, Secondary, Tile, Borderless }

    static Material roundMat; static TMP_FontAsset font;
    public static Material RoundMat => roundMat != null ? roundMat : roundMat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(RoundMatGuid));
    public static TMP_FontAsset Font => font != null ? font : font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(KoreanFontGuid));

    public static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

    /// <summary>UISet 판 하나(손 · 레이 상호작용 포함). 판 크기 w × h(px) · 실제 배율 = scale(m/px). 반환 = 내용을 놓을 UIBackplate.</summary>
    public static RectTransform Plate(GameObject root, float w, float h, float scale, float radius = 24)
    {
        var bp = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(BackplateGuid));
        var plate = (GameObject)PrefabUtility.InstantiatePrefab(bp, root.transform);
        plate.name = "판(UISet)";
        foreach (var tm in plate.GetComponentsInChildren<MonoBehaviour>(true))   // ★UISet 테마 관리자가 판 색을 회색 그라디언트로 덮는다 — 끈다
            if (tm != null && tm.GetType().Name == "UIThemeManager") tm.enabled = false;
        var canvasRoot = (RectTransform)Find(plate.transform, "CanvasRoot");
        canvasRoot.localScale = Vector3.one * scale; canvasRoot.sizeDelta = new Vector2(w, h);
        var back = (RectTransform)Find(plate.transform, "UIBackplate");
        back.anchorMin = back.anchorMax = new Vector2(0.5f, 0.5f); back.anchoredPosition = Vector2.zero; back.sizeDelta = new Vector2(w, h);
        var vlg = back.GetComponent<VerticalLayoutGroup>(); if (vlg != null) vlg.enabled = false;   // 자리는 숫자로 직접 놓는다
        var rb = back.GetComponent<RoundedBoxUIProperties>(); if (rb != null) rb.borderRadius = Vector4.one * radius;
        var bimg = back.GetComponent<Image>(); if (bimg != null) { bimg.material = RoundMat; bimg.color = Panel; }   // 실측 남색 판(UISet 회색 그라디언트 대신)
        var gfx = back.Find("GradientEffect"); if (gfx != null) gfx.gameObject.SetActive(false);   // ★UISet 회색 그라디언트 겹 — 켜져 있으면 판이 회색으로 보인다
        return back;
    }

    /// <summary>판 좌상단 기준 좌표(px)로 자리 잡은 빈 RectTransform.</summary>
    public static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h);
        go.AddComponent<LayoutElement>().ignoreLayout = true;
        return rt;
    }

    public static Image Box(Transform parent, string name, float x, float y, float w, float h, Color c, float radius)
    {
        var rt = Rect(parent, name, x, y, w, h);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = c; img.material = RoundMat; img.raycastTarget = true;
        rt.gameObject.AddComponent<RoundedBoxUIProperties>().borderRadius = Vector4.one * Mathf.Min(radius, Mathf.Min(w, h) / 2f);
        return img;
    }

    /// <summary>목록을 감싸는 바탕 상자(10-29 사용자 «두 리스트 감싼 경계 — 줄이 아니라 백그라운드로 조금 다른 색»).
    /// 선 없이 판(#303030)과 살짝 다른 색으로 채운 둥근 상자 하나 · 레이를 안 받는다(뒤 목록 끌기를 막지 않게).
    /// 종전(같은 날 첫 판) = 2px 선(#4A4A4A) + 안쪽 판 색 — «줄만 그으라는 게 아니다»로 바꿈.</summary>
    public const float FrameRadius = 16f;  // 카드 · 이름 타일 모서리와 같다
    public static readonly Color FrameColor = Track;   // #262626 — 판보다 살짝 어두운 칸(Quest 설정 분할 선택 트랙). 밝은 쪽은 Tile(#424242)이 이름 타일과 겹친다
    public static Image Frame(Transform parent, string name, float x, float y, float w, float h)
    {
        var box = Box(parent, name, x, y, w, h, FrameColor, FrameRadius);
        box.raycastTarget = false;
        return box;
    }

    public static TextMeshProUGUI Text(Transform parent, string name, float x, float y, float w, float h, string text, float size,
                                       Color c, bool bold = false, TextAlignmentOptions align = TextAlignmentOptions.Left, bool wrap = false)
    {
        var rt = Rect(parent, name, x, y, w, h);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.color = c; t.alignment = align; t.raycastTarget = false;
        if (bold) t.fontStyle = FontStyles.Bold;
        t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;   // ★말줄임 금지
        if (Font != null) t.font = Font;
        KoPubFonts.Apply(t, bold && size >= 16f);   // 10-28 KoPubWorld돋움 세 굵기(자산이 있으면) — 다시 조립해도 옛 글꼴로 안 돌아가게
        return t;
    }

    public static Color Fill(Style s) => s switch
    {
        Style.Primary => White, Style.Cta => Blue, Style.Secondary => Secondary, Style.Tile => Tile, _ => new Color(1, 1, 1, 0f)
    };
    public static Color Ink4(Style s) => (s == Style.Primary || s == Style.Cta) ? Ink : Text1;

    /// <summary>버튼(Button) — 높이 ≤ 60이면 알약, 더 높으면 모서리 24. 글자 14(높이 60 이상이면 20).</summary>
    public static Button Btn(Transform parent, string name, float x, float y, float w, float h, string label, Style style, string glyph = null, float? fontSize = null)
    {
        var bg = Box(parent, name, x, y, w, h, Fill(style), h <= 60 ? h / 2f : 24f);
        var b = bg.gameObject.AddComponent<Button>();
        b.targetGraphic = bg;
        var cb = b.colors; cb.normalColor = Color.white; cb.highlightedColor = new Color(0.92f, 0.92f, 0.92f); cb.pressedColor = new Color(0.8f, 0.8f, 0.8f);
        cb.selectedColor = Color.white; cb.disabledColor = new Color(1, 1, 1, 0.35f); cb.fadeDuration = 0.08f; b.colors = cb;
        float fs = fontSize ?? (h >= 60 ? 20f : 14f);
        string s = glyph != null ? glyph + "  " + label : label;
        Text(bg.transform, "글자", 0, 0, w, h, s, fs, Ink4(style), true, TextAlignmentOptions.Center);
        return b;
    }

    /// <summary>토글(Toggle) — 켜지면 Meta 파랑 · 꺼지면 보조. 켜짐 색은 ToggleTint가 칠한다.</summary>
    public static Toggle Tog(Transform parent, string name, float x, float y, float w, float h, string label, float radius, string glyph = null)
    {
        var bg = Box(parent, name, x, y, w, h, Secondary, radius);
        var t = bg.gameObject.AddComponent<Toggle>();
        t.targetGraphic = bg; t.transition = Selectable.Transition.None;
        var tint = bg.gameObject.AddComponent<ToggleTint>();
        var so = new SerializedObject(tint);
        so.FindProperty("target").objectReferenceValue = bg;
        so.FindProperty("onColor").colorValue = White;
        so.FindProperty("offColor").colorValue = new Color(1, 1, 1, 0f);   // 분할 선택: 안 고른 칸은 트랙이 비친다
        TextMeshProUGUI lab;
        if (glyph != null)
        {
            Text(bg.transform, "아이콘", 0, 12, w, 28, glyph, 24, Text1, false, TextAlignmentOptions.Center);
            lab = Text(bg.transform, "글자", 0, h - 14 - 24, w, 24, label, 14, Text1, true, TextAlignmentOptions.Center);
        }
        else lab = Text(bg.transform, "글자", 0, 0, w, h, label, 14, Text1, false, TextAlignmentOptions.Center);
        so.FindProperty("label").objectReferenceValue = lab;
        so.FindProperty("labelOn").colorValue = Ink;
        so.FindProperty("labelOff").colorValue = Text1;
        so.ApplyModifiedPropertiesWithoutUndo();
        return t;
    }

    public static Transform Find(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var r = Find(c, name); if (r != null) return r; }
        return null;
    }
}
