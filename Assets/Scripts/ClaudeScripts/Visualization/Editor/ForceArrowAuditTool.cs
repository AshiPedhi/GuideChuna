using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 힘의 방향 화살표 그룹 점검 · 이름 정리.
///
/// ★왜 필요한가(2026-08-13): 리그를 복제해 새 술기를 만들면 화살표 그룹도 딸려 오는데,
/// 이름이 <b>원본 술기 그대로</b>라 Hierarchy에서 어느 게 뭔지 구분이 안 된다
/// (제2늑골 리그에 `화살표 그룹 제1늑골 교정기법`이 4개 — 사용자: "세팅을 알아볼 수가 없네").
///
/// 이 도구는 <b>아무것도 지우지 않는다</b> — 목록을 읽고, 원하면 이름만 바꾼다(Undo 가능).
/// </summary>
public class ForceArrowAuditTool : EditorWindow
{
    private Vector2 scroll;
    private string report = "";

    private float presetSize = 1.6f;
    private float presetTravel = 0.6f;
    private float presetMinBright = 0.45f;

    /// <summary>통짜로 바꿀 때의 단면 모양. 박스형은 화살촉 두께가 자루와 같아 위아래에 단차가 없다.</summary>
    private bool boxedShape = true;

    [MenuItem("GuideChuna/화살표 그룹 점검 · 이름 정리")]
    public static void Open()
    {
        var w = GetWindow<ForceArrowAuditTool>(true, "화살표 그룹 점검");
        w.minSize = new Vector2(560, 760);   // 버튼이 창 밖으로 밀려 안 보이던 것을 막는다
        w.Scan();
    }

    private static List<ForceArrowGroup> FindAll()
    {
        var list = new List<ForceArrowGroup>();
        foreach (var g in Resources.FindObjectsOfTypeAll<ForceArrowGroup>())
        {
            if (g == null || EditorUtility.IsPersistent(g)) continue;
            if (!g.gameObject.scene.IsValid()) continue;
            list.Add(g);
        }
        return list;
    }

    private void Scan()
    {
        var sb = new StringBuilder();
        var groups = FindAll();
        sb.AppendLine($"화살표 그룹 {groups.Count}개\n");

        // 리그별로 묶어서 보여준다 — 어느 술기 것인지가 제일 중요하다.
        groups.Sort((a, b) => string.CompareOrdinal(RigOf(a), RigOf(b)));

        string lastRig = null;
        foreach (var g in groups)
        {
            string rig = RigOf(g);
            if (rig != lastRig)
            {
                sb.AppendLine($"\n■ {rig}");
                lastRig = rig;
            }

            sb.AppendLine($"   · {g.gameObject.name}");
            sb.AppendLine($"       {g.DescribeMatch()}");

            var arrows = g.Arrows;
            if (arrows == null || arrows.Length == 0)
            {
                sb.AppendLine("       화살표: ★없음");
            }
            else
            {
                foreach (var a in arrows)
                    sb.AppendLine($"       화살표: {(a != null ? a.gameObject.name : "★비어 있음")}");
            }
        }

        // 타겟 부위 하이라이트도 같은 Director가 켜고 끄므로 여기서 같이 보여 준다.
        var hls = new List<TargetAreaHighlight>();
        foreach (var h in Resources.FindObjectsOfTypeAll<TargetAreaHighlight>())
        {
            if (h == null || EditorUtility.IsPersistent(h)) continue;
            if (!h.gameObject.scene.IsValid()) continue;
            hls.Add(h);
        }
        sb.AppendLine($"\n\n■ 타겟 부위 하이라이트 {hls.Count}개");
        foreach (var h in hls)
            sb.AppendLine($"   · {h.gameObject.name}\n       {h.DescribeMatch()}");

        report = sb.ToString();
        Debug.Log("[화살표 그룹 점검]\n" + report);
    }

