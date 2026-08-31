using UnityEngine;

/// <summary>
/// 경추 ROM 6방향을 목뼈에 직접 얹어 돌린다.
///
/// 애니메이션 클립으로 돌리면 두 가지가 걸린다 —
///  · 아바타 human 배열에 <b>CC_Base_NeckTwist02가 없어</b> 휴머노이드가 그 뼈를 모른다.
///    각도가 관절 2개에만 몰려 꺾이는 느낌이 난다(2026-08-24 실측: 전 클립에서 Neck2 = 0.0°).
///  · 각도를 근육값(-1~1)으로 다루게 돼 "45도"를 직접 지정할 수 없다.
///
/// 그래서 Animator가 포즈를 쓴 뒤(LateUpdate) 목뼈 3개에 회전을 <b>덧얹는다</b>.
/// 시작 자세(앉은 자세·팔)는 기존 대기 클립이 그대로 잡아 준다.
///
/// 진행은 두 구간으로 나뉜다.
///  · 능동  환자가 스스로 가는 데까지. 최대각에서 <see cref="dysfunctionAngle"/>만큼 못 미친다.
///  · 압박  시술자가 손으로 더 미는 구간. 능동 끝점부터 최대각까지.
/// </summary>
public class CervicalRomDriver : MonoBehaviour
{
    public enum Direction
    {
        None,
        Flexion,        // 굴곡
        Extension,      // 신전
        LateralRight,   // 우측굴
        LateralLeft,    // 좌측굴
        RotationRight,  // 우회전
        RotationLeft,   // 좌회전
    }

    [Header("=== 뼈 ===")]
    [Tooltip("기준 몸통. 회전축을 이 트랜스폼 기준으로 잡는다. 비우면 CC_Base_Spine02를 찾는다.")]
    [SerializeField] private Transform torso;

    [Tooltip("위에서부터 순서대로 목뼈. 비우면 CC_Base_NeckTwist01 / 02 / Head를 찾는다.")]
    [SerializeField] private Transform[] neckChain = new Transform[3];

    [Tooltip("각 뼈가 나눠 가질 비율. 합이 1이 되게 정규화한다.\n" +
             "아래쪽 뼈에 조금, 머리에 많이 주면 실제 경추 움직임에 가깝다.")]
    [SerializeField] private float[] boneWeights = { 0.30f, 0.25f, 0.45f };

    [Header("=== 각도 (임상 기준) ===")]
    [Tooltip("굴곡 최대 각도")] [SerializeField] private float flexionMax = 45f;
    [Tooltip("신전 최대 각도")] [SerializeField] private float extensionMax = 90f;
    [Tooltip("측굴 최대 각도 (좌우 공통)")] [SerializeField] private float lateralMax = 45f;
    [Tooltip("회전 최대 각도 (좌우 공통)")] [SerializeField] private float rotationMax = 90f;

    [Header("=== 환자의 제한 (측정 대상) ===")]
    // ★2026-08-25 재설계. 예전에는 '여유 구간' 하나로 능동 끝점과 압박 끝점을 동시에 정해서,
    //   압박을 끝까지 밀면 머리가 <b>언제나 최대각에 정확히 도달</b>했다.
    //   그러면 DeficitAngle이 항상 0이라 누구를 측정해도 '최대'이 나온다 — 측정 술기인데
    //   측정값에 변별력이 없었다. 그래서 두 값을 분리했다.
    //
    //     최대각 ─────────────────────────────────────────
    //     능동 한계 = 최대각 − 기능장애      환자가 스스로 가는 데까지
    //     압박 한계 = 능동 한계 + 압박 여유   밀어서 더 가는 데까지 (여기서 멈춘다)
    //     부족각   = 최대각 − 압박 한계      ★이게 기록할 값이다

    [Tooltip("최대각 대비 능동으로 못 가는 각(도). 기능장애의 정도다.\n" +
             "randomizePerLoad를 켜면 이 값 대신 아래 범위에서 방향마다 따로 뽑는다.")]
    [SerializeField] private float dysfunctionAngle = 15f;

