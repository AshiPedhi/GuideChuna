using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

/// <summary>
/// 연습 모드 UI 표시
/// </summary>
public class PracticeUI : MonoBehaviour
{
    [Header("=== 메인 패널 ===")]
    [SerializeField] private GameObject practicePanel;

    [Header("=== 단계 정보 ===")]
    [SerializeField] private TMP_Text stepTitleText;
    [SerializeField] private TMP_Text instructionText;

    [Header("=== 진행률 ===")]
    [SerializeField] private TMP_Text progressText;
    [SerializeField] private Slider progressSlider;
    [SerializeField] private Image progressFill;

    [Header("=== 단계 완료 ===")]
    [SerializeField] private GameObject stepCompletePanel;
    [SerializeField] private TMP_Text stepCompleteText;
    [SerializeField] private float stepCompleteDuration = 1.5f;

    [Header("=== 전체 완료 ===")]
    [SerializeField] private GameObject completePanel;
    [SerializeField] private TMP_Text completeTitleText;
    [SerializeField] private TMP_Text completeMessageText;

    [Header("=== 색상 ===")]
    [SerializeField] private Color normalColor = new Color(0.2f, 0.6f, 1f);
    [SerializeField] private Color completeColor = new Color(0.2f, 1f, 0.4f);
    [SerializeField] private Color warningColor = new Color(1f, 0.8f, 0.2f);

    private Coroutine stepCompleteCoroutine;

    void Start()
    {
        // 초기 상태
        HideStepComplete();
        HideComplete();
    }

    /// <summary>
    /// 단계 시작 표시
    /// </summary>
    public void ShowStep(string title, string instruction, int current, int total)
    {
        // 패널 표시
        if (practicePanel != null)
            practicePanel.SetActive(true);

        // 제목
        if (stepTitleText != null)
            stepTitleText.text = title;

        // 안내 문구
        if (instructionText != null)
            instructionText.text = instruction;

        // 진행률
        UpdateProgress(current, total);

        // 완료 패널 숨기기
        HideStepComplete();
        HideComplete();

        ChunaLogger.Log($"[PracticeUI] Step: {title}");
    }

    /// <summary>
    /// 진행률 업데이트
    /// </summary>
    public void UpdateProgress(int current, int total)
    {
        // 텍스트
        if (progressText != null)
        {
            if (total > 0)
                progressText.text = $"{current} / {total}";
            else
                progressText.text = "";
        }

        // 슬라이더
        if (progressSlider != null)
        {
            if (total > 0)
            {
                progressSlider.gameObject.SetActive(true);
                progressSlider.maxValue = total;
                progressSlider.value = current;
            }
            else
            {
                progressSlider.gameObject.SetActive(false);
            }
        }

        // 색상 변경 (완료에 가까워지면)
        if (progressFill != null && total > 0)
        {
            float ratio = (float)current / total;
            progressFill.color = Color.Lerp(normalColor, completeColor, ratio);
        }
    }

    /// <summary>
    /// 단계 완료 표시
    /// </summary>
    public void ShowStepComplete(string message)
    {
        if (stepCompleteCoroutine != null)
            StopCoroutine(stepCompleteCoroutine);

        stepCompleteCoroutine = StartCoroutine(ShowStepCompleteRoutine(message));
    }

    private IEnumerator ShowStepCompleteRoutine(string message)
    {
        // 완료 패널 표시
        if (stepCompletePanel != null)
        {
            stepCompletePanel.SetActive(true);

            if (stepCompleteText != null)
                stepCompleteText.text = message;
        }

        // 진행률 색상 완료로 변경
        if (progressFill != null)
            progressFill.color = completeColor;

        yield return new WaitForSeconds(stepCompleteDuration);

        HideStepComplete();
    }

    private void HideStepComplete()
    {
        if (stepCompletePanel != null)
            stepCompletePanel.SetActive(false);
    }

    /// <summary>
    /// 전체 완료 표시
    /// </summary>
    public void ShowComplete(string title, string message)
    {
        // 메인 패널 숨기기
        if (practicePanel != null)
            practicePanel.SetActive(false);

        // 완료 패널 표시
        if (completePanel != null)
        {
            completePanel.SetActive(true);

            if (completeTitleText != null)
                completeTitleText.text = title;

            if (completeMessageText != null)
                completeMessageText.text = message;
        }

        ChunaLogger.Log($"[PracticeUI] Complete: {title}");
    }

    private void HideComplete()
    {
        if (completePanel != null)
            completePanel.SetActive(false);
    }

    /// <summary>
    /// 하이라이트 효과 (선택)
    /// </summary>
    public void HighlightTarget(Transform target)
    {
        // 타겟 오브젝트에 하이라이트 효과 추가
        // 필요 시 구현
    }

    /// <summary>
    /// 하이라이트 제거
    /// </summary>
    public void RemoveHighlight()
    {
        // 하이라이트 효과 제거
        // 필요 시 구현
    }

    /// <summary>
    /// 경고 메시지 표시
    /// </summary>
    public void ShowWarning(string message)
    {
        if (instructionText != null)
        {
            instructionText.text = message;
            instructionText.color = warningColor;
        }
    }

    /// <summary>
    /// UI 전체 숨기기
    /// </summary>
    public void HideAll()
    {
        if (practicePanel != null)
            practicePanel.SetActive(false);

        HideStepComplete();
        HideComplete();
    }

    /// <summary>
    /// UI 전체 표시
    /// </summary>
    public void ShowAll()
    {
        if (practicePanel != null)
            practicePanel.SetActive(true);
    }
}
