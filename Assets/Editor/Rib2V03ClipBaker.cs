using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// V03 환자 팔 동작 → c9 휴머노이드 클립(2026-11-02 사용자 «A로 진행»). 기존 클립 · 컨트롤러 · CSV는 건드리지 않고 새 .anim만 만든다.
/// 씬의 c9를 비공개 미리보기 씬에 복제해 근육 값을 프레임마다 풀고, 다 구운 클립을 다시 재생해 실제 본 방향으로 오차를 잰다.
/// 팔 근육 7개만 바꾸고 나머지(고개 · 몸통 · 손가락)는 `제2늑골 팔 교정` 첫 프레임을 유지한다.
/// </summary>
public static partial class Rib2V03ClipBaker
{
    const string C9Path = "위치초기화 오브젝트/GameObject/ChunaObject/환자 이동 홀더/c9";
    const string BaseClipPath = "Assets/MedicXR CPX PhysicalExamination's PatientModel v20221111/Animations/1/Animations/1/제2늑골 팔 교정.anim";
    const string OutClipPath = "Assets/Generated/Rib2Patient/제2늑골 팔 교정 V03.anim";
    const string ReportFolder = "Captures/Rib2ClipV03_20261102";
    static readonly string[] ArmMuscles = { "Arm Down-Up", "Arm Front-Back", "Arm Twist In-Out", "Forearm Stretch", "Forearm Twist In-Out", "Hand Down-Up", "Hand In-Out" };
    const float MuscleLimit = 1.6f;   // ★±1은 천장이 아니다(CLAUDE.md 규칙 5 · 08-24 실측) — 영상의 큰 거상이 ±1에서 위팔 47° 모자랐다(11-02)
    const float TwistLimit = 1f;      // ★전완 비틀림은 ±1 안 — 1.6까지 풀자 손바닥을 맞추느라 −1.60까지 찼고 화면에서 팔이 꼬였다(11-02 사용자 지적)
    static float LimitOf(int muscle) => muscle == 4 ? TwistLimit : MuscleLimit;   // 위팔 비틀림은 깊게 굽힐 때 팔꿈치 평면을 돌려야 해서 묶지 않는다(묶으면 아래팔 44° 틀어짐 · 11-02 실측)

    internal sealed class Arm
    {
        public Transform upper, lower, hand, middle, index, little;
        public int[] muscles;
        public string side;
    }

