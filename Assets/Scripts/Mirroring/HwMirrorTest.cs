using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

/// <summary>
/// ★미러링 시험 ③(2026-10-20 · 사용자 지시) — 앱이 <b>이미 그린 눈 화면</b>을 다시 그리지 않고 가져와,
/// 헤드셋 하드웨어 H.264(<c>Plugins/Android/HwMirrorEncoder.java</c>)로 압축해 TCP로 내보낸다.
/// 잴 것: ①눈 화면이 검게 안 나오고 잡히나(webrtc가 Vulkan에서 검은 화면이던 구간) ②압축 fps ③앱 72fps 유지 ④왜곡 없음.
///
/// 시험 전용이다 — 씬에 올리지 않는다. 기기 시스템 속성으로만 켠다(기본 꺼짐 · 다른 기능과 엮지 않음):
///   adb shell setprop debug.coco.hwmirror 1        켜기(앱을 다시 시작해야 먹는다)
///   adb shell setprop debug.coco.hwmirror.w 1280   영상 가로(세로는 눈 화면 비율 · 16의 배수로 맞춘다)
///   adb shell setprop debug.coco.hwmirror.fps 60   보내는 상한
///   adb shell setprop debug.coco.hwmirror.mode 0   눈 화면 잡는 방식(0 CurrentActive Blit · 1 CameraTarget Blit · 2 CopyTexture · 3 슬라이스 지정 Blit)
///   adb shell setprop debug.coco.hwmirror.evt 3    잡는 시점(0 AfterForwardOpaque · 1 AfterSkybox · 2 AfterForwardAlpha · 3 BeforeImageEffects · 4 AfterEverything)
///   adb shell setprop debug.coco.hwmirror.pattern 0 1이면 눈 화면 대신 시험 패턴
///   adb shell setprop debug.coco.hwmirror.flip 0   위아래 뒤집기
/// PC: adb forward tcp:8091 tcp:8091 → ffmpeg -f h264 -i tcp://127.0.0.1:8091 …(또는 헤드셋 IP로 바로)
/// 로그: adb logcat -s Unity HwMirror | 진단은 «[HwMirror]» 2초마다.
/// </summary>
public class HwMirrorTest : MonoBehaviour
{
    private const int Port = 8091;

#if UNITY_ANDROID && !UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (Prop("debug.coco.hwmirror", "0") != "1") return;
        var go = new GameObject("[HwMirrorTest]");
        DontDestroyOnLoad(go);
        go.AddComponent<HwMirrorTest>();
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
        catch (Exception e) { Debug.LogWarning($"[HwMirror] 속성 {key} 못 읽음: {e.Message}"); }
#endif
        return def;
    }

    private static int PropInt(string key, int def) => int.TryParse(Prop(key, def.ToString()), out int v) ? v : def;

    private int outW, outH, fps, mode, evt;
    private CameraEvent camEvent = CameraEvent.AfterEverything;
    private bool flip;
    private Camera cam;
    private CommandBuffer cb;
    private RenderTexture eyeCopy, nv12Rt;
    private Material mat;

    // 읽어 오기 두 칸(번갈아 쓴다). 각 칸 = NativeArray + 그걸 감싼 Java direct ByteBuffer(전역 참조).
    // ★두 칸이면 읽기 지연(2~3프레임)에 막혀 초당 48장에 그쳤다(10-20 기기 2차) → 네 칸.
    private const int Slots = 4;
    private NativeArray<byte>[] bufs = new NativeArray<byte>[Slots];
    private IntPtr[] jbufs = new IntPtr[Slots];
    private bool[] busy = new bool[Slots];
    private RenderTexture thumbRt;
    private string thumbInfo = "-";

    private AndroidJavaObject enc;
    private IntPtr feedMethod;
    private jvalue[] feedArgs = new jvalue[2];
    private string encoderName = "-";

    private float nextSend, nextDiag;
    private int reqs, done, fed, rejected, errors, frames;
    private float dtSum;
    private float lastMean = -1f;
    private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

    private bool ready;

    // ★시작 직후(AfterSceneLoad)엔 XR이 아직 눈 화면을 안 만들어 eyeTextureDesc가 256×256 Tex2D로 나온다
    //   (10-20 기기 1차: 그 크기로 사본을 만들어 영상이 회색 한 장이었다) → 실제 눈 화면 크기가 나올 때까지 기다린다.
    private System.Collections.IEnumerator Start()
    {
        float until = Time.realtimeSinceStartup + 15f;
        while (Time.realtimeSinceStartup < until &&
               (Camera.main == null || !XRSettings.isDeviceActive || XRSettings.eyeTextureDesc.width <= 256))
            yield return null;
        yield return null;
        Init();
        ready = enabled;
    }

    private void Init()
    {
        int w = PropInt("debug.coco.hwmirror.w", 1280);
        fps = Mathf.Clamp(PropInt("debug.coco.hwmirror.fps", 60), 1, 120);
        mode = PropInt("debug.coco.hwmirror.mode", 0);
        bool pattern = PropInt("debug.coco.hwmirror.pattern", 0) == 1;
        flip = PropInt("debug.coco.hwmirror.flip", 0) == 1;
        int bitrate = PropInt("debug.coco.hwmirror.bitrate", 16000000);

        cam = Camera.main;
        RenderTextureDescriptor eye = XRSettings.eyeTextureDesc;
        if (cam == null || eye.width <= 0)
        {
            Debug.LogError($"[HwMirror] 시작 못 함 — 카메라 {(cam != null ? cam.name : "없음")} · 눈 화면 {eye.width}×{eye.height}");
            enabled = false; return;
        }
        outW = Mathf.Max(16, w / 16 * 16);
        outH = Mathf.Max(16, Mathf.RoundToInt(outW * (float)eye.height / eye.width) / 16 * 16);

        var copyDesc = eye;
        copyDesc.msaaSamples = 1;
        copyDesc.depthBufferBits = 0;
        copyDesc.dimension = TextureDimension.Tex2DArray;
        copyDesc.volumeDepth = Mathf.Max(1, eye.volumeDepth);
        eyeCopy = new RenderTexture(copyDesc) { name = "HwMirrorEyeCopy" };
        eyeCopy.Create();

        nv12Rt = new RenderTexture(outW, outH * 3 / 2, 0, RenderTextureFormat.R8, RenderTextureReadWrite.Linear) { name = "HwMirrorNV12" };
        nv12Rt.Create();

        Shader sh = Resources.Load<Shader>("Mirror/HwMirrorNV12");
        if (sh == null || !sh.isSupported) { Debug.LogError("[HwMirror] 셰이더 없음/미지원"); enabled = false; return; }
        mat = new Material(sh);
        mat.SetFloat("_Slice", 0f);
        mat.SetFloat("_FlipY", flip ? 1f : 0f);
        mat.SetVector("_OutSize", new Vector4(outW, outH, 1f / outW, 1f / outH));
        mat.SetFloat("_Pattern", pattern ? 1f : 0f);
        thumbRt = new RenderTexture(64, 64, 0, RenderTextureFormat.ARGB32) { name = "HwMirrorThumb" };
        thumbRt.Create();

        // ★AfterEverything에서 CurrentActive · CameraTarget · CopyTexture 셋 다 사본이 비었다(10-20 기기 3차: 최대 0 · 알파 0)
        //   → 잡는 시점(evt)과 슬라이스 지정 Blit(mode 3)을 기기 속성으로 바꿔 가며 잰다.
        evt = PropInt("debug.coco.hwmirror.evt", 3);   // ★10-20 기기: 2(AfterForwardAlpha) · 3(BeforeImageEffects)만 잡혔다 — 0 · 1 · 4는 빈 사본
        CameraEvent[] evts = { CameraEvent.AfterForwardOpaque, CameraEvent.AfterSkybox, CameraEvent.AfterForwardAlpha,
                               CameraEvent.BeforeImageEffects, CameraEvent.AfterEverything };
        camEvent = evts[Mathf.Clamp(evt, 0, evts.Length - 1)];
        cb = new CommandBuffer { name = "HwMirrorCapture" };
        switch (mode)
        {
            case 1: cb.Blit(BuiltinRenderTextureType.CameraTarget, eyeCopy); break;
            case 2: cb.CopyTexture(BuiltinRenderTextureType.CameraTarget, eyeCopy); break;
            case 3: cb.Blit(BuiltinRenderTextureType.CurrentActive, eyeCopy, 0, 0); break;
            default: cb.Blit(BuiltinRenderTextureType.CurrentActive, eyeCopy); break;
        }
        // ★NV12 변환은 카메라 명령 안에서 하지 않는다 — XR 양안 상태가 걸린 채 그리면 출력 RT의 일부만 칠해졌다(10-20 기기 2차:
        //   위쪽 일부만 Y=16 · 나머지 0). Update에서 Graphics.Blit로 따로 한다.
        cam.AddCommandBuffer(camEvent, cb);

        int len = outW * outH * 3 / 2;
        for (int i = 0; i < Slots; i++)
        {
            bufs[i] = new NativeArray<byte>(len, Allocator.Persistent, NativeArrayOptions.ClearMemory);
#if UNITY_ANDROID && !UNITY_EDITOR
            IntPtr local = AndroidJNI.NewDirectByteBuffer(bufs[i]);
            jbufs[i] = AndroidJNI.NewGlobalRef(local);
            AndroidJNI.DeleteLocalRef(local);
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        enc = new AndroidJavaObject("com.coco.chuna.mirror.HwMirrorEncoder");
        encoderName = enc.Call<string>("start", outW, outH, fps, bitrate, Port);
        feedMethod = AndroidJNI.GetMethodID(enc.GetRawClass(), "feed", "(Ljava/nio/ByteBuffer;J)Z");
#endif
        Debug.Log($"[HwMirror] 시작 — 카메라 '{cam.name}' · 눈 화면 {eye.width}×{eye.height} 깊이 {eye.volumeDepth} msaa {eye.msaaSamples} " +
                  $"{eye.dimension} {eye.graphicsFormat} · 출력 {outW}×{outH} · {fps}fps · 잡기 방식 {mode} · 시점 {camEvent} · 뒤집기 {flip} · " +
                  $"패턴 {pattern} · 압축기 {encoderName} · 그래픽 {SystemInfo.graphicsDeviceType} · 포트 {Port}");
    }

    private void Update()
    {
        if (!ready) return;
        frames++; dtSum += Time.unscaledDeltaTime;
        float now = Time.unscaledTime;

        // ★씬이 바뀌면 눈 카메라도 새로 생긴다(로비 → 실습 · 10-20 추나 시험) — 잡기 명령을 새 카메라로 옮긴다.
        //   종전엔 첫 씬 카메라에만 붙어 실습 씬에서 사본이 멈췄다(같은 장면을 계속 보냈다).
        Camera main = Camera.main;
        if (main != null && main != cam)
        {
            if (cam != null) cam.RemoveCommandBuffer(camEvent, cb);
            cam = main;
            cam.AddCommandBuffer(camEvent, cb);
            Debug.Log($"[HwMirror] 카메라 옮김 — '{cam.name}' · 씬 {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
        }

        if (now >= nextSend)
        {
            int slot = -1;
            for (int i = 0; i < Slots; i++) if (!busy[i]) { slot = i; break; }
            if (slot >= 0)
            {
                Graphics.Blit(eyeCopy, nv12Rt, mat, 0);
                // ★now + 간격으로 두면 72fps 앱에서 60 상한이 한 장 걸러 36이 된다(10-20 기기 1차) → 간격을 쌓아 간다.
                nextSend = Mathf.Max(nextSend + 1f / fps, now - 1f / fps);
                busy[slot] = true; reqs++;
                int s = slot;
                AsyncGPUReadback.RequestIntoNativeArray(ref bufs[s], nv12Rt, 0, r => OnRead(r, s));
            }
        }

        if (now >= nextDiag)
        {
            float span = Mathf.Max(0.001f, dtSum);
            Graphics.Blit(eyeCopy, thumbRt, mat, 1);
            AsyncGPUReadback.Request(thumbRt, 0, OnThumb);
            string js = "-";
#if UNITY_ANDROID && !UNITY_EDITOR
            if (enc != null) js = enc.Call<string>("stats");
#endif
            Debug.Log($"[HwMirror] 앱 {frames / span:F1}fps · 읽기 요청 {reqs} 완료 {done} 넘김 {fed} 거절 {rejected} 오류 {errors} " +
                      $"(2초) · 화면 평균 밝기 {lastMean:F1}(0~255 · 16 근처면 검은 화면) · 눈 사본 {thumbInfo} · Java: {js}");
            frames = 0; dtSum = 0; reqs = done = fed = rejected = errors = 0;
            nextDiag = now + 2f;
        }
    }

    /// <summary>눈 화면 사본 64×64 — 평균 · 최대 RGB와 알파. 최대가 0이면 사본이 빈 것이다(잡기 실패).</summary>
    private void OnThumb(AsyncGPUReadbackRequest r)
    {
        if (r.hasError) { thumbInfo = "읽기 오류"; return; }
        var d = r.GetData<Color32>();
        long sr = 0, sg = 0, sb = 0, sa = 0; int mx = 0;
        for (int i = 0; i < d.Length; i++)
        {
            Color32 c = d[i]; sr += c.r; sg += c.g; sb += c.b; sa += c.a;
            int m = Mathf.Max(c.r, Mathf.Max(c.g, c.b)); if (m > mx) mx = m;
        }
        int n = Mathf.Max(1, d.Length);
        thumbInfo = $"평균 ({sr / n},{sg / n},{sb / n}) 알파 {sa / n} 최대 {mx}";
    }

    private void OnRead(AsyncGPUReadbackRequest r, int slot)
    {
        try
        {
            if (r.hasError || !bufs[slot].IsCreated) { errors++; return; }
            done++;
            // 검은 화면 판별용 — Y 평면 가운데 줄 몇 점만 본다(매 장 하지 않는다).
            if (done % 30 == 1)
            {
                var b = bufs[slot];
                int row = outH / 2 * outW; long sum = 0; int n = 0;
                for (int x = 0; x < outW; x += 16) { sum += b[row + x]; n++; }
                lastMean = n > 0 ? (float)sum / n : -1f;
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            if (enc != null)
            {
                feedArgs[0].l = jbufs[slot];
                feedArgs[1].j = clock.ElapsedTicks * 1000000L / System.Diagnostics.Stopwatch.Frequency;
                if (AndroidJNI.CallBooleanMethod(enc.GetRawObject(), feedMethod, feedArgs)) fed++; else rejected++;
            }
#endif
        }
        finally { busy[slot] = false; }
    }

    private void OnDestroy()
    {
        if (cam != null && cb != null) cam.RemoveCommandBuffer(camEvent, cb);
#if UNITY_ANDROID && !UNITY_EDITOR
        if (enc != null) { enc.Call("stop"); enc.Dispose(); }
        for (int i = 0; i < Slots; i++) if (jbufs[i] != IntPtr.Zero) AndroidJNI.DeleteGlobalRef(jbufs[i]);
#endif
        AsyncGPUReadback.WaitAllRequests();
        for (int i = 0; i < Slots; i++) if (bufs[i].IsCreated) bufs[i].Dispose();
        if (thumbRt != null) thumbRt.Release();
        if (eyeCopy != null) eyeCopy.Release();
        if (nv12Rt != null) nv12Rt.Release();
    }
}
