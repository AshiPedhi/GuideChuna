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
    [Tooltip("보내는 영상 크기(관제 화면 해상도 선택). 앱 화면은 이 크기로 그리고, 현실 영상은 카메라가 낼 수 있는 4:3 크기로 받아 늘이거나 줄여 맞춘다.")]
    [SerializeField] private Vector2Int camResolution = new Vector2Int(1280, 960);   // 10-23 저녁 사용자 지시: 기본 1280×960(종전 1024×768 · 런타임 AddComponent라 코드 기본값이 먹는다)
    private Vector2Int camRequest;   // 카메라에 실제로 요청하는 크기 — CameraSizeFor(camResolution)
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
    // 11-13 추나 앱에 합침(A안) — 추나 미러링과 같은 대기(ChunaWebMirror.AsyncReadbackReady: 앱 시작 20초 · 씬 불러온 뒤 5초)를 지난 뒤에 GPU 비동기 읽기를 시작한다.
    //   종전(별도 ROM 앱)엔 대기 없이 읽었다 — GLES 컴퓨트 버퍼 첫 생성 크래시(11-04 · 11-11 심볼)와 같은 길이다.
    private bool readWaitLogged, readWaitDone;
    // ★11-13 기기 실측(3S · 추나 앱): 대기 20초를 다 기다려도 <b>첫 비동기 읽기 0.4초 뒤</b> 같은 크래시(memset 널)가 났다 — 대기는 회피가 아니었다.
    //   동기 읽기(ReadPixels)로는 대기 없이 60초+ 멀쩡 · 영상 15장/s · 앱 72fps · 한 장 평균 5.3ms. 11-12 ROM 별도 앱의 9연속 크래시도 같은 서명.
    //   → 기본을 동기 읽기로. adb shell setprop debug.coco.webmirror.sync 0 이면 예전 비동기(추나 미러링과 같은 스위치).
    private bool syncRead = true;
    private Texture2D syncVideoTex, syncOverlayTex, syncWallTex;
    private bool syncVideoLogged;

    /// <summary>rt를 주 스레드에서 바로 읽는다(ReadPixels). 돌려준 배열은 다음 ReadPixels 전까지만 유효 — 바로 복사한다.</summary>
    private static NativeArray<byte> ReadSync(RenderTexture rt, ref Texture2D tex, TextureFormat fmt)
    {
        if (tex == null || tex.width != rt.width || tex.height != rt.height || tex.format != fmt)
        {
            if (tex != null) Destroy(tex);
            tex = new Texture2D(rt.width, rt.height, fmt, false, true) { name = "미러링 동기 읽기 " + rt.name };
        }
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
        RenderTexture.active = prev;
        return tex.GetRawTextureData<byte>();
    }
    // 11-13 영상 포트(8092) 다시 시도 — 추나 미러링이 막 비켜 준 직후라 아직 닫히는 중일 수 있다
    private float videoRetryAt = -1f;
    private int videoTries;

    private float diagNext, diagFrom;

    /// <summary>관제 화면의 해상도 버튼(10-20 사용자 지시 · 우측 상단 셋). 전부 4:3 — 화면 칸의 비율이 안 바뀐다. 지원 목록은 기기 로그로 확인(320×240 ~ 1280×1280).
    /// ★10-23 사용자 지시: 800×600 · 1200×900 · 1600×1200(종전 640×480 · 800×600 · 1280×960). 
    /// ★10-23 기기 실측: 카메라는 1200×900 · 1600×1200을 못 낸다(1200×900 → 800×600으로 열림 · 1600×1200 → 1280×1080) → 사용자 지시로
    ///   «앱 화면은 고른 크기로 그리고 현실 영상만 카메라 크기로 받아 합성»한다(영상 경로). 카메라 크기는 CameraSizeFor가 지원 목록에서 고른다.
    /// ★10-23 오후 사용자 지시: 800×600 · 1024×768 · 1280×960 — 현실 영상의 상한이 1280×960이라(앱이 받는 카메라 · 화면 녹화는 매번 동의 창이라 제외) 그 위는 뺐다.
    ///   카메라의 4:3 크기는 800×600 다음이 1280×960뿐이라 1024×768은 카메라를 1280×960으로 받아 줄여 보낸다.
    /// ★10-23 저녁 사용자 지시: 800×600 · 1280×960 두 개만(기본 1280×960) — 1024×768은 카메라가 1280×960과 같아 부하가 같았다(실측 CPU 128~142 vs 129~137%).</summary>
    public static readonly Vector2Int[] CamChoices = { new Vector2Int(800, 600), new Vector2Int(1280, 960) };
    private Vector2Int pendingRes;   // 바꿀 해상도 — 인코딩 중인 장이 끝난 뒤(camBusy 아님)에 카메라를 다시 연다
    private readonly StringBuilder info = new StringBuilder(512);

    public void Init(RomLineMonitor m, RomLineView v)
    {
        monitor = m;
        view = v;
#if UNITY_ANDROID && !UNITY_EDITOR
        syncRead = AndroidProp("debug.coco.webmirror.sync") != "0";
#endif
        Debug.Log($"[선실측] 미러링 읽기 방식 — {(syncRead ? "동기(ReadPixels) · 대기 없음" : "★비동기(AsyncGPUReadback) · 시작 20초 · 씬 5초 대기")}");
        StartCamera();
        StartVideo();
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
        supportedRes = res;
        camRequest = CameraSizeFor(camResolution);
        pca.RequestedResolution = camRequest;
        // ★10-21 사용자 지시: 카메라는 보는 화면이 붙을 때 켠다(UpdateCameraPower) — 아무도 안 보면 미러링 부담이 거의 0이 되게.
        pcaState = "꺼짐(보는 화면 없음)";
    }

    // ── 카메라 켜고 끄기(10-21) ── 영상 접속(videoClients) 또는 사진 요청(/cam.jpg)이 있으면 켜고,
    //   아무도 안 보는 상태가 CamIdleOff초 이어지면 끈다(잠깐 끊겼다 다시 붙는 경우에 카메라를 껐다 켜지 않게).
    //   켤 때는 StartCamera와 같은 길(OnEnable이 RequestedResolution으로 연다) · 첫 영상까지 약 1초(추정 · 기기에서 볼 것).
    private const float CamIdleOff = 5f;
    private float camIdleSince = -1f;

    private void UpdateCameraPower(float now)
    {
        if (pca == null) return;
        bool want = videoClients > 0 || monitor.ImageWatched("cam");
        if (want) camIdleSince = -1f;
        else if (camIdleSince < 0f) camIdleSince = now;
        bool on = pca.gameObject.activeSelf;
        if (want && !on)
        {
            pca.RequestedResolution = camRequest;
            pca.gameObject.SetActive(true);
            pcaShownRes = default;
            pcaState = "켜는 중";
            Debug.Log("[선실측] 미러링 — 현실 영상: 보는 화면이 붙어 카메라를 켠다");
        }
        else if (!want && on && now - camIdleSince >= CamIdleOff && !camBusy && !AnyVideoBusy())
        {
            pca.gameObject.SetActive(false);
            pcaState = "꺼짐(보는 화면 없음)";
            Debug.Log($"[선실측] 미러링 — 현실 영상: {CamIdleOff:F0}초 동안 보는 화면이 없어 카메라를 끈다");
        }
    }

    private bool AnyVideoBusy()
    {
        for (int i = 0; i < VideoSlots; i++) if (vBusy[i]) return true;
        return false;
    }

    /// <summary>현실 영상 해상도를 바꾼다(관제 화면 버튼 → /cmd?c=camN). 실제로 바꾸는 건 Update — 쓰던 버퍼를 인코딩 스레드가 아직 들고 있을 수 있다.</summary>
    public void RequestCamResolution(int choice)
    {
        if (choice < 0 || choice >= CamChoices.Length) return;
        if (pca == null) { Debug.LogWarning("[선실측] ★미러링 — 해상도 바꾸기: 현실 영상이 없다(" + pcaState + ")"); return; }
        pendingRes = CamChoices[choice];
    }

    private Vector2Int[] supportedRes;

    /// <summary>보내는 크기에 쓸 카메라 크기 — 지원 목록 중 같은 비율(4:3)에서 가로가 보내는 크기 이상인 가장 작은 것, 없으면 그 비율의 가장 큰 것.
    /// 10-23 Quest 3S 실측 목록: 800×600 → 800×600 · 1200×900 → 1280×960 · 1600×1200 → 1280×960(지금 고르는 1024×768 · 1280×960 → 1280×960). 목록이 없으면 그대로 요청한다.</summary>
    private Vector2Int CameraSizeFor(Vector2Int outRes)
    {
        if (supportedRes == null || supportedRes.Length == 0) return outRes;
        Vector2Int best = default, largest = default;
        foreach (var s in supportedRes)
        {
            if (s.x * outRes.y != s.y * outRes.x) continue;   // 비율이 다르면 겹치기 화각이 어긋난다
            if (s.x > largest.x) largest = s;
            if (s.x >= outRes.x && (best.x == 0 || s.x < best.x)) best = s;
        }
        if (best.x > 0) return best;
        return largest.x > 0 ? largest : outRes;
    }

    private void Update()
    {
        if (monitor == null || monitor.Port == 0) return;
        float now = Time.unscaledTime;

        if (pendingRes != default && pca != null && !camBusy)
        {
            Vector2Int req = CameraSizeFor(pendingRes);
            Debug.Log($"[선실측] 미러링 — 보내는 크기 {camResolution.x}×{camResolution.y} → {pendingRes.x}×{pendingRes.y} · 카메라 요청 {camRequest.x}×{camRequest.y} → {req.x}×{req.y}");
            camResolution = pendingRes;
            pendingRes = default;
            if (req != camRequest)   // 카메라 크기가 같으면 다시 열지 않는다(영상 크기만 바뀐다)
            {
                camRequest = req;
                bool wasOn = pca.gameObject.activeSelf;
                pca.gameObject.SetActive(false);   // OnEnable이 RequestedResolution으로 다시 연다(StartCamera와 같은 길)
                pca.RequestedResolution = camRequest;
                if (wasOn) { pca.gameObject.SetActive(true); pcaShownRes = default; pcaState = "해상도 바꾸는 중"; }   // 꺼져 있었으면 켤 때 새 해상도로 연다
            }
        }

        if (pca != null && pca.IsPlaying)
        {
            if (pca.CurrentResolution != pcaShownRes)   // 바뀔 때만 글자를 만든다(매 프레임 할당 금지)
            {
                pcaShownRes = pca.CurrentResolution;
                pcaState = $"받는 중 {pcaShownRes.x}×{pcaShownRes.y}";
                Debug.Log($"[선실측] 미러링 — 현실 영상: 첫 영상 · {pcaState}(카메라 요청 {camRequest.x}×{camRequest.y} · 보내는 크기 {camResolution.x}×{camResolution.y})");
            }
        }

        if (video != null && now >= videoPollNext)   // 붙은 화면 수 — 0.5초마다(JNI 호출이라 매 프레임 하지 않는다)
        {
            videoPollNext = now + 0.5f;
            int c = video.Call<int>("clientCount");
            if (c != videoClients)
            {
                Debug.Log($"[선실측] 미러링 영상 — 보는 화면 {videoClients} → {c}");
                videoClients = c;
                videoState = c > 0 ? "보내는 중 · 화면 " + c : "대기(보는 화면 없음)";
            }
        }

        if (video == null && videoRetryAt >= 0f && now >= videoRetryAt)
        {
            StartVideo();
            if (video != null) { videoRetryAt = -1f; Debug.Log($"[선실측] 미러링 영상 — 포트 {VideoPort} 다시 열었다({videoTries}번째)"); }
            else if (++videoTries <= 10) videoRetryAt = now + 1f;
            else { videoRetryAt = -1f; Debug.LogWarning($"[선실측] ★미러링 영상 — 포트 {VideoPort}를 10번 못 열었다 — JPEG로만 보낸다"); }
        }

        UpdateCameraPower(now);
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
        if (!camOverlay || (!syncRead && !SystemInfo.supportsAsyncGPUReadback))
        {
            ThreadPool.QueueUserWorkItem(_ => Encode(camBuf, camW, camH, "cam"));
            return;
        }

        RenderOverlay(r);
        if (syncRead) ProcessOverlay(ReadSync(overlayRt, ref syncOverlayTex, TextureFormat.RGBA32));
        else AsyncGPUReadback.Request(overlayRt, 0, TextureFormat.RGBA32, OnOverlayRead);
    }

    /// <summary>앱 화면을 패스스루 카메라 자리 · 화각에서 투명 바탕에 다시 그린다(overlayRt). JPEG · 영상 경로가 같이 쓴다.</summary>
    private void RenderOverlay(Vector2Int r)
    {
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
    }

    // ── 영상(H.264) 경로(10-21 사용자 지시: JPEG 대신 하드웨어 압축 — 배터리 · 발열) ─────────────
    //   GPU에서 «현실 영상 + 앱 겹치기 → NV12»(RomMirrorNV12 셰이더) → 비동기 읽기 → Java RomVideoEncoder(MediaCodec AVC) → :8092 /video.
    //   CPU는 읽어 온 NV12를 압축기 입력 칸에 옮기는 일만 한다(종전 JPEG 경로: 겹치기 · JPEG 압축을 CPU가 했다).
    //   보는 화면(브라우저)이 붙어 있을 때만 찍는다. 붙은 화면이 없거나 기기가 지원 안 하면 종전 JPEG 경로(/cam.jpg)가 그대로 돈다.
    private const int VideoPort = 8092;
    private const int VideoSlots = 3;
    private AndroidJavaObject video;
    private IntPtr videoFeed;
    private readonly jvalue[] videoArgs = new jvalue[2];
    private readonly NativeArray<byte>[] vBufs = new NativeArray<byte>[VideoSlots];
    private readonly IntPtr[] vJBufs = new IntPtr[VideoSlots];
    private readonly bool[] vBusy = new bool[VideoSlots];
    private RenderTexture nv12Rt;
    private Material nv12Mat = null;   // Android 분기에서만 만든다
    private Vector2Int videoRes;
    private int videoClients, vidFed;
    private float videoPollNext, vidNext;
    private string videoState = "꺼짐";
    private readonly System.Diagnostics.Stopwatch videoClock = System.Diagnostics.Stopwatch.StartNew();

    private void StartVideo()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!syncRead && !SystemInfo.supportsAsyncGPUReadback) { videoState = "지원 안 함(GPU 비동기 읽기 없음)"; Debug.Log("[선실측] 미러링 영상 — " + videoState); return; }
        Shader sh = Resources.Load<Shader>("RomLine/RomMirrorNV12");
        if (sh == null || !sh.isSupported) { videoState = "셰이더 없음"; Debug.LogWarning("[선실측] ★미러링 영상 — 셰이더 RomLine/RomMirrorNV12가 없거나 지원 안 함"); return; }
        if (nv12Mat == null) nv12Mat = new Material(sh);   // 11-13 포트 재시도 때 다시 만들지 않는다
        nv12Mat.SetFloat("_FlipY", AndroidProp("debug.coco.romvideo.flip") == "1" ? 1f : 0f);   // 기기에서 거꾸로 나오면 adb shell setprop debug.coco.romvideo.flip 1
        try
        {
            video = new AndroidJavaObject("com.coco.chuna.mirror.RomVideoEncoder");
            if (!video.Call<bool>("listen", VideoPort))
            {
                video.Dispose(); video = null;
                videoState = $"포트 {VideoPort}를 못 열었다";
                Debug.LogWarning("[선실측] ★미러링 영상 — " + videoState + " — 1초 뒤 다시(그동안 JPEG)");
                if (videoTries == 0) videoRetryAt = Time.unscaledTime + 1f;   // 첫 실패에서만 예약 — 뒤는 Update가 센다
                return;
            }
            videoFeed = AndroidJNI.GetMethodID(video.GetRawClass(), "feed", "(Ljava/nio/ByteBuffer;J)Z");
            videoState = "대기(보는 화면 없음)";
            Debug.Log($"[선실측] 미러링 영상 — 열었다 · 포트 {VideoPort} · 보는 화면이 붙으면 압축을 시작한다");
        }
        catch (Exception e)
        {
            video = null;
            videoState = "시작 실패";
            Debug.LogWarning("[선실측] ★미러링 영상 — 시작 실패(" + e.Message + ") — JPEG로만 보낸다");
        }
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private static string AndroidProp(string key)
    {
        try { using (var sp = new AndroidJavaClass("android.os.SystemProperties")) return sp.CallStatic<string>("get", key, ""); }
        catch (Exception) { return ""; }
    }
