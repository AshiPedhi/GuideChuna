using UnityEngine;
using System;
using System.Net;
using System.Text;
using System.Linq;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Cysharp.Threading.Tasks;

/// <summary>
/// Meta Quest 최적화 AuthenticationService - 완전판
/// 
/// [개선 사항]
/// - IAuthenticationService 인터페이스 구현
/// - AuthEvents 이벤트 시스템 통합
/// - Dependency Injection 지원
/// - Mock 테스트 가능
/// </summary>
public class AuthenticationService : MonoBehaviour, IAuthenticationService
{
    #region Singleton
    private static AuthenticationService instance;
    public static AuthenticationService Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("[AuthenticationService]");
                instance = go.AddComponent<AuthenticationService>();
                DontDestroyOnLoad(go);
            }
            return instance;
        }
    }
    #endregion

    #region Configuration
    [Header("API Configuration")]
    [SerializeField] private string baseApiUrl = "https://qpqjpivcg1.execute-api.ap-northeast-2.amazonaws.com";

    [Header("Network Settings")]
    [SerializeField] [Range(5, 30)] private int requestTimeout = 10;
    [SerializeField] [Range(1, 5)] private int maxRetryCount = 3;

    [Header("Debug")]
    [SerializeField] private bool enableDebugLogs = true;
    #endregion

    #region Meta Quest Optimization
    private StringBuilder urlBuilder = new StringBuilder(256);
    private System.Threading.CancellationTokenSource requestCts;
    #endregion

    #region Unity Lifecycle
    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (instance != this)
        {
            Destroy(gameObject);
            return;
        }

        requestCts = new System.Threading.CancellationTokenSource();
        LogDebug("AuthenticationService 초기화 완료");
    }

    private void OnDestroy()
    {
        requestCts?.Cancel();
        requestCts?.Dispose();
    }
    #endregion

    #region IAuthenticationService Implementation - Device
    public async UniTask<DeviceResponseData> AuthenticateDeviceAsync(string deviceSN = null)
    {
        var deviceUUID = new DeviceUUID
        {
            deviceSN = deviceSN ?? string.Empty,
            deviceUUID = SystemInfo.deviceUniqueIdentifier
        };

        string url = BuildUrl("/device/auth/chuna");
        string json = JsonConvert.SerializeObject(deviceUUID);

        try
        {
            AuthEvents.TriggerAuthenticationStarted(deviceSN ?? "AUTO");
            LogDebug($"디바이스 인증 시작: {deviceSN ?? "AUTO"}");

            var result = await PostAsync<DeviceResponseData>(url, json);

            AuthEvents.TriggerAuthenticationSuccess(deviceSN ?? "AUTO");
            LogDebug($"디바이스 인증 성공: OrgID={result.orgID}");

            return result;
        }
        catch (Exception e)
        {
            AuthEvents.TriggerAuthenticationFailed(e.Message);
            LogError($"디바이스 인증 실패: {e.Message} | DeviceSN: {deviceSN ?? "AUTO"} | UUID: {SystemInfo.deviceUniqueIdentifier}");
            throw;
        }
    }

    public async UniTask ResetDeviceAsync(string deviceSN, string cocoModule)
    {
        var data = new DeviceRequest
        {
            deviceSN = deviceSN,
            cocoModule = cocoModule
        };

        string url = BuildUrl("/device/regist/reset");
        string json = JsonConvert.SerializeObject(data);

        try
        {
            LogDebug($"디바이스 초기화 시작: {deviceSN}");
            await PostAsync<string>(url, json);

            AuthEvents.TriggerDeviceReset();
            LogDebug("디바이스 초기화 완료");
        }
        catch (Exception e)
        {
            AuthEvents.TriggerError($"디바이스 초기화 실패: {e.Message}", e);
            LogError($"디바이스 초기화 실패: {e.Message}");
            throw;
        }
    }

    public async UniTask<bool> UpdateRunStatusAsync(string deviceSN, string status)
    {
        var statusData = new RunStatus
        {
            deviceSN = deviceSN,
            status = status
        };

        string url = BuildUrl("/device/update/runstatus");
        string json = JsonConvert.SerializeObject(statusData);

        try
        {
            LogDebug($"상태 업데이트: {status}");

            string responseText = await PostAsyncRaw(url, json);
            var parsed = JObject.Parse(responseText);
            string result = parsed["result"]?.ToString()?.Trim();

            bool success = string.Equals(result, "success", StringComparison.OrdinalIgnoreCase);

            if (success)
            {
                LogDebug($"상태 업데이트 성공: {status}");
            }
            else
            {
                LogWarning($"상태 업데이트 실패: {result}");
            }

            return success;
        }
        catch (Exception e)
        {
            AuthEvents.TriggerNetworkError($"상태 업데이트 오류: {e.Message}");
            LogError($"상태 업데이트 예외: {e.Message}");
            return false;
        }
    }
    #endregion

    #region IAuthenticationService Implementation - User
    public async UniTask<UserData[]> GetUserListAsync(string orgID)
    {
        string url = BuildUrl($"/user/userlist/{orgID}");

        try
        {
            AuthEvents.TriggerUserListLoadStarted(orgID);
            LogDebug($"사용자 목록 로드 시작: {orgID}");

            using UnityWebRequest request = UnityWebRequest.Get(url);
            request.timeout = requestTimeout;

            await request.SendWebRequest().WithCancellation(requestCts.Token);

            if (request.result != UnityWebRequest.Result.Success)
            {
                string errorMsg = $"사용자 목록 로드 실패: {request.downloadHandler.text}";
                AuthEvents.TriggerUserListLoadFailed(errorMsg);
                throw new Exception(errorMsg);
            }

            var result = JsonConvert.DeserializeObject<UserData[]>(request.downloadHandler.text);

            AuthEvents.TriggerUserListLoadCompleted(result.Length);
            LogDebug($"사용자 목록 로드 완료: {result.Length}명");

            return result;
        }
        catch (Exception e)
        {
            AuthEvents.TriggerUserListLoadFailed(e.Message);
            LogError($"사용자 목록 로드 실패: {e.Message}");
            throw;
        }
    }
    #endregion

    #region IAuthenticationService Implementation - Login/Logout
    public async UniTask<MirroringData> LogonAsync(string deviceSN, string runUser, string runContents)
    {
        string deviceInfo = GetDeviceInfo(deviceSN);

        var data = new LogonData
        {
            deviceSN = deviceSN,
            status = "LOGON",
            runUser = runUser,
            runContents = runContents,
            deviceInfo = deviceInfo
        };

        string url = BuildUrl("/device/logon");
        string json = JsonConvert.SerializeObject(data);

        try
        {
            AuthEvents.TriggerLoginStarted(runUser);
            LogDebug($"로그온 시작: {runUser}");

            var result = await PostAsync<MirroringData>(url, json);

            // 로그온 성공은 AuthManager에서 TriggerLoginSuccess 호출
            LogDebug($"로그온 완료: {runUser}");

            return result;
        }
        catch (Exception e)
        {
            AuthEvents.TriggerLoginFailed(runUser, e.Message);
            LogError($"로그온 실패: {runUser} - {e.Message}");
            throw;
        }
    }

    public async UniTask LogoffAsync(string deviceSN, string runUser, string runContents)
    {
        var data = new LogoffData
        {
            deviceSN = deviceSN,
            status = "LOGOFF",
            runUser = runUser,
            runContents = runContents,
            deviceInfo = string.Empty
        };

        string url = BuildUrl("/device/logoff");
        string json = JsonConvert.SerializeObject(data);

        try
        {
            LogDebug($"로그오프 시작: {runUser}");

            string result = await PostAsyncRaw(url, json);

            AuthEvents.TriggerLogoutCompleted(runUser);
            LogDebug($"로그오프 완료: {runUser}");
        }
        catch (Exception e)
        {
            AuthEvents.TriggerError($"로그오프 실패: {e.Message}", e);
            LogError($"로그오프 실패: {runUser} - {e.Message}");
            throw;
        }
    }
    #endregion

    #region IAuthenticationService Implementation - Data
    public async UniTask<QuizData[]> GetQuizDataAsync(string orgID, string contentType, string version)
    {
        string url = BuildUrl($"/quiz/{contentType.ToLower()}");

        var request = new RequestQuizData
        {
            orgID = orgID,
            version = version
        };

        string json = JsonConvert.SerializeObject(request);

        try
        {
            LogDebug($"퀴즈 데이터 로드: {contentType}");

            var result = await PostAsync<QuizData[]>(url, json);

            AuthEvents.TriggerQuizDataLoaded(result.Length);
            LogDebug($"퀴즈 데이터 로드 완료: {result.Length}개");

            return result;
        }
        catch (Exception e)
        {
            AuthEvents.TriggerError($"퀴즈 로드 실패: {e.Message}", e);
            LogError($"퀴즈 로드 실패: {e.Message}");
            throw;
        }
    }

    public async UniTask PostResultAsync(ResultData resultData)
    {
        string url = BuildUrl("/result/chuna");
        string json = JsonConvert.SerializeObject(resultData);

        try
        {
            LogDebug($"결과 전송: {resultData.username}");

            await PostAsync<string>(url, json);

            AuthEvents.TriggerResultSubmitted(resultData);
            LogDebug($"결과 전송 완료: {resultData.username}");
        }
        catch (Exception e)
        {
            AuthEvents.TriggerError($"결과 전송 실패: {e.Message}", e);
            LogError($"결과 전송 실패: {e.Message}");
            throw;
        }
    }
    #endregion

    #region IAuthenticationService Implementation - Utility
    public void CancelAllRequests()
    {
        requestCts?.Cancel();
        requestCts?.Dispose();
        requestCts = new System.Threading.CancellationTokenSource();

        LogDebug("모든 요청 취소됨");
    }

    public void SetBaseApiUrl(string newUrl)
    {
        baseApiUrl = newUrl;
        LogDebug($"API URL 변경: {newUrl}");
    }
    #endregion

    #region Network Helpers
    private string BuildUrl(string endpoint)
    {
        urlBuilder.Clear();
        urlBuilder.Append(baseApiUrl).Append(endpoint);
        return urlBuilder.ToString();
    }

    private string GetDeviceInfo(string deviceSN)
    {
        var localIPs = Dns.GetHostEntry(Dns.GetHostName()).AddressList;
        string localIP = localIPs.FirstOrDefault(ip =>
            ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.ToString() ?? "Unknown";

        string snShort = deviceSN.Length >= 3
            ? deviceSN.Substring(deviceSN.Length - 3)
            : deviceSN;

        urlBuilder.Clear();
        urlBuilder.Append(localIP).Append(" / ").Append(snShort);
        return urlBuilder.ToString();
    }

    private async UniTask<T> PostAsync<T>(string url, string json)
    {
        using UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
        byte[] body = Encoding.UTF8.GetBytes(json);
        request.uploadHandler = new UploadHandlerRaw(body);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = requestTimeout;

        await request.SendWebRequest().WithCancellation(requestCts.Token);

        if (request.result != UnityWebRequest.Result.Success)
        {
            string errorMsg = $"HTTP Error: {request.responseCode}, {request.downloadHandler.text}";
            AuthEvents.TriggerNetworkError(errorMsg);
            throw new Exception(errorMsg);
        }

        return JsonConvert.DeserializeObject<T>(request.downloadHandler.text);
    }

    private async UniTask<string> PostAsyncRaw(string url, string json)
    {
        using UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
        byte[] body = Encoding.UTF8.GetBytes(json);
        request.uploadHandler = new UploadHandlerRaw(body);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = requestTimeout;

        await request.SendWebRequest().WithCancellation(requestCts.Token);

        if (request.result != UnityWebRequest.Result.Success)
        {
            string errorMsg = $"HTTP Error: {request.responseCode}, {request.downloadHandler.text}";
            AuthEvents.TriggerNetworkError(errorMsg);
            throw new Exception(errorMsg);
        }

        return request.downloadHandler.text.Trim();
    }
    #endregion

    #region Logging
    private void LogDebug(string message)
    {
#if UNITY_EDITOR
        if (enableDebugLogs)
        {
            Debug.Log($"[AuthService] {message}");
        }
#endif
    }

    private void LogWarning(string message)
    {
#if UNITY_EDITOR
        if (enableDebugLogs)
        {
            Debug.LogWarning($"[AuthService] {message}");
        }
#endif
    }

    private void LogError(string message)
    {
#if UNITY_EDITOR
        Debug.LogError($"[AuthService] {message}");
#endif
    }
    #endregion

    #region Configuration Methods
    public void SetRequestTimeout(int timeout)
    {
        requestTimeout = Mathf.Clamp(timeout, 5, 30);
        LogDebug($"타임아웃 변경: {requestTimeout}초");
    }

    public void SetMaxRetryCount(int count)
    {
        maxRetryCount = Mathf.Clamp(count, 1, 5);
        LogDebug($"최대 재시도 횟수 변경: {maxRetryCount}");
    }

    public void EnableDebugLogs(bool enable)
    {
        enableDebugLogs = enable;
    }
    #endregion
}

