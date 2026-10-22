using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Experimental authoring only. Bakes the existing Anatomy FBX, fits its rest
/// geometry to the measured c9 landmarks, and creates separate mesh assets.
/// Rigid skeletal pieces receive one output bone; muscle attachment weights are
/// geometric approximations, not measured muscle paths or force parameters.
/// Never changes the original mesh, material, prefab, or scene assets.
/// </summary>
public static class AnatomicalMeshBuilder
{
    public sealed class Result
    {
        public AnatomicalPatientRig.Neck neck;
        public int meshes;
    }

    private const string Bip = "Bip001/Bip001 Pelvis/Bip001 Spine/Bip001 Spine1/Bip001 Spine2/Bip001 Spine3";
    private const string NeckPath = Bip + "/Bip001 Neck";
    private const string HeadPath = NeckPath + "/Bip001 Neck1/Bip001 Neck2/Bip001 Neck3/Bip001 Neck4/bip_head/Bip001 Head";
    private const float WeldTolerance = 0.00001f;

    private enum Region
    {
        Torso, Neck, Head, Clavicle, Scapula, UpperArm, Ulna, Radius, Hand,
        ChestHumerus, ScapulaHumerus, NeckScapula, NeckClavicle, ChestScapula,
        UpperForearm, ForearmHand, ForearmTwist, ChestNeck, ChestHead,
        Trapezius, ScapulaRadius, ScapulaUlna, UpperRadius
    }

    private sealed class Arm
    {
        public AnatomicalPatientRig.Limb output;
        public Vector3 clavicle, shoulder, elbow, wrist, scapula;
        public float transverseScale;
        public float handReach;
        public Quaternion upperRotation, forearmRotation, radiusRotation;
        public Matrix4x4 handLinear;
    }

    private sealed class Context
    {
        public Transform space, chest, skeleton, muscles;
        public Vector3 sourceHip, sourceNeck, sourceHead, targetHip, targetNeck, targetHead;
        public float torsoWidth, headScale;
        public Quaternion headRotation;
        public Bounds reachableBounds;
        public Arm[] arms;
        public AnatomicalPatientRig.Neck neck;
    }

    private static readonly HashSet<string> Muscles = new HashSet<string>(StringComparer.Ordinal)
    {
        "anconeus", "bicep_brachii_long_head", "bicep_brachii_short_head", "bicipital_aponeurosis",
        "brachialis", "brachioradialis", "deltoit", "extensor_carpi_radialis_brevis",
        "extensor_carpi_radialis_longus", "extensor_carpi_ulnaris", "extensor_digitorum",
        "extensor_indicis", "extensor_pollicis_brevis", "extensor_pollicis_longus",
        "flexor_carpi_radialis", "flexor_carpi_ulnaris", "flexor_digitorum_profundus",
        "flexor_digitorum_superficialis", "flexor_pollicis_longus", "palmaris_longus",
        "pronator_quadratus", "pronator_teres", "supinator", "infraspinatus", "supraspinatus",
        "subscapularis", "teres_major", "teres_minor", "pectoralis_major", "pectoralis_minor",
        "subclavius", "serratus_anterior", "rhomboid_major", "rhomboid_minor", "trapezius",
        "latissimus_dorsi", "levator_scapularis", "sternocleidomastoid", "scalene_anterior",
        "scalene_middle", "scalene_posterior", "splenius_capitis", "semispinalis_capitis",
        "sternohyoid", "sternothyroid", "omohyoid", "thyrohyoid", "triceps_lateral_head",
        "triceps_long_head", "triceps_medial_head", "triceps_tendon_medial_head"
    };

