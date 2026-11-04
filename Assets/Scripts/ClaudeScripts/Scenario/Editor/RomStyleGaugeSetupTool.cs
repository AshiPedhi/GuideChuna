using UnityEditor;
using UnityEngine;

/// <summary>
/// ROM 각도기 양식(ScenarioConfig.romStyleAngleGauge)을 술기별로 켜고 끈다 (2026-10-30 시안 — 상부승모근).
/// 설정 에셋을 SerializedObject로 고친다(에셋 YAML을 텍스트로 고치지 않는다 — 한글 이스케이프 사고 예방).
/// </summary>
public static class RomStyleGaugeSetupTool
{
    const string Path = "Assets/Resources/ScenarioConfigs/상부승모근.asset";

    [MenuItem("GuideChuna/UI 개편/10-30 상부승모근 ROM 양식 각도기 켜기")]
    public static void On() => Set(true);

    [MenuItem("GuideChuna/UI 개편/10-30 상부승모근 ROM 양식 각도기 끄기(종전 2D)")]
    public static void Off() => Set(false);

    // 10-30 A안 — 적정 범위를 본 각도(°)로. 처음 값 = 지금 비율 × 90(종전 2D 각도 표시가 보여 주던 숫자 = 사용자가 정해 둔 각도)
    [MenuItem("GuideChuna/UI 개편/10-30 상부승모근 적정 범위를 본 각도로 (비율 × 90 → °)")]
    public static void DegreesOn()
    {
        var cfg = AssetDatabase.LoadAssetAtPath<ScenarioConfig>(Path);
        if (cfg == null) { Debug.LogWarning($"[ROM양식] 설정이 없다: {Path}"); return; }
        var so = new SerializedObject(cfg);
        string sb = "";
        void Deg(string ratioField, string degField)
        {
            var r = so.FindProperty(ratioField); var d = so.FindProperty(degField);
            if (r == null || d == null) { sb += $" ★{ratioField}/{degField} 없음"; return; }
            d.floatValue = Mathf.Round(r.floatValue * 90f * 10f) / 10f;
            sb += $" {degField}={d.floatValue}";
        }
        Deg("midHoldStart", "midHoldStartDeg"); Deg("midHoldEnd", "midHoldEndDeg");
        Deg("extendedMidHoldStart", "extendedHoldStartDeg"); Deg("extendedMidHoldEnd", "extendedHoldEndDeg");
        Deg("stretchingHoldStart", "stretchingHoldStartDeg"); Deg("stretchingHoldEnd", "stretchingHoldEndDeg");
        so.FindProperty("holdRangeInDegrees").boolValue = true;
        so.FindProperty("romStyleAngleGauge").boolValue = true;   // 같은 측정기를 쓴다
        so.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
        Debug.Log($"[ROM양식] {cfg.name} holdRangeInDegrees → true ·{sb}");
    }

    static void Set(bool on)
    {
        var cfg = AssetDatabase.LoadAssetAtPath<ScenarioConfig>(Path);
        if (cfg == null) { Debug.LogWarning($"[ROM양식] 설정이 없다: {Path}"); return; }
        var so = new SerializedObject(cfg);
        var p = so.FindProperty("romStyleAngleGauge");
        bool before = p.boolValue;
        p.boolValue = on;
        so.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
        Debug.Log($"[ROM양식] {cfg.name} romStyleAngleGauge {before} → {on}");
    }
}
