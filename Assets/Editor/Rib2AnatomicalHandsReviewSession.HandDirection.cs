using UnityEngine;

public sealed partial class Rib2AnatomicalHandsReviewSession
{
    private readonly AnatomicalPatientRig.ArmPose[] presentationArms = new AnatomicalPatientRig.ArmPose[FrameCount];
    public const float RequestedWristExtensionDegrees = 30f;
    private float minimumRequestedPalmOutwardDot = 1f;
    private float minimumMeasuredDorsalBend = float.PositiveInfinity, maximumMeasuredDorsalBend;

    private void InitializeRequestedHandDirection()
    {
        float previousRoll = 0f;
        for (int i = 0; i < FrameCount; i++)
        {
            var pose = sampledArms[i];
            pose.forearmRotation = 0f;
            pose.wristFlexion = 0f;
            pose.wristDeviation = 0f;
            Evaluate(pose);
            Vector3 neutralDistal = fingers.DistalWorld, neutralPalmar = fingers.PalmarWorld;
            pose.wristFlexion = -RequestedWristExtensionDegrees;
            Evaluate(pose);
            float dorsal = Mathf.Atan2(-Vector3.Dot(fingers.DistalWorld, neutralPalmar), Vector3.Dot(fingers.DistalWorld, neutralDistal)) * Mathf.Rad2Deg;
            minimumMeasuredDorsalBend = Mathf.Min(minimumMeasuredDorsalBend, dorsal);
            maximumMeasuredDorsalBend = Mathf.Max(maximumMeasuredDorsalBend, dorsal);

            Vector3 forearmAxis = (fingers.Wrist.position - lower.position).normalized;
            Vector3 outward = (fingers.Wrist.position - supportBodyFrame.position).normalized;
            // Only the perpendicular component can be changed by forearm roll.
            // Remove its uncontrollable axial component for the angle solve;
            // the full 3D outward dot is still measured after actual native FK.
            Vector3 currentPlane = Vector3.ProjectOnPlane(fingers.PalmarWorld, forearmAxis);
            Vector3 targetPlane = Vector3.ProjectOnPlane(outward, forearmAxis);
            float roll = i > 0 ? previousRoll : sampledArms[i].forearmRotation;
            if (currentPlane.sqrMagnitude > 1e-8f && targetPlane.sqrMagnitude > 1e-6f)
            {
                float solved = Vector3.SignedAngle(currentPlane, targetPlane, forearmAxis);
                roll = i > 0 ? previousRoll + Mathf.DeltaAngle(previousRoll, solved) : solved;
            }
            pose.forearmRotation = roll;
            presentationArms[i] = pose;
            previousRoll = roll;
            Evaluate(pose);
            minimumRequestedPalmOutwardDot = Mathf.Min(minimumRequestedPalmOutwardDot, Vector3.Dot(fingers.PalmarWorld, outward));
        }
        Debug.Log("[Rib2HandDirection] User requested moving patient hand: palm away from chest centre toward hand; wrist extension=" +
            RequestedWristExtensionDegrees + "deg; actual native dorsal bend=" + minimumMeasuredDorsalBend.ToString("F2") + ".." + maximumMeasuredDorsalBend.ToString("F2") +
            "deg; minimum full outward dot=" + minimumRequestedPalmOutwardDot.ToString("F4") + ". Shoulder/elbow/wrist positions unchanged before motion smoothing.");
    }
}
