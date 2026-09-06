using Godot;

/// Renders arbitrary .glb files untextured, from the locked dungeon angle.
///
/// Loads through GltfDocument at runtime rather than the editor importer, so
/// candidate exports sitting outside the project can be compared without being
/// committed to it first. Grey because a texture hides what the geometry is
/// doing — the investigation's own first move.
public partial class GreyShot : Node
{
    private const float Azimuth = 59.7f;
    private const float Elevation = 39.7f;
    private const float Fov = 32f;

    public override async void _Ready()
    {
        DisplayServer.WindowSetSize(new Vector2I(900, 900));
        var args = OS.GetCmdlineUserArgs();
        if (args.Length < 2) { GD.Print("usage: -- outdir file.glb ..."); GetTree().Quit(1); return; }

        BuildLighting();
        var camera = new Camera3D { Fov = Fov, Far = 500 };
        AddChild(camera);

        // Flags first, then files. --textured keeps the model's own materials,
        // which is the opposite of this tool's default but the only way to judge
        // how a candidate actually reads in the game.
        bool textured = false;
        var azimuths = new System.Collections.Generic.List<float>();
        var files = new System.Collections.Generic.List<string>();
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--textured") textured = true;
            else if (args[i].StartsWith("--az=")) azimuths.Add(float.Parse(args[i].Substring(5)));
            else files.Add(args[i]);
        }
        if (azimuths.Count == 0) azimuths.Add(Azimuth);

        foreach (string file in files)
        foreach (float azimuth in azimuths)
        {
            var doc = new GltfDocument();
            var state = new GltfState();
            if (doc.AppendFromFile(file, state) != Error.Ok)
            {
                GD.Print($"could not read {file}");
                continue;
            }
            var root = doc.GenerateScene(state) as Node3D;
            if (root == null) { GD.Print($"no scene in {file}"); continue; }
            AddChild(root);

            var mi = Find<MeshInstance3D>(root);
            if (mi == null) { GD.Print($"no mesh in {file}"); root.QueueFree(); continue; }
            if (!textured)
                mi.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("a8adb6") };

            // Hold a pose rather than the T-shape the file opens on, so the
            // shot shows the character as the fight sees it.
            var player = Find<AnimationPlayer>(root);
            if (player != null && player.HasAnimation("Idle"))
            {
                player.Play("Idle");
                player.Seek(0.4, update: true);
                player.Pause();
            }

            var aabb = mi.GlobalTransform * mi.GetAabb();
            float radius = aabb.Size.Length() * 0.5f;
            float el = Mathf.DegToRad(Elevation), az = Mathf.DegToRad(azimuth);
            camera.Position = aabb.GetCenter() + new Vector3(
                Mathf.Cos(el) * Mathf.Sin(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Cos(az))
                * (radius / Mathf.Tan(Mathf.DegToRad(Fov * 0.5f)) * 0.62f);
            camera.LookAt(aabb.GetCenter(), Vector3.Up);

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await RenderingServer.Singleton.ToSignal(
                RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

            string name = file.GetFile().GetBaseName()
                        + (textured ? "-tex" : "-grey") + $"-az{azimuth:F0}";
            GetViewport().GetTexture().GetImage().SavePng($"{args[0]}/{name}.png");
            GD.Print($"  {name,-30} {mi.Mesh.GetSurfaceCount()} surface(s)");
            root.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        GetTree().Quit();
    }

    private void BuildLighting()
    {
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("14161d"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("7a8296"),
                AmbientLightEnergy = 1.0f,
            }
        });
        var key = new DirectionalLight3D { LightEnergy = 2.2f };
        key.RotationDegrees = new Vector3(-42, -55, 0);
        AddChild(key);
        var fill = new DirectionalLight3D { LightEnergy = 0.8f };
        fill.RotationDegrees = new Vector3(-25, 120, 0);
        AddChild(fill);
    }

    private static T? Find<T>(Node from) where T : Node
    {
        if (from is T hit) return hit;
        foreach (var c in from.GetChildren()) if (Find<T>(c) is T f) return f;
        return null;
    }
}
