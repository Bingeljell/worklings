using Godot;
using Worklings.Core.Pet;

namespace Worklings.Core.Host;

/// What the pet says when you do something to it.
///
/// A single line, floating up over the animal and fading. Care was previously
/// invisible: petting a Workling changed its needs, earned it XP, wrote the save
/// — and did nothing at all on screen. An interaction the player cannot see the
/// result of is one they stop performing.
///
/// The lines are lifted verbatim from `PetReaction.thought` in
/// Sources/CompanionCore/PetPresentation.swift, so the two apps say the same
/// things in the same voice. Only the reactions care can produce are here; the
/// activity ones arrive with the activity pipeline.
public partial class PetThought : Node2D
{
    /// How long the line hangs before it has gone entirely.
    [Export] public float Seconds { get; set; } = 1.9f;

    /// How far it drifts upward over its life, in window pixels.
    [Export] public float Rise { get; set; } = 26;

    private Label _label = null!;
    private double _elapsed;
    private Vector2 _from;

    public static string Thought(PetReaction reaction) => reaction switch
    {
        PetReaction.LikedFood => "Tasty!",
        PetReaction.LovedFood => "My favourite!",
        PetReaction.EnjoyedPlay => "That was fun!",
        PetReaction.LovedPlay => "Again, again!",
        PetReaction.Comforted => "I like you.",
        PetReaction.Rested => "Much better.",
        PetReaction.TooTiredToPlay => "Maybe after a nap…",
        PetReaction.HappyToSeeYou => "A new day!",
        PetReaction.CelebratedTask => "We did it!",
        PetReaction.SharedSetback => "We'll get the next one.",
        PetReaction.ProudOfMilestone => "Shipped!",
        PetReaction.GladYouAreBack => "You're back!",
        PetReaction.StartedWorking => "Let's get to work!",
        PetReaction.TookABreak => "Taking a breather.",
        PetReaction.WaitingOnYou => "Waiting on you…",
        PetReaction.NoticedYouAreAway => "Oh, you're away…",
        PetReaction.LoggedWork => "Logged!",
        _ => "",
    };

    public string Text { get; set; } = "";

    /// The display scale, so the line is not half the size of everything else —
    /// the pet window renders in physical pixels.
    public float Scale2D { get; set; } = 2;

    public override void _Ready()
    {
        _label = new Label
        {
            Text = Text,
            HorizontalAlignment = HorizontalAlignment.Center,
            // Sized from the centre, so the line grows both ways from the pet
            // rather than off to one side of it.
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
        };
        _label.AddThemeFontOverride(
            "font", GD.Load<Font>("res://assets/fonts/ChakraPetch-Bold.ttf"));
        _label.AddThemeFontSizeOverride(
            "font_size", (int)System.Math.Round(14 * Scale2D));
        _label.AddThemeColorOverride("font_color", WorklingsTheme.Ink);
        // An outline rather than a panel behind it: the window is transparent
        // and whatever is on the desktop behind the pet could be any colour, so
        // the line has to survive being read against all of them.
        _label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        _label.AddThemeConstantOverride(
            "outline_size", (int)System.Math.Round(5 * Scale2D));
        AddChild(_label);
        _label.Position = new Vector2(-200, 0);
        _label.Size = new Vector2(400, 0);

        _from = Position;
    }

    public override void _Process(double delta)
    {
        _elapsed += delta;
        float t = (float)System.Math.Clamp(_elapsed / System.Math.Max(Seconds, 0.05f), 0, 1);

        // Rises fast and settles, the way a spoken line lands — rather than
        // drifting at a constant speed, which reads as a floating number in a
        // damage log.
        Position = _from + Vector2.Up * Rise * (1 - (1 - t) * (1 - t));

        // Holds at full opacity for the first two thirds, then goes. A line that
        // fades from the moment it appears is never quite readable.
        Modulate = Modulate with { A = t < 0.66f ? 1 : 1 - (t - 0.66f) / 0.34f };

        if (t >= 1)
        {
            QueueFree();
        }
    }
}
