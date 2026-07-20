using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 새 로비 메뉴의 시나리오 카드(Toggle) → 기존 브레인(LobbyAuthUI_Complete) 경유 실행 어댑터.
///
/// ★리스킨 원칙: 로그인 체크/로그인 팝업/인덱스 저장/씬 로드 등 실제 동작은 전부
///   LobbyAuthUI_Complete.OnScenarioCardClicked(index)가 담당한다. 이 어댑터는 새 메뉴
///   카드가 Toggle이라 브레인의 Button 자동배선(Card_01~05 탐색)에 안 꽂히는 것을
///   우회해, 토글이 켜지는 순간 그 public 핸들러를 직접 호출만 한다.
///   (이전엔 ScenarioLauncher로 브레인을 우회했으나 종료확인/설문/로그인팝업이 빠져 철회)
///
/// 이 프로젝트의 버튼은 전부 Toggle(모멘터리)이므로 isOn=true 순간 실행하고 곧바로
/// off로 되돌린다. 카테고리 탭과 달리 ToggleGroup엔 넣지 않는다(개별 액션).
///
/// [사용] 동작하는 시나리오 카드에 부착 + scenarioIndex 지정.
///        인덱스는 TrainingScene의 ScenarioBootstrapper.scenarioConfigs[] 순서와 반드시 일치해야 한다.
///        아직 동작하지 않는 플레이스홀더 카드에는 붙이지 않는다.
/// </summary>
[RequireComponent(typeof(Toggle))]
public class ScenarioLaunchButton : MonoBehaviour
{
    [Tooltip("ScenarioBootstrapper.scenarioConfigs[] 위치와 일치해야 하는 시나리오 인덱스(0부터). " +
             "0상부승모근 1견갑거근 2대흉근 3흉쇄유돌근 4사각근 5두개골교정 6두개골PM교정 " +
             "7복와위_하부흉추_굴곡변위 8앙와위_흉추_신전변위 9제1늑골_앙와위 10제2늑골_상방변위 11경추ROM측정")]
    [SerializeField] private int scenarioIndex;

    [Tooltip("비우면 씬에서 자동 검색. 로그인/팝업/씬로드를 담당하는 브레인")]
    [SerializeField] private LobbyAuthUI_Complete lobbyAuthUI;

    private Toggle toggle;

    private void Awake()
    {
        toggle = GetComponent<Toggle>();
        toggle.group = null;               // 액션 버튼은 그룹 미소속(모멘터리)
        toggle.onValueChanged.AddListener(OnChanged);

        if (lobbyAuthUI == null)
        {
            lobbyAuthUI = FindFirstObjectByType<LobbyAuthUI_Complete>();
            if (lobbyAuthUI == null)
                ChunaLogger.LogError($"[ScenarioLaunchButton] LobbyAuthUI_Complete를 찾을 수 없습니다! index={scenarioIndex}");
        }
    }

    private void OnChanged(bool isOn)
    {
        if (!isOn) return;

        // 모멘터리 복구: 로그인 팝업 등으로 씬 전환이 없을 수 있으니 눌린 상태를 즉시 되돌린다.
        toggle.SetIsOnWithoutNotify(false);

        if (lobbyAuthUI != null)
            lobbyAuthUI.OnScenarioCardClicked(scenarioIndex);   // 로그인 체크·팝업·인덱스저장·씬로드 = 브레인이 처리
        else
            ChunaLogger.LogError($"[ScenarioLaunchButton] 브레인 참조 없음! index={scenarioIndex}");
    }

#if UNITY_EDITOR
    public int ScenarioIndex => scenarioIndex;
#endif
}
