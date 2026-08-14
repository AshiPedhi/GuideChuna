using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 파지점 구체의 색을 씬 전체에 일괄 적용하는 도구.
/// 메뉴: GuideChuna/파지점 색상 일괄 적용
///
/// 왜 필요한가 = GripPointTarget.idleColor는 [SerializeField]라 씬에 이미 배치된 파지점은
/// 옛 값(흰색)이 직렬화돼 있다. 코드 기본값을 바꿔도 기존 오브젝트엔 반영되지 않는다.
///
/// ★비파괴 도구다 — 오브젝트를 만들거나 지우지 않고 색 필드만 바꾼다(Undo 가능).
/// </summary>
public class GripPointColorTool : EditorWindow
{
    /// <summary>새 기본 미파지색(연한 붉은색). GripPointTarget의 코드 기본값과 같게 유지할 것.</summary>
    private static readonly Color DefaultIdle = new Color(1f, 0.35f, 0.35f, 0.5f);
    private static readonly Color LegacyIdle = new Color(1f, 1f, 1f, 0.3f);

    private Color idleColor = DefaultIdle;
    private Color grippedColor = new Color(0f, 1f, 0f, 0.5019608f);   // 씬의 현재 값과 동일
    private bool applyGripped = false;
    private bool onlyLegacy = true;
    private Vector2 scroll;
    private string status = "";

    [MenuItem("GuideChuna/파지점 색상 일괄 적용")]
    public static void Open()
    {
        var w = GetWindow<GripPointColorTool>(true, "파지점 색상 일괄 적용");
        w.minSize = new Vector2(420, 340);
        w.Scan();
    }

    /// <summary>씬에 있는 모든 GripPointTarget(비활성 포함). 파지점은 단계별로 SetActive(false)라
    /// 활성 오브젝트만 찾으면 대부분을 놓친다.</summary>
    private static List<GripPointTarget> FindAll()
    {
        return Resources.FindObjectsOfTypeAll<GripPointTarget>()
            .Where(g => g != null
                        && !EditorUtility.IsPersistent(g)               // 프리팹 에셋 제외
                        && g.gameObject.scene.IsValid()                 // 씬에 있는 것만
                        && (g.hideFlags & HideFlags.HideAndDontSave) == 0)
            .ToList();
    }

    private void Scan()
    {
        var all = FindAll();
        var groups = new Dictionary<Color, int>();
        foreach (var g in all)
        {
            var so = new SerializedObject(g);
            var c = so.FindProperty("idleColor").colorValue;
            groups[c] = groups.TryGetValue(c, out int n) ? n + 1 : 1;
        }

        var lines = groups.OrderByDescending(kv => kv.Value)
            .Select(kv => $"  · {ColorText(kv.Key)}  ×{kv.Value}");
        status = $"씬의 파지점 {all.Count}개 — 현재 미파지색 분포\n" + string.Join("\n", lines);
    }

    private static string ColorText(Color c) =>
        $"R{c.r:0.##} G{c.g:0.##} B{c.b:0.##} A{c.a:0.##}";

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.HelpBox(
            "씬에 배치된 파지점의 색을 한 번에 바꾼다(오브젝트는 생성·삭제하지 않음, Undo 가능).\n" +
            "코드 기본값은 새로 만드는 파지점에만 적용되므로 기존 것은 이 버튼으로 맞춘다.",
            MessageType.Info);

        EditorGUILayout.Space();
        idleColor = EditorGUILayout.ColorField(
            new GUIContent("미파지 색", "아직 손이 안 닿은 상태의 구체 색. 흰색은 환자 피부에 묻혀 안 보인다."),
            idleColor);

        if (GUILayout.Button("연한 붉은색 기본값으로 되돌리기", GUILayout.Width(220)))
            idleColor = DefaultIdle;

        EditorGUILayout.Space();
        applyGripped = EditorGUILayout.ToggleLeft(
            new GUIContent("파지 성립 색도 함께 적용", "끄면 미파지 색만 바꾼다(보통 초록은 그대로 두면 된다)."),
            applyGripped);
        using (new EditorGUI.DisabledScope(!applyGripped))
            grippedColor = EditorGUILayout.ColorField("파지 성립 색", grippedColor);

        EditorGUILayout.Space();
        onlyLegacy = EditorGUILayout.ToggleLeft(
            new GUIContent($"기존 흰색({ColorText(LegacyIdle)})인 것만 바꾸기",
                           "끄면 손으로 따로 조정해 둔 색까지 전부 덮어쓴다."),
            onlyLegacy);

        EditorGUILayout.Space();
        if (GUILayout.Button("현재 상태 다시 스캔")) Scan();

