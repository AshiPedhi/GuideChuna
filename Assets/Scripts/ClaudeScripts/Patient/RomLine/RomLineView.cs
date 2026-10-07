using TMPro;
using UnityEngine;

/// <summary>
/// ★경추 ROM «선 실측» — 표시(2026-10-01 신설). 점·선·각도기, 컨트롤러 끝 이름표, 조작 판 하나.
/// 계산은 하지 않는다 — <see cref="RomLineSession"/>이 준 값만 그린다.
/// ★글자는 상태가 바뀔 때만 바꾼다(매 프레임 문자열을 만들지 않는다). 매 프레임 하는 일은 자리 옮기기뿐이다.
/// </summary>
public class RomLineView
{
    // ── 크기(m) ─────────────────────────────────────────────────────
    public const float PanelW = 0.36f, PanelH = 0.50f;   // 0.38 → 0.42(10-02): 비교 시험 두 줄 · → 0.46(10-06): 단계 버튼 줄 · → 0.50(10-07): 모드 줄
    private const float PointRadius = 0.005f;
    private const float LineWidth = 0.003f;
    private const float ArcRadius = 0.06f;
    private const float LineMargin = 0.04f;
    private const int ArcSegments = 32;
    public const float TouchDepth = 0.035f;      // 판 앞뒤 이 안에 컨트롤러 끝이 들어오면 «판을 누르는 중»으로 본다
    public const float FingerDepth = 0.02f;      // 손끝은 더 바짝 대야 한다 — 지나가는 손에 눌리지 않게(10-06)
    public const int ButtonCount = 19;

    // ── 색 ──────────────────────────────────────────────────────────
    private static readonly Color NeutralColor = new Color(0.92f, 0.92f, 0.92f, 1f);
    private static readonly Color ActiveColor = new Color(1f, 0.42f, 0.02f, 1f);      // 능동 #FF6B05
    private static readonly Color PressureColor = new Color(0.62f, 0.26f, 0.01f, 1f); // 압박 #9E4203(명도만 낮춤)
    private static readonly Color ArcColor = new Color(0.25f, 0.65f, 1f, 1f);
    private static readonly Color TipColor = new Color(1f, 0.95f, 0.2f, 1f);
    private static readonly Color TipLostColor = new Color(1f, 0.30f, 0.30f, 0.85f);   // 헤드셋이 컨트롤러를 못 보고 있다 — 자리는 추정값
    private static readonly Color PlateColor = new Color(0.07f, 0.09f, 0.12f, 0.92f);
    private static readonly Color ButtonColor = new Color(0.20f, 0.26f, 0.34f, 1f);
    private static readonly Color ButtonHot = new Color(0.30f, 0.52f, 0.85f, 1f);
    private static readonly Color ExitColor = new Color(0.55f, 0.18f, 0.18f, 1f);
    private static readonly Color TabOn = new Color(0.30f, 0.52f, 0.85f, 1f);
    private static readonly Color TabOff = new Color(0.16f, 0.20f, 0.26f, 1f);
    private static readonly Color CompareColor = new Color(0.35f, 0.80f, 1f, 1f);
    private static readonly Color CompareNeutral = new Color(0.75f, 0.92f, 1f, 1f);
    private static readonly Color ComparePressure = new Color(0.10f, 0.45f, 0.75f, 1f);
    private static readonly Color FillColor = new Color(1f, 1f, 1f, 0.55f);
    private static readonly Color FingerColor = new Color(0.55f, 0.85f, 1f, 1f);

    private static readonly string[] TabNames = { "기준", "굴곡·신전", "측굴", "회전" };
    private static readonly string[] ButtonNames = { "이 동작 다시", "전부 다시", "나가기" };
    // 컨트롤러의 스틱 · A/X · B/Y가 하던 일 — 손으로도 할 수 있게 판에 올린다(10-06)
    private static readonly string[] StepNames = { "이전 단계", "한 점 취소", "다음 단계" };
    public static readonly string[] ButtonLabel = { "기준", "굴곡·신전", "측굴", "회전", "이 동작 다시", "전부 다시", "나가기", "이전 단계", "한 점 취소", "다음 단계",
                                                   "6동작", "자유 측정", "이 동작 지우기", "3축 표시", "축 다시 잡기",   // 10~14 = 자유 측정(10-07)
                                                   "굴곡·신전", "측굴", "회전", "기준선 바꾸기" };                         // 15~17 = 자유 측정의 동작 탭 · 18

    private Transform root;
    private Material mat;
    private TMP_FontAsset font;

