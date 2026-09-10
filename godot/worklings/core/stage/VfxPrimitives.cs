using Godot;

namespace Worklings.Core.Stage;

/// The vocabulary the ability signatures are assembled from: a bolt, a
/// travelling ring, a mark on the floor, a one-frame light, a volley.
///
/// Five primitives, deliberately, and none of them character-specific. The
/// alternative — one bespoke effect per character — produces four things that
/// cannot be retuned together and share no timing, which is how a combat
/// vocabulary stops being a vocabulary. A new signature should be a new
/// arrangement of these, and a new primitive should only be added when an
/// arrangement genuinely cannot express it.
///
/// **Everything animates by hand, in Advance(), rather than by Tween.** Two
/// reasons, both load-bearing. Hit-stop freezes the fight by passing scale 0
/// down the tick, and a Tween does not know about that — it would keep running
/// through the held frame, which is precisely the frame the freeze exists to
/// hold. And the capture tool steps the scene at a fixed delta to render a
/// sequence; hand-driven interpolation gives the same frames every run, where
/// tweens tied to wall-clock time do not.
public abstract class VfxEffect
{
    /// Set when the effect has played out and its nodes can go. The owner polls
    /// this rather than each effect scheduling its own cleanup, so a cancelled
    /// attack can tear everything down on the spot.
    public bool Done { get; protected set; }

    /// `t` is seconds since this effect started, already scaled by hit-stop.
    public abstract void Advance(double t);

    public abstract void Release();
}

/// Shared material recipes. Additive and unshaded is the house style for
/// anything meant to read as light: the cave is lit warm and dim, and a lit
/// material would have the fight's brightest moments dimmed by the torch that
/// happens to be nearest.
public static class VfxMaterials
{
    public static StandardMaterial3D Additive(bool vertexColour = false) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        VertexColorUseAsAlbedo = vertexColour,
        // Additive geometry stacked on itself (a bolt's core inside its halo,
        // overlapping flame tongues) must not occlude its own layers.
        DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
    };

    /// A hotter reading of a family colour, for the core of an effect. The
    /// family stays identifiable at the edges while the middle goes near-white,
    /// which is what every one of the reference shots does.
    public static Color Hot(Color energy, float toward = 0.72f) =>
        energy.Lerp(new Color(1, 1, 1), toward);

    private static ImageTexture? _blob;

    /// A radial falloff, generated once and shared by everything that needs a
    /// quad to stop being a quad — ground marks and flame tongues both.
    ///
    /// Cheap and load-bearing. The first capture ran the flame tongues
    /// untextured and they read as a ring of white boxes; the mark of a
    /// primitive is its silhouette, and this is the whole fix.
    public static ImageTexture SoftBlob()
    {
        if (_blob != null) return _blob;
        const int size = 128;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        var centre = new Vector2(size / 2f, size / 2f);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = new Vector2(x + 0.5f, y + 0.5f).DistanceTo(centre) / (size / 2f);
            // Solid to about half, then a long shoulder out to nothing.
            float a = d >= 1 ? 0 : Mathf.Pow(Mathf.Clamp(1f - (d - 0.5f) / 0.5f, 0, 1), 1.5f);
            image.SetPixel(x, y, new Color(1, 1, 1, a));
        }
        _blob = ImageTexture.CreateFromImage(image);
        return _blob;
    }
}

/// A bolt out of frame onto a point on the floor.
///
/// Built as a camera-facing ribbon rather than a line, because Godot's line
/// primitives have no width and a one-pixel bolt is invisible at 1080p. Two
/// passes over the same jagged path — a wide dim halo in the family colour, a
/// thin near-white core inside it — which is the entire reason the reference
/// shot reads as light rather than as a blue stick.
///
/// The path is regenerated a handful of times across the life, not every frame:
/// a bolt that re-jags at 60fps reads as television static, and one that never
/// re-jags reads as a painted decal.
public sealed class Bolt : VfxEffect
{
    private const double Life = 0.32;
    private const int Segments = 24;

    private readonly MeshInstance3D _node;
    private readonly ImmediateMesh _mesh;
    private readonly StandardMaterial3D _material;
    private readonly Camera3D _camera;
    private readonly Vector3 _ground;
    private readonly Color _energy;
    private readonly float _scale;

    private Vector3[] _path;
    private double _nextJag;

