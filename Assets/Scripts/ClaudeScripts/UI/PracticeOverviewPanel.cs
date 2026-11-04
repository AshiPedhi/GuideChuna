using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 실습 개요 화면 — 상단 [시작]을 누르기 전에 진행 현황 창 위에 덮어 보인다 (2026-11-04 사용자 «실습 시작 버튼 전 개요화면 필요» · 내용 A안).
///
/// 내용(A안): 술기명 · 목표 · 단계 목록 · 주의사항 · «[시작]을 누르세요».
///   · 단계 목록 = <see cref="ScenarioManager.LoadFilteredScenario"/>(실제 시작과 같은 함수 — 모드 · 평가 거르기 포함)의 stepName.
///     «가이드» 단계는 뺀다. ★단면 용어(시상면 · 관상면 · 횡단면)가 든 단계도 뺀다 — ROM 안내는 단면 용어를 말하지 않는다(09-11 사용자 지시).
///     stepName은 판정 키라 이름을 바꾸지 않는다(규칙 8) — 보이는 목록에서만 거른다.
///   · 목표 · 주의사항 = <c>Resources/Overview/{scenarioName}.txt</c>(«# 목표» · «# 주의사항» 머리줄로 나눈다). ★없으면 그 칸을 통째로 안 그린다
///     (지어낸 의학 문구를 넣지 않는다 — 내용은 사람이 채운다).
/// ★InfoPanelController가 런타임에 붙이고 Show/Hide만 부른다. 씬은 건드리지 않는다(규칙 1).
/// ★할당은 Show 한 번뿐이다(Update 없음).
/// </summary>
public class PracticeOverviewPanel : MonoBehaviour
{
    private const string OverviewFolder = "Overview";
    private static readonly string[] HiddenStepWords = { "가이드", "시상면", "관상면", "횡단면" };

    private RectTransform root;
    private TextMeshProUGUI body;

    public bool IsShown => root != null && root.gameObject.activeSelf;

    public void Show(GameObject page, ScenarioData scenario, ScenarioConfig config)
    {
        if (page == null) { Debug.LogWarning("[개요] ★진행 현황 창(resultPage)이 없다 — 개요를 못 띄운다"); return; }
        if (root == null || root.parent != page.transform) Build(page.transform);

        string key = config != null && !string.IsNullOrEmpty(config.scenarioName) ? config.scenarioName
                   : scenario != null ? scenario.scenarioName : "";
        var file = string.IsNullOrEmpty(key) ? null : Resources.Load<TextAsset>($"{OverviewFolder}/{key}");
        Split(file != null ? file.text : null, out string goal, out string caution);

        var sb = new StringBuilder(512);
        sb.Append("<size=150%><b>").Append(string.IsNullOrEmpty(key) ? "실습" : key.Replace('_', ' ')).Append("</b></size>\n");
        sb.Append("<size=85%><color=#B8BCC2>실습 개요</color></size>\n\n");
        if (goal.Length > 0) Section(sb, "목표", goal);
        int steps = AppendSteps(sb, scenario);
        if (caution.Length > 0) Section(sb, "주의사항", caution);
        sb.Append("\n<color=#7FB2FF><b>준비되면 상단의 [시작]을 누르세요.</b></color>");
        body.text = sb.ToString();

        root.SetAsLastSibling();
        root.gameObject.SetActive(true);
        Debug.Log($"[개요] 띄움 · '{key}' · 단계 {steps}개 · 목표 {(goal.Length > 0 ? "있음" : "없음")} · 주의사항 {(caution.Length > 0 ? "있음" : "없음")} · 파일 Resources/{OverviewFolder}/{key}.txt {(file != null ? "있음" : "★없음(목표 · 주의사항 칸 생략)")}");
    }

    public void Hide()
    {
        if (root != null && root.gameObject.activeSelf) { root.gameObject.SetActive(false); Debug.Log("[개요] 걷음"); }
    }

