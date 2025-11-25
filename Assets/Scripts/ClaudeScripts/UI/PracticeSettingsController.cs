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
    [SerializeField] private float defaultHeight = 0f;            // 기본 높이

    [Header("═══ 환자 위치 조정 ═══")]
    [SerializeField] private GameObject patientPositionController; // 환자 위치 조정 컨트롤러

    [Header("═══ 모델 표시 ═══")]
    [SerializeField] private GameObject skeletonModel;            // 근골격계 모델
    [SerializeField] private GameObject patientModel;             // 환자 모델

    [Header("═══ 투명도 설정 ═══")]
    [SerializeField][Range(0f, 1f)] private float skeletonModeAlpha = 0.3f;  // 골격 표시 시 환자 모델 알파값
    [SerializeField][Range(0f, 1f)] private float realityModeAlpha = 0.5f;   // 현실 모드 시 모델 알파값
    [SerializeField][Range(0f, 1f)] private float normalAlpha = 1f;          // 일반 상태 알파값

    [Header("═══ 현실 모드 (패스쓰루) ═══")]
    [SerializeField] private GameObject backgroundObject;         // 배경 오브젝트

    // 패스쓰루 레이어 (런타임에 찾음)
    private OVRPassthroughLayer passthroughLayer;
    private bool isRealityModeOn = false;

    // SkinnedMeshRenderer 캐싱
    private SkinnedMeshRenderer[] patientRenderers;
    private SkinnedMeshRenderer[] skeletonRenderers;

    // 원본 머티리얼 저장 (투명도 복원용)
    private Material[][] originalPatientMaterials;
    private Material[][] originalSkeletonMaterials;

    // QuickMenuController 참조 (설정 토글 off 감지용)
    private QuickMenuController quickMenuController;

    void Awake()
    {
        // QuickMenuController 찾기
        quickMenuController = FindObjectOfType<QuickMenuController>();

        // 헤드셋 Transform 자동 찾기 (할당 안 되어 있으면)
        if (headsetTransform == null)
        {
            GameObject ovrCameraRig = GameObject.Find("OVRCameraRig");
            if (ovrCameraRig != null)
            {
                headsetTransform = ovrCameraRig.transform.Find("TrackingSpace/CenterEyeAnchor");
                if (headsetTransform != null)
                {
                    Debug.Log("[PracticeSettings] ✅ CenterEyeAnchor 자동 찾기 성공");
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
    }

    /// <summary>
    /// SkinnedMeshRenderer 캐싱 및 원본 머티리얼 저장
    /// </summary>
    void CacheRenderers()
    {
        // 환자 모델 렌더러 캐싱
        if (patientModel != null)
        {
            patientRenderers = patientModel.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (patientRenderers.Length > 0)
            {
                // 원본 머티리얼 저장
                originalPatientMaterials = new Material[patientRenderers.Length][];
                for (int i = 0; i < patientRenderers.Length; i++)
                {
                    originalPatientMaterials[i] = patientRenderers[i].materials;
                }
                Debug.Log($"[PracticeSettings] ✅ 환자 모델 렌더러 {patientRenderers.Length}개 캐싱 완료");
            }
        }

        // 골격 모델 렌더러 캐싱
        if (skeletonModel != null)
        {
            skeletonRenderers = skeletonModel.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skeletonRenderers.Length > 0)
            {
                // 원본 머티리얼 저장
                originalSkeletonMaterials = new Material[skeletonRenderers.Length][];
                for (int i = 0; i < skeletonRenderers.Length; i++)
                {
                    originalSkeletonMaterials[i] = skeletonRenderers[i].materials;
                }
                Debug.Log($"[PracticeSettings] ✅ 골격 모델 렌더러 {skeletonRenderers.Length}개 캐싱 완료");
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
                Debug.Log("[PracticeSettings] OVRPassthroughLayer 컴포넌트 추가됨");
            }
        }
        else
        {
            Debug.LogError("[PracticeSettings] OVRCameraRig를 찾을 수 없습니다!");
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
            Debug.Log("[PracticeSettings] ✅ 맞춤 설정 토글 연결");
        }

        // 2. 환자 위치 조정 (일반 토글)
        if (patientPositionToggle != null)
        {
            patientPositionToggle.onValueChanged.RemoveAllListeners();
            patientPositionToggle.onValueChanged.AddListener(OnPatientPositionToggle);
            Debug.Log("[PracticeSettings] ✅ 환자 위치 조정 토글 연결");
        }

        // 3. 근골격계 표시 (일반 토글)
        if (skeletonDisplayToggle != null)
        {
            skeletonDisplayToggle.onValueChanged.RemoveAllListeners();
            skeletonDisplayToggle.onValueChanged.AddListener(OnSkeletonDisplayToggle);
            Debug.Log("[PracticeSettings] ✅ 근골격계 표시 토글 연결");
        }

        // 4. 환자 모델 표시 (일반 토글)
        if (patientModelDisplayToggle != null)
        {
            patientModelDisplayToggle.onValueChanged.RemoveAllListeners();
            patientModelDisplayToggle.onValueChanged.AddListener(OnPatientModelDisplayToggle);
            Debug.Log("[PracticeSettings] ✅ 환자 모델 표시 토글 연결");
        }

        // 5. 현실 모드 (일반 토글)
        if (realityModeToggle != null)
        {
            realityModeToggle.onValueChanged.RemoveAllListeners();
            realityModeToggle.onValueChanged.AddListener(OnRealityModeToggle);
            Debug.Log("[PracticeSettings] ✅ 현실 모드 토글 연결");
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

        if (skeletonDisplayToggle != null && skeletonModel != null)
            skeletonDisplayToggle.isOn = skeletonModel.activeSelf;

        if (patientModelDisplayToggle != null && patientModel != null)
            patientModelDisplayToggle.isOn = patientModel.activeSelf;

        if (realityModeToggle != null)
            realityModeToggle.isOn = isRealityModeOn;
    }

    #region 1. 맞춤 설정
    /// <summary>
    /// 맞춤 설정: 헤드셋 위치 기준으로 오브젝트 위치 초기화
    /// </summary>
    private void OnCustomPositioning()
    {
        Debug.Log("[PracticeSettings] 맞춤 설정 실행");

        if (targetObject == null)
        {
            Debug.LogWarning("[PracticeSettings] targetObject가 할당되지 않았습니다. 위치 초기화를 건너뜁니다.");
            return;
        }

        if (headsetTransform == null)
        {
            Debug.LogError("[PracticeSettings] headsetTransform이 null입니다! CenterEyeAnchor를 찾을 수 없습니다.");
            return;
        }

        Vector3 newPosition;

        // 커스텀 기준점이 있으면 사용
        if (customReferencePoint != null)
        {
            newPosition = customReferencePoint.position;
            Debug.Log($"[PracticeSettings] 커스텀 기준점 사용: {newPosition}");
        }
        else
        {
            // 기본: 헤드셋 전방 0.5m, 높이 0
            Vector3 headsetPosition = headsetTransform.position;
            Vector3 headsetForward = headsetTransform.forward;

            // Y축은 0으로, 전방 방향만 사용
            headsetForward.y = 0;
            headsetForward.Normalize();

            newPosition = new Vector3(
                headsetPosition.x + headsetForward.x * defaultForwardDistance,
                defaultHeight,
                headsetPosition.z + headsetForward.z * defaultForwardDistance
            );

            Debug.Log($"[PracticeSettings] 기본 기준점 사용 - 헤드셋 전방 {defaultForwardDistance}m, 높이 {defaultHeight}");
        }

        targetObject.position = newPosition;
        Debug.Log($"[PracticeSettings] ✅ 오브젝트 위치 초기화 완료: {targetObject.name} -> {newPosition}");
    }
    #endregion

    #region 2. 환자 위치 조정
    /// <summary>
    /// 환자 위치 조정 컨트롤러 on/off
    /// </summary>
    private void OnPatientPositionToggle(bool isOn)
    {
        Debug.Log($"[PracticeSettings] 환자 위치 조정: {isOn}");

        if (patientPositionController != null)
        {
            patientPositionController.SetActive(isOn);
            Debug.Log($"[PracticeSettings] ✅ 환자 위치 조정 컨트롤러: {(isOn ? "활성화" : "비활성화")}");
        }
        else
        {
            Debug.LogWarning("[PracticeSettings] patientPositionController가 할당되지 않았습니다.");
        }
    }

    /// <summary>
    /// 환자 위치 조정 강제 off (QuickMenu 설정 토글 off 시 호출)
    /// </summary>
    public void ForceDisablePatientPositionController()
    {
        Debug.Log("[PracticeSettings] 환자 위치 조정 강제 비활성화");

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
        Debug.Log($"[PracticeSettings] 근골격계 표시: {isOn}");

        if (skeletonModel != null)
        {
            skeletonModel.SetActive(isOn);
            Debug.Log($"[PracticeSettings] ✅ 근골격계 모델: {(isOn ? "표시" : "숨김")}");

            // 골격 표시 시 환자 모델 반투명 처리
            if (isOn)
            {
                SetModelTransparency(patientRenderers, skeletonModeAlpha, "환자 모델");
            }
            else
            {
                // 골격 숨김 시 환자 모델을 일반 상태로 복원 (현실 모드가 아니면)
                if (!isRealityModeOn)
                {
                    SetModelTransparency(patientRenderers, normalAlpha, "환자 모델");
                }
            }
        }
        else
        {
            Debug.LogWarning("[PracticeSettings] skeletonModel이 할당되지 않았습니다.");
        }
    }
    #endregion

    #region 4. 환자 모델 표시
    /// <summary>
    /// 환자 모델 on/off
    /// </summary>
    private void OnPatientModelDisplayToggle(bool isOn)
    {
        Debug.Log($"[PracticeSettings] 환자 모델 표시: {isOn}");

        if (patientModel != null)
        {
            patientModel.SetActive(isOn);
            Debug.Log($"[PracticeSettings] ✅ 환자 모델: {(isOn ? "표시" : "숨김")}");
        }
        else
        {
            Debug.LogWarning("[PracticeSettings] patientModel이 할당되지 않았습니다.");
        }
    }
    #endregion

    #region 5. 현실 모드 (패스쓰루)
    /// <summary>
    /// 패스쓰루 on/off
    /// </summary>
    private void OnRealityModeToggle(bool isOn)
    {
        Debug.Log($"[PracticeSettings] 현실 모드: {isOn}");

        isRealityModeOn = isOn;

        if (passthroughLayer != null)
        {
            passthroughLayer.hidden = !isOn;
            passthroughLayer.enabled = isOn;
            Debug.Log($"[PracticeSettings] ✅ 패스쓰루: {(isOn ? "활성화" : "비활성화")}");
        }
        else
        {
            Debug.LogError("[PracticeSettings] passthroughLayer가 null입니다!");
        }

        // 배경 오브젝트 토글
        if (backgroundObject != null)
        {
            backgroundObject.SetActive(!isOn);
            Debug.Log($"[PracticeSettings] 배경 오브젝트: {(!isOn ? "표시" : "숨김")}");
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
            Debug.Log($"[PracticeSettings] 카메라 설정 업데이트");
        }

        // 현실 모드 시 모델 투명도 조정
        if (isOn)
        {
            // 환자 모델을 반투명하게
            SetModelTransparency(patientRenderers, realityModeAlpha, "환자 모델 (현실 모드)");

            // 골격 모델도 표시되어 있다면 반투명하게
            if (skeletonModel != null && skeletonModel.activeSelf)
            {
                SetModelTransparency(skeletonRenderers, realityModeAlpha, "골격 모델 (현실 모드)");
            }
        }
        else
        {
            // 현실 모드 해제 시
            // 골격이 표시되어 있으면 골격 모드 알파값으로, 아니면 일반 알파값으로
            bool skeletonIsOn = skeletonModel != null && skeletonModel.activeSelf;
            float targetAlpha = skeletonIsOn ? skeletonModeAlpha : normalAlpha;
            SetModelTransparency(patientRenderers, targetAlpha, "환자 모델 (현실 모드 해제)");

            // 골격 모델은 일반 상태로 복원
            if (skeletonIsOn)
            {
                SetModelTransparency(skeletonRenderers, normalAlpha, "골격 모델 (현실 모드 해제)");
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
    private void SetModelTransparency(SkinnedMeshRenderer[] renderers, float targetAlpha, string modelName)
    {
        if (renderers == null || renderers.Length == 0)
        {
            Debug.LogWarning($"[PracticeSettings] {modelName} 렌더러가 없어 투명도 조정을 건너뜁니다.");
            return;
        }

        foreach (var renderer in renderers)
        {
            if (renderer == null) continue;

            Material[] materials = renderer.materials;

            for (int i = 0; i < materials.Length; i++)
            {
                Material mat = materials[i];
                if (mat == null) continue;

                // 투명도가 1 미만이면 Transparent 모드로 변경
                if (targetAlpha < 1f)
                {
                    // Rendering Mode를 Transparent로 설정
                    mat.SetFloat("_Mode", 3); // 3 = Transparent
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mat.SetInt("_ZWrite", 0);
                    mat.DisableKeyword("_ALPHATEST_ON");
                    mat.EnableKeyword("_ALPHABLEND_ON");
                    mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    mat.renderQueue = 3000;
                }
                else
                {
                    // Rendering Mode를 Opaque로 복원
                    mat.SetFloat("_Mode", 0); // 0 = Opaque
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                    mat.SetInt("_ZWrite", 1);
                    mat.DisableKeyword("_ALPHATEST_ON");
                    mat.DisableKeyword("_ALPHABLEND_ON");
                    mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    mat.renderQueue = -1;
                }

                // 알파값 적용
                Color color = mat.color;
                color.a = targetAlpha;
                mat.color = color;

                // _Color 프로퍼티가 있으면 거기에도 적용
                if (mat.HasProperty("_Color"))
                {
                    Color mainColor = mat.GetColor("_Color");
                    mainColor.a = targetAlpha;
                    mat.SetColor("_Color", mainColor);
                }
            }

            renderer.materials = materials;
        }

        Debug.Log($"[PracticeSettings] ✅ {modelName} 투명도 조정 완료: Alpha = {targetAlpha}");
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
}
