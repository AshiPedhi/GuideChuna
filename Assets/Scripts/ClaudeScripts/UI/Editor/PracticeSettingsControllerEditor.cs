using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(PracticeSettingsController))]
public class PracticeSettingsControllerEditor : Editor
{
    private PracticeSettingsController controller;
    private bool editMode = false;

    private void OnEnable()
    {
        controller = (PracticeSettingsController)target;
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        EditorGUILayout.LabelField("맞춤 설정 미리보기", EditorStyles.boldLabel);

        // 현재 목표 위치 표시
        Vector3? targetPos = controller.CalculateTargetPosition();
        if (targetPos != null)
        {
            EditorGUILayout.HelpBox(
                $"목표 위치: {targetPos.Value:F3}\n" +
                $"전방 거리: {controller.DefaultForwardDistance}m\n" +
                $"높이 오프셋: {controller.DefaultHeightOffset}m",
                MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox("헤드셋 또는 기준점이 할당되지 않아 목표 위치를 계산할 수 없습니다.", MessageType.Warning);
        }

        EditorGUILayout.BeginHorizontal();

        // Scene뷰 핸들 편집 토글
        GUI.backgroundColor = editMode ? new Color(1f, 0.8f, 0.3f) : new Color(0.9f, 0.9f, 0.5f);
        string editLabel = editMode ? "■ 위치 편집 중" : "✎ Scene에서 위치 편집";
        if (GUILayout.Button(editLabel, GUILayout.Height(28)))
        {
            editMode = !editMode;
            SceneView.RepaintAll();
        }

        // Preview 버튼 — 목표 위치로 실제 이동
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

    /// <summary>
    /// Preview: 대상 오브젝트를 목표 위치로 실제 이동 (Undo 지원)
    /// </summary>
    private void ApplyPreview()
    {
        Vector3? targetPos = controller.CalculateTargetPosition();
        if (targetPos == null)
        {
            Debug.LogWarning("[PracticeSettings] 목표 위치를 계산할 수 없습니다.");
            return;
        }

        Transform actualTarget = controller.GetActualMoveTarget();
        if (actualTarget == null)
        {
            Debug.LogWarning("[PracticeSettings] 이동 대상 오브젝트가 없습니다.");
            return;
        }

        // 런타임 OnCustomPositioning과 동일한 pivot 보정 — 자식(targetObject)이 목표 위치에 오도록 부모 배치
        Transform child = controller.TargetObject;
        Vector3 pivotOffset = (child != null) ? (actualTarget.position - child.position) : Vector3.zero;

        Undo.RecordObject(actualTarget, "Preview Custom Positioning");
        actualTarget.position = targetPos.Value + pivotOffset;
        Debug.Log($"[PracticeSettings] Preview: {(child != null ? child.name : actualTarget.name)} → {targetPos.Value} (부모 pivot 보정 {pivotOffset.magnitude:F3}m)");
    }

    /// <summary>
    /// Scene뷰에서 목표 위치 핸들 표시 + 드래그로 편집
    /// </summary>
    private void OnSceneGUI()
    {
        if (!editMode) return;

        Vector3? targetPos = controller.CalculateTargetPosition();
        if (targetPos == null) return;

        // 위치 핸들
        EditorGUI.BeginChangeCheck();
        Handles.color = new Color(1f, 0.9f, 0.2f, 0.9f);
        Vector3 newPos = Handles.PositionHandle(targetPos.Value, Quaternion.identity);

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(controller, "Adjust Positioning Target");

            if (controller.CustomReferencePoint != null)
            {
                // 커스텀 기준점 모드 — 기준점 자체를 이동
                Undo.RecordObject(controller.CustomReferencePoint, "Move Reference Point");
                controller.CustomReferencePoint.position = newPos;
            }
            else if (controller.HeadsetTransform != null)
            {
                // 헤드셋 기준 모드 — 핸들 위치에서 forwardDistance/heightOffset 역계산
                Vector3 headsetPos = controller.HeadsetTransform.position;
                Vector3 headsetForward = controller.HeadsetTransform.forward;
                headsetForward.y = 0;
                headsetForward.Normalize();

                // 높이 오프셋 = 목표 Y - 헤드셋 Y
                controller.DefaultHeightOffset = newPos.y - headsetPos.y;

                // 전방 거리 = 헤드셋 전방 방향으로의 수평 거리
                Vector3 horizontalDelta = new Vector3(newPos.x - headsetPos.x, 0, newPos.z - headsetPos.z);
                float forwardDist = Vector3.Dot(horizontalDelta, headsetForward);
                controller.DefaultForwardDistance = Mathf.Max(0.1f, forwardDist);
            }

            EditorUtility.SetDirty(controller);
        }

        // 라벨
        Handles.color = Color.yellow;
        Handles.Label(newPos + Vector3.up * 0.25f,
            $"전방: {controller.DefaultForwardDistance:F2}m\n높이 오프셋: {controller.DefaultHeightOffset:F2}m",
            new GUIStyle
            {
                normal = { textColor = Color.yellow },
                fontSize = 12,
                fontStyle = FontStyle.Bold
            });
    }
}
