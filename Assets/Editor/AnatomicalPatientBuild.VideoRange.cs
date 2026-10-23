using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class AnatomicalPatientBuild
{
    private const string VideoRangeMenu = "GuideChuna/시험/해부학 환자/영상 분절 범위 맞춤 계측";
    private const string VideoRangePathMenu = "GuideChuna/시험/해부학 환자/영상 참고 경로 투영 검증";
    private const float VideoRangePixelsPerMetre = 650f;
    private const float VideoRangePixelTolerance = 2f;

    [Serializable] private sealed class VideoRangeObservation
    {
        public string name;
        public float sourceSeconds;
        public Vector2 shoulder, elbow, wrist;
        public int upperDepthSign, lowerDepthSign;
    }

    [Serializable] private sealed class VideoRangeFit
    {
        public string side, observation;
        public int cycle;
        public float sourceSeconds, samplerSeconds, upperLengthMetres, lowerLengthMetres;
        public int upperDepthSign, lowerDepthSign;
        public float upperDepthMetres, lowerDepthMetres;
        public AnatomicalPatientRig.ArmPose authoringPose;
        public Vector3 targetUpperCanonical, targetLowerCanonical;
        public Vector3 actualUpperCanonical, actualLowerCanonical;
        public Vector3 actualShoulderRigSpace, actualElbowRigSpace, actualWristRigSpace;
        public Vector2 sourceShoulder, sourceElbow, sourceWrist;
        public Vector2 projectedShoulder, projectedElbow, projectedWrist;
        public float elbowProjectionErrorPixels, wristProjectionErrorPixels;
        public float upperDirectionErrorDegrees, lowerDirectionErrorDegrees;
        public float measuredOutputElbowDegrees;
        public float sourceProjectedElbowAngleDegrees, actualProjectedElbowAngleDegrees;
        public Vector2 sourceWristFromShoulderInChainLengths, actualWristFromShoulderInChainLengths;
    }

    [Serializable] private sealed class VideoRangeExcursion
    {
        public string side;
        public int cycle, keySamples;
        public float fullArmChainPixels;
        public Vector2 sourceElbowRangePixels, actualElbowRangePixels, sourceWristRangePixels, actualWristRangePixels;
        public Vector2 sourceElbowRangeInChainLengths, actualElbowRangeInChainLengths, sourceWristRangeInChainLengths, actualWristRangeInChainLengths;
        public float maximumSourceElbowExcursionPixels, maximumActualElbowExcursionPixels;
        public float maximumSourceWristExcursionPixels, maximumActualWristExcursionPixels;
        public float maximumSourceElbowExcursionInChainLengths, maximumActualElbowExcursionInChainLengths;
        public float maximumSourceWristExcursionInChainLengths, maximumActualWristExcursionInChainLengths;
        public float minimumInputElevationDegrees, maximumInputElevationDegrees;
        public float minimumInputPlaneDegrees, maximumInputPlaneDegrees;
        public float minimumInputHumeralDegrees, maximumInputHumeralDegrees;
        public float minimumActualElbowDegrees, maximumActualElbowDegrees;
    }

    [Serializable] private sealed class VideoRangeReport
    {
        public bool success;
        public string utc, folder, prefab, prefabDependencyHash, rigSha256, implementationSha256, sequenceSha256;
        public bool sharedSamplerProjectionGate;
        public string runKind;
        public float samplerDurationSeconds;
        public string sourceVideo = "D:/전문가 영상 촬영본/3인칭 시점 카메라/제2늑골 교정기법.MOV";
        public Vector2 sourceImagePixels = new Vector2(960f, 540f);
        public float landmarkUncertaintyPixels = 15f;
        public float assumedPixelsPerMetre = VideoRangePixelsPerMetre;
        public Vector3 assumedImageRightCanonical, assumedImageDownCanonical, assumedDepthCanonical;
        public string canonicalCoordinates = "R = outward from the studied shoulder, U = cranial, F = anterior. The same canonical observations are mirrored onto each measured c9 limb.";
        public string cameraAssumption = "Assumed orthographic camera from a bed-axis approximation; 650 pixels/metre is an authoring assumption. These are not calibrated camera extrinsics or intrinsics.";
        public string depthAssumption = "Each segment receives a selected positive/negative camera-depth branch. Wide: -/-, Lowered: +/+, Folded: +/-. Branches are view-informed authoring assumptions, not measured depth.";
        public string method = "Manual 2D landmarks plus saved c9 segment lengths constrain one plausible 3D pose branch. Derive joint inputs using the measured native hinge; evaluate the existing runtime solver; project the actual output bones back into the assumed camera.";
        public string shoulderAlignment = "Every sample is translated so the actual shoulder projects onto the observed shoulder. Shoulder projection error is therefore zero by construction and is not a validation result. The actual shoulder and limb positions are retained in rig space.";
        public string gate = "Elbow and wrist reprojection error <=2 pixels is an implementation consistency tolerance under the assumed camera, much smaller than the estimated 15-pixel landmark uncertainty. It is not a threshold for clinical validity or unique 3D recovery.";
        public string limitations = "One camera and assumed scale/depth do not identify unique 3D joint angles. Clothing, landmark ambiguity, perspective, camera orientation and occlusion remain uncertain. This fit does not establish shoulder articulation, scapular motion, wrist orientation, contact forces, torso/bed collision, human Play, Quest or clinical fidelity.";
        public float maximumElbowProjectionErrorPixels, maximumWristProjectionErrorPixels;
        public List<VideoRangeObservation> observations = new List<VideoRangeObservation>();
        public List<VideoRangeFit> fits = new List<VideoRangeFit>();
        public List<VideoRangeExcursion> excursions = new List<VideoRangeExcursion>();
        public List<string> failures = new List<string>();
        public string exception;
    }

    private sealed class VideoRangeLimb
    {
        public bool left;
        public Transform humerus, ulna, hand;
        public Vector3 restUpper, restLower, restBend;
        public float upperLength, lowerLength;
        public AnatomicalPatientRig.ArmPose restPose;
    }

    [MenuItem(VideoRangeMenu)]
    public static void FitVideoSegmentRange() => RunVideoRange(false);

    [MenuItem(VideoRangePathMenu)]
    public static void VerifyVideoReferencePath() => RunVideoRange(true);

    private static void RunVideoRange(bool sharedSampler)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play before isolated video-range fitting.");
        string folder = "Captures/" + (sharedSampler ? "VideoRangePath_" : "VideoRangeFit_") + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        Directory.CreateDirectory(folder);
        var report = new VideoRangeReport
        {
            utc = DateTimeOffset.UtcNow.ToString("o"), folder = Path.GetFullPath(folder),
            sharedSamplerProjectionGate = sharedSampler,
            runKind = sharedSampler ? "Independent shared-sampler path projection check; no joint inversion in the evaluated path." : "Joint-input calibration under an assumed camera and selected depth branch.",
            samplerDurationSeconds = AnatomicalMotionSequence.Duration(AnatomicalMotionSequence.Motion.Rib2Study),
            assumedImageRightCanonical = new Vector3(Mathf.Sqrt(1f / 3f), Mathf.Sqrt(2f / 3f), 0f),
            assumedImageDownCanonical = new Vector3(Mathf.Sqrt(1f / 3f), -Mathf.Sqrt(1f / 6f), -Mathf.Sqrt(.5f)),
            assumedDepthCanonical = new Vector3(Mathf.Sqrt(1f / 3f), -Mathf.Sqrt(1f / 6f), Mathf.Sqrt(.5f))
        };
        report.observations.Add(new VideoRangeObservation { name = "Wide", sourceSeconds = 60.25f, shoulder = new Vector2(446f, 351f), elbow = new Vector2(591f, 373f), wrist = new Vector2(691f, 320f), upperDepthSign = -1, lowerDepthSign = -1 });
        report.observations.Add(new VideoRangeObservation { name = "Lowered", sourceSeconds = 62.25f, shoulder = new Vector2(443f, 350f), elbow = new Vector2(491f, 488f), wrist = new Vector2(642f, 515f), upperDepthSign = 1, lowerDepthSign = 1 });
        report.observations.Add(new VideoRangeObservation { name = "Folded", sourceSeconds = 63.25f, shoulder = new Vector2(443f, 350f), elbow = new Vector2(415f, 464f), wrist = new Vector2(534f, 384f), upperDepthSign = 1, lowerDepthSign = -1 });

        Scene scene = default;
        GameObject copy = null;
        try
        {
            string receiptPath = OutputFolder + "/LatestBuild.json";
            if (!File.Exists(receiptPath)) throw new FileNotFoundException("No durable generated patient receipt.", receiptPath);
            BuildReceipt receipt = JsonUtility.FromJson<BuildReceipt>(File.ReadAllText(receiptPath));
            if (receipt == null || string.IsNullOrEmpty(receipt.prefab) || !receipt.prefab.StartsWith(OutputFolder + "/", StringComparison.Ordinal))
                throw new InvalidOperationException("The durable receipt does not identify a generated patient.");
            report.prefab = receipt.prefab;
            report.prefabDependencyHash = AssetDatabase.GetAssetDependencyHash(receipt.prefab).ToString();
            report.rigSha256 = MotionFileHash("Assets/Scripts/ClaudeScripts/Patient/AnatomicalPatientRig.cs");
            report.implementationSha256 = MotionFileHash("Assets/Editor/AnatomicalPatientBuild.VideoRange.cs");
            report.sequenceSha256 = MotionFileHash("Assets/Scripts/ClaudeScripts/Patient/AnatomicalMotionSequence.cs");
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(receipt.prefab);
            if (asset == null) throw new InvalidOperationException("Saved generated prefab is missing: " + receipt.prefab);
            scene = EditorSceneManager.NewPreviewScene();
            copy = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
            var rig = copy.GetComponent<AnatomicalPatientRig>();
            if (rig == null) throw new InvalidOperationException("Saved prefab has no anatomical patient rig.");
            var animator = copy.GetComponentInChildren<Animator>(true);
            if (animator != null) animator.enabled = false;
            var lab = copy.GetComponent<AnatomicalPatientLab>();
            if (lab != null) lab.enabled = false;
            rig.enabled = false;
            rig.SetAutomaticEvaluation(false);
            rig.SetInputSource(AnatomicalPatientRig.InputSource.Manual);
            VideoRangeEvaluate(rig, rig.RestPose);
            Transform space = rig.RigSpace;
            if (space == null) throw new InvalidOperationException("Generated rig has no shared coordinate space.");
            var serialized = new SerializedObject(rig);
            SerializedProperty basis = serialized.FindProperty("anatomicalBasis");
            if (basis == null) throw new InvalidOperationException("Generated rig has no saved anatomical basis.");
            Vector3 right = basis.FindPropertyRelative("right").vector3Value.normalized;
            Vector3 cranial = basis.FindPropertyRelative("up").vector3Value.normalized;
            Vector3 anterior = basis.FindPropertyRelative("forward").vector3Value.normalized;
            VideoRangeCheckCamera(report);
            var limbs = new[] { VideoRangeReadLimb(serialized, rig, true), VideoRangeReadLimb(serialized, rig, false) };
            Debug.Log("[AnatomicalVideoRangeFit] Start: saved=" + report.prefab + "; mode=" + report.runKind + "; assumed orthographic camera/scale/depth; report=" + report.folder);
            foreach (VideoRangeLimb limb in limbs)
            {
                Vector3 outward = right * (limb.left ? -1f : 1f);
                int samples = sharedSampler ? 12 : report.observations.Count;
                for (int sample = 0; sample < samples; sample++)
                {
                    int cycle = sharedSampler ? sample / 4 + 1 : 0;
                    int key = sharedSampler ? sample % 4 : sample;
                    // Reference order in each cycle: Folded, Wide, Lowered,
                    // Folded. This checks actual shared sampling, not inversion.
                    int observationIndex = sharedSampler ? (key == 1 ? 0 : key == 2 ? 1 : 2) : sample;
                    VideoRangeObservation observation = report.observations[observationIndex];
                    float seconds = sharedSampler ? 6f + (cycle - 1) * 8f + (key == 1 ? 3.2f : key == 2 ? 6.4f : key == 3 ? 8f : 0f) : -1f;
                    float upperDepth, lowerDepth;
                    Vector3 targetUpper = VideoRangeLift(observation.elbow - observation.shoulder, limb.upperLength, observation.upperDepthSign, report, out upperDepth);
                    Vector3 targetLower = VideoRangeLift(observation.wrist - observation.elbow, limb.lowerLength, observation.lowerDepthSign, report, out lowerDepth);
                    Vector3 upper = VideoRangeFromCanonical(targetUpper, outward, cranial, anterior).normalized;
                    Vector3 lower = VideoRangeFromCanonical(targetLower, outward, cranial, anterior).normalized;
                    AnatomicalPatientRig.Pose pose;
                    AnatomicalPatientRig.ArmPose arm;
                    if (sharedSampler)
                    {
                        pose = AnatomicalMotionSequence.Sample(rig.RestPose, AnatomicalMotionSequence.Motion.Rib2Study, seconds, limb.left);
                        arm = limb.left ? pose.leftArm : pose.rightArm;
                    }
                    else
                    {
                        arm = VideoRangeAuthorPose(limb, upper, lower, outward, cranial, anterior, observation.name);
                        pose = rig.RestPose;
                        if (limb.left) pose.leftArm = arm;
                        else pose.rightArm = arm;
                    }
                    VideoRangeEvaluate(rig, pose);
                    Vector3 shoulder = space.InverseTransformPoint(limb.humerus.position);
                    Vector3 elbow = space.InverseTransformPoint(limb.ulna.position);
                    Vector3 wrist = space.InverseTransformPoint(limb.hand.position);
                    Vector3 actualUpper = VideoRangeToCanonical(elbow - shoulder, outward, cranial, anterior);
                    Vector3 actualLower = VideoRangeToCanonical(wrist - elbow, outward, cranial, anterior);
                    Vector2 projectedElbow = observation.shoulder + VideoRangeProject(actualUpper, report);
                    Vector2 projectedWrist = observation.shoulder + VideoRangeProject(actualUpper + actualLower, report);
                    var fit = new VideoRangeFit
                    {
                        side = limb.left ? "L" : "R", observation = observation.name, cycle = cycle, sourceSeconds = observation.sourceSeconds, samplerSeconds = seconds,
                        upperLengthMetres = limb.upperLength, lowerLengthMetres = limb.lowerLength,
                        upperDepthSign = observation.upperDepthSign, lowerDepthSign = observation.lowerDepthSign,
                        upperDepthMetres = upperDepth, lowerDepthMetres = lowerDepth, authoringPose = arm,
                        targetUpperCanonical = targetUpper, targetLowerCanonical = targetLower,
                        actualUpperCanonical = actualUpper, actualLowerCanonical = actualLower,
                        actualShoulderRigSpace = shoulder, actualElbowRigSpace = elbow, actualWristRigSpace = wrist,
                        sourceShoulder = observation.shoulder, sourceElbow = observation.elbow, sourceWrist = observation.wrist,
                        projectedShoulder = observation.shoulder, projectedElbow = projectedElbow, projectedWrist = projectedWrist,
                        elbowProjectionErrorPixels = Vector2.Distance(projectedElbow, observation.elbow),
                        wristProjectionErrorPixels = Vector2.Distance(projectedWrist, observation.wrist),
                        upperDirectionErrorDegrees = Vector3.Angle(targetUpper, actualUpper),
                        lowerDirectionErrorDegrees = Vector3.Angle(targetLower, actualLower),
                        measuredOutputElbowDegrees = Vector3.Angle(actualUpper, actualLower),
                        sourceProjectedElbowAngleDegrees = Vector2.Angle(observation.elbow - observation.shoulder, observation.wrist - observation.elbow),
                        actualProjectedElbowAngleDegrees = Vector2.Angle(projectedElbow - observation.shoulder, projectedWrist - projectedElbow),
                        sourceWristFromShoulderInChainLengths = (observation.wrist - observation.shoulder) / ((limb.upperLength + limb.lowerLength) * report.assumedPixelsPerMetre),
                        actualWristFromShoulderInChainLengths = (projectedWrist - observation.shoulder) / ((limb.upperLength + limb.lowerLength) * report.assumedPixelsPerMetre)
                    };
                    report.fits.Add(fit);
                    report.maximumElbowProjectionErrorPixels = Mathf.Max(report.maximumElbowProjectionErrorPixels, fit.elbowProjectionErrorPixels);
                    report.maximumWristProjectionErrorPixels = Mathf.Max(report.maximumWristProjectionErrorPixels, fit.wristProjectionErrorPixels);
                    if (fit.elbowProjectionErrorPixels > VideoRangePixelTolerance || fit.wristProjectionErrorPixels > VideoRangePixelTolerance)
                        report.failures.Add(fit.side + "/" + observation.name + " exceeds assumed-camera reprojection tolerance.");
                    Debug.Log(string.Format(CultureInfo.InvariantCulture,
                        "[AnatomicalVideoRangeFit] {0}/{1} source {2:F2}s: elevation={3:F5}, plane={4:F5}, humeral={5:F5}, elbow={6:F5}; actual output reprojection elbow={7:F4}px/wrist={8:F4}px. Authoring branch, not recovered clinical 3D.",
                        fit.side, fit.observation, fit.sourceSeconds, arm.shoulderElevation, arm.elevationPlane, arm.humeralRotation, arm.elbowFlexion, fit.elbowProjectionErrorPixels, fit.wristProjectionErrorPixels));
                }
                for (int cycle = sharedSampler ? 1 : 0; cycle <= (sharedSampler ? 3 : 0); cycle++)
                    report.excursions.Add(VideoRangeMeasureExcursion(report.fits, limb.left ? "L" : "R", cycle, (limb.upperLength + limb.lowerLength) * report.assumedPixelsPerMetre));
            }
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
            if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(folder + "/fit.json", json, new UTF8Encoding(false));
            Directory.CreateDirectory("Temp/model-audit");
            File.WriteAllText("Temp/model-audit/" + (sharedSampler ? "video-range-path.json" : "video-range-fit.json"), json, new UTF8Encoding(false));
            string message = string.Format(CultureInfo.InvariantCulture,
                "[AnatomicalVideoRangeFit] {0}; fitted branches={1}; maximum actual output reprojection elbow={2:F4}px/wrist={3:F4}px; assumed camera, not unique 3D recovery; report={4}/fit.json",
                report.success ? "PROJECTION PASS" : "FAIL", report.fits.Count, report.maximumElbowProjectionErrorPixels, report.maximumWristProjectionErrorPixels, report.folder);
            if (report.success) Debug.Log(message);
            else Debug.LogError(message + "; " + string.Join(" | ", report.failures));
        }
    }

    [MenuItem(VideoRangeMenu, true)]
    [MenuItem(VideoRangePathMenu, true)]
    private static bool VideoRangeAvailable() => !EditorApplication.isPlayingOrWillChangePlaymode;

    private static AnatomicalPatientRig.ArmPose VideoRangeAuthorPose(VideoRangeLimb limb, Vector3 upper, Vector3 lower,
        Vector3 outward, Vector3 cranial, Vector3 anterior, string observation)
    {
        var arm = new AnatomicalPatientRig.ArmPose
        {
            shoulderElevation = Vector3.Angle(-cranial, upper),
            elevationPlane = Mathf.Atan2(Vector3.Dot(upper, outward), Vector3.Dot(upper, anterior)) * Mathf.Rad2Deg,
            elbowFlexion = Vector3.Angle(upper, lower)
        };
        Quaternion swing = Quaternion.FromToRotation(limb.restUpper, upper);
        Vector3 hinge = Vector3.Cross(limb.restUpper, limb.restBend).normalized;
        Vector3 baseLower = swing * (Quaternion.AngleAxis(arm.elbowFlexion - limb.restPose.elbowFlexion, hinge) * limb.restLower);
        // Removed: each vector's component along the upper arm. That component
        // is already constrained by elbow flexion; axial roll only aligns the
        // two transverse bend directions.
        Vector3 baseBend = Vector3.ProjectOnPlane(baseLower, upper);
        Vector3 targetBend = Vector3.ProjectOnPlane(lower, upper);
        if (baseBend.sqrMagnitude < .00000001f || targetBend.sqrMagnitude < .00000001f)
            throw new InvalidOperationException("Humeral roll is undefined for an effectively straight target elbow: " + observation);
        arm.humeralRotation = Vector3.SignedAngle(baseBend, targetBend, upper);
        return arm;
    }

    private static VideoRangeExcursion VideoRangeMeasureExcursion(List<VideoRangeFit> fits, string side, int cycle, float chainPixels)
    {
        var selected = new List<VideoRangeFit>();
        foreach (VideoRangeFit fit in fits) if (fit.side == side && fit.cycle == cycle) selected.Add(fit);
        if (selected.Count == 0) throw new InvalidOperationException("No samples to measure video-range excursion.");
        VideoRangeFit first = selected[0];
        var result = new VideoRangeExcursion
        {
            side = side, cycle = cycle, keySamples = selected.Count, fullArmChainPixels = chainPixels,
            minimumInputElevationDegrees = first.authoringPose.shoulderElevation, maximumInputElevationDegrees = first.authoringPose.shoulderElevation,
            minimumInputPlaneDegrees = first.authoringPose.elevationPlane, maximumInputPlaneDegrees = first.authoringPose.elevationPlane,
            minimumInputHumeralDegrees = first.authoringPose.humeralRotation, maximumInputHumeralDegrees = first.authoringPose.humeralRotation,
            minimumActualElbowDegrees = first.measuredOutputElbowDegrees, maximumActualElbowDegrees = first.measuredOutputElbowDegrees
        };
        Vector2 seMin = first.sourceElbow - first.sourceShoulder, seMax = seMin;
        Vector2 aeMin = first.projectedElbow - first.projectedShoulder, aeMax = aeMin;
        Vector2 swMin = first.sourceWrist - first.sourceShoulder, swMax = swMin;
        Vector2 awMin = first.projectedWrist - first.projectedShoulder, awMax = awMin;
        foreach (VideoRangeFit fit in selected)
        {
            Vector2 se = fit.sourceElbow - fit.sourceShoulder, ae = fit.projectedElbow - fit.projectedShoulder;
            Vector2 sw = fit.sourceWrist - fit.sourceShoulder, aw = fit.projectedWrist - fit.projectedShoulder;
            seMin = Vector2.Min(seMin, se); seMax = Vector2.Max(seMax, se);
            aeMin = Vector2.Min(aeMin, ae); aeMax = Vector2.Max(aeMax, ae);
            swMin = Vector2.Min(swMin, sw); swMax = Vector2.Max(swMax, sw);
            awMin = Vector2.Min(awMin, aw); awMax = Vector2.Max(awMax, aw);
            result.minimumInputElevationDegrees = Mathf.Min(result.minimumInputElevationDegrees, fit.authoringPose.shoulderElevation);
            result.maximumInputElevationDegrees = Mathf.Max(result.maximumInputElevationDegrees, fit.authoringPose.shoulderElevation);
            result.minimumInputPlaneDegrees = Mathf.Min(result.minimumInputPlaneDegrees, fit.authoringPose.elevationPlane);
            result.maximumInputPlaneDegrees = Mathf.Max(result.maximumInputPlaneDegrees, fit.authoringPose.elevationPlane);
            result.minimumInputHumeralDegrees = Mathf.Min(result.minimumInputHumeralDegrees, fit.authoringPose.humeralRotation);
            result.maximumInputHumeralDegrees = Mathf.Max(result.maximumInputHumeralDegrees, fit.authoringPose.humeralRotation);
            result.minimumActualElbowDegrees = Mathf.Min(result.minimumActualElbowDegrees, fit.measuredOutputElbowDegrees);
            result.maximumActualElbowDegrees = Mathf.Max(result.maximumActualElbowDegrees, fit.measuredOutputElbowDegrees);
            foreach (VideoRangeFit other in selected)
            {
                result.maximumSourceElbowExcursionPixels = Mathf.Max(result.maximumSourceElbowExcursionPixels, Vector2.Distance(se, other.sourceElbow - other.sourceShoulder));
                result.maximumActualElbowExcursionPixels = Mathf.Max(result.maximumActualElbowExcursionPixels, Vector2.Distance(ae, other.projectedElbow - other.projectedShoulder));
                result.maximumSourceWristExcursionPixels = Mathf.Max(result.maximumSourceWristExcursionPixels, Vector2.Distance(sw, other.sourceWrist - other.sourceShoulder));
                result.maximumActualWristExcursionPixels = Mathf.Max(result.maximumActualWristExcursionPixels, Vector2.Distance(aw, other.projectedWrist - other.projectedShoulder));
            }
        }
        result.sourceElbowRangePixels = seMax - seMin; result.actualElbowRangePixels = aeMax - aeMin;
        result.sourceWristRangePixels = swMax - swMin; result.actualWristRangePixels = awMax - awMin;
        result.sourceElbowRangeInChainLengths = result.sourceElbowRangePixels / chainPixels;
        result.actualElbowRangeInChainLengths = result.actualElbowRangePixels / chainPixels;
        result.sourceWristRangeInChainLengths = result.sourceWristRangePixels / chainPixels;
        result.actualWristRangeInChainLengths = result.actualWristRangePixels / chainPixels;
        result.maximumSourceElbowExcursionInChainLengths = result.maximumSourceElbowExcursionPixels / chainPixels;
        result.maximumActualElbowExcursionInChainLengths = result.maximumActualElbowExcursionPixels / chainPixels;
        result.maximumSourceWristExcursionInChainLengths = result.maximumSourceWristExcursionPixels / chainPixels;
        result.maximumActualWristExcursionInChainLengths = result.maximumActualWristExcursionPixels / chainPixels;
        return result;
    }

    private static VideoRangeLimb VideoRangeReadLimb(SerializedObject serialized, AnatomicalPatientRig rig, bool left)
    {
        SerializedProperty array = serialized.FindProperty("limbs");
        if (array == null) throw new InvalidOperationException("Generated rig has no saved limbs.");
        SerializedProperty limb = null;
        for (int i = 0; i < array.arraySize; i++)
        {
            SerializedProperty candidate = array.GetArrayElementAtIndex(i);
            if (candidate.FindPropertyRelative("left").boolValue != left) continue;
            if (limb != null) throw new InvalidOperationException("Generated rig has duplicate limb sides.");
            limb = candidate;
        }
        if (limb == null) throw new InvalidOperationException("Generated rig is missing a measured limb.");
        var result = new VideoRangeLimb
        {
            left = left,
            humerus = (Transform)limb.FindPropertyRelative("humerus").objectReferenceValue,
            ulna = (Transform)limb.FindPropertyRelative("ulna").objectReferenceValue,
            hand = (Transform)limb.FindPropertyRelative("hand").objectReferenceValue,
            restBend = limb.FindPropertyRelative("restBendDirection").vector3Value,
            restPose = left ? rig.RestPose.leftArm : rig.RestPose.rightArm
        };
        if (result.humerus == null || result.ulna == null || result.hand == null)
            throw new InvalidOperationException("Measured limb has missing output bones.");
        Vector3 shoulder = rig.RigSpace.InverseTransformPoint(result.humerus.position);
        Vector3 elbow = rig.RigSpace.InverseTransformPoint(result.ulna.position);
        Vector3 wrist = rig.RigSpace.InverseTransformPoint(result.hand.position);
        result.upperLength = Vector3.Distance(shoulder, elbow);
        result.lowerLength = Vector3.Distance(elbow, wrist);
        if (result.upperLength < .01f || result.lowerLength < .01f || result.restBend.sqrMagnitude < .5f)
            throw new InvalidOperationException("Measured limb length or native hinge pole is degenerate.");
        result.restUpper = (elbow - shoulder).normalized;
        result.restLower = (wrist - elbow).normalized;
        return result;
    }

    private static Vector3 VideoRangeLift(Vector2 pixels, float length, int depthSign, VideoRangeReport report, out float signedDepth)
    {
        if (depthSign != -1 && depthSign != 1) throw new InvalidOperationException("Depth branch must be explicitly +1 or -1.");
        Vector2 metres = pixels / report.assumedPixelsPerMetre;
        float depthSquared = length * length - metres.sqrMagnitude;
        // Never discard an impossible observed segment by clamping a negative
        // radicand: the assumed scale/camera must instead be revised explicitly.
        if (depthSquared < 0f)
            throw new InvalidOperationException("Observed segment is longer than the measured limb under the assumed pixel scale.");
        signedDepth = depthSign * Mathf.Sqrt(depthSquared);
        return report.assumedImageRightCanonical * metres.x + report.assumedImageDownCanonical * metres.y + report.assumedDepthCanonical * signedDepth;
    }

    private static Vector2 VideoRangeProject(Vector3 canonicalMetres, VideoRangeReport report)
    {
        // Camera depth is deliberately unobserved by an orthographic image.
        // Store the selected depth and both full 3D vectors in the report.
        return report.assumedPixelsPerMetre * new Vector2(Vector3.Dot(canonicalMetres, report.assumedImageRightCanonical), Vector3.Dot(canonicalMetres, report.assumedImageDownCanonical));
    }

    private static Vector3 VideoRangeFromCanonical(Vector3 value, Vector3 outward, Vector3 cranial, Vector3 anterior)
        => outward * value.x + cranial * value.y + anterior * value.z;

    private static Vector3 VideoRangeToCanonical(Vector3 value, Vector3 outward, Vector3 cranial, Vector3 anterior)
        => new Vector3(Vector3.Dot(value, outward), Vector3.Dot(value, cranial), Vector3.Dot(value, anterior));

    private static void VideoRangeEvaluate(AnatomicalPatientRig rig, AnatomicalPatientRig.Pose pose)
    {
        if (!rig.Evaluate(pose)) throw new InvalidOperationException("Runtime anatomical solver rejected a fitted pose.");
    }

    private static void VideoRangeCheckCamera(VideoRangeReport report)
    {
        Vector3 r = report.assumedImageRightCanonical, d = report.assumedImageDownCanonical, z = report.assumedDepthCanonical;
        if (Mathf.Abs(r.sqrMagnitude - 1f) > .00001f || Mathf.Abs(d.sqrMagnitude - 1f) > .00001f || Mathf.Abs(z.sqrMagnitude - 1f) > .00001f ||
            Mathf.Abs(Vector3.Dot(r, d)) > .00001f || Mathf.Abs(Vector3.Dot(r, z)) > .00001f || Mathf.Abs(Vector3.Dot(d, z)) > .00001f)
            throw new InvalidOperationException("Assumed camera basis is not orthonormal.");
    }
}
