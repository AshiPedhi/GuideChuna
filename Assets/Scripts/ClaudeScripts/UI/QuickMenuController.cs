using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 하단 Quick 메뉴 전용 컨트롤러 (수정 버전)
/// - 메인 패널은 항상 활성화
/// - 설정과 로비(Exit) 팝업만 토글
/// </summary>
public class QuickMenuController : MonoBehaviour
{
    [Header("═══ 퀵메뉴 토글 버튼 ═══")]
    [SerializeField] private Toggle settingsToggle;  // 설정 토글
    [SerializeField] private Toggle lobbyToggle;     // 로비 토글

    [Header("═══ 토글 아이콘 ═══")]
    [SerializeField] private Image settingsIcon;
    [SerializeField] private Image lobbyIcon;

    [Header("═══ 팝업 연결 ═══")]
    [SerializeField] private GameObject mainPanelPopup;   // 메인 패널 (항상 활성화)
    [SerializeField] private GameObject settingsPopup;    // 설정 팝업
    [SerializeField] private GameObject exitConfirmPopup; // Exit 확인 팝업

    [Header("═══ 토글 컬러 설정 ═══")]
    [SerializeField] private bool useCustomColors = true;
    [SerializeField] private Color activeColor = new Color(0.2f, 0.8f, 1f, 1f);      // 활성화 색상 (하늘색)
    [SerializeField] private Color inactiveColor = new Color(0.5f, 0.5f, 0.5f, 1f);  // 비활성화 색상 (회색)
    [SerializeField] private Color disabledColor = new Color(0.3f, 0.3f, 0.3f, 0.5f); // 비활성 색상

    // 팝업 상태
    private bool isSettingsOpen = false;
    private bool isExitPopupOpen = false;

    // ModeSelectionManagerV2 참조
    private ModeSelectionManagerV2 modeSelectionManager;

    void Awake()
    {
        modeSelectionManager = FindObjectOfType<ModeSelectionManagerV2>();
    }

    void Start()
    {
        InitializeQuickMenu();
        SetupToggleListeners();
        UpdateToggleColors();
    }

    void InitializeQuickMenu()
    {
        // 메인 패널은 항상 활성화
        if (mainPanelPopup != null)
        {
            mainPanelPopup.SetActive(true);
            Debug.Log("메인 패널 상시 활성화");
        }

        // 다른 팝업들은 초기에 비활성화
        if (settingsPopup != null)
        {
            settingsPopup.SetActive(false);
            isSettingsOpen = false;
        }

        if (exitConfirmPopup != null)
        {
            exitConfirmPopup.SetActive(false);
            isExitPopupOpen = false;
        }

        // 토글 초기화
        if (settingsToggle != null)
            settingsToggle.isOn = false;

        if (lobbyToggle != null)
            lobbyToggle.isOn = false;
    }

    void SetupToggleListeners()
    {
        // 설정 토글
        if (settingsToggle != null)
        {
            settingsToggle.onValueChanged.RemoveAllListeners();
            settingsToggle.onValueChanged.AddListener((isOn) => {
                if (isOn)
                {
                    OnSettingsToggleClick();
                    StartCoroutine(ResetToggle(settingsToggle));
                }
            });
        }

        // 로비 토글
        if (lobbyToggle != null)
        {
            lobbyToggle.onValueChanged.RemoveAllListeners();
            lobbyToggle.onValueChanged.AddListener((isOn) => {
                if (isOn)
                {
                    OnLobbyToggleClick();
                    StartCoroutine(ResetToggle(lobbyToggle));
                }
            });
        }
    }

    // ═══════════════ 토글 동작 ═══════════════

    void OnSettingsToggleClick()
    {
        Debug.Log("설정 토글 클릭");

        // Exit 팝업이 열려있으면 닫기
        if (isExitPopupOpen && exitConfirmPopup != null)
        {
            exitConfirmPopup.SetActive(false);
            isExitPopupOpen = false;
        }

        // 설정 팝업 토글
        if (settingsPopup != null)
        {
            isSettingsOpen = !isSettingsOpen;
            settingsPopup.SetActive(isSettingsOpen);

            if (isSettingsOpen)
            {
                Debug.Log("설정 메뉴 열림");
            }
            else
            {
                Debug.Log("설정 메뉴 닫힘");
            }

            UpdateToggleColors();
        }
    }

