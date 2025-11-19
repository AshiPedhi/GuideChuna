using UnityEngine;

/// <summary>
/// 환자 모델 애니메이션 컨트롤러
/// - 핸드 데이터 O + 애니메이션 O → 진행도 동기화
/// - 핸드 데이터 X + 애니메이션 O → 자동 재생
/// </summary>
public class PatientModelController : MonoBehaviour
{
    [Header("=== 환자 모델 설정 ===")]
    [SerializeField] private Animator patientAnimator;

    [Tooltip("애니메이션 레이어 인덱스 (기본값: 0)")]
    [SerializeField] private int animationLayer = 0;

    [Header("=== HandPosePlayer 연결 ===")]
    [SerializeField] private HandPosePlayer handPosePlayer;

    [Header("=== 진행 설정 ===")]
    [Tooltip("양손 모두 필요 여부 (동기화 모드에서만 사용)")]
    [SerializeField] private bool requireBothHands = true;

    [Tooltip("부드러운 전환 사용 (동기화 모드)")]
    [SerializeField] private bool useSmoothTransition = true;

    [Tooltip("전환 속도 (높을수록 빠름)")]
    [SerializeField] private float transitionSpeed = 10f;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLog = true;

    // 현재 상태
    private AnimationPlayMode currentPlayMode = AnimationPlayMode.None;
    private string currentAnimationName = "";
    private int currentAnimationHash = 0;

    private bool isPlaying = false;
    private bool isCompleted = false;
    private float currentProgress = 0f;

    // 이벤트
    public System.Action<float> OnProgressUpdated;
    public System.Action OnAnimationCompleted;

    // 공개 프로퍼티
    public float CurrentProgress => currentProgress;
    public bool IsCompleted => isCompleted;
    public bool IsPlaying => isPlaying;
    public AnimationPlayMode CurrentPlayMode => currentPlayMode;

    private void Start()
    {
        InitializeComponents();
    }

    private void InitializeComponents()
    {
        // Animator 자동 찾기
        if (patientAnimator == null)
        {
            patientAnimator = GetComponent<Animator>();
            if (patientAnimator == null)
            {
                patientAnimator = GetComponentInChildren<Animator>();
            }
        }

        if (patientAnimator == null)
        {
            Debug.LogError("[PatientModelController] Animator를 찾을 수 없습니다!");
            return;
        }

        // HandPosePlayer 자동 찾기
        if (handPosePlayer == null)
        {
            handPosePlayer = FindObjectOfType<HandPosePlayer>();
            if (handPosePlayer != null && showDebugLog)
            {
                Debug.Log("[PatientModelController] HandPosePlayer 자동으로 찾음");
            }
        }

        if (showDebugLog)
        {
            Debug.Log("[PatientModelController] 초기화 완료");
        }
    }

    private void Update()
    {
        if (!isPlaying || isCompleted || patientAnimator == null) return;

        switch (currentPlayMode)
        {
            case AnimationPlayMode.AutoPlay:
                UpdateAutoPlayMode();
                break;

            case AnimationPlayMode.SyncWithUser:
                UpdateSyncWithUserMode();
                break;
        }
    }

    /// <summary>
    /// 자동 재생 모드 업데이트
    /// </summary>
    private void UpdateAutoPlayMode()
    {
        AnimatorStateInfo stateInfo = patientAnimator.GetCurrentAnimatorStateInfo(animationLayer);

        if (stateInfo.IsName(currentAnimationName))
        {
            currentProgress = stateInfo.normalizedTime;
            OnProgressUpdated?.Invoke(currentProgress);

            // 완료 체크
            if (currentProgress >= 1.0f && !isCompleted)
            {
                CompleteAnimation();
            }

            // 디버그 로그
            if (showDebugLog && Time.frameCount % 60 == 0)
            {
                Debug.Log($"[PatientModel 자동재생] {currentAnimationName}: {currentProgress:P1}");
            }
        }
    }

    /// <summary>
    /// 사용자 진행도 동기화 모드 업데이트
    /// </summary>
    private void UpdateSyncWithUserMode()
    {
        if (handPosePlayer == null)
        {
            Debug.LogWarning("[PatientModelController] HandPosePlayer가 없습니다!");
            return;
        }

        // 사용자 진행 정보 가져오기
        var userProgress = handPosePlayer.GetUserProgress();
        int leftProgress = userProgress.leftProgress;
        int rightProgress = userProgress.rightProgress;
        bool leftCompleted = userProgress.leftCompleted;
        bool rightCompleted = userProgress.rightCompleted;

        var playbackState = handPosePlayer.GetPlaybackState();
        int totalFrames = playbackState.totalFrames;

        if (totalFrames <= 0) return;

        // 진행도 계산 (0.0 ~ 1.0)
        float userProgressPercentage = 0f;

        if (requireBothHands)
        {
            int minProgress = Mathf.Min(leftProgress, rightProgress);
            userProgressPercentage = (float)(minProgress + 1) / totalFrames;
        }
        else
        {
            float avgProgress = (leftProgress + rightProgress) / 2f;
            userProgressPercentage = (avgProgress + 1f) / totalFrames;
        }

        float targetProgress = Mathf.Clamp01(userProgressPercentage);

        // 진행도 업데이트
        if (useSmoothTransition)
        {
            currentProgress = Mathf.Lerp(currentProgress, targetProgress, Time.deltaTime * transitionSpeed);
        }
        else
        {
            currentProgress = targetProgress;
        }

        // 애니메이션 위치 설정 (수동 제어)
        patientAnimator.Play(currentAnimationHash, animationLayer, currentProgress);
        patientAnimator.speed = 0f;

        OnProgressUpdated?.Invoke(currentProgress);

        // 완료 체크
        if ((leftCompleted && rightCompleted && requireBothHands) ||
            ((leftCompleted || rightCompleted) && !requireBothHands))
        {
            if (!isCompleted)
            {
                CompleteAnimation();
            }
        }

        // 디버그 로그
        if (showDebugLog && Time.frameCount % 60 == 0)
        {
            Debug.Log($"[PatientModel 동기화] L={leftProgress + 1}/{totalFrames}, R={rightProgress + 1}/{totalFrames} → {currentProgress:P1}");
        }
    }

