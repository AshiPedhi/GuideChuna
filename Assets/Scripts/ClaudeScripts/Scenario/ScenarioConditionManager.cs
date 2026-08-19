using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using ChunaTraining;  // DifficultyManager, NarrationType 사용

/// <summary>
/// 시나리오 조건 체크 인터페이스
/// 사용자가 직접 조건을 구현할 수 있도록 제공
/// </summary>
public interface IScenarioCondition
{
    bool IsConditionMet();
    string GetConditionDescription();
}

/// <summary>
/// 시나리오 조건 관리자
/// ✅ conditionType 기반 완전 자동화
/// - conditionType="HandPose": 손 동작 조건 (자동 등록)
/// - conditionType="Duration": duration 후 자동 진행
/// - conditionType="Manual": 토글로 수동 진행
/// - conditionType="PatientAnimation": 환자 애니메이션(AutoPlay) 완료 대기
/// - conditionType="PassiveStretch": 보조수 접촉 게이팅 AutoPlay + 가이드 손 표시 (주동수 없는 스트레칭)
/// - conditionType="Narration": 나레이션 완료 대기 (미구현)
/// - conditionType="None" 또는 빈칸: duration > 0이면 Duration, 아니면 Manual
///
/// ★ 나레이션(voiceInstruction) 우선 규칙:
///   voiceInstruction이 있으면 나레이션을 먼저 재생하고, 동작 완료 게이트(HandPose/cranial 등)가
///   없는 구간은 **나레이션이 끝나는 즉시 자동 진행**한다(duration 추가 대기 없음).
///   duration 컬럼은 나레이션이 꺼진(PlayNarration=false) 경우의 폴백 타이머로만 쓰인다.
///   단, 가이드 스텝(시작/종료, IsGuideStep)은 나레이션 후에도 버튼(토글) 입력을 기다린다.
/// </summary>
public class ScenarioConditionManager : MonoBehaviour
{
    [Header("=== 조건 체크 설정 ===")]
    [Tooltip("조건 체크 간격 (초)")]
    [SerializeField] private float checkInterval = 0.5f;

    [Tooltip("완료 알림 패널이 스스로 사라지기까지 (초). ★진행을 막지 않는다 — 표시만 하고 곧바로 다음 단계로 넘어간다.")]
    [SerializeField] private float completionDelay = 2f;

    [Tooltip("완료 피드백(유사도) 표시 시간 (초). ★진행을 막지 않는다.")]
    [SerializeField] private float feedbackVisibleSeconds = 2.5f;

    [Tooltip("20초 이상 진행 안될 경우 토글 버튼 활성화 (초)")]
    [SerializeField] private float progressTimeout = 20f;

    /// <summary>
    /// 게이트 조건도 duration도 없는 '작업' 단계의 폴백 진행 시간(초).
    /// 상급·평가에서 통합 나레이션이 없어 무음이 된 안내 단계가 여기에 걸린다(HandleDurationOrManual 참조).
    /// ★인스펙터에 노출하지 않는다 — 씬에 리그가 5개라 직렬화값이 코드 기본값을 덮어써 손으로 5곳을 맞춰야 한다.
    /// </summary>
    private const int SilentStepFallbackSeconds = 3;

    [Header("=== 완료 알림 UI (Final Fallback) ===")]
    [Tooltip("피드백 UI가 없을 때 사용되는 간단한 완료 알림")]
    [SerializeField] private GameObject completionAlertPanel;
    [SerializeField] private TMPro.TextMeshProUGUI completionAlertText;

