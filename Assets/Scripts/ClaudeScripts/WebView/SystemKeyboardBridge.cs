using UnityEngine;
using TLab.WebView;

/// <summary>
/// TLabWebView에서 Meta Quest 시스템 키보드를 사용하도록 연결
/// VKeyboard 대신 이 스크립트를 사용
/// </summary>
public class SystemKeyboardBridge : MonoBehaviour
{
    [Header("=== WebView ===")]
    [SerializeField] private BrowserInputField browserInputField;

    private TouchScreenKeyboard keyboard;
    private bool isKeyboardOpen = false;

    void Start()
    {
        // BrowserInputField의 키보드 이벤트 연결
        if (browserInputField != null)
        {
            // TLabWebView의 포커스 이벤트에 연결
            browserInputField.onFocus.AddListener(OnInputFieldFocused);
        }
    }

    void Update()
    {
        HandleKeyboardInput();
    }

    /// <summary>
    /// 입력 필드 포커스 시 시스템 키보드 열기
    /// </summary>
    private void OnInputFieldFocused(string currentText)
    {
        OpenSystemKeyboard(currentText);
    }

    /// <summary>
    /// 시스템 키보드 열기 (외부에서 호출 가능)
    /// </summary>
    public void OpenSystemKeyboard(string initialText = "")
    {
        keyboard = TouchScreenKeyboard.Open(
            initialText,
            TouchScreenKeyboardType.Default,
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

            // BrowserInputField에 텍스트 전달
            if (browserInputField != null)
            {
                browserInputField.OnSubmit(text);
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
}