    public Bolt(Node3D world, Camera3D camera, Vector3 groundPoint, Color energy, float scale = 1f)
    {
        _camera = camera;
        _ground = groundPoint;
        _energy = energy;
        _scale = scale;
        _mesh = new ImmediateMesh();
        _material = VfxMaterials.Additive(vertexColour: true);
        _node = new MeshInstance3D { Mesh = _mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        world.AddChild(_node);
        _path = Jag();
        Rebuild(1f);
    }

    /// A vertical path from well above the frame down to the ground point,
    /// wandering sideways as it falls. The wander is widest at the top and
    /// converges on the target, so the bolt arrives *at* the victim rather than
    /// near it — the reference bolts all land dead on their target and the
    /// deviation is purely mid-air.
    private Vector3[] Jag()
    {
        var top = _ground + new Vector3(0, 22f * _scale, 0);
        var path = new Vector3[Segments + 1];
        for (int i = 0; i <= Segments; i++)
        {
            float k = (float)i / Segments;
            var straight = top.Lerp(_ground, k);
            // Converges to zero at both ends: pinned to the target at the
            // bottom, and to a single entry point at the top so the bolt does
            // not appear to have two sources.
            float spread = Mathf.Sin(k * Mathf.Pi) * 1.5f * _scale;
            path[i] = straight + new Vector3(
                (float)GD.RandRange(-spread, spread), 0,
                (float)GD.RandRange(-spread, spread));
        }
        return path;
    }

    public override void Advance(double t)
    {
        if (t >= Life) { Done = true; return; }

        // Full brightness for the first three frames, then a decay that is not
        // smooth — real arcs stutter out. The floor of 0.35 keeps the flicker
        // from reading as a rendering fault.
        float fade = t < 0.05 ? 1f : (float)Mathf.Pow(1 - (t - 0.05) / (Life - 0.05), 1.7);
        if (t >= 0.05) fade *= (float)GD.RandRange(0.35, 1.0);

        if (t >= _nextJag) { _path = Jag(); _nextJag = t + 0.055; }
        Rebuild(fade);
    }

    private void Rebuild(float fade)
    {
        _mesh.ClearSurfaces();
        if (fade <= 0.01f) return;
        var halo = _energy;
        halo.A = 0.9f * fade;
        var core = VfxMaterials.Hot(_energy, 0.92f);
        core.A = fade;
        // Three passes, not two. The first capture over the Cache Warren's
        // sand-coloured floor showed the halo doing nothing at all: an additive
        // pass at 0.42 alpha over ground already near white adds nothing the eye
        // can find, so what survived was a bare 10px core reading as a drawn
        // line. Wider and brighter, with an outer bloom feeding the glow pass.
        Ribbon(1.30f * _scale, new Color(halo.R, halo.G, halo.B, halo.A * 0.45f));
        Ribbon(0.62f * _scale, halo);
        Ribbon(0.20f * _scale, core);
    }

    /// One camera-facing strip along the path.
    ///
    /// The width vector is the cross of the path direction and the direction to
    /// the camera, which is what keeps a flat ribbon from going edge-on at the
    /// locked dungeon angle.
    ///
    /// **The side vector is computed per path point and shared by both segments
    /// meeting there, not per segment.** Computing it per segment gives each
    /// quad its own width direction, and at every kink in a jagged path the two
    /// quads then disagree — which is exactly what the first capture showed: a
    /// bolt built out of visibly stepped rectangles rather than a continuous
    /// streak. Averaging the neighbours at the joint is the whole fix.
    private void Ribbon(float halfWidth, Color colour)
    {
        var sides = new Vector3[_path.Length];
        for (int i = 0; i < _path.Length; i++)
        {
            var into = i > 0 ? (_path[i] - _path[i - 1]).Normalized() : Vector3.Zero;
            var outOf = i < _path.Length - 1 ? (_path[i + 1] - _path[i]).Normalized() : Vector3.Zero;
            var direction = (into + outOf).Normalized();
            if (direction.LengthSquared() < 0.000001f) direction = Vector3.Down;
            var toCamera = (_camera.GlobalPosition - _path[i]).Normalized();
            // Tapers toward the top of frame, so the bolt reads as arriving
            // from far away rather than as a column of even width.
            float k = (float)i / (_path.Length - 1);
            sides[i] = direction.Cross(toCamera).Normalized() * halfWidth * Mathf.Lerp(0.55f, 1f, k);
        }

        _mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles, _material);
        for (int i = 0; i < _path.Length - 1; i++)
            Quad(_path[i] - sides[i], _path[i] + sides[i],
                 _path[i + 1] + sides[i + 1], _path[i + 1] - sides[i + 1], colour);
        _mesh.SurfaceEnd();
    }

