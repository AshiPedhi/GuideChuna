using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Oculus.Interaction;

/// <summary>
/// 추나 훈련 통합 관리자
/// 한계 기반 검사, 감점 기록, 되돌리기 기능을 통합 관리
/// 기존 HandPoseTrainingController와 함께 사용 가능
/// </summary>
public class ChunaTrainingManager : MonoBehaviour
{
    [Header("=== 훈련 모드 ===")]
    [Tooltip("훈련 모드 선택")]
    [SerializeField] private TrainingMode trainingMode = TrainingMode.LimitBased;

    [Header("=== 시술별 한계 데이터 ===")]
    [SerializeField] private ChunaLimitData healthySideRotationLimit;
    [SerializeField] private ChunaLimitData affectedSideRotationLimit;
    [SerializeField] private ChunaLimitData isometricExerciseLimit;
    [SerializeField] private ChunaLimitData lateralFlexionLimit;

    [Header("=== 모듈 참조 ===")]
    [SerializeField] private ChunaLimitChecker limitChecker;
    [SerializeField] private DeductionRecord deductionRecord;
    [SerializeField] private SafePositionManager safePositionManager;
    [SerializeField] private HandPoseTrainingController pathTrainingController;

    [Header("=== UI 참조 ===")]
    [SerializeField] private Text scoreText;
    [SerializeField] private Text statusText;
    [SerializeField] private Text violationText;
    [SerializeField] private Image leftHandStatusImage;
    [SerializeField] private Image rightHandStatusImage;
    [SerializeField] private GameObject warningPanel;
    [SerializeField] private Text warningText;

    [Header("=== 색상 설정 ===")]
    [SerializeField] private Color safeColor = Color.green;
    [SerializeField] private Color warningColor = Color.yellow;
    [SerializeField] private Color dangerColor = new Color(1f, 0.5f, 0f);
    [SerializeField] private Color exceededColor = Color.red;

    [Header("=== 설정 ===")]
    [Tooltip("자동 초기화")]
    [SerializeField] private bool autoInitialize = true;

    [Tooltip("UI 업데이트 간격")]
    [SerializeField] private float uiUpdateInterval = 0.1f;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = true;

    // 상태
    private bool isTrainingActive;
    private ChunaType currentProcedureType;
    private ChunaLimitData currentLimitData;
    private float lastUIUpdateTime;

    // 통계
    private int sessionViolationCount;
    private float sessionStartTime;

    // 이벤트
    public event Action OnTrainingStarted;
    public event Action<DeductionRecord.SessionRecord> OnTrainingEnded;
    public event Action<ChunaLimitChecker.ViolationEvent> OnViolation;
    public event Action<LimitStatus, LimitStatus> OnStatusChanged;  // left, right

    /// <summary>
    /// 훈련 모드
    /// </summary>
    public enum TrainingMode
    {
        [Tooltip("한계 기반 - 제한 범위 내에서 훈련")]
        LimitBased,

        [Tooltip("경로 기반 - 저장된 경로를 따라가며 훈련")]
        PathBased,

        [Tooltip("혼합 - 두 가지 모드 동시 사용")]
        Combined
    }

    void Awake()
    {
        // 모듈 자동 찾기
        FindModules();
    }

    void Start()
    {
        // 이벤트 연결
        ConnectEvents();

        if (autoInitialize)
        {
            Initialize();
        }
    }

    void Update()
    {
        if (!isTrainingActive)
            return;

        // UI 업데이트
        if (Time.time - lastUIUpdateTime >= uiUpdateInterval)
        {
            lastUIUpdateTime = Time.time;
            UpdateUI();
        }
    }

    void OnDestroy()
    {
        DisconnectEvents();
    }

