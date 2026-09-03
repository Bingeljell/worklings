using Godot;
using Worklings.Core.Host;
using Worklings.Core.Stage;

/// The desktop pet's window, with a Workling standing in it and nothing else.
///
/// This is the shell slice: prove Godot can *be* a desktop pet — transparent,
/// borderless, always on top, click-through, correctly placed across monitors —
/// before porting the 543 lines of PetBrain that would live inside it. Nothing
/// here decides what the pet does; it decides whether there is somewhere for a
/// pet to be.
///
/// The body is the Tempest Ram on its idle loop, deliberately rather than a
/// coloured square. A square would prove the window is transparent and nothing
/// about the hard part: a 3D viewport with per-pixel alpha, lit well enough to
/// read against an arbitrary desktop behind it.
///
/// Controls, since a borderless window has no chrome to click:
/// **Esc** quits · **Tab** next monitor · **C** toggles click-through ·
/// **R** toggles roaming · **drag** the pet to move it.
public partial class DesktopPetScene : Node3D
{
    /// Small enough to sit beside the work, big enough that the animal reads.
    /// A knob, and one worth judging on a real desktop rather than deciding here.
    [Export] public Vector2I WindowSize { get; set; } = new Vector2I(320, 320);

    /// How far off the screen edge the pet rests. Matches the Swift app's
    /// default so the pet lands in the same place under both.
    [Export] public float Margin { get; set; } = 24;

    /// The fraction of the window, centred, that keeps mouse clicks. The rest
    /// passes through to whatever is behind. Roughly the animal's footprint;
    /// a real silhouette needs a hull from the mesh, which this slice does not
    /// attempt.
    [Export] public float BodyFraction { get; set; } = 0.62f;

    /// Off makes the whole window eat clicks. Exposed because click-through is
    /// impossible to judge without being able to turn it off and feel the
    /// difference.
    [Export] public bool ClickThrough { get; set; } = true;

    /// Wander the screen on the ported pattern. Off parks the pet where it
    /// started, which is the state to be in when judging anything else.
    [Export] public bool Roam { get; set; } = true;

    /// Which monitor to open on. -1 opens on whichever the window landed on.
    [Export] public int Screen { get; set; } = -1;

    private StageActor _pet = null!;
    private int _screen;

    private enum Wander { Resting, Travelling }
    private Wander _wander = Wander.Resting;
    private ulong _sequence;
    private double _timer;
    private PlacementPoint _from, _to;
    private double _travelTotal;

    private bool _dragging;
    private Vector2I _grabOffset;

    public override void _Ready()
    {
        var window = GetWindow();
        DesktopWindow.MakeCompanion(window);
        window.Size = WindowSize;

        // Clamped, not trusted. A headless run reports zero screens and
        // WindowGetCurrentScreen answers -1, which asks DisplayServer for the
        // usable rect of a monitor that does not exist.
        int screens = DisplayServer.GetScreenCount();
        _screen = Screen >= 0 && Screen < screens ? Screen : DisplayServer.WindowGetCurrentScreen();
        _screen = System.Math.Clamp(_screen, 0, System.Math.Max(0, screens - 1));
        if (screens > 0) PlaceOnScreen(_screen);
        ApplyInteractiveRegion();

        _pet = new StageActor(GetNode<Node3D>("Pet"), "tempest_ram", ActorAnimations.TempestRam);
        _pet.Play(ActorAction.Idle, loop: true);

        Report();
        BeginResting();
    }

    /// Printed rather than assumed. Whether transparency is actually on, and
    /// where every monitor is, are the two things this slice exists to answer —
    /// and both are invisible in a screenshot of a window that looks plausible.
    private void Report()
    {
        GD.Print("— desktop pet —");
        GD.Print($"transparency allowed: {DesktopWindow.TransparencyAllowed}");
        GD.Print($"display server: {DisplayServer.GetName()}");
        GD.Print($"screens: {DisplayServer.GetScreenCount()}");
        for (int i = 0; i < DisplayServer.GetScreenCount(); i++)
        {
            var frame = DesktopWindow.UsableFrame(i);
            GD.Print($"  screen {i}: usable ({frame.X}, {frame.Y}) {frame.Width}x{frame.Height}"
                   + $" scale {DisplayServer.ScreenGetScale(i)}");
        }
        GD.Print($"on screen {_screen} at {GetWindow().Position}, size {GetWindow().Size}");
        GD.Print($"click-through: {ClickThrough}  ·  roaming: {Roam}");
        GD.Print("Esc quit · Tab next monitor · C click-through · R roam · drag to move");
    }

