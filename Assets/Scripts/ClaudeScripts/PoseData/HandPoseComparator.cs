using System;
using System.Collections.Generic;
using UnityEngine;
using Oculus.Interaction.Input;
using Oculus.Interaction;
using static HandPoseDataLoader;

/// <summary>
/// HandPose 비교기
/// 플레이어의 실시간 손 포즈와 가이드 포즈를 비교
///
/// 기능:
/// - 조인트별 로컬 포즈 비교 (위치 + 회전)
/// - 손 전체 월드 위치/회전 비교
/// - 유사도 계산 및 합격/불합격 판정
/// - 사용자 진행 추적
///
/// 사용법:
/// var comparator = new HandPoseComparator();
/// comparator.SetThresholds(0.05f, 15f, 0.7f);
/// var result = comparator.Compare(playerHand, guideFrame);
/// </summary>
public class HandPoseComparator
{
    /// <summary>
    /// 유사도 비교 결과
    /// </summary>
    public struct SimilarityResult
    {
        public float leftHandSimilarity;
        public float rightHandSimilarity;
        public bool leftHandPassed;
        public bool rightHandPassed;
        public bool overallPassed;
        public float leftHandPositionError;
        public float rightHandPositionError;
        public float leftHandRotationError;
        public float rightHandRotationError;
        public bool leftHandPositionPassed;
        public bool rightHandPositionPassed;
    }

    /// <summary>
    /// 비교 설정
    /// </summary>
    [System.Serializable]
    public class ComparisonSettings
    {
        [Header("조인트 비교 임계값")]
        public float positionThreshold = 0.05f;     // 5cm
        public float rotationThreshold = 15f;        // 15도
        public float similarityPercentage = 0.7f;    // 70%

        [Header("손 전체 비교 설정")]
        public bool compareHandPosition = true;
        public float handPositionThreshold = 0.1f;   // 10cm
        public bool compareHandRotation = true;
        public float handRotationThreshold = 20f;    // 20도

        [Header("연속 프레임 검증 (노이즈 필터링)")]
        [Tooltip("통과로 인정하기 위해 필요한 연속 프레임 수 (1 = 즉시 통과, 3 = 3프레임 연속)")]
        public int consecutiveFramesRequired = 3;

        [Header("관절별 가중치 (중요도)")]
        [Tooltip("손목 관절 가중치 (가장 중요)")]
        public float wristWeight = 2.0f;
        [Tooltip("손가락 끝 관절 가중치")]
        public float fingerTipWeight = 1.5f;
        [Tooltip("기타 관절 가중치")]
        public float otherJointWeight = 1.0f;

        [Header("적응형 임계값 (관절별 조정)")]
        [Tooltip("손목은 위치 변화가 크므로 임계값 완화")]
        public float wristPositionMultiplier = 1.5f;  // 7.5cm
        [Tooltip("손가락 끝은 회전 변화가 크므로 임계값 완화")]
        public float fingerTipRotationMultiplier = 1.5f; // 22.5도

        [Header("유사도 통합 가중치")]
        [Tooltip("조인트 포즈 가중치 (0~1)")]
        public float jointSimilarityWeight = 0.4f;
        [Tooltip("손목 위치 가중치 (0~1)")]
        public float handPositionWeight = 0.4f;
        [Tooltip("손목 회전 가중치 (0~1)")]
        public float handRotationWeight = 0.2f;

        [Header("디버그")]
        [Tooltip("실패한 관절 상세 로그 출력")]
        public bool showDetailedLogs = false;

        [Header("비교할 주요 조인트")]
        public List<HandJointId> keyJoints = new List<HandJointId>()
        {
            // 손목
            HandJointId.HandWristRoot,

            // 엄지
            HandJointId.HandThumb1,
            HandJointId.HandThumb2,
            HandJointId.HandThumb3,

            // 검지
            HandJointId.HandIndex1,
            HandJointId.HandIndex2,
            HandJointId.HandIndex3,

            // 중지
            HandJointId.HandMiddle1,
            HandJointId.HandMiddle2,
            HandJointId.HandMiddle3,

            // 약지
            HandJointId.HandRing1,
            HandJointId.HandRing2,
            HandJointId.HandRing3,

            // 새끼
            HandJointId.HandPinky1,
            HandJointId.HandPinky2,
            HandJointId.HandPinky3
        };
    }

