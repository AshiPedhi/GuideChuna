using UnityEngine;

/// <summary>
/// 실습(교육)모드에서 <b>손으로 잰 각</b>을 추적해 결과지 부록에 남긴다.
///
/// ★★<b>2026-09-07 — 화면 표시를 걷었다(사용자 지시).</b>
///   종전에는 환자 머리 위에 <c>손 32° / 대본 30° / 차 2°</c>를 띄웠는데, 그 자리는 이제
///   <see cref="CervicalRomPracticeReadout"/>이 실측과 같은 모양으로 <b>손 옆에</b> 그린다.
///   ★<b>기록은 그대로 남긴다</b>(사용자 확정) — 아직 안 끝난 검증이 하나 있다:
///     "실제 각도기와 절대값 대조"(09-01부터 미검증). 그 대조에 쓸 자료가 여기서 나온다.
///   그래서 이 컴포넌트는 이제 <b>보이지 않고 재기만 한다.</b> 미사용 코드가 아니다.
///
/// ★측정 로직은 실측모드와 같다 — 양손 파지 벡터를 그 동작의 회전면에 투영해 회전각을 읽는다.
///   다른 점은 기준틀을 <b>가상 환자의 몸통에서 그냥 가져온다</b>는 것뿐이다.
///   실제 환자는 어깨를 짚어 축을 세워야 하지만, 여기선 모델이 있으니 그럴 필요가 없다.
///
/// 용도는 A-12(실측 각도 정확도 검증)다. 판정·채점에는 아무 영향이 없다 — 읽기만 한다.
/// </summary>
public class CervicalRomHandAngleProbe : MonoBehaviour
{
    [Header("=== 참조 (비우면 자동 탐색) ===")]
    [SerializeField] private CervicalRomDriver driver;
    [SerializeField] private CervicalGripJudge gripJudge;

    [Header("=== 측정 ===")]
    [Tooltip("파지 벡터 저역통과 시간상수(초). 0이면 생값.")]
    [SerializeField] private float smoothing = 0.08f;
    [Tooltip("면 성분이 이 비율보다 작으면 그 파지로는 못 재는 것이다.")]
    [SerializeField] private float minPerpRatio = 0.34f;
    [Tooltip("0점을 다시 잡는 기준 — 대본 각이 이보다 작으면 지금을 중립으로 본다(도).")]
    [SerializeField] private float neutralResetAngle = 1.5f;

    [Tooltip("결과 화면 맨 아래에 <b>손 각 부록</b>(A-12 교차검증 표)을 붙인다.\n" +
             "★<b>기본은 끔</b>이다(2026-09-08 사용자 지시: \"실습결과 임시 항목 빼\").\n" +
             "★끄면 제공자를 안 꽂는다 = 부록이 안 붙는다. 기록은 계속 쌓이므로 켜는 즉시 다시 나온다.\n" +
             "★이 컴포넌트는 브리지가 런타임에 붙이므로 씬에 안 굳는다 — 코드 기본값이 그대로 먹는다.")]
    [SerializeField] private bool attachResultAppendix;

    [SerializeField] private bool showDebugLogs = false;

    private CervicalRomDriver.Direction zeroedFor = CervicalRomDriver.Direction.None;
    private Vector3 v0;
    private Vector3 vNow;
    private bool vNowValid;

    /// <summary>지금 읽히는 손 측정각(도, 크기). 못 재면 false.</summary>
    public bool TryGetHandAngle(out float degrees, out float perpRatio)
    {
        degrees = 0f; perpRatio = 0f;
        if (driver == null || !vNowValid) return false;
        if (zeroedFor == CervicalRomDriver.Direction.None) return false;

        Vector3 axis = driver.CurrentWorldAxis;
        if (axis.sqrMagnitude < 1e-8f) return false;

        Vector3 a = Vector3.ProjectOnPlane(v0, axis);
        Vector3 b = Vector3.ProjectOnPlane(vNow, axis);
        if (a.sqrMagnitude < 1e-8f || b.sqrMagnitude < 1e-8f) return false;

        perpRatio = a.magnitude / Mathf.Max(1e-6f, v0.magnitude);
        degrees = Mathf.Abs(Vector3.SignedAngle(a, b, axis));
        return true;
    }

