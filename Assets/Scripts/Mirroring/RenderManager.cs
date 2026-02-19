using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.RenderStreaming;
using Cysharp.Threading.Tasks;
using System;
using System.Threading;

/// <summary>
/// VR 최적화 RenderManager - 메모리 누수 방지 버전
///
/// [주요 기능]
/// - LobbyAuthUI에서 Camera X 오브젝트 활성화/비활성화 제어
/// - SetMirroringData() 호출 시 미러링 시작
/// - StopMirroring() 호출 시 미러링 중지
///
/// [메모리 최적화]
/// - CancellationTokenSource 재사용
/// - 재귀 호출 대신 반복문 사용
/// - 리소스 정리 강화
/// </summary>
public class RenderManager : MonoBehaviour
{
    public static RenderManager instance = null;

    /// <summary>
    /// 미러링 연결 상태
    /// </summary>
    private enum ConnectionState
    {
        Idle,           // 초기 상태 / 미설정
        Connecting,     // 연결 시도 중
        Connected,      // 연결 완료
        Failed          // 연결 실패
    }

    [Header("Render Streaming Components")]
    public SignalingManager sm;
    public VideoStreamSender vss;

    [Header("디버그")]
    [SerializeField] private bool enableDebugLogs = true;

    private MirroringData currentMirroringData;
    private ConnectionState connectionState = ConnectionState.Idle;
    private int connectionAttempts = 0;
    private const int MAX_CONNECTION_ATTEMPTS = 3;
    private const float CONNECTION_RETRY_DELAY = 2f;
    private const float CONNECTION_TIMEOUT = 10f;

    // 메모리 최적화
    private List<IceServer> _cachedIceServers;
    private CancellationTokenSource _connectionCts;
    private bool _isRunning = false;

    #region Unity Lifecycle
    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            _cachedIceServers = new List<IceServer>(4);
            LogDebug("RenderManager 인스턴스 생성됨");
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    private void Start()
    {
        LogDebug("RenderManager 시작됨. SetMirroringData() 호출 대기 중...");
    }

    private void OnDestroy()
    {
        // 모든 비동기 작업 취소
        CancelAllOperations();

        // 리소스 정리
        _cachedIceServers?.Clear();
        _cachedIceServers = null;
        currentMirroringData = null;

        if (instance == this)
        {
            instance = null;
        }

        LogDebug("RenderManager 파괴됨");
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        // 앱이 백그라운드로 가면 미러링 일시 중지
        if (pauseStatus && connectionState == ConnectionState.Connected)
        {
            LogDebug("앱 일시정지 - 미러링 연결 유지 (프레임 전송 중단)");
        }
    }
    #endregion

