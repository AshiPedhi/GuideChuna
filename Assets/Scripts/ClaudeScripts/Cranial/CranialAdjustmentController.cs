using UnityEngine;
using Oculus.Interaction;         // HandVisual
using Oculus.Interaction.Input;   // HandJointId

/// <summary>
/// 두개골 교정 술기 오케스트레이터 (씬에 1개, 인스펙터 와이어링).
/// 파지 타겟 2개 / 깊이 가이드 2개 / 호흡 HUD를 보유하고, 시나리오 조건
/// (GripPointCondition / PressureCondition / BreathingCondition)이 호출할 상태 API를 노출한다.
///
/// 술기 흐름 (substep 3개):
///   ① 파지      : BothGripped 게이트 (견착 전, 손 추적 O)
///   ② 압력 조절 : BothGripped 유지 (누르는 방향·세기는 안내로만, 깊이는 판정 안 함 — useDepthJudging 참고)
///   ③ 견착·호흡 : 손 판정 정지 + 호흡 윈도우 (자세 프록시 + 3호흡, 견착이라 손 추적 불가)
/// </summary>
public class CranialAdjustmentController : MonoBehaviour
{
    [Header("=== 시나리오 매칭 (술기별 리그 분리, A안) ===")]
    [Tooltip("이 리그가 담당하는 시나리오 이름(ScenarioConfig.scenarioName과 동일). " +
             "씬에 두개골 리그가 여러 개(OM/PM 등)면 ScenarioManager가 이 값으로 맞는 리그만 활성화한다. " +
             "비우면 '레거시 기본'으로 취급(이름 매칭 실패 시 폴백 대상 = 기존 OM 리그). 예: 두개골PM교정")]
    [SerializeField] private string scenarioName;
    /// <summary>이 리그가 담당하는 시나리오 이름(비면 레거시 기본). ScenarioManager가 리그 선택에 사용.</summary>
    public string ScenarioName => scenarioName;

    [Header("=== 파지 (기능1, Model B: 손가락별 5점) ===")]
    [Tooltip("왼손(후두) 파지점들. 거치만이면 1개(검지), 손가락별 파지면 여러 개를 손가락별로 배치(각 GripPointTarget.finger 지정).")]
    [SerializeField] private GripPointTarget[] leftGrips;
    [Tooltip("오른손(측두) 파지점들. 파이브핑거홀드 = 5개(엄지~새끼)를 손가락별로 배치(각 GripPointTarget.finger 지정).")]
    [SerializeField] private GripPointTarget[] rightGrips;

    [Header("=== 진단 단계 (양손 파지 유지, 압력·포즈 무관) ===")]
    [Tooltip("진단 substep별 단계 정의. CSV의 conditionType=cranialTouch + conditionParams=<단계 ID>로 선택된다.\n" +
             "  · OM 진단1 : 양손 측두부 감싸기(양손 손바닥)   — 3초 유지\n" +
             "  · OM 진단2 : 양손 후두부 모아 베개(양손 손바닥) — 8초 유지\n" +
             "  · PM·PJ 진단1 : 자세 2개(ⓐ왼손 후두부+오른손 측두 / ⓑ왼손 측두+오른손 후두부) 각 3초\n" +
             "비워 두면 아래 레거시(diagnosisRightGrips) 방식으로 폴백한다.")]
    [SerializeField] private CranialDiagnosisStage[] diagnosisStages = new CranialDiagnosisStage[0];

    [Tooltip("유지 타이머 도중 파지가 풀렸을 때 봐주는 시간(초). 이 시간 안에 다시 잡으면 누적이 유지되고, " +
             "넘기면 0으로 초기화된다. 0으로 두면 '떨어지는 즉시 초기화'. 기본 0.5초 = 추적 튐만 흡수.")]
    [SerializeField] private float gripGraceSeconds = 0.5f;

    [Tooltip("★기본 ON. 자세를 배열 순서대로(좌 → 우) 하나씩 진행한다 — 지금 해야 하는 자세의 파지점만 보이고 " +
             "그 자세만 판정한다. 진행 상황은 ProgressCircle에 '1/2' 카운트로 표시된다.\n" +
             "가이드 손 녹화가 좌→우 한 방향 시연이라 순서를 맞춰야 해서 도입했다(스텝을 좌/우로 쪼개는 대신).\n" +
             "끄면 예전처럼 순서 무관(어느 자세든 먼저 채워도 됨).")]
    [SerializeField] private bool enforcePoseOrder = true;

    [Header("=== 진단 호흡 유도 메시지 (선택) ===")]
    // ★07-30 사용자 요구로 방식 변경: '숨을 마시고/내쉬고' 4초 교대 → **진단 단계에서 한 번만 띄우고 스스로 사라짐**.
    //   계속 떠 있으면 시야를 가리고, 실제 호흡 페이스는 환자 굴곡·신전 애니와 숨소리가 이미 전달한다.
    //   표시 구간은 진단(showBreathingCue를 켠 단계)뿐 — ③ 견착·호흡의 링·카운트(BreathingSyncHUD)와는 별개 UI다.
    [Tooltip("진단 단계 시작 때 한 번 떴다 사라지는 안내 문구용 텍스트. 새로 만든 텍스트 오브젝트를 넣으면 된다. " +
             "**비워 둬도 된다** — 비어 있으면 메시지만 생략하고 단계는 정상 진행된다.")]
    [SerializeField] private TMPro.TMP_Text breathingCueText;
    [Tooltip("문구와 배경을 함께 켜고 끌 루트 오브젝트(선택). 비우면 breathingCueText의 GameObject를 쓴다. " +
             "텍스트가 배경 이미지(패널)의 자식이면 여기에 그 배경을 넣어야 배경까지 같이 사라진다.")]
    [SerializeField] private GameObject breathingCueRoot;
    [Tooltip("표시할 안내 문구. 한 번만 뜨고 사라진다.")]
    [SerializeField] private string breathingCueMessage = "환자의 호흡에 맞춰 두개골의 움직임을 느껴보세요";
    [Tooltip("문구가 떠 있는 시간(초). 너무 짧으면 다 읽지 못하고, 너무 길면 시야를 가린다.")]
    [SerializeField] private float cueVisibleSeconds = 5f;
    [Tooltip("사라질 때 서서히 흐려지는 시간(초). 0이면 즉시 사라진다.")]
    [SerializeField] private float cueFadeSeconds = 0.6f;
    [Tooltip("★기본 ON. 켜면 단계별 showBreathingCue 값과 무관하게 '모든 진단 단계'에서 문구를 띄운다. " +
             "(씬에 OM 진단1·진단2는 showBreathingCue=0으로 저장돼 있어, 이 옵션이 없으면 OM에선 문구가 안 뜬다.) " +
             "특정 단계만 골라 띄우려면 끄고 단계별 showBreathingCue로 제어할 것.")]
    [SerializeField] private bool cueOnAllDiagnosisStages = true;

    [Header("=== [레거시] 진단 촉진 파지 (diagnosisStages 미배선 시 폴백) ===")]
    [Tooltip("구버전 진단 배선. 위 diagnosisStages를 채우면 이 배열은 쓰이지 않는다.")]
    [SerializeField] private GripPointTarget[] diagnosisRightGrips;

    [Header("=== 깊이 압력 (기능2) — 기본 비활성 ===")]
    [Tooltip("★기본 OFF. 켜면 손끝이 파지 영점에서 얼마나 들어갔는지를 재서 파지 구체를 압력 색으로 칠하고 " +
             "'적정 텐션 존 유지'로 압력조절 단계를 통과시킨다.\n" +
             "끄면(기본) 깊이를 아예 재지 않고, 압력조절 단계는 '파지 유지'만으로 통과한다 — " +
             "VR엔 반력이 없어 누르는 깊이가 실제 술기의 저항감을 대변하지 못하므로 판정에서 제외한 것.\n" +
             "아래 leftDepth/rightDepth 배선은 그대로 둬도 무방하다(이 값이 꺼져 있으면 동작하지 않음).")]
    [SerializeField] private bool useDepthJudging = false;
    [SerializeField] private DepthPressureGuide leftDepth;
    [SerializeField] private DepthPressureGuide rightDepth;

    /// <summary>깊이(압력) 측정·판정을 쓰는가. 기본 false = 파지 유지만으로 판정.</summary>
    public bool UseDepthJudging => useDepthJudging;

    [Header("=== 호흡 (기능3) ===")]
    [SerializeField] private BreathingSyncHUD breathingHUD;

    [Tooltip("★술기마다 호흡법이 다르다. 호흡 윈도우를 열 때 HUD에 이 값을 밀어 넣는다(HUD는 씬 공유라 리그별 지정 필요).\n" +
             "  · OM  = 3회 (호흡 주기에 맞춰 힘을 넣고 빼기를 반복)\n" +
             "  · PJ  = 국면마다 다르다 → CSV conditionParams로 지정한다(호흡유도 1회 / 교정 3회, 첫 회는 크게).\n" +
             "0으로 두면 HUD 인스펙터 값을 그대로 쓴다. ※이 값은 '횟수'다 — 들숨 길이는 아래 항목이다.\n" +
             "★CSV에 breaths=/inhale=/exhale=/start=/firstScale= 이 있으면 그쪽이 이 값을 이긴다.")]
    [SerializeField] private int breathCountOverride = 0;
    [Tooltip("이 술기의 들숨 길이(초). 0이면 HUD 값 유지.")]
    [SerializeField] private float inhaleSecondsOverride = 0f;
    [Tooltip("이 술기의 날숨 길이(초). 0이면 HUD 값 유지.")]
    [SerializeField] private float exhaleSecondsOverride = 0f;
    [Tooltip("이 술기의 호흡을 어느 위상부터 시작할지. Keep = HUD 인스펙터 값 유지.\n" +
             "  · OM·PM = Inhale(들숨부터)\n" +
             "  · PJ    = 두 국면 모두 '들이마신 뒤 내쉰다'라 Inhale이며, CSV의 start=inhale이 이 값을 덮어쓴다.")]
    [SerializeField] private BreathingSyncHUD.StartPhase breathStartPhaseOverride = BreathingSyncHUD.StartPhase.Keep;

    [Header("=== 자세 안정화 (삼각근-이마, 호흡 게이트) ===")]
    [Tooltip("호흡 단계에서 손 대신 판정하는 자세 프록시(헤드셋-이마 근접). " +
             "비우면 호흡은 순수 3회 타이머로 동작(자세 판정 없음).")]
    [SerializeField] private CranialPostureStabilizer postureStabilizer;

    [Tooltip("이마 견착 위치 가이드(어깨를 댈 자리 표시). 비우면 이 리그 하위에서 자동으로 찾는다. " +
             "★어깨 접촉을 판정하지 않는다 — Quest에 어깨 트래킹 소스가 없어 '여기에 대라'는 표시만 한다. " +
             "표시 구간은 CSV conditionParams의 brace 토큰이 정한다.")]
    [SerializeField] private ShoulderBraceGuide braceGuide;

    [Header("=== 견착·호흡(③) 렌더링 정리 (머리 숙임 부작용 방지) ===")]
    [Tooltip("환자 모델 루트. ③에서 머리를 숙여 자세가 성립(카메라가 환자에 근접)하면 이 하위 렌더러를 꺼서 " +
             "near-clip 뚫림/오버드로우를 막고, 고개를 들면 복원한다. CranialRig 하위(파지 구체 등)는 자동 제외. " +
             "비우면 태그 'Patient'로 자동 탐색.")]
    [SerializeField] private Transform patientModelRoot;

    [Tooltip("③ 견착·호흡 진입 시 손 메시를 숨길지. 원래 목적=견착으로 손이 FOV를 벗어나 튀는 손 비주얼 제거. " +
             "★현재 테스트 위해 기본 OFF — 손이 계속 보인다.")]
    [SerializeField] private bool hideHandsDuringBreathing = false;

    [Tooltip("③ 견착·호흡에서 머리를 숙여 카메라가 환자에 근접했을 때 환자 모델을 숨길지. " +
             "원래 목적=near-clip 뚫림·오버드로우 방지. ★현재 테스트 위해 기본 OFF — 환자가 계속 보인다. " +
             "켜면 patientModelRoot 하위 렌더러를 근접 시 끄고 고개를 들면 복원한다.")]
    [SerializeField] private bool hidePatientDuringBreathing = false;

    [Header("=== 리듬 인디케이터 (진단/재평가 시각화, 선택) ===")]
    [Tooltip("두개골 리듬 프록시. 호흡 교정 완료 전=비대칭(진단), 완료 후=대칭(재평가)으로 자동 전환.")]
    [SerializeField] private CranialRhythmIndicator rhythmIndicator;

    [Header("=== 라이브 손끝 주입 (선택) ===")]
    [Tooltip("라이브 player HandVisual(왼손=후두). 지정하면 검지 끝을 leftDepth.fingertip으로 자동 주입. " +
             "비우면 인스펙터에 직접 연결한 fingertip을 그대로 사용(=주입 안 함).")]
    [SerializeField] private HandVisual leftHandVisual;
    [Tooltip("라이브 player HandVisual(오른손=측두). 비우면 rightDepth.fingertip 인스펙터값 유지.")]
    [SerializeField] private HandVisual rightHandVisual;

    [Header("=== 파지 트리거 콜라이더 자동생성 (Option B) ===")]
    [Tooltip("켜면 라이브 손끝(HandVisual)에 파지 판정용 트리거 콜라이더를 런타임 생성해 " +
             "각 파지점(GripPointTarget)이 지정한 손가락 끝에 자동 연결한다(leftGrips/rightGrips를 finger별로 매핑). " +
             "검지끝 본이 런타임 생성되어 인스펙터로 콜라이더를 연결할 수 없을 때 사용(Option B). " +
             "끄면 GripPointTarget에 인스펙터로 직접 연결한 콜라이더를 그대로 사용(Option A).")]
    [SerializeField] private bool autoCreateFingerColliders = true;
    [Tooltip("자동 생성할 손끝 트리거 콜라이더 반경 (m, 기본 1.2cm)")]
    [SerializeField] private float fingerColliderRadius = 0.012f;
    [Tooltip("손바닥(Palm) 파지점의 콜라이더 반경 (m, 기본 5cm). 왼손 후두골 거치용 — 손 중앙(중지 MCP)에 생성.")]
    [SerializeField] private float palmColliderRadius = 0.05f;

