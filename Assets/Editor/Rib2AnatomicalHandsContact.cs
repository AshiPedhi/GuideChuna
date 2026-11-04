using System;
using System.Collections.Generic;
using Oculus.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Private-preview hand geometry and contact frames. Source SDK assets and the
/// anatomical input skeleton are read only. Fingers are authored hypotheses;
/// surface points below are measured from the posed cloned meshes, not force.
/// </summary>
public static class Rib2AnatomicalHandsContact
{
    private const string LeftGuid = "70d02a90551f23042a882cdceeaf8e3a";
    private const string RightGuid = "34a22dd67c1e5344591237fdd61e78ec";
    private static readonly int[] FingerProximals = { 7, 12, 17, 22 };
    private static readonly float[] CollisionMcpAngles = { -30f, -15f, 0f, 15f, 30f };
    private static readonly float[] CollisionPipAngles = { -20f, 0f, 20f, 40f };
    private static readonly float[] CollisionRefinement = { -5f, 0f, 5f };

    public struct Surface
    {
        public Vector3 point, normal;
        public float distanceFromLandmark;
        public string renderer;
        public int vertexIndex;
    }

    [Serializable]
    public struct JointIntrusion
    {
        public int testedJointCount, intrudingJointCount;
        public float maximumInsetMetres;
    }

    [Serializable]
    public struct FingerCollisionResult
    {
        public int testedVertices, beforeIntrudingVertices, afterIntrudingVertices, adjustedFingers;
        public float beforeMaximumInsetMetres, afterMaximumInsetMetres;
        public float rootDisplacementMetres, wristDisplacementMetres;
    }

    [Serializable]
    public struct SurfaceContact
    {
        public float gapMetres, maxInsetMetres;
        public float reciprocalMaxInsetMetres;
        public Vector3 guidePoint, patientPoint;
        public int testedCount, reciprocalTestedCount;
    }

    public sealed class MeshGeometry
    {
        public Vector3[] vertices, normals;
        public int[] triangles, vertexIds;
    }

    /// <summary>Owns a cloned SDK hand and its private material/bake resources.</summary>
    public sealed class Hand : IDisposable
    {
        public readonly GameObject Object;
        public readonly Transform Root;
        public readonly HandTransformMapper Mapper;
        public readonly SkinnedMeshRenderer[] Renderers;
        public readonly bool Left;
        public Vector3 FingerDirectionWorld => Root.TransformDirection(distalLocal).normalized;
        public Vector3 PalmNormalWorld => Root.TransformDirection(palmarLocal).normalized;
        public Vector3 RadialWorld => Root.TransformDirection(radialLocal).normalized;
        public Vector3 PalmSurfaceWorld => SurfaceWorld(palmSkin, palmVertex, palmSurfaceLocal);
        public Vector3 UlnarHeelSurfaceWorld => SurfaceWorld(heelSkin, heelVertex, ulnarHeelSurfaceLocal);
        public Vector3 WristWorld => joints[1].position;
        public float LastSurfaceProbeDistance { get; private set; }
        private readonly Transform[] joints = new Transform[26];
        private readonly Vector3[] positions = new Vector3[26];
        private readonly Quaternion[] rotations = new Quaternion[26];
        private readonly Vector3[] curlAxesParent = new Vector3[26];
        private readonly Quaternion[] authoredRotations = new Quaternion[26];
        private readonly List<SkinSample>[] fingerSamples = { new List<SkinSample>(), new List<SkinSample>(), new List<SkinSample>(), new List<SkinSample>(), new List<SkinSample>() };
        private readonly List<GuideSkin> guideSkins = new List<GuideSkin>();
        private readonly Material material;
        private readonly Vector3 initialObjectScale;
        private readonly SurfaceProbe probe = new SurfaceProbe();
        private readonly TriangleSurface actualGuideSurface = new TriangleSurface();
        private Vector3 distalLocal, palmarLocal, radialLocal;
        private Vector3 palmSurfaceLocal, ulnarHeelSurfaceLocal;
        private Quaternion measuredFrameLocal;
        private GuideSkin palmSkin, heelSkin;
        private int palmVertex, heelVertex;
        private Vector3 thumbOppositionAxisParent;
        private bool disposed;

