using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 척추 쓸어내리기 구간(<see cref="SpineGlideGuide"/>)의 <b>시작점·끝점을 만들고 맞추는</b> 도구.
///
/// ★왜 도구인가(2026-08-18 사용자 요구 "흉추 시작점 끝점 설정할 수 있게"):
/// 두 점을 손으로 만들어 흉추 본 밑에 넣고 이름 붙이고 컴포넌트에 끌어다 넣는 작업을 매번 하면
/// 빠뜨리기 쉽다(단계 완료음이 fileID 0으로 방치돼 한 번도 안 울렸던 전례).
/// 여기서 한 번에 만들고, 만든 뒤에는 <b>씬 뷰에서 두 점을 그냥 끌어서</b> 미세 조정하면 된다.
///
/// ■ 하는 일 (비파괴)
///   · 리그에 <see cref="SpineGlideGuide"/>가 없으면 붙인다
///   · <c>흉추 시작점(두방)</c> / <c>흉추 끝점(족방)</c> 두 빈 오브젝트를 <b>흉추 본 하위</b>에 만든다
///     (환자가 움직여도 구간이 따라가야 하므로)
///   · 흉추 렌더러의 바운즈 <b>가장 긴 축</b>을 따라 양 끝에 초깃값으로 놓는다 —
///     머리 본에 가까운 쪽이 두방(시작점)이다
///   · LineRenderer를 만들어 구간을 보이게 한다
///
/// ★이미 만들어 둔 점이 있으면 <b>위치를 건드리지 않는다</b>(손으로 맞춘 값을 날리지 않는다).
/// 위치까지 다시 잡으려면 [초깃값으로 다시 놓기]를 쓴다. 전부 Undo 된다.
/// </summary>
public class SpineGlideSetupTool : EditorWindow
{
    private const string StartName = "흉추 시작점(두방)";
    private const string EndName = "흉추 끝점(족방)";
    private const string StartIndexName = "쓸기 자리_시작_검지";
    private const string StartMiddleName = "쓸기 자리_시작_중지";
    private const string EndIndexName = "쓸기 자리_끝_검지";
    private const string EndMiddleName = "쓸기 자리_끝_중지";
    private const string GripLeft = "Grip_횡돌기_왼손(족방수)";
    private const string GripRight = "Grip_횡돌기_오른손(두방수)";
    private const string PisiLeft = "두상골 자리(왼손)";
    private const string PisiRight = "두상골 자리(오른손)";

    private Vector2 scroll;
    private string report = "";
    private bool replacePositions;

    /// <summary>두 횡돌기 파지점 사이 간격(m). 두상골이 '거의 맞붙는' 폭이라 기본 4cm.</summary>
    private float gripSpread = 0.04f;

    [MenuItem("GuideChuna/두개골/척추 쓸어내리기 구간 만들기")]
    public static void Open()
    {
        var w = GetWindow<SpineGlideSetupTool>(true, "척추 쓸어내리기 구간");
        w.minSize = new Vector2(520, 420);
        w.Scan();
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "검지·중지로 척추를 두방(머리쪽)→족방(발쪽)으로 훑는 진단 단계의 구간을 만듭니다.\n" +
            "만든 뒤에는 씬 뷰에서 두 점을 직접 끌어서 맞추세요 — 파란 구가 시작(두방), 빨간 구가 끝(족방)입니다.",
            MessageType.Info);

        // ★대상 리그를 반드시 눈에 보이게 둔다 — 예전엔 자동 추정이 앙와위를 집어 가는데도
        //   화면에 아무 표시가 없어 엉뚱한 리그에 만들어졌다(2026-08-18).
        EditorGUILayout.LabelField("대상 리그", EditorStyles.boldLabel);
        targetRig = (CranialAdjustmentController)EditorGUILayout.ObjectField(
            new GUIContent("여기에 만듭니다", "비어 있으면 버튼이 동작하지 않습니다. 씬에서 리그를 끌어다 넣으세요."),
            targetRig, typeof(CranialAdjustmentController), true);

