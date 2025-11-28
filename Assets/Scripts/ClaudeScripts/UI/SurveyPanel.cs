using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 로그아웃/종료 시 설문 패널
/// "다음부터 보지 않기" 토글 기능 포함
/// </summary>
public class SurveyPanel : MonoBehaviour
{
    [Header("=== UI 요소 ===")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI descriptionText;
    [SerializeField] private Toggle dontShowAgainToggle;
    [SerializeField] private Button startSurveyButton;
    [SerializeField] private Button skipButton;

    [Header("=== 설정 ===")]
    [SerializeField] private string playerPrefsKey = "SurveySkipped";

    // 이벤트
    public System.Action OnSurveyCompleted;
    public System.Action OnSurveySkipped;

    // 종료 대기 중 플래그
    private bool isWaitingForAction = false;
    private System.Action pendingAction; // 원래 진행할 액션 (로그아웃, 종료 등)

    void Start()
    {
        // 버튼 이벤트 연결
        if (startSurveyButton != null)
            startSurveyButton.onClick.AddListener(OnStartSurveyClicked);

        if (skipButton != null)
            skipButton.onClick.AddListener(OnSkipClicked);

        // 패널 숨김
        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    /// <summary>
    /// 설문 패널 표시 (로그아웃/종료 시 호출)
    /// </summary>
    /// <param name="onCompleteAction">설문 완료 후 실행할 액션 (로그아웃, 종료 등)</param>
    public void ShowSurveyPanel(System.Action onCompleteAction = null)
    {
        // 스킵 설정 확인
        bool isSkipped = PlayerPrefs.GetInt(playerPrefsKey, 0) == 1;

        if (isSkipped)
        {
            Debug.Log("[SurveyPanel] 설문이 스킵 설정되어 있어 바로 진행합니다.");
            onCompleteAction?.Invoke();
            return;
        }

        // 패널 표시
        if (panelRoot != null)
            panelRoot.SetActive(true);

        // 토글 초기화 (항상 off로 시작)
        if (dontShowAgainToggle != null)
            dontShowAgainToggle.isOn = false;

        // 완료 후 액션 저장
        pendingAction = onCompleteAction;
        isWaitingForAction = true;

        Debug.Log("[SurveyPanel] 설문 패널 표시");
    }

    /// <summary>
    /// 설문 시작 버튼 클릭
    /// </summary>
    private void OnStartSurveyClicked()
    {
        // 토글 상태 저장
        SaveSkipPreference();

        // 패널 숨김
        HidePanel();

        // 이벤트 발생
        OnSurveyCompleted?.Invoke();

        Debug.Log("[SurveyPanel] 설문 시작");

        // 원래 액션 실행
        ExecutePendingAction();
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
        OnSurveySkipped?.Invoke();

        Debug.Log("[SurveyPanel] 설문 스킵");

        // 원래 액션 실행
        ExecutePendingAction();
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
            Debug.Log("[SurveyPanel] '다음부터 보지 않기' 설정 저장됨");
        }
    }

    /// <summary>
    /// 패널 숨김
    /// </summary>
    private void HidePanel()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);

        isWaitingForAction = false;
    }

    /// <summary>
    /// 대기 중인 액션 실행 (로그아웃, 종료 등)
    /// </summary>
    private void ExecutePendingAction()
    {
        if (pendingAction != null)
        {
            Debug.Log("[SurveyPanel] 원래 진행 액션 실행");
            pendingAction.Invoke();
            pendingAction = null;
        }
    }

    /// <summary>
    /// 스킵 설정 초기화 (디버그용)
    /// </summary>
    [ContextMenu("Reset Skip Preference")]
    public void ResetSkipPreference()
    {
        PlayerPrefs.DeleteKey(playerPrefsKey);
        PlayerPrefs.Save();
        Debug.Log("[SurveyPanel] 스킵 설정 초기화됨");
    }

    /// <summary>
    /// 스킵 여부 확인
    /// </summary>
    public bool IsSkipped()
    {
        return PlayerPrefs.GetInt(playerPrefsKey, 0) == 1;
    }

    /// <summary>
    /// 설문 대기 중인지 확인
    /// </summary>
    public bool IsWaitingForAction()
    {
        return isWaitingForAction;
    }
}
