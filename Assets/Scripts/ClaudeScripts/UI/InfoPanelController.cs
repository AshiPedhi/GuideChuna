using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ChunaTraining;  // DifficultyLevel, DifficultyManager 사용

/// <summary>
/// 정보 패널 통합 컨트롤러 (옵션 A - 전체 통합)
///
/// [페이지 구성]
/// - 모드 선택 페이지: 시나리오 시작 전 모드/난이도 선택
/// - 근골격 페이지: RenderTexture RawImage (근골격 표시)
/// - 전문가 영상 페이지: 영상 재생 (토글 on일 때만 재생)
/// - 수행결과 페이지: 현재까지의 실습 결과 표시
///
/// [토글 그룹]
/// - 콘텐츠 그룹: 근골격, 전문가 영상, 수행결과 (상호 배타적)
/// - 메뉴 그룹: 설정, 메인으로 (상호 배타적)
///
/// [기존 코드 대체]
/// - QuickMenuController → 삭제 (설정/메인 버튼 이전)
/// - ModeSelectionManagerV2 → 단순화 (모드 데이터만 관리)
/// </summary>
public class InfoPanelController : MonoBehaviour
{
    #region 페이지 오브젝트
    [Header("═══ 페이지 오브젝트 ═══")]
    [Tooltip("모드 선택 페이지 (시나리오 시작 전)")]
    [SerializeField] private GameObject modeSelectionPage;

    [Tooltip("근골격 RenderTexture 페이지")]
    [SerializeField] private GameObject skeletonPage;

    [Tooltip("전문가 영상 페이지")]
    [SerializeField] private GameObject expertVideoPage;

    [Tooltip("수행결과 페이지")]
    [SerializeField] private GameObject resultPage;
    #endregion

    #region 모드 선택 UI
    [Header("═══ 모드 선택 (실습/평가) ═══")]
    [SerializeField] private Toggle practiceToggle;
    [SerializeField] private Toggle evaluationToggle;
    [SerializeField] private TextMeshProUGUI practiceLabel;
    [SerializeField] private TextMeshProUGUI evaluationLabel;

    [Header("═══ 난이도 선택 ═══")]
    [SerializeField] private Toggle beginnerToggle;
    [SerializeField] private Toggle intermediateToggle;
    [SerializeField] private Toggle advancedToggle;
    [SerializeField] private TextMeshProUGUI beginnerDescription;
    [SerializeField] private TextMeshProUGUI intermediateDescription;
    [SerializeField] private TextMeshProUGUI advancedDescription;

    [Header("═══ 시나리오 정보 ═══")]
    [SerializeField] private TextMeshProUGUI scenarioTitleText;
    #endregion

    #region 콘텐츠 토글 그룹
    [Header("═══ 콘텐츠 토글 (그룹 1) ═══")]
    [Tooltip("근골격 토글 버튼")]
    [SerializeField] private Toggle skeletonToggle;

    [Tooltip("전문가 영상 토글 버튼")]
    [SerializeField] private Toggle expertVideoToggle;

    [Tooltip("수행결과 토글 버튼")]
    [SerializeField] private Toggle resultToggle;
    #endregion

    #region 메뉴 토글 그룹
    [Header("═══ 메뉴 토글 (그룹 2) ═══")]
    [Tooltip("설정 토글 버튼")]
    [SerializeField] private Toggle settingsToggle;

    [Tooltip("메인으로 토글 버튼")]
    [SerializeField] private Toggle mainMenuToggle;
    #endregion

    #region 팝업 연결
    [Header("═══ 팝업 연결 ═══")]
    [Tooltip("설정 팝업")]
    [SerializeField] private GameObject settingsPopup;

    [Tooltip("메인으로 이동 확인 팝업")]
    [SerializeField] private GameObject exitConfirmPopup;
    #endregion

    #region 컨트롤러 참조
    [Header("═══ 컨트롤러 참조 ═══")]
    [Tooltip("가이드 영상 컨트롤러 (없으면 자동 검색)")]
    [SerializeField] private GuideVideoController guideVideoController;

    [Tooltip("동적 결과 테이블 UI (없으면 자동 검색)")]
    [SerializeField] private DynamicResultTableUI dynamicResultTableUI;

    [Tooltip("ScenarioManager (없으면 자동 검색)")]
    [SerializeField] private ScenarioManager scenarioManager;

    [Tooltip("설정 컨트롤러 (환자 위치 조정 자동 off용)")]
    [SerializeField] private PracticeSettingsController practiceSettingsController;

    [Tooltip("시나리오 진행 UI (가이드패널)")]
    [SerializeField] private GameObject scenarioProgressUI;

    [Tooltip("환자 위치 프리셋 관리자 (없으면 자동 검색)")]
    [SerializeField] private PatientPositionManager patientPositionManager;

    [Tooltip("시나리오 시작 시 적용할 기본 프리셋 이름")]
    [SerializeField] private string defaultPositionPreset = "Seated";
    #endregion

    #region 토글 컬러 설정
    [Header("═══ 토글 컬러 설정 ═══")]
    [SerializeField] private bool useCustomColors = true;
    [SerializeField] private Color activeColor = new Color(0.2f, 0.8f, 1f, 1f);
    [SerializeField] private Color inactiveColor = new Color(0.5f, 0.5f, 0.5f, 1f);
    #endregion