    /// <summary>
    /// 모듈 자동 탐색
    /// </summary>
    private void FindModules()
    {
        if (limitChecker == null)
            limitChecker = GetComponent<ChunaLimitChecker>() ?? FindObjectOfType<ChunaLimitChecker>();

        if (deductionRecord == null)
            deductionRecord = GetComponent<DeductionRecord>() ?? FindObjectOfType<DeductionRecord>();

        if (safePositionManager == null)
            safePositionManager = GetComponent<SafePositionManager>() ?? FindObjectOfType<SafePositionManager>();

        if (pathTrainingController == null)
            pathTrainingController = FindObjectOfType<HandPoseTrainingController>();

        // 모듈이 없으면 생성
        if (limitChecker == null)
        {
            limitChecker = gameObject.AddComponent<ChunaLimitChecker>();
            if (showDebugLogs)
                Debug.Log("[ChunaTrainingManager] ChunaLimitChecker 자동 생성됨");
        }

        if (deductionRecord == null)
        {
            deductionRecord = gameObject.AddComponent<DeductionRecord>();
            if (showDebugLogs)
                Debug.Log("[ChunaTrainingManager] DeductionRecord 자동 생성됨");
        }

        if (safePositionManager == null)
        {
            safePositionManager = gameObject.AddComponent<SafePositionManager>();
            if (showDebugLogs)
                Debug.Log("[ChunaTrainingManager] SafePositionManager 자동 생성됨");
        }
    }

    /// <summary>
    /// 이벤트 연결
    /// </summary>
    private void ConnectEvents()
    {
        if (limitChecker != null)
        {
            limitChecker.OnViolationDetected += HandleViolation;
            limitChecker.OnLimitStatusChanged += HandleLimitStatusChanged;
            limitChecker.OnRevertRequired += HandleRevertRequired;
        }

        if (deductionRecord != null)
        {
            deductionRecord.OnScoreChanged += HandleScoreChanged;
            deductionRecord.OnDeductionAdded += HandleDeductionAdded;
        }

        if (safePositionManager != null)
        {
            safePositionManager.OnRevertStarted += HandleRevertStarted;
            safePositionManager.OnRevertCompleted += HandleRevertCompleted;
        }
    }

    /// <summary>
    /// 이벤트 연결 해제
    /// </summary>
    private void DisconnectEvents()
    {
        if (limitChecker != null)
        {
            limitChecker.OnViolationDetected -= HandleViolation;
            limitChecker.OnLimitStatusChanged -= HandleLimitStatusChanged;
            limitChecker.OnRevertRequired -= HandleRevertRequired;
        }

        if (deductionRecord != null)
        {
            deductionRecord.OnScoreChanged -= HandleScoreChanged;
            deductionRecord.OnDeductionAdded -= HandleDeductionAdded;
        }

        if (safePositionManager != null)
        {
            safePositionManager.OnRevertStarted -= HandleRevertStarted;
            safePositionManager.OnRevertCompleted -= HandleRevertCompleted;
        }
    }

    /// <summary>
    /// 초기화
    /// </summary>
    public void Initialize()
    {
        if (showDebugLogs)
            Debug.Log("<color=cyan>[ChunaTrainingManager] 초기화 중...</color>");

        // 기본 한계 데이터 설정
        if (currentLimitData == null)
        {
            SetProcedureType(ChunaType.IsometricExercise);
        }

        if (showDebugLogs)
            Debug.Log("<color=green>[ChunaTrainingManager] 초기화 완료</color>");
    }

    /// <summary>
    /// 시술 종류 설정
    /// </summary>
    public void SetProcedureType(ChunaType procedureType)
    {
        currentProcedureType = procedureType;

        // 해당 시술의 한계 데이터 가져오기
        currentLimitData = procedureType switch
        {
            ChunaType.HealthySideRotation => healthySideRotationLimit,
            ChunaType.AffectedSideRotation => affectedSideRotationLimit,
            ChunaType.IsometricExercise => isometricExerciseLimit,
            ChunaType.LateralFlexion => lateralFlexionLimit,
            _ => isometricExerciseLimit
        };

        // 모듈에 한계 데이터 설정
        if (limitChecker != null)
            limitChecker.SetLimitData(currentLimitData);

        if (deductionRecord != null)
            deductionRecord.SetLimitData(currentLimitData);

        if (safePositionManager != null)
            safePositionManager.SetLimitData(currentLimitData);

        if (showDebugLogs)
        {
            string dataName = currentLimitData != null ? currentLimitData.ProcedureName : "없음";
            Debug.Log($"<color=cyan>[ChunaTrainingManager] 시술 종류 설정: {procedureType} (데이터: {dataName})</color>");
        }
    }

