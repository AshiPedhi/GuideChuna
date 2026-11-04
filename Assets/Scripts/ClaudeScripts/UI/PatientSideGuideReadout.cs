using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;
using TMPro;

/// <summary>
/// 실습 중 꼭 봐야 할 것만 보여 주는 작은 안내판 — 사용자 앞에 느슨하게 따라온다.
/// (2026-11-03 사용자 «진행하면서 꼭 확인해야 할 타이머 · 안내 중 필수 요소를» → 같은 날 4차까지 고쳤다.)
///
/// <code>
///   유지 ■■■■■□□□□□ 3초     ← 안내 창 진행 원이 보일 때만
///   반복 1 / 3회             ← touch= · reps= 단계일 때만
///   ┌ 영상 16×9 ┐ ━●── [⟲5][▶][✕]   ← [영상]을 눌렀을 때
///        [▶|]  [▶영상]        ← 정면 [시작/다음/종료]가 떠 있을 때 · 핵심 동작 단계일 때
/// </code>
///
/// ★11-03 4차 지시와 반영:
///   · «UI · 버튼이 너무 커서 앞을 가린다» → 글 0.8cm · 판 22cm · 아이콘 버튼 5cm.
///   · «일정 범위 안에서 사용자 앞에 따라오게» → 눈 앞 <see cref="Dist"/>m, <see cref="Down"/>° 아래.
///     고개를 <see cref="FollowYaw"/>° 넘게 돌리거나 <see cref="FollowMove"/>m 넘게 옮길 때만 천천히 따라온다. 환자 가슴 위 고정은 뺐다.
///   · «영상을 너무 다 보여 주려 한다(A)» → [영상] 버튼은 <c>Resources/SideVideo/핵심동작.csv</c>에 적은 단계에서만 뜬다.
///     CSV 구간은 그대로라 정면 전문가 영상 탭은 모든 단계 구간을 반복한다.
///   · «옆 새 창이 너무 크고 불편» → 판 안의 작은 화면(<see cref="PatientSideVideoScreen"/>).
///   · «텍스트 말고 아이콘» → <c>Resources/Icons/Media</c>.
///   · «매 과정 점수 · 끝 결과 카드는 필요 없다(정면 큰 화면에 있다)» → 둘 다 뺐다.
/// ★[▶|]는 정면 창 시작 토글을 <b>그대로 켠다</b>(<c>NextToggle.isOn = true</c> — 규칙 9 «같은 함수»).
/// ★<b>값을 새로 세지 않는다</b> — 유지 = 안내 창 진행 원의 값 · 반복 = 판정이 쓰는 값(08-24 · 09-07).
/// ★13개 술기 공용. 상부승모근 · 경추ROM은 머리 쪽 표시가 이미 있어 접는다.
/// ★런타임에 스스로 붙는다(TrainingScene). 인스펙터 튜닝 값은 Play를 멈추면 사라진다.
/// </summary>
public class PatientSideGuideReadout : MonoBehaviour
{
    const float Dist = 0.60f;           // 눈에서(m) ★추정값 — 헤드셋에서 조정
    const float Down = 15f;             // 정면에서 아래로(°) ★추정값 — 시술 손을 가리지 않게 너무 내리지 않는다
    const float FollowYaw = 30f;        // 고개를 이만큼 돌리면 따라온다(°)
    const float FollowMove = 0.4f;      // 이만큼 옮기면 따라온다(m)
    const float FollowSpeed = 4f;       // 따라오는 빠르기(1/초)
    const float FontSize = 8f;          // TextMeshPro(스케일 0.01) — 글자 높이 ≈ 0.8cm
    const float PanelWidthCm = 22f;
    const float Btn = 5f, BtnGap = 2f, Pad = 1.2f;
    static readonly Color Backdrop = new Color(0.06f, 0.07f, 0.10f, 0.72f);
    static readonly Color BtnIdle = new Color(0.20f, 0.45f, 0.90f, 0.95f);
    static readonly Color BtnHover = new Color(0.35f, 0.70f, 1.00f, 1.00f);
    const float NextW = 6f, NextH = 2f, NextFont = 7f;   // 11-04 [다음] 알약(cm) · 글자 ≈0.7cm
    static readonly Color PillIdle = new Color(0.965f, 0.965f, 0.965f, 1f);   // #F6F6F6 — 팝업 주 행동 알약(HzUIKit.White)
    static readonly Color PillHover = new Color(0.82f, 0.82f, 0.82f, 1f);
    static readonly Color PillInk = new Color(0.153f, 0.153f, 0.153f, 1f);    // #272727
    static readonly Color VidIdle = new Color(0.85f, 0.25f, 0.25f, 0.95f);
    static readonly Color VidHover = new Color(1.00f, 0.45f, 0.45f, 1.00f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        if (s.name != "TrainingScene") return;
        if (FindFirstObjectByType<PatientSideGuideReadout>() != null) return;
        new GameObject("환자 옆 안내 (런타임)").AddComponent<PatientSideGuideReadout>();
    }

