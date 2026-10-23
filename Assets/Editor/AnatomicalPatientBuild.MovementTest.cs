// ★시험 전용 · 2026-10-23
//   사용자 지시: ChatGPT에 «Claude Code가 하는 해부학적 움직임 시험을 네 리그(AnatomicalPatientRig)로도 똑같이» 시켰고,
//   그 작업이 Unity 충돌(15:42)로 멈춰 «마저 진행해 달라»고 했다. ChatGPT가 만든 손 목표 풀이기(AnatomicalArmTargetStudySolver)를 그대로 쓴다.
//   목표 경로는 PatientArmIkSim(c9 팔 IK 시뮬)과 같은 단계 · 같은 정의(몸 축 기준 abd · tilt · reach · 비틀기 · 손목)다.
//   ★잰 값은 전부 출력 뼈 위치에서 잰다(풀이기에 넣은 각도를 결과로 쓰지 않는다). 몸 축은 빌더가 잰 anatomicalBasis.
//   ★이 리그에는 침대 · 몸통 막힘과 방향별 어깨 한계가 없다(ChatGPT 보고) — 관통은 재지 않는다. 풀이기 범위는 «탐색 범위»이지 임상 한계가 아니다.
//   편집 모드 미리보기 장면에서만 돈다 — 열린 씬 · 프리팹을 고치지 않고 Play도 걸지 않는다.
//   결과: Temp/anat_sim/<시각>_<팔>/summary.txt · frames.csv · 단계마다 캡처 · (시연) v_*.png + phases.txt · Editor.log «[해부학리그시험]»
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static partial class AnatomicalPatientBuild
{
    private const string MoveTestMenuBoth = "GuideChuna/시험/해부학 환자/움직임 비교 시험 (양팔 · 편집 모드)";
    private const string MoveTestMenuRib2 = "GuideChuna/시험/해부학 환자/제2늑골 시연 (오른팔 · 편집 모드)";

    [MenuItem(MoveTestMenuBoth)] private static void MoveTestBoth() => RunMoveTest(false);
    [MenuItem(MoveTestMenuRib2)] private static void MoveTestRib2() => RunMoveTest(true);
    [MenuItem(MoveTestMenuBoth, true)] [MenuItem(MoveTestMenuRib2, true)] private static bool MoveTestValidate() => !EditorApplication.isPlaying;

    private struct MvPose { public bool initial, joint; public float abd, tilt, reach, twist, wrist, dev, flex; }   // joint(10-23) = 어깨 벌림 abd · 팔꿈치 flex로 손 자리
    private struct MvPhase { public string name; public float dur, hold; public MvPose to; }
    private static MvPose MD(float abd, float tilt, float reach = 1f, float twist = 0f, float wrist = 0f, float dev = 0f)
        => new MvPose { abd = abd, tilt = tilt, reach = reach, twist = twist, wrist = wrist, dev = dev };

    private static MvPose MJ(float abd, float flex) => new MvPose { joint = true, abd = abd, flex = flex, reach = 1f };

    private static List<MvPhase> MoveTestPhases(bool demo)
    {
        var p = new List<MvPhase>();
        void Add(string n, float d, float h, MvPose t) => p.Add(new MvPhase { name = n, dur = d, hold = h, to = t });
        MvPose start = new MvPose { initial = true, reach = 1f };
        if (demo)
        {
            // PatientArmIkSim 시연 4차(10-23 · 바디캠 «제2늑골 교정기법.mp4» 35~67초)와 같은 순서 · 각: 바이셉스 포즈 → 머리 위로 밀어 올리기 3회
            Add("1 시작 - 팔 옆에", .5f, 1f, start);
            Add("2 손을 잡고 바이셉스 포즈", 3f, 1f, MJ(90, 90));
            for (int i = 1; i <= 3; i++)
            {
                Add($"{2 * i + 1} 들숨 - 머리 위로 밀어 올리기 ({i}/3)", 3f, .5f, MJ(150, 30));
                Add($"{2 * i + 2} 날숨 - 바이셉스 포즈로 ({i}/3)", 3f, .5f, MJ(90, 90));
            }
            Add("9 놓고 원위치", 3f, 1f, start);
            return p;
        }
        // PatientArmIkSim.BuildPhases와 같은 순서(10-23 · 견인 단계는 «굽힌 팔»로 바뀐 판). 몸통 · 침대 누르기는 이 리그에 막힘이 없어 뺐다.
        Add("시작 자세", .3f, .5f, start);
        Add("옆으로 90° (침대 높이)", 2f, .8f, MD(90, 0));
        Add("모서리 아래로 20°", 1.5f, .8f, MD(90, -20));
        Add("모서리 아래로 40° (영상 자세)", 1.5f, .8f, MD(90, -40));
        Add("견인 — 더 당기기", 1f, .8f, MD(90, -40, 1.12f));
        Add("비틀기 +60°", 1.5f, .8f, MD(90, -40, 1f, 60));
        Add("비틀기 −60°", 2f, .8f, MD(90, -40, 1f, -60));
        Add("비틀기 −120° (한계 넘김)", 1.5f, .8f, MD(90, -40, 1f, -120));
        Add("손목 +50°", 1.5f, .8f, MD(90, -40, 1f, 0, 50));
        Add("손목 −50°", 2f, .8f, MD(90, -40, 1f, 0, -50));
        Add("손목 +100° (한계 넘김)", 1.5f, .8f, MD(90, -40, 1f, 0, 100));
        Add("손목 옆 꺾기 +60° (한계 넘김)", 1.5f, .8f, MD(90, -40, 1f, 0, 0, 60));
        Add("손목 옆 꺾기 −60° (한계 넘김)", 2f, .8f, MD(90, -40, 1f, 0, 0, -60));
        Add("아래로 80° (신전 한계 넘김)", 1.5f, .8f, MD(90, -80));
        Add("앞으로 90° (천장)", 2f, .8f, MD(0, 90));
        Add("옆으로 머리 위 170°", 2f, .8f, MD(170, 0));
        Add("앞으로 머리 위 170°", 2.5f, .8f, MD(0, 170));
        Add("머리 위에서 뒤로 (옆 150° · 뒤 30°)", 2f, .8f, MD(150, -30));
        Add("몸 앞을 가로질러 (내전)", 2f, .8f, MD(-45, 60));
        Add("팔꿈치 굽힘 (손을 어깨 쪽으로)", 1.5f, .8f, MD(90, 30, .45f));
        Add("빠르게 휘두르기 (0.5초에 옆 → 머리 위)", .5f, .8f, MD(170, 0));
        Add("손을 어깨 쪽(0.6배)으로 — 팔꿈치 굽힘", 2f, .8f, MD(90, -40, .6f));
        Add("굽힌 팔 + 비틀기 +80°", 1.5f, .8f, MD(90, -40, .6f, 80));
        Add("굽힌 팔 + 비틀기 −80°", 2f, .8f, MD(90, -40, .6f, -80));
        Add("굽힌 팔 + 손목 +50°", 1.5f, .8f, MD(90, -40, .6f, 0, 50));
        Add("머리 위 + 손 비틀기 +60°", 2f, .8f, MD(170, 0, 1f, 60));
        Add("머리 위 + 손 비틀기 +150°", 1.5f, .8f, MD(170, 0, 1f, 150));
        Add("머리 위 + 손 비틀기 −60°", 2f, .8f, MD(170, 0, 1f, -60));
        Add("원위치", 2f, .5f, start);
        return p;
    }

    private static void RunMoveTest(bool demo)
    {
        string receiptPath = File.Exists("Temp/model-audit/latest_build.json") ? "Temp/model-audit/latest_build.json" : OutputFolder + "/LatestBuild.json";
        var receipt = JsonUtility.FromJson<BuildReceipt>(File.ReadAllText(receiptPath));
        var preview = new PreviewRenderUtility();
        try
        {
            GameObject root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(receipt.prefab));
            preview.AddSingleGO(root);
            var rig = root.GetComponent<AnatomicalPatientRig>();
            var lab = root.GetComponent<AnatomicalPatientLab>();
            if (rig == null) throw new InvalidOperationException("리그 컴포넌트가 없다: " + receipt.prefab);
            rig.SetAutomaticEvaluation(false);
            rig.SetInputSource(AnatomicalPatientRig.InputSource.Manual);
            if (!rig.Evaluate(rig.RestPose)) throw new InvalidOperationException("쉰 자세를 풀지 못했다.");
            if (lab != null) lab.SetView(AnatomicalPatientLab.View.Skin);
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.backgroundColor = new Color(.32f, .34f, .38f);
            preview.camera.fieldOfView = 40f;
            preview.camera.nearClipPlane = .01f; preview.camera.farClipPlane = 20f;
            preview.lights[0].intensity = 1.3f; preview.lights[0].transform.rotation = Quaternion.Euler(35f, -35f, 0f);
            preview.lights[1].intensity = 1f; preview.lights[1].transform.rotation = Quaternion.Euler(25f, 145f, 0f);

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            foreach (int side in demo ? new[] { 1 } : new[] { 1, 0 })   // limbs[0] = 왼팔 · [1] = 오른팔(Validation과 같음)
                RunMoveTestSide(rig, preview, root, side, demo, stamp, receipt.prefab);
        }
        catch (Exception e) { Debug.LogException(e); }
        finally { preview.Cleanup(); }
    }

    private static void RunMoveTestSide(AnatomicalPatientRig rig, PreviewRenderUtility preview, GameObject root, int sideIdx, bool demo, string stamp, string prefab)
    {
        var data = new SerializedObject(rig);
        var limbProp = data.FindProperty("limbs").GetArrayElementAtIndex(sideIdx);
        bool left = limbProp.FindPropertyRelative("left").boolValue;
        string label = left ? "왼팔" : "오른팔";
        Transform humerus = (Transform)limbProp.FindPropertyRelative("humerus").objectReferenceValue;
        Transform ulna = (Transform)limbProp.FindPropertyRelative("ulna").objectReferenceValue;
        Transform radius = (Transform)limbProp.FindPropertyRelative("radius").objectReferenceValue;
        Transform hand = (Transform)limbProp.FindPropertyRelative("hand").objectReferenceValue;
        Transform other = (Transform)data.FindProperty("limbs").GetArrayElementAtIndex(1 - sideIdx).FindPropertyRelative("humerus").objectReferenceValue;
        Transform space = rig.RigSpace;
        var basisProp = data.FindProperty("anatomicalBasis");
        // 몸 축(월드) — 빌더가 잰 앞 · 위. 바깥(L)은 두 어깨 가운데에서 이 어깨 쪽(부호를 추론하지 않는다 — CLAUDE.md 규칙 9)
        Vector3 U = space.TransformDirection(basisProp.FindPropertyRelative("up").vector3Value).normalized;
        Vector3 F = space.TransformDirection(basisProp.FindPropertyRelative("forward").vector3Value).normalized;
        F = (F - Vector3.Dot(F, U) * U).normalized;

        rig.Evaluate(rig.RestPose);
        Vector3 Lraw = humerus.position - (humerus.position + other.position) * .5f;
        Vector3 L = (Lraw - Vector3.Dot(Lraw, U) * U - Vector3.Dot(Lraw, F) * F).normalized;
        float len = Vector3.Distance(humerus.position, ulna.position) + Vector3.Distance(ulna.position, hand.position);
        Quaternion humRest = humerus.localRotation;
        Vector3 restBend = limbProp.FindPropertyRelative("restBendDirection").vector3Value;   // rigSpace — 위팔에 붙여 옮긴다

        var solver = new AnatomicalArmTargetStudySolver(rig, hand, left);
        string outDir = Path.GetFullPath(Path.Combine("Temp", "anat_sim", stamp + "_" + label + (demo ? "_제2늑골시연" : "")));
        Directory.CreateDirectory(outDir);
        var csv = new StringBuilder("phase,step,pos_err_mm,rot_err_deg,elbow_flex,reverse,side_dev,elev,plane,behind,medial,jump_deg,envelope,req_humrot,req_forearm,req_wflex,req_wdev\n");
        var summary = new StringBuilder();
        var phaseLog = new StringBuilder();
        Debug.Log($"[해부학리그시험] {label} 시작 — 프리팹 {prefab} · 팔 길이 {len * 1000f:F0}mm · 결과 {outDir}");

        // 시작 자세 = 팔을 몸 옆으로 내린 자리(c9 IK 시뮬의 애니 시작 자세에 맞춘다 · 쉰 자세는 T자일 수 있다)
        float l1 = Vector3.Distance(humerus.position, ulna.position), l2 = Vector3.Distance(ulna.position, hand.position);
        Vector3 Sgoal(MvPose t)
        {
            if (t.joint)
            {
                // PatientArmIkSim Mode.Joint와 같은 정의 — 위팔 = −위에서 바깥으로 abd · 아래팔 = 같은 면 안 머리 쪽으로 flex
                float aJ = t.abd * Mathf.Deg2Rad, fJ = t.flex * Mathf.Deg2Rad;
                Vector3 uJ = (-U * Mathf.Cos(aJ) + L * Mathf.Sin(aJ)).normalized;
                Vector3 perp = U - Vector3.Dot(U, uJ) * uJ;
                perp = perp.sqrMagnitude > 1e-8f ? perp.normalized : L;
                return humerus.position + uJ * l1 + (uJ * Mathf.Cos(fJ) + perp * Mathf.Sin(fJ)) * l2;
            }
            return humerus.position + PoseDir(t.initial ? MD(12, 0) : t, U, F, L) * len * (t.initial ? 1f : t.reach);
        }
        AnatomicalPatientRig.Pose pose = rig.RestPose;
        Quaternion rot0 = hand.rotation;
        pose = solver.Solve(pose, Sgoal(new MvPose { initial = true }), rot0, 64);
        rot0 = hand.rotation;   // 팔 내린 자세의 손 방향 = 잡기 기준
        var prevRot = new Quaternion[4]; bool hasPrev = false;
        MvPose from = new MvPose { initial = true, reach = 1f };
        int frame = 0, phaseNo = 0;

        foreach (MvPhase ph in MoveTestPhases(demo))
        {
            if (demo) phaseLog.Append(frame).Append('\t').Append(ph.name).Append('\n');
            int steps = Mathf.Max(1, Mathf.RoundToInt((ph.dur + ph.hold) * 30f));
            int moveSteps = Mathf.Max(1, Mathf.RoundToInt(ph.dur * 30f));
            float mPos = 0f, mRot = 0f, mJump = 0f, mFlex = 0f, mSide = 0f, mElev = 0f, mBehind = -999f, mMedial = -999f;
            int nRev = 0, nJump = 0, nEnv = 0, nPos = 0;
            Vector3 a0 = Sgoal(from) - humerus.position;
            for (int s = 1; s <= steps; s++)
            {
                float k = Mathf.Clamp01(s / (float)moveSteps); k = k * k * (3f - 2f * k);
                Vector3 S = humerus.position;
                Vector3 a1 = Sgoal(ph.to) - S;
                Vector3 target = from.joint && ph.to.joint
                    ? Sgoal(MJ(Mathf.Lerp(from.abd, ph.to.abd, k), Mathf.Lerp(from.flex, ph.to.flex, k)))   // 관절 각끼리는 각을 보간(10-23)
                    : S + Vector3.Slerp(a0.normalized, a1.normalized, k) * Mathf.Lerp(a0.magnitude, a1.magnitude, k);
                float twist = Mathf.Lerp(from.twist, ph.to.twist, k), wrist = Mathf.Lerp(from.wrist, ph.to.wrist, k), dev = Mathf.Lerp(from.dev, ph.to.dev, k);
                Vector3 armDir = (target - S).normalized;
                Vector3 wAxis = Vector3.Cross(armDir, F); if (wAxis.sqrMagnitude < 1e-6f) wAxis = Vector3.Cross(armDir, U);
                Vector3 dAxis = Vector3.ProjectOnPlane(F, armDir); if (dAxis.sqrMagnitude < 1e-6f) dAxis = Vector3.ProjectOnPlane(U, armDir);
                Quaternion tRot = Quaternion.AngleAxis(twist, armDir) * Quaternion.AngleAxis(wrist, wAxis.normalized) * Quaternion.AngleAxis(dev, dAxis.normalized) * rot0;
                pose = solver.Solve(pose, target, tRot, 18);

                // ── 출력 뼈에서 잰다 ──
                Vector3 sh = humerus.position, el = ulna.position, wr = hand.position;
                Vector3 up = el - sh, lo = wr - el;
                float flex = Vector3.Angle(up, lo);
                Vector3 bendNow = space.TransformDirection(humerus.localRotation * Quaternion.Inverse(humRest) * restBend);
                Vector3 uN = up.normalized;
                Vector3 fd = (bendNow - Vector3.Dot(bendNow, uN) * uN).normalized;
                float ca = Vector3.Dot(lo, uN), cb = Vector3.Dot(lo, fd), cc = (lo - ca * uN - cb * fd).magnitude;
                float flexIn = Mathf.Atan2(cb, ca) * Mathf.Rad2Deg;
                float sideDev = Mathf.Atan2(cc, Mathf.Sqrt(ca * ca + cb * cb)) * Mathf.Rad2Deg;
                bool rev = flexIn < -5f;
                float elev = Vector3.Angle(uN, -U);
                Vector3 hz = uN - Vector3.Dot(uN, U) * U;
                float plane = hz.sqrMagnitude > 1e-8f ? Mathf.Atan2(Vector3.Dot(hz.normalized, L), Vector3.Dot(hz.normalized, F)) * Mathf.Rad2Deg : 0f;
                float behind = Mathf.Asin(Mathf.Clamp(Vector3.Dot(uN, -F), -1f, 1f)) * Mathf.Rad2Deg;
                float medial = Mathf.Asin(Mathf.Clamp(Vector3.Dot(uN, -L), -1f, 1f)) * Mathf.Rad2Deg;
                Transform[] bones = { humerus, ulna, radius, hand };
                float jump = 0f;
                for (int b = 0; b < 4; b++)
                {
                    if (hasPrev) jump = Mathf.Max(jump, Quaternion.Angle(prevRot[b], bones[b].localRotation));
                    prevRot[b] = bones[b].localRotation;
                }
                hasPrev = true;
                float posMm = solver.LastPositionErrorMetres * 1000f;
                mPos = Mathf.Max(mPos, posMm); mRot = Mathf.Max(mRot, solver.LastRotationErrorDegrees);
                mJump = Mathf.Max(mJump, jump); mFlex = Mathf.Max(mFlex, flex); mSide = Mathf.Max(mSide, sideDev);
                mElev = Mathf.Max(mElev, elev); mBehind = Mathf.Max(mBehind, behind); mMedial = Mathf.Max(mMedial, medial);
                if (rev && flex > 10f) nRev++;
                if (jump > 13.2f) nJump++;            // 1/30초에 360°/초 × 1.1 — c9 IK 시뮬과 같은 기준
                if (solver.LastAtEnvelope) nEnv++;
                if (posMm > 20f) nPos++;
                AnatomicalPatientRig.ArmPose arm = left ? pose.leftArm : pose.rightArm;
                csv.Append(ph.name.Replace(',', ' ')).Append(',').Append(s).Append(',').Append(posMm.ToString("F1")).Append(',').Append(solver.LastRotationErrorDegrees.ToString("F1")).Append(',')
                   .Append(flex.ToString("F1")).Append(',').Append(rev ? 1 : 0).Append(',').Append(sideDev.ToString("F1")).Append(',').Append(elev.ToString("F1")).Append(',').Append(plane.ToString("F1")).Append(',')
                   .Append(behind.ToString("F1")).Append(',').Append(medial.ToString("F1")).Append(',').Append(jump.ToString("F1")).Append(',').Append(solver.LastAtEnvelope ? 1 : 0).Append(',')
                   .Append(arm.humeralRotation.ToString("F1")).Append(',').Append(arm.forearmRotation.ToString("F1")).Append(',').Append(arm.wristFlexion.ToString("F1")).Append(',').Append(arm.wristDeviation.ToString("F1")).Append('\n');

                if (demo)
                {
                    Vector3 c = sh + L * .25f - U * .1f;
                    CapturePreview(preview, root, Path.Combine(outDir, $"a_{frame:00000}.png"), c + F * 2.2f + L * .6f + U * .2f, c);
                    CapturePreview(preview, root, Path.Combine(outDir, $"b_{frame:00000}.png"), c + F * 2.6f + U * .15f, c);   // 10-23: 정면(관상면 = 누운 환자의 침대 면) — 바이셉스 포즈 · 머리 위로가 이 면에서 보인다
                }
                frame++;
            }
            AnatomicalPatientRig.ArmPose endArm = left ? pose.leftArm : pose.rightArm;
            string line = $"[해부학리그시험] {label} {phaseNo:00} {ph.name} | 손 오차 최대 {mPos:F0}mm(20mm 넘은 걸음 {nPos}) · 손 방향 오차 최대 {mRot:F0}° · " +
                          $"팔꿈치 최대 {mFlex:F0}° · 반대 꺾임 {nRev} · 경첩 밖 최대 {mSide:F0}° · 튐 {nJump}걸음(최대 {mJump:F1}°/걸음) · 거상 최대 {mElev:F0}° · 신전 {mBehind:F0}° · 내전 {mMedial:F0}° · " +
                          $"탐색 범위 끝 {nEnv}걸음 | 끝 요청값: 위팔 회전 {endArm.humeralRotation:+0;-0}° · 아래팔 {endArm.forearmRotation:+0;-0}° · 손목 굴곡 {endArm.wristFlexion:+0;-0}° · 편위 {endArm.wristDeviation:+0;-0}° | {steps}걸음";
            Debug.Log(line);
            summary.Append(line).Append('\n');
            if (!demo)
            {
                Vector3 c = humerus.position + L * .3f - U * .15f;
                string n = $"{phaseNo:00}";
                CapturePreview(preview, root, Path.Combine(outDir, n + "_1앞.png"), c + F * 2.2f + L * .5f + U * .3f, c);
                CapturePreview(preview, root, Path.Combine(outDir, n + "_2옆.png"), c + L * 2.4f + F * .3f + U * .1f, c);
            }
            from = ph.to;
            phaseNo++;
        }
        File.WriteAllText(Path.Combine(outDir, "frames.csv"), csv.ToString(), new UTF8Encoding(true));
        File.WriteAllText(Path.Combine(outDir, "summary.txt"), summary.ToString(), new UTF8Encoding(false));
        if (demo) File.WriteAllText(Path.Combine(outDir, "phases.txt"), phaseLog.ToString(), new UTF8Encoding(false));
        Debug.Log($"[해부학리그시험] {label} 끝 — {outDir}");
        rig.Evaluate(rig.RestPose);
    }

    /// <summary>PatientArmIkSim.PoseToPosition과 같은 정의 — abd = 내린 자리(−위)에서 바깥으로 · tilt = 앞으로(음수 = 뒤).</summary>
    private static Vector3 PoseDir(MvPose p, Vector3 U, Vector3 F, Vector3 L)
    {
        float a = p.abd * Mathf.Deg2Rad, b = p.tilt * Mathf.Deg2Rad;
        Vector3 d0 = -U * Mathf.Cos(a) + L * Mathf.Sin(a);
        return (d0 * Mathf.Cos(b) + F * Mathf.Sin(b)).normalized;
    }
}