    #region 토글 아이콘
    [Header("═══ 토글 아이콘 (선택) ═══")]
    [SerializeField] private Image skeletonIcon;
    [SerializeField] private Image expertVideoIcon;
    [SerializeField] private Image resultIcon;
    [SerializeField] private Image settingsIcon;
    [SerializeField] private Image mainMenuIcon;
    #endregion

    // 상태 추적
    private bool isScenarioStarted = false;
    private ContentPage currentContentPage = ContentPage.ModeSelection;
    private bool isSettingsOpen = false;
    private bool isExitPopupOpen = false;

    // 모드 선택 상태
    private ModeType selectedMode = ModeType.None;
    private DifficultyLevel selectedDifficulty = DifficultyLevel.Beginner;

    // 콘텐츠 페이지 열거형
    public enum ContentPage
    {
        None,
        ModeSelection,
        Skeleton,
        ExpertVideo,
        Result
    }

    // 모드 타입
    public enum ModeType
    {
        None,
        Practice,
        Evaluation
    }

    // 난이도 타입 - ChunaTraining.DifficultyLevel 사용 (중복 제거)

    // 이벤트
    public event Action<ModeType, DifficultyLevel> OnModeSelected;
    public event Action OnSimulationStarted;

    #region Unity Lifecycle
    void Awake()
    {
        // 컨트롤러 자동 검색
        if (guideVideoController == null)
            guideVideoController = FindFirstObjectByType<GuideVideoController>();

        if (dynamicResultTableUI == null)
            dynamicResultTableUI = FindFirstObjectByType<DynamicResultTableUI>();

        if (scenarioManager == null)
            scenarioManager = FindFirstObjectByType<ScenarioManager>();

        if (practiceSettingsController == null)
            practiceSettingsController = FindFirstObjectByType<PracticeSettingsController>();

        if (patientPositionManager == null)
            patientPositionManager = FindFirstObjectByType<PatientPositionManager>();
    }

    void Start()
    {
        InitializePanel();
        SetupToggleListeners();
        SetupModeSelectionListeners();
        UpdateAllToggleColors();
    }

    void OnEnable()
    {
        SubscribeScenarioEvents();
    }

    void OnDisable()
    {
        UnsubscribeScenarioEvents();
    }

    void OnDestroy()
    {
        RemoveAllListeners();
    }
    #endregion

    #region 이벤트 구독
    private void SubscribeScenarioEvents()
    {
        try
        {
            ScenarioEventSystem.Instance.OnScenarioStarted += OnScenarioStarted;
            ScenarioEventSystem.Instance.OnScenarioCompleted += OnScenarioCompleted;
        }
        catch (Exception e)
        {
            ChunaLogger.LogWarning($"[InfoPanel] 이벤트 구독 실패: {e.Message}");
        }
    }

    private void UnsubscribeScenarioEvents()
    {
        try
        {
            ScenarioEventSystem.Instance.OnScenarioStarted -= OnScenarioStarted;
            ScenarioEventSystem.Instance.OnScenarioCompleted -= OnScenarioCompleted;
        }
        catch (Exception e)
        {
            ChunaLogger.LogWarning($"[InfoPanel] 이벤트 해제 실패: {e.Message}");
        }
    }
    #endregion

    #region 초기화
    private void InitializePanel()
    {
        // 초기 상태: 모드 선택 페이지
        ShowContentPage(ContentPage.ModeSelection);

        // 시나리오 진행 UI 숨김 (시나리오 시작 전)
        if (scenarioProgressUI != null)
            scenarioProgressUI.SetActive(false);

        // 팝업 숨김
        if (settingsPopup != null)
            settingsPopup.SetActive(false);

        if (exitConfirmPopup != null)
            exitConfirmPopup.SetActive(false);

        // 콘텐츠 토글 초기화 (모두 off, 시나리오 시작 전에는 비활성화)
        SetToggleWithoutNotify(skeletonToggle, false);
        SetToggleWithoutNotify(expertVideoToggle, false);
        SetToggleWithoutNotify(resultToggle, false);
        SetContentTogglesInteractable(false);

        // 메뉴 토글 초기화 (모두 off, 메인메뉴는 항상 활성화)
        SetToggleWithoutNotify(settingsToggle, false);
        SetToggleWithoutNotify(mainMenuToggle, false);
        if (settingsToggle != null) settingsToggle.interactable = false;
        if (mainMenuToggle != null) mainMenuToggle.interactable = true;

        // 모드 선택 초기화
        InitializeModeSelection();

        isSettingsOpen = false;
        isExitPopupOpen = false;

        ChunaLogger.Log("[InfoPanel] 패널 초기화 완료");
    }

    private void InitializeModeSelection()
    {
        // 기본값: 초급자
        selectedDifficulty = DifficultyLevel.Beginner;
        selectedMode = ModeType.None;

        // 모드 토글 초기화
        SetToggleWithoutNotify(practiceToggle, false);
        SetToggleWithoutNotify(evaluationToggle, false);

        // 난이도 토글 초기화 (초급자 기본 선택)
        SetToggleWithoutNotify(beginnerToggle, true);
        SetToggleWithoutNotify(intermediateToggle, false);
        SetToggleWithoutNotify(advancedToggle, false);

        // 설명 텍스트 설정
        SetupModeDescriptions();

        // ★ DifficultyManager에 초기 난이도 전달
        if (DifficultyManager.Instance != null)
        {
            DifficultyManager.Instance.SetDifficulty(selectedDifficulty);
        }

        // ★ 초기 색상 업데이트
        UpdateModeSelectionColors();
        UpdateDifficultyDescriptions();
    }

