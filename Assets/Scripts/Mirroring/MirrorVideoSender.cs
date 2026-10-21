using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// ★웹 미러링 — 영상(H.264) 보내기 묶음(2026-10-21 · 추나 실습 미러링용).
/// GPU에서 그림을 NV12 한 장으로 바꾸고(호출하는 쪽의 셰이더 패스) → 비동기로 읽어 → Java <c>RomVideoEncoder</c>
/// (헤드셋 하드웨어 AVC · HTTP GET /video · 한 장마다 길이 4바이트)로 넘긴다. CPU는 읽어 온 NV12를 압축기 입력 칸에 옮기기만 한다.
/// 선 실측(<see cref="RomLineMirror"/>)이 10-21에 기기에서 확인한 경로를 그대로 묶은 것이다(ROM 쪽은 확인된 상태라 그대로 두었다).
/// 보는 화면(브라우저)이 붙어 있을 때만 보낸다 — <see cref="Clients"/>가 0이면 호출하는 쪽이 <see cref="Send"/>를 부르지 않는다.
/// Android 기기 빌드에서만 돈다(에디터 · PC에서는 <see cref="Ready"/>가 false — 사진 경로만 쓴다).
/// </summary>
public class MirrorVideoSender : IDisposable
{
    private const int Slots = 3;
    private readonly int port;
    private readonly string tag;
    private AndroidJavaObject enc;
    private IntPtr feedMethod;
    private readonly jvalue[] args = new jvalue[2];
    private readonly NativeArray<byte>[] bufs = new NativeArray<byte>[Slots];
    private readonly IntPtr[] jbufs = new IntPtr[Slots];
    private readonly bool[] busy = new bool[Slots];
    private RenderTexture nv12Rt;
    private Vector2Int size;
    private float pollNext;
    private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();

    public bool Ready => enc != null;
    public int Port => enc != null ? port : 0;
    /// <summary>붙어 있는 화면 수(0.5초마다 갱신 — <see cref="Poll"/>).</summary>
    public int Clients { get; private set; }
    /// <summary>압축기에 넘긴 장수(읽을 때마다 0으로 — <see cref="TakeFed"/>).</summary>
    private int fed;
    public string State { get; private set; } = "꺼짐";

    public MirrorVideoSender(int port, string logTag) { this.port = port; tag = logTag; }

    public void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!SystemInfo.supportsAsyncGPUReadback) { State = "지원 안 함(GPU 비동기 읽기 없음)"; Debug.Log($"{tag} 영상 — {State}"); return; }
        try
        {
            enc = new AndroidJavaObject("com.coco.chuna.mirror.RomVideoEncoder");
            if (!enc.Call<bool>("listen", port))
            {
                enc.Dispose(); enc = null;
                State = $"포트 {port}를 못 열었다";
                Debug.LogWarning($"{tag} ★영상 — {State} — 사진으로만 보낸다");
                return;
            }
            feedMethod = AndroidJNI.GetMethodID(enc.GetRawClass(), "feed", "(Ljava/nio/ByteBuffer;J)Z");
            State = "대기(보는 화면 없음)";
            Debug.Log($"{tag} 영상 — 열었다 · 포트 {port} · 보는 화면이 붙으면 압축을 시작한다");
        }
        catch (Exception e)
        {
            enc = null; State = "시작 실패";
            Debug.LogWarning($"{tag} ★영상 — 시작 실패({e.Message}) — 사진으로만 보낸다");
        }
