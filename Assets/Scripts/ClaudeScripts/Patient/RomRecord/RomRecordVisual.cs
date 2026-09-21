using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 실측 기록의 표시물 — 3축, 기준축, 대추·미간, 단면 각도기, 동작 마커, 안내판(2026-09-18 신설).
/// ★그리기만 한다. 각은 <see cref="RomRecordGeometry"/>가 낸 값을 받아 쓴다(미리보기가 따로 계산하지 않는다 — 규칙 9).
/// ★위치 갱신은 값이 바뀔 때만 부른다. 매 프레임 하는 것은 글자를 눈 쪽으로 돌리는 것뿐이다.
/// </summary>
public class RomRecordVisual
{
    private const int RingSegments = 72;
    private const int TickCount = 36;      // 10°마다
    private const int TickLabelCount = 12; // 30°마다
    public const int MaxMarks = 4;

    private Transform root;
    private TMP_FontAsset font;
    private Material lineMat;
    private readonly List<TextMeshPro> facing = new List<TextMeshPro>();

    private LineRenderer axisUp, axisFwd, axisRight, pivotAxis, neutralLine, ring, zeroLine, needle;
    private Transform needleGrip;
    private TextMeshPro needleLabel;
    private TextMeshPro labUp, labDown, labFwd, labBack, labRight, labLeft, panel;
    private Transform c7Dot, glabDot, liveDot, pivotDot;
    private readonly LineRenderer[] ticks = new LineRenderer[TickCount];
    private readonly TextMeshPro[] tickLabels = new TextMeshPro[TickLabelCount];
    private readonly Transform[] markDots = new Transform[MaxMarks];
    private readonly LineRenderer[] markLines = new LineRenderer[MaxMarks];
    private readonly TextMeshPro[] markLabels = new TextMeshPro[MaxMarks];
    private GameObject dialRoot;

    // ★글자 크기는 TMP 폰트 크기(스케일 1)다. 기기에서 확인된 기존 값: 각도 판독 0.05 · 축·눈금 글자 0.03
    //   (CervicalRomPlaneGauge·PracticeReadout, TrainingScene). 09-18 첫 판은 1.1을 넣어 20~30배 컸다 —
    //   실측랩 코드의 labelSize 1.2를 그대로 따라 썼다.
    public float textSize = 0.05f;     // 축 끝 글자·마커 글자(눈금 숫자는 0.8배)
    public float panelSize = 0.045f;   // 안내판

    public static readonly Color UpColor = new Color(0.35f, 0.95f, 0.45f);
    public static readonly Color FwdColor = new Color(0.35f, 0.65f, 1f);
    public static readonly Color RightColor = new Color(1f, 0.4f, 0.4f);
    public static readonly Color PivotColor = new Color(1f, 0.85f, 0.2f);
    public static readonly Color ActiveColor = new Color(1f, 0.6f, 0.15f);
    public static readonly Color PassiveColor = new Color(0.95f, 0.35f, 0.95f);
    public static readonly Color NeedleColor = new Color(0.2f, 1f, 0.85f);      // 바늘 — 마커 색과 겹치지 않게 청록
    public static readonly Color NeedleHeldColor = new Color(1f, 1f, 0.35f);    // 잡고 있는 동안

