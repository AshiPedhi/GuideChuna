using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 추나 훈련 최종 결과 요약 UI
///
/// 모든 단계가 완료된 후 전체 결과를 표시
/// - 총합 점수 및 등급
/// - 단계별 상세 결과
/// - 체크포인트별 성과
/// </summary>
public class ChunaResultSummaryUI : MonoBehaviour
{
    [Header("=== 결과 데이터 소스 ===")]
    [SerializeField] private ChunaFeedbackUI feedbackUI;

    [Header("=== 메인 패널 ===")]
    [SerializeField] private GameObject resultPanel;
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("=== 총합 결과 UI ===")]
    [SerializeField] private Text totalScoreText;
    [SerializeField] private Text totalGradeText;
    [SerializeField] private Image gradeImage;
    [SerializeField] private Text totalDurationText;
    [SerializeField] private Text totalCheckpointsText;
    [SerializeField] private Text averageSimilarityText;
    [SerializeField] private Text totalViolationsText;

    [Header("=== 단계별 결과 리스트 ===")]
    [SerializeField] private Transform stepListContainer;
    [SerializeField] private GameObject stepResultItemPrefab;

    [Header("=== 상세 정보 패널 ===")]
    [SerializeField] private GameObject detailPanel;
    [SerializeField] private Text detailStepNameText;
    [SerializeField] private Text detailScoreText;
    [SerializeField] private Text detailCheckpointsText;
    [SerializeField] private Text detailSimilarityText;
    [SerializeField] private Text detailDurationText;
    [SerializeField] private Transform checkpointListContainer;
    [SerializeField] private GameObject checkpointItemPrefab;

    [Header("=== 등급 색상 ===")]
    [SerializeField] private Color gradeS = new Color(1f, 0.84f, 0f);      // Gold
    [SerializeField] private Color gradeA = new Color(0.75f, 0.75f, 0.75f); // Silver
    [SerializeField] private Color gradeB = new Color(0.8f, 0.5f, 0.2f);    // Bronze
    [SerializeField] private Color gradeC = Color.white;
    [SerializeField] private Color gradeF = Color.red;

    [Header("=== 버튼 ===")]
    [SerializeField] private Button closeButton;
    [SerializeField] private Button retryButton;
    [SerializeField] private Button mainMenuButton;

    // 이벤트
    public event Action OnRetryRequested;
    public event Action OnMainMenuRequested;
    public event Action OnResultClosed;

    // 현재 표시 중인 결과
    private List<ChunaFeedbackUI.StepResult> displayedResults;
    private ChunaFeedbackUI.TotalResult displayedTotal;

    void Awake()
    {
        if (feedbackUI == null)
            feedbackUI = FindObjectOfType<ChunaFeedbackUI>();

        // 버튼 이벤트 연결
        if (closeButton != null)
            closeButton.onClick.AddListener(Hide);

        if (retryButton != null)
            retryButton.onClick.AddListener(OnRetryClick);

        if (mainMenuButton != null)
            mainMenuButton.onClick.AddListener(OnMainMenuClick);

        // 초기 숨김
        if (resultPanel != null)
            resultPanel.SetActive(false);

        if (detailPanel != null)
            detailPanel.SetActive(false);
    }

    /// <summary>
    /// 결과 요약 패널 표시
    /// </summary>
    public void Show()
    {
        if (feedbackUI == null)
        {
            Debug.LogError("[ChunaResultSummaryUI] ChunaFeedbackUI를 찾을 수 없습니다!");
            return;
        }

        // 결과 데이터 가져오기
        displayedResults = feedbackUI.GetAllStepResults();
        displayedTotal = feedbackUI.CalculateTotalResult();

        if (displayedResults.Count == 0)
        {
            Debug.LogWarning("[ChunaResultSummaryUI] 표시할 결과가 없습니다.");
            return;
        }

        // UI 업데이트
        UpdateTotalResultUI();
        UpdateStepListUI();

        // 패널 표시
        if (resultPanel != null)
            resultPanel.SetActive(true);

        Debug.Log($"[ChunaResultSummaryUI] 결과 표시 - {displayedResults.Count}개 단계, 평균 점수: {displayedTotal.averageScore:F0}");
    }

    /// <summary>
    /// 결과 요약 패널 숨김
    /// </summary>
    public void Hide()
    {
        if (resultPanel != null)
            resultPanel.SetActive(false);

        if (detailPanel != null)
            detailPanel.SetActive(false);

        OnResultClosed?.Invoke();
    }

    /// <summary>
    /// 총합 결과 UI 업데이트
    /// </summary>
    private void UpdateTotalResultUI()
    {
        if (displayedTotal == null) return;

        // 총점
        if (totalScoreText != null)
            totalScoreText.text = $"{displayedTotal.averageScore:F0}점";

        // 등급
        if (totalGradeText != null)
        {
            totalGradeText.text = displayedTotal.grade;
            totalGradeText.color = GetGradeColor(displayedTotal.averageScore);
        }

        if (gradeImage != null)
            gradeImage.color = GetGradeColor(displayedTotal.averageScore);

        // 소요 시간
        if (totalDurationText != null)
        {
            TimeSpan ts = TimeSpan.FromSeconds(displayedTotal.totalDuration);
            totalDurationText.text = $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        // 체크포인트
        if (totalCheckpointsText != null)
            totalCheckpointsText.text = $"{displayedTotal.passedCheckpoints}/{displayedTotal.totalCheckpoints}";

        // 평균 유사도
        if (averageSimilarityText != null)
            averageSimilarityText.text = $"{displayedTotal.averageSimilarity * 100:F0}%";

        // 위반 횟수
        if (totalViolationsText != null)
            totalViolationsText.text = $"{displayedTotal.totalViolations}회";
    }

    /// <summary>
    /// 단계별 결과 리스트 UI 업데이트
    /// </summary>
    private void UpdateStepListUI()
    {
        if (stepListContainer == null) return;

        // 기존 아이템 제거
        foreach (Transform child in stepListContainer)
        {
            Destroy(child.gameObject);
        }

        // 새 아이템 생성
        for (int i = 0; i < displayedResults.Count; i++)
        {
            var stepResult = displayedResults[i];
            CreateStepResultItem(i, stepResult);
        }
    }

    /// <summary>
    /// 단계 결과 아이템 생성
    /// </summary>
    private void CreateStepResultItem(int index, ChunaFeedbackUI.StepResult result)
    {
        GameObject item;

        if (stepResultItemPrefab != null)
        {
            item = Instantiate(stepResultItemPrefab, stepListContainer);
        }
        else
        {
            // 프리팹 없으면 기본 UI 생성
            item = new GameObject($"Step_{index}");
            item.transform.SetParent(stepListContainer);

            var rectTransform = item.AddComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(400, 60);

            var layout = item.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10;
            layout.padding = new RectOffset(10, 10, 5, 5);
        }

        // StepResultItem 컴포넌트 찾기 또는 추가
        var itemComponent = item.GetComponent<StepResultItem>();
        if (itemComponent == null)
            itemComponent = item.AddComponent<StepResultItem>();

        // 데이터 설정
        itemComponent.Setup(index, result, OnStepItemClicked);
    }

    /// <summary>
    /// 단계 아이템 클릭 시
    /// </summary>
    private void OnStepItemClicked(int index)
    {
        if (index < 0 || index >= displayedResults.Count) return;

        ShowStepDetail(displayedResults[index]);
    }

    /// <summary>
    /// 단계 상세 정보 표시
    /// </summary>
    public void ShowStepDetail(ChunaFeedbackUI.StepResult result)
    {
        if (detailPanel == null) return;

        detailPanel.SetActive(true);

        // 기본 정보
        if (detailStepNameText != null)
            detailStepNameText.text = string.IsNullOrEmpty(result.stepName) ? result.csvFileName : result.stepName;

        if (detailScoreText != null)
            detailScoreText.text = $"{result.finalScore:F0}점 ({result.grade})";

        if (detailCheckpointsText != null)
            detailCheckpointsText.text = $"체크포인트: {result.passedCheckpoints}/{result.totalCheckpoints}";

        if (detailSimilarityText != null)
            detailSimilarityText.text = $"평균 유사도: {result.averageSimilarity * 100:F0}%";

        if (detailDurationText != null)
            detailDurationText.text = $"소요 시간: {result.duration:F1}초";

        // 체크포인트 리스트
        UpdateCheckpointListUI(result.checkpointResults);
    }

    /// <summary>
    /// 체크포인트 리스트 UI 업데이트
    /// </summary>
    private void UpdateCheckpointListUI(List<ChunaFeedbackUI.StepResult.CheckpointResult> checkpoints)
    {
        if (checkpointListContainer == null) return;

        // 기존 아이템 제거
        foreach (Transform child in checkpointListContainer)
        {
            Destroy(child.gameObject);
        }

        // 새 아이템 생성
        foreach (var cp in checkpoints)
        {
            CreateCheckpointItem(cp);
        }
    }

    /// <summary>
    /// 체크포인트 아이템 생성
    /// </summary>
    private void CreateCheckpointItem(ChunaFeedbackUI.StepResult.CheckpointResult checkpoint)
    {
        GameObject item;

        if (checkpointItemPrefab != null)
        {
            item = Instantiate(checkpointItemPrefab, checkpointListContainer);
        }
        else
        {
            item = new GameObject($"Checkpoint_{checkpoint.index}");
            item.transform.SetParent(checkpointListContainer);

            var text = item.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 14;
        }

        // 텍스트 설정
        var textComponent = item.GetComponentInChildren<Text>();
        if (textComponent != null)
        {
            string passedMark = checkpoint.passed ? "✓" : "✗";
            textComponent.text = $"{passedMark} {checkpoint.name}: {checkpoint.similarity * 100:F0}% ({checkpoint.timeToPass:F1}초)";
            textComponent.color = checkpoint.passed ? Color.green : Color.red;
        }
    }

    /// <summary>
    /// 상세 패널 닫기
    /// </summary>
    public void HideStepDetail()
    {
        if (detailPanel != null)
            detailPanel.SetActive(false);
    }

    // ========== 버튼 핸들러 ==========

    private void OnRetryClick()
    {
        Hide();
        OnRetryRequested?.Invoke();
    }

    private void OnMainMenuClick()
    {
        Hide();
        OnMainMenuRequested?.Invoke();
    }

    // ========== 유틸리티 ==========

    private Color GetGradeColor(float score)
    {
        if (score >= 90f) return gradeS;
        if (score >= 80f) return gradeA;
        if (score >= 70f) return gradeB;
        if (score >= 60f) return gradeC;
        return gradeF;
    }

    /// <summary>
    /// 외부에서 결과 데이터 직접 설정
    /// </summary>
    public void SetResults(List<ChunaFeedbackUI.StepResult> results, ChunaFeedbackUI.TotalResult total)
    {
        displayedResults = results;
        displayedTotal = total;
        UpdateTotalResultUI();
        UpdateStepListUI();
    }
}

