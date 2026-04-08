using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using static HandPoseDataLoader;

/// <summary>
/// 충돌 감지 기반 추나 시술 평가 시스템
///
/// 핵심 개념:
/// - 손-환자 충돌 감지로 평가 시작/진행
/// - 피벗 기반 각도로 진행률 계산
/// - 유사도, 리밋 초과, 각도 근접도를 기록하여 점수화
/// </summary>
public class ChunaPathEvaluator : MonoBehaviour
{
    #region SerializeFields

    [Header("=== 기준 위치 (환자) ===")]
    [Tooltip("녹화 시 사용한 기준점과 동일한 Transform 할당 (예: 환자 목 피벗). 미할당 시 Patient 태그 루트를 자동 검색")]
    [SerializeField] private Transform referenceTransform;

    [Tooltip("데이터 기록 시 환자의 위치 오프셋")]
    [SerializeField] private Vector3 recordedPatientOffset = Vector3.zero;

    [Header("=== 직접 할당 충돌체 (환자) ===")]
    [Tooltip("환자 머리에 부착된 충돌체 - 손이 닿으면 트래킹 시작")]
    [SerializeField] private Collider patientHeadCollider;

    [Tooltip("환자 어깨에 부착된 충돌체 - 왼손 추가 감지용")]
    [SerializeField] private Collider patientShoulderCollider;

    [Tooltip("환자 흉부에 부착된 충돌체 - 대흉근 시술용")]
    [SerializeField] private Collider patientChestCollider;

    [Tooltip("환자 왼팔에 부착된 충돌체들 - 상완+전완 등 복수 가능")]
    [SerializeField] private Collider[] patientLeftArmColliders;

    [Tooltip("환자 오른팔에 부착된 충돌체들 - 상완+전완 등 복수 가능")]
    [SerializeField] private Collider[] patientRightArmColliders;

    [Tooltip("현재 활성화된 접촉 감지 부위 (시나리오에서 설정)")]
    [SerializeField] private ContactTarget[] activeContactTargets = new ContactTarget[] { ContactTarget.HeadAndShoulder };

    // 주동수 충돌체 타겟 (진행률 계산에 사용할 손 결정용)
    private ContactTarget primaryTarget = ContactTarget.HeadAndShoulder;

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

    [Tooltip("★ 카메라용 두 번째 환자 모델 Animator (선택)")]
    [SerializeField] private Animator secondaryPatientAnimator;

    [Tooltip("프레임 레이트에 맞춰 애니메이션 동기화")]
    [SerializeField] private bool syncAnimationWithFrame = true;

    [Tooltip("애니메이션 재생 모드")]
    [SerializeField] private AnimationPlayMode animationPlayMode = AnimationPlayMode.SyncWithUser;

    [Header("=== 가이드 손 표시 ===")]
    [SerializeField] private HandTransformMapper leftGuideHand;
    [SerializeField] private HandTransformMapper rightGuideHand;
    [SerializeField] private bool showGuideHands = true;
    [SerializeField] private Color guideHandColor = new Color(0.5f, 1f, 0.4f, 0.5f);  // ★ 연두색

    [Header("=== 가이드 손 접촉 시 투명도 ===")]
    [Tooltip("사용자 손이 환자에 접촉 시 가이드 손 투명도 조절")]
    [SerializeField] private bool fadeOnTouch = true;

    [Tooltip("접촉 시 가이드 손 투명도 (0=완전 투명, 1=불투명)")]
    [Range(0f, 1f)]
    [SerializeField] private float touchAlpha = 0.15f;

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

    [Tooltip("시작 위치 근처에서만 평가 시작")]
    [SerializeField] private bool requireNearStartToBegin = true;

    [Header("=== 상대 이동 감지 설정 ===")]
    [Tooltip("상대 이동 모드 사용 (시작 홀드 위치 기준으로 진행률 계산)")]
    [SerializeField] private bool useRelativeMovement = true;

    [Tooltip("회전 방향 반전 (손목 회전이 반대로 감지될 때 사용)")]
    [SerializeField] private bool invertRotationDirection = false;

    [Tooltip("회전 감지 축 (Y=목회전, Z=측굴, X=굴곡/신전)")]
    [SerializeField] private RotationDetectionAxis rotationDetectionAxis = RotationDetectionAxis.Y;

    [Header("=== ★ 피벗 기반 진행률 설정 (호 움직임용) ===")]
    [Tooltip("피벗 기반 각도 측정 사용 (직선 거리 대신 피벗 중심 각도로 진행률 계산)")]
    [SerializeField] private bool usePivotBasedProgress = true;

    [Tooltip("피벗 포인트 (환자 목/경추 위치) - 회전의 중심점")]
    [SerializeField] private Transform pivotTransform;

    [Tooltip("CSV 데이터에서 목표 각도 자동 계산")]
    [SerializeField] private bool autoCalculateTargetAngle = true;

    [Tooltip("기본 가이드 비율 (0~1, 1.0=전체범위, 0.5=절반)")]
    [Range(0.1f, 1f)]
    [SerializeField] private float defaultGuideRatio = 1f;

    [Tooltip("목표 각도 (도) - 애니메이션의 최대 회전 각도")]
    [SerializeField] private float targetAngle = 90f;

    [Tooltip("데이터에서 계산된 총 각도 (읽기 전용)")]
    [SerializeField] private float calculatedDataAngle = 0f;

    [Tooltip("각도 측정 평면의 법선 축 (측굴=Z, 회전=Y, 굴신=X)")]
    [SerializeField] private RotationDetectionAxis pivotPlaneAxis = RotationDetectionAxis.Z;

    [Tooltip("피벗 각도 반전 (각도가 반대로 측정될 때)")]
    [SerializeField] private bool invertPivotAngle = false;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = true;

    [Header("=== 디버그 UI 표시 ===")]
    [Tooltip("디버그 정보 표시 활성화")]
    [SerializeField] private bool showDebugUI = false;

    [Tooltip("FPS 표시용 TextMeshPro")]
    [SerializeField] private TextMeshProUGUI fpsText;

    [Tooltip("왼손 거리 표시용 TextMeshPro")]
    [SerializeField] private TextMeshProUGUI leftHandDistanceText;

    [Tooltip("오른손 거리 표시용 TextMeshPro")]
    [SerializeField] private TextMeshProUGUI rightHandDistanceText;

    [Tooltip("FPS 업데이트 간격 (초)")]
    [SerializeField] private float fpsUpdateInterval = 0.5f;

    // 디버그 UI용 내부 변수 → CollisionDetectionManager로 이동

    [Header("=== 홀드 시간 설정 ===")]
    [Tooltip("시작 홀드 시간 (초)")]
    [SerializeField] private float startHoldDuration = 3f;

    [Tooltip("중간 홀드 시간 (초)")]
    [SerializeField] private float midHoldDuration = 3f;

    [Header("=== ★ 스트레칭 모드 설정 ===")]
    [Tooltip("가이드 핸드 시작 위치 (0~1)")]
    [SerializeField] private float stretchingStart = 0.30f;

    [Tooltip("가이드 핸드 끝 = 적정범위 끝 (0~1)")]
    [SerializeField] private float stretchingEnd = 0.65f;

    [Tooltip("적정범위 시작 (가이드 범위 내)")]
    [SerializeField] private float stretchingHoldStart = 0.45f;

    // 통합 설정 외부 참조용
    public float StretchingGuideStart => stretchingStart;
    public float StretchingGuideEnd => stretchingEnd;
    public float StretchingHoldStartRatio => stretchingHoldStart;
    public float StretchingHoldEndRatio => stretchingEnd;

    [Header("=== ★ 재평가 모드 설정 ===")]
    [Tooltip("재평가 가이드 끝 (0~1)")]
    [SerializeField] private float guideReEval_End = 0.70f;

    [Tooltip("재평가 적정범위 시작")]
    [SerializeField] private float extendedMidHoldStartRatio = 0.5f;

    [Tooltip("재평가 적정범위 끝")]
    [SerializeField] private float extendedMidHoldEndRatio = 0.7f;

    [Header("=== ★ 제한장벽 확인 모드 설정 ===")]
    [Tooltip("제한장벽 가이드 끝")]
    [SerializeField] private float guideLimitCheck_End = 0.5f;

    [Header("=== 측굴 자동 프리셋 ===")]
    [Tooltip("측굴 운동 자동 감지 및 프리셋 적용")]
    [SerializeField] private bool autoApplyLateralBendingPreset = true;

    // 내부용 (Inspector 숨김)
    private float midHoldStartRatio = 0.3f;
    private float midHoldEndRatio = 0.5f;
    private float guideRotation_Start = 0f;
    private float guideRotation_End = 0.5f;
    private float guideLimitCheck_Start = 0f;
    private float guideReEval_Start = 0f;
    private float lateralBending_LimitCheckRatio = 0.5f;
    private float lateralBending_ReEvalRatio = 0.7f;

    // 스트레칭 가이드 호환용 프로퍼티
    private float guideStretching_Start => stretchingStart;
    private float guideStretching_End => stretchingEnd;

