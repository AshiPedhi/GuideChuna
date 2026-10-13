using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// ★경추 ROM «선 실측» — 자유 측정의 표시(2026-10-07 신설 · 10-13 다시 짬).
/// ★10-13 사용자 지시: 자유 측정은 동작(굴곡·신전 / 측굴 / 회전)을 가르지 않고 <b>찍은 점들이 이루는 각 그대로</b>(3차원)를 낸다.
///   방식 셋 — 3점 측정(세 점, 가운데 점에서 벌어진 각) · 4점 측정(두 점씩 선 둘, 두 선의 각) · 연속 측정(점을 이어 가며 꺾이는 각).
/// 점·선(개수 제한 없음)과 선마다의 길이 글자, 각 글자, 고정 3축(바닥 십자와 정면 화살표뿐이다), 판의 결과 줄을 그린다.
/// 계산은 하지 않는다 — <see cref="RomLineSession"/>이 준 값만 그린다. 만드는 도구는 <see cref="RomLineView"/> 것을 빌려 쓴다.
/// ★글자·자리는 상태가 바뀔 때만 바꾼다. 매 프레임 하는 일은 글자를 눈 쪽으로 돌리는 것뿐이다(할당 없음).
/// </summary>
public class RomLineFreeView
{
    /// <summary>판에 보이는 선의 수(가장 최근 것부터). 한 줄에 선 하나.</summary>
    public const int Rows = 8;
    private const int Cols = 3;               // 각 · 견준 값 둘(방식마다 머리글이 다르다)
    private const float AxisWidth = 0.002f;
    // ★10-13 사용자 지시: 3축은 <b>바닥과 정면을 가를 수 있는 십자선만</b> 그린다 — 수직 기둥, 두 세로 면의 테두리,
    //   «정면 · 환자 오른쪽 / 왼쪽 · 굴곡·신전 면 · 측굴 면» 글자를 모두 뺐다. 정면은 화살표로만 가린다.
    //   자리 · 크기는 계산에 안 들어간다(방향만 쓴다).
    // ★10-13 사용자 지시: 3축은 <b>방향만 알 수 있게 작게</b> 그린다 — 키 1.9m · 폭 1m로 세웠더니 세로 선이 뒤쪽 메인 화면을 가렸다.
    //   자리 · 크기는 계산에 안 들어간다(방향만 쓴다). 바닥 십자와 두 면의 테두리를 같은 비율로 줄였다.
    private const float FrameHeight = 0.30f;   // 기둥 · 두 세로 면의 키
    private const float FrameHalf = 0.25f;     // 두 세로 면의 반 폭
    private const float CrossFront = 0.35f, CrossBack = 0.25f, CrossSide = 0.30f;   // 바닥 십자: 정면 쪽 · 뒤쪽 · 좌우

    private static readonly Color FirstColor = RomLineView.NeutralColor;   // 기준선 = 중립과 같은 색
    private static readonly Color LineColor = RomLineView.ActiveColor;     // 측정선 = 능동과 같은 색
    private static readonly Color DimColor = new Color(0.72f, 0.76f, 0.82f, 1f);
    // 바닥 십자의 색
    private static readonly Color FloorColor = new Color(1f, 0.85f, 0.30f, 1f);

    private RomLineView v;
    private Transform world, axesRoot;

    private readonly List<Transform> spheres = new List<Transform>(64);
    private readonly List<Renderer> sphereRenderers = new List<Renderer>(64);
    private readonly List<LineRenderer> lines = new List<LineRenderer>(32);
    private readonly List<TextMeshPro> labels = new List<TextMeshPro>(32);
    private readonly List<TextMeshPro> angleTexts = new List<TextMeshPro>(16);

    private readonly TextMeshPro[] names = new TextMeshPro[Rows];
    private readonly TextMeshPro[] cells = new TextMeshPro[Rows * Cols];
    private TextMeshPro headName;
    private readonly TextMeshPro[] heads = new TextMeshPro[Cols];

    private LineRenderer axForward, axRight, axArrow;

