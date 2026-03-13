using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Cysharp.Threading.Tasks;

/// <summary>
/// 로비 UI 통합 관리 클래스 (수정 버전)
///
/// [수정 사항]
/// 1. 시나리오 카드 버튼 자동 검색 및 연결
/// 2. 시나리오 카드는 항상 클릭 가능 (로그인 안 되면 팝업)
/// 3. 버튼 연결 디버그 로그 강화
/// 4. 내부 로직을 헬퍼 클래스로 분리 (LoginStateStore, AuthFlowManager, LobbyPopupHandler, GradeSelectionHandler, UserSelectionHandler)
/// </summary>
public class LobbyAuthUI_Complete : MonoBehaviour
{
    #region UI References - Main Lobby
    [Header("=== Main Lobby UI ===")]
    [SerializeField] private GameObject headerPanel;
    [SerializeField] private GameObject highlightBar;
    [SerializeField] private GameObject scenarioCardsContainer;
    [SerializeField] private GameObject userInfoPanel;
    [SerializeField] private GameObject bottomButtonsPanel;

    [Header("User Info Panel Components")]
    [SerializeField] private Button userIconButton;
    [SerializeField] private TextMeshProUGUI userNameText;
    [SerializeField] private GameObject userInfoContent;

    [Header("Guide Message")]
    [SerializeField] private TextMeshProUGUI guideMessageText;
    [SerializeField] private string loginGuideMessage = "로그인을 하세요";
    [SerializeField] private string scenarioGuideMessage = "시나리오를 선택하세요";

    [Header("Scenario Cards")]
    [SerializeField] private Button[] scenarioCardButtons = new Button[5];
    [SerializeField] private CanvasGroup[] scenarioCardCanvasGroups = new CanvasGroup[5];
    [Tooltip("시나리오 카드를 자동으로 찾아서 연결할지 여부")]
    [SerializeField] private bool autoFindScenarioCards = true;

    [Header("Bottom Buttons")]
    [SerializeField] private Button interactionGuideButton;
    [SerializeField] private Button exitButton;

    [Header("Popups")]
    [SerializeField] private GameObject loginRequiredPopup;
    [SerializeField] private Toggle loginRequiredCloseButton;

    [Header("Exit Confirmation Popup")]
    [SerializeField] private GameObject exitConfirmationPopup;
    [SerializeField] private Toggle exitYesToggle;
    [SerializeField] private Toggle exitNoToggle;

    [Header("Logout Confirmation Popup")]
    [SerializeField] private GameObject logoutConfirmationPopup;
    [SerializeField] private Toggle logoutCancelToggle;
    [SerializeField] private Toggle logoutConfirmToggle;

    [Header("Survey Panel")]
    [Tooltip("설문 패널 (로그아웃/종료 시 표시)")]
    [SerializeField] private SurveyPanel surveyPanel;

    [Header("Mirroring (Render Streaming)")]
    [Tooltip("미러링용 Camera X 오브젝트 (로그인 시 활성화)")]
    [SerializeField] private GameObject mirroringCameraObject;
    #endregion

    #region UI References - Grade Selection Panel
    [Header("=== Grade Selection Panel ===")]
    [SerializeField] private GameObject gradeSelectionPanel;
    [SerializeField] private Image gradeSelectionBackground;
    [SerializeField] private TextMeshProUGUI gradeSelectionTitle;
    [SerializeField] private Button gradeBackButton;
    [SerializeField] private ScrollRect gradeScrollView;
    [SerializeField] private Transform gradeContentContainer;
    [SerializeField] private GameObject gradeButtonPrefab;
    #endregion

    #region UI References - User Selection Panel
    [Header("=== User Selection Panel ===")]
    [SerializeField] private GameObject userSelectionPanel;
    [SerializeField] private Image userSelectionBackground;
    [SerializeField] private TextMeshProUGUI userSelectionTitle;
    [SerializeField] private Button userBackButton;
    [SerializeField] private ScrollRect userScrollView;
    [SerializeField] private Transform userContentContainer;
    [SerializeField] private GameObject userButtonPrefab;
    #endregion

    #region Auth Service
    [Header("=== Authentication ===")]
    [SerializeField] private bool useMockService = false;

    private IAuthenticationService authService;
    private string currentDeviceSN;
    private string currentOrgID;
    private int currentUserID;
    private string currentUsername;
    private string savedDeviceSN;
    #endregion

    #region User Data
    private UserData[] allUsers;
    private Dictionary<string, List<UserData>> usersByGrade = new Dictionary<string, List<UserData>>();
    private string selectedGrade;
    #endregion

    #region Helpers
    private LoginStateStore loginStateStore;
    private AuthFlowManager authFlowManager;
    private LobbyPopupHandler popupHandler;
    private GradeSelectionHandler gradeSelectionHandler;
    private UserSelectionHandler userSelectionHandler;
    #endregion

    #region Unity Lifecycle
    private void Awake()
    {
        InitializeAuthService();
        InitializeHelpers();
        SetupInitialUI();
        SubscribeToEvents();

        // SurveyPanel 자동 찾기
        if (surveyPanel == null)
        {
            surveyPanel = FindFirstObjectByType<SurveyPanel>();
            if (surveyPanel != null)
            {
                ChunaLogger.Log("[LobbyUI] ✅ SurveyPanel 자동 찾기 성공");
            }
        }
    }

