using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static partial class AnatomicalPatientBuild
{
    [MenuItem("GuideChuna/시험/해부학 환자/팔꿈치 방향 계측")]
    private static void MeasureElbowDirections()
    {
        string receiptPath = File.Exists("Temp/model-audit/latest_build.json") ? "Temp/model-audit/latest_build.json" : OutputFolder + "/LatestBuild.json";
        var receipt = JsonUtility.FromJson<BuildReceipt>(File.ReadAllText(receiptPath));
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject copy = null, source = null;
        HumanPoseHandler handler = null;
        try
        {
            copy = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(receipt.prefab));
            source = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PatientPath));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(copy, scene);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(source, scene);
            var lab = copy.GetComponent<AnatomicalPatientLab>();
            var serialized = new SerializedObject(lab.Rig);
            var report = new System.Text.StringBuilder();
            foreach (var sample in new[] { AnatomicalPatientLab.Sample.Elbow100, AnatomicalPatientLab.Sample.Forearm80 })
            {
                lab.SetSample(sample);
                for (int side = 0; side < 2; side++)
                {
                    var limb = serialized.FindProperty("limbs").GetArrayElementAtIndex(side);
                    Transform humerus = (Transform)limb.FindPropertyRelative("humerus").objectReferenceValue;
                    Transform ulna = (Transform)limb.FindPropertyRelative("ulna").objectReferenceValue;
                    Transform hand = (Transform)limb.FindPropertyRelative("hand").objectReferenceValue;
                    Vector3 upper = (ulna.localPosition - humerus.localPosition).normalized;
                    Vector3 lower = (hand.localPosition - ulna.localPosition).normalized;
                    Vector3 bend = (lower - upper * Vector3.Dot(upper, lower)).normalized;
                    report.AppendLine(sample + " side=" + side + " savedRestBend=" + limb.FindPropertyRelative("restBendDirection").vector3Value.ToString("F6") +
                        " upper=" + upper.ToString("F6") + " lower=" + lower.ToString("F6") + " bend=" + bend.ToString("F6") + " anterior=" + bend.z.ToString("F6"));
                }
            }
            Animator animator = source.GetComponent<Animator>();
            Quaternion[] authorUpperRotations = { animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).rotation, animator.GetBoneTransform(HumanBodyBones.RightUpperArm).rotation };
            Vector3[] authorUpperDirections = {
                (animator.GetBoneTransform(HumanBodyBones.LeftLowerArm).position - animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).position).normalized,
                (animator.GetBoneTransform(HumanBodyBones.RightLowerArm).position - animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position).normalized };
            handler = new HumanPoseHandler(animator.avatar, animator.transform);
            var original = new HumanPose(); handler.GetHumanPose(ref original);
            string[] names = HumanTrait.MuscleName;
            for (int muscle = 0; muscle < names.Length; muscle++)
            {
                if (!names[muscle].Contains("Forearm Stretch")) continue;
                bool left = names[muscle].StartsWith("Left", StringComparison.Ordinal);
                foreach (float value in new[] { -1f, -.5f, .5f, 1f })
                {
                    var pose = original; pose.muscles = (float[])original.muscles.Clone(); pose.muscles[muscle] = value;
                    handler.SetHumanPose(ref pose);
                    Vector3 shoulder = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm).position;
                    Vector3 elbow = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm).position;
                    Vector3 wrist = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand).position;
                    Vector3 upper = (elbow - shoulder).normalized, lower = (wrist - elbow).normalized;
                    Vector3 bend = (lower - upper * Vector3.Dot(upper, lower)).normalized;
                    Transform posedUpper = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                    Vector3 transported = authorUpperRotations[left ? 0 : 1] * Quaternion.Inverse(posedUpper.rotation) * bend;
                    Vector3 authorAxis = authorUpperDirections[left ? 0 : 1];
                    Vector3 authorBend = (transported - authorAxis * Vector3.Dot(transported, authorAxis)).normalized;
                    report.AppendLine("c9 Avatar " + names[muscle] + " input=" + value + " flexion=" + Vector3.Angle(upper, lower).ToString("F4") +
                        " bend=" + bend.ToString("F6") + " anterior=" + bend.z.ToString("F6") + " authorPole=" + authorBend.ToString("F6"));
                }
            }
            Directory.CreateDirectory("Temp/model-audit");
            File.WriteAllText("Temp/model-audit/elbow-directions.txt", report.ToString());
            Debug.Log("[AnatomicalElbowProbe]\n" + report);
        }
        finally
        {
            if (handler != null) handler.Dispose();
            if (copy != null) UnityEngine.Object.DestroyImmediate(copy);
            if (source != null) UnityEngine.Object.DestroyImmediate(source);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    [Serializable] private class Verification
    {
        public string prefab;
        public bool success;
        public float neutralMaximumErrorMetres;
        public float repeatedPoseErrorMetres;
        public float expressionMaximumErrorMetres;
        public int expressionFramesChecked;
        public float animationInputRadiusDegrees;
        public float radiusRotationDegrees;
        public float ulnaRotationDuringPronationDegrees;
        public float independentScapulaRotationDegrees;
        public float scapularMirrorErrorDegrees;
        public float scapularSuperiorForwardComponent;
        public float neckEndpointErrorDegrees;
        public float elbowBendMinimumForward = 1f;
        public float elbowMaximumAngleErrorDegrees;
        public float forearmPresetMirrorErrorDegrees;
        public int skinVerticesMoved;
        public int validSkinnedMeshes;
        public List<MeshError> neutralByMesh = new List<MeshError>();
        public string failure;
        public string scope = "Editor evaluation and CPU skin bake using the runtime solver. Human Play, Quest, tissue/clinical validation remain unverified.";
    }

    [Serializable] private class MeshError { public string mesh; public float maximumMetres; }

    [MenuItem("GuideChuna/시험/해부학 환자/최신 복제본 검증 및 이미지")]
    public static void VerifyLatest()
    {
        Directory.CreateDirectory("Temp/model-audit");
        string receiptPath = File.Exists("Temp/model-audit/latest_build.json") ? "Temp/model-audit/latest_build.json" : OutputFolder + "/LatestBuild.json";
        var receipt = JsonUtility.FromJson<BuildReceipt>(File.ReadAllText(receiptPath));
        var report = new Verification { prefab = receipt.prefab };
        var preview = new PreviewRenderUtility();
        GameObject original = null;
        UnityEngine.SceneManagement.Scene sourceScene = EditorSceneManager.NewPreviewScene();
        try
        {
            if (File.Exists("Temp/model-audit/build_failure.txt")) throw new InvalidOperationException("The latest build failed; refusing to validate an older prefab. " + File.ReadAllText("Temp/model-audit/build_failure.txt"));
            GameObject root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(receipt.prefab));
            preview.AddSingleGO(root);
            var rig = root.GetComponent<AnatomicalPatientRig>();
            var lab = root.GetComponent<AnatomicalPatientLab>();
            if (rig == null || lab == null) throw new InvalidOperationException("Missing rig or laboratory component on saved prefab.");
            rig.SetAutomaticEvaluation(false);
            Require(rig.Evaluate(rig.RestPose), "Cannot evaluate the saved author-rest pose.");
            original = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PatientPath));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(original, sourceScene);
            var originalRenderers = new Dictionary<string, SkinnedMeshRenderer>();
            foreach (var r in original.GetComponentsInChildren<SkinnedMeshRenderer>(true)) originalRenderers.Add(r.name, r);
            var body = root.transform.Find("c9_AnimationInput").GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var originalPoints = new Dictionary<string, Vector3[]>();
            foreach (var r in body)
            {
                Vector3[] before = BakeWorld(originalRenderers[r.name]);
                Vector3[] after = BakeWorld(r);
                originalPoints.Add(r.name, after);
                Require(before.Length == after.Length, "Skin topology changed: " + r.name);
                float error = 0f;
                for (int i = 0; i < before.Length; i++) error = Mathf.Max(error, Vector3.Distance(before[i], after[i]));
                report.neutralMaximumErrorMetres = Mathf.Max(report.neutralMaximumErrorMetres, error);
                report.neutralByMesh.Add(new MeshError { mesh = r.name, maximumMetres = error });
            }
            Require(report.neutralMaximumErrorMetres < .0001f, "Original c9 appearance changed at rest: " + report.neutralMaximumErrorMetres + " metres.");
            foreach (var r in body)
            {
                var beforeRenderer = originalRenderers[r.name];
                int count = Mathf.Min(3, beforeRenderer.sharedMesh.blendShapeCount);
                for (int shape = 0; shape < count; shape++)
                {
                    Require(beforeRenderer.sharedMesh.GetBlendShapeName(shape) == r.sharedMesh.GetBlendShapeName(shape), "Original expression names/order changed.");
                    float oldBefore = beforeRenderer.GetBlendShapeWeight(shape), oldAfter = r.GetBlendShapeWeight(shape);
                    beforeRenderer.SetBlendShapeWeight(shape, 75f); r.SetBlendShapeWeight(shape, 75f);
                    Vector3[] before = BakeWorld(beforeRenderer), after = BakeWorld(r);
                    for (int i = 0; i < before.Length; i++) report.expressionMaximumErrorMetres = Mathf.Max(report.expressionMaximumErrorMetres, Vector3.Distance(before[i], after[i]));
                    beforeRenderer.SetBlendShapeWeight(shape, oldBefore); r.SetBlendShapeWeight(shape, oldAfter);
                    report.expressionFramesChecked++;
                }
            }
            Require(report.expressionMaximumErrorMetres < .0001f, "Original face/body expression transport changed the rendered shape.");
            foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Require(r.sharedMesh != null && r.bones.Length > 0, "Unbound mesh: " + r.name);
                foreach (Transform bone in r.bones) Require(bone != null, "Missing renderer bone: " + r.name);
                foreach (BoneWeight w in r.sharedMesh.boneWeights)
                    Require(Mathf.Abs(w.weight0 + w.weight1 + w.weight2 + w.weight3 - 1f) < .001f, "Unnormalized weight: " + r.name);
                report.validSkinnedMeshes++;
            }
            var data = new SerializedObject(rig);
            var limb = data.FindProperty("limbs").GetArrayElementAtIndex(1);
            Transform radius = (Transform)limb.FindPropertyRelative("radius").objectReferenceValue;
            Transform ulna = (Transform)limb.FindPropertyRelative("ulna").objectReferenceValue;
            Transform scapula = (Transform)limb.FindPropertyRelative("scapula").objectReferenceValue;
            Transform clavicle = (Transform)limb.FindPropertyRelative("clavicle").objectReferenceValue;
            Quaternion radialRest = radius.localRotation, ulnaRest = ulna.localRotation;
            Transform inputHand = (Transform)limb.FindPropertyRelative("sourceHand").objectReferenceValue;
            Transform inputLower = (Transform)limb.FindPropertyRelative("sourceLower").objectReferenceValue;
            Quaternion inputHandRest = inputHand.localRotation;
            inputHand.rotation = Quaternion.AngleAxis(60f, (inputHand.position - inputLower.position).normalized) * inputHand.rotation;
            rig.SetInputSource(AnatomicalPatientRig.InputSource.Animation);
            Require(rig.Evaluate(), "c9 input evaluation failed.");
            report.animationInputRadiusDegrees = Quaternion.Angle(radialRest, radius.localRotation);
            Require(Mathf.Abs(report.animationInputRadiusDegrees - 60f) < .25f, "Detailed radius did not follow c9 final input pose.");
            inputHand.localRotation = inputHandRest;
            rig.SetInputSource(AnatomicalPatientRig.InputSource.Manual);
            rig.Evaluate(rig.RestPose);
            var pose = rig.RestPose;
            pose.rightArm.forearmRotation = 60f;
            Require(rig.Evaluate(pose), "Pronation evaluation failed.");
            report.radiusRotationDegrees = Quaternion.Angle(radialRest, radius.localRotation);
            report.ulnaRotationDuringPronationDegrees = Quaternion.Angle(ulnaRest, ulna.localRotation);
            Require(Mathf.Abs(report.radiusRotationDegrees - 60f) < .2f, "Radius did not rotate through the requested angle.");
            Require(report.ulnaRotationDuringPronationDegrees < .2f, "Pronation incorrectly rotated the ulna.");
            rig.Evaluate(rig.RestPose);
            pose = rig.RestPose; pose.rightArm.shoulderElevation = 150f; pose.rightArm.elevationPlane = 90f;
            Require(rig.Evaluate(pose), "Shoulder evaluation failed.");
            report.independentScapulaRotationDegrees = Quaternion.Angle(scapula.localRotation, clavicle.localRotation);
            Require(report.independentScapulaRotationDegrees > 1f, "Scapula remains locked to clavicle.");
            rig.Evaluate(rig.RestPose);
            Transform leftScapula = (Transform)data.FindProperty("limbs").GetArrayElementAtIndex(0).FindPropertyRelative("scapula").objectReferenceValue;
            Quaternion leftScapulaRest = leftScapula.localRotation, rightScapulaRest = scapula.localRotation;
            pose.leftArm.shoulderElevation = 150f; pose.leftArm.elevationPlane = 90f;
            rig.Evaluate(pose);
            Quaternion leftDelta = leftScapula.localRotation * Quaternion.Inverse(leftScapulaRest);
            Quaternion rightDelta = scapula.localRotation * Quaternion.Inverse(rightScapulaRest);
            report.scapularSuperiorForwardComponent = Vector3.Dot(rightDelta * Vector3.up, Vector3.forward);
            Debug.Log("[AnatomicalPatient] Measured superior scapular direction toward anterior=" + report.scapularSuperiorForwardComponent);
            foreach (Vector3 axis in new[] { Vector3.right, Vector3.up, Vector3.forward })
            {
                Vector3 reflectedAxis = new Vector3(-axis.x, axis.y, axis.z);
                Vector3 rightResult = rightDelta * axis;
                Vector3 reflectedResult = new Vector3(-rightResult.x, rightResult.y, rightResult.z);
                report.scapularMirrorErrorDegrees = Mathf.Max(report.scapularMirrorErrorDegrees, Vector3.Angle(leftDelta * reflectedAxis, reflectedResult));
            }
            Debug.Log("[AnatomicalPatient] Measured bilateral scapular mirror error=" + report.scapularMirrorErrorDegrees + " degrees.");
            Require(report.scapularMirrorErrorDegrees < .1f, "Bilateral scapular motion is not mirrored across the sagittal plane.");
            Require(report.scapularSuperiorForwardComponent < -.01f, "Scapular superior direction tilted anterior instead of posterior during elevation.");
            foreach (var r in body)
            {
                Vector3[] points = BakeWorld(r);
                for (int i = 0; i < points.Length; i++)
                {
                    Require(FinitePoint(points[i]), "Non-finite skin point.");
                    if (Vector3.Distance(points[i], originalPoints[r.name][i]) > .0001f) report.skinVerticesMoved++;
                }
            }
            Require(report.skinVerticesMoved > 100, "Detailed shoulder motion is not reaching the c9 skin.");
            pose = rig.RestPose; pose.neckDegrees = new Vector3(30f, 15f, 35f);
            Require(rig.Evaluate(pose), "Combined cervical evaluation failed.");
            report.neckEndpointErrorDegrees = rig.LastDiagnostics.neckEndpointRotationError;
            Require(report.neckEndpointErrorDegrees < .1f, "Head and upper cervical endpoint disagree.");
            for (int i = 0; i < 120; i++) { rig.Evaluate(pose); rig.Evaluate(rig.RestPose); }
            foreach (var r in body)
            {
                Vector3[] points = BakeWorld(r);
                for (int i = 0; i < points.Length; i++) report.repeatedPoseErrorMetres = Mathf.Max(report.repeatedPoseErrorMetres, Vector3.Distance(points[i], originalPoints[r.name][i]));
            }
            Require(report.repeatedPoseErrorMetres < .00001f, "Repeated poses accumulated skin drift.");
            foreach (var sample in new[] { AnatomicalPatientLab.Sample.Elbow100, AnatomicalPatientLab.Sample.Forearm80 })
            {
                lab.SetSample(sample);
                for (int side = 0; side < 2; side++)
                {
                    var arm = data.FindProperty("limbs").GetArrayElementAtIndex(side);
                    Transform h = (Transform)arm.FindPropertyRelative("humerus").objectReferenceValue;
                    Transform e = (Transform)arm.FindPropertyRelative("ulna").objectReferenceValue;
                    Transform w = (Transform)arm.FindPropertyRelative("hand").objectReferenceValue;
                    Vector3 upper = (e.localPosition - h.localPosition).normalized, lower = (w.localPosition - e.localPosition).normalized;
                    Vector3 bend = (lower - upper * Vector3.Dot(upper, lower)).normalized;
                    float requested = sample == AnatomicalPatientLab.Sample.Elbow100 ? 100f : 90f;
                    report.elbowBendMinimumForward = Mathf.Min(report.elbowBendMinimumForward, bend.z);
                    report.elbowMaximumAngleErrorDegrees = Mathf.Max(report.elbowMaximumAngleErrorDegrees, Mathf.Abs(Vector3.Angle(upper, lower) - requested));
                    Debug.Log("[AnatomicalElbow] " + sample + " side=" + side + " requested=" + requested +
                        "; measured=" + Vector3.Angle(upper, lower) + "; anterior component=" + bend.z);
                }
            }
            Require(report.elbowBendMinimumForward > .95f, "Elbow preset bends posterior instead of anterior.");
            Require(report.elbowMaximumAngleErrorDegrees < .1f, "Elbow preset does not match its requested angle.");
            var forearmPose = rig.CurrentPose;
            Transform leftHand = (Transform)data.FindProperty("limbs").GetArrayElementAtIndex(0).FindPropertyRelative("hand").objectReferenceValue;
            Transform rightHand = (Transform)data.FindProperty("limbs").GetArrayElementAtIndex(1).FindPropertyRelative("hand").objectReferenceValue;
            var neutralForearm = forearmPose; neutralForearm.leftArm.forearmRotation = neutralForearm.rightArm.forearmRotation = 0f;
            rig.Evaluate(neutralForearm);
            Quaternion leftHandRest = leftHand.localRotation, rightHandRest = rightHand.localRotation;
            rig.Evaluate(forearmPose);
            Quaternion leftRoll = leftHand.localRotation * Quaternion.Inverse(leftHandRest), rightRoll = rightHand.localRotation * Quaternion.Inverse(rightHandRest);
            foreach (Vector3 axis in new[] { Vector3.right, Vector3.up, Vector3.forward })
            {
                Vector3 reflectedAxis = new Vector3(-axis.x, axis.y, axis.z);
                Vector3 rightResult = rightRoll * axis;
                report.forearmPresetMirrorErrorDegrees = Mathf.Max(report.forearmPresetMirrorErrorDegrees,
                    Vector3.Angle(leftRoll * reflectedAxis, new Vector3(-rightResult.x, rightResult.y, rightResult.z)));
            }
            Require(report.forearmPresetMirrorErrorDegrees < .1f, "Forearm preset rotates the two sides in opposing anatomical directions.");
            string imageFolder = "Temp/model-audit/previews";
            Directory.CreateDirectory(imageFolder);
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.backgroundColor = new Color(.14f, .16f, .19f);
            preview.camera.fieldOfView = 32f;
            preview.camera.nearClipPlane = .01f; preview.camera.farClipPlane = 20f;
            preview.lights[0].intensity = 1.3f; preview.lights[0].transform.rotation = Quaternion.Euler(35f, -35f, 0f);
            preview.lights[1].intensity = 1f; preview.lights[1].transform.rotation = Quaternion.Euler(25f, 145f, 0f);
            foreach (AnatomicalPatientLab.Sample sample in new[] { AnatomicalPatientLab.Sample.Rest, AnatomicalPatientLab.Sample.ArmsDown,
                AnatomicalPatientLab.Sample.Abduction150, AnatomicalPatientLab.Sample.Elbow100, AnatomicalPatientLab.Sample.Forearm80,
                AnatomicalPatientLab.Sample.NeckFlexion30, AnatomicalPatientLab.Sample.NeckRotation45 })
            {
                lab.SetSample(sample);
                foreach (AnatomicalPatientLab.View view in new[] { AnatomicalPatientLab.View.Skin, AnatomicalPatientLab.View.Skeleton, AnatomicalPatientLab.View.Muscles })
                {
                    lab.SetView(view);
                    preview.camera.transform.position = new Vector3(2.2f, 1.55f, 3.4f);
                    preview.camera.transform.LookAt(new Vector3(0f, 1.25f, 0f));
                    CapturePreview(preview, root, imageFolder + "/" + sample + "_" + view + ".png", preview.camera.transform.position, new Vector3(0f, 1.25f, 0f));
                }
                lab.SetView(AnatomicalPatientLab.View.Skin);
                lab.SetUpperClothingVisible(false);
                CapturePreview(preview, root, imageFolder + "/" + sample + "_BareSkin.png", new Vector3(1.4f, 1.65f, 2.2f), new Vector3(0f, 1.35f, 0f));
                CapturePreview(preview, root, imageFolder + "/" + sample + "_BackSkin.png", new Vector3(-.5f, 1.6f, -1.8f), new Vector3(0f, 1.42f, 0f));
                lab.SetUpperClothingVisible(true);
            }
            report.success = true;
        }
        catch (Exception e) { report.failure = e.ToString(); Debug.LogException(e); }
        finally
        {
            preview.Cleanup();
            if (original != null) UnityEngine.Object.DestroyImmediate(original);
            EditorSceneManager.ClosePreviewScene(sourceScene);
            File.WriteAllText("Temp/model-audit/verification.json", JsonUtility.ToJson(report, true));
        }
        Debug.Log("[AnatomicalPatient] Verification success=" + report.success + " | rest error=" + report.neutralMaximumErrorMetres + "m | skin moved=" + report.skinVerticesMoved + " | drift=" + report.repeatedPoseErrorMetres + "m. Temp/model-audit/verification.json");
    }

    private static Vector3[] BakeWorld(SkinnedMeshRenderer renderer)
    {
        var mesh = new Mesh();
        try
        {
            renderer.BakeMesh(mesh);
            Vector3[] points = mesh.vertices;
            for (int i = 0; i < points.Length; i++) points[i] = renderer.transform.TransformPoint(points[i]);
            return points;
        }
        finally { UnityEngine.Object.DestroyImmediate(mesh); }
    }

    private static void CapturePreview(PreviewRenderUtility preview, GameObject root, string path, Vector3 position, Vector3 target)
    {
        // Multiple poses render in the same Editor tick. Native skinned preview was
        // measured one pose behind for Body. Snapshot the runtime result with the
        // same CPU BakeMesh path as the numerical checks; do not alter runtime skinning.
        var snapshots = new List<GameObject>();
        var meshes = new List<Mesh>();
        var hidden = new List<SkinnedMeshRenderer>();
        Texture2D image = null;
        try
        {
            foreach (var source in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!source.enabled || !source.gameObject.activeInHierarchy || source.forceRenderingOff) continue;
                var mesh = new Mesh(); meshes.Add(mesh); source.BakeMesh(mesh);
                var snapshot = new GameObject("BakedPreview"); snapshots.Add(snapshot);
                snapshot.transform.SetParent(source.transform, false); snapshot.layer = source.gameObject.layer;
                snapshot.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = snapshot.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = source.sharedMaterials;
                renderer.shadowCastingMode = source.shadowCastingMode;
                renderer.receiveShadows = source.receiveShadows;
                source.enabled = false; hidden.Add(source);
            }
            preview.camera.transform.position = position; preview.camera.transform.LookAt(target);
            preview.BeginStaticPreview(new Rect(0f, 0f, 1100f, 1000f)); preview.Render();
            image = preview.EndStaticPreview();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            if (image != null) UnityEngine.Object.DestroyImmediate(image);
            foreach (var source in hidden) source.enabled = true;
            foreach (var snapshot in snapshots) UnityEngine.Object.DestroyImmediate(snapshot);
            foreach (var mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    private static bool FinitePoint(Vector3 point)
    {
        return !float.IsNaN(point.x) && !float.IsInfinity(point.x) && !float.IsNaN(point.y) && !float.IsInfinity(point.y) && !float.IsNaN(point.z) && !float.IsInfinity(point.z);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