    private Transform root, cm, backdrop;
    private TextMeshPro label;
    private ScenarioGuideUIController guideUI;
    private ChunaPathEvaluator evaluator;
    private ScenarioManager scenarioManager;
    private SubStepData sub;
    private bool shown = true;   // 첫 SetShown(false)가 실제로 끄게

    // 따라오기
    private bool anchored;
    private Vector3 anchorEye, anchorFwd, targetPos;

    // 바뀔 때만 글을 다시 만든다(Update 할당 금지 · CLAUDE.md 코드 컨벤션)
    private int lastHoldTenth = -2, lastHoldSec = -2, lastTouch = -2, lastReps = -2;
    private bool dirty = true, layoutDirty = true, vidHint;

    // 버튼
    private SidePokeButton nextBtn, videoBtn;
    private readonly RomRecordHands hands = new RomRecordHands();

    // 영상
    private PatientSideVideoScreen screen;
    private string clipLoadedFor;
    private VideoClip clip;
    private readonly HashSet<string> coreSteps = new HashSet<string>();

    private void Start()
    {
        root = transform;
        var c = new GameObject("cm") { hideFlags = HideFlags.DontSave };
        c.transform.SetParent(root, false);
        c.transform.localScale = Vector3.one * 0.01f;
        cm = c.transform;

        var t = new GameObject("글") { hideFlags = HideFlags.DontSave };
        t.transform.SetParent(cm, false);
        label = t.AddComponent<TextMeshPro>();
        var font = KoreanFontResolver.Resolve();
        if (font != null) label.font = font;
        label.fontSize = FontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.rectTransform.sizeDelta = new Vector2(PanelWidthCm, 6f);
        label.overflowMode = TextOverflowModes.Overflow;
        label.raycastTarget = false;

        Shader sh = Shader.Find("Sprites/Default"); if (sh == null) sh = Shader.Find("Standard");
        var bq = GameObject.CreatePrimitive(PrimitiveType.Quad);
        bq.name = "배경"; bq.hideFlags = HideFlags.DontSave;
        Destroy(bq.GetComponent<Collider>());
        bq.transform.SetParent(cm, false);
        var mr = bq.GetComponent<MeshRenderer>();
        mr.sharedMaterial = new Material(sh) { color = Backdrop, renderQueue = 2990 };
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
        backdrop = bq.transform;

        // ★11-04 사용자 «다음 버튼 디자인도 크기도 우리 규격에 안 맞고 거슬려» — 파란 정사각(5cm · 아이콘)을 버리고
        //   상단 메뉴 · 팝업과 같은 흰 알약 + «다음» 글자(굵게 · 어두운 글자)로. 크기는 눈에서의 각도로 상단 메뉴 알약과 비슷하게(★추정 — 헤드셋에서 확인)
        nextBtn = new SidePokeButton("다음 버튼", cm, font, NextW, NextH, NextFont, PillIdle, PillHover, 2995);
        nextBtn.MakePill();
        nextBtn.SetText("다음");
        nextBtn.Label.color = PillInk;
        videoBtn = new SidePokeButton("영상 버튼", cm, font, Btn, Btn, 1f, VidIdle, VidHover, 2995);
        videoBtn.SetIcon(Resources.Load<Texture2D>("Icons/Media/video"));
        nextBtn.SetVisible(false); videoBtn.SetVisible(false);

        screen = PatientSideVideoScreen.Create(cm, hands);
        LoadCoreSteps();

        var ev = ScenarioEventSystem.Instance;
        if (ev != null) { ev.OnSubStepStarted += HandleSubStep; ev.OnScenarioCompleted += HandleDone; }
        SetShown(false);
        Debug.Log($"[환자옆안내] 붙음 · 폰트 {(font != null ? font.name : "★없음")} · 이벤트 {(ev != null ? "O" : "★없음")} · 핵심 동작 {coreSteps.Count}개");
    }

