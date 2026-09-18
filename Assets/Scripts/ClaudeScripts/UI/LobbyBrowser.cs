using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>로비 실습 카테고리(섹션 소유자).</summary>
public enum LobbyCategory
{
    Simple,   // 단순추나
    Complex,  // 복잡추나
    Rom       // ROM진단
}

/// <summary>
/// 로비 상단 카테고리 탭(전체/단순추나/복잡추나/ROM진단) + 검색바로
/// 하단 섹션/카드의 표시를 필터링한다.
///
/// ★탭은 Unity Toggle(ToggleGroup 안, 하나만 켜짐)로 구현. isOn=true가 된 탭의
///   카테고리로 필터한다. 선택 상태 표시는 Toggle 자체 그래픽/ToggleGroup이 담당.
///
/// [동작]
/// - 탭 선택(isOn): '전체'면 모든 섹션, 특정 카테고리면 해당 섹션만.
/// - 검색: 카드 이름(LobbyItemCard.DisplayName) 포함 카드만. (카테고리 AND 검색)
/// - 필터/검색으로 카드가 없는 섹션은 헤더까지 숨김(hideEmptySections).
///
/// 표시/숨김·검색만 담당. 카드 클릭→시나리오 실행은 ScenarioLaunchButton이 맡는다.
/// </summary>
public class LobbyBrowser : MonoBehaviour
{
    [System.Serializable]
    public class CategoryTab
    {
        [Tooltip("탭 Toggle (4개를 하나의 ToggleGroup에 묶을 것)")]
        public Toggle toggle;
        [Tooltip("체크 시 '전체' 탭 — 모든 카테고리 표시")]
        public bool isAllTab;
        [Tooltip("isAllTab이 false일 때 이 탭이 필터링하는 카테고리")]
        public LobbyCategory category;
    }

    [System.Serializable]
    public class SectionGroup
    {
        [Tooltip("이 섹션이 대표하는 카테고리")]
        public LobbyCategory category;
        [Tooltip("헤더+카드를 포함한 섹션 전체 루트 (표시/숨김 대상)")]
        public GameObject sectionRoot;
        [Tooltip("카드들이 직접 달린 부모 Transform. 검색 카드는 LobbyItemCard를 가진다.")]
        public Transform cardContainer;
    }

    [Header("탭 (전체 / 단순추나 / 복잡추나 / ROM진단) — Toggle")]
    [SerializeField] private CategoryTab[] tabs;

    [Header("섹션 그룹 (단순 / 복잡 / ROM)")]
    [SerializeField] private SectionGroup[] sections;

    [Header("검색 (TMP_InputField, 토글 아님)")]
    [SerializeField] private TMP_InputField searchInput;

    [Header("옵션")]
    [Tooltip("필터/검색 결과 카드가 하나도 없는 섹션은 헤더까지 숨김")]
    [SerializeField] private bool hideEmptySections = true;

    // ★2026-09-17 — 아직 내보내지 않을 카드를 목록으로 숨긴다.
    //   ★<b>씬에서 카드를 꺼 두는 것으로는 안 된다.</b> 아래 Refresh가 모든 카드에
    //     SetActive(show)를 무조건 걸어서, 탭을 한 번만 눌러도 되살아난다.
    //   ★카드를 지우거나 컨테이너 밖으로 빼는 것보다 이쪽이 안전하다 —
    //     되돌릴 때 이 목록에서 빼기만 하면 되고, 씬 구조를 안 건드린다.
    [Tooltip("필터와 무관하게 <b>항상 숨길</b> 카드. 개발 중이라 아직 내보내지 않을 것을 넣는다.\n" +
             "★여기 넣은 카드는 검색으로도 안 나온다.\n" +
             "★되돌리려면 목록에서 빼기만 하면 된다 — 카드는 씬에 그대로 남아 있다.")]
    [SerializeField] private GameObject[] alwaysHiddenCards;