    private void Awake()
    {
        if (driver == null) driver = FindFirstObjectByType<CervicalRomDriver>();
        if (gripJudge == null) gripJudge = FindFirstObjectByType<CervicalGripJudge>();

        // ★[A-12] 지난 판 기록을 지운다. static이라 씬을 다시 열어도 값이 남는다 —
        //   여기서 지워야 이번 판만 나온다.
        CervicalRomHandAngleLog.Clear();

        // ★★<b>결과 화면 부록은 기본으로 안 붙인다</b>(2026-09-08 지시: "실습결과 임시 항목 빼").
        //   `CervicalRomHandAngleLog`가 스스로 <b>[임시 · A-12 교차검증 전용]</b>이라 적어 둔 그것이다.
        //   ★<b>제공자를 안 꽂으면 부록이 안 붙는다</b> — 그 파일의 '지우는 법'이 그렇게 적혀 있다.
        //     그래서 코드는 한 줄도 안 지웠다(미사용 코드 방침). 검증이 다시 필요하면 이 스위치만 켠다.
        //   ★기록 자체는 계속 쌓는다 — 비용이 없고, 켜는 순간 바로 쓸 수 있어야 한다.
        TrainingResultData.RomAppendixProvider = attachResultAppendix
                                              ? CervicalRomHandAngleLog.BuildAppendix
                                              : null;
    }

    // ★[A-12] 드라이버가 측정값을 남기는 순간을 듣는다. 그 순간의 손 각을 같이 찍어 둔다.
    private void OnEnable()
    {
        if (driver != null) driver.OnMeasurementRecorded += HandleMeasurementRecorded;
    }

    private void OnDisable()
    {
        if (driver != null) driver.OnMeasurementRecorded -= HandleMeasurementRecorded;
        ResetTracking();
    }

    private void OnDestroy()
    {
        if (driver != null) driver.OnMeasurementRecorded -= HandleMeasurementRecorded;
    }

    /// <summary>
    /// ★[A-12] 대본이 능동·수동 끝점을 기록한 <b>그 프레임의</b> 손 측정각을 남긴다.
    /// 나중에 다시 읽으면 이미 각이 변해 있어 의미가 없다.
    /// </summary>
    private void HandleMeasurementRecorded(CervicalRomDriver.Direction dir, bool isActive)
    {
        bool ok = TryGetHandAngle(out float measured, out float perp);
        CervicalRomHandAngleLog.Record(dir, isActive,
                                       measurable: ok && perp >= minPerpRatio,
                                       handDegrees: measured,
                                       scriptedDegrees: driver != null ? driver.CurrentAngle : 0f);

        if (showDebugLogs)
            ChunaLogger.Log($"<color=cyan>[손각도] {dir} {(isActive ? "능동" : "수동")} 기록 — " +
                            $"손 {(ok ? measured.ToString("F1") : "--")}° / 대본 {(driver != null ? driver.CurrentAngle : 0f):F1}°</color>");
    }

    private void LateUpdate()
    {
        // ★Awake 때 드라이버가 아직 없었으면 여기서 잡고 그때 구독한다.
        //   런타임에 붙는 컴포넌트라 순서를 장담할 수 없다.
        if (driver == null)
        {
            driver = FindFirstObjectByType<CervicalRomDriver>();
            if (driver == null) { ResetTracking(); return; }
            driver.OnMeasurementRecorded += HandleMeasurementRecorded;
        }

        CervicalRomDriver.Direction dir = driver.CurrentDirection;
        if (dir == CervicalRomDriver.Direction.None) { ResetTracking(); return; }

        // --- 손 벡터 ---
        bool has = TryGetPair(out Vector3 raw);
        if (has)
        {
            if (!vNowValid || smoothing <= 0f) vNow = raw;
            else
            {
                float k = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(1e-4f, smoothing));
                vNow = Vector3.Lerp(vNow, raw, k);
            }
            vNowValid = true;
        }
        else vNowValid = false;

        // --- 0점 ---
        // ★방향이 바뀌었거나, 대본 각이 아직 중립이면 지금을 0으로 잡는다.
        //   실측모드처럼 사람이 정렬을 잡아 주지 않으므로 여기서 알아서 잡는다.
        if (vNowValid && (zeroedFor != dir || driver.CurrentAngle <= neutralResetAngle))
        {
            v0 = vNow;
            if (zeroedFor != dir && showDebugLogs)
                ChunaLogger.Log($"<color=cyan>[손각도] {dir} 0점 재설정</color>");
            zeroedFor = dir;
        }
    }

    private bool TryGetPair(out Vector3 v)
    {
        v = Vector3.zero;
        if (gripJudge == null) return false;
        // ★엄지·검지 파지 지점만 쓴다. 손목·손바닥은 안 본다(2026-08-28 사용자 지시).
        if (!gripJudge.TryGetPinchPoint(GripFingerTip.Side.Left, out Vector3 l)) return false;
        if (!gripJudge.TryGetPinchPoint(GripFingerTip.Side.Right, out Vector3 r)) return false;
        v = r - l;
        return v.sqrMagnitude > 1e-6f;
    }

    private void ResetTracking()
    {
        vNowValid = false;
    }
}
