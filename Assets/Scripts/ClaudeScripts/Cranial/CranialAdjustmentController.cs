using UnityEngine;

/// <summary>
/// 두개골 교정 술기 오케스트레이터 (씬에 1개, 인스펙터 와이어링).
/// 파지 타겟 2개 / 깊이 가이드 2개 / 호흡 HUD를 보유하고, 시나리오 조건
/// (GripPointCondition / PressureCondition / BreathingCondition)이 호출할 상태 API를 노출한다.
///
/// 술기 흐름 (substep 3개):
///   ①  파지      : BothGripped 게이트
///   ②a 압력·방향 : 진입 시 영점 저장(휴식 위치) → BothInGoodZone 유지 게이트
///   ②b 호흡      : 진입 시 호흡 윈도우 시작(영점 유지) → BreathingComplete
/// </summary>
public class CranialAdjustmentController : MonoBehaviour
{
    [Header("=== 파지 (기능1) ===")]
    [SerializeField] private GripPointTarget leftGrip;   // 왼손 후두골
    [SerializeField] private GripPointTarget rightGrip;  // 오른손 측두골

    [Header("=== 깊이 압력 (기능2) ===")]
    [SerializeField] private DepthPressureGuide leftDepth;
    [SerializeField] private DepthPressureGuide rightDepth;

    [Header("=== 호흡 (기능3) ===")]
    [SerializeField] private BreathingSyncHUD breathingHUD;

    [Header("=== 리듬 인디케이터 (진단/재평가 시각화, 선택) ===")]
    [Tooltip("두개골 리듬 프록시. 호흡 교정 완료 전=비대칭(진단), 완료 후=대칭(재평가)으로 자동 전환.")]
    [SerializeField] private CranialRhythmIndicator rhythmIndicator;

    [Header("=== 포즈 인식 연동 (M1 이후) ===")]
    [Tooltip("HandPoseComparator 등 포즈 인식 결과를 grip에 주입. M1(파지 포즈 재녹화) 완료 후 연결.")]
    [SerializeField] private bool drivePoseFromComparator = false;
    // TODO(M1): PoseData/HandPoseComparator 참조를 추가하고 Update에서
    //   leftGrip.PoseRecognized  = comparator.ComparePose(leftHand, leftGuidePoses, out _);
    //   rightGrip.PoseRecognized = comparator.ComparePose(rightHand, rightGuidePoses, out _);
    // 로 매 프레임 주입한다. 그 전까지는 GripPointTarget.bypassPoseCheck로 트리거만으로 테스트.

    // === 파지 상태 ===
    public bool BothGripped =>
        leftGrip != null && rightGrip != null && leftGrip.IsGripped && rightGrip.IsGripped;

    // === 압력 상태 (압력 substep 게이트용) ===
    /// <summary>양손 모두 적정 텐션 존(올바른 깊이·방향). 가이드 미설정 시 해당 손은 통과 처리.</summary>
    public bool BothInGoodZone =>
        (leftDepth == null || leftDepth.IsInGoodZone) &&
        (rightDepth == null || rightDepth.IsInGoodZone);

    /// <summary>존재하는 모든 깊이 가이드에 영점이 저장됐는지 (호흡 substep의 영점 누락 방어용)</summary>
    public bool HasZeroPoints =>
        (leftDepth == null || leftDepth.HasZeroPoint) &&
        (rightDepth == null || rightDepth.HasZeroPoint);

    // === 조건이 호출하는 진입/상태 API ===

    /// <summary>① 파지 substep 시작 시 호출 (초기화/활성화 훅)</summary>
    public void BeginGripPhase()
    {
        leftDepth?.ClearZeroPoint();
        rightDepth?.ClearZeroPoint();
        if (breathingHUD != null) breathingHUD.gameObject.SetActive(false);
        ChunaLogger.Log("[CranialAdjustmentController] 파지 단계 시작");
    }

    /// <summary>②a 진입 시: 현재 파지 위치(휴식)를 영점으로 저장</summary>
    public void SaveZeroPoints()
    {
        leftDepth?.SaveZeroPoint();
        rightDepth?.SaveZeroPoint();
    }

    /// <summary>②b 호흡 윈도우 시작 (텐션 공급자 = 양손 모두 적정존)</summary>
    public void StartBreathingWindow()
    {
        if (breathingHUD == null)
        {
            ChunaLogger.LogWarning("[CranialAdjustmentController] BreathingSyncHUD 미설정");
            return;
        }

        breathingHUD.SetTensionProvider(() =>
            (leftDepth == null || leftDepth.IsInGoodZone) &&
            (rightDepth == null || rightDepth.IsInGoodZone));
        breathingHUD.StartWindow();
    }

    public bool BreathingComplete => breathingHUD != null && breathingHUD.IsComplete;

    void Update()
    {
        if (drivePoseFromComparator)
        {
            // TODO(M1): 여기서 HandPoseComparator 결과를 leftGrip/rightGrip.PoseRecognized에 주입
        }

        // 두개골 리듬: 호흡 교정 완료 전 = 비대칭(진단), 완료 후 = 대칭(재평가)
        if (rhythmIndicator != null)
            rhythmIndicator.SetMode(BreathingComplete
                ? CranialRhythmIndicator.Mode.Symmetric
                : CranialRhythmIndicator.Mode.Asymmetric);
    }

    /// <summary>술기 종료/리셋 (시나리오 시작·재시작 시 호출 → 래칭 상태 정리)</summary>
    public void ResetAll()
    {
        leftGrip?.ResetState();
        rightGrip?.ResetState();
        leftDepth?.ClearZeroPoint();
        rightDepth?.ClearZeroPoint();
        breathingHUD?.ResetState();
        rhythmIndicator?.SetMode(CranialRhythmIndicator.Mode.Asymmetric);
    }
}
