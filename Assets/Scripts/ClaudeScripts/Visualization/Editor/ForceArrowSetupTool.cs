using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 힘의 방향 화살표 생성 도구.
///
/// ★비파괴 — 오브젝트를 만들기만 하고 기존 것을 지우거나 재생성하지 않는다.
/// (진단 파지점 도구의 ①이 사용자 수작업 배치를 날린 사고가 있었다. 같은 실수를 반복하지 않는다.)
/// 전부 Undo로 되돌릴 수 있다.
///
/// 사용법: 파지점(또는 접촉 지점)을 선택 → 메뉴 실행 → 씬에서 화살표를 <b>회전</b>시켜 미는 방향을 맞춘다.
/// 화살표는 로컬 +Z를 가리킨다.
/// </summary>
public static class ForceArrowSetupTool
{
    private const string MeshPath = "Assets/Meshes/ForceArrow.asset";
    private const string MaterialPath = "Assets/Materials/ForceArrow.mat";
    private const float DefaultLength = 0.08f;   // 8cm — 두개골처럼 손과 머리 사이가 좁은 곳 기준

    // 직선(흐름) 화살표 기본값 — 오브젝트 스케일이 곱해진다
    private const int FlowChevrons = 5;        // 쐐기 수(마지막이 화살촉)
    private const float FlowSpacing = 0.22f;   // 쐐기 간격
    private const float FlowHeadScale = 1.55f; // 화살촉 배율

    // 회전 화살표 기본값 (미터 단위, 씬에서 스케일로 조정 가능)
    private const int ArcSegments = 7;        // 조각 수 = 흐름의 해상도. 마지막은 화살촉.
    private const float ArcSweepDeg = 100f;   // 호가 도는 각도
    private const float ArcRadius = 0.07f;    // 회전축에서 호까지 거리 (7cm)
    private const float ArcTube = 0.006f;     // 호 굵기
    private const int ArcRadialSeg = 8;       // 호 단면 분할
    private const float RunnerScale = 0.055f; // 호를 따라 달리는 쐐기 크기

    [MenuItem("GuideChuna/힘의 방향 화살표 만들기 (선택한 오브젝트 자식으로)")]
    private static void CreateArrow()
    {
        Transform parent = Selection.activeTransform;
        if (parent == null)
        {
            EditorUtility.DisplayDialog("힘의 방향 화살표",
                "부모로 삼을 오브젝트를 먼저 선택하세요.\n\n" +
                "보통 파지점이나 접촉 지점을 고릅니다 — 그래야 환자 애니메이션을 따라 움직입니다.", "확인");
            return;
        }

        Mesh mesh = LoadOrCreateMesh();
        Material mat = LoadOrCreateMaterial();

        var go = new GameObject("힘의 방향 화살표");
        Undo.RegisterCreatedObjectUndo(go, "힘의 방향 화살표 만들기");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one * DefaultLength;

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        go.AddComponent<ForceArrow>();

        Selection.activeGameObject = go;
        EditorGUIUtility.PingObject(go);
        Debug.Log($"[힘의 방향 화살표] '{parent.name}' 아래에 생성했습니다. " +
                  "씬에서 회전시켜 '손이 미는 방향'으로 맞추세요(화살표는 로컬 +Z를 가리킵니다).");
    }

    [MenuItem("GuideChuna/힘의 방향 화살표 (직선·흐름) 만들기 (선택한 오브젝트 자식으로)")]
    private static void CreateFlowArrow()
    {
        Transform parent = Selection.activeTransform;
        if (parent == null)
        {
            EditorUtility.DisplayDialog("힘의 방향 화살표",
                "부모로 삼을 오브젝트를 먼저 선택하세요.\n\n" +
                "보통 파지점이나 접촉 지점을 고릅니다 — 그래야 환자 애니메이션을 따라 움직입니다.", "확인");
            return;
        }

        ForceArrow made = BuildFlowArrow(parent, "힘의 방향 화살표 (흐름)");
        Selection.activeGameObject = made.gameObject;
        EditorGUIUtility.PingObject(made.gameObject);
        Debug.Log($"[힘의 방향 화살표] '{parent.name}' 아래에 쐐기 {FlowChevrons}개로 생성했습니다. " +
                  "로컬 +Z가 미는 방향입니다 — 씬에서 회전시켜 맞추세요.");
    }

    /// <summary>직선(흐름) 화살표 하나를 만들어 돌려준다(메뉴·자동 배치가 공유).</summary>
    private static ForceArrow BuildFlowArrow(Transform parent, string name)
    {
        Mesh chevron = LoadOrCreateChevronMesh();
        Material mat = LoadOrCreateMaterial();

        var root = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(root, "힘의 방향 화살표 만들기");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one * DefaultLength;

        // 꼬리 → 머리 순으로 쐐기(>)를 늘어놓는다. 정지 상태에서도 방향이 읽히고,
        // 여기에 흐름이 얹히면 진행 방향이 또렷해진다.
        var segs = new List<Renderer>();
        for (int i = 0; i < FlowChevrons; i++)
        {
            bool isHead = i == FlowChevrons - 1;
            float z = i * FlowSpacing;
            float s = isHead ? FlowHeadScale : 1f;

            var go = new GameObject(isHead ? "화살촉" : $"쐐기{i + 1}");
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, z);
            go.transform.localScale = new Vector3(s, s, s);

            go.AddComponent<MeshFilter>().sharedMesh = chevron;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            segs.Add(mr);
        }

        var arrow = root.AddComponent<ForceArrow>();
        var so = new SerializedObject(arrow);
        SerializedProperty prop = so.FindProperty("segments");
        prop.ClearArray();
        for (int i = 0; i < segs.Count; i++)
        {
            prop.InsertArrayElementAtIndex(i);
            prop.GetArrayElementAtIndex(i).objectReferenceValue = segs[i];
        }
        so.ApplyModifiedProperties();

