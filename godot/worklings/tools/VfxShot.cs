using System.Collections.Generic;
using Godot;
using Worklings.Core.Stage;

/// Renders one attack per character as a frame sequence, so the signature
/// effects can be judged as motion rather than as stills.
///
/// **Builds its own diorama instead of running CacheWarrenScene.** Two reasons.
/// The scene owns a save file — it loads a Workling on open and writes one back
/// when a run resolves — and a capture tool has no business anywhere near the
/// real pet. And a delve is four encounters of press-your-luck built from a
/// clock seed, which is a poor way to ask for "the Pangolin's slam, from the
/// front, twice". The stage scene is instanced whole, so the floor, the torches
/// and the locked camera are the real ones; only the fight is staged.
///
/// **Stepped at a fixed delta, not in real time.** Run with `--fixed-fps 60`
/// and every system here advances by exactly 1/60 per rendered frame, so the
/// sequence is the same every time it is captured and the PNGs are evenly
/// spaced in scene time. FightShot, which this replaces for this purpose, waits
/// on wall-clock timers and photographs whatever the machine happened to be
/// showing — fine for checking wiring, useless for judging a 0.3s effect.
public partial class VfxShot : Node3D
{
    private const double Step = 1.0 / 60.0;

    private static readonly string OutRoot =
        OS.GetEnvironment("WORKLINGS_VFX_OUT") is { Length: > 0 } dir
            ? dir
            : ProjectSettings.GlobalizePath("user://vfx-shots");

    /// One capture: who swings, at what, and whether the signature layer is on.
    private readonly record struct Take(
        string Name, string Attacker, string Defender, bool Effects = true,
        bool Big = false, double Tail = 1.9);

    /// Render the stage darkened as well as as-authored.
    ///
    /// Not a decision this tool gets to make. The first capture answered the
    /// lighting question the reference shots raised, and answered it the other
    /// way round from the guess: the Cache Warren's floor is bright sand under a
    /// warm key, not the near-black stone every Path of Exile 2 shot is standing
    /// on, and an additive effect over ground that is already close to white
    /// adds almost nothing. Every telegraph in the first pass was invisible for
    /// that reason alone.
    ///
    /// Darkening the stage is therefore the single biggest lever on whether any
    /// of this reads — and it changes the look of the whole dungeon, which is an
    /// art-direction call and not a VFX one. So both get rendered and the choice
    /// stays with the person who owns it.
    private static readonly bool Dark = OS.GetEnvironment("WORKLINGS_VFX_DARK") == "1";

    private static readonly Take[] Takes =
    {
        // The three Nikhil asked for, in the order the references were sent.
        new("01-ram-lightning",    "tempest_ram",        "forest_flicker"),
        new("02-pangolin-fire",    "clockwork_pangolin", "tempest_ram"),
        new("03-flicker-ghosts",   "forest_flicker",     "tempest_ram"),
        // The fourth, which the Snag's mid-clip contact makes the odd one out.
        new("04-snag-roots",       "snag",               "tempest_ram"),

        // A crit, because `big` scales rather than replaces and that claim
        // should be checkable.
        new("05-ram-lightning-crit", "tempest_ram",      "forest_flicker", Big: true),

        // The A/B. Same swing, signature layer off — impact frames and the ghost
        // trail alone, which is what shipped before tonight. Without this the
        // question "is the new layer earning its place" has no answer.
        new("06-ram-no-effects",   "tempest_ram",        "forest_flicker", Effects: false),
    };

    /// Straight out of cache_warren.tscn. Copied rather than read off the scene
    /// because the marks live in the scene file as baked transforms, and a
    /// capture placed anywhere else is not the fight.
    private static readonly Dictionary<string, float> Scales = new()
    {
        ["tempest_ram"] = 3.7f,
        ["forest_flicker"] = 2.8f,
        ["clockwork_pangolin"] = 2.8f,
        ["snag"] = 7.0f,
    };

    private static readonly Vector3 PartyMark = new(-3, 0, 8.5f);
    private static readonly Vector3 FoeMark = new(0, 0, -2);
    private const float PartyYaw = 181.64f;
    private const float FoeYaw = 3.66f;

    private Node3D _stage = null!;
    private Camera3D _camera = null!;

    public override async void _Ready()
    {
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        DirAccess.MakeDirRecursiveAbsolute(OutRoot);

        _stage = GD.Load<PackedScene>("res://scenes/dungeon_stage.tscn").Instantiate<Node3D>();
        AddChild(_stage);
        _camera = _stage.GetNode<Camera3D>("StageCamera");
        _camera.Current = true;
        if (Dark) Darken();

        foreach (var take in Takes) await Capture(take);

        GD.Print($"sequences written to {OutRoot}");
        GetTree().Quit();
    }

