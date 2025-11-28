using System;
using System.Collections.Generic;
using UnityEngine;
using Oculus.Interaction;
using static HandPoseDataLoader;
using static HandPoseComparator;

/// <summary>
/// HandPose 훈련 컨트롤러
/// 재생 + 비교 + 진행 추적을 통합 관리
///
/// 특징:
/// - 모듈식 구조: DataLoader, Comparator 분리
/// - 가이드 손 재생 (루프 가능)
/// - 실시간 포즈 비교
/// - 사용자 진행 추적
/// - 시나리오 시스템과 자동 연동
///
/// 사용법:
/// 1. GameObject에 컴포넌트 추가
/// 2. 재생용 손 모델, 플레이어 손 모델 설정
/// 3. LoadAndStartTraining("등척성운동") 호출
/// 4. OnUserProgressCompleted 이벤트 구독하여 시나리오 진행
/// </summary>
public class HandPoseTrainingController : MonoBehaviour
{
    [Header("=== 재생용 손 모델 ===")]
    [SerializeField] private HandVisual leftHandVisual;
    [SerializeField] private HandVisual rightHandVisual;
    [SerializeField] private HandTransformMapper leftHandMapper;
    [SerializeField] private HandTransformMapper rightHandMapper;

    [Header("=== 플레이어 손 ===")]
    [SerializeField] private HandVisual playerLeftHand;
    [SerializeField] private HandVisual playerRightHand;

    [Header("=== OpenXR Root (자동 탐색) ===")]
    [SerializeField] private Transform leftOpenXRRoot;       // 재생 손 모델의 OpenXRRoot
    [SerializeField] private Transform rightOpenXRRoot;      // 재생 손 모델의 OpenXRRoot
    [SerializeField] private Transform playerLeftOpenXRRoot;  // 플레이어 손의 OpenXRRoot
    [SerializeField] private Transform playerRightOpenXRRoot; // 플레이어 손의 OpenXRRoot

    [Header("=== 재생 설정 ===")]
    [SerializeField] private float playbackInterval = 0.1f;
    [SerializeField] private bool enableLoopPlayback = true;
    [SerializeField] [Range(0.1f, 1.0f)] private float playbackLengthRatio = 1.0f;
    [SerializeField] private bool showReplayHands = true;
    [SerializeField] private float replayHandAlpha = 0.5f;
    [SerializeField] private Color replayHandColor = new Color(0.3f, 0.5f, 1f, 0.5f);

    [Header("=== 비교 설정 ===")]
    [SerializeField] private float positionThreshold = 0.05f;      // 5cm
    [SerializeField] private float rotationThreshold = 15f;         // 15도
    [SerializeField] private float similarityPercentage = 0.7f;     // 70%
    [SerializeField] private bool compareHandPosition = true;
    [SerializeField] private float handPositionThreshold = 0.03f;   // 3cm (더 엄격하게 조정)
    [SerializeField] private bool compareHandRotation = true;
    [SerializeField] private float handRotationThreshold = 20f;     // 20도
    [SerializeField] private float comparisonInterval = 0.5f;       // 비교 간격
    [SerializeField] private int consecutiveFramesRequired = 5;     // 연속 프레임 요구 수 (통과 속도 조절)

    [Header("=== 진행 추적 설정 ===")]
    [SerializeField] [Range(0.0f, 1.0f)] private float progressThreshold = 0.8f;

    [Header("=== 기준점 ===")]
    [SerializeField] private Transform referencePoint;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = true;

    [Header("=== 디버그 모드 (수동 비교 진행) ===")]
    [Tooltip("활성화 시 자동 비교 중지, 버튼으로만 비교 프레임 진행")]
    [SerializeField] private bool debugMode = false;
    [Tooltip("다음 비교 프레임으로 진행할 토글 버튼 (가이드 재생과 무관)")]
    [SerializeField] private UnityEngine.UI.Toggle debugNextFrameButton;

    [Header("=== 피드백 UI ===")]
    [Tooltip("손 포즈 유사도 피드백 UI (옵션)")]
    [SerializeField] private HandFeedbackUI handFeedbackUI;

