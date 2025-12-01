using System;
using UnityEngine;

/// <summary>
/// 경로상의 물리적 체크포인트
/// 손이 트리거에 진입하면 이벤트 발생
/// </summary>
[RequireComponent(typeof(SphereCollider))]
public class PathCheckpoint : MonoBehaviour
{
    [Header("=== 체크포인트 정보 ===")]
    [SerializeField] private int checkpointIndex = 0;
    [SerializeField] private string checkpointName = "Checkpoint";
    [SerializeField] private bool isStartPoint = false;
    [SerializeField] private bool isEndPoint = false;

    [Header("=== 통과 조건 ===")]
    [Tooltip("체크포인트 내에서 유지해야 하는 시간 (0 = 즉시 통과)")]
    [SerializeField] private float requiredHoldTime = 0.2f;

    [Tooltip("손모양 유사도 체크 활성화")]
    [SerializeField] private bool checkHandPose = false;

    [Tooltip("통과에 필요한 최소 유사도 (0~1)")]
    [SerializeField] [Range(0f, 1f)] private float requiredSimilarity = 0.3f;

    [Header("=== 트리거 설정 ===")]
    [Tooltip("트리거 반경 (미터)")]
    [SerializeField] private float triggerRadius = 0.15f;

    [Tooltip("왼손 감지")]
    [SerializeField] private bool detectLeftHand = true;

    [Tooltip("오른손 감지")]
    [SerializeField] private bool detectRightHand = true;

    [Header("=== 참조 포즈 (자동 설정됨) ===")]
    [SerializeField] private Vector3 referenceLeftHandPosition;
    [SerializeField] private Quaternion referenceLeftHandRotation = Quaternion.identity;
    [SerializeField] private Vector3 referenceRightHandPosition;
    [SerializeField] private Quaternion referenceRightHandRotation = Quaternion.identity;

    [Header("=== 시각화 ===")]
    [SerializeField] private bool showGizmo = true;
    [SerializeField] private Color gizmoColor = new Color(0f, 1f, 0.5f, 0.3f);
    [SerializeField] private Color activeColor = new Color(1f, 1f, 0f, 0.5f);
    [SerializeField] private Color passedColor = new Color(0.5f, 0.5f, 0.5f, 0.2f);

    // 상태
    private bool isActive = false;
    private bool isPassed = false;
    private bool isLeftHandInside = false;
    private bool isRightHandInside = false;
    private float leftHandHoldTimer = 0f;
    private float rightHandHoldTimer = 0f;
    private float currentLeftSimilarity = 0f;
    private float currentRightSimilarity = 0f;

    // 컴포넌트
    private SphereCollider sphereCollider;
    private MeshRenderer visualRenderer;

    // 이벤트
    public event Action<PathCheckpoint, bool> OnCheckpointEntered;  // checkpoint, isLeftHand
    public event Action<PathCheckpoint, bool> OnCheckpointExited;   // checkpoint, isLeftHand
    public event Action<PathCheckpoint, bool, float> OnCheckpointPassed;  // checkpoint, isLeftHand, similarity

    // 프로퍼티
    public int CheckpointIndex => checkpointIndex;
    public string CheckpointName => checkpointName;
    public bool IsStartPoint => isStartPoint;
    public bool IsEndPoint => isEndPoint;
    public bool IsActive => isActive;
    public bool IsPassed => isPassed;
    public float RequiredHoldTime => requiredHoldTime;
    public float RequiredSimilarity => requiredSimilarity;
    public bool CheckHandPose => checkHandPose;
    public Vector3 ReferenceLeftHandPosition => referenceLeftHandPosition;
    public Quaternion ReferenceLeftHandRotation => referenceLeftHandRotation;
    public Vector3 ReferenceRightHandPosition => referenceRightHandPosition;
    public Quaternion ReferenceRightHandRotation => referenceRightHandRotation;

    void Awake()
    {
        SetupCollider();
    }

    void Update()
    {
        if (!isActive || isPassed) return;

        // 홀드 타이머 업데이트
        UpdateHoldTimers();
    }

    /// <summary>
    /// 콜라이더 설정
    /// </summary>
    private void SetupCollider()
    {
        sphereCollider = GetComponent<SphereCollider>();
        if (sphereCollider == null)
        {
            sphereCollider = gameObject.AddComponent<SphereCollider>();
        }

        sphereCollider.isTrigger = true;
        sphereCollider.radius = triggerRadius;
    }