        internal Hand(bool left, Scene scene, Transform parent, Color color, int layer)
        {
            Left = left;
            try
            {
                if (!scene.IsValid() || !EditorSceneManager.IsPreviewScene(scene))
                    throw new InvalidOperationException("Contact hands may only be created in a private preview scene.");
                string path = AssetDatabase.GUIDToAssetPath(left ? LeftGuid : RightGuid);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null) throw new InvalidOperationException("Missing SDK hand: " + path);
                Object = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
                initialObjectScale = Object.transform.localScale;
                Object.name = left ? "Expert left — anatomical contact" : "Expert right — ulnar chest support";
                Object.hideFlags = HideFlags.HideAndDontSave;
                if (parent != null) Object.transform.SetParent(parent, false);
                foreach (Transform t in Object.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
                foreach (Behaviour behaviour in Object.GetComponentsInChildren<Behaviour>(true)) behaviour.enabled = false;
                foreach (Collider collider in Object.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                foreach (Rigidbody body in Object.GetComponentsInChildren<Rigidbody>(true))
                { body.isKinematic = true; body.useGravity = false; }
                HandVisual visual = Object.GetComponentInChildren<HandVisual>(true);
                if (visual == null) throw new InvalidOperationException("Missing SDK HandVisual.");
                var data = new SerializedObject(visual);
                Root = data.FindProperty("_openXRRoot").objectReferenceValue as Transform;
                var legacy = data.FindProperty("_root").objectReferenceValue as Transform;
                SerializedProperty array = data.FindProperty("_openXRJointTransforms");
                if (Root == null || array == null || array.arraySize != 26)
                    throw new InvalidOperationException("Expected measured OpenXR root and 26 joint references.");
                if (legacy != null && legacy != Root) legacy.gameObject.SetActive(false);
                Root.gameObject.SetActive(true);
                var list = new List<Transform>(26);
                for (int i = 0; i < 26; i++)
                {
                    joints[i] = array.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                    if (joints[i] == null) throw new InvalidOperationException("Missing OpenXR joint " + i);
                    positions[i] = joints[i].localPosition;
                    rotations[i] = joints[i].localRotation;
                    list.Add(joints[i]);
                }
                Mapper = Object.AddComponent<HandTransformMapper>();
                Mapper.enabled = false;
                var mapData = new SerializedObject(Mapper);
                mapData.FindProperty("copyJointsOnStart").boolValue = false;
                mapData.FindProperty("rootTransform").objectReferenceValue = Root;
                mapData.ApplyModifiedPropertiesWithoutUndo();
                Mapper.SetHandType(left);
                Mapper.SetJoints(list);
                Renderers = Root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (Renderers.Length == 0) throw new InvalidOperationException("SDK hand has no skinned mesh.");
                Shader shader = Shader.Find("Standard");
                if (shader == null || !shader.isSupported) throw new InvalidOperationException("Built-in Standard hand shader unavailable.");
                material = new Material(shader) { name = "Private anatomical guide material", hideFlags = HideFlags.HideAndDontSave, color = color };
                foreach (var renderer in Renderers)
                {
                    var slots = new Material[renderer.sharedMesh.subMeshCount];
                    for (int i = 0; i < slots.Length; i++) slots[i] = material;
                    renderer.sharedMaterials = slots;
                    renderer.enabled = true;
                    renderer.updateWhenOffscreen = true;
                }
                Vector3 distal = (joints[12].position - joints[1].position).normalized;
                Vector3 radial = joints[7].position - joints[22].position;
                // Remove the longitudinal component only to form a measured
                // orthonormal palm frame; positions are not projected/flattened.
                radial = Vector3.ProjectOnPlane(radial, distal).normalized;
                Vector3 palmar = (left ? Vector3.Cross(radial, distal) : Vector3.Cross(distal, radial)).normalized;
                distalLocal = Root.InverseTransformDirection(distal).normalized;
                radialLocal = Root.InverseTransformDirection(radial).normalized;
                palmarLocal = Root.InverseTransformDirection(palmar).normalized;
                measuredFrameLocal = Quaternion.LookRotation(distalLocal, -palmarLocal);
                for (int i = 2; i < 25; i++)
                {
                    if (i == 5 || i == 10 || i == 15 || i == 20) continue;
                    Vector3 direction = joints[i + 1].position - joints[i].position;
                    Vector3 axis = Vector3.Cross(direction.normalized, palmar);
                    if (axis.sqrMagnitude > .000001f)
                        curlAxesParent[i] = joints[i].parent.InverseTransformDirection(axis.normalized);
                }
                Vector3 thumbLong = joints[3].position - joints[2].position;
                Vector3 thumbAcross = joints[22].position - joints[2].position;
                Vector3 opposition = Vector3.Cross(thumbLong.normalized, thumbAcross.normalized);
                thumbOppositionAxisParent = joints[2].parent.InverseTransformDirection(opposition.normalized);
                BuildFingerSamples();
                SetGrip(.65f, .7f, false);
                Debug.Log("[Rib2AnatomicalContact] Private " + (left ? "left" : "right") + " hand; measured palm frame and posed mesh contact; no source Euler offset.");
            }
            catch { Dispose(); throw; }
        }

        /// <summary>Authoring controls, not recovered hidden finger motion.</summary>
        public void SetGrip(float curl, float thumbOpposition, bool chestSupport)
        {
            RequireAlive();
            // Remove authored overdrive beyond the normalized grip parameter.
            curl = Mathf.Clamp01(curl); thumbOpposition = Mathf.Clamp01(thumbOpposition);
            for (int i = 0; i < 26; i++)
            { joints[i].localPosition = positions[i]; joints[i].localRotation = rotations[i]; }
            for (int finger = 0; finger < 4; finger++)
            {
                int start = FingerProximals[finger];
                if (chestSupport)
                {
                    Flex(start, -12f * curl);
                    Flex(start + 1, 9f * curl);
                    Flex(start + 2, 3f * curl);
                    // Source support fingers form a narrower fan than the SDK
                    // open-hand rest. Retain measured bases and bend axes.
                    Vector3 current = joints[start + 1].position - joints[start].position;
                    Vector3 narrowed = Vector3.Lerp(current.normalized, FingerDirectionWorld, .4f).normalized;
                    joints[start].rotation = Quaternion.FromToRotation(current, narrowed) * joints[start].rotation;
                }
                else
                {
                    Flex(start, (54f + finger * 4f) * curl);
                    Flex(start + 1, (78f + finger * 2f) * curl);
                    Flex(start + 2, 48f * curl);
                }
            }
            joints[2].localRotation = Quaternion.AngleAxis(36f * thumbOpposition,
                thumbOppositionAxisParent) * rotations[2];
            Flex(3, (chestSupport ? 8f : 38f) * curl);
            Flex(4, (chestSupport ? 5f : 30f) * curl);
            for (int i = 0; i < 26; i++) authoredRotations[i] = joints[i].localRotation;
            RefreshSurfaceContacts();
        }

        /// <summary>Independent measured MCP opening and PIP/DIP curl, in degrees.</summary>
        public void SetGraspFamily(float mcpOpenDegrees, float pipCurlDegrees, float dipCurlDegrees, float thumbOppositionDegrees = 30f)
        {
            RequireAlive();
            if (!Finite(new Vector3(mcpOpenDegrees, pipCurlDegrees, dipCurlDegrees)) || float.IsNaN(thumbOppositionDegrees) || float.IsInfinity(thumbOppositionDegrees)) throw new ArgumentException("Grasp family angles must be finite.");
            for (int i = 0; i < 26; i++) { joints[i].localPosition = positions[i]; joints[i].localRotation = rotations[i]; }
            foreach (int start in FingerProximals)
            { Flex(start, -mcpOpenDegrees); Flex(start + 1, pipCurlDegrees); Flex(start + 2, dipCurlDegrees); }
            joints[2].localRotation = Quaternion.AngleAxis(thumbOppositionDegrees, thumbOppositionAxisParent) * rotations[2];
            Flex(3, 15f); Flex(4, 15f);
            for (int i = 0; i < 26; i++) authoredRotations[i] = joints[i].localRotation;
            RefreshSurfaceContacts();
        }

        /// <summary>Actual SDK BakeMesh in root-rotation local METRES, including scale.</summary>
        public MeshGeometry ExportGeometry()
        {
            probe.InvalidateCache();
            probe.GetCachedGeometry(Renderers, out var points, out var normals, out var triangles);
            var result = new MeshGeometry { vertices = new Vector3[points.Count], normals = new Vector3[points.Count], triangles = triangles.ToArray(), vertexIds = new int[points.Count] };
            Quaternion inverse = Quaternion.Inverse(Root.rotation);
            for (int i = 0; i < points.Count; i++)
            { result.vertices[i] = inverse * (points[i] - Root.position); result.normals[i] = inverse * normals[i]; result.vertexIds[i] = i; }
            return result;
        }

        private sealed class GuideSkin : IDisposable
        {
            public string name;
            public SkinnedMeshRenderer renderer;
            public Transform[] bones;
            public Matrix4x4[] bindposes;
            public Vector3[] vertices;
            public BoneWeight[] weights;
            private readonly Mesh posed = new Mesh { name = "Private authoritative guide collision skin", hideFlags = HideFlags.HideAndDontSave };
            private readonly List<Vector3> posedVertices = new List<Vector3>(3000);
            // BakeMesh obeys this renderer's active skin quality and morphs.
            // Four-weight manual LBS differed by 7.134mm under the project's
            // Auto/two-bone quality; no approximation is used for trial points.
            public void UpdatePose() { renderer.BakeMesh(posed); posed.GetVertices(posedVertices); }
            public Vector3 Point(int index) { return renderer.transform.TransformPoint(posedVertices[index]); }
            public void Dispose() { UnityEngine.Object.DestroyImmediate(posed); }
        }

        private struct SkinSample { public GuideSkin skin; public int vertex; }

        private void BuildFingerSamples()
        {
            foreach (var renderer in Renderers)
            {
                Mesh mesh = renderer.sharedMesh;
                var skin = new GuideSkin { name = renderer.name, renderer = renderer, bones = renderer.bones, bindposes = mesh.bindposes,
                    vertices = mesh.vertices, weights = mesh.boneWeights };
                guideSkins.Add(skin);
                if (skin.vertices.Length != skin.weights.Length || skin.bones.Length != skin.bindposes.Length)
                    throw new InvalidOperationException("Guide collision sampling requires complete SDK skin influences.");
                skin.UpdatePose();
                Debug.Log("[Rib2AnatomicalContact] Authoritative BakeMesh trial skin; renderer quality=" + renderer.quality + ", active project skin weights=" + QualitySettings.skinWeights);
                for (int finger = 0; finger < 5; finger++)
                {
                    int first = finger == 0 ? 2 : FingerProximals[finger - 1] - 1;
                    int last = finger == 0 ? 5 : first + 4;
                    var ids = new HashSet<int>();
                    for (int b = 0; b < skin.bones.Length; b++)
                        for (int j = first; j <= last; j++) if (skin.bones[b] == joints[j]) ids.Add(b);
                    var candidates = new List<int>();
                    var candidatePoints = new List<Vector3>();
                    for (int v = 0; v < skin.weights.Length; v++)
                    {
                        BoneWeight w = skin.weights[v];
                        float influence = (ids.Contains(w.boneIndex0) ? w.weight0 : 0f) + (ids.Contains(w.boneIndex1) ? w.weight1 : 0f)
                            + (ids.Contains(w.boneIndex2) ? w.weight2 : 0f) + (ids.Contains(w.boneIndex3) ? w.weight3 : 0f);
                        if (influence < .65f) continue;
                        candidates.Add(v); candidatePoints.Add(skin.Point(v));
                    }
                    // Farthest-point sampling covers both sides and the tip of
                    // each actual SDK finger skin, rather than vertex-order stride.
                    var chosen = new bool[candidates.Count];
                    var distances = new float[candidates.Count];
                    for (int i = 0; i < distances.Length; i++) distances[i] = float.PositiveInfinity;
                    int next = 0;
                    for (int n = 0; n < Mathf.Min(32, candidates.Count); n++)
                    {
                        chosen[next] = true; fingerSamples[finger].Add(new SkinSample { skin = skin, vertex = candidates[next] });
                        float farthest = -1f; int following = next;
                        for (int i = 0; i < candidates.Count; i++)
                        {
                            distances[i] = Mathf.Min(distances[i], (candidatePoints[i] - candidatePoints[next]).sqrMagnitude);
                            if (!chosen[i] && distances[i] > farthest) { farthest = distances[i]; following = i; }
                        }
                        next = following;
                    }
                }
            }
            for (int i = 0; i < fingerSamples.Length; i++)
                if (fingerSamples[i].Count == 0) throw new InvalidOperationException("No actual guide skin samples for finger " + i);
        }

        private struct CollisionCost { public int tested, intruding; public float maximum, squared; }

        /// <summary>Check sampled BakeMesh world mapping against a fresh independent BakeMesh.</summary>
        public float ValidateSkinSampling()
        {
            var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            var vertices = new List<Vector3>(); float maximum = 0f;
            try
            {
                foreach (GuideSkin skin in guideSkins)
                {
                    skin.UpdatePose(); skin.renderer.BakeMesh(mesh); mesh.GetVertices(vertices);
                    foreach (var samples in fingerSamples)
                        foreach (SkinSample sample in samples)
                            if (sample.skin == skin)
                                maximum = Mathf.Max(maximum, Vector3.Distance(skin.Point(sample.vertex), skin.renderer.transform.TransformPoint(vertices[sample.vertex])));
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(mesh); }
            return maximum;
        }

        private CollisionCost FingerCollision(PatientFingers patient, int finger, bool bake = true)
        {
            if (bake) foreach (GuideSkin skin in guideSkins) skin.UpdatePose();
            var result = new CollisionCost();
            foreach (SkinSample sample in fingerSamples[finger])
            {
                result.tested++;
                float inset = -patient.SignedTriangleDistance(sample.skin.Point(sample.vertex));
                if (inset <= .001f) continue;
                result.intruding++; result.maximum = Mathf.Max(result.maximum, inset); result.squared += inset * inset;
            }
            return result;
        }

        private void ApplyFingerCandidate(int start, float mcpDelta, float pipDelta)
        {
            joints[start].localRotation = Quaternion.AngleAxis(mcpDelta, curlAxesParent[start]) * authoredRotations[start];
            joints[start + 1].localRotation = Quaternion.AngleAxis(pipDelta, curlAxesParent[start + 1]) * authoredRotations[start + 1];
            joints[start + 2].localRotation = Quaternion.AngleAxis(.7f * pipDelta, curlAxesParent[start + 2]) * authoredRotations[start + 2];
        }

        private static bool BetterCollision(CollisionCost trial, CollisionCost best, float mcp, float pip, float bestMcp, float bestPip)
        {
            if (trial.maximum < best.maximum - .00005f) return true;
            if (Mathf.Abs(trial.maximum - best.maximum) > .00005f) return false;
            if (trial.squared < best.squared - .00000001f) return true;
            return Mathf.Abs(trial.squared - best.squared) <= .00000001f && mcp * mcp + pip * pip < bestMcp * bestMcp + bestPip * bestPip;
        }

        /// <summary>
        /// Bounded finger-only geometric correction against actual posed patient
        /// triangles. Root and wrist remain fixed. This samples 32 skin vertices
        /// per finger, and does not claim exhaustive mesh collision or pressure.
        /// Call after PlacePalm on every Sample; authored local finger pose is
        /// restored first so correction never accumulates between frames.
        /// </summary>
        public FingerCollisionResult RelaxFingerCollision(PatientFingers patient)
        {
            RequireAlive(); if (patient == null) throw new ArgumentNullException(nameof(patient));
            Vector3 rootBefore = Root.position, wristBefore = WristWorld;
            for (int i = 2; i < 26; i++) joints[i].localRotation = authoredRotations[i];
            var report = new FingerCollisionResult();
            foreach (GuideSkin skin in guideSkins) skin.UpdatePose();
            for (int finger = 0; finger < 5; finger++)
            {
                CollisionCost before = FingerCollision(patient, finger, false);
                report.testedVertices += before.tested; report.beforeIntrudingVertices += before.intruding;
                report.beforeMaximumInsetMetres = Mathf.Max(report.beforeMaximumInsetMetres, before.maximum);
            }
            for (int finger = 0; finger < 5; finger++)
            {
                int start = finger == 0 ? 2 : FingerProximals[finger - 1];
                CollisionCost before = FingerCollision(patient, finger), best = before;
                if (before.maximum <= .001f) continue;
                float bestMcp = 0f, bestPip = 0f;
                // Proximal opening spans the patient's hand thickness while
                // independent PIP/DIP curl can wrap around its edge. A single
                // shared angle had opened the whole finger into a straight strip.
                foreach (float mcp in CollisionMcpAngles)
                    foreach (float pip in CollisionPipAngles)
                    {
                        if (mcp == 0f && pip == 0f) continue;
                        ApplyFingerCandidate(start, mcp, pip);
                        CollisionCost trial = FingerCollision(patient, finger);
                        if (BetterCollision(trial, best, mcp, pip, bestMcp, bestPip))
                        { best = trial; bestMcp = mcp; bestPip = pip; }
                    }
                float coarseMcp = bestMcp, coarsePip = bestPip;
                foreach (float mcpOffset in CollisionRefinement)
                    foreach (float pipOffset in CollisionRefinement)
                    {
                        float mcp = Mathf.Clamp(coarseMcp + mcpOffset, -30f, 30f);
                        float pip = Mathf.Clamp(coarsePip + pipOffset, -20f, 40f);
                        ApplyFingerCandidate(start, mcp, pip);
                        CollisionCost trial = FingerCollision(patient, finger);
                        if (BetterCollision(trial, best, mcp, pip, bestMcp, bestPip))
                        { best = trial; bestMcp = mcp; bestPip = pip; }
                    }
                ApplyFingerCandidate(start, bestMcp, bestPip);
                if (bestMcp != 0f || bestPip != 0f) report.adjustedFingers++;
            }
            foreach (GuideSkin skin in guideSkins) skin.UpdatePose();
            for (int finger = 0; finger < 5; finger++)
            {
                CollisionCost after = FingerCollision(patient, finger, false);
                report.afterIntrudingVertices += after.intruding;
                report.afterMaximumInsetMetres = Mathf.Max(report.afterMaximumInsetMetres, after.maximum);
            }
            report.rootDisplacementMetres = Vector3.Distance(rootBefore, Root.position);
            report.wristDisplacementMetres = Vector3.Distance(wristBefore, WristWorld);
            return report;
        }

        private void Flex(int index, float angle)
        {
            joints[index].localRotation = Quaternion.AngleAxis(angle, curlAxesParent[index]) * rotations[index];
        }

        /// <summary>Uniform authoring size relative to the original SDK prefab.</summary>
        public void SetScale(float multiplier)
        {
            RequireAlive();
            if (float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier <= 0f)
                throw new ArgumentOutOfRangeException(nameof(multiplier));
            Object.transform.localScale = initialObjectScale * multiplier;
            RefreshSurfaceContacts();
        }

        private void RefreshSurfaceContacts()
        {
            Vector3 palm = joints[0].position;
            // The 35% wrist-to-little-proximal landmark is an authored ulnar
            // heel region. The final contact is the actual posed mesh vertex.
            Vector3 heel = Vector3.Lerp(joints[1].position, joints[22].position, .35f);
            Surface palmHit = probe.Find(Renderers, palm, PalmNormalWorld, .07f);
            Surface heelHit = probe.Find(Renderers, heel, PalmNormalWorld, .06f);
            palmSurfaceLocal = Root.InverseTransformPoint(palmHit.point);
            ulnarHeelSurfaceLocal = Root.InverseTransformPoint(heelHit.point);
            foreach (GuideSkin skin in guideSkins)
            {
                if (skin.name == palmHit.renderer) { palmSkin = skin; palmVertex = palmHit.vertexIndex; }
                if (skin.name == heelHit.renderer) { heelSkin = skin; heelVertex = heelHit.vertexIndex; }
            }
            LastSurfaceProbeDistance = Mathf.Max(palmHit.distanceFromLandmark, heelHit.distanceFromLandmark);
        }

        private Vector3 SurfaceWorld(GuideSkin skin, int vertex, Vector3 localFallback)
        {
            if (skin == null) return Root.TransformPoint(localFallback);
            skin.UpdatePose(); return skin.Point(vertex);
        }

        public void PlacePalm(Vector3 contactWorld, Vector3 distalWorld, Vector3 palmarNormalWorld)
        { Place(contactWorld, distalWorld, palmarNormalWorld, palmSurfaceLocal); }

        public void PlaceUlnarHeel(Vector3 contactWorld, Vector3 distalWorld, Vector3 palmarNormalWorld)
        { Place(contactWorld, distalWorld, palmarNormalWorld, ulnarHeelSurfaceLocal); }

        /// <summary>Solver's actual OpenXRRoot world rotation, anchored to measured palm skin.</summary>
        public void PlaceRotation(Vector3 contactWorld, Quaternion rootWorldRotation)
        {
            RequireAlive();
            if (!Finite(contactWorld) || float.IsNaN(Quaternion.Dot(rootWorldRotation, rootWorldRotation)) || float.IsInfinity(Quaternion.Dot(rootWorldRotation, rootWorldRotation)) || Quaternion.Dot(rootWorldRotation, rootWorldRotation) < .000001f)
                throw new ArgumentException("Invalid solved hand contact or root rotation.");
            Root.rotation = rootWorldRotation.normalized;
            Root.position += contactWorld - Root.TransformPoint(palmSurfaceLocal);
        }

        /// <summary>Actual root world pose without a palm-centre contact constraint.</summary>
        public void PlacePose(Vector3 rootWorldPosition, Quaternion rootWorldRotation)
        {
            RequireAlive();
            float norm = Quaternion.Dot(rootWorldRotation, rootWorldRotation);
            if (!Finite(rootWorldPosition) || float.IsNaN(norm) || float.IsInfinity(norm) || norm < .000001f)
                throw new ArgumentException("Invalid solved hand root pose.");
            Root.SetPositionAndRotation(rootWorldPosition, rootWorldRotation.normalized);
        }

        /// <summary>Every actual SDK skin vertex, including palm and cuff, using current BakeMesh.</summary>
        public void CopyBakedWorldVertices(List<Vector3> destination)
        {
            RequireAlive(); if (destination == null) throw new ArgumentNullException(nameof(destination));
            probe.InvalidateCache();
            probe.GetCachedGeometry(Renderers, out var points, out var normals, out var triangles);
            destination.Clear(); destination.AddRange(points);
        }

        /// <summary>Reverse signed-normal diagnostic against the full actual SDK mesh, including its open cuff.</summary>
        public float MeasurePatientVertexInset(List<Vector3> patientWorldVertices)
        {
            RequireAlive(); if (patientWorldVertices == null) throw new ArgumentNullException(nameof(patientWorldVertices));
            // CopyBakedWorldVertices has already refreshed this same world mesh
            // for the forward test. Reuse its vertices/normals/triangles exactly.
            probe.GetCachedGeometry(Renderers, out var points, out var normals, out var triangles);
            actualGuideSurface.Build(points, normals, triangles, Vector3.zero, float.PositiveInfinity);
            float maximum = 0f;
            foreach (Vector3 point in patientWorldVertices)
                maximum = Mathf.Max(maximum, -actualGuideSurface.SignedDistance(point));
            return maximum;
        }

        private void Place(Vector3 contact, Vector3 distal, Vector3 palmar, Vector3 localContact)
        {
            RequireAlive();
            if (!Finite(contact) || distal.sqrMagnitude < .000001f || palmar.sqrMagnitude < .000001f)
                throw new InvalidOperationException("Invalid world contact frame.");
            distal.Normalize();
            // A contact frame cannot represent a palmar component along its
            // distal axis; reject near-parallel inputs rather than invent twist.
            palmar = Vector3.ProjectOnPlane(palmar, distal);
            if (palmar.sqrMagnitude < .000001f) throw new InvalidOperationException("Contact normal is parallel to the hand long axis.");
            palmar.Normalize();
            Quaternion target = Quaternion.LookRotation(distal, -palmar);
            Root.rotation = target * Quaternion.Inverse(measuredFrameLocal);
            Root.position += contact - Root.TransformPoint(localContact);
        }

        public Transform Joint(int index) => joints[index];
        private void RequireAlive() { if (disposed) throw new ObjectDisposedException(nameof(Hand)); }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            probe.Dispose();
            foreach (GuideSkin skin in guideSkins) skin.Dispose();
            if (Object != null) UnityEngine.Object.DestroyImmediate(Object);
            if (material != null) UnityEngine.Object.DestroyImmediate(material);
        }
    }