    // 판
    private Transform panel;
    private Transform guidedArea, freeArea;   // 모드마다 보이는 것 — 6동작(탭·결과표·단계 버튼) / 자유 측정(결과 줄·축 버튼)
    private readonly Renderer[] buttonPlates = new Renderer[ButtonCount];
    private readonly Rect[] buttonRects = new Rect[ButtonCount];
    private readonly Color[] buttonBase = new Color[ButtonCount];
    private Transform fill;
    private TextMeshPro title, instruction, note, footer;
    private readonly TextMeshPro[] compareNames = new TextMeshPro[2];    // 비교 시험 줄 이름: 굴곡 · 신전
    private readonly TextMeshPro[] compareValues = new TextMeshPro[8];   // 줄마다 능동 면·직접, 압박 면·직접
    private readonly TextMeshPro[] values = new TextMeshPro[12];        // 면 위에서 잰 값
    private readonly TextMeshPro[] valuesDirect = new TextMeshPro[12];  // 3차원에서 그대로 잰 값(비교용)
    private int hot = -1;

    // 환자 위 표시
    private readonly Transform[] spheres = new Transform[RomLineSession.PointCount];
    private readonly Transform[] extraSpheres = new Transform[3];        // 비교 시험의 Gn(중립·능동·압박)
    private readonly TextMeshPro[] neutralLabels = new TextMeshPro[2];   // 기준(양쪽 Tr) 이름표
    private LineRenderer neutralLine, activeLine, pressureLine, arc;
    private LineRenderer cmpNeutralLine, cmpActiveLine, cmpPressureLine;   // 비교 시험의 Gl ~ Gn 선
    private TextMeshPro activeText, pressureText, cmpText;
    private readonly Vector3[] arcPoints = new Vector3[ArcSegments + 1];

    // 컨트롤러 끝
    private Transform tip;
    private Renderer tipRenderer;
    private bool tipDrawnTracked = true;
    private TextMeshPro tipLabel;
    private readonly Transform[] fingers = new Transform[2];   // 판 가까이 온 검지 끝(왼·오른) — 손이 입력으로 잡혔다는 표시

    public bool PanelPlaced { get; private set; }
    public Transform Panel => panel;
    /// <summary>자유 측정의 판 글자를 붙일 자리와, 그 결과 줄이 시작하는 높이(판 좌표).</summary>
    public Transform FreeArea => freeArea;
    public float FreeTableTop { get; private set; }

    public void Build(Transform parent, TMP_FontAsset f, Material m)
    {
        font = f;
        mat = m;
        root = new GameObject("선 실측 표시").transform;
        root.SetParent(parent, false);

        // ── 점 ──
        for (int i = 0; i < spheres.Length; i++)
        {
            // 0·1 = 기준(양쪽 Tr) · 그 뒤로 동작마다 중립 2 · 능동 2 · 압박 2
            int phase = i < 2 ? 0 : ((i - 2) / 2) % 3;
            Color c = phase == 0 ? NeutralColor : phase == 1 ? ActiveColor : PressureColor;
            spheres[i] = MakeSphere("점", PointRadius, c);
            spheres[i].gameObject.SetActive(false);
        }
        for (int i = 0; i < extraSpheres.Length; i++)
        {
            extraSpheres[i] = MakeSphere("점(비교 Gn)", PointRadius, i == 0 ? NeutralColor : i == 1 ? ActiveColor : PressureColor);
            extraSpheres[i].gameObject.SetActive(false);
        }
        for (int i = 0; i < neutralLabels.Length; i++)
        {
            neutralLabels[i] = MakeText(root, RomLineSession.PointName[i], 0.07f, TextAlignmentOptions.Center, 5);
            neutralLabels[i].gameObject.SetActive(false);
        }

        neutralLine = MakeLine("중립 기준선", NeutralColor, 2);
        activeLine = MakeLine("능동 측정선", ActiveColor, 2);
        pressureLine = MakeLine("압박 측정선", PressureColor, 2);
        arc = MakeLine("각도기", ArcColor, ArcSegments + 1);
        activeText = MakeText(root, "", 0.11f, TextAlignmentOptions.Center, 5);
        pressureText = MakeText(root, "", 0.09f, TextAlignmentOptions.Center, 5);
        activeText.color = ActiveColor;
        pressureText.color = PressureColor;
        HideMeasure();

        // 비교 시험의 Gl ~ Gn 선 — Tr ~ Gl 선과 헷갈리지 않게 하늘색 계열로 그린다
        cmpNeutralLine = MakeLine("비교 중립선", CompareNeutral, 2);
        cmpActiveLine = MakeLine("비교 능동선", CompareColor, 2);
        cmpPressureLine = MakeLine("비교 압박선", ComparePressure, 2);
        cmpText = MakeText(root, "", 0.10f, TextAlignmentOptions.Center, 5);
        cmpText.color = CompareColor;
        HideCompare();

        // ── 컨트롤러 끝 ──
        tip = MakeSphere("컨트롤러 끝", PointRadius * 0.8f, TipColor);
        tipRenderer = tip.GetComponent<Renderer>();
        tipLabel = MakeText(root, "", 0.08f, TextAlignmentOptions.Left, 6);
        tipLabel.color = TipColor;
        tipLabel.rectTransform.sizeDelta = new Vector2(0.10f, 0.16f);   // 왼쪽 정렬 — 글자가 끝점 오른쪽에서 시작하게 폭을 좁힌다
        tip.gameObject.SetActive(false);
        tipLabel.gameObject.SetActive(false);
        for (int i = 0; i < fingers.Length; i++)
        {
            fingers[i] = MakeSphere("손끝", PointRadius * 0.9f, FingerColor);
            fingers[i].gameObject.SetActive(false);
        }

        BuildPanel();
        panel.gameObject.SetActive(false);
    }

