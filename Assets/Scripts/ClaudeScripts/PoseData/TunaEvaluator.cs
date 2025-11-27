using System.Collections.Generic;
using UnityEngine;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using static HandPoseDataLoader;
using TunaEvaluation;

/// <summary>
/// 추나 시술 평가 시스템
/// 경로 준수, 안전성, 정확도, 안정성을 실시간으로 평가
/// </summary>
public class TunaEvaluator : MonoBehaviour
{
    [Header("=== 구간 설정 ===")]
    [SerializeField] private List<TunaMotionSegment> motionSegments = new List<TunaMotionSegment>();

    [Header("=== 평가 활성화 ===")]
    [SerializeField] private bool enableEvaluation = true;
    [SerializeField] private bool showDebugLogs = false;

    [Header("=== 참조 ===")]
    [SerializeField] private HandVisual playerLeftHand;
    [SerializeField] private HandVisual playerRightHand;
    [SerializeField] private TunaEvaluationUI evaluationUI;

    // 평가 데이터
    private EvaluationResult currentResult;
    private bool isEvaluating = false;
    private int currentFrameIndex = 0;
    private int currentSegmentIndex = 0;

    // 체크포인트 유지 시간 추적
    private float checkpointHoldTimer = 0f;
    private bool isHoldingCheckpoint = false;

    // 이전 프레임 데이터 (안전 범위 계산용)
    private Vector3 prevLeftHandPosition;
    private Quaternion prevLeftHandRotation;
    private Vector3 prevRightHandPosition;
    private Quaternion prevRightHandRotation;
    private bool hasPreviousFrame = false;

    // 초기 위치 (거리 계산 기준)
    private Vector3 initialLeftHandPosition;
    private Quaternion initialLeftHandRotation;
    private Vector3 initialRightHandPosition;
    private Quaternion initialRightHandRotation;

    /// <summary>
    /// 평가 시작
    /// </summary>
    public void StartEvaluation()
    {
        if (!enableEvaluation) return;

        currentResult = new EvaluationResult();
        currentResult.startTime = Time.time;
        isEvaluating = true;
        currentFrameIndex = 0;
        currentSegmentIndex = 0;
        checkpointHoldTimer = 0f;
        isHoldingCheckpoint = false;
        hasPreviousFrame = false;

        // 초기 위치 저장
        StoreInitialPositions();

        Debug.Log("[TunaEvaluator] 평가 시작");
    }

    /// <summary>
    /// 평가 종료
    /// </summary>
    public EvaluationResult StopEvaluation()
    {
        if (!isEvaluating) return currentResult;

        isEvaluating = false;
        currentResult.endTime = Time.time;
        currentResult.totalDuration = currentResult.endTime - currentResult.startTime;

        // 최종 점수 계산
        CalculateFinalScore();

        Debug.Log("[TunaEvaluator] 평가 종료");
        Debug.Log(currentResult.GenerateReport());

        return currentResult;
    }

    /// <summary>
    /// 프레임 평가 (매 프레임 호출)
    /// </summary>
    public void EvaluateFrame(int frameIndex, PoseFrame targetFrame, HandPoseComparator.SimilarityResult comparisonResult)
    {
        if (!isEvaluating || !enableEvaluation) return;

        currentFrameIndex = frameIndex;

        // 현재 구간 찾기
        TunaMotionSegment currentSegment = GetSegmentForFrame(frameIndex);
        if (currentSegment == null) return;

        // 1. 경로 준수도 체크
        if (currentSegment.requirePathFollowing && frameIndex % currentSegment.pathCheckInterval == 0)
        {
            EvaluatePathCompliance(frameIndex, targetFrame, comparisonResult);
        }

        // 2. 안전 범위 체크
        if (currentSegment.checkSafetyLimits)
        {
            EvaluateSafety(frameIndex, currentSegment, targetFrame);
        }

        // 3. 체크포인트 평가
        if (currentSegment.isCheckpoint)
        {
            EvaluateCheckpoint(frameIndex, currentSegment, comparisonResult);
        }

        // 이전 프레임 데이터 저장
        StorePreviousFrameData();
    }

