using System.Text;
using TMPro;
using UnityEngine;

public enum RomLinePoint
{
    TrRight = 0,
    TrLeft = 1,
    Gl = 2,
    Gn = 3,
}

/// <summary>
/// ★경추 ROM «선 실측» — 본체(2026-10-01 신설, 전용 씬 <c>RomLineScene</c>에서 돈다).
///
/// 9/30 교수님 피드백으로 바뀐 방식이다. 기존 실측(<c>RomRecordSession</c> · <c>RomMarkerScene</c>)은 <b>그대로 두고</b> 따로 만든다.
///   · 중립에서 네 점(오른쪽 Tr · 왼쪽 Tr · Gl · Gn)을 찍는다 → 환자 기준 방향 + 세 동작의 중립 기준선
///   · 동작마다 능동 2점 · 압박 2점을 찍는다 → 중립 기준선에서 돌아간 각(환자 기준 면 위에서 잰다)
///   · 순서 고정: 중립 → 굴곡 → 신전 → 우측굴 → 좌측굴 → 우회전 → 좌회전(각각 능동 → 압박)
///
/// 조작은 <b>컨트롤러 하나</b>다(주로 오른손, 바꿔 쥐면 그쪽이 그대로 동작). 반대 손은 환자를 잡는 데 쓴다.
///   트리거 = 점 찍기 · 아래 버튼(A/X) = 한 점 취소 · 위 버튼(B/Y) = 건너뛰기 · 그립 = 판 옮기기 · 스틱 좌우 = 앞뒤 단계
///   판의 버튼은 컨트롤러 끝을 버튼에 대고 트리거를 당긴다(«중립 다시»·«나가기»는 1초 길게).
///   ★레이로 멀리서 누르게 하지 않았다 — 환자 머리에 점을 찍는 순간 뒤에 있는 판이 눌리는 일을 막으려고.
///
/// ★임시로 정해 둔 것(사용자 답 대기, 10-01): 각이 작아 교점이 멀면 각도기를 중립선 첫 점에 세운다 ·
///   압박은 값만 더하고 각도기는 능동 때 자리에 둔다 · 면 밖으로 벗어난 양은 로그에만 남긴다.
/// </summary>
public class RomLineSession : MonoBehaviour
{
    public const int GroupCount = 13;          // 중립 1 + 6동작 × (능동·압박)
    public const int PointCount = 28;          // 중립 4 + 12 × 2

    public static readonly string[] MotionName = { "굴곡", "신전", "우측굴", "좌측굴", "우회전", "좌회전" };
    public static readonly string[] PointName = { "오른쪽 Tr", "왼쪽 Tr", "Gl", "Gn" };
    private static readonly string[] PhaseName = { "능동", "압박" };

    [Header("=== 컨트롤러 ===")]
    [Tooltip("컨트롤러 기준 «찍는 점»의 자리(m). 노란 점이 여기에 뜬다 — 패스스루로 보이는 실제 컨트롤러 끝에 맞춘다.\n" +
             "★처음 값은 어림이다. 기기에서 보고 인스펙터로 맞춘다.")]
    [SerializeField] private Vector3 tipOffset = new Vector3(0f, 0f, 0.05f);

    [Header("=== 기타 ===")]
    [SerializeField] private float soundVolume = 1f;
    [SerializeField] private string lobbySceneName = "lobby";

    // 단계
    private readonly Vector3[] points = new Vector3[PointCount];
    private readonly bool[] has = new bool[PointCount];
    private readonly RomLineResult[] results = new RomLineResult[12];
    private RomLineFrame frame;
    private int group;      // 0 = 중립, 1~12 = 동작·단계, 13 = 끝
    private int index;      // 그 묶음 안에서 다음에 찍을 점

    // 입력
    private Transform trackingSpace;
    private Transform eye;
    private OVRInput.Controller active = OVRInput.Controller.RTouch;
    private bool tipValid;
    private Vector3 tipPos;
    private float prevTrigger;
    private Vector3 armedPos;
    private float armedTime = -10f;
    private bool stickReady = true;
    private bool grabbing;
    private Vector3 grabOffset;
    private int holdButton = -1;
    private int switchedFrame = -1;
    private float holdStart;