    [Tooltip("능동 한계에서 압박으로 더 갈 수 있는 각(도). 사람마다 다른 끝느낌 한계다.\n" +
             "★손이 이보다 더 밀어도 머리는 여기서 멈춘다 — 그 저항이 끝느낌이다.")]
    [SerializeField] private float passiveGainAngle = 7f;

    [Tooltip("시나리오를 불러올 때마다 두 값을 방향별로 다시 뽑는다.\n" +
             "매번 같은 지점에서 멈추면 끝느낌을 느끼는 게 아니라 위치를 외우게 된다.\n" +
             "방향마다 값이 다르므로 좌우 비대칭도 자연스럽게 생긴다.")]
    [SerializeField] private bool randomizePerLoad = true;

    [Tooltip("기능장애를 뽑을 범위 (도). x=최소, y=최대")]
    [SerializeField] private Vector2 dysfunctionRange = new Vector2(10f, 25f);

    [Tooltip("압박 여유를 뽑을 범위 (도). x=최소, y=최대")]
    [SerializeField] private Vector2 passiveGainRange = new Vector2(5f, 10f);

    [Tooltip("0이 아니면 그 값을 시드로 써서 매번 같은 값이 나온다. 재현이 필요할 때만 쓴다.")]
    [SerializeField] private int randomSeed = 0;

    [Header("=== 진행 ===")]
    [Tooltip("능동 구간 진행 속도 (도/초). 굴곡 38°면 약 2.5초.")]
    [SerializeField] private float activeSpeed = 25f;

    [Tooltip("중립으로 돌아오는 속도 (도/초). 갈 때보다 조금 느린 편이 자연스럽다.")]
    [SerializeField] private float returnSpeed = 20f;

    [Tooltip("각도 변화를 부드럽게 하는 시간 (초). " +
             "★속도를 만드는 값이 아니라 방향이 바뀌는 순간의 각을 없애는 값이다. " +
             "크게 올리면 손 움직임과 머리가 어긋난다.")]
    [SerializeField] private float smoothTime = 0.08f;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = false;

    private const int DirectionCount = 7;   // None 포함. Direction을 인덱스로 쓴다.

    private float[] dysfunctions;           // 방향별 기능장애(도). 시나리오 로드 때 뽑는다.
    private float[] passiveGains;           // 방향별 압박 여유(도). 같이 뽑는다.
    private Direction currentDirection = Direction.None;

    /// <summary>파지로 방향만 미리 잡아 둔 상태. 중립이어도 방향을 유지한다.</summary>
    private bool directionPrimed;
    private Direction pendingDirection = Direction.None;   // 중립 복귀를 기다리는 다음 방향
    private float targetAngle;          // 최종 목표 각도 (도)
    private float commandedAngle;       // 속도 제한을 거친 중간 목표 (도)
    private float appliedAngle;         // 실제로 얹고 있는 각도 (도)
    private float angleVelocity;        // SmoothDamp용
    private float ramifySpeed;          // 지금 적용할 진행 속도 (도/초). 0이면 즉시 추종.
    private float normalizedWeightSum;

    /// <summary>현재 얹고 있는 각도(도).</summary>
    public float CurrentAngle => appliedAngle;

    /// <summary>현재 방향의 최대 각도(도).</summary>
    public float MaxAngle => MaxAngleOf(currentDirection);

    /// <summary>능동 구간의 끝점. 최대각에서 이번 판의 기능장애만큼 못 미친 각도.</summary>
    public float ActiveTargetAngle => Mathf.Max(0f, MaxAngle - DysfunctionOf(currentDirection));

    /// <summary>
    /// 압박으로 갈 수 있는 끝점. ★손이 더 밀어도 머리는 여기서 멈춘다 — 그게 끝느낌이다.
    /// 최대각을 넘지는 않는다.
    /// </summary>
    public float PassiveLimitAngle =>
        Mathf.Min(MaxAngle, ActiveTargetAngle + PassiveGainOf(currentDirection));

