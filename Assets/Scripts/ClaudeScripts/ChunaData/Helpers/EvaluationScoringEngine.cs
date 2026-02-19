using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scoring and metrics calculation helper for ChunaPathEvaluator.
/// Pure calculations based on EvaluationSession data.
/// </summary>
public class EvaluationScoringEngine
{
    /// <summary>
    /// Record a metrics snapshot into the session.
    /// </summary>
    public void RecordMetricsSnapshot(
        ChunaPathEvaluator.EvaluationSession session,
        float evaluationStartTime,
        float metricsRecordInterval,
        float leftSimilarity, float rightSimilarity,
        ChunaLimitChecker limitChecker,
        Vector3 leftHandPos, Vector3 rightHandPos)
    {
        var snapshot = new ChunaPathEvaluator.EvaluationSession.MetricsSnapshot
        {
            timestamp = Time.time - evaluationStartTime,
            leftSimilarity = leftSimilarity,
            rightSimilarity = rightSimilarity,
            leftHandPosition = leftHandPos,
            rightHandPosition = rightHandPos
        };

        if (limitChecker != null)
        {
            var leftResult = limitChecker.GetLeftHandResult();
            var rightResult = limitChecker.GetRightHandResult();

            snapshot.leftLimitStatus = leftResult.overallStatus;
            snapshot.rightLimitStatus = rightResult.overallStatus;
            snapshot.leftLimitRatio = leftResult.frameRatio;
            snapshot.rightLimitRatio = rightResult.frameRatio;

            if (leftResult.overallStatus == LimitStatus.Warning || rightResult.overallStatus == LimitStatus.Warning)
                session.totalTimeInWarning += metricsRecordInterval;
            if (leftResult.overallStatus == LimitStatus.Danger || rightResult.overallStatus == LimitStatus.Danger)
                session.totalTimeInDanger += metricsRecordInterval;
            if (leftResult.overallStatus == LimitStatus.Exceeded || rightResult.overallStatus == LimitStatus.Exceeded)
                session.totalTimeExceeded += metricsRecordInterval;
        }

        session.metricsHistory.Add(snapshot);
    }

    /// <summary>
    /// Calculate average similarity from session metrics history.
    /// </summary>
    public void CalculateAverageSimilarity(ChunaPathEvaluator.EvaluationSession session, float leftWeight, float rightWeight)
    {
        if (session.metricsHistory.Count == 0) return;

        float totalLeft = 0f, totalRight = 0f;
        foreach (var snapshot in session.metricsHistory)
        {
            totalLeft += snapshot.leftSimilarity;
            totalRight += snapshot.rightSimilarity;

            float weightedAvg = snapshot.leftSimilarity * leftWeight + snapshot.rightSimilarity * rightWeight;
            if (weightedAvg < session.minSimilarity) session.minSimilarity = weightedAvg;
            if (weightedAvg > session.maxSimilarity) session.maxSimilarity = weightedAvg;
        }

        float avgLeft = totalLeft / session.metricsHistory.Count;
        float avgRight = totalRight / session.metricsHistory.Count;
        session.averageSimilarity = avgLeft * leftWeight + avgRight * rightWeight;
    }

    /// <summary>
    /// Calculate the final score for the session.
    /// </summary>
    public void CalculateFinalScore(ChunaPathEvaluator.EvaluationSession session)
    {
        // Similarity-based score (40%)
        float similarityScore = session.averageSimilarity * 40f;

        // Checkpoint pass rate (30%)
        float checkpointRate = session.totalCheckpoints > 0
            ? (float)session.touchedCheckpoints / session.totalCheckpoints
            : 1f;
        float checkpointScore = checkpointRate * 30f;

        // Limit compliance score (30%)
        float limitScore = 30f;
        limitScore -= session.limitViolationCount * 2f;
        limitScore -= session.totalTimeInWarning * 0.5f;
        limitScore -= session.totalTimeInDanger * 1f;
        limitScore -= session.totalTimeExceeded * 3f;
        limitScore = Mathf.Max(0f, limitScore);

        float score = similarityScore + checkpointScore + limitScore;
        score = Mathf.Clamp(score, 0f, 100f);

        session.finalScore = score;
        session.grade = GetGradeFromScore(score);
        session.feedback = GenerateFeedback(session);
    }

    private string GetGradeFromScore(float score)
    {
        if (score >= 95f) return "S";
        if (score >= 90f) return "A+";
        if (score >= 85f) return "A";
        if (score >= 80f) return "B+";
        if (score >= 75f) return "B";
        if (score >= 70f) return "C+";
        if (score >= 65f) return "C";
        if (score >= 60f) return "D";
        return "F";
    }

    private string GenerateFeedback(ChunaPathEvaluator.EvaluationSession session)
    {
        List<string> feedbacks = new List<string>();

        if (session.averageSimilarity < 0.5f)
            feedbacks.Add("손 모양을 가이드와 더 비슷하게 유지하세요");

        if (session.limitViolationCount > 3)
            feedbacks.Add("적정 범위를 벗어난 횟수가 많습니다. 부드럽게 움직이세요");

        if (session.totalTimeExceeded > 2f)
            feedbacks.Add("위험 범위에서 너무 오래 머물렀습니다");

        float checkpointRate = session.totalCheckpoints > 0
            ? (float)session.touchedCheckpoints / session.totalCheckpoints
            : 1f;
        if (checkpointRate < 0.7f)
            feedbacks.Add("경로를 더 정확하게 따라가세요");

        if (feedbacks.Count == 0)
            feedbacks.Add("잘 수행하셨습니다!");

        return string.Join("\n", feedbacks);
    }
}
