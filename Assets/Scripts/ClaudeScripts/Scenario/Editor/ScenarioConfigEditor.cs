using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(ScenarioConfig))]
public class ScenarioConfigEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        ScenarioConfig config = (ScenarioConfig)target;
        string name = config.scenarioName;

        if (string.IsNullOrEmpty(name))
        {
            EditorGUILayout.HelpBox("scenarioName을 입력하면 자동 해결 경로가 표시됩니다.", MessageType.Info);
            return;
        }

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("자동 해결 경로 (scenarioName 기준)", EditorStyles.boldLabel);

        string narrationFolder = string.IsNullOrEmpty(config.narrationSubFolder) ? name : config.narrationSubFolder;

        DrawPathRow("CSV 데이터", $"Resources/Scenarios/{name}.csv",
            ResourceExists<TextAsset>($"Scenarios/{name}"));

        DrawPathRow("Animator Controller",
            config.animatorController != null
                ? AssetDatabase.GetAssetPath(config.animatorController)
                : $"Resources/Animators/{name} (미할당 - 폴백)",
            config.animatorController != null || ResourceExists<RuntimeAnimatorController>($"Animators/{name}"));

        DrawPathRow("나레이션 (초급)", $"Resources/Narrations/Beginner/{narrationFolder}/",
            FolderHasAssets($"Assets/Resources/Narrations/Beginner/{narrationFolder}"));

        DrawPathRow("나레이션 (중급)", $"Resources/Narrations/Intermediate/{narrationFolder}/",
            FolderHasAssets($"Assets/Resources/Narrations/Intermediate/{narrationFolder}"));

        DrawPathRow("나레이션 (공통 폴백)", "Resources/Narrations/{난이도}/{clipName}", true);
    }

    private void DrawPathRow(string label, string path, bool exists)
    {
        EditorGUILayout.BeginHorizontal();

        string icon = exists ? "\u2705" : "\u274C";
        EditorGUILayout.LabelField($"  {icon} {label}", GUILayout.Width(180));
        EditorGUILayout.LabelField(path, EditorStyles.miniLabel);

        EditorGUILayout.EndHorizontal();
    }

    private bool ResourceExists<T>(string resourcePath) where T : Object
    {
        T asset = Resources.Load<T>(resourcePath);
        return asset != null;
    }

    private bool FolderHasAssets(string folderPath)
    {
        return AssetDatabase.IsValidFolder(folderPath);
    }
}
