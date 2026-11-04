using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 제2늑골 교정 «당겨 펴기» 클립(2026-11-03 사용자 지시 — 전문가 영상과 맞춰서).
/// 동작(바디캠 35~67초 · 3인칭 · 사용자 정정): 3-4 «팔 외전» 끝 자세(위팔 옆 · 팔꿈치 ≈90° · 아래팔과 손이 머리 쪽)에서
///   <b>굽은 채로 머리 쪽으로 당기며 편다</b> → 다시 굽혀 돌아온다 × 3. 편 팔을 끌어오는 것이 아니다.
/// 만드는 법: 시작 = 외전 클립 끝 자세 그대로(이어짐 보장) · 끝 = 팔이 이루는 평면 안에서 위팔을 머리 쪽으로 PullDeg 돌리고
///   팔꿈치를 EndFlexDeg만 남기고 편 방향을 근육 풀이(<see cref="Refine"/>)로 맞춘다 · 그 사이는 근육값 smoothstep 보간.
/// ★각도 두 개는 영상 눈 관찰의 대략값이다(실측 아님) — 검수 뒤 바꾼다. 결과 뼈 위치는 JSON으로 남겨 졸라맨 그림으로 검수한다.
/// 기존 클립 · CSV · 컨트롤러는 건드리지 않는다(새 파일 두 개만).
/// </summary>
public static partial class Rib2V03ClipBaker
{
    const string AbductStateName = "제2늑골 팔 외전";
    const string PullClipPath = "Assets/Generated/Rib2Patient/제2늑골 당겨 펴기 V04.anim";
    const string ReturnClipPath = "Assets/Generated/Rib2Patient/제2늑골 굽혀 돌아오기 V04.anim";
    const string PullReportPath = "Captures/Rib2PullExtendV04_20261103/pose.json";
    const float PullDeg = 55f;       // 펴진 끝 위팔 방향 = 몸 옆에서 머리 쪽으로 이만큼(몸 기준 절대각) ★바디캠 눈 관찰 대략값
    const float EndFlexDeg = 10f;    // 펴진 끝에 남는 팔꿈치 굽힘 ★대략값
    const float PullSeconds = 2.0f;  // 바디캠 56→58초 ≈ 2초
    const float ArcBumpDeg = 4f;     // 가운데서 위팔이 살짝 솟는 각(포물선) ★대략값 · 11-03 사용자 «위아래로 포물선이 살짝»
    const float ElbowLag = 0.2f;     // 팔꿈치가 어깨보다 늦게 펴지기 시작하는 몫(곡선 궤적)

