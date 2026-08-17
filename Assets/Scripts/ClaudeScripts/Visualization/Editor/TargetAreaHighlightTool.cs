using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 타겟 부위 하이라이트 만들기 — 뼈 메시에서 <b>지정한 영역의 조각만 뽑아</b> 원본 위에 얹는다.
///
/// ★왜 이 방식인가(2026-08-14): 사용자가 "흉추에서 횡돌기 부분만 모양을 맞춰 강조하고 싶다"고 했는데,
/// 씬의 흉추는 <c>thoracic_spine</c> <b>오브젝트 하나</b>다(극돌기·횡돌기가 안 나뉘어 있다 — 실측).
/// 골격 표시는 렌더러 on/off라 <b>오브젝트가 최소 단위</b>여서 부위 일부만 강조할 수 없다.
///   · Blender로 메시를 자르는 방법 → 모델링 왕복이 필요하고 원본 재임포트 위험이 있다
///   · 셰이더로 좌표 마스킹 → 이 프로젝트에서 커스텀 셰이더는 빌드에서 두 번 죽었다
///   · <b>그래서 조각을 뽑아 얹는다</b> — 원본을 안 건드리고, 되돌리기는 오브젝트 삭제면 끝이다
///
/// 절차: ① 추출 박스를 만든다 → 씬 뷰에서 부위에 맞춘다 → ② 버튼을 누른다.
/// 만들어진 조각은 <c>Assets/_Generated/BoneHighlights/</c>에 메시 에셋으로 저장된다.
/// </summary>
public class TargetAreaHighlightTool : EditorWindow
{
    private const string GenDir = "Assets/_Generated/BoneHighlights";
    private const string BoxName = "▣ 추출 영역 (맞춘 뒤 ② 실행)";

    private Renderer source;
    private Transform box;

    private string outputName = "횡돌기 하이라이트";
    private TargetAreaHighlight.Role role = TargetAreaHighlight.Role.주동수;
    private float inflateMm = 0.4f;
    private bool deleteBoxAfter = true;
    private bool wholeTriangleInside = false;

    /// <summary>박스 없이 원본 뼈 <b>전체</b>를 하이라이트로 뜬다(2026-08-17 신설).
    /// ★그래도 오브젝트는 원본의 <b>자식 오버레이</b>로 만든다 — 원본 뼈에 직접
    /// <see cref="TargetAreaHighlight"/>를 붙이면 <c>ForceArrowDirector</c>가 표시를 끌 때
    /// <c>SetActive(false)</c>로 <b>뼈 자체가 사라진다.</b></summary>
    private bool useWholeMesh = false;

    private string report = "";
    private Vector2 scroll;

    [MenuItem("GuideChuna/타겟 부위 하이라이트 만들기 (뼈 일부 추출)")]
    public static void Open()
    {
        var w = GetWindow<TargetAreaHighlightTool>(true, "타겟 부위 하이라이트");
        w.minSize = new Vector2(520, 560);
        w.PickFromSelection();
    }

    private void OnSelectionChange()
    {
        PickFromSelection();
        Repaint();
    }

    /// <summary>선택한 오브젝트에서 원본 렌더러를 집어 온다(자식까지 훑는다).</summary>
    private void PickFromSelection()
    {
        if (Selection.activeGameObject == null) return;
        if (Selection.activeGameObject.transform == box) return;   // 박스를 고른 건 무시

        var r = Selection.activeGameObject.GetComponent<Renderer>();
        if (r == null) r = Selection.activeGameObject.GetComponentInChildren<Renderer>(true);
        if (r != null && (r is MeshRenderer || r is SkinnedMeshRenderer)) source = r;
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "뼈 메시에서 하이라이트 오브젝트를 만듭니다 — 영역만 뽑거나, 뼈 전체를 뜨거나.\n" +
            "원본 메시는 건드리지 않습니다 — 되돌리려면 만들어진 오브젝트를 지우면 됩니다.\n" +
            "예1: 흉추(thoracic_spine) 전체를 통째로 강조 → ②에서 '박스 없이' 켜기\n" +
            "예2: 흉추에서 좌우 횡돌기만 뽑아 주동수/보조수 색으로 각각 강조 → 박스 사용",
            MessageType.Info);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("① 원본 뼈", EditorStyles.boldLabel);
        source = (Renderer)EditorGUILayout.ObjectField(
            new GUIContent("원본 렌더러", "Hierarchy에서 뼈를 선택하면 자동으로 채워집니다."),
            source, typeof(Renderer), true);

