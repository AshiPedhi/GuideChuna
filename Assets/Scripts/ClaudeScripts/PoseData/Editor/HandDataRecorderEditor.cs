using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(HandDataRecorder))]
public class HandDataRecorderEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        HandDataRecorder recorder = (HandDataRecorder)target;

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("녹화 제어", EditorStyles.boldLabel);

        // 기준점 미설정 경고
        if (recorder.PatientReference == null)
        {
            EditorGUILayout.HelpBox(
                "patientReference가 설정되지 않았습니다.\n절대 좌표로 저장됩니다. 환자 모델 기준 상대좌표를 원하면 기준점을 할당하세요.",
                MessageType.Warning);
        }

        // Play 모드에서만 녹화 버튼 활성
        if (Application.isPlaying)
        {
            if (recorder.IsRecording)
            {
                // 녹화 중 상태 표시
                EditorGUILayout.HelpBox(
                    $"녹화 중...\n" +
                    $"경과 시간: {recorder.RecordingDuration:F1}초\n" +
                    $"프레임 수: {recorder.RecordedFrames}\n" +
                    $"파일명: {recorder.FileName}.csv",
                    MessageType.Info);

                GUI.backgroundColor = new Color(1f, 0.3f, 0.3f);
                if (GUILayout.Button("녹화 중지", GUILayout.Height(40)))
                {
                    recorder.StopRecording();
                }
                GUI.backgroundColor = Color.white;

                // 녹화 중에는 Inspector 계속 갱신
                Repaint();
            }
            else
            {
                GUI.backgroundColor = new Color(0.3f, 1f, 0.3f);
                if (GUILayout.Button("녹화 시작", GUILayout.Height(40)))
                {
                    recorder.StartRecording();
                }
                GUI.backgroundColor = Color.white;
            }
        }
        else
        {
            EditorGUI.BeginDisabledGroup(true);
            GUILayout.Button("Play 모드에서 녹화 가능", GUILayout.Height(40));
            EditorGUI.EndDisabledGroup();
        }

        EditorGUILayout.Space(5);

        // 저장 폴더 열기 버튼
        if (GUILayout.Button("저장 폴더 열기"))
        {
            string folder = HandDataRecorder.GetSaveFolder();
            if (System.IO.Directory.Exists(folder))
            {
                EditorUtility.RevealInFinder(folder);
            }
            else
            {
                EditorUtility.DisplayDialog("폴더 없음",
                    $"저장 폴더가 아직 존재하지 않습니다:\n{folder}\n\n녹화를 완료하면 자동으로 생성됩니다.",
                    "확인");
            }
        }

        // 저장 경로 표시
        EditorGUILayout.Space(3);
        EditorGUILayout.LabelField("저장 경로", EditorStyles.miniLabel);
        EditorGUILayout.SelectableLabel(recorder.GetSavePath(), EditorStyles.miniTextField, GUILayout.Height(18));
    }
}
