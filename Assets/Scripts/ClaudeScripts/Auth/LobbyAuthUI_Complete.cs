using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;

/// <summary>
/// 로비 UI 통합 관리 클래스 (수정 버전)
/// 
/// [수정 사항]
/// 1. 시나리오 카드 버튼 자동 검색 및 연결
/// 2. 시나리오 카드는 항상 클릭 가능 (로그인 안 되면 팝업)
/// 3. 버튼 연결 디버그 로그 강화
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
    [SerializeField] private Button loginRequiredCloseButton;
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

    #region Button Pools
    private List<GameObject> activeGradeButtons = new List<GameObject>();
    private List<GameObject> activeUserButtons = new List<GameObject>();
    #endregion

    #region Unity Lifecycle
    private void Awake()
    {
        InitializeAuthService();
        SetupInitialUI();
        SubscribeToEvents();
    }

    private void Start()
    {
        // 초기 Guest 텍스트 설정
        if (userNameText != null)
        {
            userNameText.text = "Guest";
        }

        // 초기 안내 메시지 설정
        SetGuideMessage(loginGuideMessage);

        LoadSavedDeviceSN();
        AuthenticateDevice().Forget();
    }

    private void OnDestroy()
    {
        UnsubscribeFromEvents();

        // 애플리케이션 종료 시 자동 로그아웃
        if (!string.IsNullOrEmpty(currentUsername) && !string.IsNullOrEmpty(currentDeviceSN))
        {
            // OnDestroy에서는 async를 사용할 수 없으므로 동기 방식으로 처리
            try
            {
                Debug.Log($"[LobbyUI] OnDestroy - 로그아웃 시도: {currentUsername}");

                // UniTask를 동기적으로 실행
                PerformLogoutAsync().Forget();
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbyUI] OnDestroy 로그아웃 실패: {e.Message}");
            }
        }
    }
    #endregion

    #region Initialization
    private void InitializeAuthService()
    {
        if (useMockService)
        {
            authService = gameObject.AddComponent<MockAuthenticationService>();
            Debug.Log("[LobbyUI] Mock 서비스 사용");
        }
        else
        {
            authService = AuthenticationService.Instance;
            Debug.Log("[LobbyUI] 실제 서비스 사용");
        }
    }

    private void SetupInitialUI()
    {
        Debug.Log("[LobbyUI] ========== UI 초기화 시작 ==========");

        // 초기 상태 설정
        gradeSelectionPanel?.SetActive(false);
        userSelectionPanel?.SetActive(false);
        userInfoContent?.SetActive(true); // 항상 활성화! (로그인 전에는 "Guest" 표시)
        loginRequiredPopup?.SetActive(false);

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

        Debug.Log("[LobbyUI] ========== UI 초기화 완료 ==========");
    }

    /// <summary>
    /// 시나리오 카드를 자동으로 찾아서 연결
    /// </summary>
    private void AutoFindScenarioCards()
    {
        if (scenarioCardsContainer == null)
        {
            Debug.LogWarning("[LobbyUI] scenarioCardsContainer가 null입니다. 자동 검색을 건너뜁니다.");
            return;
        }

        Debug.Log("[LobbyUI] 시나리오 카드 자동 검색 시작...");

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
                    Debug.Log($"[LobbyUI] ✅ {cardName} 버튼 자동 연결 성공");
                }
                else
                {
                    Debug.LogWarning($"[LobbyUI] ⚠️ {cardName}에서 Button 컴포넌트를 찾을 수 없습니다.");
                }

                // CanvasGroup 찾기
                CanvasGroup canvasGroup = cardTransform.GetComponent<CanvasGroup>();
                if (canvasGroup != null)
                {
                    scenarioCardCanvasGroups[i] = canvasGroup;
                    Debug.Log($"[LobbyUI] ✅ {cardName} CanvasGroup 자동 연결 성공");
                }
            }
            else
            {
                Debug.LogWarning($"[LobbyUI] ⚠️ {cardName}을(를) 찾을 수 없습니다.");
            }
        }
    }

    /// <summary>
    /// 시나리오 카드 설정 (항상 클릭 가능)
    /// </summary>
    private void SetupScenarioCards()
    {
        Debug.Log("[LobbyUI] --- 시나리오 카드 버튼 연결 시작 ---");

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

                Debug.Log($"[LobbyUI] ✅ 시나리오 카드 {index + 1} 버튼 연결 완료");
                connectedCount++;
            }
            else
            {
                Debug.LogError($"[LobbyUI] ❌ 시나리오 카드 {i + 1} 버튼이 NULL입니다!");
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

        Debug.Log($"[LobbyUI] 시나리오 카드 버튼 연결 완료: {connectedCount}/5");
    }

    /// <summary>
    /// 다른 버튼들 연결
    /// </summary>
    private void SetupOtherButtons()
    {
        Debug.Log("[LobbyUI] --- 기타 버튼 연결 시작 ---");

        // 유저 아이콘 버튼
        if (userIconButton != null)
        {
            userIconButton.onClick.RemoveAllListeners();
            userIconButton.onClick.AddListener(OnUserIconClicked);
            Debug.Log("[LobbyUI] ✅ 유저 아이콘 버튼 연결");
        }
        else
        {
            Debug.LogWarning("[LobbyUI] ⚠️ userIconButton이 null입니다.");
        }

        // 조 선택 뒤로가기
        if (gradeBackButton != null)
        {
            gradeBackButton.onClick.RemoveAllListeners();
            gradeBackButton.onClick.AddListener(OnGradeBackButtonClicked);
            Debug.Log("[LobbyUI] ✅ 조 선택 뒤로가기 버튼 연결");
        }
        else
        {
            Debug.LogWarning("[LobbyUI] ⚠️ gradeBackButton이 null입니다.");
        }

        // 사용자 선택 뒤로가기
        if (userBackButton != null)
        {
            userBackButton.onClick.RemoveAllListeners();
            userBackButton.onClick.AddListener(OnUserBackButtonClicked);
            Debug.Log("[LobbyUI] ✅ 사용자 선택 뒤로가기 버튼 연결");
        }
        else
        {
            Debug.LogWarning("[LobbyUI] ⚠️ userBackButton이 null입니다.");
        }

        // 나가기 버튼
        if (exitButton != null)
        {
            exitButton.onClick.RemoveAllListeners();
            exitButton.onClick.AddListener(OnExitButtonClicked);
            Debug.Log("[LobbyUI] ✅ 나가기 버튼 연결");
        }
        else
        {
            Debug.LogWarning("[LobbyUI] ⚠️ exitButton이 null입니다.");
        }

        // 상호작용 가이드 버튼
        if (interactionGuideButton != null)
        {
            interactionGuideButton.onClick.RemoveAllListeners();
            interactionGuideButton.onClick.AddListener(OnInteractionGuideClicked);
            Debug.Log("[LobbyUI] ✅ 상호작용 가이드 버튼 연결");
        }
        else
        {
            Debug.LogWarning("[LobbyUI] ⚠️ interactionGuideButton이 null입니다.");
        }

        // 로그인 팝업 닫기 버튼
        if (loginRequiredCloseButton != null)
        {
            loginRequiredCloseButton.onClick.RemoveAllListeners();
            loginRequiredCloseButton.onClick.AddListener(OnLoginRequiredPopupClose);
            Debug.Log("[LobbyUI] ✅ 로그인 팝업 닫기 버튼 연결");
        }
        else
        {
            Debug.LogWarning("[LobbyUI] ⚠️ loginRequiredCloseButton이 null입니다.");
        }
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
            button.onClick.AddListener(() => gradeSelectionPanel.SetActive(false));
            Debug.Log("[LobbyUI] ✅ 조 선택 배경 클릭 연결");
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
            button.onClick.AddListener(() => userSelectionPanel.SetActive(false));
            Debug.Log("[LobbyUI] ✅ 사용자 선택 배경 클릭 연결");
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
            Debug.Log("[LobbyUI] 디바이스 인증 시작...");
            await AuthenticateDeviceWithRetry(null);
        }
        catch (Exception e)
        {
            Debug.LogError($"[LobbyUI] 인증 최종 실패: {e.Message} | UUID: {SystemInfo.deviceUniqueIdentifier}");
            ShowAuthenticationError(e.Message);
        }
    }

    private async UniTask AuthenticateDeviceWithRetry(string deviceSN, int retryCount = 0)
    {
        const int maxRetries = 2;

        try
        {
            var deviceData = await authService.AuthenticateDeviceAsync(deviceSN);

            if (deviceData == null)
            {
                throw new Exception("인증 응답 데이터가 null입니다.");
            }

            currentDeviceSN = deviceSN ?? SystemInfo.deviceUniqueIdentifier;
            currentOrgID = deviceData.orgID;

            SaveDeviceSN(currentDeviceSN);

            Debug.Log($"[LobbyUI] 인증 성공: DeviceSN={currentDeviceSN}, OrgID={currentOrgID}");

            if (deviceData.licCHUNA <= 0)
            {
                ShowLicenseError();
                return;
            }

            await LoadUserList();
        }
        catch (Exception e)
        {
            Debug.LogError($"[LobbyUI] 인증 실패 (시도 {retryCount + 1}/{maxRetries + 1}): {e.Message} | DeviceSN: {deviceSN ?? "AUTO"} | UUID: {SystemInfo.deviceUniqueIdentifier}");

            // "등록된 장치입니다" 오류 시 UUID 앞 10글자로 재시도
            if (e.Message.Contains("등록된 장치입니다"))
            {
                string uuidSubstring = SystemInfo.deviceUniqueIdentifier.Substring(0, 10);
                Debug.Log($"[LobbyUI] 등록된 장치 감지 - UUID 앞 10글자로 재시도: {uuidSubstring}");
                await AuthenticateDeviceWithRetry(uuidSubstring, 0);
                return;
            }

            if (retryCount < maxRetries)
            {
                await UniTask.Delay(1000);
                await AuthenticateDeviceWithRetry(savedDeviceSN, retryCount + 1);
            }
            else
            {
                throw;
            }
        }
    }

    private async UniTask LoadUserList()
    {
        try
        {
            Debug.Log($"[LobbyUI] 사용자 목록 로드 시작: {currentOrgID}");

            allUsers = await authService.GetUserListAsync(currentOrgID);

            if (allUsers == null || allUsers.Length == 0)
            {
                Debug.LogWarning("[LobbyUI] 사용자 목록이 비어있습니다.");
                return;
            }

            OrganizeUsersByGrade();

            Debug.Log($"[LobbyUI] 사용자 목록 로드 완료: {allUsers.Length}명");
        }
        catch (Exception e)
        {
            Debug.LogError($"[LobbyUI] 사용자 목록 로드 실패: {e.Message}");
        }
    }

    private void OrganizeUsersByGrade()
    {
        usersByGrade.Clear();

        foreach (var user in allUsers)
        {
            if (!usersByGrade.ContainsKey(user.grade))
            {
                usersByGrade[user.grade] = new List<UserData>();
            }
            usersByGrade[user.grade].Add(user);
        }

        Debug.Log($"[LobbyUI] 조별 분류 완료: {usersByGrade.Count}개 조");
    }
    #endregion

    #region Button Click Handlers
    /// <summary>
    /// 시나리오 카드 클릭 핸들러
    /// </summary>
    public void OnScenarioCardClicked(int scenarioIndex)
    {
        Debug.Log($"[LobbyUI] ========== 시나리오 카드 {scenarioIndex + 1} 클릭 ==========");

        // 로그인 체크
        if (string.IsNullOrEmpty(currentUsername))
        {
            Debug.LogWarning("[LobbyUI] ⚠️ 로그인이 필요합니다!");
            ShowLoginRequiredPopup();
            return;
        }

        Debug.Log($"[LobbyUI] ✅ 시나리오 {scenarioIndex + 1} 시작: 사용자={currentUsername}");

        // TODO: 시나리오 씬 로드
        SceneManager.LoadScene($"Scenario_{scenarioIndex + 1}");
    }

    private void OnUserIconClicked()
    {
        Debug.Log("[LobbyUI] 유저 아이콘 클릭");
        ShowGradeSelectionPanel();
    }

    private void OnGradeBackButtonClicked()
    {
        Debug.Log("[LobbyUI] 조 선택 뒤로가기 클릭");
        HideGradeSelectionPanel();
    }

    private void OnUserBackButtonClicked()
    {
        Debug.Log("[LobbyUI] 사용자 선택 뒤로가기 클릭");
        HideUserSelectionPanel();
        ShowGradeSelectionPanel();
    }

    private void OnExitButtonClicked()
    {
        Debug.Log("[LobbyUI] 나가기 버튼 클릭");
        ExitApplication().Forget();
    }

    /// <summary>
    /// 애플리케이션 종료 (로그아웃 후)
    /// </summary>
    private async UniTaskVoid ExitApplication()
    {
        // 로그인되어 있으면 먼저 로그아웃
        if (!string.IsNullOrEmpty(currentUsername))
        {
            Debug.Log("[LobbyUI] 종료 전 로그아웃 진행...");

            try
            {
                await PerformLogoutAsync();
                Debug.Log("[LobbyUI] 로그아웃 완료, 애플리케이션 종료");
            }
            catch (Exception e)
            {
                Debug.LogError($"[LobbyUI] 로그아웃 실패했지만 종료 진행: {e.Message}");
            }
        }
        else
        {
            Debug.Log("[LobbyUI] 로그인 안 되어 있음, 바로 종료");
        }

        // 애플리케이션 종료
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void OnInteractionGuideClicked()
    {
        Debug.Log("[LobbyUI] 상호작용 가이드 버튼 클릭");
        // TODO: 상호작용 가이드 표시
    }
    #endregion

    #region Grade Selection Panel
    private void ShowGradeSelectionPanel()
    {
        if (gradeSelectionPanel == null)
        {
            Debug.LogError("[LobbyUI] gradeSelectionPanel이 null입니다!");
            return;
        }

        gradeSelectionPanel.SetActive(true);
        CreateGradeButtons();
        Debug.Log("[LobbyUI] 조 선택 패널 표시");
    }

    private void HideGradeSelectionPanel()
    {
        if (gradeSelectionPanel != null)
        {
            gradeSelectionPanel.SetActive(false);
        }

        ClearGradeButtons();
        Debug.Log("[LobbyUI] 조 선택 패널 숨김");
    }

    private void CreateGradeButtons()
    {
        ClearGradeButtons();

        if (gradeButtonPrefab == null || gradeContentContainer == null)
        {
            Debug.LogError("[LobbyUI] gradeButtonPrefab 또는 gradeContentContainer가 null입니다!");
            return;
        }

        foreach (var kvp in usersByGrade)
        {
            string grade = kvp.Key;
            GameObject buttonObj = Instantiate(gradeButtonPrefab, gradeContentContainer);

            // 강제 활성화
            buttonObj.SetActive(true);

            // RectTransform 설정
            var rectTransform = buttonObj.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                rectTransform.sizeDelta = new Vector2(280, 60);
            }

            // LayoutElement 추가/설정
            var layoutElement = buttonObj.GetComponent<LayoutElement>();
            if (layoutElement == null)
            {
                layoutElement = buttonObj.AddComponent<LayoutElement>();
            }
            layoutElement.minHeight = 60;
            layoutElement.preferredHeight = 60;

            var textComponent = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
            if (textComponent != null)
            {
                textComponent.text = grade;
                textComponent.color = Color.white; // 텍스트 색상 명시
                Debug.Log($"[LobbyUI] 조 버튼 텍스트 설정: {grade}");
            }
            else
            {
                Debug.LogWarning($"[LobbyUI] 조 버튼에서 TextMeshProUGUI를 찾을 수 없습니다!");
            }

            var button = buttonObj.GetComponent<Button>();
            if (button != null)
            {
                button.onClick.AddListener(() => OnGradeSelected(grade));
            }

            activeGradeButtons.Add(buttonObj);
        }

        Debug.Log($"[LobbyUI] 조 버튼 생성 완료: {activeGradeButtons.Count}개");

        // Content 크기 조정
        AdjustContentSize(gradeContentContainer, activeGradeButtons.Count);
    }

    private void ClearGradeButtons()
    {
        foreach (var button in activeGradeButtons)
        {
            if (button != null)
            {
                Destroy(button);
            }
        }
        activeGradeButtons.Clear();
    }

    private void OnGradeSelected(string grade)
    {
        selectedGrade = grade;
        Debug.Log($"[LobbyUI] 조 선택: {grade}");

        HideGradeSelectionPanel();
        ShowUserSelectionPanel();
    }
    #endregion

    #region User Selection Panel
    private void ShowUserSelectionPanel()
    {
        if (userSelectionPanel == null)
        {
            Debug.LogError("[LobbyUI] userSelectionPanel이 null입니다!");
            return;
        }

        userSelectionPanel.SetActive(true);
        CreateUserButtons();
        Debug.Log("[LobbyUI] 사용자 선택 패널 표시");
    }

    private void HideUserSelectionPanel()
    {
        if (userSelectionPanel != null)
        {
            userSelectionPanel.SetActive(false);
        }

        ClearUserButtons();
        Debug.Log("[LobbyUI] 사용자 선택 패널 숨김");
    }

    private void CreateUserButtons()
    {
        ClearUserButtons();

        if (userButtonPrefab == null || userContentContainer == null)
        {
            Debug.LogError("[LobbyUI] userButtonPrefab 또는 userContentContainer가 null입니다!");
            return;
        }

        if (!usersByGrade.ContainsKey(selectedGrade))
        {
            Debug.LogWarning($"[LobbyUI] 선택된 조에 사용자가 없습니다: {selectedGrade}");
            return;
        }

        var users = usersByGrade[selectedGrade];

        foreach (var user in users)
        {
            GameObject buttonObj = Instantiate(userButtonPrefab, userContentContainer);

            // 강제 활성화
            buttonObj.SetActive(true);

            // RectTransform 설정
            var rectTransform = buttonObj.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                rectTransform.sizeDelta = new Vector2(280, 60);
            }

            // LayoutElement 추가/설정
            var layoutElement = buttonObj.GetComponent<LayoutElement>();
            if (layoutElement == null)
            {
                layoutElement = buttonObj.AddComponent<LayoutElement>();
            }
            layoutElement.minHeight = 60;
            layoutElement.preferredHeight = 60;

            var textComponent = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
            if (textComponent != null)
            {
                textComponent.text = user.username;
                textComponent.color = Color.white; // 텍스트 색상 명시
                Debug.Log($"[LobbyUI] 사용자 버튼 텍스트 설정: {user.username}");
            }
            else
            {
                Debug.LogWarning($"[LobbyUI] 사용자 버튼에서 TextMeshProUGUI를 찾을 수 없습니다!");
            }

            var button = buttonObj.GetComponent<Button>();
            if (button != null)
            {
                int userId = user.idx;
                string username = user.username;
                button.onClick.AddListener(() => OnUserSelected(userId, username));
            }

            activeUserButtons.Add(buttonObj);
        }

        Debug.Log($"[LobbyUI] 사용자 버튼 생성 완료: {activeUserButtons.Count}개");

        // Content 크기 조정
        AdjustContentSize(userContentContainer, activeUserButtons.Count);
    }

    private void ClearUserButtons()
    {
        foreach (var button in activeUserButtons)
        {
            if (button != null)
            {
                Destroy(button);
            }
        }
        activeUserButtons.Clear();
    }

    /// <summary>
    /// Content의 크기를 버튼 개수에 맞게 조정
    /// </summary>
    private void AdjustContentSize(Transform content, int buttonCount)
    {
        if (content == null) return;

        var rectTransform = content.GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            // 버튼 높이(60) + 간격(10) * 개수
            float totalHeight = (60 + 10) * buttonCount;
            rectTransform.sizeDelta = new Vector2(rectTransform.sizeDelta.x, totalHeight);

            Debug.Log($"[LobbyUI] Content 크기 조정: {totalHeight}");
        }

        // Vertical Layout Group 추가 (없으면)
        var layout = content.GetComponent<VerticalLayoutGroup>();
        if (layout == null)
        {
            layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.spacing = 10;
            layout.padding = new RectOffset(10, 10, 10, 10);

            Debug.Log($"[LobbyUI] VerticalLayoutGroup 추가됨");
        }
    }

    private void OnUserSelected(int userId, string username)
    {
        Debug.Log($"[LobbyUI] 사용자 선택: {username} (ID: {userId})");
        PerformLogin(userId, username).Forget();
    }
    #endregion

    #region Login/Logout
    private async UniTaskVoid PerformLogin(int userId, string username)
    {
        try
        {
            Debug.Log($"[LobbyUI] 로그인 시작: {username}");

            try
            {
                var mirroringData = await authService.LogonAsync(
                    currentDeviceSN,
                    username,
                    "VR_CHUNA"
                );

                Debug.Log($"[LobbyUI] 미러링 데이터 수신 완료");
            }
            catch (Exception logonException)
            {
                // 404 에러(serverIP not found)는 경고로 처리하고 로그인 진행
                if (logonException.Message.Contains("404") || logonException.Message.Contains("serverIP not found"))
                {
                    Debug.LogWarning($"[LobbyUI] 미러링 정보 없음 (무시하고 진행): {logonException.Message}");
                }
                else
                {
                    // 다른 에러는 재발생
                    throw;
                }
            }

            // 로그인 정보 저장
            currentUserID = userId;
            currentUsername = username;

            HideUserSelectionPanel();
            UpdateUserInfoPanel();

            // 시나리오 카드 활성화
            SetScenarioCardsVisualState(true);

            // 안내 메시지 변경
            SetGuideMessage(scenarioGuideMessage);

            Debug.Log($"[LobbyUI] ✅ 로그인 완료: {username} (ID: {userId})");
        }
        catch (Exception e)
        {
            Debug.LogError($"[LobbyUI] ❌ 로그인 실패: {e.Message}");
            ShowLoginError(e.Message);
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
        try
        {
            if (string.IsNullOrEmpty(currentUsername))
            {
                return;
            }

            Debug.Log($"[LobbyUI] 로그아웃 시작: {currentUsername}");

            await authService.LogoffAsync(
                currentDeviceSN,
                currentUsername,
                "VR_CHUNA"
            );

            ClearUserInfo();

            Debug.Log($"[LobbyUI] 로그아웃 완료");
        }
        catch (Exception e)
        {
            Debug.LogError($"[LobbyUI] 로그아웃 실패: {e.Message}");
            throw;
        }
    }
    #endregion

    #region Auth Event Handlers
    private void OnAuthenticationSuccess(string deviceSN)
    {
        Debug.Log($"[LobbyUI] [이벤트] 인증 성공: {deviceSN}");
    }

    private void OnAuthenticationFailed(string errorMessage)
    {
        Debug.LogError($"[LobbyUI] [이벤트] 인증 실패: {errorMessage}");
    }

    private void OnUserListLoadCompleted(int userCount)
    {
        Debug.Log($"[LobbyUI] [이벤트] 사용자 목록 로드 완료: {userCount}명");
    }

    private void OnUserListLoadFailed(string errorMessage)
    {
        Debug.LogError($"[LobbyUI] [이벤트] 사용자 목록 로드 실패: {errorMessage}");
    }

    private void OnLoginSuccess(string username, int userID)
    {
        Debug.Log($"[LobbyUI] [이벤트] 로그인 성공: {username}");
    }

    private void OnLoginFailed(string username, string errorMessage)
    {
        Debug.LogError($"[LobbyUI] [이벤트] 로그인 실패: {username} - {errorMessage}");
    }

    private void OnLogoutCompleted(string username)
    {
        Debug.Log($"[LobbyUI] [이벤트] 로그아웃 완료: {username}");
    }
    #endregion

    #region UI Update
    private void UpdateUserInfoPanel()
    {
        // userInfoContent는 항상 활성화되어 있음 (초기화 시 true로 설정됨)
        // 로그인/로그아웃 시 텍스트만 변경
        if (userNameText != null)
        {
            userNameText.text = currentUsername;
        }

        Debug.Log("[LobbyUI] 사용자 정보 패널 업데이트");
    }

    private void ClearUserInfo()
    {
        currentUsername = string.Empty;
        currentUserID = 0;

        // 텍스트만 "Guest"로 변경 (버튼은 계속 보임)
        if (userNameText != null)
        {
            userNameText.text = "Guest";
        }

        // userInfoContent는 계속 활성화 상태 유지!
        // if (userInfoContent != null)
        // {
        //     userInfoContent.SetActive(false); // 더 이상 숨기지 않음!
        // }

        // 시나리오 카드 시각적으로 비활성화
        SetScenarioCardsVisualState(false);

        // 안내 메시지 복원
        SetGuideMessage(loginGuideMessage);

        Debug.Log("[LobbyUI] 사용자 정보 초기화 (Guest로 표시)");
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

        Debug.Log($"[LobbyUI] 시나리오 카드 시각 상태: {(active ? "활성" : "비활성")} (Alpha: {targetAlpha})");
    }

    /// <summary>
    /// 안내 메시지 설정
    /// </summary>
    private void SetGuideMessage(string message)
    {
        if (guideMessageText != null)
        {
            guideMessageText.text = message;
            Debug.Log($"[LobbyUI] 안내 메시지 변경: {message}");
        }
        else
        {
            Debug.LogWarning("[LobbyUI] guideMessageText가 연결되지 않았습니다.");
        }
    }

    private void ShowLoginRequiredPopup()
    {
        if (loginRequiredPopup != null)
        {
            loginRequiredPopup.SetActive(true);
            Debug.Log("[LobbyUI] 로그인 필요 팝업 표시");
        }
        else
        {
            Debug.LogWarning("[LobbyUI] loginRequiredPopup이 연결되지 않았습니다.");
        }
    }

    private void OnLoginRequiredPopupClose()
    {
        if (loginRequiredPopup != null)
        {
            loginRequiredPopup.SetActive(false);
            Debug.Log("[LobbyUI] 로그인 필요 팝업 닫기");
        }
    }
    #endregion

    #region Error Handling
    private void ShowAuthenticationError(string errorMessage)
    {
        Debug.LogError($"[LobbyUI] 인증 오류 표시: {errorMessage}");
        // TODO: 에러 팝업 표시
    }

    private void ShowLicenseError()
    {
        Debug.LogError("[LobbyUI] 라이선스 오류");
        // TODO: 라이선스 에러 팝업 표시
    }

    private void ShowLoginError(string errorMessage)
    {
        Debug.LogError($"[LobbyUI] 로그인 오류 표시: {errorMessage}");
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
            Debug.LogWarning("[LobbyUI] DeviceSN이 없어 초기화할 수 없습니다.");
            return;
        }

        try
        {
            Debug.Log($"[LobbyUI] 디바이스 초기화 시작: {currentDeviceSN}");

            await authService.ResetDeviceAsync(currentDeviceSN, "VR_CHUNA");

            ClearSavedDeviceSN();
            currentDeviceSN = string.Empty;
            currentOrgID = string.Empty;
            currentUserID = 0;
            currentUsername = string.Empty;

            Debug.Log("[LobbyUI] 디바이스 초기화 완료");

            // TODO: 인증 씬으로 이동
            // SceneManager.LoadScene("AuthMain");
        }
        catch (Exception e)
        {
            Debug.LogError($"[LobbyUI] 디바이스 초기화 실패: {e.Message}");
        }
    }
    #endregion

    #region PlayerPrefs Management
    private void LoadSavedDeviceSN()
    {
        if (PlayerPrefs.HasKey("DEVICE_SN"))
        {
            savedDeviceSN = PlayerPrefs.GetString("DEVICE_SN");
            Debug.Log($"[LobbyUI] 저장된 DeviceSN 로드: {savedDeviceSN}");
        }
        else
        {
            savedDeviceSN = string.Empty;
            Debug.Log("[LobbyUI] 저장된 DeviceSN 없음");
        }
    }

    private void SaveDeviceSN(string deviceSN)
    {
        PlayerPrefs.SetString("DEVICE_SN", deviceSN);
        PlayerPrefs.Save();
        savedDeviceSN = deviceSN;
        Debug.Log($"[LobbyUI] DeviceSN 저장: {deviceSN}");
    }

    private void ClearSavedDeviceSN()
    {
        PlayerPrefs.DeleteKey("DEVICE_SN");
        PlayerPrefs.Save();
        savedDeviceSN = string.Empty;
        Debug.Log("[LobbyUI] 저장된 DeviceSN 삭제");
    }
    #endregion

    #region Debug Helpers
    [ContextMenu("Test - Show Grade Selection")]
    private void Debug_ShowGradeSelection()
    {
        ShowGradeSelectionPanel();
    }

    [ContextMenu("Test - Hide All Panels")]
    private void Debug_HideAllPanels()
    {
        HideGradeSelectionPanel();
        HideUserSelectionPanel();
    }

    [ContextMenu("Test - Show Login Popup")]
    private void Debug_ShowLoginPopup()
    {
        ShowLoginRequiredPopup();
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
        Debug.Log($"[LobbyUI] 현재 정보:");
        Debug.Log($"  - DeviceSN: {currentDeviceSN}");
        Debug.Log($"  - OrgID: {currentOrgID}");
        Debug.Log($"  - Username: {currentUsername}");
        Debug.Log($"  - UserID: {currentUserID}");
        Debug.Log($"  - Saved SN: {savedDeviceSN}");
    }

    [ContextMenu("Test - Check Button Connections")]
    private void Debug_CheckButtonConnections()
    {
        Debug.Log("[LobbyUI] ========== 버튼 연결 상태 확인 ==========");

        Debug.Log("시나리오 카드 버튼:");
        for (int i = 0; i < scenarioCardButtons.Length; i++)
        {
            if (scenarioCardButtons[i] != null)
            {
                Debug.Log($"  ✅ Card {i + 1}: 연결됨");
            }
            else
            {
                Debug.LogError($"  ❌ Card {i + 1}: NULL");
            }
        }

        Debug.Log("기타 버튼:");
        Debug.Log($"  - userIconButton: {(userIconButton != null ? "✅" : "❌")}");
        Debug.Log($"  - gradeBackButton: {(gradeBackButton != null ? "✅" : "❌")}");
        Debug.Log($"  - userBackButton: {(userBackButton != null ? "✅" : "❌")}");
        Debug.Log($"  - exitButton: {(exitButton != null ? "✅" : "❌")}");
        Debug.Log($"  - interactionGuideButton: {(interactionGuideButton != null ? "✅" : "❌")}");
        Debug.Log($"  - loginRequiredCloseButton: {(loginRequiredCloseButton != null ? "✅" : "❌")}");
    }
    #endregion
}