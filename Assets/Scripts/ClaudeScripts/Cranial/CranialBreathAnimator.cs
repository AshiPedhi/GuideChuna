using UnityEngine;

/// <summary>
/// 호흡 유도 구간(③ 견착·호흡) 동안 환자의 굴곡·신전 애니메이션을 **호흡 위상에 맞춰 재생**한다.
///
/// 그냥 Play로 틀면 클립이 자기 속도로 돌아 호흡과 어긋난다 → 여기서는 매 프레임
/// <see cref="BreathingSyncHUD"/>의 위상값으로 클립의 normalizedTime을 **직접 스크럽**해서
/// 들숨/날숨과 정확히 물리게 한다(호흡 길이를 바꿔도 자동으로 따라감).
///
/// ★ 사전 조건: 환자 Animator Controller(OM.controller)에 이 클립을 쓰는 **State가 있어야 한다.**
///    State 이름을 <see cref="stateName"/>에 적는다(기본 "굴곡신전").
///    State가 없으면 경고만 남기고 아무것도 하지 않는다(다른 동작에 영향 없음).
/// </summary>
public class CranialBreathAnimator : MonoBehaviour
{
    /// <summary>클립을 호흡 위상에 어떻게 대응시킬지.</summary>
    public enum SyncMode
    {
        /// <summary>클립 한 바퀴(0→1)가 호흡 한 주기(들숨+날숨) 전체.
        /// 클립 안에 굴곡→신전이 모두 들어 있는 경우(예: 3초짜리 '굴곡신전' 루프 클립).</summary>
        FullCycle,

        /// <summary>클립 0=중립, 1=최대 굴곡. 들숨에 0→1로 가고 날숨에 1→0으로 되돌아온다.
        /// 클립이 '중립→굴곡' 한 방향만 담고 있는 경우.</summary>
        PingPong,
    }

    [Header("연결")]
    [Tooltip("환자 Animator. 비우면 patientRoot(또는 태그 Patient)에서 자동 탐색.")]
    [SerializeField] private Animator patientAnimator;
    [Tooltip("patientAnimator를 비웠을 때 여기서 Animator를 찾는다. 이것도 비우면 태그 'Patient'로 탐색.")]
    [SerializeField] private Transform patientRoot;
    [Tooltip("호흡 위상을 제공하는 HUD. 비우면 씬에서 자동 탐색.")]
    [SerializeField] private BreathingSyncHUD breathingHUD;

    [Header("진단 구간 루프")]
    [Tooltip("★기본 ON. 진단 단계 동안 환자가 계속 호흡하도록 굴곡·신전을 루프 재생한다. " +
             "진단은 '리듬을 느끼는' 단계라 애니메이션이 멈춰 있으면 관찰할 게 없다.")]
    [SerializeField] private bool loopDuringDiagnosis = true;
    [Tooltip("루프를 돌릴 substep의 conditionType 목록. 진단은 cranialTouch.\n" +
             "★파지점 배선과 무관하게 이 단계에 들어오기만 하면 재생된다.")]
    [SerializeField] private string[] loopOnConditionTypes = { "cranialTouch" };
    [Tooltip("진단 루프의 들숨 길이(초). 진단은 '자연 호흡 관찰'이라 안정 호흡 비율(날숨이 더 김)로 둔다.")]
    [SerializeField] private float diagnosisInhaleSeconds = 3.2f;
    [Tooltip("진단 루프의 날숨 길이(초).")]
    [SerializeField] private float diagnosisExhaleSeconds = 4.8f;

    [Header("애니메이션")]
    [Tooltip("Animator Controller 안의 State 이름. 클립 이름과 다를 수 있으니 **State 이름**을 적을 것. (기본: 굴곡신전)")]
    [SerializeField] private string stateName = "굴곡신전";
    [Tooltip("State가 있는 레이어 번호(보통 0 = Base Layer).")]
    [SerializeField] private int layer = 0;

    [Tooltip("재생이 끝났을 때 돌아갈 State 이름. 비우면 그대로 둔다.\n" +
             "★비워 두면 굴곡·신전 State에 남아 다음 단계에서도 환자가 계속 호흡한다.")]
    [SerializeField] private string returnStateName = "idle";