        return arrow;
    }

    [MenuItem("GuideChuna/힘의 방향 화살표 (회전) 만들기 (선택한 오브젝트 자식으로)")]
    private static void CreateArcArrow()
    {
        Transform parent = Selection.activeTransform;
        if (parent == null)
        {
            EditorUtility.DisplayDialog("회전 화살표",
                "부모로 삼을 오브젝트를 먼저 선택하세요.\n\n" +
                "굴곡·신전이면 회전 중심(귀 높이)쯤에 두는 게 자연스럽습니다.", "확인");
            return;
        }

        ForceArcArrow made = BuildArcArrow(parent, "회전 방향 화살표");
        Selection.activeGameObject = made.gameObject;
        EditorGUIUtility.PingObject(made.gameObject);
        Debug.Log($"[회전 화살표] '{parent.name}' 아래에 {ArcSegments}조각 + 러너로 생성했습니다.\n" +
                  "★회전축 = 이 오브젝트의 로컬 +Y(초록 축)입니다. 굴곡·신전이면 좌우 귀를 잇는 축에 맞추세요.\n" +
                  "도는 방향을 뒤집으려면 오브젝트를 Y축으로 180° 돌리거나 X 스케일을 -1로 두면 됩니다.");
    }

    /// <summary>회전 화살표 하나를 만들어 돌려준다(메뉴·자동 배치가 공유).</summary>
    private static ForceArcArrow BuildArcArrow(Transform parent, string name)
    {
        Material mat = LoadOrCreateMaterial();

        var root = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(root, "회전 화살표 만들기");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;

        var segments = new List<Renderer>();
        float step = ArcSweepDeg / ArcSegments;
        for (int i = 0; i < ArcSegments; i++)
        {
            float a0 = i * step;
            float a1 = a0 + step * 0.82f;      // 조각 사이에 틈 — 흐름이 또렷하게 보인다
            bool isHead = i == ArcSegments - 1;

            // ★메시는 반드시 에셋으로 저장한다 — new Mesh()를 씬에서 참조만 하면
            //   씬을 다시 열었을 때 참조가 끊겨 화살표가 사라진다.
            //   조각 기하는 모든 회전 화살표가 같으므로 한 번 만들어 공유한다.
            Mesh mesh = LoadOrCreateArcMesh(i, isHead, a0, a1);

            var go = new GameObject(isHead ? "화살촉" : $"조각{i + 1}");
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            segments.Add(mr);

        }

        // ★러너 = 호를 따라 실제로 이동하는 쐐기.
        //   알파만 흐르면 얇은 튜브에서는 '깜빡임'으로 읽혀 회전이 정지처럼 보인다.
        var runnerGo = new GameObject("러너");
        runnerGo.transform.SetParent(root.transform, false);
        runnerGo.transform.localScale = Vector3.one * RunnerScale;
        runnerGo.AddComponent<MeshFilter>().sharedMesh = LoadOrCreateChevronMesh();
        var runnerMr = runnerGo.AddComponent<MeshRenderer>();
        runnerMr.sharedMaterial = mat;
        runnerMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        runnerMr.receiveShadows = false;

        var arc = root.AddComponent<ForceArcArrow>();
        var so = new SerializedObject(arc);
        so.FindProperty("arcRadius").floatValue = ArcRadius;
        so.FindProperty("arcSweepDeg").floatValue = ArcSweepDeg;
        so.FindProperty("runner").objectReferenceValue = runnerGo.transform;
        so.FindProperty("runnerRenderer").objectReferenceValue = runnerMr;

        SerializedProperty segProp = so.FindProperty("segments");
        segProp.ClearArray();
        for (int i = 0; i < segments.Count; i++)
        {
            segProp.InsertArrayElementAtIndex(i);
            segProp.GetArrayElementAtIndex(i).objectReferenceValue = segments[i];
        }
        so.ApplyModifiedProperties();

        return arc;
    }

    [MenuItem("GuideChuna/힘의 방향 화살표 그룹 만들기 (선택한 오브젝트 자식으로)")]
    private static void CreateGroup()
    {
        Transform parent = Selection.activeTransform;
        if (parent == null)
        {
            EditorUtility.DisplayDialog("힘의 방향 화살표",
                "그룹을 붙일 리그(또는 상위 오브젝트)를 먼저 선택하세요.", "확인");
            return;
        }

        var go = new GameObject("화살표 그룹 (단계 이름 지정 필요)");
        Undo.RegisterCreatedObjectUndo(go, "화살표 그룹 만들기");
        go.transform.SetParent(parent, false);
        go.AddComponent<ForceArrowGroup>();

        Selection.activeGameObject = go;
        Debug.Log("[힘의 방향 화살표] 그룹을 만들었습니다. 인스펙터에서 Step Name(예: 교정)을 채우세요.");
    }

    // ============================ PM·PJ 자동 배치 ============================

    /// <summary>
    /// PM·PJ 리그에 힘의 방향 화살표를 규격대로 만들어 붙인다.
    ///
    /// ★비파괴 — 같은 이름의 그룹이 이미 있으면 그 리그는 건너뛴다(수작업 배치를 덮지 않는다).
    /// ★위치·각도는 대략값이다. 만든 뒤 씬 뷰에서 회전시켜 실제 방향에 맞춰야 한다.
    ///
    /// 규격(08-10 사용자 구술):
    ///   PM — 잠금 없음. 양손 유양돌기를 <b>족방→두방으로 견인</b> → 직선 화살표 2개(양손), 국면 전체.
    ///   PJ — 방향이 중간에 <b>뒤집힌다</b>:
    ///        ⓐ 굴곡(왼손)·외회전(오른손) : 굴곡외회전 → 견착 → 호흡
    ///        ⓑ 신전(왼손)·내회전(오른손) : 전환 → 호흡2
    ///        그래서 같은 화살표 묶음을 단계 이름만 다른 그룹 여러 개가 공유한다.
    /// </summary>
    [MenuItem("GuideChuna/힘의 방향 화살표 기본 배치 (PM·PJ)")]
    private static void PlaceForPmPj()
    {
        var rigs = new List<CranialAdjustmentController>();
        foreach (CranialAdjustmentController c in Resources.FindObjectsOfTypeAll<CranialAdjustmentController>())
            if (c != null && c.gameObject.scene.IsValid()) rigs.Add(c);

        var log = new System.Text.StringBuilder();
        int made = 0;

        foreach (CranialAdjustmentController rig in rigs)
        {
            string name = ScenarioNameOf(rig);
            bool isPm = name.Contains("PM");
            bool isPj = name.Contains("PJ");
            if (!isPm && !isPj) continue;

            Transform left = FirstGrip(rig, "leftGrips");
            Transform right = FirstGrip(rig, "rightGrips");
            if (left == null || right == null)
            {
                log.AppendLine($"  {name}: ★교정 파지점이 없어 건너뜀 (leftGrips/rightGrips 확인)");
                continue;
            }

            if (isPm)
            {
                if (rig.transform.Find(PmGroupName) != null) { log.AppendLine($"  {name}: 이미 있음 — 건너뜀"); continue; }

                // 족방→두방 견인 = 직선. 양손에 같은 방향으로 하나씩.
                ForceArrow a1 = BuildFlowArrow(left, "힘의 방향 (왼손 견인)");
                ForceArrow a2 = BuildFlowArrow(right, "힘의 방향 (오른손 견인)");
                MakeGroup(rig.transform, PmGroupName, ForceArrowBase.ShowScope.교정국면_파지제외, "", 0,
                          new ForceArrowBase[] { a1, a2 });
                made += 2;
                log.AppendLine($"  {name}: 직선 2개(양손 견인) + 그룹 1개 — 국면 전체 표시");
            }
            else
            {
                if (rig.transform.Find("화살표 그룹 PJ 굴곡외회전") != null) { log.AppendLine($"  {name}: 이미 있음 — 건너뜀"); continue; }

                // ⓐ 굴곡(왼손) + 외회전(오른손)
                ForceArcArrow flex = BuildArcArrow(left, "회전 (왼손 굴곡)");
                ForceArcArrow exRot = BuildArcArrow(right, "회전 (오른손 외회전)");
                var setA = new ForceArrowBase[] { flex, exRot };

                // ⓑ 신전(왼손) + 내회전(오른손) — 같은 호를 반대로 돌린다(X 스케일 -1).
                ForceArcArrow ext = BuildArcArrow(left, "회전 (왼손 신전)");
                ForceArcArrow inRot = BuildArcArrow(right, "회전 (오른손 내회전)");
                Flip(ext); Flip(inRot);
                var setB = new ForceArrowBase[] { ext, inRot };

                // ★한 그룹은 단계 하나만 매칭한다 → 같은 묶음을 단계별 그룹이 공유한다.
                foreach (string step in new[] { "굴곡외회전", "견착", "호흡" })
                    MakeGroup(rig.transform, $"화살표 그룹 PJ {step}", ForceArrowBase.ShowScope.특정_단계만, step, 0, setA);
                foreach (string step in new[] { "전환", "호흡2" })
                    MakeGroup(rig.transform, $"화살표 그룹 PJ {step}", ForceArrowBase.ShowScope.특정_단계만, step, 0, setB);

                made += 4;
                log.AppendLine($"  {name}: 회전 4개(굴곡·외회전 / 신전·내회전) + 그룹 5개 — 단계별 전환");
            }
        }

        string msg = made == 0
            ? "새로 만든 화살표가 없습니다(이미 있거나 PM·PJ 리그를 찾지 못함).\n" + log
            : $"화살표 {made}개를 만들었습니다.\n{log}\n" +
              "★씬 뷰에서 각각 회전시켜 실제 방향에 맞추세요:\n" +
              "   · 직선 = 로컬 +Z(파란 축)가 미는 방향\n" +
              "   · 회전 = 로컬 +Y(초록 축)가 회전축, +Z에서 +X로 돕니다\n" +
              "   · 굴곡·신전은 좌우 귀를 잇는 축, 내·외회전은 머리 수직축에 맞추면 대체로 맞습니다\n" +
              "되돌리려면 Ctrl+Z. 확인은 메뉴 '힘의 방향 화살표 점검'.";
        Debug.Log("[힘의 방향 화살표 기본 배치]\n" + msg);
        EditorUtility.DisplayDialog("힘의 방향 화살표 기본 배치 (PM·PJ)", msg, "확인");
    }

    /// <summary>
    /// 늑골·흉추 술기의 힘의 방향 화살표를 CSV 술기 순서대로 배치한다.
    ///
    /// ★<b>주체가 둘</b>이라 색으로 나눈다 — 시술자(주황) / 환자(청록).
    /// 제1늑골은 중간에 <b>환자가 미는 등척성 저항</b>이 들어가는데, 이게 표현되지 않으면
    /// "누가 힘을 내는 단계인지"가 화면에 전혀 안 드러난다(사용자 지시).
    ///
    /// 제1늑골 교정기법 (교정·호흡 3.1~3.6)
    ///   ※신전·우측 병진의 <b>첫 세팅은 파지 단계(2.3~2.4)</b>에서 <c>제1늑골 고개</c> 클립이
    ///     파지 성립과 함께 자동으로 만들어 준다 — 교정 국면의 화살표는 그 다음 이야기다.
    ///   3.1~3.2  시술자: 왼손 검지 측면으로 제1늑골을 <b>족방</b>으로 누름
    ///   3.3~3.4  ★등척성 — <b>세 힘이 동시에</b> 걸린다:
    ///              · 왼손이 늑골을 족방으로 누르는 동안
    ///              · 환자가 <b>우측으로 밀고</b>                          (청록)
    ///              · 시술자 오른손이 <b>측두에서 맞저항</b>한다            (주황, 환자와 반대 방향)
    ///            서로 맞서는 두 힘을 같이 그려야 '등척성'이 화면에 드러난다.
    ///   3.3      늘어난 범위까지 다시 신전·우측 병진 — <b>설명만 하는 단계라 화살표를 두지 않는다.</b>
    ///
    /// 앙와위_흉추_신전변위 (교정 3.1~3.5)
    ///   시술자: 환자를 세우고 내리는 <b>견인</b> — 국면 내내 표시
    ///
    /// 복와위_하부흉추_굴곡변위 (교정 3.3 — 순간 교정)
    ///   주동수(족방수) <b>후방 → 전방</b>   · 보조수(두방수) <b>두방 → 족방</b>
    ///   두 손이 서로 다른 방향으로 동시에 민다 — 둘 다 띄워야 역할 차이가 보인다.
    /// </summary>
    [MenuItem("GuideChuna/힘의 방향 화살표 기본 배치 (늑골·흉추)")]
    private static void PlaceForRibThoracic()
    {
        var log = new System.Text.StringBuilder();
        int made = 0;
        created = 0;

        foreach (CranialAdjustmentController rig in Resources.FindObjectsOfTypeAll<CranialAdjustmentController>())
        {
            if (rig == null || !rig.gameObject.scene.IsValid()) continue;

            string name = ScenarioNameOf(rig);
            bool isRib1 = name.Contains("제1늑골");
            bool isRib2 = name.Contains("제2늑골");
            bool isProne = name.Contains("복와위");
            bool isThoracic = !isProne && name.Contains("흉추");
            if (!isRib1 && !isRib2 && !isThoracic && !isProne) continue;

            Transform left = FirstGrip(rig, "leftGrips");
            Transform right = FirstGrip(rig, "rightGrips");
            if (left == null || right == null)
            {
                log.AppendLine($"  {name}: ★교정 파지점이 없어 건너뜀 (leftGrips/rightGrips 확인)");
                continue;
            }
            // ★이미 만든 그룹이 있으면 <b>지우고 다시 만든다</b>(2026-08-12).
            //   예전엔 '이미 있음 — 건너뜀'이라, 도구를 고쳐도 다시 돌리면 아무 일이 안 일어났다.
            //   그래서 화살표가 1개인 옛 배치가 계속 남아 "저항 단계에 환자 화살표만 나온다"가 됐다.
            //   ★<b>화살표는 절대 지우지 않는다</b> — 위치·회전·색을 씬에서 손으로 맞춰 둔 자산이다.
            //     그룹(빈 오브젝트, 참조만 들고 있음)만 지우고 다시 엮는다.
            //     없는 화살표만 새로 만들고, 그때 색은 같은 리그의 기존 화살표에서 물려받는다.
            for (int c = rig.transform.childCount - 1; c >= 0; c--)
            {
                Transform child = rig.transform.GetChild(c);
                if (child != null && child.name.StartsWith("화살표 그룹 "))
                    Undo.DestroyObjectImmediate(child.gameObject);
            }

            if (isRib1)
            {
                // ① 시술자 — 제1늑골 족방 압박.
                //    ★등척성 구간(3.1 지시 · 3.2 판정)에서만 표시한다.
                //      3.3 '재병진'은 판정이 없는 설명 단계라 압박 유지를 요구하지 않는다 →
                //      거기까지 화살표를 띄우면 하지 않아도 되는 걸 하라는 신호가 된다(2026-08-12 사용자 지시).
                ForceArrowBase press = Reuse(rig, "족방 압박")
                                       ?? Adopt(rig, BuildFlowArrow(left, "힘의 방향 (왼손 족방 압박)"));
                foreach (int sub in new[] { 1, 2 })
                    MakeGroup(rig.transform, $"화살표 그룹 {name} 압박 {sub}",
                              ForceArrowBase.ShowScope.특정_단계만, "교정·호흡", sub, new ForceArrowBase[] { press });

                // ② 등척성 — ★서로 맞서는 두 힘을 <b>같이</b> 보여야 원리가 보인다.
                //    환자가 신전·우측 병진 방향으로 저항하고, 시술자는 측두에서 그걸 맞받는다.
                //    한쪽만 그리면 "누르는 힘"으로만 읽혀 등척성이라는 게 드러나지 않는다.
                //    ★환자가 내는 힘은 <b>'우측으로 미는' 것뿐</b>이다(CSV 교정2).
                //      신전·우측 병진은 시술자가 환자 머리를 움직이는 것이라 환자 화살표에 붙이면 틀린다.
                ForceArrowBase resist = Reuse(rig, "환자 우측 밀기") ?? Reuse(rig, "환자 저항");
                if (resist == null)
                {
                    resist = Adopt(rig, BuildFlowArrow(right, "힘의 방향 (환자 우측 밀기)"));
                    SetActor(resist, ForceArrowBase.Actor.Patient);
                }

                ForceArrowBase counter = Reuse(rig, "맞저항");
                if (counter == null)
                {
                    // ★새로 만드는 맞저항은 <b>환자 화살표를 복제</b>해 만든다 —
                    //   위치·회전을 그대로 물려받고 방향만 뒤집으면 되므로 씬 작업이 거의 없다.
                    counter = Adopt(rig, CloneArrow(resist, "힘의 방향 (오른손 맞저항 · 측두)"));
                    SetActor(counter, ForceArrowBase.Actor.Practitioner);
                    FlipDirection(counter);
                }

                // ★두 화살표를 <b>같은 그룹</b>에 넣어야 지시(3.1)와 판정(3.2) 내내 <b>함께</b> 켜진다.
                var isometric = new ForceArrowBase[] { resist, counter };
                foreach (int sub in new[] { 1, 2 })
                    MakeGroup(rig.transform, $"화살표 그룹 {name} 등척성 {sub}",
                              ForceArrowBase.ShowScope.특정_단계만, "교정·호흡", sub, isometric);

                // ③ 신전·우측 병진 화살표는 <b>쓰지 않는다</b>(2026-08-12 사용자 결정).
                //    3.3은 판정도 애니메이션도 없이 말로만 설명하는 단계라 방향 표시를 두지 않는다.
                //    ★남아 있으면 어느 그룹에도 안 묶여 '단독 화살표'로 취급돼 교정 국면 내내 켜지므로 지운다.
                ForceArrowBase stale = Reuse(rig, "신전·우측 병진") ?? Reuse(rig, "병진");
                if (stale != null)
                {
                    log.AppendLine($"  {name}: 사용하지 않는 '{stale.name}' 화살표 삭제");
                    Undo.DestroyObjectImmediate(stale.gameObject);
                }

                made += created;
                log.AppendLine($"  {name}: 화살표 3개 중 {created}개 신규 생성 · 나머지는 기존 것 재사용 / 그룹 4개 재구성 " +
                               "(압박·등척성 모두 3.1~3.2에만 표시, 3.3 재병진에는 화살표 없음)");
            }
            else if (isRib2)
            {
                // ① 시술자 — 두상골로 제2늑골 족방 압박. 교정·호흡 내내.
                ForceArrow press = BuildFlowArrow(left, "힘의 방향 (두상골 족방 압박)");
                MakeGroup(rig.transform, $"화살표 그룹 {name} 압박",
                          ForceArrowBase.ShowScope.특정_단계만, "교정·호흡", 0, new ForceArrowBase[] { press });

                // ② 시술자 — 호흡에 맞춰 팔을 올리고 내림(3.3~3.5).
                ForceArrow lift = BuildFlowArrow(right, "힘의 방향 (팔 거상)");
                foreach (int sub in new[] { 3, 4, 5 })
                    MakeGroup(rig.transform, $"화살표 그룹 {name} 거상 {sub}",
                              ForceArrowBase.ShowScope.특정_단계만, "교정·호흡", sub, new ForceArrowBase[] { lift });

                made += 2;
                log.AppendLine($"  {name}: 시술자 2 + 그룹 4개");
            }
            else if (isProne)
            {
                // 복와위 하부흉추 — ★두 손이 <b>서로 다른 방향</b>으로 동시에 민다(매뉴얼).
                //     주동수(족방수) 후방 → 전방   = 등에서 배 쪽, 아래로
                //     보조수(두방수) 두방 → 족방   = 머리 쪽에서 발 쪽으로
                //   방향이 달라야 두 손의 역할이 드러나므로 반드시 둘 다 띄운다.
                //   순간 교정(3.3)에서만 표시 — 호흡을 따라가는 동안에는 힘을 주지 않는다.
                ForceArrowBase down = Reuse(rig, "후방")
                                      ?? Adopt(rig, BuildFlowArrow(left, "힘의 방향 (주동수 후방→전방)"));
                ForceArrowBase footward = Reuse(rig, "족방")
                                          ?? Adopt(rig, BuildFlowArrow(right, "힘의 방향 (보조수 두방→족방)"));

                // ★호흡 유도와 순간 교정을 한 substep으로 합치면서 3.3 → 3.2로 당겨졌다.
                MakeGroup(rig.transform, $"화살표 그룹 {name} 순간교정",
                          ForceArrowBase.ShowScope.특정_단계만, "교정", 2,
                          new ForceArrowBase[] { down, footward });

                made += created;
                log.AppendLine($"  {name}: 화살표 2개 중 {created}개 신규 · 그룹 1개 (교정 3.2에만 표시)");
            }
            // ★앙와위 흉추 신전은 화살표를 쓰지 않는다(2026-08-12 사용자 결정).
            //   환자를 세우고 내리는 동작 자체가 방향을 보여 주므로 화살표가 군더더기다.
            //   전에 만든 게 남아 있으면 지운다 — 그룹에 안 묶인 화살표는 '단독'으로 취급돼
            //   교정 국면 내내 켜지기 때문이다.
            else if (isThoracic)
            {
                foreach (ForceArrowBase stale in rig.GetComponentsInChildren<ForceArrowBase>(true))
                {
                    if (stale == null) continue;
                    log.AppendLine($"  {name}: 사용하지 않는 '{stale.name}' 화살표 삭제");
                    Undo.DestroyObjectImmediate(stale.gameObject);
                }
            }
        }

        string msg =
            $"화살표 신규 생성 {created}개 · 그룹 재구성 완료.\n{log}\n" +
            "★기존 화살표는 지우지 않았습니다 — 위치·회전·색 그대로입니다.\n" +
            "   새로 만든 것만 씬 뷰에서 방향을 맞추세요(로컬 +Z가 미는 방향).\n" +
            "   맞저항 화살표는 환자 화살표를 복제해 180° 돌려 둔 것이라 대개 그대로 맞습니다.\n\n" +
            "※제2늑골·복와위는 씬에 리그가 없으면 붙일 대상이 없습니다.\n" +
            "되돌리려면 Ctrl+Z. 확인은 메뉴 '힘의 방향 화살표 점검'.";
        Debug.Log("[힘의 방향 화살표 기본 배치 (늑골·흉추)]\n" + msg);
        EditorUtility.DisplayDialog("힘의 방향 화살표 기본 배치 (늑골·흉추)", msg, "확인");
    }

    /// <summary>
    /// 씬의 모든 화살표 색을 <b>시술자 초록 · 환자 청록</b>으로 통일한다.
    ///
    /// ★색은 씬에 직렬화돼 있어 코드 기본값을 바꿔도 <b>이미 배치된 화살표에는 안 먹는다</b>.
    /// 새로 만든 것만 초록이고 옛것은 주황으로 남아 뒤섞이므로, 한 번에 맞추는 버튼을 둔다.
    /// </summary>
    [MenuItem("GuideChuna/힘의 방향 화살표 색 통일 (시술자 초록)")]
    private static void UnifyArrowColors()
    {
        Color practitioner = new Color(0.149f, 1f, 0.318f, 1f);   // #26FF51
        Color patient = new Color(0.25f, 0.8f, 0.95f, 1f);        // #3FCCF2

        int n = 0;
        var log = new System.Text.StringBuilder();
        foreach (ForceArrowBase a in Resources.FindObjectsOfTypeAll<ForceArrowBase>())
        {
            if (a == null || !a.gameObject.scene.IsValid()) continue;

            var so = new SerializedObject(a);
            SerializedProperty p = so.FindProperty("practitionerColor");
            SerializedProperty q = so.FindProperty("patientColor");
            bool changed = false;

            if (p != null && p.colorValue != practitioner) { p.colorValue = practitioner; changed = true; }
            if (q != null && q.colorValue != patient) { q.colorValue = patient; changed = true; }

            if (!changed) continue;
            Undo.RecordObject(a, "화살표 색 통일");
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(a);
            log.AppendLine($"  {Path(a.transform)}");
            n++;
        }

        if (n > 0)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        string msg = n == 0
            ? "이미 전부 통일돼 있습니다."
            : $"화살표 {n}개의 색을 맞췄습니다 (시술자 #26FF51 · 환자 #3FCCF2).\n\n{log}\n되돌리려면 Ctrl+Z.";
        Debug.Log("[힘의 방향 화살표 색 통일]\n" + msg);
        EditorUtility.DisplayDialog("힘의 방향 화살표 색 통일", msg, "확인");
    }

    /// <summary>화살표를 정반대 방향으로 돌린다(맞저항 표현용). 직선 화살표는 로컬 +Z가 미는 방향이다.</summary>
    private static void FlipDirection(ForceArrowBase arrow)
    {
        arrow.transform.localRotation *= Quaternion.Euler(0f, 180f, 0f);
    }

    // ── 비파괴 재사용 ────────────────────────────────────────────────────────
    // ★씬에서 손으로 맞춰 둔 위치·회전·색은 자산이다. 도구를 다시 돌려도 지우지 않는다.
    //   이름에 핵심어가 들어간 화살표가 이미 있으면 그걸 쓰고, 없는 것만 새로 만든다.

    private static int created;   // 이번 실행에서 실제로 새로 만든 화살표 수

    /// <summary>리그 안에서 이름에 <paramref name="key"/>가 들어간 화살표를 찾는다.</summary>
    private static ForceArrowBase Reuse(CranialAdjustmentController rig, string key)
    {
        foreach (ForceArrowBase a in rig.GetComponentsInChildren<ForceArrowBase>(true))
            if (a != null && a.name.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return a;
        return null;
    }

    /// <summary>새로 만든 화살표에 <b>같은 리그의 기존 색</b>을 물려준다 — 색을 다시 칠하지 않아도 되게.</summary>
    private static ForceArrowBase Adopt(CranialAdjustmentController rig, ForceArrowBase made)
    {
        created++;
        if (made == null) return null;

        foreach (ForceArrowBase a in rig.GetComponentsInChildren<ForceArrowBase>(true))
        {
            if (a == null || a == made) continue;
            var src = new SerializedObject(a);
            var dst = new SerializedObject(made);
            foreach (string f in new[] { "practitionerColor", "patientColor" })
            {
                SerializedProperty sp = src.FindProperty(f), dp = dst.FindProperty(f);
                if (sp != null && dp != null) dp.colorValue = sp.colorValue;
            }
            dst.ApplyModifiedProperties();
            break;   // 첫 번째 기존 화살표의 색이면 충분하다(리그 안에서는 같은 색을 쓴다)
        }
        return made;
    }

    /// <summary>기존 화살표를 그대로 복제한다 — 위치·회전·색·굵기를 전부 물려받는다.</summary>
    private static ForceArrowBase CloneArrow(ForceArrowBase src, string newName)
    {
        if (src == null) return null;
        var go = Object.Instantiate(src.gameObject, src.transform.parent);
        go.name = newName;
        go.transform.localPosition = src.transform.localPosition;
        go.transform.localRotation = src.transform.localRotation;
        go.transform.localScale = src.transform.localScale;
        Undo.RegisterCreatedObjectUndo(go, "맞저항 화살표 만들기");
        return go.GetComponent<ForceArrowBase>();
    }

    /// <summary>주체(시술자/환자)를 지정한다 — 색이 자동으로 갈린다.</summary>
    private static void SetActor(ForceArrowBase arrow, ForceArrowBase.Actor actor)
    {
        var so = new SerializedObject(arrow);
        SerializedProperty p = so.FindProperty("actor");
        if (p != null) { p.enumValueIndex = (int)actor; so.ApplyModifiedProperties(); }
    }

    private const string PmGroupName = "화살표 그룹 PM 견인";

    private static string ScenarioNameOf(CranialAdjustmentController rig)
    {
        var so = new SerializedObject(rig);
        SerializedProperty p = so.FindProperty("scenarioName");
        return p != null ? p.stringValue ?? "" : rig.gameObject.name;
    }

    private static Transform FirstGrip(CranialAdjustmentController rig, string arrayName)
    {
        var so = new SerializedObject(rig);
        SerializedProperty arr = so.FindProperty(arrayName);
        if (arr == null || !arr.isArray) return null;
        for (int i = 0; i < arr.arraySize; i++)
        {
            var g = arr.GetArrayElementAtIndex(i).objectReferenceValue as GripPointTarget;
            if (g != null) return g.transform;
        }
        return null;
    }

    /// <summary>도는 방향을 뒤집는다(X 스케일 -1) — 굴곡↔신전, 외회전↔내회전.</summary>
    private static void Flip(ForceArcArrow arc)
    {
        Vector3 s = arc.transform.localScale;
        s.x = -s.x;
        arc.transform.localScale = s;
    }

    private static void MakeGroup(Transform parent, string name, ForceArrowBase.ShowScope scope,
                                  string stepName, int subStepNo, ForceArrowBase[] arrows)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "화살표 그룹 만들기");
        go.transform.SetParent(parent, false);
        var grp = go.AddComponent<ForceArrowGroup>();

        var so = new SerializedObject(grp);
        so.FindProperty("showWhen").enumValueIndex = (int)scope;
        so.FindProperty("stepName").stringValue = stepName;
        so.FindProperty("subStepNo").intValue = subStepNo;
        SerializedProperty arr = so.FindProperty("arrows");
        arr.ClearArray();
        for (int i = 0; i < arrows.Length; i++)
        {
            arr.InsertArrayElementAtIndex(i);
            arr.GetArrayElementAtIndex(i).objectReferenceValue = arrows[i];
        }
        so.ApplyModifiedProperties();
    }

    [MenuItem("GuideChuna/힘의 방향 화살표 점검")]
    private static void Audit()
    {
        var groups = new List<ForceArrowGroup>();
        foreach (ForceArrowGroup g in Resources.FindObjectsOfTypeAll<ForceArrowGroup>())
            if (g != null && g.gameObject.scene.IsValid()) groups.Add(g);

        var arrows = new List<ForceArrowBase>();
        foreach (ForceArrowBase a in Resources.FindObjectsOfTypeAll<ForceArrowBase>())
            if (a != null && a.gameObject.scene.IsValid()) arrows.Add(a);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"화살표 {arrows.Count}개 / 그룹 {groups.Count}개\n");

        int problems = 0;

        sb.AppendLine("■ 그룹");
        if (groups.Count == 0) sb.AppendLine("   (없음 — 화살표에 직접 단계를 적었다면 그룹은 필요 없습니다)");
        foreach (ForceArrowGroup g in groups)
        {
            int n = g.Arrows != null ? g.Arrows.Length : 0;
            bool bad = n == 0;
            if (bad) problems++;
            sb.AppendLine($"   · {Path(g.transform)}  →  {g.DescribeMatch()}  화살표 {n}개" +
                          (bad ? "   ← ★비어 있음: 화살표를 자식으로 넣거나 Arrows 슬롯에 끌어다 놓으세요" : ""));
        }

        sb.AppendLine("\n■ 화살표");
        foreach (ForceArrowBase a in arrows)
        {
            ForceArrowGroup owner = a.GetComponentInParent<ForceArrowGroup>(true);
            string who;
            if (owner != null)
                who = $"그룹 '{owner.name}'이 관리 → {owner.DescribeMatch()}";
            else
                who = a.DescribeMatch();
            sb.AppendLine($"   · {Path(a.transform)}  [{a.GetType().Name}]  {who}");
        }

        if (Object.FindFirstObjectByType<ForceArrowDirector>(FindObjectsInactive.Include) == null)
        {
            sb.AppendLine("\n★씬에 ForceArrowDirector가 없습니다 — 빈 GameObject에 추가해야 단계별로 켜집니다.");
            problems++;
        }

        sb.AppendLine($"\n문제 {problems}건");
        sb.AppendLine("※ Step Name은 CSV의 stepName과 같아야 합니다(대소문자·공백 무시). 예: 파지 / 자세준비 / 호흡유도 / 교정");

        Debug.Log("[힘의 방향 화살표 점검]\n" + sb);
    }

    private static string Path(Transform t)
    {
        string p = t.name;
        while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }

    private static Material LoadOrCreateMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat != null) return mat;

        EnsureFolder("Assets/Materials");
        Shader shader = Shader.Find("Standard");
        mat = new Material(shader) { name = "ForceArrow" };
        // ForceArrow가 런타임에 Fade로 바꾸지만, 에디터에서도 반투명하게 보이도록 미리 맞춰 둔다.
        mat.SetFloat("_Mode", 3f);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.renderQueue = 3000;
        mat.color = new Color(1f, 0.55f, 0.15f, 0.85f);
        AssetDatabase.CreateAsset(mat, MaterialPath);
        AssetDatabase.SaveAssets();
        return mat;
    }

    /// <summary>흐름 화살표의 쐐기(>) 메시. 원뿔이라 정지 상태에서도 방향이 읽힌다. 모든 쐐기가 공유한다.</summary>
    private static Mesh LoadOrCreateChevronMesh()
    {
        const string path = "Assets/Meshes/ForceFlow_Chevron.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh != null) return mesh;

        EnsureFolder("Assets/Meshes");

        const int seg = 14;
        const float r = 0.15f, len = 0.2f;
        var v = new List<Vector3>();
        var tri = new List<int>();

        int ring = AddRing(v, seg, r, 0f);
        int apex = v.Count; v.Add(new Vector3(0f, 0f, len));
        int center = v.Count; v.Add(Vector3.zero);
        for (int i = 0; i < seg; i++)
        {
            int n = (i + 1) % seg;
            tri.Add(ring + i); tri.Add(apex); tri.Add(ring + n);      // 옆면
            tri.Add(center); tri.Add(ring + n); tri.Add(ring + i);    // 밑면
        }

        mesh = new Mesh { name = "ForceFlow_Chevron" };
        mesh.SetVertices(v);
        mesh.SetTriangles(tri, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path);
        AssetDatabase.SaveAssets();
        return mesh;
    }

    /// <summary>회전 화살표 조각 메시를 에셋으로 만들어 공유한다(모든 회전 화살표가 같은 기하).</summary>
    private static Mesh LoadOrCreateArcMesh(int index, bool isHead, float a0, float a1)
    {
        string path = isHead ? "Assets/Meshes/ForceArc_Head.asset"
                             : $"Assets/Meshes/ForceArc_Seg{index}.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh != null) return mesh;

        EnsureFolder("Assets/Meshes");
        mesh = isHead ? BuildArcHeadMesh(a0) : BuildArcSegmentMesh(a0, a1);
        mesh.name = isHead ? "ForceArc_Head" : $"ForceArc_Seg{index}";
        AssetDatabase.CreateAsset(mesh, path);
        AssetDatabase.SaveAssets();
        return mesh;
    }

    private static Mesh LoadOrCreateMesh()
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (mesh != null) return mesh;

        EnsureFolder("Assets/Meshes");
        mesh = BuildArrowMesh();
        AssetDatabase.CreateAsset(mesh, MeshPath);
        AssetDatabase.SaveAssets();
        return mesh;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }

    /// <summary>
    /// +Z를 향하는 화살표(자루 원기둥 + 머리 원뿔). 전체 길이 1 → 오브젝트 스케일로 크기를 준다.
    /// Unity 기본 프리미티브에 원뿔이 없어 직접 만든다.
    /// </summary>
    private static Mesh BuildArrowMesh()
    {
        const int seg = 16;
        const float shaftLen = 0.68f, shaftR = 0.085f;
        const float headR = 0.21f;

        var v = new List<Vector3>();
        var tri = new List<int>();

        // 각 링의 시작 인덱스를 기록해 가며 쌓는다.
        int shaftBack = AddRing(v, seg, shaftR, 0f);
        int shaftFront = AddRing(v, seg, shaftR, shaftLen);
        int headBase = AddRing(v, seg, headR, shaftLen);
        int apex = v.Count; v.Add(new Vector3(0f, 0f, 1f));
        int backCenter = v.Count; v.Add(Vector3.zero);
        int headBaseCenter = v.Count; v.Add(new Vector3(0f, 0f, shaftLen));

        for (int i = 0; i < seg; i++)
        {
            int n = (i + 1) % seg;

            // 자루 옆면
            tri.Add(shaftBack + i); tri.Add(shaftFront + i); tri.Add(shaftFront + n);
            tri.Add(shaftBack + i); tri.Add(shaftFront + n); tri.Add(shaftBack + n);

            // 자루 뒷면 뚜껑
            tri.Add(backCenter); tri.Add(shaftBack + n); tri.Add(shaftBack + i);

            // 머리 옆면(원뿔)
            tri.Add(headBase + i); tri.Add(apex); tri.Add(headBase + n);

            // 머리 밑면(도넛 대신 중심으로 채운다 — 자루보다 넓어 가려지지 않는다)
            tri.Add(headBaseCenter); tri.Add(headBase + i); tri.Add(headBase + n);
        }

        var mesh = new Mesh { name = "ForceArrow" };
        mesh.SetVertices(v);
        mesh.SetTriangles(tri, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ── 회전 화살표 기하 ───────────────────────────────────────────────
    // 회전축 = 로컬 +Y. 호는 로컬 +Z에서 시작해 +X 쪽으로 돈다.
    private static Vector3 ArcPoint(float deg) =>
        new Vector3(Mathf.Sin(deg * Mathf.Deg2Rad) * ArcRadius, 0f, Mathf.Cos(deg * Mathf.Deg2Rad) * ArcRadius);

    /// <summary>호의 접선(진행 방향).</summary>
    private static Vector3 ArcTangent(float deg) =>
        new Vector3(Mathf.Cos(deg * Mathf.Deg2Rad), 0f, -Mathf.Sin(deg * Mathf.Deg2Rad)).normalized;

    /// <summary>호 한 조각 = 튜브. a0~a1 구간을 단면 링으로 훑어 만든다.</summary>
    private static Mesh BuildArcSegmentMesh(float a0, float a1)
    {
        const int lengthSeg = 3;
        var v = new List<Vector3>();
        var tri = new List<int>();

        for (int s = 0; s <= lengthSeg; s++)
        {
            float a = Mathf.Lerp(a0, a1, s / (float)lengthSeg);
            Vector3 center = ArcPoint(a);
            Vector3 outward = center.normalized;          // 축에서 바깥으로
            Vector3 up = Vector3.up;                      // 회전축 방향
            for (int i = 0; i < ArcRadialSeg; i++)
            {
                float th = i / (float)ArcRadialSeg * Mathf.PI * 2f;
                v.Add(center + outward * (Mathf.Cos(th) * ArcTube) + up * (Mathf.Sin(th) * ArcTube));
            }
        }

        for (int s = 0; s < lengthSeg; s++)
        {
            int b0 = s * ArcRadialSeg, b1 = (s + 1) * ArcRadialSeg;
            for (int i = 0; i < ArcRadialSeg; i++)
            {
                int n = (i + 1) % ArcRadialSeg;
                tri.Add(b0 + i); tri.Add(b1 + i); tri.Add(b1 + n);
                tri.Add(b0 + i); tri.Add(b1 + n); tri.Add(b0 + n);
            }
        }

        var mesh = new Mesh();
        mesh.SetVertices(v);
        mesh.SetTriangles(tri, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>호 끝의 화살촉 = 접선 방향을 향한 원뿔.</summary>
    private static Mesh BuildArcHeadMesh(float a0)
    {
        const float headLen = 0.022f;
        const float headR = 0.014f;

        Vector3 baseCenter = ArcPoint(a0);
        Vector3 dir = ArcTangent(a0);
        Vector3 outward = baseCenter.normalized;
        Vector3 side = Vector3.Cross(dir, outward).normalized;

        var v = new List<Vector3>();
        var tri = new List<int>();

        for (int i = 0; i < ArcRadialSeg; i++)
        {
            float th = i / (float)ArcRadialSeg * Mathf.PI * 2f;
            v.Add(baseCenter + outward * (Mathf.Cos(th) * headR) + side * (Mathf.Sin(th) * headR));
        }
        int apex = v.Count; v.Add(baseCenter + dir * headLen);
        int center = v.Count; v.Add(baseCenter);

        for (int i = 0; i < ArcRadialSeg; i++)
        {
            int n = (i + 1) % ArcRadialSeg;
            tri.Add(i); tri.Add(apex); tri.Add(n);       // 옆면
            tri.Add(center); tri.Add(n); tri.Add(i);     // 밑면
        }

        var mesh = new Mesh();
        mesh.SetVertices(v);
        mesh.SetTriangles(tri, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static int AddRing(List<Vector3> v, int seg, float radius, float z)
    {
        int start = v.Count;
        for (int i = 0; i < seg; i++)
        {
            float a = i / (float)seg * Mathf.PI * 2f;
            v.Add(new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, z));
        }
        return start;
    }
}