    [Header("=== 핸드 모델 색상 피드백 ===")]
    [Tooltip("유사도에 따라 가이드 핸드 색상 변경")]
    [SerializeField] private bool enableHandColorFeedback = true;
    [Tooltip("낮은 유사도 색상 (빨강)")]
    [SerializeField] private Color lowSimilarityColor = new Color(1f, 0.2f, 0.2f, 0.5f);
    [Tooltip("중간 유사도 색상 (노랑)")]
    [SerializeField] private Color mediumSimilarityColor = new Color(1f, 1f, 0.2f, 0.5f);
    [Tooltip("높은 유사도 색상 (초록)")]
    [SerializeField] private Color highSimilarityColor = new Color(0.2f, 1f, 0.2f, 0.5f);
    [Tooltip("낮음→중간 임계값")]
    [SerializeField][Range(0f, 1f)] private float lowToMediumThreshold = 0.4f;
    [Tooltip("중간→높음 임계값")]
    [SerializeField][Range(0f, 1f)] private float mediumToHighThreshold = 0.7f;

    [Header("=== 추나 시술 평가 ===")]
    [Tooltip("추나 시술 평가 시스템 (옵션)")]
    [SerializeField] private TunaEvaluator tunaEvaluator;
    [Tooltip("평가 활성화 여부")]
    [SerializeField] private bool enableTunaEvaluation = false;
    [Tooltip("결과 UI (옵션)")]
    [SerializeField] private TunaResultUI tunaResultUI;

    // 모듈
    private HandPoseDataLoader dataLoader;
    private HandPoseComparator comparator;

    // 데이터
    private List<PoseFrame> loadedFrames = new List<PoseFrame>();
    private string currentFileName = "";

    // 재생 상태
    private bool isLeftPlaying = false;
    private bool isRightPlaying = false;
    private int currentLeftPlaybackIndex = 0;
    private int currentRightPlaybackIndex = 0;
    private float leftElapsedTime = 0f;
    private float rightElapsedTime = 0f;

    // 비교 상태
    private float comparisonElapsedTime = 0f;
    private int userLeftProgress = 0;
    private int userRightProgress = 0;
    private bool userCompletedLeft = false;
    private bool userCompletedRight = false;
    private bool hasNotifiedUserCompletion = false;

    // 모델 타입
    private bool useLeftMapper = false;
    private bool useRightMapper = false;

    // Quest 최적화: 캐시된 값
    private int cachedMaxFrameIndex = 0;

    // 이벤트
    public event Action OnUserProgressCompleted;
    public event Action OnSequenceCompleted;
    public event Action OnPlaybackStarted;
    public event Action<float> OnPlaybackProgress;

    void Awake()
    {
        // 모듈 초기화
        dataLoader = new HandPoseDataLoader();
        comparator = new HandPoseComparator();

        // Comparator 설정
        comparator.SetThresholds(positionThreshold, rotationThreshold, similarityPercentage);
        comparator.SetHandComparisonSettings(compareHandPosition, handPositionThreshold, compareHandRotation, handRotationThreshold);
        comparator.SetReferencePoint(referencePoint);

        // 연속 프레임 요구 수 설정
        var settings = comparator.GetSettings();
        settings.consecutiveFramesRequired = consecutiveFramesRequired;

        // Mapper 사용 여부 확인
        useLeftMapper = (leftHandMapper != null);
        useRightMapper = (rightHandMapper != null);
    }

    void Start()
    {
        // OpenXRRoot 자동 탐색
        FindOpenXRRoots();

        // Comparator에 플레이어 손의 OpenXRRoot 설정 (실제 손 위치 비교용)
        comparator.SetOpenXRRoots(playerLeftOpenXRRoot, playerRightOpenXRRoot);

        // 재생 손 설정
        SetupReplayHands();

        // 디버그 모드 버튼 설정
        SetupDebugButton();
    }

    void Update()
    {
        if (loadedFrames.Count == 0)
            return;

        // 가이드 재생은 항상 실행 (디버그 모드와 무관)
        UpdatePlayback();

        // 디버그 모드가 아닐 때만 자동 비교
        if (!debugMode)
        {
            // 비교 업데이트
            UpdateComparison();
        }
        // 디버그 모드: 비교 중지, 버튼으로만 진행
    }

