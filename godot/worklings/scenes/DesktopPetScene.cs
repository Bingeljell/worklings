using Godot;
using Worklings.Core.Host;
using Worklings.Core.Connect;
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

    /// The yaw at which the pet is actually looking at you, in degrees.
    ///
    /// **Not zero.** The camera sits off to one side — (2.2, 1.5, 3.8) in
    /// `desktop_pet.tscn` — so a model at yaw 0 faces world +Z, which is about
    /// 30 degrees away from the viewer. Turning symmetrically around 0 therefore
    /// turns *unevenly* around the camera: one direction reads as a proper
    /// profile and the other barely turns at all, and doubling the angle sends
    /// the pet facing away from the screen entirely. Everything below is
    /// measured from here rather than from zero.
    [Export] public float FacingYaw { get; set; } = 30;

    /// How far the pet turns toward where it is going, in degrees off
    /// facing-you.
    ///
    /// **Near enough to a full profile.** This was 38 degrees, chosen so the face
    /// stayed visible, and on a real desktop it read as the animal facing *you*
    /// while sliding sideways — worse than a clean profile, because the eye
    /// reads the direction the body is pointing and disbelieves the movement. It
    /// turns properly now and comes back to face you when it stops, which is
    /// when the face actually matters. Negate to turn the other way.
    [Export] public float TurnDegrees { get; set; } = 78;

    /// How long the turn takes. Slower than it sounds like it should be: a pet
    /// that snaps around reads as a sprite flipping.
    [Export] public float TurnSeconds { get; set; } = 0.45f;

    /// How much to magnify the right-click menu. 0 asks the display — which on a
    /// Retina screen is 2, and without it the menu draws at half the size of
    /// every other menu on the machine.
    [Export] public float MenuScale { get; set; }

    /// How long the puff of smoke takes when the pet leaves or arrives. Short:
    /// it is a transition, not a cutscene. 0 skips it entirely, which is the
    /// state to be in when judging anything else about the handover.
    [Export] public float SmokeSeconds { get; set; } = 0.7f;

    /// How wide the puff is drawn, as a multiple of the window. Bigger than the
    /// window on purpose: the cloud has to cover the animal, and the art only
    /// fills the lower half of its own cell.
    [Export] public float SmokeSpread { get; set; } = 1.6f;

    /// Where the puff sits vertically, as a fraction of the window height. The
    /// cloud is drawn low in its 256px cell, so centring the *sprite* leaves the
    /// smoke under the pet's feet rather than over its body.
    [Export] public float SmokeHeight { get; set; } = 0.38f;

    /// Which monitor to open on. -1 opens on whichever the window landed on.
    [Export] public int Screen { get; set; } = -1;

    private StageActor _pet = null!;
    private PetMenu _menu = null!;
    private DungeonWindow _dungeon = null!;
    private CharacterWindow _character = null!;
    private PetThought? _thought;
    /// True while a delve is running. The pet is not on the desktop then — it is
    /// down there — so nothing wanders, nothing responds to a click, and the
    /// menu offers to bring the dungeon forward rather than to open a second one.
    private bool _away;
    /// How often needs move on their own. A minute, matching the Swift app —
    /// the rates are per-hour, so the cadence only decides how smoothly the
    /// numbers slide rather than where they end up.
    private const double TickSeconds = 60;

    private PetSession _session = null!;
    private ActivityInboxWatcher _inbox = null!;
    private PresenceWatcher _presence = null!;
    private GitCommitWatcher _git = null!;
    private WakeStamp _wake = null!;
    /// Seconds until the next needs tick. Without one the pet only ages when it
    /// is interacted with, which is exactly backwards for a creature whose whole
    /// point is that it gets hungry while you are busy.
    private double _tick = TickSeconds;
    /// Cleared when the save could not be read, so a file this build cannot
    /// parse is never overwritten. Same posture as the dungeon.
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
    private bool _facingStarted;

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
        // Start looking at the viewer rather than easing round to it.
        _facing = _facingTarget = FacingYaw;
        _pet.Root.RotationDegrees = new Vector3(0, _facing, 0);

        StartSession();
        _menu = new PetMenu(this) { Scale = MenuScale };
        _menu.Chosen += OnMenuChoice;
        _menu.DisconnectRepo += path =>
        {
            _git.Disconnect(path);
            Say($"Not watching {System.IO.Path.GetFileName(path)}.");
        };

        _dungeon = new DungeonWindow(this);
        _dungeon.Resolved += OnDelveResolved;
        _dungeon.Closed += OnDungeonClosed;

        _character = new CharacterWindow(this);
        // Gear changes are PetState operations and the session owns the save, so
        // the screen proposes and the session writes.
        _character.StateChanged += _session.Replace;

        // The one thing that makes any of the activity work visible: something
        // outside the app dropping a file, and the pet noticing.
        _inbox = new ActivityInboxWatcher(_session);
        AddChild(_inbox);

        _presence = new PresenceWatcher(_session);
        AddChild(_presence);

        _git = new GitCommitWatcher(_session);
        AddChild(_git);

        // Last, once every window and listener exists. Greeting any earlier
        // means the pet changes before there is anything to show it on — which
        // it did, loudly, from inside the constructor.
        _session.Greet(System.DateTimeOffset.Now, _wake.Read());

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
               + "· S sheet · right-click for the menu · drag to move");
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
        _facingTarget = FacingYaw;
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
        // Increasing yaw turns the pet toward screen-right, measured from
        // facing-you rather than from zero.
        _facingTarget = FacingYaw + (dx >= 0 ? TurnDegrees : -TurnDegrees);
        _pet.Play(ActorAction.Walk, loop: true);
    }

    public override void _Process(double delta)
    {
        // Runs even while the pet is in the Warren. Time passes down there too,
        // and a delve that takes ten minutes should not leave the Workling
        // exactly as hungry as it was when it walked in.
        _tick -= delta;
        if (_tick <= 0)
        {
            _tick = TickSeconds;
            _session.Advance(System.DateTimeOffset.Now, _wake.Read());
        }

        if (_away) return;
        ReleaseIfButtonGone();
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
    /// Brings the Workling up and wires everything that can change it to the
    /// one thing allowed to write it.
    private void StartSession()
    {
        _wake = new WakeStamp();
        _session = new PetSession(System.DateTimeOffset.Now);
        GD.Print($"Workling: {_session.Save.Path} "
               + $"({(_session.Save.IsShared ? "the real save" : "not the real save")}"
               + $" — {_session.Save.Reason})");

        _session.Woke += _wake.Write;
        _session.Reacted += OnReaction;
        // One place the screen is refreshed from, rather than every path that
        // changes the pet remembering to.
        // Null-guarded: the session is built before the windows are, and the
        // greeting below can change the pet before there is a screen to refresh.
        _session.StateChanged += state => _character?.Refresh(state);

        GD.Print($"{_session.State.Name} · Lv {_session.State.Level} · {_session.State.Mood}");
    }

    /// Performs a care action and writes the result. Saving on every action
    /// rather than on a timer is deliberate: a desktop pet has no natural
    /// moment to close, so anything not written immediately is written never.
    private void Care(PetAction action)
    {
        // The session refuses an action the pet does not need — feeding one that
        // is already full — and the menu greys those out, so a refusal here is
        // only ever reached by a click on the animal itself. The reaction, if
        // there is one, arrives through Reacted like every other.
        _session.Perform(action, System.DateTimeOffset.Now);
    }

    /// Everything the pet reacts to, from either direction — a button you
    /// pressed or an event it noticed — arrives here. Printed as well as spoken,
    /// because a thought over a pet's head cannot be seen in a headless run and
    /// the activity path is otherwise entirely silent.
    private void OnReaction(PetReaction reaction)
    {
        Say(reaction);
        GD.Print($"{_session.State.Name}: {reaction.RawValue()} "
               + $"· Lv {_session.State.Level} · {_session.State.Mood}");
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
            case PetMenuChoice.FocusSession:
                _session.ToggleFocusSession(System.DateTimeOffset.Now);
                GD.Print($"focus session: {_session.IsFocusSessionActive}");
                break;
            case PetMenuChoice.LogWork:
                _session.LogWork(System.DateTimeOffset.Now);
                break;
            case PetMenuChoice.StayPut:
                Roam = !Roam;
                if (Roam) BeginResting();
                else _pet.Play(ActorAction.Idle, loop: true);
                GD.Print($"roaming: {Roam}");
                break;
            case PetMenuChoice.ToggleClaudeCode:
                ToggleTool(ConnectableTool.ClaudeCode);
                break;
            case PetMenuChoice.ToggleCodex:
                ToggleTool(ConnectableTool.Codex);
                break;
            case PetMenuChoice.ConnectRepo:
                ConnectARepository();
                break;
            case PetMenuChoice.CharacterSheet:
                _character.Open(_session.State, _screen);
                break;
            case PetMenuChoice.EnterTheWarren:
                EnterTheWarren();
                break;
            case PetMenuChoice.Quit:
                GetTree().Quit();
                break;
        }
    }

    /// Wires a tool's hooks in, or takes them out again.
    ///
    /// One item that toggles, because there are only two states worth offering
    /// and a pair of items would leave one of them always doing nothing. A stale
    /// connection — ours, but pointing at an app that has moved — reconnects
    /// rather than disconnects, which is what the menu's wording promises.
    ///
    /// Everything that can go wrong here is reported rather than swallowed.
    /// These are the user's own config files; a silent failure would leave them
    /// believing a tool is wired up when it is not.
    private void ToggleTool(ConnectableTool tool)
    {
        var connector = tool.Connector();
        try
        {
            switch (connector.State())
            {
                case ConnectionState.Live:
                {
                    string? backup = connector.Disconnect();
                    GD.Print($"{tool.DisplayName()}: disconnected"
                           + $"{(backup is null ? "" : $", backed up to {backup}")}");
                    break;
                }
                case ConnectionState.Unknown:
                    // Refused rather than attempted. The menu disables this, so
                    // reaching it means the file changed since the menu opened.
                    GD.PushWarning($"{tool.DisplayName()}: {connector.ConfigPath} could not be "
                                 + "read or parsed, so it was left alone.");
                    break;
                default:
                {
                    string? backup = connector.Connect();
                    GD.Print($"{tool.DisplayName()}: connected"
                           + $"{(backup is null ? "" : $", backed up to {backup}")}");
                    break;
                }
            }
        }
        catch (ConnectorException error)
        {
            GD.PushWarning($"{tool.DisplayName()}: {error.Error} — {error.Path}");
        }
        catch (HookMergeException error)
        {
            // The config is present but not shaped the way we understand, so it
            // was left exactly as it was.
            GD.PushWarning($"{tool.DisplayName()}: {connector.ConfigPath} was left untouched "
                         + $"({error.Error}).");
        }
    }

    /// Asks for a folder and hands it to the git watcher.
    ///
    /// The OS's own dialog rather than Godot's `FileDialog`, which would draw
    /// inside a 320-pixel transparent window — the same trap the right-click
    /// menu fell into. Picking a directory is the whole interaction: connecting
    /// a repository IS the opt-in to the git source, so there is nothing else to
    /// confirm afterwards.
    private void ConnectARepository()
    {
        var chosen = Callable.From((bool ok, string[] paths, int _) =>
        {
            if (!ok || paths.Length == 0) return;
            if (_git.Connect(paths[0]) is string refusal)
            {
                // Said, not just printed. A folder picker that closes and does
                // nothing visible is indistinguishable from one that worked.
                GD.Print($"git: {refusal}");
                Say(refusal.EndsWith("is already connected.")
                    ? "Already watching that one."
                    : "That's not a repository.");
                return;
            }
            Say($"Watching {System.IO.Path.GetFileName(paths[0].TrimEnd('/'))}!");
        });

        DisplayServer.FileDialogShow(
            title: "Connect a repository",
            currentDirectory: OS.GetSystemDir(OS.SystemDir.Documents),
            fileName: "",
            showHidden: false,
            mode: DisplayServer.FileDialogMode.OpenDir,
            filters: System.Array.Empty<string>(),
            callback: chosen);
    }

    // MARK: - The Warren

    /// The pet goes down. Its body leaves the desktop and the delve opens in its
    /// own window; the Workling itself is handed across rather than re-read, so
    /// there is exactly one live copy of it while the run is on.
    private void EnterTheWarren()
    {
        if (_away)
        {
            _dungeon.Open(_session.State, _screen);
            return;
        }

        _away = true;
        _dragging = false;

        // The pet vanishes under the thickest frame of the smoke, not at the
        // start of it. That is what makes it read as having left rather than as
        // having been switched off — the puff covers the cut.
        Puff(onCovered: () =>
        {
            SetPetVisible(false);
            _dungeon.Open(_session.State, _screen);
        });
    }

    /// The run resolved. What comes back is the Workling that walked out — XP,
    /// gear and condition — and the pet, which owns the save, is the thing that
    /// writes it.
    private void OnDelveResolved(PetState state)
    {
        // A character screen left open during a delve shows what came back out
        // of it, not what went in — the session's StateChanged does that.
        _session.Replace(state);
        GD.Print($"back from the Warren: {_session.State.Name} "
               + $"· Lv {_session.State.Level} · {_session.State.Mood}");
    }

    private void OnDungeonClosed()
    {
        _away = false;
        // The same puff, played the same way round: a cloud that gathers and
        // clears says "something happened here" in either direction, and the pet
        // reappears under the cover of it.
        Puff(onCovered: () =>
        {
            SetPetVisible(true);
            BeginResting();
        });
    }

    /// Plays a puff of smoke over the middle of the window, calling back on the
    /// frame it is thickest. With SmokeSeconds at 0 the callback runs at once and
    /// no smoke is drawn.
    private void Puff(System.Action onCovered)
    {
        if (SmokeSeconds <= 0)
        {
            onCovered();
            return;
        }

        var puff = new SmokePuff { Seconds = SmokeSeconds };
        puff.Covered += onCovered;
        AddChild(puff);
        var size = (Vector2)GetWindow().Size;
        puff.Position = new Vector2(size.X / 2, size.Y * SmokeHeight);
        puff.FitTo(size.X * SmokeSpread);
    }

    /// Floats what the pet thought over its head. One at a time — a second
    /// action while a line is still up replaces it rather than stacking, because
    /// two thoughts overlapping read as neither.
    private void Say(PetReaction reaction) => Say(PetThought.Thought(reaction));

    /// The same line, for something the pet is telling you rather than feeling —
    /// which repository it just started watching, say. Those have no
    /// `PetReaction` and should not get one: the vocabulary of reactions is the
    /// pet's inner life, not the app's status bar.
    private void Say(string text)
    {
        if (text.Length == 0) return;

        // A thought frees itself when it has faded, which leaves this field
        // holding a disposed object. Calling QueueFree on it throws, and the
        // throw happened *before* the new line was built — so the first click
        // after a line expired silently produced no line at all, and every one
        // after that too.
        //
        // IsInstanceValid is the check that survives a disposed wrapper;
        // TreeExiting below clears the field at the source so this is a
        // second line of defence rather than the only one.
        if (GodotObject.IsInstanceValid(_thought))
        {
            _thought!.QueueFree();
        }
        _thought = null;

        var size = (Vector2)GetWindow().Size;
        _thought = new PetThought
        {
            Text = text,
            Scale2D = MenuScale > 0
                ? MenuScale
                : (float)DisplayServer.ScreenGetScale(DisplayServer.WindowGetCurrentScreen()),
            // Above the animal's head, not over its face.
            Position = new Vector2(size.X / 2, size.Y * 0.17f),
        };
        // Cleared at the source the moment it leaves the tree, however it
        // leaves — faded out, replaced, or taken down with the scene.
        var thought = _thought;
        thought.TreeExiting += () =>
        {
            if (_thought == thought) _thought = null;
        };
        AddChild(thought);
    }

    /// Emptying the window rather than hiding it: Godot refuses to change the
    /// main window's visibility, and a transparent window drawing nothing is
    /// nothing anyway.
    private void SetPetVisible(bool visible) => _pet.Root.Visible = visible;

    /// Ends a drag whose mouse-up never arrived, which is most of them.
    ///
    /// The window moves under the cursor while dragging, and the button-up
    /// lands wherever the pointer happens to be by then — outside the window's
    /// interactive region, or swallowed by the move itself. Without this the pet
    /// stays stuck to the mouse for the rest of the session, following it around
    /// the screen, and nothing short of quitting gets it back.
    ///
    /// Polling the button state rather than trusting the event is the fix: the
    /// button either is down or it is not, and that is true regardless of which
    /// window the release was delivered to.
    private void ReleaseIfButtonGone()
    {
        if (!_dragging || Input.IsMouseButtonPressed(MouseButton.Left))
        {
            return;
        }
        EndDrag();
    }

    /// A click that did not move the window is a click on the animal, and
    /// clicking your pet should do the obvious thing.
    private void EndDrag()
    {
        bool wasAClick = !_dragged;
        _dragging = false;
        _dragged = false;
        if (wasAClick) Care(PetAction.Pet);
        BeginResting();
    }

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
                case Key.S:
                    _character.Open(_session.State, _screen);
                    return;
            }
        }

        // Right-click opens the menu. It is the only way in — a borderless
        // window has nothing else to click.
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true })
        {
            _menu.Open(_session, Roam, _git.Connected, DisplayServer.MouseGetPosition());
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
            else if (_dragging)
            {
                EndDrag();
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