    [Header("=== 사운드 (선택사항) ===")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip completionSound;

    [Tooltip("단계 완료음(띵동) 볼륨. 비워 두면 Resources/Audio/StepComplete를 자동으로 쓴다.\n" +
             "★신규 필드라 이미 배치된 씬에도 코드 기본값이 먹는다.")]
    [SerializeField, Range(0f, 1f)] private float completionVolume = 1f;

    /// <summary>완료음 볼륨 하한. 씬에 0.7로 직렬화돼 있어 코드 기본값만 올려서는 안 먹는다(08-18 실측).</summary>
    private const float MinCompletionVolume = 0.95f;

    [Header("=== 나레이션 설정 ===")]
    [Tooltip("나레이션 전용 AudioSource (없으면 audioSource 사용)")]
    [SerializeField] private AudioSource narrationAudioSource;
    [Tooltip("나레이션 클립 폴더 경로 (Resources 기준)")]
    [SerializeField] private string narrationFolderPath = "Narrations";
    private string narrationScenarioFolder = "";  // 시나리오별 서브폴더 (Bootstrapper에서 주입)

    [Header("=== UI 참조 ===")]
    [SerializeField] private ScenarioGuideUIController guideUIController;

    [Header("=== 단계 피드백 UI ===")]
    [Tooltip("단계 완료 시 유사도 피드백 UI (별도 컴포넌트)")]
    [SerializeField] private StepFeedbackUI stepFeedbackUI;

    [Tooltip("ChunaPathEvaluator 참조 (유사도 가져오기용)")]
    [SerializeField] private ChunaPathEvaluator pathEvaluator;

    // 현재 조건
    private IScenarioCondition currentCondition;
    private bool isCheckingCondition = false;
    private Coroutine checkCoroutine;

    // 이벤트 시스템
    private ScenarioEventSystem eventSystem;
    private ScenarioManager scenarioManager;

    // 조건 레지스트리 (SubStep별로 조건을 등록)
    private Dictionary<string, IScenarioCondition> conditionRegistry = new Dictionary<string, IScenarioCondition>();

    // 나레이션 관련
    private AudioClip currentNarrationClip;
    private Coroutine narrationCoroutine;
    private string currentVoiceClipName;  // ★ 현재 재생 중인 나래이션 클립명 (홀드 후 나래이션용)
    private string lastNarratedStepKey;   // ★ 상급/평가 모드: step 통합 나래이션을 1회만 재생하기 위한 키 (phaseName_stepName)

    // Quest 최적화: WaitForSeconds 캐싱
    private WaitForSeconds cachedCheckInterval;
    private WaitForSeconds cachedCompletionDelay;

    void Awake()
    {
        eventSystem = ScenarioEventSystem.Instance;
        scenarioManager = FindFirstObjectByType<ScenarioManager>();

        // GuideUIController가 설정되지 않았으면 자동으로 찾기
        if (guideUIController == null)
        {
            guideUIController = FindFirstObjectByType<ScenarioGuideUIController>();
        }

        // StepFeedbackUI 자동 찾기 (비활성화된 오브젝트도 포함)
        if (stepFeedbackUI == null)
        {
            stepFeedbackUI = FindFirstObjectByType<StepFeedbackUI>(FindObjectsInactive.Include);
        }

        // ChunaPathEvaluator 자동 찾기 (비활성화된 오브젝트도 포함)
        if (pathEvaluator == null)
        {
            pathEvaluator = FindFirstObjectByType<ChunaPathEvaluator>(FindObjectsInactive.Include);
        }

        // Quest 최적화: WaitForSeconds 객체 캐싱
        cachedCheckInterval = new WaitForSeconds(checkInterval);
        cachedCompletionDelay = new WaitForSeconds(completionDelay);

        // 완료 알림 패널 초기화
        if (completionAlertPanel != null)
        {
            completionAlertPanel.SetActive(false);
        }
    }

    void OnEnable()
    {
        // 이벤트 구독
        eventSystem.OnSubStepStarted += OnSubStepStarted;
        eventSystem.OnScenarioStarted += OnScenarioStartedReset;

        // ★ ChunaPathEvaluator 시작홀드 완료 이벤트 구독 (홀드 후 나래이션용)
        if (pathEvaluator != null)
        {
            pathEvaluator.OnStartHoldComplete += OnStartHoldCompleteForNarration;
        }
    }

    void OnDisable()
    {
        // 이벤트 구독 해제
        eventSystem.OnSubStepStarted -= OnSubStepStarted;
        eventSystem.OnScenarioStarted -= OnScenarioStartedReset;

        // ★ ChunaPathEvaluator 이벤트 구독 해제
        if (pathEvaluator != null)
        {
            pathEvaluator.OnStartHoldComplete -= OnStartHoldCompleteForNarration;
        }

        // 진행 중인 체크 중단
        StopConditionCheck();

        // 나레이션 중지
        StopNarration();
    }

    void OnDestroy()
    {
        // ★ 모든 조건 비활성화 및 이벤트 구독 해제 (메모리 누수 방지)
        ClearAllConditions();
    }

    // ★ 조건 처리 대기용 코루틴
    private Coroutine conditionProcessCoroutine;

    /// <summary>
    /// ★2026-08-19 '앞 단계의 폴백이 다음 단계를 잡아먹는' 문제 차단용 토큰.
    ///
    /// 자동 진행 코루틴들은 전부 "무언가를 기다린 뒤 NextSubStep()"인데, 기다리는 대상이
    /// (나레이션·AutoPlay) 단계 경계를 넘어 계속될 수 있어서 <b>단계가 이미 넘어간 뒤에</b>
    /// 깨어나 다음 단계를 한 칸 더 밀어버렸다.
    ///
    /// 실측(Editor.log, 상부승모근 평가):
    ///   83026  진단 SubStep2 → 3초 폴백 시작
    ///   83419  NextSubStep   → AutoPlay 완료가 정상 진행시킴
    ///   87871  제한장벽확인 게이트 오픈
    ///   87915  "3초 경과 - 다음 단계로 자동 진행"  ← 진단의 폴백이 뒤늦게 만료
    ///   87928  NextSubStep   → 제한장벽확인을 손도 못 대고 통과
    /// 스트레칭→재평가도 같은 순서로 재현됐다(103938 / 104288 / 109098 / 109111).
    /// 결과 CSV에도 그 두 단계만 유사도 샘플 0개로 남는다.
    ///
    /// OnSubStepStarted마다 1씩 올리고, 코루틴은 시작 시점의 값을 들고 있다가
    /// 진행 직전에 대조한다. <b>값이 다르면 = 그 단계는 이미 끝났다 = 진행은 이미 일어났다</b>
    /// 이므로 무시해도 진행이 막히지 않는다(현재 단계가 예약한 진행은 값이 같아 그대로 통과).
    /// </summary>
    private int subStepToken;

    /// <summary>
    /// 코루틴이 예약될 당시의 SubStep이 아직 현재 SubStep인지 확인한다.
    /// false면 그 사이 단계가 넘어간 것이므로 NextSubStep을 호출하면 안 된다.
    /// </summary>
    private bool IsProgressStillOwned(int token, string where)
    {
        if (token == subStepToken) return true;

        ChunaLogger.LogWarning(
            $"<color=orange>[ConditionManager] 지난 단계의 자동 진행 무시({where}) — " +
            $"예약 당시 토큰={token}, 현재={subStepToken}. " +
            $"단계가 이미 넘어갔으므로 중복 진행을 취소한다.</color>");
        return false;
    }

    // ★ AutoPlay 대기 중인 코루틴 추적 (이중 진행 방지)
    private bool isWaitingForAutoPlay = false;

    /// <summary>
    /// conditionManager가 AutoPlay 완료를 기다리고 있는지 (ScenarioManager 이중 진행 방지용)
    /// </summary>
    public bool IsWaitingForAutoPlay => isWaitingForAutoPlay;

    /// <summary>
    /// 시나리오 시작 시 step별 나래이션 1회 재생 키 리셋 (재실행 시 다시 재생되도록)
    /// </summary>
    private void OnScenarioStartedReset(ScenarioData scenario)
    {
        lastNarratedStepKey = null;
    }

    /// <summary>
    /// SubStep 시작 시 호출 - CSV 데이터 기반 자동 조건 처리
    /// ✅ conditionType 기반 자동 조건 등록
    /// ★ 한 프레임 지연으로 조건 등록 타이밍 문제 해결
    /// </summary>
    private void OnSubStepStarted(SubStepData subStep)
    {
        // ★ 이전 SubStep의 나레이션/조건 체크 중단 (토글로 빠르게 진행 시 충돌 방지)
        StopNarration();
        StopConditionCheck();

        // ★단계가 바뀌었음을 표시한다 — 앞 단계가 예약해 둔 자동 진행 코루틴은 여기서 무효가 된다.
        //   (코루틴을 직접 StopCoroutine 하지 않는 이유: WaitForNarrationThenNextStep처럼
        //    ScenarioManager가 외부에서 시작하는 것도 있어 핸들을 전부 붙잡을 수 없다.
        //    토큰 대조는 시작 경로와 무관하게 걸린다.)
        subStepToken++;

        // ★앞 단계의 완료 피드백을 즉시 지운다 — 여기서 해야 <b>모든</b> 단계에 적용된다.
        //   ProcessConditionByType에만 두면 마지막 판정(재평가) 다음의 '종료' 가이드 단계처럼
        //   조건이 없는 단계로 넘어갈 때 정리되지 않아 종료 문구 위에 퍼센트가 겹쳐 남았다(2026-08-12).
        HideCompletionFeedbackNow();

        ChunaLogger.Log($"<color=cyan>[ConditionManager] ===== OnSubStepStarted 호출 =====</color>");
        ChunaLogger.Log($"[ConditionManager] Phase: {scenarioManager.CurrentPhase.phaseName}, Step: {scenarioManager.CurrentStep.stepName}, SubStep: {subStep.subStepNo}");
        ChunaLogger.Log($"[ConditionManager] Duration: {subStep.duration}초");
        ChunaLogger.Log($"[ConditionManager] ConditionType: {subStep.conditionType}");
        ChunaLogger.Log($"[ConditionManager] HandTracking: {(string.IsNullOrEmpty(subStep.handTrackingFileName) ? "(없음)" : subStep.handTrackingFileName)}");
        ChunaLogger.Log($"[ConditionManager] 가이드 스텝: {scenarioManager.CurrentStep.IsGuideStep()}");

        // 가이드 스텝(Step번호 0)은 항상 토글로 수동 진행
        // ★ 단, 나레이션이 있으면 나레이션 먼저 재생
        if (scenarioManager.CurrentStep.IsGuideStep())
        {
            if (subStep.HasNarration())
            {
                ChunaLogger.Log("[ConditionManager] 가이드 스텝 - 나레이션 먼저 재생 후 토글 대기");
                HandleNarrationThenManual(subStep);
                return;
            }

            ChunaLogger.Log("[ConditionManager] 가이드 스텝 - 토글로 수동 진행");
            currentCondition = null;
            StopConditionCheck();
            eventSystem.RequestButtonStateUpdate(false);
            return;
        }

        // ★ 한 프레임 지연 후 조건 처리 (ScenarioManager에서 조건 등록할 시간 확보)
        if (conditionProcessCoroutine != null)
        {
            StopCoroutine(conditionProcessCoroutine);
        }
        conditionProcessCoroutine = StartCoroutine(ProcessConditionByTypeDelayed(subStep));
    }

    /// <summary>
    /// ★ 한 프레임 지연 후 조건 처리 (타이밍 문제 해결)
    /// ScenarioManager.OnSubStepStartedForHandPose가 먼저 실행되어 조건을 등록하도록 함
    /// </summary>
    private IEnumerator ProcessConditionByTypeDelayed(SubStepData subStep)
    {
        // 한 프레임 대기 - 다른 이벤트 핸들러들이 먼저 실행되도록
        yield return null;

        ChunaLogger.Log($"<color=yellow>[ConditionManager] 한 프레임 대기 후 조건 처리 시작</color>");
        ProcessConditionByType(subStep);
    }

    /// <summary>
    /// 조건 타입에 따라 적절한 조건 처리
    /// </summary>
    private void ProcessConditionByType(SubStepData subStep)
    {
        // ★새 단계가 시작되면 앞 단계의 완료 피드백을 즉시 지운다 (2026-08-12).
        //   완료 후 곧바로 진행하도록 바꾸면서 피드백이 타이머로만 사라지게 됐는데,
        //   그 사이에 다음 단계 지시문이 그려져 이전 퍼센트 텍스트 위에 겹쳐 보였다.
        HideCompletionFeedbackNow();

        string conditionKey = GetConditionKey(subStep);

        // conditionType이 명시되어 있으면 우선 사용
        string conditionType = string.IsNullOrEmpty(subStep.conditionType) ? "None" : subStep.conditionType;

        // conditionType이 None이고 handTrackingFileName이 있으면 HandPose로 자동 설정
        if (conditionType == "None" && !string.IsNullOrEmpty(subStep.handTrackingFileName))
        {
            conditionType = "HandPose";
        }

        // ★계측(2026-08-18): "학습모드는 되는데 평가모드만 진행이 안 된다"의 원인을 정적 분석으로
        //   좁히지 못했다(난이도로 갈리는 지점은 전부 표시 전용이었다). 실제 런타임 상태를 찍는다.
        //   조건이 등록됐는가 / 나레이션 분기로 갔는가 / 폴링이 시작됐는가가 여기서 다 갈린다.
        {
            var _dm = DifficultyManager.Instance;
            ChunaLogger.Log($"<color=yellow>[ConditionManager] 조건 타입 처리: {conditionType}</color>" +
                            $"  난이도={(_dm != null ? _dm.CurrentLevel.ToString() : "없음")}" +
                            $"  key='{conditionKey}'  등록됨={conditionRegistry.ContainsKey(conditionKey)}" +
                            $"  나레이션={(subStep.HasNarration() ? $"있음('{subStep.voiceInstruction.Trim()}')" : "없음")}");
        }

        // ★ 나레이션이 있으면 먼저 재생 후 동작 진행
        if (subStep.HasNarration())
        {
            // ★ HandPose 및 cranial 조건(등록형)은 나레이션 후 등록된 조건 폴링을 시작 (제네릭하게 동작)
            if (conditionType == "HandPose" || conditionType == "cranialTouch" || conditionType == "cranialGrip" || conditionType == "cranialPressure" || conditionType == "cranialDepthBreath" || conditionType == "cranialGlide")
            {
                ChunaLogger.Log($"<color=cyan>[ConditionManager] 나레이션 + 조건({conditionType}) 병합 - 나레이션 먼저 재생</color>");
                HandleNarrationThenHandPose(subStep, conditionKey);
                return;
            }
            else
            {
                // 나레이션 + Duration 또는 나레이션만 있는 경우
                ChunaLogger.Log($"<color=cyan>[ConditionManager] 나레이션 존재 - 나레이션 재생 후 Duration/Manual 적용</color>");
                HandleNarrationThenDuration(subStep);
                return;
            }
        }

        switch (conditionType)
        {
            case "HandPose":
                // HandPose 조건은 ScenarioActionHandler가 등록함
                // 여기서는 조건이 등록되었는지만 확인
                if (conditionRegistry.ContainsKey(conditionKey))
                {
                    currentCondition = conditionRegistry[conditionKey];
                    StartConditionCheck();
                    eventSystem.RequestButtonStateUpdate(false);
                    ChunaLogger.Log("[ConditionManager] HandPose 조건 - 자동 진행 (조건 대기)");
                }
                else
                {
                    ChunaLogger.LogWarning($"[ConditionManager] HandPose 조건이 등록되지 않았습니다. 시간 기반으로 전환합니다.");
                    HandleDurationOrManual(subStep);
                }
                break;

            case "PatientAnimation":
                ChunaLogger.Log("[ConditionManager] PatientAnimation 조건 - AutoPlay 완료 대기 후 진행");
                currentCondition = null;
                StopConditionCheck();
                eventSystem.RequestButtonStateUpdate(false);
                StartCoroutine(WaitForAutoPlayThenProgress(subStep));
                break;

            case "PassiveStretch":
                ChunaLogger.Log("[ConditionManager] PassiveStretch 조건 - 보조수 접촉 게이팅 AutoPlay 완료 대기 후 진행");
                currentCondition = null;
                StopConditionCheck();
                eventSystem.RequestButtonStateUpdate(false);
                StartCoroutine(WaitForAutoPlayThenProgress(subStep));
                break;

            case "Narration":
                HandleNarrationCondition(subStep);
                break;

            case "Duration":
                // 명시적으로 Duration 사용
                if (subStep.duration > 0)
                {
                    ChunaLogger.Log($"[ConditionManager] Duration 조건 - {subStep.duration}초 후 자동 진행");
                    currentCondition = null;
                    StopConditionCheck();
                    eventSystem.RequestButtonStateUpdate(false);
                    StartCoroutine(AutoProgressWithoutAlert(subStep.duration));
                }
                else
                {
                    ChunaLogger.LogWarning("[ConditionManager] Duration이 0입니다. 수동 진행으로 전환합니다.");
                    HandleManualProgress();
                }
                break;

            case "Manual":
                // 명시적으로 수동 진행
                ChunaLogger.Log("[ConditionManager] Manual 조건 - 토글로 수동 진행");
                HandleManualProgress();
                break;

            case "None":
            default:
                // 조건이 등록되어 있는지 확인
                if (conditionRegistry.ContainsKey(conditionKey))
                {
                    currentCondition = conditionRegistry[conditionKey];
                    StartConditionCheck();
                    eventSystem.RequestButtonStateUpdate(false);
                    ChunaLogger.Log("[ConditionManager] 등록된 조건 발견 - 자동 진행 (조건 대기)");
                }
                else
                {
                    // 등록된 조건이 없으면 duration 또는 수동 진행
                    HandleDurationOrManual(subStep);
                }
                break;
        }
    }

    /// <summary>
    /// Duration 또는 Manual 처리
    /// </summary>
    private void HandleDurationOrManual(SubStepData subStep)
    {
        if (subStep.duration > 0)
        {
            ChunaLogger.Log($"[ConditionManager] Duration={subStep.duration}초 - 자동 진행");
            currentCondition = null;
            StopConditionCheck();
            eventSystem.RequestButtonStateUpdate(false);
            StartCoroutine(AutoProgressWithoutAlert(subStep.duration));
            return;
        }

        // ★2026-08-19 진행 막힘 수정:
        //   여기로 오는 경로는 전부 "기다릴 게이트 조건이 없다"는 뜻이다(호출부 7곳 전부 동일).
        //   그 상태에서 duration까지 0이면 수동 버튼 대기로 빠지는데, 작업 단계에서는 그대로 멈춰버린다.
        //   상급·평가는 CSV voice를 무시하고 {난이도}/{stepName} 통합 클립만 찾고
        //   (LoadNarrationClipInternal의 'CSV 무시 원칙'), 신규 시나리오는 그 클립이 없어 무음 →
        //   나레이션 완료 트리거도 없고 조건도 등록 안 돼 20초 폴백·유지 타이머도 안 돈다.
        //   → 폴백 타이머로 진행시킨다. AutoProgressWithoutAlert가 나레이션·환자 애니 완료를
        //     기다려 주므로 재생 중인 것을 자르지 않는다.
        //   stepNo==0(가이드 = 시작 버튼·phase 전환 안내)은 원래 버튼 대기가 설계이므로 건드리지 않는다.
        bool isGuideStep = scenarioManager != null && scenarioManager.CurrentStep != null
                           && scenarioManager.CurrentStep.IsGuideStep();

        if (!isGuideStep)
        {
            ChunaLogger.LogWarning(
                $"<color=orange>[ConditionManager] 게이트 조건도 duration도 없는 작업 단계 — " +
                $"{SilentStepFallbackSeconds}초 폴백으로 자동 진행 " +
                $"(Step='{(scenarioManager?.CurrentStep != null ? scenarioManager.CurrentStep.stepName : "?")}' " +
                $"SubStep={subStep.subStepNo})</color>");
            currentCondition = null;
            StopConditionCheck();
            eventSystem.RequestButtonStateUpdate(false);
            StartCoroutine(AutoProgressWithoutAlert(SilentStepFallbackSeconds));
            return;
        }

        ChunaLogger.Log("[ConditionManager] Duration 없음 - 토글로 수동 진행 (가이드 단계)");
        HandleManualProgress();
    }

    /// <summary>
    /// 나레이션 조건 처리 - voiceInstruction을 클립명으로 사용하여 로드 및 재생
    /// (나레이션만 있는 경우 - 완료 후 다음 단계로 자동 진행)
    /// </summary>
    private void HandleNarrationCondition(SubStepData subStep)
    {
        if (!subStep.HasNarration())
        {
            ChunaLogger.LogWarning("[ConditionManager] voiceInstruction이 비어있습니다. Duration/Manual로 전환.");
            HandleDurationOrManual(subStep);
            return;
        }

        // 나레이션 클립 로드
        string clipName = subStep.voiceInstruction.Trim();
        currentVoiceClipName = clipName;  // ★ 홀드 후 나래이션용 클립명 저장
        AudioClip clip = LoadNarrationClip(clipName);

        if (clip == null)
        {
            ChunaLogger.LogWarning($"[ConditionManager] 나레이션 클립을 찾을 수 없습니다: {clipName}. Duration/Manual로 전환.");
            HandleDurationOrManual(subStep);
            return;
        }

        // 나레이션 재생 및 완료 대기
        currentCondition = null;
        StopConditionCheck();
        eventSystem.RequestButtonStateUpdate(false);

        narrationCoroutine = StartCoroutine(PlayNarrationAndProgress(clip, clipName));
        ChunaLogger.Log($"<color=cyan>[ConditionManager] 나레이션 재생 시작: {clipName} ({clip.length:F1}초)</color>");
    }

    /// <summary>
    /// 나레이션 + HandPose 병합 조건 처리
    /// 나레이션 재생 완료 후 HandPose 조건 체크 시작 (충돌체/가이드핸드 활성화 + 20초 타이머)
    /// </summary>
    private void HandleNarrationThenHandPose(SubStepData subStep, string conditionKey)
    {
        // 나레이션 클립 로드
        string clipName = subStep.voiceInstruction.Trim();
        currentVoiceClipName = clipName;  // ★ 홀드 후 나래이션용 클립명 저장
        AudioClip clip = LoadNarrationClip(clipName);

        if (clip == null)
        {
            ChunaLogger.LogWarning($"[ConditionManager] 나레이션 클립을 찾을 수 없습니다: {clipName}. HandPose만 진행.");
            // 나레이션 없이 HandPose 조건만 처리
            StartHandPoseCondition(conditionKey, subStep);
            return;
        }

        // 나레이션 재생 중에는 버튼 비활성화
        currentCondition = null;
        StopConditionCheck();
        eventSystem.RequestButtonStateUpdate(false);

        // 나레이션 재생 후 HandPose 조건 시작
        narrationCoroutine = StartCoroutine(PlayNarrationThenStartHandPose(clip, clipName, subStep, conditionKey));
        ChunaLogger.Log($"<color=cyan>[ConditionManager] 나레이션 + HandPose: 나레이션 먼저 재생 ({clip.length:F1}초)</color>");
    }

    /// <summary>
    /// 나레이션 + (동작 게이트 없는) 자동 진행 처리.
    /// 나레이션이 재생되면 재생 완료 시점에 자동 진행한다(duration 추가 대기 없음).
    /// 나레이션이 비활성(PlayNarration=false)이면 LoadNarrationClip이 null → HandleDurationOrManual
    /// 로 빠져 CSV duration을 폴백 타이머로 사용한다.
    /// </summary>
    private void HandleNarrationThenDuration(SubStepData subStep)
    {
        // 나레이션 클립 로드
        string clipName = subStep.voiceInstruction.Trim();
        currentVoiceClipName = clipName;  // ★ 홀드 후 나래이션용 클립명 저장
        AudioClip clip = LoadNarrationClip(clipName);

        if (clip == null)
        {
            ChunaLogger.LogWarning($"[ConditionManager] 나레이션 클립을 찾을 수 없습니다: {clipName}. Duration/Manual로 전환.");
            HandleDurationOrManual(subStep);
            return;
        }

        // 나레이션 재생 중에는 버튼 비활성화
        currentCondition = null;
        StopConditionCheck();
        eventSystem.RequestButtonStateUpdate(false);

        // 나레이션 재생 후 자동 진행 (동작 게이트 없는 구간 = 나레이션 끝나면 진행)
        narrationCoroutine = StartCoroutine(PlayNarrationThenApplyDuration(clip, clipName, subStep));
        ChunaLogger.Log($"<color=cyan>[ConditionManager] 나레이션 재생 ({clip.length:F1}초) → 완료 시 자동 진행 (duration={subStep.duration}초는 나레이션 OFF 폴백)</color>");
    }

    /// <summary>
    /// 나레이션 + Manual 병합 조건 처리 (가이드 스텝용)
    /// 나레이션 재생 완료 후 토글 대기
    /// </summary>
    private void HandleNarrationThenManual(SubStepData subStep)
    {
        // 나레이션 클립 로드
        string clipName = subStep.voiceInstruction.Trim();
        currentVoiceClipName = clipName;  // ★ 홀드 후 나래이션용 클립명 저장
        AudioClip clip = LoadNarrationClip(clipName);

        if (clip == null)
        {
            ChunaLogger.LogWarning($"[ConditionManager] 나레이션 클립을 찾을 수 없습니다: {clipName}. 바로 토글 대기.");
            HandleManualProgress();
            return;
        }

        // 나레이션 재생 중에는 버튼 비활성화
        currentCondition = null;
        StopConditionCheck();
        eventSystem.RequestButtonStateUpdate(false);

        // 나레이션 재생 후 Manual (토글 대기)
        narrationCoroutine = StartCoroutine(PlayNarrationThenManual(clip, clipName, subStep));
        ChunaLogger.Log($"<color=cyan>[ConditionManager] 나레이션 + Manual: 나레이션 먼저 재생 ({clip.length:F1}초) → 토글 대기</color>");
    }

    /// <summary>
    /// 나레이션 재생 후 Manual (토글 대기) 코루틴
    /// </summary>
    private IEnumerator PlayNarrationThenManual(AudioClip clip, string clipName, SubStepData subStep)
    {
        currentNarrationClip = clip;

        // AudioSource 선택
        AudioSource targetSource = narrationAudioSource != null ? narrationAudioSource : audioSource;

        if (targetSource == null)
        {
            ChunaLogger.LogError("[ConditionManager] 나레이션을 재생할 AudioSource가 없습니다!");
            HandleManualProgress();
            yield break;
        }

        // 나레이션 재생
        targetSource.clip = clip;
        targetSource.Play();
        ChunaLogger.Log($"<color=green>[ConditionManager] 나레이션 재생 중: {clipName}</color>");

        // 클립 재생 완료까지 대기
        yield return new WaitForSeconds(clip.length);

        ChunaLogger.Log($"<color=green>[ConditionManager] 나레이션 완료: {clipName} → 토글 대기</color>");
        currentNarrationClip = null;

        // ★ 나레이션 완료 후 Duration 적용 (있으면)
        if (subStep.duration > 0)
        {
            ChunaLogger.Log($"<color=cyan>[ConditionManager] Duration {subStep.duration}초 대기 시작</color>");
            yield return new WaitForSeconds(subStep.duration);
            ChunaLogger.Log($"<color=cyan>[ConditionManager] Duration {subStep.duration}초 완료</color>");
        }

        // Manual (토글 대기)
        HandleManualProgress();
    }

    /// <summary>
    /// 나레이션 재생 후 HandPose 조건 시작 코루틴
    /// </summary>
    private IEnumerator PlayNarrationThenStartHandPose(AudioClip clip, string clipName, SubStepData subStep, string conditionKey)
    {
        currentNarrationClip = clip;

        // AudioSource 선택
        AudioSource targetSource = narrationAudioSource != null ? narrationAudioSource : audioSource;

        if (targetSource == null)
        {
            ChunaLogger.LogError("[ConditionManager] 나레이션을 재생할 AudioSource가 없습니다!");
            // 나레이션 없이 HandPose만 시작
            StartHandPoseCondition(conditionKey, subStep);
            yield break;
        }

        // 나레이션 재생
        targetSource.clip = clip;
        targetSource.Play();
        ChunaLogger.Log($"<color=green>[ConditionManager] 나레이션 재생 중: {clipName}</color>");

        // 클립 재생 완료까지 대기
        yield return new WaitForSeconds(clip.length);

        ChunaLogger.Log($"<color=green>[ConditionManager] 나레이션 완료: {clipName} → HandPose 조건 시작</color>");
        currentNarrationClip = null;

        // ★ 나레이션 완료 후 HandPose 조건 시작 (충돌체/가이드핸드 활성화 + 20초 타이머)
        StartHandPoseCondition(conditionKey, subStep);
    }

    /// <summary>
    /// 나레이션 재생 후 자동 진행 코루틴 (나레이션 완료 = 진행 트리거, duration 추가 대기 없음)
    /// </summary>
    private IEnumerator PlayNarrationThenApplyDuration(AudioClip clip, string clipName, SubStepData subStep)
    {
        int token = subStepToken;
        currentNarrationClip = clip;

        // AudioSource 선택
        AudioSource targetSource = narrationAudioSource != null ? narrationAudioSource : audioSource;

        if (targetSource == null)
        {
            ChunaLogger.LogError("[ConditionManager] 나레이션을 재생할 AudioSource가 없습니다!");
            // 나레이션 없이 Duration/Manual만 적용
            HandleDurationOrManual(subStep);
            yield break;
        }

        // 나레이션 재생
        targetSource.clip = clip;
        targetSource.Play();
        ChunaLogger.Log($"<color=green>[ConditionManager] 나레이션 재생 중: {clipName}</color>");

        // 클립 재생 완료까지 대기
        yield return new WaitForSeconds(clip.length);

        ChunaLogger.Log($"<color=green>[ConditionManager] 나레이션 완료: {clipName}</color>");
        currentNarrationClip = null;

        // ★ 나레이션 완료 = 진행 트리거.
        //   동작 완료 게이트(HandPose/cranial 등)가 없는 구간은 나레이션이 끝나면 자동 진행한다.
        //   CSV의 duration은 "나레이션 OFF(PlayNarration=false)일 때의 폴백 타이머"로만 쓰인다
        //   — 그 경우 LoadNarrationClip이 null을 반환해 HandleDurationOrManual로 빠지므로 여기까진 오지 않는다.
        //   따라서 나레이션이 실제로 재생된 이 경로에선 duration만큼 추가로 기다리지 않는다.

        // 혹시 다른 나래이션(홀드 후 등)이 재생 중이면 대기
        yield return WaitForNarrationComplete();

        // AutoPlay(환자 애니메이션)가 아직 재생 중이면 완료까지 대기
        yield return WaitForAutoPlayComplete();

        // 다음 SubStep으로 자동 진행
        ChunaLogger.Log("[ConditionManager] 나레이션 완료 → 다음 단계로 자동 진행");
        if (scenarioManager != null && IsProgressStillOwned(token, "PlayNarrationThenApplyDuration"))
        {
            scenarioManager.NextSubStep();
        }
    }

    /// <summary>
    /// HandPose 조건 시작 (충돌체/가이드핸드 활성화 + 20초 타이머)
    /// </summary>
    private void StartHandPoseCondition(string conditionKey, SubStepData subStep)
    {
        if (conditionRegistry.ContainsKey(conditionKey))
        {
            currentCondition = conditionRegistry[conditionKey];
            LogPollingStart("StartHandPoseCondition");
            StartConditionCheck();  // 20초 타이머 포함
            eventSystem.RequestButtonStateUpdate(false);
            ChunaLogger.Log($"<color=magenta>[ConditionManager] HandPose 조건 시작 - 충돌체/가이드핸드 활성화, 20초 타이머 시작</color>");
        }
        else
        {
            ChunaLogger.LogWarning($"[ConditionManager] HandPose 조건이 등록되지 않았습니다: {conditionKey}. Duration/Manual로 전환.");
            HandleDurationOrManual(subStep);
        }
    }

    /// <summary>
    /// Resources 폴더에서 나레이션 클립 로드
    /// ★ 난이도별 다른 나래이션 로드 지원
    /// - BeginnerGuided: Narrations/Beginner/{clipName}
    /// - IntermediateSimple: Narrations/Intermediate/{clipName}
    /// - Fallback: Narrations/{clipName} (공통)
    /// </summary>
    private AudioClip LoadNarrationClip(string clipName)
    {
        // ★ 난이도 설정에서 나래이션 비활성화 시 null 반환 (각 핸들러에서 fallback 처리)
        if (DifficultyManager.Instance != null && !DifficultyManager.Instance.PlayNarration)
        {
            ChunaLogger.Log($"[ConditionManager] 나래이션 비활성화 (PlayNarration=false): {clipName} 스킵");
            return null;
        }

        // 확장자 제거 (있을 경우)
        if (clipName.EndsWith(".wav") || clipName.EndsWith(".mp3") || clipName.EndsWith(".ogg"))
        {
            clipName = System.IO.Path.GetFileNameWithoutExtension(clipName);
        }

        // ★ 중급자 이상: {phase}_{clipName} 형태로 먼저 시도
        bool isNotBeginner = DifficultyManager.Instance != null &&
            DifficultyManager.Instance.CurrentLevel != DifficultyLevel.Beginner;

        if (isNotBeginner && scenarioManager != null && scenarioManager.CurrentPhase != null)
        {
            string phaseName = scenarioManager.CurrentPhase.phaseName;
            if (!string.IsNullOrEmpty(phaseName))
            {
                string phaseClipName = $"{phaseName}_{clipName}";
                AudioClip phaseClip = LoadNarrationClipInternal(phaseClipName);
                if (phaseClip != null)
                {
                    ChunaLogger.Log($"<color=cyan>[ConditionManager] Phase별 나래이션 로드 성공: {phaseName}_{clipName} ({phaseClip.length:F1}초)</color>");
                    return phaseClip;
                }

                ChunaLogger.Log($"[ConditionManager] Phase별 나래이션 없음: {phaseClipName} → 기본 클립명으로 fallback");
            }
        }

        // 기본 로드 (초급자 또는 phase별 클립이 없는 경우)
        return LoadNarrationClipInternal(clipName);
    }

    /// <summary>
    /// Resources 폴더에서 나레이션 클립 실제 로드
    /// 난이도별/시나리오별 폴더 탐색 후 공통 폴더 fallback
    /// ★ 상급/평가 모드 + 비가이드 스텝: CSV voice 무시, stepName 통합 나래이션을 step별 1회만 반환
    /// </summary>
    private AudioClip LoadNarrationClipInternal(string clipName)
    {
        // ★ 난이도별 서브폴더 결정
        string difficultyFolder = GetNarrationSubfolder();
        string scenarioFolder = string.IsNullOrEmpty(narrationScenarioFolder) ? "" : narrationScenarioFolder;
        string basePath = string.IsNullOrEmpty(narrationFolderPath) ? "" : narrationFolderPath;

        // ★ 상급/평가 모드 처리: 비가이드 스텝은 CSV voice를 완전히 무시
        bool isAdvancedOrEval = DifficultyManager.Instance != null &&
            (DifficultyManager.Instance.CurrentLevel == DifficultyLevel.Advanced ||
             DifficultyManager.Instance.CurrentLevel == DifficultyLevel.Evaluation);

        if (isAdvancedOrEval && scenarioManager != null && scenarioManager.CurrentStep != null
            && !scenarioManager.CurrentStep.IsGuideStep())
        {
            string stepName = scenarioManager.CurrentStep.stepName;
            string phaseName = scenarioManager.CurrentPhase != null ? scenarioManager.CurrentPhase.phaseName : "";
            string stepKey = $"{phaseName}_{stepName}";

            // 같은 step의 두 번째 이후 substep → 무음 (CSV voice 무시)
            if (lastNarratedStepKey == stepKey)
            {
                ChunaLogger.Log($"[ConditionManager] 상급/평가: '{stepKey}' 이미 1회 재생됨 → 나래이션 스킵");
                return null;
            }

            // 첫 substep: stepName 기반 통합 나래이션 로드 시도
            if (!string.IsNullOrEmpty(stepName))
            {
                string stepPath = string.IsNullOrEmpty(basePath)
                    ? $"{difficultyFolder}/{stepName}"
                    : $"{basePath}/{difficultyFolder}/{stepName}";

                AudioClip stepClip = Resources.Load<AudioClip>(stepPath);
                if (stepClip != null)
                {
                    lastNarratedStepKey = stepKey;
                    ChunaLogger.Log($"<color=cyan>[ConditionManager] 스텝 통합 나래이션 로드: {stepPath} ({stepClip.length:F1}초)</color>");
                    return stepClip;
                }

                ChunaLogger.LogWarning($"[ConditionManager] 상급/평가: 스텝 통합 나래이션 없음: {stepPath} → 무음 진행");
            }

            // 클립이 없어도 CSV voice로 fallback 안 함 (CSV 무시 원칙)
            lastNarratedStepKey = stepKey;
            return null;
        }

        // 1차 시도: 시나리오별 + 난이도별 폴더 (Narrations/{난이도}/{시나리오}/{clipName})
        AudioClip clip = null;
        if (!string.IsNullOrEmpty(scenarioFolder))
        {
            string scenarioPath = string.IsNullOrEmpty(basePath)
                ? $"{difficultyFolder}/{scenarioFolder}/{clipName}"
                : $"{basePath}/{difficultyFolder}/{scenarioFolder}/{clipName}";

            clip = Resources.Load<AudioClip>(scenarioPath);
            if (clip != null)
            {
                ChunaLogger.Log($"<color=cyan>[ConditionManager] 시나리오별 나래이션 로드 성공: {scenarioPath} ({clip.length:F1}초)</color>");
                return clip;
            }
        }

        // 2차 시도: 난이도별 폴더 (Narrations/{난이도}/{clipName}) — 기존 호환
        string difficultyPath = string.IsNullOrEmpty(basePath)
            ? $"{difficultyFolder}/{clipName}"
            : $"{basePath}/{difficultyFolder}/{clipName}";

        clip = Resources.Load<AudioClip>(difficultyPath);
        if (clip != null)
        {
            ChunaLogger.Log($"<color=cyan>[ConditionManager] 난이도별 나래이션 로드 성공: {difficultyPath} ({clip.length:F1}초)</color>");
            return clip;
        }

        // 3차 시도: 공통 폴더에서 로드 (Fallback)
        string fallbackPath = string.IsNullOrEmpty(basePath)
            ? clipName
            : $"{basePath}/{clipName}";

        clip = Resources.Load<AudioClip>(fallbackPath);

        if (clip != null)
        {
            ChunaLogger.Log($"[ConditionManager] 공통 나래이션 로드 성공 (Fallback): {fallbackPath} ({clip.length:F1}초)");
        }
        else
        {
            ChunaLogger.LogWarning($"[ConditionManager] 나레이션 클립 로드 실패: {difficultyPath} 또는 {fallbackPath}");
        }

        return clip;
    }

    /// <summary>
    /// ★ 현재 난이도에 따른 나래이션 서브폴더 반환
    /// </summary>
    private string GetNarrationSubfolder()
    {
        if (DifficultyManager.Instance == null)
            return "Intermediate";  // 기본값

        switch (DifficultyManager.Instance.CurrentLevel)
        {
            case DifficultyLevel.Beginner:
                return "Beginner";
            case DifficultyLevel.Intermediate:
                return "Intermediate";
            case DifficultyLevel.Advanced:
                return "Advanced";
            case DifficultyLevel.Evaluation:
                return "Evaluation";
            default:
                return "Intermediate";
        }
    }

    /// <summary>
    /// 나레이션 재생 후 자동 진행
    /// </summary>
    private IEnumerator PlayNarrationAndProgress(AudioClip clip, string clipName)
    {
        int token = subStepToken;
        currentNarrationClip = clip;

        // AudioSource 선택 (narrationAudioSource 우선, 없으면 audioSource 사용)
        AudioSource targetSource = narrationAudioSource != null ? narrationAudioSource : audioSource;

        if (targetSource == null)
        {
            ChunaLogger.LogError("[ConditionManager] 나레이션을 재생할 AudioSource가 없습니다!");
            yield break;
        }

        // 나레이션 재생
        targetSource.clip = clip;
        targetSource.Play();

        ChunaLogger.Log($"<color=green>[ConditionManager] 나레이션 재생 중: {clipName}</color>");

        // 클립 재생 완료까지 대기
        yield return new WaitForSeconds(clip.length);

        ChunaLogger.Log($"<color=green>[ConditionManager] 나레이션 완료: {clipName}</color>");

        currentNarrationClip = null;

        // ★ AutoPlay(환자 애니메이션)가 아직 재생 중이면 완료까지 대기
        yield return WaitForAutoPlayComplete();

        // 다음 SubStep으로 진행
        if (scenarioManager != null && IsProgressStillOwned(token, "PlayNarrationAndProgress"))
        {
            scenarioManager.NextSubStep();
        }
    }

    /// <summary>
    /// 나레이션 중지
    /// </summary>
    public void StopNarration()
    {
        if (narrationCoroutine != null)
        {
            StopCoroutine(narrationCoroutine);
            narrationCoroutine = null;
        }

        AudioSource targetSource = narrationAudioSource != null ? narrationAudioSource : audioSource;
        if (targetSource != null && targetSource.isPlaying && currentNarrationClip != null)
        {
            targetSource.Stop();
        }

        currentNarrationClip = null;
        currentVoiceClipName = null;  // ★ 홀드 후 나래이션용 클립명 초기화
        ChunaLogger.Log("[ConditionManager] 나레이션 중지됨");
    }

    /// <summary>
    /// 나레이션 재생 중인지 확인
    /// </summary>
    public bool IsPlayingNarration => currentNarrationClip != null;

    /// <summary>
    /// ★ 나레이션(홀드 후 포함)이 재생 중인지 확인
    /// </summary>
    private bool IsAnyNarrationPlaying()
    {
        AudioSource targetSource = narrationAudioSource != null ? narrationAudioSource : audioSource;
        return targetSource != null && targetSource.isPlaying;
    }

    /// <summary>
    /// ★ 나레이션 완료까지 대기하는 코루틴
    /// </summary>
    private IEnumerator WaitForNarrationComplete()
    {
        AudioSource targetSource = narrationAudioSource != null ? narrationAudioSource : audioSource;

        if (targetSource == null || !targetSource.isPlaying)
        {
            yield break;
        }

        ChunaLogger.Log("<color=yellow>[ConditionManager] 나래이션 완료 대기 중...</color>");

        while (targetSource.isPlaying)
        {
            yield return null;
        }

        ChunaLogger.Log("<color=yellow>[ConditionManager] 나래이션 완료됨 - 다음 단계 진행</color>");
    }

    /// <summary>
    /// ★ AutoPlay(환자 애니메이션 자동 재생) 완료까지 대기하는 코루틴
    /// 나래이션이 짧거나 없는 난이도에서 애니메이션이 잘리는 것을 방지
    /// </summary>
    private IEnumerator WaitForAutoPlayComplete()
    {
        if (pathEvaluator == null || !pathEvaluator.IsAutoPlayMode)
        {
            yield break;
        }

        isWaitingForAutoPlay = true;
        ChunaLogger.Log("<color=yellow>[ConditionManager] AutoPlay 완료 대기 중...</color>");

        while (pathEvaluator.IsAutoPlayMode)
        {
            yield return null;
        }

        isWaitingForAutoPlay = false;
        ChunaLogger.Log("<color=yellow>[ConditionManager] AutoPlay 완료됨</color>");
    }

    /// <summary>
    /// ★ AutoPlay 완료 대기 후 다음 단계 진행 (conditionType="PatientAnimation"용)
    /// AutoPlay 완료 후 나래이션도 대기한 뒤 진행
    /// </summary>
    private IEnumerator WaitForAutoPlayThenProgress(SubStepData subStep)
    {
        int token = subStepToken;

        yield return WaitForAutoPlayComplete();
        yield return WaitForNarrationComplete();

        if (scenarioManager != null && IsProgressStillOwned(token, "WaitForAutoPlayThenProgress"))
        {
            ChunaLogger.Log("[ConditionManager] PatientAnimation 완료 - 다음 단계로 진행");
            scenarioManager.NextSubStep();
        }
    }

    /// <summary>
    /// ★ 나래이션 완료 후 다음 단계로 진행 (외부 호출용)
    /// ScenarioManager.OnAutoPlayCompletedHandler 등에서 사용
    /// </summary>
    public void WaitForNarrationThenNextStep()
    {
        StartCoroutine(WaitForNarrationThenNextStepCoroutine());
    }

    private IEnumerator WaitForNarrationThenNextStepCoroutine()
    {
        int token = subStepToken;

        yield return WaitForNarrationComplete();

        if (scenarioManager != null && IsProgressStillOwned(token, "WaitForNarrationThenNextStep"))
        {
            scenarioManager.NextSubStep();
        }
    }

    /// <summary>
    /// ★ 시작 홀드 완료 시 초급자용 2차 나래이션 재생
    /// 파일명 규칙: {원본클립명}_홀드후
    /// </summary>
    private void OnStartHoldCompleteForNarration()
    {
        // 현재 클립명이 없으면 스킵
        if (string.IsNullOrEmpty(currentVoiceClipName))
        {
            ChunaLogger.Log("[ConditionManager] 홀드 후 나래이션 스킵 - 현재 클립명 없음");
            return;
        }

        // 초급자 모드 + PlayHintAudio 활성화 시에만 재생
        if (DifficultyManager.Instance == null ||
            DifficultyManager.Instance.CurrentLevel != DifficultyLevel.Beginner ||
            !DifficultyManager.Instance.PlayHintAudio)
        {
            ChunaLogger.Log("[ConditionManager] 홀드 후 나래이션 스킵 - 초급자 모드 아니거나 힌트 비활성화");
            return;
        }

        // 홀드 후 나래이션 클립명 생성
        string afterHoldClipName = $"{currentVoiceClipName}_홀드후";

        // 클립 로드 시도
        AudioClip afterHoldClip = LoadNarrationClip(afterHoldClipName);

        if (afterHoldClip == null)
        {
            ChunaLogger.Log($"[ConditionManager] 홀드 후 나래이션 없음: {afterHoldClipName}");
            return;
        }

        // 나래이션 재생
        ChunaLogger.Log($"<color=yellow>[ConditionManager] ★ 홀드 후 나래이션 재생: {afterHoldClipName} ({afterHoldClip.length:F1}초)</color>");

        AudioSource targetSource = narrationAudioSource != null ? narrationAudioSource : audioSource;
        if (targetSource != null)
        {
            targetSource.clip = afterHoldClip;
            targetSource.Play();
        }
    }

    /// <summary>
    /// 수동 진행 처리 (토글 버튼 활성화 및 초기화)
    /// </summary>
    private void HandleManualProgress()
    {
        currentCondition = null;
        StopConditionCheck();
        eventSystem.RequestButtonStateUpdate(true);

        // 토글 버튼 초기화 (항상 off 상태로)
        if (guideUIController != null)
        {
            guideUIController.ResetStartToggle();
        }

        ChunaLogger.Log("[ConditionManager] '다음' 버튼 활성화 (수동 진행)");
    }

    /// <summary>
    /// 조건 체크 시작
    /// </summary>
    /// <summary>★계측: 폴링이 실제로 시작되는지. 이게 안 찍히면 20초 폴백도 안 도는 단계다.</summary>
    private void LogPollingStart(string where)
    {
        var dm = DifficultyManager.Instance;
        ChunaLogger.Log($"<color=lime>[ConditionManager] 조건 폴링 시작({where}) — 20초 타이머 가동" +
                        $" / 난이도={(dm != null ? dm.CurrentLevel.ToString() : "없음")}</color>");
    }

    private void StartConditionCheck()
    {
        StopConditionCheck();

        isCheckingCondition = true;
        checkCoroutine = StartCoroutine(ConditionCheckRoutine());

        ChunaLogger.Log($"[ConditionManager] 조건 체크 시작: {currentCondition?.GetConditionDescription()}");
    }

    /// <summary>
    /// 조건 체크 중단
    /// </summary>
    private void StopConditionCheck()
    {
        isCheckingCondition = false;

        if (checkCoroutine != null)
        {
            StopCoroutine(checkCoroutine);
            checkCoroutine = null;
        }
    }

    /// <summary>
    /// 조건 체크 루틴 (Quest 최적화: WaitForSeconds 캐싱, 20초 타임아웃 추가)
    /// </summary>
    private IEnumerator ConditionCheckRoutine()
    {
        float elapsedTime = 0f; // 경과 시간 추적
        bool timeoutTriggered = false; // 타임아웃 발생 여부

        while (isCheckingCondition && currentCondition != null)
        {
            // 조건 확인
            if (currentCondition.IsConditionMet())
            {
                ChunaLogger.Log($"[ConditionManager] 조건 만족: {currentCondition.GetConditionDescription()}");

                // 체크 중단
                isCheckingCondition = false;

                // 완료 처리
                yield return StartCoroutine(OnConditionCompleted());

                yield break;
            }

            // 경과 시간 증가
            elapsedTime += checkInterval;

            // 20초 타임아웃 체크
            if (!timeoutTriggered && elapsedTime >= progressTimeout)
            {
                timeoutTriggered = true;
                ChunaLogger.LogWarning($"<color=yellow>[ConditionManager] {progressTimeout}초 경과 - 진행 안됨 감지, 토글 버튼 활성화</color>");

                // 토글 버튼 활성화
                if (guideUIController != null)
                {
                    guideUIController.EnableStartToggle();
                }

                // 조건 체크는 계속 진행 (사용자가 동작을 완료할 수도 있음)
            }

            // 다음 체크까지 대기 (Quest 최적화: 캐시된 객체 사용)
            yield return cachedCheckInterval;
        }
    }

    /// <summary>
    /// 조건 완료 시 처리 (완료 사운드 + 피드백 + 다음 단계)
    /// ★ 분리 방식: guideContent 숨김 → StepFeedbackUI 표시 → 대기 → 복원 → 다음 단계
    /// </summary>
    private IEnumerator OnConditionCompleted()
    {
        // ★계측(2026-08-18): "조건은 성립했다고 로그에 뜨는데 진행을 안 한다"의 위치를 찾는다.
        //   완료 경로 각 지점을 무조건 찍는다 — 마지막으로 찍힌 줄이 끊긴 지점이다.
        ChunaLogger.Log("<color=lime>[완료추적] ① OnConditionCompleted 진입</color>");

        // 1. 완료 사운드 재생 (딩동)
        PlayCompletionSound();
        ChunaLogger.Log("<color=lime>[완료추적] ② 완료음 재생 통과</color>");

        // 현재 유사도 가져오기
        float currentSimilarity = GetCurrentSimilarity();
        ChunaLogger.Log($"<color=lime>[완료추적] ③ 유사도 산출 통과 ({currentSimilarity:P0})</color>");

        // ★ StepFeedbackUI가 null이면 다시 찾기 (첫 단계에서 못 찾은 경우 대비)
        if (stepFeedbackUI == null)
        {
            stepFeedbackUI = FindFirstObjectByType<StepFeedbackUI>(FindObjectsInactive.Include);
            if (stepFeedbackUI != null)
            {
                ChunaLogger.Log("<color=yellow>[ConditionManager] StepFeedbackUI 재탐색 성공</color>");
            }
        }

        // ★2026-08-12 규약 통일 — <b>판정이 끝나면 곧바로 다음 단계로 넘어간다.</b>
        //   진입 규약은 이미 '나레이션이 끝나야 판정이 시작된다'로 통일돼 있다
        //   (PlayNarrationThenStartHandPose — HandPose·cranial* 전부 같은 경로).
        //   그런데 출구에서만 피드백 3초 + 알림 2초 + 나레이션 대기가 겹쳐 있어서
        //   "완료했는데 왜 안 넘어가지?" 하고 이것저것 더 만지게 만들었다(사용자 지적).
        //   → 피드백·알림은 <b>띄우기만 하고 기다리지 않는다</b>. 숨기는 일은 별도 코루틴이 맡는다.
        ShowCompletionFeedbackNonBlocking(currentSimilarity);
        ChunaLogger.Log("<color=lime>[완료추적] ④ 피드백 표시 통과</color>");

        // 다음 SubStep으로 진행 — 대기 없음.
        if (scenarioManager != null)
        {
            ChunaLogger.Log("<color=lime>[완료추적] ⑤ NextSubStep 호출 직전</color>");
            scenarioManager.NextSubStep();
            ChunaLogger.Log("<color=lime>[완료추적] ⑥ NextSubStep 반환</color>");
        }
        else
        {
            ChunaLogger.LogError("[완료추적] ★scenarioManager 가 null — 진행 불가");
        }
        yield break;
    }

    /// <summary>완료 피드백을 띄우고 <b>스스로 사라지게</b> 한다 — 진행을 막지 않는다.</summary>
    private void ShowCompletionFeedbackNonBlocking(float similarity)
    {
        if (stepFeedbackUI != null)
        {
            stepFeedbackUI.gameObject.SetActive(true);
            stepFeedbackUI.ShowFeedback(similarity);
            ChunaLogger.Log($"<color=green>[ConditionManager] 피드백 표시(비대기): {similarity:P0}</color>");
            StartCoroutine(HideFeedbackLater());
        }
        else
        {
            ShowCompletionAlert();
            StartCoroutine(HideAlertLater());
        }
    }

    private IEnumerator HideFeedbackLater()
    {
        yield return new WaitForSeconds(feedbackVisibleSeconds);
        HideCompletionFeedbackNow();
    }

    /// <summary>완료 피드백·알림을 지금 즉시 지운다(다음 단계 시작 시 겹치지 않게).</summary>
    private void HideCompletionFeedbackNow()
    {
        if (stepFeedbackUI != null && stepFeedbackUI.gameObject.activeSelf)
        {
            stepFeedbackUI.Hide();
            stepFeedbackUI.gameObject.SetActive(false);
        }
        HideCompletionAlert();
    }

    private IEnumerator HideAlertLater()
    {
        yield return cachedCompletionDelay;
        HideCompletionAlert();
    }

    /// <summary>
    /// ★ 현재 유사도 가져오기
    /// </summary>
    private float GetCurrentSimilarity()
    {
        // ChunaPathEvaluator에서 최근 유사도 가져오기
        if (pathEvaluator != null)
        {
            // 좌우 손 유사도 가져오기
            var (leftSim, rightSim) = pathEvaluator.GetRealTimeSimilarityBoth();

            // 둘 다 0이 아닌 경우 평균, 하나만 있으면 그 값 사용
            if (leftSim > 0 && rightSim > 0)
                return (leftSim + rightSim) / 2f;
            else if (rightSim > 0)
                return rightSim;
            else if (leftSim > 0)
                return leftSim;
        }

        // 기본값 (데이터 없을 시)
        return 0.5f;
    }

    /// <summary>
    /// 완료 알림 없이 자동 진행 (CSV duration 전용)
    /// </summary>
    private IEnumerator AutoProgressWithoutAlert(int duration)
    {
        int token = subStepToken;

        // duration만큼 대기
        yield return new WaitForSeconds(duration);

        // ★ 나래이션이 아직 재생 중이면 완료까지 대기
        yield return WaitForNarrationComplete();

        // ★ AutoPlay(환자 애니메이션)가 아직 재생 중이면 완료까지 대기
        yield return WaitForAutoPlayComplete();

        // 완료 알림 없이 바로 다음 SubStep으로 진행
        if (scenarioManager != null && IsProgressStillOwned(token, $"AutoProgressWithoutAlert({duration}초)"))
        {
            ChunaLogger.Log($"[ConditionManager] {duration}초 경과 - 다음 단계로 자동 진행");
            scenarioManager.NextSubStep();
        }
    }

    /// <summary>
    /// 완료 알림 표시
    /// </summary>
    private void ShowCompletionAlert()
    {
        if (completionAlertPanel != null)
        {
            completionAlertPanel.SetActive(true);
        }

        if (completionAlertText != null)
        {
            completionAlertText.text = "✓ 완료!";
        }

        ChunaLogger.Log("[ConditionManager] 완료 알림 표시");
    }

    /// <summary>
    /// 완료 알림 숨김
    /// </summary>
    private void HideCompletionAlert()
    {
        if (completionAlertPanel != null)
        {
            completionAlertPanel.SetActive(false);
        }
    }

    /// <summary>
    /// 완료 사운드 재생
    /// </summary>
    private void PlayCompletionSound()
    {
        // ★씬에 completionSound·audioSource가 둘 다 비어 있어서 지금까지 완료음이 아예 안 났다(08-11 실측).
        //   배선을 기다리지 않고 코드에서 확보한다 — 단계가 끝났는지 소리로 알 수 있어야 한다는 요구.
        if (completionSound == null)
            completionSound = Resources.Load<AudioClip>("Audio/StepComplete");

        // ★볼륨 하한 — completionVolume이 씬에 0.7로 직렬화돼 있어 코드 기본값을 올려도 안 먹는다(08-18 실측).
        //   "확실히 인지되게 해 달라"는 요구라 하한을 걸고, 인스펙터에서 더 키우는 것은 그대로 반영한다.

        if (audioSource == null)
        {
            // 나레이션 소스를 재사용하면 PlayOneShot이라 말을 끊지는 않지만, 볼륨·믹서를 따로 두기 위해
            // 전용 소스를 하나 만들어 쓴다(없을 때만).
            audioSource = gameObject.GetComponent<AudioSource>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0f;   // 2D — 어느 방향을 보고 있어도 들려야 한다
            }
        }

        if (audioSource != null && completionSound != null)
            audioSource.PlayOneShot(completionSound, Mathf.Max(completionVolume, MinCompletionVolume));
        else
            ChunaLogger.LogWarning("[ConditionManager] 완료음을 재생할 수 없습니다 " +
                                   "(Resources/Audio/StepComplete.wav 확인).");
    }