    /// <summary>
    /// CSV 파일 로드 및 훈련 시작
    /// ScenarioActionHandler에서 호출
    /// </summary>
    public void LoadAndStartTraining(string csvFileName)
    {
        if (showDebugLogs)
            Debug.Log($"<color=cyan>[TrainingController] CSV 로드 시작: {csvFileName}</color>");

        // 상태 초기화
        ResetState();

        // CSV 로드
        var result = dataLoader.LoadFromResources($"HandPoseData/{csvFileName}");

        if (!result.success)
        {
            Debug.LogError($"<color=red>[TrainingController] CSV 로드 실패: {result.errorMessage}</color>");
            return;
        }

        loadedFrames = result.frames;
        currentFileName = csvFileName;

        // 재생 시작
        StartPlayback();

        // 추나 평가 시작
        if (enableTunaEvaluation && tunaEvaluator != null)
        {
            tunaEvaluator.StartEvaluation();
            if (showDebugLogs)
                Debug.Log("[TrainingController] 추나 시술 평가 시작");
        }

        if (showDebugLogs)
            Debug.Log($"<color=green>[TrainingController] ✓ 훈련 시작 - {loadedFrames.Count} 프레임, {result.totalDuration:F2}초</color>");
    }

    /// <summary>
    /// 재생 시작
    /// </summary>
    private void StartPlayback()
    {
        isLeftPlaying = true;
        isRightPlaying = true;
        currentLeftPlaybackIndex = 0;
        currentRightPlaybackIndex = 0;
        leftElapsedTime = 0f;
        rightElapsedTime = 0f;

        // Quest 최적화: maxFrameIndex 캐싱 (매 프레임 계산 방지)
        cachedMaxFrameIndex = Mathf.CeilToInt(loadedFrames.Count * playbackLengthRatio);

        // 첫 프레임 적용
        ApplyLeftHandFrame();
        ApplyRightHandFrame();

        OnPlaybackStarted?.Invoke();
    }

    /// <summary>
    /// 재생 업데이트 (Quest 최적화: deltaTime 캐싱, maxFrameIndex 캐싱)
    /// </summary>
    private void UpdatePlayback()
    {
        // Quest 최적화: deltaTime 한 번만 호출
        float deltaTime = Time.deltaTime;

        // 왼손 재생
        if (isLeftPlaying)
        {
            leftElapsedTime += deltaTime;
            if (leftElapsedTime >= playbackInterval)
            {
                leftElapsedTime = 0f;

                if (currentLeftPlaybackIndex < cachedMaxFrameIndex)
                {
                    ApplyLeftHandFrame();
                    currentLeftPlaybackIndex++;
                }

                // 루프 처리
                if (currentLeftPlaybackIndex >= cachedMaxFrameIndex)
                {
                    if (enableLoopPlayback)
                    {
                        currentLeftPlaybackIndex = 0;
                    }
                    else
                    {
                        isLeftPlaying = false;
                        if (showDebugLogs)
                            Debug.Log("<color=cyan>[TrainingController] 왼손 재생 완료</color>");
                    }
                }
            }
        }

        // 오른손 재생
        if (isRightPlaying)
        {
            rightElapsedTime += deltaTime;
            if (rightElapsedTime >= playbackInterval)
            {
                rightElapsedTime = 0f;

                if (currentRightPlaybackIndex < cachedMaxFrameIndex)
                {
                    ApplyRightHandFrame();
                    currentRightPlaybackIndex++;
                }

                // 루프 처리
                if (currentRightPlaybackIndex >= cachedMaxFrameIndex)
                {
                    if (enableLoopPlayback)
                    {
                        currentRightPlaybackIndex = 0;
                    }
                    else
                    {
                        isRightPlaying = false;
                        if (showDebugLogs)
                            Debug.Log("<color=cyan>[TrainingController] 오른손 재생 완료</color>");
                    }
                }
            }
        }

        // 진행률 이벤트
        if (isLeftPlaying || isRightPlaying)
        {
            float progress = GetOverallProgress();
            OnPlaybackProgress?.Invoke(progress);
        }
    }

