// ★시험 전용(0단계) · 2026-10-22
//   목적: 씬 뷰에서 목표 구를 끌면 환자 팔이 2관절 IK로 따라가는지만 본다.
//   범위 밖: 손 파지로 목표 잡기 · 판정 · 끝느낌 진동 · 놓으면 복귀 · 어깨 원뿔 한계 · 중심선 클램프.
//   붙이는 법: Play 중 메뉴 GuideChuna/시험/환자 팔 IK 0단계 (PatientArmIkProbeMenu).
//   씬·프리팹에 올리지 않는다. 런타임에 붙으므로 Play를 멈추면 사라지고, 인스펙터 튜닝도 저장되지 않는다.
//   기존 공유 컴포넌트(ScenarioManager·ConditionManager 등)를 부르지도 고치지도 않는다.
//
// ── 1단계 이후 설계 메모(구현 안 함) ──
//   · 목표 = 사용자 손(파지 중일 때만). 손 떨림은 목표 쪽에서 평활화한다(IK 안에서 하지 않는다).
//   · 어깨 원뿔 한계: S공간에서 (팔꿈치 − 어깨) 방향을 기준 방향 원뿔 안으로 자른 뒤 IK를 푼다.
//   · 중심선 클램프: 몸통 중앙면(정중면)의 어느 쪽인지 «부호»가 필요하다. c8 음수 스케일 때문에
//     본 이름으로 좌우를 정하지 말고 rom-frame-verify처럼 재는 도구로 먼저 정한다(CLAUDE.md 규칙 9).
//   · 끝느낌: 원뿔 경계 근처에서 목표와 실제 손 사이 거리로 진동·저항 표시를 낸다.
//   · 복귀: 놓으면 weight를 1 → 0으로 내린다(애니가 매 프레임 덮어쓴다는 전제. 아래 «멱등성» 참고).
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 환자 팔(위팔 → 아래팔 → 손)에 해석적 2관절 IK를 LateUpdate에서 덧얹는다.
/// <see cref="CervicalRomDriver"/>와 같은 결 — Animator가 포즈를 쓴 뒤 뼈를 직접 돌린다.
///
/// ★음수 스케일 처리 (2026-10-22 실측 근거)
///   환자 c9/c8 중 <b>c8의 localScale X가 −1.1</b>이고 팔 체인 전체가 그 밑이다
///   (bone_axes.py: CC_Base_R_Upperarm·Forearm·Hand 누적스케일 (−1.1, 1.1, 1.1)).
///   Unity의 Transform.rotation은 스케일 부호를 무시하고 로컬 회전을 곱한 값이라,
///   거울 밑에서 <c>bone.rotation = q * bone.rotation</c>으로 «월드 q»를 주면 실제 화면에서는
///   거울에 비친 회전(축이 반사되고 방향이 뒤집힌 회전)이 된다.
///   CervicalRomDriver.AxisOf에 «부호는 Play에서 눈으로 확인해 뒤집었다(08-24)»가 남아 있는 것이 그 흔적이다.
///   → 그래서 여기서는 월드가 아니라 <b>위팔의 부모 공간(S)</b>에서 푼다.
///     · 위치는 S.InverseTransformPoint로 옮긴다 — 행렬 전체(거울 포함)를 역으로 거치므로 정확하다.
///     · 회전은 <b>localRotation에만</b> 곱한다(<see cref="RotateInS"/>). 월드 회전(bone.rotation)은 읽지도 쓰지도 않는다.
///       S 아래(위팔·아래팔·손)에 음수 스케일이 없으면 이 공간에는 거울이 없다 — 시작 때 검사하고, 있으면 멈춘다.
///   ★10-22 Play 실측으로 정정: 처음엔 rel = Inverse(S.rotation) * bone.rotation → bone.rotation = S.rotation * (q * rel)로
///     «월드 회전을 거쳐» 넣었는데, 사용자가 «목표를 움직이는 것과 반대로 움직인다»고 했고 로그도 어긋났다
///     (목표가 팔 길이 밖인데 팔꿈치 66~77° — 쭉 펴져 180°에 가까워야 한다). 거울 밑에서 월드 회전이
///     위치(InverseTransformPoint)와 다른 규칙으로 계산되는 것으로 보고(추정), 위치와 같은 로컬 사슬만 쓰도록 바꿨다.
///   이렇게 하면 거울이 S 위 어디에 있든(몇 개든) 방향 부호를 추론할 필요가 없다.
///
/// ★멱등성: 해는 «목표 위치로 가는 최소 회전»이라, Animator가 그 프레임에 팔을 덮어쓰지 않아도
///   weight=1이면 같은 자세로 수렴한다(누적 폭주 없음). weight&lt;1이면 덮어쓰지 않는 경우 매 프레임
///   조금씩 더 가서 결국 weight=1처럼 된다 — 0단계에서는 로그로만 확인한다.
/// </summary>
[DefaultExecutionOrder(10000)] // 다른 LateUpdate(목뼈 드라이버 등)가 끝난 뒤에 얹는다
public class PatientArmIkProbe : MonoBehaviour
{
    public enum ArmSide { Right = 0, Left = 1 }

    [Tooltip("어느 팔인가(Humanoid 기준). 제2늑골 클립이 움직이는 쪽은 Right다(2026-10-22 클립 커브 실측). 시작 때만 읽는다.")]
    [SerializeField] private ArmSide side = ArmSide.Right;

    [Tooltip("IK 가중치. 0 = 애니 그대로, 1 = 목표에 완전히 붙음.")]
    [Range(0f, 1f)]
    [SerializeField] private float weight = 1f;

    [Tooltip("손 목표. 비어 있으면 Play 시작 때 손 위치에 빨간 구를 만든다.")]
    [SerializeField] private Transform target;

    [Tooltip("팔꿈치가 향할 쪽. 비어 있으면 Play 시작 때 지금 팔꿈치 바깥쪽에 파란 구를 만든다.")]
    [SerializeField] private Transform hint;

    [Tooltip("위팔을 제 축으로 비틀어 팔꿈치 굽힘면을 힌트 쪽에 맞춘다. 끄면 아래팔이 경첩이 아닌 방향으로도 꺾일 수 있다.")]
    [SerializeField] private bool alignElbowTwist = true;

    // ── 관절 한계(10-22 사용자 지시: «사람 실제 관절 부위별 한계» — 팔이 너무 뒤까지 돌아간다) ──
    //   값 = 교과서 정상 관절가동범위(AAOS). 어깨 굴곡·외전 180°는 구(球) 전체라 따로 자르지 않는다.
    //   방향(앞·위·바깥)은 본 이름이 아니라 뼈 위치로 잰다 — c8 음수 스케일 때문(CLAUDE.md 규칙 9). 기즈모 화살표로 확인한다.
    [Header("관절 한계 (AAOS 정상 가동범위)")]
    [Tooltip("관절 한계를 건다. 끄면 0단계처럼 손을 목표에 붙이기만 한다.")]
    [SerializeField] private bool applyLimits = true;

    // ── 어깨 복합체(10-22 해부학 조사 명세대로 · 사용자 선택 A) ──
    //   근거 문서: D:/CoCoChuna_환자팔_해부학적움직임_명세_어깨_V0.1_20261022
    //   ★한계는 «위팔–몸통» 각으로 건다(견갑골 몫 포함 — 견갑골 본이 없어 따로 돌리지 않는다). 보이는 쇄골은 실측만큼만 움직인다(A).
    //   ★10-22 틀렸던 것(덧댐 6ee1c32): 쇄골 30° 거상(1944 2D 추정 — 3D 실측 6~10) · 앞 · 옆 최대 거상 같게(150) · 팔 내림에서 외회전 90 ·
    //     Inui 27°를 «최소 외회전»으로 강제(실은 거상 중 최대값) — 전부 아래 값으로 바꿨다.
    [Tooltip("앞으로 든 최대 거상(°, 수동). Inui 2009 표1 156(원문 확인) · CDC 20~44세 169~172.")]
    [Range(90f, 180f)]
    [SerializeField] private float shoulderFlexionMax = 156f;

    [Tooltip("가장 높이 드는 방향의 최대 거상(°). 그 방향 = 견갑면보다 23° 앞(Browne 1990 사체 · 초록) ≈ 앞에서 37°. 값 158은 ★추정(앞 156 · 옆 146 사이 꼭대기).")]
    [Range(90f, 180f)]
    [SerializeField] private float shoulderPeakElevationMax = 158f;

    [Tooltip("옆으로 든 최대 거상(°, 수동). Inui 2009 144~147(원문 확인) — 앞보다 낮다.")]
    [Range(90f, 180f)]
    [SerializeField] private float shoulderAbductionMax = 146f;

    [Tooltip("어깨 신전 — 팔꿈치를 굽혔을 때 팔 내림에서 관상면 뒤로(°). AAOS 60(2차) · JOA 50(원표 확인).")]
    [Range(0f, 90f)]
    [SerializeField] private float shoulderExtensionMax = 60f;

    [Tooltip("팔꿈치를 편 채 어깨 신전(°) — 이두근 장두가 당겨 줄어든다(원리만 확인 · 50은 ★추정).")]
    [Range(0f, 90f)]
    [SerializeField] private float shoulderExtensionStraightElbow = 50f;

    [Tooltip("수평(90°)에서 관상면 뒤로(수평 신전 · °). AAOS 45(학습 자료) · JOA 30(원표, 견갑대 포함). 제2늑골 영상 자세는 약 40(계산) = 끝범위라 45를 쓴다.")]
    [Range(0f, 90f)]
    [SerializeField] private float shoulderHorizontalAbductionMax = 45f;

    [Tooltip("수평(90°)에서 정중면 넘어 몸 앞 안쪽으로(°). JOA 수평 굴곡 135 → 45(원표 확인).")]
    [Range(0f, 90f)]
    [SerializeField] private float shoulderHorizontalAdductionMax = 45f;

    [Tooltip("팔 내림에서 안쪽(내전 · °). AAOS 30(2차) · JOA 0(몸통이 막음) — 몸통 막힘이 따로 있어 30.")]
    [Range(0f, 90f)]
    [SerializeField] private float shoulderAdductionMax = 30f;

    [Tooltip("뒤-안쪽(손을 등 뒤 허리로) 방향 최대 거상(°). 40은 ★추정(문헌 숫자 못 찾음).")]
    [Range(0f, 90f)]
    [SerializeField] private float shoulderBehindMedialMax = 40f;

    [Header("보이는 쇄골(견갑상완 리듬 · 사용자 선택 A — 실측만큼만)")]
    [Tooltip("팔을 들 때 쇄골 본을 실측만큼 움직인다(거상 · 후인). 견갑골 상방 회전(39~50°)은 보이게 돌리지 않는다 — 한계에 이미 들어 있다.")]
    [SerializeField] private bool scapularRhythm = true;

    [Tooltip("쇄골 거상 최대(°). Ludewig 2009 · McClure 2001 골핀 3D 6~10. 팔 거상 30° 뒤로 거상 90°마다 7°씩(θ120 → 7 · 합격 5~8).")]
    [Range(0f, 30f)]
    [SerializeField] private float girdleElevationMax = 10f;

    [Tooltip("쇄골 후인 최대(°). Ludewig · McClure 16~21. 팔 거상 30° 뒤로 120°마다 20°.")]
    [Range(0f, 30f)]
    [SerializeField] private float girdleRetractionMax = 20f;

    [Header("위팔 회전(거상에 따라)")]
    [Tooltip("팔 내림 외회전(°). JOA 60(원표).")]
    [Range(0f, 120f)]
    [SerializeField] private float shoulderExternalRotAtSide = 60f;

    [Tooltip("팔 내림 내회전(°). JOA 80(원표).")]
    [Range(0f, 120f)]
    [SerializeField] private float shoulderInternalRotAtSide = 80f;

    [Tooltip("머리 위(최대 거상)에서 남는 내회전(°). ★추정 30 — Browne: 최대 거상엔 외회전 쪽(약 35)이 필요하다. 강제 최소 외회전은 두지 않는다(Inui 27은 «거상 중 최대»라 최소가 아니다).")]
    [Range(0f, 90f)]
    [SerializeField] private float shoulderInternalRotOverhead = 30f;

    [Tooltip("90° 외전에서 내회전(°). AAOS 65~70(2차). 팔 내림 값에서 90°까지 직선으로 옮겨 간다.")]
    [Range(0f, 120f)]
    [SerializeField] private float shoulderInternalRotMax = 70f;

    [Tooltip("90° 외전 이상에서 외회전(°). AAOS 90(2차).")]
    [Range(0f, 120f)]
    [SerializeField] private float shoulderExternalRotMax = 90f;

    [Tooltip("팔꿈치 굴곡 최대 각(°). 145 — JOA · CDC 범위 안(D:/CoCoChuna_환자팔_해부학적움직임_명세_팔꿈치손목_V0.1_20261022). 과신전은 0.")]
    [Range(0f, 170f)]
    [SerializeField] private float elbowFlexionMax = 145f;

    [Tooltip("팔꿈치 운반각(°) — 편 팔에서 아래팔이 바깥으로 벌어진다. 평균 12.9(남 11 · 여 15, Paraskevas 2004). 굽힐수록 준다(Morrey & Chao 1976 — 줄어드는 모양은 ★직선 추정).")]
    [Range(0f, 25f)]
    [SerializeField] private float elbowCarryingAngle = 12.9f;

    // ── 10-22 사용자 지시(제2늑골 상방변위 영상): 팔은 펴진 채 침대 모서리 밖 아래로 · 손목을 잡아 젖혀 외회전 유도 ──
    [Header("팔꿈치 굽는 쪽")]
    [Tooltip("켜면 팔꿈치가 직전 자리에서 이어서 굽는다 — 손목을 움직여도 팔꿈치가 휘돌지 않는다. 끄면 파란 힌트 구 쪽으로 굽는다(0단계 동작).")]
    [SerializeField] private bool poleFromPreviousElbow = true;

    [Header("잡은 손의 회전 (목표 구를 돌리면)")]
    [Tooltip("목표 구의 회전(잡은 뒤 바뀐 만큼)을 ① 팔이 펴졌을 때 위팔 비틀기(외·내회전) ② 손목 꺾기로 옮긴다.")]
    [SerializeField] private bool followTargetRotation = true;

    [Tooltip("아래팔 회외 최대(°) — ★해부학 중립(엄지 위) 기준. CDC · JOA 85~91. 시작 자세의 회전은 아바타 근육값으로 재서 뺀다. 손을 비튼 각은 아래팔이 먼저 받고 넘는 만큼 어깨로(이 분담은 ★추정 — 직접 근거 없음).")]
    [Range(0f, 120f)]
    [SerializeField] private float forearmSupinationMax = 90f;

    [Tooltip("아래팔 회내 최대(°) — 해부학 중립 기준. CDC · JOA 77~90.")]
    [Range(0f, 120f)]
    [SerializeField] private float forearmPronationMax = 85f;

