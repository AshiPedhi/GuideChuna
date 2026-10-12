using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// ★경추 ROM «선 실측» — 자유 측정의 표시(2026-10-07 신설).
/// 점·선(개수 제한 없음)과 선마다의 길이 글자, 고정 3축(바닥 십자 · 기둥 · 두 세로 면의 테두리), 판의 결과 줄.
/// 동작(굴곡·신전 / 측굴 / 회전)마다 갈래가 나뉘어 있어, 지금 갈래의 선과 <b>그 동작의 면 값 하나</b>만 보여 준다.
/// 계산은 하지 않는다 — <see cref="RomLineSession"/>이 준 값만 그린다. 만드는 도구는 <see cref="RomLineView"/> 것을 빌려 쓴다.
/// ★글자·자리는 상태가 바뀔 때만 바꾼다. 매 프레임 하는 일은 글자를 눈 쪽으로 돌리는 것뿐이다(할당 없음).
/// </summary>
public class RomLineFreeView
{
    /// <summary>판에 보이는 선의 수(가장 최근 것부터). 한 줄에 선 하나.</summary>
    public const int Rows = 8;
    private const int Cols = 3;               // 그 동작의 면 각 · 직접 각 · 알림
    private const float LineMargin = 0.02f;
    private const float AxisWidth = 0.006f;
    private const float FrameHeight = 1.9f;

    private static readonly Color FirstColor = new Color(0.92f, 0.92f, 0.92f, 1f);   // 첫 선(기준선)
    private static readonly Color LineColor = new Color(1f, 0.42f, 0.02f, 1f);
    private static readonly Color DimColor = new Color(0.72f, 0.76f, 0.82f, 1f);
    private static readonly Color OffColor = new Color(0.40f, 0.43f, 0.48f, 1f);
    private static readonly Color WarnColor = new Color(1f, 0.75f, 0.45f, 1f);
    // 세 면의 색 — 판의 칸 머리글과 공간의 축이 같은 색이다
    private static readonly Color SagColor = new Color(0.40f, 0.70f, 1f, 1f);     // 굴곡·신전(옆에서 본 면)
    private static readonly Color FrontColor = new Color(0.45f, 0.90f, 0.55f, 1f); // 측굴(앞에서 본 면)
    private static readonly Color FloorColor = new Color(1f, 0.85f, 0.30f, 1f);    // 회전(바닥)

    private RomLineView v;
    private Transform world, axesRoot;

    private readonly List<Transform> spheres = new List<Transform>(64);
    private readonly List<Renderer> sphereRenderers = new List<Renderer>(64);
    private readonly List<LineRenderer> lines = new List<LineRenderer>(32);
    private readonly List<TextMeshPro> labels = new List<TextMeshPro>(32);
    private TextMeshPro angleText;

    private readonly TextMeshPro[] names = new TextMeshPro[Rows];
    private readonly TextMeshPro[] cells = new TextMeshPro[Rows * Cols];
    private TextMeshPro planeHead;
    private static readonly Color[] PlaneColors = { SagColor, FrontColor, FloorColor };

    private LineRenderer axForward, axRight, axPole, axArrow, sagFrame, frontFrame;
    private readonly TextMeshPro[] axLabels = new TextMeshPro[5];
    private readonly Vector3[] four = new Vector3[4];

