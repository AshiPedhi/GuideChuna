using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// 설정창(<c>Setting</c>) 안의 <b>닫기 버튼</b>을 <see cref="InfoPanelController.CloseSettingsPopup"/>에 잇는다.
///
/// <b>왜 필요한가</b> — 2026-08-31 실측: 설정 패널의 컨트롤 6개 중 5개(위치 조정·환자 위치·골격 표시·
/// 환자 모델 표시·현실 모드)는 <c>PracticeSettingsController</c>가 코드로 리스너를 다는데,
/// 타이틀 바의 닫기 버튼만 <b>어느 스크립트도 참조하지 않아</b> 눌러도 아무 일이 없었다.
/// 닫기를 처리할 코드(<c>SettingsPopupController.closeButton</c>)는 있었지만 그 컴포넌트가 씬에 없었다.
///
/// ★<b>기본은 조회다.</b> [찾기]로 후보를 보고, 어느 것인지 확인한 뒤에 그 줄의 [배선]을 누른다.
///   자동으로 고르지 않는다 — 닫기 버튼임을 이름으로 확정할 근거가 없다(프로젝트 어디에도
///   '닫기'라는 문자열이 없다). 위치·크기로 짐작할 뿐이라 사람이 봐야 한다.
///
/// ★씬을 저장하는 것도 사람이 한다. 이 도구는 dirty 표시만 한다.
/// </summary>
public class SettingsCloseButtonWireTool : EditorWindow
{
    private class Candidate
    {
        public GameObject go;
        public string path;
        public Button button;          // 둘 중 하나만 채워진다
        public Toggle toggle;
        public int persistentCalls;
        public bool wiredToClose;
        public string ownerNote;       // 이미 다른 스크립트가 물고 있으면 그 이름
    }

    private InfoPanelController infoPanel;
    private GameObject settingRoot;
    private readonly List<Candidate> candidates = new List<Candidate>();
    private Vector2 scroll;
    private string message;

    [MenuItem("GuideChuna/설정창 닫기 버튼 배선")]
    private static void Open()
    {
        GetWindow<SettingsCloseButtonWireTool>(true, "설정창 닫기 버튼 배선").minSize = new Vector2(620, 360);
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "설정 패널의 닫기 버튼을 InfoPanelController.CloseSettingsPopup() 에 잇는다.\n" +
            "CloseSettingsPopup()은 settingsToggle을 끄고 OnSettingsToggleChanged(false)를 부른다 — " +
            "setting 버튼을 다시 누른 것과 같은 경로다.",
            MessageType.Info);

        EditorGUILayout.Space();
        infoPanel = (InfoPanelController)EditorGUILayout.ObjectField(
            "InfoPanelController", infoPanel, typeof(InfoPanelController), true);
        settingRoot = (GameObject)EditorGUILayout.ObjectField(
            "설정 패널 루트", settingRoot, typeof(GameObject), true);

        EditorGUILayout.Space();
        if (GUILayout.Button("찾기 (조회만)", GUILayout.Height(28))) Scan();

        if (!string.IsNullOrEmpty(message))
            EditorGUILayout.HelpBox(message, MessageType.None);

