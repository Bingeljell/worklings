using Godot;
using Worklings.Core.Pet;
using Worklings.Core.Roster;
using Worklings.Core.Stage;

namespace Worklings.Core.Host;

/// The Workling itself, standing in its own screen.
///
/// **Why a bay and not a portrait.** The character window is otherwise a column
/// of numbers, and the numbers are not what the player is attached to. The pet
/// on the desktop is a live 3D body playing its idle; a flat picture of it in
/// the one screen that is supposed to be *about* it would read as a downgrade.
///
/// A `SubViewport` with `OwnWorld3D`, not a camera hung off the window. The bay
/// has to sit in the tab's layout — above the name, scrolling with everything
/// else — and only a viewport inside a `Control` does that. It also means the
/// bay is self-contained: nothing outside it has to arrange a world, a light rig
/// or a clear colour for it, and it cannot pick up whatever another window is
/// looking at.
///
/// The rig is the desktop pet's, copied deliberately — same lights, same lens,
/// same angle. A Workling lit or framed differently in its own screen than on
/// the desktop looks like a different creature. The one change is that the
/// camera is pulled in along its own axis: the pet's window is a square and the
/// bay is a letterbox, and Godot keeps the vertical fov, so the desktop's
/// distance leaves the Ram stranded in the middle of a wide empty box.
public sealed partial class ModelBay : SubViewportContainer
{
    private readonly float _scale;
    private string _wornCreatureId = "";
    private Node3D? _body;
    /// Set by `Wear` before the rig exists, applied once `_Ready` has built it.
    /// The panel builds the bay and tells it who it is holding in the same pass,
    /// and Godot runs `_Ready` only after the node is in the tree.
    private PetFamily? _pending;
    private Node3D? _turntable;
    private Camera3D? _camera;
    private CreatureAura? _aura;
    private double _auraSeconds;
    private Vector3 _target;
    private float _halfHeight = 1f;
    private float _radius = 1f;

    /// Where the camera sits relative to what it is looking at — the desktop
    /// pet's angle, kept exactly. Only the distance is recomputed.
    private static readonly Vector3 Eye = new(0.4677072f, 0.35355338f, 0.81009257f);

    /// How hard the aura is driven here.
    ///
    /// The bay is lit like the desktop and for the same reason — a Workling lit
    /// differently in its own screen looks like a different creature — so it
    /// needs the same push for the same reason: `blend_add` over bright fleece
    /// barely moves at the dungeon's 1.0.
    private const float AuraStrength = 1.3f;

    /// How much of the frame's height the Workling fills.
    private const float Fill = 0.9f;

    /// The height every body is normalised to before it is framed, in world
    /// units. The camera fits whatever it is given, so this number is not
    /// visible on its own — what it buys is that the turntable, the aura and
    /// the framing arithmetic all see bodies of one size, whatever a `.glb`
    /// happened to be exported at. Deliberately NOT `Creature.StageHeight`: in
    /// the Warren a Pangolin should read as shorter than a Ram, but this is a
    /// portrait of your Workling and it should fill its own frame.
    private const float BayHeight = 1.2f;

    /// How far the camera may be pulled back beyond a height-filling fit to get
    /// the body's width in. Uncapped, a narrow bay fits the Ram's whole *length*
    /// across a few degrees of horizontal field and parks the camera fourteen
    /// metres away, which is arithmetically correct and reads as a lost sheep.
    private const float WidthAllowance = 1.25f;

    /// `height` is in physical pixels — the caller has already scaled it. Godot
    /// sizes everything here in physical pixels; see the port status doc.
    public ModelBay(int height, float scale)
    {
        _scale = scale;
        Stretch = true;
        CustomMinimumSize = new Vector2(0, height);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
    }

    /// A transform written the way a `.tscn` writes one.
    ///
    /// **The trap.** The scene format serialises a basis as its three ROWS; the
    /// C# `Transform3D` constructor takes its three COLUMNS. The same twelve
    /// numbers, transposed. Copy them straight across and the rig is silently
    /// rotated somewhere else — the model ends up hundreds of pixels off-frame
    /// and the bay renders as a clean, convincing empty box.
    private static Transform3D Rig(
        float xx, float xy, float xz,
        float yx, float yy, float yz,
        float zx, float zy, float zz,
        Vector3 origin) =>
        new(new Vector3(xx, yx, zx), new Vector3(xy, yy, zy), new Vector3(xz, yz, zz), origin);