    // ── 판 ──────────────────────────────────────────────────────────
    private void BuildPanel()
    {
        panel = new GameObject("조작 판").transform;
        panel.SetParent(root, false);
        MakeQuad(panel, "판", Vector2.zero, new Vector2(PanelW, PanelH), PlateColor, 0);

        guidedArea = new GameObject("6동작").transform;
        guidedArea.SetParent(panel, false);
        freeArea = new GameObject("자유 측정").transform;
        freeArea.SetParent(panel, false);

        // 맨 위 한 줄 — 모드(6동작 ↔ 자유 측정). 그 아래는 종전 배치 그대로 4cm 내려왔다.
        float modeY = PanelH * 0.5f - 0.026f;
        AddButton(panel, 10, new Vector2(-0.058f, modeY), new Vector2(0.108f, 0.032f), TabOn, ButtonLabel[10], 0.075f);
        AddButton(panel, 11, new Vector2(0.058f, modeY), new Vector2(0.108f, 0.032f), TabOff, ButtonLabel[11], 0.075f);
        float top = PanelH * 0.5f - 0.040f;

        // 진행 위치(탭) — 누르면 그 자리로 간다
        float tabW = 0.082f, tabH = 0.034f;
        for (int i = 0; i < 4; i++)
        {
            var c = new Vector2((i - 1.5f) * (tabW + 0.004f), top - 0.030f);
            AddButton(guidedArea, i, c, new Vector2(tabW, tabH), TabOff, TabNames[i], 0.075f);
        }
        // 자유 측정의 동작 탭(같은 줄) — 동작마다 점을 따로 모으고, 그 동작의 면 값만 보여 준다
        for (int i = 0; i < 3; i++)
            AddButton(freeArea, 15 + i, new Vector2((i - 1f) * 0.114f, top - 0.030f), new Vector2(0.110f, tabH), i == 0 ? TabOn : TabOff, ButtonLabel[15 + i], 0.075f);

        title = MakeText(panel, "", 0.13f, TextAlignmentOptions.Center, 3);
        Place(title, new Vector2(0f, top - 0.078f), PanelW - 0.02f);
        instruction = MakeText(panel, "", 0.115f, TextAlignmentOptions.Center, 3);
        Place(instruction, new Vector2(0f, top - 0.110f), PanelW - 0.02f);
        instruction.color = TipColor;
        note = MakeText(panel, "", 0.07f, TextAlignmentOptions.Center, 3);
        Place(note, new Vector2(0f, top - 0.137f), PanelW - 0.02f);
        note.color = new Color(1f, 0.75f, 0.45f, 1f);

        // 결과표 — 능동·압박마다 «면»(환자 기준 면 위에서 잰 값)과 «직접»(3차원에서 그대로 잰 값, 비교용)
        float tableTop = top - 0.158f, rowH = 0.019f;
        FreeTableTop = tableTop;
        const float xName = -0.125f;
        float[] xCol = { -0.050f, 0.015f, 0.085f, 0.150f };
        var dim = new Color(0.72f, 0.76f, 0.82f, 1f);
        Place(MakeText(guidedArea, "능동", 0.075f, TextAlignmentOptions.Center, 3), new Vector2((xCol[0] + xCol[1]) * 0.5f, tableTop), 0.12f).color = ActiveColor;
        Place(MakeText(guidedArea, "압박", 0.075f, TextAlignmentOptions.Center, 3), new Vector2((xCol[2] + xCol[3]) * 0.5f, tableTop), 0.12f).color = new Color(0.95f, 0.6f, 0.3f, 1f);
        for (int c = 0; c < 4; c++)
            Place(MakeText(guidedArea, c % 2 == 0 ? "면" : "직접", 0.06f, TextAlignmentOptions.Center, 3), new Vector2(xCol[c], tableTop - 0.017f), 0.06f).color = dim;
        for (int m = 0; m < 6; m++)
        {
            float y = tableTop - 0.017f - rowH * (m + 1);
            Place(MakeText(guidedArea, RomLineSession.MotionName[m], 0.08f, TextAlignmentOptions.Left, 3), new Vector2(xName, y), 0.09f);
            for (int ph = 0; ph < 2; ph++)
            {
                values[m * 2 + ph] = Place(MakeText(guidedArea, "—", 0.08f, TextAlignmentOptions.Center, 3), new Vector2(xCol[ph * 2], y), 0.06f);
                valuesDirect[m * 2 + ph] = Place(MakeText(guidedArea, "—", 0.072f, TextAlignmentOptions.Center, 3), new Vector2(xCol[ph * 2 + 1], y), 0.06f);
                valuesDirect[m * 2 + ph].color = dim;
            }
        }

        // 비교 시험 두 줄(굴곡 · 신전의 Gl ~ Gn 선) — 결과표 아래에 같은 칸 맞춤으로 붙인다. 찍어 둔 값은 단계가 넘어가도 남는다.
        //   ★10-02 첫 판은 «지금 동작» 한 줄만 보여 줘서, 굴곡을 다 찍고 신전으로 넘어가는 순간 굴곡 값이 사라졌다.
        for (int r = 0; r < 2; r++)
        {
            float cy = tableTop - 0.017f - rowH * (7 + r);
            compareNames[r] = Place(MakeText(guidedArea, RomLineSession.MotionName[r] + " Gl~Gn", 0.07f, TextAlignmentOptions.Left, 3), new Vector2(xName, cy), 0.09f);
            compareNames[r].color = CompareColor;
            for (int c = 0; c < 4; c++)
            {
                compareValues[r * 4 + c] = Place(MakeText(guidedArea, "—", c % 2 == 0 ? 0.08f : 0.072f, TextAlignmentOptions.Center, 3), new Vector2(xCol[c], cy), 0.06f);
                compareValues[r * 4 + c].color = CompareColor;
            }
            SetCompare(r, false, "", "", "", "");
        }

        // 버튼 두 줄 — 위: 단계(이전 · 한 점 취소 · 다음) / 아래: 다시 · 전부 다시 · 나가기
        //   «나가기»(6) · «한 점 취소»(8)는 두 모드가 같이 쓴다. 나머지 자리는 모드마다 다른 버튼이 선다.
        float by = -PanelH * 0.5f + 0.030f, bw = 0.108f, bh = 0.036f;
        var size = new Vector2(bw, bh);
        var up = new Vector2(0f, bh + 0.008f);
        for (int i = 0; i < 3; i++)
        {
            var c = new Vector2((i - 1f) * (bw + 0.006f), by);
            AddButton(i == 2 ? panel : guidedArea, 4 + i, c, size, i == 2 ? ExitColor : ButtonColor, ButtonNames[i], 0.075f);
            AddButton(i == 1 ? panel : guidedArea, 7 + i, c + up, size, ButtonColor, StepNames[i], 0.075f);
        }
        // 자유 측정: 아래 줄 «3축 표시» · «이 동작 지우기» / 윗줄 «축 다시 잡기» · (한 점 취소) · «기준선 바꾸기»
        AddButton(freeArea, 13, new Vector2(-(bw + 0.006f), by), size, TabOn, ButtonLabel[13], 0.075f);
        AddButton(freeArea, 12, new Vector2(0f, by), size, ButtonColor, ButtonLabel[12], 0.075f);
        AddButton(freeArea, 14, new Vector2(-(bw + 0.006f), by) + up, size, ButtonColor, ButtonLabel[14], 0.075f);
        AddButton(freeArea, 18, new Vector2(bw + 0.006f, by) + up, size, ButtonColor, ButtonLabel[18], 0.075f);
        freeArea.gameObject.SetActive(false);

        // 맨 아래 한 줄 — 모니터 화면의 주소
        footer = MakeText(panel, "", 0.052f, TextAlignmentOptions.Center, 3);
        Place(footer, new Vector2(0f, -PanelH * 0.5f + 0.0055f), PanelW - 0.02f);
        footer.color = new Color(0.60f, 0.66f, 0.72f, 1f);

        // 길게 누르기 진행(버튼 아래 가장자리에서 차오른다)
        fill = MakeQuad(panel, "차오름", Vector2.zero, new Vector2(0.01f, 0.004f), FillColor, 2);
        fill.gameObject.SetActive(false);
    }