        if (targetRig == null)
        {
            EditorGUILayout.HelpBox(
                "대상 리그가 비어 있습니다. [다시 스캔]을 누르면 이름에 '복와위'가 있는 리그를 찾아 넣습니다.\n" +
                "★'흉추'로는 찾지 않습니다 — 앙와위_흉추_신전변위에도 그 글자가 있어 예전에 그쪽에 만들어진 적이 있습니다.",
                MessageType.Warning);
        }
        else
        {
            string who = string.IsNullOrWhiteSpace(targetRig.ScenarioName)
                ? targetRig.gameObject.name : targetRig.ScenarioName;
            bool prone = (targetRig.gameObject.name + targetRig.ScenarioName).Contains("복와위");
            EditorGUILayout.HelpBox(
                (prone ? "대상: " : "★복와위가 아닙니다. 정말 여기에 만들까요? → ") + who,
                prone ? MessageType.None : MessageType.Warning);
        }

        if (GUILayout.Button("다시 스캔")) Scan();

        EditorGUI.BeginDisabledGroup(targetRig == null);

        EditorGUILayout.Space();
        replacePositions = EditorGUILayout.ToggleLeft(
            new GUIContent("초깃값으로 다시 놓기 (이미 맞춰 둔 위치를 덮어씀)",
                           "끄면 이미 있는 점의 위치는 건드리지 않고 배선만 확인합니다."),
            replacePositions);

        GUI.backgroundColor = new Color(0.75f, 1f, 0.8f);
        if (GUILayout.Button("① 쓸어내리기 구간 만들기 · 배선하기", GUILayout.Height(28))) Build();
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("② 횡돌기 파지점 2개 (두상골)", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "복와위 하부흉추는 손을 포개는 게 아니라 각 두상골을 좌우 횡돌기에 걸고 " +
            "두 두상골이 거의 맞붙는 자세입니다(2026-08-18 사용자 정정).\n" +
            "지금 리그는 파지점 하나를 양손이 공유하고 있어 손별 판정이 안 됩니다 — " +
            "기존 파지점을 복제해 좌·우로 나눕니다.\n" +
            "★기존 파지점은 지우지 않습니다. 배열에서만 빠지므로 필요 없으면 직접 비활성화하세요.",
            EditorStyles.wordWrappedMiniLabel);
        gripSpread = EditorGUILayout.Slider("두 파지점 간격(m)", gripSpread, 0.01f, 0.12f);

        GUI.backgroundColor = new Color(0.8f, 0.9f, 1f);
        if (GUILayout.Button("횡돌기 파지점 만들기 · 좌우로 배선", GUILayout.Height(26))) BuildGripPair();
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("③ 두상골 자리 표시 (내 손 위)", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "손날 아래쪽 두상골이 실제 접촉점인데 그에 대응하는 손 관절이 없습니다 — " +
            "손목과 새끼 MCP 사이를 보간해 자리를 잡고, 시술자 손 트래킹 모델 위에 표시합니다.\n" +
            "파지 단계에서만 켜지고, 손이 안 잡히면 자동으로 숨습니다.\n" +
            "만든 뒤 인스펙터의 '손목→새끼 비율'·'오프셋'으로 자기 손에 맞게 미세 조정하세요.",
            EditorStyles.wordWrappedMiniLabel);

        GUI.backgroundColor = new Color(1f, 0.9f, 0.7f);
        if (GUILayout.Button("두상골 표시 만들기 · 배선", GUILayout.Height(26))) BuildPisiform();
        GUI.backgroundColor = Color.white;

        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space();
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.TextArea(report, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }

    /// <summary>씬의 리그별 구간 상태를 훑는다.</summary>
    private void Scan()
    {
        if (targetRig == null) targetRig = GuessProneRig();

        var sb = new StringBuilder();
        var rigs = Resources.FindObjectsOfTypeAll<CranialAdjustmentController>();
        int n = 0;

        sb.AppendLine(targetRig != null
            ? $"대상 리그 = {targetRig.gameObject.name}\n"
            : "★대상 리그 없음 — 이름에 '복와위'가 있는 리그를 못 찾았습니다. 창에서 직접 지정하세요.\n");

        foreach (var rig in rigs)
        {
            if (rig == null || EditorUtility.IsPersistent(rig)) continue;
            if (!rig.gameObject.scene.IsValid()) continue;
            n++;

            string who = string.IsNullOrWhiteSpace(rig.ScenarioName) ? rig.gameObject.name : rig.ScenarioName;
            var guide = rig.GetComponentInChildren<SpineGlideGuide>(true);
            sb.AppendLine($"■ {who}");
            sb.AppendLine(guide == null
                ? "   쓸어내리기 구간: 없음 (이 리그에서 cranialGlide 단계를 쓰면 즉시 통과됩니다)"
                : "   " + guide.DescribeSegment());

            var pisi = rig.GetComponentInChildren<PisiformHighlight>(true);
            sb.AppendLine(pisi == null ? "   두상골 표시: 없음" : "   " + pisi.Describe());
        }

        if (n == 0) sb.AppendLine("씬에서 리그(CranialAdjustmentController)를 찾지 못했습니다.");
        report = sb.ToString();
        Debug.Log("[척추 쓸어내리기 구간]\n" + report);
    }

    private void Build()
    {
        var rig = targetRig;
        if (rig == null)
        {
            report = "대상 리그가 비어 있습니다. 창 위쪽 '여기에 만듭니다'에 리그를 넣으세요.";
            return;
        }

        var guide = rig.GetComponentInChildren<SpineGlideGuide>(true);
        if (guide == null)
        {
            guide = Undo.AddComponent<SpineGlideGuide>(rig.gameObject);
            Debug.Log($"[척추 쓸어내리기] {rig.name}에 SpineGlideGuide를 붙였습니다.");
        }

        // ★부모는 <b>이 리그와 같은 환자</b>의 흉추 본이어야 한다.
        //   씬 전체에서 이름으로 찾으면 환자 모델이 둘일 때(c9 / c9 (1)) 엉뚱한 쪽에 만들어진다
        //   (2026-08-18 사용자 보고: "왜 c9말고 c9(1) 밑에다 쳐넣냐").
        Transform anchor = FindThoracicBoneFor(rig);
        if (anchor == null)
        {
            report = "이 리그가 속한 환자 모델에서 thoracic_spine을 찾지 못했습니다.\n" +
                     "리그가 환자 모델 하위에 있는지 확인하세요.";
            return;
        }

        // 구간 앵커 2개 — 판정은 손끝 위치로만 하므로 콜라이더가 필요 없다.
        Transform s = FindOrCreateSphere(anchor, StartName, 0.010f, new Color(0.3f, 0.6f, 1f));
        Transform e = FindOrCreateSphere(anchor, EndName, 0.010f, new Color(1f, 0.4f, 0.3f));

        if (replacePositions || Vector3.Distance(s.position, e.position) < 0.001f)
        {
            if (TryDefaultEnds(anchor, out Vector3 head, out Vector3 foot))
            {
                Undo.RecordObject(s, "구간 초기 배치");
                Undo.RecordObject(e, "구간 초기 배치");
                s.position = head;
                e.position = foot;
            }
        }

        // 검지·중지 자리 표시 4개 — 위치는 컴포넌트가 매 프레임 앵커 양옆으로 잡아 준다. 표시 전용.
        Transform sa = FindOrCreateSphere(anchor, StartIndexName, 0.014f, new Color(0.35f, 0.75f, 1f));
        Transform sb = FindOrCreateSphere(anchor, StartMiddleName, 0.014f, new Color(0.35f, 0.75f, 1f));
        Transform ea = FindOrCreateSphere(anchor, EndIndexName, 0.014f, new Color(0.35f, 0.75f, 1f));
        Transform eb = FindOrCreateSphere(anchor, EndMiddleName, 0.014f, new Color(0.35f, 0.75f, 1f));

        Undo.RecordObject(guide, "구간 배선");
        guide.SetSegment(s, e);
        guide.SetMarkers(sa, sb, ea, eb);
        EnsureLine(guide);
        EditorUtility.SetDirty(guide);

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Reveal(s);

        report = $"완료 — 리그 {rig.name}\n\n" +
                 $"만든 위치 (6개 모두 같은 부모):\n" +
                 $"   {PathOf(anchor)}\n" +
                 $"      ├ {StartName}   ← 구간 앵커(두방)\n" +
                 $"      ├ {EndName}     ← 구간 앵커(족방)\n" +
                 $"      ├ {StartIndexName} / {StartMiddleName}\n" +
                 $"      └ {EndIndexName} / {EndMiddleName}\n\n" +
                 $"판정: 손끝 위치만 사용(콜라이더 없음) — 시작 밴드에서 출발 → 진행도 누적 → 완주\n" +
                 $"   {guide.DescribeSegment()}\n\n" +
                 "★Hierarchy에서 방금 선택해 뒀습니다(핑). 씬 뷰에서 F를 누르면 그리로 갑니다.\n" +
                 "★앵커 2개만 실제 흉추 위·아래로 맞추면 됩니다. 자리 표시 4개는 자동으로 따라갑니다.\n" +
                 "★Ctrl+S로 씬을 저장해야 유지됩니다.\n\n" + report;
        Debug.Log("[척추 쓸어내리기] " + report);
        Scan();
    }

