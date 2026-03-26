using System;
using UnityEngine;

/// <summary>
/// Phase-based evaluation manager for ChunaPathEvaluator.
/// Manages evaluation flow: WaitingForStart -> StartHold -> Moving -> MidHold -> Completed.
/// GuideOnly mode: WaitingForStart -> Moving -> Completed (홀드 스킵, 유사도 비평가, 시각 데모 전용).
/// SkipMidHold mode: WaitingForStart -> StartHold -> Moving -> Completed (유사도 평가 O, 임계점 통과 시 즉시 완료).
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
        bool useRelativeMovement, bool startHoldOnly, bool guideOnlyMode, bool skipMidHold, bool isGuideMode,
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
                UpdateWaitingForStart(leftPos, rightPos, isLeftTouching, isRightTouching,
                    guideOnlyMode, useRelativeMovement, currentStartRatio, showDebugLogs);
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
                    leftHandDriftThreshold, guideOnlyMode, skipMidHold, isGuideMode, showDebugLogs);
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
        bool isLeftTouching, bool isRightTouching,
        bool guideOnlyMode, bool useRelativeMovement,
        float currentStartRatio, bool showDebugLogs)
    {
        bool shouldProceed = isLeftTouching || isRightTouching;

        if (showDebugLogs && Time.frameCount % 60 == 0)
            ChunaLogger.Log($"[WaitingForStart-Collision] left:{isLeftTouching}, right:{isRightTouching}");

        if (shouldProceed)
        {
            leftHandStartHoldPosition = leftPos;
            owner.OnWaitingForStartComplete();

            if (guideOnlyMode)
            {
                // ★ GuideOnly: StartHold 스킵 → 바로 Moving으로
                ChunaLogger.Log("<color=green>[WaitingForStart] GuideOnly - StartHold 스킵, 바로 Moving 전환</color>");

                // 피벗 기반 진행률 계산을 위해 기준점 저장
                if (useRelativeMovement)
                {
                    owner.SaveUserHoldReference();
                }

                owner.StartGuideHandPlaybackInternal();
                ChangePhase(ChunaPathEvaluator.EvaluationPhase.Moving, owner.GetLoadedFrameCount(), currentStartRatio, showDebugLogs);
            }
            else
            {
                ChunaLogger.Log("<color=green>[WaitingForStart] Hand detected! Transition to StartHold</color>");
                ChangePhase(ChunaPathEvaluator.EvaluationPhase.StartHold, owner.GetLoadedFrameCount(), owner.CurrentStartRatio, showDebugLogs);
            }
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
        float leftHandDriftThreshold, bool guideOnlyMode, bool skipMidHold, bool isGuideMode, bool showDebugLogs)
    {
        // Left hand drift check (guideOnly에서는 스킵)
        if (!guideOnlyMode)
        {
            float leftDrift = Vector3.Distance(leftPos, leftHandStartHoldPosition);
            if (leftDrift > leftHandDriftThreshold)
            {
                owner.FireOnLeftHandDrifted(leftDrift);
                owner.IncrementLeftHandDriftCount();

                if (showDebugLogs)
                    ChunaLogger.Log($"<color=orange>[Moving] Left hand drift! Distance: {leftDrift:F3}m</color>");
            }
        }

        float progress = owner.GetCurrentProgress();
        float limitRatio = currentMidHoldEnd;

        if (guideOnlyMode)
        {
            // ★ GuideOnly: 목표 범위 도달 시 MidHold 없이 바로 완료
            if (progress >= currentMidHoldStart)
            {
                ChunaLogger.Log($"<color=green>[Moving] GuideOnly - 목표 도달 ({progress:P0}), 바로 완료</color>");
                ChangePhase(ChunaPathEvaluator.EvaluationPhase.Completed, owner.GetLoadedFrameCount(), owner.CurrentStartRatio, showDebugLogs);

                if (isGuideMode)
                {
                    ChunaLogger.Log("<color=magenta>[Moving] GuideOnly + Guide mode - 토글 대기</color>");
                    return;
                }

                owner.CompleteEvaluation();
            }

            if (showDebugLogs && Time.frameCount % 60 == 0)
                ChunaLogger.Log($"[Moving-GuideOnly] Progress: {progress:P0}, target:{currentMidHoldStart:P0}");

            return;
        }

        // ===== SkipMidHold 모드 (유사도 평가 O, MidHold 스킵 - 대흉근/흉쇄유돌근) =====
        if (skipMidHold)
        {
            if (progress >= currentMidHoldStart)
            {
                ChunaLogger.Log($"<color=green>[Moving] SkipMidHold - 임계점 통과 ({progress:P0} >= {currentMidHoldStart:P0}), 완료</color>");
                ChangePhase(ChunaPathEvaluator.EvaluationPhase.Completed, owner.GetLoadedFrameCount(), owner.CurrentStartRatio, showDebugLogs);

                if (isGuideMode)
                {
                    ChunaLogger.Log("<color=magenta>[Moving] SkipMidHold + Guide mode - 토글 대기</color>");
                    return;
                }

                owner.CompleteEvaluation();
            }

            if (showDebugLogs && Time.frameCount % 60 == 0)
                ChunaLogger.Log($"[Moving-SkipMidHold] Progress: {progress:P0}, threshold:{currentMidHoldStart:P0}");

            return;
        }

        // ===== 일반 모드 (기존 로직) =====

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
            ChunaLogger.Log($"[Moving] Progress: {progress:P0}, limit:{limitRatio:P0}, overLimit:{isOverLimitBarrier}");
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
