using System;
using System.Globalization;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.XR;

/// <summary>
/// ★추나 실습 웹 미러링 — 시험판(2026-10-20 사용자 요청: «ROM처럼 추나도 브라우저로 보고 싶다»).
///
/// 착용자가 보는 <b>눈 화면(왼쪽 눈)</b>을 다시 그리지 않고 복사해(HwMirrorTest와 같은 잡기 — BeforeImageEffects · CurrentActive Blit,
/// 10-20 기기에서 이 조합만 잡혔다), 고른 크기로 줄여 JPEG로 만들고, ROM과 같은 서버(<see cref="RomLineMonitor"/>)로 내보낸다.
///   GET /           화면(Resources/Mirror/chuna_mirror.html)
///   GET /cam.jpg    최신 한 장 — 브라우저가 받자마자 다음 장을 묻는다(물어 가는 화면이 없으면 찍지도 · 인코딩하지도 않는다)
///   GET /mirrorinfo 진단(장수 · 인코딩 시간 · 앱 fps · 해상도 · 씬)
///   GET /cmd?c=res0~2  출력 가로 640 · 800 · 1280(세로는 눈 화면 비율)
/// ★시험 전용 — 기기 속성으로만 켠다: adb shell setprop debug.coco.webmirror 1 (앱을 다시 시작해야 먹는다).
/// ★포트 8090은 선 실측(RomLineSession)의 모니터와 같다 — 같은 빌드에 둘이 같이 켜지면 늦게 연 쪽이 못 연다(지금 추나 빌드엔 선 실측 씬이 없다).
/// ★인증이 없다. 같은 망의 누구나 열 수 있다.
/// </summary>
public class ChunaWebMirror : MonoBehaviour
{
    private const int Port = 8090;
    private static readonly int[] Widths = { 640, 800, 1280 };
    private const float Interval = 0.05f;   // 최대 20장/s — 사용자 요구 12~24장/s
    private const int JpegQuality = 70;

#if UNITY_ANDROID && !UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Prop("debug.coco.webmirror", "0") != "1") return;
        var go = new GameObject("[ChunaWebMirror]");
        DontDestroyOnLoad(go);
        go.AddComponent<ChunaWebMirror>();
    }
#endif

    private static string Prop(string key, string def)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var sp = new AndroidJavaClass("android.os.SystemProperties"))
            {
                string v = sp.CallStatic<string>("get", key, def);
                return string.IsNullOrEmpty(v) ? def : v;
            }
        }
        catch (Exception e) { Debug.LogWarning($"[웹미러] 속성 {key} 못 읽음: {e.Message}"); }
