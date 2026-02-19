using System;
using UnityEngine;

/// <summary>
/// Phase-based evaluation manager for ChunaPathEvaluator.
/// Manages evaluation flow: WaitingForStart -> StartHold -> Moving -> MidHold -> Completed.
/// Takes ChunaPathEvaluator reference to access internal members.
/// </summary>
public class EvaluationPhaseManager
{
    private readonly ChunaPathEvaluator owner;

    public EvaluationPhaseManager(ChunaPathEvaluator owner)
    {
        this.owner = owner;
    }

    // Phase state
    private ChunaPathEvaluator.EvaluationPhase currentPhase = ChunaPathEvaluator.EvaluationPhase.Idle;
    private float phaseHoldTime = 0f;
    private Vector3 leftHandStartHoldPosition;
    private bool isOverLimitBarrier = false;

    // Hand velocity tracking
    private Vector3 lastLeftHandPosition;
    private Vector3 lastRightHandPosition;

    // Properties
    public ChunaPathEvaluator.EvaluationPhase CurrentPhase => currentPhase;
    public Vector3 LeftHandStartHoldPosition => leftHandStartHoldPosition;

    /// <summary>
    /// Initialize phase manager for a new evaluation.
    /// </summary>
    public void Initialize(Vector3 leftHandPos, Vector3 rightHandPos)
    {
        currentPhase = ChunaPathEvaluator.EvaluationPhase.WaitingForStart;
        phaseHoldTime = 0f;
        leftHandStartHoldPosition = Vector3.zero;
        isOverLimitBarrier = false;
        lastLeftHandPosition = leftHandPos;
        lastRightHandPosition = rightHandPos;
    }

    /// <summary>
    /// Update the phase-based evaluation. Call this every frame.
    /// </summary>
    public void UpdatePhaseEvaluation(
        Vector3 leftPos, Vector3 rightPos,
        bool isLeftTouching, bool isRightTouching,
        float holdVelocityThreshold,
        float startHoldDuration, float midHoldDuration,
        float currentMidHoldStart, float currentMidHoldEnd,
        float leftHandDriftThreshold,
        float currentStartRatio,
        bool useRelativeMovement, bool startHoldOnly, bool isGuideMode,
        bool showDebugLogs)
    {
        // Calculate velocities
        float leftVelocity = Time.deltaTime > 0 ? (leftPos - lastLeftHandPosition).magnitude / Time.deltaTime : 0f;
        float rightVelocity = Time.deltaTime > 0 ? (rightPos - lastRightHandPosition).magnitude / Time.deltaTime : 0f;
        lastLeftHandPosition = leftPos;
        lastRightHandPosition = rightPos;

        switch (currentPhase)
        {
            case ChunaPathEvaluator.EvaluationPhase.WaitingForStart:
                UpdateWaitingForStart(leftPos, rightPos, isLeftTouching, isRightTouching, showDebugLogs);
                break;

            case ChunaPathEvaluator.EvaluationPhase.StartHold:
                UpdateStartHold(leftPos, rightPos, leftVelocity, rightVelocity,
                    isLeftTouching, isRightTouching,
                    holdVelocityThreshold, startHoldDuration,
                    useRelativeMovement, startHoldOnly, isGuideMode,
                    currentStartRatio, showDebugLogs);
                break;

            case ChunaPathEvaluator.EvaluationPhase.Moving:
                UpdateMoving(leftPos, rightPos, currentMidHoldStart, currentMidHoldEnd,
                    leftHandDriftThreshold, isGuideMode, showDebugLogs);
                break;

            case ChunaPathEvaluator.EvaluationPhase.MidHold:
                UpdateMidHold(leftPos, rightPos, rightVelocity,
                    holdVelocityThreshold, midHoldDuration,
                    currentMidHoldStart, currentMidHoldEnd,
                    leftHandDriftThreshold, isGuideMode, showDebugLogs);
                break;
        }
    }

