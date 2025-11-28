using UnityEngine;
using TunaEvaluation;
using System.Collections.Generic;

/// <summary>
/// 추나 평가 시스템 활성화 도우미
/// Inspector에서 간편하게 추나 평가를 설정할 수 있습니다.
/// </summary>
public class TunaEvaluationSetup : MonoBehaviour
{
    [Header("=== 평가 모드 선택 ===")]
    [Tooltip("추나 평가 모드 활성화 (false면 일반 프레임 통과 모드)")]
    [SerializeField] private bool enableTunaEvaluation = false;

    [Header("=== 참조 (자동 연결) ===")]
    [SerializeField] private HandPoseTrainingController trainingController;
    [SerializeField] private TunaEvaluator tunaEvaluator;
    [SerializeField] private TunaResultUI tunaResultUI;
    [SerializeField] private TunaEvaluationUI tunaEvaluationUI;

    [Header("=== 기본 구간 설정 ===")]
    [Tooltip("CSV 총 프레임 수 (자동으로 구간 분할)")]
    [SerializeField] private int totalFrames = 100;

    [Tooltip("구간 개수")]
    [SerializeField] private int numberOfSegments = 3;

    [Tooltip("체크포인트 프레임 인덱스 (쉼표로 구분)")]
    [SerializeField] private string checkpointFrames = "30,60,90";

    [Header("=== 안전 범위 설정 ===")]
    [Tooltip("최대 회전 각도 (제한장벽)")]
    [SerializeField] private float maxRotationAngle = 45f;

    [Tooltip("최대 이동 거리 (m)")]
    [SerializeField] private float maxDistance = 0.3f;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showSetupLogs = true;

    void Awake()
    {
        // 자동 참조 찾기
        if (trainingController == null)
            trainingController = FindObjectOfType<HandPoseTrainingController>();

        if (tunaEvaluator == null)
            tunaEvaluator = FindObjectOfType<TunaEvaluator>();

        if (tunaResultUI == null)
            tunaResultUI = FindObjectOfType<TunaResultUI>();

        if (tunaEvaluationUI == null)
            tunaEvaluationUI = FindObjectOfType<TunaEvaluationUI>();
    }

    void Start()
    {
        ApplySettings();
    }

    /// <summary>
    /// 설정 적용
    /// </summary>
    [ContextMenu("Apply Settings")]
    public void ApplySettings()
    {
        if (showSetupLogs)
            Debug.Log($"[TunaSetup] 추나 평가 모드: {(enableTunaEvaluation ? "활성화" : "비활성화")}");

        if (enableTunaEvaluation)
        {
            SetupTunaEvaluation();
        }
        else
        {
            SetupNormalMode();
        }
    }

    /// <summary>
    /// 추나 평가 모드 설정
    /// </summary>
    private void SetupTunaEvaluation()
    {
        if (trainingController == null)
        {
            Debug.LogError("[TunaSetup] HandPoseTrainingController를 찾을 수 없습니다!");
            return;
        }

        // HandPoseTrainingController 설정
        trainingController.GetType().GetField("enableTunaEvaluation",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.SetValue(trainingController, true);

        trainingController.GetType().GetField("tunaEvaluator",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.SetValue(trainingController, tunaEvaluator);

        trainingController.GetType().GetField("tunaResultUI",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.SetValue(trainingController, tunaResultUI);

        // TunaEvaluator 설정
        if (tunaEvaluator != null)
        {
            SetupEvaluatorSegments();
        }

        if (showSetupLogs)
            Debug.Log("[TunaSetup] ✅ 추나 평가 모드 활성화 완료");
    }

    /// <summary>
    /// 일반 모드 설정
    /// </summary>
    private void SetupNormalMode()
    {
        if (trainingController == null) return;

        trainingController.GetType().GetField("enableTunaEvaluation",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.SetValue(trainingController, false);

        if (showSetupLogs)
            Debug.Log("[TunaSetup] ✅ 일반 프레임 통과 모드 활성화 완료");
    }

    /// <summary>
    /// 구간 자동 설정
    /// </summary>
    private void SetupEvaluatorSegments()
    {
        if (tunaEvaluator == null) return;

        List<TunaMotionSegment> segments = new List<TunaMotionSegment>();

        int framesPerSegment = totalFrames / numberOfSegments;
        string[] checkpoints = checkpointFrames.Split(',');
        HashSet<int> checkpointSet = new HashSet<int>();

        foreach (string cp in checkpoints)
        {
            if (int.TryParse(cp.Trim(), out int frame))
                checkpointSet.Add(frame);
        }

        for (int i = 0; i < numberOfSegments; i++)
        {
            TunaMotionSegment segment = new TunaMotionSegment();
            segment.segmentName = $"구간 {i + 1}";
            segment.startFrame = i * framesPerSegment;
            segment.endFrame = (i + 1) * framesPerSegment - 1;

            // 마지막 구간은 totalFrames까지
            if (i == numberOfSegments - 1)
                segment.endFrame = totalFrames - 1;

            // 안전 범위 설정
            segment.checkSafetyLimits = true;
            segment.leftHandMaxRotation = maxRotationAngle;
            segment.rightHandMaxRotation = maxRotationAngle;
            segment.leftHandMaxDistance = maxDistance;
            segment.rightHandMaxDistance = maxDistance;

            // 경로 검증 설정
            segment.requirePathFollowing = true;
            segment.pathTolerance = 0.05f;

            // 체크포인트 확인
            int midFrame = (segment.startFrame + segment.endFrame) / 2;
            if (checkpointSet.Contains(midFrame) || checkpointSet.Contains(segment.endFrame))
            {
                segment.isCheckpoint = true;
                segment.requiredHoldTime = 2f;
                segment.checkpointSimilarityThreshold = 0.8f;
            }

            segments.Add(segment);

            if (showSetupLogs)
                Debug.Log($"[TunaSetup] 구간 추가: {segment.segmentName} (프레임 {segment.startFrame}-{segment.endFrame})");
        }

        // Reflection으로 segments 설정
        var segmentsField = tunaEvaluator.GetType().GetField("motionSegments",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (segmentsField != null)
        {
            segmentsField.SetValue(tunaEvaluator, segments);
            if (showSetupLogs)
                Debug.Log($"[TunaSetup] ✅ {segments.Count}개 구간 설정 완료");
        }
    }

    /// <summary>
    /// 구간 초기화
    /// </summary>
    [ContextMenu("Clear Segments")]
    public void ClearSegments()
    {
        if (tunaEvaluator == null) return;

        var segmentsField = tunaEvaluator.GetType().GetField("motionSegments",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (segmentsField != null)
        {
            segmentsField.SetValue(tunaEvaluator, new List<TunaMotionSegment>());
            Debug.Log("[TunaSetup] 구간 초기화 완료");
        }
    }
}