    private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color colour)
    {
        _mesh.SurfaceSetColor(colour);
        _mesh.SurfaceAddVertex(a); _mesh.SurfaceAddVertex(b); _mesh.SurfaceAddVertex(c);
        _mesh.SurfaceAddVertex(a); _mesh.SurfaceAddVertex(c); _mesh.SurfaceAddVertex(d);
    }

    public override void Release()
    {
        if (GodotObject.IsInstanceValid(_node)) _node.QueueFree();
    }
}

/// A ring travelling outward across the floor from a point, with fire only at
/// its leading edge.
///
/// The reference shot's actual trick, and the thing worth copying: the inside
/// of the wave is *dark ground*, not fire. A filled disc reads as a texture
/// swap; a bright rim with nothing behind it reads as something travelling.
/// The interior darkness is the scorch mark's job, not this one's.
///
/// Rebuilt as an annulus every frame rather than scaled as a static mesh,
/// because the rim wobbles: a perfect expanding circle is unmistakably a
/// primitive, and the wobble costs one sine per vertex.
public sealed class Shockwave : VfxEffect
{
    private const int Segments = 72;

    private readonly MeshInstance3D _node;
    private readonly ImmediateMesh _mesh;
    private readonly StandardMaterial3D _material;
    private readonly Node3D _flames;
    private readonly MeshInstance3D[] _tongues;
    private readonly Vector3 _centre;
    private readonly Color _energy;
    private readonly float _maxRadius;
    private readonly double _life;
    private readonly float[] _wobble;
    private readonly float[] _tongueSizes;

