using Oculus.Interaction.Input;
using System.Collections.Generic;
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
///   · 기준: 양쪽 Tr 두 점(건너뛸 수 있다) → 환자 기준 방향
///   · 동작마다 <b>직선 셋</b>: 중립 2점 → 능동 2점 → 압박 2점. 여섯 동작이라 직선은 18개다
///   · 값 둘을 나란히 낸다: «직접» = 3차원에서 두 선의 각을 그대로(기준 없이도 나온다) /
///     «면» = 환자 기준 면 위로 옮겨 잰 각(기준 Tr을 찍었을 때만 — ★회전은 예외다. 회전의 면은 바닥이라
///     중력만으로 정해지므로 기준을 건너뛰어도 «면» 값이 나온다, 10-07)
///   · 순서 고정: 기준 → 굴곡 → 신전 → 우측굴 → 좌측굴 → 우회전 → 좌회전(각각 중립 → 능동 → 압박)
///   ★10-01 첫 판은 «중립 네 점을 한 번 찍어 모든 동작의 중립선으로 돌려쓰는» 구조였다(28점). 사용자 지시로
///     «기준은 양쪽 Tr만 · 중립선은 동작마다 찍는다(18직선)»로 바꿨다(38점, 기준을 건너뛰면 36점).
///
/// 조작은 <b>컨트롤러 하나</b>다(주로 오른손, 바꿔 쥐면 그쪽이 그대로 동작). 반대 손은 환자를 잡는 데 쓴다.
///   트리거 = 점 찍기 · 아래 버튼(A/X) = 한 점 취소 · 위 버튼(B/Y) = 건너뛰기 · 그립 = 판 옮기기 · 스틱 좌우 = 앞뒤 단계
///   판의 버튼은 컨트롤러 끝을 버튼에 대고 트리거를 당긴다(«전부 다시»·«나가기»는 1초 길게).
///   ★레이로 멀리서 누르게 하지 않았다 — 환자 머리에 점을 찍는 순간 뒤에 있는 판이 눌리는 일을 막으려고.
///
/// ★임시로 정해 둔 것(사용자 답 대기, 10-01): 각이 작아 교점이 멀면 각도기를 중립선 첫 점에 세운다 ·
///   압박은 값만 더하고 각도기는 능동 때 자리에 둔다 · 면 밖으로 벗어난 양은 로그에만 남긴다.
///
/// ★비교 시험(10-02, <c>compareGlGn</c>): 굴곡·신전에서 한 자세마다 Gn을 하나 더 찍어(오른쪽 Tr → Gl → Gn)
///   Tr ~ Gl 선과 Gl ~ Gn 선의 값을 나란히 본다. Tr ~ Gl 선은 옆면에서 비스듬해 «직접»이 작게 나오고
///   Gl ~ Gn 선은 정중선 위라 그대로 나온다는 계산을 실제로 확인하려는 것이다. 끄면 종전 흐름(2점) 그대로다.
///
/// ★자유 측정(10-07, 판 맨 위의 «자유 측정»): 기준점도 정해진 순서도 없다. 두 점마다 선 하나, 개수 제한 없이 찍는다.
///   동작마다 갈래(탭)를 나눈다 — 굴곡·신전 / 측굴 / 회전. 갈래마다 점을 따로 모으고, 처음 찍은 선이 <b>기준선</b>이다.
///   측정선마다 길이, 기준선과 벌어진 각(«직접»), 그리고 <b>그 동작의 면 하나</b>에 내린 각(«면» — 옆면·앞면·바닥면)을 낸다.
///   면은 공간에 고정된 세 축(정면·좌우·수직)으로 세운다 — 양쪽 Tr을 찍지 않는다. 회전의 바닥면은 중력만 쓴다.
///   세 축은 판을 세울 때 헤드셋이 보던 방향으로 잡는다(환자가 사용자를 마주 본다고 놓는다) — «축 다시 잡기»로 다시 잡는다.
///   ★환자는 그 축에 정면만 맞춰 앉으면 된다(자리는 뜻이 없다). 어긋난 만큼 값이 달라진다.
///   ★첫 판(10-07 낮)은 한 선에서 세 면의 값을 함께 보여 줬다. 민두 시험에서 다른 두 면의 값이 실제 움직임이 아닌
///     «겉보기 값»(순수 회전인데 옆면 120~150°)으로 나와 갈래를 나눴다 — 다른 면의 값은 로그에만 남긴다.
/// </summary>
public class RomLineSession : MonoBehaviour
{
    public const int GroupCount = 19;          // 기준 1 + 6동작 × (중립·능동·압박)
    public const int PointCount = 38;          // 묶음마다 2점

    public static readonly string[] MotionName = { "굴곡", "신전", "우측굴", "좌측굴", "우회전", "좌회전" };
    public static readonly string[] PointName = { "오른쪽 Tr", "왼쪽 Tr", "Gl", "Gn" };
    private static readonly string[] PhaseName = { "중립", "능동", "압박" };

    [Header("=== 컨트롤러 ===")]
    [Tooltip("컨트롤러 기준 «찍는 점»의 자리(m, 오른손 기준 — 왼손은 x를 뒤집어 쓴다). 노란 점이 여기에 뜬다.\n" +
             "★0.033 = 컨트롤러 모형 경계 상자의 맨 앞(브리지 romline-ctrl로 잰 값, 10-01). 실제 컨트롤러 앞면과 어긋나면 인스펙터로 맞춘다.")]
    [SerializeField] private Vector3 tipOffset = new Vector3(0f, 0f, 0.033f);

    [Header("=== 비교 시험 ===")]
    [Tooltip("켜면 굴곡·신전에서 한 자세마다 세 점(오른쪽 Tr → Gl → Gn)을 찍어 Tr~Gl 선과 Gl~Gn 선의 값을 나란히 낸다.\n" +
             "Gl~Gn 값은 판의 결과표 아래 한 줄과 로그 «[선실측] 비교»에 나온다. 측굴·회전은 그대로다.")]
    [SerializeField] private bool compareGlGn = false;

    [Header("=== 손 ===")]
    [Tooltip("켜면 판의 버튼을 맨손 검지로도 누르고(닿고 머물기) 핀치로 판을 옮긴다. 컨트롤러 + 맨손 동시 추적을 요청한다.\n" +
             "끄면 컨트롤러만 쓴다(10-07 사용자 지시로 기본 끔). 점은 어느 쪽이든 컨트롤러로만 찍는다.")]
    [SerializeField] private bool handInput = false;

    [Header("=== 기타 ===")]
    [SerializeField] private float soundVolume = 1f;
    [SerializeField] private string lobbySceneName = "lobby";

    // 단계
    private readonly Vector3[] points = new Vector3[PointCount];
    private readonly bool[] has = new bool[PointCount];
    private readonly RomLineResult[] results = new RomLineResult[12];
    private readonly RomLineDirect[] direct = new RomLineDirect[12];   // 비교용 — 면으로 옮기지 않고 3차원에서 잰 값
    // 비교 시험 — 묶음마다 세 번째 점(Gn) 하나, 굴곡·신전의 Gl ~ Gn 선 값(동작 2 × 능동·압박)
    private readonly Vector3[] extra = new Vector3[GroupCount];
    private readonly bool[] hasExtra = new bool[GroupCount];
    private readonly RomLineResult[] cmpResults = new RomLineResult[4];
    private readonly RomLineDirect[] cmpDirect = new RomLineDirect[4];
    private RomLineFrame frame;

