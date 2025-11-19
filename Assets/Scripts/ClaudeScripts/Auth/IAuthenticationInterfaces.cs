using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

/// <summary>
/// 인증 서비스 인터페이스
/// - Mock 서비스 구현 가능
/// - 테스트 용이
/// - 다른 인증 방식 교체 가능 (Google, Facebook 등)
/// </summary>
public interface IAuthenticationService
{
    #region Device Authentication
    /// <summary>
    /// 디바이스 UUID 인증
    /// </summary>
    UniTask<DeviceResponseData> AuthenticateDeviceAsync(string deviceSN = null);

    /// <summary>
    /// 디바이스 초기화
    /// </summary>
    UniTask ResetDeviceAsync(string deviceSN, string cocoModule);

    /// <summary>
    /// 실행 상태 업데이트
    /// </summary>
    UniTask<bool> UpdateRunStatusAsync(string deviceSN, string status);
    #endregion

    #region User Management
    /// <summary>
    /// 조직의 사용자 목록 가져오기
    /// </summary>
    UniTask<UserData[]> GetUserListAsync(string orgID);
    #endregion

    #region Login/Logout
    /// <summary>
    /// 로그온 처리
    /// </summary>
    UniTask<MirroringData> LogonAsync(string deviceSN, string runUser, string runContents);

    /// <summary>
    /// 로그오프 처리
    /// </summary>
    UniTask LogoffAsync(string deviceSN, string runUser, string runContents);
    #endregion

    #region Data Management
    /// <summary>
    /// 퀴즈 데이터 가져오기
    /// </summary>
    UniTask<QuizData[]> GetQuizDataAsync(string orgID, string contentType, string version);

    /// <summary>
    /// 결과 데이터 전송
    /// </summary>
    UniTask PostResultAsync(ResultData resultData);
    #endregion

    #region Utility
    /// <summary>
    /// 모든 진행 중인 요청 취소
    /// </summary>
    void CancelAllRequests();

    /// <summary>
    /// API URL 변경
    /// </summary>
    void SetBaseApiUrl(string newUrl);
    #endregion
}

/// <summary>
/// UI 인터페이스 (테스트 및 확장성)
/// </summary>
public interface IAuthUI
{
    #region Screen Management
    void ShowAuthInputScreen();
    void ShowAuthSuccessScreen();
    void ShowAuthFailScreen(string errorMessage = "");
    void ShowLicenseErrorScreen();
    void ShowUserSelectionPopup(bool show);
    void ShowLoading(bool show);
    #endregion

    #region Input Handling
    string GetAuthInput();
    void ClearAuthInput();
    #endregion

    #region Login Button
    void SetLoginButtonEnabled(bool enabled);
    void BindLoginButton(UserInfo userInfo);
    void ClearLoginButton();
    #endregion

    #region User List UI
    void SetGradeTapVisible(bool visible);
    void ClearGradeList();
    void ClearAllUserListUI();
    void ActivateFirstTab();
    #endregion

    #region Events
    event Action<string> OnAuthNumberEntered;
    event Action OnAuthResetRequested;
    event Action<UserInfo> OnUserSelected;
    event Action OnLogoffRequested;
    #endregion

    #region Public Data Access
    List<string> grade { get; }
    List<UnityEngine.GameObject> Taps { get; }
    List<UnityEngine.GameObject> contents { get; }
    int classAmount { get; set; }
    UnityEngine.Transform GetViewport();
    UnityEngine.Transform GetGradeTapParent();
    UnityEngine.UI.ToggleGroup GetToggleGroup();
    UnityEngine.GameObject gradeTap { get; }
    UnityEngine.GameObject userButton { get; }
    UnityEngine.GameObject userListScrollGameObject { get; }
    #endregion
}

/// <summary>
/// 오브젝트 풀 인터페이스
/// </summary>
public interface IObjectPool<T> where T : class
{
    T Get();
    void Return(T item);
    void Clear();
    int Count { get; }
}