    /// <summary>이번 판에 뽑힌 기능장애(도). 방향마다 다르다.</summary>
    public float CurrentDysfunction => DysfunctionOf(currentDirection);

    /// <summary>
    /// 이번 판에 뽑힌 압박 여유(도). ★시술자가 밀어야 하는 양이자 머리가 더 가는 양이다.
    /// 압박 진행률의 분모로 쓴다.
    /// </summary>
    public float CurrentPassiveGain => Mathf.Max(0.1f, PassiveLimitAngle - ActiveTargetAngle);

    /// <summary>현재 방향의 회전축(월드). 압박을 손 움직임으로 구동할 때 쓴다.</summary>
    public Vector3 CurrentWorldAxis => WorldAxisFor(currentDirection);

    // ── 에디터 프리뷰용 읽기 전용 접근자 ─────────────────────────────────
    // ★각도기가 Play 없이 그리려면 '지금 재고 있는 방향'이 아니라 <b>임의의 방향</b>의
    //   최대각·회전축을 물어볼 수 있어야 한다. 셋 다 상태를 바꾸지 않는다 —
    //   에디터에서 드라이버 필드를 건드리면 씬이 더럽혀지고, 저장 사고로 이어진다.

    /// <summary>그 방향의 임상 최대각(도). currentDirection과 무관하다.</summary>
    public float MaxAngleFor(Direction d) => MaxAngleOf(d);

    /// <summary>그 방향의 회전축(월드). currentDirection과 무관하다.</summary>
    public Vector3 WorldAxisFor(Direction d) =>
        ResolvedTorso != null && d != Direction.None
            ? ResolvedTorso.TransformDirection(AxisOf(d)) : Vector3.zero;

    // ── 에디터 프리뷰용 뼈 해석 ────────────────────────────────────────────
    // ★씬에는 torso·neckChain이 전부 비어 있고 Awake에서 이름으로 찾아 채운다.
    //   그런데 에디터에서는 Awake가 돌지 않으므로 둘 다 null이고, 각도기가
    //   "중심도 몸통도 없다"며 스스로 숨었다 — Play 없이 각도기가 안 보이던 원인이다(2026-08-26).
    // ★직렬화 필드에 <b>쓰지 않는다</b>. 에디터에서 [SerializeField]에 대입하면
    //   씬이 더러워지고, 모르는 새 저장되면 배선이 굳는다.
    [System.NonSerialized] private Transform previewTorso;
    [System.NonSerialized] private Transform previewNeckRoot;

    /// <summary>몸통. 비어 있으면(에디터) 이름으로 찾아 임시로 들고 있는다.</summary>
    private Transform ResolvedTorso
    {
        get
        {
            if (torso != null) return torso;
            if (previewTorso == null) previewTorso = FindDeep(transform, "CC_Base_Spine02");
            return previewTorso;
        }
    }

    /// <summary>목 사슬의 첫 뼈. 위와 같은 이유로 에디터에서는 이름으로 찾는다.</summary>
    private Transform ResolvedNeckRoot
    {
        get
        {
            if (neckChain != null && neckChain.Length > 0 && neckChain[0] != null) return neckChain[0];
            if (previewNeckRoot == null) previewNeckRoot = FindDeep(transform, "CC_Base_NeckTwist01");
            return previewNeckRoot;
        }
    }

    /// <summary>인스펙터에 적힌 기능장애 기본값(도). 추첨 전 값이라 프리뷰용이다.</summary>
    public float NominalDysfunction => dysfunctionAngle;

    /// <summary>인스펙터에 적힌 압박 여유 기본값(도). 추첨 전 값이라 프리뷰용이다.</summary>
    public float NominalPassiveGain => passiveGainAngle;

    // ── 압박 유지 타이머 ──────────────────────────────────────────────────
    // ★브리지가 채우고 각도기가 읽는다. 여기 실어 두면 각도기가 브리지를 몰라도 된다
    //   (각도기는 이미 드라이버를 참조하고 있다). 드라이버는 이 값을 쓰지 않는다.

    /// <summary>압박 한계에서 버틴 시간(초).</summary>
    public float HoldElapsed { get; set; }