#endif
        return def;
    }

    private readonly RomLineMonitor server = new RomLineMonitor();
    private Camera cam;
    private CommandBuffer cb;
    private const CameraEvent CamEvent = CameraEvent.BeforeImageEffects;
    private RenderTexture eyeCopy, outRt;
    private Material mat;
    private int choice, outW, outH, pendingChoice = -1;
    private bool flip;
    private float nextShot, diagNext, diagFrom;
    private volatile bool busy;
    private byte[] rgba;
    private int shots; private double encodeMs;
    private float aspect = 1f;   // 눈 화면 세로 / 가로
    private bool ready;
    private readonly StringBuilder info = new StringBuilder(256);

    private System.Collections.IEnumerator Start()
    {
        string html = Resources.Load<TextAsset>("Mirror/chuna_mirror")?.text ?? "";
        server.Start(Port, html);
        // 시작 직후엔 XR이 아직 눈 화면을 안 만든다(256×256) — 실제 크기가 나올 때까지 기다린다(HwMirrorTest와 같다)
        float until = Time.realtimeSinceStartup + 15f;
        while (Time.realtimeSinceStartup < until && (!XRSettings.isDeviceActive || XRSettings.eyeTextureDesc.width <= 256)) yield return null;
        yield return null;

        RenderTextureDescriptor eye = XRSettings.eyeTextureDesc;
        var d = eye;
        d.msaaSamples = 1;
        d.depthBufferBits = 0;
        d.dimension = TextureDimension.Tex2DArray;
        d.volumeDepth = Mathf.Max(1, eye.volumeDepth);
        eyeCopy = new RenderTexture(d) { name = "WebMirrorEyeCopy" };
        eyeCopy.Create();
        aspect = eye.width > 0 ? (float)eye.height / eye.width : 1f;

        Shader sh = Resources.Load<Shader>("Mirror/HwMirrorNV12");
        if (sh == null || !sh.isSupported) { Debug.LogError("[웹미러] ★셰이더 없음/미지원"); enabled = false; yield break; }
        mat = new Material(sh);
        mat.SetFloat("_Slice", 0f);
        flip = Prop("debug.coco.webmirror.flip", "1") == "1";   // ★읽어 온 줄은 RT의 v = 0부터 온다(10-20 HwMirror) — JPEG는 맨 윗줄부터라 기본으로 뒤집는다(기기 이미지로 확인할 것)
        mat.SetFloat("_FlipY", flip ? 1f : 0f);

        cb = new CommandBuffer { name = "WebMirrorCapture" };
        cb.Blit(BuiltinRenderTextureType.CurrentActive, eyeCopy);
        SetChoice(0);
        ready = true;
        Debug.Log($"[웹미러] 시작 — {server.Url} · 눈 화면 {eye.width}×{eye.height} {eye.dimension} · 출력 {outW}×{outH} · 뒤집기 {flip} · 그래픽 {SystemInfo.graphicsDeviceType}");
    }

    private void SetChoice(int c)
    {
        choice = Mathf.Clamp(c, 0, Widths.Length - 1);
        outW = Widths[choice];
        outH = Mathf.Max(16, Mathf.RoundToInt(outW * aspect / 2f) * 2);
        if (outRt != null) { outRt.Release(); Destroy(outRt); }
        outRt = new RenderTexture(outW, outH, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) { name = "WebMirrorOut" };   // 셰이더가 감마로 써 넣는다 — 다시 바꾸지 않게 Linear
        outRt.Create();
    }

    private void Update()
    {
        while (server.TryDequeue(out string c))
        {
            if (c.Length == 4 && c.StartsWith("res") && c[3] >= '0' && c[3] <= '2') pendingChoice = c[3] - '0';
            else Debug.LogWarning($"[웹미러] ★모르는 명령 «{c}»");
        }
        if (!ready) return;
        float now = Time.unscaledTime;

        // 씬이 바뀌면 눈 카메라도 새로 생긴다(로비 → 실습) — 잡기 명령을 새 카메라로 옮긴다
        Camera main = Camera.main;
        if (main != null && main != cam)
        {
            if (cam != null) cam.RemoveCommandBuffer(CamEvent, cb);
            cam = main;
            cam.AddCommandBuffer(CamEvent, cb);
            Debug.Log($"[웹미러] 카메라 붙임 — '{cam.name}' · 씬 {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
        }

        if (pendingChoice >= 0 && !busy)
        {
            Debug.Log($"[웹미러] 해상도 {outW}×{outH} → 가로 {Widths[pendingChoice]}");
            SetChoice(pendingChoice);
            pendingChoice = -1;
        }

        if (now >= diagNext) Diagnose(now);
        if (busy || now < nextShot || cam == null || !server.ImageWatched("cam")) return;
        nextShot = now + Interval;
        busy = true;
        Graphics.Blit(eyeCopy, outRt, mat, 2);
        AsyncGPUReadback.Request(outRt, 0, TextureFormat.RGBA32, OnRead);
    }

    private void OnRead(AsyncGPUReadbackRequest r)
    {
        if (r.hasError) { busy = false; return; }
        var data = r.GetData<byte>();
        int w = r.width, h = r.height;
        if (rgba == null || rgba.Length != data.Length) rgba = new byte[data.Length];
        data.CopyTo(rgba);
        ThreadPool.QueueUserWorkItem(_ =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { server.SetImage("cam", ImageConversion.EncodeArrayToJPG(rgba, GraphicsFormat.R8G8B8A8_UNorm, (uint)w, (uint)h, 0, JpegQuality)); }
            catch (Exception e) { Debug.LogWarning($"[웹미러] ★JPEG 실패({e.Message})"); }
            encodeMs += sw.Elapsed.TotalMilliseconds;
            Interlocked.Increment(ref shots);
            busy = false;
        });
    }

    private void Diagnose(float now)
    {
        float span = Mathf.Max(0.001f, now - diagFrom);
        int n = Interlocked.Exchange(ref shots, 0);
        double ms = n > 0 ? encodeMs / n : 0;
        encodeMs = 0;
        var ci = CultureInfo.InvariantCulture;
        info.Length = 0;
        info.Append("{\"camSupported\":true,\"camFps\":").Append((n / span).ToString("F1", ci))
            .Append(",\"camMs\":").Append(ms.ToString("F0", ci))
            .Append(",\"camRes\":\"").Append(outW).Append('x').Append(outH).Append('"')
            .Append(",\"choice\":").Append(choice)
            .Append(",\"appFps\":").Append((1f / Mathf.Max(0.0001f, Time.smoothDeltaTime)).ToString("F0", ci))
            .Append(",\"scene\":\"").Append(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name).Append('"');
        // ★10-20 사용자: 교육용(추나 실습)은 핸드트래킹이다 — 손이 잡히는지를 보인다(컨트롤러를 쥐고 있으면 그것도)
        bool lh = OVRInput.IsControllerConnected(OVRInput.Controller.LHand), rh = OVRInput.IsControllerConnected(OVRInput.Controller.RHand);
        bool lt = OVRInput.IsControllerConnected(OVRInput.Controller.LTouch), rt = OVRInput.IsControllerConnected(OVRInput.Controller.RTouch);
        info.Append(",\"handOn\":").Append(OVRPlugin.GetHandTrackingEnabled() ? "true" : "false")
            .Append(",\"hands\":\"").Append(lh && rh ? "양손" : lh ? "왼손만" : rh ? "오른손만" : "안 잡힘").Append('"')
            .Append(",\"touch\":\"").Append(lt && rt ? "양쪽" : lt ? "왼쪽" : rt ? "오른쪽" : "없음").Append('"');
        bool worn = OVRManager.isHmdPresent && OVRManager.instance != null && OVRManager.instance.isUserPresent;
        info.Append(",\"worn\":").Append(worn ? "true" : "false").Append('}');
        server.SetMirrorInfo(info.ToString());
        if (server.ImageWatched("cam"))
            Debug.Log($"[웹미러] 진단 — {n / span:F1}장/s · 인코딩 {ms:F0}ms · {outW}×{outH} · 앱 {1f / Mathf.Max(0.0001f, Time.smoothDeltaTime):F0}fps");
        diagFrom = now;
        diagNext = now + 2f;
    }

    private void OnDestroy()
    {
        if (cam != null && cb != null) cam.RemoveCommandBuffer(CamEvent, cb);
        server.Stop();
        AsyncGPUReadback.WaitAllRequests();
        if (eyeCopy != null) eyeCopy.Release();
        if (outRt != null) outRt.Release();
    }
}