#endif
    }

    /// <summary>붙은 화면 수를 0.5초마다 묻는다(JNI라 매 프레임 하지 않는다).</summary>
    public void Poll(float now)
    {
        if (enc == null || now < pollNext) return;
        pollNext = now + 0.5f;
        int c = enc.Call<int>("clientCount");
        if (c == Clients) return;
        Debug.Log($"{tag} 영상 — 보는 화면 {Clients} → {c}");
        Clients = c;
        State = c > 0 ? "보내는 중 · 화면 " + c : "대기(보는 화면 없음)";
    }

    /// <summary>
    /// 한 장 보낸다 — src를 mat의 pass로 NV12(w × h)에 그려 읽어 넘긴다. 읽는 칸이 다 차 있으면 건너뛴다(false).
    /// 크기가 바뀌면 압축기를 다시 만든다(붙어 있던 화면은 끊기고 새 크기로 다시 붙는다). mat에는 _OutSize를 여기서 넣는다.
    /// </summary>
    public bool Send(Texture src, Material mat, int pass, int w, int h, int fps)
    {
        if (enc == null) return false;
        if (!Ensure(new Vector2Int(w, h), mat, fps)) return false;
        int slot = -1;
        for (int i = 0; i < Slots; i++) if (!busy[i]) { slot = i; break; }
        if (slot < 0) return false;
        Graphics.Blit(src, nv12Rt, mat, pass);
        busy[slot] = true;
        int s = slot;
        AsyncGPUReadback.RequestIntoNativeArray(ref bufs[s], nv12Rt, 0, req => OnRead(req, s));
        return true;
    }

    private bool Ensure(Vector2Int r, Material mat, int fps)
    {
        if (size == r && nv12Rt != null) return true;
        for (int i = 0; i < Slots; i++) if (busy[i]) return false;   // 읽는 중인 칸이 있으면 다음 장으로 미룬다
        FreeBuffers();
        int bitrate = Mathf.Max(800000, Mathf.RoundToInt(r.x * r.y * fps * 0.3f));
        string name = enc.Call<string>("configure", r.x, r.y, fps, bitrate);
        if (name.StartsWith("실패")) { State = "압축기 " + name; Debug.LogWarning($"{tag} ★영상 — {State}"); return false; }
        nv12Rt = new RenderTexture(r.x, r.y * 3 / 2, 0, RenderTextureFormat.R8, RenderTextureReadWrite.Linear) { name = "미러링 영상 NV12" };
        nv12Rt.Create();
        mat.SetVector("_OutSize", new Vector4(r.x, r.y, 1f / r.x, 1f / r.y));
        int len = r.x * r.y * 3 / 2;
        for (int i = 0; i < Slots; i++)
        {
            bufs[i] = new NativeArray<byte>(len, Allocator.Persistent, NativeArrayOptions.ClearMemory);
#if UNITY_ANDROID && !UNITY_EDITOR
            IntPtr local = AndroidJNI.NewDirectByteBuffer(bufs[i]);
            jbufs[i] = AndroidJNI.NewGlobalRef(local);
            AndroidJNI.DeleteLocalRef(local);
#endif
        }
        size = r;
        Debug.Log($"{tag} 영상 — 압축기 {r.x}×{r.y} · {fps}장/s · {bitrate / 1000}kbps · {name}");
        return true;
    }

    private void OnRead(AsyncGPUReadbackRequest req, int slot)
    {
        try
        {
            if (req.hasError || !bufs[slot].IsCreated || enc == null) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            args[0].l = jbufs[slot];
            args[1].j = clock.ElapsedTicks * 1000000L / System.Diagnostics.Stopwatch.Frequency;
            if (AndroidJNI.CallBooleanMethod(enc.GetRawObject(), feedMethod, args)) fed++;
#endif
        }
        finally { busy[slot] = false; }
    }

    /// <summary>지난번 뒤로 압축기에 넘긴 장수를 꺼낸다.</summary>
    public int TakeFed() { int n = fed; fed = 0; return n; }

    /// <summary>Java 쪽 진단(JSON 조각 · 부를 때마다 0으로). 꺼져 있으면 빈 글자.</summary>
    public string JavaStats() => enc != null ? enc.Call<string>("stats") : "";

    private void FreeBuffers()
    {
        for (int i = 0; i < Slots; i++)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (jbufs[i] != IntPtr.Zero) { AndroidJNI.DeleteGlobalRef(jbufs[i]); jbufs[i] = IntPtr.Zero; }
#endif
            if (bufs[i].IsCreated) bufs[i].Dispose();
        }
        if (nv12Rt != null) { nv12Rt.Release(); UnityEngine.Object.Destroy(nv12Rt); nv12Rt = null; }
        size = default;
    }

    public void Dispose()
    {
        if (enc != null) { try { enc.Call("stop"); } catch (Exception) { } enc.Dispose(); enc = null; }
        AsyncGPUReadback.WaitAllRequests();
        FreeBuffers();
    }
}
