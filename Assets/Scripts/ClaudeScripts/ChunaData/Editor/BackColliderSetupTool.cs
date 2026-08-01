using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 환자 등·허리(흉추) 접촉 콜라이더 생성 도구.
///
/// 복잡추나 흉추/늑골 술기는 실제 시술 부위가 등(흉추)인데, 씬에는 단순추나 다른 술기용
/// 머리·어깨·흉부 콜라이더밖에 없었다. 이 도구가 흉추 본 밑에 **좌·우 2개**의 전용
/// SphereCollider를 만들고 ChunaPathEvaluator의 Patient Back Colliders 배열에 연결한다.
/// 양손 파지(conditionParams=bothHands)는 양손이 각각 닿아야 통과하므로 2개가 필요하다.
///
/// ★비파괴: 기존 오브젝트를 지우거나 옮기지 않는다. 이미 있으면 위치·크기만 갱신하고
///   재실행해도 안전하다(생성은 최초 1회뿐). Undo 가능.
/// </summary>
public class BackColliderSetupTool : EditorWindow
{
    private const string LeftName = "환자 등 충돌체 L";
    private const string RightName = "환자 등 충돌체 R";

    private Transform boneOverride;
    private ChunaPathEvaluator evaluator;

    private float radius = 0.10f;
    [Tooltip("척추 중심에서 좌우로 벌리는 거리")]
    private float lateralOffset = 0.11f;
    private Vector3 centerOffset = Vector3.zero;

    private const string OppositeShoulderName = "보조수 충돌체 반대쪽";

    private Collider sourceShoulder;      // 씬에 원래 있던 어깨 충돌체(복제 기준)
    private Transform oppositeBone;       // 반대쪽 어깨 본
    private bool mirrorShoulderX = true;  // 복제 시 localPosition.x 반전 여부

    [MenuItem("GuideChuna/환자 접촉 충돌체 설정 (등·반대쪽 어깨)")]
    public static void Open()
    {
        var w = GetWindow<BackColliderSetupTool>(true, "환자 접촉 충돌체 설정");
        w.minSize = new Vector2(460f, 560f);
        w.AutoFill();
    }

    private void AutoFill()
    {
        if (evaluator == null)
            evaluator = Object.FindFirstObjectByType<ChunaPathEvaluator>();

        if (boneOverride == null)
            boneOverride = FindSpineBone();

        // 이미 만들어 둔 게 있으면 그 값을 불러온다(재실행 시 덮어쓰기 방지).
        var left = Find(LeftName);
        if (left != null)
        {
            radius = left.radius;
            Vector3 p = left.transform.localPosition;
            lateralOffset = Mathf.Abs(p.x);
            centerOffset = new Vector3(0f, p.y, p.z);
        }

        // 반대쪽 어깨: 기존 어깨 충돌체를 evaluator에서 읽어 복제 기준으로 삼는다.
        if (sourceShoulder == null && evaluator != null)
        {
            var so = new SerializedObject(evaluator);
            var prop = so.FindProperty("patientShoulderCollider");
            if (prop != null) sourceShoulder = prop.objectReferenceValue as Collider;
        }
        if (oppositeBone == null && sourceShoulder != null)
            oppositeBone = FindOppositeBone(sourceShoulder.transform.parent);
    }

