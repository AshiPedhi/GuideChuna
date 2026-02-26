using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 환자/침대 위치 프리셋 관리자
/// 시나리오별로 환자와 침대의 위치를 다르게 설정할 수 있도록
/// 프리셋 시스템을 제공합니다.
///
/// [사용법]
/// 1. Inspector에서 patientRoot, bedRoot 할당
/// 2. 에디터에서 환자/침대를 원하는 위치에 배치
/// 3. CustomEditor의 [현재 위치 캡처] 버튼으로 프리셋 저장
/// 4. ScenarioManager 또는 InfoPanelController에서 ApplyPreset("Seated") 호출
/// </summary>
public class PatientPositionManager : MonoBehaviour
{
    [System.Serializable]
    public class PositionPreset
    {
        [Tooltip("프리셋 이름 (예: Seated, Supine, SideLying)")]
        public string presetName;

        [Header("환자 위치")]
        public Vector3 patientPosition;
        [Tooltip("Euler angles (Inspector 편의)")]
        public Vector3 patientRotation;

        [Header("침대 위치")]
        public Vector3 bedPosition;
        [Tooltip("Euler angles (Inspector 편의)")]
        public Vector3 bedRotation;
        [Tooltip("침대 활성화 여부")]
        public bool bedActive = true;

        [Header("카메라 위치")]
        public Vector3 cameraPosition;
        [Tooltip("Euler angles (Inspector 편의)")]
        public Vector3 cameraRotation;

        [Header("골격 모델 위치")]
        public Vector3 skeletonModelPosition;
        [Tooltip("Euler angles (Inspector 편의)")]
        public Vector3 skeletonModelRotation;
    }

    [Header("=== 대상 오브젝트 ===")]
    [Tooltip("환자 루트 Transform (Patient 태그 오브젝트 또는 그 부모)")]
    [SerializeField] private Transform patientRoot;

    [Tooltip("침대 루트 Transform")]
    [SerializeField] private Transform bedRoot;

    [Tooltip("카메라 Transform (골격 촬영용 카메라)")]
    [SerializeField] private Transform cameraRoot;

    [Tooltip("골격 모델 Transform (RenderTexture용 골격 모델)")]
    [SerializeField] private Transform skeletonModelRoot;

    [Header("=== 프리셋 목록 ===")]
    [SerializeField] private List<PositionPreset> presets = new List<PositionPreset>();

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLog = true;

    // 현재 적용된 프리셋 이름
    private string currentPresetName;

    /// <summary>
    /// 현재 적용된 프리셋 이름 반환
    /// </summary>
    public string GetCurrentPreset() => currentPresetName;

    /// <summary>
    /// 프리셋 목록 (Editor 접근용)
    /// </summary>
    public List<PositionPreset> Presets => presets;

    /// <summary>
    /// 환자 루트 Transform (Editor 접근용)
    /// </summary>
    public Transform PatientRoot => patientRoot;

    /// <summary>
    /// 침대 루트 Transform (Editor 접근용)
    /// </summary>
    public Transform BedRoot => bedRoot;

    /// <summary>
    /// 카메라 Transform (Editor 접근용)
    /// </summary>
    public Transform CameraRoot => cameraRoot;

    /// <summary>
    /// 골격 모델 Transform (Editor 접근용)
    /// </summary>
    public Transform SkeletonModelRoot => skeletonModelRoot;

    void Awake()
    {
        AutoFindReferences();
    }

    /// <summary>
    /// patientRoot, bedRoot 자동 찾기
    /// </summary>
    private void AutoFindReferences()
    {
        if (patientRoot == null)
        {
            GameObject patient = GameObject.FindWithTag("Patient");
            if (patient != null)
            {
                // 부모가 있으면 부모를 루트로 사용 (환자+컨트롤러 함께 이동)
                patientRoot = patient.transform.parent != null
                    ? patient.transform.parent
                    : patient.transform;

                if (showDebugLog)
                    ChunaLogger.Log($"[PatientPositionManager] 환자 루트 자동 찾기: {patientRoot.name}");
            }
        }

        if (bedRoot == null)
        {
            GameObject bed = GameObject.Find("Bed");
            if (bed == null) bed = GameObject.Find("bed");
            if (bed != null)
            {
                bedRoot = bed.transform;
                if (showDebugLog)
                    ChunaLogger.Log($"[PatientPositionManager] 침대 루트 자동 찾기: {bedRoot.name}");
            }
        }
    }

