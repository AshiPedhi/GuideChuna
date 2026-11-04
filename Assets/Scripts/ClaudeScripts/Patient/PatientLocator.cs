using UnityEngine;

/// <summary>
/// 환자 몸의 자리 · 방향을 <b>뼈에서 잰다</b> (2026-10-30 — 리모컨 자리 · 실습 UI 정면에 쓴다).
/// ★방향은 추론하지 않는다(CLAUDE.md 규칙 9) — 얼굴 앞 = 머리 뼈 → 두 눈 뼈 가운데(CC4 CC_Base_L_Eye · R_Eye). 축 이름 · 로컬 축 부호는 쓰지 않는다.
/// 환자 = 켜진 SkinnedMeshRenderer 중 부피가 가장 큰 것(4m 미만 — 방 · 무대 제외). 부를 때마다 찾는다(가끔 부르는 용도 · Update에서 부르지 말 것).
/// </summary>
public static class PatientLocator
{
    public struct Pose
    {
        public Vector3 center;   // 몸 메시 중심
        public Vector3 head;     // CC_Base_Head
        public Vector3 neck;     // CC_Base_NeckTwist01 (없으면 머리)
        public Vector3 face;     // 얼굴 앞(단위) — 머리 → 두 눈 가운데
        public bool hasBones;
    }

    public static bool TryGet(out Pose pose)
    {
        pose = default;
        SkinnedMeshRenderer body = null; float best = 0f;
        foreach (var smr in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
        {
            if (!smr.enabled || !smr.gameObject.activeInHierarchy) continue;
            var s = smr.bounds.size; float v = s.x * s.y * s.z;
            if (v > best && s.magnitude < 4f) { best = v; body = smr; }
        }
        if (body == null) return false;
        pose.center = body.bounds.center;
        Transform root = body.rootBone != null ? body.rootBone.root : body.transform.root;
        Transform head = Find(root, "CC_Base_Head"), neck = Find(root, "CC_Base_NeckTwist01");
        Transform le = Find(root, "CC_Base_L_Eye"), re = Find(root, "CC_Base_R_Eye");
        if (head == null || le == null || re == null) { pose.head = pose.neck = pose.center; pose.face = Vector3.forward; return true; }
        pose.head = head.position;
        pose.neck = neck != null ? neck.position : head.position;
        Vector3 f = (le.position + re.position) * 0.5f - head.position;
        pose.face = f.sqrMagnitude > 1e-8f ? f.normalized : Vector3.forward;
        pose.hasBones = true;
        return true;
    }

    static Transform Find(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var r = Find(c, name); if (r != null) return r; }
        return null;
    }
}
