using System;
using System.Collections.Generic;
using UnityEngine;
using Oculus.Interaction.Input;
using Oculus.Interaction;

/// <summary>
/// 추나 시술 한계 위반 감지 시스템
/// 실시간으로 관절 각도와 위치를 모니터링하고 한계 초과를 감지
/// </summary>
public class ChunaLimitChecker : MonoBehaviour
{
    [Header("=== 한계 데이터 ===")]
    [SerializeField] private ChunaLimitData limitData;

    [Header("=== 손 참조 ===")]
    [SerializeField] private HandVisual playerLeftHand;
    [SerializeField] private HandVisual playerRightHand;
    [SerializeField] private Transform leftHandRoot;
    [SerializeField] private Transform rightHandRoot;

    [Header("=== 기준점 ===")]
    [Tooltip("시작 위치 기준점 (환자 위치)")]
    [SerializeField] private Transform referencePoint;

    [Header("=== 체크 설정 ===")]
    [Tooltip("체크 간격 (초)")]
    [SerializeField] private float checkInterval = 0.1f;

    [Tooltip("체크 활성화")]
    [SerializeField] private bool enableChecking = true;

    [Tooltip("왼손(보조수) 제한 체크 활성화 - 비활성화하면 왼손은 항상 Safe")]
    [SerializeField] private bool enableLeftHandCheck = false;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = true;
    [SerializeField] private bool drawDebugGizmos = true;

    // 상태
    private float lastCheckTime;
    private bool isInitialized;

    // 시작 위치/회전 저장
    private Vector3 leftHandStartPosition;
    private Quaternion leftHandStartRotation;
    private Vector3 rightHandStartPosition;
    private Quaternion rightHandStartRotation;
    private Dictionary<int, Quaternion> leftJointStartRotations = new Dictionary<int, Quaternion>();
    private Dictionary<int, Quaternion> rightJointStartRotations = new Dictionary<int, Quaternion>();

    // 현재 상태
    private LimitCheckResult currentLeftResult = new LimitCheckResult();
    private LimitCheckResult currentRightResult = new LimitCheckResult();

    // 이벤트
    public event Action<ViolationEvent> OnViolationDetected;
    public event Action<LimitCheckResult, LimitCheckResult> OnLimitStatusChanged;
    public event Action<bool> OnRevertRequired;  // true = left, false = right

    /// <summary>
    /// 한계 체크 결과
    /// </summary>
    [System.Serializable]
    public class LimitCheckResult
    {
        public LimitStatus overallStatus = LimitStatus.Safe;
        public float maxLimitRatio = 0f;
        public List<JointViolation> jointViolations = new List<JointViolation>();
        public ViolationType primaryViolationType = ViolationType.None;
        public ViolationSeverity severity = ViolationSeverity.None;
        public Vector3 currentPosition;
        public Quaternion currentRotation;
        public Vector3 positionDelta;
        public Vector3 rotationDelta;
        public float movementSpeed;
        public float rotationSpeed;
    }

    /// <summary>
    /// 관절 위반 정보
    /// </summary>
    [System.Serializable]
    public class JointViolation
    {
        public int jointId;
        public string jointName;
        public ViolationType violationType;
        public float limitRatio;
        public Vector3 currentValue;
        public Vector3 limitValue;
        public LimitStatus status;
    }

    /// <summary>
    /// 위반 이벤트
    /// </summary>
    [System.Serializable]
    public class ViolationEvent
    {
        public float timestamp;
        public bool isLeftHand;
        public ViolationType violationType;
        public ViolationSeverity severity;
        public int jointId;
        public string jointName;
        public float limitRatio;
        public Vector3 violationValue;
        public Vector3 limitValue;
    }

    void Awake()
    {
        if (limitData == null)
        {
            Debug.LogWarning("[ChunaLimitChecker] ChunaLimitData가 설정되지 않았습니다.");
        }
    }

    void Start()
    {
        FindHandReferences();
    }

    void Update()
    {
        if (!enableChecking || !isInitialized || limitData == null)
            return;

        if (Time.time - lastCheckTime >= checkInterval)
        {
            lastCheckTime = Time.time;
            PerformLimitCheck();
        }
    }