    public static Hand Create(bool left, Scene scene, Transform parent, Color color, int layer = 30)
    { return new Hand(left, scene, parent, color, layer); }

    /// <summary>
    /// Applies authored finger curls to the final flat skin outputs AFTER every
    /// rig.Evaluate. The original c9 source transforms remain read-only.
    /// </summary>
    public sealed class PatientFingers : IDisposable
    {
        private sealed class Joint
        {
            public Transform output, next, previous;
            public Transform[] descendants;
            public float flexion;
        }
        public readonly Transform Wrist, Index, Middle, Little, Thumb;
        public Vector3 DistalWorld => (Middle.position - Wrist.position).normalized;
        public Vector3 RadialWorld => Vector3.ProjectOnPlane(Index.position - Little.position, DistalWorld).normalized;
        public Vector3 PalmarWorld => (left ? Vector3.Cross(RadialWorld, DistalWorld) : Vector3.Cross(DistalWorld, RadialWorld)).normalized;
        public Vector3 PalmCenterWorld => (Wrist.position + Middle.position + Index.position + Little.position) * .25f;
        public Surface PalmSurface => SurfaceAt(.75f, 0f, true);
        public Surface DorsalSurface => SurfaceAt(.75f, 0f, false);
        /// <summary>Measured index/middle/ring/little PIP bends; 0 is straight.</summary>
        public Vector4 FingersFlexionDegrees => new Vector4(PipBend(3), PipBend(6), PipBend(9), PipBend(12));
        private readonly bool left;
        private readonly Joint[] fingerJoints;
        private readonly SkinnedMeshRenderer[] skins;
        private readonly HashSet<Transform> handSkinBones = new HashSet<Transform>();
        private readonly SurfaceProbe probe = new SurfaceProbe();
        private readonly TriangleSurface collisionSurface = new TriangleSurface();
        private readonly List<Vector3> guideContactVertices = new List<Vector3>(3000);
        private readonly List<Vector3> reciprocalPatientVertices = new List<Vector3>(3000);
        private bool collisionSurfaceValid;

