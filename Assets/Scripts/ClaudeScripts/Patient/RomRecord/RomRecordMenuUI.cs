using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 실측 기록의 조작 판 — <b>Meta Interaction SDK의 UI Set</b>으로 만든 판(2026-09-21).
///
/// ★09-21 1차 판정은 <b>퇴화</b>였다(사용자): ①버튼 크기가 들쭉날쭉 ②배치가 헐거움 ③누르는 손에 가림.
///   ③은 판을 허공에 고정해(<see cref="FixedInSpace"/>) 따로 해결됐고, ①②를 아래에서 고쳤다(09-21 2차).
///
/// ── ①이 왜 생겼나 (전부 프리팹 YAML 실측이다. 추정이 아니다) ─────────────────
///   <c>RomMenuButtonPrimary.prefab</c>의 계층은 루트 > Content > Background > Elements > (Icon·Gap·Text > Label)이고
///   루트·Content·Elements·Text에 레이아웃 그룹이 <b>켜져</b> 있다.
///   · 루트 HorizontalLayoutGroup: ChildControlWidth=1 / <b>ChildControlHeight=0</b>
///   · Content HorizontalLayoutGroup: 같은 설정. Background의 sizeDelta.y는 <b>40으로 고정</b>이다.
///   → 가로는 내가 준 폭을 따라오지만 <b>세로는 무슨 값을 줘도 40px</b>이고, 정렬이 UpperLeft라
///     칸 위쪽에 붙는다. 그래서 칸 높이(<see cref="rowPx"/> 68)와 보이는 상자(40)가 어긋나
///     <b>버튼마다 크기가 다르게 보이고 아래에 빈 자리가 남았다</b> — ①과 ②가 같은 원인이었다.
///   · Label의 RectTransform은 <b>40×14px 고정</b>이고 왼쪽에 8px짜리 Gap이 붙어 있어
///     글자가 가운데에서 4px 밀리고, 석 자가 넘으면 칸을 넘친다.
///   · 루트 Animator의 <c>PrimaryButton_Dark</c> 컨트롤러가 Normal 상태를 <b>반복 재생</b>하며
///     <c>Content/Background</c>의 Image 색을 매 프레임 흰색(a=0.902)으로 덮어쓴다 → 우리 tint가 안 보인다.
///   ▶ 고친 방법: 프리팹 안의 레이아웃 그룹·애니메이터를 <b>전부 끄고</b>, 속(Content·Background·Elements·Text·Label)을
///     루트에 늘어붙게 만든다. 그러면 <b>루트 sizeDelta 하나가 보이는 크기이자 누름 영역</b>이 된다.
///     둥근 모서리는 <c>RoundedBoxUIProperties</c>가 uGUI 메시에 칸 크기를 실어 그리는 것이라
///     크기를 바꿔도 알아서 따라온다(RectTransform이 바뀌면 메시가 다시 만들어진다).
///
/// ── ③ Poke (프리팹·씬 실측) ────────────────────────────────────────────────
///   · 누름 판정은 <b>ISDK가</b> 한다. 이 클래스는 손가락 좌표를 재지 않는다(구현 1과 가장 다른 점).
///   · 씬 <c>RomMarkerScene</c>의 오브젝트 <c>PointableCanvasModule</c>에 EventSystem과 PointableCanvasModule이
///     둘 다 붙어 있고, 손에는 <c>HandPokeInteractorFist/Palm</c>이 있다(09-21 실측). 그래서 판이 눌린다.
///   · 판 프리팹의 Surface에 <c>RectTransformBoundsClipperDriver</c>가 있어
///     <b>판 크기를 바꾸면 Poke 범위가 저절로 따라온다</b>(OnRectTransformDimensionsChange → BoundsClipper.Size).
///   · PlaneSurface의 facing은 0 = Backward(-Z)다. 캔버스는 +Z가 눈 반대쪽을 보게 세우므로
///     손가락이 <b>보는 쪽에서</b> 들어오는 게 맞다 — 방향이 맞물려 있다.
///   · ★프리팹 기본값은 <c>MinThresholds.Enabled=0</c>·<c>RecoilAssist.Enabled=0</c>이다.
///     둘 다 꺼져 있으면 손가락이 표면 근처에서 떨 때 select/unselect가 되풀이돼 <b>연타로 들어온다.</b>
///     그래서 <see cref="Build"/>에서 켠다(값은 아래 공개 필드).
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
        public bool isText;        // 누를 수 없는 글자 칸
        // ★탭 판(2026-09-22)
        public string key;         // SetText로 찾는 이름(글자 칸)
        public RawImage icon;      // 버튼 위쪽 그림 — 없으면 꺼 둔다
        public TextMeshProUGUI dots;   // 버튼 아래 진행 점
    }

    // ── 모양(캔버스 픽셀. 캔버스 스케일 0.0005라 1px = 0.5mm) ──────────
    // ★본체(RomRecordSession)가 cellPx·rowPx·buttonPx·labelPt를 인스펙터 값으로 덮어쓴다.
    //   기본값은 Quad 판의 씬 값(칸 4.9cm · 줄 4.1cm · 버튼 3.4cm)을 px로 옮긴 것이다.
    public float cellPx = 98f;       // 칸 너비 — 4.9cm
    public float rowPx = 82f;        // 줄 높이 — 4.1cm
    public float buttonPx = 68f;     // 버튼 높이 — 3.4cm
    public float labelPt = 15f;      // 버튼 글자(Meta 표준 14)
    public float headerPt = 15f;
    public float padPx = 12f;        // 판 안쪽 여백 — Quad 판의 padding 0.006m과 같다
    public float gapPx = 8f;         // 버튼 사이 틈(칸 폭에서 뺀다) — Quad 판의 buttonGap 0.004m과 같다
    public float labelPadPx = 10f;   // 글자와 버튼 모서리 사이

    // ── 색 ───────────────────────────────────────────────────────────
    // ★애니메이터를 껐으므로 여기 값이 그대로 화면에 나온다(전에는 Normal 클립이 덮어썼다).
    public Color labelColor = new Color(1f, 1f, 1f, 0.902f);          // Meta 표준 글자색
    public Color selectedLabelColor = new Color(0.153f, 0.153f, 0.153f, 1f);
    public Color captionColor = new Color(1f, 1f, 1f, 0.698f);        // 누를 수 없는 글자 칸
    public Color fillColor = new Color(1f, 1f, 1f, 0.92f);            // 길게 누르기 막대
    public float selectedMix = 0.35f;                                  // 고른 버튼을 흰색 쪽으로 섞는 정도

    // ── 자리 ─────────────────────────────────────────────────────────
    public float lift = 0.06f;
    public float towardEye = 0.02f;
    public float followSharpness = 14f;
    public float grace = 1.5f;

    // ── 연타 막기 ────────────────────────────────────────────────────
    public float cooldown = 0.5f;        // 어떤 버튼이든 누른 뒤 이만큼은 안 받는다(Quad 판과 같은 값)
    public float samePressGap = 0.7f;    // ★같은 버튼을 다시 받기까지. 손 떨림으로 두세 번 들어오는 것을 막는다
    public float repeatGap = 0.1f;       // 반복 입력 사이 최소 간격

    // ── Poke 판정(ISDK) ──────────────────────────────────────────────
    // ★0을 넣으면 그 보정을 끈다(= 프리팹 기본값 그대로).
    public float pokeMinApproach = 0.01f;   // 이만큼 앞에서 다가와야 새로 누를 수 있다(판이 손 위로 떠서 눌리는 것 방지)
    public float pokeRecoilExit = 0.02f;    // 가장 깊이 누른 지점에서 이만큼 빼야 뗀 것으로 본다
    public float pokeRecoilReEnter = 0.02f; // 뗀 뒤 다시 누르려면 이만큼 더 들어와야 한다(이 둘이 연타를 막는 이력이다)
    public bool holdStillWhilePressed = true;   // 누르는 동안은 판을 돌리지 않는다(누르는 중에 표면이 움직이면 판정이 흔들린다)

    private const string PrefabDir = "RomRecordUI/";

    private Transform root;
    private GameObject plate;
    private RectTransform canvasRoot;
    private RectTransform backdrop;     // UIBackplate — 둥근 배경판(고정 크기라 늘어붙게 묶는다)
    private RectTransform pokeArea;     // ISDK_PokeInteraction — Poke 표면의 범위
    private Oculus.Interaction.PokeInteractable poke;
    private TextMeshProUGUI header;
    private readonly List<Btn> pool = new List<Btn>();
    private int activeCount;
    // ★버튼 프리팹은 하나만 쓴다 — Meta의 Primary/Secondary/Destructive 차이는 사실상 배경색이라
    //   색은 tint로 준다(Resources 폴더는 통째로 빌드에 들어가므로 안 쓰는 것을 두지 않는다).
    private GameObject primaryPrefab;
    private TMP_FontAsset korean;
    private string displayName;
    private bool placedOnce;
    private bool warnedShape;
    private float lastValidTime = -99f;
    private float cooldownUntil;
    private string lastPressedId;
    private float lastPressedAt = -99f;
    private float panelW, panelH;          // 월드 크기(m)
    private string pendingId;
    private bool pendingRepeat;

    public bool Visible => root != null && root.gameObject.activeSelf;
    public bool LastRepeat { get; private set; }

    // ── 공간 고정 모드(2026-09-21) — 종전 Quad 판과 같은 얼개다 ──────
    public bool FixedInSpace { get; set; }
    public bool Placed => placedOnce;
    public Vector3 Position => root != null ? root.position : Vector3.zero;

    public void PlaceAt(Vector3 pos, Transform eye)
    {
        if (root == null || eye == null) return;
        if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
        root.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - eye.position, Vector3.up));
        placedOnce = true;
        lastValidTime = Time.unscaledTime;
    }

    public void MoveTo(Vector3 pos)
    {
        if (root != null) root.position = pos;
    }

    /// <summary>판을 눈 쪽으로 돌린다. ★누르는 중에는 돌리지 않는다 — 표면이 움직이면 Poke 깊이가 흔들린다.</summary>
    private void FaceEye(Transform eye)
    {
        if (root == null || eye == null || !placedOnce) return;
        if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
        if (holdStillWhilePressed && poke != null &&
            poke.State == Oculus.Interaction.InteractableState.Select) return;
        Vector3 away = root.position - eye.position;
        if (away.sqrMagnitude < 1e-4f) return;
        float k = 1f - Mathf.Exp(-followSharpness * 0.35f * Time.unscaledDeltaTime);
        root.rotation = Quaternion.Slerp(root.rotation, Quaternion.LookRotation(away, Vector3.up), k);
    }

    /// <summary>
    /// 버튼이 눌렸다고 알려 온다(<see cref="RomMenuButtonUI"/>가 부른다).
    /// ★막는 문이 셋이다 — ①전체 쿨다운 ②같은 버튼 되풀이 ③반복 입력 간격.
    ///   ISDK가 같은 누름을 여러 번 보낼 수 있어서(표면 근처 떨림) 받는 쪽에서도 막는다.
    /// </summary>
    public void OnPressed(string id, bool repeat)
    {
        float now = Time.unscaledTime;
        if (now < cooldownUntil) return;
        if (repeat)
        {
            if (now - lastPressedAt < repeatGap) return;
        }
        else if (id != null && id == lastPressedId && now - lastPressedAt < samePressGap)
        {
            return;
        }

        pendingId = id;
        pendingRepeat = repeat;
        lastPressedId = id;
        lastPressedAt = now;
        if (!repeat) cooldownUntil = now + cooldown;
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

        Canvas canvas = plate.GetComponentInChildren<Canvas>(true);
        if (canvas == null)
        {
            Debug.LogWarning($"[실측기록] ★{displayName} — 판 프리팹에 Canvas가 없다. 버튼을 못 붙인다.");
            return;
        }
        canvasRoot = canvas.transform as RectTransform;

        // ★판 쪽 레이아웃 그룹도 전부 끈다. CanvasRoot의 HorizontalLayoutGroup(간격 50px)을 켜 두면
        //   우리가 정한 칸 자리를 통째로 무시한다(09-21 프리팹 실측).
        DisableLayout(plate.transform);

        // ★UIThemeManager는 <c>_themes</c>가 빈 배열이라 Start에서 "Theme index out of range" 에러만 남긴다(실측).
        //   색은 우리가 정하므로 아예 끈다 — 컴포넌트를 끄면 Start가 돌지 않는다.
        var theme = plate.GetComponent<Oculus.Interaction.UIThemeManager>();
        if (theme != null) theme.enabled = false;

        // ★배경과 Poke 범위를 캔버스에 늘어붙게 만든다(계층은 CanvasRoot > UIBackplate / ISDK_PokeInteraction > Surface,
        //   배경은 500x500 고정이었다). 레이아웃 그룹을 껐으니 묶어 두지 않으면 판 크기를 바꿔도 배경이 안 따라온다.
        backdrop = FindDeep(plate.transform, "UIBackplate") as RectTransform;
        pokeArea = FindDeep(plate.transform, "ISDK_PokeInteraction") as RectTransform;
        if (backdrop == null) Debug.LogWarning($"[실측기록] ★{displayName} — 판 배경(UIBackplate)을 못 찾았다. 배경이 안 따라온다.");
        Stretch(backdrop);
        Stretch(pokeArea);

        SetupPoke();

        header = MakeLabel(canvasRoot, headerPt, TextAlignmentOptions.MidlineLeft, "머리줄");

        for (int i = 0; i < capacity; i++) pool.Add(MakeButton());

        // ★조용히 안 눌리는 두 가지를 미리 잡는다.
        if (Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            Debug.LogWarning("[실측기록] ★씬에 EventSystem이 없다 — Meta UI 버튼이 조용히 안 눌린다.");
        if (Object.FindAnyObjectByType<Oculus.Interaction.PointableCanvasModule>() == null)
            Debug.LogWarning("[실측기록] ★씬에 PointableCanvasModule이 없다 — 손가락 Poke가 uGUI로 전달되지 않는다.");

        root.gameObject.SetActive(false);
        Debug.Log($"[실측기록] {displayName} — Meta UI 판을 만들었다(칸 {cellPx}×{rowPx}px · 버튼 높이 {buttonPx}px · " +
                  $"글자 {labelPt}pt · 버튼 {pool.Count}개 · Poke 보정 {(poke != null ? "켬" : "없음")})");
    }

    /// <summary>
    /// Poke 판정에 이력(hysteresis)을 준다. ★프리팹 기본값은 둘 다 꺼져 있어
    /// 표면 근처에서 손이 떨면 누름이 되풀이된다 — 그게 "다다다 눌린다"의 구조적 원인이다.
    /// </summary>
    private void SetupPoke()
    {
        poke = plate.GetComponentInChildren<Oculus.Interaction.PokeInteractable>(true);
        if (poke == null)
        {
            Debug.LogWarning($"[실측기록] ★{displayName} — 판에 PokeInteractable이 없다. 손가락으로 못 누른다.");
            return;
        }

        var min = poke.MinThresholds;
        min.Enabled = pokeMinApproach > 0f;
        if (min.Enabled) min.MinNormal = pokeMinApproach;
        poke.MinThresholds = min;

        var recoil = poke.RecoilAssist;
        recoil.Enabled = pokeRecoilExit > 0f;
        if (recoil.Enabled)
        {
            recoil.ExitDistance = pokeRecoilExit;
            recoil.ReEnterDistance = pokeRecoilReEnter;
        }
        poke.RecoilAssist = recoil;
    }

    private Btn MakeButton()
    {
        var go = Object.Instantiate(primaryPrefab, canvasRoot);
        var b = new Btn { go = go, rt = go.transform as RectTransform };

        // ★① 프리팹 안의 레이아웃을 전부 끈다 — 이것이 "크기가 들쭉날쭉"의 진짜 원인이었다.
        //   루트·Content HLG가 ChildControlHeight=0이라 Background 높이가 40px에 못 박혀 있었다.
        DisableLayout(go.transform);

        // ★② 애니메이터를 끈다 — Normal 클립이 Background 색을 매 프레임 덮어쓴다.
        var anim = go.GetComponent<Animator>();
        if (anim != null) anim.enabled = false;

        // ★③ 속을 루트에 늘어붙게 만든다. 이걸로 루트 sizeDelta 하나가 보이는 크기이자 누름 영역이 된다.
        Transform content = FindDeep(go.transform, "Content");
        Transform background = FindDeep(go.transform, "Background");
        Transform elements = FindDeep(go.transform, "Elements");
        Transform text = FindDeep(go.transform, "Text");
        b.background = background != null ? background.GetComponent<Image>() : null;
        b.label = FindText(go.transform, "Label");
        if ((content == null || background == null || elements == null || text == null || b.label == null) && !warnedShape)
        {
            warnedShape = true;
            Debug.LogWarning($"[실측기록] ★{displayName} — 버튼 프리팹 속이 바뀌었다(Content·Background·Elements·Text·Label 중 없는 것이 있다). " +
                             "크기가 다시 들쭉날쭉해진다.");
        }
        Stretch(content as RectTransform);
        Stretch(background as RectTransform);
        StretchInset(elements as RectTransform, labelPadPx, 0f);
        Stretch(text as RectTransform);
        if (b.label != null) Stretch(b.label.rectTransform);
        b.rt.localScale = Vector3.one;

        // 아이콘·틈·부제는 안 쓴다 — 라벨만 가운데 남긴다. ★Gap(8px)을 살려 두면 글자가 오른쪽으로 밀린다.
        Deactivate(FindDeep(go.transform, "Icon"));
        Deactivate(FindDeep(go.transform, "Gap"));
        Deactivate(FindDeep(go.transform, "Subtitle"));

        if (b.label != null)
        {
            if (korean != null) b.label.font = korean;
            b.label.fontSize = labelPt;
            b.label.alignment = TextAlignmentOptions.Center;
            b.label.enableAutoSizing = false;
            b.label.textWrappingMode = TextWrappingModes.NoWrap;
            b.label.overflowMode = TextOverflowModes.Overflow;   // 잘라 내지 않는다 — 넘치면 눈에 보여야 고칠 수 있다
            b.label.margin = Vector4.zero;
            b.label.raycastTarget = false;
            b.label.color = labelColor;
        }

        // 길게 누르기 막대 — 레이아웃에 끼지 않게 무시 표시를 준다(레이아웃을 껐어도 남겨 둔다).
        var fillGo = new GameObject("차오름", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
        var fillRt = (RectTransform)fillGo.transform;
        fillRt.SetParent(b.rt, false);
        fillRt.anchorMin = new Vector2(0f, 0f);
        fillRt.anchorMax = new Vector2(1f, 0.12f);
        fillRt.offsetMin = new Vector2(4f, 3f);
        fillRt.offsetMax = new Vector2(-4f, 0f);
        fillGo.GetComponent<LayoutElement>().ignoreLayout = true;
        b.fill = fillGo.GetComponent<Image>();
        b.fill.color = fillColor;
        b.fill.type = Image.Type.Filled;
        b.fill.fillMethod = Image.FillMethod.Horizontal;
        b.fill.raycastTarget = false;
        fillGo.SetActive(false);

        // ★탭 그림(2026-09-22). 루트의 마지막 자식이라 배경 위에 그려진다. 누름은 배경이 받는다.
        var iconGo = new GameObject("아이콘", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage), typeof(LayoutElement));
        var iconRt = (RectTransform)iconGo.transform;
        iconRt.SetParent(b.rt, false);
        iconRt.anchorMin = iconRt.anchorMax = new Vector2(0.5f, 1f);
        iconRt.pivot = new Vector2(0.5f, 1f);
        iconGo.GetComponent<LayoutElement>().ignoreLayout = true;
        b.icon = iconGo.GetComponent<RawImage>();
        b.icon.raycastTarget = false;
        iconGo.SetActive(false);

        // ★진행 점(2026-09-22) — 버튼 <b>밖</b> 아래에 붙는다. 자리는 배치하는 쪽이 비워 둔다.
        b.dots = MakeLabel(b.rt, labelPt, TextAlignmentOptions.Top, "진행점");
        var dRt = b.dots.rectTransform;
        dRt.anchorMin = dRt.anchorMax = new Vector2(0.5f, 0f);
        dRt.pivot = new Vector2(0.5f, 1f);
        b.dots.richText = true;
        b.dots.gameObject.SetActive(false);

        b.press = go.AddComponent<RomMenuButtonUI>();
        b.press.owner = this;
        b.press.Setup(b.background, b.fill);

        go.SetActive(false);
        return b;
    }

    public void SetText(string key, string text)
    {
        if (key == null) return;
        for (int i = 0; i < activeCount; i++)
        {
            Btn b = pool[i];
            if (b.key != key || b.label == null) continue;
            if (b.label.text != text) b.label.text = text;
            return;
        }
    }

    public void SetLayout(string headerText, RomMenuItem[] items)
    {
        if (canvasRoot == null) return;
        cooldownUntil = Time.unscaledTime + cooldown;
        pendingId = null;              // ★앞 배치에서 들어온 눌림을 새 배치로 넘기지 않는다
        activeCount = Mathf.Min(items.Length, pool.Count);

        // ★판 폭을 내용에 맞춘다 — 빈 칸이 남지 않게(09-21 "빈 공간이 많다").
        // ★판 높이는 <b>가장 아래 칸의 바닥</b>이 정한다(09-22) — 탭처럼 여러 줄짜리 칸이 생겼다.
        float maxCol = 1f, maxBottom = 1f;
        for (int i = 0; i < activeCount; i++)
        {
            maxCol = Mathf.Max(maxCol, items[i].col + items[i].span);
            maxBottom = Mathf.Max(maxBottom, items[i].row + RowsOf(items[i]));
        }
        float wPx = maxCol * cellPx + padPx * 2f;
        float hPx = maxBottom * rowPx + padPx * 2f;
        Resize(wPx, hPx);

        header.text = headerText;
        header.fontSize = headerPt;
        header.rectTransform.anchoredPosition = new Vector2(padPx, -padPx);
        header.rectTransform.sizeDelta = new Vector2(wPx - padPx * 2f, rowPx);

        for (int i = 0; i < pool.Count; i++)
        {
            Btn b = pool[i];
            bool on = i < activeCount;
            // ★배치가 바뀌는 순간 눌려 있던 상태를 푼다. 안 풀면 손가락을 댄 채 단계가 넘어갈 때
            //   그 슬롯의 새 버튼이 <b>누른 적도 없는데</b> 반복·길게누르기로 실행된다(나가기가 그 자리에 온다).
            b.press.OnPointerUp(null);
            if (b.go.activeSelf != on) b.go.SetActive(on);
            if (!on) { b.press.id = null; continue; }

            RomMenuItem it = items[i];
            b.isText = it.id == null;
            b.key = it.key;
            // ★잠긴 탭은 id를 비워 둔다 — 눌림이 들어와도 OnPressed에서 null이라 아무 일도 안 난다.
            bool pressable = !b.isText && !it.locked;
            b.press.id = pressable ? it.id : null;
            b.press.repeat = it.repeat;
            b.press.holdSeconds = it.holdSeconds;

            float rows = RowsOf(it);
            float w = it.span * cellPx - gapPx;
            // ★여러 줄짜리 버튼은 줄 사이 틈(rowPx - buttonPx)을 한 번만 뺀다 — 한 줄이면 종전과 같다.
            float h = b.isText ? rows * rowPx : buttonPx + (rows - 1f) * rowPx;
            b.rt.anchorMin = b.rt.anchorMax = new Vector2(0f, 1f);
            b.rt.pivot = new Vector2(0f, 1f);
            b.rt.sizeDelta = new Vector2(w, h);
            // 칸 안에서 위아래 가운데에 둔다(칸 높이 rows*rowPx, 버튼 높이 h).
            b.rt.anchoredPosition = new Vector2(padPx + it.col * cellPx,
                                                -(padPx + it.row * rowPx + (rows * rowPx - h) * 0.5f));

            bool hasIcon = it.icon != null && !b.isText;
            if (b.label != null)
            {
                b.label.text = it.label;
                b.label.fontSize = labelPt * (it.textScale > 0f ? it.textScale : 1f);
                b.label.fontStyle = it.selected ? FontStyles.Bold : FontStyles.Normal;
                // ★그림이 있으면 글자를 아래로 내린다. 글자 칸은 왼쪽 정렬을 고를 수 있다(값 여러 줄).
                b.label.alignment = hasIcon ? TextAlignmentOptions.Bottom
                                  : it.alignLeft ? TextAlignmentOptions.MidlineLeft
                                  : TextAlignmentOptions.Center;
                b.label.margin = hasIcon ? new Vector4(0f, 0f, 0f, 6f) : Vector4.zero;
                b.label.richText = b.isText;   // 값 표시에서 지금 단계만 밝힌다
                Color lc = b.isText ? (it.alignLeft ? labelColor : captionColor)
                         : it.selected ? selectedLabelColor : labelColor;
                if (it.locked) lc.a *= 0.35f;
                b.label.color = lc;
            }
            if (b.background != null)
            {
                // 글자 칸은 배경을 지운다 — 누를 수 없는 것이 버튼처럼 보이면 안 된다.
                Color bg = b.isText ? new Color(0f, 0f, 0f, 0f)
                         : it.selected ? Color.Lerp(it.tint, Color.white, selectedMix)
                         : it.tint;
                if (it.locked) bg.a *= 0.45f;
                b.background.color = bg;
                b.background.raycastTarget = pressable;
            }
            b.press.enabled = pressable;

            if (b.icon != null)
            {
                if (b.icon.gameObject.activeSelf != hasIcon) b.icon.gameObject.SetActive(hasIcon);
                if (hasIcon)
                {
                    // 그림 칸 = 버튼 높이에서 글자 한 줄을 뺀 정사각형
                    float labelBand = labelPt * 1.6f;
                    float s = Mathf.Max(8f, Mathf.Min(w - 12f, h - labelBand - 10f));
                    b.icon.texture = it.icon;
                    b.icon.rectTransform.sizeDelta = new Vector2(s, s);
                    b.icon.rectTransform.anchoredPosition = new Vector2(0f, -6f);
                    // ★고른 탭은 흰 바탕이라 그림을 어둡게, 잠긴 탭은 흐리게.
                    b.icon.color = it.selected ? new Color(0.153f, 0.153f, 0.153f, 1f)
                                 : it.locked ? new Color(1f, 1f, 1f, 0.3f)
                                 : new Color(1f, 1f, 1f, 0.9f);
                }
            }
            if (b.dots != null)
            {
                bool hasDots = !string.IsNullOrEmpty(it.dots);
                if (b.dots.gameObject.activeSelf != hasDots) b.dots.gameObject.SetActive(hasDots);
                if (hasDots)
                {
                    b.dots.text = it.dots;
                    b.dots.fontSize = labelPt * 0.9f;
                    b.dots.rectTransform.sizeDelta = new Vector2(w, labelPt * 1.4f);
                    b.dots.rectTransform.anchoredPosition = new Vector2(0f, -2f);
                }
            }
            b.press.Setup(b.background, b.fill);   // ★색을 정한 뒤에 부른다 — 여기서 되돌릴 색을 기억한다
            if (b.fill != null) b.fill.gameObject.SetActive(false);
        }
    }

    private static float RowsOf(RomMenuItem it) => it.rows > 0f ? it.rows : 1f;

    /// <summary>부모에 늘어붙게 만든다 — 판·버튼 크기를 바꾸면 따라오게.</summary>
    private static void Stretch(RectTransform r) => StretchInset(r, 0f, 0f);

    /// <summary>부모에 늘어붙이되 좌우·위아래로 그만큼 안쪽에 둔다.</summary>
    private static void StretchInset(RectTransform r, float x, float y)
    {
        if (r == null) return;
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = new Vector2(x, y);
        r.offsetMax = new Vector2(-x, -y);
    }

    /// <summary>레이아웃 그룹·크기 맞춤을 전부 끈다. ★켜 두면 우리가 준 크기를 다시 계산해 덮는다.</summary>
    private static void DisableLayout(Transform t)
    {
        var groups = t.GetComponentsInChildren<LayoutGroup>(true);
        for (int i = 0; i < groups.Length; i++) groups[i].enabled = false;
        var fitters = t.GetComponentsInChildren<ContentSizeFitter>(true);
        for (int i = 0; i < fitters.Length; i++) fitters[i].enabled = false;
    }

    private static void Deactivate(Transform t)
    {
        if (t != null) t.gameObject.SetActive(false);
    }

    /// <summary>판 크기를 바꾼다. 월드 크기도 같이 기억해 둔다(Near·Distance가 쓴다).</summary>
    private void Resize(float wPx, float hPx)
    {
        canvasRoot.sizeDelta = new Vector2(wPx, hPx);
        // 배경·Poke 범위는 Build에서 늘어붙게 묶어 뒀고, Poke 범위(BoundsClipper)는
        // RectTransformBoundsClipperDriver가 크기 변화를 받아 스스로 맞춘다(패키지 소스 실측).
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
        if (FixedInSpace) { FaceEye(eye); return; }   // ★고정 모드에서는 손목을 따라가지 않는다
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
        t.color = labelColor;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.raycastTarget = false;
        var le = go.AddComponent<LayoutElement>();
        le.ignoreLayout = true;
        return t;
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
