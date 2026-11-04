using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>One-shot native geometry export for offline fitting; preview objects only.</summary>
public sealed partial class Rib2AnatomicalHandsReviewSession
{
    [MenuItem("GuideChuna/시험/제2늑골/V03 손바닥 축 실측")]
    private static void AuditPatientPalmNormalMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Patient palm normal audit requires edit mode.");
        string path = Absolute(Folder + "/palm_normal_audit.json");
        if (File.Exists(path)) throw new IOException("Palm audit does not overwrite an existing file: " + path);
        using (var session = new Rib2AnatomicalHandsReviewSession(true)) session.ExportPalmNormalAudit(path);
    }

    private void ExportPalmNormalAudit(string path)
    {
        var arm = SourceArm(SourceStart, out Observation observation);
        arm.forearmRotation = arm.wristFlexion = arm.wristDeviation = 0f;
        Evaluate(arm);
        Vector3 wrist = fingers.Wrist.position, palmar = fingers.PalmarWorld;
        Vector3 weightedNormal = Vector3.zero; float selectedArea = 0f;
        int selectedTriangles = 0, windingFlips = 0;
        var materialRecords = new List<object>(); var triangleRecords = new List<object>();
        var mesh = new Mesh { name = "Private C9 nail-normal audit", hideFlags = HideFlags.HideAndDontSave };
        try
        {
            foreach (var skin in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin.name.IndexOf("CC_Base_Body", StringComparison.OrdinalIgnoreCase) < 0) continue;
                Material[] materials = skin.sharedMaterials;
                var materialNames = new string[materials.Length];
                for (int i = 0; i < materials.Length; i++) materialNames[i] = materials[i] == null ? "<null>" : materials[i].name;
                materialRecords.Add(CalibrationObject("renderer", skin.name, "materials", materialNames, "subMeshCount", skin.sharedMesh.subMeshCount));
                skin.BakeMesh(mesh); Vector3[] vertices = mesh.vertices, normals = mesh.normals;
                Matrix4x4 matrix = skin.localToWorldMatrix, normalMatrix = matrix.inverse.transpose;
                for (int subMesh = 0; subMesh < mesh.subMeshCount && subMesh < materials.Length; subMesh++)
                {
                    if (materials[subMesh] == null || materials[subMesh].name.IndexOf("Nail", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    int[] triangles = mesh.GetTriangles(subMesh);
                    for (int i = 0; i + 2 < triangles.Length; i += 3)
                    {
                        int ia = triangles[i], ib = triangles[i + 1], ic = triangles[i + 2];
                        Vector3 a = matrix.MultiplyPoint3x4(vertices[ia]), b = matrix.MultiplyPoint3x4(vertices[ib]), c = matrix.MultiplyPoint3x4(vertices[ic]);
                        Vector3 center = (a + b + c) / 3f;
                        if ((center - wrist).sqrMagnitude > .22f * .22f) continue;
                        Vector3 twiceAreaNormal = Vector3.Cross(b - a, c - a);
                        float twiceArea = twiceAreaNormal.magnitude;
                        if (twiceArea < 1e-12f) continue;
                        // A mirrored renderer can reverse triangle winding.
                        // Orient geometric area normals by the baked mesh's own
                        // outward vertex normals, independently of the hand frame.
                        if (normals.Length == vertices.Length)
                        {
                            Vector3 outward = normalMatrix.MultiplyVector(normals[ia] + normals[ib] + normals[ic]).normalized;
                            if (Vector3.Dot(twiceAreaNormal, outward) < 0f) { twiceAreaNormal = -twiceAreaNormal; windingFlips++; }
                        }
                        weightedNormal += twiceAreaNormal; selectedArea += twiceArea * .5f; selectedTriangles++;
                        triangleRecords.Add(CalibrationObject("renderer", skin.name, "material", materials[subMesh].name, "subMesh", subMesh,
                            "centerWorld", CalibrationArray(center), "outwardNormalWorld", CalibrationArray(twiceAreaNormal / twiceArea),
                            "areaSquareMetres", twiceArea * .5f, "dotPatientPalmar", Vector3.Dot(twiceAreaNormal / twiceArea, palmar)));
                    }
                }
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(mesh); }
        bool measured = selectedTriangles > 0 && weightedNormal.sqrMagnitude > 1e-14f;
        Vector3 dorsal = measured ? weightedNormal.normalized : Vector3.zero;
        float dot = measured ? Vector3.Dot(dorsal, palmar) : 0f;
        string verdict = !measured ? "undetermined_no_nail_surface" : dot > .2f ? "patient_palmar_sign_points_toward_nails_REVERSED" : dot < -.2f ? "patient_palmar_sign_opposes_nails" : "undetermined_near_tangent_nail_average";
        var audit = CalibrationObject("purpose", "Independent C9 palmar-sign audit from actual Std_Nails material geometry; source assets and original patient inputs remain read-only.",
            "sourceSeconds", SourceStart, "armPose", CalibrationArm(arm), "patientFingerCurl", .24f,
            "bodyRenderers", materialRecords, "selectedNailTriangles", selectedTriangles, "selectedNailAreaSquareMetres", selectedArea,
            "windingFlipsToMatchBakedOutwardNormals", windingFlips, "wristWorld", CalibrationArray(wrist),
            "patientPalmarWorld", CalibrationArray(palmar), "patientDistalWorld", CalibrationArray(fingers.DistalWorld), "patientRadialWorld", CalibrationArray(fingers.RadialWorld),
            "areaWeightedNailDorsalWorld", CalibrationArray(dorsal), "nailNormalDotPatientPalmar", measured ? (object)dot : null,
            "verdict", verdict, "triangles", triangleRecords,
            "scope", "Nail material is an independent dorsal indicator. Triangle-centre radius0.22m selects affected-hand nails; no Cross(radial,distal) assumption is used to define dorsal. Averaging folded fingers may reduce magnitude; absent/tangent evidence remains undetermined.");
        var json = new StringBuilder(); CalibrationWrite(json, audit); json.Append('\n');
        File.WriteAllText(path, json.ToString(), new UTF8Encoding(false));
        Debug.Log("[Rib2PalmNormalAudit] " + verdict + "; nail triangles=" + selectedTriangles + ", nail normal dot declared patient palmar=" + (measured ? dot.ToString("R", CultureInfo.InvariantCulture) : "undetermined") + "; " + path);
    }

    [MenuItem("GuideChuna/시험/제2늑골/V03 손 기하 내보내기")]
    private static void ExportHandCalibrationMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Hand calibration export requires edit mode.");
        using (var session = new Rib2AnatomicalHandsReviewSession(true))
        {
            string path = session.ExportHandCalibration();
            Debug.Log("[Rib2HandCalibration] Native geometry exported: " + path);
        }
    }

    [MenuItem("GuideChuna/시험/제2늑골/V03 전체 손바닥 기하 내보내기")]
    private static void ExportFullPalmCalibrationMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Hand calibration requires edit mode.");
        using (var session = new Rib2AnatomicalHandsReviewSession(true, .5f))
            session.ExportHandCalibration("hand_calibration_v2.json");
    }

    public string ExportHandCalibration(string filename = "hand_calibration.json")
    {
        if (Path.GetFileName(filename) != filename) throw new ArgumentException("Calibration filename must remain inside V03.");
        string path = Absolute(Folder + "/" + filename);
        if (File.Exists(path)) throw new IOException("Calibration export does not overwrite an existing file: " + path);
        var frames = new List<object>(FrameCount);
        var checks = new List<object>(FrameCount * 2);
        float maximumFormulaError = 0f, maximumWristPositionError = 0f;
        float[][] testAngles = { new[] { -37f, 18f, -9f }, new[] { 52f, -23f, 14f } };
        for (int frame = 0; frame < FrameCount; frame++)
        {
            float time = SourceStart + frame / (float)Fps;
            var arm = SourceArm(time, out Observation observation);
            arm.forearmRotation = 0f; arm.wristFlexion = 0f; arm.wristDeviation = 0f;
            Evaluate(arm);
            Vector3 shoulder = upper.position, elbow = lower.position, wrist = fingers.Wrist.position;
            Quaternion neutral = fingers.Wrist.rotation;
            Vector3 lowerAxis = (wrist - elbow).normalized;
            var trial = arm; trial.wristFlexion = 1f; Evaluate(trial);
            Vector3 flexAxis = CalibrationAxis(Quaternion.Inverse(neutral) * fingers.Wrist.rotation);
            trial = arm; trial.wristDeviation = 1f; Evaluate(trial);
            Vector3 deviationAxis = CalibrationAxis(Quaternion.Inverse(neutral) * fingers.Wrist.rotation);
            frames.Add(CalibrationObject("frameIndex", frame, "sourceSeconds", time,
                "shoulderWorld", CalibrationArray(shoulder), "elbowWorld", CalibrationArray(elbow), "wristWorld", CalibrationArray(wrist),
                "handNeutralQuaternion", CalibrationArray(neutral), "forearmAxisWorld", CalibrationArray(lowerAxis),
                "flexAxisLocal", CalibrationArray(flexAxis), "deviationAxisLocal", CalibrationArray(deviationAxis),
                "shoulderPixel", observation.patientShoulder, "patientWristPixel", observation.patientWrist,
                "patientHandTipPixel", observation.patientHandTip, "movingWristPixel", observation.leftWrist, "movingPalmPixel", observation.leftPalm,
                "armPose", CalibrationArm(arm)));
            foreach (float[] angles in testAngles)
            {
                trial = arm; trial.forearmRotation = angles[0]; trial.wristFlexion = angles[1]; trial.wristDeviation = angles[2]; Evaluate(trial);
                Quaternion predicted = Quaternion.AngleAxis(angles[0], lowerAxis) * neutral * Quaternion.AngleAxis(angles[1], flexAxis) * Quaternion.AngleAxis(angles[2], deviationAxis);
                float error = Quaternion.Angle(predicted, fingers.Wrist.rotation);
                float positionError = Vector3.Distance(wrist, fingers.Wrist.position);
                maximumFormulaError = Mathf.Max(maximumFormulaError, error);
                maximumWristPositionError = Mathf.Max(maximumWristPositionError, positionError);
                checks.Add(CalibrationObject("frameIndex", frame, "angles", angles, "nativeQuaternion", CalibrationArray(fingers.Wrist.rotation),
                    "formulaQuaternion", CalibrationArray(predicted), "errorDegrees", error, "wristPositionErrorMetres", positionError));
            }
        }

        var referenceArm = SourceArm(SourceStart, out Observation referenceObservation);
        referenceArm.forearmRotation = referenceArm.wristFlexion = referenceArm.wristDeviation = 0f;
        Evaluate(referenceArm);
        Quaternion patientInverse = Quaternion.Inverse(fingers.Wrist.rotation);
        Vector3 referenceWrist = fingers.Wrist.position;
        var patientGeometry = fingers.ExportHandGeometry(.22f);
        var included = new HashSet<int>(patientGeometry.vertexIds);
        var contacts = new List<object>(); var chosenIds = new HashSet<int>();
        foreach (float along in new[] { .25f, .4f, .55f, .7f, .85f, 1f, 1.15f })
            foreach (float across in new[] { -.8f, -.4f, 0f, .4f, .8f })
            {
                var surface = fingers.SurfaceAt(along, across, 0f);
                if (!included.Contains(surface.vertexIndex) || !chosenIds.Add(surface.vertexIndex)) continue;
                contacts.Add(CalibrationObject("vertexId", surface.vertexIndex, "along", along, "across", across,
                    "localPoint", CalibrationArray(patientInverse * (surface.point - referenceWrist)),
                    "localNormal", CalibrationArray(patientInverse * surface.normal)));
            }
        Dictionary<string, object> patient = CalibrationGeometry(patientGeometry);
        patient.Add("wristLocal", new[] { 0f, 0f, 0f });
        patient.Add("wristLocalTip", CalibrationArray(patientInverse * (handTip.position + (handTip.position - handPrevious.position) * .55f - referenceWrist)));
        patient.Add("palmLocal", CalibrationArray(patientInverse * (fingers.PalmCenterWorld - referenceWrist)));
        patient.Add("palmarLocal", CalibrationArray(patientInverse * fingers.PalmarWorld));
        patient.Add("distalLocal", CalibrationArray(patientInverse * fingers.DistalWorld));
        patient.Add("radialLocal", CalibrationArray(patientInverse * fingers.RadialWorld));
        patient.Add("contactCandidates", contacts);
        patient.Add("selection", "Body skin with >=0.4 hand/finger output influence including the actual anatomical hand palette bone, within0.22m wrist; actual posed BakeMesh; triangle indices remapped, vertexIds retain global cached skin identity.");
        float maximumPinnedError = 0f;
        var pinnedChecks = new List<object>();
        for (int i = 0; i < patientGeometry.vertexIds.Length; i += Mathf.Max(1, patientGeometry.vertexIds.Length / 12))
        {
            var actual = fingers.PinnedSurface(patientGeometry.vertexIds[i]);
            Vector3 predicted = referenceWrist + fingers.Wrist.rotation * patientGeometry.vertices[i];
            float error = Vector3.Distance(actual.point, predicted); maximumPinnedError = Mathf.Max(maximumPinnedError, error);
            pinnedChecks.Add(CalibrationObject("vertexId", patientGeometry.vertexIds[i], "errorMetres", error));
        }

        var rigidityChecks = new List<object>(); float maximumNeutralRigidityError = 0f;
        foreach (int frame in new[] { 0, 120, 247 })
        {
            var arm = SourceArm(SourceStart + frame / (float)Fps, out Observation unused);
            arm.forearmRotation = arm.wristFlexion = arm.wristDeviation = 0f; Evaluate(arm);
            float maximum = 0f;
            for (int i = 0; i < patientGeometry.vertexIds.Length; i += Mathf.Max(1, patientGeometry.vertexIds.Length / 30))
            {
                Vector3 predicted = fingers.Wrist.position + fingers.Wrist.rotation * patientGeometry.vertices[i];
                maximum = Mathf.Max(maximum, Vector3.Distance(predicted, fingers.PinnedSurface(patientGeometry.vertexIds[i]).point));
            }
            maximumNeutralRigidityError = Mathf.Max(maximumNeutralRigidityError, maximum);
            rigidityChecks.Add(CalibrationObject("frameIndex", frame, "maximumErrorMetres", maximum));
        }

        moving.Root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var families = new List<object>(); float maximumGuideSamplingError = 0f;
        float[][] familyAngles = { new[] { 0f, 60f, 35f }, new[] { 10f, 70f, 40f }, new[] { 20f, 60f, 35f }, new[] { 30f, 80f, 40f } };
        for (int i = 0; i < familyAngles.Length; i++)
        {
            float[] family = familyAngles[i]; moving.SetGraspFamily(family[0], family[1], family[2]);
            Quaternion inverse = Quaternion.Inverse(moving.Root.rotation);
            var data = CalibrationGeometry(moving.ExportGeometry());
            data.Add("id", i); data.Add("mcpOpen", family[0]); data.Add("pipCurl", family[1]); data.Add("dipCurl", family[2]);
            data.Add("thumbOpposition", 30f); data.Add("thumbProximalFlexion", 15f); data.Add("thumbDistalFlexion", 15f);
            data.Add("wristLocal", CalibrationArray(inverse * (moving.WristWorld - moving.Root.position)));
            data.Add("palmLocal", CalibrationArray(inverse * (moving.Joint(0).position - moving.Root.position)));
            data.Add("palmSurfaceLocal", CalibrationArray(inverse * (moving.PalmSurfaceWorld - moving.Root.position)));
            data.Add("ulnarHeelSurfaceLocal", CalibrationArray(inverse * (moving.UlnarHeelSurfaceWorld - moving.Root.position)));
            data.Add("palmarLocal", CalibrationArray(inverse * moving.PalmNormalWorld));
            data.Add("distalLocal", CalibrationArray(inverse * moving.FingerDirectionWorld));
            data.Add("radialLocal", CalibrationArray(inverse * moving.RadialWorld));
            float error = moving.ValidateSkinSampling(); maximumGuideSamplingError = Mathf.Max(maximumGuideSamplingError, error);
            data.Add("skinSamplingErrorMetres", error); families.Add(data);
        }

        var export = CalibrationObject("schemaVersion", 1, "quaternionOrder", "xyzw", "sourceStartSeconds", SourceStart, "fps", Fps,
            "coordinateSpace", "Unity world metres; local coordinates use inverse WORLD rotation and position only, and retain geometric scale.",
            "nativeFormula", "Qworld=AngleAxis(roll,forearmAxisWorld)*Qneutral*AngleAxis(flex,flexAxisLocal)*AngleAxis(deviation,deviationAxisLocal). Degrees; intrinsic flexion then deviation.",
            "patientGeometryScope", "Reference frame0 neutral wrist/forearm; authored finger curl recorded separately. True closed full-body surface not claimed after wrist-region cut.",
            "patientFingerCurl", patientCurlOverride >= 0f ? patientCurlOverride : solvedPatientCurl,
            "guideGeometryScope", "Full actual SDK guide BakeMesh including wrist/cuff; native active skin quality/morphs, each independent MCP/PIP/DIP family.",
            "camera", CalibrationObject("right", CalibrationArray(cameraRight), "down", CalibrationArray(cameraDown), "forward", CalibrationArray(cameraForward), "pixelsPerMetre", scale),
            "patient", patient, "guideFamilies", families, "frames", frames, "nativeFormulaChecks", checks,
            "maximumNativeFormulaErrorDegrees", maximumFormulaError, "maximumNativeWristPositionErrorMetres", maximumWristPositionError,
            "pinnedVertexChecks", pinnedChecks, "maximumPinnedVertexErrorMetres", maximumPinnedError,
            "neutralHandRigidityChecks", rigidityChecks, "maximumNeutralHandRigidityErrorMetres", maximumNeutralRigidityError,
            "maximumGuideSkinSamplingErrorMetres", maximumGuideSamplingError,
            "sourceObservationsSha256", CalibrationHash(Absolute(Folder + "/source_observations.json")),
            "patientFitSha256", CalibrationHash(Absolute(Folder + "/patient_fit.json")),
            "contactHelperSha256", CalibrationHash(Absolute("Assets/Editor/Rib2AnatomicalHandsContact.cs")),
            "exporterSha256", CalibrationHash(Absolute("Assets/Editor/Rib2AnatomicalHandsReviewSession.Calibration.cs")),
            "nativeRigSha256", CalibrationHash(Absolute("Assets/Scripts/ClaudeScripts/Patient/AnatomicalPatientRig.cs")));
        var json = new StringBuilder(1000000); CalibrationWrite(json, export); json.Append('\n');
        File.WriteAllText(path, json.ToString(), new UTF8Encoding(false));
        Debug.Log("[Rib2HandCalibration] frames=" + frames.Count + ", patient vertices=" + patientGeometry.vertices.Length + ", contact candidates=" + contacts.Count +
            ", formula maxdeg=" + maximumFormulaError + ", pinned maxm=" + maximumPinnedError + ", neutral rigidity maxm=" + maximumNeutralRigidityError + ", guide Bake maxm=" + maximumGuideSamplingError);
        return path;
    }

    private static Vector3 CalibrationAxis(Quaternion rotation)
    {
        rotation.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) { angle = 360f - angle; axis = -axis; }
        if (angle < .5f || angle > 1.5f || axis.sqrMagnitude < .9f) throw new InvalidOperationException("Native one-degree wrist perturbation did not produce a valid local axis: " + angle);
        return axis.normalized;
    }

    private static Dictionary<string, object> CalibrationArm(AnatomicalPatientRig.ArmPose arm)
    { return CalibrationObject("shoulderElevation", arm.shoulderElevation, "elevationPlane", arm.elevationPlane, "humeralRotation", arm.humeralRotation, "elbowFlexion", arm.elbowFlexion,
        "forearmRotation", arm.forearmRotation, "wristFlexion", arm.wristFlexion, "wristDeviation", arm.wristDeviation, "shrug", arm.shrug, "protraction", arm.protraction); }

    private static float[] CalibrationArray(Vector3 value) { return new[] { value.x, value.y, value.z }; }
    private static float[] CalibrationArray(Quaternion value) { return new[] { value.x, value.y, value.z, value.w }; }
    private static Dictionary<string, object> CalibrationObject(params object[] pairs)
    {
        var result = new Dictionary<string, object>();
        for (int i = 0; i < pairs.Length; i += 2) result.Add((string)pairs[i], pairs[i + 1]);
        return result;
    }

    private static Dictionary<string, object> CalibrationGeometry(Rib2AnatomicalHandsContact.MeshGeometry geometry)
    {
        var vertices = new float[geometry.vertices.Length][]; var normals = new float[geometry.normals.Length][];
        for (int i = 0; i < vertices.Length; i++) { vertices[i] = CalibrationArray(geometry.vertices[i]); normals[i] = CalibrationArray(geometry.normals[i]); }
        var triangles = new int[geometry.triangles.Length / 3][];
        for (int i = 0; i < triangles.Length; i++) triangles[i] = new[] { geometry.triangles[i * 3], geometry.triangles[i * 3 + 1], geometry.triangles[i * 3 + 2] };
        return CalibrationObject("vertices", vertices, "normals", normals, "triangles", triangles, "vertexIds", geometry.vertexIds);
    }

    private static string CalibrationHash(string path)
    {
        using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
    }

    // Tiny writer keeps vectors as standard JSON numeric arrays without a new
    // package dependency or Unity JsonUtility's unsupported jagged-array layout.
    private static void CalibrationWrite(StringBuilder target, object value)
    {
        if (value == null) { target.Append("null"); return; }
        if (value is string text)
        {
            target.Append('"');
            foreach (char c in text)
            {
                if (c == '"' || c == '\\') target.Append('\\').Append(c);
                else if (c == '\n') target.Append("\\n"); else if (c == '\r') target.Append("\\r"); else if (c == '\t') target.Append("\\t");
                else if (c < ' ') target.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)); else target.Append(c);
            }
            target.Append('"'); return;
        }
        if (value is IDictionary<string, object> dictionary)
        {
            target.Append('{'); bool first = true;
            foreach (var pair in dictionary) { if (!first) target.Append(','); first = false; CalibrationWrite(target, pair.Key); target.Append(':'); CalibrationWrite(target, pair.Value); }
            target.Append('}'); return;
        }
        if (value is IEnumerable array)
        {
            target.Append('['); bool first = true;
            foreach (object item in array) { if (!first) target.Append(','); first = false; CalibrationWrite(target, item); }
            target.Append(']'); return;
        }
        if (value is float number)
        { if (float.IsNaN(number) || float.IsInfinity(number)) throw new InvalidDataException("Calibration contains a nonfinite float."); target.Append(number.ToString("R", CultureInfo.InvariantCulture)); return; }
        if (value is double real)
        { if (double.IsNaN(real) || double.IsInfinity(real)) throw new InvalidDataException("Calibration contains a nonfinite double."); target.Append(real.ToString("R", CultureInfo.InvariantCulture)); return; }
        if (value is bool flag) { target.Append(flag ? "true" : "false"); return; }
        if (value is IFormattable scalar) { target.Append(scalar.ToString(null, CultureInfo.InvariantCulture)); return; }
        throw new InvalidDataException("Unsupported calibration JSON value: " + value.GetType().Name);
    }
}
