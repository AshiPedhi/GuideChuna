using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System;
using TMPro;

/// <summary>
/// 연습 모드 관리자 (튜토리얼)
/// 모든 토글을 순서대로 점멸시켜 사용자가 따라하도록 유도
///
/// 진행 순서:
/// 1. UI 잡고 옮기기 (3회)
/// 2. 난이도 토글 3개 아무거나 클릭 → 실습모드 토글
/// 3. 콘텐츠 토글 (비디오→결과→골격)
/// 4. 설정 토글 → 자동조절 → 환자위치조절
/// 5. 환자 위치 조정 → 설정 닫기 → 시작 토글
/// 6. 핸드 가이드 홀드 연습 (3사이클)
/// 7. 나가기(메인메뉴) 토글 → 완료
/// </summary>
public class PracticeManager : MonoBehaviour
{
    /// <summary>
    /// 토글과 하이라이트 테두리를 함께 관리하는 구조체
    /// </summary>
    [Serializable]
    public class ToggleHighlightPair
    {
        [Tooltip("토글 컴포넌트")]
        public Toggle toggle;
        [Tooltip("하이라이트 테두리 이미지 (별도 오브젝트)")]
        public GameObject highlightBorder;
    }

    /// <summary>
    /// 스텝별 나레이션과 텍스트를 관리하는 구조체
    /// </summary>
    [Serializable]
    public class StepNarration
    {
        [Tooltip("스텝 이름 (표시용)")]
        public string stepName;
        [Tooltip("나레이션 오디오 클립")]
        public AudioClip audioClip;
        [Tooltip("화면에 표시할 나레이션 텍스트")]
        [TextArea(2, 5)]
        public string displayText;
    }

    [Header("=== Step 1: UI 옮기기 ===")]
    [Tooltip("잡아서 옮길 UI (정보패널Root 등)")]
    [SerializeField] private Transform grabbableUI;
    private Vector3 uiInitialPosition;
    private Quaternion uiInitialRotation;

    [Header("=== 가이드 패널 (직접 할당) ===")]
    [Tooltip("가이드 패널 GameObject - Inspector에서 직접 할당")]
    [SerializeField] private GameObject guidePanel;

    [Header("=== Step 2: 난이도 토글 (순서 무관, 3개 모두 클릭) ===")]
    [SerializeField] private ToggleHighlightPair beginnerToggle;      // 초급
    [SerializeField] private ToggleHighlightPair intermediateToggle;  // 중급
    [SerializeField] private ToggleHighlightPair advancedToggle;      // 상급
    [SerializeField] private ToggleHighlightPair practiceToggle;      // 실습모드

    [Header("=== Step 3: 콘텐츠 토글 (비디오→결과→골격 순서) ===")]
    [SerializeField] private ToggleHighlightPair expertVideoToggle;   // 전문가 영상
    [SerializeField] private ToggleHighlightPair resultToggle;        // 수행결과
    [SerializeField] private ToggleHighlightPair skeletonToggle;      // 근골격

    [Header("=== Step 4: 설정 패널 토글 ===")]
    [SerializeField] private ToggleHighlightPair settingsToggle;      // 설정
    [SerializeField] private ToggleHighlightPair autoAdjustToggle;    // 자동조절
    [SerializeField] private ToggleHighlightPair patientPositionToggle; // 환자위치조절

    [Header("=== Step 5: 시작 ===")]
    [SerializeField] private ToggleHighlightPair startToggle;         // 시작

    [Header("=== Step 6: 홀드 연습 ===")]
    [Tooltip("ChunaPathEvaluator (홀드 감지용)")]
    [SerializeField] private ChunaPathEvaluator chunaPathEvaluator;
    [Tooltip("환자 Transform (위치 변경 감지용)")]
    [SerializeField] private Transform patientTransform;

    [Header("=== Step 7: 나가기 ===")]
    [SerializeField] private ToggleHighlightPair mainMenuToggle;      // 메인메뉴
    [Tooltip("완료 팝업")]
    [SerializeField] private ExitPopupController exitPopupController;

    [Header("=== 하이라이트 설정 ===")]
    [SerializeField] private float blinkInterval = 0.4f;
    [SerializeField] private Color highlightColor = new Color(1f, 0.8f, 0.2f, 1f);

