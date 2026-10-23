using System;
using UnityEngine;

/// <summary>
/// Experimental anatomical output rig for the c9 skin and Anatomy meshes.
/// The c9 Animator/IK bones are inputs only. Every output is an independent child
/// of rigSpace, so skin, rigid bones and muscles can share one final pose.
///
/// Joint centres/bases/rest poses are measured by the editor builder. The shoulder
/// rhythm and cervical distribution below are kinematic approximations, not a
/// validated muscle-force, ligament/contact or patient-specific biomechanical model.
/// </summary>
[DefaultExecutionOrder(20000)]
public sealed class AnatomicalPatientRig : MonoBehaviour
{
    public enum InputSource { Animation, Manual }
    public enum EvaluationMode { ExplicitOnly, LateUpdate }
    public enum CorrectionTrigger { Elevation, ElbowFlexion }

    [Serializable]
    public sealed class SkinCorrection
    {
        public SkinnedMeshRenderer renderer;
        public int blendShape;
        public bool left;
        public CorrectionTrigger trigger;
        [Tooltip("Trigger angle above the measured author rest at which the shape starts.")]
        public float startDegrees;
        [Tooltip("Trigger angle above the measured author rest at which the shape reaches 100 percent.")]
        public float fullDegrees;
    }

    [Serializable]
    public struct Basis
    {
        public Vector3 right;
        public Vector3 up;
        public Vector3 forward;
    }

    [Serializable]
    public struct ArmPose
    {
        [Tooltip("0 = arm down, 90 = horizontal, 180 = overhead.")]
        public float shoulderElevation;
        [Tooltip("0 = anterior elevation, 90 = lateral elevation, on either side.")]
        public float elevationPlane;
        [Tooltip("Signed rotation about shoulder-to-elbow axis, relative to author rest.")]
        public float humeralRotation;
        public float elbowFlexion;
        [Tooltip("Signed rotation about the measured proximal-to-distal forearm axis, relative to author rest.")]
        public float forearmRotation;
        [Tooltip("Positive bends the measured finger direction toward the palm; negative extends toward the dorsum. Applied after forearm rotation.")]
        public float wristFlexion;
        [Tooltip("Positive bends toward the measured index/little radial direction (thumb side). Applied about the hand axis after wrist flexion.")]
        public float wristDeviation;
        [Tooltip("Additional clavicular elevation in degrees; independent of humeral elevation.")]
        public float shrug;
        [Tooltip("Additional shoulder-girdle protraction in degrees.")]
        public float protraction;
    }

    [Serializable]
    public struct Pose
    {
        public ArmPose leftArm;
        public ArmPose rightArm;
        [Tooltip("x = flexion, y = right lateral bending, z = right axial rotation. Degrees relative to rest.")]
        public Vector3 neckDegrees;
    }

    [Serializable]
    public struct RestTransform
    {
        public Vector3 position;
        public Quaternion rotation;
    }

    [Serializable]
    public sealed class BoneBinding
    {
        public Transform source;
        public Transform output;
        [HideInInspector] public RestTransform sourceRest;
        [HideInInspector] public RestTransform outputRest;
    }

    [Serializable]
    public sealed class Limb
    {
        public bool left;
        public Transform sourceClavicle;
        public Transform sourceUpper;
        public Transform sourceLower;
        public Transform sourceHand;
        public Transform clavicle;
        public Transform scapula;
        public Transform humerus;
        public Transform ulna;
        public Transform radius;
        public Transform hand;
        public Vector3 clavicleMedial;
        public Vector3 shoulder;
        public Vector3 elbow;
        public Vector3 wrist;
        public Vector3 scapulaCenter;
        [Tooltip("Measured rest forearm rotation axis. If coincident, the elbow/wrist axis is used and reported as an approximation.")]
        public Vector3 radiusAxisStart;
        public Vector3 radiusAxisEnd;
        [HideInInspector] public RestTransform[] sourceRest;
        [HideInInspector] public RestTransform[] outputRest;
        [HideInInspector] public ArmPose restPose;
        [HideInInspector] public Vector3 restBendDirection;
        [NonSerialized] internal int[] bindingRegions;
        [NonSerialized] internal float[] bindingTwistFractions;
        [NonSerialized] internal bool wristFrameReady;
        [NonSerialized] internal Vector3 wristFlexionAxis;
        [NonSerialized] internal Vector3 wristDeviationAxis;
    }

    [Serializable]
    public sealed class Neck
    {
        public Transform sourceChest;
        public Transform sourceNeck1;
        public Transform sourceNeck2;
        public Transform sourceHead;
        [Tooltip("Exactly seven measured cervical output bones, ordered caudal to cranial.")]
        public Transform[] segments;
        public Transform head;
        public Vector3 basePivot;
        [HideInInspector] public RestTransform chestRest;
        [HideInInspector] public RestTransform headSourceRest;
        [HideInInspector] public RestTransform[] segmentRest;
        [HideInInspector] public RestTransform headOutputRest;
        [HideInInspector] public Vector3[] jointPivots;
        [NonSerialized] internal int[] bindingRegions;
        [NonSerialized] internal int[] bindingSegments;
    }

    public struct Diagnostics
    {
        public float leftRequestedElevation;
        public float rightRequestedElevation;
        public float leftElevation;
        public float rightElevation;
        public float leftScapularUpward;
        public float rightScapularUpward;
        public float leftForearmRotation;
        public float rightForearmRotation;
        public Vector3 neckDegrees;
        public float headDisplacement;
        public float neckEndpointRotationError;
    }

    [SerializeField] private Transform rigSpace;
    [SerializeField] private Basis anatomicalBasis;
    [SerializeField] private BoneBinding[] bodyBindings;
    [SerializeField] private Limb[] limbs;
    [SerializeField] private Neck neck;
    [SerializeField] private InputSource inputSource;
    [SerializeField] private EvaluationMode evaluationMode;
    [SerializeField] private Pose restPose;
    [SerializeField] private SkinCorrection[] skinCorrections;
    [Tooltip("Independent-girdle, arm and neck controls. Defaults to the measured author rest, which may be a T-pose.")]
    public Pose manualPose;
    [SerializeField] private bool diagnostics;

    // Unity can preserve private fields across a script reload even without SerializeField.
    // These flags and the caches they describe must have the same nonserialized lifetime.
    [NonSerialized] private bool initialized;
    [NonSerialized] private bool valid;
    [NonSerialized] private string lastFailure;
    [NonSerialized] private float nextDiagnosticTime;
    [NonSerialized] private Diagnostics lastDiagnostics;
    [NonSerialized] private Pose currentPose;
    [NonSerialized] private RestTransform chestCurrent;
    [NonSerialized] private Quaternion bodyRotation;
    [NonSerialized] private Vector3 bodyTranslation;
    [NonSerialized] private Vector3[] neckPositions;
    [NonSerialized] private Quaternion[] neckRotations;

