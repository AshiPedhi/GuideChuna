using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 추나 수행 결과를 테이블 형식으로 표시하는 UI
///
/// 구조:
/// - 행: Step별 (1.제한장벽 확인 > 회전/측굴, 2.등척성 운동, 3.스트레칭, 4.재평가하기)
/// - 열: Phase별 (전부/중부/후부) × (소요시간, 유사도)
/// </summary>
public class ChunaResultTableUI : MonoBehaviour
{
    [Header("=== 패널 ===")]
    [SerializeField] private GameObject resultPanel;
    [SerializeField] private TextMeshProUGUI titleText;

    [Header("=== 테이블 컨테이너 ===")]
    [Tooltip("테이블 행들이 들어갈 부모 Transform (Vertical Layout Group)")]
    [SerializeField] private Transform tableContainer;

    [Header("=== 프리팹 ===")]
    [Tooltip("Step 그룹 헤더 프리팹 (예: 1.제한장벽 확인)")]
    [SerializeField] private GameObject stepGroupHeaderPrefab;

    [Tooltip("SubStep 행 프리팹 (예: 회전, 측굴)")]
    [SerializeField] private GameObject subStepRowPrefab;

    [Header("=== 좌측 메뉴 버튼 ===")]
    [SerializeField] private Button infoButton;
    [SerializeField] private Button videoButton;
    [SerializeField] private Button muscleButton;
    [SerializeField] private Button resultButton;
    [SerializeField] private Button historyButton;

    [Header("=== 색상 설정 ===")]
    [SerializeField] private Color headerColor = new Color(0.2f, 0.2f, 0.2f, 1f);
    [SerializeField] private Color rowColor = new Color(0.15f, 0.15f, 0.15f, 1f);
    [SerializeField] private Color highlightColor = new Color(0.3f, 0.3f, 0.3f, 1f);

    // 데이터
    private ResultTableData currentData;
    private List<GameObject> spawnedRows = new List<GameObject>();

    // 이벤트
    public event Action OnInfoClicked;
    public event Action OnVideoClicked;
    public event Action OnMuscleClicked;
    public event Action OnHistoryClicked;

    void Awake()
    {
        // 버튼 이벤트 연결
        if (infoButton != null) infoButton.onClick.AddListener(() => OnInfoClicked?.Invoke());
        if (videoButton != null) videoButton.onClick.AddListener(() => OnVideoClicked?.Invoke());
        if (muscleButton != null) muscleButton.onClick.AddListener(() => OnMuscleClicked?.Invoke());
        if (historyButton != null) historyButton.onClick.AddListener(() => OnHistoryClicked?.Invoke());

        // 초기 숨김
        if (resultPanel != null)
            resultPanel.SetActive(false);
    }

    /// <summary>
    /// 결과 데이터로 테이블 표시
    /// </summary>
    public void Show(ResultTableData data)
    {
        currentData = data;

        // 제목 설정
        if (titleText != null && data != null)
        {
            titleText.text = data.procedureName;
        }

        // 테이블 생성
        BuildTable(data);

        // 패널 표시
        if (resultPanel != null)
            resultPanel.SetActive(true);
    }

    /// <summary>
    /// 패널 숨기기
    /// </summary>
    public void Hide()
    {
        if (resultPanel != null)
            resultPanel.SetActive(false);
    }

    /// <summary>
    /// 테이블 구성
    /// </summary>
    private void BuildTable(ResultTableData data)
    {
        // 기존 행 제거
        ClearTable();

        if (data == null || data.stepGroups == null) return;

        // Step 그룹별로 행 생성
        foreach (var stepGroup in data.stepGroups)
        {
            // Step 그룹 헤더 생성 (예: "1.제한장벽 확인")
            if (stepGroupHeaderPrefab != null && !string.IsNullOrEmpty(stepGroup.stepName))
            {
                var header = Instantiate(stepGroupHeaderPrefab, tableContainer);
                var headerText = header.GetComponentInChildren<TextMeshProUGUI>();
                if (headerText != null)
                {
                    headerText.text = stepGroup.stepName;
                }
                spawnedRows.Add(header);
            }

            // SubStep 행 생성 (예: "회전", "측굴")
            if (stepGroup.subSteps != null)
            {
                foreach (var subStep in stepGroup.subSteps)
                {
                    CreateSubStepRow(subStep);
                }
            }
        }
    }

    /// <summary>
    /// SubStep 행 생성
    /// </summary>
    private void CreateSubStepRow(SubStepResultData subStep)
    {
        if (subStepRowPrefab == null) return;

        var row = Instantiate(subStepRowPrefab, tableContainer);
        spawnedRows.Add(row);

        // 행 컴포넌트 찾기
        var rowUI = row.GetComponent<ResultTableRowUI>();
        if (rowUI != null)
        {
            rowUI.SetData(subStep);
        }
        else
        {
            // 컴포넌트가 없으면 직접 텍스트 설정 시도
            SetRowTextsManually(row, subStep);
        }
    }

