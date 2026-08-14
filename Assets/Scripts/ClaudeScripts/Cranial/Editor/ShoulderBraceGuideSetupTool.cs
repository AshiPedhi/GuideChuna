using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 이마 견착 위치 가이드(<see cref="ShoulderBraceGuide"/>) 배치 도구.
///
/// 견착 국면은 "헤드셋-이마 근접"으로 상체 숙임만 근사 판정할 뿐,
/// <b>어디에 어깨를 대야 하는지</b>를 화면에 보여주지 않았다 → 이마 표면에 접촉 패치를 놓는다.
/// ★어깨 접촉을 새로 판정하지 않는다(Quest에 어깨 트래킹 소스가 없다). 순수 표시다.
///
/// 배치 규칙:
///   · 부모 = 그 리그(CranialAdjustmentController). 환자 뼈에 자식으로 붙이지 않는다 —
///     붙이면 컨트롤러의 환자 렌더러 수집·xray가 같이 훑어 견착 순간 마커까지 사라진다.
///   · 위치는 런타임에 이마 타겟을 따라간다(localOffset 보정) → <b>씬 뷰에서 오프셋만 눈으로 맞추면 된다.</b>
/// </summary>
public static class ShoulderBraceGuideSetupTool
{
    private const string MaterialPath = "Assets/Materials/ShoulderBraceGuide.mat";
    private const string GuideName = "어깨 견착 가이드";

    [MenuItem("GuideChuna/견착 가이드 배치 (이마)")]
    public static void PlaceGuides()
    {
        var stabilizers = Object.FindObjectsByType<CranialPostureStabilizer>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (stabilizers.Length == 0)
        {
            EditorUtility.DisplayDialog("견착 가이드",
                "씬에 CranialPostureStabilizer가 없습니다.\n" +
                "견착이 있는 술기(두개골 OM·PJ)의 리그를 먼저 확인하세요.", "확인");
            return;
        }

        var existing = Object.FindObjectsByType<ShoulderBraceGuide>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        var log = new StringBuilder();
        int created = 0, skipped = 0, noTarget = 0;

        foreach (var st in stabilizers)
        {
            string rigName = st.transform.root != null ? PathOf(st.transform) : st.name;

            if (FindGuideFor(st, existing) != null)
            {
                skipped++;
                log.AppendLine($"· 건너뜀(이미 있음): {rigName}");
                continue;
            }

            if (st.ForeheadTarget == null)
            {
                noTarget++;
                log.AppendLine($"· ★이마 타겟 미배선: {rigName} — foreheadTarget을 먼저 연결하세요(보통 CC_Base_Head).");
                continue;
            }

            Transform parent = ResolveParent(st);
            var go = BuildGuide(parent, st);
            Undo.RegisterCreatedObjectUndo(go, "견착 가이드 배치");
            created++;
            log.AppendLine($"· 생성: {PathOf(go.transform)}  (기준 {st.ForeheadTarget.name})");
        }

        if (created > 0)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        string summary = $"생성 {created} / 건너뜀 {skipped} / 이마 타겟 없음 {noTarget}";
        ChunaEditorLog($"[견착 가이드] {summary}\n{log}");
        EditorUtility.DisplayDialog("견착 가이드 배치",
            summary + "\n\n" +
            "★남은 수작업: 씬 뷰에서 각 가이드의 localOffset을 이마 표면(눈썹 위)으로 맞추세요.\n" +
            "  머리 본은 두상 중심이라 기본값(앞·위 7~8cm)이 모델마다 어긋납니다.\n" +
            "  Play 중에도 위치가 이마 타겟을 따라가므로 Play 상태에서 보며 맞춰도 됩니다.\n\n" +
            "자세한 목록은 콘솔을 보세요.", "확인");
    }