    // Approximate angular contributions; caudal -> cranial, each sum = 1.
    // The authoring report labels these as approximations. They are not joint ROM limits.
    private static readonly float[] FlexionShares = { .12f, .15f, .17f, .17f, .16f, .13f, .10f };
    private static readonly float[] LateralShares = { .12f, .15f, .17f, .17f, .16f, .13f, .10f };
    private static readonly float[] AxialShares = { .06f, .06f, .08f, .10f, .10f, .10f, .50f };
    private const float Epsilon = 0.000001f;

    public Pose RestPose => restPose;
    public Pose CurrentPose => currentPose;
    public Diagnostics LastDiagnostics => lastDiagnostics;
    public bool IsConfigured => initialized && valid;
    public Transform RigSpace => rigSpace;

    /// <summary>Editor builder only: call in measured neutral pose, before animation starts.</summary>
    public void Configure(Transform space, Basis measuredBasis, BoneBinding[] bindings,
        Limb[] limbDefinitions, Neck neckDefinition, SkinCorrection[] corrections = null)
    {
        rigSpace = space;
        anatomicalBasis = measuredBasis;
        bodyBindings = bindings;
        limbs = limbDefinitions;
        neck = neckDefinition;
        skinCorrections = corrections;
        initialized = false;
        valid = false;
        lastFailure = null;
        if (!ValidateReferences()) return;

        for (int i = 0; i < bodyBindings.Length; i++)
        {
            BoneBinding binding = bodyBindings[i];
            if (!TryRead(binding.source, out binding.sourceRest)) return;
            binding.outputRest = ReadOutput(binding.output);
        }

        if (!TryRead(neck.sourceChest, out neck.chestRest) ||
            !TryRead(neck.sourceHead, out neck.headSourceRest)) return;
        neck.segmentRest = new RestTransform[7];
        neck.jointPivots = new Vector3[7];
        for (int i = 0; i < 7; i++)
        {
            neck.segmentRest[i] = ReadOutput(neck.segments[i]);
            neck.jointPivots[i] = i == 0 ? neck.basePivot :
                (neck.segmentRest[i - 1].position + neck.segmentRest[i].position) * .5f;
        }
        neck.headOutputRest = ReadOutput(neck.head);

        restPose = new Pose();
        for (int i = 0; i < limbs.Length; i++)
        {
            Limb limb = limbs[i];
            limb.sourceRest = new RestTransform[4];
            if (!TryRead(limb.sourceClavicle, out limb.sourceRest[0]) ||
                !TryRead(limb.sourceUpper, out limb.sourceRest[1]) ||
                !TryRead(limb.sourceLower, out limb.sourceRest[2]) ||
                !TryRead(limb.sourceHand, out limb.sourceRest[3])) return;
            limb.outputRest = new RestTransform[6];
            limb.outputRest[0] = ReadOutput(limb.clavicle);
            limb.outputRest[1] = ReadOutput(limb.scapula);
            limb.outputRest[2] = ReadOutput(limb.humerus);
            limb.outputRest[3] = ReadOutput(limb.ulna);
            limb.outputRest[4] = ReadOutput(limb.radius);
            limb.outputRest[5] = ReadOutput(limb.hand);
            Vector3 upper = (limb.elbow - limb.shoulder).normalized;
            Vector3 lower = (limb.wrist - limb.elbow).normalized;
            // The builder measures this pole from a flexed c9 Humanoid pose. The
            // near-straight author arm has slight hyperextension and cannot select
            // the positive flexion direction (measured posterior before calibration).
            // Removed: the upper-arm component; the measured hinge pole is perpendicular.
            Vector3 bend = limb.restBendDirection - upper * Vector3.Dot(limb.restBendDirection, upper);
            if (bend.sqrMagnitude < Epsilon) { Fail("Measured elbow flexion pole is missing. Run the anatomical builder."); return; }
            limb.restBendDirection = bend.normalized;
            limb.restPose = ReadArmAngles(limb, upper, lower);
            limb.restPose.elbowFlexion = SignedElbowFlexion(upper, lower, limb.restBendDirection);
            if (limb.left) restPose.leftArm = limb.restPose;
            else restPose.rightArm = limb.restPose;
            if ((limb.radiusAxisEnd - limb.radiusAxisStart).sqrMagnitude < Epsilon)
            {
                limb.radiusAxisStart = limb.elbow;
                limb.radiusAxisEnd = limb.wrist;
                Debug.LogWarning("[AnatomicalRig] Forearm axis uses elbow/wrist landmarks; radial-head/distal-ulna calibration remains unverified.", this);
            }
        }
        manualPose = restPose;
        currentPose = restPose;
        initialized = false;
        Initialize();
    }

    public void SetInputSource(InputSource source) { inputSource = source; }
    public void SetAutomaticEvaluation(bool automatic)
    {
        evaluationMode = automatic ? EvaluationMode.LateUpdate : EvaluationMode.ExplicitOnly;
    }

    private void Awake() { Initialize(); }

    private void LateUpdate()
    {
        if (evaluationMode == EvaluationMode.LateUpdate) Evaluate();
    }

    /// <summary>Reads the c9 Animator/IK final pose or applies the authored manual pose.</summary>
    public bool Evaluate()
    {
        return inputSource == InputSource.Manual ? Evaluate(manualPose) : EvaluateAnimation();
    }

    /// <summary>Preview and runtime use this same deterministic manual evaluation path.</summary>
    public bool Evaluate(Pose pose)
    {
        if (!Initialize()) return false;
        if (!Finite(pose)) return Fail("Manual pose contains NaN or infinity.");
        currentPose = pose;
        // An explicit author-rest request restores the original transforms exactly, including arbitrary T-pose roll.
        if (MatchesRest(pose))
        {
            RestoreRestPose();
            lastDiagnostics = new Diagnostics
            {
                leftRequestedElevation = pose.leftArm.shoulderElevation,
                rightRequestedElevation = pose.rightArm.shoulderElevation,
                leftElevation = pose.leftArm.shoulderElevation,
                rightElevation = pose.rightArm.shoulderElevation
            };
            ReportDiagnostics();
            return true;
        }
        RestoreBodyRest();
        bodyRotation = Quaternion.identity;
        bodyTranslation = Vector3.zero;
        lastDiagnostics = new Diagnostics();
        for (int i = 0; i < limbs.Length; i++)
        {
            Limb limb = limbs[i];
            SolveArm(limb, limb.left ? pose.leftArm : pose.rightArm, false,
                limb.sourceRest[0], limb.sourceRest[1], limb.sourceRest[2], limb.sourceRest[3]);
        }
        SolveNeck(pose.neckDegrees, false, neck.headSourceRest);
        ApplyCorrections();
        ReportDiagnostics();
        return true;
    }

