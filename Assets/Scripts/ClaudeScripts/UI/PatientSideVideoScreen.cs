using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// 안내판 안의 작은 전문가 영상(2026-11-03 4차 — 사용자 «옆에 새 창 켜지는 거 너무 크고 보기 불편» · «아이콘으로 단순화»).
///
/// <code>
///   ┌──── 영상 16×9cm ────┐
///   └─────────────────────┘
///   ━━━━━━●──────────           ← 누르면 그 자리로
///      [⟲5]  [▶/❚❚]  [✕]
/// </code>
///
/// ★영상 = <c>Resources/Videos/{scenarioName}</c> · 구간 = CSV <see cref="SubStepData.videoStartTime"/> ~ <see cref="SubStepData.videoEndTime"/>
///   (정면 창 <see cref="GuideVideoController"/>와 같은 파일 · 같은 칸). 구간 끝 → 1초 쉬고 처음으로 — 반복은 늘 켠다.
/// ★소리는 끈다(단계 나레이션과 겹친다). 속도 · 소리 조작은 정면 전문가 영상 탭에만 둔다(<see cref="ExpertVideoControlBar"/>).
/// ★VideoPlayer는 따로 둔다 — 정면 창 플레이어를 같이 쓰면 정면 페이지를 열고 닫을 때 서로 멈춘다.
/// ★cm 공간(부모 스케일 0.01) · 사용자 쪽 −Z. 자리는 부모(안내판)가 정한다. 높이 = <see cref="Height"/>.
/// </summary>
public class PatientSideVideoScreen : MonoBehaviour
{
    public const float ScreenW = 16f, ScreenH = 9f;   // cm ★추정값 — 헤드셋에서 조정
    const float Btn = 4f, BtnGap = 2f, BarH = 0.5f, BarHitH = 2.5f, Gap = 1f;
    const float LoopDelay = 1f;
    public static float Height => ScreenH + Gap + BarHitH + Gap + Btn;
    static readonly Color BtnIdle = new Color(0.22f, 0.25f, 0.32f, 0.95f);
    static readonly Color BtnHover = new Color(0.35f, 0.70f, 1.00f, 1.00f);
    static readonly Color BarBack = new Color(1f, 1f, 1f, 0.25f);
    static readonly Color BarFill = new Color(1f, 0.25f, 0.25f, 1f);

    private Transform cm;   // 부모의 cm 공간
    private VideoPlayer vp;
    private RenderTexture rt;
    private Transform barFill;
    private float barY;
    private SidePokeButton bBack, bPlay, bClose, bBar;
    private Texture2D icPlay, icPause;
    private RomRecordHands hands;
    private Transform[] parts;

    private float segStart, segEnd;
    private double pendingSeek = -1;
    private bool wantPlay, open;
    private float resumeAt = -1f;
    private int lastFill = -1;

    public bool IsOpen => open;

    /// <summary><paramref name="cmSpace"/> = 스케일 0.01인 부모. 자리는 <see cref="Layout"/>으로.</summary>
    public static PatientSideVideoScreen Create(Transform cmSpace, RomRecordHands hands)
    {
        var go = new GameObject("안내판 영상") { hideFlags = HideFlags.DontSave };
        go.transform.SetParent(cmSpace, false);
        var s = go.AddComponent<PatientSideVideoScreen>();
        s.Build(cmSpace, hands);
        s.SetPartsVisible(false);
        return s;
    }

    private void Build(Transform cmSpace, RomRecordHands h)
    {
        hands = h; cm = cmSpace;
        Shader sh = Shader.Find("Sprites/Default"); if (sh == null) sh = Shader.Find("Standard");
        rt = new RenderTexture(640, 360, 0) { name = "안내판 영상 RT" };
        var scr = Quad("영상", sh, Color.white, 2992);
        scr.localScale = new Vector3(ScreenW, ScreenH, 1f);
        scr.GetComponent<MeshRenderer>().sharedMaterial.mainTexture = rt;
        var barBack = Quad("막대 바탕", sh, BarBack, 2993);
        barBack.localScale = new Vector3(ScreenW, BarH, 1f);
        barFill = Quad("막대 채움", sh, BarFill, 2994);
        barFill.localScale = new Vector3(0.01f, BarH, 1f);
        bBar = new SidePokeButton("막대 누름", cm, null, ScreenW, BarHitH, 1f, new Color(1f, 1f, 1f, 0f), new Color(1f, 1f, 1f, 0.08f), 2991);
        icPlay = Resources.Load<Texture2D>("Icons/Media/play");
        icPause = Resources.Load<Texture2D>("Icons/Media/pause");
        bBack = new SidePokeButton("영상 뒤로", cm, null, Btn, Btn, 1f, BtnIdle, BtnHover, 2995);
        bBack.SetIcon(Resources.Load<Texture2D>("Icons/Media/back5"));
        bPlay = new SidePokeButton("영상 재생", cm, null, Btn, Btn, 1f, BtnIdle, BtnHover, 2995);
        bPlay.SetIcon(icPause);
        bClose = new SidePokeButton("영상 닫기", cm, null, Btn, Btn, 1f, BtnIdle, BtnHover, 2995);
        bClose.SetIcon(Resources.Load<Texture2D>("Icons/Media/close"));
        parts = new[] { scr, barBack, barFill };

        vp = gameObject.AddComponent<VideoPlayer>();
        vp.playOnAwake = false;
        vp.isLooping = false;
        vp.renderMode = VideoRenderMode.RenderTexture;
        vp.targetTexture = rt;
        vp.audioOutputMode = VideoAudioOutputMode.None;
        vp.skipOnDrop = true;
        vp.errorReceived += (p, msg) => Debug.LogWarning($"[안내판영상] 재생 오류: {msg}");
    }

