using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 골격 부위 조사 + 시나리오별 "필요 골격만 표시" 초기 설정.
/// ★비파괴 — 오브젝트를 지우거나 재생성하지 않고, 이미 있는 항목은 건너뛴다.
/// </summary>
public static class SkeletonFocusSetupTool
{
    /// <summary>시나리오·국면·단계별 남길 부위 기본값.
    ///
    /// ★부위 이름은 <b>부분 일치</b>다 — 분리된 두개골 이름이 '좌측관자뼈(측두골)_temporal bone'처럼
    /// 한글+영문 혼합이라 'temporal' 한 줄이면 좌·우가 같이 남는다.
    ///
    /// ★두개골 3종의 국면별 구성(08-11 사용자 요구 "진단·교정·재평가에서 보이는 골격이 달라야 한다"):
    ///   · 진단(평가)  — 촉진하는 뼈만: 후두골 + 경추
    ///   · 교정        — 실제로 잡는 뼈: 후두골(보조수) + 측두골·관골(주동수) + 경추
    ///   · 재평가      — 진단과 동일
    /// 실제로 무엇을 보여줄지는 실습 기준이라 여기 기본값을 그대로 쓰지 말고 인스펙터에서 조정할 것.
    /// </summary>
    private static readonly (string scenario, string phase, string step, string[] keep)[] Defaults =
    {
        // ── 두개골 OM : 보조수(왼손)=후두골 / 주동수(오른손)=관골궁·유양돌기(측두골) ──
        ("두개골OM교정", "평가",   "",           new[] { "occipital", "cervical" }),                              // 진단용 파지 부위
        ("두개골OM교정", "교정",   "",           new[] { "occipital", "temporal", "zygomatic", "cervical" }),     // 교정용 파지 부위
        ("두개골OM교정", "재평가", "",           new[] { "occipital", "cervical" }),                              // 진단과 동일

        // ── 두개골 PM : 양손 유양돌기(측두골)+후두골, 족방→두방 견인 ──
        ("두개골PM교정", "평가",   "",           new[] { "occipital", "cervical" }),
        ("두개골PM교정", "교정",   "",           new[] { "occipital", "temporal", "parietal", "cervical" }),
        ("두개골PM교정", "재평가", "",           new[] { "occipital", "cervical" }),

        // ── 두개골 PJ : 보조수=후두골(굴곡·신전) / 주동수=관골궁·유양돌기(외·내회전) ──
        ("두개골PJ교정", "평가",   "",           new[] { "occipital", "cervical" }),
        ("두개골PJ교정", "교정",   "",           new[] { "occipital", "temporal", "zygomatic", "cervical" }),
        ("두개골PJ교정", "재평가", "",           new[] { "occipital", "cervical" }),
        // ── 흉추·늑골 ──
        ("앙와위_흉추_신전변위",     "", "", new[] { "thoracic", "thorax" }),
        ("복와위_하부흉추_굴곡변위", "", "", new[] { "thoracic" }),
        ("제1늑골_앙와위",           "", "", new[] { "thorax", "clavicle", "sternum", "cervical" }),
        ("제2늑골_상방변위",         "", "", new[] { "thorax", "clavicle", "sternum" }),
    };

    /// <summary>줄을 만들지 않을 시나리오 — 단순추나·ROM은 골격 표시를 쓰지 않는다(08-11 사용자 지시).
    /// 목록에 없으면 골격이 전부 보이는 기존 동작 그대로다.</summary>
    private static readonly string[] SkipScenarios =
    {
        "상부승모근", "견갑거근", "사각근", "대흉근", "흉쇄유돌근", "경추ROM측정",
    };

    /// <summary>국면(진단·교정·재평가)마다 보이는 뼈가 다른 시나리오 — 이 시나리오만 국면별로 줄을 나눈다.
    /// ★흉추·늑골은 모든 과정에서 같은 뼈만 보이므로 시나리오당 1줄이면 된다(08-11 사용자).</summary>
    private static readonly string[] PhaseSplitScenarios =
    {
        "두개골OM교정", "두개골PM교정", "두개골PJ교정",
    };

