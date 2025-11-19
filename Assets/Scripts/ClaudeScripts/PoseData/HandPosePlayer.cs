using System;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using Oculus.Interaction.Input;
using Oculus.Interaction;
using System.Globalization;

/// <summary>
/// HandPosePlayer - 플레이어는 HandVisual, 재생 모델만 선택적으로 HandTransformMapper 지원
/// OpenXRRoot Transform 재생 지원
/// </summary>
public class HandPosePlayer : MonoBehaviour
{
    [Header("=== 재생용 손 모델 (둘 중 하나 선택) ===")]
    [SerializeField]
    private HandVisual leftHandVisual;  // 옵션1: HandVisual 사용

    [SerializeField]
    private HandVisual rightHandVisual;  // 옵션1: HandVisual 사용

    [SerializeField]
    private HandTransformMapper leftHandMapper;  // 옵션2: Mapper 사용

    [SerializeField]
    private HandTransformMapper rightHandMapper;  // 옵션2: Mapper 사용

    [Header("=== OpenXR Root (자동 탐색) ===")]
    [SerializeField]
    private Transform leftOpenXRRoot;

    [SerializeField]
    private Transform rightOpenXRRoot;

    [Header("=== 플레이어 손 (비교용 - HandVisual만 사용) ===")]
    [SerializeField]
    private HandVisual playerLeftHand;  // 항상 HandVisual

    [SerializeField]
    private HandVisual playerRightHand;  // 항상 HandVisual

    [SerializeField]
    private float playbackInterval = 0.1f;

    [Header("동작 비교 설정")]
    [SerializeField]
    private float positionThreshold = 0.05f;

    [SerializeField]
    private float rotationThreshold = 15f;

    [SerializeField]
    private float similarityPercentage = 0.7f;

    [Header("손 전체 위치 비교")]
    [SerializeField]
    private bool compareHandPosition = true;

    [SerializeField]
    private float handPositionThreshold = 0.1f;

    [SerializeField]
    private bool compareHandRotation = true;

    [SerializeField]
    private float handRotationThreshold = 20f;

    [SerializeField]
    private Transform referencePoint;

    [Header("리플레이 손 표시")]
    [SerializeField]
    private bool showReplayHands = true;

    [SerializeField]
    private Material replayHandMaterial;

    [SerializeField]
    private float replayHandAlpha = 0.5f;

    [SerializeField]
    private Color replayHandColor = new Color(0.3f, 0.5f, 1f, 0.5f);

    [Header("독립 재생 설정")]
    [SerializeField]
    private bool playLeftHand = true;

    [SerializeField]
    private bool playRightHand = true;

    [SerializeField]
    private bool compareLeftHand = true;

    [SerializeField]
    private bool compareRightHand = true;

    [SerializeField]
    private List<HandJointId> keyJoints = new List<HandJointId>()
    {
        HandJointId.HandWristRoot,
        HandJointId.HandThumb3,
        HandJointId.HandIndex3,
        HandJointId.HandMiddle3,
        HandJointId.HandRing3,
        HandJointId.HandPinky3
    };

    [Header("시나리오 연동")]
    [SerializeField]
    private bool integrateWithScenario = false;

    [Header("반복 재생 설정")]
    [SerializeField]
    [Tooltip("반복 재생 활성화 - 지정된 구간을 계속 반복합니다")]
    private bool enableLoopPlayback = true;

    [SerializeField]
    [Range(0.1f, 1.0f)]
    [Tooltip("재생할 프레임 비율 (0.8 = 전체의 80%까지만 재생 후 처음으로)")]
    private float playbackLengthRatio = 1.0f;

    [Header("유사도 표시 설정")]
    [SerializeField]
    [Range(0.0f, 1.0f)]
    [Tooltip("유사도 참고 값 (표시용, 자동 판정하지 않음)")]
    private float similarityReferenceValue = 0.7f;

    [SerializeField]
    [Tooltip("손 위치 비교 활성화")]
    private bool enableHandPositionComparison = true;

    [SerializeField]
    [Tooltip("손 각도(회전) 비교 활성화")]
    private bool enableHandRotationComparison = true;

    [SerializeField]
    [Tooltip("비교 간격 (초, 예: 0.5 = 0.5초마다 비교)")]
    private float comparisonInterval = 0.5f;

    // 비교 타이머
    private float comparisonElapsedTime = 0f;

    // 완료 이벤트
    public event System.Action OnSequenceCompleted;
    public event System.Action OnLeftHandCompleted;
    public event System.Action OnRightHandCompleted;
    public event System.Action OnUserProgressCompleted;  // 사용자가 1회 완료 시
    private bool hasNotifiedCompletion = false;

    // 사용자 진행 추적
    private int userLeftProgress = 0;    // 사용자가 도달한 최대 프레임
    private int userRightProgress = 0;   // 사용자가 도달한 최대 프레임
    private bool userCompletedLeft = false;
    private bool userCompletedRight = false;
    private bool hasNotifiedUserCompletion = false;

    [Header("디버그 표시")]
    [SerializeField]
    private bool showDebugGizmos = true;

    [SerializeField]
    private bool showPositionLines = true;

    [SerializeField]
    private float gizmoSphereSize = 0.02f;

    public enum PlaybackMode
    {
        ComparisonMode,          // 기존: 유사도에 따라 재생 속도 변경
        PlaybackOnly,
        ManualControl,
        PlaybackWithComparison   // 신규: 재생과 비교 독립
    }

    [Header("재생 모드")]
    [SerializeField]
    private PlaybackMode playbackMode = PlaybackMode.PlaybackOnly;

    private List<PoseFrame> loadedSequence = new List<PoseFrame>();
    private bool isLeftPlaying = false;
    private bool isRightPlaying = false;
    private int currentLeftPlaybackIndex = 0;
    private int currentRightPlaybackIndex = 0;
    private float lastLeftPlaybackTime = 0f;
    private float lastRightPlaybackTime = 0f;

    private float currentLeftSimilarity = 0f;
    private float currentRightSimilarity = 0f;
    private bool isLeftHandSimilar = false;
    private bool isRightHandSimilar = false;

    private float leftHandPositionError = 0f;
    private float rightHandPositionError = 0f;
    private float leftHandRotationError = 0f;
    private float rightHandRotationError = 0f;

    private Vector3 leftReplayTargetPosition;
    private Vector3 rightReplayTargetPosition;
    private Vector3 leftPlayerCurrentPosition;
    private Vector3 rightPlayerCurrentPosition;

    // 재생 모델이 Mapper인지 확인
    private bool useLeftMapper = false;
    private bool useRightMapper = false;

    // 추가 필드들
    private float playbackSpeed = 1.0f;
    private bool isPaused = false;
    private float leftElapsedTime = 0f;
    private float rightElapsedTime = 0f;
    private float currentPlayTime = 0f;
    private float totalDuration = 0f;
    private string currentLoadedFile = "";

    public event System.Action<float> OnPlaybackProgress;
    public event System.Action OnPlaybackCompleted;
    public event System.Action OnPlaybackStarted;

    [System.Serializable]
    private class PoseFrame
    {
        public Dictionary<int, PoseData> leftLocalPoses = new Dictionary<int, PoseData>();
        public Dictionary<int, PoseData> rightLocalPoses = new Dictionary<int, PoseData>();
        public Vector3 leftRootPosition;      // OpenXRRoot 위치
        public Quaternion leftRootRotation;   // OpenXRRoot 회전
        public Vector3 rightRootPosition;     // OpenXRRoot 위치
        public Quaternion rightRootRotation;  // OpenXRRoot 회전
        public float timestamp;
    }

    [System.Serializable]
    private class PoseData
    {
        public Vector3 position;
        public Quaternion rotation;
    }

    public struct SimilarityResult
    {
        public float leftHandSimilarity;
        public float rightHandSimilarity;
        public bool leftHandPassed;
        public bool rightHandPassed;
        public bool overallPassed;
        public float leftHandPositionError;
        public float rightHandPositionError;
        public float leftHandRotationError;
        public float rightHandRotationError;
        public bool leftHandPositionPassed;
        public bool rightHandPositionPassed;
    }