    // 설정
    private ComparisonSettings settings = new ComparisonSettings();

    // 기준점 (옵션)
    private Transform referencePoint;

    // OpenXRRoot Transforms
    private Transform leftOpenXRRoot;
    private Transform rightOpenXRRoot;

    // 디버그용 저장
    private Vector3 leftReplayTargetPosition;
    private Vector3 rightReplayTargetPosition;
    private Vector3 leftPlayerCurrentPosition;
    private Vector3 rightPlayerCurrentPosition;

    // 연속 프레임 검증용
    private int leftConsecutiveSuccessCount = 0;
    private int rightConsecutiveSuccessCount = 0;

    /// <summary>
    /// 설정 초기화
    /// </summary>
    public HandPoseComparator()
    {
        settings = new ComparisonSettings();
    }

    /// <summary>
    /// 임계값 설정
    /// </summary>
    public void SetThresholds(float posThreshold, float rotThreshold, float simPercentage)
    {
        settings.positionThreshold = posThreshold;
        settings.rotationThreshold = rotThreshold;
        settings.similarityPercentage = simPercentage;
    }

    /// <summary>
    /// 손 전체 비교 설정
    /// </summary>
    public void SetHandComparisonSettings(bool comparePos, float posThreshold, bool compareRot, float rotThreshold)
    {
        settings.compareHandPosition = comparePos;
        settings.handPositionThreshold = posThreshold;
        settings.compareHandRotation = compareRot;
        settings.handRotationThreshold = rotThreshold;
    }

    /// <summary>
    /// 기준점 설정
    /// </summary>
    public void SetReferencePoint(Transform reference)
    {
        referencePoint = reference;
    }

    /// <summary>
    /// OpenXRRoot 설정
    /// </summary>
    public void SetOpenXRRoots(Transform leftRoot, Transform rightRoot)
    {
        leftOpenXRRoot = leftRoot;
        rightOpenXRRoot = rightRoot;
    }

