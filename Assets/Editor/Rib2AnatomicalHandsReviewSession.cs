using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Patient-first reconstruction on a disposable AnatomicalPatientRig. Source projection remains independent of contact placement.</summary>
public sealed partial class Rib2AnatomicalHandsReviewSession : IDisposable
{
    public const string Folder = "Captures/Rib2AnatomicalHands_V03_20261027";
    public const float SourceStart = 56.5f;
    public const int Fps = 30, FrameCount = 248;
    public float Duration => (FrameCount - 1f) / Fps;
    [Serializable] private sealed class CameraFit { public float[] rightRig = null, downRig = null, forwardRig = null; public float pixelsPerMetre = 0f; }
    [Serializable] private sealed class Key { public float sourceSeconds = 0f; public AnatomicalPatientRig.ArmPose armPose = default; public float[] shoulderPixel = null, elbowPixel = null, wristPixel = null; }
    [Serializable] private sealed class Fit { public bool affectedLeft = false; public CameraFit camera = null; public Key[] keys = null, densePatientKeys = null; }
    [Serializable] private struct Grip { public float along, across, normalAngle; }
    [Serializable] private sealed class CurveCache
    {
        public string signature = "";
        public AnatomicalPatientRig.ArmPose[] arms = null;
        public Grip[] grips = null;
        public int patientVertex = -1;
    }
    [Serializable] private sealed class Observation
    {
        public float sourceSeconds = 0f, actualSourceSeconds = 0f;
        public float[] patientShoulder = null, patientElbow = null, patientWrist = null, patientHandTip = null;
        public float[] leftWrist = null, leftPalm = null, rightWrist = null, rightPalm = null, rightContactRegionCenter = null;
    }
    [Serializable] private sealed class Observations { public Observation[] keys = null; }
    [Serializable] public sealed class SampleResult
    {
        public float sourceSeconds;
        public Vector2 shoulder, elbow, wrist, movingWrist, movingPalm, supportWrist, supportPalm;
        public Vector2 patientHandTip, patientPalm, movingContact, supportContact;
        public Vector4 measuredPatientFingerFlexion;
        public int guideJointsTested, guideJointsIntruding;
        public float guideJointMaximumInsetMetres;
        public float patientPalmarCameraDot;
        public float patientPalmOutwardDot, appliedWristFlexionDegrees;
        public float patientHandTipErrorPixels;
        public float movingGloveRegionErrorPixels, supportGloveRegionErrorPixels;
        public Vector3 shoulderWorld, elbowWorld, patientWristWorld;
        public Quaternion patientHandRotation, movingGuideRotation;
        public float upperLengthErrorMetres, lowerLengthErrorMetres;
        public Rib2AnatomicalHandsContact.FingerCollisionResult fingerCollision;
        public float guideSkinSamplingErrorMetres;
        public float supportPinnedVertexSamplingErrorMetres;
        public Rib2AnatomicalHandsContact.SurfaceContact sampledSurfaceContact;
        public float elbowErrorPixels, wristErrorPixels, movingWristErrorPixels, movingPalmErrorPixels, supportWristErrorPixels, supportPalmErrorPixels;
        public float measuredElbowDegrees, contactGapMetres;
        public bool finite;
        public string notes;
    }
    private sealed class Snapshot
    {
        public SkinnedMeshRenderer source;
        public GameObject holder;
        public Mesh mesh;
        public MeshRenderer renderer;
        public bool enabled;
    }
    private readonly Fit fit;
    private readonly Observation[] observations;
    private readonly PreviewRenderUtility preview;
    private readonly GameObject root;
    private readonly AnatomicalPatientRig rig;
    private readonly AnatomicalPatientLab lab;
    private readonly Rib2AnatomicalForearmSkin forearmSkin;
    private readonly Rib2AnatomicalHandsContact.Hand moving, support;
    private readonly Rib2AnatomicalHandsContact.PatientFingers fingers, restingFingers;
    private readonly Transform upper, lower, handTip, handPrevious;
    private readonly Transform supportBodyFrame;
    private bool supportAnchorReady;
    private Vector3 supportAnchorLocal, supportNormalLocal, supportDistalLocal;
    private readonly List<Snapshot> snapshots = new List<Snapshot>();
    private readonly List<MeshCollider> chestQueries = new List<MeshCollider>();
    private readonly List<Snapshot> querySkins = new List<Snapshot>();
    private readonly Vector3 cameraRight, cameraDown, cameraForward;
    private readonly float scale;
    private readonly float restUpperLength, restLowerLength;
    private readonly float patientCurlOverride;
    private readonly bool useBodycamSolution;
    private readonly string comparisonSolutionFile;
    private float solvedPatientCurl = .24f;
    private readonly AnatomicalPatientRig.ArmPose[] sampledArms = new AnatomicalPatientRig.ArmPose[FrameCount];
    private readonly Grip[] sampledGrips = new Grip[FrameCount];
    private Vector3 shoulderWorld, movingContact, supportContact;
    private Vector2 sourceShoulder;
    private Observation currentObservation;
    private float seconds;
    private AnatomicalPatientRig.ArmPose currentPresentedArm;
    private bool disposed;
    private bool fittingPath;
    private Quaternion previousPatientRotation;
    private Grip fixedGrip;
    private int pinnedPatientVertex = -1;
    private AnatomicalPatientRig.ArmPose initialHand;
    private Rib2AnatomicalHandsContact.FingerCollisionResult fingerCollision;
    private float guideSkinSamplingError;
    private Rib2AnatomicalHandsContact.SurfaceContact sampledSurfaceContact;
    private readonly Mesh regionMesh = new Mesh { name = "Private guide region measurement", hideFlags = HideFlags.HideAndDontSave };
    private readonly List<Vector3> regionVertices = new List<Vector3>(2000);
    private readonly List<Vector2> regionPixels = new List<Vector2>(2000);
    private readonly Dictionary<Mesh, int[]> regionTriangles = new Dictionary<Mesh, int[]>();

