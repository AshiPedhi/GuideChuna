using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 상단 바 A안(시안 V0.7 그림 4-2 · 가로 알약 버튼)의 버튼 하나 (2026-10-27).
///
/// 진짜 토글(골격 · 영상 · 결과 · 설정 · 메인으로)은 B안 바 안에 그대로 있고 <see cref="InfoPanelController"/>가 그 토글을 쥔다.
/// 이 버튼은 겉모양만 다르다 — 누르면 target.isOn을 뒤집고, 켜짐 · 누를 수 있음을 target에서 읽어 색으로 보인다.
/// 상태가 바뀐 프레임에만 색을 바꾼다(할당 없음).
///
/// ★11-04 사용자 피드백 — 창(근골격 · 진행 현황 · 전문가 영상)은 <b>늘 켜져 있고</b> 상단 메뉴로 켜고 끄지 않는다.
///   버튼마다 <see cref="action"/>이 갈린다:
///   · ToggleTarget        = 종전 그대로(설정 · 홈으로)
///   · HoverOnly           = 누름 없음 · 가리키면 설명만(근골격 · 진행 현황)
///   · ExpertVideoControls = 전문가 영상 창의 미디어 조작 메뉴 켜기/끄기(<see cref="ExpertVideoControlBar"/>)
///   · StartTimer          = [시작] → 개요 화면에서 실습 시작 · 시작 뒤엔 그 자리에 실습 수행 시간(<see cref="TrainingResultTracker.SessionElapsed"/>)
///   ★enum 값은 끝에만 붙인다(씬에 int로 굳는다). 기본 0 = ToggleTarget이라 이 필드가 없던 버튼은 종전과 같다.
/// ★가리키면 버튼 아래에 작은 설명 창(<see cref="hoverTip"/>)이 뜬다. 비워 두면 안 뜬다.
/// </summary>
[RequireComponent(typeof(Button))]
public class TopBarProxyButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public enum PillAction { ToggleTarget = 0, HoverOnly = 1, ExpertVideoControls = 2, StartTimer = 3 }

    [SerializeField] private Toggle target;
    [SerializeField] private Graphic background;
    [SerializeField] private Color onColor = new Color(0.18f, 0.48f, 0.96f, 1f);
    [SerializeField] private Color offColor = new Color(0.23f, 0.25f, 0.28f, 1f);
    [Tooltip("켜짐에 따라 글자 · 아이콘 색도 바꾼다(흰 알약 위 어두운 글자) · 비워도 된다")]
    [SerializeField] private Graphic[] foreground = new Graphic[0];
    [SerializeField] private Color foreOn = new Color(0.153f, 0.153f, 0.153f, 1f);
    [SerializeField] private Color foreOff = new Color(0.94f, 0.95f, 0.96f, 1f);

    [Header("11-04 동작 · 설명")]
    [Tooltip("누르면 무엇을 하나. 기본 ToggleTarget = 종전 그대로")]
    [SerializeField] private PillAction action = PillAction.ToggleTarget;
    [Tooltip("가리키면 버튼 아래에 뜨는 짧은 설명. 비우면 안 뜬다")]
    [SerializeField] private string hoverTip = "";

    private Button button;
    private int lastState = -1;
    private InfoPanelController panel;
    private TrainingResultTracker tracker;
    private TMP_Text label;
    private string idleLabel;
    private int lastTimerSec = -1;
    private float heldElapsed;
    private RectTransform tip;

    public PillAction Action => action;

    private void Awake()
    {
        button = GetComponent<Button>();
        button.onClick.AddListener(OnClick);

        // ★10-30 사용자 «켜져 있는지 토글 효과가 없다» — UISet 애니메이터(SecondaryButton_Dark)가 매 프레임 바탕색을 덮어 켜짐 색이 안 보였다.
        //   애니메이터를 끄고, 가리킴 · 누름은 로비와 같은 색 틴트(평소 0.8 · 가리킴 1 · 누름 0.65 — ButtonHoverUnifyTool)로 바꾼다
        if (TryGetComponent<Animator>(out var anim)) anim.enabled = false;
        if (background != null)
        {
            button.transition = Selectable.Transition.ColorTint;
            button.targetGraphic = background;
            var cb = button.colors;
            cb.normalColor = cb.selectedColor = new Color(0.8f, 0.8f, 0.8f, 1f); cb.highlightedColor = Color.white;
            cb.pressedColor = new Color(0.65f, 0.65f, 0.65f, 1f); cb.colorMultiplier = 1f; cb.fadeDuration = 0.08f;
            button.colors = cb;
        }
        // 글자 · 아이콘을 비워 뒀으면 바탕 말고 자식 그림 전부 — 켜짐(흰 알약)에서 어두운 글자로
        if (foreground == null || foreground.Length == 0)
        {
            var all = GetComponentsInChildren<Graphic>(true);
            int n = 0;
            for (int i = 0; i < all.Length; i++) if (all[i] != background) n++;
            foreground = new Graphic[n];
            n = 0;
            for (int i = 0; i < all.Length; i++) if (all[i] != background) foreground[n++] = all[i];
        }

        // 글자 칸(UISet 알약: Content/Background/Elements/Text/Label)
        foreach (var t in GetComponentsInChildren<TMP_Text>(true))
            if (t.name == "Label") { label = t; break; }
        if (label != null) idleLabel = label.text;
    }

    private void Start()
    {
        panel = FindFirstObjectByType<InfoPanelController>(FindObjectsInactive.Include);
        if (action == PillAction.StartTimer)
        {
            tracker = FindFirstObjectByType<TrainingResultTracker>(FindObjectsInactive.Include);
            if (label == null) Debug.LogWarning($"[상단버튼] ★'{name}' 시작 버튼에 Label 글자가 없다 — 타이머를 못 띄운다");
            if (tracker == null) Debug.LogWarning($"[상단버튼] ★'{name}' TrainingResultTracker가 없다 — 타이머가 0:00에 머문다");
        }
    }

    private void OnClick()
    {
        switch (action)
        {
            case PillAction.HoverOnly:
                return;
            case PillAction.ExpertVideoControls:
                var bar = ExpertVideoControlBar.Instance;
                if (bar == null) { Debug.LogWarning("[상단버튼] ★전문가 영상 조작 막대가 없다(TrainingScene이 아니거나 아직 안 붙음)"); return; }
                bar.ToggleVisible();
                lastState = -1;
                return;
            case PillAction.StartTimer:
                if (panel == null || !panel.IsAwaitingStart) return;   // 이미 시작했으면 누름 없음(타이머 표시만)
                panel.BeginFromOverview();
                lastState = -1;
                return;
            default:
                if (target == null || !target.interactable) return;
                target.isOn = !target.isOn;   // 진짜 토글의 onValueChanged → InfoPanelController가 처리
                // ★11-04 사용자 «효과음은 기존 UI에 있는 거만 써» — 10-30에 넣은 ui_open/close(«팡팡») 소리는 뺐다(UiToggleAudio 파일은 남김)
                return;
        }
    }

    private void LateUpdate()
    {
        bool on, canPress;
        switch (action)
        {
            case PillAction.HoverOnly:
                on = false; canPress = true; break;   // 늘 같은 모양 — 창은 늘 켜져 있다
            case PillAction.ExpertVideoControls:
                on = ExpertVideoControlBar.Instance != null && ExpertVideoControlBar.Instance.Visible; canPress = true; break;
            case PillAction.StartTimer:
                bool waiting = panel != null && panel.IsAwaitingStart;
                // ★11-04 사용자 «시작 버튼 바탕이 흰색이라 안 보여» — 흰 알약(켜짐 색)을 쓰지 않는다. 다른 버튼과 같은 어두운 바탕 + 밝은 글자
                on = false; canPress = true;
                UpdateTimerLabel(waiting);
                break;
            default:
                if (target == null) return;
                on = target.isOn; canPress = target.interactable; break;
        }
        int s = (on ? 1 : 0) | (canPress ? 2 : 0);
        if (s == lastState) return;
        lastState = s;
        button.interactable = canPress;
        if (background != null) background.color = on ? onColor : offColor;
        for (int i = 0; i < foreground.Length; i++) if (foreground[i] != null) foreground[i].color = on ? foreOn : foreOff;
    }

    /// <summary>시작 전 = 원래 글자(«시작»), 시작 뒤 = m:ss. ★초가 바뀔 때만 글자를 바꾼다(VR 프레임 예산).
    /// 실습이 끝나면 SessionElapsed가 0으로 돌아간다 — 마지막 값을 붙들어 끝난 시간을 보인다.</summary>
    private void UpdateTimerLabel(bool waiting)
    {
        if (label == null) return;
        if (waiting)
        {
            if (lastTimerSec != -1) { lastTimerSec = -1; heldElapsed = 0f; label.text = idleLabel; }
            return;
        }
        float e = tracker != null ? tracker.SessionElapsed : 0f;
        if (e > 0f) heldElapsed = e;
        int sec = Mathf.FloorToInt(heldElapsed);
        if (sec == lastTimerSec) return;
        lastTimerSec = sec;
        label.text = (sec / 60) + ":" + (sec % 60).ToString("00");
    }

    // ── 가리키면 설명 창 ──
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (string.IsNullOrEmpty(hoverTip)) return;
        if (tip == null) tip = BuildTip();
        if (tip == null) return;
        PlaceTip();
        tip.gameObject.SetActive(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (tip != null) tip.gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        if (tip != null) tip.gameObject.SetActive(false);
    }

    /// <summary>설명 창을 캔버스 루트에 만든다 — 알약은 판(UIBackplate)의 Mask 안이라, 그 안에 두면 판 밖으로 나간 설명이 잘린다.</summary>
    private RectTransform BuildTip()
    {
        var canvas = GetComponentInParent<Canvas>(true);
        if (canvas == null) return null;
        var go = new GameObject("설명 창 (런타임) " + name, typeof(RectTransform));
        go.layer = gameObject.layer;
        var r = (RectTransform)go.transform;
        r.SetParent(canvas.transform, false);
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.pivot = new Vector2(0.5f, 1f);   // 위 가운데를 버튼 아래 가장자리에 붙인다
        go.AddComponent<LayoutElement>().ignoreLayout = true;   // CanvasRoot의 HorizontalLayoutGroup에 끌려가지 않게
        var bg = go.AddComponent<Image>(); bg.color = new Color(0.10f, 0.10f, 0.11f, 0.94f); bg.raycastTarget = false;
        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(14, 14, 8, 8);
        hlg.childControlWidth = hlg.childControlHeight = true; hlg.childForceExpandWidth = hlg.childForceExpandHeight = false;
        var fit = go.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var tgo = new GameObject("글자", typeof(RectTransform));
        tgo.layer = gameObject.layer;
        tgo.transform.SetParent(r, false);
        var t = tgo.AddComponent<TextMeshProUGUI>();
        if (label != null) { t.font = label.font; t.fontSharedMaterial = label.fontSharedMaterial; }
        else { var f = KoreanFontResolver.Resolve(); if (f != null) t.font = f; }
        t.text = hoverTip; t.fontSize = 14f; t.color = new Color(0.94f, 0.95f, 0.96f, 1f);
        t.alignment = TextAlignmentOptions.Center; t.textWrappingMode = TextWrappingModes.NoWrap; t.raycastTarget = false;
        go.SetActive(false);
        return r;
    }

    private void PlaceTip()
    {
        var self = (RectTransform)transform;
        var parent = (RectTransform)tip.parent;
        Vector3 bottom = self.TransformPoint(new Vector3(self.rect.center.x, self.rect.yMin, 0f));
        Vector3 local = parent.InverseTransformPoint(bottom);
        tip.localPosition = new Vector3(local.x, local.y - 8f, 0f);
        tip.localRotation = Quaternion.identity; tip.localScale = Vector3.one;
        tip.SetAsLastSibling();
    }
}
