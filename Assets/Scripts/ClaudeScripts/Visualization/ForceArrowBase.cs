using UnityEngine;

/// <summary>
/// 힘의 방향 표시의 공통 뼈대 (표시 전용 — 판정하지 않는다).
/// 직선(<see cref="ForceArrow"/>)과 회전(<see cref="ForceArcArrow"/>)이 이걸 상속한다.
///
/// 공통으로 갖는 것 = 주체(시술자/환자) 색 · 반투명 강제 · 진행 방향 흐름.
///
/// ★호흡 위상 연동은 넣지 않는다(08-10 사용자 결정): 두개골 교정은 호흡 위상과 무관하게
/// 힘을 줘서 잠그는 동작이라, 들숨·날숨에 따라 화살표가 흐려지면 오히려 틀린 지시가 된다.
/// </summary>
public abstract class ForceArrowBase : MonoBehaviour
{
    /// <summary>힘의 주체. 등척성 운동이 아닌 이상 주체는 시술자다(기본).</summary>
    public enum Actor
    {
        /// <summary>시술자가 가하는 힘.</summary>
        Practitioner,
        /// <summary>환자가 능동적으로 내는 힘 — 등척성 운동에서 환자가 저항하는 방향.</summary>
        Patient
    }

    /// <summary>언제 보일지. 기본은 <b>파지·준비를 뺀</b> 교정 국면이다.</summary>
    public enum ShowScope
    {
        /// <summary>교정 국면에서 <b>파지·준비 단계를 빼고</b> 표시(잠금·견착·호흡·교정).
        /// <b>기본값 — 아무것도 안 적어도 된다.</b>
        /// ★08-11 사용자 지시: "파지할 때부터 화살표를 보여주면 잠그는 단계가 의미가 없다."
        /// 파지는 '어디를 잡는가'를 익히는 단계라 방향 지시가 오히려 방해가 된다.</summary>
        교정국면_파지제외,
        /// <summary>특정 단계에서만 표시. 한 국면 안에서 힘의 방향이 바뀔 때만 쓴다(PJ의 전환 전후).
        ///
        /// ★<b>이 값은 <see cref="IsSetupStep"/>를 건너뛴다</b> — 파지·준비·견착에도 뜬다는 뜻이다.
        /// 그래서 자세 단계에 이 값을 걸면 안 된다(PJ 견착 그룹이 그랬다: 기본 스코프가 이미 견착을
        /// 빼고 있는데 이 그룹만 우회해서 "견착할 때 화살표 왜 나오냐"가 다시 났다. 2026-08-17).
        ///
        /// ★CSV의 stepName을 그대로 적는 값이라 <b>단계 이름을 바꾸면 여기도 같이 바꿔야 한다</b> —
        /// 안 바꾸면 그룹이 어느 단계와도 매칭되지 않아 화살표가 조용히 사라진다.
        ///
        /// ★<b>화살표 오브젝트 이름을 방향의 근거로 삼지 말 것</b>(2026-08-17): 이름은 CSV 안내문에서
        /// 따온 라벨이라 안내문이 틀리면 이름도 같이 틀린다. 방향의 근거는 씬에 겨눠 둔 트랜스폼이다.</summary>
        특정_단계만,
        /// <summary>시나리오 내내 표시(진단·재평가 포함). 거의 안 쓴다.</summary>
        항상,
        /// <summary>★<b>아예 안 쓴다.</b> 어느 단계에서도 뜨지 않는다.
        ///
        /// ★<b>씬에서 GameObject를 비활성으로 꺼 봐야 소용없다</b>(2026-08-17 사용자 지적) —
        /// <see cref="ForceArrowDirector"/>가 단계가 바뀔 때마다 매칭되는 화살표를
        /// <c>SetActive(true)</c>로 <b>다시 켜기</b> 때문이다. 지우지 않고 잠시 빼두려면 이 값을 쓴다.
        /// (예: PM의 유양돌기 손은 고정만 하므로 힘 방향 표시가 필요 없다.)
        ///
        /// ★열거값을 <b>맨 뒤에 추가</b>했으므로 이미 씬에 저장된 값(0·1·2)은 그대로 유지된다.</summary>
        사용안함
    }