    void OnLobbyToggleClick()
    {
        Debug.Log("로비 토글 클릭");

        // 설정 팝업이 열려있으면 닫기
        if (isSettingsOpen && settingsPopup != null)
        {
            settingsPopup.SetActive(false);
            isSettingsOpen = false;
        }

        // Exit 팝업 토글
        if (exitConfirmPopup != null)
        {
            isExitPopupOpen = !isExitPopupOpen;
            exitConfirmPopup.SetActive(isExitPopupOpen);

            if (isExitPopupOpen)
            {
                Debug.Log("종료 확인 팝업 열림");
            }
            else
            {
                Debug.Log("종료 확인 팝업 닫힘");
            }

            UpdateToggleColors();
        }
        else
        {
            // 팝업이 없으면 바로 로비로
            Debug.Log("종료 확인 팝업이 없어 바로 로비로 이동");
            if (modeSelectionManager != null)
            {
                modeSelectionManager.OnExitConfirm();
            }
        }
    }

    // ═══════════════ 토글 컬러 제어 ═══════════════

    public void UpdateToggleColors()
    {
        if (!useCustomColors) return;

        // 설정 토글 컬러
        UpdateToggleColor(settingsToggle, settingsIcon, isSettingsOpen);

        // 로비 토글 컬러 (Exit 팝업이 열렸을 때)
        UpdateToggleColor(lobbyToggle, lobbyIcon, isExitPopupOpen);
    }

    void UpdateToggleColor(Toggle toggle, Image icon, bool isActive)
    {
        if (toggle == null) return;

        // ColorBlock 설정
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

        colors.disabledColor = disabledColor;
        toggle.colors = colors;

        // 아이콘 색상도 업데이트
        if (icon != null)
        {
            icon.color = isActive ? activeColor : inactiveColor;
        }
    }

    // 런타임에 색상 변경
    public void SetActiveColor(Color color)
    {
        activeColor = color;
        UpdateToggleColors();
    }

    public void SetInactiveColor(Color color)
    {
        inactiveColor = color;
        UpdateToggleColors();
    }

    public void SetDisabledColor(Color color)
    {
        disabledColor = color;
        UpdateToggleColors();
    }

    // ═══════════════ 유틸리티 ═══════════════

    IEnumerator ResetToggle(Toggle toggle)
    {
        yield return null;
        if (toggle != null)
            toggle.isOn = false;
    }

    // ═══════════════ Public 메서드 ═══════════════

    public void CloseAllPopups()
    {
        // 메인 패널은 닫지 않음 (항상 활성화)

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

        UpdateToggleColors();
    }

    public void CloseSettingsPopup()
    {
        if (settingsPopup != null)
        {
            settingsPopup.SetActive(false);
            isSettingsOpen = false;
            UpdateToggleColors();
        }
    }

    public void CloseExitPopup()
    {
        if (exitConfirmPopup != null)
        {
            exitConfirmPopup.SetActive(false);
            isExitPopupOpen = false;
            UpdateToggleColors();
        }
    }

    public void OnExitPopupClosed()
    {
        isExitPopupOpen = false;
        UpdateToggleColors();
    }

    public void OnSettingsPopupClosed()
    {
        isSettingsOpen = false;
        UpdateToggleColors();
    }

    // 상태 확인 프로퍼티
    public bool IsSettingsOpen => isSettingsOpen;
    public bool IsExitPopupOpen => isExitPopupOpen;

    void OnDestroy()
    {
        // 리스너 정리
        if (settingsToggle != null) settingsToggle.onValueChanged.RemoveAllListeners();
        if (lobbyToggle != null) lobbyToggle.onValueChanged.RemoveAllListeners();
    }
}