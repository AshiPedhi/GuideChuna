using UnityEngine;
using System.Collections.Generic;
using System.Reflection;

/// <summary>
/// ì‹œë‚˜ë¦¬ì˜¤ ì§„í–‰ ìƒíƒœ ë””ë²„ê±°
/// í˜„ìž¬ ì§„í–‰ ì¤‘ì¸ ì‹œë‚˜ë¦¬ì˜¤ ë°ì´í„°ì™€ ì¡°ê±´ ìƒíƒœë¥¼ ì‹¤ì‹œê°„ìœ¼ë¡œ ë¡œê·¸ ì¶œë ¥
/// </summary>
public class ScenarioProgressDebugger : MonoBehaviour
{
    [Header("=== ì°¸ì¡° ì»´í¬ë„ŒíŠ¸ ===")]
    [SerializeField] private ScenarioManager scenarioManager;
    [SerializeField] private ScenarioConditionManager conditionManager;
    [SerializeField] private HandPosePlayer handPosePlayer;

    [Header("=== ë””ë²„ê·¸ ì„¤ì • ===")]
    [SerializeField] private bool autoLog = true;
    [SerializeField] private float logInterval = 2f;

    private float lastLogTime = 0f;

    void Awake()
    {
        // ì»´í¬ë„ŒíŠ¸ ìžë™ ì°¾ê¸°
        if (scenarioManager == null)
            scenarioManager = FindObjectOfType<ScenarioManager>();

        if (conditionManager == null)
            conditionManager = FindObjectOfType<ScenarioConditionManager>();

        if (handPosePlayer == null)
            handPosePlayer = FindObjectOfType<HandPosePlayer>();
    }

    void Update()
    {
        if (autoLog && Time.time - lastLogTime >= logInterval)
        {
            LogCurrentStatus();
            lastLogTime = Time.time;
        }
    }

    /// <summary>
    /// í˜„ìž¬ ìƒíƒœ ë¡œê·¸ ì¶œë ¥ (Inspector ë²„íŠ¼ìš©)
    /// </summary>
    [ContextMenu("ðŸ“Š í˜„ìž¬ ìƒíƒœ ì¶œë ¥")]
    public void LogCurrentStatus()
    {
        Debug.Log("<color=cyan>â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•</color>");
        Debug.Log("<color=cyan>ðŸ“Š ì‹œë‚˜ë¦¬ì˜¤ ì§„í–‰ ìƒíƒœ ë¦¬í¬íŠ¸</color>");
        Debug.Log($"<color=yellow>ì‹œê°„: {System.DateTime.Now:HH:mm:ss}</color>");
        Debug.Log("<color=cyan>â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•</color>");

        LogScenarioStatus();
        LogConditionStatus();
        LogHandPosePlayerStatus();
        LogRegisteredConditions();

        Debug.Log("<color=cyan>â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•</color>");
    }

