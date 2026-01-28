using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 연습 모드 전체 관리
/// 5단계 연습을 하드코딩으로 관리
/// </summary>
public class PracticeManager : MonoBehaviour
{
    [Header("=== UI 참조 ===")]
    [SerializeField] private PracticeUI practiceUI;

    [Header("=== Step 1: UI 옮기기 ===")]
    [Tooltip("그랩 가능한 UI 오브젝트")]
    [SerializeField] private Transform grabbableUI;
    [SerializeField] private Vector3 uiInitialPosition;
    [SerializeField] private Quaternion uiInitialRotation;
    [SerializeField] private Button practiceSelectButton;

    [Header("=== Step 2: 난이도 버튼 ===")]
    [SerializeField] private List<Button> difficultyButtons;
    [SerializeField] private Button practiceModeButton;

    [Header("=== Step 3: 패널 버튼 ===")]
    [SerializeField] private List<Button> panelButtons;
    private HashSet<Button> clickedPanelButtons = new HashSet<Button>();

    [Header("=== Step 4: 환자 위치 조정 ===")]
    [SerializeField] private Button settingsButton;
    [SerializeField] private Transform patientTransform;
    private Vector3 initialPatientPosition;
    private bool patientPositionChanged = false;

    [Header("=== Step 5: 가이드 핸드 ===")]
    [SerializeField] private Button guideHandToggleButton;
    [SerializeField] private string handDataPath = "PoseData/practice_hand";
    [Tooltip("가이드 핸드 표시 컴포넌트")]
    [SerializeField] private MonoBehaviour guideHandDisplay;

    [Header("=== 완료 후 ===")]
    [SerializeField] private Button mainMenuButton;
    [SerializeField] private Button startPracticeButton;

    [Header("=== 설정 ===")]
    [SerializeField] private float stepTransitionDelay = 1.5f;
    [SerializeField] private bool showDebugLogs = true;

    // 상태
    private int currentStep = 0;
    private int currentCount = 0;
    private bool isStepActive = false;

    // 상수
    private const int TOTAL_STEPS = 5;

    void Start()
    {
        Initialize();
        StartStep(0);
    }

    /// <summary>
    /// 초기화
    /// </summary>
    private void Initialize()
    {
        // 초기 위치 저장
        if (grabbableUI != null)
        {
            uiInitialPosition = grabbableUI.position;
            uiInitialRotation = grabbableUI.rotation;
        }

        if (patientTransform != null)
        {
            initialPatientPosition = patientTransform.position;
        }

        // 버튼 비활성화
        SetButtonActive(practiceSelectButton, false);
        SetButtonActive(practiceModeButton, false);
        SetButtonActive(mainMenuButton, false);
        SetButtonActive(startPracticeButton, false);

        // 버튼 이벤트 연결
        SetupButtonListeners();

        if (showDebugLogs)
            Debug.Log("[Practice] Initialized");
    }

    /// <summary>
    /// 버튼 리스너 설정
    /// </summary>
    private void SetupButtonListeners()
    {
        // Step 2: 난이도 버튼
        foreach (var btn in difficultyButtons)
        {
            if (btn != null)
                btn.onClick.AddListener(() => OnDifficultyButtonClicked());
        }

        // Step 3: 패널 버튼
        foreach (var btn in panelButtons)
        {
            if (btn != null)
            {
                Button capturedBtn = btn;
                btn.onClick.AddListener(() => OnPanelButtonClicked(capturedBtn));
            }
        }

        // Step 4: 설정 버튼
        if (settingsButton != null)
            settingsButton.onClick.AddListener(OnSettingsButtonClicked);

        // Step 5: 가이드 핸드 토글
        if (guideHandToggleButton != null)
            guideHandToggleButton.onClick.AddListener(OnGuideHandToggled);
    }

    #region Step Management

    /// <summary>
    /// 단계 시작
    /// </summary>
    private void StartStep(int step)
    {
        currentStep = step;
        currentCount = 0;
        isStepActive = true;

        if (showDebugLogs)
            Debug.Log($"[Practice] Starting Step {step + 1}");

        switch (step)
        {
            case 0:
                StartStep1_UIGrab();
                break;
            case 1:
                StartStep2_DifficultyButton();
                break;
            case 2:
                StartStep3_PanelButtons();
                break;
            case 3:
                StartStep4_PatientPosition();
                break;
            case 4:
                StartStep5_GuideHand();
                break;
            default:
                ShowComplete();
                break;
        }
    }

