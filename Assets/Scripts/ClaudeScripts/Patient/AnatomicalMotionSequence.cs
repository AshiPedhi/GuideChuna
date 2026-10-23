using UnityEngine;

/// <summary>
/// Reproducible joint-space studies for the independent patient rig. The editor
/// preview and exported studies call this same sampler and Rig.Evaluate(Pose).
/// These are presentation trajectories, not clinical ROM limits or recovered
/// motion capture. In particular Rib2Study preserves the observed bent elbow
/// and neutral wrist; its angles and timing are explicitly authoring estimates.
/// </summary>
public static class AnatomicalMotionSequence
{
    public enum Motion
    {
        Elbow, Forearm, WristFlexion, WristDeviation, CoupledWrist,
        ShoulderFlexion, ShoulderAbduction, ShoulderRotation,
        NeckFlexion, NeckSidebend, NeckRotation, CompositeNeck, Rib2Study
    }

    public static float Duration(Motion motion) => motion == Motion.Rib2Study ? 34f : 10f;

    public static string Label(Motion motion)
    {
        switch (motion)
        {
            case Motion.Elbow: return "팔꿈치 굽힘·폄";
            case Motion.Forearm: return "전완 회전";
            case Motion.WristFlexion: return "손목 장굴·배굴";
            case Motion.WristDeviation: return "손목 요측·척측 편위";
            case Motion.CoupledWrist: return "전완 회전 + 굽힌 손목";
            case Motion.ShoulderFlexion: return "어깨 전방 거상";
            case Motion.ShoulderAbduction: return "어깨 외전";
            case Motion.ShoulderRotation: return "굽힌 팔의 위팔 회전";
            case Motion.NeckFlexion: return "목 굴곡·신전";
            case Motion.NeckSidebend: return "목 좌우 측굴";
            case Motion.NeckRotation: return "목 좌우 회전";
            case Motion.CompositeNeck: return "목 복합 움직임";
            case Motion.Rib2Study: return "제2늑골 영상 참고 동작 (각도 근사)";
            default: return motion.ToString();
        }
    }

    public static string PhaseLabel(Motion motion, float seconds)
    {
        if (motion == Motion.Rib2Study)
        {
            if (seconds < 2f) return "준비";
            if (seconds < 6f) return "파지·팔을 옆으로 이동";
            if (seconds < 30f)
            {
                int repetition = Mathf.Min(3, 1 + (int)((seconds - 6f) / 8f));
                bool outward = (seconds - 6f) % 8f < 4f;
                return "반복 " + repetition + "/3 · " + (outward ? "벌린 팔 이동" : "굽힌 자세로 복귀");
            }
            return "파지 해제·원위치";
        }
        float fraction = Mathf.Clamp01(seconds / Duration(motion));
        if (fraction < .1f || fraction > .9f) return "기준 자세";
        if (fraction < .45f) return "정방향 이동";
        return "반대 방향·복귀";
    }

    public static AnatomicalPatientRig.Pose Sample(AnatomicalPatientRig.Pose rest,
        Motion motion, float seconds, bool left)
    {
        // Removed: time outside this one study. Looping belongs to the caller;
        // sampling the exact final time must remain the final, reproducible pose.
        float time = Mathf.Clamp(seconds, 0f, Duration(motion));
        var pose = rest;
        pose.neckDegrees = Vector3.zero;
        pose.leftArm = RelaxedArm();
        pose.rightArm = RelaxedArm();
        var arm = left ? pose.leftArm : pose.rightArm;
        float sign = left ? 1f : -1f; // mirrored proximal-to-distal axial rotations
        float wave = SignedWave(time / Duration(motion));
        float raise = RaisedWave(time / Duration(motion));
        switch (motion)
        {
            case Motion.Elbow:
                arm.shoulderElevation = 30f;
                arm.elbowFlexion = Mathf.Lerp(10f, 135f, raise);
                break;
            case Motion.Forearm:
                arm.elbowFlexion = 90f;
                arm.forearmRotation = sign * wave * 80f;
                break;
            case Motion.WristFlexion:
                arm.elbowFlexion = 90f;
                arm.wristFlexion = wave * 45f;
                break;
            case Motion.WristDeviation:
                arm.elbowFlexion = 90f;
                arm.wristDeviation = wave * 20f;
                break;
            case Motion.CoupledWrist:
                arm.elbowFlexion = 90f;
                arm.forearmRotation = sign * wave * 80f;
                arm.wristFlexion = 35f;
                arm.wristDeviation = 15f;
                break;
            case Motion.ShoulderFlexion:
                arm.elevationPlane = 0f;
                arm.shoulderElevation = Mathf.Lerp(10f, 145f, raise);
                break;
            case Motion.ShoulderAbduction:
                arm.shoulderElevation = Mathf.Lerp(10f, 150f, raise);
                break;
            case Motion.ShoulderRotation:
                arm.shoulderElevation = 55f;
                arm.elbowFlexion = 90f;
                arm.humeralRotation = sign * wave * 55f;
                break;
            case Motion.NeckFlexion: pose.neckDegrees.x = wave * 30f; break;
            case Motion.NeckSidebend: pose.neckDegrees.y = wave * 25f; break;
            case Motion.NeckRotation: pose.neckDegrees.z = wave * 55f; break;
            case Motion.CompositeNeck:
                pose.neckDegrees = new Vector3(20f * wave, 15f * raise, 35f * wave);
                break;
            case Motion.Rib2Study:
                arm = Rib2Arm(time, sign);
                // A head turn is visible in the source; its angle is not measured.
                pose.neckDegrees.z = (left ? -1f : 1f) * 20f *
                    (time < 6f ? Smooth(time / 6f) : 1f - Smooth((time - 30f) / 4f));
                break;
        }
        if (left) pose.leftArm = arm;
        else pose.rightArm = arm;
        return pose;
    }