    /// <summary>
    /// ì‹œë‚˜ë¦¬ì˜¤ ì§„í–‰ ìƒíƒœ ë¡œê·¸
    /// </summary>
    private void LogScenarioStatus()
    {
        if (scenarioManager == null)
        {
            Debug.LogError("âŒ ScenarioManagerë¥¼ ì°¾ì„ ìˆ˜ ì—†ìŠµë‹ˆë‹¤!");
            return;
        }

        Debug.Log("<color=white>â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”</color>");
        Debug.Log("<color=green>â–¶ 1. ì‹œë‚˜ë¦¬ì˜¤ ì§„í–‰ ìƒíƒœ</color>");
        Debug.Log("<color=white>â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”</color>");

        // í˜„ìž¬ ì‹œë‚˜ë¦¬ì˜¤
        if (scenarioManager.CurrentScenario != null)
        {
            Debug.Log($"  ðŸ“Œ ì‹œë‚˜ë¦¬ì˜¤: <color=yellow>{scenarioManager.CurrentScenario.scenarioName}</color> (ë²ˆí˜¸: {scenarioManager.CurrentScenario.scenarioNo})");
            Debug.Log($"  ðŸ“ ì´ Phase ìˆ˜: {scenarioManager.CurrentScenario.phases.Count}");
        }
        else
        {
            Debug.LogWarning("  âš  í˜„ìž¬ ì‹œë‚˜ë¦¬ì˜¤ ì—†ìŒ");
            return;
        }

        // í˜„ìž¬ Phase
        if (scenarioManager.CurrentPhase != null)
        {
            Debug.Log($"  ðŸ“‚ í˜„ìž¬ Phase: <color=yellow>{scenarioManager.CurrentPhase.phaseName}</color>");
            Debug.Log($"  ðŸ“„ ì´ Step ìˆ˜: {scenarioManager.CurrentPhase.steps.Count}");
        }
        else
        {
            Debug.LogWarning("  âš  í˜„ìž¬ Phase ì—†ìŒ");
            return;
        }

        // í˜„ìž¬ Step
        if (scenarioManager.CurrentStep != null)
        {
            Debug.Log($"  ðŸ“ í˜„ìž¬ Step: <color=yellow>{scenarioManager.CurrentStep.stepName}</color> (ë²ˆí˜¸: {scenarioManager.CurrentStep.stepNo})");
            Debug.Log($"  ðŸ“‹ ì´ SubStep ìˆ˜: {scenarioManager.CurrentStep.subSteps.Count}");
            Debug.Log($"  ðŸ”° ê°€ì´ë“œ Step ì—¬ë¶€: {scenarioManager.CurrentStep.IsGuideStep()}");
        }
        else
        {
            Debug.LogWarning("  âš  í˜„ìž¬ Step ì—†ìŒ");
            return;
        }

        // í˜„ìž¬ SubStep
        if (scenarioManager.CurrentSubStep != null)
        {
            var subStep = scenarioManager.CurrentSubStep;
            Debug.Log($"  â–¶ í˜„ìž¬ SubStep: <color=yellow>#{subStep.subStepNo}</color>");
            Debug.Log($"    â± Duration: {subStep.duration}ì´ˆ");
            Debug.Log($"    ðŸ“œ í…ìŠ¤íŠ¸ ì•ˆë‚´: {(string.IsNullOrEmpty(subStep.textInstruction) ? "(ì—†ìŒ)" : subStep.textInstruction)}");
            Debug.Log($"    ðŸ”Š ìŒì„± ì•ˆë‚´: {(string.IsNullOrEmpty(subStep.voiceInstruction) ? "(ì—†ìŒ)" : subStep.voiceInstruction.Substring(0, Mathf.Min(50, subStep.voiceInstruction.Length)))}...");
            Debug.Log($"    ðŸ‘‹ í•¸ë“œ íŠ¸ëž˜í‚¹ íŒŒì¼: {(string.IsNullOrEmpty(subStep.handTrackingFileName) ? "<color=red>(ì—†ìŒ)</color>" : $"<color=green>{subStep.handTrackingFileName}</color>")}");
            //Debug.Log($"    ðŸ¥ í™˜ìž ëª¨ì…˜ íŒŒì¼: {(string.IsNullOrEmpty(subStep.patientMotionFileName) ? "(ì—†ìŒ)" : subStep.patientMotionFileName)}");
        }
        else
        {
            Debug.LogWarning("  âš  í˜„ìž¬ SubStep ì—†ìŒ");
        }

        // ì§„í–‰ ìƒíƒœ
        Debug.Log($"  ðŸ“Š ì§„í–‰ ìƒíƒœ:");
        Debug.Log($"    â€¢ ë§ˆì§€ë§‰ SubStep: {scenarioManager.IsLastSubStep}");
        Debug.Log($"    â€¢ ë§ˆì§€ë§‰ Step: {scenarioManager.IsLastStep}");
        Debug.Log($"    â€¢ ë§ˆì§€ë§‰ Phase: {scenarioManager.IsLastPhase}");
    }