    [Header("=== 언제 보일지 (그룹에 넣었으면 그룹이 이긴다) ===")]
    [Tooltip("기본 '교정국면 파지제외' = 잠금·견착·호흡·교정에만 표시한다.\n" +
             "★파지·준비 단계와 진단·재평가에는 안 나온다 — 파지에서부터 방향을 보여주면 잠그는 단계가 의미를 잃는다.\n" +
             "아래 칸은 '특정 단계만'일 때만 쓴다.")]
    [SerializeField] private ShowScope showWhen = ShowScope.교정국면_파지제외;

    [Tooltip("'특정 단계만'일 때 쓸 단계 이름(CSV stepName). 예: 호흡유도 / 교정")]
    [SerializeField] private string stepName = "";

    [Tooltip("0 = 그 단계 전체. 1 이상이면 그 subStep에서만 표시.")]
    [SerializeField] private int subStepNo = 0;

    [Tooltip("선택. 채우면 국면(phase)까지 일치해야 표시한다.")]
    [SerializeField] private string phaseName = "";

    [Tooltip("선택. 비우면 부모 리그(CranialAdjustmentController)의 시나리오를 자동으로 물려받는다.\n" +
             "★리그 밖에 둔 화살표만 직접 적는다. 비어 있고 부모 리그도 없으면 '모든 시나리오'가 된다.")]
    [SerializeField] private string scenarioName = "";

    /// <summary>이 화살표가 속한 시나리오. 비었으면 부모 리그에서 물려받는다(캐시).</summary>
    public string ResolvedScenario
    {
        get
        {
            if (scenarioCached) return resolvedScenario;
            scenarioCached = true;
            resolvedScenario = scenarioName;
            if (string.IsNullOrWhiteSpace(resolvedScenario))
            {
                var rig = GetComponentInParent<CranialAdjustmentController>(true);
                if (rig != null) resolvedScenario = rig.ScenarioName;
            }
            return resolvedScenario;
        }
    }

    private string resolvedScenario;
    private bool scenarioCached;

    [Header("=== 흐름 표현 ===")]
    [Tooltip("★경로(호·자루) 전체를 켜 둔 채 밝기만 흐르게 하면 '지금 어디가 진행 중인지'가 묻힌다.\n" +
             "켜면 진행 위치의 1~2칸만 빛나고 나머지는 사라진다 — 방향이 훨씬 또렷하다(08-11 사용자 지시).\n" +
             "끄면 예전 방식(전체가 은은히 보이고 밝기만 흐름).")]
    [SerializeField] private bool sharpFlow = true;

    [Tooltip("동시에 빛나는 칸 수. 1~2가 권장값이다.")]
    [SerializeField, Range(0.6f, 3f)] private float litSegments = 1.2f;

    [Tooltip("빛나지 않는 칸의 알파. 0이면 완전히 사라진다(기본). 경로를 희미하게 남기고 싶으면 0.05~0.1.")]
    [SerializeField, Range(0f, 0.3f)] private float dimAlpha = 0f;

    [Header("=== 주체 (색으로 구분) ===")]
    [Tooltip("Practitioner = 시술자가 가하는 힘(기본) / Patient = 등척성에서 환자가 내는 힘")]
    [SerializeField] protected Actor actor = Actor.Practitioner;
    [Tooltip("시술자 색 (주황)")]
    // ★시술자 = 초록 #26FF51 (2026-08-12 사용자 지정). 기본값이 주황이라 새로 만든 화살표마다
    //   색을 다시 칠해야 했다 — 기본값 자체를 초록으로 바꾼다.
    [SerializeField] protected Color practitionerColor = new Color(0.149f, 1f, 0.318f, 1f);
    [Tooltip("환자 색 (청록)")]
    [SerializeField] protected Color patientColor = new Color(0.25f, 0.8f, 0.95f, 1f);

    [Tooltip("★이 화살표가 <b>주동수인지 보조수인지</b> 직접 지정한다(2026-08-18 사용자 요구).\n\n" +
             "그전에는 시술자 화살표가 주동수든 보조수든 전부 같은 진녹이라 둘이 구분되지 않았다.\n" +
             "색 값은 HandRole.cs의 전역 규약에서 온다 — 여기서는 역할만 고르고, " +
             "색을 바꾸려면 그 파일 한 곳만 고치면 모든 화살표·하이라이트가 따라온다.\n\n" +
             "'기존색 유지'(기본)면 위의 주체(Actor) 색을 그대로 쓴다 — 지금 씬은 아무것도 안 바뀐다.\n" +
             "★시나리오 단위 자동 판정은 없다: PJ 진단처럼 한 단계 안에서 좌우가 번갈아 바뀌는 " +
             "술기가 있어서 자동으로는 맞출 수 없다.")]
    [SerializeField] protected HandRole.Role colorRole = HandRole.Role.기존색유지;

