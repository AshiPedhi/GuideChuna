using UnityEngine;
using TMPro;

/// <summary>
/// 호흡 동기화 HUD + 윈도우 판정 (기능3)
/// - HUD는 World Space + 메인카메라 자식으로 배치(Quest 스테레오 깨짐 방지). 이 스크립트는 링 비주얼을 제어만 함.
/// - 윈도우 판정: 한 호흡 주기(들숨+날숨) 동안 적정 텐션 유지비율 ≥ requiredHoldRatio 이면 1회 호흡 성공.
///   requiredBreaths회 연속 성공 시 IsComplete. 텐션 상태는 tensionProvider로 외부(깊이 가이드)에서 주입.
///   ★실제 술기(영상 확인): 들숨/날숨과 무관하게 3회 호흡하는 "동안" 압력을 유지 → 위상이 아닌 전체 주기 유지비율로 판정.
/// </summary>
public class BreathingSyncHUD : MonoBehaviour
{
    [Header("=== 호흡 타이밍 (인스펙터 노출) ===")]
    [Tooltip("들숨 길이 (초). 실제 술기 = 들이마신 뒤 길게 내쉬므로 날숨보다 짧다.")]
    [SerializeField] private float breatheInDuration = 3f;
    [Tooltip("날숨 길이 (초). 이 구간 동안 굴곡을 진행시키며 압을 유지한다 — 들숨보다 길게.")]
    [SerializeField] private float breatheOutDuration = 7f;
    [Tooltip("필요한 연속 호흡 횟수. ★실제 술기는 1회(들이마신 뒤 길게 내쉬며 유지)다.")]
    [SerializeField] private int requiredBreaths = 1;
    [Tooltip("호흡 주기(들숨+날숨) 동안 적정 텐션 유지비율 임계")]
    [SerializeField, Range(0f, 1f)] private float requiredHoldRatio = 0.7f;

    /// <summary>들숨 길이(초). 진단 구간의 애니메이션 루프가 같은 리듬을 쓰도록 노출.</summary>
    public float BreatheInDuration => breatheInDuration;
    /// <summary>날숨 길이(초).</summary>
    public float BreatheOutDuration => breatheOutDuration;
    /// <summary>필요한 호흡 횟수.</summary>
    public int RequiredBreaths => requiredBreaths;

    /// <summary>★HUD는 씬에 1개뿐이고 여러 두개골 리그가 공유하므로, 술기마다 호흡이 다르면
    /// 리그가 호흡 윈도우를 열기 직전에 이 메서드로 자기 값을 밀어 넣는다.
    /// (OM = 3회 대칭 호흡 / PJ = 국면마다 다름 — 굴곡·외회전으로 잠근 채 1회 길게,
    /// 신전·내회전으로 전환해 3회이며 첫 회만 크게 = firstCycleScale)
    /// 인자가 0 이하면 그 항목은 기존 인스펙터 값을 유지한다(startPhase는 Keep이 유지).</summary>
    public void Configure(int breaths, float inhaleSeconds, float exhaleSeconds,
                          StartPhase startPhase = StartPhase.Keep, float firstCycleScaleValue = 0f)
    {
        if (breaths > 0) requiredBreaths = breaths;
        if (inhaleSeconds > 0f) breatheInDuration = inhaleSeconds;
        if (exhaleSeconds > 0f) breatheOutDuration = exhaleSeconds;
        if (startPhase != StartPhase.Keep) startWithExhale = (startPhase == StartPhase.Exhale);
        firstCycleScale = firstCycleScaleValue > 0f ? firstCycleScaleValue : 1f;
    }

    /// <summary>첫 호흡 주기의 들숨·날숨 길이 배수. PJ 교정처럼 "처음 한 번은 크게 들이쉬고
    /// 나머지는 평상 호흡"인 술기용. 1 = 전 주기 동일(기본).
    /// ★유지비율 미달로 카운트가 리셋되면 completedBreaths가 0으로 돌아가므로 '큰 호흡'부터 다시 시작한다.</summary>
    private float firstCycleScale = 1f;

