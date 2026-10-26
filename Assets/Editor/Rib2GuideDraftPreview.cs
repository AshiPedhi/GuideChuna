// Separate demonstration capture only. Never edits a scene, resource CSV, or scenario condition.
// Request: <project>/Temp/rib2-guide-draft-request.json. Response: same folder,
// rib2-guide-draft-result.json; a receipt is also written inside the new output folder.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Oculus.Interaction;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static HandPoseDataLoader;

[InitializeOnLoad]
public static class Rib2GuideDraftPreview
{
    [Serializable]
    private sealed class Request
    {
        public string csvPath = "";
        public string outputDir = "";
        public int startFrame = 0;
        public int endFrame = -1;
        public int frameStep = 1;
        public int width = 960;
        public int height = 720;
        public float fps = 30f;
        public float cameraYaw = 20f;
        public float cameraPitch = 15f;
        public float cameraRoll = 0f;
        public float framingMargin = 1.15f;
    }

    [Serializable]
    private sealed class Receipt
    {
        public bool success;
        public string error = "";
        public string csvPath;
        public string outputDir;
        public string coordinateSpace = "Input CSV root coordinates, unchanged; no patient registration. See the draft's provenance for recorded versus authored input.";
        public string poseApplication = "HandTransformMapper.SetRootWorldPose / SetJointLocalPose; original OpenXR 26-joint prefab mapping.";
        public string visualNote = "Original Meta hand mesh; private opaque preview materials, left cyan / right orange.";
        public string leftPrefab;
        public string rightPrefab;
        public int leftJointCount;
        public int rightJointCount;
        public int startFrame;
        public int endFrame;
        public int frameStep;
        public int framesWritten;
        public int inputFrameCount;
        public float fps;
        public float cameraYaw;
        public float cameraPitch;
        public float cameraRoll;
        public Vector3 cameraPosition;
        public Vector3 boundsCenter;
        public Vector3 boundsSize;
        public float orthographicSize;
        public List<string> meshes = new List<string>();
        public List<string> jointNames = new List<string>();
    }

    private sealed class Snapshot
    {
        public SkinnedMeshRenderer source;
        public Mesh mesh;
        public GameObject holder;
    }

    private sealed class Capture : IDisposable
    {
        public Request request;
        public Receipt receipt;
        public List<PoseFrame> frames;
        public int index;
        private PreviewRenderUtility preview;
        private HandTransformMapper left;
        private HandTransformMapper right;
        private readonly List<Snapshot> snapshots = new List<Snapshot>();
        private readonly List<Material> materials = new List<Material>();

