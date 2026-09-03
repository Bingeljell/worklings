using Godot;
using Worklings.Core.Pet;

namespace Worklings.Core.Host;

/// The dungeon, as a second window opened from the pet.
///
/// **Why a second window rather than one window switching modes** — see the port
/// status doc. In short: one window would have to mutate transparency,
/// always-on-top, borderless, size and content scaling live, five state changes
/// across three operating systems, all proven at launch and none proven when
/// toggled. Two windows configures each once and never touches it again, and the
/// letterboxing trap stops being something to manage.
///
/// The pet stays the *main* window, which is where the transparency is already
/// proven; this is the ordinary one.
public sealed class DungeonWindow
{
    private readonly Node _host;
    private Window? _window;
    private CacheWarrenScene? _scene;

    /// The Workling that walked out, once a run resolves. The host owns the save,
    /// so this is how the result gets home.
    public event System.Action<PetState>? Resolved;

    /// Raised when the window has gone, whether the run finished or the player
    /// closed it. The pet comes back on this.
    public event System.Action? Closed;

    public bool IsOpen => _window is not null;

    public DungeonWindow(Node host)
    {
        _host = host;
    }

    /// Opens at the project's full 1920x1080, shrunk to fit if the screen cannot
    /// hold it. The dungeon is a fixed-aspect scaling stage, so a smaller window
    /// is *correct* rather than broken — but it is also small, and a delve
    /// deserves the screen. 720p was the first guess and read as a thumbnail.
    ///
    /// The fit keeps 16:9 exactly: anything else and the stage letterboxes inside
    /// its own window.
    public void Open(PetState state, int screen)
    {
        if (_window is not null)
        {
            // Already down there. Bring it forward rather than opening a second
            // delve on the same pet — two runs resolving into one Workling would
            // each write back a result computed from the same starting state.
            _window.MoveToForeground();
            _window.GrabFocus();
            return;
        }

        var frame = DesktopWindow.UsableFrame(screen);

        // 90% of the usable area at most, so the title bar and the dock are not
        // fighting it, and never upscaled past the project's own render size.
        double fit = System.Math.Min(1.0, System.Math.Min(
            frame.Width * 0.9 / 1920.0, frame.Height * 0.9 / 1080.0));
        var size = new Vector2I(
            (int)System.Math.Round(1920 * fit), (int)System.Math.Round(1080 * fit));

        _window = new Window
        {
            Title = "The Cache Warren",
            Size = size,
            // Centred on the pet's screen, in that screen's own coordinates —
            // which for a monitor left of the primary one are negative.
            Position = new Vector2I(
                (int)(frame.X + (frame.Width - size.X) / 2),
                (int)(frame.Y + (frame.Height - size.Y) / 2)),
            // Its own 3D world. Without this the dungeon renders the *pet's*
            // room, lit by the pet's lights, with its own scene invisible inside
            // it — which reads as a broken scene rather than a window setting.
            World3D = new World3D(),
            // The dungeon keeps its aspect; the pet window disables scaling
            // entirely. Two windows is what lets both be true at once.
            ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems,
            ContentScaleAspect = Window.ContentScaleAspectEnum.Keep,
            ContentScaleSize = new Vector2I(1920, 1080),
        };

        _host.AddChild(_window);

        _scene = GD.Load<PackedScene>("res://scenes/cache_warren.tscn")
            .Instantiate<CacheWarrenScene>();
        // Set before the scene enters the tree, so _Ready finds it and never
        // reaches for the save file.
        _scene.HostedState = state;
        // A hosted delve is one delve. Looping is for working on the dungeon
        // alone, where a scene that stops is a scene you cannot see.
        _scene.Loop = false;
        _scene.Resolved += OnResolved;
        // The run ends, the summary has its moment, and then the pet comes back
        // up. Without this the window sits on a finished summary until the
        // player closes it by hand — which is fine for working on the dungeon
        // alone and wrong for a delve you sent your Workling on.
        _scene.Finished += Close;
        _window.AddChild(_scene);

        _window.CloseRequested += Close;
        _window.Show();
        // A new window in an app that is not frontmost opens behind whatever the
        // player is looking at. Entering a delve is a deliberate act.
        _window.MoveToForeground();
        _window.GrabFocus();
    }

    private void OnResolved(PetState state) => Resolved?.Invoke(state);

    /// Frees the window and tells the host. Safe to call twice — the player
    /// closing the window and the run ending can race.
    public void Close()
    {
        if (_window is null)
        {
            return;
        }
        if (_scene is not null)
        {
            _scene.Resolved -= OnResolved;
            _scene.Finished -= Close;
            _scene = null;
        }
        var going = _window;
        _window = null;
        going.QueueFree();
        Closed?.Invoke();
    }
}