    private void PlaceOnScreen(int screen)
    {
        var window = GetWindow();
        var origin = ScreenPlacement.DefaultOrigin(
            DesktopWindow.SizeOf(window), DesktopWindow.UsableFrame(screen), Margin);
        DesktopWindow.MoveTo(window, origin);
    }

    private void ApplyInteractiveRegion()
    {
        if (!ClickThrough)
        {
            DesktopWindow.ClearInteractiveRegion();
            return;
        }
        var size = (Vector2)GetWindow().Size;
        var body = size * BodyFraction;
        DesktopWindow.SetInteractiveRegion(new Rect2((size - body) / 2, body));
    }

    // MARK: - Wandering

    private void BeginResting()
    {
        _wander = Wander.Resting;
        _timer = PetRoamingPlanner.Intent(_sequence).RestDuration;
    }

    private void BeginTravelling()
    {
        var window = GetWindow();
        var intent = PetRoamingPlanner.Intent(_sequence);
        _from = DesktopWindow.OriginOf(window);
        _to = ScreenPlacement.RoamingOrigin(
            _from, intent, DesktopWindow.SizeOf(window),
            DesktopWindow.UsableFrame(_screen), Margin);
        _travelTotal = System.Math.Max(intent.TravelDuration, 0.01);
        _timer = _travelTotal;
        _wander = Wander.Travelling;
    }

    public override void _Process(double delta)
    {
        if (!Roam || _dragging) return;

        _timer -= delta;
        if (_wander == Wander.Resting)
        {
            if (_timer <= 0) BeginTravelling();
            return;
        }

        // Eased rather than linear: a pet that starts and stops at full speed
        // reads as a window being moved by a script, which is exactly what it is
        // and exactly what it should not look like.
        double progress = System.Math.Clamp(1 - _timer / _travelTotal, 0, 1);
        double eased = progress * progress * (3 - 2 * progress);
        DesktopWindow.MoveTo(GetWindow(), new PlacementPoint(
            _from.X + (_to.X - _from.X) * eased,
            _from.Y + (_to.Y - _from.Y) * eased));

        if (_timer <= 0)
        {
            _sequence += 1;
            BeginResting();
        }
    }

    // MARK: - Input

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            switch (key.Keycode)
            {
                case Key.Escape:
                    GetTree().Quit();
                    return;
                case Key.Tab:
                    if (DisplayServer.GetScreenCount() == 0) return;
                    _screen = (_screen + 1) % DisplayServer.GetScreenCount();
                    PlaceOnScreen(_screen);
                    GD.Print($"moved to screen {_screen} at {GetWindow().Position}");
                    return;
                case Key.C:
                    ClickThrough = !ClickThrough;
                    ApplyInteractiveRegion();
                    GD.Print($"click-through: {ClickThrough}");
                    return;
                case Key.R:
                    Roam = !Roam;
                    if (Roam) BeginResting();
                    GD.Print($"roaming: {Roam}");
                    return;
            }
        }

        // Dragging works in screen coordinates, not window ones: the window is
        // moving underneath the pointer, so a delta read from the window's own
        // mouse position chases itself and the pet slides away from the cursor.
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } click)
        {
            _dragging = click.Pressed;
            if (_dragging)
            {
                _grabOffset = DisplayServer.MouseGetPosition() - GetWindow().Position;
            }
            else
            {
                BeginResting();
            }
        }
        else if (@event is InputEventMouseMotion && _dragging)
        {
            GetWindow().Position = DisplayServer.MouseGetPosition() - _grabOffset;
        }
    }
}
