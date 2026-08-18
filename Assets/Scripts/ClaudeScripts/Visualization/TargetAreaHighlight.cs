using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 타겟 부위 하이라이트 — <b>뼈의 일부 모양 그대로</b> 강조하는 표시(판정하지 않는다).
///
/// ★왜 필요한가(2026-08-14 사용자 요구): "흉추 골격에서 뼈 전체 말고 <b>횡돌기 부분만</b> 모양을 맞춰
/// 강조하고 싶다." 그런데 씬의 흉추는 <c>thoracic_spine</c> <b>오브젝트 하나</b>이고 극돌기·횡돌기가
/// 별도 오브젝트로 나뉘어 있지 않다(실측). 지금 골격 표시(<see cref="SkeletonFocusController"/>)는
/// 렌더러를 껐다 켜는 방식이라 <b>켜고 끄는 최소 단위가 오브젝트</b>다 → 부위 일부만 강조할 수 없다.
///
/// 그래서 <b>그 부위의 메시 조각만 추출해 원본 위에 얹는다.</b>
/// 조각은 에디터 도구가 만든다 — 메뉴 <c>GuideChuna/타겟 부위 하이라이트 만들기 (뼈 일부 추출)</c>.
///   · 원본 메시는 <b>건드리지 않는다</b>(되돌리기 = 이 오브젝트 삭제)
///   · 노멀 방향으로 살짝 부풀려 얹으므로 Z-파이팅이 없다
///   · 커스텀 셰이더를 쓰지 않는다 — 이 프로젝트에서 커스텀 셰이더는 빌드에서 두 번 죽었다
///     (07-30 셰이더 스트립 / 08-13 배리언트 스트립). Standard의 Emission만 쓴다.
///
/// ★색 규약(2026-08-13 회의): <b>주동수 = 진한 녹색 / 보조수 = 연한 녹색.</b>
/// 화살표(<see cref="ForceArrowBase"/>)의 시술자 색과 같은 계열이라 손·화살표·타겟이 한 벌로 읽힌다.
///
/// 켜고 끄는 것은 <see cref="ForceArrowDirector"/>가 화살표와 <b>같은 타이밍</b>에 해 준다
/// (씬에 이미 1개 있으므로 추가 배선이 필요 없다).
/// </summary>
public class TargetAreaHighlight : MonoBehaviour
{
    /// <summary>이 부위를 어느 손이 잡는가 — 색이 갈린다.</summary>
    public enum Role
    {
        /// <summary>주동수(힘을 주는 손). 진한 녹색.</summary>
        주동수,
        /// <summary>보조수(지지하는 손). 연한 녹색.</summary>
        보조수,
        /// <summary>손과 무관한 표시(진단 목표 돌기 등). 노란끼.</summary>
        중립
    }

    /// <summary>언제 보일지.</summary>
    public enum Scope
    {
        /// <summary>교정 국면 전체(<b>파지 단계 포함</b>). 기본값.
        /// ★화살표와 다르다 — 화살표는 '어디로 미는가'라 파지 단계에서 빼지만,
        /// 이 표시는 <b>어디를 잡는가</b>라 파지 단계에 있어야 의미가 있다.</summary>
        교정국면_전체,
        /// <summary>특정 단계에서만.</summary>
        특정_단계만,
        /// <summary>시나리오 내내(진단·재평가 포함).</summary>
        항상
    }

    [Header("=== 언제 보일지 ===")]
    [SerializeField] private Scope showWhen = Scope.교정국면_전체;

    [Tooltip("'특정 단계만'일 때 쓸 단계 이름(CSV stepName). 예: 파지 / 교정")]
    [SerializeField] private string stepName = "";

    [Tooltip("0 = 그 단계 전체. 1 이상이면 그 subStep에서만.")]
    [SerializeField] private int subStepNo = 0;

    [Tooltip("선택. 채우면 국면(phase)까지 일치해야 표시한다.")]
    [SerializeField] private string phaseName = "";

    [Tooltip("선택. 비우면 부모 리그(CranialAdjustmentController)의 시나리오를 물려받는다.")]
    [SerializeField] private string scenarioName = "";

    [Header("=== 색 (회의 규약: 주동수 진녹 / 보조수 연녹) ===")]
    [Tooltip("이 부위를 어느 손이 잡는가. 색 값은 HandRole.cs의 전역 규약에서 온다 — " +
             "여기서는 역할만 고른다.\n" +
             "★2026-08-18: 예전엔 색 세 칸을 오브젝트마다 들고 있어서 색을 바꾸려면 " +
             "씬의 하이라이트를 전부 찾아다녀야 했다. 이제 규약 파일 한 곳만 고치면 된다.")]
    [SerializeField] private Role role = Role.주동수;

    [Header("=== 발광 ===")]
    [Tooltip("발광 세기의 최저/최고. 이 사이를 오가며 맥동한다. 맥동을 끄려면 두 값을 같게.")]
    [SerializeField, Range(0f, 4f)] private float minIntensity = 0.6f;
    [SerializeField, Range(0f, 4f)] private float maxIntensity = 1.8f;
    [Tooltip("초당 맥동 횟수. 0이면 정지(최고 세기로 고정).")]
    [SerializeField, Range(0f, 3f)] private float pulsePerSecond = 0.9f;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool debugLog = false;

    private readonly List<Renderer> targets = new List<Renderer>();
    private float phase;
    private bool initialized;

    /// <summary>지금 이 표시가 쓰는 색 — 전역 규약(<see cref="HandRole"/>)에서 온다.</summary>
    public Color CurrentColor =>
        role == Role.보조수 ? HandRole.보조수색 :
        role == Role.중립 ? HandRole.중립색 : HandRole.주동수색;