    private void OnDestroy()
    {
        var ev = ScenarioEventSystem.Instance;
        if (ev != null) { ev.OnSubStepStarted -= HandleSubStep; ev.OnScenarioCompleted -= HandleDone; }
    }

    private void HandleDone(ScenarioData _)
    {
        sub = null;
        screen.Close();
        SetShown(false);
    }

    private void HandleSubStep(SubStepData s)
    {
        sub = s;
        dirty = true; layoutDirty = true;
        if (screen.IsOpen) screen.Close();   // 11-04 작은 화면은 더 안 연다
    }

    private void Update()
    {
        var cam = Camera.main;
        if (cam == null) return;
        if (sub == null || Suppressed()) { SetShown(false); screen.Close(); return; }

        bool lines = Refresh();
        bool btns = UpdateButtonStates();
        bool on = lines || btns || screen.IsOpen;
        SetShown(on);
        if (!on) return;
        if (layoutDirty) Layout();
        Follow(cam.transform);
        PollButtons();
    }

    /// <summary>상부승모근(ROM 양식 각도기) · 경추ROM은 머리 쪽 정보 표시가 이미 있다 — 겹치지 않게 접는다.</summary>
    private bool Suppressed()
    {
        if (CervicalRomPracticeReadout.ExternalSource != null) return true;
        if (!bridgeLooked) { romBridge = FindFirstObjectByType<CervicalRomScenarioBridge>(); bridgeLooked = true; }   // 매 프레임 찾지 않는다
        return romBridge != null && romBridge.RomScenarioActive;
    }
    private CervicalRomScenarioBridge romBridge;
    private bool bridgeLooked;

