using UnityEditor;
using UnityEngine;

/// <summary>로비 판 방향 · 하늘 회전 시험(읽기 전용 · 10-29 «로비 배경 정면 맞추기» 실측용).</summary>
public static class LobbySkyProbe
{
    [MenuItem("GuideChuna/시험/로비 판 방향 재기 (읽기 전용)")]
    public static void Run()
    {
        var p = GameObject.Find("OVRCameraRig/TrackingSpace/UIAxis/UI (1)/로비UI/ContentUIExample-HorizonOS1");
        var eye = GameObject.Find("CenterEyeAnchor");
        if (p == null || eye == null) { Debug.LogWarning("[LobbySkyProbe] 로비 판 또는 눈 없음"); return; }
        Vector3 d = p.transform.position - eye.transform.position; d.y = 0;
        Debug.Log($"[LobbySkyProbe] 눈 {eye.transform.position:F2} → 로비 판 {p.transform.position:F2} · 수평 방향 {d.normalized:F2} · 방위 {Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg:F1}°(0 = +Z)");
    }

    // 패스스루 끈 모습 미리보기 — 눈 카메라와 같은 자리 · 같은 화각에서 하늘(LobbyEnvironmentSky가 실행 때 까는 재질) + 로비 UI를 찍는다.
    // 씬 카메라 설정은 건드리지 않는다(임시 카메라를 눈에 붙였다가 지운다).
    [MenuItem("GuideChuna/시험/로비 배경 + 판 눈높이 캡처 (읽기 전용)")]
    public static void CaptureWithUi()
    {
        var eye = GameObject.Find("CenterEyeAnchor");
        var sky = Object.FindFirstObjectByType<LobbyEnvironmentSky>(FindObjectsInactive.Include);
        var mat = sky != null ? new SerializedObject(sky).FindProperty("skyMaterial").objectReferenceValue as Material : null;
        if (eye == null || mat == null) { Debug.LogWarning("[LobbySkyProbe] 눈 또는 하늘 재질 없음"); return; }
        var src = eye.GetComponent<Camera>();
        var go = new GameObject("임시_눈캡처") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(src); cam.clearFlags = CameraClearFlags.Skybox; cam.fieldOfView = 80; cam.stereoTargetEye = StereoTargetEyeMask.None;
            go.AddComponent<Skybox>().material = mat;
            string dir = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, "Captures", "LobbySky");
            foreach (var (n, yaw, pitch) in new[] { ("UI_정면", 0f, 5f), ("UI_왼쪽", -70f, 0f), ("UI_오른쪽", 70f, 0f), ("UI_뒤", 180f, 0f) })
            {
                go.transform.SetPositionAndRotation(eye.transform.position, Quaternion.Euler(pitch, yaw, 0));
                var rt = new RenderTexture(1600, 900, 24); cam.targetTexture = rt; cam.aspect = 16f / 9f; cam.Render();
                RenderTexture.active = rt; var t = new Texture2D(1600, 900, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); t.Apply();
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, n + ".png"), t.EncodeToPNG());
                RenderTexture.active = null; cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(t);
            }
            Debug.Log($"[LobbySkyProbe] 눈높이 캡처 → {dir} (UI_*.png)");
        }
        finally { Object.DestroyImmediate(go); }
    }

    [MenuItem("GuideChuna/시험/로비 하늘 회전 90")]  public static void R90() { LobbySkySetupTool.Apply(90f); LobbySkySetupTool.CaptureFront(); }
    [MenuItem("GuideChuna/시험/로비 하늘 회전 270")] public static void R270() { LobbySkySetupTool.Apply(270f); LobbySkySetupTool.CaptureFront(); }
}
