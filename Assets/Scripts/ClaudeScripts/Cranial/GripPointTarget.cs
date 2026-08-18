using UnityEngine;

/// <summary>파지점이 어느 접촉부에 대응되는지 (Model B: 손가락별 5점 파지).
/// 컨트롤러가 해당 손끝(또는 손바닥)에 트리거 콜라이더를 자동 생성해 이 타겟에 연결한다.
/// Palm = 손 중앙(중지 MCP 기준 큰 콜라이더) — 왼손 후두골 거치용.</summary>
public enum CranialFinger { Thumb, Index, Middle, Ring, Pinky, Palm }

/// <summary>
/// 파지 포인트 판정 (기능1) - 두개골 타겟 콜라이더에 부착.
/// 파지 성립 = (정답 손끝 콜라이더가 트리거 진입) AND (포즈 인식 통과 = PoseRecognized).
/// 트리거 콜라이더(isTrigger=true), Rigidbody 불필요. 포즈 인식 값은
/// CranialAdjustmentController가 HandPoseComparator 결과로 매 프레임 주입한다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class GripPointTarget : MonoBehaviour
{
    [Header("=== 손 식별 ===")]
    [Tooltip("이 타겟에 닿아야 하는 손끝 콜라이더 (씬에서 직접 연결 권장)")]
    [SerializeField] private Collider expectedFingerCollider;
    [Tooltip("콜라이더 직접 연결 대신 태그로 식별할 경우 사용")]
    [SerializeField] private string expectedFingerTag = "";

    [Tooltip("이 파지점에 닿아야 하는 손가락 (Model B). 컨트롤러가 해당 손가락 끝에 트리거 콜라이더를 자동 생성해 연결한다.")]
    [SerializeField] private CranialFinger finger = CranialFinger.Index;
    public CranialFinger Finger => finger;

    [Header("=== 디버그 ===")]
    [Tooltip("M1(파지 포즈 재녹화) 전 테스트용 - 포즈 인식 무시하고 트리거만으로 파지 성립")]
    [SerializeField] private bool bypassPoseCheck = false;

    [Header("=== 피드백 (선택) ===")]
    [Tooltip("★끄면 이 파지점은 <b>'선택'</b>이 되어 파지 성립을 막지 않는다(접촉·색 표시는 그대로).\n" +
             "2026-08-17 사용자 지시: \"새끼손가락 파지 필수 아닌 걸로 빼\" — 호흡 3회가 통과되지 않았다.\n" +
             "★이유: 호흡 게이트가 `BothGripped`(= 배열의 <b>모든</b> 파지점)이라, 트래킹이 제일 불안한 " +
             "새끼손가락이 한 번 튈 때마다 호흡 누적이 0으로 초기화된다.\n" +
             "★배선을 빼는 것과 다르다 — 빼면 접촉 자체를 안 보지만, 이건 보되 <b>통과를 막지 않는다</b>.")]
    [SerializeField] private bool required = true;

    /// <summary>이 파지점이 성립에 필수인가. false면 파지 판정에서 건너뛴다.</summary>
    public bool IsRequired => required;

    [SerializeField] private Renderer targetRenderer;
    // 흰색(1,1,1,0.3)은 환자 피부·배경에 묻혀 VR에서 잘 안 보였다(사용자 피드백) → 연한 붉은색.
    // 미파지=붉은색 / 파지 성립=초록(grippedColor)으로 신호가 갈린다.
    // ※씬에 이미 배치된 파지점은 이 값이 직렬화돼 있어 기본값이 안 먹는다 →
    //   메뉴 `GuideChuna/파지점 색상 일괄 적용`으로 한 번 적용할 것.
    [SerializeField] private Color idleColor = new Color(1f, 0.35f, 0.35f, 0.5f);
    [SerializeField] private Color grippedColor = Color.green;
    [Tooltip("손이 닿아 파지가 성립했을 때의 알파. 낮출수록 반투명해져 손·환자를 가리지 않는다.\n" +
             "0이면 완전히 투명(구체가 사라진 것처럼 보임). 구체를 끄지 않고 흐려지게만 한다.")]
    [Range(0f, 1f)]
    [SerializeField] private float grippedAlpha = 0.25f;

    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip clickSound;

    [Tooltip("파지 성립 시 구체를 이 배율로 키운다(1 = 크기 변화 없음). " +
             "VR에서는 작은 구체의 색 변화만으로는 닿았는지 알아보기 어려워 크기로도 알린다.")]
    [SerializeField] private float grippedScale = 1.35f;
    [Tooltip("크기 변화 속도(초당 보간율). 클수록 즉각적.")]
    [SerializeField] private float scaleLerpSpeed = 14f;

    [Tooltip("켜면 접촉 상태가 바뀔 때마다 콘솔에 남긴다(어느 파지점이 안 닿는지 진단용).")]
    [SerializeField] private bool debugLog = false;

    private Vector3 baseScale;          // targetRenderer의 원래 로컬 스케일
    private bool baseScaleCaptured = false;

    private bool fingerInside = false;
    private bool wasGripped = false;
    private bool evaluating = true;   // false면 판정/사운드/색갱신 정지(호흡 국면). 손 튐에 의한 깜빡임 방지.

    private bool hasPressureColor = false;   // 압력 단계: 깊이 색이 파지/idle 색을 덮어씀
    private Color pressureColor;

    private bool transparencyEnsured;

    /// <summary>
    /// 구체 머티리얼을 반투명(Standard - Fade)으로 만든다.
    /// ★불투명 머티리얼이면 idleColor·grippedAlpha의 알파가 통째로 무시돼
    /// "빨간 덩어리"로만 보인다. 씬 머티리얼을 건드리지 않도록 인스턴스에만 적용한다.
    /// </summary>
    /// <summary>구체를 <b>처음부터 반투명인 셰이더</b>로 갈아끼운다.
    ///
    /// ★2026-08-18 전면 교체 — 예전에는 씬의 불투명 머티리얼을 받아 Standard의 Fade 설정
    ///   (_Mode·_SrcBlend·_DstBlend·_ZWrite·키워드 3개·renderQueue = <b>7가지</b>)을 런타임에 맞췄다.
    ///   하나라도 어긋나거나 알파블렌드 배리언트가 빌드에서 스트립되면 <b>조용히 불투명</b>이 됐고,
    ///   실패해도 예외도 로그도 없었다. "파지점이 진한 덩어리로 보인다"가 반복된 원인이다.
    ///
    ///   Sprites/Default는 알파 블렌드가 셰이더에 고정(Blend SrcAlpha OneMinusSrcAlpha, ZWrite Off)이라
    ///   <b>맞출 상태가 없다.</b> _Color 하나로 색과 알파가 정해지므로 아래 색 갱신 코드도 그대로 쓴다.
    ///   UI가 늘 쓰는 셰이더라 빌드에서 빠질 일도 없다(원래 Standard를 고른 이유였던 스트립 위험도 해소).
    ///   파지 구체는 위치·성립 여부만 알리면 되므로 조명이 필요 없다.</summary>
    private void EnsureTransparentMaterial()
    {
        if (transparencyEnsured || targetRenderer == null) return;
        transparencyEnsured = true;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            Debug.LogWarning($"[GripPointTarget] {name}: Sprites/Default를 찾지 못해 반투명 전환에 실패했습니다. " +
                             "구체가 불투명하게 보입니다(판정에는 영향 없음).");
            return;
        }

        Color keep = targetRenderer.sharedMaterial != null && targetRenderer.sharedMaterial.HasProperty("_Color")
                     ? targetRenderer.sharedMaterial.color : idleColor;

        var m = new Material(shader) { name = "GripPointMat" };
        m.color = keep;
        m.renderQueue = 3000;      // 불투명 뒤에 그린다
        targetRenderer.material = m;
    }

    /// <summary>씬에 직렬화된 렌더러 표시 여부. 평가모드 숨김을 풀 때 여기로 되돌린다
    /// (도구 `GuideChuna/파지점 표시 정리`로 이미 꺼 둔 파지점을 되살리지 않기 위함).</summary>
    private bool rendererDefaultEnabled = true;
    private bool rendererDefaultCaptured;

    private void Awake()
    {
        EnsureTransparentMaterial();
        CaptureRendererDefault();
    }

    private void CaptureRendererDefault()
    {
        if (rendererDefaultCaptured || targetRenderer == null) return;
        rendererDefaultCaptured = true;
        rendererDefaultEnabled = targetRenderer.enabled;
    }

    /// <summary>구체 <b>표시만</b> 켜고 끈다. 콜라이더·트리거는 그대로라 판정은 계속 돈다.
    ///
    /// ★오브젝트를 끄면(SetActive) 판정이 조용히 죽는다 — 파지점은 cranialGrip·cranialPressure의
    /// 접촉 소스다(08-13 회의 결론). 그래서 평가모드 숨김은 반드시 렌더러 단위로 한다.
    /// ★끄기는 '원래 켜져 있던 것'에만 적용된다. 도구로 이미 꺼 둔 파지점은 켜지지 않는다.</summary>
    public void SetRendererVisible(bool on)
    {
        CaptureRendererDefault();
        if (targetRenderer == null) return;
        targetRenderer.enabled = on && rendererDefaultEnabled;
    }

    /// <summary>파지 판정 활성/정지. 호흡 국면처럼 손이 안 보여 트리거가 튀는 구간에서 정지시켜
    /// clickSound 반복·색 깜빡임을 막는다.</summary>
    public void SetEvaluating(bool on)
    {
        evaluating = on;
        if (!on)
        {
            hasPressureColor = false;   // 압력 색 잔상 제거(호흡 국면 진입)
            if (targetRenderer != null)
            {
                targetRenderer.material.color = idleColor;
                RestoreBaseScale();     // 커진 채로 굳지 않게
            }
        }
    }

    /// <summary>압력 단계: 깊이 색(회색→초록→노랑→빨강)을 이 파지 구체에 덮어씌운다.
    /// 컨트롤러가 압력 국면 매 프레임 DepthPressureGuide.CurrentColor로 주입한다.</summary>
    public void SetPressureColor(Color c)
    {
        hasPressureColor = true;
        pressureColor = c;
    }

    /// <summary>압력 색 오버라이드 해제 → 파지 여부 색(초록/idle)으로 복귀.</summary>
    public void ClearPressureColor() => hasPressureColor = false;

    /// <summary>포즈 인식 상태 (컨트롤러가 HandPoseComparator.passed로 주입)</summary>
    public bool PoseRecognized { get; set; }

    /// <summary>정답 손끝 콜라이더 런타임 주입 (Option B: 검지끝 본이 런타임 생성이라
    /// 인스펙터 연결 불가일 때 CranialAdjustmentController가 생성한 트리거 콜라이더를 연결).</summary>
    public void SetExpectedFingerCollider(Collider c)
    {
        if (expectedFingerCollider == c) return;
        expectedFingerCollider = c;
        // ★주입 전에 아무 콜라이더나 스쳐 들어온 접촉이 남아 있을 수 있다 → 진짜 손끝 기준으로 다시 계산한다.
        //   (주입 시점에 이미 손을 대고 있어도 인식되도록 겹침을 직접 확인한다.)
        fingerInside = OverlapsExpectedFinger();
        warnedUnsetCollider = false;
    }

    /// <summary>파지 성립 여부 = 트리거 진입 AND 포즈 인식</summary>
    public bool IsGripped => fingerInside && (bypassPoseCheck || PoseRecognized);

    /// <summary>★접촉만 본다(포즈 인식 무관). 가이드손을 '손을 대면 숨기는' 용도 —
    /// 손 모양이 아직 안 맞아도 시야를 가리지 않게 하려면 파지 성립이 아니라 접촉이 기준이어야 한다.</summary>
    public bool IsTouched => fingerInside;

    /// <summary>파지가 성립되는 순간 1회 발생</summary>
    public System.Action OnGripped;

    void OnTriggerEnter(Collider other)
    {
        if (Matches(other)) fingerInside = true;
    }

    void OnTriggerExit(Collider other)
    {
        if (Matches(other)) fingerInside = false;
    }

    /// <summary>★비활성 상태에서 접촉이 '참'으로 굳는 것을 막는다(08-11).
    /// 오브젝트를 끄면 OnTriggerExit가 안 오는 경우가 있어, 진단 단계에서 잠깐 켜졌던 파지점이
    /// <b>손을 대지도 않았는데 성립 상태로 남아</b> 다음 파지 단계가 한 손만으로 통과됐다.</summary>
    void OnDisable()
    {
        fingerInside = false;
        wasGripped = false;
    }

    /// <summary>★다시 켜질 때는 '지금 손이 안에 들어와 있는지'를 직접 확인한다.
    /// 끄면서 지웠는데 손이 이미 안에 있으면 OnTriggerEnter가 재발생하지 않아
    /// 손을 뺐다 넣기 전까지 영영 인식되지 않기 때문(기존 주석에 남아 있던 함정).</summary>
    void OnEnable()
    {
        fingerInside = OverlapsExpectedFinger();
    }

    /// <summary>지금 정답 손끝 콜라이더와 겹쳐 있는가(트리거 이벤트 없이 즉시 판정).</summary>
    private bool OverlapsExpectedFinger()
    {
        if (expectedFingerCollider == null || !expectedFingerCollider.enabled) return false;
        var mine = GetComponent<Collider>();
        if (mine == null || !mine.enabled) return false;
        // 정밀 판정(ComputePenetration)은 비용이 크고 회전 콜라이더 조합에서 실패하기도 한다 →
        // 켜지는 순간 1회뿐이라 경계상자 교차로 충분하다.
        return mine.bounds.Intersects(expectedFingerCollider.bounds);
    }

    void Update()
    {
        if (!evaluating) return;   // 호흡 국면: 정지(튀는 트리거로 사운드/색 깜빡임 방지)

        bool gripped = IsGripped;
        if (gripped != wasGripped)
        {
            if (gripped)
            {
                if (audioSource != null && clickSound != null) audioSource.PlayOneShot(clickSound);
                OnGripped?.Invoke();
                ChunaLogger.Log($"<color=green>[GripPointTarget] 파지 성립: {gameObject.name}</color>");
            }
            else if (debugLog)
            {
                ChunaLogger.Log($"[GripPointTarget] 파지 해제: {gameObject.name}");
            }
        }
        wasGripped = gripped;

        if (targetRenderer != null)
        {
            // 파지 성립 시에는 구체를 끄지 않고 알파만 낮춘다 — 위치는 계속 보여야 하고,
            // 손을 댄 뒤 불투명한 구체가 손을 가리는 것을 막는다(사용자 요구).
            Color grippedShown = new Color(grippedColor.r, grippedColor.g, grippedColor.b,
                                           grippedColor.a * grippedAlpha);

            targetRenderer.material.color =
                hasPressureColor ? pressureColor            // 압력색(그라데이션) - 컨트롤러가 눌림 손가락에만 주입
                : (gripped ? grippedShown : idleColor);     // 미주입 = 안 닿음/비접촉 → idle(연한 붉은색)

            // 색만으로는 VR에서 판별이 어려워 크기로도 알린다(원래 크기 기준 배율).
            ApplyGripScale(gripped);
        }
    }

    /// <summary>파지 여부에 따라 구체를 원래 크기 ↔ grippedScale 배로 부드럽게 오간다.</summary>
    private void ApplyGripScale(bool gripped)
    {
        if (grippedScale <= 0f || Mathf.Approximately(grippedScale, 1f)) return;

        Transform t = targetRenderer.transform;
        if (!baseScaleCaptured)
        {
            baseScale = t.localScale;
            baseScaleCaptured = true;
        }

        Vector3 want = gripped ? baseScale * grippedScale : baseScale;
        t.localScale = Vector3.Lerp(t.localScale, want, 1f - Mathf.Exp(-scaleLerpSpeed * Time.deltaTime));
    }

    [Tooltip("★디버그용. 켜면 정답 손끝 콜라이더·태그가 둘 다 비었을 때 '아무 콜라이더나' 파지로 인정한다.\n" +
             "기본 OFF — 예전 기본값(허용)이 08-11 증상의 원인이었다: 손끝 주입이 끝나기 전이나 배선이 빠진 파지점이 " +
             "환자 머리·반대 손 콜라이더에 스쳐도 성립해서, 왼손만 후두골에 대도 오른손 측두 파지가 이미 잡힌 것으로 처리됐다.\n" +
             "★신규 필드라 이미 배치된 씬 파지점 전부에 코드 기본값(끔)이 먹는다.")]
    [SerializeField] private bool acceptAnyColliderWhenUnset = false;

    private bool warnedUnsetCollider;

    private bool Matches(Collider other)
    {
        if (expectedFingerCollider != null) return other == expectedFingerCollider;
        if (!string.IsNullOrEmpty(expectedFingerTag)) return other.CompareTag(expectedFingerTag);

        if (acceptAnyColliderWhenUnset) return true;

        // 손끝 주입(TryInjectFingertips)이 아직 안 끝난 순간일 수 있다 → 무시하고 다음 진입을 기다린다.
        // 주입이 끝나면 SetExpectedFingerCollider가 접촉을 다시 계산해 준다.
        if (!warnedUnsetCollider)
        {
            warnedUnsetCollider = true;
            ChunaLogger.LogWarning($"[GripPointTarget] '{gameObject.name}': 정답 손끝 콜라이더가 아직 없어 접촉을 인정하지 않았습니다. " +
                                   "손끝 주입 전이면 곧 해결되고, 계속 뜨면 리그의 HandVisual 배선을 확인하세요.");
        }
        return false;
    }

    public void ResetState()
    {
        fingerInside = false;
        wasGripped = false;
        PoseRecognized = false;
        hasPressureColor = false;
        RestoreBaseScale();
    }

    /// <summary>진단용 한 줄 상태(왜 색이 안 변하는지 판별). 컨트롤러의 덤프가 호출한다.</summary>
    public string DescribeState()
    {
        string rend;
        if (targetRenderer == null)
        {
            rend = "targetRenderer=없음(색 변화 불가!)";
        }
        else
        {
            var m = targetRenderer.sharedMaterial;
            rend = $"renderer={targetRenderer.name} shader={(m != null ? m.shader.name : "머티리얼없음")} " +
                   $"현재색={(Application.isPlaying ? targetRenderer.material.color.ToString() : "(Play중 아님)")} " +
                   $"enabled={targetRenderer.enabled}";
        }

        string expect = expectedFingerCollider != null
            ? $"기대콜라이더={expectedFingerCollider.name}"
            : (string.IsNullOrEmpty(expectedFingerTag) ? "기대콜라이더=미지정(아무거나 허용)" : $"태그={expectedFingerTag}");

        return $"{gameObject.name} [{finger}] active={gameObject.activeInHierarchy} evaluating={evaluating} " +
               $"접촉={fingerInside} 포즈통과={(bypassPoseCheck ? "bypass" : PoseRecognized.ToString())} " +
               $"→ IsGripped={IsGripped} | 압력색덮어씀={hasPressureColor} | {expect} | {rend}";
    }

    /// <summary>구체 크기를 원래대로 되돌린다(파지 스케일 잔상 제거).</summary>
    private void RestoreBaseScale()
    {
        if (!baseScaleCaptured || targetRenderer == null) return;
        targetRenderer.transform.localScale = baseScale;
    }
}
