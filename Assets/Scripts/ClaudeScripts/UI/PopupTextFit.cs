using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 팝업 판을 글자 높이에 맞춰 세로로 늘린다 (2026-10-29 사용자 «모드 팝업도 OS 텍스트 · 디스플레이 크기에 따라 모양 안 벗어나게»).
///
/// OS 화면 크기는 판 전체 배율이라 모양이 그대로다(OsDisplayScale). 텍스트 크기는 글자만 키워 칸을 넘친다
/// (10-29 실측 ×1.5: 설명 두 줄 칸 40에 세 줄 → 세로 25 넘침 · 제목 13 · 행 제목 6).
/// → 판 바로 아래 글자마다 «필요 높이 − 칸 높이»만큼 그 글자를 늘리고,
///   글자 아래에서 시작하는 것은 그만큼 내리고, 글자를 감싸는 것(행 카드 · 판)은 그만큼 늘린다. 가로 폭은 그대로다.
/// ★판 자식은 HzUIKit 좌표(왼쪽 위 기준 · 고정 앵커)를 전제한다. 처음 맞출 때 원래 자리를 기억하고 매번 거기서 다시 계산한다(여러 번 열어도 누적 안 됨).
/// ★열릴 때(OnEnable) 다음 LateUpdate에 한 번 — 팝업이 제목 · 설명 글자를 채운 뒤라야 줄 수가 맞는다.
/// </summary>
public class PopupTextFit : MonoBehaviour
{
    [Tooltip("내용을 놓는 판(UIBackplate) — 자식 자리를 옮기고 이 판의 높이를 늘린다.")]
    [SerializeField] private RectTransform back;
    [Tooltip("판을 담은 캔버스(CanvasRoot) — 레이 판정 범위가 캔버스 크기를 따른다. 판과 같이 늘린다.")]
    [SerializeField] private RectTransform canvasRoot;

    struct Base { public RectTransform r; public Vector2 pos, size; }
    // ★NonSerialized — 에디터 도메인 리로드가 private bool을 살려 «기억함»인데 목록은 빈 상태가 됐다(10-29 실측: 리로드 뒤 Fit이 아무것도 안 함)
    [System.NonSerialized] readonly List<Base> bases = new List<Base>();
    [System.NonSerialized] Vector2 backSize, canvasSize;
    [System.NonSerialized] bool captured, pending;

    void OnEnable() { pending = true; }

    void LateUpdate()
    {
        if (!pending) return;
        pending = false;
        Fit();
    }

    /// <summary>원래 자리로 되돌린 뒤 글자 높이에 맞춰 다시 놓는다. 에디터 넘침 검사도 이 함수를 탄다(미리보기와 실제가 같은 계산).</summary>
    public float Fit()
    {
        if (back == null) return 0f;
        Capture();
        ResetLayout();

        // 넘치는 글자(판 바로 아래 · 줄바꿈하는 것만 높이가 늘 수 있다)
        var grow = new List<(float top, float bottom, float xMin, float xMax, float d, RectTransform r)>();
        foreach (var b in bases)
        {
            var t = b.r.GetComponent<TMP_Text>();
            if (t == null || !t.gameObject.activeSelf || string.IsNullOrEmpty(t.text)) continue;
            float need = t.GetPreferredValues(t.text, b.size.x, 0f).y;
            // ★OS가 키운 몫만 늘린다 — 기본 크기에서도 칸을 2~3 넘는 글자가 있어(제목 24 · 칸 30 등) 그대로 재면 OS 1.0에서도 판이 +16 늘었다(10-29 실측)
            float baseNeed = need;
            if (!Mathf.Approximately(OsDisplayScale.Font, 1f))
            {
                float fs = t.fontSize;
                t.fontSize = fs / OsDisplayScale.Font;
                baseNeed = t.GetPreferredValues(t.text, b.size.x, 0f).y;
                t.fontSize = fs;
            }
            float d = Mathf.Ceil(need - Mathf.Max(b.size.y, baseNeed));
            if (d > 0.5f) grow.Add((-b.pos.y, -b.pos.y + b.size.y, b.pos.x, b.pos.x + b.size.x, d, b.r));
        }

        float total = 0f;
        foreach (var g in grow)
        {
            foreach (var b in bases)
            {
                float top = -b.pos.y, bottom = top + b.size.y;
                if (b.r == g.r) { b.r.sizeDelta += new Vector2(0f, g.d); continue; }
                if (top >= g.bottom - 0.5f) b.r.anchoredPosition -= new Vector2(0f, g.d);   // 글자 아래 → 내린다
                else if (top <= g.top + 0.5f && bottom >= g.bottom - 0.5f && b.pos.x <= g.xMin + 0.5f && b.pos.x + b.size.x >= g.xMax - 0.5f)
                    b.r.sizeDelta += new Vector2(0f, g.d);                                       // 글자를 감싸는 칸(행 카드) → 늘린다
            }
            total += g.d;
        }
        if (total > 0f)
        {
            back.sizeDelta = backSize + new Vector2(0f, total);
            if (canvasRoot != null) canvasRoot.sizeDelta = canvasSize + new Vector2(0f, total);
            Debug.Log($"[PopupTextFit] {name} 글자 {grow.Count}곳 넘침 → 판 높이 +{total:F0} ({backSize.y:F0} → {back.sizeDelta.y:F0})");
        }
        return total;
    }

    /// <summary>처음 자리로 되돌린다(에디터 검사 뒤 원복용).</summary>
    public void ResetLayout()
    {
        if (!captured) return;
        foreach (var b in bases) { b.r.anchoredPosition = b.pos; b.r.sizeDelta = b.size; }
        back.sizeDelta = backSize;
        if (canvasRoot != null) canvasRoot.sizeDelta = canvasSize;
    }

    void Capture()
    {
        if (captured) return;
        captured = true;
        backSize = back.sizeDelta;
        if (canvasRoot != null) canvasSize = canvasRoot.sizeDelta;
        foreach (Transform c in back)
            if (c is RectTransform r && Mathf.Approximately(r.anchorMin.x, r.anchorMax.x) && Mathf.Approximately(r.anchorMin.y, r.anchorMax.y))   // 늘림(stretch) 앵커는 판을 따라가니 둔다
                bases.Add(new Base { r = r, pos = r.anchoredPosition, size = r.sizeDelta });
    }
}