    /// <summary>
    /// 현재 단계 완료
    /// </summary>
    private void CompleteCurrentStep()
    {
        isStepActive = false;

        if (showDebugLogs)
            Debug.Log($"[Practice] Step {currentStep + 1} Completed!");

        // 단계별 완료 처리
        switch (currentStep)
        {
            case 0:
                OnStep1Complete();
                break;
            case 1:
                OnStep2Complete();
                break;
            case 2:
                OnStep3Complete();
                break;
            case 3:
                OnStep4Complete();
                break;
            case 4:
                OnStep5Complete();
                break;
        }

        // 다음 단계로
        StartCoroutine(TransitionToNextStep());
    }

    private IEnumerator TransitionToNextStep()
    {
        practiceUI?.ShowStepComplete($"Step {currentStep + 1} 완료!");

        yield return new WaitForSeconds(stepTransitionDelay);

        StartStep(currentStep + 1);
    }

    /// <summary>
    /// 동작 횟수 증가 (외부에서 호출)
    /// </summary>
    public void IncrementCount()
    {
        if (!isStepActive) return;

        currentCount++;

        int requiredCount = GetRequiredCountForStep(currentStep);
        practiceUI?.UpdateProgress(currentCount, requiredCount);

        if (showDebugLogs)
            Debug.Log($"[Practice] Step {currentStep + 1}: {currentCount}/{requiredCount}");

        if (requiredCount > 0 && currentCount >= requiredCount)
        {
            CompleteCurrentStep();
        }
    }

    private int GetRequiredCountForStep(int step)
    {
        switch (step)
        {
            case 0: return 3; // UI 옮기기
            case 1: return 3; // 난이도 버튼
            case 2: return 0; // 패널 버튼 (모두 클릭 시 완료)
            case 3: return 1; // 환자 위치
            case 4: return 3; // 가이드 핸드
            default: return 0;
        }
    }

    #endregion

    #region Step 1: UI 옮기기

    private void StartStep1_UIGrab()
    {
        practiceUI?.ShowStep(
            "Step 1: UI 옮기기",
            "UI를 잡아서 원하는 위치로 옮겨보세요",
            0, 3
        );

        // UI 위치 초기화
        ResetUIPosition();
    }

    /// <summary>
    /// UI Grab 완료 시 호출 (OVRGrabbable 이벤트에서 호출)
    /// </summary>
    public void OnUIGrabReleased()
    {
        if (currentStep != 0 || !isStepActive) return;
        IncrementCount();
    }

    private void OnStep1Complete()
    {
        ResetUIPosition();
        SetButtonActive(practiceSelectButton, true);
    }

    private void ResetUIPosition()
    {
        if (grabbableUI != null)
        {
            grabbableUI.position = uiInitialPosition;
            grabbableUI.rotation = uiInitialRotation;
        }
    }

    #endregion

    #region Step 2: 난이도 버튼

    private void StartStep2_DifficultyButton()
    {
        practiceUI?.ShowStep(
            "Step 2: 난이도 변경",
            "난이도 선택 버튼을 눌러 난이도를 변경해보세요",
            0, 3
        );
    }

    private void OnDifficultyButtonClicked()
    {
        if (currentStep != 1 || !isStepActive) return;
        IncrementCount();
    }

    private void OnStep2Complete()
    {
        SetButtonActive(practiceModeButton, true);
    }

    #endregion

    #region Step 3: 패널 버튼

    private void StartStep3_PanelButtons()
    {
        clickedPanelButtons.Clear();

        practiceUI?.ShowStep(
            "Step 3: 패널 버튼",
            "좌측 안내 패널의 버튼들을 모두 눌러보세요",
            0, panelButtons.Count
        );
    }

    private void OnPanelButtonClicked(Button btn)
    {
        if (currentStep != 2 || !isStepActive) return;

        if (!clickedPanelButtons.Contains(btn))
        {
            clickedPanelButtons.Add(btn);
            practiceUI?.UpdateProgress(clickedPanelButtons.Count, panelButtons.Count);

            if (showDebugLogs)
                Debug.Log($"[Practice] Panel button clicked: {clickedPanelButtons.Count}/{panelButtons.Count}");

            // 모든 버튼 클릭 완료
            if (clickedPanelButtons.Count >= panelButtons.Count)
            {
                CompleteCurrentStep();
            }
        }
    }

    private void OnStep3Complete()
    {
        // 특별한 처리 없음
    }

    #endregion

    #region Step 4: 환자 위치 조정

