using Godot;
using Worklings.Core.Host;
using Worklings.Core.Pet;
using Worklings.Core.Progression;

/// Grabs a frame of the character window's contents, so the model bay and the
/// tab layout can be checked without opening the editor. The sibling of
/// FightShot, and for the same reason: the bay is a SubViewport inside a
/// Control, which either renders or comes up as a black rectangle, and nothing
/// in a text probe can tell the difference.
///
/// Builds its own Workling. It never touches the real save — see "Which file,
/// and who is allowed to write it" in the port status doc.
public partial class CharacterShot : Node
{
    private static readonly string[] Out = { "user://character_0.png", "user://character_1.png" };

    private static AnimationPlayer? FindPlayer(Node node)
    {
        if (node is AnimationPlayer p) return p;
        foreach (var child in node.GetChildren())
        {
            var found = FindPlayer(child);
            if (found != null) return found;
        }
        return null;
    }

    public override async void _Ready()
    {
        var window = GetWindow();
        // 1:1, not the project's 1920x1080 canvas_items stretch — the shot is of
        // a window the size the character window actually opens at.
        window.ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
        window.Size = new Vector2I(560, 940);

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
        state = state.Acquiring(Item.MastersHone).Equipping(Item.MastersHone);

        var panel = new CharacterPanel(1.0f);
        // Parented to this node, not to the window: a node is still setting up
        // its children during _Ready, so AddChild on the window fails there.
        // A Control under a plain Node still anchors against the viewport.
        AddChild(panel);
        panel.Show(state);

        for (int i = 0; i < Out.Length; i++)
        {
            // A second apart, so the two frames differ if the idle is playing
            // and are identical if the bay is a still picture of frame one.
            await ToSignal(GetTree().CreateTimer(i == 0 ? 1.2 : 1.0), "timeout");
            await ToSignal(RenderingServer.Singleton, "frame_post_draw");
            var path = ProjectSettings.GlobalizePath(Out[i]);
            GetViewport().GetTexture().GetImage().SavePng(path);
            // Whether the bay is LIVE, not merely lit. A still model in a box
            // looks identical to an animated one in a screenshot, and "the idle
            // is not playing" is the one failure this tool would otherwise miss.
            var player = FindPlayer(panel);
            GD.Print($"shot -> {path}  idle {(player is null ? "no AnimationPlayer" : $"{player.CurrentAnimation} playing {player.IsPlaying()} at {player.CurrentAnimationPosition:F2}")}");
        }
        GetTree().Quit();
    }
}
