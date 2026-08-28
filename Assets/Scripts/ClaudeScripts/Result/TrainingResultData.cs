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

        // ★두개골 술기 전용 지표. 이 술기는 손 포즈 유사도·각도 리밋을 쓰지 않으므로
        //   위의 similarity/limit 항목이 전부 0으로 남는다. 대신 '자세 성립·유지'를 기록한다.
        //   null이면 두개골 단계가 아니다(기존 지표를 그대로 보면 된다).
        public CranialMetrics cranial;

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
            cranial = null;
        }
    }

    /// <summary>
    /// 두개골 교정 술기(OM/PM/PJ) 한 단계의 지표.
    ///
    /// 이 술기는 손 포즈 유사도(HandPose)와 각도 리밋 판정을 쓰지 않는다.
    /// (VR엔 반력이 없어 압력·저항감을 못 재고, 핸드트래킹 오차가 실제 변위와 같은 크기여서
    ///  깊이·변위 측정을 폐기했다 — useDepthJudging=false)
    /// 그래서 남길 수 있는 것은 <b>자세를 정확히 잡았는지 / 얼마나 안정적으로 유지했는지</b>다.
    /// </summary>
    [Serializable]
    public class CranialMetrics
    {
        // --- 단계 개요 ---
        public string label;              // 단계 구분(예: 진단1, 파지, 견착·호흡, 재평가)
        public float elapsedSeconds;      // 단계 소요 시간

        // ★어느 국면·단계의 지표인지 <b>수집을 시작한 시점에</b> 박아 둔다.
        //   기록은 다음 substep에 진입할 때 이뤄지는데, 그때는 이미 다음 단계 이름으로 바뀌어 있어서
        //   지표가 통째로 한 칸씩 밀려 붙었다(PJ 평가에 점수가 안 나오던 원인 — 2026-08-12).
        public string phaseName;
        public string stepName;

        // --- 자세 성립/유지 ---
        public int posesRequired;         // 요구한 자세 수 (진단: 좌·우 2개 / 그 외 0)
        public int posesCompleted;        // 유지 시간을 채운 자세 수
        public float holdSeconds;         // 파지 성립 상태로 있던 누적 시간
        public float firstContactSeconds = -1f;  // 단계 시작~첫 파지 성립까지(-1 = 끝까지 못 잡음)
        public int gripDropouts;          // 성립했다가 풀린 횟수(손이 떨어진 횟수)
        public int holdResets;            // 유예 시간을 넘겨 유지 타이머가 0으로 초기화된 횟수

        // --- 호흡(해당 단계만) ---
        public int breathsRequired;       // 요구 호흡 횟수(0 = 호흡 단계 아님)
        public int breathsCompleted;      // 성공한 호흡 주기 수
        public int breathFailures;        // 유지비율 미달로 카운트가 리셋된 횟수
        public float breathHoldRatio;     // 마지막 호흡 주기의 자세 유지비율(0~1)
        public int breathGripDropouts;    // ★호흡을 유도하는 동안 파지를 놓친 횟수(유예시간 이상 떨어진 것만)
        public int earlyThrusts;          // ★다 내쉬기 전에 순간 교정을 가한 횟수 — 타이밍 실수
        public int lateThrusts;           // ★날숨 끝 허용 시간을 넘겨 교정한 횟수

        // --- 견착(삼각근-이마 밀착 프록시) ---
        public float postureSeconds;      // 견착 성립 상태 누적 시간

        // --- 손모양 유사도 (가이드 클립 마지막 프레임 기준) ---
        // ★가이드 클립의 끝 프레임이 곧 '유지해야 할 자세'라서, 파지가 성립해 있는 동안
        //   그 프레임과의 유사도를 표본으로 모아 평균 낸다(2026-08-18 사용자 제안).
        //   표본이 0이면 채점에서 이 항목을 빼고 만점 처리한다 — 클립이 없는 단계·제2늑골 대응.
        public float poseSimilarity;      // 0~1 평균
        public int similaritySamples;     // 표본 수(0 = 측정 안 함)

        // --- 산출 ---
        public float score;               // 0~100 (두개골 전용 산식)
        public string grade;

        /// <summary>자세 완료율 0~1. 요구 자세가 없으면 '파지 성립 여부'로 본다.</summary>
        public float CompletionRatio =>
            posesRequired > 0
                ? Mathf.Clamp01((float)posesCompleted / posesRequired)
                : (firstContactSeconds >= 0f ? 1f : 0f);
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

    /// <summary>
    /// 경추 ROM 한 방향의 측정 결과.
    /// ★이 술기는 채점 지표가 없다 — 판정 경로가 PassiveStretch라 점수·유사도가 전부 0이다.
    ///   그래서 각도 자체를 결과로 보여준다.
    /// </summary>
    [Serializable]
    public class RomMeasurement
    {
        public string planeName;      // 시상면 · 관상면 · 횡단면
        public string directionName;  // 굴곡 · 신전 · 좌측굴 …
        public float maxAngle;     // 임상 최대각
        public float activeAngle;     // 환자가 스스로 도달한 각
        public float passiveAngle;    // 시술자가 밀어 도달한 각
        public float DeficitAngle => Mathf.Max(0f, maxAngle - passiveAngle);
    }

    [Tooltip("경추 ROM 측정값. 비어 있으면 이 시나리오가 아니다.")]
    public List<RomMeasurement> romMeasurements = new List<RomMeasurement>();

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

    /// <summary>두개골 단계 2줄 요약. 유사도 대신 '자세 성립·유지'를 보여준다.</summary>
    private static void AppendCranialStepLines(StringBuilder sb, string phaseName, StepResult step, string grade)
    {
        var c = step.cranial;

        // 1줄: 점수 + 완료도(진단은 자세 개수, 파지·호흡 단계는 성립 여부) + 유지 시간
        string done = c.posesRequired > 0
            ? $"자세 {c.posesCompleted}/{c.posesRequired}"
            : $"파지 {(c.firstContactSeconds >= 0f ? "성립" : "미성립")}";
        sb.AppendLine($"■ {phaseName} {step.stepName}  {step.finalScore:F0}점 ({grade}) · {done} · 유지 {c.holdSeconds:F1}초");

        // 2줄: 안정성 + (해당 단계만) 호흡·견착
        string firstContact = c.firstContactSeconds >= 0f ? $"{c.firstContactSeconds:F1}초" : "없음";
        string breath = c.breathsRequired > 0
            ? $" · 호흡 {c.breathsCompleted}/{c.breathsRequired}(유지율 {c.breathHoldRatio:P0}, 실패 {c.breathFailures}회" +
              (c.breathGripDropouts > 0 ? $", ★호흡 중 이탈 {c.breathGripDropouts}회" : "") +
              (c.earlyThrusts > 0 ? $", ★이른 교정 {c.earlyThrusts}회" : "") +
              (c.lateThrusts > 0 ? $", ★늦은 교정 {c.lateThrusts}회" : "") + ")"
            : "";
        string posture = c.postureSeconds > 0.1f ? $" · 견착 {c.postureSeconds:F1}초" : "";
        sb.AppendLine($"   이탈 {c.gripDropouts}회 · 유지실패 {c.holdResets}회 · 첫 접촉 {firstContact}{breath}{posture}");
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

                    // ★두개골 단계는 유사도·리밋이 성립하지 않으므로(전부 0) 자세 성립·유지 지표로 대체한다.
                    if (step.cranial != null)
                    {
                        AppendCranialStepLines(sb, phase.phaseName, step, grade);
                        continue;
                    }

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
    /// <summary>
    /// 경추 ROM 결과. 점수·유사도 대신 <b>측정한 각도</b>를 보여준다.
    /// 이 술기는 판정 경로가 PassiveStretch라 점수가 전부 0이라 그대로 쓰면 의미가 없다.
    ///
    /// 면마다 읽는 법이 다르다 —
    ///   관상면·횡단면은 <b>좌우 비대칭</b>이 임상 소견이고,
    ///   시상면은 굴곡 45°·신전 90°로 최대값이 달라 좌우 개념이 성립하지 않는다.
    /// </summary>
    public static string BuildRomSummaryText(TrainingResultData data)
    {
        if (data == null || data.romMeasurements == null || data.romMeasurements.Count == 0) return "";

        const float asymmetryWarn = 10f;   // 좌우 차가 이만큼 넘으면 짚어 준다(도)

        // ★비례 폰트라 공백 패딩으로는 칸이 안 맞는다(한글은 글자마다 폭이 다르다).
        //   TMP의 <pos=x%> 로 컬럼 위치를 고정한다. 이 문자열은 화면 표시 전용이라
        //   태그를 써도 서버 전송(BuildSummaryText)에는 영향이 없다.
        // ★2026-08-27 회의 결정 — 면 구분(시상·관상·횡단)을 빼고 방향 이름만 쓴다.
        //   열 순서도 [참고치] → 능동 → 수동 → 차이값이다. 용어는 압박→수동, 최대→참고치.
        //   차이값 = 참고치 − 수동(부족각)이다. 능동과 수동의 차가 아니다(사용자 확인).
        const string C1 = "<pos=26%>";   // 참고치
        const string C2 = "<pos=45%>";   // 능동
        const string C3 = "<pos=64%>";   // 수동
        const string C4 = "<pos=83%>";   // 차이값

        var sb = new StringBuilder();
        sb.AppendLine(data.isCompleted ? "경추ROM 진단 내역" : "경추ROM 진단 내역 (중도 종료)");
        sb.AppendLine();
        sb.AppendLine($"방향{C1}참고치{C2}능동{C3}수동{C4}차이값");
        sb.AppendLine("<size=60%>────────────────────────────────────────────────────────────</size>");

        // ★좌우 대칭 항목은 <b>둘 중 더 못 간 쪽만</b> 하이라이트한다(2026-08-28 사용자 지시).
        //   양쪽 다 칠하면 어느 쪽이 문제인지 안 보인다. 시상면은 좌우 개념이 없어 그대로 칠한다.
        string worseLateral = WorseSideName(data, "좌측굴", "우측굴");
        string worseRotation = WorseSideName(data, "좌회전", "우회전");

        foreach (var m in data.romMeasurements)
        {
            if (m == null) continue;

            bool paired = m.directionName == "좌측굴" || m.directionName == "우측굴"
                       || m.directionName == "좌회전" || m.directionName == "우회전";
            bool highlight = m.DeficitAngle >= 0.5f
                && (!paired || m.directionName == worseLateral || m.directionName == worseRotation);

            // ★부족각은 눈에 띄어야 한다 — 치료 후 되돌려야 할 각이 이 값이다.
            string deficit = highlight
                ? $"<mark=#ffd54f40><b>{m.DeficitAngle:F0}°</b></mark>"
                : $"{m.DeficitAngle:F0}°";

            sb.AppendLine($"{m.directionName}{C1}{m.maxAngle:F0}°{C2}{m.activeAngle:F0}°" +
                          $"{C3}{m.passiveAngle:F0}°{C4}{deficit}");
        }

        // 좌우 비대칭 — 관상면·횡단면만. 시상면은 굴곡·신전이라 대칭 개념이 없다.
        AppendRomAsymmetry(sb, data, "좌측굴", "우측굴", "관상면", asymmetryWarn);
        AppendRomAsymmetry(sb, data, "좌회전", "우회전", "횡단면", asymmetryWarn);

        sb.AppendLine();
        sb.AppendLine($"수행 시간 {FormatTime(data.totalTime)}");
        sb.Append("※ 참고치 = 정상 기준각(굴곡 45° · 신전 90° · 측굴 45° · 회전 90°) · " +
                  "능동 = 환자가 스스로 간 각 · 수동 = 시술자가 밀어 간 각 · 차이값 = 참고치까지 남은 각");
        return sb.ToString().TrimEnd();
    }

    /// <summary>좌우 쌍에서 더 못 간 쪽의 이름. 둘 중 하나라도 없으면 빈 문자열.</summary>
    private static string WorseSideName(TrainingResultData data, string leftName, string rightName)
    {
        RomMeasurement left = null, right = null;
        foreach (var m in data.romMeasurements)
        {
            if (m == null) continue;
            if (m.directionName == leftName) left = m;
            else if (m.directionName == rightName) right = m;
        }
        if (left == null || right == null) return "";
        if (Mathf.Abs(left.passiveAngle - right.passiveAngle) < 0.5f) return "";
        return left.passiveAngle < right.passiveAngle ? leftName : rightName;
    }

    private static void AppendRomAsymmetry(StringBuilder sb, TrainingResultData data,
                                           string leftName, string rightName, string planeName, float warnAt)
    {
        RomMeasurement left = null, right = null;
        foreach (var m in data.romMeasurements)
        {
            if (m == null) continue;
            if (m.directionName == leftName) left = m;
            else if (m.directionName == rightName) right = m;
        }
        if (left == null || right == null) return;

        // ★2026-08-27 회의 결정 — 좌우 "차"가 아니라 <b>덜 간 쪽</b>을 짚는다.
        //   차이값만 보면 어느 쪽이 문제인지 한 번 더 따져야 한다. 그쪽을 개선해야 하므로
        //   문제측을 이름으로 박아 준다.
        float diff = Mathf.Abs(left.passiveAngle - right.passiveAngle);
        bool leftLess = left.passiveAngle < right.passiveAngle;
        string limited = leftLess ? leftName : rightName;
        float lessAngle = leftLess ? left.passiveAngle : right.passiveAngle;
        float moreAngle = leftLess ? right.passiveAngle : left.passiveAngle;

        sb.AppendLine();
        if (diff < 0.5f)
        {
            sb.Append($"{leftName}·{rightName} 좌우 대칭 ({lessAngle:F0}°)");
            return;
        }

        string line = $"{limited}이(가) {diff:F0}° 덜 감 ({lessAngle:F0}° / {moreAngle:F0}°)";
        sb.Append(diff >= warnAt
            ? $"<mark=#ff6b6b40>⚠ <b>{line}</b></mark>"
            : line);
    }

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
