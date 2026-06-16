using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(PracticeManager))]
public class PracticeManagerPositionEditor : Editor
{
    private PracticeManager manager;
    private bool editMode = false;

    // 에디터 프리뷰용 가상 헤드셋 높이 (런타임에서는 실제 헤드셋 Y가 사용됨)
    private const string PrefKeyHeight = "PracticeManager_PreviewHeadsetY";
    private float previewHeadsetY = 1.6f;

    private void OnEnable()
    {
        manager = (PracticeManager)target;
        previewHeadsetY = EditorPrefs.GetFloat(PrefKeyHeight, 1.6f);
    }

    /// <summary>
    /// 에디터 프리뷰용 헤드셋 기준 목표 위치 계산.
    /// X/Z는 씬 헤드셋의 forward를 쓰고, Y만 가상 높이로 대체.
    /// </summary>
    private Vector3? CalculatePreviewTargetPosition()
    {
        Transform headset = manager.FindHeadsetTransform();
        if (headset == null) return null;

        Vector3 headsetForward = headset.forward;
        headsetForward.y = 0;
        headsetForward.Normalize();

        // 가상 헤드셋 위치: 실제 X/Z + 편집용 Y
        Vector3 virtualHeadsetPos = new Vector3(headset.position.x, previewHeadsetY, headset.position.z);

        return new Vector3(
            virtualHeadsetPos.x + headsetForward.x * manager.PatientForwardDistance,
            virtualHeadsetPos.y + manager.PatientHeightOffset,
            virtualHeadsetPos.z + headsetForward.z * manager.PatientForwardDistance);
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        EditorGUILayout.LabelField("환자 위치 미리보기", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        previewHeadsetY = EditorGUILayout.Slider(
            new GUIContent("프리뷰 헤드셋 높이 (m)",
                "런타임 사용자의 예상 헤드셋 Y. 1.6 ≈ 선 자세, 1.2 ≈ 앉은 자세.\n" +
                "이 값은 에디터 미리보기 전용이며 런타임 동작엔 영향 없음."),
            previewHeadsetY, 0.8f, 2.0f);
        if (EditorGUI.EndChangeCheck())
        {
            EditorPrefs.SetFloat(PrefKeyHeight, previewHeadsetY);
            SceneView.RepaintAll();
        }

        Vector3? targetPos = CalculatePreviewTargetPosition();
        if (targetPos != null)
        {
            EditorGUILayout.HelpBox(
                $"프리뷰 목표 위치: {targetPos.Value:F3}\n" +
                $"전방 거리: {manager.PatientForwardDistance:F2}m\n" +
                $"높이 오프셋: {manager.PatientHeightOffset:F2}m  (실제 환자 Y = 사용자 헤드셋 Y {previewHeadsetY:F2} + 오프셋)",
                MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "OVRCameraRig/TrackingSpace/CenterEyeAnchor 를 찾을 수 없습니다.",
                MessageType.Warning);
        }

        EditorGUILayout.BeginHorizontal();

        GUI.backgroundColor = editMode ? new Color(1f, 0.8f, 0.3f) : new Color(0.9f, 0.9f, 0.5f);
        string editLabel = editMode ? "■ 위치 편집 중" : "✎ Scene에서 위치 편집";
        if (GUILayout.Button(editLabel, GUILayout.Height(28)))
        {
            editMode = !editMode;
            SceneView.RepaintAll();
        }

        GUI.backgroundColor = new Color(0.6f, 0.8f, 1f);
        if (GUILayout.Button("Preview (이동)", GUILayout.Height(28)))
        {
            ApplyPreview();
        }

        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        if (editMode)
        {
            EditorGUILayout.HelpBox(
                "Scene뷰에서 노란 구체 핸들을 드래그하여 목표 위치를 조정합니다.\n" +
                "전방 거리와 높이 오프셋이 자동으로 역계산됩니다.",
                MessageType.Info);
        }
    }

    private void ApplyPreview()
    {
        Vector3? targetPos = CalculatePreviewTargetPosition();
        if (targetPos == null)
        {
            Debug.LogWarning("[PracticeManager] 목표 위치를 계산할 수 없습니다.");
            return;
        }

        Transform actualTarget = manager.GetPatientMoveTarget();
        if (actualTarget == null)
        {
            Debug.LogWarning("[PracticeManager] 이동 대상 오브젝트가 없습니다.");
            return;
        }

        Undo.RecordObject(actualTarget, "Preview Patient Position");
        actualTarget.position = targetPos.Value;

        // 환자가 헤드셋 반대 방향을 바라보도록 (등을 보여주도록)
        Transform headset = manager.FindHeadsetTransform();
        if (headset != null)
        {
            Vector3 lookDir = targetPos.Value - headset.position;
            lookDir.y = 0;
            if (lookDir.sqrMagnitude > 0.001f)
                actualTarget.rotation = Quaternion.LookRotation(lookDir);
        }

        Debug.Log($"[PracticeManager] Preview: {actualTarget.name} → {targetPos.Value}");
    }

    private void OnSceneGUI()
    {
        Vector3? targetPos = CalculatePreviewTargetPosition();
        if (targetPos == null) return;

        // 항상 구체 표시 (편집 모드 아닐 때는 위치 확인용)
        Handles.color = editMode ? new Color(1f, 0.6f, 0.1f, 0.6f) : new Color(1f, 0.9f, 0.2f, 0.35f);
        Handles.SphereHandleCap(0, targetPos.Value, Quaternion.identity, 0.15f, EventType.Repaint);

        Handles.color = Color.yellow;
        Handles.Label(targetPos.Value + Vector3.up * 0.25f,
            $"[환자 목표]\n전방: {manager.PatientForwardDistance:F2}m\n높이: {manager.PatientHeightOffset:F2}m",
            new GUIStyle
            {
                normal = { textColor = Color.yellow },
                fontSize = 12,
                fontStyle = FontStyle.Bold
            });

        if (!editMode) return;

        EditorGUI.BeginChangeCheck();
        Vector3 newPos = Handles.PositionHandle(targetPos.Value, Quaternion.identity);

        if (EditorGUI.EndChangeCheck())
        {
            Transform headset = manager.FindHeadsetTransform();
            if (headset == null) return;

            Undo.RecordObject(manager, "Adjust Patient Position");

            Vector3 headsetPos = headset.position;
            Vector3 headsetForward = headset.forward;
            headsetForward.y = 0;
            headsetForward.Normalize();

            // Y는 가상 헤드셋 높이 기준으로 역계산 (런타임 Y 오프셋과 일치)
            manager.PatientHeightOffset = newPos.y - previewHeadsetY;

            Vector3 horizontalDelta = new Vector3(newPos.x - headsetPos.x, 0, newPos.z - headsetPos.z);
            float forwardDist = Vector3.Dot(horizontalDelta, headsetForward);
            manager.PatientForwardDistance = Mathf.Max(0.1f, forwardDist);

            EditorUtility.SetDirty(manager);
        }
    }
}