    private float CurrentInhaleDuration =>
        Mathf.Max(0.0001f, breatheInDuration * (completedBreaths == 0 ? firstCycleScale : 1f));
    private float CurrentExhaleDuration =>
        Mathf.Max(0.0001f, breatheOutDuration * (completedBreaths == 0 ? firstCycleScale : 1f));

    /// <summary>호흡 윈도우를 어느 위상부터 시작할지. Keep = HUD 인스펙터 값 유지.</summary>
    public enum StartPhase { Keep = 0, Inhale = 1, Exhale = 2 }

    [Tooltip("★켜면 '날숨부터' 호흡 윈도우를 시작한다(한 주기 = 날숨 → 들숨). " +
             "주기 경계가 바뀌므로, 마지막 작업 국면이 날숨이면 켜야 그 국면이 주기 안에 들어온다. " +
             "현재 OM·PM·PJ는 모두 '들이마신 뒤 내쉰다'라 꺼 둔다(기본).")]
    [SerializeField] private bool startWithExhale = false;

    [Header("=== 표시 ===")]
    [Tooltip("★기본 ON. 호흡 링과 카운트('호흡 1/3')를 표시한다. " +
             "숨소리만으로는 몇 번째 호흡인지·언제 완료인지 판단이 안 된다는 실테스트 피드백(07-30)으로 기본값을 ON으로 되돌렸다. " +
             "끄면 화면에 아무것도 안 띄우고 호흡 타이밍·숨소리·애니메이션 동기화만 동작한다.")]
    [SerializeField] private bool showVisuals = true;

    [Header("=== 원형 게이지 (권장) ===")]
    [Tooltip("★들숨에 차오르고 날숨에 비워지는 원형 게이지. Image의 Image Type을 Filled / Radial 360으로 두고 연결한다.\n" +
             "배선하면 아래 '링 비주얼'(커졌다 작아졌다 하는 스케일 방식)은 무시된다.")]
    [SerializeField] private UnityEngine.UI.Image breathGauge;
    [Tooltip("들숨 구간 게이지 색")]
    [SerializeField] private Color inhaleGaugeColor = new Color(0.35f, 0.75f, 1f);
    [Tooltip("날숨 구간 게이지 색")]
    [SerializeField] private Color exhaleGaugeColor = new Color(1f, 0.6f, 0.3f);

    [Header("=== 링 비주얼 (구버전 폴백, World Space) ===")]
    [Tooltip("들숨에 팽창/날숨에 수축하는 링 (localScale). 원형 게이지를 배선하면 쓰이지 않는다.")]
    [SerializeField] private Transform ringVisual;
    [SerializeField] private float ringMinScale = 0.5f;
    [SerializeField] private float ringMaxScale = 1f;
    [Tooltip("호흡 카운트 표시 텍스트 (선택). 예: '호흡 1/3'. 비우면 콘솔 로그만.")]
    [SerializeField] private TMP_Text breathCountText;
    [Tooltip("진행 중 카운트 색")]
    [SerializeField] private Color countColor = Color.white;
    [Tooltip("3회 완료 시 카운트 색 (초록, '됐구나' 신호)")]
    [SerializeField] private Color doneColor = new Color(0.3f, 1f, 0.4f);

    [Header("=== 숨소리 (선택) ===")]
    [Tooltip("숨소리 재생용 AudioSource. 어깨-이마 자세로 링이 잘 안 보일 때 호흡 페이스를 소리로 안내한다.")]
    [SerializeField] private AudioSource breathAudioSource;
    [Tooltip("들숨 시작 시 재생할 클립 (선택). 클립 길이를 breatheInDuration에 맞추면 자연스럽다.")]
    [SerializeField] private AudioClip inhaleClip;
    [Tooltip("날숨 시작 시 재생할 클립 (선택).")]
    [SerializeField] private AudioClip exhaleClip;
    [Tooltip("숨소리 볼륨. 클립이 작으면 올리고, 너무 크면 낮춘다. (플레이스홀더 클립은 +17dB 증폭돼 있음)")]
    [SerializeField, Range(0f, 1f)] private float breathVolume = 1f;

