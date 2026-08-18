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

    [Tooltip("반대쪽 어깨 충돌체(보충). 씬 원본 어깨 충돌체가 한쪽에만 있어 양쪽 어깨를 다 " +
             "판정하려면 여기에 반대쪽을 넣는다. Shoulder/HeadAndShoulder/ChestAndShoulder에 모두 적용")]
    [SerializeField] private Collider[] patientShoulderCollidersExtra;

    [Tooltip("환자 등·허리(흉추)에 부착된 충돌체들 - 복잡추나 흉추/늑골 술기의 실제 시술 부위. " +
             "양손이 각각 닿아야 하므로 좌·우 2개를 넣는다(한 손은 아무 것에나 닿으면 인정). " +
             "머리·어깨·흉부 충돌체(단순추나 다른 술기용)와 별개로 배치할 것")]
    [SerializeField] private Collider[] patientBackColliders;

    [Tooltip("환자 왼팔에 부착된 충돌체들 - 상완+전완 등 복수 가능")]
    [SerializeField] private Collider[] patientLeftArmColliders;

    [Tooltip("환자 오른팔에 부착된 충돌체들 - 상완+전완 등 복수 가능")]
    [SerializeField] private Collider[] patientRightArmColliders;

    [Tooltip("환자 무릎에 부착된 충돌체들 - 좌·우 2개. 앙와위에서 무릎을 세우게 하는 준비 동작용\n" +
             "메뉴 GuideChuna/환자 접촉 충돌체 설정 에서 생성·배선할 수 있다")]
    [SerializeField] private Collider[] patientKneeColliders;

    [Tooltip("현재 활성화된 접촉 감지 부위 (시나리오에서 설정)")]
    [SerializeField] private ContactTarget[] activeContactTargets = new ContactTarget[] { ContactTarget.HeadAndShoulder };

    [Tooltip("접촉 게이트 구간에서 터치할 부위를 반투명 구체로 표시한다. " +
             "환자 접촉 콜라이더는 렌더러가 없어 VR에서 안 보이므로 어디를 만져야 하는지 알 수 없다. " +
             "미접촉=연한 붉은색 / 접촉 성립=초록")]
    [SerializeField] private bool showContactTargetIndicator = true;

    [Tooltip("접촉 표시구 크기 배율. 1 = 실제 판정 범위와 동일(콜라이더가 작으면 표시도 작다)")]
    [Range(0.5f, 3f)]
    [SerializeField] private float contactTargetIndicatorScale = 1f;

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
    // 홀드 시간은 CSV 토큰 startHold= 와 config의 startHoldDuration·midHoldDuration이 정한다.
    // 예전 enableHoldDetection·requiredHoldTime 노브는 EvaluationPhaseManager로 로직이 옮겨가며 아무도 안 읽게 돼 제거했다.
    [Tooltip("정지 판정 속도 임계값 (m/s) - 이 속도 이하면 정지로 판정")]
    [SerializeField] private float holdVelocityThreshold = 0.05f;

    [Tooltip("MidHold 중 적정범위 안에서 이 속도 이상으로 진행도가 변하면 홀드 타이머 일시정지 (ratio/s) - 훑고 지나가기 방지")]
    [SerializeField] private float pauseProgressVelocity = 0.1f;

    // requireLimitSafeForHold·requireNearStartToBegin도 같은 이유로 제거했다(참조 0개, 켜도 안 걸렸다).

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
    private Vector3 leftHandStartHoldPosition;  // 시작 홀드 시 왼손 위치 저장
    // phaseHoldTime·isOverLimitBarrier는 EvaluationPhaseManager가 자기 사본으로 관리한다(여기 것은 아무도 안 읽어 제거).

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
    /// <summary>CSV conditionParams의 "palmSupport" — 손바닥으로 받치기만 하는 술기.
    /// 판정 완화(HandPoseComparator)뿐 아니라 접촉 페이드 예외에도 쓴다.</summary>
    private bool palmSupportMode;
    /// <summary>startHoldOnly가 **CSV conditionParams**로 켜졌는가(파일명 "등척성" 자동 감지와 구분).
    /// 가이드 핸드 루프 재생은 이 경우에만 한다 — 기존 단순추나 등척성운동의 표시를 바꾸지 않기 위함.</summary>
    private bool startHoldOnlyFromParams;
    private bool guideOnlyMode;                 // true면 StartHold/MidHold 스킵, 유사도 비평가 (시각 데모 전용)
    private bool skipMidHold;                   // true면 유사도 평가하되 MidHold 스킵, 임계점 통과 시 즉시 완료 (대흉근 등)
    private float isometricHoldEntryTime = -1f; // 등척성 StartHold 진입 시각 (홀드 완수도 계산용, -1=미진입)

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
    // ★ 회전(rotation) 단계는 재평가 모드에서도 적정범위 확장 안 함 (가동범위 고정)
    private bool isRotationStep => specifiedMovementType == "rotation";
    // ★ 스트레칭 모드: 진행도가 StartHold(stretchingStart) 기준 상대값이므로 임계점도 오프셋 차감
    /// <summary>★<c>reach=0.9</c> 토큰이 있으면 그 substep의 완료 임계점을 이 값으로 덮어쓴다(&lt;0 = 미지정).
    /// 왕복 동작처럼 "끝점까지 실제로 갔다"를 요구해야 하는 단계용 — 기본 30%면 조금만 움직여도 완료된다.</summary>
    private float reachOverride = -1f;

    /// <summary>★<c>reps=3</c> — 이 단계에서 궤적 도달을 몇 번 세고 완료할지(기본 1).</summary>
    private int repsRequired = 1;
    private int repsDone;

    /// <summary>궤적 끝에 도달했다. 아직 반복이 남았으면 true(= 완료시키지 말고 처음부터 다시 세라).
    /// EvaluationPhaseManager가 임계점 통과 시 호출한다.</summary>
    internal bool ConsumeRepAndContinue()
    {
        repsDone++;
        bool more = repsDone < repsRequired;
        if (repsRequired > 1)
            ChunaLogger.Log($"<color=green>[ChunaPathEvaluator] 반복 {repsDone}/{repsRequired}" +
                            $"{(more ? " — 계속" : " — 완료")}</color>");
        return more;
    }

    // ===== 구간 터치 카운트 모드 (touch=6) =====
    // ★홀드·진행률 임계 완료를 <b>아예 타지 않는다</b>. 궤적의 시작 구간과 끝 구간을
    //   번갈아 찍은 횟수만 센다 — "터치 터치 터치… 6번"이 요구사항이다(2026-08-13 사용자).
    private int touchTarget;        // 0이면 이 모드 아님
    private int touchCount;
    private int lastZone;           // 0=아직 / 1=시작 구간 / 2=끝 구간
    private float touchZone = 0.15f;   // 구간 폭(진행률). 시작 ≤ 0.15, 끝 ≥ 0.85
    // ★2026-08-19: 첫 샘플은 세지 않고 '출발 구간'으로만 기록한다.
    //   이게 없으면 팔을 내린 채(진행률 0 = 시작 구간) 단계에 들어서는 순간 1회가 공짜로 세어져,
    //   touch=6이 (공짜)·올림·내림·올림·내림·올림 으로 끝나 <b>마지막 내리기가 빠진 채</b> 넘어갔다
    //   (제2늑골 3-7 "올렸다 내리기 3회" — 사용자 지적).
    private bool touchSeeded;

    internal bool TouchCountMode => touchTarget > 0;

    /// <summary>지금 진행률이 어느 구간인지 보고, <b>직전과 다른 구간</b>에 들어오면 1회로 센다.
    /// 목표 횟수를 채우면 true(= 이 단계 완료).</summary>
    internal bool CountZoneTouch(float progress)
    {
        int zone = progress <= touchZone ? 1
                 : progress >= 1f - touchZone ? 2
                 : 0;

        // ★첫 샘플: 출발 구간만 기록하고 세지 않는다. 이후로는 '구간이 바뀔 때'만 1회다.
        //   → touch=6 이 올림·내림 3세트가 되고, 마지막이 <b>내리기</b>로 끝난다.
        if (!touchSeeded)
        {
            touchSeeded = true;
            lastZone = zone;
            ChunaLogger.Log($"<color=green>[ChunaPathEvaluator] 구간 터치 시작 지점 기록 " +
                            $"({(zone == 1 ? "시작" : zone == 2 ? "끝" : "중간")} 구간, 진행률 {progress:P0}) — 세지 않음</color>");
            return false;
        }

        if (zone == 0 || zone == lastZone) return false;   // 중간 구간이거나 같은 구간 반복 — 안 센다

        lastZone = zone;
        touchCount++;
        ChunaLogger.Log($"<color=green>[ChunaPathEvaluator] 구간 터치 {touchCount}/{touchTarget} " +
                        $"({(zone == 1 ? "시작" : "끝")} 구간, 진행률 {progress:P0})</color>");
        return touchCount >= touchTarget;
    }

    private float currentMidHoldStart => reachOverride >= 0f ? reachOverride :
                                         isStretchingMode ? (stretchingHoldStart - stretchingStart) :
                                         (isExtendedLimitMode && !isRotationStep ? extendedMidHoldStartRatio : midHoldStartRatio);
    private float currentMidHoldEnd => isStretchingMode ? (stretchingEnd - stretchingStart) :
                                       (isExtendedLimitMode && !isRotationStep ? extendedMidHoldEndRatio : midHoldEndRatio);

    // ★ 가이드 핸드 재생 범위 (런타임)
    private float runtimeGuideStartRatio = 0f;
    private float runtimeGuideEndRatio = 0.4f;
    private float currentStartRatio => runtimeGuideStartRatio;
    private float currentEndRatio => runtimeGuideEndRatio;
    // ★ 각도 표시 오프셋: 항상 0 (axis는 사용자 진행도이므로 오프셋 적용 안 함)
    // 가이드핸드/환자 애니메이션은 runtimeGuideStartRatio로 별도 처리됨
    private float currentAngleDisplayOffset => 0f;

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

    /// <summary>가이드손을 <b>현재 구간 그대로 1회만</b> 재생한다(루프 금지).
    /// ★사용자 지시(08-03, 08-10 재확인): 가이드손은 터치 여부와 무관하게 무조건 1회 재생한다.
    /// 재생이 끝난 지점이 곧 파지 위치라서, 반복하면 어디가 목표인지가 오히려 흐려진다.
    /// 씬의 loopGuideHands(=1)를 무시하려고 별도 진입점을 둔다 — 인스펙터 값은 건드리지 않는다.</summary>
    internal void StartGuideHandPlaybackOnce()
    {
        StartGuideHandPlayback(currentStartRatio, currentEndRatio, false);
    }

    /// <summary>'시각 안내 전용' 가이드손 재생 — 재생 구간과 루프 여부를 이번 호출에만 적용한다.
    /// (두개골 술기용: 전체 구간 1회 재생. 평가 파이프라인이 쓰는 비율·루프 필드는 건드리지 않는다.)</summary>
    internal void StartGuideHandPlaybackInternal(float startRatio, float endRatio, bool loop)
    {
        StartGuideHandPlayback(startRatio, endRatio, loop);
    }

    /// <summary>가이드손 재생 중지 + 숨김. (두개골: 사용자 손이 파지 위치에 닿았을 때 호출)</summary>
    internal void StopGuideHandPlaybackInternal()
    {
        StopGuideHandPlayback();
    }

    // ===== 가이드손 유지 규칙 (08-11 사용자 지시) =====
    //  ★규약 3가지
    //   ⓐ 재생이 끝나면 <b>마지막 자세 그대로 남는다</b>(숨기지 않는다).
    //   ⓑ 같은 클립이 다음 단계에서 또 요청되면 <b>다시 재생하지 않고</b> 그 자세를 유지한다
    //      — 같은 동작을 하는 단계에서 매번 처음부터 재생되면 이미 잡은 자세를 다시 잡으라는 신호가 된다.
    //   ⓒ 시술자가 접촉하면 숨기고, 접촉이 풀리면 <b>재생 없이</b> 마지막 자세로 다시 보여준다.

    /// <summary>마지막까지 재생해 그 자세로 붙잡아 둔 클립(없으면 null).</summary>
    private string guideHeldClip;
    /// <summary>지금 재생 중인(또는 방금 재생한) 클립 이름 — 완료 시 <see cref="guideHeldClip"/>이 된다.</summary>
    private string guidePlayingClip;
    /// <summary>유지 자세로 쓸 구간 끝 비율. 접촉으로 중간에 끊겨도 이 지점을 '끝난 상태'로 본다.</summary>
    private float guideHeldEndRatio = 1f;
    private bool guideHiddenByContact;

    /// <summary>이번 substep에서만 가이드손을 끄는가 — CSV conditionParams의 <c>noGuide</c>.
    ///
    /// ★왜 난이도 필드(showGuideHands)를 쓰지 않는가: 그 값은 LoadFromCSV마다 난이도 프리셋에서
    /// 다시 덮어써진다(ChunaDataLoader). substep 하나만 끄려고 거기에 손대면 다음 단계에
    /// 설정이 새거나 난이도 설정이 지워진다. 그래서 별도 플래그로 둔다.
    ///
    /// ★쓰는 곳: "이제 직접 합니다" 처럼 <b>손 녹화는 판정 기준으로 쓰되 시연은 감추는</b> 단계.
    /// 제2늑골 3-7이 그렇다 — 3-5·3-6에서 가이드손이 올리고 내리는 시연을 마친 뒤에도
    /// 마지막 자세가 그대로 남아(MarkGuideHeld) 사용자가 직접 반복하는 내내 떠 있었다.</summary>
    private bool guideSuppressedForStep;

    /// <summary>이번 substep 동안 가이드손을 끈다(켤 때는 즉시 감춘다). ScenarioManager가 substep 진입 때 넣는다.</summary>
    internal void SetGuideHandsSuppressedForStep(bool suppress)
    {
        if (guideSuppressedForStep == suppress) return;
        guideSuppressedForStep = suppress;
        if (!suppress) return;

        if (guideHandCoroutine != null)
        {
            StopCoroutine(guideHandCoroutine);
            guideHandCoroutine = null;
        }
        // '이 동작은 이미 봤다'는 기록은 남긴다 — 지우면 다음 단계에서 처음부터 다시 재생된다.
        MarkGuideHeld();
        HideGuideHands();
    }

    /// <summary>
    /// 유지 판정의 키 = 클립 + 재생 구간 + <b>표시할 손 범위</b>.
    ///
    /// ★구간을 넣는 이유: 한 클립에 좌→우를 이어 녹화하고 자세별로 앞/뒤를 나눠 쓰는 경우가 있어
    ///   클립 이름만 보면 두 번째 자세가 '이미 봤다'로 묻힌다.
    /// ★손 범위를 넣는 이유(2026-08-12): 같은 클립·같은 구간이라도 <b>보여 줄 손이 늘어나면</b>
    ///   새 손 입장에서는 처음 보는 동작이다. 흉추 신전이 그렇다 —
    ///   보조수 단계에서 오른손만 보여 준 뒤 주동수 단계에서 양손을 보여 주는데,
    ///   키가 같으면 '이미 봤다'로 묻혀 <b>왼손 가이드가 멈춘 채로 나타난다.</b>
    /// </summary>
    private string GuideKey(string clipName, float startRatio, float endRatio) =>
        $"{clipName}|{startRatio:0.###}|{endRatio:0.###}|{guideScopeTag}";

    /// <summary>이번 단계에서 보여 줄 손 범위 표식(예: 양손/오른손). ScenarioManager가 substep 진입 때 넣는다.
    /// ★suppress 플래그를 직접 읽지 않는 이유 — 그 플래그는 컨트롤러가 <b>매 프레임</b> 갱신해서,
    /// 재생을 시작하는 시점에는 아직 <b>이전 단계 값</b>이다.</summary>
    private string guideScopeTag = "";

    internal void SetGuideScopeTag(string tag) => guideScopeTag = tag ?? "";

    /// <summary>이 클립(같은 구간·같은 손 범위)이 이미 끝난 자세로 유지 중인가(= 다시 재생할 필요가 없는가).</summary>
    internal bool IsGuideClipHeld(string clipName, float startRatio = 0f, float endRatio = 1f) =>
        !string.IsNullOrEmpty(clipName) && !string.IsNullOrEmpty(guideHeldClip) &&
        guideHeldClip == GuideKey(clipName, startRatio, endRatio);

    /// <summary>접촉 때문에 숨겨 둔 상태인가(= 접촉이 풀리면 되살려야 하는가).</summary>
    internal bool IsGuideHandHidden => guideHiddenByContact;

    // ===== 손별 숨김 (시술자가 그 손을 제자리에 갖다 대면 그 손 가이드만 사라진다) =====
    private bool guideSuppressLeft, guideSuppressRight;

    /// <summary>재생 루프·정지 표시가 이 손을 다시 켜지 못하게 막는가.</summary>
    internal bool IsGuideHandSuppressed(bool isLeft) => isLeft ? guideSuppressLeft : guideSuppressRight;

    /// <summary>그 손의 가이드손을 숨기거나 되살린다.
    /// ★되살릴 때 재생하지 않는다 — 지금 그려져 있는 자세 그대로 다시 보이기만 한다(사용자 지시).</summary>
    internal void SuppressGuideHandInternal(bool isLeft, bool suppress)
    {
        if (isLeft)
        {
            if (guideSuppressLeft == suppress) return;
            guideSuppressLeft = suppress;
        }
        else
        {
            if (guideSuppressRight == suppress) return;
            guideSuppressRight = suppress;
        }

        HandTransformMapper h = isLeft ? leftGuideHand : rightGuideHand;
        if (h == null) return;

        if (suppress)
        {
            h.SetVisible(false);
            MarkGuideHeld();   // 손을 댔다 = 이 동작은 이미 본 것 → 다음 단계에서 다시 재생하지 않는다
        }
        else if (GuideHandHasData(isLeft))
        {
            h.SetVisible(true);
        }
    }

    /// <summary>손별 숨김 플래그만 푼다(단계가 바뀔 때 한쪽 손만 숨은 채 남는 것 방지).
    /// ★가이드손 전체가 숨김 상태(<see cref="guideHiddenByContact"/>)면 <b>되살리지 않는다</b> —
    /// 안 그러면 "손 녹화가 없는 단계라 숨겼는데 곧바로 다시 켜지는" 충돌이 난다(2026-08-13).</summary>
    internal void ClearGuideHandSuppression()
    {
        guideSuppressLeft = false;
        guideSuppressRight = false;

        if (guideHiddenByContact) return;   // 이 단계는 아예 가이드손을 안 쓴다 — 그대로 둔다

        if (leftGuideHand != null && GuideHandHasData(true)) leftGuideHand.SetVisible(true);
        if (rightGuideHand != null && GuideHandHasData(false)) rightGuideHand.SetVisible(true);
    }

    /// <summary>지금 로드된 녹화에 이 손의 데이터가 있는가.
    /// ★한 손만 녹화한 클립에서 반대 손 가이드를 되살리지 않기 위한 확인용
    /// (접촉이 풀렸을 때 무조건 SetVisible(true)를 하면 빈 손이 떠 버린다).</summary>
    internal bool GuideHandHasData(bool isLeft)
    {
        if (loadedFrames == null || loadedFrames.Count == 0) return false;
        int[] check = loadedFrames.Count > 1 ? new[] { 0, loadedFrames.Count / 2 } : new[] { 0 };
        foreach (int i in check)
        {
            var poses = isLeft ? loadedFrames[i].leftLocalPoses : loadedFrames[i].rightLocalPoses;
            if (poses != null && poses.Count > 0) return true;
        }
        return false;
    }

    private void MarkGuideHeld()
    {
        if (!string.IsNullOrEmpty(guidePlayingClip)) guideHeldClip = guidePlayingClip;
    }

    /// <summary>클립 이름을 알고 재생하는 진입점. <b>같은 클립이 유지 중이면 재생하지 않고</b>
    /// 끝난 자세를 그대로 보여준다.</summary>
    internal void PlayGuideHandOnceInternal(string clipName, float startRatio, float endRatio)
    {
        if (IsGuideClipHeld(clipName, startRatio, endRatio))
        {
            ShowGuideHandLastFrameInternal();
            return;
        }
        StartGuideHandPlayback(startRatio, endRatio, false, GuideKey(clipName, startRatio, endRatio));
    }

    /// <summary>마지막 자세로 <b>정지 표시</b>한다(재생 아님). 접촉이 풀렸을 때·같은 동작이 이어질 때 쓴다.</summary>
    internal void ShowGuideHandLastFrameInternal()
    {
        if (!showGuideHands || guideSuppressedForStep) return;
        if (loadedFrames == null || loadedFrames.Count == 0) return;

        if (guideHandCoroutine != null)
        {
            StopCoroutine(guideHandCoroutine);
            guideHandCoroutine = null;
        }

        MarkGuideHeld();
        guideHiddenByContact = false;

        // ShowFirstFrame은 '지정한 비율의 한 프레임을 그린다' — 끝 비율을 주면 마지막 자세가 된다.
        guidePlaybackController.ShowFirstFrame(
            loadedFrames, leftGuideHand, rightGuideHand,
            guideHeldEndRatio,
            guideHandColor, showDebugLogs);
    }

    /// <summary>접촉 중 숨김 — <b>어느 클립의 끝난 자세인지는 기억한다</b>(접촉이 풀리면 그대로 되살린다).</summary>
    internal void HideGuideHandKeepHeldInternal()
    {
        if (guideHiddenByContact) return;

        if (guideHandCoroutine != null)
        {
            StopCoroutine(guideHandCoroutine);
            guideHandCoroutine = null;
        }

        // 접촉으로 끊긴 것도 '이 동작은 이미 봤다'로 친다 → 다음 단계에서 다시 재생하지 않는다.
        MarkGuideHeld();
        guideHiddenByContact = true;
        HideGuideHands();
    }

    // ChunaDataLoader needs
    internal bool ShowDebugLogs => showDebugLogs;
    internal string CurrentProcedureName { get => currentProcedureName; set => currentProcedureName = value; }
    internal List<PoseFrame> LoadedFrames { get => loadedFrames; set => loadedFrames = value; }
    internal Transform ReferenceTransform => referenceTransform;
    internal Transform PivotTransform => pivotTransform;
    internal HandPoseComparator PoseComparator => poseComparator;

    /// <summary>지금 로드된 가이드 클립의 <b>마지막 프레임</b>과 현재 두 손의 유사도(0~1).
    ///
    /// ★두개골·늑골·흉추 계열의 가이드 클립은 <b>마지막 프레임이 곧 유지해야 할 목표 자세</b>다
    /// (사용자 확인, 2026-08-18). 그래서 동작 전체를 따라갔는지 대신 '끝 자세를 얼마나 닮게 잡고
    /// 있는가'만 보면 유지형 술기의 손모양을 채점할 수 있다.
    /// ※제2늑골 교정만 예외 — 팔을 올리고 내리는 <b>움직이는</b> 동작이라 끝 프레임이 목표가 아니다.
    ///
    /// 평가 파이프라인(StartEvaluation)을 건드리지 않는다 — 두개골 판정은 파지점 게이트가 담당하고,
    /// 여기서는 읽기만 한다(08-11에 평가를 같이 켰다가 파지 게이트가 통째로 무너진 전례).</summary>
    internal bool TryGetLastFrameSimilarity(HandVisual left, HandVisual right, out float similarity)
    {
        similarity = 0f;
        if (poseComparator == null) return false;
        if (loadedFrames == null || loadedFrames.Count == 0) return false;
        if (left == null && right == null) return false;

        PoseFrame last = loadedFrames[loadedFrames.Count - 1];
        if (last == null) return false;

        if (left != null && right != null)
        {
            var r = poseComparator.CompareBothHands(left, right, last);
            similarity = Mathf.Clamp01((r.leftHandSimilarity + r.rightHandSimilarity) * 0.5f);
        }
        else if (left != null)
            similarity = Mathf.Clamp01(poseComparator.CompareLeftPose(left, last).leftHandSimilarity);
        else
            similarity = Mathf.Clamp01(poseComparator.CompareRightPose(right, last).rightHandSimilarity);

        return true;
    }
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
    internal bool IsStartHoldOnly => startHoldOnly;

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

        // 보조수(왼손) 품질 — 단손 step에서 접촉 유지 비율 × 방향(orientation) 프록시 (0~1)
        public float leftContactRatio;
        public float supportQuality;

        // 유사도 안정성
        public float similarityStdDev;             // 유사도 표준편차

        // 안전성
        public float peakExceededRatio;            // 최대 초과 비율

        // 등척성운동(홀드 전용) 채점 — 가동범위 대신 홀드 완수도로 40점 블록 계산
        public bool isIsometric;                   // true면 40점 블록을 holdQuality로 환산
        public float holdQuality = 1f;             // 0~1, 요구 홀드시간 / 실제 StartHold 소요시간

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
            public bool leftTouching;   // 이 시점 보조수(왼손) 환자 접촉 여부
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

        if (!isEvaluating)
        {
            HideContactTargetIndicator();
            return;
        }

        // ★ AutoPlay 모드: 핸드데이터 없이 애니메이션만 자동 재생 (via helper)
        if (autoPlayHandler.IsAutoPlayMode)
        {
            // PassiveStretch: 보조수(왼손) 접촉 중일 때만 애니메이션 재생
            // 게이팅 없는 경우 항상 true로 무시
            UpdateCollisionDetection();
            // bothHands: 양손 파지 단계 — 양손이 각각 대상 부위에 닿아야 인정.
            // touchOnce: 어느 손으로 터치해도 열린다(래치는 AutoPlayHandler가 담당).
            // 그 외 기존 게이팅(경추ROM 등)은 종전대로 왼손(보조수)만 인정.
            bool touching;
            if (autoPlayHandler.RequireBothHands)
                touching = isLeftHandTouchingPatient && isRightHandTouchingPatient;
            else
                touching = isLeftHandTouchingPatient ||
                           (autoPlayHandler.LatchGate && isRightHandTouchingPatient);
            bool gateOpen = !autoPlayHandler.IsGated || touching;

            // 접촉해야 진행되는 구간에서만 "여기를 터치" 표시구를 띄운다.
            UpdateContactTargetIndicator(autoPlayHandler.IsGated,
                                         touching || autoPlayHandler.IsGateLatched);

            bool completed = autoPlayHandler.UpdateAutoPlay(patientAnimator, gateOpen, showDebugLogs);
            if (completed)
            {
                HandleAutoPlayComplete();
            }
            return;
        }

        HideContactTargetIndicator();

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
                pauseProgressVelocity,
                startHoldDuration, midHoldDuration,
                currentMidHoldStart, currentMidHoldEnd,
                leftHandDriftThreshold,
                currentStartRatio,
                useRelativeMovement, startHoldOnly, guideOnlyMode, skipMidHold, modeConfigurator.IsGuideMode,
                isRotationStep,
                showDebugLogs);

            currentPhase = phaseManager.CurrentPhase;
        }

        // 애니메이션 선형보간 업데이트
        UpdateAnimationLerp();

        // 등척성: StartHold 진입 시각 기록 (홀드 완수도 계산용)
        if (startHoldOnly && currentPhase == EvaluationPhase.StartHold && isometricHoldEntryTime < 0f)
            isometricHoldEntryTime = Time.time;

        // 메트릭 기록 (Moving/MidHold, 그리고 등척성 홀드 중. guideOnly 제외)
        // 등척성운동은 startHoldOnly로 Moving/MidHold를 안 거치므로 StartHold에서 유사도를 샘플링해야
        // 주동수 포즈 + 보조수(접촉×포즈)가 채점된다.
        bool isIsometricHold = startHoldOnly && currentPhase == EvaluationPhase.StartHold;
        if (!guideOnlyMode && (currentPhase == EvaluationPhase.Moving || currentPhase == EvaluationPhase.MidHold || isIsometricHold))
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
                // ★ 추출 벡터는 감지 축에 수직이어야 투영이 안정적
                //   (Z축 감지 시 forward 추출하면 축과 평행 → 수치 불안정)
                Vector3 detectionAxis = GetRotationDetectionAxis();
                Vector3 measureVec = GetRotationMeasurementVector();
                Vector3 refDir = userHoldReferenceRotation * measureVec;
                Vector3 curDir = rightHandRot * measureVec;
                float signedAngle = Vector3.SignedAngle(refDir, curDir, detectionAxis);

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
                    ChunaLogger.Log($"<color=cyan>  기준:({refEuler.x:F0},{refEuler.y:F0},{refEuler.z:F0}) → 현재:({curEuler.x:F0},{curEuler.y:F0},{curEuler.z:F0}), signed:{signedAngle:F1}°, 축:{detectionAxis}, 측정벡터:{measureVec}</color>");
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
    /// ★ 스트레칭 모드: 사용자 progress 0~1을 currentStartRatio~1 범위로 remap
    /// → StartHold 직후 progress=0이어도 애니메이션은 offset 위치(stretchingStart) 유지
    /// </summary>
    private void SyncAnimationToFrame(float ratio)
    {
        if (patientAnimator == null || string.IsNullOrEmpty(currentAnimationStateName))
            return;

        float clamped = Mathf.Clamp01(ratio);

        if (isStretchingMode)
        {
            // 스트레칭 모드에서는 사용자 손 progress 0%일 때 애니메이션이 offset 위치에 머물러야 함
            // (사용자 손이 이미 limit/offset 위치에서 StartHold 했으므로)
            float startOffset = currentStartRatio;
            targetAnimationRatio = Mathf.Lerp(startOffset, 1f, clamped);
        }
        else
        {
            targetAnimationRatio = clamped;
        }
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
            patientBackColliders = patientBackColliders,
            patientShoulderCollidersExtra = patientShoulderCollidersExtra,
            patientLeftArmColliders = patientLeftArmColliders,
            patientRightArmColliders = patientRightArmColliders,
            patientKneeColliders = patientKneeColliders,
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
            // ★손바닥 지지 술기(palmSupport)는 시작부터 끝까지 손이 환자에 닿아 있으므로
            //   접촉 페이드를 걸면 가이드 핸드가 내내 알파 0.15로 보이지 않는다 → 이 모드에서만 예외.
            fadeOnTouch = fadeOnTouch && !palmSupportMode,
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

    // ─── 접촉 표시구 (어디를 터치해야 하는지) ───────────────────────────────
    private ContactTargetIndicator contactTargetIndicator;
    private readonly List<Collider> contactIndicatorBuffer = new List<Collider>();

    /// <summary>접촉 게이트 구간에서 터치 대상 부위를 반투명 구체로 표시.</summary>
    private void UpdateContactTargetIndicator(bool gateActive, bool satisfied)
    {
        if (!showContactTargetIndicator || !gateActive || HideContactHintsForDifficulty())
        {
            HideContactTargetIndicator();
            return;
        }

        if (contactTargetIndicator == null)
            contactTargetIndicator = new ContactTargetIndicator();

        CollectActiveContactColliders(contactIndicatorBuffer);
        contactTargetIndicator.UpdateMarkers(contactIndicatorBuffer, satisfied, contactTargetIndicatorScale);
    }

    /// <summary>평가·상급 난이도에서는 접촉 표시구를 감춘다(2026-08-18 사용자 지적).
    ///
    /// ★파지점과 다르게 다루는 이유: 파지점은 횡돌기·유양돌기처럼 <b>손으로 더듬어 찾아야 하는 작은 지점</b>이라
    /// 촉각이 없는 VR에서 구체가 유일한 단서다(그래서 평가에서도 남긴다). 반면 접촉 표시구는
    /// <b>무릎·팔처럼 누구나 아는 큰 부위</b>를 "여기를 터치하세요"라고 알려주는 힌트에 가깝다 —
    /// 평가에서까지 띄우면 답을 주는 셈이다.
    ///
    /// ★판정에는 영향이 없다 — 접촉 판정은 씬의 콜라이더로 하고, 표시구는 콜라이더를 지운 표시 전용이다.
    /// 기준은 가이드손을 숨기는 난이도(상급·평가)와 같다.</summary>
    private bool HideContactHintsForDifficulty()
    {
        var dm = ChunaTraining.DifficultyManager.Instance;
        return dm != null && !dm.ShowGuideHands;
    }

    private void HideContactTargetIndicator()
    {
        if (contactTargetIndicator != null)
            contactTargetIndicator.Hide();
    }

    /// <summary>현재 activeContactTargets가 가리키는 실제 콜라이더들을 중복 없이 모은다.</summary>
    private void CollectActiveContactColliders(List<Collider> results)
    {
        results.Clear();
        if (activeContactTargets == null) return;

        foreach (var target in activeContactTargets)
        {
            switch (target)
            {
                case ContactTarget.Head:
                    AddContactCollider(results, patientHeadCollider);
                    break;
                case ContactTarget.Shoulder:
                    AddContactCollider(results, patientShoulderCollider);
                    AddContactColliders(results, patientShoulderCollidersExtra);
                    break;
                case ContactTarget.Chest:
                    AddContactCollider(results, patientChestCollider);
                    break;
                case ContactTarget.Back:
                    AddContactColliders(results, patientBackColliders);
                    break;
                case ContactTarget.ChestAndShoulder:
                    AddContactCollider(results, patientChestCollider);
                    AddContactCollider(results, patientShoulderCollider);
                    AddContactColliders(results, patientShoulderCollidersExtra);
                    break;
                case ContactTarget.LeftArm:
                    AddContactColliders(results, patientLeftArmColliders);
                    break;
                case ContactTarget.RightArm:
                    AddContactColliders(results, patientRightArmColliders);
                    break;
                case ContactTarget.Knee:
                    AddContactColliders(results, patientKneeColliders);
                    break;
                case ContactTarget.Arms:
                    AddContactColliders(results, patientLeftArmColliders);
                    AddContactColliders(results, patientRightArmColliders);
                    break;
                case ContactTarget.HeadAndShoulder:
                default:
                    AddContactCollider(results, patientHeadCollider);
                    AddContactCollider(results, patientShoulderCollider);
                    AddContactColliders(results, patientShoulderCollidersExtra);
                    break;
            }
        }
    }

    private static void AddContactCollider(List<Collider> results, Collider c)
    {
        if (c != null && !results.Contains(c))
            results.Add(c);
    }

    private static void AddContactColliders(List<Collider> results, Collider[] colliders)
    {
        if (colliders == null) return;
        foreach (var c in colliders)
            AddContactCollider(results, c);
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
    // 접촉 게이트가 열릴 때까지 클립 재생을 미룬다(0프레임 강제로 앞 자세가 풀리는 것 방지).
    private bool deferAnimationUntilGateOpen;
    private bool pendingAnimationStart;

    /// <summary>게이트가 처음 열렸을 때 보류해 둔 클립을 실제로 재생한다(AutoPlayHandler가 호출).</summary>
    internal void BeginDeferredAnimation()
    {
        if (!pendingAnimationStart) return;
        pendingAnimationStart = false;

        if (patientAnimator == null || string.IsNullOrEmpty(currentAnimationStateName)) return;

        // ★구간이 지정돼 있으면(anim=시작:끝) 그 구간만 재생한다.
        //   흉추 신전은 '손이 파지점에 닿으면 30프레임까지만' 재생해 살짝 들리는 것을 연출한다.
        if (deferredRangeSet)
        {
            deferredRangeSet = false;
            PlayPatientAnimationRange(currentAnimationStateName, deferredFrom, deferredTo, deferredSpeed);
            ChunaLogger.Log($"<color=green>[Animation] 접촉 감지 — '{currentAnimationStateName}' " +
                            $"구간 재생 ({deferredFrom:P0}~{deferredTo:P0})</color>");
            return;
        }

        patientAnimator.Play(currentAnimationStateName, 0, 0f);
        patientAnimator.speed = 1f;
        if (secondaryPatientAnimator != null)
        {
            secondaryPatientAnimator.Play(currentAnimationStateName, 0, 0f);
            secondaryPatientAnimator.speed = 1f;
        }
        ChunaLogger.Log($"<color=green>[Animation] 접촉 감지 — '{currentAnimationStateName}' 재생 시작</color>");
    }

    private bool deferredRangeSet;
    private float deferredFrom, deferredTo, deferredSpeed = 1f;

    internal bool HasPendingAnimation => pendingAnimationStart;

    /// <summary>
    /// 클립을 '재생 대기' 상태로만 걸어 둔다(자세는 그대로 유지).
    /// 실제 재생은 <see cref="BeginDeferredAnimation"/>을 부르는 쪽(파지 성립 등)이 결정한다.
    /// </summary>
    public void ArmPatientAnimationForDeferredStart(string stateName)
        => ArmPatientAnimationForDeferredStart(stateName, 0f, 1f, false, 1f);

    /// <summary>구간(anim=시작:끝)·속도(animSpeed=)까지 지정해 재생을 대기시킨다.</summary>
    public void ArmPatientAnimationForDeferredStart(string stateName, float from, float to,
                                                    bool useRange, float speed)
    {
        deferredRangeSet = useRange;
        deferredFrom = from;
        deferredTo = to;
        deferredSpeed = speed;
        deferAnimationUntilGateOpen = true;
        SetPatientAnimation(stateName, AnimationPlayMode.AutoPlay);   // defer 분기를 타 pending으로만 남는다
    }

    /// <summary>
    /// 이 단계는 환자 애니를 지정하지 않았다 — 직전 클립 이름을 끊어 둔다.
    /// <para>★이걸 안 하면 남아 있는 이름을 다른 코드가 다시 <c>Play(이름, 0, 0f)</c> 해서
    /// <b>직전 동작만 시작 자세로 되감긴다</b>(무릎은 올라간 채 팔만 풀리는 증상).
    /// 되감을 수 있는 곳은 셋 — StartEvaluation의 시작프레임 세팅, UpdateAnimationLerp의 동기화,
    /// SetPatientAnimation의 재생. 셋 다 이름이 비면 아무것도 하지 않는다.</para>
    /// <para>애니메이터 자체는 건드리지 않으므로 현재 자세는 그대로 멈춘 채 유지된다.</para>
    /// </summary>
    // ── 자세 고정(애니 지정이 없는 단계) ──────────────────────────────
    private bool holdPoseActive;
    private int holdPoseHash;
    private float holdPoseTime;

    /// <summary>
    /// 지금 자세를 그대로 붙잡는다. 애니를 지정하지 않은 단계(파지 등)에서 쓴다.
    /// 어떤 코드가 <c>Play(..., 0f)</c>로 되감아도 LateUpdate에서 되돌려 놓으므로
    /// "단계 들어가자마자 직전 동작이 풀리는" 현상이 원천적으로 막힌다.
    /// </summary>
    public void HoldCurrentPose()
    {
        if (patientAnimator == null) return;
        var st = patientAnimator.GetCurrentAnimatorStateInfo(0);
        holdPoseHash = st.shortNameHash;
        holdPoseActive = true;

        // ★재생 중인 1회성 클립은 <b>끝까지 재생한 뒤에</b> 고정한다 (2026-08-12).
        //   예전엔 호출 시점의 재생 위치로 즉시 얼려서, 파지가 성립하자마자 다음 단계로 넘어갈 때
        //   동작이 중간에 멈춰 "재생되다 말고 원복된다"로 보였다(제1늑골 '제1늑골 고개').
        //   완료 지연을 없애 단계 전환이 빨라지면서 더 두드러졌다.
        holdPoseWaitFinish = !st.loop && st.normalizedTime < 1f;
        holdPoseTime = holdPoseWaitFinish ? 1f : Mathf.Clamp01(st.normalizedTime);

        if (!holdPoseWaitFinish)
        {
            patientAnimator.speed = 0f;
            if (secondaryPatientAnimator != null) secondaryPatientAnimator.speed = 0f;
        }

        ChunaLogger.Log($"<color=green>[Animation] 자세 고정 (hash={holdPoseHash}, {holdPoseTime:P0}" +
                        $"{(holdPoseWaitFinish ? " — 재생을 끝까지 마친 뒤 고정" : "")})</color>");
    }

    /// <summary>재생이 끝나기를 기다리는 중인가(1회성 클립을 중간에 얼리지 않기 위해).</summary>
    private bool holdPoseWaitFinish;

    /// <summary>자세 고정 해제. 애니를 지정한 단계로 넘어갈 때 호출.</summary>
    public void ReleasePoseHold()
    {
        holdPoseActive = false;
        holdPoseWaitFinish = false;
        rangeActive = false;

        // ★속도를 되돌린다 — 자세 고정·구간 재생은 speed=0으로 세워 두는데,
        //   풀어 줄 때 복구하지 않으면 <b>다음 단계 애니가 아예 움직이지 않는다</b>(2026-08-12).
        if (patientAnimator != null) patientAnimator.speed = 1f;
        if (secondaryPatientAnimator != null) secondaryPatientAnimator.speed = 1f;
    }

    // ── 구간 재생(클립의 일부만) ──────────────────────────────────────────
    private bool rangeActive;
    private int rangeHash;
    private float rangeEnd;

    /// <summary>
    /// 환자 클립을 <b>정규화 구간 [from, to]</b>만 재생하고 그 자세에서 멈춘다.
    ///
    /// ★한 동작을 여러 단계에 나눠 보여줄 때 쓴다 — 흉추 신전은 '파지하면서 절반만 일으키고',
    /// 파지가 확인되면 '들이마시며 끝까지' 일으킨다. 클립을 쪼개지 않고 같은 클립을 두 번에 나눠 쓴다.
    /// CSV에서는 conditionParams에 <c>anim=0:0.5</c> 처럼 적는다.
    /// </summary>
    public void PlayPatientAnimationRange(string stateName, float from, float to, float speed = 1f)
    {
        if (patientAnimator == null || string.IsNullOrWhiteSpace(stateName)) return;

        string trimmed = stateName.Trim();
        int hash = Animator.StringToHash(trimmed);
        if (patientAnimator.runtimeAnimatorController == null || !patientAnimator.HasState(0, hash))
        {
            ChunaLogger.LogWarning($"[Animation] 구간 재생 실패 — State '{trimmed}'가 현재 컨트롤러에 없습니다.");
            return;
        }

        ReleasePoseHold();
        rangeHash = hash;
        rangeEnd = Mathf.Clamp01(to);
        rangeActive = true;
        currentAnimationStateName = trimmed;

        float start = Mathf.Clamp01(from);
        float spd = Mathf.Max(0.05f, speed);   // 0이면 영영 안 끝난다
        patientAnimator.speed = spd;
        patientAnimator.Play(hash, 0, start);
        if (secondaryPatientAnimator != null)
        {
            secondaryPatientAnimator.speed = spd;
            secondaryPatientAnimator.Play(hash, 0, start);
        }

        ChunaLogger.Log($"<color=magenta>[Animation] 구간 재생 '{trimmed}' " +
                        $"{start:P0} → {rangeEnd:P0} (속도 {spd:0.##}배)</color>");
    }

    private void LateUpdate()
    {
        // 구간 재생: 끝 지점에 닿으면 그 자세로 멈춰 세우고 유지로 넘긴다.
        if (rangeActive && patientAnimator != null)
        {
            var rs = patientAnimator.GetCurrentAnimatorStateInfo(0);
            if (rs.shortNameHash == rangeHash && rs.normalizedTime >= rangeEnd)
            {
                patientAnimator.Play(rangeHash, 0, rangeEnd);
                patientAnimator.speed = 0f;
                if (secondaryPatientAnimator != null)
                {
                    secondaryPatientAnimator.Play(rangeHash, 0, rangeEnd);
                    secondaryPatientAnimator.speed = 0f;
                }
                rangeActive = false;

                // 그 자세를 그대로 붙잡는다(다른 코드가 되감아도 되돌려 놓는다).
                holdPoseHash = rangeHash;
                holdPoseTime = rangeEnd;
                holdPoseWaitFinish = false;
                holdPoseActive = true;
            }
            return;
        }

        if (!holdPoseActive || patientAnimator == null) return;

        // 아직 재생 중인 1회성 클립은 끝날 때까지 손대지 않는다 — 끝나면 그 자세로 고정한다.
        if (holdPoseWaitFinish)
        {
            var playing = patientAnimator.GetCurrentAnimatorStateInfo(0);
            if (playing.shortNameHash == holdPoseHash && playing.normalizedTime < 1f) return;
            holdPoseWaitFinish = false;
        }

        var st = patientAnimator.GetCurrentAnimatorStateInfo(0);
        bool drifted = st.shortNameHash != holdPoseHash ||
                       Mathf.Abs(Mathf.Clamp01(st.normalizedTime) - holdPoseTime) > 0.01f;
        if (drifted)
        {
            patientAnimator.Play(holdPoseHash, 0, holdPoseTime);
            if (secondaryPatientAnimator != null)
                secondaryPatientAnimator.Play(holdPoseHash, 0, holdPoseTime);
        }
        patientAnimator.speed = 0f;
        if (secondaryPatientAnimator != null) secondaryPatientAnimator.speed = 0f;
    }

    public void ClearPatientAnimationBinding()
    {
        if (string.IsNullOrEmpty(currentAnimationStateName) && !pendingAnimationStart) return;

        if (showDebugLogs)
            ChunaLogger.Log($"<color=yellow>[Animation] 애니 지정 없는 단계 — 직전 클립('{currentAnimationStateName}') 연결 해제(자세 유지)</color>");

        currentAnimationStateName = null;
        pendingAnimationStart = false;
        deferAnimationUntilGateOpen = false;
    }

    /// <param name="blendSeconds">
    /// AutoPlay 모드에서 앞 자세와 섞어 넘어갈 시간(초). 0이면 종전대로 즉시 전환(Play).
    /// CSV conditionParams의 <b>blend=</b> 토큰으로 지정한다.
    /// ★특히 '중립' 클립은 길이 0짜리 포즈라 Play로 넣으면 순간이동한다(제2늑골 재평가 4-1).
    ///   CrossFade로 넣으면 현재 자세에서 중립까지 그 시간 동안 부드럽게 흘러간다.
    /// </param>
    public void SetPatientAnimation(string animationStateName, AnimationPlayMode playMode = AnimationPlayMode.SyncWithUser,
                                    float blendSeconds = 0f)
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
            // ★접촉 게이트가 있는 단계: 아직 재생하지 않는다(앞 동작의 마지막 자세를 그대로 유지).
            //   AutoPlayHandler가 게이트가 처음 열리는 순간 BeginDeferredAnimation()을 부른다.
            //   플래그는 1회성 — 클립 없는 단계를 지나며 남아 다음 재생을 막는 일이 없게 여기서 소비한다.
            bool defer = deferAnimationUntilGateOpen;
            deferAnimationUntilGateOpen = false;
            if (defer)
            {
                pendingAnimationStart = true;
                patientAnimator.speed = 0f;
                if (secondaryPatientAnimator != null) secondaryPatientAnimator.speed = 0f;
                ChunaLogger.Log($"<color=green>[Animation] 접촉 대기 — '{trimmedName}' 재생 보류(앞 자세 유지)</color>");
                return;
            }

            // 자동 재생 모드
            // ★blend>0이면 앞 자세에서 섞어 들어간다. Play는 첫 프레임으로 즉시 튀므로
            //   중립 복귀처럼 '스르륵 돌아가야' 하는 자리에서는 순간이동으로 보인다.
            float blend = Mathf.Clamp(blendSeconds, 0f, 2f);
            if (blend > 0f)
            {
                patientAnimator.CrossFadeInFixedTime(trimmedName, blend, 0, 0f);
                patientAnimator.speed = 1f;
                if (secondaryPatientAnimator != null)
                {
                    secondaryPatientAnimator.CrossFadeInFixedTime(trimmedName, blend, 0, 0f);
                    secondaryPatientAnimator.speed = 1f;
                }
                ChunaLogger.Log($"<color=green>[Animation] ★ 자동 재생 시작(블렌드 {blend:F2}s): '{trimmedName}' (speed=1)</color>");
                return;
            }

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
            // 사용자 동기화 모드 - 시작 위치로 설정 (스트레칭이면 stretchingStart, 아니면 0)
            float startRatio = currentStartRatio;
            patientAnimator.Play(trimmedName, 0, startRatio);
            patientAnimator.speed = 0f;

            // ★ 두 번째 환자 모델도 동기화
            if (secondaryPatientAnimator != null)
            {
                secondaryPatientAnimator.Play(trimmedName, 0, startRatio);
                secondaryPatientAnimator.speed = 0f;
            }
            ChunaLogger.Log($"<color=green>[Animation] 동기화 모드 시작: '{trimmedName}' (시작 프레임 {startRatio:P0}, speed=0)</color>");
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

        // 손바닥 지지 모드: conditionParams에 "palmSupport"가 있으면 손가락 마디 판정을 빼고
        // 손바닥 평면·위치만 본다(흉추 굴곡처럼 받쳐주기만 하면 되는 단계). 없으면 원래 판정으로 복원.
        palmSupportMode = subStep != null && !string.IsNullOrEmpty(subStep.conditionParams) &&
                          subStep.conditionParams.ToLower().Contains("palmsupport");
        if (poseComparator != null)
        {
            poseComparator.SetPalmSupportMode(palmSupportMode);
        }

        // StartHold만 체크 모드 (등척성운동 등)
        // 1. 핸드데이터 파일명에 "등척성" 포함 시 자동 활성화
        // ※ conditionParams의 'key=값' 토큰을 읽는다(없으면 defaultValue).
        //   ';'로 여러 토큰이 오므로 통짜 파싱하면 안 된다.
        static float ParseNamedFloat(SubStepData s, string key, float defaultValue)
        {
            if (s == null || string.IsNullOrEmpty(s.conditionParams)) return defaultValue;
            foreach (string tok in s.conditionParams.Split(';'))
            {
                string t = tok.Trim();
                if (!t.StartsWith(key, System.StringComparison.OrdinalIgnoreCase)) continue;
                if (float.TryParse(t.Substring(key.Length), System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out float v))
                    return v;
            }
            return defaultValue;
        }

        // 2. conditionParams에 "startHoldOnly" 포함 시 활성화
        bool isIsometricExercise = subStep != null && !string.IsNullOrEmpty(subStep.handTrackingFileName) &&
            subStep.handTrackingFileName.Contains("등척성");
        bool hasStartHoldOnlyParam = subStep != null && !string.IsNullOrEmpty(subStep.conditionParams) &&
            subStep.conditionParams.ToLower().Contains("startholdonly");

        startHoldOnlyFromParams = hasStartHoldOnlyParam;

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

            // ★startHold=0.5 → 이 단계만 시작 홀드를 짧게(2026-08-13).
            //   왕복을 여러 번 반복하는 단계(제2늑골 팔 올리고 내리기 3회)는 방향마다 3초를 붙잡으면
            //   "붕붕 왔다갔다"가 안 된다 — 시작 정렬만 확인하고 바로 이동으로 넘어가야 한다.
            //   토큰이 없으면 종전대로 3초라 기존 시나리오에는 영향이 없다.
            float sh = ParseNamedFloat(subStep, "starthold=", -1f);
            if (sh >= 0f) startHoldDuration = sh;

            ChunaLogger.Log($"<color=cyan>[ChunaPathEvaluator] 일반 모드 - startHoldDuration: {startHoldDuration}초</color>");
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
        // ★reach=0.9 → 이 단계는 진행률 90%까지 실제로 가야 완료된다(2026-08-13).
        //   기본 완료 임계점은 config의 midHoldStart(=30%)라, 왕복 동작에서 손을 조금만 움직여도
        //   "갑자기 다음으로 점프"했다(사용자 지적). 토큰이 없으면 종전 동작 그대로다.
        // ★reps=3 → 한 단계 안에서 궤적 도달을 3번 세고 나서 완료한다(2026-08-13).
        //   단계를 올리기/내리기로 쪼개지 않고 "왔다갔다 3회"를 한 단계에서 카운트하기 위한 것.
        repsRequired = Mathf.Max(1, Mathf.RoundToInt(ParseNamedFloat(subStep, "reps=", 1f)));
        repsDone = 0;

        // ★touch=6 → 시작 구간·끝 구간을 번갈아 찍은 횟수만 센다(홀드·진행률 완료 안 탄다).
        //   zone=0.15 로 구간 폭(여유값)을 조절한다.
        touchTarget = Mathf.RoundToInt(ParseNamedFloat(subStep, "touch=", 0f));
        touchCount = 0;
        lastZone = 0;
        touchSeeded = false;
        touchZone = Mathf.Clamp(ParseNamedFloat(subStep, "zone=", 0.15f), 0.02f, 0.45f);
        if (touchTarget > 0)
            ChunaLogger.Log($"<color=yellow>[ChunaPathEvaluator] 구간 터치 카운트 모드 — 목표 {touchTarget}회, " +
                            $"구간 폭 {touchZone:P0} (홀드·임계 완료 없음)</color>");

        reachOverride = ParseNamedFloat(subStep, "reach=", -1f);
        if (reachOverride >= 0f)
            ChunaLogger.Log($"<color=yellow>[ChunaPathEvaluator] 도달 임계점 재정의: 진행률 {reachOverride:P0}까지 가야 완료</color>");

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
            // ★blend= 토큰(초): AutoPlay일 때 앞 자세와 섞어 넘어간다. 0이면 종전대로 즉시 전환.
            SetPatientAnimation(animStateName, mode, ParseNamedFloat(subStep, "blend=", 0f));
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
    /// <param name="gated">true면 보조수 접촉으로 재생 게이팅 (PassiveStretch)</param>
    /// <param name="latchGate">true면 최초 접촉만으로 끝까지 재생 (CSV conditionParams=touchOnce)</param>
    /// <param name="requireBothHands">true면 양손이 각각 닿아야 진행 (CSV conditionParams=bothHands)</param>
    public void StartAutoPlay(float duration = 0f, bool gated = false, bool latchGate = false, bool requireBothHands = false)
    {
        autoPlayHandler.StartAutoPlay(duration, gated, latchGate, requireBothHands);

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

        // duration 파싱 시도
        float duration = subStep.duration > 0 ? subStep.duration : 0f;

        // PassiveStretch: 보조수 접촉 게이팅
        bool gated = !string.IsNullOrEmpty(subStep.conditionType) &&
                     subStep.conditionType.Trim().Equals("PassiveStretch", System.StringComparison.OrdinalIgnoreCase);

        // ★게이트가 있는 단계는 '접촉하기 전'에 새 클립의 0프레임을 씌우면 안 된다.
        //   그러면 손을 대기도 전에 앞 동작의 마지막 자세가 풀려 환자가 초기 자세로 돌아간다.
        //   → 상태 이름만 잡아 두고 실제 재생은 게이트가 처음 열릴 때 시작한다.
        deferAnimationUntilGateOpen = gated;

        // 애니메이션 설정
        SetPatientAnimationFromSubStep(subStep);

        // conditionParams에 "touchOnce"가 있으면 최초 접촉으로 래치 (손을 계속 대고 있지 않아도 끝까지 재생)
        string prms = subStep.conditionParams != null ? subStep.conditionParams.ToLower() : "";
        bool latchGate = prms.Contains("touchonce");
        // "bothHands"면 양손이 각각 닿아야 게이트가 열린다 (양손 파지)
        bool requireBothHands = prms.Contains("bothhands");

        deferAnimationUntilGateOpen = false;   // 클립이 없어 소비되지 않은 경우 대비

        // AutoPlay 시작
        StartAutoPlay(duration, gated, latchGate, requireBothHands);
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
    /// 현재 SkipMidHold 모드인지 확인 (대흉근/흉쇄유돌근 등 홀드 판정 없는 직선 가동범위)
    /// </summary>
    public bool IsSkipMidHold => skipMidHold;

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
    /// 현재 홀드 시작 비율 반환 (evaluator 판정용 — 스트레칭은 상대값)
    /// </summary>
    public float CurrentMidHoldStart => currentMidHoldStart;

    /// <summary>
    /// 현재 홀드 종료 비율 반환 (evaluator 판정용 — 스트레칭은 상대값)
    /// </summary>
    public float CurrentMidHoldEnd => currentMidHoldEnd;

    /// <summary>
    /// 디스플레이용 홀드 시작 비율 (스트레칭도 절대값 — 재평가와 동일 좌표계)
    /// </summary>
    public float DisplayMidHoldStart => isStretchingMode ? stretchingHoldStart :
                                        (isExtendedLimitMode && !isRotationStep ? extendedMidHoldStartRatio : midHoldStartRatio);

    /// <summary>
    /// 디스플레이용 홀드 종료 비율 (스트레칭도 절대값 — 재평가와 동일 좌표계)
    /// </summary>
    public float DisplayMidHoldEnd => isStretchingMode ? stretchingEnd :
                                      (isExtendedLimitMode && !isRotationStep ? extendedMidHoldEndRatio : midHoldEndRatio);

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

        // ★ 가이드 시작 위치 미리 설정 (SetPatientAnimation 호출 시점에 currentStartRatio가 올바른 값이 되도록)
        // StartEvaluation에서도 동일하게 재설정되지만, 미리 세팅해야 SetPatientAnimation의 첫 Play가 올바른 프레임에서 시작
        if (isStretchingMode)
        {
            runtimeGuideStartRatio = stretchingStart;
        }
        else
        {
            runtimeGuideStartRatio = 0f;
        }
    }

    /// <summary>
    /// ScenarioConfig에서 평가 임계점 오버라이드 적용
    /// </summary>
    public void ApplyEvaluationThresholds(ScenarioConfig config)
    {
        if (modeConfigurator == null) return; // Bootstrapper가 Awake보다 먼저 호출 시 안전 스킵
        modeConfigurator.ApplyEvaluationThresholds(config);
    }

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
    /// 회전 측정용 추출 벡터 반환 (감지 축에 수직인 벡터)
    /// </summary>
    private Vector3 GetRotationMeasurementVector() => modeConfigurator.GetRotationMeasurementVector();

    /// <summary>
    /// 대체 측정 벡터 사용 설정 (누운 환자 오버라이드 시 활성화)
    /// </summary>
    public void SetUseAlternateMeasurementVector(bool use) => modeConfigurator.SetUseAlternateMeasurementVector(use);

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
        if (contactTargetIndicator != null)
        {
            contactTargetIndicator.Dispose();
            contactTargetIndicator = null;
        }
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

        // 가이드 frame 좌표 변환 기준을 PoseComparator에 주입
        if (poseComparator != null && referenceTransform != null)
            poseComparator.SetReferencePoint(referenceTransform);

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

    /// <summary>난이도 프리셋(가이드손 표시·투명도·홀드시간)을 <b>지금</b> 반영한다.
    ///
    /// ★왜 필요한가 (2026-08-18 실측): 이 동기화는 여태 <see cref="StartEvaluation"/> 안에서만 돌았다.
    /// 그런데 두개골·늑골·흉추(cranial*) 단계는 평가 파이프라인을 <b>일부러 켜지 않는다</b>
    /// (08-11에 같이 켰다가 파지 게이트가 무너진 전례) → 그 술기들에서는 난이도가 한 번도 적용되지 않아
    /// <b>평가·상급 모드에서도 가이드손이 그대로 나왔다</b>(씬 직렬화 값 showGuideHands=1).
    /// ScenarioManager가 substep 진입 때 불러 준다.</summary>
    public void SyncDifficultyNow() => dataLoader.SyncWithDifficultySettings();

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
        // ★ 녹화가 로드되지 않은 채 평가를 시작하면 유사도 샘플이 하나도 쌓이지 않아 채점이 0이 된다.
        //   원인(파일 없음/이름 오타)이 콘솔에서 바로 보이도록 여기서 한 번 더 알린다.
        if (loadedFrames == null || loadedFrames.Count == 0)
            ChunaLogger.LogWarning(
                $"[ChunaPathEvaluator] 손 녹화 프레임 0개로 평가 시작 ('{CurrentProcedureName}') — " +
                "판정·채점이 나오지 않습니다. Resources/HandPoseData에 파일이 있는지 확인하세요.");

        // ★ 이전 가이드 핸드 재생 중지 + 프레임 인덱스 리셋 (잔류값 방지)
        StopGuideHandPlayback();
        guidePlaybackController.ResetFrameIndex();

        // ★ AutoPlay 모드 리셋 (이전 SubStep에서 남아있을 수 있음)
        autoPlayHandler.Reset();

        // ★ 스트레칭/재평가 모드에 따라 가이드 범위 설정 (운동 종류 무관)
        // ★ 재평가: 회전 단계는 일반 모드 그대로(가동범위 고정), 위치 단계만 확장
        if (isStretchingMode)
        {
            runtimeGuideStartRatio = stretchingStart;
            runtimeGuideEndRatio = stretchingEnd;
            ChunaLogger.Log($"<color=yellow>[StartEval] 스트레칭 모드 - 가이드 범위: {stretchingStart:P0}~{stretchingEnd:P0}</color>");
        }
        else if (isExtendedLimitMode)
        {
            runtimeGuideStartRatio = 0f;
            // 회전 단계는 적정범위 고정 → 가이드도 일반 종료점 사용
            runtimeGuideEndRatio = isRotationStep ? midHoldEndRatio : extendedMidHoldEndRatio;
            ChunaLogger.Log($"<color=yellow>[StartEval] 재평가 모드 - 가이드 범위: 0~{runtimeGuideEndRatio:P0} ({(isRotationStep ? "회전(고정)" : "위치(확장)")})</color>");
        }

        // ★ 난이도 프리셋에서 가이드 핸드/투명도 동기화
        dataLoader.SyncWithDifficultySettings();

        // ★ 회전 기반이면 감지 축 기준으로 handDataTotalRotation 재계산
        //   (CSV 로드 시에는 Quaternion.Angle(3D 총 회전)로 계산했지만,
        //    실제 측정은 단일 축 SignedAngle이므로 목표값도 같은 방식이어야 함)
        if (!isPositionBasedMovement && loadedFrames.Count >= 2)
        {
            var firstFrame = loadedFrames[0];
            var lastFrame = loadedFrames[loadedFrames.Count - 1];
            Vector3 axis = GetRotationDetectionAxis();
            Vector3 mVec = GetRotationMeasurementVector();

            // frame rotation은 referenceTransform 기준 로컬이므로 refRot로 월드 방향 복원 (axis는 월드 기준).
            Quaternion refRot = referenceTransform != null ? referenceTransform.rotation : Quaternion.identity;

            // 양손 중 더 큰 회전 사용
            Vector3 rightRefDir = refRot * (firstFrame.rightRootRotation * mVec);
            Vector3 rightCurDir = refRot * (lastFrame.rightRootRotation * mVec);
            float rightAxisRot = Mathf.Abs(Vector3.SignedAngle(rightRefDir, rightCurDir, axis));

            Vector3 leftRefDir = refRot * (firstFrame.leftRootRotation * mVec);
            Vector3 leftCurDir = refRot * (lastFrame.leftRootRotation * mVec);
            float leftAxisRot = Mathf.Abs(Vector3.SignedAngle(leftRefDir, leftCurDir, axis));

            float axisBasedTotal = Mathf.Max(rightAxisRot, leftAxisRot);
            if (axisBasedTotal > 1f)
            {
                handDataTotalRotation = axisBasedTotal;
                if (showDebugLogs)
                    ChunaLogger.Log($"<color=magenta>[StartEval] 축 기반 목표 회전 재계산: {axisBasedTotal:F1}° (축:{axis}, 측정벡터:{mVec})</color>");
            }
        }

        isEvaluating = true;
        evaluationStartTime = Time.time;
        lastMetricsRecordTime = Time.time;
        scoringEngine.Reset();

        // 새로운 평가 흐름 초기화 (via phaseManager)
        Vector3 initLeftPos = playerLeftHand != null ? playerLeftHand.transform.position : Vector3.zero;
        Vector3 initRightPos = playerRightHand != null ? playerRightHand.transform.position : Vector3.zero;
        phaseManager.Initialize(initLeftPos, initRightPos);
        currentPhase = EvaluationPhase.WaitingForStart;
        leftHandStartHoldPosition = Vector3.zero;
        isometricHoldEntryTime = -1f; // 등척성 홀드 완수도 계산용 리셋

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

            // ★startHoldOnly(유지형 술기)는 StartHold가 끝나는 즉시 Completed로 빠지므로
            //   EvaluationPhaseManager의 StartGuideHandPlaybackInternal() 호출을 아예 타지 못한다
            //   (EvaluationPhaseManager.cs:205-218이 :227 앞에서 return) → 지금까지 정지 첫 프레임만 떴다.
            //   ★재생은 **클립 전체를 1회, 루프 없음**(사용자 지시) — 두개골 경로와 동일한 규약.
            //   ★구간을 (0,1)로 **명시**해야 한다. runtimeGuideStartRatio/EndRatio는 스트레칭·재평가
            //     모드에서만 갱신되고(위 :1971-1984), 그 외 단계에서는 기본값 0~0.4가 남는다 →
            //     currentStartRatio/currentEndRatio를 그냥 넘기면 앞 40%만 재생되고 끊긴다.
            //   ※CSV conditionParams로 켠 경우만 — 파일명 "등척성" 자동 감지로 켜지는
            //     기존 단순추나 등척성운동은 지금까지의 정지 프레임 표시를 그대로 둔다.
            if (startHoldOnlyFromParams && loadedFrames != null && loadedFrames.Count > 0)
                StartGuideHandPlayback(0f, 1f, false);
            else
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

        // ★세션 없이 끝나는 경우가 있다 — StartAutoPlay는 isEvaluating만 켜고 세션은 만들지 않는다
        //   (세션은 StartEvaluation에서만 생성). 그대로 두면 AutoPlay가 끝나는 순간
        //   여기서 NullReference가 나고, Update에서 터지므로 그 뒤 로직이 통째로 죽어
        //   "유지해도 단계가 안 넘어간다"가 된다(2026-08-12).
        if (currentSession == null)
        {
            ChunaLogger.Log("<color=yellow>[ChunaPathEvaluator] 평가 세션 없이 완료 — 점수 계산을 건너뜁니다 " +
                            "(AutoPlay 전용 단계).</color>");
            return null;
        }

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
            // 등척성운동: 40점 블록을 홀드 완수도로 계산 (요구 홀드시간 / 실제 StartHold 소요시간)
            // 흔들림·접촉 끊김 없이 한 번에 잘 버틸수록 소요시간이 짧아져 만점에 가까워진다.
            currentSession.isIsometric = startHoldOnly;
            if (startHoldOnly)
            {
                float spent = (isometricHoldEntryTime >= 0f) ? (Time.time - isometricHoldEntryTime) : startHoldDuration;
                currentSession.holdQuality = (spent > 0.01f) ? Mathf.Clamp01(startHoldDuration / spent) : 1f;
            }

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
            leftSim, rightSim, limitChecker, leftHandPos, rightHandPos,
            isLeftHandTouchingPatient);

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
        // isRotationStep(양손 모드)이면 기존 양손 가중 평균 유지, 단손이면 주동수+보조수 분리 채점
        scoringEngine.CalculateAverageSimilarity(currentSession, leftHandSimilarityWeight, rightHandSimilarityWeight, isRotationStep);
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
        => StartGuideHandPlayback(currentStartRatio, currentEndRatio, loopGuideHands);

    /// <summary>재생 구간·루프를 인자로 받는 버전. 인스펙터/런타임 필드를 바꾸지 않으므로
    /// 특정 술기(두개골)만 다른 설정으로 재생해도 다음 시나리오에 설정이 새지 않는다.</summary>
    private void StartGuideHandPlayback(float startRatio, float endRatio, bool loop, string clipName = null)
    {
        if (!showGuideHands || guideSuppressedForStep) return;
        if (loadedFrames == null || loadedFrames.Count == 0) return;

        StopGuideHandPlayback();          // ★여기서 유지 상태가 지워지므로

        guidePlayingClip = clipName;      // ★그 뒤에 이번 클립 이름을 심는다(순서 중요)
        guideHeldEndRatio = endRatio;

        IEnumerator routine = guidePlaybackController.PlaybackRoutine(
            loadedFrames, leftGuideHand, rightGuideHand,
            startRatio, endRatio,
            guidePlaybackSpeed, loop, loopDelaySeconds,
            guideHandColor, showDebugLogs);

        guideHandCoroutine = StartCoroutine(PlayThenHold(routine, loop));

        if (showDebugLogs)
            ChunaLogger.Log("[ChunaPathEvaluator] 가이드 핸드 재생 시작");
    }

    /// <summary>재생이 끝나면 <b>마지막 자세 그대로 남긴다</b>(숨기지 않는다).
    /// ★중첩 StartCoroutine을 쓰지 않고 직접 돌린다 — 그래야 StopCoroutine 한 번으로 확실히 멈춘다.</summary>
    private IEnumerator PlayThenHold(IEnumerator routine, bool loop)
    {
        while (routine.MoveNext()) yield return routine.Current;

        guideHandCoroutine = null;
        if (loop) yield break;

        // 마지막 프레임은 재생 코루틴이 이미 그려 둔 상태다 — 지우지 않고 '유지 중'으로만 표시한다.
        MarkGuideHeld();
    }

    private void ShowGuideHandFirstFrame()
    {
        // ★이 단계가 가이드손을 쓰지 않기로 하고 숨겨 뒀으면 다시 그리지 않는다(2026-08-13).
        //   평가가 시작될 때마다 첫 프레임을 그려서, 손 녹화가 없는 단계에서 숨겨 둔 가이드손이
        //   환자를 터치하는 순간 되살아났다(사용자: "머리 터치하니까 다시 생기고 안 사라진다").
        if (guideHiddenByContact) return;
        if (guideSuppressedForStep) return;   // noGuide 단계 — 평가가 시작돼도 첫 프레임을 그리지 않는다

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

        // 완전 정지 = 유지 상태도 버린다(다음에 같은 클립이 오면 처음부터 다시 재생한다).
        guideHeldClip = null;
        guidePlayingClip = null;
        guideHiddenByContact = false;

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