    [Header("=== 포즈 인식 연동 (M1 이후) ===")]
    [Tooltip("HandPoseComparator 등 포즈 인식 결과를 grip에 주입. M1(파지 포즈 재녹화) 완료 후 연결.")]
    [SerializeField] private bool drivePoseFromComparator = false;
    // TODO(M1): PoseData/HandPoseComparator 참조를 추가하고 Update에서
    //   foreach (var g in leftGrips)  g.PoseRecognized = comparator.ComparePose(leftHand, leftGuidePoses, out _);
    //   foreach (var g in rightGrips) g.PoseRecognized = comparator.ComparePose(rightHand, rightGuidePoses, out _);
    // 로 매 프레임 주입한다. 그 전까지는 GripPointTarget.bypassPoseCheck로 트리거만으로 테스트.

    [Header("=== 디버그 ===")]
    [Tooltip("켜면 압력 학습(②) substep이 완료돼도 다음 단계로 넘어가지 않는다(색 변화 테스트용). " +
             "★현재 테스트용으로 기본 ON — 실제 진행이 필요하면 끄세요.")]
    [SerializeField] private bool debugFreezePressureStep = true;
    /// <summary>디버그: 압력 substep을 완료시키지 않음(관찰용). PressureCondition이 읽는다.</summary>
    public bool DebugFreezePressureStep => debugFreezePressureStep;

    // === 파지 상태 ===
    /// <summary>양손의 모든 파지점이 성립해야 true (Model B: 손가락별 5점이면 5개 전부).</summary>
    public bool BothGripped => AllGripped(leftGrips) && AllGripped(rightGrips);

    /// <summary>왼손(주동수 쪽) 파지점이 전부 잡혔는가.</summary>
    public bool LeftGripped => AllGripped(leftGrips);

    /// <summary>오른손(보조수 쪽) 파지점이 전부 잡혔는가.</summary>
    public bool RightGripped => AllGripped(rightGrips);

    /// <summary>한 손만 판정해야 하는 단계용 — CSV conditionParams의 <c>hand=left|right|both</c>.</summary>
    public enum JudgeHand { 양손, 왼손, 오른손 }

    public bool GrippedBy(JudgeHand hand) =>
        hand == JudgeHand.왼손 ? LeftGripped :
        hand == JudgeHand.오른손 ? RightGripped : BothGripped;

    /// <summary>
    /// <b>양손이 한 지점에 포개졌는가</b> — 두 손의 손바닥이 서로 <paramref name="maxGap"/> 이내이고,
    /// 둘 다 지정한 파지점 근처에 있는지 본다.
    ///
    /// ★두상골(pisiform)처럼 <b>관절 매핑이 없는 접촉점</b>을 쓰는 술기용이다.
    /// 손끝 관절 하나를 정답으로 삼으면 실제 접촉 부위와 어긋난다
    /// (Palm은 중지 뿌리라 손목 쪽 두상골과 3~4cm 떨어져 있다).
    /// 그래서 "정확히 어느 관절이 닿았나" 대신 <b>두 손이 겹쳐 한 분절을 누르고 있는가</b>를 본다 —
    /// 복와위 양손두상골 교정처럼 두 손을 포개는 술기의 실제 모양과 맞는다(2026-08-12 사용자 제안).
    /// </summary>
    public bool HandsStackedAt(Transform target, float maxGap, float maxDistanceToTarget,
                               CranialFinger finger = CranialFinger.Palm)
    {
        if (target == null || leftHandVisual == null || rightHandVisual == null) return false;

        Transform lp = ResolveFingertip(leftHandVisual, finger);
        Transform rp = ResolveFingertip(rightHandVisual, finger);
        if (lp == null || rp == null) return false;

        if (Vector3.Distance(lp.position, rp.position) > maxGap) return false;      // 두 손이 붙어 있는가
        Vector3 mid = (lp.position + rp.position) * 0.5f;
        return Vector3.Distance(mid, target.position) <= maxDistanceToTarget;       // 그 자리가 목표 분절인가
    }

    /// <summary>포개짐 판정에 쓸 <b>교정</b> 목표 지점 — 교정 파지점 중 첫 번째(두상골 자리).</summary>
    public Transform StackTarget
    {
        get
        {
            if (leftGrips != null)
                foreach (var g in leftGrips)
                    if (g != null) return g.transform;
            if (rightGrips != null)
                foreach (var g in rightGrips)
                    if (g != null) return g.transform;
            return null;
        }
    }

    /// <summary>
    /// 포개짐 판정에 쓸 <b>진단</b> 목표 지점 — 지금 준비된 진단 단계의 첫 파지점(엄지 촉지 자리).
    ///
    /// ★진단과 교정은 접촉 부위가 다르다(진단=극돌기 엄지 / 교정=횡돌기 두상골).
    /// 같은 지점을 쓰면 진단에서 교정 자리를 짚어야 통과하는 엉뚱한 판정이 된다.
    /// </summary>
    public Transform DiagnosisStackTarget
    {
        get
        {
            var stage = activeStage ?? preparedStage;
            if (stage?.poses != null)
            {
                foreach (var p in stage.poses)
                {
                    if (p == null) continue;
                    var list = new System.Collections.Generic.List<GripPointTarget>();
                    p.leftHand?.CollectInto(list);
                    p.rightHand?.CollectInto(list);
                    foreach (var g in list)
                        if (g != null) return g.transform;
                }
            }
            // 진단 파지점이 없으면 레거시 배열 → 그것도 없으면 교정 파지점으로 폴백
            if (diagnosisRightGrips != null)
                foreach (var g in diagnosisRightGrips)
                    if (g != null) return g.transform;
            return StackTarget;
        }
    }

    /// <summary>진단 촉진: 양손으로 후두(뒤통수)를 감쌌는가(터치만, 압력·깊이 무관).
    /// 왼손 = 후두 Palm(leftGrips, 교정과 공유) + 오른손 = 후두 우측 Palm(diagnosisRightGrips).
    /// 터치 판정은 GripPointTarget.IsGripped(트리거 진입 AND bypassPoseCheck/PoseRecognized)이므로
    /// 진단 파지점은 bypassPoseCheck를 켜서 순수 터치로 성립시킨다.</summary>
    public bool BothHandsTouched => AllGripped(leftGrips) && AllGripped(diagnosisRightGrips);

    // === 진단 단계(유지 타이머) 상태 ===
    private CranialDiagnosisStage activeStage;     // 유지 타이머가 도는 단계(나레이션 후)
    private CranialDiagnosisStage preparedStage;   // 파지점이 표시된 단계(substep 진입 즉시)
    private float[] poseHeld;      // 자세별 누적 유지 시간(초)
    private float[] poseLostAt;    // 자세별 파지 이탈 시각(-1 = 현재 파지 중)

    // === 호흡 유도 문구(한 번 표시 후 자동 소멸) 상태 ===
    private bool cueShown;         // 이 단계에서 이미 띄웠는가(단계당 1회)
    private float cueHideAt = -1f; // 이 시각부터 흐려지기 시작(-1 = 표시 중 아님)

    /// <summary>diagnosisStages가 하나라도 배선됐는가(= 신규 진단 방식 사용).</summary>
    public bool HasDiagnosisStages => diagnosisStages != null && diagnosisStages.Length > 0;

    /// <summary>진단 단계(유지 타이머)가 진행 중인가.
    /// 진단 구간에서는 환자가 자연 호흡을 계속하므로 CranialBreathAnimator가 굴곡·신전을 루프 재생한다.</summary>
    public bool IsDiagnosisStageActive => activeStage != null;

    /// <summary>stageId에 해당하는 단계를 찾는다(대소문자·앞뒤 공백 무시). 없으면 null.
    /// stageId가 비어 있으면 첫 번째 단계를 돌려준다(단계가 1개뿐인 시나리오 편의).</summary>
    public CranialDiagnosisStage FindDiagnosisStage(string stageId)
    {
        if (!HasDiagnosisStages) return null;
        if (string.IsNullOrWhiteSpace(stageId)) return diagnosisStages[0];

        string want = stageId.Trim();
        for (int i = 0; i < diagnosisStages.Length; i++)
        {
            var s = diagnosisStages[i];
            if (s != null && !string.IsNullOrEmpty(s.stageId) &&
                s.stageId.Trim().Equals(want, System.StringComparison.OrdinalIgnoreCase))
                return s;
        }
        return null;
    }

    /// <summary>진단 단계 **표시만** 준비(유지 타이머는 시작하지 않음).
    /// substep 진입 즉시 호출해 나레이션이 흐르는 동안 목표 파지점을 보여준다.
    /// 단계를 못 찾으면 false(호출부가 레거시 방식으로 폴백).</summary>
    public bool PrepareDiagnosisStage(string stageId)
    {
        CranialDiagnosisStage stage = FindDiagnosisStage(stageId);
        if (stage == null) return false;

        // ★ 이미 준비된 단계면 아무것도 다시 하지 않는다.
        //   여기서 ResetState()를 또 부르면 fingerInside가 false로 지워지는데, 손이 이미 파지점 안에
        //   들어와 있으면 OnTriggerEnter가 재발생하지 않아(신규 진입이 아니므로) 손을 뺐다 넣기 전까지
        //   영영 파지로 인식되지 않는다. (나레이션 중 미리 손을 대고 기다리는 경우가 정확히 이 상황)
        if (preparedStage == stage) return true;

        TryInjectFingertips();
        preparedStage = stage;

        // 이 단계의 파지점만 표시 — 교정용·타 단계 파지점은 전부 숨긴다.
        // ★ 숨김을 먼저, 표시를 나중에: 같은 오브젝트를 여러 배열이 공유해도 결과가 같아진다.
        HideAllGrips();
        var mine = CollectStageGrips(stage);
        for (int i = 0; i < mine.Count; i++)
        {
            if (mine[i] == null) continue;
            if (!mine[i].gameObject.activeSelf) mine[i].gameObject.SetActive(true);
            mine[i].ResetState();
            mine[i].SetEvaluating(true);
        }

        leftDepth?.SetEvaluating(false);
        rightDepth?.SetEvaluating(false);   // 진단엔 압력 표시 없음
        postureStabilizer?.SetActive(false);
        return true;
    }

    /// <summary>진단 단계의 유지 타이머 시작. 나레이션이 끝난 뒤(첫 조건 폴 시점) 호출한다 —
    /// 생성자에서 시작하면 안내를 듣기도 전에 카운트가 흘러 조기 완료될 수 있다
    /// (압력·호흡 조건과 동일한 규약). 표시는 <see cref="PrepareDiagnosisStage"/>가 이미 해 둔다.</summary>
    /// <summary>CSV conditionParams의 hold= 값(초). 0이면 아래 기본값을 쓴다.</summary>
    private float diagnosisHoldOverride = 0f;

    /// <summary>CSV에 hold=를 안 적었을 때의 기본 유지 시간.
    /// ★유지 시간의 출처는 CSV 하나뿐이다 — 스테이지에 있던 holdSeconds 필드는 08-11에 삭제했다
    /// (씬 값과 CSV 값이 달라 "표시는 6초인데 판정은 3초"가 되는 사고가 있었다).</summary>
    public const float DefaultDiagnosisHoldSeconds = 3f;

    /// <summary>지금 적용할 자세 유지 시간 = CSV의 hold=, 없으면 기본값.</summary>
    public float StageHoldSeconds =>
        diagnosisHoldOverride > 0f ? diagnosisHoldOverride : DefaultDiagnosisHoldSeconds;

    /// <summary>CSV의 hold= 값을 넣는다(0 = 스테이지 값 사용). 진단 단계를 준비할 때마다 호출된다.</summary>
    public void SetDiagnosisHoldOverride(float seconds) => diagnosisHoldOverride = Mathf.Max(0f, seconds);

    public bool BeginDiagnosisStage(string stageId)
    {
        CranialDiagnosisStage stage = FindDiagnosisStage(stageId);
        if (stage == null) return false;

        PrepareDiagnosisStage(stageId);   // 멱등 — 표시 누락/잔상 방지용 재확인

        activeStage = stage;
        int n = stage.poses != null ? stage.poses.Length : 0;
        poseHeld = new float[n];
        poseLostAt = new float[n];
        for (int i = 0; i < n; i++) poseLostAt[i] = -1f;
        ShowBreathingCueOnce();
        // 순서 강제면 첫 자세의 파지점만 남긴다. ★초기화는 하지 않는다 —
        //   나레이션 중 미리 손을 대고 기다리는 경우 ResetState가 접촉을 지워 영영 인식되지 않는다.
        ShowOnlyCurrentPoseGrips(resetNewlyShown: false);

        if (n == 0)
            ChunaLogger.LogWarning($"[CranialAdjustmentController] 진단 단계 '{stage.stageId}'에 자세가 하나도 없습니다 — 영영 완료되지 않습니다.");
        else
            // ★어느 값이 적용됐는지 로그에 남긴다 — 씬 값과 CSV 값이 달라 헷갈리던 걸 없애기 위해.
            ChunaLogger.Log($"[CranialAdjustmentController] 진단 단계 '{stage.stageId}' 시작 " +
                            $"(자세 {n}개, 각 {StageHoldSeconds}초 유지 — " +
                            $"{(diagnosisHoldOverride > 0f ? "CSV hold=" : "기본값")})");
        return true;
    }