    private void AddButton(Transform parent, int index, Vector2 center, Vector2 size, Color color, string label, float textSize)
    {
        Transform q = MakeQuad(parent, "버튼 " + label, center, size, color, 1);
        buttonPlates[index] = q.GetComponent<Renderer>();
        buttonRects[index] = new Rect(center - size * 0.5f, size);
        buttonBase[index] = color;
        Place(MakeText(parent, label, textSize, TextAlignmentOptions.Center, 3), center, size.x);
    }

    /// <summary>판을 6동작 ↔ 자유 측정으로 바꾼다. 그 모드의 버튼·글자만 보이고, 안 보이는 버튼은 눌리지 않는다.</summary>
    public void SetMode(bool free)
    {
        SetHot(-1, 0f);
        guidedArea.gameObject.SetActive(!free);
        freeArea.gameObject.SetActive(free);
        SetButtonOn(10, !free);
        SetButtonOn(11, free);
    }

    /// <summary>자유 측정의 동작 탭(0 굴곡·신전 · 1 측굴 · 2 회전) 가운데 지금 것을 밝힌다.</summary>
    public void SetFreeTab(int tab)
    {
        for (int i = 0; i < 3; i++) SetButtonOn(15 + i, i == tab);
    }

    /// <summary>켜고 끄는 버튼(모드 · 3축 표시 · 동작 탭)의 바탕색.</summary>
    public void SetButtonOn(int index, bool on)
    {
        buttonBase[index] = on ? TabOn : TabOff;
        if (index != hot) buttonPlates[index].material.color = buttonBase[index];
    }

