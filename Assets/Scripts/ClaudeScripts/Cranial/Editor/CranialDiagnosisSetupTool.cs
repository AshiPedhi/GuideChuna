using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 두개골 진단 파지점 생성·배선 도구.
/// 메뉴: GuideChuna/두개골 진단 파지점 설정
///
/// 하는 일 = 시나리오별 진단 단계에 필요한 파지점 GameObject를 만들고
/// CranialAdjustmentController.diagnosisStages에 자동 연결한다.
/// 만들어진 파지점의 **위치는 씬 뷰에서 직접 끌어다 맞춰야 한다**(해부학적 위치는 자동으로 알 수 없음).
/// </summary>
public class CranialDiagnosisSetupTool : EditorWindow
{
    private const string GroupName = "진단 파지점";

    /// <summary>시나리오 유형별 프리셋.</summary>
    private enum Preset
    {
        OM,      // 진단1 = 양손 측두부 감싸기(손바닥) 3초 / 진단2 = 양손 후두부 베개(손바닥) 8초
        PMPJ,    // 진단1 = 자세 2개(ⓐ왼손 손바닥+오른손 3점 / ⓑ왼손 3점+오른손 손바닥) 각 3초
        Rib,     // 늑골(제1·제2) 진단1 = 양손 엄지로 좌우를 눌러 높이 비교, 자세 1개 3초
    }

    private CranialAdjustmentController rig;
    private Preset preset = Preset.OM;
    private Vector2 scroll;
    private string status = "";
    private string newRigScenarioName = "두개골PJ교정";

    [MenuItem("GuideChuna/두개골 진단 파지점 설정")]
    public static void Open()
    {
        var w = GetWindow<CranialDiagnosisSetupTool>(true, "두개골 진단 파지점 설정");
        w.minSize = new Vector2(460, 420);
        w.AutoDetect();
    }

    private void AutoDetect()
    {
        var rigs = FindRigs();
        if (rigs.Count > 0 && rig == null) rig = rigs[0];
    }

