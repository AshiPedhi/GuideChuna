using System;
using UnityEngine;
using Oculus.Interaction.Input;

/// <summary>
/// 환자 머리 터치 감지기 - OnTrigger 방식
/// 환자 머리에 부착하여 손 접촉을 감지하고 애니메이션을 제어합니다.
/// </summary>
public class PatientHeadTouchDetector : MonoBehaviour
{
    [Header("=== 애니메이션 설정 ===")]
    [Tooltip("환자의 Animator")]
    [SerializeField] private Animator patientAnimator;

    [Tooltip("재생할 애니메이션 상태 이름")]
    [SerializeField] private string animationStateName;

    [Tooltip("애니메이션 보간 속도")]
    [SerializeField] private float lerpSpeed = 5f;

    [Header("=== 손 데이터 참조 ===")]
    [Tooltip("ChunaPathEvaluator 참조 (프레임 데이터용)")]
    [SerializeField] private ChunaPathEvaluator pathEvaluator;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = true;

    // 상태
    private bool isLeftHandTouching = false;
    private bool isRightHandTouching = false;
    private float currentAnimationRatio = 0f;
    private float targetAnimationRatio = 0f;

    // 이벤트
    public event Action<bool> OnLeftHandTouchChanged;   // 왼손 접촉 상태 변경
    public event Action<bool> OnRightHandTouchChanged;  // 오른손 접촉 상태 변경
    public event Action<float> OnAnimationRatioChanged; // 애니메이션 비율 변경

    /// <summary>
    /// 손이 환자에게 닿았는지
    /// </summary>
    public bool IsAnyHandTouching => isLeftHandTouching || isRightHandTouching;
    public bool IsLeftHandTouching => isLeftHandTouching;
    public bool IsRightHandTouching => isRightHandTouching;

    void Start()
    {
        // Collider가 Trigger인지 확인
        var collider = GetComponent<Collider>();
        if (collider != null && !collider.isTrigger)
        {
            ChunaLogger.LogWarning("[PatientHeadTouchDetector] Collider가 Trigger로 설정되어 있지 않습니다. isTrigger를 활성화하세요.");
        }

        // Animator 자동 찾기
        if (patientAnimator == null)
        {
            patientAnimator = GetComponentInParent<Animator>();
        }
    }

    void Update()
    {
        if (!IsAnyHandTouching) return;
        if (patientAnimator == null || string.IsNullOrEmpty(animationStateName)) return;

        // PathEvaluator에서 현재 프레임 비율 가져오기
        if (pathEvaluator != null)
        {
            targetAnimationRatio = pathEvaluator.GetCurrentProgress();
        }

        // 선형 보간으로 부드럽게 애니메이션 적용
        currentAnimationRatio = Mathf.Lerp(currentAnimationRatio, targetAnimationRatio, Time.deltaTime * lerpSpeed);

        // Animator에 적용
        patientAnimator.Play(animationStateName, 0, currentAnimationRatio);
        patientAnimator.speed = 0f;  // 수동 제어

        OnAnimationRatioChanged?.Invoke(currentAnimationRatio);

        if (showDebugLogs && Time.frameCount % 30 == 0)
            ChunaLogger.Log($"[PatientHead] 애니메이션: {currentAnimationRatio:P0} → {targetAnimationRatio:P0}");
    }

    void OnTriggerEnter(Collider other)
    {
        var handedness = GetHandedness(other);
        if (handedness == null) return;

        if (handedness == Handedness.Left && !isLeftHandTouching)
        {
            isLeftHandTouching = true;
            OnLeftHandTouchChanged?.Invoke(true);

            if (showDebugLogs)
                ChunaLogger.Log("<color=green>[PatientHead] 왼손 접촉!</color>");
        }
        else if (handedness == Handedness.Right && !isRightHandTouching)
        {
            isRightHandTouching = true;
            OnRightHandTouchChanged?.Invoke(true);

            if (showDebugLogs)
                ChunaLogger.Log("<color=green>[PatientHead] 오른손 접촉!</color>");
        }
    }

    void OnTriggerExit(Collider other)
    {
        var handedness = GetHandedness(other);
        if (handedness == null) return;

        if (handedness == Handedness.Left && isLeftHandTouching)
        {
            isLeftHandTouching = false;
            OnLeftHandTouchChanged?.Invoke(false);

            if (showDebugLogs)
                ChunaLogger.Log("<color=orange>[PatientHead] 왼손 떨어짐</color>");
        }
        else if (handedness == Handedness.Right && isRightHandTouching)
        {
            isRightHandTouching = false;
            OnRightHandTouchChanged?.Invoke(false);

            if (showDebugLogs)
                ChunaLogger.Log("<color=orange>[PatientHead] 오른손 떨어짐</color>");
        }
    }

    void OnTriggerStay(Collider other)
    {
        // 접촉 유지 중 - 필요시 추가 로직
    }

    /// <summary>
    /// Collider에서 손의 Handedness 확인
    /// </summary>
    private Handedness? GetHandedness(Collider other)
    {
        // Meta SDK Hand 컴포넌트 확인
        var hand = other.GetComponentInParent<Hand>();
        if (hand != null)
        {
            return hand.Handedness;
        }

        // HandVisual 컴포넌트 확인
        var handVisual = other.GetComponentInParent<Oculus.Interaction.HandVisual>();
        if (handVisual != null && handVisual.Hand != null)
        {
            return handVisual.Hand.Handedness;
        }

        // Tag 기반 확인 (폴백)
        if (other.CompareTag("LeftHand")) return Handedness.Left;
        if (other.CompareTag("RightHand")) return Handedness.Right;

        return null;
    }

    /// <summary>
    /// 애니메이션 상태 설정
    /// </summary>
    public void SetAnimationState(string stateName)
    {
        animationStateName = stateName;
    }

    /// <summary>
    /// 애니메이션 비율 직접 설정
    /// </summary>
    public void SetTargetAnimationRatio(float ratio)
    {
        targetAnimationRatio = Mathf.Clamp01(ratio);
    }

    /// <summary>
    /// 현재 애니메이션 비율
    /// </summary>
    public float GetCurrentAnimationRatio()
    {
        return currentAnimationRatio;
    }

    /// <summary>
    /// 리셋
    /// </summary>
    public void ResetState()
    {
        isLeftHandTouching = false;
        isRightHandTouching = false;
        currentAnimationRatio = 0f;
        targetAnimationRatio = 0f;
    }
}
