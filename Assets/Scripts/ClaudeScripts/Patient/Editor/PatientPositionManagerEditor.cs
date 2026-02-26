using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(PatientPositionManager))]
public class PatientPositionManagerEditor : Editor
{
    private PatientPositionManager manager;

    private void OnEnable()
    {
        manager = (PatientPositionManager)target;
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
            Undo.RecordObject(manager, "Add Preset");
            manager.Presets.Add(new PatientPositionManager.PositionPreset
            {
                presetName = $"NewPreset_{manager.Presets.Count}",
                bedActive = true
            });
            EditorUtility.SetDirty(manager);
        }

        EditorGUILayout.Space(5);

        // 각 프리셋별 캡처/미리보기/삭제 버튼
        for (int i = 0; i < manager.Presets.Count; i++)
        {
            var preset = manager.Presets[i];
            DrawPresetButtons(preset, i);
        }

        // Play 모드에서 프리셋 전환 버튼
        if (Application.isPlaying && manager.Presets.Count > 0)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            EditorGUILayout.LabelField("Runtime Preset Switch", EditorStyles.boldLabel);

            foreach (var preset in manager.Presets)
            {
                if (GUILayout.Button($"Apply: {preset.presetName}"))
                {
                    manager.ApplyPreset(preset.presetName);
                }
            }
        }
    }

    private void DrawPresetButtons(PatientPositionManager.PositionPreset preset, int index)
    {
        EditorGUILayout.BeginVertical("box");
        EditorGUILayout.LabelField($"[{index}] {preset.presetName}", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();

        // 캡처 버튼
        GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
        if (GUILayout.Button("Capture Current", GUILayout.Height(22)))
        {
            Undo.RecordObject(manager, "Capture Position");
            manager.CaptureCurrentPosition(preset);
            EditorUtility.SetDirty(manager);
            Debug.Log($"[PatientPositionManagerEditor] Captured: {preset.presetName}");
        }

        // 미리보기 버튼
        GUI.backgroundColor = new Color(0.6f, 0.8f, 1f);
        if (GUILayout.Button("Preview", GUILayout.Height(22)))
        {
            Undo.RecordObject(manager, "Preview Preset");
            if (manager.PatientRoot != null)
                Undo.RecordObject(manager.PatientRoot, "Preview Patient");
            if (manager.BedRoot != null)
                Undo.RecordObject(manager.BedRoot, "Preview Bed");
            if (manager.CameraRoot != null)
                Undo.RecordObject(manager.CameraRoot, "Preview Camera");
            if (manager.SkeletonModelRoot != null)
                Undo.RecordObject(manager.SkeletonModelRoot, "Preview Skeleton");

            manager.ApplyPreset(preset);
            Debug.Log($"[PatientPositionManagerEditor] Preview: {preset.presetName}");
        }

        // 삭제 버튼
        GUI.backgroundColor = new Color(1f, 0.6f, 0.6f);
        if (GUILayout.Button("X", GUILayout.Width(25), GUILayout.Height(22)))
        {
            if (EditorUtility.DisplayDialog("Delete Preset",
                $"'{preset.presetName}' preset will be deleted.", "Delete", "Cancel"))
            {
                Undo.RecordObject(manager, "Delete Preset");
                manager.Presets.RemoveAt(index);
                EditorUtility.SetDirty(manager);
            }
        }

        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        // 위치 정보 표시
        EditorGUI.indentLevel++;
        EditorGUILayout.LabelField($"Patient: pos={preset.patientPosition} rot={preset.patientRotation}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"Bed: pos={preset.bedPosition} rot={preset.bedRotation} active={preset.bedActive}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"Camera: pos={preset.cameraPosition} rot={preset.cameraRotation}", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"Skeleton: pos={preset.skeletonModelPosition} rot={preset.skeletonModelRotation}", EditorStyles.miniLabel);
        EditorGUI.indentLevel--;

        EditorGUILayout.EndVertical();
    }
}
