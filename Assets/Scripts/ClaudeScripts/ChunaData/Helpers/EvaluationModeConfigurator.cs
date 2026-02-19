using UnityEngine;

/// <summary>
/// Mode state management helper for ChunaPathEvaluator.
/// Handles Stretching, Re-eval, Guide, and Lateral Bending modes.
/// </summary>
public class EvaluationModeConfigurator
{
    private readonly ChunaPathEvaluator owner;

    public EvaluationModeConfigurator(ChunaPathEvaluator owner)
    {
        this.owner = owner;
    }

    // Mode state
    private bool isExtendedLimitMode = false;
    private bool isStretchingMode = false;
    private bool isGuideMode = false;

    // Properties
    public bool IsExtendedLimitMode => isExtendedLimitMode;
    public bool IsStretchingMode => isStretchingMode;
    public bool IsGuideMode => isGuideMode;

    /// <summary>
    /// Enable extended limit mode (stretching/re-evaluation).
    /// </summary>
    public void EnableExtendedLimitMode()
    {
        isExtendedLimitMode = true;
        ChunaLogger.Log($"<color=magenta>[ChunaPathEvaluator] Extended limit mode enabled</color>");
    }

    /// <summary>
    /// Disable extended limit mode (return to default 50%).
    /// </summary>
    public void DisableExtendedLimitMode()
    {
        isExtendedLimitMode = false;
        ChunaLogger.Log($"<color=cyan>[ChunaPathEvaluator] Extended limit mode disabled</color>");
    }

    /// <summary>
    /// Set mode from step name (legacy compatibility).
    /// </summary>
    public void SetFromStepName(string stepName)
    {
        SetFromNames(stepName, null);
    }

    /// <summary>
    /// Set mode from step name and hand data name.
    /// Determines stretching/re-eval/guide mode from stepName,
    /// and affected/healthy side rotation direction from handDataName.
    /// </summary>
    public void SetFromNames(string stepName, string handDataName)
    {
        if (string.IsNullOrEmpty(stepName) && string.IsNullOrEmpty(handDataName))
        {
            DisableExtendedLimitMode();
            isStretchingMode = false;
            isGuideMode = false;
            return;
        }

        bool isReEvaluation = !string.IsNullOrEmpty(stepName) && stepName.Contains("재평가");
        bool isStretching = !string.IsNullOrEmpty(stepName) && stepName.Contains("스트레칭");
        bool isGuide = !string.IsNullOrEmpty(stepName) && stepName.Contains("가이드");

        isGuideMode = isGuide;
        if (isGuide)
        {
            ChunaLogger.Log($"<color=magenta>[ChunaPathEvaluator] Guide mode (Step: {stepName})</color>");
        }

        bool isAffectedSide = !string.IsNullOrEmpty(handDataName) && handDataName.Contains("환측");
        bool isHealthySide = !string.IsNullOrEmpty(handDataName) && handDataName.Contains("건측");
        bool isLateralFlexion = !string.IsNullOrEmpty(handDataName) && handDataName.Contains("측굴");
        bool isRotation = !string.IsNullOrEmpty(handDataName) && handDataName.Contains("회전");

        // Auto-set rotation detection axis
        if (isLateralFlexion)
        {
            owner.SetRotationDetectionAxis(ChunaPathEvaluator.RotationDetectionAxis.Z);
            ChunaLogger.Log($"<color=cyan>[ChunaPathEvaluator] Lateral flexion detected (data: {handDataName}) - axis: Z</color>");
        }
        else if (isRotation)
        {
            owner.SetRotationDetectionAxis(ChunaPathEvaluator.RotationDetectionAxis.Y);
            ChunaLogger.Log($"<color=cyan>[ChunaPathEvaluator] Rotation detected (data: {handDataName}) - axis: Y</color>");
        }

        // Rotation direction
        if (isAffectedSide)
        {
            owner.SetInvertRotationDirection(true);
            ChunaLogger.Log($"<color=cyan>[ChunaPathEvaluator] Affected side detected - invert rotation</color>");
        }
        else if (isHealthySide)
        {
            owner.SetInvertRotationDirection(false);
            ChunaLogger.Log($"<color=cyan>[ChunaPathEvaluator] Healthy side detected - normal rotation</color>");
        }

        // Mode setting
        if (isRotation)
        {
            DisableExtendedLimitMode();
            isStretchingMode = false;
            ChunaLogger.Log($"<color=yellow>[ChunaPathEvaluator] Rotation mode (data: {handDataName}) - normal hold range (0.3~0.5)</color>");
        }
        else if (isLateralFlexion && isStretching)
        {
            EnableExtendedLimitMode();
            isStretchingMode = true;
            ChunaLogger.Log($"<color=yellow>[ChunaPathEvaluator] Lateral flexion stretching mode</color>");
        }
        else if (isLateralFlexion && isReEvaluation)
        {
            EnableExtendedLimitMode();
            isStretchingMode = false;
            ChunaLogger.Log($"<color=yellow>[ChunaPathEvaluator] Lateral flexion re-evaluation mode</color>");
        }
        else
        {
            DisableExtendedLimitMode();
            isStretchingMode = false;
        }
    }