    // 자유 측정(10-07) — 두 점마다 선 하나, 개수 제한 없음
    private bool freeMode;
    private int freeTab;                // 0 굴곡·신전 · 1 측굴 · 2 회전 — 갈래마다 점을 따로 모은다
    private readonly List<Vector3>[] freePts = { new List<Vector3>(64), new List<Vector3>(64), new List<Vector3>(64) };
    // 선마다 «견준 기준선의 번호». 자기 번호와 같으면 그 선이 기준선이다(첫 선, 그리고 «기준선 바꾸기»로 삼은 선).
    private readonly List<int>[] freeRefOf = { new List<int>(32), new List<int>(32), new List<int>(32) };
    private readonly RomLineFreeView freeView = new RomLineFreeView();
    private bool axesOn = true;
    private RomLineFrame axes;          // 공간에 고정된 세 축 — 양쪽 Tr이 하던 일을 대신한다
    private Vector3 axesOrigin;         // 바닥의 십자를 그리는 자리(방향만 쓰고 자리는 계산에 안 들어간다)
    private const float FreeMaxOffPlane = 75f;   // 선이 그 면에서 이보다 많이 들려 있으면 그 면의 값은 비운다(그림자가 짧아 값이 튄다)
    private int group;      // 0 = 기준(양쪽 Tr), 1~18 = 동작 × (중립·능동·압박), 19 = 끝
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
    private int switchedFrame = -1;
    private Vector3 ctrlPos;
    private Quaternion ctrlRot = Quaternion.identity;
    private float hapticOff = -1f;

    // 손(10-06) — 판 조작만 한다. 점은 컨트롤러로만 찍는다(끝점이 정해져 있는 쪽).
    private readonly RomRecordHands hands = new RomRecordHands();
    private readonly bool[] fingerValid = new bool[2];      // 0 왼손 · 1 오른손 검지 끝
    private readonly Vector3[] fingerPos = new Vector3[2];
    private readonly bool[] pinching = new bool[2];
    private readonly bool[] pinchDown = new bool[2];
    private int grabHand = -1;           // 판을 잡은 손(0 · 1). 컨트롤러로 잡았으면 -1
    private bool simultaneousOn;         // 우리가 켠 «컨트롤러 + 맨손 동시 추적» — 켠 것만 끈다
    private int trackMask = -1;
    private float nextTrackLog;

    // 판 버튼 — 닿고 머물면 눌린다(컨트롤러·손 공통). source: 0 왼손 · 1 오른손 · 2 컨트롤러
    private int dwellButton = -1, dwellSource = -1;
    private float dwellStart;
    private bool dwellFired;

    // 표시
    private readonly RomLineView view = new RomLineView();
    private bool dirty = true;
    private string noteText = "";
    private float trackingWaitStart = -1f;
    private float nextSilentLog;
    private string lastSilent;

    private AudioSource audioSrc;
    private AudioClip sndMark, sndUndo, sndDeny, sndPress, sndDone;

    private const float DwellSeconds = 0.45f;      // 닿고 이만큼 머물면 눌린다
    private const float DangerDwellSeconds = 1.5f; // «전부 다시» · «나가기»는 길게
    private const float PinchClose = 0.02f, PinchOpen = 0.04f;   // 엄지–검지 끝 거리(m). 사이는 그대로 — 떨림으로 깜박이지 않게
    private static readonly string[] SourceName = { "왼손", "오른손", "컨트롤러" };
    private const float ArmThreshold = 0.15f;      // 트리거를 이만큼 당기기 시작한 순간의 자리를 찍는 자리로 쓴다
    private const float TrackingWaitSeconds = 8f;

    private bool EyeReady => eye != null && eye.position.y > 0.3f;
    private bool Done => group >= GroupCount;

    // ── 단계 ↔ 점 ───────────────────────────────────────────────────
    /// <summary>그 묶음에서 찍는 점 수. 비교 시험이 켜져 있으면 굴곡·신전(묶음 1~6)만 Gn을 더해 셋이다.</summary>
    private int SizeOf(int g) => compareGlGn && g >= 1 && g <= 6 ? 3 : 2;
    private static int GroupStart(int g) => g * 2;
    private static int MotionOf(int g) => (g - 1) / 3;
    private static int PhaseOf(int g) => (g - 1) % 3;          // 0 중립 · 1 능동 · 2 압박
    private static int GroupOf(int motion, int phase) => 1 + motion * 3 + phase;

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
        if (g == 0) return (RomLinePoint)i;   // 기준: 오른쪽 Tr → 왼쪽 Tr
        if (i >= 2) return RomLinePoint.Gn;   // 비교 시험의 세 번째 점
        LineOf((RomLineMotion)MotionOf(g), out RomLinePoint a, out RomLinePoint b);
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
        freeView.Build(view, transform, view.FreeArea, view.FreeTableTop);
        BuildSounds();

        // 한 손에 컨트롤러, 다른 손은 맨손 — 둘을 함께 추적하게 한다. 지원하지 않으면 false가 돌아오고 손은 컨트롤러를 내려놨을 때만 잡힌다.
        //   ★handInput이 꺼져 있으면(기본) 요청하지 않는다 — 컨트롤러만 쓴다.
        simultaneousOn = handInput && OVRInput.EnableSimultaneousHandsAndControllers();
        Debug.Log(handInput
            ? $"[선실측] 컨트롤러 + 맨손 동시 추적 요청 — {(simultaneousOn ? "받아들여짐" : "★거절됨(이 기기·연결에서는 컨트롤러를 쥔 동안 손이 안 잡힐 수 있다)")}"
            : "[선실측] 손 입력 끔 — 컨트롤러만 쓴다(인스펙터 «Hand Input»으로 켠다)");

