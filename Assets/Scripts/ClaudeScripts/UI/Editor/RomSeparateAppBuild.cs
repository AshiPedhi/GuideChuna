using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// ★[미사용 2026-11-13] 사용자 «XR-ROM을 추나에 같이 합쳐야» → A안: 추나 앱 로비 카드(경추ROM측정 · idx 13 · sceneOverride RomLineScene)로 들어간다.
///   별도 앱은 더 만들지 않는다. 메뉴는 지우지 않고 남긴다(CLAUDE.md 미사용 코드 방침). 추나 미러링과의 포트 비켜 주기는 ChunaWebMirror.Suspend/Resume.
/// 11-11 사용자 «ROM 지금 별도 앱으로 디바이스에 설치하고 싶은데 · 지금 옵션으로 빌드하면 추나랑 ROM 둘 중 하나가 덮어씌워져 버리니까».
/// 원인 = 추나 판과 선 실측(ROM) 판이 같은 패키지 이름(com.CoCo.CHUNA)이라 설치하면 서로 덮어쓴다.
/// → ROM 판만 <b>다른 패키지 이름 · 앱 이름</b>으로 따로 빌드한다(기기에 두 앱이 나란히 깔린다).
///   · 패키지 com.CoCo.CHUNA.ROM · 앱 이름 «CHUNA ROM» · 씬 RomLineScene 하나 · 출력 C:/UnityProjects/GuideChuna_ROM.apk
///   · 빌드가 끝나면(성공 · 실패 모두) 패키지 이름 · 앱 이름을 원래대로 되돌린다. Build Settings 창의 씬 목록은 건드리지 않는다.
/// ★서명: 키스토어 비밀번호가 에디터를 다시 켜면 비어 있다(10-13 «Can not sign the application») — Player > Publishing Settings에 넣고 돌린다.
/// ★로그 · 결과는 Editor.log «[ROM별도앱]»과 Temp/chuna-bridge/build-result.txt(브리지 build-run과 같은 자리).
/// </summary>
public static class RomSeparateAppBuild
{
    const string RomId = "com.CoCo.CHUNA.ROM", RomName = "CHUNA ROM";
    const string Scene = "Assets/Scenes/RomLineScene.unity";
    const string Out = "C:/UnityProjects/GuideChuna_ROM.apk";

    [MenuItem("GuideChuna/빌드/프로젝트 설정 지금 저장")]
    public static void SaveSettings()
    {
        AssetDatabase.SaveAssets();
        Debug.Log($"[ROM별도앱] 설정 저장 — 패키지 {PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android)} · 앱 이름 {PlayerSettings.productName}");
    }

    [MenuItem("GuideChuna/빌드/선 실측(ROM) 별도 앱 빌드 (추나 앱과 따로 설치)")]
    public static void Build()
    {
        if (!File.Exists(Scene)) { Debug.LogError($"[ROM별도앱] ★씬이 없다: {Scene}"); return; }
        string oldId = PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android);
        string oldName = PlayerSettings.productName;
        string result;
        try
        {
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, RomId);
            PlayerSettings.productName = RomName;
            var opt = new BuildPlayerOptions { scenes = new[] { Scene }, locationPathName = Out, target = BuildTarget.Android, options = BuildOptions.None };
            var t0 = System.DateTime.Now;
            BuildReport rep = BuildPipeline.BuildPlayer(opt);
            result = $"{rep.summary.result} · {(System.DateTime.Now - t0).TotalSeconds:F0}초 · 오류 {rep.summary.totalErrors} · {Out} · 패키지 {RomId}";
        }
        finally
        {
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, oldId);
            PlayerSettings.productName = oldName;
            AssetDatabase.SaveAssets();   // 11-11 빌드 중 디스크(ProjectSettings.asset)에 ROM 값이 써진 채 남았다 → 되돌린 값을 바로 저장
        }
        Debug.Log($"[ROM별도앱] 빌드 끝 — {result} · 패키지 · 앱 이름 되돌림({oldId} · {oldName})");
        try { Directory.CreateDirectory("Temp/chuna-bridge"); File.WriteAllText("Temp/chuna-bridge/build-result.txt", "ROM별도앱 " + result); } catch { }
    }
}