    [Header("손목(방향마다 다르다 — 사분면 타원)")]
    [Tooltip("배굴(손등 쪽, °). JOA · AAOS 70.")]
    [Range(0f, 90f)] [SerializeField] private float wristExtensionMax = 70f;
    [Tooltip("장굴(손바닥 쪽, °). JOA · AAOS 80~90.")]
    [Range(0f, 90f)] [SerializeField] private float wristFlexionMax = 85f;
    [Tooltip("요측 편위(엄지 쪽, °). JOA 25 · AAOS 20.")]
    [Range(0f, 60f)] [SerializeField] private float wristRadialMax = 20f;
    [Tooltip("척측 편위(새끼 쪽, °). AAOS 30 · JOA 55 — 시뮬 합격 35.")]
    [Range(0f, 60f)] [SerializeField] private float wristUlnarMax = 35f;
    // 대각선 방향은 사분면 타원으로 잇는다(Li 2005 «달걀 모양» — 대각선 숫자는 ★미확보)

    [Header("막힘 (침대 · 몸통) — 물리 엔진이 아니라 IK 안에서 밀어낸다")]
    [Tooltip("팔꿈치 · 손이 침대 상자와 몸통 캡슐을 뚫지 못하게 밀어낸다.")]
    [SerializeField] private bool applyCollision = true;

    [Tooltip("팔 굵기 반지름(m). 이만큼 떨어진 자리까지 밀어낸다.")]
    [SerializeField] private float armRadius = 0.04f;

    [Tooltip("몸통 캡슐 반지름 = 양 어깨 사이 절반 × 이 값. 기즈모 초록 캡슐이 몸통에 맞게 조절한다.")]
    [SerializeField] private float torsoRadiusScale = 0.8f;

    // 침대 크기 · 높이는 손잡이로 두지 않는다 — 시작 때 씬의 침대 메시를 직접 잰다(10-22 · MeasureBed). 기즈모 노란 선으로 확인한다.

    [Header("움직임 속도")]
    [Tooltip("관절(위팔 · 아래팔 · 손)이 한 프레임에 돌 수 있는 각속도 한계(°/초). 0이면 끈다. " +
             "★10-22 시뮬: 목표가 몸을 뚫고 지나가거나 0.5초에 휘두르면 해가 한쪽에서 반대쪽으로 넘어가 160°/프레임 튀었다. " +
             "값 360은 «시술자가 환자 팔을 옮기는 정도»를 잡은 추정이다(실측 아님).")]
    [SerializeField] private float maxJointSpeed = 360f;

    [Tooltip("자동으로 만드는 구의 지름(m).")]
    [SerializeField] private float handleSize = 0.05f;

    [Tooltip("진행 로그 간격(초).")]
    [SerializeField] private float logInterval = 1f;

    private Animator animator;
    private Transform upper, lower, hand, space;
    private bool ready;
    private float nextLogTime;

    // 로그·기즈모용 마지막 값(매 프레임 float만 갱신한다 — 문자열은 로그 시점에만 만든다)
    private float lastTargetDistance, lastReach, lastElbowAngle;
    private float lastHandErrMm;   // 풀고 난 뒤 실제 손 ↔ 풀이가 노린 손 자리(월드 mm). 맞게 돌면 0에 가깝다(10-22 실측 로그)

    // 몸 기준 축 — 시작 때 뼈 위치로 재서 가슴 본 로컬(행렬 기준, 거울 포함)에 담아 두고 매 프레임 다시 꺼낸다
    private Transform chest;
    private Vector3 frontInChest, upInChest, lateralInChest;
    private bool frameReady;
    // 로그용 마지막 관절 각과 걸린 한계
    private float lastBehindDeg, lastMedialDeg, lastElbowFlexDeg, lastRotDeg;
    private string lastLimitHit = "";   // 걸린 한계가 바뀔 때만 새 문자열(매 프레임 할당 없음)

    // 팔꿈치 이어 굽기 — 직전 프레임 팔꿈치(S공간)와 굽는 쪽
    private Vector3 prevElbowS, prevPoleS;
    private bool hasPrevElbow;

    // 애니가 이 프레임 팔을 덮어썼는지 — 안 덮었으면 기준 자세로 되돌린 뒤 푼다(비틀기 · 손목은 누적되면 안 된다)
    private Quaternion baseUpper, baseLower, baseHand, wroteUpper, wroteLower, wroteHand;
    private bool hasWritten;
    private Quaternion baseClav, wroteClav;

    // 잡은 손의 회전
    private Quaternion targetRot0;
    private Transform finger;                 // 가운뎃손가락 첫 마디 — 손이 가리키는 쪽
    private Vector3 flexRefInUpper;           // 팔꿈치가 굽는 쪽(해부학 굴곡 방향) — 위팔 본 로컬(행렬 기준). 팔이 펴져도 위팔 회전을 잴 수 있다
    private bool flexRefReady;
    private Vector3 dorsalInLower;            // 손등 쪽(배굴 방향) — 아래팔 본 로컬(행렬 기준)
    private bool dorsalReady;
    // 아래팔 회내 · 회외(10-22 사용자 지적: 팔을 들고 손을 돌리면 어깨가 아니라 팔꿈치 아래(아래팔)가 돈다)
    private float supWorldSign;               // 월드에서 «팔꿈치 → 손» 축 둘레 + 회전이 회외면 +1, 회내면 −1 (아바타 근육으로 잼). 0 = 못 잼
    private Transform foreTwist1, foreTwist2; // 비틀림 보조 본(CC_Base_*_ForearmTwist01/02) — 피부가 꼬이지 않게 회전을 나눠 준다
    private Quaternion foreTwist1Rest, foreTwist2Rest;
    private float lastForeDeg;                // 이번 프레임 아래팔 회외(+) · 회내(−) 각 — 해부학 중립 기준
    private float forearmRestSupDeg;          // 시작 자세의 아래팔 회외(+) — 근육값으로 잰다(10-22 명세)
    private Vector3 wristNeutralInLower, wristRadialInLower;   // 손목 중립 손가락 방향 · 요측 — 아래팔 본 로컬
    private bool wristAxesReady;
    private Vector3 carryLateralInUpper;      // 운반각이 벌어지는 «바깥» — 위팔 본 로컬
    private bool carryRefReady;
    private float lastWristExt, lastWristDev;  // 배굴(+)/장굴(−) · 요측(+)/척측(−)
    public float WristExtDeg => lastWristExt;
    public float WristDevDeg => lastWristDev;
    /// <summary>손목 해부학 축(월드): 중립 손가락 방향 · 손등 쪽 · 엄지 쪽. 시뮬레이션이 «순수 배굴 · 순수 편위»로 꺾게 연다.</summary>
    public bool TryWristAxesWorld(out Vector3 neutral, out Vector3 dorsal, out Vector3 radial)
    {
        neutral = dorsal = radial = Vector3.zero;
        if (!wristAxesReady || !dorsalReady) return false;
        neutral = lower.TransformVector(wristNeutralInLower).normalized;
        dorsal = lower.TransformVector(dorsalInLower); dorsal = (dorsal - Vector3.Dot(dorsal, neutral) * neutral).normalized;
        radial = lower.TransformVector(wristRadialInLower); radial = (radial - Vector3.Dot(radial, neutral) * neutral - Vector3.Dot(radial, dorsal) * dorsal).normalized;
        return true;
    }
    private string sideLabel = "";
    private float lastTwistDeg, lastWristDeg;

    // 막힘
    private Transform hipsBone, neckBone, headBone, otherUpper, footL, footR, eyeLBone, eyeRBone;
    private float lastTorsoR, headR;
    private Vector3 torsoA, torsoB, headC;   // 몸통 캡슐 끝(엉덩이 → 어깨 높이 조금 아래) · 머리 구 중심 — 매 프레임 갱신
    // 침대 = 몸 긴 축을 따라 자른 조각마다 윗면 높이 · 좌우 모서리(몸 중심선에서 잰 거리). 시작 때 씬 메시를 레이로 훑어 잰다(10-22).
    private Vector3 bedOrigin, bedLong, bedSide;
    private float bedS0, bedStep;
    private float[] bedTop, bedXMin, bedXMax;
    private bool bedReady, bedMeasured;
    private float rotRest = float.NaN;
    private int lastClampSide;                // 회전 한계: 직전에 자른 쪽(−1 = 외 · +1 = 내 · 0 = 안 자름)        // 시작 자세의 위팔 회전(°) — 펴진 팔 비틀기의 0 기준(절대값으로 맞춘다)
    private int nRestored, nAnimWrote;
    private readonly bool[] touchedBuf = new bool[3];   // 막힘에 닿은 모양(매 프레임 새로 만들지 않는다)        // 기준 자세 되돌림 / 애니가 씀 — 비틀기 누적 원인 확인용 로그(10-22)
    private bool lastReachable;

    // ── 시뮬레이션(PatientArmIkSim)이 읽는 값 — 읽기 전용 ──
    public bool IsReady => ready;
    /// <summary>
    /// 견인 — 켜면 팔꿈치를 편 채로 두고 팔이 목표 쪽을 가리킨다(목표가 팔 길이보다 가까워도 굽히지 않는다).
    /// ★10-22 사용자 선택 A: 손으로 잡고 있는 동안 켠다(PatientArmIkHandGrab). 제2늑골 영상처럼 팔을 편 채 당기는 시술.
    ///   잡은 동안 내 손이 어깨에서 0.21~0.41m(팔 길이 0.58m)라 팔꿈치가 90~136° 굽었고, 그래서 비틀기(펴진 팔에서만)가 어깨 회전으로 안 갔다(로그 실측).
    /// ★10-23: 손 잡기에서는 더 켜지 않는다(사용자 — 실제 몸처럼 손 자리대로 굽어야 한다). 시뮬의 옛 «견인» 단계만 쓴다.
    /// </summary>
    public bool Traction { get; set; }
    public Transform Target => target;
    public Transform UpperBone => upper;
    public Transform LowerBone => lower;
    public Transform HandBone => hand;
    public Transform FingerBone => finger;
    public string SideLabel => sideLabel;
    public float HandErrMm => lastHandErrMm;
    public float ShoulderBehindDeg => lastBehindDeg;
    public float ShoulderMedialDeg => lastMedialDeg;
    public float ShoulderRotDeg => lastRotDeg;
    public float TwistDeg => lastTwistDeg;
    public float WristDeg => lastWristDeg;
    public float ForearmRotDeg => lastForeDeg;   // + 회외 · − 회내(시작 자세 기준)
    /// <summary>
    /// ★10-23 사용자 지시(제2늑골 시연 — «어깨 외회전으로 바꿔서»): 손을 비튼 각을 어깨(위팔 회전)가 먼저 받고, 어깨 한계를 넘는 만큼만 아래팔로.
    /// 기본 false = 10-22 규칙(아래팔 먼저 · 머리 위에서 손을 돌리면 아래팔이 돈다). 술기가 런타임에 켠다 — 직렬화하지 않는다(씬에 굳지 않게).
    /// </summary>
    public bool TwistShoulderFirst { get; set; }
    public string LimitHit => lastLimitHit;
    public float ArmRadius => armRadius;
    public float ShoulderExtensionMax => shoulderExtensionMax;
    public float ElbowFlexionMax => elbowFlexionMax;
    public float WristExtensionMax => wristExtensionMax;

    /// <summary>몸 기준 축(월드). 못 쟀으면 false.</summary>
    public bool TryBodyAxesWorld(out Vector3 front, out Vector3 up, out Vector3 lateral)
    {
        front = up = lateral = Vector3.zero;
        if (!frameReady || chest == null) return false;
        front = chest.TransformVector(frontInChest).normalized;
        up = chest.TransformVector(upInChest).normalized;
        lateral = chest.TransformVector(lateralInChest).normalized;
        return true;
    }

    /// <summary>지금 위팔에 붙은 해부학 팔꿈치 굴곡 방향(월드). 못 쟀으면 false.</summary>
    public bool TryFlexDirWorld(out Vector3 dir)
    {
        dir = flexRefReady ? upper.TransformVector(flexRefInUpper).normalized : Vector3.zero;
        return flexRefReady;
    }

    /// <summary>지금 아래팔에 붙은 손등 쪽(배굴 방향, 월드). 못 쟀으면 false.</summary>
    public bool TryDorsalDirWorld(out Vector3 dir)
    {
        dir = dorsalReady ? lower.TransformVector(dorsalInLower).normalized : Vector3.zero;
        return dorsalReady;
    }

    /// <summary>점 p(월드, 반지름 r)가 침대 · 몸통 · 머리에 들어간 가장 깊은 깊이(m)와 그 대상. 0이면 밖. 막을 때와 같은 함수(Pen)를 탄다.</summary>
    public float PenetrationDepth(Vector3 p, float r, out string what)
    {
        what = "";
        if (!applyCollision || hipsBone == null) return 0f;
        float d = Pen(p, r, out _, out int k);
        if (d > 0f) what = ShapeName[k];
        return Mathf.Max(0f, d);
    }

    /// <summary>메뉴가 AddComponent 직후(Start 전에) 팔을 고를 때 쓴다.</summary>
    public ArmSide Side { get => side; set { if (!ready) side = value; } }

