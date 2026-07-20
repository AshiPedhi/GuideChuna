using UnityEngine;

/// <summary>
/// 깊이 연산 기반 압력/방향 판정 (기능2) — 손가락별 다지점.
/// - 영점/정답벡터를 모두 두개골(skullTarget) 로컬 프레임으로 통일 → 머리가 흔들리거나 위로 들려도 깊이 오판 방지.
/// - 깊이 = (현재-영점) 이동벡터를 정답방향에 내적 투영(순수 유효 깊이).
/// - 손떨림 보정: Deadzone(raw) + Mathf.Lerp(시각).
///
/// ★손가락별 표시 + 손 단위 판정:
///   각 손가락 끝을 개별 추적해 손가락마다 영점·깊이·색(<see cref="ColorForFinger"/>)을 산출한다(→ 파지 구체별 색).
///   방향(directionObject.forward)과 임계값은 5손가락이 공유(파이브핑거홀드 = 한 방향으로 함께 누름).
///   완료 판정(<see cref="IsInGoodZone"/>)은 **대표 손가락(gateIndex, 기본 검지) 한 슬롯**만 읽어
///   손 단위로 유지한다(기존 게이트 동작·튜닝 보존, 임상 반론 회피).
/// 한 손당 1개 인스턴스(오른손=측두). 컨트롤러가 손가락 팁 배열을 주입한다.
/// </summary>
public class DepthPressureGuide : MonoBehaviour
{
    public enum TensionState { Idle, Good, Over, WrongDirection }

    [Header("=== 좌표 기준 (씬에서 연결) ===")]
    [Tooltip("두개골 타겟 - 영점/정답벡터의 로컬 프레임 기준")]
    [SerializeField] private Transform skullTarget;
    [Tooltip("손끝 Transform (단일). 컨트롤러가 손가락 팁 배열을 주입하면 무시됨. 인스펙터 단독 테스트용 폴백.")]
    [SerializeField] private Transform fingertip;
    [Tooltip("두개골 내부에 배치한 정답 방향 기준 오브젝트. 이 오브젝트의 forward = 정답 방향(5손가락 공유)")]
    [SerializeField] private Transform directionObject;

    [Header("=== 깊이 임계값 (m, 인스펙터 튜닝) ===")]
    [Tooltip("적정 텐션 최소 깊이 (기본 0.3cm)")]
    [SerializeField] private float goodMin = 0.003f;
    [Tooltip("적정 텐션 최대 깊이 (기본 3.0cm)")]
    [SerializeField] private float goodMax = 0.030f;
    [Tooltip("과도 압력 임계 (기본 4.5cm)")]
    [SerializeField] private float overThreshold = 0.045f;
    [Tooltip("정답 방향 허용 각도 (초과 시 방향 오류)")]
    [SerializeField] private float maxAngle = 55f;

    [Header("=== 손떨림 보정 ===")]
    [Tooltip("시각 보간 속도")]
    [SerializeField] private float lerpSpeed = 10f;
    [Tooltip("데드존: raw 깊이 변화량이 이 값 이하면 갱신 무시 (기본 1mm)")]
    [SerializeField] private float deadzone = 0.001f;
    [Tooltip("접촉 해제 깊이: 손가락이 영점보다 이 값 이상 뒤로 물러나면 '비접촉'으로 보고 압력색을 끈다(흰색). 기본 0.8cm")]
    [SerializeField] private float disengageDepth = 0.008f;

    [Header("=== 디버그 ===")]
    [Tooltip("켜면 손가락별 깊이(cm)/상태를 콘솔에 주기적으로 출력 - 배선/방향 진단용")]
    [SerializeField] private bool debugLog = false;