    /// <summary>
    /// Set lateral bending mode.
    /// </summary>
    public void SetLateralBendingMode(
        ChunaPathEvaluator.LateralBendingMode mode,
        float lateralBending_LimitCheckRatio,
        float lateralBending_ReEvalRatio,
        float stretchingStart, float stretchingEnd, float stretchingHoldStart,
        float guideLimitCheck_Start, float guideLimitCheck_End,
        float guideReEval_Start, float guideReEval_End,
        float calculatedDataAngle,
        // out parameters for owner to apply
        out float newDefaultGuideRatio,
        out float newLimitBarrierRatio,
        out float newExtendedLimitBarrierRatio,
        out float newRuntimeGuideStartRatio,
        out float newRuntimeGuideEndRatio)
    {
        // Defaults
        newExtendedLimitBarrierRatio = lateralBending_ReEvalRatio;

        switch (mode)
        {
            case ChunaPathEvaluator.LateralBendingMode.LimitCheck:
                newDefaultGuideRatio = lateralBending_LimitCheckRatio;
                newLimitBarrierRatio = lateralBending_LimitCheckRatio;
                newRuntimeGuideStartRatio = guideLimitCheck_Start;
                newRuntimeGuideEndRatio = guideLimitCheck_End;
                isStretchingMode = false;
                isExtendedLimitMode = false;
                ChunaLogger.Log($"<color=green>[Lateral] Limit check mode: guide {guideLimitCheck_Start:P0} ~ {guideLimitCheck_End:P0}</color>");
                break;

            case ChunaPathEvaluator.LateralBendingMode.Stretching:
                newDefaultGuideRatio = stretchingEnd;
                newLimitBarrierRatio = lateralBending_LimitCheckRatio;
                newRuntimeGuideStartRatio = stretchingStart;
                newRuntimeGuideEndRatio = stretchingEnd;
                isStretchingMode = true;
                isExtendedLimitMode = true;
                ChunaLogger.Log($"<color=green>[Lateral] Stretching mode: guide {stretchingStart:P0} ~ {stretchingEnd:P0}</color>");
                break;

            case ChunaPathEvaluator.LateralBendingMode.ReEvaluation:
            default:
                newDefaultGuideRatio = lateralBending_ReEvalRatio;
                newLimitBarrierRatio = lateralBending_LimitCheckRatio;
                newExtendedLimitBarrierRatio = lateralBending_ReEvalRatio;
                newRuntimeGuideStartRatio = guideReEval_Start;
                newRuntimeGuideEndRatio = guideReEval_End;
                isStretchingMode = false;
                isExtendedLimitMode = true;
                ChunaLogger.Log($"<color=green>[Lateral] Re-evaluation mode: guide {guideReEval_Start:P0} ~ {guideReEval_End:P0}</color>");
                break;
        }
    }
}
