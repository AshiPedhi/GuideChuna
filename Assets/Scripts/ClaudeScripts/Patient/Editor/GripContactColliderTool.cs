using UnityEditor;
using UnityEngine;

/// <summary>
/// 경추 ROM 파지 접촉점(<see cref="GripContactPoint"/>)의 콜라이더 모양을 바꾼다.
///
/// ★★<b>왜 만들었나</b>(2026-09-15 사용자 요구 ④): "캡슐은 전체적으로 두꺼워져서 박스형으로
///   하려던 건데." — 접촉점은 <b>머리 표면에 붙는 넓고 얇은 판</b>이어야 하는데, 캡슐(사실상 구)은
///   키우면 <b>머리 바깥(앞뒤)으로도 같이</b> 두꺼워진다. 쓸모없는 방향으로 커지는 것이다.
///   좌우·위아래로만 넓히고 앞뒤는 얇게 두려면 <b>박스</b>여야 한다. 캡슐은 한 축으로만 길어진다.
///
/// ★<b>음수 스케일</b>: 환자 모델은 <c>c8</c>에 X 스케일 −1이 걸려 있다.
///   콜라이더는 음수 스케일을 못 쓰므로 <b>절대값 스케일 + 저장된 회전</b>으로 놓인다.
///   그래서 "보이는 상자"와 "판정 상자"가 어긋날 수 있다 — <b>어긋날 수 있다</b>는 것이지
///   반드시 어긋난다는 뜻이 아니다.
///
///   ★★<b>2026-09-15 실측: 이 씬의 접촉점 4개는 어긋남이 0.9°다 — 사실상 일치한다.</b>
///     거울상이 <b>상자 자기 축 하나를 뒤집는 것</b>으로 정확히 떨어져서, 상자 부피가 그대로다
///     (상자는 자기 축 뒤집기에 대칭이다). <b>Box로 바꿔도 된다.</b>
///   ★<b>그 전에 나는 "이마가 17° 기울었으니 34° 어긋난다"고 했고, 그것은 틀렸다.</b>
///     로컬 회전이 0이 아닌 것만 보고 단정했는데, 실제로 재야 하는 것은 <b>체인 전체를 곱한 결과</b>였다.
///     기울기 유무는 답이 아니다(규칙 9 — 방향은 추론하지 말고 잰다).
///   → 그래서 아래 <see cref="MirrorMismatchDegrees"/>는 <b>실제로 두 행렬을 비교해 각을 낸다.</b>
///     모델을 바꾸거나 접촉점을 옮기면 값이 달라질 수 있으니, 교체 전에 항상 다시 잰다.
///
/// ★<b>파괴성</b>(규칙 3): 콜라이더 컴포넌트를 <b>지우고 새로 붙인다</b>.
///   - <b>Undo가 걸린다</b>(Ctrl+Z).
///   - <b>멱등하다</b> — 이미 그 모양이면 건드리지 않는다.
///   - <b>크기(Transform 스케일)는 건드리지 않는다.</b> 캡슐이 쓰던 스케일을 박스가 그대로 쓴다.
///   - <b>저장하지 않는다.</b> 눈으로 확인하고 사람이 Ctrl+S 한다(절대규칙 2).
/// </summary>
public static class GripContactColliderTool
{
    private const string MenuBox = "GuideChuna/경추ROM/접촉점 콜라이더 — Box로 교체";
    private const string MenuCapsule = "GuideChuna/경추ROM/접촉점 콜라이더 — Capsule로 되돌리기";
    private const string MenuReport = "GuideChuna/경추ROM/접촉점 콜라이더 — 지금 모양 보기 (읽기 전용)";

    [MenuItem(MenuReport)]
    public static void Report()
    {
        GripContactPoint[] points = Find();
        if (points == null) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[접촉점 콜라이더] {points.Length}개");
        foreach (GripContactPoint p in points) sb.AppendLine("  " + Describe(p));
        sb.AppendLine();
        sb.AppendLine("※ '거울어긋남'은 보이는 상자와 판정 상자가 실제로 몇 도 어긋나는지다(계산이 아니라 측정).");
        sb.AppendLine("   1.5°를 넘으면 ★가 붙는다. 2026-09-15 실측은 4개 모두 0.9°였다 — Box를 써도 되는 상태다.");
        Debug.Log(sb.ToString());
    }

    [MenuItem(MenuBox)]
    public static void ToBox() => Convert(box: true);

    [MenuItem(MenuCapsule)]
    public static void ToCapsule() => Convert(box: false);

