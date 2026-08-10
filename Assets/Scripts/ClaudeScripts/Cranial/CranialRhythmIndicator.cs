using UnityEngine;

/// <summary>
/// 두개골 1차 호흡 리듬 프록시 인디케이터 (절차적, 기능: 진단/재평가 시각화).
///
/// 환자 머리 모델은 측두골/후두골이 개별 본으로 분리돼 있지 않으므로(통짜 메시),
/// 좌·우 OM(후두-유양돌기) 지점에 둔 작은 지표 오브젝트를 스크립트로 진동시켜
/// 두개골 리듬과 좌우 비대칭/대칭을 시각화한다.
///   - 비대칭(Asymmetric, 진단): 좌측 정상 진폭, 우측 제한(restrictedRatio) → 우측 OM 기능장애.
///   - 대칭(Symmetric, 재평가): 양측 동일 진폭 → 교정 완료.
///
/// 모드 전환은 CranialAdjustmentController가 BreathingComplete로 매 프레임 구동한다
/// (호흡 교정 완료 전=비대칭, 완료 후=대칭). CSV/조건 추가 없이 술기 흐름에 자연 연동.
///
/// 배치: 컴포넌트는 CranialRig(rig 토글 단위)에 두고, leftIndicator/rightIndicator는
/// skullTarget(머리) 자식으로 둬 머리를 따라 움직이게 한다. 지표 오브젝트는 씬 시작 시
/// 비활성으로 두면 비두경부 시나리오에서 노출되지 않는다(OnEnable/OnDisable이 토글).
/// </summary>
public class CranialRhythmIndicator : MonoBehaviour
{
    public enum Mode { Asymmetric, Symmetric }

    [Header("=== 사용 여부 ===")]
    // CRI(두개천골 리듬)는 별도 기능으로 분리하기로 해 시나리오에서 뺐다(2026-08-10, 회의 08-05 결정).
    // 신규 필드라 씬에 직렬화된 값이 없어 이 기본값(false)이 기존 인스턴스 5개에 그대로 적용된다.
    // CRI를 단독 기능으로 다시 붙일 때 켜면 아래 로직이 그대로 살아난다.
    [Tooltip("CRI 리듬 시각화 사용. 분리 작업 중이라 기본 꺼짐 — 켜면 좌/우 지표가 진동한다.")]
    [SerializeField] private bool rhythmEnabled = false;

    [Header("=== 지표 오브젝트 (좌/우 OM, skullTarget 자식 권장) ===")]
    [Tooltip("좌측 OM 지표 (정상측 - 항상 정상 진폭)")]
    [SerializeField] private Transform leftIndicator;
    [Tooltip("우측 OM 지표 (제한측 - 비대칭 모드에서 진폭 축소)")]
    [SerializeField] private Transform rightIndicator;

    [Header("=== 리듬 ===")]
    [Tooltip("분당 두개골 리듬 횟수 (정상 6~12)")]
    [SerializeField] private float cyclesPerMinute = 9f;
    [Tooltip("굴곡 변위 축 (지표의 로컬 좌표 기준, 보통 위/바깥)")]
    [SerializeField] private Vector3 flexAxis = Vector3.up;
    [Tooltip("정상측 최대 변위 (m)")]
    [SerializeField] private float amplitude = 0.004f;
    [Tooltip("제한측 진폭 비율 (0=완전 제한, 1=정상)")]
    [SerializeField, Range(0f, 1f)] private float restrictedRatio = 0.2f;

    [Header("=== 색 피드백 (선택) ===")]
    [SerializeField] private Renderer leftRenderer;
    [SerializeField] private Renderer rightRenderer;
    [SerializeField] private Color normalColor = Color.green;
    [SerializeField] private Color restrictedColor = new Color(1f, 0.5f, 0f); // 주황

    private Mode mode = Mode.Asymmetric;
    private Vector3 leftBase, rightBase;
    private bool baseCaptured = false;
    private float phase = 0f;

    public Mode CurrentMode => mode;

    /// <summary>컨트롤러가 매 프레임 호출 (비대칭/대칭 전환)</summary>
    public void SetMode(Mode m) => mode = m;

    void Awake() => CaptureBase();

    private void CaptureBase()
    {
        if (baseCaptured) return;
        // Transform 읽기는 비활성 오브젝트에서도 동작하므로 지표가 inactive여도 안전
        if (leftIndicator != null) leftBase = leftIndicator.localPosition;
        if (rightIndicator != null) rightBase = rightIndicator.localPosition;
        baseCaptured = true;
    }

    void OnEnable()
    {
        CaptureBase();
        mode = Mode.Asymmetric;  // rig 활성(시나리오 시작) 시 기본 = 진단(비대칭)
        phase = 0f;
        SetIndicatorsActive(rhythmEnabled);
    }

    void OnDisable()
    {
        ResetToBase();
        SetIndicatorsActive(false);
    }

    void Update()
    {
        if (!rhythmEnabled) return;

        phase += Time.deltaTime * (cyclesPerMinute / 60f) * 2f * Mathf.PI;
        float wave = (Mathf.Sin(phase) + 1f) * 0.5f;  // 0..1 굴곡 곡선
        float rightRatio = mode == Mode.Symmetric ? 1f : restrictedRatio;
        Vector3 axis = flexAxis.sqrMagnitude > 1e-6f ? flexAxis.normalized : Vector3.up;

        if (leftIndicator != null)
            leftIndicator.localPosition = leftBase + axis * (wave * amplitude);
        if (rightIndicator != null)
            rightIndicator.localPosition = rightBase + axis * (wave * amplitude * rightRatio);

        if (leftRenderer != null) leftRenderer.material.color = normalColor;
        if (rightRenderer != null)
            rightRenderer.material.color = mode == Mode.Symmetric ? normalColor : restrictedColor;
    }

    private void SetIndicatorsActive(bool on)
    {
        if (leftIndicator != null) leftIndicator.gameObject.SetActive(on);
        if (rightIndicator != null) rightIndicator.gameObject.SetActive(on);
    }

    private void ResetToBase()
    {
        if (leftIndicator != null) leftIndicator.localPosition = leftBase;
        if (rightIndicator != null) rightIndicator.localPosition = rightBase;
    }
}
