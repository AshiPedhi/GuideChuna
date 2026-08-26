using UnityEngine;

/// <summary>
/// 가이드 손을 <b>환자 머리뼈의 자식으로 넣는다</b>. 그러면 고개를 따라가는 건
/// 트랜스폼 계층이 알아서 한다 — 코드가 매 프레임 손댈 일이 없다.
///
/// ★2026-08-26. 앞서 "매 프레임 위치·회전을 복사"하는 방식을 썼는데 안 따라왔다.
///   원인을 세 번 못 짚었고, 사용자 지시로 <b>실제 부모·자식</b>으로 바꿨다.
///   <c>SetParent(worldPositionStays: true)</c>는 월드 행렬을 보존하므로
///   붙이는 순간 손의 위치·회전·크기가 그대로다(c8의 −1 스케일도 그대로 흡수된다).
///
/// ★경추ROM 전용이다. 다른 술기는 이 경로를 타지 않는다
///   (CSV <c>conditionParams=guideHold</c>가 있는 단계에서만 붙인다).
///
/// 이 컴포넌트가 하는 일은 <b>원래 부모를 기억했다가 되돌리는 것</b>뿐이다.
/// 안 되돌리면 시나리오가 끝나도 가이드 손이 환자 머리에 매달려 따라다닌다.
/// </summary>
public class GuideHandHeadFollower : MonoBehaviour
{
    private Transform target;          // 붙인 가이드 손 루트
    private Transform originalParent;  // 붙이기 전의 부모
    private int originalSiblingIndex;
    private Transform anchor;          // 붙인 뼈

    /// <summary>지금 머리뼈에 붙어 있는가.</summary>
    public bool IsAttached => target != null && anchor != null && target.parent == anchor;

    /// <summary>붙어 있는 뼈. 진단용.</summary>
    public Transform Anchor => anchor;

    /// <summary>뼈 원점에서 손까지의 거리(m). 제대로 잡혔는지 눈으로 확인하는 용도.</summary>
    public float DistanceFromAnchor =>
        target != null && anchor != null ? Vector3.Distance(target.position, anchor.position) : 0f;

    /// <summary>지금 서 있는 자세 그대로 머리뼈의 자식으로 넣는다.</summary>
    public void Attach(Transform handRoot, Transform headBone)
    {
        if (handRoot == null || headBone == null) return;

        // 이미 다른 곳에 붙어 있으면 먼저 되돌린다(두 번 붙이면 원래 부모를 잃는다).
        if (target != null && target != handRoot) Detach();

        if (target != handRoot || originalParent == null)
        {
            target = handRoot;
            originalParent = handRoot.parent;
            originalSiblingIndex = handRoot.GetSiblingIndex();
        }

        anchor = headBone;
        // ★월드 자세를 보존하며 붙인다. 붙이는 순간 손은 조금도 움직이지 않는다.
        target.SetParent(headBone, worldPositionStays: true);

        ChunaLogger.Log($"<color=cyan>[가이드손] '{target.name}'을 '{headBone.name}'의 자식으로 넣었다 " +
                        $"(거리 {DistanceFromAnchor * 100f:F1}cm) · 뼈 경로: {PathOf(headBone)}</color>");
    }

    /// <summary>원래 부모로 되돌린다. 손의 월드 자세는 그대로 둔다.</summary>
    public void Detach()
    {
        if (target == null) return;

        if (originalParent != null)
        {
            target.SetParent(originalParent, worldPositionStays: true);
            target.SetSiblingIndex(originalSiblingIndex);
        }
        else
        {
            target.SetParent(null, worldPositionStays: true);
        }

        target = null;
        anchor = null;
        originalParent = null;
    }

    /// <summary>씬이 정리될 때 매달린 채로 남지 않게 한다.</summary>
    private void OnDestroy() => Detach();

    internal static string PathOf(Transform t)
    {
        if (t == null) return "(없음)";
        string path = t.name;
        for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
        return path;
    }
}
