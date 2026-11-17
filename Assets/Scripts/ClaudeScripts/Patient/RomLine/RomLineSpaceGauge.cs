using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// ★경추 ROM «선 실측» — 공간 각도기(2026-11-16 신설 · 사용자 지시).
///   트리거로 <b>확인한</b> 동작의 각을, 사용자 자리에서 보아 <b>환자 반대편</b>에 그 구간의 면 하나로 다시 그린다.
///   - 굴곡·신전 = 옆면(법선 = 환자 좌우) · 측굴 = 앞뒤면(법선 = 환자 앞뒤) · 회전 = 수평면(법선 = 위)
///   - 언제 · 어느 탭을 그릴지는 <see cref="RomLineSession"/>이 정한다(평가는 6동작이 끝날 때까지 숨김). 이 클래스는 받은 값만 그린다.
///   ★값을 따로 계산하지 않는다 — 결과표와 같은 <see cref="RomLineResult"/>의 signedAngle을 같은 법선 둘레로 돌려 그린다(규칙 9: 미리보기는 같은 함수).
///
/// ★디자인 = VR ROM 각도기(<see cref="CervicalRomPlaneGauge"/>)와 통일(11-16 사용자 결정 · 교수님 지시).
///   모양만 옮겨 그린다(컴포넌트 재사용 아님 — 그쪽은 실시간 지침 · 자리 잡기 로직이 따로 있고 추나 13개 술기와 공유).
///   TrainingScene에 저장된 실제 값을 그대로 옮겼다(판 · 채움 끔 · ±90° 눈금 1/5/10° · 면별 눈금색 · 십자선 · 도달점 = 어두운 원 + 숫자).
///   - 능동·압박 색도 VR ROM 쪽(능동 하늘색 · 압박 붉은 주황 — 11-16 사용자 선택). ★그래서 이 각도기만 결과표 · 공간 선(능동 주황)과 색 뜻이 다르다.
///   - 지침(현재각)은 없다 — 확인한 값만 그린다. 최대각(초록) 선은 없다 — 선 실측 코드에 정상 범위 값이 없다.
///   - 길이 · 글자 크기는 VR ROM 반지름(0.36m) 기준 값을 반지름 비율로 줄이고 늘린다. 굵기는 그대로.
///   자리는 나타나는 순간 한 번 정하고 고정한다(사용자를 따라 돌지 않는다 — ui-fixed-to-scene). 매 프레임 하는 일은 글자를 눈 쪽으로 돌리는 것뿐이다(할당 없음).
/// </summary>
public class RomLineSpaceGauge
{
    // ── VR ROM 각도기(TrainingScene 저장값 · 11-16 읽음) ──────────────
    private const float RefRadius = 0.36f;                 // scaleRadius
    private const float ScaleSpan = 90f;                   // scaleSpan · mirrorScale = ±90
    private const float MajorStep = 10f, MinorStep = 5f, MicroStep = 1f;
    private const float MajorLen = 0.05f, MinorLen = 0.03f, MicroLen = 0.01f;
    private const float TickWidth = 0.002f;                // 주 ×1.35 · 미세 ×0.7
    private const float CrossWidth = 0.003f, CrossAlpha = 0.45f;
    private const float TickLabelSize = 0.2f, TickLabelOffset = 0.015f;
    private const float ReachedLen = 0.075f, ReachedWidth = 0.006f;
    private const float ReachedLabelSize = 0.2f, ReachedLabelOffset = 0.05f, DiscRadius = 0.03f;
    private static readonly Color SagittalColor = new Color(0.31f, 0.78f, 0.80f);     // 굴곡·신전
    private static readonly Color CoronalColor = new Color(0.96f, 0.63f, 0.36f);      // 측굴
    private static readonly Color TransverseColor = new Color(0.48f, 0.78f, 0.48f);   // 회전
    private static readonly Color ActiveReached = new Color(0.35f, 0.85f, 1f, 1f);
    private static readonly Color PassiveReached = new Color(1f, 0.45f, 0.35f, 1f);
    private static readonly Color DiscColor = new Color(0.09f, 0.10f, 0.13f, 0.78f);
    private const int DiscSegments = 32;
    private const int TickLabelCount = (int)(ScaleSpan * 2 / MajorStep) + 1;   // −90 … 90 → 19개