    // ── 따라오기 ─────────────────────────────────────────────────────────
    /// <summary>
    /// 눈 앞 <see cref="Dist"/>m · <see cref="Down"/>° 아래. 고개를 <see cref="FollowYaw"/>° 넘게 돌리거나
    /// <see cref="FollowMove"/>m 넘게 옮겼을 때만 새 자리로 천천히 옮긴다. 작은 움직임엔 가만히 있다(시술 중 흔들리지 않게).
    /// </summary>
    private void Follow(Transform eye)
    {
        // ★11-04 사용자 «위치 너무 높아 · 고개 숙여도 그냥 그 위치에만 있잖아 · 바라보는 방향 보기 편한 위치로 오게 하라고 했는데».
        //   종전(11-03)은 fwd.y = 0으로 <b>위아래 시선을 지웠다</b> — 고개를 숙여 환자를 봐도 판은 눈높이 −15°에 남았다(CLAUDE.md «내가 건 제약이 무엇을 지우나»).
        //   → 시선 방향 그대로(위아래 포함)를 쓰고, 그 시선에서 Down°만 아래에 놓는다. 따라오는 판정도 3차원 각도로 잰다.
        Vector3 fwd = eye.forward;
        if (fwd.sqrMagnitude < 1e-4f) fwd = anchored ? anchorFwd : Vector3.forward;
        fwd.Normalize();
        bool jump = !anchored;
        if (!anchored || Vector3.Angle(fwd, anchorFwd) > FollowYaw || (eye.position - anchorEye).sqrMagnitude > FollowMove * FollowMove)
        {
            anchorFwd = fwd; anchorEye = eye.position;
            Vector3 dir = Vector3.RotateTowards(fwd, Vector3.down, Down * Mathf.Deg2Rad, 0f);   // 시선에서 Down°만 아래(바로 아래를 보면 그대로)
            targetPos = eye.position + dir * Dist;
            if (anchored) Debug.Log($"[환자옆안내] 따라옴 → {targetPos:F2}");
            anchored = true;
        }
        root.position = jump ? targetPos : Vector3.Lerp(root.position, targetPos, 1f - Mathf.Exp(-FollowSpeed * Time.deltaTime));
        Vector3 look = root.position - eye.position;
        if (look.sqrMagnitude > 1e-6f)
        {
            Quaternion want = Quaternion.LookRotation(look, Vector3.up);
            root.rotation = jump ? want : Quaternion.Slerp(root.rotation, want, 1f - Mathf.Exp(-FollowSpeed * Time.deltaTime));
        }
    }

    // ── 글 ──────────────────────────────────────────────────────────────
    /// <summary>유지 · 반복 줄. 보여 줄 줄이 하나라도 있으면 true.</summary>
    private bool Refresh()
    {
        if (guideUI == null) guideUI = FindFirstObjectByType<ScenarioGuideUIController>();
        if (evaluator == null) evaluator = FindFirstObjectByType<ChunaPathEvaluator>();

        bool hold = guideUI != null && guideUI.ProgressVisible && !guideUI.ProgressCompleted;
        int holdTenth = hold ? Mathf.Clamp(Mathf.RoundToInt(guideUI.ProgressRatio01 * 10f), 0, 10) : -1;
        int holdSec = hold ? Mathf.CeilToInt(guideUI.ProgressRemaining) : -1;
        // ★11-04 사용자 «3회 반복 끝났으면 정보 UI에 3회 텍스트가 없어져야지 살아 있네» — 실측(1:56 화면): 재평가(cranialTouch · 손 녹화 있음)에서
        //   판정기의 반복 목표(TouchTarget 6)가 앞 단계 값으로 남아 «반복 3/3회»가 떴다. 반복 줄은 <b>지금 단계에 touch= · reps= 가 있을 때만</b>.
        string cp = sub.conditionParams ?? "";
        bool repStep = cp.Contains("touch=") || cp.Contains("reps=");
        bool rec = repStep && evaluator != null && !string.IsNullOrEmpty(sub.handTrackingFileName);
        int touch = rec && evaluator.TouchTarget > 0 ? evaluator.TouchCount : -1;
        int reps = rec && evaluator.RepsRequired > 1 ? evaluator.RepsDone : -1;
        // ★11-04 사용자 «정면 가이드에서 영상 참조 버튼 나오는 거 빼 · 참조 영상 보라고 안내만 해» —
        //   [영상] 버튼 · 판 안 작은 화면은 띄우지 않고, 같은 핵심 동작 단계에서 안내 한 줄만. 단계가 바뀔 때만 다시 잰다(StepKey가 문자열을 만든다).
        if (dirty) vidHint = VideoAvailable();
        bool any = hold || touch >= 0 || reps >= 0 || vidHint;

        if (!dirty && holdTenth == lastHoldTenth && holdSec == lastHoldSec && touch == lastTouch && reps == lastReps) return any;
        bool lineCountChanged = true;   // 안내 줄이 단계마다 달라질 수 있어 글이 바뀔 때마다 다시 놓는다(바뀔 때만 오는 자리다)
        if (!dirty) lineCountChanged = (holdTenth >= 0) != (lastHoldTenth >= 0) || (touch >= 0 || reps >= 0) != (lastTouch >= 0 || lastReps >= 0);
        dirty = false;
        lastHoldTenth = holdTenth; lastHoldSec = holdSec; lastTouch = touch; lastReps = reps;

        var sb = new System.Text.StringBuilder(96);
        if (hold) sb.Append("<color=#7FD3FF>유지 ").Append(Bar(holdTenth)).Append(' ').Append(holdSec).Append("초</color>");
        if (touch >= 0) Line(sb).Append("<color=#FFB36B>반복 ").Append(touch / 2).Append(" / ").Append(evaluator.TouchTarget / 2).Append("회</color>");
        else if (reps >= 0) Line(sb).Append("<color=#FFB36B>반복 ").Append(reps).Append(" / ").Append(evaluator.RepsRequired).Append("회</color>");
        if (vidHint) Line(sb).Append("<color=#FF9A9A>전문가 영상을 참고하세요</color>");
        label.text = sb.ToString();
        if (lineCountChanged) layoutDirty = true;
        return any;
    }