    /// <summary>
    /// 좌우 횡돌기 파지점 2개를 만들고 <c>leftGrips</c>/<c>rightGrips</c>에 하나씩 배선한다.
    ///
    /// ★기존 파지점을 <b>복제</b>한다 — 콜라이더 크기·손가락 지정·색·소리 같은 배선을 그대로 물려받는다.
    /// 맨손으로 만들면 그중 하나를 빠뜨려 판정이 조용히 죽는다(파지점 도구에서 반복해 겪은 함정).
    /// ★원본은 <b>지우지 않는다</b>. 배열에서만 빠지므로 필요 없으면 직접 비활성화하면 된다.
    /// </summary>
    private void BuildGripPair()
    {
        var rig = targetRig;
        if (rig == null) { report = "대상 리그가 비어 있습니다. 창 위쪽에서 지정하세요."; return; }

        var so = new SerializedObject(rig);
        var lp = so.FindProperty("leftGrips");
        var rp = so.FindProperty("rightGrips");
        if (lp == null || rp == null) { report = "leftGrips/rightGrips 필드를 찾지 못했습니다."; return; }

        GripPointTarget template = FirstGrip(lp) ?? FirstGrip(rp);
        if (template == null)
        {
            report = "복제할 기존 파지점이 없습니다. 리그에 GripPointTarget이 하나는 있어야 합니다.";
            return;
        }

        Transform parent = template.transform.parent != null ? template.transform.parent : rig.transform;

        // 척추 축과 수직인 방향으로 벌린다 — 좌우 횡돌기는 극돌기 양옆이다.
        Vector3 side = LateralDirection(rig);

        GripPointTarget left = CloneGrip(template, parent, GripLeft, -side * (gripSpread * 0.5f));
        GripPointTarget right = CloneGrip(template, parent, GripRight, side * (gripSpread * 0.5f));

        SetArray(lp, left);
        SetArray(rp, right);
        so.ApplyModifiedProperties();

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Reveal(left.transform);

        report = $"횡돌기 파지점 배선 완료 — 리그 {rig.name}\n\n" +
                 $"만든 위치:\n   {PathOf(parent)}\n\n" +
                 $"   leftGrips  ← {left.name}   (족방수 = 왼손, 먼저 건다)\n" +
                 $"   rightGrips ← {right.name}  (두방수 = 오른손, 나중에 건다)\n" +
                 $"   간격 {gripSpread * 100f:F0}cm · 원본 '{template.name}'은 그대로 뒀습니다(배열에서만 빠짐)\n\n" +
                 "씬 뷰에서 두 점을 실제 좌·우 횡돌기에 맞추세요.\n" +
                 "★CSV는 이미 파지 2-4=hand=left(족방수) / 2-5=양손 으로 갈라 뒀습니다.\n" +
                 "★Ctrl+S로 저장해야 유지됩니다.\n\n" + report;
        Debug.Log("[횡돌기 파지점] " + report);
        Scan();
    }