    /// <summary>
    /// 손 참조 자동 탐색
    /// </summary>
    private void FindHandReferences()
    {
        if (playerLeftHand == null)
        {
            var leftHands = FindObjectsOfType<HandVisual>();
            foreach (var hand in leftHands)
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
            var rightHands = FindObjectsOfType<HandVisual>();
            foreach (var hand in rightHands)
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
    /// 한계 체크 초기화 - 현재 위치를 시작점으로 설정
    /// </summary>
    public void Initialize()
    {
        if (playerLeftHand != null && playerLeftHand.Hand != null && playerLeftHand.Hand.IsTrackedDataValid)
        {
            leftHandStartPosition = leftHandRoot != null ? leftHandRoot.position : Vector3.zero;
            leftHandStartRotation = leftHandRoot != null ? leftHandRoot.rotation : Quaternion.identity;

            // 관절별 시작 회전 저장
            leftJointStartRotations.Clear();
            if (playerLeftHand.Joints != null)
            {
                for (int i = 0; i < playerLeftHand.Joints.Count; i++)
                {
                    if (playerLeftHand.Joints[i] != null)
                    {
                        leftJointStartRotations[i] = playerLeftHand.Joints[i].localRotation;
                    }
                }
            }
        }

        if (playerRightHand != null && playerRightHand.Hand != null && playerRightHand.Hand.IsTrackedDataValid)
        {
            rightHandStartPosition = rightHandRoot != null ? rightHandRoot.position : Vector3.zero;
            rightHandStartRotation = rightHandRoot != null ? rightHandRoot.rotation : Quaternion.identity;

            // 관절별 시작 회전 저장
            rightJointStartRotations.Clear();
            if (playerRightHand.Joints != null)
            {
                for (int i = 0; i < playerRightHand.Joints.Count; i++)
                {
                    if (playerRightHand.Joints[i] != null)
                    {
                        rightJointStartRotations[i] = playerRightHand.Joints[i].localRotation;
                    }
                }
            }
        }

        isInitialized = true;
        lastCheckTime = Time.time;

        if (showDebugLogs)
            Debug.Log("<color=green>[ChunaLimitChecker] 초기화 완료 - 시작 위치 저장됨</color>");
    }

    /// <summary>
    /// 한계 체크 수행
    /// </summary>
    private void PerformLimitCheck()
    {
        LimitCheckResult previousLeftResult = currentLeftResult;
        LimitCheckResult previousRightResult = currentRightResult;

        // 왼손 체크 (비활성화 시 항상 Safe)
        if (enableLeftHandCheck)
        {
            currentLeftResult = CheckHandLimits(playerLeftHand, leftHandRoot,
                leftHandStartPosition, leftHandStartRotation, leftJointStartRotations, true);
        }
        else
        {
            // 왼손 체크 비활성화 - 항상 Safe 상태
            currentLeftResult = new LimitCheckResult { overallStatus = LimitStatus.Safe };
        }

        // 오른손 체크
        currentRightResult = CheckHandLimits(playerRightHand, rightHandRoot,
            rightHandStartPosition, rightHandStartRotation, rightJointStartRotations, false);

        // 상태 변경 이벤트
        if (HasStatusChanged(previousLeftResult, currentLeftResult) ||
            HasStatusChanged(previousRightResult, currentRightResult))
        {
            OnLimitStatusChanged?.Invoke(currentLeftResult, currentRightResult);
        }

        // 위반 감지 및 이벤트 발생 (왼손은 체크 활성화 시에만)
        if (enableLeftHandCheck)
        {
            ProcessViolations(currentLeftResult, true);
        }
        ProcessViolations(currentRightResult, false);

        // 되돌리기 필요 여부 체크
        if (limitData.EnableAutoRevert)
        {
            if (enableLeftHandCheck && currentLeftResult.overallStatus == LimitStatus.Exceeded)
            {
                OnRevertRequired?.Invoke(true);
            }
            if (currentRightResult.overallStatus == LimitStatus.Exceeded)
            {
                OnRevertRequired?.Invoke(false);
            }
        }
    }

    /// <summary>
    /// 손 한계 체크
    /// </summary>
    private LimitCheckResult CheckHandLimits(
        HandVisual hand,
        Transform handRoot,
        Vector3 startPosition,
        Quaternion startRotation,
        Dictionary<int, Quaternion> jointStartRotations,
        bool isLeftHand)
    {
        LimitCheckResult result = new LimitCheckResult();
        result.jointViolations = new List<JointViolation>();

        if (hand == null || hand.Hand == null || !hand.Hand.IsTrackedDataValid)
            return result;

        // 현재 위치/회전
        Vector3 currentPosition = handRoot != null ? handRoot.position : Vector3.zero;
        Quaternion currentRotation = handRoot != null ? handRoot.rotation : Quaternion.identity;

        result.currentPosition = currentPosition;
        result.currentRotation = currentRotation;

        // 위치 변화량 계산
        Vector3 basePosition = referencePoint != null ? referencePoint.position : startPosition;
        result.positionDelta = currentPosition - basePosition;

        // 회전 변화량 계산 (오일러 각도로 변환)
        Quaternion rotationDelta = Quaternion.Inverse(startRotation) * currentRotation;
        result.rotationDelta = rotationDelta.eulerAngles;
        // 각도를 -180 ~ 180 범위로 정규화
        result.rotationDelta = NormalizeEulerAngles(result.rotationDelta);

        float maxRatio = 0f;
        ViolationType primaryViolation = ViolationType.None;

        // 위치 한계 체크
        float positionRatio = CheckPositionLimits(result.positionDelta, out ViolationType posViolation);
        if (positionRatio > maxRatio)
        {
            maxRatio = positionRatio;
            primaryViolation = posViolation;
        }

        // 회전 한계 체크 (전체 손)
        float rotationRatio = CheckRotationLimits(result.rotationDelta, out ViolationType rotViolation);
        if (rotationRatio > maxRatio)
        {
            maxRatio = rotationRatio;
            primaryViolation = rotViolation;
        }

        // 관절별 한계 체크
        if (hand.Joints != null && limitData.JointLimits != null)
        {
            foreach (var jointLimit in limitData.JointLimits)
            {
                if (jointLimit.jointId < hand.Joints.Count && hand.Joints[jointLimit.jointId] != null)
                {
                    Transform joint = hand.Joints[jointLimit.jointId];

                    // 시작 회전 대비 변화량 계산
                    Quaternion jointStartRot = jointStartRotations.ContainsKey(jointLimit.jointId)
                        ? jointStartRotations[jointLimit.jointId]
                        : Quaternion.identity;
                    Quaternion jointDelta = Quaternion.Inverse(jointStartRot) * joint.localRotation;
                    Vector3 jointEuler = NormalizeEulerAngles(jointDelta.eulerAngles);

                    // 관절 회전 한계 체크
                    float jointRatio = jointLimit.GetRotationLimitRatio(jointEuler);

                    if (jointRatio > 0)
                    {
                        JointViolation jv = new JointViolation
                        {
                            jointId = jointLimit.jointId,
                            jointName = jointLimit.jointName,
                            limitRatio = jointRatio,
                            currentValue = jointEuler,
                            limitValue = new Vector3(jointLimit.maxRotationX, jointLimit.maxRotationY, jointLimit.maxRotationZ),
                            status = limitData.GetLimitStatus(jointRatio)
                        };

                        // 위반 타입 결정
                        if (Mathf.Abs(jointEuler.x) > Mathf.Abs(jointEuler.y) && Mathf.Abs(jointEuler.x) > Mathf.Abs(jointEuler.z))
                        {
                            jv.violationType = jointEuler.x > 0 ? ViolationType.OverFlexion : ViolationType.OverExtension;
                        }
                        else if (Mathf.Abs(jointEuler.y) > Mathf.Abs(jointEuler.z))
                        {
                            jv.violationType = ViolationType.OverRotation;
                        }
                        else
                        {
                            jv.violationType = ViolationType.OverLateralFlexion;
                        }

                        result.jointViolations.Add(jv);

                        // 가중치 적용
                        float weightedRatio = jointRatio * jointLimit.weight;
                        if (weightedRatio > maxRatio)
                        {
                            maxRatio = weightedRatio;
                            primaryViolation = jv.violationType;
                        }

                        // 크리티컬 관절 체크
                        if (jointLimit.criticalJoint && jv.status == LimitStatus.Exceeded)
                        {
                            result.overallStatus = LimitStatus.Exceeded;
                        }
                    }
                }
            }
        }

        // 결과 설정
        result.maxLimitRatio = maxRatio;
        result.primaryViolationType = primaryViolation;

        if (result.overallStatus != LimitStatus.Exceeded)
        {
            result.overallStatus = limitData.GetLimitStatus(maxRatio);
        }

        result.severity = GetSeverityFromRatio(maxRatio);

        // 디버그 로그
        if (showDebugLogs && result.overallStatus != LimitStatus.Safe)
        {
            string handName = isLeftHand ? "왼손" : "오른손";
            Debug.Log($"<color=yellow>[ChunaLimitChecker] {handName} 상태: {result.overallStatus}, 비율: {maxRatio:P0}, 위반: {primaryViolation}</color>");
        }

        return result;
    }

    /// <summary>
    /// 위치 한계 체크
    /// </summary>
    private float CheckPositionLimits(Vector3 positionDelta, out ViolationType violationType)
    {
        violationType = ViolationType.None;
        float maxRatio = 0f;

        // 전방/후방 체크
        if (positionDelta.z > 0)
        {
            float ratio = positionDelta.z / limitData.MaxHandForwardDistance;
            if (ratio > maxRatio)
            {
                maxRatio = ratio;
                violationType = ViolationType.OverTranslation;
            }
        }
        else if (positionDelta.z < 0)
        {
            float ratio = Mathf.Abs(positionDelta.z) / limitData.MaxHandBackwardDistance;
            if (ratio > maxRatio)
            {
                maxRatio = ratio;
                violationType = ViolationType.OverTranslation;
            }
        }

        // 측면 체크
        float lateralRatio = Mathf.Abs(positionDelta.x) / limitData.MaxHandLateralDistance;
        if (lateralRatio > maxRatio)
        {
            maxRatio = lateralRatio;
            violationType = ViolationType.OverTranslation;
        }

        // 상하 체크
        float verticalRatio = Mathf.Abs(positionDelta.y) / limitData.MaxHandVerticalDistance;
        if (verticalRatio > maxRatio)
        {
            maxRatio = verticalRatio;
            violationType = ViolationType.OverTranslation;
        }

        return maxRatio;
    }

    /// <summary>
    /// 회전 한계 체크
    /// </summary>
    private float CheckRotationLimits(Vector3 rotationDelta, out ViolationType violationType)
    {
        violationType = ViolationType.None;
        float maxRatio = 0f;

        // 굴곡/신전 (X축)
        if (rotationDelta.x > 0)
        {
            float ratio = rotationDelta.x / limitData.MaxWristFlexion;
            if (ratio > maxRatio)
            {
                maxRatio = ratio;
                violationType = ViolationType.OverFlexion;
            }
        }
        else if (rotationDelta.x < 0)
        {
            float ratio = Mathf.Abs(rotationDelta.x) / limitData.MaxWristExtension;
            if (ratio > maxRatio)
            {
                maxRatio = ratio;
                violationType = ViolationType.OverExtension;
            }
        }

        // 회전 (Y축)
        float yRatio = Mathf.Abs(rotationDelta.y) / Mathf.Max(limitData.MaxWristPronation, limitData.MaxWristSupination);
        if (yRatio > maxRatio)
        {
            maxRatio = yRatio;
            violationType = ViolationType.OverRotation;
        }

        // 측굴 (Z축)
        float zRatio = Mathf.Abs(rotationDelta.z) / Mathf.Max(limitData.MaxWristRadialDeviation, limitData.MaxWristUlnarDeviation);
        if (zRatio > maxRatio)
        {
            maxRatio = zRatio;
            violationType = ViolationType.OverLateralFlexion;
        }

        return maxRatio;
    }

    /// <summary>
    /// 비율에서 심각도 계산
    /// </summary>
    private ViolationSeverity GetSeverityFromRatio(float ratio)
    {
        if (ratio >= 1.1f)
            return ViolationSeverity.Dangerous;
        if (ratio >= 1.0f)
            return ViolationSeverity.Severe;
        if (ratio >= limitData.DangerThresholdRatio)
            return ViolationSeverity.Moderate;
        if (ratio >= limitData.WarningThresholdRatio)
            return ViolationSeverity.Minor;
        return ViolationSeverity.None;
    }

    /// <summary>
    /// 위반 처리 및 이벤트 발생
    /// </summary>
    private void ProcessViolations(LimitCheckResult result, bool isLeftHand)
    {
        if (result.severity == ViolationSeverity.None)
            return;

        ViolationEvent evt = new ViolationEvent
        {
            timestamp = Time.time,
            isLeftHand = isLeftHand,
            violationType = result.primaryViolationType,
            severity = result.severity,
            limitRatio = result.maxLimitRatio
        };

        // 가장 심각한 관절 위반 찾기
        if (result.jointViolations.Count > 0)
        {
            JointViolation worst = result.jointViolations[0];
            foreach (var jv in result.jointViolations)
            {
                if (jv.limitRatio > worst.limitRatio)
                    worst = jv;
            }

            evt.jointId = worst.jointId;
            evt.jointName = worst.jointName;
            evt.violationValue = worst.currentValue;
            evt.limitValue = worst.limitValue;
        }

        OnViolationDetected?.Invoke(evt);
    }

    /// <summary>
    /// 상태 변경 여부 확인
    /// </summary>
    private bool HasStatusChanged(LimitCheckResult prev, LimitCheckResult curr)
    {
        return prev.overallStatus != curr.overallStatus ||
               prev.severity != curr.severity;
    }

    /// <summary>
    /// 오일러 각도를 -180 ~ 180 범위로 정규화
    /// </summary>
    private Vector3 NormalizeEulerAngles(Vector3 euler)
    {
        return new Vector3(
            NormalizeAngle(euler.x),
            NormalizeAngle(euler.y),
            NormalizeAngle(euler.z)
        );
    }

    private float NormalizeAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }

    // ========== Public API ==========

    /// <summary>
    /// 한계 데이터 설정
    /// </summary>
    public void SetLimitData(ChunaLimitData data)
    {
        limitData = data;
        if (showDebugLogs)
            Debug.Log($"[ChunaLimitChecker] 한계 데이터 설정: {data?.ProcedureName ?? "null"}");
    }

    /// <summary>
    /// 체크 활성화/비활성화
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        enableChecking = enabled;
    }

