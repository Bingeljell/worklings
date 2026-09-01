using Godot;
using Worklings.Core.Stage;

namespace Worklings.Core.Stage;

/// The weight of a landed hit: hit-stop, camera shake, a knockback nudge and a
/// burst of dust.
///
/// Named for the animation term — the held frame on contact that sells force.
/// The single biggest lever on whether combat reads as weighty rather than
/// merely animated, and the reason actors had to become real scene nodes: all
/// four effects are transforms and particles on nodes, impossible while the
/// combatants were SwiftUI columns drawn over the 3D view.
///
/// Everything is scaled by the blow's severity, so a chip hit and a crit do not
/// feel the same. Severity is damage as a share of the victim's max HP, which
/// keeps it meaningful as the numbers grow — a flat threshold would stop
/// registering once HP pools get large.
public sealed class ImpactFrames
{
    private readonly Camera3D _camera;
    private readonly Vector3 _cameraRest;
    private readonly Node3D _dustParent;

    /// A full-screen white quad, flashed for a few frames on contact.
    ///
    /// The anime "impact frame": two or three near-white frames that interrupt
    /// smooth motion so the eye registers a blow it would otherwise slide past.
    /// The highest impact-per-effort effect in the whole vocabulary — it is one
    /// ColorRect — and it works precisely because it is crude.
    private readonly ColorRect _flash;

    /// How long the world freezes on contact. Short — long enough to read as a
    /// held frame, not long enough to feel like a stutter.
    private const double HitStopBase = 0.055;
    private const double HitStopPerSeverity = 0.09;

    /// Shake amplitude in world units. The camera sits ~28 units out with a 32
    /// degree vertical FOV, so the visible frame is ~16 units tall: one unit is
    /// roughly 67 pixels at 1080p. The first pass used 0.06-0.34, which peaks
    /// around 20px and decays quadratically — read as "barely any shake", which
    /// it was.
    private const float ShakeBase = 0.18f;
    private const float ShakePerSeverity = 0.75f;
    private const double ShakeDuration = 0.3;

    private const float KnockbackBase = 0.05f;
    private const float KnockbackPerSeverity = 0.35f;

    private double _stopRemaining;
    private double _shakeRemaining;
    private float _shakeStrength;
    private StageActor? _knocked;
    private Vector3 _knockDirection;
    private double _knockRemaining;
    private float _knockStrength;