    /// <summary>
    /// 왼손 포즈 비교 (연속 프레임 검증 추가, 손목 위치/회전 유사도 통합)
    /// </summary>
    public SimilarityResult CompareLeftPose(HandVisual playerLeftHand, PoseFrame guideFrame, int currentFrameIndex = 0)
    {
        SimilarityResult result = new SimilarityResult();

        if (playerLeftHand == null || guideFrame == null)
        {
            leftConsecutiveSuccessCount = 0; // 실패 시 리셋
            return result;
        }

        if (playerLeftHand.Hand == null || !playerLeftHand.Hand.IsTrackedDataValid)
        {
            leftConsecutiveSuccessCount = 0; // 트래킹 실패 시 리셋
            return result;
        }

        // 손목 위치 검증 (원점 근처면 트래킹 실패로 간주)
        if (playerLeftHand.Joints != null && playerLeftHand.Joints.Count > 0)
        {
            Transform wrist = playerLeftHand.Joints[(int)HandJointId.HandWristRoot];
            if (wrist != null && wrist.position.magnitude < 0.01f)
            {
                leftConsecutiveSuccessCount = 0; // 손목이 원점 근처 (트래킹 실패)
                if (settings.showDetailedLogs && currentFrameIndex % 30 == 0)
                {
                    Debug.LogWarning("[HandPoseComparator] 왼손 손목 위치 이상 (원점 근처)");
                }
                return result;
            }
        }

        // 조인트 유사도 비교
        bool framePassed;
        float jointSimilarity = ComparePose(playerLeftHand, guideFrame.leftLocalPoses, out framePassed, "왼손", currentFrameIndex);

        // 손 전체 위치/회전 비교
        bool positionPassed = true;
        float positionSimilarity = 1f;
        float rotationSimilarity = 1f;

        if (settings.compareHandPosition)
        {
            CompareHandWorldPosition(
                playerLeftHand,
                guideFrame.leftRootPosition,
                guideFrame.leftRootRotation,
                leftOpenXRRoot,
                out result.leftHandPositionError,
                out result.leftHandRotationError,
                out positionPassed,
                out positionSimilarity,
                out rotationSimilarity,
                "왼손",
                currentFrameIndex
            );
            result.leftHandPositionPassed = positionPassed;
        }
        else
        {
            result.leftHandPositionPassed = true;
        }

        // 통합 유사도 계산 (조인트 + 위치 + 회전)
        float totalWeight = settings.jointSimilarityWeight + settings.handPositionWeight + settings.handRotationWeight;
        if (totalWeight > 0)
        {
            result.leftHandSimilarity =
                (jointSimilarity * settings.jointSimilarityWeight +
                 positionSimilarity * settings.handPositionWeight +
                 rotationSimilarity * settings.handRotationWeight) / totalWeight;
        }
        else
        {
            result.leftHandSimilarity = jointSimilarity;
        }

        // 위치 오차가 10cm 이상이면 유사도 강제 하향 (페널티)
        if (result.leftHandPositionError > 0.1f)
        {
            float penalty = Mathf.Clamp01(result.leftHandPositionError / 0.2f); // 0.1m~0.2m 사이에서 페널티
            result.leftHandSimilarity *= (1f - penalty * 0.8f); // 최대 80% 감소

            if (settings.showDetailedLogs && currentFrameIndex % 30 == 0)
            {
                Debug.LogWarning($"[HandPoseComparator] 왼손 위치 오차 큼 ({result.leftHandPositionError:F3}m) - 유사도 페널티 적용: {result.leftHandSimilarity:P0}");
            }
        }

        // 이번 프레임이 통과했는지 확인
        bool currentFrameSuccess = framePassed && positionPassed;

        // 연속 프레임 검증
        if (currentFrameSuccess)
        {
            leftConsecutiveSuccessCount++;
        }
        else
        {
            leftConsecutiveSuccessCount = 0; // 실패 시 카운터 리셋
        }

        // 연속 프레임 조건 만족 확인
        result.leftHandPassed = leftConsecutiveSuccessCount >= settings.consecutiveFramesRequired;
        result.overallPassed = result.leftHandPassed && result.leftHandPositionPassed;

        // 디버그 로그
        if (settings.showDetailedLogs && currentFrameIndex % 10 == 0)
        {
            Debug.Log($"[HandPoseComparator] 왼손 연속 성공: {leftConsecutiveSuccessCount}/{settings.consecutiveFramesRequired} (통합 유사도: {result.leftHandSimilarity:P0}, 조인트:{jointSimilarity:P0}, 위치:{positionSimilarity:P0}, 회전:{rotationSimilarity:P0})");
        }

        return result;
    }

