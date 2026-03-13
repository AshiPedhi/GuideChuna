using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(AnatomyMuscleController))]
public class AnatomyMuscleControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        AnatomyMuscleController controller = (AnatomyMuscleController)target;

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("=== 도구 ===", EditorStyles.boldLabel);

        if (GUILayout.Button("미러 캐시 재구축", GUILayout.Height(30)))
        {
            controller.BuildMirrorCache();
            EditorUtility.SetDirty(controller);
        }

        // Play 모드에서 시나리오별 프리뷰
        if (Application.isPlaying)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("=== 런타임 프리뷰 ===", EditorStyles.boldLabel);

            if (!string.IsNullOrEmpty(controller.CurrentScenario))
            {
                EditorGUILayout.HelpBox($"현재 시나리오: {controller.CurrentScenario}", MessageType.Info);
            }

            // 시나리오별 전환 버튼
            var groupsField = serializedObject.FindProperty("muscleGroups");
            if (groupsField != null)
            {
                for (int i = 0; i < groupsField.arraySize; i++)
                {
                    var element = groupsField.GetArrayElementAtIndex(i);
                    var nameProperty = element.FindPropertyRelative("scenarioName");
                    string name = nameProperty.stringValue;

                    if (string.IsNullOrEmpty(name)) continue;

                    bool isCurrent = name == controller.CurrentScenario;
                    GUI.backgroundColor = isCurrent ? Color.green : Color.white;

                    if (GUILayout.Button(isCurrent ? $"● {name}" : name))
                    {
                        controller.ApplyScenario(name);
                    }
                }
                GUI.backgroundColor = Color.white;

                EditorGUILayout.Space(3);
                if (GUILayout.Button("모두 숨기기"))
                {
                    controller.HideAll();
                }
            }
        }
    }
}
