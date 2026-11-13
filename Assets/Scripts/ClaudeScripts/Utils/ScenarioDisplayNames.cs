using System.Collections.Generic;

/// <summary>
/// 술기 화면 이름 — <b>한 곳</b>에서 정한다 (2026-10-27 사용자 지시 «명칭은 내가 준 목록으로 통일 · 양식과 규격 통일»).
///
/// [규격]
///   · 제목 = 술기 명칭(예: 흉추교정1) · 부제 = 기법명(예: 앙와위 흉추 신전변위 교정기법)
///   · 두 줄로 쓸 곳(로비 카드)은 제목/부제, 한 줄로 쓸 곳(실습 씬 제목 · 결과표 · 미러링)은 «제목 - 부제»
/// [키] ScenarioConfig.scenarioName(내부 이름). ★내부 이름은 CSV 키 · 판정 키라 바꾸지 않는다(CLAUDE.md 규칙 8) — 화면 이름만 여기서 바꾼다.
/// [근거] 복잡추나 7종 = 사용자가 준 목록(10-27) 그대로 · 단순추나 5종 = 그때 로비 카드 글자 · 경추ROM = 09-07 지시 «경추ROM진단».
/// 표에 없는 이름은 내부 이름을 그대로 보인다(종전 동작).
/// </summary>
public static class ScenarioDisplayNames
{
    private static readonly Dictionary<string, (string title, string subtitle)> Map = new Dictionary<string, (string, string)>
    {
        // 단순추나(근막요법)
        { "상부승모근", ("상부승모근", "이완강화기법") },
        { "견갑거근", ("견갑거근", "이완강화기법") },
        { "대흉근", ("대흉근", "이완강화기법") },
        { "사각근", ("사각근", "이완강화기법") },
        { "흉쇄유돌근", ("흉쇄유돌근", "이완강화기법") },
        // 복잡추나(관절교정) — 사용자 목록 10-27
        { "두개골OM교정", ("두개골(OM) 교정", "앙와위 후두유양돌기 봉합 교정기법") },
        { "두개골PM교정", ("두개골(PM) 교정", "앙와위 두정골유양돌기 결합 교정기법") },
        { "두개골PJ교정", ("두개골(PJ) 교정", "앙와위 측두골추체 아탈구 교정기법") },
        { "앙와위_흉추_신전변위", ("흉추교정1", "앙와위 흉추 신전변위 교정기법") },
        { "복와위_하부흉추_굴곡변위", ("흉추교정2", "복와위 양손두상골 하부흉추 골곡변위 교정기법") },   // ★«골곡»은 받은 목록 그대로 — 오타 여부 확인 대기
        { "제1늑골 교정기법", ("늑골교정1", "흉곽부 제1늑골(1st rib) 교정") },
        { "제2늑골_상방변위", ("늑골교정2", "흉곽부 제2늑골(2nd rib) 교정") },
        // ROM진단 — 09-07 지시 «경추ROM진단» → 11-13 사용자 «경추ROM진단을 XR-ROM경추로»
        { "경추ROM측정", ("XR-ROM경추", "") },
    };

    public static bool Has(string internalName) => !string.IsNullOrEmpty(internalName) && Map.ContainsKey(internalName);

    public static string Title(string internalName)
        => internalName != null && Map.TryGetValue(internalName, out var v) ? v.title : internalName;

    public static string Subtitle(string internalName)
        => internalName != null && Map.TryGetValue(internalName, out var v) ? v.subtitle : "";

    private static Dictionary<string, string> fullCache;

    /// <summary>한 줄 표기 «제목 - 부제»(부제가 없으면 제목만). 미리 만들어 둔 문자열을 돌려준다(매 프레임 불러도 할당 없음 — 미러링이 자주 부른다).</summary>
    public static string Full(string internalName)
    {
        if (internalName == null) return null;
        if (fullCache == null)
        {
            fullCache = new Dictionary<string, string>(Map.Count);
            foreach (var kv in Map)
                fullCache[kv.Key] = string.IsNullOrEmpty(kv.Value.subtitle) ? kv.Value.title : kv.Value.title + " - " + kv.Value.subtitle;
        }
        return fullCache.TryGetValue(internalName, out var f) ? f : internalName;
    }
}
