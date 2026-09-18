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

    [Header("=== 손목 판 ===")]
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
    private readonly RomRecordWristMenu leftMenu = new RomRecordWristMenu();
    private readonly RomRecordWristMenu rightMenu = new RomRecordWristMenu();
    private readonly RomRecordVisual view = new RomRecordVisual();
    private Transform eye;
    private bool placed;
    private bool dirty = true;
    private bool livePinchLeft;            // 지금 오므리고 있는 손(한 번에 하나만 받는다)
    private bool liveActive;
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

        view.textSize = textSize;
        view.panelSize = panelTextSize;
        view.Build(transform, font, mat);
        foreach (var menu in new[] { leftMenu, rightMenu })
        {
            menu.labelSize = buttonLabelSize;
            menu.headerSize = buttonLabelSize;
            menu.cellW = menuCellWidth;
            menu.rowH = menuRowHeight;
            menu.buttonH = menuButtonHeight;
            menu.lift = menuLift;
        }
        leftMenu.Build(transform, "왼손목 메뉴", 14, font, mat);
        rightMenu.Build(transform, "오른손목 메뉴", 14, font, mat);
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

    /// <summary>
    /// 단계별 판 배치(09-18 사용자 결정 "그룹 판넬"). 판은 6칸 너비 · 0행은 머리줄.
    /// 머리줄: 단계·진행 + [나가기](오른쪽 끝) / 가운데: 그 단계의 조작 묶음 / 맨 아래: [◀ 이전] [다음 ▶].
    /// </summary>
    private void ApplyStepButtons()
    {
        var items = new List<RomMenuItem>(14);
        items.Add(RomMenuItem.Button("exit", "나가기", 4.6f, 0, 1.4f, ExitTint));
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
                navRow = 3;
                break;
        }

        if (step > RomRecordStep.Setup) items.Add(RomMenuItem.Button("prev", "◀ 이전", 0f, navRow, 2.2f, NavTint));
        if (step < RomRecordStep.Done) items.Add(RomMenuItem.Button("next", "다음 ▶", 3.8f, navRow, 2.2f, NavTint));

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
                sb.Append(i == 0 ? "   " : " · ").Append(Kind(list[i])).Append(" ").Append(AngleOf(step, list[i], pivot).ToString("F0")).Append("°");
        }
        else if (step == RomRecordStep.Landmarks && landmarkTarget == 2)
        {
            sb.Append("   목중앙 ").Append(neckOffsetMm.ToString("F0")).Append("mm");
        }
        return sb.ToString();
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
        if (IsMotion(s) && (!hasC7 || !hasGlab))
        {
            Play(sndDeny);
            Debug.Log("[실측기록] 대추와 미간(중립)을 먼저 찍는다 — 동작 단계로 못 넘어간다.");
            return;
        }
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
        bool interacting = menuHoldL || menuHoldR || Time.unscaledTime - lastPressTime < 0.4f;
        bool nearMenu = hands.TryJoint(isLeft, HandJointId.HandIndexTip, out Vector3 tip)
                        && (leftMenu.Near(tip, 0.06f) || rightMenu.Near(tip, 0.06f));

        int ev = hands.UpdatePinch(isLeft, otherBusy || nearMenu || interacting, out Vector3 fixedPos);
        var st = isLeft ? hands.LeftPinch : hands.RightPinch;

        if (ev == 1)
        {
            liveActive = true;
            livePinchLeft = isLeft;
        }
        if (liveActive && livePinchLeft == isLeft)
        {
            if (st.closed) view.SetLive(true, st.current);
            if (ev == 2)
            {
                liveActive = false;
                view.SetLive(false, Vector3.zero);
                OnPinchFixed(fixedPos);
            }
            else if (ev == 3 || ev == 4)
            {
                liveActive = false;
                view.SetLive(false, Vector3.zero);
                if (ev == 3) Debug.Log("[실측기록] 핀치 취소 — 손을 놓쳤거나 시스템 제스처가 시작됐다.");
            }
        }
    }

    private void OnPinchFixed(Vector3 p)
    {
        switch (step)
        {
            case RomRecordStep.Setup:
                Play(sndDeny);
                Debug.Log("[실측기록] 기준선 세팅 중에는 찍지 않는다 — [다음]으로 넘어간 뒤 대추를 찍는다.");
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
        return side + (m.passive ? "수동" : "능동");
    }

    private void LogMark(RomRecordStep s, RomRecordMark m)
    {
        float atPivot = AngleOf(s, m, Pivot);
        float atC7 = Mathf.Max(0f, RomRecordGeometry.PlaneAngle(c7, glab, m.raw, RomRecordGeometry.PlaneNormal(s, yaw)) + m.adjustDeg);
        // ★두 기준을 같이 남긴다 — 대추 기준과 목 중앙 기준 중 어느 쪽이 실측에 가까운지 나중에 판별한다(사용자 09-18).
        Debug.Log($"[실측기록] 기록 {StepTitle[(int)s]} {Kind(m)} {atPivot:F1}° (목중앙 {neckOffsetMm:F0}mm 기준) · 대추 기준 {atC7:F1}° · " +
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
                float c7a = Mathf.Max(0f, RomRecordGeometry.PlaneAngle(c7, glab, list[i].raw, RomRecordGeometry.PlaneNormal(s, yaw)) + list[i].adjustDeg);
                sb.Append(Kind(list[i])).Append(' ').Append(AngleOf(s, list[i], Pivot).ToString("F1"))
                  .Append("°(대추 ").Append(c7a.ToString("F1")).Append("°)");
            }
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

        bool dial = IsMotion(step) && hasGlab;
        if (dial)
        {
            Vector3 n = RomRecordGeometry.PlaneNormal(step, yaw);
            float radius = Mathf.Max(0.08f, Vector3.ProjectOnPlane(glab - pivot, n).magnitude);
            // 회전 눈금판은 미간 높이의 수평면에 둔다 — 회전 중심 높이에 두면 머리에 가려 안 보인다.
            Vector3 center = step == RomRecordStep.Rotation ? new Vector3(pivot.x, glab.y, pivot.z) : pivot;
            view.SetDial(true, center, n, RomRecordGeometry.ScaleZero(step, yaw), radius);
        }
        else view.SetDial(false, Vector3.zero, Vector3.up, Vector3.forward, 0.1f);

        var list = IsMotion(step) ? marks[MotionIndex(step)] : null;
        for (int i = 0; i < RomRecordVisual.MaxMarks; i++)
        {
            bool on = list != null && i < list.Count;
            if (!on) { view.SetMark(i, false, Vector3.zero, Vector3.zero, null, Color.white); continue; }
            var m = list[i];
            Vector3 n = RomRecordGeometry.PlaneNormal(step, yaw);
            Vector3 shown = RomRecordGeometry.Adjusted(pivot, glab, m.raw, n, m.adjustDeg);
            string label = Kind(m) + " " + AngleOf(step, m, pivot).ToString("F0") + "°";
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
                sb.Append("Y◀▶로 '환자 앞'을 환자 정면에, 높이▲▼로 목 높이에 맞춘 뒤 [다음]\n");
                break;
            case RomRecordStep.Landmarks:
                sb.Append(!hasC7 ? "대추에 핀치하세요\n" : !hasGlab ? "미간(중립)에 핀치하세요\n" : "위치를 버튼으로 다듬고 [다음]\n");
                sb.Append(landmarkTarget == 0 ? "대상: 대추" : landmarkTarget == 1 ? "대상: 미간" : "대상: 목중앙").Append(" · 목 중앙 보정 ").Append(neckOffsetMm.ToString("F0")).Append("mm\n");
                break;
            case RomRecordStep.Done:
                sb.Append("기록을 마쳤습니다\n");
                break;
            default:
                var list = marks[MotionIndex(step)];
                int next = list.Count;
                if (next >= Capacity(step)) sb.Append("다 찍었습니다 — [다음]\n");
                else sb.Append(next % 2 == 0 ? "능동" : "수동").Append(" 위치의 미간에 핀치하세요 (")
                       .Append(next + 1).Append('/').Append(Capacity(step)).Append(")\n");
                break;
        }
        sb.Append('\n');
        for (RomRecordStep s = RomRecordStep.Flexion; s <= RomRecordStep.Rotation; s++)
        {
            sb.Append(StepTitle[(int)s]).Append("  ");
            var list = marks[MotionIndex(s)];
            if (list.Count == 0) sb.Append('—');
            for (int i = 0; i < list.Count; i++)
                sb.Append(i > 0 ? " · " : "").Append(Kind(list[i])).Append(' ').Append(AngleOf(s, list[i], pivot).ToString("F0")).Append('°');
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