        public PatientFingers(AnatomicalPatientRig rig, bool isLeft)
        {
            left = isLeft;
            var animator = rig.GetComponentInChildren<Animator>(true);
            if (animator == null || !animator.isHuman) throw new InvalidOperationException("Patient finger mapping requires its original Humanoid input.");
            var serialized = new SerializedObject(rig);
            var bindings = serialized.FindProperty("bodyBindings");
            var map = new Dictionary<Transform, Transform>();
            for (int i = 0; i < bindings.arraySize; i++)
            {
                var binding = bindings.GetArrayElementAtIndex(i);
                var source = binding.FindPropertyRelative("source").objectReferenceValue as Transform;
                var output = binding.FindPropertyRelative("output").objectReferenceValue as Transform;
                if (source != null && output != null) map.Add(source, output);
            }
            HumanBodyBones hand = left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
            Transform handSource = animator.GetBoneTransform(hand);
            foreach (var pair in map) if (pair.Key == handSource || pair.Key.IsChildOf(handSource)) handSkinBones.Add(pair.Value);
            // AnatomicalSkinBuilder maps original Hand influences to limb.hand,
            // while finger influences retain their source-mirror outputs.
            var limbs = serialized.FindProperty("limbs");
            for (int i = 0; i < limbs.arraySize; i++)
            {
                var limb = limbs.GetArrayElementAtIndex(i);
                if (limb.FindPropertyRelative("left").boolValue != left) continue;
                var anatomicalHand = limb.FindPropertyRelative("hand").objectReferenceValue as Transform;
                if (anatomicalHand == null) throw new InvalidOperationException("Patient palm skin has no anatomical hand output.");
                handSkinBones.Add(anatomicalHand);
            }
            Wrist = Output(animator, map, hand);
            Index = Output(animator, map, left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
            Middle = Output(animator, map, left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
            Little = Output(animator, map, left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
            Thumb = Output(animator, map, left ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal);
            var ids = left ? LeftFingers : RightFingers;
            var items = new List<Joint>();
            for (int finger = 0; finger < 5; finger++)
                for (int part = 0; part < 3; part++)
                {
                    Transform source = animator.GetBoneTransform(ids[finger * 3 + part]);
                    Transform output = map[source];
                    var descendants = new List<Transform>();
                    foreach (var pair in map)
                        if (pair.Key == source || pair.Key.IsChildOf(source)) descendants.Add(pair.Value);
                    items.Add(new Joint { output = output,
                        next = part < 2 ? map[animator.GetBoneTransform(ids[finger * 3 + part + 1])] : null,
                        previous = part > 0 ? map[animator.GetBoneTransform(ids[finger * 3 + part - 1])] : Wrist,
                        descendants = descendants.ToArray(), flexion = finger == 0 ? (part == 0 ? 18f : 25f) : (part == 0 ? 60f : part == 1 ? 75f : 40f) });
                }
            fingerJoints = items.ToArray();
            var bodySkins = new List<SkinnedMeshRenderer>();
            foreach (var renderer in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (renderer.name.IndexOf("CC_Base_Body", StringComparison.OrdinalIgnoreCase) >= 0) bodySkins.Add(renderer);
            if (bodySkins.Count == 0) throw new InvalidOperationException("Patient body skin is unavailable for surface contact measurement.");
            skins = bodySkins.ToArray();
        }

        public void ApplyFromEvaluatedPose(float curl)
        {
            probe.InvalidateCache();
            collisionSurfaceValid = false;
            // Remove authored parameter overshoot, never patient arm movement.
            curl = Mathf.Clamp01(curl);
            Vector3 palmar = PalmarWorld;
            // Hidden thumb opposition remains an authored hypothesis. Its axis
            // is measured from the current thumb and little-finger base rather
            // than a fixed Euler rotation, and is limited to 28 degrees.
            Joint thumb = fingerJoints[0];
            Vector3 thumbLong = thumb.next.position - thumb.output.position;
            Vector3 acrossPalm = Little.position - thumb.output.position;
            Vector3 oppositionAxis = Vector3.Cross(thumbLong.normalized, acrossPalm.normalized);
            if (oppositionAxis.sqrMagnitude > .000001f)
                RotateDescendants(thumb, Quaternion.AngleAxis(28f * curl, oppositionAxis.normalized));
            foreach (Joint joint in fingerJoints)
            {
                Vector3 longAxis = joint.next != null ? joint.next.position - joint.output.position : joint.output.position - joint.previous.position;
                Vector3 axis = Vector3.Cross(longAxis.normalized, palmar);
                if (axis.sqrMagnitude < .000001f) throw new InvalidOperationException("Patient finger bend plane is undefined.");
                Quaternion delta = Quaternion.AngleAxis(joint.flexion * curl, axis.normalized);
                RotateDescendants(joint, delta);
            }
        }

        private static void RotateDescendants(Joint joint, Quaternion delta)
        {
            Vector3 pivot = joint.output.position;
            foreach (Transform descendant in joint.descendants)
                descendant.SetPositionAndRotation(pivot + delta * (descendant.position - pivot), delta * descendant.rotation);
        }

        private float PipBend(int proximalIndex)
        {
            Vector3 first = fingerJoints[proximalIndex + 1].output.position - fingerJoints[proximalIndex].output.position;
            Vector3 second = fingerJoints[proximalIndex + 2].output.position - fingerJoints[proximalIndex + 1].output.position;
            return Vector3.Angle(first, second);
        }

        /// <summary>
        /// Approximate diagnostic only: guide finger JOINT CENTRES lying behind
        /// the nearest patient skin vertex's outward normal. This is neither a
        /// triangle collision test nor proof that all visible skin is separate.
        /// </summary>
        public JointIntrusion MeasureGuideJointIntrusion(Hand guide, float minimumInsetMetres = .003f)
        {
            if (guide == null) throw new ArgumentNullException(nameof(guide));
            if (minimumInsetMetres < 0f || float.IsNaN(minimumInsetMetres) || float.IsInfinity(minimumInsetMetres))
                throw new ArgumentOutOfRangeException(nameof(minimumInsetMetres));
            var result = new JointIntrusion();
            for (int i = 2; i < 26; i++)
            {
                Vector3 point = guide.Joint(i).position;
                result.testedJointCount++;
                if (!probe.TryNearestCached(skins, point, .04f, out Surface nearest)) continue;
                float inset = -Vector3.Dot(point - nearest.point, nearest.normal);
                if (inset <= minimumInsetMetres) continue;
                result.intrudingJointCount++;
                result.maximumInsetMetres = Mathf.Max(result.maximumInsetMetres, inset);
            }
            return result;
        }

        /// <summary>Signed nearest posed triangle distance within the hand region.</summary>
        public float SignedTriangleDistance(Vector3 worldPoint)
        {
            EnsureCollisionSurface();
            return collisionSurface.SignedDistance(worldPoint);
        }

        private void EnsureCollisionSurface()
        {
            if (!collisionSurfaceValid)
            {
                probe.GetCachedGeometry(skins, out var points, out var normals, out var triangles);
                collisionSurface.Build(points, normals, triangles, PalmCenterWorld, .18f);
                collisionSurfaceValid = true;
            }
        }

        /// <summary>
        /// All current guide BakeMesh vertices against actual patient hand-region
        /// triangles. Unsigned minimum gap and deepest signed inset are separate.
        /// This is a vertex/triangle geometry diagnostic, not pressure or an
        /// exhaustive triangle/triangle intersection proof; it moves nothing.
        /// </summary>
        public SurfaceContact MeasureSurfaceContact(Hand guide)
        {
            if (guide == null) throw new ArgumentNullException(nameof(guide));
            EnsureCollisionSurface(); guide.CopyBakedWorldVertices(guideContactVertices);
            var result = new SurfaceContact { gapMetres = float.PositiveInfinity, testedCount = guideContactVertices.Count };
            foreach (Vector3 point in guideContactVertices)
            {
                float signed = collisionSurface.NearestPoint(point, out Vector3 nearest, out Vector3 normal);
                float gap = Mathf.Abs(signed);
                if (gap < result.gapMetres) { result.gapMetres = gap; result.guidePoint = point; result.patientPoint = nearest; }
                result.maxInsetMetres = Mathf.Max(result.maxInsetMetres, -signed);
            }
            probe.GetCachedGeometry(skins, out var patientPoints, out var patientNormals, out var patientTriangles);
            reciprocalPatientVertices.Clear();
            Vector3 wrist = Wrist.position, distal = DistalWorld;
            foreach (Vector3 point in patientPoints)
            {
                Vector3 offset = point - wrist;
                if (offset.sqrMagnitude <= .22f * .22f && Vector3.Dot(offset, distal) >= -.015f) reciprocalPatientVertices.Add(point);
            }
            result.reciprocalTestedCount = reciprocalPatientVertices.Count;
            result.reciprocalMaxInsetMetres = guide.MeasurePatientVertexInset(reciprocalPatientVertices);
            if (float.IsInfinity(result.gapMetres)) throw new InvalidOperationException("Guide surface contact has no actual skin vertices.");
            return result;
        }

        /// <summary>
        /// Posed body-skin contact near a measured hand landmark: 0 is wrist,
        /// 1 is mean index/middle/little MCP, radial -1..1 spans half palm width.
        /// Values beyond 1 (up to 1.35) inspect the distal palm/finger-base region.
        /// One posed mesh bake is reused until ApplyFromEvaluatedPose is called.
        /// </summary>
        public Surface SurfaceAt(float longitudinal01, float radial01, bool palmar)
        { return SurfaceAt(longitudinal01, radial01, palmar ? 0f : 180f); }

        /// <summary>Same patient skin vertex across poses; index comes from SurfaceAt.</summary>
        public Surface PinnedSurface(int index) { return probe.PinnedCached(skins, index, PalmCenterWorld); }

        /// <summary>Actual hand/finger-influenced body skin, wrist-rotation local metres.</summary>
        public MeshGeometry ExportHandGeometry(float radiusMetres = .22f)
        {
            probe.GetCachedGeometry(skins, out var points, out var normals, out var triangles);
            var selected = new HashSet<int>(); int offset = 0;
            foreach (var skin in skins)
            {
                Transform[] bones = skin.bones; BoneWeight[] weights = skin.sharedMesh.boneWeights;
                for (int i = 0; i < weights.Length; i++)
                {
                    BoneWeight w = weights[i];
                    float influence = (handSkinBones.Contains(bones[w.boneIndex0]) ? w.weight0 : 0f) + (handSkinBones.Contains(bones[w.boneIndex1]) ? w.weight1 : 0f)
                        + (handSkinBones.Contains(bones[w.boneIndex2]) ? w.weight2 : 0f) + (handSkinBones.Contains(bones[w.boneIndex3]) ? w.weight3 : 0f);
                    if (influence >= .4f && Vector3.Distance(points[offset + i], Wrist.position) <= radiusMetres) selected.Add(offset + i);
                }
                offset += skin.sharedMesh.vertexCount;
            }
            var ids = new List<int>(selected); ids.Sort();
            var map = new Dictionary<int, int>();
            var result = new MeshGeometry { vertices = new Vector3[ids.Count], normals = new Vector3[ids.Count], vertexIds = ids.ToArray() };
            Quaternion inverse = Quaternion.Inverse(Wrist.rotation);
            for (int i = 0; i < ids.Count; i++)
            { map.Add(ids[i], i); result.vertices[i] = inverse * (points[ids[i]] - Wrist.position); result.normals[i] = inverse * normals[ids[i]]; }
            var indices = new List<int>();
            for (int i = 0; i + 2 < triangles.Count; i += 3)
                if (map.TryGetValue(triangles[i], out int a) && map.TryGetValue(triangles[i + 1], out int b) && map.TryGetValue(triangles[i + 2], out int c))
                { indices.Add(a); indices.Add(b); indices.Add(c); }
            result.triangles = indices.ToArray(); return result;
        }

        /// <summary>
        /// Side angle 0=palmar, 90=radial/thumb, 180=dorsal, 270=ulnar.
        /// The signed rotation about the measured distal axis accounts for hand
        /// laterality; no Euler or camera-facing normal is substituted.
        /// </summary>
        public Surface SurfaceAt(float longitudinal01, float radial01, float normalAngleDegrees)
        {
            if (float.IsNaN(longitudinal01) || float.IsInfinity(longitudinal01) || longitudinal01 < 0f || longitudinal01 > 1.35f)
                throw new ArgumentOutOfRangeException(nameof(longitudinal01));
            if (float.IsNaN(radial01) || float.IsInfinity(radial01) || radial01 < -1f || radial01 > 1f)
                throw new ArgumentOutOfRangeException(nameof(radial01));
            if (float.IsNaN(normalAngleDegrees) || float.IsInfinity(normalAngleDegrees))
                throw new ArgumentOutOfRangeException(nameof(normalAngleDegrees));
            Vector3 mcp = (Index.position + Middle.position + Little.position) / 3f;
            Vector3 landmark = Vector3.LerpUnclamped(Wrist.position, mcp, longitudinal01)
                + (Index.position - Little.position) * (.5f * radial01);
            Vector3 normal = Quaternion.AngleAxis(normalAngleDegrees * (left ? 1f : -1f), DistalWorld) * PalmarWorld;
            return probe.FindCached(skins, landmark, normal, .08f);
        }

        public void Dispose() { probe.Dispose(); }
    }

    private static Transform Output(Animator animator, Dictionary<Transform, Transform> map, HumanBodyBones id)
    {
        Transform source = animator.GetBoneTransform(id);
        if (source == null || !map.TryGetValue(source, out var output)) throw new InvalidOperationException("Missing patient finger output " + id);
        return output;
    }

    /// <summary>Nearest facing posed mesh vertex, not a pressure/contact solver.</summary>
    private sealed class SurfaceProbe : IDisposable
    {
        private readonly Mesh mesh = new Mesh { name = "Private contact surface probe", hideFlags = HideFlags.HideAndDontSave };
        private readonly List<Vector3> vertices = new List<Vector3>(20000);
        private readonly List<Vector3> normals = new List<Vector3>(20000);
        private readonly List<Vector3> cachedPoints = new List<Vector3>(20000);
        private readonly List<Vector3> cachedNormals = new List<Vector3>(20000);
        private readonly List<string> cachedRenderers = new List<string>(20000);
        private readonly List<int> cachedTriangles = new List<int>(60000);
        private readonly Dictionary<Mesh, int[]> topology = new Dictionary<Mesh, int[]>();
        private bool cacheValid;

        public void InvalidateCache() { cacheValid = false; }

        public void GetCachedGeometry(SkinnedMeshRenderer[] renderers, out List<Vector3> points, out List<Vector3> worldNormals, out List<int> triangles)
        {
            EnsureCache(renderers); points = cachedPoints; worldNormals = cachedNormals; triangles = cachedTriangles;
        }

        public Surface FindCached(SkinnedMeshRenderer[] renderers, Vector3 near, Vector3 facing, float radius)
        {
            EnsureCache(renderers);
            float best = radius * radius;
            bool found = false;
            Surface result = default;
            facing.Normalize();
            for (int i = 0; i < cachedPoints.Count; i++)
            {
                Vector3 direction = cachedPoints[i] - near;
                float squared = direction.sqrMagnitude;
                if (squared >= best || Vector3.Dot(direction, facing) < 0f || Vector3.Dot(cachedNormals[i], facing) < .15f) continue;
                best = squared; found = true;
                result = new Surface { point = cachedPoints[i], normal = cachedNormals[i], distanceFromLandmark = Mathf.Sqrt(squared), renderer = cachedRenderers[i], vertexIndex = i };
            }
            if (!found) throw new InvalidOperationException("No facing posed mesh surface near cached contact landmark (radius=" + radius + ").");
            return result;
        }

        public Surface PinnedCached(SkinnedMeshRenderer[] renderers, int index, Vector3 landmark)
        {
            EnsureCache(renderers);
            if (index < 0 || index >= cachedPoints.Count) throw new ArgumentOutOfRangeException(nameof(index));
            return new Surface { point = cachedPoints[index], normal = cachedNormals[index], renderer = cachedRenderers[index], vertexIndex = index,
                distanceFromLandmark = Vector3.Distance(cachedPoints[index], landmark) };
        }

        public bool TryNearestCached(SkinnedMeshRenderer[] renderers, Vector3 near, float radius, out Surface result)
        {
            EnsureCache(renderers);
            float best = radius * radius;
            bool found = false;
            result = default;
            for (int i = 0; i < cachedPoints.Count; i++)
            {
                float squared = (cachedPoints[i] - near).sqrMagnitude;
                if (squared >= best) continue;
                best = squared; found = true;
                result = new Surface { point = cachedPoints[i], normal = cachedNormals[i], distanceFromLandmark = Mathf.Sqrt(squared), renderer = cachedRenderers[i] };
            }
            return found;
        }

        private void EnsureCache(SkinnedMeshRenderer[] renderers)
        {
            if (!cacheValid)
            {
                cachedPoints.Clear(); cachedNormals.Clear(); cachedRenderers.Clear(); cachedTriangles.Clear();
                foreach (var renderer in renderers)
                {
                    if (renderer == null || renderer.sharedMesh == null) continue;
                    renderer.BakeMesh(mesh);
                    mesh.GetVertices(vertices); mesh.GetNormals(normals);
                    if (normals.Count != vertices.Count)
                        throw new InvalidOperationException("Posed contact surface has no complete mesh normals.");
                    Matrix4x4 matrix = renderer.localToWorldMatrix;
                    Matrix4x4 normalMatrix = matrix.inverse.transpose;
                    string rendererName = renderer.name;
                    int firstVertex = cachedPoints.Count;
                    for (int i = 0; i < vertices.Count; i++)
                    {
                        cachedPoints.Add(matrix.MultiplyPoint3x4(vertices[i]));
                        cachedNormals.Add(normalMatrix.MultiplyVector(normals[i]).normalized);
                        cachedRenderers.Add(rendererName);
                    }
                    if (!topology.TryGetValue(renderer.sharedMesh, out var indices))
                    { indices = renderer.sharedMesh.triangles; topology.Add(renderer.sharedMesh, indices); }
                    for (int i = 0; i < indices.Length; i++) cachedTriangles.Add(firstVertex + indices[i]);
                }
                cacheValid = true;
            }
        }

        public Surface Find(SkinnedMeshRenderer[] renderers, Vector3 near, Vector3 facing, float radius)
        {
            float best = radius * radius;
            bool found = false;
            Surface result = default;
            facing.Normalize();
            foreach (var renderer in renderers)
            {
                if (renderer == null || renderer.sharedMesh == null) continue;
                renderer.BakeMesh(mesh);
                mesh.GetVertices(vertices); mesh.GetNormals(normals);
                Matrix4x4 matrix = renderer.localToWorldMatrix;
                Matrix4x4 normalMatrix = matrix.inverse.transpose;
                for (int i = 0; i < vertices.Count; i++)
                {
                    Vector3 point = matrix.MultiplyPoint3x4(vertices[i]);
                    Vector3 direction = point - near;
                    float squared = direction.sqrMagnitude;
                    if (squared >= best || Vector3.Dot(direction, facing) < 0f) continue;
                    Vector3 normal = normals.Count == vertices.Count ? normalMatrix.MultiplyVector(normals[i]).normalized : facing;
                    if (Vector3.Dot(normal, facing) < .15f) continue;
                    best = squared; found = true;
                    result = new Surface { point = point, normal = normal, distanceFromLandmark = Mathf.Sqrt(squared), renderer = renderer.name, vertexIndex = i };
                }
            }
            if (!found) throw new InvalidOperationException("No facing posed mesh surface near contact landmark (radius=" + radius + ").");
            return result;
        }

        public void Dispose() { UnityEngine.Object.DestroyImmediate(mesh); }
    }

    /// <summary>Nearest actual triangles in a measured hand neighbourhood.</summary>
    private sealed class TriangleSurface
    {
        private struct Triangle { public int a, b, c; public Bounds bounds; public Vector3 center; }
        private struct Node { public Bounds bounds; public int start, count, left, right; }
        private sealed class TriangleComparer : IComparer<int>
        {
            public List<Triangle> triangles; public int axis;
            public int Compare(int a, int b) { return triangles[a].center[axis].CompareTo(triangles[b].center[axis]); }
        }
        private List<Vector3> points, normals;
        private readonly List<Triangle> triangles = new List<Triangle>();
        private readonly List<Node> nodes = new List<Node>();
        private readonly TriangleComparer comparer = new TriangleComparer();
        private int[] order = Array.Empty<int>();

        public void Build(List<Vector3> worldPoints, List<Vector3> worldNormals, List<int> indices, Vector3 center, float radius)
        {
            points = worldPoints; normals = worldNormals; triangles.Clear(); nodes.Clear();
            float radiusSquared = radius * radius;
            for (int i = 0; i + 2 < indices.Count; i += 3)
            {
                int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                Bounds bounds = new Bounds(points[a], Vector3.zero); bounds.Encapsulate(points[b]); bounds.Encapsulate(points[c]);
                if (bounds.SqrDistance(center) > radiusSquared) continue;
                if (Vector3.Cross(points[b] - points[a], points[c] - points[a]).sqrMagnitude < 1e-16f) continue;
                triangles.Add(new Triangle { a = a, b = b, c = c, bounds = bounds, center = bounds.center });
            }
            if (triangles.Count == 0) throw new InvalidOperationException("No posed patient hand triangles for guide finger collision.");
            if (order.Length < triangles.Count) order = new int[triangles.Count];
            for (int i = 0; i < triangles.Count; i++) order[i] = i;
            comparer.triangles = triangles; BuildNode(0, triangles.Count);
        }

        private int BuildNode(int start, int count)
        {
            Bounds bounds = triangles[order[start]].bounds;
            for (int i = 1; i < count; i++) bounds.Encapsulate(triangles[order[start + i]].bounds);
            int index = nodes.Count; var node = new Node { bounds = bounds, start = start, count = count }; nodes.Add(node);
            if (count > 12)
            {
                Vector3 size = bounds.size;
                comparer.axis = size.x > size.y ? (size.x > size.z ? 0 : 2) : (size.y > size.z ? 1 : 2);
                Array.Sort(order, start, count, comparer);
                int half = count / 2;
                node.left = BuildNode(start, half); node.right = BuildNode(start + half, count - half); node.count = 0;
                nodes[index] = node;
            }
            return index;
        }

        public float SignedDistance(Vector3 point)
        { return NearestPoint(point, out Vector3 nearest, out Vector3 normal); }

        public float NearestPoint(Vector3 point, out Vector3 nearest, out Vector3 normal)
        {
            float best = float.PositiveInfinity; nearest = Vector3.zero; normal = Vector3.zero;
            Nearest(0, point, ref best, ref nearest, ref normal);
            float distance = Mathf.Sqrt(best);
            return Vector3.Dot(point - nearest, normal) < 0f ? -distance : distance;
        }

        private void Nearest(int index, Vector3 p, ref float best, ref Vector3 nearest, ref Vector3 normal)
        {
            Node node = nodes[index]; if (node.bounds.SqrDistance(p) >= best) return;
            if (node.count == 0)
            {
                int first = node.left, second = node.right;
                if (nodes[second].bounds.SqrDistance(p) < nodes[first].bounds.SqrDistance(p)) { int swap = first; first = second; second = swap; }
                Nearest(first, p, ref best, ref nearest, ref normal); Nearest(second, p, ref best, ref nearest, ref normal); return;
            }
            for (int i = node.start; i < node.start + node.count; i++)
            {
                Triangle t = triangles[order[i]];
                Vector3 bary = ClosestBarycentric(p, points[t.a], points[t.b], points[t.c]);
                Vector3 q = points[t.a] * bary.x + points[t.b] * bary.y + points[t.c] * bary.z;
                float distance = (p - q).sqrMagnitude; if (distance >= best) continue;
                best = distance; nearest = q;
                normal = (normals[t.a] * bary.x + normals[t.b] * bary.y + normals[t.c] * bary.z).normalized;
            }
        }

        private static Vector3 ClosestBarycentric(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return new Vector3(1f, 0f, 0f);
            Vector3 bp = p - b; float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return new Vector3(0f, 1f, 0f);
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) { float v = d1 / (d1 - d3); return new Vector3(1f - v, v, 0f); }
            Vector3 cp = p - c; float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return new Vector3(0f, 0f, 1f);
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) { float w = d2 / (d2 - d6); return new Vector3(1f - w, 0f, w); }
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
            { float w = (d4 - d3) / ((d4 - d3) + (d5 - d6)); return new Vector3(0f, 1f - w, w); }
            float sum = va + vb + vc;
            if (Mathf.Abs(sum) < 1e-20f) return new Vector3(1f, 0f, 0f);
            return new Vector3(va / sum, vb / sum, vc / sum);
        }
    }

    private static readonly HumanBodyBones[] LeftFingers = {
        HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbDistal,
        HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal,
        HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal,
        HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal,
        HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal };
    private static readonly HumanBodyBones[] RightFingers = {
        HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal,
        HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal,
        HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
        HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal,
        HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal };
    private static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
}