    private RomLineView v;
    private Transform root;
    private Mesh mesh;
    private readonly List<Vector3> verts = new List<Vector3>(2048);
    private readonly List<Color> cols = new List<Color>(2048);
    private readonly List<int> tris = new List<int>(3072);
    private readonly TextMeshPro[] tickLabels = new TextMeshPro[TickLabelCount];
    // 도달점 4개: [0] 동작0 능동 · [1] 동작0 압박 · [2] 동작1 능동 · [3] 동작1 압박
    private readonly TextMeshPro[] puckLabel = new TextMeshPro[4];
    private readonly Transform[] puckDisc = new Transform[4];
    private float k = 1f;   // 반지름 / RefRadius

    public bool Visible { get; private set; }
    public Vector3 Center { get; private set; }

    public void Build(RomLineView view, Transform parent)
    {
        v = view;
        root = new GameObject("공간 각도기").transform;
        root.SetParent(parent, false);

        var go = new GameObject("각도기 눈금");
        go.transform.SetParent(root, false);
        mesh = new Mesh { name = "[선실측] 공간 각도기" };
        mesh.MarkDynamic();
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = v.Mat;   // Sprites/Default — 꼭짓점 색을 그대로 쓴다(VR ROM과 같은 셰이더)
        mr.sortingOrder = 2;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        for (int i = 0; i < TickLabelCount; i++)
        {
            tickLabels[i] = v.MakeText(root, "", TickLabelSize, TextAlignmentOptions.Center, 5, v.BoldFont);   // 11-17 글꼴 KoPub B(크기 그대로)
            if (v.BoldFont == null) tickLabels[i].fontStyle = FontStyles.Bold;   // KoPub B가 없을 때만 합성 굵게
            tickLabels[i].rectTransform.sizeDelta = new Vector2(0.12f, 0.05f);
        }
        for (int i = 0; i < 4; i++)
        {
            puckDisc[i] = MakeDisc("도달점 배경", root);
            puckLabel[i] = v.MakeText(root, "", ReachedLabelSize, TextAlignmentOptions.Center, 6, v.BoldFont);
            if (v.BoldFont == null) puckLabel[i].fontStyle = FontStyles.Bold;
            puckLabel[i].color = (i % 2 == 0) ? ActiveReached : PassiveReached;
            puckLabel[i].rectTransform.sizeDelta = new Vector2(0.12f, 0.05f);
        }
        root.gameObject.SetActive(false);
    }

    /// <summary>도달점 숫자 뒤의 어두운 원판(VR ROM CreateDisc와 같은 꼴). 반지름 1 — 스케일로 크기를 준다.</summary>
    private Transform MakeDisc(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var dv = new List<Vector3>(DiscSegments + 2) { Vector3.zero };
        var dc = new List<Color>(DiscSegments + 2) { DiscColor };
        var dt = new List<int>(DiscSegments * 3);
        for (int i = 0; i <= DiscSegments; i++)
        {
            float r = i * Mathf.PI * 2f / DiscSegments;
            dv.Add(new Vector3(Mathf.Cos(r), Mathf.Sin(r), 0f));
            dc.Add(DiscColor);
        }
        for (int i = 1; i <= DiscSegments; i++) { dt.Add(0); dt.Add(i); dt.Add(i + 1); }
        var m = new Mesh { name = "[선실측] 도달점 원판" };
        m.SetVertices(dv);
        m.SetColors(dc);
        m.SetTriangles(dt, 0);
        m.RecalculateBounds();
        go.AddComponent<MeshFilter>().sharedMesh = m;
        var r2 = go.AddComponent<MeshRenderer>();
        r2.sharedMaterial = v.Mat;
        r2.sortingOrder = 4;   // 눈금 2 < 원판 4 < 글자 5 · 6
        r2.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r2.receiveShadows = false;
        return go.transform;
    }

