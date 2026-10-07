// ★[선 실측 계산 검증] 2026-10-01 신설
// 부호·좌우·앞뒤를 추론으로 맞히지 않으려고 만든 «재는 도구»다(CLAUDE.md 규칙 9).
// 가상의 머리에 실제 회전을 넣고, RomLineMath가 <b>런타임과 같은 함수로</b> 그 각을 되돌려 주는지 본다.
//   ① 넣은 회전이 해부학적으로 그 동작이 맞는지부터 확인한다(굴곡이면 Gl이 내려가는가 등) — 축 부호를 믿지 않는다
//   ② 잰 각이 넣은 각과 같은가 · 방향 판정이 맞는가
//   ③ 양쪽 Tr을 바꿔 찍으면 잡아내는가
// 메뉴 «GuideChuna/선 실측 계산 검증» 또는 브리지 `bridge.py romline-verify`.

using System.Text;
using UnityEditor;
using UnityEngine;

public static class RomLineVerify
{
    private struct Head
    {
        public Vector3 trR, trL, gl, gn, top;
    }

    [MenuItem("GuideChuna/선 실측 계산 검증")]
    private static void Menu()
    {
        Debug.Log(Run());
    }

    public static string Run()
    {
        var sb = new StringBuilder();
        int fail = 0;
        float[] yaws = { 0f, 37f, 200f };
        float[] angles = { 5f, 30f, 60f };

        foreach (float yaw in yaws)
        {
            // 환자의 진짜 방향(검증의 정답). 환자가 yaw만큼 돌아 앉아 있다.
            Vector3 U = Vector3.up;
            Vector3 F = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3 R = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
            Vector3 center = new Vector3(0.4f, 1.25f, -0.3f);
            Vector3 neck = center - U * 0.10f;
            Head n = MakeHead(center, R, U, F);

            RomLineFrame f = RomLineMath.BuildFrame(n.trR, n.trL, n.gl);
            bool frameOk = f.valid && !f.sidesSwapped && Vector3.Angle(f.right, R) < 0.01f
                           && Vector3.Angle(f.forward, F) < 0.01f && Vector3.Angle(f.up, U) < 0.01f;
            fail += Line(sb, frameOk, $"환자 방향 {yaw:F0}° — 기준 방향(오른쪽·앞·위)이 정답과 같다");

            RomLineFrame sw = RomLineMath.BuildFrame(n.trL, n.trR, n.gl);
            bool swOk = sw.valid && sw.sidesSwapped && Vector3.Angle(sw.right, R) < 0.01f && Vector3.Angle(sw.forward, F) < 0.01f;
            fail += Line(sb, swOk, $"환자 방향 {yaw:F0}° — 양쪽 Tr을 바꿔 찍으면 알아채고 바로잡는다");

            for (int m = 0; m < 6; m++)
            {
                var motion = (RomLineMotion)m;
                foreach (float deg in angles)
                {
                    Quaternion q = Rotation(motion, deg, R, U, F);
                    Head h = Rotate(n, neck, q);

                    // ① 넣은 회전이 정말 그 동작인가 — 몸의 결과로 확인한다.
                    bool anatomy = Anatomy(motion, n, h, R, U);

                    PickLine(motion, n, out Vector3 nA, out Vector3 nB);
                    PickLine(motion, h, out Vector3 mA, out Vector3 mB);
                    RomLineResult r = RomLineMath.Measure(nA, nB, mA, mB, RomLineMath.PlaneNormal(motion, f));

                    bool ok = anatomy && r.valid && Mathf.Abs(r.angle - deg) < 0.02f
                              && RomLineMath.DirectionMatches(motion, r.signedAngle)
                              && !RomLineMath.DirectionMatches(Opposite(motion), r.signedAngle);
                    // 교점이 실제로 두 선 위에 있는가(평행·먼 교점으로 대신 세운 경우는 뺀다).
                    if (ok && r.pivotOnLines)
                        ok = DistToLine(r.pivot, r.neutralA, r.neutralDir) < 1e-4f && DistToLine(r.pivot, r.markA, r.markDir) < 1e-4f;

                    fail += Line(sb, ok, $"환자 {yaw:F0}° · {Name(motion)} {deg:F0}° → 잰 값 {r.angle:F2}° " +
                                         $"(부호 {r.signedAngle:+0.0;-0.0}) · 몸 확인 {(anatomy ? "O" : "★X")} · " +
                                         $"교점 {(r.pivotOnLines ? "선 위" : "대신 세움")}");

                    // 회전은 기준(양쪽 Tr) 없이도 같은 값·같은 방향 판정이 나와야 한다(10-07 — 면이 바닥이라 중력만으로 정해진다).
                    //   런타임이 기준을 건너뛰었을 때 넘기는 것과 같은 «빈 기준»을 넣는다.
                    if (!RomLineMath.NeedsReference(motion))
                    {
                        RomLineResult g = RomLineMath.Measure(nA, nB, mA, mB, RomLineMath.PlaneNormal(motion, default));
                        bool gOk = g.valid && Mathf.Abs(g.angle - deg) < 0.02f && Mathf.Abs(g.signedAngle - r.signedAngle) < 0.001f
                                   && RomLineMath.DirectionMatches(motion, g.signedAngle);
                        fail += Line(sb, gOk, $"환자 {yaw:F0}° · {Name(motion)} {deg:F0}° · 기준 없이(바닥면) → 잰 값 {g.angle:F2}° (부호 {g.signedAngle:+0.0;-0.0})");
                    }
                }
            }
        }

        // ── 참고(판정 없음): 다른 동작이 섞이면 값과 «면 밖 벗어남»이 어떻게 나오나 ──
        {
            Vector3 U = Vector3.up, F = Vector3.forward, R = Vector3.right;
            Vector3 center = new Vector3(0f, 1.25f, 0f), neck = center - U * 0.10f;
            Head n = MakeHead(center, R, U, F);
            RomLineFrame f = RomLineMath.BuildFrame(n.trR, n.trL, n.gl);

            Quaternion q = Quaternion.AngleAxis(10f, U) * Quaternion.AngleAxis(30f, R);   // 굴곡 30° 뒤 우회전 10°
            Head h = Rotate(n, neck, q);
            RomLineResult r = RomLineMath.Measure(n.trR, n.gl, h.trR, h.gl, RomLineMath.PlaneNormal(RomLineMotion.Flexion, f));
            sb.AppendLine($"참고  굴곡 30° + 우회전 10°가 섞임 → 굴곡으로 잰 값 {r.angle:F1}° · 면 밖 각 {r.neutralOffPlane:F1}° → {r.markOffPlane:F1}° (변화 {r.markOffPlane - r.neutralOffPlane:+0.0;-0.0}°)");

            // «직접»(면으로 옮기지 않고 3차원에서 잰 값)이 실제 회전과 얼마나 다른가 — 동작마다 30°를 넣어 본다.
            for (int m = 0; m < 6; m++)
            {
                var motion = (RomLineMotion)m;
                Head hm = Rotate(n, neck, Rotation(motion, 30f, R, U, F));
                PickLine(motion, n, out Vector3 a0, out Vector3 b0);
                PickLine(motion, hm, out Vector3 a1, out Vector3 b1);
                RomLineResult onPlane = RomLineMath.Measure(a0, b0, a1, b1, RomLineMath.PlaneNormal(motion, f));
                RomLineDirect dr = RomLineMath.MeasureDirect(a0, b0, a1, b1);
                sb.AppendLine($"참고  {Name(motion)} 30° → 면 위 {onPlane.angle:F1}° · 직접(3차원) {dr.angle:F1}° · 두 선 엇갈림 {dr.gap * 100f:F1}cm");
            }

            // 10-01 사람에게 찍은 굴곡(로그 좌표 그대로) — 같은 점에서 두 방식이 얼마나 다른가.
            {
                var nTr = new Vector3(-0.158f, 1.096f, -0.103f); var nGl = new Vector3(-0.046f, 1.155f, -0.094f);
                var aTr = new Vector3(-0.122f, 1.062f, -0.118f); var aGl = new Vector3(-0.010f, 1.066f, -0.109f);
                var pTr = new Vector3(-0.103f, 1.049f, -0.133f); var pGl = new Vector3(0.017f, 1.031f, -0.112f);
                sb.AppendLine($"참고  10-01 사람 굴곡(면 위 31.1° / 46.3°로 나온 점들) → 직접 능동 {RomLineMath.MeasureDirect(nTr, nGl, aTr, aGl).angle:F1}° · " +
                              $"압박 {RomLineMath.MeasureDirect(nTr, nGl, pTr, pGl).angle:F1}°");
            }

            // 한 점을 5mm 잘못 찍었을 때(굴곡 30°, Gl을 선에 직각으로 5mm).
            h = Rotate(n, neck, Quaternion.AngleAxis(30f, R));
            Vector3 dir = (h.gl - h.trR).normalized;
            Vector3 side = Vector3.Cross(R, dir).normalized;
            r = RomLineMath.Measure(n.trR, n.gl, h.trR, h.gl + side * 0.005f, RomLineMath.PlaneNormal(RomLineMotion.Flexion, f));
            sb.AppendLine($"참고  굴곡 30°에서 Gl을 5mm 벗어나게 찍음 → {r.angle:F1}° (Tr~Gl {Vector3.Distance(n.trR, n.gl) * 100f:F1}cm인 가상 머리)");

            // 작은 각 — 교점이 멀어지는지.
            h = Rotate(n, neck, Quaternion.AngleAxis(1f, R));
            r = RomLineMath.Measure(n.trR, n.gl, h.trR, h.gl, RomLineMath.PlaneNormal(RomLineMotion.Flexion, f));
            sb.AppendLine($"참고  굴곡 1° → {r.angle:F2}° · 교점 {(r.pivotOnLines ? $"선 위(첫 점에서 {Vector3.Distance(r.pivot, n.trR) * 100f:F0}cm)" : "멀어서 첫 점에 대신 세움")}");
        }

        sb.Insert(0, (fail == 0 ? "선 실측 계산 검증 — 전부 통과" : $"선 실측 계산 검증 — ★실패 {fail}건") + "\n");
        return sb.ToString();
    }

