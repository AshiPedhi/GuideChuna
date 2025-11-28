using System;
using System.Collections.Generic;
using UnityEngine;
using Oculus.Interaction.Input;
using Oculus.Interaction;

/// <summary>
/// 안전 위치 관리 및 되돌리기 기능
/// 마지막 안전 위치를 저장하고 한계 초과 시 되돌리기 가이드 제공
/// </summary>
public class SafePositionManager : MonoBehaviour
{
    [Header("=== 한계 데이터 ===")]
    [SerializeField] private ChunaLimitData limitData;

    [Header("=== 손 참조 ===")]
    [SerializeField] private HandVisual playerLeftHand;
    [SerializeField] private HandVisual playerRightHand;
    [SerializeField] private Transform leftHandRoot;
    [SerializeField] private Transform rightHandRoot;

    [Header("=== 가이드 손 (되돌리기 위치 표시용) ===")]
    [SerializeField] private HandTransformMapper leftGuideHand;
    [SerializeField] private HandTransformMapper rightGuideHand;

    [Header("=== 되돌리기 설정 ===")]
    [Tooltip("되돌리기 가이드 활성화")]
    [SerializeField] private bool enableRevertGuide = true;

    [Tooltip("되돌리기 가이드 보간 속도")]
    [SerializeField] private float revertLerpSpeed = 3f;

    [Tooltip("안전 위치 저장 간격 (초)")]
    [SerializeField] private float safePositionSaveInterval = 0.2f;

    [Tooltip("안전 위치 기록 최대 개수")]
    [SerializeField] private int maxSafePositionHistory = 10;

    [Header("=== 시각적 피드백 ===")]
    [Tooltip("경고 색상")]
    [SerializeField] private Color warningColor = new Color(1f, 1f, 0f, 0.5f);

    [Tooltip("위험 색상")]
    [SerializeField] private Color dangerColor = new Color(1f, 0.5f, 0f, 0.5f);

    [Tooltip("초과 색상")]
    [SerializeField] private Color exceededColor = new Color(1f, 0f, 0f, 0.7f);

    [Tooltip("안전 위치 색상")]
    [SerializeField] private Color safePositionColor = new Color(0f, 1f, 0.5f, 0.5f);

    [Header("=== 햅틱 피드백 ===")]
    [Tooltip("햅틱 피드백 활성화")]
    [SerializeField] private bool enableHapticFeedback = true;

    [Tooltip("경고 햅틱 강도")]
    [SerializeField] [Range(0f, 1f)] private float warningHapticStrength = 0.3f;

    [Tooltip("위험 햅틱 강도")]
    [SerializeField] [Range(0f, 1f)] private float dangerHapticStrength = 0.6f;

    [Tooltip("초과 햅틱 강도")]
    [SerializeField] [Range(0f, 1f)] private float exceededHapticStrength = 1f;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = true;
    [SerializeField] private bool drawDebugGizmos = true;

    // 안전 위치 기록
    private Queue<SafePositionRecord> leftSafePositions = new Queue<SafePositionRecord>();
    private Queue<SafePositionRecord> rightSafePositions = new Queue<SafePositionRecord>();
    private SafePositionRecord lastLeftSafePosition;
    private SafePositionRecord lastRightSafePosition;

    // 상태
    private float lastSaveTime;
    private bool isLeftRevertActive;
    private bool isRightRevertActive;
    private LimitStatus currentLeftStatus = LimitStatus.Safe;
    private LimitStatus currentRightStatus = LimitStatus.Safe;

    // 되돌리기 진행 상태
    private float leftRevertProgress;
    private float rightRevertProgress;

    // 이벤트
    public event Action<bool, SafePositionRecord> OnRevertStarted;  // isLeft, targetPosition
    public event Action<bool> OnRevertCompleted;  // isLeft
    public event Action<bool, LimitStatus> OnStatusChanged;  // isLeft, newStatus

    /// <summary>
    /// 안전 위치 기록
    /// </summary>
    [System.Serializable]
    public class SafePositionRecord
    {
        public float timestamp;
        public Vector3 handPosition;
        public Quaternion handRotation;
        public Dictionary<int, JointPose> jointPoses = new Dictionary<int, JointPose>();

        [System.Serializable]
        public class JointPose
        {
            public Vector3 localPosition;
            public Quaternion localRotation;
        }
    }

