using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 추나 훈련 피드백 UI
/// 실시간 점수, 상태, 위반 정보를 표시
/// </summary>
public class ChunaFeedbackUI : MonoBehaviour
{
    [Header("=== 매니저 참조 ===")]
    [SerializeField] private ChunaTrainingManager trainingManager;
    [SerializeField] private ChunaLimitChecker limitChecker;
    [SerializeField] private DeductionRecord deductionRecord;

    [Header("=== 점수 UI ===")]
    [SerializeField] private Text scoreText;
    [SerializeField] private Text gradeText;
    [SerializeField] private Slider scoreSlider;
    [SerializeField] private Image scoreSliderFill;

    [Header("=== 상태 UI ===")]
    [SerializeField] private Text leftHandStatusText;
    [SerializeField] private Text rightHandStatusText;
    [SerializeField] private Image leftHandStatusIcon;
    [SerializeField] private Image rightHandStatusIcon;
    [SerializeField] private RectTransform leftHandLimitIndicator;
    [SerializeField] private RectTransform rightHandLimitIndicator;

    [Header("=== 위반 정보 UI ===")]
    [SerializeField] private Text violationCountText;
    [SerializeField] private Text lastViolationText;
    [SerializeField] private GameObject violationAlert;
    [SerializeField] private Text violationAlertText;
    [SerializeField] private float alertDisplayDuration = 2f;

    [Header("=== 되돌리기 가이드 UI ===")]
    [SerializeField] private GameObject revertGuidePanel;
    [SerializeField] private Text revertGuideText;
    [SerializeField] private Image revertProgressBar;
    [SerializeField] private RectTransform revertArrowIndicator;

    [Header("=== 결과 패널 ===")]
    [SerializeField] private GameObject resultPanel;
    [SerializeField] private Text resultScoreText;
    [SerializeField] private Text resultGradeText;
    [SerializeField] private Text resultDetailText;
    [SerializeField] private Text resultViolationSummaryText;

    [Header("=== 색상 설정 ===")]
    [SerializeField] private Color safeColor = new Color(0.2f, 0.8f, 0.2f);
    [SerializeField] private Color warningColor = new Color(1f, 0.8f, 0f);
    [SerializeField] private Color dangerColor = new Color(1f, 0.5f, 0f);
    [SerializeField] private Color exceededColor = new Color(1f, 0.2f, 0.2f);
    [SerializeField] private Gradient scoreGradient;

    [Header("=== 애니메이션 설정 ===")]
    [SerializeField] private float updateInterval = 0.1f;
    [SerializeField] private float scoreLerpSpeed = 5f;
    [SerializeField] private float indicatorLerpSpeed = 8f;

    // 상태
    private float displayedScore;
    private float lastUpdateTime;
    private float alertEndTime;
    private bool isShowingAlert;

    void Start()
    {
        // 자동 참조 찾기
        FindReferences();

        // 이벤트 연결
        ConnectEvents();

        // 초기화
        Initialize();
    }

    void Update()
    {
        if (Time.time - lastUpdateTime >= updateInterval)
        {
            lastUpdateTime = Time.time;
            UpdateDisplay();
        }

        // 점수 스무스 업데이트
        UpdateScoreSmooth();

        // 경고 알림 타이머
        UpdateAlert();
    }

    void OnDestroy()
    {
        DisconnectEvents();
    }

    /// <summary>
    /// 참조 자동 찾기
    /// </summary>
    private void FindReferences()
    {
        if (trainingManager == null)
            trainingManager = FindObjectOfType<ChunaTrainingManager>();

        if (limitChecker == null)
            limitChecker = FindObjectOfType<ChunaLimitChecker>();

        if (deductionRecord == null)
            deductionRecord = FindObjectOfType<DeductionRecord>();
    }

