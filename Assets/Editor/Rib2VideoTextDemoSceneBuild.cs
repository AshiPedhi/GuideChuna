using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Oculus.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Explicit, create-once demonstration scene builder. Reads only the separately
/// authored video/text draft. Never saves an existing scene or edits a source asset.
/// </summary>
public static class Rib2VideoTextDemoSceneBuild
{
    private const int DemoLayer = 29;
    private const string OutputFolder = "Assets/Demos/Rib2VideoTextScene/V01";
    private const string ScenePath = OutputFolder + "/Rib2VideoTextDemo.unity";
    private const string DraftFolder = "Captures/Rib2VideoTextOnly_V0.1_20261026";
    private const string PatientGuid = "c9c2f40122045a1408d886015538ca53";
    private const string LeftHandGuid = "70d02a90551f23042a882cdceeaf8e3a";
    private const string RightHandGuid = "34a22dd67c1e5344591237fdd61e78ec";

    [Serializable]
    private sealed class AnnotationDocument
    {
        public float source_start_s = 0f;
        public AnnotationKey[] keys = null;
    }

    [Serializable]
    private sealed class AnnotationKey
    {
        public float video_s = 0f;
        public float[] patient_elbow = null;
    }

    [Serializable]
    private sealed class BuildReceipt
    {
        public string scenePath = "";
        public string sourceCsv = "";
        public string sourceCsvSha256 = "";
        public string importedCsv = "";
        public string patientAsset = "";
        public string leftHandAsset = "";
        public string rightHandAsset = "";
        public string untouched = "Existing scenes, source meshes/materials, Resources CSVs, scenarios and pass conditions were not saved or modified.";
        public string limitations = "Presentation draft. Metric scale, depth, rib contact and patient registration are authored approximations; no tracking input, clinical angle or scoring is applied.";
        public string elbowHintMethod = "Original video manual patient_elbow pixels: local x=(pixelX-450)/550, y=(318-pixelY)/550, z=0; times=video_s-56.5. This is an authored projection, not measured depth.";
        public string gripAnchorMethod = "Temporary local offset (0.02,-0.025,0.085) under the cloned left guide wrist; inspect and refine on this separate scene only.";
        public int leftJointCount = 0;
        public int rightJointCount = 0;
        public int elbowHintCount = 0;
        public float duration = 0f;
        public Vector3 patientPosition;
        public Quaternion patientRotation;
        public Vector3 shoulderWorld;
        public Vector3 elbowWorld;
        public Vector3 wristWorld;
        public float upperArmLength = 0f;
        public float forearmLength = 0f;
        public Vector3 motionFramePosition;
        public Quaternion motionFrameRotation;
        public Vector3 motionFrameScale;
        public float motionFrameDeterminant = 0f;
        public string contactSurfaceMethod = "Downward ray against temporary MeshColliders of the cloned body's and shirt's baked skins. X/Z are authored upper-chest estimates; surface height is measured on this model, not an anatomical rib landmark.";
        public Vector3 measuredContactSurface;
        public string contactSurfaceRenderer = "";
        public Vector3 leftRetargetEuler;
        public Vector3 rightRetargetEuler;
        public Vector3 rightPalmDownWorld;
        public float rightPalmDownDot = 0f;
        public List<string> skinRenderers = new List<string>();
    }

