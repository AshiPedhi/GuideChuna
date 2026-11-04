using System;
using System.Globalization;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.XR;

/// <summary>
/// ★추나 실습 웹 미러링 — 시험판(2026-10-20 사용자 요청: «ROM처럼 추나도 브라우저로 보고 싶다» · 10-21 영상 방식 · 관제 화면 틀 통일).
///
/// 착용자가 보는 <b>눈 화면(왼쪽 눈)</b>을 다시 그리지 않고 복사해(HwMirrorTest와 같은 잡기 — BeforeImageEffects · CurrentActive Blit,
/// 10-20 기기에서 이 조합만 잡혔다) 내보낸다. 서버는 ROM과 같은 <see cref="RomLineMonitor"/>다.
///   :8092 GET /video    ★영상(H.264) — <see cref="MirrorVideoSender"/>(10-21 · ROM과 같은 Java 압축기). 화면이 붙어 있을 때만 압축한다
///   :8090 GET /         관제 화면(Resources/Mirror/chuna_mirror.html — ROM 관제 화면과 같은 틀)
///   :8090 GET /state    술기 · 단계(n/전체) · 지시문 · 판정(실시간 유사도 · 파지 · 접촉) · 경과 시간 · 손 추적(10-21)
///   :8090 GET /cam.jpg  사진(JPEG) — 영상이 안 될 때 브라우저가 되돌아오는 길
///   :8090 GET /mirrorinfo 진단(장수 · 해상도 · 앱 fps · 배터리 · 영상 포트)
///   :8090 GET /cmd?c=res0~2  출력 가로 640 · 800 · 1280(세로는 눈 화면 비율 · 16의 배수)
/// ★현실 모드(패스스루)에서는 눈 화면의 비워 둔 자리가 복사본에 검게 남는다 → 셰이더가 회색으로 채운다(10-21 사용자 선택 1안 —
///   카메라를 켜거나 장면을 다시 그리지 않는다. 실제 방은 안 보인다).
/// ★10-23 사용자 지시: 늘 켜 둔다(종전 10-20 시험판은 속성 1일 때만 켜서 재부팅하면 꺼졌다 — 헤드셋에서 켤 길이 없었다).
///   끄기만 속성으로: adb shell setprop debug.coco.webmirror 0. 선 실측 씬(RomLineScene)으로 뜬 판은 켜지 않는다 — 그 씬의 모니터가 같은 8090을 쓴다.
///   보는 화면이 없으면 복사 · 압축은 안 돈다(서버만 대기) — 켠 채 대기 부담은 기기에서 아직 안 쟀다.
/// ★포트 8090은 선 실측(RomLineSession)의 모니터와 같다 — 같은 빌드에 둘이 같이 켜지면 늦게 연 쪽이 못 연다(10-21 ROM 판에서 실제로 부딪혔다).
/// ★인증이 없다. 같은 망의 누구나 열 수 있다.
/// </summary>
public class ChunaWebMirror : MonoBehaviour
{
    private const int Port = 8090, VideoPort = 8092;
    private static readonly int[] Widths = { 640, 800, 1280 };
    private const float Interval = 1f / 15f;   // 최대 15장/s — ROM과 같다(10-21 배터리 · 발열)
    private const int JpegQuality = 70;
    private const float BgGray = 0.6f;          // 현실 모드에서 비워 둔 자리를 채울 회색(감마)

#if UNITY_ANDROID && !UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Prop("debug.coco.webmirror", "1") == "0") return;
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "RomLineScene") return;   // ROM 판 — 포트 8090을 RomLineSession이 쓴다
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
    private readonly MirrorVideoSender video = new MirrorVideoSender(VideoPort, "[웹미러]");
    private Camera cam;
    private CommandBuffer cb;
    private const CameraEvent CamEvent = CameraEvent.BeforeImageEffects;
    private RenderTexture eyeCopy, outRt;
    private Material mat;
    private int choice, outW, outH, pendingChoice = -1;
    private bool flip;
    private float nextShot, nextVideo, diagNext, diagFrom;
    private volatile bool busy;
    private byte[] rgba;
    private int shots; private double encodeMs;
    private float aspect = 1f;   // 눈 화면 세로 / 가로
    private bool ready;
    private readonly StringBuilder info = new StringBuilder(384);

    private System.Collections.IEnumerator Start()
    {
        string html = Resources.Load<TextAsset>("Mirror/chuna_mirror")?.text ?? "";
        server.Start(Port, html);
        video.Start();
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

        Shader sh = Resources.Load<Shader>("Mirror/ChunaMirrorNV12");
        if (sh == null || !sh.isSupported) { Debug.LogError("[웹미러] ★셰이더 Mirror/ChunaMirrorNV12 없음/미지원"); enabled = false; yield break; }
        mat = new Material(sh);
        flip = Prop("debug.coco.webmirror.flip", "0") == "1";   // ★10-20 기기(3S · GLES3): 뒤집으면 거꾸로였다 — 기본은 안 뒤집는다
        mat.SetFloat("_FlipY", flip ? 1f : 0f);
        mat.SetFloat("_BgGray", BgGray);

        cb = new CommandBuffer { name = "WebMirrorCapture" };
        cb.Blit(BuiltinRenderTextureType.CurrentActive, eyeCopy);
        SetChoice(0);
        ready = true;
        Debug.Log($"[웹미러] 시작 — {server.Url} · 영상 포트 {video.Port} · 눈 화면 {eye.width}×{eye.height} {eye.dimension} {eye.graphicsFormat} · " +
                  $"출력 {outW}×{outH} · 뒤집기 {flip} · 빈 자리 회색 {BgGray} · 그래픽 {SystemInfo.graphicsDeviceType}");
    }

    private void SetChoice(int c)
    {
        choice = Mathf.Clamp(c, 0, Widths.Length - 1);
        outW = Widths[choice];
        outH = Mathf.Max(16, Mathf.RoundToInt(outW * aspect / 16f) * 16);   // 영상 압축기에 맞춰 16의 배수(640 → 672)
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
        float now = Time.unscaledTime;
        video.Poll(now);
        if (server.Watched) PumpState(now);
        if (!ready) return;

        // ★10-21 사용자 질문(«접속 안 해도 기본 부담이 커지나»): 눈 화면 복사는 매 프레임 전체 해상도로 일어난다 →
        //   보는 화면(영상 접속 또는 사진 요청)이 있을 때만 잡기 명령을 붙인다. 아무도 안 보면 복사도 · 압축도 · 읽기도 없다.
        //   씬이 바뀌면 눈 카메라도 새로 생긴다(로비 → 실습) — 그때는 새 카메라로 옮긴다.
        Camera main = Camera.main;
        bool want = main != null && (video.Clients > 0 || server.ImageWatched("cam"));
        Camera target = want ? main : null;
        if (target != cam)
        {
            if (cam != null) cam.RemoveCommandBuffer(CamEvent, cb);
            cam = target;
            if (cam != null) cam.AddCommandBuffer(CamEvent, cb);
            Debug.Log(cam != null ? $"[웹미러] 눈 화면 잡기 시작 — '{cam.name}' · 씬 {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name} · ★1단계(복사만 {StageGap}초) — 이 뒤에 죽으면 눈 화면 복사가 원인"
                                  : "[웹미러] 눈 화면 잡기 멈춤 — 보는 화면 없음");
            stage2At = Time.unscaledTime + StageGap;
            stage2Logged = firstVideoLogged = firstShotLogged = false;
        }

        if (pendingChoice >= 0 && !busy)
        {
            Debug.Log($"[웹미러] 해상도 {outW}×{outH} → 가로 {Widths[pendingChoice]}");
            SetChoice(pendingChoice);
            pendingChoice = -1;
        }

        if (now >= diagNext) Diagnose(now);
        if (cam == null) return;

        // ★11-04 진단: 기기(3S · GLES3)에서 잡기 시작 25~30ms 뒤 렌더 스레드 충돌 4/4
        //   (심볼: GfxDeviceGLES::UpdateComputeConstantBuffers → ClearBufferSubData → memset 널). 복사와 출력 · 읽기를 시간으로 떼어 어느 쪽인지 가른다.
        if (now < stage2At) return;
        if (!stage2Logged) { stage2Logged = true; Debug.Log("[웹미러] ★2단계 — 복사는 살아남았다 · 이제 출력 셰이더 · 읽기 시작(이 뒤에 죽으면 출력 · 읽기가 원인)"); }

        // 영상 — 보는 화면이 붙어 있을 때만(간격을 쌓아 간다 — now + 간격이면 72fps에서 한 장씩 걸러진다)
        if (video.Clients > 0 && now >= nextVideo)
        {
            nextVideo = Mathf.Max(nextVideo + Interval, now - Interval);
            if (!firstVideoLogged) { firstVideoLogged = true; Debug.Log("[웹미러] 첫 영상 — NV12 셰이더(패스 0) + 비동기 읽기"); }
            video.Send(eyeCopy, mat, 0, outW, outH, Mathf.RoundToInt(1f / Interval));
        }

        // 사진 — 브라우저가 영상을 못 받아 /cam.jpg를 물어 갈 때만.
        // ★11-04: 영상이 붙어 있으면 사진은 만들지 않는다(같은 프레임에 둘이 시작된 판에서도 죽었다 · 영상이 보이면 사진은 안 쓰인다).
        if (video.Clients > 0) return;
        if (busy || now < nextShot || !server.ImageWatched("cam")) return;
        nextShot = now + PhotoInterval;
        busy = true;
        if (!firstShotLogged) { firstShotLogged = true; Debug.Log("[웹미러] 첫 사진 — 출력 셰이더(패스 1) + 동기 읽기(ReadPixels)"); }
        Graphics.Blit(eyeCopy, outRt, mat, 1);
        // ★★11-04 기기 실측(3S · GLES3): 사진 경로의 비동기 GPU 읽기(AsyncGPUReadback.Request)를 건 지 40ms 뒤 렌더 스레드 충돌 6/6
        //   (GfxDeviceGLES::UpdateComputeConstantBuffers → ClearBufferSubData → memset 널 — 프로젝트 빌드 심볼로 풀었다).
        //   ① 형식 지정을 빼 봤다(16:26) → 앱을 켜고 40초 뒤 시작한 시험은 멀쩡했지만, 켜자마자 시작한 두 판은 또 죽었다 = 원인을 못 없앴다.
        //   ② 영상 경로(RequestIntoNativeArray · R8)는 1280×1344로 몇 분 멀쩡 → 사진 경로에서만 비동기 읽기를 뺀다.
        //   → 동기 읽기(ReadPixels = glReadPixels)로 바꾼다. 한 장마다 GPU를 기다리므로 초당 장수를 8장으로 줄인다(사진은 영상을 못 받는 브라우저용 예비 경로).
        //   ★왜 비동기 읽기가 컴퓨트 경로를 타는지는 못 밝혔다 — 이것은 원인 제거가 아니라 그 경로를 안 타게 하는 회피다.
        if (shotTex == null || shotTex.width != outW || shotTex.height != outH)
        {
            if (shotTex != null) Destroy(shotTex);
            shotTex = new Texture2D(outW, outH, TextureFormat.RGBA32, false, true) { name = "WebMirrorShot" };
        }
        var prev = RenderTexture.active;
        RenderTexture.active = outRt;
        shotTex.ReadPixels(new Rect(0, 0, outW, outH), 0, 0, false);
        RenderTexture.active = prev;
        var data = shotTex.GetRawTextureData<byte>();
        if (rgba == null || rgba.Length != data.Length) rgba = new byte[data.Length];
        data.CopyTo(rgba);
        EncodeAsync(outW, outH);
    }

    private const float PhotoInterval = 1f / 8f;
    private Texture2D shotTex;

    private const float StageGap = 1.5f;
    private float stage2At;
    private bool stage2Logged, firstVideoLogged, firstShotLogged;

    /// <summary>rgba(사진 한 장)를 작업 스레드에서 JPEG로 — 다 되면 busy를 푼다.</summary>
    private void EncodeAsync(int w, int h)
    {
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

    // ── 실습 상태(/state) — 관제 화면 왼쪽 묶음 · 아래 줄(10-21 사용자 지시) ─────────────────
    //   ScenarioManager의 현재 값을 직접 읽는다(이벤트 구독은 안 쓴다 — ScenarioManager.OnDestroy가 ScenarioEventSystem.Clear()로 구독자를 지운다).
    //   판정은 술기마다 출처가 다르다 → 공통으로 읽히는 두 가지만: 실시간 유사도(손 포즈 평가 중일 때 · ChunaPathEvaluator) · 파지점(GripPointTarget) 접촉 · 파지 수.
    private ScenarioManager sm;
    private ChunaPathEvaluator evaluator;
    private GripPointTarget[] grips = Array.Empty<GripPointTarget>();
    private float stateNext, findNext;
    private object shownStep, shownScenario;
    private string monitorUser = "";   // 관제 화면 우측 상단 사용자명(10-22)
    private float nextUserRead;
    private float stepStart, scenarioStart;
    private int stepIndex = -1, stepTotal;
    private readonly StringBuilder state = new StringBuilder(1024);

    private void PumpState(float now)
    {
        if (now < stateNext) return;
        stateNext = now + 0.25f;
        if (now >= findNext)   // 씬이 바뀌면 다시 찾는다(1초마다 · 매 프레임 찾지 않는다)
        {
            findNext = now + 1f;
            if (sm == null) sm = FindFirstObjectByType<ScenarioManager>();
            if (evaluator == null) evaluator = FindFirstObjectByType<ChunaPathEvaluator>();
            grips = FindObjectsByType<GripPointTarget>(FindObjectsSortMode.None);
        }

        ScenarioData sc = sm != null ? sm.CurrentScenario : null;
        StepData st = sm != null ? sm.CurrentStep : null;
        if (!ReferenceEquals(sc, shownScenario)) { shownScenario = sc; scenarioStart = now; }
        if (!ReferenceEquals(st, shownStep))
        {
            shownStep = st; stepStart = now;
            stepIndex = -1; stepTotal = 0;   // 단계 번호는 단계가 바뀔 때만 다시 센다
            if (sc != null && sc.phases != null)
                foreach (var ph in sc.phases)
                    foreach (var s in ph.steps) { if (ReferenceEquals(s, st)) stepIndex = stepTotal; stepTotal++; }
        }

        // 10-22 사용자 지시: 관제 화면 우측 상단에 지금 사용자명 — 기기에 남은 로그인 정보(LoginStateStore · PlayerPrefs). 2초마다만 다시 읽는다.
        if (now >= nextUserRead)
        {
            nextUserRead = now + 2f;
            var login = new LoginStateStore().LoadLoginInfo();
            monitorUser = login.hasData && !string.IsNullOrEmpty(login.username) ? login.username : "";
        }

        var ci = CultureInfo.InvariantCulture;
        state.Length = 0;
        state.Append("{\"title\":"); Json(state, sc != null ? ScenarioDisplayNames.Full(sc.scenarioName) : "");   // 10-27: 화면 이름 표
        state.Append(",\"user\":"); Json(state, monitorUser);
        state.Append(",\"scene\":"); Json(state, UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        state.Append(",\"mode\":"); Json(state, sm != null ? sm.SelectedMode : "");
        state.Append(",\"phase\":"); Json(state, sm != null && sm.CurrentPhase != null ? sm.CurrentPhase.phaseName : "");
        state.Append(",\"step\":"); Json(state, st != null ? st.stepName : "");
        state.Append(",\"stepIndex\":").Append(stepIndex).Append(",\"stepTotal\":").Append(stepTotal);
        state.Append(",\"instruction\":"); Json(state, sm != null && sm.CurrentSubStep != null ? sm.CurrentSubStep.textInstruction : "");
        state.Append(",\"done\":").Append(sm != null && sm.IsScenarioCompleted ? "true" : "false");
        state.Append(",\"stepSec\":").Append(st != null ? (now - stepStart).ToString("F0", ci) : "-1");
        state.Append(",\"totalSec\":").Append(sc != null ? (now - scenarioStart).ToString("F0", ci) : "-1");
        if (evaluator != null && evaluator.IsEvaluating)
        {
            var (l, r) = evaluator.GetRealTimeSimilarityBoth();
            state.Append(",\"simL\":").Append(l.ToString("F3", ci)).Append(",\"simR\":").Append(r.ToString("F3", ci));
        }
        int gTotal = 0, gTouch = 0, gGrip = 0;
        foreach (var g in grips)
        {
            if (g == null || !g.isActiveAndEnabled) continue;
            gTotal++;
            if (g.IsTouched) gTouch++;
            if (g.IsGripped) gGrip++;
        }
        state.Append(",\"gripTotal\":").Append(gTotal).Append(",\"touched\":").Append(gTouch).Append(",\"gripped\":").Append(gGrip);
        state.Append(",\"handL\":").Append(OVRInput.IsControllerConnected(OVRInput.Controller.LHand) ? "true" : "false")
             .Append(",\"handR\":").Append(OVRInput.IsControllerConnected(OVRInput.Controller.RHand) ? "true" : "false");
        state.Append('}');
        server.SetState(state.ToString());
    }

    private static void Json(StringBuilder sb, string s)
    {
        sb.Append('"');
        if (s != null)
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') { }
                else if (c < ' ') sb.Append(' ');
                else sb.Append(c);
            }
        sb.Append('"');
    }

    private void Diagnose(float now)
    {
        float span = Mathf.Max(0.001f, now - diagFrom);
        int n = Interlocked.Exchange(ref shots, 0);
        double ms = n > 0 ? encodeMs / n : 0;
        encodeMs = 0;
        int vf = video.TakeFed();
        string vStats = video.JavaStats();
        bool viaVideo = video.Clients > 0;
        var ci = CultureInfo.InvariantCulture;
        info.Length = 0;
        info.Append("{\"camSupported\":true,\"camFps\":").Append(((viaVideo ? vf : n) / span).ToString("F1", ci))
            .Append(",\"camMs\":").Append(viaVideo ? "0" : ms.ToString("F0", ci))
            .Append(",\"camRes\":\"").Append(outW).Append('x').Append(outH).Append('"')
            .Append(",\"choice\":").Append(choice)
            .Append(",\"appFps\":").Append((1f / Mathf.Max(0.0001f, Time.smoothDeltaTime)).ToString("F0", ci))
            .Append(",\"scene\":\"").Append(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name).Append('"')
            .Append(",\"videoPort\":").Append(video.Port)
            .Append(",\"videoState\":\"").Append(video.State).Append('"');
        bool worn = OVRManager.isHmdPresent && OVRManager.instance != null && OVRManager.instance.isUserPresent;
        info.Append(",\"worn\":").Append(worn ? "true" : "false")
            .Append(",\"battery\":").Append(RomLineView.BatteryPercent())   // 10-21: 배터리(-1 = 모름) · 색 구간은 RomLineView.BatteryBand와 같다
            .Append(",\"charging\":").Append(RomLineView.BatteryCharging() ? "true" : "false");
        if (vStats.Length > 0) info.Append(',').Append(vStats);
        info.Append('}');
        server.SetMirrorInfo(info.ToString());
        if (viaVideo)
            Debug.Log($"[웹미러] 영상 진단 — 넣음 {vf / span:F1}장/s · {outW}×{outH} · {video.State} · Java {vStats} · 앱 {1f / Mathf.Max(0.0001f, Time.smoothDeltaTime):F0}fps");
        else if (server.ImageWatched("cam"))
            Debug.Log($"[웹미러] 진단 — {n / span:F1}장/s · 인코딩 {ms:F0}ms · {outW}×{outH} · 앱 {1f / Mathf.Max(0.0001f, Time.smoothDeltaTime):F0}fps");
        diagFrom = now;
        diagNext = now + 2f;
    }

    private void OnDestroy()
    {
        if (cam != null && cb != null) cam.RemoveCommandBuffer(CamEvent, cb);
        server.Stop();
        video.Dispose();
        AsyncGPUReadback.WaitAllRequests();
        if (eyeCopy != null) eyeCopy.Release();
        if (outRt != null) outRt.Release();
    }
}
