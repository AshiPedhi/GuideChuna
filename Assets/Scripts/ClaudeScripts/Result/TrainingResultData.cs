using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 훈련 결과 데이터 저장 클래스
/// 각 단계별 진행률, 유사도, 경고 횟수 등을 저장
/// </summary>
[System.Serializable]
public class TrainingResultData
{
    // ========== 시계열 데이터 ==========

    /// <summary>
    /// 시간축 유사도 데이터 포인트 (웹뷰 그래프용)
    /// </summary>
    [System.Serializable]
    public class SimilarityTimePoint
    {
        public float time;              // 경과 시간 (초)
        public float leftSimilarity;    // 왼손 유사도
        public float rightSimilarity;   // 오른손 유사도
    }

    // ========== 단계별 결과 ==========

    /// <summary>
    /// 단일 Step 결과
    /// </summary>
    [System.Serializable]
    public class StepResult
    {
        public string stepName;                    // Step 이름
        public StepCompletionStatus completionStatus;  // 완료 상태 (O, △, X)
        public float averageSimilarity;            // 평균 유사도
        public int totalSubSteps;                  // 전체 SubStep 수
        public int completedSubSteps;              // 완료된 SubStep 수
        public int skippedSubSteps;                // 스킵된 SubStep 수
        public float totalTime;                    // 소요 시간
        public int warningCount;                   // 경고 횟수 (제한 범위 초과)

        // 스코어링 결과 (EvaluationSession에서 전달)
        public float finalScore;                   // 최종 점수 (0~100)
        public string grade;                       // 등급 (S/A+/A/B+/B/C+/C/D/F)
        public int limitViolationCount;            // 리밋 초과 진입 횟수
        public float totalTimeInWarning;           // 경고 상태 누적 시간
        public float totalTimeExceeded;            // 초과 상태 누적 시간

        // 좌/우 개별 유사도
        public float leftAverageSimilarity;        // 왼손 평균 유사도
        public float rightAverageSimilarity;       // 오른손 평균 유사도

        // 유사도 안정성
        public float similarityStdDev;             // 유사도 표준편차 (낮을수록 안정적)
        public float minSimilarity;                // 최저 유사도
        public float maxSimilarity;                // 최고 유사도

        // 안전성
        public float peakExceededRatio;            // 최대 초과 비율 (얼마나 크게 넘었는지)

        // 시계열 데이터 (웹뷰 그래프용)
        public List<SimilarityTimePoint> similarityTimeline;  // 시간축 유사도 변화

        public StepResult(string name)
        {
            stepName = name;
            completionStatus = StepCompletionStatus.None;
            averageSimilarity = 0f;
            totalSubSteps = 0;
            completedSubSteps = 0;
            skippedSubSteps = 0;
            totalTime = 0f;
            warningCount = 0;
            finalScore = 0f;
            grade = "";
            limitViolationCount = 0;
            totalTimeInWarning = 0f;
            totalTimeExceeded = 0f;
            leftAverageSimilarity = 0f;
            rightAverageSimilarity = 0f;
            similarityStdDev = 0f;
            minSimilarity = 1f;
            maxSimilarity = 0f;
            peakExceededRatio = 0f;
            similarityTimeline = new List<SimilarityTimePoint>();
        }
    }

    /// <summary>
    /// 단계 완료 상태
    /// </summary>
    public enum StepCompletionStatus
    {
        None,       // 미진행
        Complete,   // O - 전체 통과
        Partial,    // △ - 일부 통과
        Skipped     // X - 전체 스킵
    }

    /// <summary>
    /// Phase별 결과 (전부/중부/후부)
    /// </summary>
    [System.Serializable]
    public class PhaseResult
    {
        public string phaseName;                   // Phase 이름 (전부, 중부, 후부)
        public List<StepResult> stepResults;       // 각 Step 결과
        public float phaseAverageSimilarity;       // Phase 평균 유사도
        public float phaseTotalTime;               // Phase 총 소요 시간
        public int phaseWarningCount;              // Phase 총 경고 횟수

        public PhaseResult(string name)
        {
            phaseName = name;
            stepResults = new List<StepResult>();
            phaseAverageSimilarity = 0f;
            phaseTotalTime = 0f;
            phaseWarningCount = 0;
        }

        /// <summary>
        /// Phase 통계 계산
        /// </summary>
        public void CalculateStats()
        {
            if (stepResults.Count == 0) return;

            float totalSimilarity = 0f;
            float totalTime = 0f;
            int totalWarnings = 0;

            foreach (var step in stepResults)
            {
                totalSimilarity += step.averageSimilarity;
                totalTime += step.totalTime;
                totalWarnings += step.warningCount;
            }

            phaseAverageSimilarity = totalSimilarity / stepResults.Count;
            phaseTotalTime = totalTime;
            phaseWarningCount = totalWarnings;
        }
    }

