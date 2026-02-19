using UnityEngine;
using System.Collections;

/// <summary>
/// UI panel base class providing common show/hide, fade, and scale animation functionality.
/// Subclasses must implement GetPanelObject() to return the root GameObject to activate/deactivate.
/// </summary>
public abstract class BaseUIPanel : MonoBehaviour
{
    /// <summary>
    /// Whether the panel is currently visible.
    /// </summary>
    public bool IsVisible { get; protected set; }

    /// <summary>
    /// Returns the root GameObject that is activated/deactivated for show/hide.
    /// </summary>
    protected abstract GameObject GetPanelObject();

    /// <summary>
    /// Shows the panel by activating the panel object.
    /// Override to add pre/post show logic; always call base.Show().
    /// </summary>
    public virtual void Show()
    {
        var panel = GetPanelObject();
        if (panel != null)
            panel.SetActive(true);
        IsVisible = true;
    }

    /// <summary>
    /// Hides the panel by deactivating the panel object.
    /// Override to add pre/post hide logic; always call base.Hide().
    /// </summary>
    public virtual void Hide()
    {
        var panel = GetPanelObject();
        if (panel != null)
            panel.SetActive(false);
        IsVisible = false;
    }

    /// <summary>
    /// Fades a CanvasGroup alpha from one value to another over a duration.
    /// Uses unscaled time so it works even when Time.timeScale is 0.
    /// </summary>
    protected IEnumerator FadeCanvasGroup(CanvasGroup cg, float from, float to, float duration)
    {
        if (cg == null) yield break;

        float elapsed = 0f;
        cg.alpha = from;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            cg.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }

        cg.alpha = to;
    }

    /// <summary>
    /// Scales a Transform from one value to another over a duration.
    /// Optionally uses an AnimationCurve for easing.
    /// Uses unscaled time so it works even when Time.timeScale is 0.
    /// </summary>
    protected IEnumerator ScaleAnimation(Transform t, Vector3 from, Vector3 to, float duration, AnimationCurve curve = null)
    {
        if (t == null) yield break;

        float elapsed = 0f;
        t.localScale = from;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            float eval = curve != null ? curve.Evaluate(progress) : progress;
            t.localScale = Vector3.LerpUnclamped(from, to, eval);
            yield return null;
        }

        t.localScale = to;
    }
}