    public static string Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Build the separate demonstration outside Play mode and compilation.");
        if (File.Exists(Absolute(ScenePath)) || AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            throw new InvalidOperationException("The demonstration scene already exists; refusing to overwrite " + ScenePath);

        string sourceCsv = Absolute(DraftFolder + "/video_text_demo.csv");
        string sourceAnnotations = Absolute(DraftFolder + "/source_annotations.json");
        if (!File.Exists(sourceCsv) || !File.Exists(sourceAnnotations))
            throw new FileNotFoundException("The separately authored video/text CSV or annotation JSON is missing.");
        string destinationCsv = OutputFolder + "/Data/video_text_demo.csv";
        string destinationAnnotations = OutputFolder + "/Data/source_annotations.json";
        RefuseExistingFile(destinationCsv);
        RefuseExistingFile(destinationAnnotations);
        RefuseExistingFile(OutputFolder + "/BuildReceipt.json");
        foreach (string materialName in new[] { "Bed", "BedFrame", "Floor", "LeftGuideCyan", "RightGuideOrange" })
            RefuseExistingFile(OutputFolder + "/Materials/" + materialName + ".mat");
        AnnotationDocument annotation = JsonUtility.FromJson<AnnotationDocument>(File.ReadAllText(sourceAnnotations, Encoding.UTF8));
        ValidateAnnotations(annotation);
        EnsureFolder(OutputFolder + "/Data");
        EnsureFolder(OutputFolder + "/Materials");
        File.Copy(sourceCsv, Absolute(destinationCsv), false);
        File.Copy(sourceAnnotations, Absolute(destinationAnnotations), false);
        AssetDatabase.ImportAsset(destinationCsv, ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset(destinationAnnotations, ImportAssetOptions.ForceSynchronousImport);
        TextAsset motionAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(destinationCsv);
        if (motionAsset == null) throw new InvalidOperationException("The new draft CSV was not imported as a TextAsset.");
        Scene originalActiveScene = SceneManager.GetActiveScene();
        Scene demoScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        bool saved = false;
        try
        {
            SceneManager.SetActiveScene(demoScene);
            GameObject root = NewObject("Rib2 Video Text Demonstration", demoScene);
            var receipt = new BuildReceipt
            {
                scenePath = ScenePath,
                sourceCsv = sourceCsv,
                sourceCsvSha256 = Sha256(sourceCsv),
                importedCsv = destinationCsv,
                patientAsset = AssetDatabase.GUIDToAssetPath(PatientGuid)
            };

            GameObject patientAsset = AssetDatabase.LoadAssetAtPath<GameObject>(receipt.patientAsset);
            if (patientAsset == null) throw new InvalidOperationException("The measured c9 source FBX was not found.");
            var patient = (GameObject)PrefabUtility.InstantiatePrefab(patientAsset, demoScene);
            patient.name = "c9 Original Patient - Separate Demo";
            patient.transform.SetParent(root.transform, false);
            patient.transform.SetPositionAndRotation(new Vector3(10f, 1.05f, 0f), Quaternion.Euler(-90f, 0f, 0f));
            patient.transform.localScale = Vector3.one;
            SetLayerRecursively(patient, DemoLayer);
            Animator animator = patient.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                throw new InvalidOperationException("The source c9 model does not provide a valid Humanoid Avatar.");
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.enabled = false;

            Transform upper = RequiredBone(animator, HumanBodyBones.RightUpperArm);
            Transform lower = RequiredBone(animator, HumanBodyBones.RightLowerArm);
            Transform wrist = RequiredBone(animator, HumanBodyBones.RightHand);
            Transform leftUpper = RequiredBone(animator, HumanBodyBones.LeftUpperArm);
            Transform leftLower = RequiredBone(animator, HumanBodyBones.LeftLowerArm);
            LowerNonDemoArm(leftUpper, leftLower);
            ValidatePatientSkin(patient, receipt);
            receipt.patientPosition = patient.transform.position;
            receipt.patientRotation = patient.transform.rotation;
            receipt.shoulderWorld = upper.position;
            receipt.elbowWorld = lower.position;
            receipt.wristWorld = wrist.position;
            receipt.upperArmLength = Vector3.Distance(upper.position, lower.position);
            receipt.forearmLength = Vector3.Distance(lower.position, wrist.position);

            Material bedMaterial = NewMaterial("Bed", new Color(.18f, .24f, .29f));
            Material frameMaterial = NewMaterial("BedFrame", new Color(.28f, .32f, .35f));
            Material floorMaterial = NewMaterial("Floor", new Color(.075f, .09f, .115f));
            Box("Demonstration Bed", root.transform, new Vector3(10f, .75f, -.94f), new Vector3(.78f, .12f, 2.22f), bedMaterial);
            Box("Bed Frame", root.transform, new Vector3(10f, .49f, -.94f), new Vector3(.66f, .45f, 1.94f), frameMaterial);
            Box("Demonstration Floor", root.transform, new Vector3(10f, .22f, -.9f), new Vector3(4.5f, .04f, 4.5f), floorMaterial);

            Transform frame = NewObject("Video Text Motion Frame - Authored Registration", demoScene).transform;
            frame.SetParent(root.transform, false);
            Vector3 contactSurface = MeasureClonedChestSurface(patient, demoScene,
                upper.position.x - .08f, upper.position.z + .07f, out string surfaceRenderer);
            receipt.measuredContactSurface = contactSurface;
            receipt.contactSurfaceRenderer = surfaceRenderer;
            frame.SetPositionAndRotation(contactSurface + Vector3.up * .012f,
                Quaternion.LookRotation(Vector3.up, Vector3.back));
            frame.localScale = Vector3.one;
            ValidateMotionFrame(frame);
            receipt.motionFramePosition = frame.position;
            receipt.motionFrameRotation = frame.rotation;
            receipt.motionFrameScale = frame.lossyScale;
            receipt.motionFrameDeterminant = frame.localToWorldMatrix.determinant;

            HandTransformMapper left = CreateHand(LeftHandGuid, true, new Color(.12f, .77f, .94f), demoScene, root.transform, receipt);
            HandTransformMapper right = CreateHand(RightHandGuid, false, new Color(1f, .59f, .25f), demoScene, root.transform, receipt);
            Transform gripAnchor = NewObject("Patient Wrist Target - Authored Grip Offset", demoScene).transform;
            gripAnchor.SetParent(left.Root, false);
            gripAnchor.localPosition = new Vector3(.02f, -.025f, .085f);
            gripAnchor.localRotation = Quaternion.identity;

            Transform hint = NewObject("Patient Elbow Hint - Video Annotation", demoScene).transform;
            hint.SetParent(frame, false);
            hint.localPosition = new Vector3(.22f, -.2f, 0f);
            hint.localRotation = Quaternion.identity;

            var player = root.AddComponent<Rib2VideoTextScenePlayer>();
            var playerData = new SerializedObject(player);
            SetObject(playerData, "motionCsv", motionAsset);
            SetObject(playerData, "motionFrame", frame);
            SetObject(playerData, "leftGuideHand", left);
            SetObject(playerData, "rightGuideHand", right);
            SetObject(playerData, "leftGripAnchor", gripAnchor);
            receipt.leftRetargetEuler = new Vector3(0f, 0f, 180f);
            receipt.rightRetargetEuler = new Vector3(0f, 0f, 180f);
            Property(playerData, "leftRetargetEuler").vector3Value = receipt.leftRetargetEuler;
            Property(playerData, "rightRetargetEuler").vector3Value = receipt.rightRetargetEuler;
            Property(playerData, "autoPlay").boolValue = false;
            playerData.ApplyModifiedPropertiesWithoutUndo();
            if (!player.TryInitialize(out string playerError) || !player.Seek(0f))
                throw new InvalidOperationException("Separate guide player initialization failed: " + playerError);
            receipt.rightPalmDownWorld = right.Root.TransformDirection(Vector3.down).normalized;
            receipt.rightPalmDownDot = Vector3.Dot(receipt.rightPalmDownWorld, Vector3.down);
            Debug.Log("[Rib2SceneBuild] Measured clone chest surface=" + contactSurface + " on " + surfaceRenderer +
                "; motion origin=" + frame.position + "; right wrist local-down in world=" + receipt.rightPalmDownWorld +
                "; dot world-down=" + receipt.rightPalmDownDot.ToString("F4", CultureInfo.InvariantCulture) +
                "; retarget rotation is separate from source root positions.", root);
            if (receipt.rightPalmDownDot <= .5f)
                throw new InvalidOperationException("The measured right guide palm direction does not face the cloned chest; refusing to save.");

            var arm = root.AddComponent<Rib2DemoPatientArm>();
            var armData = new SerializedObject(arm);
            SetObject(armData, "upperArm", upper);
            SetObject(armData, "forearm", lower);
            SetObject(armData, "patientHand", wrist);
            SetObject(armData, "wristTarget", gripAnchor);
            SetObject(armData, "elbowHint", hint);
            SetObject(armData, "motionFrame", frame);
            SetObject(armData, "player", player);
            SetObject(armData, "guideHandRoot", left.Root);
            SerializedProperty times = Property(armData, "hintTimes");
            SerializedProperty positions = Property(armData, "hintPositions");
            times.arraySize = annotation.keys.Length;
            positions.arraySize = annotation.keys.Length;
            for (int i = 0; i < annotation.keys.Length; i++)
            {
                AnnotationKey key = annotation.keys[i];
                times.GetArrayElementAtIndex(i).floatValue = key.video_s - annotation.source_start_s;
                positions.GetArrayElementAtIndex(i).vector3Value = new Vector3((key.patient_elbow[0] - 450f) / 550f,
                    (318f - key.patient_elbow[1]) / 550f, 0f);
            }
            armData.ApplyModifiedPropertiesWithoutUndo();
            arm.CaptureReferencePose(upper, lower, wrist);
            if (!arm.Initialize(out string armError) || !arm.Evaluate())
                throw new InvalidOperationException("Separate patient arm initialization failed: " + armError);
            receipt.duration = player.Duration;
            receipt.elbowHintCount = annotation.keys.Length;

            CreateCameraAndLights(root.transform, demoScene);
            Rib2DemoSceneControlsBuilder.Create(demoScene, root, player, arm);
            ValidatePatientSkin(patient, null);
            CheckRenderedMaterialsBeforeSave(root.GetComponentInChildren<Camera>());
            if (!EditorSceneManager.SaveScene(demoScene, ScenePath, false))
                throw new IOException("Unity did not save the new separate demonstration scene.");
            saved = true;
            string receiptPath = OutputFolder + "/BuildReceipt.json";
            RefuseExistingFile(receiptPath);
            File.WriteAllText(Absolute(receiptPath), JsonUtility.ToJson(receipt, true), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(receiptPath, ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("[Rib2SceneBuild] Saved NEW demonstration scene only: " + ScenePath +
                "; duration=" + receipt.duration.ToString("F3", CultureInfo.InvariantCulture) +
                "; L26/R26; positive frame determinant=" + receipt.motionFrameDeterminant.ToString("F3", CultureInfo.InvariantCulture) +
                "; patient arm=" + receipt.upperArmLength.ToString("F3", CultureInfo.InvariantCulture) + "+" +
                receipt.forearmLength.ToString("F3", CultureInfo.InvariantCulture) +
                "m; registration/grip depth are authored approximations; existing scenes were not saved.", root);
            Selection.activeGameObject = root;
            return ScenePath;
        }
        finally
        {
            if (originalActiveScene.IsValid() && originalActiveScene.isLoaded)
                SceneManager.SetActiveScene(originalActiveScene);
            if (!saved && demoScene.IsValid() && demoScene.isLoaded)
                EditorSceneManager.CloseScene(demoScene, true);
        }
    }

    private static HandTransformMapper CreateHand(string guid, bool isLeft, Color color, Scene scene, Transform parent, BuildReceipt receipt)
    {
        string path = AssetDatabase.GUIDToAssetPath(guid);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) throw new InvalidOperationException("Original Meta hand prefab is missing: " + guid);
        var clone = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        clone.name = isLeft ? "Left Guide Hand - Video Text Only" : "Right Guide Hand - Video Text Only";
        clone.transform.SetParent(parent, false);
        clone.transform.localScale = Vector3.one;
        SetLayerRecursively(clone, DemoLayer);
        foreach (Behaviour behaviour in clone.GetComponentsInChildren<Behaviour>(true)) behaviour.enabled = false;
        foreach (Collider collider in clone.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (Rigidbody body in clone.GetComponentsInChildren<Rigidbody>(true))
        {
            body.useGravity = false;
            body.isKinematic = true;
        }
        HandVisual visual = clone.GetComponentInChildren<HandVisual>(true);
        if (visual == null) throw new InvalidOperationException("HandVisual was not found: " + path);
        var visualData = new SerializedObject(visual);
        var openXrRoot = Property(visualData, "_openXRRoot").objectReferenceValue as Transform;
        var legacyRoot = Property(visualData, "_root").objectReferenceValue as Transform;
        SerializedProperty array = Property(visualData, "_openXRJointTransforms");
        if (openXrRoot == null || array.arraySize != 26)
            throw new InvalidOperationException("Expected the original 26-joint OpenXR hand: " + path);
        if (legacyRoot != null) legacyRoot.gameObject.SetActive(false);
        openXrRoot.gameObject.SetActive(true);
        var joints = new List<Transform>(26);
        for (int i = 0; i < 26; i++)
        {
            var joint = array.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
            if (joint == null) throw new InvalidOperationException("Missing OpenXR hand joint " + i);
            joints.Add(joint);
        }
        var mapper = clone.AddComponent<HandTransformMapper>();
        mapper.enabled = false;
        mapper.SetHandType(isLeft);
        var mapperData = new SerializedObject(mapper);
        Property(mapperData, "copyJointsOnStart").boolValue = false;
        SetObject(mapperData, "rootTransform", openXrRoot);
        mapperData.ApplyModifiedPropertiesWithoutUndo();
        mapper.SetJoints(joints);

        Material material = NewMaterial(isLeft ? "LeftGuideCyan" : "RightGuideOrange", color);
        int count = 0;
        foreach (SkinnedMeshRenderer renderer in openXrRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (renderer.sharedMesh == null) continue;
            var materials = new Material[renderer.sharedMesh.subMeshCount];
            for (int i = 0; i < materials.Length; i++) materials[i] = material;
            renderer.sharedMaterials = materials;
            renderer.updateWhenOffscreen = true;
            renderer.enabled = true;
            count++;
        }
        if (count == 0) throw new InvalidOperationException("No original skinned mesh in OpenXR hand: " + path);
        if (isLeft) { receipt.leftHandAsset = path; receipt.leftJointCount = joints.Count; }
        else { receipt.rightHandAsset = path; receipt.rightJointCount = joints.Count; }
        return mapper;
    }

    private static void ValidatePatientSkin(GameObject patient, BuildReceipt receipt)
    {
        int count = 0;
        foreach (SkinnedMeshRenderer renderer in patient.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            renderer.updateWhenOffscreen = true;
            if (renderer.sharedMesh == null) continue;
            foreach (Material material in renderer.sharedMaterials)
            {
                if (material == null || material.shader == null || !material.shader.isSupported ||
                    material.shader.name.IndexOf("InternalErrorShader", StringComparison.OrdinalIgnoreCase) >= 0)
                    throw new InvalidOperationException("Patient material is missing, unsupported or pink; refusing to save the new scene: " + renderer.name);
            }
            if (receipt != null) receipt.skinRenderers.Add(renderer.name + ":" + renderer.sharedMesh.name + ":" + renderer.sharedMesh.vertexCount);
            count++;
        }
        if (count == 0) throw new InvalidOperationException("The patient clone has no skin renderers.");
    }

    private static void CheckRenderedMaterialsBeforeSave(Camera camera)
    {
        if (camera == null) throw new InvalidOperationException("The demonstration camera is missing before save.");
        var target = RenderTexture.GetTemporary(480, 300, 24);
        RenderTexture oldTarget = camera.targetTexture;
        RenderTexture oldActive = RenderTexture.active;
        Texture2D image = null;
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image = new Texture2D(480, 300, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0f, 0f, 480f, 300f), 0, 0);
            image.Apply();
            Color32[] pixels = image.GetPixels32();
            int pink = 0;
            foreach (var pixel in pixels)
                if (pixel.r > 180 && pixel.b > 180 && pixel.g < 90) pink++;
            float fraction = (float)pink / pixels.Length;
            Debug.Log("[Rib2SceneBuild] Pre-save error-magenta pixel fraction=" + fraction.ToString("F5", CultureInfo.InvariantCulture));
            if (fraction > .015f) throw new InvalidOperationException("Rendered scene contains error-magenta material pixels; refusing to save. Inspect the clone before proceeding.");
        }
        finally
        {
            camera.targetTexture = oldTarget;
            RenderTexture.active = oldActive;
            if (image != null) UnityEngine.Object.DestroyImmediate(image);
            RenderTexture.ReleaseTemporary(target);
        }
    }

    private static Vector3 MeasureClonedChestSurface(GameObject patient, Scene scene, float x, float z, out string rendererName)
    {
        // Dependence: the clone's posed skin vertices and full renderer transform,
        // and the authored X/Z sample point. No original mesh/material is modified.
        bool hitAny = false;
        Vector3 highest = Vector3.zero;
        rendererName = "";
        foreach (SkinnedMeshRenderer renderer in patient.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            bool body = renderer.name.IndexOf("CC_Base_Body", StringComparison.OrdinalIgnoreCase) >= 0;
            bool shirt = renderer.name.IndexOf("Shirt", StringComparison.OrdinalIgnoreCase) >= 0;
            if ((!body && !shirt) || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.sharedMesh == null) continue;
            Mesh baked = null;
            GameObject holder = null;
            try
            {
                baked = new Mesh { name = "Temporary Rib2 clone surface measurement", hideFlags = HideFlags.HideAndDontSave };
                renderer.BakeMesh(baked);
                baked.RecalculateBounds();
                holder = NewObject("Temporary baked patient surface raycast", scene);
                holder.hideFlags = HideFlags.HideAndDontSave;
                holder.transform.SetPositionAndRotation(renderer.transform.position, renderer.transform.rotation);
                holder.transform.localScale = renderer.transform.lossyScale;
                MeshCollider collider = holder.AddComponent<MeshCollider>();
                collider.sharedMesh = baked;
                Physics.SyncTransforms();
                float maximumY = float.NegativeInfinity;
                Bounds bounds = baked.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 local = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                    maximumY = Mathf.Max(maximumY, holder.transform.TransformPoint(local).y);
                }
                var ray = new Ray(new Vector3(x, maximumY + .5f, z), Vector3.down);
                if (collider.Raycast(ray, out RaycastHit hit, 5f) && (!hitAny || hit.point.y > highest.y))
                {
                    highest = hit.point;
                    rendererName = renderer.name;
                    hitAny = true;
                }
            }
            finally
            {
                if (holder != null) UnityEngine.Object.DestroyImmediate(holder);
                if (baked != null) UnityEngine.Object.DestroyImmediate(baked);
            }
        }
        if (!hitAny || !Finite(highest.x) || !Finite(highest.y) || !Finite(highest.z))
            throw new InvalidOperationException("The authored upper-chest X/Z ray did not hit the cloned body or shirt. A separate-scene contact placement adjustment is required; no height was guessed.");
        return highest;
    }

