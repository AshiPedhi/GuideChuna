using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using ChunaTraining;

/// <summary>
/// 단계 완료 시 유사도 피드백 UI (VR 최적화)
///
/// 표시 형식:
/// ┌──────────────────┐
/// │      72%         │  ← 큰 숫자 (현재)
/// │   ▓▓▓▓▓▓▓░░░     │  ← 프로그레스 바
/// │    목표 65%      │  ← 작은 글씨 (목표)
/// └──────────────────┘
///      🟢 초과!
/// </summary>
public class StepFeedbackUI : BaseUIPanel
{
    [Header("=== UI 요소 ===")]
    [Tooltip("현재 유사도 텍스트 (큰 글씨)")]
    [SerializeField] private TextMeshProUGUI currentSimilarityText;

    [Tooltip("목표 유사도 텍스트 (작은 글씨)")]
    [SerializeField] private TextMeshProUGUI targetSimilarityText;

    [Tooltip("피드백 메시지 텍스트")]
    [SerializeField] private TextMeshProUGUI feedbackMessageText;

    [Tooltip("프로그레스 바 이미지 (Fill)")]
    [SerializeField] private Image progressBarFill;

    [Tooltip("프로그레스 바 배경")]
    [SerializeField] private Image progressBarBackground;

    [Tooltip("전체 패널")]
    [SerializeField] private GameObject feedbackPanel;

    [Header("=== 색상 설정 ===")]
    [Tooltip("초과 달성 색상 (목표 + 10% 이상)")]
    [SerializeField] private Color excellentColor = new Color(0.2f, 0.9f, 0.3f);  // 밝은 초록

    [Tooltip("목표 달성 색상")]
    [SerializeField] private Color goodColor = new Color(0.4f, 0.8f, 0.4f);  // 초록

    [Tooltip("근접 색상 (목표의 80~99%)")]
    [SerializeField] private Color closeColor = new Color(1f, 0.8f, 0.2f);  // 노랑

    [Tooltip("미달 색상 (목표의 80% 미만)")]
    [SerializeField] private Color poorColor = new Color(1f, 0.4f, 0.3f);  // 빨강

    [Header("=== 애니메이션 설정 ===")]
    [Tooltip("피드백 표시 시간 (초)")]
    [SerializeField] private float displayDuration = 2.5f;

    [Tooltip("페이드 아웃 시간 (초)")]
    [SerializeField] private float fadeOutDuration = 0.5f;

    [Tooltip("숫자 카운트업 시간 (초)")]
    [SerializeField] private float countUpDuration = 0.5f;

    [Header("=== 피드백 메시지 ===")]
    [SerializeField] private string excellentMessage = "훌륭해요!";
    [SerializeField] private string goodMessage = "잘했어요!";
    [SerializeField] private string closeMessage = "거의 다 됐어요!";
    [SerializeField] private string poorMessage = "더 연습해봐요";

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = true;

    protected override GameObject GetPanelObject() => feedbackPanel;

    // 내부 상태
    private Coroutine displayCoroutine;
    private CanvasGroup canvasGroup;

    void Awake()
    {
        // CanvasGroup 확보 (페이드용)
        if (feedbackPanel != null)
        {
            canvasGroup = feedbackPanel.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = feedbackPanel.AddComponent<CanvasGroup>();
            }
        }