    /// <summary>위쪽 가운데를 (0, <paramref name="topY"/>)에 맞춘다(cm).</summary>
    public void Layout(float topY)
    {
        float y = topY - ScreenH * 0.5f;
        parts[0].localPosition = new Vector3(0f, y, 0.3f);
        barY = topY - ScreenH - Gap - BarHitH * 0.5f;
        parts[1].localPosition = new Vector3(0f, barY, 0.25f);
        bBar.Place(0f, barY);
        lastFill = -1;
        float by = barY - BarHitH * 0.5f - Gap - Btn * 0.5f;
        bBack.Place(-(Btn + BtnGap), by); bPlay.Place(0f, by); bClose.Place(Btn + BtnGap, by);
    }

    private Transform Quad(string n, Shader sh, Color col, int queue)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = n; q.hideFlags = HideFlags.DontSave;
        Destroy(q.GetComponent<Collider>());
        q.transform.SetParent(cm, false);
        var mr = q.GetComponent<MeshRenderer>();
        mr.sharedMaterial = new Material(sh) { color = col, renderQueue = queue };
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
        return q.transform;
    }

    private void SetPartsVisible(bool on)
    {
        foreach (var p in parts) if (p.gameObject.activeSelf != on) p.gameObject.SetActive(on);
        bBar.SetVisible(on); bBack.SetVisible(on); bPlay.SetVisible(on); bClose.SetVisible(on);
    }

    /// <summary>구간을 열고 처음부터 반복 재생한다. 같은 영상이면 다시 불러오지 않는다.</summary>
    public void Open(VideoClip clip, float start, float end)
    {
        if (clip == null || end <= start) { Debug.LogWarning($"[안내판영상] 못 엶 · 영상 {(clip != null ? clip.name : "★없음")} · 구간 {start}~{end}"); return; }
        open = true; SetPartsVisible(true);
        if (vp.clip != clip) vp.clip = clip;
        segStart = start; segEnd = Mathf.Min(end, (float)clip.length);
        pendingSeek = segStart; wantPlay = true; resumeAt = -1f; lastFill = -1;
        if (!vp.isPrepared) vp.Prepare();
        bPlay.SetIcon(icPause);
        Debug.Log($"[안내판영상] 엶 · '{clip.name}' 구간 {segStart:F0}~{segEnd:F0}초");
    }

    public void Close()
    {
        if (!open) return;
        open = false;
        if (vp.isPrepared) vp.Pause();
        resumeAt = -1f;
        SetPartsVisible(false);
        Debug.Log("[안내판영상] 닫음");
    }

    private void OnDestroy()
    {
        if (rt != null) { rt.Release(); Destroy(rt); }
    }

    private void Update()
    {
        if (!open || vp == null) return;
        if (vp.isPrepared)
        {
            if (pendingSeek >= 0) { vp.time = pendingSeek; pendingSeek = -1; if (wantPlay) vp.Play(); }
            double t = vp.time;
            if (resumeAt > 0f && Time.time >= resumeAt) { resumeAt = -1f; vp.time = segStart; vp.Play(); }
            else if (vp.isPlaying && t >= segEnd - 0.05) { vp.Pause(); resumeAt = Time.time + LoopDelay; }   // 1초 쉬고 처음으로

            float len = Mathf.Max(0.01f, segEnd - segStart);
            int fill = Mathf.RoundToInt(Mathf.Clamp01(((float)t - segStart) / len) * 100f);
            if (fill != lastFill)
            {
                lastFill = fill;
                float w = ScreenW * fill / 100f;
                barFill.localScale = new Vector3(Mathf.Max(0.01f, w), BarH, 1f);
                barFill.localPosition = new Vector3(-ScreenW * 0.5f + w * 0.5f, barY, 0.2f);
            }
        }

        hands.Resolve();
        if (bPlay.Poll(cm, hands, out _))
        {
            bool playing = vp.isPlaying || resumeAt > 0f;
            if (playing) { vp.Pause(); resumeAt = -1f; wantPlay = false; bPlay.SetIcon(icPlay); }
            else { wantPlay = true; bPlay.SetIcon(icPause); if (vp.time >= segEnd - 0.1) vp.time = segStart; vp.Play(); }
            Debug.Log($"[안내판영상] {(playing ? "일시정지" : "재생")}");
        }
        if (bBack.Poll(cm, hands, out _)) Seek((float)vp.time - 5f, "-5초");
        if (bBar.Poll(cm, hands, out float fx)) Seek(segStart + fx * (segEnd - segStart), "막대");
        if (bClose.Poll(cm, hands, out _)) Close();
    }

    /// <summary>구간 안으로만 옮긴다(구간 밖은 다른 단계의 동작이다).</summary>
    private void Seek(float to, string why)
    {
        to = Mathf.Clamp(to, segStart, Mathf.Max(segStart, segEnd - 0.1f));
        resumeAt = -1f; lastFill = -1;
        if (vp.isPrepared) { vp.time = to; if (wantPlay && !vp.isPlaying) vp.Play(); } else pendingSeek = to;
        Debug.Log($"[안내판영상] 이동({why}) → {to:F1}초");
    }
}