    [Header("=== 손 유사도 체크 ===")]
    [Tooltip("간소화된 손 유사도 체크 사용 (왼손: 손바닥 방향+주먹, 오른손: 경로+손모양)")]
    [SerializeField] private bool useSimplifiedHandComparison = true;

    [Tooltip("오른손 가중치 (0~1, 기본 0.7 = 70%)")]
    [SerializeField] private float rightHandSimilarityWeight = 0.7f;

    [Tooltip("왼손 가중치 (0~1, 기본 0.3 = 30%)")]
    [SerializeField] private float leftHandSimilarityWeight = 0.3f;

    [Tooltip("왼손 이탈 허용 거리 (미터)")]
    [SerializeField] private float leftHandDriftThreshold = 0.15f;

    #endregion

    #region Enums

    /// <summary>
    /// 회전 감지 축 (목 움직임 종류에 따라 선택)
    /// </summary>
    public enum RotationDetectionAxis
    {
        Y,  // 목 회전 (좌우 돌리기) - Vector3.up
        Z,  // 측굴 (좌우 기울이기) - Vector3.forward
        X   // 굴곡/신전 (앞뒤로 숙이기) - Vector3.right
    }

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

    #endregion

    #region State Variables

    // 상태
    private bool isEvaluating = false;
    private float evaluationStartTime;
    private float lastMetricsRecordTime;

    // 새로운 평가 흐름 상태
    private EvaluationPhase currentPhase = EvaluationPhase.Idle;
    private float phaseHoldTime = 0f;
    private Vector3 leftHandStartHoldPosition;  // 시작 홀드 시 왼손 위치 저장

    // 50% 초과 경고 상태
    private bool isOverLimitBarrier = false;

    // 충돌 감지 상태
    private bool isLeftHandTouchingPatient = false;
    private bool isRightHandTouchingPatient = false;

    // 주동수 충돌체에 닿은 손 추적 (진행률 계산용)
    private bool isLeftOnPrimary = false;
    private bool isRightOnPrimary = false;

    // 진행률 계산에 사용 중인 손 고정 (Moving 중 손이 바뀌지 않도록)
    private enum ActiveHandSide { None, Left, Right }
    private ActiveHandSide lockedActiveHand = ActiveHandSide.None;
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
    private bool isLeftHandDominant;           // true=왼손이 주동수 (핸드데이터 이동량 기준)
    private Vector3 userHoldReferencePosition;  // 사용자 시작 홀드 위치 (기준점)
    private Quaternion userHoldReferenceRotation; // 사용자 시작 홀드 회전 (기준점)
    private Vector3 movementAxis;               // 주요 이동 축 (정규화)
    private string specifiedMovementType;       // CSV에서 지정한 이동 타입 (position/rotation)
    private bool startHoldOnly;                 // true면 StartHold만 완료하면 다음으로 (등척성운동용)
    private bool guideOnlyMode;                 // true면 StartHold/MidHold 스킵, 유사도 비평가 (시각 데모 전용)
    private bool skipMidHold;                   // true면 유사도 평가하되 MidHold 스킵, 임계점 통과 시 즉시 완료 (대흉근 등)

    // ★ 피벗 기반 진행률 계산용
    private Vector3 pivotStartDirection;        // 피벗→시작손위치 방향 (정규화)

    // 환자 애니메이션
    private AnimationClip currentAnimationClip;
    private string currentAnimationStateName;

    // ★ AutoPlay 모드: autoPlayHandler가 상태를 관리

    // ★ 스트레칭/재평가 확장 모드
    private bool isExtendedLimitMode = false;   // 확장 제한 모드 활성화 여부 (재평가: 65%)
    private bool isStretchingMode = false;      // 스트레칭 모드 (각도 오프셋 적용)
    private bool isGuideMode = false;           // 가이드 모드 (토글로만 진행)
    // ★ 홀드 범위: 스트레칭 모드는 통합 설정 사용
    private float currentMidHoldStart => isStretchingMode ? stretchingHoldStart :
                                         (isExtendedLimitMode ? extendedMidHoldStartRatio : midHoldStartRatio);
    private float currentMidHoldEnd => isStretchingMode ? stretchingEnd :  // 홀드 끝 = 가이드 끝
                                       (isExtendedLimitMode ? extendedMidHoldEndRatio : midHoldEndRatio);

    // ★ 가이드 핸드 재생 범위 (런타임)
    private float runtimeGuideStartRatio = 0f;
    private float runtimeGuideEndRatio = 0.4f;
    private float currentStartRatio => runtimeGuideStartRatio;
    private float currentEndRatio => runtimeGuideEndRatio;
    private float currentAngleDisplayOffset => isStretchingMode ? stretchingStart : 0f;  // ★ 각도 표시 오프셋 (통합 설정)

    // 결과
    private EvaluationSession currentSession;

    // ★ Helper instances
    private HandCollisionDetector collisionDetector;
    private CollisionDetectionManager collisionDetectionManager;
    private EvaluationScoringEngine scoringEngine;
    private EvaluationModeConfigurator modeConfigurator;
    private AutoPlayHandler autoPlayHandler;
    private GuideHandPlaybackController guidePlaybackController;
    private EvaluationPhaseManager phaseManager;
    private ChunaDataLoader dataLoader;

    #endregion

    #region Internal Accessors (for helpers)

    // AutoPlayHandler needs
    internal string InternalAnimationStateName => currentAnimationStateName;
    internal void FireOnUserFrameChanged(int current, int total, float ratio) => OnUserFrameChanged?.Invoke(current, total, ratio);

    // EvaluationPhaseManager needs
    internal void FireOnPhaseChanged(EvaluationPhase p) => OnPhaseChanged?.Invoke(p);
    internal void FireOnHoldProgressChanged(float current, float required) => OnHoldProgressChanged?.Invoke(current, required);
    internal void FireOnHoldCompleted() => OnHoldCompleted?.Invoke();
    internal void FireOnStartHoldComplete() => OnStartHoldComplete?.Invoke();
    internal void FireOnMidHoldBegin() => OnMidHoldBegin?.Invoke();
    internal void FireOnMidHoldComplete() => OnMidHoldComplete?.Invoke();
    internal void FireOnLimitWarning(float progress) => OnLimitWarning?.Invoke(progress);
    internal void FireOnLeftHandDrifted(float distance) => OnLeftHandDrifted?.Invoke(distance);

    internal void IncrementLeftHandDriftCount()
    {
        if (currentSession != null) currentSession.leftHandDriftCount++;
    }

    internal void IncrementLimitWarningCount()
    {
        if (currentSession != null) currentSession.limitWarningCount++;
    }

    internal int GetLimitWarningCount() => currentSession?.limitWarningCount ?? 0;

    internal void InitializeMovingPhaseFrame(float startRatio, int frameCount)
    {
        int startFrameIdx = Mathf.RoundToInt(startRatio * (frameCount - 1));
        userHandFrameIndex = Mathf.Clamp(startFrameIdx, 0, frameCount - 1);
        userHandFrameRatio = startRatio;

        if (showDebugLogs)
            ChunaLogger.Log($"<color=green>[ChunaPathEvaluator] 프레임 인덱스 초기화 (Moving 단계 시작, 시작비율: {startRatio:P0})</color>");
    }

    internal void OnWaitingForStartComplete()
    {
        // Disable NeckVRController in collision mode
        if (neckController != null)
        {
            neckController.Disable();
            ChunaLogger.Log("<color=yellow>[Collision Mode] NeckVRController 비활성화 - 애니메이션으로 목 제어</color>");
        }
    }

    internal void SaveUserHoldReference()
    {
        // 고정된 손 또는 주동수에 닿은 손 기준으로 기준점 저장
        HandVisual activeHand = null;
        Collider activeCollider = null;
        Transform activeWrist = null;

        if (lockedActiveHand == ActiveHandSide.Left || (isLeftOnPrimary && playerLeftHand != null))
        {
            activeHand = playerLeftHand;
            activeCollider = leftHandCollider;
            activeWrist = leftWristBone;
            lockedActiveHand = ActiveHandSide.Left;
        }
        else if (lockedActiveHand == ActiveHandSide.Right || (isRightOnPrimary && playerRightHand != null))
        {
            activeHand = playerRightHand;
            activeCollider = rightHandCollider;
            activeWrist = rightWristBone;
            lockedActiveHand = ActiveHandSide.Right;
        }
        else if (playerRightHand != null)
        {
            activeHand = playerRightHand;
            activeCollider = rightHandCollider;
            activeWrist = rightWristBone;
            lockedActiveHand = ActiveHandSide.Right;
        }

        if (activeHand == null) return;

        FindWristBones();

        if (activeCollider != null)
        {
            userHoldReferencePosition = activeCollider.bounds.center;
        }
        else
        {
            userHoldReferencePosition = activeHand.transform.position;
        }

        userHoldReferenceRotation = activeWrist != null ? activeWrist.rotation : activeHand.transform.rotation;
        Vector3 euler = userHoldReferenceRotation.eulerAngles;
        string wristInfo = activeWrist != null ? activeWrist.name : "루트";
        string posSource = activeCollider != null ? "콜라이더" : "transform";
        ChunaLogger.Log($"<color=cyan>[StartHold] 기준 저장 - 위치:{userHoldReferencePosition} [{posSource}], 회전:({euler.x:F0},{euler.y:F0},{euler.z:F0}) [{wristInfo}]</color>");

        // Pivot-based progress: save pivot->hand direction
        if (usePivotBasedProgress && pivotTransform != null)
        {
            pivotStartDirection = (userHoldReferencePosition - pivotTransform.position).normalized;
            ChunaLogger.Log($"<color=magenta>[StartHold] 피벗 기준 저장 - 피벗:{pivotTransform.position}, 시작방향:{pivotStartDirection}, 목표각도:{targetAngle}°</color>");
        }
    }

