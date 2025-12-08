using System;
using UnityEngine;
using UnityEngine.UI;
using Oculus.Interaction;

/// <summary>
/// 추나 훈련 통합 관리자 (간소화 버전)
/// ChunaLimitChecker의 프레임 비율 기반 평가와 연동
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
    [SerializeField] private HandPoseTrainingController pathTrainingController;

    [Header("=== UI 참조 ===")]
    [SerializeField] private Text scoreText;
    [SerializeField] private Text statusText;
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
    private float sessionStartTime;
    private float currentScore = 100f;

    // 이벤트
    public event Action OnTrainingStarted;
    public event Action<float> OnTrainingEnded;  // 최종 점수
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
        FindModules();
    }

    void Start()
    {
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

        if (pathTrainingController == null)
            pathTrainingController = FindObjectOfType<HandPoseTrainingController>();

        if (limitChecker == null)
        {
            limitChecker = gameObject.AddComponent<ChunaLimitChecker>();
            if (showDebugLogs)
                Debug.Log("[ChunaTrainingManager] ChunaLimitChecker 자동 생성됨");
        }
    }

    /// <summary>
    /// 이벤트 연결
    /// </summary>
    private void ConnectEvents()
    {
        if (limitChecker != null)
        {
            limitChecker.OnLimitStatusChanged += HandleLimitStatusChanged;
        }
    }

    /// <summary>
    /// 이벤트 연결 해제
    /// </summary>
    private void DisconnectEvents()
    {
        if (limitChecker != null)
        {
            limitChecker.OnLimitStatusChanged -= HandleLimitStatusChanged;
        }
    }

    /// <summary>
    /// 초기화
    /// </summary>
    public void Initialize()
    {
        if (showDebugLogs)
            Debug.Log("<color=cyan>[ChunaTrainingManager] 초기화 중...</color>");

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

        currentLimitData = procedureType switch
        {
            ChunaType.HealthySideRotation => healthySideRotationLimit,
            ChunaType.AffectedSideRotation => affectedSideRotationLimit,
            ChunaType.IsometricExercise => isometricExerciseLimit,
            ChunaType.LateralFlexion => lateralFlexionLimit,
            _ => isometricExerciseLimit
        };

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
        sessionStartTime = Time.time;
        currentScore = 100f;

        if (limitChecker != null)
        {
            limitChecker.Initialize();
            limitChecker.SetEnabled(true);
        }

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
    public float EndTraining()
    {
        if (!isTrainingActive)
        {
            if (showDebugLogs)
                Debug.LogWarning("[ChunaTrainingManager] 훈련이 진행 중이 아닙니다.");
            return 0f;
        }

        isTrainingActive = false;

        if (limitChecker != null)
        {
            limitChecker.SetEnabled(false);
        }

        float duration = Time.time - sessionStartTime;

        if (showDebugLogs)
        {
            Debug.Log("<color=green>========== 훈련 결과 ==========</color>");
            Debug.Log($"최종 점수: {currentScore:F1}점");
            Debug.Log($"소요 시간: {duration:F1}초");
        }

        OnTrainingEnded?.Invoke(currentScore);

        return currentScore;
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

        if (limitChecker != null)
            limitChecker.Reset();

        currentScore = 100f;
        UpdateUI();

        if (showDebugLogs)
            Debug.Log("[ChunaTrainingManager] 훈련 리셋됨");
    }

    // ========== 이벤트 핸들러 ==========

    private void HandleLimitStatusChanged(ChunaLimitChecker.LimitCheckResult leftResult, ChunaLimitChecker.LimitCheckResult rightResult)
    {
        OnStatusChanged?.Invoke(leftResult.overallStatus, rightResult.overallStatus);
    }

    // ========== UI 업데이트 ==========

    private void UpdateUI()
    {
        UpdateScoreUI(currentScore);

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

    // ========== Public API ==========

    /// <summary>
    /// 현재 점수 가져오기
    /// </summary>
    public float GetCurrentScore()
    {
        return currentScore;
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
    /// 세션 경과 시간 가져오기
    /// </summary>
    public float GetSessionElapsedTime()
    {
        return isTrainingActive ? Time.time - sessionStartTime : 0f;
    }

    /// <summary>
    /// 한계 데이터 수동 설정
    /// </summary>
    public void SetLimitData(ChunaLimitData data)
    {
        currentLimitData = data;

        if (showDebugLogs)
            Debug.Log($"[ChunaTrainingManager] 한계 데이터 수동 설정: {data?.ProcedureName ?? "null"}");
    }
}
