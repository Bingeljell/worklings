using Godot;

namespace Worklings.Core.Stage;

/// One combatant's HP readout: name, numbers, and a bar that lags.
///
/// The lag is the whole idea. A bar that snaps to the new value tells you the
/// number changed; two layers — a fast fill plus a slower bar catching up behind
/// it — make the *gap between them* the damage, so a blow is legible as a shape
/// before you read a digit. It also gives the eye something to follow during
/// hit-stop, which is otherwise a frozen frame with nothing happening in it.
public sealed class HealthPlate
{
    private const float BarHeight = 12f;
    private const float PlateWidth = 420f;

    private readonly Label _name;
    private readonly Label _numbers;
    private readonly ColorRect _lag;
    private readonly ColorRect _fill;
    private readonly Control _barBox;
    private readonly bool _mirrored;

    private int _max;
    private int _shown;
    private float _lagRatio = 1f;
    private double _lagHold;

    /// How long the lag bar waits before it starts catching up, and how fast it
    /// closes. The pause is what makes the gap readable — without it the two
    /// layers move together and the effect disappears.
    private const double LagHoldSeconds = 0.22;
    private const float LagCatchupPerSecond = 0.9f;

    public Control Root { get; }

    public HealthPlate(string name, int maxHP, Color energy, bool mirrored)
    {
        _max = System.Math.Max(1, maxHP);
        _shown = _max;
        _mirrored = mirrored;

        Root = new VBoxContainer { CustomMinimumSize = new Vector2(PlateWidth, 0) };
        Root.AddThemeConstantOverride("separation", 5);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 12);
        header.Alignment = mirrored ? BoxContainer.AlignmentMode.End : BoxContainer.AlignmentMode.Begin;

        _name = new Label { Text = name };
        _name.AddThemeFontSizeOverride("font_size", 26);
        _name.AddThemeColorOverride("font_color", new Color(0.95f, 0.91f, 0.85f));
        _name.AddThemeConstantOverride("outline_size", 6);
        _name.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));

        _numbers = new Label { Text = $"{_max} / {_max}" };
        _numbers.AddThemeFontSizeOverride("font_size", 20);
        _numbers.AddThemeColorOverride("font_color", new Color(0.66f, 0.60f, 0.51f));
        _numbers.AddThemeConstantOverride("outline_size", 5);
        _numbers.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));

        // Numbers sit inboard of the name on the mirrored plate so both plates
        // read outside-in from their own frame edge.
        if (mirrored) { header.AddChild(_numbers); header.AddChild(_name); }
        else { header.AddChild(_name); header.AddChild(_numbers); }
        Root.AddChild(header);

        _barBox = new Control { CustomMinimumSize = new Vector2(PlateWidth, BarHeight) };
        var track = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.62f),
            AnchorRight = 1, AnchorBottom = 1,
        };
        _barBox.AddChild(track);

        _lag = new ColorRect { Color = new Color(1f, 0.35f, 0.24f, 0.62f), AnchorBottom = 1 };
        _fill = new ColorRect { Color = energy, AnchorBottom = 1 };
        _barBox.AddChild(_lag);
        _barBox.AddChild(_fill);
        Root.AddChild(_barBox);

        Layout(1f, 1f);
    }

    /// Set the current value. The fill moves immediately; the lag follows.
    public void Set(int current)
    {
        current = System.Math.Clamp(current, 0, _max);
        if (current < _shown) _lagHold = LagHoldSeconds;   // only damage lags
        else _lagRatio = current / (float)_max;            // healing snaps both
        _shown = current;
        _numbers.Text = $"{_shown} / {_max}";
        Layout(_shown / (float)_max, _lagRatio);
    }

    public void Reset(int maxHP)
    {
        _max = System.Math.Max(1, maxHP);
        _shown = _max;
        _lagRatio = 1f;
        _lagHold = 0;
        _numbers.Text = $"{_max} / {_max}";
        Layout(1f, 1f);
    }

    public void Tick(double delta)
    {
        float target = _shown / (float)_max;
        if (_lagRatio <= target) return;
        if (_lagHold > 0) { _lagHold -= delta; return; }
        _lagRatio = Mathf.Max(target, _lagRatio - LagCatchupPerSecond * (float)delta);
        Layout(target, _lagRatio);
    }

    /// Both layers grow from the plate's outer edge, so the pair drains toward
    /// the centre of the frame and the two combatants mirror each other.
    private void Layout(float fill, float lag)
    {
        Place(_fill, fill);
        Place(_lag, lag);
    }

    private void Place(ColorRect rect, float ratio)
    {
        float w = PlateWidth * Mathf.Clamp(ratio, 0, 1);
        rect.Size = new Vector2(w, BarHeight);
        rect.Position = new Vector2(_mirrored ? PlateWidth - w : 0, 0);
    }
}