    /// <summary>진행 중인 진단 단계가 완료됐는가(모든 자세가 각각 holdSeconds 채움).</summary>
    public bool DiagnosisStageComplete
    {
        get
        {
            if (activeStage == null || poseHeld == null || poseHeld.Length == 0) return false;
            for (int i = 0; i < poseHeld.Length; i++)
                if (poseHeld[i] < StageHoldSeconds) return false;
            return true;
        }
    }

    /// <summary>진단 단계 종료(진단이 아닌 substep 진입 시 호출) — 타이머·안내 문구 정리 + 진단 파지점 숨김.
    /// ★ 숨기는 대상은 diagnosisStages에 등록된 파지점뿐이다(교정용 leftGrips/rightGrips는 건드리지 않음).
    /// 파지 substep에선 BeginGripPhase가 교정 파지점을 켠 뒤 이 메서드가 호출돼도 안전하다.</summary>
    public void EndDiagnosisStage([System.Runtime.CompilerServices.CallerMemberName] string caller = "")
    {
        if (activeStage != null)
        {
            // ★"6초를 채우지도 않았는데 타이머가 멈추고 사라졌다"(08-11)의 추적용.
            //   유지 게이지를 지우는 곳은 여기뿐이므로, 누가 왜 껐는지가 여기 한 줄에 남는다.
            if (logDiagnosisTrace)
            {
                var prog = new System.Text.StringBuilder();
                int n = poseHeld != null ? poseHeld.Length : 0;
                for (int i = 0; i < n; i++) prog.Append($"[{i}] {poseHeld[i]:F1}/{StageHoldSeconds:F1}초  ");
                ChunaLogger.Log($"<color=orange>[CranialAdjustmentController] 진단 단계 '{activeStage.stageId}' 종료 " +
                                $"— 호출: {caller} / 진행: {(n == 0 ? "자세 없음" : prog.ToString())}</color>");
            }

            activeStage = null;
            poseHeld = null;
            poseLostAt = null;
            reportedPoseNo = -1;   // 다음 진단에서 자세 1부터 다시 알린다
        }
        preparedStage = null;   // 다음에 같은 단계로 재진입하면 다시 표시·초기화되도록
        cueShown = false;       // 다음 진단 단계에서 다시 한 번 띄우도록
        HideBreathingCue();
        ResolveGuideUI()?.ForceHideProgressCircle();   // 유지 게이지 정리(다음 단계로 잔상 안 넘어가게)

        if (diagnosisStages == null) return;
        for (int s = 0; s < diagnosisStages.Length; s++)
        {
            var grips = CollectStageGrips(diagnosisStages[s]);
            for (int i = 0; i < grips.Count; i++)
                if (grips[i] != null && grips[i].gameObject.activeSelf) grips[i].gameObject.SetActive(false);
        }
    }

    [Tooltip("★진단 유지 타이머가 왜 멈추는지 추적할 때 켠다(08-11 증상 조사용). " +
             "자세의 접촉 상태가 바뀔 때·단계가 끝날 때만 로그를 남기므로 양이 많지 않다.\n" +
             "★신규 필드라 이미 배치된 씬 리그에도 코드 기본값(켬)이 그대로 먹는다.")]
    [SerializeField] private bool logDiagnosisTrace = true;

    /// <summary>자세별 직전 접촉 상태 — 바뀔 때만 로그를 남기기 위한 것.</summary>
    private bool[] poseGrippedPrev;

    // ★진단 자세(좌·우)가 바뀌면 골격 표시도 같이 바꾼다 — 파지점이 좌우로 옮겨가는 것과 짝을 맞추기 위해.
    //   골격 쪽에 자세용 줄이 없으면 아무 일도 없다(단계 줄이 그대로 유지된다).
    private SkeletonFocusController skeletonFocus;
    private bool skeletonFocusSearched;
    private int reportedPoseNo = -1;

    private void ReportPoseToSkeletonFocus(int poseIndex)
    {
        int poseNo = poseIndex >= 0 ? poseIndex + 1 : 0;   // 표시용은 1부터
        if (poseNo == reportedPoseNo) return;
        reportedPoseNo = poseNo;

        if (!skeletonFocusSearched)
        {
            skeletonFocusSearched = true;
            skeletonFocus = FindFirstObjectByType<SkeletonFocusController>(FindObjectsInactive.Include);
        }
        skeletonFocus?.SetPose(poseNo);
    }

    /// <summary>매 프레임: 자세별 유지 타이머 누적/초기화. 파지가 풀리면 gripGraceSeconds 안에 다시 잡아야 누적이 유지된다.</summary>
    private void UpdateDiagnosisStage()
    {
        if (activeStage == null || activeStage.poses == null || poseHeld == null) return;

        int n = Mathf.Min(activeStage.poses.Length, poseHeld.Length);
        int current = CurrentPoseIndex();   // 순서 강제 시 판정 대상(-1 = 전부 달성)

        ReportPoseToSkeletonFocus(current);

        // ★접촉이 붙고 떨어지는 순간만 기록한다 — "왜 안 채워지나"의 답이 대부분 여기 있다.
        if (logDiagnosisTrace)
        {
            if (poseGrippedPrev == null || poseGrippedPrev.Length != n) poseGrippedPrev = new bool[n];
            for (int i = 0; i < n; i++)
            {
                var p = activeStage.poses[i];
                bool now = p != null && p.AllGripped();
                if (now == poseGrippedPrev[i]) continue;
                poseGrippedPrev[i] = now;
                ChunaLogger.Log($"<color=orange>[CranialAdjustmentController] 자세 {i} '{p?.label}' " +
                                $"{(now ? "접촉 성립" : "접촉 끊김")} (유지 {poseHeld[i]:F1}/{StageHoldSeconds:F1}초, " +
                                $"판정대상={current}, 순서강제={enforcePoseOrder})</color>");
            }
        }

        for (int i = 0; i < n; i++)
        {
            var pose = activeStage.poses[i];
            if (pose == null) continue;

            // 이미 채운 자세는 유지를 놓쳐도 달성으로 남긴다(손을 바꿔 잡는 동안 풀리는 것 허용).
            if (poseHeld[i] >= StageHoldSeconds) continue;

            // ★순서 강제: 가이드 손이 좌→우 한 방향으로 시연하므로, 그 순서대로만 판정한다.
            //   (끄면 예전처럼 순서 무관 — 어느 자세든 먼저 채워도 된다.)
            if (enforcePoseOrder && i != current) continue;

            if (pose.AllGripped())
            {
                poseLostAt[i] = -1f;
                poseHeld[i] += Time.deltaTime;
                if (poseHeld[i] >= StageHoldSeconds)
                {
                    ChunaLogger.Log($"<color=green>[CranialAdjustmentController] 자세 달성: {pose.label} ({StageHoldSeconds}초)</color>");
                    // ★달성한 자세의 파지점은 감춘다 → 남은 자세(반대쪽)만 보여 "이제 저기를 잡으라"가 눈에 들어온다.
                    //   전부 켜둔 채로 두면 좌·우 파지점이 동시에 떠서 어디를 잡아야 하는지 알 수 없다.
                    if (enforcePoseOrder) ShowOnlyCurrentPoseGrips(resetNewlyShown: true);
                    else HideCompletedPoseGrips(i);
                }
            }
            else if (poseHeld[i] > 0f)
            {
                if (poseLostAt[i] < 0f) poseLostAt[i] = Time.time;
                if (Time.time - poseLostAt[i] > gripGraceSeconds)
                {
                    poseHeld[i] = 0f;       // 유예 초과 → 초기화
                    poseLostAt[i] = -1f;
                    if (metrics != null) metrics.holdResets++;   // 평가 지표: 유지 실패 횟수
                }
            }
        }

    }

    // ============================ 평가 지표 수집 ============================
    // 두개골 술기는 손 포즈 유사도·각도 리밋을 쓰지 않아 기존 채점 경로로는 전부 0이 남는다.
    // 대신 '자세를 정확히 잡았는지 / 얼마나 안정적으로 유지했는지'를 모아 결과에 기록한다.

    // ── 배점표 ──────────────────────────────────────────────────────────────
    // ★인스펙터로 빼지 않고 const로 둔다. 씬에 두개골 리그가 5개(OM·PM·PJ·제1늑골·흉추)라
    //   직렬화 필드로 만들면 리그마다 기준이 갈라진다(이 프로젝트에서 이미 여러 번 겪은 함정).
    //   평가 기준을 바꿀 때는 여기 한 곳만 고치면 전 술기에 같이 먹는다.
    private const float CompletionWeight = 45f;   // 자세 완료도
    private const float StabilityWeight = 40f;    // 유지 안정성
    private const float BreathWeight = 15f;       // 호흡

    private const float DropoutPenalty = 8f;        // 파지를 놓칠 때마다(안정성에서)
    private const float HoldResetPenalty = 10f;     // 유지 타이머가 0으로 초기화될 때마다(안정성에서)
    private const float BreathDropoutPenalty = 5f;  // 호흡 중 파지를 놓칠 때마다(호흡에서)
    private const float EarlyThrustPenalty = 5f;    // 다 내쉬기 전에 순간 교정을 가할 때마다

    /// <summary>이 시간 미만으로 떨어진 것은 이탈로 세지 않는다(핸드트래킹이 한두 프레임 튀는 것).
    /// 사용자 지시: "약간 튀는 건 인정하지만 누가 봐도 손을 이탈한 경우는 감점".</summary>
    private const float DropoutTolerance = 0.35f;

    private TrainingResultData.CranialMetrics metrics;   // 진행 중인 단계 지표(null = 수집 안 함)
    private float metricsStartTime;
    private bool metricsPrevSatisfied;
    private float metricsLostSince = -1f;    // 파지가 풀린 시각(-1 = 풀린 상태 아님)
    private bool metricsLostCounted;         // 이번 이탈을 이미 셌는가(유예를 넘긴 순간 1회만)
    private bool metricsLostDuringBreath;    // 이번 이탈이 호흡을 유도하는 중에 시작됐는가

    /// <summary>두개골 substep 진입 시 ScenarioManager가 호출 — 지표 수집을 시작한다.</summary>
    public void BeginCranialMetrics(string label) => BeginCranialMetrics(label, null, null);

    /// <summary>★국면·단계 이름을 같이 받는다 — 기록은 다음 substep 진입 때 일어나므로
    /// 그때 이름을 읽으면 지표가 한 칸 뒤로 밀려 붙는다(PJ 평가 점수 누락의 원인).</summary>
    public void BeginCranialMetrics(string label, string phaseName, string stepName)
    {
        metrics = new TrainingResultData.CranialMetrics
        {
            label = label,
            phaseName = phaseName,
            stepName = stepName
        };
        metricsStartTime = Time.time;
        metricsPrevSatisfied = false;
        metricsLostSince = -1f;
        metricsLostCounted = false;
        metricsLostDuringBreath = false;
        lastTickSecond = -1;   // 단계가 바뀌면 유지 타이머 소리를 처음부터 센다
    }

    /// <summary>다 내쉬기 전에 순간 교정을 가했다 — 타이밍 실수로 기록한다.</summary>
    public void ReportEarlyThrust()
    {
        if (metrics != null) metrics.earlyThrusts++;
    }

    /// <summary>다 내쉰 뒤 허용 시간을 넘겨 교정했다 — 같은 타이밍 실수로 기록한다.</summary>
    public void ReportLateThrust()
    {
        if (metrics != null) metrics.lateThrusts++;
    }

    /// <summary>모인 지표가 있는가(다음 substep 진입 시 기록할 것이 남았는지).</summary>
    public bool HasPendingCranialMetrics => metrics != null;

    /// <summary>지표를 확정(점수 산출)해 반환하고 수집을 끝낸다.</summary>
    public TrainingResultData.CranialMetrics ConsumeCranialMetrics()
    {
        if (metrics == null) return null;

        var m = metrics;
        metrics = null;
        m.elapsedSeconds = Time.time - metricsStartTime;

        // ── 두개골 전용 산식 ────────────────────────────────────────────────
        // 2026-08-12 재배분: '한 번 잡았나'의 비중을 낮추고 '얼마나 안 놓쳤나'를 올렸다.
        // (구) 완료 60 / 안정 25(이탈3·리셋5) / 호흡 15
        // (신) 완료 45 / 안정 40(이탈8·리셋10) / 호흡 15(호흡 중 이탈 감점 신설)
        //   완료도 45 : 자세를 요구한 만큼 채웠는가(진단은 자세별, 그 외는 파지 성립 여부)
        //   안정성 40 : 잡았다 놓친 횟수·유지 타이머 초기화 횟수만큼 감점
        //   호흡   15 : 요구 호흡을 채웠는가 + 호흡을 유도하는 동안 손을 놓쳤는가
        //               (호흡 없는 단계는 만점 — 감점 사유가 없음)
        float completion = m.CompletionRatio * CompletionWeight;

        float stability = StabilityWeight
                          - (m.gripDropouts * DropoutPenalty)
                          - (m.holdResets * HoldResetPenalty);
        stability = Mathf.Max(0f, stability);

        float breath;
        if (m.breathsRequired > 0)
        {
            breath = Mathf.Clamp01((float)m.breathsCompleted / m.breathsRequired) * BreathWeight;
            // ★호흡 단계에서 손이 떨어지는 것은 잠금이 풀렸다는 뜻이라 별도로 감점한다.
            //   손떨림 수준(DropoutTolerance 미만)은 이탈로 세지 않는다.
            breath -= m.breathGripDropouts * BreathDropoutPenalty;
            // ★다 내쉬기 전에 누른 것 — 이 술기의 핵심 타이밍을 놓친 것이라 같은 무게로 깎는다.
            breath -= (m.earlyThrusts + m.lateThrusts) * EarlyThrustPenalty;
            breath = Mathf.Max(0f, breath);
        }
        else
        {
            breath = BreathWeight;
        }

        m.score = Mathf.Clamp(completion + stability + breath, 0f, 100f);
        m.grade = EvaluationScoringEngine.GetGradeFromScore(m.score);
        return m;
    }

