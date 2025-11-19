using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;

public class ModeSelectionManagerV2 : MonoBehaviour
{
    [Header("═══ 상단 모드 선택 ═══")]
    [SerializeField] private Toggle practiceToggle;  // 실습 토글
    [SerializeField] private Toggle evaluationToggle; // 평가 토글
    [SerializeField] private TextMeshProUGUI practiceLabel; // "안내에 따라 과정 학습"
    [SerializeField] private TextMeshProUGUI evaluationLabel; // "학습 내용 테스트"

    [Header("═══ 레벨 선택 (난이도) ═══")]
    [SerializeField] private GameObject levelSelectionPanel; // 레벨 선택 전체 패널
    [SerializeField] private Toggle beginnerToggle;   // 초급자 토글
    [SerializeField] private Toggle intermediateToggle; // 중급자 토글
    [SerializeField] private Toggle advancedToggle;   // 상급자 토글

    [Header("═══ 난이도 설명 텍스트 ═══")]
    [SerializeField] private TextMeshProUGUI beginnerDescription; // "최초 학습자 혹은 추나 기초학습자를 위한 레벨입니다"
    [SerializeField] private TextMeshProUGUI intermediateDescription; // "실습 경험자를 위한 레벨입니다"
    [SerializeField] private TextMeshProUGUI advancedDescription; // "안내 가이드가 없이 진행 가능한 숙련자를 위한 레벨입니다"

    [Header("═══ 상부승모근 타이틀 ═══")]
    [SerializeField] private TextMeshProUGUI titleText; // "상부승모근" 텍스트

    [Header("═══ 토글 색상 커스터마이징 ═══")]
    [SerializeField] private bool useCustomToggleColors = true;
    [SerializeField] private Color selectedToggleColor = new Color(0.2f, 0.8f, 1f, 1f);  // 선택된 토글 색상
    [SerializeField] private Color normalToggleColor = new Color(0.7f, 0.7f, 0.7f, 1f);  // 일반 토글 색상
    [SerializeField] private Color disabledToggleColor = new Color(0.4f, 0.4f, 0.4f, 0.5f);  // 비활성 토글 색상

    [Header("═══ UI 패널 참조 ═══")]
    [SerializeField] private GameObject modeSelectionPanel;  // 모드 선택 UI 패널 (이 GameObject)
    [SerializeField] private GameObject guidePanel;  // 가이드 UI 패널

    [Header("═══ 외부 컨트롤러 참조 ═══")]
    [SerializeField] private QuickMenuController quickMenuController;  // 퀵메뉴 컨트롤러 (선택사항)
    [SerializeField] private ExitPopupController exitPopupController;  // Exit 팝업 컨트롤러
    [SerializeField] private ScenarioManager scenarioManager;  // 시나리오 매니저

    // 선택 상태
    private ModeType selectedMode = ModeType.None;
    private DifficultyType selectedDifficulty = DifficultyType.Intermediate; // 기본값 중급자로 변경

    public enum ModeType
    {
        None,
        Practice,    // 실습
        Evaluation   // 평가
    }

    public enum DifficultyType
    {
        Beginner,     // 초급자
        Intermediate, // 중급자 (기본값)
        Advanced      // 상급자
    }

    void Awake()
    {
        // 초기 난이도 설정 (중급자가 기본)
        selectedDifficulty = DifficultyType.Intermediate;

        // 컨트롤러 자동 찾기 (할당되지 않은 경우)
        if (quickMenuController == null)
            quickMenuController = FindObjectOfType<QuickMenuController>();

        if (exitPopupController == null)
            exitPopupController = FindObjectOfType<ExitPopupController>();

        if (scenarioManager == null)
            scenarioManager = FindObjectOfType<ScenarioManager>();
    }

    void Start()
    {
        InitializeUI();
        SetupEventListeners();
        UpdateUI();
    }

    void InitializeUI()
    {
        // 중급자 토글을 기본으로 선택
        if (intermediateToggle != null)
        {
            intermediateToggle.isOn = true;
        }

        // 레벨 선택 패널은 항상 표시
        if (levelSelectionPanel != null)
        {
            levelSelectionPanel.SetActive(true);
        }

        // 타이틀 설정
        if (titleText != null)
        {
            titleText.text = "상부승모근";
        }

        // 설명 텍스트 설정
        SetupDescriptionTexts();

        // 토글 색상 초기화
        UpdateToggleColors();
    }