    /// <summary>
    /// 컨트롤러 모형에서 «맨 앞»이 어디인지 잰다(2026-10-01) — 찍는 점을 컨트롤러 앞면에 붙이려고.
    /// 열린 씬의 OVRControllerHelper 아래 모형마다, 모형 뿌리 좌표로 z가 가장 큰 꼭짓점을 낸다.
    /// ★모형 뿌리가 OVRInput의 컨트롤러 자리와 같다는 전제다(화면에서 노란 점으로 확인할 것).
    /// </summary>
    public static string ControllerFront()
    {
        var sb = new StringBuilder();
        var helpers = Object.FindObjectsByType<OVRControllerHelper>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        sb.AppendLine($"OVRControllerHelper {helpers.Length}개");
        var baked = new Mesh();
        foreach (OVRControllerHelper h in helpers)
        {
            Transform rootT = h.transform;
            sb.AppendLine($"■ {rootT.parent.parent.name} / {rootT.name}");
            foreach (SkinnedMeshRenderer smr in rootT.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null) continue;
                smr.BakeMesh(baked);
                Vector3[] v = baked.vertices;
                if (v.Length == 0) continue;
                Vector3 best = Vector3.zero, min = Vector3.one * 99f, max = Vector3.one * -99f;
                float bestZ = -99f;
                for (int i = 0; i < v.Length; i++)
                {
                    Vector3 p = rootT.InverseTransformPoint(smr.transform.TransformPoint(v[i]));
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                    if (p.z > bestZ) { bestZ = p.z; best = p; }
                }
                // 대조용 — 렌더러가 갖고 있는 경계 상자(뼈대 기준)를 같은 좌표로 옮긴 것. 구운 꼭짓점과 크기가 다르면 굽기가 틀린 것이다.
                Transform bone = smr.rootBone != null ? smr.rootBone : smr.transform;
                Bounds lb = smr.localBounds;
                Vector3 bmin = Vector3.one * 99f, bmax = Vector3.one * -99f;
                for (int c = 0; c < 8; c++)
                {
                    var corner = lb.center + Vector3.Scale(lb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    Vector3 q = rootT.InverseTransformPoint(bone.TransformPoint(corner));
                    bmin = Vector3.Min(bmin, q);
                    bmax = Vector3.Max(bmax, q);
                }
                sb.AppendLine($"  {"",-44} 경계상자 x {bmin.x * 100f:F1}~{bmax.x * 100f:F1} y {bmin.y * 100f:F1}~{bmax.y * 100f:F1} z {bmin.z * 100f:F1}~{bmax.z * 100f:F1} · " +
                              $"배율 {smr.transform.lossyScale.x:F2} · 뼈대 {bone.name} · 켜짐 {smr.gameObject.activeInHierarchy}");
                sb.AppendLine($"  {smr.name,-44} 맨 앞 ({best.x * 100f:F1}, {best.y * 100f:F1}, {best.z * 100f:F1})cm · " +
                              $"범위 x {min.x * 100f:F1}~{max.x * 100f:F1} y {min.y * 100f:F1}~{max.y * 100f:F1} z {min.z * 100f:F1}~{max.z * 100f:F1}");
            }
        }
        Object.DestroyImmediate(baked);
        return sb.ToString();
    }

