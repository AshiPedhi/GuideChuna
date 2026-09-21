using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Meta UI Set 버튼 하나에 붙어 <b>누름</b>을 처리한다(2026-09-21 신설).
///
/// ★UI Set의 버튼 프리팹에는 Unity <c>Button</c>이 없다 — 레이아웃과 생김새만 들어 있고
///   상호작용은 붙여 쓰는 구조다(09-21 프리팹 실측). 그래서 포인터 이벤트를 직접 받는다.
/// ★손가락 Poke는 ISDK가 <c>PointableCanvas</c>를 거쳐 uGUI 포인터 이벤트로 바꿔 준다.
///   그래서 여기서는 손가락 좌표를 몰라도 된다 — 구현 1(<see cref="RomRecordWristMenu"/>)과 다른 점이다.
///
/// 세 가지 누름을 다룬다.
///   · 보통      — 누르는 순간 한 번
///   · 반복      — 누르고 있으면 <see cref="holdDelay"/> 뒤부터 <see cref="repeatInterval"/>마다
///   · 길게 누름 — <see cref="holdSeconds"/> 동안 누르고 있어야 한 번. 그동안 막대가 차오른다.
/// </summary>
public class RomMenuButtonUI : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    public string id;
    public bool repeat;
    public float holdSeconds;
    public RomRecordMenuUI owner;

    public float holdDelay = 0.5f;
    public float repeatInterval = 0.12f;

    private Image background;     // 눌림을 색으로 알린다
    private Image fill;           // 길게 누르기 진행 막대
    private Color baseColor;
    private bool down;
    private float downAt;
    private float nextRepeat;
    private bool holdFired;

    public void Setup(Image bg, Image progress)
    {
        background = bg;
        fill = progress;
        if (background != null) baseColor = background.color;
        if (fill != null) fill.gameObject.SetActive(false);
    }

    public void OnPointerDown(PointerEventData e)
    {
        down = true;
        downAt = Time.unscaledTime;
        nextRepeat = downAt + holdDelay;
        holdFired = false;
        Tint(0.35f);
        // ★길게 누르는 버튼은 여기서 쏘지 않는다 — 그게 오눌림을 막는 전부다.
        if (holdSeconds <= 0f && owner != null) owner.OnPressed(id, false);
    }

    public void OnPointerUp(PointerEventData e) => Release();

    public void OnPointerExit(PointerEventData e) => Release();

    private void Release()
    {
        if (!down) return;
        down = false;
        Tint(0f);
        if (fill != null && fill.gameObject.activeSelf) fill.gameObject.SetActive(false);
    }

    private void Update()
    {
        if (!down) return;
        float held = Time.unscaledTime - downAt;

        if (holdSeconds > 0f)
        {
            float p = Mathf.Clamp01(held / holdSeconds);
            if (fill != null)
            {
                if (!fill.gameObject.activeSelf) fill.gameObject.SetActive(true);
                fill.fillAmount = p;
            }
            if (!holdFired && p >= 1f)
            {
                holdFired = true;
                Tint(0.8f);
                if (owner != null) owner.OnPressed(id, false);
            }
            return;
        }

        if (repeat && Time.unscaledTime >= nextRepeat)
        {
            nextRepeat = Time.unscaledTime + repeatInterval;
            if (owner != null) owner.OnPressed(id, true);
        }
    }

    private void Tint(float towardWhite)
    {
        if (background == null) return;
        background.color = towardWhite <= 0f ? baseColor : Color.Lerp(baseColor, Color.white, towardWhite);
    }
}
