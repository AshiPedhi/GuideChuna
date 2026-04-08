using UnityEngine;

/// <summary>
/// 환자 위치 조절용 그랩 핸들 소환기.
/// replace pos가 달린 부모 오브젝트가 활성화될 때(OnEnable),
/// 지정한 자식 콜라이더를 헤드셋 기준 전방 위치로 1회 이동시킨다.
/// (부모는 그대로 두고 자식의 world position만 옮기므로 replace pos 본체는 간섭 없음)
///
/// 사용법:
/// 1. replace pos가 달린 부모 오브젝트(= Grabbable 본체)에 이 스크립트 부착
/// 2. handleTarget 필드에 이동시킬 자식 콜라이더 Transform 할당
/// 3. headsetAnchor는 비워두면 OVRCameraRig/TrackingSpace/CenterEyeAnchor 자동 탐색
/// 4. 설정 팝업의 "환자위치조절" 토글이 부모 오브젝트를 SetActive(true) 해주면 자동으로 소환됨
/// </summary>
public class PatientPositionHandleSummoner : MonoBehaviour
{
    [Header("=== 대상 ===")]
    [Tooltip("눈앞으로 이동시킬 자식 콜라이더 Transform (보통 Grab 콜라이더)")]
    [SerializeField] private Transform handleTarget;

    [Tooltip("기준이 될 헤드셋 Transform. 비워두면 OVRCameraRig/CenterEyeAnchor 자동 탐색")]
    [SerializeField] private Transform headsetAnchor;

    [Header("=== 배치 오프셋 ===")]
    [Tooltip("헤드셋 전방 거리 (m)")]
    [SerializeField] private float forwardDistance = 0.25f;

    [Tooltip("헤드셋 높이 오프셋 (m, +=위)")]
    [SerializeField] private float heightOffset = 0.25f;

    [Tooltip("헤드셋 좌우 오프셋 (m, +=오른쪽)")]
    [SerializeField] private float lateralOffset = 0f;

    [Tooltip("true면 헤드셋 수평방향(Y회전)만 사용하여 고개를 숙여도 높이가 일정. false면 헤드셋 로컬 오프셋 그대로(머리 따라 기울어짐)")]
    [SerializeField] private bool ignoreHeadPitch = true;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLog = true;

    private void OnEnable()
    {
        SummonHandle();
    }

    /// <summary>
    /// 자식 핸들 콜라이더를 헤드셋 앞으로 이동. 외부에서 수동 호출도 가능.
    /// </summary>
    public void SummonHandle()
    {
        if (handleTarget == null)
        {
            ChunaLogger.LogWarning("[PatientPositionHandleSummoner] handleTarget이 할당되지 않았습니다.");
            return;
        }

        if (headsetAnchor == null)
        {
            AutoFindHeadset();
            if (headsetAnchor == null)
            {
                ChunaLogger.LogWarning("[PatientPositionHandleSummoner] 헤드셋 Transform을 찾을 수 없어 소환을 건너뜁니다.");
                return;
            }
        }

        Vector3 targetPos = CalculateTargetPosition();
        handleTarget.position = targetPos;

        if (showDebugLog)
        {
            ChunaLogger.Log($"<color=cyan>[PatientPositionHandleSummoner] '{handleTarget.name}' 소환: {targetPos} (헤드셋={headsetAnchor.name})</color>");
        }
    }

    /// <summary>
    /// 현재 헤드셋 기준 목표 월드 위치 계산
    /// </summary>
    public Vector3 CalculateTargetPosition()
    {
        if (headsetAnchor == null) return Vector3.zero;

        if (ignoreHeadPitch)
        {
            // 헤드셋의 수평 방향만 사용 (pitch/roll 무시)
            Vector3 forward = headsetAnchor.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();

            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

            return headsetAnchor.position
                + forward * forwardDistance
                + Vector3.up * heightOffset
                + right * lateralOffset;
        }
        else
        {
            // 헤드셋 로컬 좌표계 그대로 (머리 기울임 반영)
            return headsetAnchor.TransformPoint(new Vector3(lateralOffset, heightOffset, forwardDistance));
        }
    }

    /// <summary>
    /// OVRCameraRig/TrackingSpace/CenterEyeAnchor 자동 탐색
    /// </summary>
    private void AutoFindHeadset()
    {
        GameObject rig = GameObject.Find("OVRCameraRig");
        if (rig != null)
        {
            Transform centerEye = rig.transform.Find("TrackingSpace/CenterEyeAnchor");
            if (centerEye != null)
            {
                headsetAnchor = centerEye;
                if (showDebugLog)
                    ChunaLogger.Log("[PatientPositionHandleSummoner] CenterEyeAnchor 자동 탐색 성공");
                return;
            }
        }

        // 폴백: Camera.main
        if (Camera.main != null)
        {
            headsetAnchor = Camera.main.transform;
            if (showDebugLog)
                ChunaLogger.Log("[PatientPositionHandleSummoner] Camera.main으로 폴백");
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (headsetAnchor == null || handleTarget == null) return;

        Vector3 targetPos = CalculateTargetPosition();

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(targetPos, 0.03f);

        Gizmos.color = Color.green;
        Gizmos.DrawLine(headsetAnchor.position, targetPos);

        Gizmos.color = Color.red;
        Gizmos.DrawLine(handleTarget.position, targetPos);
    }
#endif
}
