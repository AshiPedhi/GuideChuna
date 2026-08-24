using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 애니메이터 컨트롤러에 클립을 State로 추가하는 도구.
///
/// 환자 애니는 CSV의 patientAnimationClip이 <b>Base Layer의 State 이름</b>과 정확히 일치해야 재생된다
/// (<c>Animator.HasState(0, hash)</c>). 클립만 프로젝트에 있고 컨트롤러에 State가 없으면
/// 콘솔에 "상태 없음"만 찍히고 조용히 넘어간다.
///
/// ★비파괴: 이미 같은 이름의 State가 있으면 건드리지 않는다. 기본 State도 바꾸지 않는다.
/// </summary>
public class AnimatorStateAddTool : EditorWindow
{
    private AnimatorController controller;
    private AnimationClip clip;
    private string stateName = "";
    private string status = "";
    private Vector2 scroll;

    [MenuItem("GuideChuna/환자·리그/애니 컨트롤러에 클립 추가 (State 생성)")]
    public static void Open()
    {
        var w = GetWindow<AnimatorStateAddTool>(true, "컨트롤러에 클립 추가");
        w.minSize = new Vector2(460f, 320f);
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.HelpBox(
            "CSV의 patientAnimationClip은 Base Layer의 State 이름으로 찾습니다.\n" +
            "클립을 컨트롤러에 State로 올려야 재생됩니다. 기존 State·기본 State는 건드리지 않습니다.",
            MessageType.Info);

        controller = (AnimatorController)EditorGUILayout.ObjectField(
            "컨트롤러", controller, typeof(AnimatorController), false);
        clip = (AnimationClip)EditorGUILayout.ObjectField("클립", clip, typeof(AnimationClip), false);

        if (clip != null && string.IsNullOrEmpty(stateName))
            stateName = clip.name;
        stateName = EditorGUILayout.TextField(
            new GUIContent("State 이름", "CSV의 patientAnimationClip과 정확히 같아야 한다(공백 포함)"), stateName);

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(controller == null || clip == null || string.IsNullOrWhiteSpace(stateName)))
        {
            if (GUILayout.Button("Base Layer에 State 추가", GUILayout.Height(30f)))
                Add();
        }

        EditorGUILayout.Space();
        if (controller != null)
        {
            EditorGUILayout.LabelField("현재 Base Layer State", EditorStyles.boldLabel);
            foreach (var n in ListStates(controller))
                EditorGUILayout.LabelField("   " + n, EditorStyles.miniLabel);
        }

        if (!string.IsNullOrEmpty(status))
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(status, MessageType.None);
        }

        EditorGUILayout.EndScrollView();
    }

    private static List<string> ListStates(AnimatorController c)
    {
        var names = new List<string>();
        if (c.layers == null || c.layers.Length == 0) return names;
        var sm = c.layers[0].stateMachine;
        if (sm == null) return names;
        foreach (var s in sm.states)
            if (s.state != null) names.Add(s.state.name);
        return names;
    }

    private void Add()
    {
        var sm = controller.layers[0].stateMachine;
        string want = stateName.Trim();

        foreach (var s in sm.states)
        {
            if (s.state != null && s.state.name == want)
            {
                // 이미 있으면 모션만 확인해 주고 끝낸다(덮어쓰지 않는다).
                status = $"이미 State '{want}'가 있습니다. (모션 = {(s.state.motion != null ? s.state.motion.name : "없음")})\n" +
                         "덮어쓰지 않았습니다. 바꾸려면 Animator 창에서 직접 지정하세요.";
                return;
            }
        }

        Undo.RegisterCompleteObjectUndo(controller, "State 추가");
        var st = sm.AddState(want);
        st.motion = clip;
        st.writeDefaultValues = true;
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        status = $"State '{want}' 추가 완료 (클립 = {clip.name}).\n" +
                 "CSV의 patientAnimationClip에 같은 이름을 넣으면 재생됩니다.\n" +
                 "되돌리려면 Ctrl+Z.";
    }
}
