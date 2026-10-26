using TMPro;
using UnityEngine;

/// <summary>Controls only the isolated Rib 2 draft player and its cloned patient arm.</summary>
public sealed class Rib2DemoSceneControls : MonoBehaviour
{
    [SerializeField] private Rib2VideoTextScenePlayer player = null;
    [SerializeField] private Rib2DemoPatientArm arm = null;
    [SerializeField] private UnityEngine.UI.Slider timelineSlider = null;
    [SerializeField] private TMP_Text readout = null;
    [SerializeField] private TMP_Text halfSpeedLabel = null;

    private bool halfSpeed;
    private float displayedTime = -1f;
    private float displayedDuration = -1f;
    private bool updatingUi;
    private string lastFailure;

    public bool IsHalfSpeed => halfSpeed;

    private void Awake()
    {
        if (!ReferencesReady()) return;
        if (player.TryInitialize(out _) && arm.Initialize(out _))
        {
            RefreshNow();
            Debug.Log("[Rib2SceneDemo] Separate scene UI ready: start pose, one cycle, pause/resume, half speed and timeline.", this);
        }
    }

    private void Update()
    {
        if (player == null || timelineSlider == null || readout == null) return;
        RefreshNow();
    }

    public void StartPose()
    {
        if (!ReferencesReady()) return;
        player.ShowStartPose();
        arm.Evaluate();
        RefreshNow();
    }

    public void PlayCycle()
    {
        if (!ReferencesReady()) return;
        player.PlayCycle();
        arm.Evaluate();
        RefreshNow();
    }

    public void TogglePlay()
    {
        if (!ReferencesReady()) return;
        player.SetPlaying(!player.IsPlaying);
        arm.Evaluate();
        RefreshNow();
    }

    public void ToggleHalf()
    {
        SetHalfSpeed(!halfSpeed);
    }

    public void SetHalfSpeed(bool enabled)
    {
        if (!ReferencesReady()) return;
        halfSpeed = enabled;
        player.SetHalfSpeed(halfSpeed);
        halfSpeedLabel.SetText(halfSpeed ? "0.5배 · 켜짐" : "0.5배");
    }

    public void Seek(float seconds)
    {
        if (updatingUi) return;
        if (!ReferencesReady()) return;
        player.SetPlaying(false);
        if (player.Seek(seconds)) arm.Evaluate();
        RefreshNow();
    }

    /// <summary>Editor previews may explicitly refresh this UI using the same player state.</summary>
    public void RefreshNow()
    {
        if (player == null || timelineSlider == null || readout == null) return;
        float time = player.CurrentTime;
        float total = player.Duration;
        if (time == displayedTime && total == displayedDuration) return;
        updatingUi = true;
        if (total != displayedDuration) timelineSlider.maxValue = total;
        timelineSlider.SetValueWithoutNotify(time);
        updatingUi = false;
        readout.SetText("{0:1} / {1:1}초", time, total);
        displayedTime = time;
        displayedDuration = total;
    }

    private bool ReferencesReady()
    {
        if (player != null && arm != null && timelineSlider != null && readout != null && halfSpeedLabel != null)
        {
            lastFailure = null;
            return true;
        }
        const string failure = "[Rib2SceneDemo] Scene UI reference is missing; wire player, arm, timelineSlider, readout and halfSpeedLabel.";
        if (lastFailure != failure) Debug.LogWarning(failure, this);
        lastFailure = failure;
        return false;
    }
}
