using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 카드 호버 · 누름 하이라이트 (2026-10-28 사용자 «원래 호버링이나 셀렉될 때 하이라이트가 있었는데 지금은 없어서 구분이 안 간다»).
///
/// 원래는 UISet 애니메이터가 카드 바탕(Content/Background) 색을 바꿔 보였는데, 그 바탕에 사진이 들어 있어 글자 밑에 깔리는 문제로
/// 바탕을 안 그리게 했다(LobbyV15Tool ⑦). 그래서 카드 뒤에 따로 둔 둥근 판(highlight)을 호버 · 누름에 맞춰 비친다(Quest 스토어 카드 꼴).
/// ★CrossFadeAlpha(캔버스 렌더러 알파)만 쓴다 — 매 프레임 할당 없음 · 애니메이터와 안 부딪친다.
/// </summary>
public class LobbyCardHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [SerializeField] private Graphic highlight;
    [SerializeField] private float hoverAlpha = 0.10f;
    [SerializeField] private float pressAlpha = 0.18f;
    [SerializeField] private float fade = 0.08f;

    private bool over;

    private void Awake()
    {
        if (highlight == null) return;
        highlight.canvasRenderer.SetAlpha(0f);
        highlight.enabled = true;   // 씬엔 꺼 둔다(에디터 · 캡처에서 흰 판이 안 보이게) — 실행 때 알파 0으로 켠다
    }

    private void OnDisable()
    {
        over = false;
        if (highlight != null) highlight.canvasRenderer.SetAlpha(0f);
    }

    private void To(float a) { if (highlight != null) highlight.CrossFadeAlpha(a, fade, true); }

    public void OnPointerEnter(PointerEventData e) { over = true; To(hoverAlpha); }
    public void OnPointerExit(PointerEventData e) { over = false; To(0f); }
    public void OnPointerDown(PointerEventData e) { To(pressAlpha); }
    public void OnPointerUp(PointerEventData e) { To(over ? hoverAlpha : 0f); }
}
