using UnityEngine;

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

    /// <summary>포즈 인식 상태 (컨트롤러가 HandPoseComparator.passed로 주입)</summary>
    public bool PoseRecognized { get; set; }

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
        bool gripped = IsGripped;
        if (gripped && !wasGripped)
        {
            if (audioSource != null && clickSound != null) audioSource.PlayOneShot(clickSound);
            OnGripped?.Invoke();
            ChunaLogger.Log($"<color=green>[GripPointTarget] 파지 성립: {gameObject.name}</color>");
        }
        wasGripped = gripped;

        if (targetRenderer != null)
            targetRenderer.material.color = gripped ? grippedColor : idleColor;
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
    }
}