    /// <summary>
    /// 조건 키 생성
    /// </summary>
    private string GetConditionKey(SubStepData subStep)
    {
        // Phase_Step_SubStep 형식으로 키 생성
        string phaseName = scenarioManager.CurrentPhase.phaseName;
        string stepName = scenarioManager.CurrentStep.stepName;
        int subStepNo = subStep.subStepNo;

        return $"{phaseName}_{stepName}_{subStepNo}";
    }

    // ========== Public API ==========

    /// <summary>
    /// 조건 등록
    /// ✅ ScenarioActionHandler가 handTrackingFileName을 감지하면 자동으로 호출함
    /// 수동 등록도 가능 (특수한 경우에만)
    /// </summary>
    public void RegisterCondition(string phaseName, string stepName, int subStepNo, IScenarioCondition condition)
    {
        string key = $"{phaseName}_{stepName}_{subStepNo}";

        if (conditionRegistry.ContainsKey(key))
        {
            // ★ 이전 조건의 이벤트 구독 해제 (메모리 누수 방지)
            DeactivateCondition(conditionRegistry[key]);
            ChunaLogger.LogWarning($"[ConditionManager] 이전 조건 비활성화 후 새 조건으로 교체: {key}");
            conditionRegistry[key] = condition;
        }
        else
        {
            conditionRegistry.Add(key, condition);
        }

        ChunaLogger.Log($"[ConditionManager] 조건 등록: {key} - {condition.GetConditionDescription()}");
    }

