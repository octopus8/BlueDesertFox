using UnityEngine;
using UnityEngine.UIElements;


/// <summary>
/// Button that continuously ping-pongs its background between the stylesheet
/// base color and a dimmed version (default 25% brightness). Retargets when
/// USS changes the base color (e.g. on :hover).
/// </summary>
[UxmlElement]
public partial class BouncingColorButton : Button
{
    const int DefaultBounceDurationMs = 800;
    const float DefaultBrightnessFactor = 0.25f;
    const float MinSampleAlpha = 0.01f;

    int _bounceDurationMs = DefaultBounceDurationMs;
    float _brightnessFactor = DefaultBrightnessFactor;

    IVisualElementScheduledItem _tick;
    Color _baseColor;
    Color _dimColor;
    bool _hasBaseColor;
    bool _needsResample = true;
    bool _awaitingUssSample;
    long _elapsedMs;

    [UxmlAttribute("bounce-duration-ms")]
    public int bounceDurationMs
    {
        get => _bounceDurationMs;
        set => _bounceDurationMs = Mathf.Max(1, value);
    }

    [UxmlAttribute("brightness-factor")]
    public float brightnessFactor
    {
        get => _brightnessFactor;
        set
        {
            _brightnessFactor = Mathf.Clamp01(value);
            if (_hasBaseColor)
                ApplyBaseColor(_baseColor);
        }
    }

    public BouncingColorButton()
    {
        RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
        RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        RegisterCallback<PointerEnterEvent>(_ => RequestResample());
        RegisterCallback<PointerLeaveEvent>(_ => RequestResample());
    }

    void OnAttachToPanel(AttachToPanelEvent evt)
    {
        _elapsedMs = 0;
        RequestResample();
        _tick?.Pause();
        _tick = schedule.Execute(OnTick).Every(16);
    }

    void OnDetachFromPanel(DetachFromPanelEvent evt)
    {
        _tick?.Pause();
        _tick = null;
        style.backgroundColor = StyleKeyword.Null;
        _hasBaseColor = false;
        _awaitingUssSample = false;
    }

    void RequestResample()
    {
        _needsResample = true;
        _awaitingUssSample = false;
    }

    void OnTick(TimerState timer)
    {
        if (_needsResample)
            ResampleBaseColor();

        // Don't overwrite the cleared/USS color while waiting for a valid sample.
        if (!_hasBaseColor || _needsResample)
            return;

        _elapsedMs += timer.deltaTime;
        float duration = _bounceDurationMs;
        float cycle = (_elapsedMs % (duration * 2)) / duration;
        float linearT = cycle <= 1f ? cycle : 2f - cycle;
        float t = EaseInOut(linearT);

        style.backgroundColor = Color.Lerp(_baseColor, _dimColor, t);
    }

    void ResampleBaseColor()
    {
        // Phase 1: clear inline override so USS can recompute (including :hover).
        if (!_awaitingUssSample)
        {
            style.backgroundColor = StyleKeyword.Null;
            _awaitingUssSample = true;
            return;
        }

        // Phase 2: read resolved USS color after a tick.
        Color sampled = resolvedStyle.backgroundColor;
        if (sampled.a < MinSampleAlpha)
        {
            // Styles not ready yet — keep retrying without locking in transparent.
            return;
        }

        _needsResample = false;
        _awaitingUssSample = false;

        if (_hasBaseColor && ColorsApproximatelyEqual(sampled, _baseColor))
            return;

        ApplyBaseColor(sampled);
    }

    void ApplyBaseColor(Color baseColor)
    {
        _baseColor = baseColor;
        _dimColor = new Color(
            baseColor.r * _brightnessFactor,
            baseColor.g * _brightnessFactor,
            baseColor.b * _brightnessFactor,
            baseColor.a);
        _hasBaseColor = true;
    }

    static float EaseInOut(float t)
    {
        return t < 0.5f
            ? 2f * t * t
            : 1f - Mathf.Pow(-2f * t + 2f, 2f) * 0.5f;
    }

    static bool ColorsApproximatelyEqual(Color a, Color b)
    {
        const float epsilon = 0.004f;
        return Mathf.Abs(a.r - b.r) < epsilon
            && Mathf.Abs(a.g - b.g) < epsilon
            && Mathf.Abs(a.b - b.b) < epsilon
            && Mathf.Abs(a.a - b.a) < epsilon;
    }
}