    private void StartStep4_PatientPosition()
    {
        patientPositionChanged = false;

        practiceUI?.ShowStep(
            "Step 4: 환자 위치 조정",
            "설정 버튼을 눌러 환자 위치를 조정해보세요",
            0, 1
        );
    }

    private void OnSettingsButtonClicked()
    {
        if (currentStep != 3 || !isStepActive) return;

        // 설정 버튼 클릭 후 환자 위치 변경 감지 시작
        StartCoroutine(WaitForPatientPositionChange());
    }

    private IEnumerator WaitForPatientPositionChange()
    {
        if (patientTransform == null) yield break;

        Vector3 startPosition = patientTransform.position;

        // 위치 변경 대기 (최대 30초)
        float timeout = 30f;
        float elapsed = 0f;

        while (elapsed < timeout)
        {
            if (Vector3.Distance(patientTransform.position, startPosition) > 0.01f)
            {
                patientPositionChanged = true;
                IncrementCount();
                yield break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    /// <summary>
    /// 환자 위치 변경 시 외부에서 호출
    /// </summary>
    public void OnPatientPositionChanged()
    {
        if (currentStep != 3 || !isStepActive || patientPositionChanged) return;

        patientPositionChanged = true;
        IncrementCount();
    }

    private void OnStep4Complete()
    {
        // 환자 위치 초기화 (선택)
        // if (patientTransform != null)
        //     patientTransform.position = initialPatientPosition;
    }

    #endregion

    #region Step 5: 가이드 핸드

    private void StartStep5_GuideHand()
    {
        practiceUI?.ShowStep(
            "Step 5: 가이드 핸드",
            "가이드 핸드를 활성화하고 측굴 동작을 따라해보세요",
            0, 3
        );

        // 가이드 핸드 데이터 로드
        LoadGuideHandData();
    }

    private void LoadGuideHandData()
    {
        if (guideHandDisplay == null) return;

        // ReferenceHandDisplay 또는 ChunaPathEvaluator의 LoadHandData 메서드 호출
        var loadMethod = guideHandDisplay.GetType().GetMethod("LoadHandData");
        if (loadMethod != null)
        {
            loadMethod.Invoke(guideHandDisplay, new object[] { handDataPath });
        }

        if (showDebugLogs)
            Debug.Log($"[Practice] Guide hand data loaded: {handDataPath}");
    }

    private void OnGuideHandToggled()
    {
        if (currentStep != 4 || !isStepActive) return;

        // 토글 후 측굴 동작 대기
        StartCoroutine(WaitForLateralFlexion());
    }

    private IEnumerator WaitForLateralFlexion()
    {
        // 측굴 동작 감지 (간단히 시간 대기로 대체, 실제로는 각도 변화 감지)
        yield return new WaitForSeconds(2f);

        IncrementCount();
    }

    /// <summary>
    /// 측굴 동작 완료 시 외부에서 호출
    /// </summary>
    public void OnLateralFlexionCompleted()
    {
        if (currentStep != 4 || !isStepActive) return;
        IncrementCount();
    }

    private void OnStep5Complete()
    {
        // 가이드 핸드 비활성화
        if (guideHandDisplay != null)
        {
            var setVisibleMethod = guideHandDisplay.GetType().GetMethod("SetVisible");
            if (setVisibleMethod != null)
            {
                setVisibleMethod.Invoke(guideHandDisplay, new object[] { false });
            }
        }
    }

    #endregion

    #region Complete

    private void ShowComplete()
    {
        isStepActive = false;

        practiceUI?.ShowComplete(
            "연습 완료!",
            "모든 연습을 완료했습니다.\n이제 실습을 시작해보세요!"
        );

        SetButtonActive(mainMenuButton, true);
        SetButtonActive(startPracticeButton, true);

        if (showDebugLogs)
            Debug.Log("[Practice] All steps completed!");
    }

    #endregion

    #region Utility

    private void SetButtonActive(Button btn, bool active)
    {
        if (btn != null)
        {
            btn.interactable = active;
        }
    }

    /// <summary>
    /// 연습 재시작
    /// </summary>
    public void RestartPractice()
    {
        StopAllCoroutines();
        Initialize();
        StartStep(0);
    }

    /// <summary>
    /// 특정 단계로 이동 (디버그용)
    /// </summary>
    public void JumpToStep(int step)
    {
        if (step >= 0 && step < TOTAL_STEPS)
        {
            StopAllCoroutines();
            StartStep(step);
        }
    }

    #endregion
}