    private bool EvaluateAnimation()
    {
        if (!Initialize()) return false;
        if (!TryRead(neck.sourceChest, out chestCurrent)) return false;
        bodyRotation = chestCurrent.rotation * Quaternion.Inverse(neck.chestRest.rotation);
        bodyTranslation = chestCurrent.position - bodyRotation * neck.chestRest.position;
        for (int i = 0; i < bodyBindings.Length; i++)
        {
            BoneBinding binding = bodyBindings[i];
            if (!TryRead(binding.source, out RestTransform current)) return false;
            Quaternion delta = current.rotation * Quaternion.Inverse(binding.sourceRest.rotation);
            SetOutput(binding.output,
                current.position + delta * (binding.outputRest.position - binding.sourceRest.position),
                delta * binding.outputRest.rotation);
        }
        lastDiagnostics = new Diagnostics();
        currentPose = restPose;
        Quaternion inverseBody = Quaternion.Inverse(bodyRotation);
        for (int i = 0; i < limbs.Length; i++)
        {
            Limb limb = limbs[i];
            if (!TryRead(limb.sourceClavicle, out RestTransform clavicle) ||
                !TryRead(limb.sourceUpper, out RestTransform upper) ||
                !TryRead(limb.sourceLower, out RestTransform lower) ||
                !TryRead(limb.sourceHand, out RestTransform hand)) return false;
            clavicle = RemoveBody(clavicle, inverseBody);
            upper = RemoveBody(upper, inverseBody);
            lower = RemoveBody(lower, inverseBody);
            hand = RemoveBody(hand, inverseBody);
            Vector3 u = (lower.position - upper.position).normalized;
            Vector3 f = (hand.position - lower.position).normalized;
            ArmPose pose = ReadArmAngles(limb, u, f);
            Vector3 restUpper = (limb.elbow - limb.shoulder).normalized;
            Quaternion swing = Quaternion.FromToRotation(restUpper, u);
            Quaternion sourceDelta = upper.rotation * Quaternion.Inverse(limb.sourceRest[1].rotation);
            pose.humeralRotation = SignedTwist(sourceDelta * Quaternion.Inverse(swing), u);
            Quaternion upperDelta = Quaternion.AngleAxis(pose.humeralRotation, u) * swing;
            pose.elbowFlexion = SignedElbowFlexion(u, f, upperDelta * limb.restBendDirection);
            Vector3 restLower = (limb.wrist - limb.elbow).normalized;
            Quaternion ulnaDelta = Quaternion.FromToRotation(upperDelta * restLower, f) * upperDelta;
            Quaternion handDelta = hand.rotation * Quaternion.Inverse(limb.sourceRest[3].rotation);
            pose.forearmRotation = SignedTwist(handDelta * Quaternion.Inverse(ulnaDelta), f);
            if (limb.left) currentPose.leftArm = pose;
            else currentPose.rightArm = pose;
            SolveArm(limb, pose, true, clavicle, upper, lower, hand);
        }
        if (!TryRead(neck.sourceHead, out RestTransform headCurrent)) return false;
        RestTransform headWithoutBody = RemoveBody(headCurrent, inverseBody);
        Quaternion headDelta = headWithoutBody.rotation * Quaternion.Inverse(neck.headSourceRest.rotation);
        currentPose.neckDegrees = NeckAngles(headDelta);
        SolveNeck(currentPose.neckDegrees, true, headWithoutBody);
        ApplyCorrections();
        ReportDiagnostics();
        return true;
    }