    /// <summary>기준 콜라이더가 붙은 본의 좌우 반대쪽 본을 이름으로 찾는다(_R_ ↔ _L_).</summary>
    private static Transform FindOppositeBone(Transform bone)
    {
        if (bone == null) return null;
        string n = bone.name;
        string opposite = null;
        if (n.Contains("_R_")) opposite = n.Replace("_R_", "_L_");
        else if (n.Contains("_L_")) opposite = n.Replace("_L_", "_R_");
        if (opposite == null) return null;

        var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var t in all)
            if (t.name == opposite) return t;
        return null;
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "흉추 본 아래에 등 접촉 콜라이더를 좌·우 2개 만들고 ChunaPathEvaluator에 연결합니다.\n" +
            "양손 파지 판정(bothHands)은 양손이 각각 닿아야 통과하므로 2개가 필요합니다.\n" +
            "기존 머리·어깨·흉부 콜라이더는 건드리지 않습니다.",
            MessageType.Info);

        evaluator = (ChunaPathEvaluator)EditorGUILayout.ObjectField(
            "ChunaPathEvaluator", evaluator, typeof(ChunaPathEvaluator), true);
        boneOverride = (Transform)EditorGUILayout.ObjectField(
            "부착할 본", boneOverride, typeof(Transform), true);

        EditorGUILayout.Space();
        radius = EditorGUILayout.Slider("반지름 (m)", radius, 0.03f, 0.25f);
        lateralOffset = EditorGUILayout.Slider("좌우 간격 (m)", lateralOffset, 0f, 0.3f);
        centerOffset = EditorGUILayout.Vector3Field("중심 오프셋 (Y·Z)", centerOffset);
        centerOffset.x = 0f;

        EditorGUILayout.Space();

        bool exists = Find(LeftName) != null || Find(RightName) != null;
        if (exists)
        {
            EditorGUILayout.HelpBox("이미 존재합니다. [적용]을 누르면 반지름·간격만 갱신됩니다.", MessageType.None);
        }

        using (new EditorGUI.DisabledScope(boneOverride == null))
        {
            if (GUILayout.Button(exists ? "적용 (갱신)" : "생성 후 연결", GUILayout.Height(32f)))
            {
                Apply();
            }
        }

        if (boneOverride == null)
        {
            EditorGUILayout.HelpBox(
                "흉추 본을 찾지 못했습니다. 하이어라키에서 CC_Base_Spine02(또는 등에 해당하는 본)를 직접 넣어주세요.",
                MessageType.Warning);
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("씬에서 선택해 보기"))
        {
            var l = Find(LeftName);
            var r = Find(RightName);
            var sel = new List<Object>();
            if (l != null) sel.Add(l.gameObject);
            if (r != null) sel.Add(r.gameObject);
            if (sel.Count > 0)
            {
                Selection.objects = sel.ToArray();
                SceneView.FrameLastActiveSceneView();
            }
        }

        // ─────────────────────────────────────────────────────────────
        EditorGUILayout.Space(14f);
        EditorGUILayout.LabelField("반대쪽 어깨 충돌체", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "씬의 어깨 충돌체는 한쪽에만 있어서 반대쪽 어깨는 터치해도 반응이 없습니다.\n" +
            "기존 것을 그대로 복제해 반대쪽 본에 붙이고 Patient Shoulder Colliders Extra에 넣습니다.",
            MessageType.Info);

        sourceShoulder = (Collider)EditorGUILayout.ObjectField(
            "기존 어깨 충돌체", sourceShoulder, typeof(Collider), true);
        oppositeBone = (Transform)EditorGUILayout.ObjectField(
            "반대쪽 본", oppositeBone, typeof(Transform), true);
        mirrorShoulderX = EditorGUILayout.Toggle(
            new GUIContent("X 반전", "복제 위치의 좌우를 뒤집는다. 엉뚱한 곳에 생기면 이걸 꺼보세요."),
            mirrorShoulderX);

        var oppExisting = FindOpposite();
        if (oppExisting != null)
            EditorGUILayout.HelpBox("이미 존재합니다. [적용]을 누르면 위치·크기만 갱신됩니다.", MessageType.None);

        using (new EditorGUI.DisabledScope(sourceShoulder == null || oppositeBone == null))
        {
            if (GUILayout.Button(oppExisting != null ? "반대쪽 어깨 적용 (갱신)" : "반대쪽 어깨 생성 후 연결", GUILayout.Height(28f)))
            {
                ApplyOppositeShoulder();
            }
        }

        if (sourceShoulder == null || oppositeBone == null)
        {
            EditorGUILayout.HelpBox(
                "기존 어깨 충돌체(보조수 충돌체)와 반대쪽 본(CC_Base_L_Upperarm 등)을 넣어주세요.",
                MessageType.Warning);
        }
    }

    private SphereCollider FindOpposite()
    {
        if (oppositeBone == null) return null;
        var t = oppositeBone.Find(OppositeShoulderName);
        return t != null ? t.GetComponent<SphereCollider>() : null;
    }

    private void ApplyOppositeShoulder()
    {
        if (sourceShoulder == null || oppositeBone == null) return;

        SphereCollider col = FindOpposite();
        if (col == null)
        {
            var go = new GameObject(OppositeShoulderName);
            Undo.RegisterCreatedObjectUndo(go, "반대쪽 어깨 충돌체 생성");
            Undo.SetTransformParent(go.transform, oppositeBone, "반대쪽 어깨 부모 설정");
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            col = Undo.AddComponent<SphereCollider>(go);
        }

        Undo.RecordObject(col.transform, "반대쪽 어깨 배치");
        Undo.RecordObject(col, "반대쪽 어깨 설정");

        Vector3 srcLocal = sourceShoulder.transform.localPosition;
        if (mirrorShoulderX) srcLocal.x = -srcLocal.x;
        col.transform.localPosition = srcLocal;

        // 기존 어깨가 SphereCollider면 반지름까지 그대로, 아니면 bounds에서 추정.
        var srcSphere = sourceShoulder as SphereCollider;
        col.radius = srcSphere != null
            ? srcSphere.radius
            : Mathf.Max(sourceShoulder.bounds.extents.x, sourceShoulder.bounds.extents.y, sourceShoulder.bounds.extents.z);
        col.isTrigger = sourceShoulder.isTrigger;

        if (evaluator != null)
        {
            var so = new SerializedObject(evaluator);
            var prop = so.FindProperty("patientShoulderCollidersExtra");
            if (prop != null)
            {
                prop.arraySize = 1;
                prop.GetArrayElementAtIndex(0).objectReferenceValue = col;
                so.ApplyModifiedProperties();
                Debug.Log($"<color=green>[BackColliderSetup] 반대쪽 어깨 충돌체 생성·연결 완료 " +
                          $"(부모 {oppositeBone.name}, r={col.radius:F3})</color>");
            }
            else
            {
                Debug.LogWarning("[BackColliderSetup] patientShoulderCollidersExtra 필드를 찾지 못했습니다. 컴파일이 끝났는지 확인하세요.");
            }
        }

        Selection.activeGameObject = col.gameObject;
        EditorUtility.SetDirty(col.gameObject);
    }

    private void Apply()
    {
        if (boneOverride == null) return;

        var left = CreateOrUpdate(LeftName, new Vector3(-lateralOffset, centerOffset.y, centerOffset.z));
        var right = CreateOrUpdate(RightName, new Vector3(+lateralOffset, centerOffset.y, centerOffset.z));

        // ChunaPathEvaluator.patientBackColliders는 private [SerializeField]라 SerializedObject로 넣는다.
        if (evaluator != null)
        {
            var so = new SerializedObject(evaluator);
            var prop = so.FindProperty("patientBackColliders");
            if (prop != null)
            {
                prop.arraySize = 2;
                prop.GetArrayElementAtIndex(0).objectReferenceValue = left;
                prop.GetArrayElementAtIndex(1).objectReferenceValue = right;
                so.ApplyModifiedProperties();
                Debug.Log($"<color=green>[BackColliderSetup] 등 충돌체 좌·우 생성·연결 완료 " +
                          $"(r={radius:F3}, 간격 ±{lateralOffset:F3})</color>");
            }
            else
            {
                Debug.LogWarning("[BackColliderSetup] patientBackColliders 필드를 찾지 못했습니다. 컴파일이 끝났는지 확인하세요.");
            }
        }
        else
        {
            Debug.LogWarning("[BackColliderSetup] ChunaPathEvaluator를 찾지 못해 연결하지 못했습니다. 인스펙터에서 직접 넣어주세요.");
        }

        Selection.objects = new Object[] { left.gameObject, right.gameObject };
    }

    private SphereCollider CreateOrUpdate(string name, Vector3 localPos)
    {
        SphereCollider col = Find(name);
        if (col == null)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "등 충돌체 생성");
            Undo.SetTransformParent(go.transform, boneOverride, "등 충돌체 부모 설정");
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            col = Undo.AddComponent<SphereCollider>(go);
        }

        Undo.RecordObject(col.transform, "등 충돌체 배치");
        Undo.RecordObject(col, "등 충돌체 설정");
        col.transform.localPosition = localPos;
        col.radius = radius;
        col.isTrigger = false;
        EditorUtility.SetDirty(col.gameObject);
        return col;
    }

    private SphereCollider Find(string name)
    {
        if (boneOverride == null) return null;
        var t = boneOverride.Find(name);
        return t != null ? t.GetComponent<SphereCollider>() : null;
    }

    /// <summary>씬에서 흉추 본을 이름으로 찾는다(비활성 포함).</summary>
    private static Transform FindSpineBone()
    {
        var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Transform fallback = null;
        foreach (var t in all)
        {
            if (t.name == "CC_Base_Spine02") return t;
            if (fallback == null && t.name == "CC_Base_Spine01") fallback = t;
        }
        return fallback;
    }
}