    /// <summary>
    /// 두상골 표시 2개를 만들어 <see cref="PisiformHighlight"/>에 배선한다.
    ///
    /// ★마커는 <b>리그 하위</b>에 만든다. 손 관절은 런타임 생성이라 에디터에서 부모로 지정할 수 없고,
    /// 런타임에 손 계층을 건드리면 손 모델 배선을 침범한다. 위치는 컴포넌트가 매 프레임 따라가게 한다.
    /// </summary>
    private void BuildPisiform()
    {
        var rig = targetRig;
        if (rig == null) { report = "대상 리그가 비어 있습니다. 창 위쪽에서 지정하세요."; return; }

        var hl = rig.GetComponentInChildren<PisiformHighlight>(true);
        if (hl == null) hl = Undo.AddComponent<PisiformHighlight>(rig.gameObject);

        // 납작한 구 = 손바닥에 찍힌 '자리'로 읽힌다. 콜라이더는 붙이지 않는다.
        Transform l = FindOrCreateSphere(rig.transform, PisiLeft, 0.018f, new Color(0.149f, 1f, 0.318f));
        Transform r = FindOrCreateSphere(rig.transform, PisiRight, 0.018f, new Color(0.149f, 1f, 0.318f));
        Flatten(l);
        Flatten(r);

        Undo.RecordObject(hl, "두상골 표시 배선");
        hl.SetMarkers(l, r);
        EditorUtility.SetDirty(hl);

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Reveal(l);
        report = $"두상골 표시 배선 완료 — 리그 {rig.name}\n\n" +
                 $"만든 위치:\n   {PathOf(rig.transform)}\n" +
                 $"      ├ {PisiLeft}\n" +
                 $"      └ {PisiRight}\n\n" +
                 $"   {hl.Describe()}\n\n" +
                 "★Play 중에만 손을 따라갑니다 — 에디트 모드에서는 리그 원점에 있습니다(정상).\n" +
                 "★자리가 어긋나면 인스펙터 '손목→새끼 비율'(기본 0.30)과 '오프셋'으로 맞추세요.\n" +
                 "★Ctrl+S로 저장해야 유지됩니다.\n\n" + report;
        Debug.Log("[두상골 표시] " + report);
        Scan();
    }

    /// <summary>구를 납작하게 눌러 '손바닥에 찍힌 자리'처럼 보이게 한다.</summary>
    private static void Flatten(Transform t)
    {
        if (t == null) return;
        Undo.RecordObject(t, "두상골 표시 모양");
        Vector3 s = t.localScale;
        t.localScale = new Vector3(s.x, s.y * 0.35f, s.z);
    }

    private static GripPointTarget FirstGrip(SerializedProperty arr)
    {
        for (int i = 0; i < arr.arraySize; i++)
        {
            var v = arr.GetArrayElementAtIndex(i).objectReferenceValue as GripPointTarget;
            if (v != null) return v;
        }
        return null;
    }

    private static void SetArray(SerializedProperty arr, GripPointTarget only)
    {
        arr.ClearArray();
        arr.InsertArrayElementAtIndex(0);
        arr.GetArrayElementAtIndex(0).objectReferenceValue = only;
    }

    /// <summary>이름이 같은 것이 이미 있으면 그것을 쓰고(위치 유지), 없으면 복제해 만든다.</summary>
    private static GripPointTarget CloneGrip(GripPointTarget template, Transform parent, string name, Vector3 offset)
    {
        foreach (Transform c in parent)
        {
            if (c.name != name) continue;
            var existing = c.GetComponent<GripPointTarget>();
            if (existing != null) return existing;      // 이미 만들어 둔 것 — 위치를 건드리지 않는다
        }

        var go = Object.Instantiate(template.gameObject, parent);
        go.name = name;
        Undo.RegisterCreatedObjectUndo(go, "횡돌기 파지점 생성");
        go.transform.position = template.transform.position + offset;
        go.transform.rotation = template.transform.rotation;
        go.SetActive(true);
        return go.GetComponent<GripPointTarget>();
    }

