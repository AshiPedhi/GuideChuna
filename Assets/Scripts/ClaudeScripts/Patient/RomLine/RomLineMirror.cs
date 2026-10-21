using System;
using System.Text;
using System.Threading;
using Meta.XR;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

/// <summary>
/// ★경추 ROM «선 실측» — 관제 미러링(2026-10-16 신설 · 사용자 지시).
///
/// 헤드셋을 거치해 두고 컨트롤러만으로 잴 때, 같은 Wi-Fi의 브라우저(<c>Resources/RomLine/mirror.html</c>)가
/// 두 장의 이미지를 받아 보여 준다. 서버는 <see cref="RomLineMonitor"/> 그대로다.
///   /wall.jpg  벽면UI — 메인 화면 바로 앞에 세운 전용 카메라가 찍는다. 헤드셋에 보이는 것과 같은 화면이다.
///   /cam.jpg   현실 영상 — Meta Passthrough Camera Access(MRUK <see cref="PassthroughCameraAccess"/>, Quest 3/3S).
///              ★10-16 사용자 요청: 카메라 영상에는 앱이 그린 것(컨트롤러 끝점 · 선 · 각도기)이 없다 → 패스스루 카메라와
///              <b>같은 자리 · 같은 화각</b>의 가상 카메라로 앱 화면을 한 번 더 그려 영상 위에 겹친다(camOverlay).
///              자리 = SDK GetCameraPose(★추적 공간 기준 → 리그 trackingSpace를 곱해 월드로), 화각 = SDK ViewportPointToRay의 두 모서리.
///              화각을 따로 계산하지 않는다 — SDK가 영상과 3D를 잇는 함수를 그대로 탄다(규칙 9: 미리보기는 실제와 같은 함수).
///              겹치기는 CPU에서 한다: 투명 바탕(0,0,0,0)에 그린 가상 화면을 읽어 out = 가상 + 영상 × (1 − 알파).
///              헤드셋도 눈 버퍼의 알파로 패스스루와 합치므로 같은 규칙이다(추정 — 기기에서 눈으로 확인).
///   /mirrorinfo 진단(지원 여부 · 해상도 · 초당 장수 · JPEG 만드는 시간 · 착용 여부)
/// 버튼은 종전 모니터와 같은 /cmd?c= 길을 탄다 — 앱의 RunMonitorCommand가 실행한다(양방향).
///
/// ★webrtc를 쓰지 않는다 — Vulkan에서 플러그인이 텍스처를 못 만들어 검은 화면이었다(10-01 기기 로그).
/// ★벽면UI를 전용 레이어로 옮기지 않는다. 눈 카메라 m_CullingMask가 0·1·2·4(·5)만 그려서, 옮기면 헤드셋에서 벽면UI가 사라진다.
///   대신 카메라를 판 바로 앞에 두고 <b>판 앞뒤 얇은 두께만</b> 그린다(near/far). 판은 3m 넘게 떨어진 허공이라 그 두께 안에 다른 것은 없다(추정 — 이미지로 확인).
/// ★브라우저가 그 이미지를 3초 안에 물어 가지 않으면 찍지도 · 인코딩하지도 않는다.
/// ★JPEG 인코딩은 스레드 풀에서 한다(ImageConversion.EncodeArrayToJPG). 한 장이 끝나기 전에는 다음 장을 잡지 않는다.
/// </summary>
public class RomLineMirror : MonoBehaviour
{
    [Header("=== 벽면UI 이미지 ===")]
    [Tooltip("벽면UI 이미지의 가로 픽셀. 세로는 벽면UI 비율로 정한다.")]
    [SerializeField] private int wallWidth = 1280;
    [Tooltip("벽면UI를 찍는 간격(초).")]
    [SerializeField] private float wallInterval = 0.1f;
    [Tooltip("벽면UI 이미지의 바탕색(헤드셋에서는 이 자리에 패스스루가 보인다).")]
    [SerializeField] private Color wallBackground = new Color(0.04f, 0.05f, 0.07f, 1f);