    // 앱 최초 실행 여부 (static으로 앱 생명주기 동안 유지)
    private static bool isFirstLaunch = true;

    private void Start()
    {
        // 앱 최초 실행 시에만 로그인 정보 초기화 (강제 종료 대응)
        // 로비 재진입 시에는 기존 로그인 유지
        if (isFirstLaunch)
        {
            isFirstLaunch = false;
            loginStateStore.ClearLoginInfo();
            currentUsername = string.Empty;
            currentUserID = 0;
        }
        else
        {
            LoadSavedLoginInfo();
        }

        if (string.IsNullOrEmpty(currentUsername))
        {
            if (userNameText != null)
                userNameText.text = "Guest";
            SetGuideMessage(loginGuideMessage);
        }
        else
        {
            UpdateUserInfoPanel();
            SetScenarioCardsVisualState(true);
            SetGuideMessage(scenarioGuideMessage);
        }

        LoadSavedDeviceSN();
        AuthenticateDevice().Forget();
    }

    private void OnDestroy()
    {
        UnsubscribeFromEvents();
    }

    /// <summary>
    /// 앱 종료 시 호출 - 로그아웃 후 종료
    /// </summary>
    private void OnApplicationQuit()
    {
        Debug.Log("[LobbyUI] OnApplicationQuit 호출");

        // 로그인 상태면 동기적으로 로그아웃 시도
        if (!string.IsNullOrEmpty(currentUsername) && !string.IsNullOrEmpty(currentDeviceSN))
        {
            Debug.Log($"[LobbyUI] 앱 종료 전 로그아웃 시도: {currentUsername}");

            try
            {
                // 동기적으로 로그아웃 API 호출 (타임아웃 3초)
                var logoutTask = authService.LogoffAsync(currentDeviceSN, currentUsername, "VR_CHUNA");
                logoutTask.AsTask().Wait(TimeSpan.FromSeconds(3));
                Debug.Log("[LobbyUI] ✅ 앱 종료 전 로그아웃 성공");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[LobbyUI] ⚠️ 앱 종료 전 로그아웃 실패 (무시): {e.Message}");
            }

            // PlayerPrefs 로그인 정보 삭제
            loginStateStore.ClearLoginInfo();
        }
    }
    #endregion

    #region Initialization
    private void InitializeAuthService()
    {
        if (useMockService)
        {
            authService = gameObject.AddComponent<MockAuthenticationService>();
            ChunaLogger.Log("[LobbyUI] Mock 서비스 사용");
        }
        else
        {
            authService = AuthenticationService.Instance;
            ChunaLogger.Log("[LobbyUI] 실제 서비스 사용");
        }
    }

    private void InitializeHelpers()
    {
        loginStateStore = new LoginStateStore();
        authFlowManager = new AuthFlowManager(authService);
        popupHandler = new LobbyPopupHandler(loginRequiredPopup, exitConfirmationPopup, logoutConfirmationPopup);
        gradeSelectionHandler = new GradeSelectionHandler(gradeSelectionPanel, gradeButtonPrefab, gradeContentContainer);
        userSelectionHandler = new UserSelectionHandler(userSelectionPanel, userButtonPrefab, userContentContainer);
    }

    private void SetupInitialUI()
    {
        ChunaLogger.Log("[LobbyUI] ========== UI 초기화 시작 ==========");

        // 초기 상태 설정
        gradeSelectionPanel?.SetActive(false);
        userSelectionPanel?.SetActive(false);
        userInfoContent?.SetActive(true); // 항상 활성화! (로그인 전에는 "Guest" 표시)
        popupHandler.InitializePopups();

        // 시나리오 카드 자동 검색
        if (autoFindScenarioCards)
        {
            AutoFindScenarioCards();
        }

        // 시나리오 카드 버튼 연결 (항상 클릭 가능하게)
        SetupScenarioCards();

        // 다른 버튼들 연결
        SetupOtherButtons();

        // 배경 딤 클릭 이벤트
        SetupBackgroundDimClicks();

        ChunaLogger.Log("[LobbyUI] ========== UI 초기화 완료 ==========");
    }

    /// <summary>
    /// 시나리오 카드를 자동으로 찾아서 연결
    /// </summary>
    private void AutoFindScenarioCards()
    {
        if (scenarioCardsContainer == null)
        {
            ChunaLogger.LogWarning("[LobbyUI] scenarioCardsContainer가 null입니다. 자동 검색을 건너뜁니다.");
            return;
        }

        ChunaLogger.Log("[LobbyUI] 시나리오 카드 자동 검색 시작...");

        // Card_01 ~ Card_05 찾기
        for (int i = 0; i < 5; i++)
        {
            string cardName = $"Card_{(i + 1):00}";
            Transform cardTransform = scenarioCardsContainer.transform.Find(cardName);

            if (cardTransform != null)
            {
                // Button 컴포넌트 찾기
                Button cardButton = cardTransform.GetComponent<Button>();
                if (cardButton == null)
                {
                    cardButton = cardTransform.GetComponentInChildren<Button>();
                }

                if (cardButton != null)
                {
                    scenarioCardButtons[i] = cardButton;
                    ChunaLogger.Log($"[LobbyUI] ✅ {cardName} 버튼 자동 연결 성공");
                }
                else
                {
                    ChunaLogger.LogWarning($"[LobbyUI] ⚠️ {cardName}에서 Button 컴포넌트를 찾을 수 없습니다.");
                }

                // CanvasGroup 찾기
                CanvasGroup canvasGroup = cardTransform.GetComponent<CanvasGroup>();
                if (canvasGroup != null)
                {
                    scenarioCardCanvasGroups[i] = canvasGroup;
                    ChunaLogger.Log($"[LobbyUI] ✅ {cardName} CanvasGroup 자동 연결 성공");
                }
            }
            else
            {
                ChunaLogger.LogWarning($"[LobbyUI] ⚠️ {cardName}을(를) 찾을 수 없습니다.");
            }
        }
    }

