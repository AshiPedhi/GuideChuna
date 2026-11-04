using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 실습 배경 — 한방병원 홀 (2026-10-27 사용자 요청 3 · 시안 V0.7 그림 7 · 7-1).
///
/// «가까운 곳은 비우고 넓이로 분위기를 낸다»(사용자). 베드 자리(원점) 반경 4m 안에는 아무것도 두지 않는다.
/// 여기서 만드는 것 = 홀 껍데기(마루 · 벽 · 높은 천장) · 둥근 목재 기둥 열 · 재질/텍스처.
/// ★<b>임시_</b>로 시작하는 오브젝트는 ChatGPT(Blender)가 만든 모델로 바꿀 자리다 — 창살 창 · 서까래 · 약장.
///   이름에 무엇으로 바꿀지 적어 둔다. 베드는 넣지 않는다(지금 베드는 방 모델 안에 붙어 있다).
///
/// 출력: Assets/Art/HanbangHall/(HanbangHall.prefab · Materials · Textures). ★열린 씬은 건드리지 않는다.
/// 배터리 · 발열: 모두 Mobile/Diffuse · 텍스처 512 이하 · 실시간 빛은 넣지 않는다(씬 빛을 그대로 받는다).
/// </summary>
public static class HanbangHallBuilder
{
    const string Dir = "Assets/Art/HanbangHall";
    const float L = 20f, Wd = 10f, Ht = 5f;   // 홀 길이(x) · 폭(z) · 천장 높이 — 시안 제안값
    const float EmptyRadius = 4f;

