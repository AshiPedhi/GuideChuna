using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 진단 시작 전 환자 표준자세 체크리스트. 약관 동의처럼 <b>세 줄을 각각 체크</b>해야 넘어간다.
///
/// ★2026-08-27 회의 결정 — "하나하나 점검하도록. 자기가 클릭할 수 있게 체크포인트처럼."
///
/// 진행 패널(Section) 안에 붙는다. 나레이션이 끝나면 지시문을 감추고 체크리스트만 남긴다.
///
/// ★<b>'다음' 버튼을 복제하지 않는다</b>(2026-08-28). 처음에 그렇게 만들었더니
///   화살표 아이콘이 그대로 딸려와 체크박스가 아니라 버튼 세 개가 됐고,
///   두 글자('다음')용으로 잡힌 라벨 칸에 긴 문장을 넣어 패널 밖으로 글자가 새어 나갔다.
///   네모 상자 + 체크 표시 + 글씨를 <b>직접</b> 만든다.
///
/// ★진행을 실제로 막는 건 '다음' 토글 잠금이다. 이 단계는 stepNo 0(가이드 스텝)이라
///   조건 매니저가 나레이션 후 토글 입력을 기다린다 — 다 체크하기 전엔 그 토글을 잠근다.
/// </summary>
public class PostureChecklistUI : MonoBehaviour
{
    [Header("=== 항목 ===")]
    [SerializeField]
    private string[] items =
    {
        "허리를 펴고 앉게 한다",
        "가슴을 편다",
        "검사하는 동안 어깨가 돌아가지 않게 한다",
    };

    [Header("=== 붙일 곳 (비우면 자동 탐색) ===")]
    [SerializeField] private ScenarioGuideUIController guideUI;

    [Header("=== 배치 (px, 패널 로컬) ===")]
    [Tooltip("첫 줄의 세로 위치. 패널 중앙이 0이고 위가 +다.")]
    [SerializeField] private float firstRowY = 46f;
    [SerializeField] private float rowSpacing = 48f;
    [Tooltip("왼쪽 끝에서 체크 상자까지.")]
    [SerializeField] private float rowIndent = 44f;
    [SerializeField] private float boxSize = 30f;
    [Tooltip("체크 상자에서 글씨까지의 간격.")]
    [SerializeField] private float labelGap = 18f;
    [SerializeField] private float labelWidth = 760f;
    [SerializeField] private float labelHeight = 40f;
    [Tooltip("0이면 지시문과 같은 크기를 쓴다.")]
    [SerializeField] private float labelFontSize = 0f;

    [Header("=== 색 ===")]
    [SerializeField] private Color boxColor = new Color(0.16f, 0.17f, 0.21f, 1f);
    [SerializeField] private Color boxBorderColor = new Color(0.55f, 0.60f, 0.70f, 1f);
    [SerializeField] private Color checkColor = new Color(0.30f, 0.82f, 0.45f, 1f);
    [SerializeField] private Color doneTextColor = new Color(0.55f, 0.90f, 0.65f, 1f);

    [Header("=== 동작 ===")]
    [Tooltip("한 번 체크하면 다시 못 끈다. 약관 동의와 같다.")]
    [SerializeField] private bool checkOnce = true;
    [SerializeField] private bool showDebugLogs = true;

    private bool visible;
    private bool built;
    private RectTransform root;
    private readonly List<Toggle> toggles = new List<Toggle>(4);
    private readonly List<TextMeshProUGUI> labels = new List<TextMeshProUGUI>(4);
    private readonly List<Image> checkMarks = new List<Image>(4);
    private bool[] checkedFlags;
    private bool warnedNoPanel;

    private TextMeshProUGUI descLabel;
    private Color descColorBackup;
    private bool descHidden;
    private Toggle nextToggle;
    private bool toggleLocked;
    private ScenarioConditionManager conditionManager;

    /// <summary>세 줄이 전부 체크됐는가.</summary>
    public bool AllChecked
    {
        get
        {
            if (checkedFlags == null || checkedFlags.Length == 0) return false;
            for (int i = 0; i < checkedFlags.Length; i++) if (!checkedFlags[i]) return false;
            return true;
        }
    }

    public bool IsVisible => visible;