    /// <summary>척추 축과 수직인 좌우 방향. 쓸어내리기 접촉점이 있으면 그 축을 쓰고, 없으면 흉추 본의 오른쪽.</summary>
    private static Vector3 LateralDirection(CranialAdjustmentController rig)
    {
        var guide = rig.GetComponentInChildren<SpineGlideGuide>(true);
        if (guide != null && guide.HasSegment)
        {
            var so = new SerializedObject(guide);
            var s = so.FindProperty("startIndex").objectReferenceValue as GripPointTarget;
            var e = so.FindProperty("endIndex").objectReferenceValue as GripPointTarget;
            if (s != null && e != null)
            {
                Vector3 axis = (e.transform.position - s.transform.position).normalized;
                Vector3 side = Vector3.Cross(axis, Vector3.up);
                if (side.sqrMagnitude > 1e-4f) return side.normalized;
            }
        }

        Transform bone = FindThoracicBoneFor(rig);
        return bone != null ? bone.right : Vector3.right;
    }

    /// <summary>
    /// 작업 대상 리그. <b>창에 그대로 보이고, 비어 있으면 아무 버튼도 동작하지 않는다.</b>
    ///
    /// ★2026-08-18 사고: 예전엔 이름에 "복와위" <b>또는 "흉추"</b>가 들어가면 잡았는데,
    /// <c>CranialRig_앙와위_흉추_신전변위</c>에도 '흉추'가 들어 있고 수집 순서상 앙와위가 먼저 나와서
    /// <b>앙와위 리그에 만들어 버렸다</b>(사용자 보고). 게다가 무엇을 골랐는지 화면에 안 보여 눈치챌 수도 없었다.
    /// 그래서 이제 <b>자동 추정은 '복와위'가 이름에 있는 리그로만</b> 좁히고, 결과를 창에 띄운다.
    /// </summary>
    private CranialAdjustmentController targetRig;

    /// <summary>자동 추정 — '복와위'가 이름·시나리오명에 있는 리그만. 없으면 null(사용자가 직접 지정).</summary>
    private static CranialAdjustmentController GuessProneRig()
    {
        foreach (var rig in Resources.FindObjectsOfTypeAll<CranialAdjustmentController>())
        {
            if (rig == null || EditorUtility.IsPersistent(rig)) continue;
            if (!rig.gameObject.scene.IsValid()) continue;
            string n = rig.gameObject.name + " " + rig.ScenarioName;
            if (n.Contains("복와위")) return rig;
        }
        return null;
    }

    /// <summary>
    /// <b>이 리그와 같은 환자 모델</b>의 흉추 본을 찾는다.
    ///
    /// ★씬 전체 이름 검색을 쓰면 안 된다 — 환자 모델이 둘 이상이면(c9 / c9 (1)) 수집 순서대로
    /// 아무거나 잡혀 리그와 무관한 환자 밑에 만들어진다(2026-08-18 사고).
    /// 리그에서 <b>위로 올라가며</b> 각 조상의 하위에서 찾아, 처음 나오는 것을 쓴다 —
    /// 가장 가까운 공통 조상이 곧 같은 환자다.
    /// </summary>
    private static Transform FindThoracicBoneFor(CranialAdjustmentController rig)
    {
        if (rig == null) return null;
        for (Transform a = rig.transform; a != null; a = a.parent)
        {
            foreach (var t in a.GetComponentsInChildren<Transform>(true))
                if (t.name.IndexOf("thoracic_spine", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return t;
        }
        return null;
    }


    /// <summary>
    /// <b>눈에 보이는</b> 작은 구를 만든다 — 콜라이더는 붙이지 않는다(판정에 끼어들면 안 된다).
    ///
    /// ★빈 GameObject로 만들면 씬 뷰에서도 게임에서도 아무것도 안 보인다. 검지·중지 자리는
    /// 학습자가 봐야 하는 표시이므로 렌더러가 반드시 있어야 한다(2026-08-18).
    /// </summary>
    private static Transform FindOrCreateSphere(Transform parent, string name, float diameter, Color color)
    {
        foreach (Transform c in parent)
            if (c.name == name) return c;    // 이미 있으면 위치·모양을 건드리지 않는다

        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        Undo.RegisterCreatedObjectUndo(go, "자리 표시 생성");

        var col = go.GetComponent<Collider>();
        if (col != null) Object.DestroyImmediate(col);   // 파지 트리거와 섞이면 오판정

        Undo.SetTransformParent(go.transform, parent, "자리 표시 생성");
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one * diameter;

        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            var mat = new Material(Shader.Find("Standard")) { color = color };
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * 1.4f);
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }
        return go.transform;
    }