    /// <summary>
    /// 비교 업데이트 (양손 모두 통과해야 진행)
    /// </summary>
    private void UpdateComparison()
    {
        comparisonElapsedTime += Time.deltaTime;

        if (comparisonElapsedTime < comparisonInterval)
            return;

        comparisonElapsedTime = 0f;

        // 양손이 같은 프레임에 있는지 확인 (동기화)
        int currentProgress = Mathf.Min(userLeftProgress, userRightProgress);

        // 양손 비교 (현재 공통 프레임과 비교)
        bool leftPassed = false;
        bool rightPassed = false;
        float leftSimilarity = 0f;
        float rightSimilarity = 0f;
        int leftConsecutive = 0;
        int rightConsecutive = 0;
        HandPoseComparator.SimilarityResult comparisonResult = new HandPoseComparator.SimilarityResult();

        if (currentProgress < cachedMaxFrameIndex)
        {
            PoseFrame targetFrame = loadedFrames[currentProgress];

            // 왼손 비교
            if (!userCompletedLeft)
            {
                var leftResult = comparator.CompareLeftPose(playerLeftHand, targetFrame, currentProgress);
                leftSimilarity = leftResult.leftHandSimilarity;
                leftPassed = leftResult.leftHandPassed && leftResult.leftHandPositionPassed;
                comparisonResult.leftHandSimilarity = leftResult.leftHandSimilarity;
                comparisonResult.leftHandPositionError = leftResult.leftHandPositionError;
                comparisonResult.leftHandRotationError = leftResult.leftHandRotationError;
                comparisonResult.leftHandPassed = leftResult.leftHandPassed;
                comparisonResult.leftHandPositionPassed = leftResult.leftHandPositionPassed;
            }

            // 오른손 비교
            if (!userCompletedRight)
            {
                var rightResult = comparator.CompareRightPose(playerRightHand, targetFrame, currentProgress);
                rightSimilarity = rightResult.rightHandSimilarity;
                rightPassed = rightResult.rightHandPassed && rightResult.rightHandPositionPassed;
                comparisonResult.rightHandSimilarity = rightResult.rightHandSimilarity;
                comparisonResult.rightHandPositionError = rightResult.rightHandPositionError;
                comparisonResult.rightHandRotationError = rightResult.rightHandRotationError;
                comparisonResult.rightHandPassed = rightResult.rightHandPassed;
                comparisonResult.rightHandPositionPassed = rightResult.rightHandPositionPassed;
            }

            var (leftCon, rightCon) = comparator.GetConsecutiveCounts();
            leftConsecutive = leftCon;
            rightConsecutive = rightCon;

            // 추나 평가 시스템에 전달
            if (enableTunaEvaluation && tunaEvaluator != null)
            {
                tunaEvaluator.EvaluateFrame(currentProgress, targetFrame, comparisonResult);
            }
        }

        // UI 업데이트 (양손 모두 항상 업데이트)
        if (handFeedbackUI != null)
        {
            handFeedbackUI.UpdateLeftHandInfo(
                leftSimilarity,
                currentProgress,
                cachedMaxFrameIndex,
                leftConsecutive,
                consecutiveFramesRequired
            );

            handFeedbackUI.UpdateRightHandInfo(
                rightSimilarity,
                currentProgress,
                cachedMaxFrameIndex,
                rightConsecutive,
                consecutiveFramesRequired
            );
        }

        // 유사도에 따라 플레이어 핸드 색상 변경 (양손 모두)
        if (enableHandColorFeedback)
        {
            if (playerLeftHand != null)
            {
                Color leftColor = GetColorForSimilarity(leftSimilarity);
                SetPlayerHandColor(playerLeftHand, leftColor);
            }

            if (playerRightHand != null)
            {
                Color rightColor = GetColorForSimilarity(rightSimilarity);
                SetPlayerHandColor(playerRightHand, rightColor);
            }
        }

        // ✅ 양손 모두 통과했을 때만 진행
        if (leftPassed && rightPassed)
        {
            userLeftProgress++;
            userRightProgress++;

            if (showDebugLogs)
                Debug.Log($"<color=cyan>[양손 통과] 프레임 {currentProgress} 완료 → 다음: {userLeftProgress}/{cachedMaxFrameIndex}</color>");

            // 완료 체크
            if (userLeftProgress >= cachedMaxFrameIndex)
            {
                userCompletedLeft = true;
            }

            if (userRightProgress >= cachedMaxFrameIndex)
            {
                userCompletedRight = true;
            }

            if (userCompletedLeft && userCompletedRight)
            {
                if (showDebugLogs)
                    Debug.Log($"<color=green>✓✓ 양손 모두 완료!</color>");
                CheckUserCompletion();
            }
        }
        else
        {
            // 통과 실패 로그
            if (showDebugLogs && currentProgress % 2 == 0)
            {
                string leftStatus = leftPassed ? "✓" : "✗";
                string rightStatus = rightPassed ? "✓" : "✗";
                Debug.Log($"<color=yellow>[진행 대기] 프레임 {currentProgress} - 왼손:{leftStatus}({leftSimilarity:P0}), 오른손:{rightStatus}({rightSimilarity:P0})</color>");
            }
        }
    }

