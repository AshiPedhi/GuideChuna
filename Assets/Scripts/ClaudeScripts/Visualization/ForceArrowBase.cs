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

    /// <summary>언제 보일지. 기본은 교정 국면 전체 — 힘은 파지부터 교정까지 계속 주는 것이라 그게 맞다.</summary>
    public enum ShowScope
    {
        /// <summary>교정 국면(파지·자세준비·호흡유도·교정) 내내 표시. <b>기본값 — 아무것도 안 적어도 된다.</b></summary>
        교정국면_전체,
        /// <summary>특정 단계에서만 표시. 한 국면 안에서 힘의 방향이 바뀔 때만 쓴다(PJ 호흡유도↔교정).</summary>
        특정_단계만,
        /// <summary>시나리오 내내 표시(진단·재평가 포함). 거의 안 쓴다.</summary>
        항상
    }

    [Header("=== 언제 보일지 (그룹에 넣었으면 그룹이 이긴다) ===")]
    [Tooltip("기본 '교정국면 전체' = 파지→자세준비→호흡유도→교정 내내 표시하고 진단·재평가에는 안 나온다.\n" +
             "힘은 파지부터 교정까지 계속 주는 것이라 이게 기본이다. 아래 칸은 '특정 단계만'일 때만 쓴다.")]
    [SerializeField] private ShowScope showWhen = ShowScope.교정국면_전체;

    [Tooltip("'특정 단계만'일 때 쓸 단계 이름(CSV stepName). 예: 호흡유도 / 교정")]
    [SerializeField] private string stepName = "";

    [Tooltip("0 = 그 단계 전체. 1 이상이면 그 subStep에서만 표시.")]
    [SerializeField] private int subStepNo = 0;

    [Tooltip("선택. 채우면 국면(phase)까지 일치해야 표시한다.")]
    [SerializeField] private string phaseName = "";

    [Header("=== 주체 (색으로 구분) ===")]
    [Tooltip("Practitioner = 시술자가 가하는 힘(기본) / Patient = 등척성에서 환자가 내는 힘")]
    [SerializeField] protected Actor actor = Actor.Practitioner;
    [Tooltip("시술자 색 (주황)")]
    [SerializeField] protected Color practitionerColor = new Color(1f, 0.55f, 0.15f, 1f);
    [Tooltip("환자 색 (청록)")]
    [SerializeField] protected Color patientColor = new Color(0.25f, 0.8f, 0.95f, 1f);

    protected Color BaseColor => actor == Actor.Patient ? patientColor : practitionerColor;

    /// <summary>인스펙터에 뭐라고 적혔는지 — 점검 도구 출력용.</summary>
    public string DescribeMatch() => Describe(showWhen, stepName, subStepNo, phaseName);

    public bool Matches(string phase, string step, int subNo) =>
        ScopeMatch(showWhen, stepName, subStepNo, phaseName, phase, step, subNo);

    /// <summary>
    /// 표시 판정 — 화살표와 그룹이 같은 규칙을 쓴다.
    /// ★교정국면_전체가 기본인 이유: 힘은 파지부터 교정까지 <b>계속</b> 주는 것이라
    /// substep마다 지정하게 하면 쓸데없는 손일이고, 중간을 빠뜨리면 화살표가 깜빡인다
    /// (xray의 restoreEachSubStep에서 겪은 것과 같은 함정).
    /// </summary>
    public static bool ScopeMatch(ShowScope scope, string wantStep, int wantSubNo, string wantPhase,
                                  string phase, string step, int subNo)
    {
        switch (scope)
        {
            case ShowScope.항상:
                return true;

            case ShowScope.특정_단계만:
                if (string.IsNullOrWhiteSpace(wantStep)) return false;
                if (!Same(wantStep, step)) return false;
                if (wantSubNo > 0 && wantSubNo != subNo) return false;
                if (!string.IsNullOrWhiteSpace(wantPhase) && !Same(wantPhase, phase)) return false;
                return true;

            default:   // 교정국면_전체
                // ScenarioManager가 교정 파지점을 남길 때 쓰는 것과 같은 판정(phaseName에 '교정' 포함).
                return !string.IsNullOrEmpty(phase) && phase.Contains("교정");
        }
    }

    public static string Describe(ShowScope scope, string step, int subNo, string phase)
    {
        switch (scope)
        {
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

            Color c = baseColor;
            c.a = a;
            SetRendererColor(segments[i], c);
        }
    }

    /// <summary>
    /// 머티리얼을 반투명(Standard - Fade)으로 만든다.
    /// ★불투명 머티리얼이면 알파가 통째로 무시된다(파지점 구체에서 겪은 것과 같은 함정).
    /// 공유 머티리얼을 건드리지 않도록 인스턴스에만 적용한다.
    /// </summary>
    protected static void EnsureTransparentMaterial(Renderer r)
    {
        if (r == null) return;
        Material m = r.material;   // 인스턴스 생성(공유 머티리얼 보호)
        if (m == null || m.shader == null) return;
        if (!m.HasProperty("_Mode")) return;   // Standard 계열이 아니면 건드리지 않는다

        m.SetFloat("_Mode", 3f);               // Fade
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.DisableKeyword("_ALPHATEST_ON");
        m.EnableKeyword("_ALPHABLEND_ON");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.renderQueue = 3000;
    }

    protected static void SetRendererColor(Renderer r, Color c)
    {
        if (r == null) return;
        Material m = r.material;
        if (m != null && m.HasProperty("_Color")) m.color = c;
    }
}