    /// <summary>버텨야 하는 시간(초). 0이면 유지 구간이 아니라 표시하지 않는다.</summary>
    public float HoldTarget { get; set; }

    /// <summary>목이 도는 중심. 손이 돌린 각을 재는 기준점이다.
    /// ★에디터에서는 배선이 비어 있으므로 이름으로 찾아 쓴다(각도기 프리뷰용).</summary>
    public Transform Pivot => ResolvedNeckRoot != null ? ResolvedNeckRoot : ResolvedTorso;

    /// <summary>지금 재고 있는 방향. 각도기가 어느 면을 띄울지 여기서 정한다.</summary>
    public Direction CurrentDirection => currentDirection;

    /// <summary>기준 몸통. 각도기가 0° 방향을 잡는 데 쓴다.
    /// ★에디터에서는 배선이 비어 있으므로 이름으로 찾아 쓴다(각도기 프리뷰용).</summary>
    public Transform Torso => ResolvedTorso;

    /// <summary>능동 끝점에 도달했는가. 압박 단계로 넘어가도 되는 시점이다.</summary>
    public bool ActiveReached => currentDirection != Direction.None &&
                                 appliedAngle >= ActiveTargetAngle - 0.5f;

    /// <summary>최대각 대비 부족한 각도. 평가 단계에서 기록할 값이다.</summary>
    public float DeficitAngle => Mathf.Max(0f, MaxAngle - appliedAngle);

    // ── 측정 결과 ─────────────────────────────────────────────────────────
    // ★방향이 바뀌면 appliedAngle은 사라진다. 결과 화면과 CSV가 읽을 값을
    //   방향별로 따로 남겨야 한다. 경추ROM은 채점 지표가 없어(PassiveStretch는 0점)
    //   이 각도가 곧 결과다.

    /// <summary>한 방향의 측정 결과.</summary>
    public struct Measurement
    {
        public bool recorded;
        public float maxAngle;    // 임상 최대각
        public float active;    // 환자가 스스로 도달한 각
        public float passive;   // 시술자가 밀어 도달한 각
        public float Deficit => Mathf.Max(0f, maxAngle - passive);
    }

    private Measurement[] measurements;

    /// <summary>
    /// ★[임시 · A-12 교차검증] 측정값을 남긴 순간. (방향, 능동기록인가)를 넘긴다.
    /// <see cref="CervicalRomHandAngleProbe"/>가 이 순간의 손 측정각을 같이 찍어 두려고 듣는다.
    /// 검증이 끝나면 이 줄과 <see cref="Record"/> 안의 발행 1줄만 지우면 된다.
    /// </summary>
    public event System.Action<Direction, bool> OnMeasurementRecorded;

    /// <summary>능동 끝점에 도달한 순간의 각을 남긴다. 브리지가 부른다.</summary>
    public void RecordActiveReached() => Record(active: appliedAngle, passive: float.NaN);

    /// <summary>압박이 끝난 순간의 각을 남긴다. 목표에 못 닿고 넘어갔어도 그 값이 곧 측정값이다.</summary>
    public void RecordPassiveReached() => Record(active: float.NaN, passive: appliedAngle);

    private void Record(float active, float passive)
    {
        if (currentDirection == Direction.None) return;
        if (measurements == null || measurements.Length != DirectionCount)
            measurements = new Measurement[DirectionCount];

        int i = (int)currentDirection;
        Measurement m = measurements[i];
        m.recorded = true;
        m.maxAngle = MaxAngle;
        if (!float.IsNaN(active)) m.active = active;
        if (!float.IsNaN(passive)) m.passive = passive;
        measurements[i] = m;

        if (showDebugLogs)
        {
            ChunaLogger.Log($"<color=cyan>[CervicalROM] {currentDirection} 기록 — " +
                            $"능동 {m.active:F1}° · 압박 {m.passive:F1}° · 최대 {m.maxAngle:F0}° · 부족 {m.Deficit:F1}°</color>");
        }

        // ★[임시 · A-12 교차검증] 듣는 쪽이 없으면 아무 일도 없다.
        OnMeasurementRecorded?.Invoke(currentDirection, !float.IsNaN(active));
    }

