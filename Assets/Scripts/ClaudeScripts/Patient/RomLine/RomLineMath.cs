using UnityEngine;

/// <summary>
/// ★경추 ROM «선 실측» — 계산(2026-10-01 신설, 9/30 교수님 피드백으로 바뀐 방식).
///
/// 방식: 동작마다 <b>같은 두 점을 세 번</b>(중립·능동·압박) 찍어 직선을 만들고,
///       중립 기준선에서 얼마나 돌아갔는지를 각으로 낸다.
/// ★찍은 점은 3차원 좌표라 두 직선은 공간에서 엇갈릴 수 있다 → 동작에 맞는 <b>환자 기준 면</b> 위로 옮긴 뒤
///   그 면 안에서 각을 잰다. 면 위에서는 두 선이 반드시 만나고(평행 제외) 그 점이 각도기 축이다.
///
/// ★입력·표시와 분리한 순수 계산이다. 검증은 메뉴 «GuideChuna/선 실측 계산 검증» 또는 브리지 <c>romline-verify</c>가
///   <b>이 함수들을 그대로</b> 부른다(미리보기가 따로 계산하면 거짓말을 한다 — CLAUDE.md 규칙 9).
/// </summary>
public enum RomLineMotion
{
    Flexion = 0,        // 굴곡
    Extension = 1,      // 신전
    LateralRight = 2,   // 우측굴
    LateralLeft = 3,    // 좌측굴
    RotationRight = 4,  // 우회전
    RotationLeft = 5,   // 좌회전
}

/// <summary>환자 기준 방향 셋. 중립을 찍는 순간 한 번 만들고 그 뒤로 바꾸지 않는다.</summary>
public struct RomLineFrame
{
    public bool valid;
    public Vector3 up;        // 위 = 중력 반대
    public Vector3 right;     // 환자의 오른쪽 = 왼쪽 Tr → 오른쪽 Tr(수평 성분)
    public Vector3 forward;   // 환자의 앞 = Gl이 있는 쪽
    public bool sidesSwapped; // 양쪽 Tr을 바꿔 찍은 것으로 보여 좌우를 뒤집었다
}

public struct RomLineResult
{
    public bool valid;
    /// <summary>중립 기준선 → 측정선의 각(도, 크기만).</summary>
    public float angle;
    /// <summary>부호 있는 각 — 면 법선 둘레(+는 <see cref="RomLineMath.PositiveMeans"/> 참고).</summary>
    public float signedAngle;
    /// <summary>면 위에서 두 선이 만나는 점(월드). <see cref="pivotOnLines"/>가 false면 대신 세운 자리다.</summary>
    public Vector3 pivot;
    /// <summary>true = 실제 교점이다. false = 평행에 가깝거나 너무 멀어 중립선의 첫 점에 대신 세웠다.</summary>
    public bool pivotOnLines;
    /// <summary>면 위로 옮긴 중립선·측정선의 방향(단위).</summary>
    public Vector3 neutralDir, markDir;
    /// <summary>면 위로 옮긴 네 점(월드).</summary>
    public Vector3 neutralA, neutralB, markA, markB;
    /// <summary>면 위로 옮기면서 버려진 양 — 각 선이 면에서 벗어난 각(도).
    /// ★값 자체는 뜻이 없다(Tr ~ Gl 선은 귀가 정중선 옆에 있어 원래 30° 넘게 벗어나 있다 — 10-01 검증기로 확인).
    ///   뜻이 있는 것은 <b>중립선 대비 변화</b>다: 크면 다른 동작이 섞인 것이다.</summary>
    public float neutralOffPlane, markOffPlane;
}

/// <summary>면으로 옮기지 않고 3차원에서 그대로 잰 값(10-01, 비교용 «B 방식»). 환자 기준 방향이 필요 없다.</summary>
public struct RomLineDirect
{
    public bool valid;
    /// <summary>두 선의 방향이 벌어진 각(도).</summary>
    public float angle;
    /// <summary>두 선이 공간에서 가장 가까운 거리(m) — 0이면 실제로 만난다.</summary>
    public float gap;
    /// <summary>가장 가까운 두 점의 중간.</summary>
    public Vector3 pivot;
}