    /// <summary>
    /// 훈련 시작
    /// </summary>
    public void StartTraining(ChunaType procedureType)
    {
        if (isTrainingActive)
        {
            if (showDebugLogs)
                Debug.LogWarning("[ChunaTrainingManager] 이미 훈련이 진행 중입니다.");
            return;
        }

        SetProcedureType(procedureType);

        isTrainingActive = true;
        sessionViolationCount = 0;
        sessionStartTime = Time.time;

        // 감점 기록 세션 시작
        if (deductionRecord != null)
        {
            string procedureName = currentLimitData != null ? currentLimitData.ProcedureName : procedureType.ToString();
            deductionRecord.StartSession(procedureName, procedureType);
        }

        // 한계 체커 초기화 및 시작
        if (limitChecker != null)
        {
            limitChecker.Initialize();
            limitChecker.SetEnabled(true);
        }

        // 안전 위치 관리자 리셋
        if (safePositionManager != null)
        {
            safePositionManager.Reset();
        }

        // UI 초기화
        ShowWarningPanel(false);
        UpdateUI();

        if (showDebugLogs)
            Debug.Log($"<color=green>[ChunaTrainingManager] 훈련 시작: {procedureType}</color>");

        OnTrainingStarted?.Invoke();
    }

    /// <summary>
    /// 훈련 시작 (문자열 버전)
    /// </summary>
    public void StartTraining(string procedureTypeName)
    {
        if (Enum.TryParse<ChunaType>(procedureTypeName, out ChunaType procedureType))
        {
            StartTraining(procedureType);
        }
        else
        {
            // CSV 파일 이름으로 매칭
            ChunaType matched = procedureTypeName switch
            {
                "건측회전" => ChunaType.HealthySideRotation,
                "환측회전" => ChunaType.AffectedSideRotation,
                "등척성운동" => ChunaType.IsometricExercise,
                "측굴" => ChunaType.LateralFlexion,
                _ => ChunaType.IsometricExercise
            };
            StartTraining(matched);
        }
    }

    /// <summary>
    /// 훈련 종료
    /// </summary>
    public DeductionRecord.SessionRecord EndTraining()
    {
        if (!isTrainingActive)
        {
            if (showDebugLogs)
                Debug.LogWarning("[ChunaTrainingManager] 훈련이 진행 중이 아닙니다.");
            return null;
        }

        isTrainingActive = false;

        // 한계 체커 정지
        if (limitChecker != null)
        {
            limitChecker.SetEnabled(false);
        }

        // 세션 종료 및 결과 가져오기
        DeductionRecord.SessionRecord result = null;
        if (deductionRecord != null)
        {
            result = deductionRecord.EndSession();
        }

        // 결과 출력
        if (showDebugLogs && result != null)
        {
            Debug.Log("<color=green>========== 훈련 결과 ==========</color>");
            Debug.Log($"최종 점수: {result.finalScore:F1}점 ({result.grade})");
            Debug.Log($"총 위반: {result.totalDeductions}회");
            Debug.Log($"소요 시간: {result.duration:F1}초");
        }

        OnTrainingEnded?.Invoke(result);

        return result;
    }

    /// <summary>
    /// 훈련 일시정지
    /// </summary>
    public void PauseTraining()
    {
        if (!isTrainingActive) return;

        if (limitChecker != null)
            limitChecker.SetEnabled(false);

        if (showDebugLogs)
            Debug.Log("[ChunaTrainingManager] 훈련 일시정지");
    }

    /// <summary>
    /// 훈련 재개
    /// </summary>
    public void ResumeTraining()
    {
        if (!isTrainingActive) return;

        if (limitChecker != null)
            limitChecker.SetEnabled(true);

        if (showDebugLogs)
            Debug.Log("[ChunaTrainingManager] 훈련 재개");
    }

