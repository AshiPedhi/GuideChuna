using ChunaTraining;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// 로비 모드 선택 팝업 — 시나리오 카드를 누르면 뜬다(2026-10-27 사용자 요청 4 · 시안 V1.4).
/// ★10-28 Quest 설정 창 꼴로 바꿈(사용자): 행마다 제목 · 그 아래 설명 · 오른쪽 드롭다운(<see cref="SettingDropdown"/>).
///   행 = ① 난이도(초급 · 중급 · 상급) ② 환경(VR · XR 준비 중 — 막힘). 아래 [실습] · [평가]를 누르면 바로 진입.
/// · 평가에서도 난이도 행은 그대로 보인다(사용자 10-28 «어차피 상관없어») — 평가로 들어가면 난이도는 Evaluation으로 넘긴다(종전).
/// · 고르면 <see cref="LobbyModeChoice"/>에 담고 종전 브레인(<see cref="LobbyAuthUI_Complete.OnScenarioCardClicked(int)"/>)으로
///   씬을 넘긴다 — 로그인 확인 · 인덱스 저장 · 씬 로드는 그대로 브레인이 한다.
/// · 실습 씬은 그 값을 보고 모드 선택 페이지를 건너뛴다(<see cref="InfoPanelController"/>).
///
/// ★컴포넌트는 늘 켜진 오브젝트에 두고, 보이는 판(<see cref="panel"/>)만 켜고 끈다 — 꺼진 오브젝트는 Instance를 못 건다.
/// </summary>
public class LobbyModePopup : MonoBehaviour
{
    public static LobbyModePopup Instance { get; private set; }

    [SerializeField] private GameObject panel;
    [SerializeField] private TMP_Text titleText;
    [Tooltip("술기 사진(카드의 사진을 그대로 보인다) · 비워도 된다")]
    [SerializeField] private Image thumbnail;
    [Tooltip("기법명(카드 부제) · 비워도 된다")]
    [SerializeField] private TMP_Text subtitleText;
    [Tooltip("[실습] — 누르면 바로 진입")]
    [FormerlySerializedAs("vrPracticeButton")]
    [SerializeField] private Button practiceButton;
    [Tooltip("[평가] — 누르면 바로 진입")]
    [FormerlySerializedAs("vrEvaluationButton")]
    [SerializeField] private Button evaluationButton;
    [Tooltip("난이도 행 드롭다운(0 초급 · 1 중급 · 2 상급) — 평가에서도 보이되 값은 안 쓴다(사용자 10-28)")]
    [SerializeField] private SettingDropdown difficultyDropdown;
    [Tooltip("환경 행 드롭다운(0 VR · 1 XR 준비 중 — 막힘)")]
    [SerializeField] private SettingDropdown environmentDropdown;
    [SerializeField] private Button closeButton;

    private static readonly DifficultyLevel[] Levels = { DifficultyLevel.Beginner, DifficultyLevel.Intermediate, DifficultyLevel.Advanced };

    private int pendingIndex = -1;
    private LobbyAuthUI_Complete auth;
    private DifficultyLevel difficulty = DifficultyLevel.Beginner;

    private void Awake()
    {
        Instance = this;
        if (practiceButton != null) practiceButton.onClick.AddListener(() => Choose(InfoPanelController.ModeType.Practice));
        if (evaluationButton != null) evaluationButton.onClick.AddListener(() => Choose(InfoPanelController.ModeType.Evaluation));
        if (difficultyDropdown != null) difficultyDropdown.Changed += i => difficulty = Levels[Mathf.Clamp(i, 0, Levels.Length - 1)];
        if (closeButton != null) closeButton.onClick.AddListener(Hide);
        if (panel != null) panel.SetActive(false);
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    public void Show(int scenarioIndex, string title, LobbyAuthUI_Complete brain, Sprite photo = null, string subtitle = null, string sceneOverride = null)
    {
        pendingScene = sceneOverride;   // 11-13 XR-ROM 카드(RomLineScene)도 팝업을 거친다 — 고른 뒤 그 씬으로
        if (subtitleText != null) subtitleText.text = subtitle ?? "";
        if (thumbnail != null) { thumbnail.sprite = photo; thumbnail.enabled = photo != null; }
        pendingIndex = scenarioIndex;
        auth = brain;
        difficulty = DifficultyLevel.Beginner;
        if (titleText != null) titleText.text = title;
        if (panel != null) panel.SetActive(true);
        if (difficultyDropdown != null) difficultyDropdown.SetValueWithoutNotify(0);
        if (environmentDropdown != null) environmentDropdown.SetValueWithoutNotify(0);   // VR(XR은 준비 중)
        ChunaLogger.Log($"[LobbyModePopup] 열림 — 시나리오 {scenarioIndex} '{title}'");
    }

    public void Hide()
    {
        if (panel != null) panel.SetActive(false);
        pendingIndex = -1;
    }

    private void Choose(InfoPanelController.ModeType mode)
    {
        if (pendingIndex < 0 || auth == null) { ChunaLogger.LogWarning("[LobbyModePopup] 고를 시나리오가 없다 — 무시"); return; }
        DifficultyLevel d = mode == InfoPanelController.ModeType.Evaluation ? DifficultyLevel.Evaluation : difficulty;
        ChunaLogger.Log($"[LobbyModePopup] 진입 — {mode} · 난이도 칸 {difficulty} → 넘김 {d}");
        LobbyModeChoice.Set(mode, d);
        int index = pendingIndex;
        string scene = pendingScene;
        Hide();
        auth.OnScenarioCardClicked(index, scene);
    }
    private string pendingScene;
}
