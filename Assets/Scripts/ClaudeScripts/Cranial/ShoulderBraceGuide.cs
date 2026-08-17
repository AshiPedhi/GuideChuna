using UnityEngine;

/// <summary>
/// 이마 견착(어깨 거치) 위치 가이드.
///
/// 견착 국면(③)은 <see cref="CranialPostureStabilizer"/>가 "헤드셋-이마 근접"으로 자세를 근사 판정하는데,
/// 화면에는 <b>어디에 어깨를 대야 하는지</b>가 전혀 표시되지 않았다(안내 문구 오브젝트 하나뿐).
/// → 파지점 구체와 같은 규약으로 이마 표면에 접촉 패치를 띄운다:
///   <b>미접촉 = 연한 붉은색 / 접근 중 = 주황 / 견착 성립 = 초록(알파를 낮춰 시야를 안 가림)</b>.
///
/// 배치: 리그 하위에 두고 <see cref="foreheadTarget"/>을 <b>매 프레임 따라간다</b>(재부모화 안 함).
/// ★환자 뼈에 자식으로 붙이면 컨트롤러의 환자 렌더러 수집(SetPatientVisible)·xray 대상에 걸려
///   견착 성립 순간 마커까지 같이 사라진다. 리그 하위(=수집 제외)에 두는 게 맞다.
///
/// ★표시 구간은 <b>CSV가 정한다</b> — conditionParams에 <c>brace</c>가 있는 substep에서만 뜬다
/// (ScenarioManager가 화살표·골격 포커스와 같은 자리에서 <see cref="SetVisible"/>를 호출).
/// 자세 안정화의 활성 여부로 판단하지 <b>않는다</b>: 지금 두개골 호흡 단계는 전부 <c>gripGate</c>라
/// StartBreathingWindow가 자세 안정화를 켜지 않아, 그 값을 따르면 가이드가 영영 안 뜬다(2026-08-13 실측).
/// </summary>
public class ShoulderBraceGuide : MonoBehaviour
{
    [Header("=== 연결 ===")]
    [Tooltip("선택: 견착 자세(상체 숙임) 프록시. 색 피드백과 이마 타겟 폴백에만 쓴다 — 표시 여부는 CSV가 정한다.")]
    [SerializeField] private CranialPostureStabilizer stabilizer;

    [Tooltip("따라갈 기준점(환자 이마/머리 본). 비우면 stabilizer에 배선된 이마 타겟을 자동으로 쓴다.")]
    [SerializeField] private Transform foreheadTarget;

    [Tooltip("기준점 로컬 기준 위치 보정(m). 머리 본은 이마 표면이 아니라 두상 중심이라 앞·위로 밀어야 한다. " +
             "★본 축 방향은 모델마다 달라 씬 뷰에서 눈으로 맞추는 것이 정답이다.")]
    [SerializeField] private Vector3 localOffset = new Vector3(0f, 0.07f, 0.08f);

    [Tooltip("마커 색을 칠할 렌더러. 비우면 자식에서 처음 찾은 것을 쓴다.")]
    [SerializeField] private Renderer markerRenderer;

    [Tooltip("선택: '어깨를 여기에 대세요' 라벨 등 함께 켜고 끌 오브젝트. 시술자 쪽을 보도록 회전시킨다.")]
    [SerializeField] private GameObject label;

    [Header("=== 색 (파지점 규약과 동일) ===")]
    [Tooltip("★이 가이드는 어깨 접촉을 판정하지 않는다(Quest는 어깨를 트래킹하지 못한다). " +
             "켜면 이미 있는 견착 프록시(헤드셋-이마 근접 = 상체 숙임) 상태를 색으로 비춰 준다 — " +
             "가까워질수록 주황, 숙임이 인정되면 초록. " +
             "끄면 색이 변하지 않는 순수 위치 표시(항상 farColor)가 된다.")]
    [SerializeField] private bool reflectEngagement = true;