    [Tooltip("★숨소리가 크다는 지적(08-11) — 위 breathVolume에 이 배수를 곱한다.\n" +
             "씬에 이미 breathVolume=1이 직렬화돼 있어 그 기본값을 바꿔도 안 먹기 때문에 배수를 따로 둔다.\n" +
             "★신규 필드라 이미 배치된 씬에도 코드 기본값이 그대로 먹는다.")]
    [SerializeField, Range(0f, 1f)] private float breathVolumeScale = 0.45f;

    private System.Func<bool> tensionProvider;
    private bool running = false;
    private bool complete = false;
    private bool inhaling = true;
    private float phaseTimer = 0f;
    private int completedBreaths = 0;
    private float breathHeldTime = 0f;   // 호흡 주기 동안 적정 텐션 유지 시간
    private float breathTotalTime = 0f;  // 호흡 주기 경과 시간

    public bool IsComplete => complete;
    public bool IsRunning => running;
    public int CompletedBreaths => completedBreaths;

    /// <summary>유지비율 미달로 호흡 카운트가 리셋된 횟수(평가 지표용). StartWindow에서 0으로 초기화.</summary>
    public int FailedBreaths => failedBreaths;
    private int failedBreaths = 0;

    /// <summary>마지막으로 평가한 호흡 주기의 자세 유지비율 0~1(평가 지표용).</summary>
    public float LastHoldRatio => lastHoldRatio;
    private float lastHoldRatio = 0f;

    /// <summary>현재 호흡량 0(완전 날숨)~1(완전 들숨). 링 스케일과 동일 위상.
    /// 환자 흉곽(CranialPatientBreath) 등 외부 시각요소를 링과 동기화하는 데 사용.</summary>
    public float BreathAmount01 => currentBreath01;
    private float currentBreath01 = 0f;

    /// <summary>지금 들숨 국면인가(false = 날숨).</summary>
    public bool IsInhaling => inhaling;

    /// <summary>한 호흡 주기(들숨→날숨) 안에서의 진행도 0~1.
    /// 들숨 구간이 0→0.5, 날숨 구간이 0.5→1로 이어진다.
    /// 굴곡·신전처럼 "한 클립에 주기 전체가 들어 있는" 애니메이션을 스크럽하는 데 쓴다.</summary>
    public float CycleProgress01
    {
        get
        {
            if (inhaling)
                return Mathf.Clamp01(phaseTimer / CurrentInhaleDuration) * 0.5f;
            return 0.5f + Mathf.Clamp01(phaseTimer / CurrentExhaleDuration) * 0.5f;
        }
    }

    /// <summary>적정 텐션 유지 여부 공급자 (예: () => left.IsInGoodZone &amp;&amp; right.IsInGoodZone)</summary>
    public void SetTensionProvider(System.Func<bool> provider) => tensionProvider = provider;

    public void StartWindow()
    {
        running = true;
        complete = false;
        inhaling = !startWithExhale;   // 날숨부터 시작하는 술기(PJ)도 있다
        phaseTimer = 0f;
        completedBreaths = 0;
        failedBreaths = 0;
        lastHoldRatio = 0f;
        breathHeldTime = 0f;
        breathTotalTime = 0f;
        gameObject.SetActive(true);
        ApplyVisualVisibility();   // showVisuals=false면 링·카운트를 숨긴다(타이밍 엔진은 계속 동작)
        EnsureBreathAudio();   // 인스펙터 미연결 시 Resources/Audio에서 자동 로드 + AudioSource 자동 생성
        UpdateBreathCountText();
        PlayBreathClip(inhaling);   // 첫 위상 숨소리(날숨부터 시작하면 날숨 소리)
        ChunaLogger.Log("<color=cyan>[BreathingSyncHUD] 호흡 윈도우 시작</color>");
    }

    private bool breathAudioResolved = false;

