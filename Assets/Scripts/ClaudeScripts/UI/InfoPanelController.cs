using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 정보 패널 통합 컨트롤러
///
/// [페이지 구성]
/// - 실습 정보 페이지: 시나리오 진입 시 최초 표시되는 이미지 페이지
/// - 근골격 페이지: RenderTexture RawImage (근골격 표시)
/// - 전문가 영상 페이지: 영상 재생 (토글 on일 때만 재생)
/// - 수행결과 페이지: 현재까지의 실습 결과 표시
///
/// [토글 그룹]
/// - 콘텐츠 그룹: 근골격, 전문가 영상, 수행결과 (상호 배타적)
/// - 메뉴 그룹: 설정, 메인으로 (상호 배타적)
/// </summary>
public class InfoPanelController : MonoBehaviour
{
    #region 페이지 오브젝트
    [Header("═══ 페이지 오브젝트 ═══")]
    [Tooltip("실습 정보 이미지 페이지 (시나리오 진입 시 최초 표시)")]
    [SerializeField] private GameObject practiceInfoPage;

    [Tooltip("근골격 RenderTexture 페이지")]
    [SerializeField] private GameObject skeletonPage;

    [Tooltip("전문가 영상 페이지")]
    [SerializeField] private GameObject expertVideoPage;

    [Tooltip("수행결과 페이지")]
    [SerializeField] private GameObject resultPage;
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

    [Tooltip("결과 요약 UI (없으면 자동 검색)")]
    [SerializeField] private ChunaResultSummaryUI resultSummaryUI;

    [Tooltip("동적 결과 테이블 UI (없으면 자동 검색)")]
    [SerializeField] private DynamicResultTableUI dynamicResultTableUI;

    [Tooltip("ModeSelectionManager (없으면 자동 검색)")]
    [SerializeField] private ModeSelectionManagerV2 modeSelectionManager;

    [Tooltip("설정 컨트롤러 (환자 위치 조정 자동 off용)")]
    [SerializeField] private PracticeSettingsController practiceSettingsController;
    #endregion

    #region 토글 컬러 설정
    [Header("═══ 토글 컬러 설정 ═══")]
    [SerializeField] private bool useCustomColors = true;
    [SerializeField] private Color activeColor = new Color(0.2f, 0.8f, 1f, 1f);      // 활성화 색상 (하늘색)
    [SerializeField] private Color inactiveColor = new Color(0.5f, 0.5f, 0.5f, 1f);  // 비활성화 색상 (회색)
    #endregion

    #region 토글 아이콘
    [Header("═══ 토글 아이콘 (선택) ═══")]
    [SerializeField] private Image skeletonIcon;
    [SerializeField] private Image expertVideoIcon;
    [SerializeField] private Image resultIcon;
    [SerializeField] private Image settingsIcon;
    [SerializeField] private Image mainMenuIcon;
    #endregion

    // 상태 추적
    private bool isScenarioStarted = false;
    private ContentPage currentContentPage = ContentPage.PracticeInfo;
    private bool isSettingsOpen = false;
    private bool isExitPopupOpen = false;

    // 콘텐츠 페이지 열거형
    public enum ContentPage
    {
        None,
        PracticeInfo,
        Skeleton,
        ExpertVideo,
        Result
    }

    #region Unity Lifecycle
    void Awake()
    {
        // 컨트롤러 자동 검색
        if (guideVideoController == null)
            guideVideoController = FindObjectOfType<GuideVideoController>();

        if (resultSummaryUI == null)
            resultSummaryUI = FindObjectOfType<ChunaResultSummaryUI>();

        if (dynamicResultTableUI == null)
            dynamicResultTableUI = FindObjectOfType<DynamicResultTableUI>();

        if (modeSelectionManager == null)
            modeSelectionManager = FindObjectOfType<ModeSelectionManagerV2>();

        if (practiceSettingsController == null)
            practiceSettingsController = FindObjectOfType<PracticeSettingsController>();
    }

    void Start()
    {
        InitializePanel();
        SetupToggleListeners();
        UpdateAllToggleColors();
    }

    void OnEnable()
    {
        // 시나리오 이벤트 구독
        try
        {
            ScenarioEventSystem.Instance.OnScenarioStarted += OnScenarioStarted;
            ScenarioEventSystem.Instance.OnScenarioCompleted += OnScenarioCompleted;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[InfoPanel] 이벤트 구독 실패: {e.Message}");
        }
    }