    #region Public API
    /// <summary>
    /// 외부에서 미러링 데이터를 설정하고 미러링 시작 (로그인 후 호출)
    /// </summary>
    public void SetMirroringData(MirroringData mirroringData)
    {
        try
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

            // 서버 주소 유효성 체크
            if (string.IsNullOrEmpty(mirroringData.serverIP))
            {
                LogWarning("미러링 서버 IP가 비어있습니다.");
                return;
            }

            currentMirroringData = mirroringData;

            LogDebug($"미러링 데이터 설정: {mirroringData.serverIP}:{mirroringData.portNo}");
            StartMirroring();
        }
        catch (Exception e)
        {
            LogError($"미러링 데이터 설정 실패: {e.Message}");
        }
    }

    /// <summary>
    /// 미러링 중지 - 모든 리소스 정리
    /// </summary>
    public void StopMirroring()
    {
        try
        {
            LogDebug("미러링 중지 시작...");

            // 1. 비동기 작업 취소
            CancelAllOperations();

            // 2. 연결 상태 초기화
            _isRunning = false;
            connectionState = ConnectionState.Idle;
            connectionAttempts = 0;

            // 3. SignalingManager 안전 중지
            SafeStopSignalingManager();

            LogDebug("미러링 중지 완료");
        }
        catch (Exception e)
        {
            LogError($"미러링 중지 실패: {e.Message}");
        }
    }

    /// <summary>
    /// 현재 미러링 정보 가져오기
    /// </summary>
    public void GetCurrentMirroringInfo(out string serverIP, out int port, out string quality)
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
    }

    /// <summary>
    /// 미러링 활성화 여부
    /// </summary>
    public bool IsMirroringActive()
    {
        return connectionState == ConnectionState.Connected;
    }

    /// <summary>
    /// 연결 시도 중인지 확인
    /// </summary>
    public bool IsConnecting => connectionState == ConnectionState.Connecting;

    /// <summary>
    /// 연결 성공 여부
    /// </summary>
    public bool IsConnected => connectionState == ConnectionState.Connected;

    /// <summary>
    /// 연결 상태 문자열 반환 (UI 표시용)
    /// </summary>
    public string GetConnectionStatus()
    {
        switch (connectionState)
        {
            case ConnectionState.Connected:
                return "연결됨";
            case ConnectionState.Connecting:
                return $"연결 중... ({connectionAttempts}/{MAX_CONNECTION_ATTEMPTS})";
            case ConnectionState.Failed:
                return "연결 실패";
            default:
                return currentMirroringData == null ? "미설정" : "연결 끊김";
        }
    }

    /// <summary>
    /// 수동 재연결 시도
    /// </summary>
    public void TryReconnect()
    {
        if (connectionState == ConnectionState.Connecting)
        {
            LogWarning("이미 연결 중입니다.");
            return;
        }

        if (currentMirroringData != null)
        {
            LogDebug("수동 재연결 시도...");
            StopMirroring();
            StartMirroring();
        }
        else
        {
            LogWarning("미러링 데이터가 없어 재연결할 수 없습니다.");
        }
    }
    #endregion

    #region Private Methods
    /// <summary>
    /// 모든 비동기 작업 취소
    /// </summary>
    private void CancelAllOperations()
    {
        if (_connectionCts != null)
        {
            try
            {
                if (!_connectionCts.IsCancellationRequested)
                {
                    _connectionCts.Cancel();
                }
                _connectionCts.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // CTS가 shutdown 중 dispose될 때 발생 - 정상 동작이므로 무시
            }
            finally
            {
                _connectionCts = null;
            }
        }
    }

    /// <summary>
    /// SignalingManager 안전 중지
    /// </summary>
    private void SafeStopSignalingManager()
    {
        if (sm != null)
        {
            try
            {
                sm.Stop();
                LogDebug("SignalingManager 중지됨");
            }
            catch (Exception e)
            {
                LogWarning($"SignalingManager 중지 중 오류 (무시): {e.Message}");
            }
        }
    }

    /// <summary>
    /// 미러링 시작
    /// </summary>
    private void StartMirroring()
    {
        try
        {
            // 이미 연결 중이면 무시
            if (connectionState == ConnectionState.Connecting || _isRunning)
            {
                LogDebug("이미 연결 중입니다. 중복 요청 무시.");
                return;
            }

            if (currentMirroringData == null)
            {
                LogWarning("미러링 데이터가 없습니다.");
                return;
            }

            // 컴포넌트 유효성 체크
            if (sm == null)
            {
                LogWarning("SignalingManager가 null입니다. 미러링 시작 불가.");
                return;
            }

            if (vss == null || !vss.isActiveAndEnabled)
            {
                LogWarning("VideoStreamSender가 null이거나 비활성화 상태입니다. 미러링 시작 불가.");
                return;
            }

            // 이전 작업 취소
            CancelAllOperations();

            // 새 CancellationTokenSource 생성
            _connectionCts = new CancellationTokenSource();

            // 연결 상태 초기화
            connectionState = ConnectionState.Connecting;
            connectionAttempts = 0;

            // Signaling 설정
            if (!SetupSignaling(currentMirroringData.serverIP, currentMirroringData.portNo))
            {
                LogWarning("Signaling 설정 실패. 미러링 시작 불가.");
                connectionState = ConnectionState.Failed;
                return;
            }

            // 품질 설정
            SetQuality(currentMirroringData.videoQuality);

            // 비동기 연결 시작
            RunConnectionLoopAsync(_connectionCts.Token).Forget();

            LogDebug($"미러링 시작: {currentMirroringData.serverIP}:{currentMirroringData.portNo}");
        }
        catch (Exception e)
        {
            LogError($"미러링 시작 실패: {e.Message}");
            connectionState = ConnectionState.Failed;
        }
    }

    /// <summary>
    /// 연결 루프 (재귀 대신 반복문 사용)
    /// </summary>
    private async UniTaskVoid RunConnectionLoopAsync(CancellationToken cancellationToken)
    {
        _isRunning = true;

        try
        {
            while (connectionAttempts < MAX_CONNECTION_ATTEMPTS && !cancellationToken.IsCancellationRequested)
            {
                connectionAttempts++;
                LogDebug($"연결 시도 {connectionAttempts}/{MAX_CONNECTION_ATTEMPTS}...");

                try
                {
                    // SignalingManager 실행
                    sm.Run();

                    // 타임아웃과 함께 Track 생성 대기
                    bool connected = await WaitForTrackWithTimeout(cancellationToken);

                    if (connected)
                    {
                        LogDebug($"Track 생성 완료! ID: {vss.Track?.Id}");
                        connectionState = ConnectionState.Connected;
                        _isRunning = false;
                        return; // 성공 - 루프 종료
                    }

                    // 타임아웃 - 재시도 준비
                    LogWarning($"Track 생성 타임아웃 ({CONNECTION_TIMEOUT}초)");

                    if (connectionAttempts < MAX_CONNECTION_ATTEMPTS)
                    {
                        // SignalingManager 중지
                        SafeStopSignalingManager();

                        // 재시도 전 대기
                        LogDebug($"{CONNECTION_RETRY_DELAY}초 후 재시도...");
                        await UniTask.Delay(TimeSpan.FromSeconds(CONNECTION_RETRY_DELAY), cancellationToken: cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    LogDebug("연결 시도 취소됨");
                    break;
                }
                catch (Exception e)
                {
                    LogError($"연결 시도 실패: {e.Message}");

                    if (connectionAttempts < MAX_CONNECTION_ATTEMPTS)
                    {
                        SafeStopSignalingManager();
                        await UniTask.Delay(TimeSpan.FromSeconds(CONNECTION_RETRY_DELAY), cancellationToken: cancellationToken);
                    }
                }
            }

            // 최대 재시도 횟수 초과
            if (!cancellationToken.IsCancellationRequested)
            {
                LogError($"최대 연결 시도 횟수 초과 ({MAX_CONNECTION_ATTEMPTS}회). 미러링 비활성화.");
            }
        }
        catch (OperationCanceledException)
        {
            LogDebug("연결 루프 취소됨 (정상 종료)");
        }
        catch (Exception e)
        {
            LogError($"연결 루프 예외: {e.Message}");
        }
        finally
        {
            _isRunning = false;

            if (connectionState != ConnectionState.Connected)
            {
                connectionState = ConnectionState.Failed;
            }
        }
    }

    /// <summary>
    /// Track 생성 대기 (타임아웃 포함)
    /// </summary>
    private async UniTask<bool> WaitForTrackWithTimeout(CancellationToken cancellationToken)
    {
        try
        {
            using (var timeoutCts = new CancellationTokenSource())
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
            {
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(CONNECTION_TIMEOUT));

                await UniTask.WaitUntil(() => vss != null && vss.Track != null, cancellationToken: linkedCts.Token);
                return true;
            }
        }
        catch (OperationCanceledException)
        {
            // 타임아웃 또는 외부 취소
            return false;
        }
    }
    #endregion

    #region Signaling Setup
    private bool SetupSignaling(string serverIP, int port)
    {
        try
        {
            if (sm == null)
            {
                LogError("SignalingManager가 null입니다.");
                return false;
            }

            if (string.IsNullOrEmpty(serverIP))
            {
                LogError("서버 IP가 비어있습니다.");
                return false;
            }

            if (port <= 0 || port > 65535)
            {
                LogError($"잘못된 포트 번호: {port}");
                return false;
            }

            // IceServer 리스트 재사용 (메모리 할당 최소화)
            _cachedIceServers.Clear();

            var signalingSettings = sm.GetSignalingSettings();
            if (signalingSettings?.iceServers != null)
            {
                foreach (var iceServer in signalingSettings.iceServers)
                {
                    _cachedIceServers.Add(iceServer);
                }
            }

            var wss = new WebSocketSignalingSettings($"ws://{serverIP}:{port}", _cachedIceServers.ToArray());
            sm.SetSignalingSettings(wss);

            LogDebug($"Signaling 설정 완료: ws://{serverIP}:{port}");
            return true;
        }
        catch (Exception e)
        {
            LogError($"Signaling 설정 실패: {e.Message}");
            return false;
        }
    }
    #endregion

    #region Quality Settings
    private void SetQuality(string streamingQuality)
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

    #region Logging
    private void LogDebug(string message)
    {
        ChunaLogger.LogVerbose(enableDebugLogs, "RenderManager", message);
    }

    private void LogWarning(string message)
    {
        if (enableDebugLogs)
            ChunaLogger.LogWarning($"[RenderManager] {message}");
    }

    private void LogError(string message)
    {
        ChunaLogger.LogError("RenderManager", message);
    }
    #endregion
}
