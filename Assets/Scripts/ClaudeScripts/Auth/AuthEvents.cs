using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 중앙 집중식 인증 이벤트 시스템
/// 
/// [장점]
/// - 전역 이벤트 관리
/// - 느슨한 결합 (Loose Coupling)
/// - 컴포넌트 간 직접 참조 불필요
/// - 디버깅 용이 (모든 이벤트를 한 곳에서 추적)
/// </summary>
public static class AuthEvents
{
    #region Authentication Events
    public static event Action<string> OnAuthenticationStarted;
    public static event Action<string> OnAuthenticationSuccess;
    public static event Action<string> OnAuthenticationFailed;
    public static event Action OnDeviceReset;
    #endregion

    #region User Events
    public static event Action<string> OnUserListLoadStarted;
    public static event Action<int> OnUserListLoadCompleted;
    public static event Action<string> OnUserListLoadFailed;
    public static event Action<UserInfo> OnUserSelected;
    #endregion

    #region Login/Logout Events
    public static event Action<string> OnLoginStarted;
    public static event Action<string, int> OnLoginSuccess;
    public static event Action<string, string> OnLoginFailed;
    public static event Action<string> OnLogoutCompleted;
    #endregion

    #region Data Events
    public static event Action<int> OnQuizDataLoaded;
    public static event Action<ResultData> OnResultSubmitted;
    #endregion

    #region UI Events
    public static event Action<bool> OnLoadingStateChanged;
    public static event Action<string> OnScreenChanged;
    #endregion

    #region Error Events
    public static event Action<string> OnNetworkError;
    public static event Action<string, Exception> OnError;
    #endregion

    #region Trigger Methods - Authentication
    public static void TriggerAuthenticationStarted(string deviceSN)
    {
        OnAuthenticationStarted?.Invoke(deviceSN);
        LogEvent($"인증 시작: {deviceSN}");
    }

    public static void TriggerAuthenticationSuccess(string deviceSN)
    {
        OnAuthenticationSuccess?.Invoke(deviceSN);
        LogEvent($"인증 성공: {deviceSN}");
    }

    public static void TriggerAuthenticationFailed(string errorMessage)
    {
        OnAuthenticationFailed?.Invoke(errorMessage);
        LogEvent($"인증 실패: {errorMessage}");
    }

    public static void TriggerDeviceReset()
    {
        OnDeviceReset?.Invoke();
        LogEvent("디바이스 초기화");
    }
    #endregion

    #region Trigger Methods - User
    public static void TriggerUserListLoadStarted(string orgID)
    {
        OnUserListLoadStarted?.Invoke(orgID);
        LogEvent($"사용자 목록 로드 시작: {orgID}");
    }

    public static void TriggerUserListLoadCompleted(int userCount)
    {
        OnUserListLoadCompleted?.Invoke(userCount);
        LogEvent($"사용자 목록 로드 완료: {userCount}명");
    }

    public static void TriggerUserListLoadFailed(string errorMessage)
    {
        OnUserListLoadFailed?.Invoke(errorMessage);
        LogEvent($"사용자 목록 로드 실패: {errorMessage}");
    }

    public static void TriggerUserSelected(UserInfo userInfo)
    {
        OnUserSelected?.Invoke(userInfo);
        LogEvent($"사용자 선택: {userInfo.runUser}");
    }
    #endregion

    #region Trigger Methods - Login/Logout
    public static void TriggerLoginStarted(string username)
    {
        OnLoginStarted?.Invoke(username);
        LogEvent($"로그인 시작: {username}");
    }

    public static void TriggerLoginSuccess(string username, int userID)
    {
        OnLoginSuccess?.Invoke(username, userID);
        LogEvent($"로그인 성공: {username} (ID: {userID})");
    }

    public static void TriggerLoginFailed(string username, string errorMessage)
    {
        OnLoginFailed?.Invoke(username, errorMessage);
        LogEvent($"로그인 실패: {username} - {errorMessage}");
    }

    public static void TriggerLogoutCompleted(string username)
    {
        OnLogoutCompleted?.Invoke(username);
        LogEvent($"로그아웃 완료: {username}");
    }
    #endregion

