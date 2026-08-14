using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 두개골을 <b>부위별로 색을 나눠 칠하는</b> 에디터 도구 (해부학 도해와 같은 방식).
///
/// 분리 두개골 프리팹(전두골·두정골·측두골·후두골·접형골·관골·상악골·하악골·비골이
/// 개별 오브젝트)이 씬에 들어와 있어야 한다.
///
/// ■ 동작
///   · 부위 이름으로 오브젝트를 찾아(부분 일치) 그 아래 렌더러 전부에 부위 색 머티리얼을 건다.
///   · 머티리얼은 <b>기존 뼈 머티리얼을 복제해 색만 바꾼다</b> → 셰이더·텍스처·질감이 그대로 유지된다.
///   · 만든 머티리얼은 <c>Assets/_JDH/BoneColors/</c> 에 에셋으로 저장돼 씬 저장 후에도 남는다.
///
/// ■ 되돌리기
///   원래 머티리얼을 <see cref="SkullBoneColorizer"/> 에 기록해 두므로 [되돌리기] 한 번이면 복구된다.
///   (Ctrl+Z 와 별개로 언제든 가능)
///
/// ★이름 함정: <c>코뼈(비골)_nasal bone</c> 과 <c>종아리뼈(비골)_fibula</c> 가 '비골'을 공유한다.
///   그래서 키워드는 '비골'이 아니라 <b>'코뼈'</b>를 쓴다. 통짜 구버전(<c>skull_Old</c>)도 건드리지 않는다.
/// </summary>
public class SkullBoneColorTool : EditorWindow
{
    // ── 부위표 ───────────────────────────────────────────────────────────────
    // 색은 첨부한 해부 도해(측면도)를 따랐다. 실습 기준에 맞게 창에서 바꿔도 된다.
    private class Part
    {
        public string label;        // 부위 이름
        public Color color;
        public string[] keys;       // 오브젝트 이름에 이게 들어 있으면 그 부위 (부분 일치, 대소문자 무시)
        public Part(string l, string hex, params string[] k)
        {
            label = l; keys = k;
            ColorUtility.TryParseHtmlString(hex, out color);
        }
    }

    private static readonly Part[] Parts =
    {
        new Part("전두골",     "#F2EAD0", "이마뼈", "Frontal"),
        new Part("두정골",     "#B9D08A", "마루뼈", "Parietal"),
        new Part("측두골",     "#D79A9E", "관자뼈", "Temporal"),
        new Part("후두골",     "#93A3CE", "뒤통수뼈", "Occipital"),
        new Part("접형골",     "#C89B57", "나비뼈", "Sphenoid"),
        new Part("관골(협골)", "#A5D6A0", "광대뼈", "Zygomatic"),
        new Part("상악골",     "#EFC960", "위턱뼈", "Maxilla"),
        new Part("하악골",     "#6B8DD6", "아래턱", "jaw_bone", "Mandible"),
        new Part("비골(코뼈)", "#7FD3E3", "코뼈",   "nasal"),
    };

    private const string MaterialFolder = "Assets/_JDH/BoneColors";

    private Color[] colors;      // 창에서 편집 중인 색(Parts의 기본색으로 시작)
    private string status = "";
    private Vector2 scroll;

    [MenuItem("GuideChuna/두개골 부위별 색상")]
    private static void Open()
    {
        var w = GetWindow<SkullBoneColorTool>("두개골 부위별 색상");
        w.minSize = new Vector2(360, 480);
        w.Init();
    }

    private void Init()
    {
        colors = new Color[Parts.Length];
        for (int i = 0; i < Parts.Length; i++) colors[i] = Parts[i].color;
    }

