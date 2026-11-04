using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 로비 배경 파노라마 적용 (2026-10-29) — 블렌더 360° 렌더(Assets/Art/LobbyEnv/lobby_pano_v03.png)로 Skybox/Panoramic 재질을 만들고
/// 로비 씬에 LobbyEnvironmentSky를 붙여 배선한다. 여러 번 돌려도 같은 결과. ★저장은 사람이 한다.
/// 방향(정면이 맞는지)은 «로비 배경 정면 캡처»로 재서 rotation을 정한다(짐작 금지 — CLAUDE.md 규칙 9).
/// </summary>
public static class LobbySkySetupTool
{
    const string Dir = "Assets/Art/LobbyEnv";
    const string Pano = Dir + "/lobby_pano_v03.png";
    const string MatPath = Dir + "/LobbySky_v03.mat";
    const string GroundMatPath = Dir + "/LobbyGround_v03.mat";
    const string GroundName = "로비 바닥 투영";
    const float GroundRadius = 5.5f;      // 블렌더 데크 원판 반지름(build_l1c.py create_circle radius 5.5)
    const float GroundFadeStart = 4.0f;   // 난간(5.5m) 앞에서 하늘로 녹인다 — 난간은 세로라 바닥에 찍으면 늘어난다(★추정값 · 헤드셋에서 조정)

    [MenuItem("GuideChuna/UI 개편/로비 배경 파노라마 적용 (lobby)")]
    public static void Apply() => Apply(float.NaN);

    public static void Apply(float rotation)
    {
        var tex = ImportPano();
        if (tex == null) { Debug.LogWarning($"[LobbySky] {Pano} 없음"); return; }
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Skybox/Panoramic"));
            AssetDatabase.CreateAsset(mat, MatPath);
        }
        mat.SetTexture("_MainTex", tex);
        mat.SetFloat("_Mapping", 1f);        // 1 = Latitude Longitude Layout
        mat.SetFloat("_ImageType", 0f);      // 360°
        mat.SetFloat("_Exposure", 1f);
        if (!float.IsNaN(rotation)) mat.SetFloat("_Rotation", rotation);
        EditorUtility.SetDirty(mat); AssetDatabase.SaveAssets();

        var auth = Object.FindFirstObjectByType<LobbyAuthUI_Complete>(FindObjectsInactive.Include);
        if (auth == null) { Debug.LogWarning("[LobbySky] 로비가 아니다 — 재질만 만들었다"); return; }
        var sky = auth.GetComponent<LobbyEnvironmentSky>() ?? Undo.AddComponent<LobbyEnvironmentSky>(auth.gameObject);
        var so = new SerializedObject(sky);
        so.FindProperty("skyMaterial").objectReferenceValue = mat;
        so.FindProperty("passthroughLayer").objectReferenceValue = Object.FindFirstObjectByType<OVRPassthroughLayer>(FindObjectsInactive.Include);
        so.ApplyModifiedProperties();

        // 10-30 사용자 «씬에서 기본 배경이 있는 상태로 로비 보여줘 — 그래야 에디터에서도 확인» — 씬 자체를 하늘 상태로 둔다
        //   스카이박스(씬 Lighting 설정) · 눈 카메라 배경 = Skybox · 패스스루 층 = 숨김. 패스스루는 ◐ 버튼이 런타임에 켠다
        RenderSettings.skybox = mat;
        var rig = GameObject.Find("OVRCameraRig");
        var eye = rig != null ? rig.transform.Find("TrackingSpace/CenterEyeAnchor")?.GetComponent<Camera>() : null;
        if (eye != null)
        {
            Undo.RecordObject(eye, "로비 하늘");
            eye.clearFlags = CameraClearFlags.Skybox;
            var c = eye.backgroundColor; c.a = 1f; eye.backgroundColor = c;
        }
        var layer = so.FindProperty("passthroughLayer").objectReferenceValue as OVRPassthroughLayer;
        if (layer != null) { Undo.RecordObject(layer, "로비 하늘"); layer.hidden = true; }
        Debug.Log($"[LobbySky] 씬 기본 = 하늘 — 스카이박스 {mat.name} · 눈 카메라 {(eye != null ? "Skybox" : "없음")} · 패스스루 층 {(layer != null ? "숨김" : "없음")}");