    private void Start()
    {
        animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInParent<Animator>();
        if (animator == null) { Debug.LogWarning("[팔IK] Animator가 없다 — 멈춘다. " + name); enabled = false; return; }
        if (!animator.isHuman) { Debug.LogWarning("[팔IK] Animator가 Humanoid가 아니다 — 멈춘다. " + animator.name); enabled = false; return; }

        bool right = side == ArmSide.Right;
        upper = animator.GetBoneTransform(right ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
        lower = animator.GetBoneTransform(right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
        hand = animator.GetBoneTransform(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
        if (upper == null || lower == null || hand == null)
        {
            Debug.LogWarning($"[팔IK] 팔 본을 못 찾았다 — 멈춘다. side={side} upper={NameOf(upper)} lower={NameOf(lower)} hand={NameOf(hand)}");
            enabled = false; return;
        }
        if (!lower.IsChildOf(upper) || !hand.IsChildOf(lower))
        {
            Debug.LogWarning($"[팔IK] 체인이 위팔 → 아래팔 → 손 순서의 부모·자식이 아니다 — 멈춘다. {upper.name}/{lower.name}/{hand.name}");
            enabled = false; return;
        }

        space = upper.parent;
        if (space == null) { Debug.LogWarning("[팔IK] 위팔에 부모가 없다 — 멈춘다."); enabled = false; return; }

        // S(위팔의 부모) 아래 체인에 음수 스케일이 있으면 S공간 풀이가 성립하지 않는다.
        for (Transform t = hand; t != space; t = t.parent)
        {
            Vector3 s = t.localScale;
            if (s.x < 0f || s.y < 0f || s.z < 0f)
            {
                Debug.LogWarning($"[팔IK] 체인 안 {t.name}에 음수 localScale {s} — S공간 풀이 불가, 멈춘다.");
                enabled = false; return;
            }
        }

        frameReady = MeasureBodyFrame(right);

        if (target == null) target = MakeHandle("[팔IK] 목표", hand.position, new Color(1f, 0.25f, 0.2f));
        if (hint == null) hint = MakeHandle("[팔IK] 힌트(팔꿈치)", DefaultHintPosition(), new Color(0.25f, 0.5f, 1f));

        targetRot0 = target.rotation;
        finger = animator.GetBoneTransform(right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
        if (finger == null) Debug.LogWarning("[팔IK] 가운뎃손가락 본이 없다 — 손목 꺾기를 끈다.");

        sideLabel = right ? "오른팔" : "왼팔";
        // 해부학 기준 = 아바타 근육으로 잰다(10-22 시뮬 분석: 시작 자세가 3°만 굽어 «자세에서 재기»가 실패했다).
        MeasureAnatomyRefs(right);

        // 막힘 기준 본
        hipsBone = animator.GetBoneTransform(HumanBodyBones.Hips);
        neckBone = animator.GetBoneTransform(HumanBodyBones.Neck);
        if (neckBone == null) neckBone = animator.GetBoneTransform(HumanBodyBones.Head);
        headBone = animator.GetBoneTransform(HumanBodyBones.Head);
        otherUpper = animator.GetBoneTransform(right ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
        footL = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        footR = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        foreTwist1 = FindChildContains(lower, "ForearmTwist01");
        foreTwist2 = FindChildContains(lower, "ForearmTwist02");
        if (foreTwist1 != null) foreTwist1Rest = foreTwist1.localRotation;
        if (foreTwist2 != null) foreTwist2Rest = foreTwist2.localRotation;
        Debug.Log($"[팔IK] {sideLabel} 아래팔 비틀림 보조 본 — {NameOf(foreTwist1)} · {NameOf(foreTwist2)}");

        eyeLBone = animator.GetBoneTransform(HumanBodyBones.LeftEye);
        eyeRBone = animator.GetBoneTransform(HumanBodyBones.RightEye);
        if (applyCollision && (hipsBone == null || neckBone == null || headBone == null || otherUpper == null || footL == null || footR == null || chest == null))
            Debug.LogWarning("[팔IK] 막힘을 잴 본이 모자라다 — 막힘을 끈다.");
        else if (applyCollision) MeasureBed();

        rotRest = MeasureHumeralRot(out _, out _, out _);
        Debug.Log($"[팔IK] {sideLabel} 시작 자세 위팔 회전 {rotRest:+0;-0}°(+내 / −외) — 펴진 팔 비틀기는 이 값 + 잡은 손이 비튼 각으로 맞춘다");

        // 로컬 회전 변환(RotateInS)은 위팔 localScale이 균일하다고 본다 — 아니면 알린다(멈추지는 않는다)
        Vector3 us = upper.localScale;
        if (Mathf.Abs(us.x - us.y) > 1e-3f || Mathf.Abs(us.x - us.z) > 1e-3f)
            Debug.LogWarning($"[팔IK] 위팔 localScale이 균일하지 않다 {us} — 아래팔 회전이 조금 어긋날 수 있다");

        Vector3 ls = upper.lossyScale;
        float det = ls.x * ls.y * ls.z;
        float l1 = Vector3.Distance(upper.position, lower.position);
        float l2 = Vector3.Distance(lower.position, hand.position);
        Debug.Log($"[팔IK] 시작 — 환자={animator.name} side={side} 본={upper.name} → {lower.name} → {hand.name} " +
                  $"위팔 {l1:F3}m · 아래팔 {l2:F3}m(월드) · 위팔 lossyScale={ls} 부호={(det < 0f ? "음(거울)" : "양")} · " +
                  $"풀이 공간 S={space.name} · weight={weight:F2} · 목표={target.name} · 힌트={hint.name}");

        ready = true;
        nextLogTime = Time.unscaledTime + logInterval;
    }

    /// <summary>
    /// 몸 기준 축을 뼈 «위치»로 잰다(본 이름 · 본 축을 믿지 않는다 — c8 음수 스케일, measure-dont-guess-signs).
    ///   위   = 엉덩이 → 목
    ///   앞   = 머리 본 → 두 눈 중점 (위 성분을 뺀다)
    ///   바깥 = 반대쪽 위팔 → 이쪽 위팔 (위 · 앞 성분을 뺀다)
    /// 외적으로 서로 엮지 않는다(CLAUDE.md 규칙 9) — 셋 다 따로 잰다.
    /// 가슴 본에 «행렬 기준»(InverseTransformVector)으로 붙여 두어 몸통이 돌면 같이 돈다.
    /// </summary>
    private bool MeasureBodyFrame(bool right)
    {
        Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        Transform neck = animator.GetBoneTransform(HumanBodyBones.Neck);
        Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
        Transform eyeL = animator.GetBoneTransform(HumanBodyBones.LeftEye);
        Transform eyeR = animator.GetBoneTransform(HumanBodyBones.RightEye);
        Transform other = animator.GetBoneTransform(right ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
        chest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
        if (chest == null) chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        if (chest == null) chest = animator.GetBoneTransform(HumanBodyBones.Spine);
        if (neck == null) neck = head;

        if (hips == null || neck == null || head == null || eyeL == null || eyeR == null || other == null || chest == null)
        {
            Debug.LogWarning($"[팔IK] 몸 기준 축을 잴 본이 없다 — 관절 한계를 끈다. hips={NameOf(hips)} neck={NameOf(neck)} head={NameOf(head)} " +
                             $"eyeL={NameOf(eyeL)} eyeR={NameOf(eyeR)} 반대 위팔={NameOf(other)} 가슴={NameOf(chest)}");
            return false;
        }

        Vector3 up = (neck.position - hips.position).normalized;
        Vector3 front = Vector3.ProjectOnPlane((eyeL.position + eyeR.position) * 0.5f - head.position, up).normalized;
        Vector3 lateral = upper.position - other.position;
        lateral = (lateral - Vector3.Dot(lateral, up) * up - Vector3.Dot(lateral, front) * front).normalized;
        if (up.sqrMagnitude < 0.5f || front.sqrMagnitude < 0.5f || lateral.sqrMagnitude < 0.5f)
        {
            Debug.LogWarning("[팔IK] 몸 기준 축 길이가 0이다 — 관절 한계를 끈다.");
            return false;
        }

        upInChest = chest.InverseTransformVector(up);
        frontInChest = chest.InverseTransformVector(front);
        lateralInChest = chest.InverseTransformVector(lateral);
        Debug.Log($"[팔IK] 몸 기준 축(월드) — 앞 {front:F2} · 위 {up:F2} · 바깥 {lateral:F2} · 가슴 본={chest.name} · " +
                  $"재는 본: 엉덩이 {hips.name} · 목 {neck.name} · 머리 {head.name} · 눈 {eyeL.name}/{eyeR.name} · 반대 위팔 {other.name}. " +
                  "기즈모 화살표(초록 = 앞 · 파랑 = 위 · 주황 = 바깥)가 환자 몸과 맞는지 눈으로 확인할 것");
        return true;
    }

    /// <summary>방향 v의 k 성분이 maxDot을 넘으면 k 성분을 maxDot으로 줄이고 나머지 방향은 그대로 둔다(단위 벡터 유지).</summary>
    private static bool ClampAlong(ref Vector3 v, Vector3 k, float maxDot, Vector3 fallbackPerp)
    {
        float dot = Vector3.Dot(v, k);
        if (dot <= maxDot) return false;
        Vector3 perp = v - dot * k;
        if (perp.sqrMagnitude < 1e-10f) perp = fallbackPerp - Vector3.Dot(fallbackPerp, k) * k;
        v = perp.normalized * Mathf.Sqrt(Mathf.Max(0f, 1f - maxDot * maxDot)) + k * maxDot;
        return true;
    }

    /// <summary>
    /// 아바타(Humanoid 근육)로 해부학 방향을 잰다 — 본 축 · 이름을 믿지 않고, 거울과도 무관하다.
    ///   · 팔꿈치 굴곡 방향: «Forearm Stretch» 근육을 굽힌 자세로 바꿔 아래팔이 간 쪽(위팔 축 성분 제외) → 위팔 본 로컬
    ///   · 손등 쪽(배굴 방향): «Hand Down-Up» 근육을 올린 자세에서 손가락이 간 쪽(아래팔 축 성분 제외) → 아래팔 본 로컬
    /// 잰 뒤 모든 본을 원래 값으로 되돌린다. 앞(F)과의 내적을 로그로 남겨 눈으로 검산한다(팔을 내린 자세면 굴곡 방향 ≈ 앞).
    /// </summary>
    private void MeasureAnatomyRefs(bool right)
    {
        Transform[] all = animator.GetComponentsInChildren<Transform>(true);
        var lp = new Vector3[all.Length]; var lr = new Quaternion[all.Length]; var lsc = new Vector3[all.Length];
        for (int i = 0; i < all.Length; i++) { lp[i] = all[i].localPosition; lr[i] = all[i].localRotation; lsc[i] = all[i].localScale; }
        try
        {
            var hph = new HumanPoseHandler(animator.avatar, animator.transform);
            var pose = new HumanPose();
            hph.GetHumanPose(ref pose);
            string side = right ? "Right" : "Left";
            int iFore = System.Array.IndexOf(HumanTrait.MuscleName, side + " Forearm Stretch");
            int iHand = System.Array.IndexOf(HumanTrait.MuscleName, side + " Hand Down-Up");
            if (iFore < 0) { Debug.LogWarning("[팔IK] 근육 «Forearm Stretch»를 못 찾았다 — 굴곡 기준 없음"); return; }

            float[] keep = (float[])pose.muscles.Clone();
            pose.muscles[iFore] = -0.6f;   // 굽힘(1 = 폄)
            if (iHand >= 0) pose.muscles[iHand] = 0f;
            hph.SetHumanPose(ref pose);
            Vector3 uW = lower.position - upper.position, fW = hand.position - lower.position;
            Vector3 perp = fW - Vector3.Dot(fW, uW.normalized) * uW.normalized;
            flexRefReady = perp.sqrMagnitude > 1e-6f;
            if (flexRefReady) flexRefInUpper = upper.InverseTransformVector(perp.normalized);
            float bentDeg = Vector3.Angle(uW, fW);
            Vector3 fingerA = finger != null ? finger.position - hand.position : Vector3.zero;

            if (iHand >= 0 && finger != null)
            {
                pose.muscles[iHand] = 1f;   // 손등 쪽으로(배굴)
                hph.SetHumanPose(ref pose);
                Vector3 foreW = (hand.position - lower.position).normalized;
                Vector3 fingerB = finger.position - hand.position;
                Vector3 dors = fingerB - fingerA;
                dors -= Vector3.Dot(dors, foreW) * foreW;
                dorsalReady = dors.sqrMagnitude > 1e-8f;
                if (dorsalReady) dorsalInLower = lower.InverseTransformVector(dors.normalized);
            }
            // 아래팔 회외 방향 — «Forearm Twist In-Out» 근육을 Out(+1)으로 바꿔 손바닥 가로 방향(검지 → 새끼 뿌리)이 팔꿈치 → 손 축 둘레로 어느 쪽으로 도는지 잰다
            int iTwist = System.Array.IndexOf(HumanTrait.MuscleName, side + " Forearm Twist In-Out");
            Transform idx = animator.GetBoneTransform(right ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
            Transform lit = animator.GetBoneTransform(right ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal);
            if (iTwist >= 0 && idx != null && lit != null)
            {
                pose.muscles = (float[])keep.Clone();
                pose.muscles[iTwist] = 0f;
                hph.SetHumanPose(ref pose);
                Vector3 ax0 = (hand.position - lower.position).normalized;
                Vector3 a0 = Vector3.ProjectOnPlane(idx.position - lit.position, ax0);
                pose.muscles[iTwist] = 1f;   // Out
                hph.SetHumanPose(ref pose);
                Vector3 ax1 = (hand.position - lower.position).normalized;
                Vector3 a1 = Vector3.ProjectOnPlane(idx.position - lit.position, ax1);
                float ang = Vector3.SignedAngle(a0, a1, ax1);   // 월드 — 월드엔 거울이 없다
                // Unity 근육 «Twist In-Out»의 Out(+) = 회외로 본다(근육 이름 규약 — ★확인은 화면으로: 손바닥이 위를 보면 회외)
                supWorldSign = Mathf.Abs(ang) > 5f ? Mathf.Sign(ang) : 0f;
                Debug.Log($"[팔IK] {sideLabel} 아래팔 회외 방향(근육 Forearm Twist Out) — 팔꿈치→손 축 둘레 {ang:+0;-0}° · 부호 {supWorldSign:+0;-0;0}");
            }
            // ★10-22 명세: 아래팔 회내 · 회외 · 손목 한계의 0은 «해부학 중립»이다 — 시작 자세가 아니다.
            //   시작 자세의 아래팔 회전 = 근육 «Forearm Twist In-Out» 값을 도로 바꾼다(아바타 근육 한계는 Unity 기본값 — c9.fbx.meta 실측).
            if (iTwist >= 0)
            {
                float m0 = keep[iTwist];
                forearmRestSupDeg = m0 >= 0f ? m0 * HumanTrait.GetMuscleDefaultMax(iTwist) : -m0 * HumanTrait.GetMuscleDefaultMin(iTwist);
            }
            //   손목 중립 축 — 손 근육(Down-Up · In-Out)을 0으로 둔 자세에서 손가락 방향 · 엄지 쪽(요측)을 아래팔 본 로컬에 담는다
            int iHandIO = System.Array.IndexOf(HumanTrait.MuscleName, side + " Hand In-Out");
            if (finger != null && idx != null && lit != null)
            {
                pose.muscles = (float[])keep.Clone();
                if (iHand >= 0) pose.muscles[iHand] = 0f;
                if (iHandIO >= 0) pose.muscles[iHandIO] = 0f;
                hph.SetHumanPose(ref pose);
                Vector3 nW = (finger.position - hand.position).normalized;
                Vector3 rW = Vector3.ProjectOnPlane(idx.position - lit.position, nW);   // 검지 뿌리 쪽 = 엄지 쪽(요측)
                wristAxesReady = rW.sqrMagnitude > 1e-8f;
                if (wristAxesReady) { wristNeutralInLower = lower.InverseTransformVector(nW); wristRadialInLower = lower.InverseTransformVector(rW.normalized); }
                // 운반각의 «바깥» — 팔 내린 시작 자세에서 몸 바깥(L)을 위팔 축 · 굴곡 방향에 수직으로 남긴 것(외적을 쓰지 않는다)
                if (frameReady && flexRefReady)
                {
                    Vector3 uAx = (lower.position - upper.position).normalized;
                    Vector3 fx = upper.TransformVector(flexRefInUpper).normalized;
                    Vector3 lat = chest.TransformVector(lateralInChest).normalized;
                    lat -= Vector3.Dot(lat, uAx) * uAx; lat -= Vector3.Dot(lat, fx) * fx;
                    carryRefReady = lat.sqrMagnitude > 1e-4f;
                    if (carryRefReady) carryLateralInUpper = upper.InverseTransformVector(lat.normalized);
                }
            }
            Debug.Log($"[팔IK] {sideLabel} 해부학 중립 — 시작 자세 아래팔 회외 {forearmRestSupDeg:+0;-0}°(근육값) · 손목 축 {(wristAxesReady ? "잼" : "★못 잼")} · 운반각 바깥 {(carryRefReady ? "잼" : "★못 잼")}");
            pose.muscles = keep;
            hph.Dispose();

            string chk = "";
            if (frameReady && flexRefReady)
                chk = $" · 검산: 굴곡 방향 · 앞 내적 {Vector3.Dot(perp.normalized, chest.TransformVector(frontInChest).normalized):+0.00;-0.00}(팔을 내린 자세면 +에 가까워야 한다)";
            Debug.Log($"[팔IK] {sideLabel} 해부학 기준(아바타 근육으로 잼) — 팔꿈치 굴곡 방향 {(flexRefReady ? "잼" : "★못 잼")}(시험 굽힘 {bentDeg:F0}°) · " +
                      $"손등 쪽 {(dorsalReady ? "잼" : "★못 잼")}{chk}");
        }
        finally
        {
            for (int i = 0; i < all.Length; i++) { all[i].localPosition = lp[i]; all[i].localRotation = lr[i]; all[i].localScale = lsc[i]; }
        }
    }

    private Transform MakeHandle(string handleName, Vector3 worldPos, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = handleName;
        // 콜라이더는 뺀다 — 환자 접촉 판정·손 충돌에 끼어들지 않게.
        Collider col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);
        go.transform.position = worldPos;
        go.transform.localScale = Vector3.one * handleSize;
        Renderer r = go.GetComponent<Renderer>();
        if (r != null) r.material.color = color; // 시작 때 한 번만 인스턴스 머티리얼을 만든다
        return go.transform;
    }

    /// <summary>지금 팔꿈치를 어깨-손 직선에서 바깥으로 0.3m 민 자리. 팔이 곧으면 몸 앞쪽 아무 수직.</summary>
    private Vector3 DefaultHintPosition()
    {
        Vector3 a = upper.position, b = lower.position, c = hand.position;
        Vector3 ac = c - a;
        Vector3 off = (b - a) - Vector3.Project(b - a, ac);
        if (off.sqrMagnitude < 1e-6f) off = Vector3.Cross(ac, Vector3.up);
        if (off.sqrMagnitude < 1e-6f) off = Vector3.Cross(ac, Vector3.forward);
        return b + off.normalized * 0.3f;
    }

    private void LateUpdate()
    {
        if (!ready || target == null) return;

        // 애니가 이번 프레임 팔을 썼으면 그게 기준 자세다. 안 썼으면(우리가 쓴 값이 그대로면) 기준 자세로 되돌린다.
        if (hasWritten && upper.localRotation == wroteUpper && lower.localRotation == wroteLower && hand.localRotation == wroteHand)
        {
            upper.localRotation = baseUpper; lower.localRotation = baseLower; hand.localRotation = baseHand;
            nRestored++;
        }
        else
        {
            nAnimWrote++;
            baseUpper = upper.localRotation; baseLower = lower.localRotation; baseHand = hand.localRotation;
        }

        // 쇄골(견갑상완 리듬)도 같은 규칙 — 애니가 안 썼으면 기준으로 되돌린다(쌓이지 않게)
        if (hasWritten && space.localRotation == wroteClav) space.localRotation = baseClav; else baseClav = space.localRotation;

        if (weight > 0f) Solve();

        // 관절 속도 제한 — 직전 프레임에 우리가 낸 자세에서 이번 해까지 maxJointSpeed만큼만 간다(애니가 매 프레임 써도 출력 기준으로 잰다)
        if (weight > 0f && maxJointSpeed > 0f && hasWritten)
        {
            float step = maxJointSpeed * Time.deltaTime;
            upper.localRotation = Quaternion.RotateTowards(wroteUpper, upper.localRotation, step);
            lower.localRotation = Quaternion.RotateTowards(wroteLower, lower.localRotation, step);
            hand.localRotation = Quaternion.RotateTowards(wroteHand, hand.localRotation, step);
            // ★10-22 시뮬 5회차: 속도를 자르자 튐은 사라졌지만, 느리게 따라가는 도중 팔이 몸통을 질러가 관통이 커졌다(최대 181mm).
            //   자른 자세에서 한 번 더 막힘만 밀어낸다 — 지금 자리에서 조금씩 미니까 몸 표면을 따라 돌아간다.
            if (applyCollision) CorrectCollisionInPlace();
        }
        // ★10-22 명세 시뮬: 속도 제한 · 제자리 막힘 보정이 한계 «뒤에» 돌아 출력이 한계를 넘었다(머리 위에서 뒤로 θ 90 > 경계 84 · 팔꿈치 150 > 145).
        //   → 그리는 자세에 마지막으로 어깨 방향별 경계 · 팔꿈치 굴곡 한계를 한 번 더 건다. 한계가 이긴다.
        if (weight > 0f && applyLimits && frameReady) EnforceFinalLimits();

        wroteUpper = upper.localRotation; wroteLower = lower.localRotation; wroteHand = hand.localRotation; wroteClav = space.localRotation;
        prevUpperDirW = (lower.position - upper.position).normalized; hasPrevUpperDir = true;
        hasWritten = true;

        if (Time.unscaledTime >= nextLogTime)
        {
            nextLogTime = Time.unscaledTime + logInterval;
            Debug.LogFormat("[팔IK] " + sideLabel + " 목표 거리 {0:F3}m / 닿는 거리 {1:F3}m ({2}) · 팔꿈치 각 {3:F1}° · 손 오차 {5:F1}mm · weight {4:F2}",
                lastTargetDistance, lastReach, lastReachable ? "닿음" : "못 닿음 — 뻗어서 가리킴",
                lastElbowAngle, weight, lastHandErrMm);
            if (applyLimits && frameReady)
                Debug.LogFormat("[팔IK] " + sideLabel + " 관절 — 어깨 뒤로 {0:F0}°/{4:F0} · 안쪽 {1:F0}°/{5:F0} · 회전 {2:+0;-0}°(+내 {6:F0} / −외 {7:F0}) · 팔꿈치 굴곡 {3:F0}°/{8:F0} · 걸린 한계: {9}",
                    lastBehindDeg, lastMedialDeg, lastRotDeg, lastElbowFlexDeg,
                    shoulderExtensionMax, shoulderAdductionMax, shoulderInternalRotMax, shoulderExternalRotMax, elbowFlexionMax,
                    lastLimitHit.Length > 0 ? lastLimitHit : "없음");
            if (followTargetRotation)
                Debug.LogFormat("[팔IK] " + sideLabel + " 잡은 손 회전 — 팔 비틀기 {0:+0;-0}° · 손목 꺾임 {1:F0}°/{2:F0} · 막힘 {3}",
                    lastTwistDeg, lastWristDeg, wristExtensionMax, applyCollision ? (bedReady ? (bedMeasured ? "침대(씬에서 잼) · 몸통 · 머리" : "침대(추정) · 몸통 · 머리") : "몸통 · 머리(침대 못 잼)") : "끔");
            Debug.LogFormat("[팔IK] {0} 프레임 — 애니가 팔을 씀 {1} · 기준 자세로 되돌림 {2}", sideLabel, nAnimWrote, nRestored);
            if (frameReady)
                Debug.LogFormat("[팔IK] {0} 방향 진단 — φ 목표 {1:F0}° · 2관절 풀이 {2:F0}° · 한계 뒤 {3:F0}° · 그린 자세 {4:F0}° · 쇄골 거상 {5:F0}° 후인 {6:F0}°",
                    sideLabel, dbgPhiTarget, dbgPhiRaw, dbgPhiLimited, PhiS(space.InverseTransformPoint(lower.position) - space.InverseTransformPoint(upper.position)), lastGirdleDeg, lastGirdleRetractDeg);
            nAnimWrote = nRestored = 0;
        }
    }

    private float lastGirdleDeg;   // 이번 프레임 쇄골 본을 들어 올린 각(로그용)
    public float GirdleDeg => lastGirdleDeg;

    /// <summary>
    /// 보이는 쇄골(사용자 선택 A — 실측만큼만): 위팔–몸통 거상 θ가 30°를 넘으면
    ///   거상 = (θ − 30) × 7/90 (최대 girdleElevationMax · θ120 → 7) · 후인 = (θ − 30) × 20/120 (최대 girdleRetractionMax)
    /// 만큼 쇄골 바깥 끝을 머리 쪽 · 뒤쪽으로 옮긴다(Ludewig 2009 · McClure 2001 골핀 3D: 거상 6~10 · 후인 16~21).
    /// θ는 직전 프레임에 풀어 낸 «위팔 방향»으로 잰다(★10-22 명세: 어깨→목표 방향으로 재면 팔꿈치를 굽혔을 때 틀린다).
    /// 쇄골은 S(풀이 공간) 자체라 풀기 «전에» 돌린다. 회전은 월드 벡터로 짓고 쇄골 부모 공간에 옮긴다(거울 너머로 축 부호를 넘기지 않는다).
    /// </summary>
    private void ApplyGirdle()
    {
        lastGirdleDeg = 0f;
        if (!scapularRhythm || !frameReady || space.parent == null || !hasPrevUpperDir) return;
        Vector3 Uw = chest.TransformVector(upInChest).normalized;
        Vector3 Fw = chest.TransformVector(frontInChest).normalized;
        float theta = Vector3.Angle(prevUpperDirW, -Uw);
        float g = Mathf.Clamp((theta - 30f) * 7f / 90f, 0f, girdleElevationMax);
        float r = Mathf.Clamp((theta - 30f) * 20f / 120f, 0f, girdleRetractionMax);
        if (g < 0.1f && r < 0.1f) return;
        Vector3 v = upper.position - space.position;                 // 쇄골 안쪽 끝 → 어깨
        if (v.sqrMagnitude < 1e-6f) return;
        Vector3 vn = v.normalized;
        Vector3 up = Uw - Vector3.Dot(Uw, vn) * vn;
        if (up.sqrMagnitude < 1e-6f) return;
        Vector3 v2 = vn * Mathf.Cos(g * Mathf.Deg2Rad) + up.normalized * Mathf.Sin(g * Mathf.Deg2Rad);
        Vector3 back = -Fw - Vector3.Dot(-Fw, v2) * v2;
        if (back.sqrMagnitude > 1e-6f) v2 = (v2 * Mathf.Cos(r * Mathf.Deg2Rad) + back.normalized * Mathf.Sin(r * Mathf.Deg2Rad)).normalized;
        Transform par = space.parent;
        Quaternion q = Quaternion.FromToRotation(par.InverseTransformVector(v), par.InverseTransformVector(v2));
        space.localRotation = q * space.localRotation;
        lastGirdleDeg = g;
        lastGirdleRetractDeg = r;
    }
    private float lastGirdleRetractDeg;
    public float GirdleRetractDeg => lastGirdleRetractDeg;
    private float lastCarryDeg;
    public float CarryDeg => lastCarryDeg;
    private Vector3 prevUpperDirW;   // 직전 프레임 위팔 방향(월드) — 쇄골 리듬의 거상을 잰다
    private bool hasPrevUpperDir;

    private void Solve()
    {
        ApplyGirdle();
        // ── 전부 S(위팔 부모) 로컬 공간에서 푼다. 위 클래스 주석 «음수 스케일 처리» 참고 ──
        Vector3 a = space.InverseTransformPoint(upper.position);
        Vector3 b = space.InverseTransformPoint(lower.position);
        Vector3 c = space.InverseTransformPoint(hand.position);
        Vector3 t = space.InverseTransformPoint(target.position);

        float l1 = (b - a).magnitude;
        float l2 = (c - b).magnitude;
        if (l1 < 1e-5f || l2 < 1e-5f) return;

        Vector3 at = t - a;
        float dist = at.magnitude;
        float maxReach = (l1 + l2) * 0.999f;              // 완전히 펴면 acos가 NaN 경계라 살짝 덜 편다
        float minReach = Mathf.Abs(l1 - l2) * 1.001f + 1e-4f;
        if (applyLimits)
        {
            // 팔꿈치 굴곡 한계 → 어깨-손 최소 거리(코사인 법칙). 안쪽 각 = 180 − 굴곡
            float inner = (180f - elbowFlexionMax) * Mathf.Deg2Rad;
            minReach = Mathf.Max(minReach, Mathf.Sqrt(l1 * l1 + l2 * l2 - 2f * l1 * l2 * Mathf.Cos(inner)));
        }
        float d = Traction ? maxReach : Mathf.Clamp(dist, minReach, maxReach);
        Vector3 dir = dist > 1e-5f ? at / dist : (c - a).normalized;

        // 로그용(월드 m로 환산 — S의 스케일 크기만큼 곱한다)
        float sScale = space.lossyScale.magnitude / 1.7320508f;
        lastTargetDistance = dist * sScale;
        lastReach = (l1 + l2) * sScale;
        lastReachable = dist <= maxReach;

        // 팔꿈치가 굽을 쪽(pole)
        //   10-22: 기본 = 직전 프레임 팔꿈치 쪽(손목을 움직여도 팔꿈치가 휘돌지 않는다 — 고정 힌트를 쓰면 팔꿈치가 힌트로 끌려갔다)
        //   팔이 쭉 펴져 직전 팔꿈치가 축 위에 있으면 직전 굽는 쪽을 그대로 쓴다.
        Vector3 pole;
        if (flexRefReady)
        {
            // ★10-22 시뮬 2회차: 굽힘이 작은(10~25°) 동안 경첩 맞추기가 꺼져 있어 팔꿈치가 반대 · 옆으로 꺾였다(35프레임).
            //   팔꿈치 자리 = 위팔에 붙은 해부학 굴곡 방향의 반대쪽(팔꿈치 끝은 굽히는 쪽의 반대로 나온다) → 굽힘이 작아도 늘 경첩 방향이다.
            pole = -space.InverseTransformVector(upper.TransformVector(flexRefInUpper));
            pole -= Vector3.Dot(pole, dir) * dir;
            if (pole.sqrMagnitude < 1e-8f && hasPrevElbow) pole = prevPoleS - Vector3.Dot(prevPoleS, dir) * dir;
        }
        else if (poleFromPreviousElbow && hasPrevElbow)
        {
            pole = prevElbowS - a;
            pole -= Vector3.Dot(pole, dir) * dir;
            if (pole.sqrMagnitude < 1e-6f * l1 * l1) pole = prevPoleS - Vector3.Dot(prevPoleS, dir) * dir;
        }
        else
        {
            pole = hint != null ? space.InverseTransformPoint(hint.position) - a : (b - a);
            pole -= Vector3.Dot(pole, dir) * dir;
        }
        if (pole.sqrMagnitude < 1e-8f) { pole = (b - a) - Vector3.Dot(b - a, dir) * dir; }
        if (pole.sqrMagnitude < 1e-8f) { pole = Vector3.Cross(dir, Vector3.up); }
        if (pole.sqrMagnitude < 1e-8f) { pole = Vector3.Cross(dir, Vector3.right); }
        pole.Normalize();

        // 코사인 법칙 — 어깨각
        float cosA = Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f);
        float sinA = Mathf.Sqrt(1f - cosA * cosA);
        Vector3 elbowGoal = a + dir * (l1 * cosA) + pole * (l1 * sinA);
        Vector3 handGoal = a + dir * d;                   // 못 닿으면 목표 방향으로 뻗은 끝
        // ★10-22 진단: 못 닿을 때 0.999배 여유로 팔꿈치가 2~3° 굽어 위팔이 목표에서 틀어졌다 — 머리 위(θ 170)에선 그게 φ 15°(90 → 105)로 커져
        //   «옆 머리 위»가 관상면 뒤 경계(128°)에 걸렸다. 못 닿으면 위팔을 목표 방향 그대로 둔다(팔꿈치는 편 채).
        if (dist >= maxReach) elbowGoal = a + dir * l1;

        string hit = null;
        if (frameReady) { dbgPhiTarget = PhiS(t - a); dbgPhiRaw = PhiS(elbowGoal - a); }   // ★10-22 진단: 옆으로 머리 위가 φ104로 밀린다 — 어느 단계에서 도는지
        if (applyLimits && frameReady) ApplyJointLimits(a, l1, l2, t, ref elbowGoal, ref handGoal, ref hit);
        if (frameReady) dbgPhiLimited = PhiS(elbowGoal - a);
        if (applyCollision)
        {
            ApplyCollision(a, ref elbowGoal, ref handGoal, ref hit);
            // ★10-22 시뮬 7회차(사용자가 화면으로 지적 — 머리 위로 올릴 때 말도 안 되는 각도): 막힘이 밀어낸 뒤 한계가 깨졌다
            //   (팔꿈치 굴곡 180° · 어깨 내전 40°). → 관절 한계를 한 번 더 건다. 둘이 부딪히면 한계가 이긴다(조금 파고드는 쪽을 택한다).
            if (applyLimits && frameReady) ApplyJointLimits(a, l1, l2, handGoal, ref elbowGoal, ref handGoal, ref hit);
        }
        // 아래팔은 늘 경첩 면의 굽는 쪽 반 안에(★10-22 시뮬 4회차: 막힘 뒤 옆으로 꺾임 100프레임)
        Vector3 uG = (elbowGoal - a).normalized, wG = handGoal - elbowGoal;
        if (wG.sqrMagnitude > 1e-10f) handGoal = elbowGoal + HingeDir(a, uG, wG.normalized) * l2;
        Vector3 rawForearm = t - elbowGoal;   // ② 위팔 회전이 «천천히» 따라갈 방향(손을 어느 쪽으로 끄는지)

        // ① 위팔: 팔꿈치를 elbowGoal로
        Quaternion q1 = Quaternion.FromToRotation(b - a, elbowGoal - a);
        RotateInS(upper, q1);

        // ② 위팔 비틀기: 위팔 축을 중심으로 돌려 아래팔이 굽힘면(a·elbowGoal·handGoal)에 들어오게
        if (alignElbowTwist)
        {
            Vector3 b1 = space.InverseTransformPoint(lower.position);
            Vector3 c1 = space.InverseTransformPoint(hand.position);
            Vector3 axis = b1 - a;
            if (axis.sqrMagnitude > 1e-10f)
            {
                axis.Normalize();
                // ★10-22: 팔꿈치는 경첩이다 — 위팔에 붙은 «해부학 굴곡 방향»을 굽힘면에 맞춘다(아바타 근육으로 잰 것).
                //   종전엔 지금 아래팔 방향을 맞춰서, 팔꿈치가 옆이나 뒤로도 꺾일 수 있었다.
                Vector3 from = flexRefReady
                    ? Vector3.ProjectOnPlane(space.InverseTransformVector(upper.TransformVector(flexRefInUpper)), axis)
                    : Vector3.ProjectOnPlane(c1 - b1, axis);
                Vector3 to = Vector3.ProjectOnPlane(flexRefReady ? rawForearm : handGoal - b1, axis);
                // 아래팔이 거의 곧으면 굽힘면이 정의되지 않는다 — 비틀지 않는다
                // ★10-22 시뮬: 팔이 거의 펴지면 굽힘면이 잡음뿐이라 여기서 위팔이 마구 돌았다(160°/프레임) — 목표 굽힘 10~25°에서 서서히 켠다.
                // ★10-22 시뮬 6회차: 팔꿈치 자리를 해부학 굴곡 반대쪽으로 정한 뒤로는 잡음 문제가 없어 2~6°에서 다 켠다
                //   (10~25°로 두니 그 사이 굽힘에서 위팔이 덜 돌아 팔꿈치가 뒤로 꺾여 보였다).
                float bendW = Mathf.InverseLerp(2f, 6f, Vector3.Angle(elbowGoal - a, (flexRefReady ? rawForearm : handGoal - elbowGoal)));
                if (bendW > 0f && from.sqrMagnitude > 1e-8f && to.sqrMagnitude > 1e-8f)
                {
                    float twist = Vector3.SignedAngle(from, to, axis) * bendW;
                    // ★10-22 시뮬 7회차: 손이 어깨 가까이를 지날 때 굽힘면이 반대로 넘어가 위팔이 한계 끝에서 끝으로(+70 → −90) 돌았다.
                    //   경첩은 손 자리(HingeDir)가 이미 지킨다 — 여기는 «사용자가 끄는 쪽으로 위팔 회전이 따라가는» 몫만, 초당 120°까지.
                    if (flexRefReady) twist = Mathf.Clamp(twist, -120f * Time.deltaTime, 120f * Time.deltaTime);
                    RotateInS(upper, Quaternion.AngleAxis(twist, axis));
                    if (flexRefReady)
                    {
                        // 위팔이 돈 만큼 경첩 면도 돌았다 — 손 자리를 새 경첩 면으로 다시 옮긴다
                        Vector3 w2 = handGoal - b1;
                        if (w2.sqrMagnitude > 1e-10f) handGoal = b1 + HingeDir(a, axis, w2.normalized) * l2;
                    }
                }
            }
        }

        // ②-2 운반각(10-22 명세) — 편 팔에서 아래팔이 바깥으로 elbowCarryingAngle(12.9°) 벌어지고, 굽힐수록 0으로(★직선 추정).
        //   경첩 면을 맞춘 «뒤에» 얹는다(경첩 면 투영이 바깥 성분을 지우기 때문). «바깥»은 위팔에 붙은 실측 방향.
        if (applyLimits && carryRefReady && elbowCarryingAngle > 0f)
        {
            Vector3 bC = space.InverseTransformPoint(lower.position);
            Vector3 uC2 = (bC - a).normalized;
            Vector3 wC2 = handGoal - bC;
            if (wC2.sqrMagnitude > 1e-10f)
            {
                wC2.Normalize();
                float flexC = Vector3.Angle(uC2, wC2);
                float cAng = elbowCarryingAngle * Mathf.Clamp01(1f - flexC / Mathf.Max(1f, elbowFlexionMax));
                Vector3 latS = space.InverseTransformVector(upper.TransformVector(carryLateralInUpper));
                latS -= Vector3.Dot(latS, wC2) * wC2;
                if (cAng > 0.01f && latS.sqrMagnitude > 1e-8f)
                    handGoal = bC + (wC2 * Mathf.Cos(cAng * Mathf.Deg2Rad) + latS.normalized * Mathf.Sin(cAng * Mathf.Deg2Rad)).normalized * l2;
                lastCarryDeg = cAng;
            }
        }

        // ③ 아래팔: 손을 handGoal로
        Vector3 b2 = space.InverseTransformPoint(lower.position);
        Vector3 c2 = space.InverseTransformPoint(hand.position);
        Quaternion q2 = Quaternion.FromToRotation(c2 - b2, handGoal - b2);
        RotateInS(lower, q2);

        // ③-2 위팔 회전 한계를 자세에 직접 건다(★10-22 시뮬 7회차: 굽은 팔에서 회전이 한계 밖으로 나갔다)
        if (applyLimits)
        {
            float rotNow = MeasureHumeralRot(out Vector3 fr, out Vector3 nr, out Vector3 mr);
            if (!float.IsNaN(rotNow))
            {
                float cl = ClampRot(rotNow, space.InverseTransformPoint(lower.position) - a);
                if (!Mathf.Approximately(cl, rotNow))
                {
                    float rr = cl * Mathf.Deg2Rad;
                    RotateInS(upper, Quaternion.FromToRotation(fr, nr * Mathf.Cos(rr) + mr * Mathf.Sin(rr)));
                    AddHit(ref hit, rotNow > 0f ? "어깨 내회전" : "어깨 외회전");
                }
                lastRotDeg = cl;
            }
        }

        // ④ 잡은 손의 회전 — 팔이 펴졌을 때 위팔 비틀기(외·내회전) · 손목 꺾기
        if (followTargetRotation) ApplyTargetRotation(a, ref hit);

        // 로그·기즈모용 팔꿈치 각(180° = 쭉 폄)
        Vector3 b3 = space.InverseTransformPoint(lower.position);
        Vector3 c3 = space.InverseTransformPoint(hand.position);
        lastElbowAngle = Vector3.Angle(a - b3, c3 - b3);
        lastHandErrMm = Vector3.Distance(hand.position, space.TransformPoint(handGoal)) * 1000f;

        // 다음 프레임 굽는 쪽
        Vector3 bend = b3 - a;
        Vector3 ax = (c3 - a).sqrMagnitude > 1e-10f ? (c3 - a).normalized : dir;
        Vector3 bendPerp = bend - Vector3.Dot(bend, ax) * ax;
        if (bendPerp.sqrMagnitude > 1e-6f * l1 * l1) prevPoleS = bendPerp.normalized;
        else if (!hasPrevElbow) prevPoleS = pole;
        prevElbowS = b3;
        hasPrevElbow = true;

        string now = hit ?? "";
        if (now != lastLimitHit)
        {
            lastLimitHit = now;
            Debug.Log("[팔IK] " + sideLabel + " 걸린 한계 → " + (now.Length > 0 ? now : "없음"));
        }
    }

    /// <summary>
    /// 팔꿈치 · 손 목표 자리를 관절 한계 안으로 옮긴다(S공간). 전부 내적만 쓴다 — S공간은 월드에 대해 거울일 수 있어
    /// 외적 · SignedAngle의 부호가 뒤집히기 때문이다(내적은 거울에서도 그대로다).
    ///   ① 위팔 방향: 관상면 뒤로 ≤ 신전 한계, 시상면 안쪽으로 ≤ 내전 한계
    ///   ② 아래팔 방향: 위팔에 대한 굴곡 ≤ 팔꿈치 한계(과신전 없음은 2관절 풀이가 이미 보장)
    ///   ③ 위팔 회전(비틀기): 팔을 내린 자세에서 아래팔이 앞을 볼 때를 0으로, 같은 최소 회전으로 옮긴 «앞» · «안쪽»에 대해
    ///      아래팔이 얼마나 안쪽(+내회전) · 바깥(−외회전)으로 돌았는지 → 한계로 자른다
    /// 한계에 걸리면 손은 목표에서 떨어진다(가장 가까운 허용 자세). 위팔을 잘랐으면 아래팔은 목표 쪽을 다시 겨눈다.
    /// </summary>
    private void ApplyJointLimits(Vector3 a, float l1, float l2, Vector3 t, ref Vector3 elbowGoal, ref Vector3 handGoal, ref string hit)
    {
        BodyAxesS(out Vector3 F, out Vector3 U, out Vector3 L);

        // ① 위팔 방향
        Vector3 u = (elbowGoal - a).normalized;
        // ★10-22 사용자 지적: «팔을 편 채 저렇게 뒤로 젖혀지냐 — 관절마다 한계만 따로 걸고 방향과 연결된 범위를 생각 안 한다».
        //   종전엔 뒤로(신전) · 안쪽(내전) · 거상을 축마다 따로 잘라, 머리 위로 150° 들면서 관상면 뒤로 44°까지 갔다.
        //   → 팔이 향하는 «방향»(φ: 0 앞 · 90 옆 · ±180 뒤 · −90 안쪽)마다 들 수 있는 최대 거상 θ를 하나의 경계(관절 가동 공간)로 건다.
        float theta = Vector3.Angle(u, -U);
        Vector3 hz = u - Vector3.Dot(u, U) * U;
        Vector3 hdir = hz.sqrMagnitude > 1e-8f ? hz.normalized : F;
        float phi = Mathf.Atan2(Vector3.Dot(hdir, L), Vector3.Dot(hdir, F)) * Mathf.Rad2Deg;
        float elbowFlexNow = Vector3.Angle(u, handGoal - elbowGoal);
        float thMax = ShoulderConeMax(phi, elbowFlexNow);
        bool hitCone = theta > thMax;
        if (hitCone)
        {
            float rr = thMax * Mathf.Deg2Rad;
            u = (-U * Mathf.Cos(rr) + hdir * Mathf.Sin(rr)).normalized;
            AddHit(ref hit, "어깨 방향별 한계");   // φ · 최대값은 lastConePhi · lastConeMax(로그)
        }
        lastConePhi = phi; lastConeMax = thMax; lastConeTheta = Mathf.Min(theta, thMax);
        bool hitBehind = hitCone, hitMedial = false;
        lastBehindDeg = Mathf.Asin(Mathf.Clamp(Vector3.Dot(u, -F), -1f, 1f)) * Mathf.Rad2Deg;
        lastMedialDeg = Mathf.Asin(Mathf.Clamp(Vector3.Dot(u, -L), -1f, 1f)) * Mathf.Rad2Deg;

        Vector3 e = a + u * l1;
        // 위팔을 잘랐으면 아래팔은 새 팔꿈치에서 목표를 다시 겨눈다. 안 잘랐으면 2관절 풀이 그대로.
        Vector3 w = (hitBehind || hitMedial) ? (t - e) : (handGoal - elbowGoal);
        w = w.sqrMagnitude > 1e-10f ? w.normalized : u;
        // ★10-22 시뮬 3회차: 어깨를 자른 뒤 아래팔이 목표를 «아무 쪽으로나» 겨눠 팔꿈치가 반대 · 옆으로 꺾였다(신전 한계 넘김 단계).
        if (hitBehind || hitMedial) w = HingeDir(a, u, w);

        // ② 팔꿈치 굴곡: 아래팔과 위팔 연장선 사이 각 ≤ 한계  ⇔  w · (−u) ≤ cos(180 − 한계)
        bool hitElbow = ClampAlong(ref w, -u, Mathf.Cos((180f - elbowFlexionMax) * Mathf.Deg2Rad), F);
        lastElbowFlexDeg = Vector3.Angle(u, w);
        if (hitElbow) hit = hit == null ? "팔꿈치 굴곡" : hit + " · 팔꿈치 굴곡";

        // ③ 위팔 회전: 내린 팔(−U)에서 u까지 최소 회전으로 «앞» · «안쪽»을 옮겨 0 기준을 만든다
        Quaternion swing = Quaternion.FromToRotation(-U, u);
        Vector3 n = swing * F;      // 회전 0일 때 아래팔이 향하는 쪽
        Vector3 m = swing * -L;     // 내회전 쪽
        float along = Vector3.Dot(w, u);
        Vector3 wp = w - along * u;
        // ★10-22 시뮬 분석: 팔꿈치 굴곡 5°에서도 잘라 «어깨 외회전 −90°»가 계속 찍혔다 — 15° 아래는 건너뛴다(펴진 팔은 ApplyTargetRotation이 굴곡 기준으로 잰다)
        if (Vector3.Angle(u, w) >= 15f && wp.sqrMagnitude > 1e-6f)
        {
            float rot = Mathf.Atan2(Vector3.Dot(wp, m), Vector3.Dot(wp, n)) * Mathf.Rad2Deg;
            float clamped = ClampRot(rot, u);
            if (!Mathf.Approximately(rot, clamped))
            {
                float r = clamped * Mathf.Deg2Rad;
                w = u * along + (n * Mathf.Cos(r) + m * Mathf.Sin(r)) * wp.magnitude;
                string rotName = rot > 0f ? "어깨 내회전" : "어깨 외회전";
                hit = hit == null ? rotName : hit + " · " + rotName;
            }
            lastRotDeg = clamped;
        }

        elbowGoal = e;
        handGoal = e + w.normalized * l2;
    }

    /// <summary>그리는 자세(모든 보정 뒤)에 어깨 방향별 경계 · 팔꿈치 굴곡 한계를 건다 — 위치만 고친다(위팔 축 둘레 회전은 그대로).</summary>
    private float dbgPhiTarget, dbgPhiRaw, dbgPhiLimited;
    /// <summary>S공간 방향의 수평 방향각 φ(0 앞 · 90 옆 · ±180 뒤 · −90 안쪽) — 진단용.</summary>
    private float PhiS(Vector3 v)
    {
        BodyAxesS(out Vector3 F, out Vector3 U, out Vector3 L);
        Vector3 hz = v - Vector3.Dot(v, U) * U;
        return hz.sqrMagnitude > 1e-10f ? Mathf.Atan2(Vector3.Dot(hz, L), Vector3.Dot(hz, F)) * Mathf.Rad2Deg : 0f;
    }

    private void EnforceFinalLimits()
    {
        BodyAxesS(out Vector3 F, out Vector3 U, out Vector3 L);
        Vector3 a = space.InverseTransformPoint(upper.position);
        Vector3 b = space.InverseTransformPoint(lower.position);
        Vector3 c = space.InverseTransformPoint(hand.position);
        Vector3 u = (b - a).normalized, w = (c - b).normalized;
        float theta = Vector3.Angle(u, -U);
        Vector3 hz = u - Vector3.Dot(u, U) * U;
        Vector3 hdir = hz.sqrMagnitude > 1e-8f ? hz.normalized : F;
        float phi = Mathf.Atan2(Vector3.Dot(hdir, L), Vector3.Dot(hdir, F)) * Mathf.Rad2Deg;
        float thMax = ShoulderConeMax(phi, Vector3.Angle(u, w));
        if (theta > thMax)
        {
            float rr = thMax * Mathf.Deg2Rad;
            Vector3 u2 = (-U * Mathf.Cos(rr) + hdir * Mathf.Sin(rr)).normalized;
            RotateInS(upper, Quaternion.FromToRotation(u, u2));
            u = u2;
            b = space.InverseTransformPoint(lower.position);
            c = space.InverseTransformPoint(hand.position);
            w = (c - b).normalized;
        }
        // 팔꿈치 굴곡 ≤ 한계 ⇔ w · (−u) ≤ cos(180 − 한계)
        Vector3 w2 = w;
        if (ClampAlong(ref w2, -u, Mathf.Cos((180f - elbowFlexionMax) * Mathf.Deg2Rad), F))
            RotateInS(lower, Quaternion.FromToRotation(w, w2));
    }

    /// <summary>지금 자세(속도 제한 뒤)의 팔꿈치 · 손이 막힘에 들어가 있으면 밀어내고 위치만 다시 맞춘다. 경첩 면은 지킨다.</summary>
    private void CorrectCollisionInPlace()
    {
        Vector3 a = space.InverseTransformPoint(upper.position);
        Vector3 b = space.InverseTransformPoint(lower.position);
        Vector3 c = space.InverseTransformPoint(hand.position);
        Vector3 e = b, h = c;
        string dummy = null;
        ApplyCollision(a, ref e, ref h, ref dummy);
        if ((e - b).sqrMagnitude < 1e-10f && (h - c).sqrMagnitude < 1e-10f) return;
        float l2 = (c - b).magnitude;
        if (applyLimits && frameReady) ApplyJointLimits(a, (b - a).magnitude, l2, h, ref e, ref h, ref dummy);   // 한계가 이긴다(7회차)
        Vector3 uC = (e - a).normalized, wC = h - e;
        if (wC.sqrMagnitude > 1e-10f) h = e + HingeDir(a, uC, wC.normalized) * l2;
        RotateInS(upper, Quaternion.FromToRotation(b - a, e - a));
        Vector3 b2 = space.InverseTransformPoint(lower.position);
        Vector3 c2 = space.InverseTransformPoint(hand.position);
        RotateInS(lower, Quaternion.FromToRotation(c2 - b2, h - b2));
    }

    /// <summary>
    /// 아래팔 방향 w를 경첩 면(위팔 축 u + 해부학 굴곡 방향)의 «굽는 쪽 반»으로 옮긴다(S공간). 팔꿈치는 경첩이다.
    /// 굴곡 방향은 지금 위팔에 붙은 것을 u로 옮긴 위팔에 붙여 옮긴다(위팔 ①이 하는 최소 회전과 같다).
    /// </summary>
    private Vector3 HingeDir(Vector3 aS, Vector3 u, Vector3 w)
    {
        if (!flexRefReady) return w;
        Vector3 uNow = (space.InverseTransformPoint(lower.position) - aS).normalized;
        Vector3 fS = Quaternion.FromToRotation(uNow, u) * space.InverseTransformVector(upper.TransformVector(flexRefInUpper));
        fS -= Vector3.Dot(fS, u) * u;
        if (fS.sqrMagnitude < 1e-10f) return w;
        fS.Normalize();
        Vector3 h2 = u * Vector3.Dot(w, u) + fS * Mathf.Max(0f, Vector3.Dot(w, fS));
        return h2.sqrMagnitude > 1e-10f ? h2.normalized : u;
    }

    /// <summary>
    /// 위팔 회전을 한계(−외 ~ +내) 안으로. 밖이면 «직전 프레임 값에 가까운 쪽» 한계로 자른다.
    /// ★10-22 시뮬 2회차: 아래팔이 뒤로 넘어가 회전이 ±180° 근처를 지날 때 +70 ↔ −90 사이를 오가 160°/프레임 튀었다.
    /// </summary>
    private float ClampRot(float rot, Vector3 uS)
    {
        RotLimits(uS, out float lo, out float hi);
        if (rot >= lo && rot <= hi) { lastClampSide = 0; return rot; }
        // ★10-22 시뮬 6회차: «직전 값에 가까운 쪽»만 보면 −120°(외회전 30° 넘김)를 +70(내회전)으로 잘라 팔꿈치가 뒤로 꺾였다.
        //   → 지금 값에 가까운 한계로 자르되, 두 한계의 한가운데(±180° 건너편) 근처 ±30°에서만 직전에 자른 쪽을 지킨다.
        float dLo = Mathf.Abs(Mathf.DeltaAngle(rot, lo)), dHi = Mathf.Abs(Mathf.DeltaAngle(rot, hi));
        int side = dLo <= dHi ? -1 : 1;
        if (Mathf.Abs(dLo - dHi) < 60f && lastClampSide != 0) side = lastClampSide;
        lastClampSide = side;
        return side < 0 ? lo : hi;
    }

    /// <summary>
    /// 어깨 가동 공간(위팔–몸통) — 팔 방향 φ(0 앞 · 90 옆 · ±180 뒤 · −90 안쪽)마다 들 수 있는 최대 거상 θ(팔 내림 0).
    /// 칸(φ → θmax): −180 신전 · −135 뒤-안 40(★추정) · −90 내전 30 · −45 수평 굴곡(정중면 넘어 45) 90 · 0 앞 156 ·
    ///   37 꼭대기 158(Browne: 견갑면보다 23° 앞 · 값 ★추정) · 90 옆 146 · 90+45 수평 신전 90 · 180 신전(팔꿈치 펴면 50 · 굽히면 60).
    /// 칸 사이는 직선(★모양은 추정 — Engin & Chen 경계 숫자 원문 못 봄). 근거: 명세 문서 어깨 V0.1.
    /// </summary>
    private readonly float[] conePhi = new float[9], coneTh = new float[9];
    private float lastConePhi, lastConeMax, lastConeTheta;
    public float ConePhi => lastConePhi;
    public float ConeMaxDeg => lastConeMax;
    /// <summary>시뮬레이션이 실제 자세를 같은 경계로 재게 연다(따로 계산하지 않는다 — CLAUDE.md 규칙 9).</summary>
    public float ConeMaxAt(float phi, float elbowFlex) => ShoulderConeMax(phi, elbowFlex);

    private float ShoulderConeMax(float phi, float elbowFlex)
    {
        float ext = Mathf.Lerp(shoulderExtensionStraightElbow, shoulderExtensionMax, Mathf.Clamp01(elbowFlex / 90f));
        float[] ph = conePhi, th = coneTh;   // 매 프레임 새로 만들지 않는다
        ph[0] = -180f; th[0] = ext;
        ph[1] = -135f; th[1] = shoulderBehindMedialMax;
        ph[2] = -90f;  th[2] = shoulderAdductionMax;
        ph[3] = -shoulderHorizontalAdductionMax; th[3] = 90f;
        ph[4] = 0f;    th[4] = shoulderFlexionMax;
        ph[5] = 37f;   th[5] = shoulderPeakElevationMax;
        ph[6] = 90f;   th[6] = shoulderAbductionMax;
        ph[7] = 90f + shoulderHorizontalAbductionMax; th[7] = 90f;
        ph[8] = 180f;  th[8] = ext;
        for (int i = 0; i < ph.Length - 1; i++)
            if (phi <= ph[i + 1]) return Mathf.Lerp(th[i], th[i + 1], Mathf.InverseLerp(ph[i], ph[i + 1], phi));
        return ext;
    }

    /// <summary>
    /// 위팔 회전 한계(−외 ~ +내) — 위팔–몸통 거상 θ에 따라:
    ///   θ 0 → 90 : 외 60 → 90 · 내 80 → 70 (팔 내림 JOA 원표 · 90° 외전 AAOS 2차)
    ///   θ 90 → 그 방향 최대 : 외 90 그대로 · 내 70 → 30(★추정 — 최대 거상엔 외회전 쪽이 필요, Browne)
    /// ★10-22 명세: 종전엔 머리 위에서 «외회전 27 이상»을 강제했다(Inui 27은 거상 중 최대값 — 오독).
    /// </summary>
    private void RotLimits(Vector3 uS, out float lo, out float hi)
    {
        lo = -shoulderExternalRotAtSide;
        hi = shoulderInternalRotAtSide;
        if (!frameReady || uS.sqrMagnitude < 1e-8f) return;
        BodyAxesS(out Vector3 F, out Vector3 U, out Vector3 L);
        Vector3 u = uS.normalized;
        float theta = Vector3.Angle(u, -U);
        float t1 = Mathf.Clamp01(theta / 90f);
        lo = -Mathf.Lerp(shoulderExternalRotAtSide, shoulderExternalRotMax, t1);
        hi = Mathf.Lerp(shoulderInternalRotAtSide, shoulderInternalRotMax, t1);
        if (theta > 90f)
        {
            Vector3 hz = u - Vector3.Dot(u, U) * U;
            float phi = hz.sqrMagnitude > 1e-8f ? Mathf.Atan2(Vector3.Dot(hz.normalized, L), Vector3.Dot(hz.normalized, F)) * Mathf.Rad2Deg : 0f;
            float top = Mathf.Max(91f, ShoulderConeMax(phi, 0f));
            hi = Mathf.Lerp(shoulderInternalRotMax, shoulderInternalRotOverhead, Mathf.Clamp01((theta - 90f) / (top - 90f)));
        }
        if (hi < lo + 5f) hi = lo + 5f;
    }

    /// <summary>몸 기준 축(앞 F · 위 U · 바깥 L)을 S공간 단위 벡터로. 외적을 쓰지 않고 직교화만 한다.</summary>
    private void BodyAxesS(out Vector3 F, out Vector3 U, out Vector3 L)
    {
        F = space.InverseTransformVector(chest.TransformVector(frontInChest)).normalized;
        U = space.InverseTransformVector(chest.TransformVector(upInChest)).normalized;
        L = space.InverseTransformVector(chest.TransformVector(lateralInChest)).normalized;
        F = (F - Vector3.Dot(F, U) * U).normalized;
        L = (L - Vector3.Dot(L, U) * U - Vector3.Dot(L, F) * F).normalized;
    }

    private static void AddHit(ref string hit, string what)
    {
        if (hit == null) hit = what;
        else if (hit.IndexOf(what, System.StringComparison.Ordinal) < 0) hit += " · " + what;
    }

    /// <summary>
    /// 지금 위팔 회전(rotNow)과, 팔 축(kW) 둘레 +1°가 해부학 회전을 어느 쪽으로 바꾸는지(sgn)를 잰다(거울 · 좌우 팔 부호를 추론하지 않는다).
    /// ★10-23: 비틀기 ①과 «어깨 먼저» 몫 계산이 같은 잼을 타도록 뽑았다(CLAUDE.md 규칙 9).
    /// </summary>
    private bool MeasureRotSign(Vector3 kW, out float rotNow, out float sgn, out Vector3 uS)
    {
        sgn = 1f;
        uS = (space.InverseTransformPoint(lower.position) - space.InverseTransformPoint(upper.position)).normalized;
        rotNow = MeasureHumeralRot(out Vector3 f, out Vector3 n, out Vector3 m);
        if (float.IsNaN(rotNow)) return false;
        Vector3 fW = upper.TransformVector(flexRefInUpper);
        Vector3 f2 = space.InverseTransformVector(Quaternion.AngleAxis(1f, kW) * fW);
        f2 -= Vector3.Dot(f2, uS) * uS;
        float rot2 = Mathf.Atan2(Vector3.Dot(f2, m), Vector3.Dot(f2, n)) * Mathf.Rad2Deg;
        sgn = Mathf.DeltaAngle(rotNow, rot2) >= 0f ? 1f : -1f;
        return true;
    }

    // ── 막힘(10-22) ─────────────────────────────────────────────────────────────
    //   월드에서 한다(월드는 거울이 없다). 모양 셋:
    //   · 침대 = 시작 때 씬의 침대 메시를 레이로 훑어 잰 «조각별 윗면 · 좌우 모서리»(임시 MeshCollider를 붙였다 뗀다)
    //     ★10-22 시뮬: 뼈 위치로 추정한 폭 0.65m 상자는 실제(좁은 추나 베드)보다 넓어 쉰 자세부터 78mm 관통으로 나왔다.
    //   · 몸통 = 엉덩이 → 어깨 높이 조금 아래 캡슐(★목까지 올리면 머리 위로 든 위팔의 어깨 쪽이 몸통으로 잡혔다)
    //   · 머리 = 머리 본과 두 눈 중점 사이를 중심으로 한 구(★없어서 아래팔이 얼굴을 지나갔다)
    //   팔꿈치 · 손 점뿐 아니라 위팔 · 아래팔 중간도 검사한다 — 중간이 들어가면 끝 관절을 지렛대 비율만큼 밀어낸다.
    private static readonly string[] ShapeName = { "침대", "몸통", "머리" };

    private void MeasureBed()
    {
        Vector3 up = Vector3.up;
        Vector3 longW = Vector3.ProjectOnPlane(headBone.position - hipsBone.position, up);
        if (longW.sqrMagnitude < 0.01f) { Debug.LogWarning("[팔IK] 몸이 누워 있지 않아 침대를 잴 수 없다 — 침대 막힘을 끈다."); return; }
        bedLong = longW.normalized;
        bedSide = Vector3.Cross(up, bedLong);   // 월드 외적(거울 없음) — 폭 방향이라 부호는 쓰지 않는다
        bedOrigin = hipsBone.position;
        Vector3 feet = (footL.position + footR.position) * 0.5f;
        float sFeet = Vector3.Dot(feet - bedOrigin, bedLong) - 0.25f;
        float sHead = Vector3.Dot(headBone.position - bedOrigin, bedLong) + 0.35f;
        bedStep = 0.05f;
        bedS0 = sFeet;
        int n = Mathf.Max(2, Mathf.CeilToInt((sHead - sFeet) / bedStep) + 1);
        bedTop = new float[n]; bedXMin = new float[n]; bedXMax = new float[n];

        // 후보 메시: 환자 밖 · 엉덩이보다 낮고 0.9m 이내 · 크기 4m 이내 · 몸 아래 영역과 겹침
        var footprint = new Bounds(bedOrigin + bedLong * (0.5f * (sFeet + sHead)), Vector3.zero);
        footprint.Encapsulate(bedOrigin + bedLong * sFeet + bedSide * 0.8f + Vector3.down * 1.0f);
        footprint.Encapsulate(bedOrigin + bedLong * sFeet - bedSide * 0.8f);
        footprint.Encapsulate(bedOrigin + bedLong * sHead + bedSide * 0.8f);
        footprint.Encapsulate(bedOrigin + bedLong * sHead - bedSide * 0.8f + Vector3.down * 1.0f);
        var temp = new List<MeshCollider>();
        var mine = new HashSet<Collider>();
        foreach (MeshRenderer mr in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (mr.transform.IsChildOf(animator.transform)) continue;
            if ((target != null && mr.transform == target) || (hint != null && mr.transform == hint)) continue;
            Bounds b = mr.bounds;
            if (b.max.y > hipsBone.position.y + 0.05f || b.max.y < hipsBone.position.y - 0.9f || b.size.magnitude > 4f) continue;
            if (!b.Intersects(footprint)) continue;
            MeshFilter mf = mr.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            if (mr.GetComponent<Collider>() != null) continue;   // 이미 있는 콜라이더는 건드리지 않는다
            MeshCollider mc = mr.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            temp.Add(mc); mine.Add(mc);
        }
        Physics.SyncTransforms();

        int valid = 0;
        float yStart = hipsBone.position.y + 0.5f;
        var hitsBuf = new RaycastHit[32];
        float TopAt(Vector3 xz)
        {
            int c = Physics.RaycastNonAlloc(new Vector3(xz.x, yStart, xz.z), Vector3.down, hitsBuf, 2.0f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < c; i++) if (mine.Contains(hitsBuf[i].collider) && hitsBuf[i].point.y > best) best = hitsBuf[i].point.y;
            return best;
        }
        for (int i = 0; i < n; i++)
        {
            Vector3 c0 = bedOrigin + bedLong * (bedS0 + i * bedStep);
            float top = TopAt(c0);
            bedTop[i] = top; bedXMin[i] = bedXMax[i] = 0f;
            if (float.IsNegativeInfinity(top) || top < hipsBone.position.y - 0.5f) { bedTop[i] = float.NaN; continue; }
            // 가운데에서 바깥으로 걸어가며 윗면(± 6cm)이 이어지는 데까지가 모서리
            float x;
            for (x = 0.02f; x <= 0.8f; x += 0.02f) { float t = TopAt(c0 + bedSide * x); if (float.IsNegativeInfinity(t) || Mathf.Abs(t - top) > 0.06f) break; }
            bedXMax[i] = x - 0.02f;
            for (x = 0.02f; x <= 0.8f; x += 0.02f) { float t = TopAt(c0 - bedSide * x); if (float.IsNegativeInfinity(t) || Mathf.Abs(t - top) > 0.06f) break; }
            bedXMin[i] = -(x - 0.02f);
            valid++;
        }
        foreach (MeshCollider mc in temp) Destroy(mc);

        bedReady = valid > 0;
        bedMeasured = bedReady;
        if (!bedReady)
        {
            Debug.LogWarning($"[팔IK] 침대 메시를 못 찾았다(후보 {temp.Count}개) — 침대 막힘을 끈다. 씬에 침대가 없거나(현실 모드) 엉덩이 아래에 메시가 없다.");
            return;
        }
        int iSh = Mathf.Clamp(Mathf.RoundToInt((Vector3.Dot(upper.position - bedOrigin, bedLong) - bedS0) / bedStep), 0, n - 1);
        Debug.Log($"[팔IK] 침대를 씬에서 쟀다 — 후보 메시 {temp.Count}개 · 조각 {valid}/{n}개({bedStep * 100f:F0}cm 간격) · " +
                  $"어깨 높이 조각: 윗면 y {bedTop[iSh]:F3}(엉덩이 본보다 {(hipsBone.position.y - bedTop[iSh]) * 100f:F1}cm 아래) · " +
                  $"모서리 {bedXMin[iSh]:F2} ~ {bedXMax[iSh]:F2}m(몸 중심선 기준) · 이 팔 어깨는 중심선에서 {Vector3.Dot(upper.position - bedOrigin, bedSide):+0.00;-0.00}m");
    }

    private void UpdateCollisionShapes()
    {
        lastTorsoR = 0.5f * Vector3.Distance(upper.position, otherUpper.position) * torsoRadiusScale;
        Vector3 A = hipsBone.position, ab = neckBone.position - A;
        float len = ab.magnitude;
        Vector3 dir = len > 1e-5f ? ab / len : Vector3.up;
        float kSh = Vector3.Dot((upper.position + otherUpper.position) * 0.5f - A, dir);   // 어깨 높이(척추 축 위)
        torsoA = A;
        torsoB = A + dir * Mathf.Max(0.05f, kSh - 0.04f);
        if (eyeLBone != null && eyeRBone != null)
        {
            Vector3 eyes = (eyeLBone.position + eyeRBone.position) * 0.5f;
            headC = Vector3.Lerp(headBone.position, eyes, 0.5f);
            headR = Mathf.Clamp(1.25f * Vector3.Distance(headBone.position, eyes), 0.08f, 0.13f);
        }
        else { headC = headBone.position; headR = 0.10f; }
    }

    /// <summary>점 p(반지름 r)가 모양에 들어간 깊이(가장 깊은 것)와 밀어낼 방향 · 모양 번호. 들어가지 않았으면 0 이하.</summary>
    private float Pen(Vector3 p, float r, out Vector3 normal, out int shape)
    {
        float best = 0f; normal = Vector3.up; shape = -1;

        // 침대
        if (bedReady)
        {
            Vector3 d = p - bedOrigin;
            float sAlong = Vector3.Dot(d, bedLong), x = Vector3.Dot(d, bedSide);
            int i = Mathf.RoundToInt((sAlong - bedS0) / bedStep);
            if (i >= 0 && i < bedTop.Length && !float.IsNaN(bedTop[i]) && p.y > bedTop[i] - 0.25f)   // 판 두께 아래(침대 밑)는 막지 않는다
            {
                float penTop = bedTop[i] + r - p.y;
                float penIn = Mathf.Min(x - (bedXMin[i] - r), (bedXMax[i] + r) - x);
                if (penTop > 0f && penIn > 0f)
                {
                    float dep = Mathf.Min(penTop, penIn);
                    Vector3 nrm = penTop <= penIn ? Vector3.up : (x > 0.5f * (bedXMin[i] + bedXMax[i]) ? bedSide : -bedSide);
                    if (dep > best) { best = dep; normal = nrm; shape = 0; }
                }
            }
        }
        // 몸통
        {
            Vector3 ab = torsoB - torsoA;
            float k = Mathf.Clamp01(Vector3.Dot(p - torsoA, ab) / Mathf.Max(ab.sqrMagnitude, 1e-8f));
            Vector3 q = torsoA + ab * k, qp = p - q;
            float dist = qp.magnitude, dep = lastTorsoR + r - dist;
            if (dep > best) { best = dep; normal = dist > 1e-6f ? qp / dist : (upper.position - q).normalized; shape = 1; }
        }
        // 머리
        {
            Vector3 qp = p - headC;
            float dist = qp.magnitude, dep = headR + r - dist;
            if (dep > best) { best = dep; normal = dist > 1e-6f ? qp / dist : (upper.position - headC).normalized; shape = 2; }
        }
        return best;
    }

    /// <summary>
    /// 팔꿈치 · 손 목표를 침대 · 몸통 · 머리 밖으로(월드). 마디 길이를 지키며 세 번까지 되풀이한다.
    ///   위팔 중간(0.5 · 0.8)이 들어가면 팔꿈치를 깊이 / 비율만큼, 아래팔 중간(0.35 · 0.7)이 들어가면 손을 같은 식으로 민다.
    /// </summary>
    private void ApplyCollision(Vector3 aS, ref Vector3 elbowGoal, ref Vector3 handGoal, ref string hit)
    {
        if (hipsBone == null || neckBone == null || headBone == null || otherUpper == null || footL == null || footR == null || chest == null) return;
        UpdateCollisionShapes();

        Vector3 sh = space.TransformPoint(aS);
        Vector3 e = space.TransformPoint(elbowGoal), h = space.TransformPoint(handGoal);
        float l1W = Vector3.Distance(sh, e), l2W = Vector3.Distance(e, h);
        float r = armRadius;
        bool[] touched = touchedBuf;
        touched[0] = touched[1] = touched[2] = false;
        bool any = false;

        for (int it = 0; it < 3; it++)
        {
            bool moved = false;
            // 위팔
            float dE = Pen(e, r, out Vector3 nE, out int kE);
            if (dE > 0f) { e += nE * dE; touched[kE] = moved = true; }
            for (int j = 0; j < 2; j++)
            {
                float f = j == 0 ? 0.5f : 0.8f;
                float d = Pen(Vector3.Lerp(sh, e, f), r, out Vector3 nn, out int k);
                if (d > 0f) { e += nn * (d / f); touched[k] = moved = true; }
            }
            Vector3 se = e - sh;
            e = sh + (se.sqrMagnitude > 1e-10f ? se.normalized : Vector3.down) * l1W;

            // 아래팔
            Vector3 eh = h - e;
            h = e + (eh.sqrMagnitude > 1e-10f ? eh.normalized : Vector3.down) * l2W;
            float dH = Pen(h, r, out Vector3 nH, out int kH);
            if (dH > 0f) { h += nH * dH; touched[kH] = moved = true; }
            for (int j = 0; j < 2; j++)
            {
                float f = j == 0 ? 0.35f : 0.7f;
                float d = Pen(Vector3.Lerp(e, h, f), r, out Vector3 nn, out int k);
                if (d > 0f) { h += nn * (d / f); touched[k] = moved = true; }
            }
            eh = h - e;
            h = e + (eh.sqrMagnitude > 1e-10f ? eh.normalized : Vector3.down) * l2W;

            any |= moved;
            if (!moved) break;
        }

        for (int k = 0; k < 3; k++) if (touched[k]) AddHit(ref hit, ShapeName[k]);
        if (any)
        {
            elbowGoal = space.InverseTransformPoint(e);
            handGoal = space.InverseTransformPoint(h);
        }
    }

    /// <summary>지금 위팔 회전(°, +내 / −외). 팔을 내린 자리에서 아래팔이 앞을 볼 때 0. f = 굴곡 방향 · n = 0° 쪽 · m = 내회전 쪽(S공간). 못 재면 NaN.</summary>
    private float MeasureHumeralRot(out Vector3 f, out Vector3 n, out Vector3 m)
    {
        f = n = m = Vector3.zero;
        if (!frameReady || !flexRefReady) return float.NaN;
        BodyAxesS(out Vector3 F, out Vector3 U, out Vector3 L);
        Vector3 aS = space.InverseTransformPoint(upper.position);
        Vector3 u = (space.InverseTransformPoint(lower.position) - aS).normalized;
        Quaternion swing = Quaternion.FromToRotation(-U, u);
        n = swing * F; m = swing * -L;
        f = space.InverseTransformVector(upper.TransformVector(flexRefInUpper));
        f -= Vector3.Dot(f, u) * u;
        if (f.sqrMagnitude < 1e-10f) return float.NaN;
        return Mathf.Atan2(Vector3.Dot(f, m), Vector3.Dot(f, n)) * Mathf.Rad2Deg;
    }

    // ── 잡은 손의 회전(10-22) ─────────────────────────────────────────────────────
    //   목표 구가 잡힌 뒤 돈 만큼(delta, 월드)을 둘로 나눈다.
    //   · 팔 축(어깨 → 손) 둘레 성분 = 비틀기 → 팔이 펴졌을 때만 위팔을 제 축으로 돌린다(굽어 있으면 위치가 회전을 정한다)
    //   · 나머지(swing) = 손목 꺾기 → 손가락이 가리키는 쪽을 돌리고 방향마다 다른 손목 한계(사분면 타원) 안으로 자른다
    //   회전은 월드에서 «벡터»로 만들어 S공간에 옮긴 뒤 FromToRotation으로 다시 짓는다 — 각 · 축 부호를 거울 너머로 넘기지 않는다.
    private void ApplyTargetRotation(Vector3 a, ref string hit)
    {
        Quaternion delta = target.rotation * Quaternion.Inverse(targetRot0);
        Vector3 armW = hand.position - upper.position;
        if (armW.sqrMagnitude < 1e-8f) return;
        Vector3 kW = armW.normalized;

        // swing-twist 분해(팔 축 k)
        Vector3 v = new Vector3(delta.x, delta.y, delta.z);
        Vector3 pr = Vector3.Dot(v, kW) * kW;
        Quaternion twistQ = new Quaternion(pr.x, pr.y, pr.z, delta.w);
        float tm = Mathf.Sqrt(twistQ.x * twistQ.x + twistQ.y * twistQ.y + twistQ.z * twistQ.z + twistQ.w * twistQ.w);
        twistQ = tm > 1e-6f ? new Quaternion(twistQ.x / tm, twistQ.y / tm, twistQ.z / tm, twistQ.w / tm) : Quaternion.identity;
        Quaternion swingQ = delta * Quaternion.Inverse(twistQ);
        twistQ.ToAngleAxis(out float twistDeg, out Vector3 twistAxis);
        if (twistDeg > 180f) twistDeg -= 360f;
        if (Vector3.Dot(twistAxis, kW) < 0f) twistDeg = -twistDeg;

        // ① 비틀기 — 팔꿈치 굴곡 15° 이하에서 전부, 30° 이상에서 0(굽은 팔은 경첩 · 위치가 회전을 정한다)
        //   ★10-22 시뮬: 비튼 각을 매 프레임 «덧붙여» 회전이 −90 ↔ +70으로 돌고(160°/프레임) 원위치 뒤에도 +68°가 남았다.
        //   → 절대값으로 맞춘다: 목표 회전 = 시작 자세 회전(rotRest) + 잡은 손이 비튼 각. 한계로 자르고, 지금 회전에서 그 값으로 한 번에 돌린다.
        float flex = 180f - Vector3.Angle(upper.position - lower.position, hand.position - lower.position);
        float straight = Mathf.InverseLerp(30f, 15f, flex);
        lastTwistDeg = twistDeg;

        // ①-0 아래팔 회내 · 회외 — 손을 비튼 각을 아래팔이 먼저 받는다(★10-22 사용자: «팔을 들고 손을 돌리면 어깨가 아니라 팔꿈치(아래팔)가 돈다»)
        //   잡은 손 회전을 «팔꿈치 → 손» 축 둘레로 다시 나눠 그만큼(한계 안) 손과 비틀림 보조 본을 돌린다. 넘는 만큼만 아래 ①이 어깨로 넘긴다.
        float foreWorld = 0f;
        lastForeDeg = 0f;
        // ★10-23 어깨 먼저(TwistShoulderFirst): 어깨가 한계 안에서 받을 몫을 먼저 정하고(아래 ①과 같은 잼 · 같은 한계), 아래팔은 나머지만 받는다.
        float shoulderShare = 0f;
        if (TwistShoulderFirst && straight > 0f && !float.IsNaN(rotRest) && MeasureRotSign(kW, out float rotNow0, out float sgn0, out Vector3 uS0))
        {
            float want0 = rotRest + sgn0 * twistDeg;
            float cl0 = applyLimits ? ClampRot(want0, uS0) : want0;
            shoulderShare = sgn0 * (cl0 - rotRest) * straight;   // 팔 축 둘레 월드 각(펴진 팔에서는 아래팔 축과 거의 같다)
        }
        Vector3 faW = hand.position - lower.position;
        if (supWorldSign != 0f && faW.sqrMagnitude > 1e-8f)
        {
            Vector3 fa = faW.normalized;
            Vector3 vv = new Vector3(delta.x, delta.y, delta.z);
            Vector3 pp = Vector3.Dot(vv, fa) * fa;
            Quaternion tq = new Quaternion(pp.x, pp.y, pp.z, delta.w);
            float tn = Mathf.Sqrt(tq.x * tq.x + tq.y * tq.y + tq.z * tq.z + tq.w * tq.w);
            tq = tn > 1e-6f ? new Quaternion(tq.x / tn, tq.y / tn, tq.z / tn, tq.w / tn) : Quaternion.identity;
            tq.ToAngleAxis(out float foreTwist, out Vector3 foreAxis);
            if (foreTwist > 180f) foreTwist -= 360f;
            if (Vector3.Dot(foreAxis, fa) < 0f) foreTwist = -foreTwist;
            foreTwist -= shoulderShare;

            float sup = supWorldSign * foreTwist;                                   // + 회외(시작 자세에서 더한 만큼)
            // 한계는 해부학 중립 기준(10-22 명세) — 시작 자세가 이미 forearmRestSupDeg만큼 돌아 있다
            float supCl = applyLimits ? Mathf.Clamp(sup, -forearmPronationMax - forearmRestSupDeg, forearmSupinationMax - forearmRestSupDeg) : sup;
            if (applyLimits && !Mathf.Approximately(sup, supCl)) AddHit(ref hit, sup > 0f ? "아래팔 회외" : "아래팔 회내");
            foreWorld = supWorldSign * supCl;                                       // 월드에서 축 둘레로 돌릴 각
            lastForeDeg = forearmRestSupDeg + supCl;

            // 손을 팔꿈치 → 손 축 둘레로(벡터로 짓고 S공간에 옮긴다 — 거울 너머로 각 부호를 넘기지 않는다)
            Vector3 pW0 = Vector3.ProjectOnPlane(Mathf.Abs(fa.y) < 0.9f ? Vector3.up : Vector3.forward, fa).normalized;
            Vector3 pW1 = Quaternion.AngleAxis(foreWorld, fa) * pW0;
            Vector3 aS = space.InverseTransformVector(fa).normalized;
            Quaternion qFore = Quaternion.FromToRotation(Vector3.ProjectOnPlane(space.InverseTransformVector(pW0), aS),
                                                         Vector3.ProjectOnPlane(space.InverseTransformVector(pW1), aS));
            RotateInS(hand, qFore);
            if (foreTwist1 != null) SetFromRest(foreTwist1, foreTwist1Rest, qFore, 0.25f);   // 팔꿈치 쪽 1/4
            if (foreTwist2 != null) SetFromRest(foreTwist2, foreTwist2Rest, qFore, 0.5f);    // 01의 자식 — 합쳐서 3/4(손목 쪽)
        }
        // 어깨로 넘길 몫 = 비튼 각 − 아래팔이 받은 각(펴진 팔에서는 두 축이 거의 같다)
        twistDeg -= foreWorld;

        if (straight > 0f && !float.IsNaN(rotRest))
        {
            float rotNow = MeasureHumeralRot(out Vector3 f, out Vector3 n, out Vector3 m);
            if (!float.IsNaN(rotNow) && MeasureRotSign(kW, out _, out float sgn, out Vector3 uS))
            {
                float want = rotRest + sgn * twistDeg;
                float cl = applyLimits ? ClampRot(want, uS) : want;
                if (applyLimits && !Mathf.Approximately(want, cl)) AddHit(ref hit, want > 0f ? "어깨 내회전" : "어깨 외회전");
                float goal = rotNow + Mathf.DeltaAngle(rotNow, cl) * straight;
                float rr = goal * Mathf.Deg2Rad;
                RotateInS(upper, Quaternion.FromToRotation(f, n * Mathf.Cos(rr) + m * Mathf.Sin(rr)));
                lastRotDeg = goal;
            }
        }

        // ② 손목 꺾기 — 방향마다 한계가 다르다(10-22 명세): 배굴 70 · 장굴 85 · 요측 20 · 척측 35, 사이는 사분면 타원(Li 2005 달걀 모양 · 대각 숫자 ★미확보)
        //   축(중립 손가락 방향 · 손등 · 요측)은 아래팔 본에 붙여 두고, 이번 프레임 아래팔 회내 · 회외(foreWorld)만큼 같이 돌린다.
        if (finger == null) return;
        Vector3 foreW = hand.position - lower.position;
        Vector3 fingW = finger.position - hand.position;
        if (foreW.sqrMagnitude < 1e-8f || fingW.sqrMagnitude < 1e-8f) return;
        Vector3 wantW = swingQ * fingW.normalized;
        float bendDeg = Vector3.Angle(foreW, wantW);
        if (wristAxesReady && dorsalReady)
        {
            Vector3 fa2 = foreW.normalized;
            Quaternion proQ = Quaternion.AngleAxis(foreWorld, fa2);
            Vector3 nW = (proQ * lower.TransformVector(wristNeutralInLower)).normalized;
            Vector3 dW = proQ * lower.TransformVector(dorsalInLower); dW = (dW - Vector3.Dot(dW, nW) * nW).normalized;
            Vector3 rW = proQ * lower.TransformVector(wristRadialInLower); rW = (rW - Vector3.Dot(rW, nW) * nW - Vector3.Dot(rW, dW) * dW).normalized;
            // 굽힘은 경도(손등 ↔ 손바닥 둘레 각), 편위는 위도(그 면에서 벗어난 각)로 나눈다.
            //   ★10-22 시뮬: 둘 다 atan2(·, z)로 재면 90° 넘게 꺾을 때(z < 0) 편위가 ±180 가까이 튀어 배굴 100°가 18°로 잘렸다.
            float z = Vector3.Dot(wantW, nW);
            float ext = Mathf.Atan2(Vector3.Dot(wantW, dW), z) * Mathf.Rad2Deg;
            float dev = Mathf.Asin(Mathf.Clamp(Vector3.Dot(wantW, rW), -1f, 1f)) * Mathf.Rad2Deg;
            float lx = ext >= 0f ? wristExtensionMax : wristFlexionMax;
            float ly = dev >= 0f ? wristRadialMax : wristUlnarMax;
            float e = (ext * ext) / Mathf.Max(1f, lx * lx) + (dev * dev) / Mathf.Max(1f, ly * ly);
            if (applyLimits && e > 1f)
            {
                float k = 1f / Mathf.Sqrt(e);
                ext *= k; dev *= k;
                float ce = Mathf.Cos(dev * Mathf.Deg2Rad);
                wantW = (nW * (ce * Mathf.Cos(ext * Mathf.Deg2Rad)) + dW * (ce * Mathf.Sin(ext * Mathf.Deg2Rad)) + rW * Mathf.Sin(dev * Mathf.Deg2Rad)).normalized;
                AddHit(ref hit, "손목");
            }
            lastWristExt = ext; lastWristDev = dev;
            bendDeg = Vector3.Angle(nW, wantW);
        }
        lastWristDeg = bendDeg;
        Vector3 fromS = space.InverseTransformVector(fingW);
        Vector3 toS = space.InverseTransformVector(wantW);
        RotateInS(hand, Quaternion.FromToRotation(fromS, toS));
    }

    /// <summary>
    /// S공간 회전 q를 뼈에 가중치만큼 얹는다 — localRotation만 쓴다(10-22 정정, 클래스 주석 참고).
    /// S → bone.parent 사이 로컬 회전의 곱 R로 q를 뼈 부모 공간으로 옮긴다: qLocal = R⁻¹ · q · R.
    /// InverseTransformPoint와 같은 로컬 사슬이므로 거울(음수 스케일)이 S 위 어디에 있든 상관없다.
    /// </summary>
    private void RotateInS(Transform bone, Quaternion q)
    {
        if (weight < 1f) q = Quaternion.Slerp(Quaternion.identity, q, weight);
        Quaternion r = Quaternion.identity;
        for (Transform t = bone.parent; t != space; t = t.parent) r = t.localRotation * r;
        bone.localRotation = (Quaternion.Inverse(r) * q * r) * bone.localRotation;
    }

    private static string NameOf(Transform t) => t != null ? t.name : "(없음)";

    private static Transform FindChildContains(Transform root, string part)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t != root && t.name.IndexOf(part, System.StringComparison.Ordinal) >= 0) return t;
        return null;
    }

    /// <summary>S공간 회전 q의 frac만큼을 rest 로컬 회전에 얹어 bone에 쓴다(애니가 안 쓰는 보조 본 — 매 프레임 rest에서 새로 정하니 쌓이지 않는다).</summary>
    private void SetFromRest(Transform bone, Quaternion rest, Quaternion q, float frac)
    {
        Quaternion qf = Quaternion.Slerp(Quaternion.identity, q, frac);
        Quaternion r = Quaternion.identity;
        for (Transform t = bone.parent; t != space; t = t.parent) r = t.localRotation * r;
        bone.localRotation = (Quaternion.Inverse(r) * qf * r) * rest;
    }

    private void OnDrawGizmos()
    {
        if (!ready || upper == null || lower == null || hand == null) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(upper.position, lower.position);
        Gizmos.DrawLine(lower.position, hand.position);
        Gizmos.DrawWireSphere(upper.position, 0.02f);
        Gizmos.DrawWireSphere(lower.position, 0.02f);

        if (target != null)
        {
            Gizmos.color = lastReachable ? Color.green : Color.red;
            Gizmos.DrawWireSphere(target.position, handleSize * 0.75f);
            Gizmos.DrawLine(hand.position, target.position);
        }
        if (hint != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(lower.position, hint.position);
        }

        // 막힘 모양 — 노란 선 = 침대 조각마다 윗면 좌우 모서리 · 초록 = 몸통 캡슐 · 하늘색 = 머리 구
        if (applyCollision && bedReady && bedTop != null)
        {
            Gizmos.color = new Color(1f, 0.9f, 0.1f);
            for (int i = 0; i < bedTop.Length; i++)
            {
                if (float.IsNaN(bedTop[i])) continue;
                Vector3 c = bedOrigin + bedLong * (bedS0 + i * bedStep);
                c.y = bedTop[i];
                Gizmos.DrawLine(c + bedSide * bedXMin[i], c + bedSide * bedXMax[i]);
            }
        }
        if (applyCollision && lastTorsoR > 0f)
        {
            Gizmos.color = new Color(0.2f, 0.9f, 0.4f);
            Gizmos.DrawLine(torsoA, torsoB);
            Gizmos.DrawWireSphere(torsoA, lastTorsoR);
            Gizmos.DrawWireSphere(torsoB, lastTorsoR);
            Gizmos.DrawWireSphere((torsoA + torsoB) * 0.5f, lastTorsoR);
            Gizmos.color = new Color(0.4f, 0.8f, 1f);
            Gizmos.DrawWireSphere(headC, headR);
        }

        // 몸 기준 축(어깨에서 0.15m) — 초록 = 앞 · 파랑 = 위 · 주황 = 바깥. 환자 몸과 맞는지 눈으로 확인한다.
        if (frameReady && chest != null)
        {
            Vector3 o = upper.position;
            Vector3 fw = chest.TransformVector(frontInChest).normalized;
            Vector3 uw = chest.TransformVector(upInChest).normalized;
            Vector3 lw = chest.TransformVector(lateralInChest).normalized;
            Gizmos.color = Color.green; Gizmos.DrawLine(o, o + fw * 0.15f);
            Gizmos.color = Color.blue; Gizmos.DrawLine(o, o + uw * 0.15f);
            Gizmos.color = new Color(1f, 0.55f, 0f); Gizmos.DrawLine(o, o + lw * 0.15f);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(o + fw * 0.16f, "앞");
            UnityEditor.Handles.Label(o + uw * 0.16f, "위");
            UnityEditor.Handles.Label(o + lw * 0.16f, "바깥");
#endif
        }

#if UNITY_EDITOR
        // 에디터 전용 글자. 런타임 코드는 이 블록 안의 어떤 함수도 부르지 않는다(CLAUDE.md 규칙 10).
        UnityEditor.Handles.Label(lower.position, $"팔꿈치 {lastElbowAngle:F0}°");
#endif
    }
}