    [Header("=== 설정 ===")]
    [SerializeField] private float stepTransitionDelay = 1.0f;
    [SerializeField] private bool showDebugLogs = true;

    [Header("=== 나레이션 시스템 ===")]
    [Tooltip("나레이션 재생용 AudioSource")]
    [SerializeField] private AudioSource narrationAudioSource;
    [Tooltip("나레이션 텍스트 표시용 TextMeshProUGUI")]
    [SerializeField] private TextMeshProUGUI narrationText;
    [Tooltip("반복 횟수 표시용 TextMeshProUGUI (예: 1/3)")]
    [SerializeField] private TextMeshProUGUI countText;
    [Tooltip("각 스텝별 나레이션 설정 (총 7개 스텝)")]
    [SerializeField] private StepNarration[] stepNarrations = new StepNarration[7];

    // 상태
    private int currentStep = 0;
    private int currentCount = 0;
    private bool isStepActive = false;

    // 순차 토글 하이라이트 (Step 3, 4, 5, 7용)
    private List<ToggleHighlightPair> sequentialToggles = new List<ToggleHighlightPair>();
    private int currentToggleIndex = 0;
    private HashSet<Toggle> clickedToggles = new HashSet<Toggle>();

    // Step 2: 난이도 토글 (순서 무관)
    private List<ToggleHighlightPair> difficultyToggles = new List<ToggleHighlightPair>();
    private HashSet<Toggle> clickedDifficultyToggles = new HashSet<Toggle>();

    // 모든 토글 리스트 (interactable 제어용)
    private List<ToggleHighlightPair> allToggles = new List<ToggleHighlightPair>();

    // 점멸 코루틴 관리
    private Dictionary<GameObject, Coroutine> blinkCoroutines = new Dictionary<GameObject, Coroutine>();

    // 홀드 연습
    private bool isWaitingForHold = false;

    // 상수
    private const int HOLD_REQUIRED_COUNT = 3;
    private const int UI_GRAB_REQUIRED = 3;
    private const int DIFFICULTY_REQUIRED_COUNT = 3;

    void Start()
    {
        StartCoroutine(InitializeWithDelay());
    }

    /// <summary>
    /// InfoPanelController 초기화 후 실행되도록 딜레이
    /// </summary>
    private IEnumerator InitializeWithDelay()
    {
        // InfoPanelController.Start() 후에 실행되도록 대기
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();

        Initialize();
    }

    private void Initialize()
    {
        // 모든 토글 리스트 구성
        BuildAllTogglesList();

        // 충돌 컴포넌트 비활성화
        DisableConflictingComponents();

        // 초기 위치 저장
        if (grabbableUI != null)
        {
            uiInitialPosition = grabbableUI.position;
            uiInitialRotation = grabbableUI.rotation;
        }

        // ★ 가이드 패널 활성화 (InfoPanelController가 끈 후에 다시 켜기)
        ShowGuidePanel();

        // 모든 하이라이트 테두리 숨김
        HideAllHighlights();

        // ★ 모든 토글 초기 비활성화 (메인메뉴 제외)
        DisableAllTogglesExceptMainMenuAndDifficulty();

        // 토글 이벤트 리스너 설정
        SetupAllToggleListeners();

        // 연습 시작
        StartStep(0);

        if (showDebugLogs)
            Debug.Log("[Practice] Initialized - 연습 모드 시작");
    }

    /// <summary>
    /// 모든 토글 리스트 구성
    /// </summary>
    private void BuildAllTogglesList()
    {
        allToggles.Clear();
        difficultyToggles.Clear();

        // 난이도 (순서 무관 그룹)
        if (IsValidPair(beginnerToggle)) { allToggles.Add(beginnerToggle); difficultyToggles.Add(beginnerToggle); }
        if (IsValidPair(intermediateToggle)) { allToggles.Add(intermediateToggle); difficultyToggles.Add(intermediateToggle); }
        if (IsValidPair(advancedToggle)) { allToggles.Add(advancedToggle); difficultyToggles.Add(advancedToggle); }

        // 모드
        if (IsValidPair(practiceToggle)) allToggles.Add(practiceToggle);

        // 콘텐츠
        if (IsValidPair(expertVideoToggle)) allToggles.Add(expertVideoToggle);
        if (IsValidPair(resultToggle)) allToggles.Add(resultToggle);
        if (IsValidPair(skeletonToggle)) allToggles.Add(skeletonToggle);

        // 설정
        if (IsValidPair(settingsToggle)) allToggles.Add(settingsToggle);
        if (IsValidPair(autoAdjustToggle)) allToggles.Add(autoAdjustToggle);
        if (IsValidPair(patientPositionToggle)) allToggles.Add(patientPositionToggle);

        // 시작
        if (IsValidPair(startToggle)) allToggles.Add(startToggle);

        // 메인메뉴
        if (IsValidPair(mainMenuToggle)) allToggles.Add(mainMenuToggle);

        if (showDebugLogs)
            Debug.Log($"[Practice] 총 {allToggles.Count}개 토글 등록됨, 난이도 {difficultyToggles.Count}개");
    }