    void OnDisable()
    {
        // 시나리오 이벤트 해제
        try
        {
            ScenarioEventSystem.Instance.OnScenarioStarted -= OnScenarioStarted;
            ScenarioEventSystem.Instance.OnScenarioCompleted -= OnScenarioCompleted;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[InfoPanel] 이벤트 해제 실패: {e.Message}");
        }
    }

    void OnDestroy()
    {
        // 리스너 정리
        RemoveAllListeners();
    }
    #endregion

    #region 초기화
    /// <summary>
    /// 패널 초기화
    /// </summary>
    private void InitializePanel()
    {
        // 초기 상태: 실습 정보 페이지만 표시
        ShowContentPage(ContentPage.PracticeInfo);

        // 팝업 숨김
        if (settingsPopup != null)
            settingsPopup.SetActive(false);

        if (exitConfirmPopup != null)
            exitConfirmPopup.SetActive(false);

        // 콘텐츠 토글 초기화 (모두 off)
        SetToggleWithoutNotify(skeletonToggle, false);
        SetToggleWithoutNotify(expertVideoToggle, false);
        SetToggleWithoutNotify(resultToggle, false);

        // 메뉴 토글 초기화 (모두 off)
        SetToggleWithoutNotify(settingsToggle, false);
        SetToggleWithoutNotify(mainMenuToggle, false);

        isSettingsOpen = false;
        isExitPopupOpen = false;

        Debug.Log("[InfoPanel] 패널 초기화 완료");
    }

    /// <summary>
    /// 토글 리스너 설정
    /// </summary>
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

    /// <summary>
    /// 모든 리스너 제거
    /// </summary>
    private void RemoveAllListeners()
    {
        if (skeletonToggle != null) skeletonToggle.onValueChanged.RemoveAllListeners();
        if (expertVideoToggle != null) expertVideoToggle.onValueChanged.RemoveAllListeners();
        if (resultToggle != null) resultToggle.onValueChanged.RemoveAllListeners();
        if (settingsToggle != null) settingsToggle.onValueChanged.RemoveAllListeners();
        if (mainMenuToggle != null) mainMenuToggle.onValueChanged.RemoveAllListeners();
    }
    #endregion

    #region 콘텐츠 토글 핸들러
    /// <summary>
    /// 근골격 토글 변경
    /// </summary>
    private void OnSkeletonToggleChanged(bool isOn)
    {
        Debug.Log($"[InfoPanel] 근골격 토글: {(isOn ? "ON" : "OFF")}");

        if (isOn)
        {
            // 다른 콘텐츠 토글 끄기
            SetToggleWithoutNotify(expertVideoToggle, false);
            SetToggleWithoutNotify(resultToggle, false);

            // 전문가 영상 정지
            StopExpertVideo();

            // 근골격 페이지 표시
            ShowContentPage(ContentPage.Skeleton);
        }
        else
        {
            // 토글이 꺼지면 실습 정보 페이지로 복귀 (시나리오 시작 전) 또는 유지
            if (!isScenarioStarted)
            {
                ShowContentPage(ContentPage.PracticeInfo);
            }
        }

        UpdateAllToggleColors();
    }

    /// <summary>
    /// 전문가 영상 토글 변경
    /// </summary>
    private void OnExpertVideoToggleChanged(bool isOn)
    {
        Debug.Log($"[InfoPanel] 전문가 영상 토글: {(isOn ? "ON" : "OFF")}");

        if (isOn)
        {
            // 다른 콘텐츠 토글 끄기
            SetToggleWithoutNotify(skeletonToggle, false);
            SetToggleWithoutNotify(resultToggle, false);

            // 전문가 영상 페이지 표시 및 재생 시작
            ShowContentPage(ContentPage.ExpertVideo);
            PlayExpertVideo();
        }
        else
        {
            // 토글이 꺼지면 영상 정지
            StopExpertVideo();

            // 시나리오 시작 전이면 실습 정보 페이지로
            if (!isScenarioStarted)
            {
                ShowContentPage(ContentPage.PracticeInfo);
            }
            else if (currentContentPage == ContentPage.ExpertVideo)
            {
                // 시나리오 중이면 근골격 페이지로 전환
                SetToggleWithoutNotify(skeletonToggle, true);
                ShowContentPage(ContentPage.Skeleton);
            }
        }

        UpdateAllToggleColors();
    }