        if (source != null)
        {
            Mesh m = MeshOf(source);
            EditorGUILayout.LabelField("   메시", m != null ? $"{m.name} (정점 {m.vertexCount:N0})" : "★없음");
            if (m != null && !m.isReadable)
                EditorGUILayout.HelpBox(
                    "이 메시는 Read/Write가 꺼져 있습니다. 추출할 때 임포트 설정을 자동으로 켜고 다시 임포트합니다.\n" +
                    "★해부 모델처럼 큰 FBX는 재임포트에 몇 분 걸릴 수 있습니다.", MessageType.Warning);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("② 추출 영역", EditorStyles.boldLabel);

        useWholeMesh = EditorGUILayout.ToggleLeft(
            new GUIContent("박스 없이 — 이 뼈 전체를 하이라이트",
                           "흉추(thoracic_spine)처럼 뼈 하나를 통째로 강조할 때. 박스를 맞출 필요가 없습니다.\n" +
                           "부위만 집으려면 끄고 박스를 쓰세요."),
            useWholeMesh);

        using (new EditorGUI.DisabledScope(useWholeMesh))
        {
            box = (Transform)EditorGUILayout.ObjectField("영역 박스", box, typeof(Transform), true);

            using (new EditorGUI.DisabledScope(source == null))
            {
                if (GUILayout.Button("추출 박스 만들기 / 원본 앞에 놓기", GUILayout.Height(24)))
                    CreateBox();
            }
            EditorGUILayout.LabelField(
                "박스를 씬 뷰에서 옮기고 크기를 조절해 원하는 부위(예: 횡돌기)를 감싸세요.\n" +
                "박스는 표시용일 뿐이고 회전시켜도 됩니다.",
                EditorStyles.wordWrappedMiniLabel);
        }

        if (useWholeMesh)
            EditorGUILayout.HelpBox(
                "뼈 전체를 뜹니다. 원본 뼈는 그대로 두고 살짝 띄운 껍데기를 자식으로 얹습니다.\n" +
                "★원본 뼈에 직접 붙이면 안 됩니다 — Director가 표시를 끌 때 SetActive(false)로 " +
                "뼈 자체가 사라집니다. 이 도구는 항상 자식 오버레이로 만듭니다.",
                MessageType.Info);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("③ 만들기", EditorStyles.boldLabel);
        outputName = EditorGUILayout.TextField("이름", outputName);
        role = (TargetAreaHighlight.Role)EditorGUILayout.EnumPopup(
            new GUIContent("역할", "주동수=진한 녹색 / 보조수=연한 녹색 / 중립=노란끼"), role);
        inflateMm = EditorGUILayout.Slider(
            new GUIContent("부풀림(mm)", "노멀 방향으로 살짝 띄워 원본과 겹쳐 깜빡이는 것(Z-파이팅)을 막습니다."),
            inflateMm, 0f, 3f);
        wholeTriangleInside = EditorGUILayout.Toggle(
            new GUIContent("삼각형 전체가 들어와야 포함", "끄면 삼각형 중심이 들어오면 포함합니다(기본, 경계가 자연스럽습니다)."),
            wholeTriangleInside);
        deleteBoxAfter = EditorGUILayout.Toggle("만든 뒤 박스 삭제", deleteBoxAfter);

        using (new EditorGUI.DisabledScope(source == null || (!useWholeMesh && box == null)))
        {
            GUI.backgroundColor = new Color(0.75f, 1f, 0.8f);
            if (GUILayout.Button(useWholeMesh ? "이 뼈 전체로 하이라이트 만들기" : "이 영역으로 하이라이트 만들기",
                                 GUILayout.Height(30)))
                Extract();
            GUI.backgroundColor = Color.white;
        }

        EditorGUILayout.Space();
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.TextArea(report, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }

    private static Mesh MeshOf(Renderer r)
    {
        if (r is SkinnedMeshRenderer smr) return smr.sharedMesh;
        var mf = r.GetComponent<MeshFilter>();
        return mf != null ? mf.sharedMesh : null;
    }

    /// <summary>원본 바운즈 중앙에 손에 잡히는 크기의 박스를 만든다.</summary>
    private void CreateBox()
    {
        if (box != null) DestroyImmediate(box.gameObject);

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = BoxName;
        var col = go.GetComponent<Collider>();
        if (col != null) DestroyImmediate(col);

        // 반투명 표시용 머티리얼(에디터에서 보이기만 하면 된다).
        var mr = go.GetComponent<MeshRenderer>();
        var mat = new Material(Shader.Find("Standard"));
        mat.SetFloat("_Mode", 3f);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.renderQueue = 3000;
        mat.color = new Color(0.2f, 0.9f, 1f, 0.25f);
        mr.sharedMaterial = mat;

        Bounds b = source.bounds;
        float size = Mathf.Clamp(b.size.magnitude * 0.12f, 0.02f, 0.2f);
        go.transform.position = b.center;
        go.transform.localScale = Vector3.one * size;

        Undo.RegisterCreatedObjectUndo(go, "추출 박스 만들기");
        box = go.transform;
        Selection.activeGameObject = go;

        report = $"추출 박스를 만들었습니다({size * 100f:F1}cm).\n" +
                 "씬 뷰에서 원하는 부위로 옮기고 크기를 맞춘 뒤 아래 버튼을 누르세요.";
    }

    private void Extract()
    {
        Mesh src = MeshOf(source);
        if (src == null) { report = "★원본 메시가 없습니다."; return; }

        // ── 메시를 읽을 수 있게 만든다 ────────────────────────────────────────
        // ★FBX는 기본적으로 Read/Write가 꺼져 있어 vertices 접근이 막힌다.
        if (!src.isReadable)
        {
            string path = AssetDatabase.GetAssetPath(src);
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null)
            {
                report = $"★메시를 읽을 수 없고 임포터도 못 찾았습니다.\n경로: {path}";
                return;
            }

            Debug.Log($"[타겟 하이라이트] Read/Write를 켜고 다시 임포트합니다 — {path}\n" +
                      "큰 FBX면 몇 분 걸릴 수 있습니다.");
            imp.isReadable = true;
            imp.SaveAndReimport();

            src = MeshOf(source);
            if (src == null || !src.isReadable)
            {
                report = "★재임포트 후에도 메시를 읽을 수 없습니다.";
                return;
            }
        }

        // 스킨드 메시면 현재 포즈를 구워서 쓴다(정적 뼈에는 해당 없음).
        Mesh work = src;
        if (source is SkinnedMeshRenderer smr)
        {
            work = new Mesh();
            smr.BakeMesh(work);
        }

        Vector3[] verts = work.vertices;
        Vector3[] normals = work.normals;
        Vector2[] uvs = work.uv;
        bool hasNormals = normals != null && normals.Length == verts.Length;
        bool hasUv = uvs != null && uvs.Length == verts.Length;

        Transform srcT = source.transform;
        Matrix4x4 l2w = srcT.localToWorldMatrix;

        // ── 박스(OBB) 안에 드는 삼각형만 고른다 ──────────────────────────────
        var keptTris = new List<int>();
        var remap = new Dictionary<int, int>();
        var newVerts = new List<Vector3>();
        var newNormals = new List<Vector3>();
        var newUvs = new List<Vector2>();

        int totalTris = 0;
        for (int sub = 0; sub < work.subMeshCount; sub++)
        {
            int[] tris = work.GetTriangles(sub);
            totalTris += tris.Length / 3;

            for (int i = 0; i < tris.Length; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];

                Vector3 wa = l2w.MultiplyPoint3x4(verts[a]);
                Vector3 wb = l2w.MultiplyPoint3x4(verts[b]);
                Vector3 wc = l2w.MultiplyPoint3x4(verts[c]);

                bool inside = wholeTriangleInside
                    ? (Inside(wa) && Inside(wb) && Inside(wc))
                    : Inside((wa + wb + wc) / 3f);
                if (!inside) continue;

                keptTris.Add(Remap(a));
                keptTris.Add(Remap(b));
                keptTris.Add(Remap(c));
            }
        }