    [MenuItem("GuideChuna/시험/제2늑골/V04 당겨 펴기 클립 만들기 (외전 끝 자세 → 머리 쪽으로 펴기)")]
    public static void BuildPullExtend()
    {
        if (Application.isPlaying) { Debug.LogWarning("[V04당겨펴기] Play 중에는 만들지 않는다."); return; }
        var sceneC9 = GameObject.Find(C9Path);
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        AnimationClip abduct = null;
        if (controller != null)
            foreach (var s in controller.layers[0].stateMachine.states)
                if (s.state.name == AbductStateName) abduct = s.state.motion as AnimationClip;
        if (sceneC9 == null || abduct == null) { Debug.LogWarning($"[V04당겨펴기] 준비물 — c9 {sceneC9 != null} · 외전 클립 {abduct != null}"); return; }

        var scene = EditorSceneManager.NewPreviewScene();
        var made = new List<GameObject>();
        HumanPoseHandler handler = null;
        var log = new StringBuilder();
        try
        {
            var holder = sceneC9.transform.parent; var parent = holder.parent;
            var rootGo = new GameObject("부모 대역") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(rootGo, scene); made.Add(rootGo);
            rootGo.transform.SetPositionAndRotation(parent.position, parent.rotation); rootGo.transform.localScale = parent.lossyScale;
            var holderCopy = Object.Instantiate(holder.gameObject, rootGo.transform, false);
            foreach (var t in rootGo.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.HideAndDontSave;
            foreach (var b in rootGo.GetComponentsInChildren<Behaviour>(true)) if (!(b is Animator)) b.enabled = false;
            var c9 = holderCopy.transform.Find(sceneC9.name).gameObject;
            var animator = c9.GetComponent<Animator>();
            handler = new HumanPoseHandler(animator.avatar, c9.transform);

            // 움직이는 팔 = 외전 클립에서 손이 더 많이 움직인 쪽(이름 좌우는 못 믿는다 — c9는 뒤집혀 있다)
            abduct.SampleAnimation(c9, 0f);
            Vector3 l0 = animator.GetBoneTransform(HumanBodyBones.LeftHand).position, r0 = animator.GetBoneTransform(HumanBodyBones.RightHand).position;
            abduct.SampleAnimation(c9, abduct.length);
            bool leftName = Vector3.Distance(l0, animator.GetBoneTransform(HumanBodyBones.LeftHand).position)
                          > Vector3.Distance(r0, animator.GetBoneTransform(HumanBodyBones.RightHand).position);
            var arm = FindArm(animator, leftName);
            // ★4차: «위쪽»은 환자 몸 기준(가슴 앞)으로 잰다. SetHumanPose가 몸을 다른 자리 · 각도로 옮기는데(11-02 실측 2.2m · 177°)
            //   월드 위쪽으로 기울기를 정했더니 실제 재생에서 손이 가운데서 5cm 내려갔다(3차 +7.0 → +1.6cm).
            //   부호만 재생 상태(SampleAnimation · 누운 환자)에서 정한다: 가슴 앞 = 월드 위쪽과 같은 편.
            float anteriorSign;
            {
                Vector3 zs = (animator.GetBoneTransform(HumanBodyBones.Head).position - animator.GetBoneTransform(HumanBodyBones.Hips).position).normalized;
                Vector3 xs = Vector3.ProjectOnPlane(arm.upper.position - animator.GetBoneTransform(HumanBodyBones.Chest).position, zs).normalized;
                anteriorSign = Vector3.Dot(Vector3.Cross(zs, xs), Vector3.up) >= 0f ? 1f : -1f;
            }

            var pose = new HumanPose(); handler.GetHumanPose(ref pose);
            handler.SetHumanPose(ref pose);   // ★SetHumanPose가 몸을 옮길 수 있다 — 방향은 이 뒤에 잰다(11-02 실측)
            float[] p0 = new float[ArmMuscles.Length];
            for (int m = 0; m < p0.Length; m++) p0[m] = pose.muscles[arm.muscles[m]];

            Vector3 u0 = arm.lower.position - arm.upper.position, f0 = arm.hand.position - arm.lower.position;
            Vector3 d0 = arm.middle.position - arm.hand.position;
            Vector3 r0v = Vector3.ProjectOnPlane(arm.index.position - arm.little.position, d0);
            float flex0 = Vector3.Angle(u0, f0);
            Vector3 headDir = (animator.GetBoneTransform(HumanBodyBones.Head).position - animator.GetBoneTransform(HumanBodyBones.Hips).position).normalized;
            log.Append($"움직이는 팔 = 이름 {arm.side} · 시작 팔꿈치 굽힘 {flex0:F1}° · 아래팔과 머리 방향 사이 {Vector3.Angle(f0, headDir):F1}°\n");

            // ★끝 방향은 몸 기준 절대 방향 — «시작에서 PullDeg 더 돌리기»로 했더니 시작 위팔이 이미 발 쪽 ≈40°라
            //   끝이 옆 +20°에 그쳤다(11-03 2차 실측 그림). 몸틀: 머리 쪽 = 골반→머리(수평) · 바깥 = 가슴→움직이는 어깨(수평, 머리 쪽 성분 뺌)
            Vector3 zHead = headDir;
            Vector3 xOut = Vector3.ProjectOnPlane(arm.upper.position - animator.GetBoneTransform(HumanBodyBones.Chest).position, zHead).normalized;
            Vector3 yB = (anteriorSign * Vector3.Cross(zHead, xOut)).normalized;   // 환자 가슴 앞 = 누우면 천장 쪽
            float elev = Mathf.Asin(Mathf.Clamp(Vector3.Dot(u0.normalized, yB), -1f, 1f));   // 시작 위팔의 위아래 기울기는 그대로 둔다
            Vector3 horiz = Mathf.Cos(PullDeg * Mathf.Deg2Rad) * xOut + Mathf.Sin(PullDeg * Mathf.Deg2Rad) * zHead;
            Vector3 u1 = (Mathf.Cos(elev) * horiz + Mathf.Sin(elev) * yB).normalized * u0.magnitude;
            Vector3 bendA = Quaternion.AngleAxis(EndFlexDeg, yB) * u1, bendB = Quaternion.AngleAxis(-EndFlexDeg, yB) * u1;
            Vector3 f1 = Vector3.Dot(bendA, zHead) > Vector3.Dot(bendB, zHead) ? bendA : bendB;   // 남은 굽힘은 머리 쪽으로
            log.Append($"시작 위팔 — 옆에서 머리 쪽으로 {Mathf.Atan2(Vector3.Dot(u0, zHead), Vector3.Dot(u0, xOut)) * Mathf.Rad2Deg:F1}° · 기울기 {elev * Mathf.Rad2Deg:F1}° → 끝 {PullDeg}°\n");
            // 아래팔이 f0 → f1로 돈 만큼 손도 같이 돈다(손목 자세는 시작 그대로)
            Quaternion foreTurn = Quaternion.FromToRotation(f0, f1);
            var target = new Target { upper = u1.normalized, lower = f1.normalized, distal = (foreTurn * d0).normalized, radial = (foreTurn * r0v).normalized };

            // ★시작값에서만 풀면 위팔 오차 34.8°에서 갇혔다(11-03 1차 실행) → 격자 출발점도 풀어 작은 쪽을 쓴다
            float[] x = (float[])p0.Clone();
            Refine(handler, ref pose, arm, target, x, new[] { .1f, .05f, .02f, .01f, .005f });
            float costA = Cost(handler, ref pose, arm, target, x);
            float[] xg = (float[])p0.Clone();
            GridStart(handler, ref pose, arm, target, xg);
            for (int m = 0; m < xg.Length; m++) xg[m] = Mathf.Clamp(xg[m], -LimitOf(m), LimitOf(m));
            Refine(handler, ref pose, arm, target, xg, new[] { .1f, .05f, .02f, .01f, .005f });
            float costB = Cost(handler, ref pose, arm, target, xg);
            log.Append($"풀이 비용 — 시작값 출발 {costA:F1} · 격자 출발 {costB:F1}\n");
            if (costB < costA) x = xg;
            Cost(handler, ref pose, arm, target, x);
            Errors(arm, target, out float eu, out float el, out float ed, out float er);
            float flex1 = Vector3.Angle(arm.lower.position - arm.upper.position, arm.hand.position - arm.lower.position);
            log.Append($"끝 자세 풀이 — 위팔 오차 {eu:F1}° · 아래팔 {el:F1}° · 손 {ed:F1}° · 손 옆 {er:F1}° · 끝 팔꿈치 굽힘 {flex1:F1}°(목표 {EndFlexDeg})\n");
            log.Append($"근육 시작 → 끝: {string.Join(" · ", Enumerable.Range(0, x.Length).Select(i => $"{ArmMuscles[i]} {p0[i]:F2}→{x[i]:F2}"))}\n");

            // ★11-03 2차(사용자 «궤적이 너무 직선적 · 너무 올라온다 · 영상은 위아래로 포물선이 살짝»):
            //   근육값 통째 보간은 손이 가운데서 +10cm 솟고(실측 +7 → +16.9 → +5.8cm) 어깨 · 팔꿈치가 같이 움직여 밋밋했다.
            //   → 프레임마다 모양을 정해 풀고(직전 프레임 값에서 출발) · 어깨가 먼저 돌고 팔꿈치는 늦게 편다 ·
            //     위팔 기울기는 시작 그대로 + 가운데만 ArcBumpDeg 살짝 솟는 포물선.
            float fps = 30f; int n = Mathf.RoundToInt(PullSeconds * fps) + 1;
            var solved = new float[n][];
            float aStart = Mathf.Atan2(Vector3.Dot(u0, zHead), Vector3.Dot(u0, xOut)) * Mathf.Rad2Deg;
            // ★3차: 아래팔은 «수평 방향(위팔 수평에서 굽힌 각)» + «기울기»로 따로 정한다. 2차에 시작 기울기를 끝으로 갈수록 풀었더니
            //   손이 가운데서 5cm 내려갔다(+7.0 → +1.7cm · 사용자 원하는 «살짝 솟음»의 반대). 기울기는 시작 값 + 같은 포물선.
            Vector3 h0 = Vector3.ProjectOnPlane(u0, yB).normalized, fh0 = Vector3.ProjectOnPlane(f0, yB).normalized;
            float flexH0 = Vector3.SignedAngle(h0, fh0, yB);
            float bendSign = Mathf.Sign(flexH0);
            float eF0 = Mathf.Asin(Mathf.Clamp(Vector3.Dot(f0.normalized, yB), -1f, 1f));
            float worstU = 0f, worstL = 0f, handMin = float.MaxValue, handMax = float.MinValue;
            float[] xi = (float[])p0.Clone();
            for (int i = 0; i < n; i++)
            {
                float s = i / (float)(n - 1);
                if (i == 0) { solved[0] = (float[])p0.Clone(); }
                else
                {
                    float sa = Mathf.SmoothStep(0f, 1f, s);
                    float sf = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((s - ElbowLag) / (1f - ElbowLag)));
                    float a = Mathf.Lerp(aStart, PullDeg, sa) * Mathf.Deg2Rad;
                    float e = elev + ArcBumpDeg * Mathf.Deg2Rad * 4f * s * (1f - s);
                    Vector3 h = Mathf.Cos(a) * xOut + Mathf.Sin(a) * zHead;
                    Vector3 u = (Mathf.Cos(e) * h + Mathf.Sin(e) * yB).normalized;
                    float flexH = Mathf.Lerp(flexH0, bendSign * EndFlexDeg, sf);
                    Vector3 fh = Quaternion.AngleAxis(flexH, yB) * h;
                    float eF = eF0 + ArcBumpDeg * Mathf.Deg2Rad * 4f * s * (1f - s);
                    Vector3 f = (Mathf.Cos(eF) * fh + Mathf.Sin(eF) * yB).normalized;
                    Quaternion turn = Quaternion.FromToRotation(f0, f);
                    var ti = new Target { upper = u, lower = f, distal = (turn * d0).normalized, radial = (turn * r0v).normalized };
                    Refine(handler, ref pose, arm, ti, xi, new[] { .04f, .02f, .01f, .005f });
                    Cost(handler, ref pose, arm, ti, xi);
                    Errors(arm, ti, out float eu2, out float el2, out _, out _);
                    if (eu2 + el2 > 5f)   // ★직전 값에서만 풀면 한 곳에서 갇혔다(2차 위팔 18.4°) → 격자 출발도 풀어 작은 쪽
                    {
                        var xg2 = (float[])xi.Clone();
                        GridStart(handler, ref pose, arm, ti, xg2);
                        for (int m = 0; m < xg2.Length; m++) xg2[m] = Mathf.Clamp(xg2[m], -LimitOf(m), LimitOf(m));
                        Refine(handler, ref pose, arm, ti, xg2, new[] { .1f, .05f, .02f, .01f, .005f });
                        if (Cost(handler, ref pose, arm, ti, xg2) < Cost(handler, ref pose, arm, ti, xi)) xi = xg2;
                        Cost(handler, ref pose, arm, ti, xi);
                        Errors(arm, ti, out eu2, out el2, out _, out _);
                    }
                    worstU = Mathf.Max(worstU, eu2); worstL = Mathf.Max(worstL, el2);
                    solved[i] = (float[])xi.Clone();
                }
                for (int m = 0; m < xi.Length; m++) pose.muscles[arm.muscles[m]] = solved[i][m];
                handler.SetHumanPose(ref pose);
                float hy = Vector3.Dot(arm.hand.position - arm.upper.position, yB); handMin = Mathf.Min(handMin, hy); handMax = Mathf.Max(handMax, hy);
            }
            log.Append($"프레임별 풀이 — 위팔 오차 최대 {worstU:F1}° · 아래팔 최대 {worstL:F1}° · 손 높이 변화 폭 {(handMax - handMin) * 100f:F1}cm\n");
            var pull = MakeArmClip(abduct, arm, solved, fps, false, Path.GetFileNameWithoutExtension(PullClipPath));
            var back = MakeArmClip(abduct, arm, solved, fps, true, Path.GetFileNameWithoutExtension(ReturnClipPath));
            Directory.CreateDirectory(Path.GetDirectoryName(PullClipPath));
            SaveClip(pull, PullClipPath); SaveClip(back, ReturnClipPath);
            AssetDatabase.SaveAssets();

            // 검수용 뼈 위치: 저장한 클립을 다시 재생해 잰다(미리보기 = 실제와 같은 길 · 규칙 9)
            var saved = AssetDatabase.LoadAssetAtPath<AnimationClip>(PullClipPath);
            var js = new StringBuilder("{\"log\":").Append(Q(log.ToString())).Append(",\"frames\":[");
            for (int i = 0; i < n; i++)
            {
                saved.SampleAnimation(c9, i / fps);
                if (i > 0) js.Append(',');
                js.Append('{');
                AppendBone(js, "hips", animator, HumanBodyBones.Hips, true); AppendBone(js, "chest", animator, HumanBodyBones.Chest);
                AppendBone(js, "neck", animator, HumanBodyBones.Neck); AppendBone(js, "head", animator, HumanBodyBones.Head);
                AppendBone(js, "lsh", animator, HumanBodyBones.LeftUpperArm); AppendBone(js, "rsh", animator, HumanBodyBones.RightUpperArm);
                js.Append(",\"sh\":").Append(V3(arm.upper.position)).Append(",\"el\":").Append(V3(arm.lower.position)).Append(",\"ha\":").Append(V3(arm.hand.position));
                js.Append('}');
            }
            js.Append("]}");
            Directory.CreateDirectory(Path.GetDirectoryName(PullReportPath));
            File.WriteAllText(PullReportPath, js.ToString(), new UTF8Encoding(false));
            Debug.Log("[V04당겨펴기] " + log + $"클립 {PullClipPath} · {ReturnClipPath} ({n}프레임 · {PullSeconds}초) · 검수 JSON {PullReportPath}");
        }
        finally
        {
            handler?.Dispose();
            foreach (var g in made) if (g != null) Object.DestroyImmediate(g);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    static AnimationClip MakeArmClip(AnimationClip baseClip, Arm arm, float[][] solved, float fps, bool reverse, string name)
    {
        var clip = new AnimationClip { name = name, frameRate = fps };
        float length = (solved.Length - 1) / fps;
        var replaced = new HashSet<string>();
        for (int m = 0; m < ArmMuscles.Length; m++)
        {
            string mn = arm.side + " " + ArmMuscles[m]; replaced.Add(mn);
            var keys = new Keyframe[solved.Length];
            for (int i = 0; i < solved.Length; i++) keys[i] = new Keyframe(i / fps, solved[reverse ? solved.Length - 1 - i : i][m]);
            var c = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++) { AnimationUtility.SetKeyLeftTangentMode(c, i, AnimationUtility.TangentMode.ClampedAuto); AnimationUtility.SetKeyRightTangentMode(c, i, AnimationUtility.TangentMode.ClampedAuto); }
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), mn), c);
        }
        foreach (var binding in AnimationUtility.GetCurveBindings(baseClip))
        {
            if (replaced.Contains(binding.propertyName)) continue;
            float v = AnimationUtility.GetEditorCurve(baseClip, binding).Evaluate(baseClip.length);   // ★외전 «끝» 값 — 앞 단계와 이어진다
            AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Linear(0f, v, length, v));
        }
        var settings = AnimationUtility.GetAnimationClipSettings(baseClip);
        settings.loopTime = false; settings.stopTime = length; settings.startTime = 0f;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        return clip;
    }

    static void AppendBone(StringBuilder sb, string key, Animator a, HumanBodyBones b, bool first = false)
    {
        var t = a.GetBoneTransform(b);
        if (!first) sb.Append(',');
        sb.Append('"').Append(key).Append("\":").Append(t != null ? V3(t.position) : "null");
    }

    static string V3(Vector3 v) => "[" + v.x.ToString("0.#####", CultureInfo.InvariantCulture) + "," + v.y.ToString("0.#####", CultureInfo.InvariantCulture) + "," + v.z.ToString("0.#####", CultureInfo.InvariantCulture) + "]";
    static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
}