    /// <summary>매 프레임 지표 누적. 수집 중이 아니면 즉시 반환.</summary>
    private void UpdateCranialMetrics()
    {
        if (metrics == null) return;

        float now = Time.time - metricsStartTime;
        bool breathing = breathingHUD != null && breathingHUD.IsRunning;

        // 파지 성립/이탈
        // ★한 프레임만 튄 것은 이탈로 세지 않는다 — 유예(DropoutTolerance)를 넘겨 떨어져 있어야
        //   비로소 1회로 센다. 안정성 배점이 40으로 올라가 1회당 8점이라, 트래킹 노이즈를
        //   그대로 세면 정상 수행도 점수가 무너진다.
        bool satisfied = IsJudgedGripSatisfied();
        if (satisfied)
        {
            metrics.holdSeconds += Time.deltaTime;
            if (metrics.firstContactSeconds < 0f) metrics.firstContactSeconds = now;
            metricsLostSince = -1f;
            metricsLostCounted = false;
        }
        else
        {
            if (metricsPrevSatisfied)
            {
                // 방금 떨어졌다 — 아직 세지 않고 유예 시간을 잰다.
                metricsLostSince = Time.time;
                metricsLostCounted = false;
                metricsLostDuringBreath = breathing;
            }

            if (!metricsLostCounted && metricsLostSince >= 0f
                && Time.time - metricsLostSince >= DropoutTolerance)
            {
                metricsLostCounted = true;
                metrics.gripDropouts++;
                // 호흡을 유도하는 동안 놓친 것은 '잠금이 풀렸다'는 뜻이라 호흡 배점에서도 깎는다.
                if (metricsLostDuringBreath || breathing) metrics.breathGripDropouts++;

                if (logDiagnosisTrace)
                    ChunaLogger.Log($"<color=orange>[CranialAdjustmentController] 파지 이탈 {metrics.gripDropouts}회" +
                                    $"{((metricsLostDuringBreath || breathing) ? " (호흡 중 — 호흡 배점 감점)" : "")}</color>");
            }
        }
        metricsPrevSatisfied = satisfied;

        // 진단 단계: 요구 자세 수 / 채운 자세 수
        if (activeStage != null && activeStage.poses != null && poseHeld != null)
        {
            int n = Mathf.Min(activeStage.poses.Length, poseHeld.Length);
            int done = 0;
            for (int i = 0; i < n; i++)
                if (poseHeld[i] >= StageHoldSeconds) done++;
            metrics.posesRequired = n;
            metrics.posesCompleted = done;
        }

        // 견착(삼각근-이마 프록시) 유지 시간
        if (postureStabilizer != null && postureStabilizer.IsInPosition)
            metrics.postureSeconds += Time.deltaTime;

        // 호흡 단계
        if (breathingHUD != null && breathingHUD.IsRunning)
        {
            metrics.breathsRequired = breathingHUD.RequiredBreaths;
            metrics.breathsCompleted = breathingHUD.CompletedBreaths;
            metrics.breathFailures = breathingHUD.FailedBreaths;
            if (breathingHUD.LastHoldRatio > 0f) metrics.breathHoldRatio = breathingHUD.LastHoldRatio;
        }
    }

    // === 가이드손: '동작(자세)마다' 켜고 끄기 ===
    private ChunaPathEvaluator guideHandOwner;   // 무장된 동안만 non-null
    private string substepGuideClip;             // CSV handTrackingFileName (substep 공용 클립)
    private string loadedGuideClip;              // 평가기에 지금 로드된 클립 이름(중복 로드 방지)
    private int guidePoseIndex = -1;             // 지금 가이드를 재생 중인 자세(-1 = 자세 없음/전부 달성)
    private bool guideHandSawRelease;            // 이 동작에서 '미성립' 상태를 한 번이라도 봤는가

    /// <summary>
    /// 이번 substep에서 <b>어느 손의 가이드를 보여줄지</b>. CSV의 <c>hand=left|right</c>를 그대로 쓴다.
    ///
    /// ★한 손씩 순서대로 대는 술기(흉추 신전: 보조수로 머리를 받쳐 들어 올린 뒤 주동수 주먹)에서
    /// 양손 가이드가 다 떠 있으면 "지금 어느 손을 대라는 건지"가 안 보인다.
    /// 녹화는 그대로 두고(양손이 다 들어 있다) 표시만 손 단위로 가린다.
    /// </summary>
    private JudgeHand guideHandScope = JudgeHand.양손;

    public void SetGuideHandScope(JudgeHand scope) => guideHandScope = scope;

    /// <summary>이번 substep의 가이드손 자동 제어를 무장한다.
    /// ScenarioManager가 두개골 substep에서 가이드손 재생을 시작한 직후 호출한다.
    /// ★무장된 동안만 동작하므로 다른 시나리오(사각근 등)의 가이드손에는 영향이 없다.</summary>
    [Tooltip("★기본 OFF. 켜면 그 자세의 파지가 성립하는 순간 가이드손 재생을 끊는다.\n" +
             "끄면(기본) 손을 댔는지와 무관하게 1회를 끝까지 재생한다.")]
    [SerializeField] private bool stopGuideHandOnGrip = false;

    [Tooltip("★기본 ON(08-11 사용자 지시). 시술자 손이 파지점에 닿아 있는 동안 가이드손을 숨기고, " +
             "손을 떼면 다시 보여준다. ★되살릴 때는 재생이 아니라 '끝난 마지막 자세'다.\n" +
             "판정(파지 성립)이 아니라 접촉만 본다 — 손 모양이 안 맞아도 시야를 가리지 않게.\n" +
             "★신규 필드라 이미 배치된 씬 리그에도 코드 기본값이 그대로 먹는다.")]
    [SerializeField] private bool hideGuideHandOnTouch = true;

    public void ArmGuideHandAutoHide(ChunaPathEvaluator evaluator, string substepClipName)
    {
        guideHandOwner = evaluator;
        // 단계가 바뀌면 손별 숨김을 푼다 — 아직 손을 대고 있으면 다음 프레임에 다시 숨겨진다.
        evaluator?.ClearGuideHandSuppression();
        substepGuideClip = substepClipName;
        loadedGuideClip = substepClipName;   // ScenarioManager가 이미 로드해 둔 상태
        guidePoseIndex = -1;
        guideHandSawRelease = false;
    }

    /// <summary>매 프레임: 가이드손을 <b>동작(자세) 단위</b>로 켜고 끈다.
    ///   · 자세가 시작되면 그 자세의 가이드를 재생
    ///   · 그 자세의 파지가 성립하면 정지
    ///   · 다음 자세로 넘어가면 그 자세 것으로 다시 재생
    /// 자세가 하나뿐인 단계(파지·압력조절·호흡)에서는 결과적으로 substep 단위와 같다.</summary>
    private void UpdateGuideHandAutoHide()
    {
        if (guideHandOwner == null) return;

        // ★동작 전환 감지: 자세가 바뀌면 그 동작의 가이드를 새로 켠다.
        if (activeStage != null)
        {
            int pose = CurrentPoseIndex();
            if (pose != guidePoseIndex)
            {
                guidePoseIndex = pose;
                guideHandSawRelease = false;
                if (pose >= 0) PlayGuideForPose(pose);
                else guideHandOwner.StopGuideHandPlaybackInternal();   // 전부 달성 — 가이드 종료
                return;
            }
        }

        // ★손을 갖다 댄 손의 가이드손만 완전히 숨긴다. 떼면 '끝난 자세' 그대로 다시 보인다(재생 아님).
        //   ★손 단위인 이유: 왼손을 먼저 대는 술기에서 양손을 같이 숨기면 아직 안 댄 오른손의
        //     목표 자세까지 사라져 어디를 잡아야 하는지 알 수 없게 된다.
        // ★이번 단계에서 쓰지 않는 손의 가이드는 아예 숨긴다(hand=left/right).
        //   접촉 여부와 무관하게 가려야 "지금 이 손을 대라"가 분명해진다.
        bool leftOutOfScope = guideHandScope == JudgeHand.오른손;
        bool rightOutOfScope = guideHandScope == JudgeHand.왼손;

        guideHandOwner.SuppressGuideHandInternal(
            true, leftOutOfScope || (hideGuideHandOnTouch && AnyJudgedGripTouched(true)));
        guideHandOwner.SuppressGuideHandInternal(
            false, rightOutOfScope || (hideGuideHandOnTouch && AnyJudgedGripTouched(false)));

        // ★기본값 OFF — 가이드손은 손을 댔는지와 무관하게 1회를 끝까지 재생한다(사용자 지시).
        //   켜면 예전처럼 '그 자세의 파지가 성립하는 순간' 재생을 끊는다.
        if (!stopGuideHandOnGrip) return;

        bool satisfied = IsJudgedGripSatisfied();

        // ★동작이 시작된 직후부터 이미 성립이면 이전 동작의 잔여 접촉일 수 있다(GripPointTarget엔
        //   OnDisable이 없어 비활성 중 접촉 상태가 남는다) → '한 번 풀린 것'을 본 뒤부터 인정한다.
        if (!satisfied) { guideHandSawRelease = true; return; }
        if (!guideHandSawRelease) return;

        guideHandOwner.StopGuideHandPlaybackInternal();
        guideHandSawRelease = false;   // 이 동작은 끝 — 다음 동작 전환 때 다시 켜진다
        ChunaLogger.Log("[CranialAdjustmentController] 파지 접촉 — 가이드손 종료");
    }

    /// <summary>해당 자세의 가이드손을 처음부터 1회 재생한다(루프 없음).
    /// 자세 전용 클립(guideClipName)이 있으면 그것을, 없으면 substep 공용 클립을 쓴다.
    /// 한 클립에 좌→우를 이어 녹화한 경우 guideStartRatio/EndRatio로 구간을 나눠 쓸 수 있다.</summary>
    private void PlayGuideForPose(int index)
    {
        if (guideHandOwner == null || activeStage == null || activeStage.poses == null) return;
        if (index < 0 || index >= activeStage.poses.Length) return;

        var p = activeStage.poses[index];
        if (p == null) return;

        string clip = !string.IsNullOrEmpty(p.guideClipName) ? p.guideClipName : substepGuideClip;
        if (string.IsNullOrEmpty(clip))
        {
            // ★"반대쪽 자세를 안 보여준다"의 1순위 원인 — 그 자세에 시연 클립이 없다.
            ChunaLogger.LogWarning($"[CranialAdjustmentController] 자세 {index} '{p.label}' 가이드손 클립이 없습니다 " +
                                   $"(자세 guideClipName·CSV handTrackingFileName 둘 다 빔) — 시연 없이 진행합니다.");
            return;
        }

        if (logDiagnosisTrace)
            ChunaLogger.Log($"<color=cyan>[CranialAdjustmentController] 자세 {index} '{p.label}' 가이드손 재생 " +
                            $"→ 클립='{clip}' 구간={p.guideStartRatio:0.##}~{p.guideEndRatio:0.##}</color>");

        if (clip != loadedGuideClip)
        {
            guideHandOwner.LoadAndGenerateCheckpoints(clip);   // 프레임만 로드(평가는 두개골 조건이 담당)
            loadedGuideClip = clip;
        }

        float s = Mathf.Clamp01(p.guideStartRatio);
        float e = Mathf.Clamp01(p.guideEndRatio);

        // ★끝 비율이 시작보다 작거나 같으면 '구간 미지정'으로 보고 <b>클립 끝까지</b> 재생한다.
        //   씬에서 새로 만든 자세는 guideEndRatio가 0으로 직렬화돼 있는데, 예전 코드는 그걸
        //   0~0 구간으로 해석해 첫 프레임만 찍고 멈췄다 — "가이드손이 재생 안 되고 멈춰 있다"의 원인
        //   (제1늑골 진단, 2026-08-12). 구간을 나눠 쓰려면 EndRatio를 명시해야 한다.
        if (e <= s) e = 1f;

        // ★클립+구간을 키로 넘긴다 — 같은 자세가 다음 단계에서 또 요청되면 재생하지 않고
        //   끝난 자세를 유지한다(08-11 사용자 지시). 자세별로 구간을 나눠 쓴 경우는 키가 달라 정상 재생된다.
        guideHandOwner.PlayGuideHandOnceInternal(clip, s, e);
    }

    /// <summary>지금 판정 중인 파지점에 <b>손이 닿아 있는가</b>(하나라도). 가이드손 숨김 판단 전용.
    /// ★파지 성립(IsGripped)이 아니라 접촉(IsTouched)을 본다 — 손 모양이 아직 안 맞아도
    /// 손이 그 자리에 가 있으면 가이드손이 시야를 가리기 때문.</summary>
    private bool AnyJudgedGripTouched(bool leftHand)
    {
        if (activeStage != null)
        {
            int i = CurrentPoseIndex();
            if (i < 0) return true;                     // 전부 달성 — 더 보여줄 게 없다
            if (activeStage.poses == null || i >= activeStage.poses.Length) return false;
            var p = activeStage.poses[i];
            if (p == null) return false;
            var hand = leftHand ? p.leftHand : p.rightHand;
            return hand != null && hand.AnyTouched();
        }
        return AnyTouched(leftHand ? leftGrips : rightGrips);
    }

    private static bool AnyTouched(GripPointTarget[] grips)
    {
        if (grips == null) return false;
        for (int i = 0; i < grips.Length; i++)
            if (grips[i] != null && grips[i].IsTouched) return true;
        return false;
    }