    void Awake()
    {
        lastLeftSafePosition = new SafePositionRecord();
        lastRightSafePosition = new SafePositionRecord();
    }

    void Start()
    {
        FindHandReferences();
        InitializeSafePositions();
    }

    void Update()
    {
        // 안전 위치 저장
        if (Time.time - lastSaveTime >= safePositionSaveInterval)
        {
            lastSaveTime = Time.time;
            TrySaveSafePositions();
        }

        // 되돌리기 가이드 업데이트
        if (enableRevertGuide)
        {
            UpdateRevertGuide();
        }
    }

    /// <summary>
    /// 손 참조 자동 탐색
    /// </summary>
    private void FindHandReferences()
    {
        if (playerLeftHand == null)
        {
            var hands = FindObjectsOfType<HandVisual>();
            foreach (var hand in hands)
            {
                if (hand.Hand != null && hand.Hand.Handedness == Handedness.Left)
                {
                    playerLeftHand = hand;
                    break;
                }
            }
        }

        if (playerRightHand == null)
        {
            var hands = FindObjectsOfType<HandVisual>();
            foreach (var hand in hands)
            {
                if (hand.Hand != null && hand.Hand.Handedness == Handedness.Right)
                {
                    playerRightHand = hand;
                    break;
                }
            }
        }

        // 손 루트 찾기
        if (leftHandRoot == null && playerLeftHand != null)
        {
            leftHandRoot = FindHandRoot(playerLeftHand.transform);
        }
        if (rightHandRoot == null && playerRightHand != null)
        {
            rightHandRoot = FindHandRoot(playerRightHand.transform);
        }
    }

    private Transform FindHandRoot(Transform start)
    {
        Transform current = start.parent;
        while (current != null)
        {
            if (current.name.Contains("OpenXR") || current.name.Contains("Hand"))
            {
                return current;
            }
            current = current.parent;
        }
        return start;
    }

    /// <summary>
    /// 초기 안전 위치 저장
    /// </summary>
    private void InitializeSafePositions()
    {
        SaveCurrentPositionAsSafe(true);
        SaveCurrentPositionAsSafe(false);

        if (showDebugLogs)
            Debug.Log("<color=green>[SafePositionManager] 초기 안전 위치 저장됨</color>");
    }

    /// <summary>
    /// 안전 위치 저장 시도 (현재 상태가 안전할 때만)
    /// </summary>
    private void TrySaveSafePositions()
    {
        if (currentLeftStatus == LimitStatus.Safe)
        {
            SaveCurrentPositionAsSafe(true);
        }

        if (currentRightStatus == LimitStatus.Safe)
        {
            SaveCurrentPositionAsSafe(false);
        }
    }

    /// <summary>
    /// 현재 위치를 안전 위치로 저장
    /// </summary>
    private void SaveCurrentPositionAsSafe(bool isLeftHand)
    {
        HandVisual hand = isLeftHand ? playerLeftHand : playerRightHand;
        Transform root = isLeftHand ? leftHandRoot : rightHandRoot;
        Queue<SafePositionRecord> history = isLeftHand ? leftSafePositions : rightSafePositions;

        if (hand == null || hand.Hand == null || !hand.Hand.IsTrackedDataValid)
            return;

        SafePositionRecord record = new SafePositionRecord
        {
            timestamp = Time.time,
            handPosition = root != null ? root.position : Vector3.zero,
            handRotation = root != null ? root.rotation : Quaternion.identity,
            jointPoses = new Dictionary<int, SafePositionRecord.JointPose>()
        };

        // 관절 포즈 저장
        if (hand.Joints != null)
        {
            for (int i = 0; i < hand.Joints.Count; i++)
            {
                if (hand.Joints[i] != null)
                {
                    record.jointPoses[i] = new SafePositionRecord.JointPose
                    {
                        localPosition = hand.Joints[i].localPosition,
                        localRotation = hand.Joints[i].localRotation
                    };
                }
            }
        }

        // 기록 추가
        history.Enqueue(record);
        while (history.Count > maxSafePositionHistory)
        {
            history.Dequeue();
        }

        // 마지막 안전 위치 업데이트
        if (isLeftHand)
            lastLeftSafePosition = record;
        else
            lastRightSafePosition = record;
    }