    private void SetupModeDescriptions()
    {
        if (practiceLabel != null)
            practiceLabel.text = "안내에 따라 과정 학습";

        if (evaluationLabel != null)
            evaluationLabel.text = "학습 내용 테스트";

        if (beginnerDescription != null)
            beginnerDescription.text = "최초 학습자를 위한 레벨입니다";

        if (intermediateDescription != null)
            intermediateDescription.text = "실습 경험자를 위한 레벨입니다";

        if (advancedDescription != null)
            advancedDescription.text = "숙련자를 위한 레벨입니다";
    }

    private void SetupToggleListeners()
    {
        // 콘텐츠 토글 그룹
        if (skeletonToggle != null)
        {
            skeletonToggle.onValueChanged.RemoveAllListeners();
            skeletonToggle.onValueChanged.AddListener(OnSkeletonToggleChanged);
        }

        if (expertVideoToggle != null)
        {
            expertVideoToggle.onValueChanged.RemoveAllListeners();
            expertVideoToggle.onValueChanged.AddListener(OnExpertVideoToggleChanged);
        }

        if (resultToggle != null)
        {
            resultToggle.onValueChanged.RemoveAllListeners();
            resultToggle.onValueChanged.AddListener(OnResultToggleChanged);
        }

        // 메뉴 토글 그룹
        if (settingsToggle != null)
        {
            settingsToggle.onValueChanged.RemoveAllListeners();
            settingsToggle.onValueChanged.AddListener(OnSettingsToggleChanged);
        }

        if (mainMenuToggle != null)
        {
            mainMenuToggle.onValueChanged.RemoveAllListeners();
            mainMenuToggle.onValueChanged.AddListener(OnMainMenuToggleChanged);
        }
    }

    private void SetupModeSelectionListeners()
    {
        // 모드 토글
        if (practiceToggle != null)
        {
            practiceToggle.onValueChanged.RemoveAllListeners();
            practiceToggle.onValueChanged.AddListener((isOn) => {
                if (isOn) OnModeToggleChanged(ModeType.Practice);
            });
        }

        if (evaluationToggle != null)
        {
            evaluationToggle.onValueChanged.RemoveAllListeners();
            evaluationToggle.onValueChanged.AddListener((isOn) => {
                if (isOn) OnModeToggleChanged(ModeType.Evaluation);
            });
        }

        // 난이도 토글
        if (beginnerToggle != null)
        {
            beginnerToggle.onValueChanged.RemoveAllListeners();
            beginnerToggle.onValueChanged.AddListener((isOn) => {
                if (isOn) OnDifficultyToggleChanged(DifficultyLevel.Beginner);
            });
        }

        if (intermediateToggle != null)
        {
            intermediateToggle.onValueChanged.RemoveAllListeners();
            intermediateToggle.onValueChanged.AddListener((isOn) => {
                if (isOn) OnDifficultyToggleChanged(DifficultyLevel.Intermediate);
            });
        }

        if (advancedToggle != null)
        {
            advancedToggle.onValueChanged.RemoveAllListeners();
            advancedToggle.onValueChanged.AddListener((isOn) => {
                if (isOn) OnDifficultyToggleChanged(DifficultyLevel.Advanced);
            });
        }
    }

    private void RemoveAllListeners()
    {
        if (skeletonToggle != null) skeletonToggle.onValueChanged.RemoveAllListeners();
        if (expertVideoToggle != null) expertVideoToggle.onValueChanged.RemoveAllListeners();
        if (resultToggle != null) resultToggle.onValueChanged.RemoveAllListeners();
        if (settingsToggle != null) settingsToggle.onValueChanged.RemoveAllListeners();
        if (mainMenuToggle != null) mainMenuToggle.onValueChanged.RemoveAllListeners();
        if (practiceToggle != null) practiceToggle.onValueChanged.RemoveAllListeners();
        if (evaluationToggle != null) evaluationToggle.onValueChanged.RemoveAllListeners();
        if (beginnerToggle != null) beginnerToggle.onValueChanged.RemoveAllListeners();
        if (intermediateToggle != null) intermediateToggle.onValueChanged.RemoveAllListeners();
        if (advancedToggle != null) advancedToggle.onValueChanged.RemoveAllListeners();
    }
    #endregion

    #region 모드 선택 핸들러
    private void OnModeToggleChanged(ModeType mode)
    {
        selectedMode = mode;
        ChunaLogger.Log($"[InfoPanel] 모드 선택: {mode}");

        UpdateModeSelectionColors();
        OnModeSelected?.Invoke(selectedMode, selectedDifficulty);

        // 모드 선택 시 자동으로 시나리오 시작
        StartSimulation();
    }

    private void OnDifficultyToggleChanged(DifficultyLevel difficulty)
    {
        selectedDifficulty = difficulty;
        ChunaLogger.Log($"[InfoPanel] 난이도 선택: {difficulty}");

        // ★ DifficultyManager에 난이도 전달 (연동)
        if (DifficultyManager.Instance != null)
        {
            DifficultyManager.Instance.SetDifficulty(difficulty);
        }

        UpdateModeSelectionColors();
        UpdateDifficultyDescriptions();
    }