    // ========== 종합 결과 ==========

    [Header("=== 기본 정보 ===")]
    public string sessionId;                       // 세션 ID
    public string userName;                        // 사용자 이름
    public int userId;                             // 사용자 ID
    public string scenarioName;                    // 시나리오 이름 (예: 상부승모근)
    public DateTime startTime;                     // 시작 시간
    public DateTime endTime;                       // 종료 시간
    public string selectedMode;                    // 선택된 모드 (학습/평가)
    public string selectedDifficulty;              // 선택된 난이도
    public bool isOfficialEvaluation;              // 공식 평가 여부 (true면 공식 점수로 기록)
    public bool isPreEvaluation;                   // 모의평가 여부 (상급자 모드)
    public int attemptNumber;                      // 시도 횟수 (같은 시나리오 몇 회차)
    public bool isCompleted = true;                // 정상 완주 여부 (false = 중도 종료/미완료, 정식 점수와 분리)

    [Header("=== Phase별 결과 ===")]
    public List<PhaseResult> phaseResults;         // 전부/중부/후부 결과

    [Header("=== 종합 통계 ===")]
    public float totalTime;                        // 총 수행 시간 (초)
    public float overallSimilarity;                // 전체 평균 유사도
    public float overallScore;                     // 전체 평균 점수
    public string overallGrade;                    // 종합 등급
    public int totalWarningCount;                  // 총 경고 횟수 (제한 범위 초과)
    public int totalLimitViolations;               // 총 리밋 초과 진입 횟수
    public int totalSkipCount;                     // 총 스킵 횟수
    public string lowestSimilarityStep;            // 최저 유사도 구간
    public float lowestSimilarity;                 // 최저 유사도
    public string highestSimilarityStep;           // 최고 유사도 구간
    public float highestSimilarity;                // 최고 유사도

    // ========== 생성자 ==========

    public TrainingResultData()
    {
        sessionId = Guid.NewGuid().ToString();
        startTime = DateTime.Now;
        phaseResults = new List<PhaseResult>();
        lowestSimilarity = 1f;
        highestSimilarity = 0f;
    }

    // ========== 메서드 ==========

    /// <summary>
    /// Phase 결과 추가 또는 가져오기
    /// </summary>
    public PhaseResult GetOrCreatePhaseResult(string phaseName)
    {
        var existing = phaseResults.Find(p => p.phaseName == phaseName);
        if (existing != null) return existing;

        var newPhase = new PhaseResult(phaseName);
        phaseResults.Add(newPhase);
        return newPhase;
    }

    /// <summary>
    /// Step 결과 추가 또는 가져오기
    /// </summary>
    public StepResult GetOrCreateStepResult(string phaseName, string stepName)
    {
        var phase = GetOrCreatePhaseResult(phaseName);
        var existing = phase.stepResults.Find(s => s.stepName == stepName);
        if (existing != null) return existing;

        var newStep = new StepResult(stepName);
        phase.stepResults.Add(newStep);
        return newStep;
    }

    /// <summary>
    /// SubStep 완료 기록
    /// </summary>
    public void RecordSubStepCompletion(string phaseName, string stepName, bool completed, bool skipped, float similarity)
    {
        var step = GetOrCreateStepResult(phaseName, stepName);
        step.totalSubSteps++;

        if (completed)
            step.completedSubSteps++;
        if (skipped)
            step.skippedSubSteps++;

        // 유사도 누적 (나중에 평균 계산)
        step.averageSimilarity = (step.averageSimilarity * (step.totalSubSteps - 1) + similarity) / step.totalSubSteps;

        // 완료 상태 업데이트
        UpdateStepCompletionStatus(step);

        // 최저/최고 유사도 갱신
        if (similarity < lowestSimilarity && similarity > 0)
        {
            lowestSimilarity = similarity;
            lowestSimilarityStep = stepName;
        }
        if (similarity > highestSimilarity)
        {
            highestSimilarity = similarity;
            highestSimilarityStep = stepName;
        }
    }

    /// <summary>
    /// 경고 횟수 증가
    /// </summary>
    public void RecordWarning(string phaseName, string stepName)
    {
        var step = GetOrCreateStepResult(phaseName, stepName);
        step.warningCount++;
        totalWarningCount++;
    }