    /// <summary>
    /// 조건 등록 해제
    /// </summary>
    public void UnregisterCondition(string phaseName, string stepName, int subStepNo)
    {
        string key = $"{phaseName}_{stepName}_{subStepNo}";

        if (conditionRegistry.ContainsKey(key))
        {
            // ★ 조건의 이벤트 구독 해제 (메모리 누수 방지)
            DeactivateCondition(conditionRegistry[key]);
            conditionRegistry.Remove(key);
            ChunaLogger.Log($"[ConditionManager] 조건 등록 해제: {key}");
        }
    }

    /// <summary>
    /// 모든 조건 등록 해제
    /// </summary>
    public void ClearAllConditions()
    {
        // ★ 모든 조건의 이벤트 구독 해제 (메모리 누수 방지)
        foreach (var condition in conditionRegistry.Values)
        {
            DeactivateCondition(condition);
        }
        conditionRegistry.Clear();
        ChunaLogger.Log("[ConditionManager] 모든 조건 등록 해제");
    }

    /// <summary>
    /// ★ 조건 비활성화 헬퍼 (이벤트 구독 해제)
    /// </summary>
    private void DeactivateCondition(IScenarioCondition condition)
    {
        if (condition is CheckpointPoseCondition checkpointCondition)
        {
            checkpointCondition.Deactivate();
        }
    }