public static class RomLineMath
{
    /// <summary>교점이 중립선 첫 점에서 이보다 멀면(m) 각도기를 첫 점에 대신 세운다 — 각이 작으면 교점이 달아난다.</summary>
    public const float MaxPivotDistance = 0.5f;

    /// <summary>
    /// 환자 기준 방향을 만든다. 위아래 = 중력, 좌우 = 왼쪽 Tr → 오른쪽 Tr, 앞 = Gl 쪽.
    /// </summary>
    public static RomLineFrame BuildFrame(Vector3 trRight, Vector3 trLeft, Vector3 gl)
    {
        var f = new RomLineFrame { up = Vector3.up };

        // 지움: 두 귀의 높이 차 → 귀 높이가 달라도 좌우 축은 수평이다(의도한 것 — 위아래는 중력이 맡는다).
        Vector3 r = Vector3.ProjectOnPlane(trRight - trLeft, f.up);
        if (r.sqrMagnitude < 1e-6f) return f;   // 두 Tr이 같은 자리(또는 수직으로 겹침) — 기준을 못 만든다
        f.right = r.normalized;

        // 앞뒤는 위·좌우 둘에 직각이다(독립으로 정할 근거가 없다 — 점이 머리에만 있다).
        f.forward = Vector3.Cross(f.right, f.up).normalized;

        // ★앞인지 뒤인지는 추론하지 않고 Gl로 잰다. Gl이 뒤쪽에 있으면 양쪽 Tr을 바꿔 찍은 것이다.
        Vector3 mid = (trRight + trLeft) * 0.5f;
        if (Vector3.Dot(gl - mid, f.forward) < 0f)
        {
            f.right = -f.right;
            f.forward = -f.forward;
            f.sidesSwapped = true;
        }
        f.valid = true;
        return f;
    }

    /// <summary>그 동작을 읽는 면의 법선. 굴곡·신전 = 좌우 / 측굴 = 앞뒤 / 회전 = 위아래 성분을 버린다.</summary>
    public static Vector3 PlaneNormal(RomLineMotion motion, RomLineFrame f)
    {
        switch (motion)
        {
            case RomLineMotion.Flexion:
            case RomLineMotion.Extension: return f.right;
            case RomLineMotion.LateralRight:
            case RomLineMotion.LateralLeft: return f.forward;
            default: return f.up;
        }
    }

    /// <summary>
    /// 부호 있는 각의 +가 뜻하는 방향. ★계산으로 정한 것이 아니라 검증기(romline-verify)가 실제 회전을 넣어 확인한다.
    /// 굴곡·신전 면: + = 굴곡 / 측굴 면: + = 좌측굴 / 회전 면: + = 우회전.
    /// </summary>
    public static RomLineMotion PositiveMeans(RomLineMotion motion)
    {
        switch (motion)
        {
            case RomLineMotion.Flexion:
            case RomLineMotion.Extension: return RomLineMotion.Flexion;
            case RomLineMotion.LateralRight:
            case RomLineMotion.LateralLeft: return RomLineMotion.LateralLeft;
            default: return RomLineMotion.RotationRight;
        }
    }

    /// <summary>측정된 부호가 그 동작의 방향과 맞는가(굴곡 단계에서 신전 쪽으로 움직였는지 등).</summary>
    public static bool DirectionMatches(RomLineMotion motion, float signedAngle)
    {
        bool positive = signedAngle >= 0f;
        return positive == (PositiveMeans(motion) == motion);
    }

