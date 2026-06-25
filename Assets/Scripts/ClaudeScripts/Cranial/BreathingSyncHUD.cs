using UnityEngine;

/// <summary>
/// 호흡 동기화 HUD + 윈도우 판정 (기능3)
/// - HUD는 World Space + 메인카메라 자식으로 배치(Quest 스테레오 깨짐 방지). 이 스크립트는 링 비주얼을 제어만 함.
/// - 윈도우 판정: 들숨(inhale) 구간 동안 적정 텐션 유지비율 ≥ requiredHoldRatio 이면 1회 호흡 성공.
///   requiredBreaths회 연속 성공 시 IsComplete. 텐션 상태는 tensionProvider로 외부(깊이 가이드)에서 주입.
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
    [Tooltip("들숨 구간 적정 텐션 유지비율 임계")]
    [SerializeField, Range(0f, 1f)] private float requiredHoldRatio = 0.7f;

    [Header("=== 링 비주얼 (선택, World Space) ===")]
    [Tooltip("들숨에 팽창/날숨에 수축하는 링 (scale 또는 fill로 표현)")]
    [SerializeField] private Transform ringVisual;
    [SerializeField] private float ringMinScale = 0.5f;
    [SerializeField] private float ringMaxScale = 1f;
    [SerializeField] private UnityEngine.UI.Image fillImage;

    private System.Func<bool> tensionProvider;
    private bool running = false;
    private bool complete = false;
    private bool inhaling = true;
    private float phaseTimer = 0f;
    private int completedBreaths = 0;
    private float inhaleHeldTime = 0f;
    private float inhaleTotalTime = 0f;

    public bool IsComplete => complete;
    public bool IsRunning => running;
    public int CompletedBreaths => completedBreaths;

    /// <summary>적정 텐션 유지 여부 공급자 (예: () => left.IsInGoodZone &amp;&amp; right.IsInGoodZone)</summary>
    public void SetTensionProvider(System.Func<bool> provider) => tensionProvider = provider;

    public void StartWindow()
    {
        running = true;
        complete = false;
        inhaling = true;
        phaseTimer = 0f;
        completedBreaths = 0;
        inhaleHeldTime = 0f;
        inhaleTotalTime = 0f;
        gameObject.SetActive(true);
        ChunaLogger.Log("<color=cyan>[BreathingSyncHUD] 호흡 윈도우 시작</color>");
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
        inhaleHeldTime = 0f;
        inhaleTotalTime = 0f;
        phaseTimer = 0f;
        gameObject.SetActive(false);
    }

    void Update()
    {
        if (!running) return;

        phaseTimer += Time.deltaTime;

        if (inhaling)
        {
            inhaleTotalTime += Time.deltaTime;
            if (tensionProvider != null && tensionProvider())
                inhaleHeldTime += Time.deltaTime;

            UpdateRing(Mathf.Clamp01(phaseTimer / breatheInDuration), true);

            if (phaseTimer >= breatheInDuration)
            {
                inhaling = false;
                phaseTimer = 0f;
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
                inhaleHeldTime = 0f;
                inhaleTotalTime = 0f;
            }
        }
    }

    private void EvaluateBreath()
    {
        float ratio = inhaleTotalTime > 0f ? inhaleHeldTime / inhaleTotalTime : 0f;
        if (ratio >= requiredHoldRatio)
        {
            completedBreaths++;
            ChunaLogger.Log($"<color=green>[BreathingSyncHUD] 호흡 성공 {completedBreaths}/{requiredBreaths} (유지비율 {ratio:P0})</color>");
            if (completedBreaths >= requiredBreaths)
            {
                complete = true;
                running = false;
            }
        }
        else
        {
            // 유지 실패 시 연속 카운트 리셋 (튜닝 포인트: 누적 허용으로 바꿀 수도 있음)
            completedBreaths = 0;
            ChunaLogger.Log($"<color=orange>[BreathingSyncHUD] 호흡 실패 (유지비율 {ratio:P0}) - 카운트 리셋</color>");
        }
    }

    private void UpdateRing(float t, bool inhale)
    {
        // 들숨: min→max 팽창, 날숨: max→min 수축
        float scale01 = inhale ? Mathf.Lerp(ringMinScale, ringMaxScale, t)
                               : Mathf.Lerp(ringMaxScale, ringMinScale, t);
        if (ringVisual != null) ringVisual.localScale = Vector3.one * scale01;
        if (fillImage != null) fillImage.fillAmount = inhale ? t : 1f - t;
    }
}