    /// <summary>
    /// 수동으로 현재 단계 완료 처리
    /// </summary>
    public void CompleteCurrentStep()
    {
        if (isCheckingCondition)
        {
            StopConditionCheck();
            StartCoroutine(OnConditionCompleted());
        }
    }

    /// <summary>
    /// 조건 체크 활성화 여부
    /// </summary>
    public bool IsCheckingCondition => isCheckingCondition;

    /// <summary>
    /// 시나리오별 나레이션 서브폴더 설정 (ScenarioBootstrapper에서 호출)
    /// </summary>
    public void SetNarrationScenarioFolder(string folder)
    {
        narrationScenarioFolder = folder ?? "";
        ChunaLogger.Log($"<color=cyan>[ConditionManager] 나레이션 시나리오 폴더 설정: '{narrationScenarioFolder}'</color>");
    }

    /// <summary>
    /// 외부에서 나레이션 재생 (완료 후 자동 진행 없음)
    /// </summary>
    public void PlayNarration(string clipName)
    {
        AudioClip clip = LoadNarrationClip(clipName);
        if (clip == null) return;

        AudioSource targetSource = narrationAudioSource != null ? narrationAudioSource : audioSource;
        if (targetSource != null)
        {
            targetSource.PlayOneShot(clip);
            ChunaLogger.Log($"[ConditionManager] 나레이션 재생 (OneShot): {clipName}");
        }
    }