    /// <summary>
    /// ì¡°ê±´ ë§¤ë‹ˆì € ìƒíƒœ ë¡œê·¸
    /// </summary>
    private void LogConditionStatus()
    {
        Debug.Log("<color=white>â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”</color>");
        Debug.Log("<color=green>â–¶ 2. ì¡°ê±´ ì²´í¬ ìƒíƒœ</color>");
        Debug.Log("<color=white>â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”</color>");

        if (conditionManager == null)
        {
            Debug.LogError("  âŒ ScenarioConditionManagerë¥¼ ì°¾ì„ ìˆ˜ ì—†ìŠµë‹ˆë‹¤!");
            return;
        }

        // IsCheckingCondition í”„ë¡œí¼í‹° í™•ì¸
        bool isChecking = conditionManager.IsCheckingCondition;
        Debug.Log($"  âš™ ì¡°ê±´ ì²´í¬ ì¤‘: <color={(isChecking ? "green>ì˜ˆ" : "red>ì•„ë‹ˆì˜¤")}</color>");

        // ë¦¬í”Œë ‰ì…˜ìœ¼ë¡œ private í•„ë“œ ì ‘ê·¼
        var currentConditionField = conditionManager.GetType().GetField("currentCondition", BindingFlags.NonPublic | BindingFlags.Instance);
        if (currentConditionField != null)
        {
            var currentCondition = currentConditionField.GetValue(conditionManager) as IScenarioCondition;

            if (currentCondition != null)
            {
                Debug.Log($"  ðŸ“Œ í˜„ìž¬ ì¡°ê±´: <color=yellow>{currentCondition.GetConditionDescription()}</color>");
                Debug.Log($"  âœ” ì¡°ê±´ ë§Œì¡± ì—¬ë¶€: <color={(currentCondition.IsConditionMet() ? "green>ë§Œì¡±" : "yellow>ë¯¸ë§Œì¡±")}</color>");

                // HandPoseConditionì¸ ê²½ìš° ì¶”ê°€ ì •ë³´
                if (currentCondition is HandPoseCondition)
                {
                    Debug.Log($"  ðŸ‘‹ ì¡°ê±´ íƒ€ìž…: <color=cyan>HandPoseCondition</color>");
                }
                else if (currentCondition is TimeBasedCondition)
                {
                    Debug.Log($"  â± ì¡°ê±´ íƒ€ìž…: <color=cyan>TimeBasedCondition</color>");
                }
                else if (currentCondition is ButtonClickCondition)
                {
                    Debug.Log($"  ðŸ”˜ ì¡°ê±´ íƒ€ìž…: <color=cyan>ButtonClickCondition</color>");
                }
                else
                {
                    Debug.Log($"  â“ ì¡°ê±´ íƒ€ìž…: {currentCondition.GetType().Name}");
                }
            }
            else
            {
                Debug.Log("  âš  <color=yellow>í˜„ìž¬ ì¡°ê±´ ì—†ìŒ</color> (í† ê¸€ ë˜ëŠ” ìžë™ ì§„í–‰ ëª¨ë“œ)");
            }
        }
    }

    /// <summary>
    /// HandPosePlayer ìƒíƒœ ë¡œê·¸
    /// </summary>
    private void LogHandPosePlayerStatus()
    {
        Debug.Log("<color=white>â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”</color>");
        Debug.Log("<color=green>â–¶ 3. HandPosePlayer ìƒíƒœ</color>");
        Debug.Log("<color=white>â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”</color>");

        if (handPosePlayer == null)
        {
            Debug.LogWarning("  âš  HandPosePlayerë¥¼ ì°¾ì„ ìˆ˜ ì—†ìŠµë‹ˆë‹¤!");
            return;
        }

        Debug.Log($"  ðŸŽ® HandPosePlayer ë°œê²¬: <color=green>{handPosePlayer.name}</color>");

        // ë¦¬í”Œë ‰ì…˜ìœ¼ë¡œ ìƒíƒœ í™•ì¸
        var isPlayingField = handPosePlayer.GetType().GetField("isPlaying", BindingFlags.NonPublic | BindingFlags.Instance);
        if (isPlayingField != null)
        {
            bool isPlaying = (bool)isPlayingField.GetValue(handPosePlayer);
            Debug.Log($"  â–¶ ìž¬ìƒ ì¤‘: <color={(isPlaying ? "green>ì˜ˆ" : "red>ì•„ë‹ˆì˜¤")}</color>");
        }

        var currentFrameField = handPosePlayer.GetType().GetField("currentFrameIndex", BindingFlags.NonPublic | BindingFlags.Instance);
        var totalFramesField = handPosePlayer.GetType().GetField("totalFrames", BindingFlags.NonPublic | BindingFlags.Instance);

        if (currentFrameField != null && totalFramesField != null)
        {
            int currentFrame = (int)currentFrameField.GetValue(handPosePlayer);
            int totalFrames = (int)totalFramesField.GetValue(handPosePlayer);
            Debug.Log($"  ðŸ“Š í”„ë ˆìž„: {currentFrame} / {totalFrames}");

            if (totalFrames > 0)
            {
                float progress = (float)currentFrame / totalFrames * 100f;
                Debug.Log($"  ðŸ“ˆ ì§„í–‰ë¥ : <color=yellow>{progress:F1}%</color>");
            }
        }

        // í˜„ìž¬ ë¡œë“œëœ CSV íŒŒì¼ëª… í™•ì¸
        var loadedFileField = handPosePlayer.GetType().GetField("loadedFileName", BindingFlags.NonPublic | BindingFlags.Instance);
        if (loadedFileField != null)
        {
            string loadedFile = loadedFileField.GetValue(handPosePlayer) as string;
            Debug.Log($"  ðŸ“ ë¡œë“œëœ íŒŒì¼: {(string.IsNullOrEmpty(loadedFile) ? "<color=red>(ì—†ìŒ)</color>" : $"<color=green>{loadedFile}</color>")}");
        }

        // ComparisonMode í™•ì¸
        var comparisonModeField = handPosePlayer.GetType().GetField("comparisonMode", BindingFlags.NonPublic | BindingFlags.Instance);
        if (comparisonModeField != null)
        {
            var comparisonMode = comparisonModeField.GetValue(handPosePlayer);
            Debug.Log($"  ðŸ” ë¹„êµ ëª¨ë“œ: {comparisonMode}");
        }

        // ì§„í–‰ë¥  ëª©í‘œì¹˜ í™•ì¸
        var progressThresholdField = handPosePlayer.GetType().GetField("progressThreshold", BindingFlags.NonPublic | BindingFlags.Instance);
        if (progressThresholdField != null)
        {
            float threshold = (float)progressThresholdField.GetValue(handPosePlayer);
            Debug.Log($"  ðŸŽ¯ ì§„í–‰ë¥  ëª©í‘œ: <color=yellow>{threshold * 100f:F0}%</color>");
        }
    }

