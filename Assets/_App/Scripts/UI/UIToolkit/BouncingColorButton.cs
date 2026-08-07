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

    int _bounceDurationMs = DefaultBounceDurationMs;
    float _brightnessFactor = DefaultBrightnessFactor;

    IVisualElementScheduledItem _tick;
    Color _baseColor;
    Color _dimColor;
    bool _hasBaseColor;
    bool _needsResample = true;
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
        RegisterCallback<PointerEnterEvent>(_ => _needsResample = true);
        RegisterCallback<PointerLeaveEvent>(_ => _needsResample = true);
    }

    void OnAttachToPanel(AttachToPanelEvent evt)
    {
        _elapsedMs = 0;
        _needsResample = true;
        _tick?.Pause();
        _tick = schedule.Execute(OnTick).Every(16);
    }

    void OnDetachFromPanel(DetachFromPanelEvent evt)
    {
        _tick?.Pause();
        _tick = null;
        style.backgroundColor = StyleKeyword.Null;
        _hasBaseColor = false;
    }

    void OnTick(TimerState timer)
    {
        if (_needsResample)
            ResampleBaseColor();

        if (!_hasBaseColor)
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
        _needsResample = false;

        // Clear inline override so resolvedStyle picks up USS (including :hover).
        style.backgroundColor = StyleKeyword.Null;
        Color sampled = resolvedStyle.backgroundColor;

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