    /// <summary>
    /// 외부에서 나레이션 재생 (완료 후 자동 진행)
    /// </summary>
    public void PlayNarrationWithProgress(SubStepData subStep)
    {
        HandleNarrationCondition(subStep);
    }
}

// ========== 조건 클래스들 ==========

/// <summary>
/// 시간 기반 조건 (N초 경과 시 완료)
/// 참고: CSV의 duration은 자동으로 처리되므로 수동 등록 시에만 사용
/// </summary>
public class TimeBasedCondition : IScenarioCondition
{
    private float startTime;
    private float requiredDuration;

    public TimeBasedCondition(float duration)
    {
        requiredDuration = duration;
        startTime = Time.time;
    }

    public bool IsConditionMet()
    {
        return Time.time - startTime >= requiredDuration;
    }

    public string GetConditionDescription()
    {
        return $"{requiredDuration}초 대기";
    }
}

/// <summary>
/// 버튼 클릭 조건
/// </summary>
public class ButtonClickCondition : IScenarioCondition
{
    private bool isClicked = false;

    public void OnButtonClick()
    {
        isClicked = true;
    }

    public bool IsConditionMet()
    {
        return isClicked;
    }

    public string GetConditionDescription()
    {
        return "버튼 클릭 대기";
    }

    public void Reset()
    {
        isClicked = false;
    }
}