    private void UpdateDifficultyDescriptions()
    {
        Color activeTextColor = Color.white;
        Color inactiveTextColor = new Color(1f, 1f, 1f, 0.6f);

        if (beginnerDescription != null)
            beginnerDescription.color = (selectedDifficulty == DifficultyLevel.Beginner) ? activeTextColor : inactiveTextColor;

        if (intermediateDescription != null)
            intermediateDescription.color = (selectedDifficulty == DifficultyLevel.Intermediate) ? activeTextColor : inactiveTextColor;

        if (advancedDescription != null)
            advancedDescription.color = (selectedDifficulty == DifficultyLevel.Advanced) ? activeTextColor : inactiveTextColor;
    }

    private void UpdateModeSelectionColors()
    {
        // 모드 토글 (Animator + Color 모두 처리)
        UpdateToggleColor(practiceToggle, null, selectedMode == ModeType.Practice);
        UpdateToggleColor(evaluationToggle, null, selectedMode == ModeType.Evaluation);

        // 난이도 토글
        UpdateToggleColor(beginnerToggle, null, selectedDifficulty == DifficultyLevel.Beginner);
        UpdateToggleColor(intermediateToggle, null, selectedDifficulty == DifficultyLevel.Intermediate);
        UpdateToggleColor(advancedToggle, null, selectedDifficulty == DifficultyLevel.Advanced);
    }
    #endregion

    #region 시뮬레이션 시작
    /// <summary>
    /// 시나리오 시뮬레이션 시작
    /// </summary>
    public void StartSimulation()
    {
        if (selectedMode == ModeType.None)
        {
            ChunaLogger.LogWarning("[InfoPanel] 모드를 선택해주세요!");
            return;
        }

        ChunaLogger.Log($"<color=green>[InfoPanel] 시뮬레이션 시작 - 모드: {selectedMode}, 난이도: {selectedDifficulty}</color>");

        // ScenarioManager에 모드 정보 전달
        if (scenarioManager != null)
        {
            scenarioManager.SetModeInfo(GetModeText(), GetDifficultyText());
            scenarioManager.StartScenario();
        }
        else
        {
            ChunaLogger.LogError("[InfoPanel] ScenarioManager를 찾을 수 없습니다!");
        }

        // 설정 저장
        PlayerPrefs.SetString(PrefsKeys.SelectedMode, selectedMode.ToString());
        PlayerPrefs.SetString(PrefsKeys.SelectedDifficulty, selectedDifficulty.ToString());
        PlayerPrefs.Save();

        OnSimulationStarted?.Invoke();
    }

    private string GetModeText()
    {
        switch (selectedMode)
        {
            case ModeType.Practice: return "실습";
            case ModeType.Evaluation: return "평가";
            default: return "미선택";
        }
    }

    private string GetDifficultyText()
    {
        switch (selectedDifficulty)
        {
            case DifficultyLevel.Beginner: return "초급자";
            case DifficultyLevel.Intermediate: return "중급자";
            case DifficultyLevel.Advanced: return "상급자";
            default: return "중급자";
        }
    }
    #endregion

    #region 콘텐츠 토글 핸들러
    private void OnSkeletonToggleChanged(bool isOn)
    {
        ChunaLogger.Log($"[InfoPanel] 근골격 토글: {(isOn ? "ON" : "OFF")}");

        if (isOn)
        {
            SetToggleWithoutNotify(expertVideoToggle, false);
            SetToggleWithoutNotify(resultToggle, false);
            StopExpertVideo();
            ShowContentPage(ContentPage.Skeleton);
        }

        UpdateAllToggleColors();
    }

    private void OnExpertVideoToggleChanged(bool isOn)
    {
        ChunaLogger.Log($"[InfoPanel] 전문가 영상 토글: {(isOn ? "ON" : "OFF")}, 시나리오시작:{isScenarioStarted}, 현재페이지:{currentContentPage}");

        if (isOn)
        {
            // 다른 콘텐츠 토글 끄기
            SetToggleWithoutNotify(skeletonToggle, false);
            SetToggleWithoutNotify(resultToggle, false);

            // 전문가 영상 페이지 표시 및 재생
            ShowContentPage(ContentPage.ExpertVideo);
            PlayExpertVideo();
        }
        else
        {
            // 영상 정지
            StopExpertVideo();

            // ★ 연습 모드에서는 자동 페이지 전환 안 함 (다른 토글 클릭으로 인한 OFF일 수 있음)
            var practiceManager = FindFirstObjectByType<PracticeManager>();
            if (practiceManager != null && practiceManager.enabled)
            {
                // 연습 모드: 페이지 전환 안 함 (다른 토글의 핸들러가 처리)
                ChunaLogger.Log("[InfoPanel] 연습 모드 - 비디오 OFF 시 자동 페이지 전환 안 함");
            }
            else if (isScenarioStarted)
            {
                // 시나리오 진행 중: 근골격 페이지로 전환
                SetToggleWithoutNotify(skeletonToggle, true);
                ShowContentPage(ContentPage.Skeleton);
            }
            else
            {
                // 시나리오 시작 전: 모드 선택 페이지로 전환
                ShowContentPage(ContentPage.ModeSelection);
            }
        }

        UpdateAllToggleColors();
    }

    private void OnResultToggleChanged(bool isOn)
    {
        ChunaLogger.Log($"[InfoPanel] 수행결과 토글: {(isOn ? "ON" : "OFF")}");

        if (isOn)
        {
            SetToggleWithoutNotify(skeletonToggle, false);
            SetToggleWithoutNotify(expertVideoToggle, false);
            StopExpertVideo();
            ShowContentPage(ContentPage.Result);
            RefreshResultUI();
        }

        UpdateAllToggleColors();
    }
    #endregion

