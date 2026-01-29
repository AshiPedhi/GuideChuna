using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System;

/// <summary>
/// 연습 모드 관리자 (튜토리얼)
/// 모든 토글을 순서대로 점멸시켜 사용자가 따라하도록 유도
///
/// 진행 순서:
/// 1. UI 잡고 옮기기 (3회)
/// 2. 난이도 토글 (중급→상급→초급) → 실습모드 토글
/// 3. 콘텐츠 토글 (비디오→결과→골격)
/// 4. 설정 토글 → 자동조절 → 환자위치조절
/// 5. 환자 위치 조정 → 설정 닫기 → 시작 토글
/// 6. 핸드 가이드 홀드 연습 (3사이클)
/// 7. 나가기(메인메뉴) 토글 → 완료
/// </summary>
public class PracticeManager : MonoBehaviour
{
    /// <summary>
    /// 토글과 점멸 이미지를 함께 관리하는 구조체
    /// </summary>
    [Serializable]
    public class ToggleBlinkPair
    {
        [Tooltip("토글 컴포넌트")]
        public Toggle toggle;
        [Tooltip("점멸할 이미지 (배경 또는 아이콘)")]
        public Image blinkImage;
    }

    [Header("=== Step 1: UI 옮기기 ===")]
    [Tooltip("잡아서 옮길 UI (정보패널Root 등)")]
    [SerializeField] private Transform grabbableUI;
    private Vector3 uiInitialPosition;
    private Quaternion uiInitialRotation;

    [Header("=== 가이드 패널 (직접 할당) ===")]
    [Tooltip("가이드 패널 GameObject - Inspector에서 직접 할당")]
    [SerializeField] private GameObject guidePanel;

    [Header("=== Step 2: 난이도 토글 (중급→상급→초급 순서로 할당) ===")]
    [SerializeField] private ToggleBlinkPair intermediateToggle;  // 중급
    [SerializeField] private ToggleBlinkPair advancedToggle;      // 상급
    [SerializeField] private ToggleBlinkPair beginnerToggle;      // 초급
    [SerializeField] private ToggleBlinkPair practiceToggle;      // 실습모드

    [Header("=== Step 3: 콘텐츠 토글 (비디오→결과→골격 순서) ===")]
    [SerializeField] private ToggleBlinkPair expertVideoToggle;   // 전문가 영상
    [SerializeField] private ToggleBlinkPair resultToggle;        // 수행결과
    [SerializeField] private ToggleBlinkPair skeletonToggle;      // 근골격

    [Header("=== Step 4: 설정 패널 토글 ===")]
    [SerializeField] private ToggleBlinkPair settingsToggle;      // 설정
    [SerializeField] private ToggleBlinkPair autoAdjustToggle;    // 자동조절
    [SerializeField] private ToggleBlinkPair patientPositionToggle; // 환자위치조절

    [Header("=== Step 5: 시작 ===")]
    [SerializeField] private ToggleBlinkPair startToggle;         // 시작

    [Header("=== Step 6: 홀드 연습 ===")]
    [Tooltip("ChunaPathEvaluator (홀드 감지용)")]
    [SerializeField] private ChunaPathEvaluator chunaPathEvaluator;
    [Tooltip("환자 Transform (위치 변경 감지용)")]
    [SerializeField] private Transform patientTransform;

    [Header("=== Step 7: 나가기 ===")]
    [SerializeField] private ToggleBlinkPair mainMenuToggle;      // 메인메뉴
    [Tooltip("완료 팝업")]
    [SerializeField] private ExitPopupController exitPopupController;

    [Header("=== 하이라이트 설정 ===")]
    [SerializeField] private float blinkInterval = 0.5f;
    [SerializeField] private Color highlightColor = new Color(1f, 0.8f, 0.2f, 1f);

    [Header("=== 설정 ===")]
    [SerializeField] private float stepTransitionDelay = 1.0f;
    [SerializeField] private bool showDebugLogs = true;

    // 상태
    private int currentStep = 0;
    private int currentCount = 0;
    private bool isStepActive = false;

    // 순차 토글 하이라이트
    private List<ToggleBlinkPair> sequentialToggles = new List<ToggleBlinkPair>();
    private int currentToggleIndex = 0;
    private Dictionary<Image, Color> originalImageColors = new Dictionary<Image, Color>();
    private HashSet<Toggle> clickedToggles = new HashSet<Toggle>();
    private Coroutine blinkCoroutine;

    // 모든 토글 리스트 (interactable 제어용)
    private List<ToggleBlinkPair> allToggles = new List<ToggleBlinkPair>();