    /// <summary>
    /// 수행결과 토글 변경
    /// </summary>
    private void OnResultToggleChanged(bool isOn)
    {
        Debug.Log($"[InfoPanel] 수행결과 토글: {(isOn ? "ON" : "OFF")}");

        if (isOn)
        {
            // 다른 콘텐츠 토글 끄기
            SetToggleWithoutNotify(skeletonToggle, false);
            SetToggleWithoutNotify(expertVideoToggle, false);

            // 전문가 영상 정지
            StopExpertVideo();

            // 수행결과 페이지 표시
            ShowContentPage(ContentPage.Result);

            // 결과 UI 업데이트
            RefreshResultUI();
        }
        else
        {
            // 토글이 꺼지면 이전 페이지로 복귀
            if (!isScenarioStarted)
            {
                ShowContentPage(ContentPage.PracticeInfo);
            }
        }

        UpdateAllToggleColors();
    }
    #endregion

    #region 메뉴 토글 핸들러
    /// <summary>
    /// 설정 토글 변경
    /// </summary>
    private void OnSettingsToggleChanged(bool isOn)
    {
        Debug.Log($"[InfoPanel] 설정 토글: {(isOn ? "ON" : "OFF")}");

        if (isOn)
        {
            // 다른 메뉴 토글 끄기
            SetToggleWithoutNotify(mainMenuToggle, false);

            // Exit 팝업이 열려있으면 닫기
            if (isExitPopupOpen && exitConfirmPopup != null)
            {
                exitConfirmPopup.SetActive(false);
                isExitPopupOpen = false;
            }

            // 설정 팝업 열기
            if (settingsPopup != null)
            {
                settingsPopup.SetActive(true);
                isSettingsOpen = true;

                // 패널 숨김 알림
                if (modeSelectionManager != null)
                    modeSelectionManager.OnPopupOpened();
            }
        }
        else
        {
            // 설정 팝업 닫기
            if (settingsPopup != null)
            {
                settingsPopup.SetActive(false);
                isSettingsOpen = false;

                // 환자 위치 조정 자동 off
                if (practiceSettingsController != null)
                    practiceSettingsController.ForceDisablePatientPositionController();

                // 패널 복원 알림
                if (modeSelectionManager != null)
                    modeSelectionManager.OnPopupClosed();
            }
        }

        UpdateAllToggleColors();
    }

    /// <summary>
    /// 메인으로 토글 변경
    /// </summary>
    private void OnMainMenuToggleChanged(bool isOn)
    {
        Debug.Log($"[InfoPanel] 메인으로 토글: {(isOn ? "ON" : "OFF")}");

        if (isOn)
        {
            // 다른 메뉴 토글 끄기
            SetToggleWithoutNotify(settingsToggle, false);

            // 설정 팝업이 열려있으면 닫기
            if (isSettingsOpen && settingsPopup != null)
            {
                settingsPopup.SetActive(false);
                isSettingsOpen = false;
            }

            // Exit 확인 팝업 열기
            if (exitConfirmPopup != null)
            {
                exitConfirmPopup.SetActive(true);
                isExitPopupOpen = true;

                // 패널 숨김 알림
                if (modeSelectionManager != null)
                    modeSelectionManager.OnPopupOpened();
            }
            else
            {
                // 팝업 없으면 바로 로비로 이동
                if (modeSelectionManager != null)
                    modeSelectionManager.OnExitConfirm();
            }
        }
        else
        {
            // Exit 팝업 닫기
            if (exitConfirmPopup != null)
            {
                exitConfirmPopup.SetActive(false);
                isExitPopupOpen = false;

                // 패널 복원 알림
                if (modeSelectionManager != null)
                    modeSelectionManager.OnPopupClosed();
            }
        }

        UpdateAllToggleColors();
    }
    #endregion