    [Tooltip("아직 멀리 있을 때(연한 붉은색).")]
    [SerializeField] private Color farColor = new Color(1f, 0.35f, 0.35f, 0.5f);
    [Tooltip("가까워지는 중(주황). 거리 구간에서 farColor와 보간된다.")]
    [SerializeField] private Color nearColor = new Color(1f, 0.72f, 0.2f, 0.6f);
    [Tooltip("견착 성립(초록).")]
    [SerializeField] private Color engagedColor = Color.green;
    [Range(0f, 1f)]
    [Tooltip("견착 성립 시 알파 배율. 낮출수록 흐려져 시야를 안 가린다(구체를 끄지는 않는다).")]
    [SerializeField] private float engagedAlpha = 0.25f;

    [Tooltip("이 거리(m)부터 주황으로 물들기 시작한다. 성립 임계(engageDistance)까지 선형 보간.")]
    [SerializeField] private float approachDistance = 0.7f;

    [Header("=== 움직임 ===")]
    [Tooltip("미성립 동안 크기를 이 폭만큼 맥동시켜 시선을 끈다(0이면 정지).")]
    [Range(0f, 0.5f)]
    [SerializeField] private float pulseAmount = 0.12f;
    [Tooltip("맥동 속도(초당 주기).")]
    [SerializeField] private float pulseSpeed = 1.6f;
    [Tooltip("견착 성립 시 크기 배율(파지점의 grippedScale과 같은 취지 — 색만으로는 VR에서 판별이 어렵다).")]
    [SerializeField] private float engagedScale = 1.15f;
    [Tooltip("크기 보간 속도.")]
    [SerializeField] private float scaleLerpSpeed = 12f;

    [Header("=== 디버그 ===")]
    [Header("=== 표시 끄기 ===")]
    [Tooltip("★켜면 마커와 라벨을 아예 그리지 않는다(2026-08-13 회의 결정).\n" +
             "이마에 밀착하면 HMD 시야를 가려서 가이드 마커를 없애기로 했다 — " +
             "★<b>견착 동작 자체를 없애는 게 아니라 표시만 지우는 것</b>이다.\n" +
             "이 가이드는 표시 전용이고 판정에 관여하지 않으므로(자세 안정화는 " +
             "CranialPostureStabilizer가 따로 본다) 꺼도 시나리오 진행에는 영향이 없다.\n" +
             "★씬에서 markerRenderer를 직접 꺼 봐야 소용없다 — SetShown()이 견착 국면마다 다시 켠다.")]
    [SerializeField] private bool hideMarker = false;

    [SerializeField] private bool debugLog = false;

    private Vector3 baseScale;
    private bool baseScaleCaptured;
    private bool shown;
    private bool wasEngaged;
    private bool transparencyEnsured;

    /// <summary>지금 견착 국면이라 가이드가 떠 있는가.</summary>
    public bool IsShowing => shown;

    private Transform Target => foreheadTarget != null ? foreheadTarget : stabilizer?.ForeheadTarget;

    void Awake()
    {
        if (markerRenderer == null) markerRenderer = GetComponentInChildren<Renderer>(true);
        CaptureBaseScale();
        EnsureTransparentMaterial();
        SetShown(false);   // 씬에서 켠 채 저장했어도 시작부터 떠 있지 않게 한다(견착 국면에서만 뜬다).
    }

    /// <summary>표시 켜기/끄기. ScenarioManager가 substep 진입마다 CSV의 <c>brace</c> 토큰으로 호출한다.</summary>
    public void SetVisible(bool on)
    {
        if (shown == on) return;
        SetShown(on);
        if (on) FollowTarget();   // 켜지는 프레임부터 제자리에 있게(한 프레임 원점에 번쩍이는 것 방지)
    }

    void LateUpdate()
    {
        if (!shown) return;

        FollowTarget();
        if (hideMarker) return;   // 그릴 게 없으면 색·펄스 계산도 건너뛴다

        // 접촉 판정이 아니라 '상체를 숙였는가'(기존 프록시)를 비추는 것뿐이다. 끄거나 프록시가 없으면 순수 위치 표시.
        bool reflect = reflectEngagement && stabilizer != null;
        ApplyFeedback(reflect && stabilizer.IsInPosition,
                      reflect ? stabilizer.CurrentDistance : Mathf.Infinity);
    }