    #region 메뉴 토글 핸들러
    private void OnSettingsToggleChanged(bool isOn)
    {
        ChunaLogger.Log($"[InfoPanel] 설정 토글: {(isOn ? "ON" : "OFF")}");

        if (isOn)
        {
            SetToggleWithoutNotify(mainMenuToggle, false);
            CloseExitPopupInternal();

            if (settingsPopup != null)
            {
                settingsPopup.SetActive(true);
                isSettingsOpen = true;
            }
        }
        else
        {
            if (settingsPopup != null)
            {
                settingsPopup.SetActive(false);
                isSettingsOpen = false;

                if (practiceSettingsController != null)
                    practiceSettingsController.ForceDisablePatientPositionController();
            }
        }

        UpdateAllToggleColors();
    }

    private void OnMainMenuToggleChanged(bool isOn)
    {
        ChunaLogger.Log($"[InfoPanel] 메인으로 토글: {(isOn ? "ON" : "OFF")}");

        if (isOn)
        {
            SetToggleWithoutNotify(settingsToggle, false);
            CloseSettingsPopupInternal();

            if (exitConfirmPopup != null)
            {
                exitConfirmPopup.SetActive(true);
                isExitPopupOpen = true;
            }
            else
            {
                // 팝업 없으면 바로 로비로
                ReturnToLobby();
            }
        }
        else
        {
            CloseExitPopupInternal();
        }

        UpdateAllToggleColors();
    }

    private void CloseSettingsPopupInternal()
    {
        if (settingsPopup != null && isSettingsOpen)
        {
            settingsPopup.SetActive(false);
            isSettingsOpen = false;
        }
    }

    private void CloseExitPopupInternal()
    {
        if (exitConfirmPopup != null && isExitPopupOpen)
        {
            exitConfirmPopup.SetActive(false);
            isExitPopupOpen = false;
        }
    }
    #endregion

    #region 페이지 전환
    private void ShowContentPage(ContentPage page)
    {
        currentContentPage = page;

        // 모든 페이지 숨김
        if (modeSelectionPage != null) modeSelectionPage.SetActive(false);
        if (skeletonPage != null) skeletonPage.SetActive(false);
        if (expertVideoPage != null) expertVideoPage.SetActive(false);
        if (resultPage != null) resultPage.SetActive(false);

        // 선택된 페이지만 표시
        switch (page)
        {
            case ContentPage.ModeSelection:
                if (modeSelectionPage != null) modeSelectionPage.SetActive(true);
                break;
            case ContentPage.Skeleton:
                if (skeletonPage != null) skeletonPage.SetActive(true);
                break;
            case ContentPage.ExpertVideo:
                if (expertVideoPage != null) expertVideoPage.SetActive(true);
                break;
            case ContentPage.Result:
                if (resultPage != null) resultPage.SetActive(true);
                break;
        }

        ChunaLogger.Log($"[InfoPanel] 페이지 전환: {page}");
    }

    private void SetContentTogglesInteractable(bool interactable)
    {
        if (skeletonToggle != null) skeletonToggle.interactable = interactable;
        if (expertVideoToggle != null) expertVideoToggle.interactable = interactable;
        if (resultToggle != null) resultToggle.interactable = interactable;
    }

    private void SetMenuTogglesInteractable(bool interactable)
    {
        if (settingsToggle != null) settingsToggle.interactable = interactable;
        if (mainMenuToggle != null) mainMenuToggle.interactable = interactable;
    }
    #endregion

    #region 전문가 영상 제어
    private void PlayExpertVideo()
    {
        if (guideVideoController != null)
        {
            guideVideoController.SetGuideEnabled(true);
            ChunaLogger.Log("[InfoPanel] 전문가 영상 재생 시작");
        }
    }

    private void StopExpertVideo()
    {
        if (guideVideoController != null)
        {
            guideVideoController.SetGuideEnabled(false);
            ChunaLogger.Log("[InfoPanel] 전문가 영상 정지");
        }
    }
    #endregion

    #region 결과 UI
    private void RefreshResultUI()
    {
        if (dynamicResultTableUI != null)
        {
            dynamicResultTableUI.ForceRefresh();
            ChunaLogger.Log("[InfoPanel] 동적 결과 테이블 갱신");
        }
    }
    #endregion

    #region 시나리오 이벤트
    private void OnScenarioStarted(ScenarioData scenario)
    {
        isScenarioStarted = true;

        // 콘텐츠 토글 활성화
        SetContentTogglesInteractable(true);

        // 메뉴 토글 활성화 (설정, 메인으로)
        SetMenuTogglesInteractable(true);

        // 근골격 페이지로 자동 전환
        SetToggleWithoutNotify(skeletonToggle, true);
        SetToggleWithoutNotify(expertVideoToggle, false);
        SetToggleWithoutNotify(resultToggle, false);
        ShowContentPage(ContentPage.Skeleton);

        // 시나리오 진행 UI 표시
        if (scenarioProgressUI != null)
            scenarioProgressUI.SetActive(true);

        // ★ UI 위치를 헤드셋 기준으로 초기화
        InitializeUIPositionOnStart();

        UpdateAllToggleColors();

        ChunaLogger.Log("[InfoPanel] 시나리오 시작 - 근골격 페이지로 전환");
    }