        Debug.Log($"[선실측] 붙었다 — 리그 {(rig != null ? "있음" : "★없음")} · 추적공간 {(trackingSpace != null ? "있음" : "★없음(컨트롤러 자리를 못 잡는다)")} · " +
                  $"셰이더 {(sh != null ? "있음" : "★없음")} · 폰트 {(font != null ? font.name : "★없음")} · 끝점 오프셋 {tipOffset:F3} · " +
                  $"비교 시험(Gl~Gn) {(compareGlGn ? "켬 — 굴곡·신전은 세 점씩" : "끔")}");
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
            CaptureAxes("처음");
        }

        ReadController();
        ReadHands();
        HandleInput();

        if (dirty) Refresh();
        view.Tick(eye, tipValid, tipPos, freeMode || !Done, fingerValid, fingerPos);
        freeView.Tick(eye);
    }

    private void OnDestroy()
    {
        if (hapticOff > 0f) OVRInput.SetControllerVibration(0f, 0f, active);
        if (simultaneousOn) OVRInput.DisableSimultaneousHandsAndControllers();   // 우리가 켠 것만 되돌린다
    }

    // ── 손 ──────────────────────────────────────────────────────────
    private void ReadHands()
    {
        if (handInput) hands.Resolve();
        for (int h = 0; h < 2; h++)
        {
            if (!handInput) { fingerValid[h] = pinching[h] = pinchDown[h] = false; continue; }   // 손 입력 끔 — 컨트롤러만
            bool left = h == 0;
            pinchDown[h] = false;
            fingerValid[h] = hands.TryJoint(left, HandJointId.HandIndexTip, out fingerPos[h]) && !hands.SystemGesture(left);
            if (!fingerValid[h]) { pinching[h] = false; continue; }
            if (!hands.TryJoint(left, HandJointId.HandThumbTip, out Vector3 thumb)) continue;
            float gap = Vector3.Distance(thumb, fingerPos[h]);
            if (!pinching[h] && gap < PinchClose) pinching[h] = pinchDown[h] = true;
            else if (pinching[h] && gap > PinchOpen) pinching[h] = false;
        }

        // 무엇이 잡히고 있는지 — 바뀔 때만(1초에 한 번까지) 남긴다. «컨트롤러와 맨손이 함께 잡히는가»를 로그로 가른다.
        int mask = (tipValid ? 4 : 0) | (fingerValid[0] ? 1 : 0) | (fingerValid[1] ? 2 : 0);
        if (mask != trackMask && Time.unscaledTime >= nextTrackLog)
        {
            trackMask = mask;
            nextTrackLog = Time.unscaledTime + 1f;
            Debug.Log($"[선실측] 추적 — 컨트롤러 {(tipValid ? "O" : "X")} · 왼손 {(fingerValid[0] ? "O" : "X")} · 오른손 {(fingerValid[1] ? "O" : "X")}");
        }
    }

    /// <summary>판 옮기기 — 컨트롤러는 그립, 손은 판 가까이에서 엄지·검지를 오므린다. 옮기는 중이면 true.</summary>
    private bool UpdateGrab()
    {
        if (grabbing)
        {
            bool held = grabHand < 0 ? tipValid && OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, active)
                                     : fingerValid[grabHand] && pinching[grabHand];
            if (held) view.MovePanel((grabHand < 0 ? tipPos : fingerPos[grabHand]) + grabOffset, eye);
            else
            {
                grabbing = false;
                Debug.Log($"[선실측] 판을 옮겼다({(grabHand < 0 ? "컨트롤러" : SourceName[grabHand])}) — {view.Panel.position:F2} (눈에서 {Vector3.Distance(view.Panel.position, eye.position) * 100f:F0}cm)");
            }
            return true;
        }
        if (tipValid && OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, active) && view.NearPanel(tipPos, 0.12f))
        {
            grabbing = true; grabHand = -1;
            grabOffset = view.Panel.position - tipPos;
            return true;
        }
        for (int h = 0; h < 2; h++)
            if (pinchDown[h] && view.NearPanel(fingerPos[h], 0.06f))
            {
                grabbing = true; grabHand = h;
                grabOffset = view.Panel.position - fingerPos[h];
                return true;
            }
        return false;
    }

    private void Buzz(float seconds, float amplitude)
    {
        if (!tipValid) return;
        OVRInput.SetControllerVibration(1f, amplitude, active);
        hapticOff = Time.unscaledTime + seconds;
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
        ctrlPos = p;
        ctrlRot = r;
        Vector3 off = tipOffset;
        if (active == OVRInput.Controller.LTouch) off.x = -off.x;   // 왼손 컨트롤러는 좌우가 거울상이다
        tipPos = p + r * off;
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
        if (hapticOff > 0f && Time.unscaledTime >= hapticOff)
        {
            OVRInput.SetControllerVibration(0f, 0f, active);
            hapticOff = -1f;
        }
        if (Time.frameCount == switchedFrame) return;   // 컨트롤러를 바꾼 그 누름은 전환으로만 쓴다

        // 트리거를 당기기 시작한 순간의 자리를 잡아 둔다 — 끝까지 당기는 동안 컨트롤러가 흔들린다.
        float trigger = tipValid ? OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, active) : prevTrigger;
        if (prevTrigger < ArmThreshold && trigger >= ArmThreshold)
        {
            armedPos = tipPos;
            armedTime = Time.unscaledTime;
        }
        bool triggerDown = tipValid && prevTrigger < 0.55f && trigger >= 0.55f;
        prevTrigger = trigger;

        // ── 판 옮기기(컨트롤러 그립 · 손 핀치) ──
        if (UpdateGrab())
        {
            dwellButton = -1;
            view.SetHot(-1, 0f);
            return;
        }

        // ── 판 버튼 — 컨트롤러 끝이 먼저, 없으면 손끝. 닿고 머물면 눌린다(컨트롤러는 트리거로 바로 눌러도 된다) ──
        int button = -1, source = -1;
        bool controllerOnPanel = false;
        if (tipValid)
        {
            int b = view.Touch(tipPos, RomLineView.TouchDepth, out controllerOnPanel);
            if (b >= 0) { button = b; source = 2; }
        }
        for (int h = 0; h < 2 && button < 0; h++)
        {
            if (!fingerValid[h] || pinching[h]) continue;
            int b = view.Touch(fingerPos[h], RomLineView.FingerDepth, out _);
            if (b >= 0) { button = b; source = h; }
        }

        float progress = 0f;
        if (button < 0) dwellButton = -1;
        else
        {
            if (button != dwellButton || source != dwellSource)
            {
                dwellButton = button; dwellSource = source;
                dwellStart = Time.unscaledTime;
                dwellFired = false;
            }
            bool danger = button == 5 || button == 6 || button == 12;   // «전부 다시» · «나가기» · «전부 지우기»
            if (!dwellFired)
            {
                progress = (Time.unscaledTime - dwellStart) / (danger ? DangerDwellSeconds : DwellSeconds);
                if (progress >= 1f || (source == 2 && triggerDown && !danger))
                {
                    dwellFired = true;   // 한 번 닿아 한 번만 — 떼었다 다시 대야 또 눌린다
                    progress = 0f;
                    Debug.Log($"[선실측] 버튼 — «{RomLineView.ButtonLabel[button]}» ({SourceName[source]})");
                    if (source == 2) Buzz(0.04f, 0.4f);
                    Press(button);
                }
            }
        }
        view.SetHot(button, progress);
        if (controllerOnPanel) return;   // ★판 위에서는 점을 찍지 않는다
        if (!tipValid) return;           // 아래는 컨트롤러로만 한다

        // ── 버튼·스틱 ──
        if (OVRInput.GetDown(OVRInput.Button.One, active)) { Undo(); return; }
        if (!freeMode && OVRInput.GetDown(OVRInput.Button.Two, active)) { Skip(); return; }   // 자유 측정에는 단계가 없다

        float sx = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, active).x;
        if (Mathf.Abs(sx) < 0.3f) stickReady = true;
        else if (!freeMode && stickReady && Mathf.Abs(sx) > 0.7f)
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
        if (freeMode) { FreeMark(pos, shake); return; }
        if (Done) { Deny("측정이 끝났다 — 다시 재려면 탭이나 스틱으로 그 단계로 간다"); return; }

        int start = GroupStart(group);
        if (index == 0)
        {
            // 이미 찍은 묶음에 다시 들어와 첫 점을 찍으면 그 묶음을 새로 잰다.
            has[start] = has[start + 1] = false;
            hasExtra[group] = false;
            Invalidate(group);
        }

        RomLinePoint which = ExpectedPoint(group, index);
        if (index < 2) { points[start + index] = pos; has[start + index] = true; }
        else { extra[group] = pos; hasExtra[group] = true; }
        Debug.Log($"[선실측] 찍음 — {GroupTitle(group)} · {PointName[(int)which]} ({index + 1}/{SizeOf(group)}) · 자리 {pos:F3} · " +
                  $"당기는 동안 끝점 이동 {shake * 1000f:F1}mm");
        index++;
        noteText = "";
        Play(sndMark);
        Buzz(0.06f, 0.6f);

        if (index >= SizeOf(group)) CompleteGroup();
        dirty = true;
    }

    private void CompleteGroup()
    {
        int start = GroupStart(group);
        if (Vector3.Distance(points[start], points[start + 1]) < 0.005f)
        {
            has[start] = has[start + 1] = false;
            index = 0;
            Deny("두 점이 같은 자리에 찍혔다 — 다시 찍는다");
            return;
        }
        if (hasExtra[group] && Vector3.Distance(points[start + 1], extra[group]) < 0.005f)
        {
            hasExtra[group] = false;
            index = 2;
            Deny("Gl과 Gn이 같은 자리에 찍혔다 — Gn을 다시 찍는다");
            return;
        }

        if (group == 0)
        {
            frame = RomLineMath.BuildFrame(points[0], points[1]);
            if (!frame.valid)
            {
                has[0] = has[1] = false;
                index = 0;
                Deny("양쪽 Tr이 위아래로 겹쳐 찍혔다 — 기준을 다시 찍는다");
                return;
            }
            Debug.Log($"[선실측] 기준 방향 — 오른쪽 {frame.right:F2} · 앞뒤 {frame.forward:F2} · 위 {frame.up:F2} · " +
                      $"Tr 간격 {Vector3.Distance(points[0], points[1]) * 100f:F1}cm");
            for (int m = 0; m < 6; m++) { FaceCheck(m); RecomputeMotion(m); }   // 기준만 다시 찍었을 때 남아 있는 측정을 다시 잰다
        }
        else
        {
            int m = MotionOf(group), ph = PhaseOf(group);
            if (ph == 0)
            {
                Debug.Log($"[선실측] 중립선 — {MotionName[m]} · 길이 {Vector3.Distance(points[start], points[start + 1]) * 100f:F1}cm");
                FaceCheck(m);
                RecomputeMotion(m);
            }
            else Compute(m, ph);
        }

        group++;
        index = 0;
        if (Done)
        {
            Play(sndDone);
            LogSummary("끝");
        }
    }

    /// <summary>
    /// 그 동작의 중립선에 든 Gl로 앞뒤를 확인한다. Gl이 뒤쪽에 있으면 양쪽 Tr을 바꿔 찍은 것이라 좌우를 바로잡는다.
    /// ★각의 크기는 안 바뀐다 — 부호(방향이 맞는가 판정)만 바뀐다.
    /// </summary>
    private void FaceCheck(int m)
    {
        if (!frame.valid) return;
        int n0 = GroupStart(GroupOf(m, 0));
        if (!has[n0] || !has[n0 + 1]) return;
        LineOf((RomLineMotion)m, out RomLinePoint a, out RomLinePoint b);
        int gi = a == RomLinePoint.Gl ? 0 : b == RomLinePoint.Gl ? 1 : -1;
        if (gi < 0) return;
        if (!RomLineMath.FaceForward(ref frame, (points[0] + points[1]) * 0.5f, points[n0 + gi])) return;

        (points[0], points[1]) = (points[1], points[0]);
        noteText = "양쪽 Tr이 바뀌어 찍혀 바로잡았다";
        Debug.Log("[선실측] ★양쪽 Tr을 바꿔 찍은 것으로 보인다(Gl이 뒤쪽에 있었다) — 좌우를 바로잡았다.");
        for (int k = 0; k < 6; k++) RecomputeMotion(k);
    }

    private void RecomputeMotion(int m)
    {
        Compute(m, 1);
        Compute(m, 2);
    }

    /// <summary>그 동작의 중립선과 측정선(ph 1 능동 · 2 압박)이 다 있으면 각을 낸다.</summary>
    private void Compute(int m, int ph)
    {
        int slot = m * 2 + ph - 1;
        ClearResult(slot);
        int n0 = GroupStart(GroupOf(m, 0)), s0 = GroupStart(GroupOf(m, ph));
        if (!has[n0] || !has[n0 + 1] || !has[s0] || !has[s0 + 1]) return;

        var motion = (RomLineMotion)m;
        string title = MotionName[m] + " · " + PhaseName[ph];
        LogCoordinates(m, ph, n0, s0, title);
        if (compareGlGn && m < 2) ComputeCompare(m, ph, n0, s0, title);

        // 직접 — 기준 없이 3차원에서 그대로.
        RomLineDirect dr = RomLineMath.MeasureDirect(points[n0], points[n0 + 1], points[s0], points[s0 + 1]);
        direct[slot] = dr;
        if (!dr.valid) { Debug.LogWarning($"[선실측] ★{title} — 각을 못 냈다(같은 자리를 두 번 찍었다)."); return; }

        // 면 — 기준(양쪽 Tr)이 있을 때만. 회전은 바닥면이라 기준 없이도 낸다.
        bool byGravity = !frame.valid && !RomLineMath.NeedsReference(motion);
        if (!frame.valid && !byGravity)
        {
            Debug.Log($"[선실측] 값 — {title} 직접(3차원) {dr.angle:F1}° · 두 선 엇갈림 {dr.gap * 100f:F1}cm · 면 위 —(기준 Tr을 안 찍었다)");
            return;
        }
        RomLineResult r = RomLineMath.Measure(points[n0], points[n0 + 1], points[s0], points[s0 + 1], RomLineMath.PlaneNormal(motion, frame));
        results[slot] = r;
        if (!r.valid)
        {
            Debug.LogWarning($"[선실측] ★{title} — 면 위 각을 못 냈다(선이 면에 수직이다). 직접 {dr.angle:F1}°");
            return;
        }

        bool match = RomLineMath.DirectionMatches(motion, r.signedAngle);
        Debug.Log($"[선실측] 값 — {title} 면 위 {r.angle:F1}°{(byGravity ? "(기준 없이 · 바닥면)" : "")} (부호 {r.signedAngle:+0.0;-0.0}{(match ? "" : " ★반대 방향")}) · " +
                  $"직접(3차원) {dr.angle:F1}° · 두 선 엇갈림 {dr.gap * 100f:F1}cm · " +
                  $"면 밖 각 중립선 {r.neutralOffPlane:F1}° → 측정선 {r.markOffPlane:F1}° (변화 {r.markOffPlane - r.neutralOffPlane:+0.0;-0.0}°) · " +
                  $"교점 {(r.pivotOnLines ? $"선 위(중립 첫 점에서 {Vector3.Distance(r.pivot, r.neutralA) * 100f:F1}cm)" : "멀거나 평행 — 첫 점에 세움")}");
        if (!match && GroupOf(m, ph) == group)
        {
            noteText = $"{MotionName[m]} 단계인데 반대쪽으로 움직인 값이다";
            Play(sndDeny);
        }
    }

    /// <summary>
    /// 값을 낸 점들의 좌표를 한 줄에 모아 남긴다(월드, m, 0.1mm 자리까지) — 나중에 로그만으로 다시 계산해 볼 수 있게.
    /// 찍을 때마다 남기는 «찍음» 줄과 달리, 다시 찍거나 취소한 뒤 <b>실제로 값에 쓰인</b> 점만 들어간다.
    /// </summary>
    private void LogCoordinates(int m, int ph, int n0, int s0, string title)
    {
        LineOf((RomLineMotion)m, out RomLinePoint a, out RomLinePoint b);
        int gN = GroupOf(m, 0), gS = GroupOf(m, ph);
        var sb = new StringBuilder(260);
        sb.Append("[선실측] 좌표 — ").Append(title)
          .Append(" · 중립 ").Append(PointName[(int)a]).Append(' ').Append(points[n0].ToString("F4"))
          .Append(' ').Append(PointName[(int)b]).Append(' ').Append(points[n0 + 1].ToString("F4"));
        if (hasExtra[gN]) sb.Append(" Gn ").Append(extra[gN].ToString("F4"));
        sb.Append(" → ").Append(PhaseName[ph]).Append(' ').Append(PointName[(int)a]).Append(' ').Append(points[s0].ToString("F4"))
          .Append(' ').Append(PointName[(int)b]).Append(' ').Append(points[s0 + 1].ToString("F4"));
        if (hasExtra[gS]) sb.Append(" Gn ").Append(extra[gS].ToString("F4"));
        if (has[0] && has[1])
            sb.Append(" · 기준 오른쪽 Tr ").Append(points[0].ToString("F4")).Append(" 왼쪽 Tr ").Append(points[1].ToString("F4"));
        Debug.Log(sb.ToString());
    }

    /// <summary>
    /// 비교 시험 — 같은 자세에서 찍은 Gl ~ Gn 선으로 굴곡·신전을 한 번 더 잰다(계산은 Tr ~ Gl 선과 같은 함수).
    /// ★부호는 보지 않는다 — 선이 달라 +가 뜻하는 방향을 검증기로 확인한 적이 없다. 크기만 쓴다.
    /// </summary>
    private void ComputeCompare(int m, int ph, int n0, int s0, string title)
    {
        int gN = GroupOf(m, 0), gS = GroupOf(m, ph);
        if (!hasExtra[gN] || !hasExtra[gS])
        {
            Debug.Log($"[선실측] 비교 — {title} Gl~Gn 값 없음(Gn을 {(hasExtra[gN] ? "측정" : "중립")} 자세에서 안 찍었다)");
            return;
        }
        int slot = m * 2 + ph - 1;
        RomLineDirect cd = RomLineMath.MeasureDirect(points[n0 + 1], extra[gN], points[s0 + 1], extra[gS]);
        cmpDirect[slot] = cd;
        if (!cd.valid) { Debug.LogWarning($"[선실측] ★비교 — {title} Gl~Gn 각을 못 냈다."); return; }
        RomLineResult cr = default;
        if (frame.valid)
        {
            cr = RomLineMath.Measure(points[n0 + 1], extra[gN], points[s0 + 1], extra[gS], RomLineMath.PlaneNormal((RomLineMotion)m, frame));
            cmpResults[slot] = cr;
        }
        RomLineDirect td = RomLineMath.MeasureDirect(points[n0], points[n0 + 1], points[s0], points[s0 + 1]);
        Debug.Log($"[선실측] 비교 — {title} · Tr~Gl 직접 {td.angle:F1}° · Gl~Gn 직접 {cd.angle:F1}° (엇갈림 {cd.gap * 100f:F1}cm) · " +
                  $"Gl~Gn 면 위 {(cr.valid ? cr.angle.ToString("F1") + "°" : "—(기준 Tr 없음)")} · " +
                  $"선 길이 Gl~Gn 중립 {Vector3.Distance(points[n0 + 1], extra[gN]) * 100f:F1}cm → 측정 {Vector3.Distance(points[s0 + 1], extra[gS]) * 100f:F1}cm · " +
                  $"Tr~Gl 중립 {Vector3.Distance(points[n0], points[n0 + 1]) * 100f:F1}cm → 측정 {Vector3.Distance(points[s0], points[s0 + 1]) * 100f:F1}cm");
    }

    /// <summary>그 묶음의 점이 바뀌면 거기에 기대던 값을 지운다.</summary>
    private void Invalidate(int g)
    {
        if (g == 0)
        {
            frame = default;
            for (int i = 0; i < results.Length; i++)   // 면 값만 — 직접 값은 기준과 무관하다. 회전의 면 값도 기준과 무관해 남긴다
                if (RomLineMath.NeedsReference((RomLineMotion)(i / 2))) results[i] = default;
            for (int i = 0; i < cmpResults.Length; i++) cmpResults[i] = default;
            return;
        }
        if (g >= GroupCount) return;
        int m = MotionOf(g), ph = PhaseOf(g);
        if (ph == 0) { ClearResult(m * 2); ClearResult(m * 2 + 1); }
        else ClearResult(m * 2 + ph - 1);
    }

    private void Undo()
    {
        if (freeMode) { FreeUndo(); return; }
        if (index > 0)
        {
            index--;
            if (index >= 2) hasExtra[group] = false;
            else has[GroupStart(group) + index] = false;
        }
        else if (group > 0)
        {
            group = Mathf.Min(group, GroupCount) - 1;
            int start = GroupStart(group);
            Invalidate(group);
            if (hasExtra[group])
            {
                hasExtra[group] = false;      // 세 점 묶음 — 마지막(Gn)만 지운다
                index = 2;
            }
            else
            {
                has[start + 1] = false;
                index = has[start] ? 1 : 0;   // 건너뛴 묶음이면 첫 점부터 찍게 한다
            }
        }
        else { Deny("지울 점이 없다"); return; }

        noteText = "";
        Debug.Log($"[선실측] 취소 → {GroupTitle(group)} {index + 1}번째 점부터");
        Play(sndUndo);
        dirty = true;
    }

    private void Skip()
    {
        if (Done) return;
        if (group == 0) Debug.Log("[선실측] 기준(양쪽 Tr)을 건너뛴다 — 굴곡·신전·측굴은 «직접» 값만 나온다. 회전은 바닥면으로 «면» 값이 나온다.");
        Go(group + 1, "건너뜀");
    }

    /// <summary>단계를 옮긴다(찍어 둔 점은 그대로 둔다 — 그 단계에서 첫 점을 찍는 순간 새로 잰다).</summary>
    private void Go(int target, string why)
    {
        target = Mathf.Clamp(target, 0, GroupCount);
        if (target == group && index == 0) return;
        if (index > 0)   // 찍다 만 묶음은 비운다
        {
            has[GroupStart(group)] = has[GroupStart(group) + 1] = false;
            hasExtra[group] = false;
        }
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
            case 1: Go(GroupOf(0, 0), "탭"); break;   // 굴곡·신전
            case 2: Go(GroupOf(2, 0), "탭"); break;   // 측굴
            case 3: Go(GroupOf(4, 0), "탭"); break;   // 회전
            case 4: RedoMotion(); break;
            case 5: ResetAll(); break;
            case 6: Exit(); break;
            case 7: Go(group - 1, "버튼"); break;
            case 8: Undo(); break;
            case 9: Skip(); break;
            case 10: SetFreeMode(false); break;
            case 11: SetFreeMode(true); break;
            case 12: FreeClear(); break;
            case 13:
                axesOn = !axesOn;
                Debug.Log($"[선실측] 자유 — 3축 표시 {(axesOn ? "켬" : "끔(세 면의 값은 안 나온다)")}");
                Play(sndPress);
                dirty = true;
                break;
            case 14: CaptureAxes("버튼"); Play(sndPress); dirty = true; break;
            case 15: case 16: case 17: SetFreeTab(button - 15); break;
            case 18: FreeRebase(); break;
        }
    }

    // ── 자유 측정(10-07) ────────────────────────────────────────────
    private static readonly string[] FreeTabName = { "굴곡·신전", "측굴", "회전" };
    private static readonly string[] FreePlaneName = { "옆면", "앞면", "바닥면" };
    // 면마다 부호가 +일 때 / −일 때의 이름(RomLineMath.PositiveMeans: 굴곡 · 좌측굴 · 우회전이 +)
    private static readonly string[] FreePlus = { "굴곡", "좌측굴", "우회전" };
    private static readonly string[] FreeMinus = { "신전", "우측굴", "좌회전" };
    private const float FreeReversedAngle = 150f;   // 기준선과 이보다 크게 벌어지면 두 점의 순서가 반대일 수 있다고 알린다
    private const float FreeJudgeMinAngle = 10f;    // 이보다 작게 움직인 선은 «어느 동작처럼 움직였나»를 가르지 않는다(찍는 오차가 더 크다)

    private List<Vector3> FreePts => freePts[freeTab];
    private List<int> FreeRefs => freeRefOf[freeTab];
    /// <summary>회전의 면(바닥)은 중력만 쓰므로 3축을 꺼도 나온다. 굴곡·신전과 측굴의 면은 3축이 있어야 한다.</summary>
    private bool FreePlaneOn => freeTab == 2 || axesOn;

    private void SetFreeMode(bool on)
    {
        if (freeMode == on) return;
        freeMode = on;
        noteText = "";
        view.SetMode(on);
        freeView.SetVisible(on);
        if (on)
        {
            // 6동작의 공간 표시는 끈다(찍어 둔 값은 그대로 — 돌아오면 다시 그린다).
            for (int i = 0; i < PointCount; i++) view.SetPoint(i, false, points[i]);
            for (int ph = 0; ph < 3; ph++) view.SetExtraPoint(ph, false, Vector3.zero);
            view.HideMeasure();
            view.HideCompare();
        }
        Debug.Log($"[선실측] 모드 → {(on ? $"자유 측정 · {FreeTabName[freeTab]}(찍어 둔 점 {freePts[0].Count}/{freePts[1].Count}/{freePts[2].Count}개 · 3축 표시 {(axesOn ? "켬" : "끔")})" : "6동작")}");
        Play(sndPress);
        dirty = true;
    }

    private void SetFreeTab(int tab)
    {
        if (tab == freeTab) return;
        freeTab = tab;
        noteText = "";
        Debug.Log($"[선실측] 자유 — 동작 → {FreeTabName[tab]}(찍어 둔 점 {FreePts.Count}개)");
        Play(sndPress);
        dirty = true;
    }

    /// <summary>
    /// 고정 3축을 지금 헤드셋이 보는 방향으로 잡는다. 환자는 사용자(헤드셋)를 마주 본다고 놓는다 — 정면 = 헤드셋 쪽.
    /// ★헤드셋을 쓰지 않고 정해 둔 자리에 놓아 기준으로 삼을 때도 같은 함수다(그 자리에서 «축 다시 잡기»).
    /// </summary>
    private void CaptureAxes(string why)
    {
        // 지움: 헤드셋의 숙임·기울임 → 세 축은 늘 수평·수직이다(위아래는 중력이 맡는다).
        Vector3 look = Vector3.ProjectOnPlane(eye.forward, Vector3.up);
        if (look.sqrMagnitude < 0.01f) look = Vector3.forward;   // 바로 위·아래를 보고 있었다
        look.Normalize();
        axes = RomLineMath.FrameFacing(-look);   // 검증기가 «양쪽 Tr로 만든 기준»과 같은 방향·부호인지 본다
        axesOrigin = eye.position + look * 1.0f;
        axesOrigin.y = trackingSpace != null ? trackingSpace.position.y : 0f;   // 바닥(추적 원점이 바닥 높이일 때)
        Debug.Log($"[선실측] 자유 — 3축을 잡았다({why}) · 환자 정면 {axes.forward:F3} · 환자 오른쪽 {axes.right:F3} · 십자 자리 {axesOrigin:F2} · 눈 {eye.position:F2}");
    }

    /// <summary>지금 갈래에서 새 선이 견줄 기준선의 번호(가장 최근에 기준선으로 삼은 선). 아직 없으면 -1.</summary>
    private int FreeCurrentRef()
    {
        List<int> refs = FreeRefs;
        for (int i = refs.Count - 1; i >= 0; i--)
            if (refs[i] == i) return i;
        return -1;
    }

    private void FreeMark(Vector3 pos, float shake)
    {
        List<Vector3> pts = FreePts;
        pts.Add(pos);
        int n = pts.Count, line = (n + 1) / 2;
        Debug.Log($"[선실측] 자유({FreeTabName[freeTab]}) — 찍음 · 선 {line}의 {(n % 2 == 1 ? "첫" : "둘째")} 점 · 자리 {pos:F4} · 당기는 동안 끝점 이동 {shake * 1000f:F1}mm");
        if (n % 2 == 0)
        {
            if (Vector3.Distance(pts[n - 2], pts[n - 1]) < 0.005f)
            {
                pts.RemoveAt(n - 1);
                Deny("두 점이 같은 자리에 찍혔다 — 둘째 점을 다시 찍는다");
                return;
            }
            int idx = line - 1, rf = FreeCurrentRef();
            FreeRefs.Add(rf < 0 ? idx : rf);   // 첫 선은 스스로 기준선이다
            LogFreeLine(idx);
        }
        noteText = "";
        Play(sndMark);
        Buzz(0.06f, 0.6f);
        dirty = true;
    }

    private void FreeUndo()
    {
        List<Vector3> pts = FreePts;
        if (pts.Count == 0) { Deny("지울 점이 없다"); return; }
        if (pts.Count % 2 == 0) FreeRefs.RemoveAt(FreeRefs.Count - 1);   // 마지막 선이 깨진다
        pts.RemoveAt(pts.Count - 1);
        noteText = "";
        Debug.Log($"[선실측] 자유({FreeTabName[freeTab]}) — 한 점 취소 → 남은 점 {pts.Count}개");
        Play(sndUndo);
        dirty = true;
    }

    private void FreeClear()
    {
        Debug.Log($"[선실측] 자유({FreeTabName[freeTab]}) — 이 동작을 지웠다(점 {FreePts.Count}개)");
        FreePts.Clear();
        FreeRefs.Clear();
        noteText = "";
        Play(sndUndo);
        dirty = true;
    }

    /// <summary>방금 찍은 선을 새 기준선으로 삼는다 — 그 뒤에 찍는 선은 이 선과 견준다(앞서 찍은 선의 값은 그대로 둔다).</summary>
    private void FreeRebase()
    {
        int last = FreeRefs.Count - 1;
        if (last < 0) { Deny("기준선으로 삼을 선이 없다 — 선을 먼저 찍는다"); return; }
        if (FreeRefs[last] == last) { Deny($"선 {last + 1}은 이미 기준선이다"); return; }
        FreeRefs[last] = last;
        noteText = $"선 {last + 1}을 기준선으로 삼았다";
        Debug.Log($"[선실측] 자유({FreeTabName[freeTab]}) — 기준선 → 선 {last + 1}(길이 {FreeLength(last) * 100f:F1}cm)");
        Play(sndPress);
        dirty = true;
    }

    private float FreeLength(int line) => Vector3.Distance(FreePts[line * 2], FreePts[line * 2 + 1]);
    private Vector3 FreeDir(int line) => FreePts[line * 2 + 1] - FreePts[line * 2];

    /// <summary>선 line이 선 from에서 벌어진 각 — 3차원 그대로(«직접»).</summary>
    private RomLineDirect FreeDirect(int from, int line)
    {
        List<Vector3> p = FreePts;
        return RomLineMath.MeasureDirect(p[from * 2], p[from * 2 + 1], p[line * 2], p[line * 2 + 1]);
    }

    /// <summary>
    /// 선 line이 선 from에서 돌아간 각을 한 면에 내려 읽는다. plane 0 옆면(굴곡·신전) · 1 앞면(측굴) · 2 바닥면(회전).
    /// 선이 그 면에서 너무 들려 있으면(<see cref="FreeMaxOffPlane"/>) false — 그림자가 짧아 값이 튄다.
    /// ★6동작과 같은 함수(<see cref="RomLineMath.Measure"/> · <see cref="RomLineMath.PlaneNormal"/>)를 탄다.
    /// </summary>
    private bool FreePlane(int from, int line, int plane, out RomLineResult r)
    {
        List<Vector3> p = FreePts;
        r = RomLineMath.Measure(p[from * 2], p[from * 2 + 1], p[line * 2], p[line * 2 + 1], RomLineMath.PlaneNormal((RomLineMotion)(plane * 2), axes));
        return r.valid && r.neutralOffPlane <= FreeMaxOffPlane && r.markOffPlane <= FreeMaxOffPlane;
    }

    private static string FreeNamed(int plane, RomLineResult r)
    {
        return (r.signedAngle >= 0f ? FreePlus[plane] : FreeMinus[plane]) + " " + r.angle.ToString("F1") + "°";
    }

    /// <summary>
    /// 그 선에 붙일 알림. 두 점의 순서가 반대로 보이거나, 고른 동작과 다른 동작처럼 움직였을 때만 글자가 있다(없으면 빈 글자).
    /// ★알리기만 한다 — 값을 고치거나 찍기를 막지 않는다(회전은 실제로 90°를 넘고, 동작이 섞이는 것도 실제 일이다).
    /// </summary>
    private string FreeWarning(int from, int line, float directAngle, out int judged, out float first, out float second)
    {
        judged = RomLineMath.DominantPlane(FreeDir(from), FreeDir(line), axes, out first, out second);
        if (directAngle > FreeReversedAngle) return "순서 반대?";
        if (!axesOn || judged < 0 || judged == freeTab || directAngle < FreeJudgeMinAngle) return "";   // 3축을 껐으면 맞춰 놓지 않은 것이라 가르지 않는다
        return FreeTabName[judged] + "으로 보임";
    }

    private void LogFreeLine(int line)
    {
        int rf = FreeRefs[line];
        var sb = new StringBuilder(320);
        sb.Append("[선실측] 자유(").Append(FreeTabName[freeTab]).Append(") — 선 ").Append(line + 1)
          .Append(" · 길이 ").Append((FreeLength(line) * 100f).ToString("F1")).Append("cm");
        if (rf == line) { sb.Append(" · 기준선"); Debug.Log(sb.ToString()); return; }

        RomLineDirect d = FreeDirect(rf, line);
        sb.Append(" ‖ 기준선(선 ").Append(rf + 1).Append(")과 직접 ").Append(d.angle.ToString("F1")).Append("°(엇갈림 ").Append((d.gap * 100f).ToString("F1")).Append("cm)");
        // 그 동작의 면이 먼저, 나머지 두 면은 «다른 면»으로 — 화면에는 그 동작의 면만 나온다. 로그에는 나중에 견줘 볼 수 있게 셋 다 남긴다.
        for (int k = 0; k < 3; k++)
        {
            int p = (freeTab + k) % 3;
            bool ok = FreePlane(rf, line, p, out RomLineResult r);
            sb.Append(k == 0 ? " · " : k == 1 ? " ‖ 다른 면 " : " · ").Append(FreePlaneName[p]).Append(' ').Append(ok ? FreeNamed(p, r) : "—")
              .Append("(면 밖 ").Append(r.neutralOffPlane.ToString("F0")).Append("→").Append(r.markOffPlane.ToString("F0")).Append("°)");
        }
        string warn = FreeWarning(rf, line, d.angle, out int judged, out float first, out float second);
        sb.Append(" ‖ 움직인 모양 ").Append(judged < 0 ? "가를 수 없음" : FreeTabName[judged]).Append('(').Append(first.ToString("F2")).Append(" / ").Append(second.ToString("F2")).Append(')');
        if (warn.Length > 0) sb.Append(" ★").Append(warn);
        if (!axesOn) sb.Append(" ‖ 3축 끔");
        Debug.Log(sb.ToString());
    }

    private void RefreshFree()
    {
        dirty = false;
        List<Vector3> pts = FreePts;
        List<int> refs = FreeRefs;
        int lines = pts.Count / 2;
        bool half = pts.Count % 2 == 1;

        string note = noteText;
        if (note.Length == 0)
            note = freeTab == 2 ? "바닥면으로 읽습니다 · 두 점은 늘 같은 순서로"
                 : axesOn ? "환자 정면을 바닥 화살표에 맞추세요 · 두 점은 늘 같은 순서로"
                 : "3축이 꺼져 있어 직접 각만 나옵니다 · 두 점은 늘 같은 순서로";
        view.SetTexts("자유 측정 · " + FreeTabName[freeTab], $"선 {lines + 1}의 {(half ? "둘째" : "첫")} 점을 찍으세요", note, -1, "선 " + (lines + 1));
        view.SetButtonOn(13, axesOn);
        view.SetFreeTab(freeTab);

        freeView.SetPoints(pts, refs);
        freeView.SetTab(freeTab, FreePlaneOn, FreePlaneName[freeTab] + " 각");
        freeView.ShowAxes(axesOn, axesOrigin, axes);

        // 판 — 가장 최근 선들. 한 줄에 선 하나: 길이 · 그 동작의 면 각 · 직접 각 · 알림.
        int firstRow = Mathf.Max(0, lines - RomLineFreeView.Rows);
        string label = "";
        Vector3 at = Vector3.zero;
        for (int row = 0; row < RomLineFreeView.Rows; row++)
        {
            int line = firstRow + row;
            if (line >= lines) { freeView.SetRow(row, "", "", "", "", false); continue; }
            string name = $"선 {line + 1}  {FreeLength(line) * 100f:F1}cm";
            int rf = refs[line];
            if (rf == line) { freeView.SetRow(row, name, "기준선", "", "", true); continue; }

            RomLineDirect d = FreeDirect(rf, line);
            string plane = !FreePlaneOn ? "" : FreePlane(rf, line, freeTab, out RomLineResult r) ? FreeNamed(freeTab, r) : "—";
            string warn = FreeWarning(rf, line, d.angle, out _, out _, out _);
            freeView.SetRow(row, name, plane, d.angle.ToString("F1") + "°", warn, false);
            if (line == lines - 1)
            {
                // 환자 위 — 가장 최근 측정선 곁에 값을 띄운다(면 각이 있으면 그것, 없으면 직접 각).
                label = plane.Length > 1 ? plane : "직접 " + d.angle.ToString("F1") + "°";
                at = (pts[line * 2] + pts[line * 2 + 1]) * 0.5f + Vector3.up * 0.04f;
            }
        }
        freeView.SetAngleLabel(label, at);
    }


    private void RedoMotion()
    {
        if (group == 0)
        {
            has[0] = has[1] = false;
            Invalidate(0);
            index = 0;
            Debug.Log("[선실측] 이 동작 다시 — 기준");
        }
        else
        {
            int m = MotionOf(Mathf.Min(group, GroupCount - 1));
            for (int ph = 0; ph < 3; ph++)
            {
                int st = GroupStart(GroupOf(m, ph));
                has[st] = has[st + 1] = false;
                hasExtra[GroupOf(m, ph)] = false;
            }
            ClearResult(m * 2);
            ClearResult(m * 2 + 1);
            group = GroupOf(m, 0);
            index = 0;
            Debug.Log($"[선실측] 이 동작 다시 — {MotionName[m]}");
        }
        noteText = "";
        Play(sndUndo);
        dirty = true;
    }

    private void ResetAll()
    {
        for (int i = 0; i < PointCount; i++) has[i] = false;
        for (int i = 0; i < GroupCount; i++) hasExtra[i] = false;
        frame = default;
        for (int i = 0; i < 12; i++) ClearResult(i);
        group = 0;
        index = 0;
        noteText = "";
        Debug.Log("[선실측] 전부 다시 — 전부 지웠다");
        Play(sndUndo);
        dirty = true;
    }

    private void ClearResult(int i)
    {
        results[i] = default;
        direct[i] = default;
        if (i < cmpResults.Length) { cmpResults[i] = default; cmpDirect[i] = default; }
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
        if (g <= 0) return "기준";
        if (g >= GroupCount) return "측정 끝";
        return MotionName[MotionOf(g)] + " · " + PhaseName[PhaseOf(g)];
    }

    private void Refresh()
    {
        if (freeMode) { RefreshFree(); return; }
        dirty = false;

        int tab = group == 0 ? 0 : group <= 6 ? 1 : group <= 12 ? 2 : 3;
        string instruction, tipText;
        if (Done)
        {
            instruction = "모든 방향을 쟀습니다";
            tipText = "";
        }
        else
        {
            RomLinePoint p = ExpectedPoint(group, index);
            instruction = $"{PointName[(int)p]} 을 찍으세요  ({index + 1}/{SizeOf(group)})";
            tipText = PointName[(int)p];
        }
        string note = noteText;
        if (note.Length == 0 && group == 0) note = "건너뛰면(B/Y) 회전만 «면» 값, 나머지는 «직접» 값만 나옵니다";
        view.SetTexts(GroupTitle(group), instruction, note, tab, tipText);

        int current = group >= 1 && !Done && PhaseOf(group) > 0 ? MotionOf(group) * 2 + PhaseOf(group) - 1 : -1;
        for (int i = 0; i < 12; i++)
            view.SetValue(i, results[i].valid ? results[i].angle.ToString("F1") + "°" : "—",
                          direct[i].valid ? direct[i].angle.ToString("F1") + "°" : "—", i == current);

        // 점 — 기준 둘은 늘, 그 밖은 지금 동작 것만.
        int motion = group == 0 ? -1 : MotionOf(Mathf.Min(group, GroupCount - 1));
        for (int i = 0; i < PointCount; i++)
        {
            bool show = has[i] && (i < 2 || (i - 2) / 6 == motion);
            view.SetPoint(i, show, points[i]);
        }

        // 비교 시험 — 지금 동작의 Gn 셋(중립·능동·압박)과 Gl ~ Gn 값 한 줄.
        bool cmp = compareGlGn && motion >= 0 && motion < 2;
        for (int ph = 0; ph < 3; ph++)
        {
            int g = cmp ? GroupOf(motion, ph) : 0;
            view.SetExtraPoint(ph, cmp && hasExtra[g], extra[g]);
        }
        // 판의 비교 줄은 굴곡·신전 둘 다 늘 보여 준다 — 지금 어느 단계에 있든 찍어 둔 값이 남아 있어야 견줄 수 있다.
        for (int m = 0; m < 2; m++)
            view.SetCompare(m, compareGlGn,
                            cmpResults[m * 2].valid ? cmpResults[m * 2].angle.ToString("F1") + "°" : "—",
                            cmpDirect[m * 2].valid ? cmpDirect[m * 2].angle.ToString("F1") + "°" : "—",
                            cmpResults[m * 2 + 1].valid ? cmpResults[m * 2 + 1].angle.ToString("F1") + "°" : "—",
                            cmpDirect[m * 2 + 1].valid ? cmpDirect[m * 2 + 1].angle.ToString("F1") + "°" : "—");
        if (cmp)
        {
            int ca = motion * 2, cp = motion * 2 + 1;

            // Gl ~ Gn 선도 환자 위에 그린다. 값은 Tr ~ Gl 쪽과 같은 규칙 — 기준이 있으면 «면», 없으면 «직접».
            int gN = GroupOf(motion, 0), gA = GroupOf(motion, 1), gP = GroupOf(motion, 2);
            int iN = GroupStart(gN) + 1, iA = GroupStart(gA) + 1, iP = GroupStart(gP) + 1;   // 각 자세의 Gl
            bool hn = has[iN] && hasExtra[gN], ha = has[iA] && hasExtra[gA], hp = has[iP] && hasExtra[gP];
            string label = "";
            if (cmpDirect[ca].valid)
            {
                label = "Gl~Gn " + (cmpResults[ca].valid ? cmpResults[ca].angle : cmpDirect[ca].angle).ToString("F1") + "°";
                if (cmpDirect[cp].valid)
                    label += " / 압박 " + (cmpResults[cp].valid ? cmpResults[cp].angle : cmpDirect[cp].angle).ToString("F1") + "°";
            }
            view.ShowCompare(hn, points[iN], extra[gN], ha, points[iA], extra[gA], hp, points[iP], extra[gP],
                             label, cmpDirect[ca].pivot - Vector3.up * 0.035f);
        }
        else view.HideCompare();
        string pre = cmp ? "Tr~Gl " : "";   // 비교 시험 중에는 어느 선의 값인지 글자에 밝힌다

        // 선·각도기 — 지금 동작 것만.
        if (motion < 0) { view.HideMeasure(); return; }
        int n0 = GroupStart(GroupOf(motion, 0)), a0 = GroupStart(GroupOf(motion, 1)), p0 = GroupStart(GroupOf(motion, 2));
        if (!has[n0] || !has[n0 + 1]) { view.HideMeasure(); return; }

        RomLineResult ra = results[motion * 2], rp = results[motion * 2 + 1];
        RomLineDirect da = direct[motion * 2], dp = direct[motion * 2 + 1];
        if (ra.valid)
        {
            Vector3 normal = RomLineMath.PlaneNormal((RomLineMotion)motion, frame);
            view.ShowMeasure(ra, rp.valid, rp, normal, pre + ra.angle.ToString("F1") + "°", rp.valid ? "압박 " + rp.angle.ToString("F1") + "°" : "");
        }
        else if (da.valid)
        {
            view.ShowDirect(points[n0], points[n0 + 1], points[a0], points[a0 + 1], da.pivot, pre + da.angle.ToString("F1") + "°",
                            dp.valid, points[p0], points[p0 + 1], dp.valid ? "압박 " + dp.angle.ToString("F1") + "°" : "");
        }
        else view.ShowNeutralOnly(points[n0], points[n0 + 1]);   // 중립 기준선만 — 직선만 보인다
    }

    private void LogSummary(string why)
    {
        var sb = new StringBuilder(320);
        sb.Append("[선실측] 요약(").Append(why).Append(") — ");
        for (int m = 0; m < 6; m++)
        {
            sb.Append(MotionName[m]).Append(" 면 ")
              .Append(results[m * 2].valid ? results[m * 2].angle.ToString("F1") : "—").Append('/')
              .Append(results[m * 2 + 1].valid ? results[m * 2 + 1].angle.ToString("F1") : "—")
              .Append(" 직접 ")
              .Append(direct[m * 2].valid ? direct[m * 2].angle.ToString("F1") : "—").Append('/')
              .Append(direct[m * 2 + 1].valid ? direct[m * 2 + 1].angle.ToString("F1") : "—");
            if (m < 5) sb.Append(" · ");
        }
        sb.Append("  (능동/압박, 도)");
        if (compareGlGn)
        {
            sb.Append(" ‖ 비교 Gl~Gn — ");
            for (int m = 0; m < 2; m++)
            {
                sb.Append(MotionName[m]).Append(" 면 ")
                  .Append(cmpResults[m * 2].valid ? cmpResults[m * 2].angle.ToString("F1") : "—").Append('/')
                  .Append(cmpResults[m * 2 + 1].valid ? cmpResults[m * 2 + 1].angle.ToString("F1") : "—")
                  .Append(" 직접 ")
                  .Append(cmpDirect[m * 2].valid ? cmpDirect[m * 2].angle.ToString("F1") : "—").Append('/')
                  .Append(cmpDirect[m * 2 + 1].valid ? cmpDirect[m * 2 + 1].angle.ToString("F1") : "—");
                if (m == 0) sb.Append(" · ");
            }
        }
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

    // ── 에디터 ──────────────────────────────────────────────────────
    /// <summary>끝점 맞추기용(에디터 인스펙터가 읽는다). 컨트롤러가 잡혀 있을 때만 뜻이 있다.</summary>
    public bool TipValid => tipValid;
    public Vector3 TipWorld => tipPos;
    public bool UsingLeft => active == OVRInput.Controller.LTouch;
    public Vector3 ControllerWorld => ctrlPos;
    public bool ControllerModelShown => modelsOn.Count > 0;

    // 끝점을 맞출 때만 켜는 컨트롤러 모형. 씬에서는 꺼 둔 채로 두고(패스스루로 실제 컨트롤러가 보인다 — 10-01),
    // 인스펙터 스위치가 Play 중에만 켠다. ★우리가 켠 것만 끈다. Play가 끝나면 씬 값(꺼짐)으로 돌아가므로 빌드에는 나오지 않는다.
    private readonly System.Collections.Generic.List<GameObject> modelsOn = new System.Collections.Generic.List<GameObject>();

    public void ShowControllerModel(bool on)
    {
        if (!on)
        {
            foreach (GameObject g in modelsOn) if (g != null) g.SetActive(false);
            modelsOn.Clear();
            Debug.Log("[선실측] 컨트롤러 모형 끔");
            return;
        }
        if (modelsOn.Count > 0) return;
        int found = 0;
        foreach (OVRControllerHelper helper in FindObjectsByType<OVRControllerHelper>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            found++;
            // 모형은 꺼진 부모(OVRControllerVisual) 아래에 있다 — 꺼져 있는 조상을 찾아 켠다.
            for (Transform a = helper.transform; a != null; a = a.parent)
                if (!a.gameObject.activeSelf)
                {
                    a.gameObject.SetActive(true);
                    modelsOn.Add(a.gameObject);
                }
        }
        Debug.Log($"[선실측] 컨트롤러 모형 켬 — 모형 {found}개 중 꺼져 있던 {modelsOn.Count}곳을 켰다" +
                  (found == 0 ? " ★씬에 컨트롤러 모형(OVRControllerHelper)이 없다" : modelsOn.Count == 0 ? " (이미 켜져 있었다)" : ""));
    }

    // 씬 창에서 컨트롤러 원점(세 축 3cm)과 찍는 점까지의 선을 본다 — 끝점 오프셋이 어느 쪽으로 얼마인지 눈으로 확인하는 용도.
    private void OnDrawGizmos()
    {
        if (!Application.isPlaying || !tipValid) return;
        Gizmos.color = Color.red; Gizmos.DrawLine(ctrlPos, ctrlPos + ctrlRot * Vector3.right * 0.03f);
        Gizmos.color = Color.green; Gizmos.DrawLine(ctrlPos, ctrlPos + ctrlRot * Vector3.up * 0.03f);
        Gizmos.color = Color.blue; Gizmos.DrawLine(ctrlPos, ctrlPos + ctrlRot * Vector3.forward * 0.03f);
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(ctrlPos, tipPos);
        Gizmos.DrawWireSphere(tipPos, 0.004f);
    }
}