    /// <summary>
    /// 이 화살표의 기준 색.
    /// <see cref="colorRole"/>을 지정했으면 전역 규약 색이 이기고, 아니면 종전대로 주체(Actor) 색을 쓴다.
    /// </summary>
    protected Color BaseColor =>
        HandRole.UsesRoleColor(colorRole) ? HandRole.ColorOf(colorRole)
        : actor == Actor.Patient ? patientColor
        : practitionerColor;

    /// <summary>점검 도구 출력용 — 지금 무슨 색 규칙을 쓰는가.</summary>
    public string DescribeColorRole() =>
        HandRole.UsesRoleColor(colorRole) ? colorRole.ToString() : $"기존색({actor})";

    [Header("=== 재질 · 크기 ===")]
    [Tooltip("★기본 = 불투명. 반투명(Standard Fade)은 ZWrite를 끄기 때문에 <b>같은 메시의 앞면과 뒷면이 서로 비쳐</b> " +
             "보는 각도에 따라 어떤 면은 뚫려 보이고 어떤 면은 겹쳐서 진해진다(2026-08-14 사용자 보고: " +
             "\"묘하게 어느 면은 투명하게 보인다\"). 불투명이면 그 현상도, 08-13에 겪은 " +
             "<b>빌드 반투명 배리언트 스트립</b>도 같이 사라진다.\n" +
             "★불투명에서는 알파가 무시되므로 밝기(RGB)로 흐름·펄스를 표현한다 — 아래 min/max는 그대로 쓰인다.\n" +
             "켜면 예전처럼 반투명으로 그린다. 겹쳐 보여야만 하는 배치에서만 쓸 것.")]
    [SerializeField] protected bool useTransparency = false;

    [Tooltip("화살표 크기 배율. 씬에 직렬화된 localScale은 그대로 두고 <b>실행 시에만</b> 곱한다.\n" +
             "★크기는 씬 인스턴스마다 따로 저장돼 있어(직선 8 · 호 6개) 코드 기본값으로 일괄 조정할 방법이 이것뿐이다.\n" +
             "1 = 원래 크기. 되돌리려면 1로 두면 된다.")]
    [SerializeField, Range(0.2f, 4f)] protected float sizeMultiplier = 1.6f;

    /// <summary>씬에 저장된 크기에 배율을 먹인 값. 각 화살표가 기준 크기를 잡을 때 쓴다.</summary>
    protected Vector3 ScaledBase(Vector3 sceneScale) => sceneScale * Mathf.Max(0.05f, sizeMultiplier);

    /// <summary>
    /// 밝기와 투명도를 한 값(0~1)으로 다룬다.
    /// ★불투명 모드에서는 알파가 통째로 무시되므로 <b>RGB를 어둡게</b> 해서 같은 인상을 만든다.
    /// 이렇게 해 두면 흐름·펄스 코드가 두 모드에서 그대로 동작한다.
    /// </summary>
    protected Color Shade(Color c, float t01)
    {
        if (useTransparency)
        {
            c.a = t01;
            return c;
        }

        float k = Mathf.Lerp(0.25f, 1f, Mathf.Clamp01(t01));
        return new Color(c.r * k, c.g * k, c.b * k, 1f);
    }

    /// <summary>인스펙터에 뭐라고 적혔는지 — 점검 도구 출력용.</summary>
    public string DescribeMatch() =>
        $"[{(string.IsNullOrWhiteSpace(ResolvedScenario) ? "모든 시나리오" : ResolvedScenario)}] " +
        Describe(showWhen, stepName, subStepNo, phaseName);

    public bool Matches(string scenario, string phase, string step, int subNo) =>
        ScenarioMatch(this, ResolvedScenario, scenario) &&
        ScopeMatch(showWhen, stepName, subStepNo, phaseName, phase, step, subNo);

