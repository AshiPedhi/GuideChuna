using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 훈련 결과를 서버에 업로드하는 컴포넌트.
/// TrainingResultTracker.OnTrainingCompleted 이벤트에 자동 구독.
/// AuthenticationService.PostResultAsync로 ResultData 전송.
///
/// learnLevel2에는 가독성 살린 Step별 상세 평가지표 직렬화 (~750자, VARCHAR(1000) 안에 들어감).
/// 인증 정보(orgID/userId/username)는 LoginStateStore PlayerPrefs에서 로드.
/// </summary>
public class TrainingResultUploader : MonoBehaviour
{
    [Header("=== 참조 ===")]
    [SerializeField] private TrainingResultTracker resultTracker;

    [Header("=== 전송 설정 ===")]
    [Tooltip("시나리오 완료 시 자동 전송")]
    [SerializeField] private bool autoUploadOnComplete = true;

    [Tooltip("평가 모드만 전송 (false면 연습/평가 모두)")]
    [SerializeField] private bool sendOnEvaluationOnly = false;

    [Header("=== ResultData 고정값 ===")]
    [Tooltip("서버에 보낼 subject 필드 (카테고리)")]
    [SerializeField] private string subjectName = "VR현훈추나";

    [Tooltip("서버에 보낼 competenyUnit 필드 (능력단위)")]
    [SerializeField] private string competenyUnit = "단순추나(근막요법)";

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = true;

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
        if (!autoUploadOnComplete || data == null) return;
        if (sendOnEvaluationOnly && !data.isOfficialEvaluation)
        {
            if (showDebugLogs)
                ChunaLogger.Log("[ResultUploader] 평가 모드만 전송 설정 → 연습 결과 업로드 스킵");
            return;
        }

        UploadAsync(data).Forget();
    }

    /// <summary>
    /// 결과 데이터를 서버로 업로드. 외부에서 수동 호출 가능.
    /// </summary>
    public async UniTaskVoid UploadAsync(TrainingResultData data)
    {
        if (data == null)
        {
            ChunaLogger.LogWarning("[ResultUploader] 업로드 데이터가 null");
            return;
        }

        ResultData payload = BuildResultData(data);

        if (string.IsNullOrEmpty(payload.orgID) || payload.userId <= 0)
        {
            ChunaLogger.LogWarning($"[ResultUploader] 인증 정보 부족 (orgID='{payload.orgID}', userId={payload.userId}) → 업로드 스킵, 로컬 CSV는 정상 저장됨");
            return;
        }

        if (AuthenticationService.Instance == null)
        {
            ChunaLogger.LogWarning("[ResultUploader] AuthenticationService.Instance 없음 → 업로드 스킵");
            return;
        }

        try
        {
            if (showDebugLogs)
                ChunaLogger.Log($"<color=cyan>[ResultUploader] 업로드 시작: {payload.username} / {payload.learnModule} / {payload.learnLevel1}</color>");

            await AuthenticationService.Instance.PostResultAsync(payload);

            if (showDebugLogs)
                ChunaLogger.Log($"<color=green>[ResultUploader] 업로드 완료: {payload.username} / runtime={payload.runtime}초</color>");
        }
        catch (System.Exception e)
        {
            ChunaLogger.LogError($"[ResultUploader] 업로드 실패: {e.Message}");
        }
    }

    /// <summary>
    /// TrainingResultData → ResultData 매핑.
    /// </summary>
    private ResultData BuildResultData(TrainingResultData data)
    {
        var loginStore = new LoginStateStore();
        var loginInfo = loginStore.LoadLoginInfo();
        string orgID = loginStore.LoadOrgID();

        return new ResultData
        {
            orgID = orgID,
            userId = loginInfo.userID,
            username = loginInfo.username,
            subject = subjectName,
            competenyUnit = competenyUnit,
            learnModule = string.IsNullOrEmpty(data.scenarioName) ? "" : data.scenarioName,
            // 중도 종료(미완료)는 정식 점수와 구분되도록 표시 (서버/강사가 필터 가능)
            learnLevel1 = data.isOfficialEvaluation
                ? (data.isCompleted ? "(평가)" : "(평가·미완료)")
                : "(연습)",
            learnLevel2 = BuildLearnLevel2(data),
            totalCnt = "0",
            doneCnt = "0",
            runtime = Mathf.RoundToInt(data.totalTime).ToString()
        };
    }

    /// <summary>
    /// 가독성 살린 Step별 상세 평가지표 직렬화 (옵션 C).
    /// 실제 포맷은 TrainingResultData.BuildSummaryText에 있음 (임시 캔버스 결과 표시와 공유).
    /// </summary>
    private static string BuildLearnLevel2(TrainingResultData data)
    {
        return TrainingResultData.BuildSummaryText(data);
    }
}
