using UnityEngine;

/// <summary>
/// Mode state management helper for ChunaPathEvaluator.
/// Handles Stretching, Re-eval, Guide, Lateral Bending modes,
/// threshold application, and axis/pivot configuration.
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

        // Auto-set rotation detection axis (direct field access, no wrapper loop)
        if (isLateralFlexion)
        {
            SetRotationDetectionAxis(ChunaPathEvaluator.RotationDetectionAxis.Z);
            ChunaLogger.Log($"<color=cyan>[ChunaPathEvaluator] Lateral flexion detected (data: {handDataName}) - axis: Z</color>");
        }
        else if (isRotation)
        {
            SetRotationDetectionAxis(ChunaPathEvaluator.RotationDetectionAxis.Y);
            ChunaLogger.Log($"<color=cyan>[ChunaPathEvaluator] Rotation detected (data: {handDataName}) - axis: Y</color>");
        }

        // Rotation direction (direct field access, no wrapper loop)
        if (isAffectedSide)
        {
            SetInvertRotationDirection(true);
            ChunaLogger.Log($"<color=cyan>[ChunaPathEvaluator] Affected side detected - invert rotation</color>");
        }
        else if (isHealthySide)
        {
            SetInvertRotationDirection(false);
            ChunaLogger.Log($"<color=cyan>[ChunaPathEvaluator] Healthy side detected - normal rotation</color>");
        }

        // Mode setting — stepName 기준으로 스트레칭/재평가 활성화 (운동 종류 무관)
        if (isStretching)
        {
            EnableExtendedLimitMode();
            isStretchingMode = true;
            ChunaLogger.Log($"<color=yellow>[ChunaPathEvaluator] 스트레칭 모드 (step: {stepName}, data: {handDataName})</color>");
        }
        else if (isReEvaluation)
        {
            EnableExtendedLimitMode();
            isStretchingMode = false;
            ChunaLogger.Log($"<color=yellow>[ChunaPathEvaluator] 재평가 모드 (step: {stepName}, data: {handDataName})</color>");
        }
        else if (isRotation)
        {
            DisableExtendedLimitMode();
            isStretchingMode = false;
            ChunaLogger.Log($"<color=yellow>[ChunaPathEvaluator] 회전 모드 (data: {handDataName}) - 일반 범위</color>");
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
        out float newRuntimeGuideStartRatio,
        out float newRuntimeGuideEndRatio)
    {
        switch (mode)
        {
            case ChunaPathEvaluator.LateralBendingMode.LimitCheck:
                newDefaultGuideRatio = lateralBending_LimitCheckRatio;
                newRuntimeGuideStartRatio = guideLimitCheck_Start;
                newRuntimeGuideEndRatio = guideLimitCheck_End;
                isStretchingMode = false;
                isExtendedLimitMode = false;
                ChunaLogger.Log($"<color=green>[Lateral] Limit check mode: guide {guideLimitCheck_Start:P0} ~ {guideLimitCheck_End:P0}</color>");
                break;

            case ChunaPathEvaluator.LateralBendingMode.Stretching:
                newDefaultGuideRatio = stretchingEnd;
                newRuntimeGuideStartRatio = stretchingStart;
                newRuntimeGuideEndRatio = stretchingEnd;
                isStretchingMode = true;
                isExtendedLimitMode = true;
                ChunaLogger.Log($"<color=green>[Lateral] Stretching mode: guide {stretchingStart:P0} ~ {stretchingEnd:P0}</color>");
                break;

            case ChunaPathEvaluator.LateralBendingMode.ReEvaluation:
            default:
                newDefaultGuideRatio = lateralBending_ReEvalRatio;
                newRuntimeGuideStartRatio = guideReEval_Start;
                newRuntimeGuideEndRatio = guideReEval_End;
                isStretchingMode = false;
                isExtendedLimitMode = true;
                ChunaLogger.Log($"<color=green>[Lateral] Re-evaluation mode: guide {guideReEval_Start:P0} ~ {guideReEval_End:P0}</color>");
                break;
        }
    }

    #region Threshold Application

    /// <summary>
    /// ScenarioConfig에서 평가 임계점 오버라이드 적용.
    /// 설정하지 않은 시나리오는 Inspector 기본값 유지.
    /// </summary>
    public void ApplyEvaluationThresholds(ScenarioConfig config)
    {
        if (config == null || !config.overrideEvaluationThresholds) return;

        ApplyThresholdValues(config.midHoldStart, config.midHoldEnd,
            config.stretchingHoldStart, config.stretchingHoldEnd,
            config.extendedMidHoldStart, config.extendedMidHoldEnd);

        ChunaLogger.Log($"<color=magenta>[ChunaPathEvaluator] 시나리오 기본 임계점 오버라이드 적용: " +
            $"일반 {owner.MidHoldStartRatio:P0}~{owner.MidHoldEndRatio:P0}, " +
            $"스트레칭 {owner.StretchingHoldStartField:P0}~{owner.StretchingEndField:P0}, " +
            $"재평가 {owner.ExtendedMidHoldStartRatio:P0}~{owner.ExtendedMidHoldEndRatio:P0}</color>");
    }

    /// <summary>
    /// Phase별 회전 임계점 오버라이드 적용 (사각근 전부/중부/후부 등).
    /// 일반 모드의 midHoldStart/End만 변경, 스트레칭/재평가는 유지.
    /// </summary>
    public void ApplyPhaseRotationThresholds(ScenarioConfig.PhaseThresholdOverride phaseOverride)
    {
        if (phaseOverride == null) return;

        owner.MidHoldStartRatio = phaseOverride.rotationHoldStart;
        owner.MidHoldEndRatio = phaseOverride.rotationHoldEnd;

        ChunaLogger.Log($"<color=magenta>[ChunaPathEvaluator] Phase 회전 임계점 [{phaseOverride.phaseName}]: " +
            $"{owner.MidHoldStartRatio:P0}~{owner.MidHoldEndRatio:P0}</color>");
    }

    /// <summary>
    /// 일반 모드 임계점을 기본값으로 복원.
    /// </summary>
    public void RestoreDefaultThresholds(ScenarioConfig config)
    {
        if (config != null && config.overrideEvaluationThresholds)
        {
            owner.MidHoldStartRatio = config.midHoldStart;
            owner.MidHoldEndRatio = config.midHoldEnd;
        }
        else
        {
            owner.MidHoldStartRatio = 0.3f;
            owner.MidHoldEndRatio = 0.5f;
        }
    }

    /// <summary>
    /// 6개 임계값 일괄 설정 (내부용).
    /// </summary>
    private void ApplyThresholdValues(float mhStart, float mhEnd,
        float sStart, float sEnd, float emStart, float emEnd)
    {
        owner.MidHoldStartRatio = mhStart;
        owner.MidHoldEndRatio = mhEnd;
        owner.StretchingHoldStartField = sStart;
        owner.StretchingEndField = sEnd;
        owner.ExtendedMidHoldStartRatio = emStart;
        owner.ExtendedMidHoldEndRatio = emEnd;
    }

    #endregion

    #region Axis / Pivot Configuration

    /// <summary>
    /// 회전 방향 반전 설정.
    /// </summary>
    public void SetInvertRotationDirection(bool invert)
    {
        owner.InvertRotationDirectionField = invert;
        if (owner.ShowDebugLogs)
            ChunaLogger.Log($"<color=cyan>[ChunaPathEvaluator] 회전 방향 반전: {invert}</color>");
    }

    /// <summary>
    /// 회전 감지 축 Vector3 반환.
    /// </summary>
    public Vector3 GetRotationDetectionAxis()
    {
        switch (owner.RotationDetectionAxisField)
        {
            case ChunaPathEvaluator.RotationDetectionAxis.Y:
                return Vector3.up;      // 목 회전 (좌우 돌리기)
            case ChunaPathEvaluator.RotationDetectionAxis.Z:
                return Vector3.forward; // 측굴 (좌우 기울이기)
            case ChunaPathEvaluator.RotationDetectionAxis.X:
                return Vector3.right;   // 굴곡/신전 (앞뒤로 숙이기)
            default:
                return Vector3.up;
        }
    }

    /// <summary>
    /// 회전 감지 축 설정.
    /// </summary>
    public void SetRotationDetectionAxis(ChunaPathEvaluator.RotationDetectionAxis axis)
    {
        owner.RotationDetectionAxisField = axis;
        if (owner.ShowDebugLogs)
        {
            string axisName = axis == ChunaPathEvaluator.RotationDetectionAxis.Y ? "Y(목회전)" :
                             axis == ChunaPathEvaluator.RotationDetectionAxis.Z ? "Z(측굴)" : "X(굴곡/신전)";
            ChunaLogger.Log($"<color=cyan>[ChunaPathEvaluator] 회전 감지 축 설정: {axisName}</color>");
        }
    }

    /// <summary>
    /// 피벗 기반 진행률 설정.
    /// </summary>
    public void SetPivotSettings(Transform pivot, float angle, ChunaPathEvaluator.RotationDetectionAxis planeAxis, bool invert = false)
    {
        owner.PivotTransformField = pivot;
        owner.TargetAngle = angle;
        owner.PivotPlaneAxisField = planeAxis;
        owner.InvertPivotAngleField = invert;
        owner.UsePivotBasedProgressField = pivot != null;

        if (owner.ShowDebugLogs)
        {
            string axisName = planeAxis == ChunaPathEvaluator.RotationDetectionAxis.Y ? "Y(XZ평면)" :
                             planeAxis == ChunaPathEvaluator.RotationDetectionAxis.Z ? "Z(XY평면-측굴)" : "X(YZ평면)";
            ChunaLogger.Log($"<color=magenta>[ChunaPathEvaluator] 피벗 설정 - 피벗:{(pivot != null ? pivot.name : "없음")}, 목표각도:{angle}°, 평면:{axisName}, 반전:{invert}</color>");
        }
    }

    #endregion
}
