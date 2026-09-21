using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 실측 기록의 손목 메뉴 — <b>Meta Interaction SDK의 UI Set</b>으로 만든 판(2026-09-21 사용자 지시).
///
/// ★09-21 증상: "빈 공간도 많고 글씨도 작다 · 누르려다 손에 간섭된다."
///   글씨는 Meta 표준을 그대로 쓴다 — 라벨 14px × 캔버스 스케일 0.0005 = <b>7mm</b>.
///   종전 월드 TMP(fontSize 0.03)는 계산상 그 절반이 안 됐다(★계산이다, 기기 실측이 아니다).
///   빈 공간은 <b>판 폭을 내용에 맞춰</b> 줄여 없앤다 — 종전엔 항목이 다섯이어도 6칸 폭을 썼다.
///
/// ★쓰는 프리팹(Assets/Resources/RomRecordUI/에 패키지에서 들여온 것)
///   · RomMenuBackplate       — 그 자체로 Poke가 되는 판이다(PokeInteractable·PointableCanvas·GraphicRaycaster).
///   · RomMenuButtonPrimary   — 보통 버튼 · RomMenuButtonSecondary — 보조 · RomMenuButtonDestructive — 나가기
///   ★버튼 프리팹에는 Unity <c>Button</c>이 없다. 생김새와 레이아웃만 들어 있어
///     누름은 <see cref="RomMenuButtonUI"/>가 포인터 이벤트로 받는다(09-21 프리팹 실측).
///
/// ★누름 판정을 ISDK가 한다 — 손가락 좌표를 이 클래스가 재지 않는다(구현 1과 가장 다른 점).
///   씬에 <c>PointableCanvasModule</c>과 <c>EventSystem</c>이 있어야 돈다. 없으면 <b>조용히</b> 안 눌린다.
///   그래서 Build에서 둘을 확인하고 없으면 경고를 남긴다.
/// </summary>
public class RomRecordMenuUI : IRomRecordMenu
{
    private class Btn
    {
        public GameObject go;
        public RectTransform rt;
        public RomMenuButtonUI press;
        public Image background;
        public Image fill;
        public TextMeshProUGUI label;
        public Color tint;
        public bool isText;        // 누를 수 없는 글자 칸
    }

    // ── 모양(캔버스 픽셀. 캔버스 스케일 0.0005라 1px = 0.5mm) ──────────
    public float cellPx = 84f;       // 칸 너비 — 6칸이면 504px ≈ 25cm
    public float rowPx = 68f;        // 줄 높이
    public float buttonPx = 56f;     // 버튼 높이
    public float labelPt = 15f;      // 버튼 글자(Meta 표준 14)
    public float headerPt = 15f;
    public float padPx = 14f;        // 판 안쪽 여백
    // ── 자리 ─────────────────────────────────────────────────────────
    public float lift = 0.06f;
    public float towardEye = 0.02f;
    public float followSharpness = 14f;
    public float grace = 1.5f;
    public float cooldown = 0.35f;

    private const string PrefabDir = "RomRecordUI/";

    private Transform root;
    private GameObject plate;
    private RectTransform canvasRoot;
    private RectTransform backdrop;     // UIBackplate — 둥근 배경판(★고정 크기라 같이 늘려야 한다)
    private RectTransform pokeArea;     // ISDK_PokeInteraction — Poke 표면의 범위
    private TextMeshProUGUI header;
    private readonly List<Btn> pool = new List<Btn>();
    private int activeCount;
    // ★버튼 프리팹은 하나만 쓴다 — Meta의 Primary/Secondary/Destructive 차이는 사실상 배경색이라
    //   색은 tint로 준다(Resources 폴더는 통째로 빌드에 들어가므로 안 쓰는 것을 두지 않는다).
    private GameObject primaryPrefab;
    private TMP_FontAsset korean;
    private string displayName;
    private bool placedOnce;
    private float lastValidTime = -99f;
    private float cooldownUntil;
    private float panelW, panelH;          // 월드 크기(m)
    private string pendingId;
    private bool pendingRepeat;

    public bool Visible => root != null && root.gameObject.activeSelf;
    public bool LastRepeat { get; private set; }

