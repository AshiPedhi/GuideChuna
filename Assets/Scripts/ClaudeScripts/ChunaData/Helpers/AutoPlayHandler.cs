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
    private bool isGated = false;              // PassiveStretch: 보조수 접촉으로 재생 게이팅
    private bool latchGate = false;            // touchOnce: 최초 접촉으로 게이트를 열고 그대로 유지
    private bool gateLatched = false;          // latchGate 모드에서 이미 열렸는지
    private bool requireBothHands = false;     // bothHands: 양손이 각각 닿아야 게이트가 열림

    // Properties
    public bool IsAutoPlayMode => isAutoPlayMode;
    public float AutoPlayProgress => autoPlayProgress;
    public bool IsGated => isGated;
    /// <summary>touchOnce 모드 여부. 최초 접촉만으로 끝까지 재생한다(손을 계속 대고 있지 않아도 됨).</summary>
    public bool LatchGate => latchGate;
    /// <summary>touchOnce 모드에서 이미 접촉이 인정되어 래치된 상태인지(표시구 색 등에 사용).</summary>
    public bool IsGateLatched => gateLatched;
    /// <summary>bothHands 모드 여부. 양손이 각각 대상 부위에 닿아야 게이트가 열린다(양손 파지).</summary>
    public bool RequireBothHands => requireBothHands;

    /// <summary>
    /// Start auto play mode.
    /// </summary>
    /// <param name="latchGate">true면 최초 접촉 시점에 게이트를 래치해 손을 떼도 계속 재생한다.</param>
    /// <param name="requireBothHands">true면 양손이 각각 닿아야 게이트가 열린다.</param>
    public void StartAutoPlay(float duration, bool gated = false, bool latchGate = false, bool requireBothHands = false)
    {
        isAutoPlayMode = true;
        autoPlayStartTime = Time.time;
        autoPlayProgress = 0f;
        isGated = gated;
        this.latchGate = latchGate;
        this.requireBothHands = requireBothHands;
        gateLatched = false;

        if (duration > 0f)
        {
            autoPlayDuration = duration;
        }
        else
        {
            autoPlayDuration = 0f;
        }

        string durationStr = autoPlayDuration > 0 ? $"{autoPlayDuration:F1}s" : "on animation complete";
        string gateStr = gated ? (latchGate ? " [gated: touchOnce - 최초 접촉 후 계속 재생]" : " [gated by assistant contact]") : "";
        if (gated && requireBothHands) gateStr += " [bothHands - 양손 각각 접촉 필요]";
        ChunaLogger.Log($"<color=green>[AutoPlay] Started! Duration:{durationStr}, Animation:{owner.InternalAnimationStateName ?? "none"}{gateStr}</color>");
    }

    /// <summary>
    /// Update auto play mode. Returns true if completed this frame.
    /// gateOpen: gated 모드에서 true=재생, false=일시정지 (접촉 게이팅용). gated 아니면 무시.
    /// </summary>
    public bool UpdateAutoPlay(Animator patientAnimator, bool gateOpen, bool showDebugLogs)
    {
        // touchOnce: 한 번 열리면 계속 열린 것으로 취급 (손을 떼도 끝까지 재생)
        if (isGated && latchGate)
        {
            if (gateOpen)
            {
                if (!gateLatched)
                {
                    gateLatched = true;
                    ChunaLogger.Log("<color=green>[AutoPlay] 접촉 감지 - 이후 손을 떼도 끝까지 재생합니다 (touchOnce)</color>");
                }
            }
            else if (gateLatched)
            {
                gateOpen = true;
            }
        }

        // Gated + 게이트 닫힘 → 일시정지 (elapsed 시간이 증가하지 않도록 startTime을 전진)
        if (isGated && !gateOpen)
        {
            autoPlayStartTime += Time.deltaTime;
            if (patientAnimator != null && patientAnimator.speed != 0f)
            {
                patientAnimator.speed = 0f;
            }
            if (showDebugLogs && Time.frameCount % 60 == 0)
            {
                ChunaLogger.Log($"<color=yellow>[AutoPlay] Paused (gate closed - 보조수 접촉 대기)</color>");
            }
            return false;
        }

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
            if (!isGated)
            {
                ChunaLogger.LogWarning($"<color=orange>[AutoPlay] Animator speed is {patientAnimator.speed}. Restoring to 1.</color>");
            }
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
        isGated = false;
        latchGate = false;
        gateLatched = false;
        requireBothHands = false;
    }

    /// <summary>
    /// Reset auto play state.
    /// </summary>
    public void Reset()
    {
        isAutoPlayMode = false;
        autoPlayProgress = 0f;
        isGated = false;
        latchGate = false;
        gateLatched = false;
        requireBothHands = false;
    }
}