    /// <summary>판을 눈앞 오른쪽에 세운다(처음 한 번).</summary>
    public void PlacePanel(Transform eye)
    {
        Vector3 fwd = Vector3.ProjectOnPlane(eye.forward, Vector3.up).normalized;   // 지움: 고개 숙임 → 판은 늘 수평 앞에 선다
        if (fwd.sqrMagnitude < 0.5f) fwd = Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        MovePanel(eye.position + fwd * 0.50f + right * 0.28f - Vector3.up * 0.08f, eye);
        panel.gameObject.SetActive(true);
        PanelPlaced = true;
    }

    public void MovePanel(Vector3 pos, Transform eye)
    {
        panel.position = pos;
        Vector3 away = pos - eye.position;
        if (away.sqrMagnitude > 1e-4f) panel.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
    }

    /// <summary>컨트롤러 끝이 닿은 버튼 번호(없으면 -1). <paramref name="onPanel"/> = 버튼은 아니어도 판 위에 있다.</summary>
    public int Touch(Vector3 tipPos, float depth, out bool onPanel)
    {
        onPanel = false;
        if (!PanelPlaced) return -1;
        Vector3 l = panel.InverseTransformPoint(tipPos);
        if (Mathf.Abs(l.z) > depth) return -1;
        if (Mathf.Abs(l.x) > PanelW * 0.5f + 0.01f || Mathf.Abs(l.y) > PanelH * 0.5f + 0.01f) return -1;
        onPanel = true;
        var p = new Vector2(l.x, l.y);
        for (int i = 0; i < buttonRects.Length; i++)
            if (buttonRects[i].Contains(p) && buttonPlates[i].gameObject.activeInHierarchy) return i;   // 다른 모드의 버튼은 같은 자리에 꺼져 있다
        return -1;
    }

    /// <summary>판을 잡을 수 있는 거리인가.</summary>
    public bool NearPanel(Vector3 tipPos, float margin)
    {
        if (!PanelPlaced) return false;
        Vector3 l = panel.InverseTransformPoint(tipPos);
        return Mathf.Abs(l.x) < PanelW * 0.5f + margin && Mathf.Abs(l.y) < PanelH * 0.5f + margin && Mathf.Abs(l.z) < margin;
    }

    /// <summary>닿은 버튼을 밝히고(-1이면 끔), 길게 누르기 진행(0~1)을 그린다.</summary>
    public void SetHot(int index, float hold01)
    {
        if (index != hot)
        {
            if (hot >= 0) buttonPlates[hot].material.color = hot < 4 ? tabColor[hot] : buttonBase[hot];
            if (index >= 0) buttonPlates[index].material.color = ButtonHot;
            hot = index;
        }
        bool show = index >= 0 && hold01 > 0f;
        if (fill.gameObject.activeSelf != show) fill.gameObject.SetActive(show);
        if (show)
        {
            Rect r = buttonRects[index];
            float w = r.width * Mathf.Clamp01(hold01);
            fill.localPosition = new Vector3(r.xMin + w * 0.5f, r.yMin + 0.003f, -0.003f);
            fill.localScale = new Vector3(w, 0.005f, 1f);
        }
    }