    public void Build(RomLineView view, Transform parent, Transform panelArea, float tableTop)
    {
        v = view;
        world = new GameObject("자유 측정 표시").transform;
        world.SetParent(parent, false);

        angleText = v.MakeText(world, "", 0.10f, TextAlignmentOptions.Center, 5);
        angleText.color = LineColor;
        angleText.gameObject.SetActive(false);

        // ── 판의 결과 줄 ── 선 · 길이 | 그 동작의 면 각 | 직접 각 | 알림
        //   10-12: 메인 화면이 넓어져(0.80 × 0.38, 배율 전) 칸을 벌리고 글자를 키웠다.
        const float xName = -0.27f;
        float[] x = { -0.06f, 0.10f, 0.27f };
        float[] w = { 0.18f, 0.12f, 0.20f };
        RomLineView.Place(v.MakeText(panelArea, "선 · 길이", 0.085f, TextAlignmentOptions.Left, 3), new Vector2(xName, tableTop), 0.20f).color = DimColor;
        planeHead = RomLineView.Place(v.MakeText(panelArea, "", 0.09f, TextAlignmentOptions.Center, 3), new Vector2(x[0], tableTop), w[0]);
        RomLineView.Place(v.MakeText(panelArea, "직접 각", 0.085f, TextAlignmentOptions.Center, 3), new Vector2(x[1], tableTop), w[1]).color = DimColor;
        for (int r = 0; r < Rows; r++)
        {
            float y = tableTop - 0.030f - r * 0.0265f;
            names[r] = RomLineView.Place(v.MakeText(panelArea, "", 0.105f, TextAlignmentOptions.Left, 3), new Vector2(xName, y), 0.20f);
            cells[r * Cols] = RomLineView.Place(v.MakeText(panelArea, "", 0.12f, TextAlignmentOptions.Center, 3), new Vector2(x[0], y), w[0]);
            cells[r * Cols + 1] = RomLineView.Place(v.MakeText(panelArea, "", 0.10f, TextAlignmentOptions.Center, 3), new Vector2(x[1], y), w[1]);
            cells[r * Cols + 1].color = DimColor;
            cells[r * Cols + 2] = RomLineView.Place(v.MakeText(panelArea, "", 0.085f, TextAlignmentOptions.Left, 3), new Vector2(x[2], y), w[2]);
            cells[r * Cols + 2].color = WarnColor;
        }

        // ── 고정 3축 ──
        axesRoot = new GameObject("고정 3축").transform;
        axesRoot.SetParent(world, false);
        axForward = Axis("앞뒤 축", FloorColor, 2);
        axRight = Axis("좌우 축", FloorColor, 2);
        axArrow = Axis("정면 화살표", FloorColor, 3);
        axPole = Axis("수직 축", DimColor, 2);
        sagFrame = Axis("굴곡·신전 면", SagColor, 4);
        frontFrame = Axis("측굴 면", FrontColor, 4);
        sagFrame.loop = frontFrame.loop = true;
        string[] names5 = { "정면", "환자 오른쪽", "환자 왼쪽", "굴곡·신전 면", "측굴 면" };
        Color[] colors5 = { FloorColor, FloorColor, FloorColor, SagColor, FrontColor };
        for (int i = 0; i < axLabels.Length; i++)
        {
            axLabels[i] = v.MakeText(axesRoot, names5[i], i == 0 ? 0.5f : 0.35f, TextAlignmentOptions.Center, 5);
            axLabels[i].color = colors5[i];
        }
        axesRoot.gameObject.SetActive(false);
        world.gameObject.SetActive(false);
    }

    private LineRenderer Axis(string name, Color c, int count)
    {
        LineRenderer lr = v.MakeLine(name, c, count);
        lr.transform.SetParent(axesRoot, false);
        lr.startWidth = lr.endWidth = AxisWidth;
        return lr;
    }

    /// <summary>자유 측정의 공간 표시(점·선·축)를 통째로 켜고 끈다 — 6동작 모드로 돌아갈 때 끈다.</summary>
    public void SetVisible(bool on)
    {
        if (world.gameObject.activeSelf != on) world.gameObject.SetActive(on);
    }

    // ── 점·선 ───────────────────────────────────────────────────────
    /// <summary>
    /// 찍힌 점들(두 점마다 선 하나)을 그리고 선마다 «번호 · 길이»를 붙인다.
    /// refOf[선] = 그 선이 견준 기준선의 번호 — 자기 번호와 같으면 기준선이라 흰색으로 그린다.
    /// </summary>
    public void SetPoints(List<Vector3> pts, List<int> refOf)
    {
        int n = pts.Count, ln = n / 2;
        while (spheres.Count < n)
        {
            Transform s = v.MakeSphere("점(자유)", 0.005f, LineColor);
            s.SetParent(world, false);
            spheres.Add(s);
            sphereRenderers.Add(s.GetComponent<Renderer>());
        }
        for (int i = 0; i < spheres.Count; i++)
        {
            bool show = i < n;
            if (spheres[i].gameObject.activeSelf != show) spheres[i].gameObject.SetActive(show);
            if (!show) continue;
            spheres[i].position = pts[i];
            int line = i / 2;
            sphereRenderers[i].material.color = line < refOf.Count && refOf[line] == line ? FirstColor : LineColor;   // 찍다 만 선의 첫 점은 측정선 색
        }

        while (lines.Count < ln)
        {
            LineRenderer lr = v.MakeLine("선(자유)", LineColor, 2);
            lr.transform.SetParent(world, false);
            lines.Add(lr);
            labels.Add(v.MakeText(world, "", 0.07f, TextAlignmentOptions.Center, 5));
        }
        for (int i = 0; i < lines.Count; i++)
        {
            bool show = i < ln;
            lines[i].enabled = show;
            if (labels[i].gameObject.activeSelf != show) labels[i].gameObject.SetActive(show);
            if (!show) continue;
            Color c = refOf[i] == i ? FirstColor : LineColor;
            lines[i].startColor = lines[i].endColor = c;
            labels[i].color = c;
            Vector3 a = pts[i * 2], b = pts[i * 2 + 1];
            Vector3 d = (b - a).normalized;
            RomLineView.DrawLine(lines[i], a - d * LineMargin, b + d * LineMargin);
            labels[i].text = (i + 1) + " · " + (Vector3.Distance(a, b) * 100f).ToString("F1") + "cm";
            labels[i].transform.position = (a + b) * 0.5f + Vector3.up * 0.015f;
        }
    }