/// <summary>
/// Mock AuthenticationService (테스트용)
/// 
/// [사용 예시]
/// var mockService = gameObject.AddComponent<MockAuthenticationService>();
/// authManager.SetAuthenticationService(mockService);
/// </summary>
public class MockAuthenticationService : MonoBehaviour, IAuthenticationService
{
    public async UniTask<DeviceResponseData> AuthenticateDeviceAsync(string deviceSN = null)
    {
        await UniTask.Delay(100); // 네트워크 지연 시뮬레이션

        return new DeviceResponseData
        {
            orgID = "TEST_ORG",
            mgtNo = "TEST_MGT",
            licCHUNA = 1
        };
    }

    public async UniTask ResetDeviceAsync(string deviceSN, string cocoModule)
    {
        await UniTask.Delay(100);
        Debug.Log("[MockService] 디바이스 초기화");
    }

    public async UniTask<bool> UpdateRunStatusAsync(string deviceSN, string status)
    {
        await UniTask.Delay(50);
        Debug.Log($"[MockService] 상태 업데이트: {status}");
        return true;
    }

    public async UniTask<UserData[]> GetUserListAsync(string orgID)
    {
        await UniTask.Delay(200);

        return new UserData[]
        {
            new UserData { idx = 1, username = "테스트1", grade = "1학년" },
            new UserData { idx = 2, username = "테스트2", grade = "1학년" },
            new UserData { idx = 3, username = "테스트3", grade = "2학년" }
        };
    }