    /// <summary>그 방향의 측정 결과. 아직 안 쟀으면 recorded=false.</summary>
    /// <summary>브리지가 술기를 벗어날 때 부른다. 미리 잡아 둔 방향을 놓는다.</summary>
    public void ClearPrimedDirection()
    {
        if (!directionPrimed) return;
        directionPrimed = false;
        if (appliedAngle < 0.05f) currentDirection = Direction.None;
    }

    public Measurement GetMeasurement(Direction d)
    {
        if (measurements == null || d == Direction.None || (int)d >= measurements.Length)
            return default;
        return measurements[(int)d];
    }

    /// <summary>
    /// 켜면 각도 진행이 멈춘다. 손이 환자에게서 떨어졌을 때 쓴다 —
    /// 환자 움직임을 손이 따라간다는 규약상, 손을 떼면 그 자리에서 멈춰야 한다.
    /// 감쇠는 계속 돌아 이미 밀린 각도는 부드럽게 마무리된다.
    /// </summary>
    public bool Paused { get; set; }

    private void Awake()
    {
        AutoFindBones();
        NormalizeWeights();
        RandomizeGaps();
    }

    /// <summary>
    /// 여유 구간을 방향별로 다시 뽑는다. 시나리오를 불러올 때 한 번 부르면 된다.
    /// randomizePerLoad가 꺼져 있으면 고정값(dysfunctionAngle · passiveGainAngle)으로 채운다.
    /// </summary>
    public void RandomizeGaps()
    {
        if (dysfunctions == null || dysfunctions.Length != DirectionCount) dysfunctions = new float[DirectionCount];
        if (passiveGains == null || passiveGains.Length != DirectionCount) passiveGains = new float[DirectionCount];

        if (!randomizePerLoad)
        {
            for (int i = 0; i < dysfunctions.Length; i++)
            {
                dysfunctions[i] = dysfunctionAngle;
                passiveGains[i] = passiveGainAngle;
            }
            return;
        }

        // 시드를 주면 재현된다. 0이면 매번 다르게 뽑는다.
        Random.State previous = default;
        bool seeded = randomSeed != 0;
        if (seeded)
        {
            previous = Random.state;
            Random.InitState(randomSeed);
        }

        for (int i = 0; i < dysfunctions.Length; i++)
        {
            dysfunctions[i] = RandomIn(dysfunctionRange);
            passiveGains[i] = RandomIn(passiveGainRange);
        }

        if (seeded) Random.state = previous;

        if (showDebugLogs)
        {
            ChunaLogger.Log("<color=cyan>[CervicalROM] 환자 제한 재추첨 — " +
                            $"기능장애 {dysfunctionRange.x:F0}~{dysfunctionRange.y:F0}° · " +
                            $"압박 여유 {passiveGainRange.x:F0}~{passiveGainRange.y:F0}°\n" +
                            Summary(Direction.Flexion, "굴곡") + Summary(Direction.Extension, "신전") +
                            Summary(Direction.LateralRight, "우측굴") + Summary(Direction.LateralLeft, "좌측굴") +
                            Summary(Direction.RotationRight, "우회전") + Summary(Direction.RotationLeft, "좌회전") +
                            "</color>");
        }
    }

    private static float RandomIn(Vector2 range)
    {
        return Random.Range(Mathf.Min(range.x, range.y), Mathf.Max(range.x, range.y));
    }

    /// <summary>추첨 결과를 한 방향씩 사람이 읽을 수 있게 적는다.</summary>
    private string Summary(Direction d, string label)
    {
        float maxAngle = MaxAngleOf(d);
        float active = Mathf.Max(0f, maxAngle - DysfunctionOf(d));
        float passive = Mathf.Min(maxAngle, active + PassiveGainOf(d));
        return $"    {label} 최대 {maxAngle:F0}° · 능동 {active:F0}° · 압박 {passive:F0}° · 부족 {maxAngle - passive:F1}°\n";
    }

