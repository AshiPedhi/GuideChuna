using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static HandPoseDataLoader;

/// <summary>
/// Guide hand playback controller for ChunaPathEvaluator.
/// Returns IEnumerator for coroutines; MonoBehaviour calls StartCoroutine.
///
/// 프레임 데이터는 ChunaPathEvaluator.ConvertFramesToWorldSpace()에서
/// 로드 시 월드 좌표로 일괄 변환됨 → 여기서는 직접 적용만 수행.
/// </summary>
public class GuideHandPlaybackController
{
    private readonly ChunaPathEvaluator owner;

    public GuideHandPlaybackController(ChunaPathEvaluator owner)
    {
        this.owner = owner;
    }

    // Current guide frame index (owner reads this)
    private int currentGuideFrameIndex = 0;
    public int CurrentGuideFrameIndex => currentGuideFrameIndex;

    /// <summary>
    /// Start guide hand playback coroutine.
    /// Returns IEnumerator; caller must wrap with StartCoroutine.
    /// </summary>
    public IEnumerator PlaybackRoutine(
        List<PoseFrame> frames,
        HandTransformMapper leftGuideHand, HandTransformMapper rightGuideHand,
        Transform referenceTransform, Vector3 recordedPatientOffset,
        float startRatio, float endRatio,
        float guidePlaybackSpeed, bool loopGuideHands, float loopDelaySeconds,
        Color guideHandColor, bool fadeOnTouch, float touchAlpha,
        bool showDebugLogs)
    {
        float frameTime = 1f / 30f;

        int startFrameIdx = Mathf.RoundToInt(startRatio * (frames.Count - 1));
        int endFrameIdx = Mathf.RoundToInt(endRatio * (frames.Count - 1));
        startFrameIdx = Mathf.Clamp(startFrameIdx, 0, frames.Count - 1);
        endFrameIdx = Mathf.Clamp(endFrameIdx, startFrameIdx, frames.Count - 1);

        currentGuideFrameIndex = startFrameIdx;

        if (showDebugLogs)
            ChunaLogger.Log($"<color=cyan>[Guide] Playback range: {startFrameIdx} ~ {endFrameIdx} (ratio: {startRatio:P0} ~ {endRatio:P0})</color>");

        while (true)
        {
            if (frames.Count == 0) yield break;

            PoseFrame frame = frames[currentGuideFrameIndex];

            if (leftGuideHand != null)
            {
                leftGuideHand.SetVisible(true);
                if (leftGuideHand.Root != null)
                {
                    leftGuideHand.Root.position = frame.leftRootPosition;
                    leftGuideHand.Root.rotation = frame.leftRootRotation;
                }

                foreach (var kvp in frame.leftLocalPoses)
                {
                    leftGuideHand.SetJointLocalPose(kvp.Key, kvp.Value.position, kvp.Value.rotation);
                }
            }

            if (rightGuideHand != null)
            {
                rightGuideHand.SetVisible(true);
                if (rightGuideHand.Root != null)
                {
                    rightGuideHand.Root.position = frame.rightRootPosition;
                    rightGuideHand.Root.rotation = frame.rightRootRotation;
                }

                foreach (var kvp in frame.rightLocalPoses)
                {
                    rightGuideHand.SetJointLocalPose(kvp.Key, kvp.Value.position, kvp.Value.rotation);
                }
            }

            currentGuideFrameIndex++;
            if (currentGuideFrameIndex > endFrameIdx)
            {
                if (loopGuideHands)
                {
                    if (loopDelaySeconds > 0f)
                    {
                        if (showDebugLogs)
                            ChunaLogger.Log($"[Guide] Loop complete, waiting {loopDelaySeconds}s before restart");
                        yield return new WaitForSeconds(loopDelaySeconds);
                    }
                    currentGuideFrameIndex = startFrameIdx;
                }
                else
                {
                    break;
                }
            }

            yield return new WaitForSeconds(frameTime / guidePlaybackSpeed);
        }
    }

    /// <summary>
    /// Show first frame of guide hands (for start position indication).
    /// </summary>
    public void ShowFirstFrame(
        List<PoseFrame> frames,
        HandTransformMapper leftGuideHand, HandTransformMapper rightGuideHand,
        Transform referenceTransform, Vector3 recordedPatientOffset,
        float startRatio,
        Color guideHandColor, bool fadeOnTouch, float touchAlpha,
        bool isAnyHandTouching, bool showDebugLogs)
    {
        if (frames == null || frames.Count == 0)
        {
            if (showDebugLogs)
                ChunaLogger.LogWarning("[ChunaPathEvaluator] Guide hand first frame failed - no frame data");
            return;
        }

        if (leftGuideHand == null && rightGuideHand == null)
        {
            ChunaLogger.LogWarning("[ChunaPathEvaluator] Guide hand first frame failed - no guide hands assigned!");
            return;
        }

        int startFrameIndex = Mathf.RoundToInt(startRatio * (frames.Count - 1));
        startFrameIndex = Mathf.Clamp(startFrameIndex, 0, frames.Count - 1);
        PoseFrame firstFrame = frames[startFrameIndex];

        if (leftGuideHand != null)
        {
            leftGuideHand.SetVisible(true);
            leftGuideHand.SetColorAndAlpha(guideHandColor, guideHandColor.a);
            if (leftGuideHand.Root != null)
            {
                leftGuideHand.Root.position = firstFrame.leftRootPosition;
                leftGuideHand.Root.rotation = firstFrame.leftRootRotation;
            }

            foreach (var kvp in firstFrame.leftLocalPoses)
            {
                leftGuideHand.SetJointLocalPose(kvp.Key, kvp.Value.position, kvp.Value.rotation);
            }
        }

        if (rightGuideHand != null)
        {
            rightGuideHand.SetVisible(true);
            rightGuideHand.SetColorAndAlpha(guideHandColor, guideHandColor.a);
            if (rightGuideHand.Root != null)
            {
                rightGuideHand.Root.position = firstFrame.rightRootPosition;
                rightGuideHand.Root.rotation = firstFrame.rightRootRotation;
            }

            foreach (var kvp in firstFrame.rightLocalPoses)
            {
                rightGuideHand.SetJointLocalPose(kvp.Key, kvp.Value.position, kvp.Value.rotation);
            }
        }

        if (showDebugLogs)
            ChunaLogger.Log($"<color=cyan>[ChunaPathEvaluator] Guide hand first frame shown (frame: {startFrameIndex}/{frames.Count - 1}, ratio: {startRatio:P0})</color>");
    }

    /// <summary>
    /// Hide guide hands.
    /// </summary>
    public void HideGuideHands(HandTransformMapper leftGuideHand, HandTransformMapper rightGuideHand)
    {
        if (leftGuideHand != null)
            leftGuideHand.SetVisible(false);

        if (rightGuideHand != null)
            rightGuideHand.SetVisible(false);
    }
}