    [MenuItem("GuideChuna/시험/제2늑골/V03 환자 팔 → c9 클립 굽기 (새 파일만)")]
    public static void Bake()
    {
        if (Application.isPlaying) { Debug.LogWarning("[V03클립] Play 중에는 굽지 않는다."); return; }
        var sceneC9 = GameObject.Find(C9Path);
        var baseClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(BaseClipPath);
        if (sceneC9 == null || baseClip == null) { Debug.LogWarning($"[V03클립] c9 {(sceneC9 != null ? "있음" : "★없음")} · 기준 클립 {(baseClip != null ? "있음" : "★없음")}"); return; }
        if (!Rib2AnatomicalHandsReviewSession.ExportClipTargets(out var targets, out bool v03Left, out float fps)) { Debug.LogWarning("[V03클립] V03 자세를 못 읽었다."); return; }

        var scene = EditorSceneManager.NewPreviewScene();
        GameObject c9 = null; HumanPoseHandler handler = null;
        try
        {
            c9 = UnityEngine.Object.Instantiate(sceneC9);
            c9.hideFlags = HideFlags.HideAndDontSave;
            SceneManager.MoveGameObjectToScene(c9, scene);
            foreach (var b in c9.GetComponentsInChildren<Behaviour>(true)) if (!(b is Animator)) b.enabled = false;
            var animator = c9.GetComponent<Animator>();
            handler = new HumanPoseHandler(animator.avatar, c9.transform);

            baseClip.SampleAnimation(c9, baseClip.length);
            Vector3 endL = animator.GetBoneTransform(HumanBodyBones.LeftHand).position, endR = animator.GetBoneTransform(HumanBodyBones.RightHand).position;
            baseClip.SampleAnimation(c9, 0f);
            float moveL = Vector3.Distance(endL, animator.GetBoneTransform(HumanBodyBones.LeftHand).position);
            float moveR = Vector3.Distance(endR, animator.GetBoneTransform(HumanBodyBones.RightHand).position);
            bool appLeftName = moveL > moveR;
            if (!Rib2AnatomyFrame.TryMeasure(animator, out var body, out bool nameIsAnatomical)) { Debug.LogWarning("[V03클립] c9 기준 본이 없다."); return; }
            bool appAnatomicalLeft = appLeftName == nameIsAnatomical;
            bool mirror = appAnatomicalLeft != v03Left;
            var arm = FindArm(animator, appLeftName);
            var pose = new HumanPose(); handler.GetHumanPose(ref pose);
            float[] restMuscles = (float[])pose.muscles.Clone();
            // ★SetHumanPose는 뿌리 회전이 있는 복제본의 몸을 다른 자리로 옮긴다(실측: 골반 2.2m · 177°) → 기준틀은 반드시 그 뒤에 잰다
            handler.SetHumanPose(ref pose);
            Rib2AnatomyFrame.TryMeasure(animator, out body, out _);

            int n = targets.Count;
            var solved = new float[n][];
            float[] x = new float[ArmMuscles.Length];
            for (int m = 0; m < x.Length; m++) x[m] = restMuscles[arm.muscles[m]];
            for (int i = 0; i < n; i++)
            {
                var t = WorldTarget(targets[i], body, mirror);
                if (i == 0) { GridStart(handler, ref pose, arm, t, x); for (int m = 0; m < x.Length; m++) x[m] = Mathf.Clamp(x[m], -LimitOf(m), LimitOf(m)); }
                Refine(handler, ref pose, arm, t, x, i == 0 ? new[] { .1f, .05f, .02f, .01f, .005f } : new[] { .04f, .02f, .01f, .005f });
                solved[i] = (float[])x.Clone();
                if (i % 40 == 0) EditorUtility.DisplayProgressBar("V03 클립 굽기", $"{i}/{n}", i / (float)n);
            }
            EditorUtility.ClearProgressBar();

            var clip = BuildClip(baseClip, arm, solved, fps);
            Directory.CreateDirectory(Path.GetDirectoryName(OutClipPath));
            var old = AssetDatabase.LoadAssetAtPath<AnimationClip>(OutClipPath);
            if (old != null) { EditorUtility.CopySerialized(clip, old); clip = old; EditorUtility.SetDirty(old); }
            else AssetDatabase.CreateAsset(clip, OutClipPath);
            AssetDatabase.SaveAssets();

            WriteReport(clip, c9, animator, arm, targets, body, mirror, fps, solved, moveL, moveR, appLeftName, nameIsAnatomical, v03Left);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            handler?.Dispose();
            if (c9 != null) UnityEngine.Object.DestroyImmediate(c9);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    const string BodycamBundlePath = "Captures/Rib2AnatomicalHands_V03_20261027/bodycam_camera_bundle_v6.json";
    [Serializable] class BodycamKey { public float[] position = null, rotation = null; }
    [Serializable] class BodycamBundle { public float verticalFovDegrees = 40f; public BodycamKey[] poseKeys = null; }
    const string TablePath = "위치초기화 오브젝트/GameObject/ChunaObject/추나 테이블";

    /// <summary>구운 클립을 씬 복제본(환자 + 침대)에 재생해 프레임별 PNG로 남긴다. 저장된 클립을 그대로 쓴다.</summary>
    [MenuItem("GuideChuna/시험/제2늑골/V03 클립 렌더 (침대와 함께)")]
    public static void Render()
    {
        var sceneC9 = GameObject.Find(C9Path); var table = GameObject.Find(TablePath);
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(OutClipPath);
        if (sceneC9 == null || table == null || clip == null) { Debug.LogWarning("[V03클립] 렌더 준비물이 없다."); return; }
        string folder = ReportFolder + "/frames"; Directory.CreateDirectory(folder);
        var scene = EditorSceneManager.NewPreviewScene();
        var made = new List<GameObject>();
        var rt = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, hideFlags = HideFlags.HideAndDontSave };
        try
        {
            GameObject Add(GameObject g) { g.hideFlags = HideFlags.HideAndDontSave; SceneManager.MoveGameObjectToScene(g, scene); made.Add(g); return g; }
            // 환자 홀더와 침대는 같은 부모 아래에 있다 — 부모 좌표까지 그대로 옮겨야 침대와의 관계가 실습과 같다
            var holder = sceneC9.transform.parent; var parent = holder.parent;
            var rootGo = Add(new GameObject("부모 대역"));
            rootGo.transform.SetPositionAndRotation(parent.position, parent.rotation); rootGo.transform.localScale = parent.lossyScale;
            var holderCopy = UnityEngine.Object.Instantiate(holder.gameObject, rootGo.transform, false);
            UnityEngine.Object.Instantiate(table, rootGo.transform, false);
            foreach (var t in rootGo.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.HideAndDontSave;
            var c9 = holderCopy.transform.Find(sceneC9.name).gameObject;
            foreach (var b in rootGo.GetComponentsInChildren<Behaviour>(true)) if (!(b is Animator)) b.enabled = false;
            var lightGo = Add(new GameObject("빛")); var light = lightGo.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.2f; lightGo.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
            var camGo = Add(new GameObject("카메라")); var cam = camGo.AddComponent<Camera>();
            cam.scene = scene; cam.enabled = false; cam.targetTexture = rt; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.13f, .14f, .16f); cam.fieldOfView = 40f; cam.nearClipPlane = .02f; cam.farClipPlane = 30f;
            var animator = c9.GetComponent<Animator>();
            clip.SampleAnimation(c9, 0f);
            Rib2AnatomyFrame.TryMeasure(animator, out var body, out _);
            Vector3 focus = animator.GetBoneTransform(HumanBodyBones.Chest).position;
            camGo.transform.position = focus + (body.left * 1.25f + Vector3.up * 1.05f - body.head * .85f).normalized * 2.1f;
            camGo.transform.LookAt(focus + body.left * .25f - Vector3.up * .1f, Vector3.up);
            Vector3 thirdPosition = camGo.transform.position; Quaternion thirdRotation = camGo.transform.rotation; float thirdFov = cam.fieldOfView;
            var bodycam = JsonUtility.FromJson<BodycamBundle>(File.ReadAllText(BodycamBundlePath));
            Rib2AnatomicalHandsReviewSession.MeasureRigFrame(out var rigBody, out Vector3 rigChest, out float rigWidth);
            float widthScale = Vector3.Distance(animator.GetBoneTransform(HumanBodyBones.LeftUpperArm).position, animator.GetBoneTransform(HumanBodyBones.RightUpperArm).position) / rigWidth;
            Vector3 chest = (animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest)).position;
            Vector3 MapDir(Vector3 v) => body.ToWorld(rigBody.ToBody(v));
            var image = new Texture2D(960, 540, TextureFormat.RGB24, false);
            int n = Mathf.RoundToInt(clip.length * clip.frameRate) + 1;
            RenderSettings.ambientLight = new Color(.45f, .45f, .45f);
            for (int i = 0; i < n; i++)
            {
                clip.SampleAnimation(c9, i / clip.frameRate);
                foreach (var smr in c9.GetComponentsInChildren<SkinnedMeshRenderer>(false)) smr.forceMatrixRecalculationPerRender = true;
                void Shot(string prefix)
                {
                    cam.Render();
                    RenderTexture.active = rt; image.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); image.Apply(false); RenderTexture.active = null;
                    File.WriteAllBytes($"{folder}/{prefix}_{i:D3}.png", image.EncodeToPNG());
                }
                camGo.transform.SetPositionAndRotation(thirdPosition, thirdRotation); cam.fieldOfView = thirdFov;
                Shot("f");
                var key = bodycam.poseKeys[Mathf.Min(i, bodycam.poseKeys.Length - 1)];
                var q = new Quaternion(key.rotation[0], key.rotation[1], key.rotation[2], key.rotation[3]).normalized;
                var rigPosition = new Vector3(key.position[0], key.position[1], key.position[2]);
                camGo.transform.SetPositionAndRotation(chest + MapDir(rigPosition - rigChest) * widthScale, Quaternion.LookRotation(MapDir(q * Vector3.forward), MapDir(q * Vector3.up)));
                cam.fieldOfView = bodycam.verticalFovDegrees;
                Shot("p");
                if (i % 30 == 0) EditorUtility.DisplayProgressBar("V03 클립 렌더", $"{i}/{n}", i / (float)n);
            }
            UnityEngine.Object.DestroyImmediate(image);
            Debug.Log($"[V03클립] 렌더 {n}장 → {folder}");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            foreach (var g in made) if (g != null) UnityEngine.Object.DestroyImmediate(g);
            rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    struct Target { public Vector3 upper, lower, distal, radial; }

    static Target WorldTarget(Rib2AnatomicalHandsReviewSession.ClipTargetFrame f, Rib2AnatomyFrame body, bool mirror)
    {
        Vector3 M(Vector3 v) => body.ToWorld(mirror ? new Vector3(v.x, v.y, -v.z) : v);
        return new Target { upper = M(f.upper), lower = M(f.lower), distal = M(f.distal), radial = M(f.radial) };
    }

    internal static Arm FindArm(Animator a, bool left)
    {
        string side = left ? "Left" : "Right";
        var arm = new Arm {
            side = side,
            upper = a.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm),
            lower = a.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm),
            hand = a.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand),
            middle = a.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal),
            index = a.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal),
            little = a.GetBoneTransform(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal),
            muscles = new int[ArmMuscles.Length]
        };
        for (int m = 0; m < ArmMuscles.Length; m++)
        {
            arm.muscles[m] = Array.IndexOf(HumanTrait.MuscleName, side + " " + ArmMuscles[m]);
            if (arm.muscles[m] < 0) throw new InvalidOperationException("근육 이름 없음: " + side + " " + ArmMuscles[m]);
        }
        return arm;
    }

    static float Cost(HumanPoseHandler handler, ref HumanPose pose, Arm arm, Target t, float[] x)
    {
        for (int m = 0; m < x.Length; m++) pose.muscles[arm.muscles[m]] = x[m];
        handler.SetHumanPose(ref pose);
        return Errors(arm, t, out _, out _, out _, out _);
    }

    /// <summary>각도(도)의 가중 합 — 위팔 · 아래팔 방향을 손 방향보다 크게 앞세운다.</summary>
    static float Errors(Arm arm, Target t, out float upper, out float lower, out float distal, out float radial)
    {
        upper = Vector3.Angle(arm.lower.position - arm.upper.position, t.upper);
        lower = Vector3.Angle(arm.hand.position - arm.lower.position, t.lower);
        distal = Vector3.Angle(arm.middle.position - arm.hand.position, t.distal);
        radial = Vector3.Angle(Vector3.ProjectOnPlane(arm.index.position - arm.little.position, t.distal), t.radial);
        // ★비틀림을 ±1로 묶으면 손 옆방향은 다 못 맞춘다 — 그 값이 크면 위팔이 47° 대신 틀어졌다(11-02 실측) → 팔 방향을 지키고 손바닥이 양보한다
        return upper + lower + .15f * distal + .05f * radial;
    }

    static void GridStart(HumanPoseHandler handler, ref HumanPose pose, Arm arm, Target t, float[] x)
    {
        float best = float.PositiveInfinity; var bestX = (float[])x.Clone(); var trial = (float[])x.Clone();
        for (float a = -1f; a <= 1.01f; a += .4f)
            for (float b = -1f; b <= 1.01f; b += .4f)
                for (float c = -1f; c <= 1.01f; c += .4f)
                    for (float d = -1f; d <= 1.01f; d += .4f)
                    {
                        trial[0] = a; trial[1] = b; trial[2] = c; trial[3] = d;
                        float e = Cost(handler, ref pose, arm, t, trial);
                        if (e < best) { best = e; Array.Copy(trial, bestX, x.Length); }
                    }
        Array.Copy(bestX, x, x.Length);
    }

    static void Refine(HumanPoseHandler handler, ref HumanPose pose, Arm arm, Target t, float[] x, float[] steps)
    {
        float best = Cost(handler, ref pose, arm, t, x);
        foreach (float step in steps)
        {
            bool improved = true; int guard = 0;
            while (improved && guard++ < 12)
            {
                improved = false;
                for (int m = 0; m < x.Length; m++)
                    foreach (float sign in new[] { 1f, -1f })
                    {
                        float keep = x[m];
                        x[m] = Mathf.Clamp(keep + sign * step, -LimitOf(m), LimitOf(m));
                        float e = Cost(handler, ref pose, arm, t, x);
                        if (e < best - 1e-3f) { best = e; improved = true; break; }
                        x[m] = keep;
                    }
            }
        }
    }

    static AnimationClip BuildClip(AnimationClip baseClip, Arm arm, float[][] solved, float fps)
    {
        var clip = new AnimationClip { name = Path.GetFileNameWithoutExtension(OutClipPath), frameRate = fps };
        float length = (solved.Length - 1) / fps;
        var replaced = new HashSet<string>();
        for (int m = 0; m < ArmMuscles.Length; m++)
        {
            string name = arm.side + " " + ArmMuscles[m];
            replaced.Add(name);
            var keys = new Keyframe[solved.Length];
            for (int i = 0; i < solved.Length; i++) keys[i] = new Keyframe(i / fps, solved[i][m]);
            var curve = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++) { AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto); AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto); }
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), name), curve);
        }
        foreach (var binding in AnimationUtility.GetCurveBindings(baseClip))
        {
            if (replaced.Contains(binding.propertyName)) continue;
            float v = AnimationUtility.GetEditorCurve(baseClip, binding).Evaluate(0f);
            AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Linear(0f, v, length, v));
        }
        var settings = AnimationUtility.GetAnimationClipSettings(baseClip);
        settings.loopTime = false; settings.stopTime = length;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return clip;
    }

    /// <summary>★풀이값이 아니라 저장된 클립을 다시 재생해 잰다.</summary>
    static void WriteReport(AnimationClip clip, GameObject c9, Animator animator, Arm arm, List<Rib2AnatomicalHandsReviewSession.ClipTargetFrame> targets,
        Rib2AnatomyFrame body, bool mirror, float fps, float[][] solved, float moveL, float moveR, bool appLeftName, bool nameIsAnatomical, bool v03Left)
    {
        var csv = new StringBuilder("frame,seconds,upperDeg,lowerDeg,distalDeg,radialDeg,elbowFlexDeg\n");
        float maxU = 0, maxL = 0, maxD = 0, maxR = 0, sumU = 0, sumL = 0, sumD = 0, sumR = 0, minElbow = 180, maxElbow = 0, jump = 0;
        Vector3 prevLower = Vector3.zero;
        for (int i = 0; i < targets.Count; i++)
        {
            clip.SampleAnimation(c9, i / fps);
            Rib2AnatomyFrame.TryMeasure(animator, out body, out _);
            var t = WorldTarget(targets[i], body, mirror);
            Errors(arm, t, out float u, out float l, out float d, out float r);
            float elbow = Vector3.Angle(arm.lower.position - arm.upper.position, arm.hand.position - arm.lower.position);
            Vector3 lowerDir = (arm.hand.position - arm.lower.position).normalized;
            if (i > 0) jump = Mathf.Max(jump, Vector3.Angle(prevLower, lowerDir));
            prevLower = lowerDir;
            maxU = Mathf.Max(maxU, u); maxL = Mathf.Max(maxL, l); maxD = Mathf.Max(maxD, d); maxR = Mathf.Max(maxR, r);
            sumU += u; sumL += l; sumD += d; sumR += r;
            minElbow = Mathf.Min(minElbow, elbow); maxElbow = Mathf.Max(maxElbow, elbow);
            csv.Append($"{i},{i / fps:F3},{u:F2},{l:F2},{d:F2},{r:F2},{elbow:F1}\n");
        }
        int clamped = 0;
        foreach (var f in solved) for (int m = 0; m < f.Length; m++) if (Mathf.Abs(f[m]) >= LimitOf(m) - 1e-3f) clamped++;
        int c = targets.Count;
        string text = $"팔 = {arm.side}(이름) · 기존 클립 손 이동 왼 {moveL:F3}m 오 {moveR:F3}m · 이름=해부학 {nameIsAnatomical} · 앱 해부학 왼팔 {appLeftName == nameIsAnatomical} · V03 해부학 왼팔 {v03Left} · 거울 {mirror}\n" +
                      $"프레임 {c} · 길이 {clip.length:F2}초\n" +
                      $"위팔 방향 오차 평균 {sumU / c:F1}° 최대 {maxU:F1}° / 아래팔 {sumL / c:F1}° {maxL:F1}° / 손 길이방향 {sumD / c:F1}° {maxD:F1}° / 손 옆방향 {sumR / c:F1}° {maxR:F1}°\n" +
                      $"팔꿈치 굽힘 {minElbow:F1}~{maxElbow:F1}° · 프레임 간 아래팔 최대 변화 {jump:F1}° · 근육 한계 닿은 값 {clamped}/{c * ArmMuscles.Length}";
        Directory.CreateDirectory(ReportFolder);
        File.WriteAllText(ReportFolder + "/frames.csv", csv.ToString(), new UTF8Encoding(false));
        File.WriteAllText(ReportFolder + "/report.txt", text, new UTF8Encoding(false));
        Debug.Log("[V03클립] " + text);
    }
}