    /// <summary>
    /// 경로 준수도 평가
    /// </summary>
    private void EvaluatePathCompliance(int frameIndex, PoseFrame targetFrame, HandPoseComparator.SimilarityResult comparisonResult)
    {
        currentResult.score.totalFrames++;

        // 왼손 경로 체크
        float leftDeviation = comparisonResult.leftHandPositionError;
        TunaMotionSegment segment = GetSegmentForFrame(frameIndex);

        if (leftDeviation <= segment.pathTolerance)
        {
            currentResult.score.framesOnPath++;
        }
        else
        {
            // 경로 이탈 기록
            currentResult.pathDeviations.Add(new PathDeviation
            {
                frameIndex = frameIndex,
                handType = "Left",
                deviation = leftDeviation,
                timestamp = Time.time - currentResult.startTime
            });

            if (showDebugLogs)
            {
                Debug.LogWarning($"[TunaEvaluator] 프레임 {frameIndex}: 왼손 경로 이탈 {leftDeviation * 100:F1}cm");
            }
        }

        // 오른손 경로 체크
        float rightDeviation = comparisonResult.rightHandPositionError;
        if (rightDeviation > segment.pathTolerance)
        {
            currentResult.pathDeviations.Add(new PathDeviation
            {
                frameIndex = frameIndex,
                handType = "Right",
                deviation = rightDeviation,
                timestamp = Time.time - currentResult.startTime
            });

            if (showDebugLogs)
            {
                Debug.LogWarning($"[TunaEvaluator] 프레임 {frameIndex}: 오른손 경로 이탈 {rightDeviation * 100:F1}cm");
            }
        }
    }

    /// <summary>
    /// 안전성 평가 (제한장벽 체크)
    /// </summary>
    private void EvaluateSafety(int frameIndex, TunaMotionSegment segment, PoseFrame targetFrame)
    {
        if (!hasPreviousFrame) return;

        // 왼손 회전 체크
        if (playerLeftHand != null && playerLeftHand.Hand != null && playerLeftHand.Hand.IsTrackedDataValid)
        {
            Transform leftWrist = playerLeftHand.Joints[(int)HandJointId.HandWristRoot];
            if (leftWrist != null)
            {
                float rotationDelta = Quaternion.Angle(initialLeftHandRotation, leftWrist.rotation);

                if (rotationDelta > segment.leftHandMaxRotation)
                {
                    RecordSafetyViolation(frameIndex, "Left", "RotationExceeded", rotationDelta, segment.leftHandMaxRotation);
                }

                // 위치 이동 체크
                float distanceMoved = Vector3.Distance(initialLeftHandPosition, leftWrist.position);
                if (distanceMoved > segment.leftHandMaxDistance)
                {
                    RecordSafetyViolation(frameIndex, "Left", "DistanceExceeded", distanceMoved, segment.leftHandMaxDistance);
                }
            }
        }

        // 오른손 회전 체크
        if (playerRightHand != null && playerRightHand.Hand != null && playerRightHand.Hand.IsTrackedDataValid)
        {
            Transform rightWrist = playerRightHand.Joints[(int)HandJointId.HandWristRoot];
            if (rightWrist != null)
            {
                float rotationDelta = Quaternion.Angle(initialRightHandRotation, rightWrist.rotation);

                if (rotationDelta > segment.rightHandMaxRotation)
                {
                    RecordSafetyViolation(frameIndex, "Right", "RotationExceeded", rotationDelta, segment.rightHandMaxRotation);
                }

                // 위치 이동 체크
                float distanceMoved = Vector3.Distance(initialRightHandPosition, rightWrist.position);
                if (distanceMoved > segment.rightHandMaxDistance)
                {
                    RecordSafetyViolation(frameIndex, "Right", "DistanceExceeded", distanceMoved, segment.rightHandMaxDistance);
                }
            }
        }
    }

    /// <summary>
    /// 체크포인트 평가
    /// </summary>
    private void EvaluateCheckpoint(int frameIndex, TunaMotionSegment segment, HandPoseComparator.SimilarityResult comparisonResult)
    {
        float avgSimilarity = (comparisonResult.leftHandSimilarity + comparisonResult.rightHandSimilarity) / 2f;

        // 유사도 체크
        if (avgSimilarity >= segment.checkpointSimilarityThreshold)
        {
            if (!isHoldingCheckpoint)
            {
                isHoldingCheckpoint = true;
                checkpointHoldTimer = 0f;
            }

            checkpointHoldTimer += Time.deltaTime;

            // 필요 시간 달성
            if (checkpointHoldTimer >= segment.requiredHoldTime)
            {
                RecordCheckpointSuccess(segment, avgSimilarity, checkpointHoldTimer);
                isHoldingCheckpoint = false;
                checkpointHoldTimer = 0f;
            }
        }
        else
        {
            // 유사도 부족 - 타이머 리셋
            if (isHoldingCheckpoint)
            {
                isHoldingCheckpoint = false;
                checkpointHoldTimer = 0f;
            }
        }
    }

