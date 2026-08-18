using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 로비 상단 카테고리 탭(전체/단순추나/복잡추나/ROM진단)이 "선택된 동안" 활성 색을 유지하게 한다.
///
/// ★왜 필요한가 (2026-08-18 실측)
///   탭은 Meta XR 샘플 프리팹 SecondaryButton_IconAndLabel을 그대로 쓴다. 그 프리팹의 Toggle은
///     - graphic(체크마크) 슬롯이 비어 있다 → isOn(켜짐) 상태를 표시하는 그래픽이 아예 없다.
///     - transition = Animation 이라, 눈에 보이는 하이라이트는 전부 Selectable의 *상호작용* 상태
///       (Normal / Highlighted / Pressed / Selected)다. 이건 "선택된 탭"이 아니라 "지금 포인터가
///       올라가 있는지 / EventSystem이 선택 중인지"를 뜻하므로, 포인터가 떠나거나 다른 UI를 누르면
///       곧바로 Normal로 돌아간다. 그래서 활성 색이 유지되지 않는다.
///   또한 컨트롤러(SecondaryButton_Dark)의 클립 5개가 전부 Content/Background 의 m_Color를
///   애니메이션한다. Animator는 그 값을 매 프레임 덮어쓰므로, 인스펙터나 코드로 배경 색을 넣어도
///   그대로 지워진다. → 배경 색을 우리가 쓰려면 Animator를 꺼서 소유권을 가져와야 한다.
///
/// [해결 방식]
///   transition을 None으로 바꾸고 Animator를 끈 뒤, isOn 값에 따라 배경 색을 직접 칠한다.
///   Animator가 하던 호버 표현은 잃지 않도록 normal/hover 색을 그대로 재현한다
///   (원본 클립 실측값: Normal #4B4B4B, Highlighted #5D5D5D, Pressed·Selected #6F6F6F).
///
/// [부착 위치] 탭 Toggle 오브젝트. 배선은 에디터 도구 `GuideChuna/로비 탭 활성색 적용`이 대신 해준다.
/// </summary>
[RequireComponent(typeof(Toggle))]
[DisallowMultipleComponent]
public class LobbyTabHighlight : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    /// <summary>Animator 클립이 색을 굽던 대상. 프리팹 기준 Toggle 하위 "Content/Background".</summary>
    public const string BackgroundPath = "Content/Background";

    [Header("대상")]
    [Tooltip("색을 칠할 배경 그래픽. 비우면 Content/Background 를 자동으로 찾는다.")]
    [SerializeField] private Graphic background;

    [Tooltip("같이 색을 바꿀 라벨(선택). 비우면 자식에서 첫 TMP_Text를 찾는다. tintLabel이 꺼져 있으면 쓰지 않는다.")]
    [SerializeField] private TMP_Text label;

    [Header("배경 색")]
    [Tooltip("선택(isOn=true)된 동안 계속 유지되는 색")]
    [SerializeField] private Color activeColor = new Color(0f, 0.392f, 0.878f, 1f);   // #0064E0 — 프리팹 Toggle이 SelectedColor로 갖고 있던 값
    [Tooltip("선택되지 않은 평소 색 (원본 Normal 클립 실측값 #4B4B4B)")]
    [SerializeField] private Color normalColor = new Color(0.294f, 0.294f, 0.294f, 1f);
    [Tooltip("선택되지 않은 탭에 포인터가 올라갔을 때 색 (원본 Highlighted 클립 실측값 #5D5D5D)")]
    [SerializeField] private Color hoverColor = new Color(0.365f, 0.365f, 0.365f, 1f);

    [Header("라벨 색 (선택)")]
    [Tooltip("켜면 라벨 색도 같이 바꾼다. 배경만 바꿔도 충분하면 꺼둘 것.")]
    [SerializeField] private bool tintLabel = false;
    [SerializeField] private Color activeLabelColor = Color.white;
    [SerializeField] private Color normalLabelColor = new Color(1f, 1f, 1f, 0.902f);

    [Header("Animator 인계")]
    [Tooltip("★필수. Animator가 Content/Background 색을 매 프레임 덮어쓰기 때문에, 끄지 않으면 여기서 칠한 색이 " +
             "그대로 지워진다. 해제하는 경우는 배경이 아닌 별도 그래픽을 background로 지정했을 때뿐이다.")]
    [SerializeField] private bool takeOverFromAnimator = true;

    private Toggle toggle;
    private Animator animator;
    private bool hovering;

    /// <summary>에디터 도구가 편집 중에도 같은 색을 미리 칠해볼 수 있도록 공개.</summary>
    public Graphic Background => background != null ? background : FindBackground();
    public Color ActiveColor => activeColor;
    public Color NormalColor => normalColor;

    private void Awake()
    {
        toggle = GetComponent<Toggle>();
        if (background == null) background = FindBackground();
        if (label == null) label = GetComponentInChildren<TMP_Text>(true);

        if (takeOverFromAnimator)
        {
            // 순서 주의: transition을 먼저 None으로 바꿔야 Selectable이 트리거를 쏘지 않는다.
            // Selectable.OnEnable의 DoStateTransition보다 Awake가 먼저라 안전하다.
            toggle.transition = Selectable.Transition.None;

            animator = GetComponent<Animator>();
            if (animator != null) animator.enabled = false;
        }

        toggle.onValueChanged.AddListener(OnToggleChanged);
    }

    private void OnDestroy()
    {
        if (toggle != null) toggle.onValueChanged.RemoveListener(OnToggleChanged);
    }

    private void OnEnable()
    {
        hovering = false;
        Apply();
    }

    private void OnToggleChanged(bool _) => Apply();

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovering = true;
        Apply();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovering = false;
        Apply();
    }

    /// <summary>현재 isOn/호버 상태에 맞는 색을 칠한다.</summary>
    public void Apply()
    {
        if (toggle == null) toggle = GetComponent<Toggle>();
        if (background == null) background = FindBackground();
        if (background == null) return;

        bool on = toggle != null && toggle.isOn;

        // 선택된 탭은 호버와 무관하게 활성 색을 유지한다 — 이게 이 컴포넌트의 목적이다.
        background.color = on ? activeColor : (hovering ? hoverColor : normalColor);

        if (tintLabel)
        {
            if (label == null) label = GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.color = on ? activeLabelColor : normalLabelColor;
        }
    }

    private Graphic FindBackground()
    {
        var t = transform.Find(BackgroundPath);
        return t != null ? t.GetComponent<Graphic>() : null;
    }
}
