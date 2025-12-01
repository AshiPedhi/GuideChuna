using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class NeckVRControllerOptimized : MonoBehaviour
{
    [Header("본 매핑 (CC_Base 구조)")]
    [Tooltip("CC_Base_NeckTwist01")]
    public Transform neckBase;
    [Tooltip("CC_Base_NeckTwist02")]
    public Transform neckMid;
    [Tooltip("CC_Base_Head")]
    public Transform head;

    [Header("VR 손 트래킹")]
    public List<Transform> hands = new List<Transform>();

    [Header("회전 제한 (ROM 기준)")]
    public float maxFlexion = 50f;
    public float maxExtension = 60f;
    public float maxRotation = 80f;
    public float maxTilt = 45f;

    [Header("회전 속도")]
    public float rotationLerpSpeed = 8f;

    [Header("목-머리 거리 제한")]
    public float maxDistanceFromNeck = 0.05f;

    private Transform activeHand = null;
    private Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.useGravity = false;
            rb.isKinematic = true;
        }

        // CC_Base 구조에 맞게 본 자동 탐색
        if (neckBase == null) neckBase = FindBoneRecursive(transform.root, "CC_Base_NeckTwist01", "NeckTwist01");
        if (neckMid == null) neckMid = FindBoneRecursive(transform.root, "CC_Base_NeckTwist02", "NeckTwist02");
        if (head == null) head = FindBoneRecursive(transform.root, "CC_Base_Head", "Head");

        if (neckBase != null && neckMid != null && head != null)
            Debug.Log($"[NeckVRController] 본 매핑 완료: {neckBase.name} → {neckMid.name} → {head.name}");
        else
            Debug.LogWarning("[NeckVRController] 일부 본을 찾지 못했습니다. Inspector에서 수동 연결하세요.");
    }

    void LateUpdate()
    {
        SelectActiveHand();

        if (activeHand == null) return;

        // ✅ 위치 제한
        Vector3 offset = transform.position - neckBase.position;
        float distance = offset.magnitude;
        if (distance > maxDistanceFromNeck)
        {
            transform.position = neckBase.position + offset.normalized * maxDistanceFromNeck;
        }

        // ✅ 회전 계산
        Quaternion targetRot = CalculateTargetRotation();
        Quaternion limitedRot = ApplyRotationLimit(neckBase.rotation, targetRot);

        // 3단계 본 구조: NeckTwist01 → NeckTwist02 → Head
        if (neckBase != null)
            neckBase.rotation = Quaternion.Slerp(neckBase.rotation, limitedRot, Time.deltaTime * rotationLerpSpeed);
        if (neckMid != null)
            neckMid.rotation = Quaternion.Slerp(neckMid.rotation, limitedRot, Time.deltaTime * rotationLerpSpeed * 1.3f);
        if (head != null)
            head.rotation = Quaternion.Slerp(head.rotation, limitedRot, Time.deltaTime * rotationLerpSpeed * 1.8f);

        transform.rotation = limitedRot;
    }

    void SelectActiveHand()
    {
        float minDistance = float.MaxValue;
        activeHand = null;

        foreach (var hand in hands)
        {
            if (hand == null) continue;

            float dist = Vector3.Distance(hand.position, transform.position);
            if (dist < maxDistanceFromNeck && dist < minDistance)
            {
                minDistance = dist;
                activeHand = hand;
            }
        }
    }

    Quaternion CalculateTargetRotation()
    {
        Vector3 dir = activeHand.position - neckBase.position;
        if (dir.sqrMagnitude < 0.0001f) dir = neckBase.forward;

        Quaternion lookRot = Quaternion.LookRotation(dir, neckBase.up);
        Quaternion handRot = activeHand.rotation;

        return lookRot * Quaternion.Inverse(Quaternion.LookRotation(activeHand.forward, activeHand.up)) * handRot;
    }

    Quaternion ApplyRotationLimit(Quaternion baseRot, Quaternion targetRot)
    {
        Quaternion localRot = Quaternion.Inverse(baseRot) * targetRot;
        Vector3 euler = NormalizeEuler(localRot.eulerAngles);

        euler.x = Mathf.Clamp(euler.x, -maxFlexion, maxExtension);
        euler.y = Mathf.Clamp(euler.y, -maxRotation, maxRotation);
        euler.z = Mathf.Clamp(euler.z, -maxTilt, maxTilt);

        Quaternion limitedLocalRot = Quaternion.Euler(euler);
        return baseRot * limitedLocalRot;
    }

    Vector3 NormalizeEuler(Vector3 euler)
    {
        return new Vector3(
            Mathf.Repeat(euler.x + 180f, 360f) - 180f,
            Mathf.Repeat(euler.y + 180f, 360f) - 180f,
            Mathf.Repeat(euler.z + 180f, 360f) - 180f
        );
    }

    /// <summary>
    /// 계층 구조 전체에서 본 탐색
    /// </summary>
    Transform FindBoneRecursive(Transform root, params string[] names)
    {
        foreach (string name in names)
        {
            Transform found = FindChildRecursive(root, name);
            if (found != null) return found;
        }
        return null;
    }

    Transform FindChildRecursive(Transform parent, string name)
    {
        if (parent.name == name) return parent;

        foreach (Transform child in parent)
        {
            Transform found = FindChildRecursive(child, name);
            if (found != null) return found;
        }
        return null;
    }

    // ✅ Gizmos 시각화
    void OnDrawGizmosSelected()
    {
        if (neckBase == null) return;

        Gizmos.color = Color.gray;
        Gizmos.DrawWireSphere(neckBase.position, maxDistanceFromNeck);

        Gizmos.color = Color.green;
        DrawAngleCone(neckBase.position, neckBase.forward, neckBase.up, maxRotation, 0.1f);

        Gizmos.color = Color.magenta;
        DrawAngleCone(neckBase.position, neckBase.forward, neckBase.forward, maxTilt, 0.1f);

        Gizmos.color = Color.red;
        DrawAngleCone(neckBase.position, neckBase.forward, neckBase.right, maxFlexion, 0.1f);

        Gizmos.color = Color.blue;
        DrawAngleCone(neckBase.position, neckBase.forward, -neckBase.right, maxExtension, 0.1f);
    }

    void DrawAngleCone(Vector3 origin, Vector3 forward, Vector3 axis, float angle, float length)
    {
        int segments = 20;
        float step = angle / segments;

        for (int i = -segments; i < segments; i++)
        {
            Quaternion rot1 = Quaternion.AngleAxis(step * i, axis);
            Quaternion rot2 = Quaternion.AngleAxis(step * (i + 1), axis);

            Vector3 dir1 = rot1 * forward;
            Vector3 dir2 = rot2 * forward;

            Gizmos.DrawLine(origin, origin + dir1 * length);
            Gizmos.DrawLine(origin + dir1 * length, origin + dir2 * length);
        }
    }
}