    public Shockwave(Node3D world, Vector3 centre, Color energy, float maxRadius, double life,
                     int tongues = 18)
    {
        // Just clear of the floor plane. The floor's top face sits at y=0 and
        // z-fighting on a surface this large is visible as a shimmer across the
        // whole frame, not as a local artefact.
        _centre = new Vector3(centre.X, 0.05f, centre.Z);
        _energy = energy;
        _maxRadius = maxRadius;
        _life = life;

        _mesh = new ImmediateMesh();
        _material = VfxMaterials.Additive(vertexColour: true);
        _node = new MeshInstance3D { Mesh = _mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        world.AddChild(_node);

        _wobble = new float[Segments];
        for (int i = 0; i < Segments; i++) _wobble[i] = (float)GD.RandRange(0.86, 1.0);

        // Flame tongues stand *up* off the rim. Without them the ring is a flat
        // graphic lying on the floor; with them the wave has a height and reads
        // as fire rather than as a projected circle. They ride the rim outward
        // rather than being parented to a scaling node, which would inflate
        // each tongue into a wall as the ring grew.
        _flames = new Node3D();
        world.AddChild(_flames);
        _tongues = new MeshInstance3D[tongues];
        // Per-tongue size variation. Identical tongues at even spacing are
        // unmistakably N copies of one quad; the irregularity is what turns
        // them into a rim that is burning.
        _tongueSizes = new float[tongues];
        for (int i = 0; i < tongues; i++) _tongueSizes[i] = (float)GD.RandRange(0.62, 1.35);
        // Only a quarter of the way to white. At 0.45 the first capture's gold
        // Relicborn ring came out cream, and a ring of evenly-sized cream blobs
        // reads as a crown of petals rather than as fire — the family colour has
        // to survive in the flame or the effect is just a bright shape.
        var hot = VfxMaterials.Hot(energy, 0.22f);
        for (int i = 0; i < tongues; i++)
        {
            var material = VfxMaterials.Additive();
            material.AlbedoColor = hot;
            // Textured, because untextured they are exactly what the first
            // capture showed: a ring of hard white squares sitting on the
            // floor. A quad only reads as flame if its edges are not there.
            material.AlbedoTexture = VfxMaterials.SoftBlob();
            material.BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled;
            material.BillboardKeepScale = true;
            _tongues[i] = new MeshInstance3D
            {
                Mesh = new QuadMesh { Size = new Vector2(1f, 1.5f), Material = material },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            _flames.AddChild(_tongues[i]);
        }
    }

    public override void Advance(double t)
    {
        if (t >= _life) { Done = true; return; }
        float k = (float)(t / _life);

        // Fast off the mark and decelerating, which is how a blast front
        // actually travels and — more usefully here — puts most of the visible
        // life in the part where the ring is large.
        float radius = _maxRadius * (1f - Mathf.Pow(1f - k, 2.4f));
        // Holds full brightness through the first third, then goes out. Fading
        // from frame one makes the wave look like it was always dying.
        float fade = k < 0.33f ? 1f : Mathf.Pow(1f - (k - 0.33f) / 0.67f, 1.5f);

        Rebuild(radius, fade);
        Flames(radius, fade);
    }

    private void Rebuild(float radius, float fade)
    {
        _mesh.ClearSurfaces();
        if (fade <= 0.01f || radius <= 0.01f) return;

        // Three concentric rings of vertices: a transparent inner edge so the
        // band fades into the dark interior, a hot core, and a near-white outer
        // lip. The lip is what the eye tracks as the wave front.
        var inner = _energy; inner.A = 0f;
        var core = VfxMaterials.Hot(_energy, 0.35f); core.A = 0.95f * fade;
        var lip = VfxMaterials.Hot(_energy, 0.85f); lip.A = 0.5f * fade;

        // The band keeps a constant *fraction* of the radius rather than a
        // constant width, so a wave that has travelled far is a thick front
        // rather than a thinning hoop.
        _mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles, _material);
        for (int i = 0; i < Segments; i++)
        {
            int j = (i + 1) % Segments;
            float w0 = _wobble[i], w1 = _wobble[j];
            var a0 = At(i, radius * 0.74f * w0); var a1 = At(j, radius * 0.74f * w1);
            var b0 = At(i, radius * 0.93f * w0); var b1 = At(j, radius * 0.93f * w1);
            var c0 = At(i, radius * w0);         var c1 = At(j, radius * w1);

            Band(a0, a1, b0, b1, inner, core);
            Band(b0, b1, c0, c1, core, lip);
        }
        _mesh.SurfaceEnd();
    }

    private Vector3 At(int segment, float radius)
    {
        float angle = Mathf.Tau * segment / Segments;
        return _centre + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
    }

    private void Band(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1, Color from, Color to)
    {
        _mesh.SurfaceSetColor(from); _mesh.SurfaceAddVertex(a0);
        _mesh.SurfaceSetColor(from); _mesh.SurfaceAddVertex(a1);
        _mesh.SurfaceSetColor(to);   _mesh.SurfaceAddVertex(b1);

        _mesh.SurfaceSetColor(from); _mesh.SurfaceAddVertex(a0);
        _mesh.SurfaceSetColor(to);   _mesh.SurfaceAddVertex(b1);
        _mesh.SurfaceSetColor(to);   _mesh.SurfaceAddVertex(b0);
    }

    private void Flames(float radius, float fade)
    {
        for (int i = 0; i < _tongues.Length; i++)
        {
            float angle = Mathf.Tau * i / _tongues.Length;
            // Each tongue flickers on its own clock, so the rim boils instead of
            // pulsing in unison.
            float flicker = 0.55f + 0.45f * Mathf.Sin((float)(Time.GetTicksMsec() * 0.02) + i * 2.3f);
            float size = _tongueSizes[i];
            float height = 1.5f * fade * flicker * size;
            _tongues[i].Position = _centre
                + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius * 0.97f
                + new Vector3(0, height * 0.5f, 0);
            // Narrower than tall: a flame is a tongue, and a quad as wide as it
            // is high is a blob whatever texture is on it.
            _tongues[i].Scale = new Vector3(0.55f * fade * size, height, 1);
            _tongues[i].Visible = fade > 0.02f;
        }
    }

    public override void Release()
    {
        if (GodotObject.IsInstanceValid(_node)) _node.QueueFree();
        if (GodotObject.IsInstanceValid(_flames)) _flames.QueueFree();
    }
}

/// A mark left on the floor — scorch, frost, a claw scar.
///
/// Deliberately NOT a Decal node. A Decal projects onto whatever geometry it
/// covers and would be the right answer on real dungeon terrain, but the stage
/// floor is one flat box and a Decal costs a renderer feature the compatibility
/// backend does not have. A flat quad a few centimetres above the floor is
/// indistinguishable here and works everywhere.
///
/// Marks persist. In the reference shots the burn outlives the explosion by
/// seconds, and cumulative damage on the floor across a fight is most of what
/// makes the fight feel like it happened somewhere.
public sealed class GroundMark : VfxEffect
{
    private readonly MeshInstance3D _node;
    private readonly StandardMaterial3D _material;
    private readonly Color _colour;
    private readonly double _fadeIn, _hold, _fadeOut;
    private readonly float _peak;
    /// Width as a multiple of length. 1 is a disc; a claw scar or a crack wants
    /// something well under it, and squashing one axis is the whole difference
    /// between a scratch and a stain.
    private readonly float _aspect;