    /// <summary>
    /// 홀드 타이머 업데이트
    /// </summary>
    private void UpdateHoldTimers()
    {
        bool leftMeetsCriteria = isLeftHandInside && (!checkHandPose || currentLeftSimilarity >= requiredSimilarity);
        bool rightMeetsCriteria = isRightHandInside && (!checkHandPose || currentRightSimilarity >= requiredSimilarity);

        // 왼손 타이머
        if (detectLeftHand && leftMeetsCriteria)
        {
            leftHandHoldTimer += Time.deltaTime;
            if (leftHandHoldTimer >= requiredHoldTime)
            {
                PassCheckpoint(true, currentLeftSimilarity);
            }
        }
        else if (detectLeftHand)
        {
            leftHandHoldTimer = Mathf.Max(0, leftHandHoldTimer - Time.deltaTime * 2f); // 빠르게 감소
        }

        // 오른손 타이머
        if (detectRightHand && rightMeetsCriteria)
        {
            rightHandHoldTimer += Time.deltaTime;
            if (rightHandHoldTimer >= requiredHoldTime)
            {
                PassCheckpoint(false, currentRightSimilarity);
            }
        }
        else if (detectRightHand)
        {
            rightHandHoldTimer = Mathf.Max(0, rightHandHoldTimer - Time.deltaTime * 2f);
        }
    }

    /// <summary>
    /// 체크포인트 통과 처리
    /// </summary>
    private void PassCheckpoint(bool isLeftHand, float similarity)
    {
        if (isPassed) return;

        isPassed = true;
        isActive = false;

        Debug.Log($"<color=green>[PathCheckpoint] {checkpointName} 통과! (유사도: {similarity:P0})</color>");

        OnCheckpointPassed?.Invoke(this, isLeftHand, similarity);

        UpdateVisual();
    }

    /// <summary>
    /// 트리거 진입
    /// </summary>
    void OnTriggerEnter(Collider other)
    {
        if (!isActive || isPassed) return;

        bool isLeftHand = IsLeftHand(other);
        bool isRightHand = IsRightHand(other);

        if (isLeftHand && detectLeftHand)
        {
            isLeftHandInside = true;
            OnCheckpointEntered?.Invoke(this, true);
            Debug.Log($"[PathCheckpoint] {checkpointName}: 왼손 진입");
        }

        if (isRightHand && detectRightHand)
        {
            isRightHandInside = true;
            OnCheckpointEntered?.Invoke(this, false);
            Debug.Log($"[PathCheckpoint] {checkpointName}: 오른손 진입");
        }
    }

    /// <summary>
    /// 트리거 이탈
    /// </summary>
    void OnTriggerExit(Collider other)
    {
        bool isLeftHand = IsLeftHand(other);
        bool isRightHand = IsRightHand(other);

        if (isLeftHand)
        {
            isLeftHandInside = false;
            leftHandHoldTimer = 0f;
            OnCheckpointExited?.Invoke(this, true);
        }

        if (isRightHand)
        {
            isRightHandInside = false;
            rightHandHoldTimer = 0f;
            OnCheckpointExited?.Invoke(this, false);
        }
    }

    /// <summary>
    /// 왼손인지 확인
    /// </summary>
    private bool IsLeftHand(Collider other)
    {
        // 1. 이름으로 판별 (가장 안전한 방법)
        string lowerName = other.name.ToLower();
        if (lowerName.Contains("left") && (lowerName.Contains("hand") || lowerName.Contains("wrist") || lowerName.Contains("palm")))
            return true;

        // 2. 부모 오브젝트 이름 확인
        Transform parent = other.transform.parent;
        while (parent != null)
        {
            string parentLower = parent.name.ToLower();
            if (parentLower.Contains("left") && (parentLower.Contains("hand") || parentLower.Contains("skeleton") || parentLower.Contains("visual")))
                return true;
            parent = parent.parent;
        }

        // 3. OVR/Oculus 컴포넌트로 확인
        var ovrHand = other.GetComponentInParent<Oculus.Interaction.Input.Hand>();
        if (ovrHand != null && ovrHand.Handedness == Oculus.Interaction.Input.Handedness.Left)
            return true;

        return false;
    }

    /// <summary>
    /// 오른손인지 확인
    /// </summary>
    private bool IsRightHand(Collider other)
    {
        // 1. 이름으로 판별 (가장 안전한 방법)
        string lowerName = other.name.ToLower();
        if (lowerName.Contains("right") && (lowerName.Contains("hand") || lowerName.Contains("wrist") || lowerName.Contains("palm")))
            return true;

        // 2. 부모 오브젝트 이름 확인
        Transform parent = other.transform.parent;
        while (parent != null)
        {
            string parentLower = parent.name.ToLower();
            if (parentLower.Contains("right") && (parentLower.Contains("hand") || parentLower.Contains("skeleton") || parentLower.Contains("visual")))
                return true;
            parent = parent.parent;
        }

        // 3. OVR/Oculus 컴포넌트로 확인
        var ovrHand = other.GetComponentInParent<Oculus.Interaction.Input.Hand>();
        if (ovrHand != null && ovrHand.Handedness == Oculus.Interaction.Input.Handedness.Right)
            return true;

        return false;
    }

    // ========== Public API ==========

    /// <summary>
    /// 체크포인트 활성화
    /// </summary>
    public void Activate()
    {
        isActive = true;
        isPassed = false;
        leftHandHoldTimer = 0f;
        rightHandHoldTimer = 0f;

        UpdateVisual();

        Debug.Log($"[PathCheckpoint] {checkpointName} 활성화");
    }

