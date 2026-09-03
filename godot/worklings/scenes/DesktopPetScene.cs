using Godot;
using Worklings.Core.Host;
using Worklings.Core.Pet;
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
/// Controls: **right-click the pet** for the menu — it is the only way in, since
/// a borderless window has no chrome — and **click the pet** to pet it. The
/// developer keys remain: **Esc** quits · **Tab** next monitor · **C** toggles
/// click-through · **R** toggles roaming · **W** enters the Warren · **drag** to
/// move it.
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
    [Export] public float WalkSpeed { get; set; } = 44;

    /// Keeps the pet on one horizontal line. The roaming pattern carries small
    /// vertical offsets — 0.04 and -0.03 of the available height — and they are
    /// the reason a walk can read as a drift: there is one walk cycle, it walks
    /// left and right, and any vertical component is the model sliding rather
    /// than stepping. Flattening the pattern is a renderer-side decision, so the
    /// ported intent is left alone and a flattened copy is used instead.
    [Export] public bool HorizontalOnly { get; set; } = true;

    /// How far the pet turns toward where it is going, in degrees off
    /// facing-you. It walks across the screen, so it should not be walking
    /// sideways — but a full 90 degrees turns its face away, and the face is the
    /// point. Negate this if it turns the wrong way.
    [Export] public float TurnDegrees { get; set; } = 38;

    /// How long the turn takes. Slower than it sounds like it should be: a pet
    /// that snaps around reads as a sprite flipping.
    [Export] public float TurnSeconds { get; set; } = 0.45f;

    /// How much to magnify the right-click menu. 0 asks the display — which on a
    /// Retina screen is 2, and without it the menu draws at half the size of
    /// every other menu on the machine.
    [Export] public float MenuScale { get; set; }

    /// Which monitor to open on. -1 opens on whichever the window landed on.
    [Export] public int Screen { get; set; } = -1;

    private StageActor _pet = null!;
    private PetMenu _menu = null!;
    private DungeonWindow _dungeon = null!;
    /// True while a delve is running. The pet is not on the desktop then — it is
    /// down there — so nothing wanders, nothing responds to a click, and the
    /// menu offers to bring the dungeon forward rather than to open a second one.
    private bool _away;
    private PetState _state = null!;
    private PetStateFileStore _store = null!;
    private SaveLocation _save;
    private readonly PetBrain _brain = new();
    /// Cleared when the save could not be read, so a file this build cannot
    /// parse is never overwritten. Same posture as the dungeon.
    private bool _saves = true;
    private int _screen;

    private enum Wander { Resting, Travelling }
    private Wander _wander = Wander.Resting;
    private ulong _sequence;
    private double _timer;
    private PlacementPoint _from, _to;
    private double _travelTotal;

    private bool _dragging;
    private bool _dragged;
    private Vector2I _grabOffset;
    private float _facing;
    private float _facingTarget;

    public override void _Ready()
    {
        var window = GetWindow();
        DesktopWindow.MakeCompanion(window);
        window.Size = WindowSize;

        // The menu must be a real OS window, not one drawn inside this one.
        // Godot embeds child windows in the parent viewport by default, which
        // for a 320x320 pet window means the right-click menu is rendered inside
        // it and clipped to it — presenting as a menu that is tiny, cut off at
        // the edges, and whose submenus open on top of their own parent.
        window.GuiEmbedSubwindows = false;

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

        LoadState();
        _menu = new PetMenu(this) { Scale = MenuScale };
        _menu.Chosen += OnMenuChoice;

        _dungeon = new DungeonWindow(this);
        _dungeon.Resolved += OnDelveResolved;
        _dungeon.Closed += OnDungeonClosed;

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
        GD.Print("Esc quit · Tab next monitor · C click-through · R roam · W Warren "
               + "· right-click for the menu · drag to move");
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
        if (HorizontalOnly)
        {
            intent = new PetRoamingIntent(
                intent.HorizontalOffset, 0, intent.RestDuration, intent.TravelDuration);
        }
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
        if (_away) return;
        TurnTowardTravel(delta);
        if (_away || !Roam || _dragging) return;

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

    // MARK: - The pet itself

    /// Reads the saved Workling and advances it to now, so a pet left overnight
    /// is hungry when the app opens rather than frozen where it was left.
    private void LoadState()
    {
        _save = SaveLocation.Resolve();
        _store = new PetStateFileStore(_save.Path);
        GD.Print($"Workling: {_save.Path} "
               + $"({(_save.IsShared ? "the real save" : "not the real save")} — {_save.Reason})");
        try
        {
            _state = _store.Load() ?? PetState.NewPet(now: System.DateTimeOffset.Now);
        }
        catch (System.Exception error)
        {
            GD.PushWarning($"Could not read {_save.Path}: {error.Message}. "
                         + "Running from a new pet; this session will not save.");
            _state = PetState.NewPet(now: System.DateTimeOffset.Now);
            _saves = false;
        }
        _state = _brain.Advance(_state, System.DateTimeOffset.Now);
        GD.Print($"{_state.Name} · Lv {_state.Level} · {_state.Mood}");
    }

    private void SaveState()
    {
        if (!_saves) return;
        try
        {
            _store.Save(_state);
        }
        catch (System.Exception error)
        {
            GD.PushWarning($"Could not write {_save.Path}: {error.Message}");
            _saves = false;
        }
    }

    /// Performs a care action and writes the result. Saving on every action
    /// rather than on a timer is deliberate: a desktop pet has no natural
    /// moment to close, so anything not written immediately is written never.
    private void Care(PetAction action)
    {
        var result = _brain.Perform(action, _state, System.DateTimeOffset.Now);
        _state = result.State;
        SaveState();
        GD.Print($"{_state.Name}: {result.Reaction.RawValue()} "
               + $"· Lv {_state.Level} · {_state.Mood}");
    }

    private void OnMenuChoice(PetMenuChoice choice, PetFood? food, PetPlayActivity? play)
    {
        switch (choice)
        {
            case PetMenuChoice.Feed when food.HasValue:
                Care(PetAction.Feed(food.Value));
                break;
            case PetMenuChoice.Play when play.HasValue:
                Care(PetAction.Playing(play.Value));
                break;
            case PetMenuChoice.Pet:
                Care(PetAction.Pet);
                break;
            case PetMenuChoice.Sleep:
                Care(PetAction.Sleep);
                break;
            case PetMenuChoice.StayPut:
                Roam = !Roam;
                if (Roam) BeginResting();
                else _pet.Play(ActorAction.Idle, loop: true);
                GD.Print($"roaming: {Roam}");
                break;
            case PetMenuChoice.EnterTheWarren:
                EnterTheWarren();
                break;
            case PetMenuChoice.Quit:
                GetTree().Quit();
                break;
        }
    }

    // MARK: - The Warren

    /// The pet goes down. Its body leaves the desktop and the delve opens in its
    /// own window; the Workling itself is handed across rather than re-read, so
    /// there is exactly one live copy of it while the run is on.
    private void EnterTheWarren()
    {
        if (_away)
        {
            _dungeon.Open(_state, _screen);
            return;
        }

        _away = true;
        _dragging = false;
        SetPetVisible(false);
        _dungeon.Open(_state, _screen);
    }

    /// The run resolved. What comes back is the Workling that walked out — XP,
    /// gear and condition — and the pet, which owns the save, is the thing that
    /// writes it.
    private void OnDelveResolved(PetState state)
    {
        _state = state;
        SaveState();
        GD.Print($"back from the Warren: {_state.Name} · Lv {_state.Level} · {_state.Mood}");
    }

    private void OnDungeonClosed()
    {
        _away = false;
        SetPetVisible(true);
        BeginResting();
    }

    /// Emptying the window rather than hiding it: Godot refuses to change the
    /// main window's visibility, and a transparent window drawing nothing is
    /// nothing anyway.
    private void SetPetVisible(bool visible) => _pet.Root.Visible = visible;

    // MARK: - Input

    public override void _UnhandledInput(InputEvent @event)
    {
        // While the pet is in the Warren its window is empty, but it is still
        // there and still on top. Without this a click on whatever is behind it
        // would pet an animal that is not on the desktop.
        if (_away && @event is InputEventMouse)
        {
            return;
        }

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
                case Key.W:
                    EnterTheWarren();
                    return;
            }
        }

        // Right-click opens the menu. It is the only way in — a borderless
        // window has nothing else to click.
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true })
        {
            _menu.Open(_state, Roam, DisplayServer.MouseGetPosition());
            return;
        }

        // Dragging works in screen coordinates, not window ones: the window is
        // moving underneath the pointer, so a delta read from the window's own
        // mouse position chases itself and the pet slides away from the cursor.
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } click)
        {
            if (click.Pressed)
            {
                _dragging = true;
                _dragged = false;
                _grabOffset = DisplayServer.MouseGetPosition() - GetWindow().Position;
            }
            else
            {
                // A click that did not move the window is a click on the animal,
                // and clicking your pet should do the obvious thing. The drag
                // check is what stops every reposition also petting it.
                if (_dragging && !_dragged) Care(PetAction.Pet);
                _dragging = false;
                BeginResting();
            }
        }
        else if (@event is InputEventMouseMotion && _dragging)
        {
            var moved = DisplayServer.MouseGetPosition() - _grabOffset;
            // A few pixels of slop, because a click is never perfectly still and
            // a hand that twitches should still be petting rather than dragging.
            if (moved.DistanceSquaredTo(GetWindow().Position) > 9) _dragged = true;
            GetWindow().Position = moved;
        }
    }
}