    /// <summary>
    /// 한계 상태 업데이트 (ChunaLimitChecker에서 호출)
    /// </summary>
    public void UpdateLimitStatus(bool isLeftHand, LimitStatus status)
    {
        LimitStatus previousStatus = isLeftHand ? currentLeftStatus : currentRightStatus;

        if (isLeftHand)
            currentLeftStatus = status;
        else
            currentRightStatus = status;

        // 상태 변경 이벤트
        if (status != previousStatus)
        {
            OnStatusChanged?.Invoke(isLeftHand, status);

            // 시각적 피드백 업데이트
            UpdateVisualFeedback(isLeftHand, status);

            // 햅틱 피드백
            if (enableHapticFeedback)
            {
                TriggerHapticFeedback(isLeftHand, status);
            }
        }

        // 한계 초과 시 되돌리기 시작
        if (status == LimitStatus.Exceeded)
        {
            StartRevert(isLeftHand);
        }
        else if (status == LimitStatus.Safe)
        {
            // 안전 상태로 돌아오면 되돌리기 종료
            if (isLeftHand && isLeftRevertActive)
            {
                CompleteRevert(true);
            }
            else if (!isLeftHand && isRightRevertActive)
            {
                CompleteRevert(false);
            }
        }
    }

    /// <summary>
    /// 되돌리기 시작
    /// </summary>
    public void StartRevert(bool isLeftHand)
    {
        if (isLeftHand)
        {
            if (isLeftRevertActive) return;
            isLeftRevertActive = true;
            leftRevertProgress = 0f;

            if (showDebugLogs)
                Debug.Log("<color=yellow>[SafePositionManager] 왼손 되돌리기 시작</color>");

            OnRevertStarted?.Invoke(true, lastLeftSafePosition);
        }
        else
        {
            if (isRightRevertActive) return;
            isRightRevertActive = true;
            rightRevertProgress = 0f;

            if (showDebugLogs)
                Debug.Log("<color=yellow>[SafePositionManager] 오른손 되돌리기 시작</color>");

            OnRevertStarted?.Invoke(false, lastRightSafePosition);
        }

        // 가이드 손 표시
        ShowGuideHand(isLeftHand, true);
    }

    /// <summary>
    /// 되돌리기 완료
    /// </summary>
    private void CompleteRevert(bool isLeftHand)
    {
        if (isLeftHand)
        {
            isLeftRevertActive = false;
            leftRevertProgress = 0f;
        }
        else
        {
            isRightRevertActive = false;
            rightRevertProgress = 0f;
        }

        // 가이드 손 숨기기
        ShowGuideHand(isLeftHand, false);

        if (showDebugLogs)
        {
            string handName = isLeftHand ? "왼손" : "오른손";
            Debug.Log($"<color=green>[SafePositionManager] {handName} 되돌리기 완료</color>");
        }

        OnRevertCompleted?.Invoke(isLeftHand);
    }

    /// <summary>
    /// 되돌리기 가이드 업데이트
    /// </summary>
    private void UpdateRevertGuide()
    {
        float speed = limitData != null ? limitData.RevertLerpSpeed : revertLerpSpeed;

        // 왼손 되돌리기 가이드
        if (isLeftRevertActive && leftGuideHand != null && lastLeftSafePosition != null)
        {
            leftRevertProgress += Time.deltaTime * speed;

            // 가이드 손을 안전 위치로 이동
            if (leftGuideHand.Root != null)
            {
                leftGuideHand.Root.position = Vector3.Lerp(
                    leftGuideHand.Root.position,
                    lastLeftSafePosition.handPosition,
                    leftRevertProgress
                );
                leftGuideHand.Root.rotation = Quaternion.Slerp(
                    leftGuideHand.Root.rotation,
                    lastLeftSafePosition.handRotation,
                    leftRevertProgress
                );
            }

            // 관절 포즈 적용
            foreach (var kvp in lastLeftSafePosition.jointPoses)
            {
                leftGuideHand.SetJointLocalPose(kvp.Key, kvp.Value.localPosition, kvp.Value.localRotation);
            }
        }

        // 오른손 되돌리기 가이드
        if (isRightRevertActive && rightGuideHand != null && lastRightSafePosition != null)
        {
            rightRevertProgress += Time.deltaTime * speed;

            // 가이드 손을 안전 위치로 이동
            if (rightGuideHand.Root != null)
            {
                rightGuideHand.Root.position = Vector3.Lerp(
                    rightGuideHand.Root.position,
                    lastRightSafePosition.handPosition,
                    rightRevertProgress
                );
                rightGuideHand.Root.rotation = Quaternion.Slerp(
                    rightGuideHand.Root.rotation,
                    lastRightSafePosition.handRotation,
                    rightRevertProgress
                );
            }

            // 관절 포즈 적용
            foreach (var kvp in lastRightSafePosition.jointPoses)
            {
                rightGuideHand.SetJointLocalPose(kvp.Key, kvp.Value.localPosition, kvp.Value.localRotation);
            }
        }
    }

