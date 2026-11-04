using UnityEngine;

/// <summary>
/// 다른 추나 술기(상부승모근 시안)에 ROM 각도기(<see cref="CervicalRomPlaneGauge"/>)를 띄우는 값 출처
/// (2026-10-30 사용자 «ROM 실습용 각도기 양식으로 다른 추나에도 각도랑 제한 영역 표시 · 양식 통일 · 상부승모근 시안 먼저 · 숫자도»).
///
/// ★각도는 <b>환자 리그에서 잰다</b>(종전 2D 각도 표시는 «애니메이션 진행률 × 90°» 명목값이었다).
///   축은 ROM 드라이버의 <see cref="CervicalRomDriver.WorldAxisFor"/>(ROM에서 부호 검증된 축) · 0°는 각도기와 같은 몸통 기준
///   (회전 = 몸통 앞 · 그 밖 = 몸통 위) · 머리의 «앞 · 위»는 바인드 자세(중립)에서 몸통 앞 · 위와 겹치는 방향이다.
///   각도기는 머리가 AngleAxis(θ, 축)으로 돈다고 보고 지침을 그리므로, 같은 축의 SignedAngle을 주면 지침과 머리가 정의상 맞는다.
/// ★적정 범위(유지 구간)는 평가기 <see cref="ChunaPathEvaluator.DisplayMidHoldStart"/>~<see cref="ChunaPathEvaluator.DisplayMidHoldEnd"/>
///   (환자 클립 시간 비율)다. 그 시점의 자세를 애니메이터로 한 번 평가해 각도로 바꾼다 — 단계(클립 · 범위)가 바뀔 때만.
///   방향(좌 · 우)도 클립 끝 자세에서 잰다(추론하지 않는다 — CLAUDE.md 규칙 9).
/// ★켜고 끄는 건 <see cref="ScenarioManager"/>가 ScenarioConfig.romStyleAngleGauge로 정한다(시나리오 이름을 코드에 박지 않는다).
///   켠 쪽이 끈다 — 각도기 출처 · 구간 모드는 여기서 끼우고 여기서 되돌린다.
/// ★런타임에 붙는다(c9 = ROM 드라이버가 있는 환자). 튜닝할 값이 없어 씬에 올리지 않는다.
/// </summary>
public class ChunaRomGaugeSource : MonoBehaviour, ICervicalRomGaugeSource
{
    private static ChunaRomGaugeSource instance;

    private CervicalRomDriver driver;
    private CervicalRomPlaneGauge gauge;
    private ChunaPathEvaluator evaluator;
    private Animator anim;
    private Transform head;
    private Vector3 headFwdLocal = Vector3.forward, headUpLocal = Vector3.up;   // 머리 로컬에서 본 «해부학 앞 · 위»
    private bool hasNeutral;

    private bool showing;
    private string preset = "";
    private CervicalRomDriver.Direction dir = CervicalRomDriver.Direction.None;
    private float bandStartDeg, bandEndDeg, maxDeg = 90f;
    private int sampledState, sampledMode = -1;
    private float sampledHoldStart = -1f, sampledHoldEnd = -1f;
    private ScenarioConfig config;   // 10-30 A안 — 적정 범위를 도(°)로 정했으면 그 값을 클립 시점으로 재서 평가기에 넣는다
    private int angleFrame = -1;
    private float angleCache;

    // ── 켜고 끄기 ──
    /// <summary>이 단계의 손 녹화 이름(프리셋 이름)으로 켠다 — 이름의 «회전» · «측굴» · «굴곡/신전»으로 면을 고른다(없으면 끝 자세에서 가장 크게 움직인 면).</summary>
    public static void Begin(string presetName, ScenarioConfig cfg = null)
    {
        if (instance == null)
        {
            var drv = FindFirstObjectByType<CervicalRomDriver>();
            if (drv == null) { Debug.LogWarning("[추나각도기] CervicalRomDriver가 없어 못 켠다"); return; }
            instance = drv.GetComponent<ChunaRomGaugeSource>();
            if (instance == null) instance = drv.gameObject.AddComponent<ChunaRomGaugeSource>();
            instance.driver = drv;
        }
        instance.config = cfg;
        instance.BeginInternal(presetName ?? "");
    }

    public static void End() { if (instance != null) instance.EndInternal(); }