    /// <summary>
    /// 안전 위반 기록
    /// </summary>
    private void RecordSafetyViolation(int frameIndex, string handType, string violationType, float actualValue, float limitValue)
    {
        // 중복 기록 방지 (같은 프레임, 같은 손, 같은 위반 유형)
        bool alreadyRecorded = currentResult.safetyViolations.Exists(v =>
            v.frameIndex == frameIndex && v.handType == handType && v.violationType == violationType);

        if (alreadyRecorded) return;

        currentResult.safetyViolations.Add(new SafetyViolation
        {
            frameIndex = frameIndex,
            handType = handType,
            violationType = violationType,
            actualValue = actualValue,
            limitValue = limitValue,
            timestamp = Time.time - currentResult.startTime
        });

        currentResult.score.safetyViolations++;

        // UI 경고 표시
        if (evaluationUI != null)
        {
            string warningMessage = $"{handType} {violationType} - {actualValue:F1}° (한계: {limitValue:F1}°)";
            if (violationType == "DistanceExceeded")
            {
                warningMessage = $"{handType} 거리 초과 - {actualValue * 100:F1}cm (한계: {limitValue * 100:F1}cm)";
            }
            evaluationUI.ShowSafetyWarning(warningMessage);
        }

        if (showDebugLogs)
        {
            Debug.LogError($"[TunaEvaluator] 안전 위반! 프레임 {frameIndex}: {handType} {violationType} - {actualValue:F1} (한계: {limitValue:F1})");
        }
    }

    /// <summary>
    /// 체크포인트 성공 기록
    /// </summary>
    private void RecordCheckpointSuccess(TunaMotionSegment segment, float similarity, float holdTime)
    {
        // 중복 기록 방지
        bool alreadyPassed = currentResult.checkpointResults.Exists(c => c.segmentName == segment.segmentName);
        if (alreadyPassed) return;

        currentResult.checkpointResults.Add(new CheckpointResult
        {
            segmentName = segment.segmentName,
            frameIndex = currentFrameIndex,
            similarity = similarity,
            holdTime = holdTime,
            passed = true,
            scoreEarned = 0 // 나중에 계산
        });

        currentResult.score.checkpointsPassed++;
        currentResult.score.totalHoldTime += holdTime;

        // UI 알림 표시
        if (evaluationUI != null)
        {
            evaluationUI.ShowCheckpointPassed(segment.segmentName);
        }

        Debug.Log($"[TunaEvaluator] 체크포인트 통과: {segment.segmentName} (유사도 {similarity * 100:F0}%, 유지 {holdTime:F1}초)");
    }

    /// <summary>
    /// 최종 점수 계산
    /// </summary>
    private void CalculateFinalScore()
    {
        // 1. 경로 준수도 (40점)
        if (currentResult.score.totalFrames > 0)
        {
            float pathRatio = (float)currentResult.score.framesOnPath / currentResult.score.totalFrames;
            currentResult.score.pathComplianceScore = pathRatio * currentResult.score.maxPathScore;
        }

        // 2. 안전성 (30점) - 위반마다 감점
        currentResult.score.safetyScore = currentResult.score.maxSafetyScore;
        foreach (var violation in currentResult.safetyViolations)
        {
            if (violation.violationType == "RotationExceeded")
            {
                currentResult.score.safetyScore -= 5f; // 회전 초과는 -5점
            }
            else if (violation.violationType == "DistanceExceeded")
            {
                currentResult.score.safetyScore -= 3f; // 거리 초과는 -3점
            }
        }
        currentResult.score.safetyScore = Mathf.Max(0f, currentResult.score.safetyScore);

        // 3. 정확도 (20점) - 체크포인트 기반
        currentResult.score.totalCheckpoints = motionSegments.FindAll(s => s.isCheckpoint).Count;
        if (currentResult.score.totalCheckpoints > 0)
        {
            float checkpointRatio = (float)currentResult.score.checkpointsPassed / currentResult.score.totalCheckpoints;
            currentResult.score.accuracyScore = checkpointRatio * currentResult.score.maxAccuracyScore;

            // 체크포인트별 점수 계산
            float scorePerCheckpoint = currentResult.score.maxAccuracyScore / currentResult.score.totalCheckpoints;
            foreach (var checkpoint in currentResult.checkpointResults)
            {
                checkpoint.scoreEarned = scorePerCheckpoint;
            }
        }

        // 4. 안정성 (10점) - 유지 시간 기반
        currentResult.score.requiredHoldTime = 0f;
        foreach (var segment in motionSegments)
        {
            if (segment.isCheckpoint)
            {
                currentResult.score.requiredHoldTime += segment.requiredHoldTime;
            }
        }

        if (currentResult.score.requiredHoldTime > 0)
        {
            float holdRatio = Mathf.Min(1f, currentResult.score.totalHoldTime / currentResult.score.requiredHoldTime);
            currentResult.score.stabilityScore = holdRatio * currentResult.score.maxStabilityScore;
        }
    }

