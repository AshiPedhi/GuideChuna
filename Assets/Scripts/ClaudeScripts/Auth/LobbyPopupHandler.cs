using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로비 팝업 관리 (로그인 필요, 종료 확인, 로그아웃 확인)
/// LobbyAuthUI_Complete에서 추출된 헬퍼 클래스
/// </summary>
public class LobbyPopupHandler
{
    private readonly GameObject loginRequiredPopup;
    private readonly GameObject exitConfirmationPopup;
    private readonly GameObject logoutConfirmationPopup;

    public LobbyPopupHandler(
        GameObject loginRequiredPopup,
        GameObject exitConfirmationPopup,
        GameObject logoutConfirmationPopup)
    {
        this.loginRequiredPopup = loginRequiredPopup;
        this.exitConfirmationPopup = exitConfirmationPopup;
        this.logoutConfirmationPopup = logoutConfirmationPopup;
    }

    #region Login Required Popup
    public void ShowLoginRequiredPopup()
    {
        if (loginRequiredPopup != null)
        {
            loginRequiredPopup.SetActive(true);
            ChunaLogger.Log("[Popup] 로그인 필요 팝업 표시");
        }
        else
        {
            ChunaLogger.LogWarning("[Popup] loginRequiredPopup이 연결되지 않았습니다.");
        }
    }

    public void HideLoginRequiredPopup()
    {
        if (loginRequiredPopup != null)
        {
            loginRequiredPopup.SetActive(false);
            ChunaLogger.Log("[Popup] 로그인 필요 팝업 닫기");
        }
    }
    #endregion

    #region Exit Confirmation Popup
    public void ShowExitConfirmPopup()
    {
        if (exitConfirmationPopup != null)
        {
            exitConfirmationPopup.SetActive(true);
            ChunaLogger.Log("[Popup] 종료 확인 팝업 표시");
        }
        else
        {
            ChunaLogger.LogWarning("[Popup] exitConfirmationPopup이 연결되지 않았습니다.");
        }
    }

    public void HideExitConfirmPopup()
    {
        if (exitConfirmationPopup != null)
        {
            exitConfirmationPopup.SetActive(false);
            ChunaLogger.Log("[Popup] 종료 확인 팝업 닫기");
        }
    }
    #endregion

    #region Logout Confirmation Popup
    public void ShowLogoutConfirmPopup()
    {
        ChunaLogger.Log($"[Popup] ShowLogoutConfirmPopup 호출 - logoutConfirmationPopup: {(logoutConfirmationPopup != null ? "연결됨" : "NULL")}");

        if (logoutConfirmationPopup != null)
        {
            logoutConfirmationPopup.SetActive(true);
            ChunaLogger.Log("[Popup] 로그아웃 확인 팝업 표시 완료");
        }
        else
        {
            ChunaLogger.LogError("[Popup] logoutConfirmationPopup이 NULL입니다! Inspector에서 연결되어 있는지 확인하세요.");
        }
    }

    public void HideLogoutConfirmPopup()
    {
        if (logoutConfirmationPopup != null)
        {
            logoutConfirmationPopup.SetActive(false);
            ChunaLogger.Log("[Popup] 로그아웃 확인 팝업 닫기");
        }
    }
    #endregion

    public void HideAllPopups()
    {
        HideLoginRequiredPopup();
        HideExitConfirmPopup();
        HideLogoutConfirmPopup();
    }

    public void InitializePopups()
    {
        loginRequiredPopup?.SetActive(false);
        exitConfirmationPopup?.SetActive(false);
        logoutConfirmationPopup?.SetActive(false);
    }
}