    /// <summary>
    /// ë“±ë¡ëœ ì¡°ê±´ë“¤ ë¡œê·¸
    /// </summary>
    private void LogRegisteredConditions()
    {
        Debug.Log("<color=white>â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”</color>");
        Debug.Log("<color=green>â–¶ 4. ë“±ë¡ëœ ì¡°ê±´ ëª©ë¡</color>");
        Debug.Log("<color=white>â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”â”</color>");

        if (conditionManager == null)
        {
            Debug.LogError("  âŒ ScenarioConditionManagerë¥¼ ì°¾ì„ ìˆ˜ ì—†ìŠµë‹ˆë‹¤!");
            return;
        }

        // conditionRegistry í™•ì¸
        var registryField = conditionManager.GetType().GetField("conditionRegistry", BindingFlags.NonPublic | BindingFlags.Instance);
        if (registryField != null)
        {
            var registry = registryField.GetValue(conditionManager) as Dictionary<string, IScenarioCondition>;

            if (registry != null && registry.Count > 0)
            {
                Debug.Log($"  ðŸ“‹ ì´ {registry.Count}ê°œì˜ ì¡°ê±´ì´ ë“±ë¡ë˜ì–´ ìžˆìŠµë‹ˆë‹¤:");

                int index = 1;
                foreach (var kvp in registry)
                {
                    Debug.Log($"    {index}. <color=cyan>{kvp.Key}</color>");
                    Debug.Log($"       â””â”€ {kvp.Value.GetConditionDescription()}");
                    Debug.Log($"       â””â”€ ë§Œì¡± ì—¬ë¶€: <color={(kvp.Value.IsConditionMet() ? "green>ë§Œì¡±" : "yellow>ë¯¸ë§Œì¡±")}</color>");
                    index++;
                }
            }
            else
            {
                Debug.LogWarning("  âš  ë“±ë¡ëœ ì¡°ê±´ì´ ì—†ìŠµë‹ˆë‹¤!");
                Debug.LogWarning("  ðŸ’¡ handTrackingFileNameì´ ìžˆëŠ” SubStepì¸ì§€ í™•ì¸í•˜ì„¸ìš”.");
            }
        }
        else
        {
            Debug.LogError("  âŒ conditionRegistry í•„ë“œë¥¼ ì°¾ì„ ìˆ˜ ì—†ìŠµë‹ˆë‹¤!");
        }
    }

