using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 시나리오 하나가 제대로 돌려면 <b>이름이 여섯 곳에서 전부 같아야 한다</b>.
/// 하나만 어긋나도 조용히 깨진다 — 나레이션이 무음이 되거나, 파지점 리그를 못 찾아 술기가 통째로 안 뜬다.
/// <code>
///   ① config의 scenarioName   ② config 에셋 파일명   ③ CSV 파일명
///   ④ CSV의 scenarioName 열   ⑤ 나레이션 폴더명      ⑥ 씬 파지점 리그의 scenarioName
/// </code>
/// 이 도구는 여섯 곳을 한 번에 대조하고, <see cref="ScenarioBootstrapper"/>의 깨진 참조까지 잡는다.
///
/// ★<b>읽기 전용</b> — 아무것도 고치지 않는다. 무엇이 어긋났는지만 보고한다.
///
/// 만든 이유: 2026-08 두 달 동안 같은 종류의 사고가 반복됐다.
///   · OM 개명 때 ④⑤⑥이 안 따라와 나레이션이 무음이 됐다
///   · 앙와위 굴곡 config를 지운 뒤 부트스트래퍼 idx 8이 깨진 참조로 남았다
///   · 대흉근·흉쇄유돌근은 나레이션 폴더가 아예 없다
/// </summary>
public static class ScenarioWiringAuditTool
{
    private const string ConfigFolder = "Assets/Resources/ScenarioConfigs";
    private const string CsvFolder = "Assets/Resources/Scenarios";
    private const string NarrationRoot = "Assets/Resources/Narrations";

    [MenuItem("GuideChuna/시나리오·로비/시나리오 배선 점검 (읽기 전용)")]
    private static void Audit()
    {
        var sb = new StringBuilder();
        int problems = 0;

        // ── 부트스트래퍼: idx → config ────────────────────────────────────────
        var boot = Object.FindFirstObjectByType<ScenarioBootstrapper>(FindObjectsInactive.Include);
        var bootIndex = new Dictionary<ScenarioConfig, int>();

        sb.AppendLine("■ ScenarioBootstrapper");
        if (boot == null)
        {
            sb.AppendLine("   ★씬에서 찾지 못했습니다 — 로비에서 시나리오를 못 띄웁니다.");
            problems++;
        }
        else
        {
            var so = new SerializedObject(boot);
            SerializedProperty arr = so.FindProperty("scenarioConfigs");
            for (int i = 0; i < arr.arraySize; i++)
            {
                var cfg = arr.GetArrayElementAtIndex(i).objectReferenceValue as ScenarioConfig;
                if (cfg == null)
                {
                    sb.AppendLine($"   ★idx {i}: 비었거나 깨진 참조 — 이 번호로 진입하면 아무것도 안 뜹니다.");
                    problems++;
                    continue;
                }
                if (!bootIndex.ContainsKey(cfg)) bootIndex[cfg] = i;
                sb.AppendLine($"   idx {i}: {cfg.scenarioName}");
            }
        }

        // ── 시나리오별 6곳 대조 ──────────────────────────────────────────────
        string[] guids = AssetDatabase.FindAssets("t:ScenarioConfig", new[] { ConfigFolder });
        var rigs = Object.FindObjectsByType<CranialAdjustmentController>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var cfg = AssetDatabase.LoadAssetAtPath<ScenarioConfig>(path);
            if (cfg == null) continue;

            string assetName = Path.GetFileNameWithoutExtension(path);
            string name = cfg.scenarioName;
            var issues = new List<string>();

            // ① vs ②
            if (string.IsNullOrWhiteSpace(name))
                issues.Add("★scenarioName이 비어 있음");
            else if (name != assetName)
                issues.Add($"★① scenarioName='{name}' ≠ ② 파일명='{assetName}'");

            // ③ CSV 파일
            string csv = $"{CsvFolder}/{name}.csv";
            bool csvExists = File.Exists(csv);
            if (!csvExists) issues.Add($"★③ CSV 없음: {csv}");

            // ④ CSV의 scenarioName 열
            if (csvExists)
            {
                string bad = FirstMismatchedScenarioColumn(csv, name);
                if (bad != null) issues.Add($"★④ CSV의 scenarioName 열이 '{bad}' (config는 '{name}')");
            }

            // ⑤ 나레이션 폴더 (narrationSubFolder가 비면 scenarioName 폴백)
            string folder = string.IsNullOrWhiteSpace(cfg.narrationSubFolder) ? name : cfg.narrationSubFolder;
            foreach (string level in new[] { "Beginner", "Intermediate" })
            {
                string dir = $"{NarrationRoot}/{level}/{folder}";
                if (!Directory.Exists(dir)) issues.Add($"★⑤ 나레이션 폴더 없음 → 무음: {dir}");
            }

            // ⑥ 씬 리그 (CSV가 cranial 조건을 쓸 때만 필요)
            if (csvExists && UsesCranialCondition(csv))
            {
                bool found = false;
                foreach (var rig in rigs)
                {
                    if (rig == null) continue;
                    var rso = new SerializedObject(rig);
                    if (rso.FindProperty("scenarioName")?.stringValue == name) { found = true; break; }
                }
                if (!found)
                    issues.Add($"★⑥ 씬에 scenarioName='{name}'인 파지점 리그가 없음 — 술기가 통째로 안 뜹니다");
            }

            // 부트스트래퍼 등록 여부
            if (boot != null && !bootIndex.ContainsKey(cfg))
                issues.Add("부트스트래퍼 배열에 없음 — 로비에서 진입할 수 없습니다");

            string idx = bootIndex.TryGetValue(cfg, out int n) ? $"idx {n}" : "미등록";
            sb.AppendLine($"\n■ {name}   ({idx})");
            if (issues.Count == 0)
            {
                sb.AppendLine("   ✓ 이름 6곳 일치");
            }
            else
            {
                problems += issues.Count;
                foreach (string s in issues) sb.AppendLine("   " + s);
            }
        }

