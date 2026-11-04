using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로딩 가림막 (2026-10-30 사용자 «생성될 때 딜레이 — 이런 거 다 잡으라고 로딩창 해 놓은 것 · 로딩창 디자인이랑 로딩 돌아가는 것도 수정»).
/// 씬 밖(DontDestroyOnLoad · SceneLoader 자식)에 살아 로딩 씬 → 목표 씬으로 넘어가도 계속 덮고, 목표 씬이 자리를 다 잡으면(SceneReadyGate) 걷힌다.
///   가림막 = 머리를 감싼 구(안쪽 단색 · ZTest Always · Queue 4500) · 가운데 판 = 로고 · 제목 · 안내 · 도는 점 8개 · 진행 막대(전부 ZTest Always 셰이더 · Queue 4600).
///   판은 머리 정면 1m에 부드럽게 따라온다(가림막 안에선 따라오는 게 맞다 — 씬 UI가 아니다).
/// 에셋은 Resources/LoadingOverlay/LoadingOverlayConfig에서 읽는다(없으면 글자 기본 글꼴 · 가림막만).
/// ★Update에서 할당하지 않는다 — 점 알파 · 막대 폭 · 자리만 바꾼다.
/// </summary>
public class LoadingOverlay : MonoBehaviour
{
    private const float PanelDistance = 1.0f, PanelW = 560f, PanelH = 300f, PxToM = 0.001f;
    private const int Dots = 8;
    private const float SpinPeriod = 1.0f, FadeSeconds = 0.35f, FollowSpeed = 6f;
    private const int VeilQueue = 4500, PanelQueue = 4600;

    private LoadingOverlayConfig cfg;
    private Transform veil, panel;
    private Material veilMat;
    private CanvasGroup group;
    private RectTransform barFill;
    private Image[] dots;
    private TMP_Text title, detail;
    private float shownProgress, targetProgress;
    private float fade = 1f;       // 1 = 다 덮음
    private bool hiding;
    private Transform cam;

    public bool IsVisible => gameObject.activeSelf;

    /// <summary>가림막이 덮고 있다(Show ~ 다 걷힘). 10-30 사용자 «로딩 창 사라지기 전부터 음성 안내가 시작된다» — 나레이션이 이걸 기다린다.</summary>
    public static bool Covering { get; private set; }

    public static LoadingOverlay Create(Transform parent)
    {
        var go = new GameObject("[LoadingOverlay]");
        go.transform.SetParent(parent, false);
        var o = go.AddComponent<LoadingOverlay>();
        o.Build();
        go.SetActive(false);
        return o;
    }

    public void Show(string titleText, string detailText)
    {
        hiding = false; fade = 1f; shownProgress = targetProgress = 0f;
        if (title != null) title.text = titleText;
        if (detail != null) detail.text = detailText;
        gameObject.SetActive(true);
        Covering = true;
        cam = null;
        SnapToCamera();
        ApplyFade();
        Debug.Log($"[LoadingOverlay] 덮음 — «{titleText}»");
    }

    public void SetProgress(float p) => targetProgress = Mathf.Max(targetProgress, Mathf.Clamp01(p));
    public void SetDetail(string s) { if (detail != null) detail.text = s; }

    public void Hide()
    {
        if (!gameObject.activeSelf || hiding) return;
        targetProgress = 1f; hiding = true;
        Debug.Log($"[LoadingOverlay] 걷기 시작 · {Time.timeSinceLevelLoad:F2}s");
    }

    private void LateUpdate()
    {
        if (cam == null && !FindCamera()) return;
        // 가림막 = 머리 위치 그대로 · 판 = 머리 정면(수평) 1m로 부드럽게
        veil.position = cam.position;
        Vector3 f = cam.forward; f.y = 0f; if (f.sqrMagnitude < 1e-6f) f = Vector3.forward; f.Normalize();
        Vector3 target = cam.position + f * PanelDistance;
        float k = 1f - Mathf.Exp(-FollowSpeed * Time.unscaledDeltaTime);
        panel.position = Vector3.Lerp(panel.position, target, k);
        panel.rotation = Quaternion.LookRotation(panel.position - cam.position, Vector3.up);

        // 도는 점 — 맨 앞 점이 가장 진하고 뒤로 갈수록 옅다
        float head = (Time.unscaledTime / SpinPeriod) % 1f * Dots;
        for (int i = 0; i < Dots; i++)
        {
            float behind = (head - i + Dots) % Dots;
            var c = dots[i].color; c.a = Mathf.Lerp(1f, 0.15f, behind / Dots); dots[i].color = c;
        }
        // 진행 막대 — 실제 진행으로 부드럽게
        shownProgress = Mathf.MoveTowards(shownProgress, targetProgress, Time.unscaledDeltaTime * (hiding ? 3f : 1.2f));
        barFill.sizeDelta = new Vector2(Mathf.Max(barFill.sizeDelta.y, BarW * shownProgress), barFill.sizeDelta.y);

        if (hiding && shownProgress >= 0.999f)
        {
            fade = Mathf.MoveTowards(fade, 0f, Time.unscaledDeltaTime / FadeSeconds);
            ApplyFade();
            if (fade <= 0f) { hiding = false; gameObject.SetActive(false); Debug.Log($"[LoadingOverlay] 걷힘 · {Time.timeSinceLevelLoad:F2}s"); }
        }
    }

    // 꺼지거나 없어지면 덮지 않는 것 — 표시가 남아 나레이션이 영영 기다리지 않게
    private void OnDisable() => Covering = false;

    private bool FindCamera()
    {
        var main = Camera.main;
        if (main != null) cam = main.transform;
        else { var eye = GameObject.Find("CenterEyeAnchor"); if (eye != null) cam = eye.transform; }
        return cam != null;
    }