    /// <summary>Hierarchy에서 바로 찾아갈 수 있게 선택 + 핑.</summary>
    private static void Reveal(Transform t)
    {
        if (t == null) return;
        Selection.activeTransform = t;
        EditorGUIUtility.PingObject(t.gameObject);
    }

    /// <summary>루트부터의 전체 경로 — "어디에 생겼냐"를 보고서에 그대로 적기 위한 것.</summary>
    private static string PathOf(Transform t)
    {
        if (t == null) return "(없음)";
        string p = t.name;
        for (Transform c = t.parent; c != null; c = c.parent) p = c.name + " / " + p;
        return p;
    }

    /// <summary>
    /// 흉추 렌더러 바운즈의 <b>가장 긴 축</b> 양 끝을 초깃값으로 준다.
    /// 머리 본에 가까운 쪽이 두방(시작점)이다 — 복와위·앙와위 어느 자세든 방향이 뒤집히지 않는다.
    /// </summary>
    private static bool TryDefaultEnds(Transform anchor, out Vector3 headEnd, out Vector3 footEnd)
    {
        headEnd = footEnd = Vector3.zero;

        var r = anchor.GetComponentInChildren<Renderer>(true);
        if (r == null) r = anchor.GetComponentInParent<Renderer>();
        if (r == null) return false;

        Bounds b = r.bounds;
        Vector3 ext = b.extents;
        Vector3 axis = ext.x >= ext.y && ext.x >= ext.z ? Vector3.right
                     : ext.y >= ext.z ? Vector3.up : Vector3.forward;
        float half = Vector3.Scale(ext, axis).magnitude;

        Vector3 p1 = b.center + axis * half;
        Vector3 p2 = b.center - axis * half;

        // 머리에 가까운 끝이 두방이다.
        Transform head = null;
        foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (t == null || !t.gameObject.scene.IsValid()) continue;
            if (t.name.IndexOf("CC_Base_Head", System.StringComparison.OrdinalIgnoreCase) >= 0) { head = t; break; }
        }

        bool p1IsHead = head == null
            || Vector3.Distance(p1, head.position) <= Vector3.Distance(p2, head.position);
        headEnd = p1IsHead ? p1 : p2;
        footEnd = p1IsHead ? p2 : p1;
        return true;
    }

    /// <summary>구간을 보여 줄 LineRenderer를 확보한다(이미 있으면 그대로 둔다).</summary>
    private static void EnsureLine(SpineGlideGuide guide)
    {
        var line = guide.GetComponent<LineRenderer>();
        if (line == null) line = Undo.AddComponent<LineRenderer>(guide.gameObject);

        Undo.RecordObject(line, "구간 선 설정");
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.widthMultiplier = 0.006f;
        line.numCapVertices = 4;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        // ★불투명 Standard만 쓴다 — 커스텀 셰이더는 이 프로젝트 빌드에서 두 번 제거됐다.
        if (line.sharedMaterial == null)
            line.sharedMaterial = new Material(Shader.Find("Sprites/Default"));

        var so = new SerializedObject(guide);
        var p = so.FindProperty("pathVisual");
        if (p != null) { p.objectReferenceValue = line; so.ApplyModifiedProperties(); }
    }
}