    private void OnGUI()
    {
        if (colors == null || colors.Length != Parts.Length) Init();

        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.HelpBox(
            "두개골을 부위별로 색을 나눠 칠합니다.\n" +
            "기존 뼈 머티리얼을 복제해 색만 바꾸므로 질감은 그대로입니다.\n" +
            "원래 머티리얼을 기록해 두니 [되돌리기]로 언제든 복구됩니다.", MessageType.Info);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("부위별 색상", EditorStyles.boldLabel);
        for (int i = 0; i < Parts.Length; i++)
            colors[i] = EditorGUILayout.ColorField(Parts[i].label, colors[i]);

        EditorGUILayout.Space();
        if (GUILayout.Button("기본 색상으로 되돌리기(창 안에서만)")) Init();

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("부위별 색상 적용", GUILayout.Height(30))) Apply();
            if (GUILayout.Button("되돌리기(원래 머티리얼)", GUILayout.Height(30))) Revert();
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("어떤 오브젝트가 잡히는지 미리 보기 (변경 없음)")) Preview();

        if (!string.IsNullOrEmpty(status))
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("결과", EditorStyles.boldLabel);
            EditorGUILayout.TextArea(status, GUILayout.MinHeight(120));
        }

        EditorGUILayout.EndScrollView();
    }

    // ── 적용 ────────────────────────────────────────────────────────────────
    private void Apply()
    {
        List<Transform> bones = CollectBones();
        if (bones.Count == 0)
        {
            status = "씬에서 골격(skeletal_system)을 찾지 못했습니다.\n해부 모델이 씬에 있는지 확인하세요.";
            return;
        }

        SkullBoneColorizer store = EnsureStore(bones[0]);
        Undo.RecordObject(store, "두개골 부위별 색상");

        if (!AssetDatabase.IsValidFolder(MaterialFolder))
        {
            if (!Directory.Exists(MaterialFolder)) Directory.CreateDirectory(MaterialFolder);
            AssetDatabase.Refresh();   // 폴더를 만든 직후엔 Refresh 해야 CreateAsset이 먹는다
        }

        var sb = new StringBuilder();
        int painted = 0, missing = 0;

        for (int i = 0; i < Parts.Length; i++)
        {
            Part part = Parts[i];
            List<Renderer> targets = FindRenderers(bones, part);

            if (targets.Count == 0)
            {
                missing++;
                sb.AppendLine($"✗ {part.label} — 오브젝트를 못 찾음 (키워드: {string.Join(", ", part.keys)})");
                continue;
            }

            // 이 부위의 머티리얼 — 첫 렌더러의 현재 머티리얼을 본보기로 복제한다(셰이더·텍스처 유지).
            Material template = FirstOriginalMaterial(store, targets);
            Material mat = MakeOrUpdateMaterial(part.label, template, colors[i]);

            foreach (Renderer r in targets)
            {
                RememberOriginal(store, part.label, r);   // ★이미 기록돼 있으면 덮어쓰지 않는다
                Undo.RecordObject(r, "두개골 부위별 색상");

                var arr = new Material[r.sharedMaterials.Length == 0 ? 1 : r.sharedMaterials.Length];
                for (int s = 0; s < arr.Length; s++) arr[s] = mat;
                r.sharedMaterials = arr;
                EditorUtility.SetDirty(r);
                painted++;
            }

            sb.AppendLine($"✓ {part.label} — 렌더러 {targets.Count}개");
        }

        store.applied = true;
        EditorUtility.SetDirty(store);
        AssetDatabase.SaveAssets();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(store.gameObject.scene);

        status = $"부위별 색상 적용 완료 — 렌더러 {painted}개" +
                 (missing > 0 ? $" (못 찾은 부위 {missing}개)" : "") + "\n\n" + sb +
                 $"\n머티리얼: {MaterialFolder}\n복원 기록: {Path(store.transform)}";
    }

    // ── 되돌리기 ────────────────────────────────────────────────────────────
    private void Revert()
    {
        var store = Object.FindFirstObjectByType<SkullBoneColorizer>(FindObjectsInactive.Include);
        if (store == null || store.entries.Count == 0)
        {
            status = "복원 기록이 없습니다. (아직 색상을 적용한 적이 없거나 기록이 지워졌습니다)";
            return;
        }

        Undo.RecordObject(store, "두개골 색상 되돌리기");
        int restored = 0, lost = 0;

        foreach (var e in store.entries)
        {
            if (e.renderer == null) { lost++; continue; }
            Undo.RecordObject(e.renderer, "두개골 색상 되돌리기");
            e.renderer.sharedMaterials = e.original ?? new Material[0];
            EditorUtility.SetDirty(e.renderer);
            restored++;
        }

        store.entries.Clear();
        store.applied = false;
        EditorUtility.SetDirty(store);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(store.gameObject.scene);

        status = $"원래 머티리얼로 되돌렸습니다 — 렌더러 {restored}개" +
                 (lost > 0 ? $"\n★{lost}개는 오브젝트가 사라져 복원하지 못했습니다(프리팹 교체 등)." : "");
    }

    // ── 미리 보기 ───────────────────────────────────────────────────────────
    private void Preview()
    {
        List<Transform> bones = CollectBones();
        var sb = new StringBuilder($"골격 오브젝트 {bones.Count}개 중 부위별 매칭\n\n");

        foreach (Part part in Parts)
        {
            var hits = FindMatches(bones, part);
            sb.AppendLine($"■ {part.label} ({string.Join(", ", part.keys)}) — {hits.Count}개");
            foreach (Transform t in hits)
                sb.AppendLine($"   · {t.name}  [렌더러 {t.GetComponentsInChildren<Renderer>(true).Length}개]");
        }
        status = sb.ToString();
    }

    // ── 내부 ────────────────────────────────────────────────────────────────

    /// <summary>부위 키워드로 오브젝트를 찾는다. 통짜 구버전(skull_Old)은 제외.</summary>
    private static List<Transform> FindMatches(List<Transform> bones, Part part)
    {
        var hits = new List<Transform>();
        foreach (Transform t in bones)
        {
            if (t.name.IndexOf("skull_Old", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;

            foreach (string key in part.keys)
            {
                if (t.name.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) < 0) continue;

                // 이미 담은 것의 자식이면 건너뛴다(부모 한 번으로 아래 렌더러를 다 가져가므로).
                bool nested = false;
                foreach (Transform h in hits)
                    if (t.IsChildOf(h)) { nested = true; break; }
                if (!nested) hits.Add(t);
                break;
            }
        }
        return hits;
    }

    private static List<Renderer> FindRenderers(List<Transform> bones, Part part)
    {
        var list = new List<Renderer>();
        foreach (Transform t in FindMatches(bones, part))
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>(true))
                if (!list.Contains(r)) list.Add(r);
        return list;
    }

    /// <summary>색을 입히기 전의 머티리얼을 고른다 — 이미 칠한 뒤라면 기록해 둔 원본을 쓴다.</summary>
    private static Material FirstOriginalMaterial(SkullBoneColorizer store, List<Renderer> targets)
    {
        foreach (Renderer r in targets)
        {
            foreach (var e in store.entries)
                if (e.renderer == r && e.original != null && e.original.Length > 0 && e.original[0] != null)
                    return e.original[0];

            if (r.sharedMaterial != null) return r.sharedMaterial;
        }
        return null;
    }

    private static Material MakeOrUpdateMaterial(string label, Material template, Color color)
    {
        string path = $"{MaterialFolder}/Bone_{label}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (mat == null)
        {
            mat = template != null
                ? new Material(template)
                : new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(mat, path);
        }

        SetColor(mat, color);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>셰이더마다 색 프로퍼티 이름이 달라 있는 것을 전부 세팅한다.</summary>
    private static void SetColor(Material mat, Color color)
    {
        string[] names = { "_Color", "_BaseColor", "_Diffuse", "_DiffuseColor", "_TintColor" };
        foreach (string n in names)
            if (mat.HasProperty(n)) mat.SetColor(n, color);
    }

    private static void RememberOriginal(SkullBoneColorizer store, string part, Renderer r)
    {
        foreach (var e in store.entries)
            if (e.renderer == r) return;    // ★재적용 시 우리가 칠한 색을 '원본'으로 덮어쓰지 않게

        store.entries.Add(new SkullBoneColorizer.Entry
        {
            part = part,
            renderer = r,
            original = r.sharedMaterials
        });
    }

    /// <summary>복원 기록을 담을 컴포넌트를 골격 루트에 확보한다.</summary>
    private static SkullBoneColorizer EnsureStore(Transform anyBone)
    {
        var store = Object.FindFirstObjectByType<SkullBoneColorizer>(FindObjectsInactive.Include);
        if (store != null) return store;

        Transform root = anyBone;
        while (root.parent != null &&
               root.name.IndexOf("skeletal_system", System.StringComparison.OrdinalIgnoreCase) < 0)
            root = root.parent;

        store = Undo.AddComponent<SkullBoneColorizer>(root.gameObject);
        return store;
    }

    /// <summary>씬의 골격 오브젝트를 모은다(skeletal_system 아래 전부, 중첩 포함).</summary>
    private static List<Transform> CollectBones()
    {
        var roots = new List<Transform>();
        foreach (Transform t in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (t == null || !t.gameObject.scene.IsValid()) continue;
            if (t.name.Equals("skeletal_system", System.StringComparison.OrdinalIgnoreCase))
                roots.Add(t);
        }

        var bones = new List<Transform>();
        foreach (Transform root in roots)
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t != root && !bones.Contains(t))
                    bones.Add(t);
        return bones;
    }

    private static string Path(Transform t)
    {
        string p = t.name;
        while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }
}
