using UnityEngine;

/// <summary>Optional desktop controls for the generated laboratory patient; not added to TrainingScene.</summary>
public sealed class AnatomicalPatientLab : MonoBehaviour
{
    public enum View { Skin, Skeleton, Muscles, SkeletonAndMuscles }
    public enum Sample { Rest, ArmsDown, Forward90, Abduction150, Elbow100, Forearm80, NeckFlexion30, NeckRotation45 }
    [SerializeField] private AnatomicalPatientRig rig;
    [SerializeField] private SkinnedMeshRenderer[] skin;
    [SerializeField] private SkinnedMeshRenderer[] skeleton;
    [SerializeField] private SkinnedMeshRenderer[] muscles;
    [SerializeField] private bool showDesktopControls;
    [SerializeField] private View view;
    [SerializeField] private bool hideUpperClothing;
    private bool windowOpen = true;

    public AnatomicalPatientRig Rig => rig;
    public View CurrentView => view;

    public void Configure(AnatomicalPatientRig patientRig, SkinnedMeshRenderer[] skinMeshes,
        SkinnedMeshRenderer[] boneMeshes, SkinnedMeshRenderer[] muscleMeshes)
    {
        rig = patientRig; skin = skinMeshes; skeleton = boneMeshes; muscles = muscleMeshes;
        showDesktopControls = true; // Explicitly enabled only on the laboratory copy.
        SetView(View.Skin);
    }

    public void SetView(View value)
    {
        view = value;
        SetVisible(skin, value == View.Skin);
        SetVisible(skeleton, value == View.Skeleton || value == View.SkeletonAndMuscles);
        SetVisible(muscles, value == View.Muscles || value == View.SkeletonAndMuscles);
        if (hideUpperClothing && skin != null)
            for (int i = 0; i < skin.Length; i++) if (skin[i] != null && skin[i].name == "Shirt") skin[i].enabled = false;
    }

    public void SetUpperClothingVisible(bool on) { hideUpperClothing = !on; SetView(view); }

    private static void SetVisible(SkinnedMeshRenderer[] renderers, bool on)
    {
        if (renderers == null) return;
        for (int i = 0; i < renderers.Length; i++) if (renderers[i] != null) renderers[i].enabled = on;
    }

    public void SetSample(Sample sample)
    {
        if (rig == null) return;
        var pose = rig.RestPose;
        switch (sample)
        {
            case Sample.ArmsDown:
                pose.leftArm.shoulderElevation = pose.rightArm.shoulderElevation = 0f; break;
            case Sample.Forward90:
                pose.leftArm.shoulderElevation = pose.rightArm.shoulderElevation = 90f;
                pose.leftArm.elevationPlane = pose.rightArm.elevationPlane = 0f; break;
            case Sample.Abduction150:
                pose.leftArm.shoulderElevation = pose.rightArm.shoulderElevation = 150f;
                pose.leftArm.elevationPlane = pose.rightArm.elevationPlane = 90f; break;
            case Sample.Elbow100:
                pose.leftArm.shoulderElevation = pose.rightArm.shoulderElevation = 35f;
                pose.leftArm.elbowFlexion = pose.rightArm.elbowFlexion = 100f; break;
            case Sample.Forearm80:
                pose.leftArm.shoulderElevation = pose.rightArm.shoulderElevation = 0f;
                pose.leftArm.elbowFlexion = pose.rightArm.elbowFlexion = 90f;
                // The signed proximal->distal rotation axis has opposite anatomical
                // handedness across the body. This preset shows mirrored forearm rotation.
                pose.leftArm.forearmRotation = 80f; pose.rightArm.forearmRotation = -80f; break;
            case Sample.NeckFlexion30: pose.neckDegrees.x = 30f; break;
            case Sample.NeckRotation45: pose.neckDegrees.z = 45f; break;
        }
        rig.SetInputSource(AnatomicalPatientRig.InputSource.Manual);
        rig.manualPose = pose;
        rig.Evaluate(pose);
    }

    private void Awake()
    {
        if (rig == null) { Debug.LogError("[AnatomicalLab] Missing explicit patient rig reference.", this); enabled = false; return; }
        SetView(view);
    }

    private void OnGUI()
    {
        if (!Application.isEditor || !showDesktopControls || rig == null) return;
        if (!windowOpen) { if (GUI.Button(new Rect(12, 12, 160, 28), "Patient controls")) windowOpen = true; return; }
        GUILayout.BeginArea(new Rect(12, 12, 260, 620), GUI.skin.box);
        GUILayout.Label("Anatomical patient laboratory");
        GUILayout.Label("Kinematic prototype / unvalidated");
        if (GUILayout.Button("Hide controls")) windowOpen = false;
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Skin")) SetView(View.Skin);
        if (GUILayout.Button("Bones")) SetView(View.Skeleton);
        if (GUILayout.Button("Muscles")) SetView(View.Muscles);
        GUILayout.EndHorizontal();
        if (GUILayout.Button(hideUpperClothing ? "Show shirt" : "Inspect skin without shirt")) SetUpperClothingVisible(hideUpperClothing);
        if (GUILayout.Button("Rest T pose")) SetSample(Sample.Rest);
        if (GUILayout.Button("Arms down")) SetSample(Sample.ArmsDown);
        if (GUILayout.Button("Forward 90")) SetSample(Sample.Forward90);
        if (GUILayout.Button("Abduction 150")) SetSample(Sample.Abduction150);
        if (GUILayout.Button("Elbow 100")) SetSample(Sample.Elbow100);
        if (GUILayout.Button("Forearm 80")) SetSample(Sample.Forearm80);
        if (GUILayout.Button("Neck flexion 30")) SetSample(Sample.NeckFlexion30);
        if (GUILayout.Button("Neck rotation 45")) SetSample(Sample.NeckRotation45);
        var p = rig.manualPose;
        GUILayout.Label("Left shoulder elevation"); p.leftArm.shoulderElevation = GUILayout.HorizontalSlider(p.leftArm.shoulderElevation, 0f, 160f);
        GUILayout.Label("Left elbow flexion"); p.leftArm.elbowFlexion = GUILayout.HorizontalSlider(p.leftArm.elbowFlexion, Mathf.Min(0f, rig.RestPose.leftArm.elbowFlexion), 135f);
        GUILayout.Label("Left forearm rotation"); p.leftArm.forearmRotation = GUILayout.HorizontalSlider(p.leftArm.forearmRotation, -80f, 80f);
        GUILayout.Label("Neck flexion / extension"); p.neckDegrees.x = GUILayout.HorizontalSlider(p.neckDegrees.x, -35f, 35f);
        GUILayout.Label("Neck rotation"); p.neckDegrees.z = GUILayout.HorizontalSlider(p.neckDegrees.z, -60f, 60f);
        rig.manualPose = p;
        GUILayout.EndArea();
    }
}
