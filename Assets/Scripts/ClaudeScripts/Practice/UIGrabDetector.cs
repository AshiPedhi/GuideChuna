using UnityEngine;
using Oculus.Interaction;

/// <summary>
/// UI Grab 감지 - Meta XR SDK 연동
/// OVRGrabbable 또는 Interaction SDK의 Grabbable 자동 감지
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

    // 상태
    private Vector3 grabStartPosition;
    private bool isGrabbed = false;

    // Meta XR 컴포넌트 (자동 감지)
    private OVRGrabbable ovrGrabbable;
    private Grabbable interactionGrabbable;

    void Awake()
    {
        // OVRGrabbable 찾기
        ovrGrabbable = GetComponent<OVRGrabbable>();

        // Interaction SDK Grabbable 찾기
        interactionGrabbable = GetComponent<Grabbable>();
    }

    void OnEnable()
    {
        // Interaction SDK 이벤트 연결
        if (interactionGrabbable != null)
        {
            interactionGrabbable.WhenPointerEventRaised += OnPointerEvent;
        }
    }

    void OnDisable()
    {
        // Interaction SDK 이벤트 해제
        if (interactionGrabbable != null)
        {
            interactionGrabbable.WhenPointerEventRaised -= OnPointerEvent;
        }
    }

    void Update()
    {
        // OVRGrabbable 상태 감지 (이벤트가 없으므로 폴링)
        if (ovrGrabbable != null)
        {
            bool currentlyGrabbed = ovrGrabbable.isGrabbed;

            if (currentlyGrabbed && !isGrabbed)
            {
                OnGrabBegin();
            }
            else if (!currentlyGrabbed && isGrabbed)
            {
                OnGrabEnd();
            }
        }
    }

    /// <summary>
    /// Interaction SDK 포인터 이벤트 처리
    /// </summary>
    private void OnPointerEvent(PointerEvent evt)
    {
        switch (evt.Type)
        {
            case PointerEventType.Select:
                OnGrabBegin();
                break;
            case PointerEventType.Unselect:
                OnGrabEnd();
                break;
        }
    }

    /// <summary>
    /// Grab 시작
    /// </summary>
    public void OnGrabBegin()
    {
        if (isGrabbed) return;

        isGrabbed = true;
        grabStartPosition = transform.position;

        if (showDebugLogs)
            Debug.Log($"[UIGrabDetector] Grab 시작 - 위치: {grabStartPosition}");
    }

    /// <summary>
    /// Grab 종료
    /// </summary>
    public void OnGrabEnd()
    {
        if (!isGrabbed) return;

        isGrabbed = false;
        float distance = Vector3.Distance(grabStartPosition, transform.position);

        if (showDebugLogs)
            Debug.Log($"[UIGrabDetector] Grab 종료 - 이동거리: {distance:F3}m");

        // 충분히 이동했으면 성공
        if (distance >= moveThreshold)
        {
            NotifyPracticeManager();
        }
        else
        {
            if (showDebugLogs)
                Debug.Log($"[UIGrabDetector] 이동 거리 부족 (필요: {moveThreshold}m)");
        }
    }

    private void NotifyPracticeManager()
    {
        if (practiceManager != null)
        {
            practiceManager.OnUIGrabReleased();

            if (showDebugLogs)
                Debug.Log("[UIGrabDetector] ✓ UI 옮기기 1회 완료!");
        }
        else
        {
            // PracticeManager 자동 찾기
            practiceManager = FindFirstObjectByType<PracticeManager>();
            if (practiceManager != null)
            {
                practiceManager.OnUIGrabReleased();
            }
            else
            {
                Debug.LogWarning("[UIGrabDetector] PracticeManager를 찾을 수 없습니다!");
            }
        }
    }

    /// <summary>
    /// PracticeManager 설정
    /// </summary>
    public void SetPracticeManager(PracticeManager manager)
    {
        practiceManager = manager;
    }

    /// <summary>
    /// 현재 Grab 상태
    /// </summary>
    public bool IsGrabbed => isGrabbed;
}