    /// <summary>
    /// ★2026-08-12 신설 — 화살표에 시나리오 개념이 없어서 <b>OM·PM 화살표가 PJ 실습 중에도 같이 켜졌다</b>
    /// (사용자 보고: "굴곡외회 신전내회가 왜 한번에 나와"). 기본 스코프가 '교정국면_파지제외'라
    /// 단계 이름이 비어 있으면 어느 시나리오든 교정 국면 내내 표시됐던 것이다.
    ///
    /// 이제 화살표는 자기 리그의 시나리오에서만 켜진다. 소속을 못 찾은 화살표(리그 밖)는
    /// 예전처럼 모든 시나리오에서 동작한다 — 기존 배치를 깨지 않기 위해서다.
    ///
    /// ★<b>이름 비교에 기대지 않는다</b>(2026-08-12 재수정). 시나리오를 개명했을 때
    /// 씬 리그의 scenarioName만 옛 이름으로 남으면 화살표가 통째로 조용히 사라졌다
    /// (제1늑골 개명 직후 실제로 발생). ScenarioManager는 <b>현재 시나리오의 리그 하나만 활성</b>으로
    /// 두므로, "내 리그가 살아 있는가"를 보는 편이 개명에 흔들리지 않고 더 정확하다.
    /// 이름 비교는 리그를 못 찾았을 때의 보조 수단으로만 쓴다.
    /// </summary>
    public static bool ScenarioMatch(Component arrow, string owner, string current)
    {
        var rig = arrow != null ? arrow.GetComponentInParent<CranialAdjustmentController>(true) : null;
        if (rig != null) return rig.gameObject.activeInHierarchy;   // 내 리그가 이번 시나리오의 리그인가

        if (string.IsNullOrWhiteSpace(owner)) return true;      // 소속 불명 = 어디서나(구버전 호환)
        if (string.IsNullOrWhiteSpace(current)) return true;    // 시나리오를 모르면 막지 않는다
        return Same(owner, current);
    }

    /// <summary>
    /// 표시 판정 — 화살표와 그룹이 같은 규칙을 쓴다.
    /// ★국면 단위가 기본인 이유: 힘은 잠금부터 교정까지 <b>계속</b> 주는 것이라
    /// substep마다 지정하게 하면 쓸데없는 손일이고, 중간을 빠뜨리면 화살표가 깜빡인다
    /// (xray의 restoreEachSubStep에서 겪은 것과 같은 함정).
    /// </summary>
    public static bool ScopeMatch(ShowScope scope, string wantStep, int wantSubNo, string wantPhase,
                                  string phase, string step, int subNo)
    {
        switch (scope)
        {
            case ShowScope.사용안함:
                return false;   // 어느 단계에서도 안 뜬다 — Director가 다시 켜지도 못한다

            case ShowScope.항상:
                return true;

            case ShowScope.특정_단계만:
                if (string.IsNullOrWhiteSpace(wantStep)) return false;
                if (!Same(wantStep, step)) return false;
                if (wantSubNo > 0 && wantSubNo != subNo) return false;
                if (!string.IsNullOrWhiteSpace(wantPhase) && !Same(wantPhase, phase)) return false;
                return true;

            default:   // 교정국면_파지제외
                // ScenarioManager가 교정 파지점을 남길 때 쓰는 것과 같은 판정(phaseName에 '교정' 포함).
                if (string.IsNullOrEmpty(phase) || !phase.Contains("교정")) return false;
                // ★파지·준비 단계는 뺀다(08-11 사용자 지시) — 방향은 '잠그는 단계'부터 보여준다.
                return !IsSetupStep(step);
        }
    }

    /// <summary>'어디를 잡는가·어떻게 자세를 만드는가'를 익히는 단계인가 = 힘의 방향을 아직 보여주면 안 되는 단계.
    /// 파지 / 준비 / 자세준비 / <b>견착</b> (제1늑골의 '파지1·파지2'처럼 뒤에 번호가 붙어도 잡힌다).
    ///
    /// ★<b>견착 추가(2026-08-17)</b>: 08-17에 순서를 '파지 → 견착 → 회전'으로 바로잡으면서
    /// 견착이 회전보다 앞으로 왔는데, 견착은 <b>팔을 이마에 붙이는 자세 단계</b>이지 힘을 주는 단계가 아니다.
    /// 그런데 화살표가 거기서부터 떠서 사용자 지적("견착할 때 화살표 왜 나오냐").
    /// 08-11에 파지를 뺀 것과 같은 이유다 — <b>방향은 '잠그는 단계'부터</b> 보여준다.</summary>
    private static bool IsSetupStep(string step)
    {
        if (string.IsNullOrWhiteSpace(step)) return false;
        string s = step.Trim();
        return s.Contains("파지") || s.Contains("준비") || s.Contains("견착");
    }