    /// <summary>
    /// 사용자 완료 체크
    /// </summary>
    private void CheckUserCompletion()
    {
        if (hasNotifiedUserCompletion)
            return;

        if (userCompletedLeft && userCompletedRight)
        {
            hasNotifiedUserCompletion = true;
            if (showDebugLogs)
            {
                Debug.Log("<color=green>======================</color>");
                Debug.Log("<color=green>✓✓ 사용자 동작 완료! ✓✓</color>");
                Debug.Log($"<color=green>  왼손: {userLeftProgress + 1}프레임 도달</color>");
                Debug.Log($"<color=green>  오른손: {userRightProgress + 1}프레임 도달</color>");
                Debug.Log("<color=green>======================</color>");
            }

            // 추나 평가 종료 및 결과 출력
            if (enableTunaEvaluation && tunaEvaluator != null)
            {
                var evaluationResult = tunaEvaluator.StopEvaluation();

                // 결과 리포트 출력
                Debug.Log("<color=cyan>========== 추나 시술 평가 결과 ==========</color>");
                Debug.Log($"<color=cyan>{evaluationResult.GenerateReport()}</color>");

                // 결과 UI 표시
                if (tunaResultUI != null)
                {
                    tunaResultUI.ShowResult(evaluationResult);
                }
            }

            OnUserProgressCompleted?.Invoke();
            OnSequenceCompleted?.Invoke();
        }
    }

    /// <summary>
    /// 왼손 프레임 적용
    /// </summary>
    private void ApplyLeftHandFrame()
    {
        if (currentLeftPlaybackIndex >= loadedFrames.Count)
            return;

        PoseFrame frame = loadedFrames[currentLeftPlaybackIndex];

        // Root Transform 계산
        Vector3 targetRootPos = frame.leftRootPosition;
        Quaternion targetRootRot = frame.leftRootRotation;

        if (referencePoint != null)
        {
            targetRootPos = referencePoint.position + frame.leftRootPosition;
            targetRootRot = referencePoint.rotation * frame.leftRootRotation;
        }

        // OpenXRRoot 적용 (비교용)
        if (leftOpenXRRoot != null)
        {
            leftOpenXRRoot.position = targetRootPos;
            leftOpenXRRoot.rotation = targetRootRot;
        }

        // 재생용 손 모델 적용
        if (useLeftMapper && leftHandMapper != null)
        {
            if (leftHandMapper.Root != null)
            {
                leftHandMapper.Root.position = targetRootPos;
                leftHandMapper.Root.rotation = targetRootRot;
            }

            foreach (var kvp in frame.leftLocalPoses)
            {
                leftHandMapper.SetJointLocalPose(kvp.Key, kvp.Value.position, kvp.Value.rotation);
            }
        }
        else if (leftHandVisual != null)
        {
            if (leftHandVisual.Root != null)
            {
                leftHandVisual.Root.position = targetRootPos;
                leftHandVisual.Root.rotation = targetRootRot;
            }

            ApplyPosesToJoints(leftHandVisual, frame.leftLocalPoses);
        }
    }

    /// <summary>
    /// 오른손 프레임 적용
    /// </summary>
    private void ApplyRightHandFrame()
    {
        if (currentRightPlaybackIndex >= loadedFrames.Count)
            return;

        PoseFrame frame = loadedFrames[currentRightPlaybackIndex];

        // Root Transform 계산
        Vector3 targetRootPos = frame.rightRootPosition;
        Quaternion targetRootRot = frame.rightRootRotation;

        if (referencePoint != null)
        {
            targetRootPos = referencePoint.position + frame.rightRootPosition;
            targetRootRot = referencePoint.rotation * frame.rightRootRotation;
        }

        // OpenXRRoot 적용 (비교용)
        if (rightOpenXRRoot != null)
        {
            rightOpenXRRoot.position = targetRootPos;
            rightOpenXRRoot.rotation = targetRootRot;
        }

        // 재생용 손 모델 적용
        if (useRightMapper && rightHandMapper != null)
        {
            if (rightHandMapper.Root != null)
            {
                rightHandMapper.Root.position = targetRootPos;
                rightHandMapper.Root.rotation = targetRootRot;
            }

            foreach (var kvp in frame.rightLocalPoses)
            {
                rightHandMapper.SetJointLocalPose(kvp.Key, kvp.Value.position, kvp.Value.rotation);
            }
        }
        else if (rightHandVisual != null)
        {
            if (rightHandVisual.Root != null)
            {
                rightHandVisual.Root.position = targetRootPos;
                rightHandVisual.Root.rotation = targetRootRot;
            }

            ApplyPosesToJoints(rightHandVisual, frame.rightLocalPoses);
        }
    }