    /// <summary>
    /// 이벤트 연결
    /// </summary>
    private void ConnectEvents()
    {
        if (trainingManager != null)
        {
            trainingManager.OnTrainingStarted += OnTrainingStarted;
            trainingManager.OnTrainingEnded += OnTrainingEnded;
            trainingManager.OnViolation += OnViolation;
            trainingManager.OnStatusChanged += OnStatusChanged;
        }

        if (deductionRecord != null)
        {
            deductionRecord.OnScoreChanged += OnScoreChanged;
            deductionRecord.OnDeductionAdded += OnDeductionAdded;
        }
    }

    /// <summary>
    /// 이벤트 연결 해제
    /// </summary>
    private void DisconnectEvents()
    {
        if (trainingManager != null)
        {
            trainingManager.OnTrainingStarted -= OnTrainingStarted;
            trainingManager.OnTrainingEnded -= OnTrainingEnded;
            trainingManager.OnViolation -= OnViolation;
            trainingManager.OnStatusChanged -= OnStatusChanged;
        }

        if (deductionRecord != null)
        {
            deductionRecord.OnScoreChanged -= OnScoreChanged;
            deductionRecord.OnDeductionAdded -= OnDeductionAdded;
        }
    }

    /// <summary>
    /// 초기화
    /// </summary>
    private void Initialize()
    {
        displayedScore = 100f;

        // UI 초기 상태
        if (violationAlert != null)
            violationAlert.SetActive(false);

        if (revertGuidePanel != null)
            revertGuidePanel.SetActive(false);

        if (resultPanel != null)
            resultPanel.SetActive(false);

        UpdateScoreDisplay(100f);
        UpdateStatusDisplay(LimitStatus.Safe, LimitStatus.Safe);
    }

    /// <summary>
    /// 표시 업데이트
    /// </summary>
    private void UpdateDisplay()
    {
        if (limitChecker != null)
        {
            var leftResult = limitChecker.GetLeftHandResult();
            var rightResult = limitChecker.GetRightHandResult();

            UpdateStatusDisplay(leftResult.overallStatus, rightResult.overallStatus);
            UpdateLimitIndicators(leftResult.maxLimitRatio, rightResult.maxLimitRatio);
        }

        if (trainingManager != null)
        {
            UpdateViolationCount(trainingManager.GetSessionViolationCount());
        }
    }

    /// <summary>
    /// 점수 스무스 업데이트
    /// </summary>
    private void UpdateScoreSmooth()
    {
        float targetScore = deductionRecord?.GetCurrentScore() ?? 100f;
        displayedScore = Mathf.Lerp(displayedScore, targetScore, Time.deltaTime * scoreLerpSpeed);
        UpdateScoreDisplay(displayedScore);
    }

    /// <summary>
    /// 경고 알림 업데이트
    /// </summary>
    private void UpdateAlert()
    {
        if (isShowingAlert && Time.time >= alertEndTime)
        {
            HideViolationAlert();
        }
    }

    /// <summary>
    /// 점수 표시 업데이트
    /// </summary>
    private void UpdateScoreDisplay(float score)
    {
        if (scoreText != null)
        {
            scoreText.text = $"{Mathf.RoundToInt(score)}";
        }

        if (gradeText != null)
        {
            gradeText.text = GetGradeFromScore(score);
            gradeText.color = GetGradeColor(score);
        }

        if (scoreSlider != null)
        {
            scoreSlider.value = score / 100f;
        }

        if (scoreSliderFill != null && scoreGradient != null)
        {
            scoreSliderFill.color = scoreGradient.Evaluate(score / 100f);
        }
    }

    /// <summary>
    /// 상태 표시 업데이트
    /// </summary>
    private void UpdateStatusDisplay(LimitStatus leftStatus, LimitStatus rightStatus)
    {
        // 왼손 상태
        if (leftHandStatusText != null)
        {
            leftHandStatusText.text = GetStatusString(leftStatus);
            leftHandStatusText.color = GetStatusColor(leftStatus);
        }

        if (leftHandStatusIcon != null)
        {
            leftHandStatusIcon.color = GetStatusColor(leftStatus);
        }

        // 오른손 상태
        if (rightHandStatusText != null)
        {
            rightHandStatusText.text = GetStatusString(rightStatus);
            rightHandStatusText.color = GetStatusColor(rightStatus);
        }

        if (rightHandStatusIcon != null)
        {
            rightHandStatusIcon.color = GetStatusColor(rightStatus);
        }
    }

