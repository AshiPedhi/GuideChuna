using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 전문가 영상 재생 조작 메뉴(2026-11-03 신설 · 11-04 개편).
///
/// <code>
///   [ 근골격 · 진행 현황 · 전문가 영상 | 설정 · 시작 · 홈으로 ]   ← 상단 메뉴
///        [ ⏮  ▶  ━━━━●──────  0:03 / 0:06  [1인칭으로] ]       ← 이 메뉴(상단 바 바로 아래 판 «미디어 조작 판»)
/// </code>
/// ★11-04 오후 사용자 «상단 버튼 누르면 영상 조작 메뉴 상단메뉴 바로 아래에 나오게 하라니까» — 영상 창 위에 얹던 것을 상단 바 아래 판으로 옮겼다.
///   판(UISet · 자체 손 · 레이 누름 영역)은 TopBar1104Tool이 씬에 만들고, 버튼은 여기서 런타임에 채운다.
///
/// ★11-04 사용자 «전문가 영상만 토글로 미디어 조작 메뉴 on/off (간단한 기본 기능만 + 1인칭 3인칭 스위치 버튼)».
///   → 메뉴는 <b>처음엔 숨김</b>이고 상단 [전문가 영상] 버튼(<see cref="TopBarProxyButton"/> 동작 ExpertVideoControls)이 켜고 끈다.
///   → 기능 = 처음부터 · 재생/일시정지 · 재생 막대 · 시간 · 1인칭/3인칭(1인칭 영상이 있는 술기만).
///   종전(11-03) ⟲5 · 5⟳ · 반복 · 0.5배속 · 소리는 뺐다. 반복은 기본값(늘 켜짐)으로 돈다.
/// ★씬은 건드리지 않는다(규칙 1) — 런타임에 <see cref="GuideVideoController"/>의 출력 텍스처를 그리는 RawImage를 찾아 그 위에 얹는다.
/// ★재생 동작은 전부 <see cref="GuideVideoController"/>의 함수를 탄다(규칙 9 «같은 함수»).
/// ★런타임에 스스로 붙는다(TrainingScene).
/// </summary>
public class ExpertVideoControlBar : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        if (s.name != "TrainingScene") return;
        if (FindFirstObjectByType<ExpertVideoControlBar>() != null) return;
        new GameObject("전문가 영상 조작 막대 (런타임)").AddComponent<ExpertVideoControlBar>();
    }

    /// <summary>씬에 하나뿐이다. 상단 [전문가 영상] 버튼이 찾는다.</summary>
    public static ExpertVideoControlBar Instance { get; private set; }

    /// <summary>상단 바 아래 판 이름(TopBar1104Tool이 씬에 만든다 · «상단 바 A (시안)» 자식).</summary>
    public const string PlateName = "미디어 조작 판";

    private GuideVideoController gvc;
    private RectTransform menu, fill;
    private GameObject plate;
    private TextMeshProUGUI timeText, viewText;
    private GameObject viewButton;
    private RawImage playIcon;
    private Texture2D icPlay, icPause;
    private bool built, visible;
    private float nextFind;
    private int lastSec = -1, lastFill = -1, lastView = -1;
    private bool lastPlaying;

    /// <summary>조작 메뉴가 보이는가. 막대가 아직 안 붙었어도 원하는 상태는 기억했다가 붙을 때 적용한다.</summary>
    public bool Visible => visible;

    public void SetVisible(bool on)
    {
        visible = on;
        if (plate != null) plate.SetActive(on);
        Debug.Log($"[전문가영상막대] 조작 메뉴 {(on ? "켬" : "끔")}{(plate == null ? " (★상단 메뉴 아래 판이 아직 없다 — 붙을 때 적용)" : "")}");
    }

    public void ToggleVisible() => SetVisible(!visible);

    private void Awake() { Instance = this; }
    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void Update()
    {
        if (!built)
        {
            if (Time.unscaledTime < nextFind) return;
            nextFind = Time.unscaledTime + 1f;   // 매 프레임 찾지 않는다
            TryBuild();
            return;
        }
        if (!visible || plate == null || !plate.activeInHierarchy) return;

        bool has = gvc.TryGetRange(out float s, out float e);
        float len = has ? Mathf.Max(0.01f, e - s) : 1f;
        float rel = has ? Mathf.Clamp((float)gvc.Player.time - s, 0f, len) : 0f;
        int f = Mathf.RoundToInt(rel / len * 200f);
        if (f != lastFill) { lastFill = f; fill.anchorMax = new Vector2(f / 200f, 1f); }
        int sec = has ? Mathf.FloorToInt(rel) * 1000 + Mathf.FloorToInt(len) : -1;
        if (sec != lastSec) { lastSec = sec; timeText.text = has ? $"{Fmt(rel)} / {Fmt(len)}" : ""; }
        bool playing = gvc.Player != null && gvc.Player.isPlaying;
        if (playing != lastPlaying) { lastPlaying = playing; playIcon.texture = playing ? icPause : icPlay; }
        // 1인칭 판 유무는 영상을 새로 걸 때 바뀐다 — 상태가 바뀐 프레임에만 손댄다(할당 없음)
        int v = !gvc.HasFirstPerson ? 0 : gvc.IsFirstPerson ? 2 : 1;
        if (v != lastView)
        {
            lastView = v;
            viewButton.SetActive(v != 0);
            viewText.text = v == 2 ? "3인칭으로" : "1인칭으로";   // 누르면 갈 쪽을 적는다
        }
    }

    private void TryBuild()
    {
        if (gvc == null) gvc = FindFirstObjectByType<GuideVideoController>(FindObjectsInactive.Include);
        if (gvc == null || gvc.Player == null) return;

        // ★11-04 사용자 «상단 버튼 누르면 영상 조작 메뉴 상단메뉴 바로 아래에 나오게» — 영상 창 위가 아니라 상단 바 아래 판에 그린다.
        //   (종전 11-04 오전판은 출력 텍스처로 영상 칸을 찾았는데, 같은 텍스처를 쓰는 45×45 칸에 붙어 보이지 않았다 — Editor.log 실측)
        if (plate == null)
            foreach (var placer in FindObjectsByType<TopBarPlacer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var t = placer.transform.Find(PlateName);
                if (t != null) { plate = t.gameObject; break; }
            }
        if (plate == null) { Debug.LogWarning($"[전문가영상막대] ★상단 바 아래 '{PlateName}'이 씬에 없다 — 메뉴 «11-04 상단 메뉴 …»를 돌리고 저장"); built = true; return; }
        RectTransform back = null;
        foreach (var r in plate.GetComponentsInChildren<RectTransform>(true)) if (r.name == "UIBackplate") { back = r; break; }
        if (back == null) { Debug.LogWarning($"[전문가영상막대] ★'{PlateName}'에 UIBackplate가 없다"); built = true; return; }

        icPlay = Load("play"); icPause = Load("pause");
        var font = KoreanFontResolver.Resolve();

        //   [⏮] [▶]  ━━━━●──────  0:03 / 0:06  [1인칭으로]
        menu = Rect("조작 메뉴 (런타임)", back, Vector2.zero, Vector2.one);
        var bar = menu;
        // 재생 막대 — 누르는 칸은 넉넉히, 보이는 줄은 가늘게
        var hitR = Rect("재생 막대", bar, new Vector2(0.25f, 0.2f), new Vector2(0.62f, 0.8f));
        var hit = hitR.gameObject.AddComponent<Image>(); hit.color = new Color(1f, 1f, 1f, 0.001f);
        var strip = Rect("줄", hitR, new Vector2(0f, 0.42f), new Vector2(1f, 0.58f));
        var sbg = strip.gameObject.AddComponent<Image>(); sbg.color = new Color(1f, 1f, 1f, 0.3f); sbg.raycastTarget = false;
        hitR.gameObject.AddComponent<SeekStrip>().Init(this, strip);
        fill = Rect("채움", strip, Vector2.zero, new Vector2(0f, 1f));
        var fi = fill.gameObject.AddComponent<Image>(); fi.color = new Color(1f, 0.2f, 0.2f, 1f); fi.raycastTarget = false;

        Btn(bar, 0.08f, Load("restart"), Restart);
        playIcon = Btn(bar, 0.18f, icPause, () => gvc.TogglePause());

        var tt = Rect("시간", bar, new Vector2(0.63f, 0.15f), new Vector2(0.80f, 0.85f));
        timeText = MakeText(tt, font);

        // 1인칭/3인칭 — 글자 버튼(아이콘으로는 뜻이 안 선다)
        var vr = Rect("시점 전환", bar, new Vector2(0.81f, 0.2f), new Vector2(0.97f, 0.8f));
        var vbg = vr.gameObject.AddComponent<Image>(); vbg.color = Color.white;
        var vb = vr.gameObject.AddComponent<Button>(); vb.targetGraphic = vbg;
        var vcb = vb.colors;
        vcb.normalColor = vcb.selectedColor = new Color(1f, 1f, 1f, 0.18f);
        vcb.highlightedColor = new Color(1f, 1f, 1f, 0.35f); vcb.pressedColor = new Color(1f, 1f, 1f, 0.5f); vcb.colorMultiplier = 1f;
        vb.colors = vcb;
        vb.onClick.AddListener(ToggleView);
        viewText = MakeText(Rect("글자", vr, Vector2.zero, Vector2.one), font);
        viewButton = vr.gameObject;
        viewButton.SetActive(false);

        plate.SetActive(visible);
        built = true;
        Debug.Log($"[전문가영상막대] 붙음 · 상단 바 아래 '{PlateName}' ({back.rect.width:F0}×{back.rect.height:F0}) · 메뉴 {(visible ? "보임" : "숨김")} · 1인칭 판 {(gvc.HasFirstPerson ? "있음" : "없음")}");
    }

    private static TextMeshProUGUI MakeText(RectTransform r, TMP_FontAsset font)
    {
        var t = r.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.enableAutoSizing = true; t.fontSizeMin = 4; t.fontSizeMax = 60;
        t.alignment = TextAlignmentOptions.Center; t.color = Color.white; t.raycastTarget = false;
        return t;
    }

    private static Texture2D Load(string n)
    {
        var t = Resources.Load<Texture2D>("Icons/Media/" + n);
        if (t == null) Debug.LogWarning($"[전문가영상막대] 아이콘 없음 '{n}'");
        return t;
    }

    private static RectTransform Rect(string n, Transform parent, Vector2 aMin, Vector2 aMax)
    {
        var go = new GameObject(n, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var r = (RectTransform)go.transform;
        r.SetParent(parent, false);
        r.anchorMin = aMin; r.anchorMax = aMax; r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
        go.AddComponent<LayoutElement>().ignoreLayout = true;   // UISet 판(UIBackplate)의 레이아웃 그룹에 끌려가지 않게
        return r;
    }

    /// <summary>띠 높이에 맞춘 정사각 아이콘 버튼. <paramref name="cx"/> = 띠 안 가로 위치(0~1).</summary>
    private RawImage Btn(RectTransform bar, float cx, Texture2D icon, UnityEngine.Events.UnityAction onClick)
    {
        var r = Rect("버튼", bar, new Vector2(cx, 0.1f), new Vector2(cx, 0.9f));
        var fit = r.gameObject.AddComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth; fit.aspectRatio = 1f;
        var hit = r.gameObject.AddComponent<Image>(); hit.color = new Color(1f, 1f, 1f, 0.001f);   // 누름 칸
        var b = r.gameObject.AddComponent<Button>();
        b.targetGraphic = hit;
        var cb = b.colors; cb.highlightedColor = new Color(1f, 1f, 1f, 0.25f); cb.pressedColor = new Color(1f, 1f, 1f, 0.4f); b.colors = cb;
        b.onClick.AddListener(onClick);
        var ir = Rect("아이콘", r, new Vector2(0.15f, 0.15f), new Vector2(0.85f, 0.85f));
        var img = ir.gameObject.AddComponent<RawImage>(); img.texture = icon; img.raycastTarget = false;
        return img;
    }

    /// <summary>지금 범위(단계 구간 또는 전체)의 처음으로.</summary>
    private void Restart()
    {
        if (!gvc.TryGetRange(out float s, out _)) return;
        gvc.SeekTo(s);
        lastSec = -1;
    }

    private void ToggleView()
    {
        gvc.SetFirstPerson(!gvc.IsFirstPerson);
        lastView = -1;
    }

    /// <summary>재생 막대를 누른 자리(0~1)로.</summary>
    internal void SeekFraction(float fx)
    {
        if (!gvc.TryGetRange(out float s, out float e)) return;
        gvc.SeekTo(s + Mathf.Clamp01(fx) * (e - s));
        lastSec = -1;
    }

    private static string Fmt(float s)
    {
        int m = (int)(s / 60f), x = (int)(s % 60f);
        return m + ":" + x.ToString("00");
    }

    /// <summary>재생 막대 누름 — 누른 월드 좌표를 막대 안 가로 위치로 바꾼다(레이 · 손가락 모두 worldPosition이 온다).</summary>
    private class SeekStrip : MonoBehaviour, IPointerDownHandler
    {
        private ExpertVideoControlBar owner;
        private RectTransform rect;
        public void Init(ExpertVideoControlBar o, RectTransform r) { owner = o; rect = r; }

        public void OnPointerDown(PointerEventData ev)
        {
            Vector3 w = ev.pointerCurrentRaycast.worldPosition;
            Vector2 local;
            if (w != Vector3.zero) local = rect.InverseTransformPoint(w);
            else if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, ev.position, ev.pressEventCamera, out local)) return;
            float fx = (local.x - rect.rect.xMin) / Mathf.Max(1e-4f, rect.rect.width);
            owner.SeekFraction(fx);
        }
    }
}