    private bool IsValidPair(ToggleHighlightPair pair)
    {
        return pair != null && pair.toggle != null;
    }

    /// <summary>
    /// 가이드 패널 표시
    /// </summary>
    private void ShowGuidePanel()
    {
        if (guidePanel != null)
        {
            guidePanel.SetActive(true);
            if (showDebugLogs)
                Debug.Log($"[Practice] 가이드 패널 표시됨: {guidePanel.name}");
        }
        else
        {
            Debug.LogError("[Practice] ⚠ 가이드 패널이 할당되지 않았습니다! Inspector에서 guidePanel을 할당해주세요.");
        }
    }

    /// <summary>
    /// 모든 하이라이트 테두리 숨김 및 점멸 중지
    /// </summary>
    private void HideAllHighlights()
    {
        // 모든 점멸 코루틴 중지
        StopAllBlinkCoroutines();

        foreach (var pair in allToggles)
        {
            if (pair.highlightBorder != null)
                pair.highlightBorder.SetActive(false);
        }
    }

    /// <summary>
    /// 모든 점멸 코루틴 중지
    /// </summary>
    private void StopAllBlinkCoroutines()
    {
        foreach (var kvp in blinkCoroutines)
        {
            if (kvp.Value != null)
                StopCoroutine(kvp.Value);
        }
        blinkCoroutines.Clear();
    }

    /// <summary>
    /// 특정 토글의 하이라이트 점멸 시작
    /// </summary>
    private void ShowHighlight(ToggleHighlightPair pair)
    {
        if (pair == null || pair.highlightBorder == null) return;

        // 기존 점멸 중지
        StopHighlightBlink(pair);

        // 색상 적용
        var image = pair.highlightBorder.GetComponent<Image>();
        if (image != null)
            image.color = highlightColor;

        // 점멸 시작
        var coroutine = StartCoroutine(BlinkHighlight(pair.highlightBorder));
        blinkCoroutines[pair.highlightBorder] = coroutine;

        if (showDebugLogs)
            Debug.Log($"[Practice] 하이라이트 점멸 시작: {pair.toggle.name}");
    }

    /// <summary>
    /// 하이라이트 점멸 코루틴
    /// </summary>
    private IEnumerator BlinkHighlight(GameObject border)
    {
        while (true)
        {
            border.SetActive(true);
            yield return new WaitForSeconds(blinkInterval);
            border.SetActive(false);
            yield return new WaitForSeconds(blinkInterval);
        }
    }

    /// <summary>
    /// 특정 토글의 하이라이트 점멸 중지 및 숨김
    /// </summary>
    private void HideHighlight(ToggleHighlightPair pair)
    {
        if (pair == null || pair.highlightBorder == null) return;

        StopHighlightBlink(pair);
        pair.highlightBorder.SetActive(false);
    }

    /// <summary>
    /// 특정 하이라이트의 점멸 코루틴 중지
    /// </summary>
    private void StopHighlightBlink(ToggleHighlightPair pair)
    {
        if (pair.highlightBorder != null && blinkCoroutines.ContainsKey(pair.highlightBorder))
        {
            if (blinkCoroutines[pair.highlightBorder] != null)
                StopCoroutine(blinkCoroutines[pair.highlightBorder]);
            blinkCoroutines.Remove(pair.highlightBorder);
        }
    }

    /// <summary>
    /// 모든 토글에 이벤트 리스너 설정
    /// </summary>
    private void SetupAllToggleListeners()
    {
        foreach (var pair in allToggles)
        {
            if (pair.toggle != null)
            {
                var toggle = pair.toggle;
                var pairRef = pair;
                toggle.onValueChanged.AddListener((isOn) => OnAnyToggleClicked(pairRef, isOn));
            }
        }
    }

