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
            string romPath = SaveRomCSV(data);   // 측정값이 없는 술기면 빈 문자열

            ChunaLogger.Log($"<color=green>[ResultExporter] 결과 저장 완료</color>\n  요약: {summaryPath}\n  시계열: {timelinePath}"
                            + (string.IsNullOrEmpty(romPath) ? "" : $"\n  ROM: {romPath}"));
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
    /// 경추 ROM 측정 각도를 CSV로 저장한다. 측정값이 없으면 파일을 만들지 않고 빈 문자열을 준다.
    ///
    /// ★<b>2026-09-01 신설.</b> 그전까지 측정 각도는 <see cref="TrainingResultData.romMeasurements"/>에
    ///   담겨 결과 <b>화면에만</b> 나가고 어디에도 저장되지 않았다. 09-01 실기 테스트 두 판의
    ///   각도가 그래서 통째로 사라졌다(logcat도 이미 밀린 뒤였다).
    ///   ★실측 모드는 그 숫자가 결과물이다. 요약 CSV에는 점수·유사도가 전부 0으로만 남는데,
    ///   이 술기는 판정 경로가 PassiveStretch라 원래 0이라서 요약만으로는 아무것도 알 수 없다.
    ///
    /// 요약 CSV에 열을 붙이지 않고 파일을 따로 뺐다 — 요약은 <b>단계마다 한 줄</b>인데
    /// 측정값은 <b>방향마다 한 줄</b>이라 축이 다르다. 6방향×4값을 열로 펴면 24열이 붙는다.
    /// </summary>
    public string SaveRomCSV(TrainingResultData data)
    {
        if (data == null || data.romMeasurements == null || data.romMeasurements.Count == 0)
            return "";

        string path = GetSavePath(data, "rom");
        EnsureDirectory(path);

        var sb = new StringBuilder();

        // ★용어는 2026-08-27 회의 결정을 따른다 — 압박→수동, 최대→참고치,
        //   차이값 = 참고치 − 수동(부족각)이다. 능동과 수동의 차가 아니다.
        //   면 이름은 화면 표에서는 빼기로 했지만, 데이터 파일에는 남긴다(나중에 묶어 보려면 필요하다).
        // ★뒤 4열은 진단 계수기다 — 각도가 아니라 "왜 오래 걸렸는지"다.
        //   계수기를 로그로만 남기면 09-01처럼 logcat이 밀려 통째로 잃는다. 파일에 같이 싣는다.
        // ★뒤 2열은 <b>평가</b> 계수기다(2026-09-02). 진단 계수기와 성격이 다르다 —
        //   기계가 잘 읽었나가 아니라 사람이 절차를 밟았나이고, 감점은 이쪽만 본다.
        sb.AppendLine("SessionId,UserName,Scenario,StartTime,Plane,Direction,Reference,Active,Passive,Deficit,"
                    + "HoldResets,RejectedFrames,Relocks,LostSeconds,PassiveSkipped,GripReleases,"
                    // ★멈춤 진단 3열(2026-09-09) — 위 계수기가 전부 0인데도 안 되던 경우를 가른다.
                    + "StaleSeconds,SlipMaxPercent,NeutralRefreshes");

        foreach (var m in data.romMeasurements)
        {
            if (m == null) continue;

            sb.Append(Escape(data.sessionId)).Append(',');
            sb.Append(Escape(data.userName)).Append(',');
            sb.Append(Escape(data.scenarioName)).Append(',');
            sb.Append(Escape(data.startTime.ToString("yyyy-MM-dd HH:mm:ss", inv))).Append(',');
            sb.Append(Escape(m.planeName)).Append(',');
            sb.Append(Escape(m.directionName)).Append(',');
            sb.Append(m.maxAngle.ToString("F1", inv)).Append(',');
            sb.Append(m.activeAngle.ToString("F1", inv)).Append(',');
            sb.Append(m.passiveAngle.ToString("F1", inv)).Append(',');
            sb.Append(m.DeficitAngle.ToString("F1", inv)).Append(',');
            sb.Append(m.holdResets.ToString(inv)).Append(',');
            sb.Append(m.rejectedFrames.ToString(inv)).Append(',');
            sb.Append(m.relocks.ToString(inv)).Append(',');
            sb.Append(m.lostSeconds.ToString("F1", inv)).Append(',');
            sb.Append(m.passiveSkipped ? "1" : "0").Append(',');
            sb.Append(m.gripReleases.ToString(inv)).Append(',');
            sb.Append(m.staleSeconds.ToString("F1", inv)).Append(',');
            sb.Append(m.slipMaxPercent.ToString("F0", inv)).Append(',');
            sb.Append(m.neutralRefreshes.ToString(inv));
            sb.AppendLine();
        }

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
            "Skipped",
            "IsCompleted"
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
        string isCompleted = data.isCompleted ? "1" : "0";

        foreach (var phase in data.phaseResults)
        {
            string phaseName = Escape(phase.phaseName);

            foreach (var step in phase.stepResults)
            {
                // 컬럼 순서는 WriteSummaryHeader와 1:1로 일치 (SubSteps/Completed/Skipped는 각각 별도 컬럼)
                sb.AppendFormat(inv,
                    "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21},{22},{23},{24},{25},{26},{27},{28},{29},{30},{31},{32},{33},{34},{35},{36}\n",
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
                    step.totalTime.ToString("F1", inv),     // StepTime
                    step.totalSubSteps.ToString(inv),        // SubSteps (전체)
                    step.completedSubSteps.ToString(inv),    // Completed
                    step.skippedSubSteps.ToString(inv),      // Skipped
                    isCompleted                              // IsCompleted (0=중도종료/미완료, 1=정상완주)
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