    /// <summary>
    /// 시각적 피드백 업데이트
    /// </summary>
    private void UpdateVisualFeedback(bool isLeftHand, LimitStatus status)
    {
        HandTransformMapper guideHand = isLeftHand ? leftGuideHand : rightGuideHand;
        if (guideHand == null) return;

        Color feedbackColor = status switch
        {
            LimitStatus.Warning => warningColor,
            LimitStatus.Danger => dangerColor,
            LimitStatus.Exceeded => exceededColor,
            _ => safePositionColor
        };

        guideHand.SetColorAndAlpha(feedbackColor, feedbackColor.a);
    }

    /// <summary>
    /// 가이드 손 표시/숨기기
    /// </summary>
    private void ShowGuideHand(bool isLeftHand, bool show)
    {
        HandTransformMapper guideHand = isLeftHand ? leftGuideHand : rightGuideHand;
        if (guideHand != null)
        {
            guideHand.SetVisible(show);
            if (show)
            {
                guideHand.SetColorAndAlpha(safePositionColor, safePositionColor.a);
            }
        }
    }

    /// <summary>
    /// 햅틱 피드백 트리거
    /// </summary>
    private void TriggerHapticFeedback(bool isLeftHand, LimitStatus status)
    {
        float strength = status switch
        {
            LimitStatus.Warning => warningHapticStrength,
            LimitStatus.Danger => dangerHapticStrength,
            LimitStatus.Exceeded => exceededHapticStrength,
            _ => 0f
        };

        if (strength <= 0f) return;

        float duration = status switch
        {
            LimitStatus.Warning => 0.1f,
            LimitStatus.Danger => 0.2f,
            LimitStatus.Exceeded => 0.5f,
            _ => 0f
        };

        // OVRInput 햅틱 (Quest용)
        try
        {
            OVRInput.Controller controller = isLeftHand
                ? OVRInput.Controller.LTouch
                : OVRInput.Controller.RTouch;

            OVRInput.SetControllerVibration(strength, strength, controller);

            // 지연 후 진동 정지
            StartCoroutine(StopHapticAfterDelay(controller, duration));
        }
        catch (Exception e)
        {
            if (showDebugLogs)
                Debug.LogWarning($"[SafePositionManager] 햅틱 피드백 실패: {e.Message}");
        }
    }

    private System.Collections.IEnumerator StopHapticAfterDelay(OVRInput.Controller controller, float delay)
    {
        yield return new WaitForSeconds(delay);
        OVRInput.SetControllerVibration(0f, 0f, controller);
    }

    // ========== Public API ==========

    /// <summary>
    /// 한계 데이터 설정
    /// </summary>
    public void SetLimitData(ChunaLimitData data)
    {
        limitData = data;
        if (data != null)
        {
            revertLerpSpeed = data.RevertLerpSpeed;
        }
    }

    /// <summary>
    /// 마지막 안전 위치 가져오기
    /// </summary>
    public SafePositionRecord GetLastSafePosition(bool isLeftHand)
    {
        return isLeftHand ? lastLeftSafePosition : lastRightSafePosition;
    }

    /// <summary>
    /// 안전 위치 히스토리 가져오기
    /// </summary>
    public SafePositionRecord[] GetSafePositionHistory(bool isLeftHand)
    {
        Queue<SafePositionRecord> history = isLeftHand ? leftSafePositions : rightSafePositions;
        return history.ToArray();
    }

    /// <summary>
    /// 되돌리기 활성화 여부
    /// </summary>
    public bool IsRevertActive(bool isLeftHand)
    {
        return isLeftHand ? isLeftRevertActive : isRightRevertActive;
    }

