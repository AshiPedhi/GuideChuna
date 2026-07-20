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
    [Tooltip("들숨 길이 (초)")]
    [SerializeField] private float breatheInDuration = 4f;
    [Tooltip("날숨 길이 (초)")]
    [SerializeField] private float breatheOutDuration = 4f;
    [Tooltip("필요한 연속 호흡 횟수")]
    [SerializeField] private int requiredBreaths = 3;
    [Tooltip("호흡 주기(들숨+날숨) 동안 적정 텐션 유지비율 임계")]
    [SerializeField, Range(0f, 1f)] private float requiredHoldRatio = 0.7f;

    [Header("=== 링 비주얼 (선택, World Space) ===")]
    [Tooltip("들숨에 팽창/날숨에 수축하는 링 (localScale로 표현)")]
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

    /// <summary>현재 호흡량 0(완전 날숨)~1(완전 들숨). 링 스케일과 동일 위상.
    /// 환자 흉곽(CranialPatientBreath) 등 외부 시각요소를 링과 동기화하는 데 사용.</summary>
    public float BreathAmount01 => currentBreath01;
    private float currentBreath01 = 0f;

    /// <summary>적정 텐션 유지 여부 공급자 (예: () => left.IsInGoodZone &amp;&amp; right.IsInGoodZone)</summary>
    public void SetTensionProvider(System.Func<bool> provider) => tensionProvider = provider;

    public void StartWindow()
    {
        running = true;
        complete = false;
        inhaling = true;
        phaseTimer = 0f;
        completedBreaths = 0;
        breathHeldTime = 0f;
        breathTotalTime = 0f;
        gameObject.SetActive(true);
        EnsureBreathAudio();   // 인스펙터 미연결 시 Resources/Audio에서 자동 로드 + AudioSource 자동 생성
        UpdateBreathCountText();
        PlayBreathClip(true);   // 첫 들숨 소리
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
        if (clip != null) breathAudioSource.PlayOneShot(clip, breathVolume);
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
    private void UpdateBreathCountText()
    {
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
            UpdateRing(Mathf.Clamp01(phaseTimer / breatheInDuration), true);

            if (phaseTimer >= breatheInDuration)
            {
                inhaling = false;
                phaseTimer = 0f;
                PlayBreathClip(false);   // 날숨 위상 진입
            }
        }
        else // 날숨
        {
            UpdateRing(Mathf.Clamp01(phaseTimer / breatheOutDuration), false);

            if (phaseTimer >= breatheOutDuration)
            {
                EvaluateBreath();
                inhaling = true;
                phaseTimer = 0f;
                breathHeldTime = 0f;
                breathTotalTime = 0f;
                if (running) PlayBreathClip(true);   // 다음 들숨 위상 진입(완료 시엔 재생 안 함)
            }
        }
    }

    private void EvaluateBreath()
    {
        float ratio = breathTotalTime > 0f ? breathHeldTime / breathTotalTime : 0f;
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
            completedBreaths = 0;
            UpdateBreathCountText();
            ChunaLogger.Log($"<color=orange>[BreathingSyncHUD] 호흡 실패 (유지비율 {ratio:P0}) - 카운트 리셋</color>");
        }
    }

    private void UpdateRing(float t, bool inhale)
    {
        // 들숨: min→max 팽창, 날숨: max→min 수축
        float scale01 = inhale ? Mathf.Lerp(ringMinScale, ringMaxScale, t)
                               : Mathf.Lerp(ringMaxScale, ringMinScale, t);
        if (ringVisual != null) ringVisual.localScale = Vector3.one * scale01;

        // 정규화 호흡량(링 스케일 설정과 무관하게 0~1) - 외부 동기화용
        currentBreath01 = inhale ? t : 1f - t;
    }
}