    public void Build(Transform parent, TMP_FontAsset f, Material mat)
    {
        font = f;
        lineMat = mat;
        root = new GameObject("[실측기록] 표시").transform;
        root.SetParent(parent, false);

        axisUp = Line("연직축", UpColor, 0.004f);
        axisFwd = Line("전후축", FwdColor, 0.004f);
        axisRight = Line("좌우축", RightColor, 0.004f);
        pivotAxis = Line("기준축(목 중앙)", PivotColor, 0.006f);
        neutralLine = Line("중립선", Color.white, 0.003f);

        labUp = Label("위", textSize, UpColor);
        labDown = Label("아래", textSize * 0.8f, UpColor);
        labFwd = Label("환자 앞", textSize, FwdColor);
        labBack = Label("환자 뒤", textSize * 0.8f, FwdColor);
        labRight = Label("환자 오른쪽", textSize, RightColor);
        labLeft = Label("환자 왼쪽", textSize * 0.8f, RightColor);

        pivotDot = Dot("기준점", PivotColor, 0.012f);
        c7Dot = Dot("대추", new Color(0.3f, 0.9f, 1f), 0.016f);
        glabDot = Dot("미간(중립)", Color.white, 0.016f);
        liveDot = Dot("핀치 중", new Color(1f, 1f, 0.3f), 0.014f);

        dialRoot = new GameObject("각도기");
        dialRoot.transform.SetParent(root, false);
        ring = Line("눈금판", new Color(1f, 1f, 1f, 0.7f), 0.004f, dialRoot.transform);
        ring.positionCount = RingSegments + 1;
        zeroLine = Line("눈금 0", new Color(1f, 1f, 1f, 0.9f), 0.005f, dialRoot.transform);
        for (int i = 0; i < TickCount; i++) ticks[i] = Line("눈금", new Color(1f, 1f, 1f, 0.75f), 0.003f, dialRoot.transform);
        for (int i = 0; i < TickLabelCount; i++)
        {
            int deg = i * 30;
            tickLabels[i] = Label((deg <= 180 ? deg : 360 - deg).ToString(), textSize * 0.8f, new Color(1f, 1f, 1f, 0.9f), dialRoot.transform);
        }

        // ★바늘(2026-09-21) — 각도기 중심에서 뻗은 지침. 끝의 손잡이를 잡아 그 단면 안에서만 돌린다.
        needle = Line("바늘", NeedleColor, 0.007f);
        needleGrip = Dot("바늘 손잡이", NeedleColor, 0.03f);
        needleLabel = Label("", textSize * 1.4f, NeedleColor);

        for (int i = 0; i < MaxMarks; i++)
        {
            markDots[i] = Dot("마커", ActiveColor, 0.016f);
            markLines[i] = Line("마커선", ActiveColor, 0.003f);
            markLabels[i] = Label("", textSize, ActiveColor);
        }

        panel = Label("", panelSize, Color.white);
        panel.alignment = TextAlignmentOptions.TopLeft;
        panel.rectTransform.sizeDelta = new Vector2(panelSize * 16f, panelSize * 12f);

        SetLive(false, Vector3.zero);
        SetNeedle(false, Vector3.zero, Vector3.up, 0.15f, null, false);
        SetDial(false, Vector3.zero, Vector3.up, Vector3.forward, 0.15f);
        for (int i = 0; i < MaxMarks; i++) SetMark(i, false, Vector3.zero, Vector3.zero, null, ActiveColor);
    }

    // ── 3축 · 기준축 ──────────────────────────────────────────────────
    public void SetFrame(Vector3 pivot, float yawDeg, float len)
    {
        Vector3 u = RomRecordGeometry.Up, f = RomRecordGeometry.Forward(yawDeg), r = RomRecordGeometry.Right(yawDeg);
        Seg(axisUp, pivot - u * len, pivot + u * len);
        Seg(axisFwd, pivot - f * len, pivot + f * len);
        Seg(axisRight, pivot - r * len, pivot + r * len);
        Seg(pivotAxis, pivot - u * (len * 1.6f), pivot + u * (len * 1.6f));
        pivotDot.position = pivot;

        labUp.transform.position = pivot + u * (len + 0.02f);
        labDown.transform.position = pivot - u * (len + 0.02f);
        labFwd.transform.position = pivot + f * (len + 0.03f);
        labBack.transform.position = pivot - f * (len + 0.03f);
        labRight.transform.position = pivot + r * (len + 0.04f);
        labLeft.transform.position = pivot - r * (len + 0.04f);
    }

    public void SetLandmarks(bool hasC7, Vector3 c7, bool hasGlab, Vector3 glab, Vector3 pivot)
    {
        Show(c7Dot, hasC7);
        if (hasC7) c7Dot.position = c7;
        Show(glabDot, hasGlab);
        Show(neutralLine, hasGlab);
        if (hasGlab)
        {
            glabDot.position = glab;
            Seg(neutralLine, pivot, glab);
        }
    }

    // ── 각도기 ───────────────────────────────────────────────────────
    /// <summary>normal 단면에 zeroDir을 0으로 하는 눈금판을 그린다. 눈금 숫자는 0~180 양쪽 대칭(부호 없음).</summary>
    public void SetDial(bool on, Vector3 center, Vector3 normal, Vector3 zeroDir, float radius)
    {
        if (dialRoot.activeSelf != on) dialRoot.SetActive(on);
        if (!on) return;

        Vector3 z = Vector3.ProjectOnPlane(zeroDir, normal).normalized;
        Vector3 o = Vector3.Cross(normal, z).normalized;
        for (int k = 0; k <= RingSegments; k++)
        {
            float a = k * (2f * Mathf.PI / RingSegments);
            ring.SetPosition(k, center + (z * Mathf.Cos(a) + o * Mathf.Sin(a)) * radius);
        }
        for (int i = 0; i < TickCount; i++)
        {
            float a = i * 10f * Mathf.Deg2Rad;
            Vector3 d = z * Mathf.Cos(a) + o * Mathf.Sin(a);
            float inner = i % 3 == 0 ? 0.86f : 0.93f;
            Seg(ticks[i], center + d * (radius * inner), center + d * radius);
        }
        for (int i = 0; i < TickLabelCount; i++)
        {
            float a = i * 30f * Mathf.Deg2Rad;
            tickLabels[i].transform.position = center + (z * Mathf.Cos(a) + o * Mathf.Sin(a)) * (radius * 1.12f);
        }
        Seg(zeroLine, center, center + z * radius);
    }