    internal void StartGuideHandPlaybackInternal()
    {
        StartGuideHandPlayback();
    }

    // ChunaDataLoader needs
    internal bool ShowDebugLogs => showDebugLogs;
    internal string CurrentProcedureName { get => currentProcedureName; set => currentProcedureName = value; }
    internal List<PoseFrame> LoadedFrames { get => loadedFrames; set => loadedFrames = value; }
    internal Transform ReferenceTransform => referenceTransform;
    internal Transform PivotTransform => pivotTransform;
    internal HandPoseComparator PoseComparator => poseComparator;
    internal ChunaLimitChecker LimitChecker => limitChecker;
    internal RotationDetectionAxis PivotPlaneAxis => pivotPlaneAxis;
    internal bool AutoCalculateTargetAngle => autoCalculateTargetAngle;
    internal bool AutoApplyLateralBendingPreset => autoApplyLateralBendingPreset;
    internal string SpecifiedMovementType => specifiedMovementType;

    internal Vector3 HandDataMovementVector { get => handDataMovementVector; set => handDataMovementVector = value; }
    internal float HandDataTotalDistance { get => handDataTotalDistance; set => handDataTotalDistance = value; }
    internal float HandDataTotalRotation { get => handDataTotalRotation; set => handDataTotalRotation = value; }
    internal bool IsPositionBasedMovement { get => isPositionBasedMovement; set => isPositionBasedMovement = value; }
    internal bool IsLeftHandDominant { get => isLeftHandDominant; set => isLeftHandDominant = value; }
    internal Vector3 MovementAxis { get => movementAxis; set => movementAxis = value; }
    internal float LeftHandSimilarityWeight { get => leftHandSimilarityWeight; set => leftHandSimilarityWeight = value; }
    internal float RightHandSimilarityWeight { get => rightHandSimilarityWeight; set => rightHandSimilarityWeight = value; }
    internal float CalculatedDataAngle { get => calculatedDataAngle; set => calculatedDataAngle = value; }
    internal float DefaultGuideRatio { get => defaultGuideRatio; set => defaultGuideRatio = value; }
    internal float TargetAngle { get => targetAngle; set => targetAngle = value; }
    internal float RuntimeGuideStartRatio { get => runtimeGuideStartRatio; set => runtimeGuideStartRatio = value; }
    internal float RuntimeGuideEndRatio { get => runtimeGuideEndRatio; set => runtimeGuideEndRatio = value; }
    internal float LateralBending_LimitCheckRatio => lateralBending_LimitCheckRatio;
    internal float LateralBending_ReEvalRatio => lateralBending_ReEvalRatio;
    internal float GuideRotation_Start => guideRotation_Start;
    internal float GuideRotation_End => guideRotation_End;
    internal bool ShowGuideHandsField { get => showGuideHands; set => showGuideHands = value; }
    internal Color GuideHandColor { get => guideHandColor; set => guideHandColor = value; }
    internal float StartHoldDuration { get => startHoldDuration; set => startHoldDuration = value; }
    internal float MidHoldDuration { get => midHoldDuration; set => midHoldDuration = value; }

    // EvaluationModeConfigurator needs (threshold/axis/pivot)
    internal float MidHoldStartRatio { get => midHoldStartRatio; set => midHoldStartRatio = value; }
    internal float MidHoldEndRatio { get => midHoldEndRatio; set => midHoldEndRatio = value; }
    internal float StretchingHoldStartField { get => stretchingHoldStart; set => stretchingHoldStart = value; }
    internal float StretchingEndField { get => stretchingEnd; set => stretchingEnd = value; }
    internal float ExtendedMidHoldStartRatio { get => extendedMidHoldStartRatio; set => extendedMidHoldStartRatio = value; }
    internal float ExtendedMidHoldEndRatio { get => extendedMidHoldEndRatio; set => extendedMidHoldEndRatio = value; }
    internal RotationDetectionAxis RotationDetectionAxisField { get => rotationDetectionAxis; set => rotationDetectionAxis = value; }
    internal RotationDetectionAxis PivotPlaneAxisField { get => pivotPlaneAxis; set => pivotPlaneAxis = value; }
    internal Transform PivotTransformField { get => pivotTransform; set => pivotTransform = value; }
    internal bool InvertRotationDirectionField { get => invertRotationDirection; set => invertRotationDirection = value; }
    internal bool InvertPivotAngleField { get => invertPivotAngle; set => invertPivotAngle = value; }
    internal bool UsePivotBasedProgressField { get => usePivotBasedProgress; set => usePivotBasedProgress = value; }

    // CollisionDetectionManager needs
    internal bool IsLeftHandTouchingPatient => isLeftHandTouchingPatient;
    internal bool IsRightHandTouchingPatient => isRightHandTouchingPatient;
    internal float CurrentAnimationRatio { get => currentAnimationRatio; set => currentAnimationRatio = value; }

    internal void SetLeftHandTouchState(bool touching, bool onPrimary)
    {
        isLeftHandTouchingPatient = touching;
        isLeftOnPrimary = onPrimary;
    }

    internal void SetRightHandTouchState(bool touching, bool onPrimary)
    {
        isRightHandTouchingPatient = touching;
        isRightOnPrimary = onPrimary;
    }

    #endregion

    #region Events

    // 이벤트
    public event Action OnEvaluationStarted;
    public event Action<EvaluationSession> OnEvaluationCompleted;
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
    public event Action<int> OnSubStepStarted;                       // SubStep 시작 (인덱스)

    #endregion

    #region Nested Classes

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

        // 좌/우 개별 유사도
        public float leftAverageSimilarity;
        public float rightAverageSimilarity;

        // 유사도 안정성
        public float similarityStdDev;             // 유사도 표준편차

        // 안전성
        public float peakExceededRatio;            // 최대 초과 비율

        // 최종 점수
        public float finalScore;
        public string grade;
        public string feedback;

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

    #endregion

    #region Unity Lifecycle

    void Awake()
    {
        if (poseComparator == null)
        {
            poseComparator = new HandPoseComparator();
        }

        // ★ 손 유사도 가중치 동기화
        if (poseComparator != null)
        {
            var settings = poseComparator.GetSettings();
            settings.rightHandWeight = rightHandSimilarityWeight;
            settings.leftHandWeight = leftHandSimilarityWeight;
        }

        // Initialize helpers
        collisionDetector = new HandCollisionDetector();
        collisionDetectionManager = new CollisionDetectionManager(this, collisionDetector);
        scoringEngine = new EvaluationScoringEngine();
        modeConfigurator = new EvaluationModeConfigurator(this);
        autoPlayHandler = new AutoPlayHandler(this);
        guidePlaybackController = new GuideHandPlaybackController(this);
        phaseManager = new EvaluationPhaseManager(this);
        dataLoader = new ChunaDataLoader(this);
    }

    void Start()
    {
        FindReferences();
        FindModules();
    }

    void Update()
    {
        // 디버그 UI는 항상 업데이트
        if (showDebugUI)
        {
            UpdateDebugUI();
        }

        if (!isEvaluating) return;

        // ★ AutoPlay 모드: 핸드데이터 없이 애니메이션만 자동 재생 (via helper)
        if (autoPlayHandler.IsAutoPlayMode)
        {
            bool completed = autoPlayHandler.UpdateAutoPlay(patientAnimator, showDebugLogs);
            if (completed)
            {
                HandleAutoPlayComplete();
            }
            return;
        }

        // 손-환자 충돌 체크
        UpdateCollisionDetection();

        // 사용자 손 위치 기반 프레임 업데이트
        UpdateUserHandFrame();

        // 새로운 평가 흐름 업데이트 (via helper)
        {
            Vector3 leftPos = playerLeftHand != null ? playerLeftHand.transform.position : Vector3.zero;
            Vector3 rightPos = playerRightHand != null ? playerRightHand.transform.position : Vector3.zero;

            phaseManager.UpdatePhaseEvaluation(
                leftPos, rightPos,
                isLeftHandTouchingPatient, isRightHandTouchingPatient,
                holdVelocityThreshold,
                startHoldDuration, midHoldDuration,
                currentMidHoldStart, currentMidHoldEnd,
                leftHandDriftThreshold,
                currentStartRatio,
                useRelativeMovement, startHoldOnly, guideOnlyMode, skipMidHold, modeConfigurator.IsGuideMode,
                showDebugLogs);

            currentPhase = phaseManager.CurrentPhase;
        }

        // 애니메이션 선형보간 업데이트
        UpdateAnimationLerp();

        // 메트릭 기록 (Moving/MidHold 단계에서만, guideOnly 제외)
        if (!guideOnlyMode && (currentPhase == EvaluationPhase.Moving || currentPhase == EvaluationPhase.MidHold))
        {
            float currentTime = Time.time;
            if (currentTime - lastMetricsRecordTime >= metricsRecordInterval)
            {
                lastMetricsRecordTime = currentTime;
                RecordMetricsSnapshot();
            }
        }
    }

