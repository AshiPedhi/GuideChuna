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
