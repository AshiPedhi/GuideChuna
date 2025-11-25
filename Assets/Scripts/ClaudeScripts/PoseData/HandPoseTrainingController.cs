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
    [SerializeField] private Transform leftOpenXRRoot;
    [SerializeField] private Transform rightOpenXRRoot;

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
    [SerializeField] private float handPositionThreshold = 0.1f;    // 10cm
    [SerializeField] private bool compareHandRotation = true;
    [SerializeField] private float handRotationThreshold = 20f;     // 20도
    [SerializeField] private float comparisonInterval = 0.5f;       // 비교 간격

    [Header("=== 진행 추적 설정 ===")]
    [SerializeField] [Range(0.0f, 1.0f)] private float progressThreshold = 0.8f;

    [Header("=== 기준점 ===")]
    [SerializeField] private Transform referencePoint;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = true;

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

        // Mapper 사용 여부 확인
        useLeftMapper = (leftHandMapper != null);
        useRightMapper = (rightHandMapper != null);
    }

    void Start()
    {
        // OpenXRRoot 자동 탐색
        FindOpenXRRoots();

        // Comparator에 OpenXRRoot 설정
        comparator.SetOpenXRRoots(leftOpenXRRoot, rightOpenXRRoot);

        // 재생 손 설정
        SetupReplayHands();
    }

    void Update()
    {
        if (loadedFrames.Count == 0)
            return;

        // 재생 업데이트
        UpdatePlayback();

        // 비교 업데이트
        UpdateComparison();
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
    /// 비교 업데이트 (Quest 최적화: Time.deltaTime 캐싱, maxFrameIndex 캐싱)
    /// </summary>
    private void UpdateComparison()
    {
        comparisonElapsedTime += Time.deltaTime;

        if (comparisonElapsedTime < comparisonInterval)
            return;

        comparisonElapsedTime = 0f;

        // 왼손 비교
        if (!userCompletedLeft && currentLeftPlaybackIndex < cachedMaxFrameIndex)
        {
            PoseFrame currentFrame = loadedFrames[currentLeftPlaybackIndex];
            var result = comparator.CompareLeftPose(playerLeftHand, currentFrame, currentLeftPlaybackIndex);

            if (result.leftHandPassed && result.leftHandPositionPassed)
            {
                if (currentLeftPlaybackIndex > userLeftProgress)
                {
                    userLeftProgress = currentLeftPlaybackIndex;
                }

                if (userLeftProgress >= cachedMaxFrameIndex - 1)
                {
                    userCompletedLeft = true;
                    if (showDebugLogs)
                        Debug.Log($"<color=green>✓ 왼손 사용자 동작 완료! ({userLeftProgress + 1}/{cachedMaxFrameIndex} 프레임)</color>");
                    CheckUserCompletion();
                }
            }
        }

        // 오른손 비교
        if (!userCompletedRight && currentRightPlaybackIndex < cachedMaxFrameIndex)
        {
            PoseFrame currentFrame = loadedFrames[currentRightPlaybackIndex];
            var result = comparator.CompareRightPose(playerRightHand, currentFrame, currentRightPlaybackIndex);

            if (result.rightHandPassed && result.rightHandPositionPassed)
            {
                if (currentRightPlaybackIndex > userRightProgress)
                {
                    userRightProgress = currentRightPlaybackIndex;
                }

                if (userRightProgress >= cachedMaxFrameIndex - 1)
                {
                    userCompletedRight = true;
                    if (showDebugLogs)
                        Debug.Log($"<color=green>✓ 오른손 사용자 동작 완료! ({userRightProgress + 1}/{cachedMaxFrameIndex} 프레임)</color>");
                    CheckUserCompletion();
                }
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
        // 왼손 OpenXRRoot 찾기
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
                            Debug.Log($"[TrainingController] 왼손 OpenXRRoot 찾음: {leftOpenXRRoot.name}");
                        break;
                    }
                    parent = parent.parent;
                }
            }
        }

        // 오른손 OpenXRRoot 찾기
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
                            Debug.Log($"[TrainingController] 오른손 OpenXRRoot 찾음: {rightOpenXRRoot.name}");
                        break;
                    }
                    parent = parent.parent;
                }
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
            leftHandMapper.SetAlpha(replayHandAlpha);
        }
        else if (leftHandVisual != null)
        {
            SetupReplayHandVisual(leftHandVisual);
        }

        if (useRightMapper && rightHandMapper != null)
        {
            rightHandMapper.SetVisible(showReplayHands);
            rightHandMapper.SetAlpha(replayHandAlpha);
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
}