        public void Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Capture is only allowed outside Play mode.");
            if (string.IsNullOrWhiteSpace(request.csvPath) || !File.Exists(request.csvPath))
                throw new FileNotFoundException("Draft CSV not found.", request.csvPath);
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string output = Path.GetFullPath(request.outputDir);
            string assets = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            if (!output.StartsWith(project + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                output.StartsWith(assets, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Output must be inside this project and outside Assets.");
            if (Directory.Exists(output) && Directory.GetFileSystemEntries(output).Length > 0)
                throw new InvalidOperationException("Output folder is not empty; select a fresh demonstration folder.");
            request.width = Mathf.Clamp(request.width, 128, 2048);
            request.height = Mathf.Clamp(request.height, 128, 2048);
            request.frameStep = Mathf.Max(1, request.frameStep);
            if (!Finite(request.fps) || request.fps <= 0f) throw new InvalidOperationException("Invalid fps.");
            if (!Finite(request.cameraYaw) || !Finite(request.cameraPitch) || !Finite(request.cameraRoll))
                throw new InvalidOperationException("Invalid camera orientation.");
            if (!Finite(request.framingMargin) || request.framingMargin < 1f)
                throw new InvalidOperationException("Framing margin must be at least one.");

            // The existing loader accepts an absolute path through Path.Combine.
            LoadResult loaded = new HandPoseDataLoader().LoadFromFile(Path.GetFullPath(request.csvPath));
            if (!loaded.success || loaded.frames.Count == 0)
                throw new InvalidOperationException("Draft CSV load failed: " + loaded.errorMessage);
            frames = loaded.frames;
            if (request.startFrame < 0 || request.startFrame >= frames.Count)
                throw new InvalidOperationException("startFrame is outside the parsed frame range.");
            if (request.endFrame < 0) request.endFrame = frames.Count - 1;
            if (request.endFrame < request.startFrame || request.endFrame >= frames.Count)
                throw new InvalidOperationException("endFrame is outside the parsed frame range.");
            Directory.CreateDirectory(output);
            receipt.csvPath = Path.GetFullPath(request.csvPath);
            receipt.outputDir = output;
            receipt.inputFrameCount = frames.Count;
            receipt.startFrame = request.startFrame;
            receipt.endFrame = request.endFrame;
            receipt.frameStep = request.frameStep;
            receipt.fps = request.fps;
            receipt.cameraYaw = request.cameraYaw;
            receipt.cameraPitch = request.cameraPitch;
            receipt.cameraRoll = request.cameraRoll;
            index = request.startFrame;

            preview = new PreviewRenderUtility();
            left = CreateHand("70d02a90551f23042a882cdceeaf8e3a", true, new Color(.12f, .77f, .94f));
            right = CreateHand("34a22dd67c1e5344591237fdd61e78ec", false, new Color(1f, .59f, .25f));
            ConfigureCamera();
            Debug.Log("[Rib2GuideDraft] Prepared " + frames.Count + " frames; roots and 26 local joints remain in input CSV coordinates; " +
                      "camera=" + receipt.cameraPosition + ", bounds=" + receipt.boundsSize + ", output=" + output);
        }

        private HandTransformMapper CreateHand(string guid, bool isLeft, Color color)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new InvalidOperationException("Original hand prefab not found: " + guid);
            // Instantiate directly in a private preview scene, never in the active training scene.
            var clone = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview.camera.gameObject.scene);
            if (clone == null) throw new InvalidOperationException("Cannot clone " + path);
            clone.hideFlags = HideFlags.HideAndDontSave;
            preview.AddSingleGO(clone);
            foreach (var behaviour in clone.GetComponentsInChildren<Behaviour>(true)) behaviour.enabled = false;
            foreach (var collider in clone.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            var visual = clone.GetComponentInChildren<HandVisual>(true);
            if (visual == null) throw new InvalidOperationException("Missing HandVisual: " + path);
            var visualData = new SerializedObject(visual);
            var root = visualData.FindProperty("_openXRRoot").objectReferenceValue as Transform;
            var legacyRoot = visualData.FindProperty("_root").objectReferenceValue as Transform;
            var jointArray = visualData.FindProperty("_openXRJointTransforms");
            if (root == null || jointArray.arraySize != 26)
                throw new InvalidOperationException("Original prefab does not contain the expected OpenXR 26-joint rig: " + path);
            if (legacyRoot != null) legacyRoot.gameObject.SetActive(false);
            root.gameObject.SetActive(true);
            var joints = new List<Transform>(26);
            for (int i = 0; i < jointArray.arraySize; i++)
            {
                var joint = jointArray.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                if (joint == null) throw new InvalidOperationException("Missing joint " + i + " in " + path);
                joints.Add(joint);
                receipt.jointNames.Add((isLeft ? "Left" : "Right") + ":" + i + ":" + joint.name);
            }
            var mapper = clone.AddComponent<HandTransformMapper>();
            mapper.enabled = false;
            mapper.SetHandType(isLeft);
            var mapperData = new SerializedObject(mapper);
            mapperData.FindProperty("copyJointsOnStart").boolValue = false;
            mapperData.FindProperty("rootTransform").objectReferenceValue = root;
            mapperData.ApplyModifiedPropertiesWithoutUndo();
            mapper.SetJoints(joints);
            Shader shader = Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("Built-in Standard shader is unavailable.");
            var material = new Material(shader) { color = color, hideFlags = HideFlags.HideAndDontSave };
            material.SetFloat("_Glossiness", .24f);
            materials.Add(material);
            int count = 0;
            foreach (var source in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (source.sharedMesh == null) continue;
                var snapshot = new Snapshot { source = source };
                snapshots.Add(snapshot);
                snapshot.mesh = new Mesh { name = "Rib2 private CPU skin", hideFlags = HideFlags.HideAndDontSave };
                snapshot.mesh.MarkDynamic();
                snapshot.holder = new GameObject("Rib2 private mesh snapshot") { hideFlags = HideFlags.HideAndDontSave };
                snapshot.holder.transform.SetParent(source.transform, false);
                snapshot.holder.AddComponent<MeshFilter>().sharedMesh = snapshot.mesh;
                snapshot.holder.AddComponent<MeshRenderer>().sharedMaterial = material;
                source.enabled = false;
                receipt.meshes.Add((isLeft ? "Left:" : "Right:") + source.sharedMesh.name + ":" + source.sharedMesh.vertexCount);
                count++;
            }
            if (count == 0) throw new InvalidOperationException("No skinned mesh under OpenXR root: " + path);
            if (isLeft) { receipt.leftPrefab = path; receipt.leftJointCount = joints.Count; }
            else { receipt.rightPrefab = path; receipt.rightJointCount = joints.Count; }
            return mapper;
        }

        private void Apply(PoseFrame frame)
        {
            ApplyHand(left, frame.leftRootPosition, frame.leftRootRotation, frame.leftLocalPoses);
            ApplyHand(right, frame.rightRootPosition, frame.rightRootRotation, frame.rightLocalPoses);
        }

        private static void ApplyHand(HandTransformMapper mapper, Vector3 position, Quaternion rotation,
            Dictionary<int, PoseData> joints)
        {
            if (!Finite(position) || !Finite(rotation) || joints.Count != 26)
                throw new InvalidOperationException("A frame has missing/nonfinite root or local hand data.");
            mapper.SetRootWorldPose(position, rotation);
            foreach (var pair in joints)
            {
                if (pair.Key < 0 || pair.Key >= 26 || !Finite(pair.Value.position) || !Finite(pair.Value.rotation))
                    throw new InvalidOperationException("Invalid local joint pose at " + pair.Key);
                mapper.SetJointLocalPose(pair.Key, pair.Value.position, pair.Value.rotation);
            }
        }

        private void ConfigureCamera()
        {
            // Dependence: all three position components, all rotations and all local joints
            // affect geometry. No flattening, reflection, trajectory normalization or patient fit.
            Bounds bounds = new Bounds();
            bool initialized = false;
            for (int i = request.startFrame; i <= request.endFrame; i += request.frameStep)
            {
                Apply(frames[i]);
                foreach (var snapshot in snapshots)
                {
                    snapshot.source.BakeMesh(snapshot.mesh);
                    Bounds local = snapshot.mesh.bounds;
                    for (int corner = 0; corner < 8; corner++)
                    {
                        Vector3 p = local.center + Vector3.Scale(local.extents,
                            new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                        p = snapshot.source.transform.TransformPoint(p);
                        if (!Finite(p)) throw new InvalidOperationException("Nonfinite mesh geometry.");
                        if (!initialized) { bounds = new Bounds(p, Vector3.zero); initialized = true; }
                        else bounds.Encapsulate(p);
                    }
                }
            }
            if (!initialized) throw new InvalidOperationException("Empty preview geometry.");
            var camera = preview.camera;
            camera.enabled = false;
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.055f, .075f, .105f);
            camera.orthographic = true;
            camera.aspect = (float)request.width / request.height;
            camera.nearClipPlane = .01f;
            camera.farClipPlane = 100f;
            Quaternion view = Quaternion.Euler(request.cameraPitch, request.cameraYaw, request.cameraRoll);
            float halfHeight = .05f;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 offset = Vector3.Scale(bounds.extents,
                    new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                Vector3 projected = Quaternion.Inverse(view) * offset;
                // Abs only measures framing extents; it does not change the applied hand poses.
                halfHeight = Mathf.Max(halfHeight, Mathf.Abs(projected.y), Mathf.Abs(projected.x) / camera.aspect);
            }
            camera.orthographicSize = halfHeight * request.framingMargin;
            camera.transform.rotation = view;
            camera.transform.position = bounds.center - camera.transform.forward * Mathf.Max(2f, bounds.size.magnitude * 2f);
            preview.lights[0].intensity = 1.3f;
            preview.lights[0].transform.rotation = Quaternion.Euler(35f, -35f, 0f);
            preview.lights[1].intensity = .9f;
            preview.lights[1].transform.rotation = Quaternion.Euler(25f, 145f, 0f);
            preview.ambientColor = new Color(.3f, .3f, .3f);
            receipt.boundsCenter = bounds.center;
            receipt.boundsSize = bounds.size;
            receipt.cameraPosition = camera.transform.position;
            receipt.orthographicSize = camera.orthographicSize;
        }

        public bool RenderNext()
        {
            if (index > request.endFrame) return false;
            Apply(frames[index]);
            foreach (var snapshot in snapshots) snapshot.source.BakeMesh(snapshot.mesh);
            Texture2D image = null;
            try
            {
                preview.BeginStaticPreview(new Rect(0f, 0f, request.width, request.height));
                preview.Render();
                image = preview.EndStaticPreview();
                string name = "hand_" + receipt.framesWritten.ToString("D5") + ".png";
                File.WriteAllBytes(Path.Combine(receipt.outputDir, name), image.EncodeToPNG());
            }
            finally { if (image != null) UnityEngine.Object.DestroyImmediate(image); }
            receipt.framesWritten++;
            index += request.frameStep;
            if (receipt.framesWritten == 1 || receipt.framesWritten % 60 == 0)
                Debug.Log("[Rib2GuideDraft] Rendered " + receipt.framesWritten + " frames; source frame=" + (index - request.frameStep));
            return index <= request.endFrame;
        }

        public void Dispose()
        {
            foreach (var snapshot in snapshots)
            {
                if (snapshot.holder != null) UnityEngine.Object.DestroyImmediate(snapshot.holder);
                if (snapshot.mesh != null) UnityEngine.Object.DestroyImmediate(snapshot.mesh);
            }
            foreach (var material in materials) if (material != null) UnityEngine.Object.DestroyImmediate(material);
            if (preview != null) preview.Cleanup();
        }
    }

