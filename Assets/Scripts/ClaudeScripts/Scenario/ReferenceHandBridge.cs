using UnityEngine;
using System.Collections.Generic;
using static HandPoseDataLoader;

/// <summary>
/// HandPoseTrainingController와 ReferenceHandDisplay를 연결하는 브리지
/// TrainingController의 현재 프레임을 ReferenceHandDisplay에 전달
/// </summary>
public class ReferenceHandBridge : MonoBehaviour
{
    [Header("=== 컴포넌트 참조 ===")]
    [Tooltip("HandPoseTrainingController")]
    [SerializeField] private HandPoseTrainingController trainingController;

    [Tooltip("ReferenceHandDisplay")]
    [SerializeField] private ReferenceHandDisplay referenceDisplay;

    [Header("=== 데이터 로더 ===")]
    [Tooltip("현재 로드된 CSV 파일명 (자동 추적)")]
    [SerializeField] private string currentCsvFileName = "";

    [Header("=== 업데이트 설정 ===")]
    [Tooltip("자동 프레임 업데이트")]
    [SerializeField] private bool autoUpdateFrames = true;

    [Tooltip("업데이트 간격 (초)")]
    [SerializeField] private float updateInterval = 0.1f;

    [Header("=== 디버그 ===")]
    [SerializeField] private bool showDebugLogs = false;

    // 데이터 로더
    private HandPoseDataLoader dataLoader;

    // 현재 로드된 프레임들
    private List<PoseFrame> loadedFrames = new List<PoseFrame>();

    // 업데이트 타이머
    private float updateTimer = 0f;

    // 마지막 적용 프레임 인덱스 (중복 업데이트 방지)
    private int lastAppliedLeftFrame = -1;
    private int lastAppliedRightFrame = -1;

    void Awake()
    {
        dataLoader = new HandPoseDataLoader();

        // 컴포넌트 자동 찾기
        if (trainingController == null)
        {
            trainingController = FindObjectOfType<HandPoseTrainingController>();
        }

        if (referenceDisplay == null)
        {
            referenceDisplay = FindObjectOfType<ReferenceHandDisplay>();
        }

        if (trainingController == null)
        {
            Debug.LogError("[ReferenceHandBridge] HandPoseTrainingController를 찾을 수 없습니다!");
        }

        if (referenceDisplay == null)
        {
            Debug.LogWarning("[ReferenceHandBridge] ReferenceHandDisplay를 찾을 수 없습니다!");
        }
    }

    void Update()
    {
        if (!autoUpdateFrames || trainingController == null || referenceDisplay == null)
            return;

        updateTimer += Time.deltaTime;

        if (updateTimer >= updateInterval)
        {
            updateTimer = 0f;
            UpdateReferenceHands();
        }
    }

    /// <summary>
    /// 참조 손 업데이트 (현재 재생 프레임 기준)
    /// </summary>
    private void UpdateReferenceHands()
    {
        if (loadedFrames.Count == 0)
        {
            if (showDebugLogs)
            {
                Debug.LogWarning("[ReferenceHandBridge] 로드된 프레임이 없습니다.");
            }
            return;
        }

        // TrainingController에서 현재 재생 상태 가져오기
        var (leftPlaying, rightPlaying, leftFrame, rightFrame, totalFrames) = trainingController.GetPlaybackState();

        // 중복 업데이트 방지
        if (leftFrame == lastAppliedLeftFrame && rightFrame == lastAppliedRightFrame)
        {
            return;
        }

        // 유효성 검사
        if (leftFrame >= loadedFrames.Count || rightFrame >= loadedFrames.Count)
        {
            if (showDebugLogs)
            {
                Debug.LogWarning($"[ReferenceHandBridge] 프레임 인덱스 범위 초과: L={leftFrame}, R={rightFrame}, Total={loadedFrames.Count}");
            }
            return;
        }

        // 왼손/오른손 중 더 큰 프레임 인덱스 사용 (동기화)
        int currentFrameIndex = Mathf.Max(leftFrame, rightFrame);
        currentFrameIndex = Mathf.Clamp(currentFrameIndex, 0, loadedFrames.Count - 1);

        // 현재 프레임 가져오기
        PoseFrame currentFrame = loadedFrames[currentFrameIndex];

        // ReferenceHandDisplay에 적용
        referenceDisplay.ApplyPoseFrame(currentFrame);

        // 마지막 적용 프레임 기록
        lastAppliedLeftFrame = leftFrame;
        lastAppliedRightFrame = rightFrame;

        if (showDebugLogs && currentFrameIndex % 10 == 0)
        {
            Debug.Log($"[ReferenceHandBridge] 프레임 {currentFrameIndex} 적용 완료");
        }
    }