    private void SolveArm(Limb limb, ArmPose requested, bool animation,
        RestTransform sourceClavicle, RestTransform sourceUpper,
        RestTransform sourceLower, RestTransform sourceHand)
    {
        if (limb.left) lastDiagnostics.leftRequestedElevation = requested.shoulderElevation;
        else lastDiagnostics.rightRequestedElevation = requested.shoulderElevation;
        if (!animation && SameArm(requested, limb.restPose))
        {
            RestoreLimbRest(limb);
            if (limb.left) lastDiagnostics.leftElevation = requested.shoulderElevation;
            else lastDiagnostics.rightElevation = requested.shoulderElevation;
            return;
        }
        // Removed: out-of-range manual elevations/flexion. This is a presentation range, not clinical ROM judgement.
        float elevation = Mathf.Clamp(requested.shoulderElevation, 0f, 180f);
        // Keep only the measured tiny author hyperextension, not arbitrary negative ROM.
        float elbowFlexion = Mathf.Clamp(requested.elbowFlexion, Mathf.Min(0f, limb.restPose.elbowFlexion), 160f);
        Vector3 restUpper = (limb.elbow - limb.shoulder).normalized;
        Vector3 restLower = (limb.wrist - limb.elbow).normalized;
        Vector3 outward = anatomicalBasis.right * (limb.left ? -1f : 1f);
        Vector3 upperDirection;
        Vector3 lowerDirection;
        Vector3 shoulder;
        Vector3 elbow;
        Vector3 wrist;
        Quaternion upperDelta;
        Quaternion ulnaDelta;
        Quaternion handDelta;
        Quaternion clavicleDelta;
        float scapularDelta = ScapularUpward(elevation) - ScapularUpward(limb.restPose.shoulderElevation);
        float clavicularDelta = ClavicularElevation(elevation) - ClavicularElevation(limb.restPose.shoulderElevation);

        // FromToRotation measures the required direction change from the basis; no assumed mirrored Euler signs.
        Vector3 elevationAxis = Vector3.Cross(outward, anatomicalBasis.up).normalized;
        Quaternion extraClavicle = Quaternion.AngleAxis(requested.shrug, elevationAxis) *
            Protraction(requested.protraction, outward);
        if (animation)
        {
            shoulder = sourceUpper.position;
            elbow = sourceLower.position;
            wrist = sourceHand.position;
            upperDirection = (elbow - shoulder).normalized;
            lowerDirection = (wrist - elbow).normalized;
            Quaternion swing = Quaternion.FromToRotation(restUpper, upperDirection);
            upperDelta = Quaternion.AngleAxis(requested.humeralRotation, upperDirection) * swing;
            ulnaDelta = Quaternion.FromToRotation(upperDelta * restLower, lowerDirection) * upperDelta;
            handDelta = sourceHand.rotation * Quaternion.Inverse(limb.sourceRest[3].rotation);
            // Animator clavicle motion is already present: retain it rather than adding the rhythm twice.
            clavicleDelta = extraClavicle * sourceClavicle.rotation * Quaternion.Inverse(limb.sourceRest[0].rotation);
        }
        else
        {
            float e = elevation * Mathf.Deg2Rad;
            float p = requested.elevationPlane * Mathf.Deg2Rad;
            upperDirection = -anatomicalBasis.up * Mathf.Cos(e) +
                (anatomicalBasis.forward * Mathf.Cos(p) + outward * Mathf.Sin(p)) * Mathf.Sin(e);
            upperDirection.Normalize();
            Quaternion swing = Quaternion.FromToRotation(restUpper, upperDirection);
            upperDelta = Quaternion.AngleAxis(requested.humeralRotation, upperDirection) * swing;
            Vector3 hingeAxis = Vector3.Cross(restUpper, limb.restBendDirection).normalized;
            float flexionChange = elbowFlexion - limb.restPose.elbowFlexion;
            lowerDirection = upperDelta * (Quaternion.AngleAxis(flexionChange, hingeAxis) * restLower);
            lowerDirection.Normalize();
            ulnaDelta = Mathf.Abs(flexionChange) < Epsilon ? upperDelta :
                Quaternion.FromToRotation(upperDelta * restLower, lowerDirection) * upperDelta;
            clavicleDelta = extraClavicle * Quaternion.AngleAxis(clavicularDelta, elevationAxis);
            shoulder = limb.clavicleMedial + clavicleDelta * (limb.shoulder - limb.clavicleMedial);
            elbow = shoulder + upperDirection * Vector3.Distance(limb.shoulder, limb.elbow);
            wrist = elbow + lowerDirection * Vector3.Distance(limb.elbow, limb.wrist);
            Quaternion forearmTwist = Quaternion.AngleAxis(requested.forearmRotation, lowerDirection);
            Quaternion wristNeutral = forearmTwist * ulnaDelta;
            // Measured c9 palm frame, on each side: +flexion moves the finger
            // longitudinal direction toward the palm; +deviation moves it toward
            // the index/little radial direction. Both axes follow forearm roll.
            Quaternion wristFlexion = Quaternion.AngleAxis(requested.wristFlexion,
                wristNeutral * limb.wristFlexionAxis);
            // Intrinsic order: flexion first, then deviation around the axis that
            // flexion carried with the hand. No world/elbow-pole wrist fallback.
            Quaternion wristDeviation = Quaternion.AngleAxis(requested.wristDeviation,
                wristFlexion * (wristNeutral * limb.wristDeviationAxis));
            handDelta = wristDeviation * wristFlexion * wristNeutral;
        }

        // Approximate independent scapular orientation. The changing elevation plane also affects tilt/rotation.
        float restGain = ScapularUpward(limb.restPose.shoulderElevation) / 60f;
        float gain = ScapularUpward(elevation) / 60f;
        float anteriorWeight = Mathf.Cos(requested.elevationPlane * Mathf.Deg2Rad);
        float restAnteriorWeight = Mathf.Cos(limb.restPose.elevationPlane * Mathf.Deg2Rad);
        Quaternion scapularDeltaRotation = Quaternion.AngleAxis(requested.shrug, elevationAxis) *
            Quaternion.AngleAxis(scapularDelta, elevationAxis) *
            // Editor runtime measurement: outward produced 13.56 degrees of bilateral
            // mirror error at 150 elevation. The common +right axis moved the
            // superior direction anterior (+0.118); -right gave posterior (-0.118).
            Quaternion.AngleAxis((gain - restGain) * 20f, -anatomicalBasis.right) *
            Protraction((gain * anteriorWeight - restGain * restAnteriorWeight) * 15f + requested.protraction, outward);
        Vector3 scapularCentre = shoulder + scapularDeltaRotation * (limb.scapulaCenter - limb.shoulder);
        Vector3 radialAxisStart = elbow + ulnaDelta * (limb.radiusAxisStart - limb.elbow);
        Vector3 radialAxisEnd = elbow + ulnaDelta * (limb.radiusAxisEnd - limb.elbow);
        Vector3 radialAxis = (radialAxisEnd - radialAxisStart).normalized;
        Quaternion radialTwist = Quaternion.AngleAxis(requested.forearmRotation, radialAxis);

        SetRestTransformed(limb.clavicle, limb.outputRest[0], limb.clavicleMedial, limb.clavicleMedial, clavicleDelta);
        SetRestTransformed(limb.scapula, limb.outputRest[1], limb.scapulaCenter, scapularCentre, scapularDeltaRotation);
        SetRestTransformed(limb.humerus, limb.outputRest[2], limb.shoulder, shoulder, upperDelta);
        SetRestTransformed(limb.ulna, limb.outputRest[3], limb.elbow, elbow, ulnaDelta);
        Vector3 radiusWithoutTwist = elbow + ulnaDelta * (limb.outputRest[4].position - limb.elbow);
        SetWithBody(limb.radius, radialAxisStart + radialTwist * (radiusWithoutTwist - radialAxisStart),
            radialTwist * ulnaDelta * limb.outputRest[4].rotation);
        SetRestTransformed(limb.hand, limb.outputRest[5], limb.wrist, wrist, handDelta);

        ApplyArmSkinBindings(limb, animation, sourceClavicle, sourceUpper, sourceLower, sourceHand,
            shoulder, elbow, wrist, clavicleDelta, upperDelta, ulnaDelta, handDelta,
            requested.forearmRotation);
        if (limb.left)
        {
            lastDiagnostics.leftElevation = elevation;
            lastDiagnostics.leftScapularUpward = scapularDelta;
            lastDiagnostics.leftForearmRotation = requested.forearmRotation;
        }
        else
        {
            lastDiagnostics.rightElevation = elevation;
            lastDiagnostics.rightScapularUpward = scapularDelta;
            lastDiagnostics.rightForearmRotation = requested.forearmRotation;
        }
    }