    /// <summary>
    /// 한계 인디케이터 업데이트
    /// </summary>
    private void UpdateLimitIndicators(float leftRatio, float rightRatio)
    {
        // 왼손 인디케이터 (0~100% 범위의 바)
        if (leftHandLimitIndicator != null)
        {
            float targetScale = Mathf.Clamp01(leftRatio);
            Vector3 scale = leftHandLimitIndicator.localScale;
            scale.x = Mathf.Lerp(scale.x, targetScale, Time.deltaTime * indicatorLerpSpeed);
            leftHandLimitIndicator.localScale = scale;

            // 색상도 업데이트
            Image img = leftHandLimitIndicator.GetComponent<Image>();
            if (img != null)
            {
                img.color = GetColorForRatio(leftRatio);
            }
        }

        // 오른손 인디케이터
        if (rightHandLimitIndicator != null)
        {
            float targetScale = Mathf.Clamp01(rightRatio);
            Vector3 scale = rightHandLimitIndicator.localScale;
            scale.x = Mathf.Lerp(scale.x, targetScale, Time.deltaTime * indicatorLerpSpeed);
            rightHandLimitIndicator.localScale = scale;

            Image img = rightHandLimitIndicator.GetComponent<Image>();
            if (img != null)
            {
                img.color = GetColorForRatio(rightRatio);
            }
        }
    }

    /// <summary>
    /// 위반 횟수 업데이트
    /// </summary>
    private void UpdateViolationCount(int count)
    {
        if (violationCountText != null)
        {
            violationCountText.text = $"위반: {count}회";
        }
    }

    /// <summary>
    /// 위반 알림 표시
    /// </summary>
    public void ShowViolationAlert(string message, ViolationSeverity severity)
    {
        if (violationAlert != null)
        {
            violationAlert.SetActive(true);
        }

        if (violationAlertText != null)
        {
            violationAlertText.text = message;
            violationAlertText.color = GetSeverityColor(severity);
        }

        isShowingAlert = true;
        alertEndTime = Time.time + alertDisplayDuration;
    }

    /// <summary>
    /// 위반 알림 숨기기
    /// </summary>
    private void HideViolationAlert()
    {
        if (violationAlert != null)
        {
            violationAlert.SetActive(false);
        }
        isShowingAlert = false;
    }

    /// <summary>
    /// 되돌리기 가이드 표시
    /// </summary>
    public void ShowRevertGuide(bool isLeftHand, Vector3 direction)
    {
        if (revertGuidePanel != null)
        {
            revertGuidePanel.SetActive(true);
        }

        if (revertGuideText != null)
        {
            string handName = isLeftHand ? "왼손" : "오른손";
            revertGuideText.text = $"{handName}을 안전 위치로 되돌려주세요";
        }

        // 화살표 방향 설정
        if (revertArrowIndicator != null)
        {
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            revertArrowIndicator.rotation = Quaternion.Euler(0, 0, angle);
        }
    }

    /// <summary>
    /// 되돌리기 가이드 숨기기
    /// </summary>
    public void HideRevertGuide()
    {
        if (revertGuidePanel != null)
        {
            revertGuidePanel.SetActive(false);
        }
    }

    /// <summary>
    /// 되돌리기 진행률 업데이트
    /// </summary>
    public void UpdateRevertProgress(float progress)
    {
        if (revertProgressBar != null)
        {
            revertProgressBar.fillAmount = progress;
        }
    }