    /// <summary>
    /// Change to a new phase.
    /// </summary>
    public void ChangePhase(ChunaPathEvaluator.EvaluationPhase newPhase, int loadedFrameCount, float currentStartRatio, bool showDebugLogs)
    {
        if (currentPhase == newPhase) return;

        var oldPhase = currentPhase;
        currentPhase = newPhase;
        phaseHoldTime = 0f;

        // Initialize frame index on Moving phase start
        if (newPhase == ChunaPathEvaluator.EvaluationPhase.Moving)
        {
            owner.InitializeMovingPhaseFrame(currentStartRatio, loadedFrameCount);
        }

        if (showDebugLogs)
        {
            ChunaLogger.Log($"<color=cyan>[ChunaPathEvaluator] Phase change: {oldPhase} -> {newPhase}</color>");
        }

        owner.FireOnPhaseChanged(newPhase);

        if (showDebugLogs)
        {
            switch (newPhase)
            {
                case ChunaPathEvaluator.EvaluationPhase.WaitingForStart:
                    ChunaLogger.Log("<color=yellow>Move to start position!</color>");
                    break;
                case ChunaPathEvaluator.EvaluationPhase.StartHold:
                    ChunaLogger.Log("<color=yellow>Hold at start position for 2 seconds!</color>");
                    break;
                case ChunaPathEvaluator.EvaluationPhase.Moving:
                    ChunaLogger.Log("<color=green>Follow the guide!</color>");
                    break;
                case ChunaPathEvaluator.EvaluationPhase.MidHold:
                    ChunaLogger.Log("<color=yellow>Hold at midpoint for 3 seconds!</color>");
                    break;
                case ChunaPathEvaluator.EvaluationPhase.Completed:
                    ChunaLogger.Log("<color=green>Evaluation complete!</color>");
                    break;
            }
        }
    }

    // ========== Phase update methods ==========

    private void UpdateWaitingForStart(Vector3 leftPos, Vector3 rightPos,
        bool isLeftTouching, bool isRightTouching, bool showDebugLogs)
    {
        bool shouldProceed = isLeftTouching || isRightTouching;

        if (showDebugLogs && Time.frameCount % 60 == 0)
            ChunaLogger.Log($"[WaitingForStart-Collision] left:{isLeftTouching}, right:{isRightTouching}");

        if (shouldProceed)
        {
            leftHandStartHoldPosition = leftPos;
            owner.OnWaitingForStartComplete();

            ChunaLogger.Log("<color=green>[WaitingForStart] Hand detected! Transition to StartHold</color>");
            ChangePhase(ChunaPathEvaluator.EvaluationPhase.StartHold, owner.GetLoadedFrameCount(), owner.CurrentStartRatio, showDebugLogs);
        }
    }

    private void UpdateStartHold(Vector3 leftPos, Vector3 rightPos, float leftVel, float rightVel,
        bool isLeftTouching, bool isRightTouching,
        float holdVelocityThreshold, float startHoldDuration,
        bool useRelativeMovement, bool startHoldOnly, bool isGuideMode,
        float currentStartRatio, bool showDebugLogs)
    {
        bool bothStopped = leftVel < holdVelocityThreshold && rightVel < holdVelocityThreshold;
        bool positionOk = isLeftTouching || isRightTouching;

        if (showDebugLogs && Time.frameCount % 10 == 0)
            ChunaLogger.Log($"[StartHold-Collision] stopped:{bothStopped}, touching:{positionOk}, hold:{phaseHoldTime:F1}s");

        if (bothStopped && positionOk)
        {
            phaseHoldTime += Time.deltaTime;
            owner.FireOnHoldProgressChanged(phaseHoldTime, startHoldDuration);

            if (phaseHoldTime >= startHoldDuration)
            {
                owner.FireOnHoldCompleted();
                owner.FireOnStartHoldComplete();

                if (startHoldOnly)
                {
                    ChunaLogger.Log("<color=green>[StartHold] Hold complete! (StartHold-only mode)</color>");
                    ChangePhase(ChunaPathEvaluator.EvaluationPhase.Completed, owner.GetLoadedFrameCount(), currentStartRatio, showDebugLogs);

                    if (isGuideMode)
                    {
                        ChunaLogger.Log("<color=magenta>[StartHold] Guide mode - proceed with toggle</color>");
                        return;
                    }

                    owner.CompleteEvaluation();
                    return;
                }

                ChunaLogger.Log("<color=green>[StartHold] Hold complete! Moving to Moving phase</color>");

                if (useRelativeMovement)
                {
                    owner.SaveUserHoldReference();
                }

                owner.StartGuideHandPlaybackInternal();
                ChangePhase(ChunaPathEvaluator.EvaluationPhase.Moving, owner.GetLoadedFrameCount(), currentStartRatio, showDebugLogs);
            }
        }
        else
        {
            if (phaseHoldTime > 0.1f && showDebugLogs)
                ChunaLogger.Log($"<color=orange>[StartHold] Hold interrupted</color>");

            phaseHoldTime = 0f;
            owner.FireOnHoldProgressChanged(0f, startHoldDuration);
        }
    }

