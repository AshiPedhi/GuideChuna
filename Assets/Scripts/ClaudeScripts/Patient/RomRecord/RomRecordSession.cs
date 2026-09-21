using System.Collections.Generic;
using System.Text;
using Oculus.Interaction.Input;
using TMPro;
using UnityEngine;

/// <summary>
/// ★경추 ROM 실측 기록 프로그램 — 본체(2026-09-18 신설, 실측 전용 씬 <c>RomMarkerScene</c>에서 돈다).
///
/// 설계(2026-09-18 사용자와 확정 — 메모리 rom-marker-lab)
///   · <b>패스스루 전용</b>. 실제 사람 대상. 센서가 재는 도구가 아니라 <b>진단 기록 보조 + 3축 시각화</b>.
///   · 시작하면 지면 연직·수평 3축이 뜬다 → Y 회전·높이 조절 또는 대추 지정으로 자리를 잡고 → <b>환자를 축에 정렬</b>시킨다.
///   · 대추·미간(중립) 지정. 기준축은 목 둘레 중앙으로 앞뒤 보정할 수 있다(미간은 그대로).
///   · 4단계: 굴곡·신전(1회째 능동, 2회째 수동), 측굴·회전(4회, 좌우 자동, 홀수 능동·짝수 수동).
///   · 값 = 중립선에서 움직인 양(양수). 각도기 눈금 0은 단면 연직(회전은 정면).
///   · 마커: 핀치를 오므리면 생기고 펴면 고정. 동작 마커는 1° 단위 수정, 대추·미간은 mm 단위.
///   · 대추 기준 각과 목 중앙 기준 각을 <b>둘 다</b> 기록한다 — 어느 쪽이 실측에 가까운지 나중에 판별한다.
///
/// ★기존 실측(경추ROM실측 시나리오)·실측랩과 <b>서로 참조하지 않는다</b>. 이 씬 전용이다.
/// ★기록 줄은 <c>Debug.Log</c>로 남긴다 — <c>ChunaLogger.Log</c>는 릴리스 빌드에서 잘린다(Conditional).
/// </summary>
public class RomRecordSession : MonoBehaviour
{
    [Header("=== 시작 자리 ===")]
    [Tooltip("시작할 때 3축을 눈앞 이만큼(m) 앞에 둔다.")]
    [SerializeField] private float startDistance = 0.7f;
    [Tooltip("시작할 때 3축을 눈높이보다 이만큼(m) 낮게 둔다.")]
    [SerializeField] private float startBelowEye = 0.3f;
    [Tooltip("3축 선 길이(m, 중심에서 한쪽).")]
    [SerializeField] private float axisLength = 0.25f;

    [Header("=== 크기(글자는 TMP 폰트 크기, 스케일 1) ===")]
    [Tooltip("축 끝 글자·마커 각도 글자·눈금 숫자(0.8배). ★기존 각도기 판독 0.05(기기 확인값).\n" +
             "09-18: 1.1(너무 큼) → 0.03(\"각도기도 텍스트도 너무 줄였다\") → 0.05.")]
    [SerializeField] private float textSize = 0.05f;
    [Tooltip("안내판(단계·지시·기록 요약) 글자.")]
    [SerializeField] private float panelTextSize = 0.045f;
    [Tooltip("손목 판의 버튼 글자·머리줄 글자.")]
    [SerializeField] private float buttonLabelSize = 0.03f;

    [Header("=== 손목 판 — Meta UI Set(2026-09-21) ===")]
    // ★09-21 기기 판정: Meta UI 판은 <b>퇴화</b>였다(사용자) — 버튼 크기가 들쭉날쭉하고, 배치가 헐겁고,
    //   무엇보다 <b>누르는 손에 판이 가려</b> 안 보였다. 그래서 기본을 끔으로 내린다.
    //   지우지는 않는다 — 크기·배치 문제를 풀면 글자 크기 이점(Meta 표준 7mm)이 여전히 크다.
    [Tooltip("Meta Interaction SDK의 UI Set 프리팹으로 판을 만든다. ★09-21 기기 확인에서 퇴화로 판정돼 기본은 끔이다.")]
    [SerializeField] private bool useMetaUI = false;
    [Tooltip("Meta 판의 칸 너비(캔버스 px). 캔버스 스케일이 0.0005라 1px = 0.5mm다 — 84px ≈ 4.2cm.")]
    [SerializeField] private float metaCellPx = 84f;
    [Tooltip("Meta 판의 줄 높이(px).")]
    [SerializeField] private float metaRowPx = 68f;
    [Tooltip("Meta 판의 버튼 높이(px).")]
    [SerializeField] private float metaButtonPx = 56f;
    [Tooltip("Meta 판의 글자 크기(pt). ★Meta 표준은 14 — 캔버스 스케일을 곱하면 7mm다.")]
    [SerializeField] private float metaLabelPt = 15f;

    [Header("=== 손목 판(종전 Quad 판) ===")]
    [Tooltip("판의 칸 너비(m). 판은 6칸 너비다.")]
    [SerializeField] private float menuCellWidth = 0.042f;
    [Tooltip("판의 줄 높이(m).")]
    [SerializeField] private float menuRowHeight = 0.034f;
    [Tooltip("버튼 높이(m).")]
    [SerializeField] private float menuButtonHeight = 0.026f;
    [Tooltip("손목 메뉴를 손목에서 위로 띄우는 높이(m).")]
    [SerializeField] private float menuLift = 0.06f;
    [Tooltip("누르는 손 검지가 판에서 이 거리(m) 안으로 오면 판이 손목을 따라가지 않고 멈춘다(가림으로 튀는 것 방지).")]
    [SerializeField] private float menuHoldDistance = 0.15f;
    [Tooltip("버튼·핀치 소리 크기(0이면 무음).")]
    [Range(0f, 1f)] [SerializeField] private float soundVolume = 0.6f;

    [Header("=== 조정 단위 ===")]
    [SerializeField] private float yawStepDeg = 1f;
    [SerializeField] private float heightStepMm = 5f;
    [Tooltip("대추·미간을 버튼으로 옮기는 단위(mm).")]
    [SerializeField] private float nudgeStepMm = 1f;
    [Tooltip("기준축(목 중앙)을 앞뒤로 옮기는 단위(mm).")]
    [SerializeField] private float neckStepMm = 5f;
    [SerializeField] private float adjustStepDeg = 1f;

    [Header("=== 잡아 끌기(2026-09-21 신설) ===")]
    // ★09-21 로그 실측: 기준선 세팅에서 핀치가 17번 거부됐다("기준선 세팅 중에는 찍지 않는다").
    //   사용자는 3축을 손으로 잡아 옮기려 한 것이었다 — 5mm 버튼으로만 올리게 해 둬 한참 걸렸다.
    [Tooltip("3축(기준선)을 잡을 수 있는 반경(m). 이 안에서 핀치를 오므리면 3축이 손을 따라온다.")]
    [SerializeField] private float axesGrabRadius = 0.25f;
    [Tooltip("대추·미간 점을 잡을 수 있는 반경(m).")]
    [SerializeField] private float dotGrabRadius = 0.08f;

