using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로비 우측 상단 사용자 이름 표시 (2026-10-27 사용자 요청 2 «사용자 상태표시 = 이름»).
/// 로그인 전에는 «로그인»을 보인다. 이름은 브레인(<see cref="LobbyAuthUI_Complete"/>)이 들고 있는 값을 읽기만 한다.
/// 매 프레임 문자열을 만들지 않는다 — 참조가 바뀐 프레임에만 글자를 바꾼다.
/// </summary>
public class LobbyUserNameLabel : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField] private LobbyAuthUI_Complete auth;
    [SerializeField] private string loggedOutText = "로그인";

    [Header("알약 폭 = 글자에 맞춤 (10-28 «여백 타원이 너무 크고 아이콘이 중앙에 너무 가깝다»)")]
    [Tooltip("이름 버튼 루트. 비우면 폭 맞춤을 안 한다(종전 190 고정)")]
    [SerializeField] private RectTransform pill;
    [SerializeField] private LayoutElement pillBackground;
    [Tooltip("Elements/Text — 글자 폭만큼 줄인다")]
    [SerializeField] private RectTransform textBox;
    [Tooltip("알약 폭 = 글자 폭 + 이 값(아이콘 24 · 사이 8 · 좌우 안쪽 여백 · Elements 들여쓰기 6×2)")]
    [SerializeField] private float fixedWidth = 60f;

    private string shown;

    private void Awake()
    {
        if (label == null) label = GetComponentInChildren<TMP_Text>(true);
        if (auth == null) auth = FindFirstObjectByType<LobbyAuthUI_Complete>();
    }

    private void Update()
    {
        if (auth == null || label == null) return;
        string name = auth.CurrentUsername;
        if (ReferenceEquals(name, shown) && label.text.Length > 0) return;
        shown = name;
        label.text = string.IsNullOrEmpty(name) ? loggedOutText : name;
        Fit();
    }

    /// <summary>알약 폭을 글자에 맞춘다(이름이 바뀐 프레임에만). 에디터 도구(LobbyV15Tool)도 같은 함수를 부른다.</summary>
    public void Fit()
    {
        if (pill == null || label == null) return;
        float tw = Mathf.Ceil(label.GetPreferredValues(label.text, Mathf.Infinity, Mathf.Infinity).x);
        if (textBox != null) textBox.sizeDelta = new Vector2(tw, textBox.sizeDelta.y);
        label.rectTransform.sizeDelta = new Vector2(tw, label.rectTransform.sizeDelta.y);
        float w = tw + fixedWidth;
        pill.sizeDelta = new Vector2(w, pill.sizeDelta.y);
        if (pillBackground != null) pillBackground.preferredWidth = w;
        if (pill.parent is RectTransform row) LayoutRebuilder.MarkLayoutForRebuild(row);
    }
}
