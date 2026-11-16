using TMPro;
using UnityEngine;

/// <summary>
/// ★경추 ROM «선 실측» — 공간 각도기(2026-11-16 신설 · 사용자 지시).
///   트리거로 <b>확인한</b> 동작의 각을, 사용자 자리에서 보아 <b>환자 반대편</b>에 그 구간의 면 하나로 다시 그린다.
///   - 굴곡·신전 = 옆면(법선 = 환자 좌우) · 측굴 = 앞뒤면(법선 = 환자 앞뒤) · 회전 = 수평면(법선 = 위)
///   - 언제 · 어느 탭을 그릴지는 <see cref="RomLineSession"/>이 정한다(평가는 6동작이 끝날 때까지 숨김). 이 클래스는 받은 값만 그린다.
///   ★값을 따로 계산하지 않는다 — 결과표와 같은 <see cref="RomLineResult"/>의 signedAngle을 같은 법선 둘레로 돌려 그린다(규칙 9: 미리보기는 같은 함수).
///   ★디자인은 아직 미정이다(XR-ROM 반원 약도 vs VR ROM A형 — 11-16 사용자 확인 대기). 그리는 부분은 이 파일에만 있다 — 디자인이 정해지면 여기만 바꾼다.
///   자리는 나타나는 순간 한 번 정하고 고정한다(사용자를 따라 돌지 않는다 — ui-fixed-to-scene). 매 프레임 하는 일은 글자를 눈 쪽으로 돌리는 것뿐이다(할당 없음).
/// </summary>
public class RomLineSpaceGauge
{
    private const int RimSegments = 40;          // 테두리 호(±RimHalf°)
    private const float RimHalf = 100f;          // 0° 선 양쪽으로 그리는 테두리 범위 — 벽면 각도기의 GaugeMaxDraw(100°)와 같게
    private const int ArcSegments = 24;          // 0° 선 → 능동 선 호
    private const float ArcRadiusRatio = 0.42f;  // 호 반지름 = 각도기 반지름 × 이 값
    private const float PressureLenRatio = 0.92f;
    private const float LabelGap = 0.045f;       // 선 끝에서 글자까지(m)
    private const float GaugeLineWidth = 0.004f; // 공간 선보다 굵게(멀리 반대편에 뜬다)
    private const float TickLong = 0.025f, TickShort = 0.012f;
    private static readonly Color RimColor = new Color(0.72f, 0.76f, 0.82f, 0.55f);

    private RomLineView v;
    private Transform root;
    private LineRenderer rim, zero, ticks;
    private readonly LineRenderer[] act = new LineRenderer[2], prs = new LineRenderer[2], arc = new LineRenderer[2];
    private readonly TextMeshPro[] label = new TextMeshPro[2];
    private TextMeshPro zeroLabel;
    private readonly Vector3[] arcBuf = new Vector3[ArcSegments + 1];
    private readonly Vector3[] rimBuf = new Vector3[RimSegments + 1];
    private readonly Vector3[] tickBuf = new Vector3[21 * 3];

    public bool Visible { get; private set; }
    public Vector3 Center { get; private set; }

    public void Build(RomLineView view, Transform parent)
    {
        v = view;
        root = new GameObject("공간 각도기").transform;
        root.SetParent(parent, false);
        rim = Line("각도기 테두리", RimColor, RimSegments + 1);
        ticks = Line("각도기 눈금", RimColor, tickBuf.Length);
        zero = Line("각도기 0°(중립)", RomLineView.NeutralColor, 2);
        for (int i = 0; i < 2; i++)
        {
            act[i] = Line("각도기 능동", RomLineView.ActiveColor, 2);
            prs[i] = Line("각도기 압박", RomLineView.PressureColor, 2);
            arc[i] = Line("각도기 호", RomLineView.ActiveColor, ArcSegments + 1);
            label[i] = RomLineView.Outlined(v.MakeText(root, "", 0.30f, TextAlignmentOptions.Center, 5));
            label[i].rectTransform.sizeDelta = new Vector2(0.40f, 0.06f);
        }
        zeroLabel = RomLineView.Outlined(v.MakeText(root, "중립 0°", 0.22f, TextAlignmentOptions.Center, 5));
        zeroLabel.rectTransform.sizeDelta = new Vector2(0.30f, 0.05f);
        root.gameObject.SetActive(false);
    }

