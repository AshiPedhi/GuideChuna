using UnityEngine;
using TMPro;

/// <summary>
/// 로비 실습 카드의 검색용 메타데이터 마커.
///
/// - 카테고리(단순/복잡/ROM)는 "카드가 어느 섹션에 들어있느냐"로 결정되므로 여기서 지정하지 않는다.
///   (LobbyBrowser.SectionGroup.category가 카테고리 소유자)
/// - 이 컴포넌트는 "검색 대상 카드"라는 표시 + 검색 이름 제공만 담당한다.
/// - displayName을 비워두면 자식 TMP_Text(카드 제목)에서 자동으로 읽는다.
/// - 카드 클릭 시 시나리오 진입 동작은 기존 ScenarioCardButton이 담당한다(이 스크립트와 무관).
///
/// [부착 위치] 검색에 걸려야 하는 카드(예: 단순추나 5개)에 부착.
///             플레이스홀더(복잡/ROM 빈 카드)에는 부착 불필요 — 이름이 없어 검색엔 안 잡히고,
///             카테고리 표시/숨김은 섹션 단위로 처리된다.
/// </summary>
public class LobbyItemCard : MonoBehaviour
{
    [Tooltip("검색 대상 이름. 비우면 자식 TMP_Text(카드 제목)에서 자동으로 읽음")]
    [SerializeField] private string displayName;

    private bool resolved;

    /// <summary>검색에 사용할 카드 이름(비면 자식 TMP에서 1회 해석).</summary>
    public string DisplayName
    {
        get
        {
            if (!resolved && string.IsNullOrEmpty(displayName))
            {
                var label = GetComponentInChildren<TMP_Text>(true);
                if (label != null) displayName = label.text;
                resolved = true;
            }
            return displayName ?? string.Empty;
        }
    }

    /// <summary>소문자로 정규화된 검색어에 이 카드가 걸리는지.</summary>
    public bool MatchesSearch(string queryLower)
    {
        if (string.IsNullOrEmpty(queryLower)) return true;
        return DisplayName.ToLowerInvariant().Contains(queryLower);
    }
}