    private static int Line(StringBuilder sb, bool ok, string text)
    {
        sb.Append(ok ? "통과  " : "★실패 ").AppendLine(text);
        return ok ? 0 : 1;
    }

    // 가상의 머리(m). 귀 높이를 6mm 다르게 둬 «좌우 축은 수평 성분만 쓴다»가 실제로 도는지 본다.
    private static Head MakeHead(Vector3 c, Vector3 R, Vector3 U, Vector3 F)
    {
        return new Head
        {
            trR = c + R * 0.075f,
            trL = c - R * 0.075f + U * 0.006f,
            gl = c + U * 0.035f + F * 0.095f,
            gn = c - U * 0.115f + F * 0.085f,
            top = c + U * 0.11f,
        };
    }

    private static Head Rotate(Head h, Vector3 pivot, Quaternion q)
    {
        return new Head
        {
            trR = pivot + q * (h.trR - pivot),
            trL = pivot + q * (h.trL - pivot),
            gl = pivot + q * (h.gl - pivot),
            gn = pivot + q * (h.gn - pivot),
            top = pivot + q * (h.top - pivot),
        };
    }

    private static Quaternion Rotation(RomLineMotion m, float deg, Vector3 R, Vector3 U, Vector3 F)
    {
        switch (m)
        {
            case RomLineMotion.Flexion: return Quaternion.AngleAxis(deg, R);
            case RomLineMotion.Extension: return Quaternion.AngleAxis(-deg, R);
            case RomLineMotion.LateralLeft: return Quaternion.AngleAxis(deg, F);
            case RomLineMotion.LateralRight: return Quaternion.AngleAxis(-deg, F);
            case RomLineMotion.RotationRight: return Quaternion.AngleAxis(deg, U);
            default: return Quaternion.AngleAxis(-deg, U);
        }
    }

