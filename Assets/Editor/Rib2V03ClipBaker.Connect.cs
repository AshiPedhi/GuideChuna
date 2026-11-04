using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// V03 클립을 실습에 붙일 자료를 만든다(2026-11-02 사용자 «A로 진행» — 가이드손 녹화도 새 동작에 맞춰 만든다).
/// ① V03 클립에서 «펴짐 → 깊게 굽힘» 구간을 잘라 올리기 · 그 역순을 내리기 클립으로
/// ② 기존 녹화 `제2늑골 교정1`의 시술자 왼손이 환자 손을 잡은 자리(환자 손 기준)를 재서, 그 파지 그대로 새 팔을 따라가는 녹화를 만든다
/// ③ 컨트롤러 `제2늑골`에 두 상태를 더한다. 기존 클립 · 녹화 · 상태는 지우거나 덮지 않는다. CSV는 사람이 확인한 뒤 따로 바꾼다.
/// </summary>
public static partial class Rib2V03ClipBaker
{
    const string RaiseClipPath = "Assets/Generated/Rib2Patient/제2늑골 팔 교정 V03 올리기.anim";
    const string LowerClipPath = "Assets/Generated/Rib2Patient/제2늑골 팔 교정 V03 내리기.anim";
    const string OldRecordingPath = "Assets/Resources/HandPoseData/제2늑골 교정1.csv";
    const string NewRecordingPath = "Assets/Resources/HandPoseData/제2늑골 교정 V03.csv";
    const string NewReverseRecordingPath = "Assets/Resources/HandPoseData/제2늑골 교정 V03 역재생.csv";
    const string ControllerPath = "Assets/Resources/Animators/제2늑골.controller";
    const int WristJoint = 1;   // HandJointId.HandWristRoot — 녹화에서 World 칸이 있는 유일한 관절

    sealed class RecRow { public int frame, joint; public string hand; public string[] cells; }

