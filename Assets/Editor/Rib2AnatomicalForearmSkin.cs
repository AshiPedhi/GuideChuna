using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// V03 private-preview skin only: replace the selected ulna/radius weight pool
/// by adjacent distributed-twist bones. No asset, rig input, hand pose, mesh
/// vertex/morph data or original bind pose is changed. The private pooled
/// vertices reproduce the source renderer's effective top-N normalized weights;
/// active non-forearm ratios are retained, and source-inactive slots are omitted
/// only from that private copy. The original mesh/palette remain recoverable.
/// </summary>
public sealed class Rib2AnatomicalForearmSkin : IDisposable
{
    private sealed class Skin
    {
        public SkinnedMeshRenderer renderer;
        public Mesh originalMesh, ownedMesh;
        public Transform[] originalBones, ownedBones;
        public BoneWeight[] weights;
        public float[] morphWeights;
        public SkinQuality quality;
        public int effectiveBudget;
        public Bounds bounds;
        public bool usingOwned;
        public bool budgetPending, budgetLogged;
        public Vector3 budgetOriginalInput, budgetOriginalBake;
    }

    private struct Influence
    {
        public int bone;
        public float weight;
        public Influence(int bone, float weight) { this.bone = bone; this.weight = weight; }
    }

    private readonly List<Skin> skins = new List<Skin>();
    private readonly Transform ulna, radius, hand, rigSpace;
    private readonly int intervals;
    private GameObject boneHolder;
    private Transform[] twistBones = Array.Empty<Transform>();
    private float lastRollDegrees;
    private Vector3 constructorElbow, constructorWrist;
    private bool loggedMovedZeroRoll;
    private bool enabled, disposed;

    public bool Enabled => enabled && !disposed;
    public int ChangedVertexCount { get; private set; }
    public int OwnedMeshCount => disposed ? 0 : skins.Count;
    public int TwistBoneCount => disposed ? 0 : twistBones.Length;
    public float MaximumWeightSumError { get; private set; }
    public int SingleBoneFallbackVertexCount { get; private set; }
    public int EffectiveInfluenceBudget { get; private set; } = 4;
    public int OmittedInactiveInfluenceCount { get; private set; }
    public float MaximumEffectiveNonForearmWeightError { get; private set; }

