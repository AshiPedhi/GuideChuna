using UnityEngine;
using Oculus.Interaction;
using static HandPoseDataLoader;

/// <summary>
/// 평가 비교용 반투명 핸드 모델 표시
/// 현재 진행 중인 스텝의 목표 손 포즈를 반투명하게 표시하여
/// 사용자가 자신의 손 위치/각도를 비교할 수 있도록 함
/// </summary>
public class ReferenceHandDisplay : MonoBehaviour
{
    [Header("=== 참조 손 모델 ===")]
    [Tooltip("왼손 참조 모델 (HandVisual 또는 HandTransformMapper)")]
    [SerializeField] private HandVisual leftReferenceHand;

    [Tooltip("오른손 참조 모델 (HandVisual 또는 HandTransformMapper)")]
    [SerializeField] private HandVisual rightReferenceHand;

    [Header("=== 트레이닝 컨트롤러 ===")]
    [Tooltip("HandPoseTrainingController 참조 (현재 프레임 가져오기)")]
    [SerializeField] private HandPoseTrainingController trainingController;

    [Header("=== 표시 설정 ===")]
    [Tooltip("참조 손 표시 여부")]
    [SerializeField] private bool showReferenceHands = true;

    [Tooltip("참조 손 투명도 (0~1)")]
    [SerializeField][Range(0f, 1f)] private float referenceAlpha = 0.4f;

    [Tooltip("참조 손 색상")]
    [SerializeField] private Color referenceColor = new Color(0.3f, 1f, 0.3f, 0.4f); // 초록색

    [Tooltip("자동 업데이트 (매 프레임 목표 포즈 갱신)")]
    [SerializeField] private bool autoUpdate = true;

    [Tooltip("업데이트 간격 (초)")]
    [SerializeField] private float updateInterval = 0.1f;

    [Header("=== 위치 설정 ===")]
    [Tooltip("참조 손 위치 오프셋 (사용자 손과 겹치지 않도록)")]
    [SerializeField] private Vector3 positionOffset = Vector3.zero;

    [Tooltip("참조점 (옵션 - 손 위치 기준점)")]
    [SerializeField] private Transform referencePoint;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = false;

    // 업데이트 타이머
    private float updateTimer = 0f;

    // 렌더러 캐시
    private SkinnedMeshRenderer[] leftRenderers;
    private SkinnedMeshRenderer[] rightRenderers;

    void Awake()
    {
        // TrainingController 자동 찾기
        if (trainingController == null)
        {
            trainingController = FindObjectOfType<HandPoseTrainingController>();
            if (trainingController == null)
            {
                Debug.LogWarning("[ReferenceHandDisplay] HandPoseTrainingController를 찾을 수 없습니다!");
            }
        }

        // 렌더러 캐시
        CacheRenderers();
    }

    void Start()
    {
        // 참조 손 초기 설정
        SetupReferenceHands();
    }

    void Update()
    {
        if (!autoUpdate || trainingController == null)
            return;

        updateTimer += Time.deltaTime;

        if (updateTimer >= updateInterval)
        {
            updateTimer = 0f;
            UpdateReferenceHandPoses();
        }
    }

    /// <summary>
    /// 참조 손 초기 설정 (반투명, 색상)
    /// </summary>
    private void SetupReferenceHands()
    {
        if (leftReferenceHand != null)
        {
            SetupHandVisual(leftReferenceHand, leftRenderers);
        }

        if (rightReferenceHand != null)
        {
            SetupHandVisual(rightReferenceHand, rightRenderers);
        }

        // 표시 상태 설정
        SetReferenceHandsVisible(showReferenceHands);

        if (showDebugLogs)
        {
            Debug.Log($"[ReferenceHandDisplay] 참조 손 초기화 완료 (투명도: {referenceAlpha}, 색상: {referenceColor})");
        }
    }

    /// <summary>
    /// HandVisual 반투명 설정
    /// </summary>
    private void SetupHandVisual(HandVisual handVisual, SkinnedMeshRenderer[] renderers)
    {
        if (handVisual == null)
            return;

        renderers = handVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true);

