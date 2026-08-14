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
///   · <b>흐름(권장)</b>: 조각이 2개 이상이면 <b>1~2칸만 빛나며</b> 꼬리→머리로 지나간다
///     (<c>sharpFlow</c>, 기본 켬). 에디터 도구의 "(흐름)" 메뉴가 이 형태를 만든다.
///   · <b>단일</b>: 통짜 화살표 1개면 밝기가 오르내리며 <b>미는 방향으로 앞뒤로 왕복</b>한다(<c>travelPulse</c>).
///
/// ★08-11 사용자 지시 — "경로 전체를 켜 둔 채 이펙트만 흐르면 안 되고 진행 위치 1~2칸만 빛나야 한다.
/// 직선도 늘어나는 게 아니라 앞뒤로 움직여야 한다."
///
/// ★길이로 힘의 크기를 암시하지 말 것 — 압력 판정이 없으므로(useDepthJudging=false)
/// 학생이 "이만큼 세게"로 오해한다. 길이는 가독성 기준으로만 정한다.
/// </summary>
public class ForceArrow : ForceArrowBase
{
    /// <summary>어떻게 보여줄지. 조각 수로 자동 판별하던 것을 <b>강제로 지정</b>할 수 있게 한 것.</summary>
    public enum DisplayMode
    {
        /// <summary>조각이 2개 이상이면 흐름, 1개면 통짜 왕복(예전 동작).</summary>
        자동,
        /// <summary>진행 위치 1~2칸만 빛나며 지나간다(점이 흐르는 형태).</summary>
        흐름,
        /// <summary>★<b>화살표 전체를 항상 켜 둔 채</b> 통째로 미는 방향으로 크게 왕복한다.</summary>
        통짜왕복
    }

    [Header("=== 표시 방식 ===")]
    [Tooltip("★기본 = 통짜 왕복(2026-08-14 사용자 요구: \"점선 이동 말고 전체 화살표가 크게 움직이면서 방향을 보여주는 게 좋겠다\").\n" +
             "흐름 모드는 진행 위치의 1~2칸만 켜고 <b>나머지 조각의 렌더러를 꺼 버리기 때문에</b> " +
             "화살표 대부분이 항상 안 보인다 — 이게 \"눈에 잘 안 보인다\"의 원인이었다.\n" +
             "자동 = 예전 동작(조각 2개 이상이면 흐름).")]
    [SerializeField] private DisplayMode displayMode = DisplayMode.통짜왕복;

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
    [Tooltip("진행 방향(+Z) 길이 펄스 진폭. 0이면 길이 고정.\n" +
             "★아래 '앞뒤 이동'이 0보다 크면 이 값은 무시된다(길이는 고정).")]
    [SerializeField, Range(0f, 0.3f)] private float lengthPulse = 0.08f;

    [Tooltip("★화살표가 늘었다 줄었다 하면 '힘이 세졌다 약해진다'로 읽힌다 → 길이는 두고 " +
             "미는 방향으로 앞뒤로 왕복시킨다(08-11 사용자 지시).\n" +
             "화살표 길이 대비 이동 폭. 0이면 제자리(예전 방식 = 길이 펄스).\n" +
             "★이 값은 씬에 이미 직렬화돼 있어 코드 기본값이 안 먹는다 — 일괄로 키우려면 " +
             "메뉴 `GuideChuna/화살표 그룹 점검 · 이름 정리`의 <b>가시성 프리셋</b> 버튼을 쓸 것.")]
    [SerializeField, Range(0f, 2f)] private float travelPulse = 0.6f;

    [Header("=== 공통 ===")]
    [SerializeField, Range(0f, 1f)] private float minAlpha = 0.25f;
    [SerializeField, Range(0f, 1f)] private float maxAlpha = 0.95f;

    private Vector3 baseScale;
    private Vector3 basePos;
    private float phase;
    private bool initialized;

    private bool FlowMode =>
        displayMode == DisplayMode.흐름 ||
        (displayMode == DisplayMode.자동 && segments != null && segments.Length > 1);

    private void Awake() => Initialize();

    private void Initialize()
    {
        if (initialized) return;
        initialized = true;

        baseScale = ScaledBase(transform.localScale);
        basePos = transform.localPosition;
        transform.localScale = baseScale;

        if (segments == null || segments.Length == 0)
        {
            segments = GetComponentsInChildren<Renderer>(true);
            // 자동 수집은 하이어라키 순서라 진행 순서를 보장하지 못한다 → +Z로 정렬한다.
            System.Array.Sort(segments, (a, b) =>
                a.transform.localPosition.z.CompareTo(b.transform.localPosition.z));
        }

        foreach (Renderer r in segments)
            EnsureMaterialMode(r);
    }

    private void OnEnable()
    {
        Initialize();
        phase = 0f;
        transform.localScale = baseScale;
        transform.localPosition = basePos;
    }

    private void OnDisable()
    {
        // 다음에 켜질 때 펄스 도중의 크기·위치가 남아 있지 않도록 되돌린다.
        if (!initialized) return;
        transform.localScale = baseScale;
        transform.localPosition = basePos;
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

        // 통짜 화살표: 전체가 같이 밝아졌다 어두워지면서, 미는 방향으로 앞뒤로 왕복한다.
        phase += Time.deltaTime * Mathf.Max(0.01f, pulsePerSecond);
        if (phase > 1f) phase -= 1f;
        float wave = (Mathf.Sin(phase * 2f * Mathf.PI) + 1f) * 0.5f;

        // ★조각을 전부 켠다 — 흐름 모드로 돌다가 이 모드로 바뀌면 렌더러가 꺼진 채 남아 있다
        //   (흐름은 안 빛나는 칸의 renderer.enabled를 false로 둔다).
        Color c = Shade(BaseColor, Mathf.Lerp(minAlpha, maxAlpha, wave));
        foreach (Renderer r in segments)
        {
            if (r == null) continue;
            if (!r.enabled) r.enabled = true;
            SetRendererColor(r, c);
        }

        if (travelPulse > 0f)
        {
            // ★길이는 고정하고 위치만 움직인다 — 늘었다 줄었다 하면 '힘의 크기'로 오해된다.
            //   화살표 메시는 길이 1이라 실제 길이 = baseScale.z. 그 비율만큼 앞뒤로 오간다.
            transform.localScale = baseScale;
            float amp = travelPulse * baseScale.z * 0.5f;
            Vector3 forwardLocal = transform.localRotation * Vector3.forward;   // 부모 기준 +Z
            transform.localPosition = basePos + forwardLocal * (Mathf.Sin(phase * 2f * Mathf.PI) * amp);
        }
        else if (lengthPulse > 0f)
        {
            Vector3 s = baseScale;
            s.z = baseScale.z * (1f + lengthPulse * (wave - 0.5f) * 2f);
            transform.localScale = s;
        }
    }
}
