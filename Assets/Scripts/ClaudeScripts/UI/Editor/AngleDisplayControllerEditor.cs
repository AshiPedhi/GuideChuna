using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(AngleDisplayController))]
public class AngleDisplayControllerEditor : Editor
{
    private AngleDisplayController controller;

    private void OnEnable()
    {
        controller = (AngleDisplayController)target;
    }

    public override void OnInspectorGUI()
    {
        // 기본 Inspector 그리기
        DrawDefaultInspector();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        EditorGUILayout.LabelField("Preset Tools", EditorStyles.boldLabel);

        // 프리셋 추가 버튼
        if (GUILayout.Button("+ Add Preset", GUILayout.Height(25)))
        {
            Undo.RecordObject(controller, "Add AngleDisplay Preset");
            controller.Presets.Add(new AngleDisplayController.AngleDisplayPreset
            {
                presetName = $"NewPreset_{controller.Presets.Count}"
            });
            EditorUtility.SetDirty(controller);
        }

        EditorGUILayout.Space(5);

        // 각 프리셋별 캡처/미리보기/삭제 버튼
        for (int i = 0; i < controller.Presets.Count; i++)
        {
            var preset = controller.Presets[i];
            DrawPresetButtons(preset, i);
        }

        // Play 모드에서 프리셋 전환 버튼
        if (Application.isPlaying && controller.Presets.Count > 0)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            EditorGUILayout.LabelField("Runtime Preset Switch", EditorStyles.boldLabel);

            // 현재 적용된 프리셋 표시
            if (!string.IsNullOrEmpty(controller.CurrentPresetName))
            {
                EditorGUILayout.LabelField($"Current: {controller.CurrentPresetName}", EditorStyles.boldLabel);
            }

            foreach (var preset in controller.Presets)
            {
                bool isCurrent = preset.presetName == controller.CurrentPresetName;
                GUI.backgroundColor = isCurrent ? new Color(0.6f, 1f, 0.6f) : Color.white;

                if (GUILayout.Button($"Apply: {preset.presetName}"))
                {
                    controller.ApplyPreset(preset.presetName);
                }
            }
            GUI.backgroundColor = Color.white;
        }
    }

    private void DrawPresetButtons(AngleDisplayController.AngleDisplayPreset preset, int index)
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField($"[{index}] {preset.presetName}", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();

        // 캡처 버튼
        GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
        if (GUILayout.Button("Capture Current", GUILayout.Height(22)))
        {
            Undo.RecordObject(controller, "Capture AngleDisplay Settings");
            controller.CaptureCurrentSettings(preset);
            EditorUtility.SetDirty(controller);
            Debug.Log($"[AngleDisplayControllerEditor] Captured: {preset.presetName}");
        }

        // 미리보기 버튼
        GUI.backgroundColor = new Color(0.6f, 0.8f, 1f);
        if (GUILayout.Button("Preview", GUILayout.Height(22)))
        {
            Undo.RecordObject(controller, "Preview AngleDisplay Preset");
            Undo.RecordObject(controller.transform, "Preview AngleDisplay Transform");
            controller.ApplyPreset(preset);
            Debug.Log($"[AngleDisplayControllerEditor] Preview: {preset.presetName}");
        }

        // 삭제 버튼
        GUI.backgroundColor = new Color(1f, 0.6f, 0.6f);
        if (GUILayout.Button("X", GUILayout.Width(25), GUILayout.Height(22)))
        {
            if (EditorUtility.DisplayDialog("Delete Preset",
                $"'{preset.presetName}' preset will be deleted.", "Delete", "Cancel"))
            {
                Undo.RecordObject(controller, "Delete AngleDisplay Preset");
                controller.Presets.RemoveAt(index);
                EditorUtility.SetDirty(controller);
            }
        }

        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        // 프리셋 정보 라벨
        EditorGUI.indentLevel++;
        EditorGUILayout.LabelField($"pos={preset.position} rot={preset.rotation} scale={preset.scale}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"axis={preset.rotationAxis} angle={preset.startAngle}°~{preset.endAngle}° invert={preset.invertAngle}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"syncSource={preset.syncSource}", EditorStyles.miniLabel);
        EditorGUI.indentLevel--;

        EditorGUILayout.EndVertical();
    }
}