    private async System.Threading.Tasks.Task Capture(Take take)
    {
        // The attacker takes whichever mark its role implies: the party Workling
        // swings from the bottom-left, a foe from the far side. Both are staged
        // every take so the shot always has something to hit.
        bool attackerIsParty = take.Attacker == "tempest_ram" && take.Defender != "tempest_ram";
        var attacker = Spawn(take.Attacker, attackerIsParty);
        var defender = Spawn(take.Defender, !attackerIsParty);

        var impact = new ImpactFrames(_camera, this, this);
        var vfx = new AbilityVfx(this, _camera) { Enabled = take.Effects };
        var lunge = new AttackLunge { Travel = true };
        var numbers = new DamageNumbers(this);
        var trail = new GhostTrail(attacker, this);

        attacker.Play(ActorAction.Idle, loop: true);
        defender.Play(ActorAction.Idle, loop: true);
        // Baked before the clock starts, exactly as the scene does it on the
        // briefing screen: the readback stall is a frame hitch, and a hitch
        // inside the capture would land in the sequence as a repeated frame.
        trail.Bake();
        attacker.Play(ActorAction.Idle, loop: true);

        var directory = $"{OutRoot}/{take.Name}";
        DirAccess.MakeDirRecursiveAbsolute(directory);

        double contact = attacker.AttackImpactDelay();
        // Half a second of idle in front, so the sequence opens on a standing
        // character and the wind-up has something to be a departure from.
        const double Lead = 0.5;
        double total = Lead + AttackLunge.DurationFor(contact) + take.Tail;
        int frames = (int)System.Math.Round(total / Step);
        bool started = false;
        double clock = 0;
        var energy = FamilyEnergy.Of(FamilyEnergy.For(take.Attacker));

        for (int f = 0; f < frames; f++)
        {
            if (!started && clock >= Lead)
            {
                started = true;
                attacker.Play(ActorAction.Attack);
                vfx.Begin(AbilitySignatures.For(take.Attacker), attacker, defender,
                          contact, energy, take.Big);
                lunge.Begin(attacker, defender, contact, trail: trail, onContact: () =>
                {
                    var direction = defender.Root.Position - attacker.Root.Position;
                    double severity = take.Big ? 0.55 : 0.28;
                    impact.Strike(defender, direction, severity, take.Big, energy);
                    numbers.Spawn(defender.Root.Position, take.Big ? 41 : 17, energy, take.Big);
                });
            }

            double scale = impact.IsHitStopped ? 0 : 1;
            impact.Tick(Step);
            trail.Tick(Step);
            lunge.Tick(Step, scale);
            vfx.Tick(Step, scale);
            clock += Step;

            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng($"{directory}/{f:D4}.png");
        }

        GD.Print($"  {take.Name,-24} {frames} frames  contact {contact:F2}s"
               + (take.Effects ? "" : "  (signature layer off)"));

        vfx.Clear();
        trail.Clear();
        attacker.Root.QueueFree();
        defender.Root.QueueFree();
        // The impact flash and the damage numbers parent themselves onto this
        // node, so they survive the actors and would bleed into the next take.
        foreach (var child in GetChildren())
            if (child is CanvasLayer or Label3D or GpuParticles3D) child.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    /// The darkened stage: dim the floor's albedo and take the key light down.
    ///
    /// Deliberately a tint on the existing material rather than a new floor
    /// texture. The question being asked is only "does the effect layer need
    /// darker ground to read", and swapping the art would answer a different
    /// and much larger question at the same time.
    private void Darken()
    {
        var floor = _stage.GetNode<MeshInstance3D>("Floor");
        if (floor.MaterialOverride is StandardMaterial3D material)
            material.AlbedoColor = new Color(0.30f, 0.29f, 0.34f);
        _stage.GetNode<DirectionalLight3D>("KeyLight").LightEnergy = 0.85f;
        _stage.GetNode<OmniLight3D>("TorchLight").LightEnergy = 2.6f;
    }

    private StageActor Spawn(string model, bool party)
    {
        var root = GD.Load<PackedScene>($"res://assets/characters/{model}.glb").Instantiate<Node3D>();
        float scale = Scales.TryGetValue(model, out var s) ? s : 3f;
        var transform = Transform3D.Identity
            .Rotated(Vector3.Up, Mathf.DegToRad(party ? PartyYaw : FoeYaw))
            .Scaled(new Vector3(scale, scale, scale));
        transform.Origin = party ? PartyMark : FoeMark;
        root.Transform = transform;
        AddChild(root);
        return new StageActor(root, model, ActorAnimations.For(model)!);
    }
}
