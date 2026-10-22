using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Rebinds private copies of the c9 skins to the experimental anatomical output rig.
/// Original mesh assets and their blendshapes are never edited. Weight regions are
/// taken from the actual c9 influences, including the Twist bones which carry the
/// arm skin even when Upperarm/Forearm themselves have no weighted vertices.
///
/// Scapular skin sharing and the small volume correctives are authoring heuristics,
/// not clinically validated tissue, muscle-force or contact simulations.
/// </summary>
public static class AnatomicalSkinBuilder
{
    public sealed class Result
    {
        public int changedVertices;
        public AnatomicalPatientRig.SkinCorrection[] corrections;
    }

    private enum Region { Unchanged, Clavicle, UpperArm, Elbow, Forearm, Hand, Neck, UpperBack }

    private struct Influence
    {
        public int index;
        public float weight;
        public Influence(int index, float weight) { this.index = index; this.weight = weight; }
    }

    private sealed class SourceBone
    {
        public int outputIndex;
        public Region region;
        public AnatomicalPatientRig.Limb limb;
    }

    private sealed class BodyFrame
    {
        public Vector3 center;
        public float shoulderHeight;
        public float posteriorDepth;
    }

    private sealed class NeutralSkin
    {
        public Vector3[] vertices;
        public Vector3[] normals;
        public Vector4[] tangents;
        public Matrix4x4[] vertexSkinMatrices;
        public float[] blendWeights;
    }

    public static Result Rebind(GameObject patient, Transform rigSpace,
        Dictionary<Transform, Transform> mirrors, AnatomicalPatientRig.Limb[] limbs,
        AnatomicalPatientRig.Neck neck, string assetFolder)
    {
        if (patient == null || rigSpace == null || mirrors == null || limbs == null || limbs.Length != 2 ||
            neck == null || neck.segments == null || neck.segments.Length != 7 || neck.sourceChest == null)
            throw new ArgumentException("[AnatomicalSkin] Missing patient, positive-scale rig space, two limbs or seven neck segments.");
        if (rigSpace.localToWorldMatrix.determinant <= 0f)
            throw new InvalidOperationException("[AnatomicalSkin] Rig space must use the measured positive-scale anatomical basis.");

        EnsureAssetFolder(assetFolder);
        var skins = patient.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var neutralSkins = new Dictionary<SkinnedMeshRenderer, NeutralSkin>();
        foreach (SkinnedMeshRenderer skin in skins)
        {
            if (skin.sharedMesh == null || skin.sharedMesh.boneWeights.Length == 0) continue;
            neutralSkins.Add(skin, CaptureNeutralSkin(skin));
        }
        var frame = MeasureBodyFrame(skins, neutralSkins, rigSpace, limbs, neck);
        var corrections = new List<AnatomicalPatientRig.SkinCorrection>();
        var redistributedCounts = new Dictionary<SkinnedMeshRenderer, int>();
        int changed = 0, trimmed = 0, copiedMeshes = 0;

        foreach (SkinnedMeshRenderer skin in skins)
        {
            Mesh original = skin.sharedMesh;
            if (original == null) continue;
            BoneWeight[] sourceWeights = original.boneWeights;
            if (sourceWeights.Length == 0) continue;
            if (sourceWeights.Length != original.vertexCount)
                throw new InvalidOperationException("[AnatomicalSkin] Unsupported weight layout: " + skin.name);

            Transform[] sourceBones = skin.bones;
            var outputBones = new List<Transform>();
            var outputIndices = new Dictionary<Transform, int>();
            var roles = new SourceBone[sourceBones.Length];
            for (int i = 0; i < sourceBones.Length; i++)
            {
                Transform source = sourceBones[i];
                Transform output;
                if (source == null || !mirrors.TryGetValue(source, out output) || output == null)
                    throw new InvalidOperationException("[AnatomicalSkin] Missing explicit mirror for " + skin.name + " bone " + i + ".");
                roles[i] = Describe(source, AddBone(output, outputBones, outputIndices), limbs, neck);
            }
            foreach (AnatomicalPatientRig.Limb limb in limbs)
            {
                RequireLimb(limb);
                AddBone(limb.clavicle, outputBones, outputIndices);
                AddBone(limb.scapula, outputBones, outputIndices);
                AddBone(limb.humerus, outputBones, outputIndices);
                AddBone(limb.ulna, outputBones, outputIndices);
                AddBone(limb.radius, outputBones, outputIndices);
                AddBone(limb.hand, outputBones, outputIndices);
            }
            foreach (Transform segment in neck.segments) AddBone(segment, outputBones, outputIndices);

            NeutralSkin neutral = neutralSkins[skin];
            Vector3[] vertices = neutral.vertices;
            var positions = new Vector3[vertices.Length];
            var weights = new BoneWeight[vertices.Length];
            var armMasks = new float[limbs.Length][];
            for (int i = 0; i < limbs.Length; i++) armMasks[i] = new float[vertices.Length];
            var fixedWeights = new Dictionary<int, float>();
            var affectedWeights = new Dictionary<int, float>();
            var packedFixed = new List<Influence>(4);
            var packedAffected = new List<Influence>(12);
            int meshChanged = 0, meshTrimmed = 0;

            for (int v = 0; v < vertices.Length; v++)
            {
                Vector3 p = rigSpace.InverseTransformPoint(skin.transform.TransformPoint(vertices[v]));
                positions[v] = p;
                fixedWeights.Clear();
                affectedWeights.Clear();
                bool touched = false;
                BoneWeight source = sourceWeights[v];
                for (int slot = 0; slot < 4; slot++)
                {
                    float w = Weight(source, slot);
                    if (w <= 0f) continue;
                    int boneIndex = Index(source, slot);
                    if (boneIndex < 0 || boneIndex >= roles.Length || float.IsNaN(w) || float.IsInfinity(w))
                        throw new InvalidOperationException("[AnatomicalSkin] Invalid source influence in " + skin.name + " vertex " + v + ".");
                    SourceBone role = roles[boneIndex];
                    if (role.limb != null)
                    {
                        for (int a = 0; a < limbs.Length; a++)
                            if (role.limb == limbs[a]) armMasks[a][v] += w;
                    }
                    touched |= MapInfluence(p, w, role, rigSpace, frame, limbs, neck,
                        outputIndices, fixedWeights, affectedWeights);
                }

                float sum = source.weight0 + source.weight1 + source.weight2 + source.weight3;
                if (Mathf.Abs(sum - 1f) > .001f)
                    throw new InvalidOperationException("[AnatomicalSkin] Source weights do not sum to one in " + skin.name + " vertex " + v + ": " + sum);
                bool compressed;
                weights[v] = Pack(fixedWeights, affectedWeights, packedFixed, packedAffected, out compressed);
                float packedSum = weights[v].weight0 + weights[v].weight1 + weights[v].weight2 + weights[v].weight3;
                if (Mathf.Abs(packedSum - sum) > .00001f)
                    throw new InvalidOperationException("[AnatomicalSkin] Weight conservation failed in " + skin.name + " vertex " + v + ".");
                if (touched) meshChanged++;
                if (compressed) meshTrimmed++;
            }

            // The c9 source's raw mesh is not its displayed skin: measured Body and
            // Shirt differences were 375.411mm and 144.414mm. Freeze the actual
            // zero-expression CPU skin bake, then transport every original morph
            // delta through the same per-vertex source skin matrix. This changes
            // only private mesh copies and preserves the existing expression weights.
            Mesh copy = CloneInCurrentSkinFrame(original, neutral);
            copy.name = original.name + "_AnatomicalSkin";
            var bindposes = new Matrix4x4[outputBones.Count];
            for (int i = 0; i < bindposes.Length; i++)
                bindposes[i] = outputBones[i].worldToLocalMatrix * skin.transform.localToWorldMatrix;
            copy.bindposes = bindposes;
            copy.boneWeights = weights;
            int shapeCountBefore = copy.blendShapeCount;
            for (int a = 0; a < limbs.Length; a++)
            {
                AddVolumeCorrective(copy, skin, rigSpace, limbs[a], positions, armMasks[a], true, corrections);
                AddVolumeCorrective(copy, skin, rigSpace, limbs[a], positions, armMasks[a], false, corrections);
            }
            copy.RecalculateBounds();
            string path = AssetDatabase.GenerateUniqueAssetPath(assetFolder.TrimEnd('/') + "/Skin_" + SafeName(skin.name) + ".asset");
            AssetDatabase.CreateAsset(copy, path);
            skin.sharedMesh = copy;
            skin.bones = outputBones.ToArray();
            Transform rootBone;
            if (skin.rootBone != null && mirrors.TryGetValue(skin.rootBone, out rootBone)) skin.rootBone = rootBone;
            else skin.rootBone = rigSpace;
            skin.quality = SkinQuality.Bone4;
            skin.localBounds = MotionBounds(skin, rigSpace, positions, armMasks, limbs);
            for (int i = 0; i < neutral.blendWeights.Length; i++) skin.SetBlendShapeWeight(i, neutral.blendWeights[i]);
            for (int i = shapeCountBefore; i < copy.blendShapeCount; i++) skin.SetBlendShapeWeight(i, 0f);
            EditorUtility.SetDirty(skin);
            changed += meshChanged;
            trimmed += meshTrimmed;
            copiedMeshes++;
            redistributedCounts.Add(skin, meshChanged);
            Debug.Log("[AnatomicalSkin] " + skin.name + ": copied " + vertices.Length + " vertices; redistributed " + meshChanged +
                "; four-influence pool compression " + meshTrimmed + "; original shapes " + shapeCountBefore +
                "; added conservative volume shapes " + (copy.blendShapeCount - shapeCountBefore) + "; " + path);
            for (int a = 0; a < limbs.Length; a++)
            {
                int scapulaIndex = outputIndices[limbs[a].scapula];
                int scapularVertices = 0;
                foreach (BoneWeight weight in weights)
                {
                    for (int slot = 0; slot < 4; slot++)
                    {
                        if (Index(weight, slot) != scapulaIndex || Weight(weight, slot) <= 0f) continue;
                        scapularVertices++;
                        break;
                    }
                }
                if (scapularVertices > 0)
                    Debug.Log("[AnatomicalSkin] " + skin.name + ": " + (limbs[a].left ? "left" : "right") +
                        " scapula has actual positive influence on " + scapularVertices + " vertices.");
            }
        }

        changed += CoupleShirtToBody(skins, rigSpace, assetFolder, corrections, redistributedCounts);

        Debug.Log("[AnatomicalSkin] Complete: private meshes " + copiedMeshes + ", redistributed vertices " + changed +
            ", compressed affected pools " + trimmed + ", correctives " + corrections.Count +
            ". Original assets/face shapes retained. Scapular sharing and volume correction are unvalidated authoring approximations.");
        return new Result { changedVertices = changed, corrections = corrections.ToArray() };
    }

