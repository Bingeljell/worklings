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

        _narration = new Label { Text = "" };
        _narration.AddThemeFontSizeOverride("font_size", 22);
        _narration.AddThemeColorOverride("font_color", new Color(0.86f, 0.81f, 0.72f));
        _narration.AddThemeConstantOverride("outline_size", 6);
        _narration.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        beatBox.AddChild(_narration);

        _beatTrack = new Control { CustomMinimumSize = new Vector2(BeatWidth, 4) };
        var beatBg = new ColorRect { Color = new Color(1, 1, 1, 0.14f), AnchorRight = 1, AnchorBottom = 1 };
        _beatTrack.AddChild(beatBg);
        _beatFill = new ColorRect { Color = new Color(0.95f, 0.91f, 0.85f, 0.9f), Size = new Vector2(0, 4) };
        _beatTrack.AddChild(_beatFill);
        beatBox.AddChild(_beatTrack);

        _status = new Label { Text = "" };
        _status.AddThemeFontSizeOverride("font_size", 18);
        _status.AddThemeColorOverride("font_color", new Color(0.55f, 0.50f, 0.42f));
        _status.AddThemeConstantOverride("outline_size", 5);
        _status.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
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

    /// `progress` is 0 at the start of a beat and 1 when the next action fires.
    public void SetBeat(double progress) =>
        _beatFill.Size = new Vector2(BeatWidth * (float)Mathf.Clamp(progress, 0, 1), 4);

    public void Tick(double delta) { _pet.Tick(delta); _foe.Tick(delta); }
}
