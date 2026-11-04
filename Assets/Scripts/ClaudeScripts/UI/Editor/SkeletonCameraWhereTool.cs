using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 진단(읽기 전용 · 11-04): 근골격 창 카메라(PatientPositionManager.cameraRoot)와 골격 모델(skeletonModelRoot)의 자리 · 부모 ·
/// 카메라가 모델을 보고 있는지(모델 렌더러 묶음 중심이 화면 안인지)를 Editor.log에 찍는다. Play 중에 돌리면 실제 자리를 잰다.
/// 사용자 «근골격 화면 — 스켈레톤 따로 표시하는 카메라 위치가 어디 갔는지 안 보여». 아무것도 바꾸지 않는다.
/// </summary>
public static class SkeletonCameraWhereTool
{
    [MenuItem("GuideChuna/진단/근골격 카메라 자리 찍기 (TrainingScene)")]
    public static void Dump()
    {
        var ppm = Object.FindFirstObjectByType<PatientPositionManager>(FindObjectsInactive.Include);
        if (ppm == null) { Debug.LogWarning("[근골격캠] PatientPositionManager 없음"); return; }
        var so = new SerializedObject(ppm);
        var camRoot = so.FindProperty("cameraRoot").objectReferenceValue as Transform;
        var skel = so.FindProperty("skeletonModelRoot").objectReferenceValue as Transform;
        var sb = new StringBuilder($"[근골격캠] {(Application.isPlaying ? "Play 중" : "에디터(씬 값)")}\n");
        sb.Append($"  카메라 루트 {Path(camRoot)} · 월드 {P(camRoot)}\n  골격 모델 {Path(skel)} · 월드 {P(skel)} · 레이어 {(skel != null ? LayerMask.LayerToName(skel.gameObject.layer) + "(" + skel.gameObject.layer + ")" : "-")}\n");
        var cam = camRoot != null ? camRoot.GetComponentInChildren<Camera>(true) : null;
        if (cam == null) { Debug.Log(sb.Append("  ★카메라 없음").ToString()); return; }
        sb.Append($"  카메라 '{cam.name}' 켜짐 {cam.isActiveAndEnabled} · 월드 {cam.transform.position:F2} · 앞 {cam.transform.forward:F2} · FOV {cam.fieldOfView:F0} · near {cam.nearClipPlane} far {cam.farClipPlane} · 마스크 {cam.cullingMask} · RT {(cam.targetTexture != null ? cam.targetTexture.name : "없음")}\n");
        if (skel != null)
        {
            var rs = skel.GetComponentsInChildren<Renderer>(true);
            Bounds b = default; bool any = false; int onMask = 0;
            foreach (var r in rs)
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (((1 << r.gameObject.layer) & cam.cullingMask) != 0) onMask++;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            sb.Append($"  모델 렌더러 {rs.Length}개 · 켜진 것 중 카메라 마스크에 드는 것 {onMask}개 · 묶음 중심 {b.center:F2} 크기 {b.size:F2}\n");
            if (any)
            {
                Vector3 vp = cam.WorldToViewportPoint(b.center);
                bool inView = vp.z > cam.nearClipPlane && vp.x > 0 && vp.x < 1 && vp.y > 0 && vp.y < 1;
                sb.Append($"  모델 중심이 카메라 화면에서 {vp:F2} → {(inView ? "보임" : "★안 보임")} · 카메라→모델 거리 {Vector3.Distance(cam.transform.position, b.center):F2}m · 각도 {Vector3.Angle(cam.transform.forward, b.center - cam.transform.position):F0}°\n");
            }
        }
        Debug.Log(sb.ToString());
    }

    static string P(Transform t) => t == null ? "-" : $"{t.position:F2} 회전 {t.eulerAngles:F0}";

    static string Path(Transform t)
    {
        if (t == null) return "없음";
        var s = t.name;
        for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
        return s;
    }
}
