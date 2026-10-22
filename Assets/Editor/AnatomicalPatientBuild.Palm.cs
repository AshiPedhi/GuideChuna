using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static partial class AnatomicalPatientBuild
{
    [Serializable] private class PalmMeasurement
    {
        public string pose;
        public bool left;
        public Vector3 skinPalm, anatomyPalm, skinRadial, anatomyRadial;
        public float palmErrorDegrees, radialErrorDegrees;
    }
    [Serializable] private class PalmReport { public List<PalmMeasurement> measurements = new List<PalmMeasurement>(); }

    [MenuItem("GuideChuna/시험/해부학 환자/현재 복제본 골격 손 정렬 재생성")]
    private static void RefitLatestAnatomy()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before saving generated anatomy meshes.");
        var receipt = JsonUtility.FromJson<BuildReceipt>(File.ReadAllText(OutputFolder + "/LatestBuild.json"));
        if (!receipt.prefab.StartsWith(OutputFolder + "/", StringComparison.Ordinal)) throw new InvalidOperationException("Only generated patients may be refitted.");
        string folder = OutputFolder + "/PalmRefit_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        if (Directory.Exists(folder)) throw new IOException("Temporary output already exists: " + folder);
        Directory.CreateDirectory(folder + "/Meshes"); AssetDatabase.Refresh();
        GameObject contents = null, source = null;
        var targets = new List<Mesh>(); var replacements = new List<Mesh>(); var backups = new List<Mesh>();
        try
        {
            contents = PrefabUtility.LoadPrefabContents(receipt.prefab);
            var rig = contents.GetComponent<AnatomicalPatientRig>();
            rig.SetAutomaticEvaluation(false); rig.Evaluate(rig.RestPose);
            var oldRenderers = new Dictionary<string, SkinnedMeshRenderer>();
            foreach (var r in contents.transform.Find("AnatomyMeshes").GetComponentsInChildren<SkinnedMeshRenderer>(true)) oldRenderers.Add(r.name, r);
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(AnatomicalPatientRig);
            var limbs = (AnatomicalPatientRig.Limb[])type.GetField("limbs", flags).GetValue(rig);
            var bindings = (AnatomicalPatientRig.BoneBinding[])type.GetField("bodyBindings", flags).GetValue(rig);
            var mirrors = new Dictionary<Transform, Transform>();
            foreach (var b in bindings) mirrors.Add(b.source, b.output);
            source = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(AnatomyPath));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(source, contents.scene);
            Transform temporary = NewBone("PalmRefitPreview", contents.transform, Vector3.zero);
            var built = AnatomicalMeshBuilder.Build(source, rig.RigSpace, temporary, contents.GetComponentInChildren<Animator>(true), limbs, mirrors, folder + "/Meshes");
            foreach (var palm in MeasurePalmGeometry(contents, source, "RefitNew", temporary.gameObject))
                if (palm.palmErrorDegrees > 1f || palm.radialErrorDegrees > 1f)
                    throw new InvalidOperationException("The newly fitted hand failed alignment before any saved mesh was changed.");
            var newRenderers = temporary.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (built.meshes != oldRenderers.Count || newRenderers.Length != oldRenderers.Count) throw new InvalidOperationException("Anatomy renderer set changed; use a fresh generated prefab.");
            foreach (var renderer in newRenderers)
            {
                if (!oldRenderers.TryGetValue(renderer.name, out var previous)) throw new InvalidOperationException("Unexpected refit renderer: " + renderer.name);
                Mesh oldMesh = previous.sharedMesh, newMesh = renderer.sharedMesh;
                string path = AssetDatabase.GetAssetPath(oldMesh);
                if (!path.StartsWith(Path.GetDirectoryName(receipt.prefab).Replace('\\', '/') + "/Meshes/", StringComparison.Ordinal))
                    throw new InvalidOperationException("Refusing to change a source or unrelated mesh: " + path);
                if (oldMesh.vertexCount != newMesh.vertexCount || previous.bones.Length != renderer.bones.Length)
                    throw new InvalidOperationException("Topology/palette changed: " + renderer.name);
                for (int i = 0; i < previous.bones.Length; i++)
                    if (previous.bones[i].name != renderer.bones[i].name) throw new InvalidOperationException("Bone palette order changed: " + renderer.name);
                Matrix4x4[] oldBind = oldMesh.bindposes, newBind = newMesh.bindposes;
                if (oldBind.Length != newBind.Length) throw new InvalidOperationException("Bind pose count changed: " + renderer.name);
                for (int i = 0; i < oldBind.Length; i++)
                    for (int j = 0; j < 16; j++) if (Mathf.Abs(oldBind[i][j] - newBind[i][j]) > .00001f)
                        throw new InvalidOperationException("Bone rest frame changed: " + renderer.name);
                var backup = UnityEngine.Object.Instantiate(oldMesh); backup.name = oldMesh.name;
                targets.Add(oldMesh); replacements.Add(newMesh); backups.Add(backup);
            }
            try
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    CopyAnatomyMeshData(replacements[i], targets[i]);
                    AssetDatabase.SaveAssetIfDirty(targets[i]);
                }
            }
            catch
            {
                for (int i = 0; i < targets.Count; i++)
                { CopyAnatomyMeshData(backups[i], targets[i]); AssetDatabase.SaveAssetIfDirty(targets[i]); }
                throw;
            }
            Debug.Log("[AnatomicalPalm] Refit " + targets.Count + " generated anatomy meshes after validating identical topology, bone palette and rest frames. Original c9 skin/FBX/scenes were not saved or modified.");
        }
        finally
        {
            foreach (var mesh in backups) UnityEngine.Object.DestroyImmediate(mesh);
            if (source != null) UnityEngine.Object.DestroyImmediate(source);
            if (contents != null) PrefabUtility.UnloadPrefabContents(contents);
            if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
            SceneView.RepaintAll();
        }
    }

    private static void CopyAnatomyMeshData(Mesh source, Mesh target)
    {
        EditorUtility.CopySerialized(source, target);
        // Explicit setters notify native Mesh buffers. CopySerialized alone left
        // the measured palm at its old 94.925-degree error even after reimport.
        // Rollback uses this same path so saved and native geometry stay together.
        target.vertices = source.vertices;
        target.normals = source.normals;
        target.tangents = source.tangents;
        target.boneWeights = source.boneWeights;
        target.bindposes = source.bindposes;
        target.bounds = source.bounds;
        target.UploadMeshData(false);
        EditorUtility.SetDirty(target);
    }

    [MenuItem("GuideChuna/시험/해부학 환자/피부 골격 손바닥 정렬 계측")]
    private static void MeasurePalmAlignment()
    {
        var receipt = JsonUtility.FromJson<BuildReceipt>(File.ReadAllText(OutputFolder + "/LatestBuild.json"));
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject copy = null, source = null;
        try
        {
            copy = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(receipt.prefab));
            source = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(AnatomyPath));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(copy, scene);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(source, scene);
            var lab = copy.GetComponent<AnatomicalPatientLab>();
            var report = new PalmReport();
            foreach (var sample in new[] { AnatomicalPatientLab.Sample.Rest, AnatomicalPatientLab.Sample.Elbow100, AnatomicalPatientLab.Sample.Forearm80 })
            {
                lab.SetSample(sample);
                report.measurements.AddRange(MeasurePalmGeometry(copy, source, sample.ToString()));
            }
            var neutral = lab.Rig.CurrentPose;
            neutral.leftArm.forearmRotation = neutral.rightArm.forearmRotation = 0f;
            lab.Rig.Evaluate(neutral);
            report.measurements.AddRange(MeasurePalmGeometry(copy, source, "Elbow90_Roll0"));
            Directory.CreateDirectory("Temp/model-audit");
            File.WriteAllText("Temp/model-audit/palm-alignment.json", JsonUtility.ToJson(report, true));
        }
        finally
        {
            if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
            if (source != null) UnityEngine.Object.DestroyImmediate(source);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static List<PalmMeasurement> MeasurePalmGeometry(GameObject root, GameObject source, string pose, GameObject anatomyOverride = null)
    {
        var rig = root.GetComponent<AnatomicalPatientRig>();
        var animator = root.GetComponentInChildren<Animator>(true);
        var data = new SerializedObject(rig);
        var result = new List<PalmMeasurement>();
        for (int side = 0; side < 2; side++)
        {
            bool left = side == 0; string prefix = left ? "L" : "R";
            Transform wrist = OutputFor(data, animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand));
            Transform index = OutputFor(data, animator.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal));
            Transform middle = OutputFor(data, animator.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal));
            Transform little = OutputFor(data, animator.GetBoneTransform(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal));
            Vector3 skinRadial = (index.localPosition - little.localPosition).normalized;
            Vector3 skinLong = (middle.localPosition - wrist.localPosition).normalized;
            Vector3 skinPalm = PalmNormal(left, skinRadial, skinLong);
            var sourceRenderer = NamedRenderer(source, prefix + "_metacarpal_bones");
            var targetRenderer = NamedRenderer(anatomyOverride != null ? anatomyOverride : root, prefix + "_metacarpal_bones");
            Vector3[] sourcePoints = BakeWorld(sourceRenderer), targetPoints = BakeWorld(targetRenderer);
            if (sourcePoints.Length != targetPoints.Length) throw new InvalidOperationException("Anatomy hand topology changed.");
            int iIndex = Nearest(sourcePoints, NamedTransform(source, "Bip001 " + prefix + " Finger1").position);
            int iMiddle = Nearest(sourcePoints, NamedTransform(source, "Bip001 " + prefix + " Finger2").position);
            int iLittle = Nearest(sourcePoints, NamedTransform(source, "Bip001 " + prefix + " Finger4").position);
            if (pose == "Rest")
            {
                Vector3[] raw = targetRenderer.sharedMesh.vertices;
                for (int j = 0; j < raw.Length; j++) raw[j] = targetRenderer.transform.TransformPoint(raw[j]);
                float rawBakeError = 0f;
                for (int j = 0; j < raw.Length; j++) rawBakeError = Mathf.Max(rawBakeError, Vector3.Distance(raw[j], targetPoints[j]));
                Vector3 restWrist = rig.RigSpace.TransformPoint(wrist.localPosition);
                Vector3 rawPalm = PalmNormal(left, raw[iIndex] - raw[iLittle], raw[iMiddle] - restWrist);
                Debug.Log("[AnatomicalPalmBuffer] " + prefix + " raw/bake vertex max=" + rawBakeError +
                    "; raw palm=" + rawPalm.ToString("F6") + "; skin palm=" + skinPalm.ToString("F6") +
                    "; asset=" + AssetDatabase.GetAssetPath(targetRenderer.sharedMesh));
            }
            Vector3 anatomicalWrist = rig.RigSpace.TransformPoint(((Transform)data.FindProperty("limbs").GetArrayElementAtIndex(side).FindPropertyRelative("hand").objectReferenceValue).localPosition);
            Vector3 anatomyRadial = rig.RigSpace.InverseTransformDirection(targetPoints[iIndex] - targetPoints[iLittle]).normalized;
            Vector3 anatomyLong = rig.RigSpace.InverseTransformDirection(targetPoints[iMiddle] - anatomicalWrist).normalized;
            Vector3 anatomyPalm = PalmNormal(left, anatomyRadial, anatomyLong);
            var measurement = new PalmMeasurement { pose = pose, left = left, skinPalm = skinPalm, anatomyPalm = anatomyPalm,
                skinRadial = skinRadial, anatomyRadial = anatomyRadial,
                palmErrorDegrees = Vector3.Angle(skinPalm, anatomyPalm), radialErrorDegrees = Vector3.Angle(skinRadial, anatomyRadial) };
            result.Add(measurement);
            Debug.Log("[AnatomicalPalm] " + pose + " " + prefix + "; skin palm=" + skinPalm.ToString("F4") +
                "; actual baked anatomy palm=" + anatomyPalm.ToString("F4") + "; palm angle=" + measurement.palmErrorDegrees +
                "; radial angle=" + measurement.radialErrorDegrees);
        }
        return result;
    }

    private static Vector3 PalmNormal(bool left, Vector3 radial, Vector3 longitudinal)
    { return (left ? Vector3.Cross(radial, longitudinal) : Vector3.Cross(longitudinal, radial)).normalized; }
    private static Transform OutputFor(SerializedObject data, Transform source)
    {
        var bindings = data.FindProperty("bodyBindings");
        for (int i = 0; i < bindings.arraySize; i++)
        {
            var binding = bindings.GetArrayElementAtIndex(i);
            if (binding.FindPropertyRelative("source").objectReferenceValue == source)
                return (Transform)binding.FindPropertyRelative("output").objectReferenceValue;
        }
        throw new InvalidOperationException("Missing c9 finger output binding: " + source);
    }
    private static Transform NamedTransform(GameObject root, string name)
    {
        Transform found = null;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) { if (found != null) throw new InvalidOperationException("Ambiguous transform: " + name); found = t; }
        if (found == null) throw new InvalidOperationException("Missing transform: " + name);
        return found;
    }
    private static SkinnedMeshRenderer NamedRenderer(GameObject root, string name)
    {
        var t = NamedTransform(root, name);
        var renderer = t.GetComponent<SkinnedMeshRenderer>();
        if (renderer == null) throw new InvalidOperationException("Missing renderer: " + name);
        return renderer;
    }
    private static int Nearest(Vector3[] points, Vector3 position)
    {
        int index = 0; float distance = float.PositiveInfinity;
        for (int i = 0; i < points.Length; i++)
        { float d = (points[i] - position).sqrMagnitude; if (d < distance) { distance = d; index = i; } }
        return index;
    }
}
