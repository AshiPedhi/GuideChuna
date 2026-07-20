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
    [SerializeField] private Renderer targetRenderer;
    [SerializeField] private Color idleColor = new Color(1f, 1f, 1f, 0.3f);
    [SerializeField] private Color grippedColor = Color.green;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip clickSound;

    private bool fingerInside = false;
    private bool wasGripped = false;
    private bool evaluating = true;   // false면 판정/사운드/색갱신 정지(호흡 국면). 손 튐에 의한 깜빡임 방지.

    private bool hasPressureColor = false;   // 압력 단계: 깊이 색이 파지/idle 색을 덮어씀
    private Color pressureColor;

    /// <summary>파지 판정 활성/정지. 호흡 국면처럼 손이 안 보여 트리거가 튀는 구간에서 정지시켜
    /// clickSound 반복·색 깜빡임을 막는다.</summary>
    public void SetEvaluating(bool on)
    {
        evaluating = on;
        if (!on)
        {
            hasPressureColor = false;   // 압력 색 잔상 제거(호흡 국면 진입)
            if (targetRenderer != null) targetRenderer.material.color = idleColor;
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
    public void SetExpectedFingerCollider(Collider c) => expectedFingerCollider = c;

    /// <summary>파지 성립 여부 = 트리거 진입 AND 포즈 인식</summary>
    public bool IsGripped => fingerInside && (bypassPoseCheck || PoseRecognized);

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

    void Update()
    {
        if (!evaluating) return;   // 호흡 국면: 정지(튀는 트리거로 사운드/색 깜빡임 방지)

        bool gripped = IsGripped;
        if (gripped && !wasGripped)
        {
            if (audioSource != null && clickSound != null) audioSource.PlayOneShot(clickSound);
            OnGripped?.Invoke();
            ChunaLogger.Log($"<color=green>[GripPointTarget] 파지 성립: {gameObject.name}</color>");
        }
        wasGripped = gripped;

        if (targetRenderer != null)
            targetRenderer.material.color =
                hasPressureColor ? pressureColor            // 압력색(그라데이션) - 컨트롤러가 눌림 손가락에만 주입
                : (gripped ? grippedColor : idleColor);     // 미주입 = 안 닿음/비접촉 → 흰색(idle)
    }

    private bool Matches(Collider other)
    {
        if (expectedFingerCollider != null) return other == expectedFingerCollider;
        if (!string.IsNullOrEmpty(expectedFingerTag)) return other.CompareTag(expectedFingerTag);
        // 둘 다 미설정이면 모든 콜라이더 허용 (디버그)
        return true;
    }

    public void ResetState()
    {
        fingerInside = false;
        wasGripped = false;
        PoseRecognized = false;
        hasPressureColor = false;
    }
}
