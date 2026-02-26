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
        [Header("조인트 비교 임계값 (완화됨 - 손모양보다 손목 위치/회전 중시)")]
        public float positionThreshold = 0.08f;     // 8cm (완화)
        public float rotationThreshold = 25f;        // 25도 (완화)
        public float similarityPercentage = 0.5f;    // 50% (완화 - 대략적 손모양만 맞으면 OK)

        [Header("손목 위치/회전 비교 설정 (핵심)")]
        public bool compareHandPosition = true;
        public float handPositionThreshold = 0.08f;   // 8cm (정밀화)
        public bool compareHandRotation = true;
        public float handRotationThreshold = 15f;    // 15도 (정밀화 - 회전 매칭 강화)

        [Header("연속 프레임 검증 (노이즈 필터링)")]
        [Tooltip("통과로 인정하기 위해 필요한 연속 프레임 수 (1 = 즉시 통과, 3 = 3프레임 연속)")]
        public int consecutiveFramesRequired = 3;

        [Header("관절별 가중치 (손목 강조)")]
        [Tooltip("손목 관절 가중치 (가장 중요 - 상향)")]
        public float wristWeight = 3.0f;
        [Tooltip("손가락 끝 관절 가중치 (하향)")]
        public float fingerTipWeight = 1.0f;
        [Tooltip("기타 관절 가중치 (하향)")]
        public float otherJointWeight = 0.5f;

        [Header("적응형 임계값 (관절별 조정 - 완화)")]
        [Tooltip("손목은 위치 변화가 크므로 임계값 완화")]
        public float wristPositionMultiplier = 2.0f;  // 16cm (완화)
        [Tooltip("손가락 끝은 회전 변화가 크므로 임계값 완화")]
        public float fingerTipRotationMultiplier = 2.0f; // 50도 (완화)

        [Header("유사도 통합 가중치 (손목 위치/회전 강조)")]
        [Tooltip("조인트 포즈 가중치 (0~1) - 하향")]
        public float jointSimilarityWeight = 0.2f;
        [Tooltip("손목 위치 가중치 (0~1) - 핵심")]
        public float handPositionWeight = 0.4f;
        [Tooltip("손목 회전 가중치 (0~1) - 상향")]
        public float handRotationWeight = 0.4f;

        [Header("양손 가중치 설정")]
        [Tooltip("오른손 가중치 (0~1, 기본 0.7 = 70%)")]
        public float rightHandWeight = 0.7f;
        [Tooltip("왼손 가중치 (0~1, 기본 0.3 = 30%)")]
        public float leftHandWeight = 0.3f;

        [Header("간소화된 손 체크 설정")]
        [Tooltip("왼손: 손바닥이 아래를 향하는지 체크 (기본 true)")]
        public bool leftHandCheckPalmDown = true;
        [Tooltip("왼손: 주먹 쥐지 않았는지 체크 (기본 true)")]
        public bool leftHandCheckNotFisted = true;
        [Tooltip("오른손: 경로 근접도 체크 (기본 true)")]
        public bool rightHandCheckPathProximity = true;
        [Tooltip("오른손: 손 모양 체크 (너무 펴지거나 쥐지 않았는지)")]
        public bool rightHandCheckHandShape = true;

        [Header("손 모양 임계값")]
        [Tooltip("주먹 쥔 정도 임계값 (이 값 이상이면 주먹으로 판정, 기본 0.7)")]
        public float fistThreshold = 0.7f;
        [Tooltip("손이 너무 펴진 정도 임계값 (이 값 이하면 너무 펴진 것으로 판정, 기본 0.2)")]
        public float openHandThreshold = 0.2f;
        [Tooltip("손바닥 아래 방향 임계값 (각도, 기본 45도)")]
        public float palmDownAngleThreshold = 45f;

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
                    ChunaLogger.LogWarning("[HandPoseComparator] 왼손 손목 위치 이상 (원점 근처)");
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
            // Palm Center 계산 (실패 시 wrist/OpenXRRoot 기반으로 fallback)
            Vector3 playerPalmPos;
            Quaternion playerPalmRot;
            Vector3 guidePalmPos;
            Quaternion guidePalmRot;

            if (!ComputePalmCenter(playerLeftHand, out playerPalmPos))
            {
                Transform fb = leftOpenXRRoot;
                if (fb == null && playerLeftHand.Joints != null && playerLeftHand.Joints.Count > 0)
                    fb = playerLeftHand.Joints[(int)HandJointId.HandWristRoot];
                playerPalmPos = fb != null ? fb.position : Vector3.zero;
            }
            if (!ComputePalmRotation(playerLeftHand, out playerPalmRot))
            {
                Transform fb = leftOpenXRRoot;
                if (fb == null && playerLeftHand.Joints != null && playerLeftHand.Joints.Count > 0)
                    fb = playerLeftHand.Joints[(int)HandJointId.HandWristRoot];
                playerPalmRot = fb != null ? fb.rotation : Quaternion.identity;
            }
            if (!ComputePalmCenterFromFrame(guideFrame.leftLocalPoses, guideFrame.leftRootPosition, guideFrame.leftRootRotation, out guidePalmPos))
            {
                guidePalmPos = guideFrame.leftRootPosition;
            }
            if (!ComputePalmRotationFromFrame(guideFrame.leftLocalPoses, guideFrame.leftRootPosition, guideFrame.leftRootRotation, out guidePalmRot))
            {
                guidePalmRot = guideFrame.leftRootRotation;
            }

            CompareHandWorldPosition(
                playerPalmPos, playerPalmRot,
                guidePalmPos, guidePalmRot,
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

        // ★ 페널티 시스템 (대폭 완화 - 기본 유사도 유지, 극단적 오차만 페널티)
        // 위치 오차가 임계값의 4배 이상이면 페널티 (매우 관대)
        if (result.leftHandPositionError > settings.handPositionThreshold * 4f)
        {
            result.leftHandSimilarity = Mathf.Min(result.leftHandSimilarity, 0.3f); // 30% 이하로 강제

            if (settings.showDetailedLogs && currentFrameIndex % 30 == 0)
            {
                ChunaLogger.LogWarning($"[HandPoseComparator] 왼손 위치 오차 매우 큼 ({result.leftHandPositionError:F3}m) - 유사도: {result.leftHandSimilarity:P0}");
            }
        }
        // 회전 오차가 임계값의 4배 이상이면 페널티 (매우 관대)
        else if (settings.compareHandRotation && result.leftHandRotationError > settings.handRotationThreshold * 4f)
        {
            result.leftHandSimilarity = Mathf.Min(result.leftHandSimilarity, 0.3f); // 30% 이하로 강제

            if (settings.showDetailedLogs && currentFrameIndex % 30 == 0)
            {
                ChunaLogger.LogWarning($"[HandPoseComparator] 왼손 회전 오차 매우 큼 ({result.leftHandRotationError:F1}°) - 유사도: {result.leftHandSimilarity:P0}");
            }
        }
        // 위치/회전 오차가 임계값의 3배 이상이면 페널티 (50% 이하, 관대)
        else if (result.leftHandPositionError > settings.handPositionThreshold * 3f ||
                 (settings.compareHandRotation && result.leftHandRotationError > settings.handRotationThreshold * 3f))
        {
            result.leftHandSimilarity = Mathf.Min(result.leftHandSimilarity, 0.5f); // 50% 이하

            if (settings.showDetailedLogs && currentFrameIndex % 30 == 0)
            {
                ChunaLogger.LogWarning($"[HandPoseComparator] 왼손 위치/회전 오차 큼 - 유사도: {result.leftHandSimilarity:P0}");
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
            ChunaLogger.Log($"[HandPoseComparator] 왼손 연속 성공: {leftConsecutiveSuccessCount}/{settings.consecutiveFramesRequired} (통합 유사도: {result.leftHandSimilarity:P0}, 조인트:{jointSimilarity:P0}, 위치:{positionSimilarity:P0}, 회전:{rotationSimilarity:P0})");
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
                    ChunaLogger.LogWarning("[HandPoseComparator] 오른손 손목 위치 이상 (원점 근처)");
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
            // Palm Center 계산 (실패 시 wrist/OpenXRRoot 기반으로 fallback)
            Vector3 playerPalmPos;
            Quaternion playerPalmRot;
            Vector3 guidePalmPos;
            Quaternion guidePalmRot;

            if (!ComputePalmCenter(playerRightHand, out playerPalmPos))
            {
                Transform fb = rightOpenXRRoot;
                if (fb == null && playerRightHand.Joints != null && playerRightHand.Joints.Count > 0)
                    fb = playerRightHand.Joints[(int)HandJointId.HandWristRoot];
                playerPalmPos = fb != null ? fb.position : Vector3.zero;
            }
            if (!ComputePalmRotation(playerRightHand, out playerPalmRot))
            {
                Transform fb = rightOpenXRRoot;
                if (fb == null && playerRightHand.Joints != null && playerRightHand.Joints.Count > 0)
                    fb = playerRightHand.Joints[(int)HandJointId.HandWristRoot];
                playerPalmRot = fb != null ? fb.rotation : Quaternion.identity;
            }
            if (!ComputePalmCenterFromFrame(guideFrame.rightLocalPoses, guideFrame.rightRootPosition, guideFrame.rightRootRotation, out guidePalmPos))
            {
                guidePalmPos = guideFrame.rightRootPosition;
            }
            if (!ComputePalmRotationFromFrame(guideFrame.rightLocalPoses, guideFrame.rightRootPosition, guideFrame.rightRootRotation, out guidePalmRot))
            {
                guidePalmRot = guideFrame.rightRootRotation;
            }

            CompareHandWorldPosition(
                playerPalmPos, playerPalmRot,
                guidePalmPos, guidePalmRot,
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

        // ★ 페널티 시스템 (대폭 완화 - 기본 유사도 유지, 극단적 오차만 페널티)
        // 위치 오차가 임계값의 4배 이상이면 페널티 (매우 관대)
        if (result.rightHandPositionError > settings.handPositionThreshold * 4f)
        {
            result.rightHandSimilarity = Mathf.Min(result.rightHandSimilarity, 0.3f); // 30% 이하로 강제

            if (settings.showDetailedLogs && currentFrameIndex % 30 == 0)
            {
                ChunaLogger.LogWarning($"[HandPoseComparator] 오른손 위치 오차 매우 큼 ({result.rightHandPositionError:F3}m) - 유사도: {result.rightHandSimilarity:P0}");
            }
        }
        // 회전 오차가 임계값의 4배 이상이면 페널티 (매우 관대)
        else if (settings.compareHandRotation && result.rightHandRotationError > settings.handRotationThreshold * 4f)
        {
            result.rightHandSimilarity = Mathf.Min(result.rightHandSimilarity, 0.3f); // 30% 이하로 강제

            if (settings.showDetailedLogs && currentFrameIndex % 30 == 0)
            {
                ChunaLogger.LogWarning($"[HandPoseComparator] 오른손 회전 오차 매우 큼 ({result.rightHandRotationError:F1}°) - 유사도: {result.rightHandSimilarity:P0}");
            }
        }
        // 위치/회전 오차가 임계값의 3배 이상이면 페널티 (50% 이하, 관대)
        else if (result.rightHandPositionError > settings.handPositionThreshold * 3f ||
                 (settings.compareHandRotation && result.rightHandRotationError > settings.handRotationThreshold * 3f))
        {
            result.rightHandSimilarity = Mathf.Min(result.rightHandSimilarity, 0.5f); // 50% 이하

            if (settings.showDetailedLogs && currentFrameIndex % 30 == 0)
            {
                ChunaLogger.LogWarning($"[HandPoseComparator] 오른손 위치/회전 오차 큼 - 유사도: {result.rightHandSimilarity:P0}");
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
            ChunaLogger.Log($"[HandPoseComparator] 오른손 연속 성공: {rightConsecutiveSuccessCount}/{settings.consecutiveFramesRequired} (통합 유사도: {result.rightHandSimilarity:P0}, 조인트:{jointSimilarity:P0}, 위치:{positionSimilarity:P0}, 회전:{rotationSimilarity:P0})");
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
                ChunaLogger.LogWarning($"[HandPoseComparator] {handName} {jointId} 실패: " +
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

    // ========== Palm Center 계산 헬퍼 ==========

    /// <summary>
    /// 플레이어 손에서 Palm Center 계산 (Index1, Middle1, Ring1 평균)
    /// 실패 시 false 반환 → 호출부에서 wrist 기반으로 fallback
    /// </summary>
    private bool ComputePalmCenter(HandVisual hand, out Vector3 palmCenter)
    {
        palmCenter = Vector3.zero;

        if (hand == null || hand.Joints == null || hand.Joints.Count == 0)
            return false;

        int index1 = (int)HandJointId.HandIndex1;    // 6
        int middle1 = (int)HandJointId.HandMiddle1;   // 9
        int ring1 = (int)HandJointId.HandRing1;       // 12

        if (index1 >= hand.Joints.Count || middle1 >= hand.Joints.Count || ring1 >= hand.Joints.Count)
            return false;

        Transform index1T = hand.Joints[index1];
        Transform middle1T = hand.Joints[middle1];
        Transform ring1T = hand.Joints[ring1];

        if (index1T == null || middle1T == null || ring1T == null)
            return false;

        palmCenter = (index1T.position + middle1T.position + ring1T.position) / 3f;
        return true;
    }

    /// <summary>
    /// 플레이어 손에서 Palm Rotation 계산 (기하학적 평면)
    /// forward = 손목→중지, right = 검지→약지, up = cross(forward, right)
    /// 실패 시 false 반환 → 호출부에서 wrist 기반으로 fallback
    /// </summary>
    private bool ComputePalmRotation(HandVisual hand, out Quaternion palmRotation)
    {
        palmRotation = Quaternion.identity;

        if (hand == null || hand.Joints == null || hand.Joints.Count == 0)
            return false;

        int wristIdx = (int)HandJointId.HandWristRoot;  // 0
        int index1 = (int)HandJointId.HandIndex1;        // 6
        int middle1 = (int)HandJointId.HandMiddle1;       // 9
        int ring1 = (int)HandJointId.HandRing1;           // 12

        if (wristIdx >= hand.Joints.Count || index1 >= hand.Joints.Count ||
            middle1 >= hand.Joints.Count || ring1 >= hand.Joints.Count)
            return false;

        Transform wristT = hand.Joints[wristIdx];
        Transform index1T = hand.Joints[index1];
        Transform middle1T = hand.Joints[middle1];
        Transform ring1T = hand.Joints[ring1];

        if (wristT == null || index1T == null || middle1T == null || ring1T == null)
            return false;

        Vector3 forwardDir = middle1T.position - wristT.position;
        Vector3 rightDir = index1T.position - ring1T.position;

        if (forwardDir.sqrMagnitude < 0.0001f || rightDir.sqrMagnitude < 0.0001f)
            return false;

        Vector3 forward = forwardDir.normalized;
        Vector3 right = rightDir.normalized;
        Vector3 upRaw = Vector3.Cross(forward, right);

        if (upRaw.sqrMagnitude < 0.0001f)
            return false;

        palmRotation = Quaternion.LookRotation(forward, upRaw.normalized);
        return true;
    }

    /// <summary>
    /// 가이드 프레임에서 Palm Center 계산 (로컬 포즈 + 루트 변환으로 월드 좌표 복원)
    /// WristRoot의 직접 자식(Index1, Middle1, Ring1)이므로 정확한 복원 가능
    /// 실패 시 false 반환 → 호출부에서 rootPos 기반으로 fallback
    /// </summary>
    private bool ComputePalmCenterFromFrame(Dictionary<int, PoseData> localPoses, Vector3 rootPos, Quaternion rootRot, out Vector3 palmCenter)
    {
        palmCenter = Vector3.zero;

        int wristIdx = (int)HandJointId.HandWristRoot;  // 0
        int index1 = (int)HandJointId.HandIndex1;        // 6
        int middle1 = (int)HandJointId.HandMiddle1;       // 9
        int ring1 = (int)HandJointId.HandRing1;           // 12

        if (!localPoses.ContainsKey(wristIdx) || !localPoses.ContainsKey(index1) ||
            !localPoses.ContainsKey(middle1) || !localPoses.ContainsKey(ring1))
            return false;

        // WristRoot의 월드 변환 계산 (OpenXRRoot → WristRoot)
        Vector3 wristWorldPos = rootPos + rootRot * localPoses[wristIdx].position;
        Quaternion wristWorldRot = rootRot * localPoses[wristIdx].rotation;

        // 직접 자식 관절들의 월드 위치 계산
        Vector3 index1WorldPos = wristWorldPos + wristWorldRot * localPoses[index1].position;
        Vector3 middle1WorldPos = wristWorldPos + wristWorldRot * localPoses[middle1].position;
        Vector3 ring1WorldPos = wristWorldPos + wristWorldRot * localPoses[ring1].position;

        palmCenter = (index1WorldPos + middle1WorldPos + ring1WorldPos) / 3f;
        return true;
    }

    /// <summary>
    /// 가이드 프레임에서 Palm Rotation 계산 (기하학적 평면)
    /// forward = 손목→중지, right = 검지→약지, up = cross(forward, right)
    /// 실패 시 false 반환 → 호출부에서 rootRot 기반으로 fallback
    /// </summary>
    private bool ComputePalmRotationFromFrame(Dictionary<int, PoseData> localPoses, Vector3 rootPos, Quaternion rootRot, out Quaternion palmRotation)
    {
        palmRotation = Quaternion.identity;

        int wristIdx = (int)HandJointId.HandWristRoot;  // 0
        int index1 = (int)HandJointId.HandIndex1;        // 6
        int middle1 = (int)HandJointId.HandMiddle1;       // 9
        int ring1 = (int)HandJointId.HandRing1;           // 12

        if (!localPoses.ContainsKey(wristIdx) || !localPoses.ContainsKey(index1) ||
            !localPoses.ContainsKey(middle1) || !localPoses.ContainsKey(ring1))
            return false;

        // WristRoot의 월드 변환 계산
        Vector3 wristWorldPos = rootPos + rootRot * localPoses[wristIdx].position;
        Quaternion wristWorldRot = rootRot * localPoses[wristIdx].rotation;

        // 직접 자식 관절들의 월드 위치 계산
        Vector3 index1WorldPos = wristWorldPos + wristWorldRot * localPoses[index1].position;
        Vector3 middle1WorldPos = wristWorldPos + wristWorldRot * localPoses[middle1].position;
        Vector3 ring1WorldPos = wristWorldPos + wristWorldRot * localPoses[ring1].position;

        Vector3 forwardDir = middle1WorldPos - wristWorldPos;
        Vector3 rightDir = index1WorldPos - ring1WorldPos;

        if (forwardDir.sqrMagnitude < 0.0001f || rightDir.sqrMagnitude < 0.0001f)
            return false;

        Vector3 forward = forwardDir.normalized;
        Vector3 right = rightDir.normalized;
        Vector3 upRaw = Vector3.Cross(forward, right);

        if (upRaw.sqrMagnitude < 0.0001f)
            return false;

        palmRotation = Quaternion.LookRotation(forward, upRaw.normalized);
        return true;
    }

    /// <summary>
    /// 손 전체 위치/회전 비교 (Palm Center 기준, 월드 좌표) - 유사도 반환
    /// </summary>
    private void CompareHandWorldPosition(
        Vector3 playerPalmCenter, Quaternion playerPalmRotation,
        Vector3 guidePalmCenter, Quaternion guidePalmRotation,
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

        // 목표 위치 계산 (기준점 적용)
        Vector3 targetPos = guidePalmCenter;
        if (referencePoint != null)
        {
            targetPos = referencePoint.position + guidePalmCenter;
        }

        Vector3 playerPos = playerPalmCenter;

        // 디버그용 저장 (palm center 기준)
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

        // ★ 위치 유사도 계산 (부드러운 곡선 - 작은 오차에 관대)
        float normalizedPosError = positionError / settings.handPositionThreshold;

        if (normalizedPosError <= 0.5f)
        {
            positionSimilarity = 1f - (normalizedPosError * 0.3f);
        }
        else if (normalizedPosError <= 1f)
        {
            positionSimilarity = 0.85f - ((normalizedPosError - 0.5f) * 0.7f);
        }
        else
        {
            positionSimilarity = Mathf.Max(0f, 0.5f - ((normalizedPosError - 1f) * 0.5f));
        }

        // 회전 오차 계산 (palm rotation 기준)
        if (settings.compareHandRotation)
        {
            rotationError = Quaternion.Angle(playerPalmRotation, guidePalmRotation);

            float normalizedError = rotationError / settings.handRotationThreshold;

            if (normalizedError <= 0.5f)
            {
                rotationSimilarity = 1f - (normalizedError * 0.4f);
            }
            else if (normalizedError <= 1f)
            {
                rotationSimilarity = 0.8f - ((normalizedError - 0.5f) * 0.8f);
            }
            else
            {
                rotationSimilarity = Mathf.Max(0f, 0.4f - ((normalizedError - 1f) * 0.4f));
            }
        }
        else
        {
            rotationSimilarity = 1f;
        }

        // 합격 여부
        bool positionPassed = positionError <= settings.handPositionThreshold;
        bool rotationPassed = !settings.compareHandRotation || rotationError <= settings.handRotationThreshold;
        passed = positionPassed && rotationPassed;

        // 디버그 로그
        if (frameIndex % 10 == 0)
        {
            ChunaLogger.Log($"[HandPoseComparator] {handName} Palm 위치 오차: {positionError:F3}m (유사도:{positionSimilarity:P0}), 회전 오차: {rotationError:F1}° (유사도:{rotationSimilarity:P0}) 합격: {passed}");
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
        ChunaLogger.Log("[HandPoseComparator] 연속 프레임 카운터 리셋");
    }

    /// <summary>
    /// 현재 연속 성공 카운트 가져오기 (디버그용)
    /// </summary>
    public (int leftCount, int rightCount) GetConsecutiveCounts()
    {
        return (leftConsecutiveSuccessCount, rightConsecutiveSuccessCount);
    }

    // ========== 간소화된 손 체크 메서드 ==========

    /// <summary>
    /// 손바닥이 아래를 향하는지 체크
    /// </summary>
    public bool IsPalmFacingDown(HandVisual hand, out float palmAngle)
    {
        palmAngle = 180f;

        if (hand == null || hand.Joints == null || hand.Joints.Count == 0)
            return false;

        // 손목 Transform 가져오기
        Transform wrist = hand.Joints[(int)HandJointId.HandWristRoot];
        if (wrist == null)
            return false;

        // 손바닥 방향 계산 (손목의 -up 방향이 손바닥 방향)
        // 손바닥이 아래를 향하면 -wrist.up과 Vector3.down의 각도가 작음
        Vector3 palmNormal = -wrist.up;
        palmAngle = Vector3.Angle(palmNormal, Vector3.down);

        return palmAngle <= settings.palmDownAngleThreshold;
    }

    /// <summary>
    /// 손의 주먹 쥔 정도 계산 (0 = 완전히 펴짐, 1 = 완전히 쥠)
    /// </summary>
    public float GetFistLevel(HandVisual hand)
    {
        if (hand == null || hand.Joints == null || hand.Joints.Count == 0)
            return 0f;

        float totalCurl = 0f;
        int fingerCount = 0;

        // 각 손가락의 굽힘 정도 계산 (엄지 제외, 검지~새끼)
        HandJointId[] fingerBases = {
            HandJointId.HandIndex1,
            HandJointId.HandMiddle1,
            HandJointId.HandRing1,
            HandJointId.HandPinky1
        };

        HandJointId[] fingerTips = {
            HandJointId.HandIndex3,
            HandJointId.HandMiddle3,
            HandJointId.HandRing3,
            HandJointId.HandPinky3
        };

        Transform wrist = hand.Joints[(int)HandJointId.HandWristRoot];
        if (wrist == null)
            return 0f;

        for (int i = 0; i < fingerBases.Length; i++)
        {
            int baseIndex = (int)fingerBases[i];
            int tipIndex = (int)fingerTips[i];

            if (baseIndex >= hand.Joints.Count || tipIndex >= hand.Joints.Count)
                continue;

            Transform fingerBase = hand.Joints[baseIndex];
            Transform fingerTip = hand.Joints[tipIndex];

            if (fingerBase == null || fingerTip == null)
                continue;

            // 손목에서 손가락 끝까지의 거리 vs 손목에서 손가락 기저부까지의 거리
            // 주먹을 쥐면 손가락 끝이 손목에 가까워짐
            float baseDistance = Vector3.Distance(wrist.position, fingerBase.position);
            float tipDistance = Vector3.Distance(wrist.position, fingerTip.position);

            // 정상적인 펴진 손: tipDistance > baseDistance * 2
            // 쥔 주먹: tipDistance ≈ baseDistance
            float expectedTipDistance = baseDistance * 2.5f; // 펴진 손일 때 예상 거리
            float curlRatio = 1f - Mathf.Clamp01(tipDistance / expectedTipDistance);

            totalCurl += curlRatio;
            fingerCount++;
        }

        return fingerCount > 0 ? totalCurl / fingerCount : 0f;
    }

    /// <summary>
    /// 손 모양 유사도 계산 (너무 펴지거나 주먹 쥐지 않았으면 높은 점수)
    /// </summary>
    public float GetHandShapeSimilarity(HandVisual hand, out bool isTooOpen, out bool isTooFisted)
    {
        float fistLevel = GetFistLevel(hand);

        isTooOpen = fistLevel < settings.openHandThreshold;
        isTooFisted = fistLevel > settings.fistThreshold;

        if (isTooOpen)
        {
            // 너무 펴짐 - 0.2 이하일 때 유사도 감소
            return Mathf.Lerp(0.5f, 1f, fistLevel / settings.openHandThreshold);
        }
        else if (isTooFisted)
        {
            // 너무 쥐어짐 - 0.7 이상일 때 유사도 감소
            return Mathf.Lerp(1f, 0.5f, (fistLevel - settings.fistThreshold) / (1f - settings.fistThreshold));
        }
        else
        {
            // 적정 범위 내 (0.2 ~ 0.7) - 높은 유사도
            return 1f;
        }
    }

    /// <summary>
    /// ★ 간소화된 왼손 유사도 계산
    /// 손바닥이 아래를 향하고 주먹을 쥐지 않았는지만 체크
    /// </summary>
    public SimilarityResult CompareLeftPoseSimplified(HandVisual playerLeftHand, PoseFrame guideFrame, int currentFrameIndex = 0)
    {
        SimilarityResult result = new SimilarityResult();

        if (playerLeftHand == null || guideFrame == null)
        {
            leftConsecutiveSuccessCount = 0;
            return result;
        }

        if (playerLeftHand.Hand == null || !playerLeftHand.Hand.IsTrackedDataValid)
        {
            leftConsecutiveSuccessCount = 0;
            return result;
        }

        float palmSimilarity = 1f;
        float shapeSimilarity = 1f;
        bool palmOk = true;
        bool shapeOk = true;

        // 1. 손바닥 방향 체크 (아래를 향해야 함)
        if (settings.leftHandCheckPalmDown)
        {
            float palmAngle;
            palmOk = IsPalmFacingDown(playerLeftHand, out palmAngle);

            // 부드러운 유사도 계산
            if (palmAngle <= settings.palmDownAngleThreshold)
            {
                palmSimilarity = 1f;
            }
            else if (palmAngle <= settings.palmDownAngleThreshold * 2f)
            {
                palmSimilarity = Mathf.Lerp(1f, 0.5f, (palmAngle - settings.palmDownAngleThreshold) / settings.palmDownAngleThreshold);
            }
            else
            {
                palmSimilarity = Mathf.Lerp(0.5f, 0f, (palmAngle - settings.palmDownAngleThreshold * 2f) / (180f - settings.palmDownAngleThreshold * 2f));
            }

            if (settings.showDetailedLogs && currentFrameIndex % 30 == 0)
            {
                ChunaLogger.Log($"[HandPoseComparator] 왼손 손바닥 각도: {palmAngle:F1}° (임계값: {settings.palmDownAngleThreshold}°) → 유사도: {palmSimilarity:P0}");
            }
        }

        // 2. 주먹 쥐지 않았는지 체크
        if (settings.leftHandCheckNotFisted)
        {
            bool isTooOpen, isTooFisted;
            shapeSimilarity = GetHandShapeSimilarity(playerLeftHand, out isTooOpen, out isTooFisted);
            shapeOk = !isTooFisted; // 왼손은 주먹 쥐었는지만 체크

            if (settings.showDetailedLogs && currentFrameIndex % 30 == 0)
            {
                float fistLevel = GetFistLevel(playerLeftHand);
                ChunaLogger.Log($"[HandPoseComparator] 왼손 주먹 정도: {fistLevel:P0} (임계값: {settings.fistThreshold:P0}) → 유사도: {shapeSimilarity:P0}");
            }
        }

        // 통합 유사도 계산
        result.leftHandSimilarity = (palmSimilarity + shapeSimilarity) / 2f;
        result.leftHandPassed = palmOk && shapeOk;
        result.leftHandPositionPassed = true; // 위치는 체크하지 않음
        result.overallPassed = result.leftHandPassed;

        // 연속 프레임 검증
        if (result.leftHandPassed)
        {
            leftConsecutiveSuccessCount++;
        }
        else
        {
            leftConsecutiveSuccessCount = 0;
        }

        if (settings.showDetailedLogs && currentFrameIndex % 10 == 0)
        {
            ChunaLogger.Log($"[HandPoseComparator] 왼손 간소화 체크 - 손바닥:{palmSimilarity:P0}, 손모양:{shapeSimilarity:P0} → 통합:{result.leftHandSimilarity:P0}");
        }

        return result;
    }

    /// <summary>
    /// ★ 간소화된 오른손 유사도 계산
    /// 경로 근접도와 손 모양(너무 펴지거나 주먹 쥐지 않았는지)만 체크
    /// </summary>
    public SimilarityResult CompareRightPoseSimplified(HandVisual playerRightHand, PoseFrame guideFrame, int currentFrameIndex = 0)
    {
        SimilarityResult result = new SimilarityResult();

        if (playerRightHand == null || guideFrame == null)
        {
            rightConsecutiveSuccessCount = 0;
            return result;
        }

        if (playerRightHand.Hand == null || !playerRightHand.Hand.IsTrackedDataValid)
        {
            rightConsecutiveSuccessCount = 0;
            return result;
        }

        float positionSimilarity = 1f;
        float shapeSimilarity = 1f;
        bool positionOk = true;
        bool shapeOk = true;

        // 1. 경로 근접도 체크 (Palm Center 기준)
        if (settings.rightHandCheckPathProximity)
        {
            // 플레이어 Palm Center 계산 (실패 시 OpenXRRoot/wrist로 fallback)
            Vector3 playerPalmPos;
            bool hasPlayerPos = ComputePalmCenter(playerRightHand, out playerPalmPos);
            if (!hasPlayerPos)
            {
                Transform fb = rightOpenXRRoot;
                if (fb == null && playerRightHand.Joints != null && playerRightHand.Joints.Count > 0)
                    fb = playerRightHand.Joints[(int)HandJointId.HandWristRoot];
                if (fb != null)
                {
                    playerPalmPos = fb.position;
                    hasPlayerPos = true;
                }
            }

            if (hasPlayerPos)
            {
                // 가이드 Palm Center 계산 (실패 시 rootPosition으로 fallback)
                Vector3 guidePalmPos;
                if (!ComputePalmCenterFromFrame(guideFrame.rightLocalPoses, guideFrame.rightRootPosition, guideFrame.rightRootRotation, out guidePalmPos))
                    guidePalmPos = guideFrame.rightRootPosition;

                Vector3 targetPos = guidePalmPos;
                if (referencePoint != null)
                {
                    targetPos = referencePoint.position + guidePalmPos;
                }

                float positionError = Vector3.Distance(playerPalmPos, targetPos);
                result.rightHandPositionError = positionError;

                // 디버그용 저장 (palm center 기준)
                rightReplayTargetPosition = targetPos;
                rightPlayerCurrentPosition = playerPalmPos;

                // 부드러운 위치 유사도 계산
                float normalizedError = positionError / settings.handPositionThreshold;
                if (normalizedError <= 1f)
                {
                    positionSimilarity = 1f - (normalizedError * 0.3f);
                }
                else if (normalizedError <= 2f)
                {
                    positionSimilarity = 0.7f - ((normalizedError - 1f) * 0.4f);
                }
                else
                {
                    positionSimilarity = Mathf.Max(0f, 0.3f - ((normalizedError - 2f) * 0.15f));
                }

                positionOk = positionError <= settings.handPositionThreshold * 2f;

                if (settings.showDetailedLogs && currentFrameIndex % 30 == 0)
                {
                    ChunaLogger.Log($"[HandPoseComparator] 오른손 Palm 위치 오차: {positionError:F3}m (임계값: {settings.handPositionThreshold}m) → 유사도: {positionSimilarity:P0}");
                }
            }
        }

        // 2. 손 모양 체크 (너무 펴지거나 쥐지 않았는지)
        if (settings.rightHandCheckHandShape)
        {
            bool isTooOpen, isTooFisted;
            shapeSimilarity = GetHandShapeSimilarity(playerRightHand, out isTooOpen, out isTooFisted);
            shapeOk = !isTooOpen && !isTooFisted;

            if (settings.showDetailedLogs && currentFrameIndex % 30 == 0)
            {
                float fistLevel = GetFistLevel(playerRightHand);
                ChunaLogger.Log($"[HandPoseComparator] 오른손 주먹 정도: {fistLevel:P0} (너무 펴짐:{isTooOpen}, 너무 쥐어짐:{isTooFisted}) → 유사도: {shapeSimilarity:P0}");
            }
        }

        // 통합 유사도 계산 (위치에 더 높은 가중치)
        result.rightHandSimilarity = (positionSimilarity * 0.7f + shapeSimilarity * 0.3f);
        result.rightHandPassed = positionOk && shapeOk;
        result.rightHandPositionPassed = positionOk;
        result.overallPassed = result.rightHandPassed;

        // 연속 프레임 검증
        if (result.rightHandPassed)
        {
            rightConsecutiveSuccessCount++;
        }
        else
        {
            rightConsecutiveSuccessCount = 0;
        }

        if (settings.showDetailedLogs && currentFrameIndex % 10 == 0)
        {
            ChunaLogger.Log($"[HandPoseComparator] 오른손 간소화 체크 - 위치:{positionSimilarity:P0}, 손모양:{shapeSimilarity:P0} → 통합:{result.rightHandSimilarity:P0}");
        }

        return result;
    }

    /// <summary>
    /// ★ 간소화된 양손 유사도 비교 (오른손 가중치 높음)
    /// </summary>
    public SimilarityResult CompareBothHandsSimplified(HandVisual playerLeftHand, HandVisual playerRightHand, PoseFrame guideFrame, int currentFrameIndex = 0)
    {
        SimilarityResult result = new SimilarityResult();

        // 왼손 비교 (간소화)
        var leftResult = CompareLeftPoseSimplified(playerLeftHand, guideFrame, currentFrameIndex);
        result.leftHandSimilarity = leftResult.leftHandSimilarity;
        result.leftHandPassed = leftResult.leftHandPassed;
        result.leftHandPositionError = leftResult.leftHandPositionError;
        result.leftHandRotationError = leftResult.leftHandRotationError;
        result.leftHandPositionPassed = leftResult.leftHandPositionPassed;

        // 오른손 비교 (간소화)
        var rightResult = CompareRightPoseSimplified(playerRightHand, guideFrame, currentFrameIndex);
        result.rightHandSimilarity = rightResult.rightHandSimilarity;
        result.rightHandPassed = rightResult.rightHandPassed;
        result.rightHandPositionError = rightResult.rightHandPositionError;
        result.rightHandRotationError = rightResult.rightHandRotationError;
        result.rightHandPositionPassed = rightResult.rightHandPositionPassed;

        // 전체 합격 여부 (양손 모두 통과해야 함)
        result.overallPassed = result.leftHandPassed && result.rightHandPassed;

        return result;
    }

    /// <summary>
    /// ★ 가중치 적용된 통합 유사도 계산 (오른손 70%, 왼손 30%)
    /// </summary>
    public float GetWeightedSimilarity(float leftSimilarity, float rightSimilarity)
    {
        return leftSimilarity * settings.leftHandWeight + rightSimilarity * settings.rightHandWeight;
    }
}