    [MenuItem("GuideChuna/배경/한방병원 홀 프리팹 만들기")]
    public static void Build()
    {
        Directory.CreateDirectory(Dir + "/Textures");
        Directory.CreateDirectory(Dir + "/Materials");

        var wood = Tex("마루_나무결", 512, WoodPlanks);
        var hanji = Tex("한지", 256, Hanji);
        var darkWood = Tex("짙은_나무", 256, DarkWood);

        var mFloor = Mat("마루", "Mobile/Diffuse", wood, new Vector2(L / 2.5f, Wd / 2.5f));
        var mWall = Mat("한지벽", "Mobile/Diffuse", hanji, new Vector2(L / 2f, Ht / 2f));
        var mWood = Mat("목재", "Mobile/Diffuse", darkWood, Vector2.one);
        var mCeil = Mat("천장", "Mobile/Diffuse", darkWood, new Vector2(L / 4f, Wd / 4f));
        var mStone = Mat("주춧돌", "Mobile/Diffuse", null, Vector2.one, new Color(0.62f, 0.6f, 0.56f));
        var mGlow = Mat("창빛_한지", "Unlit/Color", null, Vector2.one, new Color(1f, 0.93f, 0.8f));

        var root = new GameObject("HanbangHall");
        try
        {
            // 마루 · 천장
            Quad(root, "마루", new Vector3(0, 0, 0), Quaternion.Euler(90, 0, 0), new Vector3(L, Wd, 1), mFloor);
            Quad(root, "천장", new Vector3(0, Ht, 0), Quaternion.Euler(-90, 0, 0), new Vector3(L, Wd, 1), mCeil);
            // 벽 4면(안쪽을 본다)
            Quad(root, "벽_앞", new Vector3(0, Ht / 2, Wd / 2), Quaternion.Euler(0, 0, 0), new Vector3(L, Ht, 1), mWall);
            Quad(root, "벽_뒤", new Vector3(0, Ht / 2, -Wd / 2), Quaternion.Euler(0, 180, 0), new Vector3(L, Ht, 1), mWall);
            Quad(root, "벽_왼", new Vector3(-L / 2, Ht / 2, 0), Quaternion.Euler(0, -90, 0), new Vector3(Wd, Ht, 1), mWall);
            Quad(root, "벽_오른", new Vector3(L / 2, Ht / 2, 0), Quaternion.Euler(0, 90, 0), new Vector3(Wd, Ht, 1), mWall);
            // 머름(벽 아래 나무 띠) · 인방(위 나무 띠)
            foreach (var (n, z, ry, len) in new[] { ("앞", Wd / 2 - 0.03f, 0f, L), ("뒤", -Wd / 2 + 0.03f, 0f, L) })
            {
                Box(root, "머름_" + n, new Vector3(0, 0.45f, z), Quaternion.identity, new Vector3(len, 0.9f, 0.05f), mWood);
                Box(root, "인방_" + n, new Vector3(0, Ht - 0.6f, z), Quaternion.identity, new Vector3(len, 0.25f, 0.08f), mWood);
            }
            foreach (var (n, x) in new[] { ("왼", -L / 2 + 0.03f), ("오른", L / 2 - 0.03f) })
            {
                Box(root, "머름_" + n, new Vector3(x, 0.45f, 0), Quaternion.identity, new Vector3(0.05f, 0.9f, Wd), mWood);
                Box(root, "인방_" + n, new Vector3(x, Ht - 0.6f, 0), Quaternion.identity, new Vector3(0.08f, 0.25f, Wd), mWood);
            }

            // 기둥 열 — 긴 벽에서 1m 안쪽 · 4m 간격(베드 반경 4m 밖만)
            var cols = new GameObject("기둥 열"); cols.transform.SetParent(root.transform, false);
            for (float x = -8f; x <= 8.01f; x += 4f)
                foreach (float z in new[] { Wd / 2 - 1f, -Wd / 2 + 1f })
                {
                    if (new Vector2(x, z).magnitude < EmptyRadius) continue;
                    var c = Prim(cols, PrimitiveType.Cylinder, $"기둥 ({x:0},{z:0.0})", new Vector3(x, Ht / 2, z), Quaternion.identity, new Vector3(0.36f, Ht / 2, 0.36f), mWood);
                    Prim(cols, PrimitiveType.Cube, "주춧돌", new Vector3(x, 0.1f, z), Quaternion.identity, new Vector3(0.6f, 0.2f, 0.6f), mStone);
                }

            // 대들보(긴 방향) — 서까래는 ChatGPT 몫이라 큰 보만 둔다
            foreach (float z in new[] { Wd / 2 - 1f, -Wd / 2 + 1f })
                Box(root, "대들보", new Vector3(0, Ht - 0.2f, z), Quaternion.identity, new Vector3(L, 0.4f, 0.4f), mWood);

            // ── 임시_: ChatGPT 모델로 바꿀 자리 ──
            var tmp = new GameObject("임시_ChatGPT로 교체할 자리"); tmp.transform.SetParent(root.transform, false);
            // 창(긴 벽 · 기둥 사이 · 높이 1.4~3.4m) — 빛이 비치는 한지만 둔다
            for (float x = -6f; x <= 6.01f; x += 4f)
                foreach (var (z, ry) in new[] { (Wd / 2 - 0.02f, 0f), (-Wd / 2 + 0.02f, 180f) })   // Quad 앞면은 -Z — 벽과 같은 회전이어야 안을 본다
                    Quad(tmp, "임시_창 → 띠살 · 빗살 창살 창", new Vector3(x, 2.4f, z), Quaternion.Euler(0, ry, 0), new Vector3(2.4f, 2.0f, 1), mGlow);
            // 약장(짧은 벽)
            foreach (var (x, ry) in new[] { (-L / 2 + 0.3f, 90f), (L / 2 - 0.3f, -90f) })
                Box(tmp, "임시_약장 → 한약 서랍장(백자장)", new Vector3(x, 1.0f, 0), Quaternion.Euler(0, ry, 0), new Vector3(5f, 2.0f, 0.5f), mWood);
            // 서까래(천장 짧은 방향 · 0.6m 간격) — 자리만
            var raf = new GameObject("임시_서까래 → 노출 서까래 천장"); raf.transform.SetParent(tmp.transform, false);
            for (float x = -9.6f; x <= 9.61f; x += 0.6f)
                Box(raf, "서까래", new Vector3(x, Ht - 0.05f, 0), Quaternion.identity, new Vector3(0.12f, 0.1f, Wd), mWood);

            // 베드 자리 표시(보이지 않는 빈 오브젝트) — 홀을 놓을 때 이 점을 베드에 맞춘다
            new GameObject("베드 자리 (원점 · 반경 4m 비움)").transform.SetParent(root.transform, false);

            foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                GameObjectUtility.SetStaticEditorFlags(mr.gameObject, StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            }
            foreach (var col in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);   // 충돌체 없음(손 판정 간섭 방지)

            string path = Dir + "/HanbangHall.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            int tris = 0; foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true)) tris += mf.sharedMesh.triangles.Length / 3;
            Debug.Log($"[HanbangHall] 프리팹 저장 {path} · 삼각형 {tris} · 홀 {L}×{Wd}×{Ht}m · 반경 {EmptyRadius}m 비움");
        }
        finally { Object.DestroyImmediate(root); }
        AssetDatabase.SaveAssets();
    }

