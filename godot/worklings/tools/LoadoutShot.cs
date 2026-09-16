using Godot;
using Worklings.Core.Pet;
using Worklings.Core.Progression;
using Worklings.Core.Stage;

/// Grabs a frame of the prep screen on its own, without playing a dungeon.
///
/// The sibling of `CharacterShot`, and it exists for the same two reasons. The
/// rig is a live 3D bay inside a Control, which either renders or comes up as a
/// black rectangle and no text probe can tell the difference. And the only other
/// way to see this screen was `beat_shot`, which plays a whole four-encounter
/// delve to photograph its first beat — minutes of fighting for one frame of a
/// screen that appears before any of it.
///
/// Builds its own Workling, and never touches the real save.
///
/// `WORKLINGS_SHOT_CURSOR=<0..4>` moves the keyboard cursor before the shot —
/// the selected row and the selected plate are drawn differently, and "the
/// cursor is visible on a plate" is a claim a shot of row zero cannot check.
public partial class LoadoutShot : Node
{
    private const string Out = "user://loadout.png";

    /// How many frames to let pass before the shot, so the bay's idle has moved
    /// off its first frame — the shot should show a creature standing, not a
    /// creature being built.
    private const int Settle = 90;

    private int _frames;
    private bool _saved;

    public override void _Ready()
    {
        var state = new PetState(
            name: "Anvil",
            // Hunger, not fullness — the screen shows the inverse.
            needs: new PetNeeds(28, 64, 81, 55),
            preferences: new PetPreferences(PetFood.Berries, PetPlayActivity.Puzzle),
            lastUpdatedAt: System.DateTimeOffset.Parse("2026-09-04T10:00:00Z"),
            family: PetFamily.Relicborn,
            totalXP: 2600,
            petClass: PetClass.Juggernaut,
            stats: new PetStats(vitality: 24, power: 26, defense: 16, agility: 12, wit: 9));
        // A Prime Tool, a Solid Ward, an empty Charm, and a spare of each: all
        // three plate states on screen at once, and a picker with something in
        // it rather than one line.
        state = state
            .Acquiring(Item.MastersHone).Equipping(Item.MastersHone)
            .Acquiring(Item.CrackedWhetstone)
            .Acquiring(Item.DentedBuckler).Equipping(Item.DentedBuckler)
            .Acquiring(Item.WarmBackupCoal)
            .ClearingSlot(ItemSlot.Charm);

        var panel = new LoadoutPanel(this);
        panel.Open(state);

        string wanted = OS.GetEnvironment("WORKLINGS_SHOT_CURSOR");
        if (int.TryParse(wanted, out int row))
        {
            for (int i = 0; i < row; i++) panel.HandleKey(Key.Down);
        }

        // **After the frame is drawn, not during it.** `_Process` runs before the
        // draw, so `GetViewport().GetTexture()` there holds the PREVIOUS frame —
        // which is exactly the bug that made every `000_prep.png` this project
        // ever captured come out solid black. Subscribed rather than awaited,
        // the way `BeatShot` and `AuraStudy` do it.
        RenderingServer.Singleton.FramePostDraw += Capture;
    }

    public override void _Process(double _) => _frames++;

    private void Capture()
    {
        if (_saved || _frames < Settle) return;
        _saved = true;

        var image = GetViewport().GetTexture()?.GetImage();
        if (image is null)
        {
            GD.PrintErr("LoadoutShot got no frame to save.");
            GetTree().Quit(1);
            return;
        }

        string path = ProjectSettings.GlobalizePath(Out);
        var error = image.SavePng(path);
        if (error != Error.Ok)
        {
            GD.PrintErr($"LoadoutShot could not write {path}: {error}");
            GetTree().Quit(1);
            return;
        }
        GD.Print($"shot -> {path}");
        GetTree().Quit();
    }
}