    /// <summary>버튼이 눌렸다고 알려 온다(<see cref="RomMenuButtonUI"/>가 부른다).</summary>
    public void OnPressed(string id, bool repeat)
    {
        if (Time.unscaledTime < cooldownUntil) return;
        pendingId = id;
        pendingRepeat = repeat;
        if (!repeat) cooldownUntil = Time.unscaledTime + cooldown;
    }

    public void Build(Transform parent, string name, int capacity, TMP_FontAsset font, Material mat)
    {
        displayName = name;
        korean = font;
        root = new GameObject("[실측기록] " + name).transform;
        root.SetParent(parent, false);

        GameObject backPrefab = Resources.Load<GameObject>(PrefabDir + "RomMenuBackplate");
        primaryPrefab = Resources.Load<GameObject>(PrefabDir + "RomMenuButtonPrimary");
        if (backPrefab == null || primaryPrefab == null)
        {
            Debug.LogWarning($"[실측기록] ★{displayName} — UI Set 프리팹을 못 찾았다(Resources/{PrefabDir}). 판을 못 만든다.");
            root.gameObject.SetActive(false);
            return;
        }

        plate = Object.Instantiate(backPrefab, root);
        plate.name = "판";

        // CanvasRoot를 찾는다. ★이름으로 찾는다 — 못 찾으면 조용히 죽으므로 경고를 남긴다(규칙 8).
        Canvas canvas = plate.GetComponentInChildren<Canvas>(true);
        if (canvas == null)
        {
            Debug.LogWarning($"[실측기록] ★{displayName} — 판 프리팹에 Canvas가 없다. 버튼을 못 붙인다.");
            return;
        }
        canvasRoot = canvas.transform as RectTransform;
        // ★판 프리팹의 캔버스에는 가로 레이아웃이 걸려 있다 — 켜 두면 우리가 정한 칸 자리가 무시된다.
        var hl = canvasRoot.GetComponent<HorizontalLayoutGroup>();
        if (hl != null) hl.enabled = false;

        // ★배경과 Poke 범위를 캔버스에 <b>늘어붙게</b> 만든다(09-21 프리팹 실측: 계층은
        //   CanvasRoot > UIBackplate / ISDK_PokeInteraction > Surface, 배경은 500x500 고정이었다).
        //   레이아웃 그룹을 껐으니 이렇게 묶어 두지 않으면 판 크기를 바꿔도 배경이 따라오지 않는다.
        backdrop = FindDeep(plate.transform, "UIBackplate") as RectTransform;
        pokeArea = FindDeep(plate.transform, "ISDK_PokeInteraction") as RectTransform;
        if (backdrop == null) Debug.LogWarning($"[실측기록] ★{displayName} — 판 배경(UIBackplate)을 못 찾았다. 배경이 안 따라온다.");
        Stretch(backdrop);
        Stretch(pokeArea);

        header = MakeLabel(canvasRoot, headerPt, TextAlignmentOptions.MidlineLeft, "머리줄");

        for (int i = 0; i < capacity; i++) pool.Add(MakeButton());

        // ★조용히 안 눌리는 두 가지를 미리 잡는다.
        if (Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            Debug.LogWarning("[실측기록] ★씬에 EventSystem이 없다 — Meta UI 버튼이 조용히 안 눌린다.");
        if (Object.FindAnyObjectByType<Oculus.Interaction.PointableCanvasModule>() == null)
            Debug.LogWarning("[실측기록] ★씬에 PointableCanvasModule이 없다 — 손가락 Poke가 uGUI로 전달되지 않는다.");

        root.gameObject.SetActive(false);
        Debug.Log($"[실측기록] {displayName} — Meta UI 판을 만들었다(칸 {cellPx}px · 글자 {labelPt}pt · 버튼 {pool.Count}개)");
    }

    private Btn MakeButton()
    {
        var go = Object.Instantiate(primaryPrefab, canvasRoot);
        var b = new Btn { go = go, rt = go.transform as RectTransform };

        b.background = FindImage(go.transform, "Background");
        b.label = FindText(go.transform, "Label");
        // 부제와 아이콘은 안 쓴다 — 라벨만 가운데 남긴다.
        Transform sub = go.transform.Find("Content/Elements/Text/Subtitle") ?? FindDeep(go.transform, "Subtitle");
        if (sub != null) sub.gameObject.SetActive(false);
        Transform icon = FindDeep(go.transform, "Icon");
        if (icon != null) icon.gameObject.SetActive(false);

        if (b.label != null && korean != null) b.label.font = korean;
        if (b.label != null)
        {
            b.label.fontSize = labelPt;
            b.label.alignment = TextAlignmentOptions.Center;
            b.label.enableAutoSizing = false;
        }

        // 길게 누르기 막대 — 레이아웃에 끼지 않게 무시 표시를 준다.
        var fillGo = new GameObject("차오름", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
        var fillRt = (RectTransform)fillGo.transform;
        fillRt.SetParent(b.rt, false);
        fillRt.anchorMin = new Vector2(0f, 0f);
        fillRt.anchorMax = new Vector2(1f, 0.12f);
        fillRt.offsetMin = new Vector2(4f, 3f);
        fillRt.offsetMax = new Vector2(-4f, 0f);
        fillGo.GetComponent<LayoutElement>().ignoreLayout = true;
        b.fill = fillGo.GetComponent<Image>();
        b.fill.color = new Color(1f, 1f, 1f, 0.92f);
        b.fill.type = Image.Type.Filled;
        b.fill.fillMethod = Image.FillMethod.Horizontal;
        b.fill.raycastTarget = false;
        fillGo.SetActive(false);

        b.press = go.AddComponent<RomMenuButtonUI>();
        b.press.owner = this;
        b.press.Setup(b.background, b.fill);

        go.SetActive(false);
        return b;
    }

    public void SetLayout(string headerText, RomMenuItem[] items)
    {
        if (canvasRoot == null) return;
        cooldownUntil = Time.unscaledTime + cooldown;
        activeCount = Mathf.Min(items.Length, pool.Count);

        // ★판 폭을 내용에 맞춘다 — 빈 칸이 남지 않게(09-21 "빈 공간이 많다").
        float maxCol = 1f, maxRow = 0f;
        for (int i = 0; i < activeCount; i++)
        {
            maxCol = Mathf.Max(maxCol, items[i].col + items[i].span);
            maxRow = Mathf.Max(maxRow, items[i].row);
        }
        float wPx = maxCol * cellPx + padPx * 2f;
        float hPx = (maxRow + 1f) * rowPx + padPx * 2f;
        Resize(wPx, hPx);

        header.text = headerText;
        header.rectTransform.anchoredPosition = new Vector2(padPx, -padPx);
        header.rectTransform.sizeDelta = new Vector2(wPx - padPx * 2f, rowPx);

        for (int i = 0; i < pool.Count; i++)
        {
            Btn b = pool[i];
            bool on = i < activeCount;
            if (b.go.activeSelf != on) b.go.SetActive(on);
            if (!on) { b.press.id = null; continue; }

            RomMenuItem it = items[i];
            b.isText = it.id == null;
            b.press.id = it.id;
            b.press.repeat = it.repeat;
            b.press.holdSeconds = it.holdSeconds;
            b.tint = it.tint;

            float w = it.span * cellPx - 6f;
            b.rt.anchorMin = b.rt.anchorMax = new Vector2(0f, 1f);
            b.rt.pivot = new Vector2(0f, 1f);
            b.rt.sizeDelta = new Vector2(w, b.isText ? rowPx : buttonPx);
            b.rt.anchoredPosition = new Vector2(padPx + it.col * cellPx,
                                                -(padPx + it.row * rowPx) - (rowPx - (b.isText ? rowPx : buttonPx)) * 0.5f);

            if (b.label != null)
            {
                b.label.text = it.label;
                b.label.fontStyle = it.selected ? FontStyles.Bold : FontStyles.Normal;
            }
            if (b.background != null)
            {
                // 글자 칸은 배경을 지운다 — 누를 수 없는 것이 버튼처럼 보이면 안 된다.
                Color c = b.isText ? new Color(0f, 0f, 0f, 0f)
                        : it.selected ? Color.Lerp(it.tint, Color.white, 0.35f)
                        : it.tint;
                b.background.color = c;
                b.background.raycastTarget = !b.isText;
            }
            b.press.enabled = !b.isText;
            b.press.Setup(b.background, b.fill);
            if (b.fill != null) b.fill.gameObject.SetActive(false);
        }
    }

    /// <summary>부모에 늘어붙게 만든다 — 판 크기를 바꾸면 따라오게.</summary>
    private static void Stretch(RectTransform r)
    {
        if (r == null) return;
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = Vector2.zero;
        r.offsetMax = Vector2.zero;
    }

    /// <summary>판 크기를 바꾼다. 월드 크기도 같이 기억해 둔다(Near·Distance가 쓴다).</summary>
    private void Resize(float wPx, float hPx)
    {
        canvasRoot.sizeDelta = new Vector2(wPx, hPx);
        // 배경·Poke 범위는 Build에서 늘어붙게 묶어 뒀다 — 여기서 따로 손대지 않는다.
        float s = canvasRoot.lossyScale.x;
        panelW = wPx * s;
        panelH = hPx * s;
    }

    public void SetHeader(string text)
    {
        if (header != null && header.text != text) header.text = text;
    }

    public void Follow(bool wristValid, Vector3 wrist, Transform eye, bool hold)
    {
        if (root == null || eye == null) return;
        float now = Time.unscaledTime;
        if (wristValid) lastValidTime = now;

        bool show = wristValid || (placedOnce && (hold || now - lastValidTime < grace));
        if (root.gameObject.activeSelf != show) root.gameObject.SetActive(show);
        if (!show) { placedOnce = false; return; }

        if (!wristValid || (hold && placedOnce)) return;

        Vector3 toEye = eye.position - wrist;
        Vector3 anchor = wrist + Vector3.up * lift + toEye.normalized * towardEye;
        Vector3 target = anchor + Vector3.up * (panelH * 0.5f);
        // ★판은 <b>눈을 보게</b> 돌린다. uGUI 캔버스는 +Z가 앞이라 월드 TMP와 부호가 반대다.
        Quaternion rot = Quaternion.LookRotation(target - eye.position, Vector3.up);

        if (!placedOnce)
        {
            root.SetPositionAndRotation(target, rot);
            placedOnce = true;
            return;
        }
        float k = 1f - Mathf.Exp(-followSharpness * Time.unscaledDeltaTime);
        root.SetPositionAndRotation(Vector3.Lerp(root.position, target, k), Quaternion.Slerp(root.rotation, rot, k));
    }

    public void Tick() { }   // 눌림 표시·반복·길게 누르기는 버튼이 스스로 돌린다

    public float Distance(Vector3 p) => Visible ? Vector3.Distance(root.position, p) : float.MaxValue;

    public bool Near(Vector3 tip, float margin)
    {
        if (!Visible) return false;
        Vector3 l = root.InverseTransformPoint(tip);
        return Mathf.Abs(l.x) < panelW * 0.5f + margin && Mathf.Abs(l.y) < panelH * 0.5f + margin && Mathf.Abs(l.z) < margin;
    }

    public string Poll(Vector3 tip, bool tipValid)
    {
        if (pendingId == null) return null;
        string id = pendingId;
        pendingId = null;
        LastRepeat = pendingRepeat;
        return id;
    }

    // ── 만드는 도구 ──────────────────────────────────────────────────
    private TextMeshProUGUI MakeLabel(RectTransform parent, float pt, TextAlignmentOptions align, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (korean != null) t.font = korean;
        t.fontSize = pt;
        t.alignment = align;
        t.color = Color.white;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget = false;
        var le = go.AddComponent<LayoutElement>();
        le.ignoreLayout = true;
        return t;
    }

    private static Image FindImage(Transform root, string name)
    {
        Transform t = FindDeep(root, name);
        return t != null ? t.GetComponent<Image>() : null;
    }

    private static TextMeshProUGUI FindText(Transform root, string name)
    {
        Transform t = FindDeep(root, name);
        return t != null ? t.GetComponent<TextMeshProUGUI>() : null;
    }

    /// <summary>이름으로 자손을 찾는다. ★못 찾으면 null이고, 부르는 쪽이 경고를 남긴다(규칙 8).</summary>
    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform r = FindDeep(root.GetChild(i), name);
            if (r != null) return r;
        }
        return null;
    }
}
