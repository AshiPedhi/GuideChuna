using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 훈련 결과를 CSV 파일로 저장하는 컴포넌트.
/// TrainingResultTracker.OnTrainingCompleted 이벤트에 자동 구독.
/// </summary>
public class TrainingResultExporter : MonoBehaviour
{
    [SerializeField] private TrainingResultTracker resultTracker;

    [Header("저장 설정")]
    [SerializeField] private bool autoSaveOnComplete = true;
    [SerializeField] private string resultFolderName = "Results";

    private static readonly CultureInfo inv = CultureInfo.InvariantCulture;

    private void OnEnable()
    {
        if (resultTracker != null)
            resultTracker.OnTrainingCompleted += OnTrainingCompleted;
    }

    private void OnDisable()
    {
        if (resultTracker != null)
            resultTracker.OnTrainingCompleted -= OnTrainingCompleted;
    }

    private void OnTrainingCompleted(TrainingResultData data)
    {
        if (!autoSaveOnComplete || data == null) return;

        try
        {
            string summaryPath = SaveSummaryCSV(data);
            string timelinePath = SaveTimelineCSV(data);

            ChunaLogger.Log($"<color=green>[ResultExporter] 결과 저장 완료</color>\n  요약: {summaryPath}\n  시계열: {timelinePath}");
        }
        catch (Exception e)
        {
            ChunaLogger.LogError($"[ResultExporter] 저장 실패: {e.Message}");
        }
    }

    // ========== Public API ==========

    /// <summary>
    /// 수동으로 결과를 CSV로 저장
    /// </summary>
    public string SaveSummaryCSV(TrainingResultData data)
    {
        string path = GetSavePath(data, "summary");
        EnsureDirectory(path);

        var sb = new StringBuilder();
        WriteSummaryHeader(sb);
        WriteSummaryRows(sb, data);

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        return path;
    }

    /// <summary>
    /// 시계열 데이터를 CSV로 저장
    /// </summary>
    public string SaveTimelineCSV(TrainingResultData data)
    {
        string path = GetSavePath(data, "timeline");
        EnsureDirectory(path);

        var sb = new StringBuilder();
        WriteTimelineHeader(sb);
        WriteTimelineRows(sb, data);

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        return path;
    }

    /// <summary>
    /// 저장 경로 반환 (외부에서 확인용)
    /// </summary>
    public string GetResultFolder()
    {
        return GetBasePath();
    }

    // ========== CSV 작성 ==========

    private void WriteSummaryHeader(StringBuilder sb)
    {
        sb.AppendLine(string.Join(",",
            "SessionId",
            "UserName",
            "UserId",
            "Scenario",
            "StartTime",
            "EndTime",
            "Mode",
            "Difficulty",
            "IsOfficial",
            "AttemptNumber",
            "TotalTime",
            "OverallSimilarity",
            "OverallScore",
            "OverallGrade",
            "TotalWarnings",
            "TotalLimitViolations",
            "TotalSkips",
            "Phase",
            "Step",
            "Status",
            "Similarity",
            "LeftSimilarity",
            "RightSimilarity",
            "SimilarityStdDev",
            "MinSimilarity",
            "MaxSimilarity",
            "Score",
            "Grade",
            "LimitViolations",
            "TimeInWarning",
            "TimeExceeded",
            "PeakExceededRatio",
            "StepTime",
            "SubSteps",
            "Completed",
            "Skipped"
        ));
    }