    private sealed class BlendFrame
    {
        public string name;
        public float weight;
        public Vector3[] positions;
        public Vector3[] normals;
        public Vector3[] tangents;
    }

    private struct SurfaceSample
    {
        public int a, b, c;
        public Vector3 barycentric;
        public float distanceSquared;
    }

    private static int CoupleShirtToBody(SkinnedMeshRenderer[] skins, Transform space, string assetFolder,
        List<AnatomicalPatientRig.SkinCorrection> corrections, Dictionary<SkinnedMeshRenderer, int> redistributedCounts)
    {
        SkinnedMeshRenderer body = null, shirt = null;
        foreach (SkinnedMeshRenderer skin in skins)
        {
            if (skin.name == "CC_Base_Body") body = skin;
            else if (skin.name == "Shirt") shirt = skin;
        }
        if (body == null || shirt == null)
        {
            Debug.Log("[AnatomicalSkin] No CC_Base_Body/Shirt pair; cloth-layer transfer skipped.");
            return 0;
        }
        Mesh bodyMesh = body.sharedMesh, shirtMesh = shirt.sharedMesh;
        string generatedPath = AssetDatabase.GetAssetPath(shirtMesh);
        if (!generatedPath.StartsWith(assetFolder.TrimEnd('/') + "/", StringComparison.Ordinal))
            throw new InvalidOperationException("[AnatomicalSkin] Cloth transfer may only edit this build's generated Shirt mesh.");

        Vector3[] bodyPoints = bodyMesh.vertices, shirtPoints = shirtMesh.vertices;
        for (int i = 0; i < bodyPoints.Length; i++) bodyPoints[i] = space.InverseTransformPoint(body.transform.TransformPoint(bodyPoints[i]));
        for (int i = 0; i < shirtPoints.Length; i++) shirtPoints[i] = space.InverseTransformPoint(shirt.transform.TransformPoint(shirtPoints[i]));
        var surface = new TriangleSurface(bodyPoints, bodyMesh.triangles);
        var samples = new SurfaceSample[shirtPoints.Length];
        float maximumDistance = 0f, totalDistance = 0f;
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = surface.Nearest(shirtPoints[i]);
            float distance = Mathf.Sqrt(samples[i].distanceSquared);
            maximumDistance = Mathf.Max(maximumDistance, distance);
            totalDistance += distance;
        }