    void Start()
    {
        // OpenXRRoot 자동 탐색
        FindOpenXRRoots();

        // Mapper 사용 여부 확인
        useLeftMapper = (leftHandMapper != null);
        useRightMapper = (rightHandMapper != null);

        if (useLeftMapper)
        {
            Debug.Log("왼손: HandTransformMapper 사용");
            leftHandMapper.SetVisible(showReplayHands);
            leftHandMapper.SetAlpha(replayHandAlpha);
        }
        else if (leftHandVisual != null)
        {
            Debug.Log("왼손: HandVisual 사용");
            SetupReplayHandVisual(leftHandVisual, "왼손");
        }

        if (useRightMapper)
        {
            Debug.Log("오른손: HandTransformMapper 사용");
            rightHandMapper.SetVisible(showReplayHands);
            rightHandMapper.SetAlpha(replayHandAlpha);
        }
        else if (rightHandVisual != null)
        {
            Debug.Log("오른손: HandVisual 사용");
            SetupReplayHandVisual(rightHandVisual, "오른손");
        }

        if (referencePoint == null)
        {
            Debug.LogWarning("기준점이 없어 월드 좌표로 비교합니다.");
        }
    }

    /// <summary>
    /// OpenXRRoot GameObject 자동 탐색
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
                        Debug.Log($"왼손 OpenXRRoot 찾음: {leftOpenXRRoot.name}");
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
                        Debug.Log($"오른손 OpenXRRoot 찾음: {rightOpenXRRoot.name}");
                        break;
                    }
                    parent = parent.parent;
                }
            }
        }
    }

    private void SetupReplayHandVisual(HandVisual handVisual, string handName)
    {
        if (handVisual == null) return;

        SkinnedMeshRenderer[] renderers = handVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true);

        foreach (var renderer in renderers)
        {
            Material mat;
            if (replayHandMaterial != null)
            {
                mat = new Material(replayHandMaterial);
            }
            else
            {
                mat = new Material(renderer.material);
                mat.SetFloat("_Mode", 3);
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = 3000;
            }

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

    void Update()
    {
        if (loadedSequence.Count == 0 || isPaused)
            return;

        switch (playbackMode)
        {
            case PlaybackMode.PlaybackOnly:
                UpdatePlaybackOnly();
                break;
            case PlaybackMode.ComparisonMode:
                UpdateComparisonMode();
                break;
            case PlaybackMode.PlaybackWithComparison:
                UpdatePlaybackWithComparison();
                break;
            case PlaybackMode.ManualControl:
                break;
        }

        // 시나리오 통합: 양손 완료 체크
        if (integrateWithScenario && !hasNotifiedCompletion)
        {
            CheckSequenceCompletion();
        }
    }

    private void UpdatePlaybackOnly()
    {
        float deltaTime = Time.deltaTime * playbackSpeed;

        // 재생할 최대 프레임 인덱스 계산
        int maxFrameIndex = Mathf.CeilToInt(loadedSequence.Count * playbackLengthRatio);

        if (isLeftPlaying && playLeftHand && currentLeftPlaybackIndex < maxFrameIndex)
        {
            leftElapsedTime += deltaTime;
            if (leftElapsedTime >= playbackInterval)
            {
                leftElapsedTime = 0f;
                ApplyLeftHandFrame();
                currentLeftPlaybackIndex++;

                // 지정된 프레임까지 재생했으면 반복
                if (currentLeftPlaybackIndex >= maxFrameIndex)
                {
                    if (enableLoopPlayback)
                    {
                        currentLeftPlaybackIndex = 0;
                        //Debug.Log($"<color=cyan>왼손 반복 재생 (0 ~ {maxFrameIndex - 1} 프레임)</color>");
                    }
                    else
                    {
                        isLeftPlaying = false;
                        Debug.Log("<color=cyan>왼손 재생 완료!</color>");
                        CheckPlaybackComplete();
                    }
                }
            }
        }

        if (isRightPlaying && playRightHand && currentRightPlaybackIndex < maxFrameIndex)
        {
            rightElapsedTime += deltaTime;
            if (rightElapsedTime >= playbackInterval)
            {
                rightElapsedTime = 0f;
                ApplyRightHandFrame();
                currentRightPlaybackIndex++;

                // 지정된 프레임까지 재생했으면 반복
                if (currentRightPlaybackIndex >= maxFrameIndex)
                {
                    if (enableLoopPlayback)
                    {
                        currentRightPlaybackIndex = 0;
                        //Debug.Log($"<color=cyan>오른손 반복 재생 (0 ~ {maxFrameIndex - 1} 프레임)</color>");
                    }
                    else
                    {
                        isRightPlaying = false;
                        Debug.Log("<color=cyan>오른손 재생 완료!</color>");
                        CheckPlaybackComplete();
                    }
                }
            }
        }

        if (isLeftPlaying || isRightPlaying)
        {
            float progress = GetOverallProgress();
            OnPlaybackProgress?.Invoke(progress);
        }
    }

    /// <summary>
    /// 재생과 비교를 독립적으로 처리
    /// - 가이드 핸드: 일정 속도로 루프 재생
    /// - 비교: 현재 프레임 데이터만 참조
    /// </summary>
    private void UpdatePlaybackWithComparison()
    {
        // 1. 가이드 핸드 재생 (시간 기반, 독립적)
        UpdateGuideHandPlayback();

        // 2. 비교 (독립적)
        UpdatePoseComparisonLoop();
    }

    /// <summary>
    /// 가이드 핸드 재생 (루프, 일정 속도)
    /// </summary>
    private void UpdateGuideHandPlayback()
    {
        float deltaTime = Time.deltaTime * playbackSpeed;

        // 재생할 최대 프레임 인덱스 계산
        int maxFrameIndex = Mathf.CeilToInt(loadedSequence.Count * playbackLengthRatio);

        // 왼손 재생
        if (isLeftPlaying && playLeftHand)
        {
            leftElapsedTime += deltaTime;
            if (leftElapsedTime >= playbackInterval)
            {
                leftElapsedTime = 0f;

                // 프레임 진행
                if (currentLeftPlaybackIndex < maxFrameIndex)
                {
                    ApplyLeftHandFrame();
                    currentLeftPlaybackIndex++;
                }

                // 지정된 프레임까지 재생했으면 루프
                if (currentLeftPlaybackIndex >= maxFrameIndex)
                {
                    if (enableLoopPlayback)
                    {
                        currentLeftPlaybackIndex = 0;
                        //Debug.Log($"<color=cyan>[재생] 왼손 루프 재시작 (0 ~ {maxFrameIndex - 1} 프레임)</color>");
                    }
                    else
                    {
                        isLeftPlaying = false;
                        Debug.Log("<color=cyan>[재생] 왼손 완료</color>");
                    }
                }
            }
        }

        // 오른손 재생
        if (isRightPlaying && playRightHand)
        {
            rightElapsedTime += deltaTime;
            if (rightElapsedTime >= playbackInterval)
            {
                rightElapsedTime = 0f;

                // 프레임 진행
                if (currentRightPlaybackIndex < maxFrameIndex)
                {
                    ApplyRightHandFrame();
                    currentRightPlaybackIndex++;
                }

                // 지정된 프레임까지 재생했으면 루프
                if (currentRightPlaybackIndex >= maxFrameIndex)
                {
                    if (enableLoopPlayback)
                    {
                        currentRightPlaybackIndex = 0;
                        //Debug.Log($"<color=cyan>[재생] 오른손 루프 재시작 (0 ~ {maxFrameIndex - 1} 프레임)</color>");
                    }
                    else
                    {
                        isRightPlaying = false;
                        Debug.Log("<color=cyan>[재생] 오른손 완료</color>");
                    }
                }
            }
        }
    }

    /// <summary>
    /// 포즈 비교 (현재 재생 중인 프레임 데이터와 비교)
    /// 유사도 표시 + 사용자 진행 추적
    /// </summary>
    private void UpdatePoseComparisonLoop()
    {
        comparisonElapsedTime += Time.deltaTime;

        // 비교 간격마다 체크
        if (comparisonElapsedTime < comparisonInterval)
            return;

        comparisonElapsedTime = 0f;

        // 재생할 최대 프레임 인덱스 계산
        int maxFrameIndex = Mathf.CeilToInt(loadedSequence.Count * playbackLengthRatio);

        // 왼손 비교 (유사도 표시 + 사용자 진행 추적)
        if (compareLeftHand && currentLeftPlaybackIndex < maxFrameIndex && !userCompletedLeft)
        {
            var result = CompareLeftPose();
            float similarity = result.leftHandSimilarity * 100f;
            float posError = result.leftHandPositionError;
            float rotError = result.leftHandRotationError;

            // 사용자가 현재 가이드 프레임을 따라했는지 체크
            if (result.leftHandPassed && result.leftHandPositionPassed)
            {
                if (currentLeftPlaybackIndex > userLeftProgress)
                {
                    userLeftProgress = currentLeftPlaybackIndex;
                }

                if (userLeftProgress >= maxFrameIndex - 1)
                {
                    userCompletedLeft = true;
                    Debug.Log($"<color=green>✓ 왼손 사용자 동작 완료!</color>");
                    CheckUserCompletion();
                }
            }

            if (currentLeftPlaybackIndex % 10 == 0)
            {
                Debug.Log($"<color=cyan>[비교-왼손] 가이드: {currentLeftPlaybackIndex}/{maxFrameIndex}, 사용자 진행: {userLeftProgress + 1}/{maxFrameIndex}</color>");
                Debug.Log($"  - 유사도: {similarity:F1}%, 위치 오차: {posError:F3}m, 회전 오차: {rotError:F1}°");
            }
        }

        // 오른손 비교 (유사도 표시 + 사용자 진행 추적)
        if (compareRightHand && currentRightPlaybackIndex < maxFrameIndex && !userCompletedRight)
        {
            var result = CompareRightPose();
            float similarity = result.rightHandSimilarity * 100f;
            float posError = result.rightHandPositionError;
            float rotError = result.rightHandRotationError;

            // 사용자가 현재 가이드 프레임을 따라했는지 체크
            if (result.rightHandPassed && result.rightHandPositionPassed)
            {
                if (currentRightPlaybackIndex > userRightProgress)
                {
                    userRightProgress = currentRightPlaybackIndex;
                }

                if (userRightProgress >= maxFrameIndex - 1)
                {
                    userCompletedRight = true;
                    Debug.Log($"<color=green>✓ 오른손 사용자 동작 완료!</color>");
                    CheckUserCompletion();
                }
            }

            if (currentRightPlaybackIndex % 10 == 0)
            {
                Debug.Log($"<color=cyan>[비교-오른손] 가이드: {currentRightPlaybackIndex}/{maxFrameIndex}, 사용자 진행: {userRightProgress + 1}/{maxFrameIndex}</color>");
                Debug.Log($"  - 유사도: {similarity:F1}%, 위치 오차: {posError:F3}m, 회전 오차: {rotError:F1}°");
            }
        }
    }

    private void UpdateComparisonMode()
    {
        float deltaTime = Time.deltaTime * playbackSpeed;

        // 재생할 최대 프레임 인덱스 계산
        int maxFrameIndex = Mathf.CeilToInt(loadedSequence.Count * playbackLengthRatio);

        // 왼손 가이드 재생 (무한 반복)
        if (isLeftPlaying && playLeftHand && currentLeftPlaybackIndex < maxFrameIndex)
        {
            leftElapsedTime += deltaTime;
            if (leftElapsedTime >= playbackInterval)
            {
                leftElapsedTime = 0f;

                // 유사도 체크 (표시 + 사용자 진행 추적)
                if (compareLeftHand && !userCompletedLeft)
                {
                    var result = CompareLeftPose();
                    float similarity = result.leftHandSimilarity * 100f;

                    // 사용자가 현재 가이드 프레임을 따라했는지 체크
                    if (result.leftHandPassed && result.leftHandPositionPassed)
                    {
                        // 사용자가 이 프레임까지 도달했다고 기록
                        if (currentLeftPlaybackIndex > userLeftProgress)
                        {
                            userLeftProgress = currentLeftPlaybackIndex;
                        }

                        // 사용자가 목표 지점까지 도달했는지 체크
                        if (userLeftProgress >= maxFrameIndex - 1)
                        {
                            userCompletedLeft = true;
                            Debug.Log($"<color=green>✓ 왼손 사용자 동작 완료! ({userLeftProgress + 1}/{maxFrameIndex} 프레임)</color>");
                            CheckUserCompletion();
                        }
                    }

                    if (currentLeftPlaybackIndex % 10 == 0)
                    {
                        //Debug.Log($"<color=cyan>[왼손] 가이드 프레임: {currentLeftPlaybackIndex}/{maxFrameIndex}, 사용자 진행: {userLeftProgress + 1}/{maxFrameIndex}, 유사도: {similarity:F1}%</color>");
                    }
                }

                // 가이드 프레임 진행 (자동, 유사도와 무관)
                ApplyLeftHandFrame();
                currentLeftPlaybackIndex++;

                // 가이드는 지정된 프레임까지 재생 후 반복
                if (currentLeftPlaybackIndex >= maxFrameIndex)
                {
                    if (enableLoopPlayback)
                    {
                        currentLeftPlaybackIndex = 0;
                        //Debug.Log($"<color=yellow>[가이드] 왼손 반복 재생 (0 ~ {maxFrameIndex - 1} 프레임)</color>");
                    }
                    else
                    {
                        isLeftPlaying = false;
                        Debug.Log("<color=cyan>[가이드] 왼손 재생 완료!</color>");
                    }
                }
            }
        }

        // 오른손 가이드 재생 (무한 반복)
        if (isRightPlaying && playRightHand && currentRightPlaybackIndex < maxFrameIndex)
        {
            rightElapsedTime += deltaTime;
            if (rightElapsedTime >= playbackInterval)
            {
                rightElapsedTime = 0f;

                // 유사도 체크 (표시 + 사용자 진행 추적)
                if (compareRightHand && !userCompletedRight)
                {
                    var result = CompareRightPose();
                    float similarity = result.rightHandSimilarity * 100f;

                    // 사용자가 현재 가이드 프레임을 따라했는지 체크
                    if (result.rightHandPassed && result.rightHandPositionPassed)
                    {
                        // 사용자가 이 프레임까지 도달했다고 기록
                        if (currentRightPlaybackIndex > userRightProgress)
                        {
                            userRightProgress = currentRightPlaybackIndex;
                        }

                        // 사용자가 목표 지점까지 도달했는지 체크
                        if (userRightProgress >= maxFrameIndex - 1)
                        {
                            userCompletedRight = true;
                            Debug.Log($"<color=green>✓ 오른손 사용자 동작 완료! ({userRightProgress + 1}/{maxFrameIndex} 프레임)</color>");
                            CheckUserCompletion();
                        }
                    }

                    if (currentRightPlaybackIndex % 10 == 0)
                    {
                        //Debug.Log($"<color=cyan>[오른손] 가이드 프레임: {currentRightPlaybackIndex}/{maxFrameIndex}, 사용자 진행: {userRightProgress + 1}/{maxFrameIndex}, 유사도: {similarity:F1}%</color>");
                    }
                }

                // 가이드 프레임 진행 (자동, 유사도와 무관)
                ApplyRightHandFrame();
                currentRightPlaybackIndex++;

                // 가이드는 지정된 프레임까지 재생 후 반복
                if (currentRightPlaybackIndex >= maxFrameIndex)
                {
                    if (enableLoopPlayback)
                    {
                        currentRightPlaybackIndex = 0;
                        //Debug.Log($"<color=yellow>[가이드] 오른손 반복 재생 (0 ~ {maxFrameIndex - 1} 프레임)</color>");
                    }
                    else
                    {
                        isRightPlaying = false;
                        Debug.Log("<color=cyan>[가이드] 오른손 재생 완료!</color>");
                    }
                }
            }
        }
    }

    private void CheckPlaybackComplete()
    {
        if (!isLeftPlaying && !isRightPlaying)
        {
            OnPlaybackCompleted?.Invoke();
        }
    }

    private void ProgressLeftHand()
    {
        currentLeftPlaybackIndex++;
        if (currentLeftPlaybackIndex >= loadedSequence.Count)
        {
            isLeftPlaying = false;
            Debug.Log("<color=cyan>왼손 재생 완료!</color>");

            if (integrateWithScenario)
            {
                OnLeftHandCompleted?.Invoke();
            }

            return;
        }
        ApplyLeftHandFrame();
    }

    private void ProgressRightHand()
    {
        currentRightPlaybackIndex++;
        if (currentRightPlaybackIndex >= loadedSequence.Count)
        {
            isRightPlaying = false;
            Debug.Log("<color=cyan>오른손 재생 완료!</color>");

            if (integrateWithScenario)
            {
                OnRightHandCompleted?.Invoke();
            }

            return;
        }
        ApplyRightHandFrame();
    }

    private SimilarityResult CompareLeftPose()
    {
        SimilarityResult result = new SimilarityResult();

        if (playerLeftHand == null || currentLeftPlaybackIndex >= loadedSequence.Count)
            return result;

        PoseFrame currentFrame = loadedSequence[currentLeftPlaybackIndex];

        // 플레이어는 항상 HandVisual
        if (playerLeftHand.Hand == null || !playerLeftHand.Hand.IsTrackedDataValid)
            return result;

        bool passed;
        result.leftHandSimilarity = ComparePose(playerLeftHand, currentFrame.leftLocalPoses, out passed, "왼손");
        result.leftHandPassed = passed;

        if (enableHandPositionComparison)
        {
            CompareHandWorldPosition(
                playerLeftHand,
                currentFrame.leftRootPosition,
                currentFrame.leftRootRotation,
                out result.leftHandPositionError,
                out result.leftHandRotationError,
                out result.leftHandPositionPassed,
                "왼손"
            );
        }
        else
        {
            result.leftHandPositionPassed = true;
        }

        isLeftHandSimilar = result.leftHandPassed && result.leftHandPositionPassed;
        currentLeftSimilarity = result.leftHandSimilarity;
        leftHandPositionError = result.leftHandPositionError;
        leftHandRotationError = result.leftHandRotationError;

        return result;
    }

    private SimilarityResult CompareRightPose()
    {
        SimilarityResult result = new SimilarityResult();

        if (playerRightHand == null || currentRightPlaybackIndex >= loadedSequence.Count)
            return result;

        PoseFrame currentFrame = loadedSequence[currentRightPlaybackIndex];

        if (playerRightHand.Hand == null || !playerRightHand.Hand.IsTrackedDataValid)
            return result;

        bool passed;
        result.rightHandSimilarity = ComparePose(playerRightHand, currentFrame.rightLocalPoses, out passed, "오른손");
        result.rightHandPassed = passed;

        if (enableHandPositionComparison)
        {
            CompareHandWorldPosition(
                playerRightHand,
                currentFrame.rightRootPosition,
                currentFrame.rightRootRotation,
                out result.rightHandPositionError,
                out result.rightHandRotationError,
                out result.rightHandPositionPassed,
                "오른손"
            );
        }
        else
        {
            result.rightHandPositionPassed = true;
        }

        isRightHandSimilar = result.rightHandPassed && result.rightHandPositionPassed;
        currentRightSimilarity = result.rightHandSimilarity;
        rightHandPositionError = result.rightHandPositionError;
        rightHandRotationError = result.rightHandRotationError;

        return result;
    }

    private void CompareHandWorldPosition(
        HandVisual playerHand,
        Vector3 targetRootPosition,
        Quaternion targetRootRotation,
        out float positionError,
        out float rotationError,
        out bool passed,
        string handName)
    {
        positionError = 0f;
        rotationError = 0f;
        passed = false;

        if (playerHand == null || playerHand.Hand == null || !playerHand.Hand.IsTrackedDataValid)
            return;

        // OpenXRRoot Transform 비교
        Transform openXRRoot = null;
        if (handName == "왼손" && leftOpenXRRoot != null)
        {
            openXRRoot = leftOpenXRRoot;
        }
        else if (handName == "오른손" && rightOpenXRRoot != null)
        {
            openXRRoot = rightOpenXRRoot;
        }

        if (openXRRoot == null)
        {
            // OpenXRRoot가 없으면 Wrist로 폴백
            Transform wrist = playerHand.Joints[(int)HandJointId.HandWristRoot];
            if (wrist == null)
                return;
            openXRRoot = wrist;
        }

        Vector3 targetPos = targetRootPosition;
        if (referencePoint != null)
        {
            targetPos = referencePoint.position + targetRootPosition;
        }

        Vector3 playerPos = openXRRoot.position;

        if (handName == "왼손")
        {
            leftReplayTargetPosition = targetPos;
            leftPlayerCurrentPosition = playerPos;
        }
        else
        {
            rightReplayTargetPosition = targetPos;
            rightPlayerCurrentPosition = playerPos;
        }

        positionError = Vector3.Distance(playerPos, targetPos);

        if (enableHandRotationComparison)
        {
            rotationError = Quaternion.Angle(openXRRoot.rotation, targetRootRotation);
        }

        bool positionPassed = positionError <= handPositionThreshold;
        bool rotationPassed = !compareHandRotation || rotationError <= handRotationThreshold;
        passed = positionPassed && rotationPassed;

        if (handName == "왼손" && currentLeftPlaybackIndex % 10 == 0)
        {
            Debug.Log($"[비교] 왼손 위치 오차: {positionError:F3}m, 회전 오차: {rotationError:F1}°");
        }
        else if (handName == "오른손" && currentRightPlaybackIndex % 10 == 0)
        {
            Debug.Log($"[비교] 오른손 위치 오차: {positionError:F3}m, 회전 오차: {rotationError:F1}°");
        }
    }

    private float ComparePose(HandVisual playerHand, Dictionary<int, PoseData> replayPoses, out bool passed, string handName)
    {
        passed = false;

        if (playerHand == null || playerHand.Hand == null || !playerHand.Hand.IsTrackedDataValid)
        {
            return 0f;
        }

        int similarJointCount = 0;
        int totalJointCount = 0;

        foreach (HandJointId jointId in keyJoints)
        {
            int jointIndex = (int)jointId;

            if (!replayPoses.ContainsKey(jointIndex))
                continue;

            totalJointCount++;

            if (jointIndex >= playerHand.Joints.Count || playerHand.Joints[jointIndex] == null)
                continue;

            Transform playerJoint = playerHand.Joints[jointIndex];
            PoseData replayPose = replayPoses[jointIndex];

            float positionDistance = Vector3.Distance(playerJoint.localPosition, replayPose.position);
            float rotationAngle = Quaternion.Angle(playerJoint.localRotation, replayPose.rotation);

            if (positionDistance <= positionThreshold && rotationAngle <= rotationThreshold)
            {
                similarJointCount++;
            }
        }

        if (totalJointCount == 0)
            return 0f;

        float similarity = (float)similarJointCount / totalJointCount;
        passed = similarity >= similarityReferenceValue;  // 참고용 값

        return similarity;
    }

    private void ApplyLeftHandFrame()
    {
        if (currentLeftPlaybackIndex >= loadedSequence.Count)
            return;

        PoseFrame frame = loadedSequence[currentLeftPlaybackIndex];

        // Root Transform 계산
        Vector3 targetRootPos = frame.leftRootPosition;
        Quaternion targetRootRot = frame.leftRootRotation;

        if (referencePoint != null)
        {
            targetRootPos = referencePoint.position + frame.leftRootPosition;
            targetRootRot = referencePoint.rotation * frame.leftRootRotation;
        }

        // 1. OpenXRRoot Transform 적용 (비교용)
        if (leftOpenXRRoot != null)
        {
            leftOpenXRRoot.position = targetRootPos;
            leftOpenXRRoot.rotation = targetRootRot;

            if (currentLeftPlaybackIndex % 10 == 0)
            {
                //Debug.Log($"<color=green>[재생] Frame {currentLeftPlaybackIndex} 왼손 OpenXRRoot 적용:</color>" +
                //$"  Pos={targetRootPos}, Rot={targetRootRot.eulerAngles}");
            }
        }

        // 2. 재생용 핸드 모델의 Root에 적용
        Transform replayRoot = null;
        string rootName = "";

        if (useLeftMapper && leftHandMapper != null && leftHandMapper.Root != null)
        {
            replayRoot = leftHandMapper.Root;
            rootName = leftHandMapper.Root.name + " (Mapper)";
        }
        else if (leftHandVisual != null && leftHandVisual.Root != null)
        {
            replayRoot = leftHandVisual.Root;
            rootName = leftHandVisual.Root.name + " (Visual)";
        }

        if (replayRoot != null)
        {
            replayRoot.position = targetRootPos;
            replayRoot.rotation = targetRootRot;

            if (currentLeftPlaybackIndex % 10 == 0)
            {
                /*Debug.Log($"<color=yellow>[재생] Frame {currentLeftPlaybackIndex} 왼손 재생 모델 Root 적용:</color>" +
                         $"  Root: {rootName}" +
                         $"  Pos={targetRootPos}, Rot={targetRootRot.eulerAngles}");*/
            }
        }
        else if (currentLeftPlaybackIndex == 0)
        {
            Debug.LogWarning("왼손: 재생용 핸드 모델의 Root를 찾을 수 없습니다!");
        }

        // 조인트 포즈 적용 (Mapper 또는 Visual 사용)
        if (useLeftMapper && leftHandMapper != null)
        {
            foreach (var kvp in frame.leftLocalPoses)
            {
                leftHandMapper.SetJointLocalPose(kvp.Key, kvp.Value.position, kvp.Value.rotation);
            }
        }
        else if (leftHandVisual != null)
        {
            ApplyPosesToJoints(leftHandVisual, frame.leftLocalPoses);
        }
    }

    private void ApplyRightHandFrame()
    {
        if (currentRightPlaybackIndex >= loadedSequence.Count)
            return;

        PoseFrame frame = loadedSequence[currentRightPlaybackIndex];

        // Root Transform 계산
        Vector3 targetRootPos = frame.rightRootPosition;
        Quaternion targetRootRot = frame.rightRootRotation;

        if (referencePoint != null)
        {
            targetRootPos = referencePoint.position + frame.rightRootPosition;
            targetRootRot = referencePoint.rotation * frame.rightRootRotation;
        }

        // 1. OpenXRRoot Transform 적용 (비교용)
        if (rightOpenXRRoot != null)
        {
            rightOpenXRRoot.position = targetRootPos;
            rightOpenXRRoot.rotation = targetRootRot;

            if (currentRightPlaybackIndex % 10 == 0)
            {
                /*Debug.Log($"<color=green>[재생] Frame {currentRightPlaybackIndex} 오른손 OpenXRRoot 적용:</color>" +
                         $"  Pos={targetRootPos}, Rot={targetRootRot.eulerAngles}");*/
            }
        }

        // 2. 재생용 핸드 모델의 Root에 적용
        Transform replayRoot = null;
        string rootName = "";

        if (useRightMapper && rightHandMapper != null && rightHandMapper.Root != null)
        {
            replayRoot = rightHandMapper.Root;
            rootName = rightHandMapper.Root.name + " (Mapper)";
        }
        else if (rightHandVisual != null && rightHandVisual.Root != null)
        {
            replayRoot = rightHandVisual.Root;
            rootName = rightHandVisual.Root.name + " (Visual)";
        }

        if (replayRoot != null)
        {
            replayRoot.position = targetRootPos;
            replayRoot.rotation = targetRootRot;

            if (currentRightPlaybackIndex % 10 == 0)
            {
                /*Debug.Log($"<color=yellow>[재생] Frame {currentRightPlaybackIndex} 오른손 재생 모델 Root 적용:</color>" +
                         $"  Root: {rootName}" +
                         $"  Pos={targetRootPos}, Rot={targetRootRot.eulerAngles}");*/
            }
        }
        else if (currentRightPlaybackIndex == 0)
        {
            Debug.LogWarning("오른손: 재생용 핸드 모델의 Root를 찾을 수 없습니다!");
        }

        // 조인트 포즈 적용 (Mapper 또는 Visual 사용)
        if (useRightMapper && rightHandMapper != null)
        {
            foreach (var kvp in frame.rightLocalPoses)
            {
                rightHandMapper.SetJointLocalPose(kvp.Key, kvp.Value.position, kvp.Value.rotation);
            }
        }
        else if (rightHandVisual != null)
        {
            ApplyPosesToJoints(rightHandVisual, frame.rightLocalPoses);
        }
    }

    private void ApplyPosesToJoints(HandVisual handVisual, Dictionary<int, PoseData> poses)
    {
        if (handVisual == null) return;

        bool isDebugFrame = (currentLeftPlaybackIndex == 0 || currentRightPlaybackIndex == 0 ||
                            currentLeftPlaybackIndex % 10 == 0 || currentRightPlaybackIndex % 10 == 0);

        for (int i = 0; i < handVisual.Joints.Count; i++)
        {
            if (poses.TryGetValue(i, out PoseData poseData) && handVisual.Joints[i] != null)
            {
                handVisual.Joints[i].localPosition = poseData.position;
                handVisual.Joints[i].localRotation = poseData.rotation;

                // 첫 프레임과 10프레임마다 디버그 출력
                if (isDebugFrame && i == 0)
                {
                    /*Debug.Log($"[Frame {currentLeftPlaybackIndex}/{currentRightPlaybackIndex}] " +
                             $"Wrist LocalPos: {poseData.position}, LocalRot: {poseData.rotation.eulerAngles}");*/
                }
            }
        }
    }

    public void StartPlaybackFromCSV(string csvFileName)
    {
        ResetCompletionFlag();  // 재생 시작 시 완료 플래그 초기화
        string path = Path.Combine(Application.persistentDataPath, csvFileName);
        if (!path.EndsWith(".csv"))
            path += ".csv";

        if (!File.Exists(path))
        {
            Debug.LogError("CSV 파일 없음: " + path);
            return;
        }

        string[] lines = File.ReadAllLines(path, System.Text.Encoding.UTF8);
        if (lines.Length < 2)
        {
            Debug.LogError("CSV 데이터 부족.");
            return;
        }

        loadedSequence.Clear();
        PoseFrame currentFrame = null;
        int lastFrameIndex = -1;

        CultureInfo invariantCulture = CultureInfo.InvariantCulture;

        for (int i = 1; i < lines.Length; i++)
        {
            try
            {
                string[] values = lines[i].Split(',');

                if (values.Length < 11)
                {
                    Debug.LogWarning($"라인 {i}: 필드가 부족합니다. ({values.Length}개)");
                    continue;
                }

                int frameIndex = int.Parse(values[0], invariantCulture);
                string handType = values[1].Trim();
                int jointId = int.Parse(values[2], invariantCulture);

                Vector3 pos = new Vector3(
                    float.Parse(values[3], invariantCulture),
                    float.Parse(values[4], invariantCulture),
                    float.Parse(values[5], invariantCulture)
                );
                Quaternion rot = new Quaternion(
                    float.Parse(values[6], invariantCulture),
                    float.Parse(values[7], invariantCulture),
                    float.Parse(values[8], invariantCulture),
                    float.Parse(values[9], invariantCulture)
                );
                float timestamp = float.Parse(values[10], invariantCulture);

                if (frameIndex != lastFrameIndex)
                {
                    if (currentFrame != null) loadedSequence.Add(currentFrame);
                    currentFrame = new PoseFrame { timestamp = timestamp };
                    lastFrameIndex = frameIndex;
                }

                PoseData poseData = new PoseData { position = pos, rotation = rot };

                if (handType == "Left")
                {
                    currentFrame.leftLocalPoses[jointId] = poseData;

                    // Root Transform 읽기 (JointID=1에 저장되어 있음)
                    if (values.Length >= 18 && !string.IsNullOrEmpty(values[11]) && !string.IsNullOrEmpty(values[14]))
                    {
                        currentFrame.leftRootPosition = new Vector3(
                            float.Parse(values[11], invariantCulture),
                            float.Parse(values[12], invariantCulture),
                            float.Parse(values[13], invariantCulture)
                        );
                        currentFrame.leftRootRotation = new Quaternion(
                            float.Parse(values[14], invariantCulture),
                            float.Parse(values[15], invariantCulture),
                            float.Parse(values[16], invariantCulture),
                            float.Parse(values[17], invariantCulture)
                        );

                        // 첫 프레임과 10프레임마다 디버그 출력
                        if (frameIndex == 0 || frameIndex % 10 == 0)
                        {
                            /*Debug.Log($"<color=cyan>[CSV 파싱] Frame {frameIndex} 왼손 Root (JointID={jointId}):</color>" +
                                     $"  Position: {currentFrame.leftRootPosition}" +
                                     $"  Rotation: {currentFrame.leftRootRotation} (Euler: {currentFrame.leftRootRotation.eulerAngles})");*/
                        }
                    }
                }
                else if (handType == "Right")
                {
                    currentFrame.rightLocalPoses[jointId] = poseData;

                    // Root Transform 읽기 (JointID=1에 저장되어 있음)
                    if (values.Length >= 18 && !string.IsNullOrEmpty(values[11]) && !string.IsNullOrEmpty(values[14]))
                    {
                        currentFrame.rightRootPosition = new Vector3(
                            float.Parse(values[11], invariantCulture),
                            float.Parse(values[12], invariantCulture),
                            float.Parse(values[13], invariantCulture)
                        );
                        currentFrame.rightRootRotation = new Quaternion(
                            float.Parse(values[14], invariantCulture),
                            float.Parse(values[15], invariantCulture),
                            float.Parse(values[16], invariantCulture),
                            float.Parse(values[17], invariantCulture)
                        );

                        // 첫 프레임과 10프레임마다 디버그 출력
                        if (frameIndex == 0 || frameIndex % 10 == 0)
                        {
                            /*Debug.Log($"<color=cyan>[CSV 파싱] Frame {frameIndex} 오른손 Root (JointID={jointId}):</color>" +
                                     $"  Position: {currentFrame.rightRootPosition}" +
                                     $"  Rotation: {currentFrame.rightRootRotation} (Euler: {currentFrame.rightRootRotation.eulerAngles})");*/
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"라인 {i} 파싱 실패: {e.Message}\n라인 내용: {lines[i]}");
                continue;
            }
        }

        if (currentFrame != null) loadedSequence.Add(currentFrame);

        if (loadedSequence.Count == 0)
        {
            Debug.LogError("CSV 파싱 실패.");
            return;
        }

        // 재생 시작
        currentLoadedFile = csvFileName;
        totalDuration = loadedSequence[loadedSequence.Count - 1].timestamp;

        if (playLeftHand)
        {
            isLeftPlaying = true;
            currentLeftPlaybackIndex = 0;
            lastLeftPlaybackTime = Time.time;
            leftElapsedTime = 0f;
            ApplyLeftHandFrame();
        }

        if (playRightHand)
        {
            isRightPlaying = true;
            currentRightPlaybackIndex = 0;
            lastRightPlaybackTime = Time.time;
            rightElapsedTime = 0f;
            ApplyRightHandFrame();
        }

        OnPlaybackStarted?.Invoke();

        string modeText = playbackMode == PlaybackMode.PlaybackOnly ? "재생 전용" :
                         playbackMode == PlaybackMode.ComparisonMode ? "비교" : "수동";

        Debug.Log($"<color=cyan>재생 시작 ({modeText} 모드)</color> - 총 {loadedSequence.Count} 프레임\n" +
                 $"기준점: {(referencePoint != null ? referencePoint.name : "없음")}\n" +
                 $"손 위치 비교: {(compareHandPosition ? "ON" : "OFF")}\n" +
                 $"손 회전 비교: {(compareHandRotation ? "ON" : "OFF")}");
    }


    /// <summary>
    /// Resources 폴더에서 CSV 로드 (Resources/HandPoseData/)
    /// ScenarioActionHandler에서 자동으로 호출됨
    /// </summary>
    public void StartPlaybackFromResourcesCSV(string csvFileName)
    {
        ResetCompletionFlag();

        // .csv 확장자 제거 (Resources.Load는 확장자 없이 사용)
        string fileNameWithoutExt = csvFileName;
        if (fileNameWithoutExt.EndsWith(".csv"))
            fileNameWithoutExt = fileNameWithoutExt.Substring(0, fileNameWithoutExt.Length - 4);

        // Resources/HandPoseData/ 폴더에서 로드
        TextAsset csvFile = Resources.Load<TextAsset>($"HandPoseData/{fileNameWithoutExt}");

        if (csvFile == null)
        {
            Debug.LogError($"<color=red>[HandPosePlayer] Resources/HandPoseData/{fileNameWithoutExt} 파일을 찾을 수 없습니다!</color>");
            Debug.LogError($"<color=yellow>[HandPosePlayer] 확인 사항:</color>\n" +
                          $"  1. Assets/Resources/HandPoseData/ 폴더가 존재하는지\n" +
                          $"  2. {fileNameWithoutExt}.csv 파일이 해당 폴더에 있는지\n" +
                          $"  3. 파일명이 정확한지 (대소문자 구분)\n" +
                          $"  4. 한글 파일명의 경우 영문으로 변경 권장");
            return;
        }

        Debug.Log($"<color=green>[HandPosePlayer] ✓ Resources에서 CSV 로드 성공: {fileNameWithoutExt}</color>");


        // CSV 텍스트를 올바른 인코딩으로 디코딩 (한글 지원)
        string csvText = DecodeCSVText(csvFile.bytes);

        // CSV 텍스트를 라인으로 분리
        string[] lines = csvText.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length < 2)
        {
            Debug.LogError("[HandPosePlayer] CSV 데이터 부족.");
            return;
        }

        loadedSequence.Clear();
        PoseFrame currentFrame = null;
        int lastFrameIndex = -1;

        CultureInfo invariantCulture = CultureInfo.InvariantCulture;

        // 기존 StartPlaybackFromCSV와 동일한 파싱 로직
        for (int i = 1; i < lines.Length; i++)
        {
            try
            {
                string[] values = lines[i].Split(',');

                if (values.Length < 11)
                {
                    Debug.LogWarning($"라인 {i}: 필드가 부족합니다. ({values.Length}개)");
                    continue;
                }

                int frameIndex = int.Parse(values[0], invariantCulture);
                string handType = values[1].Trim();
                int jointId = int.Parse(values[2], invariantCulture);

                Vector3 pos = new Vector3(
                    float.Parse(values[3], invariantCulture),
                    float.Parse(values[4], invariantCulture),
                    float.Parse(values[5], invariantCulture)
                );
                Quaternion rot = new Quaternion(
                    float.Parse(values[6], invariantCulture),
                    float.Parse(values[7], invariantCulture),
                    float.Parse(values[8], invariantCulture),
                    float.Parse(values[9], invariantCulture)
                );
                float timestamp = float.Parse(values[10], invariantCulture);

                if (frameIndex != lastFrameIndex)
                {
                    if (currentFrame != null) loadedSequence.Add(currentFrame);
                    currentFrame = new PoseFrame { timestamp = timestamp };
                    lastFrameIndex = frameIndex;
                }

                PoseData poseData = new PoseData { position = pos, rotation = rot };

                if (handType == "Left")
                {
                    currentFrame.leftLocalPoses[jointId] = poseData;

                    if (values.Length >= 18 && !string.IsNullOrEmpty(values[11]) && !string.IsNullOrEmpty(values[14]))
                    {
                        currentFrame.leftRootPosition = new Vector3(
                            float.Parse(values[11], invariantCulture),
                            float.Parse(values[12], invariantCulture),
                            float.Parse(values[13], invariantCulture)
                        );
                        currentFrame.leftRootRotation = new Quaternion(
                            float.Parse(values[14], invariantCulture),
                            float.Parse(values[15], invariantCulture),
                            float.Parse(values[16], invariantCulture),
                            float.Parse(values[17], invariantCulture)
                        );
                    }
                }
                else if (handType == "Right")
                {
                    currentFrame.rightLocalPoses[jointId] = poseData;

                    if (values.Length >= 18 && !string.IsNullOrEmpty(values[11]) && !string.IsNullOrEmpty(values[14]))
                    {
                        currentFrame.rightRootPosition = new Vector3(
                            float.Parse(values[11], invariantCulture),
                            float.Parse(values[12], invariantCulture),
                            float.Parse(values[13], invariantCulture)
                        );
                        currentFrame.rightRootRotation = new Quaternion(
                            float.Parse(values[14], invariantCulture),
                            float.Parse(values[15], invariantCulture),
                            float.Parse(values[16], invariantCulture),
                            float.Parse(values[17], invariantCulture)
                        );
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"라인 {i} 파싱 실패: {e.Message}\n라인 내용: {lines[i]}");
                continue;
            }
        }

        if (currentFrame != null) loadedSequence.Add(currentFrame);

        if (loadedSequence.Count == 0)
        {
            Debug.LogError("[HandPosePlayer] CSV 파싱 실패.");
            return;
        }

        // 재생 시작
        currentLoadedFile = csvFileName;
        totalDuration = loadedSequence[loadedSequence.Count - 1].timestamp;

        if (playLeftHand)
        {
            isLeftPlaying = true;
            currentLeftPlaybackIndex = 0;
            lastLeftPlaybackTime = Time.time;
            leftElapsedTime = 0f;
            ApplyLeftHandFrame();
        }

        if (playRightHand)
        {
            isRightPlaying = true;
            currentRightPlaybackIndex = 0;
            lastRightPlaybackTime = Time.time;
            rightElapsedTime = 0f;
            ApplyRightHandFrame();
        }

        OnPlaybackStarted?.Invoke();

        string modeText = playbackMode == PlaybackMode.PlaybackOnly ? "재생 전용" :
                         playbackMode == PlaybackMode.ComparisonMode ? "비교" : "수동";

        Debug.Log($"<color=cyan>[Resources] 재생 시작 ({modeText} 모드)</color> - 총 {loadedSequence.Count} 프레임");
    }
    // ========== 기존 메서드들 모두 유지 ==========

    public void StartLeftHandPlayback(string csvFileName)
    {
        playLeftHand = true;
        playRightHand = false;
        StartPlaybackFromCSV(csvFileName);
    }

    public void StartRightHandPlayback(string csvFileName)
    {
        playLeftHand = false;
        playRightHand = true;
        StartPlaybackFromCSV(csvFileName);
    }

    public void StartBothHandsPlayback(string csvFileName)
    {
        playLeftHand = true;
        playRightHand = true;
        StartPlaybackFromCSV(csvFileName);
    }

    public void StopPlayback()
    {
        isLeftPlaying = false;
        isRightPlaying = false;
        currentLeftPlaybackIndex = 0;
        currentRightPlaybackIndex = 0;
        Debug.Log("재생 중지.");
    }

    public void StopLeftHand()
    {
        isLeftPlaying = false;
        currentLeftPlaybackIndex = 0;
    }

    public void StopRightHand()
    {
        isRightPlaying = false;
        currentRightPlaybackIndex = 0;
    }

    public void SetHandPositionComparison(bool enable, float posThreshold, float rotThreshold)
    {
        compareHandPosition = enable;
        handPositionThreshold = posThreshold;
        handRotationThreshold = rotThreshold;
        Debug.Log($"손 위치 비교: {enable}, 위치 임계값: {posThreshold * 100}cm, 회전 임계값: {rotThreshold}°");
    }

    public void SetReferencePoint(Transform reference)
    {
        referencePoint = reference;
        Debug.Log($"기준점 설정: {(reference != null ? reference.name + " at " + reference.position : "없음")}");
    }

    public void SetReplayHandsVisible(bool visible)
    {
        showReplayHands = visible;

        if (useLeftMapper && leftHandMapper != null)
        {
            leftHandMapper.SetVisible(visible);
        }
        else if (leftHandVisual != null)
        {
            SkinnedMeshRenderer[] leftRenderers = leftHandVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var renderer in leftRenderers)
            {
                renderer.enabled = visible;
            }
        }

        if (useRightMapper && rightHandMapper != null)
        {
            rightHandMapper.SetVisible(visible);
        }
        else if (rightHandVisual != null)
        {
            SkinnedMeshRenderer[] rightRenderers = rightHandVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var renderer in rightRenderers)
            {
                renderer.enabled = visible;
            }
        }

        Debug.Log($"리플레이 손 표시: {(visible ? "ON" : "OFF")}");
    }

    public void SetReplayHandAlpha(float alpha)
    {
        replayHandAlpha = Mathf.Clamp01(alpha);

        if (useLeftMapper && leftHandMapper != null)
        {
            leftHandMapper.SetAlpha(replayHandAlpha);
        }
        else if (leftHandVisual != null)
        {
            SkinnedMeshRenderer[] leftRenderers = leftHandVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var renderer in leftRenderers)
            {
                Material mat = renderer.material;
                if (mat.HasProperty("_Color"))
                {
                    Color color = mat.color;
                    color.a = replayHandAlpha;
                    mat.color = color;
                }
            }
        }

        if (useRightMapper && rightHandMapper != null)
        {
            rightHandMapper.SetAlpha(replayHandAlpha);
        }
        else if (rightHandVisual != null)
        {
            SkinnedMeshRenderer[] rightRenderers = rightHandVisual.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var renderer in rightRenderers)
            {
                Material mat = renderer.material;
                if (mat.HasProperty("_Color"))
                {
                    Color color = mat.color;
                    color.a = replayHandAlpha;
                    mat.color = color;
                }
            }
        }

        Debug.Log($"리플레이 손 투명도: {replayHandAlpha * 100}%");
    }

    public void SetDebugGizmos(bool show)
    {
        showDebugGizmos = show;
    }

    public void SetPositionLines(bool show)
    {
        showPositionLines = show;
    }

    public float GetLeftHandProgress()
    {
        if (loadedSequence.Count == 0) return 0f;
        return (float)currentLeftPlaybackIndex / loadedSequence.Count;
    }

    public float GetRightHandProgress()
    {
        if (loadedSequence.Count == 0) return 0f;
        return (float)currentRightPlaybackIndex / loadedSequence.Count;
    }

    public string GetStatusText()
    {
        string leftStatus = isLeftPlaying ? $"재생중 ({currentLeftPlaybackIndex + 1}/{loadedSequence.Count})" : "중지";
        string rightStatus = isRightPlaying ? $"재생중 ({currentRightPlaybackIndex + 1}/{loadedSequence.Count})" : "중지";

        return $"왼손: {leftStatus}\n" +
               $"  조인트 유사도: {currentLeftSimilarity * 100:F1}%\n" +
               $"  위치 오차: {leftHandPositionError * 100:F1}cm\n" +
               $"  회전 오차: {leftHandRotationError:F1}°\n" +
               $"오른손: {rightStatus}\n" +
               $"  조인트 유사도: {currentRightSimilarity * 100:F1}%\n" +
               $"  위치 오차: {rightHandPositionError * 100:F1}cm\n" +
               $"  회전 오차: {rightHandRotationError:F1}°";
    }

    public Vector3 GetLeftHandPosition()
    {
        if (playerLeftHand != null && playerLeftHand.Joints.Count > 0)
        {
            return playerLeftHand.Joints[(int)HandJointId.HandWristRoot].position;
        }
        return Vector3.zero;
    }

    public Vector3 GetRightHandPosition()
    {
        if (playerRightHand != null && playerRightHand.Joints.Count > 0)
        {
            return playerRightHand.Joints[(int)HandJointId.HandWristRoot].position;
        }
        return Vector3.zero;
    }

    // 추가 메서드들
    public bool IsLeftHandPlaying() => isLeftPlaying;
    public bool IsRightHandPlaying() => isRightPlaying;
    public float GetLeftSimilarity() => currentLeftSimilarity;
    public float GetRightSimilarity() => currentRightSimilarity;
    public float GetLeftHandPositionError() => leftHandPositionError;
    public float GetRightHandPositionError() => rightHandPositionError;

    public void SetThresholds(float pos, float rot, float sim)
    {
        positionThreshold = pos;
        rotationThreshold = rot;
        similarityReferenceValue = sim;
    }

    public void LoadCSV(string csvFileName)
    {
        StartPlaybackFromCSV(csvFileName);
    }

    public void LoadFromCSV(string csvFileName)
    {
        currentLoadedFile = csvFileName;
        StartPlaybackFromCSV(csvFileName);
    }

    public void StartPlayback()
    {
        if (!string.IsNullOrEmpty(currentLoadedFile))
        {
            StartPlaybackFromCSV(currentLoadedFile);
        }
    }

    public void StopAllPlayback()
    {
        StopPlayback();
    }

    public void PausePlayback()
    {
        isPaused = true;
        Debug.Log("재생 일시정지");
    }

    public void ResumePlayback()
    {
        isPaused = false;
        Debug.Log("재생 재개");
    }

    public void SetPlaybackSpeed(float speed)
    {
        playbackSpeed = Mathf.Clamp(speed, 0.1f, 4.0f);
        Debug.Log($"재생 속도: {playbackSpeed}x");
    }

    public float GetPlaybackSpeed() => playbackSpeed;

    public void SeekToTime(float time)
    {
        if (loadedSequence.Count == 0) return;
        currentPlayTime = Mathf.Clamp(time, 0f, totalDuration);

        int targetFrame = 0;
        for (int i = 0; i < loadedSequence.Count; i++)
        {
            if (loadedSequence[i].timestamp <= time)
                targetFrame = i;
            else
                break;
        }

        currentLeftPlaybackIndex = targetFrame;
        currentRightPlaybackIndex = targetFrame;

        ApplyLeftHandFrame();
        ApplyRightHandFrame();
    }

    public void GoToFrame(int frameIndex)
    {
        if (loadedSequence.Count == 0) return;

        frameIndex = Mathf.Clamp(frameIndex, 0, loadedSequence.Count - 1);

        currentLeftPlaybackIndex = frameIndex;
        currentRightPlaybackIndex = frameIndex;

        ApplyLeftHandFrame();
        ApplyRightHandFrame();
    }

    public float GetCurrentTime()
    {
        if (loadedSequence.Count == 0) return 0f;
        int currentIndex = Mathf.Max(currentLeftPlaybackIndex, currentRightPlaybackIndex);
        if (currentIndex < loadedSequence.Count)
            return loadedSequence[currentIndex].timestamp;
        return currentPlayTime;
    }

    public float GetTotalDuration() => totalDuration;

    public float GetOverallProgress()
    {
        if (loadedSequence.Count == 0) return 0f;

        float leftProgress = playLeftHand ? (float)currentLeftPlaybackIndex / loadedSequence.Count : 1f;
        float rightProgress = playRightHand ? (float)currentRightPlaybackIndex / loadedSequence.Count : 1f;

        if (playLeftHand && playRightHand)
            return (leftProgress + rightProgress) / 2f;
        else if (playLeftHand)
            return leftProgress;
        else if (playRightHand)
            return rightProgress;
        else
            return 0f;
    }

    public void SetPlaybackMode(PlaybackMode mode)
    {
        playbackMode = mode;
        Debug.Log($"재생 모드 변경: {mode}");
    }

    public PlaybackMode GetPlaybackMode() => playbackMode;

    public void EnablePlaybackOnlyMode()
    {
        playbackMode = PlaybackMode.PlaybackOnly;
        compareLeftHand = false;
        compareRightHand = false;
        playLeftHand = true;
        playRightHand = true;
        Debug.Log("재생 전용 모드 활성화");
    }

    public void EnableComparisonMode()
    {
        playbackMode = PlaybackMode.ComparisonMode;
        compareLeftHand = true;
        compareRightHand = true;
        Debug.Log("비교 모드 활성화 - 유사도 표시만");
    }

    public void EnablePlaybackWithComparison()
    {
        playbackMode = PlaybackMode.PlaybackWithComparison;
        compareLeftHand = true;
        compareRightHand = true;
        Debug.Log("재생+비교 모드 활성화 - 독립 실행");
    }

    public void EnableManualControl()
    {
        playbackMode = PlaybackMode.ManualControl;
        Debug.Log("수동 제어 모드 활성화");
    }

    public void NextFrame()
    {
        if (playbackMode != PlaybackMode.ManualControl)
        {
            Debug.LogWarning("수동 제어는 ManualControl 모드에서만 가능합니다.");
            return;
        }

        if (isLeftPlaying && currentLeftPlaybackIndex < loadedSequence.Count)
        {
            ApplyLeftHandFrame();
            currentLeftPlaybackIndex++;
        }

        if (isRightPlaying && currentRightPlaybackIndex < loadedSequence.Count)
        {
            ApplyRightHandFrame();
            currentRightPlaybackIndex++;
        }
    }

    void OnDrawGizmos()
    {
        if (!showDebugGizmos || !Application.isPlaying)
            return;

        if (referencePoint != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(referencePoint.position, 0.05f);
            Gizmos.DrawLine(referencePoint.position, referencePoint.position + Vector3.up * 0.1f);
        }

        if (isLeftPlaying && showPositionLines)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawSphere(leftReplayTargetPosition, gizmoSphereSize);

            Gizmos.color = Color.green;
            Gizmos.DrawSphere(leftPlayerCurrentPosition, gizmoSphereSize);

            Gizmos.color = leftHandPositionError <= handPositionThreshold ? Color.green : Color.red;
            Gizmos.DrawLine(leftReplayTargetPosition, leftPlayerCurrentPosition);
        }

        if (isRightPlaying && showPositionLines)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(rightReplayTargetPosition, gizmoSphereSize);

            Gizmos.color = Color.green;
            Gizmos.DrawSphere(rightPlayerCurrentPosition, gizmoSphereSize);

            Gizmos.color = rightHandPositionError <= handPositionThreshold ? Color.green : Color.red;
            Gizmos.DrawLine(rightReplayTargetPosition, rightPlayerCurrentPosition);
        }
    }

    /// <summary>
    /// 시퀀스 완료 체크 (시나리오 연동용)
    /// </summary>
    private void CheckSequenceCompletion()
    {
        bool leftCompleted = !playLeftHand || !isLeftPlaying;
        bool rightCompleted = !playRightHand || !isRightPlaying;

        if (leftCompleted && rightCompleted && loadedSequence.Count > 0)
        {
            hasNotifiedCompletion = true;
            Debug.Log("<color=yellow>[HandPosePlayer] 시퀀스 완료 - 시나리오에 알림</color>");
            OnSequenceCompleted?.Invoke();
        }
    }

    /// <summary>
    /// 사용자 동작 완료 체크
    /// 양손 모두 목표 지점까지 도달했는지 확인
    /// </summary>
    private void CheckUserCompletion()
    {
        if (hasNotifiedUserCompletion)
            return;

        bool leftDone = !compareLeftHand || userCompletedLeft;
        bool rightDone = !compareRightHand || userCompletedRight;

        if (leftDone && rightDone)
        {
            hasNotifiedUserCompletion = true;
            Debug.Log("<color=green>======================</color>");
            Debug.Log("<color=green>✓✓ 사용자 동작 완료! ✓✓</color>");
            Debug.Log($"<color=green>  왼손: {userLeftProgress + 1}프레임 도달</color>");
            Debug.Log($"<color=green>  오른손: {userRightProgress + 1}프레임 도달</color>");
            Debug.Log("<color=green>======================</color>");

            OnUserProgressCompleted?.Invoke();

            // 시나리오 통합 시 이 이벤트로 다음 단계 진행
            if (integrateWithScenario)
            {
                OnSequenceCompleted?.Invoke();
            }
        }
    }

    /// <summary>
    /// 시퀀스 재시작 시 플래그 초기화
    /// </summary>
    private void ResetCompletionFlag()
    {
        hasNotifiedCompletion = false;
        hasNotifiedUserCompletion = false;
        comparisonElapsedTime = 0f;

        // 사용자 진행 초기화
        userLeftProgress = 0;
        userRightProgress = 0;
        userCompletedLeft = false;
        userCompletedRight = false;
    }

    /// <summary>
    /// 현재 유사도 정보 가져오기 (외부 UI/피드백 시스템용)
    /// HandPoseProgressIndicator, HandPoseSimilarityUI 등에서 사용
    /// </summary>
    public SimilarityResult GetCurrentSimilarity()
    {
        return new SimilarityResult
        {
            leftHandSimilarity = currentLeftSimilarity,
            rightHandSimilarity = currentRightSimilarity,
            leftHandPassed = isLeftHandSimilar,
            rightHandPassed = isRightHandSimilar,
            overallPassed = isLeftHandSimilar && isRightHandSimilar,
            leftHandPositionError = leftHandPositionError,
            rightHandPositionError = rightHandPositionError,
            leftHandRotationError = leftHandRotationError,
            rightHandRotationError = rightHandRotationError,
            leftHandPositionPassed = leftHandPositionError <= handPositionThreshold,
            rightHandPositionPassed = rightHandPositionError <= handPositionThreshold
        };
    }

    /// <summary>
    /// 사용자 진행도 정보 가져오기 (외부 UI용)
    /// </summary>
    public (int leftProgress, int rightProgress, bool leftCompleted, bool rightCompleted) GetUserProgress()
    {
        return (userLeftProgress, userRightProgress, userCompletedLeft, userCompletedRight);
    }

    /// <summary>
    /// 현재 재생 상태 정보 가져오기 (외부 UI용)
    /// </summary>
    public (bool leftPlaying, bool rightPlaying, int leftFrame, int rightFrame, int totalFrames) GetPlaybackState()
    {
        return (isLeftPlaying, isRightPlaying, currentLeftPlaybackIndex, currentRightPlaybackIndex, loadedSequence.Count);
    }

    /// <summary>
    /// CSV 바이트 배열을 올바른 인코딩으로 디코딩
    /// UTF-8, EUC-KR 자동 감지 (ScenarioCSVLoader와 동일한 로직)
    /// </summary>
    private string DecodeCSVText(byte[] bytes)
    {
        // 1. UTF-8 BOM 체크
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            Debug.Log("[HandPosePlayer] ✓ UTF-8 BOM 감지 - UTF-8로 디코딩");
            return System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        // 2. UTF-8 시도 (BOM 없음)
        try
        {
            string utf8Text = System.Text.Encoding.UTF8.GetString(bytes);

            // UTF-8 디코딩 오류 체크 (� 문자가 있으면 잘못된 인코딩)
            if (!utf8Text.Contains("�"))
            {
                // 한글이 제대로 디코딩되었는지 확인
                bool hasKorean = ContainsKoreanCharacters(utf8Text);

                if (hasKorean || !ContainsKoreanBytes(bytes))
                {
                    Debug.Log("[HandPosePlayer] ✓ UTF-8 인코딩 사용");
                    return utf8Text;
                }
            }
        }
        catch
        {
            Debug.LogWarning("[HandPosePlayer] UTF-8 디코딩 실패");
        }

        // 3. EUC-KR 시도
        try
        {
            System.Text.Encoding euckr = System.Text.Encoding.GetEncoding("euc-kr");
            string euckrText = euckr.GetString(bytes);

            Debug.Log("[HandPosePlayer] ✓ EUC-KR 인코딩 사용");
            return euckrText;
        }
        catch
        {
            Debug.LogWarning("[HandPosePlayer] EUC-KR 디코딩 실패");
        }

        // 4. 최후의 수단: 시스템 기본 인코딩
        Debug.LogWarning("[HandPosePlayer] ⚠ 기본 인코딩 사용 (한글이 깨질 수 있음)");
        return System.Text.Encoding.Default.GetString(bytes);
    }

    /// <summary>
    /// 문자열에 한글 문자가 있는지 확인
    /// 한글 유니코드 범위: AC00-D7A3 (가-힣)
    /// </summary>
    private bool ContainsKoreanCharacters(string text)
    {
        foreach (char c in text)
        {
            if (c >= 0xAC00 && c <= 0xD7A3)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 바이트 배열에 한글 바이트가 있는지 확인
    /// EUC-KR 한글 범위: 첫 바이트 0xB0-0xC8, 두 번째 바이트 0xA1-0xFE
    /// </summary>
    private bool ContainsKoreanBytes(byte[] bytes)
    {
        for (int i = 0; i < bytes.Length - 1; i++)
        {
            // EUC-KR 한글 범위 체크
            if (bytes[i] >= 0xB0 && bytes[i] <= 0xC8 &&
                bytes[i + 1] >= 0xA1 && bytes[i + 1] <= 0xFE)
            {
                return true;
            }
        }
        return false;
    }
}