    [MenuItem("GuideChuna/시험/제2늑골/V03 앱 연결 자료 만들기 (클립 · 녹화 · 컨트롤러 상태)")]
    public static void BuildConnection()
    {
        if (Application.isPlaying) { Debug.LogWarning("[V03연결] Play 중에는 만들지 않는다."); return; }
        var sceneC9 = GameObject.Find(C9Path);
        var full = AssetDatabase.LoadAssetAtPath<AnimationClip>(OutClipPath);
        var oldClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(BaseClipPath);
        var evaluator = UnityEngine.Object.FindFirstObjectByType<ChunaPathEvaluator>(FindObjectsInactive.Include);
        Transform sceneRef = evaluator != null ? new SerializedObject(evaluator).FindProperty("referenceTransform").objectReferenceValue as Transform : null;
        if (sceneC9 == null || full == null || oldClip == null || sceneRef == null || !File.Exists(OldRecordingPath))
        { Debug.LogWarning($"[V03연결] 준비물 — c9 {sceneC9 != null} · V03 클립 {full != null} · 기존 클립 {oldClip != null} · HandGuideAxis {sceneRef != null} · 기존 녹화 {File.Exists(OldRecordingPath)}"); return; }

        var scene = EditorSceneManager.NewPreviewScene();
        var made = new List<GameObject>();
        var log = new StringBuilder();
        try
        {
            // 씬과 같은 계층으로 복제(HandGuideAxis가 환자 뼈를 따라 움직이면 그것까지 같이 잰다)
            var holder = sceneC9.transform.parent; var parent = holder.parent;
            var rootGo = new GameObject("부모 대역") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(rootGo, scene); made.Add(rootGo);
            rootGo.transform.SetPositionAndRotation(parent.position, parent.rotation); rootGo.transform.localScale = parent.lossyScale;
            var holderCopy = UnityEngine.Object.Instantiate(holder.gameObject, rootGo.transform, false);
            foreach (var t in rootGo.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.HideAndDontSave;
            foreach (var b in rootGo.GetComponentsInChildren<Behaviour>(true)) if (!(b is Animator)) b.enabled = false;
            var c9 = holderCopy.transform.Find(sceneC9.name).gameObject;
            var animator = c9.GetComponent<Animator>();
            Transform reference = sceneRef.IsChildOf(holder) ? FindByPath(holderCopy.transform, RelativePath(holder, sceneRef)) : null;
            bool refMoves = reference != null;
            log.Append($"HandGuideAxis = {FullPath(sceneRef)} · {(refMoves ? "환자 홀더 안(복제본에서 매 프레임 다시 읽음)" : "홀더 밖(고정 · 씬 값 사용)")}\n");
            Vector3 RefPos() => refMoves ? reference.position : sceneRef.position;
            Quaternion RefRot() => refMoves ? reference.rotation : sceneRef.rotation;

            var arm = FindArm(animator, false);
            // 이름 Right = 앱에서 움직이는 팔(Bake에서 잰 값 · 해부학 왼팔). 다시 확인한다.
            oldClip.SampleAnimation(c9, oldClip.length);
            Vector3 endR = animator.GetBoneTransform(HumanBodyBones.RightHand).position, endL = animator.GetBoneTransform(HumanBodyBones.LeftHand).position;
            oldClip.SampleAnimation(c9, 0f);
            if (Vector3.Distance(endR, arm.hand.position) < Vector3.Distance(endL, animator.GetBoneTransform(HumanBodyBones.LeftHand).position))
            { Debug.LogWarning("[V03연결] ★기존 클립에서 움직이는 팔이 Right가 아니다 — 멈춘다"); return; }

            // ① 구간: 펴짐(팔꿈치 최소) → 그 뒤 깊은 굽힘(최대)
            int n = Mathf.RoundToInt(full.length * full.frameRate) + 1;
            var elbow = new float[n];
            for (int i = 0; i < n; i++) { full.SampleAnimation(c9, i / full.frameRate); elbow[i] = ElbowFlex(arm); }
            int from = 0; for (int i = 1; i < n * 3 / 4; i++) if (elbow[i] < elbow[from]) from = i;
            int to = from + 1; for (int i = from + 1; i < n; i++) if (elbow[i] > elbow[to]) to = i;
            float t0 = from / full.frameRate, t1 = to / full.frameRate;
            log.Append($"구간 = 프레임 {from}(팔꿈치 {elbow[from]:F1}°) → {to}({elbow[to]:F1}°) · {t1 - t0:F2}초\n");
            var raise = SliceClip(full, t0, t1, false, Path.GetFileNameWithoutExtension(RaiseClipPath));
            var lower = SliceClip(full, t0, t1, true, Path.GetFileNameWithoutExtension(LowerClipPath));
            raise = SaveClip(raise, RaiseClipPath); lower = SaveClip(lower, LowerClipPath);

            // ② 기존 녹화의 파지 = 환자 손 기준 시술자 왼손 손목. 녹화 진행도 p ↔ 기존 클립 p × 길이(SyncWithUser)
            var rows = ReadRecording(OldRecordingPath, out string header);
            int frames = rows.Max(r => r.frame) + 1;
            var relPos = new List<Vector3>(); var relRot = new List<Quaternion>();
            for (int f = 0; f < frames; f++)
            {
                oldClip.SampleAnimation(c9, f / (float)(frames - 1) * oldClip.length);
                var w = rows.First(r => r.frame == f && r.hand == "Left" && r.joint == WristJoint);
                Vector3 gp = RefPos() + RefRot() * V3(w.cells, 11); Quaternion gr = RefRot() * Q(w.cells, 14);
                Quaternion hf = HandFrame(arm);
                relPos.Add(Quaternion.Inverse(hf) * (gp - arm.hand.position)); relRot.Add(Quaternion.Inverse(hf) * gr);
            }
            float spreadPos = relPos.Max(p => Vector3.Distance(p, relPos[0])) * 1000f, spreadRot = relRot.Max(q => Quaternion.Angle(q, relRot[0]));
            log.Append($"기존 녹화 파지(환자 손 기준) — 손목 거리 첫 프레임 {relPos[0].magnitude * 1000f:F0}mm · 녹화 내내 흔들림 최대 {spreadPos:F0}mm · {spreadRot:F1}°\n");
            Vector3 gripPos = relPos[0]; Quaternion gripRot = relRot[0];

            // 새 녹화: 프레임 수 · 시간 폭은 기존과 같게(데모 속도 유지) · 손가락 관절은 기존 첫 프레임 그대로(실제 파지 모양)
            float span = rows.Where(r => r.frame == frames - 1).Select(r => float.Parse(r.cells[10], CultureInfo.InvariantCulture)).First();
            var forward = new StringBuilder(header + "\n"); var frameBlocks = new List<string>();
            float maxGap = 0f;
            for (int f = 0; f < frames; f++)
            {
                raise.SampleAnimation(c9, f / (float)(frames - 1) * raise.length);
                Quaternion hf = HandFrame(arm);
                Vector3 gp = arm.hand.position + hf * gripPos; Quaternion gr = hf * gripRot;
                Vector3 lp = Quaternion.Inverse(RefRot()) * (gp - RefPos()); Quaternion lr = Quaternion.Inverse(RefRot()) * gr;
                // 재생 공식(ChunaDataLoader: refPos + refRot × 값)으로 되돌려 환자 손과의 거리를 잰다
                maxGap = Mathf.Max(maxGap, Mathf.Abs(Vector3.Distance(RefPos() + RefRot() * lp, arm.hand.position) - gripPos.magnitude));
                frameBlocks.Add(FrameBlock(rows, f, lp, lr));
            }
            var times = Enumerable.Range(0, frames).Select(f => f / (float)(frames - 1) * span).ToArray();
            WriteRecording(NewRecordingPath, header, frameBlocks, times, false);
            WriteRecording(NewReverseRecordingPath, header, frameBlocks, times, true);
            log.Append($"새 녹화 {frames}프레임 · {span:F2}초 · 재생 공식으로 되돌린 손 거리 오차 최대 {maxGap * 1000f:F2}mm\n");

            // ③ 컨트롤러 상태
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            var sm = controller.layers[0].stateMachine;
            foreach (var clip in new[] { raise, lower })
            {
                var existing = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == clip.name);
                if (existing == null) { var st = sm.AddState(clip.name); st.motion = clip; st.writeDefaultValues = sm.states.First().state.writeDefaultValues; log.Append($"상태 추가 «{clip.name}»\n"); }
                else { existing.motion = clip; log.Append($"상태 있음 «{clip.name}» — 클립만 다시 연결\n"); }
            }
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(NewRecordingPath); AssetDatabase.ImportAsset(NewReverseRecordingPath);
            File.WriteAllText(ReportFolder + "/connect.txt", log.ToString(), new UTF8Encoding(false));
            Debug.Log("[V03연결] " + log);
        }
        finally
        {
            foreach (var g in made) if (g != null) UnityEngine.Object.DestroyImmediate(g);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    static float ElbowFlex(Arm arm) => Vector3.Angle(arm.lower.position - arm.upper.position, arm.hand.position - arm.lower.position);

    const string GuideLeftPath = "위치초기화 오브젝트/GameObject/OVRLeftHandVisual";
    const string GuideRightPath = "위치초기화 오브젝트/GameObject/OVRRightHandVisual";

    /// <summary>가이드손을 실제 손 모델로 붙여 기존(교정1 + 팔 교정)과 새 자료(V03 녹화 + 올리기)를 같은 진행도에서 찍는다. 재생 공식 = refPos + refRot × 값.</summary>
    [MenuItem("GuideChuna/시험/제2늑골/V03 가이드손 확인 렌더 (기존 · 새 자료)")]
    public static void RenderGuide()
    {
        var sceneC9 = GameObject.Find(C9Path); var table = GameObject.Find(TablePath);
        var gl = GameObject.Find(GuideLeftPath); var gr = GameObject.Find(GuideRightPath);
        var evaluator = UnityEngine.Object.FindFirstObjectByType<ChunaPathEvaluator>(FindObjectsInactive.Include);
        Transform sceneRef = new SerializedObject(evaluator).FindProperty("referenceTransform").objectReferenceValue as Transform;
        var oldClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(BaseClipPath); var raise = AssetDatabase.LoadAssetAtPath<AnimationClip>(RaiseClipPath);
        if (sceneC9 == null || table == null || gl == null || gr == null || sceneRef == null || raise == null) { Debug.LogWarning("[V03연결] 렌더 준비물이 없다."); return; }
        string folder = ReportFolder + "/guide"; Directory.CreateDirectory(folder);
        var scene = EditorSceneManager.NewPreviewScene(); var made = new List<GameObject>();
        var rt = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, hideFlags = HideFlags.HideAndDontSave };
        try
        {
            GameObject Add(GameObject g) { foreach (var t in g.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.HideAndDontSave; SceneManager.MoveGameObjectToScene(g, scene); made.Add(g); return g; }
            var holder = sceneC9.transform.parent; var parent = holder.parent;
            var rootGo = Add(new GameObject("부모 대역"));
            rootGo.transform.SetPositionAndRotation(parent.position, parent.rotation); rootGo.transform.localScale = parent.lossyScale;
            var holderCopy = UnityEngine.Object.Instantiate(holder.gameObject, rootGo.transform, false);
            UnityEngine.Object.Instantiate(table, rootGo.transform, false);
            var left = Add(UnityEngine.Object.Instantiate(gl)); var right = Add(UnityEngine.Object.Instantiate(gr));
            Add(rootGo);
            foreach (var g in new[] { rootGo, left, right }) foreach (var b in g.GetComponentsInChildren<Behaviour>(true)) if (!(b is Animator)) b.enabled = false;
            var c9 = holderCopy.transform.Find(sceneC9.name).gameObject; var animator = c9.GetComponent<Animator>();
            var reference = FindByPath(holderCopy.transform, RelativePath(holder, sceneRef));
            var lightGo = Add(new GameObject("빛")); var light = lightGo.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.2f; lightGo.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
            var camGo = Add(new GameObject("카메라")); var cam = camGo.AddComponent<Camera>();
            cam.scene = scene; cam.enabled = false; cam.targetTexture = rt; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.13f, .14f, .16f); cam.fieldOfView = 34f; cam.nearClipPlane = .02f;
            RenderSettings.ambientLight = new Color(.45f, .45f, .45f);
            var image = new Texture2D(960, 540, TextureFormat.RGB24, false);
            var leftVisual = left.GetComponent<Oculus.Interaction.HandVisual>(); var rightVisual = right.GetComponent<Oculus.Interaction.HandVisual>();
            foreach (var pass in new[] { ("old", oldClip, OldRecordingPath), ("new", raise, NewRecordingPath) })
            {
                var rows = ReadRecording(pass.Item3, out _); int frames = rows.Max(r => r.frame) + 1;
                foreach (int f in new[] { 0, frames / 2, frames - 1 })
                {
                    pass.Item2.SampleAnimation(c9, f / (float)(frames - 1) * pass.Item2.length);
                    var shotArm = FindArm(animator, false);
                    Debug.Log($"[V03연결] 렌더 {pass.Item1} {f} · 클립 {pass.Item2.name} {f / (float)(frames - 1) * pass.Item2.length:F2}/{pass.Item2.length:F2}초 · 팔꿈치 {ElbowFlex(shotArm):F1}° · 손 {shotArm.hand.position:F2}");
                    foreach (var (visual, hand) in new[] { (leftVisual, "Left"), (rightVisual, "Right") })
                        foreach (var r in rows.Where(r => r.frame == f && r.hand == hand))
                        {
                            if (r.joint >= visual.Joints.Count || visual.Joints[r.joint] == null) continue;
                            var j = visual.Joints[r.joint];
                            j.localPosition = V3(r.cells, 3); j.localRotation = Q(r.cells, 6);
                            if (r.joint == WristJoint) j.SetPositionAndRotation(reference.position + reference.rotation * V3(r.cells, 11), reference.rotation * Q(r.cells, 14));
                        }
                    Rib2AnatomyFrame.TryMeasure(animator, out var body, out _);
                    Vector3 hand0 = animator.GetBoneTransform(HumanBodyBones.RightHand).position;
                    Vector3 focus = Vector3.Lerp(animator.GetBoneTransform(HumanBodyBones.Chest).position, hand0, .55f);
                    camGo.transform.position = focus + (body.left * 1.1f + Vector3.up * 1.2f - body.head * .5f).normalized * 1.5f;
                    camGo.transform.LookAt(focus, Vector3.up);
                    // ★스킨은 한 장 늦게 갱신된다(11-02 실측: 팔꿈치 로그는 맞는데 화면은 앞 자세) → 강제 재계산 후 두 번 그린다
                    foreach (var g in new[] { rootGo, left, right }) foreach (var smr in g.GetComponentsInChildren<SkinnedMeshRenderer>(false)) smr.forceMatrixRecalculationPerRender = true;
                    cam.Render(); cam.Render();
                    RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); image.Apply(false); RenderTexture.active = null;
                    File.WriteAllBytes($"{folder}/{pass.Item1}_{f:D3}.png", image.EncodeToPNG());
                }
            }
            UnityEngine.Object.DestroyImmediate(image);
            Debug.Log("[V03연결] 가이드손 렌더 → " + folder);
        }
        finally
        {
            foreach (var g in made) if (g != null) UnityEngine.Object.DestroyImmediate(g);
            rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    /// <summary>환자 손의 해부학 틀(손가락 쪽 · 엄지 쪽). 좌우가 뒤집힌 모델에서도 같은 식이면 앞뒤 계산이 서로 맞는다.</summary>
    static Quaternion HandFrame(Arm arm)
    {
        Vector3 distal = (arm.middle.position - arm.hand.position).normalized;
        Vector3 radial = Vector3.ProjectOnPlane(arm.index.position - arm.little.position, distal).normalized;
        return Quaternion.LookRotation(distal, radial);
    }

    static AnimationClip SliceClip(AnimationClip src, float t0, float t1, bool reverse, string name)
    {
        var clip = new AnimationClip { name = name, frameRate = src.frameRate };
        float len = t1 - t0; int count = Mathf.RoundToInt(len * src.frameRate) + 1;
        foreach (var binding in AnimationUtility.GetCurveBindings(src))
        {
            var curve = AnimationUtility.GetEditorCurve(src, binding);
            var keys = new Keyframe[count];
            for (int i = 0; i < count; i++)
            {
                float t = i / src.frameRate;
                keys[i] = new Keyframe(t, curve.Evaluate(reverse ? t1 - t : t0 + t));
            }
            var c = new AnimationCurve(keys);
            for (int i = 0; i < count; i++) { AnimationUtility.SetKeyLeftTangentMode(c, i, AnimationUtility.TangentMode.ClampedAuto); AnimationUtility.SetKeyRightTangentMode(c, i, AnimationUtility.TangentMode.ClampedAuto); }
            AnimationUtility.SetEditorCurve(clip, binding, c);
        }
        var settings = AnimationUtility.GetAnimationClipSettings(src);
        settings.loopTime = false; settings.stopTime = len;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return clip;
    }

    static AnimationClip SaveClip(AnimationClip clip, string path)
    {
        var old = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (old == null) { AssetDatabase.CreateAsset(clip, path); return clip; }
        string name = old.name; EditorUtility.CopySerialized(clip, old); old.name = name; EditorUtility.SetDirty(old); return old;
    }

    static List<RecRow> ReadRecording(string path, out string header)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        header = lines[0].TrimStart('﻿');
        var rows = new List<RecRow>();
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var c = lines[i].Split(',');
            rows.Add(new RecRow { frame = int.Parse(c[0]), hand = c[1], joint = int.Parse(c[2]), cells = c });
        }
        return rows;
    }

