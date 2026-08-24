using UnityEngine;

/// <summary>
/// 시나리오별 설정 데이터 (ScriptableObject)
/// scenarioName으로 CSV, 나레이션 폴더, 표시 이름을 자동 해결
/// animatorController는 직접 참조
/// </summary>
[CreateAssetMenu(fileName = "ScenarioConfig", menuName = "Chuna/Scenario Config")]
public class ScenarioConfig : ScriptableObject
{
    [Header("=== 시나리오 ===")]
    [Tooltip("시나리오 이름 = 화면 제목이자 내부 키. 다음 다섯 곳이 ★전부 같아야 한다:\n" +
             "  ① 이 칸  ② config 에셋 파일명  ③ CSV 파일명  ④ CSV의 scenarioName 열  ⑤ 나레이션 폴더명\n" +
             "  ⑥ (두개골·늑골류) 씬 파지점 리그의 scenarioName\n" +
             "하나라도 다르면 나레이션이 안 나오거나 파지점 리그를 못 찾는다. 개명은 이 여섯 개를 한 번에.")]
    public string scenarioName;  // 예: "상부승모근"

    [Header("=== 환자 애니메이션 ===")]
    [Tooltip("환자 Animator Controller (직접 할당 필수)")]
    public RuntimeAnimatorController animatorController;

    [Header("=== 환자 위치 ===")]
    [Tooltip("환자 위치 프리셋 이름 (PatientPositionManager에서 사용)")]
    public string patientPositionPreset = "Seated";

    [Header("=== 접촉 감지 부위 ===")]
    [Tooltip("주동수 접촉 부위 (기본: Head)")]
    public ContactTarget primaryContactTarget = ContactTarget.Head;

    [Tooltip("보조수 접촉 부위 (기본: Shoulder)")]
    public ContactTarget assistContactTarget = ContactTarget.Shoulder;

    [Tooltip("자세지시 접촉 부위 (기본: LeftArm)")]
    public ContactTarget postureGuideContactTarget = ContactTarget.LeftArm;

    [Header("=== 나레이션 ===")]
    [Tooltip("나레이션 서브폴더 (비어있으면 scenarioName 사용) → Narrations/{난이도}/{이 값}/{clipName}")]
    public string narrationSubFolder;

    [Header("=== 평가 임계점 — 시나리오 기본값 (선택) ===")]
    [Tooltip("true면 아래 값으로 ChunaPathEvaluator 기본 임계점을 오버라이드")]
    public bool overrideEvaluationThresholds = false;

    [Tooltip("일반 모드 적정범위 시작 (기본 0.3)")]
    public float midHoldStart = 0.3f;

    [Tooltip("일반 모드 적정범위 끝 (기본 0.5)")]
    public float midHoldEnd = 0.5f;

    [Tooltip("스트레칭 적정범위 시작 (기본 0.45)")]
    public float stretchingHoldStart = 0.45f;

    [Tooltip("스트레칭 적정범위 끝 / 가이드 끝 (기본 0.65)")]
    public float stretchingHoldEnd = 0.65f;

    [Tooltip("재평가 적정범위 시작 (기본 0.45)")]
    public float extendedMidHoldStart = 0.45f;

    [Tooltip("재평가 적정범위 끝 (기본 0.65)")]
    public float extendedMidHoldEnd = 0.65f;

    [Header("=== 회전 감지 축 오버라이드 (누운 환자용) ===")]
    [Tooltip("true면 '회전' 감지 시 아래 축을 사용 (기본 Y축 대신). 누운 환자의 머리 회전 등")]
    public bool overrideRotationAxis = false;

    [Tooltip("누운 환자의 머리 회전 감지 축 (Lying: Z축 권장)")]
    public ChunaPathEvaluator.RotationDetectionAxis lyingRotationAxis = ChunaPathEvaluator.RotationDetectionAxis.Z;

    [Header("=== Phase별 임계점 오버라이드 (선택) ===")]
    [Tooltip("Phase 이름으로 매칭하여 해당 phase 진입 시 임계점 교체 (사각근 전부/중부/후부 등)")]
    public PhaseThresholdOverride[] phaseOverrides;

    [Header("=== 평가모드 Phase 필터 (선택) ===")]
    [Tooltip("평가모드에서만 진행할 phase 이름들 (비어있으면 전체 phase 진행). 예: 상부승모근 중부만 평가하려면 [시작, 중부, 종료]")]
    public string[] evaluationPhases;

    public bool HasEvaluationPhaseFilter => evaluationPhases != null && evaluationPhases.Length > 0;

    public bool IsEvaluationPhaseAllowed(string phaseName)
    {
        if (!HasEvaluationPhaseFilter) return true;
        foreach (var p in evaluationPhases)
        {
            if (p == phaseName) return true;
        }
        return false;
    }

    [Header("=== 진행 방식별 Phase 필터 (선택) ===")]
    [Tooltip("교육 모드에서 진행할 phase 이름들. 비어있으면 전체 진행(= 기존 동작).\n" +
             "예: 경추ROM은 [시작, 교육, 종료]")]
    public string[] educationPhases;

    [Tooltip("실측 모드에서 진행할 phase 이름들. 비어있으면 이 시나리오는 실측을 지원하지 않는다.\n" +
             "예: 경추ROM은 [시작, 실측, 종료]")]
    public string[] measurementPhases;

    /// <summary>실측 모드로 진입할 수 있는 시나리오인가 (로비·정보패널에서 선택지를 띄울지 판단)</summary>
    public bool SupportsMeasurementMode => measurementPhases != null && measurementPhases.Length > 0;

    /// <summary>
    /// 진행 방식에 해당하는 phase 화이트리스트. 비어 있으면(null) 필터하지 않는다.
    /// ★기존 시나리오는 두 배열이 모두 비어 있어 종전과 동일하게 전체가 진행된다.
    /// </summary>
    public string[] GetModePhases(ChunaTraining.ScenarioMode mode)
    {
        string[] list = mode == ChunaTraining.ScenarioMode.Measurement ? measurementPhases : educationPhases;
        return (list != null && list.Length > 0) ? list : null;
    }

    public bool IsModePhaseAllowed(ChunaTraining.ScenarioMode mode, string phaseName)
    {
        string[] list = GetModePhases(mode);
        if (list == null) return true;
        foreach (var p in list)
        {
            if (p == phaseName) return true;
        }
        return false;
    }

    /// <summary>
    /// Phase별 회전 적정범위 오버라이드
    /// movementType이 rotation인 스텝에서만 적용, 측굴 등은 기본값 유지
    /// </summary>
    [System.Serializable]
    public class PhaseThresholdOverride
    {
        [Tooltip("CSV phase 이름 (예: 전부, 중부, 후부)")]
        public string phaseName;

        [Tooltip("회전 적정범위 시작")]
        public float rotationHoldStart = 0.3f;

        [Tooltip("회전 적정범위 끝")]
        public float rotationHoldEnd = 0.5f;
    }

    /// <summary>
    /// phase 이름으로 오버라이드 찾기 (없으면 null)
    /// </summary>
    public PhaseThresholdOverride FindPhaseOverride(string phaseName)
    {
        if (phaseOverrides == null || string.IsNullOrEmpty(phaseName)) return null;

        foreach (var po in phaseOverrides)
        {
            if (po.phaseName == phaseName) return po;
        }
        return null;
    }
}
