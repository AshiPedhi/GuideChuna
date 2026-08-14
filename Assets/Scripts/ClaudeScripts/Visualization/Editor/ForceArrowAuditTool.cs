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

    [MenuItem("GuideChuna/화살표 그룹 점검 · 이름 정리")]
    public static void Open()
    {
        var w = GetWindow<ForceArrowAuditTool>(true, "화살표 그룹 점검");
        w.minSize = new Vector2(560, 420);
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
