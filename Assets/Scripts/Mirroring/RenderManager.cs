using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.RenderStreaming;
using Cysharp.Threading.Tasks;
using System;

/// <summary>
/// VR 최적화 RenderManager - 단순화 버전
///
/// [주요 기능]
/// - LobbyAuthUI에서 Camera X 오브젝트 활성화/비활성화 제어
/// - SetMirroringData() 호출 시 미러링 시작
/// - StopMirroring() 호출 시 미러링 중지
///
/// [사용법]
/// 1. Camera X 오브젝트는 Inspector에서 비활성화 상태로 설정
/// 2. 로그인 성공 시 LobbyAuthUI에서 Camera X 활성화 + SetMirroringData() 호출
/// 3. 로그아웃 시 LobbyAuthUI에서 StopMirroring() + Camera X 비활성화
/// </summary>
public class RenderManager : MonoBehaviour
{
    public static RenderManager instance = null;

    [Header("Render Streaming Components")]
    public SignalingManager sm;
    public VideoStreamSender vss;

    [Header("디버그")]
    [SerializeField] private bool enableDebugLogs = true;

    private MirroringData currentMirroringData;
    private bool hasInitialized = false;
    private Coroutine runCoroutine;

    // 연결 상태 추적
    private bool isConnecting = false;
    private bool isConnected = false;
    private int connectionAttempts = 0;
    private const int MAX_CONNECTION_ATTEMPTS = 3;
    private const float CONNECTION_RETRY_DELAY = 2f;

    // 메모리 최적화를 위한 재사용 가능한 리스트
    private List<IceServer> _cachedIceServers;

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

