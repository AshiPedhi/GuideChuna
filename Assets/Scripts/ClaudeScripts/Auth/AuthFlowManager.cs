using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 디바이스 인증 및 사용자 목록 관리
/// LobbyAuthUI_Complete에서 추출된 헬퍼 클래스
/// </summary>
public class AuthFlowManager
{
    private readonly IAuthenticationService authService;

    public AuthFlowManager(IAuthenticationService authService)
    {
        this.authService = authService;
    }

    /// <summary>
    /// 디바이스 인증 (재시도 로직 포함)
    /// </summary>
    /// <returns>인증 성공 시 (deviceSN, orgID, licenseValid) 반환</returns>
    public async UniTask<(string deviceSN, string orgID, bool licenseValid)> AuthenticateDeviceWithRetry(
        string savedDeviceSN, int maxRetries = 2)
    {
        return await AuthenticateDeviceInternal(savedDeviceSN, null, 0, maxRetries);
    }

    private async UniTask<(string deviceSN, string orgID, bool licenseValid)> AuthenticateDeviceInternal(
        string savedDeviceSN, string deviceSN, int retryCount, int maxRetries)
    {
        try
        {
            var deviceData = await authService.AuthenticateDeviceAsync(deviceSN);

            if (deviceData == null)
            {
                throw new Exception("인증 응답 데이터가 null입니다.");
            }

            string resultDeviceSN = deviceSN ?? SystemInfo.deviceUniqueIdentifier;
            string orgID = deviceData.orgID;

            ChunaLogger.Log($"[AuthFlow] 인증 성공: DeviceSN={resultDeviceSN}, OrgID={orgID}");

            bool licenseValid = deviceData.licCHUNA > 0;
            return (resultDeviceSN, orgID, licenseValid);
        }
        catch (Exception e)
        {
            ChunaLogger.LogError($"[AuthFlow] 인증 실패 (시도 {retryCount + 1}/{maxRetries + 1}): {e.Message} | DeviceSN: {deviceSN ?? "AUTO"} | UUID: {SystemInfo.deviceUniqueIdentifier}");

            // "등록된 장치입니다" 오류 시 UUID 앞 10글자로 재시도
            if (e.Message.Contains("등록된 장치입니다"))
            {
                string uuidSubstring = SystemInfo.deviceUniqueIdentifier.Substring(0, 10);
                ChunaLogger.Log($"[AuthFlow] 등록된 장치 감지 - UUID 앞 10글자로 재시도: {uuidSubstring}");
                return await AuthenticateDeviceInternal(savedDeviceSN, uuidSubstring, 0, maxRetries);
            }

            if (retryCount < maxRetries)
            {
                await UniTask.Delay(1000);
                return await AuthenticateDeviceInternal(savedDeviceSN, savedDeviceSN, retryCount + 1, maxRetries);
            }

            throw;
        }
    }

    /// <summary>
    /// 사용자 목록 로드
    /// </summary>
    public async UniTask<UserData[]> LoadUserList(string orgID)
    {
        ChunaLogger.Log($"[AuthFlow] 사용자 목록 로드 시작: {orgID}");

        var users = await authService.GetUserListAsync(orgID);

        if (users == null || users.Length == 0)
        {
            ChunaLogger.LogWarning("[AuthFlow] 사용자 목록이 비어있습니다.");
            return users;
        }

        ChunaLogger.Log($"[AuthFlow] 사용자 목록 로드 완료: {users.Length}명");
        return users;
    }

    /// <summary>
    /// 사용자 목록을 조별로 분류
    /// </summary>
    public Dictionary<string, List<UserData>> OrganizeByGrade(UserData[] users)
    {
        var result = new Dictionary<string, List<UserData>>();

        if (users == null) return result;

        foreach (var user in users)
        {
            if (!result.ContainsKey(user.grade))
            {
                result[user.grade] = new List<UserData>();
            }
            result[user.grade].Add(user);
        }

        ChunaLogger.Log($"[AuthFlow] 조별 분류 완료: {result.Count}개 조");
        return result;
    }

    /// <summary>
    /// 로그인 수행
    /// </summary>
    /// <returns>미러링 데이터 (없을 수 있음)</returns>
    public async UniTask<MirroringData> PerformLogin(string deviceSN, string username)
    {
        ChunaLogger.Log($"[AuthFlow] 로그인 시작: {username}");

        MirroringData mirroringData = null;
        try
        {
            mirroringData = await authService.LogonAsync(deviceSN, username, "VR_CHUNA");
            ChunaLogger.Log($"[AuthFlow] 미러링 데이터 수신 완료: {mirroringData?.serverIP}:{mirroringData?.portNo}");
        }
        catch (Exception logonException)
        {
            // 404 에러(serverIP not found)는 경고로 처리하고 로그인 진행
            if (logonException.Message.Contains("404") || logonException.Message.Contains("serverIP not found"))
            {
                ChunaLogger.LogWarning($"[AuthFlow] 미러링 정보 없음 (무시하고 진행): {logonException.Message}");
            }
            else
            {
                throw;
            }
        }

        return mirroringData;
    }

    /// <summary>
    /// 로그아웃 수행
    /// </summary>
    public async UniTask PerformLogout(string deviceSN, string username)
    {
        ChunaLogger.Log($"[AuthFlow] 로그아웃 시작: {username}");

        try
        {
            await authService.LogoffAsync(deviceSN, username, "VR_CHUNA");
            ChunaLogger.Log("[AuthFlow] 로그아웃 API 호출 성공");
        }
        catch (Exception e)
        {
            // 로그아웃 실패는 무시 (이미 로그아웃 상태이거나 네트워크 오류일 수 있음)
            ChunaLogger.LogWarning($"[AuthFlow] 로그아웃 API 실패 (무시하고 진행): {e.Message}");
        }
    }

    /// <summary>
    /// 디바이스 초기화
    /// </summary>
    public async UniTask ResetDevice(string deviceSN)
    {
        ChunaLogger.Log($"[AuthFlow] 디바이스 초기화 시작: {deviceSN}");
        await authService.ResetDeviceAsync(deviceSN, "VR_CHUNA");
        ChunaLogger.Log("[AuthFlow] 디바이스 초기화 완료");
    }
}
