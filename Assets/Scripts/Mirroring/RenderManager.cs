using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.RenderStreaming;
using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using System.Net.Sockets;

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
    // sm.Run() 전 도달성 사전 검사 타임아웃 (죽은/stale IP로 메인스레드가 막혀 ANR나는 것 방지)
    private const float CONNECTION_PROBE_TIMEOUT = 3f;
    // [E] 연결 후 헬스체크: 주기 + 연속 실패 임계 (PC 종료/네트워크 끊김 시 좀비 연결 자동 정리)
    private const float HEALTH_CHECK_INTERVAL = 5f;
    private const int HEALTH_CHECK_MAX_FAILURES = 2;

    // 메모리 최적화
    private List<IceServer> _cachedIceServers;
    private CancellationTokenSource _connectionCts;
    private bool _isRunning = false;

    // [재조회] 미러링 실패 시 백엔드에서 최신 MirroringData를 다시 받아오는 콜백 (로그인 측에서 주입).
    // PC 미러링 클라가 IP를 재등록했을 때 새 주소를 자동으로 받아 재시도하기 위함.
    public Func<UniTask<MirroringData>> mirroringDataRefresher;

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

        // [C] 앱 종료/씬 파괴 시 SignalingManager teardown 보장 (CancelAllOperations만으론 sm이 안 멈춤)
        SafeStopSignalingManager();

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

    /// <summary>
    /// 씬 전환 후 재연결 — VideoStreamSender를 강제 리셋하여 Track/RenderTexture 재생성
    /// </summary>
    public void ReconnectAfterSceneTransition()
    {
        if (currentMirroringData == null)
        {
            LogWarning("미러링 데이터가 없어 씬 전환 후 재연결 불가.");
            return;
        }

        LogDebug("씬 전환 후 재연결 시작 (VideoStreamSender 리셋 포함)...");
        StopMirroring();
        StartCoroutine(ReconnectWithVideoResetCoroutine());
    }

    private IEnumerator ReconnectWithVideoResetCoroutine()
    {
        // VideoStreamSender를 비활성화하여 내부 Track/RenderTexture 해제
        if (vss != null)
        {
            vss.enabled = false;
            LogDebug("VideoStreamSender 비활성화 → 내부 리소스 해제");
        }

        // 1프레임 대기 — OnDisable 처리 완료
        yield return null;

        // VideoStreamSender 재활성화 — 새 RenderTexture/Track 생성 준비
        if (vss != null)
        {
            vss.enabled = true;
            LogDebug("VideoStreamSender 재활성화 → 새 Track 생성 준비");
        }

        // 1프레임 더 대기 — OnEnable 처리 완료
        yield return null;

        StartMirroring();
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
                    // ★ 도달성 사전 검사 (논블로킹):
                    // serverIP가 stale/오류이거나 PC 미실행/다른 네트워크면 sm.Run()이 메인스레드에서
                    // WebSocket 연결을 시도하며 OS 타임아웃까지 블록 → Quest ANR → 강제종료.
                    // 먼저 백그라운드 스레드에서 짧은 타임아웃 TCP 연결로 닿는지 확인하고,
                    // 닿지 않으면 sm.Run()을 호출하지 않아 ANR을 막는다 (앱 유지 → 다음 로그인 시 정상 연결).
                    bool reachable = await ProbeReachableAsync(currentMirroringData.serverIP, currentMirroringData.portNo);
                    if (cancellationToken.IsCancellationRequested) break;
                    if (!reachable)
                    {
                        LogWarning($"서버 도달 불가: {currentMirroringData.serverIP}:{currentMirroringData.portNo} " +
                                   $"(시도 {connectionAttempts}/{MAX_CONNECTION_ATTEMPTS}) — sm.Run() 건너뜀(ANR 방지)");

                        // [재조회] 백엔드에서 최신 미러링 IP를 한 번 받아온다.
                        // PC 미러링 클라가 IP를 재등록했다면 새 주소가 오고, 그 경우에만 새 주소로 재시도한다.
                        // IP가 그대로(=아직 미갱신)이거나 재조회 자체가 실패면 더 시도해도 무의미하므로 중단(앱 유지).
                        // 그 이후 재시도는 사용자의 로그아웃/로그인으로만.
                        var fresh = await TryRefreshMirroringDataAsync();
                        if (cancellationToken.IsCancellationRequested) break;

                        bool ipChanged = fresh != null
                            && !string.IsNullOrEmpty(fresh.serverIP)
                            && fresh.mirroring != "off"
                            && (fresh.serverIP != currentMirroringData.serverIP || fresh.portNo != currentMirroringData.portNo);

                        if (!ipChanged)
                        {
                            LogWarning("재조회 결과 IP 동일/없음 → 자동 재시도 중단 (재로그인 시 재시도)");
                            break; // 루프 종료 → finally에서 Failed 처리, 앱 유지
                        }

                        LogDebug($"새 미러링 IP 수신: {fresh.serverIP}:{fresh.portNo} → 교체 후 재시도");
                        currentMirroringData = fresh;
                        if (!SetupSignaling(currentMirroringData.serverIP, currentMirroringData.portNo))
                        {
                            LogWarning("새 IP로 Signaling 재설정 실패 → 중단");
                            break;
                        }
                        SetQuality(currentMirroringData.videoQuality);

                        if (connectionAttempts < MAX_CONNECTION_ATTEMPTS)
                        {
                            await UniTask.Delay(TimeSpan.FromSeconds(CONNECTION_RETRY_DELAY), cancellationToken: cancellationToken);
                        }
                        continue; // 새 IP로 다음 시도 (다음 루프에서 새 주소 프로브)
                    }

                    // SignalingManager 실행
                    sm.Run();

                    // 타임아웃과 함께 Track 생성 대기
                    bool connected = await WaitForTrackWithTimeout(cancellationToken);

                    if (connected)
                    {
                        LogDebug($"Track 생성 완료! ID: {vss.Track?.Id}");
                        connectionState = ConnectionState.Connected;
                        _isRunning = false;
                        // [E] 연결 유지 동안 주기적 헬스체크 시작 (끊기면 자동 stop)
                        RunHealthCheckLoopAsync(cancellationToken).Forget();
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
                // [C] 실패로 끝나는 경로 teardown 보장:
                // 마지막 시도에서 sm.Run() 후 timeout/예외로 끝나면 SignalingManager가 살아있어
                // 좀비 연결로 남는다. 비연결 종료 시 무조건 중지.
                SafeStopSignalingManager();
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

    /// <summary>
    /// [E] 연결 유지 중 주기적 헬스체크. Track 소실 또는 서버 도달 불가가 연속
    /// HEALTH_CHECK_MAX_FAILURES회 발생하면 좀비 연결로 판단하고 자동 중지(StopMirroring → Failed).
    /// 도달성 검사는 ProbeReachableAsync(스레드풀, 논블로킹)라 메인스레드/ANR 영향 없음.
    /// 재연결은 하지 않음 — 사용자가 재로그인/수동 재연결로 복구.
    /// </summary>
    private async UniTaskVoid RunHealthCheckLoopAsync(CancellationToken cancellationToken)
    {
        int consecutiveFailures = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested &&
                   connectionState == ConnectionState.Connected)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(HEALTH_CHECK_INTERVAL), cancellationToken: cancellationToken);
                if (cancellationToken.IsCancellationRequested) break;

                bool trackAlive = vss != null && vss.Track != null;
                // Track이 살아있을 때만 도달성 프로브 (불필요한 소켓 연결 방지)
                bool reachable = trackAlive && currentMirroringData != null &&
                                 await ProbeReachableAsync(currentMirroringData.serverIP, currentMirroringData.portNo);
                if (cancellationToken.IsCancellationRequested) break;

                if (trackAlive && reachable)
                {
                    consecutiveFailures = 0;
                    continue;
                }

                consecutiveFailures++;
                LogWarning($"미러링 헬스체크 실패 {consecutiveFailures}/{HEALTH_CHECK_MAX_FAILURES} " +
                           $"(track={trackAlive}, reachable={reachable})");

                if (consecutiveFailures >= HEALTH_CHECK_MAX_FAILURES)
                {
                    LogWarning("미러링 연결 끊김으로 판단 — 자동 중지");
                    StopMirroring();                          // 비동기 작업 취소 + signaling 정리 (cancellationToken도 취소됨)
                    connectionState = ConnectionState.Failed; // UI 표시용 (StopMirroring은 Idle로 두므로 덮어씀)
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            LogDebug("헬스체크 종료 (취소)");
        }
        catch (Exception e)
        {
            LogError($"헬스체크 예외 (무시): {e.Message}");
        }
    }
    #endregion

    #region Signaling Setup
    /// <summary>
    /// 미러링 서버(serverIP:port)에 실제로 닿는지 짧은 타임아웃으로 사전 확인.
    /// 전부 백그라운드 스레드에서 처리하고, 어떤 실패(타임아웃/거부/형식오류)든 false로 흡수하여
    /// 메인스레드 블로킹/예외 전파 없이 "도달 불가"만 알려준다.
    /// </summary>
    private async UniTask<bool> ProbeReachableAsync(string host, int port)
    {
        if (string.IsNullOrEmpty(host) || port <= 0 || port > 65535)
            return false;

        try
        {
            // 전부 스레드풀에서 처리(메인스레드 무간섭). 타임아웃은 타이머 기반 CTS +
            // AttachExternalCancellation으로 스레드 무관하게 적용(플레이어 루프 의존 없음).
            return await UniTask.RunOnThreadPool(async () =>
            {
                var tcp = new TcpClient();
                using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(CONNECTION_PROBE_TIMEOUT)))
                {
                    try
                    {
                        await tcp.ConnectAsync(host, port).AsUniTask()
                                 .AttachExternalCancellation(timeoutCts.Token);
                        return tcp.Connected;
                    }
                    finally
                    {
                        // 소켓 정리 → 타임아웃으로 버린 연결 시도도 함께 폐기
                        try { tcp.Close(); } catch { }
                        tcp.Dispose();
                    }
                }
            });
        }
        catch (Exception e)
        {
            // 타임아웃(OperationCanceledException)/연결 거부/형식 오류 등 모두 도달 불가로 흡수
            LogDebug($"도달성 프로브: {host}:{port} 미연결 처리 ({e.GetType().Name})");
            return false;
        }
    }

    /// <summary>
    /// [재조회] 주입된 콜백으로 백엔드에서 최신 MirroringData를 받아온다.
    /// 콜백 미설정/예외는 모두 null로 흡수(= 재시도 중단 신호). 메인스레드 블로킹 없음(콜백은 UniTask 비동기).
    /// </summary>
    private async UniTask<MirroringData> TryRefreshMirroringDataAsync()
    {
        if (mirroringDataRefresher == null)
        {
            LogDebug("미러링 재조회 콜백 미설정 → 재조회 생략");
            return null;
        }

        try
        {
            var fresh = await mirroringDataRefresher.Invoke();
            LogDebug($"미러링 재조회 결과: {fresh?.serverIP}:{fresh?.portNo} (mirroring={fresh?.mirroring})");
            return fresh;
        }
        catch (Exception e)
        {
            LogWarning($"미러링 재조회 실패 (무시): {e.Message}");
            return null;
        }
    }

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
