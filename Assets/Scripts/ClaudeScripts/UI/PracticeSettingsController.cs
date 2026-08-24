using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 실습 내부 설정 UI 컨트롤러
/// 모든 버튼은 토글 형태로 동작
/// </summary>
public class PracticeSettingsController : MonoBehaviour
{
    [Header("═══ 토글 버튼 참조 ═══")]
    [SerializeField] private Toggle customPositioningToggle;      // 맞춤 설정
    [SerializeField] private Toggle patientPositionToggle;        // 환자 위치 조정
    [SerializeField] private Toggle skeletonDisplayToggle;        // 근골격계 표시
    [SerializeField] private Toggle patientModelDisplayToggle;    // 환자 모델 표시
    [SerializeField] private Toggle realityModeToggle;            // 현실 모드

    [Header("═══ 맞춤 설정 ═══")]
    [SerializeField] private Transform targetObject;              // 위치를 초기화할 오브젝트
    [SerializeField] private Transform customReferencePoint;      // 커스텀 기준점 (옵션)
    [SerializeField] private Transform headsetTransform;          // 헤드셋 Transform (OVR CenterEyeAnchor)
    [SerializeField] private float defaultForwardDistance = 0.5f; // 기본 전방 거리
    [SerializeField] private float defaultHeightOffset = -0.3f;   // 헤드셋 높이 기준 오프셋 (음수 = 아래)

    [Header("═══ 맞춤 설정 미리보기 ═══")]
    [Tooltip("Scene뷰에서 맞춤 설정 목표 위치를 Gizmo로 표시")]
    [SerializeField] private bool showPositioningGizmo = true;

    // Editor 접근용
    public Transform TargetObject => targetObject;
    public Transform CustomReferencePoint => customReferencePoint;
    public Transform HeadsetTransform => headsetTransform;
    public float DefaultForwardDistance { get => defaultForwardDistance; set => defaultForwardDistance = value; }
    public float DefaultHeightOffset { get => defaultHeightOffset; set => defaultHeightOffset = value; }

    [Header("═══ 환자 위치 조정 ═══")]
    [SerializeField] private GameObject patientPositionController; // 환자 위치 조정 컨트롤러

    [Header("═══ 모델 표시 ═══")]
    [SerializeField] private GameObject skeletonModel;            // 근골격계 모델
    [SerializeField] private GameObject patientModel;             // 환자 모델

    [Header("═══ 투명도 설정 ═══")]
    [SerializeField][Range(0f, 1f)] private float skeletonModeAlpha = 0.3f;  // 골격 표시 시 환자 모델 알파값
    [SerializeField][Range(0f, 1f)] private float realityModeAlpha = 0.5f;   // 현실 모드 시 모델 알파값
    [SerializeField][Range(0f, 1f)] private float normalAlpha = 1f;          // 일반 상태 알파값

    [Tooltip("투명도 처리에서 제외할 렌더러 이름(부분일치). 특수 셰이더(눈·각막·치아 등)는 렌더 상태를 " +
             "바꾸면 사라지므로 건드리지 않는다.")]
    [SerializeField] private string[] transparencyExcludeNames =
        { "CC_Base_Eye", "EyeOcclusion", "TearLine", "CC_Base_Teeth", "Cornea", "Tongue", "Eyelash" };

    [Tooltip("★기본 ON — 골격(skeletal_system 하위)은 어떤 경우에도 반투명으로 만들지 않는다.\n\n" +
             "2026-08-17 사용자 지적: \"xray 키니까 뼈 색상 빠지고 반투명해진다\".\n" +
             "원인 = 여기 SetModelTransparency/SetMeshTransparency가 골격 렌더러의 머티리얼에 " +
             "_Mode=3(Transparent) · ZWrite=0 · _Color.a=알파를 직접 먹였다. 그 알파가 부위별 색까지 " +
             "씻어내 <b>분할·색칠한 의미가 사라진다.</b>\n" +
             "★xray가 켜지면 skullOverlay(=이 skeletonModel과 같은 '근육골격' 오브젝트)가 활성화되므로 " +
             "그때부터 skeletonModel.activeSelf가 true가 되고, 현실 모드 경로가 골격까지 반투명 대상으로 " +
             "잡아 버린다 — 그래서 'xray를 켠 순간'에 증상이 난다.\n" +
             "CranialHeadXray는 이미 같은 규칙(skeletal_system 제외)을 쓰고 있다. 여기만 빠져 있었다.\n" +
             "끄면 예전 동작(골격도 현실 모드에서 반투명).")]
    [SerializeField] private bool keepSkeletonOpaque = true;


    [Header("═══ 현실 모드 (패스쓰루) ═══")]
    [SerializeField] private GameObject backgroundObject;         // 배경 오브젝트
    [Tooltip("씬 시작 시 패스쓰루 ON 상태로 진입 (HandRecord 등 패스스루 전용 씬용)")]
    [SerializeField] private bool startWithRealityMode = false;

    // 패스쓰루 레이어 (런타임에 찾음)
    private OVRPassthroughLayer passthroughLayer;
    private bool isRealityModeOn = false;

    // SkinnedMeshRenderer 캐싱
    private SkinnedMeshRenderer[] patientRenderers;
    private SkinnedMeshRenderer[] skeletonRenderers;

    // MeshRenderer 캐싱 (환자 몸 등)
    private MeshRenderer[] patientMeshRenderers;
    private MeshRenderer[] skeletonMeshRenderers;

    // 원본 머티리얼 저장 (투명도 복원용)
    private Material[][] originalPatientMaterials;
    private Material[][] originalSkeletonMaterials;
    private Material[][] originalPatientMeshMaterials;
    private Material[][] originalSkeletonMeshMaterials;

    // InfoPanelController 참조 (설정 상태 관리용 - 선택적)
    private InfoPanelController infoPanelController;

    // ── 환자 그룹 드리프트 보정용 ──
    // 위치설정(ReplacePos)은 환자 그룹(all)의 "월드" 위치/회전을 매 프레임 직접 덮어쓴다.
    // 그 결과 부모(targetObject) 기준 로컬 오프셋이 누적 변형되어, 다시 맞춤 설정을 누르면
    // 옮긴 만큼 밀린다. 시작 시 authored(깨끗한) 로컬 포즈를 캐싱해 두고, 맞춤 설정 때 복원한다.
    private Transform patientGroupTransform;
    private Vector3 patientGroupAuthoredLocalPos;
    private Quaternion patientGroupAuthoredLocalRot;
    private bool patientGroupCaptured = false;