    public void Build(RomLineView view, Transform parent, Transform panelArea, float tableTop)
    {
        v = view;
        world = new GameObject("자유 측정 표시").transform;
        world.SetParent(parent, false);

        // ── 판의 결과 줄 ── 번호 | 각 | 견준 값 둘. 머리글은 방식마다 다르다(SetHeads).
        const float xName = -0.33f;
        float[] x = { -0.20f, 0.02f, 0.26f };
        float[] w = { 0.14f, 0.24f, 0.20f };
        headName = RomLineView.Place(v.MakeText(panelArea, "", 0.10f, TextAlignmentOptions.Left, 3), new Vector2(xName, tableTop), 0.10f);
        headName.color = DimColor;
        for (int c = 0; c < Cols; c++)
        {
            heads[c] = RomLineView.Place(v.MakeText(panelArea, "", 0.10f, TextAlignmentOptions.Center, 3), new Vector2(x[c], tableTop), w[c]);
            heads[c].color = DimColor;
        }
        for (int r = 0; r < Rows; r++)
        {
            float y = tableTop - 0.030f - r * 0.0250f;
            names[r] = RomLineView.Place(v.MakeText(panelArea, "", 0.125f, TextAlignmentOptions.Left, 3), new Vector2(xName, y), 0.10f);
            for (int c = 0; c < Cols; c++)
                cells[r * Cols + c] = RomLineView.Place(v.MakeText(panelArea, "", c == 0 ? 0.14f : 0.12f, TextAlignmentOptions.Center, 3), new Vector2(x[c], y), w[c]);
        }

        // ── 고정 3축 ──
        axesRoot = new GameObject("고정 3축").transform;
        axesRoot.SetParent(world, false);
        axForward = Axis("앞뒤 축", FloorColor, 2);
        axRight = Axis("좌우 축", FloorColor, 2);
        axArrow = Axis("정면 화살표", FloorColor, 3);
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
    /// 찍힌 점들과 그 사이의 선을 그리고 선마다 길이(mm)를 붙인다. mode 0 = 세 점씩(1-2 · 2-3) · 1 = 네 점씩(1-2 · 3-4) · 2 = 이어서(앞 점과 전부).
    /// 3점 측정의 첫 묶음은 기준이라 흰색으로 그린다. ★어느 점끼리 잇는지는 <see cref="Linked"/> 하나가 정한다 — 세션(모니터로 보내는 선)도 같은 함수를 쓴다.
    /// </summary>
    public void SetShape(List<Vector3> pts, int mode)
    {
        int n = pts.Count;
        while (spheres.Count < n)
        {
            Transform s = v.MakeSphere("점(자유)", RomLineView.PointRadius, LineColor);
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
            sphereRenderers[i].material.color = mode == 0 && i < 3 ? FirstColor : LineColor;
        }

        int seg = 0;
        for (int i = 0; i + 1 < n; i++)
        {
            if (!Linked(mode, i)) continue;
            if (lines.Count <= seg)
            {
                LineRenderer lr = v.MakeLine("선(자유)", LineColor, 2);
                lr.transform.SetParent(world, false);
                lines.Add(lr);
                labels.Add(RomLineView.Outlined(v.MakeText(world, "", 0.07f, TextAlignmentOptions.Center, 5)));   // 글자는 선의 색을 따르지 않는다(10-13 오후)
            }
            Color c = mode == 0 && i < 3 ? FirstColor : LineColor;
            lines[seg].startColor = lines[seg].endColor = c;
            Vector3 a = pts[i], b = pts[i + 1];
            RomLineView.DrawLine(lines[seg], a, b);   // 찍은 두 점 사이만(10-13)
            labels[seg].text = (Vector3.Distance(a, b) * 1000f).ToString("F0");   // 길이는 mm, 단위 글자 없이(10-13)
            labels[seg].transform.position = (a + b) * 0.5f + Vector3.up * 0.012f;
            if (!labels[seg].gameObject.activeSelf) labels[seg].gameObject.SetActive(true);
            seg++;
        }
        for (int i = seg; i < lines.Count; i++)
        {
            lines[i].enabled = false;
            if (labels[i].gameObject.activeSelf) labels[i].gameObject.SetActive(false);
        }
    }

    /// <summary>점 i와 점 i+1을 선으로 잇는가. mode 0 = 세 점씩 · 1 = 네 점씩 · 2 = 이어서.</summary>
    public static bool Linked(int mode, int i)
    {
        return mode == 2 || (mode == 0 ? i % 3 != 2 : i % 4 == 0 || i % 4 == 2);
    }

    /// <summary>각 글자들 — 자리(꼭짓점이나 두 선 사이)와 글. 개수가 줄면 남는 글자는 끈다.</summary>
    public void SetAngles(List<Vector3> at, List<string> text)
    {
        while (angleTexts.Count < at.Count)
        {
            TextMeshPro t = v.MakeText(world, "", 0.10f, TextAlignmentOptions.Center, 5);
            RomLineView.Outlined(t);
            angleTexts.Add(t);
        }
        for (int i = 0; i < angleTexts.Count; i++)
        {
            bool show = i < at.Count;
            if (angleTexts[i].gameObject.activeSelf != show) angleTexts[i].gameObject.SetActive(show);
            if (!show) continue;
            angleTexts[i].text = text[i];
            angleTexts[i].transform.position = at[i] + Vector3.up * 0.022f;
        }
    }

    // ── 판 ──────────────────────────────────────────────────────────
    /// <summary>결과 줄의 머리글 넷(번호 칸 · 각 · 견준 값 둘). 방식을 바꿀 때 부른다.</summary>
    public void SetHeads(string name, string a, string b, string c)
    {
        headName.text = name;
        heads[0].text = a;
        heads[1].text = b;
        heads[2].text = c;
    }

    /// <summary>한 줄(측정 하나). 빈 글자를 주면 그 칸은 비운다. isRef = 기준(3점 측정의 첫 묶음) — 흰색으로 적는다.</summary>
    public void SetRow(int row, string name, string a, string b, string c, bool isRef)
    {
        names[row].text = name;
        names[row].color = isRef ? FirstColor : Color.white;
        int o = row * Cols;
        cells[o].text = a;
        cells[o].color = isRef ? FirstColor : LineColor;
        cells[o + 1].text = b;
        cells[o + 2].text = c;
    }

    // ── 고정 3축 ────────────────────────────────────────────────────
    /// <summary>
    /// 바닥의 십자와 정면 화살표. origin은 바닥 위의 한 점이다.
    /// ★자리는 뜻이 없다(방향만 쓴다) — 환자가 이 십자 위에 꼭 서지 않아도 정면만 화살표와 나란하면 된다.
    /// </summary>
    public void ShowAxes(bool show, Vector3 origin, RomLineFrame f)
    {
        if (axesRoot.gameObject.activeSelf != show) axesRoot.gameObject.SetActive(show);
        if (!show) return;

        Vector3 o = origin + Vector3.up * 0.005f;   // 바닥에 묻히지 않게
        Vector3 tipPos = o + f.forward * CrossFront;
        RomLineView.DrawLine(axForward, o - f.forward * CrossBack, tipPos);
        RomLineView.DrawLine(axRight, o - f.right * CrossSide, o + f.right * CrossSide);
        axArrow.positionCount = 3;
        axArrow.SetPosition(0, tipPos - f.forward * 0.06f + f.right * 0.035f);
        axArrow.SetPosition(1, tipPos);
        axArrow.SetPosition(2, tipPos - f.forward * 0.06f - f.right * 0.035f);
        axArrow.enabled = true;
    }

    // ── 매 프레임 ───────────────────────────────────────────────────
    public void Tick(Transform eye)
    {
        if (!world.gameObject.activeSelf) return;
        for (int i = 0; i < labels.Count; i++)
            if (labels[i].gameObject.activeSelf) RomLineView.Face(labels[i].transform, eye);
        for (int i = 0; i < angleTexts.Count; i++)
            if (angleTexts[i].gameObject.activeSelf) RomLineView.Face(angleTexts[i].transform, eye);
    }
}