    #endregion

    #region Phase-Based Evaluation

    // Phase-based evaluation is now delegated to EvaluationPhaseManager helper.
    // The phase manager calls back through internal accessors.

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
    /// 로드된 프레임 수 (핸드 데이터 유무 확인용)
    /// </summary>
    public int GetLoadedFrameCount()
    {
        return loadedFrames != null ? loadedFrames.Count : 0;
    }

    /// <summary>
    /// 현재 시술 이름 반환
    /// </summary>
    public string GetCurrentProcedureName()
    {
        return currentProcedureName;
    }

    /// <summary>
    /// 데이터에서 계산된 총 각도 반환
    /// </summary>
    public float GetCalculatedDataAngle()
    {
        return calculatedDataAngle;
    }

    /// <summary>
    /// 현재 목표 각도 반환
    /// </summary>
    public float GetTargetAngle()
    {
        return targetAngle;
    }

    /// <summary>
    /// 현재 가이드 프레임 인덱스 가져오기
    /// </summary>
    public int GetCurrentGuideFrameIndex()
    {
        return guidePlaybackController != null ? guidePlaybackController.CurrentGuideFrameIndex : currentGuideFrameIndex;
    }

    /// <summary>
    /// 현재 평가 단계
    /// </summary>
    public EvaluationPhase CurrentPhase => phaseManager != null ? phaseManager.CurrentPhase : currentPhase;

    #endregion

    #region User Hand Frame Tracking

    /// <summary>
    /// 사용자 손 움직임 기반으로 진행률 계산
    /// ★ 상대 이동 모드: 시작 홀드 위치 기준으로 이동량/회전량에 따라 진행률 계산
    /// </summary>
    private void UpdateUserHandFrame()
    {
        if (loadedFrames == null || loadedFrames.Count == 0) return;

        // Moving 또는 MidHold 단계에서만 프레임 업데이트
        if (currentPhase != EvaluationPhase.Moving && currentPhase != EvaluationPhase.MidHold)
        {
            userHandFrameIndex = 0;
            userHandFrameRatio = 0f;
            return;
        }

        // ★ 주동수 충돌체에 닿은 손으로 진행률 계산 (한번 결정되면 고정)
        // 고정된 손이 있으면 유지, 없으면 새로 결정
        if (lockedActiveHand == ActiveHandSide.Left && playerLeftHand != null && isLeftOnPrimary)
        {
            // 유지
        }
        else if (lockedActiveHand == ActiveHandSide.Right && playerRightHand != null && isRightOnPrimary)
        {
            // 유지
        }
        else if (isLeftOnPrimary && playerLeftHand != null)
        {
            lockedActiveHand = ActiveHandSide.Left;
        }
        else if (isRightOnPrimary && playerRightHand != null)
        {
            lockedActiveHand = ActiveHandSide.Right;
        }
        else if (lockedActiveHand != ActiveHandSide.None)
        {
            // 고정된 손이 주동수에서 떨어졌으면 해제
            lockedActiveHand = ActiveHandSide.None;
        }

        HandVisual activeHand = null;
        Collider activeCollider = null;
        Transform activeWristBone = null;

        if (lockedActiveHand == ActiveHandSide.Left)
        {
            activeHand = playerLeftHand;
            activeCollider = leftHandCollider;
            activeWristBone = leftWristBone;
        }
        else if (lockedActiveHand == ActiveHandSide.Right)
        {
            activeHand = playerRightHand;
            activeCollider = rightHandCollider;
            activeWristBone = rightWristBone;
        }
        else
        {
            // fallback: 주동수에 안 닿았으면 진행률 갱신 안 함
            return;
        }

        // ★ 손 위치: 콜라이더가 있으면 콜라이더 중심, 없으면 transform 위치 사용
        Vector3 rightHandPos;
        if (activeCollider != null)
        {
            rightHandPos = activeCollider.bounds.center;
        }
        else
        {
            rightHandPos = activeHand.transform.position;
        }

        // 회전은 손목 본에서 가져옴 (더 정확한 손목 회전 감지)
        Quaternion rightHandRot = activeWristBone != null ? activeWristBone.rotation : activeHand.transform.rotation;

        float prevRatio = userHandFrameRatio;
        float newRatio = 0f;

        // ★ 상대 이동 모드: 시작 홀드 위치 기준으로 진행률 계산
        if (useRelativeMovement)
        {
            // ★★★ 피벗 기반 모드: 위치/회전 구분 없이 피벗 각도 사용 (측굴, 회전 동작 모두) ★★★
            if (usePivotBasedProgress && pivotTransform != null && pivotStartDirection != Vector3.zero)
            {
                // 피벗에서 현재 손 위치로의 방향
                Vector3 pivotCurrentDirection = (rightHandPos - pivotTransform.position).normalized;

                // 각도 측정 평면의 법선 축 결정
                Vector3 planeNormal = GetPivotPlaneNormal();

                // 시작 방향과 현재 방향 사이의 부호 있는 각도
                float signedAngle = Vector3.SignedAngle(pivotStartDirection, pivotCurrentDirection, planeNormal);

                // 각도 반전 옵션
                if (invertPivotAngle)
                {
                    signedAngle = -signedAngle;
                }

                // 반대 방향(음수)이면 0으로 처리
                float effectiveAngle = Mathf.Max(0f, signedAngle);

                // 목표 각도 대비 진행률 계산
                newRatio = Mathf.Clamp01(effectiveAngle / targetAngle);

                if (showDebugLogs && Time.frameCount % 30 == 0)
                {
                    string posSource = rightHandCollider != null ? "[콜라이더]" : "[transform]";
                    string dirInfo = signedAngle < 0 ? "(반대방향-무시)" : "";
                    string moveType = isPositionBasedMovement ? "위치기반" : "회전기반";
                    ChunaLogger.Log($"<color=magenta>[Pivot Angle - {moveType}] 각도:{effectiveAngle:F1}° / 목표:{targetAngle:F0}° = {newRatio:P0} {dirInfo}</color>");
                    ChunaLogger.Log($"<color=cyan>  피벗:{pivotTransform.position}, 현재:{rightHandPos} {posSource}, signed:{signedAngle:F1}°</color>");
                }
            }
            // 기존 방식: 위치 기반 (직선 거리)
            else if (isPositionBasedMovement)
            {
                // 위치 기반: 기준 위치에서 얼마나 이동했는지 계산
                Vector3 displacement = rightHandPos - userHoldReferencePosition;

                // ★ 축 방향으로 프로젝션 (부호 있음 - 반대 방향은 음수)
                float projectedDistance = Vector3.Dot(displacement, movementAxis);

                // ★ 반대 방향(음수)이면 0으로 처리 - 뒤로 가면 목이 안 돌아감
                float effectiveDistance = Mathf.Max(0f, projectedDistance);

                // ★ 핸드데이터 이동 거리가 너무 작으면 기본값 사용 (5cm)
                float targetDistance = Mathf.Max(handDataTotalDistance, 0.05f);

                // 핸드데이터 총 이동 거리로 나눠서 0~1 비율 계산
                newRatio = Mathf.Clamp01(effectiveDistance / targetDistance);

                if (showDebugLogs && Time.frameCount % 30 == 0)
                {
                    string posSource = rightHandCollider != null ? "[콜라이더]" : "[transform]";
                    string dirInfo = projectedDistance < 0 ? "(반대방향-무시)" : "";
                    ChunaLogger.Log($"<color=yellow>[Position Move] 이동:{effectiveDistance:F3}m / 목표:{targetDistance:F3}m = {newRatio:P0} {dirInfo}</color>");
                    ChunaLogger.Log($"<color=cyan>  기준:{userHoldReferencePosition}, 현재:{rightHandPos} {posSource}, 축방향:{projectedDistance:F3}m</color>");
                }
            }
            // 기존 방식: 회전 기반 (손목 회전)
            else
            {
                // 회전 기반: 기준 회전에서 얼마나 회전했는지 계산
                // ★ 선택된 축에 따라 회전 감지 방향 결정
                Vector3 detectionAxis = GetRotationDetectionAxis();
                Vector3 refForward = userHoldReferenceRotation * Vector3.forward;
                Vector3 curForward = rightHandRot * Vector3.forward;
                float signedAngle = Vector3.SignedAngle(refForward, curForward, detectionAxis);

                // ★ 회전 방향 반전 옵션
                if (invertRotationDirection)
                {
                    signedAngle = -signedAngle;
                }

                // ★ 반대 방향(음수)이면 0으로 처리
                float effectiveAngle = Mathf.Max(0f, signedAngle);

                // 핸드데이터 총 회전 각도로 나눠서 0~1 비율 계산
                if (handDataTotalRotation > 1f)
                {
                    newRatio = Mathf.Clamp01(effectiveAngle / handDataTotalRotation);
                }

                if (showDebugLogs && Time.frameCount % 30 == 0)
                {
                    Vector3 refEuler = userHoldReferenceRotation.eulerAngles;
                    Vector3 curEuler = rightHandRot.eulerAngles;
                    string dirInfo = signedAngle < 0 ? "(반대방향-무시)" : "";
                    ChunaLogger.Log($"<color=yellow>[Relative Rotate] 회전:{effectiveAngle:F1}° / {handDataTotalRotation:F1}° = {newRatio:P0} {dirInfo}</color>");
                    ChunaLogger.Log($"<color=cyan>  기준:({refEuler.x:F0},{refEuler.y:F0},{refEuler.z:F0}) → 현재:({curEuler.x:F0},{curEuler.y:F0},{curEuler.z:F0}), signed:{signedAngle:F1}°</color>");
                }
            }
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
                ChunaLogger.Log($"<color=green>[Progress] {userHandFrameRatio:P0} (프레임:{userHandFrameIndex}/{loadedFrames.Count})</color>");
            }
        }

