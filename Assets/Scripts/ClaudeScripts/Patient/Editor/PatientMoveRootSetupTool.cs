using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 환자 이동용 홀더(빈 부모) 생성 도구.
///
/// 왜 필요한가 — 환자 애니 클립들이 <b>환자 루트(c9)의 localPosition·localRotation을 직접 애니메이션</b>한다
/// (path="" 오브젝트 커브). 그래서 프리셋으로 c9를 옮겨도 재생 순간 클립이 기록한 자리로 끌려간다.
/// 부모 ChunaObject에는 침대·골격모델·Canvas가 같이 들어 있어 그걸 옮길 수도 없다.
/// → 환자(및 복제본)만 담는 빈 부모를 하나 끼워 넣고, 프리셋은 그 부모를 옮긴다.
///   클립은 부모를 절대 건드리지 않으므로 충돌이 사라진다.
///
/// ★비파괴: 오브젝트를 지우지 않고 부모만 바꾼다(월드 위치 유지). Undo 가능.
/// </summary>
public class PatientMoveRootSetupTool : EditorWindow
{
    private const string HolderName = "환자 이동 홀더";

    private PatientPositionManager manager;
    private Transform patientRoot;
    private readonly List<Transform> extras = new List<Transform>();
    private Vector2 scroll;
    private string status = "";

    [MenuItem("GuideChuna/환자·리그/환자 이동 홀더 만들기 (애니 위치 충돌 해결)")]
    public static void Open()
    {
        var w = GetWindow<PatientMoveRootSetupTool>(true, "환자 이동 홀더");
        w.minSize = new Vector2(480f, 420f);
        w.AutoFill();
    }

    private void AutoFill()
    {
        if (manager == null) manager = Object.FindFirstObjectByType<PatientPositionManager>();
        if (patientRoot == null && manager != null) patientRoot = manager.PatientRoot;

        // 같은 부모 아래의 환자 복제본(c9 (2) 등)도 함께 옮길 후보로 잡는다.
        extras.Clear();
        if (patientRoot != null && patientRoot.parent != null)
        {
            string baseName = patientRoot.name;
            foreach (Transform sib in patientRoot.parent)
            {
                if (sib == patientRoot) continue;
                if (sib.name.StartsWith(baseName)) extras.Add(sib);
            }
        }
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.HelpBox(
            "환자만 담는 빈 부모를 만들어 그 아래로 옮깁니다.\n" +
            "프리셋은 이 부모를 옮기므로, 애니 클립이 c9의 로컬 위치를 써도 서로 부딪히지 않습니다.\n" +
            "침대·골격모델·Canvas는 건드리지 않습니다.",
            MessageType.Info);

        manager = (PatientPositionManager)EditorGUILayout.ObjectField(
            "PatientPositionManager", manager, typeof(PatientPositionManager), true);
        patientRoot = (Transform)EditorGUILayout.ObjectField("환자 루트 (c9)", patientRoot, typeof(Transform), true);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("함께 옮길 오브젝트 (복제본 등)", EditorStyles.boldLabel);
        if (extras.Count == 0)
        {
            EditorGUILayout.LabelField("  없음", EditorStyles.miniLabel);
        }
        else
        {
            for (int i = 0; i < extras.Count; i++)
                extras[i] = (Transform)EditorGUILayout.ObjectField($"  추가 {i + 1}", extras[i], typeof(Transform), true);
        }
        if (GUILayout.Button("형제 중 환자 복제본 다시 찾기")) AutoFill();

        EditorGUILayout.Space();
        Transform existing = FindHolder();
        if (existing != null)
            EditorGUILayout.HelpBox($"'{HolderName}'이 이미 있습니다. 다시 실행하면 배선만 갱신합니다.", MessageType.None);

        using (new EditorGUI.DisabledScope(patientRoot == null))
        {
            if (GUILayout.Button(existing != null ? "배선 갱신" : "홀더 생성 + 환자 이동 + 배선", GUILayout.Height(32f)))
                Apply();
        }

        if (!string.IsNullOrEmpty(status))
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(status, MessageType.None);
        }

        EditorGUILayout.EndScrollView();
    }

    private Transform FindHolder()
    {
        if (patientRoot == null) return null;
        // 이미 홀더 밑으로 옮겨진 경우
        if (patientRoot.parent != null && patientRoot.parent.name == HolderName) return patientRoot.parent;
        // 만들어만 두고 아직 안 옮긴 경우
        if (patientRoot.parent != null)
        {
            var t = patientRoot.parent.Find(HolderName);
            if (t != null) return t;
        }
        return null;
    }

    private void Apply()
    {
        if (patientRoot == null) return;

        Transform holder = FindHolder();
        if (holder == null)
        {
            var go = new GameObject(HolderName);
            Undo.RegisterCreatedObjectUndo(go, "환자 이동 홀더 생성");
            // 부모의 좌표계를 그대로 물려받는다(스케일·회전 왜곡 방지).
            Undo.SetTransformParent(go.transform, patientRoot.parent, "홀더 부모 설정");
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            holder = go.transform;
        }

        int moved = 0;
        if (patientRoot.parent != holder)
        {
            Undo.SetTransformParent(patientRoot, holder, "환자를 홀더로 이동");   // 월드 위치 유지
            moved++;
        }
        foreach (var ex in extras)
        {
            if (ex == null || ex == holder || ex.parent == holder) continue;
            Undo.SetTransformParent(ex, holder, "환자 복제본을 홀더로 이동");
            moved++;
        }

        // PatientPositionManager.patientMoveRoot 배선(private [SerializeField]).
        string wired = "PatientPositionManager를 찾지 못해 배선하지 못했습니다 — 인스펙터에서 직접 넣어주세요.";
        if (manager != null)
        {
            var so = new SerializedObject(manager);
            var prop = so.FindProperty("patientMoveRoot");
            if (prop != null)
            {
                prop.objectReferenceValue = holder;
                so.ApplyModifiedProperties();
                wired = "patientMoveRoot 배선 완료.";
            }
            else
            {
                wired = "patientMoveRoot 필드를 찾지 못했습니다. 컴파일이 끝났는지 확인하세요.";
            }
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(patientRoot.gameObject.scene);
        Selection.activeGameObject = holder.gameObject;

        status = $"'{HolderName}' 준비 완료 (옮긴 오브젝트 {moved}개).\n{wired}\n\n" +
                 "※프리셋은 그대로 씁니다 — c9가 프리셋 좌표에 오도록 홀더 위치를 역산합니다.\n" +
                 "되돌리려면 Ctrl+Z.";
    }
}
