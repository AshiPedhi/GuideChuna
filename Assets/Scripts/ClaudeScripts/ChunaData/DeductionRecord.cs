using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 감점 요소 저장 및 점수 관리 시스템
/// 위반 기록을 저장하고 최종 점수를 계산
/// </summary>
public class DeductionRecord : MonoBehaviour
{
    [Header("=== 점수 설정 ===")]
    [Tooltip("시작 점수")]
    [SerializeField] private float startingScore = 100f;

    [Tooltip("최소 점수 (이 이하로 내려가지 않음)")]
    [SerializeField] private float minimumScore = 0f;

    [Tooltip("최대 점수")]
    [SerializeField] private float maximumScore = 100f;

    [Header("=== 감점 배율 ===")]
    [Tooltip("연속 위반 시 추가 감점 배율")]
    [SerializeField] private float consecutiveViolationMultiplier = 1.5f;

    [Tooltip("같은 유형 반복 위반 시 추가 감점 배율")]
    [SerializeField] private float repeatViolationMultiplier = 1.2f;

    [Header("=== 보너스 설정 ===")]
    [Tooltip("무위반 시간 보너스 (초당)")]
    [SerializeField] private float noViolationBonusPerSecond = 0.1f;

    [Tooltip("완벽 수행 보너스")]
    [SerializeField] private float perfectExecutionBonus = 10f;

    [Header("=== 한계 데이터 참조 ===")]
    [SerializeField] private ChunaLimitData limitData;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = true;

    // 현재 세션 데이터
    private SessionRecord currentSession;
    private float sessionStartTime;
    private float lastViolationTime;
    private ViolationType lastViolationType;
    private int consecutiveViolationCount;

    // 이벤트
    public event Action<float> OnScoreChanged;
    public event Action<DeductionEntry> OnDeductionAdded;
    public event Action<SessionRecord> OnSessionEnded;

    /// <summary>
    /// 세션 기록
    /// </summary>
    [System.Serializable]
    public class SessionRecord
    {
        public string sessionId;
        public string procedureName;
        public ChunaType procedureType;
        public DateTime startTime;
        public DateTime endTime;
        public float duration;
        public float startingScore;
        public float finalScore;
        public int totalDeductions;
        public float totalDeductionAmount;
        public List<DeductionEntry> deductions = new List<DeductionEntry>();
        public Dictionary<ViolationType, int> violationCounts = new Dictionary<ViolationType, int>();
        public float bonusEarned;
        public bool isCompleted;
        public string grade;

        public SessionRecord()
        {
            sessionId = Guid.NewGuid().ToString();
            deductions = new List<DeductionEntry>();
            violationCounts = new Dictionary<ViolationType, int>();
        }
    }

    /// <summary>
    /// 감점 항목
    /// </summary>
    [System.Serializable]
    public class DeductionEntry
    {
        public int entryId;
        public float timestamp;
        public float elapsedTime;
        public ViolationType violationType;
        public ViolationSeverity severity;
        public float baseDeduction;
        public float multiplier;
        public float finalDeduction;
        public bool isLeftHand;
        public int jointId;
        public string jointName;
        public float limitRatio;
        public Vector3 violationValue;
        public Vector3 limitValue;
        public string description;
    }

    void Awake()
    {
        currentSession = new SessionRecord();
    }

    /// <summary>
    /// 새 세션 시작
    /// </summary>
    public void StartSession(string procedureName = "", ChunaType procedureType = ChunaType.IsometricExercise)
    {
        currentSession = new SessionRecord
        {
            procedureName = procedureName,
            procedureType = procedureType,
            startTime = DateTime.Now,
            startingScore = startingScore
        };

        sessionStartTime = Time.time;
        lastViolationTime = 0f;
        lastViolationType = ViolationType.None;
        consecutiveViolationCount = 0;

        if (limitData != null)
        {
            currentSession.procedureName = limitData.ProcedureName;
            currentSession.procedureType = limitData.ProcedureType;
        }

        if (showDebugLogs)
            Debug.Log($"<color=cyan>[DeductionRecord] 세션 시작: {currentSession.procedureName}</color>");
    }

    /// <summary>
    /// 세션 종료
    /// </summary>
    public SessionRecord EndSession()
    {
        currentSession.endTime = DateTime.Now;
        currentSession.duration = Time.time - sessionStartTime;
        currentSession.isCompleted = true;

        // 보너스 계산
        CalculateBonus();

        // 최종 점수 계산
        currentSession.finalScore = CalculateFinalScore();
        currentSession.grade = CalculateGrade(currentSession.finalScore);

        if (showDebugLogs)
        {
            Debug.Log($"<color=green>[DeductionRecord] 세션 종료</color>");
            Debug.Log($"  최종 점수: {currentSession.finalScore:F1}점");
            Debug.Log($"  등급: {currentSession.grade}");
            Debug.Log($"  총 감점: {currentSession.totalDeductionAmount:F1}점 ({currentSession.totalDeductions}회)");
            Debug.Log($"  보너스: +{currentSession.bonusEarned:F1}점");
        }

        OnSessionEnded?.Invoke(currentSession);

        return currentSession;
    }

    /// <summary>
    /// 수동 감점 추가
    /// </summary>
    public void AddManualDeduction(float amount, string reason, ViolationType type = ViolationType.None)
    {
        if (currentSession == null || currentSession.isCompleted)
            return;

        DeductionEntry entry = new DeductionEntry
        {
            entryId = currentSession.deductions.Count + 1,
            timestamp = Time.time,
            elapsedTime = Time.time - sessionStartTime,
            violationType = type,
            severity = ViolationSeverity.Moderate,
            baseDeduction = amount,
            multiplier = 1f,
            finalDeduction = amount,
            description = reason
        };

        currentSession.deductions.Add(entry);
        currentSession.totalDeductions++;
        currentSession.totalDeductionAmount += amount;

        OnDeductionAdded?.Invoke(entry);
        OnScoreChanged?.Invoke(GetCurrentScore());

        if (showDebugLogs)
            Debug.Log($"<color=red>[DeductionRecord] 수동 감점: -{amount:F1}점 ({reason})</color>");
    }