    private static Capture current;
    private static double nextPoll;
    private static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp"));
    private static string RequestPath => Path.Combine(Root, "rib2-guide-draft-request.json");
    private static string ResultPath => Path.Combine(Root, "rib2-guide-draft-result.json");

    static Rib2GuideDraftPreview()
    {
        EditorApplication.update += Tick;
        AssemblyReloadEvents.beforeAssemblyReload += Cancel;
        EditorApplication.quitting += Cancel;
    }

    private static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        try
        {
            if (current != null)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new InvalidOperationException("Play mode transition interrupted the isolated capture.");
                // One frame per Editor update keeps the editor responsive while saving PNGs.
                if (!current.RenderNext()) Finish(true, "");
                return;
            }
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + .25;
            if (!File.Exists(RequestPath)) return;
            string json = File.ReadAllText(RequestPath, Encoding.UTF8);
            File.Delete(RequestPath);
            var request = JsonUtility.FromJson<Request>(json);
            if (request == null) throw new InvalidOperationException("Request JSON is empty.");
            current = new Capture { request = request, receipt = new Receipt() };
            current.Prepare();
        }
        catch (Exception e) { Finish(false, e.ToString()); }
    }

    private static void Finish(bool success, string error)
    {
        Receipt receipt = current != null ? current.receipt : new Receipt();
        receipt.success = success;
        receipt.error = error;
        try
        {
            Directory.CreateDirectory(Root);
            string json = JsonUtility.ToJson(receipt, true);
            File.WriteAllText(ResultPath, json, new UTF8Encoding(false));
            if (!string.IsNullOrEmpty(receipt.outputDir))
                File.WriteAllText(Path.Combine(receipt.outputDir, "capture-receipt.json"), json, new UTF8Encoding(false));
            if (success) Debug.Log("[Rib2GuideDraft] Complete: " + receipt.framesWritten + " frames; " + receipt.outputDir);
            else Debug.LogError("[Rib2GuideDraft] Capture failed: " + error);
        }
        finally
        {
            if (current != null) current.Dispose();
            current = null;
        }
    }

    private static void Cancel()
    {
        if (current != null) Finish(false, "Editor quit or assembly reload interrupted the preview.");
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    private static bool Finite(Quaternion value) => Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w) &&
        value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w > .00001f;
}
