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
    const int MaxStaleSampleTicks = 30;

    int _bounceDurationMs = DefaultBounceDurationMs;
    float _brightnessFactor = DefaultBrightnessFactor;

    IVisualElementScheduledItem _tick;
    Color _baseColor;
    Color _dimColor;
    bool _hasBaseColor;
    bool _needsResample = true;
    bool _awaitingUssSample;
    int _staleSampleTicks;
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
        // Over/Out are more reliable than Enter/Leave with some XR UI Toolkit setups.
        RegisterCallback<PointerOverEvent>(_ => RequestResample());
        RegisterCallback<PointerOutEvent>(_ => RequestResample());
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
        _staleSampleTicks = 0;
    }

    void RequestResample()
    {
        _needsResample = true;
        _awaitingUssSample = false;
        _staleSampleTicks = 0;
    }

    void OnTick(TimerState timer)
    {
        if (_needsResample)
            ResampleBaseColor();

        // Freeze on the last bounce color while waiting for a valid USS sample.
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
        bool hadBaseColor = _hasBaseColor;
        Color previousDisplay = hadBaseColor
            ? Color.Lerp(_baseColor, _dimColor, CurrentBounceT())
            : default;

        // Wait one tick after the request so :hover / leave pseudo-classes apply.
        // Same-frame sampling often reads the pre-hover color with XR pointers.
        if (!_awaitingUssSample)
        {
            _awaitingUssSample = true;
            return;
        }

        style.backgroundColor = StyleKeyword.Null;
        Color sampled = resolvedStyle.backgroundColor;

        if (!TryFinishResample(sampled, retargetFromDim: hadBaseColor))
        {
            if (hadBaseColor)
                style.backgroundColor = previousDisplay;
        }
    }

    bool TryFinishResample(Color sampled, bool retargetFromDim)
    {
        if (sampled.a < MinSampleAlpha)
            return false;

        // Pseudo-class colors can lag enter/leave by a few ticks; keep trying.
        if (_hasBaseColor && ColorsApproximatelyEqual(sampled, _baseColor))
        {
            if (retargetFromDim && _staleSampleTicks < MaxStaleSampleTicks)
            {
                _staleSampleTicks++;
                return false;
            }

            _needsResample = false;
            _awaitingUssSample = false;
            _staleSampleTicks = 0;
            return true;
        }

        _needsResample = false;
        _awaitingUssSample = false;
        _staleSampleTicks = 0;

        ApplyBaseColor(sampled);

        // On hover/leave retarget, start from the dim end so we don't flash full base.
        if (retargetFromDim)
            _elapsedMs = _bounceDurationMs;

        return true;
    }

    float CurrentBounceT()
    {
        float duration = _bounceDurationMs;
        float cycle = (_elapsedMs % (duration * 2)) / duration;
        float linearT = cycle <= 1f ? cycle : 2f - cycle;
        return EaseInOut(linearT);
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
