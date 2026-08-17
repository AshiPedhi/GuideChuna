using UnityEngine;

/// <summary>
/// 척추를 <b>검지·중지로 두방에서 족방까지 한 번 훑는</b> 촉지 진단 판정 (표시 + 판정).
///
/// ■ 판정 = <b>손끝 위치만</b> 본다. 콜라이더도 파지점도 쓰지 않는다.
/// 핸드트래킹이 <c>HandIndexTip</c>·<c>HandMiddleTip</c>을 Transform으로 매 프레임 주므로,
/// 두 손끝의 <b>중점</b>을 시작→끝 선분에 투영하면 진행도가 그대로 나온다:
/// <code>
///   mid  = (검지끝 + 중지끝) / 2
///   seg  = 끝점 - 시작점
///   t    = Dot(mid - 시작점, seg) / |seg|²   → 0~1   ← 진행도
///   dist = |mid - (시작점 + seg·t)|                  ← 척추선에서 벗어난 거리
/// </code>
///
/// ★<b>접촉 판정을 쓰지 않는 이유</b>(2026-08-18 사용자 판단): 접촉점(<see cref="GripPointTarget"/>)은
/// 컨트롤러가 <c>expectedFingerCollider</c>를 주입해 줘야만 동작한다. 그 배선이 하나 빠지면
/// <c>IsTouched</c>가 영영 false가 되어 <b>판정이 조용히 죽는다.</b> 손끝 위치는 그 의존성이 없다.
///
/// ■ 흐름
///   ① 손끝 중점이 <b>시작 밴드</b>(<see cref="startBand"/>) 안에 들어오면 출발 성립
///   ② 진행도 t를 <b>단조롭게</b> 누적 — 되올라가거나 선에서 벗어나면 처음부터
///   ③ <see cref="requiredCoverage"/>까지 내려오면 완료
///
/// ■ 표시
/// 시작점·끝점에 각각 검지·중지 자리 구 2개(총 4개)를 놓아 어디에 손가락을 얹을지 보여 주고,
/// 두 점을 잇는 선이 진행도만큼 색이 찬다. <b>구는 표시 전용이다 — 콜라이더가 없다.</b>
/// </summary>
[DisallowMultipleComponent]
public class SpineGlideGuide : MonoBehaviour
{
    /// <summary>진행도를 재는 손.</summary>
    public enum GlideHand { 어느손이든, 왼손, 오른손 }

    [Header("=== 구간 (두방 → 족방) ===")]
    [Tooltip("★시작점 = 두방(머리쪽). 환자를 따라 움직이도록 흉추 본 하위에 두세요.")]
    [SerializeField] private Transform startPoint;

    [Tooltip("★끝점 = 족방(발쪽).")]
    [SerializeField] private Transform endPoint;

    [Header("=== 판정 ===")]
    [Tooltip("진행도를 잴 손. 복와위 하부흉추는 왼손이다.")]
    [SerializeField] private GlideHand hand = GlideHand.왼손;

    [Tooltip("진행도를 잴 손가락 2개. 두 손끝의 중점을 척추선에 투영해 잰다.")]
    [SerializeField] private CranialFinger fingerA = CranialFinger.Index;
    [SerializeField] private CranialFinger fingerB = CranialFinger.Middle;

    [Tooltip("두 손끝이 이 거리(m) 이내여야 '모아서 훑는다'로 본다. 기본 6cm.")]
    [SerializeField, Range(0.01f, 0.15f)] private float fingerGap = 0.06f;

    [Tooltip("손끝 중점이 척추선에서 이 거리(m) 이내여야 진행으로 친다. 기본 5cm.\n" +
             "손이 등에 가리면 트래킹 추정값이 흔들리므로 너무 작게 잡으면 자꾸 끊긴다.")]
    [SerializeField, Range(0.01f, 0.2f)] private float maxDistanceToAxis = 0.05f;

