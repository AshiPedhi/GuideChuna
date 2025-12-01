using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using static HandPoseDataLoader;

/// <summary>
/// 체크포인트 기반 추나 시술 평가 시스템 (v2)
///
/// 핵심 개념:
/// - 체크포인트는 점수 판단용 지표 (관문이 아님)
/// - 진행은 시간/수동으로 제어
/// - 유사도, 리밋 초과, 각도 근접도를 기록하여 점수화
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
    [SerializeField] private NeckVRControllerOptimized neckController;

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
    [Tooltip("메트릭 기록 간격 (초)")]
    [SerializeField] private float metricsRecordInterval = 0.1f;

    [Header("=== 홀드 감지 (다음 단계 진행 조건) ===")]
    [Tooltip("홀드 감지 활성화")]
    [SerializeField] private bool enableHoldDetection = true;

    [Tooltip("다음 단계로 넘어가기 위해 유지해야 하는 시간 (초)")]
    [SerializeField] private float requiredHoldTime = 2f;

    [Tooltip("정지 판정 속도 임계값 (m/s) - 이 속도 이하면 정지로 판정")]
    [SerializeField] private float holdVelocityThreshold = 0.02f;

    [Tooltip("홀드 위치 (리밋 범위 내에 있어야 함)")]
    [SerializeField] private bool requireLimitSafeForHold = true;

    [Tooltip("목표 위치(경로 끝) 근처에서만 홀드 인정")]
    [SerializeField] private bool requireNearTargetForHold = true;

    [Tooltip("목표 위치 인정 반경 (미터)")]
    [SerializeField] private float holdTargetRadius = 0.15f;

    [Header("=== 자동 생성 설정 ===")]
    [Tooltip("체크포인트 간격 (프레임)")]
    [SerializeField] private int checkpointFrameInterval = 15;

    [Tooltip("체크포인트 트리거 반경 (미터)")]
    [SerializeField] private float checkpointRadius = 0.15f;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = true;

    // 상태
    private bool isEvaluating = false;
    private float evaluationStartTime;
    private float lastMetricsRecordTime;

    // 홀드 감지 상태
    private float currentHoldTime = 0f;
    private bool isHolding = false;
    private Vector3 lastLeftHandPosition;
    private Vector3 lastRightHandPosition;

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
    public event Action<PathCheckpoint, float> OnCheckpointTouched;  // 체크포인트 터치 시
    public event Action<PathCheckpoint, float> OnCheckpointPassed;   // OnCheckpointTouched와 동일 (Bridge 호환용)
    public event Action<int, int> OnProgressChanged;                 // 진행률 변경 (current, total)
    public event Action<LimitStatus, bool> OnLimitStatusChanged;      // 리밋 상태 변경 시
    public event Action<float, float> OnSimilarityUpdated;           // 유사도 업데이트 (left, right)
    public event Action<float, float> OnHoldProgressChanged;         // 홀드 진행률 (current, required)
    public event Action OnHoldCompleted;                             // 홀드 완료 (다음 단계로)

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

        // 체크포인트 관련
        public int totalCheckpoints;
        public int touchedCheckpoints;      // 터치한 체크포인트 수
        public int passedCheckpoints { get => touchedCheckpoints; set => touchedCheckpoints = value; }  // Bridge 호환용 별칭
        public List<CheckpointRecord> checkpointRecords = new List<CheckpointRecord>();

        // 메트릭 기록
        public List<MetricsSnapshot> metricsHistory = new List<MetricsSnapshot>();

        // 리밋 관련
        public int limitViolationCount;     // 리밋 초과 횟수
        public float totalTimeInWarning;    // 경고 상태 총 시간
        public float totalTimeInDanger;     // 위험 상태 총 시간
        public float totalTimeExceeded;     // 초과 상태 총 시간

        // 유사도 관련
        public float averageSimilarity;
        public float minSimilarity;
        public float maxSimilarity;

        // 최종 점수
        public float finalScore;
        public string grade;
        public string feedback;

        [System.Serializable]
        public class CheckpointRecord
        {
            public int index;
            public string name;
            public float touchTime;         // 터치한 시간
            public float similarity;        // 터치 시 유사도
            public LimitStatus limitStatus; // 터치 시 리밋 상태
            public bool isLeftHand;
        }

        [System.Serializable]
        public class MetricsSnapshot
        {
            public float timestamp;
            public float leftSimilarity;
            public float rightSimilarity;
            public LimitStatus leftLimitStatus;
            public LimitStatus rightLimitStatus;
            public float leftLimitRatio;
            public float rightLimitRatio;
            public Vector3 leftHandPosition;
            public Vector3 rightHandPosition;
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

        // 메트릭 기록
        if (currentTime - lastMetricsRecordTime >= metricsRecordInterval)
        {
            lastMetricsRecordTime = currentTime;
            RecordMetricsSnapshot();
        }

        // 홀드 감지 (다음 단계 진행 조건)
        if (enableHoldDetection)
        {
            UpdateHoldDetection();
        }
    }

    /// <summary>
    /// 홀드 감지 업데이트 - 목표 위치에서 손이 일정 시간 정지하면 다음 단계로
    /// </summary>
    private void UpdateHoldDetection()
    {
        Vector3 leftPos = playerLeftHand != null ? playerLeftHand.transform.position : Vector3.zero;
        Vector3 rightPos = playerRightHand != null ? playerRightHand.transform.position : Vector3.zero;

        // 손 이동 속도 계산
        float leftVelocity = (leftPos - lastLeftHandPosition).magnitude / Time.deltaTime;
        float rightVelocity = (rightPos - lastRightHandPosition).magnitude / Time.deltaTime;

        lastLeftHandPosition = leftPos;
        lastRightHandPosition = rightPos;

        // 양손 모두 정지 판정
        bool bothHandsStopped = leftVelocity < holdVelocityThreshold && rightVelocity < holdVelocityThreshold;

        // 목표 위치 근처인지 확인 (경로 끝 = 마지막 체크포인트)
        bool nearTarget = true;
        if (requireNearTargetForHold)
        {
            nearTarget = IsNearTargetPosition(leftPos, rightPos);
        }

        // 리밋 범위 내 확인 (옵션)
        bool inSafeRange = true;
        if (requireLimitSafeForHold && limitChecker != null)
        {
            var leftResult = limitChecker.GetLeftHandResult();
            var rightResult = limitChecker.GetRightHandResult();
            // Danger나 Exceeded 상태가 아니면 OK
            inSafeRange = leftResult.overallStatus != LimitStatus.Exceeded &&
                          leftResult.overallStatus != LimitStatus.Danger &&
                          rightResult.overallStatus != LimitStatus.Exceeded &&
                          rightResult.overallStatus != LimitStatus.Danger;
        }

        // 홀드 조건 충족 여부: 정지 + 목표 근처 + 안전 범위
        bool canHold = bothHandsStopped && nearTarget && inSafeRange;

        if (canHold)
        {
            if (!isHolding)
            {
                isHolding = true;
                if (showDebugLogs)
                    Debug.Log("<color=yellow>[ChunaPathEvaluator] 목표 위치에서 홀드 시작...</color>");
            }

            currentHoldTime += Time.deltaTime;
            OnHoldProgressChanged?.Invoke(currentHoldTime, requiredHoldTime);

            // 홀드 완료
            if (currentHoldTime >= requiredHoldTime)
            {
                if (showDebugLogs)
                    Debug.Log("<color=green>[ChunaPathEvaluator] 홀드 완료! 다음 단계로 진행</color>");

                OnHoldCompleted?.Invoke();
                CompleteEvaluation();
            }
        }
        else
        {
            // 홀드 중단 - 타이머 리셋
            if (isHolding)
            {
                string reason = !bothHandsStopped ? "손 움직임" : (!nearTarget ? "목표 위치 이탈" : "안전 범위 이탈");
                if (showDebugLogs && currentHoldTime > 0.3f)
                    Debug.Log($"<color=orange>[ChunaPathEvaluator] 홀드 중단: {reason} ({currentHoldTime:F1}s)</color>");

                isHolding = false;
                currentHoldTime = 0f;
                OnHoldProgressChanged?.Invoke(0f, requiredHoldTime);
            }
        }
    }

    /// <summary>
    /// 손이 목표 위치(경로 끝) 근처에 있는지 확인
    /// </summary>
    private bool IsNearTargetPosition(Vector3 leftHandPos, Vector3 rightHandPos)
    {
        // 마지막 체크포인트 위치가 목표
        Vector3? leftTarget = GetLastCheckpointPosition(true);
        Vector3? rightTarget = GetLastCheckpointPosition(false);

        bool leftNear = true;
        bool rightNear = true;

        if (leftTarget.HasValue)
        {
            float leftDist = Vector3.Distance(leftHandPos, leftTarget.Value);
            leftNear = leftDist <= holdTargetRadius;
        }

        if (rightTarget.HasValue)
        {
            float rightDist = Vector3.Distance(rightHandPos, rightTarget.Value);
            rightNear = rightDist <= holdTargetRadius;
        }

        // 체크포인트가 있는 쪽만 확인 (양쪽 모두 있으면 양쪽 모두 근처여야 함)
        if (leftTarget.HasValue && rightTarget.HasValue)
            return leftNear && rightNear;
        else if (leftTarget.HasValue)
            return leftNear;
        else if (rightTarget.HasValue)
            return rightNear;
        else
            return true;  // 체크포인트 없으면 위치 상관없이 허용
    }

    /// <summary>
    /// 마지막 체크포인트 위치 가져오기
    /// </summary>
    private Vector3? GetLastCheckpointPosition(bool isLeftHand)
    {
        var checkpoints = isLeftHand ? leftCheckpoints : rightCheckpoints;
        if (checkpoints == null || checkpoints.Count == 0)
            return null;

        var lastCp = checkpoints[checkpoints.Count - 1];
        return lastCp != null ? lastCp.transform.position : (Vector3?)null;
    }

    void OnDestroy()
    {
        StopGuideHandPlayback();
    }

    /// <summary>
    /// 참조 자동 찾기
    /// </summary>
    private void FindReferences()
    {
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

        if (limitChecker != null && limitData != null)
        {
            limitChecker.SetLimitData(limitData);
        }

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

        // 목 컨트롤러 자동 탐색
        if (neckController == null)
            neckController = FindObjectOfType<NeckVRControllerOptimized>();
    }

    // ========== CSV 데이터 기반 체크포인트 생성 ==========

    public void LoadAndGenerateCheckpoints(string csvFileName)
    {
        if (showDebugLogs)
            Debug.Log($"<color=cyan>[ChunaPathEvaluator] CSV 로드: {csvFileName}</color>");

        currentProcedureName = csvFileName;

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

        ClearCheckpoints();
        GenerateCheckpointsFromFrames();

        if (showDebugLogs)
        {
            Debug.Log($"<color=green>[ChunaPathEvaluator] 체크포인트 생성 완료</color>");
            Debug.Log($"  - 왼손: {leftCheckpoints.Count}개");
            Debug.Log($"  - 오른손: {rightCheckpoints.Count}개");
        }
    }

    private void GenerateCheckpointsFromFrames()
    {
        if (loadedFrames == null || loadedFrames.Count == 0) return;

        Vector3 positionOffset = Vector3.zero;
        if (referenceTransform != null)
        {
            positionOffset = referenceTransform.position - recordedPatientOffset;

            if (showDebugLogs)
                Debug.Log($"[ChunaPathEvaluator] 위치 오프셋: {positionOffset}");
        }

        int checkpointCount = 0;

        for (int i = 0; i < loadedFrames.Count; i += checkpointFrameInterval)
        {
            PoseFrame frame = loadedFrames[i];

            Vector3 leftPos = frame.leftRootPosition + positionOffset;
            CreateCheckpoint(true, checkpointCount, leftPos, frame, leftCheckpoints);

            Vector3 rightPos = frame.rightRootPosition + positionOffset;
            CreateCheckpoint(false, checkpointCount, rightPos, frame, rightCheckpoints);

            checkpointCount++;
        }
    }

    private void CreateCheckpoint(bool isLeftHand, int index, Vector3 position, PoseFrame frame, List<PathCheckpoint> checkpointList)
    {
        string handName = isLeftHand ? "L" : "R";
        string cpName = $"{handName}_{index}";

        GameObject cpObj = new GameObject(cpName);
        cpObj.transform.SetParent(checkpointParent);
        cpObj.transform.position = position;

        PathCheckpoint checkpoint = cpObj.AddComponent<PathCheckpoint>();

        checkpoint.Initialize(
            index: index,
            name: cpName,
            position: position,
            leftHandPos: isLeftHand ? frame.leftRootPosition : Vector3.zero,
            leftHandRot: isLeftHand ? frame.leftRootRotation : Quaternion.identity,
            rightHandPos: isLeftHand ? Vector3.zero : frame.rightRootPosition,
            rightHandRot: isLeftHand ? Quaternion.identity : frame.rightRootRotation,
            holdTime: 0f,      // 즉시 터치로 기록
            similarity: 0f    // 유사도 조건 없음
        );

        checkpoint.SetTriggerRadius(checkpointRadius);
        checkpoint.SetDetectHand(isLeftHand, !isLeftHand);
        checkpoint.SetPassConditions(0f, 0f, false);  // 즉시 터치, 유사도 무관

        // 터치 이벤트 연결 (관문이 아닌 기록용)
        checkpoint.OnCheckpointPassed += (cp, isLeft, similarity) => HandleCheckpointTouched(cp, isLeft, similarity);

        checkpointList.Add(checkpoint);
    }

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

    // ========== 평가 제어 ==========

    /// <summary>
    /// 평가 시작 (체크포인트는 지표용, 진행은 수동)
    /// </summary>
    public void StartEvaluation()
    {
        int totalCheckpoints = leftCheckpoints.Count + rightCheckpoints.Count;

        if (totalCheckpoints == 0)
        {
            Debug.LogWarning("[ChunaPathEvaluator] 체크포인트가 없습니다. 가이드만 재생합니다.");
        }

        isEvaluating = true;
        evaluationStartTime = Time.time;
        lastMetricsRecordTime = Time.time;

        // 홀드 상태 초기화
        currentHoldTime = 0f;
        isHolding = false;
        if (playerLeftHand != null)
            lastLeftHandPosition = playerLeftHand.transform.position;
        if (playerRightHand != null)
            lastRightHandPosition = playerRightHand.transform.position;

        // 세션 초기화
        currentSession = new EvaluationSession
        {
            procedureName = currentProcedureName,
            startTime = DateTime.Now,
            totalCheckpoints = totalCheckpoints,
            touchedCheckpoints = 0,
            checkpointRecords = new List<EvaluationSession.CheckpointRecord>(),
            metricsHistory = new List<EvaluationSession.MetricsSnapshot>(),
            minSimilarity = 1f,
            maxSimilarity = 0f
        };

        // 모든 체크포인트 활성화 (관문이 아닌 지표이므로 전부 활성화)
        ActivateAllCheckpoints();

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

            // 리밋 이벤트 연결
            limitChecker.OnViolationDetected += HandleLimitViolation;
        }

        // 가이드 핸드 재생 시작
        StartGuideHandPlayback();

        // 목 컨트롤러 활성화 (평가 중에만 환자 목이 반응)
        if (neckController != null)
        {
            neckController.Enable();
        }

        if (showDebugLogs)
            Debug.Log("<color=green>[ChunaPathEvaluator] 평가 시작! (체크포인트는 점수 지표용)</color>");

        OnEvaluationStarted?.Invoke();
    }

    /// <summary>
    /// 모든 체크포인트 활성화
    /// </summary>
    private void ActivateAllCheckpoints()
    {
        foreach (var cp in leftCheckpoints)
            cp?.Activate();
        foreach (var cp in rightCheckpoints)
            cp?.Activate();
    }

    /// <summary>
    /// 평가 완료 (외부에서 호출 - 다음 단계로 넘어갈 때)
    /// </summary>
    public EvaluationSession CompleteEvaluation()
    {
        if (!isEvaluating) return currentSession;

        isEvaluating = false;

        // 세션 완료
        currentSession.endTime = DateTime.Now;
        currentSession.duration = Time.time - evaluationStartTime;

        // 평균 유사도 계산
        CalculateAverageSimilarity();

        // 리밋 관련 통계 계산
        CalculateLimitStatistics();

        // 최종 점수 계산
        CalculateFinalScore();

        // 리밋 체커 이벤트 해제 및 중지
        if (limitChecker != null)
        {
            limitChecker.OnViolationDetected -= HandleLimitViolation;
            limitChecker.SetEnabled(false);
        }

        // 감점 기록 종료
        if (deductionRecord != null)
        {
            deductionRecord.EndSession();
        }

        // 가이드 핸드 중지
        StopGuideHandPlayback();

        // 목 컨트롤러 비활성화 (초기 위치로 복귀)
        if (neckController != null)
        {
            neckController.Disable();
        }

        if (showDebugLogs)
        {
            Debug.Log("<color=green>========== 평가 완료 ==========</color>");
            Debug.Log($"터치한 체크포인트: {currentSession.touchedCheckpoints}/{currentSession.totalCheckpoints}");
            Debug.Log($"평균 유사도: {currentSession.averageSimilarity:P0}");
            Debug.Log($"리밋 초과 횟수: {currentSession.limitViolationCount}");
            Debug.Log($"최종 점수: {currentSession.finalScore:F0}점 ({currentSession.grade})");
        }

        OnEvaluationCompleted?.Invoke(currentSession);

        return currentSession;
    }

    /// <summary>
    /// 평가 중지 (결과 저장 없이 중단)
    /// </summary>
    public void StopEvaluation()
    {
        if (!isEvaluating) return;

        isEvaluating = false;

        StopGuideHandPlayback();

        if (limitChecker != null)
        {
            limitChecker.OnViolationDetected -= HandleLimitViolation;
            limitChecker.SetEnabled(false);
        }

        // 목 컨트롤러 비활성화
        if (neckController != null)
        {
            neckController.Disable();
        }

        if (showDebugLogs)
            Debug.Log("[ChunaPathEvaluator] 평가 중지");
    }

    /// <summary>
    /// 평가 리셋
    /// </summary>
    public void ResetEvaluation()
    {
        isEvaluating = false;

        foreach (var cp in leftCheckpoints) cp?.ResetCheckpoint();
        foreach (var cp in rightCheckpoints) cp?.ResetCheckpoint();

        StopGuideHandPlayback();

        if (limitChecker != null)
        {
            limitChecker.OnViolationDetected -= HandleLimitViolation;
            limitChecker.SetEnabled(false);
        }

        // 목 컨트롤러 비활성화
        if (neckController != null)
        {
            neckController.Disable();
        }

        if (deductionRecord != null)
        {
            deductionRecord.ResetSession();
        }

        if (showDebugLogs)
            Debug.Log("[ChunaPathEvaluator] 평가 리셋");
    }

    // ========== 체크포인트 터치 처리 (기록용) ==========

    private void HandleCheckpointTouched(PathCheckpoint checkpoint, bool isLeftHand, float similarity)
    {
        if (!isEvaluating) return;

        // 현재 유사도 계산
        float currentSimilarity = CalculateCurrentSimilarity(isLeftHand, checkpoint.CheckpointIndex);

        // 현재 리밋 상태
        LimitStatus currentLimitStatus = LimitStatus.Safe;
        if (limitChecker != null)
        {
            var result = isLeftHand ? limitChecker.GetLeftHandResult() : limitChecker.GetRightHandResult();
            currentLimitStatus = result.overallStatus;
        }

        // 기록 추가
        currentSession.checkpointRecords.Add(new EvaluationSession.CheckpointRecord
        {
            index = checkpoint.CheckpointIndex,
            name = checkpoint.CheckpointName,
            touchTime = Time.time - evaluationStartTime,
            similarity = currentSimilarity,
            limitStatus = currentLimitStatus,
            isLeftHand = isLeftHand
        });

        currentSession.touchedCheckpoints++;

        if (showDebugLogs)
        {
            string hand = isLeftHand ? "왼손" : "오른손";
            string limitStr = currentLimitStatus.ToString();
            Debug.Log($"<color=cyan>[ChunaPathEvaluator] {hand} 체크포인트 {checkpoint.CheckpointIndex} 터치 (유사도: {currentSimilarity:P0}, 리밋: {limitStr})</color>");
        }

        // 이벤트 발생
        OnCheckpointTouched?.Invoke(checkpoint, currentSimilarity);
        OnCheckpointPassed?.Invoke(checkpoint, currentSimilarity);  // Bridge 호환용
        OnProgressChanged?.Invoke(currentSession.touchedCheckpoints, currentSession.totalCheckpoints);
    }

    // ========== 리밋 위반 처리 ==========

    private void HandleLimitViolation(ChunaLimitChecker.ViolationEvent evt)
    {
        if (!isEvaluating) return;

        currentSession.limitViolationCount++;

        if (showDebugLogs)
        {
            string hand = evt.isLeftHand ? "왼손" : "오른손";
            Debug.Log($"<color=red>[ChunaPathEvaluator] 리밋 위반! {hand} - {evt.violationType} (비율: {evt.limitRatio:P0})</color>");
        }

        // 감점 기록
        if (deductionRecord != null && limitData != null)
        {
            float deduction = limitData.GetDeductionForSeverity(evt.severity);
            string reason = $"{evt.violationType}: {evt.violationValue}";
            deductionRecord.AddManualDeduction(deduction, reason, evt.violationType);
        }

        OnLimitStatusChanged?.Invoke(evt.severity == ViolationSeverity.Dangerous ? LimitStatus.Exceeded : LimitStatus.Warning, evt.isLeftHand);
    }

    // ========== 메트릭 기록 ==========

    private void RecordMetricsSnapshot()
    {
        var snapshot = new EvaluationSession.MetricsSnapshot
        {
            timestamp = Time.time - evaluationStartTime
        };

        // 유사도 계산
        snapshot.leftSimilarity = CalculateCurrentSimilarity(true, -1);
        snapshot.rightSimilarity = CalculateCurrentSimilarity(false, -1);

        // 리밋 상태
        if (limitChecker != null)
        {
            var leftResult = limitChecker.GetLeftHandResult();
            var rightResult = limitChecker.GetRightHandResult();

            snapshot.leftLimitStatus = leftResult.overallStatus;
            snapshot.rightLimitStatus = rightResult.overallStatus;
            snapshot.leftLimitRatio = leftResult.maxLimitRatio;
            snapshot.rightLimitRatio = rightResult.maxLimitRatio;

            // 리밋 상태별 시간 누적
            if (leftResult.overallStatus == LimitStatus.Warning || rightResult.overallStatus == LimitStatus.Warning)
                currentSession.totalTimeInWarning += metricsRecordInterval;
            if (leftResult.overallStatus == LimitStatus.Danger || rightResult.overallStatus == LimitStatus.Danger)
                currentSession.totalTimeInDanger += metricsRecordInterval;
            if (leftResult.overallStatus == LimitStatus.Exceeded || rightResult.overallStatus == LimitStatus.Exceeded)
                currentSession.totalTimeExceeded += metricsRecordInterval;
        }

        // 손 위치
        if (playerLeftHand != null)
            snapshot.leftHandPosition = playerLeftHand.transform.position;
        if (playerRightHand != null)
            snapshot.rightHandPosition = playerRightHand.transform.position;

        currentSession.metricsHistory.Add(snapshot);

        // 유사도 이벤트 발생
        OnSimilarityUpdated?.Invoke(snapshot.leftSimilarity, snapshot.rightSimilarity);
    }

    private float CalculateCurrentSimilarity(bool isLeftHand, int checkpointIndex)
    {
        if (loadedFrames == null || loadedFrames.Count == 0) return 0f;

        // 가장 가까운 프레임 찾기
        int frameIndex = checkpointIndex >= 0 ? checkpointIndex * checkpointFrameInterval : currentGuideFrameIndex;
        frameIndex = Mathf.Clamp(frameIndex, 0, loadedFrames.Count - 1);

        PoseFrame frame = loadedFrames[frameIndex];

        if (isLeftHand && playerLeftHand != null)
        {
            var result = poseComparator.CompareLeftPose(playerLeftHand, frame, frameIndex);
            return result.leftHandSimilarity;
        }
        else if (!isLeftHand && playerRightHand != null)
        {
            var result = poseComparator.CompareRightPose(playerRightHand, frame, frameIndex);
            return result.rightHandSimilarity;
        }

        return 0f;
    }

    // ========== 점수 계산 ==========

    private void CalculateAverageSimilarity()
    {
        if (currentSession.metricsHistory.Count == 0) return;

        float totalLeft = 0f, totalRight = 0f;
        foreach (var snapshot in currentSession.metricsHistory)
        {
            totalLeft += snapshot.leftSimilarity;
            totalRight += snapshot.rightSimilarity;

            float avg = (snapshot.leftSimilarity + snapshot.rightSimilarity) / 2f;
            if (avg < currentSession.minSimilarity) currentSession.minSimilarity = avg;
            if (avg > currentSession.maxSimilarity) currentSession.maxSimilarity = avg;
        }

        currentSession.averageSimilarity = (totalLeft + totalRight) / (currentSession.metricsHistory.Count * 2);
    }

    private void CalculateLimitStatistics()
    {
        // 이미 Update에서 누적됨
    }

    private void CalculateFinalScore()
    {
        float score = 100f;

        // 유사도 기반 점수 (40%)
        float similarityScore = currentSession.averageSimilarity * 40f;

        // 체크포인트 통과율 (30%)
        float checkpointRate = currentSession.totalCheckpoints > 0
            ? (float)currentSession.touchedCheckpoints / currentSession.totalCheckpoints
            : 1f;
        float checkpointScore = checkpointRate * 30f;

        // 리밋 준수 점수 (30%)
        float limitScore = 30f;
        limitScore -= currentSession.limitViolationCount * 2f;  // 위반당 -2점
        limitScore -= currentSession.totalTimeInWarning * 0.5f;  // 경고 초당 -0.5점
        limitScore -= currentSession.totalTimeInDanger * 1f;     // 위험 초당 -1점
        limitScore -= currentSession.totalTimeExceeded * 3f;     // 초과 초당 -3점
        limitScore = Mathf.Max(0f, limitScore);

        score = similarityScore + checkpointScore + limitScore;
        score = Mathf.Clamp(score, 0f, 100f);

        currentSession.finalScore = score;
        currentSession.grade = GetGradeFromScore(score);
        currentSession.feedback = GenerateFeedback();
    }

    private string GenerateFeedback()
    {
        List<string> feedbacks = new List<string>();

        if (currentSession.averageSimilarity < 0.5f)
            feedbacks.Add("손 모양을 가이드와 더 비슷하게 유지하세요");

        if (currentSession.limitViolationCount > 3)
            feedbacks.Add("적정 범위를 벗어난 횟수가 많습니다. 부드럽게 움직이세요");

        if (currentSession.totalTimeExceeded > 2f)
            feedbacks.Add("위험 범위에서 너무 오래 머물렀습니다");

        float checkpointRate = currentSession.totalCheckpoints > 0
            ? (float)currentSession.touchedCheckpoints / currentSession.totalCheckpoints
            : 1f;
        if (checkpointRate < 0.7f)
            feedbacks.Add("경로를 더 정확하게 따라가세요");

        if (feedbacks.Count == 0)
            feedbacks.Add("잘 수행하셨습니다!");

        return string.Join("\n", feedbacks);
    }

    // ========== 가이드 손 루프 재생 ==========

    private void StartGuideHandPlayback()
    {
        if (!showGuideHands) return;
        if (loadedFrames == null || loadedFrames.Count == 0) return;

        StopGuideHandPlayback();
        guideHandCoroutine = StartCoroutine(GuideHandPlaybackRoutine());

        if (showDebugLogs)
            Debug.Log("[ChunaPathEvaluator] 가이드 핸드 재생 시작");
    }

    private void StopGuideHandPlayback()
    {
        if (guideHandCoroutine != null)
        {
            StopCoroutine(guideHandCoroutine);
            guideHandCoroutine = null;
        }

        HideGuideHands();
    }

    private IEnumerator GuideHandPlaybackRoutine()
    {
        Vector3 positionOffset = Vector3.zero;
        if (referenceTransform != null)
        {
            positionOffset = referenceTransform.position - recordedPatientOffset;
        }

        float frameTime = 1f / 30f;
        currentGuideFrameIndex = 0;

        while (true)
        {
            if (loadedFrames.Count == 0) yield break;

            PoseFrame frame = loadedFrames[currentGuideFrameIndex];

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

            currentGuideFrameIndex++;
            if (currentGuideFrameIndex >= loadedFrames.Count)
            {
                if (loopGuideHands)
                {
                    currentGuideFrameIndex = 0;
                }
                else
                {
                    break;
                }
            }

            yield return new WaitForSeconds(frameTime / guidePlaybackSpeed);
        }
    }

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
    public int TotalCheckpoints => leftCheckpoints.Count + rightCheckpoints.Count;
    public int TouchedCheckpoints => currentSession?.touchedCheckpoints ?? 0;

    /// <summary>
    /// 현재 진행률 가져오기 (0~1)
    /// </summary>
    public float GetProgress()
    {
        int total = TotalCheckpoints;
        if (total == 0) return 0f;
        return (float)TouchedCheckpoints / total;
    }

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
    /// 홀드 감지 설정
    /// </summary>
    public void SetHoldSettings(float holdTime, float velocityThreshold, bool requireSafeRange)
    {
        requiredHoldTime = Mathf.Max(0.1f, holdTime);
        holdVelocityThreshold = Mathf.Max(0.001f, velocityThreshold);
        requireLimitSafeForHold = requireSafeRange;
    }

    /// <summary>
    /// 홀드 감지 활성화/비활성화
    /// </summary>
    public void SetHoldDetectionEnabled(bool enabled)
    {
        enableHoldDetection = enabled;
    }

    /// <summary>
    /// 현재 홀드 진행률 (0~1)
    /// </summary>
    public float GetHoldProgress()
    {
        return requiredHoldTime > 0 ? Mathf.Clamp01(currentHoldTime / requiredHoldTime) : 0f;
    }

    /// <summary>
    /// 현재 홀드 중인지
    /// </summary>
    public bool IsHolding => isHolding;

    public void SetLimitData(ChunaLimitData data)
    {
        limitData = data;
        if (limitChecker != null && data != null)
        {
            limitChecker.SetLimitData(data);
        }
    }

    public void SetReferenceTransform(Transform reference)
    {
        referenceTransform = reference;
    }

    public void SetRecordedPatientOffset(Vector3 offset)
    {
        recordedPatientOffset = offset;
    }

    /// <summary>
    /// 현재 리밋 상태 가져오기
    /// </summary>
    public LimitStatus GetCurrentLimitStatus(bool isLeftHand)
    {
        if (limitChecker == null) return LimitStatus.Safe;
        var result = isLeftHand ? limitChecker.GetLeftHandResult() : limitChecker.GetRightHandResult();
        return result.overallStatus;
    }

    /// <summary>
    /// 현재 유사도 가져오기
    /// </summary>
    public float GetCurrentSimilarity(bool isLeftHand)
    {
        return CalculateCurrentSimilarity(isLeftHand, -1);
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
