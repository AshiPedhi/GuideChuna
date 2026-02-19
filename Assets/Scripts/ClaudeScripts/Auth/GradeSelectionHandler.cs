using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 조 선택 패널 관리
/// LobbyAuthUI_Complete에서 추출된 헬퍼 클래스
/// </summary>
public class GradeSelectionHandler
{
    private readonly GameObject panel;
    private readonly GameObject buttonPrefab;
    private readonly Transform contentContainer;
    private readonly List<GameObject> activeButtons = new List<GameObject>();

    public GradeSelectionHandler(
        GameObject panel,
        GameObject buttonPrefab,
        Transform contentContainer)
    {
        this.panel = panel;
        this.buttonPrefab = buttonPrefab;
        this.contentContainer = contentContainer;
    }

    public void ShowPanel(Dictionary<string, List<UserData>> usersByGrade, Action<string> onGradeSelected)
    {
        if (panel == null)
        {
            ChunaLogger.LogError("[GradeSelect] gradeSelectionPanel이 null입니다!");
            return;
        }

        panel.SetActive(true);
        CreateButtons(usersByGrade, onGradeSelected);
        ChunaLogger.Log("[GradeSelect] 조 선택 패널 표시");
    }

    public void Hide()
    {
        if (panel != null)
        {
            panel.SetActive(false);
        }

        ClearButtons();
        ChunaLogger.Log("[GradeSelect] 조 선택 패널 숨김");
    }

    private void CreateButtons(Dictionary<string, List<UserData>> usersByGrade, Action<string> onGradeSelected)
    {
        ClearButtons();

        if (buttonPrefab == null || contentContainer == null)
        {
            ChunaLogger.LogError("[GradeSelect] gradeButtonPrefab 또는 gradeContentContainer가 null입니다!");
            return;
        }

        foreach (var kvp in usersByGrade)
        {
            string grade = kvp.Key;
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
                textComponent.text = grade;
                textComponent.color = Color.white;
                ChunaLogger.Log($"[GradeSelect] 조 버튼 텍스트 설정: {grade}");
            }
            else
            {
                ChunaLogger.LogWarning("[GradeSelect] 조 버튼에서 TextMeshProUGUI를 찾을 수 없습니다!");
            }

            var button = buttonObj.GetComponent<Button>();
            if (button != null)
            {
                button.onClick.AddListener(() => onGradeSelected?.Invoke(grade));
            }

            activeButtons.Add(buttonObj);
        }

        ChunaLogger.Log($"[GradeSelect] 조 버튼 생성 완료: {activeButtons.Count}개");

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
            ChunaLogger.Log($"[GradeSelect] Content 크기 조정: {totalHeight}");
        }

        var layout = content.GetComponent<VerticalLayoutGroup>();
        if (layout == null)
        {
            layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.spacing = 10;
            layout.padding = new RectOffset(10, 10, 10, 10);
            ChunaLogger.Log("[GradeSelect] VerticalLayoutGroup 추가됨");
        }
    }
}
