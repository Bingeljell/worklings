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

    /// How fast the pet crosses the desktop, in screen pixels per second.
    ///
    /// **This replaces the roaming pattern's own travel durations**, which is a
    /// deliberate divergence from the port. Those durations were authored for a
    /// pet that slid: 2.2 to 3 seconds regardless of distance, which works out
    /// near 110 px/s on a long leg and much slower on a short one — so the same
    /// walk cycle would have to play at two different speeds to keep its feet on
    /// the ground. Deriving the duration from the distance instead means one
    /// speed, and the walk reads as walking. The pattern still owns *where* the
    /// pet goes and how long it rests.
    [Export] public float WalkSpeed { get; set; } = 55;

    /// How far the pet turns toward where it is going, in degrees off
    /// facing-you. It walks across the screen, so it should not be walking
    /// sideways — but a full 90 degrees turns its face away, and the face is the
    /// point. Negate this if it turns the wrong way.
    [Export] public float TurnDegrees { get; set; } = 38;

    /// How long the turn takes. Slower than it sounds like it should be: a pet
    /// that snaps around reads as a sprite flipping.
    [Export] public float TurnSeconds { get; set; } = 0.45f;

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
    private float _facing;
    private float _facingTarget;

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
        var w = GetWindow();
        GD.Print($"on screen {_screen} at {w.Position}, size {w.Size}");
        GD.Print($"content scale: mode={w.ContentScaleMode} aspect={w.ContentScaleAspect} "
               + $"size={w.ContentScaleSize} factor={w.ContentScaleFactor}");
        GD.Print($"viewport visible rect: {GetViewport().GetVisibleRect()}");
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
        _facingTarget = 0;
        _pet.Play(ActorAction.Idle, loop: true);
    }

    private void BeginTravelling()
    {
        var window = GetWindow();
        var intent = PetRoamingPlanner.Intent(_sequence);
        _from = DesktopWindow.OriginOf(window);
        _to = ScreenPlacement.RoamingOrigin(
            _from, intent, DesktopWindow.SizeOf(window),
            DesktopWindow.UsableFrame(_screen), Margin);

        double dx = _to.X - _from.X;
        double dy = _to.Y - _from.Y;
        double distance = System.Math.Sqrt(dx * dx + dy * dy);

        // A leg the roaming pattern folded back onto where the pet already is —
        // nothing to walk, so rest again rather than play a walk cycle in place.
        if (distance < 1)
        {
            _sequence += 1;
            BeginResting();
            return;
        }

        _travelTotal = distance / System.Math.Max(WalkSpeed, 1);
        _timer = _travelTotal;
        _wander = Wander.Travelling;
        _facingTarget = dx >= 0 ? TurnDegrees : -TurnDegrees;
        _pet.Play(ActorAction.Walk, loop: true);
    }

    public override void _Process(double delta)
    {
        TurnTowardTravel(delta);
        if (!Roam || _dragging) return;

        _timer -= delta;
        if (_wander == Wander.Resting)
        {
            if (_timer <= 0) BeginTravelling();
            return;
        }

        // Linear, not eased. An eased position with a constant-speed walk cycle
        // slides the feet at both ends — the pet covers ground faster than its
        // legs are moving in the middle and slower at the edges. Accelerating
        // properly needs start and stop variants of the walk clip, which the Ram
        // does not have; see the animation-timing note in the port status doc.
        double progress = System.Math.Clamp(1 - _timer / _travelTotal, 0, 1);
        DesktopWindow.MoveTo(GetWindow(), new PlacementPoint(
            _from.X + (_to.X - _from.X) * progress,
            _from.Y + (_to.Y - _from.Y) * progress));

        if (_timer <= 0)
        {
            _sequence += 1;
            BeginResting();
        }
    }

    /// Eases the pet around to face where it is walking, and back to facing you
    /// when it stops. Runs whether or not it is roaming, so a turn already under
    /// way finishes rather than freezing mid-swing when roaming is switched off.
    private void TurnTowardTravel(double delta)
    {
        if (Mathf.IsEqualApprox(_facing, _facingTarget)) return;
        // Rated so the widest swing there is — one side to the other — takes
        // TurnSeconds, which makes a turn back to facing you take half as long.
        float rate = System.Math.Abs(TurnDegrees) * 2 / System.Math.Max(TurnSeconds, 0.01f);
        _facing = Mathf.MoveToward(_facing, _facingTarget, rate * (float)delta);
        _pet.Root.RotationDegrees = new Vector3(0, _facing, 0);
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