    [Header("=== 압력 색 (파지 구체에 주입) ===")]
    [Tooltip("영점~적정 진입 전(접근 중) 색")]
    [SerializeField] private Color idleColor = new Color(0.6f, 0.6f, 0.6f, 0.5f);
    [Tooltip("적정 텐션 색")]
    [SerializeField] private Color goodColor = Color.green;
    [Tooltip("적정~과도 사이 경계 그라데이션 색 (초록→이 색→빨강)")]
    [SerializeField] private Color warnColor = Color.yellow;
    [Tooltip("과도/방향오류 색")]
    [SerializeField] private Color errorColor = Color.red;

    [Header("=== 방향 경고/사운드 (선택) ===")]
    [Tooltip("방향 오류 시 켜는 화면 테두리 경고(선택). 비워도 색으로 표시됨. 대표 손가락 기준.")]
    [SerializeField] private GameObject screenEdgeWarning;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip goodSound;
    [SerializeField] private AudioClip errorSound;

    // === 손가락별 상태 (병렬 배열) ===
    private Transform[] tips;          // 추적 중인 손가락 끝들
    private CranialFinger[] fingers;   // 각 슬롯의 손가락 (tips와 병렬)
    private int gateIndex = 0;         // 손 단위 판정을 대표하는 슬롯 (기본 검지)
    private Vector3[] zeroLocal;
    private float[] rawDepth;          // deadzone 통과한 직전 채택값
    private float[] smoothedDepth;     // Lerp 결과 (시각/판정에 사용)
    private float[] fingerAngle;

    private TensionState[] state;
    private TensionState gateLastState = TensionState.Idle;   // 사운드/경고 트랜지션 감지(대표 슬롯만)

    private bool hasZero = false;
    private bool evaluating = true;    // false면 판정 정지(호흡 국면). 사운드/경고/색 튐 방지.

    // === Public API (조건/컨트롤러용) — 손 단위(대표 슬롯) ===
    public bool HasZeroPoint => hasZero;
    public TensionState State => GateValid ? state[gateIndex] : TensionState.Idle;
    public bool IsInGoodZone => State == TensionState.Good;
    public float Depth => GateValid ? smoothedDepth[gateIndex] : 0f;

    /// <summary>대표 손가락 색(손 단위). 단일 표시가 필요한 경우/폴백.</summary>
    public Color CurrentColor => GateValid ? ColorAt(gateIndex) : idleColor;
    /// <summary>지금 압력 색을 파지 구체에 표시해야 하는 국면인가(판정 중 + 영점 저장됨).</summary>
    public bool IsShowingPressure => evaluating && hasZero;

    private bool GateValid => state != null && gateIndex >= 0 && gateIndex < state.Length && tips != null && tips[gateIndex] != null;

    public void SetSkullTarget(Transform t) => skullTarget = t;

    /// <summary>단일 손끝 설정(폴백/좌측 거치 등). 검지 대표 1슬롯으로 초기화.</summary>
    public void SetFingertip(Transform t)
    {
        SetFingertips(new[] { CranialFinger.Index }, new[] { t }, 0);
    }

    /// <summary>손가락별 팁 배열 주입. fingers[i]가 tips[i]에 대응. gate = 손 단위 판정 대표 슬롯.</summary>
    public void SetFingertips(CranialFinger[] fingerIds, Transform[] fingerTips, int gate)
    {
        if (fingerIds == null || fingerTips == null || fingerIds.Length != fingerTips.Length)
        {
            ChunaLogger.LogWarning("[DepthPressureGuide] SetFingertips 인자 불일치 - 무시");
            return;
        }
        int n = fingerIds.Length;
        fingers = fingerIds;
        tips = fingerTips;
        gateIndex = (gate >= 0 && gate < n) ? gate : 0;
        zeroLocal = new Vector3[n];
        rawDepth = new float[n];
        smoothedDepth = new float[n];
        fingerAngle = new float[n];
        state = new TensionState[n];
        for (int i = 0; i < n; i++) state[i] = TensionState.Idle;
        gateLastState = TensionState.Idle;
    }