    private void ApplyArmSkinBindings(Limb limb, bool animation,
        RestTransform sourceClavicle, RestTransform sourceUpper, RestTransform sourceLower, RestTransform sourceHand,
        Vector3 shoulder, Vector3 elbow, Vector3 wrist, Quaternion clavicleDelta,
        Quaternion upperDelta, Quaternion ulnaDelta, Quaternion handDelta, float forearmRotation)
    {
        Vector3 currentLowerAxis = (wrist - elbow).normalized;
        Quaternion bodyInverse = Quaternion.Inverse(bodyRotation);
        for (int i = 0; i < bodyBindings.Length; i++)
        {
            int region = limb.bindingRegions[i];
            if (region == 0) continue;
            BoneBinding binding = bodyBindings[i];
            // Detailed output bones may also be source-mirror bindings. Their final pose was already written above.
            if (binding.output == limb.clavicle || binding.output == limb.scapula || binding.output == limb.humerus ||
                binding.output == limb.ulna || binding.output == limb.radius || binding.output == limb.hand) continue;
            RestTransform oldFrame;
            Vector3 newPosition;
            Quaternion newRotation;
            if (region == 1)
            {
                oldFrame = animation ? sourceClavicle : limb.sourceRest[0];
                newPosition = limb.clavicleMedial + clavicleDelta * (limb.sourceRest[0].position - limb.clavicleMedial);
                newRotation = clavicleDelta * limb.sourceRest[0].rotation;
            }
            else if (region == 2)
            {
                oldFrame = animation ? sourceUpper : limb.sourceRest[1];
                newPosition = shoulder;
                newRotation = upperDelta * limb.sourceRest[1].rotation;
            }
            else if (region == 3)
            {
                oldFrame = animation ? sourceLower : limb.sourceRest[2];
                Quaternion twist = Quaternion.AngleAxis(forearmRotation * limb.bindingTwistFractions[i], currentLowerAxis);
                newPosition = elbow;
                newRotation = twist * ulnaDelta * limb.sourceRest[2].rotation;
            }
            else
            {
                oldFrame = animation ? sourceHand : limb.sourceRest[3];
                newPosition = wrist;
                newRotation = handDelta * limb.sourceRest[3].rotation;
            }
            Quaternion correction = newRotation * Quaternion.Inverse(oldFrame.rotation);
            Vector3 oldPosition = bodyInverse * (binding.output.localPosition - bodyTranslation);
            Quaternion oldRotation = bodyInverse * binding.output.localRotation;
            SetWithBody(binding.output, newPosition + correction * (oldPosition - oldFrame.position), correction * oldRotation);
        }
    }

    private void SolveNeck(Vector3 degrees, bool animation, RestTransform sourceHead)
    {
        Vector3 previousRestPivot = neck.basePivot;
        Vector3 previousPivot = neck.basePivot;
        Quaternion cumulative = Quaternion.identity;
        Quaternion headDelta = NeckRotation(degrees);
        for (int i = 0; i < 7; i++)
        {
            Vector3 pivot = previousPivot + cumulative * (neck.jointPivots[i] - previousRestPivot);
            // Mixed-axis rotations do not commute. The cranial joint receives the exact remaining
            // rotation, so the top cervical segment and head share one final frame without a jump.
            Quaternion jointDelta = i == 6 ? Quaternion.Inverse(cumulative) * headDelta :
                NeckRotation(new Vector3(degrees.x * FlexionShares[i],
                    degrees.y * LateralShares[i], degrees.z * AxialShares[i]));
            cumulative = cumulative * jointDelta;
            Vector3 position = pivot + cumulative * (neck.segmentRest[i].position - neck.jointPivots[i]);
            Quaternion rotation = cumulative * neck.segmentRest[i].rotation;
            neckPositions[i] = position;
            neckRotations[i] = rotation;
            SetWithBody(neck.segments[i], position, rotation);
            previousPivot = pivot;
            previousRestPivot = neck.jointPivots[i];
        }
        Vector3 headPosition = previousPivot + cumulative * (neck.headOutputRest.position - previousRestPivot);
        Quaternion headRotation = headDelta * neck.headOutputRest.rotation;
        SetWithBody(neck.head, headPosition, headRotation);

        Quaternion inverseBody = Quaternion.Inverse(bodyRotation);
        RestTransform oldHead = animation ? sourceHead : neck.headSourceRest;
        // c9 head/face outputs preserve their expression relative to the source head, then receive the detailed neck endpoint.
        Vector3 sourceHeadTarget = headPosition + headDelta * (neck.headSourceRest.position - neck.headOutputRest.position);
        Quaternion sourceHeadTargetRotation = headDelta * neck.headSourceRest.rotation;
        Quaternion headCorrection = sourceHeadTargetRotation * Quaternion.Inverse(oldHead.rotation);
        for (int i = 0; i < bodyBindings.Length; i++)
        {
            int region = neck.bindingRegions[i];
            if (region == 0) continue;
            BoneBinding binding = bodyBindings[i];
            if (binding.output == neck.head || IsNeckOutput(binding.output)) continue;
            if (region == 2)
            {
                Vector3 oldPosition = inverseBody * (binding.output.localPosition - bodyTranslation);
                Quaternion oldRotation = inverseBody * binding.output.localRotation;
                SetWithBody(binding.output, sourceHeadTarget + headCorrection * (oldPosition - oldHead.position),
                    headCorrection * oldRotation);
            }
            else
            {
                int segment = neck.bindingSegments[i];
                Quaternion delta = neckRotations[segment] * Quaternion.Inverse(neck.segmentRest[segment].rotation);
                SetWithBody(binding.output,
                    neckPositions[segment] + delta * (binding.outputRest.position - neck.segmentRest[segment].position),
                    delta * binding.outputRest.rotation);
            }
        }
        lastDiagnostics.neckDegrees = degrees;
        lastDiagnostics.headDisplacement = Vector3.Distance(headPosition, neck.headOutputRest.position);
        lastDiagnostics.neckEndpointRotationError = Quaternion.Angle(cumulative, headDelta);
    }

    public void RestoreRestPose()
    {
        if (!Initialize()) return;
        RestoreBodyRest();
        for (int i = 0; i < limbs.Length; i++)
        {
            RestoreLimbRest(limbs[i]);
        }
        for (int i = 0; i < 7; i++)
            SetOutput(neck.segments[i], neck.segmentRest[i].position, neck.segmentRest[i].rotation);
        SetOutput(neck.head, neck.headOutputRest.position, neck.headOutputRest.rotation);
        currentPose = restPose;
        ResetCorrections();
    }

    private void RestoreBodyRest()
    {
        for (int i = 0; i < bodyBindings.Length; i++)
            SetOutput(bodyBindings[i].output, bodyBindings[i].outputRest.position, bodyBindings[i].outputRest.rotation);
    }

    private static void RestoreLimbRest(Limb limb)
    {
        SetOutput(limb.clavicle, limb.outputRest[0].position, limb.outputRest[0].rotation);
        SetOutput(limb.scapula, limb.outputRest[1].position, limb.outputRest[1].rotation);
        SetOutput(limb.humerus, limb.outputRest[2].position, limb.outputRest[2].rotation);
        SetOutput(limb.ulna, limb.outputRest[3].position, limb.outputRest[3].rotation);
        SetOutput(limb.radius, limb.outputRest[4].position, limb.outputRest[4].rotation);
        SetOutput(limb.hand, limb.outputRest[5].position, limb.outputRest[5].rotation);
    }

