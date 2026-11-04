using System;
using UnityEngine;

public sealed partial class Rib2AnatomicalHandsReviewSession
{
    public const int DefaultSmoothingPasses = 1;
    private readonly int smoothingPasses;
    public int MotionSmoothingPasses => smoothingPasses;
    private readonly AnimationCurve[] smoothArmCurves = new AnimationCurve[9];
    private readonly AnimationCurve[] smoothGuidePositionCurves = new AnimationCurve[3];
    private readonly Quaternion[] smoothGuideRotations = new Quaternion[FrameCount];
    private readonly Quaternion[] smoothGuideControls = new Quaternion[FrameCount];

    private void InitializeSmoothMotion()
    {
        for (int channel = 0; channel < 9; channel++)
        {
            var values = new float[FrameCount];
            for (int i = 0; i < FrameCount; i++)
            {
                float raw = ArmChannel(presentationArms[i], channel);
                bool wrapped = channel == 1 || channel == 2 || channel == 4;
                values[i] = wrapped && i > 0 ? values[i - 1] + Mathf.DeltaAngle(values[i - 1], raw) : raw;
            }
            smoothArmCurves[channel] = MotionCurve(FilterMotion(values));
        }
        for (int axis = 0; axis < 3; axis++)
        {
            var values = new float[FrameCount];
            for (int i = 0; i < FrameCount; i++) values[i] = solvedRelativeGuidePositions[i][axis];
            smoothGuidePositionCurves[axis] = MotionCurve(FilterMotion(values));
        }
        for (int i = 0; i < FrameCount; i++)
        {
            Quaternion q = solvedRelativeGuideRotations[i].normalized;
            smoothGuideRotations[i] = i > 0 && Quaternion.Dot(smoothGuideRotations[i - 1], q) < 0f ? Negate(q) : q;
        }
        var buffer = new Quaternion[FrameCount];
        for (int pass = 0; pass < smoothingPasses; pass++)
        {
            Array.Copy(smoothGuideRotations, buffer, FrameCount);
            for (int i = 1; i < FrameCount - 1; i++)
            {
                Quaternion a = buffer[i - 1], b = buffer[i], c = buffer[i + 1];
                smoothGuideRotations[i] = new Quaternion((a.x + 2f * b.x + c.x) * .25f, (a.y + 2f * b.y + c.y) * .25f,
                    (a.z + 2f * b.z + c.z) * .25f, (a.w + 2f * b.w + c.w) * .25f).normalized;
            }
        }
        smoothGuideControls[0] = smoothGuideRotations[0];
        smoothGuideControls[FrameCount - 1] = smoothGuideRotations[FrameCount - 1];
        for (int i = 1; i < FrameCount - 1; i++)
        {
            Quaternion q = smoothGuideRotations[i], inverse = Quaternion.Inverse(q);
            Vector3 tangent = -.25f * (RotationLog(inverse * smoothGuideRotations[i - 1]) + RotationLog(inverse * smoothGuideRotations[i + 1]));
            smoothGuideControls[i] = (q * RotationExp(tangent)).normalized;
        }
        Debug.Log("[Rib2SmoothMotion] Source 30Hz samples retained; zero-phase three-tap passes=" + smoothingPasses +
            "; monotone cubic joint curves and quaternion SQUAD. Actual path deviation is measured separately; no elapsed-time clipping.");
    }

    private float[] FilterMotion(float[] values)
    {
        var buffer = new float[FrameCount];
        for (int pass = 0; pass < smoothingPasses; pass++)
        {
            Array.Copy(values, buffer, FrameCount);
            // End poses stay exact. Centred weights remove sample jitter without
            // introducing a time lag; spatial deviations require native checks.
            for (int i = 1; i < FrameCount - 1; i++) values[i] = (buffer[i - 1] + 2f * buffer[i] + buffer[i + 1]) * .25f;
        }
        return values;
    }

    private static AnimationCurve MotionCurve(float[] values)
    {
        var keys = new Keyframe[FrameCount];
        for (int i = 0; i < FrameCount; i++)
        {
            float before = i > 0 ? values[i] - values[i - 1] : values[1] - values[0];
            float after = i < FrameCount - 1 ? values[i + 1] - values[i] : before;
            // Zero tangent at extrema removes cubic overshoot. Otherwise the
            // harmonic mean retains direction and gives continuous velocity.
            float tangent = before * after <= 0f ? 0f : 2f * before * after / (before + after);
            tangent *= Fps;
            keys[i] = new Keyframe(i / (float)Fps, values[i], tangent, tangent);
        }
        return new AnimationCurve(keys) { preWrapMode = WrapMode.ClampForever, postWrapMode = WrapMode.ClampForever };
    }

    private AnatomicalPatientRig.ArmPose SmoothArm(float time)
    {
        return new AnatomicalPatientRig.ArmPose {
            shoulderElevation = smoothArmCurves[0].Evaluate(time), elevationPlane = smoothArmCurves[1].Evaluate(time),
            humeralRotation = smoothArmCurves[2].Evaluate(time), elbowFlexion = smoothArmCurves[3].Evaluate(time),
            forearmRotation = smoothArmCurves[4].Evaluate(time), wristFlexion = smoothArmCurves[5].Evaluate(time),
            wristDeviation = smoothArmCurves[6].Evaluate(time), shrug = smoothArmCurves[7].Evaluate(time), protraction = smoothArmCurves[8].Evaluate(time)
        };
    }

    private Vector3 SmoothGuidePosition(float time) => new Vector3(smoothGuidePositionCurves[0].Evaluate(time), smoothGuidePositionCurves[1].Evaluate(time), smoothGuidePositionCurves[2].Evaluate(time));

    private Quaternion SmoothGuideRotation(int lo, int hi, float t)
    {
        Quaternion direct = Quaternion.Slerp(smoothGuideRotations[lo], smoothGuideRotations[hi], t);
        Quaternion controls = Quaternion.Slerp(smoothGuideControls[lo], smoothGuideControls[hi], t);
        return Quaternion.Slerp(direct, controls, 2f * t * (1f - t));
    }

    private static Quaternion Negate(Quaternion q) => new Quaternion(-q.x, -q.y, -q.z, -q.w);
    private static Vector3 RotationLog(Quaternion q)
    {
        q = q.normalized;
        if (q.w < 0f) q = Negate(q);
        float length = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z);
        return length < 1e-7f ? Vector3.zero : new Vector3(q.x, q.y, q.z) * (Mathf.Atan2(length, q.w) / length);
    }
    private static Quaternion RotationExp(Vector3 value)
    {
        float length = value.magnitude;
        if (length < 1e-7f) return Quaternion.identity;
        float factor = Mathf.Sin(length) / length;
        return new Quaternion(value.x * factor, value.y * factor, value.z * factor, Mathf.Cos(length));
    }
    private static float ArmChannel(AnatomicalPatientRig.ArmPose pose, int channel)
    {
        switch (channel)
        {
            case 0: return pose.shoulderElevation;
            case 1: return pose.elevationPlane;
            case 2: return pose.humeralRotation;
            case 3: return pose.elbowFlexion;
            case 4: return pose.forearmRotation;
            case 5: return pose.wristFlexion;
            case 6: return pose.wristDeviation;
            case 7: return pose.shrug;
            default: return pose.protraction;
        }
    }
}
