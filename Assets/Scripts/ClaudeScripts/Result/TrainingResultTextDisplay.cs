using TMPro;
using UnityEngine;

/// <summary>
/// 실습/평가 결과를 캔버스의 TextMeshProUGUI 한 칸에 통째로 표시하는 임시 컴포넌트.
///
/// 웹뷰 결과 페이지가 준비되기 전까지 쓰는 임시 표시용.
/// 칸을 일일이 만들 필요 없이, TrainingResultData.BuildSummaryText가 만든
/// 전체 결과 문자열(서버 learnLevel2와 동일)을 텍스트 박스 하나에 그대로 대입한다.
/// phase/step 수에 따라 자동으로 늘어나므로 시나리오별 배선이 필요 없다.
///
/// [씬 배치]
/// - resultText: 결과를 표시할 TextMeshProUGUI (ScrollRect 안에 두고 ContentSizeFitter 권장)
/// - resultTracker: 없으면 자동 검색
/// - resultPanel: (선택) 완료 시 활성화할 패널. 비워두면 활성/비활성 제어 안 함
/// </summary>
public class TrainingResultTextDisplay : MonoBehaviour
{
    [Header("=== 참조 ===")]
    [Tooltip("결과를 표시할 텍스트 (ScrollRect Content 안 권장)")]
    [SerializeField] private TextMeshProUGUI resultText;

    [Tooltip("TrainingResultTracker (없으면 자동 검색)")]
    [SerializeField] private TrainingResultTracker resultTracker;

    [Header("=== 표시 설정 ===")]
    [Tooltip("시나리오 완료 시 자동으로 결과 텍스트 갱신")]
    [SerializeField] private bool autoShowOnComplete = true;

    [Tooltip("(선택) 완료 시 활성화할 결과 패널. 비워두면 제어 안 함")]
    [SerializeField] private GameObject resultPanel;

    [Tooltip("결과 데이터가 없을 때 표시할 문구")]
    [SerializeField] private string emptyText = "결과 데이터가 없습니다.";

    private void Awake()
    {
        if (resultTracker == null)
            resultTracker = FindFirstObjectByType<TrainingResultTracker>();
    }

    private void OnEnable()
    {
        if (resultTracker != null)
            resultTracker.OnTrainingCompleted += HandleTrainingCompleted;
    }

    private void OnDisable()
    {
        if (resultTracker != null)
            resultTracker.OnTrainingCompleted -= HandleTrainingCompleted;
    }

    private void HandleTrainingCompleted(TrainingResultData data)
    {
        if (!autoShowOnComplete) return;

        if (resultPanel != null)
            resultPanel.SetActive(true);

        Display(data);
    }

    /// <summary>
    /// 현재 트래커가 가진 결과를 끌어와 표시. 버튼 등에서 수동 호출용.
    /// </summary>
    public void ShowCurrentResult()
    {
        if (resultTracker == null)
        {
            resultTracker = FindFirstObjectByType<TrainingResultTracker>();
            if (resultTracker == null)
            {
                SetText(emptyText);
                return;
            }
        }

        if (resultPanel != null)
            resultPanel.SetActive(true);

        Display(resultTracker.GetResultData());
    }

    /// <summary>
    /// 주어진 결과 데이터를 텍스트로 표시.
    /// 평가(공식)는 상세 포맷, 실습모드는 가벼운 종합 요약.
    /// </summary>
    public void Display(TrainingResultData data)
    {
        if (data == null)
        {
            SetText(emptyText);
            return;
        }

        // ★경추 ROM은 점수·유사도가 전부 0이라(판정 경로가 PassiveStretch) 기존 포맷이 무의미하다.
        //   측정한 각도가 곧 결과이므로 전용 포맷으로 바꿔 보여준다.
        string text;
        if (data.romMeasurements != null && data.romMeasurements.Count > 0)
        {
            text = TrainingResultData.BuildRomSummaryText(data);
        }
        else
        {
            text = data.isOfficialEvaluation
                ? TrainingResultData.BuildSummaryText(data)
                : TrainingResultData.BuildPracticeSummaryText(data);
        }
        SetText(text);
    }

    private void SetText(string text)
    {
        if (resultText != null)
            resultText.text = text;
        else
            ChunaLogger.LogWarning("[ResultTextDisplay] resultText가 할당되지 않았습니다.");
    }
}
