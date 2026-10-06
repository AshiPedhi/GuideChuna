// ★[선 실측 — 끝점 맞추기] 2026-10-06 신설
// 찍는 점은 «컨트롤러 원점 + tipOffset»이다. 이 값을 눈으로 보면서 맞추는 도구다.
//   · Play를 하지 않을 때: «컨트롤러 모형 보기»를 켜면 씬 창에 컨트롤러 모형과 노란 끝점이 뜬다.
//     끝점의 화살표를 끌거나 mm 버튼으로 민다. 모형은 미리보기일 뿐 씬에 저장되지 않고, Play·빌드에는 나오지 않는다.
//   · Play 중: 같은 버튼이 씬에 꺼 둔 실제 컨트롤러 모형을 켠다(헤드셋에서도 보인다). tipOffset은 매 프레임 읽으므로 바로 움직인다.
//     Play 중에 고친 값은 Play를 멈출 때 씬의 값에 옮겨 적는다(저장은 사람이 Ctrl+S).

using System.Collections.Generic;
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
        var s = (RomLineSession)target;

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("끝점 맞추기 (mm)", EditorStyles.boldLabel);

        // ── 컨트롤러 모형 ──
        EditorGUILayout.BeginHorizontal();
        if (Application.isPlaying)
        {
            bool shown = s.ControllerModelShown;
            bool want = GUILayout.Toggle(shown, shown ? "컨트롤러 모형 보는 중 (끄기)" : "컨트롤러 모형 보기", "Button", GUILayout.Height(24f));
            if (want != shown) s.ShowControllerModel(want);
            if (GUILayout.Button("씬 창에서 보기", GUILayout.Height(24f), GUILayout.Width(110f)) && s.TipValid && SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.LookAt((s.TipWorld + s.ControllerWorld) * 0.5f, SceneView.lastActiveSceneView.rotation, 0.12f);
        }
        else
        {
            bool shown = RomLineTipPreview.Shown;
            bool want = GUILayout.Toggle(shown, shown ? "컨트롤러 모형 보는 중 (끄기)" : "컨트롤러 모형 보기", "Button", GUILayout.Height(24f));
            if (want != shown) { if (want) RomLineTipPreview.Show(); else RomLineTipPreview.Hide(); }
            EditorGUI.BeginDisabledGroup(!shown);
            if (GUILayout.Button("씬 창에서 보기", GUILayout.Height(24f), GUILayout.Width(110f))) RomLineTipPreview.Frame();
            EditorGUI.EndDisabledGroup();
        }
        EditorGUILayout.EndHorizontal();

        if (!Application.isPlaying)
        {
            string[] models = RomLineTipPreview.ModelNames();
            if (models.Length > 0)
            {
                int cur = Mathf.Max(0, System.Array.IndexOf(models, RomLineTipPreview.ModelName));
                int pick = EditorGUILayout.Popup("모형", cur, models);
                if (pick != cur)
                {
                    RomLineTipPreview.ModelName = models[pick];
                    if (RomLineTipPreview.Shown) RomLineTipPreview.Show();
                }
            }
        }

        // ── 값 ──
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
            SceneView.RepaintAll();
        }

        string live = !Application.isPlaying ? "모형을 켜면 씬 창에서 노란 끝점의 화살표를 끌어 맞출 수도 있다. 모형은 미리보기라 저장되지 않고 Play·빌드에는 나오지 않는다."
                    : s.TipValid ? $"지금 쓰는 컨트롤러: {(s.UsingLeft ? "왼쪽(좌우 값은 뒤집어 적용)" : "오른쪽")} · 끝점 {s.TipWorld:F3}\nPlay 중에 고친 값은 Play를 멈출 때 씬에 옮겨 적는다 — 그 뒤 Ctrl+S로 저장한다."
                    : "컨트롤러가 추적되지 않는다.";
        EditorGUILayout.HelpBox("기준은 오른손 컨트롤러다(쥐고 앞을 가리킬 때의 오른쪽 · 위 · 앞). 왼손은 좌우만 뒤집어 쓴다.\n" + live, MessageType.Info);
        if (Application.isPlaying) Repaint();
    }
}

