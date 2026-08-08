using UnityEngine;
using UnityEngine.UIElements;


/// <summary>
/// Button that continuously ping-pongs its background between a rest/hover color
/// and a dimmed version. Colors are explicit (not re-sampled from USS) so enter/leave
/// never flashes a full-bright stylesheet :hover color.
/// </summary>
[UxmlElement]
public partial class BouncingColorButton : Button
{
    const int DefaultBounceDurationMs = 800;
    const float DefaultBrightnessFactor = 0.25f;

    // Matches StartMenu.uss .unity-button-start / .unity-button:hover
    static readonly Color DefaultRestColor = new Color(158f / 255f, 19f / 255f, 78f / 255f, 1f);
    static readonly Color DefaultHoverColor = new Color(0f, 1f, 1f, 1f);

    int _bounceDurationMs = DefaultBounceDurationMs;
    float _brightnessFactor = DefaultBrightnessFactor;
    Color _restColor = DefaultRestColor;
    Color _hoverColor = DefaultHoverColor;

    IVisualElementScheduledItem _tick;
    Color _baseColor;
    Color _dimColor;
    bool _hasBaseColor;
    bool _pointerHovered;
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
                ApplyTargetColor(CurrentTargetColor(), retargetFromDim: false);
        }
    }

    [UxmlAttribute("rest-color")]
    public Color restColor
    {
        get => _restColor;
        set
        {
            _restColor = value;
            if (_hasBaseColor && !_pointerHovered)
                ApplyTargetColor(_restColor, retargetFromDim: false);
        }
    }

    [UxmlAttribute("hover-color")]
    public Color hoverColor
    {
        get => _hoverColor;
        set
        {
            _hoverColor = value;
            if (_hasBaseColor && _pointerHovered)
                ApplyTargetColor(_hoverColor, retargetFromDim: false);
        }
    }

    public BouncingColorButton()
    {
        RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
        RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        // Enter/Leave only — Over/Out fire when moving between the button and its text child.
        RegisterCallback<PointerEnterEvent>(_ => SetHovered(true));
        RegisterCallback<PointerLeaveEvent>(_ => SetHovered(false));
    }

    void OnAttachToPanel(AttachToPanelEvent evt)
    {
        _elapsedMs = 0;
        _pointerHovered = false;
        ApplyTargetColor(_restColor, retargetFromDim: false);
        _tick?.Pause();
        _tick = schedule.Execute(OnTick).Every(16);
    }

    void OnDetachFromPanel(DetachFromPanelEvent evt)
    {
        _tick?.Pause();
        _tick = null;
        _hasBaseColor = false;
    }

    void SetHovered(bool hovered)
    {
        if (_pointerHovered == hovered)
            return;

        _pointerHovered = hovered;
        ApplyTargetColor(CurrentTargetColor(), retargetFromDim: true);
    }

    Color CurrentTargetColor() => _pointerHovered ? _hoverColor : _restColor;

    void ApplyTargetColor(Color target, bool retargetFromDim)
    {
        _baseColor = target;
        _dimColor = new Color(
            target.r * _brightnessFactor,
            target.g * _brightnessFactor,
            target.b * _brightnessFactor,
            target.a);
        _hasBaseColor = true;

        if (retargetFromDim)
            _elapsedMs = _bounceDurationMs;

        style.backgroundColor = Color.Lerp(_baseColor, _dimColor, CurrentBounceT());
    }

    void OnTick(TimerState timer)
    {
        if (!_hasBaseColor)
            return;

        _elapsedMs += timer.deltaTime;
        style.backgroundColor = Color.Lerp(_baseColor, _dimColor, CurrentBounceT());
    }

    float CurrentBounceT()
    {
        float duration = _bounceDurationMs;
        float cycle = (_elapsedMs % (duration * 2)) / duration;
        float linearT = cycle <= 1f ? cycle : 2f - cycle;
        return EaseInOut(linearT);
    }

    static float EaseInOut(float t)
    {
        return t < 0.5f
            ? 2f * t * t
            : 1f - Mathf.Pow(-2f * t + 2f, 2f) * 0.5f;
    }
}
