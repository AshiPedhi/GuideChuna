using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public sealed partial class Rib2AnatomicalHandsReviewSession
{
    [MenuItem("GuideChuna/시험/제2늑골/V03 1인칭 비교 기하 내보내기")]
    private static void ExportFirstPersonGeometryMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("First-person comparison requires edit mode.");
        string path = Absolute(Folder + "/first_person_geometry_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".json");
        if (File.Exists(path)) throw new IOException("Comparison export never overwrites a file.");
        using (var session = new Rib2AnatomicalHandsReviewSession()) session.ExportFirstPersonGeometry(path);
        Debug.Log("[Rib2FirstPerson] Actual private-preview geometry exported: " + path + "; no regular-scene save.");
    }

    [MenuItem("GuideChuna/시험/제2늑골/V03 양 시점 팔기하 검증")]
    private static void ExportJointFirstPersonGeometryMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Native arm export requires edit mode.");
        string path = Absolute(Folder + "/joint_native_geometry_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".json");
        using (var session = new Rib2AnatomicalHandsReviewSession(true, .5f, DefaultSmoothingPasses, false, null, LatestFirstPersonDataFile("patient_fit_bodycam_v*.json")))
        {
            session.PrepareNeutralArmExport();
            session.ExportFirstPersonGeometry(path);
        }
        Debug.Log("[Rib2FirstPerson] New two-view arm hypothesis measured through actual native FK: " + path);
    }

    [MenuItem("GuideChuna/시험/제2늑골/V03 후보 실제 피부 내보내기")]
    private static void ExportCandidateSkinMenu()
    { ExportCandidateSkin(-1f); }

    [MenuItem("GuideChuna/시험/제2늑골/V03 느슨한 파지 기하 내보내기")]
    private static void ExportCoarseCurlCandidateSkinMenu()
    { ExportCandidateSkin(.5f); }

    private static void ExportCandidateSkin(float coarseCurl)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Native skin export requires edit mode.");
        string latest = null; DateTime modified = DateTime.MinValue;
        foreach (string path in Directory.GetFiles(Absolute(Folder), "hand_solution_bodycam_candidate_v*.json"))
        {
            DateTime stamp = File.GetLastWriteTimeUtc(path);
            if (stamp > modified) { latest = path; modified = stamp; }
        }
        if (latest == null) throw new FileNotFoundException("No hand candidate is available.");
        string target = Absolute(Folder + (coarseCurl >= 0f ? "/candidate_native_skin_curl50_" : "/candidate_native_skin_") + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".json");
        using (var session = new Rib2AnatomicalHandsReviewSession(false, coarseCurl, DefaultSmoothingPasses, false, Path.GetFileName(latest)))
            session.ExportFirstPersonGeometry(target);
        Debug.Log("[Rib2FirstPerson] Actual candidate body/guide skins exported: " + target);
    }

    private static string LatestFirstPersonDataFile(string pattern)
    {
        string latest = null; DateTime modified = DateTime.MinValue;
        foreach (string path in Directory.GetFiles(Absolute(Folder), pattern))
        {
            DateTime stamp = File.GetLastWriteTimeUtc(path);
            if (stamp > modified) { latest = path; modified = stamp; }
        }
        if (latest == null) throw new FileNotFoundException("No comparison hypothesis found: " + pattern);
        return Path.GetFileName(latest);
    }

    private void PrepareNeutralArmExport()
    {
        var seed = JsonUtility.FromJson<SolvedHands>(File.ReadAllText(Absolute(Folder + "/hand_solution.json")));
        moving.SetGraspFamily(seed.mcpOpen, seed.pipCurl, seed.dipCurl, seed.thumbOpposition);
        for (int frame = 0; frame < FrameCount; frame++)
        {
            var arm = SourceArm(SourceStart + frame / (float)Fps, out Observation ignored);
            arm.forearmRotation = arm.wristFlexion = arm.wristDeviation = 0f;
            presentationArms[frame] = arm;
            solvedRelativeGuideRotations[frame] = seed.frames[frame].guideRelativeRotation.normalized;
            solvedRelativeGuidePositions[frame] = seed.frames[frame].guideRelativePosition;
        }
        // Hands here are only reference geometry. No source wrist/guide match
        // is asserted before the subsequent independent two-view hand fit.
        solvedHandsLoaded = solvedSurfaceGrasp = true;
        InitializeSmoothMotion();
    }

    [MenuItem("GuideChuna/시험/제2늑골/V03 전체 파지 모양 기하 내보내기")]
    private static void ExportCoarseGuideFamiliesMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Coarse guide geometry requires edit mode.");
        string path = Absolute(Folder + "/guide_grasp_families_v3.json");
        if (File.Exists(path)) throw new IOException("Coarse guide export never overwrites an existing file.");
        using (var session = new Rib2AnatomicalHandsReviewSession(true)) session.ExportCoarseGuideFamilies(path);
        Debug.Log("[Rib2FirstPerson] Three whole-hand coarse hypotheses exported: " + path + "; no individual finger fitting.");
    }

    private void ExportCoarseGuideFamilies(string path)
    {
        float[][] presets = { new[] { 15f, 25f, 10f, 15f }, new[] { 45f, 60f, 35f, 15f }, new[] { 0f, 15f, 5f, 15f } };
        var families = new List<object>();
        for (int family = 0; family < presets.Length; family++)
        {
            float[] angles = presets[family];
            moving.Root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            moving.SetGraspFamily(angles[0], angles[1], angles[2], angles[3]);
            Quaternion inverse = Quaternion.Inverse(moving.Root.rotation);
            var data = CalibrationGeometry(moving.ExportGeometry());
            data.Add("id", family + 4); data.Add("mcpOpen", angles[0]); data.Add("pipCurl", angles[1]); data.Add("dipCurl", angles[2]); data.Add("thumbOpposition", angles[3]);
            data.Add("wristLocal", CalibrationArray(inverse * (moving.WristWorld - moving.Root.position)));
            data.Add("palmLocal", CalibrationArray(inverse * (moving.Joint(0).position - moving.Root.position)));
            data.Add("palmSurfaceLocal", CalibrationArray(inverse * (moving.PalmSurfaceWorld - moving.Root.position)));
            data.Add("ulnarHeelSurfaceLocal", CalibrationArray(inverse * (moving.UlnarHeelSurfaceWorld - moving.Root.position)));
            data.Add("palmarLocal", CalibrationArray(inverse * moving.PalmNormalWorld));
            data.Add("distalLocal", CalibrationArray(inverse * moving.FingerDirectionWorld));
            data.Add("radialLocal", CalibrationArray(inverse * moving.RadialWorld));
            var joints = new List<object>(26);
            for (int joint = 0; joint < 26; joint++) joints.Add(CalibrationArray(inverse * (moving.Joint(joint).position - moving.Root.position)));
            data.Add("jointsLocal", joints); data.Add("skinSamplingErrorMetres", moving.ValidateSkinSampling()); families.Add(data);
        }
        var json = new StringBuilder();
        CalibrationWrite(json, CalibrationObject("guideFamilies", families,
            "scope", "Actual Native SDK mesh in pure root-rotation-local metres for three fixed whole-hand authoring hypotheses. Values are offsets from SDK rest, not measured clinical finger angles. No per-finger fitting or source asset writes.",
            "contactHelperSha256", FileHash(Absolute("Assets/Editor/Rib2AnatomicalHandsContact.cs"))));
        json.Append('\n'); File.WriteAllText(path, json.ToString(), new UTF8Encoding(false));
    }

    private Dictionary<string, Transform> FirstPersonBodyBones()
    {
        var animator = root.GetComponentInChildren<Animator>(true);
        var data = new SerializedObject(rig);
        var bindings = data.FindProperty("bodyBindings");
        var map = new Dictionary<Transform, Transform>();
        for (int i = 0; i < bindings.arraySize; i++)
        {
            var binding = bindings.GetArrayElementAtIndex(i);
            map[(Transform)binding.FindPropertyRelative("source").objectReferenceValue] =
                (Transform)binding.FindPropertyRelative("output").objectReferenceValue;
        }
        var result = new Dictionary<string, Transform>();
        foreach (HumanBodyBones id in new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest,
            HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head, HumanBodyBones.LeftShoulder,
            HumanBodyBones.RightShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
            HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightHand, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg })
        {
            Transform source = animator.GetBoneTransform(id);
            if (source != null && map.TryGetValue(source, out Transform output)) result[id.ToString()] = output;
        }
        return result;
    }

    private object FirstPersonGuideGeometry(Rib2AnatomicalHandsContact.Hand hand)
    {
        var joints = new List<object>(26);
        for (int i = 0; i < 26; i++) joints.Add(CalibrationArray(hand.Joint(i).position));
        return CalibrationObject("rootWorld", CalibrationArray(hand.Root.position), "rotation", CalibrationArray(hand.Root.rotation),
            "wristWorld", CalibrationArray(hand.WristWorld), "palmWorld", CalibrationArray(hand.Joint(0).position),
            "palmSurfaceWorld", CalibrationArray(hand.PalmSurfaceWorld), "heelWorld", CalibrationArray(hand.UlnarHeelSurfaceWorld),
            "distalWorld", CalibrationArray(hand.FingerDirectionWorld), "palmarWorld", CalibrationArray(hand.PalmNormalWorld),
            "radialWorld", CalibrationArray(hand.RadialWorld), "jointsWorld", joints);
    }

    private void ExportFirstPersonGeometry(string path)
    {
        var bones = FirstPersonBodyBones();
        var frames = new List<object>(FrameCount);
        var meshes = new List<object>();
        var torsoMeshes = new List<object>();
        object supportGeometry = null, movingGeometry = null;
        var bake = new Mesh { name = "Private first-person geometry export", hideFlags = HideFlags.HideAndDontSave };
        try
        {
            Sample(0f, false);
            BakeTorsoQueries();
            foreach (var query in torsoOnlyQueries)
            {
                var points = new List<object>(query.vertices.Count);
                foreach (Vector3 vertex in query.vertices) points.Add(CalibrationArray(query.source.transform.TransformPoint(vertex)));
                torsoMeshes.Add(CalibrationObject("name", query.source.name, "verticesWorld", points,
                    "triangles", query.filteredIndices, "policy", "Every triangle vertex has >=0.6 explicit torso/rib/clavicle source influence; arm-source skin is excluded."));
            }
            supportGeometry = CalibrationObject("mesh", CalibrationGeometry(support.ExportGeometry()),
                "reference", FirstPersonGuideGeometry(support), "contactWorld", CalibrationArray(supportContact));
            movingGeometry = CalibrationObject("mesh", CalibrationGeometry(moving.ExportGeometry()), "reference", FirstPersonGuideGeometry(moving));
            foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skin.name != "CC_Base_Body" && skin.name != "Shirt") continue;
                skin.BakeMesh(bake);
                Vector3[] vertices = bake.vertices, normals = bake.normals;
                var points = new List<object>(vertices.Length);
                var directions = new List<object>(normals.Length);
                foreach (Vector3 vertex in vertices) points.Add(CalibrationArray(skin.transform.TransformPoint(vertex)));
                Matrix4x4 normalMatrix = skin.localToWorldMatrix.inverse.transpose;
                foreach (Vector3 normal in normals) directions.Add(CalibrationArray(normalMatrix.MultiplyVector(normal).normalized));
                meshes.Add(CalibrationObject("name", skin.name, "verticesWorld", points, "normalsWorld", directions, "triangles", bake.triangles));
            }
            for (int frame = 0; frame < FrameCount; frame++)
            {
                Sample(frame / (float)Fps, false);
                var body = new Dictionary<string, object>();
                foreach (var bone in bones) body[bone.Key] = CalibrationArray(bone.Value.position);
                var actualArm = currentPresentedArm;
                object actualPatient = CalibrationObject("wristWorld", CalibrationArray(fingers.Wrist.position),
                    "rotation", CalibrationArray(fingers.Wrist.rotation), "palmWorld", CalibrationArray(fingers.PalmCenterWorld),
                    "distalWorld", CalibrationArray(fingers.DistalWorld), "palmarWorld", CalibrationArray(fingers.PalmarWorld),
                    "radialWorld", CalibrationArray(fingers.RadialWorld), "thumbWorld", CalibrationArray(fingers.Thumb.position),
                    "indexWorld", CalibrationArray(fingers.Index.position), "middleWorld", CalibrationArray(fingers.Middle.position),
                    "littleWorld", CalibrationArray(fingers.Little.position),
                    "tipWorld", CalibrationArray(handTip.position + (handTip.position - handPrevious.position) * .55f));
                object movingGuide = FirstPersonGuideGeometry(moving), supportGuide = FirstPersonGuideGeometry(support);
                var neutralArm = actualArm;
                neutralArm.forearmRotation = neutralArm.wristFlexion = neutralArm.wristDeviation = 0f;
                Evaluate(neutralArm);
                Quaternion neutral = fingers.Wrist.rotation;
                Vector3 axis = (fingers.Wrist.position - lower.position).normalized;
                var trial = neutralArm; trial.wristFlexion = 1f; Evaluate(trial);
                Vector3 flex = CalibrationAxis(Quaternion.Inverse(neutral) * fingers.Wrist.rotation);
                trial = neutralArm; trial.wristDeviation = 1f; Evaluate(trial);
                Vector3 deviation = CalibrationAxis(Quaternion.Inverse(neutral) * fingers.Wrist.rotation);
                frames.Add(CalibrationObject("frameIndex", frame, "sourceSeconds", SourceStart + frame / (float)Fps,
                    "bodycamSeconds", SourceStart + frame / (float)Fps - 2.430f, "body", body,
                    "patient", actualPatient, "moving", movingGuide, "support", supportGuide,
                    "supportContactWorld", CalibrationArray(supportContact),
                    "supportSkinOutwardWorld", CalibrationArray(-supportBodyFrame.TransformDirection(supportNormalLocal)),
                    "chestRotation", CalibrationArray(supportBodyFrame.rotation), "armPose", CalibrationArm(actualArm),
                    "neutralQuaternion", CalibrationArray(neutral), "forearmAxisWorld", CalibrationArray(axis),
                    "flexAxisLocal", CalibrationArray(flex), "deviationAxisLocal", CalibrationArray(deviation)));
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(bake); }
        var json = new StringBuilder();
        CalibrationWrite(json, CalibrationObject("scope", "Actual native FK and baked private preview geometry. Bodycam time=MOV time-2.430s. Camera and hidden depth still require estimation; this export does not fit or save scenes.",
            "units", "Unity world metres; quaternion xyzw", "meshesAtFrameZero", meshes,
            "positivelyIdentifiedTorsoMeshesAtFrameZero", torsoMeshes,
            "supportGeometry", supportGeometry, "movingGeometry", movingGeometry, "frames", frames,
            "handSolutionSha256", HandSolutionHash,
            "patientFingerCurlActual", patientCurlOverride >= 0f ? patientCurlOverride : solvedPatientCurl,
            "patientFitFile", Path.GetFileName(NativePatientFitPath), "patientFitSha256", FileHash(NativePatientFitPath),
            "handDirectionSha256", FileHash(Absolute("Assets/Editor/Rib2AnatomicalHandsReviewSession.HandDirection.cs")),
            "forearmSkinSha256", FileHash(Absolute("Assets/Editor/Rib2AnatomicalForearmSkin.cs"))));
        json.Append('\n');
        File.WriteAllText(path, json.ToString(), new UTF8Encoding(false));
    }
}