    /// <summary>숨소리 재생 준비를 보장(멱등). 인스펙터에 연결돼 있으면 그 값을 그대로 쓰고,
    /// 비어 있으면 이 GameObject의 AudioSource(없으면 생성) + Resources/Audio의 플레이스홀더 클립을 자동 연결한다.
    /// 시험용 배선 — 실제 녹음이 준비되면 인스펙터에서 직접 클립을 지정하면 이 폴백은 무시된다.</summary>
    private void EnsureBreathAudio()
    {
        if (breathAudioResolved) return;
        breathAudioResolved = true;

        if (breathAudioSource == null)
        {
            breathAudioSource = GetComponent<AudioSource>();
            if (breathAudioSource == null) breathAudioSource = gameObject.AddComponent<AudioSource>();
            breathAudioSource.playOnAwake = false;
            breathAudioSource.spatialBlend = 0f;   // 2D (시야 코너 HUD라 위치 무관)
            breathAudioSource.loop = false;
        }
        if (inhaleClip == null) inhaleClip = Resources.Load<AudioClip>("Audio/CranialBreathIn");
        if (exhaleClip == null) exhaleClip = Resources.Load<AudioClip>("Audio/CranialBreathOut");

        if (inhaleClip == null || exhaleClip == null)
            ChunaLogger.LogWarning("[BreathingSyncHUD] 숨소리 클립 로드 실패 — Resources/Audio/CranialBreathIn(Out).wav 확인");
    }

    /// <summary>위상 진입 시 해당 숨소리 재생 (클립 미연결이면 무음).</summary>
    private void PlayBreathClip(bool inhale)
    {
        if (breathAudioSource == null) return;
        AudioClip clip = inhale ? inhaleClip : exhaleClip;
        if (clip != null) breathAudioSource.PlayOneShot(clip, breathVolume * breathVolumeScale);
    }

    public void StopWindow()
    {
        running = false;
    }

    /// <summary>완전 초기화 - 시나리오(재)시작 시 완료 래칭 플래그까지 리셋</summary>
    public void ResetState()
    {
        running = false;
        complete = false;
        completedBreaths = 0;
        breathHeldTime = 0f;
        breathTotalTime = 0f;
        phaseTimer = 0f;
        gameObject.SetActive(false);
        UpdateBreathCountText();
    }

    /// <summary>호흡 카운트 텍스트 갱신 (연결됐을 때만).
    /// 머리를 숙인 견착 자세에서 곁눈질로 진행/완료를 판별하는 시야 코너 표시.
    /// 완료 시 "호흡 완료"+초록으로 바꿔 "됐구나" 신호를 명확히 준다.</summary>
    /// <summary>링·카운트 표시를 showVisuals에 맞춰 켜고 끈다.
    /// ★이 오브젝트 자체는 계속 활성이어야 한다 — Update가 돌아야 호흡 타이밍(숨소리·애니메이션 동기화)이 진행된다.
    /// 그래서 GameObject를 끄는 대신 자식 비주얼만 숨긴다.</summary>
    private void ApplyVisualVisibility()
    {
        if (ringVisual != null && ringVisual.gameObject.activeSelf != showVisuals)
            ringVisual.gameObject.SetActive(showVisuals);
        if (breathCountText != null && breathCountText.gameObject.activeSelf != showVisuals)
            breathCountText.gameObject.SetActive(showVisuals);
    }

    private void UpdateBreathCountText()
    {
        if (!showVisuals) return;   // 표시 끔 — 텍스트 갱신 불필요
        if (breathCountText == null) return;
        if (complete)
        {
            breathCountText.text = "호흡 완료";
            breathCountText.color = doneColor;
        }
        else
        {
            breathCountText.text = $"호흡 {completedBreaths}/{requiredBreaths}";
            breathCountText.color = countColor;
        }
    }

