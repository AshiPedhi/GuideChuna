using UnityEngine;

/// <summary>
/// PlayerPrefs 기반 로그인 상태 관리
/// LobbyAuthUI_Complete에서 추출된 헬퍼 클래스
/// </summary>
public class LoginStateStore
{
    public struct LoginInfo
    {
        public string username;
        public int userID;
        public bool hasData;
    }

    public void SaveLoginInfo(string username, int userID)
    {
        PlayerPrefs.SetString(PrefsKeys.LoginUsername, username);
        PlayerPrefs.SetInt(PrefsKeys.LoginUserID, userID);
        PlayerPrefs.Save();
        ChunaLogger.Log($"[LoginState] 로그인 정보 저장: {username} (ID: {userID})");
    }

    public LoginInfo LoadLoginInfo()
    {
        if (PlayerPrefs.HasKey(PrefsKeys.LoginUsername))
        {
            var info = new LoginInfo
            {
                username = PlayerPrefs.GetString(PrefsKeys.LoginUsername),
                userID = PlayerPrefs.GetInt(PrefsKeys.LoginUserID, 0),
                hasData = true
            };
            ChunaLogger.Log($"[LoginState] 저장된 로그인 정보 로드: {info.username} (ID: {info.userID})");
            return info;
        }

        ChunaLogger.Log("[LoginState] 저장된 로그인 정보 없음");
        return new LoginInfo { username = string.Empty, userID = 0, hasData = false };
    }

    public void ClearLoginInfo()
    {
        PlayerPrefs.DeleteKey(PrefsKeys.LoginUsername);
        PlayerPrefs.DeleteKey(PrefsKeys.LoginUserID);
        PlayerPrefs.Save();
        ChunaLogger.Log("[LoginState] 저장된 로그인 정보 삭제");
    }

    public void SaveDeviceSN(string deviceSN)
    {
        PlayerPrefs.SetString(PrefsKeys.DeviceSN, deviceSN);
        PlayerPrefs.Save();
        ChunaLogger.Log($"[LoginState] DeviceSN 저장: {deviceSN}");
    }

    public string LoadDeviceSN()
    {
        if (PlayerPrefs.HasKey(PrefsKeys.DeviceSN))
        {
            string sn = PlayerPrefs.GetString(PrefsKeys.DeviceSN);
            ChunaLogger.Log($"[LoginState] 저장된 DeviceSN 로드: {sn}");
            return sn;
        }

        ChunaLogger.Log("[LoginState] 저장된 DeviceSN 없음");
        return string.Empty;
    }

    public void ClearDeviceSN()
    {
        PlayerPrefs.DeleteKey(PrefsKeys.DeviceSN);
        PlayerPrefs.Save();
        ChunaLogger.Log("[LoginState] 저장된 DeviceSN 삭제");
    }

    public void SaveOrgID(string orgID)
    {
        PlayerPrefs.SetString(PrefsKeys.OrgID, orgID ?? string.Empty);
        PlayerPrefs.Save();
        ChunaLogger.Log($"[LoginState] OrgID 저장: {orgID}");
    }

    public string LoadOrgID()
    {
        if (PlayerPrefs.HasKey(PrefsKeys.OrgID))
        {
            string id = PlayerPrefs.GetString(PrefsKeys.OrgID);
            ChunaLogger.Log($"[LoginState] 저장된 OrgID 로드: {id}");
            return id;
        }

        ChunaLogger.Log("[LoginState] 저장된 OrgID 없음");
        return string.Empty;
    }

    public void ClearOrgID()
    {
        PlayerPrefs.DeleteKey(PrefsKeys.OrgID);
        PlayerPrefs.Save();
        ChunaLogger.Log("[LoginState] 저장된 OrgID 삭제");
    }

    public void ClearAllData()
    {
        PlayerPrefs.DeleteKey(PrefsKeys.LoginUsername);
        PlayerPrefs.DeleteKey(PrefsKeys.LoginUserID);
        PlayerPrefs.DeleteKey(PrefsKeys.DeviceSN);
        PlayerPrefs.DeleteKey(PrefsKeys.OrgID);
        PlayerPrefs.Save();
        ChunaLogger.Log("[LoginState] 모든 저장 데이터 초기화 완료");
    }
}