    /// <summary>
    /// 이름으로 프리셋 검색
    /// </summary>
    public PositionPreset FindPreset(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;

        foreach (var preset in presets)
        {
            if (preset.presetName == name)
                return preset;
        }

        // 대소문자 무시 검색 (fallback)
        string lowerName = name.ToLower();
        foreach (var preset in presets)
        {
            if (preset.presetName.ToLower() == lowerName)
                return preset;
        }

        return null;
    }

    /// <summary>
    /// 프리셋 이름으로 환자/침대 위치 적용
    /// </summary>
    public bool ApplyPreset(string presetName)
    {
        PositionPreset preset = FindPreset(presetName);
        if (preset == null)
        {
            ChunaLogger.LogWarning($"[PatientPositionManager] 프리셋을 찾을 수 없습니다: {presetName}");
            return false;
        }

        return ApplyPreset(preset);
    }

    /// <summary>
    /// 프리셋 데이터로 환자/침대 위치 적용
    /// </summary>
    public bool ApplyPreset(PositionPreset preset)
    {
        if (preset == null) return false;

        // 환자 위치 적용
        if (patientRoot != null)
        {
            patientRoot.position = preset.patientPosition;
            patientRoot.rotation = Quaternion.Euler(preset.patientRotation);

            if (showDebugLog)
                ChunaLogger.Log($"[PatientPositionManager] 환자 위치 적용: pos={preset.patientPosition}, rot={preset.patientRotation}");
        }
        else
        {
            ChunaLogger.LogWarning("[PatientPositionManager] patientRoot가 null입니다!");
        }

        // 침대 위치 적용
        if (bedRoot != null)
        {
            bedRoot.position = preset.bedPosition;
            bedRoot.rotation = Quaternion.Euler(preset.bedRotation);
            bedRoot.gameObject.SetActive(preset.bedActive);

            if (showDebugLog)
                ChunaLogger.Log($"[PatientPositionManager] 침대 위치 적용: pos={preset.bedPosition}, rot={preset.bedRotation}, active={preset.bedActive}");
        }

        // 카메라 위치 적용
        if (cameraRoot != null)
        {
            cameraRoot.position = preset.cameraPosition;
            cameraRoot.rotation = Quaternion.Euler(preset.cameraRotation);

            if (showDebugLog)
                ChunaLogger.Log($"[PatientPositionManager] 카메라 위치 적용: pos={preset.cameraPosition}, rot={preset.cameraRotation}");
        }

        // 골격 모델 위치 적용
        if (skeletonModelRoot != null)
        {
            skeletonModelRoot.position = preset.skeletonModelPosition;
            skeletonModelRoot.rotation = Quaternion.Euler(preset.skeletonModelRotation);

            if (showDebugLog)
                ChunaLogger.Log($"[PatientPositionManager] 골격 모델 위치 적용: pos={preset.skeletonModelPosition}, rot={preset.skeletonModelRotation}");
        }

        currentPresetName = preset.presetName;
        ChunaLogger.Log($"[PatientPositionManager] 프리셋 적용 완료: {preset.presetName}");
        return true;
    }

    /// <summary>
    /// 현재 환자/침대 위치를 프리셋에 캡처 (Editor용)
    /// </summary>
    public void CaptureCurrentPosition(PositionPreset preset)
    {
        if (preset == null) return;

        if (patientRoot != null)
        {
            preset.patientPosition = patientRoot.position;
            preset.patientRotation = patientRoot.rotation.eulerAngles;
        }

        if (bedRoot != null)
        {
            preset.bedPosition = bedRoot.position;
            preset.bedRotation = bedRoot.rotation.eulerAngles;
            preset.bedActive = bedRoot.gameObject.activeSelf;
        }

        if (cameraRoot != null)
        {
            preset.cameraPosition = cameraRoot.position;
            preset.cameraRotation = cameraRoot.rotation.eulerAngles;
        }

        if (skeletonModelRoot != null)
        {
            preset.skeletonModelPosition = skeletonModelRoot.position;
            preset.skeletonModelRotation = skeletonModelRoot.rotation.eulerAngles;
        }

        ChunaLogger.Log($"[PatientPositionManager] 현재 위치 캡처 완료: {preset.presetName}");
    }
}
