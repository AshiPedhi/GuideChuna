using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 동적 결과 테이블 UI
///
/// Phase 수에 따라 자동으로 열이 생성되고,
/// Step별로 행이 생성되어 결과를 표시합니다.
/// 중간에 확인해도 실시간으로 업데이트됩니다.
///
/// [테이블 구조]
/// ┌─────────────┬──────────────┬──────────────┬──────────────┐
/// │    항목     │     전부     │     중부     │     후부     │
/// │             │ 진행률 유사도│ 진행률 유사도│ 진행률 유사도│
/// ├─────────────┼──────────────┼──────────────┼──────────────┤
/// │ 1.진단하기  │   △    64%  │   △    64%  │   △    64%  │
/// │ 2.제한장벽  │   △    53%  │   △    53%  │   △    53%  │
/// │ 3.등척성운동│   ○    76%  │   ○    82%  │   ○    63%  │
/// │ ...         │   ...        │   ...        │   ...        │
/// ├─────────────┼──────────────┴──────────────┴──────────────┤
/// │  종합 결과  │ 수행시간 | 전체유사도 | 경고 | 스킵 | ...  │
/// └─────────────┴────────────────────────────────────────────┘
/// </summary>
public class DynamicResultTableUI : BaseUIPanel
{
    #region Inspector 설정
    [Header("═══ 데이터 소스 ═══")]
    [Tooltip("TrainingResultTracker (없으면 자동 검색)")]
    [SerializeField] private TrainingResultTracker resultTracker;

    [Header("═══ 테이블 컨테이너 ═══")]
    [Tooltip("시나리오 이름 표시 텍스트")]
    [SerializeField] private TextMeshProUGUI scenarioNameText;

    [Tooltip("헤더 행 컨테이너 (Phase 이름들)")]
    [SerializeField] private RectTransform headerRowContainer;

    [Tooltip("데이터 행 컨테이너 (Step별 결과)")]
    [SerializeField] private RectTransform dataRowContainer;

    [Tooltip("종합 결과 컨테이너")]
    [SerializeField] private RectTransform summaryContainer;

    [Header("═══ 프리팹 ═══")]
    [Tooltip("Phase 헤더 셀 프리팹")]
    [SerializeField] private GameObject phaseHeaderCellPrefab;

    [Tooltip("데이터 행 프리팹")]
    [SerializeField] private GameObject dataRowPrefab;

    [Tooltip("데이터 셀 프리팹 (진행률 + 유사도)")]
    [SerializeField] private GameObject dataCellPrefab;

    [Header("═══ 종합 결과 UI ═══")]
    [SerializeField] private TextMeshProUGUI totalTimeText;
    [SerializeField] private TextMeshProUGUI totalSimilarityText;
    [SerializeField] private TextMeshProUGUI warningCountText;
    [SerializeField] private TextMeshProUGUI skipCountText;
    [SerializeField] private TextMeshProUGUI lowestSimilarityText;
    [SerializeField] private TextMeshProUGUI highestSimilarityText;

    [Header("═══ 스타일 설정 ═══")]
    [SerializeField] private Color completeColor = new Color(0.3f, 0.8f, 0.3f);      // O - 녹색
    [SerializeField] private Color partialColor = new Color(0.9f, 0.9f, 0.3f);       // △ - 노란색
    [SerializeField] private Color skippedColor = new Color(0.8f, 0.3f, 0.3f);       // X - 빨간색
    [SerializeField] private Color noneColor = new Color(0.5f, 0.5f, 0.5f);          // - - 회색

    [Header("═══ 자동 업데이트 ═══")]
    [Tooltip("자동 갱신 활성화")]
    [SerializeField] private bool autoRefresh = true;

    [Tooltip("갱신 간격 (초)")]
    [SerializeField] private float refreshInterval = 1f;
    #endregion

    protected override GameObject GetPanelObject() => gameObject;

    #region Private Fields
    // 생성된 UI 요소들
    private List<GameObject> generatedPhaseHeaders = new List<GameObject>();
    private List<GameObject> generatedDataRows = new List<GameObject>();
    private Dictionary<string, Dictionary<string, DataCellUI>> cellLookup = new Dictionary<string, Dictionary<string, DataCellUI>>();

