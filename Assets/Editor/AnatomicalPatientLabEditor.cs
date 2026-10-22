using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(AnatomicalPatientLab))]
public sealed class AnatomicalPatientLabEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var lab = (AnatomicalPatientLab)target;
        EditorGUILayout.HelpBox("실험용 관절·스키닝 모델입니다. 피부/골격/근육은 같은 출력 리그를 사용합니다. 임상 정확도·Play·Quest 성능은 별도 검증이 필요합니다.", MessageType.Info);
        if (EditorUtility.IsPersistent(lab))
        {
            EditorGUILayout.HelpBox("자세 시험은 생성된 실험 씬 또는 씬에 배치한 복제본에서 실행하세요. 프로젝트의 프리팹 자산에는 미리보기 자세를 기록하지 않습니다.", MessageType.Info);
            return;
        }
        if (GUILayout.Button("상의 숨기고 피부 검사")) { Undo.RegisterFullObjectHierarchyUndo(lab.gameObject, "Inspect patient skin"); lab.SetUpperClothingVisible(false); SceneView.RepaintAll(); }
        if (GUILayout.Button("원래 상의 표시")) { Undo.RegisterFullObjectHierarchyUndo(lab.gameObject, "Show patient shirt"); lab.SetUpperClothingVisible(true); SceneView.RepaintAll(); }
        foreach (AnatomicalPatientLab.View view in System.Enum.GetValues(typeof(AnatomicalPatientLab.View)))
            if (GUILayout.Button(view.ToString())) { Undo.RecordObject(lab, "Patient view"); lab.SetView(view); SceneView.RepaintAll(); }
        foreach (AnatomicalPatientLab.Sample sample in System.Enum.GetValues(typeof(AnatomicalPatientLab.Sample)))
            if (GUILayout.Button(sample.ToString()))
            {
                if (lab.Rig == null) continue;
                Undo.RegisterFullObjectHierarchyUndo(lab.gameObject, "Preview anatomical pose");
                lab.SetSample(sample); SceneView.RepaintAll();
            }
        if (lab.Rig != null && GUILayout.Button("Evaluate inspector pose"))
        {
            Undo.RegisterFullObjectHierarchyUndo(lab.gameObject, "Preview anatomical pose");
            lab.Rig.SetInputSource(AnatomicalPatientRig.InputSource.Manual);
            lab.Rig.Evaluate(lab.Rig.manualPose); SceneView.RepaintAll();
        }
    }
}
