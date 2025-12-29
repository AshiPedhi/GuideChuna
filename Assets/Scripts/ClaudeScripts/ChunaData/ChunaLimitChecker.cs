using System;
using System.Collections.Generic;
using UnityEngine;
using Oculus.Interaction.Input;
using Oculus.Interaction;

/// <summary>
/// 추나 시술 한계 감지 시스템 (프레임 비율 기반 단순화 버전)
/// 사용자 손이 핸드 데이터의 몇 % 프레임에 있는지를 기준으로 상태 결정
/// </summary>
public class ChunaLimitChecker : MonoBehaviour
{
    [Header("=== 손 참조 ===")]
    [SerializeField] private HandVisual playerLeftHand;
    [SerializeField] private HandVisual playerRightHand;

    [Header("=== 진행률 기반 체크 ===")]
    [Tooltip("ChunaPathEvaluator 참조 - 진행률 확인용")]
    [SerializeField] private ChunaPathEvaluator pathEvaluator;

    [Tooltip("경고 시작 비율 (0~1)")]
    [SerializeField] private float warningRatio = 0.3f;

    [Tooltip("위험 시작 비율 (0~1)")]
    [SerializeField] private float dangerRatio = 0.5f;

    [Tooltip("체크 활성화")]
    [SerializeField] private bool enableChecking = true;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = true;
    [SerializeField] private bool drawDebugGizmos = true;

    // 상태
    private bool isInitialized;
    private LimitCheckResult currentLeftResult = new LimitCheckResult();
    private LimitCheckResult currentRightResult = new LimitCheckResult();

    // 참조 캐싱 완료 플래그
    private bool referencesSearched = false;

    // 이벤트
    public event Action<LimitCheckResult, LimitCheckResult> OnLimitStatusChanged;

    /// <summary>
    /// 한계 체크 결과 (단순화)
    /// </summary>
    [System.Serializable]
    public class LimitCheckResult
    {
        public LimitStatus overallStatus = LimitStatus.Safe;
        public float frameRatio = 0f;  // 현재 프레임 비율
        public int currentFrame = 0;
        public int totalFrames = 0;
    }

    void Start()
    {
        // 한 번만 검색
        FindHandReferences();
        referencesSearched = true;
    }

    void Update()
    {
        if (!enableChecking || !isInitialized)
            return;

        UpdateLimitStatus();
    }

    /// <summary>
    /// 손 참조 자동 탐색
    /// </summary>
    private void FindHandReferences()
    {
        if (playerLeftHand == null || playerRightHand == null)
        {
            var hands = FindObjectsOfType<HandVisual>();
            foreach (var hand in hands)
            {
                if (hand.Hand != null)
                {
                    if (hand.Hand.Handedness == Handedness.Left && playerLeftHand == null)
                        playerLeftHand = hand;
                    else if (hand.Hand.Handedness == Handedness.Right && playerRightHand == null)
                        playerRightHand = hand;
                }
            }
        }
    }

    /// <summary>
    /// 초기화
    /// </summary>
    public void Initialize()
    {
        if (pathEvaluator == null)
        {
            pathEvaluator = FindObjectOfType<ChunaPathEvaluator>();
        }

        isInitialized = true;

        if (showDebugLogs)
            Debug.Log("<color=green>[ChunaLimitChecker] 초기화 완료 (프레임 비율 기반)</color>");
    }

