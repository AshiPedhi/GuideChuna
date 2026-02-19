using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 사용자 선택 패널 관리
/// LobbyAuthUI_Complete에서 추출된 헬퍼 클래스
/// </summary>
public class UserSelectionHandler
{
    private readonly GameObject panel;
    private readonly GameObject buttonPrefab;
    private readonly Transform contentContainer;
    private readonly List<GameObject> activeButtons = new List<GameObject>();

    public UserSelectionHandler(
        GameObject panel,
        GameObject buttonPrefab,
        Transform contentContainer)
    {
        this.panel = panel;
        this.buttonPrefab = buttonPrefab;
        this.contentContainer = contentContainer;
    }

    public void ShowPanel(List<UserData> users, Action<int, string> onUserSelected)
    {
        if (panel == null)
        {
            ChunaLogger.LogError("[UserSelect] userSelectionPanel이 null입니다!");
            return;
        }

        panel.SetActive(true);
        CreateButtons(users, onUserSelected);
        ChunaLogger.Log("[UserSelect] 사용자 선택 패널 표시");
    }

    public void Hide()
    {
        if (panel != null)
        {
            panel.SetActive(false);
        }

        ClearButtons();
        ChunaLogger.Log("[UserSelect] 사용자 선택 패널 숨김");
    }

    private void CreateButtons(List<UserData> users, Action<int, string> onUserSelected)
    {
        ClearButtons();

        if (buttonPrefab == null || contentContainer == null)
        {
            ChunaLogger.LogError("[UserSelect] userButtonPrefab 또는 userContentContainer가 null입니다!");
            return;
        }

        if (users == null || users.Count == 0)
        {
            ChunaLogger.LogWarning("[UserSelect] 선택된 조에 사용자가 없습니다.");
            return;
        }

        foreach (var user in users)
        {
            GameObject buttonObj = UnityEngine.Object.Instantiate(buttonPrefab, contentContainer);

            buttonObj.SetActive(true);

            var rectTransform = buttonObj.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                rectTransform.sizeDelta = new Vector2(280, 60);
            }

            var layoutElement = buttonObj.GetComponent<LayoutElement>();
            if (layoutElement == null)
            {
                layoutElement = buttonObj.AddComponent<LayoutElement>();
            }
            layoutElement.minHeight = 60;
            layoutElement.preferredHeight = 60;

            var textComponent = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
            if (textComponent != null)
            {
                textComponent.text = user.username;
                textComponent.color = Color.white;
                ChunaLogger.Log($"[UserSelect] 사용자 버튼 텍스트 설정: {user.username}");
            }
            else
            {
                ChunaLogger.LogWarning("[UserSelect] 사용자 버튼에서 TextMeshProUGUI를 찾을 수 없습니다!");
            }

            var button = buttonObj.GetComponent<Button>();
            if (button != null)
            {
                int userId = user.idx;
                string username = user.username;
                button.onClick.AddListener(() => onUserSelected?.Invoke(userId, username));
            }

            activeButtons.Add(buttonObj);
        }

        ChunaLogger.Log($"[UserSelect] 사용자 버튼 생성 완료: {activeButtons.Count}개");

        AdjustContentSize(contentContainer, activeButtons.Count);
    }

    private void ClearButtons()
    {
        foreach (var button in activeButtons)
        {
            if (button != null)
            {
                UnityEngine.Object.Destroy(button);
            }
        }
        activeButtons.Clear();
    }

    private void AdjustContentSize(Transform content, int buttonCount)
    {
        if (content == null) return;

        var rectTransform = content.GetComponent<RectTransform>();
        if (rectTransform != null)
        {
            float totalHeight = (60 + 10) * buttonCount;
            rectTransform.sizeDelta = new Vector2(rectTransform.sizeDelta.x, totalHeight);
            ChunaLogger.Log($"[UserSelect] Content 크기 조정: {totalHeight}");
        }

        var layout = content.GetComponent<VerticalLayoutGroup>();
        if (layout == null)
        {
            layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.spacing = 10;
            layout.padding = new RectOffset(10, 10, 10, 10);
            ChunaLogger.Log("[UserSelect] VerticalLayoutGroup 추가됨");
        }
    }
}