    // Phase/Step 정보 (시나리오에서 가져옴)
    private List<string> phaseNames = new List<string>();
    private List<string> stepNames = new List<string>();

    // 캐시
    private ScenarioManager scenarioManager;
    private float lastRefreshTime = 0f;
    private bool isInitialized = false;
    #endregion

    #region Unity Lifecycle
    void Awake()
    {
        if (resultTracker == null)
            resultTracker = FindFirstObjectByType<TrainingResultTracker>();

        scenarioManager = FindFirstObjectByType<ScenarioManager>();
    }

    void OnEnable()
    {
        // 시나리오 이벤트 구독
        SubscribeEvents();

        // 표시될 때 갱신
        if (isInitialized)
        {
            RefreshTable();
        }
    }

    void OnDisable()
    {
        UnsubscribeEvents();
    }

    void Update()
    {
        // 자동 갱신
        if (autoRefresh && isInitialized && Time.time - lastRefreshTime >= refreshInterval)
        {
            RefreshData();
            lastRefreshTime = Time.time;
        }
    }
    #endregion

    #region 이벤트 구독
    private void SubscribeEvents()
    {
        try
        {
            ScenarioEventSystem.Instance.OnScenarioStarted += OnScenarioStarted;
            ScenarioEventSystem.Instance.OnPhaseChanged += OnPhaseChanged;
            ScenarioEventSystem.Instance.OnStepCompleted += OnStepCompleted;
            ScenarioEventSystem.Instance.OnScenarioCompleted += OnScenarioCompleted;
        }
        catch (Exception e)
        {
            ChunaLogger.LogWarning($"[DynamicResultTable] 이벤트 구독 실패: {e.Message}");
        }
    }

    private void UnsubscribeEvents()
    {
        try
        {
            ScenarioEventSystem.Instance.OnScenarioStarted -= OnScenarioStarted;
            ScenarioEventSystem.Instance.OnPhaseChanged -= OnPhaseChanged;
            ScenarioEventSystem.Instance.OnStepCompleted -= OnStepCompleted;
            ScenarioEventSystem.Instance.OnScenarioCompleted -= OnScenarioCompleted;
        }
        catch (Exception e)
        {
            ChunaLogger.LogWarning($"[DynamicResultTable] 이벤트 해제 실패: {e.Message}");
        }
    }
    #endregion

    #region 이벤트 핸들러
    private void OnScenarioStarted(ScenarioData scenario)
    {
        if (scenario == null) return;

        // 시나리오 이름 표시
        if (scenarioNameText != null)
            scenarioNameText.text = scenario.scenarioName;

        // 테이블 구조 생성
        BuildTableStructure(scenario);
        isInitialized = true;
    }

    private void OnPhaseChanged(PhaseData phase)
    {
        // Phase 변경 시 해당 열 강조 (선택적)
    }

    private void OnStepCompleted(StepData step)
    {
        // Step 완료 시 즉시 갱신
        RefreshData();
    }

    private void OnScenarioCompleted(ScenarioData scenario)
    {
        // 시나리오 완료 시 최종 갱신
        RefreshTable();
    }
    #endregion

    #region 테이블 구조 생성
    /// <summary>
    /// 시나리오 데이터를 기반으로 테이블 구조 생성
    /// </summary>
    public void BuildTableStructure(ScenarioData scenario)
    {
        if (scenario == null)
        {
            ChunaLogger.LogWarning("[DynamicResultTable] 시나리오 데이터가 없습니다.");
            return;
        }

        // 기존 UI 정리
        ClearTable();

        // Phase/Step 정보 수집
        CollectPhaseAndStepInfo(scenario);

        // 헤더 생성 (Phase별 열)
        CreatePhaseHeaders();

        // 데이터 행 생성 (Step별 행)
        CreateDataRows();

        ChunaLogger.Log($"[DynamicResultTable] 테이블 생성 완료 - Phase: {phaseNames.Count}개, Step: {stepNames.Count}개");
    }