    [Header("어느 구간에서 재생할지")]
    [Tooltip("★기본 ON. ③ 견착·호흡(시술) 구간에서 호흡 유도에 맞춰 재생한다.\n" +
             "PM처럼 진단에서만 보여주고 시술 중엔 안 쓰려면 끄세요.")]
    [SerializeField] private bool syncDuringBreathingWindow = true;

    [Tooltip("클립을 호흡 위상에 대응시키는 방식.\n" +
             "  · FullCycle : 클립 한 바퀴 = 호흡 한 주기 (굴곡·신전이 한 클립에 다 있을 때)\n" +
             "  · PingPong  : 들숨에 0→1, 날숨에 1→0 (클립이 중립→굴곡 한 방향일 때)")]
    [SerializeField] private SyncMode syncMode = SyncMode.FullCycle;

    [Tooltip("위상을 뒤집는다. 굴곡·신전이 호흡과 반대로 움직이면 켜세요.")]
    [SerializeField] private bool invert = false;

    [Header("클립 구간 (FullCycle 모드에서만 사용)")]
    [Tooltip("클립 전체 길이(초). Animation 창 맨 끝 시각. ※Animation 창은 '초:프레임' 표기 — " +
             "60fps에서 3:00 = 3.0초. 굴곡신전.anim = 3.0초.")]
    [SerializeField] private float clipLengthSeconds = 3f;

    [Tooltip("클립에서 **들숨이 끝나고 날숨이 시작되는** 시각(초). " +
             "굴곡신전.anim은 들숨 1:40(=1.667초) / 날숨 1:20(=1.333초)로 나뉘어 있으므로 1.667.\n" +
             "★이 값이 있어야 클립의 전환점과 호흡의 전환점이 정확히 맞는다 — " +
             "클립을 반으로 갈라 쓰면(0.5) 들숨이 끝나도 클립은 아직 들숨 구간에 남아 어긋난다.")]
    [SerializeField] private float inhaleEndSeconds = 1.6667f;

    [Header("디버그")]
    [SerializeField] private bool debugLog = false;

    private int stateHash;
    private bool stateChecked;
    private bool stateValid;
    private bool driving;          // 지금 이 스크립트가 Animator를 잡고 있는가
    private float restoreSpeed = 1f;

    private void Awake()
    {
        stateHash = Animator.StringToHash(stateName);
    }

    private void OnEnable()
    {
        var ev = ScenarioEventSystem.Instance;
        if (ev != null) ev.OnSubStepStarted += HandleSubStepStarted;
    }

    private void OnDisable()
    {
        var ev = ScenarioEventSystem.Instance;
        if (ev != null) ev.OnSubStepStarted -= HandleSubStepStarted;
        inLoopSubStep = false;
        StopDriving();
    }

    /// <summary>지금 substep이 '진단'인지만 본다. 파지점·진단단계 배선과 무관 — 단계에 들어오면 재생한다.</summary>
    private void HandleSubStepStarted(SubStepData subStep)
    {
        inLoopSubStep = subStep != null && MatchesLoopType(subStep.conditionType);
        if (!inLoopSubStep) loopTimer = 0f;
        if (debugLog)
            Debug.Log($"[CranialBreathAnimator] substep '{subStep?.conditionType}' → 진단루프={inLoopSubStep}");
    }

