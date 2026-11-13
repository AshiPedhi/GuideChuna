using UnityEngine;

/// <summary>
/// 11-13 최적화 — 일반 로그 · 경고의 호출 스택 수집을 끈다(오류 · 예외 · Assert는 그대로 둔다).
/// 프로젝트 설정(StackTraceTypes)은 다섯 종류 모두 «스크립트 스택»이라 Debug.Log 한 줄마다 스택을 모았다 — 릴리스 빌드에도 켜져 있다.
/// 로그 글자는 그대로 남고 그 밑의 스택 줄만 빠진다. 크래시 · 오류 추적에 필요한 오류 · 예외 스택은 유지한다.
/// 에디터에서는 건드리지 않는다(콘솔에서 로그 위치를 따라가야 하니까).
/// </summary>
public static class LogStackTraceTrim
{
#if !UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Apply()
    {
        Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
        Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
        Debug.Log("[로그] 일반 로그 · 경고 스택 수집 끔(오류 · 예외는 유지) — 11-13 최적화");
    }
#endif
}