    /// <summary>
    /// 토글 클릭 이벤트 핸들러 (모든 토글 공용)
    /// </summary>
    private void OnAnyToggleClicked(ToggleHighlightPair clickedPair, bool isOn)
    {
        if (!isStepActive) return;
        if (!isOn) return; // 토글이 켜질 때만 처리

        var clickedToggle = clickedPair.toggle;

        // Step 2: 난이도 토글 (순서 무관)
        if (currentStep == 1 && difficultyToggles.Contains(clickedPair))
        {
            OnDifficultyToggleClicked(clickedPair);
            return;
        }

        // 순차 토글 처리
        for (int i = 0; i < sequentialToggles.Count; i++)
        {
            if (sequentialToggles[i].toggle == clickedToggle)
            {
                if (i == currentToggleIndex)
                {
                    OnCorrectToggleClicked(clickedPair, i);
                }
                return;
            }
        }
    }

    /// <summary>
    /// 난이도 토글 클릭 처리 (순서 무관)
    /// </summary>
    private void OnDifficultyToggleClicked(ToggleHighlightPair clickedPair)
    {
        if (clickedDifficultyToggles.Contains(clickedPair.toggle))
        {
            if (showDebugLogs)
                Debug.Log($"[Practice] 이미 클릭한 난이도: {clickedPair.toggle.name}");
            return;
        }

        clickedDifficultyToggles.Add(clickedPair.toggle);
        HideHighlight(clickedPair);

        if (showDebugLogs)
            Debug.Log($"[Practice] ✓ 난이도 클릭: {clickedPair.toggle.name} ({clickedDifficultyToggles.Count}/{DIFFICULTY_REQUIRED_COUNT})");

        // 3개 모두 클릭했으면 실습모드 토글로 이동
        if (clickedDifficultyToggles.Count >= DIFFICULTY_REQUIRED_COUNT)
        {
            // 모든 난이도 하이라이트 숨기고 실습모드로
            foreach (var pair in difficultyToggles)
                HideHighlight(pair);

            StartPracticeModeToggle();
        }
    }

    /// <summary>
    /// 실습모드 토글 시작 (난이도 완료 후)
    /// </summary>
    private void StartPracticeModeToggle()
    {
        if (showDebugLogs)
            Debug.Log("[Practice] Step 2-2: 실습모드 토글을 눌러주세요");

        sequentialToggles.Clear();
        if (IsValidPair(practiceToggle))
            sequentialToggles.Add(practiceToggle);

        if (sequentialToggles.Count > 0)
        {
            currentToggleIndex = 0;
            EnableOnlyThisToggle(practiceToggle);
            ShowHighlight(practiceToggle);
        }
        else
        {
            CompleteCurrentStep();
        }
    }

    /// <summary>
    /// 올바른 토글 클릭 처리 (순차)
    /// </summary>
    private void OnCorrectToggleClicked(ToggleHighlightPair clickedPair, int index)
    {
        clickedToggles.Add(clickedPair.toggle);
        HideHighlight(clickedPair);

        if (showDebugLogs)
            Debug.Log($"[Practice] ✓ 토글 클릭 완료: {clickedPair.toggle.name} ({index + 1}/{sequentialToggles.Count})");

        currentToggleIndex++;

        if (currentToggleIndex >= sequentialToggles.Count)
        {
            // Step 4 (위치+시작)는 코루틴에서 처리
            if (currentStep != 4)
            {
                CompleteCurrentStep();
            }
        }
        else
        {
            StartHighlightToggle(currentToggleIndex);
        }
    }

    #region 토글 Interactable 제어

    /// <summary>
    /// 모든 토글 비활성화 (메인메뉴, 난이도 토글 제외)
    /// </summary>
    private void DisableAllTogglesExceptMainMenuAndDifficulty()
    {
        foreach (var pair in allToggles)
        {
            if (pair.toggle != null)
            {
                bool isMainMenu = (mainMenuToggle != null && pair.toggle == mainMenuToggle.toggle);
                bool isDifficulty = difficultyToggles.Contains(pair);
                pair.toggle.interactable = isMainMenu || isDifficulty;
            }
        }
    }