    public override void _Ready()
    {
        var viewport = new SubViewport
        {
            // Its own world. Without this the bay renders whatever world its
            // parent viewport holds, which is the character window's — empty,
            // so the bay would come up blank.
            OwnWorld3D = true,
            // Always, not WhenVisible: the container is inside a ScrollContainer
            // inside a TabContainer, and "visible" there is not the same
            // question as "on screen".
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Msaa3D = Viewport.Msaa.Msaa4X,
        };
        AddChild(viewport);

        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            // A shade off the panel rather than black, so the bay reads as a
            // recess in the window and not as a hole in it.
            BackgroundColor = new Color(0.09f, 0.09f, 0.11f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.55f, 0.57f, 0.62f),
            AmbientLightEnergy = 1.2f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
        };
        viewport.AddChild(new WorldEnvironment { Environment = environment });

        viewport.AddChild(new DirectionalLight3D
        {
            Transform = Rig(
                0.70710677f, 0.49999997f, -0.49999997f,
                0, 0.70710677f, 0.70710677f,
                0.70710677f, -0.49999997f, 0.49999997f, Vector3.Zero),
            LightColor = new Color(1f, 0.94f, 0.86f),
            LightEnergy = 1.8f,
        });
        viewport.AddChild(new DirectionalLight3D
        {
            Transform = Rig(
                -0.70710677f, -0.35355338f, 0.6123724f,
                0, 0.8660254f, 0.5f,
                -0.70710677f, 0.35355338f, -0.6123724f, Vector3.Zero),
            LightColor = new Color(0.75f, 0.82f, 1f),
            LightEnergy = 0.7f,
        });

        // The turntable sits at the origin and the body hangs off it, so a drag
        // spins the Workling on the spot rather than swinging it around a point
        // half a metre below its feet.
        _turntable = new Node3D();
        viewport.AddChild(_turntable);

