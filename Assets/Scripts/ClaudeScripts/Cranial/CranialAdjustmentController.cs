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
///   ② 압력 학습 : 영점 저장 → BothInGoodZone 유지 (손가락별 색으로 적정 압력 학습, 과압 방지)
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

    [Header("=== 진단 촉진 파지 (양손 후두 감싸기, 압력·포즈 무관) ===")]
    [Tooltip("진단(촉진)은 양손으로 뒤통수(후두)를 감싸는 형태. 왼손은 교정과 동일한 후두 Palm(leftGrips)을 재사용하고, " +
             "오른손은 이 '후두 우측 Palm' 파지점을 쓴다. finger=Palm, bypassPoseCheck ON = 터치만. 보통 1개.")]
    [SerializeField] private GripPointTarget[] diagnosisRightGrips;

    [Header("=== 깊이 압력 (기능2) ===")]
    [SerializeField] private DepthPressureGuide leftDepth;
    [SerializeField] private DepthPressureGuide rightDepth;

    [Header("=== 호흡 (기능3) ===")]
    [SerializeField] private BreathingSyncHUD breathingHUD;

    [Header("=== 자세 안정화 (삼각근-이마, 호흡 게이트) ===")]
    [Tooltip("호흡 단계에서 손 대신 판정하는 자세 프록시(헤드셋-이마 근접). " +
             "비우면 호흡은 순수 3회 타이머로 동작(자세 판정 없음).")]
    [SerializeField] private CranialPostureStabilizer postureStabilizer;

    [Header("=== 견착·호흡(③) 렌더링 정리 (머리 숙임 부작용 방지) ===")]
    [Tooltip("환자 모델 루트. ③에서 머리를 숙여 자세가 성립(카메라가 환자에 근접)하면 이 하위 렌더러를 꺼서 " +
             "near-clip 뚫림/오버드로우를 막고, 고개를 들면 복원한다. CranialRig 하위(파지 구체 등)는 자동 제외. " +
             "비우면 태그 'Patient'로 자동 탐색.")]
    [SerializeField] private Transform patientModelRoot;

    [Tooltip("③ 견착·호흡 진입 시 손 메시를 숨길지. OM(견착=손이 FOV 밖으로 나감)은 true. " +
             "PM처럼 손을 계속 머리에 댄 채 진행하는 술기는 false(손 계속 표시).")]
    [SerializeField] private bool hideHandsDuringBreathing = true;

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

    /// <summary>진단 촉진: 양손으로 후두(뒤통수)를 감쌌는가(터치만, 압력·깊이 무관).
    /// 왼손 = 후두 Palm(leftGrips, 교정과 공유) + 오른손 = 후두 우측 Palm(diagnosisRightGrips).
    /// 터치 판정은 GripPointTarget.IsGripped(트리거 진입 AND bypassPoseCheck/PoseRecognized)이므로
    /// 진단 파지점은 bypassPoseCheck를 켜서 순수 터치로 성립시킨다.</summary>
    public bool BothHandsTouched => AllGripped(leftGrips) && AllGripped(diagnosisRightGrips);

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

    /// <summary>① 파지 substep 시작 시 호출 (초기화/활성화 훅)</summary>
    public void BeginGripPhase()
    {
        TryInjectFingertips();   // 영점 저장(②a) 전에 손끝 확보
        // 교정: 진단 전용 오른손 후두 Palm 숨김, 왼손 후두 Palm 유지 + 측두 5지 표시
        SetGripsActive(diagnosisRightGrips, false);
        SetGripsActive(leftGrips, true);
        SetGripsActive(rightGrips, true);
        SetHandJudgingActive(true);   // 호흡국면에서 정지됐을 수 있으니 재진입 시 재활성(재시작 대비)
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
        TryInjectFingertips();   // 마지막 안전망 (Joints가 늦게 채워진 경우)
        leftDepth?.SaveZeroPoint();
        rightDepth?.SaveZeroPoint();
    }

    // === 라이브 손끝 주입 ===
    // HandVisual 미지정이면 아무것도 안 함 → 인스펙터 fingertip 그대로(기존 동작 불변).
    private bool fingertipsInjected = false;

    /// <summary>지정된 HandVisual에서 깊이용 검지 끝 + 각 파지점이 지정한 손가락 끝을 주입.</summary>
    private void TryInjectFingertips()
    {
        if (fingertipsInjected) return;

        // 미지정 손은 "할 일 없음"으로 통과 처리(=인스펙터값 유지).
        // 교정 파지점 + 진단 촉진 파지점(터치용)을 같은 손에 함께 주입(콜라이더 생성은 멱등).
        bool leftDone = leftHandVisual == null
            || InjectHand(leftHandVisual, leftGrips, leftDepth, "leftHandVisual");
        bool rightDone = rightHandVisual == null
            || (InjectHand(rightHandVisual, rightGrips, rightDepth, "rightHandVisual")
                && InjectHand(rightHandVisual, diagnosisRightGrips, null, "diagnosisRightGrips"));

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

    /// <summary>②b 호흡 윈도우 시작.
    /// 이 국면은 어깨-이마 자세로 손이 FOV를 벗어나 파지/압력 판정이 불가 →
    /// 유지 게이트를 "압력 적정존" 대신 "자세 안정화(헤드셋-이마 근접)"로 둔다.
    /// (압력 정확성은 직전 ②a 압력 substep에서 이미 검증됨.)</summary>
    public void StartBreathingWindow()
    {
        if (breathingHUD == null)
        {
            ChunaLogger.LogWarning("[CranialAdjustmentController] BreathingSyncHUD 미설정");
            return;
        }

        // 손이 FOV를 벗어나 데이터가 튀는 국면 → 파지/깊이 판정 정지(사운드·경고·색 깜빡임·화살표 잔상 방지)
        SetHandJudgingActive(false);
        // 견착 국면 내내 손 메시 숨김(트래킹은 유지) — 가림/FOV 밖에서 튀는 손 비주얼 제거.
        // (PM처럼 손을 계속 머리에 대는 술기는 hideHandsDuringBreathing=false로 숨기지 않음)
        if (hideHandsDuringBreathing) SetHandVisualsHidden(true);

        postureStabilizer?.SetActive(true);   // 자세 안내 활성 + 판정 시작
        breathingHUD.SetTensionProvider(() => IsPostureEngaged);
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

    void Update()
    {
        if (!fingertipsInjected) TryInjectFingertips();

        if (drivePoseFromComparator)
        {
            // TODO(M1): 여기서 HandPoseComparator 결과를 leftGrips/rightGrips 각 파지점.PoseRecognized에 주입
        }

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
        bool bowedClose = breathingHUD != null && breathingHUD.IsRunning
                          && postureStabilizer != null && postureStabilizer.IsInPosition;
        SetPatientVisible(!bowedClose);
    }

    /// <summary>오른손 측두 파지 구체에 압력 깊이 색을 매 프레임 주입/해제(손가락별).
    /// 깊이는 오른손(측두)만 측정하며, 각 구체는 자기 손가락 팁의 깊이 색을 표시한다.</summary>
    private void UpdatePressureColorOnGrips()
    {
        if (rightGrips == null) return;

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

    /// <summary>술기 종료/리셋 (시나리오 시작·재시작 시 호출 → 래칭 상태 정리)</summary>
    public void ResetAll()
    {
        ResetGrips(leftGrips);
        ResetGrips(rightGrips);
        ResetGrips(diagnosisRightGrips);
        // 기본 표시: 왼손 후두 Palm(진단·교정 공통) 표시, 진단 전용 오른손 후두 Palm·측두 5지는 각 단계 Begin*에서 켬
        SetGripsActive(leftGrips, true);
        SetGripsActive(diagnosisRightGrips, false);
        SetGripsActive(rightGrips, false);
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
