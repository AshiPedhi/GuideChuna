using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 시나리오 카드 버튼 연결 헬퍼
/// 
/// [사용 방법]
/// 1. 각 시나리오 카드(Card_01~05)에 이 스크립트 추가
/// 2. Inspector에서 Scenario Index 설정 (0~4)
/// 3. Lobby Auth UI 참조 연결
/// 4. 자동으로 Button OnClick 이벤트가 연결됨
/// </summary>
[RequireComponent(typeof(Button))]
public class ScenarioCardButton : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] [Range(0, 4)] private int scenarioIndex;

    [Header("References")]
    [SerializeField] private LobbyAuthUI_Complete lobbyAuthUI;

    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();

        if (button != null)
        {
            button.onClick.AddListener(OnCardClicked);
        }

        if (lobbyAuthUI == null)
        {
            lobbyAuthUI = FindObjectOfType<LobbyAuthUI_Complete>();

            if (lobbyAuthUI == null)
            {
                Debug.LogError($"[ScenarioCard] LobbyAuthUI_Complete를 찾을 수 없습니다! Card Index: {scenarioIndex}");
            }
        }
    }

    private void OnCardClicked()
    {
        if (lobbyAuthUI != null)
        {
            lobbyAuthUI.OnScenarioCardClicked(scenarioIndex);
        }
        else
        {
            Debug.LogError($"[ScenarioCard] LobbyAuthUI 참조가 없습니다! Card Index: {scenarioIndex}");
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // 인스펙터에서 값 변경 시 자동으로 이름 업데이트
        gameObject.name = $"Card_{scenarioIndex + 1:00}";
    }
#endif
}