    [Header("=== 현실 영상 (Passthrough Camera Access) ===")]
    [Tooltip("요청할 카메라 해상도. 지원 목록에 없으면 바로 아래 해상도가 골라진다.")]
    [SerializeField] private Vector2Int camResolution = new Vector2Int(640, 480);
    [Tooltip("현실 영상을 보내는 간격(초). 10-21 사용자 지시: 상한을 낮춰 배터리 · 발열을 줄인다 — 1/15초(최대 15장/s · 종전 0.05 = 20장/s). 런타임 AddComponent라 코드 기본값이 먹는다.")]
    [SerializeField] private float camInterval = 1f / 15f;
    [Tooltip("현실 영상 위에 앱 화면(끝점 · 선 · 각도기 · 벽면UI)을 겹친다. 끄면 카메라 영상만 보낸다.")]
    [SerializeField] private bool camOverlay = true;

    [Header("=== 공통 ===")]
    [Range(30, 95)] [SerializeField] private int jpegQuality = 70;
    [Tooltip("진단 로그 간격(초). 0이면 남기지 않는다.")]
    [SerializeField] private float diagInterval = 5f;

    // 벽면UI의 «배율 전» 범위 — RomLineView의 판 + 탭 줄 + 안내 띠. (위 끝, 아래 끝, 폭)
    private const float WallTop = RomLineView.PanelH * 0.5f;
    private const float WallBottom = -(RomLineView.PanelH * 0.5f + 0.015f + 0.088f + 0.015f + 0.075f);
    private const float CamBack = 0.5f;     // 판 앞 몇 m(배율 전)에 카메라를 두나
    private const float SlabHalf = 0.05f;   // 판 앞뒤로 그릴 두께의 절반(배율 전)

    private RomLineMonitor monitor;
    private RomLineView view;

    private Camera wallCam;
    private RenderTexture wallRt;
    private int wallW, wallH;
    private float wallNext;
    private volatile bool wallBusy;
    private byte[] wallBuf;
    private int wallFrames; private double wallEncodeMs;

    private PassthroughCameraAccess pca;
    private string pcaState = "시작 전";
    private Vector2Int pcaShownRes;
    private float camNext;
    private volatile bool camBusy;
    private byte[] camBuf;
    private int camW, camH;
    private int camFrames; private double camEncodeMs;
    private Camera overlayCam;
    private RenderTexture overlayRt;
    private byte[] overlayBuf;
    private Transform trackingSpace;
    private bool overlayLogged;

    private float diagNext, diagFrom;

    /// <summary>관제 화면의 해상도 버튼(10-20 사용자 지시 · 우측 상단 셋). 전부 4:3 — 화면 칸의 비율이 안 바뀐다. 지원 목록은 기기 로그로 확인(320×240 ~ 1280×1280).</summary>
    public static readonly Vector2Int[] CamChoices = { new Vector2Int(640, 480), new Vector2Int(800, 600), new Vector2Int(1280, 960) };
    private Vector2Int pendingRes;   // 바꿀 해상도 — 인코딩 중인 장이 끝난 뒤(camBusy 아님)에 카메라를 다시 연다
    private readonly StringBuilder info = new StringBuilder(512);

    public void Init(RomLineMonitor m, RomLineView v)
    {
        monitor = m;
        view = v;
        StartCamera();
        Debug.Log($"[선실측] 미러링 — 붙었다 · 벽면UI {wallWidth}px 간격 {wallInterval:F2}s · 현실 영상 {camResolution.x}×{camResolution.y} 간격 {camInterval:F2}s · JPEG {jpegQuality}");
    }

