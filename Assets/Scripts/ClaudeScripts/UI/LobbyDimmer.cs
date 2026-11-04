using UnityEngine;

/// <summary>
/// 팝업이 뜨면 뒤의 로비 판을 반투명하게 어둡게(딤) 한다 (2026-10-28 사용자 «로그인 · 시나리오 팝업 뜰 때 로비 UI에 반투명 딤 — 팝업이 강조되게»).
///
/// [판단 기준] 지켜보는 팝업(watch) 중 하나라도 activeInHierarchy면 켠다 — 팝업을 켜고 끄는 코드(LobbyAuthUI_Complete · LobbyModePopup)는
///   건드리지 않고 그 결과만 본다(InfoWindowArranger와 같은 방식). 그래서 어느 경로로 팝업이 닫혀도 딤이 같이 걷힌다.
/// 켜져 있는 동안은 레이를 막아 뒤의 카드가 눌리지 않는다.
/// ★이 오브젝트는 늘 켜 둔다(꺼지면 Update가 안 돈다) — 보이기는 CanvasGroup 알파로만.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class LobbyDimmer : MonoBehaviour
{
    [SerializeField] private GameObject[] watch = new GameObject[0];
    [SerializeField] private float fadeSpeed = 12f;

    private CanvasGroup group;

    private void Awake()
    {
        group = GetComponent<CanvasGroup>();
        Snap();
    }

    private bool AnyOpen()
    {
        for (int i = 0; i < watch.Length; i++)
            if (watch[i] != null && watch[i].activeInHierarchy) return true;
        return false;
    }

    private void LateUpdate()
    {
        bool on = AnyOpen();
        float target = on ? 1f : 0f;
        if (!Mathf.Approximately(group.alpha, target))
            group.alpha = Mathf.MoveTowards(group.alpha, target, fadeSpeed * Time.unscaledDeltaTime);
        if (group.blocksRaycasts != on) { group.blocksRaycasts = on; group.interactable = on; }
    }

    /// <summary>지금 상태로 바로 맞춘다(시작 · 에디터 캡처용 — 캡처도 이 함수를 탄다).</summary>
    public void Snap()
    {
        if (group == null) group = GetComponent<CanvasGroup>();
        bool on = AnyOpen();
        group.alpha = on ? 1f : 0f;
        group.blocksRaycasts = on; group.interactable = on;
    }
}