    // 표시
    private readonly RomLineView view = new RomLineView();
    private bool dirty = true;
    private string noteText = "";
    private float trackingWaitStart = -1f;
    private float nextSilentLog;
    private string lastSilent;

    private AudioSource audioSrc;
    private AudioClip sndMark, sndUndo, sndDeny, sndPress, sndDone;

    private const float HoldSeconds = 1f;
    private const float ArmThreshold = 0.15f;      // 트리거를 이만큼 당기기 시작한 순간의 자리를 찍는 자리로 쓴다
    private const float TrackingWaitSeconds = 8f;

    private bool EyeReady => eye != null && eye.position.y > 0.3f;
    private bool Done => group >= GroupCount;

    // ── 단계 ↔ 점 ───────────────────────────────────────────────────
    private static int GroupStart(int g) => g == 0 ? 0 : 4 + (g - 1) * 2;
    private static int GroupSize(int g) => g == 0 ? 4 : 2;

    /// <summary>그 동작이 쓰는 두 점. ★검증기(RomLineVerify)도 이 함수를 그대로 쓴다.</summary>
    public static void LineOf(RomLineMotion m, out RomLinePoint a, out RomLinePoint b)
    {
        switch (m)
        {
            case RomLineMotion.Flexion:
            case RomLineMotion.Extension: a = RomLinePoint.TrRight; b = RomLinePoint.Gl; break;    // 우측면: 오른쪽 Tr ~ Gl
            case RomLineMotion.LateralRight:
            case RomLineMotion.LateralLeft: a = RomLinePoint.Gl; b = RomLinePoint.Gn; break;       // 정면: Gl ~ Gn
            case RomLineMotion.RotationRight: a = RomLinePoint.TrLeft; b = RomLinePoint.Gl; break; // 반대쪽(왼쪽) Tr ~ Gl
            default: a = RomLinePoint.TrRight; b = RomLinePoint.Gl; break;                         // 좌회전: 오른쪽 Tr ~ Gl
        }
    }

    private RomLinePoint ExpectedPoint(int g, int i)
    {
        if (g == 0) return (RomLinePoint)i;
        LineOf((RomLineMotion)((g - 1) / 2), out RomLinePoint a, out RomLinePoint b);
        return i == 0 ? a : b;
    }

    private void Start()
    {
        EnablePassthrough();

        var rig = FindAnyObjectByType<OVRCameraRig>();
        if (rig != null) trackingSpace = rig.trackingSpace;

        TMP_FontAsset font = KoreanFontResolver.Resolve();
        Shader sh = Shader.Find("Sprites/Default");   // Always Included에 들어 있다(기존 실측과 같은 셰이더)
        if (sh == null)
        {
            Debug.LogError("[선실측] ★Sprites/Default 셰이더를 못 찾았다 — 표시를 만들 수 없어 멈춘다.");
            enabled = false;
            return;
        }
        view.Build(transform, font, new Material(sh));
        BuildSounds();

        Debug.Log($"[선실측] 붙었다 — 리그 {(rig != null ? "있음" : "★없음")} · 추적공간 {(trackingSpace != null ? "있음" : "★없음(컨트롤러 자리를 못 잡는다)")} · " +
                  $"셰이더 {(sh != null ? "있음" : "★없음")} · 폰트 {(font != null ? font.name : "★없음")} · 끝점 오프셋 {tipOffset}");
    }