    /// <summary>넣은 회전이 몸에서 그 동작으로 나타났는가.</summary>
    private static bool Anatomy(RomLineMotion m, Head n, Head h, Vector3 R, Vector3 U)
    {
        switch (m)
        {
            case RomLineMotion.Flexion: return Vector3.Dot(h.gl - n.gl, U) < 0f;          // 이마가 내려간다
            case RomLineMotion.Extension: return Vector3.Dot(h.gl - n.gl, U) > 0f;        // 이마가 올라간다
            case RomLineMotion.LateralRight: return Vector3.Dot(h.top - n.top, R) > 0f;   // 정수리가 환자 오른쪽으로
            case RomLineMotion.LateralLeft: return Vector3.Dot(h.top - n.top, R) < 0f;
            case RomLineMotion.RotationRight: return Vector3.Dot(h.gl - n.gl, R) > 0f;    // 얼굴이 환자 오른쪽으로
            default: return Vector3.Dot(h.gl - n.gl, R) < 0f;
        }
    }

    // ★런타임(RomLineSession.LineOf)과 같은 짝이어야 한다 — 굴곡·신전 오른쪽 Tr~Gl / 측굴 Gl~Gn / 회전 반대쪽 Tr~Gl.
    private static void PickLine(RomLineMotion m, Head h, out Vector3 a, out Vector3 b)
    {
        RomLineSession.LineOf(m, out RomLinePoint pa, out RomLinePoint pb);
        a = Point(h, pa);
        b = Point(h, pb);
    }

    private static Vector3 Point(Head h, RomLinePoint p)
    {
        switch (p)
        {
            case RomLinePoint.TrRight: return h.trR;
            case RomLinePoint.TrLeft: return h.trL;
            case RomLinePoint.Gl: return h.gl;
            default: return h.gn;
        }
    }

    private static RomLineMotion Opposite(RomLineMotion m) => (RomLineMotion)((int)m ^ 1);

    private static float DistToLine(Vector3 p, Vector3 a, Vector3 dir)
    {
        return Vector3.Cross(p - a, dir).magnitude;
    }

    private static string Name(RomLineMotion m) => RomLineSession.MotionName[(int)m];
}
