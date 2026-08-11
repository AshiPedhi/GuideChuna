using UnityEngine;

/// <summary>
/// 회전 방향 화살표 — 호(arc)를 따라 빛이 흘러가며 회전 방향을 보여준다.
///
/// ★왜 필요한가(08-10 실사용 피드백): 굴곡·신전, 내회전·외회전은 직선 화살표로는
/// "어느 쪽으로 도는가"가 안 읽힌다. 시술자의 손이 미는 방향과 환자의 턱이 들리는 방향이
/// <b>같이 회전하듯</b> 일어나는 동작이라 궤적 자체를 보여줘야 한다.
///
/// ★구현: 커스텀 셰이더를 쓰지 않는다(빌드에서 스트립돼 xray가 죽은 전례).
/// 호를 <b>여러 조각으로 나눈 자식 오브젝트</b>를 만들고 조각마다 알파를 위상차를 두어 흘린다
/// → 회전 방향으로 빛이 지나가는 것처럼 보인다. 마지막 조각은 화살촉이다.
///
/// ★08-11 사용자 지시로 <b>호 전체를 켜 두지 않는다</b> — <c>sharpFlow</c>(기본 켬)가 진행 위치의
/// 1~2칸만 남기고 나머지를 지운다. 러너와 함께 "빛나는 점이 궤도를 타고 도는" 형태가 된다.
/// 조각은 에디터 도구가 만들어 둔다(플레이 안 해도 씬에서 모양이 보이도록).
///
/// ★기하 규약: <b>회전축 = 로컬 +Y</b>. 호는 로컬 +Z에서 시작해 +X 쪽으로 돈다.
/// 씬에서 오브젝트를 회전시켜 축을 해부학적 회전축(굴곡·신전이면 좌우 귀를 잇는 축)에 맞추고,
/// 도구의 sweep 부호로 도는 방향을 뒤집는다.
/// </summary>
public class ForceArcArrow : ForceArrowBase
{
    [Header("=== 조각 (에디터 도구가 채운다. 비우면 자식에서 자동 수집) ===")]
    [Tooltip("호를 이루는 조각들. 배열 순서 = 회전 진행 순서. 마지막이 화살촉.")]
    [SerializeField] private Renderer[] segments;

    [Header("=== 흐름 ===")]
    [Tooltip("초당 흐름 횟수. 호 전체를 몇 번 지나가는지.")]
    [SerializeField] private float flowPerSecond = 0.7f;
    [Tooltip("빛이 지나가는 폭(조각 수 기준). 크면 넓고 부드럽게, 작으면 또렷한 점처럼 흐른다.")]
    [SerializeField, Range(0.5f, 6f)] private float flowWidth = 2f;
    [SerializeField, Range(0f, 1f)] private float minAlpha = 0.22f;
    [SerializeField, Range(0f, 1f)] private float maxAlpha = 0.95f;

    [Tooltip("화살촉(마지막 조각)은 흐름과 무관하게 항상 진하게 유지한다 — 방향의 종착점이라 " +
             "깜빡이면 어디로 도는지가 흐려진다.")]
    [SerializeField] private bool keepHeadBright = true;

    [Header("=== 러너 (호를 따라 실제로 이동하는 쐐기) ===")]
    [Tooltip("★알파만 흐르면 얇은 튜브에서는 '깜빡임'으로 보이고 이동으로 안 읽힌다. " +
             "이 쐐기가 호를 따라 실제로 움직여 회전 방향을 확실하게 만든다. 비우면 알파 흐름만 동작.")]
    [SerializeField] private Transform runner;
    [Tooltip("러너에 색·알파를 입힐 렌더러. 비우면 runner 자식에서 찾는다.")]
    [SerializeField] private Renderer runnerRenderer;

    [Tooltip("호의 반지름(m). 에디터 도구가 채운다 — 러너 위치 계산에 쓴다.")]
    [SerializeField] private float arcRadius = 0.07f;
    [Tooltip("호가 도는 각도. 에디터 도구가 채운다.")]
    [SerializeField] private float arcSweepDeg = 100f;
    [Tooltip("러너가 양 끝에서 사라지는 구간 비율(0~0.5). 끝에서 툭 튀는 걸 막는다.")]
    [SerializeField, Range(0f, 0.5f)] private float runnerFadeEnds = 0.15f;

    private float phase;
    private bool initialized;

    private void Awake() => Initialize();

    private void Initialize()
    {
        if (initialized) return;
        initialized = true;

        if (runner != null && runnerRenderer == null)
            runnerRenderer = runner.GetComponentInChildren<Renderer>(true);

        if (segments == null || segments.Length == 0)
        {
            var found = new System.Collections.Generic.List<Renderer>(
                GetComponentsInChildren<Renderer>(true));
            found.Remove(runnerRenderer);   // 러너는 호의 조각이 아니다
            segments = found.ToArray();
        }

        foreach (Renderer r in segments)
            EnsureTransparentMaterial(r);
        EnsureTransparentMaterial(runnerRenderer);
    }

    private void OnEnable()
    {
        Initialize();
        phase = 0f;
    }

    private void Update()
    {
        phase += Time.deltaTime * Mathf.Max(0.01f, flowPerSecond);
        if (phase > 1f) phase -= 1f;

        if (segments != null && segments.Length > 0)
            ApplyFlow(segments, phase, flowWidth, minAlpha, maxAlpha, keepHeadBright);

        UpdateRunner();
    }

    /// <summary>러너를 호 위 phase 지점에 놓고 진행 방향(접선)을 보게 한다.</summary>
    private void UpdateRunner()
    {
        if (runner == null) return;

        float deg = phase * arcSweepDeg;
        float rad = deg * Mathf.Deg2Rad;

        // 회전축 = 로컬 +Y, 호는 +Z에서 시작해 +X 쪽으로 (조각 메시와 같은 규약)
        Vector3 pos = new Vector3(Mathf.Sin(rad) * arcRadius, 0f, Mathf.Cos(rad) * arcRadius);
        Vector3 tangent = new Vector3(Mathf.Cos(rad), 0f, -Mathf.Sin(rad));

        runner.localPosition = pos;
        if (tangent.sqrMagnitude > 1e-6f)
            runner.localRotation = Quaternion.LookRotation(tangent, Vector3.up);

        if (runnerRenderer == null) return;

        // 양 끝에서 서서히 사라지게 — 안 그러면 끝에서 시작점으로 툭 튄다.
        float a = 1f;
        if (runnerFadeEnds > 0f)
        {
            float edge = Mathf.Min(phase, 1f - phase) / runnerFadeEnds;
            a = Mathf.Clamp01(edge);
        }

        // 세그먼트와 같은 규칙 — 알파가 0에 가까우면 렌더러를 꺼서 머티리얼 상태와 무관하게 사라지게 한다.
        bool visible = a > 0.02f;
        if (runnerRenderer.enabled != visible) runnerRenderer.enabled = visible;
        if (!visible) return;

        Color c = BaseColor;
        c.a = Mathf.Lerp(0f, maxAlpha, a);
        SetRendererColor(runnerRenderer, c);
    }
}