    /// <summary>인스펙터 단일 fingertip만 연결된 경우 지연 초기화(런타임 주입 없을 때).</summary>
    private void EnsureTipsInitialized()
    {
        if ((tips == null || tips.Length == 0) && fingertip != null)
            SetFingertip(fingertip);
    }

    /// <summary>깊이 판정 활성/정지. 호흡 국면처럼 손이 FOV를 벗어나 팁이 튀는 구간에서 정지시켜
    /// 상태변화에 딸린 사운드·방향경고 튐을 막는다. 영점(zeroLocal)은 유지.</summary>
    public void SetEvaluating(bool on)
    {
        evaluating = on;
        if (!on && screenEdgeWarning != null) screenEdgeWarning.SetActive(false);
    }

    /// <summary>파지 완료 순간 호출 - 모든 손가락 끝을 각자의 깊이 0 영점으로 두개골 로컬 좌표에 저장.</summary>
    public void SaveZeroPoint()
    {
        EnsureTipsInitialized();
        if (skullTarget == null || tips == null || tips.Length == 0)
        {
            ChunaLogger.LogWarning("[DepthPressureGuide] skullTarget 또는 손끝 미설정 - 영점 저장 실패");
            return;
        }

        for (int i = 0; i < tips.Length; i++)
        {
            if (tips[i] != null) zeroLocal[i] = skullTarget.InverseTransformPoint(tips[i].position);
            rawDepth[i] = 0f;
            smoothedDepth[i] = 0f;
            state[i] = TensionState.Idle;
        }
        hasZero = true;
        gateLastState = TensionState.Idle;
        ChunaLogger.Log($"<color=cyan>[DepthPressureGuide] 영점 저장(손가락 {tips.Length}점, 대표={fingers[gateIndex]})</color>");
    }

    public void ClearZeroPoint()
    {
        hasZero = false;
        gateLastState = TensionState.Idle;
        if (state != null) for (int i = 0; i < state.Length; i++) state[i] = TensionState.Idle;
        if (screenEdgeWarning != null) screenEdgeWarning.SetActive(false);
    }

    void Update()
    {
        if (!evaluating) return;   // 호흡 국면 등: 판정 정지(튀는 손끝으로 사운드/경고 유발 방지)
        if (!hasZero || skullTarget == null || directionObject == null || tips == null) return;

        Vector3 dirLocal = skullTarget.InverseTransformDirection(directionObject.forward).normalized;

        for (int i = 0; i < tips.Length; i++)
        {
            if (tips[i] == null) continue;

            // 모든 연산을 두개골 로컬 프레임에서 수행 (머리 움직임 자동 상쇄)
            Vector3 curLocal = skullTarget.InverseTransformPoint(tips[i].position);
            Vector3 moveLocal = curLocal - zeroLocal[i];

            float projected = Vector3.Dot(moveLocal, dirLocal);              // 순수 유효 깊이
            fingerAngle[i] = moveLocal.sqrMagnitude > 1e-8f
                ? Vector3.Angle(moveLocal, dirLocal)
                : 0f;

            // Deadzone: raw 변화량이 1mm 이하이면 갱신 무시 (Lerp 이전 raw에 적용)
            if (Mathf.Abs(projected - rawDepth[i]) >= deadzone)
                rawDepth[i] = projected;

            // Lerp 보간 (시각 흔들림 제거)
            smoothedDepth[i] = Mathf.Lerp(smoothedDepth[i], rawDepth[i], Time.deltaTime * lerpSpeed);

            state[i] = Classify(smoothedDepth[i], fingerAngle[i]);
        }

        // 사운드/화면경고는 대표 손가락(손 단위) 상태 전이에서만 (5배 스팸 방지)
        if (GateValid)
        {
            TensionState gs = state[gateIndex];
            if (gs != gateLastState)
            {
                OnGateStateEntered(gs);
                gateLastState = gs;
            }
        }

        // 진단: 손가락별 깊이(cm)/상태를 주기적으로 출력 (배선/방향 확인용)
        if (debugLog && Time.frameCount % 20 == 0)
        {
            string line = "";
            for (int i = 0; i < tips.Length; i++)
                line += $"{fingers[i]}={(smoothedDepth[i] * 100f):F2}cm/{state[i]}  ";
            ChunaLogger.Log($"[DepthPressureGuide] {line}(대표={fingers[gateIndex]}, GoodZone={IsInGoodZone})");
        }
    }

