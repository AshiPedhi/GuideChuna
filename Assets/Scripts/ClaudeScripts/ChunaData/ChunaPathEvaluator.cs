using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using static HandPoseDataLoader;

/// <summary>
/// 체크포인트 기반 추나 시술 평가 시스템 (개선 버전)
///
/// 개선 사항:
/// - 환자 위치 기준 상대 좌표 사용
/// - 좌/우 손 별도 체크포인트 생성
/// - 가이드 핸드 루프 재생 (경로 시각화)
/// - 체크포인트는 평가용으로만 사용
/// </summary>
public class ChunaPathEvaluator : MonoBehaviour
{
    [Header("=== 기준 위치 (환자) ===")]
    [Tooltip("체크포인트 위치의 기준점 (환자 Transform)")]
    [SerializeField] private Transform referenceTransform;

    [Tooltip("데이터 기록 시 환자의 위치 오프셋")]
    [SerializeField] private Vector3 recordedPatientOffset = Vector3.zero;

    [Header("=== 체크포인트 설정 ===")]
    [SerializeField] private List<PathCheckpoint> leftCheckpoints = new List<PathCheckpoint>();
    [SerializeField] private List<PathCheckpoint> rightCheckpoints = new List<PathCheckpoint>();
    [SerializeField] private Transform checkpointParent;

    [Header("=== 손 참조 ===")]
    [SerializeField] private HandVisual playerLeftHand;
    [SerializeField] private HandVisual playerRightHand;

    [Header("=== 모듈 참조 ===")]
    [SerializeField] private DeductionRecord deductionRecord;
    [SerializeField] private HandPoseComparator poseComparator;
    [SerializeField] private ChunaLimitChecker limitChecker;
    [SerializeField] private ChunaLimitData limitData;

    [Header("=== 가이드 손 표시 ===")]
    [SerializeField] private HandTransformMapper leftGuideHand;
    [SerializeField] private HandTransformMapper rightGuideHand;
    [SerializeField] private bool showGuideHands = true;
    [SerializeField] private Color guideHandColor = new Color(0.3f, 0.7f, 1f, 0.5f);

    [Tooltip("가이드 핸드 재생 속도 (1 = 원본 속도)")]
    [SerializeField] private float guidePlaybackSpeed = 1f;

    [Tooltip("가이드 핸드 루프 재생")]
    [SerializeField] private bool loopGuideHands = true;

    [Header("=== 평가 설정 ===")]
    [Tooltip("순차 통과 필수 (1→2→3 순서로)")]
    [SerializeField] private bool requireSequentialPass = true;

    [Tooltip("유사도 체크 간격 (초)")]
    [SerializeField] private float similarityCheckInterval = 0.2f;

    [Header("=== 자동 생성 설정 ===")]
    [Tooltip("체크포인트 간격 (프레임)")]
    [SerializeField] private int checkpointFrameInterval = 15;

    [Tooltip("체크포인트 트리거 반경 (미터)")]
    [SerializeField] private float checkpointRadius = 0.15f;

    [Tooltip("체크포인트 홀드 시간 (초)")]
    [SerializeField] private float checkpointHoldTime = 0.2f;

    [Tooltip("손 모양 유사도 체크 활성화")]
    [SerializeField] private bool checkHandPoseSimilarity = false;

    [Tooltip("통과에 필요한 최소 유사도")]
    [SerializeField][Range(0f, 1f)] private float requiredSimilarity = 0.3f;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = true;

    // 상태
    private bool isEvaluating = false;
    private int currentLeftCheckpointIndex = 0;
    private int currentRightCheckpointIndex = 0;
    private float evaluationStartTime;
    private float lastSimilarityCheckTime;

    // 데이터
    private List<PoseFrame> loadedFrames = new List<PoseFrame>();
    private string currentProcedureName = "";