        string head = problems == 0
            ? "시나리오 배선 점검 — 문제 없음\n\n"
            : $"시나리오 배선 점검 — ★문제 {problems}건\n\n";
        Debug.Log("[시나리오 배선 점검]\n" + head + sb);
        EditorUtility.DisplayDialog("시나리오 배선 점검",
            head + "자세한 내용은 Console을 보세요.", "확인");
    }

    /// <summary>CSV의 scenarioName 열이 기대값과 다른 첫 값을 돌려준다(전부 같으면 null).</summary>
    private static string FirstMismatchedScenarioColumn(string csvPath, string expected)
    {
        string[] lines = File.ReadAllLines(csvPath);
        if (lines.Length < 2) return null;

        int col = System.Array.IndexOf(SplitCsv(lines[0]), "scenarioName");
        if (col < 0) return null;

        for (int i = 1; i < lines.Length; i++)
        {
            string[] cells = SplitCsv(lines[i]);
            if (col >= cells.Length) continue;
            string v = cells[col].Trim();
            if (string.IsNullOrEmpty(v)) continue;      // 이어지는 줄(따옴표 안 줄바꿈)
            if (v != expected) return v;
        }
        return null;
    }

    private static bool UsesCranialCondition(string csvPath)
    {
        string text = File.ReadAllText(csvPath);
        return text.Contains("cranialTouch") || text.Contains("cranialGrip") ||
               text.Contains("cranialPressure") || text.Contains("cranialDepthBreath");
    }

    /// <summary>따옴표 안의 쉼표를 지키는 최소 CSV 분리.</summary>
    private static string[] SplitCsv(string line)
    {
        var cells = new List<string>();
        var cur = new StringBuilder();
        bool q = false;
        foreach (char c in line)
        {
            if (c == '"') { q = !q; continue; }
            if (c == ',' && !q) { cells.Add(cur.ToString()); cur.Clear(); continue; }
            cur.Append(c);
        }
        cells.Add(cur.ToString());
        return cells.ToArray();
    }
}