    /// <summary>가장 최근 선 곁에 띄우는 각 글자(«첫 선 ○° · 앞 선 ○°»). 글자가 비면 끈다.</summary>
    public void SetAngleLabel(string text, Vector3 pos)
    {
        bool show = text.Length > 0;
        if (angleText.gameObject.activeSelf != show) angleText.gameObject.SetActive(show);
        if (!show) return;
        angleText.text = text;
        angleText.transform.position = pos;
    }

    // ── 판 ──────────────────────────────────────────────────────────
    /// <summary>
    /// 지금 갈래(0 굴곡·신전 · 1 측굴 · 2 회전)의 면 이름을 칸 머리글에 적고 그 면의 색으로 칠한다.
    /// planeOn이 false면(3축을 꺼서 그 면을 세울 수 없다) 흐리게 한다.
    /// </summary>
    public void SetTab(int tab, bool planeOn, string head)
    {
        Color c = planeOn ? PlaneColors[tab] : OffColor;
        planeHead.text = head;
        planeHead.color = c;
        for (int r = 0; r < Rows; r++) cells[r * Cols].color = c;
    }

    /// <summary>한 줄(선 하나). 빈 글자를 주면 그 칸은 비운다. isRef = 기준선(면 각 칸에 «기준선»이라고 적는다).</summary>
    public void SetRow(int row, string name, string plane, string direct, string note, bool isRef)
    {
        names[row].text = name;
        names[row].color = isRef ? FirstColor : Color.white;
        int o = row * Cols;
        cells[o].text = plane;
        if (isRef) cells[o].color = DimColor;   // SetTab이 먼저 칠한 색 위에 — 기준선 줄만 흐리게
        cells[o + 1].text = direct;
        cells[o + 2].text = note;
    }

    // ── 고정 3축 ────────────────────────────────────────────────────
    /// <summary>
    /// 바닥의 십자(정면 화살표) · 수직 기둥 · 두 세로 면의 테두리. origin은 바닥 위의 한 점이다.
    /// ★자리는 뜻이 없다(방향만 쓴다) — 환자가 이 십자 위에 꼭 서지 않아도 정면만 화살표와 나란하면 된다.
    /// </summary>
    public void ShowAxes(bool show, Vector3 origin, RomLineFrame f)
    {
        if (axesRoot.gameObject.activeSelf != show) axesRoot.gameObject.SetActive(show);
        if (!show) return;

        Vector3 o = origin + Vector3.up * 0.005f;   // 바닥에 묻히지 않게
        Vector3 tipPos = o + f.forward * 0.7f;
        RomLineView.DrawLine(axForward, o - f.forward * 0.5f, tipPos);
        RomLineView.DrawLine(axRight, o - f.right * 0.6f, o + f.right * 0.6f);
        RomLineView.DrawLine(axPole, o, o + f.up * FrameHeight);
        axArrow.positionCount = 3;
        axArrow.SetPosition(0, tipPos - f.forward * 0.12f + f.right * 0.07f);
        axArrow.SetPosition(1, tipPos);
        axArrow.SetPosition(2, tipPos - f.forward * 0.12f - f.right * 0.07f);
        axArrow.enabled = true;
        Frame(sagFrame, o, f.forward, f.up);
        Frame(frontFrame, o, f.right, f.up);

        axLabels[0].transform.position = tipPos + f.forward * 0.10f + f.up * 0.05f;
        axLabels[1].transform.position = o + f.right * 0.70f + f.up * 0.05f;
        axLabels[2].transform.position = o - f.right * 0.70f + f.up * 0.05f;
        axLabels[3].transform.position = o + f.forward * 0.5f + f.up * (FrameHeight + 0.04f);
        axLabels[4].transform.position = o + f.right * 0.5f + f.up * (FrameHeight + 0.04f);
    }

    private void Frame(LineRenderer lr, Vector3 o, Vector3 side, Vector3 up)
    {
        four[0] = o - side * 0.5f;
        four[1] = o + side * 0.5f;
        four[2] = o + side * 0.5f + up * FrameHeight;
        four[3] = o - side * 0.5f + up * FrameHeight;
        lr.positionCount = 4;
        lr.SetPositions(four);
        lr.enabled = true;
    }

    // ── 매 프레임 ───────────────────────────────────────────────────
    public void Tick(Transform eye)
    {
        if (!world.gameObject.activeSelf) return;
        for (int i = 0; i < labels.Count; i++)
            if (labels[i].gameObject.activeSelf) RomLineView.Face(labels[i].transform, eye);
        if (angleText.gameObject.activeSelf) RomLineView.Face(angleText.transform, eye);
        if (!axesRoot.gameObject.activeSelf) return;
        for (int i = 0; i < axLabels.Length; i++) RomLineView.Face(axLabels[i].transform, eye);
    }
}