    private void Build(Transform page)
    {
        var go = new GameObject("실습 개요 (런타임)", typeof(RectTransform));
        go.layer = page.gameObject.layer;
        root = (RectTransform)go.transform;
        root.SetParent(page, false);
        root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.offsetMin = root.offsetMax = Vector2.zero;
        var le = go.AddComponent<LayoutElement>(); le.ignoreLayout = true;   // 창 안 레이아웃에 끌려가지 않게
        var bg = go.AddComponent<Image>(); bg.color = new Color(0.188f, 0.188f, 0.188f, 1f);   // 판 색 #303030 — 아래 결과표를 가린다
        bg.raycastTarget = false;

        var t = new GameObject("본문", typeof(RectTransform));
        t.layer = go.layer;
        var tr = (RectTransform)t.transform;
        tr.SetParent(root, false);
        tr.anchorMin = new Vector2(0.06f, 0.06f); tr.anchorMax = new Vector2(0.94f, 0.94f); tr.offsetMin = tr.offsetMax = Vector2.zero;
        body = t.AddComponent<TextMeshProUGUI>();
        var font = KoreanFontResolver.Resolve(); if (font != null) body.font = font;
        body.enableAutoSizing = true; body.fontSizeMin = 6; body.fontSizeMax = 40;
        body.alignment = TextAlignmentOptions.TopLeft; body.color = new Color(0.94f, 0.95f, 0.96f, 1f);
        body.textWrappingMode = TextWrappingModes.Normal; body.raycastTarget = false;
        body.lineSpacing = 8f;
    }

    private static void Section(StringBuilder sb, string title, string text)
    {
        sb.Append("<b>").Append(title).Append("</b>\n").Append(text.Trim()).Append("\n\n");
    }

    /// <summary>phase별로 «진단 → 제한장벽확인 → …». phase 이름이 시작 · 종료면 머리말 없이 쓴다.</summary>
    private static int AppendSteps(StringBuilder sb, ScenarioData scenario)
    {
        if (scenario == null || scenario.phases == null) return 0;
        int count = 0;
        var line = new StringBuilder(128);
        bool header = false;
        foreach (var phase in scenario.phases)
        {
            line.Length = 0;
            if (phase?.steps == null) continue;
            foreach (var step in phase.steps)
            {
                if (step == null || string.IsNullOrEmpty(step.stepName) || Hidden(step.stepName)) continue;
                if (line.Length > 0) line.Append(" → ");
                line.Append(step.stepName);
                count++;
            }
            if (line.Length == 0) continue;
            if (!header) { sb.Append("<b>진행 단계</b>\n"); header = true; }
            bool plain = phase.phaseName == "시작" || phase.phaseName == "종료" || string.IsNullOrEmpty(phase.phaseName);
            sb.Append("• ");
            if (!plain) sb.Append("<color=#B8BCC2>").Append(phase.phaseName).Append("</color>  ");
            sb.Append(line).Append('\n');
        }
        if (header) sb.Append('\n');
        return count;
    }

    private static bool Hidden(string stepName)
    {
        for (int i = 0; i < HiddenStepWords.Length; i++)
            if (stepName.Contains(HiddenStepWords[i])) return true;
        return false;
    }

    /// <summary>«# 목표» · «# 주의사항» 머리줄로 나눈다. 머리줄이 없으면 전부 목표로 본다.</summary>
    private static void Split(string text, out string goal, out string caution)
    {
        goal = ""; caution = "";
        if (string.IsNullOrEmpty(text)) return;
        var g = new StringBuilder(); var c = new StringBuilder();
        StringBuilder cur = g;
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            string l = raw.TrimEnd();
            if (l.StartsWith("#"))
            {
                string h = l.TrimStart('#').Trim();
                cur = h.StartsWith("주의") ? c : g;
                continue;
            }
            cur.Append(l).Append('\n');
        }
        goal = g.ToString().Trim(); caution = c.ToString().Trim();
    }
}