    /// <summary>
    /// CSV 파일 로드 (TrainingController와 동일한 데이터)
    /// </summary>
    /// <param name="csvFileName">CSV 파일명 (예: "등척성운동")</param>
    public void LoadCSVData(string csvFileName)
    {
        if (string.IsNullOrEmpty(csvFileName))
        {
            Debug.LogError("[ReferenceHandBridge] CSV 파일명이 비어있습니다!");
            return;
        }

        currentCsvFileName = csvFileName;

        // CSV 로드
        var result = dataLoader.LoadFromResources($"HandPoseData/{csvFileName}");

        if (!result.success)
        {
            Debug.LogError($"[ReferenceHandBridge] CSV 로드 실패: {result.errorMessage}");
            loadedFrames.Clear();
            return;
        }

        loadedFrames = result.frames;

        // 마지막 적용 프레임 리셋
        lastAppliedLeftFrame = -1;
        lastAppliedRightFrame = -1;

        if (showDebugLogs)
        {
            Debug.Log($"<color=cyan>[ReferenceHandBridge] ✓ CSV 로드 완료: {csvFileName} ({loadedFrames.Count} 프레임, {result.totalDuration:F2}초)</color>");
        }
    }

    /// <summary>
    /// ScenarioActionHandler에서 호출 (TrainingController와 동시 로드)
    /// </summary>
    public void OnTrainingStarted(string csvFileName)
    {
        LoadCSVData(csvFileName);

        if (showDebugLogs)
        {
            Debug.Log($"[ReferenceHandBridge] 훈련 시작: {csvFileName}");
        }
    }

    /// <summary>
    /// 훈련 종료 시 호출
    /// </summary>
    public void OnTrainingStopped()
    {
        loadedFrames.Clear();
        lastAppliedLeftFrame = -1;
        lastAppliedRightFrame = -1;

        if (showDebugLogs)
        {
            Debug.Log("[ReferenceHandBridge] 훈련 종료");
        }
    }

    /// <summary>
    /// 참조 손 표시/숨김
    /// </summary>
    public void SetReferenceHandsVisible(bool visible)
    {
        if (referenceDisplay != null)
        {
            referenceDisplay.SetReferenceHandsVisible(visible);
        }
    }

    /// <summary>
    /// 참조 손 투명도 설정
    /// </summary>
    public void SetReferenceAlpha(float alpha)
    {
        if (referenceDisplay != null)
        {
            referenceDisplay.SetAlpha(alpha);
        }
    }

    /// <summary>
    /// 현재 상태 정보
    /// </summary>
    public (string csvFile, int frameCount, int lastLeftFrame, int lastRightFrame) GetStatus()
    {
        return (currentCsvFileName, loadedFrames.Count, lastAppliedLeftFrame, lastAppliedRightFrame);
    }

#if UNITY_EDITOR
    [ContextMenu("Test - Load Sample CSV")]
    private void TestLoadSampleCSV()
    {
        // 테스트용 샘플 CSV 로드
        LoadCSVData("등척성운동");
    }

    [ContextMenu("Test - Show Status")]
    private void TestShowStatus()
    {
        var status = GetStatus();
        Debug.Log($"[ReferenceHandBridge] Status:\n" +
                  $"  CSV: {status.csvFile}\n" +
                  $"  Frames: {status.frameCount}\n" +
                  $"  Last L/R: {status.lastLeftFrame}/{status.lastRightFrame}");
    }
#endif
}
