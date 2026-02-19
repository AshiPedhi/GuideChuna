using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// 듀얼 카메라 녹화 시스템
///
/// 기능:
/// - 2개의 카메라에서 동시 녹화
/// - 시나리오 시작 시 자동 녹화 시작
/// - 5분 타이머 또는 씬 이동/다시하기 시 자동 저장
/// - 이미지 시퀀스로 저장 (PNG/JPG)
/// - AsyncGPUReadback으로 성능 최적화
///
/// 사용법:
/// 1. 녹화용 카메라 2개 배치 (정면, 측면 등)
/// 2. 이 컴포넌트에 카메라 할당
/// 3. 시나리오 시작 시 자동으로 녹화 시작
/// </summary>
public class DualCameraRecorder : MonoBehaviour
{
    #region Inspector 설정
    [Header("═══ 카메라 설정 ═══")]
    [Tooltip("녹화용 카메라 1 (정면 앵글)")]
    [SerializeField] private Camera recordCamera1;

    [Tooltip("녹화용 카메라 2 (측면 앵글)")]
    [SerializeField] private Camera recordCamera2;

    [Header("═══ 녹화 설정 ═══")]
    [Tooltip("녹화 해상도 너비")]
    [SerializeField] private int recordWidth = 1280;

    [Tooltip("녹화 해상도 높이")]
    [SerializeField] private int recordHeight = 720;

    [Tooltip("녹화 FPS (낮을수록 성능 좋음, VR은 10~15 추천)")]
    [Range(5, 30)]
    [SerializeField] private int recordFPS = 10;

    [Tooltip("이미지 포맷")]
    [SerializeField] private ImageFormat imageFormat = ImageFormat.JPG;

    [Tooltip("JPG 품질 (1-100, 낮을수록 용량 작음)")]
    [Range(1, 100)]
    [SerializeField] private int jpgQuality = 75;

    [Header("═══ 자동 녹화 설정 ═══")]
    [Tooltip("시나리오 시작 시 자동 녹화")]
    [SerializeField] private bool autoRecordOnScenarioStart = true;

    [Tooltip("최대 녹화 시간 (초)")]
    [SerializeField] private float maxRecordDuration = 300f; // 5분

    [Tooltip("씬 이동 시 자동 저장")]
    [SerializeField] private bool autoSaveOnSceneChange = true;

    [Header("═══ 저장 경로 ═══")]
    [Tooltip("저장 폴더 이름")]
    [SerializeField] private string saveFolderName = "Recordings";

    [Tooltip("커스텀 저장 경로 (비어있으면 persistentDataPath 사용)")]
    [SerializeField] private string customSavePath = "";

    [Header("═══ 디버그 ═══")]
    [SerializeField] private bool showDebugLogs = true;
    [SerializeField] private bool showRecordingIndicator = true;
    #endregion

    #region Enums
    public enum ImageFormat
    {
        PNG,
        JPG
    }
    #endregion

    #region Private Fields
    // RenderTexture
    private RenderTexture renderTexture1;
    private RenderTexture renderTexture2;

    // 녹화 상태
    private bool isRecording = false;
    private float recordStartTime = 0f;
    private int frameCount = 0;
    private string currentSessionPath = "";
    private string currentScenarioName = "";
    private string currentModeName = "";

    // 프레임 캡처 타이밍
    private float lastCaptureTime = 0f;
    private float captureInterval = 0f;

    // 인코딩 상태 (끊김 방지용)
    private bool isEncoding1 = false;
    private bool isEncoding2 = false;

    // 비동기 저장 큐
    private Queue<FrameData> saveQueue = new Queue<FrameData>();
    private Thread saveThread;
    private bool isSaveThreadRunning = false;
    private object queueLock = new object();

    // 이벤트
    public event Action OnRecordingStarted;
    public event Action<string> OnRecordingStopped; // 저장 경로 반환
    #endregion

    #region Unity Lifecycle
    void Awake()
    {
        captureInterval = 1f / recordFPS;
        InitializeRenderTextures();
    }

    void Start()
    {
        SubscribeEvents();
        StartSaveThread();
    }

    void OnDestroy()
    {
        UnsubscribeEvents();
        StopRecording();
        StopSaveThread();
        CleanupRenderTextures();
    }

    void OnApplicationQuit()
    {
        if (isRecording)
        {
            StopRecording();
        }
    }

    void Update()
    {
        if (!isRecording) return;

        // 최대 녹화 시간 체크
        if (Time.time - recordStartTime >= maxRecordDuration)
        {
            if (showDebugLogs)
                ChunaLogger.Log($"<color=orange>[DualCameraRecorder] 최대 녹화 시간 도달 ({maxRecordDuration}초)</color>");
            StopRecording();
            return;
        }

        // 프레임 캡처
        if (Time.time - lastCaptureTime >= captureInterval)
        {
            CaptureFrame();
            lastCaptureTime = Time.time;
        }
    }
    #endregion

