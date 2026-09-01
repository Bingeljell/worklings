using Godot;

/// Runs the Cache Warren scene and grabs frames as the fight plays, so the
/// wiring can be checked without opening the editor.
public partial class FightShot : Node
{
    private const string Out = "user://fight_";
    private static readonly double[] At = { 2.6, 2.72, 2.80, 4.0, 4.12, 5.4 };

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
