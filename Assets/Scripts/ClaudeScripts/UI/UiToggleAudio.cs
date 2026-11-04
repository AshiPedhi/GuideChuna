// ★[미사용 2026-11-04] 부르는 곳이 없다 — 사용자 «효과음은 기존 UI에 있는 거만 써»로 상단 버튼 호출을 뺐다(ui_open.wav = «팡팡» 두 번 타격).
using UnityEngine;

/// <summary>
/// 메뉴 토글 켬 · 끔 효과음 (2026-10-30 사용자 «설정이랑 이런 것들 켜져 있는지 소리도 토글 효과도 없어서 누르고 안 보면 모르겠다»).
/// 켬 = ui_open · 끔 = ui_close (Meta XR Haptics SDK 샘플 Application_UX — Resources/Audio/UI로 복사).
/// <see cref="HoldTickAudio"/>와 같은 꼴 — 2D 소리 한 개를 지연 생성해 공유한다(손이 시야 밖이어도 들려야 한다).
/// </summary>
public static class UiToggleAudio
{
    private const string OnPath = "Audio/UI/ui_open";
    private const string OffPath = "Audio/UI/ui_close";
    private const float Volume = 0.6f;

    private static AudioSource source;
    private static AudioClip onClip, offClip;

    public static void Play(bool on)
    {
        EnsureReady();
        var clip = on ? onClip : offClip;
        if (clip == null || source == null) return;
        source.PlayOneShot(clip, Volume);
    }

    private static void EnsureReady()
    {
        if (onClip == null) onClip = Resources.Load<AudioClip>(OnPath);
        if (offClip == null) offClip = Resources.Load<AudioClip>(OffPath);
        if (source != null) return;   // 씬을 다시 로드하면 파괴되므로 null 검사로 다시 만든다
        var go = new GameObject("[UiToggleAudio]");
        go.hideFlags = HideFlags.HideAndDontSave;
        source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
    }
}
