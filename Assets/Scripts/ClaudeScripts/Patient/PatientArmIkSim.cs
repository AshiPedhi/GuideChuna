// ★시험 전용 · 2026-10-22
//   PatientArmIkProbe의 목표 구를 정해진 경로로 자동으로 움직이며, 팔이 사람처럼 · 관절이 되는 범위로 움직이는지 잰다.
//   사용자 지시(10-22): «영상에서 했던 동작(제2늑골 교정기법)을 포함해 다양하게 움직여 보고, 사람처럼 움직이는지 ·
//   실제로 되는 관절 움직임인지 점검».
//   붙이는 법: Play 중 메뉴 GuideChuna/시험/환자 팔 IK 시뮬레이션 (PatientArmIkProbeMenu). 씬 · 프리팹에 올리지 않는다.
//   결과: Editor.log «[팔IK시뮬]» 단계별 요약 + Temp/ik_sim/<시각>_<팔>/ 에 단계마다 캡처 3장(위 · 옆 · 발쪽)과 프레임별 CSV.
//
// ★경로 각도는 전문가 영상(3인칭 40~64초 · 바디캠 18:53:43~18:54:15)을 눈으로 보고 잡은 «추정»이다.
// ★잴 때 쓰는 해부학 방향(몸 축 · 팔꿈치 굴곡 방향 · 손등 쪽)은 전부 probe가 뼈 위치 · 아바타 근육으로 잰 것을 받아 쓴다
//   — 여기서 따로 계산하지 않는다(CLAUDE.md 규칙 9: 미리보기 · 시험은 실제와 같은 값을 타야 한다).
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

[DefaultExecutionOrder(10001)] // probe(10000)가 푼 뒤에 잰다
public class PatientArmIkSim : MonoBehaviour
{
    [SerializeField] private PatientArmIkProbe probe;

    [Tooltip("단계마다 캡처를 남긴다(위 · 옆 · 발쪽).")]
    [SerializeField] private bool capture = true;

    // 목표 자세 — 몸 축 기준. abd = 팔을 내린 자리(−위)에서 바깥(L)으로 벌린 각(음수 = 몸 안쪽),
    // tilt = 그 방향에서 앞(F, 누운 환자는 천장)으로 든 각(음수 = 바닥 쪽 = 신전), reach = 팔 길이 배수.
    private enum Mode { Dir, Torso, BedPress, Initial }
    private struct Pose
    {
        public Mode mode; public float abd, tilt, reach, twist, wrist;
        public bool traction;   // 손으로 잡은 것처럼 견인(팔을 편 채 · 10-22)
        public static Pose D(float abd, float tilt, float reach = 1f, float twist = 0f, float wrist = 0f)
            => new Pose { mode = Mode.Dir, abd = abd, tilt = tilt, reach = reach, twist = twist, wrist = wrist };
    }
    private struct Phase { public string name, expect; public float dur, hold; public Pose to; }

    private readonly List<Phase> phases = new List<Phase>();
    private int phaseIdx = -1;
    private float phaseT;
    private Vector3 fromPos; private float fromTwist, fromWrist;
    private Quaternion rot0; private Vector3 initPos;
    private bool started, finished;

    // 단계 통계
    private float mHandTarget, mPen, mJump, mWrongElbow, mWristDors, mWristPalm;
    private int nFrames, nWrong, nSideways, nJump, nPen;
    private string mPenWhere = "";
    private readonly HashSet<string> hits = new HashSet<string>();
    private Quaternion pU, pL, pH; private bool hasPrev;

    private string outDir;
    private readonly StringBuilder csv = new StringBuilder();
    private readonly StringBuilder summary = new StringBuilder();
    private Camera cam; private RenderTexture rt; private Texture2D tex;

    public void Init(PatientArmIkProbe p) { probe = p; }