    /// <summary>이 그룹이 속한 리그(술기) 이름.</summary>
    private static string RigOf(ForceArrowGroup g)
    {
        var rig = g.GetComponentInParent<CranialAdjustmentController>(true);
        return rig != null
            ? $"{(string.IsNullOrWhiteSpace(rig.ScenarioName) ? rig.gameObject.name : rig.ScenarioName)}"
            : "(리그 밖)";
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "화살표 그룹이 어느 술기·어느 단계에 걸려 있는지 한 번에 봅니다.\n" +
            "★리그를 복제하면 화살표 그룹도 원본 이름 그대로 따라옵니다 — 아래 버튼으로 이름만 정리하세요.\n" +
            "이 도구는 오브젝트를 지우지 않습니다.",
            MessageType.Info);

        if (GUILayout.Button("다시 스캔")) Scan();

        GUI.backgroundColor = new Color(0.8f, 0.9f, 1f);
        if (GUILayout.Button("이름을 '화살표 그룹 <술기> <단계>.<subStep>' 형식으로 정리", GUILayout.Height(26)))
            RenameAll();
        GUI.backgroundColor = Color.white;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("제2늑골 자동 구성", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "리그를 복제하면서 따라온 화살표 그룹을 제2늑골 CSV에 맞게 재설정합니다.\n" +
            "  · 압박   → 교정·호흡 subStep 2 (족방)\n" +
            "  · 시연 올리기 → subStep 5 (두방)\n" +
            "  · 시연 내리기 → subStep 6 (족방)\n" +
            "방향은 제1늑골의 '족방 압박' 화살표 회전을 기준으로 잡고 두방은 180도 돌립니다. " +
            "남는 그룹은 지우지 않고 꺼 두기만 합니다.",
            EditorStyles.wordWrappedMiniLabel);
        if (GUILayout.Button("제2늑골 화살표 구성", GUILayout.Height(24)))
            ConfigureRib2();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("가시성 (2026-08-14 사용자 요구)", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "\"점선 이동 말고 전체 화살표가 크게 움직이면서 방향을 보여주는 게 좋겠다\" + " +
            "\"어느 면은 투명하게 보인다\"에 대한 일괄 적용입니다.\n" +
            "  · 표시 방식 → 통짜 왕복 (전체를 켜 두고 통째로 왕복)\n" +
            "  · 재질 → 불투명 (면이 서로 비쳐 보이는 현상·빌드 배리언트 문제 제거)\n" +
            "  · 왕복 폭·밝기 → 아래 값으로\n" +
            "★크기·표시방식은 신규 필드라 코드 기본값이 이미 먹지만, 왕복 폭·밝기는 씬에 저장돼 있어 " +
            "이 버튼으로만 바뀝니다.",
            EditorStyles.wordWrappedMiniLabel);

        presetSize = EditorGUILayout.Slider("크기 배율", presetSize, 0.5f, 3f);
        presetTravel = EditorGUILayout.Slider("왕복 폭(길이 대비)", presetTravel, 0f, 2f);
        presetMinBright = EditorGUILayout.Slider("최저 밝기", presetMinBright, 0f, 1f);

        GUI.backgroundColor = new Color(0.75f, 1f, 0.8f);
        if (GUILayout.Button("가시성 프리셋 일괄 적용 (직선 + 호 전부)", GUILayout.Height(26)))
            ApplyVisibilityPreset();
        GUI.backgroundColor = Color.white;

        if (GUILayout.Button("되돌리기 — 예전 방식(흐름 + 반투명)으로", GUILayout.Height(20)))
            ApplyLegacyPreset();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("★VR에서 안 보이는 문제 (2026-08-17 실측)", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "① 메시가 안팎이 뒤집혀 있습니다. 자루·화살촉의 옆면 노멀이 전부 축 안쪽을 향해 있어 " +
            "(실측: 표면 정점 (+0.085,0,0)의 노멀이 (-0.994,-0.066,-0.083)) Standard의 Cull Back에 " +
            "가까운 면이 잘려 나갑니다 → \"속이 빈 느낌\", \"보는 각도·위치에 따라 안 보임\". " +
            "불투명 전환으로는 안 고쳐집니다(컬링은 투명도와 무관).\n" +
            "② 화살표가 조각으로 만들어져 있습니다. 직선은 쐐기(>) 5조각, 호는 7조각 + 사이 18% 틈이라 " +
            "전부 켜도 점선처럼 읽힙니다 — 요구는 덩어리진 실선입니다.\n" +
            "아래 두 버튼을 위에서부터 순서대로 누르세요.",
            EditorStyles.wordWrappedMiniLabel);