    // 가이드 핸드 재생
    private Coroutine guideHandCoroutine;
    private int currentGuideFrameIndex = 0;

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
            public bool isLeftHand;
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
        FindReferences();
        FindModules();
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
    }

    void OnDestroy()
    {
        StopGuideHandPlayback();
        DisconnectAllCheckpointEvents();
    }

    /// <summary>
    /// 참조 자동 찾기
    /// </summary>
    private void FindReferences()
    {
        // 환자 기준점 찾기
        if (referenceTransform == null)
        {
            var patient = GameObject.FindGameObjectWithTag("Patient");
            if (patient != null)
            {
                referenceTransform = patient.transform;
                if (showDebugLogs)
                    Debug.Log($"[ChunaPathEvaluator] 환자 Transform 자동 연결: {patient.name}");
            }
        }
    }

    /// <summary>
    /// 모듈 자동 탐색
    /// </summary>
    private void FindModules()
    {
        if (deductionRecord == null)
            deductionRecord = FindObjectOfType<DeductionRecord>();

        if (limitChecker == null)
            limitChecker = FindObjectOfType<ChunaLimitChecker>();

        // 리밋 체커에 데이터 연결
        if (limitChecker != null && limitData != null)
        {
            limitChecker.SetLimitData(limitData);
        }

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

        if (showDebugLogs)
            Debug.Log($"[ChunaPathEvaluator] {loadedFrames.Count}개 프레임 로드됨");

        // 기존 체크포인트 정리
        ClearCheckpoints();

        // 체크포인트 생성 (좌/우 분리)
        GenerateCheckpointsFromFrames();

        if (showDebugLogs)
        {
            Debug.Log($"<color=green>[ChunaPathEvaluator] 체크포인트 생성 완료</color>");
            Debug.Log($"  - 왼손: {leftCheckpoints.Count}개");
            Debug.Log($"  - 오른손: {rightCheckpoints.Count}개");
        }
    }

    /// <summary>
    /// 프레임 데이터에서 체크포인트 생성 (좌/우 분리)
    /// </summary>
    private void GenerateCheckpointsFromFrames()
    {
        if (loadedFrames == null || loadedFrames.Count == 0) return;

        // 위치 오프셋 계산
        Vector3 positionOffset = Vector3.zero;
        if (referenceTransform != null)
        {
            // 현재 환자 위치 - 기록 당시 환자 위치 = 오프셋
            positionOffset = referenceTransform.position - recordedPatientOffset;

            if (showDebugLogs)
                Debug.Log($"[ChunaPathEvaluator] 위치 오프셋: {positionOffset}");
        }

        int checkpointCount = 0;

        for (int i = 0; i < loadedFrames.Count; i += checkpointFrameInterval)
        {
            PoseFrame frame = loadedFrames[i];

            // 왼손 체크포인트 생성
            Vector3 leftPos = frame.leftRootPosition + positionOffset;
            CreateCheckpoint(
                isLeftHand: true,
                index: checkpointCount,
                position: leftPos,
                frame: frame,
                checkpointList: leftCheckpoints
            );

            // 오른손 체크포인트 생성
            Vector3 rightPos = frame.rightRootPosition + positionOffset;
            CreateCheckpoint(
                isLeftHand: false,
                index: checkpointCount,
                position: rightPos,
                frame: frame,
                checkpointList: rightCheckpoints
            );

            checkpointCount++;
        }
    }

    /// <summary>
    /// 개별 체크포인트 생성
    /// </summary>
    private void CreateCheckpoint(bool isLeftHand, int index, Vector3 position, PoseFrame frame, List<PathCheckpoint> checkpointList)
    {
        string handName = isLeftHand ? "L" : "R";
        string cpName = $"{handName}_{index}";

        GameObject cpObj = new GameObject(cpName);
        cpObj.transform.SetParent(checkpointParent);
        cpObj.transform.position = position;

        PathCheckpoint checkpoint = cpObj.AddComponent<PathCheckpoint>();

        // 해당 손만 감지하도록 설정
        checkpoint.Initialize(
            index: index,
            name: cpName,
            position: position,
            leftHandPos: isLeftHand ? frame.leftRootPosition : Vector3.zero,
            leftHandRot: isLeftHand ? frame.leftRootRotation : Quaternion.identity,
            rightHandPos: isLeftHand ? Vector3.zero : frame.rightRootPosition,
            rightHandRot: isLeftHand ? Quaternion.identity : frame.rightRootRotation,
            holdTime: checkpointHoldTime,
            similarity: 0.5f
        );

        checkpoint.SetTriggerRadius(checkpointRadius);
        checkpoint.SetDetectHand(isLeftHand, !isLeftHand); // 한쪽 손만 감지
        checkpoint.SetPassConditions(checkpointHoldTime, requiredSimilarity, checkHandPoseSimilarity);

        // 이벤트 연결
        checkpoint.OnCheckpointPassed += (cp, isLeft, similarity) => HandleCheckpointPassed(cp, isLeft, similarity);

        checkpointList.Add(checkpoint);
    }

    /// <summary>
    /// 체크포인트 정리
    /// </summary>
    private void ClearCheckpoints()
    {
        foreach (var cp in leftCheckpoints)
        {
            if (cp != null) Destroy(cp.gameObject);
        }
        leftCheckpoints.Clear();

        foreach (var cp in rightCheckpoints)
        {
            if (cp != null) Destroy(cp.gameObject);
        }
        rightCheckpoints.Clear();
    }

    /// <summary>
    /// 모든 체크포인트 이벤트 해제
    /// </summary>
    private void DisconnectAllCheckpointEvents()
    {
        // PathCheckpoint는 Destroy 시 자동으로 정리됨
    }

    // ========== 평가 제어 ==========

    /// <summary>
    /// 평가 시작
    /// </summary>
    public void StartEvaluation()
    {
        int totalCheckpoints = leftCheckpoints.Count + rightCheckpoints.Count;

        if (totalCheckpoints == 0)
        {
            Debug.LogError("[ChunaPathEvaluator] 체크포인트가 없습니다!");
            return;
        }

        isEvaluating = true;
        currentLeftCheckpointIndex = 0;
        currentRightCheckpointIndex = 0;
        evaluationStartTime = Time.time;
        lastSimilarityCheckTime = Time.time;

        // 세션 초기화
        currentSession = new EvaluationSession
        {
            procedureName = currentProcedureName,
            startTime = DateTime.Now,
            totalCheckpoints = totalCheckpoints,
            passedCheckpoints = 0,
            checkpointRecords = new List<EvaluationSession.CheckpointRecord>()
        };

        // 모든 체크포인트 리셋
        ResetAllCheckpoints();

        // 첫 번째 체크포인트 활성화
        ActivateNextCheckpoints();

        // 감점 기록 시작
        if (deductionRecord != null)
        {
            deductionRecord.StartSession(currentProcedureName);
        }

        // 리밋 체커 시작
        if (limitChecker != null)
        {
            if (limitData != null)
            {
                limitChecker.SetLimitData(limitData);
            }
            limitChecker.Initialize();
            limitChecker.SetEnabled(true);
        }

        // 가이드 핸드 재생 시작
        StartGuideHandPlayback();

        if (showDebugLogs)
            Debug.Log("<color=green>[ChunaPathEvaluator] 평가 시작!</color>");

        OnEvaluationStarted?.Invoke();
        OnProgressChanged?.Invoke(0, totalCheckpoints);
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
        else
        {
            // 감점 없으면 유사도 기반 점수
            currentSession.finalScore = currentSession.averageSimilarity * 100f;
            currentSession.grade = GetGradeFromScore(currentSession.finalScore);
        }

        // 가이드 핸드 중지
        StopGuideHandPlayback();

        // 리밋 체커 중지
        if (limitChecker != null)
        {
            limitChecker.SetEnabled(false);
        }

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
        currentLeftCheckpointIndex = 0;
        currentRightCheckpointIndex = 0;

        ResetAllCheckpoints();
        StopGuideHandPlayback();

        if (deductionRecord != null)
        {
            deductionRecord.ResetSession();
        }

        if (showDebugLogs)
            Debug.Log("[ChunaPathEvaluator] 평가 리셋");
    }

    /// <summary>
    /// 모든 체크포인트 리셋
    /// </summary>
    private void ResetAllCheckpoints()
    {
        foreach (var cp in leftCheckpoints) cp?.ResetCheckpoint();
        foreach (var cp in rightCheckpoints) cp?.ResetCheckpoint();
    }

    /// <summary>
    /// 다음 체크포인트 활성화
    /// </summary>
    private void ActivateNextCheckpoints()
    {
        // 왼손 체크포인트
        if (currentLeftCheckpointIndex < leftCheckpoints.Count)
        {
            leftCheckpoints[currentLeftCheckpointIndex].Activate();
        }

        // 오른손 체크포인트
        if (currentRightCheckpointIndex < rightCheckpoints.Count)
        {
            rightCheckpoints[currentRightCheckpointIndex].Activate();
        }
    }

    // ========== 체크포인트 처리 ==========

    /// <summary>
    /// 체크포인트 통과 처리
    /// </summary>
    private void HandleCheckpointPassed(PathCheckpoint checkpoint, bool isLeftHand, float similarity)
    {
        if (!isEvaluating) return;

        // 기록 추가
        currentSession.checkpointRecords.Add(new EvaluationSession.CheckpointRecord
        {
            index = checkpoint.CheckpointIndex,
            name = checkpoint.CheckpointName,
            similarity = similarity,
            holdTime = checkpoint.RequiredHoldTime,
            timestamp = Time.time - evaluationStartTime,
            passed = true,
            isLeftHand = isLeftHand
        });

        currentSession.passedCheckpoints++;

        if (showDebugLogs)
        {
            string hand = isLeftHand ? "왼손" : "오른손";
            Debug.Log($"<color=green>[ChunaPathEvaluator] {hand} 체크포인트 {checkpoint.CheckpointIndex} 통과! (유사도: {similarity:P0})</color>");
        }

        OnCheckpointPassed?.Invoke(checkpoint, similarity);
        OnProgressChanged?.Invoke(currentSession.passedCheckpoints, currentSession.totalCheckpoints);

        // 다음 체크포인트 활성화
        if (isLeftHand)
        {
            currentLeftCheckpointIndex++;
            if (currentLeftCheckpointIndex < leftCheckpoints.Count)
            {
                leftCheckpoints[currentLeftCheckpointIndex].Activate();
            }
        }
        else
        {
            currentRightCheckpointIndex++;
            if (currentRightCheckpointIndex < rightCheckpoints.Count)
            {
                rightCheckpoints[currentRightCheckpointIndex].Activate();
            }
        }

        // 모든 체크포인트 통과 확인
        if (currentLeftCheckpointIndex >= leftCheckpoints.Count &&
            currentRightCheckpointIndex >= rightCheckpoints.Count)
        {
            StopEvaluation();
        }
    }

    // ========== 유사도 체크 ==========

    /// <summary>
    /// 현재 체크포인트 유사도 업데이트
    /// </summary>
    private void UpdateCurrentCheckpointSimilarity()
    {
        // 왼손 체크포인트 유사도
        if (currentLeftCheckpointIndex < leftCheckpoints.Count)
        {
            var leftCP = leftCheckpoints[currentLeftCheckpointIndex];
            if (leftCP.IsActive && leftCP.IsHandInside(true) && playerLeftHand != null)
            {
                int frameIndex = leftCP.CheckpointIndex * checkpointFrameInterval;
                if (frameIndex < loadedFrames.Count)
                {
                    var leftResult = poseComparator.CompareLeftPose(playerLeftHand, loadedFrames[frameIndex], frameIndex);
                    leftCP.UpdateSimilarity(true, leftResult.leftHandSimilarity);
                }
            }
        }

        // 오른손 체크포인트 유사도
        if (currentRightCheckpointIndex < rightCheckpoints.Count)
        {
            var rightCP = rightCheckpoints[currentRightCheckpointIndex];
            if (rightCP.IsActive && rightCP.IsHandInside(false) && playerRightHand != null)
            {
                int frameIndex = rightCP.CheckpointIndex * checkpointFrameInterval;
                if (frameIndex < loadedFrames.Count)
                {
                    var rightResult = poseComparator.CompareRightPose(playerRightHand, loadedFrames[frameIndex], frameIndex);
                    rightCP.UpdateSimilarity(false, rightResult.rightHandSimilarity);
                }
            }
        }
    }

    // ========== 가이드 손 루프 재생 ==========

    /// <summary>
    /// 가이드 핸드 재생 시작
    /// </summary>
    private void StartGuideHandPlayback()
    {
        if (!showGuideHands) return;
        if (loadedFrames == null || loadedFrames.Count == 0) return;

        StopGuideHandPlayback();
        guideHandCoroutine = StartCoroutine(GuideHandPlaybackRoutine());

        if (showDebugLogs)
            Debug.Log("[ChunaPathEvaluator] 가이드 핸드 재생 시작");
    }

    /// <summary>
    /// 가이드 핸드 재생 중지
    /// </summary>
    private void StopGuideHandPlayback()
    {
        if (guideHandCoroutine != null)
        {
            StopCoroutine(guideHandCoroutine);
            guideHandCoroutine = null;
        }

        HideGuideHands();
    }

    /// <summary>
    /// 가이드 핸드 루프 재생 코루틴
    /// </summary>
    private IEnumerator GuideHandPlaybackRoutine()
    {
        // 위치 오프셋 계산
        Vector3 positionOffset = Vector3.zero;
        if (referenceTransform != null)
        {
            positionOffset = referenceTransform.position - recordedPatientOffset;
        }

        float frameTime = 1f / 30f; // 30fps 기준
        currentGuideFrameIndex = 0;

        while (true)
        {
            if (loadedFrames.Count == 0) yield break;

            PoseFrame frame = loadedFrames[currentGuideFrameIndex];

            // 왼손 가이드 업데이트
            if (leftGuideHand != null)
            {
                leftGuideHand.SetVisible(true);
                leftGuideHand.SetColorAndAlpha(guideHandColor, guideHandColor.a);

                if (leftGuideHand.Root != null)
                {
                    leftGuideHand.Root.position = frame.leftRootPosition + positionOffset;
                    leftGuideHand.Root.rotation = frame.leftRootRotation;
                }

                foreach (var kvp in frame.leftLocalPoses)
                {
                    leftGuideHand.SetJointLocalPose(kvp.Key, kvp.Value.position, kvp.Value.rotation);
                }
            }

            // 오른손 가이드 업데이트
            if (rightGuideHand != null)
            {
                rightGuideHand.SetVisible(true);
                rightGuideHand.SetColorAndAlpha(guideHandColor, guideHandColor.a);

                if (rightGuideHand.Root != null)
                {
                    rightGuideHand.Root.position = frame.rightRootPosition + positionOffset;
                    rightGuideHand.Root.rotation = frame.rightRootRotation;
                }

                foreach (var kvp in frame.rightLocalPoses)
                {
                    rightGuideHand.SetJointLocalPose(kvp.Key, kvp.Value.position, kvp.Value.rotation);
                }
            }

            // 다음 프레임으로
            currentGuideFrameIndex++;
            if (currentGuideFrameIndex >= loadedFrames.Count)
            {
                if (loopGuideHands)
                {
                    currentGuideFrameIndex = 0; // 루프
                }
                else
                {
                    break; // 종료
                }
            }

            yield return new WaitForSeconds(frameTime / guidePlaybackSpeed);
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

    public bool IsEvaluating => isEvaluating;
    public EvaluationSession GetCurrentSession() => currentSession;
    public int CurrentLeftCheckpointIndex => currentLeftCheckpointIndex;
    public int CurrentRightCheckpointIndex => currentRightCheckpointIndex;
    public int TotalCheckpoints => leftCheckpoints.Count + rightCheckpoints.Count;

    public void SetCheckpointInterval(int frameInterval)
    {
        checkpointFrameInterval = Mathf.Max(1, frameInterval);
    }

    public void SetCheckpointRadius(float radius)
    {
        checkpointRadius = Mathf.Max(0.01f, radius);
        foreach (var cp in leftCheckpoints) cp?.SetTriggerRadius(radius);
        foreach (var cp in rightCheckpoints) cp?.SetTriggerRadius(radius);
    }

    /// <summary>
    /// 통과 조건 설정 (홀드 시간, 유사도, 유사도 체크 여부)
    /// </summary>
    public void SetPassConditions(float holdTime, float similarity, bool checkPose)
    {
        checkpointHoldTime = Mathf.Max(0f, holdTime);
        requiredSimilarity = Mathf.Clamp01(similarity);
        checkHandPoseSimilarity = checkPose;

        foreach (var cp in leftCheckpoints)
            cp?.SetPassConditions(holdTime, similarity, checkPose);
        foreach (var cp in rightCheckpoints)
            cp?.SetPassConditions(holdTime, similarity, checkPose);
    }

    /// <summary>
    /// 유사도 체크 활성화/비활성화
    /// </summary>
    public void SetCheckHandPose(bool enable)
    {
        checkHandPoseSimilarity = enable;
        foreach (var cp in leftCheckpoints)
            cp?.SetCheckHandPose(enable);
        foreach (var cp in rightCheckpoints)
            cp?.SetCheckHandPose(enable);
    }

    /// <summary>
    /// 리밋 데이터 설정
    /// </summary>
    public void SetLimitData(ChunaLimitData data)
    {
        limitData = data;
        if (limitChecker != null && data != null)
        {
            limitChecker.SetLimitData(data);
        }
    }

    public float GetProgress()
    {
        int total = leftCheckpoints.Count + rightCheckpoints.Count;
        if (total == 0) return 0f;
        return (float)(currentSession?.passedCheckpoints ?? 0) / total;
    }

    /// <summary>
    /// 기준 위치 설정 (환자 위치)
    /// </summary>
    public void SetReferenceTransform(Transform reference)
    {
        referenceTransform = reference;
    }

    /// <summary>
    /// 기록 당시 환자 오프셋 설정
    /// </summary>
    public void SetRecordedPatientOffset(Vector3 offset)
    {
        recordedPatientOffset = offset;
    }

    private string GetGradeFromScore(float score)
    {
        if (score >= 95f) return "S";
        if (score >= 90f) return "A+";
        if (score >= 85f) return "A";
        if (score >= 80f) return "B+";
        if (score >= 75f) return "B";
        if (score >= 70f) return "C+";
        if (score >= 65f) return "C";
        if (score >= 60f) return "D";
        return "F";
    }
}