    void Awake()
    {
        // InfoPanelController 찾기 (선택적)
        infoPanelController = FindFirstObjectByType<InfoPanelController>();

        // 헤드셋 Transform 자동 찾기 (할당 안 되어 있으면)
        if (headsetTransform == null)
        {
            GameObject ovrCameraRig = GameObject.Find("OVRCameraRig");
            if (ovrCameraRig != null)
            {
                headsetTransform = ovrCameraRig.transform.Find("TrackingSpace/CenterEyeAnchor");
                if (headsetTransform != null)
                {
                    ChunaLogger.Log("[PracticeSettings] ✅ CenterEyeAnchor 자동 찾기 성공");
                }
            }
        }

        // 패스쓰루 레이어 찾기
        InitializePassthrough();
    }

    void Start()
    {
        CacheRenderers();
        SetupToggleListeners();
        InitializeToggles();
        CachePatientGroupAuthoredPose();
    }

    /// <summary>
    /// 위치설정(ReplacePos)이 매 프레임 덮어쓰는 환자 그룹(all)의 깨끗한 로컬 포즈를 캐싱.
    /// Start 시점에는 아직 위치설정을 사용하지 않아 드리프트가 없으므로 authored 값이 잡힌다.
    /// </summary>
    private void CachePatientGroupAuthoredPose()
    {
        if (patientPositionController == null) return;

        ReplacePos replacePos = patientPositionController.GetComponent<ReplacePos>();
        if (replacePos != null && replacePos.all != null)
        {
            patientGroupTransform = replacePos.all.transform;
            patientGroupAuthoredLocalPos = patientGroupTransform.localPosition;
            patientGroupAuthoredLocalRot = patientGroupTransform.localRotation;
            patientGroupCaptured = true;
            ChunaLogger.Log($"[PracticeSettings] 환자 그룹 기준 포즈 캐싱: {patientGroupTransform.name} localPos={patientGroupAuthoredLocalPos}");
        }
        else
        {
            ChunaLogger.LogWarning("[PracticeSettings] ReplacePos/all을 찾지 못해 환자 그룹 드리프트 보정을 건너뜁니다.");
        }
    }