    /// <summary>
    /// 특정 토글만 활성화 (메인메뉴, 난이도는 항상 활성화)
    /// </summary>
    private void EnableOnlyThisToggle(ToggleHighlightPair targetPair)
    {
        DisableAllTogglesExceptMainMenuAndDifficulty();

        if (targetPair != null && targetPair.toggle != null)
        {
            targetPair.toggle.interactable = true;
        }
    }

    /// <summary>
    /// 여러 토글 활성화
    /// </summary>
    private void EnableToggles(List<ToggleHighlightPair> pairs)
    {
        DisableAllTogglesExceptMainMenuAndDifficulty();

        foreach (var pair in pairs)
        {
            if (pair != null && pair.toggle != null)
                pair.toggle.interactable = true;
        }
    }

    /// <summary>
    /// 현재 순차 토글만 활성화
    /// </summary>
    private void EnableCurrentToggleOnly()
    {
        if (currentToggleIndex >= 0 && currentToggleIndex < sequentialToggles.Count)
        {
            EnableOnlyThisToggle(sequentialToggles[currentToggleIndex]);
        }
    }

    #endregion

    private void DisableConflictingComponents()
    {
        var scenarioManager = FindFirstObjectByType<ScenarioManager>();
        if (scenarioManager != null)
        {
            scenarioManager.enabled = false;
            if (showDebugLogs) Debug.Log("[Practice] ScenarioManager disabled");
        }

        var conditionManager = FindFirstObjectByType<ScenarioConditionManager>();
        if (conditionManager != null)
        {
            conditionManager.enabled = false;
            if (showDebugLogs) Debug.Log("[Practice] ScenarioConditionManager disabled");
        }

        var trainingController = FindFirstObjectByType<HandPoseTrainingController>();
        if (trainingController != null)
        {
            trainingController.enabled = false;
            if (showDebugLogs) Debug.Log("[Practice] HandPoseTrainingController disabled");
        }

        var resultTracker = FindFirstObjectByType<TrainingResultTracker>();
        if (resultTracker != null)
        {
            resultTracker.enabled = false;
            if (showDebugLogs) Debug.Log("[Practice] TrainingResultTracker disabled");
        }

        var quizPanel = FindFirstObjectByType<QuizPanel>();
        if (quizPanel != null)
        {
            quizPanel.gameObject.SetActive(false);
            if (showDebugLogs) Debug.Log("[Practice] QuizPanel disabled");
        }

        var lateralFlexionDetector = FindFirstObjectByType<LateralFlexionDetector>();
        if (lateralFlexionDetector != null)
        {
            lateralFlexionDetector.enabled = false;
            if (showDebugLogs) Debug.Log("[Practice] LateralFlexionDetector disabled");
        }

        if (chunaPathEvaluator != null)
        {
            chunaPathEvaluator.enabled = false;
            if (showDebugLogs) Debug.Log("[Practice] ChunaPathEvaluator disabled (Step 6에서 활성화)");
        }
    }

    #region Step Management

    private void StartStep(int step)
    {
        currentStep = step;
        currentCount = 0;
        isStepActive = true;

        if (showDebugLogs)
            Debug.Log($"[Practice] ═══ Step {step + 1} 시작 ═══");

        // 스텝 시작 시 나레이션 재생 및 텍스트 표시
        PlayStepNarration(step);

        switch (step)
        {
            case 0: StartStep1_UIGrab(); break;
            case 1: StartStep2_DifficultyAndMode(); break;
            case 2: StartStep3_ContentToggles(); break;
            case 3: StartStep4_SettingsPanel(); break;
            case 4: StartStep5_PositionAndStart(); break;
            case 5: StartStep6_HoldPractice(); break;
            case 6: StartStep7_Exit(); break;
            default: ShowCompletionPopup(); break;
        }
    }

    /// <summary>
    /// 스텝별 나레이션 재생 및 텍스트 표시
    /// </summary>
    private void PlayStepNarration(int step)
    {
        // 유효한 스텝 범위 확인
        if (stepNarrations == null || step < 0 || step >= stepNarrations.Length)
            return;

        var narration = stepNarrations[step];
        if (narration == null)
            return;

        // 나레이션 오디오 재생
        if (narrationAudioSource != null && narration.audioClip != null)
        {
            narrationAudioSource.Stop();
            narrationAudioSource.clip = narration.audioClip;
            narrationAudioSource.Play();

            if (showDebugLogs)
                Debug.Log($"[Practice] 나레이션 재생: {narration.stepName}");
        }

        // 나레이션 텍스트 표시
        if (narrationText != null)
        {
            narrationText.text = narration.displayText ?? "";

            if (showDebugLogs && !string.IsNullOrEmpty(narration.displayText))
                Debug.Log($"[Practice] 텍스트 표시: {narration.displayText}");
        }

        // 기본적으로 카운트 텍스트 초기화 (반복 스텝에서 별도 설정)
        ClearCountText();
    }

