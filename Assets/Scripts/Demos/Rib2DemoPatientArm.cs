using UnityEngine;

/// <summary>Single writer of the cloned patient's right arm. Presentation only; no assessment calls.</summary>
public sealed class Rib2DemoPatientArm : MonoBehaviour
{
    [SerializeField] private Transform upperArm = null;
    [SerializeField] private Transform forearm = null;
    [SerializeField] private Transform patientHand = null;
    [SerializeField] private Transform wristTarget = null;
    [SerializeField] private Transform elbowHint = null;
    [SerializeField] private Transform motionFrame = null;
    [SerializeField] private Rib2VideoTextScenePlayer player = null;
    [SerializeField] private Transform guideHandRoot = null;
    [SerializeField] private float[] hintTimes = new float[0];
    [SerializeField] private Vector3[] hintPositions = new Vector3[0];

    private bool initialized;
    // Authoring data, not tuning overrides: preserve the original model's rest pose
    // when a posed scene is saved and reopened. Recapturing posed bones changes twist.
    [SerializeField, HideInInspector] private bool referencePoseRecorded = false;
    [SerializeField, HideInInspector] private Quaternion upperRest = Quaternion.identity;
    [SerializeField, HideInInspector] private Quaternion forearmRest = Quaternion.identity;
    [SerializeField, HideInInspector] private Quaternion handRest = Quaternion.identity;
    private string lastError;
    private bool warnedReach;
    private float maximumReachCorrection;
    private float maximumWristError;

    public float MaximumReachCorrection => maximumReachCorrection;
    public float MaximumWristError => maximumWristError;
    public Transform Wrist => patientHand;
    public Transform Target => wristTarget;

    private void Awake() { Initialize(out _); }
    private void LateUpdate() { if (Application.isPlaying) Evaluate(); }

    public void CaptureReferencePose(Transform referenceUpper, Transform referenceForearm, Transform referenceHand)
    {
        if (referencePoseRecorded) throw new System.InvalidOperationException("The original model reference pose is already recorded.");
        if (referenceUpper == null || referenceForearm == null || referenceHand == null)
            throw new System.ArgumentException("All three original model reference bones are required.");
        upperRest = referenceUpper.localRotation;
        forearmRest = referenceForearm.localRotation;
        handRest = referenceHand.localRotation;
        referencePoseRecorded = true;
        initialized = false;
    }

    public bool Initialize(out string error)
    {
        error = null;
        if (initialized) return true;
        if (upperArm == null || forearm == null || patientHand == null || wristTarget == null ||
            elbowHint == null || motionFrame == null || player == null || guideHandRoot == null)
            error = "A required cloned arm, guide, player or coordinate reference is missing.";
        else if (!forearm.IsChildOf(upperArm) || !patientHand.IsChildOf(forearm) || !wristTarget.IsChildOf(guideHandRoot))
            error = "Arm hierarchy or the grip target parent is invalid.";
        else if (upperArm.parent == null || forearm.parent == null || upperArm.localToWorldMatrix.determinant <= 0f)
            error = "The demonstration patient must use an unmirrored rig.";
        else if (!referencePoseRecorded)
            error = "Original model reference rotations are missing. Record them once with the separate scene authoring tool.";
        else if (hintTimes == null || hintPositions == null || hintTimes.Length != hintPositions.Length || hintTimes.Length < 2)
            error = "Video elbow observations are not wired.";
        else
        {
            for (int i = 0; i < hintTimes.Length; i++)
                if (!Finite(hintTimes[i]) || !Finite(hintPositions[i]) || (i > 0 && hintTimes[i] <= hintTimes[i - 1]))
                { error = "Elbow observation time/position is invalid."; break; }
        }
        if (error != null) return Fail(error);
        float upperLength = Vector3.Distance(upperArm.position, forearm.position);
        float lowerLength = Vector3.Distance(forearm.position, patientHand.position);
        if (upperLength < .01f || lowerLength < .01f)
        { error = "Measured cloned arm segment length is too small."; return Fail(error); }
        initialized = true;
        lastError = null;
        Debug.Log("[Rib2SceneArm] Initialized clone-only two-bone presentation: upper=" + upperLength.ToString("F4") +
            "m; lower=" + lowerLength.ToString("F4") + "m; hint keys=" + hintTimes.Length +
            "; source depth/hand grip are authored assumptions; no clinical angle or scoring.", this);
        return true;
    }