        if (candidates.Count == 0) return;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"후보 {candidates.Count}개", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "★'물린 곳 없음'이면서 타이틀 바에 있는 작은 정사각형이 닫기 버튼일 가능성이 높다.",
            EditorStyles.miniLabel);

        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (Candidate c in candidates)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(c.go.name, EditorStyles.boldLabel, GUILayout.Width(260));
            EditorGUILayout.LabelField(c.button != null ? "Button (On Click)" : "Toggle (On Value Changed)",
                                       GUILayout.Width(160));
            if (GUILayout.Button("선택", GUILayout.Width(50)))
                Selection.activeGameObject = c.go;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(c.path, EditorStyles.miniLabel);

            RectTransform rt = c.go.transform as RectTransform;
            if (rt != null)
                EditorGUILayout.LabelField($"크기 {rt.sizeDelta.x:F0}×{rt.sizeDelta.y:F0} · " +
                                           $"앵커 ({rt.anchorMin.x:F2},{rt.anchorMin.y:F2})", EditorStyles.miniLabel);

            if (c.wiredToClose)
                EditorGUILayout.LabelField("✔ 이미 CloseSettingsPopup에 배선돼 있다", EditorStyles.miniLabel);
            else if (!string.IsNullOrEmpty(c.ownerNote))
                EditorGUILayout.LabelField($"물린 곳: {c.ownerNote}", EditorStyles.miniLabel);
            else
                EditorGUILayout.LabelField($"물린 곳 없음 (등록된 호출 {c.persistentCalls}개)", EditorStyles.miniLabel);

            using (new EditorGUI.DisabledScope(c.wiredToClose || infoPanel == null))
            {
                if (GUILayout.Button("이 버튼을 닫기에 배선", GUILayout.Height(22)))
                    Wire(c);
            }

            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndScrollView();
    }

    private void Scan()
    {
        candidates.Clear();
        message = "";

        if (infoPanel == null) infoPanel = FindFirstObjectByType<InfoPanelController>(FindObjectsInactive.Include);
        if (infoPanel == null)
        {
            message = "★InfoPanelController를 씬에서 못 찾았다. 위 칸에 직접 넣어라.";
            return;
        }

        if (settingRoot == null) settingRoot = FindSettingRoot();
        if (settingRoot == null)
        {
            message = "★설정 패널 루트를 못 찾았다. InfoPanelController의 settingsPopup을 위 칸에 넣어라.";
            return;
        }

        // ★비활성 오브젝트도 봐야 한다 — 설정 패널은 평소 꺼져 있다.
        foreach (Button b in settingRoot.GetComponentsInChildren<Button>(true))
            candidates.Add(Build(b.gameObject, b, null, b.onClick));
        foreach (Toggle t in settingRoot.GetComponentsInChildren<Toggle>(true))
            candidates.Add(Build(t.gameObject, null, t, t.onValueChanged));

        message = $"'{settingRoot.name}' 아래에서 {candidates.Count}개를 찾았다. " +
                  "어느 것이 닫기 버튼인지 [선택]으로 하이어라키에서 확인한 뒤 배선해라.";
    }

    private Candidate Build(GameObject go, Button b, Toggle t, UnityEventBase evt)
    {
        var c = new Candidate
        {
            go = go,
            button = b,
            toggle = t,
            path = GetPath(go),
            persistentCalls = evt.GetPersistentEventCount(),
        };

        var owners = new List<string>();
        for (int i = 0; i < evt.GetPersistentEventCount(); i++)
        {
            Object target = evt.GetPersistentTarget(i);
            string method = evt.GetPersistentMethodName(i);
            if (target == null) continue;
            if (target is InfoPanelController && method == nameof(InfoPanelController.CloseSettingsPopup))
                c.wiredToClose = true;
            owners.Add($"{target.GetType().Name}.{method}");
        }
        c.ownerNote = string.Join(", ", owners);
        return c;
    }

    private void Wire(Candidate c)
    {
        UnityEventBase evt = c.button != null ? (UnityEventBase)c.button.onClick : c.toggle.onValueChanged;

        // ★AddVoidPersistentListener를 쓴다 — Toggle의 onValueChanged는 UnityEvent<bool>이라
        //   인자 없는 메서드를 그냥 못 붙인다. 이 API가 '인자 없이 호출' 형태로 등록해 준다.
        //   인스펙터에서 손으로 거는 것과 결과가 같다.
        UnityAction action = infoPanel.CloseSettingsPopup;
        UnityEventTools.AddVoidPersistentListener(evt, action);

        EditorUtility.SetDirty(c.button != null ? (Object)c.button : c.toggle);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(c.go.scene);

        Debug.Log($"<color=cyan>[닫기 배선] '{c.go.name}' → InfoPanelController.CloseSettingsPopup()\n" +
                  $"    {c.path}\n" +
                  $"    ★씬 저장은 직접 해라(Ctrl+S). 저장 전엔 반영되지 않는다.</color>");

        message = $"'{c.go.name}'에 배선했다. ★Ctrl+S로 씬을 저장해라.";
        Scan();
    }

    /// <summary>InfoPanelController의 settingsPopup을 SerializedObject로 읽는다(private 필드다).</summary>
    private GameObject FindSettingRoot()
    {
        var so = new SerializedObject(infoPanel);
        SerializedProperty prop = so.FindProperty("settingsPopup");
        return prop != null ? prop.objectReferenceValue as GameObject : null;
    }

    private static string GetPath(GameObject go)
    {
        var parts = new List<string>();
        Transform t = go.transform;
        while (t != null)
        {
            parts.Insert(0, t.name);
            t = t.parent;
        }
        return string.Join("/", parts);
    }
}
