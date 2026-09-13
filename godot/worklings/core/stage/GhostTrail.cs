using System.Collections.Generic;
using Godot;

namespace Worklings.Core.Stage;

/// The streak an attacker leaves behind as it closes on its target.
///
/// A ghost is a *static* snapshot of the character's own skinned mesh, frozen at
/// a pose it held a moment ago, tinted to its family energy and fading with age.
/// Not a second skinned body: no skeleton, no AnimationPlayer, no per-frame
/// skinning. The Ram carries 283 joints, and ten live copies of it would have
/// been a real cost where ten static ones are not.
///
/// **The poses are baked once, not per swing.** `BakeMeshFromCurrentSkeletonPose`
/// reads mesh data back from the GPU, and the probe priced that at 4.8-8.1ms with
/// a floor decimation does not touch — ten of those inside one attack is a frame
/// hitch, not an effect. But the trail always covers the same window of the same
/// clip, so the poses are identical every time the character swings. Baking them
/// at the briefing screen and replaying them costs nothing in the fight.
///
/// What differs per swing is only *where* each ghost is placed, which comes from
/// the travel vector the lunge is running.
public sealed class GhostTrail
{
    /// How long a ghost lives after it appears. Longer than the travel itself, so
    /// the oldest of them are still fading when the blow lands and the trail is
    /// present in the contact frame rather than gone by it.
    private const double LifeSeconds = 0.30;

    /// Peak opacity of the newest ghost. The solid body has to stay the clearest
    /// thing in frame.
    ///
    /// Much lower than the stills wanted, because the stills were shot against
    /// black. Additive blending adds to whatever is behind it, and the Cache
    /// Warren floor is bright tan — the same alpha that read as a cool streak on
    /// black blew the Ram out to a white silhouette on sand.
    private const float PeakAlpha = 0.09f;

    /// How far along the travel the newest ghost sits, as a share of the whole.
    ///
    /// Not 1.0: the trail has to stop short of the body. Spawning a ghost at the
    /// point of arrival puts the brightest end of the streak directly on top of
    /// the character, which is exactly where it must not be.
    ///
    /// It has to stop a long way short, because the gap is measured in travel
    /// and the overlap is measured in body lengths. The Ram covers about two of
    /// its own lengths, so a tenth of the travel is nowhere near a body clear —
    /// at 0.78 its hindquarters were still inside the brightest ghost and read
    /// as white.
    private const float NewestAt = 0.55f;

    private readonly StageActor _actor;
    private readonly Node3D _parent;
    private readonly Color _energy;

    private readonly List<ArrayMesh> _poses = new();
    private readonly List<MeshInstance3D> _pool = new();
    private readonly List<double> _age = new();
    private readonly List<bool> _live = new();

    /// The mesh's transform with the actor on its mark, read at the start of each
    /// swing rather than once at bake time.
    ///
    /// It has to be captured, not read live: the bake is in the mesh's own space
    /// and the actor's root moves during the lunge, so reading it per ghost would
    /// place every one of them on top of the body instead of behind it. And it
    /// has to be captured per swing, because a foe model is shared — the Scamp is
    /// a Flicker at 0.55 — so the same mesh appears at different scales in
    /// different encounters.
    private Transform3D _restTransform = Transform3D.Identity;

    private Vector3 _travel;
    private int _shown;

    public bool IsBaked { get; private set; }

    public GhostTrail(StageActor actor, Node3D parent)
    {
        _actor = actor;
        _parent = parent;
        _energy = FamilyEnergy.Of(FamilyEnergy.For(actor.ModelName));
    }

