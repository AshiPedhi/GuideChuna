using UnityEngine;

/// <summary>
/// 환자 위치 변경 감지
/// Step 4: 환자 위치 조정 연습용
/// </summary>
public class PatientPositionDetector : MonoBehaviour
{
    [Header("=== 연결 ===")]
    [Tooltip("PracticeManager 참조")]
    [SerializeField] private PracticeManager practiceManager;

    [Header("=== 설정 ===")]
    [Tooltip("위치 변경 감지 임계값 (미터)")]
    [SerializeField] private float positionThreshold = 0.01f;

    [Tooltip("회전 변경 감지 임계값 (도)")]
    [SerializeField] private float rotationThreshold = 1f;

    [Tooltip("감지 활성화")]
    [SerializeField] private bool detectEnabled = true;

    [Tooltip("디버그 로그")]
    [SerializeField] private bool showDebugLogs = true;

    private Vector3 lastPosition;
    private Quaternion lastRotation;
    private bool hasNotified = false;

    void Start()
    {
        lastPosition = transform.position;
        lastRotation = transform.rotation;
    }

    void Update()
    {
        if (!detectEnabled || hasNotified) return;

        CheckPositionChange();
    }

    private void CheckPositionChange()
    {
        float positionDelta = Vector3.Distance(transform.position, lastPosition);
        float rotationDelta = Quaternion.Angle(transform.rotation, lastRotation);

        if (positionDelta > positionThreshold || rotationDelta > rotationThreshold)
        {
            if (showDebugLogs)
                ChunaLogger.Log($"[PatientPosition] Changed! Pos: {positionDelta:F3}m, Rot: {rotationDelta:F1}°");

            NotifyPracticeManager();
            hasNotified = true;
        }
    }

    private void NotifyPracticeManager()
    {
        if (practiceManager != null)
        {
            practiceManager.OnPatientPositionChanged();

            if (showDebugLogs)
                ChunaLogger.Log("[PatientPosition] Notified PracticeManager");
        }
        else
        {
            ChunaLogger.LogWarning("[PatientPosition] PracticeManager not assigned!");
        }
    }

    /// <summary>
    /// 감지 리셋 (새 라운드 시작 시)
    /// </summary>
    public void ResetDetection()
    {
        lastPosition = transform.position;
        lastRotation = transform.rotation;
        hasNotified = false;

        if (showDebugLogs)
            ChunaLogger.Log("[PatientPosition] Detection reset");
    }

    /// <summary>
    /// 감지 활성화/비활성화
    /// </summary>
    public void SetDetectEnabled(bool enabled)
    {
        detectEnabled = enabled;
        if (enabled)
        {
            ResetDetection();
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