    [Header("=== 바늘 각도기(2026-09-21 신설) ===")]
    // ★사용자 지시 09-21: "미간 찍는 게 빡세면 각도기에 니들을 하나 그어서 그 끝을 잡고
    //   그 면에서만 회전하도록". ★점찍기를 <b>대체하지 않는다</b> — 둘 다 두고 따로 테스트한다.
    [Tooltip("동작 단계에서 바늘을 띄운다. 손목 판의 [바늘] 버튼으로도 껐다 켤 수 있다.")]
    [SerializeField] private bool needleEnabled = true;
    [Tooltip("바늘 손잡이를 잡을 수 있는 반경(m).")]
    [SerializeField] private float needleGrabRadius = 0.1f;
    [Tooltip("손잡이를 눈금판보다 이만큼(m) 더 밖에 둔다. ★0이면 손잡이가 머리 표면에 놓여 사람 머리에 묻힌다(09-21 사용자 지적).")]
    [SerializeField] private float needleHandleOut = 0.16f;
    [Tooltip("미간을 아직 안 찍었을 때 쓸 각도기 반지름(m). 미간이 있으면 중심에서 미간까지를 쓴다.")]
    [SerializeField] private float needleFallbackRadius = 0.18f;

    [Header("=== 나가기 ===")]
    [Tooltip("[나가기]를 이만큼(초) 누르고 있어야 나간다. ★09-21: 지탱하던 손이 스쳐 나가기가 눌렸다.")]
    [SerializeField] private float exitHoldSeconds = 1f;

    [Header("=== 기준축(목 중앙) ===")]
    [Tooltip("대추에서 환자 앞쪽으로 기준축을 옮기는 거리(mm). 사람마다 달라 정해진 값이 없다 — 버튼으로 맞춘다.\n" +
             "★0이면 대추 위치 그대로다.")]
    [SerializeField] private float neckOffsetMm = 0f;

    [Header("=== 좌우 ===")]
    [Tooltip("측굴·회전의 좌우가 반대로 기록되면 켠다. ★Play에서 환자 오른쪽으로 기울여 '우'가 찍히는지 먼저 본다.")]
    [SerializeField] private bool flipSides = false;

    [Header("=== 씬 ===")]
    [Tooltip("시작할 때 패스스루를 켠다. 이 씬은 패스스루 전용이다.")]
    [SerializeField] private bool enablePassthroughOnStart = true;
    [SerializeField] private string lobbySceneName = "lobby";   // ★실제 종료 팝업(ExitPopupController)이 쓰는 이름과 같게

    // ── 상태 ─────────────────────────────────────────────────────────
    private RomRecordStep step = RomRecordStep.Setup;
    private float yaw;
    private Vector3 frameOrigin;           // 대추를 찍기 전 3축 자리
    private bool hasC7, hasGlab;
    private Vector3 c7, glab;
    private int landmarkTarget;            // 0 대추 · 1 미간 · 2 목 중앙
    private readonly List<RomRecordMark>[] marks =
    {
        new List<RomRecordMark>(), new List<RomRecordMark>(), new List<RomRecordMark>(), new List<RomRecordMark>(),
    };

    private readonly RomRecordHands hands = new RomRecordHands();
    // ★판 구현이 둘이다(09-21) — Meta UI Set(기본)과 종전 월드 Quad 판. useMetaUI로 고른다.
    private IRomRecordMenu leftMenu, rightMenu;
    private readonly RomRecordVisual view = new RomRecordVisual();
    private Transform eye;
    private bool placed;
    private bool dirty = true;
    private bool livePinchLeft;            // 지금 오므리고 있는 손(한 번에 하나만 받는다)
    private bool liveActive;
    // ★바늘(09-21) — 단계마다 <b>둘</b>(능동·압박)을 들고 있는다. 인덱스는 단계*2 + 종류다.
    //   <b>각이 아니라 방향</b>으로 둔다(규칙 9: 부호를 정할 일이 없다).
    private const int NeedleActive = 0, NeedlePress = 1;
    private readonly Vector3[] needleDir = new Vector3[8];
    private readonly bool[] needlePlaced = new bool[8];
    private bool needleOn = true;
    // ★잡아 끌기(09-21) — 0 3축 · 1 대추 · 2 미간 · 3 능동 바늘 · 4 압박 바늘 · -1 아무것도 안 잡음(«찍기»로 간다)
    private int dragTarget = -1;
    private Vector3 dragGrabbedAt, dragStartValue;
    // ★진단 로그 자기 침묵 — <b>손별로</b> 따로 센다(09-21 수정).
    //   처음엔 하나로 뒀는데 왼손·오른손을 번갈아 처리하니 «마지막 이유»가 매번 바뀌어
    //   억제가 통째로 죽었다 — 한 판에 11,000줄이 쏟아졌다. 로그가 로그를 못 보게 만든 꼴이다.
    private readonly float[] nextPinchLog = new float[2];
    private readonly Dictionary<string, int> pinchBlockCount = new Dictionary<string, int>();
    private readonly StringBuilder sb = new StringBuilder(512);
    private float lastPressTime = -99f;
    private bool menuHoldL, menuHoldR;
    private AudioSource audioSrc;
    private AudioClip sndPress, sndRepeat, sndPinch, sndUndo, sndDeny;

    private static readonly string[] StepTitle = { "기준선 세팅", "대추·미간", "굴곡", "신전", "측굴", "회전", "완료" };

    private void Start()
    {
        if (enablePassthroughOnStart) EnablePassthrough();

        TMP_FontAsset font = KoreanFontResolver.Resolve();
        Shader sh = Shader.Find("Sprites/Default");   // ★Always Included에 들어 있다(GraphicsSettings 10753) — 빌드에서도 null이 아니다
        Material mat = sh != null ? new Material(sh) : null;
        if (mat == null) ChunaLogger.LogWarning("[실측기록] Sprites/Default 셰이더를 못 찾았다 — 선이 분홍으로 보이면 이것이다.");

        needleOn = needleEnabled;   // ★런타임 토글([바늘] 버튼)이 이 값을 이어받는다
        view.textSize = textSize;
        view.panelSize = panelTextSize;
        view.Build(transform, font, mat);
        if (useMetaUI)
        {
            var lm = new RomRecordMenuUI();
            var rm = new RomRecordMenuUI();
            foreach (var menu in new[] { lm, rm })
            {
                menu.cellPx = metaCellPx;
                menu.rowPx = metaRowPx;
                menu.buttonPx = metaButtonPx;
                menu.labelPt = metaLabelPt;
                menu.headerPt = metaLabelPt;
                menu.lift = menuLift;
            }
            leftMenu = lm;
            rightMenu = rm;
        }
        else
        {
            var lm = new RomRecordWristMenu();
            var rm = new RomRecordWristMenu();
            foreach (var menu in new[] { lm, rm })
            {
                menu.labelSize = buttonLabelSize;
                menu.headerSize = buttonLabelSize;
                menu.cellW = menuCellWidth;
                menu.rowH = menuRowHeight;
                menu.buttonH = menuButtonHeight;
                menu.lift = menuLift;
            }
            leftMenu = lm;
            rightMenu = rm;
        }
        leftMenu.Build(transform, "왼손목 메뉴", 14, font, mat);
        rightMenu.Build(transform, "오른손목 메뉴", 14, font, mat);
        Debug.Log("[실측기록] 손목 판 — " + (useMetaUI ? "Meta UI Set" : "종전 Quad 판"));
        ApplyStepButtons();
        BuildSounds();

        Debug.Log("[실측기록] 시작 — 기준선 세팅부터. 좌우 뒤집기 " + (flipSides ? "켬" : "끔") + $" · 목 중앙 보정 {neckOffsetMm:F0}mm · " +
                  $"글자 {textSize}/{panelTextSize}/버튼 {buttonLabelSize} · 칸 {menuCellWidth * 100f:F1}×{menuRowHeight * 100f:F1}cm · 소리 {soundVolume:F1}");
    }