    public Rib2AnatomicalHandsReviewSession(bool calibrationOnly = false, float patientCurlOverride = -1f, int motionSmoothingPasses = DefaultSmoothingPasses, bool useBodycamSolution = true, string comparisonSolutionFile = null, string patientFitFile = null)
    {
        this.patientCurlOverride = patientCurlOverride;
        this.useBodycamSolution = useBodycamSolution;
        if (comparisonSolutionFile != null && Path.GetFileName(comparisonSolutionFile) != comparisonSolutionFile)
            throw new ArgumentException("Comparison input must be a file inside V03.");
        this.comparisonSolutionFile = comparisonSolutionFile;
        smoothingPasses = Mathf.Clamp(motionSmoothingPasses, 0, 2);
        try
        {
            if (patientFitFile == null)
            {
                string handInput = comparisonSolutionFile;
                if (handInput == null && useBodycamSolution && File.Exists(Absolute(Folder + "/hand_solution_bodycam_v1.json"))) handInput = "hand_solution_bodycam_v1.json";
                if (handInput != null) patientFitFile = JsonUtility.FromJson<SolvedHands>(File.ReadAllText(Absolute(Folder + "/" + handInput))).patientFitFile;
            }
            if (string.IsNullOrEmpty(patientFitFile)) patientFitFile = "patient_fit.json";
            if (Path.GetFileName(patientFitFile) != patientFitFile) throw new ArgumentException("Patient fit must stay inside V03.");
            NativePatientFitPath = Absolute(Folder + "/" + patientFitFile);
            fit = JsonUtility.FromJson<Fit>(File.ReadAllText(NativePatientFitPath));
            observations = JsonUtility.FromJson<Observations>(File.ReadAllText(Absolute(Folder + "/source_observations.json"))).keys;
            if (fit == null || fit.keys == null || fit.keys.Length != observations.Length || observations.Length < 3) throw new InvalidDataException("Patient fit/source observations are incomplete.");
            cameraRight = V3(fit.camera.rightRig).normalized;
            cameraDown = V3(fit.camera.downRig).normalized;
            cameraForward = V3(fit.camera.forwardRig).normalized;
            scale = fit.camera.pixelsPerMetre;
            if (Vector3.Dot(Vector3.Cross(cameraRight, -cameraDown), cameraForward) < .999f || cameraForward.z >= 0f || scale <= 0f)
                throw new InvalidDataException("Source camera must be a proper measured-front frame, not a mirrored/back view.");
            preview = new PreviewRenderUtility();
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Generated/AnatomicalPatient/Build_20261022_174000/AnatomicalPatient_c9.prefab");
            if (asset == null) throw new InvalidOperationException("Saved anatomical patient is missing.");
            root = (GameObject)PrefabUtility.InstantiatePrefab(asset, preview.camera.gameObject.scene);
            root.hideFlags = HideFlags.HideAndDontSave;
            preview.AddSingleGO(root);
            rig = root.GetComponent<AnatomicalPatientRig>(); lab = root.GetComponent<AnatomicalPatientLab>();
            foreach (Behaviour component in root.GetComponentsInChildren<Behaviour>(true)) component.enabled = false;
            rig.SetAutomaticEvaluation(false); rig.SetInputSource(AnatomicalPatientRig.InputSource.Manual);
            if (!rig.Evaluate(rig.RestPose)) throw new InvalidOperationException("Anatomical rest evaluation failed.");
            lab.SetView(AnatomicalPatientLab.View.Skin); lab.SetUpperClothingVisible(true);
            forearmSkin = new Rib2AnatomicalForearmSkin(rig, fit.affectedLeft);
            fingers = new Rib2AnatomicalHandsContact.PatientFingers(rig, fit.affectedLeft);
            restingFingers = new Rib2AnatomicalHandsContact.PatientFingers(rig, !fit.affectedLeft);
            var data = new SerializedObject(rig);
            var limbs = data.FindProperty("limbs");
            for (int i = 0; i < limbs.arraySize; i++)
            {
                var limb = limbs.GetArrayElementAtIndex(i);
                if (limb.FindPropertyRelative("left").boolValue != fit.affectedLeft) continue;
                upper = limb.FindPropertyRelative("humerus").objectReferenceValue as Transform;
                lower = limb.FindPropertyRelative("ulna").objectReferenceValue as Transform;
            }
            if (upper == null || lower == null) throw new InvalidOperationException("Anatomical arm outputs missing.");
            restUpperLength = Vector3.Distance(upper.position, lower.position); restLowerLength = Vector3.Distance(lower.position, fingers.Wrist.position);
            var animator = root.GetComponentInChildren<Animator>(true);
            var map = new Dictionary<Transform, Transform>();
            var bindings = data.FindProperty("bodyBindings");
            for (int i = 0; i < bindings.arraySize; i++)
            {
                var b = bindings.GetArrayElementAtIndex(i);
                map.Add((Transform)b.FindPropertyRelative("source").objectReferenceValue, (Transform)b.FindPropertyRelative("output").objectReferenceValue);
            }
            handTip = map[animator.GetBoneTransform(fit.affectedLeft ? HumanBodyBones.LeftMiddleDistal : HumanBodyBones.RightMiddleDistal)];
            handPrevious = map[animator.GetBoneTransform(fit.affectedLeft ? HumanBodyBones.LeftMiddleIntermediate : HumanBodyBones.RightMiddleIntermediate)];
            Transform chestSource = animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest);
            if (chestSource == null || !map.TryGetValue(chestSource, out Transform chestOutput)) throw new InvalidOperationException("Support hand chest frame is missing.");
            supportBodyFrame = chestOutput;
            moving = Rib2AnatomicalHandsContact.Create(true, preview.camera.gameObject.scene, null, new Color(.08f, .8f, .95f), 30);
            support = Rib2AnatomicalHandsContact.Create(false, preview.camera.gameObject.scene, null, new Color(1f, .57f, .18f), 30);
            preview.AddSingleGO(moving.Object); preview.AddSingleGO(support.Object);
            moving.SetGrip(.52f, .85f, false); support.SetGrip(.8f, .8f, true); support.SetScale(.9f);
            AddSnapshots(root); AddSnapshots(moving.Object); AddSnapshots(support.Object);
            for (int i = 0; i < snapshots.Count; i++)
            {
                var s = snapshots[i];
                if (!s.source.transform.IsChildOf(root.transform) || (s.source.name != "CC_Base_Body" && s.source.name != "Shirt")) continue;
                var queryObject = new GameObject("Private skin query") { hideFlags = HideFlags.HideAndDontSave };
                queryObject.transform.SetParent(s.source.transform, false);
                chestQueries.Add(queryObject.AddComponent<MeshCollider>()); querySkins.Add(s);
            }
            if (chestQueries.Count == 0) throw new InvalidOperationException("Anatomical skin query meshes missing.");
            preview.camera.enabled = false; preview.camera.stereoTargetEye = StereoTargetEyeMask.None;
            preview.camera.orthographic = true; preview.camera.nearClipPlane = .01f; preview.camera.farClipPlane = 20f;
            preview.camera.clearFlags = CameraClearFlags.SolidColor; preview.camera.backgroundColor = new Color(.12f, .14f, .16f);
            preview.ambientColor = new Color(.35f, .35f, .35f);
            preview.lights[0].intensity = 1.05f; preview.lights[0].transform.rotation = Quaternion.LookRotation(cameraForward, -cameraDown) * Quaternion.Euler(25f, -30f, 0f);
            preview.lights[1].intensity = .8f; preview.lights[1].transform.rotation = Quaternion.Euler(30f, 130f, 0f);
            if (calibrationOnly) return; // Read-only native FK/skin export; no motion fitting or normal-scene writes.
            if (!TryLoadHandSolution()) throw new InvalidDataException("V03 hand_solution.json is missing. Export native hand geometry and fit the separate hand curve before review.");
            Sample(0f);
            guideSkinSamplingError = moving.ValidateSkinSampling();
            if (guideSkinSamplingError > .0001f) throw new InvalidOperationException("Guide collision skin samples disagree with Unity BakeMesh: " + guideSkinSamplingError + "m");
            Render(960, 540, 0, true); // Warm the disposable skin/material preview before its first recorded frame.
            Debug.Log("[Rib2AnatomicalV03] AnatomicalPatientRig only; patient S/E/W first, real-surface two-hand contact, source camera reprojection; no V01/V02 player/IK or scene saves.");
        }
        catch { Dispose(); throw; }
    }

    private void AddSnapshots(GameObject owner)
    {
        foreach (var source in owner.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var item = new Snapshot { source = source };
            snapshots.Add(item);
            item.mesh = new Mesh { name = "Anatomical V03 CPU skin", hideFlags = HideFlags.HideAndDontSave }; item.mesh.MarkDynamic();
            item.holder = new GameObject("V03 CPU snapshot") { hideFlags = HideFlags.HideAndDontSave, layer = source.gameObject.layer };
            item.holder.transform.SetParent(source.transform, false);
            item.holder.AddComponent<MeshFilter>().sharedMesh = item.mesh;
            item.renderer = item.holder.AddComponent<MeshRenderer>(); item.renderer.sharedMaterials = source.sharedMaterials;
            item.renderer.shadowCastingMode = ShadowCastingMode.Off; item.renderer.enabled = false;
        }
    }

    private AnatomicalPatientRig.Pose Pose(AnatomicalPatientRig.ArmPose arm)
    {
        var pose = rig.RestPose;
        // Measured on the actual private C9 rig, 2026-10-27: elbow stays lateral
        // to the torso, forearm approaches the front abdomen, palm faces shirt.
        // This is an authored resting pose; the source obscures the elbow.
        var rest = new AnatomicalPatientRig.ArmPose {
            shoulderElevation = 42.9f, elevationPlane = 42.1f,
            humeralRotation = 51.6f, elbowFlexion = 91f,
            forearmRotation = -8.2f, wristFlexion = 19.8f, wristDeviation = -15f
        };
        pose.leftArm = fit.affectedLeft ? arm : rest; pose.rightArm = fit.affectedLeft ? rest : arm;
        pose.neckDegrees = Vector3.zero;
        return pose;
    }

    private void Evaluate(AnatomicalPatientRig.ArmPose arm)
    {
        if (!EvaluateNativePose(Pose(arm))) throw new InvalidOperationException("Anatomical FK rejected the sampled pose.");
        // Fixed coarse curl is an authoring hypothesis. Synchronized bodycam
        // constrains the whole grasp, without recovering hidden finger joints.
        fingers.ApplyFromEvaluatedPose(patientCurlOverride >= 0f ? patientCurlOverride : solvedPatientCurl);
        restingFingers.ApplyFromEvaluatedPose(.18f);
        shoulderWorld = upper.position;
    }

    private bool EvaluateNativePose(AnatomicalPatientRig.Pose pose)
    {
        if (!rig.Evaluate(pose)) return false;
        forearmSkin?.Apply((fit.affectedLeft ? pose.leftArm : pose.rightArm).forearmRotation);
        return true;
    }

    // ★[미사용 2026-10-27] Early fixed-palm search retained for investigation.
    // Normal review requires the native-geometry hand_solution; it never falls
    // back to this disproven palm-centre correspondence or its old cache.
    private void PlaceMoving(Rib2AnatomicalHandsContact.Surface skin, Observation observation)
    {
        // A held grasp is one physical relationship, independent of camera or
        // nearest-vertex normal. Both palms oppose; expert fingers point back
        // toward the patient's wrist. Only one authored yaw is fitted globally.
        movingContact = skin.point;
        Vector3 palm = fingers.PalmarWorld;
        Vector3 distal = Quaternion.AngleAxis(fixedGrip.normalAngle, palm) * -fingers.DistalWorld;
        moving.PlacePalm(movingContact, distal, -palm);
    }

    private float HandCost(AnatomicalPatientRig.ArmPose arm, Observation observation)
    {
        Evaluate(arm);
        if (Vector3.Dot(fingers.PalmarWorld, cameraForward) < .05f) return float.PositiveInfinity;
        if (fittingPath && Quaternion.Angle(previousPatientRotation, fingers.Wrist.rotation) > 12.001f)
            return float.PositiveInfinity;
        PlaceMoving(fingers.PinnedSurface(pinnedPatientVertex), observation);
        Vector3 tip = handTip.position + (handTip.position - handPrevious.position) * .55f;
        float cost = (Project(moving.WristWorld) - V2(observation.leftWrist)).sqrMagnitude +
            (Project(moving.Joint(0).position) - V2(observation.leftPalm)).sqrMagnitude +
            1.2f * (Project(tip) - V2(observation.patientHandTip)).sqrMagnitude;
        // Visible-side prior from the reference grasp; source depth/roll is not
        // uniquely measured. This is not a camera-directed guide-hand rotation.
        float visibleSide = Mathf.Max(0f, -.1f - Vector3.Dot(fingers.PalmarWorld, cameraForward));
        return cost + 900f * visibleSide * visibleSide + .02f * arm.wristFlexion * arm.wristFlexion + .04f * arm.wristDeviation * arm.wristDeviation;
    }

    private AnatomicalPatientRig.ArmPose ImproveHand(AnatomicalPatientRig.ArmPose arm, Observation observation, float[] steps, int passes, AnatomicalPatientRig.ArmPose previous)
    {
        float best = HandCost(arm, observation);
        foreach (float step in steps)
            for (int pass = 0; pass < passes; pass++)
                for (int axis = 0; axis < 3; axis++)
                    foreach (float sign in new[] { -1f, 1f })
                    {
                        var trial = arm;
                        if (axis == 0) trial.forearmRotation = Mathf.Clamp(arm.forearmRotation + sign * step, -110f, 110f);
                        if (axis == 1) trial.wristFlexion = Mathf.Clamp(arm.wristFlexion + sign * step, -45f, 45f);
                        if (axis == 2) trial.wristDeviation = Mathf.Clamp(arm.wristDeviation + sign * step, -25f, 25f);
                        if (fittingPath && (Mathf.Abs(trial.forearmRotation - previous.forearmRotation) > 8.001f ||
                            Mathf.Abs(trial.wristFlexion - previous.wristFlexion) > 5.001f || Mathf.Abs(trial.wristDeviation - previous.wristDeviation) > 5.001f)) continue;
                        float cost = HandCost(trial, observation);
                        if (cost < best) { best = cost; arm = trial; }
                    }
        return arm;
    }

    private void FitFixedGrasp()
    {
        var reference = fit.keys[0].armPose;
        Observation observation = observations[0]; sourceShoulder = V2(observation.patientShoulder);
        float best = float.PositiveInfinity; Grip selected = default; int selectedVertex = -1;
        var selectedArm = reference;
        foreach (float along in new[] { .35f, .65f, .95f })
            foreach (float across in new[] { -.5f, 0f, .5f })
                foreach (float yaw in new[] { -80f, -60f, -40f, -20f, 0f, 20f, 40f, 60f, 80f })
                {
                    Evaluate(reference);
                    var surface = fingers.SurfaceAt(along, across, 0f);
                    if (Vector3.Dot(surface.normal, fingers.PalmarWorld) < .65f) continue;
                    pinnedPatientVertex = surface.vertexIndex;
                    fixedGrip = new Grip { along = along, across = across, normalAngle = yaw };
                    var candidate = reference; float initial = HandCost(candidate, observation);
                    foreach (float roll in new[] { -100f, -50f, 0f, 50f, 100f })
                    {
                        var trial = reference; trial.forearmRotation = roll;
                        float cost = HandCost(trial, observation);
                        if (cost < initial) { initial = cost; candidate = trial; }
                    }
                    candidate = ImproveHand(candidate, observation, new[] { 30f, 12f, 4f, 1f }, 2, reference);
                    float finalCost = HandCost(candidate, observation);
                    if (float.IsInfinity(finalCost)) continue;
                    var clearance = moving.RelaxFingerCollision(fingers);
                    // A visually plausible grasp must also fit actual patient
                    // skin. Do not choose a pixel fit that pierces the hand.
                    finalCost += 1e8f * clearance.afterMaximumInsetMetres * clearance.afterMaximumInsetMetres;
                    if (finalCost < best) { best = finalCost; selected = fixedGrip; selectedVertex = pinnedPatientVertex; selectedArm = candidate; }
                }
        if (selectedVertex < 0) throw new InvalidOperationException("No palmar fixed-grasp template matches the measured patient skin.");
        fixedGrip = selected; pinnedPatientVertex = selectedVertex; initialHand = selectedArm;
        Debug.Log("[Rib2AnatomicalV03] FIXED palm-to-palm grasp; patient skin vertex=" + pinnedPatientVertex + " region=" + selected.along + "," + selected.across + " yaw=" + selected.normalAngle + " reference cost=" + best);
    }

    private void FitContinuousHands()
    {
        // Follow source observations with a continuous wrist solution. Control
        // increments and ACTUAL output quaternion increments are both bounded;
        // fitting pixels cannot introduce a separate guide-hand flip.
        for (int frame = 0; frame < FrameCount; frame++)
        {
            fittingPath = frame > 0;
            Observation observation;
            var arm = SourceArm(SourceStart + frame / (float)Fps, out observation);
            sourceShoulder = V2(observation.patientShoulder);
            var previous = frame > 0 ? sampledArms[frame - 1] : initialHand;
            arm.forearmRotation = previous.forearmRotation; arm.wristFlexion = previous.wristFlexion; arm.wristDeviation = previous.wristDeviation;
            arm = ImproveHand(arm, observation, new[] { 5f, 2f, 1f }, 2, previous);
            Evaluate(arm);
            float rotationStep = fittingPath ? Quaternion.Angle(previousPatientRotation, fingers.Wrist.rotation) : 0f;
            if (rotationStep > 12.01f) throw new InvalidOperationException("Continuous hand rotation bound could not be satisfied at frame " + frame + ": " + rotationStep);
            sampledArms[frame] = arm; sampledGrips[frame] = fixedGrip;
            previousPatientRotation = fingers.Wrist.rotation;
        }
        fittingPath = false;
    }

    private string CurveSignature()
    {
        using (var hash = SHA256.Create())
        {
            var signature = new StringBuilder();
            foreach (string file in new[] { Folder + "/source_observations.json", Folder + "/patient_fit.json", "Assets/Editor/Rib2AnatomicalHandsReviewSession.cs", "Assets/Editor/Rib2AnatomicalHandsContact.cs", "Assets/Scripts/ClaudeScripts/Patient/AnatomicalPatientRig.cs" })
                signature.Append(Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(Absolute(file)))));
            signature.Append(AssetDatabase.GetAssetDependencyHash("Assets/Generated/AnatomicalPatient/Build_20261022_174000/AnatomicalPatient_c9.prefab"));
            signature.Append(QualitySettings.GetQualityLevel()).Append(':').Append((int)QualitySettings.skinWeights);
            return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(signature.ToString())));
        }
    }

    private bool TryLoadCurve()
    {
        string path = Absolute(Folder + "/authoring_cache.json");
        if (!File.Exists(path)) return false;
        var cache = JsonUtility.FromJson<CurveCache>(File.ReadAllText(path));
        if (cache == null || cache.signature != CurveSignature() || cache.arms == null || cache.grips == null || cache.arms.Length != FrameCount || cache.grips.Length != FrameCount) return false;
        Array.Copy(cache.arms, sampledArms, FrameCount); Array.Copy(cache.grips, sampledGrips, FrameCount);
        fixedGrip = cache.grips[0]; pinnedPatientVertex = cache.patientVertex;
        if (pinnedPatientVertex < 0) return false;
        Debug.Log("[Rib2AnatomicalV03] Loaded source/rig/code-hashed authoring curve; actual FK, skin contacts and measurements still evaluated on the patient.");
        return true;
    }

    public void SaveCurveCache()
    {
        if (solvedHandsLoaded) return;
        var cache = new CurveCache { signature = CurveSignature(), arms = sampledArms, grips = sampledGrips, patientVertex = pinnedPatientVertex };
        File.WriteAllText(Absolute(Folder + "/authoring_cache.json"), JsonUtility.ToJson(cache, true), new UTF8Encoding(false));
    }

    public void Sample(float time, bool fullDiagnostics = true, bool smoothMotion = true)
    {
        if (disposed) throw new ObjectDisposedException(nameof(Rib2AnatomicalHandsReviewSession));
        seconds = Mathf.Clamp(time, 0f, Duration);
        float sourceTime = SourceStart + seconds;
        SourceArm(sourceTime, out currentObservation);
        sourceShoulder = V2(currentObservation.patientShoulder);
        float f = seconds * Fps;
        int lo = Mathf.Min(Mathf.FloorToInt(f), FrameCount - 2), hi = lo + 1;
        float t = f - lo;
        var arm = smoothMotion ? SmoothArm(seconds) : BlendPose(presentationArms[lo], presentationArms[hi], t);
        currentPresentedArm = arm;
        Evaluate(arm);
        if (fullDiagnostics) BakePatientQueries();
        if (solvedSurfaceGrasp)
        {
            var handRotation = fingers.Wrist.rotation;
            moving.PlacePose(fingers.Wrist.position + handRotation * (smoothMotion ? SmoothGuidePosition(seconds) : Vector3.Lerp(solvedRelativeGuidePositions[lo], solvedRelativeGuidePositions[hi], t)),
                handRotation * (smoothMotion ? SmoothGuideRotation(lo, hi, t) : Quaternion.Slerp(solvedRelativeGuideRotations[lo], solvedRelativeGuideRotations[hi], t)));
        }
        else if (solvedHandsLoaded)
        {
            var skin = fingers.PinnedSurface(pinnedPatientVertex);
            movingContact = skin.point;
            moving.PlaceRotation(movingContact, fingers.Wrist.rotation * (smoothMotion ? SmoothGuideRotation(lo, hi, t) : Quaternion.Slerp(solvedRelativeGuideRotations[lo], solvedRelativeGuideRotations[hi], t)));
        }
        else PlaceMoving(fingers.PinnedSurface(pinnedPatientVertex), currentObservation);
        // 2026-10-27 user scope: arm/wrist and whole guide-hand placement only.
        // Keep a fixed coarse grasp; per-phalanx correction distorted the
        // source whole-glove direction and is outside the requested detail.
        fingerCollision = default;
        sampledSurfaceContact = fullDiagnostics ? fingers.MeasureSurfaceContact(moving) : default;
        if (solvedSurfaceGrasp) movingContact = fullDiagnostics ? sampledSurfaceContact.patientPoint : fingers.PalmCenterWorld;
        FitSupport(currentObservation);
    }

    private AnatomicalPatientRig.ArmPose SourceArm(float sourceTime, out Observation observation)
    {
        int lo = 0; while (lo + 1 < fit.keys.Length - 1 && fit.keys[lo + 1].sourceSeconds < sourceTime) lo++;
        int hi = lo + 1;
        float t = Mathf.InverseLerp(fit.keys[lo].sourceSeconds, fit.keys[hi].sourceSeconds, sourceTime);
        observation = BlendObservation(observations[lo], observations[hi], t);
        var arm = BlendPose(fit.keys[lo].armPose, fit.keys[hi].armPose, t);
        if (fit.densePatientKeys != null && fit.densePatientKeys.Length > 1)
        {
            int a = 0; while (a + 1 < fit.densePatientKeys.Length - 1 && fit.densePatientKeys[a + 1].sourceSeconds < sourceTime) a++;
            int b = a + 1;
            var dense = BlendPose(fit.densePatientKeys[a].armPose, fit.densePatientKeys[b].armPose, Mathf.InverseLerp(fit.densePatientKeys[a].sourceSeconds, fit.densePatientKeys[b].sourceSeconds, sourceTime));
            arm.shoulderElevation = dense.shoulderElevation; arm.elevationPlane = dense.elevationPlane;
            arm.humeralRotation = dense.humeralRotation; arm.elbowFlexion = dense.elbowFlexion;
        }
        return arm;
    }

    private void FitSupport(Observation observation)
    {
        if (supportWorldBundleLoaded) { ApplySupportWorldBundle(); return; }
        // The video support remains on the same chest region. Register it once
        // on actual torso skin, then follow the chest frame instead of chasing
        // moving shoulder/arm skin or source annotation noise every sample.
        if (supportAnchorReady)
        {
            supportContact = supportBodyFrame.TransformPoint(supportAnchorLocal);
            if (hasSynchronizedSupport) PlaceSynchronizedSupport();
            else support.PlaceUlnarHeel(supportContact, supportBodyFrame.TransformDirection(supportDistalLocal), supportBodyFrame.TransformDirection(supportNormalLocal));
            return;
        }
        BakeTorsoQueries();
        float best = float.PositiveInfinity; RaycastHit selected = default; bool found = false;
        Vector2 region = V2(observation.rightContactRegionCenter);
        // The exact heel pressure point is hidden. Search only inside its
        // annotated region, scoring the independently visible cuff/palm.
        for (int x = -15; x <= 15; x += 5)
            for (int y = -15; y <= 15; y += 5)
            {
                Vector2 point = region + new Vector2(x, y);
                if (!TryTorsoChestHit(point, out RaycastHit chest)) continue;
                support.PlaceUlnarHeel(chest.point, Direction(V2(observation.rightPalm) - V2(observation.rightWrist), -chest.normal), -chest.normal);
                float cost = (Project(support.WristWorld) - V2(observation.rightWrist)).sqrMagnitude +
                    (Project(support.Joint(0).position) - V2(observation.rightPalm)).sqrMagnitude + .1f * (x * x + y * y);
                if (cost < best) { best = cost; selected = chest; found = true; }
            }
        if (!found) throw new InvalidOperationException("Chest support authoring region misses actual torso-weighted skin; check source/model registration.");
        supportContact = selected.point;
        Vector3 distal = Direction(V2(observation.rightPalm) - V2(observation.rightWrist), -selected.normal);
        support.PlaceUlnarHeel(supportContact, distal, -selected.normal);
        supportAnchorLocal = supportBodyFrame.InverseTransformPoint(supportContact);
        supportNormalLocal = supportBodyFrame.InverseTransformDirection(-selected.normal);
        supportDistalLocal = supportBodyFrame.InverseTransformDirection(distal);
        supportAnchorReady = true;
        if (hasSynchronizedSupport) PlaceSynchronizedSupport();
        Debug.Log("[Rib2ChestSupport] Coarse fixed chest-frame anchor from actual torso skin; local=" + supportAnchorLocal.ToString("F4") + "; normal=" + supportNormalLocal.ToString("F4") + ". No per-finger fitting or per-frame arm-surface chasing.");
    }

    private void PlaceSynchronizedSupport()
    {
        Quaternion rotation = supportBodyFrame.rotation * synchronizedSupportChestRotation;
        support.PlacePose(supportContact - rotation * synchronizedSupportHeelLocalMetres, rotation);
    }

    private Vector3 ActiveSupportContactWorld => hasSynchronizedSupport ?
        support.Root.position + support.Root.rotation * synchronizedSupportHeelLocalMetres : support.UlnarHeelSurfaceWorld;

    private void BakePatientQueries()
    {
        for (int i = 0; i < querySkins.Count; i++)
        {
            var s = querySkins[i]; s.source.BakeMesh(s.mesh);
            chestQueries[i].sharedMesh = null; chestQueries[i].sharedMesh = s.mesh;
        }
    }

    private RaycastHit ChestHit(Vector2 pixel)
    {
        float bestDistance = float.PositiveInfinity; RaycastHit best = default; bool found = false;
        for (int radius = 0; radius <= 15 && !found; radius += 5)
            for (int j = 0; j < (radius == 0 ? 1 : 8); j++)
            {
                Vector2 test = pixel + new Vector2(Mathf.Cos(j * Mathf.PI / 4f), Mathf.Sin(j * Mathf.PI / 4f)) * radius;
                Vector2 offset = (test - sourceShoulder) / scale;
                Ray ray = new Ray(shoulderWorld + cameraRight * offset.x + cameraDown * offset.y - cameraForward * 3f, cameraForward);
                foreach (MeshCollider collider in chestQueries)
                    if (collider.Raycast(ray, out RaycastHit hit, 6f) && hit.distance < bestDistance)
                    { found = true; bestDistance = hit.distance; best = hit; }
            }
        if (!found) throw new InvalidOperationException("Observed chest-support region misses anatomical skin; registration must be corrected.");
        return best;
    }

    private Vector3 Direction(Vector2 pixelDirection, Vector3 normal)
    {
        Vector3 plane = cameraRight * pixelDirection.x + cameraDown * pixelDirection.y;
        float divisor = Vector3.Dot(normal, cameraForward);
        if (Mathf.Abs(divisor) > .12f) plane -= cameraForward * (Vector3.Dot(normal, plane) / divisor);
        else
        {
            // Near-edge-on palm does not identify depth; this tangent fallback
            // removes the normal component and its projection error is reported.
            plane = Vector3.ProjectOnPlane(plane, normal);
        }
        if (plane.sqrMagnitude < .000001f) throw new InvalidOperationException("Source hand direction cannot define a contact frame.");
        return plane.normalized;
    }

    public Vector2 Project(Vector3 world)
    {
        Vector3 delta = world - shoulderWorld;
        return sourceShoulder + new Vector2(Vector3.Dot(delta, cameraRight), Vector3.Dot(delta, cameraDown)) * scale;
    }

    public SampleResult Measure(bool fullDiagnostics = true)
    {
        var result = new SampleResult
        {
            sourceSeconds = SourceStart + seconds, shoulder = Project(upper.position), elbow = Project(lower.position), wrist = Project(fingers.Wrist.position),
            movingWrist = Project(moving.WristWorld), movingPalm = Project(moving.Joint(0).position), supportWrist = Project(support.WristWorld), supportPalm = Project(support.Joint(0).position),
            patientHandTip = Project(handTip.position + (handTip.position - handPrevious.position) * .55f), patientPalm = Project(fingers.PalmCenterWorld), movingContact = Project(movingContact), supportContact = Project(supportContact),
            shoulderWorld = upper.position, elbowWorld = lower.position, patientWristWorld = fingers.Wrist.position,
            patientHandRotation = fingers.Wrist.rotation, movingGuideRotation = moving.Root.rotation,
            upperLengthErrorMetres = Mathf.Abs(Vector3.Distance(upper.position, lower.position) - restUpperLength),
            lowerLengthErrorMetres = Mathf.Abs(Vector3.Distance(lower.position, fingers.Wrist.position) - restLowerLength),
            fingerCollision = fingerCollision, guideSkinSamplingErrorMetres = guideSkinSamplingError,
            sampledSurfaceContact = sampledSurfaceContact,
            measuredElbowDegrees = Vector3.Angle(lower.position - upper.position, fingers.Wrist.position - lower.position),
            contactGapMetres = fullDiagnostics ? Mathf.Max(solvedSurfaceGrasp ? sampledSurfaceContact.gapMetres : Vector3.Distance(moving.PalmSurfaceWorld, movingContact), Vector3.Distance(ActiveSupportContactWorld, supportContact)) : 0f,
            finite = true,
            notes = "Shoulder pan is aligned by construction; source clinical side/depth/hidden hand roll/fingers remain assumptions. Contact-point gap is a placement constraint, NOT source fidelity. Pixel residuals use independent observed wrist/palm regions."
        };
        result.measuredPatientFingerFlexion = fingers.FingersFlexionDegrees;
        result.patientPalmarCameraDot = Vector3.Dot(fingers.PalmarWorld, cameraForward);
        result.patientPalmOutwardDot = Vector3.Dot(fingers.PalmarWorld, (fingers.Wrist.position - supportBodyFrame.position).normalized);
        result.appliedWristFlexionDegrees = currentPresentedArm.wristFlexion;
        result.patientHandTipErrorPixels = Vector2.Distance(result.patientHandTip, V2(currentObservation.patientHandTip));
        if (fullDiagnostics)
        {
            if (hasSynchronizedSupport && synchronizedSupportVertex >= 0)
            {
                support.CopyBakedWorldVertices(regionVertices);
                result.supportPinnedVertexSamplingErrorMetres = Vector3.Distance(regionVertices[synchronizedSupportVertex], ActiveSupportContactWorld);
                if (result.supportPinnedVertexSamplingErrorMetres > .0001f)
                    throw new InvalidOperationException("Support contact anchor disagrees with actual Unity BakeMesh by " + result.supportPinnedVertexSamplingErrorMetres + "m.");
            }
            var regionErrors = MeasureGloveRegions(V2(currentObservation.leftPalm), V2(currentObservation.rightPalm));
            result.movingGloveRegionErrorPixels = regionErrors.x; result.supportGloveRegionErrorPixels = regionErrors.y;
            var intrusion = fingers.MeasureGuideJointIntrusion(moving);
            result.guideJointsTested = intrusion.testedJointCount; result.guideJointsIntruding = intrusion.intrudingJointCount;
            result.guideJointMaximumInsetMetres = intrusion.maximumInsetMetres;
        }
        else result.notes = "Live preview: bone projection only. Surface/contact diagnostics omitted; zero diagnostic fields are not validation results.";
        result.elbowErrorPixels = Vector2.Distance(result.elbow, V2(currentObservation.patientElbow));
        result.wristErrorPixels = Vector2.Distance(result.wrist, V2(currentObservation.patientWrist));
        result.movingWristErrorPixels = Vector2.Distance(result.movingWrist, V2(currentObservation.leftWrist));
        result.movingPalmErrorPixels = Vector2.Distance(result.movingPalm, V2(currentObservation.leftPalm));
        result.supportWristErrorPixels = Vector2.Distance(result.supportWrist, V2(currentObservation.rightWrist));
        result.supportPalmErrorPixels = Vector2.Distance(result.supportPalm, V2(currentObservation.rightPalm));
        result.finite = Finite(result.elbow) && Finite(result.wrist) && Finite(result.movingPalm) && Finite(result.supportPalm);
        return result;
    }

    public Vector2 MeasureGloveRegions(Vector2 movingMarker, Vector2 supportMarker)
    { return new Vector2(GloveRegionDistance(moving, movingMarker), GloveRegionDistance(support, supportMarker)); }

    public Vector2 MeasureMovingGloveEnvelope()
    {
        // Whole posed glove extent from its cuff, independent of Palm0 labels.
        // Projection discards depth; this checks image silhouette direction only.
        Vector2 wrist = Project(moving.WristWorld), extent = Vector2.zero;
        foreach (var skin in moving.Renderers)
        {
            skin.BakeMesh(regionMesh); regionMesh.GetVertices(regionVertices);
            foreach (var vertex in regionVertices)
            {
                Vector2 delta = Project(skin.transform.TransformPoint(vertex)) - wrist;
                if (delta.sqrMagnitude > extent.sqrMagnitude) extent = delta;
            }
        }
        return extent;
    }

    private float GloveRegionDistance(Rib2AnatomicalHandsContact.Hand hand, Vector2 marker)
    {
        // The source marker is an approximate visible/inferred glove region,
        // not an identified Palm0 or MCP joint. Measure its distance to the
        // actual projected hand mesh. Occlusion is deliberately not inferred.
        float best = float.PositiveInfinity;
        foreach (var skin in hand.Renderers)
        {
            skin.BakeMesh(regionMesh); regionMesh.GetVertices(regionVertices); regionPixels.Clear();
            foreach (var vertex in regionVertices) regionPixels.Add(Project(skin.transform.TransformPoint(vertex)));
            if (!regionTriangles.TryGetValue(skin.sharedMesh, out int[] triangles))
            { triangles = skin.sharedMesh.triangles; regionTriangles.Add(skin.sharedMesh, triangles); }
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                Vector2 a = regionPixels[triangles[i]], b = regionPixels[triangles[i + 1]], c = regionPixels[triangles[i + 2]];
                float area = Cross2(b - a, c - a);
                float first = Cross2(b - a, marker - a), second = Cross2(c - b, marker - b), third = Cross2(a - c, marker - c);
                bool negative = first < 0f || second < 0f || third < 0f, positive = first > 0f || second > 0f || third > 0f;
                if (Mathf.Abs(area) > .00001f && !(negative && positive)) return 0f;
                best = Mathf.Min(best, SegmentDistanceSquared(marker, a, b));
                best = Mathf.Min(best, SegmentDistanceSquared(marker, b, c));
                best = Mathf.Min(best, SegmentDistanceSquared(marker, c, a));
            }
        }
        return Mathf.Sqrt(best);
    }

    private static float Cross2(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    private static float SegmentDistanceSquared(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 edge = b - a;
        // This clamp selects the finite triangle edge, not motion or depth.
        float t = edge.sqrMagnitude > .000001f ? Mathf.Clamp01(Vector2.Dot(point - a, edge) / edge.sqrMagnitude) : 0f;
        return (point - a - edge * t).sqrMagnitude;
    }

    public Texture Render(int width, int height, int view, bool showHands = true)
    {
        bool began = false;
        try
        {
            foreach (Snapshot s in snapshots) s.enabled = s.source.enabled;
            foreach (Snapshot s in snapshots)
            {
                bool guide = s.source.transform.IsChildOf(moving.Object.transform) || s.source.transform.IsChildOf(support.Object.transform);
                bool visible = s.enabled && s.source.gameObject.activeInHierarchy && !s.source.forceRenderingOff && (!guide || showHands);
                s.renderer.enabled = visible;
                if (visible) { s.source.BakeMesh(s.mesh); s.source.enabled = false; }
            }
            preview.camera.aspect = (float)width / height;
            preview.camera.orthographic = view != 3;
            preview.camera.ResetProjectionMatrix();
            if (view == 3) ConfigureBodycamCamera(seconds);
            else if (view == 0)
            {
                Vector3 center = shoulderWorld - cameraRight * ((sourceShoulder.x - 480f) / scale) - cameraDown * ((sourceShoulder.y - 270f) / scale);
                preview.camera.orthographicSize = 540f / (2f * scale);
                preview.camera.transform.SetPositionAndRotation(center - cameraForward * 3f, Quaternion.LookRotation(cameraForward, -cameraDown));
            }
            else
            {
                Vector3 center = new Vector3(-.12f, 1.4f, .05f);
                Vector3 offset = view == 2 ? new Vector3(0f, 0f, 2.5f) : new Vector3(-1.4f, .5f, 2f);
                preview.camera.orthographicSize = .7f;
                preview.camera.transform.position = center + offset; preview.camera.transform.LookAt(center, view == 2 ? Vector3.right : Vector3.up);
            }
            preview.BeginPreview(new Rect(0f, 0f, width, height), GUIStyle.none); began = true;
            preview.Render(); var image = preview.EndPreview(); began = false; return image;
        }
        finally
        {
            try { if (began) preview.EndPreview(); }
            finally { foreach (Snapshot s in snapshots) { s.source.enabled = s.enabled; s.renderer.enabled = false; } }
        }
    }

    private static AnatomicalPatientRig.ArmPose BlendPose(AnatomicalPatientRig.ArmPose a, AnatomicalPatientRig.ArmPose b, float t)
    {
        return new AnatomicalPatientRig.ArmPose {
            shoulderElevation = Mathf.Lerp(a.shoulderElevation, b.shoulderElevation, t), elevationPlane = Mathf.LerpAngle(a.elevationPlane, b.elevationPlane, t),
            humeralRotation = Mathf.LerpAngle(a.humeralRotation, b.humeralRotation, t), elbowFlexion = Mathf.Lerp(a.elbowFlexion, b.elbowFlexion, t),
            forearmRotation = Mathf.LerpAngle(a.forearmRotation, b.forearmRotation, t), wristFlexion = Mathf.Lerp(a.wristFlexion, b.wristFlexion, t), wristDeviation = Mathf.Lerp(a.wristDeviation, b.wristDeviation, t)
        };
    }
    private static float[] Blend(float[] a, float[] b, float t) => new[] { Mathf.Lerp(a[0], b[0], t), Mathf.Lerp(a[1], b[1], t) };
    private static Observation BlendObservation(Observation a, Observation b, float t) => new Observation {
        patientShoulder = Blend(a.patientShoulder, b.patientShoulder, t), patientElbow = Blend(a.patientElbow, b.patientElbow, t), patientWrist = Blend(a.patientWrist, b.patientWrist, t),
        patientHandTip = Blend(a.patientHandTip, b.patientHandTip, t),
        leftWrist = Blend(a.leftWrist, b.leftWrist, t), leftPalm = Blend(a.leftPalm, b.leftPalm, t), rightWrist = Blend(a.rightWrist, b.rightWrist, t), rightPalm = Blend(a.rightPalm, b.rightPalm, t), rightContactRegionCenter = Blend(a.rightContactRegionCenter, b.rightContactRegionCenter, t)
    };
    public static string SourcePath(int index) => Absolute(Folder + "/source_frames/source_" + Mathf.Clamp(index, 0, FrameCount - 1).ToString("D5") + ".jpg");
    private static string Absolute(string path) => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.dataPath), path));
    private static Vector3 V3(float[] a) => new Vector3(a[0], a[1], a[2]);
    private static Vector2 V2(float[] a) => new Vector2(a[0], a[1]);
    private static bool Finite(Vector2 p) => !float.IsNaN(p.x) && !float.IsInfinity(p.x) && !float.IsNaN(p.y) && !float.IsInfinity(p.y);
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        if (fingers != null) fingers.Dispose();
        if (restingFingers != null) restingFingers.Dispose();
        DisposeTorsoQueries();
        UnityEngine.Object.DestroyImmediate(regionMesh);
        foreach (var s in snapshots) { if (s.holder != null) UnityEngine.Object.DestroyImmediate(s.holder); if (s.mesh != null) UnityEngine.Object.DestroyImmediate(s.mesh); }
        forearmSkin?.Dispose();
        if (moving != null) moving.Dispose(); if (support != null) support.Dispose();
        if (preview != null) preview.Cleanup();
    }
}
