using System;

/// <summary>
/// 인증 시스템 데이터 클래스 모음
/// 
/// 이 파일은 다음을 포함합니다:
/// - 사용자 데이터
/// - 디바이스 인증 데이터
/// - 로그인/로그아웃 데이터
/// - 결과 및 퀴즈 데이터
/// </summary>

#region User Data
[Serializable]
public class UserData
{
    public int idx;
    public string username;
    public string grade;
}

[Serializable]
public class UserInfo
{
    public int userID;
    public string runUser;
}
#endregion

#region Device Authentication
[Serializable]
public class DeviceRequest
{
    public string deviceSN;
    public string cocoModule;
}

[Serializable]
public class DeviceUID
{
    public string deviceNum;
    public string deviceSerial;
}

[Serializable]
public class DeviceUUID
{
    public string deviceSN;
    public string deviceUUID;
}

[Serializable]
public class DeviceResponseData
{
    public string orgID;
    public string mgtNo;
    public int licCHUNA;
}

[Serializable]
public class RegistData
{
    public string result;
    public string message;
}

[Serializable]
public class RunStatus
{
    public string deviceSN;
    public string status;
}
#endregion

#region Login/Logout
[Serializable]
public class LogonData
{
    public string deviceSN;
    public string status;
    public string runUser;
    public string runContents;
    public string deviceInfo;
}

[Serializable]
public class LogoffData
{
    public string deviceSN;
    public string status;
    public string runUser;
    public string runContents;
    public string deviceInfo;
}

[Serializable]
public class MirroringData
{
    public string serverIP;
    public int portNo;
    public string videoQuality;
    public string mirroring;
}
#endregion

#region Result Data
[Serializable]
public class ResultData
{
    public string orgID;
    public int userId;
    public string username;
    public string subject;
    public string competenyUnit;
    public string learnModule;
    public string learnLevel1;
    public string learnLevel2;
    public string totalCnt;
    public string doneCnt;
    public string runtime;
}
#endregion

#region Quiz Data
[Serializable]
public class QuizData
{
    public string system;
    public string question;
    public string answer;
    public int score;
}

[Serializable]
public class RequestQuizData
{
    public string orgID;
    public string version;
}
#endregion