    /// <summary>
    /// 수동으로 행 텍스트 설정 (ResultTableRowUI 컴포넌트가 없는 경우)
    /// </summary>
    private void SetRowTextsManually(GameObject row, SubStepResultData subStep)
    {
        var texts = row.GetComponentsInChildren<TextMeshProUGUI>();

        // 순서: 항목명, 전부시간, 전부유사도, 중부시간, 중부유사도, 후부시간, 후부유사도
        if (texts.Length >= 7)
        {
            texts[0].text = subStep.subStepName;

            // 전부 (Front)
            texts[1].text = FormatTime(subStep.frontDuration);
            texts[2].text = FormatPercent(subStep.frontSimilarity);

            // 중부 (Middle)
            texts[3].text = FormatTime(subStep.middleDuration);
            texts[4].text = FormatPercent(subStep.middleSimilarity);

            // 후부 (Back)
            texts[5].text = FormatTime(subStep.backDuration);
            texts[6].text = FormatPercent(subStep.backSimilarity);
        }
    }

    /// <summary>
    /// 시간 포맷 (빈 값이면 빈 문자열)
    /// </summary>
    private string FormatTime(float? duration)
    {
        if (!duration.HasValue || duration.Value <= 0)
            return "";
        return duration.Value.ToString("F1");
    }

    /// <summary>
    /// 퍼센트 포맷 (빈 값이면 빈 문자열)
    /// </summary>
    private string FormatPercent(float? similarity)
    {
        if (!similarity.HasValue || similarity.Value <= 0)
            return "";
        return $"{similarity.Value:F0}%";
    }

    /// <summary>
    /// 테이블 행 모두 제거
    /// </summary>
    private void ClearTable()
    {
        foreach (var row in spawnedRows)
        {
            if (row != null)
                Destroy(row);
        }
        spawnedRows.Clear();
    }

    /// <summary>
    /// 테스트용 더미 데이터로 표시
    /// </summary>
    [ContextMenu("Test With Dummy Data")]
    public void TestWithDummyData()
    {
        var dummyData = CreateDummyData();
        Show(dummyData);
    }

    /// <summary>
    /// 테스트용 더미 데이터 생성
    /// </summary>
    private ResultTableData CreateDummyData()
    {
        return new ResultTableData
        {
            procedureName = "상부승모근",
            stepGroups = new List<StepGroupData>
            {
                new StepGroupData
                {
                    stepName = "1.제한장벽 확인",
                    subSteps = new List<SubStepResultData>
                    {
                        new SubStepResultData { subStepName = "회전", frontDuration = 5.3f, frontSimilarity = 60f, backDuration = 4.6f, backSimilarity = 78f },
                        new SubStepResultData { subStepName = "측굴", frontDuration = 7.6f, frontSimilarity = 76f, middleDuration = 8.2f, middleSimilarity = 82f, backDuration = 6.8f, backSimilarity = 63f }
                    }
                },
                new StepGroupData
                {
                    stepName = "2.등척성 운동",
                    subSteps = new List<SubStepResultData>
                    {
                        new SubStepResultData { subStepName = "", frontDuration = 7.6f, frontSimilarity = 76f, middleDuration = 8.2f, middleSimilarity = 82f, backDuration = 6.8f, backSimilarity = 63f }
                    }
                },
                new StepGroupData
                {
                    stepName = "3.스트레칭",
                    subSteps = new List<SubStepResultData>
                    {
                        new SubStepResultData { subStepName = "측굴", frontDuration = 7.6f, frontSimilarity = 76f, middleDuration = 8.2f, middleSimilarity = 82f, backDuration = 6.8f, backSimilarity = 63f }
                    }
                },
                new StepGroupData
                {
                    stepName = "4.재평가하기",
                    subSteps = new List<SubStepResultData>
                    {
                        new SubStepResultData { subStepName = "회전", frontDuration = 5.3f, frontSimilarity = 60f, backDuration = 4.6f, backSimilarity = 78f },
                        new SubStepResultData { subStepName = "측굴", frontDuration = 7.6f, frontSimilarity = 76f, middleDuration = 8.2f, middleSimilarity = 82f, backDuration = 6.8f, backSimilarity = 63f }
                    }
                }
            }
        };
    }
}

/// <summary>
/// 결과 테이블 전체 데이터
/// </summary>
[Serializable]
public class ResultTableData
{
    public string procedureName;  // 시술 이름 (예: "상부승모근")
    public List<StepGroupData> stepGroups;
}

/// <summary>
/// Step 그룹 데이터 (예: 1.제한장벽 확인)
/// </summary>
[Serializable]
public class StepGroupData
{
    public string stepName;  // Step 이름 (예: "1.제한장벽 확인")
    public List<SubStepResultData> subSteps;
}

/// <summary>
/// SubStep 결과 데이터 (한 행)
/// </summary>
[Serializable]
public class SubStepResultData
{
    public string subStepName;  // SubStep 이름 (예: "회전", "측굴")

    // 전부 (Front)
    public float? frontDuration;
    public float? frontSimilarity;

    // 중부 (Middle)
    public float? middleDuration;
    public float? middleSimilarity;

    // 후부 (Back)
    public float? backDuration;
    public float? backSimilarity;
}