    /// <summary>
    /// ★CSV를 읽어 <b>시나리오 × 국면 × 단계</b> 줄을 전부 만들어 둔다(뼈는 비워 둔 채).
    /// 사용자는 각 줄의 Show Bones에 뼈만 드래그하면 된다.
    ///
    /// ★비어 있는 줄 = 그 단계에서 <b>골격을 전부 숨김</b>(사용자 지시 08-11).
    /// ★이미 뼈를 배정해 둔 줄은 <b>그대로 보존</b>한다 — 다시 실행해도 작업이 날아가지 않는다.
    /// 시작·종료 안내(stepNo 0)는 만들지 않는다.
    /// </summary>
    [MenuItem("GuideChuna/골격 포커스 — 시나리오·국면 줄 만들기 (CSV 기준)")]
    private static void BuildEntriesFromCsv()
    {
        var focus = Object.FindFirstObjectByType<SkeletonFocusController>(FindObjectsInactive.Include);
        if (focus == null)
        {
            EditorUtility.DisplayDialog("골격 포커스",
                "씬에 SkeletonFocusController가 없습니다.\n\n빈 GameObject에 컴포넌트를 추가한 뒤 다시 실행하세요.", "확인");
            return;
        }

        // 시나리오 목록 = ScenarioConfig 에셋들(로비에 실제로 등록되는 것과 같은 이름)
        var scenarioNames = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:ScenarioConfig"))
        {
            var cfg = AssetDatabase.LoadAssetAtPath<ScenarioConfig>(AssetDatabase.GUIDToAssetPath(guid));
            if (cfg == null || string.IsNullOrWhiteSpace(cfg.scenarioName)) continue;
            if (System.Array.Exists(SkipScenarios,
                    s => string.Equals(s, cfg.scenarioName, System.StringComparison.OrdinalIgnoreCase))) continue;
            if (!scenarioNames.Contains(cfg.scenarioName)) scenarioNames.Add(cfg.scenarioName);
        }
        scenarioNames.Sort(System.StringComparer.Ordinal);

        // CSV 파싱은 런타임 로더를 그대로 빌려 쓴다(따옴표·줄바꿈·인코딩 처리가 이미 되어 있음).
        var probe = new GameObject("~CSVProbe") { hideFlags = HideFlags.HideAndDontSave };
        var loader = probe.AddComponent<ScenarioCSVLoader>();

        var so = new SerializedObject(focus);
        SerializedProperty entries = so.FindProperty("entries");

        // 기존 줄의 뼈 배정을 키로 기억해 둔다(다시 실행해도 보존).
        var keptBones = new Dictionary<string, List<Object>>();
        for (int i = 0; i < entries.arraySize; i++)
        {
            SerializedProperty e = entries.GetArrayElementAtIndex(i);
            SerializedProperty bones = e.FindPropertyRelative("showBones");
            var list = new List<Object>();
            for (int b = 0; b < bones.arraySize; b++) list.Add(bones.GetArrayElementAtIndex(b).objectReferenceValue);
            if (list.Count > 0)
                keptBones[Key(e.FindPropertyRelative("scenarioName").stringValue,
                              e.FindPropertyRelative("phaseName").stringValue,
                              e.FindPropertyRelative("stepName").stringValue)] = list;
        }

        entries.ClearArray();
        int made = 0, restored = 0;
        var report = new StringBuilder();

        foreach (string scenario in scenarioNames)
        {
            ScenarioCollection col = loader.LoadScenarios(scenario);
            ScenarioData data = col != null && col.scenarios != null && col.scenarios.Count > 0 ? col.scenarios[0] : null;
            if (data == null) { report.AppendLine($"   {scenario}: CSV를 못 읽음 — 건너뜀"); continue; }

            // ★두개골만 국면별로 나눈다(진단·교정·재평가에서 보이는 뼈가 다름).
            //   흉추·늑골은 모든 과정에서 같은 뼈만 보이므로 시나리오당 1줄(국면 비움).
            bool splitByPhase = System.Array.Exists(PhaseSplitScenarios,
                s => string.Equals(s, scenario, System.StringComparison.OrdinalIgnoreCase));

            var phaseNames = new List<string>();
            if (splitByPhase)
            {
                foreach (PhaseData phase in data.phases)
                {
                    bool hasReal = false;
                    foreach (StepData st in phase.steps) if (!st.IsGuideStep()) { hasReal = true; break; }
                    if (!hasReal || string.IsNullOrWhiteSpace(phase.phaseName)) continue;
                    if (!phaseNames.Contains(phase.phaseName)) phaseNames.Add(phase.phaseName);
                }
            }
            else
            {
                phaseNames.Add("");     // 국면 무관 = 시나리오 전체 1줄
            }

            int lines = 0;
            foreach (string phaseName in phaseNames)
            {
                {
                    string step = "";
                    string key = Key(scenario, phaseName, step);
                    entries.InsertArrayElementAtIndex(entries.arraySize);
                    SerializedProperty e = entries.GetArrayElementAtIndex(entries.arraySize - 1);
                    e.FindPropertyRelative("scenarioName").stringValue = scenario;
                    e.FindPropertyRelative("phaseName").stringValue = phaseName;
                    e.FindPropertyRelative("stepName").stringValue = step;

                    SerializedProperty bones = e.FindPropertyRelative("showBones");
                    bones.ClearArray();
                    if (keptBones.TryGetValue(key, out List<Object> old))
                    {
                        for (int b = 0; b < old.Count; b++)
                        {
                            bones.InsertArrayElementAtIndex(b);
                            bones.GetArrayElementAtIndex(b).objectReferenceValue = old[b];
                        }
                        restored++;
                    }
                    made++; lines++;
                }
            }
            report.AppendLine($"   {scenario}: {lines}줄");
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(focus);
        Object.DestroyImmediate(probe);

        Debug.Log($"[골격 포커스] 시나리오 {scenarioNames.Count}종 → 총 {made}줄 생성 " +
                  $"(기존 뼈 배정 {restored}줄 보존).\n{report}" +
                  $"   제외(단순추나·ROM): {string.Join(", ", SkipScenarios)}\n" +
                  "★뼈를 비워 둔 줄은 '설정 안 함'이라 골격이 전부 보입니다. 필요한 줄만 채우세요.");
        EditorUtility.DisplayDialog("골격 포커스",
            $"{made}줄을 만들었습니다(기존 배정 {restored}줄 보존).\n\n" +
            "각 줄의 Show Bones에 뼈를 드래그하세요.\n" +
            "★비어 있는 줄은 그 단계에서 골격이 전부 숨겨집니다.", "확인");
    }

    [MenuItem("GuideChuna/골격 부위 목록 보기")]
    private static void ListParts()
    {
        var roots = FindRoots();
        var sb = new StringBuilder();
        sb.AppendLine($"골격 루트 {roots.Count}개");

        foreach (Transform root in roots)
        {
            sb.AppendLine($"\n■ {Path(root)}  (자식 {root.childCount}개)");
            var names = new List<string>();
            foreach (Transform c in root) names.Add(c.name + (c.gameObject.activeSelf ? "" : " [꺼짐]"));
            names.Sort();
            foreach (string n in names) sb.AppendLine("   · " + n);
        }

        if (roots.Count == 0)
            sb.AppendLine("\n'skeletal_system' 오브젝트를 못 찾았습니다. " +
                          "해부 모델이 씬에 있는지 확인하세요.");

        Debug.Log("[골격 부위 목록]\n" + sb);
    }

    /// <summary>
    /// ★<b>비어 있는 줄</b>에만 기본 키워드로 뼈를 자동 배정한다(이미 배정한 줄은 손대지 않는다).
    /// 줄 자체는 'CSV 기준 줄 만들기'가 만든다 — 여기서는 만들지 않는다.
    /// 골격 프리팹을 교체해 참조가 끊겼을 때 다시 채우는 용도이기도 하다.
    /// </summary>
    [MenuItem("GuideChuna/골격 포커스 — 빈 줄에 뼈 자동 배정")]
    private static void FillEmptyRows()
    {
        var focus = Object.FindFirstObjectByType<SkeletonFocusController>(FindObjectsInactive.Include);
        if (focus == null)
        {
            EditorUtility.DisplayDialog("골격 포커스",
                "씬에 SkeletonFocusController가 없습니다.\n\n빈 GameObject에 컴포넌트를 추가한 뒤 다시 실행하세요.", "확인");
            return;
        }

        List<Transform> bones = CollectBones();
        if (bones.Count == 0)
        {
            EditorUtility.DisplayDialog("골격 포커스",
                "씬에서 골격(skeletal_system)을 찾지 못했습니다.\n해부 모델이 씬에 있는지 확인하세요.", "확인");
            return;
        }

        var so = new SerializedObject(focus);
        SerializedProperty entries = so.FindProperty("entries");

        int filled = 0, skipped = 0, noRule = 0;
        var missing = new List<string>();

        for (int i = 0; i < entries.arraySize; i++)
        {
            SerializedProperty e = entries.GetArrayElementAtIndex(i);
            SerializedProperty objs = e.FindPropertyRelative("showBones");
            if (objs.arraySize > 0) { skipped++; continue; }          // 이미 배정됨 — 보존

            string scenario = e.FindPropertyRelative("scenarioName").stringValue;
            string phase = e.FindPropertyRelative("phaseName").stringValue;
            string step = e.FindPropertyRelative("stepName").stringValue;

            string[] keys = FindRule(scenario, phase, step);
            if (keys == null) { noRule++; continue; }                 // 규칙 없음 — 비운 채로 둔다

            foreach (string key in keys)
            {
                var hits = bones.FindAll(b => b.name.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) >= 0);
                if (hits.Count == 0) { if (!missing.Contains(key)) missing.Add(key); continue; }
                foreach (Transform h in hits)
                {
                    objs.InsertArrayElementAtIndex(objs.arraySize);
                    objs.GetArrayElementAtIndex(objs.arraySize - 1).objectReferenceValue = h;
                }
            }
            if (objs.arraySize > 0) filled++;
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(focus);

        Debug.Log($"[골격 포커스] 빈 줄 채우기 — 배정 {filled}줄 / 이미 있어 건너뜀 {skipped}줄 / 규칙 없어 비움 {noRule}줄.\n" +
                  (missing.Count > 0 ? "★씬에서 못 찾은 키워드: " + string.Join(", ", missing) + "\n" : "") +
                  "규칙 없는 줄은 직접 드래그하세요(비워 두면 그 단계는 골격 전부 숨김).");
    }

    /// <summary>기본값에서 이 줄에 맞는 키워드 묶음을 찾는다(단계 지정 > 국면 지정 > 시나리오만).</summary>
    private static string[] FindRule(string scenario, string phase, string step)
    {
        string[] best = null; int bestScore = -1;
        foreach ((string s, string ph, string st, string[] keep) in Defaults)
        {
            if (!string.Equals(s, scenario, System.StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrWhiteSpace(ph) && !string.Equals(ph, phase, System.StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrWhiteSpace(st) && !string.Equals(st, step, System.StringComparison.OrdinalIgnoreCase)) continue;
            int score = (!string.IsNullOrWhiteSpace(st) ? 2 : 0) + (!string.IsNullOrWhiteSpace(ph) ? 1 : 0);
            if (score > bestScore) { bestScore = score; best = keep; }
        }
        return best;
    }

    /// <summary>씬의 골격 오브젝트 후보를 모은다(skeletal_system 아래 전부, 중첩 포함).</summary>
    private static List<Transform> CollectBones()
    {
        var bones = new List<Transform>();
        foreach (Transform root in FindRoots())
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root) continue;
                // Visuals·SnapTarget 같은 하위 보조 오브젝트는 제외 — 부위 오브젝트만 담는다.
                if (t.name.StartsWith("Visuals") || t.name.StartsWith("SnapTarget")) continue;
                if (t.parent != null && (t.parent.name.StartsWith("Visuals") || t.parent.name.StartsWith("SnapTarget"))) continue;
                bones.Add(t);
            }
        return bones;
    }

    private static string Key(string scenario, string phase, string step) =>
        $"{(scenario ?? "").Trim()}|{(phase ?? "").Trim()}|{(step ?? "").Trim()}";

    private static List<Transform> FindRoots()
    {
        var roots = new List<Transform>();
        foreach (Transform t in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (t == null || !t.gameObject.scene.IsValid()) continue;
            if (t.name.Equals("skeletal_system", System.StringComparison.OrdinalIgnoreCase))
                roots.Add(t);
        }
        return roots;
    }

    private static string Path(Transform t)
    {
        string p = t.name;
        while (t.parent != null) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }
}
