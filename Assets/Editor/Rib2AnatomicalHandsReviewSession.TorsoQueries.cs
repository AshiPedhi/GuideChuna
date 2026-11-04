using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Private preview ray queries restricted to positively identified torso skin influences.</summary>
public sealed partial class Rib2AnatomicalHandsReviewSession
{
    private const float TorsoVertexWeightMinimum = .6f;
    private sealed class TorsoQuery
    {
        public SkinnedMeshRenderer source;
        public GameObject holder;
        public Mesh baked, filtered;
        public MeshCollider collider;
        public readonly List<Vector3> vertices = new List<Vector3>();
        public BoneWeight[] weights;
        public string[] sourceBoneNames;
        public float[] torsoWeights;
        public int[] fullIndices, filteredIndices, originalTriangleOrdinals;
        public bool topologyAssigned;
    }
    private readonly List<TorsoQuery> torsoOnlyQueries = new List<TorsoQuery>();
    private readonly List<string> torsoAllowedSourceNames = new List<string>();

    [Serializable]
    public sealed class TorsoHitEvidence
    {
        public string renderer, queryPolicy;
        public Vector3 pointWorld, normalWorld;
        public int queryTriangleIndex, originalTriangleIndex;
        public float minimumTorsoVertexWeight, meanTorsoVertexWeight;
        public string[] sourceBoneNames;
        public float[] sourceBoneMeanWeights;
    }

