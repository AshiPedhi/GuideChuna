using System;
using System.Collections.Generic;
using UnityEngine;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using static HandPoseDataLoader;

/// <summary>
/// 체크포인트 기반 추나 시술 평가 시스템
///
/// 동작 흐름:
/// 1. CSV 데이터에서 체크포인트 자동 생성
/// 2. 손이 체크포인트 통과 시 손모양 유사도 체크
/// 3. 한계 초과 시 경고 및 감점
/// 4. 모든 체크포인트 통과 시 완료
/// </summary>
public class ChunaPathEvaluator : MonoBehaviour
{
    [Header("=== 체크포인트 설정 ===")]
    [SerializeField] private List<PathCheckpoint> checkpoints = new List<PathCheckpoint>();
    [SerializeField] private Transform checkpointParent;

    [Header("=== 손 참조 ===")]
    [SerializeField] private HandVisual playerLeftHand;
    [SerializeField] private HandVisual playerRightHand;

    [Header("=== 모듈 참조 ===")]
    [SerializeField] private ChunaLimitChecker limitChecker;
    [SerializeField] private DeductionRecord deductionRecord;
    [SerializeField] private SafePositionManager safePositionManager;
    [SerializeField] private HandPoseComparator poseComparator;

    [Header("=== 가이드 손 표시 ===")]
    [SerializeField] private HandTransformMapper leftGuideHand;
    [SerializeField] private HandTransformMapper rightGuideHand;
    [SerializeField] private bool showGuideHands = true;
    [SerializeField] private Color guideHandColor = new Color(0.3f, 0.7f, 1f, 0.5f);

    [Header("=== 평가 설정 ===")]
    [Tooltip("순차 통과 필수 (1→2→3 순서로)")]
    [SerializeField] private bool requireSequentialPass = true;

    [Tooltip("유사도 체크 간격 (초)")]
    [SerializeField] private float similarityCheckInterval = 0.2f;

    [Tooltip("한계 체크 간격 (초)")]
    [SerializeField] private float limitCheckInterval = 0.1f;

    [Header("=== 자동 생성 설정 ===")]
    [Tooltip("체크포인트 간격 (프레임)")]
    [SerializeField] private int checkpointFrameInterval = 10;

    [Tooltip("체크포인트 트리거 반경 (미터)")]
    [SerializeField] private float checkpointRadius = 0.08f;

    [Tooltip("체크포인트 홀드 시간 (초)")]
    [SerializeField] private float checkpointHoldTime = 0.5f;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = true;

    // 상태
    private bool isEvaluating = false;
    private int currentCheckpointIndex = 0;
    private float evaluationStartTime;
    private float lastSimilarityCheckTime;
    private float lastLimitCheckTime;

    // 데이터
    private List<PoseFrame> loadedFrames = new List<PoseFrame>();
    private string currentProcedureName = "";

    // 결과
    private EvaluationSession currentSession;

    // 이벤트
    public event Action OnEvaluationStarted;
    public event Action<EvaluationSession> OnEvaluationCompleted;
    public event Action<PathCheckpoint> OnCheckpointActivated;
    public event Action<PathCheckpoint, float> OnCheckpointPassed;
    public event Action<int, int> OnProgressChanged;  // current, total

    /// <summary>
    /// 평가 세션 데이터
    /// </summary>
    [System.Serializable]
    public class EvaluationSession
    {
        public string procedureName;
        public DateTime startTime;
        public DateTime endTime;
        public float duration;
        public int totalCheckpoints;
        public int passedCheckpoints;
        public List<CheckpointRecord> checkpointRecords = new List<CheckpointRecord>();
        public float averageSimilarity;
        public int limitViolations;
        public float finalScore;
        public string grade;

        [System.Serializable]
        public class CheckpointRecord
        {
            public int index;
            public string name;
            public float similarity;
            public float holdTime;
            public float timestamp;
            public bool passed;
        }
    }

