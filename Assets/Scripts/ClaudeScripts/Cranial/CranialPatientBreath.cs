using UnityEngine;

/// <summary>
/// 환자 호흡(흉곽) 절차적 애니메이션 (선택, 시각 보조 - 기능3 보조).
///
/// 실제 술기 영상(두개골OM) 분석 결과, 환자는 앙와위로 사실상 정지해 있고 유일하게
/// 의미 있는 움직임은 호흡에 따른 흉곽의 미세한 상하이다. 환자 모델은 통짜 메시라
/// 별도 애니메이션 클립 없이 흉곽 Transform 하나를 절차적으로 구동한다
/// (CranialRhythmIndicator와 동일한 절차적 패턴 - 클립/리깅 불필요).
///
/// 동기화:
///   - 호흡 윈도우(BreathingSyncHUD.IsRunning) 중에는 HUD의 호흡 위상(BreathAmount01)을
///     그대로 따라가 링과 환자 흉곽이 같은 리듬으로 움직인다. 훈련생이 날숨에 압력을 주도록
///     맞출 시각 단서가 된다(임상: 날숨에 교정압 적용).
///   - 그 외 구간에는 느린 자율 호흡(idleBreathsPerMinute)으로 환자가 살아있는 느낌 유지.
///
/// 배치: 컴포넌트는 CranialRig(rig 토글 단위)에 두고 chestTarget에 환자 흉곽 Transform 연결.
/// 비두경부 시나리오에서는 rig가 꺼지며 함께 비활성 → 환자 기본 포즈 복원(OnDisable).
/// chestTarget은 환자 본체(머리 자식 아님)라도 무방 - Transform 참조는 계층과 무관.
/// </summary>
public class CranialPatientBreath : MonoBehaviour
{
    [Header("=== 대상 (환자 흉곽 Transform) ===")]
    [Tooltip("호흡으로 상하할 환자 흉곽/상체 Transform. 환자 모델의 Chest/Spine 본 권장.")]
    [SerializeField] private Transform chestTarget;

    [Header("=== 호흡 동기화 (선택) ===")]
    [Tooltip("연결하면 호흡 윈도우 중 링과 동일 위상으로 흉곽을 구동. 비우면 항상 자율 호흡.")]
    [SerializeField] private BreathingSyncHUD breathingHUD;

    [Header("=== 움직임 ===")]
    [Tooltip("흉곽 팽창 방향(chestTarget 로컬 기준, 보통 위/앞).")]
    [SerializeField] private Vector3 breathAxis = Vector3.up;
    [Tooltip("들숨 시 최대 변위 (m). 미세하게 (기본 8mm).")]
    [SerializeField] private float amplitude = 0.008f;
    [Tooltip("흉곽 팽창 스케일 배율 (1=스케일 안 함). 예: 1.03 = 들숨 시 3% 확장.")]
    [SerializeField] private float breathScale = 1f;
    [Tooltip("자율 호흡 속도 (분당 호흡수). 성인 안정 시 12~16.")]
    [SerializeField] private float idleBreathsPerMinute = 14f;
    [Tooltip("동기화↔자율 전환 및 흔들림 완화용 보간 속도.")]
    [SerializeField] private float smooth = 8f;

    private Vector3 basePos;
    private Vector3 baseScale;
    private bool baseCaptured = false;
    private float idlePhase = 0f;
    private float breath01 = 0f;   // 현재 적용 중인 호흡량 0(날숨)~1(들숨)

    void Awake() => CaptureBase();

    private void CaptureBase()
    {
        if (baseCaptured || chestTarget == null) return;
        basePos = chestTarget.localPosition;
        baseScale = chestTarget.localScale;
        baseCaptured = true;
    }

    void OnEnable()
    {
        CaptureBase();
        idlePhase = 0f;
    }

    void OnDisable() => ResetToBase();

    void Update()
    {
        if (chestTarget == null) return;

        float target;
        if (breathingHUD != null && breathingHUD.IsRunning)
        {
            // 호흡 윈도우: 링과 동일 위상 (훈련생 호흡 맞춤 단서)
            target = breathingHUD.BreathAmount01;
        }
        else
        {
            // 자율 호흡 (느린 sin) - 환자가 살아있는 느낌 유지
            idlePhase += Time.deltaTime * (idleBreathsPerMinute / 60f) * 2f * Mathf.PI;
            target = (Mathf.Sin(idlePhase) + 1f) * 0.5f;
        }

        breath01 = Mathf.Lerp(breath01, target, Time.deltaTime * smooth);

        Vector3 axis = breathAxis.sqrMagnitude > 1e-6f ? breathAxis.normalized : Vector3.up;
        chestTarget.localPosition = basePos + axis * (breath01 * amplitude);
        if (!Mathf.Approximately(breathScale, 1f))
            chestTarget.localScale = baseScale * Mathf.Lerp(1f, breathScale, breath01);
    }

    private void ResetToBase()
    {
        if (!baseCaptured || chestTarget == null) return;
        chestTarget.localPosition = basePos;
        chestTarget.localScale = baseScale;
        breath01 = 0f;
    }
}
