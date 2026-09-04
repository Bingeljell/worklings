using Godot;
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
    /// One body, hard-coded, because that is all there is. The pet, the party
    /// member in the Warren and this bay are all the Ram; family does not pick a
    /// model yet anywhere in the codebase.
    private const string ModelPath = "res://assets/characters/tempest_ram.glb";
    private const string ModelName = "tempest_ram";

    private readonly float _scale;
    private Node3D? _turntable;

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

        var body = GD.Load<PackedScene>(ModelPath).Instantiate<Node3D>();
        body.Transform = new Transform3D(
            Basis.Identity.Scaled(Vector3.One * 0.9f), new Vector3(0, -0.55f, 0));
        _turntable.AddChild(body);
        new StageActor(body, ModelName, ActorAnimations.TempestRam)
            .Play(ActorAction.Idle, loop: true);

        var camera = new Camera3D
        {
            Transform = Rig(
                0.86602545f, -0.17677669f, 0.4677072f,
                0, 0.9354143f, 0.35355338f,
                // The desktop's (2.2, 1.5, 3.8) at 80% of its distance from
                // what it looks at, so the angle and the lens are untouched.
                -0.5f, -0.30618623f, 0.81009257f, new Vector3(1.76f, 1.168f, 3.04f)),
            Fov = 34.0f,
        };
        viewport.AddChild(camera);
        // MakeCurrent after it is in the tree. Setting Current on a camera with
        // no viewport yet does nothing at all.
        camera.MakeCurrent();
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
