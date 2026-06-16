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

    // MidHold 진행도 속도 추적 (훑고 지나가기 방지)
    private float lastProgressForVelocity = 0f;
    private bool progressVelocityInitialized = false;

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
        float pauseProgressVelocity,
        float startHoldDuration, float midHoldDuration,
        float currentMidHoldStart, float currentMidHoldEnd,
        float leftHandDriftThreshold,
        float currentStartRatio,
        bool useRelativeMovement, bool startHoldOnly, bool guideOnlyMode, bool skipMidHold, bool isGuideMode,
        bool isBothHandsMode,
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
                    leftHandDriftThreshold, guideOnlyMode, skipMidHold, isGuideMode, isBothHandsMode, showDebugLogs);
                break;

            case ChunaPathEvaluator.EvaluationPhase.MidHold:
                UpdateMidHold(leftPos, rightPos,
                    pauseProgressVelocity, midHoldDuration,
                    currentMidHoldStart, currentMidHoldEnd,
                    leftHandDriftThreshold, isGuideMode, isBothHandsMode, showDebugLogs);
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

        // MidHold 진입 시 진행도 속도 기준점 초기화 (첫 프레임 delta=0 보장)
        if (newPhase == ChunaPathEvaluator.EvaluationPhase.MidHold)
        {
            lastProgressForVelocity = owner.GetCurrentProgress();
            progressVelocityInitialized = true;
        }
        else
        {
            progressVelocityInitialized = false;
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

            // guideOnly 포함 모든 모드에서 StartHold 거침
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
        float leftHandDriftThreshold, bool guideOnlyMode, bool skipMidHold, bool isGuideMode, bool isBothHandsMode, bool showDebugLogs)
    {
        // Left hand drift check (guideOnly / 양손 회전에서는 스킵)
        if (!guideOnlyMode && !isBothHandsMode)
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
            // ★ GuideOnly: 진행률 95% 이상 도달 시 완료
            if (progress >= 0.95f)
            {
                ChunaLogger.Log($"<color=green>[Moving] GuideOnly - 진행률 달성 ({progress:P0} >= 95%), 완료</color>");
                ChangePhase(ChunaPathEvaluator.EvaluationPhase.Completed, owner.GetLoadedFrameCount(), owner.CurrentStartRatio, showDebugLogs);

                if (isGuideMode)
                {
                    ChunaLogger.Log("<color=magenta>[Moving] GuideOnly + Guide mode - 토글 대기</color>");
                    return;
                }

                owner.CompleteEvaluation();
            }

            if (showDebugLogs && Time.frameCount % 60 == 0)
                ChunaLogger.Log($"[Moving-GuideOnly] Progress: {progress:P0}");

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
            ChunaLogger.Log($"<color=green>[Moving→MidHold] 적정범위 진입! progress={progress:P2}, holdStart={currentMidHoldStart:P2}, holdEnd={limitRatio:P2}</color>");
            owner.FireOnMidHoldBegin();
            ChangePhase(ChunaPathEvaluator.EvaluationPhase.MidHold, owner.GetLoadedFrameCount(), owner.CurrentStartRatio, showDebugLogs);
        }

        if (showDebugLogs && Time.frameCount % 60 == 0)
            ChunaLogger.Log($"[Moving] progress={progress:P2}, holdRange={currentMidHoldStart:P2}~{limitRatio:P2}, overLimit:{isOverLimitBarrier}");
    }

    private void UpdateMidHold(Vector3 leftPos, Vector3 rightPos,
        float pauseProgressVelocity, float midHoldDuration,
        float currentMidHoldStart, float currentMidHoldEnd,
        float leftHandDriftThreshold, bool isGuideMode, bool isBothHandsMode, bool showDebugLogs)
    {
        // 양손 회전 모드에서는 보조수 드리프트 체크 생략
        float leftDrift = isBothHandsMode ? 0f : Vector3.Distance(leftPos, leftHandStartHoldPosition);
        bool leftOk = isBothHandsMode || leftDrift <= leftHandDriftThreshold;
        float progress = owner.GetCurrentProgress();
        bool rightInRange = progress >= currentMidHoldStart && progress <= currentMidHoldEnd;

        // ★ 초과 경고: MidHold 진입 후 적정범위 초과 시 — UpdateMoving과 동일 처리
        //   (이 누락이 "초과 후 복귀 시 경고음 미변경" 버그의 원인이었음)
        if (progress > currentMidHoldEnd)
        {
            if (!isOverLimitBarrier)
            {
                isOverLimitBarrier = true;
                owner.IncrementLimitWarningCount();
                ChunaLogger.Log($"<color=red>[MidHold] Over limit! (warning #{owner.GetLimitWarningCount()})</color>");
            }
            owner.FireOnLimitWarning(progress);
        }
        else if (isOverLimitBarrier)
        {
            isOverLimitBarrier = false;
            ChunaLogger.Log($"<color=green>[MidHold] Returned to safe range. Warning cleared.</color>");
        }

        // 진행도 속도 계산 (ratio/s)
        if (!progressVelocityInitialized)
        {
            lastProgressForVelocity = progress;
            progressVelocityInitialized = true;
        }
        float progressVel = Time.deltaTime > 0f ? Mathf.Abs(progress - lastProgressForVelocity) / Time.deltaTime : 0f;
        lastProgressForVelocity = progress;

        bool progressSlow = progressVel < pauseProgressVelocity;

        // 범위 이탈 / 왼손 드리프트 → 리셋 (홀드 무효)
        if (!rightInRange || !leftOk)
        {
            if (phaseHoldTime > 0.1f && showDebugLogs)
            {
                string reason = !leftOk ? "left hand drifted" : "out of range";
                ChunaLogger.Log($"<color=orange>[MidHold] Hold reset: {reason}</color>");
            }
            phaseHoldTime = 0f;
            owner.FireOnHoldProgressChanged(0f, midHoldDuration);
            return;
        }

        // 범위 안 + 진행도 속도 과다 → 일시정지 (phaseHoldTime 보존)
        if (!progressSlow)
        {
            if (showDebugLogs && Time.frameCount % 30 == 0)
                ChunaLogger.Log($"<color=yellow>[MidHold] Paused (progressVel={progressVel:F3}/s >= {pauseProgressVelocity:F3}/s), held={phaseHoldTime:F1}s</color>");
            owner.FireOnHoldProgressChanged(phaseHoldTime, midHoldDuration);
            return;
        }

        // 타이머 진행
        phaseHoldTime += Time.deltaTime;
        owner.FireOnHoldProgressChanged(phaseHoldTime, midHoldDuration);

        if (showDebugLogs && Time.frameCount % 30 == 0)
            ChunaLogger.Log($"[MidHold] Hold progress: {phaseHoldTime:F1}s (progressVel={progressVel:F3}/s)");

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