    public async UniTask<MirroringData> LogonAsync(string deviceSN, string runUser, string runContents)
    {
        await UniTask.Delay(150);

        return new MirroringData
        {
            serverIP = "127.0.0.1",
            portNo = 8080,
            videoQuality = "HIGH",
            mirroring = "ON"
        };
    }

    public async UniTask LogoffAsync(string deviceSN, string runUser, string runContents)
    {
        await UniTask.Delay(100);
        Debug.Log($"[MockService] 로그오프: {runUser}");
    }

    public async UniTask<QuizData[]> GetQuizDataAsync(string orgID, string contentType, string version)
    {
        await UniTask.Delay(200);

        return new QuizData[]
        {
            new QuizData { system = "TEST", question = "문제1", answer = "답1", score = 10 },
            new QuizData { system = "TEST", question = "문제2", answer = "답2", score = 20 }
        };
    }

    public async UniTask PostResultAsync(ResultData resultData)
    {
        await UniTask.Delay(100);
        Debug.Log($"[MockService] 결과 전송: {resultData.username}");
    }

    public void CancelAllRequests()
    {
        Debug.Log("[MockService] 모든 요청 취소");
    }

    public void SetBaseApiUrl(string newUrl)
    {
        Debug.Log($"[MockService] API URL 변경: {newUrl}");
    }
}