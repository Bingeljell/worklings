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

    public ImpactFrames(Camera3D camera, Node3D dustParent)
    {
        _camera = camera;
        _cameraRest = camera.Position;
        _dustParent = dustParent;
    }

    /// True while the world is held on a contact frame. The scene checks this
    /// and stops advancing the fight, which is what makes it a freeze rather
    /// than a slow-motion.
    public bool IsHitStopped => _stopRemaining > 0;

    /// Fire the whole reaction. `severity` is 0..1.
    public void Strike(StageActor victim, Vector3 fromDirection, double severity, bool crit)
    {
        severity = System.Math.Clamp(severity, 0, 1);
        double weight = crit ? System.Math.Min(1, severity + 0.35) : severity;

        _stopRemaining = HitStopBase + HitStopPerSeverity * weight;
        _shakeStrength = ShakeBase + ShakePerSeverity * (float)weight;
        _shakeRemaining = ShakeDuration;

        _knocked = victim;
        _knockDirection = fromDirection.Normalized();
        _knockStrength = KnockbackBase + KnockbackPerSeverity * (float)weight;
        _knockRemaining = 0.28;

        victim.Play(ActorAction.Wince);
        SpawnDust(victim.Root.Position, weight);
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

    /// A one-shot puff at the point of contact. Built in code rather than as a
    /// scene file so the whole effect stays readable in one place while it is
    /// still being tuned.
    private void SpawnDust(Vector3 at, double weight)
    {
        var particles = new GpuParticles3D
        {
            Amount = 8 + (int)(weight * 20),
            Lifetime = 0.7,
            OneShot = true,
            Explosiveness = 1.0f,
            Position = at + new Vector3(0, 0.35f, 0),
        };

        var material = new ParticleProcessMaterial
        {
            Direction = new Vector3(0, 1, 0),
            Spread = 65,
            InitialVelocityMin = 1.2f + (float)weight * 2.0f,
            InitialVelocityMax = 2.4f + (float)weight * 3.0f,
            Gravity = new Vector3(0, -4.5f, 0),
            ScaleMin = 0.06f,
            ScaleMax = 0.16f + (float)weight * 0.12f,
            Damping = new Vector2(1.5f, 3.0f),
        };
        material.SetParam(ParticleProcessMaterial.Parameter.AngularVelocity, new Vector2(-90, 90));
        particles.ProcessMaterial = material;

        var mesh = new QuadMesh { Size = new Vector2(0.28f, 0.28f) };
        mesh.Material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.62f, 0.48f, 0.33f, 0.85f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
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
