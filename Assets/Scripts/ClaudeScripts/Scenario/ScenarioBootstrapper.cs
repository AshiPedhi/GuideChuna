using UnityEngine;

/// <summary>
/// 씬 로드 시 PlayerPrefs에서 시나리오 인덱스를 읽어
/// ScenarioConfig를 ScenarioManager / InfoPanelController에 주입하는 부트스트래퍼.
/// DefaultExecutionOrder(-100)으로 다른 컴포넌트보다 먼저 실행됨.
/// </summary>
[DefaultExecutionOrder(-100)]
public class ScenarioBootstrapper : MonoBehaviour
{
    [Header("=== 시나리오 설정 ===")]
    [Tooltip("인덱스 순서대로 시나리오 Config 할당 (0: 상부승모근, 1: 견갑거근, ...)")]
    [SerializeField] private ScenarioConfig[] scenarioConfigs;

    [Tooltip("PlayerPrefs에 값이 없을 때 사용할 기본 인덱스")]
    [SerializeField] private int defaultScenarioIndex = 0;

    [Tooltip("true면 PlayerPrefs 무시하고 항상 defaultScenarioIndex 사용 (에디터 테스트용)")]
    [SerializeField] private bool forceDefaultIndex = false;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLog = true;

    private void Awake()
    {
        int selectedIndex = forceDefaultIndex
            ? defaultScenarioIndex
            : PlayerPrefs.GetInt(PrefsKeys.SelectedScenario, defaultScenarioIndex);

        if (scenarioConfigs == null || scenarioConfigs.Length == 0)
        {
            ChunaLogger.LogWarning("[ScenarioBootstrapper] scenarioConfigs 배열이 비어있습니다!");
            return;
        }

        if (selectedIndex < 0 || selectedIndex >= scenarioConfigs.Length)
        {
            ChunaLogger.LogWarning($"[ScenarioBootstrapper] 인덱스 {selectedIndex}이(가) 범위를 벗어남 (0~{scenarioConfigs.Length - 1}). 기본값 {defaultScenarioIndex} 사용");
            selectedIndex = defaultScenarioIndex;
        }

        ScenarioConfig config = scenarioConfigs[selectedIndex];
        if (config == null)
        {
            ChunaLogger.LogError($"[ScenarioBootstrapper] scenarioConfigs[{selectedIndex}]가 null입니다!");
            return;
        }

        if (showDebugLog)
        {
            ChunaLogger.Log($"<color=cyan>[ScenarioBootstrapper] 시나리오 Config 로드: {config.scenarioName} (index={selectedIndex})</color>");
            ChunaLogger.Log($"<color=cyan>  - patientPositionPreset: {config.patientPositionPreset}</color>");
        }

        // ScenarioManager에 config 주입
        ScenarioManager scenarioManager = FindFirstObjectByType<ScenarioManager>();
        if (scenarioManager != null)
        {
            scenarioManager.SetScenarioConfig(config);
            if (showDebugLog)
                ChunaLogger.Log("[ScenarioBootstrapper] ScenarioManager에 config 주입 완료");
        }
        else
        {
            ChunaLogger.LogWarning("[ScenarioBootstrapper] ScenarioManager를 찾을 수 없습니다!");
        }

        // AnatomyMuscleController에 시나리오별 근육 활성화
        AnatomyMuscleController muscleController = FindFirstObjectByType<AnatomyMuscleController>();
        if (muscleController != null)
        {
            muscleController.ApplyScenario(config.scenarioName);
            if (showDebugLog)
                ChunaLogger.Log($"[ScenarioBootstrapper] AnatomyMuscleController에 시나리오 적용: '{config.scenarioName}'");
        }

        // ScenarioConditionManager에 나레이션 시나리오 폴더 주입
        ScenarioConditionManager conditionManager = FindFirstObjectByType<ScenarioConditionManager>();
        if (conditionManager != null)
        {
            string narrationFolder = !string.IsNullOrEmpty(config.narrationSubFolder)
                ? config.narrationSubFolder
                : config.scenarioName;
            conditionManager.SetNarrationScenarioFolder(narrationFolder);
            if (showDebugLog)
                ChunaLogger.Log($"[ScenarioBootstrapper] ConditionManager에 나레이션 폴더 주입: '{narrationFolder}'");
        }

        // InfoPanelController에 환자 위치 프리셋 주입
        InfoPanelController infoPanelController = FindFirstObjectByType<InfoPanelController>();
        if (infoPanelController != null)
        {
            infoPanelController.SetDefaultPositionPreset(config.patientPositionPreset);
            if (showDebugLog)
                ChunaLogger.Log("[ScenarioBootstrapper] InfoPanelController에 위치 프리셋 주입 완료");
        }
        else
        {
            if (showDebugLog)
                ChunaLogger.Log("[ScenarioBootstrapper] InfoPanelController가 없음 (Practice씬일 수 있음)");
        }
    }

    private System.Collections.IEnumerator Start()
    {
        // 다른 컴포넌트 초기화가 모두 끝난 후 1프레임 대기
        yield return null;

        ScenarioManager scenarioManager = FindFirstObjectByType<ScenarioManager>();
        if (scenarioManager != null)
        {
            scenarioManager.ApplyAnimatorController();
        }
    }
}