    /// <summary>
    /// 나레이션 텍스트 지우기
    /// </summary>
    public void ClearNarrationText()
    {
        if (narrationText != null)
            narrationText.text = "";
    }

    /// <summary>
    /// 나레이션 오디오 중지
    /// </summary>
    public void StopNarration()
    {
        if (narrationAudioSource != null)
            narrationAudioSource.Stop();
    }

    /// <summary>
    /// 반복 횟수 텍스트 업데이트
    /// </summary>
    private void UpdateCountText(int current, int required)
    {
        if (countText != null)
        {
            countText.text = $"{current}/{required}";
        }
    }

    /// <summary>
    /// 반복 횟수 텍스트 지우기
    /// </summary>
    private void ClearCountText()
    {
        if (countText != null)
        {
            countText.text = "";
        }
    }

    private void CompleteCurrentStep()
    {
        isStepActive = false;
        HideAllHighlights();

        if (showDebugLogs)
            Debug.Log($"[Practice] Step {currentStep + 1} 완료!");

        StartCoroutine(TransitionToNextStep());
    }

    private IEnumerator TransitionToNextStep()
    {
        yield return new WaitForSeconds(stepTransitionDelay);
        StartStep(currentStep + 1);
    }

    #endregion

    #region Step 1: UI 옮기기 (3회)

    private void StartStep1_UIGrab()
    {
        DisableAllTogglesExceptMainMenuAndDifficulty();
        UpdateCountText(0, UI_GRAB_REQUIRED);

        if (showDebugLogs)
            Debug.Log($"[Practice] Step 1: UI를 잡아서 옮겨보세요 (0/{UI_GRAB_REQUIRED})");
    }

    public void OnUIGrabReleased()
    {
        if (currentStep != 0 || !isStepActive) return;

        currentCount++;
        UpdateCountText(currentCount, UI_GRAB_REQUIRED);

        if (showDebugLogs)
            Debug.Log($"[Practice] UI 옮기기: {currentCount}/{UI_GRAB_REQUIRED}");

        if (currentCount >= UI_GRAB_REQUIRED)
        {
            CompleteCurrentStep();
        }
    }

    #endregion

    #region Step 2: 난이도 (순서 무관) → 실습모드

    private void StartStep2_DifficultyAndMode()
    {
        clickedDifficultyToggles.Clear();
        sequentialToggles.Clear();

        // 난이도 토글 모두 활성화 (순서 무관)
        EnableToggles(difficultyToggles);

        // 모든 난이도 하이라이트 표시
        foreach (var pair in difficultyToggles)
            ShowHighlight(pair);

        if (showDebugLogs)
            Debug.Log($"[Practice] Step 2: 난이도 토글 {difficultyToggles.Count}개를 모두 눌러보세요 (순서 무관)");
    }

    #endregion

    #region Step 3: 콘텐츠 토글 (비디오→결과→골격)

    private void StartStep3_ContentToggles()
    {
        sequentialToggles.Clear();

        // 순서: 비디오 → 결과 → 골격
        if (IsValidPair(expertVideoToggle)) sequentialToggles.Add(expertVideoToggle);
        if (IsValidPair(resultToggle)) sequentialToggles.Add(resultToggle);
        if (IsValidPair(skeletonToggle)) sequentialToggles.Add(skeletonToggle);

        if (sequentialToggles.Count == 0)
        {
            if (showDebugLogs) Debug.Log("[Practice] Step 3: 콘텐츠 토글 없음, 건너뜀");
            CompleteCurrentStep();
            return;
        }

        currentToggleIndex = 0;
        StartHighlightToggle(0);

        if (showDebugLogs)
            Debug.Log($"[Practice] Step 3: 콘텐츠(비디오→결과→골격) (총 {sequentialToggles.Count}개)");
    }