        int Remap(int old)
        {
            if (remap.TryGetValue(old, out int idx)) return idx;
            idx = newVerts.Count;
            remap[old] = idx;

            Vector3 n = hasNormals ? normals[old] : Vector3.up;
            newVerts.Add(verts[old] + n * (inflateMm * 0.001f));
            newNormals.Add(n);
            if (hasUv) newUvs.Add(uvs[old]);
            return idx;
        }

        bool Inside(Vector3 worldPoint)
        {
            if (useWholeMesh) return true;          // 뼈 전체 모드 — 박스를 보지 않는다
            Vector3 p = box.InverseTransformPoint(worldPoint);
            return Mathf.Abs(p.x) <= 0.5f && Mathf.Abs(p.y) <= 0.5f && Mathf.Abs(p.z) <= 0.5f;
        }

        if (keptTris.Count == 0)
        {
            report = useWholeMesh
                ? $"★원본에 면이 없습니다(삼각형 {totalTris:N0}개)."
                : $"★영역 안에 들어온 면이 없습니다(원본 삼각형 {totalTris:N0}개).\n" +
                  "박스를 뼈 표면에 겹치도록 옮기거나 키워 보세요.";
            return;
        }

        // ── 메시 에셋으로 저장 ───────────────────────────────────────────────
        var mesh = new Mesh { name = outputName };
        if (newVerts.Count > 65000)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(newVerts);
        mesh.SetNormals(newNormals);
        if (hasUv && newUvs.Count == newVerts.Count) mesh.SetUVs(0, newUvs);
        mesh.SetTriangles(keptTris, 0);
        mesh.RecalculateBounds();