    private void BeginInternal(string presetName)
    {
        if (gauge == null) gauge = FindFirstObjectByType<CervicalRomPlaneGauge>();
        if (evaluator == null) evaluator = FindFirstObjectByType<ChunaPathEvaluator>();
        if (anim == null) anim = driver.GetComponent<Animator>();
        if (head == null) head = FindDeep(driver.transform, "CC_Base_Head");
        if (!hasNeutral) ResolveNeutral();
        if (gauge == null || anim == null || head == null || driver.Torso == null)
        { Debug.LogWarning($"[추나각도기] 못 켠다 — 각도기 {gauge != null} · 애니메이터 {anim != null} · 머리 {head != null} · 몸통 {driver.Torso != null}"); return; }

        preset = presetName;
        dir = CervicalRomDriver.Direction.None;   // 다음 LateUpdate에서 클립 끝 자세로 정한다
        sampledState = 0; sampledMode = -1; sampledHoldStart = sampledHoldEnd = -1f;
        if (!showing)
        {
            showing = true;
            gauge.SetSource(this);
            gauge.SetChunaZones(true);
            CervicalRomPracticeReadout.ExternalSource = this;   // 머리 쪽 정보 표시(각도 · 유지 게이지)도 이 출처로
        }
    }

    private void EndInternal()
    {
        if (!showing) return;
        if (evaluator != null && DegreeMode) evaluator.ClearHoldRangeOverride();   // 우리가 넣은 것만 되돌린다
        showing = false;
        dir = CervicalRomDriver.Direction.None;
        if (gauge != null) { gauge.SetChunaZones(false); gauge.SetSource(null); }   // 우리가 끼운 것만 되돌린다
        if (ReferenceEquals(CervicalRomPracticeReadout.ExternalSource, this)) CervicalRomPracticeReadout.ExternalSource = null;
    }

    private void OnDisable() => EndInternal();
    private void OnDestroy() { EndInternal(); if (instance == this) instance = null; }

    // ── 단계마다 한 번: 방향 · 적정 범위 각도 ──
    private void LateUpdate()
    {
        if (!showing || anim == null || evaluator == null || anim.IsInTransition(0)) return;
        var st = anim.GetCurrentAnimatorStateInfo(0);
        float hs = evaluator.DisplayMidHoldStart, he = evaluator.DisplayMidHoldEnd;
        // 모드(스트레칭 · 재평가 · 회전)는 평가가 시작될 때(나레이션 뒤) 바뀐다 — 도 값을 고르는 기준이라 같이 본다
        int mode = (evaluator.IsStretchingMode ? 1 : 0) | (evaluator.IsExtendedLimitMode ? 2 : 0) | (evaluator.IsRotationStep ? 4 : 0);
        if (st.fullPathHash == sampledState && mode == sampledMode
            && Mathf.Abs(hs - sampledHoldStart) < 1e-4f && Mathf.Abs(he - sampledHoldEnd) < 1e-4f) return;
        sampledMode = mode;
        Sample(st, hs, he);
    }

    private bool DegreeMode => config != null && config.holdRangeInDegrees;

    /// <summary>지금 평가 모드에 맞는 설정 도(°) — 평가기가 비율을 고르는 규칙과 같다(스트레칭 › 재평가 위치 단계 › 그 밖).</summary>
    private void TargetDegrees(out float start, out float end, out string which)
    {
        if (evaluator.IsStretchingMode) { start = config.stretchingHoldStartDeg; end = config.stretchingHoldEndDeg; which = "스트레칭"; }
        else if (evaluator.IsExtendedLimitMode && !evaluator.IsRotationStep) { start = config.extendedHoldStartDeg; end = config.extendedHoldEndDeg; which = "재평가"; }
        else { start = config.midHoldStartDeg; end = config.midHoldEndDeg; which = "기본"; }
    }

    /// <summary>본 각도가 처음 deg에 닿는 클립 시점 — from~1을 고르게 재서 사이를 보간한다. 못 닿으면 1(클립 끝).</summary>
    private float TimeAtDegrees(int stateHash, float from, float deg)
    {
        const int N = 40;
        float prevT = from, prevA = float.NegativeInfinity;
        for (int i = 0; i <= N; i++)
        {
            float t = Mathf.Lerp(from, 1f, i / (float)N);
            PoseAt(stateHash, t);
            float a = Measure(dir);
            if (a >= deg)
            {
                if (i == 0 || float.IsNegativeInfinity(prevA) || a - prevA < 1e-4f) return t;
                return Mathf.Lerp(prevT, t, (deg - prevA) / (a - prevA));
            }
            prevT = t; prevA = a;
        }
        return 1f;
    }