    /// <summary>
    /// 현재 프레임에 해당하는 구간 찾기
    /// </summary>
    private TunaMotionSegment GetSegmentForFrame(int frameIndex)
    {
        foreach (var segment in motionSegments)
        {
            if (frameIndex >= segment.startFrame && frameIndex <= segment.endFrame)
            {
                return segment;
            }
        }
        return null;
    }

    /// <summary>
    /// 초기 위치 저장
    /// </summary>
    private void StoreInitialPositions()
    {
        if (playerLeftHand != null && playerLeftHand.Hand != null && playerLeftHand.Hand.IsTrackedDataValid)
        {
            Transform leftWrist = playerLeftHand.Joints[(int)HandJointId.HandWristRoot];
            if (leftWrist != null)
            {
                initialLeftHandPosition = leftWrist.position;
                initialLeftHandRotation = leftWrist.rotation;
            }
        }

        if (playerRightHand != null && playerRightHand.Hand != null && playerRightHand.Hand.IsTrackedDataValid)
        {
            Transform rightWrist = playerRightHand.Joints[(int)HandJointId.HandWristRoot];
            if (rightWrist != null)
            {
                initialRightHandPosition = rightWrist.position;
                initialRightHandRotation = rightWrist.rotation;
            }
        }
    }

    /// <summary>
    /// 이전 프레임 데이터 저장
    /// </summary>
    private void StorePreviousFrameData()
    {
        if (playerLeftHand != null && playerLeftHand.Hand != null && playerLeftHand.Hand.IsTrackedDataValid)
        {
            Transform leftWrist = playerLeftHand.Joints[(int)HandJointId.HandWristRoot];
            if (leftWrist != null)
            {
                prevLeftHandPosition = leftWrist.position;
                prevLeftHandRotation = leftWrist.rotation;
            }
        }

        if (playerRightHand != null && playerRightHand.Hand != null && playerRightHand.Hand.IsTrackedDataValid)
        {
            Transform rightWrist = playerRightHand.Joints[(int)HandJointId.HandWristRoot];
            if (rightWrist != null)
            {
                prevRightHandPosition = rightWrist.position;
                prevRightHandRotation = rightWrist.rotation;
            }
        }

        hasPreviousFrame = true;
    }

    /// <summary>
    /// 현재 평가 결과 가져오기
    /// </summary>
    public EvaluationResult GetCurrentResult()
    {
        return currentResult;
    }

    /// <summary>
    /// 평가 중인지 확인
    /// </summary>
    public bool IsEvaluating()
    {
        return isEvaluating;
    }

    /// <summary>
    /// 구간 추가 (에디터/코드에서 사용)
    /// </summary>
    public void AddSegment(TunaMotionSegment segment)
    {
        motionSegments.Add(segment);
    }

    /// <summary>
    /// 기본 구간 생성 (테스트용)
    /// </summary>
    [ContextMenu("Create Default Segments")]
    public void CreateDefaultSegments()
    {
        motionSegments.Clear();

        // 구간 1: 시작 (체크포인트)
        motionSegments.Add(new TunaMotionSegment
        {
            segmentName = "시작 자세",
            startFrame = 0,
            endFrame = 10,
            isCheckpoint = true,
            requiredHoldTime = 2f,
            leftHandMaxRotation = 30f,
            rightHandMaxRotation = 30f,
            pathTolerance = 0.05f
        });

        // 구간 2: 진행
        motionSegments.Add(new TunaMotionSegment
        {
            segmentName = "시술 진행",
            startFrame = 11,
            endFrame = 30,
            isCheckpoint = false,
            leftHandMaxRotation = 45f,
            rightHandMaxRotation = 45f,
            pathTolerance = 0.05f
        });

        // 구간 3: 종료 (체크포인트)
        motionSegments.Add(new TunaMotionSegment
        {
            segmentName = "종료 자세",
            startFrame = 31,
            endFrame = 40,
            isCheckpoint = true,
            requiredHoldTime = 2f,
            leftHandMaxRotation = 30f,
            rightHandMaxRotation = 30f,
            pathTolerance = 0.05f
        });

        Debug.Log("[TunaEvaluator] 기본 구간 생성 완료");
    }
}