    /// <summary>
    /// 애니메이션 완료
    /// </summary>
    private void CompleteAnimation()
    {
        isCompleted = true;
        isPlaying = false;

        if (showDebugLog)
        {
            Debug.Log($"[PatientModelController] 애니메이션 완료: {currentAnimationName}");
        }

        OnAnimationCompleted?.Invoke();
    }

    // ===== 공개 메서드 =====

    /// <summary>
    /// SubStep 데이터로부터 애니메이션 시작
    /// 핸드 데이터 유무에 따라 자동으로 모드 결정
    /// </summary>
    public void PlayAnimationFromSubStep(SubStepData subStepData)
    {
        if (subStepData == null)
        {
            if (showDebugLog)
            {
                Debug.Log("[PatientModelController] SubStepData가 null입니다.");
            }
            return;
        }

        // 애니메이션이 없으면 아무것도 하지 않음
        if (!subStepData.HasPatientAnimation())
        {
            if (showDebugLog)
            {
                Debug.Log("[PatientModelController] 애니메이션 클립이 없습니다.");
            }
            return;
        }

        // 재생 모드 자동 결정
        AnimationPlayMode playMode = subStepData.GetAnimationPlayMode();
        PlayAnimation(subStepData.patientAnimationClip, playMode);
    }

    /// <summary>
    /// 애니메이션 재생
    /// </summary>
    public void PlayAnimation(string animationName, AnimationPlayMode playMode)
    {
        if (string.IsNullOrEmpty(animationName))
        {
            Debug.LogWarning("[PatientModelController] 애니메이션 이름이 비어있습니다!");
            return;
        }

        if (patientAnimator == null)
        {
            Debug.LogError("[PatientModelController] Animator가 없습니다!");
            return;
        }

        // 상태 초기화
        currentAnimationName = animationName;
        currentAnimationHash = Animator.StringToHash(animationName);
        currentPlayMode = playMode;
        currentProgress = 0f;
        isPlaying = true;
        isCompleted = false;

        // 모드에 따라 재생
        switch (playMode)
        {
            case AnimationPlayMode.AutoPlay:
                // 자동 재생: speed = 1
                patientAnimator.Play(currentAnimationHash, animationLayer, 0f);
                patientAnimator.speed = 1f;

                if (showDebugLog)
                {
                    Debug.Log($"[PatientModelController] 🎬 자동 재생: {animationName}");
                }
                break;

            case AnimationPlayMode.SyncWithUser:
                // 동기화: speed = 0 (수동 제어)
                patientAnimator.Play(currentAnimationHash, animationLayer, 0f);
                patientAnimator.speed = 0f;

                if (showDebugLog)
                {
                    Debug.Log($"[PatientModelController] 🎮 진행도 동기화: {animationName}");
                }
                break;
        }
    }

    /// <summary>
    /// 애니메이션 정지
    /// </summary>
    public void StopAnimation()
    {
        isPlaying = false;

        if (patientAnimator != null)
        {
            patientAnimator.speed = 0f;
        }

        if (showDebugLog)
        {
            Debug.Log("[PatientModelController] 애니메이션 정지");
        }
    }

    /// <summary>
    /// 애니메이션 리셋
    /// </summary>
    public void ResetAnimation()
    {
        currentProgress = 0f;
        isCompleted = false;
        isPlaying = false;

        if (patientAnimator != null && !string.IsNullOrEmpty(currentAnimationName))
        {
            patientAnimator.Play(currentAnimationHash, animationLayer, 0f);
            patientAnimator.speed = 0f;
        }

        if (showDebugLog)
        {
            Debug.Log("[PatientModelController] 애니메이션 리셋");
        }
    }

    /// <summary>
    /// 양손 필요 여부 설정
    /// </summary>
    public void SetRequireBothHands(bool require)
    {
        requireBothHands = require;
    }

    /// <summary>
    /// 현재 애니메이션 정보
    /// </summary>
    public (string name, AnimationPlayMode mode, float progress) GetCurrentAnimationInfo()
    {
        return (currentAnimationName, currentPlayMode, currentProgress);
    }

    // ===== 디버그 메서드 =====

    [ContextMenu("테스트 - 자동 재생")]
    private void TestAutoPlay()
    {
        PlayAnimation("TestAnimation", AnimationPlayMode.AutoPlay);
    }

    [ContextMenu("테스트 - 동기화 재생")]
    private void TestSyncPlay()
    {
        PlayAnimation("TestAnimation", AnimationPlayMode.SyncWithUser);
    }

    [ContextMenu("테스트 - 정지")]
    private void TestStop()
    {
        StopAnimation();
    }

    [ContextMenu("테스트 - 리셋")]
    private void TestReset()
    {
        ResetAnimation();
    }
}