    public GroundMark(Node3D world, Vector3 centre, Color colour, float radius, float peak = 0.85f,
                      double fadeIn = 0.08, double hold = 1.6, double fadeOut = 1.4,
                      bool additive = false, float yaw = 0f, float aspect = 1f)
    {
        _colour = colour;
        _peak = peak;
        _aspect = aspect;
        _fadeIn = fadeIn; _hold = hold; _fadeOut = fadeOut;

        _material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            // A scorch has to *darken* the floor, and additive cannot subtract —
            // the first pass burned a bright grey circle into the ground. Mix is
            // the only blend that can make a mark look like damage.
            BlendMode = additive ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix,
            AlbedoTexture = VfxMaterials.SoftBlob(),
            AlbedoColor = colour,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
        };
        _node = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(radius * 2, radius * 2), Material = _material },
            Position = new Vector3(centre.X, 0.03f, centre.Z),
            Rotation = new Vector3(0, yaw, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        world.AddChild(_node);
        SetAlpha(0);
    }

    public override void Advance(double t)
    {
        double total = _fadeIn + _hold + _fadeOut;
        if (t >= total) { Done = true; return; }

        float alpha;
        if (t < _fadeIn)
        {
            alpha = (float)(t / _fadeIn);
            // Punches in oversized and settles, so the mark arrives with the
            // blow instead of quietly appearing under it. Over a long fade-in
            // this doubles as a telegraph: a wide dim glow contracting onto the
            // victim's feet through the wind-up.
            Squash(1.35f - 0.35f * alpha);
        }
        else if (t < _fadeIn + _hold)
        {
            alpha = 1;
            Squash(1f);
        }
        else
        {
            alpha = (float)(1 - (t - _fadeIn - _hold) / _fadeOut);
        }
        SetAlpha(alpha * _peak);
    }

    private void Squash(float s) => _node.Scale = new Vector3(s * _aspect, 1, s);

    private void SetAlpha(float a) =>
        _material.AlbedoColor = new Color(_colour.R, _colour.G, _colour.B, a);

    public override void Release()
    {
        if (GodotObject.IsInstanceValid(_node)) _node.QueueFree();
    }
}

/// One frame of real light at the point of impact.
///
/// The cheapest effect in the set and, per line, the most convincing: it relights
/// the floor and both bodies, which is the one thing an additive billboard can
/// never do. In every reference shot the ground around the impact is visibly lit
/// by it, and that — not the sprite — is what places the effect in the world
/// rather than on top of it.
public sealed class LightPop : VfxEffect
{
    private readonly OmniLight3D _light;
    private readonly float _peak;
    private readonly double _life;

    public LightPop(Node3D world, Vector3 at, Color colour, float peak = 9f, float range = 14f,
                    double life = 0.28)
    {
        _peak = peak;
        _life = life;
        _light = new OmniLight3D
        {
            Position = at,
            LightColor = colour,
            LightEnergy = peak,
            OmniRange = range,
            ShadowEnabled = false,
        };
        world.AddChild(_light);
    }

    public override void Advance(double t)
    {
        if (t >= _life) { Done = true; return; }
        // Straight to peak, then a sharp exponential out — a flash, not a lamp
        // being dimmed.
        _light.LightEnergy = _peak * Mathf.Pow(1f - (float)(t / _life), 3f);
    }

    public override void Release()
    {
        if (GodotObject.IsInstanceValid(_light)) _light.QueueFree();
    }
}

