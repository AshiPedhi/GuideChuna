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
    // 2026-09-15: 팝업은 on/off만 한다(사용자 지시). 아래 AnimateShow/AnimateHide 는 안 부른다 —
    //   지우지 않는 것은 프로젝트 방침이다. 다시 쓰려면 이 필드에 StartCoroutine 결과를 담으면 된다.
#pragma warning disable CS0649   // 지금은 아무도 대입하지 않는다(애니메이션 미사용)
    private Coroutine animationCoroutine;
#pragma warning restore CS0649

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

            // ★★<b>2026-09-08 — 열자마자 저절로 눌리던 것을 고친다.</b>
            //   증상: "메인으로 팝업에서 취소를 눌렀을 때 창이 안 닫혀."
            //   ★Editor.log 실측: `[InfoPanel] 메인으로 토글: ON` <b>바로 다음 줄</b>에
            //     `[ExitPopup] 다시하기 토글 선택됨 → 다시하기 실행`이 5번 중 4번 찍혔다.
            //     사람이 누르기 전에 <b>팝업이 스스로</b> 동작을 실행하고 있었다.
            //   ★원인 = <c>isOn = true</c> 대입이 <c>onValueChanged</c>를 <b>발화</b>시키고,
            //     <c>autoExecuteOnToggle</c>이 켜져 있어 그 자리에서 실행까지 갔다.
            //     그리고 취소가 이미 켜진 상태라, 진짜로 취소를 누르면 true→<b>false</b>가 되어
            //     <c>if (!isOn) return;</c>에 걸려 <b>아무 일도 안 일어났다.</b> 그게 "안 닫힌다"였다.
            //   ★씬에 <c>toggleGroup</c>이 <b>비어 있어</b> 배타 선택도 안 된다 — 기본 선택을 둘 이유가 없다.
            // → <b>전부 꺼 두고, 알림 없이</b> 초기화한다. 이제 사람이 누른 것만 발화한다.
            SetToggleOffSilently(cancelToggle);
            SetToggleOffSilently(retryToggle);
            SetToggleOffSilently(mainMenuToggle);
            SetToggleOffSilently(closeToggle);

            // 애니메이션
            if (animationCoroutine != null)
            {
                StopCoroutine(animationCoroutine);
            }
            // 2026-09-15 사용자 지시: "쓸데없이 애니메이션 넣지 말고 기존처럼 그냥 on/off만 해."
            //   축소/확대 애니메이션은 안 쓴다. 스케일만 1로 되돌려 둔다 —
            //   예전에 0으로 남아 "다시 키니까 안 돌아오는" 사고가 났던 자리다.
            popupPanel.transform.localScale = Vector3.one;
        }

        // 게임 일시정지
        if (pauseGameOnShow)
        {
            Time.timeScale = 0f;
        }
    }

    /// <summary>
    /// 토글을 <b>이벤트 없이</b> 끈다 (2026-09-08).
    /// ★<c>isOn = false</c>는 <c>onValueChanged</c>를 발화시킨다 — 여기서 그러면
    ///   팝업을 여닫을 때마다 동작이 저절로 실행된다. 그게 이번 버그의 형태였다.
    /// </summary>
    private static void SetToggleOffSilently(Toggle t)
    {
        if (t != null) t.SetIsOnWithoutNotify(false);
    }

    /// <summary>
    /// 팝업을 숨깁니다
    /// </summary>
    public void HidePopup()
    {
        // 2026-09-15 — 취소가 안 먹던 자리다.
        //   팝업을 여는 경로가 둘이다: 이 클래스의 ShowPopup() 과,
        //   InfoPanelController 가 exitConfirmPopup.SetActive(true) 로 직접 켜는 것.
        //   뒤쪽으로 열리면 isShowing 이 false 로 남아, 취소를 눌러도 여기서 그냥 빠져나갔다
        //   — 로그에는 "취소 실행"이 찍히는데 창은 그대로이고, timeScale 복구와 닫기 애니메이션도 안 돌았다.
        //   -> 실제로 켜져 있으면 닫는다. 상태 플래그가 아니라 화면의 사실을 본다.
        bool panelOpen = popupPanel != null && popupPanel.activeSelf;
        if (!isShowing && !panelOpen) return;

        // 우리가 띄운 게 아니면 애니메이션 없이 그냥 닫는다.
        //   InfoPanelController 가 직접 켠 경우인데, 그쪽은 AnimateShow 를 안 타서
        //   축소 애니메이션만 걸면 <b>스케일이 0인 채로 남아 다음에 열 때 안 보인다.</b>
        //   (2026-09-15: 애니메이션을 살렸더니 "왜 줄어들고 사라지냐"는 지적을 받은 자리다 —
        //    종전 동작은 즉시 닫힘이었다. 그 동작을 지킨다.)
        if (!isShowing)
        {
            isShowing = false;
            IsVisible = false;
            if (animationCoroutine != null) StopCoroutine(animationCoroutine);
            if (popupPanel != null)
            {
                popupPanel.transform.localScale = Vector3.one;   // 다음에 열 때를 위해 되돌린다
                popupPanel.SetActive(false);
            }
            if (pauseGameOnShow) Time.timeScale = 1f;
            return;
        }

        isShowing = false;
        IsVisible = false;

        // 팝업 숨김 (InfoPanelController가 내부적으로 팝업 상태 관리)
        // 애니메이션
        if (animationCoroutine != null)
        {
            StopCoroutine(animationCoroutine);
        }
        // on/off만 한다(2026-09-15). 애니메이션 없음.
        if (popupPanel != null)
        {
            popupPanel.transform.localScale = Vector3.one;
            popupPanel.SetActive(false);
        }

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

        // 모멘터리로 되돌린다(2026-09-15). 이 토글들은 켜진 채로 남는다 —
        //   팝업을 InfoPanelController 가 SetActive 로 직접 열어서 ShowPopup() 의
        //   초기화(SetToggleOffSilently)가 돌지 않기 때문이다. 그래서 다음 클릭이 ON->OFF 가 되어
        //   if (!isOn) return 에 걸리고, 두 번 눌러야 동작하는 상태가 됐다.
        SetToggleOffSilently(cancelToggle);

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

        // 모멘터리로 되돌린다(2026-09-15). 이 토글들은 켜진 채로 남는다 —
        //   팝업을 InfoPanelController 가 SetActive 로 직접 열어서 ShowPopup() 의
        //   초기화(SetToggleOffSilently)가 돌지 않기 때문이다. 그래서 다음 클릭이 ON->OFF 가 되어
        //   if (!isOn) return 에 걸리고, 두 번 눌러야 동작하는 상태가 됐다.
        SetToggleOffSilently(retryToggle);

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

        // 모멘터리로 되돌린다(2026-09-15). 이 토글들은 켜진 채로 남는다 —
        //   팝업을 InfoPanelController 가 SetActive 로 직접 열어서 ShowPopup() 의
        //   초기화(SetToggleOffSilently)가 돌지 않기 때문이다. 그래서 다음 클릭이 ON->OFF 가 되어
        //   if (!isOn) return 에 걸리고, 두 번 눌러야 동작하는 상태가 됐다.
        SetToggleOffSilently(mainMenuToggle);

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

        // 팝업을 연 주인에게도 알린다(2026-09-15).
        //   이 팝업은 InfoPanelController 의 실습종료 토글이 켜져 있는 동안 열려 있는 구조다.
        //   패널만 끄면 토글이 ON 으로 남아 다시 열려면 두 번 눌러야 한다.
        var panel = FindFirstObjectByType<InfoPanelController>();
        if (panel != null) panel.NotifyExitPopupClosedExternally();
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
        popupPanel.transform.localScale = Vector3.one;   // 0으로 남으면 다음에 열 때 안 보인다
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