        Transform[] bodyBones = body.bones;
        var shirtBones = new List<Transform>(shirt.bones);
        var shirtIndices = new Dictionary<Transform, int>();
        for (int i = 0; i < shirtBones.Count; i++) if (!shirtIndices.ContainsKey(shirtBones[i])) shirtIndices.Add(shirtBones[i], i);
        var palette = new int[bodyBones.Length];
        for (int i = 0; i < bodyBones.Length; i++) palette[i] = AddBone(bodyBones[i], shirtBones, shirtIndices);
        BoneWeight[] bodyWeights = bodyMesh.boneWeights;
        var shirtWeights = new BoneWeight[shirtPoints.Length];
        var fixedWeights = new Dictionary<int, float>();
        var affectedWeights = new Dictionary<int, float>();
        var locked = new List<Influence>(4);
        var candidates = new List<Influence>(12);
        int compressed = 0;
        for (int i = 0; i < shirtWeights.Length; i++)
        {
            SurfaceSample sample = samples[i];
            affectedWeights.Clear();
            AddSurfaceWeights(affectedWeights, bodyWeights[sample.a], sample.barycentric.x, palette);
            AddSurfaceWeights(affectedWeights, bodyWeights[sample.b], sample.barycentric.y, palette);
            AddSurfaceWeights(affectedWeights, bodyWeights[sample.c], sample.barycentric.z, palette);
            bool trimmed;
            shirtWeights[i] = Pack(fixedWeights, affectedWeights, locked, candidates, out trimmed);
            if (trimmed) compressed++;
            float sum = shirtWeights[i].weight0 + shirtWeights[i].weight1 + shirtWeights[i].weight2 + shirtWeights[i].weight3;
            if (Mathf.Abs(sum - 1f) > .00001f) throw new InvalidOperationException("[AnatomicalSkin] Cloth transfer lost weight normalization.");
        }

