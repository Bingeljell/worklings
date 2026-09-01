using Godot;

namespace Worklings.Core.Stage;

/// Numbers that rise from the point of contact and fade.
///
/// World-space, not screen-space: the number is spawned at the victim and
/// tracks it, so it stays attached to *where* the blow landed rather than
/// floating in a corner. With the HP plates already at the frame edges, this is
/// what connects the abstract number to the body it came off.
///
/// Coloured by the attacker's family, so a hit reads as belonging to whoever
/// threw it — the same system that drives hit sparks and the impact flash.
public sealed class DamageNumbers
{
    private readonly Node3D _parent;

    public DamageNumbers(Node3D parent) => _parent = parent;

    /// `crit` makes it bigger, hotter and slower — the three cues that read as
    /// "this one mattered" without needing a label.
    public void Spawn(Vector3 at, int amount, Color energy, bool crit)
    {
        var label = new Label3D
        {
            Text = amount.ToString(),
            Position = at + new Vector3(0, 2.6f, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = crit ? 320 : 200,
            OutlineSize = crit ? 60 : 44,
            Modulate = crit ? FamilyEnergy.Crit : FamilyEnergy.Lift(energy, 0.55f),
            OutlineModulate = new Color(0, 0, 0, 0.9f),
            PixelSize = 0.006f,
            // Numbers must never be occluded by the body they came off, which is
            // exactly where they spawn.
            NoDepthTest = true,
            RenderPriority = 8,
        };
        _parent.AddChild(label);

        double rise = crit ? 1.35 : 1.0;
        float height = crit ? 2.2f : 1.5f;
        var start = label.Position;

        var tween = label.CreateTween();
        tween.SetParallel(true);
        // Out fast then drift: a linear rise reads as a floating sticker, an
        // eased one reads as something knocked loose.
        tween.TweenProperty(label, "position", start + new Vector3(0, height, 0), rise)
             .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        tween.TweenProperty(label, "modulate:a", 0.0f, rise * 0.55)
             .SetDelay(rise * 0.45);
        if (crit)
        {
            label.Scale = Vector3.One * 0.55f;
            tween.TweenProperty(label, "scale", Vector3.One, 0.22)
                 .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Back);
        }
        tween.Chain().TweenCallback(Callable.From(label.QueueFree));
    }

    /// A miss says so in words — a blank beat with no feedback reads as a
    /// dropped frame rather than a dodge.
    public void SpawnMiss(Vector3 at)
    {
        var label = new Label3D
        {
            Text = "miss",
            Position = at + new Vector3(0, 2.6f, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 130,
            OutlineSize = 34,
            Modulate = new Color(0.72f, 0.68f, 0.60f),
            OutlineModulate = new Color(0, 0, 0, 0.9f),
            PixelSize = 0.006f,
            NoDepthTest = true,
            RenderPriority = 8,
        };
        _parent.AddChild(label);
        var tween = label.CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(label, "position", label.Position + new Vector3(0, 1.1f, 0), 0.85)
             .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(label, "modulate:a", 0.0f, 0.45).SetDelay(0.4);
        tween.Chain().TweenCallback(Callable.From(label.QueueFree));
    }
}
