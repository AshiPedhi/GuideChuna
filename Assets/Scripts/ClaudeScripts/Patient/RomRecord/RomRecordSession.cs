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
    [Tooltip("축 끝 글자·마커 각도 글자. ★기존 각도기 기준: 축·눈금 0.03 · 판독 0.05(기기 확인값).\n" +
             "09-18 첫 판은 1.1이라 20~30배 컸다.")]
    [SerializeField] private float textSize = 0.03f;
    [Tooltip("안내판(단계·지시·기록 요약) 글자.")]
    [SerializeField] private float panelTextSize = 0.028f;
    [Tooltip("손목 버튼 글자.")]
    [SerializeField] private float buttonLabelSize = 0.026f;
    [Tooltip("손목 버튼 지름(m).")]
    [SerializeField] private float buttonSize = 0.018f;
    [Tooltip("손목 버튼 사이 간격(m).")]
    [SerializeField] private float buttonGap = 0.034f;
    [Tooltip("손목 메뉴를 손목에서 위로 띄우는 높이(m).")]
    [SerializeField] private float menuLift = 0.06f;

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
            menu.buttonSize = buttonSize;
            menu.gap = buttonGap;
            menu.lift = menuLift;
            menu.pressRadius = buttonSize * 0.75f;   // 버튼을 키우면 누르는 범위도 같이
        }
        leftMenu.Build(transform, "왼손목 메뉴", 10, font, mat);
        rightMenu.Build(transform, "오른손목 메뉴", 10, font, mat);
        ApplyStepButtons();

        Debug.Log("[실측기록] 시작 — 기준선 세팅부터. 좌우 뒤집기 " + (flipSides ? "켬" : "끔") + $" · 목 중앙 보정 {neckOffsetMm:F0}mm · " +
                  $"글자 {textSize}/{panelTextSize}/버튼 {buttonLabelSize} · 버튼 {buttonSize * 100f:F1}cm 간격 {buttonGap * 100f:F1}cm");
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
        leftMenu.SetVisible(lw);
        rightMenu.SetVisible(rw);
        if (lw) leftMenu.Place(lwp.position, eye);
        if (rw) rightMenu.Place(rwp.position, eye);

        // ★왼손목 메뉴는 오른 검지로, 오른손목 메뉴는 왼 검지로 누른다.
        bool rTip = hands.TryJoint(false, HandJointId.HandIndexTip, out Vector3 rIdx);
        bool lTip = hands.TryJoint(true, HandJointId.HandIndexTip, out Vector3 lIdx);
        string id = lw ? leftMenu.Poll(rIdx, rTip) : null;
        if (id == null && rw) id = rightMenu.Poll(lIdx, lTip);
        if (id != null) OnButton(id);
    }

    private void ApplyStepButtons()
    {
        string[] ids, labels;
        bool[] rep;
        Color[] tint;
        Color nav = new Color(0.55f, 0.85f, 1f), exit = new Color(1f, 0.45f, 0.4f), adj = new Color(0.8f, 0.95f, 0.5f);

        switch (step)
        {
            case RomRecordStep.Setup:
                ids = new[] { "yaw-", "yaw+", "h+", "h-", "next", "exit" };
                labels = new[] { "Y◀", "Y▶", "높이▲", "높이▼", "다음", "나가기" };
                rep = new[] { true, true, true, true, false, false };
                tint = new[] { adj, adj, adj, adj, nav, exit };
                break;
            case RomRecordStep.Landmarks:
                ids = new[] { "target", "up", "down", "fwd", "back", "left", "right", "prev", "next", "exit" };
                labels = new[] { TargetLabel(), "위", "아래", "앞", "뒤", "왼", "오른", "이전", "다음", "나가기" };
                rep = new[] { false, true, true, true, true, true, true, false, false, false };
                tint = new[] { new Color(1f, 0.85f, 0.3f), adj, adj, adj, adj, adj, adj, nav, nav, exit };
                break;
            case RomRecordStep.Done:
                ids = new[] { "prev", "exit" };
                labels = new[] { "이전", "나가기" };
                rep = new[] { false, false };
                tint = new[] { nav, exit };
                break;
            default:
                ids = new[] { "adj+", "adj-", "undo", "prev", "next", "exit" };
                labels = new[] { "+1°", "-1°", "취소", "이전", "다음", "나가기" };
                rep = new[] { true, true, false, false, false, false };
                tint = new[] { adj, adj, new Color(1f, 0.7f, 0.3f), nav, nav, exit };
                break;
        }
        leftMenu.SetButtons(ids, labels, rep, tint);
        rightMenu.SetButtons(ids, labels, rep, tint);
    }

    private string TargetLabel() => landmarkTarget == 0 ? "대상:대추" : landmarkTarget == 1 ? "대상:미간" : "대상:목중앙";

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
            case "target":
                landmarkTarget = (landmarkTarget + 1) % 3;
                leftMenu.SetLabel("target", TargetLabel());
                rightMenu.SetLabel("target", TargetLabel());
                break;
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

        // ★버튼을 누르려던 손이 핀치로 읽히지 않게, 검지가 메뉴 근처면 새로 시작하지 않는다.
        bool nearMenu = hands.TryJoint(isLeft, HandJointId.HandIndexTip, out Vector3 tip)
                        && (leftMenu.Near(tip, 0.06f) || rightMenu.Near(tip, 0.06f));

        int ev = hands.UpdatePinch(isLeft, otherBusy || nearMenu || (liveActive && livePinchLeft != isLeft), out Vector3 fixedPos);
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
            else if (ev == 3)
            {
                liveActive = false;
                view.SetLive(false, Vector3.zero);
                Debug.Log("[실측기록] 핀치 취소 — 손을 놓쳤거나 시스템 제스처가 시작됐다.");
            }
        }
    }

    private void OnPinchFixed(Vector3 p)
    {
        switch (step)
        {
            case RomRecordStep.Setup:
                Debug.Log("[실측기록] 기준선 세팅 중에는 찍지 않는다 — [다음]으로 넘어간 뒤 대추를 찍는다.");
                return;

            case RomRecordStep.Landmarks:
                if (landmarkTarget == 0)
                {
                    c7 = p; hasC7 = true;
                    landmarkTarget = 1;                       // 대추 다음은 미간
                    leftMenu.SetLabel("target", TargetLabel());
                    rightMenu.SetLabel("target", TargetLabel());
                    Debug.Log($"[실측기록] 대추 {Fmt(c7)}");
                }
                else if (landmarkTarget == 1)
                {
                    glab = p; hasGlab = true;
                    Debug.Log($"[실측기록] 미간(중립) {Fmt(glab)}");
                }
                else Debug.Log("[실측기록] 대상이 목 중앙일 때는 앞·뒤 버튼으로 옮긴다(찍지 않는다).");
                dirty = true;
                return;

            case RomRecordStep.Done:
                return;
        }

        var list = marks[MotionIndex(step)];
        if (list.Count >= Capacity(step))
        {
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
                sb.Append(TargetLabel()).Append(" · 목 중앙 보정 ").Append(neckOffsetMm.ToString("F0")).Append("mm\n");
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