    // ── 현실 영상 ────────────────────────────────────────────────────
    private void StartCamera()
    {
        if (!PassthroughCameraAccess.IsSupported)
        {
            pcaState = "지원 안 함(Quest 3/3S · Horizon OS v74 이상 · 기기 빌드에서만)";
            Debug.Log("[선실측] 미러링 — 현실 영상: " + pcaState);
            return;
        }
#if UNITY_ANDROID && !UNITY_EDITOR
        const string perm = OVRPermissionsRequester.PassthroughCameraAccessPermission;
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(perm))
        {
            UnityEngine.Android.Permission.RequestUserPermission(perm);
            Debug.Log("[선실측] 미러링 — 현실 영상: 카메라 권한을 요청했다(" + perm + ")");
        }
#endif
        var res = PassthroughCameraAccess.GetSupportedResolutions(PassthroughCameraAccess.CameraPositionType.Left);
        Debug.Log("[선실측] 미러링 — 현실 영상: 지원 해상도 " + (res == null ? "(없음)" : string.Join(" · ", Array.ConvertAll(res, r => r.x + "×" + r.y))));

        var go = new GameObject("미러링 카메라(패스스루)");
        go.SetActive(false);   // 값을 넣은 뒤에 켠다 — OnEnable이 RequestedResolution으로 연다
        go.transform.SetParent(transform, false);
        pca = go.AddComponent<PassthroughCameraAccess>();
        pca.CameraPosition = PassthroughCameraAccess.CameraPositionType.Left;
        pca.RequestedResolution = camResolution;
        go.SetActive(true);
        pcaState = "권한 · 첫 영상 기다리는 중";
    }

    /// <summary>현실 영상 해상도를 바꾼다(관제 화면 버튼 → /cmd?c=camN). 실제로 바꾸는 건 Update — 쓰던 버퍼를 인코딩 스레드가 아직 들고 있을 수 있다.</summary>
    public void RequestCamResolution(int choice)
    {
        if (choice < 0 || choice >= CamChoices.Length) return;
        if (pca == null) { Debug.LogWarning("[선실측] ★미러링 — 해상도 바꾸기: 현실 영상이 없다(" + pcaState + ")"); return; }
        pendingRes = CamChoices[choice];
    }

    private void Update()
    {
        if (monitor == null || monitor.Port == 0) return;
        float now = Time.unscaledTime;

        if (pendingRes != default && pca != null && !camBusy)
        {
            Debug.Log($"[선실측] 미러링 — 현실 영상 해상도 {camResolution.x}×{camResolution.y} → {pendingRes.x}×{pendingRes.y}(카메라를 다시 연다)");
            camResolution = pendingRes;
            pendingRes = default;
            pca.gameObject.SetActive(false);   // OnEnable이 RequestedResolution으로 다시 연다(StartCamera와 같은 길)
            pca.RequestedResolution = camResolution;
            pca.gameObject.SetActive(true);
            pcaShownRes = default;
            pcaState = "해상도 바꾸는 중";
        }

        if (pca != null && pca.IsPlaying)
        {
            if (pca.CurrentResolution != pcaShownRes)   // 바뀔 때만 글자를 만든다(매 프레임 할당 금지)
            {
                pcaShownRes = pca.CurrentResolution;
                pcaState = $"받는 중 {pcaShownRes.x}×{pcaShownRes.y}";
                Debug.Log("[선실측] 미러링 — 현실 영상: 첫 영상 · " + pcaState);
            }
        }

        if (diagInterval > 0f && now >= diagNext) Diagnose(now);
    }

    private void CaptureCam()
    {
        NativeArray<Color32> px = pca.GetColors();   // ★캐시 · 수정 금지 — 바로 복사한다
        Vector2Int r = pca.CurrentResolution;
        int bytes = px.Length * 4;
        if (bytes == 0 || r.x * r.y != px.Length) return;
        if (camBuf == null || camBuf.Length != bytes) camBuf = new byte[bytes];
        px.Reinterpret<byte>(4).CopyTo(camBuf);
        camW = r.x; camH = r.y;
        camBusy = true;
        if (!camOverlay || !SystemInfo.supportsAsyncGPUReadback)
        {
            ThreadPool.QueueUserWorkItem(_ => Encode(camBuf, camW, camH, "cam"));
            return;
        }

        // 앱 화면을 패스스루 카메라 자리에서 다시 그린다
        EnsureOverlay(r);
        Pose pose = pca.GetCameraPose();   // 영상이 찍힌 시각의 자세 · ★추적 공간 기준
        if (trackingSpace == null) { var rig = FindFirstObjectByType<OVRCameraRig>(); if (rig != null) trackingSpace = rig.trackingSpace; }
        Vector3 pos = trackingSpace != null ? trackingSpace.TransformPoint(pose.position) : pose.position;
        Quaternion rot = trackingSpace != null ? trackingSpace.rotation * pose.rotation : pose.rotation;
        overlayCam.transform.SetPositionAndRotation(pos, rot);

        Quaternion inv = Quaternion.Inverse(pose.rotation);   // 광선과 자세는 같은 공간(추적 공간)이다
        Vector3 lo = inv * pca.ViewportPointToRay(Vector2.zero).direction, hi = inv * pca.ViewportPointToRay(Vector2.one).direction;
        const float n = 0.03f, f = 30f;
        overlayCam.projectionMatrix = Matrix4x4.Frustum(lo.x / lo.z * n, hi.x / hi.z * n, lo.y / lo.z * n, hi.y / hi.z * n, n, f);
        Camera eyeCam = Camera.main;
        overlayCam.cullingMask = eyeCam != null ? eyeCam.cullingMask : ~0;
        overlayCam.Render();
        if (!overlayLogged)
        {
            overlayLogged = true;
            Debug.Log($"[선실측] 미러링 — 겹치기 카메라 · {r.x}×{r.y} · 화각 좌 {Mathf.Atan(lo.x / lo.z) * Mathf.Rad2Deg:F1}° 우 {Mathf.Atan(hi.x / hi.z) * Mathf.Rad2Deg:F1}° " +
                      $"아래 {Mathf.Atan(lo.y / lo.z) * Mathf.Rad2Deg:F1}° 위 {Mathf.Atan(hi.y / hi.z) * Mathf.Rad2Deg:F1}° · 카메라 {pos:F3} · 눈 {(eyeCam != null ? eyeCam.transform.position.ToString("F3") : "없음")} · " +
                      $"추적 공간 {(trackingSpace != null ? trackingSpace.position.ToString("F3") + " " + trackingSpace.rotation.eulerAngles.ToString("F1") : "★없음(추적 공간 = 월드로 본다)")} · 가림 마스크 {overlayCam.cullingMask}");
        }
        AsyncGPUReadback.Request(overlayRt, 0, TextureFormat.RGBA32, OnOverlayRead);
    }

    private void EnsureOverlay(Vector2Int r)
    {
        if (overlayRt != null && overlayRt.width == r.x && overlayRt.height == r.y) return;
        if (overlayRt != null) { overlayRt.Release(); Destroy(overlayRt); }
        overlayRt = new RenderTexture(r.x, r.y, 24, RenderTextureFormat.ARGB32) { name = "미러링 겹치기" };
        if (overlayCam == null)
        {
            var go = new GameObject("미러링 카메라(겹치기)");
            go.transform.SetParent(transform, false);
            overlayCam = go.AddComponent<Camera>();
            overlayCam.enabled = false;   // Render()로만 찍는다
            overlayCam.clearFlags = CameraClearFlags.SolidColor;
            overlayCam.backgroundColor = new Color(0f, 0f, 0f, 0f);   // 투명 바탕 — 알파가 곧 «앱이 그린 자리»다
            overlayCam.stereoTargetEye = StereoTargetEyeMask.None;
            overlayCam.allowHDR = false;
            overlayCam.allowMSAA = false;
        }
        overlayCam.targetTexture = overlayRt;
    }

    private void OnOverlayRead(AsyncGPUReadbackRequest req)
    {
        if (req.hasError || camBuf == null) { camBusy = false; return; }
        NativeArray<byte> data = req.GetData<byte>();
        if (data.Length != camBuf.Length) { camBusy = false; return; }
        if (overlayBuf == null || overlayBuf.Length != data.Length) overlayBuf = new byte[data.Length];
        data.CopyTo(overlayBuf);
        ThreadPool.QueueUserWorkItem(_ =>
        {
            // out = 가상 + 영상 × (1 − 알파) — 투명 바탕에 SrcAlpha 섞기로 그린 색은 이미 알파가 곱해져 있다
            byte[] c = camBuf, o = overlayBuf;
            for (int i = 0; i < c.Length; i += 4)
            {
                int a = o[i + 3];
                if (a == 0) continue;
                int ia = 255 - a;
                c[i] = (byte)Math.Min(255, o[i] + c[i] * ia / 255);
                c[i + 1] = (byte)Math.Min(255, o[i + 1] + c[i + 1] * ia / 255);
                c[i + 2] = (byte)Math.Min(255, o[i + 2] + c[i + 2] * ia / 255);
            }
            Encode(c, camW, camH, "cam");
        });
    }

    // ── 벽면UI ───────────────────────────────────────────────────────
    private void LateUpdate()   // 판이 이번 프레임의 글자 · 선으로 바뀐 뒤에 찍는다
    {
        if (monitor == null || monitor.Port == 0 || view == null || view.Panel == null || !view.PanelPlaced) return;
        float now = Time.unscaledTime;
        if (pca != null && pca.IsPlaying && !camBusy && now >= camNext && monitor.ImageWatched("cam") && pca.IsUpdatedThisFrame)
        {
            camNext = now + camInterval;
            CaptureCam();   // 겹칠 가상 화면이 이번 프레임의 끝점 · 선을 그리게 LateUpdate에서
        }
        if (wallBusy || now < wallNext || !monitor.ImageWatched("wall")) return;
        if (!SystemInfo.supportsAsyncGPUReadback)
        {
            Debug.LogWarning("[선실측] ★미러링 — 이 기기는 GPU 비동기 읽기를 지원하지 않는다(벽면UI 이미지 없음)");
            wallNext = float.MaxValue;   // 한 번만 남긴다
            return;
        }
        wallNext = now + wallInterval;

        EnsureWallCamera();
        Transform p = view.Panel;
        float k = p.lossyScale.x;
        float centerY = (WallTop + WallBottom) * 0.5f;
        wallCam.transform.SetPositionAndRotation(p.TransformPoint(new Vector3(0f, centerY, -CamBack)), p.rotation);
        wallCam.orthographicSize = (WallTop - WallBottom) * 0.5f * k;
        wallCam.nearClipPlane = (CamBack - SlabHalf) * k;
        wallCam.farClipPlane = (CamBack + SlabHalf) * k;
        wallCam.Render();

        wallBusy = true;
        AsyncGPUReadback.Request(wallRt, 0, TextureFormat.RGBA32, OnWallRead);
    }

    private void EnsureWallCamera()
    {
        if (wallCam != null) return;
        float aspect = RomLineView.PanelW / (WallTop - WallBottom);
        wallW = Mathf.Max(64, wallWidth);
        wallH = Mathf.Max(32, Mathf.RoundToInt(wallW / aspect));
        wallRt = new RenderTexture(wallW, wallH, 16, RenderTextureFormat.ARGB32) { name = "미러링 벽면UI" };
        var go = new GameObject("미러링 카메라(벽면UI)");
        go.transform.SetParent(transform, false);
        wallCam = go.AddComponent<Camera>();
        wallCam.enabled = false;   // Render()로만 찍는다
        wallCam.orthographic = true;
        wallCam.clearFlags = CameraClearFlags.SolidColor;
        wallCam.backgroundColor = wallBackground;
        wallCam.cullingMask = 1 << view.Panel.gameObject.layer;
        wallCam.stereoTargetEye = StereoTargetEyeMask.None;
        wallCam.allowHDR = false;
        wallCam.allowMSAA = false;
        wallCam.aspect = aspect;
        wallCam.targetTexture = wallRt;
        Debug.Log($"[선실측] 미러링 — 벽면UI 카메라를 세웠다 · {wallW}×{wallH} · 레이어 {view.Panel.gameObject.layer} · 판 배율 {view.Panel.lossyScale.x:F2}");
    }

    private void OnWallRead(AsyncGPUReadbackRequest req)
    {
        if (req.hasError || wallRt == null) { wallBusy = false; return; }
        NativeArray<byte> data = req.GetData<byte>();
        if (wallBuf == null || wallBuf.Length != data.Length) wallBuf = new byte[data.Length];
        data.CopyTo(wallBuf);
        ThreadPool.QueueUserWorkItem(_ => Encode(wallBuf, wallW, wallH, "wall"));
    }

    // ── 인코딩(스레드 풀) ──────────────────────────────────────────────
    private void Encode(byte[] rgba, int w, int h, string name)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            byte[] jpg = ImageConversion.EncodeArrayToJPG(rgba, GraphicsFormat.R8G8B8A8_SRGB, (uint)w, (uint)h, 0, jpegQuality);
            monitor?.SetImage(name, jpg);
        }
        catch (Exception e) { Debug.LogWarning($"[선실측] ★미러링 — {name} JPEG 실패({e.Message})"); }
        double ms = sw.Elapsed.TotalMilliseconds;
        if (name == "wall") { wallEncodeMs += ms; Interlocked.Increment(ref wallFrames); wallBusy = false; }
        else { camEncodeMs += ms; Interlocked.Increment(ref camFrames); camBusy = false; }
    }

    // ── 진단 ─────────────────────────────────────────────────────────
    private void Diagnose(float now)
    {
        float span = Mathf.Max(0.001f, now - diagFrom);
        int wf = Interlocked.Exchange(ref wallFrames, 0), cf = Interlocked.Exchange(ref camFrames, 0);
        double wms = wf > 0 ? wallEncodeMs / wf : 0, cms = cf > 0 ? camEncodeMs / cf : 0;
        wallEncodeMs = camEncodeMs = 0;
        bool worn = OVRManager.isHmdPresent && OVRManager.instance != null && OVRManager.instance.isUserPresent;

        info.Length = 0;
        info.Append("{\"camSupported\":").Append(PassthroughCameraAccess.IsSupported ? "true" : "false")
            .Append(",\"camState\":\"").Append(pcaState).Append('"')
            .Append(",\"camRes\":\"").Append(camResolution.x).Append('x').Append(camResolution.y).Append('"')
            .Append(",\"wallFps\":").Append((wf / span).ToString("F1", System.Globalization.CultureInfo.InvariantCulture))
            .Append(",\"camFps\":").Append((cf / span).ToString("F1", System.Globalization.CultureInfo.InvariantCulture))
            .Append(",\"wallMs\":").Append(wms.ToString("F0", System.Globalization.CultureInfo.InvariantCulture))
            .Append(",\"camMs\":").Append(cms.ToString("F0", System.Globalization.CultureInfo.InvariantCulture))
            .Append(",\"worn\":").Append(worn ? "true" : "false")
            .Append(",\"battery\":").Append(RomLineView.BatteryPercent())   // 10-21: 관제 화면 정보 줄의 배터리(-1 = 모름)
            .Append(",\"charging\":").Append(RomLineView.BatteryCharging() ? "true" : "false")
            .Append(",\"appFps\":").Append((1f / Mathf.Max(0.0001f, Time.smoothDeltaTime)).ToString("F0", System.Globalization.CultureInfo.InvariantCulture))
            .Append('}');
        monitor.SetMirrorInfo(info.ToString());

        if (monitor.ImageWatched("wall") || monitor.ImageWatched("cam"))
            Debug.Log($"[선실측] 미러링 진단 — 벽면UI {wf / span:F1}장/s · 인코딩 {wms:F0}ms · 현실 영상 {cf / span:F1}장/s · 인코딩 {cms:F0}ms · {pcaState} · 착용 {(worn ? "예" : "아니오")} · 앱 {1f / Mathf.Max(0.0001f, Time.smoothDeltaTime):F0}fps");
        diagFrom = now;
        diagNext = now + diagInterval;
    }

    private void OnDestroy()
    {
        if (wallRt != null) { wallRt.Release(); Destroy(wallRt); wallRt = null; }
        if (overlayRt != null) { overlayRt.Release(); Destroy(overlayRt); overlayRt = null; }
    }
}
