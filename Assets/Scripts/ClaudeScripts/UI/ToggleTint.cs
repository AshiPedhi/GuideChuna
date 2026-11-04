using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 켜진 토글을 색으로 표시한다(시안 A안 · 2026-10-27). UISet 토글의 기본 켜짐 표시 위에 바탕색을 덮는다.
/// 상태가 바뀐 프레임에만 색을 바꾼다(할당 없음).
/// </summary>
[RequireComponent(typeof(Toggle))]
public class ToggleTint : MonoBehaviour
{
    [SerializeField] private Graphic target;
    [SerializeField] private Color onColor = new Color(0.18f, 0.48f, 0.96f, 1f);
    [SerializeField] private Color offColor = new Color(0.23f, 0.25f, 0.28f, 1f);
    [Tooltip("켜짐에 따라 글자색도 바꾼다(흰 알약 위 어두운 글자) · 비워도 된다")]
    [SerializeField] private Graphic label;
    [SerializeField] private Color labelOn = new Color(0.153f, 0.153f, 0.153f, 1f);
    [SerializeField] private Color labelOff = new Color(0.94f, 0.95f, 0.96f, 1f);

    private Toggle toggle;
    private int last = -1;

    private void Awake() => toggle = GetComponent<Toggle>();

    private void LateUpdate()
    {
        if (toggle == null || target == null) return;
        int now = toggle.isOn ? 1 : 0;
        if (now == last) return;
        last = now;
        target.color = toggle.isOn ? onColor : offColor;
        if (label != null) label.color = toggle.isOn ? labelOn : labelOff;
    }
}
