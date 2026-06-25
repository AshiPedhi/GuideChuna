using UnityEngine;

/// <summary>
/// 깊이 연산 기반 압력/방향 판정 (기능2)
/// - 영점/정답벡터를 모두 두개골(skullTarget) 로컬 프레임으로 통일 → 머리가 흔들리거나 위로 들려도 깊이 오판 방지.
/// - 깊이 = (현재-영점) 이동벡터를 정답방향에 내적 투영(순수 유효 깊이).
/// - 손떨림 보정: Deadzone(raw) + Mathf.Lerp(시각).
/// 한 손당 1개 인스턴스(왼손=굴곡, 오른손=하방/내방). 두 인스턴스를 CranialAdjustmentController가 보유.
/// </summary>
public class DepthPressureGuide : MonoBehaviour
{
    public enum TensionState { Idle, Good, Over, WrongDirection }

    [Header("=== 좌표 기준 (씬에서 연결) ===")]
    [Tooltip("두개골 타겟 - 영점/정답벡터의 로컬 프레임 기준")]
    [SerializeField] private Transform skullTarget;
    [Tooltip("손끝 Transform (예: HandIndex3). 컨트롤러가 런타임에 주입해도 됨")]
    [SerializeField] private Transform fingertip;
    [Tooltip("두개골 내부에 배치한 정답 방향 기준 오브젝트. 이 오브젝트의 forward = 정답 방향")]
    [SerializeField] private Transform directionObject;

    [Header("=== 깊이 임계값 (m, 인스펙터 튜닝) ===")]
    [Tooltip("적정 텐션 최소 깊이 (기본 0.5cm)")]
    [SerializeField] private float goodMin = 0.005f;
    [Tooltip("적정 텐션 최대 깊이 (기본 1.5cm)")]
    [SerializeField] private float goodMax = 0.015f;
    [Tooltip("과도 압력 임계 (기본 2.0cm)")]
    [SerializeField] private float overThreshold = 0.020f;
    [Tooltip("정답 방향 허용 각도 (초과 시 방향 오류)")]
    [SerializeField] private float maxAngle = 45f;

    [Header("=== 손떨림 보정 ===")]
    [Tooltip("시각 보간 속도")]
    [SerializeField] private float lerpSpeed = 10f;
    [Tooltip("데드존: raw 깊이 변화량이 이 값 이하면 갱신 무시 (기본 1mm)")]
    [SerializeField] private float deadzone = 0.001f;

    [Header("=== 시각/사운드 (선택) ===")]
    [SerializeField] private Transform arrowVisual;
    [SerializeField] private Renderer arrowRenderer;
    [SerializeField] private Color idleColor = new Color(0.6f, 0.6f, 0.6f, 0.5f);
    [SerializeField] private Color goodColor = Color.green;
    [SerializeField] private Color errorColor = Color.red;
    [Tooltip("방향 오류 시 켜는 화면 테두리 경고")]
    [SerializeField] private GameObject screenEdgeWarning;
    [SerializeField] private float shakeMagnitude = 0.003f;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip goodSound;
    [SerializeField] private AudioClip errorSound;

    // 상태
    private bool hasZero = false;
    private Vector3 zeroLocal;
    private float rawDepth;        // deadzone 통과한 직전 채택값
    private float smoothedDepth;   // Lerp 결과 (시각/판정에 사용)
    private float currentAngle;
    private TensionState state = TensionState.Idle;
    private TensionState lastState = TensionState.Idle;
    private Vector3 arrowBaseLocalPos;

    // === Public API (조건/컨트롤러용) ===
    public bool HasZeroPoint => hasZero;
    public TensionState State => state;
    public bool IsInGoodZone => state == TensionState.Good;
    public float Depth => smoothedDepth;

    public void SetFingertip(Transform t) => fingertip = t;
    public void SetSkullTarget(Transform t) => skullTarget = t;

    void Awake()
    {
        if (arrowVisual != null) arrowBaseLocalPos = arrowVisual.localPosition;
    }

