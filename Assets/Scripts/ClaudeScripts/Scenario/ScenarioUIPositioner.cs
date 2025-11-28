using UnityEngine;

/// <summary>
/// 시나리오 UI 자동 배치 컨트롤러
/// 시나리오 시작 시 헤드셋 위치를 기준으로 UI를 자동 배치
/// </summary>
public class ScenarioUIPositioner : MonoBehaviour
{
    [Header("=== UI 배치 대상 ===")]
    [Tooltip("자동 배치할 UI Canvas 또는 UI 루트 오브젝트")]
    [SerializeField] private Transform[] uiTargets;

    [Header("=== 헤드셋 참조 ===")]
    [Tooltip("헤드셋 Transform (OVR CenterEyeAnchor)")]
    [SerializeField] private Transform headsetTransform;

    [Header("=== 배치 설정 ===")]
    [Tooltip("헤드셋 전방으로부터의 거리 (미터)")]
    [SerializeField] private float forwardDistance = 1.5f;

    [Tooltip("헤드셋으로부터의 높이 오프셋 (미터)")]
    [SerializeField] private float heightOffset = 0f;

    [Tooltip("시나리오 시작 시 자동으로 UI 배치")]
    [SerializeField] private bool autoPositionOnStart = true;

    [Tooltip("UI가 항상 헤드셋을 바라보도록 설정")]
    [SerializeField] private bool lookAtHeadset = true;

    // UI 위치 초기화가 한 번만 실행되도록 하는 플래그
    private bool hasPositionedOnce = false;

    void Awake()
    {
        // 헤드셋 Transform 자동 찾기
        if (headsetTransform == null)
        {
            GameObject ovrCameraRig = GameObject.Find("OVRCameraRig");
            if (ovrCameraRig != null)
            {
                headsetTransform = ovrCameraRig.transform.Find("TrackingSpace/CenterEyeAnchor");
                if (headsetTransform != null)
                {
                    Debug.Log("[ScenarioUIPositioner] ✅ CenterEyeAnchor 자동 찾기 성공");
                }
                else
                {
                    Debug.LogError("[ScenarioUIPositioner] ❌ CenterEyeAnchor를 찾을 수 없습니다!");
                }
            }
            else
            {
                Debug.LogError("[ScenarioUIPositioner] ❌ OVRCameraRig를 찾을 수 없습니다!");
            }
        }
    }

    void Start()
    {
        if (autoPositionOnStart)
        {
            PositionUIElements();
        }
    }

    /// <summary>
    /// UI 요소들을 헤드셋 위치 기준으로 배치
    /// (시나리오당 한 번만 실행됨)
    /// </summary>
    public void PositionUIElements()
    {
        // 이미 배치가 완료된 경우 건너뜀
        if (hasPositionedOnce)
        {
            Debug.Log("[ScenarioUIPositioner] 이미 UI 배치가 완료되었습니다. 건너뜁니다.");
            return;
        }

        if (headsetTransform == null)
        {
            Debug.LogError("[ScenarioUIPositioner] headsetTransform이 null입니다! UI 배치를 건너뜁니다.");
            return;
        }

        if (uiTargets == null || uiTargets.Length == 0)
        {
            Debug.LogWarning("[ScenarioUIPositioner] uiTargets가 비어있습니다! UI 배치를 건너뜁니다.");
            return;
        }

        Vector3 headsetPosition = headsetTransform.position;
        Vector3 headsetForward = headsetTransform.forward;

        // Y축은 수평 방향만 고려
        headsetForward.y = 0;
        headsetForward.Normalize();

        // 목표 위치 계산
        Vector3 targetPosition = new Vector3(
            headsetPosition.x + headsetForward.x * forwardDistance,
            headsetPosition.y + heightOffset,
            headsetPosition.z + headsetForward.z * forwardDistance
        );

        // 모든 UI 대상에 적용
        foreach (var uiTarget in uiTargets)
        {
            if (uiTarget == null)
            {
                Debug.LogWarning("[ScenarioUIPositioner] null UI target 발견, 건너뜁니다.");
                continue;
            }

            // 위치 설정
            uiTarget.position = targetPosition;

            // UI가 헤드셋을 바라보도록 설정
            if (lookAtHeadset)
            {
                Vector3 lookDirection = headsetPosition - targetPosition;
                lookDirection.y = 0; // 수평 방향만 고려

                if (lookDirection.sqrMagnitude > 0.001f)
                {
                    uiTarget.rotation = Quaternion.LookRotation(lookDirection);
                }
            }

            Debug.Log($"[ScenarioUIPositioner] ✅ UI 배치 완료: {uiTarget.name} -> {targetPosition}");
        }

        // 플래그 설정: 한 번만 실행되도록
        hasPositionedOnce = true;

        Debug.Log($"[ScenarioUIPositioner] 총 {uiTargets.Length}개 UI 배치 완료 (이후 재실행 방지)");
    }

