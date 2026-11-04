using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로그인 조 · 이름 목록 공통 규칙 (2026-10-27 사용자 요청 2 · 시안 V0.7 그림 2-3).
///
/// · 정렬: 조 = 번호순(글자순이면 «10조»가 «1조» 바로 뒤에 붙는다) · 이름 = 가나다순. 종전엔 서버가 준 순서 그대로였다.
/// · ★긴 이름은 <b>말줄임 없이</b> 다 보인다(사용자 지시) — 두 줄까지 감고, 그래도 넘치면 글자를 줄인다.
/// · 판이 넘치면 판 안에서 세로 스크롤한다(Quest 방식) — 칸 크기는 수와 상관없이 고정. 격자(GridLayoutGroup)가 있으면 격자 높이로 잰다.
///   격자가 없으면 종전대로 세로 한 줄(VerticalLayoutGroup)이다.
/// </summary>
public static class LoginListLayout
{
    private static readonly CompareInfo Ko = CultureInfo.GetCultureInfo("ko-KR").CompareInfo;

    /// <summary>조 이름 비교 — 처음 나오는 숫자로 먼저 비교하고, 같거나 숫자가 없으면 가나다순.</summary>
    public static int CompareGrade(string a, string b)
    {
        int na = FirstNumber(a), nb = FirstNumber(b);
        if (na >= 0 && nb >= 0 && na != nb) return na.CompareTo(nb);
        if (na >= 0 && nb < 0) return -1;
        if (na < 0 && nb >= 0) return 1;
        return Ko.Compare(a ?? "", b ?? "", CompareOptions.None);
    }

    public static int CompareName(string a, string b) => Ko.Compare(a ?? "", b ?? "", CompareOptions.None);

    private static int FirstNumber(string s)
    {
        if (string.IsNullOrEmpty(s)) return -1;
        int i = 0;
        while (i < s.Length && !char.IsDigit(s[i])) i++;
        if (i == s.Length) return -1;
        int v = 0;
        while (i < s.Length && char.IsDigit(s[i]) && v < 100000) { v = v * 10 + (s[i] - '0'); i++; }
        return v;
    }

    /// <summary>버튼 글자 — 감기 허용 · 말줄임 금지 · 넘치면 자동 축소(최소 60%).</summary>
    public static void ApplyLabel(TextMeshProUGUI t)
    {
        if (t == null) return;
        OsDisplayScale.ApplyFont(t);   // 10-28 OS 텍스트 크기(실행 중에 만드는 조 · 이름 칸은 씬 로드 때 못 잡는다)
        t.textWrappingMode = TextWrappingModes.Normal;
        t.overflowMode = TextOverflowModes.Overflow;   // ★Ellipsis · Truncate 금지
        float size = t.fontSize;
        t.enableAutoSizing = true;
        t.fontSizeMax = size;
        t.fontSizeMin = Mathf.Max(8f, size * 0.6f);
    }

    /// <summary>목록 칸(Content) 높이를 맞춘다. 격자면 줄 수로, 아니면 종전 세로 한 줄 규칙으로.</summary>
    public static void AdjustContent(Transform content, int count, string tag)
    {
        if (content == null) return;
        var rt = content.GetComponent<RectTransform>();
        var grid = content.GetComponent<GridLayoutGroup>();
        if (grid != null)
        {
            float w = rt != null ? rt.rect.width - grid.padding.horizontal : 0f;
            int cols = grid.constraint == GridLayoutGroup.Constraint.FixedColumnCount ? grid.constraintCount
                     : Mathf.Max(1, Mathf.FloorToInt((w + grid.spacing.x) / (grid.cellSize.x + grid.spacing.x)));
            int rows = Mathf.CeilToInt(count / (float)cols);
            float h = grid.padding.vertical + rows * grid.cellSize.y + Mathf.Max(0, rows - 1) * grid.spacing.y;
            if (rt != null) rt.sizeDelta = new Vector2(rt.sizeDelta.x, h);
            ChunaLogger.Log($"[{tag}] 격자 {cols}열 × {rows}줄 · 높이 {h}");
            return;
        }

        if (rt != null)
        {
            float totalHeight = (60 + 10) * count;
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, totalHeight);
            ChunaLogger.Log($"[{tag}] Content 크기 조정: {totalHeight}");
        }
        var layout = content.GetComponent<VerticalLayoutGroup>();
        if (layout == null)
        {
            layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.spacing = 10;
            layout.padding = new RectOffset(10, 10, 10, 10);
            ChunaLogger.Log($"[{tag}] VerticalLayoutGroup 추가됨");
        }
    }
}
