using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static HandPoseDataLoader;

/// <summary>
/// Guide hand playback controller for ChunaPathEvaluator.
/// Returns IEnumerator for coroutines; MonoBehaviour calls StartCoroutine.
///
/// 프레임 좌표는 referenceTransform 기준 로컬로 저장되어 있으므로,
/// 매 프레임 (refPos + refRot * localPos, refRot * localRot)로 월드 변환 후 적용한다.
/// 이렇게 하면 환자(referenceTransform)가 이동/회전해도 가이드 핸드가 자동 추종.
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
    /// 프레임 인덱스 리셋 (평가 시작 시 이전 값 잔류 방지)
    /// </summary>
    public void ResetFrameIndex()
    {
        currentGuideFrameIndex = 0;
    }

    /// <summary>
    /// Start guide hand playback coroutine.
    /// Returns IEnumerator; caller must wrap with StartCoroutine.
    /// </summary>
    public IEnumerator PlaybackRoutine(
        List<PoseFrame> frames,
        HandTransformMapper leftGuideHand, HandTransformMapper rightGuideHand,
        float startRatio, float endRatio,
        float guidePlaybackSpeed, bool loopGuideHands, float loopDelaySeconds,
        Color guideHandColor, bool showDebugLogs)
    {
        float frameTime = 1f / 30f;

        int startFrameIdx = Mathf.RoundToInt(startRatio * (frames.Count - 1));
        int endFrameIdx = Mathf.RoundToInt(endRatio * (frames.Count - 1));
        startFrameIdx = Mathf.Clamp(startFrameIdx, 0, frames.Count - 1);
        endFrameIdx = Mathf.Clamp(endFrameIdx, startFrameIdx, frames.Count - 1);

        currentGuideFrameIndex = startFrameIdx;

        // ★ 한 손만 녹화된 경우 감지 (해당 손의 조인트 데이터가 비어있는지 확인)
        bool hasLeftData = HasHandData(frames, true);
        bool hasRightData = HasHandData(frames, false);

        if (showDebugLogs)
            ChunaLogger.Log($"<color=cyan>[Guide] Playback range: {startFrameIdx} ~ {endFrameIdx} (ratio: {startRatio:P0} ~ {endRatio:P0}), 데이터: L={hasLeftData}, R={hasRightData}</color>");

        // ★ 데이터 없는 손은 숨김
        if (leftGuideHand != null && !hasLeftData)
            leftGuideHand.SetVisible(false);
        if (rightGuideHand != null && !hasRightData)
            rightGuideHand.SetVisible(false);

        // ★ 재생 시작 시 색상 초기화 (데이터 있는 손만)
        if (leftGuideHand != null && hasLeftData)
            leftGuideHand.SetColorAndAlpha(guideHandColor, guideHandColor.a);
        if (rightGuideHand != null && hasRightData)
            rightGuideHand.SetColorAndAlpha(guideHandColor, guideHandColor.a);

        while (true)
        {
            if (frames.Count == 0) yield break;

            PoseFrame frame = frames[currentGuideFrameIndex];

            // referenceTransform이 매 프레임 현재 환자 위치/회전을 들고 있다고 가정.
            Transform refT = owner.ReferenceTransform;
            Vector3 refPos = refT != null ? refT.position : Vector3.zero;
            Quaternion refRot = refT != null ? refT.rotation : Quaternion.identity;

            // ★그 손을 시술자가 이미 제자리에 갖다 댄 상태면 켜지 않는다(손별 숨김) — 08-11 사용자 지시.
            if (leftGuideHand != null && hasLeftData && !owner.IsGuideHandSuppressed(true))
            {
                leftGuideHand.SetVisible(true);
                if (leftGuideHand.Root != null)
                {
                    leftGuideHand.Root.position = refPos + refRot * frame.leftRootPosition;
                    leftGuideHand.Root.rotation = refRot * frame.leftRootRotation;
                }

                foreach (var kvp in frame.leftLocalPoses)
                {
                    leftGuideHand.SetJointLocalPose(kvp.Key, kvp.Value.position, kvp.Value.rotation);
                }
            }

            if (rightGuideHand != null && hasRightData && !owner.IsGuideHandSuppressed(false))
            {
                rightGuideHand.SetVisible(true);
                if (rightGuideHand.Root != null)
                {
                    rightGuideHand.Root.position = refPos + refRot * frame.rightRootPosition;
                    rightGuideHand.Root.rotation = refRot * frame.rightRootRotation;
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
        float startRatio,
        Color guideHandColor, bool showDebugLogs)
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

        // ★ 한 손만 녹화된 경우 감지
        bool hasLeftData = HasHandData(frames, true);
        bool hasRightData = HasHandData(frames, false);

        // referenceTransform 기준 로컬→월드 변환 (환자 이동/회전 추종)
        Transform refT = owner.ReferenceTransform;
        Vector3 refPos = refT != null ? refT.position : Vector3.zero;
        Quaternion refRot = refT != null ? refT.rotation : Quaternion.identity;

        if (leftGuideHand != null)
        {
            if (!hasLeftData || owner.IsGuideHandSuppressed(true))
            {
                leftGuideHand.SetVisible(false);
            }
            else
            {
                leftGuideHand.SetVisible(true);
                leftGuideHand.SetColorAndAlpha(guideHandColor, guideHandColor.a);
                if (leftGuideHand.Root != null)
                {
                    leftGuideHand.Root.position = refPos + refRot * firstFrame.leftRootPosition;
                    leftGuideHand.Root.rotation = refRot * firstFrame.leftRootRotation;
                }

                foreach (var kvp in firstFrame.leftLocalPoses)
                {
                    leftGuideHand.SetJointLocalPose(kvp.Key, kvp.Value.position, kvp.Value.rotation);
                }
            }
        }

        if (rightGuideHand != null)
        {
            if (!hasRightData || owner.IsGuideHandSuppressed(false))
            {
                rightGuideHand.SetVisible(false);
            }
            else
            {
                rightGuideHand.SetVisible(true);
                rightGuideHand.SetColorAndAlpha(guideHandColor, guideHandColor.a);
                if (rightGuideHand.Root != null)
                {
                    rightGuideHand.Root.position = refPos + refRot * firstFrame.rightRootPosition;
                    rightGuideHand.Root.rotation = refRot * firstFrame.rightRootRotation;
                }

                foreach (var kvp in firstFrame.rightLocalPoses)
                {
                    rightGuideHand.SetJointLocalPose(kvp.Key, kvp.Value.position, kvp.Value.rotation);
                }
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

    /// <summary>
    /// 해당 손이 실제로 녹화되었는지 확인.
    /// 첫 번째와 중간 프레임을 검사하여 조인트 데이터가 있는지 확인.
    /// 한 손만 녹화한 경우 다른 손은 localPoses가 비어있음.
    /// </summary>
    private static bool HasHandData(List<PoseFrame> frames, bool isLeft)
    {
        if (frames == null || frames.Count == 0) return false;

        // 첫 프레임과 중간 프레임 둘 다 검사 (안정성)
        int[] checkIndices = frames.Count > 1
            ? new[] { 0, frames.Count / 2 }
            : new[] { 0 };

        foreach (int idx in checkIndices)
        {
            var frame = frames[idx];
            var poses = isLeft ? frame.leftLocalPoses : frame.rightLocalPoses;
            if (poses != null && poses.Count > 0)
                return true;
        }
        return false;
    }
}