    private readonly Color[] tabColor = { TabOff, TabOff, TabOff, TabOff };

    // ── 글자(상태가 바뀔 때만 부른다) ────────────────────────────────
    public void SetTexts(string titleText, string instructionText, string noteText, int tab, string tipText)
    {
        title.text = titleText;
        instruction.text = instructionText;
        note.text = noteText;
        tipLabel.text = tipText;
        for (int i = 0; i < 4; i++)
        {
            tabColor[i] = i == tab ? TabOn : TabOff;
            if (i != hot) buttonPlates[i].material.color = tabColor[i];
        }
    }

    /// <summary>판 맨 아래 한 줄(모니터 화면의 주소).</summary>
    public void SetFooter(string text)
    {
        footer.text = text;
    }

    public void SetValue(int slot, string text, string directText, bool current)
    {
        values[slot].text = text;
        values[slot].fontStyle = current ? FontStyles.Bold : FontStyles.Normal;
        valuesDirect[slot].text = directText;
    }

    /// <summary>비교 시험 줄. row 0 = 굴곡 · 1 = 신전.</summary>
    public void SetCompare(int row, bool show, string activePlane, string activeDirect, string pressurePlane, string pressureDirect)
    {
        if (compareNames[row].gameObject.activeSelf != show) compareNames[row].gameObject.SetActive(show);
        int o = row * 4;
        for (int c = 0; c < 4; c++)
            if (compareValues[o + c].gameObject.activeSelf != show) compareValues[o + c].gameObject.SetActive(show);
        if (!show) return;
        compareValues[o].text = activePlane;
        compareValues[o + 1].text = activeDirect;
        compareValues[o + 2].text = pressurePlane;
        compareValues[o + 3].text = pressureDirect;
    }

    public void HideCompare()
    {
        cmpNeutralLine.enabled = cmpActiveLine.enabled = cmpPressureLine.enabled = false;
        cmpText.gameObject.SetActive(false);
    }

    /// <summary>비교 시험의 Gl ~ Gn 선을 찍힌 그대로(3차원) 그리고 값을 띄운다. 글자가 비면 선만 그린다.</summary>
    public void ShowCompare(bool hasN, Vector3 nA, Vector3 nB, bool hasA, Vector3 aA, Vector3 aB, bool hasP, Vector3 pA, Vector3 pB,
                            string label, Vector3 labelPos)
    {
        HideCompare();
        if (hasN) DrawRaw(cmpNeutralLine, nA, nB);
        if (hasA) DrawRaw(cmpActiveLine, aA, aB);
        if (hasP) DrawRaw(cmpPressureLine, pA, pB);
        if (label.Length == 0) return;
        cmpText.text = label;
        cmpText.transform.position = labelPos;
        cmpText.gameObject.SetActive(true);
    }

    // ── 점 ──────────────────────────────────────────────────────────
    /// <summary>비교 시험의 세 번째 점(Gn). phase 0 중립 · 1 능동 · 2 압박.</summary>
    public void SetExtraPoint(int phase, bool show, Vector3 pos)
    {
        Transform t = extraSpheres[phase];
        if (t.gameObject.activeSelf != show) t.gameObject.SetActive(show);
        if (show) t.position = pos;
    }

    public void SetPoint(int index, bool show, Vector3 pos)
    {
        if (spheres[index].gameObject.activeSelf != show) spheres[index].gameObject.SetActive(show);
        if (show) spheres[index].position = pos;
        if (index < neutralLabels.Length)
        {
            if (neutralLabels[index].gameObject.activeSelf != show) neutralLabels[index].gameObject.SetActive(show);
            if (show) neutralLabels[index].transform.position = pos + Vector3.up * 0.018f;
        }
    }

    // ── 선·각도기 ───────────────────────────────────────────────────
    public void HideMeasure()
    {
        neutralLine.enabled = activeLine.enabled = pressureLine.enabled = arc.enabled = false;
        activeText.gameObject.SetActive(false);
        pressureText.gameObject.SetActive(false);
    }

