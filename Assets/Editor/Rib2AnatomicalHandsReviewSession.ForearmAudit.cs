using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public sealed partial class Rib2AnatomicalHandsReviewSession
{
    private const float ForearmAuditWeldMetres = .00005f;
    private const float ForearmAuditPositionToleranceMetres = .00001f;
    private const float ForearmAuditRotationToleranceDegrees = .001f;
    private static readonly float[] ForearmAuditStations = { .1f, .25f, .5f, .75f, .9f };

    private sealed class ForearmAuditPose
    {
        public Vector3 shoulder, elbow, wrist, guidePosition, supportPosition;
        public Quaternion patientRotation, guideRotation, supportRotation;
        public float outward, wristFlexion, roll;
    }

    private sealed class ForearmAuditBody
    {
        public Vector3[] vertices;
        public int[] triangles;
        public int boneCount, bindPoseCount;
        public string rendererName, meshName;
        public Matrix4x4 localToWorld;
        public Vector3 rendererScale;
    }

    private sealed class ForearmAuditSection
    {
        public bool valid, closed;
        public string failure = "";
        public float fraction, polygonArea, hullArea, meanRadius, rmsRadius, minimumRadius, maximumRadius, centroidOffset;
        public int components, openComponents, loopVertices, segments, coplanarTriangles, ambiguousTriangles;
        public Vector3 origin, axis, basisU, basisV;
    }

    private sealed class ForearmAuditNode
    {
        public Vector2 point;
        public readonly List<int> neighbours = new List<int>(2);
    }

    [MenuItem("GuideChuna/시험/제2늑골/V03 전완 피부 체적 비교")]
    private static void AuditForearmSkinMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Forearm skin review requires edit mode.");
        string directory = Absolute(Folder + "/forearm_review_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));
        if (Directory.Exists(directory)) throw new IOException("Forearm audit directory already exists: " + directory);
        Directory.CreateDirectory(directory);
        Debug.Log("[Rib2ForearmAudit] Starting actual skin sections and 248-frame pose comparison: " + directory);
        using (var session = new Rib2AnatomicalHandsReviewSession()) session.AuditForearmSkin(directory);
    }

    private void AuditForearmSkin(string directory)
    {
        if (forearmSkin == null) throw new InvalidOperationException("The disposable V03 forearm skin helper is not connected.");
        SkinnedMeshRenderer body = null;
        foreach (var candidate in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (candidate.name == "CC_Base_Body")
            {
                if (body != null) throw new InvalidOperationException("Expected one actual CC_Base_Body renderer.");
                body = candidate;
            }
        if (body == null || body.sharedMesh == null) throw new InvalidOperationException("Actual patient body skin is missing.");

        var rows = new List<object>(FrameCount);
        float maxShoulder = 0f, maxElbow = 0f, maxWrist = 0f, maxGuidePosition = 0f, maxSupportPosition = 0f;
        float maxPatientRotation = 0f, maxGuideRotation = 0f, maxSupportRotation = 0f, maxOutward = 0f, maxFlexion = 0f;
        int invariantFailures = 0;
        for (int frame = 0; frame < FrameCount; frame++)
        {
            float time = frame / (float)Fps;
            forearmSkin.SetEnabled(false); Sample(time, false);
            ForearmAuditPose before = CaptureForearmAuditPose();
            forearmSkin.SetEnabled(true); Sample(time, false);
            ForearmAuditPose after = CaptureForearmAuditPose();
            float ds = Vector3.Distance(before.shoulder, after.shoulder), de = Vector3.Distance(before.elbow, after.elbow);
            float dw = Vector3.Distance(before.wrist, after.wrist), dg = Vector3.Distance(before.guidePosition, after.guidePosition);
            float dSupport = Vector3.Distance(before.supportPosition, after.supportPosition);
            float dq = ForearmAuditQuaternionAngle(before.patientRotation, after.patientRotation);
            float dgq = ForearmAuditQuaternionAngle(before.guideRotation, after.guideRotation);
            float dsq = ForearmAuditQuaternionAngle(before.supportRotation, after.supportRotation);
            float dOutward = Mathf.Abs(before.outward - after.outward), dFlex = Mathf.Abs(before.wristFlexion - after.wristFlexion);
            bool finite = ForearmAuditFinite(before) && ForearmAuditFinite(after);
            bool invariant = finite && Mathf.Max(ds, de, dw, dg, dSupport) <= ForearmAuditPositionToleranceMetres
                && Mathf.Max(dq, dgq, dsq) <= ForearmAuditRotationToleranceDegrees && dOutward <= .00001f && dFlex <= .00001f;
            if (!invariant) invariantFailures++;
            maxShoulder = Mathf.Max(maxShoulder, ds); maxElbow = Mathf.Max(maxElbow, de); maxWrist = Mathf.Max(maxWrist, dw);
            maxGuidePosition = Mathf.Max(maxGuidePosition, dg); maxSupportPosition = Mathf.Max(maxSupportPosition, dSupport);
            maxPatientRotation = Mathf.Max(maxPatientRotation, dq); maxGuideRotation = Mathf.Max(maxGuideRotation, dgq);
            maxSupportRotation = Mathf.Max(maxSupportRotation, dsq); maxOutward = Mathf.Max(maxOutward, dOutward); maxFlexion = Mathf.Max(maxFlexion, dFlex);
            rows.Add(CalibrationObject("frameIndex", frame, "sourceSeconds", SourceStart + time, "finite", finite, "invariant", invariant,
                "before", ForearmAuditPoseRecord(before), "after", ForearmAuditPoseRecord(after),
                "shoulderDifferenceMetres", ds, "elbowDifferenceMetres", de, "wristDifferenceMetres", dw,
                "guideRootPositionDifferenceMetres", dg, "supportRootPositionDifferenceMetres", dSupport,
                "patientHandRotationDifferenceDegrees", dq, "guideRootRotationDifferenceDegrees", dgq, "supportRootRotationDifferenceDegrees", dsq,
                "palmOutwardDotDifference", dOutward, "appliedWristFlexionDifferenceDegrees", dFlex));
        }

        var geometryRows = new List<object>();
        int invalidSections = 0;
        float minimumBeforeAreaRatio = float.PositiveInfinity, minimumAfterAreaRatio = float.PositiveInfinity;
        float maximumZeroRollSkinDifference = 0f;
        object continuousSectionAudit = null;
        var baked = new Mesh { name = "Private V03 forearm section audit", hideFlags = HideFlags.HideAndDontSave };
        try
        {
            foreach (int frame in new[] { 120, 135, 214 })
            {
                float time = frame / (float)Fps;
                forearmSkin.SetEnabled(false); Sample(time, false);
                var arm = currentPresentedArm;
                Vector3 elbow = lower.position, wrist = fingers.Wrist.position;
                ForearmAuditBody beforeBody = BakeForearmAuditBody(body, baked);
                ForearmAuditSection[] before = ForearmAuditSections(beforeBody, elbow, wrist);
                SaveRestArmViews(directory, "frame" + frame + "_before");

                forearmSkin.SetEnabled(true); Sample(time, false);
                ForearmAuditBody afterBody = BakeForearmAuditBody(body, baked);
                ForearmAuditSection[] after = ForearmAuditSections(afterBody, lower.position, fingers.Wrist.position);
                SaveRestArmViews(directory, "frame" + frame + "_after");

                // This is a size reference only. Pose invariance above always compares
                // the identical current roll, never this deliberately changed roll.
                forearmSkin.SetEnabled(false);
                var zero = arm; zero.forearmRotation = 0f;
                Evaluate(zero);
                ForearmAuditBody referenceBody = BakeForearmAuditBody(body, baked);
                ForearmAuditSection[] reference = ForearmAuditSections(referenceBody, lower.position, fingers.Wrist.position);
                BoneWeight[] referenceWeights = body.sharedMesh.boneWeights;
                Transform[] referenceBones = body.bones;
                forearmSkin.SetEnabled(true); Evaluate(zero);
                ForearmAuditBody zeroAfterBody = BakeForearmAuditBody(body, baked);
                if (referenceBody.vertices.Length != zeroAfterBody.vertices.Length) throw new InvalidOperationException("Zero-roll skin topology changed.");
                int worstZeroVertex = 0; float poseZeroDifference = 0f;
                for (int vertex = 0; vertex < referenceBody.vertices.Length; vertex++)
                {
                    float difference = Vector3.Distance(referenceBody.vertices[vertex], zeroAfterBody.vertices[vertex]);
                    if (difference > poseZeroDifference) { poseZeroDifference = difference; worstZeroVertex = vertex; }
                    maximumZeroRollSkinDifference = Mathf.Max(maximumZeroRollSkinDifference, difference);
                }
                BoneWeight worstWeight = referenceWeights[worstZeroVertex];
                var sections = new List<object>(ForearmAuditStations.Length);
                for (int station = 0; station < ForearmAuditStations.Length; station++)
                {
                    bool valid = reference[station].valid && before[station].valid && after[station].valid;
                    if (!valid) invalidSections++;
                    float beforeRatio = valid ? before[station].polygonArea / reference[station].polygonArea : 0f;
                    float afterRatio = valid ? after[station].polygonArea / reference[station].polygonArea : 0f;
                    if (valid) { minimumBeforeAreaRatio = Mathf.Min(minimumBeforeAreaRatio, beforeRatio); minimumAfterAreaRatio = Mathf.Min(minimumAfterAreaRatio, afterRatio); }
                    sections.Add(CalibrationObject("fraction", ForearmAuditStations[station], "comparable", valid,
                        "zeroRollReference", ForearmAuditSectionRecord(reference[station]), "before", ForearmAuditSectionRecord(before[station]),
                        "after", ForearmAuditSectionRecord(after[station]), "beforeAreaRatioToZeroRoll", valid ? (object)beforeRatio : null,
                        "afterAreaRatioToZeroRoll", valid ? (object)afterRatio : null));
                }
                float maximumVertexDisplacement = 0f; double squaredDisplacement = 0;
                bool sameTopology = beforeBody.vertices.Length == afterBody.vertices.Length && ForearmAuditSameTriangles(beforeBody.triangles, afterBody.triangles);
                if (sameTopology)
                    for (int i = 0; i < beforeBody.vertices.Length; i++)
                    {
                        float displacement = Vector3.Distance(beforeBody.vertices[i], afterBody.vertices[i]);
                        maximumVertexDisplacement = Mathf.Max(maximumVertexDisplacement, displacement); squaredDisplacement += displacement * displacement;
                    }
                geometryRows.Add(CalibrationObject("frameIndex", frame, "sourceSeconds", SourceStart + time, "actualPresentedRollDegrees", arm.forearmRotation,
                    "zeroRollWorstVertex", worstZeroVertex, "zeroRollWorstDifferenceMetres", poseZeroDifference,
                    "zeroRollWorstBefore", CalibrationArray(referenceBody.vertices[worstZeroVertex]), "zeroRollWorstAfter", CalibrationArray(zeroAfterBody.vertices[worstZeroVertex]),
                    "zeroRollWorstOriginalBones", new[] { referenceBones[worstWeight.boneIndex0].name, referenceBones[worstWeight.boneIndex1].name, referenceBones[worstWeight.boneIndex2].name, referenceBones[worstWeight.boneIndex3].name },
                    "zeroRollWorstOriginalWeights", new[] { worstWeight.weight0, worstWeight.weight1, worstWeight.weight2, worstWeight.weight3 },
                    "zeroRollReferencePreservesOtherArmControls", true, "sections", sections,
                    "zeroRollBody", ForearmAuditBodyRecord(referenceBody), "beforeBody", ForearmAuditBodyRecord(beforeBody), "afterBody", ForearmAuditBodyRecord(afterBody),
                    "sameBeforeAfterBodyTopology", sameTopology, "maximumActualBodyVertexDisplacementMetres", sameTopology ? (object)maximumVertexDisplacement : null,
                    "rmsActualBodyVertexDisplacementMetres", sameTopology ? (object)Math.Sqrt(squaredDisplacement / beforeBody.vertices.Length) : null,
                    "zeroRollPartialShaftVolumeApproximationCubicMetres", ForearmAuditPartialVolume(reference, Vector3.Distance(elbow, wrist)),
                    "beforePartialShaftVolumeApproximationCubicMetres", ForearmAuditPartialVolume(before, Vector3.Distance(elbow, wrist)),
                    "afterPartialShaftVolumeApproximationCubicMetres", ForearmAuditPartialVolume(after, Vector3.Distance(elbow, wrist))));
                forearmSkin.SetEnabled(true); Sample(time, false);
            }
            continuousSectionAudit = AuditEveryForearmMidsection(body, baked);
        }
        finally { UnityEngine.Object.DestroyImmediate(baked); forearmSkin.SetEnabled(true); }

        var summary = CalibrationObject("sampleCount", FrameCount, "invariantFailureCount", invariantFailures, "poseInvariant", invariantFailures == 0,
            "positionToleranceMetres", ForearmAuditPositionToleranceMetres, "rotationToleranceDegrees", ForearmAuditRotationToleranceDegrees,
            "palmOutwardDotTolerance", .00001f, "appliedWristFlexionToleranceDegrees", .00001f,
            "maximumShoulderDifferenceMetres", maxShoulder, "maximumElbowDifferenceMetres", maxElbow, "maximumWristDifferenceMetres", maxWrist,
            "maximumGuideRootPositionDifferenceMetres", maxGuidePosition, "maximumSupportRootPositionDifferenceMetres", maxSupportPosition,
            "maximumPatientHandRotationDifferenceDegrees", maxPatientRotation, "maximumGuideRootRotationDifferenceDegrees", maxGuideRotation,
            "maximumSupportRootRotationDifferenceDegrees", maxSupportRotation, "maximumPalmOutwardDotDifference", maxOutward,
            "maximumAppliedWristFlexionDifferenceDegrees", maxFlexion, "invalidSectionComparisonCount", invalidSections,
            "maximumZeroRollBodyVertexDifferenceMetres", maximumZeroRollSkinDifference,
            "zeroRollSkinPreserved", maximumZeroRollSkinDifference <= .00001f,
            "minimumBeforeAreaRatioToZeroRoll", float.IsPositiveInfinity(minimumBeforeAreaRatio) ? null : (object)minimumBeforeAreaRatio,
            "minimumAfterAreaRatioToZeroRoll", float.IsPositiveInfinity(minimumAfterAreaRatio) ? null : (object)minimumAfterAreaRatio);
        var report = CalibrationObject("schemaVersion", 1, "measurement", "Native Unity SkinnedMeshRenderer.BakeMesh and actual evaluated output transforms; not an offline FK estimate.",
            "scope", "Only a disposable V03 preview patient and new audit folder. Original training scenes, prefabs, source observations, patient_fit and hand_solution are not written. No Play toggle.",
            "helperType", forearmSkin.GetType().Name,
            "helper", CalibrationObject("changedVertexCount", forearmSkin.ChangedVertexCount, "ownedMeshCount", forearmSkin.OwnedMeshCount,
                "twistBoneCount", forearmSkin.TwistBoneCount, "maximumWeightSumError", forearmSkin.MaximumWeightSumError,
                "effectiveInfluenceBudget", forearmSkin.EffectiveInfluenceBudget, "omittedSourceInactiveInfluences", forearmSkin.OmittedInactiveInfluenceCount,
                "maximumEffectiveNonForearmRatioError", forearmSkin.MaximumEffectiveNonForearmWeightError,
                "singleBoneFallbackVertexCount", forearmSkin.SingleBoneFallbackVertexCount, "enabledAtReport", forearmSkin.Enabled),
            "fps", Fps, "summary", summary, "poseFrames", rows, "representativeGeometry", geometryRows,
            "everyFrameMidsection", continuousSectionAudit,
            "sectionMethod", "Intersect actual full CC_Base_Body triangles with planes normal to the current elbow-to-wrist axis. Weld endpoints at 0.05mm. Choose the nearest connected shaft component within 120mm of the axis; require one closed degree-two loop. Polygon shoelace area is the measured section; convex hull is separately labelled an outer-envelope approximation. Radii use 128 equal-arclength samples about the shaft axis.",
            "volumeMethod", "Trapezoidal integration of five measured polygon areas from shaft fraction 0.1 to 0.9. This is a sparse partial-shaft volume approximation, not the closed full forearm mesh volume.",
            "renderMethod", "Before/after use the same Session.Render via SaveRestArmViews: source camera and the two existing review views, 960x540. Reference roll0 is numerical only.",
            "limitations", "A closed section and restored area do not alone prove anatomical tissue behaviour. No patient-specific scan, soft-tissue stress measurement, Quest performance test or human Play verification is performed. Open/ambiguous sections fail geometry comparison; they are not replaced with hull area.");
        var json = new StringBuilder(); CalibrationWrite(json, report); json.Append('\n');
        File.WriteAllText(Path.Combine(directory, "audit.json"), json.ToString(), new UTF8Encoding(false));
        Debug.Log("[Rib2ForearmAudit] 248-frame pose invariant=" + (invariantFailures == 0) + "; invalid section comparisons=" + invalidSections
            + "; minimum area/reference before=" + minimumBeforeAreaRatio.ToString("F4") + ", after=" + minimumAfterAreaRatio.ToString("F4") + "; " + directory);
    }

    private object AuditEveryForearmMidsection(SkinnedMeshRenderer body, Mesh baked)
    {
        var frames = new List<object>(FrameCount);
        int invalid = 0;
        float minimum = float.PositiveInfinity;
        for (int frame = 0; frame < FrameCount; frame++)
        {
            Sample(frame / (float)Fps, false);
            var arm = currentPresentedArm;
            Vector3 elbow = lower.position, wrist = fingers.Wrist.position;
            Vector3 axis = (wrist - elbow).normalized, origin = (elbow + wrist) * .5f;
            var actual = ForearmAuditIntersect(BakeForearmAuditBody(body, baked), origin, axis, .5f);
            var zero = arm; zero.forearmRotation = 0f; Evaluate(zero);
            var reference = ForearmAuditIntersect(BakeForearmAuditBody(body, baked), origin, axis, .5f);
            bool valid = actual.valid && reference.valid && reference.polygonArea > 1e-10f;
            if (!valid) invalid++;
            float ratio = valid ? actual.polygonArea / reference.polygonArea : 0f;
            if (valid) minimum = Mathf.Min(minimum, ratio);
            frames.Add(CalibrationObject("frameIndex", frame, "rollDegrees", arm.forearmRotation,
                "valid", valid, "areaRatioToSamePoseZeroRoll", valid ? (object)ratio : null));
        }
        Sample(seconds, false);
        return CalibrationObject("sampleCount", FrameCount, "invalidCount", invalid,
            "minimumAreaRatioToSamePoseZeroRoll", float.IsPositiveInfinity(minimum) ? null : (object)minimum,
            "passesConservativeAreaFloor", invalid == 0 && minimum >= .90f,
            "method", "Actual closed full-body triangle cross-section at50% shaft for every30Hz sample; same shoulder/elbow/wrist and wrist bend, comparison changes forearm roll only. 90% is an engineering regression floor, not a biological threshold.",
            "frames", frames);
    }

    private ForearmAuditPose CaptureForearmAuditPose()
    {
        return new ForearmAuditPose { shoulder = upper.position, elbow = lower.position, wrist = fingers.Wrist.position,
            patientRotation = fingers.Wrist.rotation, guidePosition = moving.Root.position, guideRotation = moving.Root.rotation,
            supportPosition = support.Root.position, supportRotation = support.Root.rotation,
            outward = Vector3.Dot(fingers.PalmarWorld, (fingers.Wrist.position - supportBodyFrame.position).normalized),
            wristFlexion = currentPresentedArm.wristFlexion, roll = currentPresentedArm.forearmRotation };
    }

    private static object ForearmAuditPoseRecord(ForearmAuditPose pose)
    {
        return CalibrationObject("shoulderWorld", CalibrationArray(pose.shoulder), "elbowWorld", CalibrationArray(pose.elbow), "wristWorld", CalibrationArray(pose.wrist),
            "patientHandRotation", CalibrationArray(pose.patientRotation), "guideRootPosition", CalibrationArray(pose.guidePosition), "guideRootRotation", CalibrationArray(pose.guideRotation),
            "supportRootPosition", CalibrationArray(pose.supportPosition), "supportRootRotation", CalibrationArray(pose.supportRotation),
            "palmOutwardDot", pose.outward, "appliedWristFlexionDegrees", pose.wristFlexion, "appliedForearmRotationDegrees", pose.roll);
    }

    private static bool ForearmAuditFinite(ForearmAuditPose pose)
    {
        foreach (Vector3 point in new[] { pose.shoulder, pose.elbow, pose.wrist, pose.guidePosition, pose.supportPosition })
            if (!ForearmAuditFinite(point.x) || !ForearmAuditFinite(point.y) || !ForearmAuditFinite(point.z)) return false;
        foreach (Quaternion q in new[] { pose.patientRotation, pose.guideRotation, pose.supportRotation })
            if (!ForearmAuditFinite(q.x) || !ForearmAuditFinite(q.y) || !ForearmAuditFinite(q.z) || !ForearmAuditFinite(q.w)) return false;
        return ForearmAuditFinite(pose.outward) && ForearmAuditFinite(pose.wristFlexion) && ForearmAuditFinite(pose.roll);
    }

    private static bool ForearmAuditFinite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }

    private static float ForearmAuditQuaternionAngle(Quaternion a, Quaternion b)
    {
        double dot = (double)a.x * b.x + (double)a.y * b.y + (double)a.z * b.z + (double)a.w * b.w;
        double na = (double)a.x * a.x + (double)a.y * a.y + (double)a.z * a.z + (double)a.w * a.w;
        double nb = (double)b.x * b.x + (double)b.y * b.y + (double)b.z * b.z + (double)b.w * b.w;
        if (na <= 0 || nb <= 0) return float.PositiveInfinity;
        return (float)(2.0 * Math.Acos(Math.Min(1.0, Math.Abs(dot) / Math.Sqrt(na * nb))) * 180.0 / Math.PI);
    }

    private static ForearmAuditBody BakeForearmAuditBody(SkinnedMeshRenderer renderer, Mesh temporary)
    {
        // BakeMesh(false) returns renderer-local vertices without applying its
        // Transform scale. Apply localToWorld exactly once, including that scale.
        renderer.BakeMesh(temporary, false);
        Vector3[] vertices = temporary.vertices;
        Matrix4x4 matrix = renderer.localToWorldMatrix;
        for (int i = 0; i < vertices.Length; i++) vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
        return new ForearmAuditBody { vertices = vertices, triangles = temporary.triangles, boneCount = renderer.bones.Length,
            bindPoseCount = renderer.sharedMesh.bindposes.Length, rendererName = renderer.name, meshName = renderer.sharedMesh.name,
            localToWorld = matrix, rendererScale = renderer.transform.lossyScale };
    }

    private static object ForearmAuditBodyRecord(ForearmAuditBody body)
    {
        var matrix = new float[16];
        for (int row = 0; row < 4; row++) for (int column = 0; column < 4; column++) matrix[row * 4 + column] = body.localToWorld[row, column];
        return CalibrationObject("renderer", body.rendererName, "mesh", body.meshName, "vertexCount", body.vertices.Length,
            "triangleCount", body.triangles.Length / 3, "boneCount", body.boneCount, "bindPoseCount", body.bindPoseCount,
            "rendererLossyScale", CalibrationArray(body.rendererScale), "rendererLocalToWorldRowMajor", matrix, "bakeUseScale", false);
    }

    private static bool ForearmAuditSameTriangles(int[] a, int[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private static ForearmAuditSection[] ForearmAuditSections(ForearmAuditBody body, Vector3 elbow, Vector3 wrist)
    {
        var result = new ForearmAuditSection[ForearmAuditStations.Length];
        Vector3 axis = (wrist - elbow).normalized;
        for (int i = 0; i < result.Length; i++)
            result[i] = ForearmAuditIntersect(body, Vector3.Lerp(elbow, wrist, ForearmAuditStations[i]), axis, ForearmAuditStations[i]);
        return result;
    }

    private static ForearmAuditSection ForearmAuditIntersect(ForearmAuditBody body, Vector3 origin, Vector3 axis, float fraction)
    {
        var section = new ForearmAuditSection { fraction = fraction, origin = origin, axis = axis };
        if (axis.sqrMagnitude < .5f) { section.failure = "Degenerate elbow-to-wrist axis."; return section; }
        Vector3 basis = Mathf.Abs(axis.y) < .8f ? Vector3.up : Vector3.right;
        section.basisU = Vector3.Cross(axis, basis).normalized;
        section.basisV = Vector3.Cross(axis, section.basisU).normalized;
        var nodes = new List<ForearmAuditNode>();
        var cells = new Dictionary<Vector2Int, List<int>>();
        var edges = new HashSet<ulong>();
        var hits = new Vector3[3];
        for (int index = 0; index < body.triangles.Length; index += 3)
        {
            Vector3 a = body.vertices[body.triangles[index]], b = body.vertices[body.triangles[index + 1]], c = body.vertices[body.triangles[index + 2]];
            float da = Vector3.Dot(a - origin, axis), db = Vector3.Dot(b - origin, axis), dc = Vector3.Dot(c - origin, axis);
            const float planeEpsilon = .0000001f;
            if ((da > planeEpsilon && db > planeEpsilon && dc > planeEpsilon) || (da < -planeEpsilon && db < -planeEpsilon && dc < -planeEpsilon)) continue;
            if (Mathf.Abs(da) <= planeEpsilon && Mathf.Abs(db) <= planeEpsilon && Mathf.Abs(dc) <= planeEpsilon) { section.coplanarTriangles++; continue; }
            int count = 0;
            ForearmAuditEdgeHit(a, b, da, db, hits, ref count);
            ForearmAuditEdgeHit(b, c, db, dc, hits, ref count);
            ForearmAuditEdgeHit(c, a, dc, da, hits, ref count);
            if (count < 2) continue;
            if (count != 2) { section.ambiguousTriangles++; continue; }
            Vector3 wa = hits[0] - origin, wb = hits[1] - origin;
            Vector2 pa = new Vector2(Vector3.Dot(wa, section.basisU), Vector3.Dot(wa, section.basisV));
            Vector2 pb = new Vector2(Vector3.Dot(wb, section.basisU), Vector3.Dot(wb, section.basisV));
            int ia = ForearmAuditWeld(pa, nodes, cells), ib = ForearmAuditWeld(pb, nodes, cells);
            if (ia == ib) continue;
            int low = Math.Min(ia, ib), high = Math.Max(ia, ib);
            ulong key = ((ulong)(uint)low << 32) | (uint)high;
            if (!edges.Add(key)) continue;
            nodes[ia].neighbours.Add(ib); nodes[ib].neighbours.Add(ia); section.segments++;
        }
        var seen = new bool[nodes.Count];
        List<int> selected = null; float nearest = float.PositiveInfinity;
        for (int seed = 0; seed < nodes.Count; seed++)
        {
            if (seen[seed]) continue;
            var component = new List<int>(); var stack = new Stack<int>(); stack.Push(seed); seen[seed] = true;
            Vector2 center = Vector2.zero; bool closed = true;
            while (stack.Count > 0)
            {
                int current = stack.Pop(); component.Add(current); center += nodes[current].point;
                if (nodes[current].neighbours.Count != 2) closed = false;
                foreach (int next in nodes[current].neighbours) if (!seen[next]) { seen[next] = true; stack.Push(next); }
            }
            section.components++; if (!closed) section.openComponents++;
            center /= component.Count;
            if (center.magnitude < nearest) { nearest = center.magnitude; selected = component; section.closed = closed; }
        }
        if (selected == null || nearest > .12f) { section.failure = "No shaft component with centroid within 120mm of the axis."; return section; }
        if (!section.closed || selected.Count < 3) { section.failure = "The nearest shaft component is open, branched, or has fewer than three nodes."; return section; }
        var polygon = new List<Vector2>(selected.Count);
        int start = selected[0], previous = -1, cursor = start;
        for (int i = 0; i < selected.Count; i++)
        {
            polygon.Add(nodes[cursor].point);
            int next = nodes[cursor].neighbours[0] == previous ? nodes[cursor].neighbours[1] : nodes[cursor].neighbours[0];
            previous = cursor; cursor = next;
            if (cursor == start) break;
        }
        if (cursor != start || polygon.Count != selected.Count) { section.failure = "Loop traversal did not visit and close every selected node."; return section; }
        section.loopVertices = polygon.Count;
        section.polygonArea = ForearmAuditArea(polygon);
        section.hullArea = ForearmAuditArea(ForearmAuditHull(polygon));
        if (section.polygonArea <= .00000001f) { section.failure = "Closed section has negligible area."; return section; }
        if (ForearmAuditSelfIntersects(polygon)) { section.failure = "Selected section self-intersects; signed polygon area is not a valid physical section."; return section; }
        section.centroidOffset = ForearmAuditCentroid(polygon).magnitude;
        ForearmAuditRadii(polygon, section);
        section.valid = true;
        return section;
    }

    private static void ForearmAuditEdgeHit(Vector3 a, Vector3 b, float da, float db, Vector3[] hits, ref int count)
    {
        const float epsilon = .0000001f;
        if (Mathf.Abs(da) <= epsilon) ForearmAuditAddHit(a, hits, ref count);
        if ((da > epsilon && db < -epsilon) || (da < -epsilon && db > epsilon))
            ForearmAuditAddHit(Vector3.LerpUnclamped(a, b, da / (da - db)), hits, ref count);
    }

    private static void ForearmAuditAddHit(Vector3 point, Vector3[] hits, ref int count)
    {
        for (int i = 0; i < count; i++) if ((hits[i] - point).sqrMagnitude < .000000000001f) return;
        if (count < hits.Length) hits[count++] = point;
    }

    private static int ForearmAuditWeld(Vector2 point, List<ForearmAuditNode> nodes, Dictionary<Vector2Int, List<int>> cells)
    {
        var cell = new Vector2Int(Mathf.FloorToInt(point.x / ForearmAuditWeldMetres), Mathf.FloorToInt(point.y / ForearmAuditWeldMetres));
        for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++)
            if (cells.TryGetValue(cell + new Vector2Int(x, y), out List<int> candidates))
                foreach (int candidate in candidates) if ((nodes[candidate].point - point).sqrMagnitude <= ForearmAuditWeldMetres * ForearmAuditWeldMetres) return candidate;
        int index = nodes.Count; nodes.Add(new ForearmAuditNode { point = point });
        if (!cells.TryGetValue(cell, out List<int> bucket)) { bucket = new List<int>(); cells.Add(cell, bucket); }
        bucket.Add(index); return index;
    }

    private static float ForearmAuditArea(List<Vector2> polygon)
    {
        double twice = 0;
        for (int i = 0; i < polygon.Count; i++) { Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Count]; twice += (double)a.x * b.y - (double)a.y * b.x; }
        return (float)(Math.Abs(twice) * .5);
    }

    private static Vector2 ForearmAuditCentroid(List<Vector2> polygon)
    {
        double twice = 0, x = 0, y = 0;
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Count]; double cross = (double)a.x * b.y - (double)a.y * b.x;
            twice += cross; x += (a.x + b.x) * cross; y += (a.y + b.y) * cross;
        }
        return Math.Abs(twice) > 1e-14 ? new Vector2((float)(x / (3 * twice)), (float)(y / (3 * twice))) : Vector2.zero;
    }

    private static float ForearmAuditCross(Vector2 a, Vector2 b, Vector2 c) { return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x); }

    private static List<Vector2> ForearmAuditHull(List<Vector2> polygon)
    {
        var points = new List<Vector2>(polygon);
        points.Sort((a, b) => { int x = a.x.CompareTo(b.x); return x != 0 ? x : a.y.CompareTo(b.y); });
        var hull = new List<Vector2>();
        foreach (Vector2 point in points)
        {
            while (hull.Count >= 2 && ForearmAuditCross(hull[hull.Count - 2], hull[hull.Count - 1], point) <= 0) hull.RemoveAt(hull.Count - 1);
            hull.Add(point);
        }
        int lowerCount = hull.Count;
        for (int i = points.Count - 2; i >= 0; i--)
        {
            Vector2 point = points[i];
            while (hull.Count > lowerCount && ForearmAuditCross(hull[hull.Count - 2], hull[hull.Count - 1], point) <= 0) hull.RemoveAt(hull.Count - 1);
            hull.Add(point);
        }
        if (hull.Count > 1) hull.RemoveAt(hull.Count - 1);
        return hull;
    }

    private static bool ForearmAuditSelfIntersects(List<Vector2> polygon)
    {
        for (int i = 0; i < polygon.Count; i++)
            for (int j = i + 2; j < polygon.Count; j++)
            {
                if (i == 0 && j == polygon.Count - 1) continue;
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Count], c = polygon[j], d = polygon[(j + 1) % polygon.Count];
                float abC = ForearmAuditCross(a, b, c), abD = ForearmAuditCross(a, b, d), cdA = ForearmAuditCross(c, d, a), cdB = ForearmAuditCross(c, d, b);
                if (abC * abD < -1e-16f && cdA * cdB < -1e-16f) return true;
            }
        return false;
    }

    private static void ForearmAuditRadii(List<Vector2> polygon, ForearmAuditSection section)
    {
        var cumulative = new float[polygon.Count + 1];
        for (int i = 0; i < polygon.Count; i++) cumulative[i + 1] = cumulative[i] + Vector2.Distance(polygon[i], polygon[(i + 1) % polygon.Count]);
        const int samples = 128; double sum = 0, squared = 0; float minimum = float.PositiveInfinity, maximum = 0f; int edge = 0;
        for (int i = 0; i < samples; i++)
        {
            float distance = cumulative[polygon.Count] * i / samples;
            while (edge + 1 < polygon.Count && cumulative[edge + 1] < distance) edge++;
            float t = Mathf.InverseLerp(cumulative[edge], cumulative[edge + 1], distance);
            float radius = Vector2.Lerp(polygon[edge], polygon[(edge + 1) % polygon.Count], t).magnitude;
            sum += radius; squared += radius * radius; minimum = Mathf.Min(minimum, radius); maximum = Mathf.Max(maximum, radius);
        }
        section.meanRadius = (float)(sum / samples); section.rmsRadius = (float)Math.Sqrt(squared / samples);
        section.minimumRadius = minimum; section.maximumRadius = maximum;
    }

    private static object ForearmAuditPartialVolume(ForearmAuditSection[] sections, float length)
    {
        double volume = 0;
        for (int i = 1; i < sections.Length; i++)
        {
            if (!sections[i - 1].valid || !sections[i].valid) return null;
            volume += .5 * (sections[i - 1].polygonArea + sections[i].polygonArea) * (sections[i].fraction - sections[i - 1].fraction) * length;
        }
        return volume;
    }

    private static object ForearmAuditSectionRecord(ForearmAuditSection section)
    {
        return CalibrationObject("valid", section.valid, "closed", section.closed, "failure", section.failure,
            "planeOriginWorld", CalibrationArray(section.origin), "shaftAxisWorld", CalibrationArray(section.axis),
            "planeBasisUWorld", CalibrationArray(section.basisU), "planeBasisVWorld", CalibrationArray(section.basisV),
            "componentCount", section.components, "openComponentCount", section.openComponents, "intersectionSegmentCount", section.segments,
            "coplanarTriangleCount", section.coplanarTriangles, "ambiguousTriangleCount", section.ambiguousTriangles, "selectedLoopVertexCount", section.loopVertices,
            "polygonAreaSquareMetres", section.valid ? (object)section.polygonArea : null,
            "convexHullEnvelopeAreaApproximationSquareMetres", section.loopVertices > 2 ? (object)section.hullArea : null,
            "meanShaftRadiusMetres", section.valid ? (object)section.meanRadius : null, "rmsShaftRadiusMetres", section.valid ? (object)section.rmsRadius : null,
            "minimumSampledShaftRadiusMetres", section.valid ? (object)section.minimumRadius : null,
            "maximumSampledShaftRadiusMetres", section.valid ? (object)section.maximumRadius : null,
            "sectionCentroidDistanceFromShaftMetres", section.valid ? (object)section.centroidOffset : null);
    }
}
