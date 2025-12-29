using UnityEngine;

/// <summary>
/// [DEPRECATED] 모드 선택 매니저 V2
///
/// 이 클래스는 더 이상 사용되지 않습니다.
/// 모든 기능이 InfoPanelController로 통합되었습니다.
///
/// 기존 씬 호환성을 위해 유지되며, Start()에서 InfoPanelController로 위임합니다.
///
/// 마이그레이션:
/// - 모드/난이도 선택 UI → InfoPanelController.modeSelectionPage
/// - 패널 전환 → InfoPanelController.ShowContentPage()
/// - 시나리오 시작 → InfoPanelController.StartSimulation()
/// </summary>
[System.Obsolete("ModeSelectionManagerV2는 더 이상 사용되지 않습니다. InfoPanelController를 사용하세요.")]
public class ModeSelectionManagerV2 : MonoBehaviour
{
    [Header("═══ InfoPanelController 위임 ═══")]
    [SerializeField] private InfoPanelController infoPanelController;

    // 기존 Inspector 호환성 (할당은 무시됨)
    [Header("═══ [DEPRECATED] 이 필드들은 무시됩니다 ═══")]
    [SerializeField] private GameObject modeSelectionPanel;
    [SerializeField] private GameObject guidePanel;

    void Awake()
    {
        // InfoPanelController 자동 검색
        if (infoPanelController == null)
        {
            infoPanelController = FindObjectOfType<InfoPanelController>();
        }

        if (infoPanelController == null)
        {
            Debug.LogError("[ModeSelectionManagerV2] InfoPanelController를 찾을 수 없습니다! InfoPanelController가 씬에 있는지 확인하세요.");
        }
        else
        {
            Debug.Log("<color=yellow>[ModeSelectionManagerV2] ⚠ DEPRECATED: 이 컴포넌트는 더 이상 필요하지 않습니다. InfoPanelController로 대체되었습니다.</color>");
        }
    }

    // ═══════════════ 호환성을 위한 Public API (위임) ═══════════════

    public enum ModeType
    {
        None,
        Practice,
        Evaluation
    }

    public enum DifficultyType
    {
        Beginner,
        Intermediate,
        Advanced
    }

    /// <summary>
    /// 선택된 모드 반환 (InfoPanelController에서 가져옴)
    /// </summary>
    public ModeType GetSelectedMode()
    {
        if (infoPanelController == null) return ModeType.None;

        switch (infoPanelController.SelectedMode)
        {
            case InfoPanelController.ModeType.Practice:
                return ModeType.Practice;
            case InfoPanelController.ModeType.Evaluation:
                return ModeType.Evaluation;
            default:
                return ModeType.None;
        }
    }

    /// <summary>
    /// 선택된 난이도 반환 (InfoPanelController에서 가져옴)
    /// </summary>
    public DifficultyType GetSelectedDifficulty()
    {
        if (infoPanelController == null) return DifficultyType.Intermediate;

        switch (infoPanelController.SelectedDifficulty)
        {
            case InfoPanelController.DifficultyType.Beginner:
                return DifficultyType.Beginner;
            case InfoPanelController.DifficultyType.Intermediate:
                return DifficultyType.Intermediate;
            case InfoPanelController.DifficultyType.Advanced:
                return DifficultyType.Advanced;
            default:
                return DifficultyType.Intermediate;
        }
    }

    /// <summary>
    /// 선택 초기화 (InfoPanelController에 위임)
    /// </summary>
    public void ResetSelection()
    {
        if (infoPanelController != null)
        {
            infoPanelController.ResetModeSelection();
        }
    }

    /// <summary>
    /// 시뮬레이션 시작 (InfoPanelController에 위임)
    /// </summary>
    public void StartSimulation()
    {
        if (infoPanelController != null)
        {
            infoPanelController.StartSimulation();
        }
    }

    /// <summary>
    /// 팝업 열림 처리 (InfoPanelController에 위임)
    /// </summary>
    public void OnPopupOpened()
    {
        // InfoPanelController에서 자체적으로 처리
        Debug.Log("[ModeSelectionManagerV2] OnPopupOpened - InfoPanelController에서 처리됨");
    }

    /// <summary>
    /// 팝업 닫힘 처리 (InfoPanelController에 위임)
    /// </summary>
    public void OnPopupClosed()
    {
        // InfoPanelController에서 자체적으로 처리
        Debug.Log("[ModeSelectionManagerV2] OnPopupClosed - InfoPanelController에서 처리됨");
    }

    /// <summary>
    /// Exit 확인 (InfoPanelController에 위임)
    /// </summary>
    public void OnExitConfirm()
    {
        if (infoPanelController != null)
        {
            infoPanelController.OnExitConfirm();
        }
    }

    /// <summary>
    /// Exit 취소 (InfoPanelController에 위임)
    /// </summary>
    public void OnExitCancel()
    {
        if (infoPanelController != null)
        {
            infoPanelController.OnExitCancel();
        }
    }
}
