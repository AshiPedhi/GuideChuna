using System;
using UnityEditor;
using UnityEngine;

/// <summary>Explicit edit-mode preview restricted to the new isolated demonstration scene.</summary>
[CustomEditor(typeof(Rib2VideoTextScenePlayer))]
public sealed class Rib2VideoTextScenePlayerEditor : Editor
{
    private const string DemoScenePrefix = "Assets/Demos/Rib2VideoTextScene/";
    private Rib2VideoTextScenePlayer player;
    private Rib2DemoPatientArm arm;
    private Rib2DemoSceneControls controls;
    private bool previewPlaying;
    private bool halfSpeed;
    private double previousUpdate;
    private float endTime;
    private string previewError;

    private void OnEnable()
    {
        player = target as Rib2VideoTextScenePlayer;
        AssemblyReloadEvents.beforeAssemblyReload += StopPreview;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        ResolvePeers();
    }

    private void OnDisable()
    {
        StopPreview();
        AssemblyReloadEvents.beforeAssemblyReload -= StopPreview;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
    }

    public override void OnInspectorGUI()
    {
        if (DrawDefaultInspector()) { StopPreview(); ResolvePeers(); }
        if (controls != null) halfSpeed = controls.IsHalfSpeed;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("시연 미리보기", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("새 시연씬의 복제 손과 환자 팔만 움직입니다. 영상·텍스트로 제작한 깊이 가정이며 실제 판정은 실행하지 않습니다.", MessageType.Info);
        bool allowed = IsDraftScene();
        if (!allowed) EditorGUILayout.HelpBox("미리보기는 Assets/Demos/Rib2VideoTextScene/ 아래의 별도 시연씬에서만 사용할 수 있습니다.", MessageType.Warning);
        using (new EditorGUI.DisabledScope(!allowed))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("시작 파지")) StartPose();
                if (GUILayout.Button("전체 재생")) StartFull();
                if (GUILayout.Button("팔 한 주기")) StartCycle();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("일시정지")) { StopPreview(); if (player != null) player.SetPlaying(false); }
                bool nextHalf = GUILayout.Toggle(halfSpeed, "0.5배", "Button");
                if (nextHalf != halfSpeed)
                {
                    halfSpeed = nextHalf;
                    if (controls != null) controls.SetHalfSpeed(halfSpeed);
                    else if (player != null) player.SetHalfSpeed(halfSpeed);
                }
            }
            float duration = player != null ? player.Duration : 0f;
            float current = player != null ? player.CurrentTime : 0f;
            EditorGUI.BeginChangeCheck();
            float time = EditorGUILayout.Slider("시간(초)", current, 0f, Mathf.Max(.001f, duration));
            if (EditorGUI.EndChangeCheck())
            {
                StopPreview();
                if (Prepare()) { player.SetPlaying(false); Sample(time); }
            }
        }
        if (!string.IsNullOrEmpty(previewError)) EditorGUILayout.HelpBox(previewError, MessageType.Warning);
    }

    private void StartPose()
    {
        StopPreview();
        if (!Prepare()) return;
        player.ShowStartPose();
        Sample(player.CurrentTime);
    }

    private void StartFull()
    {
        StopPreview();
        if (!Prepare()) return;
        player.PlayFull();
        if (!Sample(player.CurrentTime)) { player.SetPlaying(false); return; }
        if (!Application.isPlaying) BeginPreview();
    }

    private void StartCycle()
    {
        StopPreview();
        if (!Prepare()) return;
        player.PlayCycle();
        if (!Sample(player.CurrentTime)) { player.SetPlaying(false); return; }
        if (!Application.isPlaying) BeginPreview();
    }

    private bool Prepare()
    {
        if (!IsDraftScene()) { previewError = "별도 시연씬을 선택하세요."; return false; }
        ResolvePeers();
        if (arm == null) { previewError = "이 플레이어에 연결된 시연 환자 팔이 없습니다."; return false; }
        if (!ReferencesStayInScene()) { previewError = "손·환자·기준틀 참조는 모두 이 시연씬에 있어야 합니다."; return false; }
        if (!player.TryInitialize(out previewError) || !arm.Initialize(out previewError)) return false;
        if (controls != null) controls.SetHalfSpeed(halfSpeed);
        else player.SetHalfSpeed(halfSpeed);
        previewError = null;
        return true;
    }

    private void BeginPreview()
    {
        player.SetPlaying(false);
        endTime = player.Duration;
        if (player.CurrentTime >= endTime) return;
        previousUpdate = EditorApplication.timeSinceStartup;
        previewPlaying = true;
        EditorApplication.update += TickPreview;
    }

    private void TickPreview()
    {
        if (!previewPlaying || player == null || arm == null || EditorApplication.isPlayingOrWillChangePlaymode || !IsDraftScene())
        { StopPreview(); return; }
        if (!ReferencesStayInScene()) { previewError = "참조가 다른 씬으로 이동해 미리보기를 정지했습니다."; StopPreview(); return; }
        double now = EditorApplication.timeSinceStartup;
        float nextTime = player.CurrentTime + (float)(now - previousUpdate) * (halfSpeed ? .5f : 1f);
        previousUpdate = now;
        bool ended = nextTime >= endTime;
        if (!Sample(ended ? endTime : nextTime) || ended) StopPreview();
    }

    private bool Sample(float time)
    {
        if (!player.Seek(time) || !arm.Evaluate())
        { previewError = "포즈 적용이 중단되었습니다. [Rib2SceneDemo] 또는 [Rib2SceneArm] 로그를 확인하세요."; return false; }
        if (controls != null) controls.RefreshNow();
        SceneView.RepaintAll();
        EditorApplication.QueuePlayerLoopUpdate();
        Repaint();
        return true;
    }

    private void StopPreview()
    {
        previewPlaying = false;
        EditorApplication.update -= TickPreview;
    }

    private void OnPlayModeChanged(PlayModeStateChange state)
    {
        StopPreview();
    }

    private bool IsDraftScene()
    {
        return player != null && player.gameObject.scene.IsValid() && player.gameObject.scene.isLoaded &&
            player.gameObject.scene.path.StartsWith(DemoScenePrefix, StringComparison.Ordinal);
    }

    private void ResolvePeers()
    {
        arm = null; controls = null;
        if (!IsDraftScene()) return;
        foreach (GameObject root in player.gameObject.scene.GetRootGameObjects())
        {
            foreach (Rib2DemoPatientArm candidate in root.GetComponentsInChildren<Rib2DemoPatientArm>(true))
            {
                var data = new SerializedObject(candidate);
                if (data.FindProperty("player").objectReferenceValue == player) arm = candidate;
            }
            foreach (Rib2DemoSceneControls candidate in root.GetComponentsInChildren<Rib2DemoSceneControls>(true))
            {
                var data = new SerializedObject(candidate);
                if (data.FindProperty("player").objectReferenceValue == player) controls = candidate;
            }
        }
    }

    private bool ReferencesStayInScene()
    {
        var playerData = new SerializedObject(player);
        foreach (string name in new[] { "motionFrame", "leftGuideHand", "rightGuideHand", "leftGripAnchor" })
        {
            var reference = playerData.FindProperty(name).objectReferenceValue as Component;
            if (reference == null || reference.gameObject.scene != player.gameObject.scene) return false;
        }
        var armData = new SerializedObject(arm);
        foreach (string name in new[] { "upperArm", "forearm", "patientHand", "wristTarget", "elbowHint", "motionFrame", "guideHandRoot" })
        {
            var reference = armData.FindProperty(name).objectReferenceValue as Component;
            if (reference == null || reference.gameObject.scene != player.gameObject.scene) return false;
        }
        return true;
    }
}