    /// <summary>
    /// Step 완료 상태 업데이트
    /// </summary>
    private void UpdateStepCompletionStatus(StepResult step)
    {
        if (step.totalSubSteps == 0)
        {
            step.completionStatus = StepCompletionStatus.None;
        }
        else if (step.skippedSubSteps == step.totalSubSteps)
        {
            // 전부 스킵 → X (상태 변경 시에만 카운트)
            if (step.completionStatus != StepCompletionStatus.Skipped)
                totalSkipCount++;
            step.completionStatus = StepCompletionStatus.Skipped;
        }
        else if (step.completedSubSteps == step.totalSubSteps)
        {
            // 전부 완료 → O
            step.completionStatus = StepCompletionStatus.Complete;
        }
        else
        {
            // 일부 완료 → △
            step.completionStatus = StepCompletionStatus.Partial;
        }
    }

    /// <summary>
    /// 종료 및 최종 통계 계산
    /// </summary>
    public void FinalizeResult()
    {
        endTime = DateTime.Now;
        totalTime = (float)(endTime - startTime).TotalSeconds;

        // 각 Phase 통계 계산
        float totalSimilarity = 0f;
        float totalScore = 0f;
        int scoredStepCount = 0;
        int phaseCount = 0;

        foreach (var phase in phaseResults)
        {
            phase.CalculateStats();
            if (phase.stepResults.Count > 0)
            {
                totalSimilarity += phase.phaseAverageSimilarity;
                phaseCount++;

                foreach (var step in phase.stepResults)
                {
                    if (step.finalScore > 0f)
                    {
                        totalScore += step.finalScore;
                        totalLimitViolations += step.limitViolationCount;
                        scoredStepCount++;
                    }
                }
            }
        }

        // 전체 평균 유사도
        overallSimilarity = phaseCount > 0 ? totalSimilarity / phaseCount : 0f;

        // 전체 평균 점수 및 등급
        overallScore = scoredStepCount > 0 ? totalScore / scoredStepCount : 0f;
        overallGrade = EvaluationScoringEngine.GetGradeFromScore(overallScore);
    }

    /// <summary>
    /// 결과를 문자열로 출력
    /// </summary>
    public override string ToString()
    {
        string result = $"=== 훈련 결과 ===\n";
        result += $"사용자: {userName} (ID: {userId})\n";
        result += $"시나리오: {scenarioName}\n";
        result += $"수행 시간: {FormatTime(totalTime)}\n";
        result += $"전체 점수: {overallScore:F0}점 ({overallGrade})\n";
        result += $"전체 유사도: {overallSimilarity:P0}\n";
        result += $"경고 횟수: {totalWarningCount}회\n";
        result += $"리밋 초과: {totalLimitViolations}회\n";
        result += $"스킵 횟수: {totalSkipCount}회\n";
        result += $"최저 유사도 구간: {lowestSimilarityStep} ({lowestSimilarity:P0})\n";
        result += $"최고 유사도 구간: {highestSimilarityStep} ({highestSimilarity:P0})\n";

        foreach (var phase in phaseResults)
        {
            result += $"\n--- {phase.phaseName} ---\n";
            foreach (var step in phase.stepResults)
            {
                string status = GetStatusSymbol(step.completionStatus);
                result += $"  {step.stepName}: {status} / {step.averageSimilarity:P0} / {step.finalScore:F0}점({step.grade})\n";
            }
        }

        return result;
    }

    /// <summary>
    /// 시간 포맷팅
    /// </summary>
    public static string FormatTime(float seconds)
    {
        int minutes = Mathf.FloorToInt(seconds / 60f);
        int secs = Mathf.FloorToInt(seconds % 60f);
        return $"{minutes}:{secs:D2}";
    }

    /// <summary>
    /// 상태 심볼 반환
    /// </summary>
    public static string GetStatusSymbol(StepCompletionStatus status)
    {
        switch (status)
        {
            case StepCompletionStatus.Complete: return "O";
            case StepCompletionStatus.Partial: return "△";
            case StepCompletionStatus.Skipped: return "X";
            default: return "-";
        }
    }