/// <summary>
/// Play를 하지 않을 때 씬 창에 띄우는 컨트롤러 모형 미리보기. 씬의 (꺼 둔) 컨트롤러 모형을 복제해 숨김·저장 안 함으로 세운다.
/// ★컨트롤러 자세 = OVRControllerHelper가 붙은 오브젝트의 자세다(끝점 0.033도 이 틀에서 쟀다 — 10-01 romline-ctrl).
///   그래서 복제한 모형은 그 오브젝트 아래에서의 로컬 자세를 그대로 갖고 미리보기 뿌리 아래에 놓는다.
/// </summary>
[InitializeOnLoad]
public static class RomLineTipPreview
{
    private const string RootName = "(선 실측 끝점 맞추기 미리보기)";
    private const string ModelKey = "RomLineTipPreview.model";
    private const string DefaultModel = "MetaQuestTouchPlus_Right";
    private const float TipRadius = 0.004f;   // 앱의 노란 끝점과 같은 크기
    private static readonly Vector3 PreviewPosition = new Vector3(1.5f, 1.3f, 1.5f);

    private static GameObject root;
    private static RomLineSession session;

    public static bool Shown => root != null;

    public static string ModelName
    {
        get => EditorPrefs.GetString(ModelKey, DefaultModel);
        set => EditorPrefs.SetString(ModelKey, value);
    }

    static RomLineTipPreview()
    {
        AssemblyReloadEvents.beforeAssemblyReload += Hide;
        EditorApplication.playModeStateChanged += c => { if (c == PlayModeStateChange.ExitingEditMode) Hide(); };
        EditorSceneManager.sceneClosing += (sc, removing) => Hide();
        EditorApplication.delayCall += Sweep;   // 지난번에 못 치운 것이 있으면 치운다
    }

    private static void Sweep()
    {
        foreach (GameObject g in Resources.FindObjectsOfTypeAll<GameObject>())
            if (g != null && g != root && g.name == RootName) Object.DestroyImmediate(g);
    }

    /// <summary>오른손 쪽 컨트롤러 모형 묶음(OVRControllerHelper).</summary>
    private static OVRControllerHelper Helper()
    {
        OVRControllerHelper any = null;
        foreach (OVRControllerHelper h in Object.FindObjectsByType<OVRControllerHelper>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            any = any != null ? any : h;
            for (Transform a = h.transform; a != null; a = a.parent)
                if (a.name.StartsWith("Right")) return h;
        }
        return any;
    }

    public static string[] ModelNames()
    {
        OVRControllerHelper h = Helper();
        if (h == null) return new string[0];
        var list = new List<string>();
        foreach (Transform c in h.transform)
            if (c.GetComponentInChildren<Renderer>(true) != null) list.Add(c.name);
        return list.ToArray();
    }

