// ★시험 전용 · 2026-10-23
//   사용자 지시: ChatGPT에 «Claude Code가 하는 해부학적 움직임 시험을 네 리그(AnatomicalPatientRig)로도 똑같이» 시켰고,
//   제2늑골 시연은 AnatomicalMotionSequence의 관절 입력을 그대로 평가한다. 뷰어와 같은 경로이며 손 회전을 월드에 고정하지 않는다.
//   별도의 손 목표 탐색 시험은 PatientArmIkSim과 비슷한 스트레스 입력이며, 임상 관절 한계 시험이 아니다.
//   ★잰 값은 전부 출력 뼈 위치에서 잰다(풀이기에 넣은 각도를 결과로 쓰지 않는다). 몸 축은 빌더가 잰 anatomicalBasis.
//   ★이 리그에는 침대 · 몸통 막힘과 방향별 어깨 한계가 없다(ChatGPT 보고) — 관통은 재지 않는다. 풀이기 범위는 «탐색 범위»이지 임상 한계가 아니다.
//   편집 모드 미리보기 장면에서만 돈다 — 열린 씬 · 프리팹을 고치지 않고 Play도 걸지 않는다.
//   결과: Temp/anat_sim/<시각>_<팔>/summary.txt · frames.csv · 캡처. 시연은 30Hz 수치 + 최대 15개 시점의 피부/골격 정지화면만 남긴다.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

public static partial class AnatomicalPatientBuild
{
    private const string MoveTestMenuBoth = "GuideChuna/시험/해부학 환자/움직임 비교 시험 (양팔 · 편집 모드)";
    private const string MoveTestMenuRib2 = "GuideChuna/시험/해부학 환자/제2늑골 시연 (오른팔 · 편집 모드)";

    [MenuItem(MoveTestMenuBoth)] private static void MoveTestBoth() => RunMoveTest(false);
    [MenuItem(MoveTestMenuRib2)] private static void MoveTestRib2() => RunMoveTest(true);
    [MenuItem(MoveTestMenuBoth, true)] [MenuItem(MoveTestMenuRib2, true)] private static bool MoveTestValidate() => !EditorApplication.isPlaying;

    private struct MvPose { public bool initial; public float abd, tilt, reach, twist, wrist, dev; }
    private struct MvPhase { public string name; public float dur, hold; public MvPose to; }
    private static MvPose MD(float abd, float tilt, float reach = 1f, float twist = 0f, float wrist = 0f, float dev = 0f)
        => new MvPose { abd = abd, tilt = tilt, reach = reach, twist = twist, wrist = wrist, dev = dev };