    /// <summary>
    /// UI 및 환자 위치를 헤드셋 기준으로 초기화 (시나리오 시작 시)
    /// </summary>
    private void InitializeUIPositionOnStart()
    {
        // ★ 연습 모드(PracticeManager)가 활성화되어 있으면 건너뜀
        // PracticeManager가 이미 Initialize()에서 위치를 설정했으므로 중복 설정 방지
        var practiceManager = FindFirstObjectByType<PracticeManager>();
        if (practiceManager != null && practiceManager.enabled)
        {
            if (showDebugLogs)
                ChunaLogger.Log("[InfoPanel] PracticeManager가 활성화되어 있어 UI 위치 초기화 건너뜀");
            return;
        }

        // UI 위치 초기화
        var uiPositioner = FindFirstObjectByType<ScenarioUIPositioner>();
        if (uiPositioner != null)
        {
            uiPositioner.PositionUIElements();
            if (showDebugLogs)
                ChunaLogger.Log("[InfoPanel] ScenarioUIPositioner로 UI 위치 초기화");
        }

        // ★ 환자 위치도 헤드셋 기준으로 초기화
        InitializePatientPosition();
    }

    /// <summary>
    /// 환자 위치를 프리셋으로 초기화 (PatientPositionManager에 위임)
    /// </summary>
    private void InitializePatientPosition()
    {
        if (patientPositionManager != null)
        {
            string presetName = defaultPositionPreset;
            if (patientPositionManager.ApplyPreset(presetName))
            {
                if (showDebugLogs)
                    ChunaLogger.Log($"[InfoPanel] 환자 위치 프리셋 적용: {presetName}");
            }
            else
            {
                ChunaLogger.LogWarning($"[InfoPanel] 환자 위치 프리셋 적용 실패: {presetName}");
            }
        }
        else
        {
            if (showDebugLogs)
                ChunaLogger.Log("[InfoPanel] PatientPositionManager가 없어 환자 위치 초기화 건너뜀");
        }
    }

    private void OnScenarioCompleted(ScenarioData scenario)
    {
        isScenarioStarted = false;

        // 결과 페이지로 자동 전환
        SetToggleWithoutNotify(skeletonToggle, false);
        SetToggleWithoutNotify(expertVideoToggle, false);
        SetToggleWithoutNotify(resultToggle, true);
        ShowContentPage(ContentPage.Result);
        RefreshResultUI();

        // 시나리오 진행 UI 숨김
        if (scenarioProgressUI != null)
            scenarioProgressUI.SetActive(false);

        UpdateAllToggleColors();

        ChunaLogger.Log("[InfoPanel] 시나리오 완료 - 결과 페이지로 전환");
    }
    #endregion

    #region 토글 컬러
    private void UpdateAllToggleColors()
    {
        // 콘텐츠 토글
        UpdateToggleColor(skeletonToggle, skeletonIcon, IsToggleOn(skeletonToggle));
        UpdateToggleColor(expertVideoToggle, expertVideoIcon, IsToggleOn(expertVideoToggle));
        UpdateToggleColor(resultToggle, resultIcon, IsToggleOn(resultToggle));

        // 메뉴 토글
        UpdateToggleColor(settingsToggle, settingsIcon, isSettingsOpen);
        UpdateToggleColor(mainMenuToggle, mainMenuIcon, isExitPopupOpen);

        // 모드 선택 토글
        UpdateModeSelectionColors();
    }

    private void UpdateToggleColor(Toggle toggle, Image icon, bool isActive)
    {
        if (toggle == null) return;

        // Animation 전환 방식: Selectable 트리거로 제어 (색상 직접 설정 스킵)
        if (toggle.transition == Selectable.Transition.Animation)
        {
            SyncSelectableAnimationTrigger(toggle, isActive);

            // 아이콘만 별도 업데이트 (아이콘은 Animator 제어 대상 아님)
            if (useCustomColors && icon != null)
            {
                icon.color = isActive ? activeColor : inactiveColor;
            }
            return;
        }

        // ColorBlock 기반 토글은 useCustomColors가 true일 때만
        if (useCustomColors)
        {
            Color baseColor = isActive ? activeColor : inactiveColor;
            Color highlightColor = new Color(
                Mathf.Min(baseColor.r * 1.1f, 1f),
                Mathf.Min(baseColor.g * 1.1f, 1f),
                Mathf.Min(baseColor.b * 1.1f, 1f),
                baseColor.a
            );
            Color pressedColor = new Color(
                baseColor.r * 0.9f,
                baseColor.g * 0.9f,
                baseColor.b * 0.9f,
                baseColor.a
            );

            ColorBlock colors = toggle.colors;
            colors.normalColor = baseColor;
            colors.highlightedColor = highlightColor;
            colors.pressedColor = pressedColor;
            colors.selectedColor = baseColor;
            toggle.colors = colors;

            if (toggle.targetGraphic != null)
            {
                toggle.targetGraphic.color = baseColor;
            }

            // 아이콘 색상
            if (icon != null)
            {
                icon.color = isActive ? activeColor : inactiveColor;
            }
        }
    }
    #endregion

