using System.Collections.Generic;
using UnityEngine;
using Oculus.Interaction;

/// <summary>
/// 사용자의 동작 진행도를 시각적으로 표시하는 손 모델 관리 컴포넌트
/// HandPosePlayer와 함께 사용하여 유사도에 따라 색상이 변하는 피드백 제공
/// </summary>
public class HandPoseProgressIndicator : MonoBehaviour
{
    [Header("===  진행도 표시 손 모델 ===")]
    [SerializeField]
    [Tooltip("왼손 진행도 표시 모델 (HandVisual)")]
    private HandVisual leftProgressHand;

    [SerializeField]
    [Tooltip("오른손 진행도 표시 모델 (HandVisual)")]
    private HandVisual rightProgressHand;

    [Header("=== HandPosePlayer 참조 ===")]
    [SerializeField]
    [Tooltip("유사도 정보를 가져올 HandPosePlayer")]
    private HandPosePlayer handPosePlayer;

    [Header("=== 색상 설정 ===")]
    [SerializeField]
    [Tooltip("낮은 유사도 색상 (0~mediumThreshold)")]
    private Color lowSimilarityColor = new Color(1f, 0f, 0f, 0.7f); // 빨강

    [SerializeField]
    [Tooltip("중간 유사도 색상 (mediumThreshold~highThreshold)")]
    private Color mediumSimilarityColor = new Color(1f, 1f, 0f, 0.7f); // 노랑

