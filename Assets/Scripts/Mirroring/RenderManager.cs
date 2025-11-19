using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.RenderStreaming;
using Cysharp.Threading.Tasks;
using System;

/// <summary>
/// VR 최적화 RenderManager - AuthenticationService 통합 버전
/// 
/// [주요 기능]
/// - AuthEvents를 통한 로그인/로그아웃 감지
/// - 게스트 모드와 로그인 모드 자동 전환
/// - 메모리 최적화 (VR 환경 특화)
/// - 미러링 데이터 동적 업데이트
/// </summary>
public class RenderManager : MonoBehaviour
{
    public static RenderManager instance = null;

    [Header("Render Streaming Components")]
    public SignalingManager sm;
    public VideoStreamSender vss;

    [Header("게스트 모드 설정")]
    public string guestServerIP = "localhost";
    public int guestPortNo = 80;
    public string guestVideoQuality = "low";
    public bool enableGuestMode = true;

    [Header("디버그")]
    [SerializeField] private bool enableDebugLogs = true;

    private MirroringData currentMirroringData;
    private bool isGuestMode = true;
    private bool hasInitialized = false;
    private Coroutine runCoroutine;

    // 메모리 최적화를 위한 재사용 가능한 리스트
    private List<IceServer> _cachedIceServers;

    #region Unity Lifecycle
    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            _cachedIceServers = new List<IceServer>(4);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    private void Start()
    {
        SubscribeToAuthEvents();
        StartCoroutine(DelayedInit());
    }

    private IEnumerator DelayedInit()
    {
        yield return new WaitForSeconds(0.5f);

        // 게스트 모드가 활성화되어 있으면 자동 시작
        if (enableGuestMode)
        {
            LogDebug("게스트 모드로 미러링 시작");
            StartGuestMode();
        }
    }

    private void OnDestroy()
    {
        UnsubscribeFromAuthEvents();

        if (runCoroutine != null)
        {
            StopCoroutine(runCoroutine);
            runCoroutine = null;
        }

        _cachedIceServers?.Clear();
        _cachedIceServers = null;

        if (instance == this)
        {
            instance = null;
        }
    }
    #endregion

    #region Auth Events Subscription
    private void SubscribeToAuthEvents()
    {
        // 로그인 성공 시
        AuthEvents.OnLoginSuccess += OnLoginSuccess;

        // 로그아웃 시
        AuthEvents.OnLogoutCompleted += OnLogoutCompleted;

        LogDebug("AuthEvents 구독 완료");
    }

    private void UnsubscribeFromAuthEvents()
    {
        AuthEvents.OnLoginSuccess -= OnLoginSuccess;
        AuthEvents.OnLogoutCompleted -= OnLogoutCompleted;

        LogDebug("AuthEvents 구독 해제");
    }
    #endregion

    #region Event Handlers
    /// <summary>
    /// 로그인 성공 시 호출됨
    /// </summary>
    private void OnLoginSuccess(string username, int userID)
    {
        LogDebug($"로그인 감지: {username} (ID: {userID})");

        // 미러링 데이터를 가져와서 사용자 모드로 전환
        SwitchToUserModeAsync(username).Forget();
    }

    /// <summary>
    /// 로그아웃 시 호출됨
    /// </summary>
    private void OnLogoutCompleted(string username)
    {
        LogDebug($"로그아웃 감지: {username}");

        // 게스트 모드로 복귀
        if (enableGuestMode)
        {
            StopMirroring();
            StartGuestMode();
        }
        else
        {
            StopMirroring();
        }
    }
    #endregion

    #region Guest Mode
    /// <summary>
    /// 게스트 모드로 미러링 시작
    /// </summary>
    public void StartGuestMode()
    {
        isGuestMode = true;

        currentMirroringData = new MirroringData
        {
            serverIP = guestServerIP,
            portNo = guestPortNo,
            videoQuality = guestVideoQuality,
            mirroring = "on"
        };

        StartMirroring();
    }
    #endregion

