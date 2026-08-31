// ★[미사용 2026-08-31] 씬·프리팹에 배선이 없고, 다른 스크립트에서 부르지도 않는다.
//   지우지 않고 남겨 둔다 — 읽는 사람이 "이게 지금 도는 코드"라고 오해하지 않도록 이 줄을 단다.
//   판정 근거와 재확인 방법: .claude/tools/deadscan.py
using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 버튼 점멸 하이라이트 시스템
/// 순서대로 버튼을 하이라이트하고 클릭 시 다음 버튼으로 진행
/// </summary>
public class ButtonHighlighter : MonoBehaviour
{
    [Header("=== 설정 ===")]
    [Tooltip("점멸 속도 (초)")]
    [SerializeField] private float blinkInterval = 0.5f;

    [Tooltip("하이라이트 색상")]
    [SerializeField] private Color highlightColor = new Color(1f, 0.8f, 0.2f, 1f); // 노란색

    [Tooltip("디버그 로그")]
    [SerializeField] private bool showDebugLogs = true;

    // 상태
    private List<Button> targetButtons = new List<Button>();
    private Dictionary<Button, Color> originalColors = new Dictionary<Button, Color>();
    private int currentButtonIndex = 0;
    private bool isHighlighting = false;
    private Coroutine blinkCoroutine;

    // 이벤트
    public event System.Action<Button> OnButtonClicked;
    public event System.Action OnAllButtonsClicked;

    /// <summary>
    /// 하이라이트할 버튼 목록 설정 및 시작
    /// </summary>
    public void StartHighlighting(List<Button> buttons)
    {
        if (buttons == null || buttons.Count == 0)
        {
            ChunaLogger.LogWarning("[ButtonHighlighter] No buttons to highlight");
            return;
        }

        StopHighlighting();

        targetButtons = new List<Button>(buttons);
        currentButtonIndex = 0;
        originalColors.Clear();

        // 원본 색상 저장 및 리스너 추가
        foreach (var btn in targetButtons)
        {
            if (btn != null)
            {
                var image = btn.GetComponent<Image>();
                if (image != null)
                {
                    originalColors[btn] = image.color;
                }

                // 클릭 리스너 추가
                btn.onClick.AddListener(() => OnTargetButtonClicked(btn));
            }
        }

        isHighlighting = true;
        HighlightCurrentButton();

        if (showDebugLogs)
            ChunaLogger.Log($"[ButtonHighlighter] Started highlighting {targetButtons.Count} buttons");
    }

    /// <summary>
    /// 하이라이트 중지
    /// </summary>
    public void StopHighlighting()
    {
        isHighlighting = false;

        if (blinkCoroutine != null)
        {
            StopCoroutine(blinkCoroutine);
            blinkCoroutine = null;
        }

        // 모든 버튼 원래 색상으로 복원
        foreach (var kvp in originalColors)
        {
            if (kvp.Key != null)
            {
                var image = kvp.Key.GetComponent<Image>();
                if (image != null)
                {
                    image.color = kvp.Value;
                }
            }
        }

        // 리스너 제거
        foreach (var btn in targetButtons)
        {
            if (btn != null)
            {
                btn.onClick.RemoveListener(() => OnTargetButtonClicked(btn));
            }
        }

        targetButtons.Clear();
        originalColors.Clear();
        currentButtonIndex = 0;
    }

    private void OnTargetButtonClicked(Button clickedButton)
    {
        if (!isHighlighting) return;

        // 현재 하이라이트된 버튼인지 확인
        if (currentButtonIndex < targetButtons.Count && targetButtons[currentButtonIndex] == clickedButton)
        {
            if (showDebugLogs)
                ChunaLogger.Log($"[ButtonHighlighter] Button {currentButtonIndex + 1}/{targetButtons.Count} clicked: {clickedButton.name}");

            // 현재 버튼 원래 색상으로 복원
            RestoreButtonColor(clickedButton);

            OnButtonClicked?.Invoke(clickedButton);

            // 다음 버튼으로
            currentButtonIndex++;

            if (currentButtonIndex >= targetButtons.Count)
            {
                // 모든 버튼 클릭 완료
                if (showDebugLogs)
                    ChunaLogger.Log("[ButtonHighlighter] All buttons clicked!");

                StopHighlighting();
                OnAllButtonsClicked?.Invoke();
            }
            else
            {
                // 다음 버튼 하이라이트
                HighlightCurrentButton();
            }
        }
    }

    private void HighlightCurrentButton()
    {
        if (blinkCoroutine != null)
        {
            StopCoroutine(blinkCoroutine);
        }

        if (currentButtonIndex < targetButtons.Count)
        {
            var currentButton = targetButtons[currentButtonIndex];
            if (currentButton != null)
            {
                blinkCoroutine = StartCoroutine(BlinkButton(currentButton));

                if (showDebugLogs)
                    ChunaLogger.Log($"[ButtonHighlighter] Now highlighting: {currentButton.name} ({currentButtonIndex + 1}/{targetButtons.Count})");
            }
        }
    }

    private IEnumerator BlinkButton(Button button)
    {
        var image = button.GetComponent<Image>();
        if (image == null) yield break;

        Color originalColor = originalColors.ContainsKey(button) ? originalColors[button] : image.color;
        bool isHighlightOn = false;

        while (isHighlighting && currentButtonIndex < targetButtons.Count && targetButtons[currentButtonIndex] == button)
        {
            isHighlightOn = !isHighlightOn;
            image.color = isHighlightOn ? highlightColor : originalColor;
            yield return new WaitForSeconds(blinkInterval);
        }

        // 복원
        image.color = originalColor;
    }

    private void RestoreButtonColor(Button button)
    {
        if (button != null && originalColors.ContainsKey(button))
        {
            var image = button.GetComponent<Image>();
            if (image != null)
            {
                image.color = originalColors[button];
            }
        }
    }

    /// <summary>
    /// 현재 진행 상황
    /// </summary>
    public int GetCurrentIndex() => currentButtonIndex;
    public int GetTotalCount() => targetButtons.Count;
    public bool IsHighlighting => isHighlighting;

    void OnDestroy()
    {
        StopHighlighting();
    }
}
