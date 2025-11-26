using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HandPose 디버깅 UI
/// 현재 프레임 정보, 유사도, 진행 상태를 표시
/// </summary>
public class HandPoseDebugUI : MonoBehaviour
{
    [Header("=== HandPoseTrainingController 참조 ===")]
    [SerializeField] private HandPoseTrainingController trainingController;

    [Header("=== UI 요소 ===")]
    [SerializeField] private Text frameInfoText;        // 프레임 정보 (현재/전체)
    [SerializeField] private Text progressInfoText;     // 사용자 진행률
    [SerializeField] private Text playbackStateText;    // 재생 상태

    [Header("=== 업데이트 설정 ===")]
    [SerializeField] private float updateInterval = 0.1f;

    private float updateTimer = 0f;

    void Awake()
    {
        // TrainingController 자동 찾기
        if (trainingController == null)
        {
            trainingController = FindObjectOfType<HandPoseTrainingController>();
        }

        if (trainingController == null)
        {
            Debug.LogWarning("[HandPoseDebugUI] HandPoseTrainingController를 찾을 수 없습니다!");
        }
    }

    void Update()
    {
        if (trainingController == null)
            return;

        updateTimer += Time.deltaTime;

        if (updateTimer >= updateInterval)
        {
            updateTimer = 0f;
            UpdateDebugInfo();
        }
    }

    /// <summary>
    /// 디버그 정보 업데이트
    /// </summary>
    private void UpdateDebugInfo()
    {
        // 재생 상태
        var (leftPlaying, rightPlaying, leftFrame, rightFrame, totalFrames) = trainingController.GetPlaybackState();

        // 사용자 진행률
        var (leftProgress, rightProgress, leftCompleted, rightCompleted) = trainingController.GetUserProgress();

        // 프레임 정보
        if (frameInfoText != null)
        {
            frameInfoText.text = $"프레임: L {leftFrame}/{totalFrames} | R {rightFrame}/{totalFrames}";
        }

        // 진행률 정보
        if (progressInfoText != null)
        {
            progressInfoText.text = $"진행: L {leftProgress}/{totalFrames} {(leftCompleted ? "✓" : "")} | R {rightProgress}/{totalFrames} {(rightCompleted ? "✓" : "")}";
        }

        // 재생 상태
        if (playbackStateText != null)
        {
            string status = "";
            if (leftPlaying && rightPlaying)
                status = "재생 중";
            else if (!leftPlaying && !rightPlaying)
                status = "일시정지";
            else
                status = "부분 재생";

            playbackStateText.text = $"상태: {status}";
        }
    }

    /// <summary>
    /// UI 표시/숨김
    /// </summary>
    public void SetVisible(bool visible)
    {
        gameObject.SetActive(visible);
    }

#if UNITY_EDITOR
    [ContextMenu("Test - Show Info")]
    private void TestShowInfo()
    {
        UpdateDebugInfo();
    }
#endif
}