    /// <summary>
    /// 프레임 비율 기반 상태 업데이트
    /// </summary>
    private void UpdateLimitStatus()
    {
        float ratio = GetCurrentProgress();
        int currentFrame = pathEvaluator != null ? pathEvaluator.GetUserHandFrameIndex() : 0;
        int totalFrames = pathEvaluator != null ? pathEvaluator.GetTotalFrameCount() : 0;

        LimitStatus newStatus = GetStatusFromRatio(ratio);

        // 결과 업데이트 (왼손은 항상 Safe, 오른손만 체크)
        LimitCheckResult prevRight = currentRightResult;

        currentLeftResult = new LimitCheckResult
        {
            overallStatus = LimitStatus.Safe,
            frameRatio = ratio,
            currentFrame = currentFrame,
            totalFrames = totalFrames
        };

        currentRightResult = new LimitCheckResult
        {
            overallStatus = newStatus,
            frameRatio = ratio,
            currentFrame = currentFrame,
            totalFrames = totalFrames
        };

        // 상태 변경 감지
        if (prevRight.overallStatus != newStatus)
        {
            OnLimitStatusChanged?.Invoke(currentLeftResult, currentRightResult);

            if (showDebugLogs)
            {
                string color = newStatus == LimitStatus.Safe ? "green" :
                              (newStatus == LimitStatus.Warning ? "yellow" :
                              (newStatus == LimitStatus.Danger ? "orange" : "red"));
                Debug.Log($"<color={color}>[ChunaLimitChecker] 상태: {newStatus}, 프레임: {currentFrame}/{totalFrames} ({ratio:P0})</color>");
            }
        }
    }

    /// <summary>
    /// 비율에서 상태 결정
    /// </summary>
    private LimitStatus GetStatusFromRatio(float ratio)
    {
        if (ratio >= dangerRatio)
            return LimitStatus.Exceeded;
        if (ratio >= warningRatio)
            return LimitStatus.Warning;
        return LimitStatus.Safe;
    }

    /// <summary>
    /// 현재 진행률 가져오기 (캐싱된 참조 사용)
    /// </summary>
    private float GetCurrentProgress()
    {
        // pathEvaluator가 없으면 0 반환 (Initialize에서 찾음)
        return pathEvaluator != null ? pathEvaluator.GetCurrentProgress() : 0f;
    }

    // ========== Public API ==========

    public void SetPathEvaluator(ChunaPathEvaluator evaluator)
    {
        pathEvaluator = evaluator;
    }

    public void SetEnabled(bool enabled)
    {
        enableChecking = enabled;
    }

    public void SetRatioThresholds(float warning, float danger)
    {
        warningRatio = Mathf.Clamp01(warning);
        dangerRatio = Mathf.Clamp01(danger);
    }

    public LimitCheckResult GetLeftHandResult() => currentLeftResult;
    public LimitCheckResult GetRightHandResult() => currentRightResult;

    public bool IsHandSafe(bool isLeftHand)
    {
        return isLeftHand ? true : currentRightResult.overallStatus == LimitStatus.Safe;
    }

    public void Reset()
    {
        isInitialized = false;
        currentLeftResult = new LimitCheckResult();
        currentRightResult = new LimitCheckResult();

        if (showDebugLogs)
            Debug.Log("[ChunaLimitChecker] 리셋됨");
    }

    /// <summary>
    /// 손목 위치 가져오기
    /// </summary>
    private Vector3 GetWristPosition(HandVisual hand)
    {
        if (hand == null || hand.Joints == null)
            return Vector3.zero;

        // Wrist 관절 (0번)
        int wristIndex = 0;
        if (hand.Joints.Count > wristIndex && hand.Joints[wristIndex] != null)
        {
            return hand.Joints[wristIndex].position;
        }

        return hand.transform.position;
    }

    void OnDrawGizmos()
    {
        if (!drawDebugGizmos || !isInitialized)
            return;

        // 오른손 상태 표시 (손목 위치)
        Vector3 rightWristPos = GetWristPosition(playerRightHand);
        if (rightWristPos != Vector3.zero)
        {
            Gizmos.color = GetColorForStatus(currentRightResult.overallStatus);
            Gizmos.DrawWireSphere(rightWristPos, 0.05f);
        }
    }

    private Color GetColorForStatus(LimitStatus status)
    {
        switch (status)
        {
            case LimitStatus.Safe:
                return Color.green;
            case LimitStatus.Warning:
                return Color.yellow;
            case LimitStatus.Danger:
                return new Color(1f, 0.5f, 0f);
            case LimitStatus.Exceeded:
                return Color.red;
            default:
                return Color.gray;
        }
    }
}