    /// <summary>
    /// 시나리오 카드 설정 (항상 클릭 가능)
    /// </summary>
    private void SetupScenarioCards()
    {
        ChunaLogger.Log("[LobbyUI] --- 시나리오 카드 버튼 연결 시작 ---");

        int connectedCount = 0;

        for (int i = 0; i < scenarioCardButtons.Length; i++)
        {
            if (scenarioCardButtons[i] != null)
            {
                int index = i; // 클로저를 위한 로컬 복사

                // 기존 리스너 제거
                scenarioCardButtons[i].onClick.RemoveAllListeners();

                // 새 리스너 추가
                scenarioCardButtons[i].onClick.AddListener(() => OnScenarioCardClicked(index));

                // 버튼은 항상 활성화
                scenarioCardButtons[i].interactable = true;

                ChunaLogger.Log($"[LobbyUI] ✅ 시나리오 카드 {index + 1} 버튼 연결 완료");
                connectedCount++;
            }
            else
            {
                ChunaLogger.LogError($"[LobbyUI] ❌ 시나리오 카드 {i + 1} 버튼이 NULL입니다!");
            }
        }

        // CanvasGroup 설정 (시각적으로만 비활성화, 클릭은 가능)
        for (int i = 0; i < scenarioCardCanvasGroups.Length; i++)
        {
            if (scenarioCardCanvasGroups[i] != null)
            {
                scenarioCardCanvasGroups[i].alpha = 0.5f; // 시각적으로 어둡게
                scenarioCardCanvasGroups[i].interactable = true; // 클릭 가능
                scenarioCardCanvasGroups[i].blocksRaycasts = true; // 레이캐스트 차단 안 함
            }
        }

        ChunaLogger.Log($"[LobbyUI] 시나리오 카드 버튼 연결 완료: {connectedCount}/5");
    }

    /// <summary>
    /// 다른 버튼들 연결
    /// </summary>
    private void SetupOtherButtons()
    {
        ChunaLogger.Log("[LobbyUI] --- 기타 버튼 연결 시작 ---");

        // 유저 아이콘 버튼
        if (userIconButton != null)
        {
            userIconButton.onClick.RemoveAllListeners();
            userIconButton.onClick.AddListener(OnUserIconClicked);
            ChunaLogger.Log("[LobbyUI] ✅ 유저 아이콘 버튼 연결");
        }
        else
        {
            ChunaLogger.LogWarning("[LobbyUI] ⚠️ userIconButton이 null입니다.");
        }

        // 조 선택 뒤로가기
        if (gradeBackButton != null)
        {
            gradeBackButton.onClick.RemoveAllListeners();
            gradeBackButton.onClick.AddListener(OnGradeBackButtonClicked);
            ChunaLogger.Log("[LobbyUI] ✅ 조 선택 뒤로가기 버튼 연결");
        }
        else
        {
            ChunaLogger.LogWarning("[LobbyUI] ⚠️ gradeBackButton이 null입니다.");
        }

        // 사용자 선택 뒤로가기
        if (userBackButton != null)
        {
            userBackButton.onClick.RemoveAllListeners();
            userBackButton.onClick.AddListener(OnUserBackButtonClicked);
            ChunaLogger.Log("[LobbyUI] ✅ 사용자 선택 뒤로가기 버튼 연결");
        }
        else
        {
            ChunaLogger.LogWarning("[LobbyUI] ⚠️ userBackButton이 null입니다.");
        }

        // 나가기 버튼
        if (exitButton != null)
        {
            exitButton.onClick.RemoveAllListeners();
            exitButton.onClick.AddListener(OnExitButtonClicked);
            ChunaLogger.Log("[LobbyUI] ✅ 나가기 버튼 연결");
        }
        else
        {
            ChunaLogger.LogWarning("[LobbyUI] ⚠️ exitButton이 null입니다.");
        }

        // 상호작용 가이드 버튼
        if (interactionGuideButton != null)
        {
            interactionGuideButton.onClick.RemoveAllListeners();
            interactionGuideButton.onClick.AddListener(OnInteractionGuideClicked);
            ChunaLogger.Log("[LobbyUI] ✅ 상호작용 가이드 버튼 연결");
        }
        else
        {
            ChunaLogger.LogWarning("[LobbyUI] ⚠️ interactionGuideButton이 null입니다.");
        }

        // 토글들을 버튼처럼 설정
        SetupToggleAsButton(loginRequiredCloseButton, "로그인 팝업 - 유저 목록", OnLoginRequiredPopupGoToUserList);
        SetupToggleAsButton(exitYesToggle, "종료 확인 - 예", OnExitConfirmYes);
        SetupToggleAsButton(exitNoToggle, "종료 확인 - 아니오", OnExitConfirmNo);
        SetupToggleAsButton(logoutCancelToggle, "로그아웃 확인 - 취소", OnLogoutCancel);
        SetupToggleAsButton(logoutConfirmToggle, "로그아웃 확인 - 로그아웃하기", OnLogoutConfirm);
    }

