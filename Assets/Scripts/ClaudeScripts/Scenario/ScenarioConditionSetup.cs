using UnityEngine;

/// <summary>
/// 시나리오 조건 등록 헬퍼
/// Inspector에서 쉽게 조건을 등록할 수 있도록 도와주는 스크립트
/// </summary>
public class ScenarioConditionSetup : MonoBehaviour
{
    [Header("=== 조건 매니저 참조 ===")]
    [SerializeField] private ScenarioConditionManager conditionManager;
    
    [Header("=== 자동 조건 등록 ===")]
    [SerializeField] private bool registerOnStart = true;
    
    void Awake()
    {
        if (conditionManager == null)
        {
            conditionManager = FindObjectOfType<ScenarioConditionManager>();
        }
    }
    
    void Start()
    {
        if (registerOnStart)
        {
            RegisterExampleConditions();
        }
    }
    
    /// <summary>
    /// 예제 조건 등록
    /// 여기서 필요한 조건들을 등록하세요
    /// </summary>
    [ContextMenu("조건 등록")]
    public void RegisterExampleConditions()
    {
        if (conditionManager == null)
        {
            Debug.LogError("[ConditionSetup] ScenarioConditionManager를 찾을 수 없습니다!");
            return;
        }
        
        // 예제: 평가 Phase의 평가 Step의 1번 SubStep - 5초 대기
        conditionManager.RegisterCondition(
            phaseName: "평가",
            stepName: "평가",
            subStepNo: 1,
            condition: new TimeBasedCondition(5f)
        );
        
        // 예제: 중부 Phase의 등척성운동 Step의 1번 SubStep - 10초 대기
        conditionManager.RegisterCondition(
            phaseName: "중부",
            stepName: "등척성운동",
            subStepNo: 1,
            condition: new TimeBasedCondition(10f)
        );
        
        Debug.Log("[ConditionSetup] 조건 등록 완료");
    }
    
    /// <summary>
    /// 시간 기반 조건 등록
    /// </summary>
    public void RegisterTimeCondition(string phaseName, string stepName, int subStepNo, float duration)
    {
        if (conditionManager == null)
        {
            Debug.LogError("[ConditionSetup] ScenarioConditionManager를 찾을 수 없습니다!");
            return;
        }
        
        conditionManager.RegisterCondition(
            phaseName,
            stepName,
            subStepNo,
            new TimeBasedCondition(duration)
        );
        
        Debug.Log($"[ConditionSetup] 시간 조건 등록: {phaseName}/{stepName}/{subStepNo} - {duration}초");
    }
    
    /// <summary>
    /// 커스텀 조건 등록
    /// </summary>
    public void RegisterCustomCondition(string phaseName, string stepName, int subStepNo, System.Func<bool> conditionFunc, string description)
    {
        if (conditionManager == null)
        {
            Debug.LogError("[ConditionSetup] ScenarioConditionManager를 찾을 수 없습니다!");
            return;
        }
        
        conditionManager.RegisterCondition(
            phaseName,
            stepName,
            subStepNo,
            new CustomCondition(conditionFunc, description)
        );
        
        Debug.Log($"[ConditionSetup] 커스텀 조건 등록: {phaseName}/{stepName}/{subStepNo} - {description}");
    }
    
    /// <summary>
    /// 버튼 클릭 조건 등록
    /// </summary>
    public ButtonClickCondition RegisterButtonCondition(string phaseName, string stepName, int subStepNo)
    {
        if (conditionManager == null)
        {
            Debug.LogError("[ConditionSetup] ScenarioConditionManager를 찾을 수 없습니다!");
            return null;
        }
        
        ButtonClickCondition condition = new ButtonClickCondition();
        
        conditionManager.RegisterCondition(
            phaseName,
            stepName,
            subStepNo,
            condition
        );
        
        Debug.Log($"[ConditionSetup] 버튼 클릭 조건 등록: {phaseName}/{stepName}/{subStepNo}");
        
        return condition;
    }
    
    /// <summary>
    /// 위치 기반 조건 등록
    /// </summary>
    public void RegisterPositionCondition(string phaseName, string stepName, int subStepNo, Transform target, Vector3 position, float threshold)
    {
        if (conditionManager == null)
        {
            Debug.LogError("[ConditionSetup] ScenarioConditionManager를 찾을 수 없습니다!");
            return;
        }
        
        conditionManager.RegisterCondition(
            phaseName,
            stepName,
            subStepNo,
            new PositionBasedCondition(target, position, threshold)
        );
        
        Debug.Log($"[ConditionSetup] 위치 조건 등록: {phaseName}/{stepName}/{subStepNo}");
    }
}
