using UnityEngine;

/// <summary>
/// [DEPRECATED] 하단 Quick 메뉴 컨트롤러
///
/// 이 클래스는 더 이상 사용되지 않습니다.
/// 설정/로비 버튼 기능이 InfoPanelController로 통합되었습니다.
///
/// 기존 씬 호환성을 위해 유지되며, InfoPanelController로 위임합니다.
///
/// 마이그레이션:
/// - 설정 토글 → InfoPanelController.settingsToggle
/// - 로비 토글 → InfoPanelController.mainMenuToggle
/// - 팝업 제어 → InfoPanelController.CloseAllPopups()
/// </summary>
[System.Obsolete("QuickMenuController는 더 이상 사용되지 않습니다. InfoPanelController를 사용하세요.")]
public class QuickMenuController : MonoBehaviour
{
    [Header("═══ InfoPanelController 위임 ═══")]
    [SerializeField] private InfoPanelController infoPanelController;

    // 기존 Inspector 호환성 (할당은 무시됨)
    [Header("═══ [DEPRECATED] 이 필드들은 무시됩니다 ═══")]
    [SerializeField] private GameObject mainPanelPopup;
    [SerializeField] private GameObject settingsPopup;
    [SerializeField] private GameObject exitConfirmPopup;

    void Awake()
    {
        // InfoPanelController 자동 검색
        if (infoPanelController == null)
        {
            infoPanelController = FindObjectOfType<InfoPanelController>();
        }

        if (infoPanelController == null)
        {
            Debug.LogError("[QuickMenuController] InfoPanelController를 찾을 수 없습니다! InfoPanelController가 씬에 있는지 확인하세요.");
        }
        else
        {
            Debug.Log("<color=yellow>[QuickMenuController] ⚠ DEPRECATED: 이 컴포넌트는 더 이상 필요하지 않습니다. InfoPanelController로 대체되었습니다.</color>");
        }
    }

    // ═══════════════ 호환성을 위한 Public API (위임) ═══════════════

    /// <summary>
    /// 모든 팝업 닫기 (InfoPanelController에 위임)
    /// </summary>
    public void CloseAllPopups()
    {
        if (infoPanelController != null)
        {
            infoPanelController.CloseAllPopups();
        }
    }

    /// <summary>
    /// 설정 팝업 닫기 (InfoPanelController에 위임)
    /// </summary>
    public void CloseSettingsPopup()
    {
        if (infoPanelController != null)
        {
            infoPanelController.CloseSettingsPopup();
        }
    }

    /// <summary>
    /// Exit 팝업 닫기 (InfoPanelController에 위임)
    /// </summary>
    public void CloseExitPopup()
    {
        if (infoPanelController != null)
        {
            infoPanelController.CloseExitPopup();
        }
    }

    /// <summary>
    /// Exit 팝업 닫힘 처리 (InfoPanelController에 위임)
    /// </summary>
    public void OnExitPopupClosed()
    {
        if (infoPanelController != null)
        {
            infoPanelController.CloseExitPopup();
        }
    }

    /// <summary>
    /// 설정 팝업 닫힘 처리 (InfoPanelController에 위임)
    /// </summary>
    public void OnSettingsPopupClosed()
    {
        if (infoPanelController != null)
        {
            infoPanelController.CloseSettingsPopup();
        }
    }

    /// <summary>
    /// 토글 색상 업데이트 (InfoPanelController에서 처리)
    /// </summary>
    public void UpdateToggleColors()
    {
        // InfoPanelController에서 자체적으로 처리
    }

    // 상태 확인 프로퍼티 (InfoPanelController에서 가져옴)
    public bool IsSettingsOpen => infoPanelController != null && infoPanelController.IsSettingsOpen;
    public bool IsExitPopupOpen => infoPanelController != null && infoPanelController.IsExitPopupOpen;
}