    private void SetupBackgroundDimClicks()
    {
        // GradeSelectionPanel 배경 클릭
        if (gradeSelectionBackground != null)
        {
            var button = gradeSelectionBackground.GetComponent<Button>();
            if (button == null)
            {
                button = gradeSelectionBackground.gameObject.AddComponent<Button>();
            }
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => gradeSelectionHandler.Hide());
            ChunaLogger.Log("[LobbyUI] ✅ 조 선택 배경 클릭 연결");
        }

        // UserSelectionPanel 배경 클릭
        if (userSelectionBackground != null)
        {
            var button = userSelectionBackground.GetComponent<Button>();
            if (button == null)
            {
                button = userSelectionBackground.gameObject.AddComponent<Button>();
            }
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => userSelectionHandler.Hide());
            ChunaLogger.Log("[LobbyUI] ✅ 사용자 선택 배경 클릭 연결");
        }
    }
    #endregion

    #region Event Subscription
    private void SubscribeToEvents()
    {
        AuthEvents.OnAuthenticationSuccess += OnAuthenticationSuccess;
        AuthEvents.OnAuthenticationFailed += OnAuthenticationFailed;
        AuthEvents.OnUserListLoadCompleted += OnUserListLoadCompleted;
        AuthEvents.OnUserListLoadFailed += OnUserListLoadFailed;
        AuthEvents.OnLoginSuccess += OnLoginSuccess;
        AuthEvents.OnLoginFailed += OnLoginFailed;
        AuthEvents.OnLogoutCompleted += OnLogoutCompleted;
    }

    private void UnsubscribeFromEvents()
    {
        AuthEvents.OnAuthenticationSuccess -= OnAuthenticationSuccess;
        AuthEvents.OnAuthenticationFailed -= OnAuthenticationFailed;
        AuthEvents.OnUserListLoadCompleted -= OnUserListLoadCompleted;
        AuthEvents.OnUserListLoadFailed -= OnUserListLoadFailed;
        AuthEvents.OnLoginSuccess -= OnLoginSuccess;
        AuthEvents.OnLoginFailed -= OnLoginFailed;
        AuthEvents.OnLogoutCompleted -= OnLogoutCompleted;
    }
    #endregion

    #region Authentication Flow
    private async UniTaskVoid AuthenticateDevice()
    {
        try
        {
            ChunaLogger.Log("[LobbyUI] 디바이스 인증 시작...");

            var (deviceSN, orgID, licenseValid) = await authFlowManager.AuthenticateDeviceWithRetry(savedDeviceSN);

            currentDeviceSN = deviceSN;
            currentOrgID = orgID;
            loginStateStore.SaveDeviceSN(currentDeviceSN);

            if (!licenseValid)
            {
                ShowLicenseError();
                return;
            }

            allUsers = await authFlowManager.LoadUserList(currentOrgID);

            if (allUsers != null && allUsers.Length > 0)
            {
                usersByGrade = authFlowManager.OrganizeByGrade(allUsers);
            }
        }
        catch (Exception e)
        {
            ChunaLogger.LogError($"[LobbyUI] 인증 최종 실패: {e.Message} | UUID: {SystemInfo.deviceUniqueIdentifier}");
            ShowAuthenticationError(e.Message);
        }
    }
    #endregion

    #region Button Click Handlers
    /// <summary>
    /// 시나리오 카드 클릭 핸들러
    /// </summary>
    public void OnScenarioCardClicked(int scenarioIndex)
    {
        ChunaLogger.Log($"[LobbyUI] ========== 시나리오 카드 {scenarioIndex + 1} 클릭 ==========");

        // 로그인 체크
        if (string.IsNullOrEmpty(currentUsername))
        {
            ChunaLogger.LogWarning("[LobbyUI] ⚠️ 로그인이 필요합니다!");
            popupHandler.ShowLoginRequiredPopup();
            return;
        }

        ChunaLogger.Log($"[LobbyUI] ✅ 시나리오 {scenarioIndex + 1} 시작: 사용자={currentUsername}");

        // 선택된 시나리오 인덱스 저장 (ScenarioBootstrapper가 읽음)
        PlayerPrefs.SetInt(PrefsKeys.SelectedScenario, scenarioIndex);
        PlayerPrefs.Save();

        // 통합 TrainingScene 로드
        SceneLoader.LoadScene("TrainingScene");
    }

    private void OnUserIconClicked()
    {
        ChunaLogger.Log($"[LobbyUI] ========== 유저 아이콘 클릭 ==========");
        ChunaLogger.Log($"[LobbyUI] currentUsername: '{currentUsername}' (IsNullOrEmpty: {string.IsNullOrEmpty(currentUsername)})");

        // 로그인 상태 확인
        if (string.IsNullOrEmpty(currentUsername))
        {
            // 로그인 안 되어 있으면 조 선택 패널 표시
            ChunaLogger.Log("[LobbyUI] ➡️ 로그인 안 되어 있음 - 조 선택 패널 표시");
            gradeSelectionHandler.ShowPanel(usersByGrade, OnGradeSelected);
        }
        else
        {
            // 로그인 되어 있으면 로그아웃 확인 팝업 표시
            ChunaLogger.Log($"[LobbyUI] ➡️ 로그인되어 있음 ({currentUsername}) - 로그아웃 확인 팝업 표시");
            popupHandler.ShowLogoutConfirmPopup();
        }
    }

    private void OnGradeBackButtonClicked()
    {
        ChunaLogger.Log("[LobbyUI] 조 선택 뒤로가기 클릭");
        gradeSelectionHandler.Hide();
    }

    private void OnUserBackButtonClicked()
    {
        ChunaLogger.Log("[LobbyUI] 사용자 선택 뒤로가기 클릭");
        userSelectionHandler.Hide();
        gradeSelectionHandler.ShowPanel(usersByGrade, OnGradeSelected);
    }

    private void OnExitButtonClicked()
    {
        ChunaLogger.Log("[LobbyUI] 나가기 버튼 클릭");
        popupHandler.ShowExitConfirmPopup();
    }

    /// <summary>
    /// 종료 확인 팝업 - 예 선택
    /// </summary>
    private void OnExitConfirmYes()
    {
        ChunaLogger.Log("[LobbyUI] 종료 확인 - 예 선택");
        popupHandler.HideExitConfirmPopup();

        // 설문 패널 표시 후 종료
        if (surveyPanel != null)
        {
            surveyPanel.ShowSurveyPanel(() => {
                ExitApplication().Forget();
            });
        }
        else
        {
            // SurveyPanel 없으면 바로 종료
            ChunaLogger.LogWarning("[LobbyUI] SurveyPanel이 없어 바로 종료합니다.");
            ExitApplication().Forget();
        }
    }

    /// <summary>
    /// 종료 확인 팝업 - 아니오 선택
    /// </summary>
    private void OnExitConfirmNo()
    {
        ChunaLogger.Log("[LobbyUI] 종료 확인 - 아니오 선택");
        popupHandler.HideExitConfirmPopup();
    }

    /// <summary>
    /// 애플리케이션 종료 (로그아웃 후 종료)
    /// </summary>
    private async UniTaskVoid ExitApplication()
    {
        ChunaLogger.Log("[LobbyUI] 애플리케이션 종료 시작");

        // 로그인 상태면 로그아웃 먼저 수행
        if (!string.IsNullOrEmpty(currentUsername) && !string.IsNullOrEmpty(currentDeviceSN))
        {
            ChunaLogger.Log($"[LobbyUI] 종료 전 로그아웃 시도: {currentUsername}");
            await authFlowManager.PerformLogout(currentDeviceSN, currentUsername);

            // PlayerPrefs 로그인 정보 삭제
            loginStateStore.ClearLoginInfo();
        }

        ChunaLogger.Log("[LobbyUI] 애플리케이션 종료");

        // 애플리케이션 종료
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void OnInteractionGuideClicked()
    {
        ChunaLogger.Log("[LobbyUI] 상호작용 가이드 버튼 클릭");
        // TODO: 상호작용 가이드 표시
    }
    #endregion

    #region Grade/User Selection
    private void OnGradeSelected(string grade)
    {
        selectedGrade = grade;
        ChunaLogger.Log($"[LobbyUI] 조 선택: {grade}");

        gradeSelectionHandler.Hide();

        if (usersByGrade.ContainsKey(selectedGrade))
        {
            userSelectionHandler.ShowPanel(usersByGrade[selectedGrade], OnUserSelected);
        }
        else
        {
            ChunaLogger.LogWarning($"[LobbyUI] 선택된 조에 사용자가 없습니다: {selectedGrade}");
        }
    }

    private void OnUserSelected(int userId, string username)
    {
        ChunaLogger.Log($"[LobbyUI] 사용자 선택: {username} (ID: {userId})");
        PerformLogin(userId, username).Forget();
    }
    #endregion

    #region Login/Logout
    private async UniTaskVoid PerformLogin(int userId, string username)
    {
        try
        {
            var mirroringData = await authFlowManager.PerformLogin(currentDeviceSN, username);

            // 로그인 정보 저장
            currentUserID = userId;
            currentUsername = username;
            loginStateStore.SaveLoginInfo(username, userId);

            ChunaLogger.Log($"[LobbyUI] 로그인 정보 저장 완료 - currentUsername: '{currentUsername}', currentUserID: {currentUserID}");

            userSelectionHandler.Hide();
            UpdateUserInfoPanel();

            // 시나리오 카드 활성화
            SetScenarioCardsVisualState(true);

            // 안내 메시지 변경
            SetGuideMessage(scenarioGuideMessage);

            // 미러링 시작: Camera X 활성화 및 미러링 데이터 전달
            ActivateMirroring(mirroringData);

            ChunaLogger.Log($"[LobbyUI] ✅ 로그인 완료: {username} (ID: {userId})");
        }
        catch (Exception e)
        {
            ChunaLogger.LogError($"[LobbyUI] ❌ 로그인 실패: {e.Message}");
            ShowLoginError(e.Message);
        }
    }

    /// <summary>
    /// 미러링 활성화 (Camera X 활성화 + RenderManager에 데이터 전달)
    /// </summary>
    private void ActivateMirroring(MirroringData mirroringData)
    {
        // 미러링 데이터가 없거나 off면 미러링 안함
        if (mirroringData == null || mirroringData.mirroring == "off")
        {
            ChunaLogger.Log("[LobbyUI] 미러링 비활성화 상태 (데이터 없음 또는 off)");
            return;
        }

        // Camera X 활성화
        if (mirroringCameraObject != null)
        {
            mirroringCameraObject.SetActive(true);
            ChunaLogger.Log($"[LobbyUI] 미러링 카메라 활성화됨: {mirroringCameraObject.name}");

            // RenderManager가 활성화되면 약간의 딜레이 후 데이터 전달
            StartCoroutine(SetMirroringDataDelayed(mirroringData));
        }
        else
        {
            ChunaLogger.LogWarning("[LobbyUI] mirroringCameraObject가 설정되지 않음");

            // 직접 RenderManager 찾아서 전달 시도
            if (RenderManager.instance != null)
            {
                RenderManager.instance.SetMirroringData(mirroringData);
            }
        }
    }

    private System.Collections.IEnumerator SetMirroringDataDelayed(MirroringData mirroringData)
    {
        // RenderManager.Start()가 실행될 시간을 줌
        yield return new WaitForSeconds(0.1f);

        if (RenderManager.instance != null)
        {
            RenderManager.instance.SetMirroringData(mirroringData);
            ChunaLogger.Log($"[LobbyUI] RenderManager에 미러링 데이터 전달 완료");
        }
        else
        {
            ChunaLogger.LogWarning("[LobbyUI] RenderManager.instance가 null");
        }
    }

    private async UniTaskVoid PerformLogout()
    {
        await PerformLogoutAsync();
    }

    /// <summary>
    /// 로그아웃 처리 (await 가능)
    /// </summary>
    private async UniTask PerformLogoutAsync()
    {
        if (string.IsNullOrEmpty(currentUsername))
        {
            return;
        }

        await authFlowManager.PerformLogout(currentDeviceSN, currentUsername);

        // 미러링 비활성화
        DeactivateMirroring();

        // API 실패 여부와 관계없이 로컬 상태는 항상 초기화
        ClearUserInfo();
        ChunaLogger.Log($"[LobbyUI] ✅ 로그아웃 완료 (로컬 상태 초기화)");
    }

    /// <summary>
    /// 미러링 비활성화 (Camera X 비활성화)
    /// </summary>
    private void DeactivateMirroring()
    {
        // RenderManager 미러링 중지
        if (RenderManager.instance != null)
        {
            RenderManager.instance.StopMirroring();
        }

        // Camera X 비활성화
        if (mirroringCameraObject != null)
        {
            mirroringCameraObject.SetActive(false);
            ChunaLogger.Log($"[LobbyUI] 미러링 카메라 비활성화됨: {mirroringCameraObject.name}");
        }
    }
    #endregion

    #region Popup Handlers
    /// <summary>
    /// 로그인 팝업에서 유저 목록으로 이동
    /// </summary>
    private void OnLoginRequiredPopupGoToUserList()
    {
        ChunaLogger.Log("[LobbyUI] 로그인 팝업 - 유저 목록으로 이동");
        popupHandler.HideLoginRequiredPopup();
        gradeSelectionHandler.ShowPanel(usersByGrade, OnGradeSelected);
    }

    /// <summary>
    /// 로그아웃 확인 팝업 - 취소 선택
    /// </summary>
    private void OnLogoutCancel()
    {
        ChunaLogger.Log("[LobbyUI] 로그아웃 취소");
        popupHandler.HideLogoutConfirmPopup();
    }

    /// <summary>
    /// 로그아웃 확인 팝업 - 로그아웃하기 선택
    /// </summary>
    private void OnLogoutConfirm()
    {
        ChunaLogger.Log("[LobbyUI] 로그아웃 확인 - 로그아웃 진행");
        popupHandler.HideLogoutConfirmPopup();

        // 설문 패널 표시 후 로그아웃
        if (surveyPanel != null)
        {
            surveyPanel.ShowSurveyPanel(() => {
                PerformLogout().Forget();
            });
        }
        else
        {
            // SurveyPanel 없으면 바로 로그아웃
            ChunaLogger.LogWarning("[LobbyUI] SurveyPanel이 없어 바로 로그아웃합니다.");
            PerformLogout().Forget();
        }
    }

    /// <summary>
    /// 토글을 버튼처럼 사용하기 위한 리셋 코루틴
    /// </summary>
    private System.Collections.IEnumerator ResetToggle(Toggle toggle)
    {
        yield return null;
        if (toggle != null)
            toggle.isOn = false;
    }

    /// <summary>
    /// 토글을 버튼처럼 설정하는 헬퍼 메서드
    /// </summary>
    private void SetupToggleAsButton(Toggle toggle, string debugLabel, Action onClickAction)
    {
        if (toggle == null)
        {
            ChunaLogger.LogWarning($"[LobbyUI] ⚠️ {debugLabel} 토글이 null입니다.");
            return;
        }

        toggle.group = null;
        toggle.onValueChanged.RemoveAllListeners();
        toggle.onValueChanged.AddListener((isOn) =>
        {
            if (isOn)
            {
                onClickAction?.Invoke();
                StartCoroutine(ResetToggle(toggle));
            }
        });
        ChunaLogger.Log($"[LobbyUI] ✅ {debugLabel} 토글 연결");
    }
    #endregion

    #region Auth Event Handlers
    private void OnAuthenticationSuccess(string deviceSN)
    {
        ChunaLogger.Log($"[LobbyUI] [이벤트] 인증 성공: {deviceSN}");
    }

    private void OnAuthenticationFailed(string errorMessage)
    {
        ChunaLogger.LogError($"[LobbyUI] [이벤트] 인증 실패: {errorMessage}");
    }

    private void OnUserListLoadCompleted(int userCount)
    {
        ChunaLogger.Log($"[LobbyUI] [이벤트] 사용자 목록 로드 완료: {userCount}명");
    }

    private void OnUserListLoadFailed(string errorMessage)
    {
        ChunaLogger.LogError($"[LobbyUI] [이벤트] 사용자 목록 로드 실패: {errorMessage}");
    }

    private void OnLoginSuccess(string username, int userID)
    {
        ChunaLogger.Log($"[LobbyUI] [이벤트] 로그인 성공: {username}");
    }

    private void OnLoginFailed(string username, string errorMessage)
    {
        ChunaLogger.LogError($"[LobbyUI] [이벤트] 로그인 실패: {username} - {errorMessage}");
    }

    private void OnLogoutCompleted(string username)
    {
        ChunaLogger.Log($"[LobbyUI] [이벤트] 로그아웃 완료: {username}");
    }
    #endregion

    #region UI Update
    private void UpdateUserInfoPanel()
    {
        if (userNameText != null)
        {
            userNameText.text = currentUsername;
        }

        ChunaLogger.Log("[LobbyUI] 사용자 정보 패널 업데이트");
    }

    private void ClearUserInfo()
    {
        currentUsername = string.Empty;
        currentUserID = 0;

        // PlayerPrefs에서 로그인 정보 삭제
        loginStateStore.ClearLoginInfo();

        // 텍스트만 "Guest"로 변경 (버튼은 계속 보임)
        if (userNameText != null)
        {
            userNameText.text = "Guest";
        }

        // 시나리오 카드 시각적으로 비활성화
        SetScenarioCardsVisualState(false);

        // 안내 메시지 복원
        SetGuideMessage(loginGuideMessage);

        ChunaLogger.Log("[LobbyUI] 사용자 정보 초기화 (Guest로 표시)");
    }

    private void SetScenarioCardsVisualState(bool active)
    {
        float targetAlpha = active ? 1f : 0.5f;

        // CanvasGroup으로 알파 조절 (클릭은 항상 가능)
        foreach (var canvasGroup in scenarioCardCanvasGroups)
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = targetAlpha;
                // interactable과 blocksRaycasts는 항상 true (항상 클릭 가능)
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;
            }
        }

        ChunaLogger.Log($"[LobbyUI] 시나리오 카드 시각 상태: {(active ? "활성" : "비활성")} (Alpha: {targetAlpha})");
    }

    /// <summary>
    /// 안내 메시지 설정
    /// </summary>
    private void SetGuideMessage(string message)
    {
        if (guideMessageText != null)
        {
            guideMessageText.text = message;
            ChunaLogger.Log($"[LobbyUI] 안내 메시지 변경: {message}");
        }
        else
        {
            ChunaLogger.LogWarning("[LobbyUI] guideMessageText가 연결되지 않았습니다.");
        }
    }
    #endregion

    #region PlayerPrefs Management (delegated to LoginStateStore)
    private void LoadSavedDeviceSN()
    {
        savedDeviceSN = loginStateStore.LoadDeviceSN();
    }

    private void LoadSavedLoginInfo()
    {
        var info = loginStateStore.LoadLoginInfo();
        if (info.hasData)
        {
            currentUsername = info.username;
            currentUserID = info.userID;
        }
        else
        {
            currentUsername = string.Empty;
            currentUserID = 0;
        }
    }
    #endregion

    #region Error Handling
    private void ShowAuthenticationError(string errorMessage)
    {
        ChunaLogger.LogError($"[LobbyUI] 인증 오류 표시: {errorMessage}");
        // TODO: 에러 팝업 표시
    }

    private void ShowLicenseError()
    {
        ChunaLogger.LogError("[LobbyUI] 라이선스 오류");
        // TODO: 라이선스 에러 팝업 표시
    }

    private void ShowLoginError(string errorMessage)
    {
        ChunaLogger.LogError($"[LobbyUI] 로그인 오류 표시: {errorMessage}");
        // TODO: 로그인 에러 팝업 표시
    }
    #endregion

    #region Public Methods
    public (int userID, string username, string orgID, string deviceSN) GetCurrentUserInfo()
    {
        return (currentUserID, currentUsername, currentOrgID, currentDeviceSN);
    }

    public void ForceLogout()
    {
        if (!string.IsNullOrEmpty(currentUsername))
        {
            PerformLogout().Forget();
        }
    }

    public async UniTask ResetDevice()
    {
        if (string.IsNullOrEmpty(currentDeviceSN))
        {
            ChunaLogger.LogWarning("[LobbyUI] DeviceSN이 없어 초기화할 수 없습니다.");
            return;
        }

        try
        {
            await authFlowManager.ResetDevice(currentDeviceSN);

            loginStateStore.ClearAllData();
            currentDeviceSN = string.Empty;
            currentOrgID = string.Empty;
            currentUserID = 0;
            currentUsername = string.Empty;
            savedDeviceSN = string.Empty;

            ChunaLogger.Log("[LobbyUI] 디바이스 초기화 완료");
        }
        catch (Exception e)
        {
            ChunaLogger.LogError($"[LobbyUI] 디바이스 초기화 실패: {e.Message}");
        }
    }
    #endregion

    #region Debug Helpers
    [ContextMenu("Debug - Clear All Saved Data (Reset)")]
    public void Debug_ClearAllSavedData()
    {
        ChunaLogger.Log("[LobbyUI] ========== 저장된 모든 데이터 초기화 ==========");

        loginStateStore.ClearAllData();

        // 현재 상태 초기화
        currentUsername = string.Empty;
        currentUserID = 0;
        currentDeviceSN = string.Empty;
        currentOrgID = string.Empty;
        savedDeviceSN = string.Empty;

        ChunaLogger.Log("[LobbyUI] ✅ 모든 저장 데이터 초기화 완료");
        ChunaLogger.Log("[LobbyUI] 앱을 재시작하세요.");
    }

    [ContextMenu("Test - Show Grade Selection")]
    private void Debug_ShowGradeSelection()
    {
        gradeSelectionHandler.ShowPanel(usersByGrade, OnGradeSelected);
    }

    [ContextMenu("Test - Hide All Panels")]
    private void Debug_HideAllPanels()
    {
        gradeSelectionHandler.Hide();
        userSelectionHandler.Hide();
    }

    [ContextMenu("Test - Show Login Popup")]
    private void Debug_ShowLoginPopup()
    {
        popupHandler.ShowLoginRequiredPopup();
    }

    [ContextMenu("Test - Test Scenario Click (Not Logged In)")]
    private void Debug_TestScenarioClickNotLoggedIn()
    {
        currentUsername = ""; // 로그아웃 상태로 만듦
        OnScenarioCardClicked(0);
    }

    [ContextMenu("Test - Test Scenario Click (Logged In)")]
    private void Debug_TestScenarioClickLoggedIn()
    {
        currentUsername = "TestUser"; // 로그인 상태로 만듦
        OnScenarioCardClicked(0);
    }

    [ContextMenu("Test - Reset Device")]
    private void Debug_ResetDevice()
    {
        ResetDevice().Forget();
    }

    [ContextMenu("Test - Show Current Info")]
    private void Debug_ShowCurrentInfo()
    {
        ChunaLogger.Log($"[LobbyUI] 현재 정보:");
        ChunaLogger.Log($"  - DeviceSN: {currentDeviceSN}");
        ChunaLogger.Log($"  - OrgID: {currentOrgID}");
        ChunaLogger.Log($"  - Username: {currentUsername}");
        ChunaLogger.Log($"  - UserID: {currentUserID}");
        ChunaLogger.Log($"  - Saved SN: {savedDeviceSN}");
    }

    [ContextMenu("Test - Check Button Connections")]
    private void Debug_CheckButtonConnections()
    {
        ChunaLogger.Log("[LobbyUI] ========== 버튼 연결 상태 확인 ==========");

        ChunaLogger.Log("시나리오 카드 버튼:");
        for (int i = 0; i < scenarioCardButtons.Length; i++)
        {
            if (scenarioCardButtons[i] != null)
            {
                ChunaLogger.Log($"  ✅ Card {i + 1}: 연결됨");
            }
            else
            {
                ChunaLogger.LogError($"  ❌ Card {i + 1}: NULL");
            }
        }

        ChunaLogger.Log("기타 버튼:");
        ChunaLogger.Log($"  - userIconButton: {(userIconButton != null ? "✅" : "❌")}");
        ChunaLogger.Log($"  - gradeBackButton: {(gradeBackButton != null ? "✅" : "❌")}");
        ChunaLogger.Log($"  - userBackButton: {(userBackButton != null ? "✅" : "❌")}");
        ChunaLogger.Log($"  - exitButton: {(exitButton != null ? "✅" : "❌")}");
        ChunaLogger.Log($"  - interactionGuideButton: {(interactionGuideButton != null ? "✅" : "❌")}");
        ChunaLogger.Log($"  - loginRequiredCloseButton (토글): {(loginRequiredCloseButton != null ? "✅" : "❌")}");

        ChunaLogger.Log("종료 확인 팝업:");
        ChunaLogger.Log($"  - exitYesToggle (토글): {(exitYesToggle != null ? "✅" : "❌")}");
        ChunaLogger.Log($"  - exitNoToggle (토글): {(exitNoToggle != null ? "✅" : "❌")}");

        ChunaLogger.Log("로그아웃 확인 팝업:");
        ChunaLogger.Log($"  - logoutCancelToggle (토글): {(logoutCancelToggle != null ? "✅" : "❌")}");
        ChunaLogger.Log($"  - logoutConfirmToggle (토글): {(logoutConfirmToggle != null ? "✅" : "❌")}");
    }
    #endregion
}