    // 홀드 연습
    private bool isWaitingForHold = false;

    // 상수
    private const int HOLD_REQUIRED_COUNT = 3;
    private const int UI_GRAB_REQUIRED = 3;

    void Start()
    {
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

        // ★ 가이드 패널 처음부터 표시
        ShowGuidePanel();

        // ★ 모든 토글 초기 비활성화 (메인메뉴 제외)
        DisableAllTogglesExceptMainMenu();

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

        // 난이도
        if (IsValidPair(intermediateToggle)) allToggles.Add(intermediateToggle);
        if (IsValidPair(advancedToggle)) allToggles.Add(advancedToggle);
        if (IsValidPair(beginnerToggle)) allToggles.Add(beginnerToggle);

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
            Debug.Log($"[Practice] 총 {allToggles.Count}개 토글 등록됨");
    }

    private bool IsValidPair(ToggleBlinkPair pair)
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
    /// 모든 토글에 이벤트 리스너 설정
    /// </summary>
    private void SetupAllToggleListeners()
    {
        foreach (var pair in allToggles)
        {
            if (pair.toggle != null)
            {
                var toggle = pair.toggle;
                toggle.onValueChanged.AddListener((isOn) => OnAnyToggleClicked(toggle, isOn));
            }
        }
    }

    /// <summary>
    /// 토글 클릭 이벤트 핸들러 (모든 토글 공용)
    /// </summary>
    private void OnAnyToggleClicked(Toggle clickedToggle, bool isOn)
    {
        // 비활성 상태면 무시 (추가 안전장치)
        if (!clickedToggle.interactable)
        {
            if (showDebugLogs)
                Debug.Log($"[Practice] 토글 {clickedToggle.name} 클릭 무시 (interactable=false)");
            return;
        }

        if (!isStepActive) return;
        if (!isOn) return; // 토글이 켜질 때만 처리

        // 현재 순차 토글 리스트에서 해당 토글 찾기
        for (int i = 0; i < sequentialToggles.Count; i++)
        {
            if (sequentialToggles[i].toggle == clickedToggle)
            {
                // 현재 하이라이트된 토글인지 확인
                if (i == currentToggleIndex)
                {
                    OnCorrectToggleClicked(clickedToggle, i);
                }
                else
                {
                    if (showDebugLogs)
                        Debug.Log($"[Practice] 잘못된 순서 클릭: {clickedToggle.name} (현재 {currentToggleIndex}, 클릭 {i})");
                }
                return;
            }
        }
    }