    /// <summary>지금 판정 중인 파지가 성립했는가. 진단 단계면 '지금 차례 자세', 그 외엔 양손 교정 파지.</summary>
    private bool IsJudgedGripSatisfied()
    {
        if (activeStage != null)
        {
            int i = CurrentPoseIndex();
            if (i < 0) return true;                     // 전부 달성
            if (activeStage.poses == null || i >= activeStage.poses.Length) return false;
            var p = activeStage.poses[i];
            return p != null && p.AllGripped();
        }
        return BothGripped;
    }

    /// <summary>아직 못 채운 첫 자세의 인덱스(-1 = 전부 달성). 순서 강제·카운트 표시의 기준.</summary>
    private int CurrentPoseIndex()
    {
        if (activeStage == null || activeStage.poses == null || poseHeld == null) return -1;
        int n = Mathf.Min(activeStage.poses.Length, poseHeld.Length);
        for (int i = 0; i < n; i++)
            if (poseHeld[i] < StageHoldSeconds) return i;
        return -1;
    }

    /// <summary>진단 '자세 유지' 남은 시간을 기존 ProgressCircle UI에 표시한다.
    /// 파지가 풀리면 카운트가 되돌아간다. 자세가 2개 이상이면 "1/2" 카운트도 함께 띄운다
    /// (스텝을 좌/우로 쪼개지 않고 한 substep 안에서 진행 상황을 알리는 방법).</summary>
    /// <summary>진단이 아닌 '유지' 단계(cranialPressure)가 매 폴마다 남은 시간을 알린다.
    /// ★유지 타이머가 진단에서만 보이고 자세준비에서는 안 보여 들쭉날쭉하다는 지적(08-10) →
    /// 유지 판정이 있는 단계는 전부 같은 ProgressCircle에 표시한다.
    /// 폴링이 멈추면 보고 시각이 낡아 자동으로 사라진다(별도 해제 배선 불필요).</summary>
    public void ReportHoldProgress(float remainingSeconds, float totalSeconds)
    {
        holdReportRemaining = Mathf.Max(0f, remainingSeconds);
        holdReportTotal = Mathf.Max(0.01f, totalSeconds);
        holdReportTime = Time.time;
    }

    private float holdReportRemaining, holdReportTotal, holdReportTime = -99f;

    private void UpdateHoldProgressUI()
    {
        var ui = ResolveGuideUI();
        if (ui == null) return;

        // 진단 단계가 없으면 = 파지 유지(cranialPressure) 같은 단계. 최근 보고가 있으면 그걸 표시한다.
        if (activeStage == null || activeStage.poses == null || poseHeld == null)
        {
            if (Time.time - holdReportTime < 0.3f)
            {
                ui.DriveProgressExternally(holdReportRemaining, holdReportRemaining / holdReportTotal, null);
                PlayHoldTick(holdReportRemaining);
            }
            else
            {
                lastTickSecond = -1;   // 표시가 끊기면 다음 유지에서 처음부터 센다
            }
            return;
        }

        float hold = Mathf.Max(0.01f, StageHoldSeconds);
        int show = -1;

        if (enforcePoseOrder)
        {
            show = CurrentPoseIndex();   // 순서 강제 = 지금 해야 하는 자세만 표시
        }
        else
        {
            // ① 지금 잡고 있는 미완료 자세 우선
            for (int i = 0; i < activeStage.poses.Length && i < poseHeld.Length; i++)
            {
                if (poseHeld[i] >= hold) continue;
                var p = activeStage.poses[i];
                if (p != null && p.AllGripped()) { show = i; break; }
            }
            // ② 없으면 아직 못 채운 첫 자세
            if (show < 0) show = CurrentPoseIndex();
        }

        if (show < 0) return;   // 전부 달성 — 곧 단계가 넘어간다

        int total = Mathf.Min(activeStage.poses.Length, poseHeld.Length);
        string label = total > 1 ? $"{show + 1}/{total}" : null;

        float remaining = Mathf.Max(0f, hold - poseHeld[show]);
        ui.DriveProgressExternally(remaining, remaining / hold, label);

        // ★유지 게이지가 안 보이는 자세·각도가 있다는 지적(08-11) → 초마다 소리로도 알린다.
        PlayHoldTick(remaining);
    }

    // ================= 유지 타이머 효과음 =================
    [Header("=== 유지 타이머 소리 (08-11 신규) ===")]
    [Tooltip("★유지 타이머가 화면에서 안 보이는 각도·단계가 있어 소리로 남은 초를 알린다. " +
             "1초마다 '틱', 마지막 1초는 조금 높은 소리. 비우면 Resources/Audio/TimerTick·TimerTickLast를 자동으로 쓴다.\n" +
             "★신규 필드라 이미 배치된 씬 리그에도 코드 기본값이 그대로 먹는다.")]
    [SerializeField] private bool playHoldTickSound = true;
    [SerializeField, Range(0f, 1f)] private float holdTickVolume = 0.5f;
    [SerializeField] private AudioClip holdTickClip;
    [SerializeField] private AudioClip holdTickLastClip;

    private AudioSource tickSource;
    private int lastTickSecond = -1;

    /// <summary>남은 초가 바뀌는 순간에만 한 번 울린다(매 프레임 울리지 않게).</summary>
    private void PlayHoldTick(float remaining)
    {
        if (!playHoldTickSound) return;

        int sec = Mathf.CeilToInt(remaining);
        if (sec == lastTickSecond) return;
        lastTickSecond = sec;
        if (sec <= 0) return;               // 0초 = 완료 — 완료음(띵동)이 담당한다

        if (holdTickClip == null) holdTickClip = Resources.Load<AudioClip>("Audio/TimerTick");
        if (holdTickLastClip == null) holdTickLastClip = Resources.Load<AudioClip>("Audio/TimerTickLast");

        if (tickSource == null)
        {
            tickSource = gameObject.GetComponent<AudioSource>();
            if (tickSource == null)
            {
                tickSource = gameObject.AddComponent<AudioSource>();
                tickSource.playOnAwake = false;
                tickSource.spatialBlend = 0f;   // 2D — 손이 시야를 벗어나도 들려야 한다
            }
        }

        AudioClip clip = sec <= 1 && holdTickLastClip != null ? holdTickLastClip : holdTickClip;
        if (clip != null) tickSource.PlayOneShot(clip, holdTickVolume);
    }

    private ScenarioGuideUIController cachedGuideUI;
    private bool guideUISearched;

    private ScenarioGuideUIController ResolveGuideUI()
    {
        if (cachedGuideUI != null) return cachedGuideUI;
        if (guideUISearched) return null;
        guideUISearched = true;
        cachedGuideUI = FindObjectOfType<ScenarioGuideUIController>(true);
        return cachedGuideUI;
    }

    /// <summary>순서 강제일 때, <b>지금 해야 하는 자세</b>의 파지점만 보이게 한다(나머지는 숨김).
    ///
    /// <paramref name="resetNewlyShown"/>=true면 새로 켜는 파지점의 접촉 상태를 초기화한다.
    /// 자세가 넘어가는 순간엔 시술자의 손이 아직 <b>이전</b> 자세에 있으므로(방금 그 자세를 완료했으니)
    /// 초기화해도 "이미 트리거 안에 있어 OnTriggerEnter가 재발생하지 않는" 함정에 걸리지 않는다.
    /// 반대로 단계 시작 시점엔 나레이션 중 미리 손을 대고 기다릴 수 있어 초기화하면 안 된다.</summary>
    private void ShowOnlyCurrentPoseGrips(bool resetNewlyShown)
    {
        if (!enforcePoseOrder || activeStage == null || activeStage.poses == null) return;

        int current = CurrentPoseIndex();
        var want = new System.Collections.Generic.HashSet<GripPointTarget>();
        if (current >= 0 && current < activeStage.poses.Length)
        {
            var list = new System.Collections.Generic.List<GripPointTarget>();
            var p = activeStage.poses[current];
            p?.leftHand?.CollectInto(list);
            p?.rightHand?.CollectInto(list);
            for (int i = 0; i < list.Count; i++) if (list[i] != null) want.Add(list[i]);
        }

        var all = CollectStageGrips(activeStage);
        for (int i = 0; i < all.Count; i++)
        {
            var g = all[i];
            if (g == null) continue;
            bool on = want.Contains(g);
            if (g.gameObject.activeSelf == on) continue;      // 상태 동일 — 건드리지 않는다
            if (on && resetNewlyShown) g.ResetState();        // 이전 자세에서 남은 접촉 상태 제거
            g.gameObject.SetActive(on);
        }
    }

    /// <summary>달성한 자세의 파지점을 감춘다. 단, 아직 못 채운 다른 자세가 같은 파지점을 쓰면 남겨 둔다.</summary>
    private void HideCompletedPoseGrips(int completedIndex)
    {
        if (activeStage == null || activeStage.poses == null) return;

        // 아직 미완료인 자세들이 쓰는 파지점은 계속 보여야 한다.
        var stillNeeded = new System.Collections.Generic.HashSet<GripPointTarget>();
        for (int i = 0; i < activeStage.poses.Length; i++)
        {
            if (i == completedIndex) continue;
            if (poseHeld != null && i < poseHeld.Length && poseHeld[i] >= StageHoldSeconds) continue;
            var p = activeStage.poses[i];
            if (p == null) continue;
            var list = new System.Collections.Generic.List<GripPointTarget>();
            p.leftHand?.CollectInto(list);
            p.rightHand?.CollectInto(list);
            foreach (var g in list) if (g != null) stillNeeded.Add(g);
        }

        var mine = new System.Collections.Generic.List<GripPointTarget>();
        var done = activeStage.poses[completedIndex];
        done?.leftHand?.CollectInto(mine);
        done?.rightHand?.CollectInto(mine);

        for (int i = 0; i < mine.Count; i++)
        {
            var g = mine[i];
            if (g == null || stillNeeded.Contains(g)) continue;
            if (g.gameObject.activeSelf) g.gameObject.SetActive(false);
        }
    }

    // ── 파지 성립 시 환자 애니 재생 ──────────────────────────────────
    private ChunaPathEvaluator gripAnimEvaluator;

    /// <summary>
    /// 파지가 성립하는 순간 대기 중인 환자 애니를 재생하도록 무장한다.
    /// <para><paramref name="leftOnly"/>=true면 <b>왼손 파지점만</b> 잡혀도 재생한다
    /// (제1늑골: 왼손 검지 측면이 늑골 파지점에 닿으면 머리가 신전·병진).</para>
    /// </summary>
    /// <summary>어느 손이 닿으면 애니를 시작할지.</summary>
    public enum GripAnimTrigger { 왼손, 오른손, 양손 }

    public void ArmAnimationOnGrip(ChunaPathEvaluator evaluator, bool leftOnly)
        => ArmAnimationOnGrip(evaluator, leftOnly ? GripAnimTrigger.왼손 : GripAnimTrigger.양손);

    public void ArmAnimationOnGrip(ChunaPathEvaluator evaluator, GripAnimTrigger trigger)
    {
        gripAnimEvaluator = evaluator;
        gripAnimTrigger = trigger;
    }

    private GripAnimTrigger gripAnimTrigger = GripAnimTrigger.왼손;

    /// <summary>무장 해제(단계 전환 시).</summary>
    public void DisarmAnimationOnGrip() => gripAnimEvaluator = null;

    private void UpdateGripTriggeredAnimation()
    {
        if (gripAnimEvaluator == null) return;
        if (!gripAnimEvaluator.HasPendingAnimation) { gripAnimEvaluator = null; return; }

        bool ok = gripAnimTrigger == GripAnimTrigger.왼손 ? AllGripped(leftGrips)
                : gripAnimTrigger == GripAnimTrigger.오른손 ? AllGripped(rightGrips)
                : BothGripped;
        if (!ok) return;

        gripAnimEvaluator.BeginDeferredAnimation();
        gripAnimEvaluator = null;
    }

    /// <summary>진단 단계 시작 시 호흡 유도 문구를 <b>한 번만</b> 띄운다(cueVisibleSeconds 뒤 자동 소멸).
    /// 텍스트 미배선이면 아무것도 안 한다(단계는 정상 진행).</summary>
    private void ShowBreathingCueOnce()
    {
        if (breathingCueText == null) return;

        // 진단 단계가 아니거나 이 단계가 문구를 안 쓰면 남아 있던 문구만 정리한다.
        bool wants = activeStage != null && (cueOnAllDiagnosisStages || activeStage.showBreathingCue);
        if (!wants) { HideBreathingCue(); return; }
        if (cueShown) return;   // 단계당 1회 — 조건이 Begin을 두 번 불러도 다시 안 뜬다

        cueShown = true;
        breathingCueText.text = breathingCueMessage;
        SetCueAlpha(1f);
        SetCueRootActive(true);
        cueHideAt = Time.time + Mathf.Max(0.1f, cueVisibleSeconds);
    }

    /// <summary>표시 시간이 지나면 서서히 흐려지다 사라진다. 매 프레임 호출(Update).</summary>
    private void UpdateBreathingCueFade()
    {
        if (breathingCueText == null || cueHideAt < 0f) return;

        float over = Time.time - cueHideAt;
        if (over < 0f) return;   // 아직 표시 시간 중

        float fade = Mathf.Max(0f, cueFadeSeconds);
        if (fade <= 0.01f || over >= fade) { HideBreathingCue(); return; }
        SetCueAlpha(1f - (over / fade));
    }

    /// <summary>문구를 즉시 감추고 다음 표시를 위해 알파를 원복한다.</summary>
    private void HideBreathingCue()
    {
        cueHideAt = -1f;
        if (breathingCueText == null) return;
        SetCueAlpha(1f);
        if (breathingCueText.text != "") breathingCueText.text = "";
        SetCueRootActive(false);
    }