    private void WriteSummaryRows(StringBuilder sb, TrainingResultData data)
    {
        string sessionId = Escape(data.sessionId);
        string userName = Escape(data.userName);
        string odUserId = data.userId.ToString(inv);
        string scenario = Escape(data.scenarioName);
        string startTime = data.startTime.ToString("yyyy-MM-dd HH:mm:ss");
        string endTime = data.endTime.ToString("yyyy-MM-dd HH:mm:ss");
        string mode = Escape(data.selectedMode);
        string difficulty = Escape(data.selectedDifficulty);
        string isOfficial = data.isOfficialEvaluation ? "1" : "0";
        string attempt = data.attemptNumber.ToString(inv);
        string totalTime = data.totalTime.ToString("F1", inv);
        string overallSim = data.overallSimilarity.ToString("F4", inv);
        string overallScore = data.overallScore.ToString("F1", inv);
        string overallGrade = Escape(data.overallGrade);
        string totalWarnings = data.totalWarningCount.ToString(inv);
        string totalViolations = data.totalLimitViolations.ToString(inv);
        string totalSkips = data.totalSkipCount.ToString(inv);

        foreach (var phase in data.phaseResults)
        {
            string phaseName = Escape(phase.phaseName);

            foreach (var step in phase.stepResults)
            {
                sb.AppendFormat(inv,
                    "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21},{22},{23},{24},{25},{26},{27},{28},{29},{30},{31},{32},{33}\n",
                    sessionId,
                    userName,
                    odUserId,
                    scenario,
                    startTime,
                    endTime,
                    mode,
                    difficulty,
                    isOfficial,
                    attempt,
                    totalTime,
                    overallSim,
                    overallScore,
                    overallGrade,
                    totalWarnings,
                    totalViolations,
                    totalSkips,
                    phaseName,
                    Escape(step.stepName),
                    TrainingResultData.GetStatusSymbol(step.completionStatus),
                    step.averageSimilarity.ToString("F4", inv),
                    step.leftAverageSimilarity.ToString("F4", inv),
                    step.rightAverageSimilarity.ToString("F4", inv),
                    step.similarityStdDev.ToString("F4", inv),
                    step.minSimilarity.ToString("F4", inv),
                    step.maxSimilarity.ToString("F4", inv),
                    step.finalScore.ToString("F1", inv),
                    Escape(step.grade),
                    step.limitViolationCount.ToString(inv),
                    step.totalTimeInWarning.ToString("F2", inv),
                    step.totalTimeExceeded.ToString("F2", inv),
                    step.peakExceededRatio.ToString("F4", inv),
                    step.totalTime.ToString("F1", inv),
                    $"{step.completedSubSteps}/{step.totalSubSteps}/{step.skippedSubSteps}"
                );
            }
        }
    }

    private void WriteTimelineHeader(StringBuilder sb)
    {
        sb.AppendLine(string.Join(",",
            "SessionId",
            "Phase",
            "Step",
            "Time",
            "LeftSimilarity",
            "RightSimilarity"
        ));
    }

    private void WriteTimelineRows(StringBuilder sb, TrainingResultData data)
    {
        string sessionId = Escape(data.sessionId);

        foreach (var phase in data.phaseResults)
        {
            string phaseName = Escape(phase.phaseName);

            foreach (var step in phase.stepResults)
            {
                if (step.similarityTimeline == null || step.similarityTimeline.Count == 0)
                    continue;

                string stepName = Escape(step.stepName);

                foreach (var point in step.similarityTimeline)
                {
                    sb.AppendFormat(inv, "{0},{1},{2},{3},{4},{5}\n",
                        sessionId,
                        phaseName,
                        stepName,
                        point.time.ToString("F3", inv),
                        point.leftSimilarity.ToString("F4", inv),
                        point.rightSimilarity.ToString("F4", inv)
                    );
                }
            }
        }
    }

    // ========== 유틸리티 ==========

    private string GetSavePath(TrainingResultData data, string suffix)
    {
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string user = SanitizeFileName(data.userName);
        string scenario = SanitizeFileName(data.scenarioName);
        string sessionShort = data.sessionId.Length >= 8 ? data.sessionId.Substring(0, 8) : data.sessionId;
        string fileName = $"{timestamp}_{user}_{scenario}_{sessionShort}_{suffix}.csv";
        return Path.Combine(GetBasePath(), fileName);
    }

    private string GetBasePath()
    {
#if UNITY_EDITOR
        return Path.Combine(Application.dataPath, resultFolderName);
#else
        return Path.Combine(Application.persistentDataPath, resultFolderName);
#endif
    }

    private static void EnsureDirectory(string filePath)
    {
        string dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(",") || value.Contains("\"") || value.Contains("\n"))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "Unknown";
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}