    /// <summary>
    /// 결과 패널 표시
    /// </summary>
    public void ShowResultPanel(DeductionRecord.SessionRecord result)
    {
        if (resultPanel != null)
        {
            resultPanel.SetActive(true);
        }

        if (resultScoreText != null)
        {
            resultScoreText.text = $"{result.finalScore:F0}점";
        }

        if (resultGradeText != null)
        {
            resultGradeText.text = result.grade;
            resultGradeText.color = GetGradeColor(result.finalScore);
        }

        if (resultDetailText != null)
        {
            resultDetailText.text = $"소요 시간: {result.duration:F1}초\n" +
                                    $"총 감점: -{result.totalDeductionAmount:F1}점\n" +
                                    $"보너스: +{result.bonusEarned:F1}점";
        }

        if (resultViolationSummaryText != null)
        {
            string summary = $"총 위반: {result.totalDeductions}회\n";
            foreach (var kvp in result.violationCounts)
            {
                summary += $"  {GetViolationTypeName(kvp.Key)}: {kvp.Value}회\n";
            }
            resultViolationSummaryText.text = summary;
        }
    }

    /// <summary>
    /// 결과 패널 숨기기
    /// </summary>
    public void HideResultPanel()
    {
        if (resultPanel != null)
        {
            resultPanel.SetActive(false);
        }
    }

    // ========== 이벤트 핸들러 ==========

    private void OnTrainingStarted()
    {
        Initialize();
        HideResultPanel();
    }

    private void OnTrainingEnded(DeductionRecord.SessionRecord result)
    {
        if (result != null)
        {
            ShowResultPanel(result);
        }
    }

    private void OnViolation(ChunaLimitChecker.ViolationEvent violation)
    {
        string handName = violation.isLeftHand ? "왼손" : "오른손";
        string violationName = GetViolationTypeName(violation.violationType);
        ShowViolationAlert($"{handName}: {violationName}!", violation.severity);

        if (lastViolationText != null)
        {
            lastViolationText.text = $"최근: {violationName} ({violation.limitRatio:P0})";
        }
    }

    private void OnStatusChanged(LimitStatus leftStatus, LimitStatus rightStatus)
    {
        UpdateStatusDisplay(leftStatus, rightStatus);

        // 초과 상태면 되돌리기 가이드 표시
        if (leftStatus == LimitStatus.Exceeded)
        {
            ShowRevertGuide(true, Vector3.left);
        }
        else if (rightStatus == LimitStatus.Exceeded)
        {
            ShowRevertGuide(false, Vector3.right);
        }
        else if (leftStatus == LimitStatus.Safe && rightStatus == LimitStatus.Safe)
        {
            HideRevertGuide();
        }
    }

    private void OnScoreChanged(float newScore)
    {
        // 스무스 업데이트를 위해 타겟 점수만 저장
        // 실제 표시는 UpdateScoreSmooth에서 처리
    }

    private void OnDeductionAdded(DeductionRecord.DeductionEntry entry)
    {
        if (lastViolationText != null)
        {
            lastViolationText.text = $"-{entry.finalDeduction:F1}점: {entry.description}";
        }
    }

    // ========== 유틸리티 ==========

    private string GetStatusString(LimitStatus status)
    {
        return status switch
        {
            LimitStatus.Safe => "안전",
            LimitStatus.Warning => "주의",
            LimitStatus.Danger => "위험",
            LimitStatus.Exceeded => "초과!",
            _ => "?"
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

    private Color GetColorForRatio(float ratio)
    {
        if (ratio >= 1f) return exceededColor;
        if (ratio >= 0.95f) return dangerColor;
        if (ratio >= 0.8f) return warningColor;
        return safeColor;
    }

    private Color GetSeverityColor(ViolationSeverity severity)
    {
        return severity switch
        {
            ViolationSeverity.Minor => warningColor,
            ViolationSeverity.Moderate => dangerColor,
            ViolationSeverity.Severe => exceededColor,
            ViolationSeverity.Dangerous => exceededColor,
            _ => Color.white
        };
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

    private Color GetGradeColor(float score)
    {
        if (score >= 90f) return new Color(1f, 0.84f, 0f); // Gold
        if (score >= 80f) return new Color(0.75f, 0.75f, 0.75f); // Silver
        if (score >= 70f) return new Color(0.8f, 0.5f, 0.2f); // Bronze
        if (score >= 60f) return Color.white;
        return Color.red;
    }

    private string GetViolationTypeName(ViolationType type)
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
}
