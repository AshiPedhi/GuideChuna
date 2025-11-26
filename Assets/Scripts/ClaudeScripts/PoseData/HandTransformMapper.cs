using System.Collections.Generic;
using UnityEngine;
using Oculus.Interaction;

/// <summary>
/// HandVisual의 조인트를 그대로 가져와서 사용하는 Transform 제어 스크립트
/// IHand나 실시간 추적 없이 조인트만 사용
/// </summary>
public class HandTransformMapper : MonoBehaviour
{
    [Header("손 모델 설정")]
    [SerializeField] private bool isLeftHand = true;

    [Header("HandVisual에서 조인트 가져오기")]
    [SerializeField] private HandVisual sourceHandVisual;
    [SerializeField] private bool copyJointsOnStart = true;

    [Header("조인트 매핑")]
    [SerializeField] private Transform rootTransform;
    [SerializeField] private List<Transform> joints = new List<Transform>(26);

    // HandVisual과의 호환성을 위한 프로퍼티
    public IList<Transform> Joints => joints;
    public Transform Root => rootTransform;
    public bool IsLeftHand => isLeftHand;

    private bool isInitialized = false;

    void Awake()
    {
        if (rootTransform == null)
        {
            rootTransform = transform;
        }

        // HandVisual이 있으면 조인트 복사
        if (copyJointsOnStart)
        {
            CopyJointsFromHandVisual();
        }
    }

    void Start()
    {
        ValidateJoints();
        isInitialized = true;
    }

    /// <summary>
    /// HandVisual에서 조인트 리스트를 그대로 복사
    /// </summary>
    public void CopyJointsFromHandVisual()
    {
        // sourceHandVisual이 없으면 자동으로 찾기
        if (sourceHandVisual == null)
        {
            sourceHandVisual = GetComponent<HandVisual>();
            if (sourceHandVisual == null)
            {
                sourceHandVisual = GetComponentInParent<HandVisual>();
            }
            if (sourceHandVisual == null)
            {
                sourceHandVisual = GetComponentInChildren<HandVisual>();
            }
        }

        if (sourceHandVisual != null)
        {
            joints.Clear();

            // HandVisual의 Joints 리스트를 그대로 복사
            if (sourceHandVisual.Joints != null && sourceHandVisual.Joints.Count > 0)
            {
                foreach (Transform joint in sourceHandVisual.Joints)
                {
                    joints.Add(joint);
                }

                Debug.Log($"[HandTransformMapper] HandVisual에서 {joints.Count}개 조인트 복사 완료 ({(isLeftHand ? "왼손" : "오른손")})");

                // Root도 HandVisual과 동일하게 설정
                if (sourceHandVisual.Root != null)
                {
                    rootTransform = sourceHandVisual.Root;
                }

                // HandVisual 컴포넌트 비활성화 (실시간 추적 차단)
                sourceHandVisual.enabled = false;
                Debug.Log("[HandTransformMapper] HandVisual 컴포넌트 비활성화 - 실시간 추적 차단");
            }
            else
            {
                Debug.LogWarning("[HandTransformMapper] HandVisual의 Joints가 비어있습니다. OVRSkeleton에서 직접 가져옵니다.");
                FindJointsFromSkeleton();
            }
        }
        else
        {
            Debug.LogWarning("[HandTransformMapper] HandVisual을 찾을 수 없습니다. OVRSkeleton에서 직접 가져옵니다.");
            FindJointsFromSkeleton();
        }
    }

    /// <summary>
    /// OVRSkeleton에서 직접 조인트 가져오기
    /// </summary>
    private void FindJointsFromSkeleton()
    {
        joints.Clear();

        OVRSkeleton skeleton = GetComponentInParent<OVRSkeleton>();
        if (skeleton == null)
        {
            skeleton = GetComponent<OVRSkeleton>();
        }

        if (skeleton != null && skeleton.Bones != null && skeleton.Bones.Count > 0)
        {
            foreach (var bone in skeleton.Bones)
            {
                if (bone != null && bone.Transform != null)
                {
                    joints.Add(bone.Transform);
                }
            }

            Debug.Log($"[HandTransformMapper] OVRSkeleton에서 {joints.Count}개 조인트 가져옴 ({(isLeftHand ? "왼손" : "오른손")})");
        }
        else
        {
            Debug.LogError("[HandTransformMapper] OVRSkeleton을 찾을 수 없습니다!");
        }
    }

    /// <summary>
    /// 조인트 유효성 검사
    /// </summary>
    private void ValidateJoints()
    {
        if (joints.Count == 0)
        {
            Debug.LogError($"[HandTransformMapper] 조인트가 없습니다! HandVisual 또는 OVRSkeleton을 확인하세요. ({(isLeftHand ? "왼손" : "오른손")})");
            return;
        }

        int validCount = 0;
        for (int i = 0; i < joints.Count; i++)
        {
            if (joints[i] != null)
                validCount++;
        }

        Debug.Log($"[HandTransformMapper] {validCount}/{joints.Count} 유효한 조인트 ({(isLeftHand ? "왼손" : "오른손")})");

        if (joints.Count != 26)
        {
            Debug.LogWarning($"[HandTransformMapper] 조인트 개수가 26개가 아닙니다: {joints.Count}개");
        }
    }

    /// <summary>
    /// 특정 인덱스의 조인트 Transform 가져오기
    /// </summary>
    public Transform GetJoint(int index)
    {
        if (index >= 0 && index < joints.Count)
            return joints[index];
        return null;
    }