    private bool Initialize()
    {
        if (initialized)
        {
            if (!valid) return false;
            if (RuntimeCachesValid()) return true;
            // Defensive against editor reload/deserialization restoring a flag without its arrays.
            initialized = false;
            valid = false;
        }
        if (!ValidateReferences()) return false;
        if (neck.segmentRest == null || neck.segmentRest.Length != 7 || neck.jointPivots == null || neck.jointPivots.Length != 7)
            return Fail("Author rest is missing. Run the measured builder; rest is never recaptured in Awake.");
        if (neckPositions == null || neckPositions.Length != 7) neckPositions = new Vector3[7];
        if (neckRotations == null || neckRotations.Length != 7) neckRotations = new Quaternion[7];
        for (int i = 0; i < limbs.Length; i++)
        {
            Limb limb = limbs[i];
            if (limb.sourceRest == null || limb.sourceRest.Length != 4 || limb.outputRest == null || limb.outputRest.Length != 6)
                return Fail("Limb author rest is missing.");
            if (!MeasureWristFrame(limb)) return false;
            limb.bindingRegions = new int[bodyBindings.Length];
            limb.bindingTwistFractions = new float[bodyBindings.Length];
            Vector3 forearm = limb.wrist - limb.elbow;
            float lengthSquared = forearm.sqrMagnitude;
            for (int j = 0; j < bodyBindings.Length; j++)
            {
                Transform source = bodyBindings[j].source;
                int region = source.IsChildOf(limb.sourceHand) ? 4 :
                    source.IsChildOf(limb.sourceLower) ? 3 :
                    source.IsChildOf(limb.sourceUpper) ? 2 :
                    source.IsChildOf(limb.sourceClavicle) ? 1 : 0;
                limb.bindingRegions[j] = region;
                // Removed: positions outside the forearm interval, so helper bones cannot receive more than full wrist roll.
                limb.bindingTwistFractions[j] = Mathf.Clamp01(Vector3.Dot(bodyBindings[j].sourceRest.position - limb.elbow, forearm) / lengthSquared);
            }
        }
        neck.bindingRegions = new int[bodyBindings.Length];
        neck.bindingSegments = new int[bodyBindings.Length];
        for (int i = 0; i < bodyBindings.Length; i++)
        {
            Transform source = bodyBindings[i].source;
            neck.bindingRegions[i] = source.IsChildOf(neck.sourceHead) ? 2 : source.IsChildOf(neck.sourceNeck1) ? 1 : 0;
            int nearest = 0;
            float distance = float.PositiveInfinity;
            for (int j = 0; j < 7; j++)
            {
                float candidate = (bodyBindings[i].sourceRest.position - neck.segmentRest[j].position).sqrMagnitude;
                if (candidate < distance) { nearest = j; distance = candidate; }
            }
            neck.bindingSegments[i] = nearest;
        }
        initialized = true;
        valid = true;
        lastFailure = null;
        ChunaLogger.Log($"[AnatomicalRig] Ready: body bindings {bodyBindings.Length}, arms {limbs.Length}, cervical segments 7. Source={inputSource}, evaluation={evaluationMode}. Shoulder/cervical motion model is an approximation.");
        return true;
    }

    private bool RuntimeCachesValid()
    {
        if (bodyBindings == null || limbs == null || neck == null ||
            neckPositions == null || neckPositions.Length != 7 ||
            neckRotations == null || neckRotations.Length != 7 ||
            neck.bindingRegions == null || neck.bindingRegions.Length != bodyBindings.Length ||
            neck.bindingSegments == null || neck.bindingSegments.Length != bodyBindings.Length)
            return false;
        for (int i = 0; i < limbs.Length; i++)
        {
            Limb limb = limbs[i];
            if (limb == null || limb.bindingRegions == null || limb.bindingRegions.Length != bodyBindings.Length ||
                limb.bindingTwistFractions == null || limb.bindingTwistFractions.Length != bodyBindings.Length ||
                !limb.wristFrameReady)
                return false;
        }
        return true;
    }

    private bool MeasureWristFrame(Limb limb)
    {
        limb.wristFrameReady = false;
        Animator animator = limb.sourceHand.GetComponentInParent<Animator>();
        if (animator == null || !animator.isHuman)
            return Fail("Manual wrist frame requires the source c9 Humanoid Animator.");
        HumanBodyBones handId = limb.left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
        HumanBodyBones indexId = limb.left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal;
        HumanBodyBones middleId = limb.left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal;
        HumanBodyBones littleId = limb.left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal;
        if (animator.GetBoneTransform(handId) != limb.sourceHand)
            return Fail("Manual wrist frame source hand does not match its explicit Humanoid side.");
        if (!TryBindingRestPosition(limb.sourceHand, out Vector3 wrist) ||
            !TryBindingRestPosition(animator.GetBoneTransform(indexId), out Vector3 index) ||
            !TryBindingRestPosition(animator.GetBoneTransform(middleId), out Vector3 middle) ||
            !TryBindingRestPosition(animator.GetBoneTransform(littleId), out Vector3 little))
            return Fail("Manual wrist frame is missing a stored c9 hand/index/middle/little output binding.");
        Vector3 longitudinal = middle - wrist;
        if (!Finite(longitudinal) || longitudinal.sqrMagnitude < Epsilon)
            return Fail("Measured c9 wrist/middle landmarks are coincident or invalid.");
        longitudinal.Normalize();
        Vector3 radial = index - little;
        // Removed: longitudinal component, measuring only the hand's transverse
        // index-to-little direction rather than treating knuckle stagger as roll.
        radial -= longitudinal * Vector3.Dot(radial, longitudinal);
        if (!Finite(radial) || radial.sqrMagnitude < Epsilon)
            return Fail("Measured c9 index/little landmarks cannot define a radial wrist direction.");
        radial.Normalize();
        // Side selects the palmar side of the measured finger plane, using the
        // same c9 landmark convention as the skin/anatomy palm alignment audit.
        Vector3 palmar = (limb.left ? Vector3.Cross(radial, longitudinal) :
            Vector3.Cross(longitudinal, radial)).normalized;
        limb.wristFlexionAxis = Vector3.Cross(longitudinal, palmar).normalized;
        limb.wristDeviationAxis = Vector3.Cross(longitudinal, radial).normalized;
        limb.wristFrameReady = true;
        ChunaLogger.Log($"[AnatomicalRig] Manual wrist frame {(limb.left ? "L" : "R")}: finger={longitudinal:F5}, palm={palmar:F5}, radial={radial:F5}; positive flexion=palm, positive deviation=radial; flexion then intrinsic deviation.");
        return true;
    }

    private bool TryBindingRestPosition(Transform source, out Vector3 position)
    {
        if (source != null)
            for (int i = 0; i < bodyBindings.Length; i++)
                if (bodyBindings[i].source == source)
                {
                    position = bodyBindings[i].outputRest.position;
                    return Finite(position);
                }
        position = Vector3.zero;
        return false;
    }