    private static System.Text.StringBuilder Line(System.Text.StringBuilder sb) => sb.Length > 0 ? sb.Append('\n') : sb;

    // ── 버튼 ─────────────────────────────────────────────────────────────
    private bool UpdateButtonStates()
    {
        var obj = guideUI != null ? guideUI.NextToggleObject : null;
        var tg = guideUI != null ? guideUI.NextToggle : null;
        // ★11-04 사용자 «다음으로 넘기는 표시 시작 타이머 밑으로» — [다음]은 상단 [시작] 아래(TopBarNextPlate)로 옮겼다. 여기선 안 띄운다
        bool nextOn = false && obj != null && obj.activeInHierarchy && tg != null && tg.interactable;
        const bool vidOn = false;   // ★11-04 [영상] 버튼 뺌 — 안내 줄(Refresh의 vidHint)로 바꿨다. 버튼 · 작은 화면 코드는 되살릴 수 있게 둔다
        if (nextOn != nextBtn.Visible || vidOn != videoBtn.Visible)
        {
            nextBtn.SetVisible(nextOn); videoBtn.SetVisible(vidOn);
            layoutDirty = true;
            Debug.Log($"[환자옆안내] 버튼 · 다음 {(nextOn ? "O" : "-")} · 영상 {(vidOn ? sub.videoStartTime + "~" + sub.videoEndTime : "-")}");
        }
        return nextOn || vidOn;
    }

    private void PollButtons()
    {
        if (!nextBtn.Visible && !videoBtn.Visible) return;
        hands.Resolve();
        if (nextBtn.Poll(cm, hands, out _))
        {
            var tg = guideUI != null ? guideUI.NextToggle : null;
            Debug.Log("[환자옆안내] [다음] 누름 → 정면 시작 토글 켬");
            if (tg != null) tg.isOn = true;   // 정면 버튼을 누른 것과 같은 경로(OnStartToggleChanged)
        }
        if (videoBtn.Poll(cm, hands, out _)) OpenVideo();
    }

    // ── 영상 ─────────────────────────────────────────────────────────────
    /// <summary>핵심 동작 목록에 있고 · CSV 구간이 있고 · 영상 파일이 있을 때.</summary>
    private bool VideoAvailable()
    {
        if (sub == null || !sub.HasVideoSegment()) return false;
        if (scenarioManager == null) scenarioManager = FindFirstObjectByType<ScenarioManager>();
        if (scenarioManager == null || scenarioManager.CurrentScenario == null) return false;
        if (!coreSteps.Contains(StepKey())) return false;
        return ClipForScenario() != null;
    }