        // 10-30 A안: 바닥 투영 판 — 하늘 그림을 실제 바닥(추적 공간 y)에 찍는다. 실행 때 LobbyGroundProjection이 머리 자리로 다시 놓는다
        var groundMat = AssetDatabase.LoadAssetAtPath<Material>(GroundMatPath);
        if (groundMat == null)
        {
            var sh = Shader.Find("GuideChuna/LobbyGroundProjection");
            if (sh == null) { Debug.LogWarning("[LobbySky] 셰이더 GuideChuna/LobbyGroundProjection 없음 — 바닥 투영 건너뜀"); }
            else { groundMat = new Material(sh); AssetDatabase.CreateAsset(groundMat, GroundMatPath); }
        }
        if (groundMat != null)
        {
            foreach (var p in new[] { "_MainTex" }) groundMat.SetTexture(p, mat.GetTexture(p));
            foreach (var p in new[] { "_Rotation", "_Exposure" }) groundMat.SetFloat(p, mat.GetFloat(p));
            groundMat.SetColor("_Tint", mat.GetColor("_Tint"));
            groundMat.SetFloat("_Radius", GroundRadius); groundMat.SetFloat("_FadeStart", GroundFadeStart);
            var space = rig != null ? rig.transform.Find("TrackingSpace") : null;
            float floorY = space != null ? space.position.y : 0f;
            Vector3 head = eye != null ? eye.transform.position : Vector3.zero;
            groundMat.SetVector("_EyeCenter", new Vector4(head.x, floorY + 1.6f, head.z, 0f));   // 에디터 미리보기용(실행 때는 머리 자리로 덮는다)
            EditorUtility.SetDirty(groundMat); AssetDatabase.SaveAssets();

            GameObject g = null;
            foreach (var gp in Object.FindObjectsByType<LobbyGroundProjection>(FindObjectsInactive.Include, FindObjectsSortMode.None)) { g = gp.gameObject; break; }
            if (g == null)
            {
                g = GameObject.CreatePrimitive(PrimitiveType.Quad);
                g.name = GroundName;
                Object.DestroyImmediate(g.GetComponent<Collider>());
                Undo.RegisterCreatedObjectUndo(g, "로비 바닥 투영");
                Undo.AddComponent<LobbyGroundProjection>(g);
            }
            Undo.RecordObject(g.transform, "로비 바닥 투영");
            g.transform.SetPositionAndRotation(new Vector3(head.x, floorY + 0.002f, head.z), Quaternion.Euler(90f, 0f, 0f));
            g.transform.localScale = Vector3.one * (GroundRadius * 2f);
            var mr = g.GetComponent<MeshRenderer>();
            Undo.RecordObject(mr, "로비 바닥 투영");
            mr.sharedMaterial = groundMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            so.Update();
            so.FindProperty("groundProjection").objectReferenceValue = g;
            so.ApplyModifiedProperties();
            Debug.Log($"[LobbySky] 바닥 투영 판 «{g.name}» — 반지름 {GroundRadius}m(녹이기 {GroundFadeStart}m~) · 바닥 y {floorY:F2} · 에디터 눈 {head:F2}");
        }
        EditorSceneManager.MarkSceneDirty(auth.gameObject.scene);
        Debug.Log($"[LobbySky] 적용 — {MatPath} · 회전 {mat.GetFloat("_Rotation")}° · {sky.name}에 LobbyEnvironmentSky(useSky {so.FindProperty("useSky").boolValue}) · ★저장은 Ctrl+S");
    }

    // 정면 확인용: 에디터 카메라를 눈 자리에서 정면(+Z)으로 두고 스카이박스만 찍는다 — 블렌더 정면 렌더와 비교
    [MenuItem("GuideChuna/시험/로비 배경 정면 캡처 (읽기 전용)")]
    public static void CaptureFront()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat == null) { Debug.LogWarning("[LobbySky] 재질 없음"); return; }
        var go = new GameObject("임시_배경캡처카메라") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox; cam.cullingMask = 0; cam.fieldOfView = 90;
            var skyComp = go.AddComponent<Skybox>(); skyComp.material = mat;
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Captures", "LobbySky");
            Directory.CreateDirectory(dir);
            foreach (var (n, yaw) in new[] { ("정면", 0f), ("왼쪽", -90f), ("오른쪽", 90f), ("뒤", 180f) })
            {
                go.transform.rotation = Quaternion.Euler(0, yaw, 0);
                var rt = new RenderTexture(1280, 720, 24); cam.targetTexture = rt; cam.Render();
                RenderTexture.active = rt; var t = new Texture2D(1280, 720, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); t.Apply();
                File.WriteAllBytes(Path.Combine(dir, $"{n}.png"), t.EncodeToPNG());
                RenderTexture.active = null; cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(t);
            }
            Debug.Log($"[LobbySky] 정면 캡처 → {dir} (회전 {mat.GetFloat("_Rotation")}°)");
        }
        finally { Object.DestroyImmediate(go); }
    }

    static Texture2D ImportPano()
    {
        var imp = AssetImporter.GetAtPath(Pano) as TextureImporter;
        if (imp == null) return null;
        bool change = imp.textureShape != TextureImporterShape.Texture2D || imp.maxTextureSize != 4096 || imp.wrapModeU != TextureWrapMode.Repeat || imp.mipmapEnabled;
        if (change)
        {
            imp.textureShape = TextureImporterShape.Texture2D; imp.maxTextureSize = 4096;
            imp.wrapModeU = TextureWrapMode.Repeat; imp.wrapModeV = TextureWrapMode.Clamp;
            imp.mipmapEnabled = false;   // 하늘은 늘 같은 거리 — 밉맵이 경도 이음매에 줄을 만든다
            imp.textureCompression = TextureImporterCompression.Compressed;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(Pano);
    }
}
