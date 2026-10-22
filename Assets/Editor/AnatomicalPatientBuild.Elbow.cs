using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static partial class AnatomicalPatientBuild
{
    // Measurement only: source c9 is posed in a disposable preview scene. Neither
    // the user's animation input nor its transforms are modified to find the pole.
    private static Vector3[] MeasureAvatarElbowPoles(Animator target, Transform space)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        GameObject source = null;
        HumanPoseHandler handler = null;
        try
        {
            source = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PatientPath));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(source, scene);
            Animator animator = source.GetComponent<Animator>();
            handler = new HumanPoseHandler(animator.avatar, animator.transform);
            var original = new HumanPose(); handler.GetHumanPose(ref original);
            var poles = new Vector3[2];
            string[] names = HumanTrait.MuscleName;
            for (int side = 0; side < 2; side++)
            {
                string muscleName = (side == 0 ? "Left" : "Right") + " Forearm Stretch";
                int muscle = Array.IndexOf(names, muscleName);
                if (muscle < 0) throw new InvalidOperationException("Missing c9 Humanoid muscle: " + muscleName);
                var pose = original; pose.muscles = (float[])original.muscles.Clone(); pose.muscles[muscle] = -.5f;
                handler.SetHumanPose(ref pose);
                HumanBodyBones upperId = side == 0 ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm;
                HumanBodyBones lowerId = side == 0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm;
                HumanBodyBones handId = side == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
                Transform upper = animator.GetBoneTransform(upperId), lower = animator.GetBoneTransform(lowerId), hand = animator.GetBoneTransform(handId);
                Vector3 u = (lower.position - upper.position).normalized, f = (hand.position - lower.position).normalized;
                // Remove the longitudinal component to measure only the elbow's bend plane.
                Vector3 measured = (f - u * Vector3.Dot(f, u)).normalized;
                Vector3 localPole = Quaternion.Inverse(upper.rotation) * measured;
                Transform targetUpper = target.GetBoneTransform(upperId), targetLower = target.GetBoneTransform(lowerId);
                Vector3 authorAxis = space.InverseTransformDirection(targetLower.position - targetUpper.position).normalized;
                Vector3 pole = Quaternion.Inverse(space.rotation) * targetUpper.rotation * localPole;
                poles[side] = (pole - authorAxis * Vector3.Dot(pole, authorAxis)).normalized;
                if (Vector3.Dot(poles[side], Vector3.forward) < .8f)
                    throw new InvalidOperationException("Measured c9 elbow pole does not point anterior: " + muscleName);
                Debug.Log("[AnatomicalElbow] c9 " + muscleName + "=-0.5; measured flexion=" + Vector3.Angle(u, f) +
                    "; author-space pole=" + poles[side].ToString("F6"));
            }
            return poles;
        }
        finally
        {
            if (handler != null) handler.Dispose();
            if (source != null) UnityEngine.Object.DestroyImmediate(source);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    [MenuItem("GuideChuna/시험/해부학 환자/최신 복제본 팔꿈치 기준 교정")]
    private static void CalibrateLatestElbows()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before saving elbow calibration.");
        var receipt = JsonUtility.FromJson<BuildReceipt>(File.ReadAllText(OutputFolder + "/LatestBuild.json"));
        if (!receipt.prefab.StartsWith(OutputFolder + "/", StringComparison.Ordinal)) throw new InvalidOperationException("Only a generated patient can be calibrated.");
        GameObject asset = PrefabUtility.LoadPrefabContents(receipt.prefab);
        try
        {
            var rig = asset.GetComponent<AnatomicalPatientRig>();
            CalibrateElbowFields(rig, false);
            rig.Evaluate(rig.RestPose);
            PrefabUtility.SaveAsPrefabAsset(asset, receipt.prefab);
        }
        finally { PrefabUtility.UnloadPrefabContents(asset); }
        // Existing loaded scene values can override prefab/default changes. Update
        // these exact generated instances too, without saving the user's scene.
        foreach (var rig in Resources.FindObjectsOfTypeAll<AnatomicalPatientRig>())
        {
            if (EditorUtility.IsPersistent(rig) || !rig.gameObject.scene.IsValid() || EditorSceneManager.IsPreviewSceneObject(rig)) continue;
            if (PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(rig.gameObject) != receipt.prefab) continue;
            CalibrateElbowFields(rig, true);
            rig.Evaluate(rig.manualPose);
            EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);
        }
        Debug.Log("[AnatomicalElbow] Saved explicit measured elbow poles/signed author-rest angles to generated prefab; loaded copies updated without saving their scenes.");
    }

    private static void CalibrateElbowFields(AnatomicalPatientRig rig, bool undo)
    {
        Animator animator = rig.GetComponentInChildren<Animator>(true);
        Vector3[] poles = MeasureAvatarElbowPoles(animator, rig.RigSpace);
        if (undo) Undo.RecordObject(rig, "Calibrate measured c9 elbow flexion");
        var data = new SerializedObject(rig);
        for (int side = 0; side < 2; side++)
        {
            var limb = data.FindProperty("limbs").GetArrayElementAtIndex(side);
            Vector3 shoulder = limb.FindPropertyRelative("shoulder").vector3Value;
            Vector3 elbow = limb.FindPropertyRelative("elbow").vector3Value;
            Vector3 wrist = limb.FindPropertyRelative("wrist").vector3Value;
            Vector3 upper = (elbow - shoulder).normalized, lower = (wrist - elbow).normalized;
            float signedRest = Mathf.Atan2(Vector3.Dot(lower, poles[side]), Vector3.Dot(lower, upper)) * Mathf.Rad2Deg;
            float oldRest = limb.FindPropertyRelative("restPose").FindPropertyRelative("elbowFlexion").floatValue;
            limb.FindPropertyRelative("restBendDirection").vector3Value = poles[side];
            limb.FindPropertyRelative("restPose").FindPropertyRelative("elbowFlexion").floatValue = signedRest;
            string arm = side == 0 ? "leftArm" : "rightArm";
            data.FindProperty("restPose").FindPropertyRelative(arm).FindPropertyRelative("elbowFlexion").floatValue = signedRest;
            var manual = data.FindProperty("manualPose").FindPropertyRelative(arm).FindPropertyRelative("elbowFlexion");
            if (Mathf.Abs(manual.floatValue - oldRest) < .01f) manual.floatValue = signedRest;
            Debug.Log("[AnatomicalElbow] " + rig.name + " side=" + side + "; explicit saved rest=" + oldRest + " -> " + signedRest +
                "; forward pole=" + poles[side].ToString("F6"));
        }
        data.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(rig);
    }
}
