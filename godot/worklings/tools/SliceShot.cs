using Godot;

/// Photographs the **live** dungeon, not a diorama of it.
///
/// This is the tool the handover asked for and neither previous pass had: a
/// capture of `cache_warren.tscn` itself, running a real delve through the real
/// scene, so what gets reviewed is the thing that ships. `VfxShot` deliberately
/// builds its own staging because it only wanted one attack at a time, and
/// `BattleLab` builds its own world entirely — both are honest about being
/// studies, and both can therefore be right about an arena the game never
/// shows. A vertical slice has to be judged as a slice.
///
/// **It owns the save problem rather than dodging it.** The scene loads a
/// Workling on open and writes one back when a run resolves, so this refuses to
/// start unless `WORKLINGS_SAVE` points somewhere — a capture has no business
/// anywhere near the real pet, and the rule is that only the shipped app writes
/// that file.
///
/// **Stepped at a fixed delta**, so `--fixed-fps 60` makes every frame exactly
/// 1/60 of scene time and two captures of the same seed are comparable. The
/// delve seeds off the clock, so runs still differ between captures; what is
/// stable is the pacing, which is what a review of framing and readability
/// needs.
public partial class SliceShot : Node
{
    private string _out = "";
    private int _frame;
    private int _saved;

    /// How many frames to capture, and how many to skip between saves.
    ///
    /// A delve is minutes long and a beat is three seconds, so saving every
    /// frame would be tens of thousands of PNGs of two creatures standing
    /// still. Every 6th frame is a 10fps contact sheet of the whole run —
    /// enough to judge framing, silhouette, size relationships and text, and
    /// deliberately not enough to judge a 0.3s effect. Use `VfxShot` for that;
    /// this answers "does the slice read", not "does the bolt read".
    private const int Frames = 3600;
    private const int Every = 6;

    public override void _Ready()
    {
        _out = OS.GetEnvironment("WORKLINGS_SLICE_OUT");
        if (_out.Length == 0) _out = ProjectSettings.GlobalizePath("user://slice-shots");

        if (OS.GetEnvironment("WORKLINGS_SAVE").Length == 0)
        {
            GD.PrintErr("SliceShot refuses to run without WORKLINGS_SAVE set. "
                      + "The dungeon writes a Workling back when a run resolves, and "
                      + "the real pet file is not a thing a capture tool may touch.");
            GetTree().Quit(1);
            return;
        }

        DirAccess.MakeDirRecursiveAbsolute(_out);

        var scene = GD.Load<PackedScene>("res://scenes/cache_warren.tscn");
        var dungeon = scene.Instantiate<CacheWarrenScene>();
        // Unattended: the capture takes every decision itself, so a full chain
        // to the mini-boss runs without a keypress. This is also the reason the
        // tool cannot review the two beats that are the player's — the steering
        // prompt and the bank/push choice are photographed being held, not being
        // decided.
        dungeon.AutoPlay = true;
        dungeon.Loop = false;
        AddChild(dungeon);

        GD.Print($"SliceShot -> {_out} ({Frames} frames, saving every {Every})");
    }

    public override void _Process(double delta)
    {
        if (_frame++ % Every == 0) Save();
        if (_frame > Frames)
        {
            GD.Print($"SliceShot wrote {_saved} frames to {_out}");
            GetTree().Quit();
        }
    }

    /// Saves the frame that has already been drawn.
    ///
    /// `GetTexture().GetImage()` reads the viewport after the frame is
    /// presented, so this is genuinely what was on screen rather than a
    /// re-render of the scene from a second camera.
    private void Save()
    {
        var image = GetViewport().GetTexture()?.GetImage();
        if (image == null) return;
        var error = image.SavePng($"{_out}/frame_{_saved:D5}.png");
        if (error != Error.Ok)
        {
            GD.PrintErr($"SliceShot could not write frame {_saved}: {error}");
            GetTree().Quit(1);
            return;
        }
        _saved++;
    }
}