    /// <summary>
    /// Phase와 Step 정보 수집
    /// </summary>
    private void CollectPhaseAndStepInfo(ScenarioData scenario)
    {
        phaseNames.Clear();
        stepNames.Clear();

        // 고유한 Step 이름 수집을 위한 세트
        HashSet<string> uniqueSteps = new HashSet<string>();

        foreach (var phase in scenario.phases)
        {
            // "시작하기", "가이드", "종료" 등 결과 표시가 필요 없는 Phase 제외
            if (IsExcludedPhase(phase.phaseName))
                continue;

            phaseNames.Add(phase.phaseName);

            foreach (var step in phase.steps)
            {
                // 가이드 스텝 제외
                if (step.IsGuideStep())
                    continue;

                if (!uniqueSteps.Contains(step.stepName))
                {
                    uniqueSteps.Add(step.stepName);
                    stepNames.Add(step.stepName);
                }
            }
        }
    }

    /// <summary>
    /// 결과 표시에서 제외할 Phase인지 확인
    /// </summary>
    private bool IsExcludedPhase(string phaseName)
    {
        if (phaseName == null) return false;

        string[] excludedPhases = { "시작하기", "가이드", "종료", "진단" };
        foreach (var excluded in excludedPhases)
        {
            if (phaseName.Contains(excluded))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Phase 헤더 생성
    /// </summary>
    private void CreatePhaseHeaders()
    {
        if (headerRowContainer == null || phaseHeaderCellPrefab == null)
        {
            ChunaLogger.LogWarning("[DynamicResultTable] 헤더 컨테이너 또는 프리팹이 없습니다.");
            return;
        }

        foreach (var phaseName in phaseNames)
        {
            // phase가 부위로 나뉘지 않은 시나리오는 헤더 cell을 만들지 않음
            if (string.IsNullOrEmpty(phaseName))
                continue;

            GameObject headerCell = Instantiate(phaseHeaderCellPrefab, headerRowContainer);
            headerCell.name = $"Header_{phaseName}";

            // 텍스트 설정
            var headerText = headerCell.GetComponentInChildren<TextMeshProUGUI>();
            if (headerText != null)
            {
                headerText.text = phaseName;
            }

            generatedPhaseHeaders.Add(headerCell);
        }
    }

    /// <summary>
    /// 데이터 행 생성
    /// </summary>
    private void CreateDataRows()
    {
        if (dataRowContainer == null)
        {
            ChunaLogger.LogWarning("[DynamicResultTable] 데이터 행 컨테이너가 없습니다.");
            return;
        }

        int stepIndex = 1;
        foreach (var stepName in stepNames)
        {
            GameObject row = CreateDataRow(stepIndex, stepName);
            if (row != null)
            {
                generatedDataRows.Add(row);
            }
            stepIndex++;
        }
    }

    /// <summary>
    /// 단일 데이터 행 생성
    /// </summary>
    private GameObject CreateDataRow(int stepIndex, string stepName)
    {
        GameObject row;

        if (dataRowPrefab != null)
        {
            row = Instantiate(dataRowPrefab, dataRowContainer);
        }
        else
        {
            // 프리팹 없으면 기본 생성
            row = new GameObject($"Row_{stepName}");
            row.transform.SetParent(dataRowContainer);

            var rectTransform = row.AddComponent<RectTransform>();
            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 10;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
        }

        row.name = $"Row_{stepName}";

        // Step 이름 셀 (첫 번째 열)
        var stepNameText = row.GetComponentInChildren<TextMeshProUGUI>();
        if (stepNameText != null)
        {
            stepNameText.text = $"{stepIndex}.{stepName}";
        }
        else
        {
            // 텍스트 컴포넌트 생성
            CreateStepNameCell(row, stepIndex, stepName);
        }

        // 각 Phase별 데이터 셀 생성
        cellLookup[stepName] = new Dictionary<string, DataCellUI>();

        foreach (var phaseName in phaseNames)
        {
            DataCellUI cell = CreateDataCell(row, stepName, phaseName);
            if (cell != null)
            {
                cellLookup[stepName][phaseName] = cell;
            }
        }

        return row;
    }

    /// <summary>
    /// Step 이름 셀 생성
    /// </summary>
    private void CreateStepNameCell(GameObject row, int stepIndex, string stepName)
    {
        GameObject cell = new GameObject("StepName");
        cell.transform.SetParent(row.transform);
        cell.transform.SetAsFirstSibling();

        var rectTransform = cell.AddComponent<RectTransform>();
        rectTransform.sizeDelta = new Vector2(150, 40);

        var text = cell.AddComponent<TextMeshProUGUI>();
        text.text = $"{stepIndex}.{stepName}";
        text.fontSize = 14;
        text.alignment = TextAlignmentOptions.Left;
        text.color = Color.white;
    }

    /// <summary>
    /// 데이터 셀 생성 (진행률 + 유사도)
    /// </summary>
    private DataCellUI CreateDataCell(GameObject row, string stepName, string phaseName)
    {
        GameObject cell;

        if (dataCellPrefab != null)
        {
            cell = Instantiate(dataCellPrefab, row.transform);
        }
        else
        {
            // 프리팹 없으면 기본 생성
            cell = new GameObject($"Cell_{phaseName}");
            cell.transform.SetParent(row.transform);

            var rectTransform = cell.AddComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(120, 40);

            var layout = cell.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 5;
            layout.childAlignment = TextAnchor.MiddleCenter;
        }

        cell.name = $"Cell_{stepName}_{phaseName}";

        // DataCellUI 컴포넌트 추가
        var cellUI = cell.GetComponent<DataCellUI>();
        if (cellUI == null)
        {
            cellUI = cell.AddComponent<DataCellUI>();
            cellUI.Initialize();
        }

        return cellUI;
    }

    /// <summary>
    /// 테이블 정리
    /// </summary>
    private void ClearTable()
    {
        // 헤더 정리
        foreach (var header in generatedPhaseHeaders)
        {
            if (header != null) Destroy(header);
        }
        generatedPhaseHeaders.Clear();

        // 행 정리
        foreach (var row in generatedDataRows)
        {
            if (row != null) Destroy(row);
        }
        generatedDataRows.Clear();

        // 셀 참조 정리
        cellLookup.Clear();

        phaseNames.Clear();
        stepNames.Clear();
    }
    #endregion

    #region 데이터 갱신
    /// <summary>
    /// 전체 테이블 갱신 (구조 + 데이터)
    /// </summary>
    public void RefreshTable()
    {
        // 현재 시나리오 정보로 테이블 재구성
        if (scenarioManager != null && scenarioManager.CurrentScenario != null)
        {
            BuildTableStructure(scenarioManager.CurrentScenario);
        }

        RefreshData();
    }

    /// <summary>
    /// 데이터만 갱신 (구조 유지)
    /// </summary>
    public void RefreshData()
    {
        if (resultTracker == null)
        {
            resultTracker = FindFirstObjectByType<TrainingResultTracker>();
            if (resultTracker == null) return;
        }

        var resultData = resultTracker.GetResultData();
        if (resultData == null) return;

        // 각 셀 업데이트
        foreach (var phase in resultData.phaseResults)
        {
            foreach (var step in phase.stepResults)
            {
                UpdateCell(step.stepName, phase.phaseName, step);
            }
        }

        // 종합 결과 업데이트
        UpdateSummary(resultData);

        lastRefreshTime = Time.time;
    }

    /// <summary>
    /// 단일 셀 업데이트
    /// </summary>
    private void UpdateCell(string stepName, string phaseName, TrainingResultData.StepResult stepResult)
    {
        if (!cellLookup.ContainsKey(stepName) || !cellLookup[stepName].ContainsKey(phaseName))
            return;

        var cell = cellLookup[stepName][phaseName];
        if (cell == null) return;

        // 상태 심볼
        string statusSymbol = TrainingResultData.GetStatusSymbol(stepResult.completionStatus);

        // 색상
        Color statusColor = GetStatusColor(stepResult.completionStatus);

        // 유사도
        float similarity = stepResult.averageSimilarity;

        // 셀 업데이트
        cell.SetData(statusSymbol, similarity, statusColor);
    }

    /// <summary>
    /// 종합 결과 업데이트
    /// </summary>
    private void UpdateSummary(TrainingResultData resultData)
    {
        // 수행 시간
        if (totalTimeText != null)
            totalTimeText.text = TrainingResultData.FormatTime(resultData.totalTime);

        // 전체 유사도
        if (totalSimilarityText != null)
            totalSimilarityText.text = $"{resultData.overallSimilarity:P0}";

        // 경고 횟수
        if (warningCountText != null)
            warningCountText.text = $"{resultData.totalWarningCount}회";

        // 스킵 횟수
        if (skipCountText != null)
            skipCountText.text = $"{resultData.totalSkipCount}회";

        // 최저 유사도 구간
        if (lowestSimilarityText != null)
        {
            string lowestStep = string.IsNullOrEmpty(resultData.lowestSimilarityStep) ? "-" : resultData.lowestSimilarityStep;
            lowestSimilarityText.text = lowestStep;
        }

        // 최고 유사도 구간
        if (highestSimilarityText != null)
        {
            string highestStep = string.IsNullOrEmpty(resultData.highestSimilarityStep) ? "-" : resultData.highestSimilarityStep;
            highestSimilarityText.text = highestStep;
        }
    }

    /// <summary>
    /// 상태에 따른 색상 반환
    /// </summary>
    private Color GetStatusColor(TrainingResultData.StepCompletionStatus status)
    {
        switch (status)
        {
            case TrainingResultData.StepCompletionStatus.Complete: return completeColor;
            case TrainingResultData.StepCompletionStatus.Partial: return partialColor;
            case TrainingResultData.StepCompletionStatus.Skipped: return skippedColor;
            default: return noneColor;
        }
    }
    #endregion

    #region Public API
    /// <summary>
    /// 시나리오 이름 설정
    /// </summary>
    public void SetScenarioName(string name)
    {
        if (scenarioNameText != null)
            scenarioNameText.text = name;
    }

    /// <summary>
    /// 자동 갱신 설정
    /// </summary>
    public void SetAutoRefresh(bool enabled, float interval = 1f)
    {
        autoRefresh = enabled;
        refreshInterval = interval;
    }

    /// <summary>
    /// 수동 갱신 트리거
    /// </summary>
    public void ForceRefresh()
    {
        RefreshData();
    }

    /// <summary>
    /// ResultTracker 설정
    /// </summary>
    public void SetResultTracker(TrainingResultTracker tracker)
    {
        resultTracker = tracker;
    }
    #endregion
}

/// <summary>
/// 데이터 셀 UI 컴포넌트
/// 진행률 심볼 + 유사도 퍼센트 표시
/// </summary>
public class DataCellUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI statusText;    // O, △, X
    [SerializeField] private TextMeshProUGUI similarityText; // 64%

    private bool isInitialized = false;

    /// <summary>
    /// 초기화
    /// </summary>
    public void Initialize()
    {
        if (isInitialized) return;

        // 자식에서 텍스트 컴포넌트 찾기
        var texts = GetComponentsInChildren<TextMeshProUGUI>();

        if (texts.Length >= 2)
        {
            statusText = texts[0];
            similarityText = texts[1];
        }
        else if (texts.Length == 1)
        {
            statusText = texts[0];
            // 유사도 텍스트 생성
            CreateSimilarityText();
        }
        else
        {
            // 둘 다 생성
            CreateStatusText();
            CreateSimilarityText();
        }

        isInitialized = true;
    }

    private void CreateStatusText()
    {
        GameObject statusObj = new GameObject("StatusText");
        statusObj.transform.SetParent(transform);

        var rectTransform = statusObj.AddComponent<RectTransform>();
        rectTransform.sizeDelta = new Vector2(30, 30);

        statusText = statusObj.AddComponent<TextMeshProUGUI>();
        statusText.text = "-";
        statusText.fontSize = 18;
        statusText.alignment = TextAlignmentOptions.Center;
        statusText.color = Color.white;
    }

    private void CreateSimilarityText()
    {
        GameObject simObj = new GameObject("SimilarityText");
        simObj.transform.SetParent(transform);

        var rectTransform = simObj.AddComponent<RectTransform>();
        rectTransform.sizeDelta = new Vector2(60, 30);

        similarityText = simObj.AddComponent<TextMeshProUGUI>();
        similarityText.text = "0%";
        similarityText.fontSize = 14;
        similarityText.alignment = TextAlignmentOptions.Center;
        similarityText.color = Color.white;
    }

    /// <summary>
    /// 데이터 설정
    /// </summary>
    public void SetData(string status, float similarity, Color statusColor)
    {
        if (!isInitialized) Initialize();

        if (statusText != null)
        {
            statusText.text = status;
            statusText.color = statusColor;
        }

        if (similarityText != null)
        {
            similarityText.text = $"{similarity * 100:F0}%";
        }
    }

    /// <summary>
    /// 초기화 상태로 리셋
    /// </summary>
    public void Reset()
    {
        SetData("-", 0f, Color.gray);
    }
}
