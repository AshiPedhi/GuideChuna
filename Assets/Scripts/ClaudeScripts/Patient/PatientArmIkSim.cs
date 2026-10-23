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
        public Mode mode; public float abd, tilt, reach, twist, wrist, dev;   // dev = 손목 옆 꺾기(요측 · 척측 시험 · 10-22 명세)
        public bool traction;   // 손으로 잡은 것처럼 견인(팔을 편 채 · 10-22)
        public static Pose D(float abd, float tilt, float reach = 1f, float twist = 0f, float wrist = 0f)
            => new Pose { mode = Mode.Dir, abd = abd, tilt = tilt, reach = reach, twist = twist, wrist = wrist };
    }
    private struct Phase { public string name, expect; public float dur, hold; public Pose to; }

    private readonly List<Phase> phases = new List<Phase>();
    private int phaseIdx = -1;
    private float phaseT;
    private Vector3 fromPos; private float fromTwist, fromWrist, fromDev;
    private Quaternion rot0; private Vector3 initPos;
    private bool started, finished;

    // 단계 통계
    private float mHandTarget, mPen, mJump, mWrongElbow, mWristDors, mWristPalm;
    private int nFrames, nWrong, nSideways, nJump, nPen, nOver;
    private float mElbowFlex, mMedial, mBehind, mElev;
    private float lastTheta, lastPhi, lastCone;   // 이 프레임 실제 위팔 거상 · 방향 · 그 방향 경계
    private string overWhat = "";
    private string mPenWhere = "";
    private readonly HashSet<string> hits = new HashSet<string>();
    private Quaternion pU, pL, pH; private bool hasPrev;

    private string outDir;
    private readonly StringBuilder csv = new StringBuilder();
    private readonly StringBuilder summary = new StringBuilder();
    private Camera cam; private RenderTexture rt; private Texture2D tex;

    // ★10-23 사용자 지시(«제2늑골 동작 과정대로 시연해봐»): 시연 모드 — 전문가 영상 순서대로 움직이고 두 방향(발쪽 · 옆)을 매 프레임 찍는다.
    //   Time.captureFramerate = 30으로 고정해 프레임 = 1/30초(영상 속도가 실제와 같다). 끝나면 v_*.jpg를 ffmpeg로 묶는다(phases.txt = 단계 시작 프레임).
    private bool demo;
    private int videoFrame;
    private Vector3 cachedWristAxis, cachedDevAxis; private bool hasWristCache;   // 비틀기 0일 때 잰 손목 축(10-23)
    private RenderTexture vrt; private Texture2D vtex;
    private readonly StringBuilder phaseLog = new StringBuilder();
    private const int VideoW = 1280, VideoH = 720;

    public void Init(PatientArmIkProbe p, bool demoMode = false) { probe = p; demo = demoMode; }
    public PatientArmIkProbe Probe => probe;

    private void BuildPhases()
    {
        void Add(string name, float dur, float hold, Pose to, string expect)
            => phases.Add(new Phase { name = name, dur = dur, hold = hold, to = to, expect = expect });

        if (demo)
        {
            // 제2늑골 상방변위 교정 — 전문가 영상(3인칭 40~64초 · 바디캠 18:53:43~18:54:15)을 눈으로 보고 잡은 순서 · 각(★추정):
            //   앙와위 → 팔을 펴서 옆으로 → 침대 모서리 밖 아래로 → 악수하듯 잡고 당김(견인) → 손목 배굴 → 외회전 → 유지 → 풀기 → 원위치.
            //   다른 손으로 쇄골 아래 제2늑골을 누르는 것은 시술자 쪽이라 여기엔 없다. 외회전 부호: 오른팔은 비틀기 +가 외회전(10-22 시뮬 실측 — +80 → 외회전 −75).
            //   ★10-23 1차 시연: 비틀기 +70이 아래팔 회외 81°로 가고 어깨는 +1° → 사용자 지시로 «어깨 먼저»(probe.TwistShoulderFirst)를 켠다.
            Add("1 시작 - 앙와위, 팔 옆에", 0.5f, 1.0f, new Pose { mode = Mode.Initial, reach = 1f }, "애니 자세 그대로");
            Add("2 팔을 펴서 옆으로 벌림", 2.5f, 0.8f, Pose.D(90, 0), "팔 펴짐 · 침대 높이");
            Add("3 침대 모서리 밖 아래로", 2.0f, 0.8f, Pose.D(90, -40), "어깨 신전 40° 안팎");
            Pose g1 = Pose.D(90, -40, 1.05f); g1.traction = true;
            Add("4 악수하듯 잡고 당김(견인)", 1.5f, 1.0f, g1, "팔 쭉 폄");
            Pose g2 = Pose.D(90, -40, 1.05f, 0, 50); g2.traction = true;
            Add("5 손목 배굴", 2.0f, 1.0f, g2, "손목 손등 쪽으로");
            Pose g3 = Pose.D(90, -40, 1.05f, 70, 50); g3.traction = true;
            Add("6 어깨 외회전 (유지)", 2.5f, 3.0f, g3, "위팔 외회전 · 아래팔은 어깨 한계를 넘는 만큼만");
            Pose g4 = Pose.D(90, -40, 1.05f, 0, 0); g4.traction = true;
            Add("7 풀기 - 회전 · 손목 되돌림", 2.0f, 0.8f, g4, "중립으로");
            Add("8 팔을 침대 위로", 2.0f, 0.8f, Pose.D(90, 0), "침대 높이");
            Add("9 원위치", 2.5f, 1.0f, new Pose { mode = Mode.Initial, reach = 1f }, "애니 자세로");
            return;
        }

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
        Add("손목 +100° (한계 넘김)", 1.5f, 0.8f, Pose.D(90, -40, 1.0f, 0, 100), "배굴 70 또는 장굴 85에서 멈춤(±3)");
        Pose wd1 = Pose.D(90, -40, 1.0f); wd1.dev = 60f;
        Pose wd2 = Pose.D(90, -40, 1.0f); wd2.dev = -60f;
        Add("손목 옆 꺾기 +60° (한계 넘김)", 1.5f, 0.8f, wd1, "요측 20 또는 척측 35에서 멈춤(±3)");
        Add("손목 옆 꺾기 −60° (한계 넘김)", 2f, 0.8f, wd2, "반대 쪽 한계에서 멈춤(±3)");
        Add("아래로 80° (신전 한계 넘김)", 1.5f, 0.8f, Pose.D(90, -80), "수평에서 뒤로 45(수평 신전)에서 멈춤");
        Add("앞으로 90° (천장)", 2f, 0.8f, Pose.D(0, 90), "굴곡 90 · 한계 없음");
        Add("옆으로 머리 위 170°", 2f, 0.8f, Pose.D(170, 0), "옆 면 145~150에서 멈춤 · 쇄골 거상 ≤10");
        Add("앞으로 머리 위 170°", 2.5f, 0.8f, Pose.D(0, 170), "앞 면 155~170에서 멈춤");
        Add("머리 위에서 뒤로 (옆 150° · 뒤 30°)", 2f, 0.8f, Pose.D(150, -30), "머리 위(θ≥120)에선 관상면 뒤 ≤10(추정)");
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
        // 10-22 사용자 지적: 팔을 머리 위로 올리고 손을 돌리면 어깨가 아니라 아래팔이 돈다 — 아래팔 80°까지, 넘는 만큼 어깨
        Add("머리 위 + 손 비틀기 +60°", 2f, 0.8f, Pose.D(170, 0, 1f, 60), "아래팔만 60° · 어깨 회전 그대로");
        Add("머리 위 + 손 비틀기 +150°", 1.5f, 0.8f, Pose.D(170, 0, 1f, 150), "아래팔이 중립 기준 한계(회외 90 · 회내 85)에서 멈추고 넘는 만큼 어깨로 · 어깨는 그 거상의 한계 ±5");
        Add("머리 위 + 손 비틀기 −60°", 2f, 0.8f, Pose.D(170, 0, 1f, -60), "아래팔 반대로 60°");
        Add("원위치", 2f, 0.5f, new Pose { mode = Mode.Initial, reach = 1f }, "애니 자세로 돌아옴");
    }

    private void Update()
    {
        if (finished || probe == null || !probe.IsReady || probe.Target == null) return;
        if (!started) Begin();
        if (!probe.TryBodyAxesWorld(out Vector3 F, out Vector3 U, out Vector3 L)) return;

        if (phaseIdx < 0 || phaseIdx >= phases.Count) return;   // ★10-22: 단계 밖에서 매 프레임 예외가 났다(실행기가 두 번 붙었을 때)
        Phase ph = phases[phaseIdx];
        probe.Traction = ph.to.traction;
        phaseT += Time.deltaTime;
        float k = ph.dur > 0f ? Mathf.Clamp01(phaseT / ph.dur) : 1f;
        k = k * k * (3f - 2f * k);

        Vector3 S = probe.UpperBone.position;
        Vector3 toPos = PoseToPosition(ph.to, S, F, U, L);
        // ★10-22: 사람이 팔을 옮기듯 어깨를 중심으로 «호»를 그린다(직선으로 옮기면 손이 어깨 · 몸을 질러가는 길이 되어 시험이 아니라 경로 탓에 이상해졌다).
        //   몸통 · 침대로 누르는 단계(특수 자리)는 일부러 직선이다.
        Vector3 pos;
        if (ph.to.mode == Mode.Dir || ph.to.mode == Mode.Initial)
        {
            Vector3 a0 = fromPos - S, a1 = toPos - S;
            Vector3 dir = Vector3.Slerp(a0.normalized, a1.normalized, k);
            pos = S + dir * Mathf.Lerp(a0.magnitude, a1.magnitude, k);
        }
        else pos = Vector3.Lerp(fromPos, toPos, k);
        float twist = Mathf.Lerp(fromTwist, ph.to.twist, k);
        float wrist = Mathf.Lerp(fromWrist, ph.to.wrist, k);
        float dev = Mathf.Lerp(fromDev, ph.to.dev, k);

        Transform t = probe.Target;
        t.position = pos;
        Vector3 armDir = (pos - S).sqrMagnitude > 1e-6f ? (pos - S).normalized : -U;
        Vector3 wristAxis = Vector3.Cross(armDir, F);   // 월드 외적 — 월드엔 거울이 없다. 부호는 결과를 손등 쪽 기준으로 재서 판정한다
        if (wristAxis.sqrMagnitude < 1e-6f) wristAxis = Vector3.Cross(armDir, U);
        Vector3 devAxis = Vector3.ProjectOnPlane(F, armDir);
        if (devAxis.sqrMagnitude < 1e-6f) devAxis = Vector3.ProjectOnPlane(U, armDir);
        // ★10-22: 손목은 해부학 축 그대로 꺾는다 — 손가락(중립)을 손등 쪽(+wrist = 배굴) · 엄지 쪽(+dev = 요측)으로 미는 회전(월드 외적 — 월드엔 거울이 없다)
        //   (종전엔 팔 방향 · 몸 축으로 꺾어 배굴만 시켜도 편위가 섞여 손목 한계를 시험하지 못했다)
        // ★10-23 시연 2차: 어깨 먼저(TwistShoulderFirst)면 위팔이 돌며 손목 축(아래팔에 붙음)도 같이 돈다 — 돈 축으로 꺾고 비틀기를 또 얹으면
        //   비틀기가 두 번 들어가 배굴 50이 «배굴 19 · 척측 34»가 됐다. → 비틀기가 0일 때 잰 축을 쓴다(팔 방향이 그대로인 단계 안에서).
        if (Mathf.Abs(twist) < 0.5f && probe.TryWristAxesWorld(out Vector3 wn, out Vector3 wd, out Vector3 wr))
        {
            wristAxis = Vector3.Cross(wn, wd);
            devAxis = Vector3.Cross(wn, wr);
            cachedWristAxis = wristAxis; cachedDevAxis = devAxis; hasWristCache = true;
        }
        else if (hasWristCache) { wristAxis = cachedWristAxis; devAxis = cachedDevAxis; }
        t.rotation = Quaternion.AngleAxis(twist, armDir) * Quaternion.AngleAxis(wrist, wristAxis.normalized) * Quaternion.AngleAxis(dev, devAxis.normalized) * rot0;
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
        outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "ik_sim", stamp + "_" + probe.SideLabel + (demo ? "_제2늑골시연" : "")));
        Directory.CreateDirectory(outDir);
        csv.Append("phase,t,hand_target_mm,elbow_flex,sh_behind,sh_medial,sh_rot,twist,wrist_signed,pen_mm,jump_deg,hinge_off_deg,limits\n");
        if (capture || demo) SetupCamera();
        if (demo)
        {
            probe.TwistShoulderFirst = true;   // ★10-23 사용자 지시: 제2늑골의 외회전은 어깨(위팔)가 받는다
            Time.captureFramerate = 30;
            vrt = new RenderTexture(VideoW, VideoH, 24);
            vtex = new Texture2D(VideoW, VideoH, TextureFormat.RGB24, false);
        }
        Debug.Log($"[팔IK시뮬] {probe.SideLabel} 시작 — 단계 {phases.Count}개 · 결과 폴더 {outDir}");
        NextPhase();
    }

    private void NextPhase()
    {
        phaseIdx++;
        if (phaseIdx >= phases.Count) { Finish(); return; }
        phaseT = 0f;
        fromPos = probe.Target.position;
        if (demo) phaseLog.Append(videoFrame).Append('\t').Append(phases[phaseIdx].name).Append('\n');
        // 견인을 끄는 단계면 손을 놓을 때(PatientArmIkHandGrab.Release)처럼 목표를 지금 환자 손 자리로 — 남은 목표가 팔 길이보다 가까워 팔꿈치가 접히지 않게
        if (phaseIdx > 0 && phases[phaseIdx - 1].to.traction && !phases[phaseIdx].to.traction) { probe.Target.position = probe.HandBone.position; fromPos = probe.HandBone.position; }
        if (phaseIdx > 0) { fromTwist = phases[phaseIdx - 1].to.twist; fromWrist = phases[phaseIdx - 1].to.wrist; fromDev = phases[phaseIdx - 1].to.dev; }
        mHandTarget = mPen = mJump = mWrongElbow = mWristDors = mWristPalm = 0f;
        nFrames = nWrong = nSideways = nJump = nPen = nOver = 0;
        mElbowFlex = mMedial = mBehind = mElev = -999f; overWhat = "";
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
        // 한계 넘음(★10-22 7회차 신설 — 사용자가 화면으로 «말도 안 되는 각도»를 지적): 실제 자세에서 직접 잰다(probe 내부값 아님)
        mElbowFlex = Mathf.Max(mElbowFlex, elbowFlex);
        if (probe.TryBodyAxesWorld(out Vector3 bF, out Vector3 bU, out Vector3 bL))
        {
            Vector3 uN = uW.normalized;
            float behind = Mathf.Asin(Mathf.Clamp(Vector3.Dot(uN, -bF), -1f, 1f)) * Mathf.Rad2Deg;
            float medial = Mathf.Asin(Mathf.Clamp(Vector3.Dot(uN, -bL), -1f, 1f)) * Mathf.Rad2Deg;
            mBehind = Mathf.Max(mBehind, behind); mMedial = Mathf.Max(mMedial, medial); mElev = Mathf.Max(mElev, Vector3.Angle(uN, -bU));
            Vector3 hzN = uN - Vector3.Dot(uN, bU) * bU;
            float phiN = hzN.sqrMagnitude > 1e-8f ? Mathf.Atan2(Vector3.Dot(hzN.normalized, bL), Vector3.Dot(hzN.normalized, bF)) * Mathf.Rad2Deg : 0f;
            float coneN = probe.ConeMaxAt(phiN, elbowFlex);
            lastTheta = Vector3.Angle(uN, -bU); lastPhi = phiN; lastCone = coneN;
            string ov = Vector3.Angle(uN, -bU) > coneN + 3f ? "어깨" : elbowFlex > probe.ElbowFlexionMax + 3f ? "팔꿈치" : behind > probe.ShoulderExtensionMax + 3f ? "신전" : medial > 33f ? "내전" : "";
            if (ov.Length > 0) { nOver++; if (overWhat.IndexOf(ov, System.StringComparison.Ordinal) < 0) overWhat += (overWhat.Length > 0 ? "·" : "") + ov; }
        }
        // ★10-23: 운반각(10-22 명세 · 편 팔에서 아래팔이 바깥으로 12.9°)을 «옆 꺾임 90° · 반대 꺾임»으로 세고 있었다
        //   (편 팔은 굴곡 ≈ 운반각 13°라 «굴곡 > 10»을 넘고, 벌어진 방향이 경첩 면에 수직이라 90° 근처로 나왔다 — 62단계 거의 전부 ★).
        //   → 아래팔을 경첩 면 안(굴곡 · 음수 = 과신전)과 면 밖(옆)으로 나눠, 면 밖은 지금 운반각을 뺀 넘침만 센다.
        float hingeOff = 0f;
        if (probe.TryFlexDirWorld(out Vector3 flexDir))
        {
            Vector3 uN0 = uW.normalized;
            Vector3 fd = flexDir - Vector3.Dot(flexDir, uN0) * uN0;
            if (fd.sqrMagnitude > 1e-8f)
            {
                fd.Normalize();
                float ca = Vector3.Dot(fW, uN0), cb = Vector3.Dot(fW, fd);
                float cc = (fW - ca * uN0 - cb * fd).magnitude;
                float flexIn = Mathf.Atan2(cb, ca) * Mathf.Rad2Deg;                          // 경첩 면 안 굴곡(음수 = 과신전 쪽)
                float latDeg = Mathf.Atan2(cc, Mathf.Sqrt(ca * ca + cb * cb)) * Mathf.Rad2Deg; // 경첩 면 밖
                float latExcess = Mathf.Max(0f, latDeg - probe.CarryDeg);
                if (flexIn < -5f) nWrong++;              // 팔꿈치가 반대로(과신전 쪽) 꺾임
                else if (latExcess > 10f) nSideways++;   // 운반각보다 10° 넘게 옆으로 꺾임
                hingeOff = Mathf.Max(latExcess, -flexIn);
                mWrongElbow = Mathf.Max(mWrongElbow, hingeOff);
            }
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
        if (demo) CaptureVideoFrame();

        // ── 단계 끝 ──
        if (phaseT >= ph.dur + ph.hold)
        {
            string line = $"[팔IK시뮬] {probe.SideLabel} {phaseIdx:00} {ph.name} | 기대: {ph.expect} | " +
                $"끝 자세: 거상 {lastTheta:F0}° · 방향 φ {lastPhi:F0}°(경계 {lastCone:F0}°) · 어깨 뒤로 {probe.ShoulderBehindDeg:F0}° · 안쪽 {probe.ShoulderMedialDeg:F0}° · 회전 {probe.ShoulderRotDeg:+0;-0}° · 팔꿈치 굴곡 {elbowFlex:F0}° · " +
                $"손목 {(probe.WristExtDeg >= 0 ? "배굴" : "장굴")} {Mathf.Abs(probe.WristExtDeg):F0}° · {(probe.WristDevDeg >= 0 ? "요측" : "척측")} {Mathf.Abs(probe.WristDevDeg):F0}° · " +
                $"아래팔(중립 기준) {(probe.ForearmRotDeg >= 0 ? "회외" : "회내")} {Mathf.Abs(probe.ForearmRotDeg):F0}° · 운반각 {probe.CarryDeg:F0}° · 쇄골 거상 {probe.GirdleDeg:F0}° 후인 {probe.GirdleRetractDeg:F0}° | " +
                $"손-목표 최대 {mHandTarget:F0}mm · 관통 최대 {mPen * 1000f:F0}mm{(mPen > 0f ? "(" + mPenWhere + ")" : "")} {nPen}프레임 · " +
                $"팔꿈치 반대 꺾임 {nWrong} · 옆 꺾임 {nSideways}프레임(경첩 벗어남 최대 {mWrongElbow:F0}°) · " +
                $"튐 {nJump}프레임(최대 {mJump:F1}°/프레임) · 한계 넘음 {nOver}프레임{(nOver > 0 ? "(" + overWhat + ")" : "")} · 최대 팔꿈치 {mElbowFlex:F0}° · 신전 {mBehind:F0}° · 내전 {mMedial:F0}° · 거상 {mElev:F0}°(쇄골 {probe.GirdleDeg:F0}° · 방향 φ {probe.ConePhi:F0}° 경계 {probe.ConeMaxDeg:F0}°) · 손목 범위 배굴 {mWristDors:F0}° ~ 장굴 {-mWristPalm:F0}° · " +
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
            if (demo) File.WriteAllText(Path.Combine(outDir, "phases.txt"), phaseLog.ToString(), new UTF8Encoding(false));
        }
        catch (System.Exception e) { Debug.LogWarning("[팔IK시뮬] 결과 파일을 못 썼다 — " + e.Message); }
        Debug.Log($"[팔IK시뮬] {probe.SideLabel} 끝 — {outDir}");
        if (cam != null) Destroy(cam.gameObject);
        if (rt != null) { rt.Release(); Destroy(rt); }
        if (tex != null) Destroy(tex);
        if (demo)
        {
            Time.captureFramerate = 0;
            if (vrt != null) { vrt.Release(); Destroy(vrt); }
            if (vtex != null) Destroy(vtex);
            Debug.Log($"[팔IK시뮬] 시연 영상 프레임 {videoFrame}장(30fps) — {outDir}");
        }
    }

    /// <summary>시연 — 왼쪽 = 발쪽에서, 오른쪽 = 환자 옆에서 본 장면을 한 장(1280×720)에 그려 jpg로 남긴다.</summary>
    private void CaptureVideoFrame()
    {
        if (cam == null || vrt == null || !probe.TryBodyAxesWorld(out Vector3 F, out Vector3 U, out Vector3 L)) return;
        Vector3 S = probe.UpperBone.position;
        RenderTexture keep = cam.targetTexture;
        cam.targetTexture = vrt;
        cam.rect = new Rect(0f, 0f, 0.5f, 1f);
        Vector3 p1 = S - U * 1.6f + F * 0.55f + L * 0.4f, l1 = S + L * 0.35f - F * 0.1f;
        cam.transform.SetPositionAndRotation(p1, Quaternion.LookRotation(l1 - p1, F));
        cam.Render();
        cam.rect = new Rect(0.5f, 0f, 0.5f, 1f);
        Vector3 p2 = S + L * 1.7f - F * 0.05f - U * 0.1f, l2 = S + L * 0.35f - F * 0.15f;
        cam.transform.SetPositionAndRotation(p2, Quaternion.LookRotation(l2 - p2, F));
        cam.Render();
        cam.rect = new Rect(0f, 0f, 1f, 1f);
        cam.targetTexture = keep;
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = vrt;
        vtex.ReadPixels(new Rect(0, 0, VideoW, VideoH), 0, 0);
        vtex.Apply();
        RenderTexture.active = prev;
        try { File.WriteAllBytes(Path.Combine(outDir, $"v_{videoFrame:00000}.jpg"), vtex.EncodeToJPG(85)); }
        catch (System.Exception e) { Debug.LogWarning("[팔IK시뮬] 시연 프레임을 못 썼다 — " + e.Message); }
        videoFrame++;
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