    private void Update()
    {
        if (eye == null && Camera.main != null) eye = Camera.main.transform;
        if (eye == null) { Silent("눈(Camera.main)이 없다"); return; }

        // ★첫 프레임에 자리를 잡지 않는다 — 추적이 붙기 전엔 눈이 원점이라 판이 발밑에 선다(09-21 전례).
        if (!view.PanelPlaced)
        {
            if (!EyeReady)
            {
                if (trackingWaitStart < 0f) trackingWaitStart = Time.unscaledTime;
                if (Time.unscaledTime - trackingWaitStart < TrackingWaitSeconds) { Silent("헤드셋 추적을 기다린다"); return; }
            }
            view.PlacePanel(eye);
            Debug.Log($"[선실측] 판을 세웠다 — 눈 {eye.position:F2} · 판 {view.Panel.position:F2}");
        }

        ReadController();
        HandleInput();

        if (dirty) Refresh();
        view.Tick(eye, tipValid, tipPos, !Done);
    }

    // ── 컨트롤러 ────────────────────────────────────────────────────
    private void ReadController()
    {
        tipValid = false;
        if (trackingSpace == null) { Silent("추적공간이 없다 — OVRCameraRig를 못 찾았다"); return; }

        // 쥐고 있지 않은 쪽에서 무언가 눌리면 그쪽으로 바꾼다. ★바꾼 그 누름은 «전환»으로만 쓴다 —
        //   컨트롤러를 바꿔 쥐다 눌린 트리거가 점으로 찍히지 않게.
        OVRInput.Controller other = active == OVRInput.Controller.RTouch ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
        if (AnyDown(other))
        {
            active = other;
            prevTrigger = 1f;       // 이번 당김은 끝날 때까지 무시
            armedTime = -10f;
            grabbing = false;
            switchedFrame = Time.frameCount;
            Debug.Log($"[선실측] 쓰는 컨트롤러 → {(active == OVRInput.Controller.RTouch ? "오른쪽" : "왼쪽")}");
            Play(sndPress);
        }

        if (!OVRInput.IsControllerConnected(active) || !OVRInput.GetControllerPositionTracked(active))
        {
            Silent($"{(active == OVRInput.Controller.RTouch ? "오른쪽" : "왼쪽")} 컨트롤러가 연결·추적되지 않는다 — 점을 찍을 수 없다");
            return;
        }

        Vector3 p = trackingSpace.TransformPoint(OVRInput.GetLocalControllerPosition(active));
        Quaternion r = trackingSpace.rotation * OVRInput.GetLocalControllerRotation(active);
        tipPos = p + r * tipOffset;
        tipValid = true;
        lastSilent = null;
    }