    #endregion

    #region Step 4: 설정 패널 (설정→자동조절→환자위치조절)

    private void StartStep4_SettingsPanel()
    {
        sequentialToggles.Clear();

        // 설정 → 자동조절 → 환자위치조절
        if (IsValidPair(settingsToggle)) sequentialToggles.Add(settingsToggle);
        if (IsValidPair(autoAdjustToggle)) sequentialToggles.Add(autoAdjustToggle);
        if (IsValidPair(patientPositionToggle)) sequentialToggles.Add(patientPositionToggle);

        if (sequentialToggles.Count == 0)
        {
            if (showDebugLogs) Debug.Log("[Practice] Step 4: 설정 토글 없음, 건너뜀");
            CompleteCurrentStep();
            return;
        }

        currentToggleIndex = 0;
        StartHighlightToggle(0);

        if (showDebugLogs)
            Debug.Log($"[Practice] Step 4: 설정→자동조절→환자위치조절 (총 {sequentialToggles.Count}개)");
    }

    #endregion

    #region Step 5: 환자 위치 조절 → 설정 닫기 → 시작

    private void StartStep5_PositionAndStart()
    {
        if (showDebugLogs)
            Debug.Log("[Practice] Step 5: 환자 위치를 조정하세요...");

        StartCoroutine(WaitForPatientPositionThenContinue());
    }