    /// <summary>
    /// 기본 감점값 가져오기
    /// </summary>
    private float GetBaseDeduction(ViolationSeverity severity)
    {
        if (limitData != null)
        {
            return limitData.GetDeductionForSeverity(severity);
        }

        // 기본값
        switch (severity)
        {
            case ViolationSeverity.Minor:
                return 1f;
            case ViolationSeverity.Moderate:
                return 5f;
            case ViolationSeverity.Severe:
                return 15f;
            case ViolationSeverity.Dangerous:
                return 30f;
            default:
                return 0f;
        }
    }

    /// <summary>
    /// 보너스 계산
    /// </summary>
    private void CalculateBonus()
    {
        float bonus = 0f;

        // 무위반 시간 보너스
        float safeTime = currentSession.duration;
        foreach (var deduction in currentSession.deductions)
        {
            safeTime -= 1f; // 각 위반마다 1초 차감
        }
        if (safeTime > 0)
        {
            bonus += safeTime * noViolationBonusPerSecond;
        }

        // 완벽 수행 보너스
        if (currentSession.totalDeductions == 0)
        {
            bonus += perfectExecutionBonus;
        }

        currentSession.bonusEarned = bonus;
    }

    /// <summary>
    /// 최종 점수 계산
    /// </summary>
    private float CalculateFinalScore()
    {
        float score = startingScore - currentSession.totalDeductionAmount + currentSession.bonusEarned;
        return Mathf.Clamp(score, minimumScore, maximumScore);
    }

    /// <summary>
    /// 현재 점수 가져오기
    /// </summary>
    public float GetCurrentScore()
    {
        if (currentSession == null)
            return startingScore;

        float score = startingScore - currentSession.totalDeductionAmount;
        return Mathf.Clamp(score, minimumScore, maximumScore);
    }

    /// <summary>
    /// 등급 계산
    /// </summary>
    private string CalculateGrade(float score)
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

    /// <summary>
    /// 현재 세션 가져오기
    /// </summary>
    public SessionRecord GetCurrentSession()
    {
        return currentSession;
    }

    /// <summary>
    /// 감점 기록 가져오기
    /// </summary>
    public List<DeductionEntry> GetDeductions()
    {
        return currentSession?.deductions ?? new List<DeductionEntry>();
    }

    /// <summary>
    /// 위반 유형별 통계 가져오기
    /// </summary>
    public Dictionary<ViolationType, int> GetViolationStatistics()
    {
        return currentSession?.violationCounts ?? new Dictionary<ViolationType, int>();
    }

    /// <summary>
    /// 세션 리셋
    /// </summary>
    public void ResetSession()
    {
        currentSession = new SessionRecord
        {
            startingScore = startingScore
        };
        sessionStartTime = Time.time;
        lastViolationTime = 0f;
        lastViolationType = ViolationType.None;
        consecutiveViolationCount = 0;

        OnScoreChanged?.Invoke(startingScore);

        if (showDebugLogs)
            Debug.Log("[DeductionRecord] 세션 리셋됨");
    }

    /// <summary>
    /// 한계 데이터 설정
    /// </summary>
    public void SetLimitData(ChunaLimitData data)
    {
        limitData = data;
    }

    /// <summary>
    /// 세션 요약 리포트 생성
    /// </summary>
    public string GenerateSummaryReport()
    {
        if (currentSession == null)
            return "세션 데이터 없음";

        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        sb.AppendLine("========== 추나 훈련 결과 리포트 ==========");
        sb.AppendLine($"세션 ID: {currentSession.sessionId}");
        sb.AppendLine($"시술 종류: {currentSession.procedureName}");
        sb.AppendLine($"시작 시간: {currentSession.startTime:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"소요 시간: {currentSession.duration:F1}초");
        sb.AppendLine();
        sb.AppendLine("========== 점수 ==========");
        sb.AppendLine($"시작 점수: {currentSession.startingScore:F1}점");
        sb.AppendLine($"총 감점: -{currentSession.totalDeductionAmount:F1}점 ({currentSession.totalDeductions}회)");
        sb.AppendLine($"보너스: +{currentSession.bonusEarned:F1}점");
        sb.AppendLine($"최종 점수: {currentSession.finalScore:F1}점");
        sb.AppendLine($"등급: {currentSession.grade}");
        sb.AppendLine();

        if (currentSession.violationCounts.Count > 0)
        {
            sb.AppendLine("========== 위반 통계 ==========");
            foreach (var kvp in currentSession.violationCounts.OrderByDescending(x => x.Value))
            {
                sb.AppendLine($"  {kvp.Key}: {kvp.Value}회");
            }
            sb.AppendLine();
        }

        if (currentSession.deductions.Count > 0)
        {
            sb.AppendLine("========== 감점 상세 ==========");
            foreach (var entry in currentSession.deductions)
            {
                sb.AppendLine($"[{entry.elapsedTime:F1}초] -{entry.finalDeduction:F1}점: {entry.description}");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// JSON 형식으로 세션 데이터 내보내기
    /// </summary>
    public string ExportToJson()
    {
        if (currentSession == null)
            return "{}";

        return JsonUtility.ToJson(currentSession, true);
    }
}
