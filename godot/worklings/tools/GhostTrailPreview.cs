using System.Collections.Generic;
using Godot;
using Worklings.Core.Stage;

/// Renders the motion trail as stills, so the effect can be judged before it is
/// built.
///
/// The bake probe answered what a ghost *costs*; it drew nothing. This answers
/// the other half — whether a trail reads at all — and does it the cheap way:
/// every pose is baked up front and placed as a static MeshInstance3D, then the
/// viewport is captured. No per-frame baking, so the readback stall that rules
/// out the runtime version is irrelevant to a still.
///
/// Two characters, because they fail differently. The Snag is rooted and only
/// its whip moves, so its ghosts sit on the spot and the trail is pure pose
/// history. The Ram travels, which is open #8 — "AttackersTravel reads as
/// sliding" — so its ghosts are spread along the same eased approach curve
/// AttackLunge actually uses, and the shot is a direct answer to whether a
/// streak fixes the slide.
public partial class GhostTrailPreview : Node
{
    /// The locked Cache Warren angle (docs: dungeon camera), so the trail is
    /// judged from the angle the fight is actually watched from rather than a
    /// flattering three-quarter view.
    private const float Azimuth = 59.7f;
    private const float Elevation = 39.7f;
    private const float Fov = 32f;

    private static readonly string OutputDir =
        "/private/tmp/claude-501/-Users-nikhilshahane-projects-worklings/fcca5d72-c899-42ad-984a-5f3b10dd870d/scratchpad/trail-shots";

    private Camera3D _camera = null!;

    public override async void _Ready()
    {
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        DirAccess.MakeDirRecursiveAbsolute(OutputDir);
        BuildLighting();

        _camera = new Camera3D { Fov = Fov, Far = 500 };
        AddChild(_camera);

        // The Snag: rooted, so the ghosts stack on the spot and the only thing
        // the trail can show is the whip's own arc.
        // Textured and plain grey, side by side. The investigation's own first
        // move was to strip textures — it separates a broken surface from a
        // broken bake, and they look alike at a glance.
        await Shot("00-snag-grey", "snag", ghosts: 0, travel: Vector3.Zero, grey: true);
        await Shot("00-flicker-grey", "forest_flicker", ghosts: 0, travel: Vector3.Zero, grey: true);
        await Shot("01-snag-no-trail", "snag", ghosts: 0, travel: Vector3.Zero);
        await Shot("02-snag-trail", "snag", ghosts: 8, travel: Vector3.Zero, step: 0.022, alpha: 0.30f);

        // The Ram mid-approach, which is where the sliding complaint lives.
        //
        // Three versions, because the first run showed the tuning matters more
        // than the technique: widely spaced ghosts read as a row of separate
        // rams rather than one fast one. Overlap is what turns copies into a
        // streak, so the spacing sweep is the actual finding here.
        var lunge = new Vector3(0, 0, -6.5f);
        await Shot("03-ram-no-trail", "tempest_ram", ghosts: 0, travel: lunge);
        await Shot("04-ram-trail-tight", "tempest_ram", ghosts: 10, travel: lunge, step: 0.014, alpha: 0.22f);
        await Shot("05-ram-trail-wide", "tempest_ram", ghosts: 8, travel: lunge, step: 0.045, alpha: 0.55f);

        GD.Print($"shots written to {OutputDir}");
        GetTree().Quit();
    }

