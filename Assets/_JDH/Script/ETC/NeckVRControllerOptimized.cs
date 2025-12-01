using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class NeckVRControllerOptimized : MonoBehaviour
{
    [Header("=== 활성화 설정 ===")]
    [Tooltip("평가 시작 전까지 비활성화 상태로 유지")]
    [SerializeField] private bool isEnabled = false;

    [Tooltip("비활성화 시 초기 위치로 복귀")]
    [SerializeField] private bool returnToInitialOnDisable = true;

    [Tooltip("복귀 속도")]
    [SerializeField] private float returnLerpSpeed = 5f;

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

    // 초기 회전 저장 (복귀용)
    private Quaternion initialNeckBaseRotation;
    private Quaternion initialNeckMidRotation;
    private Quaternion initialHeadRotation;

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

        // 초기 회전 저장
        SaveInitialRotations();

        if (neckBase != null && neckMid != null && head != null)
            Debug.Log($"[NeckVRController] 본 매핑 완료: {neckBase.name} → {neckMid.name} → {head.name}");
        else
            Debug.LogWarning("[NeckVRController] 일부 본을 찾지 못했습니다. Inspector에서 수동 연결하세요.");
    }

    /// <summary>
    /// 초기 회전값 저장
    /// </summary>
    private void SaveInitialRotations()
    {
        if (neckBase != null) initialNeckBaseRotation = neckBase.rotation;
        if (neckMid != null) initialNeckMidRotation = neckMid.rotation;
        if (head != null) initialHeadRotation = head.rotation;
    }

    void LateUpdate()
    {
        // 비활성화 상태면 초기 위치로 복귀
        if (!isEnabled)
        {
            if (returnToInitialOnDisable)
            {
                ReturnToInitialRotation();
            }
            return;
        }

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
        Quaternion limitedRot = ApplyRotationLimit(initialNeckBaseRotation, targetRot);

        // 3단계 본 구조: NeckTwist01 → NeckTwist02 → Head
        if (neckBase != null)
            neckBase.rotation = Quaternion.Slerp(neckBase.rotation, limitedRot, Time.deltaTime * rotationLerpSpeed);
        if (neckMid != null)
            neckMid.rotation = Quaternion.Slerp(neckMid.rotation, limitedRot, Time.deltaTime * rotationLerpSpeed * 1.3f);
        if (head != null)
            head.rotation = Quaternion.Slerp(head.rotation, limitedRot, Time.deltaTime * rotationLerpSpeed * 1.8f);

        transform.rotation = limitedRot;
    }

    /// <summary>
    /// 초기 회전으로 복귀
    /// </summary>
    private void ReturnToInitialRotation()
    {
        float t = Time.deltaTime * returnLerpSpeed;

        if (neckBase != null)
            neckBase.rotation = Quaternion.Slerp(neckBase.rotation, initialNeckBaseRotation, t);
        if (neckMid != null)
            neckMid.rotation = Quaternion.Slerp(neckMid.rotation, initialNeckMidRotation, t);
        if (head != null)
            head.rotation = Quaternion.Slerp(head.rotation, initialHeadRotation, t);
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

    // ========== Public API ==========

    /// <summary>
    /// 활성화 상태 확인
    /// </summary>
    public bool IsEnabled => isEnabled;

    /// <summary>
    /// 목 컨트롤러 활성화 (평가 시작 시 호출)
    /// </summary>
    public void Enable()
    {
        isEnabled = true;
        Debug.Log("<color=green>[NeckVRController] 활성화됨 - 손 움직임에 따라 목이 반응합니다</color>");
    }

    /// <summary>
    /// 목 컨트롤러 비활성화 (평가 종료 시 호출)
    /// </summary>
    public void Disable()
    {
        isEnabled = false;
        Debug.Log("<color=yellow>[NeckVRController] 비활성화됨 - 목이 초기 위치로 복귀합니다</color>");
    }

    /// <summary>
    /// 활성화 상태 설정
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        isEnabled = enabled;
        if (enabled)
            Debug.Log("<color=green>[NeckVRController] 활성화됨</color>");
        else
            Debug.Log("<color=yellow>[NeckVRController] 비활성화됨</color>");
    }

    /// <summary>
    /// 초기 회전 리셋 (현재 위치를 새 초기 위치로 저장)
    /// </summary>
    public void ResetInitialRotation()
    {
        SaveInitialRotations();
        Debug.Log("[NeckVRController] 현재 위치를 초기 위치로 저장했습니다");
    }

    /// <summary>
    /// 즉시 초기 위치로 복귀
    /// </summary>
    public void ResetToInitial()
    {
        if (neckBase != null) neckBase.rotation = initialNeckBaseRotation;
        if (neckMid != null) neckMid.rotation = initialNeckMidRotation;
        if (head != null) head.rotation = initialHeadRotation;
        Debug.Log("[NeckVRController] 초기 위치로 즉시 복귀");
    }

    /// <summary>
    /// ROM 제한값 설정
    /// </summary>
    public void SetRotationLimits(float flexion, float extension, float rotation, float tilt)
    {
        maxFlexion = flexion;
        maxExtension = extension;
        maxRotation = rotation;
        maxTilt = tilt;
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