    private string StepKey()
    {
        var step = scenarioManager.CurrentStep;
        string phase = scenarioManager.CurrentPhase != null ? scenarioManager.CurrentPhase.phaseName : "";
        return scenarioManager.CurrentScenario.scenarioName + "|" + phase + "|" + (step != null ? step.stepNo : 0) + "|" + sub.subStepNo;
    }

    private void LoadCoreSteps()
    {
        coreSteps.Clear();
        var ta = Resources.Load<TextAsset>("SideVideo/핵심동작");
        if (ta == null) { Debug.LogWarning("[환자옆안내] ★핵심동작.csv 없음 — [영상] 버튼이 안 뜬다"); return; }
        var lines = ta.text.Split('\n');
        for (int i = 1; i < lines.Length; i++)
        {
            var c = lines[i].Trim().Split(',');
            if (c.Length >= 4 && c[0].Length > 0) coreSteps.Add(c[0].Trim() + "|" + c[1].Trim() + "|" + c[2].Trim() + "|" + c[3].Trim());
        }
    }

    private void OpenVideo()
    {
        screen.Open(ClipForScenario(), sub.GetVideoStartSeconds(), sub.GetVideoEndSeconds());
        layoutDirty = true;
    }

    /// <summary>정면 창 <see cref="GuideVideoController"/>와 같은 규칙 — <c>Resources/Videos/{scenarioName}</c>. 시나리오가 바뀔 때만 불러온다.</summary>
    private VideoClip ClipForScenario()
    {
        string scen = scenarioManager != null && scenarioManager.CurrentScenario != null ? scenarioManager.CurrentScenario.scenarioName : null;
        if (scen != clipLoadedFor)
        {
            clipLoadedFor = scen;
            clip = string.IsNullOrEmpty(scen) ? null : Resources.Load<VideoClip>("Videos/" + scen);
            Debug.Log($"[환자옆안내] 전문가 영상 '{scen}' {(clip != null ? clip.length.ToString("F0") + "초" : "★없음 — [영상] 안 뜸")}");
        }
        return clip;
    }

    private static string Bar(int filled)
    {
        filled = Mathf.Clamp(filled, 0, 10);
        return new string('■', filled) + new string('□', 10 - filled);
    }

    // ── 배치(cm) — 위에서 아래로: 글 → 영상 → 버튼 ─────────────────────────────
    private void Layout()
    {
        layoutDirty = false;
        float y = 0f, top = Pad;
        if (!string.IsNullOrEmpty(label.text))
        {
            label.ForceMeshUpdate();
            float h = label.GetRenderedValues(false).y;
            if (h <= 1e-4f || float.IsNaN(h) || h > 1000f) h = label.preferredHeight;
            label.rectTransform.localPosition = new Vector3(0f, -h * 0.5f, 0f);
            y = -h;
        }
        if (screen.IsOpen)
        {
            if (y < 0f) y -= Pad;
            screen.Layout(y);
            y -= PatientSideVideoScreen.Height;
        }
        // 11-04: [영상] 버튼은 뺐다 — [다음] 알약 하나만 놓는다
        int n = nextBtn.Visible ? 1 : 0;
        if (n > 0)
        {
            if (y < 0f) y -= Pad;
            nextBtn.Place(0f, y - nextBtn.H * 0.5f);
            y -= nextBtn.H;
        }
        float bottom = y - Pad;
        float w = screen.IsOpen ? PatientSideVideoScreen.ScreenW + Pad * 2f : (string.IsNullOrEmpty(label.text) ? nextBtn.W + Pad * 2f : PanelWidthCm);
        backdrop.localPosition = new Vector3(0f, (top + bottom) * 0.5f, 0.6f);
        backdrop.localRotation = Quaternion.identity;
        backdrop.localScale = new Vector3(w, top - bottom, 1f);
    }

    private void SetShown(bool on)
    {
        if (shown == on) return;
        shown = on;
        cm.gameObject.SetActive(on);
        if (!on) anchored = false;   // 다시 뜰 때는 그 순간 정면에
    }
}
