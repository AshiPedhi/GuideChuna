using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scoring and metrics calculation helper for ChunaPathEvaluator.
/// Pure calculations based on EvaluationSession data.
/// </summary>
public class EvaluationScoringEngine
{
    // 이전 스냅샷의 Exceeded 여부 (진입 횟수 카운트용)
    private bool wasExceeded = false;

    /// <summary>
    /// Record a metrics snapshot into the session.
    /// </summary>
    public void RecordMetricsSnapshot(
        ChunaPathEvaluator.EvaluationSession session,
        float evaluationStartTime,
        float metricsRecordInterval,
        float leftSimilarity, float rightSimilarity,
        ChunaLimitChecker limitChecker,
        Vector3 leftHandPos, Vector3 rightHandPos,
        bool leftTouching = false)
    {
        var snapshot = new ChunaPathEvaluator.EvaluationSession.MetricsSnapshot
        {
            timestamp = Time.time - evaluationStartTime,
            leftSimilarity = leftSimilarity,
            rightSimilarity = rightSimilarity,
            leftHandPosition = leftHandPos,
            rightHandPosition = rightHandPos,
            leftTouching = leftTouching
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

            bool isExceeded = leftResult.overallStatus == LimitStatus.Exceeded || rightResult.overallStatus == LimitStatus.Exceeded;
            if (isExceeded)
                session.totalTimeExceeded += metricsRecordInterval;

            // 초과 "진입" 시에만 카운트 (매 프레임이 아닌 상태 전환 시)
            if (isExceeded && !wasExceeded)
                session.limitViolationCount++;
            wasExceeded = isExceeded;

            // 최대 초과 비율 갱신
            float maxRatio = Mathf.Max(leftResult.frameRatio, rightResult.frameRatio);
            if (maxRatio > session.peakExceededRatio)
                session.peakExceededRatio = maxRatio;
        }

        session.metricsHistory.Add(snapshot);
    }

    /// <summary>
    /// 새 평가 시작 시 상태 리셋
    /// </summary>
    public void Reset()
    {
        wasExceeded = false;
    }

    /// <summary>
    /// Calculate average similarity from session metrics history.
    /// </summary>
    public void CalculateAverageSimilarity(ChunaPathEvaluator.EvaluationSession session, float leftWeight, float rightWeight, bool isBothHandsMode = false)
    {
        if (session.metricsHistory.Count == 0) return;

        int count = session.metricsHistory.Count;
        float totalLeft = 0f, totalRight = 0f;
        int leftTouchCount = 0;

        foreach (var snapshot in session.metricsHistory)
        {
            totalLeft += snapshot.leftSimilarity;
            totalRight += snapshot.rightSimilarity;
            if (snapshot.leftTouching) leftTouchCount++;

            float weightedAvg = snapshot.leftSimilarity * leftWeight + snapshot.rightSimilarity * rightWeight;
            if (weightedAvg < session.minSimilarity) session.minSimilarity = weightedAvg;
            if (weightedAvg > session.maxSimilarity) session.maxSimilarity = weightedAvg;
        }

        float avgLeft = totalLeft / count;
        float avgRight = totalRight / count;

        // 좌/우 개별 평균 유사도
        session.leftAverageSimilarity = avgLeft;
        session.rightAverageSimilarity = avgRight;

        // 보조수(왼손) 품질 = 접촉 유지 비율 × 방향(orientation 프록시: avgLeft)
        session.leftContactRatio = (float)leftTouchCount / count;
        session.supportQuality = session.leftContactRatio * avgLeft;

        if (isBothHandsMode)
        {
            // 양손 회전 step: 두 손 모두 주동 → 기존 가중 평균 유지
            session.averageSimilarity = avgLeft * leftWeight + avgRight * rightWeight;
        }
        else
        {
            // 단손 step: 점수 비중 주45/보주15 → 유사도(60점)에서 주동수 45 + 보조수 15 환산
            // averageSimilarity는 0~1로 정규화 (CalculateFinalScore에서 ×60). (avgRight×45 + supportQuality×15)/60
            session.averageSimilarity = (avgRight * 45f + session.supportQuality * 15f) / 60f;
        }

        // 유사도 표준편차 — 단손은 주동수(오른손) 안정성, 양손은 가중 평균 안정성
        float stdMean = isBothHandsMode ? session.averageSimilarity : avgRight;
        float sumSquaredDiff = 0f;
        foreach (var snapshot in session.metricsHistory)
        {
            float sampleVal = isBothHandsMode
                ? snapshot.leftSimilarity * leftWeight + snapshot.rightSimilarity * rightWeight
                : snapshot.rightSimilarity;
            float diff = sampleVal - stdMean;
            sumSquaredDiff += diff * diff;
        }
        session.similarityStdDev = Mathf.Sqrt(sumSquaredDiff / count);
    }

    /// <summary>
    /// Calculate the final score for the session.
    /// </summary>
    public void CalculateFinalScore(ChunaPathEvaluator.EvaluationSession session)
    {
        // Similarity-based score (60%) — 단손 step은 averageSimilarity 안에 주45/보주15 환산이 이미 포함됨
        float similarityScore = session.averageSimilarity * 60f;

        // 40% 블록: 등척성운동은 가동범위 개념이 없으므로 '홀드 완수도'로, 그 외는 리밋 준수로 계산
        float limitScore;
        if (session.isIsometric)
        {
            limitScore = 40f * Mathf.Clamp01(session.holdQuality);
        }
        else
        {
            // Limit compliance score (40%)
            limitScore = 40f;
            limitScore -= session.limitViolationCount * 2f;
            limitScore -= session.totalTimeInWarning * 0.5f;
            limitScore -= session.totalTimeInDanger * 1f;
            limitScore -= session.totalTimeExceeded * 3f;
            limitScore = Mathf.Max(0f, limitScore);
        }

        float score = similarityScore + limitScore;
        score = Mathf.Clamp(score, 0f, 100f);

        session.finalScore = score;
        session.grade = GetGradeFromScore(score);
        session.feedback = GenerateFeedback(session);
    }

    public static string GetGradeFromScore(float score)
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

        if (feedbacks.Count == 0)
            feedbacks.Add("잘 수행하셨습니다!");

        return string.Join("\n", feedbacks);
    }
}
