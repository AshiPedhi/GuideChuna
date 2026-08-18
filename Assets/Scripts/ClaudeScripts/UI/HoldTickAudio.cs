using UnityEngine;

/// <summary>
/// 유지 타이머 '틱' 효과음을 한 곳에서 낸다.
///
/// ★왜 공용으로 뽑았나 (2026-08-18)
///   틱 소리가 두개골 계열(CranialAdjustmentController)에만 있었다. 손 포즈 판정(HandPose)의
///   StartHold·MidHold에는 진행 표시기(HoldProgressIndicator)만 있고 소리가 없어서,
///   같은 '유지' 동작인데 술기에 따라 소리가 나기도 하고 안 나기도 했다.
///   두 곳이 같은 소리를 쓰게 만들어야 크기를 조절할 때도 한 번에 바뀐다.
///
/// ★볼륨 하한(<see cref="MinVolume"/>)을 두는 이유
///   호출부의 볼륨 필드는 <b>이미 씬에 직렬화</b>돼 있다(리그 7개가 전부 0.5). 코드 기본값을 올려도
///   먹지 않으므로, 여기서 하한을 걸어 조용한 옛 값을 끌어올린다. 인스펙터에서 더 키우는 것은 그대로 반영된다.
///   ※음원 자체도 08-18에 증폭했다(TimerTick −20.9dB → −3dB, TimerTickLast −18.1dB → −1.5dB).
/// </summary>
public static class HoldTickAudio
{
    /// <summary>씬에 낮게 직렬화된 볼륨을 끌어올리는 하한. VR에서 나레이션에 묻히지 않을 정도.</summary>
    public const float MinVolume = 0.9f;

    private const string TickPath = "Audio/TimerTick";
    private const string TickLastPath = "Audio/TimerTickLast";

    private static AudioSource source;
    private static AudioClip tickClip;
    private static AudioClip tickLastClip;

    /// <summary>남은 초가 <b>바뀌는 순간에만</b> 한 번 울린다(매 프레임 울리지 않게).
    /// 마지막 1초는 조금 다른 소리로 알린다.</summary>
    /// <param name="lastSecond">호출자가 들고 있는 직전 초. 표시가 끊기면 <see cref="ResetCounter"/>로 되돌린다.</param>
    public static void Play(float remainingSeconds, ref int lastSecond, float volume)
    {
        int sec = Mathf.CeilToInt(remainingSeconds);
        if (sec == lastSecond) return;
        lastSecond = sec;
        if (sec <= 0) return;   // 0초 = 완료 — 완료음(띵동)이 담당한다

        EnsureReady();

        AudioClip clip = (sec <= 1 && tickLastClip != null) ? tickLastClip : tickClip;
        if (clip == null || source == null) return;
        source.PlayOneShot(clip, Mathf.Clamp01(Mathf.Max(volume, MinVolume)));
    }

    /// <summary>진행 표시가 끊겼을 때 — 다음 유지에서 처음부터 세도록 되돌린다.</summary>
    public static void ResetCounter(ref int lastSecond) => lastSecond = -1;

    private static void EnsureReady()
    {
        if (tickClip == null) tickClip = Resources.Load<AudioClip>(TickPath);
        if (tickLastClip == null) tickLastClip = Resources.Load<AudioClip>(TickLastPath);

        // 씬을 다시 로드하면 이전 오브젝트가 파괴되므로 null 검사로 다시 만든다.
        if (source != null) return;

        var go = new GameObject("[HoldTickAudio]");
        go.hideFlags = HideFlags.HideAndDontSave;
        source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;   // 2D — 손이 시야를 벗어나도 들려야 한다
    }
}