    /// <summary>클립의 끝 · 적정 시작 · 적정 끝 자세를 애니메이터로 한 번씩 평가해 잰 뒤 지금 시점으로 되돌린다.</summary>
    private void Sample(AnimatorStateInfo st, float hs, float he)
    {
        sampledState = st.fullPathHash;
        float now = Mathf.Clamp01(st.normalizedTime);
        float speed = anim.speed;

        PoseAt(st.fullPathHash, 1f);
        dir = ChooseDirection(out float endDeg);
        string degNote = "";
        if (DegreeMode)
        {
            // A안: 설정 도(°) → 본이 그 각도가 되는 클립 시점 → 평가기(판정 · 경고음 · 표시가 모두 읽는 값)
            TargetDegrees(out float d0, out float d1, out string which);
            float from = evaluator.IsStretchingMode ? evaluator.StretchingGuideStart : 0f;   // 스트레칭은 시작 오프셋 앞을 안 쓴다
            float t0 = TimeAtDegrees(st.fullPathHash, from, d0), t1 = TimeAtDegrees(st.fullPathHash, from, d1);
            evaluator.SetHoldRangeOverride(t0, t1);
            hs = t0; he = t1;
            degNote = $" · ★본 각도 설정({which}) {d0:F0}~{d1:F0}° → 클립 시간 {t0:F2}~{t1:F2}" +
                      (d1 > endDeg + 0.5f ? $" · ★클립 끝이 {endDeg:F1}°라 {d1:F0}°에 못 간다(끝으로 잡음)" : "");
        }
        PoseAt(st.fullPathHash, hs); bandStartDeg = Mathf.Max(0f, Measure(dir));
        PoseAt(st.fullPathHash, he); bandEndDeg = Mathf.Max(bandStartDeg, Measure(dir));
        PoseAt(st.fullPathHash, now);   // 되돌림
        anim.speed = speed;
        sampledHoldStart = evaluator.DisplayMidHoldStart; sampledHoldEnd = evaluator.DisplayMidHoldEnd;   // 넣은 값으로 — 다음 프레임에 또 재지 않게

        float clinical = driver.MaxAngleFor(dir);
        maxDeg = Mathf.Ceil(Mathf.Max(clinical, bandEndDeg + 5f, endDeg + 5f) / 5f) * 5f;
        angleFrame = -1;
        Debug.Log($"[추나각도기] '{preset}' → {dir} · 적정 {bandStartDeg:F1}~{bandEndDeg:F1}° (클립 시간 {hs:F2}~{he:F2}) · 클립 끝 {endDeg:F1}° · 눈금 최대 {maxDeg:F0}° · 지금 {Measure(dir):F1}°{degNote}");
    }

    private void PoseAt(int stateHash, float t)
    {
        anim.Play(stateHash, 0, t);
        anim.Update(0f);
    }

    private CervicalRomDriver.Direction ChooseDirection(out float endDeg)
    {
        CervicalRomDriver.Direction right;
        if (preset.Contains("회전")) right = CervicalRomDriver.Direction.RotationRight;
        else if (preset.Contains("측굴")) right = CervicalRomDriver.Direction.LateralRight;
        else if (preset.Contains("굴곡") || preset.Contains("신전")) right = CervicalRomDriver.Direction.Flexion;
        else
        {
            // 이름에 없으면 끝 자세에서 가장 크게 움직인 면
            float r = Mathf.Abs(Measure(CervicalRomDriver.Direction.RotationRight));
            float l = Mathf.Abs(Measure(CervicalRomDriver.Direction.LateralRight));
            float s = Mathf.Abs(Measure(CervicalRomDriver.Direction.Flexion));
            right = r >= l && r >= s ? CervicalRomDriver.Direction.RotationRight : l >= s ? CervicalRomDriver.Direction.LateralRight : CervicalRomDriver.Direction.Flexion;
        }
        float a = Measure(right);
        var d = a >= 0f ? right : OppositeOf(right);
        endDeg = Measure(d);
        return d;
    }

    // ── 재기 ──
    /// <summary>방향 d 축으로 머리가 중립에서 돈 각(도) — 양수 = d 쪽. 각도기 지침과 같은 정의.</summary>
    private float Measure(CervicalRomDriver.Direction d)
    {
        Transform torso = driver.Torso;
        Vector3 axis = driver.WorldAxisFor(d);
        if (torso == null || head == null || axis.sqrMagnitude < 1e-8f) return 0f;
        axis.Normalize();
        bool rot = d == CervicalRomDriver.Direction.RotationLeft || d == CervicalRomDriver.Direction.RotationRight;
        Vector3 zero = Vector3.ProjectOnPlane(rot ? torso.forward : torso.up, axis);
        Vector3 now = Vector3.ProjectOnPlane(head.rotation * (rot ? headFwdLocal : headUpLocal), axis);
        if (zero.sqrMagnitude < 1e-8f || now.sqrMagnitude < 1e-8f) return 0f;
        return Vector3.SignedAngle(zero, now, axis);
    }

