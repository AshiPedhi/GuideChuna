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
    [Tooltip("시나리오 이름 (CSV 파일명, 표시 이름으로 공용)")]
    public string scenarioName;  // 예: "상부승모근"

    [Header("=== 환자 애니메이션 ===")]
    [Tooltip("환자 Animator Controller (직접 할당 필수)")]
    public RuntimeAnimatorController animatorController;

    [Header("=== 환자 위치 ===")]
    [Tooltip("환자 위치 프리셋 이름 (PatientPositionManager에서 사용)")]
    public string patientPositionPreset = "Seated";

    [Header("=== 나레이션 ===")]
    [Tooltip("나레이션 서브폴더 (비어있으면 scenarioName 사용) → Narrations/{난이도}/{이 값}/{clipName}")]
    public string narrationSubFolder;
}