    private static List<MvPhase> MoveTestPhases()
    {
        var p = new List<MvPhase>();
        void Add(string n, float d, float h, MvPose t) => p.Add(new MvPhase { name = n, dur = d, hold = h, to = t });
        MvPose start = new MvPose { initial = true, reach = 1f };
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
            GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(receipt.prefab), preview.camera.gameObject.scene);
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
            if (demo) RunVideoReferenceStudy(rig, preview, root, 1, stamp, receipt.prefab, lab);
            else foreach (int side in new[] { 1, 0 })   // limbs[0] = 왼팔 · [1] = 오른팔(Validation과 같음)
                RunMoveTestSide(rig, preview, root, side, stamp, receipt.prefab);
        }
        catch (Exception e) { Debug.LogException(e); }
        finally { preview.Cleanup(); }
    }

    private static void RunMoveTestSide(AnatomicalPatientRig rig, PreviewRenderUtility preview, GameObject root, int sideIdx, string stamp, string prefab)
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
        string outDir = Path.GetFullPath(Path.Combine("Temp", "anat_sim", stamp + "_" + label));
        Directory.CreateDirectory(outDir);
        var csv = new StringBuilder("phase,step,pos_err_mm,rot_err_deg,elbow_flex,reverse,side_dev,elev,plane,behind,medial,jump_deg,envelope,req_humrot,req_forearm,req_wflex,req_wdev,t_seconds,target_x,target_y,target_z,target_qx,target_qy,target_qz,target_qw,hand_x,hand_y,hand_z,hand_qx,hand_qy,hand_qz,hand_qw\n");
        var summary = new StringBuilder();
        Debug.Log($"[해부학리그시험] {label} 시작 — 프리팹 {prefab} · 팔 길이 {len * 1000f:F0}mm · 결과 {outDir}");
        summary.AppendLine("손 목표 스트레스 탐색. 목표는 시작 시 어깨 월드 위치에 고정한다. 몸 축으로 만든 회전 입력은 독립적인 손목 ROM 판정이 아니다. 탐색 범위와 20mm/13.2도 기준은 임상 통과 기준이 아니다.");

        // 목표 기준점은 풀이 결과로 이동시키지 않는다. 견갑대 이동도 손의 월드 오차에 남긴다.
        Vector3 shoulderAnchor = humerus.position;
        Vector3 Sgoal(MvPose t)
        {
            return shoulderAnchor + PoseDir(t.initial ? MD(12, 0) : t, U, F, L) * len * (t.initial ? 1f : t.reach);
        }
        AnatomicalPatientRig.Pose pose = rig.RestPose;
        AnatomicalPatientRig.ArmPose startArm = left ? pose.leftArm : pose.rightArm;
        startArm.shoulderElevation = 12f; startArm.elevationPlane = 90f;
        startArm.elbowFlexion = 0f; startArm.humeralRotation = 0f;
        startArm.forearmRotation = startArm.wristFlexion = startArm.wristDeviation = 0f;
        if (left) pose.leftArm = startArm; else pose.rightArm = startArm;
        if (!rig.Evaluate(pose)) throw new InvalidOperationException("손 목표 시험의 시작 자세 평가에 실패했다.");
        Quaternion rot0 = hand.rotation;   // 실제 팔 내린 중립 손 방향. T자 손 방향을 억지로 유지하지 않는다.
        var prevRot = new Quaternion[4]; bool hasPrev = false;
        MvPose from = new MvPose { initial = true, reach = 1f };
        int frame = 0, phaseNo = 0;

        foreach (MvPhase ph in MoveTestPhases())
        {
            int steps = Mathf.Max(1, Mathf.RoundToInt((ph.dur + ph.hold) * 30f));
            int moveSteps = Mathf.Max(1, Mathf.RoundToInt(ph.dur * 30f));
            float mPos = 0f, mRot = 0f, mJump = 0f, mFlex = 0f, mSide = 0f, mElev = 0f, mBehind = -999f, mMedial = -999f;
            int nRev = 0, nJump = 0, nEnv = 0, nPos = 0;
            Vector3 a0 = Sgoal(from) - shoulderAnchor;
            for (int s = 1; s <= steps; s++)
            {
                float k = Mathf.Clamp01(s / (float)moveSteps); k = k * k * (3f - 2f * k);
                Vector3 S = shoulderAnchor;
                Vector3 a1 = Sgoal(ph.to) - S;
                Vector3 target = S + Vector3.Slerp(a0.normalized, a1.normalized, k) * Mathf.Lerp(a0.magnitude, a1.magnitude, k);
                float twist = Mathf.Lerp(from.twist, ph.to.twist, k), wrist = Mathf.Lerp(from.wrist, ph.to.wrist, k), dev = Mathf.Lerp(from.dev, ph.to.dev, k);
                Vector3 armDir = (target - S).normalized;
                Vector3 wAxis = Vector3.Cross(armDir, F); if (wAxis.sqrMagnitude < 1e-6f) wAxis = Vector3.Cross(armDir, U);
                Vector3 dAxis = Vector3.ProjectOnPlane(F, armDir); if (dAxis.sqrMagnitude < 1e-6f) dAxis = Vector3.ProjectOnPlane(U, armDir);
                Quaternion tRot = Quaternion.AngleAxis(twist, armDir) * Quaternion.AngleAxis(wrist, wAxis.normalized) * Quaternion.AngleAxis(dev, dAxis.normalized) * rot0;
                pose = solver.Solve(pose, target, tRot, 18);
                if (!solver.LastEvaluationSucceeded) throw new InvalidOperationException("손 목표 풀이의 FK 평가에 실패했다: " + ph.name);

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
                float posMm = Vector3.Distance(hand.position, target) * 1000f;
                float rotationError = Quaternion.Angle(hand.rotation, tRot);
                mPos = Mathf.Max(mPos, posMm); mRot = Mathf.Max(mRot, rotationError);
                mJump = Mathf.Max(mJump, jump); mFlex = Mathf.Max(mFlex, flex); mSide = Mathf.Max(mSide, sideDev);
                mElev = Mathf.Max(mElev, elev); mBehind = Mathf.Max(mBehind, behind); mMedial = Mathf.Max(mMedial, medial);
                if (rev && flex > 10f) nRev++;
                if (jump > 13.2f) nJump++;            // 1/30초에 360°/초 × 1.1 — c9 IK 시뮬과 같은 기준
                if (solver.LastAtEnvelope) nEnv++;
                if (posMm > 20f) nPos++;
                AnatomicalPatientRig.ArmPose arm = left ? pose.leftArm : pose.rightArm;
                csv.Append(ph.name.Replace(',', ' ')).Append(',').Append(s);
                AppendMoveValues(csv, posMm, rotationError, flex, rev ? 1f : 0f, sideDev, elev, plane, behind, medial, jump,
                    solver.LastAtEnvelope ? 1f : 0f, arm.humeralRotation, arm.forearmRotation, arm.wristFlexion, arm.wristDeviation, frame / 30f);
                AppendMoveVector(csv, target); AppendMoveQuaternion(csv, tRot);
                AppendMoveVector(csv, hand.position); AppendMoveQuaternion(csv, hand.rotation); csv.Append('\n');
                frame++;
            }
            AnatomicalPatientRig.ArmPose endArm = left ? pose.leftArm : pose.rightArm;
            string line = $"[해부학리그시험] {label} {phaseNo:00} {ph.name} | 손 오차 최대 {mPos:F0}mm(20mm 넘은 걸음 {nPos}) · 손 방향 오차 최대 {mRot:F0}° · " +
                          $"팔꿈치 최대 {mFlex:F0}° · 반대 꺾임 {nRev} · 경첩 밖 최대 {mSide:F0}° · 튐 {nJump}걸음(최대 {mJump:F1}°/걸음) · 거상 최대 {mElev:F0}° · 신전 {mBehind:F0}° · 내전 {mMedial:F0}° · " +
                          $"탐색 범위 끝 {nEnv}걸음 | 끝 요청값: 위팔 회전 {endArm.humeralRotation:+0;-0}° · 아래팔 {endArm.forearmRotation:+0;-0}° · 손목 굴곡 {endArm.wristFlexion:+0;-0}° · 편위 {endArm.wristDeviation:+0;-0}° | {steps}걸음";
            Debug.Log(line);
            summary.Append(line).Append('\n');
            Vector3 c = humerus.position + L * .3f - U * .15f;
            string n = $"{phaseNo:00}";
            CapturePreview(preview, root, Path.Combine(outDir, n + "_1앞.png"), c + F * 2.2f + L * .5f + U * .3f, c);
            CapturePreview(preview, root, Path.Combine(outDir, n + "_2옆.png"), c + L * 2.4f + F * .3f + U * .1f, c);
            from = ph.to;
            phaseNo++;
        }
        File.WriteAllText(Path.Combine(outDir, "frames.csv"), csv.ToString(), new UTF8Encoding(true));
        File.WriteAllText(Path.Combine(outDir, "summary.txt"), summary.ToString(), new UTF8Encoding(false));
        Debug.Log($"[해부학리그시험] {label} 끝 — {outDir}");
        rig.Evaluate(rig.RestPose);
    }

    [Serializable]
    private sealed class VideoReferenceStudyReport
    {
        public string prefab;
        public string sequenceSource;
        public string sequenceSha256;
        public string inputMode = "AnatomicalMotionSequence.Sample -> AnatomicalPatientRig.Evaluate; direct joint trajectory";
        public string limitation = "Video-inspired approximate trajectory. No video-derived 3D ground truth, collision, force or clinical validation.";
        public int sampleRate = 30;
        public int sampledFrames;
        public float durationSeconds;
        public float maxElbowRequestErrorDegrees;
        public float maxElevationRequestErrorDegrees;
        public float maxNeutralWristErrorDegrees;
        public float maxRequestedWristDegrees;
        public float maxFrameRotationDegrees;
        public int heldPoseSamples;
        public float minimumHeldForearmHeadwardDot = 1f;
        public int repeatedPoseComparisons;
        public float maxRepeatedPosePositionMm;
        public float maxRepeatedPoseRotationDegrees;
        public bool outputMatchesRequestedJoints;
        public bool neutralWristPreserved;
        public bool repeatedPosesDeterministic;
        public bool heldForearmPointsHeadward;
    }

    private sealed class MovePoseSnapshot
    {
        public Vector3[] positions;
        public Quaternion[] rotations;
    }

    private static void RunVideoReferenceStudy(AnatomicalPatientRig rig, PreviewRenderUtility preview,
        GameObject root, int sideIdx, string stamp, string prefab, AnatomicalPatientLab lab)
    {
        if (lab == null) throw new InvalidOperationException("피부/골격 시연에 필요한 실험실 컴포넌트가 없다.");
        var data = new SerializedObject(rig);
        var limb = data.FindProperty("limbs").GetArrayElementAtIndex(sideIdx);
        bool left = limb.FindPropertyRelative("left").boolValue;
        string label = left ? "왼팔" : "오른팔";
        Transform clavicle = (Transform)limb.FindPropertyRelative("clavicle").objectReferenceValue;
        Transform scapula = (Transform)limb.FindPropertyRelative("scapula").objectReferenceValue;
        Transform humerus = (Transform)limb.FindPropertyRelative("humerus").objectReferenceValue;
        Transform ulna = (Transform)limb.FindPropertyRelative("ulna").objectReferenceValue;
        Transform radius = (Transform)limb.FindPropertyRelative("radius").objectReferenceValue;
        Transform hand = (Transform)limb.FindPropertyRelative("hand").objectReferenceValue;
        var bones = new[] { clavicle, scapula, humerus, ulna, radius, hand };
        foreach (Transform bone in bones) if (bone == null) throw new InvalidOperationException("시연 출력 뼈 참조가 없다.");
        Transform space = rig.RigSpace;
        var basis = data.FindProperty("anatomicalBasis");
        Vector3 upAxis = space.TransformDirection(basis.FindPropertyRelative("up").vector3Value).normalized;
        Vector3 forward = space.TransformDirection(basis.FindPropertyRelative("forward").vector3Value).normalized;
        Vector3 outward = space.TransformDirection(basis.FindPropertyRelative("right").vector3Value).normalized * (left ? -1f : 1f);
        if (!rig.Evaluate(rig.RestPose)) throw new InvalidOperationException("시연 기준 자세 평가에 실패했다.");
        Quaternion humerusRest = humerus.localRotation;
        Quaternion ulnaRest = ulna.rotation;
        Quaternion handRest = hand.rotation;
        Vector3 restBend = limb.FindPropertyRelative("restBendDirection").vector3Value;

        var motion = AnatomicalMotionSequence.Motion.Rib2Study;
        float duration = AnatomicalMotionSequence.Duration(motion);
        if (duration <= 0f || float.IsNaN(duration) || float.IsInfinity(duration))
            throw new InvalidOperationException("시연 길이가 유효하지 않다.");
        int lastFrame = Mathf.CeilToInt(duration * 30f);
        string outDir = Path.GetFullPath(Path.Combine("Temp", "anat_sim", stamp + "_" + label + "_제2늑골_공유관절경로"));
        Directory.CreateDirectory(outDir);
        var report = new VideoReferenceStudyReport { prefab = prefab, durationSeconds = duration };
        foreach (string guid in AssetDatabase.FindAssets("AnatomicalMotionSequence t:MonoScript"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path) != "AnatomicalMotionSequence") continue;
            report.sequenceSource = path;
            using (var sha = SHA256.Create())
                report.sequenceSha256 = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
            break;
        }
        Debug.Log($"[해부학리그시험] {label} 제2늑골 공유 관절 경로 시작 — {duration:F2}초, 30Hz, 피부/골격 정지화면 최대 15시점 · 손 방향 월드 고정 없음 · {outDir}");
        var csv = new StringBuilder("sample,t_seconds,phase,input_mode,req_elev,req_plane,req_humrot,req_elbow,req_forearm,req_wflex,req_wdev,actual_elev,actual_elbow,elbow_error_deg,elevation_error_deg,neutral_wrist_error_deg,frame_jump_deg,forearm_headward_dot");
        foreach (string boneName in new[] { "clavicle", "scapula", "humerus", "ulna", "radius", "hand" })
            csv.Append(',').Append(boneName).Append("_x,").Append(boneName).Append("_y,").Append(boneName).Append("_z,")
               .Append(boneName).Append("_qx,").Append(boneName).Append("_qy,").Append(boneName).Append("_qz,").Append(boneName).Append("_qw");
        csv.Append('\n');
        var stills = new StringBuilder("sample,t_seconds,phase,skin_file,bones_file\n");
        var firstPoses = new Dictionary<string, MovePoseSnapshot>();
        var previous = new Quaternion[bones.Length];
        bool hasPrevious = false;
        int nextStill = 0;
        // Diagnostic checkpoints for the current 34s shared sequence; each pose is still sampled through its public API.
        float[] stillTimes = { 0f, 2f, 4f, 6f, 9.2f, 12.4f, 14f, 17.2f, 20.4f, 22f, 25.2f, 28.4f, 30f, 32f, duration };
        try
        {
            for (int frame = 0; frame <= lastFrame; frame++)
            {
                float seconds = Mathf.Min(duration, frame / 30f);
                AnatomicalPatientRig.Pose pose = AnatomicalMotionSequence.Sample(rig.RestPose, motion, seconds, left);
                AnatomicalPatientRig.ArmPose arm = left ? pose.leftArm : pose.rightArm;
                if (!rig.Evaluate(pose)) throw new InvalidOperationException("시연 관절 경로 평가 실패: " + seconds.ToString("F3", CultureInfo.InvariantCulture));

                Vector3 upper = (ulna.position - humerus.position).normalized;
                Vector3 lower = (hand.position - ulna.position).normalized;
                Vector3 bend = space.TransformDirection(humerus.localRotation * Quaternion.Inverse(humerusRest) * restBend);
                // Removed: the upper-axis component. Signed flexion is measured in the calibrated elbow hinge plane.
                bend = Vector3.ProjectOnPlane(bend, upper).normalized;
                float actualElbow = Mathf.Atan2(Vector3.Dot(lower, bend), Vector3.Dot(lower, upper)) * Mathf.Rad2Deg;
                float actualElevation = Vector3.Angle(upper, -upAxis);
                float headward = Vector3.Dot(lower, upAxis);
                float elbowError = Mathf.Abs(actualElbow - arm.elbowFlexion);
                float elevationError = Mathf.Abs(actualElevation - arm.shoulderElevation);
                // Relative output transforms measure wrist compensation independently of solver diagnostics.
                Quaternion neutralHand = Quaternion.AngleAxis(arm.forearmRotation, lower) * (ulna.rotation * Quaternion.Inverse(ulnaRest)) * handRest;
                float neutralWristError = Quaternion.Angle(neutralHand, hand.rotation);
                float wristRequest = Mathf.Max(Mathf.Abs(arm.wristFlexion), Mathf.Abs(arm.wristDeviation));
                report.maxRequestedWristDegrees = Mathf.Max(report.maxRequestedWristDegrees, wristRequest);
                if (wristRequest <= .0001f)
                    report.maxNeutralWristErrorDegrees = Mathf.Max(report.maxNeutralWristErrorDegrees, neutralWristError);
                report.maxElbowRequestErrorDegrees = Mathf.Max(report.maxElbowRequestErrorDegrees, elbowError);
                report.maxElevationRequestErrorDegrees = Mathf.Max(report.maxElevationRequestErrorDegrees, elevationError);
                float jump = 0f;
                for (int i = 0; i < bones.Length; i++)
                {
                    if (hasPrevious) jump = Mathf.Max(jump, Quaternion.Angle(previous[i], bones[i].rotation));
                    previous[i] = bones[i].rotation;
                }
                hasPrevious = true;
                report.maxFrameRotationDegrees = Mathf.Max(report.maxFrameRotationDegrees, jump);
                if (Mathf.Abs(seconds - 6f) < .0001f || Mathf.Abs(seconds - 14f) < .0001f ||
                    Mathf.Abs(seconds - 22f) < .0001f || Mathf.Abs(seconds - 30f) < .0001f)
                {
                    report.heldPoseSamples++;
                    report.minimumHeldForearmHeadwardDot = Mathf.Min(report.minimumHeldForearmHeadwardDot, headward);
                }
                string signature = JsonUtility.ToJson(pose);
                if (firstPoses.TryGetValue(signature, out MovePoseSnapshot first))
                {
                    report.repeatedPoseComparisons++;
                    for (int i = 0; i < bones.Length; i++)
                    {
                        report.maxRepeatedPosePositionMm = Mathf.Max(report.maxRepeatedPosePositionMm, Vector3.Distance(first.positions[i], bones[i].position) * 1000f);
                        report.maxRepeatedPoseRotationDegrees = Mathf.Max(report.maxRepeatedPoseRotationDegrees, Quaternion.Angle(first.rotations[i], bones[i].rotation));
                    }
                }
                else
                {
                    var snapshot = new MovePoseSnapshot { positions = new Vector3[bones.Length], rotations = new Quaternion[bones.Length] };
                    for (int i = 0; i < bones.Length; i++) { snapshot.positions[i] = bones[i].position; snapshot.rotations[i] = bones[i].rotation; }
                    firstPoses.Add(signature, snapshot);
                }
                string phase = AnatomicalMotionSequence.PhaseLabel(motion, seconds).Replace(',', ' ');
                csv.Append(frame); AppendMoveValues(csv, seconds); csv.Append(',').Append(phase).Append(",joint");
                AppendMoveValues(csv, arm.shoulderElevation, arm.elevationPlane, arm.humeralRotation, arm.elbowFlexion,
                    arm.forearmRotation, arm.wristFlexion, arm.wristDeviation, actualElevation, actualElbow, elbowError,
                    elevationError, neutralWristError, jump, headward);
                foreach (Transform bone in bones) { AppendMoveVector(csv, bone.position); AppendMoveQuaternion(csv, bone.rotation); }
                csv.Append('\n');
                report.sampledFrames++;

                if (nextStill < stillTimes.Length && seconds + .0001f >= stillTimes[nextStill])
                {
                    string prefix = nextStill.ToString("00", CultureInfo.InvariantCulture);
                    Vector3 center = humerus.position + outward * .15f + upAxis * .1f;
                    Vector3 cameraPosition = center + forward * 2.35f + outward * .2f;
                    lab.SetView(AnatomicalPatientLab.View.Skin);
                    CaptureMoveStudyStill(preview, root, Path.Combine(outDir, prefix + "_skin.png"), cameraPosition, center);
                    lab.SetView(AnatomicalPatientLab.View.Skeleton);
                    CaptureMoveStudyStill(preview, root, Path.Combine(outDir, prefix + "_bones.png"), cameraPosition, center);
                    stills.Append(frame); AppendMoveValues(stills, seconds);
                    stills.Append(',').Append(phase).Append(',').Append(prefix).Append("_skin.png,").Append(prefix).Append("_bones.png\n");
                    nextStill++;
                }
            }
            report.outputMatchesRequestedJoints = report.maxElbowRequestErrorDegrees <= .1f && report.maxElevationRequestErrorDegrees <= .1f;
            report.neutralWristPreserved = report.maxRequestedWristDegrees <= .0001f && report.maxNeutralWristErrorDegrees <= .1f;
            report.repeatedPosesDeterministic = report.repeatedPoseComparisons > 0 && report.maxRepeatedPosePositionMm <= .01f && report.maxRepeatedPoseRotationDegrees <= .1f;
            report.heldForearmPointsHeadward = report.heldPoseSamples == 4 && report.minimumHeldForearmHeadwardDot > .5f;
            string summary = $"[해부학리그시험] {label} 공유 관절 경로: {report.sampledFrames} samples / {duration:F2}s. " +
                $"출력 팔꿈치/입력 최대 오차 {report.maxElbowRequestErrorDegrees:F5}°, 거상 {report.maxElevationRequestErrorDegrees:F5}°. " +
                $"손목 입력 최대 {report.maxRequestedWristDegrees:F5}°, 중립 손목 출력 오차 {report.maxNeutralWristErrorDegrees:F5}°. " +
                $"동일 입력 자세 반복 {report.repeatedPoseComparisons}건, 최대 위치 차 {report.maxRepeatedPosePositionMm:F6}mm / 회전 차 {report.maxRepeatedPoseRotationDegrees:F5}°. " +
                $"준비 자세 아래팔 머리 방향 최소 내적 {report.minimumHeldForearmHeadwardDot:F5} ({report.heldPoseSamples}회). " +
                $"프레임 간 회전 최대 {report.maxFrameRotationDegrees:F3}°.\n" + report.limitation;
            File.WriteAllText(Path.Combine(outDir, "frames.csv"), csv.ToString(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(outDir, "stills.csv"), stills.ToString(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(outDir, "report.json"), JsonUtility.ToJson(report, true), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(outDir, "summary.txt"), summary, new UTF8Encoding(false));
            Debug.Log(summary + "\n" + outDir);
            Require(report.outputMatchesRequestedJoints, "시연 출력 관절각이 공유 경로 입력과 일치하지 않는다: " + outDir);
            Require(report.neutralWristPreserved, "시연에서 중립 손목에 보상 꺾임이 생겼다: " + outDir);
            Require(report.repeatedPosesDeterministic, "동일 시연 입력 자세가 같은 출력 뼈 자세로 돌아오지 않는다: " + outDir);
            Require(report.heldForearmPointsHeadward, "준비 자세의 실제 아래팔이 머리 방향으로 향하지 않는다: " + outDir);
        }
        finally { lab.SetView(AnatomicalPatientLab.View.Skin); rig.Evaluate(rig.RestPose); }
    }

    private static void AppendMoveValues(StringBuilder csv, params float[] values)
    {
        foreach (float value in values) csv.Append(',').Append(value.ToString("G9", CultureInfo.InvariantCulture));
    }

    private static void AppendMoveVector(StringBuilder csv, Vector3 value) => AppendMoveValues(csv, value.x, value.y, value.z);
    private static void AppendMoveQuaternion(StringBuilder csv, Quaternion value) => AppendMoveValues(csv, value.x, value.y, value.z, value.w);

    private static void CaptureMoveStudyStill(PreviewRenderUtility preview, GameObject root, string path, Vector3 position, Vector3 target)
    {
        // CPU snapshots avoid the measured one-pose lag of native skinned preview inside one Editor tick.
        var snapshots = new List<GameObject>();
        var meshes = new List<Mesh>();
        var hidden = new List<SkinnedMeshRenderer>();
        Texture2D image = null;
        try
        {
            foreach (SkinnedMeshRenderer source in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!source.enabled || !source.gameObject.activeInHierarchy || source.forceRenderingOff) continue;
                var mesh = new Mesh(); meshes.Add(mesh); source.BakeMesh(mesh);
                var snapshot = new GameObject("MovementStudySnapshot"); snapshots.Add(snapshot);
                snapshot.transform.SetParent(source.transform, false); snapshot.layer = source.gameObject.layer;
                snapshot.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = snapshot.AddComponent<MeshRenderer>(); renderer.sharedMaterials = source.sharedMaterials;
                source.enabled = false; hidden.Add(source);
            }
            preview.camera.transform.position = position; preview.camera.transform.LookAt(target);
            preview.BeginStaticPreview(new Rect(0f, 0f, 640f, 640f)); preview.Render();
            image = preview.EndStaticPreview(); File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            if (image != null) UnityEngine.Object.DestroyImmediate(image);
            foreach (SkinnedMeshRenderer source in hidden) if (source != null) source.enabled = true;
            foreach (GameObject snapshot in snapshots) UnityEngine.Object.DestroyImmediate(snapshot);
            foreach (Mesh mesh in meshes) UnityEngine.Object.DestroyImmediate(mesh);
        }
    }

    /// <summary>PatientArmIkSim.PoseToPosition과 같은 정의 — abd = 내린 자리(−위)에서 바깥으로 · tilt = 앞으로(음수 = 뒤).</summary>
    private static Vector3 PoseDir(MvPose p, Vector3 U, Vector3 F, Vector3 L)
    {
        float a = p.abd * Mathf.Deg2Rad, b = p.tilt * Mathf.Deg2Rad;
        Vector3 d0 = -U * Mathf.Cos(a) + L * Mathf.Sin(a);
        return (d0 * Mathf.Cos(b) + F * Mathf.Sin(b)).normalized;
    }
}
