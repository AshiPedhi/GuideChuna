using UnityEngine;

/// <summary>
/// 시나리오 시스템 초기화 및 통합
/// </summary>
public class ScenarioSystemInitializer : MonoBehaviour
{
    [Header("Core")]
    [SerializeField] private ScenarioManager scenarioManager;
    
    [Header("UI")]
    [SerializeField] private ScenarioUIController uiController;    
    
    [Header("Settings")]
    [SerializeField] private int initialScenarioNo = 1;
    [SerializeField] private bool autoStart = false;
    
    private void Start()
    {
        InitializeSystem();
        
        if (autoStart)
        {
            StartScenario();
        }
    }
    
    /// <summary>
    /// 시스템 초기화
    /// </summary>
    private void InitializeSystem()
    {
        // 컴포넌트 자동 찾기
        if (scenarioManager == null)
            scenarioManager = FindObjectOfType<ScenarioManager>();
        
        if (uiController == null)
            uiController = FindObjectOfType<ScenarioUIController>();
        
        Debug.Log("[ScenarioSystem] 초기화 완료");
    }
    
    /// <summary>
    /// 시나리오 시작
    /// </summary>
    public void StartScenario()
    {
        if (scenarioManager != null)
        {
            scenarioManager.StartScenario();
        }
        else
        {
            Debug.LogError("[ScenarioSystem] ScenarioManager를 찾을 수 없습니다!");
        }
    }
    
    /// <summary>
    /// Phase로 점프
    /// </summary>
    public void JumpToPhase(string phaseName)
    {
        if (scenarioManager != null)
        {
            scenarioManager.JumpToPhase(phaseName);
        }
    }
    
    /// <summary>
    /// 다음 단계로 진행 (외부 호출용)
    /// </summary>
    public void NextStep()
    {
        if (scenarioManager != null)
        {
            scenarioManager.NextSubStep();
        }
    }
}