    private static void Convert(bool box)
    {
        GripContactPoint[] points = Find();
        if (points == null) return;

        int changed = 0, skipped = 0, warned = 0;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[접촉점 콜라이더] {(box ? "Box" : "Capsule")}로 교체");

        foreach (GripContactPoint p in points)
        {
            Collider old = p.GetComponent<Collider>();

            // ★멱등 — 이미 그 모양이면 손대지 않는다.
            if ((box && old is BoxCollider) || (!box && old is CapsuleCollider))
            {
                skipped++;
                sb.AppendLine($"  · {p.name} — 이미 {(box ? "Box" : "Capsule")}. 건너뜀");
                continue;
            }

            // ★캡슐이 쓰던 축을 박스가 이어받게 하려고 먼저 읽어 둔다.
            int capsuleDirection = old is CapsuleCollider c ? c.direction : -1;

            if (old != null) Undo.DestroyObjectImmediate(old);

            if (box)
            {
                BoxCollider b = Undo.AddComponent<BoxCollider>(p.gameObject);
                // ★크기는 Transform 스케일이 정한다(캡슐도 그랬다). 여기서는 단위 박스로 둔다.
                b.center = Vector3.zero;
                b.size = Vector3.one;
                b.isTrigger = true;
            }
            else
            {
                CapsuleCollider c2 = Undo.AddComponent<CapsuleCollider>(p.gameObject);
                c2.center = Vector3.zero;
                c2.radius = 0.5f;
                c2.height = 1f;
                // 원래 값(이마·뒤통수 X, 측두 Z)을 모르면 가장 긴 로컬 축을 쓴다.
                c2.direction = capsuleDirection >= 0 ? capsuleDirection : LongestAxis(p.transform.localScale);
                c2.isTrigger = true;
            }

            changed++;
            sb.AppendLine("  ✔ " + Describe(p));
            if (box && NeedsFlipWarning(p.transform)) warned++;
        }

        EditorSceneMarkDirty(points);

        sb.AppendLine();
        sb.AppendLine($"바꾼 것 {changed}개 · 건너뜀 {skipped}개");
        if (warned > 0)
        {
            sb.AppendLine($"★어긋남 주의 {warned}개 — 보이는 상자와 판정 상자가 1.5°를 넘게 어긋난다.");
            sb.AppendLine("  Play에서 그 접촉점이 안 잡히면 로컬 회전을 0으로 펴 보면 된다.");
            sb.AppendLine("  (2026-09-15 실측 당시엔 4개 모두 0.9°라 이 줄이 안 떴다 — 뜨면 모델이 바뀐 것이다.)");
        }
        sb.AppendLine("→ 씬을 저장하세요(Ctrl+S). 되돌리려면 Ctrl+Z.");
        Debug.Log(sb.ToString());
    }

    /// <summary>
    /// <b>보이는 상자</b>와 <b>판정 상자</b>가 실제로 몇 도 어긋나는가. 0에 가까우면 Box를 써도 된다.
    ///
    /// 보이는 쪽 = <c>localToWorldMatrix</c>(음수 스케일 포함).
    /// 판정 쪽  = <c>TRS(position, rotation, |lossyScale|)</c> — 물리가 실제로 쓰는 것.
    /// ★상자는 <b>자기 축을 뒤집어도 같은 상자</b>다. 그래서 축의 <b>부호는 무시</b>하고
    ///   방향만 비교한다. 축이 서로 자리를 바꾼 경우(치환)도 치수만 바뀔 뿐 부피는 같다.
    /// </summary>
    private static float MirrorMismatchDegrees(Transform t)
    {
        Matrix4x4 full = t.localToWorldMatrix;
        Vector3 s = t.lossyScale;
        Matrix4x4 phys = Matrix4x4.TRS(
            t.position, t.rotation, new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z)));

        float worst = 0f;
        for (int j = 0; j < 3; j++)
        {
            Vector3 a = ((Vector3)full.GetColumn(j)).normalized;
            if (a.sqrMagnitude < 1e-8f) continue;

            // 판정 쪽 세 축 중 가장 잘 맞는 것과의 각(부호 무시).
            float best = 180f;
            for (int k = 0; k < 3; k++)
            {
                Vector3 b = ((Vector3)phys.GetColumn(k)).normalized;
                if (b.sqrMagnitude < 1e-8f) continue;
                float ang = Vector3.Angle(a, b);
                best = Mathf.Min(best, Mathf.Min(ang, 180f - ang));
            }
            worst = Mathf.Max(worst, best);
        }
        return worst;
    }

    /// <summary>어긋남이 눈에 띌 만한가. 1도까지는 수치 노이즈로 본다(09-15 실측치가 0.9도였다).</summary>
    private static bool NeedsFlipWarning(Transform t) => MirrorMismatchDegrees(t) > 1.5f;

    private static string Describe(GripContactPoint p)
    {
        Collider col = p.GetComponent<Collider>();
        string shape = col is BoxCollider ? "Box"
                     : col is CapsuleCollider ? "Capsule"
                     : col is SphereCollider ? "Sphere"
                     : col == null ? "★콜라이더 없음" : col.GetType().Name;

        Vector3 s = p.transform.lossyScale;
        float mismatch = MirrorMismatchDegrees(p.transform);
        string flip = NeedsFlipWarning(p.transform) ? "  ★어긋남 주의" : "";

        // 박스 기준 실제 월드 치수(㎝). size=1 이므로 스케일이 곧 한 변이다.
        return $"{p.name,-16} {shape,-8} 한변({Mathf.Abs(s.x) * 100f:F1}, {Mathf.Abs(s.y) * 100f:F1}, " +
               $"{Mathf.Abs(s.z) * 100f:F1})cm  거울어긋남 {mismatch:F1}°  " +
               $"트리거 {(col != null && col.isTrigger ? "O" : "★X")}{flip}";
    }

    private static int LongestAxis(Vector3 v)
    {
        if (Mathf.Abs(v.x) >= Mathf.Abs(v.y) && Mathf.Abs(v.x) >= Mathf.Abs(v.z)) return 0;
        return Mathf.Abs(v.y) >= Mathf.Abs(v.z) ? 1 : 2;
    }

    /// <summary>열린 씬 전체에서 접촉점을 찾는다. 하나도 없으면 이유를 말하고 null.</summary>
    private static GripContactPoint[] Find()
    {
        GripContactPoint[] points =
            Object.FindObjectsByType<GripContactPoint>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (points.Length == 0)
        {
            Debug.LogError("[접촉점 콜라이더] GripContactPoint를 하나도 못 찾았습니다 — " +
                           "TrainingScene이 열려 있는지 확인하세요.");
            return null;
        }
        return points;
    }

    private static void EditorSceneMarkDirty(GripContactPoint[] points)
    {
        foreach (GripContactPoint p in points)
        {
            if (p == null) continue;
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(p.gameObject.scene);
            return;
        }
    }
}
