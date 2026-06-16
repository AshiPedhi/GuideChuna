using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>
/// 종료 확인 팝업 컨트롤러 (토글 버전)
/// 실습 종료 시 확인 팝업을 표시
/// </summary>
public class ExitPopupController : BaseUIPanel
{
    [Header("=== UI References ===")]
    [SerializeField] private GameObject popupPanel;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private Toggle cancelToggle;      // 취소
    [SerializeField] private Toggle retryToggle;       // 다시하기
    [SerializeField] private Toggle mainMenuToggle;    // 메인으로
    [SerializeField] private Toggle closeToggle;       // X 토글 (옵션)
    [SerializeField] private ToggleGroup toggleGroup;  // 토글 그룹

    [Header("=== Text Settings ===")]
    [SerializeField] private string popupTitle = "실습 종료";
    [SerializeField] private string popupMessage = "실습을 마치고 메인으로 이동하시겠습니까?";
    [Tooltip("평가 진행 중 종료 시도 시 표시할 경고 제목")]
    [SerializeField] private string evaluationWarningTitle = "평가 종료 경고";
    [Tooltip("평가 진행 중 종료 시도 시 표시할 경고 메시지")]
    [SerializeField] private string evaluationWarningMessage = "지금 나가면 평가가 미완료로 저장됩니다.\n정말 나가시겠습니까?";
    [SerializeField] private string cancelToggleText = "취소";
    [SerializeField] private string retryToggleText = "다시하기";
    [SerializeField] private string mainMenuToggleText = "메인으로";