    /// <summary>기준점을 매 프레임 따라간다(위치·회전 모두). 재부모화하지 않는 이유는 클래스 주석 참조.</summary>
    private void FollowTarget()
    {
        Transform t = Target;
        if (t == null) return;

        transform.position = t.TransformPoint(localOffset);
        transform.rotation = t.rotation;

        // 라벨만 시술자 쪽을 보게 한다(마커 자체는 이마 면을 따라야 접촉 패치처럼 보인다).
        if (label != null && label.activeSelf)
        {
            Transform head = stabilizer != null ? stabilizer.Headset : null;
            if (head != null)
            {
                Vector3 dir = label.transform.position - head.position;
                if (dir.sqrMagnitude > 1e-6f) label.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            }
        }
    }

    /// <summary>거리에 따라 색·크기를 갱신한다. 거리 피드백이 있어야 "더 숙여야 하는지"를 알 수 있다.</summary>
    private void ApplyFeedback(bool engaged, float distance)
    {
        if (markerRenderer != null)
        {
            Color c;
            if (engaged)
            {
                c = new Color(engagedColor.r, engagedColor.g, engagedColor.b, engagedColor.a * engagedAlpha);
            }
            else
            {
                // 성립 임계까지 남은 거리를 0~1로 환산해 붉은색 → 주황으로.
                float engageAt = stabilizer != null ? stabilizer.EngageDistance : 0.3f;
                float span = Mathf.Max(0.01f, approachDistance - engageAt);
                float t = Mathf.Clamp01((approachDistance - distance) / span);
                c = Color.Lerp(farColor, nearColor, t);
            }
            markerRenderer.material.color = c;
        }

        CaptureBaseScale();
        if (baseScaleCaptured)
        {
            float mul = engaged
                ? engagedScale
                : 1f + pulseAmount * Mathf.Sin(Time.time * pulseSpeed * Mathf.PI * 2f);
            Vector3 want = baseScale * mul;
            transform.localScale = Vector3.Lerp(transform.localScale, want,
                                                1f - Mathf.Exp(-scaleLerpSpeed * Time.deltaTime));
        }

        if (engaged != wasEngaged)
        {
            wasEngaged = engaged;
            if (debugLog)
                ChunaLogger.Log($"[ShoulderBraceGuide] 견착 {(engaged ? "<color=green>성립</color>" : "해제")} " +
                                $"(거리 {distance * 100f:F1}cm)");
        }
    }

    private void SetShown(bool on)
    {
        shown = on;
        // ★hideMarker면 국면 상태(shown)는 그대로 두고 '그리기'만 막는다 —
        //   견착 국면 자체는 살아 있어야 CSV 흐름과 나레이션이 어긋나지 않는다.
        bool draw = on && !hideMarker;
        if (markerRenderer != null) markerRenderer.enabled = draw;
        if (label != null) label.SetActive(draw);
        if (!on)
        {
            wasEngaged = false;
            if (baseScaleCaptured) transform.localScale = baseScale;
        }
    }

    private void CaptureBaseScale()
    {
        if (baseScaleCaptured) return;
        baseScale = transform.localScale;
        baseScaleCaptured = true;
    }

    /// <summary>
    /// 마커 머티리얼을 반투명(Standard - Fade)으로 만든다.
    /// ★불투명 머티리얼이면 알파가 통째로 무시돼 이마에 "빨간 덩어리"만 붙는다(파지점에서 겪은 것과 같은 함정).
    /// 공유 머티리얼을 건드리지 않도록 인스턴스에만 적용한다.
    /// </summary>
    private void EnsureTransparentMaterial()
    {
        if (transparencyEnsured || markerRenderer == null) return;
        transparencyEnsured = true;

        var m = markerRenderer.material;
        if (m == null || m.shader == null) return;
        if (!m.HasProperty("_Mode")) return;

        m.SetFloat("_Mode", 3f);
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.DisableKeyword("_ALPHATEST_ON");
        m.EnableKeyword("_ALPHABLEND_ON");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.renderQueue = 3000;
    }

    /// <summary>진단용 한 줄 상태(왜 안 보이는지 판별). 에디터 도구가 호출한다.</summary>
    public string DescribeState()
    {
        string t = Target != null ? Target.name : "<이마 타겟 없음!>";
        return $"{gameObject.name}: stabilizer={(stabilizer != null ? stabilizer.name : "<없음!>")} " +
               $"이마타겟={t} 렌더러={(markerRenderer != null ? markerRenderer.name : "<없음!>")} " +
               $"표시중={shown}";
    }
}