#endif

    /// <summary>압축기 · NV12 버퍼를 지금 해상도에 맞춘다. 읽는 중인 칸이 있으면 다음 장으로 미룬다(false).</summary>
    private bool EnsureVideo(Vector2Int r)
    {
        if (videoRes == r && nv12Rt != null) return true;
        for (int i = 0; i < VideoSlots; i++) if (vBusy[i]) return false;
        FreeVideoBuffers();
        int fps = Mathf.Max(1, Mathf.RoundToInt(1f / Mathf.Max(0.001f, camInterval)));
        int bitrate = Mathf.Max(800000, Mathf.RoundToInt(r.x * r.y * fps * 0.3f));   // 640×480 · 15장 ≈ 1.4Mbps
        string name = video.Call<string>("configure", r.x, r.y, fps, bitrate);
        if (name.StartsWith("실패")) { videoState = "압축기 " + name; Debug.LogWarning("[선실측] ★미러링 영상 — " + videoState); return false; }
        nv12Rt = new RenderTexture(r.x, r.y * 3 / 2, 0, RenderTextureFormat.R8, RenderTextureReadWrite.Linear) { name = "미러링 영상 NV12" };
        nv12Rt.Create();
        nv12Mat.SetVector("_OutSize", new Vector4(r.x, r.y, 1f / r.x, 1f / r.y));
        int len = r.x * r.y * 3 / 2;
        for (int i = 0; i < VideoSlots; i++)
        {
            vBufs[i] = new NativeArray<byte>(len, Allocator.Persistent, NativeArrayOptions.ClearMemory);
#if UNITY_ANDROID && !UNITY_EDITOR
            IntPtr local = AndroidJNI.NewDirectByteBuffer(vBufs[i]);
            vJBufs[i] = AndroidJNI.NewGlobalRef(local);
            AndroidJNI.DeleteLocalRef(local);
#endif
        }
        videoRes = r;
        Debug.Log($"[선실측] 미러링 영상 — 압축기 {r.x}×{r.y} · {fps}장/s · {bitrate / 1000}kbps · {name}");
        return true;
    }

    private void FreeVideoBuffers()
    {
        for (int i = 0; i < VideoSlots; i++)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (vJBufs[i] != IntPtr.Zero) { AndroidJNI.DeleteGlobalRef(vJBufs[i]); vJBufs[i] = IntPtr.Zero; }
#endif
            if (vBufs[i].IsCreated) vBufs[i].Dispose();
        }
        if (nv12Rt != null) { nv12Rt.Release(); Destroy(nv12Rt); nv12Rt = null; }
        videoRes = default;
    }

    private void CaptureVideo(float now)
    {
        // ★10-23: 보내는 크기 = 고른 해상도(camResolution) — 앱 화면은 이 크기로 그리고, 현실 영상은 셰이더가 uv로 읽어 늘이거나 줄인다.
        if (pca.CurrentResolution.x <= 0) return;
        Vector2Int r = camResolution;
        if (!EnsureVideo(r)) return;
        int slot = -1;
        for (int i = 0; i < VideoSlots; i++) if (!vBusy[i]) { slot = i; break; }
        if (slot < 0) return;
        vidNext = Mathf.Max(vidNext + camInterval, now - camInterval);   // 간격을 쌓아 간다 — now + 간격이면 72fps에서 한 장씩 걸러진다(HwMirrorTest 10-20)

        Texture cam = pca.GetTexture();
        if (camOverlay) { RenderOverlay(r); nv12Mat.SetTexture("_OverlayTex", overlayRt); }
        else nv12Mat.SetTexture("_OverlayTex", Texture2D.blackTexture);   // 알파 0 — 영상만
        nv12Mat.SetTexture("_CamTex", cam);
        Graphics.Blit(cam, nv12Rt, nv12Mat, 0);
        vBusy[slot] = true;
        int s = slot;
        if (syncRead)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var data = ReadSync(nv12Rt, ref syncVideoTex, TextureFormat.R8);
            bool ok = data.Length == vBufs[s].Length;
            if (ok) data.CopyTo(vBufs[s]);
            if (!syncVideoLogged)
            {
                syncVideoLogged = true;
                if (ok) Debug.Log($"[선실측] 미러링 영상 — 동기 읽기 첫 장 {nv12Rt.width}×{nv12Rt.height} R8 · {sw.Elapsed.TotalMilliseconds:F1}ms · 첫 바이트 {data[0]} · 가운데 {data[data.Length / 3]}(0만 나오면 읽기 실패)");
                else Debug.LogError($"[선실측] ★미러링 영상 — 동기 읽기 크기가 안 맞는다(읽음 {data.Length} · 칸 {vBufs[s].Length})");
            }
            if (ok) FeedVideo(s);
            vBusy[s] = false;
            return;
        }
        AsyncGPUReadback.RequestIntoNativeArray(ref vBufs[s], nv12Rt, 0, req => OnVideoRead(req, s));
    }

    private void OnVideoRead(AsyncGPUReadbackRequest req, int slot)
    {
        try
        {
            if (req.hasError) return;
            FeedVideo(slot);
        }
        finally { vBusy[slot] = false; }
    }

    private void FeedVideo(int slot)
    {
        {
            if (!vBufs[slot].IsCreated || video == null) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            videoArgs[0].l = vJBufs[slot];
            videoArgs[1].j = videoClock.ElapsedTicks * 1000000L / System.Diagnostics.Stopwatch.Frequency;
            if (AndroidJNI.CallBooleanMethod(video.GetRawObject(), videoFeed, videoArgs)) vidFed++;
#endif
        }
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
        if (req.hasError) { camBusy = false; return; }
        ProcessOverlay(req.GetData<byte>());
    }

    private void ProcessOverlay(NativeArray<byte> data)
    {
        if (camBuf == null || data.Length != camBuf.Length) { camBusy = false; return; }
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
        if (!readWaitDone && !syncRead)   // 동기 읽기면 기다릴 이유가 없다(11-13 기기 실측)
        {
            if (!ChunaWebMirror.AsyncReadbackReady(out float left))
            {
                if (!readWaitLogged) { readWaitLogged = true; Debug.Log($"[선실측] 미러링 — GPU 읽기 대기 {left:F1}초(GLES 첫 읽기 크래시 회피 · 추나 미러링과 같은 기준)"); }
                return;
            }
            readWaitDone = true;
            if (readWaitLogged) Debug.Log("[선실측] 미러링 — 대기 끝 · GPU 읽기 시작");
        }
        float now = Time.unscaledTime;
        if (videoClients > 0 && video != null && pca != null && pca.IsPlaying && pca.IsUpdatedThisFrame && now >= vidNext)
            CaptureVideo(now);   // 영상 경로 — 보는 화면이 붙어 있을 때만
        if (pca != null && pca.IsPlaying && !camBusy && now >= camNext && monitor.ImageWatched("cam") && pca.IsUpdatedThisFrame)
        {
            camNext = now + camInterval;
            CaptureCam();   // 겹칠 가상 화면이 이번 프레임의 끝점 · 선을 그리게 LateUpdate에서
        }
        if (wallBusy || now < wallNext || !monitor.ImageWatched("wall")) return;
        if (!syncRead && !SystemInfo.supportsAsyncGPUReadback)
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
        if (syncRead) ProcessWall(ReadSync(wallRt, ref syncWallTex, TextureFormat.RGBA32));
        else AsyncGPUReadback.Request(wallRt, 0, TextureFormat.RGBA32, OnWallRead);
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
        if (req.hasError) { wallBusy = false; return; }
        ProcessWall(req.GetData<byte>());
    }

    private void ProcessWall(NativeArray<byte> data)
    {
        if (wallRt == null) { wallBusy = false; return; }
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
        int vf = vidFed; vidFed = 0;
        string vStats = video != null ? video.Call<string>("stats") : "";
        if (videoClients > 0) { cf = vf; cms = 0; }   // 영상으로 보는 중이면 «헤드셋 장수»는 압축기에 넣은 장수다

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
            .Append(",\"videoPort\":").Append(video != null ? VideoPort : 0)   // 10-21 영상 경로 — 0이면 화면은 JPEG로만 받는다
            .Append(",\"videoState\":\"").Append(videoState).Append('"');
        if (vStats.Length > 0) info.Append(',').Append(vStats);
        info.Append('}');
        monitor.SetMirrorInfo(info.ToString());

        if (videoClients > 0)
            Debug.Log($"[선실측] 미러링 영상 진단 — 넣음 {vf / span:F1}장/s · {videoState} · Java {vStats} · 앱 {1f / Mathf.Max(0.0001f, Time.smoothDeltaTime):F0}fps");
        if (monitor.ImageWatched("wall") || monitor.ImageWatched("cam"))
            Debug.Log($"[선실측] 미러링 진단 — 벽면UI {wf / span:F1}장/s · 인코딩 {wms:F0}ms · 현실 영상 {cf / span:F1}장/s · 인코딩 {cms:F0}ms · {pcaState} · 착용 {(worn ? "예" : "아니오")} · 앱 {1f / Mathf.Max(0.0001f, Time.smoothDeltaTime):F0}fps");
        diagFrom = now;
        diagNext = now + diagInterval;
    }

    private void OnDestroy()
    {
        if (wallRt != null) { wallRt.Release(); Destroy(wallRt); wallRt = null; }
        if (overlayRt != null) { overlayRt.Release(); Destroy(overlayRt); overlayRt = null; }
        if (video != null) { try { video.Call("stop"); } catch (Exception) { } video.Dispose(); video = null; }
        AsyncGPUReadback.WaitAllRequests();
        FreeVideoBuffers();
        if (syncVideoTex != null) Destroy(syncVideoTex);
        if (syncOverlayTex != null) Destroy(syncOverlayTex);
        if (syncWallTex != null) Destroy(syncWallTex);
    }
}