        _camera = new Camera3D { Fov = 34.0f };
        viewport.AddChild(_camera);
        // MakeCurrent after it is in the tree. Setting Current on a camera with
        // no viewport yet does nothing at all.
        _camera.MakeCurrent();
        if (_pending is { } waiting)
        {
            _pending = null;
            Wear(waiting);
        }
        Frame();
        // The bay is now the element that absorbs the window's spare width, so
        // its aspect is whatever the player drags it to.
        Resized += Frame;
    }

    /// Puts this race's Workling in the bay.
    ///
    /// **The bay used to be the Ram, always.** It loaded `tempest_ram.glb` and
    /// named its own animation table, so a Relicborn opened the one screen that
    /// is supposed to be about their Workling and found someone else's body in
    /// it. `DesktopPetScene.WearBody` had already closed exactly this gap on the
    /// desktop; this is the same fix, against the same roster, and the roster
    /// stays the only place that knows which body a race wears.
    ///
    /// Idempotent by creature, not by race: two races resolving to the same
    /// creature must not pay for a reload, and the panel calls this on every
    /// rebuild. Instancing a `.glb` is a frame hitch, and a rebuild happens on
    /// every keystroke in the name field.
    public void Wear(PetFamily race)
    {
        if (_turntable is null)
        {
            _pending = race;
            return;
        }

        var creature = CreatureRoster.ForRace(race);
        if (creature.Id == _wornCreatureId) return;
        if (creature.Animations is null)
        {
            GD.PushWarning($"[bay] {creature.Id} has no animation table; body unchanged");
            return;
        }

        var packed = GD.Load<PackedScene>(creature.ScenePath);
        if (packed is null)
        {
            GD.PushWarning($"[bay] {creature.ScenePath} did not load; body unchanged");
            return;
        }

        _aura?.Release();
        _aura = null;
        if (_body is not null)
        {
            // Renamed before freeing: the replacement goes in this frame and
            // `QueueFree` takes until the end of it, so without this the tree
            // briefly holds two nodes of the same name.
            _body.Name = "BodyRetired";
            _body.QueueFree();
        }

        var body = packed.Instantiate<Node3D>();
        body.Name = "Body";
        _turntable.AddChild(body);
        _body = body;
        // In the tree first, and only then measured: `MeasureBounds` walks the
        // model reading `GlobalTransform`, which a node outside the tree does
        // not have — it returns identity and logs, and the bounds come back as
        // the first mesh's alone. Still before the transform is set, so these
        // are the model's authored bounds.
        var bounds = StageCast.MeasureBounds(body);
        float scale = bounds.Size.Y > 0.0001f ? BayHeight / bounds.Size.Y : 1f;
        // Centred on the turntable's axis rather than offset by a number tuned
        // against one body. The Ram's old -0.55 was doing exactly this for the
        // Ram alone; a Pangolin at the same offset hangs below the frame.
        body.Transform = new Transform3D(
            Basis.Identity.Scaled(Vector3.One * scale), -bounds.GetCenter() * scale);

        var actor = new StageActor(body, creature.Id, creature.Animations);
        actor.Play(ActorAction.Idle, loop: true);
        // The idle identity belongs to the body, so it belongs here too: this is
        // the one screen whose whole job is looking at the creature, and it was
        // the only surface showing it without its aura.
        _aura = CreatureAura.For(creature.Id, actor.Mesh, AuraStrength);
        _auraSeconds = 0;

        // What the camera has to fit. The body is centred on the origin, so the
        // target is the origin whatever stands here.
        _target = Vector3.Zero;
        var measured = bounds.Size * scale;
        _halfHeight = Mathf.Max(measured.Y * 0.5f, 0.01f);
        // A cylinder, not a box: the turntable spins the body, so the fit has to
        // be the same at every angle or the Ram would grow and shrink as it is
        // dragged. The radius is the corner's, so a long body is held by its
        // diagonal rather than by whichever side happens to face the camera.
        _radius = Mathf.Max(
            new Vector2(measured.X, measured.Z).Length() * 0.5f, 0.01f);

        _wornCreatureId = creature.Id;
        Frame();
    }

    /// Puts the camera far enough back to hold the whole Workling at the bay's
    /// current shape.
    ///
    /// **Godot keeps the vertical field of view**, so a bay that grows taller
    /// than it is wide does not show the creature bigger — it shows more empty
    /// room above and below it. The fixed distance the bay shipped with was
    /// measured against a letterbox, and in a tall column it stranded the Ram in
    /// the upper half of a mostly empty box. Fitting the smaller of the two
    /// fields of view is what makes the frame right at any aspect.
    private void Frame()
    {
        if (_camera is null) return;
        var size = Size;
        if (size.X < 1 || size.Y < 1) return;

        // Height first, width second. Godot fixes the vertical field of view, so
        // filling the height is what makes the creature big; the width is only
        // allowed to push the camera back so far before it is left to crop.
        //
        // The camera looks down at the body, which is why this is not simply the
        // body's height: a metre of *length* under a 21° tilt is a third of a
        // metre of screen height, and ignoring that put the Ram's head off the
        // side of the frame while the arithmetic insisted it fitted.
        float lean = Eye.Y;                        // sine of the camera's tilt
        float level = Mathf.Sqrt(1f - lean * lean); // and its cosine
        float screenHalfHeight = _halfHeight * level + _radius * lean;
        float halfDepth = _radius * level + _halfHeight * lean;

        float vertical = Mathf.Tan(Mathf.DegToRad(_camera.Fov) * 0.5f);
        float horizontal = vertical * (size.X / size.Y);
        float forHeight = screenHalfHeight / vertical / Fill;
        float forWidth = _radius / Mathf.Max(horizontal, 0.0001f) / Fill;
        float distance =
            Mathf.Max(forHeight, Mathf.Min(forWidth, forHeight * WidthAllowance))
            // The near side is closer than the centre by this much, and a body
            // fitted as though it were flat bulges out of the top of the frame.
            + halfDepth * 0.45f;

        // Spare height goes above the Workling, not around it. A tall bay fitted
        // dead-centre leaves the creature hanging in the middle of a column of
        // nothing; pushed down, the same spare room reads as headroom over
        // something standing on a floor.
        float spare = Mathf.Max(distance * vertical - screenHalfHeight, 0f);
        var aim = _target + Vector3.Up * spare * 0.45f;
        _camera.Position = aim + Eye * distance;
        _camera.LookAt(aim);
    }

    /// The aura takes its time explicitly, so the bay advances it.
    public override void _Process(double delta)
    {
        if (_aura is null) return;
        _auraSeconds += delta;
        _aura.Draw((float)_auraSeconds);
    }

    /// Drag to turn it. The first thing anyone does to a model in a box, and
    /// cheap enough that not having it would be the surprising choice. Divided
    /// by the display scale so a drag turns the same amount on a 1x screen as on
    /// a 2x one, where the same gesture reports twice the pixels.
    public override void _GuiInput(InputEvent @event)
    {
        if (_turntable is null
            || @event is not InputEventMouseMotion motion
            || (motion.ButtonMask & MouseButtonMask.Left) == 0)
        {
            return;
        }
        _turntable.RotateY(-motion.Relative.X * 0.01f / Mathf.Max(_scale, 0.01f));
        AcceptEvent();
    }
}