    /// <summary>Same solver for runtime LateUpdate and explicit Editor preview.</summary>
    public bool Evaluate()
    {
        if (!Initialize(out _)) return false;
        // One writer owns these three cloned bones. Resetting avoids accumulated rotation drift.
        upperArm.localRotation = upperRest;
        forearm.localRotation = forearmRest;
        patientHand.localRotation = handRest;
        Vector3 shoulder = upperArm.position;
        Vector3 input = wristTarget.position;
        Vector3 delta = input - shoulder;
        if (!Finite(delta) || delta.sqrMagnitude < .000001f) return Fail("Wrist target coincides with the shoulder or is not finite.");
        float a = Vector3.Distance(shoulder, forearm.position);
        float b = Vector3.Distance(forearm.position, patientHand.position);
        float requestedDistance = delta.magnitude;
        // Removes unreachable distance only to preserve this mesh's measured bone lengths.
        // This is a presentation reach constraint, never a pass angle or patient ROM judgement.
        float distance = Mathf.Clamp(requestedDistance, Mathf.Abs(a - b) + .0001f, a + b - .0001f);
        Vector3 direction = delta / requestedDistance;
        Vector3 hint = SampleHint(player.CurrentTime);
        elbowHint.position = hint;
        Vector3 hintDelta = hint - shoulder;
        // Removes the pole's component along the arm axis; the wrist target retains all XYZ.
        Vector3 bend = hintDelta - direction * Vector3.Dot(hintDelta, direction);
        if (bend.sqrMagnitude < .000001f)
            bend = Vector3.Cross(direction, motionFrame.forward);
        if (bend.sqrMagnitude < .000001f) return Fail("Elbow observation cannot determine a bend plane.");
        bend.Normalize();
        float along = (a * a + distance * distance - b * b) / (2f * distance);
        // Roundoff below zero is discarded; positive lengths remain measured from the clone.
        float height = Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
        Vector3 desiredElbow = shoulder + direction * along + bend * height;
        Vector3 solvedTarget = shoulder + direction * distance;
        AimInParentSpace(upperArm, forearm, desiredElbow);
        AimInParentSpace(forearm, patientHand, solvedTarget);
        float correction = Vector3.Distance(input, solvedTarget);
        maximumReachCorrection = Mathf.Max(maximumReachCorrection, correction);
        float solverError = Vector3.Distance(patientHand.position, solvedTarget);
        maximumWristError = Mathf.Max(maximumWristError, solverError);
        if (solverError > .005f) return Fail("Cloned wrist solver residual exceeds 5 mm: " + solverError.ToString("F4"));
        // Keep the displayed grip attached after mesh-reach retargeting. Source CSV is unchanged.
        guideHandRoot.position += patientHand.position - input;
        if (correction > .03f && !warnedReach)
        {
            warnedReach = true;
            Debug.LogWarning("[Rib2SceneArm] Source display path exceeded cloned arm reach by " + correction.ToString("F4") +
                "m; guide grip was attached to the solved wrist. This is authored retargeting, not a changed pass condition.", this);
        }
        lastError = null;
        return true;
    }

    private Vector3 SampleHint(float time)
    {
        int low = 0;
        int high = hintTimes.Length - 1;
        while (low + 1 < high)
        {
            int middle = (low + high) / 2;
            if (hintTimes[middle] <= time) low = middle; else high = middle;
        }
        float blend = Mathf.InverseLerp(hintTimes[low], hintTimes[high], time);
        return motionFrame.TransformPoint(Vector3.Lerp(hintPositions[low], hintPositions[high], blend));
    }

    private static void AimInParentSpace(Transform joint, Transform end, Vector3 target)
    {
        // Parent-space vectors include the actual parent transform; no guessed bone-axis sign.
        Vector3 from = joint.parent.InverseTransformVector(end.position - joint.position);
        Vector3 to = joint.parent.InverseTransformVector(target - joint.position);
        joint.localRotation = Quaternion.FromToRotation(from, to) * joint.localRotation;
    }

    private bool Fail(string error)
    {
        if (lastError != error) { lastError = error; Debug.LogWarning("[Rib2SceneArm] " + error, this); }
        return false;
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
}