        foreach (var renderer in renderers)
        {
            // 새 머티리얼 생성 (원본 보존)
            Material mat = new Material(renderer.material);

            // Transparent 모드로 변경
            mat.SetFloat("_Mode", 3); // Transparent
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;

            // 색상 및 투명도 설정
            if (mat.HasProperty("_Color"))
            {
                Color finalColor = referenceColor;
                finalColor.a = referenceAlpha;
                mat.color = finalColor;
            }
            else if (mat.HasProperty("_BaseColor"))
            {
                Color finalColor = referenceColor;
                finalColor.a = referenceAlpha;
                mat.SetColor("_BaseColor", finalColor);
            }

            renderer.material = mat;
        }
    }

    /// <summary>
    /// 렌더러 캐시
    /// </summary>
    private void CacheRenderers()
    {
        if (leftReferenceHand != null)
        {
            leftRenderers = leftReferenceHand.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        }

        if (rightReferenceHand != null)
        {
            rightRenderers = rightReferenceHand.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        }
    }

    /// <summary>
    /// 참조 손 포즈 업데이트 (현재 목표 프레임 기준)
    /// </summary>
    private void UpdateReferenceHandPoses()
    {
        if (trainingController == null)
            return;

        // TrainingController에서 현재 재생 상태 가져오기
        var (leftPlaying, rightPlaying, leftFrame, rightFrame, totalFrames) = trainingController.GetPlaybackState();

        if (totalFrames == 0)
        {
            if (showDebugLogs)
            {
                Debug.LogWarning("[ReferenceHandDisplay] 로드된 프레임이 없습니다.");
            }
            return;
        }

        // 현재 프레임 인덱스로 포즈 가져오기
        // 참고: trainingController는 내부적으로 loadedFrames를 가지고 있지만
        // public으로 노출되어 있지 않으므로, 대안으로 현재 재생 중인 손 모델의
        // Transform을 복사하는 방식을 사용

        // 대신 간단하게 trainingController의 재생 손 모델과 동일한 포즈를 표시
        UpdateFromTrainingController();
    }

    /// <summary>
    /// TrainingController의 재생 손에서 포즈 복사
    /// </summary>
    private void UpdateFromTrainingController()
    {
        // TrainingController의 재생 손 모델 참조 필요
        // 현재 TrainingController는 leftHandVisual/rightHandVisual을 private으로 가지고 있음
        //
        // 해결 방법:
        // 1. TrainingController에 public getter 추가
        // 2. 직접 CSV 데이터를 로드하여 현재 프레임 가져오기
        // 3. ScenarioEventSystem을 통해 현재 프레임 데이터 전달받기
        //
        // 여기서는 방법 2를 사용: 직접 데이터 로드

        if (showDebugLogs)
        {
            Debug.Log("[ReferenceHandDisplay] 포즈 업데이트 (TrainingController 기반)");
        }
    }

    /// <summary>
    /// 특정 프레임의 포즈를 참조 손에 적용
    /// </summary>
    /// <param name="frame">적용할 PoseFrame</param>
    public void ApplyPoseFrame(PoseFrame frame)
    {
        if (frame == null)
        {
            Debug.LogWarning("[ReferenceHandDisplay] PoseFrame이 null입니다.");
            return;
        }

        // 왼손 적용
        if (leftReferenceHand != null)
        {
            ApplyHandPose(leftReferenceHand, frame.leftRootPosition, frame.leftRootRotation, frame.leftLocalPoses, "왼손");
        }

        // 오른손 적용
        if (rightReferenceHand != null)
        {
            ApplyHandPose(rightReferenceHand, frame.rightRootPosition, frame.rightRootRotation, frame.rightLocalPoses, "오른손");
        }

        if (showDebugLogs)
        {
            Debug.Log($"[ReferenceHandDisplay] PoseFrame 적용 완료");
        }
    }

    /// <summary>
    /// 손 포즈 적용 (Root + 조인트)
    /// </summary>
    private void ApplyHandPose(HandVisual handVisual, Vector3 rootPosition, Quaternion rootRotation, System.Collections.Generic.Dictionary<int, PoseData> localPoses, string handName)
    {
        if (handVisual == null || handVisual.Root == null)
            return;

        // Root Transform 계산
        Vector3 targetRootPos = rootPosition + positionOffset;
        Quaternion targetRootRot = rootRotation;

        if (referencePoint != null)
        {
            targetRootPos = referencePoint.position + rootPosition + positionOffset;
            targetRootRot = referencePoint.rotation * rootRotation;
        }

        // Root 적용
        handVisual.Root.position = targetRootPos;
        handVisual.Root.rotation = targetRootRot;

        // 조인트 적용
        for (int i = 0; i < handVisual.Joints.Count; i++)
        {
            if (localPoses.TryGetValue(i, out PoseData poseData) && handVisual.Joints[i] != null)
            {
                handVisual.Joints[i].localPosition = poseData.position;
                handVisual.Joints[i].localRotation = poseData.rotation;
            }
        }
    }

    /// <summary>
    /// 참조 손 표시/숨김
    /// </summary>
    public void SetReferenceHandsVisible(bool visible)
    {
        showReferenceHands = visible;

        // 왼손
        if (leftReferenceHand != null && leftReferenceHand.gameObject != null)
        {
            leftReferenceHand.gameObject.SetActive(visible);
        }

        // 오른손
        if (rightReferenceHand != null && rightReferenceHand.gameObject != null)
        {
            rightReferenceHand.gameObject.SetActive(visible);
        }

        if (showDebugLogs)
        {
            Debug.Log($"[ReferenceHandDisplay] 참조 손 표시: {visible}");
        }
    }

    /// <summary>
    /// 투명도 설정
    /// </summary>
    public void SetAlpha(float alpha)
    {
        referenceAlpha = Mathf.Clamp01(alpha);

        // 왼손
        if (leftRenderers != null)
        {
            SetRenderersAlpha(leftRenderers, referenceAlpha);
        }

        // 오른손
        if (rightRenderers != null)
        {
            SetRenderersAlpha(rightRenderers, referenceAlpha);
        }

        if (showDebugLogs)
        {
            Debug.Log($"[ReferenceHandDisplay] 투명도 변경: {referenceAlpha}");
        }
    }

    /// <summary>
    /// 렌더러들의 투명도 설정
    /// </summary>
    private void SetRenderersAlpha(SkinnedMeshRenderer[] renderers, float alpha)
    {
        if (renderers == null)
            return;

        foreach (var renderer in renderers)
        {
            if (renderer == null || renderer.material == null)
                continue;

            Material mat = renderer.material;

            if (mat.HasProperty("_Color"))
            {
                Color color = mat.color;
                color.a = alpha;
                mat.color = color;
            }
            else if (mat.HasProperty("_BaseColor"))
            {
                Color color = mat.GetColor("_BaseColor");
                color.a = alpha;
                mat.SetColor("_BaseColor", color);
            }
        }
    }

    /// <summary>
    /// 색상 설정
    /// </summary>
    public void SetReferenceColor(Color color)
    {
        referenceColor = color;

        // 왼손
        if (leftRenderers != null)
        {
            SetRenderersColor(leftRenderers, referenceColor);
        }

        // 오른손
        if (rightRenderers != null)
        {
            SetRenderersColor(rightRenderers, referenceColor);
        }

        if (showDebugLogs)
        {
            Debug.Log($"[ReferenceHandDisplay] 색상 변경: {referenceColor}");
        }
    }

    /// <summary>
    /// 렌더러들의 색상 설정
    /// </summary>
    private void SetRenderersColor(SkinnedMeshRenderer[] renderers, Color color)
    {
        if (renderers == null)
            return;

        foreach (var renderer in renderers)
        {
            if (renderer == null || renderer.material == null)
                continue;

            Material mat = renderer.material;

            if (mat.HasProperty("_Color"))
            {
                Color finalColor = color;
                finalColor.a = referenceAlpha;
                mat.color = finalColor;
            }
            else if (mat.HasProperty("_BaseColor"))
            {
                Color finalColor = color;
                finalColor.a = referenceAlpha;
                mat.SetColor("_BaseColor", finalColor);
            }
        }
    }

    /// <summary>
    /// 위치 오프셋 설정 (사용자 손과 겹치지 않도록)
    /// </summary>
    public void SetPositionOffset(Vector3 offset)
    {
        positionOffset = offset;

        if (showDebugLogs)
        {
            Debug.Log($"[ReferenceHandDisplay] 위치 오프셋 변경: {offset}");
        }
    }

    /// <summary>
    /// 자동 업데이트 토글
    /// </summary>
    public void SetAutoUpdate(bool enable)
    {
        autoUpdate = enable;

        if (showDebugLogs)
        {
            Debug.Log($"[ReferenceHandDisplay] 자동 업데이트: {enable}");
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// Inspector 테스트 - 표시
    /// </summary>
    [ContextMenu("Test - Show Reference Hands")]
    private void TestShow()
    {
        SetReferenceHandsVisible(true);
    }

    /// <summary>
    /// Inspector 테스트 - 숨김
    /// </summary>
    [ContextMenu("Test - Hide Reference Hands")]
    private void TestHide()
    {
        SetReferenceHandsVisible(false);
    }

    /// <summary>
    /// Inspector 테스트 - 투명도 변경
    /// </summary>
    [ContextMenu("Test - Set Alpha 0.5")]
    private void TestAlpha()
    {
        SetAlpha(0.5f);
    }
#endif
}