    private void Awake()
    {
        if (guideUI == null) guideUI = FindFirstObjectByType<ScenarioGuideUIController>(FindObjectsInactive.Include);
        if (conditionManager == null) conditionManager = FindFirstObjectByType<ScenarioConditionManager>(FindObjectsInactive.Include);
        checkedFlags = new bool[items != null ? items.Length : 0];
    }

    private void OnDestroy()
    {
        SetDescriptionHidden(false);
        ApplyToggleLock(false);
        if (root != null) Destroy(root.gameObject);
    }

    private void OnDisable()
    {
        SetDescriptionHidden(false);
        ApplyToggleLock(false);
    }

    public void ResetChecks()
    {
        if (checkedFlags == null) return;
        for (int i = 0; i < checkedFlags.Length; i++) checkedFlags[i] = false;
        for (int i = 0; i < toggles.Count; i++)
            if (toggles[i] != null) toggles[i].SetIsOnWithoutNotify(false);
        RefreshRowVisuals();
    }

    /// <summary>브리지가 단계에 맞춰 켜고 끈다.</summary>
    public void SetVisible(bool on)
    {
        if (visible == on) return;
        visible = on;

        if (on && !built) Build();

        if (!on)
        {
            if (root != null) root.gameObject.SetActive(false);
            SetDescriptionHidden(false);
            ApplyToggleLock(false);
            return;
        }

        if (built && showDebugLogs)
            ChunaLogger.Log("<color=cyan>[표준자세] 체크리스트 표시 — 세 항목을 모두 체크해야 [다음]이 눌린다.</color>");
    }

    /// <summary>
    /// 나레이션을 읽는 동안은 지시문, 끝나면 체크리스트.
    /// 체크 상태가 바뀔 때마다 '다음' 잠금도 갱신한다.
    /// </summary>
    private void Update()
    {
        if (!visible || !built) return;

        bool reading = conditionManager != null && conditionManager.IsPlayingNarration;
        SetDescriptionHidden(!reading);
        if (root != null && root.gameObject.activeSelf == reading)
            root.gameObject.SetActive(!reading);

        ApplyToggleLock(true);
    }

    /// <summary>★컴포넌트를 끄지 않고 색만 투명하게 한다 — 패널이 매 프레임 text를 다시 쓴다.</summary>
    private void SetDescriptionHidden(bool hide)
    {
        if (descLabel == null || descHidden == hide) return;
        descHidden = hide;
        descLabel.color = hide ? new Color(descColorBackup.r, descColorBackup.g, descColorBackup.b, 0f)
                               : descColorBackup;
    }

    /// <summary>다 체크하기 전에는 '다음'을 못 누르게 잠근다. 이게 실제 게이트다.</summary>
    private void ApplyToggleLock(bool active)
    {
        if (nextToggle == null) return;

        bool shouldLock = active && !AllChecked;
        if (toggleLocked == shouldLock) return;
        toggleLocked = shouldLock;
        nextToggle.interactable = !shouldLock;
    }

    // ================= 생성 =================

    private void Build()
    {
        TextMeshProUGUI desc = guideUI != null ? guideUI.DescriptionLabel : null;
        if (desc == null)
        {
            if (!warnedNoPanel)
            {
                warnedNoPanel = true;
                ChunaLogger.LogWarning("[표준자세] 진행 패널을 찾지 못했습니다 — " +
                                       "ScenarioGuideUIController의 descriptionText가 배선돼 있어야 합니다.");
            }
            return;
        }

        descLabel = desc;
        descColorBackup = desc.color;
        nextToggle = guideUI.NextToggle;

        var holder = new GameObject("표준자세_체크리스트", typeof(RectTransform));
        root = holder.GetComponent<RectTransform>();
        root.SetParent(desc.rectTransform.parent, false);
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.pivot = new Vector2(0.5f, 0.5f);
        root.anchoredPosition = Vector2.zero;
        root.sizeDelta = Vector2.zero;

        int n = items != null ? items.Length : 0;
        for (int i = 0; i < n; i++) BuildRow(i, desc);

        built = true;
        ChunaLogger.Log($"<color=cyan>[표준자세] 진행 패널에 체크박스 {n}줄을 만들었다.</color>");
        RefreshRowVisuals();
    }