    /// <summary>
    /// 체크포인트 비활성화
    /// </summary>
    public void Deactivate()
    {
        isActive = false;
        UpdateVisual();
    }

    /// <summary>
    /// 체크포인트 리셋
    /// </summary>
    public void ResetCheckpoint()
    {
        isActive = false;
        isPassed = false;
        isLeftHandInside = false;
        isRightHandInside = false;
        leftHandHoldTimer = 0f;
        rightHandHoldTimer = 0f;
        currentLeftSimilarity = 0f;
        currentRightSimilarity = 0f;

        UpdateVisual();
    }

    /// <summary>
    /// 유사도 업데이트 (외부에서 호출)
    /// </summary>
    public void UpdateSimilarity(bool isLeftHand, float similarity)
    {
        if (isLeftHand)
            currentLeftSimilarity = similarity;
        else
            currentRightSimilarity = similarity;
    }

    /// <summary>
    /// 현재 홀드 진행률 가져오기
    /// </summary>
    public float GetHoldProgress(bool isLeftHand)
    {
        if (requiredHoldTime <= 0) return 1f;

        float timer = isLeftHand ? leftHandHoldTimer : rightHandHoldTimer;
        return Mathf.Clamp01(timer / requiredHoldTime);
    }

    /// <summary>
    /// 손이 체크포인트 안에 있는지 확인
    /// </summary>
    public bool IsHandInside(bool isLeftHand)
    {
        return isLeftHand ? isLeftHandInside : isRightHandInside;
    }

    /// <summary>
    /// 체크포인트 초기화 (자동 생성 시 사용)
    /// </summary>
    public void Initialize(int index, string name, Vector3 position,
        Vector3 leftHandPos, Quaternion leftHandRot,
        Vector3 rightHandPos, Quaternion rightHandRot,
        float holdTime = 0.5f, float similarity = 0.6f)
    {
        checkpointIndex = index;
        checkpointName = name;
        transform.position = position;

        referenceLeftHandPosition = leftHandPos;
        referenceLeftHandRotation = leftHandRot;
        referenceRightHandPosition = rightHandPos;
        referenceRightHandRotation = rightHandRot;

        requiredHoldTime = holdTime;
        requiredSimilarity = similarity;

        SetupCollider();
    }

    /// <summary>
    /// 트리거 반경 설정
    /// </summary>
    public void SetTriggerRadius(float radius)
    {
        triggerRadius = radius;
        if (sphereCollider != null)
        {
            sphereCollider.radius = radius;
        }
    }

    /// <summary>
    /// 감지할 손 설정
    /// </summary>
    public void SetDetectHand(bool left, bool right)
    {
        detectLeftHand = left;
        detectRightHand = right;
    }

    /// <summary>
    /// 통과 조건 설정
    /// </summary>
    public void SetPassConditions(float holdTime, float similarity, bool checkPose)
    {
        requiredHoldTime = holdTime;
        requiredSimilarity = similarity;
        checkHandPose = checkPose;
    }

    /// <summary>
    /// 유사도 체크 활성화/비활성화
    /// </summary>
    public void SetCheckHandPose(bool enable)
    {
        checkHandPose = enable;
    }

    /// <summary>
    /// 시각화 업데이트
    /// </summary>
    private void UpdateVisual()
    {
        if (visualRenderer != null)
        {
            if (isPassed)
                visualRenderer.material.color = passedColor;
            else if (isActive)
                visualRenderer.material.color = activeColor;
            else
                visualRenderer.material.color = gizmoColor;
        }
    }

    void OnDrawGizmos()
    {
        if (!showGizmo) return;

        // 체크포인트 구체
        if (isPassed)
            Gizmos.color = passedColor;
        else if (isActive)
            Gizmos.color = activeColor;
        else
            Gizmos.color = gizmoColor;

        Gizmos.DrawSphere(transform.position, triggerRadius);

        // 와이어프레임
        Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 1f);
        Gizmos.DrawWireSphere(transform.position, triggerRadius);

        // 인덱스 표시 (에디터에서만)
#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.1f,
            $"{checkpointIndex}: {checkpointName}");
#endif
    }

    void OnDrawGizmosSelected()
    {
        // 선택 시 참조 손 위치 표시
        Gizmos.color = Color.blue;
        if (referenceLeftHandPosition != Vector3.zero)
        {
            Gizmos.DrawWireSphere(referenceLeftHandPosition, 0.02f);
            Gizmos.DrawLine(transform.position, referenceLeftHandPosition);
        }

        Gizmos.color = Color.red;
        if (referenceRightHandPosition != Vector3.zero)
        {
            Gizmos.DrawWireSphere(referenceRightHandPosition, 0.02f);
            Gizmos.DrawLine(transform.position, referenceRightHandPosition);
        }
    }
}
