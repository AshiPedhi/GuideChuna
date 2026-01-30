using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// 토글 점멸 하이라이트 시스템
/// 순서대로 토글을 하이라이트하고 클릭 시 다음 토글로 진행
/// </summary>
public class ToggleHighlighter : MonoBehaviour
{
    [Header("=== 설정 ===")]
    [Tooltip("점멸 속도 (초)")]
    [SerializeField] private float blinkInterval = 0.5f;

    [Tooltip("하이라이트 색상")]
    [SerializeField] private Color highlightColor = new Color(1f, 0.8f, 0.2f, 1f); // 노란색

    [Tooltip("디버그 로그")]
    [SerializeField] private bool showDebugLogs = true;

    // 상태
    private List<Toggle> targetToggles = new List<Toggle>();
    private Dictionary<Toggle, Color> originalColors = new Dictionary<Toggle, Color>();
    private int currentToggleIndex = 0;
    private bool isHighlighting = false;
    private Coroutine blinkCoroutine;

    // 이벤트
    public event System.Action<Toggle> OnToggleClicked;
    public event System.Action OnAllTogglesClicked;

    /// <summary>
    /// 하이라이트할 토글 목록 설정 및 시작
    /// </summary>
    public void StartHighlighting(List<Toggle> toggles)
    {
        if (toggles == null || toggles.Count == 0)
        {
            Debug.LogWarning("[ToggleHighlighter] No toggles to highlight");
            return;
        }

        StopHighlighting();

        targetToggles = new List<Toggle>(toggles);
        currentToggleIndex = 0;
        originalColors.Clear();

        // 원본 색상 저장 및 리스너 추가
        foreach (var toggle in targetToggles)
        {
            if (toggle != null)
            {
                var image = toggle.GetComponent<Image>();
                if (image != null)
                {
                    originalColors[toggle] = image.color;
                }

                // 클릭 리스너 추가
                toggle.onValueChanged.AddListener((_) => OnTargetToggleClicked(toggle));
            }
        }

        isHighlighting = true;
        HighlightCurrentToggle();

        if (showDebugLogs)
            Debug.Log($"[ToggleHighlighter] Started highlighting {targetToggles.Count} toggles");
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

        // 모든 토글 원래 색상으로 복원
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
        foreach (var toggle in targetToggles)
        {
            if (toggle != null)
            {
                toggle.onValueChanged.RemoveListener((_) => OnTargetToggleClicked(toggle));
            }
        }

        targetToggles.Clear();
        originalColors.Clear();
        currentToggleIndex = 0;
    }

    private void OnTargetToggleClicked(Toggle clickedToggle)
    {
        if (!isHighlighting) return;

        // 현재 하이라이트된 토글인지 확인
        if (currentToggleIndex < targetToggles.Count && targetToggles[currentToggleIndex] == clickedToggle)
        {
            if (showDebugLogs)
                Debug.Log($"[ToggleHighlighter] Toggle {currentToggleIndex + 1}/{targetToggles.Count} clicked: {clickedToggle.name}");

            // 현재 토글 원래 색상으로 복원
            RestoreToggleColor(clickedToggle);

            OnToggleClicked?.Invoke(clickedToggle);

            // 다음 토글으로
            currentToggleIndex++;

            if (currentToggleIndex >= targetToggles.Count)
            {
                // 모든 토글 클릭 완료
                if (showDebugLogs)
                    Debug.Log("[ToggleHighlighter] All toggles clicked!");

                StopHighlighting();
                OnAllTogglesClicked?.Invoke();
            }
            else
            {
                // 다음 토글 하이라이트
                HighlightCurrentToggle();
            }
        }
    }

    private void HighlightCurrentToggle()
    {
        if (blinkCoroutine != null)
        {
            StopCoroutine(blinkCoroutine);
        }

        if (currentToggleIndex < targetToggles.Count)
        {
            var currentToggle = targetToggles[currentToggleIndex];
            if (currentToggle != null)
            {
                blinkCoroutine = StartCoroutine(BlinkToggle(currentToggle));

                if (showDebugLogs)
                    Debug.Log($"[ToggleHighlighter] Now highlighting: {currentToggle.name} ({currentToggleIndex + 1}/{targetToggles.Count})");
            }
        }
    }

    private IEnumerator BlinkToggle(Toggle toggle)
    {
        var image = toggle.GetComponent<Image>();
        if (image == null) yield break;

        Color originalColor = originalColors.ContainsKey(toggle) ? originalColors[toggle] : image.color;
        bool isHighlightOn = false;

        while (isHighlighting && currentToggleIndex < targetToggles.Count && targetToggles[currentToggleIndex] == toggle)
        {
            isHighlightOn = !isHighlightOn;
            image.color = isHighlightOn ? highlightColor : originalColor;
            yield return new WaitForSeconds(blinkInterval);
        }

        // 복원
        image.color = originalColor;
    }

    private void RestoreToggleColor(Toggle toggle)
    {
        if (toggle != null && originalColors.ContainsKey(toggle))
        {
            var image = toggle.GetComponent<Image>();
            if (image != null)
            {
                image.color = originalColors[toggle];
            }
        }
    }

    /// <summary>
    /// 현재 진행 상황
    /// </summary>
    public int GetCurrentIndex() => currentToggleIndex;
    public int GetTotalCount() => targetToggles.Count;
    public bool IsHighlighting => isHighlighting;

    void OnDestroy()
    {
        StopHighlighting();
    }
}