        boxedShape = EditorGUILayout.Toggle(
            new GUIContent("박스형으로", "켬 = 사각기둥 자루 + 같은 두께의 납작 화살촉(위아래 단차 없음).\n" +
                                        "끔 = 원통 자루 + 원뿔 화살촉."), boxedShape);

        GUI.backgroundColor = new Color(1f, 0.85f, 0.6f);
        if (GUILayout.Button("① 화살표 메시 다시 굽기 (안팎 뒤집힘 수정)", GUILayout.Height(26)))
        {
            report = ForceArrowSetupTool.RebakeMeshAssets() + "\n\n" + report;
            Debug.Log("[화살표 메시] " + report);
        }
        if (GUILayout.Button($"② 통짜 실선으로 교체 — 직선 + 곡선 ({(boxedShape ? "박스형" : "원통형")})", GUILayout.Height(26)))
            ConvertToSolid(false);
        GUI.backgroundColor = Color.white;

        if (GUILayout.Button("②만 되돌리기 — 조각 화살표로", GUILayout.Height(20)))
            ConvertToSolid(true);

        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.TextArea(report, GUILayout.ExpandHeight(true));
        EditorGUILayout.EndScrollView();
    }

    /// <summary>제2늑골 리그의 화살표 그룹을 CSV 구조(압박 2 / 시연 5·6)에 맞춰 재설정한다.
    /// ★그룹을 지우지 않는다 — 남는 것은 비활성화만 하고 목록에 남긴다.</summary>
    private void ConfigureRib2()
    {
        const string Rib2 = "제2늑골_상방변위";

        var mine = new List<ForceArrowGroup>();
        foreach (var g in FindAll())
            if (RigOf(g) == Rib2) mine.Add(g);

        if (mine.Count == 0)
        {
            report = $"'{Rib2}' 리그에서 화살표 그룹을 찾지 못했습니다.\n" +
                     "리그가 만들어져 있는지, 화살표 그룹이 그 하위에 있는지 확인하세요.";
            return;
        }

        // ★사용자가 이미 만들어 둔 화살표를 건드리지 않는다 — <b>위치·회전은 손대지 않고</b>
        //   이름으로 용도를 알아내 '언제 뜨는지'(단계·subStep)만 맞춘다.
        //   두방 → 시연 올리기(5) / 족방 → 시연 내리기(6). 그 외는 보고만 하고 그대로 둔다.
        var log = new StringBuilder();
        int fixedCount = 0;

        foreach (var g in mine)
        {
            string tag = NameOfArrows(g) + " " + g.gameObject.name;
            bool cephalad = tag.Contains("두방");
            bool caudad = tag.Contains("족방");

            if (!cephalad && !caudad)
            {
                log.AppendLine($"  · {g.gameObject.name}  ← 두방/족방을 이름에서 못 찾아 <b>그대로 뒀습니다</b>");
                continue;
            }

            int sub = cephalad ? 5 : 6;

            Undo.RecordObject(g, "제2늑골 화살표 구성");
            var so = new SerializedObject(g);
            so.FindProperty("stepName").stringValue = "교정·호흡";
            so.FindProperty("subStepNo").intValue = sub;
            var listProp = so.FindProperty("subStepNos");
            if (listProp != null) listProp.stringValue = "";
            so.FindProperty("showWhen").enumValueIndex = (int)ForceArrowBase.ShowScope.특정_단계만;
            so.ApplyModifiedProperties();

            log.AppendLine($"  · {g.gameObject.name}  ← 교정·호흡 subStep {sub} ({(cephalad ? "두방=올리기" : "족방=내리기")})");
            fixedCount++;
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        report = $"제2늑골 화살표 구성 — {fixedCount}개 재설정 (위치·회전은 건드리지 않았습니다)\n" + log +
                 "\n두방=시연 올리기(subStep 5) / 족방=시연 내리기(subStep 6)로 잡았습니다.\n" +
                 "늑골 압박용 화살표를 따로 두시려면 그 그룹만 subStep 2로 바꾸세요.\n\n" + report;
        Debug.Log("[화살표 그룹] " + report);
        Scan();
    }

    /// <summary>씬의 모든 화살표(직선·호)를 비활성 포함 수집한다.</summary>
    private static List<ForceArrowBase> FindAllArrows()
    {
        var list = new List<ForceArrowBase>();
        foreach (var a in Resources.FindObjectsOfTypeAll<ForceArrowBase>())
        {
            if (a == null || EditorUtility.IsPersistent(a)) continue;
            if (!a.gameObject.scene.IsValid()) continue;
            list.Add(a);
        }
        return list;
    }

    /// <summary>가시성 프리셋 — 통짜 왕복 + 불투명 + 큰 왕복 폭.</summary>
    private void ApplyVisibilityPreset() => ApplyPreset(false);

    /// <summary>예전 방식으로 되돌린다(흐름 + 반투명). 마음에 안 들 때의 탈출구.</summary>
    private void ApplyLegacyPreset() => ApplyPreset(true);

    private void ApplyPreset(bool legacy)
    {
        var arrows = FindAllArrows();
        int n = 0;

        foreach (var a in arrows)
        {
            Undo.RecordObject(a, "화살표 가시성 프리셋");
            var so = new SerializedObject(a);

            Set(so, "useTransparency", p => p.boolValue = legacy);
            Set(so, "sizeMultiplier", p => p.floatValue = legacy ? 1f : presetSize);

            // 직선 화살표만 갖는 값들 — 호(ForceArcArrow)에는 없다.
            Set(so, "displayMode", p => p.enumValueIndex = legacy
                ? (int)ForceArrow.DisplayMode.자동
                : (int)ForceArrow.DisplayMode.통짜왕복);
            Set(so, "travelPulse", p => p.floatValue = legacy ? 0.35f : presetTravel);
            Set(so, "pulsePerSecond", p => p.floatValue = legacy ? 0.8f : 0.9f);

            Set(so, "minAlpha", p => p.floatValue = legacy ? 0.25f : presetMinBright);
            Set(so, "maxAlpha", p => p.floatValue = legacy ? 0.95f : 1f);

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(a);
            n++;
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        report = legacy
            ? $"예전 방식으로 되돌렸습니다 — 화살표 {n}개 (흐름 + 반투명 + 원래 크기)\n\n" + report
            : $"가시성 프리셋 적용 — 화살표 {n}개\n" +
              $"  통짜 왕복 / 불투명 / 크기 ×{presetSize:0.0} / 왕복 폭 {presetTravel:0.00} / 최저 밝기 {presetMinBright:0.00}\n" +
              "  ★씬을 저장해야 유지됩니다.\n\n" + report;
        Debug.Log("[화살표 가시성] " + report);
        Scan();
    }

    /// <summary>
    /// 조각으로 만들어진 화살표를 <b>통짜 실선 하나</b>로 바꾼다.
    /// 직선(쐐기 5조각)과 회전(호 7조각 + 러너) <b>둘 다</b> 대상이다.
    ///
    /// ★비파괴 — 조각 자식을 <b>지우지 않고 비활성화만</b> 한다(07-30 파지점 도구가 사용자 수작업을
    /// 날린 전례). 되돌리기가 그대로 되살린다. 전부 Undo도 된다.
    /// ★트랜스폼(위치·회전·크기)은 손대지 않는다 — 사용자가 손으로 겨눠 둔 방향이다.
    ///   다만 직선은 통짜 메시가 길이 1.0, 쐐기 5개 사슬은 약 1.19라 <b>약 16% 짧아진다.</b>
    ///   길이를 맞추려면 루트의 Scale Z만 1.19배 하면 된다. (호는 각도가 같아 차이 없다.)
    /// </summary>
    private void ConvertToSolid(bool revert)
    {
        report = DoConvert(revert, boxedShape) + "\n\n" + report;
        Scan();
    }

    [MenuItem("GuideChuna/화살표 ② 통짜 실선으로 교체 — 박스형 (권장)")]
    private static void ConvertBoxMenu()
    {
        string r = DoConvert(false, true);
        Debug.Log("[화살표] " + r);
        EditorUtility.DisplayDialog("② 통짜 실선(박스형)으로 교체", r, "확인");
    }

    [MenuItem("GuideChuna/화살표 ② 통짜 실선으로 교체 — 원통형")]
    private static void ConvertRoundMenu()
    {
        string r = DoConvert(false, false);
        Debug.Log("[화살표] " + r);
        EditorUtility.DisplayDialog("② 통짜 실선(원통형)으로 교체", r, "확인");
    }

    [MenuItem("GuideChuna/화살표 ②를 되돌리기 (조각으로)")]
    private static void RevertMenu()
    {
        string r = DoConvert(true, false);
        Debug.Log("[화살표] " + r);
        EditorUtility.DisplayDialog("조각으로 되돌리기", r, "확인");
    }

    private static string DoConvert(bool revert, bool boxed)
    {
        Material mat = ForceArrowSetupTool.ArrowMaterial();

        var log = new StringBuilder();
        int changed = 0, skipped = 0;

        foreach (var a in FindAllArrows())
        {
            if (a == null) continue;

            bool isArc = a is ForceArcArrow;
            Mesh solid = isArc
                ? ForceArrowSetupTool.SolidArcMesh(boxed)
                : (boxed ? ForceArrowSetupTool.BoxArrowMesh() : ForceArrowSetupTool.SolidArrowMesh());

            GameObject go = a.gameObject;
            var chevrons = new List<Renderer>();
            foreach (Transform c in go.transform)
            {
                var r = c.GetComponent<Renderer>();
                if (r != null) chevrons.Add(r);
            }

            if (chevrons.Count == 0)
            {
                log.AppendLine($"  · {go.name} — 자식 조각이 없어 건너뜀(이미 통짜)");
                skipped++;
                continue;
            }

            var mf = go.GetComponent<MeshFilter>();
            var mr = go.GetComponent<MeshRenderer>();

            if (revert)
            {
                foreach (var r in chevrons)
                {
                    Undo.RecordObject(r.gameObject, "조각 화살표로 되돌리기");
                    r.gameObject.SetActive(true);
                }
                if (mr != null) Undo.DestroyObjectImmediate(mr);
                if (mf != null) Undo.DestroyObjectImmediate(mf);
                // ★호는 비워 둔다 — 자동 수집이 러너를 목록에서 빼 주기 때문(직접 채우면 러너가 섞인다).
                SetSegments(a, isArc ? new List<Renderer>() : chevrons);
                SetEnum(a, "displayMode", (int)ForceArrow.DisplayMode.자동);
                log.AppendLine($"  · {go.name} — 조각 {chevrons.Count}개 복구");
            }
            else
            {
                if (mf == null) mf = Undo.AddComponent<MeshFilter>(go);
                if (mr == null) mr = Undo.AddComponent<MeshRenderer>(go);

                Undo.RecordObject(mf, "통짜 실선으로 교체");
                mf.sharedMesh = solid;
                Undo.RecordObject(mr, "통짜 실선으로 교체");
                mr.sharedMaterial = mat;
                mr.enabled = true;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;

                foreach (var r in chevrons)
                {
                    Undo.RecordObject(r.gameObject, "통짜 실선으로 교체");
                    r.gameObject.SetActive(false);
                }

                // ★segments를 루트 렌더러 하나로 못 박는다. 비워 두면 Awake의 자동 수집이
                //   비활성 자식(GetComponentsInChildren(true))까지 긁어와 조각이 목록에 남는다.
                SetSegments(a, new List<Renderer> { mr });
                SetEnum(a, "displayMode", (int)ForceArrow.DisplayMode.통짜왕복);
                log.AppendLine($"  · {go.name} — {(isArc ? "통짜 곡선" : "통짜 직선")}({(boxed ? "박스형" : "원통형")})으로 교체 " +
                               $"(조각 {chevrons.Count}개는 끄기만 함)");
            }

            EditorUtility.SetDirty(a);
            changed++;
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        return (revert
                   ? $"조각 화살표로 되돌렸습니다 — {changed}개"
                   : $"통짜 실선({(boxed ? "박스형" : "원통형")})으로 교체 — {changed}개 (건너뜀 {skipped}개)") +
               "\n" + log +
               "\n★Ctrl+S로 씬을 저장해야 유지됩니다. 트랜스폼은 건드리지 않았습니다.\n" +
               (revert ? "" : "★직선은 통짜 메시가 조각 사슬보다 약 16% 짧습니다 — 맞추려면 루트 Scale Z만 1.19배 하세요.\n");
    }

    // ── 진단 ───────────────────────────────────────────────────────────
    //
    // ★"눌렀는데 된 건지 모르겠다"를 없애기 위한 것. 눈으로 판단하지 말고 이걸 돌린다.

    [MenuItem("GuideChuna/화살표 상태 진단 (지금 어떤 상태인지)")]
    private static void DiagnoseMenu()
    {
        string r = Diagnose();
        Debug.Log("[화살표 진단]\n" + r);
        EditorUtility.DisplayDialog("화살표 상태 진단", r, "확인");
    }

    private static string Diagnose()
    {
        var sb = new StringBuilder();

        sb.AppendLine("■ ① 메시 안팎 — 옆면이 바깥을 향해야 정상");
        sb.AppendLine(CheckMeshZ("Assets/Meshes/ForceArrow.asset", "통짜 화살표"));
        sb.AppendLine(CheckMeshZ("Assets/Meshes/ForceFlow_Chevron.asset", "쐐기"));

        sb.AppendLine();
        sb.AppendLine("■ 공유 머티리얼");
        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/ForceArrow.mat");
        if (mat == null) sb.AppendLine("   ★ForceArrow.mat 없음");
        else if (!mat.HasProperty("_ZWrite")) sb.AppendLine("   Standard 계열이 아님 — 판단 보류");
        else sb.AppendLine(mat.GetInt("_ZWrite") == 1
                ? "   불투명(ZWrite 1) ✓"
                : "   ★반투명(ZWrite 0) — ①을 아직 안 돌렸습니다");

        sb.AppendLine();
        sb.AppendLine("■ ② 씬의 화살표 (직선 + 곡선)");
        int solidCnt = 0, piecedCnt = 0;
        foreach (var a in FindAllArrows())
        {
            if (a == null) continue;

            var mf = a.GetComponent<MeshFilter>();
            bool rootSolid = mf != null && mf.sharedMesh != null;

            int liveChildren = 0;
            foreach (Transform c in a.transform)
                if (c.gameObject.activeSelf && c.GetComponent<Renderer>() != null) liveChildren++;

            string kind = a is ForceArcArrow ? "곡선" : "직선";
            if (rootSolid && liveChildren == 0)
            {
                solidCnt++;
                sb.AppendLine($"   {kind} {a.gameObject.name} — 통짜 [{mf.sharedMesh.name}] ✓");
            }
            else
            {
                piecedCnt++;
                sb.AppendLine($"   ★{kind} {a.gameObject.name} — 아직 조각 {liveChildren}개");
            }
        }
        sb.AppendLine($"   → 통짜 {solidCnt}개 / 아직 조각인 것 {piecedCnt}개");

        sb.AppendLine();
        sb.AppendLine(piecedCnt == 0 && solidCnt > 0
            ? "→ ②는 적용됐습니다."
            : "→ ★②를 아직 안 돌렸습니다. 메뉴 `GuideChuna/화살표 ② 통짜 실선으로 교체 — 박스형 (권장)`");

        return sb.ToString();
    }

    /// <summary>
    /// 로컬 +Z를 축으로 하는 메시의 <b>옆면 노멀</b>이 축 바깥을 향하는지 센다.
    /// 뚜껑 노멀은 축과 나란해서 판정에서 자동으로 빠진다(내적 ≈ 0).
    /// </summary>
    private static string CheckMeshZ(string path, string label)
    {
        var m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (m == null) return $"   ★{label} — 에셋이 없습니다 ({path})";

        Vector3[] v = m.vertices;
        Vector3[] nr = m.normals;
        if (nr == null || nr.Length != v.Length) return $"   ★{label} — 노멀이 없습니다";

        int outward = 0, inward = 0;
        for (int i = 0; i < v.Length; i++)
        {
            var radial = new Vector2(v[i].x, v[i].y);
            if (radial.sqrMagnitude < 1e-8f) continue;      // 축 위 정점(꼭짓점·중심)
            radial.Normalize();
            float d = nr[i].x * radial.x + nr[i].y * radial.y;
            if (d > 0.1f) outward++;
            else if (d < -0.1f) inward++;
        }

        return inward == 0 && outward > 0
            ? $"   {label} — 옆면 {outward}개 전부 바깥 ✓"
            : $"   ★{label} — 안쪽을 향한 옆면 {inward}개 (바깥 {outward}개). ①을 아직 안 돌렸습니다";
    }

    private static void SetSegments(Component arrow, List<Renderer> renderers)
    {
        var so = new SerializedObject(arrow);
        SerializedProperty p = so.FindProperty("segments");
        p.ClearArray();
        for (int i = 0; i < renderers.Count; i++)
        {
            p.InsertArrayElementAtIndex(i);
            p.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
        }
        so.ApplyModifiedProperties();
    }

    private static void SetEnum(Object target, string field, int value)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        if (p == null) return;
        p.enumValueIndex = value;
        so.ApplyModifiedProperties();
    }

    /// <summary>그 컴포넌트에 없는 필드는 조용히 건너뛴다(직선/호가 필드 구성이 다르다).</summary>
    private static void Set(SerializedObject so, string field, System.Action<SerializedProperty> apply)
    {
        var p = so.FindProperty(field);
        if (p != null) apply(p);
    }

    /// <summary>그룹에 든 화살표 이름을 모두 이어 붙인다(용도 판별용).</summary>
    private static string NameOfArrows(ForceArrowGroup g)
    {
        var sb = new StringBuilder();
        foreach (var a in g.Arrows)
            if (a != null) sb.Append(a.gameObject.name).Append(' ');
        return sb.ToString();
    }

    private void RenameAll()
    {
        int n = 0;
        foreach (var g in FindAll())
        {
            var so = new SerializedObject(g);
            string step = so.FindProperty("stepName").stringValue;
            int sub = so.FindProperty("subStepNo").intValue;
            string subs = so.FindProperty("subStepNos") != null ? so.FindProperty("subStepNos").stringValue : "";

            string tail = !string.IsNullOrWhiteSpace(subs) ? subs
                        : sub > 0 ? sub.ToString()
                        : "전체";
            string want = $"화살표 그룹 {RigOf(g)} {(string.IsNullOrWhiteSpace(step) ? "전단계" : step)}.{tail}";

            if (g.gameObject.name == want) continue;
            Undo.RecordObject(g.gameObject, "화살표 그룹 이름 정리");
            g.gameObject.name = want;
            EditorUtility.SetDirty(g.gameObject);
            n++;
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Scan();
        report = $"이름 {n}개 정리 완료.\n\n" + report;
    }
}