    /// <summary>
    /// 오른손 포즈 비교 (연속 프레임 검증 추가, 손목 위치/회전 유사도 통합)
    /// </summary>
    public SimilarityResult CompareRightPose(HandVisual playerRightHand, PoseFrame guideFrame, int currentFrameIndex = 0)
    {
        SimilarityResult result = new SimilarityResult();

        if (playerRightHand == null || guideFrame == null)
        {
            rightConsecutiveSuccessCount = 0; // 실패 시 리셋
            return result;
        }

        if (playerRightHand.Hand == null || !playerRightHand.Hand.IsTrackedDataValid)
        {
            rightConsecutiveSuccessCount = 0; // 트래킹 실패 시 리셋
            return result;
        }

        // 손목 위치 검증 (원점 근처면 트래킹 실패로 간주)
        if (playerRightHand.Joints != null && playerRightHand.Joints.Count > 0)
        {
            Transform wrist = playerRightHand.Joints[(int)HandJointId.HandWristRoot];
            if (wrist != null && wrist.position.magnitude < 0.01f)
            {
                rightConsecutiveSuccessCount = 0; // 손목이 원점 근처 (트래킹 실패)
                if (settings.showDetailedLogs && currentFrameIndex % 30 == 0)
                {
                    Debug.LogWarning("[HandPoseComparator] 오른손 손목 위치 이상 (원점 근처)");
                }
                return result;
            }
        }

        // 조인트 유사도 비교
        bool framePassed;
        float jointSimilarity = ComparePose(playerRightHand, guideFrame.rightLocalPoses, out framePassed, "오른손", currentFrameIndex);

        // 손 전체 위치/회전 비교
        bool positionPassed = true;
        float positionSimilarity = 1f;
        float rotationSimilarity = 1f;

        if (settings.compareHandPosition)
        {
            CompareHandWorldPosition(
                playerRightHand,
                guideFrame.rightRootPosition,
                guideFrame.rightRootRotation,
                rightOpenXRRoot,
                out result.rightHandPositionError,
                out result.rightHandRotationError,
                out positionPassed,
                out positionSimilarity,
                out rotationSimilarity,
                "오른손",
                currentFrameIndex
            );
            result.rightHandPositionPassed = positionPassed;
        }
        else
        {
            result.rightHandPositionPassed = true;
        }

        // 통합 유사도 계산 (조인트 + 위치 + 회전)
        float totalWeight = settings.jointSimilarityWeight + settings.handPositionWeight + settings.handRotationWeight;
        if (totalWeight > 0)
        {
            result.rightHandSimilarity =
                (jointSimilarity * settings.jointSimilarityWeight +
                 positionSimilarity * settings.handPositionWeight +
                 rotationSimilarity * settings.handRotationWeight) / totalWeight;
        }
        else
        {
            result.rightHandSimilarity = jointSimilarity;
        }

        // 위치 오차가 10cm 이상이면 유사도 강제 하향 (페널티)
        if (result.rightHandPositionError > 0.1f)
        {
            float penalty = Mathf.Clamp01(result.rightHandPositionError / 0.2f); // 0.1m~0.2m 사이에서 페널티
            result.rightHandSimilarity *= (1f - penalty * 0.8f); // 최대 80% 감소

            if (settings.showDetailedLogs && currentFrameIndex % 30 == 0)
            {
                Debug.LogWarning($"[HandPoseComparator] 오른손 위치 오차 큼 ({result.rightHandPositionError:F3}m) - 유사도 페널티 적용: {result.rightHandSimilarity:P0}");
            }
        }

        // 이번 프레임이 통과했는지 확인
        bool currentFrameSuccess = framePassed && positionPassed;

        // 연속 프레임 검증
        if (currentFrameSuccess)
        {
            rightConsecutiveSuccessCount++;
        }
        else
        {
            rightConsecutiveSuccessCount = 0; // 실패 시 카운터 리셋
        }

        // 연속 프레임 조건 만족 확인
        result.rightHandPassed = rightConsecutiveSuccessCount >= settings.consecutiveFramesRequired;
        result.overallPassed = result.rightHandPassed && result.rightHandPositionPassed;

        // 디버그 로그
        if (settings.showDetailedLogs && currentFrameIndex % 10 == 0)
        {
            Debug.Log($"[HandPoseComparator] 오른손 연속 성공: {rightConsecutiveSuccessCount}/{settings.consecutiveFramesRequired} (통합 유사도: {result.rightHandSimilarity:P0}, 조인트:{jointSimilarity:P0}, 위치:{positionSimilarity:P0}, 회전:{rotationSimilarity:P0})");
        }

        return result;
    }

    /// <summary>
    /// 양손 포즈 비교
    /// </summary>
    public SimilarityResult CompareBothHands(HandVisual playerLeftHand, HandVisual playerRightHand, PoseFrame guideFrame, int currentFrameIndex = 0)
    {
        SimilarityResult result = new SimilarityResult();

        // 왼손 비교
        var leftResult = CompareLeftPose(playerLeftHand, guideFrame, currentFrameIndex);
        result.leftHandSimilarity = leftResult.leftHandSimilarity;
        result.leftHandPassed = leftResult.leftHandPassed;
        result.leftHandPositionError = leftResult.leftHandPositionError;
        result.leftHandRotationError = leftResult.leftHandRotationError;
        result.leftHandPositionPassed = leftResult.leftHandPositionPassed;

        // 오른손 비교
        var rightResult = CompareRightPose(playerRightHand, guideFrame, currentFrameIndex);
        result.rightHandSimilarity = rightResult.rightHandSimilarity;
        result.rightHandPassed = rightResult.rightHandPassed;
        result.rightHandPositionError = rightResult.rightHandPositionError;
        result.rightHandRotationError = rightResult.rightHandRotationError;
        result.rightHandPositionPassed = rightResult.rightHandPositionPassed;

        // 전체 합격 여부
        result.overallPassed = result.leftHandPassed && result.rightHandPassed &&
                              result.leftHandPositionPassed && result.rightHandPositionPassed;

        return result;
    }

