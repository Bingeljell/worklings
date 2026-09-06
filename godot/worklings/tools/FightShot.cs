using Godot;

/// Runs the Cache Warren scene and grabs frames as the fight plays, so the
/// wiring can be checked without opening the editor.
///
/// AutoPlay is the scene's own hands-off mode: the briefing confirms itself and
/// every decision is taken on a timer. Without it this tool sat on the loadout
/// screen for its whole run and photographed a menu six times, because the
/// briefing gate landed after the tool was written.
public partial class FightShot : Node
{
    private const string Out = "user://fight_";

    /// Spread across the first encounter rather than the whole chain — the
    /// interesting frames are the swings, and the later ones are the same beats
    /// against a different foe.
    private static readonly double[] At = MakeTimes();

    /// A dense burst rather than a handful of marks. The travel is a quarter of a
    /// second inside a beat that runs for well over one, so sampling on round
    /// numbers photographs the wind-up and the recovery and misses the streak
    /// entirely — the frames worth looking at are the ones you cannot predict.
    private static double[] MakeTimes()
    {
        var times = new System.Collections.Generic.List<double>();
        for (double t = 3.4; t <= 6.2; t += 0.06) times.Add(t);
        return times.ToArray();
    }

    public override async void _Ready()
    {
        var scene = GD.Load<PackedScene>("res://scenes/cache_warren.tscn").Instantiate<CacheWarrenScene>();
        scene.AutoPlay = true;
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