/// <summary>
/// 위치 기반 조건 (특정 위치에 도달 시 완료)
/// </summary>
public class PositionBasedCondition : IScenarioCondition
{
    private Transform targetTransform;
    private Vector3 targetPosition;
    private float threshold;

    public PositionBasedCondition(Transform target, Vector3 position, float distanceThreshold = 0.1f)
    {
        targetTransform = target;
        targetPosition = position;
        threshold = distanceThreshold;
    }

    public bool IsConditionMet()
    {
        if (targetTransform == null) return false;

        float distance = Vector3.Distance(targetTransform.position, targetPosition);
        return distance <= threshold;
    }

    public string GetConditionDescription()
    {
        return $"목표 위치 도달 (거리: {threshold}m 이내)";
    }
}

/// <summary>
/// 커스텀 델리게이트 조건
/// </summary>
public class CustomCondition : IScenarioCondition
{
    private Func<bool> conditionFunc;
    private string description;

    public CustomCondition(Func<bool> condition, string desc = "커스텀 조건")
    {
        conditionFunc = condition;
        description = desc;
    }

    public bool IsConditionMet()
    {
        return conditionFunc != null && conditionFunc();
    }

    public string GetConditionDescription()
    {
        return description;
    }
}

/// <summary>
/// 체크포인트 기반 손 동작 조건 (ChunaPathEvaluator 사용)
/// ChunaPathEvaluatorBridge와 연동하여 체크포인트 통과 감지
/// </summary>
public class CheckpointPoseCondition : IScenarioCondition
{
    private ChunaPathEvaluatorBridge evaluatorBridge;
    private bool isCompleted = false;
    private string fileName;
    private float creationTime;  // ★ 생성 시간 기록
    private bool isActive = true;  // ★ 활성화 상태
    // ★0.15초로 낮췄다(2026-08-13). 원래 1초는 "직전 단계의 완료가 흘러 들어와 즉시 넘어가는 것"을
    //   막으려던 안전장치인데, 왕복을 빠르게 반복하는 단계에서는 <b>끝에 도달해도 1초를 기다려야</b>
    //   넘어가서 "끝에서 홀드가 걸린다"가 됐다(사용자 지적). 오탐 방지는 0.15초로도 충분하다.
    private const float MIN_ACTIVE_TIME = 0.15f;