    /// <summary>The supplied rig must already have evaluated its exact author RestPose.</summary>
    public Rib2AnatomicalForearmSkin(AnatomicalPatientRig rig, bool left, int intervals = 12)
    {
        try
        {
            if (rig == null) throw new ArgumentNullException(nameof(rig));
            if (!rig.gameObject.scene.IsValid() || !EditorSceneManager.IsPreviewScene(rig.gameObject.scene))
                throw new InvalidOperationException("Distributed forearm skin is restricted to a private preview scene.");
            if (intervals < 1 || intervals > 128) throw new ArgumentOutOfRangeException(nameof(intervals));
            this.intervals = intervals;
            rigSpace = rig.RigSpace;
            if (rigSpace == null || rigSpace.localToWorldMatrix.determinant <= 0f)
                throw new InvalidOperationException("Forearm skin requires the rig's positive-scale output space.");
            var current = rig.CurrentPose; var rest = rig.RestPose;
            if (!SameArm(current.leftArm, rest.leftArm) || !SameArm(current.rightArm, rest.rightArm) || !current.neckDegrees.Equals(rest.neckDegrees))
                throw new InvalidOperationException("Construct forearm skin immediately after rig.Evaluate(rig.RestPose).");

            var limbs = new SerializedObject(rig).FindProperty("limbs");
            SerializedProperty selected = null;
            for (int i = 0; i < limbs.arraySize; i++)
                if (limbs.GetArrayElementAtIndex(i).FindPropertyRelative("left").boolValue == left)
                {
                    if (selected != null) throw new InvalidOperationException("Duplicate forearm side in the anatomical rig.");
                    selected = limbs.GetArrayElementAtIndex(i);
                }
            if (selected == null) throw new InvalidOperationException("Selected forearm is absent from the anatomical rig.");
            ulna = selected.FindPropertyRelative("ulna").objectReferenceValue as Transform;
            radius = selected.FindPropertyRelative("radius").objectReferenceValue as Transform;
            hand = selected.FindPropertyRelative("hand").objectReferenceValue as Transform;
            if (ulna == null || radius == null || hand == null) throw new InvalidOperationException("Selected forearm outputs are incomplete.");
            Vector3 restElbow = rigSpace.TransformPoint(selected.FindPropertyRelative("elbow").vector3Value);
            Vector3 restWrist = rigSpace.TransformPoint(selected.FindPropertyRelative("wrist").vector3Value);
            constructorElbow = restElbow; constructorWrist = restWrist;
            if (Vector3.Distance(ulna.position, restElbow) > .00001f || Vector3.Distance(hand.position, restWrist) > .00001f)
                throw new InvalidOperationException("Forearm output positions are not at their stored author rest.");
            if ((restWrist - restElbow).sqrMagnitude < 1e-10f) throw new InvalidOperationException("Forearm rest axis is degenerate.");

            foreach (var renderer in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // C9 visual skin/clothing only; anatomical muscles/bones remain untouched.
                if (renderer.name != "CC_Base_Body" && renderer.name != "Shirt") continue;
                BuildSkinPlan(renderer);
            }
            if (skins.Count != 0)
            {
                boneHolder = new GameObject("V03 private distributed forearm skin bones") { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(boneHolder, rig.gameObject.scene);
                boneHolder.transform.SetParent(rigSpace, false);
                twistBones = new Transform[intervals + 1];
                for (int i = 0; i < twistBones.Length; i++)
                {
                    var bone = new GameObject("V03 skin twist " + i) { hideFlags = HideFlags.HideAndDontSave };
                    SceneManager.MoveGameObjectToScene(bone, rig.gameObject.scene);
                    bone.transform.SetParent(boneHolder.transform, false);
                    bone.transform.SetPositionAndRotation(Vector3.Lerp(restElbow, restWrist, i / (float)intervals), ulna.rotation);
                    twistBones[i] = bone.transform;
                }
                foreach (Skin skin in skins) CreateOwnedMesh(skin);
            }
            SetEnabled(true);
            Apply(0f);
            LogSkinMatrixDiagnostics("constructor-rest");
            Debug.Log("[Rib2ForearmSkin] Private " + (left ? "left" : "right") + " Body/Shirt skin only; intervals=" + intervals +
                ", twist bones=" + TwistBoneCount + ", meshes=" + OwnedMeshCount + ", changed vertices=" + ChangedVertexCount +
                ", effective influence budget=" + EffectiveInfluenceBudget + ", nearest-one-bone budget fallbacks=" + SingleBoneFallbackVertexCount +
                ", source-inactive slots omitted only on private pooled vertices=" + OmittedInactiveInfluenceCount + ", maximum weight sum error=" + MaximumWeightSumError.ToString("R") +
                ", maximum active non-forearm ratio error=" + MaximumEffectiveNonForearmWeightError.ToString("R") +
                ". Original assets/palette, visible active non-forearm ratios, hand pose, morphs, quality and bounds preserved; source-inactive private slots intentionally omitted.");
        }
        catch { Dispose(); throw; }
    }

    private void BuildSkinPlan(SkinnedMeshRenderer renderer)
    {
        Mesh mesh = renderer.sharedMesh;
        if (mesh == null) return;
        Transform[] palette = renderer.bones;
        int ulnaIndex = Array.IndexOf(palette, ulna), radiusIndex = Array.IndexOf(palette, radius);
        if (ulnaIndex < 0 && radiusIndex < 0) return;
        BoneWeight[] original = mesh.boneWeights;
        if (original.Length == 0) return;
        if (original.Length != mesh.vertexCount || mesh.bindposes.Length != palette.Length)
            throw new InvalidOperationException("Incomplete anatomical skin weights/bindposes: " + renderer.name);
        int globalBudget = Mathf.Clamp((int)QualitySettings.skinWeights, 1, 4);
        int rendererBudget = renderer.quality == SkinQuality.Auto ? globalBudget : Mathf.Clamp((int)renderer.quality, 1, 4);
        int budget = Mathf.Min(rendererBudget, globalBudget);
        var rewritten = new BoneWeight[original.Length];
        var retained = new Influence[4]; int changed = 0;
        for (int v = 0; v < original.Length; v++)
        {
            BoneWeight value = original[v]; float pool = 0f, radial = 0f, sum = 0f, activeTotal = 0f; int count = 0;
            for (int slot = 0; slot < 4; slot++)
            {
                int index = BoneIndex(value, slot); float weight = Weight(value, slot);
                if (float.IsNaN(weight) || float.IsInfinity(weight) || weight < 0f || (weight > 0f && (index < 0 || index >= palette.Length)))
                    throw new InvalidOperationException("Invalid forearm input influence in " + renderer.name + " vertex " + v);
                // Preserve the existing native slot order. Unrelated C9 skin
                // vertices can have unsorted legacy slots; reordering their
                // first-N active influences would change the displayed shape.
                sum += weight;
                if (weight <= 0f || slot >= budget) continue;
                activeTotal += weight;
                if (index == ulnaIndex || index == radiusIndex) { pool += weight; if (index == radiusIndex) radial += weight; }
                else retained[count++] = new Influence(index, weight);
            }
            if (Mathf.Abs(sum - 1f) > .001f) throw new InvalidOperationException("Input skin weights do not sum to one: " + renderer.name + " vertex " + v);
            if (pool <= 0f) { rewritten[v] = value; MaximumWeightSumError = Mathf.Max(MaximumWeightSumError, Mathf.Abs(sum - 1f)); continue; }
            if (activeTotal <= 0f) throw new InvalidOperationException("The active source skin weight total is zero.");
            // BakeMesh was measured to clamp even explicit Bone4 to global
            // TwoBones. Preserve its normalized active source, not inactive
            // managed slots which would displace a visible humerus/hand weight.
            for (int i = 0; i < count; i++) retained[i].weight /= activeTotal;
            float normalizedPool = pool / activeTotal;
            for (int slot = budget; slot < 4; slot++) if (Weight(value, slot) > 0f) OmittedInactiveInfluenceCount++;

            float coordinate = Mathf.Clamp01(radial / pool) * intervals;
            int lower = Mathf.Min(Mathf.FloorToInt(coordinate), intervals), upper = Mathf.Min(lower + 1, intervals);
            float upperWeight = lower == upper ? 0f : normalizedPool * (coordinate - lower);
            float lowerWeight = normalizedPool - upperWeight;
            int added = (lowerWeight > 0f ? 1 : 0) + (upperWeight > 0f ? 1 : 0);
            if (count + added > budget)
            {
                if (count >= budget) throw new InvalidOperationException("No effective renderer slot remains for a preserved forearm pool.");
                retained[count++] = new Influence(palette.Length + Mathf.RoundToInt(coordinate), normalizedPool);
                SingleBoneFallbackVertexCount++;
            }
            else
            {
                if (lowerWeight > 0f) retained[count++] = new Influence(palette.Length + lower, lowerWeight);
                if (upperWeight > 0f) retained[count++] = new Influence(palette.Length + upper, upperWeight);
            }
            // Sort inside the measured budget so every retained contribution is visible.
            for (int i = 1; i < count; i++)
            {
                Influence next = retained[i]; int j = i - 1;
                while (j >= 0 && retained[j].weight < next.weight) { retained[j + 1] = retained[j]; j--; }
                retained[j + 1] = next;
            }
            BoneWeight result = default; float newSum = 0f;
            for (int i = 0; i < count; i++) { Assign(ref result, i, retained[i]); newSum += retained[i].weight; }
            MaximumWeightSumError = Mathf.Max(MaximumWeightSumError, Mathf.Abs(newSum - 1f));
            if (Mathf.Abs(newSum - 1f) > .000001f) throw new InvalidOperationException("Private effective source weights do not sum to one in " + renderer.name + " vertex " + v);
            for (int slot = 0; slot < budget; slot++)
            {
                int index = BoneIndex(value, slot);
                if (Weight(value, slot) <= 0f || index == ulnaIndex || index == radiusIndex) continue;
                float expected = 0f;
                for (int sourceSlot = 0; sourceSlot < budget; sourceSlot++) if (BoneIndex(value, sourceSlot) == index) expected += Weight(value, sourceSlot) / activeTotal;
                float error = Mathf.Abs(expected - SumBone(result, index));
                MaximumEffectiveNonForearmWeightError = Mathf.Max(MaximumEffectiveNonForearmWeightError, error);
                if (error > .000001f) throw new InvalidOperationException("Visible active non-forearm ratio changed in " + renderer.name + " vertex " + v);
            }
            rewritten[v] = result; changed++;
        }
        if (changed == 0) return;
        EffectiveInfluenceBudget = Mathf.Min(EffectiveInfluenceBudget, budget);
        ChangedVertexCount += changed;
        skins.Add(new Skin { renderer = renderer, originalMesh = mesh, originalBones = palette, weights = rewritten,
            morphWeights = new float[mesh.blendShapeCount], quality = renderer.quality, effectiveBudget = budget, bounds = renderer.localBounds });
    }

    private void CreateOwnedMesh(Skin skin)
    {
        skin.ownedBones = new Transform[skin.originalBones.Length + twistBones.Length];
        Array.Copy(skin.originalBones, skin.ownedBones, skin.originalBones.Length);
        Array.Copy(twistBones, 0, skin.ownedBones, skin.originalBones.Length, twistBones.Length);
        skin.ownedMesh = UnityEngine.Object.Instantiate(skin.originalMesh);
        skin.ownedMesh.name = skin.originalMesh.name + " — V03 private forearm skin";
        skin.ownedMesh.hideFlags = HideFlags.HideAndDontSave;
        Matrix4x4[] originalBindposes = skin.originalMesh.bindposes;
        var bindposes = new Matrix4x4[skin.ownedBones.Length];
        Array.Copy(originalBindposes, bindposes, originalBindposes.Length);
        for (int i = 0; i < twistBones.Length; i++) bindposes[originalBindposes.Length + i] = twistBones[i].worldToLocalMatrix * skin.renderer.localToWorldMatrix;
        skin.ownedMesh.bindposes = bindposes; skin.ownedMesh.boneWeights = skin.weights;
        if (skin.ownedMesh.vertexCount != skin.originalMesh.vertexCount || skin.ownedMesh.blendShapeCount != skin.originalMesh.blendShapeCount)
            throw new InvalidOperationException("Private forearm mesh topology or morph count changed unexpectedly.");
    }

    /// <summary>Call after every native rig Evaluate, using its unchanged forearm roll.</summary>
    public void Apply(float rollDegrees)
    {
        RequireAlive();
        if (float.IsNaN(rollDegrees) || float.IsInfinity(rollDegrees)) throw new ArgumentOutOfRangeException(nameof(rollDegrees));
        lastRollDegrees = rollDegrees;
        if (!enabled || twistBones.Length == 0) return;
        Vector3 elbow = ulna.position, wrist = hand.position, axis = wrist - elbow;
        if (axis.sqrMagnitude < 1e-10f) throw new InvalidOperationException("Current forearm axis is degenerate.");
        axis.Normalize(); Quaternion orientation = ulna.rotation;
        for (int i = 0; i < twistBones.Length; i++)
        {
            float fraction = i / (float)intervals;
            twistBones[i].SetPositionAndRotation(Vector3.Lerp(elbow, wrist, fraction), Quaternion.AngleAxis(rollDegrees * fraction, axis) * orientation);
        }
        if (!loggedMovedZeroRoll && Mathf.Abs(rollDegrees) < .000001f &&
            ((elbow - constructorElbow).sqrMagnitude > 1e-8f || (wrist - constructorWrist).sqrMagnitude > 1e-8f))
        {
            loggedMovedZeroRoll = true;
            LogSkinMatrixDiagnostics("first-moved-zero-roll");
        }
    }

    // One-shot measurements only; these do not alter transforms or mesh data.
    private void LogSkinMatrixDiagnostics(string phase)
    {
        foreach (Skin skin in skins)
        {
            int ui = Array.IndexOf(skin.originalBones, ulna), ri = Array.IndexOf(skin.originalBones, radius);
            Matrix4x4[] originalBind = skin.originalMesh.bindposes, ownedBind = skin.ownedMesh.bindposes;
            Matrix4x4 rendererInverse = skin.renderer.worldToLocalMatrix;
            Matrix4x4 um = ui >= 0 ? rendererInverse * ulna.localToWorldMatrix * originalBind[ui] : Matrix4x4.identity;
            Matrix4x4 rm = ri >= 0 ? rendererInverse * radius.localToWorldMatrix * originalBind[ri] : Matrix4x4.identity;
            var virtualMatrices = new Matrix4x4[twistBones.Length];
            for (int i = 0; i < twistBones.Length; i++) virtualMatrices[i] = rendererInverse * twistBones[i].localToWorldMatrix * ownedBind[skin.originalBones.Length + i];
            BoneWeight[] originalWeights = skin.originalMesh.boneWeights; Vector3[] vertices = skin.originalMesh.vertices;
            float maximum = 0f; int offending = -1;
            for (int v = 0; v < vertices.Length; v++)
            {
                float uw = ui >= 0 ? ActiveNormalizedBone(originalWeights[v], ui, skin.effectiveBudget) : 0f;
                float rw = ri >= 0 ? ActiveNormalizedBone(originalWeights[v], ri, skin.effectiveBudget) : 0f;
                if (uw + rw <= 0f) continue;
                Vector3 oldPoint = um.MultiplyPoint3x4(vertices[v]) * uw + rm.MultiplyPoint3x4(vertices[v]) * rw;
                Vector3 newPoint = Vector3.zero;
                for (int slot = 0; slot < 4; slot++)
                {
                    int index = BoneIndex(skin.weights[v], slot) - skin.originalBones.Length;
                    if (index >= 0 && index < virtualMatrices.Length) newPoint += virtualMatrices[index].MultiplyPoint3x4(vertices[v]) * Weight(skin.weights[v], slot);
                }
                float error = skin.renderer.localToWorldMatrix.MultiplyVector(newPoint - oldPoint).magnitude;
                if (error > maximum) { maximum = error; offending = v; }
            }
            Matrix4x4 first = virtualMatrices[0], middle = virtualMatrices[virtualMatrices.Length / 2], last = virtualMatrices[virtualMatrices.Length - 1];
            Debug.Log("[Rib2ForearmSkinMatrix] " + phase + " " + skin.renderer.name +
                " oldUlna(identityMax=" + MatrixDifference(um, Matrix4x4.identity).ToString("R") + ",det=" + um.determinant.ToString("R") + ",translation=" + ((Vector3)um.GetColumn(3)).ToString("F7") +
                ") oldRadius(identityMax=" + MatrixDifference(rm, Matrix4x4.identity).ToString("R") + ",det=" + rm.determinant.ToString("R") + ",translation=" + ((Vector3)rm.GetColumn(3)).ToString("F7") +
                ") virtualStart/Mid/EndIdentityMax=" + MatrixDifference(first, Matrix4x4.identity).ToString("R") + "/" + MatrixDifference(middle, Matrix4x4.identity).ToString("R") + "/" + MatrixDifference(last, Matrix4x4.identity).ToString("R") +
                " oldUlnaVsVirtualStartMax=" + MatrixDifference(um, first).ToString("R") + " oldRadiusVsVirtualEndMax=" + MatrixDifference(rm, last).ToString("R") +
                " effectiveTopN=" + skin.effectiveBudget + " predictedActiveNormalizedPoolMaximumWorldDisplacement=" + maximum.ToString("R") + "m vertex=" + offending + "; diagnostic only.");
        }
    }

    private static float MatrixDifference(Matrix4x4 a, Matrix4x4 b)
    {
        float maximum = 0f;
        for (int row = 0; row < 4; row++) for (int column = 0; column < 4; column++) maximum = Mathf.Max(maximum, Mathf.Abs(a[row, column] - b[row, column]));
        return maximum;
    }

    private static Vector3 BakeRawDiagnosticVertex(Skin skin, int vertex)
    {
        // Isolate skinning from morph deltas for this one-shot diagnostic only.
        // Every current morph value is restored before returning, including on failure.
        int count = skin.renderer.sharedMesh.blendShapeCount;
        var saved = new float[count];
        for (int i = 0; i < count; i++) saved[i] = skin.renderer.GetBlendShapeWeight(i);
        var mesh = new Mesh { name = "Private raw forearm skinning diagnostic", hideFlags = HideFlags.HideAndDontSave };
        try
        {
            for (int i = 0; i < count; i++) skin.renderer.SetBlendShapeWeight(i, 0f);
            skin.renderer.BakeMesh(mesh);
            return skin.renderer.transform.TransformPoint(mesh.vertices[vertex]);
        }
        finally
        {
            for (int i = 0; i < count; i++) skin.renderer.SetBlendShapeWeight(i, saved[i]);
            UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    private static string SkinningBudgetPredictions(Skin skin, Mesh mesh, Transform[] palette, Vector3 input, Vector3 actualWorld, int vertex)
    {
        // These arrays are non-owning native mesh views, as in AnatomicalSkinBuilder.
        var counts = mesh.GetBonesPerVertex(); var all = mesh.GetAllBoneWeights();
        int offset = 0; for (int i = 0; i < vertex; i++) offset += counts[i];
        int count = counts[vertex]; var influences = new BoneWeight1[count];
        var mapped = new Vector3[count]; Matrix4x4[] bindposes = mesh.bindposes;
        Matrix4x4 inverse = skin.renderer.worldToLocalMatrix;
        var output = new System.Text.StringBuilder(" nativeCount=" + count + " influences=");
        for (int i = 0; i < count; i++)
        {
            influences[i] = all[offset + i];
            int index = influences[i].boneIndex;
            mapped[i] = (inverse * palette[index].localToWorldMatrix * bindposes[index]).MultiplyPoint3x4(input);
            output.Append(palette[index].name).Append(':').Append(influences[i].weight.ToString("R")).Append(';');
        }
        foreach (int limit in new[] { 1, 2, 4 })
        {
            int keep = Mathf.Min(count, limit); float sum = 0f; Vector3 raw = Vector3.zero;
            for (int i = 0; i < keep; i++) { sum += influences[i].weight; raw += mapped[i] * influences[i].weight; }
            Vector3 rawWorld = skin.renderer.transform.TransformPoint(raw);
            Vector3 normalizedWorld = skin.renderer.transform.TransformPoint(sum > 0f ? raw / sum : raw);
            output.Append(" top").Append(limit).Append("(sum=").Append(sum.ToString("R"))
                .Append(",rawErrorMm=").Append((1000f * Vector3.Distance(rawWorld, actualWorld)).ToString("R"))
                .Append(",normalizedErrorMm=").Append((1000f * Vector3.Distance(normalizedWorld, actualWorld)).ToString("R")).Append(')');
        }
        if (count >= 2)
        {
            Vector3 complement = mapped[0] * influences[0].weight + mapped[1] * (1f - influences[0].weight);
            output.Append(" top2ComplementErrorMm=").Append((1000f * Vector3.Distance(skin.renderer.transform.TransformPoint(complement), actualWorld)).ToString("R"));
        }
        return output.ToString();
    }

    private void LogPendingBudgetDiagnostics()
    {
        foreach (Skin skin in skins)
        {
            if (!skin.budgetPending) continue;
            const int vertex = 6808;
            Vector3 input = skin.ownedMesh.vertices[vertex];
            Vector3 actual = BakeRawDiagnosticVertex(skin, vertex);
            Debug.Log("[Rib2ForearmSkinBudget] vertex=" + vertex + " renderer=" + skin.renderer.name + " rendererQuality=" + skin.renderer.quality +
                " globalSkinWeights=" + QualitySettings.skinWeights + "; morphs temporarily zeroed for each raw bake and restored; originalNativeWorld=" + skin.budgetOriginalBake.ToString("F7") +
                " privateNativeWorld=" + actual.ToString("F7") + " nativeDifferenceMm=" + (1000f * Vector3.Distance(actual, skin.budgetOriginalBake)).ToString("R") +
                " inputVertexDifferenceMm=" + (1000f * Vector3.Distance(input, skin.budgetOriginalInput)).ToString("R") +
                "; ORIGINAL" + SkinningBudgetPredictions(skin, skin.originalMesh, skin.originalBones, skin.budgetOriginalInput, skin.budgetOriginalBake, vertex) +
                "; PRIVATE" + SkinningBudgetPredictions(skin, skin.ownedMesh, skin.ownedBones, input, actual, vertex));
            skin.budgetPending = false; skin.budgetLogged = true;
        }
    }

    public void SetEnabled(bool value)
    {
        RequireAlive();
        foreach (Skin skin in skins)
        {
            if (skin.renderer == null || skin.usingOwned == value) continue;
            if (skin.renderer.sharedMesh != skin.originalMesh && skin.renderer.sharedMesh != skin.ownedMesh)
                throw new InvalidOperationException("Another tool replaced this preview skin mesh; refusing to overwrite it.");
            for (int i = 0; i < skin.morphWeights.Length; i++) skin.morphWeights[i] = skin.renderer.GetBlendShapeWeight(i);
            if (value && !skin.budgetLogged && skin.renderer.name == "CC_Base_Body" && skin.originalMesh.vertexCount > 6808 && Mathf.Abs(lastRollDegrees) < .000001f &&
                ((ulna.position - constructorElbow).sqrMagnitude > 1e-8f || (hand.position - constructorWrist).sqrMagnitude > 1e-8f))
            {
                skin.budgetOriginalInput = skin.originalMesh.vertices[6808];
                skin.budgetOriginalBake = BakeRawDiagnosticVertex(skin, 6808);
                skin.budgetPending = true;
            }
            try
            {
                skin.renderer.sharedMesh = value ? skin.ownedMesh : skin.originalMesh;
                skin.renderer.bones = value ? skin.ownedBones : skin.originalBones;
                skin.renderer.quality = skin.quality; skin.renderer.localBounds = skin.bounds;
                for (int i = 0; i < skin.morphWeights.Length; i++) skin.renderer.SetBlendShapeWeight(i, skin.morphWeights[i]);
                skin.usingOwned = value;
            }
            catch
            {
                skin.renderer.sharedMesh = skin.originalMesh; skin.renderer.bones = skin.originalBones;
                skin.renderer.quality = skin.quality; skin.renderer.localBounds = skin.bounds;
                for (int i = 0; i < skin.morphWeights.Length; i++) skin.renderer.SetBlendShapeWeight(i, skin.morphWeights[i]);
                skin.usingOwned = false; throw;
            }
        }
        enabled = value;
        if (value && twistBones.Length != 0) Apply(lastRollDegrees);
        if (value) LogPendingBudgetDiagnostics();
    }

    public void Dispose()
    {
        if (disposed) return;
        try { SetEnabled(false); }
        finally
        {
            foreach (Skin skin in skins) if (skin.ownedMesh != null) UnityEngine.Object.DestroyImmediate(skin.ownedMesh);
            if (boneHolder != null) UnityEngine.Object.DestroyImmediate(boneHolder);
            disposed = true; enabled = false;
        }
    }

    private void RequireAlive() { if (disposed) throw new ObjectDisposedException(nameof(Rib2AnatomicalForearmSkin)); }
    private static bool SameArm(AnatomicalPatientRig.ArmPose a, AnatomicalPatientRig.ArmPose b)
    { return a.shoulderElevation == b.shoulderElevation && a.elevationPlane == b.elevationPlane && a.humeralRotation == b.humeralRotation && a.elbowFlexion == b.elbowFlexion &&
        a.forearmRotation == b.forearmRotation && a.wristFlexion == b.wristFlexion && a.wristDeviation == b.wristDeviation && a.shrug == b.shrug && a.protraction == b.protraction; }
    private static int BoneIndex(BoneWeight value, int slot)
    { switch (slot) { case 0: return value.boneIndex0; case 1: return value.boneIndex1; case 2: return value.boneIndex2; default: return value.boneIndex3; } }
    private static float Weight(BoneWeight value, int slot)
    { switch (slot) { case 0: return value.weight0; case 1: return value.weight1; case 2: return value.weight2; default: return value.weight3; } }
    private static float SumBone(BoneWeight value, int index)
    { float sum = 0f; for (int i = 0; i < 4; i++) if (BoneIndex(value, i) == index) sum += Weight(value, i); return sum; }
    private static float ActiveNormalizedBone(BoneWeight value, int index, int budget)
    {
        float sum = 0f, selected = 0f;
        for (int i = 0; i < budget; i++) { float weight = Weight(value, i); sum += weight; if (BoneIndex(value, i) == index) selected += weight; }
        return sum > 0f ? selected / sum : 0f;
    }
    private static void Assign(ref BoneWeight value, int slot, Influence influence)
    {
        switch (slot)
        {
            case 0: value.boneIndex0 = influence.bone; value.weight0 = influence.weight; break;
            case 1: value.boneIndex1 = influence.bone; value.weight1 = influence.weight; break;
            case 2: value.boneIndex2 = influence.bone; value.weight2 = influence.weight; break;
            case 3: value.boneIndex3 = influence.bone; value.weight3 = influence.weight; break;
        }
    }
}