    /// <summary>
    /// 올바른 토글 클릭 처리
    /// </summary>
    private void OnCorrectToggleClicked(Toggle clickedToggle, int index)
    {
        clickedToggles.Add(clickedToggle);

        if (showDebugLogs)
            Debug.Log($"[Practice] ✓ 토글 클릭 완료: {clickedToggle.name} ({index + 1}/{sequentialToggles.Count})");

        currentToggleIndex++;

        if (currentToggleIndex >= sequentialToggles.Count)
        {
            StopBlinking();

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
    /// 모든 토글 비활성화 (메인메뉴 제외)
    /// </summary>
    private void DisableAllTogglesExceptMainMenu()
    {
        foreach (var pair in allToggles)
        {
            if (pair.toggle != null)
            {
                // 메인메뉴는 항상 활성화
                bool isMainMenu = (mainMenuToggle != null && pair.toggle == mainMenuToggle.toggle);
                pair.toggle.interactable = isMainMenu;
            }
        }

        if (showDebugLogs)
            Debug.Log("[Practice] 모든 토글 비활성화 (메인메뉴 제외)");
    }

    /// <summary>
    /// 특정 토글만 활성화
    /// </summary>
    private void EnableOnlyThisToggle(ToggleBlinkPair targetPair)
    {
        DisableAllTogglesExceptMainMenu();

        if (targetPair != null && targetPair.toggle != null)
        {
            targetPair.toggle.interactable = true;

            if (showDebugLogs)
                Debug.Log($"[Practice] 토글 활성화: {targetPair.toggle.name}");
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

    private void CompleteCurrentStep()
    {
        isStepActive = false;
        StopBlinking();

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
        DisableAllTogglesExceptMainMenu();

        if (showDebugLogs)
            Debug.Log($"[Practice] Step 1: UI를 잡아서 옮겨보세요 (0/{UI_GRAB_REQUIRED})");
    }

    public void OnUIGrabReleased()
    {
        if (currentStep != 0 || !isStepActive) return;

        currentCount++;
        if (showDebugLogs)
            Debug.Log($"[Practice] UI 옮기기: {currentCount}/{UI_GRAB_REQUIRED}");

        if (currentCount >= UI_GRAB_REQUIRED)
        {
            CompleteCurrentStep();
        }
    }

    #endregion

    #region Step 2: 난이도 선택 (중급→상급→초급) → 실습모드

    private void StartStep2_DifficultyAndMode()
    {
        sequentialToggles.Clear();

        // 순서: 중급 → 상급 → 초급 → 실습모드
        if (IsValidPair(intermediateToggle)) sequentialToggles.Add(intermediateToggle);
        if (IsValidPair(advancedToggle)) sequentialToggles.Add(advancedToggle);
        if (IsValidPair(beginnerToggle)) sequentialToggles.Add(beginnerToggle);
        if (IsValidPair(practiceToggle)) sequentialToggles.Add(practiceToggle);

        if (sequentialToggles.Count == 0)
        {
            if (showDebugLogs) Debug.Log("[Practice] Step 2: 토글 없음, 건너뜀");
            CompleteCurrentStep();
            return;
        }

        currentToggleIndex = 0;
        SaveOriginalColors();
        EnableCurrentToggleOnly();
        StartHighlightToggle(0);

        if (showDebugLogs)
            Debug.Log($"[Practice] Step 2: 난이도(중급→상급→초급) → 실습모드 (총 {sequentialToggles.Count}개)");
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
        SaveOriginalColors();
        EnableCurrentToggleOnly();
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
        SaveOriginalColors();
        EnableCurrentToggleOnly();
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
            SaveOriginalColors();
            EnableCurrentToggleOnly();
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
            SaveOriginalColors();
            EnableCurrentToggleOnly();
            StartHighlightToggle(0);

            yield return new WaitUntil(() => currentToggleIndex >= sequentialToggles.Count || !isStepActive);
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

        DisableAllTogglesExceptMainMenu();

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
        SaveOriginalColors();
        EnableCurrentToggleOnly();
        StartHighlightToggle(0);

        if (showDebugLogs)
            Debug.Log("[Practice] Step 7: 나가기(메인메뉴) 토글을 눌러주세요");
    }

    #endregion

    #region 점멸 시스템

    private void SaveOriginalColors()
    {
        originalImageColors.Clear();
        clickedToggles.Clear();

        foreach (var pair in sequentialToggles)
        {
            if (pair.blinkImage != null)
            {
                originalImageColors[pair.blinkImage] = pair.blinkImage.color;
            }
        }
    }

    private void StartHighlightToggle(int index)
    {
        if (index >= sequentialToggles.Count) return;

        currentToggleIndex = index;
        StopBlinking();
        EnableCurrentToggleOnly();

        var currentPair = sequentialToggles[index];
        if (currentPair.blinkImage != null)
        {
            blinkCoroutine = StartCoroutine(BlinkImage(currentPair.blinkImage));

            if (showDebugLogs)
                Debug.Log($"[Practice] 점멸 시작: {currentPair.toggle.name} ({index + 1}/{sequentialToggles.Count})");
        }
        else
        {
            Debug.LogWarning($"[Practice] ⚠ {currentPair.toggle.name}에 blinkImage가 할당되지 않았습니다!");
        }
    }

    private IEnumerator BlinkImage(Image image)
    {
        if (image == null) yield break;

        Color originalColor = originalImageColors.ContainsKey(image) ? originalImageColors[image] : image.color;
        bool isHighlight = false;

        while (true)
        {
            isHighlight = !isHighlight;
            image.color = isHighlight ? highlightColor : originalColor;
            yield return new WaitForSeconds(blinkInterval);
        }
    }

    private void StopBlinking()
    {
        if (blinkCoroutine != null)
        {
            StopCoroutine(blinkCoroutine);
            blinkCoroutine = null;
        }

        // 원래 색상 복원
        foreach (var kvp in originalImageColors)
        {
            if (kvp.Key != null)
            {
                kvp.Key.color = kvp.Value;
            }
        }
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
        StopBlinking();
        currentStep = 0;
        currentCount = 0;
        isStepActive = false;
        isWaitingForHold = false;
        sequentialToggles.Clear();
        clickedToggles.Clear();

        StartStep(0);

        if (showDebugLogs)
            Debug.Log("[Practice] 연습 재시작");
    }

    public int GetCurrentStep() => currentStep;
    public int GetCurrentCount() => currentCount;
    public bool IsActive() => isStepActive;

    #endregion

    void OnDestroy()
    {
        StopBlinking();

        if (chunaPathEvaluator != null)
            chunaPathEvaluator.OnPhaseChanged -= OnEvaluationPhaseChanged;
    }
}
