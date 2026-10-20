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
///   adb shell setprop debug.coco.hwmirror.mode 0   눈 화면 잡는 방식(0 CurrentActive Blit · 1 CameraTarget Blit · 2 CopyTexture)
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

    private int outW, outH, fps, mode;
    private bool flip;
    private Camera cam;
    private CommandBuffer cb;
    private RenderTexture eyeCopy, nv12Rt;
    private Material mat;

    // 읽어 오기 두 칸(번갈아 쓴다). 각 칸 = NativeArray + 그걸 감싼 Java direct ByteBuffer(전역 참조).
    private NativeArray<byte>[] bufs = new NativeArray<byte>[2];
    private IntPtr[] jbufs = new IntPtr[2];
    private bool[] busy = new bool[2];

    private AndroidJavaObject enc;
    private IntPtr feedMethod;
    private jvalue[] feedArgs = new jvalue[2];
    private string encoderName = "-";

    private float nextSend, nextDiag;
    private int reqs, done, fed, rejected, errors, frames;
    private float dtSum;
    private float lastMean = -1f;
    private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

    private void Start()
    {
        int w = PropInt("debug.coco.hwmirror.w", 1280);
        fps = Mathf.Clamp(PropInt("debug.coco.hwmirror.fps", 60), 1, 120);
        mode = PropInt("debug.coco.hwmirror.mode", 0);
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

        cb = new CommandBuffer { name = "HwMirrorCapture" };
        switch (mode)
        {
            case 1: cb.Blit(BuiltinRenderTextureType.CameraTarget, eyeCopy); break;
            case 2: cb.CopyTexture(BuiltinRenderTextureType.CameraTarget, eyeCopy); break;
            default: cb.Blit(BuiltinRenderTextureType.CurrentActive, eyeCopy); break;
        }
        cb.Blit(eyeCopy, nv12Rt, mat);
        cam.AddCommandBuffer(CameraEvent.AfterEverything, cb);

        int len = outW * outH * 3 / 2;
        for (int i = 0; i < 2; i++)
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
                  $"{eye.dimension} {eye.graphicsFormat} · 출력 {outW}×{outH} · {fps}fps · 잡기 방식 {mode} · 뒤집기 {flip} · " +
                  $"압축기 {encoderName} · 그래픽 {SystemInfo.graphicsDeviceType} · 포트 {Port}");
    }

    private void Update()
    {
        frames++; dtSum += Time.unscaledDeltaTime;
        float now = Time.unscaledTime;

        if (now >= nextSend)
        {
            int slot = !busy[0] ? 0 : !busy[1] ? 1 : -1;
            if (slot >= 0)
            {
                nextSend = now + 1f / fps;
                busy[slot] = true; reqs++;
                int s = slot;
                AsyncGPUReadback.RequestIntoNativeArray(ref bufs[s], nv12Rt, 0, r => OnRead(r, s));
            }
        }

        if (now >= nextDiag)
        {
            float span = Mathf.Max(0.001f, dtSum);
            string js = "-";
#if UNITY_ANDROID && !UNITY_EDITOR
            if (enc != null) js = enc.Call<string>("stats");
#endif
            Debug.Log($"[HwMirror] 앱 {frames / span:F1}fps · 읽기 요청 {reqs} 완료 {done} 넘김 {fed} 거절 {rejected} 오류 {errors} " +
                      $"(2초) · 화면 평균 밝기 {lastMean:F1}(0~255 · 16 근처면 검은 화면) · Java: {js}");
            frames = 0; dtSum = 0; reqs = done = fed = rejected = errors = 0;
            nextDiag = now + 2f;
        }
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
        if (cam != null && cb != null) cam.RemoveCommandBuffer(CameraEvent.AfterEverything, cb);
#if UNITY_ANDROID && !UNITY_EDITOR
        if (enc != null) { enc.Call("stop"); enc.Dispose(); }
        for (int i = 0; i < 2; i++) if (jbufs[i] != IntPtr.Zero) AndroidJNI.DeleteGlobalRef(jbufs[i]);
#endif
        AsyncGPUReadback.WaitAllRequests();
        for (int i = 0; i < 2; i++) if (bufs[i].IsCreated) bufs[i].Dispose();
        if (eyeCopy != null) eyeCopy.Release();
        if (nv12Rt != null) nv12Rt.Release();
    }
}
