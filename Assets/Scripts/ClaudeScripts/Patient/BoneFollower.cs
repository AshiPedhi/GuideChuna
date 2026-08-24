using UnityEngine;

/// <summary>
/// 대상 뼈의 월드 위치·회전만 따라간다. 스케일은 건드리지 않는다.
///
/// ★뼈 밑에 콜라이더를 직접 두면 안 되는 경우가 있다. c9 리그는 상위에 미러(음수)
///   스케일이 걸려 있어서, 그 밑에 만든 BoxCollider가 Unity에서 무효가 된다
///   ("negative scaling ... convex MeshCollider" 경고, 2026-08-24 실측).
///   그래서 스케일이 깨끗한 홀더를 씬 루트에 두고 뼈를 따라가게 한 뒤,
///   접촉점을 그 밑에 붙인다.
/// </summary>
[DefaultExecutionOrder(100)]   // Animator가 포즈를 쓴 뒤에 따라간다
public class BoneFollower : MonoBehaviour
{
    [SerializeField] private Transform target;

    public void SetTarget(Transform t) => target = t;

    private void LateUpdate()
    {
        if (target == null) return;
        transform.SetPositionAndRotation(target.position, target.rotation);
    }
}