    private static void LowerNonDemoArm(Transform upper, Transform lower)
    {
        Transform parent = upper.parent;
        if (parent == null) throw new InvalidOperationException("Patient left upper arm has no parent.");
        Vector3 current = parent.InverseTransformPoint(lower.position) - parent.InverseTransformPoint(upper.position);
        Vector3 desired = parent.InverseTransformVector(Vector3.forward);
        if (current.sqrMagnitude < .000001f || desired.sqrMagnitude < .000001f)
            throw new InvalidOperationException("Cannot measure the non-demonstration arm direction.");
        upper.localRotation = Quaternion.FromToRotation(current, desired) * upper.localRotation;
    }

    private static void ValidateMotionFrame(Transform frame)
    {
        float determinant = frame.localToWorldMatrix.determinant;
        if (determinant <= 0f || Vector3.Dot(frame.right, Vector3.right) < .9999f ||
            Vector3.Dot(frame.up, Vector3.back) < .9999f || Vector3.Dot(frame.forward, Vector3.up) < .9999f ||
            (frame.lossyScale - Vector3.one).sqrMagnitude > .000001f)
            throw new InvalidOperationException("The authored motion frame is reflected or its measured basis does not match +X/right, -Z/head, +Y/anterior.");
    }

    private static void ValidateAnnotations(AnnotationDocument annotation)
    {
        if (annotation == null || annotation.keys == null || annotation.keys.Length < 2 ||
            Mathf.Abs(annotation.source_start_s - 56.5f) > .0001f)
            throw new InvalidOperationException("Expected the separate 56.5s video/text annotations.");
        float previous = float.NegativeInfinity;
        foreach (AnnotationKey key in annotation.keys)
        {
            if (key == null || key.patient_elbow == null || key.patient_elbow.Length != 2 ||
                !Finite(key.video_s) || !Finite(key.patient_elbow[0]) || !Finite(key.patient_elbow[1]) || key.video_s <= previous)
                throw new InvalidOperationException("Invalid or unordered manual video elbow annotation.");
            previous = key.video_s;
        }
    }