        // ★ 매 프레임마다 환자 애니메이션 실시간 동기화
        if (syncAnimationWithFrame && animationPlayMode == AnimationPlayMode.SyncWithUser)
        {
            SyncAnimationToFrame(userHandFrameRatio);
        }
    }

    /// <summary>
    /// 애니메이션을 프레임 비율에 맞춰 동기화
    /// </summary>
    private void SyncAnimationToFrame(float ratio)
    {
        if (patientAnimator == null || string.IsNullOrEmpty(currentAnimationStateName))
            return;

        // 선형보간 사용 (UpdateAnimationLerp에서 처리)
        targetAnimationRatio = Mathf.Clamp01(ratio);
    }

    #endregion

    #region Collision Detection (delegated to CollisionDetectionManager)

    /// <summary>
    /// Build the context struct with current frame's SerializeField values.
    /// </summary>
    private CollisionDetectionManager.CollisionUpdateContext BuildCollisionContext()
    {
        return new CollisionDetectionManager.CollisionUpdateContext
        {
            leftHandTransform = playerLeftHand != null ? playerLeftHand.transform : null,
            rightHandTransform = playerRightHand != null ? playerRightHand.transform : null,
            leftHandCollider = leftHandCollider,
            rightHandCollider = rightHandCollider,
            patientHeadCollider = patientHeadCollider,
            patientShoulderCollider = patientShoulderCollider,
            patientChestCollider = patientChestCollider,
            patientLeftArmColliders = patientLeftArmColliders,
            patientRightArmColliders = patientRightArmColliders,
            activeContactTargets = activeContactTargets,
            primaryTarget = primaryTarget,
            handCollisionShape = handCollisionShape,
            handColliderScale = handColliderScale,
            defaultHandCollisionRadius = defaultHandCollisionRadius,
            handCollisionForwardOffset = handCollisionForwardOffset,
            palmWidth = palmWidth,
            palmThickness = palmThickness,
            palmHeight = palmHeight,
            fingerLength = fingerLength,
            fadeOnTouch = fadeOnTouch,
            touchAlpha = touchAlpha,
            guideHandColor = guideHandColor,
            leftGuideHand = leftGuideHand,
            rightGuideHand = rightGuideHand,
            showDebugUI = showDebugUI,
            showDebugLogs = showDebugLogs,
            fpsText = fpsText,
            leftHandDistanceText = leftHandDistanceText,
            rightHandDistanceText = rightHandDistanceText,
            fpsUpdateInterval = fpsUpdateInterval,
            leftWristBone = leftWristBone,
            rightWristBone = rightWristBone,
            patientAnimator = patientAnimator,
            secondaryPatientAnimator = secondaryPatientAnimator,
            currentAnimationStateName = currentAnimationStateName,
            targetAnimationRatio = targetAnimationRatio,
            animationLerpSpeed = animationLerpSpeed,
            currentPhase = currentPhase,
            userHandFrameRatio = userHandFrameRatio,
        };
    }

    private void UpdateCollisionDetection()
    {
        var ctx = BuildCollisionContext();
        collisionDetectionManager.UpdateCollisionDetection(in ctx);
    }

    private void UpdateDebugUI()
    {
        var ctx = BuildCollisionContext();
        collisionDetectionManager.UpdateDebugUI(in ctx);
    }

    private void UpdateAnimationLerp()
    {
        var ctx = BuildCollisionContext();
        collisionDetectionManager.UpdateAnimationLerp(in ctx);
    }

    #endregion

    #region AutoPlay Mode

    // AutoPlay logic is delegated to AutoPlayHandler helper.
    // Update() calls autoPlayHandler.UpdateAutoPlay() and handles completion via HandleAutoPlayComplete().

    /// <summary>
    /// AutoPlay 완료 이벤트
    /// </summary>
    public event Action OnAutoPlayCompleted;

    /// <summary>
    /// Handle auto play completion (called from Update when helper reports completion).
    /// </summary>
    private void HandleAutoPlayComplete()
    {
        autoPlayHandler.CompleteAutoPlay();

        // Phase change
        phaseManager.ChangePhase(EvaluationPhase.Completed, GetLoadedFrameCount(), currentStartRatio, showDebugLogs);
        currentPhase = phaseManager.CurrentPhase;

        if (modeConfigurator.IsGuideMode)
        {
            ChunaLogger.Log("<color=magenta>[AutoPlay] 가이드 모드 - 토글 버튼으로 진행하세요</color>");
            OnAutoPlayCompleted?.Invoke();
            return;
        }

        CompleteEvaluation();
        OnAutoPlayCompleted?.Invoke();
    }

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
        // ★ 애니메이션 이름 공백 제거 (CSV 파싱 시 공백 문제 방지)
        string trimmedName = animationStateName?.Trim();

        animationPlayMode = playMode;

        ChunaLogger.Log($"<color=magenta>[Animation] ★ 애니메이션 설정 시도: '{trimmedName}' (모드:{playMode})</color>");

        if (patientAnimator == null)
        {
            // Patient 태그로 Animator 찾기
            var patient = GameObject.FindGameObjectWithTag("Patient");
            if (patient != null)
            {
                patientAnimator = patient.GetComponent<Animator>();
                ChunaLogger.Log($"<color=cyan>[Animation] Patient 태그로 Animator 찾음: {patient.name}</color>");
            }
            else
            {
                ChunaLogger.LogError("<color=red>[Animation] Patient 태그 오브젝트를 찾을 수 없음!</color>");
            }
        }

        if (patientAnimator == null)
        {
            ChunaLogger.LogError("<color=red>[Animation] patientAnimator가 NULL - 애니메이션 재생 불가!</color>");
            currentAnimationStateName = null;  // ★ 실패 시 null로 설정
            return;
        }

        if (string.IsNullOrEmpty(trimmedName))
        {
            ChunaLogger.LogWarning("<color=orange>[Animation] 애니메이션 이름이 비어있음</color>");
            currentAnimationStateName = null;  // ★ 실패 시 null로 설정
            return;
        }

        // Animator에 해당 상태가 있는지 확인
        int stateHash = Animator.StringToHash(trimmedName);
        bool hasState = patientAnimator.HasState(0, stateHash);
        ChunaLogger.Log($"<color=cyan>[Animation] Animator '{patientAnimator.name}'에 상태 '{trimmedName}' (해시:{stateHash}) 존재: {hasState}</color>");

        if (!hasState)
        {
            ChunaLogger.LogError($"<color=red>[Animation] ★★★ 경고: Animator에 '{trimmedName}' 상태가 없습니다! 애니메이션 재생 불가!</color>");
            currentAnimationStateName = null;  // ★ 상태 없으면 null로 설정 (UpdateAutoPlay에서 즉시 완료 방지)
            // 사용 가능한 상태 목록 출력 시도
            var controller = patientAnimator.runtimeAnimatorController;
            if (controller != null)
            {
                ChunaLogger.Log($"<color=yellow>[Animation] Animator Controller: {controller.name}</color>");
                ChunaLogger.Log($"<color=yellow>[Animation] 클립 수: {controller.animationClips?.Length ?? 0}</color>");
                if (controller.animationClips != null)
                {
                    foreach (var clip in controller.animationClips)
                    {
                        ChunaLogger.Log($"<color=yellow>  - 클립: '{clip.name}'</color>");
                    }
                }
            }
            return;
        }

        // ★ 상태 확인 후에만 이름 설정 (실패 시 null 유지)
        currentAnimationStateName = trimmedName;

        if (playMode == AnimationPlayMode.AutoPlay)
        {
            // 자동 재생 모드
            patientAnimator.Play(trimmedName, 0, 0f);
            patientAnimator.speed = 1f;

            // ★ 두 번째 환자 모델도 동기화
            if (secondaryPatientAnimator != null)
            {
                secondaryPatientAnimator.Play(trimmedName, 0, 0f);
                secondaryPatientAnimator.speed = 1f;
            }
            ChunaLogger.Log($"<color=green>[Animation] ★ 자동 재생 시작: '{trimmedName}' (speed=1)</color>");
        }
        else if (playMode == AnimationPlayMode.SyncWithUser)
        {
            // 사용자 동기화 모드 - 시작 위치로 설정
            patientAnimator.Play(trimmedName, 0, 0f);
            patientAnimator.speed = 0f;

            // ★ 두 번째 환자 모델도 동기화
            if (secondaryPatientAnimator != null)
            {
                secondaryPatientAnimator.Play(trimmedName, 0, 0f);
                secondaryPatientAnimator.speed = 0f;
            }
            ChunaLogger.Log($"<color=green>[Animation] 동기화 모드 시작: '{trimmedName}' (첫 프레임, speed=0)</color>");
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
            ChunaLogger.Log($"<color=magenta>[ChunaPathEvaluator] 이동 타입 지정: '{specifiedMovementType}'</color>");
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

            // 등척성운동: duration 값을 홀드 시간으로 사용
            if (subStep.duration > 0)
            {
                startHoldDuration = subStep.duration;
                ChunaLogger.Log($"<color=yellow>[ChunaPathEvaluator] StartHold 전용 모드 활성화 (등척성 운동) - 홀드 시간: {startHoldDuration}초</color>");
            }
            else
            {
                ChunaLogger.Log($"<color=yellow>[ChunaPathEvaluator] StartHold 전용 모드 활성화 (등척성 운동) - 기본 홀드 시간: {startHoldDuration}초</color>");
            }
        }
        else
        {
            startHoldOnly = false;
            // ★ 등척성 운동이 아니면 기본값(3초)으로 초기화
            startHoldDuration = 3f;
            ChunaLogger.Log($"<color=cyan>[ChunaPathEvaluator] 일반 모드 - startHoldDuration 기본값 복원: {startHoldDuration}초</color>");
        }

        // ★ GuideOnly 모드 (시각 데모 전용 - StartHold/MidHold 스킵, 유사도 비평가)
        // conditionParams에 "guideOnly" 포함 시 활성화
        bool hasGuideOnlyParam = subStep != null && !string.IsNullOrEmpty(subStep.conditionParams) &&
            subStep.conditionParams.ToLower().Contains("guideonly");
        guideOnlyMode = hasGuideOnlyParam;
        if (guideOnlyMode)
        {
            // ★ GuideOnly: 가이드 핸드가 전체 프레임을 재생하도록 범위 설정
            runtimeGuideStartRatio = 0f;
            runtimeGuideEndRatio = 1.0f;
            ChunaLogger.Log($"<color=yellow>[ChunaPathEvaluator] GuideOnly 모드 활성화 - 가이드 재생 범위 0~100%, 진행률 95% 완료</color>");
        }

        // ★ SkipMidHold 모드 (유사도 평가 O, MidHold 스킵 - 임계점 통과 시 즉시 완료)
        // conditionParams에 "skipMidHold" 포함 시 활성화 (대흉근, 흉쇄유돌근 등 직선 가동범위)
        bool hasSkipMidHoldParam = subStep != null && !string.IsNullOrEmpty(subStep.conditionParams) &&
            subStep.conditionParams.ToLower().Contains("skipmidhold");
        skipMidHold = hasSkipMidHoldParam;
        if (skipMidHold)
        {
            ChunaLogger.Log($"<color=yellow>[ChunaPathEvaluator] SkipMidHold 모드 활성화 - 유사도 평가 + 임계점 통과 완료 (MidHold 스킵)</color>");
        }

        // 시나리오 데이터에 애니메이션 클립이 있을 때만 설정
        if (subStep != null && subStep.HasPatientAnimation())
        {
            string animStateName = subStep.patientAnimationClip;
            AnimationPlayMode mode = subStep.GetAnimationPlayMode();
            SetPatientAnimation(animStateName, mode);
            ChunaLogger.Log($"<color=cyan>[ChunaPathEvaluator] 환자 애니메이션 로드: {animStateName}</color>");
        }
        else
        {
            // 비어있으면 애니메이션 없이 진행
            currentAnimationStateName = null;
            if (showDebugLogs)
                ChunaLogger.Log("<color=yellow>[ChunaPathEvaluator] 애니메이션 클립 없음 - 애니메이션 없이 진행</color>");
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
        // ★ 두 번째 환자 모델도 정지
        if (secondaryPatientAnimator != null)
        {
            secondaryPatientAnimator.speed = 0f;
        }
        currentAnimationStateName = null;
    }

    /// <summary>
    /// ★ AutoPlay 모드 시작 - 핸드데이터 없이 애니메이션만 자동 재생
    /// </summary>
    /// <param name="duration">자동 재생 시간 (초). 0이면 애니메이션 완료 시 자동 진행</param>
    public void StartAutoPlay(float duration = 0f)
    {
        autoPlayHandler.StartAutoPlay(duration);

        // 평가 시작 처리
        isEvaluating = true;
        evaluationStartTime = Time.time;
        phaseManager.ChangePhase(EvaluationPhase.Moving, GetLoadedFrameCount(), currentStartRatio, showDebugLogs);
        currentPhase = phaseManager.CurrentPhase;
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
    public bool IsAutoPlayMode => autoPlayHandler.IsAutoPlayMode;

    /// <summary>
    /// AutoPlay 진행률 (0~1)
    /// </summary>
    public float AutoPlayProgress => autoPlayHandler.AutoPlayProgress;

    #endregion

    #region Extended Limit Mode (Stretching/Re-evaluation)

    /// <summary>
    /// 현재 확장 제한 모드인지 확인
    /// </summary>
    public bool IsExtendedLimitMode => isExtendedLimitMode;

    /// <summary>
    /// 현재 스트레칭 모드인지 확인 (30%부터 시작)
    /// </summary>
    public bool IsStretchingMode => isStretchingMode;

    /// <summary>
    /// 현재 가이드 모드인지 확인 (토글로만 진행)
    /// </summary>
    public bool IsGuideMode => modeConfigurator != null ? modeConfigurator.IsGuideMode : isGuideMode;

    /// <summary>
    /// 현재 GuideOnly 모드인지 확인 (시각 데모 전용)
    /// </summary>
    public bool IsGuideOnlyMode => guideOnlyMode;

    /// <summary>
    /// 현재 시작 비율 반환 (애니메이션 시작 위치, 항상 0)
    /// </summary>
    public float CurrentStartRatio => currentStartRatio;

    /// <summary>
    /// 현재 각도 표시 오프셋 비율 반환 (스트레칭 모드면 0.3, 아니면 0)
    /// AngleDisplayController에서 사용
    /// </summary>
    public float CurrentAngleDisplayOffset => currentAngleDisplayOffset;

    /// <summary>
    /// 현재 홀드 시작 비율 반환 (스트레칭/재평가/일반 모드에 따라)
    /// </summary>
    public float CurrentMidHoldStart => currentMidHoldStart;

    /// <summary>
    /// 현재 홀드 종료 비율 반환 (스트레칭/재평가/일반 모드에 따라)
    /// </summary>
    public float CurrentMidHoldEnd => currentMidHoldEnd;

    /// <summary>
    /// 현재 제한 비율 반환 (확장 모드 여부에 따라)
    /// </summary>
    public float CurrentLimitRatio => currentMidHoldEnd;

    /// <summary>
    /// Step 이름 및 핸드데이터 이름에 따라 확장 제한 모드 및 회전 방향 자동 설정
    /// stepName: 스트레칭/재평가 모드 판단 (가이드/진단/제한장벽확인/등척성운동/스트레칭/재평가)
    /// handDataName: 환측/건측 회전 방향 판단
    /// </summary>
    public void SetExtendedLimitModeFromNames(string stepName, string handDataName)
    {
        modeConfigurator.SetFromNames(stepName, handDataName);

        // Sync local state from configurator
        isExtendedLimitMode = modeConfigurator.IsExtendedLimitMode;
        isStretchingMode = modeConfigurator.IsStretchingMode;
        isGuideMode = modeConfigurator.IsGuideMode;
    }

    /// <summary>
    /// ScenarioConfig에서 평가 임계점 오버라이드 적용
    /// </summary>
    public void ApplyEvaluationThresholds(ScenarioConfig config) => modeConfigurator.ApplyEvaluationThresholds(config);

    /// <summary>
    /// Phase별 회전 임계점 오버라이드 적용
    /// </summary>
    public void ApplyPhaseRotationThresholds(ScenarioConfig.PhaseThresholdOverride phaseOverride) => modeConfigurator.ApplyPhaseRotationThresholds(phaseOverride);

    /// <summary>
    /// 일반 모드 임계점을 기본값으로 복원
    /// </summary>
    public void RestoreDefaultThresholds(ScenarioConfig config) => modeConfigurator.RestoreDefaultThresholds(config);

    /// <summary>
    /// 회전 방향 반전 설정 (수동)
    /// </summary>
    public void SetInvertRotationDirection(bool invert) => modeConfigurator.SetInvertRotationDirection(invert);

    /// <summary>
    /// 현재 회전 방향 반전 여부
    /// </summary>
    public bool InvertRotationDirection => invertRotationDirection;

    /// <summary>
    /// 회전 감지 축 Vector3 반환
    /// </summary>
    private Vector3 GetRotationDetectionAxis() => modeConfigurator.GetRotationDetectionAxis();

    /// <summary>
    /// ★ 피벗 각도 측정 평면의 법선 축 반환 (delegates to ChunaDataLoader static)
    /// </summary>
    private Vector3 GetPivotPlaneNormal()
    {
        return ChunaDataLoader.GetPivotPlaneNormal(pivotPlaneAxis);
    }

    /// <summary>
    /// 회전 감지 축 설정
    /// </summary>
    public void SetRotationDetectionAxis(RotationDetectionAxis axis) => modeConfigurator.SetRotationDetectionAxis(axis);

    /// <summary>
    /// 현재 회전 감지 축
    /// </summary>
    public RotationDetectionAxis CurrentRotationAxis => rotationDetectionAxis;

    /// <summary>
    /// ★ 피벗 기반 진행률 설정
    /// </summary>
    public void SetPivotSettings(Transform pivot, float angle, RotationDetectionAxis planeAxis, bool invert = false) => modeConfigurator.SetPivotSettings(pivot, angle, planeAxis, invert);

    /// <summary>
    /// 피벗 기반 진행률 활성화 여부
    /// </summary>
    public bool UsePivotBasedProgress
    {
        get => usePivotBasedProgress;
        set => usePivotBasedProgress = value;
    }

    #endregion

    #region References Initialization

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
                    ChunaLogger.Log($"[ChunaPathEvaluator] 환자 Transform 자동 연결: {patient.name}");
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
                ChunaLogger.Log($"<color=cyan>[ChunaPathEvaluator] 왼손 손목 본 연결: {leftWristBone.name}</color>");
        }

        if (playerRightHand != null && rightWristBone == null)
        {
            rightWristBone = FindBoneInHierarchy(playerRightHand.transform, "Wrist", "Palm");
            if (rightWristBone != null && showDebugLogs)
                ChunaLogger.Log($"<color=cyan>[ChunaPathEvaluator] 오른손 손목 본 연결: {rightWristBone.name}</color>");
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
            limitChecker = FindFirstObjectByType<ChunaLimitChecker>();

        if (playerLeftHand == null || playerRightHand == null)
        {
            var hands = FindObjectsByType<HandVisual>(FindObjectsSortMode.None);
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
            neckController = FindFirstObjectByType<NeckVRControllerOptimized>();
    }

    #endregion

    #region CSV Data and Checkpoint Generation

    public void LoadAndGenerateCheckpoints(string csvFileName) => dataLoader.LoadAndGenerateCheckpoints(csvFileName);

    /// <summary>
    /// 측굴 모드 수동 전환 (제한장벽 확인 / 스트레칭 / 재평가)
    /// </summary>
    public enum LateralBendingMode { LimitCheck, Stretching, ReEvaluation }

    public void SetLateralBendingMode(LateralBendingMode mode)
    {
        modeConfigurator.SetLateralBendingMode(
            mode,
            lateralBending_LimitCheckRatio, lateralBending_ReEvalRatio,
            stretchingStart, stretchingEnd, stretchingHoldStart,
            guideLimitCheck_Start, guideLimitCheck_End,
            guideReEval_Start, guideReEval_End,
            calculatedDataAngle,
            out float newDefaultGuideRatio,
            out float newRuntimeGuideStartRatio,
            out float newRuntimeGuideEndRatio);

        // Apply results to local state
        defaultGuideRatio = newDefaultGuideRatio;
        runtimeGuideStartRatio = newRuntimeGuideStartRatio;
        runtimeGuideEndRatio = newRuntimeGuideEndRatio;
        isStretchingMode = modeConfigurator.IsStretchingMode;
        isExtendedLimitMode = modeConfigurator.IsExtendedLimitMode;

        // 리밋 체커를 새 적정범위 끝에 맞춰 갱신
        dataLoader.UpdateLimitCheckerFromGuideEnd();

        // targetAngle 재계산
        if (calculatedDataAngle > 0.1f)
        {
            targetAngle = calculatedDataAngle * defaultGuideRatio;
            ChunaLogger.Log($"<color=cyan>  목표 각도: {targetAngle:F1}°</color>");
        }
    }

    #endregion

    #region Evaluation Control

    /// <summary>
    /// 평가 시작
    /// </summary>
    public void StartEvaluation()
    {
        // ★ 이전 가이드 핸드 재생 중지 + 프레임 인덱스 리셋 (잔류값 방지)
        StopGuideHandPlayback();
        guidePlaybackController.ResetFrameIndex();

        // ★ AutoPlay 모드 리셋 (이전 SubStep에서 남아있을 수 있음)
        autoPlayHandler.Reset();

        // ★ 스트레칭/재평가 모드에 따라 가이드 범위 설정 (운동 종류 무관)
        if (isStretchingMode)
        {
            runtimeGuideStartRatio = stretchingStart;
            runtimeGuideEndRatio = stretchingEnd;
            ChunaLogger.Log($"<color=yellow>[StartEval] 스트레칭 모드 - 가이드 범위: {stretchingStart:P0}~{stretchingEnd:P0}</color>");
        }
        else if (isExtendedLimitMode)
        {
            runtimeGuideStartRatio = 0f;
            runtimeGuideEndRatio = extendedMidHoldEndRatio;
            ChunaLogger.Log($"<color=yellow>[StartEval] 재평가 모드 - 가이드 범위: 0~{extendedMidHoldEndRatio:P0}</color>");
        }

        // ★ 난이도 프리셋에서 가이드 핸드/투명도 동기화
        dataLoader.SyncWithDifficultySettings();

        isEvaluating = true;
        evaluationStartTime = Time.time;
        lastMetricsRecordTime = Time.time;
        scoringEngine.Reset();

        // 새로운 평가 흐름 초기화 (via phaseManager)
        Vector3 initLeftPos = playerLeftHand != null ? playerLeftHand.transform.position : Vector3.zero;
        Vector3 initRightPos = playerRightHand != null ? playerRightHand.transform.position : Vector3.zero;
        phaseManager.Initialize(initLeftPos, initRightPos);
        currentPhase = EvaluationPhase.WaitingForStart;
        phaseHoldTime = 0f;
        leftHandStartHoldPosition = Vector3.zero;
        isOverLimitBarrier = false;  // 50% 초과 경고 상태 초기화

        // ★ 충돌 감지 플래그 리셋 (이전 SubStep에서 남아있을 수 있음)
        isLeftHandTouchingPatient = false;
        isRightHandTouchingPatient = false;
        isLeftOnPrimary = false;
        isRightOnPrimary = false;
        lockedActiveHand = ActiveHandSide.None;

        // ★ 홀드 UI 리셋 이벤트 발생 (이전 SubStep의 타이머 표시 제거)
        OnHoldProgressChanged?.Invoke(0f, startHoldDuration);

        // 프레임/애니메이션 상태 초기화
        // ★ 스트레칭/재평가 모드에서는 30%부터 시작
        userHandFrameIndex = 0;
        userHandFrameRatio = currentStartRatio;
        currentAnimationRatio = currentStartRatio;
        targetAnimationRatio = currentStartRatio;

        if (showDebugLogs)
            ChunaLogger.Log("<color=green>[ChunaPathEvaluator] 평가 시작 - 시작 위치 대기 중...</color>");

        // ★ SubStep 시작 이벤트 발생 (녹화 등에서 사용)
        OnSubStepStarted?.Invoke(0);

        // 시작 위치 대기 중 시작 프레임 표시 (가이드 핸드 + 환자 애니메이션)
        // ★ 스트레칭/재평가 모드에서는 30% 프레임부터 시작
        if (showFirstFrameWhileWaiting)
        {
            // ★ 가이드 핸드 표시 전에 충돌 검사 먼저 수행 (접촉 시 투명도 반영)
            UpdateCollisionDetection();

            ShowGuideHandFirstFrame();

            // 환자 애니메이션도 시작 프레임으로 설정 (스트레칭은 30%, 일반은 0%)
            if (patientAnimator != null && !string.IsNullOrEmpty(currentAnimationStateName))
            {
                patientAnimator.Play(currentAnimationStateName, 0, currentStartRatio);
                patientAnimator.speed = 0f;

                // ★ 두 번째 환자 모델도 동기화
                if (secondaryPatientAnimator != null)
                {
                    secondaryPatientAnimator.Play(currentAnimationStateName, 0, currentStartRatio);
                    secondaryPatientAnimator.speed = 0f;
                }

                if (showDebugLogs)
                    ChunaLogger.Log($"<color=magenta>[ChunaPathEvaluator] 환자 애니메이션 시작 프레임 설정: {currentAnimationStateName} @ {currentStartRatio:P0}</color>");
            }
        }

        // 세션 초기화
        currentSession = new EvaluationSession
        {
            procedureName = currentProcedureName,
            startTime = DateTime.Now,
            metricsHistory = new List<EvaluationSession.MetricsSnapshot>(),
            minSimilarity = 1f,
            maxSimilarity = 0f
        };

        // 리밋 체커 시작 — 모드별 임계값 설정
        if (limitChecker != null)
        {
            limitChecker.SetPathEvaluator(this);
            limitChecker.Initialize();

            // 리밋 = 적정범위 끝(runtimeGuideEndRatio) 기반 자동 매핑
            dataLoader.UpdateLimitCheckerFromGuideEnd();

            limitChecker.SetEnabled(true);
        }

        // 가이드 핸드는 StartHold 완료 후에 재생됨 (UpdateStartHold에서 호출)
        // 여기서는 재생하지 않음!

        // 애니메이션으로 목을 제어하므로 NeckVRController 비활성화
        if (neckController != null)
        {
            neckController.Disable();
            if (showDebugLogs)
                ChunaLogger.Log("<color=yellow>[ChunaPathEvaluator] NeckVRController 비활성화 - 애니메이션으로 목 제어</color>");
        }

        if (showDebugLogs)
            ChunaLogger.Log("<color=green>[ChunaPathEvaluator] 평가 시작 - 시작 위치 대기 중...</color>");

        OnEvaluationStarted?.Invoke();
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

        if (guideOnlyMode)
        {
            // GuideOnly: 유사도/점수 계산 스킵
            ChunaLogger.Log("<color=yellow>[ChunaPathEvaluator] GuideOnly 모드 - 점수 계산 스킵</color>");
        }
        else
        {
            // 평균 유사도 계산
            CalculateAverageSimilarity();

            // 리밋 관련 통계 계산
            CalculateLimitStatistics();

            // 최종 점수 계산
            CalculateFinalScore();
        }

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
            ChunaLogger.Log("<color=green>========== 평가 완료 ==========</color>");
            ChunaLogger.Log($"평균 유사도: {currentSession.averageSimilarity:P0}");
            ChunaLogger.Log($"리밋 초과 횟수: {currentSession.limitViolationCount}");
            ChunaLogger.Log($"최종 점수: {currentSession.finalScore:F0}점 ({currentSession.grade})");
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

        // ★ AutoPlay 모드 리셋
        autoPlayHandler.Reset();

        // ★ 충돌 감지 플래그 리셋
        isLeftHandTouchingPatient = false;
        isRightHandTouchingPatient = false;

        // ★ 홀드 UI 리셋 이벤트 발생
        OnHoldProgressChanged?.Invoke(0f, startHoldDuration);

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
            ChunaLogger.Log("[ChunaPathEvaluator] 평가 중지");
    }

    /// <summary>
    /// 평가 리셋
    /// </summary>
    public void ResetEvaluation()
    {
        isEvaluating = false;

        // ★ AutoPlay 모드 리셋
        autoPlayHandler.Reset();

        // ★ 충돌 감지 플래그 리셋
        isLeftHandTouchingPatient = false;
        isRightHandTouchingPatient = false;

        // ★ 홀드 UI 리셋 이벤트 발생
        OnHoldProgressChanged?.Invoke(0f, startHoldDuration);

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
            ChunaLogger.Log("[ChunaPathEvaluator] 평가 리셋");
    }

    #endregion

    #region Metrics

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
        float leftSim = CalculateCurrentSimilarity(true, -1);
        float rightSim = CalculateCurrentSimilarity(false, -1);
        Vector3 leftHandPos = playerLeftHand != null ? playerLeftHand.transform.position : Vector3.zero;
        Vector3 rightHandPos = playerRightHand != null ? playerRightHand.transform.position : Vector3.zero;

        scoringEngine.RecordMetricsSnapshot(
            currentSession, evaluationStartTime, metricsRecordInterval,
            leftSim, rightSim, limitChecker, leftHandPos, rightHandPos);

        // 유사도 이벤트 발생
        OnSimilarityUpdated?.Invoke(leftSim, rightSim);
    }

    private float CalculateCurrentSimilarity(bool isLeftHand, int unusedIndex = -1)
    {
        if (loadedFrames == null || loadedFrames.Count == 0) return 0f;

        // 사용자 손 위치 기반 프레임으로 유사도 계산
        int frameIndex = Mathf.Clamp(userHandFrameIndex, 0, loadedFrames.Count - 1);

        PoseFrame frame = loadedFrames[frameIndex];

        // ★ 간소화된 비교 모드 사용 시
        if (useSimplifiedHandComparison)
        {
            if (isLeftHand && playerLeftHand != null)
            {
                var result = poseComparator.CompareLeftPoseSimplified(playerLeftHand, frame, frameIndex);
                return result.leftHandSimilarity;
            }
            else if (!isLeftHand && playerRightHand != null)
            {
                var result = poseComparator.CompareRightPoseSimplified(playerRightHand, frame, frameIndex);
                return result.rightHandSimilarity;
            }
        }
        else
        {
            // 기존 상세 비교 모드
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
        }

        return 0f;
    }

    /// <summary>
    /// ★ 실시간 유사도 가져오기 (사용자 손 위치와 가장 가까운 가이드 프레임과 비교)
    /// 타이밍 무관하게 동작 정확도 평가 - HandFeedbackUI에서 사용
    /// </summary>
    public float GetRealTimeSimilarity(bool isLeftHand)
    {
        return CalculateCurrentSimilarity(isLeftHand, -1);
    }

    /// <summary>
    /// ★ 양손 실시간 유사도 가져오기 (사용자 손 위치 기준)
    /// </summary>
    public (float left, float right) GetRealTimeSimilarityBoth()
    {
        float leftSim = CalculateCurrentSimilarity(true, -1);
        float rightSim = CalculateCurrentSimilarity(false, -1);
        return (leftSim, rightSim);
    }

    /// <summary>
    /// ★ 가중치 적용된 통합 유사도 가져오기 (오른손 70%, 왼손 30%)
    /// </summary>
    public float GetWeightedRealTimeSimilarity()
    {
        float leftSim = CalculateCurrentSimilarity(true, -1);
        float rightSim = CalculateCurrentSimilarity(false, -1);
        return leftSim * leftHandSimilarityWeight + rightSim * rightHandSimilarityWeight;
    }

    // ========== 점수 계산 (via EvaluationScoringEngine helper) ==========

    private void CalculateAverageSimilarity()
    {
        scoringEngine.CalculateAverageSimilarity(currentSession, leftHandSimilarityWeight, rightHandSimilarityWeight);
    }

    private void CalculateLimitStatistics()
    {
        // 이미 Update에서 누적됨
    }

    private void CalculateFinalScore()
    {
        scoringEngine.CalculateFinalScore(currentSession);
    }

    #endregion

    #region Guide Hand Playback

    // Guide hand playback is delegated to GuideHandPlaybackController helper.
    // This MonoBehaviour wraps IEnumerator with StartCoroutine.

    private void StartGuideHandPlayback()
    {
        if (!showGuideHands) return;
        if (loadedFrames == null || loadedFrames.Count == 0) return;

        StopGuideHandPlayback();

        IEnumerator routine = guidePlaybackController.PlaybackRoutine(
            loadedFrames, leftGuideHand, rightGuideHand,
            currentStartRatio, currentEndRatio,
            guidePlaybackSpeed, loopGuideHands, loopDelaySeconds,
            guideHandColor, showDebugLogs);

        guideHandCoroutine = StartCoroutine(routine);

        if (showDebugLogs)
            ChunaLogger.Log("[ChunaPathEvaluator] 가이드 핸드 재생 시작");
    }

    private void ShowGuideHandFirstFrame()
    {
        if (!showGuideHands)
        {
            if (showDebugLogs)
                ChunaLogger.LogWarning("[ChunaPathEvaluator] 가이드 핸드 표시 비활성화됨 (showGuideHands=false)");
            return;
        }

        guidePlaybackController.ShowFirstFrame(
            loadedFrames, leftGuideHand, rightGuideHand,
            currentStartRatio,
            guideHandColor, showDebugLogs);
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

    private void HideGuideHands()
    {
        guidePlaybackController.HideGuideHands(leftGuideHand, rightGuideHand);
    }

    #endregion

    #region Public API

    public bool IsEvaluating => isEvaluating;
    public EvaluationSession GetCurrentSession() => currentSession;

    /// <summary>
    /// 접촉 감지 부위 설정 (시나리오별로 다른 부위 사용)
    /// </summary>
    public void SetContactTarget(ContactTarget target)
    {
        activeContactTargets = new ContactTarget[] { target };
        primaryTarget = target;
        if (showDebugLogs)
            ChunaLogger.Log($"<color=cyan>[ChunaPathEvaluator] 접촉 감지 부위 변경: {target}</color>");
    }

    public void SetContactTargets(ContactTarget primary, ContactTarget assist)
    {
        activeContactTargets = new ContactTarget[] { primary, assist };
        primaryTarget = primary;
        if (showDebugLogs)
            ChunaLogger.Log($"<color=cyan>[ChunaPathEvaluator] 접촉 감지 부위 변경: {primary}(주동) + {assist}(보조)</color>");
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

    #endregion
}