    private static List<CranialAdjustmentController> FindRigs()
    {
        var found = Object.FindObjectsByType<CranialAdjustmentController>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        return new List<CranialAdjustmentController>(found);
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.HelpBox(
            "진단 단계에 필요한 파지점을 만들고 컨트롤러에 자동 배선합니다.\n" +
            "★ 생성 후 씬 뷰에서 각 파지점을 실제 위치(측두부·후두부)로 끌어다 놓으세요.",
            MessageType.Info);

        EditorGUILayout.Space();

        var rigs = FindRigs();
        if (rigs.Count == 0)
        {
            EditorGUILayout.HelpBox("씬에 CranialAdjustmentController가 없습니다.", MessageType.Error);
            EditorGUILayout.EndScrollView();
            return;
        }

        EditorGUILayout.LabelField("대상 리그", EditorStyles.boldLabel);
        foreach (var r in rigs)
        {
            string nm = string.IsNullOrEmpty(r.ScenarioName) ? "(이름 없음 = 레거시 기본)" : r.ScenarioName;
            bool sel = rig == r;
            if (GUILayout.Toggle(sel, $"{nm}   —   {r.gameObject.name}", EditorStyles.radioButton) && !sel)
                rig = r;
        }

        EditorGUILayout.Space();
        preset = (Preset)EditorGUILayout.EnumPopup("프리셋", preset);
        EditorGUILayout.LabelField(PresetSummary(preset), EditorStyles.wordWrappedMiniLabel);

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(rig == null))
        {
            if (GUILayout.Button("① 파지점 생성 + 컨트롤러 배선", GUILayout.Height(30)))
                Build();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("굴곡·신전 호흡 애니메이션", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "선택한 리그에 CranialBreathAnimator를 붙이고 참조를 자동 연결합니다.\n" +
            "호흡 구간에서는 호흡 위상에, 진단 구간에서는 자체 루프로 굴곡·신전 클립을 재생합니다.",
            EditorStyles.wordWrappedMiniLabel);
        using (new EditorGUI.DisabledScope(rig == null))
        {
            if (GUILayout.Button("굴곡·신전 호흡 애니메이터 추가 / 설정"))
                SetupBreathAnimator();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("기존 교정 파지점 정리", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "교정 파지(cranialGrip)를 파이브핑거홀드 → 3점 파지(엄지·검지·새끼)로 바꿉니다.\n" +
            "중지·약지 파지점을 배열에서 빼고 오브젝트도 지웁니다(Ctrl+Z로 되돌릴 수 있음).",
            EditorStyles.wordWrappedMiniLabel);
        using (new EditorGUI.DisabledScope(rig == null))
        {
            if (GUILayout.Button("교정 파지점을 3점(엄지·검지·새끼)으로 정리"))
                MakeThreePointGrip();
            if (GUILayout.Button("PM 교정 파지 구성 (왼손 엄지·중지 / 오른손 엄지·검지)"))
                SetupPmCorrectionGrips();
            if (GUILayout.Button("늑골·흉추 교정 파지 구성 (좌우 손바닥 1점씩)"))
                SetupRibCorrectionGrips();
            if (GUILayout.Button("레거시 diagnosisRightGrips 배열 비우기"))
                ClearLegacyDiagnosisArray();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("새 시나리오용 리그 만들기", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "PJ처럼 리그가 아직 없는 시나리오용. 위에서 고른 리그를 통째로 복제하고 시나리오 이름만 바꿉니다.\n" +
            "복제 후 그 리그를 선택해 위 ①을 실행하세요.",
            EditorStyles.wordWrappedMiniLabel);
        newRigScenarioName = EditorGUILayout.TextField("새 시나리오 이름", newRigScenarioName);
        using (new EditorGUI.DisabledScope(rig == null || string.IsNullOrWhiteSpace(newRigScenarioName)))
        {
            if (GUILayout.Button("선택한 리그 복제"))
                DuplicateRig();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("정리", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(rig == null))
        {
            if (GUILayout.Button($"생성된 '{GroupName}' 그룹 삭제 (다시 만들기 전에)"))
                DeleteGroup();
        }

        if (!string.IsNullOrEmpty(status))
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(status, MessageType.None);
        }

        EditorGUILayout.EndScrollView();
    }

    private static string PresetSummary(Preset p)
    {
        switch (p)
        {
            case Preset.OM:
                return "OM(두개골교정)\n" +
                       "  · 진단1 — 양손 측두부 감싸기 (왼손 손바닥 + 오른손 손바닥), 3초 유지\n" +
                       "  · 진단2 — 양손 후두부 모아 베개 (왼손 손바닥 + 오른손 손바닥), 8초 유지\n" +
                       "  → 파지점 4개";
            case Preset.PMPJ:
                return "PM·PJ(두개골PM교정 / 두개골PJ교정)\n" +
                       "  · 진단1 — 자세 2개, 각 3초 유지 (순서 무관), 호흡 유도 메시지 ON\n" +
                       "      ⓐ 왼손 후두(손바닥) + 오른손 측두(엄지·검지·새끼)\n" +
                       "      ⓑ 왼손 측두(엄지·검지·새끼) + 오른손 후두(손바닥)\n" +
                       "  → 파지점 8개 (자세당 4개)\n" +
                       "  ※ CSV 지시문이 '한 손은 후두를 손바닥으로 받치고, 다른 손은 관골궁과 유양돌기를 감싸 파지'라서\n" +
                       "     받치는 손만 손바닥이고 측두 쪽은 손가락 파지다.";
            case Preset.Rib:
                return "늑골(제1늑골_앙와위 / 제2늑골_상방변위)\n" +
                       "  · 진단1 — 양손 엄지로 좌우 높이 비교, 자세 1개 3초 유지\n" +
                       "  → 파지점 2개\n" +
                       "  ※ 교정 파지는 아래 '늑골 교정 파지 구성' 버튼으로 따로 만든다\n" +
                       "     (제1늑골 = 왼손 웹 + 오른손 머리 측면 / 제2늑골 = 두상골 접촉 + 반대손 팔 파지).";
        }
        return "";
    }

    // ================= 생성 =================

    private void Build()
    {
        if (rig == null) return;

        Transform existing = rig.transform.Find(GroupName);
        if (existing != null)
        {
            if (!EditorUtility.DisplayDialog("이미 있습니다",
                    $"'{GroupName}' 그룹이 이미 있습니다. 지우고 다시 만들까요?\n(기존 위치 조정이 사라집니다)",
                    "다시 만들기", "취소"))
                return;
            Undo.DestroyObjectImmediate(existing.gameObject);
        }

        GripPointTarget template = FindTemplate(rig);

        // 기존 교정 파지점의 실제 위치를 초기값으로 재활용(임의 좌표보다 훨씬 가깝게 시작)
        var leftRef = CollectRefPositions(rig, "leftGrips");
        var rightRef = CollectRefPositions(rig, "rightGrips");

        var group = new GameObject(GroupName);
        Undo.RegisterCreatedObjectUndo(group, "진단 파지점 생성");
        group.transform.SetParent(rig.transform, false);

        var stages = new List<StageBuild>();

        if (preset == Preset.OM)
        {
            var s1 = new StageBuild("진단1", 3f, false);
            s1.poses.Add(new PoseBuild("양손 측두부 감싸기",
                left: new[] { CranialFinger.Palm },
                right: new[] { CranialFinger.Palm }));
            stages.Add(s1);

            var s2 = new StageBuild("진단2", 8f, false);
            s2.poses.Add(new PoseBuild("양손 후두부 모아 베개",
                left: new[] { CranialFinger.Palm },
                right: new[] { CranialFinger.Palm }));
            stages.Add(s2);
        }
        else if (preset == Preset.Rib)
        {
            // 늑골 술기의 진단은 '좌우를 눌러 높이를 비교'하는 한 동작뿐이다(자세 2개로 나눌 게 없다).
            //   제1늑골 — 승모근을 엄지로 제끼고 양쪽 제1늑골을 두방→족방으로 눌러 비교
            //   제2늑골 — 쇄골 바깥 델토펙토랄 부위를 좌우로 밀어보며 비교
            // 접촉점이 표면에 있어 손바닥이 아니라 엄지로 판정한다.
            var s1 = new StageBuild("진단1", 3f, false);
            s1.poses.Add(new PoseBuild("양손 엄지로 좌우 높이 비교",
                left: new[] { CranialFinger.Thumb },
                right: new[] { CranialFinger.Thumb }));
            stages.Add(s1);
        }
        else
        {
            // ★PM·PJ 진단 = 한 손은 후두를 '손바닥으로 받치고', 다른 손은 측두골의
            //   '관골궁과 유양돌기를 감싸 파지'(=손가락 파지)한다. CSV 지시문 원문이 그렇다:
            //     PM "한 손은 환자의 후두부를 손바닥으로 받치고, 다른 손은 측두골의 관골궁과 유양돌기를 감싸 파지"
            //     PJ "왼손으로 후두골을, 오른손으로 관골궁과 유양돌기를 잡고"
            //   → 손바닥만으로 판정하면 지시문의 '측두 파지'를 검사하지 않는다. 좌우를 바꿔 잡아 양쪽 확인(순서 무관).
            //   손가락 구성은 교정 파지와 동일하게 엄지·검지·새끼.
            var fingers = new[] { CranialFinger.Thumb, CranialFinger.Index, CranialFinger.Pinky };
            var s1 = new StageBuild("진단1", 3f, true);
            s1.poses.Add(new PoseBuild("ⓐ 왼손 후두(손바닥) + 오른손 측두(엄지·검지·새끼)",
                left: new[] { CranialFinger.Palm },
                right: fingers));
            s1.poses.Add(new PoseBuild("ⓑ 왼손 측두(엄지·검지·새끼) + 오른손 후두(손바닥)",
                left: fingers,
                right: new[] { CranialFinger.Palm }));
            stages.Add(s1);
        }

        int created = 0;
        foreach (var st in stages)
        {
            var stageGo = new GameObject(st.stageId);
            Undo.RegisterCreatedObjectUndo(stageGo, "진단 파지점 생성");
            stageGo.transform.SetParent(group.transform, false);

            foreach (var pose in st.poses)
            {
                Transform poseParent = stageGo.transform;
                if (st.poses.Count > 1)
                {
                    var poseGo = new GameObject(pose.label);
                    Undo.RegisterCreatedObjectUndo(poseGo, "진단 파지점 생성");
                    poseGo.transform.SetParent(stageGo.transform, false);
                    poseParent = poseGo.transform;
                }

                pose.leftCreated = CreateSide(poseParent, template, pose.left, true, ref created, rig, leftRef, rightRef);
                pose.rightCreated = CreateSide(poseParent, template, pose.right, false, ref created, rig, leftRef, rightRef);
            }
        }

        WireStages(rig, stages);

        // ★늑골에는 호흡 유도 문구가 없다. 두개골 리그를 복제해 만들면 cueOnAllDiagnosisStages(기본 ON)가
        //   딸려와 진단 단계마다 "호흡에 맞춰 두개골의 움직임을 느껴보세요" 문구가 뜬다 → 여기서 끈다.
        if (preset == Preset.Rib)
        {
            var so = new SerializedObject(rig);
            var cue = so.FindProperty("cueOnAllDiagnosisStages");
            if (cue != null)
            {
                cue.boolValue = false;
                so.ApplyModifiedProperties();
            }
        }

        // ※굴곡·신전 애니메이션은 CSV의 patientAnimationClip으로 재생된다(컴포넌트 불필요).
        //   호흡 위상에 정확히 물려야 할 때만 아래 'CranialBreathAnimator' 버튼을 따로 쓴다.

        EditorUtility.SetDirty(rig);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);
        Selection.activeGameObject = group;

        status = $"파지점 {created}개 생성 + diagnosisStages {stages.Count}단계 배선 완료.\n\n" +
                 "다음 할 일:\n" +
                 "  1) 씬 뷰에서 각 파지점을 실제 위치(측두부·후두부)로 이동\n" +
                 "  2) Play로 접촉 판정·유지 타이머 확인\n" +
                 (template == null
                     ? "  ※ 기존 파지점 템플릿을 못 찾아 기본 구체로 만들었습니다(반경·색은 인스펙터에서 조정)."
                     : $"  ※ '{template.name}'을 템플릿으로 복제해 시각·콜라이더 설정을 물려받았습니다.");
    }

    /// <summary>기존 교정 파지점의 로컬 위치를 손가락별로 수집한다(리그 기준).
    /// 이미 해부학적으로 맞춰 놓은 자리라, 새 진단 파지점의 초기 위치로 재활용하면 배치 수고가 크게 준다.</summary>
    private static Dictionary<CranialFinger, Vector3> CollectRefPositions(
        CranialAdjustmentController rig, string arrayName)
    {
        var map = new Dictionary<CranialFinger, Vector3>();
        var so = new SerializedObject(rig);
        var arr = so.FindProperty(arrayName);
        if (arr == null || !arr.isArray) return map;

        for (int i = 0; i < arr.arraySize; i++)
        {
            var g = arr.GetArrayElementAtIndex(i).objectReferenceValue as GripPointTarget;
            if (g == null) continue;
            // 리그 로컬 좌표로 환산(파지점이 리그 직속이 아닐 수도 있으므로)
            Vector3 local = rig.transform.InverseTransformPoint(g.transform.position);
            if (!map.ContainsKey(g.Finger)) map[g.Finger] = local;
        }
        return map;
    }

    /// <summary>해당 손·손가락의 초기 위치를 정한다.
    /// ① 같은 손의 기존 파지점 → ② 반대 손의 것을 X 미러 → ③ 폴백(좌우로 갈라 배치).</summary>
    private static Vector3 SeedPosition(
        CranialFinger f, bool leftSide, int index,
        Dictionary<CranialFinger, Vector3> sameSide, Dictionary<CranialFinger, Vector3> otherSide)
    {
        if (sameSide != null && sameSide.TryGetValue(f, out var p)) return p;
        if (otherSide != null && otherSide.TryGetValue(f, out var q)) return new Vector3(-q.x, q.y, q.z);

        // 같은 손의 다른 손가락이라도 있으면 그 근처에 둔다(측두 3점처럼 몰려 있는 세트용).
        if (sameSide != null)
            foreach (var kv in sameSide)
                if (kv.Key != CranialFinger.Palm)
                    return kv.Value + new Vector3(0f, 0f, index * 0.018f);

        // 반대 손의 손가락 자리를 미러해서라도 대략의 해부 위치를 잡는다
        // (예: 왼손 3점을 만드는데 왼쪽엔 손바닥 파지점밖에 없는 PM 리그).
        if (otherSide != null)
            foreach (var kv in otherSide)
                if (kv.Key != CranialFinger.Palm)
                    return new Vector3(-kv.Value.x, kv.Value.y, kv.Value.z + index * 0.018f);

        float xSign = leftSide ? 1f : -1f;
        return new Vector3(xSign * 0.07f, 0.05f, -0.02f + index * 0.025f);
    }

    /// <summary>한 손 분량의 파지점 생성. 초기 위치는 기존 파지점에서 유도한다.</summary>
    private static Dictionary<CranialFinger, GripPointTarget> CreateSide(
        Transform parent, GripPointTarget template, CranialFinger[] fingers, bool leftSide, ref int created,
        CranialAdjustmentController rig,
        Dictionary<CranialFinger, Vector3> leftRef, Dictionary<CranialFinger, Vector3> rightRef)
    {
        var map = new Dictionary<CranialFinger, GripPointTarget>();
        if (fingers == null || fingers.Length == 0) return map;

        string sideName = leftSide ? "왼손" : "오른손";
        var sameSide = leftSide ? leftRef : rightRef;
        var otherSide = leftSide ? rightRef : leftRef;

        for (int i = 0; i < fingers.Length; i++)
        {
            var f = fingers[i];
            string label = $"{sideName}_{KoreanName(f)} 파지점";

            GripPointTarget grip = template != null
                ? Object.Instantiate(template, parent)
                : BuildFromScratch(parent);

            grip.gameObject.name = label;
            Undo.RegisterCreatedObjectUndo(grip.gameObject, "진단 파지점 생성");

            // 리그 로컬 기준 좌표를 구해 현재 부모 기준으로 변환해 배치.
            Vector3 rigLocal = SeedPosition(f, leftSide, i, sameSide, otherSide);
            grip.transform.position = rig.transform.TransformPoint(rigLocal);
            grip.transform.localRotation = Quaternion.identity;

            ApplySettings(grip, f);
            grip.gameObject.SetActive(false);   // 표시는 해당 진단 단계 진입 시 컨트롤러가 켠다

            map[f] = grip;
            created++;
        }
        return map;
    }

    private static void ApplySettings(GripPointTarget grip, CranialFinger finger)
    {
        var so = new SerializedObject(grip);
        so.FindProperty("finger").enumValueIndex = (int)finger;
        so.FindProperty("bypassPoseCheck").boolValue = true;    // 진단은 접촉만 본다(포즈 인식 무관)
        so.FindProperty("expectedFingerCollider").objectReferenceValue = null;  // 런타임에 컨트롤러가 주입
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>기존 파지점이 하나도 없을 때의 최소 구성(트리거 구체 + 시각용 자식).</summary>
    private static GripPointTarget BuildFromScratch(Transform parent)
    {
        var go = new GameObject("파지점");
        go.transform.SetParent(parent, false);

        var col = go.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = 0.02f;

        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        var vis = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        vis.name = "Visual";
        vis.transform.SetParent(go.transform, false);
        vis.transform.localScale = Vector3.one * 0.04f;
        Object.DestroyImmediate(vis.GetComponent<Collider>());

        var grip = go.AddComponent<GripPointTarget>();
        var so = new SerializedObject(grip);
        so.FindProperty("targetRenderer").objectReferenceValue = vis.GetComponent<Renderer>();
        so.ApplyModifiedPropertiesWithoutUndo();
        return grip;
    }

    /// <summary>리그에 이미 배선된 파지점 중 하나를 복제 템플릿으로 쓴다(시각·콜라이더 설정 승계).</summary>
    private static GripPointTarget FindTemplate(CranialAdjustmentController r)
    {
        var so = new SerializedObject(r);
        foreach (string arr in new[] { "rightGrips", "leftGrips", "diagnosisRightGrips" })
        {
            var p = so.FindProperty(arr);
            if (p == null || !p.isArray) continue;
            for (int i = 0; i < p.arraySize; i++)
            {
                var g = p.GetArrayElementAtIndex(i).objectReferenceValue as GripPointTarget;
                if (g != null) return g;
            }
        }
        return r.GetComponentInChildren<GripPointTarget>(true);
    }

    private static string KoreanName(CranialFinger f)
    {
        switch (f)
        {
            case CranialFinger.Palm:   return "손바닥";
            case CranialFinger.Thumb:  return "엄지";
            case CranialFinger.Index:  return "검지";
            case CranialFinger.Middle: return "중지";
            case CranialFinger.Ring:   return "약지";
            case CranialFinger.Pinky:  return "새끼";
        }
        return f.ToString();
    }

    // ================= 배선 =================

    private static void WireStages(CranialAdjustmentController r, List<StageBuild> stages)
    {
        var so = new SerializedObject(r);
        var stagesProp = so.FindProperty("diagnosisStages");
        stagesProp.arraySize = stages.Count;

        for (int s = 0; s < stages.Count; s++)
        {
            var st = stages[s];
            var sp = stagesProp.GetArrayElementAtIndex(s);
            sp.FindPropertyRelative("stageId").stringValue = st.stageId;
            sp.FindPropertyRelative("holdSeconds").floatValue = st.holdSeconds;
            sp.FindPropertyRelative("showBreathingCue").boolValue = st.showBreathingCue;

            var posesProp = sp.FindPropertyRelative("poses");
            posesProp.arraySize = st.poses.Count;

            for (int i = 0; i < st.poses.Count; i++)
            {
                var pose = st.poses[i];
                var pp = posesProp.GetArrayElementAtIndex(i);
                pp.FindPropertyRelative("label").stringValue = pose.label;
                AssignHand(pp.FindPropertyRelative("leftHand"), pose.leftCreated);
                AssignHand(pp.FindPropertyRelative("rightHand"), pose.rightCreated);
            }
        }

        so.ApplyModifiedProperties();
    }

    private static void AssignHand(SerializedProperty handProp, Dictionary<CranialFinger, GripPointTarget> map)
    {
        Set(handProp, "palmGrip", map, CranialFinger.Palm);
        Set(handProp, "thumbGrip", map, CranialFinger.Thumb);
        Set(handProp, "indexGrip", map, CranialFinger.Index);
        Set(handProp, "pinkyGrip", map, CranialFinger.Pinky);
    }

    private static void Set(SerializedProperty handProp, string field,
                            Dictionary<CranialFinger, GripPointTarget> map, CranialFinger f)
    {
        GripPointTarget g = null;
        if (map != null) map.TryGetValue(f, out g);
        handProp.FindPropertyRelative(field).objectReferenceValue = g;
    }

    /// <summary>선택한 리그에 CranialBreathAnimator를 붙이고 Animator·호흡 HUD·컨트롤러 참조를 자동 연결한다.
    /// State 존재 여부까지 확인해 결과를 알려준다(없으면 Animator 창에서 추가해야 함).</summary>
    private void SetupBreathAnimator()
    {
        if (rig == null) return;

        var anim = rig.GetComponent<CranialBreathAnimator>();
        if (anim == null)
        {
            anim = Undo.AddComponent<CranialBreathAnimator>(rig.gameObject);
            status = "CranialBreathAnimator를 새로 추가했습니다.\n";
        }
        else
        {
            status = "이미 있는 CranialBreathAnimator를 다시 설정했습니다.\n";
        }

        // 참조 자동 연결 — 비워두면 런타임에도 자동 탐색되지만, 인스펙터에 보이게 채워 둔다.
        var patientAnimator = FindPatientAnimator();
        var hud = Object.FindObjectsByType<BreathingSyncHUD>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        var so = new SerializedObject(anim);
        if (patientAnimator != null) so.FindProperty("patientAnimator").objectReferenceValue = patientAnimator;
        if (hud.Length > 0) so.FindProperty("breathingHUD").objectReferenceValue = hud[0];
        so.ApplyModifiedProperties();

        string stateName = new SerializedObject(anim).FindProperty("stateName").stringValue;
        bool hasState = patientAnimator != null
                        && patientAnimator.runtimeAnimatorController != null
                        && patientAnimator.HasState(0, Animator.StringToHash(stateName));

        status +=
            $"  Animator = {(patientAnimator != null ? patientAnimator.name : "못 찾음 — 직접 연결 필요")}\n" +
            $"  Controller = {(patientAnimator != null && patientAnimator.runtimeAnimatorController != null ? patientAnimator.runtimeAnimatorController.name : "없음")}\n" +
            $"  호흡 HUD = {(hud.Length > 0 ? hud[0].name : "못 찾음")}\n" +
            $"  State '{stateName}' 존재 = {(hasState ? "예" : "아니오 ← Animator 창에서 이 이름의 State를 만들어야 재생됩니다")}";

        EditorUtility.SetDirty(anim);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);
        Selection.activeGameObject = rig.gameObject;
    }

    /// <summary>환자 모델의 Animator를 찾는다(리그의 patientModelRoot → 태그 Patient → 씬에서 Humanoid Animator).</summary>
    private Animator FindPatientAnimator()
    {
        var so = new SerializedObject(rig);
        var rootProp = so.FindProperty("patientModelRoot");
        var root = rootProp != null ? rootProp.objectReferenceValue as Transform : null;

        if (root == null)
        {
            try
            {
                var tagged = GameObject.FindWithTag("Patient");
                if (tagged != null) root = tagged.transform;
            }
            catch { /* 태그 미정의 무시 */ }
        }
        if (root != null)
        {
            var a = root.GetComponentInChildren<Animator>(true);
            if (a != null) return a;
        }

        // 폴백: 씬에서 Controller가 붙은 Animator 아무거나
        foreach (var a in Object.FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (a != null && a.runtimeAnimatorController != null) return a;
        return null;
    }

    /// <summary>교정 파지(leftGrips/rightGrips)에서 중지·약지 파지점을 제거해 3점 파지로 만든다.
    /// 배열에서 빼는 것만으로는 씬에 켜져 있는 구체가 그대로 보이므로 오브젝트도 함께 삭제한다.
    /// (진단 파지점 diagnosisStages는 건드리지 않는다 — 거기 3점은 이미 엄지·검지·새끼로만 만들어진다.)</summary>
    private void MakeThreePointGrip()
    {
        if (rig == null) return;

        var doomed = new List<GameObject>();
        var so = new SerializedObject(rig);

        foreach (string arrName in new[] { "leftGrips", "rightGrips", "diagnosisRightGrips" })
        {
            var arr = so.FindProperty(arrName);
            if (arr == null || !arr.isArray) continue;

            for (int i = arr.arraySize - 1; i >= 0; i--)
            {
                var g = arr.GetArrayElementAtIndex(i).objectReferenceValue as GripPointTarget;
                if (g == null) continue;
                if (g.Finger != CranialFinger.Middle && g.Finger != CranialFinger.Ring) continue;

                if (!doomed.Contains(g.gameObject)) doomed.Add(g.gameObject);
                arr.DeleteArrayElementAtIndex(i);   // 참조 슬롯이면 1회 호출로 요소가 제거된다
            }
        }
        so.ApplyModifiedProperties();

        if (doomed.Count == 0)
        {
            status = "중지·약지 파지점이 없습니다 — 이미 3점이거나 다른 손가락으로 배선돼 있습니다.";
            return;
        }

        foreach (var go in doomed) Undo.DestroyObjectImmediate(go);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);

        var names = new List<string>();
        foreach (var go in doomed) names.Add(go == null ? "(삭제됨)" : go.name);
        status = $"교정 파지점 3점화 완료 — 파지점 {doomed.Count}개 제거.\n" +
                 "제거: " + string.Join(", ", names) + "\n" +
                 "되돌리려면 Ctrl+Z.";
    }

    /// <summary>PM 교정 파지를 실제 술기대로 구성한다.
    ///   왼손 = 엄지(기존 후두 파지점 재사용) + 중지(환자 정수리, 신규)
    ///   오른손 = 엄지(기존 측두 파지점 재사용) + 검지(신규)
    /// 기존 파지점은 위치를 살리고 손가락 지정만 바로잡으며, 없는 것만 새로 만든다.</summary>
    private void SetupPmCorrectionGrips()
    {
        if (rig == null) return;

        var template = FindTemplate(rig);
        var log = new List<string>();

        var left = EnsureCorrectionSide(rig, "leftGrips", true, template,
            new[] { CranialFinger.Thumb, CranialFinger.Middle }, log);
        var right = EnsureCorrectionSide(rig, "rightGrips", false, template,
            new[] { CranialFinger.Thumb, CranialFinger.Index }, log);

        EditorUtility.SetDirty(rig);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);
        if (left != null) Selection.activeGameObject = left.gameObject;

        status = "PM 교정 파지 구성 완료.\n" + string.Join("\n", log) +
                 "\n\n★ 새로 만든 파지점은 대략 위치라 씬 뷰에서 옮겨야 합니다:\n" +
                 "   · 왼손 중지 → 환자 정수리\n" +
                 "   · 오른손 검지 → 엄지 옆(귀를 따라간 파리에탈 노치 방향)\n" +
                 "되돌리려면 Ctrl+Z.";
    }