    /// <summary>
    /// 조인트 포즈 비교 (로컬 좌표, 가중치 및 적응형 임계값 적용)
    /// </summary>
    private float ComparePose(HandVisual playerHand, Dictionary<int, PoseData> guidePoses, out bool passed, string handName, int frameIndex)
    {
        passed = false;

        if (playerHand == null || playerHand.Hand == null || !playerHand.Hand.IsTrackedDataValid)
        {
            return 0f;
        }

        float weightedSimilaritySum = 0f;
        float totalWeight = 0f;

        foreach (HandJointId jointId in settings.keyJoints)
        {
            int jointIndex = (int)jointId;

            if (!guidePoses.ContainsKey(jointIndex))
                continue;

            if (jointIndex >= playerHand.Joints.Count || playerHand.Joints[jointIndex] == null)
                continue;

            Transform playerJoint = playerHand.Joints[jointIndex];
            PoseData guidePose = guidePoses[jointIndex];

            // 관절별 가중치 계산
            float jointWeight = GetJointWeight(jointId);
            totalWeight += jointWeight;

            // 관절별 적응형 임계값 계산
            float posThreshold = GetAdaptivePositionThreshold(jointId);
            float rotThreshold = GetAdaptiveRotationThreshold(jointId);

            float positionDistance = Vector3.Distance(playerJoint.localPosition, guidePose.position);
            float rotationAngle = Quaternion.Angle(playerJoint.localRotation, guidePose.rotation);

            // 이 관절이 통과했는지 확인
            bool jointPassed = positionDistance <= posThreshold && rotationAngle <= rotThreshold;

            if (jointPassed)
            {
                weightedSimilaritySum += jointWeight;
            }

            // 상세 디버그 로그
            if (settings.showDetailedLogs && frameIndex % 30 == 0 && !jointPassed)
            {
                Debug.LogWarning($"[HandPoseComparator] {handName} {jointId} 실패: " +
                    $"위치오차={positionDistance * 100:F1}cm (임계값={posThreshold * 100:F1}cm), " +
                    $"각도오차={rotationAngle:F1}° (임계값={rotThreshold:F1}°)");
            }
        }

        if (totalWeight == 0f)
            return 0f;

        // 가중치 적용된 유사도 계산
        float weightedSimilarity = weightedSimilaritySum / totalWeight;
        passed = weightedSimilarity >= settings.similarityPercentage;

        return weightedSimilarity;
    }

    /// <summary>
    /// 관절별 가중치 반환
    /// </summary>
    private float GetJointWeight(HandJointId jointId)
    {
        // 손목은 가장 중요
        if (jointId == HandJointId.HandWristRoot)
            return settings.wristWeight;

        // 손가락 끝은 중요
        if (jointId == HandJointId.HandThumb3 ||
            jointId == HandJointId.HandIndex3 ||
            jointId == HandJointId.HandMiddle3 ||
            jointId == HandJointId.HandRing3 ||
            jointId == HandJointId.HandPinky3)
            return settings.fingerTipWeight;

        // 기타 관절
        return settings.otherJointWeight;
    }

    /// <summary>
    /// 적응형 위치 임계값 반환
    /// </summary>
    private float GetAdaptivePositionThreshold(HandJointId jointId)
    {
        // 손목은 위치 변화가 크므로 임계값 완화
        if (jointId == HandJointId.HandWristRoot)
            return settings.positionThreshold * settings.wristPositionMultiplier;

        return settings.positionThreshold;
    }

    /// <summary>
    /// 적응형 회전 임계값 반환
    /// </summary>
    private float GetAdaptiveRotationThreshold(HandJointId jointId)
    {
        // 손가락 끝은 회전 변화가 크므로 임계값 완화
        if (jointId == HandJointId.HandThumb3 ||
            jointId == HandJointId.HandIndex3 ||
            jointId == HandJointId.HandMiddle3 ||
            jointId == HandJointId.HandRing3 ||
            jointId == HandJointId.HandPinky3)
            return settings.rotationThreshold * settings.fingerTipRotationMultiplier;

        return settings.rotationThreshold;
    }

