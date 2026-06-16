#if UNITY_ANDROID
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

/// <summary>
/// 부팅 자동실행(BootReceiver) 게이팅 도구.
///
/// 정적 AndroidManifest.xml 에는 부팅 자동실행 항목이 없다(= 스토어 빌드 클린).
/// Scripting Define `CHUNA_KIOSK_AUTOLAUNCH` 가 켜진 빌드에서만
/// 빌드 후처리(OnPostGenerateGradleAndroidProject)로 생성된 manifest에
/// RECEIVE_BOOT_COMPLETED 권한 + BootReceiver 리시버를 주입한다.
///
/// BootReceiver.java 클래스 자체는 항상 컴파일되지만, manifest에 등록되지 않으면
/// 호출되지 않는 죽은 코드라 스토어 정책상 무해하다(정책 스캔은 manifest 기준).
///
/// 사용:
///  - 키오스크 빌드: 메뉴 Build > 키오스크 자동실행 ON → (재컴파일 대기) → 빌드
///  - 스토어 빌드: 메뉴 Build > 키오스크 자동실행 OFF → 빌드
/// </summary>
public class KioskAutoLaunchBuild : IPostGenerateGradleAndroidProject
{
    private const string DEFINE = "CHUNA_KIOSK_AUTOLAUNCH";
    private const string ANDROID_NS = "http://schemas.android.com/apk/res/android";
    private const string RECEIVER_CLASS = "com.coco.chuna.boot.BootReceiver";
    private const string BOOT_PERMISSION = "android.permission.RECEIVE_BOOT_COMPLETED";
    // SYSTEM_ALERT_WINDOW: BAL(백그라운드 액티비티 실행) 면제용. 부팅 ~15초 후 AlarmManager
    // 실행이 차단되지 않게 한다. manifest 선언만으론 부족하고 기기마다
    // `adb shell appops set com.CoCo.CHUNA SYSTEM_ALERT_WINDOW allow` 1회 부여가 필요하다.
    private const string SAW_PERMISSION = "android.permission.SYSTEM_ALERT_WINDOW";
    // SCHEDULE_EXACT_ALARM: targetSdk 32(API 31+)에서 setExactAndAllowWhileIdle 호출에 필요.
    // 미선언 시 SecurityException 으로 알람이 조용히 안 걸려 자동실행이 통째로 실패한다.
    // API 31/32 에서는 선언만 하면 기본 허용(BootReceiver 는 불가 시 inexact 로 폴백).
    private const string EXACT_ALARM_PERMISSION = "android.permission.SCHEDULE_EXACT_ALARM";

    public int callbackOrder => 90;