        var bindposes = new Matrix4x4[shirtBones.Count];
        for (int i = 0; i < bindposes.Length; i++) bindposes[i] = shirtBones[i].worldToLocalMatrix * shirt.transform.localToWorldMatrix;
        shirtMesh.bindposes = bindposes;
        shirtMesh.boneWeights = shirtWeights;
        shirt.bones = shirtBones.ToArray();
        TransferBodyCorrectives(body, shirt, samples, corrections);
        shirt.bones = shirtBones.ToArray();
        EditorUtility.SetDirty(shirtMesh);
        EditorUtility.SetDirty(shirt);
        int previous = 0;
        redistributedCounts.TryGetValue(shirt, out previous);
        Debug.Log("[AnatomicalSkin] Shirt cloth-layer approximation: transferred all " + shirtWeights.Length +
            " vertices from the nearest generated Body triangle using barycentric weights and explicit Transform palette mapping; " +
            "mean/max rest surface distance " + (totalDistance / Mathf.Max(1, samples.Length) * 1000f).ToString("F2") + "/" +
            (maximumDistance * 1000f).ToString("F2") + "mm; four-influence compression " + compressed +
            ". Shirt neutral vertices and original morph frames are unchanged. Shirt intentionally replaces its original regional weights " +
            "to follow the underlying skin; this is a cloth-layer approximation, not tissue/contact simulation.");
        return shirtWeights.Length - previous;
    }

    private static void AddSurfaceWeights(Dictionary<int, float> target, BoneWeight weight, float factor, int[] palette)
    {
        for (int slot = 0; slot < 4; slot++)
        {
            float amount = Weight(weight, slot) * factor;
            if (amount > 0f) Add(target, palette[Index(weight, slot)], amount);
        }
    }

    private static void TransferBodyCorrectives(SkinnedMeshRenderer body, SkinnedMeshRenderer shirt,
        SurfaceSample[] samples, List<AnatomicalPatientRig.SkinCorrection> corrections)
    {
        Mesh target = shirt.sharedMesh, source = body.sharedMesh;
        var removed = new HashSet<int>();
        var bodyCorrections = new List<AnatomicalPatientRig.SkinCorrection>();
        foreach (AnatomicalPatientRig.SkinCorrection correction in corrections)
        {
            if (correction.renderer == shirt) removed.Add(correction.blendShape);
            else if (correction.renderer == body) bodyCorrections.Add(correction);
        }
        var savedWeights = new Dictionary<string, float>();
        var originalFrames = new List<BlendFrame>();
        for (int shape = 0; shape < target.blendShapeCount; shape++)
        {
            string name = target.GetBlendShapeName(shape);
            savedWeights[name] = shirt.GetBlendShapeWeight(shape);
            if (removed.Contains(shape)) continue;
            for (int frame = 0; frame < target.GetBlendShapeFrameCount(shape); frame++)
            {
                var saved = new BlendFrame { name = name, weight = target.GetBlendShapeFrameWeight(shape, frame),
                    positions = new Vector3[target.vertexCount], normals = new Vector3[target.vertexCount], tangents = new Vector3[target.vertexCount] };
                target.GetBlendShapeFrameVertices(shape, frame, saved.positions, saved.normals, saved.tangents);
                originalFrames.Add(saved);
            }
        }
        target.ClearBlendShapes();
        foreach (BlendFrame saved in originalFrames)
            target.AddBlendShapeFrame(saved.name, saved.weight, saved.positions, saved.normals, saved.tangents);
        corrections.RemoveAll(correction => correction.renderer == shirt);
        Matrix4x4 bodyToShirt = shirt.transform.worldToLocalMatrix * body.transform.localToWorldMatrix;
        foreach (AnatomicalPatientRig.SkinCorrection correction in bodyCorrections)
        {
            string name = source.GetBlendShapeName(correction.blendShape);
            var sourcePositions = new Vector3[source.vertexCount];
            var sourceNormals = new Vector3[source.vertexCount];
            var sourceTangents = new Vector3[source.vertexCount];
            var positions = new Vector3[target.vertexCount];
            var normals = new Vector3[target.vertexCount];
            var tangents = new Vector3[target.vertexCount];
            for (int frame = 0; frame < source.GetBlendShapeFrameCount(correction.blendShape); frame++)
            {
                source.GetBlendShapeFrameVertices(correction.blendShape, frame, sourcePositions, sourceNormals, sourceTangents);
                for (int i = 0; i < samples.Length; i++)
                {
                    SurfaceSample sample = samples[i];
                    Vector3 b = sample.barycentric;
                    positions[i] = bodyToShirt.MultiplyVector(sourcePositions[sample.a] * b.x + sourcePositions[sample.b] * b.y + sourcePositions[sample.c] * b.z);
                    normals[i] = bodyToShirt.MultiplyVector(sourceNormals[sample.a] * b.x + sourceNormals[sample.b] * b.y + sourceNormals[sample.c] * b.z);
                    tangents[i] = bodyToShirt.MultiplyVector(sourceTangents[sample.a] * b.x + sourceTangents[sample.b] * b.y + sourceTangents[sample.c] * b.z);
                }
                target.AddBlendShapeFrame(name, source.GetBlendShapeFrameWeight(correction.blendShape, frame), positions, normals, tangents);
            }
            corrections.Add(new AnatomicalPatientRig.SkinCorrection { renderer = shirt, blendShape = target.GetBlendShapeIndex(name),
                left = correction.left, trigger = correction.trigger, startDegrees = correction.startDegrees, fullDegrees = correction.fullDegrees });
        }
        // Refresh Unity's renderer-side shape array after rebuilding generated frames.
        shirt.sharedMesh = null;
        shirt.sharedMesh = target;
        for (int shape = 0; shape < target.blendShapeCount; shape++)
        {
            string name = target.GetBlendShapeName(shape);
            float weight;
            shirt.SetBlendShapeWeight(shape, savedWeights.TryGetValue(name, out weight) ? weight : 0f);
        }
    }

    /// <summary>Build-time nearest-triangle query. No per-frame cloth processing.</summary>
    private sealed class TriangleSurface
    {
        private struct Triangle { public int a, b, c; public Bounds bounds; public Vector3 center; }
        private sealed class Node { public Bounds bounds; public int start, count; public int left = -1, right = -1; }
        private readonly Vector3[] points;
        private readonly Triangle[] triangles;
        private readonly int[] order;
        private readonly List<Node> nodes = new List<Node>();

        public TriangleSurface(Vector3[] points, int[] indices)
        {
            this.points = points;
            triangles = new Triangle[indices.Length / 3];
            order = new int[triangles.Length];
            if (triangles.Length == 0) throw new InvalidOperationException("[AnatomicalSkin] Body surface has no triangles.");
            for (int i = 0; i < triangles.Length; i++)
            {
                int a = indices[i * 3], b = indices[i * 3 + 1], c = indices[i * 3 + 2];
                Bounds bounds = new Bounds(points[a], Vector3.zero);
                bounds.Encapsulate(points[b]); bounds.Encapsulate(points[c]);
                triangles[i] = new Triangle { a = a, b = b, c = c, bounds = bounds, center = bounds.center };
                order[i] = i;
            }
            Build(0, order.Length);
        }

        private int Build(int start, int count)
        {
            int result = nodes.Count;
            var node = new Node { start = start, count = count, bounds = triangles[order[start]].bounds };
            nodes.Add(node);
            for (int i = 1; i < count; i++) node.bounds.Encapsulate(triangles[order[start + i]].bounds);
            if (count <= 8) return result;
            Vector3 size = node.bounds.size;
            int axis = size.x > size.y ? (size.x > size.z ? 0 : 2) : (size.y > size.z ? 1 : 2);
            Array.Sort(order, start, count, Comparer<int>.Create((a, b) => triangles[a].center[axis].CompareTo(triangles[b].center[axis])));
            int half = count / 2;
            node.left = Build(start, half); node.right = Build(start + half, count - half); node.count = 0;
            return result;
        }

        public SurfaceSample Nearest(Vector3 p)
        {
            var sample = new SurfaceSample { distanceSquared = float.PositiveInfinity };
            Query(0, p, ref sample);
            if (float.IsInfinity(sample.distanceSquared)) throw new InvalidOperationException("[AnatomicalSkin] Body surface projection failed.");
            return sample;
        }

        private void Query(int index, Vector3 p, ref SurfaceSample sample)
        {
            Node node = nodes[index];
            if (node.bounds.SqrDistance(p) >= sample.distanceSquared) return;
            if (node.count > 0)
            {
                for (int i = 0; i < node.count; i++)
                {
                    Triangle t = triangles[order[node.start + i]];
                    Vector3 bary = ClosestBarycentric(p, points[t.a], points[t.b], points[t.c]);
                    Vector3 q = points[t.a] * bary.x + points[t.b] * bary.y + points[t.c] * bary.z;
                    float distance = (p - q).sqrMagnitude;
                    if (distance < sample.distanceSquared) sample = new SurfaceSample { a = t.a, b = t.b, c = t.c, barycentric = bary, distanceSquared = distance };
                }
                return;
            }
            int near = node.left, far = node.right;
            if (nodes[far].bounds.SqrDistance(p) < nodes[near].bounds.SqrDistance(p)) { int swap = near; near = far; far = swap; }
            Query(near, p, ref sample); Query(far, p, ref sample);
        }

        private static Vector3 ClosestBarycentric(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            if (Vector3.Cross(ab, ac).sqrMagnitude < 1e-20f)
            {
                // Degenerate source faces still have a nearest point on one of
                // their edges. This query transfers influences; it never snaps
                // the Shirt's preserved neutral vertices onto the body.
                Vector3 bary = new Vector3(1f, 0f, 0f);
                float best = (p - a).sqrMagnitude;
                TestEdge(p, a, b, new Vector3(1f, 0f, 0f), new Vector3(0f, 1f, 0f), ref bary, ref best);
                TestEdge(p, b, c, new Vector3(0f, 1f, 0f), new Vector3(0f, 0f, 1f), ref bary, ref best);
                TestEdge(p, c, a, new Vector3(0f, 0f, 1f), new Vector3(1f, 0f, 0f), ref bary, ref best);
                return bary;
            }
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return new Vector3(1f, 0f, 0f);
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return new Vector3(0f, 1f, 0f);
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) { float v = d1 / (d1 - d3); return new Vector3(1f - v, v, 0f); }
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return new Vector3(0f, 0f, 1f);
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) { float w = d2 / (d2 - d6); return new Vector3(1f - w, 0f, w); }
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6)); return new Vector3(0f, 1f - w, w);
            }
            float sum = va + vb + vc;
            if (Mathf.Abs(sum) < 1e-20f) return new Vector3(1f, 0f, 0f);
            return new Vector3(va / sum, vb / sum, vc / sum);
        }

        private static void TestEdge(Vector3 p, Vector3 a, Vector3 b, Vector3 ba, Vector3 bb, ref Vector3 bary, ref float best)
        {
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 1e-20f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            float distance = (p - (a + ab * t)).sqrMagnitude;
            if (distance >= best) return;
            best = distance;
            bary = ba * (1f - t) + bb * t;
        }
    }

    private static SourceBone Describe(Transform source, int outputIndex,
        AnatomicalPatientRig.Limb[] limbs, AnatomicalPatientRig.Neck neck)
    {
        var role = new SourceBone { outputIndex = outputIndex };
        if (source == neck.sourceNeck1 || source == neck.sourceNeck2)
        {
            role.region = Region.Neck;
            return role;
        }
        if (source == neck.sourceChest)
        {
            role.region = Region.UpperBack;
            return role;
        }
        foreach (AnatomicalPatientRig.Limb limb in limbs)
        {
            string prefix = limb.sourceUpper.name.Substring(0, limb.sourceUpper.name.LastIndexOf('_') + 1);
            if (!source.name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            role.limb = limb;
            if (source == limb.sourceClavicle) role.region = Region.Clavicle;
            else if (source == limb.sourceHand) role.region = Region.Hand;
            else if (source.name.IndexOf("Upperarm", StringComparison.OrdinalIgnoreCase) >= 0) role.region = Region.UpperArm;
            else if (source.name.IndexOf("ElbowShare", StringComparison.OrdinalIgnoreCase) >= 0) role.region = Region.Elbow;
            else if (source.name.IndexOf("Forearm", StringComparison.OrdinalIgnoreCase) >= 0) role.region = Region.Forearm;
            else role.limb = null; // Fingers/face/other body areas keep the original mirror influence.
            break;
        }
        return role;
    }

    private static bool MapInfluence(Vector3 p, float w, SourceBone role, Transform space, BodyFrame frame,
        AnatomicalPatientRig.Limb[] limbs, AnatomicalPatientRig.Neck neck, Dictionary<Transform, int> indices,
        Dictionary<int, float> fixedWeights, Dictionary<int, float> affected)
    {
        AnatomicalPatientRig.Limb limb = role.limb;
        switch (role.region)
        {
            case Region.UpperArm:
                Add(affected, indices[limb.humerus], w);
                return true;
            case Region.Elbow:
                Add(affected, indices[limb.humerus], w * .5f);
                Add(affected, indices[limb.ulna], w * .5f);
                return true;
            case Region.Forearm:
                Vector3 elbow = space.InverseTransformPoint(limb.ulna.position);
                Vector3 wrist = space.InverseTransformPoint(limb.hand.position);
                Vector3 axis = wrist - elbow;
                // Projection deliberately selects longitudinal skin twist sharing. It does
                // not estimate radius/ulna identity or discard depth in the motion solver.
                float t = axis.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector3.Dot(p - elbow, axis) / axis.sqrMagnitude) : 0f;
                float radialShare = Mathf.SmoothStep(0f, 1f, t);
                Add(affected, indices[limb.ulna], w * (1f - radialShare));
                Add(affected, indices[limb.radius], w * radialShare);
                return true;
            case Region.Hand:
                Add(affected, indices[limb.hand], w);
                return true;
            case Region.Clavicle:
                float scapularShare = .35f * BackMask(p, limb, space, frame);
                Add(affected, indices[limb.clavicle], w * (1f - scapularShare));
                Add(affected, indices[limb.scapula], w * scapularShare);
                return true;
            case Region.Neck:
                int lower;
                float fraction;
                ClosestNeckInterval(p, space, neck.segments, out lower, out fraction);
                Add(affected, indices[neck.segments[lower]], w * (1f - fraction));
                Add(affected, indices[neck.segments[lower + 1]], w * fraction);
                return true;
            case Region.UpperBack:
                float leftMask = BackMask(p, limbs[0], space, frame);
                float rightMask = BackMask(p, limbs[1], space, frame);
                float total = leftMask + rightMask;
                if (total > 1e-5f)
                {
                    // Only part of the original chest influence moves to the scapula.
                    // The masks intentionally exclude anterior chest, lumbar skin and
                    // midline skin; no claim is made about their muscle deformation.
                    float transfer = .18f * Mathf.Min(1f, total);
                    Add(affected, role.outputIndex, w * (1f - transfer));
                    Add(affected, indices[limbs[0].scapula], w * transfer * leftMask / total);
                    Add(affected, indices[limbs[1].scapula], w * transfer * rightMask / total);
                    return true;
                }
                break;
        }
        Add(fixedWeights, role.outputIndex, w);
        return false;
    }

    private static BodyFrame MeasureBodyFrame(SkinnedMeshRenderer[] skins,
        Dictionary<SkinnedMeshRenderer, NeutralSkin> neutralSkins, Transform space,
        AnatomicalPatientRig.Limb[] limbs, AnatomicalPatientRig.Neck neck)
    {
        RequireLimb(limbs[0]);
        RequireLimb(limbs[1]);
        var frame = new BodyFrame();
        frame.center = space.InverseTransformPoint(neck.sourceChest.position);
        Vector3 a = space.InverseTransformPoint(limbs[0].sourceUpper.position);
        Vector3 b = space.InverseTransformPoint(limbs[1].sourceUpper.position);
        frame.shoulderHeight = (a.y + b.y) * .5f;
        float halfWidth = Mathf.Abs(a.x - b.x) * .5f;
        foreach (SkinnedMeshRenderer skin in skins)
        {
            NeutralSkin neutral;
            if (!neutralSkins.TryGetValue(skin, out neutral)) continue;
            Vector3[] points = neutral.vertices;
            foreach (Vector3 point in points)
            {
                Vector3 p = space.InverseTransformPoint(skin.transform.TransformPoint(point));
                if (p.y < frame.center.y || p.y > frame.shoulderHeight || Mathf.Abs(p.x - frame.center.x) > halfWidth) continue;
                frame.posteriorDepth = Mathf.Max(frame.posteriorDepth, frame.center.z - p.z);
            }
        }
        if (frame.posteriorDepth <= .001f || frame.shoulderHeight <= frame.center.y + .01f)
            throw new InvalidOperationException("[AnatomicalSkin] Measured torso basis has no posterior depth/shoulder height. Check the builder's +Y up/+Z forward alignment.");
        Debug.Log("[AnatomicalSkin] Measured chest-to-shoulder height " + (frame.shoulderHeight - frame.center.y).ToString("F4") +
            "m; posterior skin depth " + frame.posteriorDepth.ToString("F4") + "m. Posterior masks use this measured geometry.");
        return frame;
    }

    private static NeutralSkin CaptureNeutralSkin(SkinnedMeshRenderer skin)
    {
        Mesh original = skin.sharedMesh;
        var state = new NeutralSkin { blendWeights = new float[original.blendShapeCount] };
        int activeExpressions = 0;
        var baked = new Mesh();
        try
        {
            // Bake the unexpressed base; keeping an active expression in both the
            // new base and its original blendshape would apply that expression twice.
            for (int i = 0; i < state.blendWeights.Length; i++)
            {
                state.blendWeights[i] = skin.GetBlendShapeWeight(i);
                if (Mathf.Abs(state.blendWeights[i]) > .00001f) activeExpressions++;
                skin.SetBlendShapeWeight(i, 0f);
            }
            skin.BakeMesh(baked);
            state.vertices = baked.vertices;
            state.normals = baked.normals;
            state.tangents = baked.tangents;
        }
        finally
        {
            for (int i = 0; i < state.blendWeights.Length; i++) skin.SetBlendShapeWeight(i, state.blendWeights[i]);
            UnityEngine.Object.DestroyImmediate(baked);
        }

        Transform[] bones = skin.bones;
        Matrix4x4[] bindposes = original.bindposes;
        BoneWeight[] weights = original.boneWeights;
        Vector3[] raw = original.vertices;
        if (state.vertices.Length != raw.Length || weights.Length != raw.Length || bindposes.Length != bones.Length)
            throw new InvalidOperationException("[AnatomicalSkin] Cannot preserve current source skin topology/bindposes: " + skin.name);
        var matrices = new Matrix4x4[bones.Length];
        Matrix4x4 rendererInverse = skin.transform.worldToLocalMatrix;
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == null) throw new InvalidOperationException("[AnatomicalSkin] Missing source skin bone: " + skin.name);
            matrices[i] = rendererInverse * bones[i].localToWorldMatrix * bindposes[i];
        }
        // These NativeArrays are non-owning views of mesh data. Copy their values;
        // do not Dispose them, which would invalidate Unity-owned mesh storage.
        byte[] nativeCounts = original.GetBonesPerVertex().ToArray();
        BoneWeight1[] nativeWeights = original.GetAllBoneWeights().ToArray();
        if (nativeCounts.Length != raw.Length)
            throw new InvalidOperationException("[AnatomicalSkin] Native weight counts do not match source vertices: " + skin.name);
        int sourceLimit = skin.quality == SkinQuality.Auto ? (int)QualitySettings.skinWeights : (int)skin.quality;
        if (sourceLimit <= 0) sourceLimit = 255;
        var histogram = new int[256];
        int maximumInfluences = 0;
        foreach (byte count in nativeCounts)
        {
            histogram[count]++;
            maximumInfluences = Mathf.Max(maximumInfluences, count);
        }
        if (maximumInfluences > 4)
            throw new InvalidOperationException("[AnatomicalSkin] Source " + skin.name + " has " + maximumInfluences +
                " native influences per vertex. Its original weights cannot be preserved with the explicit Bone4 output policy.");
        state.vertexSkinMatrices = new Matrix4x4[raw.Length];
        float rawDifference = 0f, matrixError = 0f;
        float oneError = 0f, twoError = 0f, fourError = 0f, fullError = 0f;
        int nativeOffset = 0;
        for (int v = 0; v < raw.Length; v++)
        {
            int count = nativeCounts[v];
            Matrix4x4 matrix = SourceSkinMatrix(matrices, nativeWeights, nativeOffset, count, sourceLimit);
            state.vertexSkinMatrices[v] = matrix;
            rawDifference = Mathf.Max(rawDifference, Vector3.Distance(raw[v], state.vertices[v]));
            matrixError = Mathf.Max(matrixError, Vector3.Distance(matrix.MultiplyPoint3x4(raw[v]), state.vertices[v]));
            oneError = Mathf.Max(oneError, Vector3.Distance(SourceSkinMatrix(matrices, nativeWeights, nativeOffset, count, 1).MultiplyPoint3x4(raw[v]), state.vertices[v]));
            twoError = Mathf.Max(twoError, Vector3.Distance(SourceSkinMatrix(matrices, nativeWeights, nativeOffset, count, 2).MultiplyPoint3x4(raw[v]), state.vertices[v]));
            fourError = Mathf.Max(fourError, Vector3.Distance(SourceSkinMatrix(matrices, nativeWeights, nativeOffset, count, 4).MultiplyPoint3x4(raw[v]), state.vertices[v]));
            fullError = Mathf.Max(fullError, Vector3.Distance(SourceSkinMatrix(matrices, nativeWeights, nativeOffset, count, 255).MultiplyPoint3x4(raw[v]), state.vertices[v]));
            nativeOffset += count;
        }
        if (nativeOffset != nativeWeights.Length)
            throw new InvalidOperationException("[AnatomicalSkin] Native weight array traversal mismatch: " + skin.name);
        string counts = "";
        for (int i = 0; i < histogram.Length; i++) if (histogram[i] > 0) counts += " " + i + ":" + histogram[i];
        Debug.Log("[AnatomicalSkin] " + skin.name + ": source renderer quality=" + skin.quality + ", global skin weights=" +
            QualitySettings.skinWeights + ", actual configured limit=" + sourceLimit + ", native influences histogram" + counts +
            ". Native normalized 1/2/4/all reproduction error (mm)=" + (oneError * 1000f).ToString("F6") + "/" +
            (twoError * 1000f).ToString("F6") + "/" + (fourError * 1000f).ToString("F6") + "/" + (fullError * 1000f).ToString("F6") + ".");
        // A mismatching skin-space convention would corrupt all original morph
        // deltas. Stop instead of silently guessing a scale/axis correction.
        if (matrixError > .0001f)
            throw new InvalidOperationException("[AnatomicalSkin] Source weighted matrices do not reproduce zero-expression BakeMesh for " +
                skin.name + ": " + matrixError + "m. Cannot transport original blendshape frames safely.");
        Debug.Log("[AnatomicalSkin] " + skin.name + ": raw-to-zero-expression BakeMesh " + (rawDifference * 1000f).ToString("F4") +
            "mm; weighted-matrix reproduction error " + (matrixError * 1000f).ToString("F6") +
            "mm; active original expression weights " + activeExpressions + ". All original weights are restored after capture.");
        return state;
    }

    private static Matrix4x4 SourceSkinMatrix(Matrix4x4[] matrices, BoneWeight1[] influences, int offset, int count, int limit)
    {
        int keep = Mathf.Min(count, limit);
        float sum = 0f;
        for (int i = 0; i < keep; i++) sum += influences[offset + i].weight;
        if (sum <= 0f) throw new InvalidOperationException("[AnatomicalSkin] Source has a vertex with no effective skin weight.");
        var result = new Matrix4x4();
        for (int i = 0; i < keep; i++)
        {
            BoneWeight1 influence = influences[offset + i];
            if (influence.boneIndex < 0 || influence.boneIndex >= matrices.Length)
                throw new InvalidOperationException("[AnatomicalSkin] Native source weight references an invalid bone.");
            float weight = influence.weight / sum;
            Matrix4x4 matrix = matrices[influence.boneIndex];
            for (int element = 0; element < 16; element++) result[element] += matrix[element] * weight;
        }
        return result;
    }

    private static Mesh CloneInCurrentSkinFrame(Mesh original, NeutralSkin neutral)
    {
        Mesh copy = UnityEngine.Object.Instantiate(original);
        copy.ClearBlendShapes();
        copy.vertices = neutral.vertices;
        if (neutral.normals.Length == neutral.vertices.Length) copy.normals = neutral.normals;
        if (neutral.tangents.Length == neutral.vertices.Length) copy.tangents = neutral.tangents;
        var positions = new Vector3[neutral.vertices.Length];
        var normals = new Vector3[neutral.vertices.Length];
        var tangents = new Vector3[neutral.vertices.Length];
        for (int shape = 0; shape < original.blendShapeCount; shape++)
        {
            string name = original.GetBlendShapeName(shape);
            for (int frame = 0; frame < original.GetBlendShapeFrameCount(shape); frame++)
            {
                original.GetBlendShapeFrameVertices(shape, frame, positions, normals, tangents);
                for (int v = 0; v < positions.Length; v++)
                {
                    Matrix4x4 matrix = neutral.vertexSkinMatrices[v];
                    // Morph deltas are directions: translation must not be applied.
                    // This is the same linear source skin map as the neutral bake,
                    // including the c9 current rest-vs-bind difference.
                    positions[v] = matrix.MultiplyVector(positions[v]);
                    normals[v] = matrix.MultiplyVector(normals[v]);
                    tangents[v] = matrix.MultiplyVector(tangents[v]);
                }
                copy.AddBlendShapeFrame(name, original.GetBlendShapeFrameWeight(shape, frame), positions, normals, tangents);
            }
        }
        return copy;
    }

    private static float BackMask(Vector3 p, AnatomicalPatientRig.Limb limb, Transform space, BodyFrame frame)
    {
        Vector3 shoulder = space.InverseTransformPoint(limb.sourceUpper.position);
        float side = shoulder.x >= frame.center.x ? 1f : -1f;
        float width = Mathf.Abs(shoulder.x - frame.center.x);
        if (width < .01f) return 0f;
        float lateral = side * (p.x - frame.center.x) / width;
        float height = (p.y - frame.center.y) / (frame.shoulderHeight - frame.center.y);
        float depth = (frame.center.z - p.z) / frame.posteriorDepth;
        return Smooth(.12f, .5f, lateral) * (1f - Smooth(.95f, 1.18f, lateral)) *
            Smooth(0f, .35f, height) * (1f - Smooth(1f, 1.25f, height)) * Smooth(.15f, .75f, depth);
    }

    private static void ClosestNeckInterval(Vector3 p, Transform space, Transform[] segments, out int lower, out float fraction)
    {
        lower = 0;
        fraction = 0f;
        float best = float.PositiveInfinity;
        for (int i = 0; i < segments.Length - 1; i++)
        {
            Vector3 a = space.InverseTransformPoint(segments[i].position);
            Vector3 b = space.InverseTransformPoint(segments[i + 1].position);
            Vector3 ab = b - a;
            // Endpoint clamping intentionally assigns vertices outside the chain to its
            // nearest end rather than extrapolating negative skin weights.
            float t = ab.sqrMagnitude > 1e-10f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            float distance = (p - (a + ab * t)).sqrMagnitude;
            if (distance >= best) continue;
            best = distance;
            lower = i;
            fraction = t;
        }
    }

    private static BoneWeight Pack(Dictionary<int, float> fixedWeights, Dictionary<int, float> affected,
        List<Influence> locked, List<Influence> candidates, out bool compressed)
    {
        locked.Clear();
        candidates.Clear();
        foreach (KeyValuePair<int, float> pair in fixedWeights)
        {
            float same;
            locked.Add(new Influence(pair.Key, pair.Value + (affected.TryGetValue(pair.Key, out same) ? same : 0f)));
        }
        float affectedTotal = 0f;
        foreach (KeyValuePair<int, float> pair in affected)
        {
            if (fixedWeights.ContainsKey(pair.Key) || pair.Value <= 0f) continue;
            candidates.Add(new Influence(pair.Key, pair.Value));
            affectedTotal += pair.Value;
        }
        int available = 4 - locked.Count;
        if (available < 0 || (available == 0 && candidates.Count > 0))
            throw new InvalidOperationException("[AnatomicalSkin] No four-weight budget remains for the affected source pool.");
        candidates.Sort((a, b) => b.weight.CompareTo(a.weight));
        compressed = candidates.Count > available;
        int keep = Mathf.Min(available, candidates.Count);
        float retained = 0f;
        for (int i = 0; i < keep; i++) retained += candidates[i].weight;
        // Four-weight mobile skinning cannot retain every interpolated neighbour.
        // Only the affected pool loses its weakest spatial contributions. Rescaling
        // that pool preserves its total exactly; untouched head/leg/finger weights
        // and their proportion of the original vertex are retained.
        float scale = retained > 0f ? affectedTotal / retained : 0f;
        for (int i = 0; i < keep; i++) locked.Add(new Influence(candidates[i].index, candidates[i].weight * scale));
        locked.Sort((a, b) => b.weight.CompareTo(a.weight));
        var result = new BoneWeight();
        for (int i = 0; i < locked.Count; i++) Assign(ref result, i, locked[i]);
        return result;
    }

    private static void AddVolumeCorrective(Mesh mesh, SkinnedMeshRenderer skin, Transform space,
        AnatomicalPatientRig.Limb limb, Vector3[] positions, float[] mask, bool shoulder,
        List<AnatomicalPatientRig.SkinCorrection> corrections)
    {
        Vector3 center = space.InverseTransformPoint((shoulder ? limb.humerus : limb.ulna).position);
        Vector3 end = space.InverseTransformPoint((shoulder ? limb.ulna : limb.hand).position);
        Vector3 axis = end - center;
        if (axis.sqrMagnitude < 1e-8f) return;
        float length = axis.magnitude;
        axis /= length;
        float width = length * (shoulder ? .34f : .22f);
        var deltas = new Vector3[positions.Length];
        int affected = 0;
        float maximum = 0f;
        for (int i = 0; i < positions.Length; i++)
        {
            if (mask[i] < .001f) continue;
            Vector3 d = positions[i] - center;
            float along = Vector3.Dot(d, axis);
            Vector3 radial = d - axis * along;
            float radius = radial.magnitude;
            if (radius < 1e-6f) continue;
            // Cross-section projection intentionally creates radial volume only; it
            // does not add longitudinal stretch or imitate sliding muscle fibres.
            float local = Mathf.Exp(-(along * along) / (width * width));
            float amount = Mathf.Min(radius * .10f, shoulder ? .007f : .004f) * local * Mathf.Clamp01(mask[i]);
            if (amount < .00001f) continue;
            Vector3 delta = radial * (amount / radius);
            deltas[i] = skin.transform.InverseTransformVector(space.TransformVector(delta));
            maximum = Mathf.Max(maximum, amount);
            affected++;
        }
        if (affected == 0) return;
        string baseName = "GC_Anatomy_" + (shoulder ? "ShoulderVolume_" : "ElbowVolume_") + (limb.left ? "L" : "R");
        string name = baseName;
        int suffix = 2;
        while (mesh.GetBlendShapeIndex(name) >= 0) name = baseName + "_" + suffix++;
        mesh.AddBlendShapeFrame(name, 100f, deltas, null, null);
        corrections.Add(new AnatomicalPatientRig.SkinCorrection
        {
            renderer = skin,
            blendShape = mesh.GetBlendShapeIndex(name),
            left = limb.left,
            trigger = shoulder ? AnatomicalPatientRig.CorrectionTrigger.Elevation : AnatomicalPatientRig.CorrectionTrigger.ElbowFlexion,
            startDegrees = 0f,
            fullDegrees = shoulder ? 60f : 100f
        });
        Debug.Log("[AnatomicalSkin] " + skin.name + " " + name + ": " + affected +
            " measured vertices; maximum local-volume expansion " + (maximum * 1000f).ToString("F2") +
            "mm; rest weight zero. This small corrective is not clinically validated.");
    }

    private static Bounds MotionBounds(SkinnedMeshRenderer skin, Transform space, Vector3[] points,
        float[][] armMasks, AnatomicalPatientRig.Limb[] limbs)
    {
        Bounds bounds = skin.localBounds;
        for (int a = 0; a < limbs.Length; a++)
        {
            Vector3 shoulder = space.InverseTransformPoint(limbs[a].humerus.position);
            float radius = 0f;
            for (int v = 0; v < points.Length; v++)
            {
                if (armMasks[a][v] > .05f) radius = Mathf.Max(radius, Vector3.Distance(shoulder, points[v]));
            }
            if (radius <= 0f) continue;
            // A measured shoulder-centred reach sphere is a conservative culling
            // bound, not a movement clamp. It includes overhead/backward motion
            // which was absent from the original imported animation bounds.
            Vector3 medial = space.InverseTransformPoint(limbs[a].clavicle.position);
            // Two clavicular lengths cover displacement from one orientation to
            // any other; 10mm covers the bounded volume corrective.
            radius += 2f * Vector3.Distance(medial, shoulder) + .01f;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 p = shoulder + new Vector3((corner & 1) == 0 ? -radius : radius,
                    (corner & 2) == 0 ? -radius : radius, (corner & 4) == 0 ? -radius : radius);
                bounds.Encapsulate(skin.transform.InverseTransformPoint(space.TransformPoint(p)));
            }
        }
        return bounds;
    }

    private static void RequireLimb(AnatomicalPatientRig.Limb limb)
    {
        if (limb == null || limb.sourceUpper == null || limb.sourceLower == null || limb.sourceHand == null ||
            limb.sourceClavicle == null || limb.clavicle == null || limb.scapula == null || limb.humerus == null ||
            limb.ulna == null || limb.radius == null || limb.hand == null)
            throw new ArgumentException("[AnatomicalSkin] Incomplete measured limb definition.");
    }

    private static int AddBone(Transform bone, List<Transform> bones, Dictionary<Transform, int> indices)
    {
        if (bone == null) throw new ArgumentException("[AnatomicalSkin] Null output bone.");
        int index;
        if (indices.TryGetValue(bone, out index)) return index;
        index = bones.Count;
        bones.Add(bone);
        indices.Add(bone, index);
        return index;
    }

    private static void Add(Dictionary<int, float> weights, int bone, float value)
    {
        if (value <= 0f) return;
        float old;
        weights.TryGetValue(bone, out old);
        weights[bone] = old + value;
    }

    private static float Smooth(float start, float end, float value)
    {
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(start, end, value));
    }

    private static int Index(BoneWeight weight, int slot)
    {
        switch (slot) { case 0: return weight.boneIndex0; case 1: return weight.boneIndex1; case 2: return weight.boneIndex2; default: return weight.boneIndex3; }
    }

    private static float Weight(BoneWeight weight, int slot)
    {
        switch (slot) { case 0: return weight.weight0; case 1: return weight.weight1; case 2: return weight.weight2; default: return weight.weight3; }
    }

    private static void Assign(ref BoneWeight weight, int slot, Influence influence)
    {
        switch (slot)
        {
            case 0: weight.boneIndex0 = influence.index; weight.weight0 = influence.weight; break;
            case 1: weight.boneIndex1 = influence.index; weight.weight1 = influence.weight; break;
            case 2: weight.boneIndex2 = influence.index; weight.weight2 = influence.weight; break;
            case 3: weight.boneIndex3 = influence.index; weight.weight3 = influence.weight; break;
        }
    }

    private static string SafeName(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
        return value.Replace('/', '_').Replace('\\', '_');
    }

    private static void EnsureAssetFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder) || (folder != "Assets" && !folder.StartsWith("Assets/", StringComparison.Ordinal)))
            throw new ArgumentException("[AnatomicalSkin] Output folder must be inside Assets.");
        string[] parts = folder.TrimEnd('/').Split('/');
        string current = "Assets";
        for (int i = 1; i < parts.Length; i++)
        {
            if (parts[i] == ".." || parts[i].Length == 0) throw new ArgumentException("[AnatomicalSkin] Invalid output folder.");
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