        LogDebug("RenderManager 파괴됨");
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
    /// 미러링 중지
    /// </summary>
    public void StopMirroring()
    {
        try
        {
            if (runCoroutine != null)
            {
                StopCoroutine(runCoroutine);
                runCoroutine = null;
            }

            // 연결 상태 초기화
            isConnecting = false;
            isConnected = false;
            connectionAttempts = 0;
            hasInitialized = false;

            // SignalingManager 안전 중지
            if (sm != null)
            {
                try
                {
                    sm.Stop();
                }
                catch (Exception e)
                {
                    LogWarning($"SignalingManager 중지 중 오류: {e.Message}");
                }
            }

            LogDebug("미러링 중지됨");
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
        return isConnected && hasInitialized;
    }

    /// <summary>
    /// 연결 시도 중인지 확인
    /// </summary>
    public bool IsConnecting => isConnecting;

    /// <summary>
    /// 연결 성공 여부
    /// </summary>
    public bool IsConnected => isConnected;

    /// <summary>
    /// 연결 상태 문자열 반환 (UI 표시용)
    /// </summary>
    public string GetConnectionStatus()
    {
        if (isConnected && hasInitialized)
            return "연결됨";
        else if (isConnecting)
            return $"연결 중... ({connectionAttempts}/{MAX_CONNECTION_ATTEMPTS})";
        else if (currentMirroringData == null)
            return "미설정";
        else
            return "연결 끊김";
    }

    /// <summary>
    /// 수동 재연결 시도
    /// </summary>
    public void TryReconnect()
    {
        if (isConnecting)
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

    #region Mirroring Control (Private)
    /// <summary>
    /// 미러링 시작
    /// </summary>
    private void StartMirroring()
    {
        try
        {
            // 이미 연결 중이면 무시
            if (isConnecting)
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

            // 기존 실행 중지
            if (runCoroutine != null)
            {
                StopCoroutine(runCoroutine);
                runCoroutine = null;
            }

            // 연결 상태 초기화
            isConnecting = true;
            isConnected = false;
            connectionAttempts = 0;

            // Signaling 설정
            if (!SetupSignaling(currentMirroringData.serverIP, currentMirroringData.portNo))
            {
                LogWarning("Signaling 설정 실패. 미러링 시작 불가.");
                isConnecting = false;
                return;
            }

            // 품질 설정
            SetQuality(currentMirroringData.videoQuality);

            // 실행
            runCoroutine = StartCoroutine(Run());

            LogDebug($"미러링 시작: {currentMirroringData.serverIP}:{currentMirroringData.portNo} | 품질: {currentMirroringData.videoQuality}");
        }
        catch (Exception e)
        {
            LogError($"미러링 시작 실패: {e.Message}");
            isConnecting = false;
            isConnected = false;
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

            // 서버 주소 유효성 체크
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
            if (signalingSettings != null && signalingSettings.iceServers != null)
            {
                var iceServerEnumerator = signalingSettings.iceServers.GetEnumerator();
                while (iceServerEnumerator.MoveNext())
                {
                    _cachedIceServers.Add(iceServerEnumerator.Current);
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
            isConnecting = false;
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
            isConnecting = false;
            return;
        }

        connectionAttempts++;
        LogDebug($"연결 시도 {connectionAttempts}/{MAX_CONNECTION_ATTEMPTS}...");

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
                LogDebug($"Track 생성 완료! ID: {vss.Track.Id}");
                hasInitialized = true;
                isConnected = true;
                isConnecting = false;
                connectionAttempts = 0;
            }
            else // 타임아웃
            {
                LogWarning($"Track 생성 타임아웃 (10초) - 시도 {connectionAttempts}/{MAX_CONNECTION_ATTEMPTS}");

                // 재시도 로직
                if (connectionAttempts < MAX_CONNECTION_ATTEMPTS)
                {
                    LogDebug($"{CONNECTION_RETRY_DELAY}초 후 재시도...");
                    await UniTask.Delay(TimeSpan.FromSeconds(CONNECTION_RETRY_DELAY), cancellationToken: cts);

                    // SignalingManager 중지 후 재시작
                    try
                    {
                        sm.Stop();
                        await UniTask.Delay(500, cancellationToken: cts);
                    }
                    catch (Exception stopEx)
                    {
                        LogWarning($"재시도 전 중지 실패: {stopEx.Message}");
                    }

                    // 재시도
                    await RunAsync();
                }
                else
                {
                    LogError($"최대 연결 시도 횟수 초과 ({MAX_CONNECTION_ATTEMPTS}회). 미러링 비활성화.");
                    isConnecting = false;
                    isConnected = false;
                    hasInitialized = false;
                }
            }
        }
        catch (OperationCanceledException)
        {
            LogDebug("RunAsync 취소됨 (정상 종료)");
            isConnecting = false;
        }
        catch (Exception e)
        {
            LogError($"RunAsync 실패: {e.Message}");
            isConnecting = false;
            isConnected = false;

            // 재시도 로직
            if (connectionAttempts < MAX_CONNECTION_ATTEMPTS)
            {
                try
                {
                    LogDebug($"{CONNECTION_RETRY_DELAY}초 후 재시도...");
                    var cts = this.GetCancellationTokenOnDestroy();
                    await UniTask.Delay(TimeSpan.FromSeconds(CONNECTION_RETRY_DELAY), cancellationToken: cts);

                    // SignalingManager 중지
                    try
                    {
                        sm.Stop();
                        await UniTask.Delay(500, cancellationToken: cts);
                    }
                    catch { }

                    isConnecting = true;
                    await RunAsync();
                }
                catch (OperationCanceledException)
                {
                    LogDebug("재시도 취소됨");
                }
            }
            else
            {
                LogError($"최대 연결 시도 횟수 초과. 미러링 비활성화.");
            }
        }
    }
    #endregion

    #region Logging
    /// <summary>
    /// Quest 최적화: 조건부 컴파일로 릴리스 빌드에서 로그 제거
    /// </summary>
    [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void LogDebug(string message)
    {
        if (enableDebugLogs)
        {
            Debug.Log($"[RenderManager] {message}");
        }
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
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
