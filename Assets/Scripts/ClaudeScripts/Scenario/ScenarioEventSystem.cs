using System;
using UnityEngine;

/// <summary>
/// 시나리오 이벤트 시스템
/// 모듈 간 통신을 위한 이벤트 허브
/// </summary>
public class ScenarioEventSystem
{
    // === 시나리오 진행 이벤트 ===
    public event Action<ScenarioData> OnScenarioStarted;
    public event Action<ScenarioData> OnScenarioCompleted;
    public event Action<string> OnScenarioFailed;

    // === Phase 변경 이벤트 ===
    public event Action<PhaseData> OnPhaseChanged;
    public event Action<PhaseData> OnPhaseCompleted;

    // === Step 변경 이벤트 ===
    public event Action<StepData> OnStepChanged;
    public event Action<StepData> OnStepCompleted;

    // === SubStep 변경 이벤트 ===
    public event Action<SubStepData> OnSubStepStarted;
    public event Action<SubStepData> OnSubStepCompleted;

    // === UI 업데이트 요청 이벤트 ===
    public event Action<string, string, string> OnUIUpdateRequested; // (scenarioName, stepDesc, buttonText)
    public event Action<int, int> OnProgressUpdateRequested;         // (current, total)
    public event Action<bool> OnButtonStateUpdateRequested;          // (isEnabled) - 버튼 활성화 상태

    // === 동작 실행 요청 이벤트 ===
    public event Action<string, SubStepData> OnActionRequested;      // (actionType, subStepData)

    // 싱글톤 패턴
    private static ScenarioEventSystem _instance;
    public static ScenarioEventSystem Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = new ScenarioEventSystem();
            }
            return _instance;
        }
    }

    // === 이벤트 발생 메서드 ===

    public void ScenarioStarted(ScenarioData scenario)
    {
        Debug.Log($"[Event] 시나리오 시작: {scenario.scenarioName}");
        OnScenarioStarted?.Invoke(scenario);
    }

    public void ScenarioCompleted(ScenarioData scenario)
    {
        Debug.Log($"[Event] 시나리오 완료: {scenario.scenarioName}");
        OnScenarioCompleted?.Invoke(scenario);
    }

    public void PhaseChanged(PhaseData phase)
    {
        Debug.Log($"[Event] Phase 변경: {phase.phaseName}");
        OnPhaseChanged?.Invoke(phase);
    }

    public void PhaseCompleted(PhaseData phase)
    {
        Debug.Log($"[Event] Phase 완료: {phase.phaseName}");
        OnPhaseCompleted?.Invoke(phase);
    }

    public void StepChanged(StepData step)
    {
        Debug.Log($"[Event] Step 변경: {step.stepName}");
        OnStepChanged?.Invoke(step);
    }

    public void StepCompleted(StepData step)
    {
        Debug.Log($"[Event] Step 완료: {step.stepName}");
        OnStepCompleted?.Invoke(step);
    }

    public void SubStepStarted(SubStepData subStep)
    {
        Debug.Log($"[Event] SubStep 시작: {subStep.subStepNo}");
        OnSubStepStarted?.Invoke(subStep);
    }

    public void SubStepCompleted(SubStepData subStep)
    {
        Debug.Log($"[Event] SubStep 완료: {subStep.subStepNo}");
        OnSubStepCompleted?.Invoke(subStep);
    }

    public void RequestUIUpdate(string scenarioName, string stepDesc, string buttonText)
    {
        OnUIUpdateRequested?.Invoke(scenarioName, stepDesc, buttonText);
    }

    public void RequestProgressUpdate(int current, int total)
    {
        OnProgressUpdateRequested?.Invoke(current, total);
    }

    public void RequestButtonStateUpdate(bool isEnabled)
    {
        OnButtonStateUpdateRequested?.Invoke(isEnabled);
    }

    public void RequestAction(string actionType, SubStepData subStep)
    {
        Debug.Log($"[Event] 동작 요청: {actionType}");
        OnActionRequested?.Invoke(actionType, subStep);
    }

    /// <summary>
    /// 모든 이벤트 리스너 초기화
    /// </summary>
    public void Clear()
    {
        OnScenarioStarted = null;
        OnScenarioCompleted = null;
        OnScenarioFailed = null;
        OnPhaseChanged = null;
        OnPhaseCompleted = null;
        OnStepChanged = null;
        OnStepCompleted = null;
        OnSubStepStarted = null;
        OnSubStepCompleted = null;
        OnUIUpdateRequested = null;
        OnProgressUpdateRequested = null;
        OnButtonStateUpdateRequested = null;
        OnActionRequested = null;
    }
}