    /// Bakes the poses for this character's attack, once.
    ///
    /// Call it somewhere a stall does not show — the briefing screen. Calling it
    /// mid-fight works and costs one visible hitch on the first swing.
    public void Bake()
    {
        if (IsBaked) return;
        IsBaked = true;

        var mesh = _actor.Mesh;
        var player = _actor.Player;
        var clip = _actor.Animations.Name(ActorAction.Attack);
        if (mesh == null || player == null || clip == null || !player.HasAnimation(clip)) return;

        int count = _actor.Animations.GhostCount;
        double length = player.GetAnimation(clip).Length;
        double contact = length * _actor.Animations.AttackImpactPoint;
        double window = _actor.Animations.TravelSeconds;

        // Oldest first, so the newest is added last and draws on top. Ghost k
        // holds the pose the body was in when it was k/count of the way through
        // the travel — the trail is the same animation seen a few frames late.
        for (int k = 0; k < count; k++)
        {
            double at = contact - window + window * Along(k, count);
            player.Play(clip);
            player.Seek(Mathf.Max(0, at), update: true);
            player.Pause();

            var baked = new ArrayMesh();
            mesh.BakeMeshFromCurrentSkeletonPose(baked);
            _poses.Add(baked);

            var ghost = new MeshInstance3D
            {
                Mesh = baked,
                Visible = false,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                MaterialOverride = Material((float)k / count),
            };
            _parent.AddChild(ghost);
            _pool.Add(ghost);
            _age.Add(0);
            _live.Add(false);
        }

        player.Stop();
    }

    /// Arms the trail for one swing. `travel` is the full displacement the lunge
    /// will cover; ghosts are placed along it as the body passes them.
    public void Begin(Vector3 travel)
    {
        Clear();
        _travel = travel;
        _shown = 0;
        if (_actor.Mesh != null) _restTransform = _actor.Mesh.GlobalTransform;
    }

    /// `progress` is how far through its travel the attacker is, 0..1. Every
    /// ghost the body has passed appears at the point it was passed, which is
    /// what makes the spacing the character's real speed rather than a fixed gap.
    public void Advance(float progress)
    {
        while (_shown < _pool.Count && Along(_shown, _pool.Count) <= progress)
        {
            float at = (float)Along(_shown, _pool.Count);
            // The same ease-out the lunge runs, so a ghost sits where the body
            // actually was: bunched at the start, stretched as it commits.
            float eased = 1f - Mathf.Pow(1f - at, 3f);
            var t = _restTransform;
            t.Origin += _travel * eased;

            var ghost = _pool[_shown];
            ghost.Transform = t;
            ghost.Visible = true;
            _age[_shown] = 0;
            _live[_shown] = true;
            _shown++;
        }
    }

    /// Fades what is out there. Driven on real time rather than fight time, so
    /// hit-stop freezes the fight and lets the trail keep dissipating — the same
    /// split impact frames already use for shake and dust.
    public void Tick(double delta)
    {
        for (int i = 0; i < _pool.Count; i++)
        {
            if (!_live[i]) continue;
            _age[i] += delta;
            double life = _age[i] / LifeSeconds;
            if (life >= 1)
            {
                _pool[i].Visible = false;
                _live[i] = false;
                continue;
            }
            // Age within the trail and age in seconds both dim a ghost: the one
            // gives the streak its direction, the other makes it go away.
            float rank = (float)i / _pool.Count;
            SetAlpha(_pool[i], Fade(rank) * (1f - (float)life));
        }
    }

    /// Where ghost `k` of `count` sits along the travel. Spread across the first
    /// NewestAt of it rather than the whole, which is what leaves the gap.
    private static double Along(int k, int count) =>
        count <= 1 ? 0 : NewestAt * k / (count - 1.0);

    public void Clear()
    {
        for (int i = 0; i < _pool.Count; i++)
        {
            _pool[i].Visible = false;
            _live[i] = false;
            _age[i] = 0;
        }
        _shown = 0;
    }

    /// A ghost's look: unshaded and additive so it reads as light rather than as
    /// a second body, tinted to the family energy that already drives hit sparks
    /// and damage numbers.
    private StandardMaterial3D Material(float rank) => new()
    {
        AlbedoColor = Tint(rank),
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        CullMode = BaseMaterial3D.CullModeEnum.Back,
    };

    private Color Tint(float rank)
    {
        var tint = _energy.Lerp(new Color(1, 1, 1), 0.35f);
        tint.A = Fade(rank);
        return tint;
    }

    /// Older ghosts are dimmer. `rank` is 0 for the oldest, 1 for the newest.
    private static float Fade(float rank) => PeakAlpha * Mathf.Pow(rank, 1.2f);

    private static void SetAlpha(MeshInstance3D ghost, float alpha)
    {
        if (ghost.MaterialOverride is not StandardMaterial3D m) return;
        var c = m.AlbedoColor;
        c.A = alpha;
        m.AlbedoColor = c;
    }
}
