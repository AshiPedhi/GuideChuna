using System.Diagnostics;
using Debug = UnityEngine.Debug;

/// <summary>
/// 조건부 로깅 유틸리티
/// Release 빌드에서 자동으로 로그 호출이 제거됨
///
/// 사용법:
/// - ChunaLogger.Log("message") → Debug.Log
/// - ChunaLogger.LogWarning("message") → Debug.LogWarning
/// - ChunaLogger.LogError("message") → Debug.LogError (항상 출력)
/// - ChunaLogger.Log("TAG", "message") → Debug.Log with prefix
/// </summary>
public static class ChunaLogger
{
    #region Conditional Logging (Development Only)

    /// <summary>
    /// 개발 빌드에서만 로그 출력
    /// </summary>
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void Log(string message)
    {
        Debug.Log(message);
    }

    /// <summary>
    /// 태그와 함께 로그 출력 (개발 빌드 전용)
    /// </summary>
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void Log(string tag, string message)
    {
        Debug.Log($"[{tag}] {message}");
    }

    /// <summary>
    /// 색상이 지정된 로그 출력 (개발 빌드 전용)
    /// </summary>
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void LogColored(string color, string message)
    {
        Debug.Log($"<color={color}>{message}</color>");
    }

    /// <summary>
    /// 태그와 색상이 지정된 로그 출력 (개발 빌드 전용)
    /// </summary>
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void LogColored(string tag, string color, string message)
    {
        Debug.Log($"<color={color}>[{tag}] {message}</color>");
    }

    /// <summary>
    /// 경고 로그 (개발 빌드 전용)
    /// </summary>
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void LogWarning(string message)
    {
        Debug.LogWarning(message);
    }

    /// <summary>
    /// 태그와 함께 경고 로그 (개발 빌드 전용)
    /// </summary>
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void LogWarning(string tag, string message)
    {
        Debug.LogWarning($"[{tag}] {message}");
    }

    #endregion

    #region Always Logging (Production)

    /// <summary>
    /// 에러 로그 (항상 출력 - 프로덕션에서도 필요)
    /// </summary>
    public static void LogError(string message)
    {
        Debug.LogError(message);
    }

    /// <summary>
    /// 태그와 함께 에러 로그 (항상 출력)
    /// </summary>
    public static void LogError(string tag, string message)
    {
        Debug.LogError($"[{tag}] {message}");
    }

    #endregion

    #region Verbose Logging (Detailed Debug)

    /// <summary>
    /// 상세 디버그 로그 (showDebugLogs 플래그와 함께 사용)
    /// </summary>
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void LogVerbose(bool enabled, string message)
    {
        if (enabled)
            Debug.Log(message);
    }

    /// <summary>
    /// 상세 디버그 로그 (태그 포함)
    /// </summary>
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void LogVerbose(bool enabled, string tag, string message)
    {
        if (enabled)
            Debug.Log($"[{tag}] {message}");
    }

    /// <summary>
    /// 상세 디버그 로그 (색상 포함)
    /// </summary>
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void LogVerboseColored(bool enabled, string color, string message)
    {
        if (enabled)
            Debug.Log($"<color={color}>{message}</color>");
    }

    #endregion

    #region Frame-Limited Logging

    /// <summary>
    /// 특정 프레임 간격으로만 로그 출력 (Update에서 사용 시 로그 스팸 방지)
    /// </summary>
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void LogEveryNFrames(int frameInterval, string message)
    {
        if (UnityEngine.Time.frameCount % frameInterval == 0)
            Debug.Log(message);
    }

    /// <summary>
    /// 조건 + 프레임 간격으로 로그 출력
    /// </summary>
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void LogEveryNFrames(bool enabled, int frameInterval, string message)
    {
        if (enabled && UnityEngine.Time.frameCount % frameInterval == 0)
            Debug.Log(message);
    }

    #endregion
}