    private static bool AnyDown(OVRInput.Controller c)
    {
        return OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, c) || OVRInput.GetDown(OVRInput.Button.One, c)
            || OVRInput.GetDown(OVRInput.Button.Two, c) || OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, c);
    }

    private void HandleInput()
    {
        if (!tipValid)
        {
            view.SetHot(-1, 0f);
            holdButton = -1;
            return;
        }

        if (Time.frameCount == switchedFrame) return;   // 컨트롤러를 바꾼 그 누름은 전환으로만 쓴다

        // 트리거를 당기기 시작한 순간의 자리를 잡아 둔다 — 끝까지 당기는 동안 컨트롤러가 흔들린다.
        float trigger = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, active);
        if (prevTrigger < ArmThreshold && trigger >= ArmThreshold)
        {
            armedPos = tipPos;
            armedTime = Time.unscaledTime;
        }
        bool triggerDown = prevTrigger < 0.55f && trigger >= 0.55f;
        bool triggerHeld = trigger >= 0.55f;
        prevTrigger = trigger;

        // ── 판 옮기기(그립) ──
        bool grip = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, active);
        if (!grabbing && OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, active) && view.NearPanel(tipPos, 0.12f))
        {
            grabbing = true;
            grabOffset = view.Panel.position - tipPos;
        }
        if (grabbing)
        {
            if (grip) view.MovePanel(tipPos + grabOffset, eye);
            else
            {
                grabbing = false;
                Debug.Log($"[선실측] 판을 옮겼다 — {view.Panel.position:F2} (눈에서 {Vector3.Distance(view.Panel.position, eye.position) * 100f:F0}cm)");
            }
            view.SetHot(-1, 0f);
            return;
        }

        // ── 판 버튼 ──
        int button = view.Touch(tipPos, out bool onPanel);
        if (onPanel)
        {
            if (button != holdButton) { holdButton = -1; }
            bool needHold = button >= 5;   // «중립 다시» · «나가기»
            if (button >= 0 && triggerDown)
            {
                if (needHold) { holdButton = button; holdStart = Time.unscaledTime; Play(sndPress); }
                else Press(button);
            }
            float hold01 = 0f;
            if (holdButton >= 0 && holdButton == button)
            {
                if (!triggerHeld) holdButton = -1;
                else
                {
                    hold01 = (Time.unscaledTime - holdStart) / HoldSeconds;
                    if (hold01 >= 1f) { int b = holdButton; holdButton = -1; hold01 = 0f; Press(b); }
                }
            }
            view.SetHot(button, hold01);
            return;   // ★판 위에서는 점을 찍지 않는다
        }
        holdButton = -1;
        view.SetHot(-1, 0f);

        // ── 버튼·스틱 ──
        if (OVRInput.GetDown(OVRInput.Button.One, active)) { Undo(); return; }
        if (OVRInput.GetDown(OVRInput.Button.Two, active)) { Skip(); return; }

        float sx = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, active).x;
        if (Mathf.Abs(sx) < 0.3f) stickReady = true;
        else if (stickReady && Mathf.Abs(sx) > 0.7f)
        {
            stickReady = false;
            Go(group + (sx > 0f ? 1 : -1), "스틱");
            return;
        }

        // ── 점 찍기 ──
        if (triggerDown)
        {
            bool armed = Time.unscaledTime - armedTime < 0.6f;
            Vector3 at = armed ? armedPos : tipPos;
            float shake = armed ? Vector3.Distance(armedPos, tipPos) : 0f;
            Mark(at, shake);
        }
    }

    // ── 동작 ────────────────────────────────────────────────────────
    private void Mark(Vector3 pos, float shake)
    {
        if (Done) { Deny("측정이 끝났다 — 다시 재려면 탭이나 스틱으로 그 단계로 간다"); return; }

        int start = GroupStart(group), size = GroupSize(group);
        if (index == 0)
        {
            // 이미 찍은 묶음에 다시 들어와 첫 점을 찍으면 그 묶음을 새로 잰다.
            for (int i = 0; i < size; i++) has[start + i] = false;
            if (group == 0) InvalidateAll();
            else results[group - 1] = default;
        }

        RomLinePoint which = ExpectedPoint(group, index);
        points[start + index] = pos;
        has[start + index] = true;
        Debug.Log($"[선실측] 찍음 — {GroupTitle(group)} · {PointName[(int)which]} ({index + 1}/{size}) · 자리 {pos:F3} · " +
                  $"당기는 동안 끝점 이동 {shake * 1000f:F1}mm");
        index++;
        noteText = "";
        Play(sndMark);

        if (index >= size) CompleteGroup();
        dirty = true;
    }

    private void CompleteGroup()
    {
        if (group == 0)
        {
            frame = RomLineMath.BuildFrame(points[0], points[1], points[2]);
            if (!frame.valid)
            {
                for (int i = 0; i < 4; i++) has[i] = false;
                index = 0;
                Deny("양쪽 Tr이 같은 자리에 찍혔다 — 중립을 다시 찍는다");
                return;
            }
            if (frame.sidesSwapped)
            {
                (points[0], points[1]) = (points[1], points[0]);
                noteText = "양쪽 Tr이 바뀌어 찍혀 바로잡았다";
                Debug.Log("[선실측] ★양쪽 Tr을 바꿔 찍은 것으로 보인다(Gl이 뒤쪽에 있었다) — 좌우를 바로잡았다.");
            }
            Debug.Log($"[선실측] 기준 방향 — 오른쪽 {frame.right:F2} · 앞 {frame.forward:F2} · 위 {frame.up:F2} · " +
                      $"Tr 간격 {Vector3.Distance(points[0], points[1]) * 100f:F1}cm · Tr~Gl {Vector3.Distance(points[0], points[2]) * 100f:F1}cm · " +
                      $"Gl~Gn {Vector3.Distance(points[2], points[3]) * 100f:F1}cm");
            for (int g = 1; g < GroupCount; g++) Compute(g);   // 중립만 다시 찍었을 때 남아 있는 측정을 새 기준으로 다시 잰다
        }
        else
        {
            Compute(group);
            if (!results[group - 1].valid)
            {
                has[GroupStart(group)] = has[GroupStart(group) + 1] = false;
                index = 0;
                return;
            }
        }

        group++;
        index = 0;
        if (Done)
        {
            Play(sndDone);
            LogSummary("끝");
        }
    }

    /// <summary>그 묶음의 두 점이 다 있으면 각을 낸다.</summary>
    private void Compute(int g)
    {
        results[g - 1] = default;
        int start = GroupStart(g);
        if (!frame.valid || !has[start] || !has[start + 1]) return;

        var motion = (RomLineMotion)((g - 1) / 2);
        LineOf(motion, out RomLinePoint a, out RomLinePoint b);
        Vector3 normal = RomLineMath.PlaneNormal(motion, frame);
        RomLineResult r = RomLineMath.Measure(points[(int)a], points[(int)b], points[start], points[start + 1], normal);
        results[g - 1] = r;

        if (!r.valid)
        {
            Debug.LogWarning($"[선실측] ★{GroupTitle(g)} — 각을 못 냈다(두 점이 같은 자리이거나 선이 면에 수직이다).");
            if (g == group) Deny("각을 못 냈다 — 두 점을 다시 찍는다");
            return;
        }

        bool match = RomLineMath.DirectionMatches(motion, r.signedAngle);
        Debug.Log($"[선실측] 값 — {GroupTitle(g)} {r.angle:F1}° (부호 {r.signedAngle:+0.0;-0.0}{(match ? "" : " ★반대 방향")}) · " +
                  $"면 밖 각 중립선 {r.neutralOffPlane:F1}° → 측정선 {r.markOffPlane:F1}° (변화 {r.markOffPlane - r.neutralOffPlane:+0.0;-0.0}° — 다른 동작이 섞인 정도) · " +
                  $"교점 {(r.pivotOnLines ? $"선 위(중립 첫 점에서 {Vector3.Distance(r.pivot, r.neutralA) * 100f:F1}cm)" : "멀거나 평행 — 첫 점에 세움")}");
        if (!match && g == group)
        {
            noteText = $"{MotionName[(int)motion]} 단계인데 반대쪽으로 움직인 값이다";
            Play(sndDeny);
        }
    }

    private void Undo()
    {
        if (index > 0)
        {
            index--;
            has[GroupStart(group) + index] = false;
        }
        else if (group > 0)
        {
            group = Mathf.Min(group, GroupCount) - 1;
            index = GroupSize(group) - 1;
            has[GroupStart(group) + index] = false;
            // 건너뛴 묶음이면 앞 점이 없다 — 마지막 점만 찍으면 선이 안 되므로 처음부터 찍게 한다.
            for (int i = 0; i < index; i++)
                if (!has[GroupStart(group) + i]) { index = 0; break; }
            if (group == 0) InvalidateAll();
            else results[group - 1] = default;
        }
        else { Deny("지울 점이 없다"); return; }

        noteText = "";
        Debug.Log($"[선실측] 취소 → {GroupTitle(group)} {index + 1}번째 점부터");
        Play(sndUndo);
        dirty = true;
    }

    private void Skip()
    {
        if (!frame.valid) { Deny("중립은 건너뛸 수 없다"); return; }
        if (Done) return;
        Go(group + 1, "건너뜀");
    }

    /// <summary>단계를 옮긴다(찍어 둔 점은 그대로 둔다 — 그 단계에서 첫 점을 찍는 순간 새로 잰다).</summary>
    private void Go(int target, string why)
    {
        target = Mathf.Clamp(target, 0, GroupCount);
        if (target > 0 && !frame.valid) { Deny("중립 네 점을 먼저 찍는다"); return; }
        if (target == group && index == 0) return;
        group = target;
        index = 0;
        noteText = "";
        Debug.Log($"[선실측] 단계 → {GroupTitle(group)} ({why})");
        Play(sndPress);
        dirty = true;
    }

    private void Press(int button)
    {
        switch (button)
        {
            case 0: Go(0, "탭"); break;
            case 1: Go(1, "탭"); break;
            case 2: Go(5, "탭"); break;
            case 3: Go(9, "탭"); break;
            case 4: RedoMotion(); break;
            case 5: ResetAll(); break;
            case 6: Exit(); break;
        }
    }

    private void RedoMotion()
    {
        if (group == 0 || !frame.valid) { ResetAll(); return; }
        int m = (Mathf.Min(group, GroupCount - 1) - 1) / 2;
        for (int g = 1 + m * 2; g <= 2 + m * 2; g++)
        {
            has[GroupStart(g)] = has[GroupStart(g) + 1] = false;
            results[g - 1] = default;
        }
        group = 1 + m * 2;
        index = 0;
        noteText = "";
        Debug.Log($"[선실측] 이 동작 다시 — {MotionName[m]}");
        Play(sndUndo);
        dirty = true;
    }

    private void ResetAll()
    {
        for (int i = 0; i < PointCount; i++) has[i] = false;
        InvalidateAll();
        group = 0;
        index = 0;
        noteText = "";
        Debug.Log("[선실측] 중립 다시 — 전부 지웠다");
        Play(sndUndo);
        dirty = true;
    }

    private void InvalidateAll()
    {
        frame = default;
        for (int i = 0; i < results.Length; i++) results[i] = default;
    }

    private void Exit()
    {
        LogSummary("나가기");
        Debug.Log("[선실측] 나가기 → " + lobbySceneName);
        SceneLoader.LoadScene(lobbySceneName);
    }

    private void Deny(string why)
    {
        noteText = why;
        Debug.Log("[선실측] 받지 않음 — " + why);
        Play(sndDeny);
        dirty = true;
    }

    // ── 표시 갱신(상태가 바뀔 때만) ──────────────────────────────────
    private string GroupTitle(int g)
    {
        if (g <= 0) return "중립";
        if (g >= GroupCount) return "측정 끝";
        return MotionName[(g - 1) / 2] + " · " + PhaseName[(g - 1) % 2];
    }

    private void Refresh()
    {
        dirty = false;

        int tab = group == 0 ? 0 : group <= 4 ? 1 : group <= 8 ? 2 : 3;
        string instruction, tipText;
        if (Done)
        {
            instruction = "모든 방향을 쟀습니다";
            tipText = "";
        }
        else
        {
            RomLinePoint p = ExpectedPoint(group, index);
            instruction = $"{PointName[(int)p]} 을 찍으세요  ({index + 1}/{GroupSize(group)})";
            tipText = PointName[(int)p];
        }
        view.SetTexts(GroupTitle(group), instruction, noteText, tab, tipText);

        for (int i = 0; i < 12; i++)
            view.SetValue(i, results[i].valid ? results[i].angle.ToString("F1") + "°" : "—", i == group - 1);

        // 점 — 중립 넷은 늘, 측정 점은 지금 동작 것만.
        int motion = group == 0 ? -1 : (Mathf.Min(group, GroupCount - 1) - 1) / 2;
        for (int i = 0; i < PointCount; i++)
        {
            bool show = has[i] && (i < 4 || (i - 4) / 4 == motion);
            view.SetPoint(i, show, points[i]);
        }

        // 선·각도기 — 지금 동작 것만.
        if (motion < 0 || !frame.valid) { view.HideMeasure(); return; }
        RomLineResult a = results[motion * 2], pr = results[motion * 2 + 1];
        LineOf((RomLineMotion)motion, out RomLinePoint la, out RomLinePoint lb);
        Vector3 normal = RomLineMath.PlaneNormal((RomLineMotion)motion, frame);
        if (a.valid)
            view.ShowMeasure(a, pr.valid, pr, normal, a.angle.ToString("F1") + "°", pr.valid ? "압박 " + pr.angle.ToString("F1") + "°" : "");
        else
        {
            // 중립 기준선만 — 면 위로 옮긴 선을 그린다(능동을 찍으면 이 선과 만난다).
            Vector3 pa = points[(int)la], pb = points[(int)lb];
            pb -= normal * Vector3.Dot(pb - pa, normal);
            view.ShowNeutralOnly(pa, pb);
        }
    }

    private void LogSummary(string why)
    {
        var sb = new StringBuilder(256);
        sb.Append("[선실측] 요약(").Append(why).Append(") — ");
        for (int m = 0; m < 6; m++)
        {
            RomLineResult a = results[m * 2], p = results[m * 2 + 1];
            sb.Append(MotionName[m]).Append(' ')
              .Append(a.valid ? a.angle.ToString("F1") : "—").Append('/')
              .Append(p.valid ? p.angle.ToString("F1") : "—");
            if (m < 5) sb.Append(" · ");
        }
        sb.Append("  (능동/압박, 도)");
        Debug.Log(sb.ToString());
    }

    // ★막힐 때만 말한다(2초에 한 번). 잘 돌면 한 줄도 안 남긴다.
    private void Silent(string reason)
    {
        if (reason == lastSilent && Time.unscaledTime < nextSilentLog) return;
        lastSilent = reason;
        nextSilentLog = Time.unscaledTime + 2f;
        Debug.LogWarning("[선실측] 멈춰 있다 — " + reason);
    }

    // ── 패스스루·소리(기존 실측과 같은 방식) ─────────────────────────
    private void EnablePassthrough()
    {
        // 이 씬은 패스스루 전용이라 우리가 켜고 씬이 내려갈 때 같이 사라진다(다른 씬과 공유하는 상태가 아니다).
        var layer = FindFirstObjectByType<OVRPassthroughLayer>(FindObjectsInactive.Include);
        if (layer != null)
        {
            layer.enabled = true;
            layer.hidden = false;
        }
        if (OVRManager.instance != null) OVRManager.instance.isInsightPassthroughEnabled = true;

        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);   // 알파 0이어야 뒤의 패스스루가 보인다
        }
        Debug.Log($"[선실측] 패스스루 — 레이어 {(layer != null ? "O" : "★없음")} · OVRManager {(OVRManager.instance != null ? "O" : "★없음")} · 카메라 {(cam != null ? "O" : "★없음")}");
    }

    private void BuildSounds()
    {
        audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.playOnAwake = false;
        audioSrc.spatialBlend = 0f;
        sndMark = Tone("찍음", 880f, 1320f, 0.11f);
        sndUndo = Tone("취소", 700f, 440f, 0.09f);
        sndDeny = Tone("거부", 300f, 300f, 0.12f);
        sndPress = Tone("누름", 1250f, 1250f, 0.045f);
        sndDone = Tone("끝", 660f, 1760f, 0.25f);
    }

    private static AudioClip Tone(string name, float f0, float f1, float seconds)
    {
        const int rate = 44100;
        int n = Mathf.CeilToInt(rate * seconds);
        var data = new float[n];
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            phase += 2f * Mathf.PI * Mathf.Lerp(f0, f1, t) / rate;
            float env = Mathf.Min(1f, t * 40f) * (1f - t) * (1f - t);
            data[i] = Mathf.Sin(phase) * env * 0.6f;
        }
        var clip = AudioClip.Create("[선실측] " + name, n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private void Play(AudioClip c)
    {
        if (audioSrc != null && c != null && soundVolume > 0f) audioSrc.PlayOneShot(c, soundVolume);
    }
}
