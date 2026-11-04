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
    [Header("═══ 모드 선택 (실습/평가/실측) ═══")]
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

    [Tooltip("정보 디스플레이 펼침(2026-10-27). 비우면 종전대로 한 번에 한 페이지만 보인다(배타). " +
             "채우면 골격 · 영상 · 결과를 동시에 띄우고, 최근 것이 정면에 온다 — 페이지 켜고 끄는 규칙은 그대로 여기서 쥔다.")]
    [SerializeField] private InfoWindowArranger windowArranger;

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

    #region 녹화/설정 전용 모드
    [Header("═══ 녹화/설정 전용 모드 ═══")]
    [Tooltip("시작 시 설정 팝업(녹화 UI)을 메인으로 표시. 시나리오 없는 HandRecord 빌드용")]
    [SerializeField] private bool startInSettingsMode = false;
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
        Evaluation,
        // ★2026-08-31 Measurement 제거 — ROM에서는 <b>평가모드가 곧 실측</b>이라 별도 모드가 필요 없다.
        //   진행 방식(교육/실측)은 ChunaTraining.ScenarioMode가 따로 들고 있고,
        //   갈림길은 ScenarioConfig.measurementPhases 유무 하나뿐이다.
        //   ※새 값은 반드시 끝에 붙일 것 — 씬에 int로 직렬화된다.
    }

    // 난이도 타입 - ChunaTraining.DifficultyLevel 사용 (중복 제거)

    // 이벤트
    public event Action<ModeType, DifficultyLevel> OnModeSelected;
    public event Action OnSimulationStarted;

    #region Unity Lifecycle
    void Awake()
    {
        // 골격 페이지 좌상단의 '실습 소요 시간' 표시 (2026-09-08).
        // ★씬에 올려 두면 여기서 안 붙인다 — 인스펙터로 여백·글씨를 맞추려면 씬에 올려야 한다
        //   (런타임에 붙인 것은 Play를 멈추면 사라져 맞춘 값이 저장되지 않는다).
        if (FindFirstObjectByType<PracticeElapsedLabel>(FindObjectsInactive.Include) == null)
            gameObject.AddComponent<PracticeElapsedLabel>();

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

        cachedPracticeManager = FindFirstObjectByType<PracticeManager>();
    }

    private PracticeManager cachedPracticeManager;

    void Start()
    {
        InitializePanel();
        SetupToggleListeners();
        SetupModeSelectionListeners();
        UpdateAllToggleColors();

        // 2026-10-27: 로비 모드 팝업에서 이미 골랐으면 모드 선택 페이지를 건너뛴다(없으면 종전대로)
        if (!startInSettingsMode && LobbyModeChoice.TryConsume(out var lobbyMode, out var lobbyDifficulty))
            StartCoroutine(ApplyLobbyModeChoice(lobbyMode, lobbyDifficulty));
    }

    /// <summary>
    /// 로비에서 고른 모드로 바로 시작한다 — 사람이 모드 페이지에서 누른 것과 <b>같은 함수</b>(OnModeToggleChanged)를 탄다.
    /// ★ScenarioConfig가 물린 뒤에 불러야 실측 여부 판단이 맞다(ScenarioSupportsMeasurement) — 최대 3초 기다린다.
    /// </summary>
    private System.Collections.IEnumerator ApplyLobbyModeChoice(ModeType mode, DifficultyLevel difficulty)
    {
        float until = Time.realtimeSinceStartup + 3f;
        while (Time.realtimeSinceStartup < until)
        {
            if (scenarioManager == null) scenarioManager = FindFirstObjectByType<ScenarioManager>();
            if (scenarioManager != null && scenarioManager.CurrentConfig != null) break;
            yield return null;
        }
        if (scenarioManager == null || scenarioManager.CurrentConfig == null)
            ChunaLogger.LogWarning("[InfoPanel] 로비 모드 적용 — 3초 안에 ScenarioConfig가 안 물렸다. 그대로 진행한다(실측 판단이 틀릴 수 있음)");

        // ★11-04: 펼침(창 3개) 구성이면 바로 시작하지 않고 개요 화면에서 [시작]을 기다린다 — 창 배치기가 준비될 때까지 최대 3초 더 본다
        float untilWin = Time.realtimeSinceStartup + 3f;
        while (!MultiWindow && windowArranger != null && Time.realtimeSinceStartup < untilWin) yield return null;
        bool overview = MultiWindow;

        ChunaLogger.Log($"[InfoPanel] 로비에서 고른 모드로 {(overview ? "개요 화면 → [시작] 대기" : "바로 시작(펼침 아님)")}: {mode} · {difficulty}");
        if (mode == ModeType.Practice) SetDifficultyForPractice(difficulty);
        SetToggleWithoutNotify(practiceToggle, mode == ModeType.Practice);
        SetToggleWithoutNotify(evaluationToggle, mode == ModeType.Evaluation);
        OnModeToggleChanged(mode, autoStart: !overview);
        if (overview) EnterOverview();

        // ★10-30 사용자 «환자 위치가 원래 최초에 자동 맞춤 되게 되어 있었는데, 모드 선택이 로비로 가면서 없어졌다»
        //   종전엔 실습 안 모드 페이지 → 시나리오 시작 → InitializeUIPositionOnStart가 '맞춤 설정'을 1회 불렀다.
        //   로비 경로에서도 한 번 부른다(이미 불렸으면 customPositioningApplied가 막는다).
        //   추적 전 머리 값으로 맞추지 않게 씬 로드 1.5초 뒤에 부른다(10-29 «추적 전 머리» 사고와 같은 이유)
        while (!TrackingReady.Ready()) yield return null;
        bool before = customPositioningApplied;
        ApplyCustomPositioningOnce();
        Debug.Log($"[InfoPanel] 로비 경로 자동 맞춤 — {(before ? "이미 불렸음(건너뜀)" : customPositioningApplied ? "1회 부름" : "못 부름(PracticeSettingsController 없음)")} · {Time.timeSinceLevelLoad:F1}s");
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
        // 녹화/설정 전용 모드 분기 — HandRecord 빌드 등
        if (startInSettingsMode)
        {
            InitializeAsSettingsMain();
            return;
        }

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

    /// <summary>
    /// HandRecord 등 시나리오 없는 빌드용 — 설정 팝업(녹화 UI)을 메인으로 띄움.
    /// 모드 선택/시나리오 페이지는 모두 꺼지고, 설정 토글이 켜진 상태로 시작.
    /// </summary>
    private void InitializeAsSettingsMain()
    {
        // 콘텐츠 페이지 모두 끔
        if (modeSelectionPage != null) modeSelectionPage.SetActive(false);
        if (skeletonPage != null) skeletonPage.SetActive(false);
        if (expertVideoPage != null) expertVideoPage.SetActive(false);
        if (resultPage != null) resultPage.SetActive(false);

        // 시나리오 진행 UI 숨김
        if (scenarioProgressUI != null)
            scenarioProgressUI.SetActive(false);

        // 설정 팝업 켜기 (= 녹화 UI)
        if (settingsPopup != null)
            settingsPopup.SetActive(true);

        if (exitConfirmPopup != null)
            exitConfirmPopup.SetActive(false);

        // 콘텐츠 토글은 비활성 (사용 안 함)
        SetToggleWithoutNotify(skeletonToggle, false);
        SetToggleWithoutNotify(expertVideoToggle, false);
        SetToggleWithoutNotify(resultToggle, false);
        SetContentTogglesInteractable(false);

        // 설정 토글은 켜진 상태로 시작 + 클릭 가능하게
        SetToggleWithoutNotify(settingsToggle, true);
        SetToggleWithoutNotify(mainMenuToggle, false);
        if (settingsToggle != null) settingsToggle.interactable = true;
        if (mainMenuToggle != null) mainMenuToggle.interactable = true;

        currentContentPage = ContentPage.None;
        isSettingsOpen = true;
        isExitPopupOpen = false;

        ChunaLogger.Log("[InfoPanel] ✅ 녹화/설정 메인 모드로 초기화 (settingsPopup 활성)");
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
    private void OnModeToggleChanged(ModeType mode, bool autoStart = true)
    {
        selectedMode = mode;
        ChunaLogger.Log($"[InfoPanel] 모드 선택: {mode}");

        // ★ DifficultyManager 동기화: 평가 모드는 난이도 토글 무시하고 Evaluation 강제,
        //    실습 모드는 현재 선택된 난이도 반영 (이전엔 난이도 토글 변경 시에만 set돼서 평가모드가 동작 안 했음)
        // ★★2026-09-11 재정의(09-10 XR-ROM 2차 회의) — <b>실측은 별도 시나리오</b>다.
        //   종전(08-31)엔 "[평가] = 실측"이었다. 이제 measurementPhases를 가진 config(= 경추ROM실측)는
        //   <b>어느 버튼을 눌러도</b> 실측이고, 가상환자 경추ROM(경추ROM측정)은 measurementPhases를 비워
        //   [평가]가 다른 12개 술기처럼 교육 진행 + 채점으로 간다.
        //   갈림길은 여전히 measurementPhases 유무 하나다 — 시나리오 이름을 코드에 박지 않는다.
        bool useMeasurement = ScenarioSupportsMeasurement();

        // ★실측이면 모드를 [평가]로 맞춘다. 결과의 Mode 열이 종전 실측 기록('평가')과 같아야
        //   서버 쪽 집계가 갈리지 않는다. 토글 표시도 같이 맞춘다 — 안 맞추면 화면과 기록이 다른 말을 한다.
        if (useMeasurement && mode != ModeType.Evaluation)
        {
            ChunaLogger.Log($"[InfoPanel] 실측 전용 시나리오 — '{mode}'를 눌렀지만 실측(평가)으로 진행한다");
            mode = ModeType.Evaluation;
            selectedMode = mode;
            SetToggleWithoutNotify(practiceToggle, false);
            SetToggleWithoutNotify(evaluationToggle, true);
        }

        if (DifficultyManager.Instance != null)
        {
            // 진행 방식(교육/실측)은 난이도와 별개 축이다. ★시나리오 로드 전에 정해져야 반영된다.
            DifficultyManager.Instance.SetMode(useMeasurement
                ? ScenarioMode.Measurement
                : ScenarioMode.Education);

            // ★실측은 훈련이 아니라 측정 도구라 난이도가 무의미하다. 초급으로 고정한다 —
            //   실측 나레이션이 Beginner·Intermediate에만 있고 Advanced·Evaluation에는 0개라,
            //   평가 난이도로 두면 절차 안내가 통째로 무음이 된다(2026-08-31 실측).
            DifficultyLevel target =
                  useMeasurement               ? DifficultyLevel.Beginner
                : mode == ModeType.Evaluation  ? DifficultyLevel.Evaluation
                : selectedDifficulty;
            DifficultyManager.Instance.SetDifficulty(target);
        }

        // ★패스스루를 여기서 켜지 않는다. 실측 진입/이탈은 CervicalRomMeasurementBridge가
        //   한 곳에서 대칭으로 처리한다 — 켠 것은 켠 쪽이 되돌려야 상태가 안 샌다.

        UpdateModeSelectionColors();
        OnModeSelected?.Invoke(selectedMode, selectedDifficulty);

        // 모드 선택 시 자동으로 시나리오 시작 — ★11-04 로비 경로 + 펼침이면 개요 화면의 [시작]이 부른다(BeginFromOverview)
        if (autoStart) StartSimulation();
    }

    #region 실습 개요 → [시작] (2026-11-04)
    // 사용자 피드백 «실습 시작 버튼 전 개요화면 필요» · «시작(누르면 실습 수행 시간 타이머로 바뀜)» · «실습씬 진입시 최초에 골격 현황 영상 패널 전부 on».
    // ★펼침(MultiWindow) + 로비 경로에서만 탄다. 옛 단일 패널 · 실습 안 모드 페이지 경로는 종전 그대로(모드를 누르면 바로 시작).
    // ★타이머는 따로 세지 않는다 — StartSimulation → ScenarioManager.StartScenario → TrainingResultTracker.StartTracking이 시간의 출처다.
    private bool awaitingStart, positionedAtOverview;
    private PracticeOverviewPanel overviewPanel;

    /// <summary>개요 화면이 떠 있고 [시작]을 기다리는 중인가. 상단 [시작] 버튼이 본다.</summary>
    public bool IsAwaitingStart => awaitingStart;

    private void EnterOverview()
    {
        awaitingStart = true;

        // ★위치 초기화(UI 배치 → 환자 프리셋 → 맞춤 설정)는 종전엔 시나리오 시작 때 돌았다. 개요에서 사용자가 위치를 고친 뒤
        //   [시작]에서 다시 돌면 그 조정이 날아간다 → 같은 함수를 지금(개요 진입) 한 번 돌리고, 시작 때는 건너뛴다.
        InitializeUIPositionOnStart();
        positionedAtOverview = true;

        // 창 3개를 모두 연다 — 여는 순서 = 자리(정면 → 왼쪽 → 오른쪽 · InfoWindowArranger 고정 칸) = 상단 버튼 순서(근골격 · 진행 현황 · 전문가 영상)의 가운데 · 왼쪽 · 오른쪽
        OpenAllContentPages();

        // [시작] 전에도 설정(환자 위치 등)은 쓸 수 있어야 한다 — 종전엔 모드를 고르면 곧바로 시작해서 이 상태가 없었다
        if (settingsToggle != null) settingsToggle.interactable = true;

        ScenarioData preview = scenarioManager != null ? scenarioManager.LoadFilteredScenario() : null;

        // 전문가 영상 — 시나리오 시작 전에는 GuideVideoController가 영상을 안 건다(OnScenarioStarted에서 건다). 개요 화면에서도 보이게 먼저 건다.
        if (guideVideoController != null && preview != null)
        {
            bool loaded = guideVideoController.LoadVideoByName(preview.scenarioName);
            if (loaded) PlayExpertVideo();
            ChunaLogger.Log($"[InfoPanel] 개요 — 전문가 영상 '{preview.scenarioName}' {(loaded ? "걸고 전체 재생" : "★없음")}");
        }

        if (overviewPanel == null) overviewPanel = gameObject.AddComponent<PracticeOverviewPanel>();
        overviewPanel.Show(resultPage, preview, scenarioManager != null ? scenarioManager.CurrentConfig : null);

        UpdateAllToggleColors();
        Debug.Log($"[InfoPanel] 개요 화면 · [시작] 대기 · 단계 {(preview != null ? preview.phases.Count : -1)}개 · 창 근골격 {Active(skeletonPage)} · 진행 {Active(resultPage)} · 영상 {Active(expertVideoPage)}");
    }

    /// <summary>상단 [시작]. 개요를 걷고 시나리오를 시작한다(시간 기록도 여기서 시작된다).</summary>
    public void BeginFromOverview()
    {
        if (!awaitingStart) return;
        awaitingStart = false;
        if (overviewPanel != null) overviewPanel.Hide();
        Debug.Log("[InfoPanel] [시작] — 개요를 걷고 시나리오 시작");
        StartSimulation();
        StartCoroutine(SkipOpeningGuideStep());
    }

    /// <summary>
    /// ★11-04 사용자 «애초에 시작을 누르기 전 개요에서 설정에 관한 것까지 같이 하고 넘어갈 거야 · 시작 두 번은 필요 없어».
    ///   CSV 첫 단계(«시작/가이드» — «필요시 [설정]으로 환자 위치를 맞추고 [시작]을 누르세요»)는 개요가 대신했다 → 상단 [시작]이 그 단계를 바로 넘긴다.
    ///   넘기는 길 = 정면 시작 토글을 켜는 것(사람이 누른 것과 같은 경로 · 규칙 9). CSV는 그대로 둔다(13개 술기 공용 · 개요 없는 경로는 종전대로).
    ///   ★첫 phase의 첫 단계가 가이드 단계일 때만 — 아니면 손대지 않는다.
    /// </summary>
    private System.Collections.IEnumerator SkipOpeningGuideStep()
    {
        yield return null;   // 시작 이벤트(단계 · 토글 초기화)가 다 돈 뒤
        var sm = scenarioManager;
        var guideUI = FindFirstObjectByType<ScenarioGuideUIController>();
        var scen = sm != null ? sm.CurrentScenario : null;
        bool opening = scen != null && scen.phases.Count > 0 && sm.CurrentPhase == scen.phases[0]
                       && sm.CurrentStep != null && sm.CurrentStep.IsGuideStep() && sm.CurrentStep == scen.phases[0].steps[0];
        if (!opening || guideUI == null || guideUI.NextToggle == null)
        {
            Debug.Log($"[InfoPanel] [시작] 첫 안내 단계 건너뛰기 안 함 — 첫 단계가 가이드 아님 또는 토글 없음(가이드 {(sm?.CurrentStep != null && sm.CurrentStep.IsGuideStep())} · 토글 {(guideUI?.NextToggle != null)})");
            yield break;
        }
        Debug.Log($"[InfoPanel] [시작] 첫 안내 단계 '{sm.CurrentStep.stepName}' 건너뜀(개요가 대신함) → 정면 시작 토글 켬");
        guideUI.NextToggle.isOn = true;
    }

    /// <summary>펼침에서 창 3개를 모두 연다(이미 열려 있으면 그대로).</summary>
    private void OpenAllContentPages()
    {
        if (!MultiWindow) return;
        if (modeSelectionPage != null) modeSelectionPage.SetActive(false);
        foreach (var p in new[] { resultPage, skeletonPage, expertVideoPage })
            if (p != null && !p.activeSelf) SetMultiPage(p, true);
        SyncContentTogglesToPages();
    }

    // ★activeSelf로 잰다 — 판(창)은 InfoWindowArranger가 다음 프레임에 켜서, activeInHierarchy로 재면 켰는데도 «끔»으로 찍혔다(11-04 Editor.log 실측 · 영상엔 셋 다 보임)
    private static string Active(GameObject g) => g == null ? "없음" : g.activeSelf ? "켬" : "끔";
    #endregion

    /// <summary>
    /// 지금 로드된 술기가 실측 진행을 지원하는가. <c>ScenarioConfig.measurementPhases</c>가 근거다.
    ///
    /// ★<c>CurrentScenario</c>가 아니라 <c>CurrentConfig</c>를 본다 — 모드 선택은
    ///   <c>StartSimulation()</c> <b>이전</b>이라 시나리오가 아직 로드되지 않았다.
    ///   config는 ScenarioBootstrapper가 씬 시작 때 이미 물려 놓는다.
    /// </summary>
    private bool ScenarioSupportsMeasurement()
    {
        if (scenarioManager == null) scenarioManager = FindFirstObjectByType<ScenarioManager>();
        ScenarioConfig config = scenarioManager != null ? scenarioManager.CurrentConfig : null;
        if (config == null)
        {
            ChunaLogger.LogWarning("[InfoPanel] ScenarioConfig를 못 찾아 실측 지원 여부를 판단할 수 없습니다 — 교육 진행으로 갑니다.");
            return false;
        }
        return config.SupportsMeasurementMode;
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
        // 평가 모드는 난이도 무관 → 빈값 (Mode 컬럼에서 "평가"로 구분됨)
        if (selectedMode == ModeType.Evaluation) return "";

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
        // ★11-04 사용자 «패널은 상시 on · 상단 메뉴로 on/off 토글 x» — 펼침에서는 끄라는 신호를 무시하고 창을 그대로 둔다
        if (MultiWindow) { if (isOn) SetMultiPage(skeletonPage, true); SyncContentTogglesToPages(); return; }

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
        if (MultiWindow)
        {
            // ★11-04 창은 늘 켜져 있다 — 끄라는 신호로 창을 닫거나 영상을 멈추지 않는다(미디어 메뉴 켜기/끄기는 ExpertVideoControlBar)
            if (isOn) { SetMultiPage(expertVideoPage, true); PlayExpertVideo(); }
            SyncContentTogglesToPages();
            return;
        }

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
            var practiceManager = cachedPracticeManager;
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
        if (MultiWindow)
        {
            // ★11-04 창은 늘 켜져 있다
            if (isOn) { SetMultiPage(resultPage, true); RefreshResultUI(); }
            SyncContentTogglesToPages();
            return;
        }

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

    /// <summary>
    /// 팝업을 <b>바깥에서</b> 닫았을 때(취소 버튼) 이 패널의 상태를 맞춘다 — 2026-09-15.
    ///
    /// ★왜 필요한가: 이 팝업은 <c>mainMenuToggle</c>이 <b>켜져 있는 동안</b> 열려 있는 구조다.
    ///   그런데 취소는 <c>ExitPopupController</c>가 처리하면서 <b>패널만</b> 껐다.
    ///   토글은 ON으로 남아 다음에 누르면 OFF가 되어(= 닫기) 아무 일도 안 일어나고,
    ///   <b>두 번 눌러야 다시 열리는</b> 상태가 됐다.
    /// ★알림 없이 끈다 — <c>onValueChanged</c>가 돌면 CloseExitPopupInternal이 또 불려 겹친다.
    /// </summary>
    public void NotifyExitPopupClosedExternally()
    {
        // ★토글을 그냥 끈다. 나머지는 <b>기존 경로가 그대로 한다</b> —
        //   OnMainMenuToggleChanged(false) → CloseExitPopupInternal() → 패널 끄기 · 상태 내리기 · 색 갱신.
        //   따로 흉내 내면 손잡이가 둘이 되어 또 어긋난다.
        if (mainMenuToggle != null && mainMenuToggle.isOn) mainMenuToggle.isOn = false;
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

        if (MultiWindow && page != ContentPage.ModeSelection && page != ContentPage.None)
        {
            if (modeSelectionPage != null) modeSelectionPage.SetActive(false);
            GameObject target = page == ContentPage.Skeleton ? skeletonPage : page == ContentPage.ExpertVideo ? expertVideoPage : resultPage;
            SetMultiPage(target, true);
            // ★부르는 쪽이 다른 콘텐츠 토글을 꺼 두었어도 판은 열려 있다 — 토글을 판 상태에 다시 맞춘다
            SyncContentTogglesToPages();
            ChunaLogger.Log($"[InfoPanel] 페이지 표시(펼침): {page}");
            return;
        }

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

    // ── 경추ROM 실측 전용 접근구 (2026-09-04) ─────────────────────────────
    // 사용자: "결과창을 현재 진행창으로 바꿔서, 모드 선택한 후에 진행창을 띄워서 보여줘."
    //
    // ★<b>ROM 실측에서만</b> 쓴다(사용자 확정). 13개 술기가 이 화면을 공유하므로
    //   여기서 흐름을 바꾸지 않는다 — 밖에서 부를 <b>수단</b>만 연다.
    //   종전 흐름(모드 선택 → 근골격 페이지)은 그대로 둔다.

    /// <summary>결과 페이지의 루트. ROM 실측이 여기에 진행Root를 넣는다.</summary>
    public GameObject ResultPageObject => resultPage;

    /// <summary>
    /// 근골격(골격) 페이지. <see cref="PracticeElapsedLabel"/>이 좌상단에 소요 시간을 얹는다.
    /// ★<b>여는 것은 창뿐이다</b> — 페이지를 켜고 끄는 규칙은 이 클래스가 그대로 쥐고 있다.
    ///   13개 술기가 같이 쓰는 패널이라, 표시물을 더하는 일은 부르는 쪽에서 한다.
    /// </summary>
    public GameObject SkeletonPageObject => skeletonPage;

    /// <summary>전문가 영상 페이지. <see cref="ExpertVideoSubtitle"/>가 이 안의 영상 칸 아래에 자막을 얹는다(11-04).</summary>
    public GameObject ExpertVideoPageObject => expertVideoPage;

    /// <summary>
    /// 결과 페이지를 띄운다. ROM 실측에서는 이 페이지가 <b>'현재 진행'</b> 노릇을 한다.
    /// ★토글 상태도 같이 맞춘다 — 안 맞추면 화면과 버튼이 서로 다른 말을 한다.
    /// </summary>
    public void ShowResultPageExternally(bool refreshTable = false)
    {
        SetToggleWithoutNotify(skeletonToggle, false);
        SetToggleWithoutNotify(expertVideoToggle, false);
        SetToggleWithoutNotify(resultToggle, true);
        ShowContentPage(ContentPage.Result);
        if (refreshTable) RefreshResultUI();
        UpdateAllToggleColors();
    }

    /// <summary>
    /// 설정·메인으로 팝업 중 하나라도 열려 있는가(2026-09-04).
    /// ★ROM 실측이 이걸 보고 진행창을 접는다 — 팝업 앞을 가려 버튼이 안 눌렸다.
    /// </summary>
    public bool IsAnyPopupOpen
        => (settingsPopup != null && settingsPopup.activeSelf)
        || (exitConfirmPopup != null && exitConfirmPopup.activeSelf);

    /// <summary>지금 결과 페이지가 떠 있는가.</summary>
    public bool IsResultPageShown => currentContentPage == ContentPage.Result;

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

        // ★★<b>실측(ROM 평가)이면 결과 페이지로 시작한다</b>(2026-09-04 회의 지시).
        //   그 페이지가 실측에서는 '현재 진행' 창 노릇을 한다 — 진행Root가 그 자리에 얹힌다.
        //   사용자: "시작할 때 골격 말고 결과창(지금은 진행 단계창)으로 바꾸라니까."
        //
        // ★<b>실측일 때만</b> 갈린다. 나머지 12개 술기는 종전대로 근골격 페이지다.
        //   바로 아래 ShowProgressBar와 같은 DifficultyManager를 본다 — 새 배선이 없다.
        //   ★실측 모드는 경추ROM에만 있다(measurementPhases를 가진 시나리오가 그것 하나다).
        // ★★10-29 사용자 지시로 <b>13개 술기 모두</b> 결과 페이지(= «진행 현황», 진행 가이드가 그 창 위에 겹쳐 그려진다 — ProgressGuideDock)로 시작한다.
        //   «처음에 켜지는 화면은 수행결과를 진행 현황으로 바꿔서 켜지게». 종전(09-04 ~ 10-28) = 실측만 결과 · 나머지는 근골격.
        //   ★펼침(MultiWindow)이 아니면 종전 그대로 둔다 — 옛 단일 패널에선 진행 가이드가 따로 떠 있다.
        bool measurement = ChunaTraining.DifficultyManager.Instance != null
                           && ChunaTraining.DifficultyManager.Instance.IsMeasurementMode;
        bool startWithProgress = measurement || MultiWindow;

        SetToggleWithoutNotify(skeletonToggle, !startWithProgress);
        SetToggleWithoutNotify(expertVideoToggle, false);
        SetToggleWithoutNotify(resultToggle, startWithProgress);
        ShowContentPage(startWithProgress ? ContentPage.Result : ContentPage.Skeleton);

        // ★11-04 펼침이면 창 3개가 늘 켜져 있다(개요를 거치지 않은 경로 — 실습 안 모드 페이지 · 다시하기 — 도 같게) · 영상도 켠 채로 둔다
        if (MultiWindow)
        {
            awaitingStart = false;
            if (overviewPanel != null) overviewPanel.Hide();
            OpenAllContentPages();
            PlayExpertVideo();
        }

        // 시나리오 진행 UI 표시 (난이도 설정에 따라)
        if (scenarioProgressUI != null)
        {
            bool showProgress = ChunaTraining.DifficultyManager.Instance == null
                || ChunaTraining.DifficultyManager.Instance.ShowProgressBar;
            scenarioProgressUI.SetActive(showProgress);
        }

        // ★ UI 위치를 헤드셋 기준으로 초기화
        // ★11-04 사용자 «시작 누를 때 위치 초기화돼서 내가 설정한 게 날아간다» — 실측(Editor.log): [시작] 뒤 프리셋 lying_2가
        //   환자 · 침대 · 카메라를 다시 놓았다. 개요 경로에서는 이 초기화를 개요에 들어갈 때 이미 돌렸으니 여기서 다시 돌리지 않는다.
        if (positionedAtOverview) { positionedAtOverview = false; Debug.Log("[InfoPanel] 시작 — 위치 초기화 건너뜀(개요에서 이미 함 · 사용자 조정 유지)"); }
        else InitializeUIPositionOnStart();

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
        var practiceManager = cachedPracticeManager;
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

        // ★★설정 팝업의 <b>맞춤 설정</b>을 여기서 <b>1회</b> 부른다 (2026-09-08 지시).
        //   위의 InitializePatientPosition은 <b>프리셋</b>(앉기·눕기)을 적용할 뿐이라
        //   지금 사람이 서 있는 자리와는 무관하다. 헤드셋 기준으로 실제로 맞추는 것은
        //   설정의 '맞춤 설정'(PracticeSettingsController)이고, 종전에는 <b>사람이 눌러야만</b> 돌았다.
        //   ★같은 함수를 부른다 — 배선을 두 벌로 만들면 어느 쪽이 이겼는지 매번 확인해야 한다.
        ApplyCustomPositioningOnce();
    }

    private bool fitDeferred;

    private System.Collections.IEnumerator DeferredCustomPositioning()
    {
        while (!TrackingReady.Ready()) yield return null;   // 10-30 «너무 오래 걸린다» — 고정 1.5초 대신 추적이 들어오면 바로(상한 1.5초)
        fitDeferred = false;
        ApplyCustomPositioningOnce();
        Debug.Log($"[InfoPanel] 시작 자동 맞춤 — 추적 안정 뒤 {(customPositioningApplied ? "1회 부름" : "못 부름")} · {Time.timeSinceLevelLoad:F1}s");
    }

    /// <summary>
    /// 설정의 '맞춤 설정'을 시나리오당 <b>한 번만</b> 부른다.
    /// ★두 번 부르면 <see cref="PracticeSettingsController"/>가 authored 로컬 포즈를 복원한 뒤
    ///   다시 옮기므로 결과는 같지만, 사람이 손으로 옮겨 둔 것을 되돌려 버린다.
    /// </summary>
    private void ApplyCustomPositioningOnce()
    {
        if (customPositioningApplied) return;
        // ★10-30 기기 로그 실측: 시나리오 시작(≈0.1초)에 불려 추적 전 머리 값으로 맞췄다 → 환자 목이 y −0.29(바닥 아래).
        //   추적이 안정될 때(씬 로드 1.5초)까지 미룬다. 미룬 동안 다른 경로(로비 경로 코루틴)가 불러도 한 번만 돈다
        if (!TrackingReady.Ready())
        {
            if (!fitDeferred) { fitDeferred = true; StartCoroutine(DeferredCustomPositioning()); }
            return;
        }

        var settings = FindFirstObjectByType<PracticeSettingsController>(FindObjectsInactive.Include);
        if (settings == null)
        {
            // ★조용히 넘어가지 않는다 — 안 맞춰졌는데 이유가 안 보이면 코드를 의심하게 된다.
            ChunaLogger.LogWarning("[InfoPanel] 맞춤 설정을 부르려 했으나 PracticeSettingsController가 씬에 없다.");
            return;
        }

        settings.ApplyCustomPositioningOnce();
        customPositioningApplied = true;
        SceneReadyGate.Mark("fit");   // 10-30 로딩 가림막이 이걸 기다린다
        if (showDebugLogs)
            ChunaLogger.Log("<color=cyan>[InfoPanel] 시나리오 시작 — 설정의 '맞춤 설정'을 1회 불렀다.</color>");
    }

    private bool customPositioningApplied;

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
        StartCoroutine(LogResultVisibility());
    }

    /// <summary>★11-04 진단 — «결과창이 안 보인다». 완료 1초 뒤 진행 현황 창 안 무엇이 켜져 있고 무엇이 위에 덮였는지 남긴다.</summary>
    private System.Collections.IEnumerator LogResultVisibility()
    {
        yield return new WaitForSecondsRealtime(1f);
        if (resultPage == null) { Debug.LogWarning("[InfoPanel] 결과 확인 — resultPage 없음"); yield break; }
        var sb = new System.Text.StringBuilder($"[InfoPanel] 완료 1초 뒤 진행 현황 창: 페이지 켜짐 {resultPage.activeInHierarchy} · 펼침 {MultiWindow}\n");
        foreach (Transform c in resultPage.transform)
        {
            var g = c.GetComponent<Graphic>();
            var tmp = c.GetComponent<TMPro.TMP_Text>();
            sb.Append($"   [{c.GetSiblingIndex()}] {(c.gameObject.activeInHierarchy ? "켬" : "끔")} '{c.name}'{(g != null ? $" α {g.color.a:F2}" : "")}{(tmp != null ? $" 글자 {tmp.text?.Length ?? 0}자" : "")}\n");
        }
        Debug.Log(sb.ToString());
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
    /// 지금 걸려 있는 제목. ★<b>경추ROM 한정</b>으로
    /// <see cref="RomProgressInResultPage"/>가 제목을 잠깐 바꿔 걸고 <b>나갈 때 되돌리려고</b> 읽는다.
    /// 읽기 전용이다 — 제목을 정하는 것은 여전히 <c>ScenarioBootstrapper</c>다.
    /// </summary>
    public string ScenarioTitle => scenarioTitleText != null ? scenarioTitleText.text : null;

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
    private bool MultiWindow => windowArranger != null && windowArranger.isActiveAndEnabled && windowArranger.IsReady;

    /// <summary>펼침 모드에서 페이지 하나를 열고 닫는다. 열면 정면으로 가져온다. 판(창)은 InfoWindowArranger가 페이지를 보고 따라 켠다.</summary>
    private void SetMultiPage(GameObject page, bool open)
    {
        if (page == null) return;
        if (open && modeSelectionPage != null) modeSelectionPage.SetActive(false);
        page.SetActive(open);
        if (open) windowArranger.BringToFront(page);
    }

    private void SyncContentTogglesToPages()
    {
        SetToggleWithoutNotify(skeletonToggle, skeletonPage != null && skeletonPage.activeSelf);
        SetToggleWithoutNotify(expertVideoToggle, expertVideoPage != null && expertVideoPage.activeSelf);
        SetToggleWithoutNotify(resultToggle, resultPage != null && resultPage.activeSelf);
        UpdateAllToggleColors();
    }

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
