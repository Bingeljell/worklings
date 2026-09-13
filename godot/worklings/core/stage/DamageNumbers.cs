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
            Font = StageType.Bold,
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
    ///
    /// Louder than a damage number rather than quieter. The attacker has just
    /// committed its whole travel and come back with nothing, and the old
    /// lowercase "miss" was the same weight as a 4, so the biggest swing in the
    /// round produced the smallest thing on screen. It punches in rather than
    /// simply rising, because a miss is an event, not a tally.
    public void SpawnMiss(Vector3 at)
    {
        var label = new Label3D
        {
            Text = "MISS",
            Position = at + new Vector3(0, 3.0f, 0),
            Font = StageType.Semi,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 180,
            OutlineSize = 44,
            Modulate = new Color(0.86f, 0.83f, 0.76f),
            OutlineModulate = new Color(0, 0, 0, 0.9f),
            PixelSize = 0.006f,
            NoDepthTest = true,
            RenderPriority = 8,
            Scale = Vector3.One * 1.6f,
        };
        _parent.AddChild(label);

        // Overshoot and settle, then hang before it goes. The hang is what makes
        // it readable at this camera distance.
        var punch = label.CreateTween();
        punch.TweenProperty(label, "scale", Vector3.One, 0.16)
             .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);

        var tween = label.CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(label, "position", label.Position + new Vector3(0, 0.9f, 0), 1.05)
             .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(label, "modulate:a", 0.0f, 0.35).SetDelay(0.7);
        tween.Chain().TweenCallback(Callable.From(label.QueueFree));
    }
}