    public void Hide()
    {
        if (!Visible) return;
        Visible = false;
        root.gameObject.SetActive(false);
    }

    /// <summary>
    /// 그린다. center = 각도기 중심(세션이 정한 반대편 자리) · normal = 그 면의 법선(결과를 낸 것과 같은 <see cref="RomLineMath.PlaneNormal"/>) ·
    /// zeroDir = 0° 선(면 위 단위벡터 — 첫 확인 동작의 중립선 방향) · tab = 0 굴곡·신전 / 1 측굴 / 2 회전(눈금 색) ·
    /// 동작 둘(같은 면의 두 동작)마다 능동 signedAngle · 압박 있음 여부 · 압박 signedAngle.
    /// 세션의 Refresh(값이 바뀔 때만)에서 부른다 — 매 프레임 아님.
    /// </summary>
    public void Show(Vector3 center, Vector3 normal, Vector3 zeroDir, float radius, int tab,
                     bool on0, float a0, bool p0On, float p0,
                     bool on1, float a1, bool p1On, float p1)
    {
        Center = center;
        if (!Visible) { Visible = true; root.gameObject.SetActive(true); }

        // 판 좌표: +Z = 법선 · +Y = 0°(중립) — 그러면 AngleAxis(a, normal) * zeroDir = 판 위 Dir(a)다(값을 낸 회전과 같은 함수).
        root.SetPositionAndRotation(center, Quaternion.LookRotation(normal, zeroDir));
        k = Mathf.Max(0.01f, radius) / RefRadius;

        Color plane = tab == 0 ? SagittalColor : tab == 1 ? CoronalColor : TransverseColor;
        Color tick = plane; tick.a = 0.95f;
        Color micro = plane; micro.a = 0.55f;    // 미세눈금은 옅게 — 촘촘해서 진하면 띠로 뭉친다(VR ROM)
        Color cross = plane; cross.a = CrossAlpha;

        verts.Clear(); cols.Clear(); tris.Clear();
        // 십자선 — 제일 밑에. 회전 중심이 보여야 각도가 읽힌다(VR ROM)
        for (int q = 0; q < 4; q++) Arm(q * 90f, radius, CrossWidth, cross);
        // 미세 → 보조 → 주 순으로 겹쳐 그린다(굵은 눈금이 위)
        for (float a = -ScaleSpan; a <= ScaleSpan + 0.001f; a += MicroStep)
        {
            if (OnStep(a, MinorStep)) continue;
            Tick(a, radius, MicroLen * k, TickWidth * 0.7f, micro);
        }
        for (float a = -ScaleSpan; a <= ScaleSpan + 0.001f; a += MinorStep)
        {
            if (OnStep(a, MajorStep)) continue;
            Tick(a, radius, MinorLen * k, TickWidth, tick);
        }
        for (float a = -ScaleSpan; a <= ScaleSpan + 0.001f; a += MajorStep)
            Tick(a, radius, MajorLen * k, TickWidth * 1.35f, tick);

        // 도달 마킹 — 눈금 바깥에서 안쪽으로 굵게(VR ROM AddReachedPair와 같은 꼴). 압박을 먼저 깔아 능동이 위로 온다.
        if (on0 && p0On) Tick(p0, radius, ReachedLen * k, ReachedWidth, PassiveReached);
        if (on1 && p1On) Tick(p1, radius, ReachedLen * k, ReachedWidth, PassiveReached);
        if (on0) Tick(a0, radius, ReachedLen * k, ReachedWidth, ActiveReached);
        if (on1) Tick(a1, radius, ReachedLen * k, ReachedWidth, ActiveReached);

        mesh.Clear();
        mesh.SetVertices(verts);
        mesh.SetColors(cols);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();

        // 주눈금 숫자 — 양쪽 다 절댓값(각도기지 좌표축이 아니다 · VR ROM)
        Color labelColor = plane; labelColor.a = 1f;
        for (int i = 0; i < TickLabelCount; i++)
        {
            float a = -ScaleSpan + i * MajorStep;
            TextMeshPro t = tickLabels[i];
            t.text = Mathf.Abs(a).ToString("F0");
            t.color = labelColor;
            t.fontSize = TickLabelSize * k;
            t.transform.localPosition = Dir(a) * (radius + TickLabelOffset * k);
        }

        Puck(0, on0, a0, radius);
        Puck(1, on0 && p0On, p0, radius);
        Puck(2, on1, a1, radius);
        Puck(3, on1 && p1On, p1, radius);
    }

