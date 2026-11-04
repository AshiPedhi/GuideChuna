using TMPro;
using UnityEngine;

/// <summary>
/// 로비 세부정보 말풍선 (2026-10-28 사용자 «정보줄이 너무 길면 …으로 줄이고, 선택 올리면 윈도에서 세부정보 박스 보여주는 것처럼»).
///
/// 카드(<see cref="LobbyCardTooltip"/>)가 손을 올리면 Request, 떼면 Cancel을 부른다. 윈도처럼 잠깐(delay) 머문 뒤에 뜬다.
/// 자리 = 그 글자 바로 아래(판 아래로 넘치면 위). 판 오른쪽 밖으로 안 나가게 민다.
/// ★레이를 막지 않는다(상자 · 글자 raycastTarget 꺼짐) — 말풍선 아래 카드도 그대로 눌린다.
/// ★매 프레임 할당 없음 — 모서리 배열은 한 번 만든다.
/// </summary>
public class LobbyTooltip : MonoBehaviour
{
    [SerializeField] private RectTransform box;
    [SerializeField] private TMP_Text label;
    [SerializeField] private float delay = 0.4f;
    [SerializeField] private float maxWidth = 360f;
    [SerializeField] private float padX = 12f;
    [SerializeField] private float padY = 8f;
    [SerializeField] private float gap = 6f;

    private readonly Vector3[] corners = new Vector3[4];
    private RectTransform area;
    private TMP_Text pending, shownFor;
    private float showAt;

    private void Awake()
    {
        area = (RectTransform)transform;
        if (box != null) box.gameObject.SetActive(false);
    }

    public void Request(TMP_Text source)
    {
        pending = source;
        showAt = Time.unscaledTime + delay;
    }

    public void Cancel(TMP_Text source)
    {
        if (pending == source) pending = null;
        if (shownFor == source) { shownFor = null; if (box != null) box.gameObject.SetActive(false); }
    }

    private void Update()
    {
        if (pending == null || Time.unscaledTime < showAt) return;
        var src = pending;
        pending = null;
        Show(src);
    }

    private void Show(TMP_Text src)
    {
        if (box == null || label == null || src == null || !src.isActiveAndEnabled) return;
        label.text = src.text;
        float inner = maxWidth - 2f * padX;
        Vector2 one = label.GetPreferredValues(src.text, Mathf.Infinity, Mathf.Infinity);
        float w = Mathf.Min(one.x, inner);
        float h = one.x > inner ? label.GetPreferredValues(src.text, inner, Mathf.Infinity).y : one.y;
        box.sizeDelta = new Vector2(w + 2f * padX, h + 2f * padY);

        // 원본 글자 칸의 아래-왼쪽 · 위-왼쪽(이 판 좌표)
        src.rectTransform.GetWorldCorners(corners);
        Vector3 bl = area.InverseTransformPoint(corners[0]);
        Vector3 tl = area.InverseTransformPoint(corners[1]);
        Rect r = area.rect;
        float x = Mathf.Min(bl.x, r.xMax - box.sizeDelta.x - 8f);
        float y = bl.y - gap;                                         // 상자 위변(pivot 0,1)
        if (y - box.sizeDelta.y < r.yMin + 8f) y = tl.y + gap + box.sizeDelta.y;   // 아래로 넘치면 글자 위에
        box.localPosition = new Vector3(x, y, 0f);
        box.gameObject.SetActive(true);
        shownFor = src;
    }
}