    /// <summary>프리팹을 씬 밖 먼 자리(+1000m)에 잠깐 띄워 베드 자리 눈높이에서 네 방향을 찍는다. 열린 씬을 dirty로 만들지 않는다.</summary>
    [MenuItem("GuideChuna/배경/한방병원 홀 미리보기 찍기")]
    public static void Preview()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/HanbangHall.prefab");
        if (prefab == null) { Debug.LogWarning("[HanbangHall] 프리팹이 없다 — 먼저 «프리팹 만들기»"); return; }
        Vector3 off = new Vector3(1000, 0, 0);
        var inst = Object.Instantiate(prefab, off, Quaternion.identity);
        inst.hideFlags = HideFlags.HideAndDontSave;
        var lightGo = new GameObject("미리보기 빛") { hideFlags = HideFlags.HideAndDontSave };
        var lt = lightGo.AddComponent<Light>(); lt.type = LightType.Point; lt.range = 25; lt.intensity = 1.6f; lt.color = new Color(1f, 0.92f, 0.82f);
        lightGo.transform.position = off + new Vector3(0, 4.2f, 0);
        var camGo = new GameObject("미리보기 카메라") { hideFlags = HideFlags.HideAndDontSave };
        var cam = camGo.AddComponent<Camera>(); cam.fieldOfView = 90; cam.nearClipPlane = 0.05f; cam.farClipPlane = 60f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
        var rt = new RenderTexture(1280, 720, 24); cam.targetTexture = rt;
        string outDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Captures", "UIPreview");
        Directory.CreateDirectory(outDir);
        var img = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        foreach (var (name, yaw, pitch) in new[] { ("홀_앞", 0f, 5f), ("홀_오른", 90f, 5f), ("홀_뒤", 180f, 5f), ("홀_왼", 270f, 5f), ("홀_천장쪽", 0f, -35f) })
        {
            camGo.transform.SetPositionAndRotation(off + new Vector3(0.6f, 1.6f, 0), Quaternion.Euler(pitch, yaw, 0));
            cam.Render();
            RenderTexture.active = rt; img.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); img.Apply(); RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(outDir, name + ".png"), img.EncodeToPNG());
        }
        cam.targetTexture = null;
        Object.DestroyImmediate(img); Object.DestroyImmediate(rt);
        Object.DestroyImmediate(camGo); Object.DestroyImmediate(lightGo); Object.DestroyImmediate(inst);
        Debug.Log($"[HanbangHall] 미리보기 5장 → {outDir}");
    }

    // ── 도우미 ──
    static GameObject Prim(GameObject parent, PrimitiveType t, string name, Vector3 pos, Quaternion rot, Vector3 scale, Material m)
    {
        var g = GameObject.CreatePrimitive(t); g.name = name;
        g.transform.SetParent(parent.transform, false);
        g.transform.SetLocalPositionAndRotation(pos, rot); g.transform.localScale = scale;
        g.GetComponent<MeshRenderer>().sharedMaterial = m;
        return g;
    }
    static GameObject Quad(GameObject p, string n, Vector3 pos, Quaternion rot, Vector3 s, Material m) => Prim(p, PrimitiveType.Quad, n, pos, rot, s, m);
    static GameObject Box(GameObject p, string n, Vector3 pos, Quaternion rot, Vector3 s, Material m) => Prim(p, PrimitiveType.Cube, n, pos, rot, s, m);

    static Material Mat(string name, string shader, Texture2D tex, Vector2 tiling, Color? color = null)
    {
        string path = $"{Dir}/Materials/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, path); }
        else m.shader = Shader.Find(shader);
        if (tex != null) { m.mainTexture = tex; m.mainTextureScale = tiling; }
        if (color.HasValue) m.color = color.Value;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Texture2D Tex(string name, int size, System.Func<int, int, int, Color> f)
    {
        string path = $"{Dir}/Textures/{name}.png";
        var t = new Texture2D(size, size, TextureFormat.RGB24, true);
        var px = new Color[size * size];
        for (int y = 0; y < size; y++) for (int x = 0; x < size; x++) px[y * size + x] = f(x, y, size);
        t.SetPixels(px); t.Apply();
        File.WriteAllBytes(path, t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.maxTextureSize = size; imp.wrapMode = TextureWrapMode.Repeat; imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // 마루: 판 4장(가로) · 판마다 색조 다름 · 나무결 줄무늬 · 판 이음매
    static Color WoodPlanks(int x, int y, int s)
    {
        int plank = y * 4 / s;
        float py = (y * 4f / s) - plank;
        float seed = Mathf.Sin(plank * 12.9898f) * 43758.5453f; seed -= Mathf.Floor(seed);
        float grain = Mathf.PerlinNoise(x * 0.012f + plank * 7.1f, y * 0.25f) * 0.5f + Mathf.PerlinNoise(x * 0.05f, y * 0.9f + plank) * 0.15f;
        Color baseC = Color.Lerp(new Color(0.55f, 0.38f, 0.22f), new Color(0.66f, 0.47f, 0.29f), seed);
        Color c = Color.Lerp(baseC * 0.82f, baseC * 1.08f, grain);
        int joint = (int)((x + plank * 173) % s);
        if (py < 0.015f || py > 0.985f || joint < 2) c *= 0.55f;
        c.a = 1; return c;
    }
    // 한지: 밝은 미색 + 섬유 얼룩
    static Color Hanji(int x, int y, int s)
    {
        float n = Mathf.PerlinNoise(x * 0.08f, y * 0.08f) * 0.6f + Mathf.PerlinNoise(x * 0.4f, y * 0.03f) * 0.4f;
        return Color.Lerp(new Color(0.88f, 0.84f, 0.76f), new Color(0.95f, 0.92f, 0.86f), n);
    }
    static Color DarkWood(int x, int y, int s)
    {
        float n = Mathf.PerlinNoise(x * 0.02f, y * 0.3f) * 0.7f + Mathf.PerlinNoise(x * 0.1f, y * 0.8f) * 0.3f;
        return Color.Lerp(new Color(0.24f, 0.15f, 0.09f), new Color(0.36f, 0.23f, 0.14f), n);
    }
}
