using UnityEngine;
using UnityEngine.UI;
using TMPro;
using TLab.WebView;

/// <summary>
/// WebView 브라우저 UI 컨트롤러
/// - Meta Quest 시스템 키보드로 URL 입력
/// - 뒤로가기, 앞으로가기, 새로고침 기능
/// </summary>
public class WebViewBrowserUI : MonoBehaviour
{
    [Header("=== WebView ===")]
    [Tooltip("Browser 컴포넌트 (WebView 또는 GeckoView)")]
    [SerializeField] private Browser browser;

    [Header("=== URL 입력 ===")]
    [Tooltip("URL 표시/입력 텍스트")]
    [SerializeField] private TMP_Text urlDisplayText;

    [Tooltip("URL 입력 버튼 (클릭하면 키보드 열림)")]
    [SerializeField] private Button urlInputButton;

    [Header("=== 네비게이션 버튼 ===")]
    [SerializeField] private Button backButton;
    [SerializeField] private Button forwardButton;
    [SerializeField] private Button refreshButton;
    [SerializeField] private Button homeButton;

    [Header("=== 설정 ===")]
    [Tooltip("홈 URL")]
    [SerializeField] private string homeUrl = "https://www.google.com";

    [Tooltip("URL 자동 업데이트 간격 (초)")]
    [SerializeField] private float urlUpdateInterval = 1f;

    [Tooltip("디버그 로그")]
    [SerializeField] private bool showDebugLogs = false;

    // 시스템 키보드
    private TouchScreenKeyboard keyboard;
    private string currentInputText = "";
    private float lastUrlUpdateTime;

    void Start()
    {
        SetupButtons();
        UpdateUrlDisplay();
    }

    void Update()
    {
        // 시스템 키보드 입력 처리
        HandleKeyboardInput();

        // URL 표시 주기적 업데이트
        if (Time.time - lastUrlUpdateTime > urlUpdateInterval)
        {
            UpdateUrlDisplay();
            lastUrlUpdateTime = Time.time;
        }
    }

    /// <summary>
    /// 버튼 이벤트 설정
    /// </summary>
    private void SetupButtons()
    {
        if (urlInputButton != null)
            urlInputButton.onClick.AddListener(OpenKeyboard);

        if (backButton != null)
            backButton.onClick.AddListener(GoBack);

        if (forwardButton != null)
            forwardButton.onClick.AddListener(GoForward);

        if (refreshButton != null)
            refreshButton.onClick.AddListener(Refresh);

        if (homeButton != null)
            homeButton.onClick.AddListener(GoHome);

        if (showDebugLogs)
            Debug.Log("[BrowserUI] Buttons setup complete");
    }

    /// <summary>
    /// Meta Quest 시스템 키보드 열기
    /// </summary>
    public void OpenKeyboard()
    {
        // 현재 URL을 초기값으로 설정
        string initialText = "";
        if (browser != null)
        {
            initialText = browser.GetUrl();
        }

        keyboard = TouchScreenKeyboard.Open(
            initialText,
            TouchScreenKeyboardType.URL,
            false,  // autocorrection
            false,  // multiline
            false,  // secure
            false,  // alert
            "Enter URL"
        );

        currentInputText = initialText;

        if (showDebugLogs)
            Debug.Log($"[BrowserUI] Keyboard opened with: {initialText}");
    }

    /// <summary>
    /// 키보드 입력 처리
    /// </summary>
    private void HandleKeyboardInput()
    {
        if (keyboard == null) return;

        // 키보드가 활성화되어 있는 동안 텍스트 업데이트
        if (keyboard.status == TouchScreenKeyboard.Status.Visible)
        {
            currentInputText = keyboard.text;
        }

        // 입력 완료
        if (keyboard.status == TouchScreenKeyboard.Status.Done)
        {
            string url = keyboard.text;

            if (!string.IsNullOrEmpty(url))
            {
                // http/https 없으면 추가
                if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                {
                    url = "https://" + url;
                }

                LoadUrl(url);
            }

            keyboard = null;

            if (showDebugLogs)
                Debug.Log($"[BrowserUI] Keyboard Done, URL: {url}");
        }

        // 취소됨
        if (keyboard.status == TouchScreenKeyboard.Status.Canceled)
        {
            keyboard = null;

            if (showDebugLogs)
                Debug.Log("[BrowserUI] Keyboard Canceled");
        }
    }

    /// <summary>
    /// URL 표시 업데이트
    /// </summary>
    private void UpdateUrlDisplay()
    {
        if (urlDisplayText != null && browser != null)
        {
            string currentUrl = browser.GetUrl();
            if (!string.IsNullOrEmpty(currentUrl))
            {
                urlDisplayText.text = currentUrl;
            }
        }
    }

    #region Navigation Methods

    /// <summary>
    /// URL 로드
    /// </summary>
    public void LoadUrl(string url)
    {
        if (browser != null)
        {
            browser.LoadUrl(url);
            UpdateUrlDisplay();

            if (showDebugLogs)
                Debug.Log($"[BrowserUI] Loading: {url}");
        }
    }

    /// <summary>
    /// 뒤로 가기
    /// </summary>
    public void GoBack()
    {
        if (browser != null)
        {
            browser.GoBack();

            if (showDebugLogs)
                Debug.Log("[BrowserUI] Go Back");
        }
    }

    /// <summary>
    /// 앞으로 가기
    /// </summary>
    public void GoForward()
    {
        if (browser != null)
        {
            browser.GoForward();

            if (showDebugLogs)
                Debug.Log("[BrowserUI] Go Forward");
        }
    }

    /// <summary>
    /// 새로고침
    /// </summary>
    public void Refresh()
    {
        if (browser != null)
        {
            string currentUrl = browser.GetUrl();
            if (!string.IsNullOrEmpty(currentUrl))
            {
                browser.LoadUrl(currentUrl);

                if (showDebugLogs)
                    Debug.Log($"[BrowserUI] Refresh: {currentUrl}");
            }
        }
    }

    /// <summary>
    /// 홈으로 이동
    /// </summary>
    public void GoHome()
    {
        if (browser != null && !string.IsNullOrEmpty(homeUrl))
        {
            browser.LoadUrl(homeUrl);

            if (showDebugLogs)
                Debug.Log($"[BrowserUI] Go Home: {homeUrl}");
        }
    }

    #endregion

    #region Public Utility Methods

    /// <summary>
    /// 현재 URL 가져오기
    /// </summary>
    public string GetCurrentUrl()
    {
        return browser != null ? browser.GetUrl() : string.Empty;
    }

    /// <summary>
    /// Browser 설정
    /// </summary>
    public void SetBrowser(Browser newBrowser)
    {
        browser = newBrowser;
        UpdateUrlDisplay();
    }

    /// <summary>
    /// 홈 URL 설정
    /// </summary>
    public void SetHomeUrl(string url)
    {
        homeUrl = url;
    }

    #endregion
}