    private LineRenderer Line(string name, Color c, int count)
    {
        LineRenderer lr = v.MakeLine(name, c, count);
        lr.transform.SetParent(root, true);
        lr.startWidth = lr.endWidth = GaugeLineWidth;
        return lr;
    }

    public void Hide()
    {
        if (!Visible) return;
        Visible = false;
        root.gameObject.SetActive(false);
    }

    /// <summary>
    /// 그린다. center = 각도기 중심(세션이 정한 반대편 자리) · normal = 그 면의 법선(결과를 낸 것과 같은 <see cref="RomLineMath.PlaneNormal"/>) ·
    /// zeroDir = 0° 선(면 위 단위벡터 — 첫 확인 동작의 중립선 방향) · 동작 둘(같은 면의 두 동작)마다 signedAngle · 있음 여부 · 이름.
    /// </summary>
    public void Show(Vector3 center, Vector3 normal, Vector3 zeroDir, float radius,
                     bool on0, string name0, float a0, bool p0On, float p0, string text0,
                     bool on1, string name1, float a1, bool p1On, float p1, string text1)
    {
        Center = center;
        if (!Visible) { Visible = true; root.gameObject.SetActive(true); }

        // 테두리 · 눈금(10°마다, 30°마다 길게)
        for (int i = 0; i <= RimSegments; i++)
        {
            float deg = -RimHalf + 2f * RimHalf * i / RimSegments;
            rimBuf[i] = center + Quaternion.AngleAxis(deg, normal) * zeroDir * radius;
        }
        rim.SetPositions(rimBuf);
        rim.enabled = true;
        int k = 0;
        for (int d = -100; d <= 100; d += 10)
        {
            Vector3 dir = Quaternion.AngleAxis(d, normal) * zeroDir;
            float len = d % 30 == 0 ? TickLong : TickShort;
            Vector3 outer = center + dir * radius, inner = center + dir * (radius - len);
            tickBuf[k++] = outer; tickBuf[k++] = inner; tickBuf[k++] = outer;   // 한 줄로 이어 그린다(갔다 돌아옴)
        }
        ticks.SetPositions(tickBuf);
        ticks.enabled = true;

        zero.SetPosition(0, center);
        zero.SetPosition(1, center + zeroDir * radius);
        zero.enabled = true;
        zeroLabel.transform.position = center + zeroDir * (radius + LabelGap);

        Motion(0, on0, a0, p0On, p0, text0, center, normal, zeroDir, radius);
        Motion(1, on1, a1, p1On, p1, text1, center, normal, zeroDir, radius);
    }

    private void Motion(int i, bool on, float signedA, bool pOn, float signedP, string text, Vector3 c, Vector3 n, Vector3 z, float r)
    {
        act[i].enabled = on;
        arc[i].enabled = on;
        label[i].gameObject.SetActive(on);
        prs[i].enabled = on && pOn;
        if (!on) return;

        Vector3 ad = Quaternion.AngleAxis(signedA, n) * z;
        act[i].SetPosition(0, c);
        act[i].SetPosition(1, c + ad * r);
        for (int s = 0; s <= ArcSegments; s++)
            arcBuf[s] = c + Quaternion.AngleAxis(signedA * s / ArcSegments, n) * z * (r * ArcRadiusRatio);
        arc[i].SetPositions(arcBuf);
        if (pOn)
        {
            Vector3 pd = Quaternion.AngleAxis(signedP, n) * z;
            prs[i].SetPosition(0, c);
            prs[i].SetPosition(1, c + pd * (r * PressureLenRatio));
        }
        label[i].text = text;
        label[i].transform.position = c + ad * (r + LabelGap * 1.6f);
    }

    /// <summary>글자를 눈 쪽으로 돌린다(뒤집혀 읽히지 않게). 할당 없음.</summary>
    public void Tick(Transform eye)
    {
        if (!Visible || eye == null) return;
        Face(zeroLabel.transform, eye);
        for (int i = 0; i < 2; i++) if (label[i].gameObject.activeSelf) Face(label[i].transform, eye);
    }

    private static void Face(Transform t, Transform eye)
    {
        Vector3 d = t.position - eye.position;
        if (d.sqrMagnitude > 1e-6f) t.rotation = Quaternion.LookRotation(d, Vector3.up);
    }
}
