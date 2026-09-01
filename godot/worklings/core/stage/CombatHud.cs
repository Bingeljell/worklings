using Godot;

namespace Worklings.Core.Stage;

/// The dungeon's combat HUD: two health plates, a beat countdown, and the round
/// and Approach readout.
///
/// Screen-space rather than world-space bars floating over each creature. With
/// two combatants and a fixed camera, edge plates stay legible and never crowd
/// the models or clip at the frame border. That trade flips if a fight ever
/// holds three or four bodies — at which point this wants revisiting rather
/// than stretching.
///
/// Built in code rather than as a .tscn so the whole layout is readable in one
/// place while it is still being tuned.
public sealed class CombatHud
{
    private const int Margin = 46;

    private readonly HealthPlate _pet;
    private readonly HealthPlate _foe;
    private readonly ColorRect _beatFill;
    private readonly Control _beatTrack;
    private readonly Label _beatCount;
    private readonly Label _status;
    private readonly Label _narration;

    private const float BeatWidth = 240f;

    public CombatHud(Node parent, string petName, int petMax, Color petEnergy,
                     string foeName, int foeMax, Color foeEnergy)
    {
        var layer = new CanvasLayer();
        parent.AddChild(layer);

        var root = new Control { AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.AddChild(root);

        _pet = new HealthPlate(petName, petMax, petEnergy, mirrored: false);
        _pet.Root.Position = new Vector2(Margin, Margin);
        root.AddChild(_pet.Root);

        _foe = new HealthPlate(foeName, foeMax, foeEnergy, mirrored: true);
        _foe.Root.AnchorLeft = 1; _foe.Root.AnchorRight = 1;
        _foe.Root.Position = new Vector2(-Margin - 420, Margin);
        root.AddChild(_foe.Root);

        // Beat countdown, bottom-left: the fight's pulse. A flat metronome with
        // nothing showing it reads as dead air; a visible drain reads as a
        // wind-up to the next exchange.
        var beatBox = new VBoxContainer { AnchorTop = 1, AnchorBottom = 1 };
        beatBox.AddThemeConstantOverride("separation", 6);
        beatBox.Position = new Vector2(Margin, -Margin - 52);
        root.AddChild(beatBox);

        _narration = StageType.Label("", 24, StageType.Ink);
        beatBox.AddChild(_narration);

        // The countdown reads as a wind-up to the next exchange. A bare bar
        // draining was too quiet to notice, so the seconds are spelled out
        // beside it — the number is what makes the pause feel deliberate rather
        // than like the game hanging.
        var beatRow = new HBoxContainer();
        beatRow.AddThemeConstantOverride("separation", 12);
        beatRow.Alignment = BoxContainer.AlignmentMode.Begin;

        _beatTrack = new Control { CustomMinimumSize = new Vector2(BeatWidth, 6) };
        var beatBg = new ColorRect { Color = new Color(1, 1, 1, 0.12f), AnchorRight = 1, AnchorBottom = 1 };
        _beatTrack.AddChild(beatBg);
        _beatFill = new ColorRect { Color = new Color(0.95f, 0.91f, 0.85f, 0.9f), Size = new Vector2(0, 6) };
        _beatTrack.AddChild(_beatFill);
        beatRow.AddChild(_beatTrack);

        _beatCount = StageType.Label("", 19, StageType.Muted);
        beatRow.AddChild(_beatCount);
        beatBox.AddChild(beatRow);

        _status = StageType.Label("", 19, StageType.Faint);
        _status.AnchorTop = 1; _status.AnchorBottom = 1;
        _status.AnchorLeft = 1; _status.AnchorRight = 1;
        _status.Position = new Vector2(-Margin - 320, -Margin - 26);
        _status.Size = new Vector2(320, 26);
        _status.HorizontalAlignment = HorizontalAlignment.Right;
        root.AddChild(_status);
    }

    public void SetHP(int pet, int foe) { _pet.Set(pet); _foe.Set(foe); }
    public void Reset(int petMax, int foeMax) { _pet.Reset(petMax); _foe.Reset(foeMax); }
    public void SetNarration(string line) => _narration.Text = line;
    public void SetStatus(string line) => _status.Text = line;

    /// `progress` is 0 at the start of a beat and 1 when the next action fires;
    /// `remaining` is the seconds still to run, shown to one decimal so the
    /// countdown visibly moves rather than sitting on a whole number.
    public void SetBeat(double progress, double remaining)
    {
        _beatFill.Size = new Vector2(BeatWidth * (float)Mathf.Clamp(progress, 0, 1), 6);
        _beatCount.Text = remaining > 0.05 ? $"{remaining:0.0}s" : "";
    }

    /// Hides the countdown while an action is actually playing — a bar that
    /// drains through the attack implies the attack is the wait, when it is the
    /// thing being waited for.
    public void ClearBeat()
    {
        _beatFill.Size = new Vector2(0, 6);
        _beatCount.Text = "";
    }

    public void Tick(double delta) { _pet.Tick(delta); _foe.Tick(delta); }
}
