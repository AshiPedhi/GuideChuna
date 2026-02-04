using UnityEngine;

/// <summary>
/// 측굴 동작 감지 (가이드 핸드 따라하기)
/// Step 5: 가이드 핸드 연습용
/// </summary>
public class LateralFlexionDetector : MonoBehaviour
{
    [Header("=== 연결 ===")]
    [Tooltip("PracticeManager 참조")]
    [SerializeField] private PracticeManager practiceManager;

    [Tooltip("ChunaPathEvaluator 참조 (가이드 핸드 표시)")]
    [SerializeField] private MonoBehaviour chunaPathEvaluator;

    [Header("=== 손 참조 ===")]
    [SerializeField] private OVRHand leftHand;
    [SerializeField] private OVRHand rightHand;

    [Header("=== 설정 ===")]
    [Tooltip("동작 유사도 임계값 (0~1)")]
    [Range(0f, 1f)]
    [SerializeField] private float similarityThreshold = 0.7f;

    [Tooltip("동작 완료 유지 시간 (초)")]
    [SerializeField] private float holdDuration = 0.5f;

    [Tooltip("감지 활성화")]
    [SerializeField] private bool detectEnabled = true;

    [Tooltip("디버그 로그")]
    [SerializeField] private bool showDebugLogs = true;

    // 상태
    private float currentSimilarity = 0f;
    private float holdTimer = 0f;
    private bool isHolding = false;

    void Update()
    {
        if (!detectEnabled) return;

        UpdateSimilarity();
        CheckCompletion();
    }

    private void UpdateSimilarity()
    {
        // ChunaPathEvaluator에서 유사도 가져오기
        if (chunaPathEvaluator != null)
        {
            // GetCurrentSimilarity 메서드 호출 시도
            var method = chunaPathEvaluator.GetType().GetMethod("GetCurrentSimilarity");
            if (method != null)
            {
                currentSimilarity = (float)method.Invoke(chunaPathEvaluator, null);
            }
            else
            {
                // 대체: 손 위치 기반 간단한 유사도 계산
                currentSimilarity = CalculateSimpleSimilarity();
            }
        }
        else
        {
            currentSimilarity = CalculateSimpleSimilarity();
        }
    }

    private float CalculateSimpleSimilarity()
    {
        // ★ ChunaPathEvaluator 없이는 정확한 유사도 계산 불가
        // 손 트래킹만으로는 동작 완료를 판단할 수 없으므로 0 반환
        // (이 경우 Step 6은 ChunaPathEvaluator를 통해서만 진행 가능)
        if (showDebugLogs && chunaPathEvaluator == null)
        {
            Debug.LogWarning("[LateralFlexion] ChunaPathEvaluator 없음 - 유사도 계산 불가");
        }
        return 0f;
    }

    private void CheckCompletion()
    {
        if (currentSimilarity >= similarityThreshold)
        {
            if (!isHolding)
            {
                isHolding = true;
                holdTimer = 0f;

                if (showDebugLogs)
                    Debug.Log($"[LateralFlexion] Started holding (similarity: {currentSimilarity:F2})");
            }

            holdTimer += Time.deltaTime;

            if (holdTimer >= holdDuration)
            {
                OnFlexionCompleted();
                holdTimer = 0f;
                isHolding = false;
            }
        }
        else
        {
            if (isHolding && showDebugLogs)
                Debug.Log($"[LateralFlexion] Hold broken (similarity: {currentSimilarity:F2})");

            isHolding = false;
            holdTimer = 0f;
        }
    }

    private void OnFlexionCompleted()
    {
        if (showDebugLogs)
            Debug.Log("[LateralFlexion] Motion completed!");

        if (practiceManager != null)
        {
            // 사이클 완료 알림 (Step 5에서 사용)
            practiceManager.OnCycleCompleted();
        }
        else
        {
            Debug.LogWarning("[LateralFlexion] PracticeManager not assigned!");
        }
    }

    /// <summary>
    /// 감지 활성화/비활성화
    /// </summary>
    public void SetDetectEnabled(bool enabled)
    {
        detectEnabled = enabled;
        holdTimer = 0f;
        isHolding = false;

        if (showDebugLogs)
            Debug.Log($"[LateralFlexion] Detection enabled: {enabled}");
    }

    /// <summary>
    /// 현재 유사도 가져오기
    /// </summary>
    public float GetCurrentSimilarity()
    {
        return currentSimilarity;
    }

    /// <summary>
    /// PracticeManager 설정
    /// </summary>
    public void SetPracticeManager(PracticeManager manager)
    {
        practiceManager = manager;
    }

    /// <summary>
    /// ChunaPathEvaluator 설정
    /// </summary>
    public void SetChunaPathEvaluator(MonoBehaviour evaluator)
    {
        chunaPathEvaluator = evaluator;
    }
}