    /// <summary>이 표시가 속한 시나리오. 비었으면 부모 리그에서 물려받는다.</summary>
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

    private void Awake() => Initialize();
    private void OnEnable()
    {
        Initialize();
        phase = 0f;
        Apply(maxIntensity);
    }

    private void Initialize()
    {
        if (initialized) return;
        initialized = true;

        targets.Clear();
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            if (r != null) targets.Add(r);

        foreach (Renderer r in targets) EnsureEmissive(r);

        if (debugLog)
            ChunaLogger.Log($"[TargetAreaHighlight] {name} — 렌더러 {targets.Count}개 / {role} / {DescribeMatch()}");
    }

    private void Update()
    {
        if (targets.Count == 0) return;

        if (pulsePerSecond <= 0.001f)
        {
            Apply(maxIntensity);
            return;
        }

        phase += Time.deltaTime * pulsePerSecond;
        if (phase > 1f) phase -= 1f;

        float wave = (Mathf.Sin(phase * 2f * Mathf.PI) + 1f) * 0.5f;
        Apply(Mathf.Lerp(minIntensity, maxIntensity, wave));
    }

    private void Apply(float intensity)
    {
        Color c = CurrentColor;
        for (int i = 0; i < targets.Count; i++)
        {
            Renderer r = targets[i];
            if (r == null) continue;

            Material m = r.material;    // 인스턴스(공유 머티리얼 보호)
            if (m == null) continue;

            if (m.HasProperty("_Color")) m.color = c;
            if (m.HasProperty("_EmissionColor"))
                m.SetColor("_EmissionColor", EmissionOf(c, intensity));
        }
    }

    /// <summary>발광 색 — <b>역할 색의 밝기에 끌려가지 않게</b> 색조만 남기고 밝기를 정규화한다.
    ///
    /// ★왜 (2026-08-18): 발광을 역할 색에 그대로 곱하고 있었다. 그래서 08-18에 주동수색을
    /// 대비 때문에 어둡게(#26FF51 → #05BF29) 바꾸자 <b>하이라이트가 같이 어두워졌다</b>
    /// (사용자: "하이라이트가 잘 안 보인다"). 색 규약은 '무슨 색인가'를 정하는 것이지
    /// '얼마나 밝게 빛나는가'를 정하는 게 아니므로 둘을 분리한다.
    ///
    /// 최대 채널을 1로 맞춘 뒤 세기를 곱하므로, 규약 색을 아무리 어둡게 바꿔도 발광 밝기는 유지된다.
    /// <see cref="Boost"/>는 씬에 직렬화된 maxIntensity(1.8)를 코드에서 더 올리기 위한 배수다
    /// — 인스펙터 값을 고치지 않아도 잘 보이게 하기 위한 것.</summary>
    private const float Boost = 1.5f;

    private static Color EmissionOf(Color c, float intensity)
    {
        float mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        if (mx > 0.001f) c = new Color(c.r / mx, c.g / mx, c.b / mx, 1f);
        float k = intensity * Boost;
        return new Color(c.r * k, c.g * k, c.b * k, 1f);
    }

    /// <summary>Standard의 발광을 켠다. ★키워드를 안 켜면 <c>_EmissionColor</c>를 아무리 넣어도 안 빛난다.</summary>
    private static void EnsureEmissive(Renderer r)
    {
        if (r == null) return;
        Material m = r.material;
        if (m == null || m.shader == null) return;

        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

        // 불투명으로 둔다 — 반투명은 ZWrite를 꺼서 뼈 안쪽 면이 비쳐 보인다(화살표에서 겪은 것과 같은 함정).
        if (m.HasProperty("_Mode"))
        {
            m.SetFloat("_Mode", 0f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            m.SetInt("_ZWrite", 1);
            m.DisableKeyword("_ALPHABLEND_ON");
            m.renderQueue = -1;
        }
    }

    /// <summary>이번 substep에 보여야 하는가. 규칙은 화살표와 같은 것을 쓴다.</summary>
    public bool Matches(string scenario, string phase, string step, int subNo)
    {
        if (!ForceArrowBase.ScenarioMatch(this, ResolvedScenario, scenario)) return false;

        switch (showWhen)
        {
            case Scope.항상:
                return true;

            case Scope.특정_단계만:
                if (string.IsNullOrWhiteSpace(stepName)) return false;
                if (!Same(stepName, step)) return false;
                if (subStepNo > 0 && subStepNo != subNo) return false;
                if (!string.IsNullOrWhiteSpace(phaseName) && !Same(phaseName, phase)) return false;
                return true;

            default:   // 교정국면_전체 — 파지 단계도 포함한다
                return !string.IsNullOrEmpty(phase) && phase.Contains("교정");
        }
    }

    /// <summary>점검 도구 출력용.</summary>
    public string DescribeMatch()
    {
        string who = $"[{(string.IsNullOrWhiteSpace(ResolvedScenario) ? "모든 시나리오" : ResolvedScenario)}] {role}";
        switch (showWhen)
        {
            case Scope.항상: return who + " · 항상";
            case Scope.특정_단계만:
                if (string.IsNullOrWhiteSpace(stepName)) return who + " · 특정 단계만 — ★단계 이름이 비어 있음";
                return who + " · " + (string.IsNullOrWhiteSpace(phaseName) ? "" : phaseName.Trim() + " / ") +
                       stepName.Trim() + (subStepNo > 0 ? $" ({subStepNo})" : " (단계 전체)");
            default: return who + " · 교정 국면 전체(파지 포함)";
        }
    }

    private static bool Same(string a, string b)
    {
        if (a == null || b == null) return false;
        return a.Trim().Equals(b.Trim(), System.StringComparison.OrdinalIgnoreCase);
    }
}