    #region 페이지 전환
    /// <summary>
    /// 콘텐츠 페이지 표시
    /// </summary>
    private void ShowContentPage(ContentPage page)
    {
        currentContentPage = page;

        // 모든 페이지 숨김
        if (practiceInfoPage != null) practiceInfoPage.SetActive(false);
        if (skeletonPage != null) skeletonPage.SetActive(false);
        if (expertVideoPage != null) expertVideoPage.SetActive(false);
        if (resultPage != null) resultPage.SetActive(false);

        // 선택된 페이지만 표시
        switch (page)
        {
            case ContentPage.PracticeInfo:
                if (practiceInfoPage != null) practiceInfoPage.SetActive(true);
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

        Debug.Log($"[InfoPanel] 페이지 전환: {page}");
    }
    #endregion

    #region 전문가 영상 제어
    /// <summary>
    /// 전문가 영상 재생
    /// </summary>
    private void PlayExpertVideo()
    {
        if (guideVideoController != null)
        {
            guideVideoController.SetGuideEnabled(true);
            Debug.Log("[InfoPanel] 전문가 영상 재생 시작");
        }
    }

    /// <summary>
    /// 전문가 영상 정지
    /// </summary>
    private void StopExpertVideo()
    {
        if (guideVideoController != null)
        {
            guideVideoController.SetGuideEnabled(false);
            Debug.Log("[InfoPanel] 전문가 영상 정지");
        }
    }
    #endregion

    #region 결과 UI
    /// <summary>
    /// 결과 UI 갱신
    /// </summary>
    private void RefreshResultUI()
    {
        // DynamicResultTableUI 우선 사용
        if (dynamicResultTableUI != null)
        {
            dynamicResultTableUI.ForceRefresh();
            Debug.Log("[InfoPanel] 동적 결과 테이블 갱신");
        }
        // 기존 ChunaResultSummaryUI 폴백
        else if (resultSummaryUI != null)
        {
            resultSummaryUI.Show();
            Debug.Log("[InfoPanel] 결과 요약 UI 갱신");
        }
    }
    #endregion

    #region 시나리오 이벤트
    /// <summary>
    /// 시나리오 시작 시
    /// </summary>
    private void OnScenarioStarted(ScenarioData scenario)
    {
        isScenarioStarted = true;

        // 시나리오 시작 시 근골격 페이지로 자동 전환
        SetToggleWithoutNotify(skeletonToggle, true);
        SetToggleWithoutNotify(expertVideoToggle, false);
        SetToggleWithoutNotify(resultToggle, false);

        ShowContentPage(ContentPage.Skeleton);
        UpdateAllToggleColors();

        Debug.Log("[InfoPanel] 시나리오 시작 - 근골격 페이지로 전환");
    }

    /// <summary>
    /// 시나리오 완료 시
    /// </summary>
    private void OnScenarioCompleted(ScenarioData scenario)
    {
        isScenarioStarted = false;

        // 시나리오 완료 시 결과 페이지로 자동 전환
        SetToggleWithoutNotify(skeletonToggle, false);
        SetToggleWithoutNotify(expertVideoToggle, false);
        SetToggleWithoutNotify(resultToggle, true);

        ShowContentPage(ContentPage.Result);
        RefreshResultUI();
        UpdateAllToggleColors();

        Debug.Log("[InfoPanel] 시나리오 완료 - 결과 페이지로 전환");
    }
    #endregion

    #region 토글 컬러
    /// <summary>
    /// 모든 토글 색상 업데이트
    /// </summary>
    private void UpdateAllToggleColors()
    {
        if (!useCustomColors) return;

        // 콘텐츠 토글
        UpdateToggleColor(skeletonToggle, skeletonIcon, IsToggleOn(skeletonToggle));
        UpdateToggleColor(expertVideoToggle, expertVideoIcon, IsToggleOn(expertVideoToggle));
        UpdateToggleColor(resultToggle, resultIcon, IsToggleOn(resultToggle));

        // 메뉴 토글
        UpdateToggleColor(settingsToggle, settingsIcon, isSettingsOpen);
        UpdateToggleColor(mainMenuToggle, mainMenuIcon, isExitPopupOpen);
    }

    /// <summary>
    /// 개별 토글 색상 업데이트
    /// </summary>
    private void UpdateToggleColor(Toggle toggle, Image icon, bool isActive)
    {
        if (toggle == null) return;

        ColorBlock colors = toggle.colors;

        if (isActive)
        {
            colors.normalColor = activeColor;
            colors.highlightedColor = activeColor * 1.2f;
            colors.pressedColor = activeColor * 0.8f;
            colors.selectedColor = activeColor;
        }
        else
        {
            colors.normalColor = inactiveColor;
            colors.highlightedColor = inactiveColor * 1.2f;
            colors.pressedColor = inactiveColor * 0.8f;
            colors.selectedColor = inactiveColor;
        }

        toggle.colors = colors;

        // 아이콘 색상
        if (icon != null)
        {
            icon.color = isActive ? activeColor : inactiveColor;
        }
    }

    /// <summary>
    /// 활성화 색상 설정
    /// </summary>
    public void SetActiveColor(Color color)
    {
        activeColor = color;
        UpdateAllToggleColors();
    }

    /// <summary>
    /// 비활성화 색상 설정
    /// </summary>
    public void SetInactiveColor(Color color)
    {
        inactiveColor = color;
        UpdateAllToggleColors();
    }
    #endregion

    #region Public API
    /// <summary>
    /// 모든 팝업 닫기
    /// </summary>
    public void CloseAllPopups()
    {
        // 설정 팝업 닫기
        if (settingsPopup != null)
        {
            settingsPopup.SetActive(false);
            isSettingsOpen = false;
        }

        // Exit 팝업 닫기
        if (exitConfirmPopup != null)
        {
            exitConfirmPopup.SetActive(false);
            isExitPopupOpen = false;
        }

        // 토글 상태 초기화
        SetToggleWithoutNotify(settingsToggle, false);
        SetToggleWithoutNotify(mainMenuToggle, false);

        UpdateAllToggleColors();
    }

    /// <summary>
    /// 설정 팝업 닫기 (외부 호출용)
    /// </summary>
    public void CloseSettingsPopup()
    {
        SetToggleWithoutNotify(settingsToggle, false);
        OnSettingsToggleChanged(false);
    }

    /// <summary>
    /// Exit 팝업 닫기 (외부 호출용)
    /// </summary>
    public void CloseExitPopup()
    {
        SetToggleWithoutNotify(mainMenuToggle, false);
        OnMainMenuToggleChanged(false);
    }

    /// <summary>
    /// 근골격 페이지 표시
    /// </summary>
    public void ShowSkeletonPage()
    {
        SetToggleWithoutNotify(skeletonToggle, true);
        OnSkeletonToggleChanged(true);
    }

    /// <summary>
    /// 전문가 영상 페이지 표시
    /// </summary>
    public void ShowExpertVideoPage()
    {
        SetToggleWithoutNotify(expertVideoToggle, true);
        OnExpertVideoToggleChanged(true);
    }

    /// <summary>
    /// 수행결과 페이지 표시
    /// </summary>
    public void ShowResultPage()
    {
        SetToggleWithoutNotify(resultToggle, true);
        OnResultToggleChanged(true);
    }

    /// <summary>
    /// 실습 정보 페이지 표시 (초기 상태)
    /// </summary>
    public void ShowPracticeInfoPage()
    {
        // 모든 콘텐츠 토글 끄기
        SetToggleWithoutNotify(skeletonToggle, false);
        SetToggleWithoutNotify(expertVideoToggle, false);
        SetToggleWithoutNotify(resultToggle, false);

        // 전문가 영상 정지
        StopExpertVideo();

        ShowContentPage(ContentPage.PracticeInfo);
        UpdateAllToggleColors();
    }

    /// <summary>
    /// 현재 콘텐츠 페이지 확인
    /// </summary>
    public ContentPage CurrentPage => currentContentPage;

    /// <summary>
    /// 설정 팝업 열림 상태
    /// </summary>
    public bool IsSettingsOpen => isSettingsOpen;

    /// <summary>
    /// Exit 팝업 열림 상태
    /// </summary>
    public bool IsExitPopupOpen => isExitPopupOpen;

    /// <summary>
    /// 시나리오 시작 여부
    /// </summary>
    public bool IsScenarioStarted => isScenarioStarted;
    #endregion

    #region 유틸리티
    /// <summary>
    /// 토글 값 설정 (이벤트 발생 없이)
    /// </summary>
    private void SetToggleWithoutNotify(Toggle toggle, bool value)
    {
        if (toggle != null)
        {
            toggle.SetIsOnWithoutNotify(value);
        }
    }

    /// <summary>
    /// 토글이 켜져있는지 확인
    /// </summary>
    private bool IsToggleOn(Toggle toggle)
    {
        return toggle != null && toggle.isOn;
    }
    #endregion
}