        EnsureFolder();
        string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{GenDir}/{Safe(outputName)}.asset");
        AssetDatabase.CreateAsset(mesh, assetPath);

        // ── 오브젝트 생성 ────────────────────────────────────────────────────
        // ★원본 트랜스폼의 자식으로 두고 로컬 TRS를 초기화한다 — 메시 좌표가 원본 로컬 기준이라
        //   이렇게 하면 위치·회전·스케일이 원본과 정확히 겹친다.
        var go = new GameObject(outputName);
        go.transform.SetParent(srcT, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var rend = go.AddComponent<MeshRenderer>();
        rend.sharedMaterial = GetOrCreateMaterial();
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;

        var hl = go.AddComponent<TargetAreaHighlight>();
        var so = new SerializedObject(hl);
        so.FindProperty("role").enumValueIndex = (int)role;
        so.ApplyModifiedProperties();

        Undo.RegisterCreatedObjectUndo(go, "타겟 부위 하이라이트 만들기");
        AssetDatabase.SaveAssets();

        if (deleteBoxAfter && box != null)
        {
            Undo.DestroyObjectImmediate(box.gameObject);
            box = null;
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Selection.activeGameObject = go;

        report =
            $"만들었습니다 — {outputName} ({role})\n" +
            $"  면 {keptTris.Count / 3:N0}개 / 정점 {newVerts.Count:N0}개  (원본 {totalTris:N0}면 중)\n" +
            $"  메시: {assetPath}\n" +
            $"  부모: {srcT.name}\n\n" +
            "표시 타이밍은 인스펙터의 '언제 보일지'에서 정합니다(기본 = 교정 국면 전체, 파지 포함).\n" +
            "켜고 끄는 것은 씬의 ForceArrowDirector가 화살표와 같은 시점에 처리합니다 — 추가 배선이 없습니다.";
        Debug.Log("[타겟 하이라이트] " + report);
    }

    /// <summary>하이라이트용 공용 머티리얼(Standard + Emission). 색은 컴포넌트가 실행 시 입힌다.</summary>
    private static Material GetOrCreateMaterial()
    {
        string path = $"{GenDir}/TargetHighlight.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;

        EnsureFolder();
        m = new Material(Shader.Find("Standard"));
        m.color = new Color(0.149f, 1f, 0.318f, 1f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", new Color(0.149f, 1f, 0.318f) * 1.5f);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        m.SetFloat("_Glossiness", 0.1f);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    /// <summary>생성 폴더를 <b>에셋 데이터베이스가 아는 상태로</b> 만든다.
    /// ★Directory.CreateDirectory만 하면 디스크에는 생겨도 AssetDatabase가 몰라 CreateAsset이 실패한다.</summary>
    private static void EnsureFolder()
    {
        if (AssetDatabase.IsValidFolder(GenDir)) return;
        if (!AssetDatabase.IsValidFolder("Assets/_Generated"))
            AssetDatabase.CreateFolder("Assets", "_Generated");
        AssetDatabase.CreateFolder("Assets/_Generated", "BoneHighlights");
    }

    private static string Safe(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return string.IsNullOrWhiteSpace(s) ? "TargetHighlight" : s.Trim();
    }
}