    /// <summary>문구 투명도 조절. TMP 전용 API(alpha) 대신 color를 쓴다(버전 의존 없음).</summary>
    private void SetCueAlpha(float a)
    {
        if (breathingCueText == null) return;
        Color c = breathingCueText.color;
        c.a = Mathf.Clamp01(a);
        breathingCueText.color = c;
    }

    /// <summary>문구 루트(배경 포함) 켜고 끄기. 지정이 없으면 텍스트 자신의 GameObject를 쓴다.</summary>
    private void SetCueRootActive(bool on)
    {
        GameObject root = breathingCueRoot != null
            ? breathingCueRoot
            : (breathingCueText != null ? breathingCueText.gameObject : null);
        if (root != null && root.activeSelf != on) root.SetActive(on);
    }

    /// <summary>한 단계에 속한 모든 파지점(양손·전 자세) 수집.</summary>
    private static System.Collections.Generic.List<GripPointTarget> CollectStageGrips(CranialDiagnosisStage stage)
    {
        var list = new System.Collections.Generic.List<GripPointTarget>();
        if (stage == null || stage.poses == null) return list;
        for (int i = 0; i < stage.poses.Length; i++)
        {
            var p = stage.poses[i];
            if (p == null) continue;
            p.leftHand?.CollectInto(list);
            p.rightHand?.CollectInto(list);
        }
        return list;
    }

    /// <summary>모든 진단 단계 파지점을 한 손(왼손/오른손)별로 모은다 — 손끝 콜라이더 주입용.</summary>
    private GripPointTarget[] CollectAllStageGrips(bool leftSide)
    {
        var list = new System.Collections.Generic.List<GripPointTarget>();
        if (diagnosisStages != null)
        {
            for (int s = 0; s < diagnosisStages.Length; s++)
            {
                var stage = diagnosisStages[s];
                if (stage == null || stage.poses == null) continue;
                for (int i = 0; i < stage.poses.Length; i++)
                {
                    var p = stage.poses[i];
                    if (p == null) continue;
                    if (leftSide) p.leftHand?.CollectInto(list);
                    else p.rightHand?.CollectInto(list);
                }
            }
        }
        return list.ToArray();
    }

    /// <summary>파지 구체를 전부 숨긴다(교정용·레거시·전 진단 단계).
    /// 두개골 조건이 아닌 substep(진단3·재평가·시작/종료 안내)에 들어갈 때 ScenarioManager가 호출한다.
    /// ★이게 없으면 파지 단계에서 켠 교정 파지 구체가 재평가·종료까지 화면에 남는다
    ///   (BeginGripPhase가 켜기만 하고 끄는 곳이 없었음).</summary>
    public void HideAllGripPoints()
    {
        HideGripPoints(keepCorrectionGrips: false);
    }

    /// <summary>
    /// 파지 구체를 정리한다.
    /// <para><paramref name="keepCorrectionGrips"/>=true면 <b>교정 파지점(leftGrips/rightGrips)은 그대로 둔다</b> —
    /// 교정 단계는 파지를 유지한 채 진행하는데 조건 타입이 cranial이 아니라는 이유로 구체를 지워 버리면
    /// 학습자가 어디를 잡고 있어야 하는지 볼 수 없다. 진단용 구체만 정리한다.</para>
    /// </summary>
    public void HideGripPoints(bool keepCorrectionGrips)
    {
        if (keepCorrectionGrips) HideDiagnosisGrips();
        else HideAllGrips();

        SetHandJudgingActive(keepCorrectionGrips);   // 파지점을 남기면 색이 갱신되도록 판정도 살려 둔다

        // ★손별 가이드손 억제를 반드시 풀고 무장 해제한다(2026-08-13).
        //   두개골 단계에서 파지점에 손을 대면 그 손의 가이드를 숨기는데(SuppressGuideHandInternal),
        //   그 플래그는 <b>클립이 아니라 손 단위 전역 상태</b>라 무장만 해제하면 <b>억제가 그대로 남는다.</b>
        //   그 결과 다음 substep이 궤적(HandPose) 단계여도 가이드손이 안 뜨거나 한쪽만 떠서
        //   "가이드가 나왔다 사라졌다 하고, 시연이 진행되지 않는다"가 됐다(제2늑골 Play 검증).
        guideHandOwner?.ClearGuideHandSuppression();
        guideHandOwner = null;                       // 두개골이 아닌 substep — 가이드손 자동 종료 무장 해제
    }

    /// <summary>진단 단계용 파지점만 숨긴다(교정 파지점은 건드리지 않는다).</summary>
    private void HideDiagnosisGrips()
    {
        SetGripsActive(diagnosisRightGrips, false);
        if (diagnosisStages == null) return;
        for (int s = 0; s < diagnosisStages.Length; s++)
        {
            var grips = CollectStageGrips(diagnosisStages[s]);
            for (int i = 0; i < grips.Count; i++)
                if (grips[i] != null && grips[i].gameObject.activeSelf) grips[i].gameObject.SetActive(false);
        }
    }

    /// <summary>교정용·레거시·전 진단 단계 파지점을 전부 숨긴다(단계 전환 시 잔상 방지).</summary>
    private void HideAllGrips()
    {
        SetGripsActive(leftGrips, false);
        SetGripsActive(rightGrips, false);
        SetGripsActive(diagnosisRightGrips, false);
        if (diagnosisStages == null) return;
        for (int s = 0; s < diagnosisStages.Length; s++)
        {
            var grips = CollectStageGrips(diagnosisStages[s]);
            for (int i = 0; i < grips.Count; i++)
                if (grips[i] != null && grips[i].gameObject.activeSelf) grips[i].gameObject.SetActive(false);
        }
    }

    private static bool AllGripped(GripPointTarget[] grips)
    {
        if (grips == null || grips.Length == 0) return false;   // 미설정 손 = 미성립(양손 파지 강제)
        for (int i = 0; i < grips.Length; i++)
            if (grips[i] == null || !grips[i].IsGripped) return false;
        return true;
    }

    /// <summary>파지점 GameObject 표시/숨김(구체 자체를 켜고 끔 — 단계별로 관련 없는 파지점 숨김).</summary>
    private static void SetGripsActive(GripPointTarget[] grips, bool active)
    {
        if (grips == null) return;
        for (int i = 0; i < grips.Length; i++)
            if (grips[i] != null && grips[i].gameObject.activeSelf != active)
                grips[i].gameObject.SetActive(active);
    }


    // === 압력 상태 (압력 substep 게이트용) ===
    /// <summary>양손 모두 적정 텐션 존(올바른 깊이·방향). 가이드 미설정 시 해당 손은 통과 처리.</summary>
    public bool BothInGoodZone =>
        (leftDepth == null || leftDepth.IsInGoodZone) &&
        (rightDepth == null || rightDepth.IsInGoodZone);

    /// <summary>존재하는 모든 깊이 가이드에 영점이 저장됐는지 (호흡 substep의 영점 누락 방어용)</summary>
    public bool HasZeroPoints =>
        (leftDepth == null || leftDepth.HasZeroPoint) &&
        (rightDepth == null || rightDepth.HasZeroPoint);

    // === 조건이 호출하는 진입/상태 API ===

    /// <summary>⓪ 진단 촉진 substep 시작 시 호출: 양손 후두 감싸기 터치 판정만 활성화.
    /// 왼손 후두 Palm(leftGrips)+오른손 후두 Palm(diagnosisRightGrips)을 켜고, 측두 5지·깊이·호흡·자세는 끈다.</summary>
    public void BeginDiagnosisPhase()
    {
        TryInjectFingertips();                          // 손끝/손바닥 트리거 콜라이더 확보(진단 파지점 포함)
        // 진단: 양손으로 후두 감싸기 — 왼손 후두 Palm(leftGrips) + 오른손 후두 Palm(diagnosisRightGrips) 표시·판정
        // ★ 순서 주의: 숨김을 먼저, 표시를 나중에.
        //   씬에서 diagnosisRightGrips에 rightGrips와 같은 오브젝트를 넣어 재사용하는 배선이 있는데,
        //   표시를 먼저 하면 뒤이은 숨김이 같은 GameObject를 꺼버려 진단 구체가 영영 비활성이 된다
        //   (→ AllGripped 불가 → cranialTouch 미완료 → 20초 폴백 버튼). 공유하지 않는 리그에선 순서 무관.
        SetGripsActive(rightGrips, false);              // 측두 5지는 교정부터 — 진단 중엔 숨김
        SetGripsActive(leftGrips, true);
        SetGripsEvaluating(leftGrips, true);
        SetGripsActive(diagnosisRightGrips, true);
        SetGripsEvaluating(diagnosisRightGrips, true);
        leftDepth?.SetEvaluating(false);
        rightDepth?.SetEvaluating(false);               // 압력 표시는 진단엔 없음
        postureStabilizer?.SetActive(false);
        // 호흡 HUD 비활성은 ScenarioManager가 substep 진입 시 일괄 처리(③ 아닐 때 HideBreathingHud)
        ChunaLogger.Log("[CranialAdjustmentController] 진단 촉진 단계 시작 (양손 후두 감싸기)");
    }

    /// <summary>
    /// 교정 파지점을 켜고 판정을 살린다.
    ///
    /// ★2026-08-12: "파지점은 직접 쓸 때만 보이게" 규칙을 넣으면서, 판정이 없는 안내 substep에서
    /// 교정 파지점을 끄도록 바꿨다. 그런데 <b>파지 유지를 판정하는 조건들이 파지점을 다시 켜지 않아서</b>
    /// 안내 행을 한 번 지나면 <c>cranialPressure</c>가 영영 성립하지 않았다(제1늑골 3.2에서 20초 폴백).
    /// → 파지점이 필요한 조건은 진입할 때 이걸 부른다.
    /// </summary>
    /// <summary>
    /// 지금 시술자 머리(HMD)의 높이(m). 체중을 싣는 동작을 판정하는 데 쓴다.
    /// 견착용 <see cref="postureStabilizer"/>에 배선된 헤드셋을 우선 쓰고, 없으면 메인 카메라를 본다.
    /// </summary>
    /// <summary>
    /// 양손 손바닥의 평균 높이(m). <b>손이 눌러 들어갔다 나오는</b> 것을 재는 데 쓴다.
    ///
    /// ★헤드셋 하강으로 순간 교정을 판정했더니 "생각보다 과하게 움직여야 해서 힘들다"는 지적이 있었다.
    /// 손은 실제로 누르는 부위라 변위가 작아도 의미가 분명하고, 시술자가 몸을 크게 낮출 필요가 없다.
    /// </summary>
    public bool TryGetHandDepth(out float y)
    {
        y = 0f;
        Transform lp = leftHandVisual != null ? ResolveFingertip(leftHandVisual, CranialFinger.Palm) : null;
        Transform rp = rightHandVisual != null ? ResolveFingertip(rightHandVisual, CranialFinger.Palm) : null;

        if (lp == null && rp == null) return false;
        if (lp == null) { y = rp.position.y; return true; }
        if (rp == null) { y = lp.position.y; return true; }

        y = (lp.position.y + rp.position.y) * 0.5f;
        return true;
    }

    public bool TryGetHeadHeight(out float y)
    {
        Transform h = postureStabilizer != null ? postureStabilizer.Headset : null;
        if (h == null && Camera.main != null) h = Camera.main.transform;

        if (h == null) { y = 0f; return false; }
        y = h.position.y;
        return true;
    }

    public void ShowCorrectionGrips()
    {
        TryInjectFingertips();
        // 진단 파지점(단계·레거시)은 전부 숨기고 교정 파지점만 표시.
        // ★ 숨김을 먼저, 표시를 나중에 — 같은 오브젝트를 공유해도 결과가 같아진다.
        HideAllGrips();
        SetGripsActive(leftGrips, true);
        SetGripsActive(rightGrips, true);
        SetHandJudgingActive(true);   // 안내 substep에서 정지됐을 수 있으니 재활성
    }

    /// <summary>① 파지 substep 시작 시 호출 (초기화/활성화 훅)</summary>
    public void BeginGripPhase()
    {
        EndDiagnosisStage();     // 진단 유지 타이머·안내 문구 정리
        ShowCorrectionGrips();   // 손끝 주입 + 교정 파지점 표시 + 판정 활성
        leftDepth?.ClearZeroPoint();
        rightDepth?.ClearZeroPoint();
        postureStabilizer?.SetActive(false);   // 자세 안내는 호흡 국면에서만
        // 호흡 HUD 비활성은 ScenarioManager가 substep 진입 시 일괄 처리(③ 아닐 때 HideBreathingHud)
        ChunaLogger.Log("[CranialAdjustmentController] 파지 단계 시작");
    }

    /// <summary>파지·깊이 판정 일괄 활성/정지. 호흡 국면(손이 FOV 밖)에선 정지시켜 튐을 막는다.</summary>
    private void SetHandJudgingActive(bool on)
    {
        SetGripsEvaluating(leftGrips, on);
        SetGripsEvaluating(rightGrips, on);
        leftDepth?.SetEvaluating(on);
        rightDepth?.SetEvaluating(on);
    }

    private static void SetGripsEvaluating(GripPointTarget[] grips, bool on)
    {
        if (grips == null) return;
        for (int i = 0; i < grips.Length; i++) grips[i]?.SetEvaluating(on);
    }

    /// <summary>②a 진입 시: 현재 파지 위치(휴식)를 영점으로 저장</summary>
    public void SaveZeroPoints()
    {
        if (!useDepthJudging) return;   // 깊이 판정 OFF면 영점 자체가 필요 없다
        TryInjectFingertips();   // 마지막 안전망 (Joints가 늦게 채워진 경우)
        leftDepth?.SaveZeroPoint();
        rightDepth?.SaveZeroPoint();
    }

