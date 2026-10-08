// ★[선 실측 — 환자 모델 시뮬레이션] 2026-10-02 신설
// «기준 없이 재는 방법(직접)»과 «양쪽 Tr 기준을 두는 방법(면)»의 차이를, 가정한 치수가 아니라
// <b>환자 모델의 실제 머리 형상</b>에서 재려고 만든 에디터 도구다(사람 실측이 아니다 — 모델은 한 명이다).
//   · 씬을 «미리보기 씬»으로 읽기만 한다 — 열려 있는 씬을 바꾸지 않고, 아무것도 저장하지 않는다.
//   · 회전축은 실습과 같은 함수(CervicalRomDriver.WorldAxisFor), 각은 앱과 같은 함수(RomLineMath · RomLineSession.LineOf)로 낸다.
//   · 좌우는 본 이름을 믿지 않는다(환자 c8에 음수 배율). 위 = 몸통 축을 «머리가 있는 쪽»으로 재고, 앞 = 눈이 있는 쪽, 오른쪽 = 위 × 앞.
// 브리지: bridge.py romline-model --mode=info|render|sim  (ChunaAgentBridge.Scene.cs에 연결)

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class RomLineModelSim
{
    private const string DefaultScene = "Assets/Scenes/RomSimScene.unity";   // TrainingScene의 복사본(브리지 소유)
    private const string OutDir = "Captures/romsim";

    private class Ctx
    {
        public CervicalRomDriver driver;
        public Transform torso, head;
        public Vector3 origin, R, U, F;            // 머리 기준틀(월드): 원점 = 두 눈 본의 중점
        public SkinnedMeshRenderer body;
        public List<Vector3> verts = new List<Vector3>();   // 머리에 붙은 정점(기준틀 좌표, m)
        public float eyeLineOff;                   // 두 눈을 이은 선이 R과 어긋난 각(도) — 0 또는 180에 가까워야 한다
        public Vector3 Canon(Vector3 world) { Vector3 d = world - origin; return new Vector3(Vector3.Dot(d, R), Vector3.Dot(d, U), Vector3.Dot(d, F)); }
        public Vector3 CanonDir(Vector3 w) { return new Vector3(Vector3.Dot(w, R), Vector3.Dot(w, U), Vector3.Dot(w, F)); }
        public Vector3 World(Vector3 c) { return origin + R * c.x + U * c.y + F * c.z; }
    }

    private struct Marks { public Vector3 trR, trL, gl, gn; public bool ok; public string why; }

    public static string Run(Dictionary<string, string> a)
    {
        string mode = Arg(a, "mode", "info");
        string path = Arg(a, "scene", DefaultScene);
        if (!File.Exists(path)) return "★씬이 없다: " + path;

        var sb = new StringBuilder();
        Scene sc = EditorSceneManager.OpenPreviewScene(path);
        try
        {
            Ctx c = Build(sc, sb);
            if (c == null) return sb.ToString();
            switch (mode)
            {
                case "info": Info(c, sb); break;
                case "render": Render(c, sc, a, sb); break;
                case "sim": Sim(c, a, sb); break;
                case "mc": MonteCarlo(c, a, sb); break;
                default: sb.AppendLine("★모르는 mode: " + mode + " (info · render · sim · mc)"); break;
            }
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(sc);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 에디터 창 없이 한 번에 돌린다(문서에 쓰는 실행 묶음 전부): Unity.exe -batchmode -projectPath … -executeMethod RomLineModelSim.Batch
    /// 결과는 Captures/romsim/ 아래 같은 이름으로 덮어쓰고, 요약은 batch.log에 남긴다.
    /// </summary>
    public static void Batch()
    {
        var log = new StringBuilder();
        int code = 0;
        try
        {
            string[] tr = { "tr_y", "-0.016", "tr_z", "-0.077" };
            string[][] runs =
            {
                new[] { "mode", "sim", "n", "3000", "out", "v2" },
                new[] { "mode", "mc", "out", "v2" },
                new[] { "mode", "mc", "seed", "23", "out", "s23" },
                new[] { "mode", "mc", "seed", "37", "out", "s37" },
                new[] { "mode", "mc", "seed", "11", "n", "20000", "out", "n20k" },
                new[] { "mode", "sim", "n", "200", "yaw", "37", "out", "yaw37" },
                new[] { "mode", "sim", "n", "200", "yaw", "200", "out", "yaw200" },
                new[] { "mode", "sim", "n", "200", "pivot_y", "0", "pivot_z", "0", "out", "pivot0" },
                new[] { "mode", "sim", "n", "200", "pivot_y", "-0.2", "pivot_z", "0.05", "out", "pivot2" },
                new[] { "mode", "sim", "n", "200", "tr_y", "-0.021", "tr_z", "-0.077", "out", "trYm" },
                new[] { "mode", "sim", "n", "200", "tr_y", "-0.011", "tr_z", "-0.077", "out", "trYp" },
                new[] { "mode", "sim", "n", "200", "tr_y", "-0.016", "tr_z", "-0.082", "out", "trZm" },
                new[] { "mode", "sim", "n", "200", "tr_y", "-0.016", "tr_z", "-0.072", "out", "trZp" },
            };
            foreach (string[] r in runs)
            {
                var a = new Dictionary<string, string>();
                for (int i = 0; i + 1 < tr.Length; i += 2) a[tr[i]] = tr[i + 1];
                for (int i = 0; i + 1 < r.Length; i += 2) a[r[i]] = r[i + 1];   // 실행마다 준 값이 기본(Tr 자리)을 덮는다
                log.AppendLine("── " + string.Join(" ", r));
                log.AppendLine(Run(a));
            }
        }
        catch (Exception e)
        {
            log.AppendLine("★실패: " + e);
            code = 1;
        }
        Directory.CreateDirectory(OutDir);
        File.WriteAllText(Path.Combine(OutDir, "batch.log"), log.ToString(), new UTF8Encoding(false));
        if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(code);
    }

    // ── 모델 읽기 ───────────────────────────────────────────────────
    private static Ctx Build(Scene sc, StringBuilder sb)
    {
        var c = new Ctx();
        foreach (GameObject root in sc.GetRootGameObjects())
        {
            c.driver = root.GetComponentInChildren<CervicalRomDriver>(true);
            if (c.driver != null) break;
        }
        if (c.driver == null) { sb.AppendLine("★CervicalRomDriver가 씬에 없다."); return null; }

        Transform pr = c.driver.transform;
        c.torso = FindDeep(pr, "CC_Base_Spine02");
        c.head = FindDeep(pr, "CC_Base_Head");
        Transform e1 = FindDeep(pr, "CC_Base_L_Eye"), e2 = FindDeep(pr, "CC_Base_R_Eye");
        if (c.torso == null || c.head == null || e1 == null || e2 == null)
        {
            sb.AppendLine($"★본을 못 찾았다 — 몸통 {(c.torso != null)} · 머리 {(c.head != null)} · 눈 {(e1 != null)}/{(e2 != null)}");
            return null;
        }

        c.origin = (e1.position + e2.position) * 0.5f;
        // 위: 몸통의 회전축(실습에서 «회전»에 쓰는 축)을, 머리가 있는 쪽으로 향하게 잰다.
        Vector3 up = c.torso.TransformDirection(Vector3.up).normalized;
        if (Vector3.Dot(up, c.head.position - c.torso.position) < 0f) up = -up;
        c.U = up;
        // 앞: 머리 본에서 눈 쪽(위 성분은 뺀다 — 앞뒤만 남긴다).
        Vector3 f = Vector3.ProjectOnPlane(c.origin - c.head.position, c.U);
        if (f.sqrMagnitude < 1e-8f) { sb.AppendLine("★눈이 머리 본 바로 위에 있어 앞을 못 정한다."); return null; }
        c.F = f.normalized;
        c.R = Vector3.Cross(c.U, c.F).normalized;   // Unity(왼손 좌표): 위 × 앞 = 오른쪽
        c.eyeLineOff = Vector3.Angle(c.R, e2.position - e1.position);

        // 머리 본을 스키닝에 쓰는 메시 중 정점이 가장 많은 것을 몸(피부)으로 본다.
        var headSet = new HashSet<Transform>();
        Collect(c.head, headSet);
        int best = -1;
        foreach (var smr in pr.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.sharedMesh == null || Array.IndexOf(smr.bones, c.head) < 0) continue;
            // ★정점 수로만 고르면 머리카락(Undercut_fade 20006개)이 잡힌다(10-02 실측) — 피부 메시는 이름으로 먼저 찾는다.
            if (smr.name == "CC_Base_Body") { c.body = smr; break; }
            if (smr.sharedMesh.vertexCount > best) { best = smr.sharedMesh.vertexCount; c.body = smr; }
        }
        if (c.body == null) { sb.AppendLine("★머리 본에 묶인 스킨 메시가 없다."); return null; }

        // 직접 스키닝한다(BakeMesh는 음수 배율에서 믿기 어렵다): 월드 = Σ 가중치 · 본 · 바인드포즈 · 정점
        Mesh m = c.body.sharedMesh;
        Vector3[] v = m.vertices;
        BoneWeight[] w = m.boneWeights;
        Matrix4x4[] bind = m.bindposes;
        Transform[] bones = c.body.bones;
        var mat = new Matrix4x4[bones.Length];
        var isHead = new bool[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            mat[i] = bones[i] != null ? bones[i].localToWorldMatrix * bind[i] : Matrix4x4.identity;
            isHead[i] = bones[i] != null && headSet.Contains(bones[i]);
        }
        for (int i = 0; i < v.Length; i++)
        {
            BoneWeight bw = w[i];
            float hw = (isHead[bw.boneIndex0] ? bw.weight0 : 0f) + (isHead[bw.boneIndex1] ? bw.weight1 : 0f)
                     + (isHead[bw.boneIndex2] ? bw.weight2 : 0f) + (isHead[bw.boneIndex3] ? bw.weight3 : 0f);
            if (hw < 0.5f) continue;
            Vector3 p = mat[bw.boneIndex0].MultiplyPoint3x4(v[i]) * bw.weight0 + mat[bw.boneIndex1].MultiplyPoint3x4(v[i]) * bw.weight1
                      + mat[bw.boneIndex2].MultiplyPoint3x4(v[i]) * bw.weight2 + mat[bw.boneIndex3].MultiplyPoint3x4(v[i]) * bw.weight3;
            c.verts.Add(c.Canon(p));
        }
        return c;
    }

    private static void Info(Ctx c, StringBuilder sb)
    {
        Transform pr = c.driver.transform;
        sb.AppendLine($"환자 루트 '{pr.name}' · 몸통 {c.torso.name} · 머리 {c.head.name}");
        sb.AppendLine($"위(몸통 축)와 월드 수직의 각 {Vector3.Angle(c.U, Vector3.up):F1}° · 두 눈 선이 «오른쪽»과 이룬 각 {c.eyeLineOff:F1}° (0 또는 180 근처여야 한다)");
        sb.AppendLine($"머리 본 방향: 머리 본 → 눈 중점 {c.Canon(c.head.position) * -100f:F1}cm(기준틀, 오른쪽·위·앞)");
        sb.AppendLine("스킨 메시:");
        foreach (var smr in pr.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            sb.AppendLine($"  {(smr == c.body ? "▶" : " ")} {smr.name} · 정점 {(smr.sharedMesh != null ? smr.sharedMesh.vertexCount : 0)} · {(smr.gameObject.activeInHierarchy && smr.enabled ? "켜짐" : "꺼짐")} · 머리 본 {(Array.IndexOf(smr.bones, c.head) >= 0 ? "O" : "X")}");
        Vector3 mn = Vector3.one * 9f, mx = Vector3.one * -9f;
        foreach (Vector3 p in c.verts) { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
        sb.AppendLine($"머리에 붙은 정점 {c.verts.Count}개 · 범위(cm) 좌우 {mn.x * 100f:F1} ~ {mx.x * 100f:F1} · 위아래 {mn.y * 100f:F1} ~ {mx.y * 100f:F1} · 앞뒤 {mn.z * 100f:F1} ~ {mx.z * 100f:F1}");
        for (int d = 1; d <= 6; d++)
        {
            Vector3 ax = c.CanonDir(c.driver.WorldAxisFor((CervicalRomDriver.Direction)d));
            sb.AppendLine($"  회전축 {(CervicalRomDriver.Direction)d}: 기준틀 {ax:F3}");
        }
    }

    // ── 표식 자리 ───────────────────────────────────────────────────
    // 기준틀 좌표(m): x 오른쪽 · y 위 · z 앞, 원점 = 두 눈 본의 중점.
    private static Marks Pick(Ctx c, Dictionary<string, string> a)
    {
        var k = new Marks { ok = true, why = "" };
        const float mid = 0.004f;

        // Gl — 정중선 위, 눈높이 조금 위에서 가장 앞으로 나온 점.
        float glLo = ArgF(a, "gl_lo", 0.005f), glHi = ArgF(a, "gl_hi", 0.035f);
        float bestZ = -9f;
        foreach (Vector3 p in c.verts)
            if (Mathf.Abs(p.x) < mid && p.y >= glLo && p.y <= glHi && p.z > bestZ) { bestZ = p.z; k.gl = p; }
        if (bestZ < -8f) { k.ok = false; k.why += "Gl 후보 없음 · "; }

        // Gn — 정중선 위, 입 아래에서 «앞·아래» 쪽으로 가장 나온 점(턱 끝).
        float gnTop = ArgF(a, "gn_top", -0.06f);
        float bestS = -9f;
        foreach (Vector3 p in c.verts)
        {
            if (Mathf.Abs(p.x) >= mid || p.y > gnTop) continue;
            float s = p.z - p.y;
            if (s > bestS) { bestS = s; k.gn = p; }
        }
        if (bestS < -8f) { k.ok = false; k.why += "Gn 후보 없음 · "; }

        // Tr — 옆에서 본 자리(y, z)를 받아, 그 둘레 반경 안에서 가장 바깥으로 나온 점(양쪽 각각).
        float ty = ArgF(a, "tr_y", -0.015f), tz = ArgF(a, "tr_z", -0.085f), tr = ArgF(a, "tr_r", 0.004f);
        float bR = 0f, bL = 0f;
        foreach (Vector3 p in c.verts)
        {
            float dy = p.y - ty, dz = p.z - tz;
            if (dy * dy + dz * dz > tr * tr) continue;
            if (p.x > bR) { bR = p.x; k.trR = p; }
            if (-p.x > bL) { bL = -p.x; k.trL = p; }
        }
        if (bR < 0.03f || bL < 0.03f) { k.ok = false; k.why += $"Tr 후보 없음(오른쪽 {bR * 100f:F1}cm · 왼쪽 {bL * 100f:F1}cm) · "; }
        return k;
    }

    private static string MarksText(Marks k)
    {
        Func<Vector3, string> cm = p => $"({p.x * 100f:F1}, {p.y * 100f:F1}, {p.z * 100f:F1})";
        return $"오른쪽 Tr {cm(k.trR)} · 왼쪽 Tr {cm(k.trL)} · Gl {cm(k.gl)} · Gn {cm(k.gn)} cm(오른쪽·위·앞) · " +
               $"Tr 간격 {Vector3.Distance(k.trR, k.trL) * 100f:F1} · Tr~Gl {Vector3.Distance(k.trR, k.gl) * 100f:F1} · Gl~Gn {Vector3.Distance(k.gl, k.gn) * 100f:F1}cm";
    }

    // ── 그림(눈으로 표식 자리를 확인) ───────────────────────────────
    private static void Render(Ctx c, Scene sc, Dictionary<string, string> a, StringBuilder sb)
    {
        Marks k = Pick(c, a);
        sb.AppendLine((k.ok ? "표식 — " : "★표식 일부 없음(" + k.why + ") — ") + MarksText(k));

        // 몸 메시만 남긴다(머리카락·눈썹·방 모델이 가리지 않게). 미리보기 씬이라 원본에는 영향이 없다.
        foreach (GameObject root in sc.GetRootGameObjects())
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                if (r != c.body) r.enabled = false;
        // 월드 공간 UI 판은 Renderer가 아니라서 위에서 안 꺼진다 — 앞·위 그림에 같이 찍혔다(10-06 확인).
        foreach (GameObject root in sc.GetRootGameObjects())
            foreach (Canvas cv in root.GetComponentsInChildren<Canvas>(true))
                cv.enabled = false;
        Transform t = c.body.transform;
        while (t != null) { t.gameObject.SetActive(true); t = t.parent; }
        c.body.enabled = true;

        var made = new List<GameObject>();
        if (Arg(a, "marks", "1") != "0")
        {
            made.Add(Sphere(sc, c.World(k.trR), Color.red));
            made.Add(Sphere(sc, c.World(k.trL), Color.blue));
            made.Add(Sphere(sc, c.World(k.gl), Color.green));
            made.Add(Sphere(sc, c.World(k.gn), Color.yellow));
        }

        var lightGo = new GameObject("sim light");
        SceneManager.MoveGameObjectToScene(lightGo, sc);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;

        var camGo = new GameObject("sim cam");
        SceneManager.MoveGameObjectToScene(camGo, sc);
        var cam = camGo.AddComponent<Camera>();
        cam.scene = sc;
        cam.orthographic = true;
        float half = ArgF(a, "half", 0.16f);
        cam.orthographicSize = half;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.white;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 1.2f;
        int px = 900;
        var rt = new RenderTexture(px, px, 24);
        cam.targetTexture = rt;
        Vector3 center = c.World(new Vector3(0f, -0.04f, -0.05f));
        Directory.CreateDirectory(OutDir);

        // 보는 방향(카메라가 서는 쪽) — 오른쪽 · 왼쪽 · 앞 · 위
        var views = new (string name, Vector3 from, Vector3 up)[]
        {
            ("right", c.R, c.U), ("left", -c.R, c.U), ("front", c.F, c.U), ("top", c.U, c.F),
        };
        foreach (var v in views)
        {
            cam.transform.position = center + v.from * 0.6f;
            cam.transform.rotation = Quaternion.LookRotation(-v.from, v.up);
            lightGo.transform.rotation = Quaternion.LookRotation(-v.from - v.up * 0.4f + cam.transform.right * 0.3f, v.up);
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(px, px, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, px, px), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            string file = Path.Combine(OutDir, "head_" + v.name + Suffix(a) + ".png");
            File.WriteAllBytes(file, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            sb.AppendLine($"그림 {file} · 한 변 {half * 200f:F0}cm · {px}px · 가운데 = 기준틀 (0, -4, -5)cm");
        }
        cam.targetTexture = null;
        rt.Release();
        UnityEngine.Object.DestroyImmediate(rt);
        foreach (GameObject g in made) UnityEngine.Object.DestroyImmediate(g);
    }

    private static GameObject Sphere(Scene sc, Vector3 pos, Color col)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        SceneManager.MoveGameObjectToScene(g, sc);
        g.transform.position = pos;
        g.transform.localScale = Vector3.one * 0.007f;
        var m = new Material(Shader.Find("Sprites/Default"));
        m.color = col;
        g.GetComponent<Renderer>().sharedMaterial = m;
        return g;
    }

    // ── 시뮬레이션 ──────────────────────────────────────────────────
    private static readonly string[] MotionName = { "굴곡", "신전", "우측굴", "좌측굴", "우회전", "좌회전" };

    private struct LineDef { public string name; public RomLinePoint a, b; }

    private static LineDef L(string name, RomLinePoint a, RomLinePoint b) { return new LineDef { name = name, a = a, b = b }; }

    /// <summary>
    /// 그 동작에서 재 보는 선들. 첫 줄 = 9/30 기준(앱과 같은 함수 RomLineSession.LineOf), 나머지 = 견줘 보는 후보(10-06 추가).
    /// </summary>
    private static LineDef[] LinesFor(RomLineMotion motion)
    {
        RomLineSession.LineOf(motion, out RomLinePoint a, out RomLinePoint b);
        int m = (int)motion;
        if (m < 2) return new[] { L("Tr~Gl", a, b), L("Gl~Gn", RomLinePoint.Gl, RomLinePoint.Gn), L("Tr~Gn", RomLinePoint.TrRight, RomLinePoint.Gn) };
        if (m < 4) return new[] { L("Gl~Gn", a, b), L("Tr~Tr", RomLinePoint.TrLeft, RomLinePoint.TrRight) };
        return new[] { L("반대쪽Tr~Gl", a, b), L("Tr~Tr", RomLinePoint.TrLeft, RomLinePoint.TrRight) };
    }

    // 넣는 각(도). ★굴곡은 45°가 최대, 신전은 90°가 최대다(10-06 사용자 정정 — 처음엔 굴곡·신전을 똑같이 80°까지 넣었다).
    private static readonly float[][] Thetas =
    {
        new[] { 10f, 20f, 30f, 40f, 45f }, new[] { 10f, 20f, 30f, 45f, 60f, 80f, 90f },
        new[] { 10f, 20f, 30f, 40f, 45f }, new[] { 10f, 20f, 30f, 40f, 45f },   // 측굴 최대 45°(사용자 정정 10-08 — 60°까지 넣었었다)
        new[] { 10f, 20f, 30f, 45f, 60f, 80f, 90f }, new[] { 10f, 20f, 30f, 45f, 60f, 80f, 90f },
    };

    private static string Suffix(Dictionary<string, string> a)
    {
        string s = Arg(a, "out", "");
        return s.Length > 0 ? "_" + s : "";
    }

    /// <summary>회전 중심(기준틀, m). 각에는 영향이 없어야 한다 — 선의 방향만 쓰기 때문이다(--pivot_y --pivot_z로 바꿔 확인한다).</summary>
    private static Vector3 Pivot(Dictionary<string, string> a)
    {
        return new Vector3(0f, ArgF(a, "pivot_y", -0.10f), ArgF(a, "pivot_z", -0.08f));
    }

    private static Vector3[] Axes(Ctx c)
    {
        var axes = new Vector3[6];
        for (int m = 0; m < 6; m++) axes[m] = c.CanonDir(c.driver.WorldAxisFor((CervicalRomDriver.Direction)(m + 1))).normalized;
        return axes;
    }

    /// <summary>밖에서 따로 계산해 대조할 수 있게, 시뮬레이션이 쓴 입력(표식·회전축·회전 중심)을 그대로 적어 둔다.</summary>
    private static void WriteModel(Ctx c, Marks k, Vector3[] axes, Vector3 pivot, string suffix, StringBuilder sb)
    {
        Func<Vector3, string> v = p => string.Format(CultureInfo.InvariantCulture, "[{0:R}, {1:R}, {2:R}]", p.x, p.y, p.z);
        var j = new StringBuilder();
        j.AppendLine("{");
        j.AppendLine($"  \"trR\": {v(k.trR)}, \"trL\": {v(k.trL)}, \"gl\": {v(k.gl)}, \"gn\": {v(k.gn)},");
        j.AppendLine($"  \"pivot\": {v(pivot)},");
        j.Append("  \"axes\": [");
        for (int m = 0; m < 6; m++) j.Append(v(axes[m]) + (m < 5 ? ", " : ""));
        j.AppendLine("],");
        j.Append("  \"lift\": {");
        bool first = true;
        for (int m = 0; m < 6; m++)
            foreach (LineDef ln in LinesFor((RomLineMotion)m))
            {
                j.Append((first ? "" : ", ") + $"\"{MotionName[m]}|{ln.name}\": " + Lift(Of(k, ln.a), Of(k, ln.b), axes[m]).ToString("F3", CultureInfo.InvariantCulture));
                first = false;
            }
        j.AppendLine("},");
        j.AppendLine($"  \"verts\": {c.verts.Count}, \"upVsGravity\": {Vector3.Angle(c.U, Vector3.up).ToString("F3", CultureInfo.InvariantCulture)},");
        j.AppendLine(string.Format(CultureInfo.InvariantCulture, "  \"trGap\": {0:F4}, \"trGl\": {1:F4}, \"glGn\": {2:F4}",
                                   Vector3.Distance(k.trR, k.trL), Vector3.Distance(k.trR, k.gl), Vector3.Distance(k.gl, k.gn)));
        j.AppendLine("}");
        Directory.CreateDirectory(OutDir);
        string file = Path.Combine(OutDir, "model" + suffix + ".json");
        File.WriteAllText(file, j.ToString(), new UTF8Encoding(false));
        sb.AppendLine("입력 " + file);
    }

    private static void Sim(Ctx c, Dictionary<string, string> a, StringBuilder sb)
    {
        Marks k = Pick(c, a);
        if (!k.ok) { sb.AppendLine("★표식을 못 잡았다 — " + k.why); return; }
        sb.AppendLine("표식 — " + MarksText(k));

        int n = (int)ArgF(a, "n", 2000f);
        var rng = new System.Random((int)ArgF(a, "seed", 7f));
        Vector3[] axes = Axes(c);
        Vector3 pivot = Pivot(a);
        // 환자가 서 있는 방향(수직축 둘레). 바꿔도 값이 같아야 한다 — 확인용.
        Quaternion yaw = Quaternion.AngleAxis(ArgF(a, "yaw", 0f), Vector3.up);
        string suffix = Suffix(a);
        WriteModel(c, k, axes, pivot, suffix, sb);

        var tsv = new StringBuilder();
        tsv.AppendLine("motion\tline\ttheta\tgroup\tparam\tmethod\tmean\tsd\tn");

        // 모델의 선이 각 동작의 면(회전축에 직각인 면)에서 들린 각
        sb.AppendLine("선이 그 동작의 면에서 들린 각:");
        foreach (int m in new[] { 0, 2, 4, 5 })
        {
            sb.Append($"  {MotionName[m]}");
            foreach (LineDef ln in LinesFor((RomLineMotion)m)) sb.Append($" · {ln.name} {Lift(Of(k, ln.a), Of(k, ln.b), axes[m]):F1}°");
            sb.AppendLine();
        }
        float phiModel = Lift(k.trR, k.gl, axes[0]);

        float[] noises = { 0.001f, 0.002f, 0.003f, 0.005f, 0.007f };
        float[] mixes = { 5f, 10f, 15f, 20f };
        float[] drifts = { 5f, 10f, 15f, 20f };
        float[] tilts = { 5f, 10f };
        float[] widths = { 0.9f, 1.1f };
        float[] phiErr = { -5f, 5f };

        int anatomyFail = 0;
        for (int m = 0; m < 6; m++)
        {
            var motion = (RomLineMotion)m;
            foreach (float th in Thetas[m])
            {
                Quaternion q = Quaternion.AngleAxis(th, axes[m]);
                if (!Anatomy(motion, k, q, pivot)) anatomyFail++;

                // 1) 그 동작만
                Trial(tsv, k, motion, th, q, Quaternion.identity, yaw, 1f, 0f, null, 1, "순수", "-", phiModel, phiErr, pivot);
                // 2) 찍는 점의 오차
                foreach (float nz in noises)
                    Trial(tsv, k, motion, th, q, Quaternion.identity, yaw, 1f, nz, rng, n, "점오차", (nz * 1000f).ToString("F0") + "mm", phiModel, null, pivot);
                // 3) 다른 동작이 섞임(두 축 × 양쪽)
                foreach (float mx in mixes)
                    for (int o = 0; o < 6; o++)
                    {
                        if (o / 2 == m / 2) continue;
                        Quaternion q2 = Quaternion.AngleAxis(mx, axes[o]) * q;
                        Trial(tsv, k, motion, th, q2, Quaternion.identity, yaw, 1f, 0f, null, 1, "섞임", MotionName[o] + " " + mx.ToString("F0") + "°", phiModel, null, pivot);
                    }
                // 4) 기준을 찍은 뒤 몸통이 돌아감(수직축 둘레) — 기준이 있는 방법에만 영향
                foreach (float dr in drifts)
                    Trial(tsv, k, motion, th, q, Quaternion.AngleAxis(dr, Vector3.up), yaw, 1f, 0f, null, 1, "몸통돌아감", dr.ToString("F0") + "°", phiModel, null, pivot);
                // 5) 머리 폭이 다른 사람(귀 자리만 좌우로) — 평균값 보정에 영향
                foreach (float w in widths)
                    Trial(tsv, k, motion, th, q, Quaternion.identity, yaw, w, 0f, null, 1, "머리폭", (w * 100f).ToString("F0") + "%", phiModel, null, pivot);
                // 6) 몸통이 처음부터 기울어 있음(10-06 추가) — «면»은 위아래를 중력으로 잡으므로, 환자가 기울여 앉으면 면이 몸과 어긋난다.
                //    기준·중립·측정 모두 기울어진 채로 찍는다(= 기준을 다시 찍어도 없어지지 않는다).
                foreach (float t in tilts)
                {
                    Trial(tsv, k, motion, th, q, Quaternion.identity, yaw * Quaternion.AngleAxis(t, Vector3.right), 1f, 0f, null, 1, "몸통기울어짐", "앞뒤 " + t.ToString("F0") + "°", phiModel, null, pivot);
                    Trial(tsv, k, motion, th, q, Quaternion.identity, yaw * Quaternion.AngleAxis(t, Vector3.forward), 1f, 0f, null, 1, "몸통기울어짐", "좌우 " + t.ToString("F0") + "°", phiModel, null, pivot);
                }
            }
        }

        Directory.CreateDirectory(OutDir);
        string file = Path.Combine(OutDir, "sim" + suffix + ".tsv");
        File.WriteAllText(file, tsv.ToString(), new UTF8Encoding(false));
        sb.AppendLine($"몸 확인(넣은 회전이 그 동작인가) 실패 {anatomyFail}건 · 굴곡 Tr~Gl 선이 들린 각(모델) {phiModel:F1}°");
        sb.AppendLine($"결과 {file} · 반복 {n}회(점오차 묶음)");
    }

    /// <summary>
    /// 한 조건. pose = 머리에 넣는 회전, drift = 기준을 찍은 뒤 몸 전체가 돌아간 양, world = 환자 전체가 놓인 자세(방향·기울어짐),
    /// width = 귀 자리 좌우 배율. 선마다 직접 · 면을 내고, 굴곡·신전의 Tr~Gl 선에는 평균값 보정을 더한다.
    /// </summary>
    private static void Trial(StringBuilder tsv, Marks k0, RomLineMotion motion, float th, Quaternion pose, Quaternion drift, Quaternion world, float width,
                              float noise, System.Random rng, int n, string group, string param, float phiModel, float[] phiErr, Vector3 pivot)
    {
        Marks k = k0;
        k.trR.x *= width; k.trL.x *= width;

        int m = (int)motion;
        LineDef[] lines = LinesFor(motion);
        int corr = lines.Length * 2;
        var sum = new double[corr + 1]; var sq = new double[corr + 1];
        Quaternion after = drift * world;
        for (int it = 0; it < n; it++)
        {
            // 기준(양쪽 Tr)은 몸통이 돌아가기 <b>전</b>에, 중립선과 측정선은 돌아간 <b>뒤</b>에 찍는다
            //   — «기준을 찍은 뒤 환자가 돌아앉았다»는 상황이다. 직접 값은 기준을 안 쓰므로 영향이 없어야 한다.
            //   ★첫 판은 중립선을 돌아가기 전에 찍게 해 놓아서, 직접 값까지 같이 틀어졌다(다른 동작이 섞인 것과 같아졌다).
            RomLineFrame f = RomLineMath.BuildFrame(world * k.trR + N(rng, noise), world * k.trL + N(rng, noise), world * k.gl + N(rng, noise));
            Vector3 normal = RomLineMath.PlaneNormal(motion, f);

            for (int j = 0; j < lines.Length; j++)
            {
                Vector3 a0 = Of(k, lines[j].a), b0 = Of(k, lines[j].b);
                Vector3 nA = after * a0 + N(rng, noise), nB = after * b0 + N(rng, noise);
                Vector3 mA = after * (pivot + pose * (a0 - pivot)) + N(rng, noise), mB = after * (pivot + pose * (b0 - pivot)) + N(rng, noise);
                float direct = RomLineMath.MeasureDirect(nA, nB, mA, mB).angle;
                Acc(sum, sq, j * 2, direct);
                Acc(sum, sq, j * 2 + 1, RomLineMath.Measure(nA, nB, mA, mB, normal).angle);
                if (m < 2 && j == 0) Acc(sum, sq, corr, Corrected(direct, phiModel));
                if ((m == 2 || m == 3) && j == 0) Acc(sum, sq, corr, NeutralPlaneAngle(nA, nB, mA, mB, direct));
            }
        }
        string head = $"{MotionName[m]}\t{{0}}\t{th.ToString("F0", CultureInfo.InvariantCulture)}\t{group}\t{param}\t{{1}}\t{{2}}\t{{3}}\t{n}";
        for (int j = 0; j < lines.Length; j++)
        {
            Row(tsv, head, lines[j].name, "직접", sum, sq, j * 2, n);
            Row(tsv, head, lines[j].name, "면", sum, sq, j * 2 + 1, n);
        }
        if (m < 2)
        {
            Row(tsv, head, "Tr~Gl", "평균값보정", sum, sq, corr, n);
            if (phiErr != null)
                foreach (float e in phiErr)   // 평균값이 그 사람과 e도 다를 때(순수 조건에서만)
                {
                    float d = (float)(sum[0] / n);
                    tsv.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0}\tTr~Gl\t{1:F0}\t평균값오차\t{2:+0;-0}°\t평균값보정\t{3:F3}\t0\t1",
                                                 MotionName[m], th, e, Corrected(d, phiModel + e)));
                }
        }
        else if (m < 4) Row(tsv, head, "Gl~Gn", "중립선면", sum, sq, corr, n);
    }

    // ── 종합 시뮬레이션(여러 요인을 한꺼번에 흔든다) ────────────────
    // sim은 요인을 하나씩 떼어 본다. 여기서는 한 번 잴 때마다 «사람(머리 모양) · 찍는 오차 · 섞이는 동작 · 몸통 돌아감»을
    // 모두 새로 뽑아, 실제로 잴 때 값이 어느 범위에 퍼지는지를 본다. ★넣는 크기는 전부 가정이다(찍는 오차만 10-02 시험에서 역산).
    private struct Env { public string name; public float shape, tap, mix, drift, tilt; }

    private static void MonteCarlo(Ctx c, Dictionary<string, string> a, StringBuilder sb)
    {
        Marks k0 = Pick(c, a);
        if (!k0.ok) { sb.AppendLine("★표식을 못 잡았다 — " + k0.why); return; }
        int n = (int)ArgF(a, "n", 5000f);
        var rng = new System.Random((int)ArgF(a, "seed", 11f));
        Vector3[] axes = Axes(c);
        Vector3 pivot = Pivot(a);
        string suffix = Suffix(a);
        float phiModel = Lift(k0.trR, k0.gl, axes[0]);

        // shape = 머리 폭·높이·깊이를 각각 따로 늘이고 줄이는 비율의 표준편차 / tap = 한 번 찍을 때 축마다의 오차(m)
        // mix = 섞이는 다른 두 동작 각각의 표준편차(도) / drift = 기준을 찍은 뒤 몸통이 돌아간 양의 표준편차(도)
        // tilt = 몸통이 처음부터 앞뒤·좌우로 기울어 있는 양 각각의 표준편차(도)
        // «영점»은 아무것도 흔들지 않는다 — 차이가 0이 나와야 한다(확인용). «보통+기움»은 10-06 추가.
        var envs = new[]
        {
            new Env { name = "영점" },
            new Env { name = "모양만", shape = 0.05f },
            new Env { name = "좋음", shape = 0.05f, tap = 0.002f, mix = 2f, drift = 2f },
            new Env { name = "보통", shape = 0.05f, tap = 0.0035f, mix = 3f, drift = 3f },
            new Env { name = "나쁨", shape = 0.05f, tap = 0.005f, mix = 6f, drift = 6f },
            new Env { name = "보통+기움", shape = 0.05f, tap = 0.0035f, mix = 3f, drift = 3f, tilt = 5f },
        };

        var tsv = new StringBuilder();
        tsv.AppendLine("env\tmotion\tline\ttheta\tmethod\tbias\tsd\tlo95\thi95\tp95abs\twithin3\twithin5\tn");
        const int MaxCols = 7;
        var err = new float[MaxCols][];
        for (int i = 0; i < MaxCols; i++) err[i] = new float[n];
        var abs = new float[n];
        float liftSum = 0f, liftSq = 0f; int liftN = 0;

        foreach (Env e in envs)
            for (int m = 0; m < 6; m++)
            {
                var motion = (RomLineMotion)m;
                LineDef[] lines = LinesFor(motion);
                int corr = lines.Length * 2;
                int cols = corr + (m < 4 ? 1 : 0);   // 굴곡·신전 = 평균값 보정 / 측굴 = 중립선으로 세운 면(10-07)
                int o1 = ((m / 2 + 1) % 3) * 2, o2 = ((m / 2 + 2) % 3) * 2;   // 섞이는 다른 두 동작(축 부호는 뽑는 값의 부호가 맡는다)
                foreach (float th in Thetas[m])
                {
                    for (int it = 0; it < n; it++)
                    {
                        // 사람: 머리 폭·높이·깊이를 따로 늘이고 줄인다(±3σ에서 자른다).
                        Vector3 s = new Vector3(1f + Mathf.Clamp(G(rng), -3f, 3f) * e.shape, 1f + Mathf.Clamp(G(rng), -3f, 3f) * e.shape,
                                                1f + Mathf.Clamp(G(rng), -3f, 3f) * e.shape);
                        Marks k = k0;
                        k.trR = Vector3.Scale(k0.trR, s); k.trL = Vector3.Scale(k0.trL, s);
                        k.gl = Vector3.Scale(k0.gl, s); k.gn = Vector3.Scale(k0.gn, s);
                        if (m == 0 && e.name == "모양만" && th == Thetas[0][0])
                        {
                            float lf = Lift(k.trR, k.gl, axes[0]);
                            liftSum += lf; liftSq += lf * lf; liftN++;
                        }

                        Quaternion pose = Quaternion.AngleAxis(G(rng) * e.mix, axes[o1]) * Quaternion.AngleAxis(G(rng) * e.mix, axes[o2])
                                          * Quaternion.AngleAxis(th, axes[m]);
                        Quaternion drift = Quaternion.AngleAxis(G(rng) * e.drift, Vector3.up);
                        Quaternion world = Quaternion.AngleAxis(G(rng) * e.tilt, Vector3.right) * Quaternion.AngleAxis(G(rng) * e.tilt, Vector3.forward);
                        Quaternion after = drift * world;

                        // 기준(양쪽 Tr + 앞뒤 확인용 Gl)은 몸통이 돌아가기 전에, 중립·측정은 돌아간 뒤에 찍는다.
                        RomLineFrame f = RomLineMath.BuildFrame(world * k.trR + N(rng, e.tap), world * k.trL + N(rng, e.tap), world * k.gl + N(rng, e.tap));
                        Vector3 normal = RomLineMath.PlaneNormal(motion, f);
                        for (int j = 0; j < lines.Length; j++)
                        {
                            Vector3 a0 = Of(k, lines[j].a), b0 = Of(k, lines[j].b);
                            Vector3 nA = after * a0 + N(rng, e.tap), nB = after * b0 + N(rng, e.tap);
                            Vector3 mA = after * (pivot + pose * (a0 - pivot)) + N(rng, e.tap), mB = after * (pivot + pose * (b0 - pivot)) + N(rng, e.tap);
                            float direct = RomLineMath.MeasureDirect(nA, nB, mA, mB).angle;
                            err[j * 2][it] = direct - th;
                            err[j * 2 + 1][it] = RomLineMath.Measure(nA, nB, mA, mB, normal).angle - th;
                            if (m < 2 && j == 0) err[corr][it] = Corrected(direct, phiModel) - th;   // 평균값 = 기준 모델의 들린 각(그 사람의 값이 아니다)
                            if ((m == 2 || m == 3) && j == 0) err[corr][it] = NeutralPlaneAngle(nA, nB, mA, mB, direct) - th;
                        }
                    }
                    for (int i = 0; i < cols; i++)
                    {
                        double sum = 0, sq = 0; int w3 = 0, w5 = 0;
                        for (int it = 0; it < n; it++)
                        {
                            float v = err[i][it];
                            sum += v; sq += (double)v * v;
                            abs[it] = Mathf.Abs(v);
                            if (abs[it] <= 3f) w3++;
                            if (abs[it] <= 5f) w5++;
                        }
                        Array.Sort(abs);
                        Array.Sort(err[i]);
                        double mean = sum / n, sd = Math.Sqrt(Math.Max(0.0, sq / n - mean * mean));
                        string line = i == corr ? (m < 2 ? "Tr~Gl" : "Gl~Gn") : lines[i / 2].name;
                        string method = i == corr ? (m < 2 ? "평균값보정" : "중립선면") : i % 2 == 0 ? "직접" : "면";
                        tsv.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0}\t{1}\t{2}\t{3:F0}\t{4}\t{5:F3}\t{6:F3}\t{7:F3}\t{8:F3}\t{9:F3}\t{10:F4}\t{11:F4}\t{12}",
                                                     e.name, MotionName[m], line, th, method, mean, sd,
                                                     err[i][(int)(n * 0.025f)], err[i][Mathf.Min(n - 1, (int)(n * 0.975f))],
                                                     abs[Mathf.Min(n - 1, (int)(n * 0.95f))], w3 / (float)n, w5 / (float)n, n));
                    }
                }
            }

        Directory.CreateDirectory(OutDir);
        string file = Path.Combine(OutDir, "mc" + suffix + ".tsv");
        File.WriteAllText(file, tsv.ToString(), new UTF8Encoding(false));
        float lm = liftSum / Mathf.Max(1, liftN);
        sb.AppendLine("표식 — " + MarksText(k0));
        sb.AppendLine($"굴곡 Tr~Gl 선이 들린 각 — 기준 모델 {phiModel:F1}° · 모양을 흔든 사람들 평균 {lm:F1}° ± {Mathf.Sqrt(Mathf.Max(0f, liftSq / Mathf.Max(1, liftN) - lm * lm)):F1}°");
        foreach (Env e in envs)
            sb.AppendLine($"  환경 «{e.name}» — 머리 모양 ±{e.shape * 100f:F0}% · 찍는 오차 {e.tap * 1000f:F1}mm · 섞이는 동작 ±{e.mix:F0}° · 몸통 돌아감 ±{e.drift:F0}° · 몸통 기울어짐 ±{e.tilt:F0}°");
        sb.AppendLine($"결과 {file} · 조건마다 {n}회 · 씨앗 {ArgF(a, "seed", 11f):F0}");
    }

    /// <summary>들린 각 phi를 가정하고 «직접» 값을 그 면 위의 각으로 되돌린다: cosθ = (cos d − sin²φ) / cos²φ.</summary>
    private static float Corrected(float directDeg, float phiDeg)
    {
        float s = Mathf.Sin(phiDeg * Mathf.Deg2Rad), c = Mathf.Cos(phiDeg * Mathf.Deg2Rad);
        return Mathf.Acos(Mathf.Clamp((Mathf.Cos(directDeg * Mathf.Deg2Rad) - s * s) / (c * c), -1f, 1f)) * Mathf.Rad2Deg;
    }

    /// <summary>
    /// 기준(양쪽 Tr) 없이 측굴면을 세워 보는 시험(10-07, 사용자 질문 «측굴도 바닥에 수직인 면에 그리면 되지 않나»).
    /// 바닥에 수직인 면은 무수히 많아 환자의 앞뒤를 알아야 하나를 고를 수 있다 → 중립의 Gl ~ Gn 선이 기운 방향
    /// (수평 성분 — 턱 끝이 미간보다 뒤에 있다)을 앞뒤로 삼는다. 찍힌 점(오차 포함)으로 계산한다.
    /// ★앱에는 없는 방법이다 — 이득이 있는지 보려는 것. 수평 성분이 없으면(선이 수직) fallback(직접 값)을 돌려준다.
    /// </summary>
    private static float NeutralPlaneAngle(Vector3 nA, Vector3 nB, Vector3 mA, Vector3 mB, float fallback)
    {
        Vector3 h = Vector3.ProjectOnPlane(nB - nA, Vector3.up);
        if (h.sqrMagnitude < 1e-8f) return fallback;
        RomLineResult r = RomLineMath.Measure(nA, nB, mA, mB, h.normalized);
        return r.valid ? r.angle : fallback;
    }

    private static void Acc(double[] sum, double[] sq, int i, float v) { sum[i] += v; sq[i] += (double)v * v; }

    private static void Row(StringBuilder tsv, string head, string line, string method, double[] sum, double[] sq, int i, int n)
    {
        double mean = sum[i] / n, sd = Math.Sqrt(Math.Max(0.0, sq[i] / n - mean * mean));
        tsv.AppendLine(string.Format(CultureInfo.InvariantCulture, head, line, method, mean.ToString("F3", CultureInfo.InvariantCulture), sd.ToString("F3", CultureInfo.InvariantCulture)));
    }

    private static Vector3 Of(Marks k, RomLinePoint p)
    {
        switch (p) { case RomLinePoint.TrRight: return k.trR; case RomLinePoint.TrLeft: return k.trL; case RomLinePoint.Gl: return k.gl; default: return k.gn; }
    }

    /// <summary>선이 «축에 직각인 면»에서 들린 각(도).</summary>
    private static float Lift(Vector3 a, Vector3 b, Vector3 axis)
    {
        return Mathf.Asin(Mathf.Clamp01(Mathf.Abs(Vector3.Dot((b - a).normalized, axis.normalized)))) * Mathf.Rad2Deg;
    }

    /// <summary>넣은 회전이 몸에서 정말 그 동작인가 — 축 부호를 믿지 않고 결과로 본다.</summary>
    private static bool Anatomy(RomLineMotion motion, Marks k, Quaternion q, Vector3 pivot)
    {
        Vector3 gl = pivot + q * (k.gl - pivot), gn = pivot + q * (k.gn - pivot);
        switch (motion)
        {
            case RomLineMotion.Flexion: return gl.y < k.gl.y;              // 숙이면 미간이 내려간다
            case RomLineMotion.Extension: return gl.y > k.gl.y;
            case RomLineMotion.LateralRight: return gl.x > k.gl.x;         // 오른쪽으로 기울이면 미간이 오른쪽으로 간다(회전 중심이 아래)
            case RomLineMotion.LateralLeft: return gl.x < k.gl.x;
            case RomLineMotion.RotationRight: return gl.x > k.gl.x;        // 오른쪽으로 돌리면 미간이 오른쪽으로 간다
            default: return gl.x < k.gl.x;
        }
    }

    private static Vector3 N(System.Random rng, float sigma)
    {
        if (rng == null || sigma <= 0f) return Vector3.zero;
        return new Vector3(G(rng), G(rng), G(rng)) * sigma;
    }

    private static float G(System.Random rng)
    {
        double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
        return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
    }

    // ── 잡동사니 ────────────────────────────────────────────────────
    private static string Arg(Dictionary<string, string> a, string key, string def)
    {
        return a != null && a.TryGetValue(key, out string v) && v.Length > 0 ? v : def;
    }

    private static float ArgF(Dictionary<string, string> a, string key, float def)
    {
        return float.TryParse(Arg(a, key, null), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : def;
    }

    private static void Collect(Transform t, HashSet<Transform> set)
    {
        set.Add(t);
        for (int i = 0; i < t.childCount; i++) Collect(t.GetChild(i), set);
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform f = FindDeep(root.GetChild(i), name);
            if (f != null) return f;
        }
        return null;
    }
}