    /// <summary>
    /// 조인트에 포즈 적용
    /// </summary>
    private void ApplyPosesToJoints(HandVisual handVisual, Dictionary<int, PoseData> poses)
    {
        if (handVisual == null)
            return;

        for (int i = 0; i < handVisual.Joints.Count; i++)
        {
            if (poses.TryGetValue(i, out PoseData poseData) && handVisual.Joints[i] != null)
            {
                handVisual.Joints[i].localPosition = poseData.position;
                handVisual.Joints[i].localRotation = poseData.rotation;
            }
        }
    }

    /// <summary>
    /// OpenXRRoot 자동 탐색
    /// </summary>
    private void FindOpenXRRoots()
    {
        // 재생 손 모델의 왼손 OpenXRRoot 찾기
        if (leftOpenXRRoot == null)
        {
            Transform searchFrom = null;
            if (leftHandMapper != null)
                searchFrom = leftHandMapper.transform;
            else if (leftHandVisual != null)
                searchFrom = leftHandVisual.transform;

            if (searchFrom != null)
            {
                Transform parent = searchFrom.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("OpenXR") || parent.name.Contains("LeftHand"))
                    {
                        leftOpenXRRoot = parent;
                        if (showDebugLogs)
                            Debug.Log($"[TrainingController] 재생 왼손 OpenXRRoot 찾음: {leftOpenXRRoot.name}");
                        break;
                    }
                    parent = parent.parent;
                }
            }
        }

        // 재생 손 모델의 오른손 OpenXRRoot 찾기
        if (rightOpenXRRoot == null)
        {
            Transform searchFrom = null;
            if (rightHandMapper != null)
                searchFrom = rightHandMapper.transform;
            else if (rightHandVisual != null)
                searchFrom = rightHandVisual.transform;

            if (searchFrom != null)
            {
                Transform parent = searchFrom.parent;
                while (parent != null)
                {
                    if (parent.name.Contains("OpenXR") || parent.name.Contains("RightHand"))
                    {
                        rightOpenXRRoot = parent;
                        if (showDebugLogs)
                            Debug.Log($"[TrainingController] 재생 오른손 OpenXRRoot 찾음: {rightOpenXRRoot.name}");
                        break;
                    }
                    parent = parent.parent;
                }
            }
        }

        // 플레이어 손의 왼손 OpenXRRoot 찾기
        if (playerLeftOpenXRRoot == null && playerLeftHand != null)
        {
            Transform searchFrom = playerLeftHand.transform;
            Transform parent = searchFrom.parent;
            while (parent != null)
            {
                if (parent.name.Contains("OpenXR") || parent.name.Contains("LeftHand"))
                {
                    playerLeftOpenXRRoot = parent;
                    if (showDebugLogs)
                        Debug.Log($"[TrainingController] 플레이어 왼손 OpenXRRoot 찾음: {playerLeftOpenXRRoot.name}");
                    break;
                }
                parent = parent.parent;
            }
        }

        // 플레이어 손의 오른손 OpenXRRoot 찾기
        if (playerRightOpenXRRoot == null && playerRightHand != null)
        {
            Transform searchFrom = playerRightHand.transform;
            Transform parent = searchFrom.parent;
            while (parent != null)
            {
                if (parent.name.Contains("OpenXR") || parent.name.Contains("RightHand"))
                {
                    playerRightOpenXRRoot = parent;
                    if (showDebugLogs)
                        Debug.Log($"[TrainingController] 플레이어 오른손 OpenXRRoot 찾음: {playerRightOpenXRRoot.name}");
                    break;
                }
                parent = parent.parent;
            }
        }
    }

    /// <summary>
    /// 재생 손 설정
    /// </summary>
    private void SetupReplayHands()
    {
        if (useLeftMapper && leftHandMapper != null)
        {
            leftHandMapper.SetVisible(showReplayHands);
            leftHandMapper.SetColorAndAlpha(replayHandColor, replayHandAlpha);
        }
        else if (leftHandVisual != null)
        {
            SetupReplayHandVisual(leftHandVisual);
        }

        if (useRightMapper && rightHandMapper != null)
        {
            rightHandMapper.SetVisible(showReplayHands);
            rightHandMapper.SetColorAndAlpha(replayHandColor, replayHandAlpha);
        }
        else if (rightHandVisual != null)
        {
            SetupReplayHandVisual(rightHandVisual);
        }
    }

    /// <summary>
    /// 재생 손 비주얼 설정
    /// </summary>
    private void SetupReplayHandVisual(HandVisual handVisual)
    {
        if (handVisual == null)
            return;

        SkinnedMeshRenderer[] renderers = handVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true);

        foreach (var renderer in renderers)
        {
            Material mat = new Material(renderer.material);
            mat.SetFloat("_Mode", 3);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;

            if (mat.HasProperty("_Color"))
            {
                Color finalColor = replayHandColor;
                finalColor.a = replayHandAlpha;
                mat.color = finalColor;
            }

            renderer.material = mat;
            renderer.enabled = showReplayHands;
        }
    }

    /// <summary>
    /// 상태 초기화
    /// </summary>
    private void ResetState()
    {
        loadedFrames.Clear();
        isLeftPlaying = false;
        isRightPlaying = false;
        currentLeftPlaybackIndex = 0;
        currentRightPlaybackIndex = 0;
        leftElapsedTime = 0f;
        rightElapsedTime = 0f;
        comparisonElapsedTime = 0f;
        userLeftProgress = 0;
        userRightProgress = 0;
        userCompletedLeft = false;
        userCompletedRight = false;
        hasNotifiedUserCompletion = false;

        // 연속 프레임 카운터 리셋
        if (comparator != null)
        {
            comparator.ResetConsecutiveCounters();
        }
    }

    /// <summary>
    /// 디버그 모드 버튼 설정
    /// </summary>
    private void SetupDebugButton()
    {
        if (debugNextFrameButton != null)
        {
            debugNextFrameButton.onValueChanged.AddListener(OnDebugNextFrameButtonClick);
            debugNextFrameButton.isOn = false; // 초기값
            if (showDebugLogs)
                Debug.Log("[TrainingController] 디버그 다음 프레임 버튼 연결 완료");
        }
    }

    /// <summary>
    /// 디버그 모드: 다음 비교 프레임 버튼 클릭
    /// </summary>
    private void OnDebugNextFrameButtonClick(bool isOn)
    {
        if (!debugMode || !isOn)
            return;

        // 다음 비교 프레임으로 수동 진행 (가이드 재생과 무관)
        AdvanceUserProgress();

        // 토글 자동 리셋 (버튼처럼 동작)
        StartCoroutine(ResetDebugButton());

        if (showDebugLogs)
            Debug.Log($"[TrainingController] 디버그: 다음 비교 프레임으로 진행 (userProgress - L:{userLeftProgress}, R:{userRightProgress})");
    }

    /// <summary>
    /// 사용자 진행 프레임을 수동으로 증가 (디버그용, 양손 함께 진행)
    /// </summary>
    private void AdvanceUserProgress()
    {
        if (loadedFrames.Count == 0)
            return;

        // 양손 진행 프레임 함께 증가 (동기화)
        if ((!userCompletedLeft || !userCompletedRight) &&
            (userLeftProgress < cachedMaxFrameIndex || userRightProgress < cachedMaxFrameIndex))
        {
            if (!userCompletedLeft && userLeftProgress < cachedMaxFrameIndex)
            {
                userLeftProgress++;
            }

            if (!userCompletedRight && userRightProgress < cachedMaxFrameIndex)
            {
                userRightProgress++;
            }

            if (showDebugLogs)
                Debug.Log($"[TrainingController] 디버그 진행: {Mathf.Min(userLeftProgress, userRightProgress)}/{cachedMaxFrameIndex}");
        }

        // 완료 체크
        if (userLeftProgress >= cachedMaxFrameIndex)
        {
            userCompletedLeft = true;
        }

        if (userRightProgress >= cachedMaxFrameIndex)
        {
            userCompletedRight = true;
        }

        CheckUserCompletion();
    }

    /// <summary>
    /// 디버그 버튼 리셋 코루틴
    /// </summary>
    private System.Collections.IEnumerator ResetDebugButton()
    {
        yield return null;
        if (debugNextFrameButton != null)
        {
            debugNextFrameButton.isOn = false;
        }
    }

    /// <summary>
    /// 재생 중지
    /// </summary>
    public void StopPlayback()
    {
        isLeftPlaying = false;
        isRightPlaying = false;
        if (showDebugLogs)
            Debug.Log("[TrainingController] 재생 중지");
    }

    /// <summary>
    /// 전체 진행률 가져오기
    /// </summary>
    public float GetOverallProgress()
    {
        if (loadedFrames.Count == 0)
            return 0f;

        float leftProgress = (float)currentLeftPlaybackIndex / loadedFrames.Count;
        float rightProgress = (float)currentRightPlaybackIndex / loadedFrames.Count;

        return Mathf.Max(leftProgress, rightProgress);
    }

    /// <summary>
    /// 사용자 진행률 가져오기
    /// </summary>
    public (int leftProgress, int rightProgress, bool leftCompleted, bool rightCompleted) GetUserProgress()
    {
        return (userLeftProgress, userRightProgress, userCompletedLeft, userCompletedRight);
    }

    /// <summary>
    /// 재생 상태 가져오기
    /// </summary>
    public (bool leftPlaying, bool rightPlaying, int leftFrame, int rightFrame, int totalFrames) GetPlaybackState()
    {
        return (isLeftPlaying, isRightPlaying, currentLeftPlaybackIndex, currentRightPlaybackIndex, loadedFrames.Count);
    }

    /// <summary>
    /// 유사도에 따른 색상 계산 (HandFeedbackUI와 동일한 로직)
    /// </summary>
    /// <param name="similarity">유사도 (0~1)</param>
    /// <returns>계산된 색상</returns>
    private Color GetColorForSimilarity(float similarity)
    {
        similarity = Mathf.Clamp01(similarity);

        if (similarity < lowToMediumThreshold)
        {
            // 빨강 → 노랑 보간 (0 ~ lowToMediumThreshold)
            float t = similarity / lowToMediumThreshold;
            return Color.Lerp(lowSimilarityColor, mediumSimilarityColor, t);
        }
        else if (similarity < mediumToHighThreshold)
        {
            // 노랑 → 초록 보간 (lowToMediumThreshold ~ mediumToHighThreshold)
            float t = (similarity - lowToMediumThreshold) / (mediumToHighThreshold - lowToMediumThreshold);
            return Color.Lerp(mediumSimilarityColor, highSimilarityColor, t);
        }
        else
        {
            // 초록 유지 (mediumToHighThreshold ~ 1)
            return highSimilarityColor;
        }
    }

    /// <summary>
    /// 플레이어 손에 색상 적용 (OculusHand 쉐이더 지원)
    /// </summary>
    /// <param name="handVisual">플레이어 HandVisual</param>
    /// <param name="color">적용할 색상</param>
    private void SetPlayerHandColor(HandVisual handVisual, Color color)
    {
        if (handVisual == null)
            return;

        SkinnedMeshRenderer[] renderers = handVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true);

        foreach (var renderer in renderers)
        {
            if (renderer == null)
                continue;

            Material[] materials = renderer.materials;

            for (int i = 0; i < materials.Length; i++)
            {
                Material mat = materials[i];
                if (mat == null)
                    continue;

                // 색상 적용 (다양한 쉐이더 지원)
                if (mat.HasProperty("_Color"))
                {
                    mat.color = color;
                }
                else if (mat.HasProperty("_BaseColor"))
                {
                    mat.SetColor("_BaseColor", color);
                }

                // OculusHand 쉐이더 지원
                if (mat.HasProperty("_ColorTop"))
                {
                    mat.SetColor("_ColorTop", color);
                }
                if (mat.HasProperty("_ColorBottom"))
                {
                    mat.SetColor("_ColorBottom", color);
                }
                if (mat.HasProperty("_GlowColor"))
                {
                    // Glow는 약간 밝게
                    Color glowColor = color * 1.5f;
                    glowColor.a = color.a;
                    mat.SetColor("_GlowColor", glowColor);
                }
            }

            renderer.materials = materials;
        }
    }
}
