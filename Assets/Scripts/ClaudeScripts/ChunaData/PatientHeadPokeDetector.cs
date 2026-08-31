// ★[미사용 2026-08-31] 씬·프리팹에 배선이 없고, 다른 스크립트에서 부르지도 않는다.
//   지우지 않고 남겨 둔다 — 읽는 사람이 "이게 지금 도는 코드"라고 오해하지 않도록 이 줄을 단다.
//   판정 근거와 재확인 방법: .claude/tools/deadscan.py
using System;
using System.Linq;
using UnityEngine;
using Oculus.Interaction;
using Oculus.Interaction.Input;

/// <summary>
/// 환자 머리 Poke 감지기 - Meta SDK Poke 방식
/// PokeInteractable을 사용하여 손가락 접촉을 감지하고 애니메이션을 제어합니다.
///
/// 설정 방법:
/// 1. 환자 머리에 PokeInteractable 컴포넌트 추가
/// 2. 이 스크립트를 동일 오브젝트에 추가
/// 3. OVRCameraRig의 Hand에 PokeInteractor가 있는지 확인
/// </summary>
public class PatientHeadPokeDetector : MonoBehaviour
{
    [Header("=== Poke 설정 ===")]
    [Tooltip("PokeInteractable 참조 (없으면 자동 검색)")]
    [SerializeField] private PokeInteractable pokeInteractable;

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
    private bool isPoking = false;
    private Handedness? currentPokeHand = null;
    private float currentAnimationRatio = 0f;
    private float targetAnimationRatio = 0f;
    private Vector3 pokePosition;
    private float pokeDepth;

    // 이벤트
    public event Action<Handedness> OnPokeStarted;
    public event Action<Handedness> OnPokeEnded;
    public event Action<float> OnAnimationRatioChanged;
    public event Action<Vector3, float> OnPokeUpdated;  // position, depth

    /// <summary>
    /// 현재 Poke 중인지
    /// </summary>
    public bool IsPoking => isPoking;
    public Handedness? CurrentPokeHand => currentPokeHand;
    public Vector3 PokePosition => pokePosition;
    public float PokeDepth => pokeDepth;

    void Start()
    {
        // PokeInteractable 자동 찾기
        if (pokeInteractable == null)
        {
            pokeInteractable = GetComponent<PokeInteractable>();
        }

        if (pokeInteractable == null)
        {
            ChunaLogger.LogWarning("[PatientHeadPokeDetector] PokeInteractable이 없습니다. 추가해주세요.");
            return;
        }

        // Animator 자동 찾기
        if (patientAnimator == null)
        {
            patientAnimator = GetComponentInParent<Animator>();
        }

        // 이벤트 구독
        SubscribeToPokeEvents();
    }

    void OnDestroy()
    {
        UnsubscribeFromPokeEvents();
    }

    private void SubscribeToPokeEvents()
    {
        if (pokeInteractable == null) return;

        // PokeInteractable의 State 변화 감지
        pokeInteractable.WhenStateChanged += HandlePokeStateChanged;
        pokeInteractable.WhenPointerEventRaised += HandlePokePointerEvent;
    }

    private void UnsubscribeFromPokeEvents()
    {
        if (pokeInteractable == null) return;

        pokeInteractable.WhenStateChanged -= HandlePokeStateChanged;
        pokeInteractable.WhenPointerEventRaised -= HandlePokePointerEvent;
    }

    private void HandlePokeStateChanged(InteractableStateChangeArgs args)
    {
        if (showDebugLogs)
        {
            ChunaLogger.Log($"[PokeDetector] State: {args.PreviousState} → {args.NewState}");
        }

        // Select 상태 = Poke가 활성화됨
        if (args.NewState == InteractableState.Select)
        {
            isPoking = true;
            TryGetPokeHandedness();

            if (currentPokeHand.HasValue)
            {
                OnPokeStarted?.Invoke(currentPokeHand.Value);
            }

            if (showDebugLogs)
                ChunaLogger.Log($"<color=green>[PokeDetector] Poke 시작! 손: {currentPokeHand}</color>");
        }
        else if (args.PreviousState == InteractableState.Select && args.NewState != InteractableState.Select)
        {
            if (isPoking && currentPokeHand.HasValue)
            {
                OnPokeEnded?.Invoke(currentPokeHand.Value);
            }

            isPoking = false;
            currentPokeHand = null;

            if (showDebugLogs)
                ChunaLogger.Log("<color=orange>[PokeDetector] Poke 종료</color>");
        }
    }

    private void HandlePokePointerEvent(PointerEvent pointerEvent)
    {
        if (pointerEvent.Type == PointerEventType.Move && isPoking)
        {
            // Poke 위치 업데이트
            pokePosition = pointerEvent.Pose.position;

            OnPokeUpdated?.Invoke(pokePosition, pokeDepth);
        }
    }

    private void TryGetPokeHandedness()
    {
        // PokeInteractable에서 현재 Interactor 가져오기
        if (pokeInteractable.SelectingInteractorViews.Any())
        {
            foreach (var interactorView in pokeInteractable.SelectingInteractorViews)
            {
                var interactor = interactorView as PokeInteractor;
                if (interactor != null)
                {
                    // PokeInteractor에서 Hand 참조 가져오기 시도
                    var hand = interactor.GetComponentInParent<Hand>();
                    if (hand != null)
                    {
                        currentPokeHand = hand.Handedness;
                        return;
                    }

                    // HandVisual에서도 시도
                    var handVisual = interactor.GetComponentInParent<HandVisual>();
                    if (handVisual != null && handVisual.Hand != null)
                    {
                        currentPokeHand = handVisual.Hand.Handedness;
                        return;
                    }

                    // 이름으로 추측
                    string name = interactor.gameObject.name.ToLower();
                    if (name.Contains("left"))
                    {
                        currentPokeHand = Handedness.Left;
                        return;
                    }
                    if (name.Contains("right"))
                    {
                        currentPokeHand = Handedness.Right;
                        return;
                    }
                }
            }
        }

        currentPokeHand = null;
    }

    void Update()
    {
        if (!isPoking) return;
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
            ChunaLogger.Log($"[PokeDetector] 애니메이션: {currentAnimationRatio:P0} → {targetAnimationRatio:P0}");
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
        isPoking = false;
        currentPokeHand = null;
        currentAnimationRatio = 0f;
        targetAnimationRatio = 0f;
    }
}