    #region User Mode
    /// <summary>
    /// 사용자 모드로 전환 (비동기)
    /// </summary>
    private async UniTaskVoid SwitchToUserModeAsync(string username)
    {
        try
        {
            // 현재 실행 중인 미러링 중지
            StopMirroring();

            // AuthenticationService에서 로그온 데이터가 이미 처리되었으므로
            // LobbyAuthUI나 다른 매니저에서 미러링 데이터를 가져와야 함
            // 여기서는 임시로 대기 후 재시작
            await UniTask.Delay(500);

            // 외부에서 SetMirroringData를 호출해줘야 함
            LogDebug($"사용자 모드 전환 대기 중: {username}");
        }
        catch (Exception e)
        {
            LogError($"사용자 모드 전환 실패: {e.Message}");

            // 실패 시 게스트 모드로 복귀
            if (enableGuestMode)
            {
                StartGuestMode();
            }
        }
    }

    /// <summary>
    /// 외부에서 미러링 데이터를 설정 (로그인 후 호출)
    /// </summary>
    public void SetMirroringData(MirroringData mirroringData)
    {
        if (mirroringData == null)
        {
            LogWarning("미러링 데이터가 null입니다.");
            return;
        }

        // mirroring이 off면 중지
        if (mirroringData.mirroring == "off")
        {
            LogDebug("미러링 off 설정됨");
            StopMirroring();
            return;
        }

        currentMirroringData = mirroringData;
        isGuestMode = false;

        LogDebug($"미러링 데이터 설정: {mirroringData.serverIP}:{mirroringData.portNo}");
        StartMirroring();
    }
    #endregion

    #region Mirroring Control
    /// <summary>
    /// 미러링 시작
    /// </summary>
    private void StartMirroring()
    {
        if (currentMirroringData == null)
        {
            LogError("미러링 데이터가 없습니다.");
            return;
        }

        // 기존 실행 중지
        if (runCoroutine != null)
        {
            StopCoroutine(runCoroutine);
            runCoroutine = null;
        }

        // Signaling 설정
        SetupSignaling(currentMirroringData.serverIP, currentMirroringData.portNo);

        // 품질 설정
        SetQuality(currentMirroringData.videoQuality);

        // 실행
        runCoroutine = StartCoroutine(Run());

        LogDebug($"미러링 시작: {currentMirroringData.serverIP}:{currentMirroringData.portNo} | 모드: {(isGuestMode ? "게스트" : "로그인")} | 품질: {currentMirroringData.videoQuality}");
    }

    /// <summary>
    /// 미러링 중지
    /// </summary>
    public void StopMirroring()
    {
        if (runCoroutine != null)
        {
            StopCoroutine(runCoroutine);
            runCoroutine = null;
        }

        LogDebug("미러링 중지됨");
    }
    #endregion

    #region Signaling Setup
    private void SetupSignaling(string serverIP, int port)
    {
        if (sm == null)
        {
            LogError("SignalingManager가 null입니다.");
            return;
        }

        // IceServer 리스트 재사용 (메모리 할당 최소화)
        _cachedIceServers.Clear();
        var iceServerEnumerator = sm.GetSignalingSettings().iceServers.GetEnumerator();
        while (iceServerEnumerator.MoveNext())
        {
            _cachedIceServers.Add(iceServerEnumerator.Current);
        }

        var wss = new WebSocketSignalingSettings($"ws://{serverIP}:{port}", _cachedIceServers.ToArray());
        sm.SetSignalingSettings(wss);
    }
    #endregion

