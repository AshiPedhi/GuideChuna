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

    [Header("=== 직접 할당 충돌체 (환자 머리) ===")]
    [Tooltip("환자 머리에 부착된 충돌체 - 손이 닿으면 트래킹 시작")]
    [SerializeField] private Collider patientHeadCollider;

    [Tooltip("손 충돌체 (왼손)")]
    [SerializeField] private Collider leftHandCollider;

    [Tooltip("손 충돌체 (오른손)")]
    [SerializeField] private Collider rightHandCollider;

    [Tooltip("손 충돌체 크기 배율 (1.0 = 원본, 0.7 = 70%)")]
    [Range(0.1f, 2f)]
    [SerializeField] private float handColliderScale = 0.7f;

    [Tooltip("손 충돌체가 없을 때 사용할 기본 충돌 반지름 (m)")]
    [SerializeField] private float defaultHandCollisionRadius = 0.08f;

    [Tooltip("손 충돌 감지 위치 오프셋 - 손가락 방향으로 이동 (m)")]
    [SerializeField] private float handCollisionForwardOffset = 0.02f;

    [Tooltip("충돌 감지 모드 사용 (체크포인트 대신)")]
    [SerializeField] private bool useCollisionMode = true;

    [Header("=== 손 충돌 형태 설정 ===")]
    [Tooltip("충돌 감지 형태: Sphere(구), Box(박스-손바닥+손가락), PalmOnly(손바닥만)")]
    [SerializeField] private HandCollisionShape handCollisionShape = HandCollisionShape.Box;

    [Tooltip("손바닥 너비 (m) - Box/PalmOnly 모드에서 사용")]
    [SerializeField] private float palmWidth = 0.08f;

    [Tooltip("손바닥 두께 (m) - Box/PalmOnly 모드에서 사용")]
    [SerializeField] private float palmThickness = 0.03f;

    [Tooltip("손바닥 높이/길이 (m) - PalmOnly 모드에서 사용")]
    [SerializeField] private float palmHeight = 0.08f;

    [Tooltip("손가락 길이 (m) - Box 모드에서 손바닥+손가락 총 길이")]
    [SerializeField] private float fingerLength = 0.10f;

    [Header("=== 체크포인트 설정 (레거시) ===")]
    [SerializeField] private List<PathCheckpoint> leftCheckpoints = new List<PathCheckpoint>();
    [SerializeField] private List<PathCheckpoint> rightCheckpoints = new List<PathCheckpoint>();
    [SerializeField] private Transform checkpointParent;

    [Header("=== 손 참조 ===")]
    [SerializeField] private HandVisual playerLeftHand;
    [SerializeField] private HandVisual playerRightHand;

    [Tooltip("회전 감지에 사용할 손목 본 (자동 검색됨)")]
    private Transform leftWristBone;
    private Transform rightWristBone;

    [Header("=== 모듈 참조 ===")]
    [SerializeField] private HandPoseComparator poseComparator;
    [SerializeField] private ChunaLimitChecker limitChecker;
    [SerializeField] private NeckVRControllerOptimized neckController;

    [Header("=== 환자 애니메이션 ===")]
    [Tooltip("환자 모델의 Animator")]
    [SerializeField] private Animator patientAnimator;

    [Tooltip("프레임 레이트에 맞춰 애니메이션 동기화")]
    [SerializeField] private bool syncAnimationWithFrame = true;

    [Tooltip("애니메이션 재생 모드")]
    [SerializeField] private AnimationPlayMode animationPlayMode = AnimationPlayMode.SyncWithUser;

    [Header("=== 가이드 손 표시 ===")]
    [SerializeField] private HandTransformMapper leftGuideHand;
    [SerializeField] private HandTransformMapper rightGuideHand;
    [SerializeField] private bool showGuideHands = true;
    [SerializeField] private Color guideHandColor = new Color(0.3f, 0.7f, 1f, 0.5f);

    [Tooltip("가이드 핸드 재생 속도 (1 = 원본 속도)")]
    [SerializeField] private float guidePlaybackSpeed = 1f;

    [Tooltip("가이드 핸드 루프 재생")]
    [SerializeField] private bool loopGuideHands = true;

    [Tooltip("루프 재생 시 대기 시간 (초)")]
    [SerializeField] private float loopDelaySeconds = 1f;

    [Tooltip("시작 위치 대기 중 첫 프레임 표시")]
    [SerializeField] private bool showFirstFrameWhileWaiting = true;

    [Header("=== 평가 설정 ===")]
    [Tooltip("메트릭 기록 간격 (초)")]
    [SerializeField] private float metricsRecordInterval = 0.1f;

    [Header("=== 홀드 감지 (다음 단계 진행 조건) ===")]
    [Tooltip("홀드 감지 활성화")]
    [SerializeField] private bool enableHoldDetection = true;

    [Tooltip("다음 단계로 넘어가기 위해 유지해야 하는 시간 (초)")]
    [SerializeField] private float requiredHoldTime = 2f;

    [Tooltip("정지 판정 속도 임계값 (m/s) - 이 속도 이하면 정지로 판정")]
    [SerializeField] private float holdVelocityThreshold = 0.05f;

    [Tooltip("홀드 위치 (리밋 범위 내에 있어야 함)")]
    [SerializeField] private bool requireLimitSafeForHold = true;

    [Tooltip("목표 위치(경로 끝) 근처에서만 홀드 인정")]
    [SerializeField] private bool requireNearTargetForHold = true;

    [Tooltip("목표 위치 인정 반경 (미터)")]
    [SerializeField] private float holdTargetRadius = 0.15f;

    [Tooltip("시작 위치 근처에서만 평가 시작")]
    [SerializeField] private bool requireNearStartToBegin = true;

    [Tooltip("시작 위치 인정 반경 (미터)")]
    [SerializeField] private float startPositionRadius = 0.2f;

    [Header("=== 가이드 레이어 설정 ===")]
    [Tooltip("가이드 체크포인트 레이어 이름")]
    [SerializeField] private string guideLayerName = "Guide";

    [Header("=== 자동 생성 설정 ===")]
    [Tooltip("체크포인트 간격 (프레임)")]
    [SerializeField] private int checkpointFrameInterval = 15;

    [Tooltip("체크포인트 트리거 반경 (미터)")]
    [SerializeField] private float checkpointRadius = 0.15f;

    [Header("=== 회전 감지 설정 ===")]
    [Tooltip("회전 감지 활성화 (제자리 회전 동작용)")]
    [SerializeField] private bool useRotationMatching = true;

    [Tooltip("회전 가중치 (0=위치만, 1=회전 강조)")]
    [Range(0f, 1f)]
    [SerializeField] private float rotationWeight = 0.3f;

    [Header("=== 상대 이동 감지 설정 ===")]
    [Tooltip("상대 이동 모드 사용 (시작 홀드 위치 기준으로 진행률 계산)")]
    [SerializeField] private bool useRelativeMovement = true;

    [Tooltip("회전 방향 반전 (손목 회전이 반대로 감지될 때 사용)")]
    [SerializeField] private bool invertRotationDirection = true;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = true;

    [Header("=== 새로운 평가 흐름 설정 ===")]
    [Tooltip("시작 홀드 시간 (초)")]
    [SerializeField] private float startHoldDuration = 3f;

    [Tooltip("중간 홀드 시간 (초)")]
    [SerializeField] private float midHoldDuration = 3f;

    [Tooltip("중간 홀드 시작 구간 (0~1, 전체 프레임 기준)")]
    [SerializeField] private float midHoldStartRatio = 0.3f;

    [Tooltip("중간 홀드 종료 구간 (0~1, 전체 프레임 기준)")]
    [SerializeField] private float midHoldEndRatio = 0.5f;

    [Tooltip("제한장벽 최대지점 구간 (0~1, 이 지점 이후 경고)")]
    [SerializeField] private float limitBarrierRatio = 0.5f;

    [Header("=== 스트레칭/재평가 확장 제한 ===")]
    [Tooltip("스트레칭/재평가 시 확장된 제한 장벽 (0~1)")]
    [SerializeField] private float extendedLimitBarrierRatio = 0.65f;

    [Tooltip("스트레칭/재평가 시 확장된 중간 홀드 종료 구간")]
    [SerializeField] private float extendedMidHoldEndRatio = 0.65f;

    [Tooltip("왼손 이탈 허용 거리 (미터)")]
    [SerializeField] private float leftHandDriftThreshold = 0.15f;

    /// <summary>
    /// 손 충돌 감지 형태
    /// </summary>
    public enum HandCollisionShape
    {
        Sphere,     // 구형 (기존 방식)
        Box,        // 박스형 (손바닥 + 손가락 길이)
        PalmOnly    // 손바닥만 (작은 박스)
    }

    /// <summary>
    /// 평가 단계
    /// </summary>
    public enum EvaluationPhase
    {
        Idle,              // 대기
        WaitingForStart,   // 시작 위치 대기
        StartHold,         // 시작 홀드 (2초)
        Moving,            // 자유 이동
        MidHold,           // 중간 홀드 (3초)
        Completed          // 완료
    }

    // 상태
    private bool isEvaluating = false;
    private float evaluationStartTime;
    private float lastMetricsRecordTime;

    // 새로운 평가 흐름 상태
    private EvaluationPhase currentPhase = EvaluationPhase.Idle;
    private float phaseHoldTime = 0f;
    private Vector3 leftHandStartHoldPosition;  // 시작 홀드 시 왼손 위치 저장

    // 홀드 감지 상태
    private float currentHoldTime = 0f;
    private bool isHolding = false;
    private Vector3 lastLeftHandPosition;
    private Vector3 lastRightHandPosition;

    // 시작 위치 도달 상태
    private bool hasReachedStartPosition = false;

    // 50% 초과 경고 상태
    private bool isOverLimitBarrier = false;

    // 충돌 감지 상태
    private bool isLeftHandTouchingPatient = false;
    private bool isRightHandTouchingPatient = false;
    private float targetAnimationRatio = 0f;
    private float currentAnimationRatio = 0f;
    private float animationLerpSpeed = 5f;

    // 데이터
    private List<PoseFrame> loadedFrames = new List<PoseFrame>();
    private string currentProcedureName = "";

    // 가이드 핸드 재생
    private Coroutine guideHandCoroutine;
    private int currentGuideFrameIndex = 0;

    // 사용자 손 위치 기반 프레임
    private int userHandFrameIndex = 0;
    private float userHandFrameRatio = 0f;  // 0~1

    // ★ 상대 이동 감지 데이터 (시작-끝 프레임 간 이동량 기준)
    private Vector3 handDataMovementVector;     // 핸드데이터 시작→끝 이동 벡터
    private float handDataTotalDistance;        // 핸드데이터 총 이동 거리
    private float handDataTotalRotation;        // 핸드데이터 총 회전 각도 (도)
    private bool isPositionBasedMovement;       // true=위치 기반, false=회전 기반
    private Vector3 userHoldReferencePosition;  // 사용자 시작 홀드 위치 (기준점)
    private Quaternion userHoldReferenceRotation; // 사용자 시작 홀드 회전 (기준점)
    private Vector3 movementAxis;               // 주요 이동 축 (정규화)
    private string specifiedMovementType;       // CSV에서 지정한 이동 타입 (position/rotation)
    private bool startHoldOnly;                 // true면 StartHold만 완료하면 다음으로 (등척성운동용)

    // 환자 애니메이션
    private AnimationClip currentAnimationClip;
    private string currentAnimationStateName;
    private float animationFrameRate = 30f;  // 핸드 데이터 프레임 레이트
    private float animationDuration = 0f;

    // ★ AutoPlay 모드 관련
    private float autoPlayProgress = 0f;        // 자동 재생 진행률 (0~1)
    private float autoPlayDuration = 3f;        // 자동 재생 시간 (초) - 기본값 3초
    private float autoPlayStartTime = 0f;       // 자동 재생 시작 시간
    private bool isAutoPlayMode = false;        // 자동 재생 모드 활성화 여부

    // ★ 스트레칭/재평가 확장 모드
    private bool isExtendedLimitMode = false;   // 확장 제한 모드 활성화 여부
    private float currentLimitRatio => isExtendedLimitMode ? extendedLimitBarrierRatio : limitBarrierRatio;
    private float currentMidHoldEnd => isExtendedLimitMode ? extendedMidHoldEndRatio : midHoldEndRatio;

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

    // 새로운 평가 흐름 이벤트
    public event Action<EvaluationPhase> OnPhaseChanged;             // 단계 변경
    public event Action OnStartHoldComplete;                         // 시작 홀드 완료 (움직이세요)
    public event Action OnMidHoldBegin;                              // 중간 홀드 시작 (멈추세요)
    public event Action OnMidHoldComplete;                           // 중간 홀드 완료
    public event Action<float> OnLimitWarning;                       // 제한장벽 경고 (현재 비율)
    public event Action<float> OnLeftHandDrifted;                    // 왼손 이탈 (이탈 거리)
    public event Action<int, int, float> OnUserFrameChanged;         // 사용자 손 프레임 변경 (현재, 총, 비율)

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

        // 새로운 평가 흐름 관련
        public int leftHandDriftCount;      // 왼손 이탈 횟수
        public int limitWarningCount;       // 제한장벽 경고 횟수

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

        // ★ AutoPlay 모드: 핸드데이터 없이 애니메이션만 자동 재생
        if (isAutoPlayMode)
        {
            UpdateAutoPlay();
            return;
        }

        // 충돌 모드: 손-환자 충돌 체크
        if (useCollisionMode)
        {
            UpdateCollisionDetection();
        }

        // 사용자 손 위치 기반 프레임 업데이트
        UpdateUserHandFrame();

        // 새로운 평가 흐름 업데이트
        UpdatePhaseBasedEvaluation();

        // 애니메이션 선형보간 업데이트
        UpdateAnimationLerp();

        // 메트릭 기록 (Moving/MidHold 단계에서만)
        if (currentPhase == EvaluationPhase.Moving || currentPhase == EvaluationPhase.MidHold)
        {
            float currentTime = Time.time;
            if (currentTime - lastMetricsRecordTime >= metricsRecordInterval)
            {
                lastMetricsRecordTime = currentTime;
                RecordMetricsSnapshot();
            }
        }
    }

    /// <summary>
    /// 새로운 평가 흐름 업데이트
    /// </summary>
    private void UpdatePhaseBasedEvaluation()
    {
        Vector3 leftPos = playerLeftHand != null ? playerLeftHand.transform.position : Vector3.zero;
        Vector3 rightPos = playerRightHand != null ? playerRightHand.transform.position : Vector3.zero;

        // 손 속도 계산
        float leftVelocity = Time.deltaTime > 0 ? (leftPos - lastLeftHandPosition).magnitude / Time.deltaTime : 0f;
        float rightVelocity = Time.deltaTime > 0 ? (rightPos - lastRightHandPosition).magnitude / Time.deltaTime : 0f;
        lastLeftHandPosition = leftPos;
        lastRightHandPosition = rightPos;

        switch (currentPhase)
        {
            case EvaluationPhase.WaitingForStart:
                UpdateWaitingForStart(leftPos, rightPos);
                break;

            case EvaluationPhase.StartHold:
                UpdateStartHold(leftPos, rightPos, leftVelocity, rightVelocity);
                break;

            case EvaluationPhase.Moving:
                UpdateMoving(leftPos, rightPos);
                break;

            case EvaluationPhase.MidHold:
                UpdateMidHold(leftPos, rightPos, rightVelocity);
                break;
        }
    }

    /// <summary>
    /// 단계 변경
    /// </summary>
    private void ChangePhase(EvaluationPhase newPhase)
    {
        if (currentPhase == newPhase) return;

        var oldPhase = currentPhase;
        currentPhase = newPhase;
        phaseHoldTime = 0f;

        // Moving 단계 시작 시 프레임 인덱스 초기화
        if (newPhase == EvaluationPhase.Moving)
        {
            userHandFrameIndex = 0;
            userHandFrameRatio = 0f;

            if (showDebugLogs)
                Debug.Log("<color=green>[ChunaPathEvaluator] 프레임 인덱스 초기화 (Moving 단계 시작)</color>");
        }

        if (showDebugLogs)
        {
            Debug.Log($"<color=cyan>[ChunaPathEvaluator] ========== 단계 변경: {oldPhase} → {newPhase} ==========</color>");
            Debug.Log($"<color=magenta>[Animation Info] 상태이름: {currentAnimationStateName ?? "NULL"}, Animator: {(patientAnimator != null ? patientAnimator.name : "NULL")}, 프레임수: {loadedFrames?.Count ?? 0}</color>");
        }

        OnPhaseChanged?.Invoke(newPhase);

        // 단계별 안내
        if (showDebugLogs)
        {
            switch (newPhase)
            {
                case EvaluationPhase.WaitingForStart:
                    Debug.Log("<color=yellow>시작 위치로 이동하세요!</color>");
                    break;
                case EvaluationPhase.StartHold:
                    Debug.Log("<color=yellow>시작 위치에서 2초간 유지하세요! (홀드 타이머 시작)</color>");
                    break;
                case EvaluationPhase.Moving:
                    Debug.Log("<color=green>가이드를 따라 움직이세요!</color>");
                    break;
                case EvaluationPhase.MidHold:
                    Debug.Log("<color=yellow>중간 지점에서 3초간 멈추세요! (홀드 타이머 시작)</color>");
                    break;
                case EvaluationPhase.Completed:
                    Debug.Log("<color=green>평가 완료!</color>");
                    break;
            }
        }
    }

    /// <summary>
    /// 1단계: 시작 위치 대기
    /// 손 인식 시 StartHold 단계로 전환
    /// </summary>
    private void UpdateWaitingForStart(Vector3 leftPos, Vector3 rightPos)
    {
        bool shouldProceed = false;

        // ★ 충돌 모드: 손이 환자에게 닿았는지 확인
        if (useCollisionMode)
        {
            shouldProceed = isLeftHandTouchingPatient || isRightHandTouchingPatient;

            if (showDebugLogs && Time.frameCount % 60 == 0)
                Debug.Log($"[WaitingForStart-Collision] 왼손:{isLeftHandTouchingPatient}, 오른손:{isRightHandTouchingPatient}");
        }
        else
        {
            // 레거시: 체크포인트 위치 기반
            Vector3? leftStart = GetFirstCheckpointPosition(true);
            Vector3? rightStart = GetFirstCheckpointPosition(false);

            bool leftNear = !leftStart.HasValue || Vector3.Distance(leftPos, leftStart.Value) <= startPositionRadius;
            bool rightNear = !rightStart.HasValue || Vector3.Distance(rightPos, rightStart.Value) <= startPositionRadius;

            if (showDebugLogs && Time.frameCount % 60 == 0)
            {
                float leftDist = leftStart.HasValue ? Vector3.Distance(leftPos, leftStart.Value) : 0f;
                float rightDist = rightStart.HasValue ? Vector3.Distance(rightPos, rightStart.Value) : 0f;
                Debug.Log($"[WaitingForStart] 왼손:{leftNear}({leftDist:F3}m), 오른손:{rightNear}({rightDist:F3}m)");
            }

            shouldProceed = leftNear && rightNear;
        }

        if (shouldProceed)
        {
            leftHandStartHoldPosition = leftPos;

            // 충돌 모드에서는 NeckVRController 비활성화 (애니메이션으로 목 제어)
            if (useCollisionMode && neckController != null)
            {
                neckController.Disable();
                Debug.Log("<color=yellow>[Collision Mode] NeckVRController 비활성화 - 애니메이션으로 목 제어</color>");
            }
            else if (neckController != null && !neckController.IsEnabled)
            {
                neckController.Enable();
            }

            Debug.Log("<color=green>[WaitingForStart] 손 인식! StartHold 단계로 전환</color>");
            ChangePhase(EvaluationPhase.StartHold);
        }
    }

    /// <summary>
    /// 2단계: 시작 홀드 (첫 프레임에서 3초 홀드)
    /// </summary>
    private void UpdateStartHold(Vector3 leftPos, Vector3 rightPos, float leftVel, float rightVel)
    {
        bool bothStopped = leftVel < holdVelocityThreshold && rightVel < holdVelocityThreshold;
        bool positionOk = false;

        // ★ 충돌 모드: 손이 환자에게 닿아있는지 확인
        if (useCollisionMode)
        {
            positionOk = isLeftHandTouchingPatient || isRightHandTouchingPatient;

            if (showDebugLogs && Time.frameCount % 10 == 0)
                Debug.Log($"[StartHold-Collision] 정지:{bothStopped}, 접촉:{positionOk}, 홀드:{phaseHoldTime:F0}s");
        }
        else
        {
            // 레거시: 체크포인트 위치 기반
            Vector3? leftStart = GetFirstCheckpointPosition(true);
            Vector3? rightStart = GetFirstCheckpointPosition(false);
            bool leftNear = !leftStart.HasValue || Vector3.Distance(leftPos, leftStart.Value) <= startPositionRadius;
            bool rightNear = !rightStart.HasValue || Vector3.Distance(rightPos, rightStart.Value) <= startPositionRadius;

            if (showDebugLogs && Time.frameCount % 10 == 0)
            {
                float leftDist = leftStart.HasValue ? Vector3.Distance(leftPos, leftStart.Value) : 0f;
                float rightDist = rightStart.HasValue ? Vector3.Distance(rightPos, rightStart.Value) : 0f;
                Debug.Log($"[StartHold] 정지:{bothStopped}, 위치OK(L:{leftNear}/R:{rightNear}), 홀드:{phaseHoldTime:F0}s");
            }

            positionOk = leftNear && rightNear;
        }

        if (bothStopped && positionOk)
        {
            phaseHoldTime += Time.deltaTime;
            OnHoldProgressChanged?.Invoke(phaseHoldTime, startHoldDuration);

            if (phaseHoldTime >= startHoldDuration)
            {
                OnHoldCompleted?.Invoke();
                OnStartHoldComplete?.Invoke();

                // ★ StartHold 전용 모드 (등척성운동): StartHold만 완료하면 바로 종료
                if (startHoldOnly)
                {
                    Debug.Log("<color=green>[StartHold] 홀드 완료! (StartHold 전용 모드 - 바로 완료)</color>");
                    ChangePhase(EvaluationPhase.Completed);
                    CompleteEvaluation();
                    return;
                }

                Debug.Log("<color=green>[StartHold] 홀드 완료! Moving 단계로</color>");

                // ★ 사용자 기준 위치/회전 저장 (상대 이동 감지용)
                if (useRelativeMovement && playerRightHand != null)
                {
                    // 손목 본을 아직 못찾았으면 다시 검색
                    if (rightWristBone == null)
                        FindWristBones();

                    // ★ 위치: 콜라이더가 있으면 콜라이더 중심, 없으면 transform 위치 사용
                    if (rightHandCollider != null)
                    {
                        userHoldReferencePosition = rightHandCollider.bounds.center;
                    }
                    else
                    {
                        userHoldReferencePosition = playerRightHand.transform.position;
                    }

                    // 회전은 손목 본에서 가져옴 (더 정확한 손목 회전)
                    userHoldReferenceRotation = rightWristBone != null ? rightWristBone.rotation : playerRightHand.transform.rotation;
                    Vector3 euler = userHoldReferenceRotation.eulerAngles;
                    string wristInfo = rightWristBone != null ? rightWristBone.name : "루트";
                    string posSource = rightHandCollider != null ? "콜라이더" : "transform";
                    Debug.Log($"<color=cyan>[StartHold] 기준 저장 - 위치:{userHoldReferencePosition} [{posSource}], 회전:({euler.x:F0},{euler.y:F0},{euler.z:F0}) [{wristInfo}]</color>");
                }
                else
                {
                    Debug.Log($"<color=red>[StartHold] 기준 저장 실패! useRelativeMovement:{useRelativeMovement}, playerRightHand:{playerRightHand != null}</color>");
                }

                StartGuideHandPlayback();
                ChangePhase(EvaluationPhase.Moving);
            }
        }
        else
        {
            if (phaseHoldTime > 0.1f && showDebugLogs)
                Debug.Log($"<color=orange>[StartHold] 홀드 중단</color>");

            phaseHoldTime = 0f;
            OnHoldProgressChanged?.Invoke(0f, startHoldDuration);
        }
    }

    /// <summary>
    /// 3단계: 자유 이동
    /// 50% 초과 시 지속 경고, 30~50% 구간에서 홀드 대기
    /// </summary>
    private void UpdateMoving(Vector3 leftPos, Vector3 rightPos)
    {
        // 왼손 이탈 체크 (시작 위치에서 너무 벗어나면 경고)
        float leftDrift = Vector3.Distance(leftPos, leftHandStartHoldPosition);
        if (leftDrift > leftHandDriftThreshold)
        {
            OnLeftHandDrifted?.Invoke(leftDrift);

            // 감점 기록
            if (currentSession != null)
                currentSession.leftHandDriftCount++;

            if (showDebugLogs)
                Debug.Log($"<color=orange>[Moving] 왼손 이탈! 거리: {leftDrift:F3}m</color>");
        }

        // 현재 진행률 계산 (사용자 손 위치 기준)
        float progress = GetCurrentProgress();

        // ★ 현재 제한 비율 (스트레칭/재평가 시 확장됨)
        float limitRatio = currentMidHoldEnd;
        string limitLabel = isExtendedLimitMode ? $"{limitRatio:P0}(확장)" : $"{limitRatio:P0}";

        // ★ 제한 초과 경고 체크 (지속적으로 경고, 돌아가기 전까지)
        if (progress > limitRatio)
        {
            // 제한 초과 상태 진입 시 경고 횟수 1회 증가
            if (!isOverLimitBarrier)
            {
                isOverLimitBarrier = true;
                if (currentSession != null)
                    currentSession.limitWarningCount++;

                Debug.Log($"<color=red>[Moving] ★★★ {limitLabel} 초과! 돌아가세요! (경고 #{currentSession?.limitWarningCount}) ★★★</color>");
            }

            // 지속적으로 경고 이벤트 발생 (UI 알람용)
            OnLimitWarning?.Invoke(progress);

            if (showDebugLogs && Time.frameCount % 30 == 0)
                Debug.Log($"<color=red>[Moving] 경고 지속 중! 진행률: {progress:P0} ({limitLabel} 이하로 돌아가세요)</color>");
        }
        else
        {
            // 제한 이하로 돌아오면 경고 상태 해제
            if (isOverLimitBarrier)
            {
                isOverLimitBarrier = false;
                Debug.Log($"<color=green>[Moving] {limitLabel} 이하로 복귀! 경고 해제</color>");
            }
        }

        // 중간 홀드 구간 체크 (30~제한비율)
        if (progress >= midHoldStartRatio && progress <= limitRatio)
        {
            // 중간 홀드 구간 진입 → 중간 홀드 단계로
            OnMidHoldBegin?.Invoke();  // "멈추세요" 안내
            ChangePhase(EvaluationPhase.MidHold);
        }

        if (showDebugLogs && Time.frameCount % 60 == 0)
            Debug.Log($"[Moving] 진행률: {progress:P0}, 제한:{limitLabel}, 왼손이탈: {leftDrift:F3}m, 초과:{isOverLimitBarrier}");
    }

    /// <summary>
    /// 4단계: 중간 홀드 (3초)
    /// </summary>
    private void UpdateMidHold(Vector3 leftPos, Vector3 rightPos, float rightVel)
    {
        // 오른손 정지 체크
        bool rightStopped = rightVel < holdVelocityThreshold;

        // 왼손 이탈 체크
        float leftDrift = Vector3.Distance(leftPos, leftHandStartHoldPosition);
        bool leftOk = leftDrift <= leftHandDriftThreshold;

        // 오른손 목표 구간 내 체크 (0.3~0.5)
        float progress = GetCurrentProgress();
        bool rightInRange = progress >= midHoldStartRatio && progress <= midHoldEndRatio;

        if (rightStopped && leftOk && rightInRange)
        {
            phaseHoldTime += Time.deltaTime;
            OnHoldProgressChanged?.Invoke(phaseHoldTime, midHoldDuration);

            if (showDebugLogs && Time.frameCount % 30 == 0)
                Debug.Log($"[MidHold] 홀드 진행: {phaseHoldTime:F0}s");

            if (phaseHoldTime >= midHoldDuration)
            {
                // 중간 홀드 완료
                OnMidHoldComplete?.Invoke();
                OnHoldCompleted?.Invoke();
                ChangePhase(EvaluationPhase.Completed);
                CompleteEvaluation();
            }
        }
        else
        {
            // 홀드 중단 - 초기화
            if (phaseHoldTime > 0.1f)
            {
                string reason = !rightStopped ? "오른손 움직임" :
                               (!leftOk ? "왼손 이탈" : "구간 이탈");
                if (showDebugLogs)
                    Debug.Log($"<color=orange>[MidHold] 홀드 중단: {reason}</color>");
            }

            phaseHoldTime = 0f;
            OnHoldProgressChanged?.Invoke(0f, midHoldDuration);
        }
    }

    /// <summary>
    /// 현재 진행률 (0~1) - 사용자 손 위치 기반
    /// </summary>
    public float GetCurrentProgress()
    {
        return userHandFrameRatio;
    }

    /// <summary>
    /// 현재 사용자 손이 위치한 프레임 인덱스
    /// </summary>
    public int GetUserHandFrameIndex()
    {
        return userHandFrameIndex;
    }

    /// <summary>
    /// 총 프레임 수
    /// </summary>
    public int GetTotalFrameCount()
    {
        return loadedFrames != null ? loadedFrames.Count : 0;
    }

    /// <summary>
    /// 현재 가이드 프레임 인덱스 가져오기
    /// </summary>
    public int GetCurrentGuideFrameIndex()
    {
        return currentGuideFrameIndex;
    }

    /// <summary>
    /// 현재 평가 단계
    /// </summary>
    public EvaluationPhase CurrentPhase => currentPhase;

    /// <summary>
    /// 사용자 손 움직임 기반으로 진행률 계산
    /// ★ 상대 이동 모드: 시작 홀드 위치 기준으로 이동량/회전량에 따라 진행률 계산
    /// </summary>
    private void UpdateUserHandFrame()
    {
        if (loadedFrames == null || loadedFrames.Count == 0) return;
        if (playerRightHand == null) return;

        // Moving 또는 MidHold 단계에서만 프레임 업데이트
        if (currentPhase != EvaluationPhase.Moving && currentPhase != EvaluationPhase.MidHold)
        {
            userHandFrameIndex = 0;
            userHandFrameRatio = 0f;
            return;
        }

        // ★ 손 위치: 콜라이더가 있으면 콜라이더 중심, 없으면 transform 위치 사용
        Vector3 rightHandPos;
        if (rightHandCollider != null)
        {
            rightHandPos = rightHandCollider.bounds.center;
        }
        else
        {
            rightHandPos = playerRightHand.transform.position;
        }

        // 회전은 손목 본에서 가져옴 (더 정확한 손목 회전 감지)
        Quaternion rightHandRot = rightWristBone != null ? rightWristBone.rotation : playerRightHand.transform.rotation;

        float prevRatio = userHandFrameRatio;
        float newRatio = 0f;

        // ★ 상대 이동 모드: 시작 홀드 위치 기준으로 진행률 계산
        if (useRelativeMovement)
        {
            if (isPositionBasedMovement)
            {
                // 위치 기반: 기준 위치에서 얼마나 이동했는지 계산
                Vector3 displacement = rightHandPos - userHoldReferencePosition;

                // 방법 1: 축 방향 프로젝션 (방향 무관하게 절대값 사용)
                float projectedDistance = Vector3.Dot(displacement, movementAxis);
                float absProjectedDistance = Mathf.Abs(projectedDistance);

                // 방법 2: 전체 이동 거리 (축 방향 무시)
                float totalDisplacement = displacement.magnitude;

                // 더 큰 값 사용 (둘 중 하나라도 이동했으면 인정)
                float effectiveDistance = Mathf.Max(absProjectedDistance, totalDisplacement * 0.8f);

                // ★ 핸드데이터 이동 거리가 너무 작으면 기본값 사용 (5cm)
                float targetDistance = Mathf.Max(handDataTotalDistance, 0.05f);

                // 핸드데이터 총 이동 거리로 나눠서 0~1 비율 계산
                newRatio = Mathf.Clamp01(effectiveDistance / targetDistance);

                if (showDebugLogs && Time.frameCount % 30 == 0)
                {
                    string posSource = rightHandCollider != null ? "[콜라이더]" : "[transform]";
                    Debug.Log($"<color=yellow>[Position Move] 이동:{effectiveDistance:F3}m / 목표:{targetDistance:F3}m = {newRatio:P0} (데이터거리:{handDataTotalDistance:F3}m)</color>");
                    Debug.Log($"<color=cyan>  기준:{userHoldReferencePosition}, 현재:{rightHandPos} {posSource}</color>");
                }
            }
            else
            {
                // 회전 기반: 기준 회전에서 얼마나 회전했는지 계산
                float rotationAngle = Quaternion.Angle(userHoldReferenceRotation, rightHandRot);

                // 핸드데이터 총 회전 각도로 나눠서 0~1 비율 계산
                if (handDataTotalRotation > 1f)
                {
                    newRatio = Mathf.Clamp01(rotationAngle / handDataTotalRotation);

                    // ★ 회전 방향 반전 옵션
                    if (invertRotationDirection)
                    {
                        newRatio = 1f - newRatio;
                    }
                }

                if (showDebugLogs && Time.frameCount % 30 == 0)
                {
                    Vector3 refEuler = userHoldReferenceRotation.eulerAngles;
                    Vector3 curEuler = rightHandRot.eulerAngles;
                    string invertInfo = invertRotationDirection ? "(반전)" : "";
                    Debug.Log($"<color=yellow>[Relative Rotate] 회전:{rotationAngle:F1}° / {handDataTotalRotation:F1}° = {newRatio:P0} {invertInfo}</color>");
                    Debug.Log($"<color=cyan>  기준:({refEuler.x:F0},{refEuler.y:F0},{refEuler.z:F0}) → 현재:({curEuler.x:F0},{curEuler.y:F0},{curEuler.z:F0})</color>");
                }
            }
        }
        else
        {
            // 기존 프레임 매칭 방식 (fallback)
            newRatio = CalculateFrameMatchingRatio(rightHandPos, rightHandRot);
        }

        // 비율 업데이트
        userHandFrameRatio = newRatio;
        userHandFrameIndex = Mathf.RoundToInt(newRatio * (loadedFrames.Count - 1));

        // 변화 시 이벤트 발생
        if (Mathf.Abs(prevRatio - userHandFrameRatio) > 0.01f)
        {
            OnUserFrameChanged?.Invoke(userHandFrameIndex, loadedFrames.Count, userHandFrameRatio);

            if (showDebugLogs && Time.frameCount % 15 == 0)
            {
                Debug.Log($"<color=green>[Progress] {userHandFrameRatio:P0} (프레임:{userHandFrameIndex}/{loadedFrames.Count})</color>");
            }
        }

        // ★ 매 프레임마다 환자 애니메이션 실시간 동기화
        if (syncAnimationWithFrame && animationPlayMode == AnimationPlayMode.SyncWithUser)
        {
            SyncAnimationToFrame(userHandFrameRatio);
        }
    }

    /// <summary>
    /// 기존 프레임 매칭 방식 (fallback)
    /// </summary>
    private float CalculateFrameMatchingRatio(Vector3 rightHandPos, Quaternion rightHandRot)
    {
        Vector3 positionOffset = Vector3.zero;
        if (referenceTransform != null)
        {
            positionOffset = referenceTransform.position - recordedPatientOffset;
        }

        float minScore = float.MaxValue;
        int closestFrame = 0;

        for (int i = 0; i < loadedFrames.Count; i++)
        {
            Vector3 framePos = loadedFrames[i].rightRootPosition + positionOffset;
            float positionDist = Vector3.Distance(rightHandPos, framePos);
            float score = positionDist;

            if (useRotationMatching && rotationWeight > 0f)
            {
                Quaternion frameRot = loadedFrames[i].rightRootRotation;
                float rotationDiff = Quaternion.Angle(rightHandRot, frameRot) / 180f;
                score = positionDist * (1f - rotationWeight) + rotationDiff * rotationWeight;
            }

            if (score < minScore)
            {
                minScore = score;
                closestFrame = i;
            }
        }

        return Mathf.Clamp01((float)closestFrame / Mathf.Max(1, loadedFrames.Count - 1));
    }

    /// <summary>
    /// 애니메이션을 프레임 비율에 맞춰 동기화
    /// </summary>
    private void SyncAnimationToFrame(float ratio)
    {
        if (patientAnimator == null || string.IsNullOrEmpty(currentAnimationStateName))
            return;

        // 충돌 모드에서는 선형보간 사용
        if (useCollisionMode)
        {
            targetAnimationRatio = Mathf.Clamp01(ratio);
            return;  // UpdateAnimationLerp에서 처리
        }

        // 기존 방식: 즉시 적용
        float normalizedTime = Mathf.Clamp01(ratio);
        patientAnimator.Play(currentAnimationStateName, 0, normalizedTime);
        patientAnimator.speed = 0f;

        if (showDebugLogs && Time.frameCount % 30 == 0)
            Debug.Log($"[Animation] 동기화: {currentAnimationStateName} @ {normalizedTime:P0}");
    }

    /// <summary>
    /// 충돌 감지 업데이트 (거리 기반, 스케일 적용)
    /// 콜라이더가 없으면 손 Transform 위치 사용
    /// ★ HandCollisionShape에 따라 구형/박스형/손바닥만 감지
    /// </summary>
    private void UpdateCollisionDetection()
    {
        if (patientHeadCollider == null)
        {
            if (showDebugLogs && Time.frameCount % 120 == 0)
                Debug.Log("<color=red>[Collision] patientHeadCollider가 NULL!</color>");
            return;
        }

        // 환자 콜라이더 정보
        Bounds patientBounds = patientHeadCollider.bounds;
        Vector3 patientCenter = patientBounds.center;

        // 왼손 충돌 감지
        if (playerLeftHand != null)
        {
            bool wasTouch = isLeftHandTouchingPatient;
            isLeftHandTouchingPatient = CheckHandCollision(playerLeftHand.transform, leftHandCollider, patientBounds, true);

            if (isLeftHandTouchingPatient && !wasTouch && showDebugLogs)
                Debug.Log("<color=green>[Collision] 왼손이 환자에게 닿음!</color>");
        }

        // 오른손 충돌 감지
        if (playerRightHand != null)
        {
            bool wasTouch = isRightHandTouchingPatient;
            isRightHandTouchingPatient = CheckHandCollision(playerRightHand.transform, rightHandCollider, patientBounds, false);

            if (isRightHandTouchingPatient && !wasTouch && showDebugLogs)
                Debug.Log("<color=green>[Collision] 오른손이 환자에게 닿음!</color>");
        }

        // 디버그: 양손 충돌 상태 표시
        if (showDebugLogs && Time.frameCount % 60 == 0)
        {
            string shapeInfo = handCollisionShape.ToString();

            if (playerLeftHand != null)
            {
                string lStatus = isLeftHandTouchingPatient ? "<color=green>접촉</color>" : "<color=red>미접촉</color>";
                Debug.Log($"<color=cyan>[왼손] {lStatus} [{shapeInfo}]</color>");
            }

            if (playerRightHand != null)
            {
                string rStatus = isRightHandTouchingPatient ? "<color=green>접촉</color>" : "<color=red>미접촉</color>";
                Debug.Log($"<color=cyan>[오른손] {rStatus} [{shapeInfo}]</color>");
            }
        }
    }

    /// <summary>
    /// ★ 손 충돌 감지 (형태에 따라 다른 방식 사용)
    /// </summary>
    private bool CheckHandCollision(Transform handTransform, Collider handCollider, Bounds patientBounds, bool isLeftHand)
    {
        Vector3 handCenter;
        Vector3 handForward = handTransform.forward;
        Vector3 handRight = handTransform.right;
        Vector3 handUp = handTransform.up;

        // 손 중심 위치 결정
        if (handCollider != null)
        {
            handCenter = handCollider.bounds.center;
        }
        else
        {
            handCenter = handTransform.position;
        }

        // 손가락 방향으로 오프셋 적용
        handCenter += handForward * handCollisionForwardOffset;

        switch (handCollisionShape)
        {
            case HandCollisionShape.Sphere:
                return CheckSphereCollision(handCenter, handCollider, patientBounds);

            case HandCollisionShape.Box:
                // 박스: 손바닥 + 손가락 전체 영역
                Vector3 boxSize = new Vector3(
                    palmWidth * handColliderScale,
                    palmThickness * handColliderScale,
                    (palmHeight + fingerLength) * handColliderScale
                );
                // 박스 중심을 손가락 방향으로 약간 이동 (손바닥+손가락 중심)
                Vector3 boxCenter = handCenter + handForward * (fingerLength * 0.3f * handColliderScale);
                return CheckBoxCollision(boxCenter, boxSize, handTransform.rotation, patientBounds);

            case HandCollisionShape.PalmOnly:
                // 손바닥만: 작은 박스
                Vector3 palmSize = new Vector3(
                    palmWidth * handColliderScale,
                    palmThickness * handColliderScale,
                    palmHeight * handColliderScale
                );
                return CheckBoxCollision(handCenter, palmSize, handTransform.rotation, patientBounds);

            default:
                return CheckSphereCollision(handCenter, handCollider, patientBounds);
        }
    }

    /// <summary>
    /// 구형 충돌 감지 (기존 방식)
    /// </summary>
    private bool CheckSphereCollision(Vector3 handCenter, Collider handCollider, Bounds patientBounds)
    {
        float handRadius;
        if (handCollider != null)
        {
            handRadius = Mathf.Max(handCollider.bounds.extents.x,
                handCollider.bounds.extents.y, handCollider.bounds.extents.z) * handColliderScale;
        }
        else
        {
            handRadius = defaultHandCollisionRadius * handColliderScale;
        }

        float patientRadius = Mathf.Max(patientBounds.extents.x, patientBounds.extents.y, patientBounds.extents.z);
        float distance = Vector3.Distance(handCenter, patientBounds.center);

        return distance <= (handRadius + patientRadius);
    }

    /// <summary>
    /// ★ 박스 충돌 감지 (OBB vs AABB 간소화 버전)
    /// 손 박스가 환자 바운드와 교차하는지 확인
    /// </summary>
    private bool CheckBoxCollision(Vector3 boxCenter, Vector3 boxSize, Quaternion boxRotation, Bounds patientBounds)
    {
        // 박스의 8개 꼭짓점 계산
        Vector3 halfSize = boxSize * 0.5f;
        Vector3[] corners = new Vector3[8];
        int idx = 0;
        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 localCorner = new Vector3(halfSize.x * x, halfSize.y * y, halfSize.z * z);
                    corners[idx++] = boxCenter + boxRotation * localCorner;
                }
            }
        }

        // 꼭짓점 중 하나라도 환자 바운드 안에 있으면 충돌
        foreach (var corner in corners)
        {
            if (patientBounds.Contains(corner))
                return true;
        }

        // 환자 바운드 중심이 손 박스 안에 있는지 확인 (역방향 체크)
        Vector3 patientCenterLocal = Quaternion.Inverse(boxRotation) * (patientBounds.center - boxCenter);
        if (Mathf.Abs(patientCenterLocal.x) <= halfSize.x &&
            Mathf.Abs(patientCenterLocal.y) <= halfSize.y &&
            Mathf.Abs(patientCenterLocal.z) <= halfSize.z)
        {
            return true;
        }

        // 박스 중심이 환자 바운드와 가까운지 확인 (확장된 바운드)
        Bounds expandedBounds = patientBounds;
        expandedBounds.Expand(boxSize.magnitude * 0.5f);
        if (expandedBounds.Contains(boxCenter))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 애니메이션 선형보간 업데이트
    /// ★ 손이 환자에게 닿은 상태에서만 애니메이션 업데이트
    /// </summary>
    private void UpdateAnimationLerp()
    {
        if (!useCollisionMode) return;

        bool isHandTouching = isLeftHandTouchingPatient || isRightHandTouchingPatient;

        // 애니메이션 상태 체크 로그
        if (showDebugLogs && Time.frameCount % 120 == 0)
        {
            Debug.Log($"<color=yellow>[Animation Check] Animator:{(patientAnimator != null ? patientAnimator.name : "NULL")}, 상태이름:'{currentAnimationStateName}', 단계:{currentPhase}, 접촉:{isHandTouching}</color>");
        }

        if (patientAnimator == null || string.IsNullOrEmpty(currentAnimationStateName)) return;

        // ★ 손이 닿은 상태 + Moving/MidHold 단계에서만 애니메이션 업데이트
        if (isHandTouching && (currentPhase == EvaluationPhase.Moving || currentPhase == EvaluationPhase.MidHold))
        {
            currentAnimationRatio = Mathf.Lerp(currentAnimationRatio, targetAnimationRatio, Time.deltaTime * animationLerpSpeed);
            patientAnimator.Play(currentAnimationStateName, 0, currentAnimationRatio);
            patientAnimator.speed = 0f;

            if (showDebugLogs && Time.frameCount % 30 == 0)
            {
                Debug.Log($"<color=green>[Animation Lerp] '{currentAnimationStateName}' @ {currentAnimationRatio:P0} → {targetAnimationRatio:P0} (진행률:{userHandFrameRatio:P0})</color>");
            }
        }
        else if (showDebugLogs && Time.frameCount % 60 == 0)
        {
            Debug.Log($"<color=orange>[Animation Skip] 접촉:{isHandTouching}, 단계:{currentPhase}, 애니메이션:'{currentAnimationStateName}'</color>");
        }
    }

    /// <summary>
    /// ★ AutoPlay 모드 업데이트 - 핸드데이터 없이 애니메이션만 자동 재생
    /// </summary>
    private void UpdateAutoPlay()
    {
        if (patientAnimator == null || string.IsNullOrEmpty(currentAnimationStateName))
        {
            // 애니메이션이 없으면 바로 완료 처리
            if (showDebugLogs)
                Debug.Log("<color=orange>[AutoPlay] 애니메이션 없음 - 즉시 완료</color>");
            CompleteAutoPlay();
            return;
        }

        // 현재 애니메이션 상태 정보 가져오기
        AnimatorStateInfo stateInfo = patientAnimator.GetCurrentAnimatorStateInfo(0);

        // 경과 시간으로 진행률 계산
        float elapsed = Time.time - autoPlayStartTime;
        autoPlayProgress = Mathf.Clamp01(elapsed / autoPlayDuration);

        // 진행률 이벤트 발생
        OnUserFrameChanged?.Invoke(0, 1, autoPlayProgress);

        // 디버그 로그
        if (showDebugLogs && Time.frameCount % 60 == 0)
        {
            Debug.Log($"<color=cyan>[AutoPlay] 진행: {autoPlayProgress:P0} ({elapsed:F1}s / {autoPlayDuration:F1}s)</color>");
        }

        // 애니메이션 완료 체크 (normalizedTime >= 1 또는 지정 시간 경과)
        bool animationComplete = stateInfo.normalizedTime >= 0.99f;
        bool timeComplete = elapsed >= autoPlayDuration;

        if (animationComplete || timeComplete)
        {
            if (showDebugLogs)
            {
                string reason = animationComplete ? "애니메이션 끝" : "시간 초과";
                Debug.Log($"<color=green>[AutoPlay] 완료! ({reason})</color>");
            }
            CompleteAutoPlay();
        }
    }

    /// <summary>
    /// AutoPlay 완료 처리
    /// </summary>
    private void CompleteAutoPlay()
    {
        isAutoPlayMode = false;
        autoPlayProgress = 1f;

        // 평가 완료 처리
        ChangePhase(EvaluationPhase.Completed);
        CompleteEvaluation();

        // 완료 이벤트
        OnAutoPlayCompleted?.Invoke();
    }

    /// <summary>
    /// AutoPlay 완료 이벤트
    /// </summary>
    public event Action OnAutoPlayCompleted;

    /// <summary>
    /// 손이 환자에게 닿았는지 확인
    /// </summary>
    public bool IsAnyHandTouchingPatient()
    {
        return isLeftHandTouchingPatient || isRightHandTouchingPatient;
    }

    /// <summary>
    /// 환자 애니메이션 설정 (시나리오 데이터에서)
    /// </summary>
    public void SetPatientAnimation(string animationStateName, AnimationPlayMode playMode = AnimationPlayMode.SyncWithUser)
    {
        currentAnimationStateName = animationStateName;
        animationPlayMode = playMode;

        Debug.Log($"<color=magenta>[Animation] ★ 애니메이션 설정 시도: '{animationStateName}' (모드:{playMode})</color>");

        if (patientAnimator == null)
        {
            // Patient 태그로 Animator 찾기
            var patient = GameObject.FindGameObjectWithTag("Patient");
            if (patient != null)
            {
                patientAnimator = patient.GetComponent<Animator>();
                Debug.Log($"<color=cyan>[Animation] Patient 태그로 Animator 찾음: {patient.name}</color>");
            }
            else
            {
                Debug.Log("<color=red>[Animation] Patient 태그 오브젝트를 찾을 수 없음!</color>");
            }
        }

        if (patientAnimator == null)
        {
            Debug.Log("<color=red>[Animation] patientAnimator가 NULL - 애니메이션 재생 불가!</color>");
            return;
        }

        if (string.IsNullOrEmpty(animationStateName))
        {
            Debug.Log("<color=orange>[Animation] 애니메이션 이름이 비어있음</color>");
            return;
        }

        // Animator에 해당 상태가 있는지 확인
        bool hasState = patientAnimator.HasState(0, Animator.StringToHash(animationStateName));
        Debug.Log($"<color=cyan>[Animation] Animator '{patientAnimator.name}'에 상태 '{animationStateName}' 존재: {hasState}</color>");

        if (playMode == AnimationPlayMode.AutoPlay)
        {
            // 자동 재생 모드
            patientAnimator.Play(animationStateName);
            patientAnimator.speed = 1f;
            Debug.Log($"<color=green>[Animation] 자동 재생 시작: {animationStateName}</color>");
        }
        else if (playMode == AnimationPlayMode.SyncWithUser)
        {
            // 사용자 동기화 모드 - 시작 위치로 설정
            patientAnimator.Play(animationStateName, 0, 0f);
            patientAnimator.speed = 0f;
            Debug.Log($"<color=green>[Animation] 동기화 모드 시작: {animationStateName} (첫 프레임)</color>");
        }
    }

    /// <summary>
    /// SubStepData에서 애니메이션 및 이동 타입 설정
    /// </summary>
    public void SetPatientAnimationFromSubStep(SubStepData subStep)
    {
        // 이동 타입 설정 (position/rotation)
        if (subStep != null && !string.IsNullOrEmpty(subStep.movementType))
        {
            specifiedMovementType = subStep.movementType.Trim().ToLower();
            Debug.Log($"<color=magenta>[ChunaPathEvaluator] 이동 타입 지정: '{specifiedMovementType}'</color>");
        }
        else
        {
            specifiedMovementType = null; // 자동 감지 사용
        }

        // StartHold만 체크 모드 (등척성운동 등)
        // 1. 핸드데이터 파일명에 "등척성" 포함 시 자동 활성화
        // 2. conditionParams에 "startHoldOnly" 포함 시 활성화
        bool isIsometricExercise = subStep != null && !string.IsNullOrEmpty(subStep.handTrackingFileName) &&
            subStep.handTrackingFileName.Contains("등척성");
        bool hasStartHoldOnlyParam = subStep != null && !string.IsNullOrEmpty(subStep.conditionParams) &&
            subStep.conditionParams.ToLower().Contains("startholdonly");

        if (isIsometricExercise || hasStartHoldOnlyParam)
        {
            startHoldOnly = true;
            Debug.Log($"<color=yellow>[ChunaPathEvaluator] StartHold 전용 모드 활성화 (등척성 운동)</color>");
        }
        else
        {
            startHoldOnly = false;
        }

        // 시나리오 데이터에 애니메이션 클립이 있을 때만 설정
        if (subStep != null && subStep.HasPatientAnimation())
        {
            string animStateName = subStep.patientAnimationClip;
            AnimationPlayMode mode = subStep.GetAnimationPlayMode();
            SetPatientAnimation(animStateName, mode);
            Debug.Log($"<color=cyan>[ChunaPathEvaluator] 환자 애니메이션 로드: {animStateName}</color>");
        }
        else
        {
            // 비어있으면 애니메이션 없이 진행
            currentAnimationStateName = null;
            if (showDebugLogs)
                Debug.Log("<color=yellow>[ChunaPathEvaluator] 애니메이션 클립 없음 - 애니메이션 없이 진행</color>");
        }
    }

    /// <summary>
    /// 애니메이션 정지 및 초기화
    /// </summary>
    public void StopPatientAnimation()
    {
        if (patientAnimator != null)
        {
            patientAnimator.speed = 0f;
        }
        currentAnimationStateName = null;
    }

    /// <summary>
    /// ★ AutoPlay 모드 시작 - 핸드데이터 없이 애니메이션만 자동 재생
    /// </summary>
    /// <param name="duration">자동 재생 시간 (초). 0이면 애니메이션 길이 사용</param>
    public void StartAutoPlay(float duration = 0f)
    {
        isAutoPlayMode = true;
        autoPlayStartTime = Time.time;
        autoPlayProgress = 0f;

        // duration이 0이면 애니메이션 클립 길이 사용, 없으면 기본값 3초
        if (duration > 0f)
        {
            autoPlayDuration = duration;
        }
        else if (patientAnimator != null && !string.IsNullOrEmpty(currentAnimationStateName))
        {
            // 애니메이션 클립 길이 가져오기
            AnimatorStateInfo stateInfo = patientAnimator.GetCurrentAnimatorStateInfo(0);
            autoPlayDuration = stateInfo.length > 0f ? stateInfo.length : 3f;
        }
        else
        {
            autoPlayDuration = 3f; // 기본값
        }

        // 평가 시작 처리
        isEvaluating = true;
        evaluationStartTime = Time.time;
        ChangePhase(EvaluationPhase.Moving); // AutoPlay는 바로 Moving 단계로

        Debug.Log($"<color=green>[AutoPlay] ★ 자동 재생 시작! 시간:{autoPlayDuration:F1}초, 애니메이션:{currentAnimationStateName ?? "없음"}</color>");
    }

    /// <summary>
    /// ★ SubStep에서 AutoPlay 모드 시작 (핸드데이터 없고 애니메이션만 있는 경우)
    /// </summary>
    public void StartAutoPlayFromSubStep(SubStepData subStep)
    {
        if (subStep == null) return;

        // 애니메이션 설정
        SetPatientAnimationFromSubStep(subStep);

        // duration 파싱 시도
        float duration = subStep.duration > 0 ? subStep.duration : 0f;

        // AutoPlay 시작
        StartAutoPlay(duration);
    }

    /// <summary>
    /// 현재 AutoPlay 모드인지 확인
    /// </summary>
    public bool IsAutoPlayMode => isAutoPlayMode;

    /// <summary>
    /// AutoPlay 진행률 (0~1)
    /// </summary>
    public float AutoPlayProgress => autoPlayProgress;

    // ========== 스트레칭/재평가 확장 제한 모드 ==========

    /// <summary>
    /// ★ 확장 제한 모드 활성화 (스트레칭/재평가용)
    /// 제한 장벽이 50% → 65%로 확장됨
    /// </summary>
    public void EnableExtendedLimitMode()
    {
        isExtendedLimitMode = true;
        Debug.Log($"<color=magenta>[ChunaPathEvaluator] ★ 확장 제한 모드 활성화 - 제한:{currentMidHoldEnd:P0}</color>");
    }

    /// <summary>
    /// 확장 제한 모드 비활성화 (기본 50%로 복귀)
    /// </summary>
    public void DisableExtendedLimitMode()
    {
        isExtendedLimitMode = false;
        Debug.Log($"<color=cyan>[ChunaPathEvaluator] 확장 제한 모드 비활성화 - 제한:{currentMidHoldEnd:P0}</color>");
    }

    /// <summary>
    /// 현재 확장 제한 모드인지 확인
    /// </summary>
    public bool IsExtendedLimitMode => isExtendedLimitMode;

    /// <summary>
    /// 현재 제한 비율 반환 (확장 모드 여부에 따라)
    /// </summary>
    public float CurrentLimitRatio => currentMidHoldEnd;

    /// <summary>
    /// Step 이름에 따라 확장 제한 모드 자동 설정
    /// "스트레칭" 또는 "재평가"가 포함되면 확장 모드 활성화
    /// </summary>
    public void SetExtendedLimitModeFromStepName(string stepName)
    {
        if (string.IsNullOrEmpty(stepName))
        {
            DisableExtendedLimitMode();
            return;
        }

        bool shouldExtend = stepName.Contains("스트레칭") || stepName.Contains("재평가");

        if (shouldExtend)
        {
            EnableExtendedLimitMode();
        }
        else
        {
            DisableExtendedLimitMode();
        }
    }

    /// <summary>
    /// 홀드 감지 업데이트 - 목표 위치에서 손이 일정 시간 정지하면 다음 단계로
    /// 왼손: 시작 위치 유지 체크, 오른손: 끝 지점 정지 체크
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

        // 오른손(주동수) 정지 판정
        bool rightHandStopped = rightVelocity < holdVelocityThreshold;

        // 왼손(보조수): 시작 위치(첫 번째 체크포인트)에서 유지하고 있는지 체크
        bool leftHandAtStart = IsLeftHandAtStartPosition(leftPos);

        // 오른손(주동수): 끝 지점(마지막 체크포인트) 근처인지 확인
        bool rightHandAtTarget = true;
        if (requireNearTargetForHold)
        {
            rightHandAtTarget = IsRightHandAtTargetPosition(rightPos);
        }

        // 리밋 범위 내 확인 (옵션) - 오른손(주동수)만 체크
        bool inSafeRange = true;
        if (requireLimitSafeForHold && limitChecker != null)
        {
            var rightResult = limitChecker.GetRightHandResult();
            // Danger나 Exceeded 상태가 아니면 OK (오른손만 체크)
            inSafeRange = rightResult.overallStatus != LimitStatus.Exceeded &&
                          rightResult.overallStatus != LimitStatus.Danger;
        }

        // 디버그: 각 조건 상태 출력
        if (showDebugLogs && Time.frameCount % 60 == 0)  // 1초에 한번
        {
            Debug.Log($"[HoldDetection] 왼손시작위치:{leftHandAtStart}, 오른손정지:{rightHandStopped}(vel:{rightVelocity:F3}), 오른손목표:{rightHandAtTarget}, 안전범위:{inSafeRange}");
        }

        // 홀드 조건: 왼손 시작위치 유지 + 오른손 정지 + 오른손 목표 근처 + 안전 범위
        bool canHold = leftHandAtStart && rightHandStopped && rightHandAtTarget && inSafeRange;

        if (canHold)
        {
            if (!isHolding)
            {
                isHolding = true;
                if (showDebugLogs)
                    Debug.Log("<color=yellow>[ChunaPathEvaluator] 홀드 시작 (왼손: 시작위치 유지, 오른손: 목표위치 정지)</color>");
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
            if (isHolding || currentHoldTime > 0f)
            {
                string reason = !leftHandAtStart ? "왼손 시작위치 이탈" :
                               (!rightHandStopped ? "오른손 움직임" :
                               (!rightHandAtTarget ? "오른손 목표위치 이탈" : "안전 범위 이탈"));
                if (showDebugLogs && currentHoldTime > 0.3f)
                    Debug.Log($"<color=orange>[ChunaPathEvaluator] 홀드 중단: {reason} ({currentHoldTime:F0}s)</color>");

                isHolding = false;
                currentHoldTime = 0f;
                OnHoldProgressChanged?.Invoke(0f, requiredHoldTime);
            }
        }
    }

    /// <summary>
    /// 왼손이 시작 위치(첫 번째 체크포인트)에서 유지하고 있는지 확인
    /// </summary>
    private bool IsLeftHandAtStartPosition(Vector3 leftHandPos)
    {
        Vector3? leftStart = GetFirstCheckpointPosition(true);

        if (leftStart.HasValue)
        {
            float dist = Vector3.Distance(leftHandPos, leftStart.Value);
            return dist <= startPositionRadius;
        }

        return true;  // 왼손 체크포인트 없으면 조건 무시
    }

    /// <summary>
    /// 오른손이 목표 위치(마지막 체크포인트)에 있는지 확인
    /// </summary>
    private bool IsRightHandAtTargetPosition(Vector3 rightHandPos)
    {
        Vector3? rightTarget = GetLastCheckpointPosition(false);

        if (rightTarget.HasValue)
        {
            float dist = Vector3.Distance(rightHandPos, rightTarget.Value);
            return dist <= holdTargetRadius;
        }

        return true;  // 오른손 체크포인트 없으면 조건 무시
    }

    /// <summary>
    /// [Deprecated] 손이 목표 위치(경로 끝) 근처에 있는지 확인 - 오른손(주동수)만 체크
    /// IsRightHandAtTargetPosition으로 대체됨
    /// </summary>
    private bool IsNearTargetPosition(Vector3 leftHandPos, Vector3 rightHandPos)
    {
        return IsRightHandAtTargetPosition(rightHandPos);
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

    /// <summary>
    /// 첫 번째 체크포인트 위치 가져오기
    /// </summary>
    private Vector3? GetFirstCheckpointPosition(bool isLeftHand)
    {
        var checkpoints = isLeftHand ? leftCheckpoints : rightCheckpoints;
        if (checkpoints == null || checkpoints.Count == 0)
            return null;

        var firstCp = checkpoints[0];
        return firstCp != null ? firstCp.transform.position : (Vector3?)null;
    }

    /// <summary>
    /// 시작 위치 도달 확인 - 양손 모두 시작 위치에 도달해야 평가 시작
    /// </summary>
    private void CheckStartPositionReached()
    {
        Vector3 leftPos = playerLeftHand != null ? playerLeftHand.transform.position : Vector3.zero;
        Vector3 rightPos = playerRightHand != null ? playerRightHand.transform.position : Vector3.zero;

        Vector3? leftStart = GetFirstCheckpointPosition(true);
        Vector3? rightStart = GetFirstCheckpointPosition(false);

        bool leftNear = true;
        bool rightNear = true;

        // 왼손 시작 위치 체크
        if (leftStart.HasValue && playerLeftHand != null)
        {
            float dist = Vector3.Distance(leftPos, leftStart.Value);
            leftNear = dist <= startPositionRadius;
        }

        // 오른손 시작 위치 체크
        if (rightStart.HasValue && playerRightHand != null)
        {
            float dist = Vector3.Distance(rightPos, rightStart.Value);
            rightNear = dist <= startPositionRadius;
        }

        // 체크포인트가 있는 손만 확인 (양쪽 모두 있으면 양쪽 모두 도달해야 함)
        bool nearStart = true;
        if (leftStart.HasValue && rightStart.HasValue)
            nearStart = leftNear && rightNear;
        else if (leftStart.HasValue)
            nearStart = leftNear;
        else if (rightStart.HasValue)
            nearStart = rightNear;

        if (nearStart)
        {
            hasReachedStartPosition = true;

            // 목 컨트롤러 활성화 (시작 위치 도달 시)
            if (neckController != null && !neckController.IsEnabled)
            {
                neckController.Enable();
            }

            if (showDebugLogs)
                Debug.Log("<color=green>[ChunaPathEvaluator] 양손 시작 위치 도달! 평가를 시작합니다.</color>");
        }
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

        // 손목 본 자동 검색
        FindWristBones();
    }

    /// <summary>
    /// 손목 본 검색 (XRHand_Wrist 또는 XRHand_Palm)
    /// </summary>
    private void FindWristBones()
    {
        if (playerLeftHand != null && leftWristBone == null)
        {
            leftWristBone = FindBoneInHierarchy(playerLeftHand.transform, "Wrist", "Palm");
            if (leftWristBone != null && showDebugLogs)
                Debug.Log($"<color=cyan>[ChunaPathEvaluator] 왼손 손목 본 연결: {leftWristBone.name}</color>");
        }

        if (playerRightHand != null && rightWristBone == null)
        {
            rightWristBone = FindBoneInHierarchy(playerRightHand.transform, "Wrist", "Palm");
            if (rightWristBone != null && showDebugLogs)
                Debug.Log($"<color=cyan>[ChunaPathEvaluator] 오른손 손목 본 연결: {rightWristBone.name}</color>");
        }
    }

    /// <summary>
    /// 계층 구조에서 특정 이름을 포함하는 본 검색
    /// </summary>
    private Transform FindBoneInHierarchy(Transform root, params string[] namePatterns)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>())
        {
            foreach (string pattern in namePatterns)
            {
                if (child.name.Contains(pattern))
                    return child;
            }
        }
        return null;
    }

    /// <summary>
    /// 모듈 자동 탐색
    /// </summary>
    private void FindModules()
    {
        if (limitChecker == null)
            limitChecker = FindObjectOfType<ChunaLimitChecker>();

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

        // ★ 핸드데이터 이동량 계산 (시작-끝 프레임 간)
        CalculateHandDataMovement();

        // ★ 충돌 모드에서는 체크포인트 생성 건너뛰기
        if (useCollisionMode)
        {
            if (showDebugLogs)
                Debug.Log("<color=yellow>[ChunaPathEvaluator] 충돌 모드 - 체크포인트 생성 건너뜀</color>");
        }
        else
        {
            ClearCheckpoints();
            GenerateCheckpointsFromFrames();
        }

        // 리밋 체커에 첫 프레임의 회전을 기준점으로 설정
        SetLimitCheckerReferenceFromFirstFrame();

        if (showDebugLogs)
        {
            if (useCollisionMode)
            {
                Debug.Log($"<color=green>[ChunaPathEvaluator] 데이터 로드 완료 (충돌 모드)</color>");
                Debug.Log($"  - 프레임 수: {loadedFrames.Count}개");
            }
            else
            {
                Debug.Log($"<color=green>[ChunaPathEvaluator] 체크포인트 생성 완료</color>");
                Debug.Log($"  - 왼손: {leftCheckpoints.Count}개");
                Debug.Log($"  - 오른손: {rightCheckpoints.Count}개");
            }
        }
    }

    /// <summary>
    /// 핸드데이터 시작-끝 프레임 간 이동량/회전량 계산
    /// </summary>
    private void CalculateHandDataMovement()
    {
        if (loadedFrames == null || loadedFrames.Count < 2)
        {
            Debug.LogWarning("[ChunaPathEvaluator] 프레임이 2개 미만이라 이동량 계산 불가");
            return;
        }

        PoseFrame firstFrame = loadedFrames[0];
        PoseFrame lastFrame = loadedFrames[loadedFrames.Count - 1];

        // 오른손 기준 이동 벡터 계산
        Vector3 startPos = firstFrame.rightRootPosition;
        Vector3 endPos = lastFrame.rightRootPosition;
        handDataMovementVector = endPos - startPos;
        handDataTotalDistance = handDataMovementVector.magnitude;

        // 이동 축 계산 (정규화)
        if (handDataTotalDistance > 0.001f)
        {
            movementAxis = handDataMovementVector.normalized;
        }
        else
        {
            movementAxis = Vector3.forward;
        }

        // 오른손 기준 회전량 계산
        Quaternion startRot = firstFrame.rightRootRotation;
        Quaternion endRot = lastFrame.rightRootRotation;
        handDataTotalRotation = Quaternion.Angle(startRot, endRot);

        // 위치 기반 vs 회전 기반 결정
        // ★ CSV에서 지정한 타입이 있으면 그것을 사용, 없으면 자동 감지
        if (!string.IsNullOrEmpty(specifiedMovementType))
        {
            // StartsWith로 비교 (혹시 모를 공백/특수문자 대비)
            isPositionBasedMovement = specifiedMovementType.StartsWith("position");
            string moveType = isPositionBasedMovement ? "위치 기반" : "회전 기반";
            Debug.Log($"<color=magenta>[HandData Analysis] ★ {moveType} (CSV 지정: '{specifiedMovementType}')</color>");
        }
        else
        {
            // 자동 감지: 이동 거리가 5cm 미만이고 회전이 15도 이상이면 회전 기반으로 판단
            isPositionBasedMovement = handDataTotalDistance >= 0.05f || handDataTotalRotation < 15f;
            if (showDebugLogs)
            {
                string moveType = isPositionBasedMovement ? "위치 기반" : "회전 기반";
                Debug.Log($"<color=magenta>[HandData Analysis] {moveType} (자동 감지)</color>");
            }
        }

        // 항상 출력 (디버깅용)
        Debug.Log($"<color=cyan>[HandData] 시작위치: {startPos}, 끝위치: {endPos}</color>");
        Debug.Log($"<color=cyan>[HandData] 이동 거리: {handDataTotalDistance:F3}m, 회전 각도: {handDataTotalRotation:F1}°</color>");
        Debug.Log($"<color=cyan>[HandData] 이동 축: {movementAxis}, 판정: {(isPositionBasedMovement ? "위치기반" : "회전기반")}</color>");
    }

    /// <summary>
    /// 리밋 체커에 PathEvaluator 참조 설정
    /// </summary>
    private void SetLimitCheckerReferenceFromFirstFrame()
    {
        if (limitChecker == null)
            return;

        // PathEvaluator 참조 설정 (프레임 비율 기반 체크용)
        limitChecker.SetPathEvaluator(this);

        if (showDebugLogs)
            Debug.Log($"<color=cyan>[ChunaPathEvaluator] 리밋 체커에 PathEvaluator 참조 설정 완료</color>");
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

        // Guide 레이어 설정
        int guideLayer = LayerMask.NameToLayer(guideLayerName);
        if (guideLayer != -1)
        {
            cpObj.layer = guideLayer;
        }
        else
        {
            Debug.LogWarning($"[ChunaPathEvaluator] '{guideLayerName}' 레이어를 찾을 수 없습니다. Unity에서 레이어를 생성하세요.");
        }

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
        // ★ 이전 가이드 핸드 재생 중지 (스킵 시 중복 재생 방지)
        StopGuideHandPlayback();

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
        hasReachedStartPosition = !requireNearStartToBegin;  // 시작 위치 체크 필요 시 false
        if (playerLeftHand != null)
            lastLeftHandPosition = playerLeftHand.transform.position;
        if (playerRightHand != null)
            lastRightHandPosition = playerRightHand.transform.position;

        // 새로운 평가 흐름 초기화
        currentPhase = EvaluationPhase.WaitingForStart;
        phaseHoldTime = 0f;
        leftHandStartHoldPosition = Vector3.zero;
        isOverLimitBarrier = false;  // 50% 초과 경고 상태 초기화

        // 프레임/애니메이션 상태 초기화
        userHandFrameIndex = 0;
        userHandFrameRatio = 0f;
        currentAnimationRatio = 0f;
        targetAnimationRatio = 0f;

        if (showDebugLogs)
            Debug.Log("<color=green>[ChunaPathEvaluator] 평가 시작 - 시작 위치 대기 중...</color>");

        // 시작 위치 대기 중 첫 프레임 표시 (가이드 핸드 + 환자 애니메이션)
        if (showFirstFrameWhileWaiting)
        {
            ShowGuideHandFirstFrame();

            // 환자 애니메이션도 첫 프레임(0%)으로 설정
            if (patientAnimator != null && !string.IsNullOrEmpty(currentAnimationStateName))
            {
                patientAnimator.Play(currentAnimationStateName, 0, 0f);
                patientAnimator.speed = 0f;

                if (showDebugLogs)
                    Debug.Log($"<color=magenta>[ChunaPathEvaluator] 환자 애니메이션 첫 프레임 설정: {currentAnimationStateName}</color>");
            }
        }

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

        // 리밋 체커 시작
        if (limitChecker != null)
        {
            limitChecker.SetPathEvaluator(this);
            limitChecker.Initialize();
            limitChecker.SetEnabled(true);
        }

        // 가이드 핸드는 StartHold 완료 후에 재생됨 (UpdateStartHold에서 호출)
        // 여기서는 재생하지 않음!

        // 목 컨트롤러 활성화 (충돌 모드가 아닐 때만)
        // 충돌 모드에서는 애니메이션으로 목을 제어하므로 NeckVRController 비활성화
        if (neckController != null && !requireNearStartToBegin && !useCollisionMode)
        {
            neckController.Enable();
        }
        else if (neckController != null && useCollisionMode)
        {
            neckController.Disable();
            if (showDebugLogs)
                Debug.Log("<color=yellow>[Collision Mode] NeckVRController 비활성화 - 애니메이션으로 목 제어</color>");
        }

        if (showDebugLogs)
        {
            if (requireNearStartToBegin)
                Debug.Log("<color=yellow>[ChunaPathEvaluator] 평가 대기 중... 시작 위치로 이동하세요!</color>");
            else
                Debug.Log("<color=green>[ChunaPathEvaluator] 평가 시작! (체크포인트는 점수 지표용)</color>");
        }

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

        // 리밋 체커 중지
        if (limitChecker != null)
        {
            limitChecker.SetEnabled(false);
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
            limitChecker.SetEnabled(false);
        }

        // 목 컨트롤러 비활성화
        if (neckController != null)
        {
            neckController.Disable();
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

    // ========== 리밋 상태 체크 (프레임 비율 기반) ==========

    /// <summary>
    /// 현재 리밋 상태 가져오기 (프레임 비율 기반)
    /// </summary>
    public LimitStatus GetCurrentLimitStatus()
    {
        if (limitChecker == null) return LimitStatus.Safe;
        var result = limitChecker.GetRightHandResult();
        return result.overallStatus;
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
            snapshot.leftLimitRatio = leftResult.frameRatio;
            snapshot.rightLimitRatio = rightResult.frameRatio;

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

    /// <summary>
    /// 첫 프레임만 정적으로 표시 (시작 위치 안내용)
    /// </summary>
    private void ShowGuideHandFirstFrame()
    {
        if (!showGuideHands)
        {
            if (showDebugLogs)
                Debug.LogWarning("[ChunaPathEvaluator] 가이드 핸드 표시 비활성화됨 (showGuideHands=false)");
            return;
        }

        if (loadedFrames == null || loadedFrames.Count == 0)
        {
            if (showDebugLogs)
                Debug.LogWarning("[ChunaPathEvaluator] 가이드 핸드 표시 실패 - 프레임 데이터 없음");
            return;
        }

        if (leftGuideHand == null && rightGuideHand == null)
        {
            Debug.LogWarning("[ChunaPathEvaluator] 가이드 핸드 표시 실패 - leftGuideHand/rightGuideHand가 연결되지 않음!");
            return;
        }

        Vector3 positionOffset = Vector3.zero;
        if (referenceTransform != null)
        {
            positionOffset = referenceTransform.position - recordedPatientOffset;
        }

        PoseFrame firstFrame = loadedFrames[0];

        // 왼손 첫 프레임 표시
        if (leftGuideHand != null)
        {
            leftGuideHand.SetVisible(true);
            leftGuideHand.SetColorAndAlpha(guideHandColor, guideHandColor.a);

            if (leftGuideHand.Root != null)
            {
                leftGuideHand.Root.position = firstFrame.leftRootPosition + positionOffset;
                leftGuideHand.Root.rotation = firstFrame.leftRootRotation;
            }

            foreach (var kvp in firstFrame.leftLocalPoses)
            {
                leftGuideHand.SetJointLocalPose(kvp.Key, kvp.Value.position, kvp.Value.rotation);
            }
        }

        // 오른손 첫 프레임 표시
        if (rightGuideHand != null)
        {
            rightGuideHand.SetVisible(true);
            rightGuideHand.SetColorAndAlpha(guideHandColor, guideHandColor.a);

            if (rightGuideHand.Root != null)
            {
                rightGuideHand.Root.position = firstFrame.rightRootPosition + positionOffset;
                rightGuideHand.Root.rotation = firstFrame.rightRootRotation;
            }

            foreach (var kvp in firstFrame.rightLocalPoses)
            {
                rightGuideHand.SetJointLocalPose(kvp.Key, kvp.Value.position, kvp.Value.rotation);
            }
        }

        if (showDebugLogs)
            Debug.Log("<color=cyan>[ChunaPathEvaluator] 가이드 핸드 첫 프레임 표시 (시작 위치 안내)</color>");
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
                    // 1회 재생 완료 후 대기 시간
                    if (loopDelaySeconds > 0f)
                    {
                        if (showDebugLogs)
                            Debug.Log($"[ChunaPathEvaluator] 가이드 핸드 1회 재생 완료, {loopDelaySeconds}초 대기 후 재시작");
                        yield return new WaitForSeconds(loopDelaySeconds);
                    }
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