    /// <summary>
    /// íŠ¹ì • ì¡°ê±´ í‚¤ë¡œ ì¡°ê±´ ìƒíƒœ í™•ì¸
    /// </summary>
    [ContextMenu("ðŸ” í˜„ìž¬ SubStepì˜ ì¡°ê±´ í‚¤ í™•ì¸")]
    public void CheckCurrentSubStepConditionKey()
    {
        if (scenarioManager == null || conditionManager == null)
        {
            Debug.LogError("ScenarioManager ë˜ëŠ” ConditionManagerê°€ ì—†ìŠµë‹ˆë‹¤!");
            return;
        }

        if (scenarioManager.CurrentPhase == null || scenarioManager.CurrentStep == null || scenarioManager.CurrentSubStep == null)
        {
            Debug.LogError("í˜„ìž¬ ì§„í–‰ ì¤‘ì¸ ì‹œë‚˜ë¦¬ì˜¤ê°€ ì—†ìŠµë‹ˆë‹¤!");
            return;
        }

        string phaseName = scenarioManager.CurrentPhase.phaseName;
        string stepName = scenarioManager.CurrentStep.stepName;
        int subStepNo = scenarioManager.CurrentSubStep.subStepNo;

        string conditionKey = $"{phaseName}_{stepName}_{subStepNo}";

        Debug.Log("<color=cyan>â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•</color>");
        Debug.Log($"<color=yellow>í˜„ìž¬ SubStepì˜ ì¡°ê±´ í‚¤: {conditionKey}</color>");
        Debug.Log("<color=cyan>â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•</color>");

        // ì´ í‚¤ë¡œ ë“±ë¡ëœ ì¡°ê±´ì´ ìžˆëŠ”ì§€ í™•ì¸
        var registryField = conditionManager.GetType().GetField("conditionRegistry", BindingFlags.NonPublic | BindingFlags.Instance);
        if (registryField != null)
        {
            var registry = registryField.GetValue(conditionManager) as Dictionary<string, IScenarioCondition>;

            if (registry != null && registry.ContainsKey(conditionKey))
            {
                var condition = registry[conditionKey];
                Debug.Log($"<color=green>âœ” ì¡°ê±´ì´ ë“±ë¡ë˜ì–´ ìžˆìŠµë‹ˆë‹¤!</color>");
                Debug.Log($"  ì¡°ê±´: {condition.GetConditionDescription()}");
                Debug.Log($"  ë§Œì¡± ì—¬ë¶€: <color={(condition.IsConditionMet() ? "green>ë§Œì¡±" : "yellow>ë¯¸ë§Œì¡±")}</color>");
            }
            else
            {
                Debug.LogWarning($"<color=yellow>âš  ì´ í‚¤ë¡œ ë“±ë¡ëœ ì¡°ê±´ì´ ì—†ìŠµë‹ˆë‹¤!</color>");
                Debug.LogWarning($"  â€¢ durationì´ 0ì´ê³  handTrackingFileNameë„ ì—†ìœ¼ë©´ í† ê¸€ë¡œ ìˆ˜ë™ ì§„í–‰ë©ë‹ˆë‹¤.");
                Debug.LogWarning($"  â€¢ duration > 0ì´ë©´ ìžë™ìœ¼ë¡œ ì‹œê°„ í›„ ì§„í–‰ë©ë‹ˆë‹¤.");
            }
        }
    }