        // 초기 상태: 숨김
        Hide();
    }

    /// <summary>
    /// 피드백 표시
    /// </summary>
    /// <param name="currentSimilarity">현재 유사도 (0~1)</param>
    /// <param name="targetSimilarity">목표 유사도 (0~1), null이면 DifficultyManager에서 가져옴</param>
    public void ShowFeedback(float currentSimilarity, float? targetSimilarity = null)
    {
        // ★ 난이도 설정에서 상세 점수 비활성화 시 표시하지 않음
        var dm = DifficultyManager.Instance;
        if (dm != null && !dm.ShowDetailedScore) return;

        // ★ 코루틴 시작 전 자신의 GameObject 활성화 필수
        if (!gameObject.activeInHierarchy)
        {
            gameObject.SetActive(true);
        }

        // 목표 유사도 결정
        float target = targetSimilarity ?? GetTargetSimilarity();

        if (showDebugLogs)
        {
            ChunaLogger.Log($"<color=cyan>[StepFeedbackUI] 피드백 표시: {currentSimilarity:P0} / 목표 {target:P0}</color>");
        }

        // 이전 코루틴 중지
        if (displayCoroutine != null)
        {
            StopCoroutine(displayCoroutine);
        }

        // 피드백 표시 시작
        displayCoroutine = StartCoroutine(DisplayFeedbackRoutine(currentSimilarity, target));
    }

    /// <summary>
    /// DifficultyManager에서 목표 유사도 가져오기
    /// </summary>
    private float GetTargetSimilarity()
    {
        if (DifficultyManager.Instance != null)
        {
            return DifficultyManager.Instance.SimilarityThreshold;
        }

        // 기본값: 중급 기준
        return 0.65f;
    }

    /// <summary>
    /// 피드백 표시 코루틴
    /// </summary>
    private IEnumerator DisplayFeedbackRoutine(float currentSimilarity, float targetSimilarity)
    {
        // 패널 표시
        if (feedbackPanel != null)
        {
            feedbackPanel.SetActive(true);
        }

        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
        }

        // 피드백 등급 결정
        FeedbackGrade grade = GetFeedbackGrade(currentSimilarity, targetSimilarity);

        // 색상 및 메시지 설정
        Color feedbackColor = GetGradeColor(grade);
        string feedbackMessage = GetGradeMessage(grade);

        // UI 업데이트 (애니메이션)
        yield return StartCoroutine(AnimateFeedback(currentSimilarity, targetSimilarity, feedbackColor, feedbackMessage));

        // 표시 유지
        yield return new WaitForSeconds(displayDuration);

        // 페이드 아웃
        yield return StartCoroutine(FadeOut());

        // 숨김
        Hide();
    }

    /// <summary>
    /// 피드백 애니메이션
    /// </summary>
    private IEnumerator AnimateFeedback(float currentSimilarity, float targetSimilarity, Color color, string message)
    {
        float elapsed = 0f;

        // 초기값
        if (currentSimilarityText != null)
        {
            currentSimilarityText.text = "0%";
            currentSimilarityText.color = color;
        }

        if (progressBarFill != null)
        {
            progressBarFill.fillAmount = 0f;
            progressBarFill.color = color;
        }

        if (targetSimilarityText != null)
        {
            targetSimilarityText.text = $"목표 {targetSimilarity:P0}";
        }

        if (feedbackMessageText != null)
        {
            feedbackMessageText.text = "";
        }

        // 카운트업 애니메이션
        while (elapsed < countUpDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / countUpDuration);

            // 이징 (ease out)
            float easedT = 1f - Mathf.Pow(1f - t, 3f);

            float displayValue = Mathf.Lerp(0f, currentSimilarity, easedT);

            if (currentSimilarityText != null)
            {
                currentSimilarityText.text = $"{displayValue:P0}";
            }

            if (progressBarFill != null)
            {
                progressBarFill.fillAmount = displayValue;
            }

            yield return null;
        }

        // 최종값 설정
        if (currentSimilarityText != null)
        {
            currentSimilarityText.text = $"{currentSimilarity:P0}";
        }

        if (progressBarFill != null)
        {
            progressBarFill.fillAmount = currentSimilarity;
        }

        // 피드백 메시지 표시 (약간의 딜레이)
        yield return new WaitForSeconds(0.2f);

        if (feedbackMessageText != null)
        {
            feedbackMessageText.text = message;
            feedbackMessageText.color = color;
        }
    }

    /// <summary>
    /// 페이드 아웃
    /// </summary>
    private IEnumerator FadeOut()
    {
        yield return FadeCanvasGroup(canvasGroup, 1f, 0f, fadeOutDuration);
    }

    /// <summary>
    /// 피드백 등급 결정
    /// </summary>
    private FeedbackGrade GetFeedbackGrade(float current, float target)
    {
        if (target <= 0f) return FeedbackGrade.Poor;
        float ratio = current / target;

        if (ratio >= 1.1f)  // 목표 + 10% 이상
            return FeedbackGrade.Excellent;
        else if (ratio >= 1.0f)  // 목표 달성
            return FeedbackGrade.Good;
        else if (ratio >= 0.8f)  // 80% 이상
            return FeedbackGrade.Close;
        else  // 80% 미만
            return FeedbackGrade.Poor;
    }

    /// <summary>
    /// 등급별 색상
    /// </summary>
    private Color GetGradeColor(FeedbackGrade grade)
    {
        switch (grade)
        {
            case FeedbackGrade.Excellent: return excellentColor;
            case FeedbackGrade.Good: return goodColor;
            case FeedbackGrade.Close: return closeColor;
            case FeedbackGrade.Poor: return poorColor;
            default: return goodColor;
        }
    }

    /// <summary>
    /// 등급별 메시지
    /// </summary>
    private string GetGradeMessage(FeedbackGrade grade)
    {
        switch (grade)
        {
            case FeedbackGrade.Excellent: return excellentMessage;
            case FeedbackGrade.Good: return goodMessage;
            case FeedbackGrade.Close: return closeMessage;
            case FeedbackGrade.Poor: return poorMessage;
            default: return goodMessage;
        }
    }

    /// <summary>
    /// 숨김
    /// </summary>
    public override void Hide()
    {
        base.Hide();
    }

    /// <summary>
    /// 피드백 등급
    /// </summary>
    private enum FeedbackGrade
    {
        Excellent,  // 초과 달성
        Good,       // 목표 달성
        Close,      // 근접
        Poor        // 미달
    }

    // ========== 테스트용 ==========

    [ContextMenu("테스트: 초과 달성 (80%)")]
    private void TestExcellent()
    {
        ShowFeedback(0.80f, 0.65f);
    }

    [ContextMenu("테스트: 목표 달성 (68%)")]
    private void TestGood()
    {
        ShowFeedback(0.68f, 0.65f);
    }

    [ContextMenu("테스트: 근접 (55%)")]
    private void TestClose()
    {
        ShowFeedback(0.55f, 0.65f);
    }

    [ContextMenu("테스트: 미달 (40%)")]
    private void TestPoor()
    {
        ShowFeedback(0.40f, 0.65f);
    }
}