    /// <summary>
    /// 중립 = 바인드 자세에서 머리와 몸통의 관계. 그때 몸통의 앞 · 위와 겹치는 머리 로컬 방향을 «머리 앞 · 위»로 삼는다
    /// (뼈 로컬 축 이름을 믿지 않는다 — 리그마다 다르다). 바인드를 못 찾으면 지금 자세를 중립으로 삼고 경고한다.
    /// </summary>
    private void ResolveNeutral()
    {
        Transform torso = driver.Torso;
        foreach (var smr in driver.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var bones = smr.bones; var binds = smr.sharedMesh != null ? smr.sharedMesh.bindposes : null;
            if (bones == null || binds == null) continue;
            int hi = System.Array.IndexOf(bones, head), ti = System.Array.IndexOf(bones, torso);
            if (hi < 0 || ti < 0 || hi >= binds.Length || ti >= binds.Length) continue;
            Quaternion headBind = binds[hi].inverse.rotation, torsoBind = binds[ti].inverse.rotation;
            Quaternion headFromTorso = Quaternion.Inverse(headBind) * torsoBind;
            headFwdLocal = headFromTorso * Vector3.forward;
            headUpLocal = headFromTorso * Vector3.up;
            hasNeutral = true;
            Debug.Log($"[추나각도기] 중립 = 바인드 자세({smr.name})");
            return;
        }
        if (head != null && torso != null)
        {
            Quaternion headFromTorso = Quaternion.Inverse(head.rotation) * torso.rotation;
            headFwdLocal = headFromTorso * Vector3.forward;
            headUpLocal = headFromTorso * Vector3.up;
            hasNeutral = true;
            Debug.LogWarning("[추나각도기] 바인드 자세를 못 찾아 지금 자세를 중립으로 삼는다");
        }
    }

    // ── ICervicalRomGaugeSource ──
    public CervicalRomDriver.Direction CurrentDirection => showing ? dir : CervicalRomDriver.Direction.None;
    public Transform Pivot => driver != null ? driver.Pivot : null;
    public Transform Torso => driver != null ? driver.Torso : null;
    public Vector3 CurrentWorldAxis => driver != null ? driver.WorldAxisFor(dir) : Vector3.zero;
    public Vector3 WorldAxisFor(CervicalRomDriver.Direction d) => driver != null ? driver.WorldAxisFor(d) : Vector3.zero;
    public float MaxAngle => maxDeg;
    public float MaxAngleFor(CervicalRomDriver.Direction d) => maxDeg;
    public float CurrentAngle
    {
        get
        {
            if (angleFrame != Time.frameCount) { angleFrame = Time.frameCount; angleCache = Mathf.Clamp(Measure(dir), 0f, maxDeg); }
            return angleCache;
        }
    }
    public float ActiveTargetAngle => bandStartDeg;     // 각도기 추나 구간: 적정 범위 시작
    public float PassiveLimitAngle => bandEndDeg;       // 각도기 추나 구간: 적정 범위 끝 = 한계
    public float NominalDysfunction => 0f;
    public float NominalPassiveGain => 0f;
    public CervicalRomDriver.Measurement GetMeasurement(CervicalRomDriver.Direction d) => default;   // 도달 기록 없음

    private static CervicalRomDriver.Direction OppositeOf(CervicalRomDriver.Direction d)
    {
        switch (d)
        {
            case CervicalRomDriver.Direction.RotationRight: return CervicalRomDriver.Direction.RotationLeft;
            case CervicalRomDriver.Direction.RotationLeft: return CervicalRomDriver.Direction.RotationRight;
            case CervicalRomDriver.Direction.LateralRight: return CervicalRomDriver.Direction.LateralLeft;
            case CervicalRomDriver.Direction.LateralLeft: return CervicalRomDriver.Direction.LateralRight;
            case CervicalRomDriver.Direction.Flexion: return CervicalRomDriver.Direction.Extension;
            case CervicalRomDriver.Direction.Extension: return CervicalRomDriver.Direction.Flexion;
            default: return CervicalRomDriver.Direction.None;
        }
    }

    private static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var f = FindDeep(c, name); if (f != null) return f; }
        return null;
    }
}