    /// <summary>
    /// ë¬¸ì œ ì§„ë‹¨
    /// </summary>
    [ContextMenu("ðŸ”§ ì§„í–‰ ì¤‘ë‹¨ ë¬¸ì œ ì§„ë‹¨")]
    public void DiagnoseStuckProblem()
    {
        Debug.Log("<color=red>â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•</color>");
        Debug.Log("<color=red>ðŸ”§ ì§„í–‰ ì¤‘ë‹¨ ë¬¸ì œ ì§„ë‹¨</color>");
        Debug.Log("<color=red>â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•</color>");

        if (scenarioManager == null)
        {
            Debug.LogError("âŒ ScenarioManagerë¥¼ ì°¾ì„ ìˆ˜ ì—†ìŠµë‹ˆë‹¤!");
            return;
        }

        if (scenarioManager.CurrentSubStep == null)
        {
            Debug.LogError("âŒ í˜„ìž¬ SubStepì´ ì—†ìŠµë‹ˆë‹¤! ì‹œë‚˜ë¦¬ì˜¤ê°€ ì‹œìž‘ë˜ì—ˆëŠ”ì§€ í™•ì¸í•˜ì„¸ìš”.");
            return;
        }

        var subStep = scenarioManager.CurrentSubStep;

        Debug.Log("\n<color=yellow>1. SubStep ë°ì´í„° í™•ì¸</color>");
        Debug.Log($"  â€¢ handTrackingFileName: {(string.IsNullOrEmpty(subStep.handTrackingFileName) ? "<color=red>ì—†ìŒ</color>" : $"<color=green>{subStep.handTrackingFileName}</color>")}");
        Debug.Log($"  â€¢ duration: {subStep.duration}ì´ˆ");
        Debug.Log($"  â€¢ ê°€ì´ë“œ Step: {scenarioManager.CurrentStep.IsGuideStep()}");

        Debug.Log("\n<color=yellow>2. ì§„í–‰ ë°©ì‹ íŒë‹¨</color>");
        if (scenarioManager.CurrentStep.IsGuideStep())
        {
            Debug.Log("  â†’ <color=cyan>ê°€ì´ë“œ Step</color>: í† ê¸€ë¡œ ìˆ˜ë™ ì§„í–‰");
        }
        else if (!string.IsNullOrEmpty(subStep.handTrackingFileName))
        {
            Debug.Log("  â†’ <color=cyan>HandPose ì¡°ê±´</color>: ì† ë™ìž‘ ì™„ë£Œ ì‹œ ìžë™ ì§„í–‰");

            // HandPoseConditionì´ ë“±ë¡ë˜ì—ˆëŠ”ì§€ í™•ì¸
            string conditionKey = $"{scenarioManager.CurrentPhase.phaseName}_{scenarioManager.CurrentStep.stepName}_{subStep.subStepNo}";

            if (conditionManager != null)
            {
                var registryField = conditionManager.GetType().GetField("conditionRegistry", BindingFlags.NonPublic | BindingFlags.Instance);
                if (registryField != null)
                {
                    var registry = registryField.GetValue(conditionManager) as Dictionary<string, IScenarioCondition>;

                    if (registry != null && registry.ContainsKey(conditionKey))
                    {
                        Debug.Log($"    âœ” <color=green>ì¡°ê±´ì´ ë“±ë¡ë˜ì–´ ìžˆìŠµë‹ˆë‹¤</color>");

                        var condition = registry[conditionKey];
                        bool isMet = condition.IsConditionMet();
                        Debug.Log($"    â€¢ ì¡°ê±´ ë§Œì¡±: <color={(isMet ? "green>ì˜ˆ" : "yellow>ì•„ë‹ˆì˜¤")}</color>");

                        if (!isMet)
                        {
                            Debug.LogWarning("\n<color=yellow>ðŸ’¡ ì¡°ê±´ì´ ì•„ì§ ë§Œì¡±ë˜ì§€ ì•Šì•˜ìŠµë‹ˆë‹¤.</color>");
                            Debug.LogWarning("  ê°€ëŠ¥í•œ ì›ì¸:");
                            Debug.LogWarning("  1. HandPosePlayerê°€ ìž¬ìƒ ì¤‘ì´ ì•„ë‹™ë‹ˆë‹¤");
                            Debug.LogWarning("  2. ì§„í–‰ë¥ ì´ ëª©í‘œì— ë„ë‹¬í•˜ì§€ ì•Šì•˜ìŠµë‹ˆë‹¤");
                            Debug.LogWarning("  3. OnSequenceCompleted ë˜ëŠ” OnProgressThresholdReached ì´ë²¤íŠ¸ê°€ ë°œìƒí•˜ì§€ ì•Šì•˜ìŠµë‹ˆë‹¤");

                            // HandPosePlayer ìƒíƒœ í™•ì¸
                            if (handPosePlayer != null)
                            {
                                var currentFrameField = handPosePlayer.GetType().GetField("currentFrameIndex", BindingFlags.NonPublic | BindingFlags.Instance);
                                var totalFramesField = handPosePlayer.GetType().GetField("totalFrames", BindingFlags.NonPublic | BindingFlags.Instance);

                                if (currentFrameField != null && totalFramesField != null)
                                {
                                    int currentFrame = (int)currentFrameField.GetValue(handPosePlayer);
                                    int totalFrames = (int)totalFramesField.GetValue(handPosePlayer);

                                    if (totalFrames > 0)
                                    {
                                        float progress = (float)currentFrame / totalFrames;
                                        Debug.Log($"\n  í˜„ìž¬ ì§„í–‰ë¥ : <color=yellow>{progress * 100f:F1}%</color>");

                                        var progressThresholdField = handPosePlayer.GetType().GetField("progressThreshold", BindingFlags.NonPublic | BindingFlags.Instance);
                                        if (progressThresholdField != null)
                                        {
                                            float threshold = (float)progressThresholdField.GetValue(handPosePlayer);
                                            Debug.Log($"  ëª©í‘œ ì§„í–‰ë¥ : <color=yellow>{threshold * 100f:F0}%</color>");

                                            if (progress >= threshold)
                                            {
                                                Debug.LogError("  âŒâŒ <color=red>ì§„í–‰ë¥ ì€ ëª©í‘œë¥¼ ë‹¬ì„±í–ˆì§€ë§Œ ì´ë²¤íŠ¸ê°€ ë°œìƒí•˜ì§€ ì•Šì•˜ìŠµë‹ˆë‹¤!</color>");
                                                Debug.LogError("  â†’ HandPosePlayerì˜ ì´ë²¤íŠ¸ ë°œìƒ ë¡œì§ì„ í™•ì¸í•˜ì„¸ìš”.");
                                            }
                                        }
                                    }
                                    else
                                    {
                                        Debug.LogError("  âŒ totalFramesê°€ 0ìž…ë‹ˆë‹¤ - HandPosePlayerê°€ ë°ì´í„°ë¥¼ ë¡œë“œí•˜ì§€ ëª»í–ˆìŠµë‹ˆë‹¤!");
                                    }
                                }
                            }
                        }
                        else
                        {
                            Debug.Log($"    âœ” <color=green>ì¡°ê±´ì´ ë§Œì¡±ë˜ì—ˆìŠµë‹ˆë‹¤</color>");

                            // ì¡°ê±´ ì²´í¬ê°€ ì§„í–‰ ì¤‘ì¸ì§€ í™•ì¸
                            if (conditionManager.IsCheckingCondition)
                            {
                                Debug.Log("    âœ” <color=green>ì¡°ê±´ ì²´í¬ê°€ ì§„í–‰ ì¤‘ìž…ë‹ˆë‹¤ - ê³§ ìžë™ìœ¼ë¡œ ì§„í–‰ë  ê²ƒìž…ë‹ˆë‹¤</color>");
                            }
                            else
                            {
                                Debug.LogError("    âŒ <color=red>ì¡°ê±´ì€ ë§Œì¡±ë˜ì—ˆì§€ë§Œ ì¡°ê±´ ì²´í¬ê°€ ì§„í–‰ë˜ì§€ ì•Šê³  ìžˆìŠµë‹ˆë‹¤!</color>");
                                Debug.LogError("    â†’ ScenarioConditionManagerì˜ StartConditionCheck() í˜¸ì¶œ ì—¬ë¶€ë¥¼ í™•ì¸í•˜ì„¸ìš”.");
                            }
                        }
                    }
                    else
                    {
                        Debug.LogError($"    âŒ <color=red>ì¡°ê±´ì´ ë“±ë¡ë˜ì§€ ì•Šì•˜ìŠµë‹ˆë‹¤!</color>");
                        Debug.LogError($"    ì¡°ê±´ í‚¤: {conditionKey}");
                        Debug.LogError("    â†’ ScenarioActionHandler.LoadAndRegisterHandTracking()ì´ í˜¸ì¶œë˜ì—ˆëŠ”ì§€ í™•ì¸í•˜ì„¸ìš”.");
                    }
                }
            }
        }
        else if (subStep.duration > 0)
        {
            Debug.Log($"  â†’ <color=cyan>ì‹œê°„ ê¸°ë°˜</color>: {subStep.duration}ì´ˆ í›„ ìžë™ ì§„í–‰");
        }
        else
        {
            Debug.Log("  â†’ <color=cyan>ìˆ˜ë™ ì§„í–‰</color>: í† ê¸€ ë˜ëŠ” ë²„íŠ¼ í´ë¦­ í•„ìš”");
        }

        Debug.Log("\n<color=yellow>3. ConditionManager ìƒíƒœ</color>");
        if (conditionManager != null)
        {
            Debug.Log($"  â€¢ ì¡°ê±´ ì²´í¬ ì¤‘: <color={(conditionManager.IsCheckingCondition ? "green>ì˜ˆ" : "red>ì•„ë‹ˆì˜¤")}</color>");
        }
        else
        {
            Debug.LogError("  âŒ ScenarioConditionManagerë¥¼ ì°¾ì„ ìˆ˜ ì—†ìŠµë‹ˆë‹¤!");
        }

        Debug.Log("<color=red>â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•</color>");
    }
}