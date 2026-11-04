using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public sealed partial class Rib2AnatomicalHandsReviewSession
{
    [MenuItem("GuideChuna/시험/제2늑골/V03 반대팔 복부 자세 실측")]
    private static void AuditRestArmMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Rest-arm review requires edit mode.");
        using (var session = new Rib2AnatomicalHandsReviewSession(true)) session.AuditRestArm();
    }

    private void AuditRestArm()
    {
        string directory = Absolute(Folder + "/rest_arm_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(directory);
        var arm = SourceArm(SourceStart, out Observation observation);
        sourceShoulder = V2(observation.patientShoulder);
        var data = new SerializedObject(rig);
        var limbs = data.FindProperty("limbs");
        Transform restUpper = null, restLower = null;
        for (int i = 0; i < limbs.arraySize; i++)
        {
            var limb = limbs.GetArrayElementAtIndex(i);
            if (limb.FindPropertyRelative("left").boolValue == fit.affectedLeft) continue;
            restUpper = limb.FindPropertyRelative("humerus").objectReferenceValue as Transform;
            restLower = limb.FindPropertyRelative("ulna").objectReferenceValue as Transform;
        }
        if (restUpper == null || restLower == null) throw new InvalidOperationException("Opposite arm outputs are missing.");
        using (var restFingers = new Rib2AnatomicalHandsContact.PatientFingers(rig, !fit.affectedLeft))
        {
            var pose = Pose(arm);
            var before = fit.affectedLeft ? pose.rightArm : pose.leftArm;
            Action<AnatomicalPatientRig.ArmPose> evaluateRest = value =>
            {
                if (fit.affectedLeft) pose.rightArm = value; else pose.leftArm = value;
                if (!EvaluateNativePose(pose)) throw new InvalidOperationException("Opposite-arm FK failed.");
                fingers.ApplyFromEvaluatedPose(.24f); restFingers.ApplyFromEvaluatedPose(.18f);
                shoulderWorld = upper.position;
            };
            var records = new List<object>();
            evaluateRest(before);
            records.Add(RestArmRecord("before", before, restUpper, restLower, restFingers));
            SaveRestArmViews(directory, "before");

            // Measure the actual unoccluded torso with this arm moved aside.
            evaluateRest(new AnatomicalPatientRig.ArmPose { shoulderElevation = 90f, elevationPlane = 90f });
            BakePatientQueries();
            Vector3 shoulder = restUpper.position;
            var animator = root.GetComponentInChildren<Animator>(true);
            Transform hipSource = animator.GetBoneTransform(HumanBodyBones.Hips), hipOutput = null;
            var bindings = data.FindProperty("bodyBindings");
            for (int i = 0; i < bindings.arraySize; i++)
            {
                var binding = bindings.GetArrayElementAtIndex(i);
                if (binding.FindPropertyRelative("source").objectReferenceValue == hipSource)
                    hipOutput = binding.FindPropertyRelative("output").objectReferenceValue as Transform;
            }
            if (hipOutput == null) throw new InvalidOperationException("Measured pelvis output missing.");
            float side = fit.affectedLeft ? 1f : -1f;
            Vector3 wristGoal = new Vector3(side * .08f, hipOutput.position.y + .19f, .2f);
            if (!RestTorsoFront(wristGoal, out float wristFront)) throw new InvalidOperationException("Abdomen goal misses torso.");
            wristGoal.z = wristFront + .035f;
            Vector3 elbowGoal = new Vector3(side * .29f, shoulder.y - .255f, .055f);
            float best = float.PositiveInfinity;
            AnatomicalPatientRig.ArmPose selected = default;
            foreach (float humeral in new[] { -120f, -60f, 0f, 60f, 120f })
            {
                var trial = new AnatomicalPatientRig.ArmPose { shoulderElevation = 20f, elevationPlane = 65f, humeralRotation = humeral, elbowFlexion = 75f, forearmRotation = 90f };
                float cost = RestArmCost(trial, evaluateRest, restUpper, restLower, restFingers, wristGoal, elbowGoal);
                foreach (float step in new[] { 30f, 15f, 7f, 3f, 1f, .3f })
                    for (int pass = 0; pass < 4; pass++)
                        for (int axis = 0; axis < 7; axis++)
                            foreach (float sign in new[] { -1f, 1f })
                            {
                                var candidate = trial;
                                RestArmStep(ref candidate, axis, step * sign);
                                float value = RestArmCost(candidate, evaluateRest, restUpper, restLower, restFingers, wristGoal, elbowGoal);
                                if (value < cost) { cost = value; trial = candidate; }
                            }
                if (cost < best) { best = cost; selected = trial; }
            }
            evaluateRest(selected);
            records.Add(RestArmRecord("abdomen_candidate", selected, restUpper, restLower, restFingers));
            SaveRestArmViews(directory, "abdomen_candidate");
            var audit = CalibrationObject("scope", "Disposable V03 opposite-arm authoring. Torso ray tests use actual baked skin with the opposite arm aside; no original scene/prefab writes.",
                "wristGoal", CalibrationArray(wristGoal), "elbowGoal", CalibrationArray(elbowGoal), "cost", best, "records", records);
            var json = new StringBuilder(); CalibrationWrite(json, audit); json.Append('\n');
            File.WriteAllText(Path.Combine(directory, "audit.json"), json.ToString(), new UTF8Encoding(false));
            Debug.Log("[Rib2RestArm] Actual FK abdomen candidate: " + JsonUtility.ToJson(selected) + "; wrist=" + restFingers.Wrist.position.ToString("F4") + "; palm=" + restFingers.PalmarWorld.ToString("F4") + "; " + directory);
        }
    }

    private float RestArmCost(AnatomicalPatientRig.ArmPose value, Action<AnatomicalPatientRig.ArmPose> evaluateRest, Transform restUpper, Transform restLower,
        Rib2AnatomicalHandsContact.PatientFingers restFingers, Vector3 wristGoal, Vector3 elbowGoal)
    {
        evaluateRest(value);
        float cost = 12f * (restFingers.Wrist.position - wristGoal).sqrMagnitude + 3f * (restLower.position - elbowGoal).sqrMagnitude;
        cost += .04f * (restFingers.PalmarWorld + Vector3.forward).sqrMagnitude;
        cost += .025f * (restFingers.DistalWorld + Vector3.up).sqrMagnitude;
        for (int i = 1; i <= 7; i++)
        {
            Vector3 point = Vector3.Lerp(restLower.position, restFingers.Wrist.position, i / 7f);
            if (RestTorsoFront(point, out float front))
            {
                float overlap = Mathf.Max(0f, front + .028f - point.z);
                cost += 40f * overlap * overlap;
            }
        }
        return cost;
    }

    private bool RestTorsoFront(Vector3 point, out float front)
    {
        bool found = false; front = float.NegativeInfinity;
        var ray = new Ray(new Vector3(point.x, point.y, 2f), Vector3.back);
        foreach (MeshCollider collider in chestQueries)
            if (collider.Raycast(ray, out RaycastHit hit, 4f) && hit.point.z > front) { front = hit.point.z; found = true; }
        return found;
    }

    private static void RestArmStep(ref AnatomicalPatientRig.ArmPose pose, int axis, float step)
    {
        // These bounds remove implausible resting poses, not observed motion.
        if (axis == 0) pose.shoulderElevation = Mathf.Clamp(pose.shoulderElevation + step, 0f, 55f);
        if (axis == 1) pose.elevationPlane = Mathf.Clamp(pose.elevationPlane + step, -30f, 110f);
        if (axis == 2) pose.humeralRotation = Mathf.Clamp(pose.humeralRotation + step, -180f, 180f);
        if (axis == 3) pose.elbowFlexion = Mathf.Clamp(pose.elbowFlexion + step, 35f, 135f);
        if (axis == 4) pose.forearmRotation = Mathf.Clamp(pose.forearmRotation + step, -180f, 180f);
        if (axis == 5) pose.wristFlexion = Mathf.Clamp(pose.wristFlexion + step, -25f, 25f);
        if (axis == 6) pose.wristDeviation = Mathf.Clamp(pose.wristDeviation + step, -15f, 15f);
    }

    private static object RestArmRecord(string name, AnatomicalPatientRig.ArmPose pose, Transform restUpper, Transform restLower, Rib2AnatomicalHandsContact.PatientFingers restFingers)
    {
        return CalibrationObject("name", name, "pose", CalibrationArm(pose), "shoulderWorld", CalibrationArray(restUpper.position),
            "elbowWorld", CalibrationArray(restLower.position), "wristWorld", CalibrationArray(restFingers.Wrist.position),
            "palmarWorld", CalibrationArray(restFingers.PalmarWorld), "distalWorld", CalibrationArray(restFingers.DistalWorld));
    }

    private void SaveRestArmViews(string directory, string label)
    {
        for (int view = 0; view < 3; view++)
        {
            Texture rendered = Render(960, 540, view, false);
            var temporary = RenderTexture.GetTemporary(960, 540, 0);
            RenderTexture previous = RenderTexture.active;
            var image = new Texture2D(960, 540, TextureFormat.RGB24, false);
            try
            {
                Graphics.Blit(rendered, temporary); RenderTexture.active = temporary;
                image.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); image.Apply(false);
                File.WriteAllBytes(Path.Combine(directory, label + "_view" + view + ".jpg"), image.EncodeToJPG(92));
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(temporary); UnityEngine.Object.DestroyImmediate(image); }
        }
    }
}