    // ── 동작 마커 ────────────────────────────────────────────────────
    public void SetMark(int i, bool on, Vector3 pos, Vector3 pivot, string label, Color c)
    {
        if (i < 0 || i >= MaxMarks) return;
        Show(markDots[i], on);
        Show(markLines[i], on);
        if (markLabels[i].gameObject.activeSelf != on) markLabels[i].gameObject.SetActive(on);
        if (!on) return;

        markDots[i].position = pos;
        markDots[i].GetComponent<Renderer>().material.color = c;
        markLines[i].startColor = markLines[i].endColor = c;
        Seg(markLines[i], pivot, pos);
        markLabels[i].color = c;
        if (label != null) markLabels[i].text = label;
        markLabels[i].transform.position = pos + Vector3.up * 0.03f;
    }

    public void SetLive(bool on, Vector3 pos)
    {
        Show(liveDot, on);
        if (on) liveDot.position = pos;
    }

    /// <summary>
    /// 바늘(2026-09-21). 각도기 중심에서 <paramref name="dir"/> 쪽으로 뻗고, 끝에 잡는 손잡이가 달린다.
    /// ★손잡이는 <b>바늘보다 조금 더 밖</b>에 둔다 — 눈금과 겹치면 잡으려다 눈금을 가린다.
    /// </summary>
    public void SetNeedle(bool on, Vector3 center, Vector3 dir, float radius, string label, bool held)
    {
        Show(needle, on);
        Show(needleGrip, on);
        if (needleLabel.gameObject.activeSelf != on) needleLabel.gameObject.SetActive(on);
        if (!on) return;

        Color c = held ? NeedleHeldColor : NeedleColor;
        Vector3 tip = center + dir * radius;
        Seg(needle, center, tip);
        needle.startColor = needle.endColor = c;
        needleGrip.position = tip;
        needleGrip.GetComponent<Renderer>().material.color = c;
        needleGrip.localScale = Vector3.one * (held ? 0.038f : 0.03f);
        needleLabel.color = c;
        if (label != null) needleLabel.text = label;
        needleLabel.transform.position = center + dir * (radius * 0.62f);
    }

    public void SetPanel(Vector3 pos, string text)
    {
        panel.transform.position = pos;
        if (text != null) panel.text = text;
    }

    public void SetAxesVisible(bool on)
    {
        Show(axisUp, on); Show(axisFwd, on); Show(axisRight, on); Show(pivotAxis, on); Show(pivotDot, on);
        foreach (var t in new[] { labUp, labDown, labFwd, labBack, labRight, labLeft })
            if (t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
    }

    /// <summary>글자를 눈 쪽으로 돌린다. 매 프레임 부른다 — 할당은 없다.</summary>
    public void FaceCamera(Transform eye)
    {
        if (eye == null) return;
        for (int i = 0; i < facing.Count; i++)
        {
            var t = facing[i];
            if (!t.gameObject.activeInHierarchy) continue;
            t.transform.rotation = Quaternion.LookRotation(t.transform.position - eye.position, Vector3.up);
        }
    }

    // ── 만드는 도구 ──────────────────────────────────────────────────
    private LineRenderer Line(string name, Color c, float width, Transform parent = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent != null ? parent : root, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.useWorldSpace = true;
        lr.widthMultiplier = width;
        lr.sharedMaterial = lineMat;
        lr.startColor = lr.endColor = c;
        lr.numCapVertices = 2;
        return lr;
    }

    private TextMeshPro Label(string text, float size, Color c, Transform parent = null)
    {
        var go = new GameObject("글자 " + text);
        go.transform.SetParent(parent != null ? parent : root, false);
        var t = go.AddComponent<TextMeshPro>();
        if (font != null) t.font = font;
        t.text = text;
        t.fontSize = size;
        t.color = c;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.rectTransform.sizeDelta = new Vector2(size * 10f, size * 2f);
        // ★패스스루(실제 방) 위에서 읽혀야 한다 — 밝은 벽에서도 보이게 외곽선을 준다.
        t.outlineWidth = 0.25f;
        t.outlineColor = new Color32(0, 0, 0, 220);
        facing.Add(t);
        return t;
    }

    private Transform Dot(string name, Color c, float size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(root, false);
        go.transform.localScale = Vector3.one * size;
        var r = go.GetComponent<Renderer>();
        if (lineMat != null) r.sharedMaterial = lineMat;
        r.material.color = c;
        return go.transform;
    }

    private static void Seg(LineRenderer lr, Vector3 a, Vector3 b)
    {
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);
    }

    private static void Show(Component c, bool on)
    {
        if (c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
    }
}