    /// <summary>늑골·흉추 술기의 교정 파지를 구성한다 — 양손 모두 '한 점을 대고 유지'하는 형태라 손바닥 1점씩이다.
    ///   제1늑골 — 왼손 웹(Web)을 좌측 제1늑골에 / 오른손은 머리 측면을 파지
    ///   제2늑골 — 두상골(Pisiform)을 제2늑골 부위에 / 반대손은 환자 팔을 파지
    ///   흉추(신전변위) — 좌우 흉추에 지지손을 대는 자세(파지 2.3 = cranialGrip)
    /// 웹·두상골은 손바닥 슬롯으로 근사한다(손가락 끝이 아니라 손 아래쪽 면이라 Palm이 가장 가깝다).</summary>
    private void SetupRibCorrectionGrips()
    {
        if (rig == null) return;

        var template = FindTemplate(rig);
        var log = new List<string>();

        var left = EnsureCorrectionSide(rig, "leftGrips", true, template,
            new[] { CranialFinger.Palm }, log);
        EnsureCorrectionSide(rig, "rightGrips", false, template,
            new[] { CranialFinger.Palm }, log);

        EditorUtility.SetDirty(rig);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);
        if (left != null) Selection.activeGameObject = left.gameObject;

        status = "교정 파지 구성 완료 (좌우 손바닥 1점씩).\n" + string.Join("\n", log) +
                 "\n\n★ 위치는 씬 뷰에서 옮겨야 합니다:\n" +
                 "   · 제1늑골 — 왼손 = 좌측 제1늑골(승모근 아래), 오른손 = 머리 우측면\n" +
                 "   · 제2늑골 — 접촉손 = 쇄골 바깥 델토펙토랄, 반대손 = 환자 팔(상완)\n" +
                 "   · 흉추 신전변위 — 좌우 흉추(등 아래로 손을 넣는 지점)\n" +
                 "되돌리려면 Ctrl+Z.";
    }

    /// <summary>한 손의 교정 파지점 배열을 지정한 손가락 구성으로 맞춘다.
    /// 기존 파지점은 순서대로 재사용해 손가락만 바꾸고(위치 보존), 모자라면 새로 만든다.</summary>
    private GripPointTarget EnsureCorrectionSide(
        CranialAdjustmentController r, string arrayName, bool leftSide,
        GripPointTarget template, CranialFinger[] want, List<string> log)
    {
        var so = new SerializedObject(r);
        var arr = so.FindProperty(arrayName);
        if (arr == null || !arr.isArray) return null;

        var existing = new List<GripPointTarget>();
        for (int i = 0; i < arr.arraySize; i++)
        {
            var g = arr.GetArrayElementAtIndex(i).objectReferenceValue as GripPointTarget;
            if (g != null) existing.Add(g);
        }

        Transform parent = existing.Count > 0 && existing[0].transform.parent != null
            ? existing[0].transform.parent : r.transform;

        var result = new List<GripPointTarget>();
        for (int i = 0; i < want.Length; i++)
        {
            CranialFinger f = want[i];
            GripPointTarget grip;

            if (i < existing.Count)
            {
                grip = existing[i];
                CranialFinger before = grip.Finger;
                ApplySettings(grip, f);   // 위치는 그대로, 손가락 지정만 교정
                string newName = RenameForFinger(grip.gameObject.name, f);
                if (newName != grip.gameObject.name)
                {
                    Undo.RecordObject(grip.gameObject, "PM 교정 파지 구성");
                    grip.gameObject.name = newName;
                }
                log.Add($"  재사용: {grip.gameObject.name} ({KoreanName(before)} → {KoreanName(f)}, 위치 유지)");
            }
            else
            {
                grip = template != null ? Object.Instantiate(template, parent) : BuildFromScratch(parent);
                grip.gameObject.name = $"Grip_{(leftSide ? "왼손" : "오른손")}_{KoreanName(f)}";
                Undo.RegisterCreatedObjectUndo(grip.gameObject, "PM 교정 파지 구성");
                grip.transform.position = SeedNewCorrectionPoint(r, existing, f, leftSide, i);
                grip.transform.localRotation = Quaternion.identity;
                ApplySettings(grip, f);
                log.Add($"  신규: {grip.gameObject.name} ← 위치 조정 필요");
            }
            result.Add(grip);
        }

        arr.arraySize = result.Count;
        for (int i = 0; i < result.Count; i++)
            arr.GetArrayElementAtIndex(i).objectReferenceValue = result[i];
        so.ApplyModifiedProperties();

        return result.Count > 0 ? result[0] : null;
    }

    /// <summary>신규 교정 파지점의 대략 위치. 중지(정수리)는 머리 본 위쪽, 나머지는 기존 점 옆.</summary>
    private static Vector3 SeedNewCorrectionPoint(
        CranialAdjustmentController r, List<GripPointTarget> existing, CranialFinger f, bool leftSide, int index)
    {
        if (f == CranialFinger.Middle)
        {
            Transform head = FindHeadBone(r);
            if (head != null) return head.position + head.up * 0.09f;   // 정수리 근처
        }
        if (existing.Count > 0)
            return existing[0].transform.position + existing[0].transform.right * (leftSide ? 0.03f : -0.03f);

        return r.transform.position + new Vector3(leftSide ? 0.07f : -0.07f, 0.05f, 0f);
    }

    /// <summary>환자 머리 본(CC_Base_Head) 탐색 — 정수리 파지점 초기 배치용.</summary>
    private static Transform FindHeadBone(CranialAdjustmentController r)
    {
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t != null && t.name.IndexOf("CC_Base_Head", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return t;
        return null;
    }

    /// <summary>파지점 이름의 손가락 접미사를 새 손가락으로 바꾼다(위치를 뜻하는 앞부분은 보존).</summary>
    private static string RenameForFinger(string current, CranialFinger f)
    {
        if (string.IsNullOrEmpty(current)) return $"Grip_{KoreanName(f)}";
        string baseName = current;
        foreach (CranialFinger v in System.Enum.GetValues(typeof(CranialFinger)))
        {
            string en = "_" + v;
            string ko = "_" + KoreanName(v);
            if (baseName.EndsWith(en, System.StringComparison.OrdinalIgnoreCase))
                baseName = baseName.Substring(0, baseName.Length - en.Length);
            else if (baseName.EndsWith(ko, System.StringComparison.Ordinal))
                baseName = baseName.Substring(0, baseName.Length - ko.Length);
        }
        return $"{baseName}_{KoreanName(f)}";
    }

    /// <summary>구 진단 배선(diagnosisRightGrips) 참조를 비운다. 오브젝트는 지우지 않는다
    /// (대개 교정용 rightGrips와 같은 오브젝트를 공유하고 있어 지우면 교정 파지가 깨진다).</summary>
    private void ClearLegacyDiagnosisArray()
    {
        if (rig == null) return;
        var so = new SerializedObject(rig);
        var arr = so.FindProperty("diagnosisRightGrips");
        if (arr == null) { status = "diagnosisRightGrips 필드를 찾지 못했습니다."; return; }

        int before = arr.arraySize;
        arr.arraySize = 0;
        so.ApplyModifiedProperties();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);
        status = $"레거시 diagnosisRightGrips 배열을 비웠습니다 (참조 {before}개 해제, 오브젝트는 그대로).";
    }

    /// <summary>선택한 리그를 통째로 복제하고 scenarioName만 바꾼다(PJ처럼 리그가 없는 시나리오용).
    /// 리그 내부 참조(파지점·깊이 가이드 등)는 Unity가 복제본 쪽으로 다시 이어주고,
    /// 외부 참조(호흡 HUD·손 HandVisual·환자 모델)는 원본과 같은 대상을 그대로 가리킨다(의도된 동작).</summary>
    private void DuplicateRig()
    {
        if (rig == null) return;

        string wanted = newRigScenarioName.Trim();
        foreach (var r in FindRigs())
        {
            if (r.ScenarioName != null && r.ScenarioName.Trim() == wanted)
            {
                status = $"'{wanted}' 리그가 이미 있습니다 — 복제하지 않았습니다.";
                return;
            }
        }

        var copy = Object.Instantiate(rig.gameObject, rig.transform.parent);
        copy.name = $"CranialRig_{wanted}";
        Undo.RegisterCreatedObjectUndo(copy, "두개골 리그 복제");

        var ctrl = copy.GetComponent<CranialAdjustmentController>();
        var so = new SerializedObject(ctrl);
        so.FindProperty("scenarioName").stringValue = wanted;
        so.FindProperty("diagnosisStages").arraySize = 0;   // 복제본은 진단 단계를 새로 만든다
        so.ApplyModifiedProperties();

        // 복제된 진단 파지점 그룹은 stage 배선이 지워졌으니 같이 정리(고아 오브젝트 방지)
        Transform staleGroup = copy.transform.Find(GroupName);
        if (staleGroup != null) Undo.DestroyObjectImmediate(staleGroup.gameObject);

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(copy.scene);
        rig = ctrl;
        Selection.activeGameObject = copy;
        status = $"'{wanted}' 리그 복제 완료 ({copy.name}).\n" +
                 "이제 프리셋을 PMPJ로 두고 ①을 실행한 뒤, 파지점 위치를 맞추세요.";
    }

    private void DeleteGroup()
    {
        if (rig == null) return;
        Transform existing = rig.transform.Find(GroupName);
        if (existing == null) { status = $"'{GroupName}' 그룹이 없습니다."; return; }

        Undo.DestroyObjectImmediate(existing.gameObject);
        var so = new SerializedObject(rig);
        so.FindProperty("diagnosisStages").arraySize = 0;
        so.ApplyModifiedProperties();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);
        status = $"'{GroupName}' 그룹 삭제 + diagnosisStages 비움 완료.";
    }

    // ================= 빌드 기술서 =================

    private class StageBuild
    {
        public readonly string stageId;
        public readonly float holdSeconds;
        public readonly bool showBreathingCue;
        public readonly List<PoseBuild> poses = new List<PoseBuild>();

        public StageBuild(string stageId, float holdSeconds, bool showBreathingCue)
        {
            this.stageId = stageId;
            this.holdSeconds = holdSeconds;
            this.showBreathingCue = showBreathingCue;
        }
    }

    private class PoseBuild
    {
        public readonly string label;
        public readonly CranialFinger[] left;
        public readonly CranialFinger[] right;
        public Dictionary<CranialFinger, GripPointTarget> leftCreated;
        public Dictionary<CranialFinger, GripPointTarget> rightCreated;

        public PoseBuild(string label, CranialFinger[] left, CranialFinger[] right)
        {
            this.label = label;
            this.left = left;
            this.right = right;
        }
    }
}