    private bool ValidateReferences()
    {
        if (rigSpace == null || bodyBindings == null || limbs == null || neck == null)
            return Fail("Missing rig space or explicit authoring data.");
        if (anatomicalBasis.right.sqrMagnitude < .9f || anatomicalBasis.up.sqrMagnitude < .9f || anatomicalBasis.forward.sqrMagnitude < .9f ||
            Mathf.Abs(Vector3.Dot(anatomicalBasis.right, anatomicalBasis.up)) > .001f ||
            Mathf.Abs(Vector3.Dot(anatomicalBasis.right, anatomicalBasis.forward)) > .001f ||
            Mathf.Abs(Vector3.Dot(anatomicalBasis.up, anatomicalBasis.forward)) > .001f)
            return Fail("Measured anatomical basis is not orthonormal.");
        anatomicalBasis.right.Normalize(); anatomicalBasis.up.Normalize(); anatomicalBasis.forward.Normalize();
        if (Vector3.Dot(Vector3.Cross(anatomicalBasis.right, anatomicalBasis.up), anatomicalBasis.forward) < .999f)
            return Fail("Anatomical right/up/forward basis is reflected. Measure the basis in the shared source/output space.");
        if (neck.sourceChest == null || neck.sourceNeck1 == null || neck.sourceNeck2 == null || neck.sourceHead == null ||
            neck.segments == null || neck.segments.Length != 7 || !OutputIsValid(neck.head))
            return Fail("Missing explicit neck source/output references (seven segments required).");
        for (int i = 0; i < 7; i++) if (!OutputIsValid(neck.segments[i])) return Fail("Neck output must be a direct rigSpace child.");
        for (int i = 0; i < bodyBindings.Length; i++)
        {
            BoneBinding b = bodyBindings[i];
            if (b == null || b.source == null || !OutputIsValid(b.output)) return Fail("Body source/output binding is missing or has a non-flat output parent.");
            if (b.source == b.output || b.source.IsChildOf(rigSpace)) return Fail("Source/output feedback is forbidden: Animator inputs must be outside the output rig.");
        }
        for (int i = 0; i < limbs.Length; i++)
        {
            Limb limb = limbs[i];
            if (limb == null || limb.sourceClavicle == null || limb.sourceUpper == null || limb.sourceLower == null || limb.sourceHand == null ||
                !OutputIsValid(limb.clavicle) || !OutputIsValid(limb.scapula) || !OutputIsValid(limb.humerus) ||
                !OutputIsValid(limb.ulna) || !OutputIsValid(limb.radius) || !OutputIsValid(limb.hand))
                return Fail("Missing limb inputs/outputs; every output must be a direct rigSpace child.");
            if ((limb.elbow - limb.shoulder).sqrMagnitude < Epsilon || (limb.wrist - limb.elbow).sqrMagnitude < Epsilon)
                return Fail("Measured upper-arm/forearm length is zero.");
        }
        if (skinCorrections != null)
        {
            for (int i = 0; i < skinCorrections.Length; i++)
            {
                SkinCorrection correction = skinCorrections[i];
                if (correction == null || correction.renderer == null || correction.renderer.sharedMesh == null ||
                    correction.blendShape < 0 || correction.blendShape >= correction.renderer.sharedMesh.blendShapeCount ||
                    correction.fullDegrees <= correction.startDegrees)
                    return Fail("Skin correction renderer/index/angle interval is invalid.");
            }
        }
        return true;
    }

    private bool OutputIsValid(Transform output) { return output != null && output.parent == rigSpace; }

    private bool TryRead(Transform source, out RestTransform result)
    {
        Matrix4x4 matrix = rigSpace.worldToLocalMatrix * source.localToWorldMatrix;
        Vector3 x = matrix.GetColumn(0), y = matrix.GetColumn(1), z = matrix.GetColumn(2);
        result = new RestTransform { position = matrix.GetColumn(3), rotation = Quaternion.identity };
        float determinant = Vector3.Dot(Vector3.Cross(x, y), z);
        if (!Finite(result.position) || !Finite(x) || !Finite(y) || !Finite(z) || determinant <= Epsilon ||
            x.sqrMagnitude < Epsilon || y.sqrMagnitude < Epsilon || z.sqrMagnitude < Epsilon)
            return Fail("Source frame is singular/reflected relative to rigSpace: " + source.name + ". Place output rig below the same mirrored ancestry.");
        x.Normalize(); y.Normalize(); z.Normalize();
        if (Mathf.Abs(Vector3.Dot(x, y)) > .002f || Mathf.Abs(Vector3.Dot(x, z)) > .002f || Mathf.Abs(Vector3.Dot(y, z)) > .002f)
            return Fail("Source frame contains shear: " + source.name);
        result.rotation = Quaternion.LookRotation(z, y);
        return true;
    }

    private RestTransform ReadOutput(Transform output)
    {
        return new RestTransform { position = output.localPosition, rotation = output.localRotation };
    }

    private RestTransform RemoveBody(RestTransform value, Quaternion inverseBody)
    {
        value.position = inverseBody * (value.position - bodyTranslation);
        value.rotation = inverseBody * value.rotation;
        return value;
    }

    private ArmPose ReadArmAngles(Limb limb, Vector3 upper, Vector3 lower)
    {
        Vector3 outward = anatomicalBasis.right * (limb.left ? -1f : 1f);
        float elevation = Vector3.Angle(-anatomicalBasis.up, upper);
        // The elevation plane is undefined at arm-down/up. Preserve the author plane there instead of deriving noisy atan2(0,0).
        float anterior = Vector3.Dot(upper, anatomicalBasis.forward);
        float lateral = Vector3.Dot(upper, outward);
        float plane = anterior * anterior + lateral * lateral > Epsilon ?
            Mathf.Atan2(lateral, anterior) * Mathf.Rad2Deg : limb.restPose.elevationPlane;
        return new ArmPose { shoulderElevation = elevation, elevationPlane = plane, elbowFlexion = Vector3.Angle(upper, lower) };
    }

    private static float SignedElbowFlexion(Vector3 upper, Vector3 lower, Vector3 bendPole)
    {
        return Mathf.Atan2(Vector3.Dot(lower, bendPole), Vector3.Dot(lower, upper)) * Mathf.Rad2Deg;
    }

    private Quaternion Protraction(float degrees, Vector3 outward)
    {
        Vector3 desired = outward * Mathf.Cos(degrees * Mathf.Deg2Rad) + anatomicalBasis.forward * Mathf.Sin(degrees * Mathf.Deg2Rad);
        return Quaternion.FromToRotation(outward, desired);
    }

    private Quaternion NeckRotation(Vector3 degrees)
    {
        return Quaternion.AngleAxis(degrees.z, anatomicalBasis.up) *
            Quaternion.AngleAxis(degrees.x, anatomicalBasis.right) *
            Quaternion.AngleAxis(-degrees.y, anatomicalBasis.forward);
    }