    [MenuItem("GuideChuna/견착 가이드 점검 (읽기 전용)")]
    public static void AuditGuides()
    {
        var stabilizers = Object.FindObjectsByType<CranialPostureStabilizer>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        var guides = Object.FindObjectsByType<ShoulderBraceGuide>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        var sb = new StringBuilder();
        sb.AppendLine($"견착 판정(CranialPostureStabilizer) {stabilizers.Length}개 / 가이드 {guides.Length}개");

        foreach (var st in stabilizers)
        {
            var g = FindGuideFor(st, guides);
            sb.AppendLine($"· {PathOf(st.transform)}");
            sb.AppendLine($"    이마 타겟 = {(st.ForeheadTarget != null ? st.ForeheadTarget.name : "★없음")}");
            sb.AppendLine($"    가이드     = {(g != null ? g.DescribeState() : "★없음 — 메뉴 '견착 가이드 배치 (이마)' 실행")}");
        }

        foreach (var g in guides)
            if (System.Array.IndexOf(stabilizers, FindStabilizerOf(g)) < 0)
                sb.AppendLine($"· ★고아 가이드(판정 미연결): {PathOf(g.transform)}");

        ChunaEditorLog(sb.ToString());
        EditorUtility.DisplayDialog("견착 가이드 점검", "결과를 콘솔에 출력했습니다.", "확인");
    }

    // === 내부 ===

    /// <summary>가이드를 둘 부모 = 그 판정이 속한 리그. 리그를 못 찾으면 판정 오브젝트의 부모.</summary>
    private static Transform ResolveParent(CranialPostureStabilizer st)
    {
        var rig = st.GetComponentInParent<CranialAdjustmentController>(true);
        if (rig != null) return rig.transform;
        return st.transform.parent != null ? st.transform.parent : st.transform;
    }

    private static GameObject BuildGuide(Transform parent, CranialPostureStabilizer st)
    {
        // 어깨가 닿는 넓적한 접촉면 → 납작한 구체. 콜라이더는 없앤다(판정에 쓰지 않는다).
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = GuideName;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localScale = new Vector3(0.11f, 0.03f, 0.09f);   // 어깨 접촉 패치 근사

        var rend = go.GetComponent<Renderer>();
        rend.sharedMaterial = LoadOrCreateMaterial();
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;

        var guide = go.AddComponent<ShoulderBraceGuide>();
        var so = new SerializedObject(guide);
        so.FindProperty("stabilizer").objectReferenceValue = st;
        so.FindProperty("foreheadTarget").objectReferenceValue = st.ForeheadTarget;
        so.FindProperty("markerRenderer").objectReferenceValue = rend;
        so.ApplyModifiedPropertiesWithoutUndo();

        // 시작부터 떠 있지 않게(견착 국면에서만 뜬다) — 런타임 Awake도 같은 처리를 한다.
        rend.enabled = false;
        return go;
    }

    private static ShoulderBraceGuide FindGuideFor(CranialPostureStabilizer st, IEnumerable<ShoulderBraceGuide> all)
    {
        foreach (var g in all)
            if (FindStabilizerOf(g) == st) return g;
        return null;
    }

    private static CranialPostureStabilizer FindStabilizerOf(ShoulderBraceGuide g)
    {
        var so = new SerializedObject(g);
        return so.FindProperty("stabilizer").objectReferenceValue as CranialPostureStabilizer;
    }

    private static Material LoadOrCreateMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat != null) return mat;

        EnsureFolder("Assets/Materials");
        var mat2 = new Material(Shader.Find("Standard")) { name = "ShoulderBraceGuide" };
        // 런타임(ShoulderBraceGuide.EnsureTransparentMaterial)도 Fade로 바꾸지만,
        // 에디터에서 위치를 맞출 때도 반투명하게 보여야 이마 표면에 맞추기 쉽다.
        mat2.SetFloat("_Mode", 3f);
        mat2.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat2.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat2.SetInt("_ZWrite", 0);
        mat2.EnableKeyword("_ALPHABLEND_ON");
        mat2.renderQueue = 3000;
        mat2.color = new Color(1f, 0.35f, 0.35f, 0.5f);   // 파지점 미파지색과 같은 연한 붉은색
        AssetDatabase.CreateAsset(mat2, MaterialPath);
        AssetDatabase.SaveAssets();
        return mat2;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }

    private static string PathOf(Transform t)
    {
        string p = t.name;
        while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }

    private static void ChunaEditorLog(string msg) => Debug.Log(msg);
}
