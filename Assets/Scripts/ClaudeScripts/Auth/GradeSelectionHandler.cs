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

        // 2026-10-27: 번호순 정렬(종전엔 서버 순서 그대로) — LoginListLayout
        var grades = new List<string>(usersByGrade.Keys);
        grades.Sort(LoginListLayout.CompareGrade);
        foreach (var grade in grades)
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
                textComponent.text = grade;
                // 10-27 V0.9 타일: «Sub» 글자가 있으면 인원수를 넣는다(없는 옛 버튼은 그대로)
                var sub = buttonObj.transform.Find("Sub")?.GetComponent<TextMeshProUGUI>();
                if (sub != null) sub.text = $"{usersByGrade[grade].Count}명";
                LoginListLayout.ApplyLabel(textComponent);   // ★긴 이름도 말줄임 없이 다 보인다
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

    // 10-28 로그인 한 판: 고른 조 = 흰 알약 · 나머지 = 바탕 없이 글자만(Quest 설정 창 왼쪽 메뉴 꼴)
    private static readonly Color SelectedFill = new Color(0.965f, 0.965f, 0.965f);
    private static readonly Color SelectedInk = new Color(0.153f, 0.153f, 0.153f);
    private static readonly Color Clear = new Color(1f, 1f, 1f, 0f);

    /// <summary>고른 조를 표시한다(한 판 로그인에서만 부른다).</summary>
    public void MarkSelected(string grade)
    {
        foreach (var b in activeButtons)
        {
            if (b == null) continue;
            var label = b.GetComponentInChildren<TextMeshProUGUI>();
            bool on = label != null && label.text == grade;
            var img = b.GetComponent<Image>();
            if (img != null) img.color = on ? SelectedFill : Clear;
            foreach (var t in b.GetComponentsInChildren<TextMeshProUGUI>(true)) t.color = on ? SelectedInk : Color.white;
        }
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
        => LoginListLayout.AdjustContent(content, buttonCount, "GradeSelect");
}
