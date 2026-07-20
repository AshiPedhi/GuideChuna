using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로비 메뉴 버튼(조작연습 / 종료·닫기) — Toggle 기반 모멘터리 액션.
///
/// 이 프로젝트의 버튼은 전부 Toggle이라 Button.OnClick 대신 Toggle.onValueChanged를 쓴다.
/// 토글은 켜질 때/꺼질 때 모두 발동하므로, 인스펙터로 직접 배선하면 두 번 호출된다.
/// → 이 컴포넌트가 isOn만 걸러 실행하고 자동으로 off로 되돌린다(인스펙터 OnClick 배선 불필요).
///
/// [사용] 버튼 오브젝트(Toggle 있는)에 부착하고 Action만 선택.
/// </summary>
[RequireComponent(typeof(Toggle))]
public class LobbyMenuActions : MonoBehaviour
{
    public enum MenuAction
    {
        OpenPracticeScene,  // 조작연습 → Practice_Scene
        QuitApp,            // 앱 즉시 종료(팝업/설문 없음)
        ExitViaBrain,       // 종료(X) → 브레인 종료확인 팝업 + 설문 흐름 경유
        UserIconViaBrain,   // 유저 아이콘/로그인 → 브레인(미로그인=조 선택, 로그인=로그아웃 팝업)
        LogoutViaBrain,     // 전용 로그아웃 버튼 → 브레인 로그아웃 확인 팝업 직접 표시(로그인 시에만)
        CloseMenu           // 메뉴 패널 닫기(끄기)
    }

    private bool NeedsBrain => action == MenuAction.ExitViaBrain
                            || action == MenuAction.UserIconViaBrain
                            || action == MenuAction.LogoutViaBrain;

    [Tooltip("이 버튼이 수행할 동작")]
    [SerializeField] private MenuAction action = MenuAction.OpenPracticeScene;

    [Tooltip("CloseMenu일 때 끌 대상. 비면 이 오브젝트를 끔")]
    [SerializeField] private GameObject menuRootToClose;

    [Tooltip("ExitViaBrain일 때 종료흐름을 담당하는 브레인. 비면 씬에서 자동 검색")]
    [SerializeField] private LobbyAuthUI_Complete lobbyAuthUI;

    private Toggle toggle;

    private void Awake()
    {
        toggle = GetComponent<Toggle>();
        toggle.group = null;               // 액션 버튼은 그룹 미소속
        toggle.onValueChanged.AddListener(OnChanged);

        if (NeedsBrain && lobbyAuthUI == null)
        {
            lobbyAuthUI = FindFirstObjectByType<LobbyAuthUI_Complete>();
            if (lobbyAuthUI == null)
                ChunaLogger.LogError($"[LobbyMenuActions] {action}인데 LobbyAuthUI_Complete를 찾을 수 없습니다!");
        }
    }

    private void OnChanged(bool isOn)
    {
        if (!isOn) return;
        toggle.SetIsOnWithoutNotify(false); // 모멘터리 복구

        switch (action)
        {
            case MenuAction.OpenPracticeScene:
                ScenarioLauncher.OpenPracticeScene();
                break;
            case MenuAction.QuitApp:
                ScenarioLauncher.QuitApp();
                break;
            case MenuAction.ExitViaBrain:
                if (lobbyAuthUI != null) lobbyAuthUI.RequestExit();  // 종료확인 팝업 + 설문 = 브레인이 처리
                else ChunaLogger.LogError("[LobbyMenuActions] 브레인 참조 없음 → 종료흐름 실행 불가");
                break;
            case MenuAction.UserIconViaBrain:
                if (lobbyAuthUI != null) lobbyAuthUI.RequestUserIcon();  // 로그인/로그아웃 진입 = 브레인이 처리
                else ChunaLogger.LogError("[LobbyMenuActions] 브레인 참조 없음 → 유저아이콘 동작 불가");
                break;
            case MenuAction.LogoutViaBrain:
                if (lobbyAuthUI != null) lobbyAuthUI.RequestLogout();  // 로그아웃 확인 팝업 = 브레인이 처리
                else ChunaLogger.LogError("[LobbyMenuActions] 브레인 참조 없음 → 로그아웃 동작 불가");
                break;
            case MenuAction.CloseMenu:
                var target = menuRootToClose != null ? menuRootToClose : gameObject;
                target.SetActive(false);
                break;
        }
    }
}