    public void OnPostGenerateGradleAndroidProject(string path)
    {
#if CHUNA_KIOSK_AUTOLAUNCH
        string manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
        if (!File.Exists(manifestPath))
        {
            Debug.LogWarning($"[KioskAutoLaunch] 생성된 manifest를 찾지 못함: {manifestPath}");
            return;
        }

        try
        {
            var doc = new XmlDocument();
            doc.Load(manifestPath);

            var manifest = doc.DocumentElement;
            if (manifest == null)
            {
                Debug.LogWarning("[KioskAutoLaunch] <manifest> 루트 없음");
                return;
            }

            bool changed = false;
            changed |= EnsureUsesPermission(doc, manifest, BOOT_PERMISSION);
            changed |= EnsureUsesPermission(doc, manifest, SAW_PERMISSION);
            changed |= EnsureUsesPermission(doc, manifest, EXACT_ALARM_PERMISSION);
            changed |= EnsureBootReceiver(doc, manifest);

            if (changed)
            {
                doc.Save(manifestPath);
                Debug.Log("[KioskAutoLaunch] 부팅 자동실행 주입 완료 (키오스크 빌드)");
            }
            else
            {
                Debug.Log("[KioskAutoLaunch] 이미 주입돼 있음 (변경 없음)");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[KioskAutoLaunch] manifest 주입 실패: {e}");
        }
#else
        // 스토어 빌드: 주입하지 않음. 정적 manifest가 그대로 클린 상태로 유지된다.
#endif
    }

#if CHUNA_KIOSK_AUTOLAUNCH
    private static bool EnsureUsesPermission(XmlDocument doc, XmlElement manifest, string permissionName)
    {
        foreach (XmlElement perm in manifest.GetElementsByTagName("uses-permission"))
        {
            if (perm.GetAttribute("name", ANDROID_NS) == permissionName)
                return false; // 이미 존재
        }

        var node = doc.CreateElement("uses-permission");
        SetAndroidAttr(doc, node, "name", permissionName);
        manifest.AppendChild(node);
        return true;
    }

    private static bool EnsureBootReceiver(XmlDocument doc, XmlElement manifest)
    {
        var application = manifest.SelectSingleNode("application") as XmlElement;
        if (application == null)
        {
            Debug.LogWarning("[KioskAutoLaunch] <application> 없음 — 리시버 주입 생략");
            return false;
        }

        foreach (XmlElement r in application.GetElementsByTagName("receiver"))
        {
            if (r.GetAttribute("name", ANDROID_NS) == RECEIVER_CLASS)
                return false; // 이미 존재
        }

        var receiver = doc.CreateElement("receiver");
        SetAndroidAttr(doc, receiver, "name", RECEIVER_CLASS);
        SetAndroidAttr(doc, receiver, "enabled", "true");
        SetAndroidAttr(doc, receiver, "exported", "true");

        var filter = doc.CreateElement("intent-filter");
        filter.AppendChild(CreateAction(doc, "android.intent.action.BOOT_COMPLETED"));
        filter.AppendChild(CreateAction(doc, "android.intent.action.QUICKBOOT_POWERON"));
        receiver.AppendChild(filter);

        application.AppendChild(receiver);
        return true;
    }

    private static XmlElement CreateAction(XmlDocument doc, string actionName)
    {
        var action = doc.CreateElement("action");
        SetAndroidAttr(doc, action, "name", actionName);
        return action;
    }

    private static void SetAndroidAttr(XmlDocument doc, XmlElement element, string localName, string value)
    {
        var attr = doc.CreateAttribute("android", localName, ANDROID_NS);
        attr.Value = value;
        element.Attributes.Append(attr);
    }
#endif

    // ── Scripting Define 토글 메뉴 ──────────────────────────────────────────

    [MenuItem("Build/키오스크 자동실행 ON (CHUNA_KIOSK_AUTOLAUNCH)")]
    public static void EnableDefine()
    {
        SetDefine(true);
    }

    [MenuItem("Build/키오스크 자동실행 OFF")]
    public static void DisableDefine()
    {
        SetDefine(false);
    }

    [MenuItem("Build/키오스크 자동실행 상태 확인")]
    public static void LogState()
    {
        Debug.Log($"[KioskAutoLaunch] CHUNA_KIOSK_AUTOLAUNCH = {(HasDefine() ? "ON (키오스크 빌드)" : "OFF (스토어 클린)")}");
    }

    private static bool HasDefine()
    {
        string defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android);
        return new List<string>(defines.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)).Contains(DEFINE);
    }

    private static void SetDefine(bool enable)
    {
        var target = NamedBuildTarget.Android;
        string defines = PlayerSettings.GetScriptingDefineSymbols(target);
        var list = new List<string>(defines.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));

        bool has = list.Contains(DEFINE);
        if (enable && !has) list.Add(DEFINE);
        else if (!enable && has) list.Remove(DEFINE);

        PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", list));
        Debug.Log($"[KioskAutoLaunch] CHUNA_KIOSK_AUTOLAUNCH = {(enable ? "ON (키오스크 빌드)" : "OFF (스토어 클린)")} — 재컴파일 후 빌드하세요.");
    }
}
#endif