    /// <summary>
    /// 수동으로 UI 재배치 (설정 변경 후 호출 가능)
    /// 플래그를 리셋하여 강제로 재배치합니다.
    /// </summary>
    [ContextMenu("Reposition UI")]
    public void RepositionUI()
    {
        hasPositionedOnce = false;
        PositionUIElements();
    }

    /// <summary>
    /// UI 위치 초기화 플래그를 리셋합니다.
    /// (새 시나리오 시작 시 호출)
    /// </summary>
    public void ResetPositionFlag()
    {
        hasPositionedOnce = false;
        Debug.Log("[ScenarioUIPositioner] UI 위치 초기화 플래그 리셋");
    }

    /// <summary>
    /// 배치 거리 설정
    /// </summary>
    public void SetForwardDistance(float distance)
    {
        forwardDistance = distance;
        Debug.Log($"[ScenarioUIPositioner] 전방 거리 변경: {distance}m");
    }

    /// <summary>
    /// 높이 오프셋 설정
    /// </summary>
    public void SetHeightOffset(float offset)
    {
        heightOffset = offset;
        Debug.Log($"[ScenarioUIPositioner] 높이 오프셋 변경: {offset}m");
    }

    /// <summary>
    /// UI 대상 추가
    /// </summary>
    public void AddUITarget(Transform uiTarget)
    {
        if (uiTarget == null) return;

        // 배열 확장
        var newTargets = new Transform[uiTargets.Length + 1];
        for (int i = 0; i < uiTargets.Length; i++)
        {
            newTargets[i] = uiTargets[i];
        }
        newTargets[uiTargets.Length] = uiTarget;
        uiTargets = newTargets;

        Debug.Log($"[ScenarioUIPositioner] UI 대상 추가: {uiTarget.name}");
    }

    /// <summary>
    /// 현재 헤드셋 기준 목표 위치 계산
    /// </summary>
    public Vector3 CalculateTargetPosition()
    {
        if (headsetTransform == null)
        {
            Debug.LogWarning("[ScenarioUIPositioner] headsetTransform이 null입니다!");
            return Vector3.zero;
        }

        Vector3 headsetPosition = headsetTransform.position;
        Vector3 headsetForward = headsetTransform.forward;

        headsetForward.y = 0;
        headsetForward.Normalize();

        return new Vector3(
            headsetPosition.x + headsetForward.x * forwardDistance,
            headsetPosition.y + heightOffset,
            headsetPosition.z + headsetForward.z * forwardDistance
        );
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        // 디버그용 Gizmo 표시
        if (headsetTransform != null)
        {
            Vector3 targetPos = CalculateTargetPosition();

            // 목표 위치 표시 (노란색 구)
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(targetPos, 0.1f);

            // 헤드셋에서 목표 위치로의 선
            Gizmos.color = Color.green;
            Gizmos.DrawLine(headsetTransform.position, targetPos);
        }
    }
#endif
}
