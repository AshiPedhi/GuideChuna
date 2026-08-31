using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 진단 시작 전 환자 표준자세 체크리스트. <b>한 줄씩 순차로</b> 확인해 나간다.
///
/// ★2026-08-31 개편 (사용자 지시) — 체크박스 동시 표시에서 <b>순차 확인</b>으로 바꿨다.
///   줄이 나타나면 그 줄의 나레이션이 재생되고, [확인]을 누르면 다음 줄이 나타나며
///   그 줄의 나레이션이 재생된다. 마지막 줄을 확인하면 <b>[다음] 버튼 없이 그대로 넘어간다</b>.
///
///   왜 나레이션을 '나타날 때' 재생하는가 — 나레이션 내용이 <b>지시문</b>이기 때문이다.
///   확인한 뒤에 읽으면 순서가 거꾸로다. 확인은 "그 지시를 수행했다"는 뜻이다.
///
/// ★2026-08-27 회의 결정 "하나하나 점검하도록"은 그대로 지킨다 — 오히려 더 강해졌다.
///   동시에 세 개를 보여 주면 읽지 않고 세 번 누를 수 있지만, 순차면 그럴 수 없다.
///
/// ★<b>'다음' 토글은 이 단계 내내 잠가 둔다.</b> 진행 경로를 하나로 유지하기 위해서다 —
///   토글과 브리지가 둘 다 넘길 수 있으면 세 번째 확인 직후에 누른 클릭이
///   <b>다음 단계</b>에 먹혀 기준 단계를 건너뛴다(2026-08-31에 실제로 밟은 형태다).
///   전부 확인되면 <c>CervicalRomMeasurementBridge</c>가 <see cref="AllChecked"/>를 보고 넘긴다.
///
/// ★'다음' 버튼을 복제하지 않는다(2026-08-28). 화살표 아이콘이 딸려오고
///   두 글자용 라벨 칸에 긴 문장이 들어가 패널 밖으로 샌다. 상자·글씨를 직접 만든다.
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

    [Tooltip("줄마다 재생할 나레이션 클립 이름. 항목 수와 같게 맞춘다.\n" +
             "★비워 두면 그 줄은 무음으로 넘어간다(에러는 안 난다).")]
    [SerializeField]
    private string[] itemNarrations =
    {
        "실측자세1",
        "실측자세2",
        "실측자세3",
    };

    [Header("=== 붙일 곳 (비우면 자동 탐색) ===")]
    [SerializeField] private ScenarioGuideUIController guideUI;

    [Header("=== 배치 (px, 패널 로컬) ===")]
    [Tooltip("첫 줄의 세로 위치. 패널 중앙이 0이고 위가 +다.")]
    [SerializeField] private float firstRowY = 46f;
    [SerializeField] private float rowSpacing = 48f;
    [Tooltip("왼쪽 끝에서 확인 버튼까지.")]
    [SerializeField] private float rowIndent = 44f;
    [SerializeField] private Vector2 confirmSize = new Vector2(104f, 36f);
    [Tooltip("버튼에서 글씨까지의 간격.")]
    [SerializeField] private float labelGap = 18f;
    [SerializeField] private float labelWidth = 720f;
    [SerializeField] private float labelHeight = 40f;
    [Tooltip("0이면 지시문과 같은 크기를 쓴다.")]
    [SerializeField] private float labelFontSize = 0f;
    [Tooltip("확인 버튼 안의 글씨 크기.")]
    [SerializeField] private float confirmFontSize = 22f;

    [Header("=== 색 ===")]
    [SerializeField] private Color confirmFillColor = new Color(0.20f, 0.45f, 0.75f, 1f);
    [SerializeField] private Color confirmBorderColor = new Color(0.55f, 0.75f, 1f, 1f);
    [SerializeField] private Color confirmTextColor = Color.white;
    [SerializeField] private Color doneMarkColor = new Color(0.30f, 0.82f, 0.45f, 1f);
    [SerializeField] private Color doneTextColor = new Color(0.55f, 0.90f, 0.65f, 1f);

    [Header("=== 동작 ===")]
    [SerializeField] private bool showDebugLogs = true;

    // --- 상태 ---
    private bool visible;
    private bool built;
    private RectTransform root;
    private int currentIndex;          // 지금 확인해야 할 줄. items.Length면 전부 끝.
    private bool sequenceStarted;      // 진입 나레이션이 끝나 첫 줄을 띄웠는가
    private bool warnedNoPanel;

    private readonly List<GameObject> confirmButtons = new List<GameObject>(4);
    private readonly List<Image> doneMarks = new List<Image>(4);
    private readonly List<TextMeshProUGUI> labels = new List<TextMeshProUGUI>(4);
    private readonly List<GameObject> rowObjects = new List<GameObject>(8);

    private TextMeshProUGUI descLabel;
    private Color descColorBackup;
    private bool descHidden;
    private Toggle nextToggle;
    private bool toggleLocked;
    private ScenarioConditionManager conditionManager;

    private int ItemCount => items != null ? items.Length : 0;

    /// <summary>전부 확인됐는가. 브리지가 이걸 보고 단계를 넘긴다.</summary>
    public bool AllChecked => ItemCount > 0 && currentIndex >= ItemCount;

    public bool IsVisible => visible;

    private void Awake()
    {
        if (guideUI == null) guideUI = FindFirstObjectByType<ScenarioGuideUIController>(FindObjectsInactive.Include);
        if (conditionManager == null) conditionManager = FindFirstObjectByType<ScenarioConditionManager>(FindObjectsInactive.Include);
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
        currentIndex = 0;
        sequenceStarted = false;
        RefreshRows();
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
            sequenceStarted = false;
            return;
        }

        if (built && showDebugLogs)
            ChunaLogger.Log($"<color=cyan>[표준자세] 순차 확인 시작 — {ItemCount}줄. " +
                            "마지막 확인 시 [다음] 없이 그대로 진행한다.</color>");
    }

    /// <summary>
    /// 진입 나레이션(실측준비)을 읽는 동안은 지시문, 끝나면 체크리스트를 띄우고 첫 줄을 시작한다.
    /// </summary>
    private void Update()
    {
        if (!visible || !built) return;

        bool reading = conditionManager != null && conditionManager.IsPlayingNarration;
        SetDescriptionHidden(!reading);
        if (root != null && root.gameObject.activeSelf == reading)
            root.gameObject.SetActive(!reading);

        // ★진입 나레이션이 끝난 뒤에 첫 줄을 띄운다. 겹쳐 읽으면 둘 다 안 들린다.
        if (!reading && !sequenceStarted)
        {
            sequenceStarted = true;
            RefreshRows();
            PlayNarrationFor(0);
        }

        // ★이 단계 내내 잠가 둔다. 진행은 브리지가 AllChecked를 보고 한다.
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

    /// <summary>이 단계에서는 '다음'을 아예 못 누르게 한다. 진행 경로를 하나로 둔다.</summary>
    private void ApplyToggleLock(bool active)
    {
        if (nextToggle == null) return;
        if (toggleLocked == active) return;
        toggleLocked = active;
        nextToggle.interactable = !active;
    }

    private void PlayNarrationFor(int index)
    {
        if (itemNarrations == null || index < 0 || index >= itemNarrations.Length) return;
        string clip = itemNarrations[index];
        if (string.IsNullOrWhiteSpace(clip)) return;
        if (conditionManager == null) return;

        conditionManager.PlayNarration(clip.Trim());
        if (showDebugLogs)
            ChunaLogger.Log($"<color=cyan>[표준자세] {index + 1}번 줄 나레이션 '{clip}'</color>");
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

        for (int i = 0; i < ItemCount; i++) BuildRow(i, desc);

        built = true;
        ChunaLogger.Log($"<color=cyan>[표준자세] 순차 확인 {ItemCount}줄을 만들었다.</color>");
        RefreshRows();
    }

    private void BuildRow(int i, TextMeshProUGUI desc)
    {
        float y = firstRowY - rowSpacing * i;

        // --- 확인 버튼 ---
        var btnGo = new GameObject($"확인{i}", typeof(RectTransform));
        RectTransform btnRT = btnGo.GetComponent<RectTransform>();
        btnRT.SetParent(root, false);
        Anchor(btnRT, new Vector2(rowIndent, y), confirmSize);

        Image border = btnGo.AddComponent<Image>();
        border.color = confirmBorderColor;
        border.raycastTarget = true;

        var fillGo = new GameObject("면", typeof(RectTransform));
        RectTransform fillRT = fillGo.GetComponent<RectTransform>();
        fillRT.SetParent(btnRT, false);
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.offsetMin = new Vector2(2f, 2f);
        fillRT.offsetMax = new Vector2(-2f, -2f);
        Image fill = fillGo.AddComponent<Image>();
        fill.color = confirmFillColor;
        fill.raycastTarget = false;

        var btnTextGo = new GameObject("글씨", typeof(RectTransform));
        RectTransform btnTextRT = btnTextGo.GetComponent<RectTransform>();
        btnTextRT.SetParent(btnRT, false);
        btnTextRT.anchorMin = Vector2.zero;
        btnTextRT.anchorMax = Vector2.one;
        btnTextRT.offsetMin = Vector2.zero;
        btnTextRT.offsetMax = Vector2.zero;
        var btnText = btnTextGo.AddComponent<TextMeshProUGUI>();
        btnText.font = desc.font;
        btnText.fontSize = confirmFontSize;
        btnText.text = "확인";
        btnText.color = confirmTextColor;
        btnText.alignment = TextAlignmentOptions.Center;
        btnText.raycastTarget = false;

        Button b = btnGo.AddComponent<Button>();
        b.transition = Selectable.Transition.None;
        b.targetGraphic = border;
        int index = i;   // ★클로저 캡처 — 루프 변수를 그대로 쓰면 전부 마지막 값이 된다
        b.onClick.AddListener(() => OnConfirm(index));

        // --- 완료 표시 (확인 버튼과 같은 자리에 겹쳐 둔다) ---
        var markGo = new GameObject($"완료{i}", typeof(RectTransform));
        RectTransform markRT = markGo.GetComponent<RectTransform>();
        markRT.SetParent(root, false);
        Anchor(markRT, new Vector2(rowIndent + (confirmSize.x - confirmSize.y) * 0.5f, y),
               new Vector2(confirmSize.y, confirmSize.y));
        Image mark = markGo.AddComponent<Image>();
        mark.color = doneMarkColor;
        mark.raycastTarget = false;

        // --- 글씨 ---
        var labelGo = new GameObject($"항목{i}", typeof(RectTransform));
        RectTransform labelRT = labelGo.GetComponent<RectTransform>();
        labelRT.SetParent(root, false);
        Anchor(labelRT, new Vector2(rowIndent + confirmSize.x + labelGap, y), new Vector2(labelWidth, labelHeight));

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

        confirmButtons.Add(btnGo);
        doneMarks.Add(mark);
        labels.Add(label);
        rowObjects.Add(labelGo);
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

    private void OnConfirm(int index)
    {
        // ★지금 차례인 줄만 받는다. 뒤 줄은 애초에 숨겨져 있지만, 중복 클릭도 여기서 막힌다.
        if (index != currentIndex) return;

        currentIndex++;
        RefreshRows();

        if (AllChecked)
        {
            if (showDebugLogs)
                ChunaLogger.Log("<color=cyan>[표준자세] 전부 확인됨 — 브리지가 다음 단계로 넘긴다.</color>");
            return;
        }

        PlayNarrationFor(currentIndex);

        if (showDebugLogs)
            ChunaLogger.Log($"<color=cyan>[표준자세] {index + 1}번 확인 — 다음 줄({currentIndex + 1}/{ItemCount})</color>");
    }

    /// <summary>지난 줄은 완료 표시, 지금 줄은 확인 버튼, 앞으로 올 줄은 통째로 숨김.</summary>
    private void RefreshRows()
    {
        for (int i = 0; i < ItemCount; i++)
        {
            bool done = i < currentIndex;
            bool current = i == currentIndex && sequenceStarted;
            bool shown = done || current;

            if (i < confirmButtons.Count && confirmButtons[i] != null)
                confirmButtons[i].SetActive(current);
            if (i < doneMarks.Count && doneMarks[i] != null)
                doneMarks[i].enabled = done;
            if (i < rowObjects.Count && rowObjects[i] != null)
                rowObjects[i].SetActive(shown);
            if (i < labels.Count && labels[i] != null)
                labels[i].color = done ? doneTextColor : descColorBackup;
        }
    }
}
