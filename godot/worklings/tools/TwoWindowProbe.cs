using Godot;
using Worklings.Core.Host;
using Worklings.Core.Stage;

/// Proves — or disproves — the two-window architecture before anything is built
/// on it.
///
/// The question: can the pet stay the **main** window, keeping the transparent /
/// borderless / always-on-top setup that is already proven, while the dungeon
/// opens as a **second, ordinary** window with its own size and its own content
/// scaling? If so, nothing about the pet window ever has to mutate mid-session,
/// and the letterboxing trap stops being something to manage and becomes
/// something that cannot happen.
///
/// Runs itself on a timer rather than waiting for input, so the whole cycle can
/// be screenshotted without a hand on the keyboard:
///
///   0s  pet only, transparent and borderless
///   4s  second window opens with the real dungeon in it; pet hides
///   12s pet returns; second window closes
///   16s quit
///
/// Deliberately loads `cache_warren.tscn` rather than a coloured rectangle. A
/// rectangle would prove a second window opens and nothing about whether a 3D
/// scene with its own camera renders correctly inside one — which is the actual
/// question.
public partial class TwoWindowProbe : Node3D
{
    private Window? _dungeon;
    private double _elapsed;
    private int _step;

    public override void _Ready()
    {
        var window = GetWindow();
        DesktopWindow.MakeCompanion(window);
        window.Size = new Vector2I(320, 320);
        window.Position = new Vector2I(3256, 102);

        // Godot embeds child Windows *inside* the parent viewport by default
        // (gui/embed_subwindows), which for a 320x320 pet window means a
        // 1280x720 dungeon is drawn inside it and clipped to nothing. Turning
        // embedding off is what makes a second window a real OS window.
        window.GuiEmbedSubwindows = false;

        var pet = new StageActor(
            GetNode<Node3D>("Pet"), "tempest_ram", ActorAnimations.TempestRam);
        pet.Play(ActorAction.Idle, loop: true);

        GD.Print("[two-window] main window: "
               + $"size={window.Size} transparent={window.TransparentBg} "
               + $"scale-mode={window.ContentScaleMode}");
    }

    public override void _Process(double delta)
    {
        _elapsed += delta;

        if (_step == 0 && _elapsed > 4)
        {
            _step = 1;
            OpenDungeon();
        }
        else if (_step == 1 && _elapsed > 12)
        {
            _step = 2;
            CloseDungeon();
        }
        else if (_step == 2 && _elapsed > 16)
        {
            _step = 3;
            GetTree().Quit();
        }
    }

    private void OpenDungeon()
    {
        _dungeon = new Window
        {
            Title = "The Cache Warren",
            Size = new Vector2I(1280, 720),
            Position = new Vector2I(400, 300),
            // Its own 3D world. Without this the child window renders the
            // *parent's* world — it would show the pet's empty room lit by the
            // pet's lights, with the dungeon's own scene invisible inside it.
            // This is the detail a coloured rectangle would never have surfaced.
            World3D = new World3D(),
            // The dungeon is fixed-aspect; the pet window is not. Two windows
            // means each says so once, here, instead of one window toggling it
            // on every mode change.
            ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems,
            ContentScaleAspect = Window.ContentScaleAspectEnum.Keep,
            ContentScaleSize = new Vector2I(1920, 1080),
        };
        AddChild(_dungeon);
        _dungeon.AddChild(GD.Load<PackedScene>("res://scenes/cache_warren.tscn").Instantiate());
        _dungeon.Show();
        // A new window in an app that is not frontmost opens *behind* whatever
        // the user is looking at. Entering a delve is a deliberate act, so it
        // comes forward and takes focus.
        _dungeon.MoveToForeground();
        _dungeon.GrabFocus();

        // The pet leaves. NOT by hiding the window — Godot refuses to change the
        // main window's visibility ("Can't change visibility of main window"),
        // and the pet is the main window because that is where the transparency
        // is already proven. Emptying it is equivalent and needs no flag turned
        // off and back on: a transparent window drawing nothing is nothing.
        GetNode<Node3D>("Pet").Visible = false;

        GD.Print("[two-window] dungeon open: "
               + $"size={_dungeon.Size} own-world={_dungeon.World3D != null} "
               + $"scale-mode={_dungeon.ContentScaleMode} aspect={_dungeon.ContentScaleAspect}");
        GD.Print($"[two-window] pet emptied: {!GetNode<Node3D>("Pet").Visible}, "
               + $"embedded={_dungeon.IsEmbedded()}");
    }

    private void CloseDungeon()
    {
        _dungeon?.QueueFree();
        _dungeon = null;
        GetNode<Node3D>("Pet").Visible = true;
        GD.Print("[two-window] pet back");
    }
}