    /// <summary>
    /// 평가모드(공식) 결과를 step당 2줄 컴팩트 포맷으로 직렬화.
    /// 서버 전송(learnLevel2)과 임시 캔버스 결과 표시가 동일한 문자열을 공유 → 이중 관리 없음.
    /// 5 step + 종합이 한 화면에 들어가도록 압축 (스크롤 불요).
    /// </summary>
    public static string BuildSummaryText(TrainingResultData data)
    {
        if (data == null) return "";

        var sb = new StringBuilder();

        // 중도 종료(미완료)는 정식 점수가 아님을 맨 위에 명시
        if (!data.isCompleted)
            sb.AppendLine("※ 미완료(중도 종료) — 정식 점수 아님");

        if (data.phaseResults != null)
        {
            bool firstStep = true;
            foreach (var phase in data.phaseResults)
            {
                if (phase?.stepResults == null) continue;

                foreach (var step in phase.stepResults)
                {
                    // 가이드 step은 평가 데이터 없음 → 스킵
                    if (step == null || step.totalSubSteps == 0) continue;

                    if (!firstStep) sb.AppendLine();
                    firstStep = false;

                    string grade = string.IsNullOrEmpty(step.grade) ? "-" : step.grade;

                    // step별 상태 심볼(O/△/X)은 제거 — 점수로 충분, 스킵 개수는 종합에 집계
                    sb.AppendLine($"■ {phase.phaseName} {step.stepName}  {step.finalScore:F0}점 ({grade}) · 유사도 {step.averageSimilarity:P0} · 위험 범위 초과 {step.limitViolationCount}");
                    sb.AppendLine($"   좌 {step.leftAverageSimilarity:P0} / 우 {step.rightAverageSimilarity:P0} · 최저 {step.minSimilarity:P0} / 최고 {step.maxSimilarity:P0}");
                }
            }
        }

        sb.AppendLine();
        sb.Append($"[ 종합 ] {data.overallScore:F0}점 ({(string.IsNullOrEmpty(data.overallGrade) ? "-" : data.overallGrade)}) · 유사도 {data.overallSimilarity:P0} · 위험 범위 초과 {data.totalLimitViolations}회 · 스킵 {data.totalSkipCount}개 · {FormatTime(data.totalTime)}");

        return sb.ToString();
    }

    /// <summary>
    /// 실습모드용 종합 요약. phase별 한 줄 분석 + 종합 통계 + 잘한·더 연습할 단계.
    /// 평가모드는 BuildSummaryText(step별 상세)를 쓰고, 실습모드는 이걸 사용.
    /// </summary>
    public static string BuildPracticeSummaryText(TrainingResultData data)
    {
        if (data == null) return "";

        var sb = new StringBuilder();

        string scenario = string.IsNullOrEmpty(data.scenarioName) ? "연습" : $"{data.scenarioName} 연습";
        sb.AppendLine(data.isCompleted ? $"{scenario} 완료!" : $"{scenario} 중도 종료 (미완료)");

        // Phase별 한 줄 요약 — 이름 있는 phase 중 작업 step이 있는 것만
        if (data.phaseResults != null)
        {
            bool firstPhase = true;
            foreach (var phase in data.phaseResults)
            {
                if (phase?.stepResults == null || phase.stepResults.Count == 0) continue;
                if (string.IsNullOrEmpty(phase.phaseName)) continue;

                float totalScore = 0f;
                int scoredCount = 0;
                int phaseLimit = 0;
                foreach (var s in phase.stepResults)
                {
                    if (s == null || s.totalSubSteps == 0) continue;
                    totalScore += s.finalScore;
                    scoredCount++;
                    phaseLimit += s.limitViolationCount;
                }
                if (scoredCount == 0) continue;

                float phaseScore = totalScore / scoredCount;
                string phaseGrade = EvaluationScoringEngine.GetGradeFromScore(phaseScore);

                if (firstPhase) { sb.AppendLine(); firstPhase = false; }
                sb.AppendLine($"[{phase.phaseName}] {phaseScore:F0}점 ({phaseGrade}) · 유사도 {phase.phaseAverageSimilarity:P0} · 위험 범위 초과 {phaseLimit}회");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"종합 점수 {data.overallScore:F0}점 ({(string.IsNullOrEmpty(data.overallGrade) ? "-" : data.overallGrade)})");
        sb.AppendLine($"평균 유사도 {data.overallSimilarity:P0}");
        sb.AppendLine($"위험 범위 초과 {data.totalLimitViolations}회 · 스킵 {data.totalSkipCount}개");
        sb.AppendLine($"수행 시간 {FormatTime(data.totalTime)}");

        if (!string.IsNullOrEmpty(data.highestSimilarityStep) || !string.IsNullOrEmpty(data.lowestSimilarityStep))
            sb.AppendLine();

        if (!string.IsNullOrEmpty(data.highestSimilarityStep))
            sb.AppendLine($"잘한 단계: {data.highestSimilarityStep} ({data.highestSimilarity:P0})");
        if (!string.IsNullOrEmpty(data.lowestSimilarityStep))
            sb.Append($"더 연습할 단계: {data.lowestSimilarityStep} ({data.lowestSimilarity:P0})");

        return sb.ToString().TrimEnd();
    }
}