    /// <summary>
    /// 훈련 리셋
    /// </summary>
    public void ResetTraining()
    {
        if (isTrainingActive)
        {
            EndTraining();
        }

        if (deductionRecord != null)
            deductionRecord.ResetSession();

        if (safePositionManager != null)
            safePositionManager.Reset();

        if (limitChecker != null)
            limitChecker.Reset();

        sessionViolationCount = 0;
        UpdateUI();

        if (showDebugLogs)
            Debug.Log("[ChunaTrainingManager] 훈련 리셋됨");
    }

    // ========== 이벤트 핸들러 ==========

    private void HandleViolation(ChunaLimitChecker.ViolationEvent violation)
    {
        sessionViolationCount++;

        // 감점 기록에 추가
        if (deductionRecord != null)
        {
            deductionRecord.AddDeduction(violation);
        }

        // 경고 표시
        ShowViolationWarning(violation);

        OnViolation?.Invoke(violation);
    }

    private void HandleLimitStatusChanged(ChunaLimitChecker.LimitCheckResult leftResult, ChunaLimitChecker.LimitCheckResult rightResult)
    {
        // 안전 위치 관리자에 상태 업데이트
        if (safePositionManager != null)
        {
            safePositionManager.UpdateLimitStatus(true, leftResult.overallStatus);
            safePositionManager.UpdateLimitStatus(false, rightResult.overallStatus);
        }

        OnStatusChanged?.Invoke(leftResult.overallStatus, rightResult.overallStatus);
    }

    private void HandleRevertRequired(bool isLeftHand)
    {
        if (safePositionManager != null)
        {
            safePositionManager.StartRevert(isLeftHand);
        }
    }

    private void HandleScoreChanged(float newScore)
    {
        UpdateScoreUI(newScore);
    }

    private void HandleDeductionAdded(DeductionRecord.DeductionEntry entry)
    {
        UpdateViolationUI(entry);
    }

    private void HandleRevertStarted(bool isLeftHand, SafePositionManager.SafePositionRecord targetPosition)
    {
        string handName = isLeftHand ? "왼손" : "오른손";
        ShowWarning($"{handName}을 안전 위치로 되돌려주세요!");
    }

    private void HandleRevertCompleted(bool isLeftHand)
    {
        ShowWarningPanel(false);
    }

    // ========== UI 업데이트 ==========

    private void UpdateUI()
    {
        if (deductionRecord != null)
        {
            UpdateScoreUI(deductionRecord.GetCurrentScore());
        }

        if (limitChecker != null)
        {
            UpdateStatusUI(limitChecker.GetLeftHandResult(), limitChecker.GetRightHandResult());
        }
    }

    private void UpdateScoreUI(float score)
    {
        if (scoreText != null)
        {
            scoreText.text = $"점수: {score:F0}점";
        }
    }

    private void UpdateStatusUI(ChunaLimitChecker.LimitCheckResult leftResult, ChunaLimitChecker.LimitCheckResult rightResult)
    {
        if (statusText != null)
        {
            string leftStatus = GetStatusString(leftResult.overallStatus);
            string rightStatus = GetStatusString(rightResult.overallStatus);
            statusText.text = $"왼손: {leftStatus} | 오른손: {rightStatus}";
        }

        if (leftHandStatusImage != null)
        {
            leftHandStatusImage.color = GetStatusColor(leftResult.overallStatus);
        }

        if (rightHandStatusImage != null)
        {
            rightHandStatusImage.color = GetStatusColor(rightResult.overallStatus);
        }
    }

    private void UpdateViolationUI(DeductionRecord.DeductionEntry entry)
    {
        if (violationText != null)
        {
            violationText.text = $"위반: {sessionViolationCount}회\n최근: -{entry.finalDeduction:F1}점";
        }
    }

    private void ShowViolationWarning(ChunaLimitChecker.ViolationEvent violation)
    {
        string handName = violation.isLeftHand ? "왼손" : "오른손";
        string violationDesc = GetViolationDescription(violation.violationType);
        string severityDesc = GetSeverityDescription(violation.severity);

        ShowWarning($"{handName}에서 {severityDesc} {violationDesc}이 감지되었습니다!\n한계의 {violation.limitRatio:P0} 도달");
    }

    private void ShowWarning(string message)
    {
        if (warningPanel != null)
        {
            warningPanel.SetActive(true);
        }

        if (warningText != null)
        {
            warningText.text = message;
        }
    }