    /// <summary>
    /// CheckpointPoseCondition 생성자
    /// </summary>
    public CheckpointPoseCondition(ChunaPathEvaluatorBridge bridge, string trackingFileName, ScenarioConditionManager conditionManager)
    {
        evaluatorBridge = bridge;
        fileName = trackingFileName;
        creationTime = Time.time;  // ★ 생성 시간 기록

        if (bridge != null)
        {
            // ★ 이전 이벤트 구독 해제 (혹시 남아있을 수 있음)
            bridge.OnSequenceCompleted -= OnSequenceCompleted;
            bridge.OnProgressThresholdReached -= OnProgressThresholdReached;

            // OnSequenceCompleted 이벤트 구독
            bridge.OnSequenceCompleted += OnSequenceCompleted;
            ChunaLogger.Log($"<color=cyan>[CheckpointPoseCondition] OnSequenceCompleted 이벤트 구독 성공: {trackingFileName}</color>");

            // OnProgressThresholdReached 이벤트 구독
            bridge.OnProgressThresholdReached += OnProgressThresholdReached;
            ChunaLogger.Log($"<color=cyan>[CheckpointPoseCondition] OnProgressThresholdReached 이벤트 구독 성공: {trackingFileName}</color>");
        }
        else
        {
            ChunaLogger.LogError("[CheckpointPoseCondition] ChunaPathEvaluatorBridge가 null입니다!");
        }
    }

    private void OnSequenceCompleted()
    {
        // ★ 비활성화 상태면 무시
        if (!isActive) return;

        // ★ 최소 활성화 시간 체크 (너무 빨리 완료되는 것 방지)
        float elapsed = Time.time - creationTime;
        if (elapsed < MIN_ACTIVE_TIME)
        {
            ChunaLogger.Log($"<color=yellow>[CheckpointPoseCondition] 최소 시간 미달로 완료 무시 ({elapsed:F2}s < {MIN_ACTIVE_TIME}s): {fileName}</color>");
            return;
        }

        isCompleted = true;
        ChunaLogger.Log($"<color=green>[CheckpointPoseCondition] 모든 체크포인트 통과 (경과: {elapsed:F1}s): {fileName}</color>");

        // ★ 이벤트 구독 해제
        Unsubscribe();
    }

    private void OnProgressThresholdReached()
    {
        // ★ 비활성화 상태면 무시
        if (!isActive) return;

        // ★ 최소 활성화 시간 체크 (너무 빨리 완료되는 것 방지)
        float elapsed = Time.time - creationTime;
        if (elapsed < MIN_ACTIVE_TIME)
        {
            ChunaLogger.Log($"<color=yellow>[CheckpointPoseCondition] 최소 시간 미달로 완료 무시 ({elapsed:F2}s < {MIN_ACTIVE_TIME}s): {fileName}</color>");
            return;
        }

        isCompleted = true;
        ChunaLogger.Log($"<color=green>[CheckpointPoseCondition] 진행률 목표 달성으로 완료 (경과: {elapsed:F1}s): {fileName}</color>");

        // ★ 이벤트 구독 해제
        Unsubscribe();
    }

    /// <summary>
    /// ★ 이벤트 구독 해제
    /// </summary>
    private void Unsubscribe()
    {
        if (evaluatorBridge != null)
        {
            evaluatorBridge.OnSequenceCompleted -= OnSequenceCompleted;
            evaluatorBridge.OnProgressThresholdReached -= OnProgressThresholdReached;
        }
    }

    public bool IsConditionMet()
    {
        return isCompleted;
    }

    public string GetConditionDescription()
    {
        return $"체크포인트 기반 추나 평가: {fileName}";
    }

    public void Reset()
    {
        isCompleted = false;
        creationTime = Time.time;  // ★ 리셋 시 시간도 갱신
    }

    /// <summary>
    /// ★ 조건 비활성화 및 이벤트 구독 해제
    /// </summary>
    public void Deactivate()
    {
        isActive = false;
        Unsubscribe();
        ChunaLogger.Log($"<color=orange>[CheckpointPoseCondition] 비활성화됨: {fileName}</color>");
    }
}