    void SetupDescriptionTexts()
    {
        if (beginnerDescription != null)
            beginnerDescription.text = "최초 학습자 혹은 추나 기초학습자를 위한 레벨입니다";

        if (intermediateDescription != null)
            intermediateDescription.text = "실습 경험자를 위한 레벨입니다";

        if (advancedDescription != null)
            advancedDescription.text = "안내 가이드가 없이 진행 가능한 숙련자를 위한 레벨입니다";

        if (practiceLabel != null)
            practiceLabel.text = "안내에 따라 과정 학습";

        if (evaluationLabel != null)
            evaluationLabel.text = "학습 내용 테스트";
    }

    void SetupEventListeners()
    {
        // 모드 선택 리스너
        if (practiceToggle != null)
        {
            practiceToggle.onValueChanged.AddListener((isOn) => {
                if (isOn) OnModeSelected(ModeType.Practice);
            });
        }

        if (evaluationToggle != null)
        {
            evaluationToggle.onValueChanged.AddListener((isOn) => {
                if (isOn) OnModeSelected(ModeType.Evaluation);
            });
        }

        // 난이도 선택 리스너
        if (beginnerToggle != null)
        {
            beginnerToggle.onValueChanged.AddListener((isOn) => {
                if (isOn) OnDifficultySelected(DifficultyType.Beginner);
            });
        }

        if (intermediateToggle != null)
        {
            intermediateToggle.onValueChanged.AddListener((isOn) => {
                if (isOn) OnDifficultySelected(DifficultyType.Intermediate);
            });
        }

        if (advancedToggle != null)
        {
            advancedToggle.onValueChanged.AddListener((isOn) => {
                if (isOn) OnDifficultySelected(DifficultyType.Advanced);
            });
        }
    }

    void OnModeSelected(ModeType mode)
    {
        selectedMode = mode;
        Debug.Log($"모드 선택됨: {mode}");

        UpdateUI();
        UpdateToggleColors();
        CheckSelectionComplete();
    }

    void OnDifficultySelected(DifficultyType difficulty)
    {
        selectedDifficulty = difficulty;
        Debug.Log($"난이도 선택됨: {difficulty}");

        UpdateUI();
        UpdateToggleColors();
        CheckSelectionComplete();
    }

    void UpdateUI()
    {
        // 난이도별 설명 텍스트 활성화/비활성화
        UpdateDifficultyDescriptions();

        // 선택 상태에 따른 UI 업데이트
        UpdateButtonStates();
    }

    void UpdateDifficultyDescriptions()
    {
        // 선택된 난이도의 설명만 강조 표시 (옵션)
        if (beginnerDescription != null)
        {
            Color textColor = (selectedDifficulty == DifficultyType.Beginner) ? Color.white : new Color(1f, 1f, 1f, 0.6f);
            beginnerDescription.color = textColor;
        }

        if (intermediateDescription != null)
        {
            Color textColor = (selectedDifficulty == DifficultyType.Intermediate) ? Color.white : new Color(1f, 1f, 1f, 0.6f);
            intermediateDescription.color = textColor;
        }

        if (advancedDescription != null)
        {
            Color textColor = (selectedDifficulty == DifficultyType.Advanced) ? Color.white : new Color(1f, 1f, 1f, 0.6f);
            advancedDescription.color = textColor;
        }
    }

    // ═══════════════ 토글 색상 제어 ═══════════════

    void UpdateToggleColors()
    {
        if (!useCustomToggleColors) return;

        // 모드 토글 색상 업데이트
        UpdateModeToggleColor(practiceToggle, selectedMode == ModeType.Practice);
        UpdateModeToggleColor(evaluationToggle, selectedMode == ModeType.Evaluation);

        // 난이도 토글 색상 업데이트
        UpdateDifficultyToggleColor(beginnerToggle, selectedDifficulty == DifficultyType.Beginner);
        UpdateDifficultyToggleColor(intermediateToggle, selectedDifficulty == DifficultyType.Intermediate);
        UpdateDifficultyToggleColor(advancedToggle, selectedDifficulty == DifficultyType.Advanced);
    }