    public static string Show()
    {
        if (Application.isPlaying) return "Play 중에는 인스펙터의 «컨트롤러 모형 보기»가 실제 모형을 켠다.";
        Hide();
        session = Object.FindFirstObjectByType<RomLineSession>();
        if (session == null) return "★열린 씬에 RomLineSession이 없다.";
        OVRControllerHelper h = Helper();
        if (h == null) return "★씬에 컨트롤러 모형(OVRControllerHelper)이 없다.";
        Transform model = h.transform.Find(ModelName);
        if (model == null) model = h.transform.Find(DefaultModel);
        if (model == null) return $"★모형 «{ModelName}»을 못 찾았다.";

        root = new GameObject(RootName);
        // 리그의 손·컨트롤러 모형(원점 근처)과 겹치지 않는 빈 자리에 세운다. 자세는 월드와 나란히 — 좌우·위아래·앞뒤가 월드 축과 같아 화살표를 읽기 쉽다.
        root.transform.position = PreviewPosition;
        GameObject clone = Object.Instantiate(model.gameObject, root.transform);   // 로컬 자세 그대로
        clone.name = model.name;
        clone.SetActive(true);
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.HideAndDontSave;
        foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;

        SceneView.duringSceneGui -= OnScene;
        SceneView.duringSceneGui += OnScene;
        Frame();

        Bounds b = default; bool has = false;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
        {
            if (has) b.Encapsulate(r.bounds); else { b = r.bounds; has = true; }
        }
        Vector3 lo = root.transform.InverseTransformPoint(b.min) * 1000f, hi = root.transform.InverseTransformPoint(b.max) * 1000f;
        return $"미리보기 켬 — 모형 «{model.name}» · 그려지는 범위(컨트롤러 틀, mm) 좌우 {lo.x:F0}~{hi.x:F0} · 위아래 {lo.y:F0}~{hi.y:F0} · 앞뒤 {lo.z:F0}~{hi.z:F0}" +
               (has ? "" : " ★렌더러가 없다") + $" · 끝점 {Offset() * 1000f:F1}";
    }

    public static void Hide()
    {
        SceneView.duringSceneGui -= OnScene;
        if (root != null) Object.DestroyImmediate(root);
        root = null;
        SceneView.RepaintAll();
    }

    /// <summary>씬 창을 컨트롤러 오른쪽 앞 위에서 끝점을 내려다보는 자리로 옮긴다.</summary>
    public static void Frame()
    {
        SceneView sv = SceneView.lastActiveSceneView;
        if (sv == null || root == null) return;
        sv.LookAt(root.transform.position + new Vector3(0f, -0.02f, -0.03f), Quaternion.LookRotation(new Vector3(-1f, -0.4f, -0.55f)), 0.11f, false, true);
        sv.Repaint();
    }

    private static Vector3 Offset()
    {
        return session != null ? new SerializedObject(session).FindProperty("tipOffset").vector3Value : Vector3.zero;
    }

    private static void OnScene(SceneView sv)
    {
        if (root == null || session == null) { Hide(); return; }
        var so = new SerializedObject(session);
        SerializedProperty prop = so.FindProperty("tipOffset");
        Transform t = root.transform;
        Vector3 tip = t.TransformPoint(prop.vector3Value);

        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
        Handles.color = new Color(1f, 0.95f, 0.2f, 1f);
        if (Event.current.type == EventType.Repaint)
            Handles.SphereHandleCap(0, tip, Quaternion.identity, TipRadius * 2f, EventType.Repaint);
        Handles.DrawLine(t.position, tip);
        Handles.Label(tip + Vector3.up * 0.012f, $"끝점  좌우 {prop.vector3Value.x * 1000f:F1} · 위아래 {prop.vector3Value.y * 1000f:F1} · 앞뒤 {prop.vector3Value.z * 1000f:F1} mm");

        EditorGUI.BeginChangeCheck();
        Vector3 moved = Handles.PositionHandle(tip, t.rotation);
        if (EditorGUI.EndChangeCheck())
        {
            Vector3 v = t.InverseTransformPoint(moved);
            v = new Vector3(Mathf.Round(v.x * 10000f), Mathf.Round(v.y * 10000f), Mathf.Round(v.z * 10000f)) / 10000f;   // 0.1mm 자리
            prop.vector3Value = v;
            so.ApplyModifiedProperties();   // Undo에 걸리고 씬이 dirty가 된다
        }
    }

    /// <summary>브리지: romline-tip --show=1|0 [--model=이름]</summary>
    public static string Bridge(Dictionary<string, string> a)
    {
        if (a != null && a.TryGetValue("model", out string m) && m.Length > 0) ModelName = m;
        bool show = a == null || !a.TryGetValue("show", out string s) || s != "0";
        if (!show) { Hide(); return "미리보기 끔"; }
        return Show() + "\n모형 목록: " + string.Join(", ", ModelNames());
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
