using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Builds an isolated, reproducible anatomical patient. Never saves an existing scene.</summary>
public static partial class AnatomicalPatientBuild
{
    internal const string PatientPath = "Assets/MedicXR CPX PhysicalExamination's PatientModel v20221111/Character/c9/c9.fbx";
    internal const string AnatomyPath = "Assets/Models/AnatomyV3/AnatomyV2_3.fbx";
    internal const string OutputFolder = "Assets/Generated/AnatomicalPatient";

    [Serializable] private class Measurements { public ModelMeasure patient; public ModelMeasure anatomy; }
    [Serializable] private class ModelMeasure { public string path; public BoneMeasure[] transforms; public MeshMeasure[] meshes; }
    [Serializable] private class BoneMeasure { public string name; public string path; public Vector3 position; public Quaternion rotation; public Vector3 scale; public float determinant; }
    [Serializable] private class MeshMeasure { public string name; public int vertices; public string[] bones; public Bounds bounds; public float rawToBakedMaximum; }

    [MenuItem("GuideChuna/시험/해부학 환자/원본 계측")]
    public static void Measure()
    {
        var result = new Measurements { patient = MeasureModel(PatientPath), anatomy = MeasureModel(AnatomyPath) };
        Directory.CreateDirectory("Temp/model-audit");
        File.WriteAllText("Temp/model-audit/unity_measurements.json", JsonUtility.ToJson(result, true));
        Debug.Log("[AnatomicalPatient] Measured imported model transforms and meshes: Temp/model-audit/unity_measurements.json");
    }

    private static ModelMeasure MeasureModel(string path)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null) throw new InvalidOperationException("Missing model: " + path);
        var scene = EditorSceneManager.NewPreviewScene();
        var root = UnityEngine.Object.Instantiate(asset);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        try
        {
            var transforms = new List<BoneMeasure>();
            foreach (var bone in root.GetComponentsInChildren<Transform>(true))
                transforms.Add(new BoneMeasure { name = bone.name, path = AnimationUtility.CalculateTransformPath(bone, root.transform), position = bone.position,
                    rotation = bone.rotation, scale = bone.lossyScale, determinant = bone.localToWorldMatrix.determinant });
            var meshes = new List<MeshMeasure>();
            foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var names = new string[r.bones.Length];
                for (int i = 0; i < names.Length; i++) names[i] = r.bones[i] == null ? "MISSING" : r.bones[i].name;
                var baked = new Mesh();
                float difference = 0f;
                try
                {
                    r.BakeMesh(baked);
                    Vector3[] raw = r.sharedMesh.vertices, shown = baked.vertices;
                    for (int i = 0; i < raw.Length; i++) difference = Mathf.Max(difference, Vector3.Distance(raw[i], shown[i]));
                }
                finally { UnityEngine.Object.DestroyImmediate(baked); }
                meshes.Add(new MeshMeasure { name = r.name, vertices = r.sharedMesh.vertexCount, bones = names, bounds = r.bounds, rawToBakedMaximum = difference });
            }
            return new ModelMeasure { path = path, transforms = transforms.ToArray(), meshes = meshes.ToArray() };
        }
        finally { UnityEngine.Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(scene); }
    }
}
