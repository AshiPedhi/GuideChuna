using System;
using UnityEngine;

/// <summary>
/// AutoPlay mode handler for ChunaPathEvaluator.
/// Manages auto-play state with independent state tracking.
/// </summary>
public class AutoPlayHandler
{
    private readonly ChunaPathEvaluator owner;

    public AutoPlayHandler(ChunaPathEvaluator owner)
    {
        this.owner = owner;
    }

    // State
    private float autoPlayProgress = 0f;
    private float autoPlayDuration = 3f;
    private float autoPlayStartTime = 0f;
    private bool isAutoPlayMode = false;

    // Properties
    public bool IsAutoPlayMode => isAutoPlayMode;
    public float AutoPlayProgress => autoPlayProgress;

    /// <summary>
    /// Start auto play mode.
    /// </summary>
    public void StartAutoPlay(float duration)
    {
        isAutoPlayMode = true;
        autoPlayStartTime = Time.time;
        autoPlayProgress = 0f;

        if (duration > 0f)
        {
            autoPlayDuration = duration;
        }
        else
        {
            autoPlayDuration = 0f;
        }

        string durationStr = autoPlayDuration > 0 ? $"{autoPlayDuration:F1}s" : "on animation complete";
        ChunaLogger.Log($"<color=green>[AutoPlay] Started! Duration:{durationStr}, Animation:{owner.InternalAnimationStateName ?? "none"}</color>");
    }

    /// <summary>
    /// Update auto play mode. Returns true if completed this frame.
    /// </summary>
    public bool UpdateAutoPlay(Animator patientAnimator, bool showDebugLogs)
    {
        float elapsed = Time.time - autoPlayStartTime;

        bool hasAnimation = patientAnimator != null && !string.IsNullOrEmpty(owner.InternalAnimationStateName);

        if (!hasAnimation)
        {
            autoPlayProgress = autoPlayDuration > 0 ? Mathf.Clamp01(elapsed / autoPlayDuration) : 0f;
            owner.FireOnUserFrameChanged(0, 1, autoPlayProgress);

            if (showDebugLogs && Time.frameCount % 60 == 0)
            {
                ChunaLogger.Log($"<color=orange>[AutoPlay] No animation - time-based: {autoPlayProgress:P0} ({elapsed:F1}s / {autoPlayDuration:F1}s)</color>");
            }

            if (elapsed >= autoPlayDuration && autoPlayDuration > 0)
            {
                if (showDebugLogs)
                    ChunaLogger.Log($"<color=green>[AutoPlay] Complete! (elapsed: {elapsed:F1}s)</color>");
                return true; // completed
            }
            return false;
        }

        AnimatorStateInfo stateInfo = patientAnimator.GetCurrentAnimatorStateInfo(0);

        if (patientAnimator.speed != 1f)
        {
            ChunaLogger.LogWarning($"<color=orange>[AutoPlay] Animator speed is {patientAnimator.speed}. Restoring to 1.</color>");
            patientAnimator.speed = 1f;
        }

        int expectedStateHash = Animator.StringToHash(owner.InternalAnimationStateName);
        bool isCorrectState = stateInfo.shortNameHash == expectedStateHash || stateInfo.fullPathHash == expectedStateHash;

        if (autoPlayDuration > 0)
        {
            autoPlayProgress = Mathf.Clamp01(elapsed / autoPlayDuration);
        }
        else if (isCorrectState)
        {
            autoPlayProgress = Mathf.Clamp01(stateInfo.normalizedTime);
        }

        owner.FireOnUserFrameChanged(0, 1, autoPlayProgress);

        if (showDebugLogs && Time.frameCount % 60 == 0)
        {
            string durationInfo = autoPlayDuration > 0 ? $"{autoPlayDuration:F1}s" : "animation length";
            ChunaLogger.Log($"<color=cyan>[AutoPlay] Progress: {autoPlayProgress:P0} ({elapsed:F1}s / {durationInfo})</color>");
            ChunaLogger.Log($"<color=cyan>[AutoPlay] State: '{owner.InternalAnimationStateName}', correct: {isCorrectState}, normalizedTime: {stateInfo.normalizedTime:F2}</color>");
            ChunaLogger.Log($"<color=yellow>[AutoPlay Debug] speed={patientAnimator.speed}, enabled={patientAnimator.enabled}, stateSpeed={stateInfo.speed}, length={stateInfo.length}</color>");
        }

        const float minElapsedBeforeComplete = 0.5f;
        if (elapsed < minElapsedBeforeComplete)
        {
            return false;
        }

        bool animationComplete = isCorrectState && stateInfo.normalizedTime >= 0.99f;
        bool timeComplete = autoPlayDuration > 0 && elapsed >= autoPlayDuration;

        if (animationComplete || timeComplete)
        {
            if (showDebugLogs)
            {
                string reason = animationComplete ? "animation complete" : "time elapsed";
                ChunaLogger.Log($"<color=green>[AutoPlay] Complete! ({reason}, elapsed: {elapsed:F1}s, normalizedTime: {stateInfo.normalizedTime:F2})</color>");
            }
            return true; // completed
        }

        return false;
    }

    /// <summary>
    /// Complete auto play and reset state.
    /// </summary>
    public void CompleteAutoPlay()
    {
        isAutoPlayMode = false;
        autoPlayProgress = 1f;
    }

    /// <summary>
    /// Reset auto play state.
    /// </summary>
    public void Reset()
    {
        isAutoPlayMode = false;
        autoPlayProgress = 0f;
    }
}