    /// <summary>
    /// 현재 왼손 결과 가져오기
    /// </summary>
    public LimitCheckResult GetLeftHandResult()
    {
        return currentLeftResult;
    }

    /// <summary>
    /// 현재 오른손 결과 가져오기
    /// </summary>
    public LimitCheckResult GetRightHandResult()
    {
        return currentRightResult;
    }

    /// <summary>
    /// 특정 손이 안전한지 확인
    /// </summary>
    public bool IsHandSafe(bool isLeftHand)
    {
        LimitCheckResult result = isLeftHand ? currentLeftResult : currentRightResult;
        return result.overallStatus == LimitStatus.Safe;
    }

    /// <summary>
    /// 양손 모두 안전한지 확인
    /// </summary>
    public bool AreBothHandsSafe()
    {
        return currentLeftResult.overallStatus == LimitStatus.Safe &&
               currentRightResult.overallStatus == LimitStatus.Safe;
    }

    /// <summary>
    /// 리셋 (시작 위치 재설정)
    /// </summary>
    public void Reset()
    {
        isInitialized = false;
        currentLeftResult = new LimitCheckResult();
        currentRightResult = new LimitCheckResult();
        leftJointStartRotations.Clear();
        rightJointStartRotations.Clear();

        if (showDebugLogs)
            Debug.Log("[ChunaLimitChecker] 리셋됨");
    }

    void OnDrawGizmos()
    {
        if (!drawDebugGizmos || !isInitialized)
            return;

        // 왼손 상태 표시
        if (leftHandRoot != null)
        {
            Gizmos.color = GetColorForStatus(currentLeftResult.overallStatus);
            Gizmos.DrawWireSphere(leftHandRoot.position, 0.05f);
        }

        // 오른손 상태 표시
        if (rightHandRoot != null)
        {
            Gizmos.color = GetColorForStatus(currentRightResult.overallStatus);
            Gizmos.DrawWireSphere(rightHandRoot.position, 0.05f);
        }

        // 기준점 표시
        if (referencePoint != null)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawWireCube(referencePoint.position, Vector3.one * 0.1f);
        }
    }

    private Color GetColorForStatus(LimitStatus status)
    {
        switch (status)
        {
            case LimitStatus.Safe:
                return Color.green;
            case LimitStatus.Warning:
                return Color.yellow;
            case LimitStatus.Danger:
                return new Color(1f, 0.5f, 0f); // Orange
            case LimitStatus.Exceeded:
                return Color.red;
            default:
                return Color.gray;
        }
    }
}