    private void BuildRow(int i, TextMeshProUGUI desc)
    {
        float y = firstRowY - rowSpacing * i;

        // --- 체크 상자 (테두리) ---
        var boxGo = new GameObject($"체크상자{i}", typeof(RectTransform));
        RectTransform boxRT = boxGo.GetComponent<RectTransform>();
        boxRT.SetParent(root, false);
        Anchor(boxRT, new Vector2(rowIndent, y), new Vector2(boxSize, boxSize));

        Image border = boxGo.AddComponent<Image>();
        border.color = boxBorderColor;
        border.raycastTarget = true;   // ★여기를 눌러 체크한다

        // 안쪽 면 — 테두리가 보이도록 살짝 작게
        var innerGo = new GameObject("면", typeof(RectTransform));
        RectTransform innerRT = innerGo.GetComponent<RectTransform>();
        innerRT.SetParent(boxRT, false);
        innerRT.anchorMin = Vector2.zero;
        innerRT.anchorMax = Vector2.one;
        innerRT.offsetMin = new Vector2(2f, 2f);
        innerRT.offsetMax = new Vector2(-2f, -2f);
        Image inner = innerGo.AddComponent<Image>();
        inner.color = boxColor;
        inner.raycastTarget = false;

        // 체크 표시 — 스프라이트 없이 네모를 채운다(체크 아이콘 에셋에 기대지 않는다)
        var markGo = new GameObject("체크", typeof(RectTransform));
        RectTransform markRT = markGo.GetComponent<RectTransform>();
        markRT.SetParent(boxRT, false);
        markRT.anchorMin = Vector2.zero;
        markRT.anchorMax = Vector2.one;
        markRT.offsetMin = new Vector2(6f, 6f);
        markRT.offsetMax = new Vector2(-6f, -6f);
        Image mark = markGo.AddComponent<Image>();
        mark.color = checkColor;
        mark.raycastTarget = false;
        mark.enabled = false;

        // --- 글씨 ---
        var labelGo = new GameObject($"항목{i}", typeof(RectTransform));
        RectTransform labelRT = labelGo.GetComponent<RectTransform>();
        labelRT.SetParent(root, false);
        Anchor(labelRT, new Vector2(rowIndent + boxSize + labelGap, y), new Vector2(labelWidth, labelHeight));

        var label = labelGo.AddComponent<TextMeshProUGUI>();
        label.font = desc.font;
        label.fontSize = labelFontSize > 0f ? labelFontSize : desc.fontSize;
        label.text = items[i];
        label.color = desc.color;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.enableAutoSizing = false;
        label.raycastTarget = false;

        // --- 토글 ---
        Toggle t = boxGo.AddComponent<Toggle>();
        t.transition = Selectable.Transition.None;
        t.targetGraphic = border;
        t.graphic = mark;
        t.isOn = false;

        int index = i;   // ★클로저 캡처 — 루프 변수를 그대로 쓰면 전부 마지막 값이 된다
        t.onValueChanged.AddListener(v => OnToggled(index, v));

        toggles.Add(t);
        labels.Add(label);
        checkMarks.Add(mark);
    }

    /// <summary>왼쪽·세로중앙 기준으로 못박는다. 스트레치 앵커를 물려받으면 칸이 늘어난다.</summary>
    private static void Anchor(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    private void OnToggled(int index, bool value)
    {
        if (checkedFlags == null || index >= checkedFlags.Length) return;

        // 한 번 체크하면 못 끈다. 되돌리려 하면 다시 켜 준다.
        if (checkOnce && checkedFlags[index] && !value)
        {
            if (index < toggles.Count && toggles[index] != null) toggles[index].SetIsOnWithoutNotify(true);
            return;
        }

        checkedFlags[index] = value;
        RefreshRowVisuals();

        if (showDebugLogs)
        {
            ChunaLogger.Log($"<color=cyan>[표준자세] {index + 1}번 {(value ? "체크" : "해제")} — {items[index]}" +
                            (AllChecked ? " · <b>전부 확인됨 → [다음] 열림</b>" : "") + "</color>");
        }
    }

    private void RefreshRowVisuals()
    {
        if (checkedFlags == null) return;
        for (int i = 0; i < checkedFlags.Length; i++)
        {
            bool on = checkedFlags[i];
            if (i < checkMarks.Count && checkMarks[i] != null) checkMarks[i].enabled = on;
            if (i < labels.Count && labels[i] != null)
                labels[i].color = on ? doneTextColor : descColorBackup;
        }
    }
}