    public ImpactFrames(Camera3D camera, Node3D dustParent, Node hudParent)
    {
        _camera = camera;
        _cameraRest = camera.Position;
        _dustParent = dustParent;

        var layer = new CanvasLayer { Layer = 2 };   // above the HUD
        hudParent.AddChild(layer);
        _flash = new ColorRect
        {
            Color = new Color(1, 1, 1, 0),
            AnchorRight = 1,
            AnchorBottom = 1,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        layer.AddChild(_flash);
    }

    /// True while the world is held on a contact frame. The scene checks this
    /// and stops advancing the fight, which is what makes it a freeze rather
    /// than a slow-motion.
    public bool IsHitStopped => _stopRemaining > 0;

    /// Fire the whole reaction. `severity` is 0..1. `energy` is the attacker's
    /// family colour, which tints the spark and the flash so a hit reads as
    /// belonging to whoever threw it.
    public void Strike(StageActor victim, Vector3 fromDirection, double severity, bool crit,
                       Color energy)
    {
        severity = System.Math.Clamp(severity, 0, 1);
        double weight = crit ? System.Math.Min(1, severity + 0.35) : severity;
        Flash(weight, crit, energy);

        _stopRemaining = HitStopBase + HitStopPerSeverity * weight;
        _shakeStrength = ShakeBase + ShakePerSeverity * (float)weight;
        _shakeRemaining = ShakeDuration;

        _knocked = victim;
        _knockDirection = fromDirection.Normalized();
        _knockStrength = KnockbackBase + KnockbackPerSeverity * (float)weight;
        _knockRemaining = 0.28;

        victim.Play(ActorAction.Wince);
        SpawnSpark(victim.Root.Position, weight, energy, crit);
    }

    /// The impact frame itself: up hard, out fast. Tinted toward the attacker's
    /// family rather than pure white, so even the loudest moment stays
    /// identifiable — an Elemental crit flashes violet-white, a Relicborn one
    /// gold-white.
    private void Flash(double weight, bool crit, Color energy)
    {
        var tint = energy.Lerp(new Color(1, 1, 1), crit ? 0.55f : 0.75f);
        float peak = (float)(0.30 + 0.45 * weight);
        double hold = crit ? 0.05 : 0.03;
        double fade = crit ? 0.16 : 0.10;

        _flash.Color = new Color(tint.R, tint.G, tint.B, peak);
        var tween = _flash.CreateTween();
        tween.TweenInterval(hold);
        tween.TweenProperty(_flash, "color:a", 0.0f, fade).SetEase(Tween.EaseType.Out);
    }

    /// Advance the effects. Called with the real frame delta even during
    /// hit-stop — the freeze applies to the *fight*, not to the reaction
    /// animating its way out.
    public void Tick(double delta)
    {
        if (_stopRemaining > 0) _stopRemaining -= delta;

        if (_shakeRemaining > 0)
        {
            _shakeRemaining -= delta;
            float falloff = (float)System.Math.Max(0, _shakeRemaining / ShakeDuration);
            // Linear falloff, not quadratic: squaring killed most of the motion
            // in the first few frames, which is exactly where a shake reads.
            float amount = _shakeStrength * falloff;
            // Displace along the camera's own right/up, not world XY. The stage
            // camera is angled ~40 degrees down and 60 across, so a world-space
            // nudge slides the frame diagonally instead of shaking it.
            var basis = _camera.GlobalTransform.Basis;
            _camera.Position = _cameraRest
                + basis.X * (float)GD.RandRange(-amount, amount)
                + basis.Y * (float)GD.RandRange(-amount, amount);
            if (_shakeRemaining <= 0) _camera.Position = _cameraRest;
        }

        if (_knocked != null && _knockRemaining > 0)
        {
            _knockRemaining -= delta;
            // Out fast, back slow: the recoil is the part that reads as weight.
            float t = (float)System.Math.Max(0, _knockRemaining / 0.28);
            _knocked.SetOffset(_knockDirection * _knockStrength * t * t);
            if (_knockRemaining <= 0) { _knocked.ClearOffset(); _knocked = null; }
        }
    }

    /// A burst at the point of contact, in the attacker's family colour.
    ///
    /// Replaces the brown dust puff this started as, which read as scenery
    /// kicked up rather than as a hit — the colour was doing the opposite of
    /// its job by blending into the floor. Emissive and unshaded so it reads
    /// against the cave whatever the lighting is doing.
    private void SpawnSpark(Vector3 at, double weight, Color energy, bool crit)
    {
        var particles = new GpuParticles3D
        {
            Amount = 14 + (int)(weight * 30),
            Lifetime = crit ? 0.85 : 0.55,
            OneShot = true,
            Explosiveness = 1.0f,
            Position = at + new Vector3(0, 0.35f, 0),
        };

        var material = new ParticleProcessMaterial
        {
            Direction = new Vector3(0, 1, 0),
            // Wide spread and low gravity: a spark scatters outward from the
            // blow, where dust fell downward from it.
            Spread = 180,
            InitialVelocityMin = 2.6f + (float)weight * 3.5f,
            InitialVelocityMax = 5.0f + (float)weight * 6.0f,
            Gravity = new Vector3(0, -2.2f, 0),
            ScaleMin = 0.05f,
            ScaleMax = 0.13f + (float)weight * 0.14f,
            Damping = new Vector2(3.0f, 6.5f),
        };
        material.SetParam(ParticleProcessMaterial.Parameter.AngularVelocity, new Vector2(-90, 90));
        particles.ProcessMaterial = material;

        var mesh = new QuadMesh { Size = new Vector2(0.22f, 0.22f) };
        var hot = FamilyEnergy.Lift(crit ? FamilyEnergy.Crit : energy, 0.35f);
        mesh.Material = new StandardMaterial3D
        {
            AlbedoColor = new Color(hot.R, hot.G, hot.B, 0.95f),
            EmissionEnabled = true,
            Emission = hot,
            EmissionEnergyMultiplier = crit ? 3.0f : 1.9f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
        };
        particles.DrawPass1 = mesh;

        _dustParent.AddChild(particles);
        particles.Emitting = true;

        // Clean up after the burst; without this every hit leaks a node.
        var timer = _dustParent.GetTree().CreateTimer(1.6);
        timer.Timeout += () => { if (GodotObject.IsInstanceValid(particles)) particles.QueueFree(); };
    }
}