    #region 초기화
    private void InitializeRenderTextures()
    {
        // 카메라 1용 RenderTexture
        if (recordCamera1 != null)
        {
            renderTexture1 = new RenderTexture(recordWidth, recordHeight, 24, RenderTextureFormat.ARGB32);
            renderTexture1.antiAliasing = 2;
            renderTexture1.Create();
            recordCamera1.targetTexture = renderTexture1;
        }

        // 카메라 2용 RenderTexture
        if (recordCamera2 != null)
        {
            renderTexture2 = new RenderTexture(recordWidth, recordHeight, 24, RenderTextureFormat.ARGB32);
            renderTexture2.antiAliasing = 2;
            renderTexture2.Create();
            recordCamera2.targetTexture = renderTexture2;
        }

        if (showDebugLogs)
            ChunaLogger.Log($"<color=cyan>[DualCameraRecorder] RenderTexture 초기화 완료 ({recordWidth}x{recordHeight})</color>");
    }

    private void CleanupRenderTextures()
    {
        if (recordCamera1 != null)
            recordCamera1.targetTexture = null;
        if (recordCamera2 != null)
            recordCamera2.targetTexture = null;

        if (renderTexture1 != null)
        {
            renderTexture1.Release();
            Destroy(renderTexture1);
        }
        if (renderTexture2 != null)
        {
            renderTexture2.Release();
            Destroy(renderTexture2);
        }
    }
    #endregion

    #region 이벤트 구독
    private void SubscribeEvents()
    {
        // 씬 이동 감지
        SceneManager.sceneUnloaded += OnSceneUnloaded;

        // 시나리오 이벤트 구독
        try
        {
            ScenarioEventSystem.Instance.OnScenarioStarted += OnScenarioStarted;
            ScenarioEventSystem.Instance.OnScenarioCompleted += OnScenarioCompleted;
        }
        catch (Exception e)
        {
            if (showDebugLogs)
                ChunaLogger.LogWarning($"[DualCameraRecorder] 시나리오 이벤트 구독 실패: {e.Message}");
        }
    }

    private void UnsubscribeEvents()
    {
        SceneManager.sceneUnloaded -= OnSceneUnloaded;

        try
        {
            ScenarioEventSystem.Instance.OnScenarioStarted -= OnScenarioStarted;
            ScenarioEventSystem.Instance.OnScenarioCompleted -= OnScenarioCompleted;
        }
        catch { }
    }

    private void OnSceneUnloaded(Scene scene)
    {
        if (autoSaveOnSceneChange && isRecording)
        {
            if (showDebugLogs)
                ChunaLogger.Log($"<color=orange>[DualCameraRecorder] 씬 언로드 감지 - 녹화 저장</color>");
            StopRecording();
        }
    }

    private void OnScenarioStarted(ScenarioData scenario)
    {
        if (autoRecordOnScenarioStart && scenario != null)
        {
            currentScenarioName = scenario.scenarioName;
            StartRecording();
        }
    }

    private void OnScenarioCompleted(ScenarioData scenario)
    {
        if (isRecording)
        {
            StopRecording();
        }
    }
    #endregion

    #region 녹화 제어
    /// <summary>
    /// 녹화 시작
    /// </summary>
    public void StartRecording(string scenarioName = "", string modeName = "")
    {
        if (isRecording)
        {
            if (showDebugLogs)
                ChunaLogger.LogWarning("[DualCameraRecorder] 이미 녹화 중입니다.");
            return;
        }

        if (recordCamera1 == null && recordCamera2 == null)
        {
            ChunaLogger.LogError("[DualCameraRecorder] 녹화 카메라가 설정되지 않았습니다!");
            return;
        }

        // 세션 정보 설정
        if (!string.IsNullOrEmpty(scenarioName))
            currentScenarioName = scenarioName;
        if (!string.IsNullOrEmpty(modeName))
            currentModeName = modeName;

        // 저장 경로 생성
        currentSessionPath = CreateSessionFolder();

        // 녹화 시작
        isRecording = true;
        recordStartTime = Time.time;
        frameCount = 0;
        lastCaptureTime = Time.time;

        // 카메라 활성화
        if (recordCamera1 != null) recordCamera1.enabled = true;
        if (recordCamera2 != null) recordCamera2.enabled = true;

        if (showDebugLogs)
            ChunaLogger.Log($"<color=green>[DualCameraRecorder] 녹화 시작: {currentSessionPath}</color>");

        OnRecordingStarted?.Invoke();
    }

