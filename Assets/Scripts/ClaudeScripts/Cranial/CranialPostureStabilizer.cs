using UnityEngine;

/// <summary>
/// 호흡 단계 자세 안정화(삼각근-이마 밀착) 프록시 판정.
///
/// Quest는 팔/어깨를 직접 트래킹하지 못하므로, 시술자 헤드셋(머리)이 환자 이마에
/// 충분히 근접했는지로 "상체를 깊게 숙여 어깨를 이마에 댄 자세"를 근사 판정한다.
/// (그냥 서서 고개만 떨궈선 이 거리까지 좁혀지지 않으므로, 깊게 숙인 자세만 성립.)
///
/// 호흡 단계(②b)에서는 손이 FOV를 벗어나 파지/압력 판정이 불가능하다 →
/// 그 국면의 유지 게이트를 이 자세 프록시로 대체한다
/// (CranialAdjustmentController.StartBreathingWindow가 tensionProvider로 IsInPosition 주입).
/// </summary>
public class CranialPostureStabilizer : MonoBehaviour
{
    [Header("=== 좌표 (씬 연결) ===")]
    [Tooltip("시술자 헤드셋(중앙 카메라). 보통 OVRCameraRig > TrackingSpace > CenterEyeAnchor.")]
    [SerializeField] private Transform headset;
    [Tooltip("환자 이마 기준점. 두개골 타겟(CC_Base_Head)이나 이마 표면에 둔 마커.")]
    [SerializeField] private Transform foreheadTarget;

    [Header("=== 근접 임계 (m) — 깊게 숙여야만 도달하게 튜닝 ===")]
    [Tooltip("헤드셋-이마 거리가 이 값 이하이면 '자세 취함'(engage).")]
    [SerializeField] private float engageDistance = 0.30f;
    [Tooltip("히스테리시스: 이 값 이상 멀어지면 '자세 풀림'(release). engageDistance보다 크게.")]
    [SerializeField] private float releaseDistance = 0.38f;

    [Header("=== 안내 (선택) ===")]
    [Tooltip("호흡 국면에서 자세가 아직 안 됐을 때 켜는 안내(예: '상체를 숙여 어깨를 이마에 대세요').")]
    [SerializeField] private GameObject notEngagedHint;

    [Header("=== 디버그 ===")]
    [Tooltip("켜면 호흡 국면(active) 동안 현재 헤드셋-이마 거리와 engaged 상태를 20프레임마다 콘솔에 출력. " +
             "engageDistance 실측 튜닝용 — 실제로 어깨를 이마에 댔을 때 나오는 거리를 보고 임계값을 맞춘다.")]
    [SerializeField] private bool debugLog = false;

    private bool active = false;    // 호흡 국면 동안만 true (컨트롤러가 토글) → 안내 표시 제어
    private bool engaged = false;
    private int debugFrame = 0;

    /// <summary>현재 자세(어깨-이마 밀착 프록시)가 성립했는가. 호흡 유지 게이트에서 읽는다.</summary>
    public bool IsInPosition => engaged;

    /// <summary>현재 헤드셋-이마 거리(m). 임계 튜닝/디버그용.</summary>
    public float CurrentDistance { get; private set; } = Mathf.Infinity;

    /// <summary>호흡 국면 진입/이탈 시 컨트롤러가 호출. 안내 표시 여부를 제어한다.</summary>
    public void SetActive(bool on)
    {
        active = on;
        if (!on) engaged = false;
        UpdateHint();
    }

    void OnEnable()
    {
        engaged = false;
        UpdateHint();
    }

    void Update()
    {
        if (headset == null || foreheadTarget == null)
        {
            CurrentDistance = Mathf.Infinity;
            if (engaged) { engaged = false; UpdateHint(); }
            return;
        }

        CurrentDistance = Vector3.Distance(headset.position, foreheadTarget.position);

        // 히스테리시스로 임계 근처 깜빡임 방지
        bool prev = engaged;
        if (!engaged && CurrentDistance <= engageDistance) engaged = true;
        else if (engaged && CurrentDistance >= releaseDistance) engaged = false;

        if (engaged != prev) UpdateHint();

        // engageDistance 실측 튜닝용: 호흡 국면 동안만, 20프레임마다 거리/상태 출력
        if (debugLog && active && (++debugFrame % 20 == 0))
        {
            ChunaLogger.Log($"[CranialPostureStabilizer] 거리 {CurrentDistance * 100f:F1}cm " +
                            $"(engage≤{engageDistance * 100f:F0} / release≥{releaseDistance * 100f:F0}) " +
                            $"→ {(engaged ? "<color=green>자세 성립</color>" : "미성립")}");
        }
    }

    private void UpdateHint()
    {
        // 안내는 호흡 국면(active)에서 자세가 아직 안 됐을 때만 표시
        if (notEngagedHint != null) notEngagedHint.SetActive(active && !engaged);
    }
}
