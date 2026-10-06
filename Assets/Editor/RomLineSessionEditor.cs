// ★[선 실측 — 끝점 맞추기] 2026-10-06 신설
// 찍는 점은 «컨트롤러 원점 + tipOffset»이다. 헤드셋에서 실제 컨트롤러와 노란 점이 어긋나 보일 때,
// Play 중에 인스펙터에서 mm 단위로 밀어 보고 그 값을 남길 수 있게 한다.
//   · tipOffset은 매 프레임 읽으므로 Play 중에 고치면 바로 점이 움직인다.
//   · Play 중에 고친 값은 Play를 멈추면 사라진다 → 멈출 때 기억해 뒀다가 씬의 값에 옮겨 적는다(저장은 사람이 Ctrl+S).
//   · 씬 창에는 컨트롤러 원점의 세 축과 끝점까지의 노란 선이 보인다(RomLineSession.OnDrawGizmos).

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(RomLineSession))]
public class RomLineSessionEditor : Editor
{
    private static readonly string[] AxisName = { "좌우  (+ 오른쪽)", "위아래 (+ 위)", "앞뒤  (+ 앞)" };
    private static readonly float[] Steps = { -5f, -1f, 1f, 5f };

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        serializedObject.Update();
        SerializedProperty prop = serializedObject.FindProperty("tipOffset");
        if (prop == null) return;

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("끝점 맞추기 (mm)", EditorStyles.boldLabel);
        Vector3 mm = prop.vector3Value * 1000f;
        for (int i = 0; i < 3; i++)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(AxisName[i], GUILayout.Width(110f));
            foreach (float st in Steps)
                if (GUILayout.Button(st.ToString("+0;-0"), GUILayout.Width(34f))) mm[i] += st;
            mm[i] = EditorGUILayout.FloatField(mm[i], GUILayout.Width(60f));
            EditorGUILayout.LabelField("mm", GUILayout.Width(26f));
            EditorGUILayout.EndHorizontal();
        }
        Vector3 next = mm / 1000f;
        if (next != prop.vector3Value)
        {
            prop.vector3Value = next;
            serializedObject.ApplyModifiedProperties();
        }

        var s = (RomLineSession)target;
        string live = !Application.isPlaying ? "Play 중에 고치면 노란 점이 바로 움직인다."
                    : s.TipValid ? $"지금 쓰는 컨트롤러: {(s.UsingLeft ? "왼쪽(좌우 값은 뒤집어 적용)" : "오른쪽")} · 끝점 {s.TipWorld:F3}"
                    : "컨트롤러가 추적되지 않는다.";
        EditorGUILayout.HelpBox(
            "기준은 오른손 컨트롤러다(컨트롤러를 쥐고 앞을 가리킬 때의 오른쪽 · 위 · 앞). 왼손은 좌우만 뒤집어 쓴다.\n" + live +
            "\nPlay 중에 고친 값은 Play를 멈출 때 씬에 옮겨 적는다 — 그 뒤 Ctrl+S로 저장한다.", MessageType.Info);
        if (Application.isPlaying) Repaint();
    }
}

/// <summary>Play 중에 고친 tipOffset을 Play가 끝난 뒤 씬의 값에 옮겨 적는다.</summary>
[InitializeOnLoad]
public static class RomLineTipOffsetKeeper
{
    private const string Key = "RomLineSession.tipOffset.afterPlay";

    static RomLineTipOffsetKeeper()
    {
        EditorApplication.playModeStateChanged += OnChange;
    }

    private static void OnChange(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingPlayMode)
        {
            var s = Object.FindFirstObjectByType<RomLineSession>();
            if (s == null) { SessionState.EraseString(Key); return; }
            Vector3 v = new SerializedObject(s).FindProperty("tipOffset").vector3Value;
            SessionState.SetString(Key, JsonUtility.ToJson(v));
        }
        else if (change == PlayModeStateChange.EnteredEditMode)
        {
            string json = SessionState.GetString(Key, "");
            SessionState.EraseString(Key);
            if (json.Length == 0) return;
            var s = Object.FindFirstObjectByType<RomLineSession>();
            if (s == null) return;
            Vector3 played = JsonUtility.FromJson<Vector3>(json);
            var so = new SerializedObject(s);
            SerializedProperty prop = so.FindProperty("tipOffset");
            if ((prop.vector3Value - played).sqrMagnitude < 1e-10f) return;   // Play 중에 안 고쳤다

            Vector3 before = prop.vector3Value;
            prop.vector3Value = played;
            so.ApplyModifiedProperties();   // Undo에 걸린다(Ctrl+Z로 되돌아간다)
            EditorSceneManager.MarkSceneDirty(s.gameObject.scene);
            Debug.Log($"[선실측] 끝점 오프셋 — Play 중에 고친 값을 씬에 옮겨 적었다: {before * 1000f:F1} → {played * 1000f:F1} mm. 저장은 Ctrl+S.");
        }
    }
}
