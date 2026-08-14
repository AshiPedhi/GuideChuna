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

        [Tooltip("★켜면 회전은 프리셋이 건드리지 않고 <b>애니메이션에 맡긴다</b>(위치만 맞춤).\n" +
                 "복와위처럼 클립이 자세(엎드림)를 만드는 시나리오용 — 끄면 프리셋 회전이 " +
                 "클립이 만든 자세를 덮어써 환자가 뒤집힌다.")]
        public bool useAnimationRotation = false;

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
    // ── 애니메이션 루트 커브 보정 ────────────────────────────────────────
    // 환자 애니 클립들은 **환자 루트(c9)의 localPosition·localRotation을 직접 애니메이션**한다
    // (path="" 오브젝트 커브. 클립끼리 끝값→시작값이 이어지도록 저자가 맞춰 둔 값이다).
    // 그래서 프리셋으로 환자를 옮겨도 재생되는 순간 클립이 기록한 자리로 튄다(침대는 그대로라 어긋나 보임).
    // → 프리셋 적용 시점의 로컬값을 기준으로 잡아 두고, 이후에는 **기준 대비 변화량만** 살려서
    //   프리셋 위치에 얹는다. 클립이 의도한 미세 이동(수 cm)은 그대로 남고, 절대 위치만 프리셋을 따른다.
    [Header("애니메이션 루트 보정")]
    [Tooltip("★권장 해법: 환자(c9·복제본)를 통째로 담은 빈 부모를 여기 지정한다.\n" +
             "애니 클립은 c9의 로컬값만 건드리므로, 클립이 절대 손대지 않는 이 부모를 옮기면 충돌이 없다.\n" +
             "프리셋은 그대로 c9 기준 좌표를 쓴다 — c9가 프리셋 자리에 오도록 부모 위치를 역산한다.\n" +
             "비워 두면 예전처럼 patientRoot를 직접 옮기고 아래 보정으로 버틴다.")]
    [SerializeField] private Transform patientMoveRoot;

    [Tooltip("★기본 꺼짐. patientMoveRoot(홀더)를 못 쓰는 경우에만 쓰는 임시 방편이다.\n" +
             "매 프레임 루트를 되돌리기 때문에, 위치 설정·구체 드래그처럼 다른 코드가 환자를 옮기면\n" +
             "그 이동을 애니메이션 변화량으로 오인해 환자가 엉뚱한 곳으로 튄다.\n" +
             "정상 해법은 홀더를 지정하는 것 — 메뉴 GuideChuna/환자 이동 홀더 만들기.")]
    [SerializeField] private bool holdPresetAgainstAnimation = false;

    /// <summary>홀더 방식을 쓰는가(= 애니메이션과 싸울 필요가 없는 상태).</summary>
    private bool UsingMoveRoot => patientMoveRoot != null && patientMoveRoot != patientRoot;

    private bool anchored;
    private Vector3 anchorWorldPos;
    private Quaternion anchorWorldRot;
    private Vector3 anchorLocalPos;
    private Quaternion anchorLocalRot;

    /// <summary>현재 환자 위치를 '프리셋이 원한 위치'로 확정하고, 그때의 로컬값을 기준으로 삼는다.</summary>
    public void AnchorToCurrentPose()
    {
        if (patientRoot == null) { anchored = false; return; }
        anchorWorldPos = patientRoot.position;
        anchorWorldRot = patientRoot.rotation;
        anchorLocalPos = patientRoot.localPosition;
        anchorLocalRot = patientRoot.localRotation;
        anchored = true;
    }

    /// <summary>보정을 끈다(수동으로 환자를 옮기는 기능 등에서 호출).</summary>
    public void ReleaseAnchor() => anchored = false;

    private bool hasWritten;
    private Vector3 lastWrittenLocalPos;
    private Quaternion lastWrittenLocalRot;
    private Vector3 curDeltaLocal;
    private Quaternion curDeltaRot = Quaternion.identity;

    private void LateUpdate()
    {
        // 홀더를 쓰면 애니메이션과 겹칠 일이 없으므로 보정 자체가 필요 없다.
        if (UsingMoveRoot) return;

        // Animator가 값을 쓴 뒤(LateUpdate)에 덮어써야 이긴다.
        if (!holdPresetAgainstAnimation || !anchored || patientRoot == null) return;

        Vector3 nowLocalPos = patientRoot.localPosition;
        Quaternion nowLocalRot = patientRoot.localRotation;

        // ★우리가 쓴 값을 다시 읽어 델타에 누적시키면 매 프레임 환자가 밀려난다.
        //   Animator가 새로 쓴 프레임에만 델타를 갱신하고, 아니면 직전 델타를 유지한다.
        bool animatorWrote = !hasWritten ||
                             (nowLocalPos - lastWrittenLocalPos).sqrMagnitude > 1e-10f ||
                             Quaternion.Angle(nowLocalRot, lastWrittenLocalRot) > 0.001f;

        if (animatorWrote)
        {
            curDeltaLocal = nowLocalPos - anchorLocalPos;
            curDeltaRot = Quaternion.Inverse(anchorLocalRot) * nowLocalRot;
        }

        Transform parent = patientRoot.parent;
        Vector3 deltaWorld = parent != null ? parent.TransformVector(curDeltaLocal) : curDeltaLocal;

        patientRoot.position = anchorWorldPos + deltaWorld;
        patientRoot.rotation = anchorWorldRot * curDeltaRot;

        lastWrittenLocalPos = patientRoot.localPosition;
        lastWrittenLocalRot = patientRoot.localRotation;
        hasWritten = true;
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
            if (UsingMoveRoot)
            {
                // 홀더를 옮겨 c9(애니가 로컬값을 쓰는 오브젝트)가 프리셋 자리에 오게 한다.
                // ① 먼저 회전을 맞추고(회전하면 c9 위치가 홀더 피벗을 중심으로 돌아간다)
                //   ★useAnimationRotation이면 회전은 건드리지 않는다 — 클립이 만든 자세(엎드림 등)를
                //     프리셋 회전이 덮어써 환자가 뒤집히기 때문이다(복와위, 2026-08-12).
                if (!preset.useAnimationRotation)
                {
                    Quaternion want = Quaternion.Euler(preset.patientRotation);
                    Quaternion deltaRot = want * Quaternion.Inverse(patientRoot.rotation);
                    patientMoveRoot.rotation = deltaRot * patientMoveRoot.rotation;
                }
                // ② 그다음 남은 위치 차이만큼 홀더를 평행이동
                patientMoveRoot.position += preset.patientPosition - patientRoot.position;
            }
            else
            {
                patientRoot.position = preset.patientPosition;
                if (!preset.useAnimationRotation)
                    patientRoot.rotation = Quaternion.Euler(preset.patientRotation);
                // 보정을 켠 경우에만 기준점을 잡는다(꺼져 있으면 LateUpdate가 아무것도 하지 않는다).
                if (holdPresetAgainstAnimation) AnchorToCurrentPose();
            }

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