    /// <summary>
    /// 되돌리기 강제 취소
    /// </summary>
    public void CancelRevert(bool isLeftHand)
    {
        if (isLeftHand)
        {
            isLeftRevertActive = false;
            leftRevertProgress = 0f;
        }
        else
        {
            isRightRevertActive = false;
            rightRevertProgress = 0f;
        }

        ShowGuideHand(isLeftHand, false);

        if (showDebugLogs)
        {
            string handName = isLeftHand ? "왼손" : "오른손";
            Debug.Log($"[SafePositionManager] {handName} 되돌리기 취소됨");
        }
    }

    /// <summary>
    /// 현재 위치를 강제로 안전 위치로 저장
    /// </summary>
    public void ForceSaveCurrentPosition(bool isLeftHand)
    {
        SaveCurrentPositionAsSafe(isLeftHand);

        if (showDebugLogs)
        {
            string handName = isLeftHand ? "왼손" : "오른손";
            Debug.Log($"[SafePositionManager] {handName} 현재 위치 강제 저장됨");
        }
    }

    /// <summary>
    /// 모든 안전 위치 기록 클리어
    /// </summary>
    public void ClearHistory()
    {
        leftSafePositions.Clear();
        rightSafePositions.Clear();
        lastLeftSafePosition = new SafePositionRecord();
        lastRightSafePosition = new SafePositionRecord();

        if (showDebugLogs)
            Debug.Log("[SafePositionManager] 안전 위치 기록 클리어됨");
    }

    /// <summary>
    /// 리셋
    /// </summary>
    public void Reset()
    {
        ClearHistory();
        isLeftRevertActive = false;
        isRightRevertActive = false;
        leftRevertProgress = 0f;
        rightRevertProgress = 0f;
        currentLeftStatus = LimitStatus.Safe;
        currentRightStatus = LimitStatus.Safe;

        ShowGuideHand(true, false);
        ShowGuideHand(false, false);

        InitializeSafePositions();
    }

    /// <summary>
    /// 사용자의 손이 안전 위치 근처에 있는지 확인
    /// </summary>
    public bool IsNearSafePosition(bool isLeftHand, float threshold = 0.05f)
    {
        SafePositionRecord safePos = isLeftHand ? lastLeftSafePosition : lastRightSafePosition;
        Transform handRoot = isLeftHand ? leftHandRoot : rightHandRoot;

        if (safePos == null || handRoot == null)
            return false;

        float distance = Vector3.Distance(handRoot.position, safePos.handPosition);
        return distance <= threshold;
    }

    /// <summary>
    /// 안전 위치까지의 거리 가져오기
    /// </summary>
    public float GetDistanceToSafePosition(bool isLeftHand)
    {
        SafePositionRecord safePos = isLeftHand ? lastLeftSafePosition : lastRightSafePosition;
        Transform handRoot = isLeftHand ? leftHandRoot : rightHandRoot;

        if (safePos == null || handRoot == null)
            return float.MaxValue;

        return Vector3.Distance(handRoot.position, safePos.handPosition);
    }

    void OnDrawGizmos()
    {
        if (!drawDebugGizmos)
            return;

        // 왼손 안전 위치 표시
        if (lastLeftSafePosition != null && lastLeftSafePosition.handPosition != Vector3.zero)
        {
            Gizmos.color = safePositionColor;
            Gizmos.DrawWireSphere(lastLeftSafePosition.handPosition, 0.03f);

            if (leftHandRoot != null)
            {
                Gizmos.color = currentLeftStatus == LimitStatus.Safe ? Color.green : Color.red;
                Gizmos.DrawLine(leftHandRoot.position, lastLeftSafePosition.handPosition);
            }
        }

        // 오른손 안전 위치 표시
        if (lastRightSafePosition != null && lastRightSafePosition.handPosition != Vector3.zero)
        {
            Gizmos.color = safePositionColor;
            Gizmos.DrawWireSphere(lastRightSafePosition.handPosition, 0.03f);

            if (rightHandRoot != null)
            {
                Gizmos.color = currentRightStatus == LimitStatus.Safe ? Color.green : Color.red;
                Gizmos.DrawLine(rightHandRoot.position, lastRightSafePosition.handPosition);
            }
        }
    }
}