    void UpdateModeToggleColor(Toggle toggle, bool isSelected)
    {
        if (toggle == null) return;

        ColorBlock colors = toggle.colors;

        if (isSelected)
        {
            colors.normalColor = selectedToggleColor;
            colors.highlightedColor = selectedToggleColor * 1.2f;
            colors.pressedColor = selectedToggleColor * 0.8f;
            colors.selectedColor = selectedToggleColor;
        }
        else
        {
            colors.normalColor = normalToggleColor;
            colors.highlightedColor = normalToggleColor * 1.2f;
            colors.pressedColor = normalToggleColor * 0.8f;
            colors.selectedColor = normalToggleColor;
        }

        colors.disabledColor = disabledToggleColor;
        toggle.colors = colors;

        // 텍스트 색상도 업데이트
        TextMeshProUGUI text = toggle.GetComponentInChildren<TextMeshProUGUI>();
        if (text != null)
        {
            text.color = isSelected ? Color.white : new Color(0.8f, 0.8f, 0.8f, 1f);
        }
    }

    void UpdateDifficultyToggleColor(Toggle toggle, bool isSelected)
    {
        if (toggle == null) return;

        ColorBlock colors = toggle.colors;

        if (isSelected)
        {
            colors.normalColor = selectedToggleColor;
            colors.highlightedColor = selectedToggleColor * 1.2f;
            colors.pressedColor = selectedToggleColor * 0.8f;
            colors.selectedColor = selectedToggleColor;
        }
        else
        {
            colors.normalColor = normalToggleColor;
            colors.highlightedColor = normalToggleColor * 1.2f;
            colors.pressedColor = normalToggleColor * 0.8f;
            colors.selectedColor = normalToggleColor;
        }

        colors.disabledColor = disabledToggleColor;
        toggle.colors = colors;
    }

    // 런타임에 색상 변경 메서드
    public void SetSelectedToggleColor(Color color)
    {
        selectedToggleColor = color;
        UpdateToggleColors();
    }

    public void SetNormalToggleColor(Color color)
    {
        normalToggleColor = color;
        UpdateToggleColors();
    }

    public void SetDisabledToggleColor(Color color)
    {
        disabledToggleColor = color;
        UpdateToggleColors();
    }

    void UpdateButtonStates()
    {
        // 모드가 선택되었을 때 버튼 상태 업데이트
        bool isModeSelected = (selectedMode != ModeType.None);

        // 필요시 하단 토글들의 interactable 상태 관리
    }

    void CheckSelectionComplete()
    {
        // 모드가 선택되었는지 확인
        if (selectedMode != ModeType.None)
        {
            Debug.Log($"<color=green>[ModeSelection] ✓ 선택 완료 - 모드: {selectedMode}, 난이도: {selectedDifficulty}</color>");

            // 자동으로 시뮬레이션 시작
            StartSimulation();
        }
    }

    string GetModeText()
    {
        switch (selectedMode)
        {
            case ModeType.Practice:
                return "실습";
            case ModeType.Evaluation:
                return "평가";
            default:
                return "미선택";
        }
    }

    string GetDifficultyText()
    {
        switch (selectedDifficulty)
        {
            case DifficultyType.Beginner:
                return "초급자";
            case DifficultyType.Intermediate:
                return "중급자";
            case DifficultyType.Advanced:
                return "상급자";
            default:
                return "중급자";  // 기본값 중급자
        }
    }

    // ═══════════════ 종료 관련 메서드 ═══════════════

    public void OnExitConfirm()
    {
        // 종료 확인 - 로비로 이동
        Debug.Log("종료 확인됨");
        ReturnToLobby();
    }

    public void OnExitCancel()
    {
        // 종료 취소
        Debug.Log("종료 취소됨");

        // Exit 팝업이 닫혔음을 QuickMenuController에 알림
        if (quickMenuController != null)
        {
            quickMenuController.OnExitPopupClosed();
        }
    }

    void ReturnToLobby()
    {
        Debug.Log("로비로 이동 중...");

        // 로비 씬으로 이동
        // SceneManager.LoadScene("LobbyScene");
    }

    // ═══════════════ 게임 시작 ═══════════════

