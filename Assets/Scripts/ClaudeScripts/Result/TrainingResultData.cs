using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 훈련 결과 데이터 저장 클래스
/// 각 단계별 진행률, 유사도, 경고 횟수 등을 저장
/// </summary>
[System.Serializable]
public class TrainingResultData
{
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
    public DateTime startTime;                     // 시작 시간
    public DateTime endTime;                       // 종료 시간
    public string selectedMode;                    // 선택된 모드 (학습/평가)
    public string selectedDifficulty;              // 선택된 난이도

    [Header("=== Phase별 결과 ===")]
    public List<PhaseResult> phaseResults;         // 전부/중부/후부 결과

    [Header("=== 종합 통계 ===")]
    public float totalTime;                        // 총 수행 시간 (초)
    public float overallSimilarity;                // 전체 평균 유사도
    public int totalWarningCount;                  // 총 경고 횟수 (제한 범위 초과)
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
            // 전부 스킵 → X
            step.completionStatus = StepCompletionStatus.Skipped;
            totalSkipCount++;
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
    public void Finalize()
    {
        endTime = DateTime.Now;
        totalTime = (float)(endTime - startTime).TotalSeconds;

        // 각 Phase 통계 계산
        float totalSimilarity = 0f;
        int phaseCount = 0;

        foreach (var phase in phaseResults)
        {
            phase.CalculateStats();
            if (phase.stepResults.Count > 0)
            {
                totalSimilarity += phase.phaseAverageSimilarity;
                phaseCount++;
            }
        }

        // 전체 평균 유사도
        overallSimilarity = phaseCount > 0 ? totalSimilarity / phaseCount : 0f;
    }

    /// <summary>
    /// 결과를 문자열로 출력
    /// </summary>
    public override string ToString()
    {
        string result = $"=== 훈련 결과 ===\n";
        result += $"수행 시간: {FormatTime(totalTime)}\n";
        result += $"전체 유사도: {overallSimilarity:P0}\n";
        result += $"경고 횟수: {totalWarningCount}회\n";
        result += $"스킵 횟수: {totalSkipCount}회\n";
        result += $"최저 유사도 구간: {lowestSimilarityStep} ({lowestSimilarity:P0})\n";
        result += $"최고 유사도 구간: {highestSimilarityStep} ({highestSimilarity:P0})\n";

        foreach (var phase in phaseResults)
        {
            result += $"\n--- {phase.phaseName} ---\n";
            foreach (var step in phase.stepResults)
            {
                string status = GetStatusSymbol(step.completionStatus);
                result += $"  {step.stepName}: {status} / {step.averageSimilarity:P0}\n";
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
}