    #region Public API
    /// <summary>
    /// 토글 onValueChanged 리스너 모두 제거 (PracticeManager 간섭 방지용)
    /// PracticeManager가 활성화될 때 호출
    /// </summary>
    public void DisableToggleListeners()
    {
        // 콘텐츠 토글 리스너 제거 (애니메이션 간섭 방지)
        if (skeletonToggle != null) skeletonToggle.onValueChanged.RemoveAllListeners();
        if (expertVideoToggle != null) expertVideoToggle.onValueChanged.RemoveAllListeners();
        if (resultToggle != null) resultToggle.onValueChanged.RemoveAllListeners();
        // ★ settingsToggle, mainMenuToggle: 리스너 유지 (팝업 열기/닫기 기능 필요)
        // 모드 선택 토글 리스너 제거
        if (practiceToggle != null) practiceToggle.onValueChanged.RemoveAllListeners();
        if (evaluationToggle != null) evaluationToggle.onValueChanged.RemoveAllListeners();
        if (beginnerToggle != null) beginnerToggle.onValueChanged.RemoveAllListeners();
        if (intermediateToggle != null) intermediateToggle.onValueChanged.RemoveAllListeners();
        if (advancedToggle != null) advancedToggle.onValueChanged.RemoveAllListeners();
        ChunaLogger.Log("[InfoPanel] 콘텐츠/모드 토글 리스너 제거됨 (PracticeManager 모드)");
    }

    public void CloseAllPopups()
    {
        CloseSettingsPopupInternal();
        CloseExitPopupInternal();
        SetToggleWithoutNotify(settingsToggle, false);
        SetToggleWithoutNotify(mainMenuToggle, false);
        UpdateAllToggleColors();
    }

    public void CloseSettingsPopup()
    {
        SetToggleWithoutNotify(settingsToggle, false);
        OnSettingsToggleChanged(false);
    }

    public void CloseExitPopup()
    {
        SetToggleWithoutNotify(mainMenuToggle, false);
        OnMainMenuToggleChanged(false);
    }

    /// <summary>
    /// Exit 확인 (ExitPopupController에서 호출)
    /// </summary>
    public void OnExitConfirm()
    {
        CloseExitPopup();
        ReturnToLobby();
    }

    /// <summary>
    /// Exit 취소 (ExitPopupController에서 호출)
    /// </summary>
    public void OnExitCancel()
    {
        CloseExitPopup();
    }

    private void ReturnToLobby()
    {
        ChunaLogger.Log("[InfoPanel] 로비로 이동...");
        SceneLoader.LoadScene("Lobby", useLoadingScene: true);
    }

    public void ShowSkeletonPage()
    {
        if (!isScenarioStarted) return;
        SetToggleWithoutNotify(skeletonToggle, true);
        OnSkeletonToggleChanged(true);
    }

    /// <summary>
    /// 연습 모드용 콘텐츠 토글 상태 초기화 (페이지 전환 없음)
    /// 모드 선택 페이지는 그대로 유지하고, 콘텐츠 토글 상태만 골격으로 설정
    /// </summary>
    public void ForceShowSkeletonPage()
    {
        // 콘텐츠 토글 상태만 설정 (페이지 전환 안 함 - 모드 선택 페이지 유지)
        SetToggleWithoutNotify(skeletonToggle, true);
        SetToggleWithoutNotify(expertVideoToggle, false);
        SetToggleWithoutNotify(resultToggle, false);
        // ShowContentPage(ContentPage.Skeleton); // ★ 제거: 모드 선택 페이지 유지
        UpdateAllToggleColors();

        // ★ 딜레이 후 토글 상태 재확인 (Animator 초기화 순서 문제 해결)
        StartCoroutine(ReapplySkeletonToggleStateDelayed());

        if (showDebugLogs)
            ChunaLogger.Log("[InfoPanel] 연습모드: 콘텐츠 토글 상태 초기화 (골격 선택, 페이지 유지)");
    }

    /// <summary>
    /// 딜레이 후 골격 토글 상태 재적용 (Animator 초기화 순서 문제 해결)
    /// </summary>
    private IEnumerator ReapplySkeletonToggleStateDelayed()
    {
        // 2프레임 대기 (Animator 초기화 완료 대기)
        yield return null;
        yield return null;

        // ★ 연습 모드에서는 골격 토글이 항상 ON 상태여야 함
        // (다른 코드가 중간에 바꿀 수 있으므로 명시적으로 재설정)
        if (skeletonToggle != null)
        {
            // 토글 상태를 다시 ON으로 강제 설정
            skeletonToggle.SetIsOnWithoutNotify(true);
            SyncSelectableAnimationTrigger(skeletonToggle, true);

            if (showDebugLogs)
                ChunaLogger.Log("[InfoPanel] 골격 토글 Animator 재적용: Selected 트리거 (강제)");
        }

        // 다른 콘텐츠 토글은 OFF
        ForceSetToggleOff(expertVideoToggle);
        ForceSetToggleOff(resultToggle);

        UpdateAllToggleColors();
    }

    /// <summary>
    /// 토글을 명시적으로 OFF 상태로 강제 설정
    /// </summary>
    private void ForceSetToggleOff(Toggle toggle)
    {
        if (toggle == null) return;

        toggle.SetIsOnWithoutNotify(false);
        SyncSelectableAnimationTrigger(toggle, false);
    }

    /// <summary>
    /// 토글의 Animator 상태를 isOn 값과 일치시킴
    /// </summary>
    private void ReapplyToggleAnimatorState(Toggle toggle)
    {
        if (toggle == null) return;
        SyncSelectableAnimationTrigger(toggle, toggle.isOn);
    }

