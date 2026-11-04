using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 카드에 손(레이)을 올리면 말줄임(…)된 정보줄 전체를 <see cref="LobbyTooltip"/>으로 보인다 (2026-10-28 사용자).
/// ★잘린 글자일 때만 띄운다 — 다 보이는 정보줄에 같은 말을 또 띄우면 거슬린다.
/// 레이 호버는 PointableCanvas가 EventSystem 포인터 이벤트로 넘겨 준다(UISet 버튼 Highlighted와 같은 경로).
/// </summary>
public class LobbyCardTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private TMP_Text detail;
    [SerializeField] private LobbyTooltip tooltip;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (detail != null && tooltip != null && detail.isTextTruncated) tooltip.Request(detail);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (tooltip != null) tooltip.Cancel(detail);
    }

    private void OnDisable()
    {
        if (tooltip != null) tooltip.Cancel(detail);
    }
}
