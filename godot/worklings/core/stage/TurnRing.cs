using Godot;

namespace Worklings.Core.Stage;

/// The mark under the Workling that means *it is your turn*.
///
/// **The one cue the player had was in the wrong place.** The command bar goes
/// from dim to lit when a round opens, and it sits at the bottom of the frame
/// while the player is looking at their Workling in the middle of it. The foe
/// has declared its move over its own head since `IntentBadge` landed, so the
/// Workling having nothing was also an asymmetry the eye notices: one creature
/// is clearly saying something and the other is just standing there.
///
/// A ground ring rather than an arrow overhead, deliberately. Overhead is the
/// foe's register now — two badges in the same band of the frame compete, and
/// the one that matters most is the one you are about to answer. Under the feet
/// is a different register entirely, and it is what "waiting on you" looks like
/// in every game that has ever had to say it.
///
/// Two pieces, which is what Nikhil asked for: a solid rim, and a subtle glow
/// under the body. The rim is the statement and the glow is what stops it
/// reading as a decal lying on the floor.
///
/// **World-space, unlike the badge.** The badge is screen-space because a
/// Monolith at 7.50 units and a Scamp at 2.40 need the same legible icon. This
/// is the opposite case: the ring is a thing on the floor at the creature's
/// feet, and it should be as big as the creature is.
public sealed class TurnRing
{
    /// How many segments the rim is drawn in. Enough that it reads as a circle
    /// rather than as a polygon at the camera's distance.
    private const int Segments = 72;

    /// The rim's thickness, as a fraction of its radius.
    private const float Thickness = 0.055f;

    /// Clear of the floor plane. The floor's top face sits at y=0, and
    /// z-fighting across a surface this large shimmers over the whole frame.
    private const float Height = 0.04f;

    /// How long the ring takes to arrive and to leave. Short, but not instant —
    /// a mark that pops on reads as a bug, and the round has just changed hands.
    private const double FadeSeconds = 0.22;

    /// The breath: how far the ring swells, and how long one cycle takes.
    ///
    /// Slow on purpose. This is a state, not an event — it says "still your
    /// turn" for as long as the player takes, and anything quick enough to read
    /// as animation would nag.
    private const float Swell = 0.045f;
    private const double BreathSeconds = 2.4;

    private readonly MeshInstance3D _rim;
    private readonly MeshInstance3D _glow;
    private readonly StandardMaterial3D _rimMaterial;
    private readonly StandardMaterial3D _glowMaterial;
    private readonly Node3D _root;

    private StageActor? _actor;
    private Color _energy = Colors.White;
    private float _radius = 1f;
    private bool _wanted;
    private double _alpha;
    private double _breath;

    public TurnRing(Node3D world)
    {
        _root = new Node3D { Visible = false };
        world.AddChild(_root);

        _rimMaterial = VfxMaterials.Additive();
        _rim = new MeshInstance3D
        {
            Mesh = Annulus(),
            MaterialOverride = _rimMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _root.AddChild(_rim);

        // The glow is the same soft blob every ground mark uses, run very dim.
        // Its job is to put light on the floor inside the rim so the rim has
        // something to be the edge of.
        _glowMaterial = VfxMaterials.Additive();
        _glowMaterial.AlbedoTexture = VfxMaterials.SoftBlob();
        _glow = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(2, 2) },
            MaterialOverride = _glowMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _root.AddChild(_glow);
    }

    /// A flat ring of unit radius, built once and scaled per creature.
    ///
    /// Static rather than rebuilt per frame like `Shockwave`'s rim — that one
    /// wobbles because a perfect expanding circle reads as a primitive, and this
    /// one is *meant* to read as a mark rather than as fire.
    private static ArrayMesh Annulus()
    {
        // Built as explicit arrays rather than drawn into an `ImmediateMesh` and
        // read back. `SurfaceGetArrays` on an ImmediateMesh does not hand back
        // what was drawn into it, so the first version of this baked an empty
        // surface: the ring was in the tree, positioned and lit, and drew
        // nothing. The glow beside it was visible, which is what made it look
        // like a colour problem rather than a missing mesh.
        var vertices = new Vector3[(Segments + 1) * 2];
        for (int i = 0; i <= Segments; i++)
        {
            float angle = Mathf.Tau * i / Segments;
            var direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            vertices[i * 2] = direction * (1f - Thickness);
            vertices[i * 2 + 1] = direction * (1f + Thickness);
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.TriangleStrip, arrays);
        return mesh;
    }

    /// Whether the ring should be up, and under whom.
    ///
    /// Declarative and called every frame rather than shown and hidden at the
    /// phase changes: the ring means exactly "the fight is waiting on the
    /// player", which is one boolean the scene already has, and a pair of
    /// show/hide calls scattered through the round is how a cue gets left on
    /// over a corpse.
    public void Set(bool wanted, StageActor? actor, Color energy, float stageHeight)
    {
        _wanted = wanted && actor is not null;
        if (actor is null) return;
        _actor = actor;
        _energy = energy;
        // As wide as the creature is tall is too wide — a Monolith would be
        // ringed to the edges of the floor. Just past the feet is what reads as
        // standing in it.
        _radius = Mathf.Clamp(stageHeight * 0.38f, 0.7f, 2.6f);
    }

    public void Tick(double delta)
    {
        double target = _wanted ? 1 : 0;
        if (_alpha < target) _alpha = Mathf.Min(target, _alpha + delta / FadeSeconds);
        else if (_alpha > target) _alpha = Mathf.Max(target, _alpha - delta / FadeSeconds);

        _root.Visible = _alpha > 0.001;
        if (!_root.Visible || _actor is null) return;

        _breath += delta;
        float breath = 1f + Swell * Mathf.Sin((float)(_breath / BreathSeconds * Mathf.Tau));

        // Follows the body rather than sitting where it was placed. The ring is
        // only up while nobody is moving, but "only" is a claim about the rest
        // of the scene, and a cue that can be left behind by a lunge will be.
        // Global, not local. The actors hang under the scene's `Party` and `Foe`
        // nodes and the ring hangs under the scene itself, so reading the local
        // position put the Workling's ring on the floor under the foe.
        var at = _actor.Root.GlobalPosition;
        _root.Position = new Vector3(at.X, Height, at.Z);

        float radius = _radius * breath;
        _rim.Scale = new Vector3(radius, 1, radius);
        // The glow is a plane of size 2, so half the scale of the radius puts
        // its edge on the rim — and a little past it, because a soft falloff
        // that ends exactly at the rim leaves a dark gap inside the line.
        float glow = radius * 0.62f;
        _glow.Scale = new Vector3(glow, 1, glow);

        _rimMaterial.AlbedoColor = _energy with { A = (float)_alpha };
        // A seventh of the rim's strength. It is meant to be felt rather than
        // seen: at parity the pair reads as a spotlight, which says "look here"
        // where the ring says "answer".
        _glowMaterial.AlbedoColor = _energy with { A = (float)_alpha * 0.14f };
    }
}
