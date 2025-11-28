using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 학습 완료 후 퀴즈 패널
/// "다음부터 보지 않기" 토글 기능 포함
/// </summary>
public class QuizPanel : MonoBehaviour
{
    [Header("=== UI 요소 ===")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI descriptionText;
    [SerializeField] private Toggle dontShowAgainToggle;
    [SerializeField] private Button startQuizButton;
    [SerializeField] private Button skipButton;

    [Header("=== 설정 ===")]
    [SerializeField] private string playerPrefsKey = "QuizSkipped";

    // 이벤트
    public System.Action OnQuizStarted;
    public System.Action OnQuizSkipped;

    void Start()
    {
        // 버튼 이벤트 연결
        if (startQuizButton != null)
            startQuizButton.onClick.AddListener(OnStartQuizClicked);

        if (skipButton != null)
            skipButton.onClick.AddListener(OnSkipClicked);

        // 패널 숨김
        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    /// <summary>
    /// 퀴즈 패널 표시 (학습 완료 후 호출)
    /// </summary>
    public void ShowQuizPanel()
    {
        // 스킵 설정 확인
        bool isSkipped = PlayerPrefs.GetInt(playerPrefsKey, 0) == 1;

        if (isSkipped)
        {
            Debug.Log("[QuizPanel] 퀴즈가 스킵 설정되어 있어 표시하지 않습니다.");
            OnQuizSkipped?.Invoke();
            return;
        }

        // 패널 표시
        if (panelRoot != null)
            panelRoot.SetActive(true);

        // 토글 초기화 (항상 off로 시작)
        if (dontShowAgainToggle != null)
            dontShowAgainToggle.isOn = false;

        Debug.Log("[QuizPanel] 퀴즈 패널 표시");
    }

    /// <summary>
    /// 퀴즈 시작 버튼 클릭
    /// </summary>
    private void OnStartQuizClicked()
    {
        // 토글 상태 저장
        SaveSkipPreference();

        // 패널 숨김
        HidePanel();

        // 이벤트 발생
        OnQuizStarted?.Invoke();

        Debug.Log("[QuizPanel] 퀴즈 시작");
    }

    /// <summary>
    /// 스킵 버튼 클릭
    /// </summary>
    private void OnSkipClicked()
    {
        // 토글 상태 저장
        SaveSkipPreference();

        // 패널 숨김
        HidePanel();

        // 이벤트 발생
        OnQuizSkipped?.Invoke();

        Debug.Log("[QuizPanel] 퀴즈 스킵");
    }

    /// <summary>
    /// 스킵 설정 저장
    /// </summary>
    private void SaveSkipPreference()
    {
        if (dontShowAgainToggle != null && dontShowAgainToggle.isOn)
        {
            PlayerPrefs.SetInt(playerPrefsKey, 1);
            PlayerPrefs.Save();
            Debug.Log("[QuizPanel] '다음부터 보지 않기' 설정 저장됨");
        }
    }

    /// <summary>
    /// 패널 숨김
    /// </summary>
    private void HidePanel()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    /// <summary>
    /// 스킵 설정 초기화 (디버그용)
    /// </summary>
    [ContextMenu("Reset Skip Preference")]
    public void ResetSkipPreference()
    {
        PlayerPrefs.DeleteKey(playerPrefsKey);
        PlayerPrefs.Save();
        Debug.Log("[QuizPanel] 스킵 설정 초기화됨");
    }

    /// <summary>
    /// 스킵 여부 확인
    /// </summary>
    public bool IsSkipped()
    {
        return PlayerPrefs.GetInt(playerPrefsKey, 0) == 1;
    }
}