    private void BuildPhases()
    {
        void Add(string name, float dur, float hold, Pose to, string expect)
            => phases.Add(new Phase { name = name, dur = dur, hold = hold, to = to, expect = expect });

        Add("시작 자세", 0.3f, 0.5f, new Pose { mode = Mode.Initial, reach = 1f }, "애니 자세 그대로");
        Add("옆으로 90° (침대 높이)", 2f, 0.8f, Pose.D(90, 0), "팔 펴짐 · 한계 없음");
        Add("모서리 아래로 20°", 1.5f, 0.8f, Pose.D(90, -20), "침대 모서리 밖이면 막힘 없음");
        Add("모서리 아래로 40° (영상 자세)", 1.5f, 0.8f, Pose.D(90, -40), "어깨 신전 40° · 한계 안");
        Add("견인 — 더 당기기", 1f, 0.8f, Pose.D(90, -40, 1.12f), "팔 쭉 폄 · 손이 목표를 못 따라감(정상)");
        Add("비틀기 +60°", 1.5f, 0.8f, Pose.D(90, -40, 1.0f, 60), "위팔 회전 — 외·내 중 하나");
        Add("비틀기 −60°", 2f, 0.8f, Pose.D(90, -40, 1.0f, -60), "반대 회전");
        Add("비틀기 −120° (한계 넘김)", 1.5f, 0.8f, Pose.D(90, -40, 1.0f, -120), "외·내회전 한계에서 멈춤");
        Add("손목 +50°", 1.5f, 0.8f, Pose.D(90, -40, 1.0f, 0, 50), "손목 꺾임 50 — 배굴 · 장굴 중 하나");
        Add("손목 −50°", 2f, 0.8f, Pose.D(90, -40, 1.0f, 0, -50), "반대로 꺾임");
        Add("손목 +100° (한계 넘김)", 1.5f, 0.8f, Pose.D(90, -40, 1.0f, 0, 100), "손목 70°에서 멈춤");
        Add("아래로 80° (신전 한계 넘김)", 1.5f, 0.8f, Pose.D(90, -80), "신전 50°에서 멈춤");
        Add("앞으로 90° (천장)", 2f, 0.8f, Pose.D(0, 90), "굴곡 90 · 한계 없음");
        Add("머리 위로 170°", 2f, 0.8f, Pose.D(170, 0), "외전 170 · 한계 없음");
        Add("몸 앞을 가로질러 (내전)", 2f, 0.8f, Pose.D(-45, 60), "내전 30° 근처에서 멈춤 · 몸통 안 뚫음");
        Add("팔꿈치 굽힘 (손을 어깨 쪽으로)", 1.5f, 0.8f, Pose.D(90, 30, 0.45f), "팔꿈치가 경첩 방향으로만 굽음");
        Add("몸통 안으로 밀기", 1.5f, 0.8f, new Pose { mode = Mode.Torso, reach = 1f }, "몸통 표면에서 막힘");
        Add("침대 속으로 누르기", 1.5f, 0.8f, new Pose { mode = Mode.BedPress, reach = 1f }, "침대 윗면에서 막힘");
        Add("빠르게 휘두르기 (0.5초에 옆 → 머리 위)", 0.5f, 0.8f, Pose.D(170, 0), "튐 없음");
        // 10-22: 손으로 잡은 상태(견인) — 사용자 실기에서 내 손이 어깨에서 0.21~0.41m라 팔꿈치가 굽고 외회전이 안 됐다
        Pose tA = Pose.D(90, -40, 0.6f); tA.traction = true;
        Pose tB = Pose.D(90, -40, 0.6f, 80); tB.traction = true;
        Pose tC = Pose.D(90, -40, 0.6f, -80); tC.traction = true;
        Pose tD = Pose.D(90, -40, 0.6f, 0, 50); tD.traction = true;
        Add("견인 — 손을 어깨 쪽(0.6배)에 둔 영상 자세", 2f, 0.8f, tA, "팔꿈치 편 채 · 어깨 뒤로 40°");
        Add("견인 + 비틀기 +80°", 1.5f, 0.8f, tB, "팔 편 채 위팔 회전(외 · 내 중 하나)");
        Add("견인 + 비틀기 −80°", 2f, 0.8f, tC, "반대 회전");
        Add("견인 + 손목 +50°", 1.5f, 0.8f, tD, "손목 꺾임 · 팔 편 채");
        Add("원위치", 2f, 0.5f, new Pose { mode = Mode.Initial, reach = 1f }, "애니 자세로 돌아옴");
    }