/// <summary>
/// 단계 결과 아이템 컴포넌트
/// </summary>
public class StepResultItem : MonoBehaviour
{
    [SerializeField] private Text stepNameText;
    [SerializeField] private Text scoreText;
    [SerializeField] private Text gradeText;
    [SerializeField] private Image statusIcon;
    [SerializeField] private Button selectButton;

    private int stepIndex;
    private Action<int> onClickCallback;

    public void Setup(int index, ChunaFeedbackUI.StepResult result, Action<int> onClick)
    {
        stepIndex = index;
        onClickCallback = onClick;

        // 텍스트 설정
        if (stepNameText != null)
            stepNameText.text = string.IsNullOrEmpty(result.stepName) ? $"단계 {index + 1}" : result.stepName;

        if (scoreText != null)
            scoreText.text = $"{result.finalScore:F0}점";

        if (gradeText != null)
        {
            gradeText.text = result.grade;
            gradeText.color = GetGradeColor(result.finalScore);
        }

        // 상태 아이콘
        if (statusIcon != null)
        {
            bool allPassed = result.passedCheckpoints >= result.totalCheckpoints;
            statusIcon.color = allPassed ? Color.green : Color.yellow;
        }

        // 버튼 이벤트
        if (selectButton != null)
        {
            selectButton.onClick.RemoveAllListeners();
            selectButton.onClick.AddListener(OnClick);
        }
        else
        {
            // 버튼 없으면 전체 오브젝트에 클릭 추가
            var button = gameObject.GetComponent<Button>();
            if (button == null)
                button = gameObject.AddComponent<Button>();

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OnClick);
        }
    }

    private void OnClick()
    {
        onClickCallback?.Invoke(stepIndex);
    }

    private Color GetGradeColor(float score)
    {
        if (score >= 90f) return new Color(1f, 0.84f, 0f);
        if (score >= 80f) return new Color(0.75f, 0.75f, 0.75f);
        if (score >= 70f) return new Color(0.8f, 0.5f, 0.2f);
        if (score >= 60f) return Color.white;
        return Color.red;
    }
}
