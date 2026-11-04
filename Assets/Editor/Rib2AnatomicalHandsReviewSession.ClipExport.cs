using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// V03 환자 팔 자세를 몸 기준 방향으로 내보낸다(2026-11-02 · c9 클립 굽기용).
/// 좌우는 본 이름을 믿지 않고 발끝 방향으로 잰 해부학 기준으로 적는다. 읽기만 하고 아무것도 저장하지 않는다.
/// </summary>
public sealed partial class Rib2AnatomicalHandsReviewSession
{
    public struct ClipTargetFrame
    {
        public float seconds;
        public Vector3 upper, lower, distal, radial;   // (앞, 머리, 해부학 왼쪽) 성분
    }

    /// <summary>바디캠 카메라 자료는 리그 좌표라 c9 렌더로 옮기려면 리그의 몸 기준틀이 필요하다.</summary>
    public static void MeasureRigFrame(out Rib2AnatomyFrame body, out Vector3 chest, out float shoulderWidth)
    {
        using (var session = new Rib2AnatomicalHandsReviewSession())
        {
            var animator = session.root.GetComponentInChildren<Animator>(true);
            Rib2AnatomyFrame.TryMeasure(animator, out body, out _);
            chest = session.supportBodyFrame.position;
            shoulderWidth = Vector3.Distance(animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).position, animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position);
        }
    }

    public static bool ExportClipTargets(out List<ClipTargetFrame> frames, out bool movedArmIsAnatomicalLeft, out float fps)
    {
        frames = new List<ClipTargetFrame>(FrameCount);
        fps = Fps;
        using (var session = new Rib2AnatomicalHandsReviewSession())
        {
            var animator = session.root.GetComponentInChildren<Animator>(true);
            Rib2AnatomyFrame.TryMeasure(animator, out var body, out bool nameLeftIsAnatomicalLeft);
            movedArmIsAnatomicalLeft = session.fit.affectedLeft == nameLeftIsAnatomicalLeft;
            for (int i = 0; i < FrameCount; i++)
            {
                float t = i / (float)Fps;
                session.Sample(t, false);
                Vector3 s = session.upper.position, e = session.lower.position, w = session.fingers.Wrist.position;
                frames.Add(new ClipTargetFrame {
                    seconds = t,
                    upper = body.ToBody((e - s).normalized), lower = body.ToBody((w - e).normalized),
                    distal = body.ToBody(session.fingers.DistalWorld), radial = body.ToBody(session.fingers.RadialWorld)
                });
            }
        }
        return frames.Count > 0;
    }
}

/// <summary>몸 기준틀 — 앞 · 머리 · 해부학 왼쪽. 왼쪽은 «이름이 왼쪽인 본»이 아니라 앞 × 머리(앞 = 발끝으로 잰 방향)다.</summary>
public struct Rib2AnatomyFrame
{
    public Vector3 front, head, left;

    public Vector3 ToBody(Vector3 world) => new Vector3(Vector3.Dot(world, front), Vector3.Dot(world, head), Vector3.Dot(world, left));
    public Vector3 ToWorld(Vector3 body) => front * body.x + head * body.y + left * body.z;

    public static bool TryMeasure(Animator animator, out Rib2AnatomyFrame frame, out bool nameLeftIsAnatomicalLeft)
    {
        frame = default; nameLeftIsAnatomicalLeft = true;
        Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips), head = animator.GetBoneTransform(HumanBodyBones.Head);
        Transform l = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm), r = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        Transform foot = animator.GetBoneTransform(HumanBodyBones.LeftFoot), toes = animator.GetBoneTransform(HumanBodyBones.LeftToes);
        if (hips == null || head == null || l == null || r == null || foot == null || toes == null) return false;
        Vector3 up = (head.position - hips.position).normalized;
        Vector3 toe = Vector3.ProjectOnPlane(toes.position - foot.position, up).normalized;
        Vector3 nameLeft = Vector3.ProjectOnPlane(l.position - r.position, up).normalized;
        // 해부학: 왼쪽 = 앞 × 머리(Unity 왼손 좌표 · 11-02 실측: 머리 +Y · 앞 +Z면 왼쪽 −X). 이름상 왼쪽이 그 반대면 좌우가 뒤집힌 모델이다.
        Vector3 left = Vector3.Cross(toe, up).normalized;
        nameLeftIsAnatomicalLeft = Vector3.Dot(left, nameLeft) > 0f;
        left = nameLeftIsAnatomicalLeft ? nameLeft : -nameLeft;
        frame = new Rib2AnatomyFrame { head = up, left = left, front = Vector3.Cross(up, left).normalized };
        return true;
    }
}