    /// <summary>
    /// 녹화 중지 및 저장
    /// </summary>
    public void StopRecording()
    {
        if (!isRecording) return;

        isRecording = false;

        float duration = Time.time - recordStartTime;

        if (showDebugLogs)
            ChunaLogger.Log($"<color=green>[DualCameraRecorder] 녹화 중지: {frameCount}프레임, {duration:F1}초</color>");

        // 남은 큐 처리 대기
        StartCoroutine(WaitForSaveComplete());

        OnRecordingStopped?.Invoke(currentSessionPath);
    }

    private IEnumerator WaitForSaveComplete()
    {
        // 큐가 비워질 때까지 대기
        while (true)
        {
            lock (queueLock)
            {
                if (saveQueue.Count == 0) break;
            }
            yield return new WaitForSeconds(0.1f);
        }

        if (showDebugLogs)
            ChunaLogger.Log($"<color=green>[DualCameraRecorder] 모든 프레임 저장 완료: {currentSessionPath}</color>");
    }

    /// <summary>
    /// 녹화 중인지 확인
    /// </summary>
    public bool IsRecording => isRecording;

    /// <summary>
    /// 현재 녹화 시간
    /// </summary>
    public float CurrentRecordTime => isRecording ? Time.time - recordStartTime : 0f;

    /// <summary>
    /// 현재 프레임 수
    /// </summary>
    public int CurrentFrameCount => frameCount;
    #endregion

    #region 프레임 캡처
    private void CaptureFrame()
    {
        frameCount++;

        // 카메라 1 캡처 (인코딩 중이면 스킵)
        if (recordCamera1 != null && renderTexture1 != null && !isEncoding1)
        {
            CaptureFromCamera(renderTexture1, 1);
        }

        // 카메라 2 캡처 (인코딩 중이면 스킵)
        if (recordCamera2 != null && renderTexture2 != null && !isEncoding2)
        {
            CaptureFromCamera(renderTexture2, 2);
        }
    }

    private void CaptureFromCamera(RenderTexture rt, int cameraIndex)
    {
        // 인코딩 플래그 설정
        if (cameraIndex == 1) isEncoding1 = true;
        else isEncoding2 = true;

        // AsyncGPUReadback 사용 (성능 최적화)
        AsyncGPUReadback.Request(rt, 0, TextureFormat.RGB24, (request) =>
        {
            if (request.hasError)
            {
                if (showDebugLogs)
                    ChunaLogger.LogWarning($"[DualCameraRecorder] GPU 읽기 오류 (카메라 {cameraIndex})");
                // 인코딩 플래그 해제
                if (cameraIndex == 1) isEncoding1 = false;
                else isEncoding2 = false;
                return;
            }

            if (!isRecording && saveQueue.Count == 0)
            {
                if (cameraIndex == 1) isEncoding1 = false;
                else isEncoding2 = false;
                return;
            }

            // 메인 스레드에서 Texture2D 생성 및 인코딩
            StartCoroutine(EncodeAndSaveFrame(request, cameraIndex, frameCount));
        });
    }

    private IEnumerator EncodeAndSaveFrame(AsyncGPUReadbackRequest request, int cameraIndex, int frame)
    {
        // Texture2D 생성
        Texture2D tex = new Texture2D(recordWidth, recordHeight, TextureFormat.RGB24, false);
        tex.LoadRawTextureData(request.GetData<byte>());
        tex.Apply();

        // 이미지 인코딩
        byte[] encodedData;
        string extension;

        if (imageFormat == ImageFormat.PNG)
        {
            encodedData = tex.EncodeToPNG();
            extension = "png";
        }
        else
        {
            encodedData = tex.EncodeToJPG(jpgQuality);
            extension = "jpg";
        }

        // Texture2D 해제
        Destroy(tex);

        // 저장 큐에 추가
        var frameData = new FrameData
        {
            encodedData = encodedData,
            frameNumber = frame,
            cameraIndex = cameraIndex,
            sessionPath = currentSessionPath,
            extension = extension
        };

        lock (queueLock)
        {
            saveQueue.Enqueue(frameData);
        }

        // 인코딩 완료 - 플래그 해제
        if (cameraIndex == 1) isEncoding1 = false;
        else isEncoding2 = false;

        yield return null;
    }
    #endregion

    #region 파일 저장 (별도 스레드)
    private void StartSaveThread()
    {
        isSaveThreadRunning = true;
        saveThread = new Thread(SaveThreadLoop);
        saveThread.IsBackground = true;
        saveThread.Start();
    }

    private void StopSaveThread()
    {
        isSaveThreadRunning = false;
        if (saveThread != null && saveThread.IsAlive)
        {
            saveThread.Join(1000);
        }
    }