    #region Trigger Methods - Data
    public static void TriggerQuizDataLoaded(int quizCount)
    {
        OnQuizDataLoaded?.Invoke(quizCount);
        LogEvent($"퀴즈 데이터 로드: {quizCount}개");
    }

    public static void TriggerResultSubmitted(ResultData resultData)
    {
        OnResultSubmitted?.Invoke(resultData);
        LogEvent($"결과 전송: {resultData.username}");
    }
    #endregion

    #region Trigger Methods - UI
    public static void TriggerLoadingStateChanged(bool isLoading)
    {
        OnLoadingStateChanged?.Invoke(isLoading);
        LogEvent($"로딩 상태: {(isLoading ? "시작" : "종료")}");
    }

    public static void TriggerScreenChanged(string screenName)
    {
        OnScreenChanged?.Invoke(screenName);
        LogEvent($"화면 전환: {screenName}");
    }
    #endregion

    #region Trigger Methods - Error
    public static void TriggerNetworkError(string errorMessage)
    {
        OnNetworkError?.Invoke(errorMessage);
        LogEvent($"네트워크 오류: {errorMessage}", true);
    }

    public static void TriggerError(string errorMessage, Exception exception = null)
    {
        OnError?.Invoke(errorMessage, exception);
        LogEvent($"오류 발생: {errorMessage}", true);
    }
    #endregion

    #region Utility
    public static void ClearAllEvents()
    {
        OnAuthenticationStarted = null;
        OnAuthenticationSuccess = null;
        OnAuthenticationFailed = null;
        OnDeviceReset = null;

        OnUserListLoadStarted = null;
        OnUserListLoadCompleted = null;
        OnUserListLoadFailed = null;
        OnUserSelected = null;

        OnLoginStarted = null;
        OnLoginSuccess = null;
        OnLoginFailed = null;
        OnLogoutCompleted = null;

        OnQuizDataLoaded = null;
        OnResultSubmitted = null;

        OnLoadingStateChanged = null;
        OnScreenChanged = null;

        OnNetworkError = null;
        OnError = null;

        LogEvent("모든 이벤트 구독 해제");
    }

    private static void LogEvent(string message, bool isError = false)
    {
#if UNITY_EDITOR
        if (isError)
        {
            Debug.LogError($"[AuthEvents] {message}");
        }
        else
        {
            Debug.Log($"[AuthEvents] {message}");
        }
#endif
    }

    public static void LogSubscriberCount()
    {
#if UNITY_EDITOR
        Debug.Log($"[AuthEvents] 이벤트 구독자 수:");
        Debug.Log($"  - OnAuthenticationSuccess: {OnAuthenticationSuccess?.GetInvocationList().Length ?? 0}");
        Debug.Log($"  - OnLoginSuccess: {OnLoginSuccess?.GetInvocationList().Length ?? 0}");
        Debug.Log($"  - OnUserListLoadCompleted: {OnUserListLoadCompleted?.GetInvocationList().Length ?? 0}");
#endif
    }
    #endregion
}

/// <summary>
/// 이벤트 자동 구독 해제 헬퍼 (수정됨 - ref 에러 해결)
/// 
/// [사용 예시]
/// using (var subscription = new EventSubscription())
/// {
///     subscription.Subscribe(() => AuthEvents.OnAuthenticationSuccess += HandleAuth, 
///                           () => AuthEvents.OnAuthenticationSuccess -= HandleAuth);
/// }
/// </summary>
public class EventSubscription : IDisposable
{
    private readonly List<Action> unsubscribeActions = new List<Action>();

    /// <summary>
    /// 이벤트 구독 (subscribe와 unsubscribe 액션을 모두 제공)
    /// </summary>
    public void Subscribe(Action subscribeAction, Action unsubscribeAction)
    {
        subscribeAction?.Invoke();
        unsubscribeActions.Add(unsubscribeAction);
    }

    /// <summary>
    /// 모든 구독 해제
    /// </summary>
    public void Dispose()
    {
        foreach (var unsubscribe in unsubscribeActions)
        {
            unsubscribe?.Invoke();
        }
        unsubscribeActions.Clear();
    }
}