    private void UpdateMoving(Vector3 leftPos, Vector3 rightPos,
        float currentMidHoldStart, float currentMidHoldEnd,
        float leftHandDriftThreshold, bool isGuideMode, bool showDebugLogs)
    {
        // Left hand drift check
        float leftDrift = Vector3.Distance(leftPos, leftHandStartHoldPosition);
        if (leftDrift > leftHandDriftThreshold)
        {
            owner.FireOnLeftHandDrifted(leftDrift);
            owner.IncrementLeftHandDriftCount();

            if (showDebugLogs)
                ChunaLogger.Log($"<color=orange>[Moving] Left hand drift! Distance: {leftDrift:F3}m</color>");
        }

        float progress = owner.GetCurrentProgress();
        float limitRatio = currentMidHoldEnd;

        // Over-limit warning
        if (progress > limitRatio)
        {
            if (!isOverLimitBarrier)
            {
                isOverLimitBarrier = true;
                owner.IncrementLimitWarningCount();
                ChunaLogger.Log($"<color=red>[Moving] Over limit! Go back! (warning #{owner.GetLimitWarningCount()})</color>");
            }

            owner.FireOnLimitWarning(progress);

            if (showDebugLogs && Time.frameCount % 30 == 0)
                ChunaLogger.Log($"<color=red>[Moving] Warning active! Progress: {progress:P0}</color>");
        }
        else
        {
            if (isOverLimitBarrier)
            {
                isOverLimitBarrier = false;
                ChunaLogger.Log($"<color=green>[Moving] Returned below limit. Warning cleared.</color>");
            }
        }

        // Mid hold zone check
        if (progress >= currentMidHoldStart && progress <= limitRatio)
        {
            owner.FireOnMidHoldBegin();
            ChangePhase(ChunaPathEvaluator.EvaluationPhase.MidHold, owner.GetLoadedFrameCount(), owner.CurrentStartRatio, showDebugLogs);
        }

        if (showDebugLogs && Time.frameCount % 60 == 0)
            ChunaLogger.Log($"[Moving] Progress: {progress:P0}, limit:{limitRatio:P0}, leftDrift: {leftDrift:F3}m, overLimit:{isOverLimitBarrier}");
    }

    private void UpdateMidHold(Vector3 leftPos, Vector3 rightPos, float rightVel,
        float holdVelocityThreshold, float midHoldDuration,
        float currentMidHoldStart, float currentMidHoldEnd,
        float leftHandDriftThreshold, bool isGuideMode, bool showDebugLogs)
    {
        bool rightStopped = rightVel < holdVelocityThreshold;
        float leftDrift = Vector3.Distance(leftPos, leftHandStartHoldPosition);
        bool leftOk = leftDrift <= leftHandDriftThreshold;
        float progress = owner.GetCurrentProgress();
        bool rightInRange = progress >= currentMidHoldStart && progress <= currentMidHoldEnd;

        if (rightStopped && leftOk && rightInRange)
        {
            phaseHoldTime += Time.deltaTime;
            owner.FireOnHoldProgressChanged(phaseHoldTime, midHoldDuration);

            if (showDebugLogs && Time.frameCount % 30 == 0)
                ChunaLogger.Log($"[MidHold] Hold progress: {phaseHoldTime:F0}s");

            if (phaseHoldTime >= midHoldDuration)
            {
                owner.FireOnMidHoldComplete();
                owner.FireOnHoldCompleted();

                if (isGuideMode)
                {
                    ChunaLogger.Log("<color=magenta>[MidHold] Guide mode - proceed with toggle</color>");
                    ChangePhase(ChunaPathEvaluator.EvaluationPhase.Completed, owner.GetLoadedFrameCount(), owner.CurrentStartRatio, showDebugLogs);
                    return;
                }

                ChangePhase(ChunaPathEvaluator.EvaluationPhase.Completed, owner.GetLoadedFrameCount(), owner.CurrentStartRatio, showDebugLogs);
                owner.CompleteEvaluation();
            }
        }
        else
        {
            if (phaseHoldTime > 0.1f)
            {
                string reason = !rightStopped ? "right hand moving" :
                               (!leftOk ? "left hand drifted" : "out of range");
                if (showDebugLogs)
                    ChunaLogger.Log($"<color=orange>[MidHold] Hold interrupted: {reason}</color>");
            }

            phaseHoldTime = 0f;
            owner.FireOnHoldProgressChanged(0f, midHoldDuration);
        }
    }

    /// <summary>
    /// Reset phase to idle.
    /// </summary>
    public void Reset()
    {
        currentPhase = ChunaPathEvaluator.EvaluationPhase.Idle;
        phaseHoldTime = 0f;
        isOverLimitBarrier = false;
    }
}