    /// <summary>
    /// 특정 조인트의 로컬 포즈 설정
    /// </summary>
    public void SetJointLocalPose(int jointIndex, Vector3 localPosition, Quaternion localRotation)
    {
        if (jointIndex >= 0 && jointIndex < joints.Count && joints[jointIndex] != null)
        {
            joints[jointIndex].localPosition = localPosition;
            joints[jointIndex].localRotation = localRotation;
        }
    }

    /// <summary>
    /// 루트 Transform의 월드 포즈 설정
    /// </summary>
    public void SetRootWorldPose(Vector3 worldPosition, Quaternion worldRotation)
    {
        if (rootTransform != null)
        {
            rootTransform.position = worldPosition;
            rootTransform.rotation = worldRotation;
        }
    }

    /// <summary>
    /// 모든 조인트 포즈를 한번에 설정
    /// </summary>
    public void SetAllJointPoses(Dictionary<int, (Vector3 pos, Quaternion rot)> poses)
    {
        foreach (var kvp in poses)
        {
            SetJointLocalPose(kvp.Key, kvp.Value.pos, kvp.Value.rot);
        }
    }

    /// <summary>
    /// 수동으로 조인트 리스트 설정
    /// </summary>
    public void SetJoints(List<Transform> newJoints)
    {
        joints = new List<Transform>(newJoints);
        ValidateJoints();
    }

    /// <summary>
    /// 수동으로 HandVisual 설정
    /// </summary>
    public void SetSourceHandVisual(HandVisual handVisual)
    {
        sourceHandVisual = handVisual;
        if (handVisual != null)
        {
            CopyJointsFromHandVisual();
        }
    }

    /// <summary>
    /// 조인트 개수 반환
    /// </summary>
    public int GetJointCount()
    {
        return joints.Count;
    }

    /// <summary>
    /// 초기화 상태 확인
    /// </summary>
    public bool IsInitialized()
    {
        return isInitialized;
    }

    /// <summary>
    /// 손 타입 설정 (왼손/오른손)
    /// </summary>
    public void SetHandType(bool isLeft)
    {
        isLeftHand = isLeft;
    }

    /// <summary>
    /// 렌더러 표시/숨김 제어
    /// </summary>
    public void SetVisible(bool visible)
    {
        SkinnedMeshRenderer[] renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var renderer in renderers)
        {
            renderer.enabled = visible;
        }

        MeshRenderer[] meshRenderers = GetComponentsInChildren<MeshRenderer>(true);
        foreach (var renderer in meshRenderers)
        {
            renderer.enabled = visible;
        }
    }

    /// <summary>
    /// 재질 투명도 설정
    /// </summary>
    public void SetAlpha(float alpha)
    {
        SkinnedMeshRenderer[] renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var renderer in renderers)
        {
            if (renderer.material != null && renderer.material.HasProperty("_Color"))
            {
                Color color = renderer.material.color;
                color.a = alpha;
                renderer.material.color = color;
            }
        }
    }

    /// <summary>
    /// 재질 색상 설정
    /// </summary>
    public void SetColor(Color color)
    {
        SkinnedMeshRenderer[] renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var renderer in renderers)
        {
            if (renderer.material != null)
            {
                if (renderer.material.HasProperty("_Color"))
                {
                    renderer.material.color = color;
                }
                else if (renderer.material.HasProperty("_BaseColor"))
                {
                    renderer.material.SetColor("_BaseColor", color);
                }
            }
        }
    }

    /// <summary>
    /// 재질 색상과 투명도 동시 설정
    /// </summary>
    public void SetColorAndAlpha(Color color, float alpha)
    {
        Color finalColor = color;
        finalColor.a = alpha;

        SkinnedMeshRenderer[] renderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (var renderer in renderers)
        {
            if (renderer.material != null)
            {
                if (renderer.material.HasProperty("_Color"))
                {
                    renderer.material.color = finalColor;
                }
                else if (renderer.material.HasProperty("_BaseColor"))
                {
                    renderer.material.SetColor("_BaseColor", finalColor);
                }
            }
        }
    }

    /// <summary>
    /// 디버그 정보 출력
    /// </summary>
    public void DebugPrintJoints()
    {
        Debug.Log($"===== HandTransformMapper ({(isLeftHand ? "왼손" : "오른손")}) =====");
        Debug.Log($"Root: {(rootTransform != null ? rootTransform.name : "NULL")}");
        Debug.Log($"Joints: {joints.Count}개");

        for (int i = 0; i < joints.Count; i++)
        {
            if (joints[i] != null)
            {
                Debug.Log($"  [{i}] {joints[i].name}");
            }
            else
            {
                Debug.Log($"  [{i}] NULL");
            }
        }
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        // 에디터에서 HandVisual 변경 시 자동 복사
        if (sourceHandVisual != null && copyJointsOnStart && Application.isPlaying)
        {
            CopyJointsFromHandVisual();
        }
    }

    void OnDrawGizmosSelected()
    {
        if (joints == null || joints.Count == 0) return;

        Gizmos.color = isLeftHand ? Color.blue : Color.red;

        foreach (var joint in joints)
        {
            if (joint != null)
            {
                Gizmos.DrawWireSphere(joint.position, 0.005f);
            }
        }

        // 루트 표시
        if (rootTransform != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(rootTransform.position, 0.01f);
        }
    }
#endif
}