using UnityEngine;

/// <summary>
/// 시나리오 실행 진입점 (메뉴 UI와 독립).
///
/// 기존엔 이 로직이 거대한 LobbyAuthUI_Complete.OnScenarioCardClicked 안에 묶여 있어,
/// 카드가 LobbyAuthUI 인스턴스를 통째로 참조해야 했다. 메뉴를 완전히 개편/이식할 때는
/// 그 의존이 방해가 되므로 실행에 필요한 최소 로직만 여기로 분리한다.
///
/// ★핵심: 시나리오의 "실제 로직"은 전부 TrainingScene의 ScenarioBootstrapper가
///   PlayerPrefs[SelectedScenario]를 읽어 수행한다. 메뉴가 할 일은 인덱스 저장 + 씬 로드뿐.
///   → 메뉴를 아무리 새로 만들어도 이 아래 로직은 재구현할 필요가 없다(그대로 재사용).
///
/// 로그인 상태는 LoginStateStore와 동일하게 PlayerPrefs[LoginUsername]로 읽으므로
/// LobbyAuthUI의 in-memory 상태에 의존하지 않는다.
/// </summary>
public static class ScenarioLauncher
{
    /// <summary>로그인 필요인데 미로그인 상태로 실행이 막혔을 때 발생.
    /// 새 메뉴의 로그인 팝업이 구독해 안내를 띄우면 된다(구독 없으면 로그만).</summary>
    public static event System.Action OnLoginRequired;

    public static bool IsLoggedIn
    {
        get
        {
            if (!PlayerPrefs.HasKey(PrefsKeys.LoginUsername)) return false;
            return !string.IsNullOrEmpty(PlayerPrefs.GetString(PrefsKeys.LoginUsername));
        }
    }

    /// <summary>
    /// 시나리오 실행: 인덱스를 저장하고 TrainingScene을 로드한다.
    /// index는 ScenarioBootstrapper.scenarioConfigs[]의 위치와 정확히 일치해야 한다(플랫 인덱스).
    /// </summary>
    /// <returns>실제로 실행했으면 true, 로그인 게이트에 막혔으면 false</returns>
    public static bool LaunchScenario(int index, bool requireLogin = true)
    {
        if (requireLogin && !IsLoggedIn)
        {
            ChunaLogger.LogWarning($"[ScenarioLauncher] 로그인 필요 — 시나리오 {index} 실행 취소");
            OnLoginRequired?.Invoke();
            return false;
        }

        PlayerPrefs.SetInt(PrefsKeys.SelectedScenario, index);
        PlayerPrefs.Save();
        ChunaLogger.Log($"[ScenarioLauncher] 시나리오 {index} 실행 → TrainingScene 로드");
        SceneLoader.LoadScene("TrainingScene");
        return true;
    }

    /// <summary>조작법 익히기 씬으로 이동.</summary>
    public static void OpenPracticeScene()
    {
        ChunaLogger.Log("[ScenarioLauncher] Practice_Scene 로드");
        SceneLoader.LoadScene("Practice_Scene");
    }

    /// <summary>앱 종료(에디터에선 Play 중지).</summary>
    public static void QuitApp()
    {
        ChunaLogger.Log("[ScenarioLauncher] 종료 요청");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
