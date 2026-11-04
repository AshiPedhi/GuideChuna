using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 월드 캔버스 판 하나를 정면에서 찍어 PNG로 남긴다 (2026-10-27 · UI 개편 확인용).
/// 씬 뷰 캡처는 UI에 렌더러 경계가 없어 판을 못 잡는다 — 그래서 판 앞에 임시 카메라를 세운다.
///
/// 선택한 오브젝트(꺼져 있어도 됨)를 잠깐 켜고, 그 안 첫 RectTransform 캔버스 크기에 맞춰 카메라를 놓고 찍은 뒤
/// ★켜짐 상태 · 카메라를 원래대로 되돌린다. 씬을 dirty로 만들지 않는다(임시 카메라는 HideAndDontSave).
/// 출력: &lt;프로젝트&gt;/Captures/UIPreview/&lt;이름&gt;.png
/// </summary>
public static class UIPanelPreviewCapture
{
    [MenuItem("GuideChuna/UI 개편/선택한 판 미리보기 찍기")]
    public static void CaptureSelected()
    {
        var go = Selection.activeGameObject;
        if (go == null) { Debug.LogWarning("[UIPreview] 오브젝트를 하나 선택하고 다시"); return; }
        Capture(go, go.name);
    }

    /// <summary>설정 리모컨을 찍는다(브리지 menu로 부르기 쉽게 이름을 고정).</summary>
    [MenuItem("GuideChuna/UI 개편/설정 리모컨 미리보기 찍기")]
    public static void CaptureRemote()
    {
        foreach (var r in Object.FindObjectsByType<PatientPositionRemote>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        { Capture(r.gameObject, "설정리모컨"); return; }
        Debug.LogWarning("[UIPreview] 설정 리모컨이 씬에 없다");
    }

    /// <summary>UI 개편으로 만든 판 전부(설정 리모컨 · 상단 바 · 정보 창)를 찍는다. 정보 창은 안의 페이지도 잠깐 켠다.</summary>
    [MenuItem("GuideChuna/UI 개편/UI 개편 판 전부 미리보기 찍기")]
    public static void CaptureAll()
    {
        var group = GameObject.Find("UI Group");
        if (group == null) { Debug.LogWarning("[UIPreview] UI Group 없음"); return; }
        foreach (Transform t in group.transform)
        {
            bool window = t.name.StartsWith("정보 창 - ");
            // 10-27 A/B 뒤 이름이 «상단 바 A (시안)» · «설정 리모컨 B (Meta 기본)» 꼴이라 앞머리로 찾는다
            if (!(window || t.name.StartsWith("상단 바") || t.name.StartsWith("설정 리모컨"))) continue;
            GameObject page = null;
            if (window)
            {
                var back = t.GetComponentInChildren<UnityEngine.UI.Mask>(true);   // UIBackplate
                if (back != null)
                    foreach (Transform c in back.transform)
                        if (c.name != "GradientEffect" && c.GetComponent<UnityEngine.UI.LayoutElement>() is var le && le != null && le.ignoreLayout) { page = c.gameObject; break; }
            }
            bool pageWas = page != null && page.activeSelf;
            if (page != null) page.SetActive(true);
            Capture(t.gameObject, t.name.Replace(" ", ""));
            if (page != null) page.SetActive(pageWas);
        }
    }

    /// <summary>로비 판(LobbyAuthUI_Complete 아래 PointableCanvas 전부)을 찍는다. 팝업은 켜진 상태 그대로.</summary>
    [MenuItem("GuideChuna/UI 개편/로비 미리보기 찍기")]
    public static void CaptureLobby()
    {
        var auth = Object.FindFirstObjectByType<LobbyAuthUI_Complete>(FindObjectsInactive.Include);
        if (auth == null) { Debug.LogWarning("[UIPreview] 로비가 열려 있지 않다"); return; }
        foreach (var pc in auth.GetComponentsInChildren<Oculus.Interaction.PointableCanvas>(true))
            if (pc.gameObject.activeInHierarchy || pc.transform.parent == auth.transform)
                Capture(pc.gameObject, "로비_" + pc.gameObject.name.Replace(" ", ""));
    }

    public static string Capture(GameObject root, string name)
    {
        bool wasActive = root.activeSelf;
        // ★판들이 같은 자리에 겹쳐 있을 수 있다 — 찍는 동안 혼자 먼 곳(+500m 위)으로 옮겼다가 되돌린다
        Vector3 oldPos = root.transform.position;
        root.transform.position = oldPos + Vector3.up * 500f;
        root.SetActive(true);
        Canvas.ForceUpdateCanvases();
        var canvas = root.GetComponentInChildren<Canvas>(true);
        if (canvas == null) { root.SetActive(wasActive); root.transform.position = oldPos; Debug.LogWarning("[UIPreview] 캔버스가 없다"); return null; }

        var rt = (RectTransform)canvas.transform;
        Vector3[] c = new Vector3[4];
        rt.GetWorldCorners(c);
        Vector3 center = (c[0] + c[2]) * 0.5f;
        float wM = Vector3.Distance(c[0], c[3]), hM = Vector3.Distance(c[0], c[1]);
        Vector3 normal = rt.forward;   // UI 정면은 -forward 쪽에서 본다

        var camGo = new GameObject("UIPreviewCam") { hideFlags = HideFlags.HideAndDontSave };
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = Mathf.Max(hM, wM * 720f / 1280f) * 0.5f * 1.08f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.55f, 0.54f, 0.52f, 1f);   // 패스스루 방 흉내(회색)
        cam.nearClipPlane = 0.01f; cam.farClipPlane = 2f;
        camGo.transform.position = center - normal * 0.5f;
        camGo.transform.rotation = Quaternion.LookRotation(normal, rt.up);
        cam.cullingMask = 1 << root.layer | 1 << canvas.gameObject.layer;

        var tex = new RenderTexture(1280, 720, 24);
        cam.targetTexture = tex;
        cam.Render();
        RenderTexture.active = tex;
        var img = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        img.Apply();
        RenderTexture.active = null;

        string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Captures", "UIPreview");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name + ".png");
        File.WriteAllBytes(path, img.EncodeToPNG());

        cam.targetTexture = null;
        Object.DestroyImmediate(img); Object.DestroyImmediate(tex); Object.DestroyImmediate(camGo);
        root.SetActive(wasActive);
        root.transform.position = oldPos;
        Debug.Log($"[UIPreview] {path} · 판 {wM:F2} × {hM:F2} m");
        return path;
    }
}