    private bool filterIsAll = true;
    private LobbyCategory activeCategory = LobbyCategory.Simple;
    private string searchLower = string.Empty;

    private void Awake()
    {
        if (tabs != null)
        {
            foreach (var tab in tabs)
            {
                if (tab == null || tab.toggle == null) continue;
                var captured = tab;
                // 토글은 켜질 때/꺼질 때 모두 발동 → isOn(선택)일 때만 필터 적용
                tab.toggle.onValueChanged.AddListener(isOn => { if (isOn) OnTabSelected(captured); });
            }
        }

        if (searchInput != null)
            searchInput.onValueChanged.AddListener(OnSearchChanged);
    }

    private void OnEnable()
    {
        SyncSearch();

        // 현재 켜져있는 탭을 반영. 없으면 '전체' 탭을 켠다(리스너가 필터 적용).
        CategoryTab current = null;
        if (tabs != null)
            foreach (var t in tabs)
                if (t != null && t.toggle != null && t.toggle.isOn) { current = t; break; }

        if (current != null)
        {
            ApplyFilter(current);
            Refresh();
        }
        else
        {
            CategoryTab all = FindAllTab();
            if (all != null && all.toggle != null)
                all.toggle.isOn = true;   // onValueChanged → OnTabSelected → Refresh
            else
            {
                filterIsAll = true;
                Refresh();
            }
        }
    }

    private CategoryTab FindAllTab()
    {
        if (tabs != null)
            foreach (var t in tabs)
                if (t != null && t.isAllTab) return t;
        return null;
    }

    private void OnTabSelected(CategoryTab tab)
    {
        ApplyFilter(tab);
        Refresh();
    }

    private void ApplyFilter(CategoryTab tab)
    {
        filterIsAll = tab.isAllTab;
        activeCategory = tab.category;
    }

    private void SyncSearch()
    {
        searchLower = (searchInput != null && !string.IsNullOrEmpty(searchInput.text))
            ? searchInput.text.Trim().ToLowerInvariant()
            : string.Empty;
    }

    private void OnSearchChanged(string value)
    {
        searchLower = string.IsNullOrEmpty(value) ? string.Empty : value.Trim().ToLowerInvariant();
        Refresh();
    }

    /// <summary>이 카드는 필터와 무관하게 늘 숨기는가.</summary>
    private bool IsAlwaysHidden(GameObject card)
    {
        if (alwaysHiddenCards == null || card == null) return false;

        for (int i = 0; i < alwaysHiddenCards.Length; i++)
            if (alwaysHiddenCards[i] == card) return true;

        return false;
    }

    /// <summary>현재 필터(카테고리 + 검색어)로 섹션/카드 표시를 갱신.</summary>
    public void Refresh()
    {
        if (sections == null) return;

        foreach (var section in sections)
        {
            if (section == null) continue;

            bool categoryPass = filterIsAll || activeCategory == section.category;
            int visibleCards = 0;

            if (section.cardContainer != null)
            {
                int n = section.cardContainer.childCount;
                for (int c = 0; c < n; c++)
                {
                    var child = section.cardContainer.GetChild(c);
                    var card = child.GetComponent<LobbyItemCard>();

                    // 이름 있는 카드는 검색어로도 거르고,
                    // 메타 없는 플레이스홀더는 검색 중이면 숨긴다(이름 없음).
                    bool show = categoryPass && (card != null
                        ? card.MatchesSearch(searchLower)
                        : string.IsNullOrEmpty(searchLower));

                    // ★아직 내보내지 않을 카드는 무엇으로도 안 보인다 — 검색으로도.
                    if (show && IsAlwaysHidden(child.gameObject)) show = false;

                    child.gameObject.SetActive(show);
                    if (show) visibleCards++;
                }
            }

            if (section.sectionRoot != null)
            {
                bool sectionVisible = categoryPass && (!hideEmptySections || visibleCards > 0);
                section.sectionRoot.SetActive(sectionVisible);
            }
        }
    }
}