    // === 라이브 손끝 주입 ===
    // HandVisual 미지정이면 아무것도 안 함 → 인스펙터 fingertip 그대로(기존 동작 불변).
    private bool fingertipsInjected = false;
    private GripPointTarget[] cachedStageLeftGrips;    // 전 진단 단계의 왼손 파지점(1회 수집)
    private GripPointTarget[] cachedStageRightGrips;   // 전 진단 단계의 오른손 파지점(1회 수집)

    /// <summary>지정된 HandVisual에서 깊이용 검지 끝 + 각 파지점이 지정한 손가락 끝을 주입.</summary>
    private void TryInjectFingertips()
    {
        if (fingertipsInjected) return;

        // 진단 단계 파지점은 런타임에 바뀌지 않으므로 1회만 수집해 캐시한다.
        if (cachedStageLeftGrips == null) cachedStageLeftGrips = CollectAllStageGrips(true);
        if (cachedStageRightGrips == null) cachedStageRightGrips = CollectAllStageGrips(false);

        // 미지정 손은 "할 일 없음"으로 통과 처리(=인스펙터값 유지).
        // 교정 파지점 + 진단 파지점(터치용)을 같은 손에 함께 주입(콜라이더 생성은 멱등).
        bool leftDone = leftHandVisual == null
            || (InjectHand(leftHandVisual, leftGrips, leftDepth, "leftHandVisual")
                && InjectHand(leftHandVisual, cachedStageLeftGrips, null, "diagnosisStages(왼손)"));
        bool rightDone = rightHandVisual == null
            || (InjectHand(rightHandVisual, rightGrips, rightDepth, "rightHandVisual")
                && InjectHand(rightHandVisual, diagnosisRightGrips, null, "diagnosisRightGrips")
                && InjectHand(rightHandVisual, cachedStageRightGrips, null, "diagnosisStages(오른손)"));

        if (leftDone && rightDone)
        {
            fingertipsInjected = true;
            if (leftHandVisual != null || rightHandVisual != null)
                ChunaLogger.Log("[CranialAdjustmentController] 라이브 손끝(fingertip) 주입 완료");
        }
    }

    /// <summary>한 손: 깊이(압력)는 검지끝 1점, 각 파지점은 지정 손가락 끝에 트리거 콜라이더 생성·연결.
    /// 필요한 조인트가 아직 준비 안 됐으면 false 반환 → Update가 다음 프레임 재시도.</summary>
    private bool InjectHand(HandVisual hv, GripPointTarget[] grips, DepthPressureGuide depth, string fieldName)
    {
        WarnIfNotLive(hv, fieldName);

        // Model B: 각 파지점의 손가락 끝을 해석 → 트리거 콜라이더 생성 + (깊이용) 손가락별 팁 수집
        int count = grips != null ? grips.Length : 0;
        var fingerIds = new System.Collections.Generic.List<CranialFinger>(count);
        var fingerTips = new System.Collections.Generic.List<Transform>(count);
        int gateIndex = 0;   // 손 단위 판정 대표 슬롯 (검지)

        for (int i = 0; i < count; i++)
        {
            GripPointTarget grip = grips[i];
            if (grip == null) continue;
            Transform tip = ResolveFingertip(hv, grip.Finger);
            if (tip == null) return false;               // 아직 준비 안 됨 → 재시도
            EnsureFingerCollider(tip, grip);
            if (grip.Finger == CranialFinger.Index) gateIndex = fingerTips.Count;
            fingerIds.Add(grip.Finger);
            fingerTips.Add(tip);
        }

        // 깊이(압력)는 depth가 있는 손만(=오른손 측두). 손가락별 팁 주입, 파지점 없으면 검지 단일 폴백.
        if (depth != null)
        {
            if (fingerTips.Count > 0)
            {
                depth.SetFingertips(fingerIds.ToArray(), fingerTips.ToArray(), gateIndex);
            }
            else
            {
                Transform indexTip = ResolveFingertip(hv, CranialFinger.Index);
                if (indexTip == null) return false;      // 조인트 미준비 → 재시도
                depth.SetFingertip(indexTip);
            }
        }
        return true;
    }

    /// <summary>연결한 HandVisual이 라이브 추적이 아닌 경우(=HandTransformMapper가 enabled=false로 캡처한 손)
    /// 경고. 이런 손은 조인트가 얼어붙어 깊이가 영점에 고정됨 → 라이브 추적 손을 연결해야 함.</summary>
    private void WarnIfNotLive(HandVisual hv, string fieldName)
    {
        if (hv != null && !hv.enabled)
            ChunaLogger.LogWarning($"[CranialAdjustmentController] {fieldName}이 비활성(enabled=false) — " +
                "HandTransformMapper에 캡처된 녹화/재생용 손일 수 있습니다. 깊이가 고정되니 " +
                "라이브 추적 HandVisual(예: ChunaLimitChecker가 참조하는 손)을 연결하세요.");
    }

    /// <summary>HandVisual.Joints에서 해당 손가락의 끝 Transform 해석. Tip 미보급 SDK는 distal(3) 조인트로 폴백.</summary>
    private Transform ResolveFingertip(HandVisual hv, CranialFinger finger)
    {
        if (hv == null || hv.Joints == null) return null;
        Transform t = JointAt(hv, TipJoint(finger));
        if (t == null) t = JointAt(hv, FallbackJoint(finger));
        return t;
    }

    private static Transform JointAt(HandVisual hv, HandJointId id)
    {
        int j = (int)id;
        if (j >= 0 && j < hv.Joints.Count && hv.Joints[j] != null) return hv.Joints[j];
        return null;
    }

    private static HandJointId TipJoint(CranialFinger f)
    {
        switch (f)
        {
            case CranialFinger.Thumb:  return HandJointId.HandThumbTip;
            case CranialFinger.Index:  return HandJointId.HandIndexTip;
            case CranialFinger.Middle: return HandJointId.HandMiddleTip;
            case CranialFinger.Ring:   return HandJointId.HandRingTip;
            case CranialFinger.Pinky:  return HandJointId.HandPinkyTip;
            case CranialFinger.Palm:   return HandJointId.HandMiddle1;     // 손 중앙(중지 MCP)
            default:                   return HandJointId.HandIndexTip;
        }
    }

    private static HandJointId FallbackJoint(CranialFinger f)
    {
        switch (f)
        {
            case CranialFinger.Thumb:  return HandJointId.HandThumb3;
            case CranialFinger.Index:  return HandJointId.HandIndex3;
            case CranialFinger.Middle: return HandJointId.HandMiddle3;
            case CranialFinger.Ring:   return HandJointId.HandRing3;
            case CranialFinger.Pinky:  return HandJointId.HandPinky3;
            case CranialFinger.Palm:   return HandJointId.HandMiddle1;
            default:                   return HandJointId.HandIndex3;
        }
    }

    // === Option B: 파지 트리거 콜라이더 자동생성 ===
    private const string FingerColliderName = "CranialGripFingerCollider";

    /// <summary>손끝 Transform 아래에 파지 판정용 트리거 콜라이더(자식)를 생성/재사용하고
    /// 해당 GripPointTarget의 expectedFingerCollider로 연결한다. 비파괴적(전용 자식 오브젝트)이고
    /// 멱등(이름으로 기존 자식 재사용)하다. Rigidbody는 GripPointTarget 쪽(Kinematic)에 있으므로
    /// 손끝 콜라이더는 isTrigger만 있으면 트리거 이벤트가 발생한다.</summary>
    private void EnsureFingerCollider(Transform fingertip, GripPointTarget grip)
    {
        if (!autoCreateFingerColliders || fingertip == null || grip == null) return;

        Transform existing = fingertip.Find(FingerColliderName);
        SphereCollider col;
        if (existing != null)
        {
            col = existing.GetComponent<SphereCollider>();
        }
        else
        {
            var go = new GameObject(FingerColliderName);
            go.transform.SetParent(fingertip, false);   // localPos/rot = 0 → 손끝/손목에 정확히 부착
            col = go.AddComponent<SphereCollider>();
            col.isTrigger = true;
        }

        if (col != null)
        {
            // 손바닥(Palm)은 손목 루트에 큰 반경, 손끝은 작은 반경
            col.radius = grip.Finger == CranialFinger.Palm ? palmColliderRadius : fingerColliderRadius;
            grip.SetExpectedFingerCollider(col);
        }
    }

    /// <summary>현재 자세(어깨-이마 밀착 프록시)가 성립했는가. 자세 안정화 미설정 시 통과 처리(순수 호흡 타이머).</summary>
    public bool IsPostureEngaged => postureStabilizer == null || postureStabilizer.IsInPosition;

    private bool braceGuideLookupDone;

    /// <summary>이마 견착 위치 가이드 표시/숨김.
    /// ScenarioManager가 substep 진입마다 CSV의 <c>brace</c> 토큰을 보고 호출한다
    /// (호흡 국면이 전부 gripGate라 자세 안정화의 활성 여부로는 판단할 수 없다 — 2026-08-13).
    /// 판정과는 무관한 순수 표시다.</summary>
    public void SetBraceGuideVisible(bool on)
    {
        if (braceGuide == null && !braceGuideLookupDone)
        {
            braceGuideLookupDone = true;   // 씬 배선을 빼먹어도 리그 하위에 있으면 살린다(도구가 리그 밑에 만든다).
            braceGuide = GetComponentInChildren<ShoulderBraceGuide>(true);
        }
        braceGuide?.SetVisible(on);
    }

    /// <summary>②b 호흡 윈도우 시작.
    /// 이 국면은 어깨-이마 자세로 손이 FOV를 벗어나 파지/압력 판정이 불가 →
    /// 유지 게이트를 "압력 적정존" 대신 "자세 안정화(헤드셋-이마 근접)"로 둔다.
    /// (압력 정확성은 직전 ②a 압력 substep에서 이미 검증됨.)</summary>
    /// <param name="gripGate">true면 호흡 1회를 인정하는 조건이 <b>양손 파지 성립</b>이 된다.
    /// PM처럼 호흡 내내 손이 머리에 남아 파지 판정이 가능한 술기용 — 시간만 흘러서는 카운트가
    /// 오르지 않고 파지점에 제대로 대고 있어야 세어진다.
    /// false(기본)면 기존대로 이마 견착 자세 프록시를 쓴다(OM·PJ는 손이 FOV 밖이라 파지 판정 불가).</param>
    public void StartBreathingWindow(bool gripGate = false)
        => StartBreathingWindow(gripGate, 0, 0f, 0f, BreathingSyncHUD.StartPhase.Keep, 0f);

    /// <summary>substep별 호흡 규격을 CSV에서 받아 여는 오버로드.
    /// ★한 술기 안에서 국면마다 호흡이 다른 경우가 있다 — PJ 교정이 그렇다
    /// (굴곡·외회전으로 잠근 채 <b>1회</b> 길게 → 신전·내회전으로 전환해 <b>3회</b>, 첫 회는 크게).
    /// 리그 단위 오버라이드 하나로는 표현이 안 되므로 CSV conditionParams가 있으면 그쪽이 이긴다.
    /// 인자가 0/Keep이면 해당 항목만 리그 오버라이드로 폴백한다.</summary>
    public void StartBreathingWindow(bool gripGate, int breaths, float inhaleSec, float exhaleSec,
                                     BreathingSyncHUD.StartPhase startPhase, float firstCycleScale)
    {
        if (breathingHUD == null)
        {
            ChunaLogger.LogWarning("[CranialAdjustmentController] BreathingSyncHUD 미설정");
            return;
        }

        // 손이 FOV를 벗어나 데이터가 튀는 국면 → 파지/깊이 판정 정지(사운드·경고·색 깜빡임·화살표 잔상 방지)
        // ★gripGate면 파지 성립 여부가 곧 게이트이므로 손 판정을 계속 살려 둔다.
        //   ★파지점 자체도 켜 둬야 한다 — 앞의 안내 substep에서 꺼졌을 수 있다(2026-08-12).
        if (gripGate) ShowCorrectionGrips();
        SetHandJudgingActive(gripGate);
        // 견착 국면 내내 손 메시 숨김(트래킹은 유지) — 가림/FOV 밖에서 튀는 손 비주얼 제거.
        // (PM처럼 손을 계속 머리에 대는 술기는 hideHandsDuringBreathing=false로 숨기지 않음)
        if (hideHandsDuringBreathing && !gripGate) SetHandVisualsHidden(true);

        // ★공유 HUD에 이 국면의 호흡법을 먼저 밀어 넣는다.
        //   우선순위 = CSV(substep별) > 리그 오버라이드 > HUD 인스펙터 값.
        breathingHUD.Configure(
            breaths    > 0  ? breaths    : breathCountOverride,
            inhaleSec  > 0f ? inhaleSec  : inhaleSecondsOverride,
            exhaleSec  > 0f ? exhaleSec  : exhaleSecondsOverride,
            startPhase != BreathingSyncHUD.StartPhase.Keep ? startPhase : breathStartPhaseOverride,
            firstCycleScale);

        if (gripGate)
        {
            // 파지 게이트: 견착 자세 안내는 띄우지 않는다(이 술기엔 견착이 없다).
            breathingHUD.SetTensionProvider(() => BothGripped);
        }
        else
        {
            postureStabilizer?.SetActive(true);   // 자세 안내 활성 + 판정 시작
            breathingHUD.SetTensionProvider(() => IsPostureEngaged);
        }
        breathingHUD.StartWindow();
    }

    public bool BreathingComplete => breathingHUD != null && breathingHUD.IsComplete;

    /// <summary>호흡 HUD(링)를 끈다. 견착·호흡(③) substep이 아닐 때 ScenarioManager가 매 substep 진입 시 호출.
    /// 활성화는 오직 <see cref="StartBreathingWindow"/>(=③ 진입)에서만 일어난다.</summary>
    public void HideBreathingHud()
    {
        // ③를 벗어나므로 손 메시·환자 모델을 원상 복구(견착 국면 렌더링 정리 해제).
        SetHandVisualsHidden(false);
        SetPatientVisible(true);
        if (breathingHUD == null) return;
        breathingHUD.StopWindow();
        if (breathingHUD.gameObject.activeSelf) breathingHUD.gameObject.SetActive(false);
    }