    void Update()
    {
        if (!running) return;

        phaseTimer += Time.deltaTime;

        // 호흡 주기 전체(들숨+날숨) 동안 적정 텐션 유지 시간을 누적한다(위상 무관).
        // 실제 술기: 3회 호흡하는 "동안" 압력을 유지 → 들숨/날숨 어느 위상인지는 상관없다.
        breathTotalTime += Time.deltaTime;
        if (tensionProvider != null && tensionProvider())
            breathHeldTime += Time.deltaTime;

        if (inhaling)
        {
            UpdateRing(Mathf.Clamp01(phaseTimer / CurrentInhaleDuration), true);

            if (phaseTimer >= CurrentInhaleDuration)
            {
                // 날숨부터 시작하는 술기(PJ)에선 '들숨 끝'이 한 주기의 끝이다.
                if (startWithExhale) CloseBreathCycle();
                inhaling = false;
                phaseTimer = 0f;
                if (running) PlayBreathClip(false);   // 날숨 위상 진입(완료 시엔 재생 안 함)
            }
        }
        else // 날숨
        {
            UpdateRing(Mathf.Clamp01(phaseTimer / CurrentExhaleDuration), false);

            if (phaseTimer >= CurrentExhaleDuration)
            {
                if (!startWithExhale) CloseBreathCycle();
                inhaling = true;
                phaseTimer = 0f;
                if (running) PlayBreathClip(true);   // 다음 들숨 위상 진입(완료 시엔 재생 안 함)
            }
        }
    }

    /// <summary>한 호흡 주기의 끝 처리 = 유지비율 평가 + 누적 초기화.
    /// 주기의 끝은 '시작 위상의 반대 위상이 끝나는 지점'이다
    /// (들숨부터면 날숨 끝, 날숨부터면 들숨 끝). 이 경계를 잘못 잡으면
    /// 날숨 6초만 채우고 단계가 끝나 마지막 작업 국면(크게 들숨)이 사라진다.</summary>
    private void CloseBreathCycle()
    {
        EvaluateBreath();
        breathHeldTime = 0f;
        breathTotalTime = 0f;
    }

    private void EvaluateBreath()
    {
        float ratio = breathTotalTime > 0f ? breathHeldTime / breathTotalTime : 0f;
        lastHoldRatio = ratio;   // 평가 지표용
        if (ratio >= requiredHoldRatio)
        {
            completedBreaths++;
            if (completedBreaths >= requiredBreaths)
            {
                complete = true;
                running = false;
            }
            UpdateBreathCountText();
            ChunaLogger.Log($"<color=green>[BreathingSyncHUD] 호흡 성공 {completedBreaths}/{requiredBreaths} (유지비율 {ratio:P0})</color>");
        }
        else
        {
            // 유지 실패 시 연속 카운트 리셋 (튜닝 포인트: 누적 허용으로 바꿀 수도 있음)
            failedBreaths++;
            completedBreaths = 0;
            UpdateBreathCountText();
            ChunaLogger.Log($"<color=orange>[BreathingSyncHUD] 호흡 실패 (유지비율 {ratio:P0}) - 카운트 리셋</color>");
        }
    }

    private void UpdateRing(float t, bool inhale)
    {
        // 정규화 호흡량(표시 방식과 무관하게 0~1) - 외부 동기화용
        currentBreath01 = inhale ? t : 1f - t;

        // ★원형 게이지 방식(기본) — 들숨에 차오르고 날숨에 비워진다. 색으로 위상을 구분한다.
        //   커졌다 작아졌다 하는 스케일 방식은 "지금 얼마나 남았는지"가 안 읽혀 게이지로 교체했다(08-10).
        if (breathGauge != null)
        {
            breathGauge.fillAmount = currentBreath01;
            breathGauge.color = inhale ? inhaleGaugeColor : exhaleGaugeColor;
            return;   // 게이지를 쓰면 링 스케일은 건드리지 않는다
        }

        // 폴백: 게이지가 배선되지 않은 씬은 기존 스케일 방식으로 동작한다.
        float scale01 = inhale ? Mathf.Lerp(ringMinScale, ringMaxScale, t)
                               : Mathf.Lerp(ringMaxScale, ringMinScale, t);
        if (ringVisual != null) ringVisual.localScale = Vector3.one * scale01;
    }
}