    private static void CreateCameraAndLights(Transform parent, Scene scene)
    {
        GameObject cameraObject = NewObject("Rib2 Demonstration Camera", scene);
        cameraObject.transform.SetParent(parent, false);
        cameraObject.transform.position = new Vector3(11.6f, 2.55f, -2f);
        cameraObject.transform.LookAt(new Vector3(10.25f, 1.05f, -1.1f), Vector3.up);
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.035f, .05f, .075f);
        camera.cullingMask = 1 << DemoLayer;
        camera.nearClipPlane = .01f;
        camera.farClipPlane = 30f;
        camera.fieldOfView = 45f;
        camera.stereoTargetEye = StereoTargetEyeMask.None;
        camera.depth = 50f;
        CreateLight("Rib2 Key Light", scene, parent, Quaternion.Euler(45f, -30f, 0f), 1.15f);
        CreateLight("Rib2 Fill Light", scene, parent, Quaternion.Euler(25f, 145f, 0f), .7f);
    }

    private static void CreateLight(string name, Scene scene, Transform parent, Quaternion rotation, float intensity)
    {
        GameObject holder = NewObject(name, scene);
        holder.transform.SetParent(parent, false);
        holder.transform.rotation = rotation;
        Light light = holder.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = intensity;
        light.cullingMask = 1 << DemoLayer;
        light.shadows = LightShadows.None;
    }

    private static GameObject NewObject(string name, Scene scene)
    {
        var result = new GameObject(name) { layer = DemoLayer };
        SceneManager.MoveGameObjectToScene(result, scene);
        return result;
    }

    private static void Box(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        SceneManager.MoveGameObjectToScene(box, parent.gameObject.scene);
        box.name = name;
        box.layer = DemoLayer;
        box.transform.SetParent(parent, false);
        box.transform.position = position;
        box.transform.localScale = scale;
        Collider collider = box.GetComponent<Collider>();
        if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
        box.GetComponent<Renderer>().sharedMaterial = material;
    }

    private static Material NewMaterial(string name, Color color)
    {
        string path = OutputFolder + "/Materials/" + name + ".mat";
        RefuseExistingFile(path);
        Shader shader = Shader.Find("Standard");
        if (shader == null || !shader.isSupported) throw new InvalidOperationException("Built-in Standard shader is unavailable.");
        var material = new Material(shader) { name = "Rib2 Demo " + name, color = color };
        material.SetFloat("_Glossiness", .22f);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static Transform RequiredBone(Animator animator, HumanBodyBones bone)
    {
        Transform result = animator.GetBoneTransform(bone);
        if (result == null) throw new InvalidOperationException("The source patient is missing Humanoid bone " + bone);
        return result;
    }

    private static SerializedProperty Property(SerializedObject data, string field)
    {
        SerializedProperty result = data.FindProperty(field);
        if (result == null) throw new InvalidOperationException("Required demo field is missing: " + data.targetObject.GetType().Name + "." + field);
        return result;
    }

    private static void SetObject(SerializedObject data, string field, UnityEngine.Object value)
    {
        Property(data, field).objectReferenceValue = value;
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = layer;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/'));
        string name = path.Substring(path.LastIndexOf('/') + 1);
        EnsureFolder(parent);
        if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, name)))
            throw new IOException("Unity could not create the new demonstration folder: " + path);
    }

    private static void RefuseExistingFile(string path)
    {
        if (File.Exists(Absolute(path))) throw new IOException("Refusing to overwrite an existing demonstration file: " + path);
    }

    private static string Absolute(string projectRelative)
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", projectRelative));
    }

    private static string Sha256(string path)
    {
        using (var stream = File.OpenRead(path))
        using (SHA256 sha = SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(stream);
            var result = new StringBuilder(hash.Length * 2);
            foreach (byte value in hash) result.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return result.ToString();
        }
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
