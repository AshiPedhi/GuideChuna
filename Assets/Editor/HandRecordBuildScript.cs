using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// HandRecord 씬만 별도 APK로 빌드하는 도구.
/// 메뉴: Build > HandRecord 전용 APK 빌드
///
/// 기존 PlayerSettings/EditorBuildSettings를 백업 → 임시 변경 → 빌드 → 무조건 원복.
/// 메인 GuideChuna 앱 설정에 영향 없음.
/// </summary>
public static class HandRecordBuildScript
{
    private const string HAND_RECORD_SCENE_PATH = "Assets/Scenes/HandRecord.unity";
    private const string PACKAGE_SUFFIX = ".handrecord";
    private const string PRODUCT_SUFFIX = " HandRecord";
    private const string BUILD_VERSION = "1.0";
    private const int BUILD_VERSION_CODE = 1;
    private const string OUTPUT_DIR = "Builds/HandRecord";

    [MenuItem("Build/HandRecord 전용 APK 빌드")]
    public static void BuildHandRecordOnly()
    {
        if (!File.Exists(HAND_RECORD_SCENE_PATH))
        {
            EditorUtility.DisplayDialog("HandRecord 빌드 실패",
                $"씬을 찾을 수 없습니다:\n{HAND_RECORD_SCENE_PATH}", "확인");
            return;
        }

        // 현재 빌드 타겟이 Android 인지 확인
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            bool switchTarget = EditorUtility.DisplayDialog("빌드 타겟 변경 필요",
                $"현재 타겟: {EditorUserBuildSettings.activeBuildTarget}\nAndroid로 전환하시겠습니까?",
                "전환", "취소");
            if (!switchTarget) return;

            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        }

        NamedBuildTarget ndAndroid = NamedBuildTarget.Android;

        // === 백업 ===
        string origIdentifier = PlayerSettings.GetApplicationIdentifier(ndAndroid);
        string origProductName = PlayerSettings.productName;
        string origBundleVersion = PlayerSettings.bundleVersion;
        int origVersionCode = PlayerSettings.Android.bundleVersionCode;
        EditorBuildSettingsScene[] origScenes = EditorBuildSettings.scenes;
        ScriptingImplementation origScripting = PlayerSettings.GetScriptingBackend(ndAndroid);

        Debug.Log($"[HandRecordBuild] === 빌드 시작 ===\n" +
                  $"  원본 패키지: {origIdentifier}\n" +
                  $"  원본 제품명: {origProductName}\n" +
                  $"  원본 버전: {origBundleVersion} ({origVersionCode})\n" +
                  $"  원본 씬 개수: {origScenes.Length}");

        try
        {
            // === 임시 변경 ===
            string newIdentifier = origIdentifier + PACKAGE_SUFFIX;
            string newProductName = origProductName + PRODUCT_SUFFIX;

            PlayerSettings.SetApplicationIdentifier(ndAndroid, newIdentifier);
            PlayerSettings.productName = newProductName;
            PlayerSettings.bundleVersion = BUILD_VERSION;
            PlayerSettings.Android.bundleVersionCode = BUILD_VERSION_CODE;

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(HAND_RECORD_SCENE_PATH, true)
            };

            Debug.Log($"[HandRecordBuild] 임시 설정 적용\n" +
                      $"  패키지: {newIdentifier}\n" +
                      $"  제품명: {newProductName}\n" +
                      $"  버전: {BUILD_VERSION} ({BUILD_VERSION_CODE})\n" +
                      $"  씬: {HAND_RECORD_SCENE_PATH}");

            // === 출력 경로 ===
            if (!Directory.Exists(OUTPUT_DIR))
            {
                Directory.CreateDirectory(OUTPUT_DIR);
            }
            string apkName = $"HandRecord_v{BUILD_VERSION}_{DateTime.Now:yyyyMMdd_HHmmss}.apk";
            string outputPath = Path.Combine(OUTPUT_DIR, apkName);

            // === 빌드 실행 ===
            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = new[] { HAND_RECORD_SCENE_PATH },
                locationPathName = outputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                double sizeMB = summary.totalSize / 1024.0 / 1024.0;
                double seconds = summary.totalTime.TotalSeconds;
                string msg = $"빌드 성공!\n\n" +
                             $"경로: {outputPath}\n" +
                             $"크기: {sizeMB:F1} MB\n" +
                             $"소요 시간: {seconds:F1}초\n" +
                             $"패키지: {newIdentifier}";
                Debug.Log($"[HandRecordBuild] {msg}");
                bool reveal = EditorUtility.DisplayDialog("HandRecord 빌드 완료", msg, "폴더 열기", "닫기");
                if (reveal)
                {
                    EditorUtility.RevealInFinder(outputPath);
                }
            }
            else
            {
                string msg = $"빌드 실패: {summary.result}\n에러 {summary.totalErrors}개, 경고 {summary.totalWarnings}개\nConsole 확인 요망";
                Debug.LogError($"[HandRecordBuild] {msg}");
                EditorUtility.DisplayDialog("HandRecord 빌드 실패", msg, "확인");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[HandRecordBuild] 예외 발생: {e}");
            EditorUtility.DisplayDialog("HandRecord 빌드 예외",
                $"빌드 중 예외 발생:\n{e.Message}\n\n빌드 설정은 자동 원복됩니다.", "확인");
        }
        finally
        {
            // === 무조건 원복 ===
            PlayerSettings.SetApplicationIdentifier(ndAndroid, origIdentifier);
            PlayerSettings.productName = origProductName;
            PlayerSettings.bundleVersion = origBundleVersion;
            PlayerSettings.Android.bundleVersionCode = origVersionCode;
            EditorBuildSettings.scenes = origScenes;

            AssetDatabase.SaveAssets();

            Debug.Log($"[HandRecordBuild] === 설정 원복 완료 ===\n" +
                      $"  패키지: {origIdentifier}\n" +
                      $"  제품명: {origProductName}\n" +
                      $"  버전: {origBundleVersion} ({origVersionCode})\n" +
                      $"  씬 개수: {origScenes.Length}");
        }
    }

    [MenuItem("Build/HandRecord 빌드 설정 미리보기")]
    public static void PreviewSettings()
    {
        NamedBuildTarget ndAndroid = NamedBuildTarget.Android;
        string origIdentifier = PlayerSettings.GetApplicationIdentifier(ndAndroid);
        string newIdentifier = origIdentifier + PACKAGE_SUFFIX;
        string outputPath = Path.Combine(OUTPUT_DIR,
            $"HandRecord_v{BUILD_VERSION}_{DateTime.Now:yyyyMMdd_HHmmss}.apk");

        string msg = $"=== HandRecord 빌드 미리보기 ===\n\n" +
                     $"현재 메인 앱:\n" +
                     $"  패키지: {origIdentifier}\n" +
                     $"  제품명: {PlayerSettings.productName}\n" +
                     $"  버전: {PlayerSettings.bundleVersion} ({PlayerSettings.Android.bundleVersionCode})\n\n" +
                     $"HandRecord 빌드 시 (임시):\n" +
                     $"  패키지: {newIdentifier}\n" +
                     $"  제품명: {PlayerSettings.productName + PRODUCT_SUFFIX}\n" +
                     $"  버전: {BUILD_VERSION} ({BUILD_VERSION_CODE})\n" +
                     $"  씬: {HAND_RECORD_SCENE_PATH}\n" +
                     $"  출력: {outputPath}\n\n" +
                     $"빌드 후 원본 설정은 자동 원복됩니다.";

        EditorUtility.DisplayDialog("HandRecord 빌드 설정 미리보기", msg, "확인");
    }
}