    private Vector3 NeckAngles(Quaternion delta)
    {
        Vector3 forward = delta * anatomicalBasis.forward;
        float yaw = Mathf.Atan2(Vector3.Dot(forward, anatomicalBasis.right), Vector3.Dot(forward, anatomicalBasis.forward)) * Mathf.Rad2Deg;
        Vector3 yawRemoved = Quaternion.AngleAxis(-yaw, anatomicalBasis.up) * forward;
        float flexion = Mathf.Atan2(-Vector3.Dot(yawRemoved, anatomicalBasis.up), Vector3.Dot(yawRemoved, anatomicalBasis.forward)) * Mathf.Rad2Deg;
        Quaternion swing = Quaternion.AngleAxis(yaw, anatomicalBasis.up) * Quaternion.AngleAxis(flexion, anatomicalBasis.right);
        Vector3 remainingUp = Quaternion.Inverse(swing) * (delta * anatomicalBasis.up);
        float lateral = Mathf.Atan2(Vector3.Dot(remainingUp, anatomicalBasis.right), Vector3.Dot(remainingUp, anatomicalBasis.up)) * Mathf.Rad2Deg;
        return new Vector3(flexion, lateral, yaw);
    }

    private static float SignedTwist(Quaternion rotation, Vector3 axis)
    {
        float projection = rotation.x * axis.x + rotation.y * axis.y + rotation.z * axis.z;
        return Mathf.DeltaAngle(0f, 2f * Mathf.Atan2(projection, rotation.w) * Mathf.Rad2Deg);
    }

    // Piecewise curves are rough presentation models, not a fixed 2:1 anatomical law.
    private static float ScapularUpward(float elevation)
    {
        if (elevation <= 30f) return Mathf.Lerp(0f, 5f, elevation / 30f);
        if (elevation <= 90f) return Mathf.Lerp(5f, 30f, (elevation - 30f) / 60f);
        return Mathf.Lerp(30f, 60f, (elevation - 90f) / 90f);
    }

    private static float ClavicularElevation(float elevation)
    {
        if (elevation <= 30f) return Mathf.Lerp(0f, 2f, elevation / 30f);
        if (elevation <= 90f) return Mathf.Lerp(2f, 15f, (elevation - 30f) / 60f);
        return Mathf.Lerp(15f, 30f, (elevation - 90f) / 90f);
    }

    private void SetRestTransformed(Transform output, RestTransform rest, Vector3 restPivot, Vector3 pivot, Quaternion delta)
    {
        SetWithBody(output, pivot + delta * (rest.position - restPivot), delta * rest.rotation);
    }

    private void SetWithBody(Transform output, Vector3 position, Quaternion rotation)
    {
        SetOutput(output, bodyTranslation + bodyRotation * position, bodyRotation * rotation);
    }

    private static void SetOutput(Transform output, Vector3 position, Quaternion rotation)
    {
        output.localPosition = position;
        output.localRotation = rotation;
    }

    private bool IsNeckOutput(Transform output)
    {
        for (int i = 0; i < 7; i++) if (neck.segments[i] == output) return true;
        return false;
    }

    private bool Fail(string reason)
    {
        valid = false;
        initialized = true;
        if (lastFailure != reason)
        {
            Debug.LogError("[AnatomicalRig] Cannot evaluate: " + reason, this);
            lastFailure = reason;
        }
        return false;
    }

    private void ReportDiagnostics()
    {
        if (!diagnostics || (Application.isPlaying && Time.unscaledTime < nextDiagnosticTime)) return;
        nextDiagnosticTime = Time.unscaledTime + 2f;
        ChunaLogger.Log($"[AnatomicalRig] Elevation requested/applied L {lastDiagnostics.leftRequestedElevation:F1}/{lastDiagnostics.leftElevation:F1}, R {lastDiagnostics.rightRequestedElevation:F1}/{lastDiagnostics.rightElevation:F1}; scapula delta {lastDiagnostics.leftScapularUpward:F1}/{lastDiagnostics.rightScapularUpward:F1}; forearm {lastDiagnostics.leftForearmRotation:F1}/{lastDiagnostics.rightForearmRotation:F1}; neck {lastDiagnostics.neckDegrees}; head shift {lastDiagnostics.headDisplacement:F4} m.");
    }

    private void ApplyCorrections()
    {
        if (skinCorrections == null) return;
        for (int i = 0; i < skinCorrections.Length; i++)
        {
            SkinCorrection correction = skinCorrections[i];
            ArmPose pose = correction.left ? currentPose.leftArm : currentPose.rightArm;
            ArmPose authorRest = correction.left ? restPose.leftArm : restPose.rightArm;
            float delta = correction.trigger == CorrectionTrigger.Elevation ?
                pose.shoulderElevation - authorRest.shoulderElevation : pose.elbowFlexion - authorRest.elbowFlexion;
            // Removed: trigger amounts outside the authored corrective interval. A blend shape only has a 0..100 presentation range.
            float weight = Mathf.Clamp01((delta - correction.startDegrees) /
                (correction.fullDegrees - correction.startDegrees)) * 100f;
            correction.renderer.SetBlendShapeWeight(correction.blendShape, weight);
        }
    }

    private void ResetCorrections()
    {
        if (skinCorrections == null) return;
        for (int i = 0; i < skinCorrections.Length; i++)
            skinCorrections[i].renderer.SetBlendShapeWeight(skinCorrections[i].blendShape, 0f);
    }

    private bool MatchesRest(Pose pose)
    {
        return SameArm(pose.leftArm, restPose.leftArm) && SameArm(pose.rightArm, restPose.rightArm) &&
            pose.neckDegrees == restPose.neckDegrees;
    }

    private static bool SameArm(ArmPose a, ArmPose b)
    {
        return a.shoulderElevation == b.shoulderElevation && a.elevationPlane == b.elevationPlane &&
            a.humeralRotation == b.humeralRotation && a.elbowFlexion == b.elbowFlexion &&
            a.forearmRotation == b.forearmRotation && a.wristFlexion == b.wristFlexion &&
            a.wristDeviation == b.wristDeviation && a.shrug == b.shrug && a.protraction == b.protraction;
    }

    private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    private static bool Finite(Vector3 value) { return Finite(value.x) && Finite(value.y) && Finite(value.z); }
    private static bool Finite(ArmPose pose)
    {
        return Finite(pose.shoulderElevation) && Finite(pose.elevationPlane) && Finite(pose.humeralRotation) &&
            Finite(pose.elbowFlexion) && Finite(pose.forearmRotation) && Finite(pose.wristFlexion) &&
            Finite(pose.wristDeviation) && Finite(pose.shrug) && Finite(pose.protraction);
    }
    private static bool Finite(Pose pose) { return Finite(pose.leftArm) && Finite(pose.rightArm) && Finite(pose.neckDegrees); }
}
