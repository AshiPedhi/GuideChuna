using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class AnatomicalPatientBuild
{
    [Serializable] private class BuildReceipt
    {
        public string prefab;
        public string scene;
        public string sourceCommit = "8c97b79 (pre-existing changes checkpoint; Play unverified)";
        public int anatomicalMeshes;
        public int changedSkinVertices;
        public string limitations = "Kinematic approximation. Shoulder rhythm, soft-tissue weights, cervical angular shares and corrective shapes require expert and device validation. Original c9 Avatar is an animation input. No clinical accuracy or force simulation is claimed.";
    }

    [MenuItem("GuideChuna/시험/해부학 환자/새 재리깅 복제본 생성")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before building.");
        Directory.CreateDirectory("Temp/model-audit");
        if (File.Exists("Temp/model-audit/build_failure.txt")) File.Delete("Temp/model-audit/build_failure.txt");
        // Fresh output folder on every explicit build. Never overwrite a source or an older generated patient.
        string folder = OutputFolder + "/Build_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        if (Directory.Exists(folder)) throw new IOException("Output already exists: " + folder);
        Directory.CreateDirectory(folder + "/Meshes");
        AssetDatabase.Refresh();
        Scene preview = EditorSceneManager.NewPreviewScene();
        GameObject root = new GameObject("AnatomicalPatient_c9");
        SceneManager.MoveGameObjectToScene(root, preview);
        GameObject anatomy = null;
        try
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(PatientPath);
            GameObject anatomyAsset = AssetDatabase.LoadAssetAtPath<GameObject>(AnatomyPath);
            if (source == null || anatomyAsset == null) throw new InvalidOperationException("Missing measured source models.");
            GameObject patient = UnityEngine.Object.Instantiate(source, root.transform);
            patient.name = "c9_AnimationInput";
            var animator = patient.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                throw new InvalidOperationException("c9 Humanoid Avatar is not valid.");
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Transform space = NewBone("AnatomicalOutput", root.transform, Vector3.zero);
            var mirrors = new Dictionary<Transform, Transform>();
            var bindings = new List<AnatomicalPatientRig.BoneBinding>();
            foreach (var renderer in patient.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                foreach (Transform bone in renderer.bones)
                {
                    if (bone == null) throw new InvalidOperationException("c9 has a missing skin bone.");
                    if (mirrors.ContainsKey(bone)) continue;
                    Transform output = NewBone("Skin_" + bone.name, space, bone.position);
                    output.rotation = bone.rotation;
                    output.localScale = bone.lossyScale;
                    mirrors.Add(bone, output);
                    bindings.Add(new AnatomicalPatientRig.BoneBinding { source = bone, output = output });
                }
            var limbs = new[] { CreateLimb(animator, space, true), CreateLimb(animator, space, false) };
            Vector3[] elbowPoles = MeasureAvatarElbowPoles(animator, space);
            for (int i = 0; i < limbs.Length; i++) limbs[i].restBendDirection = elbowPoles[i];
            anatomy = UnityEngine.Object.Instantiate(anatomyAsset);
            SceneManager.MoveGameObjectToScene(anatomy, preview);
            Transform anatomyRoot = NewBone("AnatomyMeshes", root.transform, Vector3.zero);
            var assembly = AnatomicalMeshBuilder.Build(anatomy, space, anatomyRoot, animator, limbs, mirrors, folder + "/Meshes");
            var skin = AnatomicalSkinBuilder.Rebind(patient, space, mirrors, limbs, assembly.neck, folder + "/Meshes");
            var rig = root.AddComponent<AnatomicalPatientRig>();
            rig.Configure(space, new AnatomicalPatientRig.Basis { right = Vector3.right, up = Vector3.up, forward = Vector3.forward },
                bindings.ToArray(), limbs, assembly.neck, skin.corrections);
            if (!rig.IsConfigured) throw new InvalidOperationException("Rig configuration failed; see [AnatomicalRig] logs.");
            rig.SetInputSource(AnatomicalPatientRig.InputSource.Manual);
            rig.SetAutomaticEvaluation(true);
            var bones = new List<SkinnedMeshRenderer>();
            var muscles = new List<SkinnedMeshRenderer>();
            foreach (var r in anatomyRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (r.transform.parent.name == "Skeleton") bones.Add(r); else muscles.Add(r);
            root.AddComponent<AnatomicalPatientLab>().Configure(rig, patient.GetComponentsInChildren<SkinnedMeshRenderer>(true), bones.ToArray(), muscles.ToArray());
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 30;
            rig.Evaluate(rig.RestPose);
            string prefabPath = folder + "/AnatomicalPatient_c9.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null) throw new IOException("Could not save generated prefab.");
            string scenePath = CreateLabScene(prefabPath, folder);
            var receipt = new BuildReceipt { prefab = prefabPath, scene = scenePath, anatomicalMeshes = assembly.meshes, changedSkinVertices = skin.changedVertices };
            string json = JsonUtility.ToJson(receipt, true);
            File.WriteAllText(folder + "/BuildReceipt.json", json);
            File.WriteAllText(OutputFolder + "/LatestBuild.json", json);
            Directory.CreateDirectory("Temp/model-audit");
            File.WriteAllText("Temp/model-audit/latest_build.json", json);
            AssetDatabase.Refresh();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Debug.Log("[AnatomicalPatient] Created new patient; c9 source Avatar retained. " + prefabPath + " | anatomy meshes=" + assembly.meshes + " | reskinned vertices=" + skin.changedVertices + " | original scenes were not saved.");
        }
        catch (Exception error)
        {
            File.WriteAllText("Temp/model-audit/build_failure.txt", folder + "\n" + error);
            throw;
        }
        finally
        {
            if (anatomy != null) UnityEngine.Object.DestroyImmediate(anatomy);
            UnityEngine.Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    internal static Transform NewBone(string name, Transform parent, Vector3 worldPosition)
    {
        var bone = new GameObject(name).transform;
        bone.SetParent(parent, false);
        bone.position = worldPosition;
        bone.rotation = Quaternion.identity;
        return bone;
    }

    private static AnatomicalPatientRig.Limb CreateLimb(Animator animator, Transform space, bool left)
    {
        Transform clavicle = animator.GetBoneTransform(left ? HumanBodyBones.LeftShoulder : HumanBodyBones.RightShoulder);
        Transform upper = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
        Transform lower = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
        Transform hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        if (clavicle == null || upper == null || lower == null || hand == null) throw new InvalidOperationException("c9 limb landmarks are missing.");
        string side = left ? "Left_" : "Right_";
        Vector3 scapulaCenter = upper.position + Vector3.back * .06f;
        return new AnatomicalPatientRig.Limb
        {
            left = left, sourceClavicle = clavicle, sourceUpper = upper, sourceLower = lower, sourceHand = hand,
            clavicle = NewBone(side + "Clavicle", space, clavicle.position), scapula = NewBone(side + "Scapula", space, scapulaCenter),
            humerus = NewBone(side + "Humerus", space, upper.position), ulna = NewBone(side + "Ulna", space, lower.position),
            radius = NewBone(side + "Radius", space, lower.position), hand = NewBone(side + "Hand", space, hand.position),
            clavicleMedial = clavicle.position, shoulder = upper.position, elbow = lower.position, wrist = hand.position,
            scapulaCenter = scapulaCenter, radiusAxisStart = lower.position, radiusAxisEnd = hand.position
        };
    }

    private static string CreateLabScene(string prefabPath, string folder)
    {
        // Additive creation preserves the user's unsaved TrainingScene. Only this fresh scene is saved.
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        string path = folder + "/AnatomicalPatientLab.unity";
        GameObject patient = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath), scene);
        var cameraObject = new GameObject("AnatomicalLabCamera");
        SceneManager.MoveGameObjectToScene(cameraObject, scene);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.15f, .17f, .20f);
        camera.cullingMask = 1 << 30; camera.fieldOfView = 35f; camera.nearClipPlane = .01f; camera.farClipPlane = 20f;
        camera.transform.position = new Vector3(0f, 1.2f, 3.2f);
        camera.transform.LookAt(new Vector3(0f, 1.2f, 0f));
        foreach (Vector3 position in new[] { new Vector3(-2f, 3f, 2f), new Vector3(2f, 2f, -2f) })
        {
            var go = new GameObject("AnatomicalLabLight"); SceneManager.MoveGameObjectToScene(go, scene);
            Light light = go.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = .9f; light.cullingMask = 1 << 30;
            go.transform.position = position; go.transform.LookAt(new Vector3(0f, 1.2f, 0f));
        }
        if (!EditorSceneManager.SaveScene(scene, path)) throw new IOException("Could not save fresh lab scene.");
        // Keep the existing unsaved scene and the user's active scene intact; lab can be opened explicitly later.
        EditorSceneManager.CloseScene(scene, true);
        return path;
    }
}