    /// <summary>
    /// 손 전체 위치/회전 비교 (월드 좌표) - 유사도 반환
    /// </summary>
    private void CompareHandWorldPosition(
        HandVisual playerHand,
        Vector3 targetRootPosition,
        Quaternion targetRootRotation,
        Transform openXRRoot,
        out float positionError,
        out float rotationError,
        out bool passed,
        out float positionSimilarity,
        out float rotationSimilarity,
        string handName,
        int frameIndex)
    {
        positionError = 0f;
        rotationError = 0f;
        passed = false;
        positionSimilarity = 0f;
        rotationSimilarity = 0f;

        if (playerHand == null || playerHand.Hand == null || !playerHand.Hand.IsTrackedDataValid)
            return;

        // OpenXRRoot가 없으면 Wrist로 폴백
        Transform targetTransform = openXRRoot;
        if (targetTransform == null)
        {
            Transform wrist = playerHand.Joints[(int)HandJointId.HandWristRoot];
            if (wrist == null)
                return;
            targetTransform = wrist;
        }

        // 목표 위치 계산 (기준점 적용)
        Vector3 targetPos = targetRootPosition;
        if (referencePoint != null)
        {
            targetPos = referencePoint.position + targetRootPosition;
        }

        Vector3 playerPos = targetTransform.position;

        // 디버그용 저장
        if (handName == "왼손")
        {
            leftReplayTargetPosition = targetPos;
            leftPlayerCurrentPosition = playerPos;
        }
        else
        {
            rightReplayTargetPosition = targetPos;
            rightPlayerCurrentPosition = playerPos;
        }

        // 위치 오차 계산
        positionError = Vector3.Distance(playerPos, targetPos);

        // 위치 유사도 계산 (0~1, 임계값 기준으로 역비례)
        // positionError가 0이면 1.0, threshold이면 0.0
        positionSimilarity = Mathf.Clamp01(1f - (positionError / settings.handPositionThreshold));

        // 회전 오차 계산
        if (settings.compareHandRotation)
        {
            rotationError = Quaternion.Angle(targetTransform.rotation, targetRootRotation);

            // 회전 유사도 계산 (0~1, 임계값 기준으로 역비례)
            // rotationError가 0이면 1.0, threshold이면 0.0
            rotationSimilarity = Mathf.Clamp01(1f - (rotationError / settings.handRotationThreshold));
        }
        else
        {
            rotationSimilarity = 1f;  // 회전 비교 안 함 = 항상 통과
        }

        // 합격 여부
        bool positionPassed = positionError <= settings.handPositionThreshold;
        bool rotationPassed = !settings.compareHandRotation || rotationError <= settings.handRotationThreshold;
        passed = positionPassed && rotationPassed;

        // 디버그 로그
        if (frameIndex % 10 == 0)
        {
            Debug.Log($"[HandPoseComparator] {handName} 위치 오차: {positionError:F3}m (유사도:{positionSimilarity:P0}), 회전 오차: {rotationError:F1}° (유사도:{rotationSimilarity:P0}) 합격: {passed}");
        }
    }

    /// <summary>
    /// 설정 가져오기
    /// </summary>
    public ComparisonSettings GetSettings()
    {
        return settings;
    }

    /// <summary>
    /// 디버그용 위치 가져오기
    /// </summary>
    public (Vector3 leftTarget, Vector3 leftPlayer, Vector3 rightTarget, Vector3 rightPlayer) GetDebugPositions()
    {
        return (leftReplayTargetPosition, leftPlayerCurrentPosition, rightReplayTargetPosition, rightPlayerCurrentPosition);
    }

    /// <summary>
    /// 연속 프레임 카운터 리셋 (새로운 훈련 시작 시 호출)
    /// </summary>
    public void ResetConsecutiveCounters()
    {
        leftConsecutiveSuccessCount = 0;
        rightConsecutiveSuccessCount = 0;
        Debug.Log("[HandPoseComparator] 연속 프레임 카운터 리셋");
    }

    /// <summary>
    /// 현재 연속 성공 카운트 가져오기 (디버그용)
    /// </summary>
    public (int leftCount, int rightCount) GetConsecutiveCounts()
    {
        return (leftConsecutiveSuccessCount, rightConsecutiveSuccessCount);
    }
}