    /// <summary>도달점 — 어두운 원 + 숫자(정수 · 접두 없음 · 능동/압박은 색으로 가른다 — VR ROM 08-28 사용자 지시).</summary>
    private void Puck(int i, bool show, float angle, float radius)
    {
        puckLabel[i].gameObject.SetActive(show);
        puckDisc[i].gameObject.SetActive(show);
        if (!show) return;
        puckLabel[i].text = Mathf.RoundToInt(Mathf.Abs(angle)) + "°";
        puckLabel[i].fontSize = ReachedLabelSize * k;
        Vector3 local = Dir(angle) * (radius + ReachedLabelOffset * k);
        puckLabel[i].transform.localPosition = local;
        puckDisc[i].localPosition = local;
        puckDisc[i].localScale = Vector3.one * (DiscRadius * k);
    }

    /// <summary>판 위 방향. 0° = +Y · 양의 각 = +Z(법선) 둘레 반시계 = Quaternion.AngleAxis(a, normal) * zeroDir.</summary>
    private static Vector3 Dir(float deg)
    {
        float r = deg * Mathf.Deg2Rad;
        return new Vector3(-Mathf.Sin(r), Mathf.Cos(r), 0f);
    }

    private static bool OnStep(float degrees, float step) => step > 0f && Mathf.Abs(degrees % step) < 0.001f;

    /// <summary>눈금 하나 — 반지름 r에서 안쪽으로 length만큼(VR ROM AddTick).</summary>
    private void Tick(float deg, float r, float length, float width, Color c)
    {
        Vector3 d = Dir(deg);
        Vector3 side = new Vector3(d.y, -d.x, 0f) * (width * 0.5f);
        Vector3 outer = d * r, inner = d * (r - length);
        Quad(inner - side, outer - side, outer + side, inner + side, c);
    }

    /// <summary>중심에서 뻗는 팔 — 십자선(VR ROM AddArm).</summary>
    private void Arm(float deg, float length, float width, Color c)
    {
        Vector3 d = Dir(deg);
        Vector3 side = new Vector3(d.y, -d.x, 0f) * (width * 0.5f);
        Vector3 tip = d * length;
        Quad(-side, tip - side, tip + side, side, c);
    }

    private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
    {
        int i = verts.Count;
        verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
        cols.Add(col); cols.Add(col); cols.Add(col); cols.Add(col);
        tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
    }

    /// <summary>글자 · 원판을 눈 쪽으로 돌린다(뒤집혀 읽히지 않게). 원판은 글자 바로 뒤(눈에서 먼 쪽)로. 할당 없음.</summary>
    public void Tick(Transform eye)
    {
        if (!Visible || eye == null) return;
        for (int i = 0; i < TickLabelCount; i++) Face(tickLabels[i].transform, eye);
        for (int i = 0; i < 4; i++)
        {
            if (!puckLabel[i].gameObject.activeSelf) continue;
            Transform t = puckLabel[i].transform;
            Face(t, eye);
            puckDisc[i].rotation = t.rotation;
            puckDisc[i].position = t.position + t.forward * 0.002f;
        }
    }

    private static void Face(Transform t, Transform eye)
    {
        Vector3 d = t.position - eye.position;
        if (d.sqrMagnitude > 1e-6f) t.rotation = Quaternion.LookRotation(d, Vector3.up);
    }
}