    [Tooltip("이 비율 안(시작점 기준)에서 출발해야 인정한다. 0.15 = 위쪽 15%.\n" +
             "중간부터 훑는 것을 출발로 치지 않기 위한 값이다.")]
    [SerializeField, Range(0.02f, 0.5f)] private float startBand = 0.15f;

    [Tooltip("이 비율까지 내려오면 완료. 0.85 = 85%.\n" +
             "1.0으로 두면 끝점에 정확히 닿아야 해서 트래킹 오차로 잘 안 끝난다.")]
    [SerializeField, Range(0.5f, 1f)] private float requiredCoverage = 0.85f;

    [Tooltip("선에서 벗어나거나 손이 안 잡혀도 이 시간(초)까지는 진행도를 지킨다(트래킹 튐 유예).")]
    [SerializeField, Range(0f, 2f)] private float graceSeconds = 0.4f;

    [Tooltip("이 비율보다 크게 되올라가면 처음부터 다시. 0.2 = 구간의 20%.")]
    [SerializeField, Range(0.05f, 0.6f)] private float backtrackTolerance = 0.2f;

    [Header("=== 표시 (전부 선택) ===")]
    [Tooltip("시작점↔끝점을 잇는 선. 진행도만큼 색이 찬다.")]
    [SerializeField] private LineRenderer pathVisual;
    [SerializeField] private Color pendingColor = new Color(1f, 0.86f, 0.35f, 1f);
    [SerializeField] private Color movingColor = new Color(0.35f, 0.75f, 1f, 1f);
    [SerializeField] private Color doneColor = new Color(0.149f, 1f, 0.318f, 1f);

    [Tooltip("시작점의 검지·중지 자리 구. 표시 전용(콜라이더 없음).")]
    [SerializeField] private Transform startMarkerA;
    [SerializeField] private Transform startMarkerB;

    [Tooltip("끝점의 검지·중지 자리 구. 출발이 성립한 뒤에 켜진다.")]
    [SerializeField] private Transform endMarkerA;
    [SerializeField] private Transform endMarkerB;

    [Tooltip("한 쌍의 두 구 사이 간격(m). 검지·중지를 모아 얹는 폭이라 2cm 정도.")]
    [SerializeField, Range(0.005f, 0.06f)] private float markerGap = 0.02f;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool debugLog = false;

    private CranialAdjustmentController rig;
    private bool running;
    private bool completed;
    private bool started;           // 시작 밴드에서 출발했는가
    private float progress;         // 지금까지 내려온 최대 t (0~1)
    private float offAxisSince = -1f;

    /// <summary>0~1 진행도(완주 비율 기준). HUD·로그용.</summary>
    public float Progress01 =>
        completed ? 1f : !started ? 0f : Mathf.Clamp01(progress / Mathf.Max(0.01f, requiredCoverage));

    /// <summary>쓸어내림이 완료됐는가.</summary>
    public bool Completed => completed;

    /// <summary>구간이 배선돼 있는가.</summary>
    public bool HasSegment => startPoint != null && endPoint != null;

    private void Awake()
    {
        rig = GetComponentInParent<CranialAdjustmentController>(true);
        if (rig == null) rig = FindFirstObjectByType<CranialAdjustmentController>(FindObjectsInactive.Include);
    }

    private void OnEnable()
    {
        var ev = ScenarioEventSystem.Instance;
        if (ev != null) ev.OnSubStepStarted += HandleSubStepStarted;
    }

    private void OnDisable()
    {
        var ev = ScenarioEventSystem.Instance;
        if (ev != null) ev.OnSubStepStarted -= HandleSubStepStarted;
        StopGlide();
    }

    /// <summary>쓸어내리기 단계를 벗어나면 표시를 끈다(다음 단계까지 남지 않게).</summary>
    private void HandleSubStepStarted(SubStepData subStep)
    {
        bool mine = subStep != null && !string.IsNullOrWhiteSpace(subStep.conditionType) &&
                    subStep.conditionType.Trim().Equals("cranialGlide", System.StringComparison.OrdinalIgnoreCase);
        if (!mine) StopGlide();
    }

