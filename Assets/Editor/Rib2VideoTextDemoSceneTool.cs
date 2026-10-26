using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Isolated scene authoring only. Existing dirty scenes are never saved.
[InitializeOnLoad]
public static class Rib2VideoTextDemoSceneTool
{
    private const string PatientGuid = "c9c2f40122045a1408d886015538ca53";
    private const string Work = "Temp/rib2-scene-demo";
    private static string RequestPath => Work + "/request.json";
    private static double nextPoll;
    private static int previewIndex = -1;
    private const string PreviewFolder = Work + "/camera_frames_baked";
    [Serializable] private sealed class Request { public string command = "probe"; public float time = 0f; }
    [Serializable] private sealed class SweepResult
    {
        public string scene; public int samples; public float maximumReachCorrection;
        public float maximumWristError; public float maximumBoneLengthChange;
        public float rightPalmDownDot; public float frameDeterminant;
        public string validation = "Editor samples use the exact runtime player and arm solver. This is not a Play or device test.";
    }
    [Serializable] private sealed class BoneInfo
    {
        public string name; public string parent; public Vector3 position; public Quaternion rotation;
        public Vector3 localPosition; public Quaternion localRotation; public Vector3 scale; public float determinant;
    }
    [Serializable] private sealed class MeshInfo { public string name; public Bounds bounds; public string[] materials; public string[] shaders; }
    [Serializable] private sealed class ContactInfo { public string path; public Vector3 world; public Vector3 patientLocal; public Quaternion patientLocalRotation; }
    [Serializable] private sealed class Probe
    {
        public string patientAsset; public bool humanoid; public BoneInfo[] bones; public MeshInfo[] meshes;
        public ContactInfo[] existingContacts; public string note = "Geometry and scene anchors only; no animation clips or recorded hand data sampled. Existing scenes not saved.";
    }