    /// <summary>
    /// SkinnedMeshRenderer, MeshRenderer 캐싱 및 원본 머티리얼 저장
    /// </summary>
    void CacheRenderers()
    {
        // ★ 환자 모델 렌더러 캐싱 - Patient 태그로 루트 찾기
        GameObject patientRoot = patientModel;

        // patientModel이 없거나 렌더러가 없으면 Patient 태그로 찾기
        if (patientRoot == null)
        {
            patientRoot = GameObject.FindWithTag("Patient");
        }

        if (patientRoot != null)
        {
            // ★ 환자 루트와 모든 자식에서 렌더러 찾기
            SkinnedMeshRenderer[] allSkinnedRenderers = patientRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            MeshRenderer[] allMeshRenderers = patientRoot.GetComponentsInChildren<MeshRenderer>(true);

            // SkinnedMeshRenderer 캐싱 (몸체 + 옷)
            if (allSkinnedRenderers.Length > 0)
            {
                patientRenderers = allSkinnedRenderers;
                originalPatientMaterials = new Material[patientRenderers.Length][];
                for (int i = 0; i < patientRenderers.Length; i++)
                {
                    originalPatientMaterials[i] = patientRenderers[i].materials;
                }
                ChunaLogger.Log($"[PracticeSettings] ✅ 환자 모델 SkinnedMeshRenderer {patientRenderers.Length}개 캐싱 완료 (루트: {patientRoot.name})");

                // 각 렌더러 이름 로그
                foreach (var renderer in patientRenderers)
                {
                    ChunaLogger.Log($"[PracticeSettings]   - SkinnedMesh: {renderer.gameObject.name}");
                }
            }

            // MeshRenderer 캐싱
            if (allMeshRenderers.Length > 0)
            {
                patientMeshRenderers = allMeshRenderers;
                originalPatientMeshMaterials = new Material[patientMeshRenderers.Length][];
                for (int i = 0; i < patientMeshRenderers.Length; i++)
                {
                    originalPatientMeshMaterials[i] = patientMeshRenderers[i].materials;
                }
                ChunaLogger.Log($"[PracticeSettings] ✅ 환자 모델 MeshRenderer {patientMeshRenderers.Length}개 캐싱 완료");
            }

            // patientModel 참조 업데이트
            if (patientModel == null)
            {
                patientModel = patientRoot;
            }
        }
        else
        {
            ChunaLogger.LogWarning("[PracticeSettings] ⚠ 환자 모델을 찾을 수 없습니다 (patientModel 미할당, Patient 태그 없음)");
        }

        // 골격 모델 렌더러 캐싱
        if (skeletonModel != null)
        {
            // SkinnedMeshRenderer
            skeletonRenderers = skeletonModel.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skeletonRenderers.Length > 0)
            {
                originalSkeletonMaterials = new Material[skeletonRenderers.Length][];
                for (int i = 0; i < skeletonRenderers.Length; i++)
                {
                    originalSkeletonMaterials[i] = skeletonRenderers[i].materials;
                }
                ChunaLogger.Log($"[PracticeSettings] ✅ 골격 모델 SkinnedMeshRenderer {skeletonRenderers.Length}개 캐싱 완료");
            }

            // MeshRenderer
            skeletonMeshRenderers = skeletonModel.GetComponentsInChildren<MeshRenderer>(true);
            if (skeletonMeshRenderers.Length > 0)
            {
                originalSkeletonMeshMaterials = new Material[skeletonMeshRenderers.Length][];
                for (int i = 0; i < skeletonMeshRenderers.Length; i++)
                {
                    originalSkeletonMeshMaterials[i] = skeletonMeshRenderers[i].materials;
                }
                ChunaLogger.Log($"[PracticeSettings] ✅ 골격 모델 MeshRenderer {skeletonMeshRenderers.Length}개 캐싱 완료");
            }
        }
    }

    void InitializePassthrough()
    {
        GameObject ovrCameraRig = GameObject.Find("OVRCameraRig");
        if (ovrCameraRig != null)
        {
            passthroughLayer = ovrCameraRig.GetComponent<OVRPassthroughLayer>();
            if (passthroughLayer == null)
            {
                passthroughLayer = ovrCameraRig.AddComponent<OVRPassthroughLayer>();
                ChunaLogger.Log("[PracticeSettings] OVRPassthroughLayer 컴포넌트 추가됨");
            }
        }
        else
        {
            ChunaLogger.LogError("[PracticeSettings] OVRCameraRig를 찾을 수 없습니다!");
        }
    }

    void SetupToggleListeners()
    {
        // 1. 맞춤 설정 (버튼처럼 동작)
        if (customPositioningToggle != null)
        {
            customPositioningToggle.onValueChanged.RemoveAllListeners();
            customPositioningToggle.onValueChanged.AddListener((isOn) => {
                if (isOn)
                {
                    OnCustomPositioning();
                    StartCoroutine(ResetToggle(customPositioningToggle));
                }
            });
            ChunaLogger.Log("[PracticeSettings] ✅ 맞춤 설정 토글 연결");
        }

        // 2. 환자 위치 조정 (일반 토글)
        if (patientPositionToggle != null)
        {
            patientPositionToggle.onValueChanged.RemoveAllListeners();
            patientPositionToggle.onValueChanged.AddListener(OnPatientPositionToggle);
            ChunaLogger.Log("[PracticeSettings] ✅ 환자 위치 조정 토글 연결");
        }

        // 3. 근골격계 표시 (일반 토글)
        if (skeletonDisplayToggle != null)
        {
            skeletonDisplayToggle.onValueChanged.RemoveAllListeners();
            skeletonDisplayToggle.onValueChanged.AddListener(OnSkeletonDisplayToggle);
            ChunaLogger.Log("[PracticeSettings] ✅ 근골격계 표시 토글 연결");
        }

        // 4. 환자 모델 표시 (일반 토글)
        if (patientModelDisplayToggle != null)
        {
            patientModelDisplayToggle.onValueChanged.RemoveAllListeners();
            patientModelDisplayToggle.onValueChanged.AddListener(OnPatientModelDisplayToggle);
            ChunaLogger.Log("[PracticeSettings] ✅ 환자 모델 표시 토글 연결");
        }

        // 5. 현실 모드 (일반 토글)
        if (realityModeToggle != null)
        {
            realityModeToggle.onValueChanged.RemoveAllListeners();
            realityModeToggle.onValueChanged.AddListener(OnRealityModeToggle);
            ChunaLogger.Log("[PracticeSettings] ✅ 현실 모드 토글 연결");
        }
    }

    void InitializeToggles()
    {
        // 환자 위치 조정 컨트롤러 초기 상태 (off)
        if (patientPositionController != null)
        {
            patientPositionController.SetActive(false);
        }

        // 토글 초기 상태 설정
        if (patientPositionToggle != null)
            patientPositionToggle.isOn = false;

        // ★시작 시 골격표시 강제 OFF(환자 불투명 유지) — 시작하자마자 자동 반투명되는 것 방지.
        //   사용자가 토글로 켤 때만 골격표시·반투명 동작. (SetIsOnWithoutNotify로 리스너 미발동)
        //   ※ xray(피부 투명)는 CranialHeadXray가 담당 — 여기 SetModelTransparency는 Reallusion 피부에
        //     알파를 못 먹여 옷만 투명해지므로 시작 xray 용도로 쓰지 않는다.
        if (skeletonModel != null)
            skeletonModel.SetActive(false);
        if (skeletonDisplayToggle != null)
            skeletonDisplayToggle.SetIsOnWithoutNotify(false);

        // 환자 모델은 기본적으로 표시 (알파값 기반이므로 초기값 true)
        if (patientModelDisplayToggle != null)
            patientModelDisplayToggle.isOn = true;

        // 시작 시 현실 모드 적용 (인스펙터 옵션)
        isRealityModeOn = startWithRealityMode;
        if (realityModeToggle != null)
        {
            realityModeToggle.SetIsOnWithoutNotify(isRealityModeOn);
        }
        // 카메라/배경/패스스루/모델 알파를 일관되게 적용 (true든 false든)
        OnRealityModeToggle(isRealityModeOn);
    }

    #region 1. 맞춤 설정

    /// <summary>
    /// 맞춤 설정 목표 위치 계산 (Gizmo/Editor 공용)
    /// </summary>
    public Vector3? CalculateTargetPosition()
    {
        if (customReferencePoint != null)
            return customReferencePoint.position;

        if (headsetTransform == null) return null;

        Vector3 headsetPosition = headsetTransform.position;
        Vector3 headsetForward = headsetTransform.forward;
        headsetForward.y = 0;
        headsetForward.Normalize();

        return new Vector3(
            headsetPosition.x + headsetForward.x * defaultForwardDistance,
            headsetPosition.y + defaultHeightOffset,
            headsetPosition.z + headsetForward.z * defaultForwardDistance
        );
    }

    /// <summary>
    /// 실제 이동 대상 Transform 반환 (부모가 있으면 부모)
    /// </summary>
    public Transform GetActualMoveTarget()
    {
        if (targetObject == null) return null;
        return targetObject.parent != null ? targetObject.parent : targetObject;
    }

    /// <summary>
    /// 맞춤 설정: 헤드셋 위치 기준으로 오브젝트 위치 초기화
    /// </summary>
    private void OnCustomPositioning()
    {
        ChunaLogger.Log("[PracticeSettings] 맞춤 설정 실행");

        if (targetObject == null)
        {
            ChunaLogger.LogWarning("[PracticeSettings] targetObject가 할당되지 않았습니다. 위치 초기화를 건너뜁니다.");
            return;
        }

        // ★ 누적 밀림 버그 수정:
        // 1) 위치설정이 켜져 있으면 끈다 — ReplacePos가 매 프레임 환자 그룹 위치를 덮어써서
        //    아래 복원/이동을 바로 무효화하는 것을 방지 (재배치는 곧 수동 조정 종료이기도 함)
        // 2) 위치설정이 변형해 둔 환자 그룹의 로컬 오프셋을 authored 값으로 되돌린다 →
        //    targetObject(루트)만 옮기면 첫 맞춤 설정과 동일하게 환자가 의도한 위치로 옴
        if (patientPositionController != null && patientPositionController.activeSelf)
        {
            ForceDisablePatientPositionController();
        }
        RestorePatientGroupLocalPose();

        Vector3? pos = CalculateTargetPosition();
        if (pos == null)
        {
            ChunaLogger.LogError("[PracticeSettings] 목표 위치를 계산할 수 없습니다!");
            return;
        }

        Vector3 newPosition = pos.Value;
        if (customReferencePoint != null)
            ChunaLogger.Log($"[PracticeSettings] 커스텀 기준점 사용: {newPosition}");
        else
            ChunaLogger.Log($"[PracticeSettings] 기본 기준점 사용 - 헤드셋 전방 {defaultForwardDistance}m, 높이 오프셋 {defaultHeightOffset}m = Y:{newPosition.y:F2}m");

        Transform actualTarget = GetActualMoveTarget();
        if (actualTarget == null) return;

        // 부모 pivot이 자식(targetObject)의 시각적 중심과 다르면 그대로 두 위치가 어긋남
        // → 자식이 newPosition에 오도록 부모를 보정 (부모 == 자식이면 offset=0이라 자동 안전)
        Vector3 pivotOffset = actualTarget.position - targetObject.position;
        actualTarget.position = newPosition + pivotOffset;
        ChunaLogger.Log($"[PracticeSettings] ✅ 위치 초기화 완료: {targetObject.name} → {newPosition} (부모 pivot 보정 {pivotOffset.magnitude:F3}m)");
    }

    /// <summary>
    /// 위치설정(ReplacePos)이 환자 그룹의 월드 위치를 직접 덮어써서 생긴 로컬 오프셋 드리프트를
    /// authored(시작 시 캐싱한) 값으로 되돌린다. 캐싱 실패 시 아무 것도 하지 않는다(기존 동작 유지).
    /// </summary>
    private void RestorePatientGroupLocalPose()
    {
        if (!patientGroupCaptured || patientGroupTransform == null) return;

        Vector3 drift = patientGroupTransform.localPosition - patientGroupAuthoredLocalPos;
        patientGroupTransform.localPosition = patientGroupAuthoredLocalPos;
        patientGroupTransform.localRotation = patientGroupAuthoredLocalRot;

        if (drift.sqrMagnitude > 0.0000001f)
            ChunaLogger.Log($"[PracticeSettings] 환자 그룹 드리프트 복원: {drift.magnitude:F3}m → authored 로컬 포즈");
    }
    #endregion

    #region 2. 환자 위치 조정
    /// <summary>
    /// 환자 위치 조정 컨트롤러 on/off
    /// </summary>
    private void OnPatientPositionToggle(bool isOn)
    {
        ChunaLogger.Log($"[PracticeSettings] 환자 위치 조정: {isOn}");

        if (patientPositionController != null)
        {
            // ★ 활성화 전에 현재 환자 위치로 컨트롤러 동기화 (위치 점프 방지)
            if (isOn)
            {
                SyncControllerToPatient();
            }

            patientPositionController.SetActive(isOn);
            ChunaLogger.Log($"[PracticeSettings] ✅ 환자 위치 조정 컨트롤러: {(isOn ? "활성화" : "비활성화")}");
        }
        else
        {
            ChunaLogger.LogWarning("[PracticeSettings] patientPositionController가 할당되지 않았습니다.");
        }
    }

    /// <summary>
    /// 환자 위치 조정 컨트롤러를 현재 환자 위치로 동기화
    /// </summary>
    private void SyncControllerToPatient()
    {
        if (patientPositionController == null) return;

        // 환자 모델 찾기
        GameObject patient = patientModel;
        if (patient == null)
        {
            patient = GameObject.FindWithTag("Patient");
        }

        if (patient != null)
        {
            // ★동기화 기준은 '구체가 실제로 끌고 갈 오브젝트'여야 한다.
            //   ReplacePos가 매 프레임 all을 구체 위치로 옮기므로, 구체를 다른 곳에 놓으면
            //   그 순간 all이 통째로 순간이동한다(환자·침대가 엉뚱한 곳으로 튐).
            //   예전에는 '환자의 부모'로 대신 짐작했는데, 계층이 바뀌면(환자 이동 홀더 삽입 등)
            //   짐작이 빗나가 정확히 그 사고가 난다.
            Transform syncTarget = null;
            ReplacePos mover = patientPositionController.GetComponent<ReplacePos>();
            if (mover != null && mover.all != null)
            {
                syncTarget = mover.all.transform;
                ChunaLogger.Log($"[PracticeSettings] 이동 대상 기준 동기화: {syncTarget.name}");
            }

            if (syncTarget == null)
            {
                // 폴백: 예전 동작(환자의 부모 → 환자 자신)
                syncTarget = patient.transform.parent != null ? patient.transform.parent : patient.transform;
                ChunaLogger.LogWarning($"[PracticeSettings] ReplacePos.all이 없어 부모 기준으로 폴백: {syncTarget.name}");
            }

            // ★ 위치만 동기화, 회전은 변경하지 않음 (환자 회전 문제 방지)
            patientPositionController.transform.position = syncTarget.position;
            // patientPositionController.transform.rotation = syncTarget.rotation; // 회전 동기화 비활성화

            ChunaLogger.Log($"[PracticeSettings] ✅ 컨트롤러를 환자 위치로 동기화 (위치만): {syncTarget.position}");
        }
        else
        {
            ChunaLogger.LogWarning("[PracticeSettings] 환자 모델을 찾을 수 없어 동기화 건너뜀");
        }
    }

    /// <summary>
    /// 환자 위치 조정 강제 off (QuickMenu 설정 토글 off 시 호출)
    /// </summary>
    public void ForceDisablePatientPositionController()
    {
        ChunaLogger.Log("[PracticeSettings] 환자 위치 조정 강제 비활성화");

        if (patientPositionToggle != null)
        {
            patientPositionToggle.isOn = false;
        }

        if (patientPositionController != null)
        {
            patientPositionController.SetActive(false);
        }
    }
    #endregion

    #region 3. 근골격계 표시
    /// <summary>
    /// 근골격계 모델 on/off
    /// </summary>
    private void OnSkeletonDisplayToggle(bool isOn)
    {
        ChunaLogger.Log($"[PracticeSettings] 근골격계 표시: {isOn}");

        if (skeletonModel != null)
        {
            skeletonModel.SetActive(isOn);
            ChunaLogger.Log($"[PracticeSettings] ✅ 근골격계 모델: {(isOn ? "표시" : "숨김")}");

            // 환자 모델이 표시되어 있을 때만 투명도 조정
            bool isPatientModelVisible = patientModelDisplayToggle != null && patientModelDisplayToggle.isOn;

            if (isPatientModelVisible)
            {
                // 골격 표시 시 환자 모델 반투명 처리
                if (isOn)
                {
                    SetModelTransparency(patientRenderers, skeletonModeAlpha, "환자 모델");
                    SetMeshTransparency(patientMeshRenderers, skeletonModeAlpha, "환자 모델");
                }
                else
                {
                    // 골격 숨김 시 환자 모델을 일반 상태로 복원 (현실 모드가 아니면)
                    if (!isRealityModeOn)
                    {
                        SetModelTransparency(patientRenderers, normalAlpha, "환자 모델");
                        SetMeshTransparency(patientMeshRenderers, normalAlpha, "환자 모델");
                    }
                }
            }
        }
        else
        {
            ChunaLogger.LogWarning("[PracticeSettings] skeletonModel이 할당되지 않았습니다.");
        }
    }
    #endregion

    #region 4. 환자 모델 표시
    /// <summary>
    /// 환자 모델 on/off (알파값 기반)
    /// </summary>
    private void OnPatientModelDisplayToggle(bool isOn)
    {
        ChunaLogger.Log($"[PracticeSettings] 환자 모델 표시: {isOn}");

        if (patientModel != null)
        {
            if (isOn)
            {
                // 환자 모델 표시: 현재 상태에 따른 적절한 알파값 적용
                float targetAlpha = normalAlpha;

                // 현실 모드가 켜져있으면 현실 모드 알파값 우선
                if (isRealityModeOn)
                {
                    targetAlpha = realityModeAlpha;
                }
                // 골격이 켜져있으면 골격 모드 알파값
                else if (skeletonModel != null && skeletonModel.activeSelf)
                {
                    targetAlpha = skeletonModeAlpha;
                }

                SetModelTransparency(patientRenderers, targetAlpha, "환자 모델");
                SetMeshTransparency(patientMeshRenderers, targetAlpha, "환자 모델");
                ChunaLogger.Log($"[PracticeSettings] ✅ 환자 모델 표시 (Alpha: {targetAlpha})");
            }
            else
            {
                // 환자 모델 숨김: 알파값 0으로 완전 투명
                SetModelTransparency(patientRenderers, 0f, "환자 모델");
                SetMeshTransparency(patientMeshRenderers, 0f, "환자 모델");
                ChunaLogger.Log($"[PracticeSettings] ✅ 환자 모델 숨김 (Alpha: 0)");
            }
        }
        else
        {
            ChunaLogger.LogWarning("[PracticeSettings] patientModel이 할당되지 않았습니다.");
        }
    }
    #endregion

    #region 5. 현실 모드 (패스쓰루)
    /// <summary>
    /// 현실 모드(패스쓰루)를 코드에서 켜고 끈다. 토글 UI 상태도 같이 맞춘다.
    /// 실측 모드 진입 시 InfoPanelController가 호출한다 — 사용자가 설정에서 따로 켜지 않아도 되게.
    /// </summary>
    public void SetRealityMode(bool isOn)
    {
        if (realityModeToggle != null)
            realityModeToggle.SetIsOnWithoutNotify(isOn);

        OnRealityModeToggle(isOn);
    }

    /// <summary>
    /// 패스쓰루 on/off
    /// </summary>
    private void OnRealityModeToggle(bool isOn)
    {
        ChunaLogger.Log($"[PracticeSettings] 현실 모드: {isOn}");

        isRealityModeOn = isOn;

        if (passthroughLayer != null)
        {
            passthroughLayer.hidden = !isOn;
            passthroughLayer.enabled = isOn;
            ChunaLogger.Log($"[PracticeSettings] ✅ 패스쓰루: {(isOn ? "활성화" : "비활성화")}");
        }
        else
        {
            ChunaLogger.LogError("[PracticeSettings] passthroughLayer가 null입니다!");
        }

        // 배경 오브젝트 토글
        if (backgroundObject != null)
        {
            backgroundObject.SetActive(!isOn);
            ChunaLogger.Log($"[PracticeSettings] 배경 오브젝트: {(!isOn ? "표시" : "숨김")}");
        }

        // 카메라 설정
        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            if (isOn)
            {
                mainCamera.backgroundColor = new Color(0, 0, 0, 0);
                mainCamera.clearFlags = CameraClearFlags.SolidColor;
            }
            else
            {
                mainCamera.backgroundColor = new Color(0, 0, 0, 1);
                mainCamera.clearFlags = CameraClearFlags.Skybox;
            }
            ChunaLogger.Log($"[PracticeSettings] 카메라 설정 업데이트");
        }

        // 현실 모드 시 모델 투명도 조정
        // 환자 모델이 표시되어 있을 때만 투명도 조정
        bool isPatientModelVisible = patientModelDisplayToggle != null && patientModelDisplayToggle.isOn;

        if (isOn)
        {
            // 환자 모델을 반투명하게
            if (isPatientModelVisible)
            {
                SetModelTransparency(patientRenderers, realityModeAlpha, "환자 모델 (현실 모드)");
                SetMeshTransparency(patientMeshRenderers, realityModeAlpha, "환자 모델 (현실 모드)");
            }

            // 골격 모델도 표시되어 있다면 반투명하게
            if (skeletonModel != null && skeletonModel.activeSelf)
            {
                SetModelTransparency(skeletonRenderers, realityModeAlpha, "골격 모델 (현실 모드)");
                SetMeshTransparency(skeletonMeshRenderers, realityModeAlpha, "골격 모델 (현실 모드)");
            }
        }
        else
        {
            // 현실 모드 해제 시
            if (isPatientModelVisible)
            {
                // 골격이 표시되어 있으면 골격 모드 알파값으로, 아니면 일반 알파값으로
                bool skeletonIsOn = skeletonModel != null && skeletonModel.activeSelf;
                float targetAlpha = skeletonIsOn ? skeletonModeAlpha : normalAlpha;
                SetModelTransparency(patientRenderers, targetAlpha, "환자 모델 (현실 모드 해제)");
                SetMeshTransparency(patientMeshRenderers, targetAlpha, "환자 모델 (현실 모드 해제)");
            }

            // 골격 모델은 일반 상태로 복원
            if (skeletonModel != null && skeletonModel.activeSelf)
            {
                SetModelTransparency(skeletonRenderers, normalAlpha, "골격 모델 (현실 모드 해제)");
                SetMeshTransparency(skeletonMeshRenderers, normalAlpha, "골격 모델 (현실 모드 해제)");
            }
        }
    }
    #endregion

    #region 투명도 제어
    /// <summary>
    /// 모델의 투명도를 조정하는 메서드
    /// </summary>
    /// <param name="renderers">대상 SkinnedMeshRenderer 배열</param>
    /// <param name="targetAlpha">목표 알파값 (0~1)</param>
    /// <param name="modelName">로그용 모델 이름</param>
    /// <summary>이 렌더러를 투명도 처리에서 제외할지(눈·각막·치아 등 특수 셰이더 보호).</summary>
    private bool IsTransparencyExcluded(Renderer renderer)
    {
        if (renderer == null) return false;

        // ★골격은 반투명 대상이 아니다 — 뼈를 부위별로 나누고 색칠한 이유가 '잘 보이게'이기 때문이다.
        //   여기서 알파를 먹이면 _Color.a가 부위색을 씻어내고 ZWrite까지 꺼져 뼈 안쪽이 비쳐 보인다.
        //   CranialHeadXray가 쓰는 것과 같은 판정(조상 중에 skeletal_system이 있는가).
        if (keepSkeletonOpaque)
            for (Transform t = renderer.transform; t != null; t = t.parent)
                if (t.name.IndexOf("skeletal_system", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

        // ★훈련 표시물은 환자 투명도 조절 대상이 아니다 (2026-08-18).
        //   patientRoot.GetComponentsInChildren<MeshRenderer>로 렌더러를 모으는데(:173),
        //   파지점 구체·접촉 표시구·화살표는 전부 환자 본(CC_Base_*) 아래에 붙어 있어 그 배열에 딸려 들어온다.
        //   알파가 1이면 아래 else 분기가 _Mode 0 · ZWrite 1 · _ALPHABLEND_ON 해제 · renderQueue -1로 되돌려
        //   <b>표시물이 전부 완전 불투명</b>해졌다(사용자 지적: "제2늑골만이 아니라 흉추 신전 무릎·팔까지 다").
        //   CranialHeadXray가 이미 같은 이유로 리그 하위와 파지점을 제외하고 있는데 여기만 빠져 있었다.
        if (renderer.GetComponentInParent<CranialAdjustmentController>(true) != null) return true;  // 리그 하위 표시물 일체
        if (renderer.GetComponentInParent<GripPointTarget>(true) != null) return true;              // 리그 밖에 둔 파지점
        if (renderer.GetComponentInParent<ForceArrowBase>(true) != null) return true;               // 힘의 방향 화살표
        if (renderer.GetComponentInParent<TargetAreaHighlight>(true) != null) return true;          // 타겟 부위 하이라이트
        if (renderer.gameObject.name.StartsWith("ContactTarget_", System.StringComparison.Ordinal))
            return true;                                                                            // 접촉 터치 표시구

        if (transparencyExcludeNames == null) return false;
        string n = renderer.gameObject.name;
        foreach (var t in transparencyExcludeNames)
            if (!string.IsNullOrEmpty(t) && n.IndexOf(t, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }

    private void SetModelTransparency(SkinnedMeshRenderer[] renderers, float targetAlpha, string modelName)
    {
        if (renderers == null || renderers.Length == 0)
        {
            ChunaLogger.LogWarning($"[PracticeSettings] {modelName} 렌더러가 없어 투명도 조정을 건너뜁니다.");
            return;
        }

        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;
            if (IsTransparencyExcluded(renderer)) continue;   // 눈·각막·치아 등 특수 셰이더는 건드리지 않음

            Material[] materials = renderer.materials;

            for (int i = 0; i < materials.Length; i++)
            {
                Material mat = materials[i];
                if (mat == null || mat.shader.name == "Hidden/InternalErrorShader") continue;

                // 투명도가 1 미만이면 Transparent 모드로 변경
                if (targetAlpha < 1f)
                {
                    // Rendering Mode를 Transparent로 설정 (Standard Shader)
                    if (mat.HasProperty("_Mode"))
                    {
                        mat.SetFloat("_Mode", 3); // 3 = Transparent
                    }

                    // 블렌드 모드 설정
                    if (mat.HasProperty("_SrcBlend"))
                        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    if (mat.HasProperty("_DstBlend"))
                        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    if (mat.HasProperty("_ZWrite"))
                        mat.SetInt("_ZWrite", 0);

                    // 키워드 설정
                    mat.DisableKeyword("_ALPHATEST_ON");
                    mat.EnableKeyword("_ALPHABLEND_ON");
                    mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");

                    // 렌더 큐 설정
                    mat.renderQueue = 3000;
                }
                else
                {
                    // Rendering Mode를 Opaque로 복원 (Standard Shader)
                    if (mat.HasProperty("_Mode"))
                    {
                        mat.SetFloat("_Mode", 0); // 0 = Opaque
                    }

                    // 블렌드 모드 복원
                    if (mat.HasProperty("_SrcBlend"))
                        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    if (mat.HasProperty("_DstBlend"))
                        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                    if (mat.HasProperty("_ZWrite"))
                        mat.SetInt("_ZWrite", 1);

                    // 키워드 복원
                    mat.DisableKeyword("_ALPHATEST_ON");
                    mat.DisableKeyword("_ALPHABLEND_ON");
                    mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");

                    // 렌더 큐 복원
                    mat.renderQueue = -1;
                }

                // 알파값 적용 - 다양한 셰이더의 컬러/알파 프로퍼티 지원
                // 1. _Color (Standard Shader, Unity Built-in)
                if (mat.HasProperty("_Color"))
                {
                    Color mainColor = mat.GetColor("_Color");
                    mainColor.a = targetAlpha;
                    mat.SetColor("_Color", mainColor);
                }

                // 2. _BaseColor (URP/HDRP Lit Shader)
                if (mat.HasProperty("_BaseColor"))
                {
                    Color baseColor = mat.GetColor("_BaseColor");
                    baseColor.a = targetAlpha;
                    mat.SetColor("_BaseColor", baseColor);
                }

                // 3. _MainColor (일부 커스텀 셰이더)
                if (mat.HasProperty("_MainColor"))
                {
                    Color mainColor = mat.GetColor("_MainColor");
                    mainColor.a = targetAlpha;
                    mat.SetColor("_MainColor", mainColor);
                }

                // 4. _DiffuseColor (Reallusion 셰이더)
                if (mat.HasProperty("_DiffuseColor"))
                {
                    Color diffuseColor = mat.GetColor("_DiffuseColor");
                    diffuseColor.a = targetAlpha;
                    mat.SetColor("_DiffuseColor", diffuseColor);
                }

                // 5. _Opacity (Reallusion 셰이더 - float 타입)
                if (mat.HasProperty("_Opacity"))
                {
                    mat.SetFloat("_Opacity", targetAlpha);
                }

                // 6. _Alpha (일부 커스텀 셰이더 - float 타입)
                if (mat.HasProperty("_Alpha"))
                {
                    mat.SetFloat("_Alpha", targetAlpha);
                }

                // 7. _AlphaClip (알파 클리핑 임계값 - 0으로 설정하여 투명도 활성화)
                if (mat.HasProperty("_AlphaClip") && targetAlpha < 1f)
                {
                    mat.SetFloat("_AlphaClip", 0f);
                }

                // 알파 미지원 셰이더(눈, 치아, 각막 등)는 무시
            }

            renderer.materials = materials;
        }

        ChunaLogger.Log($"[PracticeSettings] ✅ {modelName} 투명도 조정 완료: Alpha = {targetAlpha}");
    }

    /// <summary>
    /// 모델의 투명도를 조정하는 메서드 (MeshRenderer 오버로드)
    /// </summary>
    /// <param name="renderers">대상 MeshRenderer 배열</param>
    /// <param name="targetAlpha">목표 알파값 (0~1)</param>
    /// <param name="modelName">로그용 모델 이름</param>
    private void SetMeshTransparency(MeshRenderer[] renderers, float targetAlpha, string modelName)
    {
        if (renderers == null || renderers.Length == 0)
        {
            return; // MeshRenderer가 없어도 경고 없이 진행
        }

        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;
            if (IsTransparencyExcluded(renderer)) continue;   // 눈·각막·치아 등 특수 셰이더는 건드리지 않음

            Material[] materials = renderer.materials;

            for (int i = 0; i < materials.Length; i++)
            {
                Material mat = materials[i];
                if (mat == null || mat.shader.name == "Hidden/InternalErrorShader") continue;

                // 투명도가 1 미만이면 Transparent 모드로 변경
                if (targetAlpha < 1f)
                {
                    // Rendering Mode를 Transparent로 설정 (Standard Shader)
                    if (mat.HasProperty("_Mode"))
                    {
                        mat.SetFloat("_Mode", 3); // 3 = Transparent
                    }

                    // 블렌드 모드 설정
                    if (mat.HasProperty("_SrcBlend"))
                        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    if (mat.HasProperty("_DstBlend"))
                        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    if (mat.HasProperty("_ZWrite"))
                        mat.SetInt("_ZWrite", 0);

                    // 키워드 설정
                    mat.DisableKeyword("_ALPHATEST_ON");
                    mat.EnableKeyword("_ALPHABLEND_ON");
                    mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");

                    // 렌더 큐 설정
                    mat.renderQueue = 3000;
                }
                else
                {
                    // Rendering Mode를 Opaque로 복원 (Standard Shader)
                    if (mat.HasProperty("_Mode"))
                    {
                        mat.SetFloat("_Mode", 0); // 0 = Opaque
                    }

                    // 블렌드 모드 복원
                    if (mat.HasProperty("_SrcBlend"))
                        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    if (mat.HasProperty("_DstBlend"))
                        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                    if (mat.HasProperty("_ZWrite"))
                        mat.SetInt("_ZWrite", 1);

                    // 키워드 복원
                    mat.DisableKeyword("_ALPHATEST_ON");
                    mat.DisableKeyword("_ALPHABLEND_ON");
                    mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");

                    // 렌더 큐 복원
                    mat.renderQueue = -1;
                }

                // 알파값 적용 - 다양한 셰이더의 컬러/알파 프로퍼티 지원
                // 1. _Color (Standard Shader, Unity Built-in)
                if (mat.HasProperty("_Color"))
                {
                    Color mainColor = mat.GetColor("_Color");
                    mainColor.a = targetAlpha;
                    mat.SetColor("_Color", mainColor);
                }

                // 2. _BaseColor (URP/HDRP Lit Shader)
                if (mat.HasProperty("_BaseColor"))
                {
                    Color baseColor = mat.GetColor("_BaseColor");
                    baseColor.a = targetAlpha;
                    mat.SetColor("_BaseColor", baseColor);
                }

                // 3. _MainColor (일부 커스텀 셰이더)
                if (mat.HasProperty("_MainColor"))
                {
                    Color mainColor = mat.GetColor("_MainColor");
                    mainColor.a = targetAlpha;
                    mat.SetColor("_MainColor", mainColor);
                }

                // 4. _DiffuseColor (Reallusion 셰이더)
                if (mat.HasProperty("_DiffuseColor"))
                {
                    Color diffuseColor = mat.GetColor("_DiffuseColor");
                    diffuseColor.a = targetAlpha;
                    mat.SetColor("_DiffuseColor", diffuseColor);
                }

                // 5. _Opacity (Reallusion 셰이더 - float 타입)
                if (mat.HasProperty("_Opacity"))
                {
                    mat.SetFloat("_Opacity", targetAlpha);
                }

                // 6. _Alpha (일부 커스텀 셰이더 - float 타입)
                if (mat.HasProperty("_Alpha"))
                {
                    mat.SetFloat("_Alpha", targetAlpha);
                }

                // 7. _AlphaClip (알파 클리핑 임계값 - 0으로 설정하여 투명도 활성화)
                if (mat.HasProperty("_AlphaClip") && targetAlpha < 1f)
                {
                    mat.SetFloat("_AlphaClip", 0f);
                }
            }

            renderer.materials = materials;
        }

        ChunaLogger.Log($"[PracticeSettings] ✅ {modelName} MeshRenderer 투명도 조정 완료: Alpha = {targetAlpha}");
    }
    #endregion

    #region 유틸리티
    /// <summary>
    /// 토글을 버튼처럼 사용하기 위한 리셋 코루틴
    /// </summary>
    private IEnumerator ResetToggle(Toggle toggle)
    {
        yield return null;
        if (toggle != null)
            toggle.isOn = false;
    }
    #endregion

    void OnDestroy()
    {
        // 리스너 정리
        if (customPositioningToggle != null) customPositioningToggle.onValueChanged.RemoveAllListeners();
        if (patientPositionToggle != null) patientPositionToggle.onValueChanged.RemoveAllListeners();
        if (skeletonDisplayToggle != null) skeletonDisplayToggle.onValueChanged.RemoveAllListeners();
        if (patientModelDisplayToggle != null) patientModelDisplayToggle.onValueChanged.RemoveAllListeners();
        if (realityModeToggle != null) realityModeToggle.onValueChanged.RemoveAllListeners();
    }

#if UNITY_EDITOR
    /// <summary>
    /// Scene뷰에서 맞춤 설정 목표 위치를 Gizmo로 시각화
    /// - 커스텀 기준점이 있으면: 기준점 위치 (마젠타 구체)
    /// - 없으면: 헤드셋 전방 + 높이 오프셋 위치 (노란 구체 + 초록 선)
    /// - 실제 이동될 대상 오브젝트도 표시 (빨간 연결선)
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (!showPositioningGizmo) return;

        Vector3? pos = CalculateTargetPosition();
        if (pos == null) return;
        Vector3 targetPosition = pos.Value;

        if (customReferencePoint != null)
        {
            // 커스텀 기준점 모드
            Gizmos.color = new Color(1f, 0.3f, 1f, 0.8f);
            Gizmos.DrawWireSphere(targetPosition, 0.12f);
            Gizmos.DrawSphere(targetPosition, 0.03f);

            UnityEditor.Handles.Label(targetPosition + Vector3.up * 0.2f,
                "맞춤 기준점 (커스텀)", new GUIStyle
                { normal = { textColor = Color.magenta }, fontSize = 11, fontStyle = FontStyle.Bold });
        }
        else if (headsetTransform != null)
        {
            Vector3 headsetPos = headsetTransform.position;

            // 헤드셋 위치 (하늘색)
            Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.5f);
            Gizmos.DrawWireSphere(headsetPos, 0.06f);

            // 헤드셋 → 목표 위치 (초록)
            Gizmos.color = new Color(0.3f, 1f, 0.3f, 0.6f);
            Gizmos.DrawLine(headsetPos, targetPosition);

            // 목표 위치 (노란 구체)
            Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.8f);
            Gizmos.DrawWireSphere(targetPosition, 0.12f);
            Gizmos.DrawSphere(targetPosition, 0.03f);

            // 높이 기준선 (수직)
            Vector3 headsetHeightAtTarget = new Vector3(targetPosition.x, headsetPos.y, targetPosition.z);
            Gizmos.color = new Color(1f, 1f, 1f, 0.3f);
            Gizmos.DrawLine(headsetHeightAtTarget, targetPosition);

            // 전방 거리 (수평)
            Gizmos.color = new Color(0.3f, 1f, 0.3f, 0.3f);
            Gizmos.DrawLine(headsetPos, headsetHeightAtTarget);

            UnityEditor.Handles.Label(targetPosition + Vector3.up * 0.2f,
                $"맞춤 목표 위치\n전방 {defaultForwardDistance}m / 높이 오프셋 {defaultHeightOffset}m",
                new GUIStyle
                { normal = { textColor = Color.yellow }, fontSize = 11, fontStyle = FontStyle.Bold });
        }

        // 실제 보이는 위치(자식 targetObject) 표시 — 헤드셋 목표 포인트와 어긋나면 pivot 보정이 작동해서 일치하게 됨
        if (targetObject != null)
        {
            Gizmos.color = new Color(1f, 0.4f, 0.4f, 0.5f);
            Gizmos.DrawLine(targetObject.position, targetPosition);
            Gizmos.DrawWireSphere(targetObject.position, 0.08f);

            UnityEditor.Handles.Label(targetObject.position + Vector3.up * 0.15f,
                $"현재 위치(자식): {targetObject.name}", new GUIStyle
                { normal = { textColor = new Color(1f, 0.6f, 0.6f) }, fontSize = 10 });

            // 부모 pivot이 다르면 시각적으로 보이게 작은 보라색 점으로 표시 (디버깅 보조)
            Transform actualTarget = GetActualMoveTarget();
            if (actualTarget != null && actualTarget != targetObject)
            {
                Gizmos.color = new Color(0.7f, 0.3f, 1f, 0.4f);
                Gizmos.DrawWireSphere(actualTarget.position, 0.04f);
                Gizmos.DrawLine(actualTarget.position, targetObject.position);
            }

            // 자식 메시(환자/침대 등) Bounds — 현재 위치(회색) + 이동 후 예상 위치(초록) 와이어큐브
            if (TryGetTargetVisualBounds(out Bounds currentBounds))
            {
                // 현재 자식들이 실제로 그려지는 영역
                Gizmos.color = new Color(0.7f, 0.7f, 0.7f, 0.6f);
                Gizmos.DrawWireCube(currentBounds.center, currentBounds.size);

                UnityEditor.Handles.Label(currentBounds.center + Vector3.down * (currentBounds.extents.y + 0.05f),
                    "환자/침대 현재 위치", new GUIStyle
                    { normal = { textColor = new Color(0.85f, 0.85f, 0.85f) }, fontSize = 11, fontStyle = FontStyle.Bold });

                // 이동 후 예상 위치 — pivot 보정 후 부모가 가는 곳을 기준으로 자식 Bounds를 평행이동
                if (actualTarget != null)
                {
                    Vector3 pivotOffset = actualTarget.position - targetObject.position;
                    Vector3 newParentPos = targetPosition + pivotOffset;
                    Vector3 deltaMove = newParentPos - actualTarget.position;

                    if (deltaMove.sqrMagnitude > 0.0001f)
                    {
                        Vector3 predictedCenter = currentBounds.center + deltaMove;
                        Gizmos.color = new Color(0.3f, 1f, 0.3f, 0.8f);
                        Gizmos.DrawWireCube(predictedCenter, currentBounds.size);

                        UnityEditor.Handles.Label(predictedCenter + Vector3.up * (currentBounds.extents.y + 0.05f),
                            "이동 후 예상 위치", new GUIStyle
                            { normal = { textColor = new Color(0.5f, 1f, 0.5f) }, fontSize = 11, fontStyle = FontStyle.Bold });
                    }
                }
            }
        }
    }

    /// <summary>
    /// targetObject 하위 모든 활성 Renderer의 결합 Bounds (Gizmo용)
    /// — 환자 모델, 침대 등 실제로 화면에 보이는 자식들이 차지하는 영역
    /// </summary>
    private bool TryGetTargetVisualBounds(out Bounds bounds)
    {
        bounds = new Bounds();
        if (targetObject == null) return false;

        Renderer[] renderers = targetObject.GetComponentsInChildren<Renderer>(false);
        if (renderers == null || renderers.Length == 0) return false;

        bool initialized = false;
        foreach (var r in renderers)
        {
            if (r == null || !r.enabled) continue;
            if (!initialized) { bounds = r.bounds; initialized = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return initialized;
    }
#endif
}