/// A staggered flight of emissive shapes crossing the gap between two points.
///
/// Staggered is the whole effect. Five projectiles launched together are one
/// wide projectile; launched two frames apart they are a volley, and the eye
/// gets five separate arrivals to register instead of one. The reference shot
/// has the same thing — the arrows are visibly at different points along their
/// flight in a single frame.
///
/// Each shape is drawn twice, a thin bright core inside a fatter dim halo, the
/// same two-pass trick the bolt uses and for the same reason.
public sealed class Volley : VfxEffect
{
    private readonly Node3D _parent;
    private readonly Node3D[] _shots;
    private readonly MeshInstance3D[] _flashes;
    private readonly Vector3 _from, _to;
    private readonly double _stagger, _flight;
    private readonly Color _energy;

    public Volley(Node3D world, Vector3 from, Vector3 to, Color energy, int count = 4,
                  double stagger = 0.07, double flight = 0.20, float size = 1f)
    {
        _from = from; _to = to;
        _stagger = stagger; _flight = flight;
        _energy = energy;

        _parent = new Node3D();
        world.AddChild(_parent);
        _shots = new Node3D[count];
        _flashes = new MeshInstance3D[count];

        var core = VfxMaterials.Additive();
        core.AlbedoColor = VfxMaterials.Hot(energy, 0.85f);
        var halo = VfxMaterials.Additive();
        halo.AlbedoColor = new Color(energy.R, energy.G, energy.B, 0.5f);

        for (int i = 0; i < count; i++)
        {
            var shot = new Node3D { Visible = false };
            // Long in Z and thin in X/Y; the node is aimed with LookAt so the
            // shape lies along its own flight path. A round projectile reads as
            // a bead and gives the eye nothing to judge speed by.
            shot.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.10f, 0.10f, 1.5f) * size, Material = core },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
            shot.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.34f, 0.34f, 2.4f) * size, Material = halo },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
            _parent.AddChild(shot);
            _shots[i] = shot;

            var flashMaterial = VfxMaterials.Additive();
            flashMaterial.AlbedoColor = VfxMaterials.Hot(energy, 0.6f);
            flashMaterial.BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled;
            flashMaterial.BillboardKeepScale = true;
            _flashes[i] = new MeshInstance3D
            {
                Mesh = new QuadMesh { Size = new Vector2(1.6f, 1.6f) * size, Material = flashMaterial },
                Position = to,
                Visible = false,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            _parent.AddChild(_flashes[i]);
        }
    }

    /// When the last shot lands, from the volley's own start. The caller needs
    /// this to line the first arrival up with the animation's contact frame
    /// rather than the last.
    public double LastArrival => _stagger * (_shots.Length - 1) + _flight;

    public override void Advance(double t)
    {
        bool anyAlive = false;
        for (int i = 0; i < _shots.Length; i++)
        {
            double launched = t - _stagger * i;
            if (launched < 0) { anyAlive = true; continue; }

            double k = launched / _flight;
            if (k <= 1)
            {
                anyAlive = true;
                var at = _from.Lerp(_to, (float)k);
                _shots[i].Visible = true;
                _shots[i].Position = at;
                // LookAt fails on a zero-length basis; the guard matters on the
                // first frame, where the shot is still sitting on its origin.
                if ((_to - at).LengthSquared() > 0.0001f) _shots[i].LookAt(_to, Vector3.Up);
                _flashes[i].Visible = false;
            }
            else
            {
                _shots[i].Visible = false;
                // The arrival: a quick expanding puff where the shot went in.
                double since = launched - _flight;
                const double FlashLife = 0.18;
                if (since < FlashLife)
                {
                    anyAlive = true;
                    float f = (float)(since / FlashLife);
                    _flashes[i].Visible = true;
                    _flashes[i].Scale = Vector3.One * (0.4f + 1.4f * f);
                    var c = VfxMaterials.Hot(_energy, 0.6f);
                    c.A = 1f - f;
                    ((QuadMesh)_flashes[i].Mesh).Material.Set("albedo_color", c);
                }
                else _flashes[i].Visible = false;
            }
        }
        if (!anyAlive) Done = true;
    }

    public override void Release()
    {
        if (GodotObject.IsInstanceValid(_parent)) _parent.QueueFree();
    }
}