    [Header("=== Animation ===")]
    [SerializeField] private float animationDuration = 0.3f;
    [SerializeField] private AnimationCurve scaleCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("=== Settings ===")]
    [SerializeField] private bool pauseGameOnShow = true;
    [SerializeField] private string mainMenuSceneName = "MainMenu";
    [SerializeField] private bool autoExecuteOnToggle = false; // 토글 선택 시 자동 실행 (기본값: false)

    [Header("=== Action Button ===")]
    [SerializeField] private Button executeButton; // 실행 버튼 (옵션)
    [SerializeField] private string executeButtonText = "확인";

    // 이벤트
    public UnityEvent OnCancelSelected = new UnityEvent();
    public UnityEvent OnRetrySelected = new UnityEvent();
    public UnityEvent OnMainMenuSelected = new UnityEvent();

    // 상태
    private bool isShowing = false;
    private Coroutine animationCoroutine;

    // InfoPanelController 참조 (팝업 상태 알림용)
    private InfoPanelController infoPanelController;

    // 평가 진행 중 판단용 (경고 메시지 분기). 지연 탐색.
    private TrainingResultTracker resultTracker;

    protected override GameObject GetPanelObject() => popupPanel;

    private void Awake()
    {
        // InfoPanelController 찾기
        infoPanelController = FindFirstObjectByType<InfoPanelController>();

        // 토글 이벤트 연결
        if (cancelToggle != null)
        {
            cancelToggle.onValueChanged.AddListener(OnCancelToggled);
        }

        if (retryToggle != null)
        {
            retryToggle.onValueChanged.AddListener(OnRetryToggled);
        }

        if (mainMenuToggle != null)
        {
            mainMenuToggle.onValueChanged.AddListener(OnMainMenuToggled);
        }

        if (closeToggle != null)
        {
            closeToggle.onValueChanged.AddListener(OnCloseToggled);
        }

        // 실행 버튼 연결 (자동 실행이 아닐 경우 사용)
        if (executeButton != null)
        {
            executeButton.onClick.AddListener(ExecuteSelectedAction);

            var buttonText = executeButton.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
            {
                buttonText.text = executeButtonText;
            }
        }

        // 토글 그룹 설정
        if (toggleGroup != null)
        {
            if (cancelToggle != null) cancelToggle.group = toggleGroup;
            if (retryToggle != null) retryToggle.group = toggleGroup;
            if (mainMenuToggle != null) mainMenuToggle.group = toggleGroup;
            if (closeToggle != null) closeToggle.group = toggleGroup;

            // 토글 그룹 설정 - 최소 하나는 선택되도록
            toggleGroup.allowSwitchOff = false;
        }

        // 초기 상태
        if (popupPanel != null)
        {
            popupPanel.SetActive(false);
        }

        // 텍스트 설정
        UpdateTexts();
    }

    /// <summary>
    /// 팝업 표시 직전 컨텍스트에 맞는 제목/메시지 적용.
    /// 평가 진행 중(공식 평가 + 추적 중) → 미완료 저장 경고. 그 외 → 기본 메시지.
    /// </summary>
    private void ApplyContextualMessage()
    {
        if (resultTracker == null)
            resultTracker = FindFirstObjectByType<TrainingResultTracker>();

        bool evaluationInProgress = resultTracker != null
                                    && resultTracker.IsTracking
                                    && resultTracker.IsOfficialEvaluation;

        if (titleText != null)
            titleText.text = evaluationInProgress ? evaluationWarningTitle : popupTitle;
        if (messageText != null)
            messageText.text = evaluationInProgress ? evaluationWarningMessage : popupMessage;
    }

    private void UpdateTexts()
    {
        if (titleText != null)
        {
            titleText.text = popupTitle;
        }

        if (messageText != null)
        {
            messageText.text = popupMessage;
        }

        if (cancelToggle != null)
        {
            var toggleLabel = cancelToggle.GetComponentInChildren<TextMeshProUGUI>();
            if (toggleLabel != null)
            {
                toggleLabel.text = cancelToggleText;
            }
        }

        if (retryToggle != null)
        {
            var toggleLabel = retryToggle.GetComponentInChildren<TextMeshProUGUI>();
            if (toggleLabel != null)
            {
                toggleLabel.text = retryToggleText;
            }
        }

        if (mainMenuToggle != null)
        {
            var toggleLabel = mainMenuToggle.GetComponentInChildren<TextMeshProUGUI>();
            if (toggleLabel != null)
            {
                toggleLabel.text = mainMenuToggleText;
            }
        }
    }

    /// <summary>
    /// 팝업을 표시합니다
    /// </summary>
    public void ShowPopup()
    {
        if (isShowing) return;

        isShowing = true;
        IsVisible = true;

        // 평가 진행 중이면 "미완료로 저장됨" 경고 메시지로 교체 (그 외엔 기본 메시지)
        ApplyContextualMessage();

        // 팝업 표시 (InfoPanelController가 내부적으로 팝업 상태 관리)
        if (popupPanel != null)
        {
            popupPanel.SetActive(true);

            // 초기 토글 상태 설정 (취소 토글을 기본 선택)
            if (cancelToggle != null)
            {
                cancelToggle.isOn = true;
            }

            // 애니메이션
            if (animationCoroutine != null)
            {
                StopCoroutine(animationCoroutine);
            }
            animationCoroutine = StartCoroutine(AnimateShow());
        }

        // 게임 일시정지
        if (pauseGameOnShow)
        {
            Time.timeScale = 0f;
        }
    }

    /// <summary>
    /// 팝업을 숨깁니다
    /// </summary>
    public void HidePopup()
    {
        if (!isShowing) return;

        isShowing = false;
        IsVisible = false;

        // 팝업 숨김 (InfoPanelController가 내부적으로 팝업 상태 관리)
        // 애니메이션
        if (animationCoroutine != null)
        {
            StopCoroutine(animationCoroutine);
        }
        animationCoroutine = StartCoroutine(AnimateHide());

        // 게임 재개
        if (pauseGameOnShow)
        {
            Time.timeScale = 1f;
        }
    }

    /// <summary>
    /// 팝업 토글
    /// </summary>
    public void TogglePopup()
    {
        if (isShowing)
        {
            HidePopup();
        }
        else
        {
            ShowPopup();
        }
    }

    /// <summary>
    /// 취소 토글이 선택되었을 때
    /// </summary>
    private void OnCancelToggled(bool isOn)
    {
        if (!isOn) return;

        ChunaLogger.Log("[ExitPopup] 취소 토글 선택됨");

        if (autoExecuteOnToggle)
        {
            ExecuteCancel();
        }
    }

    /// <summary>
    /// 다시하기 토글이 선택되었을 때
    /// </summary>
    private void OnRetryToggled(bool isOn)
    {
        if (!isOn) return;

        ChunaLogger.Log("[ExitPopup] 다시하기 토글 선택됨");

        if (autoExecuteOnToggle)
        {
            ExecuteRetry();
        }
    }

    /// <summary>
    /// 메인으로 토글이 선택되었을 때
    /// </summary>
    private void OnMainMenuToggled(bool isOn)
    {
        if (!isOn) return;

        ChunaLogger.Log("[ExitPopup] 메인으로 토글 선택됨");

        if (autoExecuteOnToggle)
        {
            ExecuteMainMenu();
        }
    }

    /// <summary>
    /// 닫기 토글이 선택되었을 때 (X 버튼)
    /// </summary>
    private void OnCloseToggled(bool isOn)
    {
        if (!isOn) return;

        ChunaLogger.Log("[ExitPopup] 닫기 토글 선택됨");

        if (autoExecuteOnToggle)
        {
            ExecuteCancel();
        }
    }

    /// <summary>
    /// 취소를 실행합니다
    /// </summary>
    public void ExecuteCancel()
    {
        ChunaLogger.Log("[ExitPopup] 취소 실행");

        // 이벤트 발생
        OnCancelSelected?.Invoke();

        // 팝업 숨기기
        HidePopup();
    }

    /// <summary>
    /// 다시하기를 실행합니다
    /// </summary>
    public void ExecuteRetry()
    {
        ChunaLogger.Log("[ExitPopup] 다시하기 실행");

        // 이벤트 발생
        OnRetrySelected?.Invoke();

        // 팝업 숨기기
        HidePopup();

        // 현재 씬 다시 로드
        ReloadCurrentScene();
    }

    /// <summary>
    /// 메인으로 이동을 실행합니다
    /// </summary>
    public void ExecuteMainMenu()
    {
        ChunaLogger.Log("[ExitPopup] 메인으로 이동 실행");

        // 이벤트 발생
        OnMainMenuSelected?.Invoke();

        // 팝업 숨기기
        HidePopup();

        // 메인 메뉴로 이동
        LoadMainMenu();
    }

    /// <summary>
    /// 현재 선택된 토글의 액션을 실행합니다
    /// </summary>
    public void ExecuteSelectedAction()
    {
        if (cancelToggle != null && cancelToggle.isOn)
        {
            ExecuteCancel();
        }
        else if (retryToggle != null && retryToggle.isOn)
        {
            ExecuteRetry();
        }
        else if (mainMenuToggle != null && mainMenuToggle.isOn)
        {
            ExecuteMainMenu();
        }
        else if (closeToggle != null && closeToggle.isOn)
        {
            ExecuteCancel();
        }
    }

    /// <summary>
    /// 현재 씬을 다시 로드합니다
    /// </summary>
    private void ReloadCurrentScene()
    {
        Time.timeScale = 1f; // 시간 정상화

        // SceneLoader를 통해 씬 전환 (Camera X 보존)
        SceneLoader.ReloadCurrentScene(useLoadingScene: true);
    }

    /// <summary>
    /// 메인 메뉴로 이동합니다
    /// </summary>
    private void LoadMainMenu()
    {
        Time.timeScale = 1f; // 시간 정상화

        if (!string.IsNullOrEmpty(mainMenuSceneName))
        {
            // SceneLoader를 통해 씬 전환 (Camera X 보존)
            SceneLoader.LoadScene(mainMenuSceneName, useLoadingScene: true);
        }
        else
        {
            ChunaLogger.LogWarning("[ExitPopup] 메인 메뉴 씬 이름이 설정되지 않았습니다!");
        }
    }

    private IEnumerator AnimateShow()
    {
        if (popupPanel == null) yield break;
        yield return ScaleAnimation(popupPanel.transform, Vector3.zero, Vector3.one, animationDuration, scaleCurve);
    }

    private IEnumerator AnimateHide()
    {
        if (popupPanel == null) yield break;
        yield return ScaleAnimation(popupPanel.transform, Vector3.one, Vector3.zero, animationDuration, scaleCurve);
        popupPanel.SetActive(false);
    }

    /// <summary>
    /// 메시지를 설정합니다
    /// </summary>
    public void SetMessage(string title, string message)
    {
        popupTitle = title;
        popupMessage = message;
        UpdateTexts();
    }

    /// <summary>
    /// 토글 텍스트를 설정합니다
    /// </summary>
    public void SetToggleTexts(string cancel, string retry, string mainMenu)
    {
        cancelToggleText = cancel;
        retryToggleText = retry;
        mainMenuToggleText = mainMenu;
        UpdateTexts();
    }

    /// <summary>
    /// 메인 메뉴 씬 이름을 설정합니다
    /// </summary>
    public void SetMainMenuSceneName(string sceneName)
    {
        mainMenuSceneName = sceneName;
    }

    /// <summary>
    /// 자동 실행 모드를 설정합니다
    /// </summary>
    public void SetAutoExecute(bool autoExecute)
    {
        autoExecuteOnToggle = autoExecute;

        // 자동 실행 모드에 따라 실행 버튼 표시/숨김
        if (executeButton != null)
        {
            executeButton.gameObject.SetActive(!autoExecute);
        }
    }

    /// <summary>
    /// 현재 선택된 토글을 가져옵니다
    /// </summary>
    public string GetSelectedToggle()
    {
        if (cancelToggle != null && cancelToggle.isOn)
            return "cancel";
        if (retryToggle != null && retryToggle.isOn)
            return "retry";
        if (mainMenuToggle != null && mainMenuToggle.isOn)
            return "mainMenu";
        if (closeToggle != null && closeToggle.isOn)
            return "close";
        return "none";
    }

    public bool IsShowing()
    {
        return isShowing;
    }

    private void OnDestroy()
    {
        // 시간 정상화
        if (Time.timeScale == 0f)
        {
            Time.timeScale = 1f;
        }

        // 코루틴 정리
        if (animationCoroutine != null)
        {
            StopCoroutine(animationCoroutine);
        }
    }
}