    public static string Describe(ShowScope scope, string step, int subNo, string phase)
    {
        switch (scope)
        {
            case ShowScope.사용안함: return "사용 안 함 (어느 단계에서도 안 뜸)";
            case ShowScope.항상: return "항상";
            case ShowScope.특정_단계만:
                if (string.IsNullOrWhiteSpace(step)) return "특정 단계만 — ★단계 이름이 비어 있음";
                return (string.IsNullOrWhiteSpace(phase) ? "" : phase.Trim() + " / ") +
                       step.Trim() + (subNo > 0 ? $" ({subNo})" : " (단계 전체)");
            default: return "교정 국면 전체";
        }
    }

    private static bool Same(string a, string b)
    {
        if (a == null || b == null) return false;
        return a.Trim().Equals(b.Trim(), System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 조각 배열에 "진행 방향으로 흐르는 빛"을 입힌다. 직선·회전이 같은 코드를 쓴다.
    /// ★셰이더를 쓰지 않는 이유 = 커스텀 셰이더가 빌드에서 스트립돼 xray가 죽은 전례.
    /// 조각마다 알파에 위상차를 주는 것만으로 방향이 읽힌다.
    /// </summary>
    /// <param name="segments">배열 순서 = 진행 순서(꼬리 → 머리). 마지막이 화살촉.</param>
    /// <param name="phase01">0~1로 순환하는 흐름 위상.</param>
    /// <param name="flowWidth">빛이 지나가는 폭(조각 수 기준).</param>
    /// <param name="keepHeadBright">화살촉을 항상 진하게 유지할지. 종착점이 깜빡이면 방향이 흐려진다.</param>
    protected void ApplyFlow(Renderer[] segments, float phase01, float flowWidth,
                             float minAlpha, float maxAlpha, bool keepHeadBright)
    {
        if (segments == null || segments.Length == 0) return;

        // ★sharpFlow(기본 켬)면 인스펙터의 폭·최소알파 대신 '1~2칸만 빛난다' 규칙이 이긴다.
        //   화살촉 상시 점등도 끈다 — 종착점이 계속 밝으면 빛나는 칸이 3개로 보여 효과가 반감된다.
        //   ★신규 필드라 이미 씬에 배치된 화살표에도 코드 기본값이 그대로 먹는다(재배치 불필요).
        if (sharpFlow)
        {
            flowWidth = litSegments;
            minAlpha = dimAlpha;
            keepHeadBright = false;
        }

        int n = segments.Length;
        Color baseColor = BaseColor;
        float head = phase01 * n;

        for (int i = 0; i < n; i++)
        {
            float a;
            if (keepHeadBright && i == n - 1)
            {
                a = maxAlpha;
            }
            else
            {
                // 흐름 머리에서 뒤로 갈수록 어두워진다(순환 거리).
                float d = head - i;
                if (d < 0f) d += n;
                float t = Mathf.Clamp01(1f - d / Mathf.Max(0.01f, flowWidth));
                a = Mathf.Lerp(minAlpha, maxAlpha, t);
            }

            // ★알파만 낮추면 '경로 전체가 그대로 보이는' 개체가 생긴다(08-11 증상: 회전 화살표 2개 중
            //   하나만 1~2칸 점등이 되고 나머지는 멀쩡한 호 위에 러너만 움직였다).
            //   머티리얼이 Fade가 아니거나 셰이더가 알파를 무시하면 알파는 통째로 버려지기 때문 →
            //   <b>렌더러 자체를 껐다 켠다.</b> 그러면 머티리얼 상태와 무관하게 항상 같은 모양이 된다.
            //   (불투명 모드에서는 이게 유일한 숨김 수단이기도 하다.)
            bool visible = a > 0.02f;
            if (segments[i] != null && segments[i].enabled != visible) segments[i].enabled = visible;
            if (!visible) continue;

            SetRendererColor(segments[i], Shade(baseColor, a));
        }
    }

    /// <summary>
    /// 머티리얼을 <see cref="useTransparency"/>에 맞춰 불투명 / 반투명(Standard - Fade)으로 맞춘다.
    /// 공유 머티리얼을 건드리지 않도록 인스턴스에만 적용한다.
    ///
    /// ★기본이 <b>불투명</b>인 이유(2026-08-14): 반투명은 ZWrite를 꺼야 해서 같은 메시의 앞뒤 면이
    /// 서로 비쳐 보인다. 화살촉처럼 두께가 있는 입체에서 "어느 면은 투명하게 보인다"로 나타난다.
    /// 게다가 빌드에 Fade 머티리얼이 없으면 <c>_ALPHABLEND_ON</c> 배리언트가 스트립돼 빌드에서만 깨진다.
    /// </summary>
    protected void EnsureMaterialMode(Renderer r)
    {
        if (r == null) return;
        Material m = r.material;   // 인스턴스 생성(공유 머티리얼 보호)
        if (m == null || m.shader == null) return;
        if (!m.HasProperty("_Mode")) return;   // Standard 계열이 아니면 건드리지 않는다

        if (useTransparency)
        {
            m.SetFloat("_Mode", 3f);               // Fade
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = 3000;
            return;
        }

        m.SetFloat("_Mode", 0f);                   // Opaque
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
        m.SetInt("_ZWrite", 1);
        m.DisableKeyword("_ALPHATEST_ON");
        m.DisableKeyword("_ALPHABLEND_ON");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.renderQueue = -1;                        // 셰이더 기본(Geometry)으로 되돌린다
    }

    private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
    private static MaterialPropertyBlock colorBlock;

    /// <summary>렌더러 색을 <b>MaterialPropertyBlock으로</b> 칠한다.
    ///
    /// ★예전에는 <c>r.material.color</c>였다. 그러면 렌더러마다 머티리얼 <b>인스턴스가 생기고</b>,
    /// 에디트 모드에서 부르면 그 임시 머티리얼이 씬에 눌어붙는다(07-27 xray 사고의 유력 원인).
    /// 프로퍼티 블록은 직렬화되지 않아 씬을 더럽히지 않으므로 <b>에디터 미리보기와 런타임이 같은 경로</b>를 쓴다.</summary>
    protected static void SetRendererColor(Renderer r, Color c)
    {
        if (r == null) return;
        Material shared = r.sharedMaterial;
        if (shared == null || !shared.HasProperty(ColorPropertyId)) return;

        if (colorBlock == null) colorBlock = new MaterialPropertyBlock();
        r.GetPropertyBlock(colorBlock);
        colorBlock.SetColor(ColorPropertyId, c);
        r.SetPropertyBlock(colorBlock);
    }

    /// <summary>지금 역할 색을 <b>펄스 없이</b> 그대로 칠한다. 에디터에서 색을 바로 확인하기 위한 것.</summary>
    public void ApplyRoleColorNow()
    {
        Renderer[] all = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < all.Length; i++) SetRendererColor(all[i], BaseColor);
    }

#if UNITY_EDITOR
    /// <summary>인스펙터에서 colorRole을 바꾸면 <b>씬 뷰에도 바로</b> 반영한다.
    ///
    /// ★왜 필요한가(2026-08-18 사용자 지적: "화살표마다 지정한 걸로 색이 바뀌어야지 안 바뀐다"):
    /// 색은 Update에서만 칠해지는데 이 컴포넌트들은 ExecuteAlways가 아니라 <b>Play 중에만</b> 돈다.
    /// 그래서 역할을 지정해도 에디터에서는 아무 변화가 없어 '적용이 안 된다'로 보였다.
    /// (Play에서는 원래도 정상 동작했다 — 칠하는 코드 자체는 BaseColor를 옳게 읽고 있었다.)</summary>
    protected virtual void OnValidate()
    {
        if (Application.isPlaying) return;
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null) return;                 // 사이에 삭제됐을 수 있다
            if (Application.isPlaying) return;
            ApplyRoleColorNow();
        };
    }
#endif
}
