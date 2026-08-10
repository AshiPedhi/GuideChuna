using UnityEngine;

/// <summary>
/// 직선 힘의 방향 화살표 (표시 전용 — 판정하지 않는다).
///
/// ★규약: <b>화살표는 "손이 미는 방향"이지 "뼈가 어떻게 움직이는가"가 아니다.</b>
/// 족방→두방 당기기처럼 <b>직선으로 미는 동작</b>에 쓴다.
/// 굴곡·신전, 내회전·외회전처럼 <b>회전이 눈에 보여야 하는 동작</b>에는
/// <see cref="ForceArcArrow"/>(호 화살표)를 쓴다.
///
/// ★기하 규약: <b>화살표는 로컬 +Z(파란 축) 방향을 가리킨다.</b>
/// 파지점의 자식으로 두면 환자 애니메이션을 따라간다.
///
/// ★표시 방식 2가지 — 어느 쪽인지는 <b>자식 조각 수로 자동 판별</b>한다.
///   · <b>흐름(권장)</b>: 조각이 2개 이상이면 꼬리→머리로 빛이 흘러 진행 방향이 또렷하다.
///     에디터 도구의 "(흐름)" 메뉴가 이 형태를 만든다.
///   · <b>단일</b>: 통짜 화살표 1개면 전체가 같이 밝아졌다 어두워진다(기존 형태 호환).
///
/// ★길이로 힘의 크기를 암시하지 말 것 — 압력 판정이 없으므로(useDepthJudging=false)
/// 학생이 "이만큼 세게"로 오해한다. 길이는 가독성 기준으로만 정한다.
/// </summary>
public class ForceArrow : ForceArrowBase
{
    [Header("=== 조각 (비우면 자식에서 자동 수집, +Z 순으로 정렬) ===")]
    [Tooltip("배열 순서 = 진행 순서(꼬리 → 머리). 마지막이 화살촉.")]
    [SerializeField] private Renderer[] segments;

    [Header("=== 흐름 (조각 2개 이상일 때) ===")]
    [Tooltip("초당 흐름 횟수. 화살표를 몇 번 훑고 지나가는지.")]
    [SerializeField] private float flowPerSecond = 1.1f;
    [Tooltip("빛이 지나가는 폭(조각 수 기준). 작을수록 또렷한 점처럼 흐른다.")]
    [SerializeField, Range(0.5f, 6f)] private float flowWidth = 1.8f;
    [Tooltip("화살촉을 항상 진하게 유지 — 종착점이 깜빡이면 어디로 미는지가 흐려진다.")]
    [SerializeField] private bool keepHeadBright = true;

    [Header("=== 펄스 (조각 1개일 때) ===")]
    [Tooltip("초당 펄스 횟수")]
    [SerializeField] private float pulsePerSecond = 0.8f;
    [Tooltip("진행 방향(+Z) 길이 펄스 진폭. 0이면 길이 고정.")]
    [SerializeField, Range(0f, 0.3f)] private float lengthPulse = 0.08f;

    [Header("=== 공통 ===")]
    [SerializeField, Range(0f, 1f)] private float minAlpha = 0.25f;
    [SerializeField, Range(0f, 1f)] private float maxAlpha = 0.95f;

    private Vector3 baseScale;
    private float phase;
    private bool initialized;

    private bool FlowMode => segments != null && segments.Length > 1;

    private void Awake() => Initialize();

    private void Initialize()
    {
        if (initialized) return;
        initialized = true;

        baseScale = transform.localScale;

        if (segments == null || segments.Length == 0)
        {
            segments = GetComponentsInChildren<Renderer>(true);
            // 자동 수집은 하이어라키 순서라 진행 순서를 보장하지 못한다 → +Z로 정렬한다.
            System.Array.Sort(segments, (a, b) =>
                a.transform.localPosition.z.CompareTo(b.transform.localPosition.z));
        }

        foreach (Renderer r in segments)
            EnsureTransparentMaterial(r);
    }

    private void OnEnable()
    {
        Initialize();
        phase = 0f;
        transform.localScale = baseScale;
    }

    private void OnDisable()
    {
        // 다음에 켜질 때 펄스 도중 크기가 남아 있지 않도록 되돌린다.
        if (initialized) transform.localScale = baseScale;
    }

    private void Update()
    {
        if (segments == null || segments.Length == 0) return;

        if (FlowMode)
        {
            phase += Time.deltaTime * Mathf.Max(0.01f, flowPerSecond);
            if (phase > 1f) phase -= 1f;
            ApplyFlow(segments, phase, flowWidth, minAlpha, maxAlpha, keepHeadBright);
            return;
        }

        // 통짜 화살표: 전체가 같이 밝아졌다 어두워지고, 길이가 살짝 늘었다 줄어든다.
        phase += Time.deltaTime * Mathf.Max(0.01f, pulsePerSecond);
        if (phase > 1f) phase -= 1f;
        float wave = (Mathf.Sin(phase * 2f * Mathf.PI) + 1f) * 0.5f;

        Color c = BaseColor;
        c.a = Mathf.Lerp(minAlpha, maxAlpha, wave);
        foreach (Renderer r in segments) SetRendererColor(r, c);

        if (lengthPulse > 0f)
        {
            Vector3 s = baseScale;
            s.z = baseScale.z * (1f + lengthPulse * (wave - 0.5f) * 2f);
            transform.localScale = s;
        }
    }
}