    private void SnapToCamera()
    {
        if (!FindCamera()) return;
        Vector3 f = cam.forward; f.y = 0f; if (f.sqrMagnitude < 1e-6f) f = Vector3.forward; f.Normalize();
        veil.position = cam.position;
        panel.position = cam.position + f * PanelDistance;
        panel.rotation = Quaternion.LookRotation(panel.position - cam.position, Vector3.up);
    }

    private void ApplyFade()
    {
        if (veilMat != null) { var c = cfg != null ? cfg.veilColor : new Color(0.05f, 0.05f, 0.06f, 1f); c.a *= fade; veilMat.color = c; }
        if (group != null) group.alpha = fade;
    }

    // ── 만들기(한 번) ──
    private const float BarW = 360f, BarH = 6f;

    private void Build()
    {
        cfg = Resources.Load<LoadingOverlayConfig>("LoadingOverlay/LoadingOverlayConfig");

        // 가림막 구
        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "가림막";
        Destroy(sphere.GetComponent<Collider>());
        sphere.transform.SetParent(transform, false);
        sphere.transform.localScale = Vector3.one * 0.6f;   // 반지름 0.3m — 눈 바로 앞(가까운 판 0.08m 등도 덮게 ZTest Always)
        veil = sphere.transform;
        var mr = sphere.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
        var vs = cfg != null && cfg.veilShader != null ? cfg.veilShader : Shader.Find("GuideChuna/LoadingVeil");
        veilMat = new Material(vs) { renderQueue = VeilQueue };
        mr.sharedMaterial = veilMat;

        // 가운데 판
        var p = new GameObject("판", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
        p.transform.SetParent(transform, false);
        panel = p.transform;
        var canvas = p.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
        var prt = (RectTransform)p.transform; prt.sizeDelta = new Vector2(PanelW, PanelH); prt.localScale = Vector3.one * PxToM;
        group = p.GetComponent<CanvasGroup>(); group.interactable = false; group.blocksRaycasts = false;

        var bg = Box(prt, "바탕", Vector2.zero, new Vector2(PanelW, PanelH), cfg != null ? cfg.panelColor : new Color(0.11f, 0.11f, 0.12f, 1f), 24f);
        bg.anchorMin = bg.anchorMax = new Vector2(0.5f, 0.5f);

        // 로고 · 제목 · 안내
        if (cfg != null && cfg.logo != null)
        {
            var logo = new GameObject("로고", typeof(RectTransform), typeof(Image));
            logo.transform.SetParent(prt, false);
            var img = logo.GetComponent<Image>(); img.sprite = cfg.logo; img.preserveAspect = true; img.raycastTarget = false;
            var lrt = (RectTransform)logo.transform; lrt.sizeDelta = new Vector2(180f, 40f); lrt.anchoredPosition = new Vector2(0f, 96f);
        }
        title = Text(prt, "제목", new Vector2(0f, 40f), new Vector2(PanelW - 48f, 36f), 24f, cfg != null ? cfg.titleFont : null, Color.white);
        detail = Text(prt, "안내", new Vector2(0f, 6f), new Vector2(PanelW - 48f, 24f), 14f, cfg != null ? cfg.bodyFont : null, new Color(1f, 1f, 1f, 0.6f));

        // 도는 점 8개(지름 10 · 반지름 22 원)
        var spin = new GameObject("도는 점", typeof(RectTransform)).transform as RectTransform;
        spin.SetParent(prt, false); spin.anchoredPosition = new Vector2(0f, -50f);
        dots = new Image[Dots];
        for (int i = 0; i < Dots; i++)
        {
            float a = Mathf.PI / 2f - i * Mathf.PI * 2f / Dots;   // 12시에서 시계 방향
            var d = Box(spin, "점 " + i, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 22f, new Vector2(10f, 10f), Color.white, 5f);
            dots[i] = d.GetComponent<Image>();
        }

        // 진행 막대
        var track = Box(prt, "막대 바탕", new Vector2(0f, -106f), new Vector2(BarW, BarH), new Color(1f, 1f, 1f, 0.12f), BarH / 2f);
        barFill = Box(track, "막대", Vector2.zero, new Vector2(BarH, BarH), cfg != null ? cfg.accentColor : new Color(0f, 0.39f, 0.88f, 1f), BarH / 2f);
        barFill.anchorMin = barFill.anchorMax = barFill.pivot = new Vector2(0f, 0.5f);
        barFill.anchoredPosition = Vector2.zero;
    }

    private RectTransform Box(Transform parent, string n, Vector2 pos, Vector2 size, Color color, float radius)
    {
        var go = new GameObject(n, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform; rt.sizeDelta = size; rt.anchoredPosition = pos;
        var img = go.GetComponent<Image>(); img.color = color; img.raycastTarget = false;
        if (cfg != null && cfg.roundedBoxMaterial != null && cfg.roundedBoxOnTop != null)
        {
            img.material = new Material(cfg.roundedBoxMaterial) { shader = cfg.roundedBoxOnTop, renderQueue = PanelQueue };
            go.AddComponent<RoundedBoxUIProperties>().borderRadius = Vector4.one * radius;
        }
        return rt;
    }

    private TMP_Text Text(Transform parent, string n, Vector2 pos, Vector2 size, float fontSize, TMP_FontAsset font, Color color)
    {
        var go = new GameObject(n, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform; rt.sizeDelta = size; rt.anchoredPosition = pos;
        var t = go.GetComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = fontSize; t.color = color; t.alignment = TextAlignmentOptions.Center; t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        if (cfg != null && cfg.tmpOnTop != null) { var fm = t.fontMaterial; fm.shader = cfg.tmpOnTop; fm.renderQueue = PanelQueue + 1; }
        return t;
    }
}