    #region Quality Settings
    void SetQuality(string streamingQuality)
    {
        if (vss == null || !vss.isActiveAndEnabled)
        {
            LogError("VideoStreamSender가 null이거나 비활성화되었습니다.");
            return;
        }

        // VR 최적화: 기본값을 낮은 품질로 설정
        Vector2Int t_size = new Vector2Int(640, 360);
        float fr = 15f;
        uint min_bitrate = 0;
        uint max_bitrate = 500;
        float resolution_lower = 2.0f;

        switch (streamingQuality?.ToLower())
        {
            case "low":
                t_size = new Vector2Int(480, 270);
                fr = 15f;
                min_bitrate = 0;
                max_bitrate = 300;
                resolution_lower = 2.5f;
                break;
            case "med":
            case "medium":
                t_size = new Vector2Int(1280, 720);
                fr = 30f;
                min_bitrate = 0;
                max_bitrate = 1000;
                resolution_lower = 1.0f;
                break;
            case "high":
                t_size = new Vector2Int(1920, 1080);
                fr = 30f;
                min_bitrate = 0;
                max_bitrate = 2000;
                resolution_lower = 1.0f;
                break;
        }

        try
        {
            vss.SetTextureSize(t_size);
            vss.SetFrameRate(fr);
            vss.SetBitrate(min_bitrate, max_bitrate);
            vss.SetScaleResolutionDown(resolution_lower);

            LogDebug($"스트리밍 품질 설정: {streamingQuality} | 해상도: {t_size} | FPS: {fr}");
        }
        catch (Exception e)
        {
            LogError($"품질 설정 실패: {e.Message}");
        }
    }
    #endregion

    #region Run
    IEnumerator Run()
    {
        if (vss == null || !vss.isActiveAndEnabled)
        {
            LogError("VideoStreamSender가 null이거나 비활성화되었습니다.");
            yield break;
        }

        yield return null;
        RunAsync().Forget();
    }

    private async UniTask RunAsync()
    {
        if (vss == null || !vss.isActiveAndEnabled)
        {
            LogError("VideoStreamSender가 null이거나 비활성화되었습니다.");
            return;
        }

        try
        {
            sm.Run();

            // 타임아웃 추가로 무한 대기 방지 (VR에서 중요)
            var cts = this.GetCancellationTokenOnDestroy();
            var timeoutTask = UniTask.Delay(TimeSpan.FromSeconds(10), cancellationToken: cts);
            var waitTask = UniTask.WaitUntil(() => vss.Track != null, cancellationToken: cts);

            var result = await UniTask.WhenAny(waitTask, timeoutTask);

            if (result == 0) // waitTask 완료
            {
                LogDebug($"🎥 Track 생성 완료! ID: {vss.Track.Id}");
                hasInitialized = true;
            }
            else // 타임아웃
            {
                LogWarning("Track 생성 타임아웃 (10초)");
            }
        }
        catch (OperationCanceledException)
        {
            LogDebug("RunAsync 취소됨 (정상 종료)");
        }
        catch (Exception e)
        {
            LogError($"RunAsync 실패: {e.Message}\n{e.StackTrace}");
        }
    }
    #endregion

    #region Public API
    /// <summary>
    /// 현재 미러링 정보 가져오기
    /// </summary>
    public void GetCurrentMirroringInfo(out string serverIP, out int port, out string quality, out bool isGuest)
    {
        if (currentMirroringData != null)
        {
            serverIP = currentMirroringData.serverIP;
            port = currentMirroringData.portNo;
            quality = currentMirroringData.videoQuality;
        }
        else
        {
            serverIP = "None";
            port = 0;
            quality = "None";
        }

        isGuest = isGuestMode;
    }

    /// <summary>
    /// 미러링 활성화 여부
    /// </summary>
    public bool IsMirroringActive()
    {
        return runCoroutine != null && hasInitialized;
    }
    #endregion

    #region Logging
    private void LogDebug(string message)
    {
        if (enableDebugLogs)
        {
            Debug.Log($"[RenderManager] {message}");
        }
    }

    private void LogWarning(string message)
    {
        if (enableDebugLogs)
        {
            Debug.LogWarning($"[RenderManager] {message}");
        }
    }

    private void LogError(string message)
    {
        Debug.LogError($"[RenderManager] {message}");
    }
    #endregion
}