    private float DysfunctionOf(Direction d)
    {
        if (d == Direction.None) return 0f;
        if (dysfunctions == null || (int)d >= dysfunctions.Length) return dysfunctionAngle;
        return dysfunctions[(int)d];
    }

    private float PassiveGainOf(Direction d)
    {
        if (d == Direction.None) return 0f;
        if (passiveGains == null || (int)d >= passiveGains.Length) return passiveGainAngle;
        return passiveGains[(int)d];
    }

    /// <summary>
    /// 방향을 정하고 능동 구간을 시작한다. 자동으로 능동 끝점까지 간다.
    /// ★앞 방향의 각도가 남아 있으면 <b>중립까지 먼저 돌아온 뒤</b> 시작한다.
    ///   각 측정은 압박 → 중립 복귀 → 다음 방향 순서를 지켜야 한다.
    /// </summary>
    public void BeginActive(Direction direction)
    {
        if (currentDirection != Direction.None && currentDirection != direction && appliedAngle > 0.5f)
        {
            pendingDirection = direction;
            ReturnToNeutral();
            if (showDebugLogs)
            {
                ChunaLogger.Log($"<color=cyan>[CervicalROM] {currentDirection} 각도가 {appliedAngle:F1}° 남아 " +
                                $"중립 복귀 후 {direction} 시작</color>");
            }
            return;
        }

        StartActiveInternal(direction);
    }

    /// <summary>
    /// 각도기가 뜨도록 <b>방향만</b> 잡는다. 움직이지 않는다.
    ///
    /// ★2026-08-27 회의 결정 — "파지하는 순간 각도가 딱 나와야 한다".
    ///   여태는 BeginActive가 불려야 방향이 정해져서, 각도기가 <b>동작이 시작된 뒤</b>에 떴다.
    /// ★앞 방향의 각이 남아 있으면 건드리지 않는다. 중립 복귀가 먼저다.
    /// </summary>
    public void PrepareDirection(Direction direction)
    {
        if (direction == Direction.None) return;
        if (currentDirection == direction && directionPrimed) return;
        if (appliedAngle > 0.5f) return;

        currentDirection = direction;
        pendingDirection = Direction.None;
        directionPrimed = true;
        targetAngle = 0f;
        commandedAngle = 0f;

        if (showDebugLogs)
            ChunaLogger.Log($"<color=cyan>[CervicalROM] 파지 — {direction} 각도기 준비(움직이지 않는다)</color>");
    }

    private void StartActiveInternal(Direction direction)
    {
        currentDirection = direction;
        pendingDirection = Direction.None;
        directionPrimed = false;   // 실제 동작이 시작됐다 — 이제 중립에 닿으면 방향을 놓는다
        targetAngle = ActiveTargetAngle;
        ramifySpeed = activeSpeed;       // ★즉시 대입하면 안 된다. 속도로 밀어야 부드럽다.

        if (showDebugLogs)
        {
            ChunaLogger.Log($"<color=cyan>[CervicalROM] 능동 시작: {direction} → " +
                            $"{ActiveTargetAngle:F0}° (최대 {MaxAngle:F0}°, 기능장애 {CurrentDysfunction:F1}°, "
                          + $"압박 한계 {PassiveLimitAngle:F0}°)</color>");
        }
    }

    /// <summary>
    /// 압박 구간. 0이면 능동 끝점, 1이면 <b>압박 한계</b>다(최대각이 아니다).
    /// 시술자 손의 진행률을 그대로 넣으면 된다.
    ///
    /// ★손이 더 밀어도 압박 한계에서 멈춘다 — 그 저항이 끝느낌이고,
    ///   최대각까지 남는 각이 곧 부족각이다. 예전처럼 최대각까지 가면
    ///   누구를 측정해도 부족각 0이 나와 측정이 무의미해진다.
    /// </summary>
    public void SetOverpressure(float progress01)
    {
        if (currentDirection == Direction.None) return;

        // 압박은 시술자 손을 즉시 따라가야 한다. 속도 제한을 걸면 손과 머리가 어긋난다.
        ramifySpeed = 0f;
        targetAngle = Mathf.Lerp(ActiveTargetAngle, PassiveLimitAngle, Mathf.Clamp01(progress01));
    }