    private void SaveThreadLoop()
    {
        while (isSaveThreadRunning)
        {
            FrameData frameData = null;

            lock (queueLock)
            {
                if (saveQueue.Count > 0)
                {
                    frameData = saveQueue.Dequeue();
                }
            }

            if (frameData != null)
            {
                SaveFrameToFile(frameData);
            }
            else
            {
                Thread.Sleep(10);
            }
        }
    }

    private void SaveFrameToFile(FrameData frameData)
    {
        try
        {
            string cameraFolder = Path.Combine(frameData.sessionPath, $"Camera{frameData.cameraIndex}");

            if (!Directory.Exists(cameraFolder))
            {
                Directory.CreateDirectory(cameraFolder);
            }

            string fileName = $"frame_{frameData.frameNumber:D6}.{frameData.extension}";
            string filePath = Path.Combine(cameraFolder, fileName);

            // 인코딩된 데이터 직접 저장 (JPG/PNG)
            File.WriteAllBytes(filePath, frameData.encodedData);
        }
        catch (Exception e)
        {
            ChunaLogger.LogError($"[DualCameraRecorder] 프레임 저장 오류: {e.Message}");
        }
    }
    #endregion

    #region 폴더 관리
    private string CreateSessionFolder()
    {
        string basePath = string.IsNullOrEmpty(customSavePath)
            ? Application.persistentDataPath
            : customSavePath;

        string recordingsPath = Path.Combine(basePath, saveFolderName);

        // 세션 폴더 이름 생성
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string scenarioSafe = string.IsNullOrEmpty(currentScenarioName)
            ? "Recording"
            : SanitizeFileName(currentScenarioName);
        string modeSafe = string.IsNullOrEmpty(currentModeName)
            ? ""
            : $"_{SanitizeFileName(currentModeName)}";

        string sessionName = $"{scenarioSafe}{modeSafe}_{timestamp}";
        string sessionPath = Path.Combine(recordingsPath, sessionName);

        // 폴더 생성
        if (!Directory.Exists(sessionPath))
        {
            Directory.CreateDirectory(sessionPath);
        }

        return sessionPath;
    }

    private string SanitizeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        foreach (char c in invalid)
        {
            name = name.Replace(c, '_');
        }
        return name;
    }

    /// <summary>
    /// 저장 경로 가져오기
    /// </summary>
    public string GetSavePath()
    {
        string basePath = string.IsNullOrEmpty(customSavePath)
            ? Application.persistentDataPath
            : customSavePath;
        return Path.Combine(basePath, saveFolderName);
    }
    #endregion

    #region Public API
    /// <summary>
    /// 녹화 카메라 설정
    /// </summary>
    public void SetRecordCameras(Camera cam1, Camera cam2)
    {
        // 기존 RenderTexture 정리
        CleanupRenderTextures();

        recordCamera1 = cam1;
        recordCamera2 = cam2;

        // 새 RenderTexture 초기화
        InitializeRenderTextures();
    }

    /// <summary>
    /// 녹화 해상도 설정
    /// </summary>
    public void SetResolution(int width, int height)
    {
        if (isRecording)
        {
            ChunaLogger.LogWarning("[DualCameraRecorder] 녹화 중에는 해상도를 변경할 수 없습니다.");
            return;
        }

        recordWidth = width;
        recordHeight = height;

        // RenderTexture 재생성
        CleanupRenderTextures();
        InitializeRenderTextures();
    }

    /// <summary>
    /// 녹화 FPS 설정
    /// </summary>
    public void SetFPS(int fps)
    {
        recordFPS = Mathf.Clamp(fps, 10, 60);
        captureInterval = 1f / recordFPS;
    }

    /// <summary>
    /// 최대 녹화 시간 설정
    /// </summary>
    public void SetMaxDuration(float seconds)
    {
        maxRecordDuration = Mathf.Max(10f, seconds);
    }

    /// <summary>
    /// 모드 이름 설정 (파일명에 사용)
    /// </summary>
    public void SetModeName(string mode)
    {
        currentModeName = mode;
    }
    #endregion

    #region 데이터 구조
    private class FrameData
    {
        public byte[] encodedData;  // JPG/PNG 인코딩된 데이터
        public int frameNumber;
        public int cameraIndex;
        public string sessionPath;
        public string extension;    // "jpg" 또는 "png"
    }
    #endregion

    #region GUI (녹화 표시)
    void OnGUI()
    {
        if (!showRecordingIndicator || !isRecording) return;

        // 녹화 중 표시
        GUI.color = new Color(1f, 0f, 0f, Mathf.PingPong(Time.time * 2f, 1f));
        GUI.Label(new Rect(10, 10, 200, 30), $"● REC {CurrentRecordTime:F1}s");
        GUI.color = Color.white;
    }
    #endregion
}