    // === 견착·호흡(③) 렌더링 정리: 손 메시 숨김 + 근접 시 환자 모델 숨김 ===

    /// <summary>손 메시 표시/숨김. 트래킹(조인트)은 그대로 두고 렌더만 끈다(HandVisual.ForceOffVisibility).</summary>
    private void SetHandVisualsHidden(bool hidden)
    {
        if (leftHandVisual != null) leftHandVisual.ForceOffVisibility = hidden;
        if (rightHandVisual != null) rightHandVisual.ForceOffVisibility = hidden;
    }

    private Renderer[] cachedPatientRenderers;
    private bool patientVisible = true;

    /// <summary>환자 모델 렌더러 수집(1회 캐시). CranialRig(this) 하위 렌더러(파지 구체·리듬 지표)는 제외.</summary>
    private Renderer[] ResolvePatientRenderers()
    {
        if (cachedPatientRenderers != null) return cachedPatientRenderers;

        Transform root = patientModelRoot;
        if (root == null)
        {
            GameObject tagged = null;
            try { tagged = GameObject.FindWithTag("Patient"); } catch { /* 태그 미정의 무시 */ }
            if (tagged != null) root = tagged.transform;
        }
        if (root == null) { cachedPatientRenderers = new Renderer[0]; return cachedPatientRenderers; }

        var all = root.GetComponentsInChildren<Renderer>(true);
        var list = new System.Collections.Generic.List<Renderer>(all.Length);
        for (int i = 0; i < all.Length; i++)
        {
            var r = all[i];
            if (r != null && !r.transform.IsChildOf(transform)) list.Add(r);   // CranialRig 하위 제외
        }
        cachedPatientRenderers = list.ToArray();
        return cachedPatientRenderers;
    }

    /// <summary>환자 모델 렌더러 일괄 표시/숨김(상태 변할 때만 토글).</summary>
    private void SetPatientVisible(bool visible)
    {
        if (patientVisible == visible) return;
        patientVisible = visible;
        var rends = ResolvePatientRenderers();
        for (int i = 0; i < rends.Length; i++)
            if (rends[i] != null) rends[i].enabled = visible;
    }

    void Awake()
    {
        // 씬에서 문구 오브젝트를 켜 둔 채 저장했더라도 시작부터 떠 있지 않게 한다.
        HideBreathingCue();
        DisableRhythmIndicatorIfNotCranial();

        // ★같은 이유로 호흡링과 파지 구체도 시작 전에는 보이면 안 된다(2026-08-13 사용자 지적).
        //   둘 다 씬에 켜진 채 저장돼 있어 로비→시작 안내 구간 내내 떠 있었다.
        //   켜는 곳은 각각 StartBreathingWindow(③)와 파지 단계뿐이므로 여기서 꺼도 회귀가 없다.
        if (breathingHUD != null) breathingHUD.ResetState();   // 완료 래칭까지 초기화 + 오브젝트 끔
        HideAllGrips();
    }

    /// <summary>
    /// 두개골 리듬(CRI) 표시는 <b>두개골 술기 전용</b>이다 — 늑골·흉추에서는 끈다.
    ///
    /// ★리그를 두개골에서 복제해 만들다 보니 늑골·흉추 리그에도 리듬 표시가 딸려 와서
    /// 관계없는 술기에 좌우 비대칭 지표가 계속 떠 있었다(2026-08-12 사용자 지적).
    /// 인스펙터에서 일일이 비우게 하는 대신, 시나리오 이름으로 판단해 자동으로 끈다.
    /// </summary>
    private void DisableRhythmIndicatorIfNotCranial()
    {
        if (!string.IsNullOrEmpty(scenarioName) && scenarioName.Contains("두개골")) return;

        if (rhythmIndicator != null)
        {
            rhythmIndicator.gameObject.SetActive(false);
            rhythmIndicator = null;   // 이후 SetMode 호출도 무시되게
        }

        // ★호흡 유도 문구도 두개골 전용이다("두개골을 이완시키며 촉진하세요").
        //   늑골·흉추 진단에서 이 박스가 뜨는 건 술기와 무관한 안내다.
        cueOnAllDiagnosisStages = false;
        HideBreathingCue();

        ChunaLogger.Log($"[CranialAdjustmentController] '{scenarioName}'은 두개골 술기가 아니므로 " +
                        "리듬 표시와 호흡 유도 문구를 껐습니다.");
    }

    // ★2026-08-12: 여기서 CranialBreathAnimator를 자동으로 붙였다가 <b>철회했다.</b>
    //   그 컴포넌트는 환자 Animator의 speed를 0으로 만들고 클립을 직접 스크럽하는데,
    //   State 존재 여부를 <b>한 번만 검사해 캐시</b>한다(stateChecked). 두개골(굴곡신전 있음)을 먼저 돌린 뒤
    //   늑골(그 State 없음)로 넘어가면 캐시된 '있음'을 믿고 애니메이터를 세워 버려
    //   "파지해도 애니가 제대로 재생 안 되고 재평가에서 중립도 안 지킨다"가 됐다.
    //   호흡 길이 동기화가 필요하면 씬에 직접 붙이되, 그 술기의 컨트롤러에 해당 State가 있어야 한다.

    void Update()
    {
        if (!fingertipsInjected) TryInjectFingertips();

        // 호흡 유도 문구 자동 소멸(진단 단계가 끝나 폴링이 멈춰도 확실히 사라지도록 Update에서 구동).
        UpdateBreathingCueFade();

        // 가이드손: 사용자 손이 파지 위치에 닿으면 끈다(무장돼 있을 때만).
        UpdateGuideHandAutoHide();

        // 평가 지표 누적(수집 중일 때만).
        UpdateCranialMetrics();

        // ★유지 타이머 표시 — <b>진단이 아닌 단계에서도</b> 돌아야 한다.
        //   예전에는 이 호출이 UpdateDiagnosisStage() 안에 있었는데, 그 함수는 진단 스테이지가 없으면
        //   첫 줄에서 return 해 버린다. 그래서 cranialPressure(등척성 5초 등)에서는 표시 함수가
        //   아예 호출되지 않아, 판정은 도는데 화면에 타이머가 안 나왔다(2026-08-12).
        UpdateHoldProgressUI();

        if (drivePoseFromComparator)
        {
            // TODO(M1): 여기서 HandPoseComparator 결과를 leftGrips/rightGrips 각 파지점.PoseRecognized에 주입
        }

        // 파지가 성립하면 대기시켜 둔 환자 애니를 재생한다(예: 왼손이 늑골 파지점을 잡으면 고개가 신전).
        UpdateGripTriggeredAnimation();

        // 진단 단계 진행 중이면 자세별 유지 타이머를 누적/초기화한다(활성 단계 없으면 즉시 반환).
        UpdateDiagnosisStage();

        // 압력 표시 = 오른손(측두) 파지 구체 색: 압력 국면(영점 저장 + 판정 중)엔 깊이 색을 덮어씌우고,
        // 그 외(파지 단계·호흡 단계)엔 해제 → 구체가 파지 여부 색(초록/idle)으로 복귀.
        UpdatePressureColorOnGrips();

        // 두개골 리듬: 호흡 교정 완료 전 = 비대칭(진단), 완료 후 = 대칭(재평가)
        if (rhythmIndicator != null)
            rhythmIndicator.SetMode(BreathingComplete
                ? CranialRhythmIndicator.Mode.Symmetric
                : CranialRhythmIndicator.Mode.Asymmetric);

        // ③ 견착 국면에서 머리를 숙여 자세가 성립(카메라가 환자에 근접)하면 환자 모델을 숨긴다.
        // (near-clip 뚫림·오버드로우 방지) 고개를 들면(release 히스테리시스) 복원. ③ 밖에선 항상 표시.
        // hidePatientDuringBreathing=false(기본)면 아예 숨기지 않는다 — 항상 표시.
        bool bowedClose = hidePatientDuringBreathing
                          && breathingHUD != null && breathingHUD.IsRunning
                          && postureStabilizer != null && postureStabilizer.IsInPosition;
        SetPatientVisible(!bowedClose);
    }

    /// <summary>오른손 측두 파지 구체에 압력 깊이 색을 매 프레임 주입/해제(손가락별).
    /// 깊이는 오른손(측두)만 측정하며, 각 구체는 자기 손가락 팁의 깊이 색을 표시한다.</summary>
    private void UpdatePressureColorOnGrips()
    {
        if (rightGrips == null) return;

        // 깊이 판정 OFF(기본): 압력 색을 칠하지 않고 파지 여부 색(초록/idle)만 남긴다.
        if (!useDepthJudging)
        {
            for (int k = 0; k < rightGrips.Length; k++) rightGrips[k]?.ClearPressureColor();
            return;
        }

        bool show = rightDepth != null && rightDepth.IsShowingPressure;
        for (int i = 0; i < rightGrips.Length; i++)
        {
            GripPointTarget grip = rightGrips[i];
            if (grip == null) continue;
            // 접촉/눌림 판정은 트리거가 아니라 깊이 기반(누르다 트리거를 벗어나도 안 꺼짐).
            // 손가락을 크게 뒤로 빼(비접촉) 있으면 해제 → 구체가 흰색(idle)로 복귀.
            if (show && rightDepth.IsFingerEngaged(grip.Finger))
                grip.SetPressureColor(rightDepth.ColorForFinger(grip.Finger));
            else
                grip.ClearPressureColor();
        }
    }

    /// <summary>파지점이 왜 색이 안 변하는지 한 번에 판별하는 덤프.
    /// Play 중 컴포넌트 우클릭 → 이 메뉴 실행. 각 파지점의 활성/판정중/접촉/IsGripped/렌더러/현재색을 출력한다.
    /// 읽는 법:
    ///   · 접촉=False   → 손끝 콜라이더가 파지점에 안 닿음(위치·반경 문제). 색이 안 변하는 게 정상.
    ///   · 접촉=True인데 IsGripped=False → 포즈 인식 미통과(bypassPoseCheck를 켜세요).
    ///   · IsGripped=True인데 색 그대로 → 압력색덮어씀=True(깊이 판정 끄기) 또는 targetRenderer 미배선.
    ///   · active=False → 그 단계에서 아예 안 켜진 파지점.</summary>
    [ContextMenu("진단: 파지점 상태 덤프")]
    public void DumpGripStates()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[CranialAdjustmentController] 파지점 상태 덤프 — {gameObject.name} (시나리오='{scenarioName}')");
        sb.AppendLine($"  손 배선: leftHandVisual={(leftHandVisual != null ? leftHandVisual.name : "없음")} / " +
                      $"rightHandVisual={(rightHandVisual != null ? rightHandVisual.name : "없음")} / " +
                      $"손끝콜라이더자동생성={autoCreateFingerColliders} 주입완료={fingertipsInjected}");
        sb.AppendLine($"  깊이 판정={useDepthJudging} (false면 압력색이 파지 초록색을 덮지 않음)");

        AppendGrips(sb, "교정 왼손(leftGrips)", leftGrips);
        AppendGrips(sb, "교정 오른손(rightGrips)", rightGrips);
        AppendGrips(sb, "레거시 진단(diagnosisRightGrips)", diagnosisRightGrips);

        if (diagnosisStages != null)
            for (int s = 0; s < diagnosisStages.Length; s++)
            {
                var stage = diagnosisStages[s];
                if (stage == null) continue;
                var grips = CollectStageGrips(stage);
                AppendGrips(sb, $"진단단계 '{stage.stageId}'", grips.ToArray());
            }

        ChunaLogger.Log(sb.ToString());
    }

    private static void AppendGrips(System.Text.StringBuilder sb, string title, GripPointTarget[] grips)
    {
        sb.AppendLine($"  ── {title}: {(grips == null || grips.Length == 0 ? "미배선" : grips.Length + "개")}");
        if (grips == null) return;
        for (int i = 0; i < grips.Length; i++)
            sb.AppendLine(grips[i] == null ? "      (null)" : "      " + grips[i].DescribeState());
    }

    /// <summary>술기 종료/리셋 (시나리오 시작·재시작 시 호출 → 래칭 상태 정리)</summary>
    public void ResetAll()
    {
        EndDiagnosisStage();
        ResetGrips(leftGrips);
        ResetGrips(rightGrips);
        ResetGrips(diagnosisRightGrips);
        if (diagnosisStages != null)
            for (int s = 0; s < diagnosisStages.Length; s++)
            {
                var grips = CollectStageGrips(diagnosisStages[s]);
                for (int i = 0; i < grips.Count; i++) grips[i]?.ResetState();
            }

        // 기본 표시: 전부 숨김 → 각 단계 Begin*(진단 단계/파지 단계)에서 필요한 것만 켠다.
        // (진단 단계를 쓰는 시나리오는 시작 시 아무 파지점도 안 보이는 게 맞다.)
        HideAllGrips();
        if (!HasDiagnosisStages) SetGripsActive(leftGrips, true);   // 레거시 배선은 기존 동작 유지
        leftDepth?.ClearZeroPoint();
        rightDepth?.ClearZeroPoint();
        breathingHUD?.ResetState();
        postureStabilizer?.SetActive(false);
        rhythmIndicator?.SetMode(CranialRhythmIndicator.Mode.Asymmetric);
        // ③ 렌더링 정리 원상 복구(재시작 대비)
        SetHandVisualsHidden(false);
        SetPatientVisible(true);
    }

    private static void ResetGrips(GripPointTarget[] grips)
    {
        if (grips == null) return;
        for (int i = 0; i < grips.Length; i++) grips[i]?.ResetState();
    }
}