    private void Update()
    {
        if (finished || probe == null || !probe.IsReady || probe.Target == null) return;
        if (!started) Begin();
        if (!probe.TryBodyAxesWorld(out Vector3 F, out Vector3 U, out Vector3 L)) return;

        Phase ph = phases[phaseIdx];
        probe.Traction = ph.to.traction;
        phaseT += Time.deltaTime;
        float k = ph.dur > 0f ? Mathf.Clamp01(phaseT / ph.dur) : 1f;
        k = k * k * (3f - 2f * k);

        Vector3 S = probe.UpperBone.position;
        Vector3 toPos = PoseToPosition(ph.to, S, F, U, L);
        Vector3 pos = Vector3.Lerp(fromPos, toPos, k);
        float twist = Mathf.Lerp(fromTwist, ph.to.twist, k);
        float wrist = Mathf.Lerp(fromWrist, ph.to.wrist, k);

        Transform t = probe.Target;
        t.position = pos;
        Vector3 armDir = (pos - S).sqrMagnitude > 1e-6f ? (pos - S).normalized : -U;
        Vector3 wristAxis = Vector3.Cross(armDir, F);   // 월드 외적 — 월드엔 거울이 없다. 부호는 결과를 손등 쪽 기준으로 재서 판정한다
        if (wristAxis.sqrMagnitude < 1e-6f) wristAxis = Vector3.Cross(armDir, U);
        t.rotation = Quaternion.AngleAxis(twist, armDir) * Quaternion.AngleAxis(wrist, wristAxis.normalized) * rot0;
    }

    private Vector3 PoseToPosition(Pose p, Vector3 S, Vector3 F, Vector3 U, Vector3 L)
    {
        float len = Vector3.Distance(probe.UpperBone.position, probe.LowerBone.position)
                  + Vector3.Distance(probe.LowerBone.position, probe.HandBone.position);
        switch (p.mode)
        {
            case Mode.Initial: return initPos;
            case Mode.Torso: return S - L * 0.25f + F * 0.02f - U * 0.15f;   // 가슴 안쪽(어깨에서 몸 중심 쪽)
            case Mode.BedPress: return S + L * 0.15f - F * 0.30f - U * 0.35f; // 몸 옆 침대 속
        }
        float a = p.abd * Mathf.Deg2Rad, b = p.tilt * Mathf.Deg2Rad;
        Vector3 d0 = -U * Mathf.Cos(a) + L * Mathf.Sin(a);
        Vector3 dir = d0 * Mathf.Cos(b) + F * Mathf.Sin(b);
        return S + dir.normalized * len * p.reach;
    }