    /// <summary>
    /// 중립선(nA→nB)과 측정선(mA→mB)을 면 위로 옮겨 각과 교점을 낸다.
    /// ★두 선의 점 순서는 같아야 한다(둘 다 Tr → Gl처럼). 순서가 다르면 각이 180° 뒤집힌다.
    /// </summary>
    public static RomLineResult Measure(Vector3 nA, Vector3 nB, Vector3 mA, Vector3 mB, Vector3 normal)
    {
        var r = new RomLineResult();
        Vector3 n = normal.normalized;
        if (n.sqrMagnitude < 0.5f) return r;

        Vector3 nRaw = nB - nA, mRaw = mB - mA;
        if (nRaw.sqrMagnitude < 1e-8f || mRaw.sqrMagnitude < 1e-8f) return r;   // 같은 자리를 두 번 찍었다

        // 지움: 면 법선 방향 성분 → 그쪽으로 움직인 만큼은 각에 안 들어온다(다른 동작이 섞인 양이다).
        //   버린 양은 neutralOffPlane·markOffPlane으로 따로 남겨 «못 보게 된 것»을 볼 수 있게 한다.
        r.neutralOffPlane = Mathf.Asin(Mathf.Clamp01(Mathf.Abs(Vector3.Dot(nRaw.normalized, n)))) * Mathf.Rad2Deg;
        r.markOffPlane = Mathf.Asin(Mathf.Clamp01(Mathf.Abs(Vector3.Dot(mRaw.normalized, n)))) * Mathf.Rad2Deg;

        // 면은 중립선의 첫 점을 지난다. 네 점을 모두 그 면 위로 옮긴다.
        r.neutralA = nA;
        r.neutralB = OntoPlane(nB, nA, n);
        r.markA = OntoPlane(mA, nA, n);
        r.markB = OntoPlane(mB, nA, n);

        Vector3 nd = r.neutralB - r.neutralA, md = r.markB - r.markA;
        if (nd.sqrMagnitude < 1e-8f || md.sqrMagnitude < 1e-8f) return r;   // 선이 면에 수직이라 점으로 눌렸다
        r.neutralDir = nd.normalized;
        r.markDir = md.normalized;

        r.signedAngle = Vector3.SignedAngle(r.neutralDir, r.markDir, n);
        r.angle = Mathf.Abs(r.signedAngle);

        // 면 안 2차원 좌표: u = 중립선 방향, v = 면 안에서 u에 직각.
        Vector3 u = r.neutralDir, v = Vector3.Cross(n, u);
        Vector3 rel = r.markA - r.neutralA;
        float mx = Vector3.Dot(rel, u), my = Vector3.Dot(rel, v);
        float dx = Vector3.Dot(r.markDir, u), dy = Vector3.Dot(r.markDir, v);

        r.pivot = r.neutralA;
        if (Mathf.Abs(dy) > 1e-4f)
        {
            float s = -my / dy;                 // 측정선이 중립선(v = 0)과 만나는 자리
            float px = mx + s * dx;
            if (Mathf.Abs(px) <= MaxPivotDistance)
            {
                r.pivot = r.neutralA + u * px;
                r.pivotOnLines = true;
            }
        }
        r.valid = true;
        return r;
    }

    /// <summary>
    /// 두 직선을 면으로 옮기지 않고 3차원에서 그대로 잰다. ★선이 회전축에 직각이 아니면 실제 회전보다 작게 나온다
    ///   (선의 축 방향 성분은 돌아도 그대로라서) — 검증기 참고 줄에서 크기를 볼 수 있다.
    /// </summary>
    public static RomLineDirect MeasureDirect(Vector3 nA, Vector3 nB, Vector3 mA, Vector3 mB)
    {
        var r = new RomLineDirect();
        Vector3 d1 = nB - nA, d2 = mB - mA;
        if (d1.sqrMagnitude < 1e-8f || d2.sqrMagnitude < 1e-8f) return r;
        r.angle = Vector3.Angle(d1, d2);

        Vector3 w = nA - mA;
        float a = Vector3.Dot(d1, d1), b = Vector3.Dot(d1, d2), c = Vector3.Dot(d2, d2);
        float d = Vector3.Dot(d1, w), e = Vector3.Dot(d2, w);
        float denom = a * c - b * b;
        if (denom < 1e-10f)
        {
            // 평행 — 가장 가까운 점이 정해지지 않는다. 중립선 첫 점에서 측정선까지의 거리를 쓴다.
            r.gap = Vector3.Cross(w, d2.normalized).magnitude;
            r.pivot = nA;
        }
        else
        {
            Vector3 p1 = nA + d1 * ((b * e - c * d) / denom);
            Vector3 p2 = mA + d2 * ((a * e - b * d) / denom);
            r.gap = Vector3.Distance(p1, p2);
            r.pivot = (p1 + p2) * 0.5f;
        }
        r.valid = true;
        return r;
    }

    private static Vector3 OntoPlane(Vector3 p, Vector3 planePoint, Vector3 n)
    {
        return p - n * Vector3.Dot(p - planePoint, n);
    }
}