    /// <summary>중립 기준선만 그린다(아직 측정선이 없을 때) — 직선만 보인다.</summary>
    public void ShowNeutralOnly(Vector3 a, Vector3 b)
    {
        HideMeasure();
        Vector3 d = (b - a).normalized;
        DrawLine(neutralLine, a - d * LineMargin, b + d * LineMargin);
    }

    /// <summary>능동(필수)·압박(있으면)을 그린다. 각도기는 능동 때의 교점에 세운다.</summary>
    public void ShowMeasure(RomLineResult active, bool hasPressure, RomLineResult pressure, Vector3 normal, string activeLabel, string pressureLabel)
    {
        HideMeasure();
        Vector3 pivot = active.pivot;

        float nMin, nMax;
        Span(active.neutralA, active.neutralB, active.neutralDir, pivot, active.pivotOnLines, out nMin, out nMax);
        DrawLine(neutralLine, active.neutralA + active.neutralDir * nMin, active.neutralA + active.neutralDir * nMax);

        float aMin, aMax;
        Span(active.markA, active.markB, active.markDir, pivot, active.pivotOnLines, out aMin, out aMax);
        DrawLine(activeLine, active.markA + active.markDir * aMin, active.markA + active.markDir * aMax);

        // 각도기 — 점들이 있는 쪽으로 편다(교점 건너편에 펴면 선은 있는데 점이 없는 쪽에 선다).
        float side = Vector3.Dot((active.neutralA + active.neutralB) * 0.5f - pivot, active.neutralDir) >= 0f ? 1f : -1f;
        Vector3 from = active.neutralDir * side;
        for (int i = 0; i <= ArcSegments; i++)
            arcPoints[i] = pivot + Quaternion.AngleAxis(active.signedAngle * i / ArcSegments, normal) * from * ArcRadius;
        arc.positionCount = ArcSegments + 1;
        arc.SetPositions(arcPoints);
        arc.enabled = true;

        Vector3 mid = Quaternion.AngleAxis(active.signedAngle * 0.5f, normal) * from;
        activeText.text = activeLabel;
        activeText.transform.position = pivot + mid * (ArcRadius + 0.03f);
        activeText.gameObject.SetActive(true);

        if (hasPressure)
        {
            float pMin, pMax;
            Span(pressure.markA, pressure.markB, pressure.markDir, pressure.pivot, pressure.pivotOnLines, out pMin, out pMax);
            DrawLine(pressureLine, pressure.markA + pressure.markDir * pMin, pressure.markA + pressure.markDir * pMax);
            pressureText.text = pressureLabel;
            pressureText.transform.position = pressure.markB + pressure.markDir * (LineMargin + 0.025f);
            pressureText.gameObject.SetActive(true);
        }
    }

    /// <summary>
    /// 면으로 옮기지 않은 3차원 선을 그대로 그린다(기준 Tr을 안 찍었을 때). 두 선은 공간에서 엇갈릴 수 있어
    /// 각도기 호는 그리지 않고, 가장 가까운 자리에 값만 띄운다.
    /// </summary>
    public void ShowDirect(Vector3 nA, Vector3 nB, Vector3 aA, Vector3 aB, Vector3 pivot, string activeLabel,
                           bool hasPressure, Vector3 pA, Vector3 pB, string pressureLabel)
    {
        HideMeasure();
        DrawRaw(neutralLine, nA, nB);
        DrawRaw(activeLine, aA, aB);
        activeText.text = activeLabel;
        activeText.transform.position = pivot + Vector3.up * 0.03f;
        activeText.gameObject.SetActive(true);
        if (hasPressure)
        {
            DrawRaw(pressureLine, pA, pB);
            pressureText.text = pressureLabel;
            pressureText.transform.position = pB + (pB - pA).normalized * (LineMargin + 0.025f);
            pressureText.gameObject.SetActive(true);
        }
    }

    private static void DrawRaw(LineRenderer lr, Vector3 a, Vector3 b)
    {
        Vector3 d = (b - a).normalized;
        DrawLine(lr, a - d * LineMargin, b + d * LineMargin);
    }

    // 선을 두 점과 교점을 모두 덮게 편다(교점이 실제 교점일 때만 교점까지 뻗는다).
    private static void Span(Vector3 a, Vector3 b, Vector3 dir, Vector3 pivot, bool usePivot, out float min, out float max)
    {
        float tb = Vector3.Dot(b - a, dir);
        min = Mathf.Min(0f, tb);
        max = Mathf.Max(0f, tb);
        if (usePivot)
        {
            float tp = Vector3.Dot(pivot - a, dir);
            min = Mathf.Min(min, tp);
            max = Mathf.Max(max, tp);
        }
        min -= LineMargin;
        max += LineMargin;
    }