    /// <summary>
    /// 파지 완료 순간 호출 - 현재 손끝을 깊이 0의 영점으로 두개골 로컬 좌표에 저장.
    /// </summary>
    public void SaveZeroPoint()
    {
        if (skullTarget == null || fingertip == null)
        {
            ChunaLogger.LogWarning("[DepthPressureGuide] skullTarget 또는 fingertip 미설정 - 영점 저장 실패");
            return;
        }

        zeroLocal = skullTarget.InverseTransformPoint(fingertip.position);
        rawDepth = 0f;
        smoothedDepth = 0f;
        hasZero = true;
        state = TensionState.Idle;
        lastState = TensionState.Idle;
        ChunaLogger.Log($"<color=cyan>[DepthPressureGuide] 영점 저장(로컬): {zeroLocal}</color>");
    }

    public void ClearZeroPoint()
    {
        hasZero = false;
        state = TensionState.Idle;
        if (screenEdgeWarning != null) screenEdgeWarning.SetActive(false);
    }

    void Update()
    {
        if (!hasZero || skullTarget == null || fingertip == null || directionObject == null) return;

        // 모든 연산을 두개골 로컬 프레임에서 수행 (머리 움직임 자동 상쇄)
        Vector3 curLocal = skullTarget.InverseTransformPoint(fingertip.position);
        Vector3 moveLocal = curLocal - zeroLocal;
        Vector3 dirLocal = skullTarget.InverseTransformDirection(directionObject.forward).normalized;

        float projected = Vector3.Dot(moveLocal, dirLocal);                  // 순수 유효 깊이
        currentAngle = moveLocal.sqrMagnitude > 1e-8f
            ? Vector3.Angle(moveLocal, dirLocal)
            : 0f;

        // Deadzone: raw 변화량이 1mm 이하이면 갱신 무시 (Lerp 이전 raw에 적용)
        if (Mathf.Abs(projected - rawDepth) >= deadzone)
            rawDepth = projected;

        // Lerp 보간 (시각 흔들림 제거)
        smoothedDepth = Mathf.Lerp(smoothedDepth, rawDepth, Time.deltaTime * lerpSpeed);

        UpdateState();
        UpdateVisual();
    }

    private void UpdateState()
    {
        // 방향 오류: 뒤로 당김(음수) 또는 각도 크게 벌어짐
        if (smoothedDepth < 0f || currentAngle > maxAngle)
            state = TensionState.WrongDirection;
        else if (smoothedDepth >= overThreshold)
            state = TensionState.Over;
        else if (smoothedDepth >= goodMin && smoothedDepth <= goodMax)
            state = TensionState.Good;
        else
            state = TensionState.Idle; // 영점~goodMin(접근중) 또는 goodMax~over(경계) = 대기

        if (state != lastState)
        {
            OnStateEntered(state);
            lastState = state;
        }
    }

    private void OnStateEntered(TensionState s)
    {
        switch (s)
        {
            case TensionState.Good:
                PlaySound(goodSound);
                break;
            case TensionState.Over:
            case TensionState.WrongDirection:
                PlaySound(errorSound);
                break;
        }

        if (screenEdgeWarning != null)
            screenEdgeWarning.SetActive(s == TensionState.WrongDirection);
    }

    private void UpdateVisual()
    {
        if (arrowRenderer != null)
        {
            Color c = state == TensionState.Good ? goodColor
                    : (state == TensionState.Over || state == TensionState.WrongDirection) ? errorColor
                    : idleColor;
            arrowRenderer.material.color = c;
        }

        // 과도 압력 시 Shake
        if (arrowVisual != null)
        {
            if (state == TensionState.Over)
            {
                Vector3 jitter = new Vector3(
                    (Mathf.PerlinNoise(Time.time * 40f, 0f) - 0.5f),
                    (Mathf.PerlinNoise(0f, Time.time * 40f) - 0.5f),
                    0f) * (shakeMagnitude * 2f);
                arrowVisual.localPosition = arrowBaseLocalPos + jitter;
            }
            else
            {
                arrowVisual.localPosition = arrowBaseLocalPos;
            }
        }
    }

    private void PlaySound(AudioClip clip)
    {
        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip);
    }
}
