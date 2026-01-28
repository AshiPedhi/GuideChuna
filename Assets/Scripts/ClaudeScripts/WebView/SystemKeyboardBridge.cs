using UnityEngine;
using TLab.WebView;

/// <summary>
/// TLabWebView에서 Meta Quest 시스템 키보드를 사용하도록 연결
/// VKeyboard 대신 이 스크립트를 사용
/// </summary>
public class SystemKeyboardBridge : MonoBehaviour
{
    [Header("=== WebView ===")]
    [Tooltip("Browser 컴포넌트 (WebView 또는 GeckoView)")]
    [SerializeField] private Browser browser;

    [Header("=== 설정 ===")]
    [Tooltip("키보드 타입")]
    [SerializeField] private TouchScreenKeyboardType keyboardType = TouchScreenKeyboardType.Default;

    private TouchScreenKeyboard keyboard;
    private bool isKeyboardOpen = false;

    void Update()
    {
        HandleKeyboardInput();
    }

    /// <summary>
    /// 시스템 키보드 열기 (외부에서 호출)
    /// </summary>
    public void OpenKeyboard()
    {
        OpenSystemKeyboard("");
    }

    /// <summary>
    /// 시스템 키보드 열기 (초기 텍스트 지정)
    /// </summary>
    public void OpenSystemKeyboard(string initialText = "")
    {
        keyboard = TouchScreenKeyboard.Open(
            initialText,
            keyboardType,
            false,  // autocorrection
            false,  // multiline
            false,  // secure
            false,  // alert
            ""      // placeholder
        );

        isKeyboardOpen = true;
        Debug.Log($"[SystemKeyboard] Opened with: {initialText}");
    }

    /// <summary>
    /// 키보드 입력 처리
    /// </summary>
    private void HandleKeyboardInput()
    {
        if (keyboard == null || !isKeyboardOpen) return;

        // 입력 완료
        if (keyboard.status == TouchScreenKeyboard.Status.Done)
        {
            string text = keyboard.text;

            // Browser에 텍스트 입력 (각 문자를 키 이벤트로 전송)
            if (browser != null && !string.IsNullOrEmpty(text))
            {
                foreach (char c in text)
                {
                    browser.KeyEvent(c);
                }
            }

            Debug.Log($"[SystemKeyboard] Done: {text}");

            keyboard = null;
            isKeyboardOpen = false;
        }

        // 취소
        if (keyboard.status == TouchScreenKeyboard.Status.Canceled)
        {
            Debug.Log("[SystemKeyboard] Canceled");

            keyboard = null;
            isKeyboardOpen = false;
        }
    }

    /// <summary>
    /// Browser 설정
    /// </summary>
    public void SetBrowser(Browser newBrowser)
    {
        browser = newBrowser;
    }
}