    /// <summary>판정 시작(조건 첫 폴 시점에 호출).</summary>
    public void BeginGlide()
    {
        running = true;
        completed = false;
        started = false;
        progress = 0f;
        offAxisSince = -1f;
        Redraw();

        if (!HasSegment)
            ChunaLogger.LogWarning($"[SpineGlideGuide] {name} — 시작점·끝점이 비어 있어 쓸어내림을 잴 수 없습니다. " +
                                   "메뉴 `GuideChuna/척추 쓸어내리기 구간 만들기`로 만드세요.");
        else if (debugLog)
            ChunaLogger.Log($"[SpineGlideGuide] 판정 시작 — {hand} {fingerA}+{fingerB}, " +
                            $"구간 {SegmentLength * 100f:F0}cm, 완주 {requiredCoverage * 100f:F0}%");
    }

    /// <summary>판정 종료(단계 이탈). 표시 정리.</summary>
    public void StopGlide()
    {
        running = false;
        if (pathVisual != null) pathVisual.enabled = false;
        Show(startMarkerA, false);
        Show(startMarkerB, false);
        Show(endMarkerA, false);
        Show(endMarkerB, false);
    }

    private static void Show(Transform t, bool on)
    {
        if (t != null && t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
    }

    private float SegmentLength =>
        HasSegment ? Vector3.Distance(startPoint.position, endPoint.position) : 0f;

    private void Update()
    {
        if (!running || completed || !HasSegment) return;

        if (TrySample(out float t, out float offAxis) && offAxis <= maxDistanceToAxis)
        {
            offAxisSince = -1f;
            Advance(t);
        }
        else
        {
            // 손을 뗐거나 선에서 벗어남 — 유예가 지나면 처음부터.
            if (offAxisSince < 0f) offAxisSince = Time.time;
            else if (Time.time - offAxisSince > graceSeconds && (started || progress > 0f))
            {
                if (debugLog) ChunaLogger.Log("[SpineGlideGuide] 경로 이탈 — 처음부터 다시");
                started = false;
                progress = 0f;
            }
        }

        Redraw();
    }

    /// <summary>진행도 갱신. 시작 밴드에서 출발했을 때만 누적한다.</summary>
    private void Advance(float t)
    {
        if (!started)
        {
            if (t > startBand) return;      // 중간부터 훑는 것은 출발로 치지 않는다
            started = true;
            progress = t;
            ChunaLogger.Log($"[SpineGlideGuide] 두방 출발 (t={t:F2}) — 족방까지 훑으세요");
            return;
        }

        if (t + backtrackTolerance < progress)
        {
            ChunaLogger.Log($"[SpineGlideGuide] 되올라감(t={t:F2} < {progress:F2}) — 처음부터 다시");
            started = false;
            progress = 0f;
            return;
        }

        if (t > progress) progress = t;

        if (progress >= requiredCoverage)
        {
            completed = true;
            ChunaLogger.Log($"[SpineGlideGuide] 쓸어내림 완료 — {progress * 100f:F0}% 구간 통과");
        }
    }

    /// <summary>두 손끝이 모여 있으면 그 중점의 진행도 t와 선분까지 거리를 낸다.</summary>
    private bool TrySample(out float t, out float offAxis)
    {
        t = 0f;
        offAxis = float.MaxValue;
        if (rig == null) return false;

        bool ok = false;
        if (hand != GlideHand.오른손)
            ok |= TryHand(CranialAdjustmentController.JudgeHand.왼손, ref t, ref offAxis);
        if (hand != GlideHand.왼손)
            ok |= TryHand(CranialAdjustmentController.JudgeHand.오른손, ref t, ref offAxis);
        return ok;
    }

    /// <summary>한 손을 재서 더 가까운 표본이면 채택한다.</summary>
    private bool TryHand(CranialAdjustmentController.JudgeHand which, ref float t, ref float offAxis)
    {
        Transform a = rig.GetFingertip(which, fingerA);
        Transform b = rig.GetFingertip(which, fingerB);
        if (a == null || b == null) return false;
        if (Vector3.Distance(a.position, b.position) > fingerGap) return false;   // 손가락을 모아야 한다

        Vector3 mid = (a.position + b.position) * 0.5f;
        Vector3 s = startPoint.position;
        Vector3 seg = endPoint.position - s;
        float len2 = seg.sqrMagnitude;
        if (len2 < 1e-6f) return false;

        float proj = Mathf.Clamp01(Vector3.Dot(mid - s, seg) / len2);
        float dist = Vector3.Distance(mid, s + seg * proj);
        if (dist >= offAxis) return false;    // 이미 더 가까운 손이 있다

        t = proj;
        offAxis = dist;
        return true;
    }

    /// <summary>선과 자리 표시 구를 갱신한다.</summary>
    private void Redraw()
    {
        if (!HasSegment) return;

        if (pathVisual != null)
        {
            pathVisual.enabled = running;
            pathVisual.useWorldSpace = true;
            pathVisual.positionCount = 2;
            pathVisual.SetPosition(0, startPoint.position);
            pathVisual.SetPosition(1, endPoint.position);
            pathVisual.startColor = pathVisual.endColor =
                completed ? doneColor
                : started ? Color.Lerp(movingColor, doneColor, Progress01)
                : pendingColor;
        }

        // 축과 수직으로 벌린다 — 축 방향으로 놓으면 '모아서 얹는다'가 아니라 '따라 짚는다'로 읽힌다.
        Vector3 axis = (endPoint.position - startPoint.position).normalized;
        Vector3 side = Vector3.Cross(axis, Vector3.up);
        if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(axis, Vector3.forward);
        side = side.normalized * (markerGap * 0.5f);

        // 시작 자리는 출발 전까지, 끝 자리는 출발한 뒤에 — 지금 어디를 봐야 하는지가 흐려지지 않게.
        Place(startMarkerA, startPoint.position + side, running && !started);
        Place(startMarkerB, startPoint.position - side, running && !started);
        Place(endMarkerA, endPoint.position + side, running && started && !completed);
        Place(endMarkerB, endPoint.position - side, running && started && !completed);
    }

    private static void Place(Transform t, Vector3 pos, bool on)
    {
        if (t == null) return;
        Show(t, on);
        if (on) t.position = pos;
    }

    /// <summary>씬 뷰에서 구간을 눈으로 맞추기 위한 것 — 두방(파랑) → 족방(빨강).</summary>
    private void OnDrawGizmos()
    {
        if (!HasSegment) return;
        Gizmos.color = new Color(0.3f, 0.6f, 1f);
        Gizmos.DrawSphere(startPoint.position, 0.012f);
        Gizmos.color = new Color(1f, 0.4f, 0.3f);
        Gizmos.DrawSphere(endPoint.position, 0.012f);
        Gizmos.color = new Color(1f, 0.86f, 0.35f);
        Gizmos.DrawLine(startPoint.position, endPoint.position);
    }

    /// <summary>배선·구간 상태 요약(점검용).</summary>
    public string DescribeSegment() =>
        !HasSegment
            ? "★시작점·끝점 미배선"
            : $"두방 '{startPoint.name}' → 족방 '{endPoint.name}' · 구간 {SegmentLength * 100f:F0}cm · " +
              $"{hand} {fingerA}+{fingerB} · 완주 {requiredCoverage * 100f:F0}% · 이탈 {maxDistanceToAxis * 100f:F0}cm";

    /// <summary>에디터 도구가 만든 구간을 연결한다.</summary>
    public void SetSegment(Transform head, Transform foot)
    {
        startPoint = head;
        endPoint = foot;
    }

    /// <summary>에디터 도구가 만든 자리 표시 구 4개를 연결한다.</summary>
    public void SetMarkers(Transform sA, Transform sB, Transform eA, Transform eB)
    {
        startMarkerA = sA;
        startMarkerB = sB;
        endMarkerA = eA;
        endMarkerB = eB;
    }

    /// <summary>도구가 만든 선을 연결한다.</summary>
    public void SetPathVisual(LineRenderer line) => pathVisual = line;
}