    /// <summary>각도를 직접 지정한다(도).</summary>
    public void SetAngle(Direction direction, float degrees)
    {
        currentDirection = direction;
        ramifySpeed = 0f;
        targetAngle = Mathf.Max(0f, degrees);
    }

    /// <summary>중립에 도달했는가. 다음 방향으로 넘어가도 되는 시점이다.</summary>
    public bool AtNeutral => currentDirection == Direction.None || appliedAngle < 0.5f;

    /// <summary>중립으로 되돌린다. returnSpeed로 천천히 내려온다.</summary>
    public void ReturnToNeutral()
    {
        targetAngle = 0f;
        ramifySpeed = returnSpeed;
    }

    private void LateUpdate()
    {
        // Animator가 포즈를 쓴 다음에 덧얹어야 한다. Update에서 하면 애니메이션이 덮어쓴다.
        if (currentDirection == Direction.None) return;

        // 1단계 — 속도 제한. 능동·복귀는 도/초로 밀고, 압박은 손을 즉시 따라간다.
        if (Paused)
        {
            // 손을 뗀 상태. 진행을 멈추고 감쇠만 돌린다.
        }
        else if (ramifySpeed > 0f)
        {
            commandedAngle = Mathf.MoveTowards(commandedAngle, targetAngle, ramifySpeed * Time.deltaTime);
        }
        else
        {
            commandedAngle = targetAngle;
        }

        // 2단계 — 감쇠. 방향이 바뀌는 순간의 각을 없애는 용도지 속도를 만드는 용도가 아니다.
        if (smoothTime > 0f)
        {
            appliedAngle = Mathf.SmoothDamp(appliedAngle, commandedAngle, ref angleVelocity, smoothTime);
        }
        else
        {
            appliedAngle = commandedAngle;
        }

        if (targetAngle <= 0f && commandedAngle <= 0f && appliedAngle < 0.05f)
        {
            appliedAngle = 0f;
            commandedAngle = 0f;
            angleVelocity = 0f;

            // ★파지로 미리 잡아 둔 방향은 여기서 지우지 않는다(2026-08-28).
            //   PrepareDirection이 만드는 상태가 정확히 '중립'이라, 방향을 잡자마자
            //   다음 프레임에 여기서 None으로 지워져 각도기가 뜰 틈이 없었다.
            //   실제 동작이 시작되면(StartActiveInternal) 이 표식은 풀린다.
            if (!directionPrimed) currentDirection = Direction.None;

            // 중립에 닿았다. 기다리던 다음 방향이 있으면 여기서 이어 간다.
            if (pendingDirection != Direction.None) StartActiveInternal(pendingDirection);
            return;
        }

        ApplyRotation(appliedAngle);
    }

    /// <summary>목뼈 3개에 각도를 나눠 얹는다. 뿌리부터 처리해야 합이 맞는다.</summary>
    private void ApplyRotation(float degrees)
    {
        if (torso == null || Mathf.Approximately(degrees, 0f)) return;

        Vector3 axis = AxisOf(currentDirection);
        if (axis == Vector3.zero) return;

        Vector3 worldAxis = torso.TransformDirection(axis);

        for (int i = 0; i < neckChain.Length; i++)
        {
            Transform bone = neckChain[i];
            if (bone == null) continue;

            float weight = i < boneWeights.Length ? boneWeights[i] : 0f;
            if (weight <= 0f) continue;

            // 월드 축 기준으로 덧회전. 부모를 먼저 돌리면 자식이 따라오므로
            // 뿌리→끝 순서로 처리하면 최종 머리 각도가 정확히 degrees가 된다.
            bone.rotation = Quaternion.AngleAxis(degrees * weight / normalizedWeightSum, worldAxis) * bone.rotation;
        }
    }

