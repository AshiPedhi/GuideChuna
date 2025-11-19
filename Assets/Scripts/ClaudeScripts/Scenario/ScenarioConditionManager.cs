using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

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
/// - conditionType="PatientAnimation": 환자 애니메이션 완료 대기 (미구현)
/// - conditionType="Narration": 나레이션 완료 대기 (미구현)
/// - conditionType="None" 또는 빈칸: duration > 0이면 Duration, 아니면 Manual
/// </summary>
public class ScenarioConditionManager : MonoBehaviour
{
    [Header("=== 조건 체크 설정 ===")]
    [Tooltip("조건 체크 간격 (초)")]
    [SerializeField] private float checkInterval = 0.5f;

    [Tooltip("완료 후 다음 단계까지 딜레이 (초)")]
    [SerializeField] private float completionDelay = 2f;

    [Header("=== 완료 알림 UI ===")]
    [SerializeField] private GameObject completionAlertPanel;
    [SerializeField] private TMPro.TextMeshProUGUI completionAlertText;

    [Header("=== 사운드 (선택사항) ===")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip completionSound;

    // 현재 조건
    private IScenarioCondition currentCondition;
    private bool isCheckingCondition = false;
    private Coroutine checkCoroutine;

    // 이벤트 시스템
    private ScenarioEventSystem eventSystem;
    private ScenarioManager scenarioManager;

    // 조건 레지스트리 (SubStep별로 조건을 등록)
    private Dictionary<string, IScenarioCondition> conditionRegistry = new Dictionary<string, IScenarioCondition>();

    void Awake()
    {
        eventSystem = ScenarioEventSystem.Instance;
        scenarioManager = FindObjectOfType<ScenarioManager>();

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
    }

    void OnDisable()
    {
        // 이벤트 구독 해제
        eventSystem.OnSubStepStarted -= OnSubStepStarted;

        // 진행 중인 체크 중단
        StopConditionCheck();
    }

    /// <summary>
    /// SubStep 시작 시 호출 - CSV 데이터 기반 자동 조건 처리
    /// ✅ conditionType 기반 자동 조건 등록
    /// </summary>
    private void OnSubStepStarted(SubStepData subStep)
    {
        Debug.Log($"<color=cyan>[ConditionManager] ===== OnSubStepStarted 호출 =====</color>");
        Debug.Log($"[ConditionManager] Phase: {scenarioManager.CurrentPhase.phaseName}, Step: {scenarioManager.CurrentStep.stepName}, SubStep: {subStep.subStepNo}");
        Debug.Log($"[ConditionManager] Duration: {subStep.duration}초");
        Debug.Log($"[ConditionManager] ConditionType: {subStep.conditionType}");
        Debug.Log($"[ConditionManager] HandTracking: {(string.IsNullOrEmpty(subStep.handTrackingFileName) ? "(없음)" : subStep.handTrackingFileName)}");
        Debug.Log($"[ConditionManager] 가이드 스텝: {scenarioManager.CurrentStep.IsGuideStep()}");

        // 가이드 스텝(Step번호 0)은 항상 토글로 수동 진행
        if (scenarioManager.CurrentStep.IsGuideStep())
        {
            Debug.Log("[ConditionManager] 가이드 스텝 - 토글로 수동 진행");
            currentCondition = null;
            StopConditionCheck();
            eventSystem.RequestButtonStateUpdate(false);
            return;
        }

        // ✅ conditionType에 따른 조건 처리
        ProcessConditionByType(subStep);
    }

    /// <summary>
    /// 조건 타입에 따라 적절한 조건 처리
    /// </summary>
    private void ProcessConditionByType(SubStepData subStep)
    {
        string conditionKey = GetConditionKey(subStep);

        // conditionType이 명시되어 있으면 우선 사용
        string conditionType = string.IsNullOrEmpty(subStep.conditionType) ? "None" : subStep.conditionType;

        // conditionType이 None이고 handTrackingFileName이 있으면 HandPose로 자동 설정
        if (conditionType == "None" && !string.IsNullOrEmpty(subStep.handTrackingFileName))
        {
            conditionType = "HandPose";
        }

        Debug.Log($"<color=yellow>[ConditionManager] 조건 타입 처리: {conditionType}</color>");

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
                    Debug.Log("[ConditionManager] HandPose 조건 - 자동 진행 (조건 대기)");
                }
                else
                {
                    Debug.LogWarning($"[ConditionManager] HandPose 조건이 등록되지 않았습니다. 시간 기반으로 전환합니다.");
                    HandleDurationOrManual(subStep);
                }
                break;

            case "PatientAnimation":
                Debug.LogWarning("[ConditionManager] PatientAnimation 조건은 아직 구현되지 않았습니다.");
                HandleDurationOrManual(subStep);
                break;

            case "Narration":
                Debug.LogWarning("[ConditionManager] Narration 조건은 아직 구현되지 않았습니다.");
                HandleDurationOrManual(subStep);
                break;

            case "Duration":
                // 명시적으로 Duration 사용
                if (subStep.duration > 0)
                {
                    Debug.Log($"[ConditionManager] Duration 조건 - {subStep.duration}초 후 자동 진행");
                    currentCondition = null;
                    StopConditionCheck();
                    eventSystem.RequestButtonStateUpdate(false);
                    StartCoroutine(AutoProgressWithoutAlert(subStep.duration));
                }
                else
                {
                    Debug.LogWarning("[ConditionManager] Duration이 0입니다. 수동 진행으로 전환합니다.");
                    HandleManualProgress();
                }
                break;

            case "Manual":
                // 명시적으로 수동 진행
                Debug.Log("[ConditionManager] Manual 조건 - 토글로 수동 진행");
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
                    Debug.Log("[ConditionManager] 등록된 조건 발견 - 자동 진행 (조건 대기)");
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
            Debug.Log($"[ConditionManager] Duration={subStep.duration}초 - 자동 진행");
            currentCondition = null;
            StopConditionCheck();
            eventSystem.RequestButtonStateUpdate(false);
            StartCoroutine(AutoProgressWithoutAlert(subStep.duration));
        }
        else
        {
            Debug.Log("[ConditionManager] Duration 없음 - 토글로 수동 진행");
            HandleManualProgress();
        }
    }

    /// <summary>
    /// 수동 진행 처리
    /// </summary>
    private void HandleManualProgress()
    {
        currentCondition = null;
        StopConditionCheck();
        eventSystem.RequestButtonStateUpdate(true);
        Debug.Log("[ConditionManager] '다음' 버튼 활성화 (수동 진행)");
    }

    /// <summary>
    /// 조건 체크 시작
    /// </summary>
    private void StartConditionCheck()
    {
        StopConditionCheck();

        isCheckingCondition = true;
        checkCoroutine = StartCoroutine(ConditionCheckRoutine());

        Debug.Log($"[ConditionManager] 조건 체크 시작: {currentCondition?.GetConditionDescription()}");
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
    /// 조건 체크 루틴
    /// </summary>
    private IEnumerator ConditionCheckRoutine()
    {
        while (isCheckingCondition && currentCondition != null)
        {
            // 조건 확인
            if (currentCondition.IsConditionMet())
            {
                Debug.Log($"[ConditionManager] 조건 만족: {currentCondition.GetConditionDescription()}");

                // 체크 중단
                isCheckingCondition = false;

                // 완료 처리
                yield return StartCoroutine(OnConditionCompleted());

                yield break;
            }

            // 다음 체크까지 대기
            yield return new WaitForSeconds(checkInterval);
        }
    }

    /// <summary>
    /// 조건 완료 시 처리 (완료 알림 + 딜레이)
    /// HandPose 조건 등 등록된 조건에서 사용
    /// </summary>
    private IEnumerator OnConditionCompleted()
    {
        // 완료 알림 표시
        ShowCompletionAlert();

        // 완료 사운드 재생
        PlayCompletionSound();

        // 딜레이
        yield return new WaitForSeconds(completionDelay);

        // 완료 알림 숨김
        HideCompletionAlert();

        // 다음 SubStep으로 진행
        if (scenarioManager != null)
        {
            scenarioManager.NextSubStep();
        }
    }

    /// <summary>
    /// 완료 알림 없이 자동 진행 (CSV duration 전용)
    /// </summary>
    private IEnumerator AutoProgressWithoutAlert(int duration)
    {
        // duration만큼 대기
        yield return new WaitForSeconds(duration);

        // 완료 알림 없이 바로 다음 SubStep으로 진행
        if (scenarioManager != null)
        {
            Debug.Log($"[ConditionManager] {duration}초 경과 - 다음 단계로 자동 진행");
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

        Debug.Log("[ConditionManager] 완료 알림 표시");
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
        if (audioSource != null && completionSound != null)
        {
            audioSource.PlayOneShot(completionSound);
        }
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
            Debug.LogWarning($"[ConditionManager] 조건이 이미 등록되어 있습니다: {key}");
            conditionRegistry[key] = condition;
        }
        else
        {
            conditionRegistry.Add(key, condition);
        }

        Debug.Log($"[ConditionManager] 조건 등록: {key} - {condition.GetConditionDescription()}");
    }

    /// <summary>
    /// 조건 등록 해제
    /// </summary>
    public void UnregisterCondition(string phaseName, string stepName, int subStepNo)
    {
        string key = $"{phaseName}_{stepName}_{subStepNo}";

        if (conditionRegistry.ContainsKey(key))
        {
            conditionRegistry.Remove(key);
            Debug.Log($"[ConditionManager] 조건 등록 해제: {key}");
        }
    }

    /// <summary>
    /// 모든 조건 등록 해제
    /// </summary>
    public void ClearAllConditions()
    {
        conditionRegistry.Clear();
        Debug.Log("[ConditionManager] 모든 조건 등록 해제");
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
/// 손 동작 트래킹 조건
/// HandPoseTrainingControllerBridge와 연동하여 사용자 손 동작 완료 감지
/// </summary>
public class HandPoseCondition : IScenarioCondition
{
    private HandPoseTrainingControllerBridge eventBridge;
    private bool isCompleted = false;
    private string fileName;

    /// <summary>
    /// HandPoseCondition 생성자
    /// </summary>
    public HandPoseCondition(HandPoseTrainingControllerBridge bridge, string trackingFileName, ScenarioConditionManager conditionManager)
    {
        eventBridge = bridge;
        fileName = trackingFileName;

        if (bridge != null)
        {
            // OnSequenceCompleted 이벤트 구독
            bridge.OnSequenceCompleted += OnSequenceCompleted;
            Debug.Log($"<color=cyan>[HandPoseCondition] OnSequenceCompleted 이벤트 구독 성공: {trackingFileName}</color>");

            // OnProgressThresholdReached 이벤트 구독
            bridge.OnProgressThresholdReached += OnProgressThresholdReached;
            Debug.Log($"<color=cyan>[HandPoseCondition] OnProgressThresholdReached 이벤트 구독 성공: {trackingFileName}</color>");
        }
        else
        {
            Debug.LogError("[HandPoseCondition] HandPoseTrainingControllerBridge가 null입니다!");
        }
    }

    private void OnSequenceCompleted()
    {
        isCompleted = true;
        Debug.Log($"<color=green>[HandPoseCondition] 전체 시퀀스 완료: {fileName}</color>");
    }

    private void OnProgressThresholdReached()
    {
        isCompleted = true;
        Debug.Log($"<color=green>[HandPoseCondition] 진행률 목표 달성으로 완료: {fileName}</color>");
    }

    public bool IsConditionMet()
    {
        return isCompleted;
    }

    public string GetConditionDescription()
    {
        return $"손 동작 트래킹: {fileName}";
    }

    public void Reset()
    {
        isCompleted = false;
    }
}