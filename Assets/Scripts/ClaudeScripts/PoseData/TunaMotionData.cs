using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 추나 시술 동작 데이터 구조
/// 구간별 평가, 안전 범위, 체크포인트 정의
/// </summary>

namespace TunaEvaluation
{
    /// <summary>
    /// 추나 시술 동작 구간
    /// </summary>
    [System.Serializable]
    public class TunaMotionSegment
    {
        [Header("=== 구간 정보 ===")]
        [Tooltip("구간 이름 (예: 경추 회전)")]
        public string segmentName = "구간 1";

        [Tooltip("시작 프레임 인덱스")]
        public int startFrame = 0;

        [Tooltip("종료 프레임 인덱스")]
        public int endFrame = 10;

        [Header("=== 안전 범위 ===")]
        [Tooltip("안전 범위 체크 활성화")]
        public bool checkSafetyLimits = true;

        [Tooltip("왼손 최대 회전 각도 (제한장벽)")]
        public float leftHandMaxRotation = 45f;

        [Tooltip("오른손 최대 회전 각도 (제한장벽)")]
        public float rightHandMaxRotation = 45f;

        [Tooltip("왼손 최대 위치 이동 거리 (m)")]
        public float leftHandMaxDistance = 0.3f;

        [Tooltip("오른손 최대 위치 이동 거리 (m)")]
        public float rightHandMaxDistance = 0.3f;

        [Header("=== 경로 검증 ===")]
        [Tooltip("경로 준수 체크 활성화")]
        public bool requirePathFollowing = true;

        [Tooltip("경로 허용 오차 (m)")]
        public float pathTolerance = 0.05f; // 5cm

        [Tooltip("경로 체크 프레임 간격 (1 = 모든 프레임)")]
        public int pathCheckInterval = 1;

        [Header("=== 체크포인트 ===")]
        [Tooltip("이 구간이 체크포인트인가?")]
        public bool isCheckpoint = false;

        [Tooltip("체크포인트 목표 유지 시간 (초)")]
        public float requiredHoldTime = 2f;

        [Tooltip("체크포인트 유사도 임계값 (0~1)")]
        public float checkpointSimilarityThreshold = 0.8f;

        [Header("=== 점수 가중치 ===")]
        [Tooltip("이 구간의 중요도 (1.0 = 기본, 2.0 = 2배 중요)")]
        public float segmentWeight = 1f;
    }

    /// <summary>
    /// 안전 위반 기록
    /// </summary>
    [System.Serializable]
    public class SafetyViolation
    {
        public int frameIndex;
        public string handType; // "Left" or "Right"
        public string violationType; // "RotationExceeded", "DistanceExceeded"
        public float actualValue;
        public float limitValue;
        public float timestamp;

        public override string ToString()
        {
            return $"프레임 {frameIndex}: {handType} {violationType} - {actualValue:F1} (한계: {limitValue:F1})";
        }
    }

    /// <summary>
    /// 경로 이탈 기록
    /// </summary>
    [System.Serializable]
    public class PathDeviation
    {
        public int frameIndex;
        public string handType;
        public float deviation; // 미터
        public float timestamp;

        public override string ToString()
        {
            return $"프레임 {frameIndex}: {handType} 경로 이탈 {deviation * 100:F1}cm";
        }
    }

    /// <summary>
    /// 체크포인트 평가 결과
    /// </summary>
    [System.Serializable]
    public class CheckpointResult
    {
        public string segmentName;
        public int frameIndex;
        public float similarity;
        public float holdTime;
        public bool passed;
        public float scoreEarned;

        public override string ToString()
        {
            string status = passed ? "통과" : "실패";
            return $"{segmentName}: {status} (유사도 {similarity * 100:F0}%, 유지 {holdTime:F1}초) - {scoreEarned}점";
        }
    }

    /// <summary>
    /// 전체 평가 점수
    /// </summary>
    [System.Serializable]
    public class EvaluationScore
    {
        [Header("=== 카테고리별 점수 ===")]
        public float pathComplianceScore = 0f;     // 경로 준수도 (40점)
        public float safetyScore = 0f;             // 안전성 (30점)
        public float accuracyScore = 0f;           // 정확도 (20점)
        public float stabilityScore = 0f;          // 안정성 (10점)