    /// <summary>
    /// 방향별 회전축 (몸통 로컬 기준).
    /// ★2026-08-24 클립 실측에서 나온 값이다 — 굴곡·신전 x축, 측굴 z축, 회전 y축.
    /// </summary>
    private static Vector3 AxisOf(Direction d)
    {
        switch (d)
        {
            // ★부호는 Play에서 눈으로 확인해 뒤집었다(2026-08-24). 클립 실측의 축 부호를
            //   그대로 옮겼더니 굴곡·신전이 반대로 나왔다. 측굴·회전은 그대로 맞았다.
            case Direction.Flexion:        return Vector3.left;       // x −
            case Direction.Extension:      return Vector3.right;      // x +
            case Direction.LateralRight:   return Vector3.back;       // z −
            case Direction.LateralLeft:    return Vector3.forward;    // z +
            case Direction.RotationRight:  return Vector3.up;         // y +
            case Direction.RotationLeft:   return Vector3.down;       // y −
            default:                       return Vector3.zero;
        }
    }

    private float MaxAngleOf(Direction d)
    {
        switch (d)
        {
            case Direction.Flexion:       return flexionMax;
            case Direction.Extension:     return extensionMax;
            case Direction.LateralRight:
            case Direction.LateralLeft:   return lateralMax;
            case Direction.RotationRight:
            case Direction.RotationLeft:  return rotationMax;
            default:                      return 0f;
        }
    }

    private void NormalizeWeights()
    {
        normalizedWeightSum = 0f;
        for (int i = 0; i < boneWeights.Length && i < neckChain.Length; i++)
        {
            if (neckChain[i] != null) normalizedWeightSum += boneWeights[i];
        }
        if (normalizedWeightSum <= 0f) normalizedWeightSum = 1f;
    }

    private void AutoFindBones()
    {
        if (torso == null) torso = FindDeep(transform, "CC_Base_Spine02");

        string[] names = { "CC_Base_NeckTwist01", "CC_Base_NeckTwist02", "CC_Base_Head" };
        if (neckChain == null || neckChain.Length < names.Length)
        {
            neckChain = new Transform[names.Length];
        }
        for (int i = 0; i < names.Length; i++)
        {
            if (neckChain[i] == null) neckChain[i] = FindDeep(transform, names[i]);
        }

        if (torso == null || neckChain[0] == null || neckChain[2] == null)
        {
            ChunaLogger.LogWarning("[CervicalROM] 목 사슬을 찾지 못했습니다. " +
                                   "환자 루트에 붙였는지, 뼈 이름이 CC_Base_* 인지 확인하세요.");
        }
    }

    private static Transform FindDeep(Transform root, string boneName)
    {
        if (root.name == boneName) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeep(root.GetChild(i), boneName);
            if (found != null) return found;
        }
        return null;
    }

#if UNITY_EDITOR
    [ContextMenu("테스트 — 1 굴곡 능동")]
    private void TestFlexion() => BeginActive(Direction.Flexion);

    [ContextMenu("테스트 — 2 신전 능동")]
    private void TestExtension() => BeginActive(Direction.Extension);

    [ContextMenu("테스트 — 3 우측굴 능동")]
    private void TestLateralRight() => BeginActive(Direction.LateralRight);

    [ContextMenu("테스트 — 4 좌측굴 능동")]
    private void TestLateralLeft() => BeginActive(Direction.LateralLeft);

    [ContextMenu("테스트 — 5 우회전 능동")]
    private void TestRotationRight() => BeginActive(Direction.RotationRight);

    [ContextMenu("테스트 — 6 좌회전 능동")]
    private void TestRotationLeft() => BeginActive(Direction.RotationLeft);

    [ContextMenu("테스트 — 압박 100%")]
    private void TestOverpressure() => SetOverpressure(1f);

    [ContextMenu("테스트 — 중립 복귀")]
    private void TestNeutral() => ReturnToNeutral();

    [ContextMenu("테스트 — 여유 구간 다시 뽑기")]
    private void TestRandomize()
    {
        bool prev = showDebugLogs;
        showDebugLogs = true;
        RandomizeGaps();
        showDebugLogs = prev;
    }
#endif
}
