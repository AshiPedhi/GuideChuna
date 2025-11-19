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

        [Header("비교할 주요 조인트")]
        public List<HandJointId> keyJoints = new List<HandJointId>()
        {
            HandJointId.HandWristRoot,
            HandJointId.HandThumb3,
            HandJointId.HandIndex3,
            HandJointId.HandMiddle3,
            HandJointId.HandRing3,
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
    /// 왼손 포즈 비교
    /// </summary>
    public SimilarityResult CompareLeftPose(HandVisual playerLeftHand, PoseFrame guideFrame, int currentFrameIndex = 0)
    {
        SimilarityResult result = new SimilarityResult();

        if (playerLeftHand == null || guideFrame == null)
            return result;

        if (playerLeftHand.Hand == null || !playerLeftHand.Hand.IsTrackedDataValid)
            return result;

        // 조인트 유사도 비교
        bool passed;
        result.leftHandSimilarity = ComparePose(playerLeftHand, guideFrame.leftLocalPoses, out passed, "왼손", currentFrameIndex);
        result.leftHandPassed = passed;

        // 손 전체 위치/회전 비교
        if (settings.compareHandPosition)
        {
            CompareHandWorldPosition(
                playerLeftHand,
                guideFrame.leftRootPosition,
                guideFrame.leftRootRotation,
                leftOpenXRRoot,
                out result.leftHandPositionError,
                out result.leftHandRotationError,
                out result.leftHandPositionPassed,
                "왼손",
                currentFrameIndex
            );
        }
        else
        {
            result.leftHandPositionPassed = true;
        }

        result.overallPassed = result.leftHandPassed && result.leftHandPositionPassed;

        return result;
    }

    /// <summary>
    /// 오른손 포즈 비교
    /// </summary>
    public SimilarityResult CompareRightPose(HandVisual playerRightHand, PoseFrame guideFrame, int currentFrameIndex = 0)
    {
        SimilarityResult result = new SimilarityResult();

        if (playerRightHand == null || guideFrame == null)
            return result;

        if (playerRightHand.Hand == null || !playerRightHand.Hand.IsTrackedDataValid)
            return result;

        // 조인트 유사도 비교
        bool passed;
        result.rightHandSimilarity = ComparePose(playerRightHand, guideFrame.rightLocalPoses, out passed, "오른손", currentFrameIndex);
        result.rightHandPassed = passed;

        // 손 전체 위치/회전 비교
        if (settings.compareHandPosition)
        {
            CompareHandWorldPosition(
                playerRightHand,
                guideFrame.rightRootPosition,
                guideFrame.rightRootRotation,
                rightOpenXRRoot,
                out result.rightHandPositionError,
                out result.rightHandRotationError,
                out result.rightHandPositionPassed,
                "오른손",
                currentFrameIndex
            );
        }
        else
        {
            result.rightHandPositionPassed = true;
        }

        result.overallPassed = result.rightHandPassed && result.rightHandPositionPassed;

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
    /// 조인트 포즈 비교 (로컬 좌표)
    /// </summary>
    private float ComparePose(HandVisual playerHand, Dictionary<int, PoseData> guidePoses, out bool passed, string handName, int frameIndex)
    {
        passed = false;

        if (playerHand == null || playerHand.Hand == null || !playerHand.Hand.IsTrackedDataValid)
        {
            return 0f;
        }

        int similarJointCount = 0;
        int totalJointCount = 0;

        foreach (HandJointId jointId in settings.keyJoints)
        {
            int jointIndex = (int)jointId;

            if (!guidePoses.ContainsKey(jointIndex))
                continue;

            totalJointCount++;

            if (jointIndex >= playerHand.Joints.Count || playerHand.Joints[jointIndex] == null)
                continue;

            Transform playerJoint = playerHand.Joints[jointIndex];
            PoseData guidePose = guidePoses[jointIndex];

            float positionDistance = Vector3.Distance(playerJoint.localPosition, guidePose.position);
            float rotationAngle = Quaternion.Angle(playerJoint.localRotation, guidePose.rotation);

            if (positionDistance <= settings.positionThreshold && rotationAngle <= settings.rotationThreshold)
            {
                similarJointCount++;
            }
        }

        if (totalJointCount == 0)
            return 0f;

        float similarity = (float)similarJointCount / totalJointCount;
        passed = similarity >= settings.similarityPercentage;

        return similarity;
    }

    /// <summary>
    /// 손 전체 위치/회전 비교 (월드 좌표)
    /// </summary>
    private void CompareHandWorldPosition(
        HandVisual playerHand,
        Vector3 targetRootPosition,
        Quaternion targetRootRotation,
        Transform openXRRoot,
        out float positionError,
        out float rotationError,
        out bool passed,
        string handName,
        int frameIndex)
    {
        positionError = 0f;
        rotationError = 0f;
        passed = false;

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

        // 회전 오차 계산
        if (settings.compareHandRotation)
        {
            rotationError = Quaternion.Angle(targetTransform.rotation, targetRootRotation);
        }

        // 합격 여부
        bool positionPassed = positionError <= settings.handPositionThreshold;
        bool rotationPassed = !settings.compareHandRotation || rotationError <= settings.handRotationThreshold;
        passed = positionPassed && rotationPassed;

        // 디버그 로그
        if (frameIndex % 10 == 0)
        {
            Debug.Log($"[HandPoseComparator] {handName} 위치 오차: {positionError:F3}m, 회전 오차: {rotationError:F1}° (합격: {passed})");
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
}