        [Header("=== 세부 내역 ===")]
        public int totalFrames = 0;
        public int framesOnPath = 0;
        public int safetyViolations = 0;
        public int checkpointsPassed = 0;
        public int totalCheckpoints = 0;
        public float totalHoldTime = 0f;
        public float requiredHoldTime = 0f;

        [Header("=== 최대 점수 ===")]
        public float maxPathScore = 40f;
        public float maxSafetyScore = 30f;
        public float maxAccuracyScore = 20f;
        public float maxStabilityScore = 10f;

        /// <summary>
        /// 총점 계산
        /// </summary>
        public float TotalScore
        {
            get { return pathComplianceScore + safetyScore + accuracyScore + stabilityScore; }
        }

        /// <summary>
        /// 최대 총점
        /// </summary>
        public float MaxTotalScore
        {
            get { return maxPathScore + maxSafetyScore + maxAccuracyScore + maxStabilityScore; }
        }

        /// <summary>
        /// 백분율 점수
        /// </summary>
        public float Percentage
        {
            get { return MaxTotalScore > 0 ? (TotalScore / MaxTotalScore) * 100f : 0f; }
        }

        /// <summary>
        /// 등급 계산
        /// </summary>
        public string Grade
        {
            get
            {
                float percent = Percentage;
                if (percent >= 90f) return "A+";
                if (percent >= 85f) return "A";
                if (percent >= 80f) return "B+";
                if (percent >= 75f) return "B";
                if (percent >= 70f) return "C+";
                if (percent >= 65f) return "C";
                if (percent >= 60f) return "D";
                return "F";
            }
        }

        public override string ToString()
        {
            return $"총점: {TotalScore:F1}/{MaxTotalScore} ({Percentage:F1}%) - {Grade}등급\n" +
                   $"경로 준수: {pathComplianceScore:F1}/{maxPathScore}\n" +
                   $"안전성: {safetyScore:F1}/{maxSafetyScore}\n" +
                   $"정확도: {accuracyScore:F1}/{maxAccuracyScore}\n" +
                   $"안정성: {stabilityScore:F1}/{maxStabilityScore}";
        }
    }

    /// <summary>
    /// 전체 평가 결과
    /// </summary>
    [System.Serializable]
    public class EvaluationResult
    {
        public EvaluationScore score = new EvaluationScore();
        public List<SafetyViolation> safetyViolations = new List<SafetyViolation>();
        public List<PathDeviation> pathDeviations = new List<PathDeviation>();
        public List<CheckpointResult> checkpointResults = new List<CheckpointResult>();

        public float startTime;
        public float endTime;
        public float totalDuration;

        /// <summary>
        /// 상세 리포트 생성
        /// </summary>
        public string GenerateReport()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            sb.AppendLine("========== 추나 시술 평가 결과 ==========");
            sb.AppendLine();
            sb.AppendLine(score.ToString());
            sb.AppendLine();
            sb.AppendLine($"수행 시간: {totalDuration:F1}초");
            sb.AppendLine();

            // 안전 위반 내역
            if (safetyViolations.Count > 0)
            {
                sb.AppendLine($"[안전 위반 내역: {safetyViolations.Count}건]");
                foreach (var violation in safetyViolations)
                {
                    sb.AppendLine($"  - {violation}");
                }
                sb.AppendLine();
            }

            // 경로 이탈 내역 (주요 건만)
            var majorDeviations = pathDeviations.FindAll(d => d.deviation > 0.03f); // 3cm 이상
            if (majorDeviations.Count > 0)
            {
                sb.AppendLine($"[주요 경로 이탈: {majorDeviations.Count}건]");
                foreach (var deviation in majorDeviations)
                {
                    sb.AppendLine($"  - {deviation}");
                }
                sb.AppendLine();
            }

            // 체크포인트 결과
            if (checkpointResults.Count > 0)
            {
                sb.AppendLine("[체크포인트 결과]");
                foreach (var checkpoint in checkpointResults)
                {
                    sb.AppendLine($"  - {checkpoint}");
                }
                sb.AppendLine();
            }

            sb.AppendLine("=======================================");

            return sb.ToString();
        }
    }
}
