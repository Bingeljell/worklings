using Godot;

/// Runs the Cache Warren scene and grabs frames as the fight plays, so the
/// wiring can be checked without opening the editor.
public partial class FightShot : Node
{
    private const string Out = "user://fight_";
    private static readonly double[] At = { 1.5, 2.4, 3.6, 5.2, 6.6, 8.0 };

    public override async void _Ready()
    {
        var scene = GD.Load<PackedScene>("res://scenes/cache_warren.tscn").Instantiate();
        AddChild(scene);
        double elapsed = 0;
        for (int i = 0; i < At.Length; i++)
        {
            double wait = At[i] - elapsed;
            if (wait > 0) { await ToSignal(GetTree().CreateTimer(wait), "timeout"); elapsed = At[i]; }
            await ToSignal(RenderingServer.Singleton, "frame_post_draw");
            var path = ProjectSettings.GlobalizePath($"{Out}{i}.png");
            GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print("shot -> ", path);
        }
        GetTree().Quit();
    }
}
