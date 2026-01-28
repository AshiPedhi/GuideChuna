using UnityEngine;

/// <summary>
/// UI Grab 감지 - OVRGrabbable과 함께 사용
/// Step 1: UI 옮기기 연습용
/// </summary>
public class UIGrabDetector : MonoBehaviour
{
    [Header("=== 연결 ===")]
    [Tooltip("PracticeManager 참조")]
    [SerializeField] private PracticeManager practiceManager;

    [Header("=== 설정 ===")]
    [Tooltip("이동 거리 임계값 (미터)")]
    [SerializeField] private float moveThreshold = 0.05f;

    [Tooltip("디버그 로그")]
    [SerializeField] private bool showDebugLogs = true;

    private Vector3 grabStartPosition;
    private bool isGrabbed = false;

    /// <summary>
    /// Grab 시작 시 호출 (OVRGrabbable 이벤트 또는 외부에서 호출)
    /// </summary>
    public void OnGrabBegin()
    {
        isGrabbed = true;
        grabStartPosition = transform.position;

        if (showDebugLogs)
            Debug.Log($"[UIGrabDetector] Grab started at {grabStartPosition}");
    }

    /// <summary>
    /// Grab 종료 시 호출 (OVRGrabbable 이벤트 또는 외부에서 호출)
    /// </summary>
    public void OnGrabEnd()
    {
        if (!isGrabbed) return;

        isGrabbed = false;
        float distance = Vector3.Distance(grabStartPosition, transform.position);

        if (showDebugLogs)
            Debug.Log($"[UIGrabDetector] Grab ended. Moved: {distance:F3}m");

        // 충분히 이동했으면 성공으로 처리
        if (distance >= moveThreshold)
        {
            NotifyPracticeManager();
        }
    }

    private void NotifyPracticeManager()
    {
        if (practiceManager != null)
        {
            practiceManager.OnUIGrabReleased();

            if (showDebugLogs)
                Debug.Log("[UIGrabDetector] Notified PracticeManager");
        }
        else
        {
            Debug.LogWarning("[UIGrabDetector] PracticeManager not assigned!");
        }
    }

    /// <summary>
    /// PracticeManager 설정
    /// </summary>
    public void SetPracticeManager(PracticeManager manager)
    {
        practiceManager = manager;
    }
}