    private bool MatchesLoopType(string conditionType)
    {
        if (loopOnConditionTypes == null || loopOnConditionTypes.Length == 0) return false;
        if (string.IsNullOrWhiteSpace(conditionType)) return false;
        string t = conditionType.Trim();
        foreach (var v in loopOnConditionTypes)
            if (!string.IsNullOrEmpty(v) && v.Trim().Equals(t, System.StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private Animator ResolveAnimator()
    {
        if (patientAnimator != null) return patientAnimator;

        Transform root = patientRoot;
        if (root == null)
        {
            GameObject tagged = null;
            try { tagged = GameObject.FindWithTag("Patient"); } catch { /* 태그 미정의 무시 */ }
            if (tagged != null) root = tagged.transform;
        }
        if (root != null) patientAnimator = root.GetComponentInChildren<Animator>(true);
        return patientAnimator;
    }

    private BreathingSyncHUD ResolveHud()
    {
        if (breathingHUD != null) return breathingHUD;
        breathingHUD = FindObjectOfType<BreathingSyncHUD>(true);
        return breathingHUD;
    }

    /// <summary>Animator에 해당 State가 실제로 있는지 1회 확인(없으면 경고 후 영구 비활성).</summary>
    private bool EnsureState(Animator anim)
    {
        if (stateChecked) return stateValid;
        stateChecked = true;

        if (anim.runtimeAnimatorController == null)
        {
            Debug.LogWarning($"[CranialBreathAnimator] {anim.name}에 Animator Controller가 없습니다 — 굴곡·신전 재생을 건너뜁니다.");
            return stateValid = false;
        }

        stateValid = anim.HasState(layer, stateHash);
        if (!stateValid)
        {
            Debug.LogWarning(
                $"[CranialBreathAnimator] Animator Controller '{anim.runtimeAnimatorController.name}'의 " +
                $"레이어 {layer}에 State '{stateName}'가 없습니다.\n" +
                $"→ Animator 창에서 굴곡·신전 클립을 State로 추가하고 이름을 '{stateName}'로 맞추거나, " +
                $"이 컴포넌트의 State 이름을 실제 State 이름으로 바꾸세요.");
        }
        return stateValid;
    }

    private void LateUpdate()
    {
        var hud = ResolveHud();

        // 구동 소스 결정:
        //   ① 견착·호흡(③) 진행 중 → HUD의 실제 호흡 위상에 물린다(시술자가 호흡에 맞춰 압을 준다).
        //   ② 진단 단계 진행 중   → 환자 자연 호흡을 자체 타이머로 루프 재생(관찰용).
        bool byBreathWindow = syncDuringBreathingWindow && hud != null && hud.IsRunning;
        bool byDiagnosisLoop = !byBreathWindow && loopDuringDiagnosis && inLoopSubStep;

        if (!byBreathWindow && !byDiagnosisLoop)
        {
            StopDriving();
            loopTimer = 0f;
            return;
        }

        var anim = ResolveAnimator();
        if (anim == null || !EnsureState(anim)) return;

        if (!driving)
        {
            driving = true;
            restoreSpeed = anim.speed;
            anim.speed = 0f;    // 시간은 우리가 직접 준다(스크럽)
            if (debugLog)
                Debug.Log($"[CranialBreathAnimator] 굴곡·신전 스크럽 시작 " +
                          $"(state='{stateName}', mode={syncMode}, 소스={(byBreathWindow ? "호흡 윈도우" : "진단 루프")})");
        }

        // 호흡 주기 진행도(들숨 0→0.5, 날숨 0.5→1)를 구한다.
        float cycle01 = byBreathWindow ? hud.CycleProgress01 : AdvanceDiagnosisLoop();

        float normalized = syncMode == SyncMode.FullCycle
            ? MapFullCycle(cycle01)
            : (cycle01 <= 0.5f ? cycle01 / 0.5f : 1f - (cycle01 - 0.5f) / 0.5f);   // PingPong: 0→1→0
        if (invert) normalized = 1f - normalized;

        // 매 프레임 같은 State를 지정한 시각으로 다시 재생 = 스크럽
        anim.Play(stateHash, layer, normalized);
        anim.Update(0f);
    }

    private float loopTimer;
    private bool inLoopSubStep;   // 지금 substep이 진단인가(OnSubStepStarted가 갱신)

    /// <summary>진단 구간용 자체 호흡 루프. 들숨/날숨 길이를 따로 두고 주기 진행도(0→1)를 계속 돌린다.</summary>
    private float AdvanceDiagnosisLoop()
    {
        float inDur = Mathf.Max(0.01f, diagnosisInhaleSeconds);
        float outDur = Mathf.Max(0.01f, diagnosisExhaleSeconds);

        loopTimer += Time.deltaTime;
        float total = inDur + outDur;
        if (loopTimer >= total) loopTimer -= total * Mathf.Floor(loopTimer / total);   // 루프

        return loopTimer < inDur
            ? (loopTimer / inDur) * 0.5f                       // 들숨 0→0.5
            : 0.5f + ((loopTimer - inDur) / outDur) * 0.5f;    // 날숨 0.5→1
    }

    /// <summary>
    /// 호흡 주기 진행도(들숨 0→0.5, 날숨 0.5→1)를 클립의 normalizedTime으로 옮긴다.
    ///
    /// 클립의 들숨/날숨 길이가 서로 다르므로 **각 국면을 따로 늘려 맞춘다.**
    ///   들숨: 호흡 0→0.5  ↔  클립 0 → split
    ///   날숨: 호흡 0.5→1  ↔  클립 split → 1        (split = inhaleEndSeconds / clipLengthSeconds)
    /// 이렇게 해야 호흡이 날숨으로 넘어가는 순간 클립도 정확히 같은 지점에서 넘어간다.
    /// 호흡 길이(breatheIn/OutDuration)를 바꿔도 국면 비율로 계산하므로 그대로 맞는다.
    /// </summary>
    private float MapFullCycle(float cycle01)
    {
        float split = Mathf.Clamp01(inhaleEndSeconds / Mathf.Max(0.0001f, clipLengthSeconds));
        float c = Mathf.Clamp01(cycle01);

        return c <= 0.5f
            ? Mathf.Lerp(0f, split, c / 0.5f)               // 들숨 구간
            : Mathf.Lerp(split, 1f, (c - 0.5f) / 0.5f);     // 날숨 구간
    }

    /// <summary>구간이 끝나면 Animator를 원래대로 돌려준다.
    /// ★speed 복원만으로는 부족하다 — 우리가 Play로 굴곡·신전 State에 올려놨기 때문에,
    ///   그대로 두면 다음 단계(진단3·재평가·종료 등)에서도 환자가 계속 호흡한다.
    ///   그래서 returnStateName(기본 idle)으로 되돌려 놓는다.</summary>
    private void StopDriving()
    {
        if (!driving) return;
        driving = false;

        if (patientAnimator != null)
        {
            patientAnimator.speed = restoreSpeed;

            if (!string.IsNullOrWhiteSpace(returnStateName))
            {
                int backHash = Animator.StringToHash(returnStateName.Trim());
                if (patientAnimator.runtimeAnimatorController != null &&
                    patientAnimator.HasState(layer, backHash))
                {
                    patientAnimator.Play(backHash, layer, 0f);
                }
                else if (debugLog)
                {
                    Debug.LogWarning($"[CranialBreathAnimator] 복귀 State '{returnStateName}'를 찾지 못해 " +
                                     "굴곡·신전 State에 남습니다(다음 단계에서도 호흡이 이어질 수 있음).");
                }
            }
        }

        if (debugLog) Debug.Log("[CranialBreathAnimator] 굴곡·신전 종료 — Animator 원복");
    }

    [ContextMenu("진단: 연결 상태 확인")]
    private void DiagnoseWiring()
    {
        var anim = ResolveAnimator();
        var hud = ResolveHud();
        string ctrl = anim != null && anim.runtimeAnimatorController != null
            ? anim.runtimeAnimatorController.name : "없음";
        bool has = anim != null && anim.runtimeAnimatorController != null && anim.HasState(layer, Animator.StringToHash(stateName));

        float split = Mathf.Clamp01(inhaleEndSeconds / Mathf.Max(0.0001f, clipLengthSeconds));
        Debug.Log($"[CranialBreathAnimator] 진단\n" +
                  $"  Animator = {(anim != null ? anim.name : "없음")} / Controller = {ctrl}\n" +
                  $"  호흡 HUD = {(hud != null ? hud.name : "없음")}\n" +
                  $"  State '{stateName}' (레이어 {layer}) 존재 = {has}\n" +
                  $"  복귀 State '{returnStateName}' 존재 = " +
                  $"{(anim != null && anim.runtimeAnimatorController != null && !string.IsNullOrWhiteSpace(returnStateName) && anim.HasState(layer, Animator.StringToHash(returnStateName.Trim())))}\n" +
                  $"  재생 구간: 진단={loopDuringDiagnosis} / 시술(견착·호흡)={syncDuringBreathingWindow}\n" +
                  $"  동기화 = {syncMode}, invert = {invert}\n" +
                  $"  클립 {clipLengthSeconds:0.###}초 중 들숨 0~{inhaleEndSeconds:0.###}초 / " +
                  $"날숨 {inhaleEndSeconds:0.###}~{clipLengthSeconds:0.###}초 → 전환점 {split:0.000}");
    }
}