        GUI.backgroundColor = new Color(0.7f, 1f, 0.7f);
        if (GUILayout.Button("씬의 파지점에 일괄 적용", GUILayout.Height(32))) Apply();
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("빌드 대응 (반투명이 빌드에서만 안 먹을 때)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "증상: 에디터·Link에서는 반투명인데 빌드하면 생으로(불투명) 보인다.\n" +
            "원인: 런타임에 Standard 머티리얼을 Fade로 바꾸지만, 빌드에는 그 배리언트(_ALPHABLEND_ON)가 " +
            "포함되지 않아 스트립된다. 에디터에는 모든 배리언트가 있어서 문제가 안 보인다.\n" +
            "조치: 처음부터 Fade로 저장된 머티리얼 에셋을 파지점에 물린다 — 에셋이 씬에서 참조되므로 " +
            "빌드에 그 배리언트가 확실히 포함된다.",
            MessageType.Warning);
        if (GUILayout.Button("파지점에 반투명 머티리얼 물리기 (빌드 대응)", GUILayout.Height(26)))
            ApplyFadeMaterial();

        if (!string.IsNullOrEmpty(status))
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(status, EditorStyles.wordWrappedLabel);
        }

        EditorGUILayout.EndScrollView();
    }

    private void Apply()
    {
        var all = FindAll();
        int changed = 0, skipped = 0;

        foreach (var g in all)
        {
            var so = new SerializedObject(g);
            var idle = so.FindProperty("idleColor");

            if (onlyLegacy && !Approximately(idle.colorValue, LegacyIdle)) { skipped++; continue; }

            idle.colorValue = idleColor;
            if (applyGripped) so.FindProperty("grippedColor").colorValue = grippedColor;

            // ApplyModifiedProperties가 Undo 등록과 씬 더티 표시를 함께 처리한다.
            if (so.ApplyModifiedProperties()) changed++;
        }

        status = $"적용 완료 — {changed}개 변경" + (skipped > 0 ? $", {skipped}개 건너뜀(이미 다른 색)" : "")
               + $"\n※씬을 저장해야 유지된다(Ctrl+S).";
        Debug.Log($"[GripPointColorTool] 미파지색 {ColorText(idleColor)} 적용: {changed}개 변경 / {skipped}개 건너뜀");
    }

    private const string FadeMaterialPath = "Assets/Materials/GripPointFade.mat";

    /// <summary>
    /// 파지점 구체에 <b>처음부터 Fade로 저장된</b> 머티리얼 에셋을 물린다.
    ///
    /// ★빌드에서만 반투명이 안 먹던 원인(2026-08-13): 런타임 코드가 Standard 머티리얼을
    /// <c>_Mode=3</c>으로 바꿔 Fade로 만드는데, <b>빌드에는 그 셰이더 배리언트가 없다</b> —
    /// 빌드에 포함된 어떤 머티리얼도 Fade를 안 쓰면 Unity가 그 배리언트를 스트립하기 때문이다.
    /// 에디터에는 모든 배리언트가 있어 증상이 안 보인다(사용자: "링크에선 잘 보였는데 빌드만 하면").
    /// 씬이 참조하는 머티리얼 에셋이 Fade면 그 배리언트가 반드시 컴파일되어 들어간다.
    /// </summary>
    private void ApplyFadeMaterial()
    {
        Material mat = LoadOrCreateFadeMaterial();

        var all = FindAll();
        int applied = 0, noRenderer = 0;

        foreach (var g in all)
        {
            var so = new SerializedObject(g);
            var rendProp = so.FindProperty("targetRenderer");
            var rend = rendProp != null ? rendProp.objectReferenceValue as Renderer : null;
            if (rend == null) { noRenderer++; continue; }
            if (rend.sharedMaterial == mat) continue;

            Undo.RecordObject(rend, "파지점 반투명 머티리얼");
            rend.sharedMaterial = mat;
            EditorUtility.SetDirty(rend);
            applied++;
        }

        if (applied > 0)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        status = $"반투명 머티리얼 적용 — {applied}개 변경 (파지점 {all.Count}개 중)" +
                 (noRenderer > 0 ? $", {noRenderer}개는 targetRenderer 미배선이라 건너뜀" : "") +
                 $"\n머티리얼: {FadeMaterialPath}" +
                 "\n※씬 저장(Ctrl+S) 후 다시 빌드하세요. 색은 런타임에 파지점별로 덮어씁니다.";
        Debug.Log("[GripPointColorTool] " + status);
    }

    private static Material LoadOrCreateFadeMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(FadeMaterialPath);
        if (mat != null) return mat;

        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
            AssetDatabase.CreateFolder("Assets", "Materials");

        mat = new Material(Shader.Find("Standard")) { name = "GripPointFade" };
        mat.SetFloat("_Mode", 3f);                       // Fade
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");             // ★이 키워드가 에셋에 저장돼야 빌드에 배리언트가 들어간다
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 3000;
        mat.color = DefaultIdle;

        AssetDatabase.CreateAsset(mat, FadeMaterialPath);
        AssetDatabase.SaveAssets();
        return mat;
    }

    private static bool Approximately(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f &&
        Mathf.Abs(a.b - b.b) < 0.01f && Mathf.Abs(a.a - b.a) < 0.01f;
}
