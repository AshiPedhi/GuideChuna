// ★[진단용 2026-10-01 — 원인이 갈리면 이 파일을 지운다]
// 미러링이 Vulkan 빌드에서만 «연결은 되는데 검은 화면»이다(GLES3 대조 빌드로 확정, v1.41).
// 어디서 검어지는지 가른다:
//   ① 미러링 카메라가 검게 그린다   → 눈앞 판이 검고, 로그 «카메라 화면 평균»이 0에 가깝다
//   ② 카메라는 정상, WebRTC가 못 읽는다 → 판은 정상이고 평균도 0이 아닌데 PC만 검다
// ★씬을 안 건드린다 — 스스로 떠서(RuntimeInitializeOnLoad) RenderManager.instance.vss를 읽기만 한다.
// ★릴리스 빌드는 ChunaLogger.Log가 잘리므로 Debug.Log로 찍는다. logcat에서 «[미러링진단]» 줄만 보면 된다.

using System.Collections;
using Unity.RenderStreaming;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public class MirroringVulkanProbe : MonoBehaviour
{
    // ★꺼 둔다(10-01) — 실습·평가용 빌드는 GLES3로 나가고 눈앞 판이 떠선 안 된다.
    //   Vulkan 미러링을 다시 팔 때만 true로 바꿔 진단 빌드를 만든다.
    private static readonly bool ProbeEnabled = false;

    private const float Interval = 3f;
    private const string Tag = "[미러링진단]";

    private float nextTime;
    private Transform quad;
    private Material quadMaterial;
    private RenderTexture shownTexture;
    private bool statsRunning;
    private bool quadFailed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (!ProbeEnabled) return;

        var go = new GameObject("MirroringVulkanProbe");
        DontDestroyOnLoad(go);
        go.AddComponent<MirroringVulkanProbe>();

        // ★libwebrtc 네이티브 경고·오류를 logcat으로 끌어낸다 — 인코더가 프레임을 왜 못 내는지 보려고.
        WebRTC.ConfigureNativeLogging(true, NativeLoggingSeverity.Warning);

        GraphicsFormat want = WebRTC.GetSupportedGraphicsFormat(SystemInfo.graphicsDeviceType);
        Debug.Log($"{Tag} 붙었다 — 그래픽 API {SystemInfo.graphicsDeviceType} · WebRTC가 바라는 포맷 {want}" +
                  $" · 그 포맷 렌더 지원 {SystemInfo.IsFormatSupported(want, GraphicsFormatUsage.Render)}" +
                  $" · 대체 포맷 {SystemInfo.GetCompatibleFormat(want, GraphicsFormatUsage.Render)}");
    }

    private void Update()
    {
        if (Time.unscaledTime < nextTime) return;
        nextTime = Time.unscaledTime + Interval;

        RenderManager rm = RenderManager.instance;
        VideoStreamSender vss = rm != null ? rm.vss : null;
        if (vss == null)
        {
            Debug.Log($"{Tag} 보낼 것이 없다 — RenderManager {(rm != null ? "있음" : "★없음")} · VideoStreamSender ★없음");
            return;
        }

        Camera cam = vss.sourceCamera;
        RenderTexture rt = cam != null ? cam.targetTexture : null;
        var track = vss.Track as VideoStreamTrack;

        Debug.Log($"{Tag} 송신기 켜짐 {vss.isActiveAndEnabled} · 소스 {vss.source}" +
                  $" · 카메라 {(cam != null ? (cam.isActiveAndEnabled ? "켜짐" : "★꺼짐") : "★없음")}" +
                  $" · 텍스처 {(rt != null ? $"{rt.width}x{rt.height} {rt.graphicsFormat} 깊이 {rt.depth} AA {rt.antiAliasing} 생성됨 {rt.IsCreated()}" : "★없음")}" +
                  $" · 트랙 {(track != null ? $"있음(켜짐 {track.Enabled} · {track.ReadyState})" : "★없음")}");

        // ★화면 밝기 읽기(AsyncGPUReadback)는 뺐다 — 판이 정상으로 보이는 것을 사용자가 확인했고(10-01),
        //   Vulkan에서 이 포맷은 ReadPixels 미지원이라 3초마다 오류만 찍었다.
        shownTexture = rt;

        if (track != null && !statsRunning)
            StartCoroutine(ReportStats(vss));
    }

    // 인코더가 프레임을 실제로 내보내는지.
    private IEnumerator ReportStats(VideoStreamSender vss)
    {
        statsRunning = true;
        foreach (var pair in vss.Transceivers)
        {
            RTCRtpSender sender = pair.Value != null ? pair.Value.Sender : null;
            if (sender == null) continue;

            var op = sender.GetStats();
            yield return op;
            if (op.IsError || op.Value == null) continue;

            foreach (var stat in op.Value.Stats.Values)
            {
                if (stat is RTCOutboundRTPStreamStats o)
                    Debug.Log($"{Tag} 송출 — 인코딩한 프레임 {o.framesEncoded} · 키프레임 {o.keyFramesEncoded}" +
                              $" · 크기 {o.frameWidth}x{o.frameHeight} · 보낸 바이트 {o.bytesSent} · 인코더 {o.encoderImplementation}");
            }
        }
        statsRunning = false;
    }

    // 미러링 카메라의 텍스처를 눈앞 오른쪽 아래 작은 판에 그대로 띄운다.
    private void LateUpdate()
    {
        Camera head = Camera.main;
        if (quadFailed) return;
        if (head == null || shownTexture == null)
        {
            if (quad != null && quad.gameObject.activeSelf) quad.gameObject.SetActive(false);
            return;
        }

        if (quad == null) CreateQuad();
        if (quad == null) return;

        if (!quad.gameObject.activeSelf) quad.gameObject.SetActive(true);
        if (quadMaterial.mainTexture != shownTexture) quadMaterial.mainTexture = shownTexture;

        Transform t = head.transform;
        quad.SetPositionAndRotation(
            t.position + t.forward * 0.6f + t.right * 0.22f - t.up * 0.16f,
            t.rotation);
    }

    private void CreateQuad()
    {
        Shader shader = Shader.Find("Unlit/Texture");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            Debug.Log($"{Tag} 판을 못 만든다 — 셰이더 ★없음(로그의 «카메라 화면 평균»으로 판단할 것)");
            quadFailed = true;
            return;
        }

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "MirroringVulkanProbeQuad";
        Destroy(go.GetComponent<Collider>());
        DontDestroyOnLoad(go);

        quad = go.transform;
        quad.localScale = new Vector3(0.24f, 0.135f, 1f);   // 16:9, 60cm 앞 24cm 폭
        quadMaterial = new Material(shader);
        go.GetComponent<MeshRenderer>().sharedMaterial = quadMaterial;
        Debug.Log($"{Tag} 눈앞 판을 만들었다 — 셰이더 {shader.name}");
    }
}