    /// One still: a live actor plus `ghosts` baked copies behind it.
    ///
    /// `travel` is how far the attacker has moved by the captured moment. Zero
    /// leaves the ghosts on the spot (a rooted foe); a vector spreads them along
    /// the approach, positioned with AttackLunge's own ease-out so the spacing
    /// is the real one — bunched at the start, stretched as it commits.
    private async System.Threading.Tasks.Task Shot(string name, string model, int ghosts,
                                                   Vector3 travel, double step = 0.03, float alpha = 0.3f,
                                                   bool grey = false)
    {
        var packed = GD.Load<PackedScene>($"res://assets/characters/{model}.glb");
        var root = packed.Instantiate<Node3D>();
        AddChild(root);

        var mi = FindNode<MeshInstance3D>(root);
        var player = FindNode<AnimationPlayer>(root);
        string? clip = ActorAnimations.For(model)?.Name(ActorAction.Attack);
        if (mi == null || player == null || clip == null) { GD.Print($"{name}: cannot build"); return; }

        // Face the way it is going. The raw .glb faces +Z, and cache_warren.tscn
        // spins the party Ram 180 degrees to face the foe — omitting that here
        // moved the model backwards along its own trail, which read as the
        // ghosts leading rather than following.
        if (travel != Vector3.Zero)
            root.Rotation = new Vector3(0, Mathf.Atan2(travel.X, travel.Z), 0);

        double length = player.GetAnimation(clip).Length;
        var energy = FamilyEnergy.Of(FamilyEnergy.For(model));

        // The captured instant. Mid-clip for the Snag, whose whip cracks at 0.50;
        // early for the Ram, because its travel happens during the wind-up.
        double now = travel == Vector3.Zero ? 0.55 : 0.30;

        // Ghosts are baked oldest-first so the newest is drawn last and reads on
        // top. Each is one clip-time step back, which is what a trail is: the
        // same animation, seen a few frames late.
        double StepSeconds = step;
        var made = new List<MeshInstance3D>();
        for (int k = ghosts; k >= 1; k--)
        {
            // Offset by a step and a half so the newest ghost never sits on the
            // live body. Without the gap the brightest end of the trail washes
            // out the character, and the solid body has to stay the clearest
            // thing in frame.
            double back = (k + 1.5) * StepSeconds;
            double t = Mathf.Max(0, now * length - back);
            await Pose(player, clip, t);

            var baked = mi.BakeMeshFromCurrentSkeletonPose();
            float age = (float)k / ghosts;

            var ghost = new MeshInstance3D
            {
                Mesh = baked,
                // The bake is in the mesh's own space, so the ghost inherits the
                // model's transform to land where the character actually is.
                Transform = mi.GlobalTransform,
                MaterialOverride = GhostMaterial(energy, age, alpha),
            };
            // Where the attacker *was*, on AttackLunge's ease-out curve. The
            // ghost covers the same clip-time step back as its pose, so the
            // spacing on screen is the character's real speed rather than an
            // arbitrary gap — which is why widening the step spreads them into
            // separate bodies instead of stretching one streak.
            float progress = Mathf.Max(0, 1f - (float)(back / (ghosts * StepSeconds)));
            ghost.Position += travel * (1f - Mathf.Pow(1f - progress, 3f));
            AddChild(ghost);
            made.Add(ghost);
        }

        await Pose(player, clip, now * length);
        root.Position += travel;
        if (grey)
            mi.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("9aa0a8") };

        FrameOn(mi, travel);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await RenderingServer.Singleton.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        var image = GetViewport().GetTexture().GetImage();
        image.SavePng($"{OutputDir}/{name}.png");
        GD.Print($"  {name,-26} {ghosts} ghosts");

        foreach (var g in made) g.QueueFree();
        root.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    /// A ghost's look: unshaded so it reads as light rather than a second body,
    /// tinted to the family energy that already drives sparks and damage
    /// numbers, and fading with age so the trail has a direction.
    private static StandardMaterial3D GhostMaterial(Color energy, float age, float alpha)
    {
        var tint = energy.Lerp(new Color(1, 1, 1), 0.35f);
        tint.A = alpha * Mathf.Pow(1f - age, 1.6f);
        return new StandardMaterial3D
        {
            AlbedoColor = tint,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            CullMode = BaseMaterial3D.CullModeEnum.Back,
            NoDepthTest = false,
        };
    }

    private async System.Threading.Tasks.Task Pose(AnimationPlayer player, string clip, double time)
    {
        player.Play(clip);
        player.Seek(time, update: true);
        player.Pause();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    /// Puts the camera on the locked dungeon angle at whatever distance fits the
    /// subject. The angle is the fixed part; the models are authored at wildly
    /// different scales, so the radius has to be derived rather than copied.
    private void FrameOn(MeshInstance3D mi, Vector3 travel)
    {
        // Frame the whole streak, not just the live body: the trail extends back
        // along the travel, and the first run cropped it off the bottom of the
        // shot because the camera only knew about the character.
        var aabb = mi.GlobalTransform * mi.GetAabb();
        aabb = aabb.Merge(new Aabb(aabb.Position - travel, aabb.Size));
        var centre = aabb.GetCenter();
        float radius = aabb.Size.Length() * 0.5f;
        float distance = radius / Mathf.Tan(Mathf.DegToRad(Fov * 0.5f)) * 0.72f;

        float az = Mathf.DegToRad(Azimuth);
        float el = Mathf.DegToRad(Elevation);
        var direction = new Vector3(
            Mathf.Cos(el) * Mathf.Sin(az),
            Mathf.Sin(el),
            Mathf.Cos(el) * Mathf.Cos(az));

        _camera.Position = centre + direction * distance;
        _camera.LookAt(centre, Vector3.Up);
    }

    private void BuildLighting()
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color("14161d"),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color("6b7490"),
            AmbientLightEnergy = 1.15f,
        };
        AddChild(new WorldEnvironment { Environment = env });

        var key = new DirectionalLight3D { LightEnergy = 2.4f };
        key.RotationDegrees = new Vector3(-42, -55, 0);
        AddChild(key);

        // A fill from the camera side. Without it the live actor sat darker than
        // its own ghosts, which inverts the whole read — the solid body has to
        // be the brightest thing in frame or the trail looks like the character.
        var fill = new DirectionalLight3D { LightEnergy = 0.9f };
        fill.RotationDegrees = new Vector3(-25, 120, 0);
        AddChild(fill);
    }

    private static T? FindNode<T>(Node from) where T : Node
    {
        if (from is T hit) return hit;
        foreach (var child in from.GetChildren())
            if (FindNode<T>(child) is T found) return found;
        return null;
    }
}