    private void Begin()
    {
        started = true;
        BuildPhases();
        rot0 = probe.Target.rotation;
        initPos = probe.Target.position;
        string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "ik_sim", stamp + "_" + probe.SideLabel));
        Directory.CreateDirectory(outDir);
        csv.Append("phase,t,hand_target_mm,elbow_flex,sh_behind,sh_medial,sh_rot,twist,wrist_signed,pen_mm,jump_deg,hinge_off_deg,limits\n");
        if (capture) SetupCamera();
        Debug.Log($"[팔IK시뮬] {probe.SideLabel} 시작 — 단계 {phases.Count}개 · 결과 폴더 {outDir}");
        NextPhase();
    }

    private void NextPhase()
    {
        phaseIdx++;
        if (phaseIdx >= phases.Count) { Finish(); return; }
        phaseT = 0f;
        fromPos = probe.Target.position;
        if (phaseIdx > 0) { fromTwist = phases[phaseIdx - 1].to.twist; fromWrist = phases[phaseIdx - 1].to.wrist; }
        mHandTarget = mPen = mJump = mWrongElbow = mWristDors = mWristPalm = 0f;
        nFrames = nWrong = nSideways = nJump = nPen = 0;
        mPenWhere = "";
        hits.Clear();
    }

    private void LateUpdate()
    {
        if (!started || finished) return;
        Phase ph = phases[phaseIdx];
        Transform up = probe.UpperBone, lo = probe.LowerBone, ha = probe.HandBone;

        // ── 재기 ──
        nFrames++;
        float handTarget = Vector3.Distance(ha.position, probe.Target.position) * 1000f;
        mHandTarget = Mathf.Max(mHandTarget, handTarget);

        Vector3 uW = lo.position - up.position, fW = ha.position - lo.position;
        float elbowFlex = Vector3.Angle(uW, fW);
        float hingeOff = 0f;
        if (elbowFlex > 10f && probe.TryFlexDirWorld(out Vector3 flexDir))
        {
            Vector3 perp = fW - Vector3.Dot(fW, uW.normalized) * uW.normalized;
            hingeOff = Vector3.Angle(perp, flexDir - Vector3.Dot(flexDir, uW.normalized) * uW.normalized);
            if (hingeOff > 90f) nWrong++;          // 팔꿈치가 반대로(과신전 쪽) 꺾임
            else if (hingeOff > 30f) nSideways++;  // 경첩 면을 벗어나 옆으로 꺾임
            mWrongElbow = Mathf.Max(mWrongElbow, hingeOff);
        }

        float wristSigned = 0f;
        if (probe.FingerBone != null)
        {
            Vector3 fing = probe.FingerBone.position - ha.position;
            float bend = Vector3.Angle(fW, fing);
            if (probe.TryDorsalDirWorld(out Vector3 dors))
                wristSigned = Vector3.Dot(fing - Vector3.Dot(fing, fW.normalized) * fW.normalized, dors) >= 0f ? bend : -bend;
            else wristSigned = bend;
            mWristDors = Mathf.Max(mWristDors, wristSigned);
            mWristPalm = Mathf.Min(mWristPalm, wristSigned);
        }

        float pen = 0f; string where = "";
        float r = probe.ArmRadius * 0.75f;   // 겉면 여유 — 팔 굵기 절반 이상 들어가면 «뚫음»으로 센다
        // 위팔은 어깨 관절 쪽 1/4을 뺀다 — 관절 자리는 원래 몸통에 붙어 있다(10-22 1회차에서 오탐)
        for (int i = 1; i <= 4; i++)
        {
            float s = i / 4f;
            float d1 = probe.PenetrationDepth(Vector3.Lerp(up.position, lo.position, s), r, out string w1);
            if (d1 > pen) { pen = d1; where = "위팔·" + w1; }
        }
        for (int i = 0; i <= 4; i++)
        {
            float s = i / 4f;
            float d2 = probe.PenetrationDepth(Vector3.Lerp(lo.position, ha.position, s), r, out string w2);
            if (d2 > pen) { pen = d2; where = "아래팔·" + w2; }
        }
        if (pen > 0.005f) nPen++;
        if (pen > mPen) { mPen = pen; mPenWhere = where; }

        float jump = 0f;
        if (hasPrev)
            jump = Mathf.Max(Quaternion.Angle(pU, up.localRotation), Mathf.Max(Quaternion.Angle(pL, lo.localRotation), Quaternion.Angle(pH, ha.localRotation)));
        pU = up.localRotation; pL = lo.localRotation; pH = ha.localRotation; hasPrev = true;
        float expected = 360f * Time.deltaTime;   // 초당 360°보다 빠르면 «튐»(사람 팔이 그렇게 돌지 않는다)
        if (jump > Mathf.Max(8f, expected)) nJump++;
        mJump = Mathf.Max(mJump, jump);

        string lim = probe.LimitHit;
        if (!string.IsNullOrEmpty(lim)) foreach (string h in lim.Split('·')) hits.Add(h.Trim());

        csv.Append(phaseIdx).Append(',').Append(phaseT.ToString("F2")).Append(',').Append(handTarget.ToString("F0")).Append(',')
           .Append(elbowFlex.ToString("F0")).Append(',').Append(probe.ShoulderBehindDeg.ToString("F0")).Append(',')
           .Append(probe.ShoulderMedialDeg.ToString("F0")).Append(',').Append(probe.ShoulderRotDeg.ToString("F0")).Append(',')
           .Append(probe.TwistDeg.ToString("F0")).Append(',').Append(wristSigned.ToString("F0")).Append(',')
           .Append((pen * 1000f).ToString("F0")).Append(',').Append(jump.ToString("F1")).Append(',')
           .Append(hingeOff.ToString("F0")).Append(',').Append(lim.Replace(',', ' ')).Append('\n');

        // ── 단계 끝 ──
        if (phaseT >= ph.dur + ph.hold)
        {
            string line = $"[팔IK시뮬] {probe.SideLabel} {phaseIdx:00} {ph.name} | 기대: {ph.expect} | " +
                $"끝 자세: 어깨 뒤로 {probe.ShoulderBehindDeg:F0}° · 안쪽 {probe.ShoulderMedialDeg:F0}° · 회전 {probe.ShoulderRotDeg:+0;-0}° · 팔꿈치 굴곡 {elbowFlex:F0}° · " +
                $"손목 {(wristSigned >= 0 ? "배굴" : "장굴")} {Mathf.Abs(wristSigned):F0}° | " +
                $"손-목표 최대 {mHandTarget:F0}mm · 관통 최대 {mPen * 1000f:F0}mm{(mPen > 0f ? "(" + mPenWhere + ")" : "")} {nPen}프레임 · " +
                $"팔꿈치 반대 꺾임 {nWrong} · 옆 꺾임 {nSideways}프레임(경첩 벗어남 최대 {mWrongElbow:F0}°) · " +
                $"튐 {nJump}프레임(최대 {mJump:F1}°/프레임) · 손목 범위 배굴 {mWristDors:F0}° ~ 장굴 {-mWristPalm:F0}° · " +
                $"걸린 한계 {(hits.Count > 0 ? string.Join(" · ", hits) : "없음")} · {nFrames}프레임";
            Debug.Log(line);
            summary.Append(line).Append('\n');
            if (capture) CaptureViews(phaseIdx);
            NextPhase();
        }
    }

    private void Finish()
    {
        finished = true;
        try
        {
            File.WriteAllText(Path.Combine(outDir, "frames.csv"), csv.ToString(), new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(outDir, "summary.txt"), summary.ToString(), new UTF8Encoding(false));
        }
        catch (System.Exception e) { Debug.LogWarning("[팔IK시뮬] 결과 파일을 못 썼다 — " + e.Message); }
        Debug.Log($"[팔IK시뮬] {probe.SideLabel} 끝 — {outDir}");
        if (cam != null) Destroy(cam.gameObject);
        if (rt != null) { rt.Release(); Destroy(rt); }
        if (tex != null) Destroy(tex);
    }

    // ── 캡처: 시험용 카메라 하나를 세 자리로 옮겨 가며 RenderTexture에 그린다(헤드셋 · 게임 카메라와 무관) ──
    private void SetupCamera()
    {
        var go = new GameObject("[팔IK시뮬] 카메라");
        cam = go.AddComponent<Camera>();
        cam.enabled = false;
        cam.stereoTargetEye = StereoTargetEyeMask.None;
        cam.fieldOfView = 55f;
        cam.nearClipPlane = 0.02f;
        cam.farClipPlane = 20f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.32f, 0.34f, 0.38f);
        rt = new RenderTexture(800, 600, 24);
        cam.targetTexture = rt;
        tex = new Texture2D(800, 600, TextureFormat.RGB24, false);
    }

    private void CaptureViews(int idx)
    {
        if (cam == null || !probe.TryBodyAxesWorld(out Vector3 F, out Vector3 U, out Vector3 L)) return;
        Vector3 S = probe.UpperBone.position;
        Shot(idx, "1위", S + F * 1.5f + L * 0.45f - U * 0.05f, S + L * 0.45f - U * 0.05f, U);
        Shot(idx, "2옆", S + L * 1.7f - F * 0.05f - U * 0.1f, S + L * 0.35f - F * 0.15f, F);
        Shot(idx, "3발쪽", S - U * 1.6f + F * 0.55f + L * 0.4f, S + L * 0.35f - F * 0.1f, F);
    }

    private void Shot(int idx, string view, Vector3 pos, Vector3 look, Vector3 upDir)
    {
        cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos, upDir));
        cam.Render();
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        try { File.WriteAllBytes(Path.Combine(outDir, $"{idx:00}_{view}.png"), tex.EncodeToPNG()); }
        catch (System.Exception e) { Debug.LogWarning("[팔IK시뮬] 캡처를 못 썼다 — " + e.Message); }
    }
}