    void Awake()
    {
        if (poseComparator == null)
        {
            poseComparator = new HandPoseComparator();
        }

        if (checkpointParent == null)
        {
            checkpointParent = new GameObject("Checkpoints").transform;
            checkpointParent.SetParent(transform);
        }
    }

    void Start()
    {
        FindModules();
        ConnectEvents();
    }

    void Update()
    {
        if (!isEvaluating) return;

        float currentTime = Time.time;

        // 유사도 체크
        if (currentTime - lastSimilarityCheckTime >= similarityCheckInterval)
        {
            lastSimilarityCheckTime = currentTime;
            UpdateCurrentCheckpointSimilarity();
        }

        // 한계 체크 (ChunaLimitChecker가 처리)
        // 결과는 이벤트로 받음
    }

    void OnDestroy()
    {
        DisconnectEvents();
    }

    /// <summary>
    /// 모듈 자동 탐색
    /// </summary>
    private void FindModules()
    {
        if (limitChecker == null)
            limitChecker = FindObjectOfType<ChunaLimitChecker>();

        if (deductionRecord == null)
            deductionRecord = FindObjectOfType<DeductionRecord>();

        if (safePositionManager == null)
            safePositionManager = FindObjectOfType<SafePositionManager>();

        // 손 찾기
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
    /// 이벤트 연결
    /// </summary>
    private void ConnectEvents()
    {
        // 한계 체커 이벤트
        if (limitChecker != null)
        {
            limitChecker.OnViolationDetected += OnLimitViolation;
        }

        // 체크포인트 이벤트
        foreach (var checkpoint in checkpoints)
        {
            ConnectCheckpointEvents(checkpoint);
        }
    }

    /// <summary>
    /// 이벤트 연결 해제
    /// </summary>
    private void DisconnectEvents()
    {
        if (limitChecker != null)
        {
            limitChecker.OnViolationDetected -= OnLimitViolation;
        }

        foreach (var checkpoint in checkpoints)
        {
            DisconnectCheckpointEvents(checkpoint);
        }
    }

    /// <summary>
    /// 체크포인트 이벤트 연결
    /// </summary>
    private void ConnectCheckpointEvents(PathCheckpoint checkpoint)
    {
        checkpoint.OnCheckpointPassed += HandleCheckpointPassed;
        checkpoint.OnCheckpointEntered += HandleCheckpointEntered;
    }

    /// <summary>
    /// 체크포인트 이벤트 해제
    /// </summary>
    private void DisconnectCheckpointEvents(PathCheckpoint checkpoint)
    {
        checkpoint.OnCheckpointPassed -= HandleCheckpointPassed;
        checkpoint.OnCheckpointEntered -= HandleCheckpointEntered;
    }

    // ========== CSV 데이터 기반 체크포인트 생성 ==========

    /// <summary>
    /// CSV 파일 로드 및 체크포인트 생성
    /// </summary>
    public void LoadAndGenerateCheckpoints(string csvFileName)
    {
        if (showDebugLogs)
            Debug.Log($"<color=cyan>[ChunaPathEvaluator] CSV 로드: {csvFileName}</color>");

        currentProcedureName = csvFileName;

        // 데이터 로드
        HandPoseDataLoader loader = new HandPoseDataLoader();
        var result = loader.LoadFromResources($"HandPoseData/{csvFileName}");

        if (!result.success)
        {
            Debug.LogError($"[ChunaPathEvaluator] CSV 로드 실패: {result.errorMessage}");
            return;
        }

        loadedFrames = result.frames;

        // 기존 체크포인트 정리
        ClearCheckpoints();

        // 체크포인트 생성
        GenerateCheckpointsFromFrames();

        if (showDebugLogs)
            Debug.Log($"<color=green>[ChunaPathEvaluator] {checkpoints.Count}개 체크포인트 생성 완료</color>");
    }

    /// <summary>
    /// 프레임 데이터에서 체크포인트 생성
    /// </summary>
    private void GenerateCheckpointsFromFrames()
    {
        if (loadedFrames == null || loadedFrames.Count == 0) return;

        int checkpointCount = 0;

        for (int i = 0; i < loadedFrames.Count; i += checkpointFrameInterval)
        {
            PoseFrame frame = loadedFrames[i];

            // 체크포인트 위치 계산 (양손 중간점)
            Vector3 checkpointPos = (frame.leftRootPosition + frame.rightRootPosition) / 2f;

            // 체크포인트 생성
            GameObject cpObj = new GameObject($"Checkpoint_{checkpointCount}");
            cpObj.transform.SetParent(checkpointParent);
            cpObj.transform.position = checkpointPos;

            PathCheckpoint checkpoint = cpObj.AddComponent<PathCheckpoint>();

            // 시작/끝 체크포인트 설정
            bool isStart = (i == 0);
            bool isEnd = (i + checkpointFrameInterval >= loadedFrames.Count);

            string cpName = isStart ? "시작" : (isEnd ? "종료" : $"구간 {checkpointCount}");

            checkpoint.Initialize(
                index: checkpointCount,
                name: cpName,
                position: checkpointPos,
                leftHandPos: frame.leftRootPosition,
                leftHandRot: frame.leftRootRotation,
                rightHandPos: frame.rightRootPosition,
                rightHandRot: frame.rightRootRotation,
                holdTime: checkpointHoldTime,
                similarity: 0.6f
            );

            checkpoint.SetTriggerRadius(checkpointRadius);

            // 이벤트 연결
            ConnectCheckpointEvents(checkpoint);

            checkpoints.Add(checkpoint);
            checkpointCount++;
        }

        // 마지막 프레임이 체크포인트가 아니면 추가
        int lastFrameIndex = loadedFrames.Count - 1;
        if (lastFrameIndex % checkpointFrameInterval != 0)
        {
            PoseFrame lastFrame = loadedFrames[lastFrameIndex];
            Vector3 endPos = (lastFrame.leftRootPosition + lastFrame.rightRootPosition) / 2f;

            GameObject endObj = new GameObject($"Checkpoint_{checkpointCount}_End");
            endObj.transform.SetParent(checkpointParent);
            endObj.transform.position = endPos;

            PathCheckpoint endCheckpoint = endObj.AddComponent<PathCheckpoint>();
            endCheckpoint.Initialize(
                index: checkpointCount,
                name: "종료",
                position: endPos,
                leftHandPos: lastFrame.leftRootPosition,
                leftHandRot: lastFrame.leftRootRotation,
                rightHandPos: lastFrame.rightRootPosition,
                rightHandRot: lastFrame.rightRootRotation,
                holdTime: checkpointHoldTime,
                similarity: 0.6f
            );

            ConnectCheckpointEvents(endCheckpoint);
            checkpoints.Add(endCheckpoint);
        }
    }

    /// <summary>
    /// 체크포인트 정리
    /// </summary>
    private void ClearCheckpoints()
    {
        foreach (var cp in checkpoints)
        {
            if (cp != null)
            {
                DisconnectCheckpointEvents(cp);
                Destroy(cp.gameObject);
            }
        }
        checkpoints.Clear();
    }

    // ========== 평가 제어 ==========

    /// <summary>
    /// 평가 시작
    /// </summary>
    public void StartEvaluation()
    {
        if (checkpoints.Count == 0)
        {
            Debug.LogError("[ChunaPathEvaluator] 체크포인트가 없습니다!");
            return;
        }

        isEvaluating = true;
        currentCheckpointIndex = 0;
        evaluationStartTime = Time.time;
        lastSimilarityCheckTime = Time.time;
        lastLimitCheckTime = Time.time;

        // 세션 초기화
        currentSession = new EvaluationSession
        {
            procedureName = currentProcedureName,
            startTime = DateTime.Now,
            totalCheckpoints = checkpoints.Count,
            passedCheckpoints = 0,
            checkpointRecords = new List<EvaluationSession.CheckpointRecord>()
        };

        // 모든 체크포인트 리셋
        foreach (var cp in checkpoints)
        {
            cp.ResetCheckpoint();
        }

        // 첫 번째 체크포인트 활성화
        ActivateCheckpoint(0);

        // 한계 체커 시작
        if (limitChecker != null)
        {
            limitChecker.Initialize();
            limitChecker.SetEnabled(true);
        }

        // 감점 기록 시작
        if (deductionRecord != null)
        {
            deductionRecord.StartSession(currentProcedureName);
        }

        // 가이드 손 표시
        UpdateGuideHands();

        if (showDebugLogs)
            Debug.Log("<color=green>[ChunaPathEvaluator] 평가 시작!</color>");

        OnEvaluationStarted?.Invoke();
        OnProgressChanged?.Invoke(0, checkpoints.Count);
    }

    /// <summary>
    /// 평가 종료
    /// </summary>
    public EvaluationSession StopEvaluation()
    {
        if (!isEvaluating) return currentSession;

        isEvaluating = false;

        // 세션 완료
        currentSession.endTime = DateTime.Now;
        currentSession.duration = Time.time - evaluationStartTime;

        // 평균 유사도 계산
        if (currentSession.checkpointRecords.Count > 0)
        {
            float totalSim = 0f;
            foreach (var record in currentSession.checkpointRecords)
            {
                totalSim += record.similarity;
            }
            currentSession.averageSimilarity = totalSim / currentSession.checkpointRecords.Count;
        }

        // 감점 기록 종료
        if (deductionRecord != null)
        {
            var deductionResult = deductionRecord.EndSession();
            currentSession.finalScore = deductionResult.finalScore;
            currentSession.grade = deductionResult.grade;
            currentSession.limitViolations = deductionResult.totalDeductions;
        }

        // 한계 체커 정지
        if (limitChecker != null)
        {
            limitChecker.SetEnabled(false);
        }

        // 가이드 손 숨기기
        HideGuideHands();

        if (showDebugLogs)
        {
            Debug.Log("<color=green>========== 평가 완료 ==========</color>");
            Debug.Log($"통과: {currentSession.passedCheckpoints}/{currentSession.totalCheckpoints}");
            Debug.Log($"평균 유사도: {currentSession.averageSimilarity:P0}");
            Debug.Log($"최종 점수: {currentSession.finalScore:F0}점 ({currentSession.grade})");
        }

        OnEvaluationCompleted?.Invoke(currentSession);

        return currentSession;
    }

    /// <summary>
    /// 평가 리셋
    /// </summary>
    public void ResetEvaluation()
    {
        isEvaluating = false;
        currentCheckpointIndex = 0;

        foreach (var cp in checkpoints)
        {
            cp.ResetCheckpoint();
        }

        if (deductionRecord != null)
        {
            deductionRecord.ResetSession();
        }

        if (safePositionManager != null)
        {
            safePositionManager.Reset();
        }

        HideGuideHands();

        if (showDebugLogs)
            Debug.Log("[ChunaPathEvaluator] 평가 리셋");
    }

    // ========== 체크포인트 처리 ==========

    /// <summary>
    /// 체크포인트 활성화
    /// </summary>
    private void ActivateCheckpoint(int index)
    {
        if (index < 0 || index >= checkpoints.Count) return;

        // 순차 모드: 현재 체크포인트만 활성화
        if (requireSequentialPass)
        {
            foreach (var cp in checkpoints)
            {
                cp.Deactivate();
            }
        }

        checkpoints[index].Activate();
        currentCheckpointIndex = index;

        // 가이드 손 업데이트
        UpdateGuideHands();

        if (showDebugLogs)
            Debug.Log($"[ChunaPathEvaluator] 체크포인트 {index} 활성화: {checkpoints[index].CheckpointName}");

        OnCheckpointActivated?.Invoke(checkpoints[index]);
    }

    /// <summary>
    /// 체크포인트 진입 처리
    /// </summary>
    private void HandleCheckpointEntered(PathCheckpoint checkpoint, bool isLeftHand)
    {
        if (showDebugLogs)
        {
            string hand = isLeftHand ? "왼손" : "오른손";
            Debug.Log($"[ChunaPathEvaluator] {checkpoint.CheckpointName}에 {hand} 진입");
        }
    }

    /// <summary>
    /// 체크포인트 통과 처리
    /// </summary>
    private void HandleCheckpointPassed(PathCheckpoint checkpoint, bool isLeftHand, float similarity)
    {
        // 순차 모드: 순서 확인
        if (requireSequentialPass && checkpoint.CheckpointIndex != currentCheckpointIndex)
        {
            if (showDebugLogs)
                Debug.LogWarning($"[ChunaPathEvaluator] 순서 어긋남! 현재: {currentCheckpointIndex}, 통과 시도: {checkpoint.CheckpointIndex}");
            return;
        }

        // 기록 추가
        currentSession.checkpointRecords.Add(new EvaluationSession.CheckpointRecord
        {
            index = checkpoint.CheckpointIndex,
            name = checkpoint.CheckpointName,
            similarity = similarity,
            holdTime = checkpoint.RequiredHoldTime,
            timestamp = Time.time - evaluationStartTime,
            passed = true
        });

        currentSession.passedCheckpoints++;

        if (showDebugLogs)
            Debug.Log($"<color=green>[ChunaPathEvaluator] 체크포인트 {checkpoint.CheckpointIndex} 통과! (유사도: {similarity:P0})</color>");

        OnCheckpointPassed?.Invoke(checkpoint, similarity);
        OnProgressChanged?.Invoke(currentSession.passedCheckpoints, currentSession.totalCheckpoints);

        // 다음 체크포인트 활성화
        int nextIndex = checkpoint.CheckpointIndex + 1;
        if (nextIndex < checkpoints.Count)
        {
            ActivateCheckpoint(nextIndex);
        }
        else
        {
            // 모든 체크포인트 통과 - 평가 완료
            StopEvaluation();
        }
    }

    /// <summary>
    /// 한계 위반 처리
    /// </summary>
    private void OnLimitViolation(ChunaLimitChecker.ViolationEvent violation)
    {
        if (!isEvaluating) return;

        // 감점 기록에 추가
        if (deductionRecord != null)
        {
            deductionRecord.AddDeduction(violation);
        }

        if (showDebugLogs)
        {
            string hand = violation.isLeftHand ? "왼손" : "오른손";
            Debug.LogWarning($"<color=red>[ChunaPathEvaluator] 한계 위반: {hand} {violation.violationType}</color>");
        }
    }

    // ========== 유사도 체크 ==========

    /// <summary>
    /// 현재 체크포인트 유사도 업데이트
    /// </summary>
    private void UpdateCurrentCheckpointSimilarity()
    {
        if (currentCheckpointIndex >= checkpoints.Count) return;

        PathCheckpoint currentCP = checkpoints[currentCheckpointIndex];

        if (!currentCP.IsActive || !currentCP.CheckHandPose) return;

        // 현재 체크포인트의 참조 포즈와 비교할 프레임 인덱스 계산
        int frameIndex = currentCheckpointIndex * checkpointFrameInterval;
        if (frameIndex >= loadedFrames.Count)
            frameIndex = loadedFrames.Count - 1;

        PoseFrame referenceFrame = loadedFrames[frameIndex];

        // 왼손 유사도
        if (playerLeftHand != null && currentCP.IsHandInside(true))
        {
            var leftResult = poseComparator.CompareLeftPose(playerLeftHand, referenceFrame, frameIndex);
            currentCP.UpdateSimilarity(true, leftResult.leftHandSimilarity);
        }

        // 오른손 유사도
        if (playerRightHand != null && currentCP.IsHandInside(false))
        {
            var rightResult = poseComparator.CompareRightPose(playerRightHand, referenceFrame, frameIndex);
            currentCP.UpdateSimilarity(false, rightResult.rightHandSimilarity);
        }
    }

    // ========== 가이드 손 ==========

    /// <summary>
    /// 가이드 손 업데이트
    /// </summary>
    private void UpdateGuideHands()
    {
        if (!showGuideHands) return;

        if (currentCheckpointIndex >= checkpoints.Count) return;

        int frameIndex = currentCheckpointIndex * checkpointFrameInterval;
        if (frameIndex >= loadedFrames.Count)
            frameIndex = loadedFrames.Count - 1;

        PoseFrame frame = loadedFrames[frameIndex];

        // 왼손 가이드
        if (leftGuideHand != null)
        {
            leftGuideHand.SetVisible(true);
            leftGuideHand.SetColorAndAlpha(guideHandColor, guideHandColor.a);

            if (leftGuideHand.Root != null)
            {
                leftGuideHand.Root.position = frame.leftRootPosition;
                leftGuideHand.Root.rotation = frame.leftRootRotation;
            }

            foreach (var kvp in frame.leftLocalPoses)
            {
                leftGuideHand.SetJointLocalPose(kvp.Key, kvp.Value.position, kvp.Value.rotation);
            }
        }

        // 오른손 가이드
        if (rightGuideHand != null)
        {
            rightGuideHand.SetVisible(true);
            rightGuideHand.SetColorAndAlpha(guideHandColor, guideHandColor.a);

            if (rightGuideHand.Root != null)
            {
                rightGuideHand.Root.position = frame.rightRootPosition;
                rightGuideHand.Root.rotation = frame.rightRootRotation;
            }

            foreach (var kvp in frame.rightLocalPoses)
            {
                rightGuideHand.SetJointLocalPose(kvp.Key, kvp.Value.position, kvp.Value.rotation);
            }
        }
    }

    /// <summary>
    /// 가이드 손 숨기기
    /// </summary>
    private void HideGuideHands()
    {
        if (leftGuideHand != null)
            leftGuideHand.SetVisible(false);

        if (rightGuideHand != null)
            rightGuideHand.SetVisible(false);
    }

    // ========== Public API ==========

    /// <summary>
    /// 평가 중인지 확인
    /// </summary>
    public bool IsEvaluating => isEvaluating;

    /// <summary>
    /// 현재 세션 가져오기
    /// </summary>
    public EvaluationSession GetCurrentSession() => currentSession;

    /// <summary>
    /// 현재 체크포인트 인덱스
    /// </summary>
    public int CurrentCheckpointIndex => currentCheckpointIndex;

    /// <summary>
    /// 총 체크포인트 수
    /// </summary>
    public int TotalCheckpoints => checkpoints.Count;

    /// <summary>
    /// 체크포인트 간격 설정
    /// </summary>
    public void SetCheckpointInterval(int frameInterval)
    {
        checkpointFrameInterval = Mathf.Max(1, frameInterval);
    }

    /// <summary>
    /// 체크포인트 반경 설정
    /// </summary>
    public void SetCheckpointRadius(float radius)
    {
        checkpointRadius = Mathf.Max(0.01f, radius);

        foreach (var cp in checkpoints)
        {
            cp.SetTriggerRadius(radius);
        }
    }

    /// <summary>
    /// 진행률 가져오기
    /// </summary>
    public float GetProgress()
    {
        if (checkpoints.Count == 0) return 0f;
        return (float)currentSession.passedCheckpoints / checkpoints.Count;
    }
}
