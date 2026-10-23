using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class AnatomicalPatientBuild
{
    // This verifies observable kinematic invariants of the real output, not the
    // solver's diagnostic/input fields. Dependencies: all requested joints,
    // measured rest landmarks, output joint centres and output finger positions.
    // Collision, contact, force and clinical ROM are deliberately unmeasured.
    private const string MotionVerificationMenu = "GuideChuna/시험/해부학 환자/연속 동작 분절 검증";
    private const int MotionVerificationSteps = 120;

    [Serializable] private sealed class MotionVerificationReport
    {
        public bool success;
        public string utc, prefab, prefabDependencyHash, rigSha256, sequenceSha256, folder, rawCsv;
        public int motions, trajectoryFrames, covarianceFrames, driftChecks, nonFiniteValues;
        public float maximumUpperLengthErrorMetres, maximumLowerLengthErrorMetres;
        public float maximumElbowAngleErrorDegrees, minimumHingePoleDot = 1f, maximumOutsideHingeDegrees;
        public float maximumOutsideHingeDeviationFromRestDegrees;
        public float[] measuredRestOutsideHingeDegrees = new float[2];
        public float maximumRepeatPositionErrorMetres, maximumRepeatRotationErrorDegrees;
        public float maximumWristCovarianceRotationDegrees, maximumWristCovariancePalmDegrees;
        public float maximumWristCovarianceRadialDegrees, maximumWristCovarianceLongitudinalDegrees;
        public float minimumPositiveFlexionPalmarDot = 1f, minimumPositiveDeviationRadialDot = 1f;
        public float maximumAnatomyPalmErrorDegrees, maximumAnatomyRadialErrorDegrees;
        public float rib2MinimumHeldForearmCranialDot = 1f;
        public float rib2MaximumCyclePositionErrorMetres, rib2MaximumCycleRotationErrorDegrees;
        public float rib2MaximumReturnPositionErrorMetres, rib2MaximumReturnRotationErrorDegrees;
        public List<MotionRib2HeldObservation> rib2Held = new List<MotionRib2HeldObservation>();
        public MotionMirrorObservation restMirror;
        public List<MotionSeriesResult> series = new List<MotionSeriesResult>();
        public List<MotionMirrorObservation> bilateralObservations = new List<MotionMirrorObservation>();
        public List<string> failures = new List<string>();
        public string scope = "Saved generated prefab, isolated Editor PreviewScene, same runtime Evaluate(Pose), 121 inclusive time samples per motion and side. Invariants are engineering checks, not clinical validation.";
        public string bilateralScope = "Absolute left/right mirror errors include measured c9 author asymmetry. Rest baseline is recorded separately; bilateral observations are not physiological pass/fail thresholds.";
        public string unsupported = "Human Play and Quest; clinical or patient-specific joint limits; muscle forces, ligaments, joint surfaces; skin self-contact, torso/bed collision and tissue deformation accuracy; exact video-reconstructed 3D angles or contact forces remain unverified. Rib2Study is a study sequence, not a validated treatment procedure.";
        public string thresholds = "Segment length and repeat positions <0.00001 m; elbow angle <0.05 deg; signed off-hinge deviation from each measured native rest <0.05 deg (raw native carry retained and reported); signed hinge pole dot >0.999 when 2<flexion<158 deg; repeated rotations <0.1 deg; wrist rigid-roll covariance <0.1 deg; positive isolated wrist direction dot >0.1; baked anatomy/skin palm and radial alignment <1 deg; Rib2 held forearm cranial dot >0.5 and cycle/return closure <0.00001 m, <0.1 deg. These are numerical consistency tolerances, not anatomical ROM limits.";
        public string exception;
    }

    [Serializable] private sealed class MotionSeriesResult
    {
        public string motion, label, side;
        public int frames;
        public float durationSeconds, maximumHandStepMetres, maximumHandRotationStepDegrees;
        public float maximumHeadStepMetres, maximumHeadRotationStepDegrees;
        public float minimumMeasuredElbowDegrees = float.PositiveInfinity, maximumMeasuredElbowDegrees = float.NegativeInfinity;
        public float maximumHumerusMotionDegrees, maximumScapulaMotionDegrees, maximumRadiusMotionDegrees;
        public float[] maximumCervicalSegmentMotionDegrees = new float[7];
    }

    [Serializable] private sealed class MotionMirrorObservation
    {
        public string motion;
        public int samples;
        public float maximumShoulderErrorMetres, maximumElbowErrorMetres, maximumWristErrorMetres;
        public float maximumPalmErrorDegrees, maximumRadialErrorDegrees, maximumLongitudinalErrorDegrees;
    }

    [Serializable] private sealed class MotionRib2HeldObservation
    {
        public string side;
        public float seconds, requestedElbowDegrees, measuredElbowDegrees, measuredForearmCranialDot;
        public Vector3 shoulder, elbow, wrist, forearmDirection, palm;
    }

    private sealed class MotionLimbMeasurement
    {
        public bool left;
        public Transform clavicle, scapula, humerus, ulna, radius, hand, index, middle, little;
        public Vector3 restBend;
        public Quaternion restHumerusRotation, restScapulaRotation, restRadiusRotation;
        public float upperLength, lowerLength;
        public float restOutsideHingeDegrees;
    }

    private struct MotionOutputFrame
    {
        public Vector3 shoulder, elbow, wrist, palm, radial, longitudinal;
        public Quaternion handRotation;
    }

    private struct MotionTransformState
    {
        public Vector3 position;
        public Quaternion rotation;
    }

    [MenuItem(MotionVerificationMenu)]
    public static void VerifyContinuousMotionSegments()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play before isolated continuous-motion verification.");
        string folder = "Captures/AnatomicalStudy_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        Directory.CreateDirectory(folder);
        var report = new MotionVerificationReport { utc = DateTimeOffset.UtcNow.ToString("o"), folder = Path.GetFullPath(folder), rawCsv = Path.GetFullPath(folder + "/raw.csv") };
        Scene scene = default;
        GameObject copy = null, anatomy = null;
        try
        {
            // Durable receipt is authoritative. A stale Temp receipt must not
            // redirect this run to another saved patient.
            string receiptPath = OutputFolder + "/LatestBuild.json";
            if (!File.Exists(receiptPath)) throw new FileNotFoundException("No saved generated patient receipt.", receiptPath);
            BuildReceipt receipt = JsonUtility.FromJson<BuildReceipt>(File.ReadAllText(receiptPath));
            if (receipt == null || string.IsNullOrEmpty(receipt.prefab) || !receipt.prefab.StartsWith(OutputFolder + "/", StringComparison.Ordinal))
                throw new InvalidOperationException("The durable receipt does not identify a generated patient.");
            report.prefab = receipt.prefab;
            report.prefabDependencyHash = AssetDatabase.GetAssetDependencyHash(receipt.prefab).ToString();
            report.rigSha256 = MotionFileHash("Assets/Scripts/ClaudeScripts/Patient/AnatomicalPatientRig.cs");
            report.sequenceSha256 = MotionFileHash("Assets/Scripts/ClaudeScripts/Patient/AnatomicalMotionSequence.cs");
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(receipt.prefab);
            if (asset == null) throw new InvalidOperationException("Saved generated prefab is missing: " + receipt.prefab);
            scene = EditorSceneManager.NewPreviewScene();
            copy = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            var rig = copy.GetComponent<AnatomicalPatientRig>();
            if (rig == null) throw new InvalidOperationException("Saved prefab has no AnatomicalPatientRig.");
            rig.SetAutomaticEvaluation(false);
            rig.SetInputSource(AnatomicalPatientRig.InputSource.Manual);
            MotionEvaluate(rig, rig.RestPose);
            var serialized = new SerializedObject(rig);
            var limbs = new[] { MotionReadLimb(serialized, copy, true), MotionReadLimb(serialized, copy, false) };
            report.measuredRestOutsideHingeDegrees[0] = limbs[0].restOutsideHingeDegrees;
            report.measuredRestOutsideHingeDegrees[1] = limbs[1].restOutsideHingeDegrees;
            var neckProperty = serialized.FindProperty("neck");
            Transform head = MotionTransform(neckProperty, "head");
            var cervical = new Transform[7];
            var restCervicalRotations = new Quaternion[7];
            for (int i = 0; i < cervical.Length; i++)
            {
                cervical[i] = (Transform)neckProperty.FindPropertyRelative("segments").GetArrayElementAtIndex(i).objectReferenceValue;
                if (cervical[i] == null) throw new InvalidOperationException("Missing cervical segment " + i);
                restCervicalRotations[i] = cervical[i].localRotation;
            }
            Transform[] outputs = rig.RigSpace.GetComponentsInChildren<Transform>(true);
            Vector3 right = serialized.FindProperty("anatomicalBasis").FindPropertyRelative("right").vector3Value.normalized;
            Vector3 mirrorCentre = (limbs[0].clavicle.localPosition + limbs[1].clavicle.localPosition) * .5f;
            report.restMirror = new MotionMirrorObservation { motion = "Measured author rest", samples = 1 };
            MotionAccumulateMirror(report.restMirror, MotionReadFrame(limbs[0]), MotionReadFrame(limbs[1]), right, mirrorCentre);
            Debug.Log("[AnatomicalMotionVerification] Start: saved=" + report.prefab + "; independent PreviewScene; actual bones/finger landmarks; source hash=" + report.rigSha256 + "; report=" + report.folder);
            using (var csv = new StreamWriter(report.rawCsv, false, new UTF8Encoding(false)))
            {
                csv.WriteLine(MotionCsvHeader());
                foreach (AnatomicalMotionSequence.Motion motion in Enum.GetValues(typeof(AnatomicalMotionSequence.Motion)))
                {
                    float duration = AnatomicalMotionSequence.Duration(motion);
                    if (duration <= 0f || !MotionFinite(duration)) throw new InvalidOperationException("Invalid motion duration: " + motion);
                    var bilateral = new MotionMirrorObservation { motion = motion.ToString() };
                    var leftFrames = new MotionOutputFrame[MotionVerificationSteps + 1];
                    for (int side = 0; side < 2; side++)
                    {
                        MotionLimbMeasurement limb = limbs[side];
                        var series = new MotionSeriesResult { motion = motion.ToString(), label = AnatomicalMotionSequence.Label(motion), side = limb.left ? "L" : "R", durationSeconds = duration };
                        MotionOutputFrame previous = default;
                        Vector3 previousHead = default;
                        Quaternion previousHeadRotation = Quaternion.identity;
                        for (int sample = 0; sample <= MotionVerificationSteps; sample++)
                        {
                            float seconds = duration * sample / MotionVerificationSteps;
                            AnatomicalPatientRig.Pose pose = AnatomicalMotionSequence.Sample(rig.RestPose, motion, seconds, limb.left);
                            MotionEvaluate(rig, pose);
                            MotionCheckFinite(outputs, report);
                            MotionOutputFrame measured = MotionReadFrame(limb);
                            AnatomicalPatientRig.ArmPose requested = limb.left ? pose.leftArm : pose.rightArm;
                            float signedElbow, outsideHinge, poleDot;
                            MotionMeasureElbow(limb, measured, out signedElbow, out outsideHinge, out poleDot);
                            report.maximumUpperLengthErrorMetres = Mathf.Max(report.maximumUpperLengthErrorMetres, Mathf.Abs(Vector3.Distance(measured.shoulder, measured.elbow) - limb.upperLength));
                            report.maximumLowerLengthErrorMetres = Mathf.Max(report.maximumLowerLengthErrorMetres, Mathf.Abs(Vector3.Distance(measured.elbow, measured.wrist) - limb.lowerLength));
                            // Compare supported requests directly. Out-of-presentation-range requests
                            // remain in raw.csv; do not hide them behind an unreported clamp.
                            float restFlexion = limb.left ? rig.RestPose.leftArm.elbowFlexion : rig.RestPose.rightArm.elbowFlexion;
                            if (requested.elbowFlexion >= Mathf.Min(0f, restFlexion) && requested.elbowFlexion <= 160f)
                                report.maximumElbowAngleErrorDegrees = Mathf.Max(report.maximumElbowAngleErrorDegrees, Mathf.Abs(signedElbow - requested.elbowFlexion));
                            report.maximumOutsideHingeDegrees = Mathf.Max(report.maximumOutsideHingeDegrees, Mathf.Abs(outsideHinge));
                            report.maximumOutsideHingeDeviationFromRestDegrees = Mathf.Max(report.maximumOutsideHingeDeviationFromRestDegrees, Mathf.Abs(outsideHinge - limb.restOutsideHingeDegrees));
                            // At a straight elbow the projected bend has zero length and no pole.
                            if (signedElbow > 2f && signedElbow < 158f) report.minimumHingePoleDot = Mathf.Min(report.minimumHingePoleDot, poleDot);
                            series.minimumMeasuredElbowDegrees = Mathf.Min(series.minimumMeasuredElbowDegrees, signedElbow);
                            series.maximumMeasuredElbowDegrees = Mathf.Max(series.maximumMeasuredElbowDegrees, signedElbow);
                            series.maximumHumerusMotionDegrees = Mathf.Max(series.maximumHumerusMotionDegrees, Quaternion.Angle(limb.humerus.localRotation, limb.restHumerusRotation));
                            series.maximumScapulaMotionDegrees = Mathf.Max(series.maximumScapulaMotionDegrees, Quaternion.Angle(limb.scapula.localRotation, limb.restScapulaRotation));
                            series.maximumRadiusMotionDegrees = Mathf.Max(series.maximumRadiusMotionDegrees, Quaternion.Angle(limb.radius.localRotation, limb.restRadiusRotation));
                            for (int segment = 0; segment < 7; segment++) series.maximumCervicalSegmentMotionDegrees[segment] = Mathf.Max(series.maximumCervicalSegmentMotionDegrees[segment], Quaternion.Angle(cervical[segment].localRotation, restCervicalRotations[segment]));
                            if (sample > 0)
                            {
                                series.maximumHandStepMetres = Mathf.Max(series.maximumHandStepMetres, Vector3.Distance(previous.wrist, measured.wrist));
                                series.maximumHandRotationStepDegrees = Mathf.Max(series.maximumHandRotationStepDegrees, Quaternion.Angle(previous.handRotation, measured.handRotation));
                                series.maximumHeadStepMetres = Mathf.Max(series.maximumHeadStepMetres, Vector3.Distance(previousHead, head.localPosition));
                                series.maximumHeadRotationStepDegrees = Mathf.Max(series.maximumHeadRotationStepDegrees, Quaternion.Angle(previousHeadRotation, head.localRotation));
                            }
                            previous = measured; previousHead = head.localPosition; previousHeadRotation = head.localRotation;
                            MotionWriteCsv(csv, "trajectory", motion.ToString(), AnatomicalMotionSequence.PhaseLabel(motion, seconds), limb, sample, seconds, requested, pose.neckDegrees, measured, signedElbow, outsideHinge, poleDot, head, cervical);
                            if (side == 0) leftFrames[sample] = measured;
                            else
                            {
                                MotionAccumulateMirror(bilateral, leftFrames[sample], measured, right, mirrorCentre);
                                bilateral.samples++;
                            }
                            series.frames++; report.trajectoryFrames++;
                        }
                        report.series.Add(series);
                        MotionCheckRepeat(rig, outputs, motion, limb.left, report);
                    }
                    report.bilateralObservations.Add(bilateral);
                    report.motions++;
                }
                MotionVerifyWristCovariance(rig, limbs, head, cervical, csv, report);
                Vector3 cranial = serialized.FindProperty("anatomicalBasis").FindPropertyRelative("up").vector3Value.normalized;
                MotionVerifyRib2(rig, limbs, cranial, outputs, head, cervical, csv, report);
            }
            anatomy = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(AnatomyPath), scene);
            foreach (AnatomicalMotionSequence.Motion motion in new[] { AnatomicalMotionSequence.Motion.Forearm, AnatomicalMotionSequence.Motion.CoupledWrist, AnatomicalMotionSequence.Motion.Rib2Study })
            {
                // A few actual CPU-baked hand meshes cover motion combinations;
                // full-trajectory finger landmarks above avoid thousands of bake/log calls.
                float t = AnatomicalMotionSequence.Duration(motion) * .375f;
                foreach (bool left in new[] { true, false })
                {
                    MotionEvaluate(rig, AnatomicalMotionSequence.Sample(rig.RestPose, motion, t, left));
                    foreach (PalmMeasurement palm in MeasurePalmGeometry(copy, anatomy, motion + "_" + (left ? "L" : "R") + "_37.5pct"))
                    {
                        report.maximumAnatomyPalmErrorDegrees = Mathf.Max(report.maximumAnatomyPalmErrorDegrees, palm.palmErrorDegrees);
                        report.maximumAnatomyRadialErrorDegrees = Mathf.Max(report.maximumAnatomyRadialErrorDegrees, palm.radialErrorDegrees);
                    }
                }
            }
            MotionAssert(report, report.nonFiniteValues == 0, "Non-finite output transform values.");
            MotionAssert(report, report.maximumUpperLengthErrorMetres < .00001f && report.maximumLowerLengthErrorMetres < .00001f, "Segment lengths changed.");
            MotionAssert(report, report.maximumElbowAngleErrorDegrees < .05f, "Actual signed elbow flexion differs from supported input.");
            MotionAssert(report, report.maximumOutsideHingeDeviationFromRestDegrees < .05f && report.minimumHingePoleDot > .999f, "Elbow deviated from its measured native off-hinge baseline or bent against the native pole.");
            MotionAssert(report, report.maximumRepeatPositionErrorMetres < .00001f && report.maximumRepeatRotationErrorDegrees < .1f, "Same pose after another motion accumulated transform drift.");
            MotionAssert(report, report.maximumWristCovarianceRotationDegrees < .1f && report.maximumWristCovariancePalmDegrees < .1f && report.maximumWristCovarianceRadialDegrees < .1f && report.maximumWristCovarianceLongitudinalDegrees < .1f, "Wrist bends do not follow the rolled hand frame.");
            MotionAssert(report, report.minimumPositiveFlexionPalmarDot > .1f, "Positive isolated wrist flexion moves away from the measured palmar direction.");
            MotionAssert(report, report.minimumPositiveDeviationRadialDot > .1f, "Positive isolated wrist deviation moves away from the measured radial direction.");
            MotionAssert(report, report.maximumAnatomyPalmErrorDegrees < 1f && report.maximumAnatomyRadialErrorDegrees < 1f, "Baked anatomy hand and c9 finger landmarks disagree.");
            MotionAssert(report, report.rib2MinimumHeldForearmCranialDot > .5f, "Rib2 held forearm does not bend toward the cranial side.");
            MotionAssert(report, report.rib2MaximumCyclePositionErrorMetres < .00001f && report.rib2MaximumCycleRotationErrorDegrees < .1f, "Rib2 held poses do not close after each repetition.");
            MotionAssert(report, report.rib2MaximumReturnPositionErrorMetres < .00001f && report.rib2MaximumReturnRotationErrorDegrees < .1f, "Rib2 final pose does not return to its initial relaxed pose.");
            report.success = report.failures.Count == 0;
        }
        catch (Exception ex)
        {
            report.success = false;
            report.exception = ex.ToString();
            report.failures.Add(ex.Message);
        }
        finally
        {
            if (anatomy != null) UnityEngine.Object.DestroyImmediate(anatomy);
            if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(folder + "/verification.json", json, new UTF8Encoding(false));
            Directory.CreateDirectory("Temp/model-audit");
            File.WriteAllText("Temp/model-audit/motion-verification.json", json, new UTF8Encoding(false));
            string message = "[AnatomicalMotionVerification] " + (report.success ? "PASS" : "FAIL") + "; trajectory=" + report.trajectoryFrames + "; covariance=" + report.covarianceFrames + "; flexion error=" + report.maximumElbowAngleErrorDegrees.ToString("F5", CultureInfo.InvariantCulture) + " deg; wrist covariance=" + report.maximumWristCovarianceRotationDegrees.ToString("F5", CultureInfo.InvariantCulture) + " deg; report=" + report.folder + "/verification.json";
            if (report.success) Debug.Log(message);
            else Debug.LogError(message + "; " + string.Join(" | ", report.failures));
        }
    }

    [MenuItem(MotionVerificationMenu, true)]
    private static bool MotionVerificationAvailable() => !EditorApplication.isPlayingOrWillChangePlaymode;

    private static MotionLimbMeasurement MotionReadLimb(SerializedObject data, GameObject root, bool left)
    {
        SerializedProperty limb = null;
        var array = data.FindProperty("limbs");
        for (int i = 0; i < array.arraySize; i++)
        {
            var candidate = array.GetArrayElementAtIndex(i);
            if (candidate.FindPropertyRelative("left").boolValue == left) { limb = candidate; break; }
        }
        if (limb == null) throw new InvalidOperationException("Missing anatomical limb: " + (left ? "L" : "R"));
        var animator = root.GetComponentInChildren<Animator>(true);
        if (animator == null || !animator.isHuman) throw new InvalidOperationException("No c9 Humanoid input for measured finger landmarks.");
        var result = new MotionLimbMeasurement
        {
            left = left, clavicle = MotionTransform(limb, "clavicle"), scapula = MotionTransform(limb, "scapula"),
            humerus = MotionTransform(limb, "humerus"), ulna = MotionTransform(limb, "ulna"), radius = MotionTransform(limb, "radius"), hand = MotionTransform(limb, "hand"),
            restBend = limb.FindPropertyRelative("restBendDirection").vector3Value,
            index = OutputFor(data, animator.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal)),
            middle = OutputFor(data, animator.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal)),
            little = OutputFor(data, animator.GetBoneTransform(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal))
        };
        result.upperLength = Vector3.Distance(result.humerus.localPosition, result.ulna.localPosition);
        result.lowerLength = Vector3.Distance(result.ulna.localPosition, result.hand.localPosition);
        result.restHumerusRotation = result.humerus.localRotation;
        result.restScapulaRotation = result.scapula.localRotation;
        result.restRadiusRotation = result.radius.localRotation;
        if (result.upperLength < .01f || result.lowerLength < .01f) throw new InvalidOperationException("Degenerate measured arm segments.");
        float restFlexion, restOutside, restPole;
        MotionMeasureElbow(result, MotionReadFrame(result), out restFlexion, out restOutside, out restPole);
        result.restOutsideHingeDegrees = restOutside;
        return result;
    }

    private static Transform MotionTransform(SerializedProperty property, string name)
    {
        Transform t = (Transform)property.FindPropertyRelative(name).objectReferenceValue;
        if (t == null) throw new InvalidOperationException("Missing output reference: " + name);
        return t;
    }

    private static MotionOutputFrame MotionReadFrame(MotionLimbMeasurement limb)
    {
        Vector3 longitudinal = (limb.middle.localPosition - limb.hand.localPosition).normalized;
        Vector3 rawRadial = limb.index.localPosition - limb.little.localPosition;
        // Remove knuckle stagger along the finger direction to measure transverse
        // thumb-side direction; retain all three axes of the full hand frame.
        Vector3 radial = (rawRadial - longitudinal * Vector3.Dot(rawRadial, longitudinal)).normalized;
        return new MotionOutputFrame
        {
            shoulder = limb.humerus.localPosition, elbow = limb.ulna.localPosition, wrist = limb.hand.localPosition,
            handRotation = limb.hand.localRotation, longitudinal = longitudinal, radial = radial,
            palm = PalmNormal(limb.left, radial, longitudinal)
        };
    }

    private static void MotionMeasureElbow(MotionLimbMeasurement limb, MotionOutputFrame measured, out float signedFlexion, out float outsideHinge, out float poleDot)
    {
        Vector3 upper = (measured.elbow - measured.shoulder).normalized;
        Vector3 lower = (measured.wrist - measured.elbow).normalized;
        Quaternion observedHumerusDelta = limb.humerus.localRotation * Quaternion.Inverse(limb.restHumerusRotation);
        Vector3 pole = observedHumerusDelta * limb.restBend;
        // Only the longitudinal component is removed to define the measured
        // hinge pole. The discarded component is not used as a flexion axis.
        pole = (pole - upper * Vector3.Dot(upper, pole)).normalized;
        Vector3 lateral = Vector3.Cross(upper, pole).normalized;
        signedFlexion = Mathf.Atan2(Vector3.Dot(lower, pole), Vector3.Dot(lower, upper)) * Mathf.Rad2Deg;
        // Clamp only floating-point overshoot of asin's mathematical domain.
        outsideHinge = Mathf.Asin(Mathf.Clamp(Vector3.Dot(lower, lateral), -1f, 1f)) * Mathf.Rad2Deg;
        Vector3 bend = lower - upper * Vector3.Dot(lower, upper);
        poleDot = bend.sqrMagnitude < .00000001f ? 1f : Vector3.Dot(bend.normalized, pole);
    }

    private static void MotionCheckFinite(Transform[] transforms, MotionVerificationReport report)
    {
        foreach (Transform t in transforms)
        {
            Vector3 p = t.localPosition, s = t.localScale; Quaternion q = t.localRotation;
            if (MotionFinite(p.x) && MotionFinite(p.y) && MotionFinite(p.z) && MotionFinite(s.x) && MotionFinite(s.y) && MotionFinite(s.z) && MotionFinite(q.x) && MotionFinite(q.y) && MotionFinite(q.z) && MotionFinite(q.w)) continue;
            report.nonFiniteValues++;
            throw new InvalidOperationException("Non-finite output transform: " + t.name);
        }
    }

    private static void MotionEvaluate(AnatomicalPatientRig rig, AnatomicalPatientRig.Pose pose)
    {
        if (!rig.Evaluate(pose)) throw new InvalidOperationException("Runtime anatomical solver rejected a motion sample.");
    }

    private static void MotionCheckRepeat(AnatomicalPatientRig rig, Transform[] outputs, AnatomicalMotionSequence.Motion motion, bool left, MotionVerificationReport report)
    {
        AnatomicalPatientRig.Pose target = AnatomicalMotionSequence.Sample(rig.RestPose, motion, AnatomicalMotionSequence.Duration(motion) * .375f, left);
        MotionEvaluate(rig, target);
        var before = new MotionTransformState[outputs.Length];
        for (int i = 0; i < outputs.Length; i++) before[i] = new MotionTransformState { position = outputs[i].localPosition, rotation = outputs[i].localRotation };
        foreach (AnatomicalMotionSequence.Motion other in new[] { AnatomicalMotionSequence.Motion.CompositeNeck, AnatomicalMotionSequence.Motion.ShoulderRotation, AnatomicalMotionSequence.Motion.CoupledWrist })
            MotionEvaluate(rig, AnatomicalMotionSequence.Sample(rig.RestPose, other, AnatomicalMotionSequence.Duration(other) * .625f, !left));
        MotionEvaluate(rig, target);
        for (int i = 0; i < outputs.Length; i++)
        {
            report.maximumRepeatPositionErrorMetres = Mathf.Max(report.maximumRepeatPositionErrorMetres, Vector3.Distance(before[i].position, outputs[i].localPosition));
            report.maximumRepeatRotationErrorDegrees = Mathf.Max(report.maximumRepeatRotationErrorDegrees, Quaternion.Angle(before[i].rotation, outputs[i].localRotation));
        }
        report.driftChecks++;
    }

    private static void MotionVerifyWristCovariance(AnatomicalPatientRig rig, MotionLimbMeasurement[] limbs, Transform head, Transform[] cervical, StreamWriter csv, MotionVerificationReport report)
    {
        int sample = 0;
        foreach (MotionLimbMeasurement limb in limbs)
            foreach (float flexion in new[] { -40f, 0f, 40f })
                foreach (float deviation in new[] { -20f, 0f, 20f })
                {
                    AnatomicalPatientRig.Pose baseline = rig.RestPose;
                    AnatomicalPatientRig.ArmPose arm = limb.left ? baseline.leftArm : baseline.rightArm;
                    arm.shoulderElevation = 45f; arm.elevationPlane = 40f; arm.humeralRotation = limb.left ? 25f : -25f;
                    arm.elbowFlexion = 90f; arm.forearmRotation = 0f; arm.wristFlexion = flexion; arm.wristDeviation = deviation;
                    if (limb.left) baseline.leftArm = arm; else baseline.rightArm = arm;
                    MotionEvaluate(rig, baseline);
                    MotionOutputFrame zero = MotionReadFrame(limb);
                    Vector3 forearmAxis = (zero.wrist - zero.elbow).normalized;
                    var neutralPose = baseline; arm.wristFlexion = arm.wristDeviation = 0f;
                    if (limb.left) neutralPose.leftArm = arm; else neutralPose.rightArm = arm;
                    MotionEvaluate(rig, neutralPose);
                    MotionOutputFrame neutral = MotionReadFrame(limb);
                    if (flexion > 0f && deviation == 0f) report.minimumPositiveFlexionPalmarDot = Mathf.Min(report.minimumPositiveFlexionPalmarDot, Vector3.Dot(zero.longitudinal - neutral.longitudinal, neutral.palm));
                    if (deviation > 0f && flexion == 0f) report.minimumPositiveDeviationRadialDot = Mathf.Min(report.minimumPositiveDeviationRadialDot, Vector3.Dot(zero.longitudinal - neutral.longitudinal, neutral.radial));
                    for (int roll = -80; roll <= 80; roll += 20)
                    {
                        var posed = baseline;
                        arm = limb.left ? posed.leftArm : posed.rightArm; arm.forearmRotation = roll;
                        if (limb.left) posed.leftArm = arm; else posed.rightArm = arm;
                        MotionEvaluate(rig, posed);
                        MotionOutputFrame actual = MotionReadFrame(limb);
                        Quaternion rigidRoll = Quaternion.AngleAxis(roll, forearmAxis);
                        report.maximumWristCovarianceRotationDegrees = Mathf.Max(report.maximumWristCovarianceRotationDegrees, Quaternion.Angle(actual.handRotation, rigidRoll * zero.handRotation));
                        report.maximumWristCovariancePalmDegrees = Mathf.Max(report.maximumWristCovariancePalmDegrees, Vector3.Angle(actual.palm, rigidRoll * zero.palm));
                        report.maximumWristCovarianceRadialDegrees = Mathf.Max(report.maximumWristCovarianceRadialDegrees, Vector3.Angle(actual.radial, rigidRoll * zero.radial));
                        report.maximumWristCovarianceLongitudinalDegrees = Mathf.Max(report.maximumWristCovarianceLongitudinalDegrees, Vector3.Angle(actual.longitudinal, rigidRoll * zero.longitudinal));
                        float flex, outside, pole; MotionMeasureElbow(limb, actual, out flex, out outside, out pole);
                        MotionWriteCsv(csv, "wrist_covariance", "CoupledWrist", "RigidRollEquivariance", limb, sample++, 0f, arm, Vector3.zero, actual, flex, outside, pole, head, cervical);
                        report.covarianceFrames++;
                    }
                }
    }

    private static void MotionVerifyRib2(AnatomicalPatientRig rig, MotionLimbMeasurement[] limbs, Vector3 cranial, Transform[] outputs, Transform head, Transform[] cervical, StreamWriter csv, MotionVerificationReport report)
    {
        foreach (MotionLimbMeasurement limb in limbs)
        {
            MotionOutputFrame firstHeld = default;
            foreach (float seconds in new[] { 6f, 14f, 22f, 30f })
            {
                var pose = AnatomicalMotionSequence.Sample(rig.RestPose, AnatomicalMotionSequence.Motion.Rib2Study, seconds, limb.left);
                MotionEvaluate(rig, pose);
                MotionOutputFrame frame = MotionReadFrame(limb);
                Vector3 direction = (frame.wrist - frame.elbow).normalized;
                float elbow, outside, pole; MotionMeasureElbow(limb, frame, out elbow, out outside, out pole);
                AnatomicalPatientRig.ArmPose input = limb.left ? pose.leftArm : pose.rightArm;
                float cranialDot = Vector3.Dot(direction, cranial);
                report.rib2MinimumHeldForearmCranialDot = Mathf.Min(report.rib2MinimumHeldForearmCranialDot, cranialDot);
                report.rib2Held.Add(new MotionRib2HeldObservation { side = limb.left ? "L" : "R", seconds = seconds, requestedElbowDegrees = input.elbowFlexion, measuredElbowDegrees = elbow, measuredForearmCranialDot = cranialDot, shoulder = frame.shoulder, elbow = frame.elbow, wrist = frame.wrist, forearmDirection = direction, palm = frame.palm });
                if (seconds == 6f) firstHeld = frame;
                else
                {
                    report.rib2MaximumCyclePositionErrorMetres = Mathf.Max(report.rib2MaximumCyclePositionErrorMetres, Vector3.Distance(firstHeld.shoulder, frame.shoulder), Vector3.Distance(firstHeld.elbow, frame.elbow), Vector3.Distance(firstHeld.wrist, frame.wrist));
                    report.rib2MaximumCycleRotationErrorDegrees = Mathf.Max(report.rib2MaximumCycleRotationErrorDegrees, Quaternion.Angle(firstHeld.handRotation, frame.handRotation));
                }
                MotionWriteCsv(csv, "rib2_held", "Rib2Study", "HeldClosure", limb, (int)seconds, seconds, input, pose.neckDegrees, frame, elbow, outside, pole, head, cervical);
            }
            MotionEvaluate(rig, AnatomicalMotionSequence.Sample(rig.RestPose, AnatomicalMotionSequence.Motion.Rib2Study, 0f, limb.left));
            var initial = new MotionTransformState[outputs.Length];
            for (int i = 0; i < outputs.Length; i++) initial[i] = new MotionTransformState { position = outputs[i].localPosition, rotation = outputs[i].localRotation };
            MotionEvaluate(rig, AnatomicalMotionSequence.Sample(rig.RestPose, AnatomicalMotionSequence.Motion.Rib2Study, AnatomicalMotionSequence.Duration(AnatomicalMotionSequence.Motion.Rib2Study), limb.left));
            for (int i = 0; i < outputs.Length; i++)
            {
                report.rib2MaximumReturnPositionErrorMetres = Mathf.Max(report.rib2MaximumReturnPositionErrorMetres, Vector3.Distance(initial[i].position, outputs[i].localPosition));
                report.rib2MaximumReturnRotationErrorDegrees = Mathf.Max(report.rib2MaximumReturnRotationErrorDegrees, Quaternion.Angle(initial[i].rotation, outputs[i].localRotation));
            }
        }
    }

    private static void MotionAccumulateMirror(MotionMirrorObservation report, MotionOutputFrame left, MotionOutputFrame right, Vector3 sagittalNormal, Vector3 centre)
    {
        report.maximumShoulderErrorMetres = Mathf.Max(report.maximumShoulderErrorMetres, Vector3.Distance(left.shoulder, MotionReflectPoint(right.shoulder, sagittalNormal, centre)));
        report.maximumElbowErrorMetres = Mathf.Max(report.maximumElbowErrorMetres, Vector3.Distance(left.elbow, MotionReflectPoint(right.elbow, sagittalNormal, centre)));
        report.maximumWristErrorMetres = Mathf.Max(report.maximumWristErrorMetres, Vector3.Distance(left.wrist, MotionReflectPoint(right.wrist, sagittalNormal, centre)));
        report.maximumPalmErrorDegrees = Mathf.Max(report.maximumPalmErrorDegrees, Vector3.Angle(left.palm, Vector3.Reflect(right.palm, sagittalNormal)));
        report.maximumRadialErrorDegrees = Mathf.Max(report.maximumRadialErrorDegrees, Vector3.Angle(left.radial, Vector3.Reflect(right.radial, sagittalNormal)));
        report.maximumLongitudinalErrorDegrees = Mathf.Max(report.maximumLongitudinalErrorDegrees, Vector3.Angle(left.longitudinal, Vector3.Reflect(right.longitudinal, sagittalNormal)));
    }

    private static Vector3 MotionReflectPoint(Vector3 point, Vector3 normal, Vector3 centre) => centre + Vector3.Reflect(point - centre, normal);
    private static bool MotionFinite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
    private static void MotionAssert(MotionVerificationReport report, bool pass, string failure) { if (!pass) report.failures.Add(failure); }
    private static string MotionFileHash(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Cannot identify tested source.", path);
        using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
    }

    private static string MotionCsvHeader()
    {
        var b = new StringBuilder("kind,motion,phase,side,sample,seconds,input_elevation_deg,input_plane_deg,input_humeral_deg,input_elbow_deg,input_forearm_deg,input_wrist_flex_deg,input_wrist_dev_deg,input_shrug_deg,input_protraction_deg,input_neck_flex_deg,input_neck_side_deg,input_neck_rot_deg,measured_elbow_deg,measured_outside_hinge_deg,measured_pole_dot");
        foreach (string name in new[] { "shoulder", "elbow", "wrist", "clavicle", "scapula", "radius", "palm", "radial", "finger_long", "head" }) b.Append(',').Append(name).Append("_x,").Append(name).Append("_y,").Append(name).Append("_z");
        b.Append(",hand_qx,hand_qy,hand_qz,hand_qw,head_qx,head_qy,head_qz,head_qw");
        for (int i = 0; i < 7; i++) b.Append(",cervical_").Append(i).Append("_x,cervical_").Append(i).Append("_y,cervical_").Append(i).Append("_z,cervical_").Append(i).Append("_qx,cervical_").Append(i).Append("_qy,cervical_").Append(i).Append("_qz,cervical_").Append(i).Append("_qw");
        return b.ToString();
    }

    private static void MotionWriteCsv(StreamWriter csv, string kind, string motion, string phase, MotionLimbMeasurement limb, int sample, float seconds, AnatomicalPatientRig.ArmPose input, Vector3 neckInput, MotionOutputFrame frame, float elbow, float outside, float pole, Transform head, Transform[] cervical)
    {
        var b = new StringBuilder();
        b.Append(MotionQuote(kind)).Append(',').Append(MotionQuote(motion)).Append(',').Append(MotionQuote(phase)).Append(',').Append(limb.left ? 'L' : 'R').Append(',').Append(sample);
        foreach (float f in new[] { seconds, input.shoulderElevation, input.elevationPlane, input.humeralRotation, input.elbowFlexion, input.forearmRotation, input.wristFlexion, input.wristDeviation, input.shrug, input.protraction, neckInput.x, neckInput.y, neckInput.z, elbow, outside, pole }) MotionAppend(b, f);
        foreach (Vector3 v in new[] { frame.shoulder, frame.elbow, frame.wrist, limb.clavicle.localPosition, limb.scapula.localPosition, limb.radius.localPosition, frame.palm, frame.radial, frame.longitudinal, head.localPosition }) MotionAppend(b, v);
        MotionAppend(b, frame.handRotation); MotionAppend(b, head.localRotation);
        foreach (Transform segment in cervical) { MotionAppend(b, segment.localPosition); MotionAppend(b, segment.localRotation); }
        csv.WriteLine(b.ToString());
    }

    private static string MotionQuote(string text) => "\"" + text.Replace("\"", "\"\"") + "\"";
    private static void MotionAppend(StringBuilder b, float f) { b.Append(',').Append(f.ToString("R", CultureInfo.InvariantCulture)); }
    private static void MotionAppend(StringBuilder b, Vector3 v) { MotionAppend(b, v.x); MotionAppend(b, v.y); MotionAppend(b, v.z); }
    private static void MotionAppend(StringBuilder b, Quaternion q) { MotionAppend(b, q.x); MotionAppend(b, q.y); MotionAppend(b, q.z); MotionAppend(b, q.w); }
}