    private void InitializeTorsoQueries()
    {
        if (torsoOnlyQueries.Count != 0) return;
        if (!EditorSceneManager.IsPreviewScene(rig.gameObject.scene)) throw new InvalidOperationException("Torso queries require the private preview scene.");
        var animator = rig.GetComponentInChildren<Animator>(true);
        if (animator == null || !animator.isHuman) throw new InvalidOperationException("Torso source-bone identification requires the original Humanoid animator.");
        var torsoSources = new HashSet<Transform>();
        foreach (HumanBodyBones id in new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest,
            HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder })
        {
            Transform bone = animator.GetBoneTransform(id);
            if (bone != null) { torsoSources.Add(bone); torsoAllowedSourceNames.Add(id + ":" + bone.name); }
        }
        var allowedOutputs = new HashSet<Transform>(); var outputSourceNames = new Dictionary<Transform, string>();
        SerializedProperty bindings = new SerializedObject(rig).FindProperty("bodyBindings");
        for (int i = 0; i < bindings.arraySize; i++)
        {
            var binding = bindings.GetArrayElementAtIndex(i);
            var source = binding.FindPropertyRelative("source").objectReferenceValue as Transform;
            var output = binding.FindPropertyRelative("output").objectReferenceValue as Transform;
            if (source == null || output == null) continue;
            outputSourceNames[output] = source.name;
            // Actual C9 chest skin also uses the rib-twist bones (confirmed by
            // torso_query_audit.json); they are torso, not arm influences.
            bool rib = source.name.IndexOf("RibsTwist", StringComparison.OrdinalIgnoreCase) >= 0;
            if (torsoSources.Contains(source) || rib)
            {
                allowedOutputs.Add(output);
                if (rib) torsoAllowedSourceNames.Add("RibTwist:" + source.name);
            }
        }
        if (allowedOutputs.Count == 0) throw new InvalidOperationException("No explicit Hips/Spine/Chest/UpperChest output bindings found for torso query.");
        foreach (Snapshot snapshot in querySkins)
        {
            var skin = snapshot.source;
            var query = new TorsoQuery { source = skin, weights = skin.sharedMesh.boneWeights, fullIndices = skin.sharedMesh.triangles };
            torsoOnlyQueries.Add(query); // Owned resources remain disposable after a partial failure.
            if (query.weights.Length != skin.sharedMesh.vertexCount) throw new InvalidOperationException("Torso query requires the actual four-weight anatomical skin layout: " + skin.name);
            Transform[] bones = skin.bones;
            var allowed = new bool[bones.Length]; query.sourceBoneNames = new string[bones.Length];
            for (int b = 0; b < bones.Length; b++)
            {
                allowed[b] = allowedOutputs.Contains(bones[b]);
                query.sourceBoneNames[b] = outputSourceNames.TryGetValue(bones[b], out string name) ? name : "<unmapped-output>:" + bones[b].name;
            }
            query.torsoWeights = new float[query.weights.Length];
            for (int v = 0; v < query.weights.Length; v++)
            {
                BoneWeight w = query.weights[v];
                query.torsoWeights[v] = (allowed[w.boneIndex0] ? w.weight0 : 0f) + (allowed[w.boneIndex1] ? w.weight1 : 0f)
                    + (allowed[w.boneIndex2] ? w.weight2 : 0f) + (allowed[w.boneIndex3] ? w.weight3 : 0f);
            }
            var kept = new List<int>(); var ordinals = new List<int>();
            for (int i = 0; i + 2 < query.fullIndices.Length; i += 3)
            {
                int a = query.fullIndices[i], b = query.fullIndices[i + 1], c = query.fullIndices[i + 2];
                if (query.torsoWeights[a] < TorsoVertexWeightMinimum || query.torsoWeights[b] < TorsoVertexWeightMinimum || query.torsoWeights[c] < TorsoVertexWeightMinimum) continue;
                kept.Add(a); kept.Add(b); kept.Add(c); ordinals.Add(i / 3);
            }
            query.filteredIndices = kept.ToArray(); query.originalTriangleOrdinals = ordinals.ToArray();
            query.baked = new Mesh { name = "Private torso source bake", hideFlags = HideFlags.HideAndDontSave };
            query.filtered = new Mesh { name = "Private positively identified torso query", hideFlags = HideFlags.HideAndDontSave, indexFormat = skin.sharedMesh.indexFormat };
            query.holder = new GameObject("Private torso-only skin query") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(query.holder, skin.gameObject.scene); query.holder.transform.SetParent(skin.transform, false);
            query.collider = query.holder.AddComponent<MeshCollider>(); query.collider.convex = false;
            Debug.Log("[Rib2TorsoQuery] " + skin.name + " torso triangles=" + ordinals.Count + "/" + query.fullIndices.Length / 3 + "; each vertex torso source influence >=" + TorsoVertexWeightMinimum);
        }
    }

    /// <summary>Call after the same native Evaluate used for the patient preview.</summary>
    private void BakeTorsoQueries()
    {
        InitializeTorsoQueries();
        foreach (TorsoQuery query in torsoOnlyQueries)
        {
            query.source.BakeMesh(query.baked); query.baked.GetVertices(query.vertices);
            query.collider.sharedMesh = null; query.filtered.SetVertices(query.vertices);
            if (!query.topologyAssigned) { query.filtered.SetTriangles(query.filteredIndices, 0, false); query.topologyAssigned = true; }
            query.filtered.bounds = query.baked.bounds;
            if (query.filteredIndices.Length != 0) query.collider.sharedMesh = query.filtered;
        }
    }

    /// <summary>Same source-camera ray construction; only positively identified torso triangles can hit.</summary>
    private RaycastHit TorsoChestHit(Vector2 pixel)
    {
        if (!TryTorsoChestHit(pixel, out RaycastHit result))
            throw new InvalidOperationException("Observed support region misses positively identified torso skin; no arm/shoulder fallback is used.");
        return result;
    }

    private bool TryTorsoChestHit(Vector2 pixel, out RaycastHit best)
    {
        float bestDistance = float.PositiveInfinity; best = default; bool found = false;
        for (int radius = 0; radius <= 15 && !found; radius += 5)
            for (int j = 0; j < (radius == 0 ? 1 : 8); j++)
            {
                Vector2 test = pixel + new Vector2(Mathf.Cos(j * Mathf.PI / 4f), Mathf.Sin(j * Mathf.PI / 4f)) * radius;
                Vector2 offset = (test - sourceShoulder) / scale;
                var ray = new Ray(shoulderWorld + cameraRight * offset.x + cameraDown * offset.y - cameraForward * 3f, cameraForward);
                foreach (TorsoQuery query in torsoOnlyQueries)
                    if (query.collider.sharedMesh != null && query.collider.Raycast(ray, out RaycastHit hit, 6f) && hit.distance < bestDistance)
                    { found = true; bestDistance = hit.distance; best = hit; }
            }
        return found;
    }

    /// <summary>One-shot evidence for a selected full-mesh or torso-only ray hit.</summary>
    private TorsoHitEvidence DescribeTorsoHit(RaycastHit hit, bool filtered)
    {
        InitializeTorsoQueries(); TorsoQuery selected = null;
        if (filtered)
        { foreach (TorsoQuery query in torsoOnlyQueries) if (query.collider == hit.collider) { selected = query; break; } }
        else
        {
            for (int i = 0; i < chestQueries.Count; i++)
                if (chestQueries[i] == hit.collider)
                    foreach (TorsoQuery query in torsoOnlyQueries) if (query.source == querySkins[i].source) { selected = query; break; }
        }
        if (selected == null) throw new InvalidOperationException("Selected chest hit is not owned by this private query session.");
        int ordinal = filtered ? selected.originalTriangleOrdinals[hit.triangleIndex] : hit.triangleIndex;
        int start = ordinal * 3;
        int[] ids = { selected.fullIndices[start], selected.fullIndices[start + 1], selected.fullIndices[start + 2] };
        float minimum = 1f, mean = 0f; var boneWeights = new Dictionary<string, float>();
        foreach (int id in ids)
        {
            minimum = Mathf.Min(minimum, selected.torsoWeights[id]); mean += selected.torsoWeights[id] / 3f;
            BoneWeight w = selected.weights[id];
            AddTorsoBoneEvidence(boneWeights, selected.sourceBoneNames[w.boneIndex0], w.weight0 / 3f);
            AddTorsoBoneEvidence(boneWeights, selected.sourceBoneNames[w.boneIndex1], w.weight1 / 3f);
            AddTorsoBoneEvidence(boneWeights, selected.sourceBoneNames[w.boneIndex2], w.weight2 / 3f);
            AddTorsoBoneEvidence(boneWeights, selected.sourceBoneNames[w.boneIndex3], w.weight3 / 3f);
        }
        var ordered = new List<KeyValuePair<string, float>>(boneWeights); ordered.Sort((a, b) => b.Value.CompareTo(a.Value));
        var names = new string[ordered.Count]; var values = new float[ordered.Count];
        for (int i = 0; i < ordered.Count; i++) { names[i] = ordered[i].Key; values[i] = ordered[i].Value; }
        return new TorsoHitEvidence { renderer = selected.source.name, queryPolicy = filtered ? "positive-torso-source-influence" : "legacy-full-body-and-shirt",
            pointWorld = hit.point, normalWorld = hit.normal, queryTriangleIndex = hit.triangleIndex, originalTriangleIndex = ordinal,
            minimumTorsoVertexWeight = minimum, meanTorsoVertexWeight = mean, sourceBoneNames = names, sourceBoneMeanWeights = values };
    }

    private static void AddTorsoBoneEvidence(Dictionary<string, float> target, string name, float weight)
    { if (weight <= .00001f) return; target.TryGetValue(name, out float old); target[name] = old + weight; }

    private void DisposeTorsoQueries()
    {
        foreach (TorsoQuery query in torsoOnlyQueries)
        {
            if (query.holder != null) UnityEngine.Object.DestroyImmediate(query.holder);
            if (query.baked != null) UnityEngine.Object.DestroyImmediate(query.baked);
            if (query.filtered != null) UnityEngine.Object.DestroyImmediate(query.filtered);
        }
        torsoOnlyQueries.Clear(); torsoAllowedSourceNames.Clear();
    }

    [MenuItem("GuideChuna/시험/제2늑골/V03 흉부 접촉 후보 실측")]
    private static void AuditTorsoQueryMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Torso query audit requires edit mode.");
        string path = Absolute(Folder + "/torso_query_audit.json");
        if (File.Exists(path)) throw new IOException("Torso query audit does not overwrite existing evidence: " + path);
        using (var session = new Rib2AnatomicalHandsReviewSession())
        {
            try
            {
                var samples = new List<object>();
                foreach (int frame in new[] { 0, 120, 202 })
                {
                    session.Sample(frame / (float)Fps);
                    Vector2 selectedPixel = session.Project(session.supportContact);
                    RaycastHit legacy = session.ChestHit(selectedPixel);
                    TorsoHitEvidence original = session.DescribeTorsoHit(legacy, false);
                    session.BakeTorsoQueries();
                    object torso = null; string torsoError = "";
                    try { torso = TorsoEvidenceJson(session.DescribeTorsoHit(session.TorsoChestHit(selectedPixel), true)); }
                    catch (InvalidOperationException error) { torsoError = error.Message; }
                    samples.Add(CalibrationObject("frameIndex", frame, "sourceSeconds", SourceStart + frame / (float)Fps,
                        "selectedPixel", new[] { selectedPixel.x, selectedPixel.y }, "selectedRayRehitErrorMetres", Vector3.Distance(legacy.point, session.supportContact),
                        "supportRootWorldQuaternion", CalibrationArray(session.support.Root.rotation), "supportPalmarWorld", CalibrationArray(session.support.PalmNormalWorld),
                        "supportDistalWorld", CalibrationArray(session.support.FingerDirectionWorld), "legacy", TorsoEvidenceJson(original), "torsoOnly", torso, "torsoOnlyError", torsoError));
                }
                var audit = CalibrationObject("policy", "Actual source Humanoid Hips/Spine/Chest/UpperChest only; all3triangle vertices >=0.6 summed torso influence. Private copied BakeMesh query, no original asset/scene edits.",
                    "allowedSourceBones", session.torsoAllowedSourceNames, "samples", samples,
                    "scope", "Classifies which native surface the support ray selected. Hidden pressure contact is not uniquely recovered from either camera.");
                var json = new StringBuilder(); CalibrationWrite(json, audit); json.Append('\n'); File.WriteAllText(path, json.ToString(), new UTF8Encoding(false));
                Debug.Log("[Rib2TorsoQueryAudit] " + path);
            }
            finally { session.DisposeTorsoQueries(); }
        }
    }

    private static object TorsoEvidenceJson(TorsoHitEvidence evidence)
    { return CalibrationObject("renderer", evidence.renderer, "queryPolicy", evidence.queryPolicy, "pointWorld", CalibrationArray(evidence.pointWorld), "normalWorld", CalibrationArray(evidence.normalWorld),
        "queryTriangleIndex", evidence.queryTriangleIndex, "originalTriangleIndex", evidence.originalTriangleIndex, "minimumTorsoVertexWeight", evidence.minimumTorsoVertexWeight,
        "meanTorsoVertexWeight", evidence.meanTorsoVertexWeight, "sourceBoneNames", evidence.sourceBoneNames, "sourceBoneMeanWeights", evidence.sourceBoneMeanWeights); }
}