    /// <summary>한 프레임 = 기존 첫 프레임의 관절 줄(손가락 모양) + 왼손 손목만 새 자리. 프레임 번호 · 시간은 쓸 때 채운다.</summary>
    static string FrameBlock(List<RecRow> rows, int frame, Vector3 leftPos, Quaternion leftRot)
    {
        var sb = new StringBuilder();
        foreach (var r in rows.Where(r => r.frame == 0))
        {
            var c = (string[])r.cells.Clone();
            if (r.hand == "Left" && r.joint == WristJoint)
            {
                string F(float v) => v.ToString("F4", CultureInfo.InvariantCulture);
                c[11] = F(leftPos.x); c[12] = F(leftPos.y); c[13] = F(leftPos.z);
                c[14] = F(leftRot.x); c[15] = F(leftRot.y); c[16] = F(leftRot.z); c[17] = F(leftRot.w);
            }
            c[0] = "{F}"; c[10] = "{T}";
            sb.Append(string.Join(",", c)).Append('\n');
        }
        return sb.ToString();
    }

    static void WriteRecording(string path, string header, List<string> blocks, float[] times, bool reverse)
    {
        var sb = new StringBuilder(header).Append('\n');
        for (int i = 0; i < blocks.Count; i++)
        {
            string block = blocks[reverse ? blocks.Count - 1 - i : i];
            sb.Append(block.Replace("{F}", i.ToString(CultureInfo.InvariantCulture)).Replace("{T}", times[i].ToString("F3", CultureInfo.InvariantCulture)));
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));   // 기존 녹화와 같이 BOM 있음
    }

    static Vector3 V3(string[] c, int i) => new Vector3(P(c[i]), P(c[i + 1]), P(c[i + 2]));
    static Quaternion Q(string[] c, int i) => new Quaternion(P(c[i]), P(c[i + 1]), P(c[i + 2]), P(c[i + 3])).normalized;
    static float P(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    static string RelativePath(Transform root, Transform t)
    {
        var parts = new List<string>();
        for (var x = t; x != null && x != root; x = x.parent) parts.Insert(0, x.name);
        return string.Join("/", parts);
    }
    static Transform FindByPath(Transform root, string path) => string.IsNullOrEmpty(path) ? root : root.Find(path);
    static string FullPath(Transform t) => t.parent == null ? t.name : FullPath(t.parent) + "/" + t.name;
}