    public static Result Build(GameObject anatomySource, Transform rigSpace, Transform anatomyRoot,
        Animator patient, AnatomicalPatientRig.Limb[] limbs, Dictionary<Transform, Transform> bodyMirrors,
        string assetFolder)
    {
        if (anatomySource == null || rigSpace == null || anatomyRoot == null || patient == null)
            throw new ArgumentNullException("Anatomy mesh builder requires source, rig space, output root and patient.");
        if (limbs == null || limbs.Length != 2 || !AssetDatabase.IsValidFolder(assetFolder))
            throw new InvalidOperationException("Mesh builder needs two measured limbs and an existing new asset folder.");

        Transform source = anatomySource.transform;
        Transform chestInput = PatientBone(patient, "CC_Base_Spine02");
        Transform neck1 = PatientBone(patient, "CC_Base_NeckTwist01");
        Transform neck2 = PatientBone(patient, "CC_Base_NeckTwist02");
        Transform headInput = PatientBone(patient, "CC_Base_Head");
        Transform hipInput = PatientBone(patient, "CC_Base_Hip");
        Transform chestOutput;
        if (!bodyMirrors.TryGetValue(chestInput, out chestOutput) || chestOutput == null)
            throw new InvalidOperationException("Measured c9 chest has no output mirror.");

        var c = new Context
        {
            space = rigSpace, chest = chestOutput,
            skeleton = OutputGroup(anatomyRoot, "Skeleton"), muscles = OutputGroup(anatomyRoot, "Muscles"),
            sourceHip = Point(rigSpace, Require(source, "Bip001/Bip001 Pelvis")),
            sourceNeck = Point(rigSpace, Require(source, NeckPath)),
            sourceHead = Point(rigSpace, Require(source, HeadPath)),
            targetHip = Point(rigSpace, hipInput), targetNeck = Point(rigSpace, neck1),
            targetHead = Point(rigSpace, headInput), arms = new Arm[2]
        };
        for (int i = 0; i < 2; i++)
        {
            AnatomicalPatientRig.Limb limb = limbs[i];
            string side = limb.left ? "L" : "R";
            string armPath = Bip + "/Bip001 " + side + " Clavicle";
            string upperPath = armPath + "/Bip001 " + side + " UpperArm";
            string lowerPath = upperPath + "/Bip001 " + side + " Forearm";
            string handPath = lowerPath + "/Bip001 " + side + " Hand";
            c.arms[i] = new Arm
            {
                output = limb, clavicle = Point(rigSpace, Require(source, armPath)),
                shoulder = Point(rigSpace, Require(source, upperPath)),
                elbow = Point(rigSpace, Require(source, lowerPath)),
                wrist = Point(rigSpace, Require(source, handPath))
            };
        }
        float sourceWidth = Vector3.Distance(c.arms[0].shoulder, c.arms[1].shoulder);
        c.torsoWidth = Vector3.Distance(limbs[0].shoulder, limbs[1].shoulder) / RequireLength(sourceWidth, "shoulder span");
        c.headRotation = Quaternion.FromToRotation(c.sourceHead - c.sourceNeck, c.targetHead - c.targetNeck);
        c.headScale = 1f;
        SkinnedMeshRenderer[] renderers = anatomySource.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < 2; i++)
        {
            string name = c.arms[i].output.left ? "L_scapula" : "R_scapula";
            Vector3[] scapula = BakePoints(FindRenderer(renderers, name), rigSpace, out Mesh temporary);
            try { c.arms[i].scapula = Average(scapula); }
            finally { UnityEngine.Object.DestroyImmediate(temporary); }
            c.arms[i].transverseScale = c.torsoWidth;
            MeasureArmFrames(c.arms[i], source, patient, rigSpace, renderers);
            Vector3 center = ScapulaMap(c, c.arms[i], c.arms[i].scapula);
            c.arms[i].output.scapulaCenter = center;
            c.arms[i].output.scapula.position = rigSpace.TransformPoint(center);
        }

        // Both extents are measured from baked geometry in the same rig space.
        SkinnedMeshRenderer patientBody = FindRenderer(patient.GetComponentsInChildren<SkinnedMeshRenderer>(true), "CC_Base_Body");
        Vector3[] bodyPoints = BakePoints(patientBody, rigSpace, out Mesh bodyTemporary);
        Vector3[] skullPoints = BakePoints(FindRenderer(renderers, "skull"), rigSpace, out Mesh skullTemporary);
        try
        {
            float sourceHeight = MaxY(skullPoints) - c.sourceHead.y;
            float targetHeight = MaxY(bodyPoints) - c.targetHead.y;
            c.headScale = RequireLength(targetHeight, "patient head height") / RequireLength(sourceHeight, "anatomy skull height");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(bodyTemporary);
            UnityEngine.Object.DestroyImmediate(skullTemporary);
        }

        c.neck = new AnatomicalPatientRig.Neck
        {
            sourceChest = chestInput, sourceNeck1 = neck1, sourceNeck2 = neck2, sourceHead = headInput,
            basePivot = c.targetNeck, segments = new Transform[7], head = OutputBone(rigSpace, "Anatomy_Head", c.targetHead)
        };
        SkinnedMeshRenderer cervical = FindRenderer(renderers, "cervical_spine");
        Vector3[] neckPoints = BakePoints(cervical, rigSpace, out Mesh neckTemporary);
        int[] component;
        try
        {
            component = CervicalComponents(neckTemporary, neckPoints, out Vector3[] centers);
            for (int i = 0; i < 7; i++)
                c.neck.segments[i] = OutputBone(rigSpace, "Anatomy_Cervical_" + (i + 1), NeckMap(c, centers[i]));
        }
        finally { UnityEngine.Object.DestroyImmediate(neckTemporary); }

        // A rest-pose mesh bound alone disappears when the arm is elevated.
        // Measure the complete limb reach (including the baked hand geometry)
        // around the hip, so every generated renderer covers all arm/neck poses.
        float radius = Vector3.Distance(c.targetHip, c.targetHead) +
                       Vector3.Distance(c.sourceHead, c.sourceNeck) * c.headScale;
        foreach (SkinnedMeshRenderer renderer in renderers)
        {
            string name = Normalize(renderer.name);
            if (!Select(name, out Region region, out _) || region != Region.Hand) continue;
            bool left = name[0] == 'l';
            Arm arm = c.arms[0].output.left == left ? c.arms[0] : c.arms[1];
            Vector3[] handPoints = BakePoints(renderer, rigSpace, out Mesh temporary);
            try
            {
                foreach (Vector3 p in handPoints)
                    arm.handReach = Mathf.Max(arm.handReach, Vector3.Distance(HandMap(arm, p), arm.output.wrist));
            }
            finally { UnityEngine.Object.DestroyImmediate(temporary); }
        }
        foreach (Arm arm in c.arms)
        {
            AnatomicalPatientRig.Limb limb = arm.output;
            float reach = Vector3.Distance(c.targetHip, limb.clavicleMedial) +
                Vector3.Distance(limb.clavicleMedial, limb.shoulder) + Vector3.Distance(limb.shoulder, limb.elbow) +
                Vector3.Distance(limb.elbow, limb.wrist) + arm.handReach;
            radius = Mathf.Max(radius, reach);
        }
        // Five percent is an explicit authoring margin for the approximate
        // shoulder translation; it is not an anatomical range-of-motion claim.
        c.reachableBounds = new Bounds(c.targetHip, Vector3.one * (radius * 2.1f));