    // ── 소리 ─────────────────────────────────────────────────────────
    // ★09-18 사용자: "누른 건지 구분이 안 간다. 소리도 애니메이션도 없다."
    //   음원 파일 없이 짧은 음을 코드로 만든다(에셋 배선이 필요 없고 빌드에서 빠질 일이 없다).
    private void BuildSounds()
    {
        audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.playOnAwake = false;
        audioSrc.spatialBlend = 0f;            // 2D — 어디서 눌러도 같은 크기로 들린다
        sndPress = Tone("누름", 1250f, 1250f, 0.045f);
        sndRepeat = Tone("반복", 1600f, 1600f, 0.025f);
        sndPinch = Tone("찍음", 880f, 1320f, 0.11f);      // 올라가는 두 음 — 기록됐다
        sndUndo = Tone("취소", 700f, 440f, 0.09f);        // 내려가는 음 — 지웠다
        sndDeny = Tone("거부", 300f, 300f, 0.12f);        // 낮은 음 — 받지 않았다
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
            float f = Mathf.Lerp(f0, f1, t);
            phase += 2f * Mathf.PI * f / rate;
            float env = Mathf.Min(1f, t * 40f) * (1f - t) * (1f - t);   // 짧게 올라갔다 빠르게 사라진다(딸깍)
            data[i] = Mathf.Sin(phase) * env * 0.6f;
        }
        var clip = AudioClip.Create("[실측기록] " + name, n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private void Play(AudioClip c)
    {
        if (audioSrc != null && c != null && soundVolume > 0f) audioSrc.PlayOneShot(c, soundVolume);
    }

    private void EnablePassthrough()
    {
        // ★이 씬은 패스스루 전용이라 우리가 켜고 씬이 내려갈 때 같이 사라진다(다른 씬과 공유하는 상태가 아니다).
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
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);   // ★알파 0이어야 뒤의 패스스루가 보인다(Underlay)
        }
        Debug.Log($"[실측기록] 패스스루 — 레이어 {(layer != null ? "O" : "★없음")} · OVRManager {(OVRManager.instance != null ? "O" : "★없음")} · 카메라 {(cam != null ? "O" : "★없음")}");
    }

    private void Update()
    {
        if (eye == null && Camera.main != null) eye = Camera.main.transform;
        if (eye == null) return;

        if (!placed)
        {
            // 처음 한 번 — 눈앞에, 눈높이보다 조금 아래에 3축을 둔다. 정면은 사용자가 보는 수평 방향.
            Vector3 f = Vector3.ProjectOnPlane(eye.forward, Vector3.up);
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            f.Normalize();
            frameOrigin = eye.position + f * startDistance - Vector3.up * startBelowEye;
            yaw = Quaternion.LookRotation(f, Vector3.up).eulerAngles.y;
            placed = true;
            dirty = true;
        }

        hands.Resolve();
        HandleMenus();
        HandlePinch(true);
        HandlePinch(false);

        if (dirty)
        {
            dirty = false;
            Redraw();
        }
        view.FaceCamera(eye);
    }

    // ── 계산 ─────────────────────────────────────────────────────────
    private Vector3 Pivot => hasC7 ? c7 + RomRecordGeometry.Forward(yaw) * (neckOffsetMm * 0.001f) : frameOrigin;

    /// <summary>
    /// 각도기·바늘이 함께 쓰는 기하(2026-09-21). ★표시·드래그·기록이 <b>같은 함수</b>를 탄다(규칙 9 —
    /// 따로 계산하면 미리보기가 거짓말을 한다).
    /// ★미간이 없어도 뜬다 — 바늘은 미간 찍기를 대신하려고 만든 것이다. 그때는 반지름이 기본값이고
    ///   중립선 기준 각은 못 낸다(눈금 기준 각만 남는다).
    /// </summary>
    private bool DialFrame(out Vector3 center, out Vector3 normal, out Vector3 zero, out float radius)
    {
        center = default;
        normal = Vector3.up;
        zero = Vector3.forward;
        radius = needleFallbackRadius;
        if (!IsMotion(step) || !hasC7) return false;

        Vector3 pivot = Pivot;
        normal = RomRecordGeometry.PlaneNormal(step, yaw);
        zero = RomRecordGeometry.ScaleZero(step, yaw);
        // 회전 눈금판은 미간 높이의 수평면에 둔다 — 회전 중심 높이에 두면 머리에 가려 안 보인다.
        center = step == RomRecordStep.Rotation && hasGlab ? new Vector3(pivot.x, glab.y, pivot.z) : pivot;
        radius = hasGlab
            ? Mathf.Max(0.08f, Vector3.ProjectOnPlane(glab - center, normal).magnitude)
            : needleFallbackRadius;
        return true;
    }

    private static int MotionIndex(RomRecordStep s) => (int)s - (int)RomRecordStep.Flexion;
    private static bool IsMotion(RomRecordStep s) => s >= RomRecordStep.Flexion && s <= RomRecordStep.Rotation;
    private static int Capacity(RomRecordStep s) => s == RomRecordStep.LateralFlexion || s == RomRecordStep.Rotation ? 4 : 2;
    private static bool HasSides(RomRecordStep s) => s == RomRecordStep.LateralFlexion || s == RomRecordStep.Rotation;

    /// <summary>그 마커의 각 — 지금 기준축(목 중앙) 기준, 수정 포함.</summary>
    private float AngleOf(RomRecordStep s, RomRecordMark m, Vector3 pivot)
    {
        Vector3 n = RomRecordGeometry.PlaneNormal(s, yaw);
        return Mathf.Max(0f, RomRecordGeometry.PlaneAngle(pivot, glab, m.raw, n) + m.adjustDeg);
    }

    // ── 손목 메뉴 ────────────────────────────────────────────────────
    private void HandleMenus()
    {
        bool lw = hands.TryWrist(true, out Pose lwp);
        bool rw = hands.TryWrist(false, out Pose rwp);
        bool rTip = hands.TryJoint(false, HandJointId.HandIndexTip, out Vector3 rIdx);
        bool lTip = hands.TryJoint(true, HandJointId.HandIndexTip, out Vector3 lIdx);

        // ★다가오면 제자리 고정(09-18 사용자 결정). 왼손목 판은 오른 검지가, 오른손목 판은 왼 검지가 누른다.
        //   누르는 손이 판 쪽 손목을 가리면 그 손목 추적이 끊겨 판이 숨었다 튀었다("UI가 도망간다").
        menuHoldL = rTip && leftMenu.Distance(rIdx) < menuHoldDistance;
        menuHoldR = lTip && rightMenu.Distance(lIdx) < menuHoldDistance;
        leftMenu.Follow(lw, lwp.position, eye, menuHoldL);
        rightMenu.Follow(rw, rwp.position, eye, menuHoldR);

        string id = leftMenu.Poll(rIdx, rTip);
        bool repeat = leftMenu.LastRepeat;
        if (id == null) { id = rightMenu.Poll(lIdx, lTip); repeat = rightMenu.LastRepeat; }
        if (id != null)
        {
            lastPressTime = Time.unscaledTime;
            Play(id == "undo" ? sndUndo : repeat ? sndRepeat : sndPress);
            OnButton(id);
        }
        leftMenu.Tick();
        rightMenu.Tick();
    }

    private static readonly Color NavTint = new Color(0.35f, 0.65f, 0.95f);
    private static readonly Color ExitTint = new Color(0.9f, 0.3f, 0.3f);
    private static readonly Color AdjTint = new Color(0.45f, 0.75f, 0.35f);
    private static readonly Color TargetTint = new Color(0.95f, 0.75f, 0.2f);
    private static readonly Color UndoTint = new Color(0.95f, 0.55f, 0.2f);
    private static readonly Color ActiveTint = new Color(0.95f, 0.6f, 0.15f);
    private static readonly Color PressTint = new Color(0.9f, 0.35f, 0.9f);
    private static readonly Color NeedleTint = new Color(0.2f, 0.8f, 0.7f);   // 바늘 — 표시물 색과 같은 계열

    /// <summary>
    /// 단계별 판 배치(09-18 사용자 결정 "그룹 판넬"). 판은 6칸 너비 · 0행은 머리줄.
    /// 머리줄: 단계·진행 + [나가기](오른쪽 끝) / 가운데: 그 단계의 조작 묶음 / 맨 아래: [◀ 이전] [다음 ▶].
    /// </summary>
    private void ApplyStepButtons()
    {
        var items = new List<RomMenuItem>(14);
        float navRow;

        switch (step)
        {
            case RomRecordStep.Setup:
                items.Add(RomMenuItem.Text("정면", 0f, 1, 1f));
                items.Add(RomMenuItem.Button("yaw-", "↺", 1f, 1, 1f, AdjTint, repeat: true));
                items.Add(RomMenuItem.Button("yaw+", "↻", 2f, 1, 1f, AdjTint, repeat: true));
                items.Add(RomMenuItem.Text("높이", 3.2f, 1, 1f));
                items.Add(RomMenuItem.Button("h+", "▲", 4.4f, 1, 1f, AdjTint, repeat: true));
                items.Add(RomMenuItem.Button("h-", "▼", 4.4f, 2, 1f, AdjTint, repeat: true));
                navRow = 3;
                break;

            case RomRecordStep.Landmarks:
                items.Add(RomMenuItem.Button("t0", "대추", 0f, 1, 2f, TargetTint, selected: landmarkTarget == 0));
                items.Add(RomMenuItem.Button("t1", "미간", 2f, 1, 2f, TargetTint, selected: landmarkTarget == 1));
                items.Add(RomMenuItem.Button("t2", "목중앙", 4f, 1, 2f, TargetTint, selected: landmarkTarget == 2));
                if (landmarkTarget == 2)
                {
                    // 목 중앙은 앞·뒤로만 옮긴다(미간은 그대로)
                    items.Add(RomMenuItem.Button("fwd", "앞", 2f, 2, 2f, AdjTint, repeat: true));
                    items.Add(RomMenuItem.Button("back", "뒤", 2f, 3, 2f, AdjTint, repeat: true));
                }
                else
                {
                    // 십자: 앞·뒤·왼·오른(수평) + 옆에 위·아래(수직). 환자 기준 방향이다.
                    items.Add(RomMenuItem.Button("fwd", "앞", 1f, 2, 1f, AdjTint, repeat: true));
                    items.Add(RomMenuItem.Button("left", "왼", 0f, 3, 1f, AdjTint, repeat: true));
                    items.Add(RomMenuItem.Button("right", "오른", 2f, 3, 1f, AdjTint, repeat: true));
                    items.Add(RomMenuItem.Button("back", "뒤", 1f, 4, 1f, AdjTint, repeat: true));
                    items.Add(RomMenuItem.Button("up", "위", 4.4f, 2, 1.2f, AdjTint, repeat: true));
                    items.Add(RomMenuItem.Button("down", "아래", 4.4f, 4, 1.2f, AdjTint, repeat: true));
                }
                navRow = 5;
                break;

            case RomRecordStep.Done:
                navRow = 1;
                break;

            default:   // 굴곡·신전·측굴·회전
                items.Add(RomMenuItem.Button("adj-", "-1°", 1f, 1, 1.8f, AdjTint, repeat: true));
                items.Add(RomMenuItem.Button("adj+", "+1°", 3.2f, 1, 1.8f, AdjTint, repeat: true));
                items.Add(RomMenuItem.Button("undo", "취소", 2.1f, 2, 1.8f, UndoTint));
                // ★바늘(09-21) — 점찍기와 <b>나란히</b> 둔다. 어느 쪽으로 기록할지는 사용자가 고른다.
                items.Add(RomMenuItem.Button("needle", needleOn ? "바늘 끔" : "바늘 켬", 0f, 3, 2.2f, NeedleTint, selected: needleOn));
                if (needleOn)
                {
                    // ★바늘이 둘이라 기록도 둘이다(09-21) — 능동·압박을 사용자가 고른다.
                    items.Add(RomMenuItem.Button("nrecA", "능동 기록", 2.4f, 3, 1.8f, ActiveTint));
                    items.Add(RomMenuItem.Button("nrecP", "압박 기록", 4.2f, 3, 1.8f, PressTint));
                }
                navRow = 4;
                break;
        }

        if (step > RomRecordStep.Setup) items.Add(RomMenuItem.Button("prev", "◀ 이전", 0f, navRow, 2.2f, NavTint));
        if (step < RomRecordStep.Done) items.Add(RomMenuItem.Button("next", "다음 ▶", 3.8f, navRow, 2.2f, NavTint));
        // ★[나가기]는 판 모서리에서 뺀다(09-21 사용자 지시). 맨 아랫줄 <b>가운데</b> + 길게 눌러야 먹는다.
        //   종전엔 머리줄 오른쪽 끝(모서리)이라 손을 뻗을 때 가장 먼저 닿았고, 닿는 즉시 실행됐다.
        items.Add(RomMenuItem.Button("exit", "나가기", 2.1f, navRow + 1f, 1.8f, ExitTint, holdSeconds: exitHoldSeconds));

        var arr = items.ToArray();
        string head = MenuHeader();
        leftMenu.SetLayout(head, arr);
        rightMenu.SetLayout(head, arr);
    }

    /// <summary>판 머리줄 — 단계·진행, 동작 단계면 지금까지 찍은 값. 값이 바뀔 때만 만든다.</summary>
    private string MenuHeader()
    {
        sb.Clear();
        sb.Append((int)step + 1).Append("/7  ").Append(StepTitle[(int)step]);
        if (IsMotion(step))
        {
            var list = marks[MotionIndex(step)];
            Vector3 pivot = Pivot;
            for (int i = 0; i < list.Count; i++)
            {
                sb.Append(i == 0 ? "   " : " · ").Append(Kind(list[i])).Append(' ');
                AppendDeg(sb, step, list[i], pivot);
            }
        }
        else if (step == RomRecordStep.Landmarks && landmarkTarget == 2)
        {
            sb.Append("   목중앙 ").Append(neckOffsetMm.ToString("F0")).Append("mm");
        }
        return sb.ToString();
    }

    /// <summary>
    /// 요약 줄에 쓸 각 하나. ★바늘은 눈금 각, 점은 중립선 기준 각이다 — <b>다른 수</b>라서 섞어 적으면 안 된다.
    /// 미간이 없으면 점은 각이 없다(0°가 아니라 «—»다).
    /// </summary>
    private void AppendDeg(StringBuilder b, RomRecordStep s, RomRecordMark m, Vector3 pivot)
    {
        if (m.byNeedle) b.Append(m.dialDeg.ToString("F0")).Append('°');
        else if (hasGlab) b.Append(AngleOf(s, m, pivot).ToString("F0")).Append('°');
        else b.Append('—');
    }

    private void OnButton(string id)
    {
        Vector3 f = RomRecordGeometry.Forward(yaw), r = RomRecordGeometry.Right(yaw), u = Vector3.up;
        float mm = nudgeStepMm * 0.001f;
        switch (id)
        {
            case "yaw-": yaw -= yawStepDeg; break;
            case "yaw+": yaw += yawStepDeg; break;
            case "h+": if (!hasC7) frameOrigin += u * (heightStepMm * 0.001f); break;
            case "h-": if (!hasC7) frameOrigin -= u * (heightStepMm * 0.001f); break;
            case "t0": SetTarget(0); break;
            case "t1": SetTarget(1); break;
            case "t2": SetTarget(2); break;
            case "up": Nudge(u * mm); break;
            case "down": Nudge(-u * mm); break;
            case "fwd": if (landmarkTarget == 2) neckOffsetMm += neckStepMm; else Nudge(f * mm); break;
            case "back": if (landmarkTarget == 2) neckOffsetMm -= neckStepMm; else Nudge(-f * mm); break;
            case "left": Nudge(-r * mm); break;
            case "right": Nudge(r * mm); break;
            case "adj+": AdjustLast(+adjustStepDeg); break;
            case "adj-": AdjustLast(-adjustStepDeg); break;
            case "undo": UndoLast(); break;
            case "needle":
                needleOn = !needleOn;
                lastNeedleDegShown[0] = lastNeedleDegShown[1] = -999;
                Debug.Log("[실측기록] 바늘 " + (needleOn ? "켬" : "끔"));
                ApplyStepButtons();
                break;
            case "nrecA": RecordNeedle(NeedleActive); break;
            case "nrecP": RecordNeedle(NeedlePress); break;
            case "prev": GoTo(step - 1); break;
            case "next": GoTo(step + 1); break;
            case "exit": Exit(); break;
        }
        if (id == "fwd" || id == "back")
            if (landmarkTarget == 2) Debug.Log($"[실측기록] 목 중앙 보정 {neckOffsetMm:F0}mm");
        dirty = true;
    }

    private void SetTarget(int t)
    {
        if (landmarkTarget == t) return;
        landmarkTarget = t;
        ApplyStepButtons();   // 고른 버튼을 밝히고, 목중앙이면 앞·뒤만 남긴다
    }

    private void Nudge(Vector3 d)
    {
        if (landmarkTarget == 0 && hasC7) c7 += d;
        else if (landmarkTarget == 1 && hasGlab) glab += d;
    }

    private void GoTo(RomRecordStep s)
    {
        if (s < RomRecordStep.Setup || s > RomRecordStep.Done) return;
        // ★대추만 필수로 바꿨다(09-21). 바늘은 미간 없이도 재는 수단이라 미간으로 막으면 그 길이 닫힌다.
        //   미간이 없으면 점찍기 각(중립선 기준)은 못 내고 바늘 눈금 각만 남는다 — 안내판이 그것을 말한다.
        if (IsMotion(s) && !hasC7)
        {
            Play(sndDeny);
            Debug.Log("[실측기록] 대추를 먼저 찍는다 — 동작 단계로 못 넘어간다.");
            return;
        }
        if (IsMotion(s) && !hasGlab)
            Debug.Log("[실측기록] ★미간이 없다 — 점찍기는 각이 안 나온다. 바늘로 눈금 각만 기록된다.");
        step = s;
        ApplyStepButtons();
        Debug.Log($"[실측기록] 단계 → {StepTitle[(int)step]}");
        if (step == RomRecordStep.Done) LogSummary();
    }

    private void Exit()
    {
        LogSummary();
        Debug.Log("[실측기록] 나가기 → " + lobbySceneName);
        SceneLoader.LoadScene(lobbySceneName);
    }

    // ── 핀치 ─────────────────────────────────────────────────────────
    private void HandlePinch(bool isLeft)
    {
        // ★한 번에 한 손만. 다른 손이 오므리고 있으면 이 손은 새로 시작하지 않는다.
        bool otherBusy = liveActive && livePinchLeft != isLeft;

        // ★버튼을 누르는 동안과 누른 직후에는 핀치를 받지 않는다(09-18 로그: 의도하지 않은 핀치 30여 번).
        //   판을 누르러 다가오는 중(고정 상태)이면 양손 모두 막는다 — 가려진 손이 핀치로 읽히는 것을 막는다.
        bool nearMenu = hands.TryJoint(isLeft, HandJointId.HandIndexTip, out Vector3 tip)
                        && (leftMenu.Near(tip, 0.06f) || rightMenu.Near(tip, 0.06f));

        // ★막은 이유를 문구로 넘긴다(09-21) — 막는 자리가 넷인데 셋이 조용해 원인을 못 갈랐다.
        string blockedBy = otherBusy ? "다른 손이 잡는 중"
                         : menuHoldL || menuHoldR ? "판에 손이 다가옴(판 고정 중)"
                         : Time.unscaledTime - lastPressTime < 0.4f ? "버튼 누른 직후"
                         : nearMenu ? "판 근처" : null;

        int ev = hands.UpdatePinch(isLeft, blockedBy, out Vector3 fixedPos);
        var st = isLeft ? hands.LeftPinch : hands.RightPinch;
        ReportPinch(isLeft, st);

        if (ev == 1)
        {
            liveActive = true;
            livePinchLeft = isLeft;
            // ★잡을 것이 있으면 «찍기»가 아니라 «잡아 끌기»다. 없으면 종전대로 찍는다.
            dragTarget = FindDragTarget(st.current, out dragStartValue);
            if (dragTarget >= 0)
            {
                dragGrabbedAt = st.current;
                Play(sndPress);
                Debug.Log($"[실측기록] 잡았다 — {DragName(dragTarget)}");
            }
        }
        if (liveActive && livePinchLeft == isLeft)
        {
            if (st.closed)
            {
                if (dragTarget >= 0) ApplyDrag(st.current);
                else view.SetLive(true, st.current);
            }
            if (ev == 2)
            {
                liveActive = false;
                view.SetLive(false, Vector3.zero);
                if (dragTarget >= 0) EndDrag();
                else OnPinchFixed(fixedPos);
            }
            else if (ev == 3 || ev == 4)
            {
                liveActive = false;
                view.SetLive(false, Vector3.zero);
                // ★놓친 드래그는 되돌리지 않는다 — 옮긴 자리를 그대로 둔다(되돌리면 더 놀란다).
                if (dragTarget >= 0) EndDrag();
                else if (ev == 3) Debug.Log("[실측기록] 핀치 취소 — 손을 놓쳤거나 시스템 제스처가 시작됐다.");
            }
        }
    }

    /// <summary>
    /// ★막힌 이유를 2초에 한 줄만 말한다(자기 침묵 — 잘 잡히면 한 줄도 안 남는다).
    /// "위에서 잡으면 안 잡힌다"(09-21)를 가르려고 <b>손 높이·판까지 거리·신뢰도</b>를 같이 찍는다.
    /// </summary>
    private void ReportPinch(bool isLeft, RomRecordHands.PinchState st)
    {
        string r = st.blockReason;
        if (r == null) return;
        // ★세는 것은 매번, 말하는 것은 손별로 2초에 한 번. 요약에서 <b>종류별 횟수</b>로 본다 —
        //   한 줄씩 쏟아 놓고 눈으로 세면 오판한다(09-21에 11,000줄을 쏟아 놓고 그랬다).
        pinchBlockCount.TryGetValue(r, out int n);
        pinchBlockCount[r] = n + 1;

        int k = isLeft ? 0 : 1;
        if (Time.unscaledTime < nextPinchLog[k]) return;
        nextPinchLog[k] = Time.unscaledTime + 2f;

        float eyeDy = eye != null ? (st.tip.y - eye.position.y) * 100f : 0f;
        float toMenu = Mathf.Min(leftMenu.Distance(st.tip), rightMenu.Distance(st.tip));
        Debug.Log($"[실측기록·핀치] 안 받는다 — {(isLeft ? "왼" : "오른")}손 «{r}» · " +
                  $"간격 {(st.gap >= 0f ? (st.gap * 100f).ToString("F1") + "cm" : "—")} · " +
                  $"신뢰 {(st.highConfidence ? "O" : "★낮음")} · 눈높이 대비 {eyeDy:+0;-0}cm · " +
                  $"판까지 {(toMenu < 9f ? (toMenu * 100f).ToString("F0") + "cm" : "—")}");
    }

    // ── 잡아 끌기(09-21 신설) ─────────────────────────────────────────
    private static string DragName(int t) => t == 0 ? "기준선(3축)" : t == 1 ? "대추" : t == 2 ? "미간" : "바늘";

    /// <summary>핀치를 오므린 자리에서 잡을 수 있는 것을 고른다. 없으면 -1(그러면 «찍기»로 간다).</summary>
    private int FindDragTarget(Vector3 p, out Vector3 startValue)
    {
        startValue = default;
        // ★바늘 손잡이가 먼저다(09-21) — 동작 단계에서 바늘 끝을 잡으면 마커를 찍지 않는다.
        //   바늘이 둘이라 <b>가까운 쪽</b>을 잡는다.
        if (NeedleVisible(out Vector3 nc, out _, out _, out _, out float nh, out int mi))
        {
            float dA = Vector3.Distance(p, nc + needleDir[mi * 2 + NeedleActive] * nh);
            float dP = Vector3.Distance(p, nc + needleDir[mi * 2 + NeedlePress] * nh);
            if (Mathf.Min(dA, dP) <= needleGrabRadius) return dA <= dP ? 3 : 4;
        }
        if (step == RomRecordStep.Setup)
        {
            if (Vector3.Distance(p, frameOrigin) > axesGrabRadius) return -1;
            startValue = frameOrigin;
            return 0;
        }
        if (step != RomRecordStep.Landmarks) return -1;

        // 찍혀 있는 점만 잡는다. 아직 안 찍은 것은 종전대로 핀치로 찍는다.
        float dC7 = hasC7 ? Vector3.Distance(p, c7) : float.MaxValue;
        float dGl = hasGlab ? Vector3.Distance(p, glab) : float.MaxValue;
        if (dC7 > dotGrabRadius && dGl > dotGrabRadius) return -1;
        if (dC7 <= dGl) { startValue = c7; return 1; }
        startValue = glab;
        return 2;
    }

    /// <summary>
    /// 바늘이 지금 보이나. 보이면 기하와 <b>손잡이 반지름</b>을 준다. 바늘 둘이 같은 기하를 쓴다.
    /// ★손잡이 반지름은 눈금 반지름보다 <see cref="needleHandleOut"/>만큼 <b>더 밖</b>이다 —
    ///   눈금 반지름은 회전중심에서 미간까지라 그 자리에 손잡이를 두면 <b>사람 머리 안에 묻힌다</b>(09-21).
    /// </summary>
    private bool NeedleVisible(out Vector3 center, out Vector3 normal, out Vector3 zero,
                               out float radius, out float handleRadius, out int mi)
    {
        mi = 0;
        handleRadius = needleFallbackRadius + needleHandleOut;
        if (!needleOn || !DialFrame(out center, out normal, out zero, out radius))
        {
            center = default; normal = Vector3.up; zero = Vector3.forward; radius = needleFallbackRadius;
            return false;
        }
        handleRadius = radius + needleHandleOut;
        mi = MotionIndex(step);
        for (int k = 0; k < 2; k++)
        {
            int n = mi * 2 + k;
            if (needlePlaced[n]) continue;
            // 처음엔 눈금 0(연직 · 회전은 정면)에서 서로 조금 벌려 세운다 — 겹쳐 두면 하나뿐인 줄 안다.
            needleDir[n] = Quaternion.AngleAxis(k == NeedleActive ? -8f : 8f, normal) * zero;
            needlePlaced[n] = true;
        }
        return true;
    }

    private static string NeedleName(int k) => k == NeedleActive ? "능동" : "압박";

    private void ApplyDrag(Vector3 p)
    {
        // ★바늘은 델타가 아니라 <b>손이 있는 쪽</b>을 향한다. 단면에 투영해 그 면에서만 돌게 한다.
        if (dragTarget == 3 || dragTarget == 4)
        {
            if (NeedleVisible(out Vector3 nc, out Vector3 nn, out _, out _, out _, out int mi))
            {
                int n = mi * 2 + (dragTarget == 3 ? NeedleActive : NeedlePress);
                needleDir[n] = RomRecordGeometry.OnPlane(p - nc, nn, needleDir[n]);
                DrawNeedle();
            }
            return;
        }

        Vector3 v = dragStartValue + (p - dragGrabbedAt);
        switch (dragTarget)
        {
            case 0: frameOrigin = v; break;
            case 1: c7 = v; break;
            case 2: glab = v; break;
        }
        // ★끄는 동안은 가벼운 갱신만 한다 — 안내판·머리줄 문자열을 매 프레임 새로 만들지 않는다(VR 프레임 예산).
        Vector3 pivot = Pivot;
        view.SetFrame(pivot, yaw, axisLength);
        view.SetLandmarks(hasC7, c7, hasGlab, glab, pivot);
    }

    private void EndDrag()
    {
        if ((dragTarget == 3 || dragTarget == 4) && NeedleVisible(out _, out _, out Vector3 z, out _, out _, out int mi))
        {
            int k = dragTarget == 3 ? NeedleActive : NeedlePress;
            Debug.Log($"[실측기록] {NeedleName(k)} 바늘 놓았다 — 눈금 {Vector3.Angle(z, needleDir[mi * 2 + k]):F1}° " +
                      $"([{NeedleName(k)} 기록]을 눌러야 남는다)");
        }
        else
            Debug.Log($"[실측기록] 놓았다 — {DragName(dragTarget)} {Fmt(dragTarget == 0 ? frameOrigin : dragTarget == 1 ? c7 : glab)}");
        dragTarget = -1;
        Play(sndPinch);
        dirty = true;
    }

    // ── 바늘(09-21 신설) ─────────────────────────────────────────────
    private readonly int[] lastNeedleDegShown = { -999, -999 };

    /// <summary>바늘 둘을 그린다. ★끄는 동안 매 프레임 불린다 — 각이 1° 넘게 바뀔 때만 글자를 새로 만든다.</summary>
    private void DrawNeedle()
    {
        if (!NeedleVisible(out Vector3 c, out _, out Vector3 z, out float r, out float h, out int mi))
        {
            for (int k = 0; k < 2; k++)
            {
                view.SetNeedle(k, false, Vector3.zero, Vector3.up, 0.1f, 0.2f, null, false);
                lastNeedleDegShown[k] = -999;
            }
            return;
        }
        for (int k = 0; k < 2; k++)
        {
            Vector3 dir = needleDir[mi * 2 + k];
            int d = Mathf.RoundToInt(Vector3.Angle(z, dir));
            string label = null;
            if (d != lastNeedleDegShown[k])
            {
                lastNeedleDegShown[k] = d;
                label = NeedleName(k) + " " + d + "°";
            }
            view.SetNeedle(k, true, c, dir, r, h, label, dragTarget == 3 + k);
        }
    }

    /// <summary>바늘이 지금 가리키는 각을 기록한다. ★점찍기와 따로 남긴다 — 사용자가 둘을 비교한다.</summary>
    private void RecordNeedle(int kind)
    {
        if (!NeedleVisible(out Vector3 c, out _, out Vector3 z, out float r, out _, out int mi))
        {
            Play(sndDeny);
            Debug.Log("[실측기록] 바늘이 없다 — 대추를 먼저 찍고 동작 단계로 온다.");
            return;
        }
        var list = marks[mi];
        if (list.Count >= Capacity(step))
        {
            Play(sndDeny);
            Debug.Log($"[실측기록] {StepTitle[(int)step]}은 {Capacity(step)}개까지다 — [취소]로 지우고 다시 한다.");
            return;
        }

        Vector3 dir = needleDir[mi * 2 + kind];
        var m = new RomRecordMark
        {
            raw = c + dir * r,
            byNeedle = true,
            dialDeg = Vector3.Angle(z, dir),
            // ★바늘은 사용자가 <b>고른다</b> — 점찍기처럼 순서로 정하지 않는다.
            passive = kind == NeedlePress,
            // ★좌우는 바늘이 가리키는 쪽으로 본다 — 미간이 없어도 갈린다.
            side = HasSides(step) ? RomRecordGeometry.SideOfDirection(dir, yaw, flipSides) : 0,
        };
        list.Add(m);
        Play(sndPinch);
        LogMark(step, m);
        dirty = true;
    }

    private void OnPinchFixed(Vector3 p)
    {
        switch (step)
        {
            case RomRecordStep.Setup:
                Play(sndDeny);
                // ★잡을 만큼 가까이 가지 않았다는 뜻이다(09-21). 3축 근처에서 오므리면 잡혀서 따라온다.
                Debug.Log($"[실측기록] 기준선을 못 잡았다 — 3축 중심에서 {axesGrabRadius * 100f:F0}cm 안에서 오므려야 잡힌다.");
                return;

            case RomRecordStep.Landmarks:
                if (landmarkTarget == 0)
                {
                    c7 = p; hasC7 = true;
                    landmarkTarget = 1;                       // 대추 다음은 미간
                    ApplyStepButtons();
                    Play(sndPinch);
                    Debug.Log($"[실측기록] 대추 {Fmt(c7)}");
                }
                else if (landmarkTarget == 1)
                {
                    glab = p; hasGlab = true;
                    Play(sndPinch);
                    Debug.Log($"[실측기록] 미간(중립) {Fmt(glab)}");
                }
                else { Play(sndDeny); Debug.Log("[실측기록] 대상이 목 중앙일 때는 앞·뒤 버튼으로 옮긴다(찍지 않는다)."); }
                dirty = true;
                return;

            case RomRecordStep.Done:
                return;
        }

        var list = marks[MotionIndex(step)];
        if (list.Count >= Capacity(step))
        {
            Play(sndDeny);
            Debug.Log($"[실측기록] {StepTitle[(int)step]}은 {Capacity(step)}개까지다 — [취소]로 지우고 다시 찍는다.");
            return;
        }

        var m = new RomRecordMark
        {
            raw = p,
            passive = list.Count % 2 == 1,               // 홀수번째(1,3) 능동 · 짝수번째(2,4) 수동
            side = HasSides(step) ? RomRecordGeometry.Side(glab, p, yaw, flipSides) : 0,
        };
        list.Add(m);
        Play(sndPinch);
        LogMark(step, m);
        dirty = true;
    }

    private void AdjustLast(float d)
    {
        if (!IsMotion(step)) return;
        var list = marks[MotionIndex(step)];
        if (list.Count == 0) return;
        var m = list[list.Count - 1];
        // ★바늘로 맞춘 것은 ±1°로 고치지 않는다 — 바늘을 다시 잡아 맞추는 것이 그 방식의 수정이다.
        if (m.byNeedle)
        {
            Play(sndDeny);
            Debug.Log("[실측기록] 바늘로 기록한 것은 ±1°로 못 고친다 — [취소]하고 바늘을 다시 맞춘다.");
            return;
        }
        if (!hasGlab)
        {
            Play(sndDeny);
            Debug.Log("[실측기록] 미간이 없어 각이 없다 — ±1°로 고칠 것이 없다.");
            return;
        }
        float before = AngleOf(step, m, Pivot);
        m.adjustDeg += d;
        // ★0° 아래로는 안 간다 — 중립을 넘어 반대쪽으로 도는 것은 수정이 아니다.
        float raw = RomRecordGeometry.PlaneAngle(Pivot, glab, m.raw, RomRecordGeometry.PlaneNormal(step, yaw));
        if (raw + m.adjustDeg < 0f) m.adjustDeg = -raw;
        Debug.Log($"[실측기록] 수정 {StepTitle[(int)step]} {Kind(m)} {before:F1}° → {AngleOf(step, m, Pivot):F1}° (누적 {m.adjustDeg:+0;-0;0}°)");
    }

    private void UndoLast()
    {
        if (!IsMotion(step)) return;
        var list = marks[MotionIndex(step)];
        if (list.Count == 0) return;
        var m = list[list.Count - 1];
        list.RemoveAt(list.Count - 1);
        Debug.Log($"[실측기록] 취소 {StepTitle[(int)step]} {Kind(m)}");
    }

    // ── 기록 ─────────────────────────────────────────────────────────
    private static string Kind(RomRecordMark m)
    {
        string side = m.side > 0 ? "우 " : m.side < 0 ? "좌 " : "";
        // ★바늘로 맞춘 것은 표시에서도 구분한다 — 점찍기와 섞이면 비교를 못 한다.
        return side + (m.passive ? "압박" : "능동") + (m.byNeedle ? "·바늘" : "");
    }

    private void LogMark(RomRecordStep s, RomRecordMark m)
    {
        string how = m.byNeedle ? "바늘" : "점";
        if (!hasGlab)
        {
            // ★미간이 없으면 중립선이 없어 중립 기준 각을 못 낸다 — 0을 각인 것처럼 적지 않는다.
            Debug.Log($"[실측기록] 기록 {StepTitle[(int)s]} {Kind(m)} ({how}) 눈금 {m.dialDeg:F1}° · " +
                      $"★미간이 없어 중립 기준 각은 못 낸다 · 자리 {Fmt(m.raw)}");
            return;
        }
        float atPivot = AngleOf(s, m, Pivot);
        float atC7 = Mathf.Max(0f, RomRecordGeometry.PlaneAngle(c7, glab, m.raw, RomRecordGeometry.PlaneNormal(s, yaw)) + m.adjustDeg);
        // ★두 기준을 같이 남긴다 — 대추 기준과 목 중앙 기준 중 어느 쪽이 실측에 가까운지 나중에 판별한다(사용자 09-18).
        Debug.Log($"[실측기록] 기록 {StepTitle[(int)s]} {Kind(m)} ({how}) {atPivot:F1}° (목중앙 {neckOffsetMm:F0}mm 기준) · " +
                  $"대추 기준 {atC7:F1}°{(m.byNeedle ? $" · 눈금 {m.dialDeg:F1}°" : "")} · " +
                  $"자리 {Fmt(m.raw)} · 수정 {m.adjustDeg:+0;-0;0}°");
    }

    private void LogSummary()
    {
        sb.Clear();
        sb.Append("[실측기록] 요약 — 목 중앙 보정 ").Append(neckOffsetMm.ToString("F0")).Append("mm\n");
        for (RomRecordStep s = RomRecordStep.Flexion; s <= RomRecordStep.Rotation; s++)
        {
            sb.Append("  ").Append(StepTitle[(int)s]).Append(": ");
            var list = marks[MotionIndex(s)];
            if (list.Count == 0) sb.Append("—");
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(" · ");
                var mk = list[i];
                sb.Append(Kind(mk)).Append(' ');
                if (mk.byNeedle) sb.Append("눈금 ").Append(mk.dialDeg.ToString("F1")).Append('°');
                if (!hasGlab)
                {
                    if (!mk.byNeedle) sb.Append('—');
                    continue;
                }
                float c7a = Mathf.Max(0f, RomRecordGeometry.PlaneAngle(c7, glab, mk.raw, RomRecordGeometry.PlaneNormal(s, yaw)) + mk.adjustDeg);
                sb.Append(mk.byNeedle ? " / " : "").Append(AngleOf(s, mk, Pivot).ToString("F1"))
                  .Append("°(대추 ").Append(c7a.ToString("F1")).Append("°)");
            }
            sb.Append('\n');
        }
        // ★조용히 버려진 핀치를 여기서 한 번 센다(09-21) — 얼마나 걸러졌는지 판마다 남는다.
        sb.Append("  무시된 핀치: 너무 짧음 ").Append(hands.IgnoredShort)
          .Append(" · 추적 신뢰 낮음 ").Append(hands.IgnoredLowConfidence).Append('\n');
        if (pinchBlockCount.Count > 0)
        {
            sb.Append("  핀치를 막은 것(프레임 수):");
            foreach (var kv in pinchBlockCount) sb.Append(' ').Append(kv.Key).Append(' ').Append(kv.Value).Append(" ·");
            sb.Append('\n');
        }
        Debug.Log(sb.ToString());
    }

    private static string Fmt(Vector3 v) => $"({v.x:F3}, {v.y:F3}, {v.z:F3})";

    // ── 그리기(값이 바뀔 때만) ────────────────────────────────────────
    private void Redraw()
    {
        Vector3 pivot = Pivot;
        view.SetFrame(pivot, yaw, axisLength);
        view.SetLandmarks(hasC7, c7, hasGlab, glab, pivot);

        // 손목 판 머리줄 — 지금까지 찍은 값(값이 바뀔 때만)
        string head = MenuHeader();
        leftMenu.SetHeader(head);
        rightMenu.SetHeader(head);

        // ★각도기와 바늘은 같은 기하를 쓴다(DialFrame) — 따로 계산하면 미리보기가 거짓말을 한다(규칙 9).
        if (DialFrame(out Vector3 dc, out Vector3 dn, out Vector3 dz, out float dr))
            view.SetDial(true, dc, dn, dz, dr);
        else
            view.SetDial(false, Vector3.zero, Vector3.up, Vector3.forward, 0.1f);
        DrawNeedle();

        var list = IsMotion(step) ? marks[MotionIndex(step)] : null;
        for (int i = 0; i < RomRecordVisual.MaxMarks; i++)
        {
            bool on = list != null && i < list.Count;
            if (!on) { view.SetMark(i, false, Vector3.zero, Vector3.zero, null, Color.white); continue; }
            var m = list[i];
            Vector3 n = RomRecordGeometry.PlaneNormal(step, yaw);
            // ★미간이 없으면 중립선이 없어 돌릴 기준도 없다 — 찍힌 자리를 그대로 둔다.
            Vector3 shown = hasGlab ? RomRecordGeometry.Adjusted(pivot, glab, m.raw, n, m.adjustDeg) : m.raw;
            // ★바늘은 <b>눈금 각</b>을 보여 준다 — 사용자가 눈으로 읽어 맞춘 값이 그것이다.
            string deg = m.byNeedle ? m.dialDeg.ToString("F0") + "°"
                       : hasGlab ? AngleOf(step, m, pivot).ToString("F0") + "°"
                       : "—";
            string label = Kind(m) + " " + deg;
            view.SetMark(i, true, shown, pivot, label, m.passive ? RomRecordVisual.PassiveColor : RomRecordVisual.ActiveColor);
        }

        view.SetPanel(pivot + Vector3.up * (axisLength + 0.18f), PanelText(pivot));
    }

    private string PanelText(Vector3 pivot)
    {
        sb.Clear();
        sb.Append('[').Append((int)step + 1).Append("/7] ").Append(StepTitle[(int)step]).Append('\n');
        switch (step)
        {
            case RomRecordStep.Setup:
                sb.Append("축을 손으로 잡아 끌어 목 높이에 두고, ↺↻로 '환자 앞'을 맞춘 뒤 [다음]\n");
                break;
            case RomRecordStep.Landmarks:
                sb.Append(!hasC7 ? "대추에 핀치하세요\n" : !hasGlab ? "미간(중립)에 핀치하세요\n" : "점을 잡아 끌거나 버튼으로 다듬고 [다음]\n");
                sb.Append(landmarkTarget == 0 ? "대상: 대추" : landmarkTarget == 1 ? "대상: 미간" : "대상: 목중앙").Append(" · 목 중앙 보정 ").Append(neckOffsetMm.ToString("F0")).Append("mm\n");
                break;
            case RomRecordStep.Done:
                sb.Append("기록을 마쳤습니다\n");
                break;
            default:
                var list = marks[MotionIndex(step)];
                int next = list.Count;
                if (next >= Capacity(step)) sb.Append("다 찍었습니다 — [다음]\n");
                else
                {
                    sb.Append(next % 2 == 0 ? "능동" : "압박").Append(' ').Append(next + 1).Append('/').Append(Capacity(step));
                    // ★두 길을 나란히 알린다(09-21) — 어느 쪽으로 남길지는 사용자가 고른다.
                    sb.Append(needleOn ? " — 바늘 끝을 잡아 맞추고 [바늘 기록], 또는 미간에 핀치\n"
                                       : " — 위치의 미간에 핀치하세요\n");
                }
                if (!hasGlab) sb.Append("★미간 없음 — 바늘 눈금 각만 기록됩니다\n");
                break;
        }
        sb.Append('\n');
        for (RomRecordStep s = RomRecordStep.Flexion; s <= RomRecordStep.Rotation; s++)
        {
            sb.Append(StepTitle[(int)s]).Append("  ");
            var list = marks[MotionIndex(s)];
            if (list.Count == 0) sb.Append('—');
            for (int i = 0; i < list.Count; i++)
            {
                sb.Append(i > 0 ? " · " : "").Append(Kind(list[i])).Append(' ');
                AppendDeg(sb, s, list[i], pivot);
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
