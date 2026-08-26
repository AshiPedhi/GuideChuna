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

            ApplyFrame(frames[currentGuideFrameIndex], leftGuideHand, rightGuideHand, hasLeftData, hasRightData);

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
    /// 한 프레임을 가이드 손에 얹는다. 기준 트랜스폼(환자) 기준의 상대 좌표라
    /// 환자가 움직이면 가이드도 같이 따라간다.
    /// </summary>
    private void ApplyFrame(PoseFrame frame,
                            HandTransformMapper leftGuideHand, HandTransformMapper rightGuideHand,
                            bool hasLeftData, bool hasRightData)
    {
        // referenceTransform이 매 프레임 현재 환자 위치/회전을 들고 있다고 가정.
        Transform refT = owner.ReferenceTransform;
        Vector3 refPos = refT != null ? refT.position : Vector3.zero;
        Quaternion refRot = refT != null ? refT.rotation : Quaternion.identity;

        // ★접촉해도 <b>재생은 계속 간다</b>. 끄는 건 렌더러뿐이다(2026-08-26 사용자 지시).
        //   예전엔 가려진 손에 자세를 아예 안 얹어서, 접촉이 풀리면 그동안 지나간 구간이
        //   날아간 채 중간부터 이어졌다 — "재생이 다 안 된 상태로 굳는다"의 정체다.
        if (leftGuideHand != null && hasLeftData)
        {
            leftGuideHand.SetVisible(!owner.IsGuideHandSuppressed(true));
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

        if (rightGuideHand != null && hasRightData)
        {
            rightGuideHand.SetVisible(!owner.IsGuideHandSuppressed(false));
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
    }

    /// <summary>
    /// 가이드 손을 <b>환자 머리뼈의 자식으로 넣는다</b>. 자세는 건드리지 않는다 —
    /// 지금 서 있는 그대로 붙는다(<c>worldPositionStays</c>).
    ///
    /// ★붙이는 것만으로는 안 따라간다. 재생 루틴이 매 프레임 <b>월드 위치를 써 넣기</b> 때문에,
    ///   부모가 무엇이든 그 자리로 끌려간다(2026-08-26 사용자 지적 — 정확했다).
    ///   그래서 순서가 중요하다: 붙인다 → <b>1회 재생</b> → 재생이 끝나 쓰는 쪽이 사라지면
    ///   그때부터 계층이 손을 끌고 간다.
    /// </summary>
    /// <returns>한 손이라도 붙었으면 true.</returns>
    public bool AttachHandsToHead(
        List<PoseFrame> frames,
        HandTransformMapper leftGuideHand, HandTransformMapper rightGuideHand,
        Transform anchor)
    {
        if (anchor == null)
        {
            ChunaLogger.LogWarning("[Guide] 붙일 뼈가 없습니다 — 가이드 손이 고개를 따라가지 않습니다.");
            return false;
        }

        bool hasLeftData = HasHandData(frames, true);
        bool hasRightData = HasHandData(frames, false);

        bool any = false;
        any |= AttachOne(leftGuideHand, hasLeftData, anchor);
        any |= AttachOne(rightGuideHand, hasRightData, anchor);
        return any;
    }

    private static bool AttachOne(HandTransformMapper hand, bool hasData, Transform anchor)
    {
        if (hand == null || !hasData || hand.Root == null) return false;

        var follower = hand.GetComponent<GuideHandHeadFollower>();
        if (follower == null) follower = hand.gameObject.AddComponent<GuideHandHeadFollower>();
        follower.Attach(hand.Root, anchor);
        return true;
    }

    /// <summary>
    /// 마지막 프레임 자세를 강제로 얹는다. 파지 단계가 끝날 때 부른다 —
    /// 재생이 중간에 있어도 최종 자세로 맞춰야 그 뒤 머리 추종이 제 모양이 된다.
    /// </summary>
    public void ApplyLastFrame(
        List<PoseFrame> frames,
        HandTransformMapper leftGuideHand, HandTransformMapper rightGuideHand)
    {
        if (frames == null || frames.Count == 0) return;

        currentGuideFrameIndex = frames.Count - 1;
        ApplyFrame(frames[currentGuideFrameIndex], leftGuideHand, rightGuideHand,
                   HasHandData(frames, true), HasHandData(frames, false));
    }

    /// <summary>붙여 둔 가이드 손을 뗀다. 손은 마지막 자리에 남는다.</summary>
    public static void DetachFromHead(HandTransformMapper leftGuideHand, HandTransformMapper rightGuideHand)
    {
        leftGuideHand?.GetComponent<GuideHandHeadFollower>()?.Detach();
        rightGuideHand?.GetComponent<GuideHandHeadFollower>()?.Detach();
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