        int count = 0, muscles = 0;
        foreach (SkinnedMeshRenderer renderer in renderers)
        {
            string normalized = Normalize(renderer.name);
            if (normalized == "cervical_spine")
            {
                BuildRenderer(c, renderer, assetFolder, Region.Neck, null, component);
                count++;
                continue;
            }
            if (!Select(normalized, out Region region, out bool muscle)) continue;
            Arm arm = null;
            if (normalized.StartsWith("l_", StringComparison.Ordinal) || normalized.StartsWith("r_", StringComparison.Ordinal))
            {
                bool left = normalized[0] == 'l';
                arm = c.arms[0].output.left == left ? c.arms[0] : c.arms[1];
            }
            BuildRenderer(c, renderer, assetFolder, region, arm, null);
            count++;
            if (muscle) muscles++;
        }
        Debug.Log("[AnatomicalMeshBuilder] Built " + count + " new mesh assets; muscles " + muscles +
            ". Cervical components=7; scapula/clavicle independent; source assets unchanged. " +
            "Muscle attachment weights and rest fitting are unvalidated geometric approximations.");
        return new Result { neck = c.neck, meshes = count };
    }

    private static void BuildRenderer(Context c, SkinnedMeshRenderer source, string folder, Region region, Arm arm, int[] components)
    {
        string sourceGroup = source.transform.parent != null ? source.transform.parent.name : "";
        Transform outputGroup;
        if (sourceGroup == "skeletal_system") outputGroup = c.skeleton;
        else if (sourceGroup == "muscular_system") outputGroup = c.muscles;
        else throw new InvalidOperationException("Selected Anatomy mesh has an unrecognized source category: " + source.name + " under " + sourceGroup);
        Vector3[] points = BakePoints(source, c.space, out Mesh mesh);
        mesh.name = source.name.Trim() + "_Anatomical";
        var go = new GameObject(source.name.Trim());
        go.transform.SetParent(outputGroup, false);
        var bones = new List<Transform>();
        var weights = new BoneWeight[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            Vector3 fitted;
            if (components != null)
            {
                fitted = NeckMap(c, points[i]);
                weights[i] = Single(bones, c.neck.segments[components[i]]);
            }
            else
                fitted = Fit(c, arm, region, points[i], bones, out weights[i]);
            points[i] = go.transform.InverseTransformPoint(c.space.TransformPoint(fitted));
        }
        mesh.vertices = points;
        mesh.boneWeights = weights;
        Matrix4x4[] bindposes = new Matrix4x4[bones.Count];
        for (int i = 0; i < bones.Count; i++) bindposes[i] = bones[i].worldToLocalMatrix * go.transform.localToWorldMatrix;
        mesh.bindposes = bindposes;
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        if (mesh.uv.Length == mesh.vertexCount) mesh.RecalculateTangents();
        string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + mesh.name.Replace(' ', '_') + ".asset");
        AssetDatabase.CreateAsset(mesh, path);
        var renderer = go.AddComponent<SkinnedMeshRenderer>();
        renderer.sharedMesh = mesh;
        renderer.sharedMaterials = source.sharedMaterials;
        renderer.bones = bones.ToArray();
        renderer.rootBone = c.space;
        renderer.quality = SkinQuality.Bone4;
        Bounds localBounds = mesh.bounds;
        for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
        {
            Vector3 p = c.reachableBounds.center + Vector3.Scale(c.reachableBounds.extents, new Vector3(x, y, z));
            localBounds.Encapsulate(go.transform.InverseTransformPoint(c.space.TransformPoint(p)));
        }
        renderer.localBounds = localBounds;
        renderer.updateWhenOffscreen = false;
        renderer.shadowCastingMode = source.shadowCastingMode;
        renderer.receiveShadows = source.receiveShadows;
    }

    private static Vector3 Fit(Context c, Arm arm, Region region, Vector3 p, List<Transform> bones, out BoneWeight weight)
    {
        if (region == Region.Torso) { weight = Single(bones, c.chest); return TorsoMap(c, p); }
        if (region == Region.Head) { weight = Single(bones, c.neck.head); return HeadMap(c, p); }
        if (region == Region.Neck)
        {
            float t = Mathf.Clamp01(Project(p, c.sourceNeck, c.sourceHead)) * 6f;
            int a = Mathf.Min(Mathf.FloorToInt(t), 6), b = Mathf.Min(a + 1, 6);
            weight = Pair(bones, c.neck.segments[a], c.neck.segments[b], t - a);
            return NeckMap(c, p);
        }
        if (arm == null) throw new InvalidOperationException("Sided anatomy region has no measured arm: " + region);
        AnatomicalPatientRig.Limb limb = arm.output;
        switch (region)
        {
            case Region.Clavicle: weight = Single(bones, limb.clavicle); return ClavicleMap(c, arm, p);
            case Region.Scapula: weight = Single(bones, limb.scapula); return ScapulaMap(c, arm, p);
            case Region.UpperArm: weight = Single(bones, limb.humerus); return UpperMap(arm, p);
            case Region.Ulna: weight = Single(bones, limb.ulna); return ForearmMap(arm, p);
            case Region.Radius: weight = Single(bones, limb.radius); return RadiusMap(arm, p);
            case Region.Hand: weight = Single(bones, limb.hand); return HandMap(arm, p);
            case Region.ChestHumerus:
                return Blend(p, (c.arms[0].clavicle + c.arms[1].clavicle) * .5f, arm.shoulder,
                    TorsoMap(c, p), UpperMap(arm, p), c.chest, limb.humerus, bones, out weight);
            case Region.ScapulaHumerus:
                return Blend(p, arm.scapula, Vector3.Lerp(arm.shoulder, arm.elbow, .35f),
                    ScapulaMap(c, arm, p), UpperMap(arm, p), limb.scapula, limb.humerus, bones, out weight);
            case Region.NeckScapula:
                return Blend(p, Vector3.Lerp(c.sourceNeck, c.sourceHead, .65f), arm.scapula,
                    NeckMap(c, p), ScapulaMap(c, arm, p), c.neck.segments[4], limb.scapula, bones, out weight);
            case Region.NeckClavicle:
                return Blend(p, c.sourceHead, arm.clavicle,
                    HeadMap(c, p), ClavicleMap(c, arm, p), c.neck.head, limb.clavicle, bones, out weight);
            case Region.ChestScapula:
                return Blend(p, (c.sourceHip + c.sourceNeck) * .5f, arm.scapula,
                    TorsoMap(c, p), ScapulaMap(c, arm, p), c.chest, limb.scapula, bones, out weight);
            case Region.UpperForearm:
                return Blend(p, Vector3.Lerp(arm.shoulder, arm.elbow, .5f), arm.elbow,
                    UpperMap(arm, p), ForearmMap(arm, p), limb.humerus, limb.ulna, bones, out weight);
            case Region.ForearmHand:
                return Blend(p, arm.elbow, arm.wrist,
                    RadiusMap(arm, p), HandMap(arm, p), limb.radius, limb.hand, bones, out weight);
            case Region.ForearmTwist:
                weight = Pair(bones, limb.ulna, limb.radius, .5f);
                return Vector3.LerpUnclamped(ForearmMap(arm, p), RadiusMap(arm, p), .5f);
            case Region.ChestNeck:
                return Blend(p, Vector3.Lerp(c.sourceHip, c.sourceNeck, .8f), Vector3.Lerp(c.sourceNeck, c.sourceHead, .6f),
                    TorsoMap(c, p), NeckMap(c, p), c.chest, c.neck.segments[4], bones, out weight);
            case Region.ChestHead:
                return Blend(p, Vector3.Lerp(c.sourceHip, c.sourceNeck, .8f), c.sourceHead,
                    TorsoMap(c, p), HeadMap(c, p), c.chest, c.neck.head, bones, out weight);
            case Region.Trapezius:
                return Triple(p, Vector3.Lerp(c.sourceHip, c.sourceNeck, .6f), c.sourceHead, arm.scapula,
                    TorsoMap(c, p), HeadMap(c, p), ScapulaMap(c, arm, p), c.chest, c.neck.head, limb.scapula, bones, out weight);
            case Region.ScapulaRadius:
                return Blend(p, arm.shoulder, arm.elbow, UpperMap(arm, p), RadiusMap(arm, p),
                    limb.scapula, limb.radius, bones, out weight);
            case Region.ScapulaUlna:
                return Blend(p, arm.shoulder, arm.elbow, UpperMap(arm, p), ForearmMap(arm, p),
                    limb.scapula, limb.ulna, bones, out weight);
            case Region.UpperRadius:
                return Blend(p, Vector3.Lerp(arm.shoulder, arm.elbow, .7f), Vector3.Lerp(arm.elbow, arm.wrist, .9f),
                    UpperMap(arm, p), RadiusMap(arm, p), limb.humerus, limb.radius, bones, out weight);
            default: throw new InvalidOperationException("Unmapped anatomical region: " + region);
        }
    }

    private static bool Select(string name, out Region region, out bool muscle)
    {
        muscle = false;
        region = Region.Torso;
        if (name == "skull" || name == "jaw_bone" || name == "upper_teeth" || name == "lower_teeth")
        { region = Region.Head; return true; }
        if (name == "thorax" || name == "thoracic_spine" || name == "sternum" || name == "costal_cartilage") return true;
        if (name == "hyoid_bone_skeletal") { region = Region.Neck; return true; }
        if (!(name.StartsWith("l_", StringComparison.Ordinal) || name.StartsWith("r_", StringComparison.Ordinal))) return false;
        string part = name.Substring(2);
        switch (part)
        {
            case "clavicle": region = Region.Clavicle; return true;
            case "scapula": region = Region.Scapula; return true;
            case "humerus": region = Region.UpperArm; return true;
            case "ulna": region = Region.Ulna; return true;
            case "radius": region = Region.Radius; return true;
            case "wrist": case "metacarpal_bones": case "finger_bones": region = Region.Hand; return true;
        }
        if (!Muscles.Contains(part)) return false;
        muscle = true;
        if (part == "pectoralis_major" || part == "latissimus_dorsi") region = Region.ChestHumerus;
        else if (part == "pectoralis_minor" || part == "serratus_anterior" || part.StartsWith("rhomboid", StringComparison.Ordinal)) region = Region.ChestScapula;
        else if (part == "subclavius") region = Region.Clavicle;
        else if (part == "levator_scapularis" || part == "omohyoid") region = Region.NeckScapula;
        else if (part == "trapezius") region = Region.Trapezius;
        else if (part == "sternocleidomastoid") region = Region.NeckClavicle;
        else if (part.StartsWith("scalene", StringComparison.Ordinal) || part == "sternohyoid" || part == "sternothyroid") region = Region.ChestNeck;
        else if (part.Contains("capitis")) region = Region.ChestHead;
        else if (part == "thyrohyoid") region = Region.Neck;
        else if (part == "deltoit" || part == "infraspinatus" || part == "supraspinatus" || part == "subscapularis" || part.StartsWith("teres_", StringComparison.Ordinal)) region = Region.ScapulaHumerus;
        else if (part.Contains("bicep") || part == "bicipital_aponeurosis") region = Region.ScapulaRadius;
        else if (part == "triceps_long_head") region = Region.ScapulaUlna;
        else if (part == "brachioradialis") region = Region.UpperRadius;
        else if (part.Contains("triceps") || part == "brachialis" || part == "anconeus") region = Region.UpperForearm;
        else if (part.StartsWith("pronator", StringComparison.Ordinal) || part == "supinator") region = Region.ForearmTwist;
        else region = Region.ForearmHand;
        return true;
    }

    private static Vector3 TorsoMap(Context c, Vector3 p) => Segment(p, c.sourceHip, c.sourceNeck, c.targetHip, c.targetNeck, c.torsoWidth);
    private static Vector3 NeckMap(Context c, Vector3 p) => Segment(p, c.sourceNeck, c.sourceHead, c.targetNeck, c.targetHead, 1f);
    private static Vector3 HeadMap(Context c, Vector3 p) => c.targetHead + c.headRotation * (p - c.sourceHead) * c.headScale;
    private static Vector3 ClavicleMap(Context c, Arm a, Vector3 p) => Segment(p, a.clavicle, a.shoulder, a.output.clavicleMedial, a.output.shoulder, a.transverseScale);
    private static Vector3 UpperMap(Arm a, Vector3 p) => FramedSegment(p, a.shoulder, a.elbow, a.output.shoulder, a.output.elbow, a.upperRotation, a.transverseScale);
    private static Vector3 ForearmMap(Arm a, Vector3 p) => FramedSegment(p, a.elbow, a.wrist, a.output.elbow, a.output.wrist, a.forearmRotation, a.transverseScale);
    private static Vector3 RadiusMap(Arm a, Vector3 p) => FramedSegment(p, a.elbow, a.wrist, a.output.elbow, a.output.wrist, a.radiusRotation, a.transverseScale);
    private static Vector3 HandMap(Arm a, Vector3 p) => a.output.wrist + a.handLinear.MultiplyVector(p - a.wrist);
    private static Vector3 ScapulaMap(Context c, Arm a, Vector3 p) => TorsoMap(c, p) + a.output.shoulder - TorsoMap(c, a.shoulder);

    private static void MeasureArmFrames(Arm arm, Transform anatomy, Animator patient, Transform space,
        SkinnedMeshRenderer[] renderers)
    {
        string side = arm.output.left ? "L" : "R";
        string handPath = Bip + "/Bip001 " + side + " Clavicle/Bip001 " + side +
            " UpperArm/Bip001 " + side + " Forearm/Bip001 " + side + " Hand";
        Vector3 sourceUpper = (arm.elbow - arm.shoulder).normalized;
        Vector3 sourceLower = (arm.wrist - arm.elbow).normalized;
        // Removed: upper-axis component, so only the source's observed elbow bend
        // selects its hinge. Unlike c9's nearly straight author pose, this source is flexed.
        Vector3 sourceBend = sourceLower - sourceUpper * Vector3.Dot(sourceLower, sourceUpper);
        if (sourceBend.sqrMagnitude < .0001f)
            throw new InvalidOperationException("Anatomy elbow is too straight to measure its flexion plane: " + side);
        sourceBend.Normalize();
        Vector3 targetUpper = (arm.output.elbow - arm.output.shoulder).normalized;
        Vector3 targetLower = (arm.output.wrist - arm.output.elbow).normalized;
        // The target pole was measured from c9 Humanoid flexion before this builder runs.
        Vector3 targetBend = arm.output.restBendDirection - targetUpper * Vector3.Dot(arm.output.restBendDirection, targetUpper);
        if (targetBend.sqrMagnitude < .0001f)
            throw new InvalidOperationException("Measured c9 elbow pole is required before anatomy fitting: " + side);
        targetBend.Normalize();
        Vector3 sourceHinge = Vector3.Cross(sourceUpper, sourceBend).normalized;
        Vector3 targetHinge = Vector3.Cross(targetUpper, targetBend).normalized;
        arm.upperRotation = MeasuredFrame(targetUpper, targetBend, side + " target humerus") *
            Quaternion.Inverse(MeasuredFrame(sourceUpper, sourceBend, side + " source humerus"));
        arm.forearmRotation = MeasuredFrame(targetLower, targetHinge, side + " target ulna") *
            Quaternion.Inverse(MeasuredFrame(sourceLower, sourceHinge, side + " source ulna"));

        Vector3[] metacarpal = BakePoints(FindRenderer(renderers, side + "_metacarpal_bones"), space, out Mesh temporary);
        Vector3 sourceIndex, sourceMiddle, sourceLittle;
        try
        {
            int index = NearestVertex(metacarpal, Point(space, Require(anatomy, handPath + "/Bip001 " + side + " Finger1")));
            int middle = NearestVertex(metacarpal, Point(space, Require(anatomy, handPath + "/Bip001 " + side + " Finger2")));
            int little = NearestVertex(metacarpal, Point(space, Require(anatomy, handPath + "/Bip001 " + side + " Finger4")));
            if (index == middle || middle == little || index == little)
                throw new InvalidOperationException("Anatomy metacarpal anchors did not select distinct baked vertices: " + side);
            sourceIndex = metacarpal[index]; sourceMiddle = metacarpal[middle]; sourceLittle = metacarpal[little];
            Debug.Log("[AnatomicalMeshBuilder] " + side + " baked metacarpal anchor vertices index/middle/little=" + index + "/" + middle + "/" + little);
        }
        finally { UnityEngine.Object.DestroyImmediate(temporary); }
        Vector3 targetIndex = Point(space, RequiredHumanBone(patient, arm.output.left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal));
        Vector3 targetMiddle = Point(space, RequiredHumanBone(patient, arm.output.left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal));
        Vector3 targetLittle = Point(space, RequiredHumanBone(patient, arm.output.left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal));
        Vector3 sourceRadial = sourceIndex - sourceLittle, targetRadial = targetIndex - targetLittle;
        Vector3 sourceLong = sourceMiddle - arm.wrist, targetLong = targetMiddle - arm.output.wrist;

        // Ulna orientation follows the measured elbow hinge. The remaining distal
        // roll belongs to radius/hand; do not roll the ulna to match the palm.
        Vector3 mappedRadial = arm.forearmRotation * sourceRadial;
        Vector3 oldRadial = mappedRadial - targetLower * Vector3.Dot(mappedRadial, targetLower);
        Vector3 desiredRadial = targetRadial - targetLower * Vector3.Dot(targetRadial, targetLower);
        RequireLength(oldRadial.magnitude, side + " mapped radial direction");
        RequireLength(desiredRadial.magnitude, side + " c9 radial direction");
        float radialRoll = Vector3.SignedAngle(oldRadial, desiredRadial, targetLower);
        arm.radiusRotation = Quaternion.AngleAxis(radialRoll, targetLower) * arm.forearmRotation;

        // Use the actual baked landmarks, not Bip proxies, for this nonorthogonal
        // rest fit. Radial and longitudinal directions map exactly; the third column
        // retains hand thickness. Both bases have positive determinant, so no reflection.
        Matrix4x4 sourceBasis = HandBasis(sourceLong, sourceRadial, 1f, side + " source hand");
        Matrix4x4 targetBasis = HandBasis(targetLong, targetRadial, arm.transverseScale, side + " c9 hand");
        arm.handLinear = targetBasis * sourceBasis.inverse;
        if (arm.handLinear.determinant <= 0f || float.IsNaN(arm.handLinear.determinant) || float.IsInfinity(arm.handLinear.determinant))
            throw new InvalidOperationException("Anatomy hand fit is singular/reflected: " + side);
        Debug.Log("[AnatomicalMeshBuilder] " + side + " source elbow flexion=" + Vector3.Angle(sourceUpper, sourceLower) +
            "; radius rest roll=" + radialRoll + "; hand affine determinant=" + arm.handLinear.determinant +
            ". Elbow hinge fixes ulna; actual baked palm landmarks fix radius/hand. Radius axis and muscle fitting remain geometric approximations.");
    }

    private static Quaternion MeasuredFrame(Vector3 longitudinal, Vector3 reference, string label)
    {
        Vector3 forward = longitudinal.normalized;
        Vector3 up = reference - forward * Vector3.Dot(reference, forward);
        RequireLength(up.magnitude, label + " reference perpendicular to longitudinal axis");
        return Quaternion.LookRotation(forward, up.normalized);
    }

    private static Matrix4x4 HandBasis(Vector3 longitudinal, Vector3 radial, float thicknessScale, string label)
    {
        RequireLength(longitudinal.magnitude, label + " longitudinal");
        RequireLength(radial.magnitude, label + " radial");
        Vector3 normal = Vector3.Cross(longitudinal, radial);
        RequireLength(normal.magnitude, label + " cross section");
        Matrix4x4 basis = Matrix4x4.identity;
        basis.SetColumn(0, new Vector4(longitudinal.x, longitudinal.y, longitudinal.z, 0f));
        basis.SetColumn(1, new Vector4(radial.x, radial.y, radial.z, 0f));
        normal = normal.normalized * thicknessScale;
        basis.SetColumn(2, new Vector4(normal.x, normal.y, normal.z, 0f));
        if (basis.determinant < .00000001f) throw new InvalidOperationException("Degenerate measured hand basis: " + label);
        return basis;
    }

    private static int NearestVertex(Vector3[] points, Vector3 landmark)
    {
        if (points.Length == 0) throw new InvalidOperationException("Empty baked metacarpal mesh.");
        int best = 0; float distance = float.PositiveInfinity;
        for (int i = 0; i < points.Length; i++)
        {
            float candidate = (points[i] - landmark).sqrMagnitude;
            if (candidate < distance) { best = i; distance = candidate; }
        }
        return best;
    }

    private static Transform RequiredHumanBone(Animator patient, HumanBodyBones bone)
    {
        Transform found = patient.GetBoneTransform(bone);
        if (found == null) throw new InvalidOperationException("Missing c9 hand landmark: " + bone);
        return found;
    }

    private static Vector3 FramedSegment(Vector3 p, Vector3 sourceA, Vector3 sourceB, Vector3 targetA, Vector3 targetB,
        Quaternion rotation, float radialScale)
    {
        Vector3 source = sourceB - sourceA, target = targetB - targetA;
        float sourceLength = RequireLength(source.magnitude, "source framed segment");
        float targetLength = RequireLength(target.magnitude, "target framed segment");
        Vector3 axis = source / sourceLength;
        float along = Vector3.Dot(p - sourceA, axis);
        Vector3 radial = p - sourceA - axis * along;
        return targetA + target / targetLength * (along * targetLength / sourceLength) + rotation * radial * radialScale;
    }

    private static Vector3 Segment(Vector3 p, Vector3 sourceA, Vector3 sourceB, Vector3 targetA, Vector3 targetB, float radialScale)
    {
        Vector3 source = sourceB - sourceA, target = targetB - targetA;
        float sourceLength = RequireLength(source.magnitude, "source segment"), targetLength = RequireLength(target.magnitude, "target segment");
        Vector3 axis = source / sourceLength;
        float along = Vector3.Dot(p - sourceA, axis);
        Vector3 radial = p - sourceA - axis * along;
        return targetA + target / targetLength * (along * targetLength / sourceLength) +
               Quaternion.FromToRotation(source, target) * radial * radialScale;
    }

    private static Vector3 Blend(Vector3 p, Vector3 sourceA, Vector3 sourceB, Vector3 fittedA, Vector3 fittedB,
        Transform a, Transform b, List<Transform> bones, out BoneWeight weight)
    {
        // The projection only chooses attachment influence; the complete 3-D
        // position is preserved in both fitted positions before interpolation.
        float t = Mathf.Clamp01(Project(p, sourceA, sourceB));
        weight = Pair(bones, a, b, t);
        return Vector3.LerpUnclamped(fittedA, fittedB, t);
    }

    private static Vector3 Triple(Vector3 p, Vector3 sourceA, Vector3 sourceB, Vector3 sourceC,
        Vector3 fittedA, Vector3 fittedB, Vector3 fittedC, Transform a, Transform b, Transform c,
        List<Transform> bones, out BoneWeight weight)
    {
        // Three measured attachment-region anchors; distance falloff is a
        // smooth visual interpolation, not an estimate of muscle force.
        float wa = 1f / ((p - sourceA).sqrMagnitude + 0.000001f);
        float wb = 1f / ((p - sourceB).sqrMagnitude + 0.000001f);
        float wc = 1f / ((p - sourceC).sqrMagnitude + 0.000001f);
        float sum = wa + wb + wc;
        wa /= sum; wb /= sum; wc /= sum;
        weight = new BoneWeight { boneIndex0 = Index(bones, a), weight0 = wa,
            boneIndex1 = Index(bones, b), weight1 = wb, boneIndex2 = Index(bones, c), weight2 = wc };
        return fittedA * wa + fittedB * wb + fittedC * wc;
    }

    private static float Project(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 delta = b - a;
        float length = RequireLength(delta.magnitude, "attachment segment");
        return Vector3.Dot(p - a, delta) / (length * length);
    }

    private static BoneWeight Single(List<Transform> bones, Transform bone) => new BoneWeight { boneIndex0 = Index(bones, bone), weight0 = 1f };
    private static BoneWeight Pair(List<Transform> bones, Transform a, Transform b, float t)
    {
        if (a == b) return Single(bones, a);
        return new BoneWeight { boneIndex0 = Index(bones, a), weight0 = 1f - t, boneIndex1 = Index(bones, b), weight1 = t };
    }
    private static int Index(List<Transform> bones, Transform bone)
    {
        if (bone == null) throw new InvalidOperationException("Null anatomical output bone.");
        int index = bones.IndexOf(bone);
        if (index < 0) { index = bones.Count; bones.Add(bone); }
        return index;
    }

    private static int[] CervicalComponents(Mesh mesh, Vector3[] vertices, out Vector3[] centers)
    {
        int[] parents = new int[vertices.Length];
        for (int i = 0; i < parents.Length; i++) parents[i] = i;
        var buckets = new Dictionary<Vector3Int, List<int>>();
        float squared = WeldTolerance * WeldTolerance;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 v = vertices[i] / WeldTolerance;
            var key = new Vector3Int(Mathf.FloorToInt(v.x), Mathf.FloorToInt(v.y), Mathf.FloorToInt(v.z));
            for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
            {
                if (!buckets.TryGetValue(key + new Vector3Int(x, y, z), out List<int> candidates)) continue;
                foreach (int j in candidates) if ((vertices[i] - vertices[j]).sqrMagnitude <= squared) Union(parents, i, j);
            }
            if (!buckets.TryGetValue(key, out List<int> bucket)) { bucket = new List<int>(); buckets.Add(key, bucket); }
            bucket.Add(i);
        }
        int[] triangles = mesh.triangles;
        for (int i = 0; i < triangles.Length; i += 3)
        { Union(parents, triangles[i], triangles[i + 1]); Union(parents, triangles[i], triangles[i + 2]); }
        var groups = new Dictionary<int, List<int>>();
        for (int i = 0; i < vertices.Length; i++)
        {
            int root = Find(parents, i);
            if (!groups.TryGetValue(root, out List<int> group)) { group = new List<int>(); groups.Add(root, group); }
            group.Add(i);
        }
        if (groups.Count != 7) throw new InvalidOperationException("Cervical welded components=" + groups.Count + "; expected exactly 7. No anatomical mesh was guessed or split by height.");
        var ordered = new List<KeyValuePair<int, Vector3>>();
        foreach (KeyValuePair<int, List<int>> group in groups)
        {
            Vector3 center = Vector3.zero;
            foreach (int i in group.Value) center += vertices[i];
            ordered.Add(new KeyValuePair<int, Vector3>(group.Key, center / group.Value.Count));
        }
        ordered.Sort((a, b) => a.Value.y.CompareTo(b.Value.y));
        centers = new Vector3[7];
        var indices = new Dictionary<int, int>();
        for (int i = 0; i < 7; i++) { indices.Add(ordered[i].Key, i); centers[i] = ordered[i].Value; }
        int[] result = new int[vertices.Length];
        for (int i = 0; i < result.Length; i++) result[i] = indices[Find(parents, i)];
        Debug.Log("[AnatomicalMeshBuilder] Cervical bake vertices=" + vertices.Length + ", position weld=" + WeldTolerance + "m, components=7, caudal-to-cranial order measured by rig-space Y.");
        return result;
    }
    private static int Find(int[] parents, int i)
    {
        while (parents[i] != i) { parents[i] = parents[parents[i]]; i = parents[i]; }
        return i;
    }
    private static void Union(int[] parents, int a, int b)
    { a = Find(parents, a); b = Find(parents, b); if (a != b) parents[b] = a; }

    private static Vector3[] BakePoints(SkinnedMeshRenderer renderer, Transform space, out Mesh mesh)
    {
        if (renderer == null || renderer.sharedMesh == null) throw new InvalidOperationException("Anatomy source renderer is missing.");
        mesh = new Mesh();
        renderer.BakeMesh(mesh);
        Vector3[] points = mesh.vertices;
        for (int i = 0; i < points.Length; i++) points[i] = space.InverseTransformPoint(renderer.transform.TransformPoint(points[i]));
        return points;
    }
    private static SkinnedMeshRenderer FindRenderer(SkinnedMeshRenderer[] renderers, string name)
    {
        SkinnedMeshRenderer found = null;
        foreach (SkinnedMeshRenderer renderer in renderers)
            if (renderer.name.Trim() == name)
            { if (found != null) throw new InvalidOperationException("Ambiguous source mesh: " + name); found = renderer; }
        if (found == null) throw new InvalidOperationException("Missing source mesh: " + name);
        return found;
    }
    private static Transform Require(Transform source, string path)
    {
        Transform found = source.Find(path);
        if (found == null) throw new InvalidOperationException("Missing exact Anatomy rig path: " + path);
        return found;
    }
    private static Transform PatientBone(Animator patient, string name)
    {
        Transform found = null;
        foreach (Transform candidate in patient.GetComponentsInChildren<Transform>(true))
            if (candidate.name == name)
            { if (found != null) throw new InvalidOperationException("Ambiguous c9 input bone: " + name); found = candidate; }
        if (found == null) throw new InvalidOperationException("Missing c9 input bone: " + name);
        return found;
    }
    private static Transform OutputBone(Transform space, string name, Vector3 position)
    {
        var go = new GameObject(name);
        go.transform.SetParent(space, false);
        go.transform.localPosition = position;
        return go.transform;
    }
    private static Transform OutputGroup(Transform root, string name)
    {
        Transform group = root.Find(name);
        if (group != null) return group;
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        return go.transform;
    }
    private static Vector3 Point(Transform space, Transform point) => space.InverseTransformPoint(point.position);
    private static float RequireLength(float length, string label)
    { if (length < 0.000001f || float.IsNaN(length) || float.IsInfinity(length)) throw new InvalidOperationException("Invalid measured " + label + ": " + length); return length; }
    private static string Normalize(string name) => name.Trim().ToLowerInvariant().Replace(' ', '_');
    private static Vector3 Average(Vector3[] points)
    { Vector3 sum = Vector3.zero; foreach (Vector3 point in points) sum += point; return sum / RequireLength(points.Length, "mesh vertex count"); }
    private static float MaxY(Vector3[] points)
    { float value = float.NegativeInfinity; foreach (Vector3 point in points) value = Mathf.Max(value, point.y); return value; }
}