    private IEnumerator WaitForPatientPositionThenContinue()
    {
        // 환자 위치 변경 대기 (또는 타임아웃)
        if (patientTransform != null)
        {
            Vector3 startPos = patientTransform.position;
            float timeout = 30f;
            float elapsed = 0f;

            while (elapsed < timeout)
            {
                if (Vector3.Distance(patientTransform.position, startPos) > 0.01f)
                {
                    if (showDebugLogs)
                        Debug.Log("[Practice] 환자 위치 변경 감지!");
                    break;
                }
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        yield return new WaitForSeconds(0.5f);

        // 2. 설정 토글 점멸 (닫기)
        if (showDebugLogs)
            Debug.Log("[Practice] Step 5-2: 설정 패널을 닫아주세요");

        if (IsValidPair(settingsToggle))
        {
            sequentialToggles.Clear();
            sequentialToggles.Add(settingsToggle);
            currentToggleIndex = 0;
            StartHighlightToggle(0);

            yield return new WaitUntil(() => currentToggleIndex >= sequentialToggles.Count || !isStepActive);
        }

        if (!isStepActive) yield break;

        yield return new WaitForSeconds(0.5f);

        // 3. 시작 토글 점멸
        if (showDebugLogs)
            Debug.Log("[Practice] Step 5-3: 시작 토글을 눌러주세요");

        if (IsValidPair(startToggle))
        {
            sequentialToggles.Clear();
            sequentialToggles.Add(startToggle);
            currentToggleIndex = 0;
            StartHighlightToggle(0);

            if (showDebugLogs)
                Debug.Log($"[Practice] 시작 토글 상태: interactable={startToggle.toggle.interactable}, isOn={startToggle.toggle.isOn}");

            yield return new WaitUntil(() => currentToggleIndex >= sequentialToggles.Count || !isStepActive);
        }
        else
        {
            Debug.LogWarning("[Practice] ⚠ startToggle이 할당되지 않았습니다!");
        }

        if (isStepActive)
            CompleteCurrentStep();
    }

    #endregion

    #region Step 6: 홀드 연습 (3사이클)

    private void StartStep6_HoldPractice()
    {
        currentCount = 0;
        isWaitingForHold = true;

        DisableAllTogglesExceptMainMenuAndDifficulty();
        UpdateCountText(0, HOLD_REQUIRED_COUNT);

        if (chunaPathEvaluator != null)
        {
            chunaPathEvaluator.enabled = true;
            chunaPathEvaluator.OnPhaseChanged += OnEvaluationPhaseChanged;
            if (showDebugLogs)
                Debug.Log("[Practice] ChunaPathEvaluator 활성화 - 핸드 가이드 시작");
        }

        if (showDebugLogs)
        {
            Debug.Log($"[Practice] Step 6: 측굴 동작 연습 (0/{HOLD_REQUIRED_COUNT} 사이클)");
        }
    }

    private void OnEvaluationPhaseChanged(ChunaPathEvaluator.EvaluationPhase newPhase)
    {
        if (currentStep != 5 || !isStepActive || !isWaitingForHold) return;

        if (showDebugLogs)
            Debug.Log($"[Practice] 평가 단계: {newPhase}");

        if (newPhase == ChunaPathEvaluator.EvaluationPhase.Completed)
        {
            currentCount++;
            UpdateCountText(currentCount, HOLD_REQUIRED_COUNT);

            if (showDebugLogs)
                Debug.Log($"[Practice] ★ 사이클 완료! ({currentCount}/{HOLD_REQUIRED_COUNT})");

            if (currentCount >= HOLD_REQUIRED_COUNT)
            {
                if (chunaPathEvaluator != null)
                    chunaPathEvaluator.OnPhaseChanged -= OnEvaluationPhaseChanged;

                isWaitingForHold = false;
                CompleteCurrentStep();
            }
        }
    }

    public void OnCycleCompleted()
    {
        if (currentStep != 5 || !isStepActive) return;

        currentCount++;
        UpdateCountText(currentCount, HOLD_REQUIRED_COUNT);

        if (showDebugLogs)
            Debug.Log($"[Practice] 사이클 완료! ({currentCount}/{HOLD_REQUIRED_COUNT})");

        if (currentCount >= HOLD_REQUIRED_COUNT)
        {
            if (chunaPathEvaluator != null)
                chunaPathEvaluator.OnPhaseChanged -= OnEvaluationPhaseChanged;
            isWaitingForHold = false;
            CompleteCurrentStep();
        }
    }

    #endregion

    #region Step 7: 나가기 (메인메뉴 점멸)

    private void StartStep7_Exit()
    {
        sequentialToggles.Clear();

        if (IsValidPair(mainMenuToggle))
            sequentialToggles.Add(mainMenuToggle);

        if (sequentialToggles.Count == 0)
        {
            ShowCompletionPopup();
            return;
        }

        currentToggleIndex = 0;
        StartHighlightToggle(0);

        if (showDebugLogs)
            Debug.Log("[Practice] Step 7: 나가기(메인메뉴) 토글을 눌러주세요");
    }

    #endregion

    #region 하이라이트 시스템

    private void StartHighlightToggle(int index)
    {
        if (index >= sequentialToggles.Count) return;

        currentToggleIndex = index;
        HideAllHighlights();
        EnableCurrentToggleOnly();

        var currentPair = sequentialToggles[index];
        ShowHighlight(currentPair);

        if (showDebugLogs)
            Debug.Log($"[Practice] 하이라이트: {currentPair.toggle.name} ({index + 1}/{sequentialToggles.Count})");
    }

    #endregion

    #region Completion

    private void ShowCompletionPopup()
    {
        isStepActive = false;

        if (showDebugLogs)
            Debug.Log("[Practice] ★★★ 모든 연습 완료! ★★★");

        if (exitPopupController != null)
        {
            exitPopupController.ShowPopup();
        }
        else
        {
            var popup = FindFirstObjectByType<ExitPopupController>();
            if (popup != null)
                popup.ShowPopup();
            else
                Debug.LogWarning("[Practice] 완료 팝업을 찾을 수 없습니다!");
        }
    }

    #endregion

    #region Public Methods

    public void RestartPractice()
    {
        StopAllCoroutines();
        HideAllHighlights();
        currentStep = 0;
        currentCount = 0;
        isStepActive = false;
        isWaitingForHold = false;
        sequentialToggles.Clear();
        clickedToggles.Clear();
        clickedDifficultyToggles.Clear();

        StartStep(0);

        if (showDebugLogs)
            Debug.Log("[Practice] 연습 재시작");
    }

    public int GetCurrentStep() => currentStep;
    public int GetCurrentCount() => currentCount;
    public bool IsActive() => isStepActive;

    /// <summary>
    /// 외부에서 환자 위치 변경 시 호출
    /// </summary>
    public void OnPatientPositionChanged()
    {
        if (currentStep != 4 || !isStepActive) return;
        if (showDebugLogs)
            Debug.Log("[Practice] 환자 위치 변경됨!");
    }

    #endregion

    void OnDestroy()
    {
        HideAllHighlights();

        if (chunaPathEvaluator != null)
            chunaPathEvaluator.OnPhaseChanged -= OnEvaluationPhaseChanged;
    }
}