    private TensionState Classify(float depth, float ang)
    {
        // 충분히 안 누른 구간(접촉 직후·손떨림): 방향 판정 보류 → 대기(회색).
        // 뒤로 '크게'(< -goodMin) 당겼을 때만 방향 오류 → 미세 음수 떨림에 빨개지는 것 방지.
        if (depth < goodMin)
            return depth < -goodMin ? TensionState.WrongDirection : TensionState.Idle;

        // 여기부터는 실제로 누르는 중(depth >= goodMin)
        if (ang > maxAngle) return TensionState.WrongDirection;   // 옆으로 크게 빗나감
        if (depth >= overThreshold) return TensionState.Over;     // 과압
        if (depth <= goodMax) return TensionState.Good;           // 적정
        return TensionState.Idle; // goodMax~over 경계 = 대기(색은 그라데이션으로 경고)
    }

    private void OnGateStateEntered(TensionState s)
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

    /// <summary>특정 손가락의 압력 색. 컨트롤러가 해당 파지 구체에 주입한다.
    /// 미추적 손가락이면 대표 손가락 색으로 폴백.</summary>
    public Color ColorForFinger(CranialFinger f)
    {
        if (fingers != null)
            for (int i = 0; i < fingers.Length; i++)
                if (fingers[i] == f && tips[i] != null) return ColorAt(i);
        return CurrentColor;
    }

    /// <summary>이 손가락이 접촉/눌림 상태인가(압력색 표시 대상). 영점보다 disengageDepth 이상
    /// 뒤로 물러나면 비접촉으로 본다. 트리거가 아니라 깊이 기반이라 누르는 중에도 유지된다.</summary>
    public bool IsFingerEngaged(CranialFinger f)
    {
        if (!hasZero || fingers == null) return false;
        for (int i = 0; i < fingers.Length; i++)
            if (fingers[i] == f && tips[i] != null)
                return smoothedDepth[i] > -disengageDepth;
        return false;
    }

    private Color ColorAt(int i) => DepthColor(state[i], smoothedDepth[i]);

    /// <summary>깊이에 따른 색: 영점~적정=회색→초록, 적정=초록, 적정초과~과도=초록→노랑→빨강 그라데이션,
    /// 방향오류=빨강. 이 색을 컨트롤러가 파지 구체에 주입한다(판정은 Classify/게이트 그대로).</summary>
    private Color DepthColor(TensionState s, float depth)
    {
        if (s == TensionState.WrongDirection) return errorColor;   // 방향 틀림 = 빨강

        float d = depth;
        if (d <= goodMin)
        {
            // 영점~적정 진입: 회색→초록
            float t = goodMin > 1e-6f ? Mathf.Clamp01(d / goodMin) : 1f;
            return Color.Lerp(idleColor, goodColor, t);
        }
        if (d <= goodMax) return goodColor;                        // 적정 = 초록

        // 적정 초과~과도: 초록→노랑→빨강 (과도 임계에서 완전 빨강)
        float u = Mathf.Clamp01(Mathf.InverseLerp(goodMax, overThreshold, d));
        return u < 0.5f
            ? Color.Lerp(goodColor, warnColor, u * 2f)
            : Color.Lerp(warnColor, errorColor, (u - 0.5f) * 2f);
    }

    private void PlaySound(AudioClip clip)
    {
        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip);
    }
}
