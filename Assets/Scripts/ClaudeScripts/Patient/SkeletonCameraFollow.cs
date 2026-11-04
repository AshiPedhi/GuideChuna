using UnityEngine;

/// <summary>
/// 근골격 창 카메라(PatientPositionManager.cameraRoot)를 골격 모델(skeletonModelRoot)에 묶는다 (2026-11-04).
///
/// ★사용자 «근골격 화면 — 스켈레톤 따로 표시하는 카메라 위치가 어디 갔는지 안 보여 · 찾아서 모델한테 위치하게».
/// ★원인(씬 실측 · 진단 메뉴 «근골격 카메라 자리 찍기»): 골격 모델은 <c>…/환자 이동 홀더/c9 (1)</c> — 리모컨으로 환자를 옮기면 같이 간다.
///   카메라는 <c>위치초기화 오브젝트/GameObject/근골격카메라</c> — 홀더 밖이라 그 자리에 남는다. 환자 위치를 맞출수록 둘이 벌어졌다.
///   (11-04부터 개요 화면에서 위치를 맞춘 뒤 시작하므로, 이 벌어짐이 늘 생긴다.)
/// ★방법: 프리셋이 둘을 <b>함께</b> 놓는 순간(= 설계된 구도)의 «모델 기준 카메라 자리»를 적어 두고, 매 프레임 그 구도로 다시 놓는다.
///   새 구도를 지어내지 않는다 — 프리셋 값이 정한 구도를 그대로 지킨다.
/// ★우리가 옮긴 것만 되돌린다: 꺼질 때 따로 복원할 것은 없다(카메라는 원래 프리셋이 매번 덮어쓰는 물건).
/// </summary>
[DefaultExecutionOrder(300)]   // 홀더 이동 · 애니메이션 뒤에
public class SkeletonCameraFollow : MonoBehaviour
{
    private Transform cam, model;
    private Vector3 localPos;
    private Quaternion localRot;
    private bool captured, aimed;
    private float checkAt = -1f;
    private Vector3 aimLocal;   // 모델 기준 «바라볼 점»(2초 뒤 잰 렌더러 묶음 중심)

    /// <summary>프리셋 적용 직후 PatientPositionManager가 부른다 — 지금 두 자리 사이 관계를 구도로 적는다.</summary>
    public static void Capture(Transform cameraRoot, Transform skeletonModel)
    {
        if (cameraRoot == null || skeletonModel == null) return;
        var f = cameraRoot.GetComponent<SkeletonCameraFollow>();
        if (f == null) f = cameraRoot.gameObject.AddComponent<SkeletonCameraFollow>();
        f.cam = cameraRoot; f.model = skeletonModel;
        f.localPos = skeletonModel.InverseTransformPoint(cameraRoot.position);
        f.localRot = Quaternion.Inverse(skeletonModel.rotation) * cameraRoot.rotation;
        f.captured = true; f.aimed = false;
        f.checkAt = Time.realtimeSinceStartup + 2f;
        Debug.Log($"[근골격캠] 구도 적음 · 모델 기준 카메라 {f.localPos:F2} · 이후 모델을 따라감");
    }

    private void LateUpdate()
    {
        if (!captured || cam == null || model == null) return;
        Vector3 pos = model.TransformPoint(localPos);
        Quaternion rot = model.rotation * localRot;
        // ★11-04 실측: 프리셋 구도 그대로면 모델 중심이 화면 세로 −0.05(아래 가장자리 바로 밖)였다 → 자리는 그대로 두고 모델 중심을 바라보게
        if (aimed)
        {
            Vector3 look = model.TransformPoint(aimLocal) - pos;
            if (look.sqrMagnitude > 1e-6f) rot = Quaternion.LookRotation(look, rot * Vector3.up);
        }
        cam.SetPositionAndRotation(pos, rot);
        if (checkAt > 0f && Time.realtimeSinceStartup > checkAt) { checkAt = -1f; LogView(); }
    }

    /// <summary>2초 뒤 한 번 — 카메라 화면 안에 모델이 들어오는지(로그로 실측 · log-first).</summary>
    private void LogView()
    {
        var c = cam.GetComponentInChildren<Camera>(true);
        if (c == null) { Debug.LogWarning("[근골격캠] ★카메라 컴포넌트 없음"); return; }
        Bounds b = default; bool any = false; int n = 0;
        foreach (var r in model.GetComponentsInChildren<Renderer>(false))
        {
            if (!r.enabled || ((1 << r.gameObject.layer) & c.cullingMask) == 0) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            n++;
        }
        if (!any) { Debug.LogWarning($"[근골격캠] ★카메라 마스크({c.cullingMask})에 드는 켜진 모델 렌더러가 0개 — 구도와 상관없이 빈 화면이 된다(레이어 확인)"); return; }
        Vector3 vp = c.WorldToViewportPoint(b.center);
        bool inView = vp.z > c.nearClipPlane && vp.x > 0f && vp.x < 1f && vp.y > 0f && vp.y < 1f;
        float halfFov = c.fieldOfView * 0.5f * Mathf.Deg2Rad;
        float need = b.extents.magnitude / Mathf.Max(0.05f, Mathf.Tan(halfFov));   // 묶음 전체가 들어가는 거리(대략)
        Debug.Log($"[근골격캠] 확인 · 렌더러 {n}개 · 모델 중심 화면 {vp:F2} → {(inView ? "보임" : "★안 보임")} · 거리 {Vector3.Distance(c.transform.position, b.center):F2}m(전체가 들어가려면 ≈{need:F2}m) · 카메라 켜짐 {c.isActiveAndEnabled} → 이제 중심을 바라본다");
        aimLocal = model.InverseTransformPoint(b.center);
        aimed = true;
    }
}
