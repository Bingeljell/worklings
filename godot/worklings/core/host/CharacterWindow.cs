using Godot;
using Worklings.Core.Pet;

namespace Worklings.Core.Host;

/// The Workling's own screen: who it is, what it can do, and what it is carrying.
///
/// **Why this is not optional.** Gear drops in a delve and then disappears from
/// view until the next prep screen. Levels go up in a log nobody reads. Without
/// this, everything the player earns is invisible between one delve and the next,
/// which makes earning it feel like nothing happened.
///
/// The third window, and the easiest of the three: freely resizable, opaque,
/// ordinary. That it can be a different shape from the pet's fixed square and the
/// dungeon's locked 16:9 is exactly why the app is multi-window — see
/// "Two windows, and why not one" in the port status doc.
///
/// Follows the Swift app's alpha.9 design — hub window, tabs, gear rail, model
/// bay — which is SwiftUI, so this is a rebuild against the same decisions rather
/// than a port.
public sealed class CharacterWindow
{
    private readonly Node _host;
    private Window? _window;
    private CharacterPanel? _panel;

    /// Raised when the player changes gear, since equipping is a `PetState`
    /// operation and the pet owns the save.
    public event System.Action<PetState>? StateChanged;

    public event System.Action? Closed;

    public bool IsOpen => _window is not null;

    public CharacterWindow(Node host)
    {
        _host = host;
    }

    public void Open(PetState state, int screen)
    {
        if (_window is not null)
        {
            _panel?.Show(state);
            _window.GrabFocus();
            return;
        }

        var frame = DesktopWindow.UsableFrame(screen);
        // Portrait-ish and roughly half the screen's height: a character sheet is
        // a column of rows, and a wide window would be mostly empty.
        // Sized against the screen, and generous with it. A window's size is in
        // PHYSICAL pixels, so a number that reads as roomy on paper comes out
        // half that in points on a 2x display — the trap that has now produced
        // a letterboxed pet, a half-size menu and a thumbnail dungeon.
        var size = new Vector2I(
            (int)System.Math.Round(frame.Width * 0.34),
            (int)System.Math.Round(frame.Height * 0.80));

        _window = new Window
        {
            Title = "Workling",
            Size = size,
            Position = new Vector2I(
                (int)(frame.X + (frame.Width - size.X) / 2),
                (int)(frame.Y + (frame.Height - size.Y) / 2)),
        };
        _host.AddChild(_window);

        float scale = (float)DisplayServer.ScreenGetScale(screen);
        _panel = new CharacterPanel(scale);
        _panel.StateChanged += OnStateChanged;
        _window.AddChild(_panel);
        _panel.Show(state);

        _window.CloseRequested += Close;
        _window.Show();
        _window.GrabFocus();
    }

    /// Re-reads the screen from a state changed elsewhere — a delve resolving, a
    /// care action — so an open window never shows a stale Workling.
    public void Refresh(PetState state) => _panel?.Show(state);

    private void OnStateChanged(PetState state) => StateChanged?.Invoke(state);

    public void Close()
    {
        if (_window is null)
        {
            return;
        }
        if (_panel is not null)
        {
            _panel.StateChanged -= OnStateChanged;
            _panel = null;
        }
        var going = _window;
        _window = null;
        going.QueueFree();
        Closed?.Invoke();
    }
}