    private void ShowWarningPanel(bool show)
    {
        if (warningPanel != null)
        {
            warningPanel.SetActive(show);
        }
    }

    private string GetStatusString(LimitStatus status)
    {
        return status switch
        {
            LimitStatus.Safe => "안전",
            LimitStatus.Warning => "주의",
            LimitStatus.Danger => "위험",
            LimitStatus.Exceeded => "초과!",
            _ => "알 수 없음"
        };
    }

    private Color GetStatusColor(LimitStatus status)
    {
        return status switch
        {
            LimitStatus.Safe => safeColor,
            LimitStatus.Warning => warningColor,
            LimitStatus.Danger => dangerColor,
            LimitStatus.Exceeded => exceededColor,
            _ => Color.gray
        };
    }

    private string GetViolationDescription(ViolationType type)
    {
        return type switch
        {
            ViolationType.OverFlexion => "과굴곡",
            ViolationType.OverExtension => "과신전",
            ViolationType.OverRotation => "과회전",
            ViolationType.OverLateralFlexion => "과측굴",
            ViolationType.OverTranslation => "과이동",
            ViolationType.OverSpeed => "과속",
            ViolationType.OverForce => "과압력",
            _ => "위반"
        };
    }

    private string GetSeverityDescription(ViolationSeverity severity)
    {
        return severity switch
        {
            ViolationSeverity.Minor => "경미한",
            ViolationSeverity.Moderate => "중간 수준의",
            ViolationSeverity.Severe => "심각한",
            ViolationSeverity.Dangerous => "위험한",
            _ => ""
        };
    }

    // ========== Public API ==========

    /// <summary>
    /// 현재 점수 가져오기
    /// </summary>
    public float GetCurrentScore()
    {
        return deductionRecord?.GetCurrentScore() ?? 100f;
    }

    /// <summary>
    /// 훈련 활성화 여부
    /// </summary>
    public bool IsTrainingActive()
    {
        return isTrainingActive;
    }

    /// <summary>
    /// 현재 시술 종류 가져오기
    /// </summary>
    public ChunaType GetCurrentProcedureType()
    {
        return currentProcedureType;
    }

    /// <summary>
    /// 현재 한계 데이터 가져오기
    /// </summary>
    public ChunaLimitData GetCurrentLimitData()
    {
        return currentLimitData;
    }

    /// <summary>
    /// 훈련 모드 설정
    /// </summary>
    public void SetTrainingMode(TrainingMode mode)
    {
        trainingMode = mode;

        if (pathTrainingController != null)
        {
            pathTrainingController.enabled = (mode == TrainingMode.PathBased || mode == TrainingMode.Combined);
        }

        if (limitChecker != null)
        {
            limitChecker.enabled = (mode == TrainingMode.LimitBased || mode == TrainingMode.Combined);
        }

        if (showDebugLogs)
            Debug.Log($"[ChunaTrainingManager] 훈련 모드 변경: {mode}");
    }

    /// <summary>
    /// 세션 위반 횟수 가져오기
    /// </summary>
    public int GetSessionViolationCount()
    {
        return sessionViolationCount;
    }

    /// <summary>
    /// 세션 경과 시간 가져오기
    /// </summary>
    public float GetSessionElapsedTime()
    {
        return isTrainingActive ? Time.time - sessionStartTime : 0f;
    }

    /// <summary>
    /// 결과 리포트 생성
    /// </summary>
    public string GenerateReport()
    {
        return deductionRecord?.GenerateSummaryReport() ?? "데이터 없음";
    }

    /// <summary>
    /// 한계 데이터 수동 설정
    /// </summary>
    public void SetLimitData(ChunaLimitData data)
    {
        currentLimitData = data;

        if (limitChecker != null)
            limitChecker.SetLimitData(data);

        if (deductionRecord != null)
            deductionRecord.SetLimitData(data);

        if (safePositionManager != null)
            safePositionManager.SetLimitData(data);

        if (showDebugLogs)
            Debug.Log($"[ChunaTrainingManager] 한계 데이터 수동 설정: {data?.ProcedureName ?? "null"}");
    }
}