    public void StartSimulation()
    {
        Debug.Log("<color=cyan>═══════════════════════════════════</color>");
        Debug.Log("<color=cyan>[ModeSelection] StartSimulation() 호출됨</color>");

        if (selectedMode == ModeType.None)
        {
            Debug.LogWarning("[ModeSelection] 모드를 선택해주세요!");
            return;
        }

        Debug.Log($"<color=green>[ModeSelection] 시뮬레이션 시작!</color>");
        Debug.Log($"<color=green>  - 모드: {selectedMode} ({GetModeText()})</color>");
        Debug.Log($"<color=green>  - 난이도: {selectedDifficulty} ({GetDifficultyText()})</color>");

        // ScenarioManager 확인
        if (scenarioManager == null)
        {
            Debug.LogError("<color=red>[ModeSelection] ❌ ScenarioManager가 null입니다! 다시 찾는 중...</color>");
            scenarioManager = FindObjectOfType<ScenarioManager>();

            if (scenarioManager == null)
            {
                Debug.LogError("<color=red>[ModeSelection] ❌ ScenarioManager를 찾을 수 없습니다!</color>");
                Debug.LogError("<color=red>씬에 ScenarioManager 컴포넌트가 있는지 확인하세요.</color>");
                return;
            }
            else
            {
                Debug.Log("<color=yellow>[ModeSelection] ✓ ScenarioManager를 찾았습니다.</color>");
            }
        }

        // ═══ UI 패널 전환 ═══
        Debug.Log("<color=cyan>[ModeSelection] UI 패널 전환 중...</color>");

        // 모드 선택 패널 비활성화
        if (modeSelectionPanel != null)
        {
            modeSelectionPanel.SetActive(false);
            Debug.Log("<color=yellow>[ModeSelection] ✓ 모드 선택 패널 비활성화</color>");
        }
        else
        {
            Debug.LogWarning("<color=yellow>[ModeSelection] ⚠ modeSelectionPanel이 할당되지 않음 (Inspector에서 할당 필요)</color>");
        }

        // 가이드 패널 활성화
        if (guidePanel != null)
        {
            guidePanel.SetActive(true);
            Debug.Log("<color=yellow>[ModeSelection] ✓ 가이드 패널 활성화</color>");
        }
        else
        {
            Debug.LogWarning("<color=yellow>[ModeSelection] ⚠ guidePanel이 할당되지 않음 (Inspector에서 할당 필요)</color>");
        }

        // ═══ ScenarioManager에 모드 정보 전달 ═══
        Debug.Log($"<color=yellow>[ModeSelection] ScenarioManager에 모드 정보 전달 중...</color>");
        scenarioManager.SetModeInfo(GetModeText(), GetDifficultyText());

        // ═══ 시나리오 시작 ═══
        Debug.Log($"<color=yellow>[ModeSelection] ScenarioManager.StartScenario() 호출 중...</color>");
        scenarioManager.StartScenario();

        Debug.Log("<color=green>[ModeSelection] ✓ 시나리오 시작 명령 완료</color>");
        Debug.Log("<color=cyan>═══════════════════════════════════</color>");

        // 선택된 설정 저장
        PlayerPrefs.SetString("SelectedMode", selectedMode.ToString());
        PlayerPrefs.SetString("SelectedDifficulty", selectedDifficulty.ToString());
        PlayerPrefs.Save();
    }

    // ═══════════════ 외부 접근 메서드 ═══════════════

    public ModeType GetSelectedMode()
    {
        return selectedMode;
    }

    public DifficultyType GetSelectedDifficulty()
    {
        return selectedDifficulty;
    }

    public void ResetSelection()
    {
        selectedMode = ModeType.None;
        selectedDifficulty = DifficultyType.Intermediate; // 중급자로 리셋

        // UI 토글 초기화
        if (practiceToggle != null) practiceToggle.isOn = false;
        if (evaluationToggle != null) evaluationToggle.isOn = false;
        if (beginnerToggle != null) beginnerToggle.isOn = false;
        if (intermediateToggle != null) intermediateToggle.isOn = true; // 중급자 기본 선택
        if (advancedToggle != null) advancedToggle.isOn = false;

        UpdateUI();
        UpdateToggleColors();

        Debug.Log("선택 초기화 완료 (중급자로 설정)");
    }

    void OnDestroy()
    {
        // 이벤트 리스너 정리
        if (practiceToggle != null) practiceToggle.onValueChanged.RemoveAllListeners();
        if (evaluationToggle != null) evaluationToggle.onValueChanged.RemoveAllListeners();
        if (beginnerToggle != null) beginnerToggle.onValueChanged.RemoveAllListeners();
        if (intermediateToggle != null) intermediateToggle.onValueChanged.RemoveAllListeners();
        if (advancedToggle != null) advancedToggle.onValueChanged.RemoveAllListeners();
    }
}