    [SerializeField]
    [Tooltip("높은 유사도 색상 (highThreshold~1.0)")]
    private Color highSimilarityColor = new Color(0f, 1f, 0f, 0.7f); // 초록

    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip("중간 유사도 기준점")]
    private float mediumThreshold = 0.5f;

    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip("높은 유사도 기준점")]
    private float highThreshold = 0.7f;

    [Header("=== 표시 설정 ===")]
    [SerializeField]
    [Tooltip("진행도 표시 활성화")]
    private bool showProgressIndicator = true;

    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip("진행도 표시 손의 기본 투명도")]
    private float baseAlpha = 0.7f;

    [SerializeField]
    [Tooltip("유사도 변화 시 부드러운 색상 전환")]
    private bool smoothColorTransition = true;

    [SerializeField]
    [Tooltip("색상 전환 속도 (높을수록 빠름)")]
    private float colorTransitionSpeed = 5f;

    [Header("=== 조인트별 피드백 (실험적) ===")]
    [SerializeField]
    [Tooltip("각 조인트별로 다른 색상 표시 (고급 기능)")]
    private bool enablePerJointFeedback = false;

    // 내부 상태
    private Color currentLeftColor;
    private Color currentRightColor;
    private float currentLeftSimilarity = 0f;
    private float currentRightSimilarity = 0f;
    
    // 손 렌더러 캐싱
    private Renderer[] leftHandRenderers;
    private Renderer[] rightHandRenderers;
    private List<Material> leftHandMaterials = new List<Material>();
    private List<Material> rightHandMaterials = new List<Material>();

    void Start()
    {
        // HandPosePlayer 자동 탐색
        if (handPosePlayer == null)
        {
            handPosePlayer = FindObjectOfType<HandPosePlayer>();
            if (handPosePlayer == null)
            {
                Debug.LogError("[HandPoseProgressIndicator] HandPosePlayer를 찾을 수 없습니다!");
                return;
            }
        }

        // 진행도 손 모델 초기화
        InitializeProgressHands();
        
        // 초기 색상 설정
        currentLeftColor = lowSimilarityColor;
        currentRightColor = lowSimilarityColor;
        
        Debug.Log("[HandPoseProgressIndicator] 초기화 완료");
    }

    void Update()
    {
        if (!showProgressIndicator || handPosePlayer == null)
            return;

        // HandPosePlayer로부터 현재 유사도 가져오기
        var result = handPosePlayer.GetCurrentSimilarity();
        currentLeftSimilarity = result.leftHandSimilarity;
        currentRightSimilarity = result.rightHandSimilarity;

        // 유사도에 따라 색상 업데이트
        UpdateHandColors();
    }

    /// <summary>
    /// 진행도 표시 손 모델 초기화
    /// </summary>
    private void InitializeProgressHands()
    {
        if (leftProgressHand != null)
        {
            leftProgressHand.gameObject.SetActive(showProgressIndicator);
            CacheHandRenderers(leftProgressHand, out leftHandRenderers);
            CreateMaterialInstances(leftHandRenderers, leftHandMaterials);
            Debug.Log($"[ProgressIndicator] 왼손 진행도 모델 초기화: {leftHandMaterials.Count}개 재질");
        }

        if (rightProgressHand != null)
        {
            rightProgressHand.gameObject.SetActive(showProgressIndicator);
            CacheHandRenderers(rightProgressHand, out rightHandRenderers);
            CreateMaterialInstances(rightHandRenderers, rightHandMaterials);
            Debug.Log($"[ProgressIndicator] 오른손 진행도 모델 초기화: {rightHandMaterials.Count}개 재질");
        }
    }

    /// <summary>
    /// HandVisual의 모든 렌더러 캐싱
    /// </summary>
    private void CacheHandRenderers(HandVisual handVisual, out Renderer[] renderers)
    {
        renderers = handVisual.GetComponentsInChildren<Renderer>(true);
    }

    /// <summary>
    /// 각 렌더러의 Material 인스턴스 생성 (공유 방지)
    /// </summary>
    private void CreateMaterialInstances(Renderer[] renderers, List<Material> materialList)
    {
        materialList.Clear();
        foreach (var renderer in renderers)
        {
            if (renderer != null)
            {
                // 각 렌더러마다 독립적인 Material 인스턴스 생성
                Material[] mats = renderer.materials;
                for (int i = 0; i < mats.Length; i++)
                {
                    mats[i] = new Material(mats[i]); // 인스턴스 생성
                    materialList.Add(mats[i]);
                }
                renderer.materials = mats;
            }
        }
    }

    /// <summary>
    /// 유사도에 따라 손 모델 색상 업데이트
    /// </summary>
    private void UpdateHandColors()
    {
        // 왼손 색상 업데이트
        if (leftProgressHand != null && leftHandMaterials.Count > 0)
        {
            Color targetColor = GetColorForSimilarity(currentLeftSimilarity);
            
            if (smoothColorTransition)
            {
                currentLeftColor = Color.Lerp(currentLeftColor, targetColor, Time.deltaTime * colorTransitionSpeed);
            }
            else
            {
                currentLeftColor = targetColor;
            }
            
            ApplyColorToMaterials(leftHandMaterials, currentLeftColor);
        }

        // 오른손 색상 업데이트
        if (rightProgressHand != null && rightHandMaterials.Count > 0)
        {
            Color targetColor = GetColorForSimilarity(currentRightSimilarity);
            
            if (smoothColorTransition)
            {
                currentRightColor = Color.Lerp(currentRightColor, targetColor, Time.deltaTime * colorTransitionSpeed);
            }
            else
            {
                currentRightColor = targetColor;
            }
            
            ApplyColorToMaterials(rightHandMaterials, currentRightColor);
        }
    }

    /// <summary>
    /// 유사도 값에 따른 색상 계산
    /// </summary>
    private Color GetColorForSimilarity(float similarity)
    {
        if (similarity < mediumThreshold)
        {
            // 낮은 유사도: 빨강 → 노랑으로 보간
            float t = similarity / mediumThreshold;
            return Color.Lerp(lowSimilarityColor, mediumSimilarityColor, t);
        }
        else if (similarity < highThreshold)
        {
            // 중간 유사도: 노랑 → 초록으로 보간
            float t = (similarity - mediumThreshold) / (highThreshold - mediumThreshold);
            return Color.Lerp(mediumSimilarityColor, highSimilarityColor, t);
        }
        else
        {
            // 높은 유사도: 초록
            return highSimilarityColor;
        }
    }

    /// <summary>
    /// Material 리스트에 색상 적용
    /// </summary>
    private void ApplyColorToMaterials(List<Material> materials, Color color)
    {
        foreach (var mat in materials)
        {
            if (mat != null)
            {
                // Standard Shader의 경우
                if (mat.HasProperty("_Color"))
                {
                    mat.SetColor("_Color", color);
                }
                
                // Transparent 모드 설정
                if (mat.HasProperty("_Mode"))
                {
                    mat.SetFloat("_Mode", 3); // Transparent
                }
                
                // 렌더링 모드 설정
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = 3000;
            }
        }
    }

    /// <summary>
    /// 진행도 표시 활성화/비활성화
    /// </summary>
    public void SetProgressIndicatorActive(bool active)
    {
        showProgressIndicator = active;
        
        if (leftProgressHand != null)
            leftProgressHand.gameObject.SetActive(active);
        
        if (rightProgressHand != null)
            rightProgressHand.gameObject.SetActive(active);
        
        Debug.Log($"[ProgressIndicator] 진행도 표시: {(active ? "활성화" : "비활성화")}");
    }

    /// <summary>
    /// 색상 임계값 설정
    /// </summary>
    public void SetThresholds(float medium, float high)
    {
        mediumThreshold = Mathf.Clamp01(medium);
        highThreshold = Mathf.Clamp01(high);
        Debug.Log($"[ProgressIndicator] 임계값 설정: Medium={mediumThreshold:F2}, High={highThreshold:F2}");
    }

    /// <summary>
    /// 현재 유사도 정보 가져오기 (디버깅/UI용)
    /// </summary>
    public (float left, float right) GetCurrentSimilarity()
    {
        return (currentLeftSimilarity, currentRightSimilarity);
    }

    void OnDestroy()
    {
        // Material 인스턴스 정리
        foreach (var mat in leftHandMaterials)
        {
            if (mat != null)
                Destroy(mat);
        }
        foreach (var mat in rightHandMaterials)
        {
            if (mat != null)
                Destroy(mat);
        }
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        // Inspector에서 값 변경 시 즉시 반영
        if (Application.isPlaying && showProgressIndicator)
        {
            UpdateHandColors();
        }
    }
#endif
}