    private static AnatomicalPatientRig.ArmPose RelaxedArm()
    {
        return new AnatomicalPatientRig.ArmPose
        {
            shoulderElevation = 10f, elevationPlane = 90f, elbowFlexion = 10f
        };
    }

    private static AnatomicalPatientRig.ArmPose Rib2Arm(float time, float sign)
    {
        var neutral = RelaxedArm();
        // Two views show an abducted, persistently bent arm. They do not justify
        // the earlier 150-degree overhead/30-degree elbow endpoint. This study
        // uses a small excursion while keeping the wrist neutral to the forearm.
        // Raw humeral roll is checked against the measured elbow pole by the
        // output-bone verification; it is not an Euler guess about c9 bone axes.
        var held = new AnatomicalPatientRig.ArmPose
        {
            shoulderElevation = 80f, elevationPlane = 90f,
            elbowFlexion = 100f, humeralRotation = sign * 85f
        };
        if (time <= 2f) return neutral;
        if (time < 6f) return Lerp(neutral, held, Smooth((time - 2f) / 4f));
        if (time < 30f)
        {
            float cycle = (time - 6f) % 8f;
            float reach = cycle <= 4f ? Smooth(cycle / 4f) : 1f - Smooth((cycle - 4f) / 4f);
            var extended = held;
            extended.shoulderElevation = 100f;
            extended.elbowFlexion = 85f;
            return Lerp(held, extended, reach);
        }
        return Lerp(held, neutral, Smooth((time - 30f) / 4f));
    }

    private static AnatomicalPatientRig.ArmPose Lerp(AnatomicalPatientRig.ArmPose a,
        AnatomicalPatientRig.ArmPose b, float t)
    {
        return new AnatomicalPatientRig.ArmPose
        {
            shoulderElevation = Mathf.Lerp(a.shoulderElevation, b.shoulderElevation, t),
            elevationPlane = Mathf.Lerp(a.elevationPlane, b.elevationPlane, t),
            humeralRotation = Mathf.Lerp(a.humeralRotation, b.humeralRotation, t),
            elbowFlexion = Mathf.Lerp(a.elbowFlexion, b.elbowFlexion, t),
            forearmRotation = Mathf.Lerp(a.forearmRotation, b.forearmRotation, t),
            wristFlexion = Mathf.Lerp(a.wristFlexion, b.wristFlexion, t),
            wristDeviation = Mathf.Lerp(a.wristDeviation, b.wristDeviation, t),
            shrug = Mathf.Lerp(a.shrug, b.shrug, t), protraction = Mathf.Lerp(a.protraction, b.protraction, t)
        };
    }

    private static float SignedWave(float t)
    {
        if (t < .1f || t > .9f) return 0f;
        if (t < .3f) return Smooth((t - .1f) / .2f);
        if (t < .5f) return 1f - Smooth((t - .3f) / .2f);
        if (t < .7f) return -Smooth((t - .5f) / .2f);
        return -1f + Smooth((t - .7f) / .2f);
    }

    private static float RaisedWave(float t)
    {
        if (t < .1f || t > .9f) return 0f;
        if (t < .4f) return Smooth((t - .1f) / .3f);
        if (t < .6f) return 1f;
        return 1f - Smooth((t - .6f) / .3f);
    }

    private static float Smooth(float t)
    {
        // Removed: overshoot between explicit authoring endpoints, not a ROM limit.
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }
}