    /// <summary>
    /// 연습 모드용 콘텐츠 페이지 전환 (시나리오 시작 여부 무관)
    /// Step 2 완료 후 골격 페이지로 전환할 때 사용
    /// </summary>
    public void ShowContentPageForPractice(ContentPage page)
    {
        // 콘텐츠 토글 상태 설정
        SetToggleWithoutNotify(skeletonToggle, page == ContentPage.Skeleton);
        SetToggleWithoutNotify(expertVideoToggle, page == ContentPage.ExpertVideo);
        SetToggleWithoutNotify(resultToggle, page == ContentPage.Result);

        // 페이지 전환
        ShowContentPage(page);
        UpdateAllToggleColors();

        if (showDebugLogs)
            ChunaLogger.Log($"[InfoPanel] 연습모드: {page} 페이지로 전환");
    }

    // showDebugLogs 속성 추가 (내부 로깅용)
    private bool showDebugLogs = true;

    public void ShowExpertVideoPage()
    {
        if (!isScenarioStarted) return;
        SetToggleWithoutNotify(expertVideoToggle, true);
        OnExpertVideoToggleChanged(true);
    }

    public void ShowResultPage()
    {
        if (!isScenarioStarted) return;
        SetToggleWithoutNotify(resultToggle, true);
        OnResultToggleChanged(true);
    }

    public void ShowModeSelectionPage()
    {
        SetContentTogglesInteractable(false);
        SetToggleWithoutNotify(skeletonToggle, false);
        SetToggleWithoutNotify(expertVideoToggle, false);
        SetToggleWithoutNotify(resultToggle, false);
        StopExpertVideo();
        ShowContentPage(ContentPage.ModeSelection);
        UpdateAllToggleColors();
    }

    public void ResetModeSelection()
    {
        InitializeModeSelection();
        UpdateModeSelectionColors();
    }

    public void SetScenarioTitle(string title)
    {
        if (scenarioTitleText != null)
            scenarioTitleText.text = title;
    }

    /// <summary>
    /// 연습 모드에서 난이도를 외부에서 설정할 때 사용 (리스너 트리거 방지)
    /// </summary>
    public void SetDifficultyForPractice(DifficultyLevel difficulty)
    {
        selectedDifficulty = difficulty;

        // 토글 상태도 업데이트 (리스너 트리거 안 함)
        SetToggleWithoutNotify(beginnerToggle, difficulty == DifficultyLevel.Beginner);
        SetToggleWithoutNotify(intermediateToggle, difficulty == DifficultyLevel.Intermediate);
        SetToggleWithoutNotify(advancedToggle, difficulty == DifficultyLevel.Advanced);

        // 시각 상태 업데이트
        UpdateModeSelectionColors();
        UpdateDifficultyDescriptions();

        if (showDebugLogs)
            ChunaLogger.Log($"[InfoPanel] 연습모드 난이도 설정: {difficulty}");
    }

    /// <summary>
    /// ScenarioBootstrapper에서 호출 — 환자 위치 프리셋을 외부에서 설정
    /// </summary>
    public void SetDefaultPositionPreset(string presetName)
    {
        if (!string.IsNullOrEmpty(presetName))
        {
            defaultPositionPreset = presetName;
            if (showDebugLogs)
                ChunaLogger.Log($"[InfoPanel] 기본 환자 위치 프리셋 설정: {presetName}");
        }
    }

    // Properties
    public ContentPage CurrentPage => currentContentPage;
    public bool IsSettingsOpen => isSettingsOpen;
    public bool IsExitPopupOpen => isExitPopupOpen;
    public bool IsScenarioStarted => isScenarioStarted;
    public ModeType SelectedMode => selectedMode;
    public DifficultyLevel SelectedDifficulty => selectedDifficulty;
    #endregion

    #region 유틸리티
    private void SetToggleWithoutNotify(Toggle toggle, bool value)
    {
        if (toggle == null) return;

        toggle.SetIsOnWithoutNotify(value);

        // ★ Animation 전환 토글: Selectable 트리거 동기화
        // (SetIsOnWithoutNotify는 DoStateTransition을 호출하지 않으므로 수동 트리거 필요)
        SyncSelectableAnimationTrigger(toggle, value);
    }

    /// <summary>
    /// Animation 전환 토글의 Animator 상태 동기화.
    /// Meta SDK 토글은 AnimatorOverrideLayerWeigth가 onValueChanged를 통해
    /// Selected Layer weight를 제어하지만, SetIsOnWithoutNotify()는 onValueChanged를
    /// 발동하지 않고, DisableToggleListeners()로 리스너가 제거될 수 있으므로
    /// layer weight를 직접 설정해야 함.
    /// </summary>
    private void SyncSelectableAnimationTrigger(Toggle toggle, bool isOn)
    {
        if (toggle == null) return;

        var animator = toggle.GetComponent<Animator>();
        if (animator == null || !animator.isActiveAndEnabled) return;

        // Selected Layer(index 1) weight 직접 설정
        // (AnimatorOverrideLayerWeigth.SetOverrideLayerActive 대체)
        if (animator.layerCount > 1)
        {
            animator.SetLayerWeight(1, isOn ? 1f : 0f);
        }
    }

    private bool IsToggleOn(Toggle toggle)
    {
        return toggle != null && toggle.isOn;
    }
    #endregion
}