    internal static void DrawLine(LineRenderer lr, Vector3 a, Vector3 b)
    {
        lr.positionCount = 2;
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);
        lr.enabled = true;
    }

    // ── 매 프레임 ───────────────────────────────────────────────────
    /// <summary>
    /// 컨트롤러 끝 표시와 이름표, 글자들의 눈 쪽 돌리기. 할당 없음.
    /// tipValid = 끝의 자리가 있다(추정값이라도) · tipTracked = 헤드셋 카메라가 실제로 보고 있다.
    /// 못 보고 있으면 끝 구슬을 빨갛게 그리고 이름표를 숨긴다 — 그 상태에서는 점이 찍히지 않는다는 표시다.
    /// </summary>
    public void Tick(Transform eye, bool tipValid, bool tipTracked, Vector3 tipPos, bool showTipLabel, bool[] fingerValid, Vector3[] fingerPos)
    {
        if (tipTracked != tipDrawnTracked)
        {
            tipDrawnTracked = tipTracked;
            tipRenderer.material.color = tipTracked ? TipColor : TipLostColor;
            tip.localScale = Vector3.one * PointRadius * (tipTracked ? 1.6f : 2.6f);
        }
        showTipLabel &= tipTracked;
        // 손끝 구슬은 판 가까이(15cm) 왔을 때만 — 환자를 잡은 손 위에 떠다니지 않게
        for (int i = 0; i < fingers.Length; i++)
        {
            bool show = fingerValid[i] && NearPanel(fingerPos[i], 0.15f);
            if (fingers[i].gameObject.activeSelf != show) fingers[i].gameObject.SetActive(show);
            if (show) fingers[i].position = fingerPos[i];
        }

        if (tip.gameObject.activeSelf != tipValid) tip.gameObject.SetActive(tipValid);
        bool label = tipValid && showTipLabel;
        if (tipLabel.gameObject.activeSelf != label) tipLabel.gameObject.SetActive(label);
        if (tipValid)
        {
            tip.position = tipPos;
            if (label)
            {
                tipLabel.transform.position = tipPos + eye.right * 0.065f + eye.up * 0.012f;
                Face(tipLabel.transform, eye);
            }
        }
        for (int i = 0; i < neutralLabels.Length; i++)
            if (neutralLabels[i].gameObject.activeSelf) Face(neutralLabels[i].transform, eye);
        if (activeText.gameObject.activeSelf) Face(activeText.transform, eye);
        if (pressureText.gameObject.activeSelf) Face(pressureText.transform, eye);
        if (cmpText.gameObject.activeSelf) Face(cmpText.transform, eye);
    }

    internal static void Face(Transform t, Transform eye)
    {
        Vector3 away = t.position - eye.position;
        if (away.sqrMagnitude > 1e-6f) t.rotation = Quaternion.LookRotation(away, eye.up);
    }

    // ── 만들기 ──────────────────────────────────────────────────────
    internal Transform MakeSphere(string name, float radius, Color c)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(root, false);
        go.transform.localScale = Vector3.one * radius * 2f;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.material.color = c;
        r.sortingOrder = 4;
        return go.transform;
    }

    internal LineRenderer MakeLine(string name, Color c, int count)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = mat;
        lr.useWorldSpace = true;
        lr.positionCount = count;
        lr.startWidth = lr.endWidth = LineWidth;
        lr.startColor = lr.endColor = c;
        lr.numCapVertices = 2;
        lr.sortingOrder = 3;
        lr.enabled = false;
        return lr;
    }

    private Transform MakeQuad(Transform parent, string name, Vector2 center, Vector2 size, Color c, int order)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(center.x, center.y, -0.001f * order);
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.material.color = c;
        r.sortingOrder = order;   // 같은 투명 큐라 그리는 순서를 정해 준다: 판 0 · 버튼 1 · 차오름 2 · 글자 3
        return go.transform;
    }

    internal TextMeshPro MakeText(Transform parent, string text, float size, TextAlignmentOptions align, int order)
    {
        var go = new GameObject("글자");
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshPro>();
        if (font != null) t.font = font;
        t.fontSize = size;
        t.alignment = align;
        t.color = Color.white;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.rectTransform.sizeDelta = new Vector2(0.2f, size * 2f);
        t.sortingOrder = order;
        t.text = text;
        return t;
    }

    internal static TextMeshPro Place(TextMeshPro t, Vector2 center, float width)
    {
        t.rectTransform.sizeDelta = new Vector2(width, t.fontSize * 2f);
        t.transform.localPosition = new Vector3(center.x, center.y, -0.004f);
        return t;
    }
}