    static Rib2VideoTextDemoSceneTool() { EditorApplication.update += Poll; }
    private static void Poll()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (previewIndex >= 0)
        {
            try
            {
                Capture(previewIndex / 30f, true);
                previewIndex++;
                if (previewIndex >= 248)
                {
                    previewIndex = -1;
                    var player = DemoPlayer(); player.ShowStartPose(); DemoArm(player).Evaluate();
                    File.WriteAllText(Work + "/preview_baked_complete.json", "{\"frames\":248,\"fps\":30,\"playMode\":false,\"skinCapture\":\"CPU BakeMesh from the same evaluated bones\"}");
                    Debug.Log("[Rib2SceneAuthor] Completed 248 actual scene-camera preview frames. Play was not entered.");
                    FocusDemo();
                }
            }
            catch (Exception error)
            {
                previewIndex = -1; File.WriteAllText(Work + "/error.txt", error.ToString());
                Debug.LogError("[Rib2SceneAuthor] Preview failed: " + error);
            }
            return;
        }
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + .25;
        if (!File.Exists(RequestPath)) return;
        if (File.Exists(Work + "/error.txt")) File.Delete(Work + "/error.txt");
        try
        {
            var request = JsonUtility.FromJson<Request>(File.ReadAllText(RequestPath));
            File.Delete(RequestPath);
            if (request.command == "probe") Measure();
            else if (request.command == "build")
            {
                string path = Rib2VideoTextDemoSceneBuild.Build();
                File.WriteAllText(Work + "/built_scene.txt", path);
                FocusDemo();
            }
            else if (request.command == "capture") Capture(request.time);
            else if (request.command == "preview")
            {
                string folder = PreviewFolder;
                if (Directory.Exists(folder) && Directory.GetFiles(folder).Length > 0)
                    throw new IOException("Preview output is not empty; refusing to overwrite.");
                Directory.CreateDirectory(folder); previewIndex = 0;
            }
            else if (request.command == "sweep") Sweep();
            else if (request.command == "save-start") SaveStart();
            else if (request.command == "ui-probe") ProbeUi(false);
            else if (request.command == "ui-repair") ProbeUi(true);
            else if (request.command == "verify-reload") VerifyReload();
            else if (request.command == "record-reference") RecordOriginalReference();
            else throw new InvalidOperationException("Unknown isolated demo request: " + request.command);
        }
        catch (Exception error)
        {
            Directory.CreateDirectory(Work);
            File.WriteAllText(Work + "/error.txt", error.ToString());
            Debug.LogError("[Rib2SceneAuthor] " + error);
        }
    }

    private static Rib2VideoTextScenePlayer DemoPlayer()
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.path.StartsWith("Assets/Demos/Rib2VideoTextScene/", StringComparison.Ordinal)) continue;
            foreach (var root in scene.GetRootGameObjects())
            {
                var found = root.GetComponentInChildren<Rib2VideoTextScenePlayer>(true);
                if (found != null) return found;
            }
        }
        throw new InvalidOperationException("Open the separate generated demonstration scene first.");
    }

    private static Rib2DemoPatientArm DemoArm(Rib2VideoTextScenePlayer player)
    {
        var arm = player.GetComponent<Rib2DemoPatientArm>();
        if (arm == null) throw new InvalidOperationException("The demonstration patient arm is missing.");
        return arm;
    }

    private static void FocusDemo()
    {
        var player = DemoPlayer();
        Selection.activeGameObject = player.gameObject;
        var camera = player.GetComponentInChildren<Camera>(true);
        if (camera != null && SceneView.lastActiveSceneView != null)
            SceneView.lastActiveSceneView.LookAt(new Vector3(10.25f, 1.05f, -1.1f), camera.transform.rotation, 1.25f);
        SceneView.RepaintAll();
    }

    private static void Capture(float time, bool sequence = false)
    {
        var player = DemoPlayer();
        var arm = DemoArm(player);
        if (!player.Seek(time) || !arm.Evaluate()) throw new InvalidOperationException("Demonstration sample failed.");
        var camera = player.GetComponentInChildren<Camera>(true);
        if (camera == null) throw new InvalidOperationException("Demonstration camera is missing.");
        player.GetComponent<Rib2DemoSceneControls>()?.RefreshNow();
        foreach (var root in player.gameObject.scene.GetRootGameObjects())
            foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = .25f;
                foreach (var child in canvas.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 29;
            }
        Canvas.ForceUpdateCanvases();
        Directory.CreateDirectory(Work);
        int width = sequence ? 960 : 1440;
        int height = sequence ? 600 : 900;
        RenderTexture target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = camera.targetTexture;
        Texture2D image = null;
        var skins = player.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var snapshots = new List<GameObject>();
        var bakedMeshes = new List<Mesh>();
        var hiddenSkins = new List<SkinnedMeshRenderer>();
        Scene oldScene = SceneManager.GetActiveScene();
        try
        {
            SceneManager.SetActiveScene(player.gameObject.scene);
            // Camera.Render in Edit mode can show a stale GPU skin. Bake the evaluated
            // bones into temporary render-only meshes; never modify a shared mesh/material.
            foreach (var skin in skins)
            {
                if (!skin.enabled || !skin.gameObject.activeInHierarchy || skin.sharedMesh == null) continue;
                var mesh = new Mesh(); bakedMeshes.Add(mesh);
                skin.BakeMesh(mesh);
                var snapshot = new GameObject("Temporary capture skin", typeof(MeshFilter), typeof(MeshRenderer));
                snapshots.Add(snapshot);
                snapshot.hideFlags = HideFlags.HideAndDontSave;
                snapshot.transform.SetParent(skin.transform, false);
                snapshot.layer = skin.gameObject.layer;
                snapshot.GetComponent<MeshFilter>().sharedMesh = mesh;
                snapshot.GetComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
                hiddenSkins.Add(skin); skin.enabled = false;
            }
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            image.Apply();
            string path = sequence ? PreviewFolder + "/frame_" + previewIndex.ToString("D5") + ".png" :
                Work + "/scene_" + time.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + ".png";
            File.WriteAllBytes(path, image.EncodeToPNG());
            if (!sequence || previewIndex % 60 == 0)
                Debug.Log("[Rib2SceneAuthor] Captured actual separate-scene camera at " + time + "s: " + path);
        }
        finally
        {
            foreach (var skin in hiddenSkins) if (skin != null) skin.enabled = true;
            foreach (var snapshot in snapshots) if (snapshot != null) UnityEngine.Object.DestroyImmediate(snapshot);
            foreach (var mesh in bakedMeshes) if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            if (image != null) UnityEngine.Object.DestroyImmediate(image);
            RenderTexture.ReleaseTemporary(target);
            if (oldScene.IsValid() && oldScene.isLoaded) SceneManager.SetActiveScene(oldScene);
        }
        if (!sequence) FocusDemo();
    }

    private static void ProbeUi(bool repair)
    {
        var player = DemoPlayer();
        var report = new System.Text.StringBuilder();
        foreach (var root in player.gameObject.scene.GetRootGameObjects())
        foreach (var label in root.GetComponentsInChildren<TMPro.TMP_Text>(true))
        {
            if (label.name != "Title" && label.name != "Subtitle") continue;
            label.ForceMeshUpdate(true);
            report.AppendLine(label.name + " before: rect=" + label.rectTransform.rect + "; font=" + label.fontSize +
                "; preferredHeight=" + label.preferredHeight + "; chars=" + label.textInfo.characterCount +
                "; firstVisible=" + (label.textInfo.characterCount > 0 && label.textInfo.characterInfo[0].isVisible));
            if (!repair) continue;
            bool title = label.name == "Title";
            label.rectTransform.anchoredPosition = new Vector2(28f, title ? -10f : -73f);
            label.rectTransform.sizeDelta = new Vector2(title ? 1040f : 960f, title ? 60f : 40f);
            label.fontSize = title ? 30f : 20f;
            label.ForceMeshUpdate(true);
            report.AppendLine(label.name + " after: rect=" + label.rectTransform.rect + "; font=" + label.fontSize +
                "; preferredHeight=" + label.preferredHeight + "; chars=" + label.textInfo.characterCount +
                "; firstVisible=" + (label.textInfo.characterCount > 0 && label.textInfo.characterInfo[0].isVisible));
        }
        Canvas.ForceUpdateCanvases();
        File.WriteAllText(Work + (repair ? "/ui_repair.txt" : "/ui_probe.txt"), report.ToString());
        Debug.Log("[Rib2SceneAuthor] " + report);
    }

    private static void Sweep()
    {
        var player = DemoPlayer();
        var arm = DemoArm(player);
        var armData = new SerializedObject(arm);
        var upper = armData.FindProperty("upperArm").objectReferenceValue as Transform;
        var lower = armData.FindProperty("forearm").objectReferenceValue as Transform;
        var hand = armData.FindProperty("patientHand").objectReferenceValue as Transform;
        var playerData = new SerializedObject(player);
        var right = playerData.FindProperty("rightGuideHand").objectReferenceValue as HandTransformMapper;
        var frame = playerData.FindProperty("motionFrame").objectReferenceValue as Transform;
        if (upper == null || lower == null || hand == null || right == null || frame == null)
            throw new InvalidOperationException("The saved demonstration references are incomplete.");
        float a = Vector3.Distance(upper.position, lower.position);
        float b = Vector3.Distance(lower.position, hand.position);
        var report = new SweepResult { scene = player.gameObject.scene.path, frameDeterminant = frame.localToWorldMatrix.determinant };
        for (int sample = 0; sample < 248; sample++)
        {
            if (!player.Seek(sample / 30f) || !arm.Evaluate()) throw new InvalidOperationException("Sample failed at " + sample);
            report.samples++;
            report.maximumBoneLengthChange = Mathf.Max(report.maximumBoneLengthChange,
                Mathf.Abs(Vector3.Distance(upper.position, lower.position) - a), Mathf.Abs(Vector3.Distance(lower.position, hand.position) - b));
        }
        report.maximumReachCorrection = arm.MaximumReachCorrection;
        report.maximumWristError = arm.MaximumWristError;
        report.rightPalmDownDot = Vector3.Dot(right.Root.TransformDirection(Vector3.down), Vector3.down);
        if (report.maximumWristError > .005f || report.maximumBoneLengthChange > .001f || report.rightPalmDownDot < .5f)
            throw new InvalidOperationException("Demonstration sweep failed geometry validation: " + JsonUtility.ToJson(report));
        player.ShowStartPose(); arm.Evaluate();
        File.WriteAllText(Work + "/sweep_result.json", JsonUtility.ToJson(report, true));
        Debug.Log("[Rib2SceneAuthor] Validated " + report.samples + " editor samples; max wrist residual=" + report.maximumWristError +
            "; max bone length change=" + report.maximumBoneLengthChange + "; max reach correction=" + report.maximumReachCorrection + ". Play unverified.");
    }

    private static void SaveStart()
    {
        var player = DemoPlayer();
        player.ShowStartPose();
        if (!DemoArm(player).Evaluate()) throw new InvalidOperationException("Start pose failed.");
        player.GetComponent<Rib2DemoSceneControls>()?.RefreshNow();
        Scene scene = player.gameObject.scene;
        if (!scene.path.StartsWith("Assets/Demos/Rib2VideoTextScene/", StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing to save a scene outside this new demonstration.");
        if (!EditorSceneManager.SaveScene(scene, scene.path, false)) throw new IOException("Could not save the new demonstration start pose.");
        Debug.Log("[Rib2SceneAuthor] Saved only the new demonstration at its start pose: " + scene.path);
    }

    private static Vector3[] SkinVerticesAt(float time)
    {
        var player = DemoPlayer();
        if (!player.Seek(time) || !DemoArm(player).Evaluate()) throw new InvalidOperationException("Reload comparison pose failed.");
        var points = new List<Vector3>();
        foreach (var skin in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!skin.enabled || skin.sharedMesh == null) continue;
            var mesh = new Mesh();
            try
            {
                skin.BakeMesh(mesh);
                foreach (var vertex in mesh.vertices) points.Add(skin.transform.TransformPoint(vertex));
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }
        return points.ToArray();
    }

    private static void VerifyReload()
    {
        SaveStart();
        float[] times = { 0f, 2f, 4f, 6f, 8.233333f };
        var before = new Vector3[times.Length][];
        for (int i = 0; i < times.Length; i++) before[i] = SkinVerticesAt(times[i]);
        Scene demo = DemoPlayer().gameObject.scene;
        string path = demo.path;
        // Only this create-once demonstration is closed/reloaded. Other dirty scenes stay open.
        EditorSceneManager.CloseScene(demo, true);
        EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        float maximum = 0f;
        int count = 0;
        for (int i = 0; i < times.Length; i++)
        {
            Vector3[] after = SkinVerticesAt(times[i]);
            if (after.Length != before[i].Length) throw new InvalidOperationException("Reload changed the demonstration vertex count.");
            count += after.Length;
            for (int v = 0; v < after.Length; v++) maximum = Mathf.Max(maximum, Vector3.Distance(after[v], before[i][v]));
        }
        var player = DemoPlayer(); player.ShowStartPose(); DemoArm(player).Evaluate();
        player.GetComponent<Rib2DemoSceneControls>()?.RefreshNow();
        string report = "Compared " + count + " baked world-space vertices across " + times.Length +
            " poses before/after saving and reopening only the new scene. Maximum distance=" + maximum.ToString("R") + "m. Play unverified.";
        File.WriteAllText(Work + "/reload_verification.txt", report);
        Debug.Log("[Rib2SceneAuthor] " + report);
        FocusDemo();
        if (maximum > .001f) throw new InvalidOperationException("Reload changed the presented mesh by more than 1 mm: " + maximum);
    }

    private static void RecordOriginalReference()
    {
        var arm = DemoArm(DemoPlayer());
        Scene preview = EditorSceneManager.NewPreviewScene();
        GameObject clone = null;
        try
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(PatientGuid));
            if (source == null) throw new InvalidOperationException("Original patient model is missing.");
            clone = (GameObject)PrefabUtility.InstantiatePrefab(source, preview);
            var animator = clone.GetComponent<Animator>();
            arm.CaptureReferencePose(animator.GetBoneTransform(HumanBodyBones.RightUpperArm),
                animator.GetBoneTransform(HumanBodyBones.RightLowerArm), animator.GetBoneTransform(HumanBodyBones.RightHand));
            EditorUtility.SetDirty(arm);
            SaveStart();
            Debug.Log("[Rib2SceneAuthor] Recorded three original FBX reference rotations on the new scene arm only.");
        }
        finally
        {
            if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    [MenuItem("GuideChuna/시연/제2늑골 영상 기반/원본 모델 계측")]
    public static void Measure()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before measuring.");
        string path = AssetDatabase.GUIDToAssetPath(PatientGuid);
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (source == null) throw new InvalidOperationException("Original patient model not found.");
        Scene preview = EditorSceneManager.NewPreviewScene();
        var patient = (GameObject)PrefabUtility.InstantiatePrefab(source, preview);
        try
        {
            var animator = patient.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                throw new InvalidOperationException("Original patient does not have a valid Humanoid avatar.");
            animator.runtimeAnimatorController = null;
            animator.enabled = false;
            var bones = new List<BoneInfo>();
            foreach (HumanBodyBones id in new[] { HumanBodyBones.Hips, HumanBodyBones.Chest, HumanBodyBones.UpperChest,
                HumanBodyBones.Head, HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm,
                HumanBodyBones.RightHand, HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm,
                HumanBodyBones.LeftHand, HumanBodyBones.RightFoot, HumanBodyBones.LeftFoot })
            {
                var bone = animator.GetBoneTransform(id);
                if (bone == null) continue;
                bones.Add(new BoneInfo { name = id + ":" + bone.name, parent = bone.parent == null ? "" : bone.parent.name,
                    position = bone.position, rotation = bone.rotation, localPosition = bone.localPosition, localRotation = bone.localRotation,
                    scale = bone.lossyScale, determinant = bone.localToWorldMatrix.determinant });
            }
            var meshes = new List<MeshInfo>();
            foreach (var renderer in patient.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var materials = new List<string>(); var shaders = new List<string>();
                foreach (var material in renderer.sharedMaterials)
                {
                    materials.Add(material == null ? "MISSING" : AssetDatabase.GetAssetPath(material));
                    shaders.Add(material == null || material.shader == null ? "MISSING" : material.shader.name);
                }
                meshes.Add(new MeshInfo { name = renderer.name, bounds = renderer.bounds, materials = materials.ToArray(), shaders = shaders.ToArray() });
            }
            var contacts = new List<ContactInfo>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.path != "Assets/Scenes/TrainingScene.unity") continue;
                Transform trainingPatient = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (var candidate in root.GetComponentsInChildren<Transform>(true))
                        if (candidate.name == "c9" && candidate.GetComponent<Animator>() != null) trainingPatient = candidate;
                if (trainingPatient == null) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (var target in root.GetComponentsInChildren<Transform>(true))
                    {
                        string targetPath = AnimationUtility.CalculateTransformPath(target, null);
                        bool chest = targetPath.Contains("CranialRig_제2늑골_상방변위/right/") && target.name == "Grip_측두_손바닥";
                        bool wrist = targetPath.Contains("CC_Base_R_Hand/") && target.name == "Grip_후두_손바닥";
                        if (!chest && !wrist) continue;
                        contacts.Add(new ContactInfo { path = targetPath, world = target.position,
                            patientLocal = trainingPatient.InverseTransformPoint(target.position),
                            patientLocalRotation = Quaternion.Inverse(trainingPatient.rotation) * target.rotation });
                    }
            }
            var result = new Probe { patientAsset = path, humanoid = true, bones = bones.ToArray(), meshes = meshes.ToArray(), existingContacts = contacts.ToArray() };
            Directory.CreateDirectory(Work);
            File.WriteAllText(Work + "/model_probe.json", JsonUtility.ToJson(result, true));
            Debug.Log("[Rib2SceneAuthor] Measured original patient, " + bones.Count + " bones and " + contacts.Count + " existing reference contacts. Existing scenes not saved.");
        }
        finally { UnityEngine.Object.DestroyImmediate(patient); EditorSceneManager.ClosePreviewScene(preview); }
    }
}
