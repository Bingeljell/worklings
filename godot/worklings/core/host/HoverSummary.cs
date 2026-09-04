using Godot;
using Worklings.Core.Pet;

namespace Worklings.Core.Host;

/// One sentence about how the Workling is doing, floating above it while the
/// pointer rests on it.
///
/// **The whole point is that it costs nothing to ask.** Condition is otherwise
/// only visible by opening the menu or the character screen, which is a decision
/// rather than a glance — so a pet that is quietly starving reads exactly like a
/// pet that is fine. The sentence comes from `PetCareStatus`, the same one the
/// menu's mood word comes from, so the two can never disagree.
///
/// Ported in behaviour from Sources/Worklings/HoverSummaryPanelController.swift.
/// A window rather than something drawn inside the pet's own: the pet window is
/// 320 **physical** pixels wide, which on a 2x display is 160 points, and a
/// sentence does not fit in it.
public sealed class HoverSummary
{
    private readonly Node _host;
    private readonly float _scale;
    private Window? _window;
    private Label? _label;

    /// The gap between the pet and the panel. Enough to read as a separate
    /// thing, not so much that it looks unattached.
    private const int Spacing = 8;

    public HoverSummary(Node host, float scale)
    {
        _host = host;
        _scale = scale;
    }

    private int S(float units) => System.Math.Max(1, (int)System.Math.Round(units * _scale));

    public void Show(PetState state, Window anchor)
    {
        string summary = PetCareStatus.Make(state).HoverSummary;
        Build();
        _label!.Text = summary;

        var size = new Vector2I(S(260), S(56));
        _window!.Size = size;
        _window.Position = Place(anchor, size);
        _window.Show();
    }

    public void Hide() => _window?.Hide();

    /// Frees the window. Called when the scene goes, so a borderless always-on-
    /// top panel can never outlive the pet it belongs to.
    public void Close()
    {
        if (_window is null) return;
        var going = _window;
        _window = null;
        _label = null;
        going.QueueFree();
    }

    private void Build()
    {
        if (_window is not null) return;

        _window = new Window
        {
            Borderless = true,
            AlwaysOnTop = true,
            Unfocusable = true,
            // It must never take a click. The pet underneath is what the pointer
            // is aimed at, and a panel that swallowed the click would make the
            // animal unpettable exactly when you are looking at it.
            MousePassthrough = true,
            Transparent = true,
            TransparentBg = true,
            ContentScaleMode = Window.ContentScaleModeEnum.Disabled,
            ContentScaleAspect = Window.ContentScaleAspectEnum.Ignore,
            Visible = false,
        };
        _host.AddChild(_window);

        var panel = new PanelContainer();
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var background = new StyleBoxFlat
        {
            BgColor = new Color(0.09f, 0.09f, 0.11f, 0.94f),
            BorderColor = WorklingsTheme.Brass with { A = 0.45f },
        };
        background.SetCornerRadiusAll(S(14));
        background.SetBorderWidthAll(S(1));
        background.ContentMarginLeft = background.ContentMarginRight = S(14);
        background.ContentMarginTop = background.ContentMarginBottom = S(8);
        panel.AddThemeStyleboxOverride("panel", background);
        _window.AddChild(panel);

        _label = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _label.AddThemeFontOverride(
            "font", GD.Load<Font>("res://assets/fonts/ChakraPetch-SemiBold.ttf"));
        _label.AddThemeFontSizeOverride("font_size", S(13));
        _label.AddThemeColorOverride("font_color", WorklingsTheme.Ink);
        panel.AddChild(_label);
    }

    private Vector2I Place(Window anchor, Vector2I size) => Place(
        anchor.Position, anchor.Size, size, S(Spacing),
        DesktopWindow.UsableFrame(
            DisplayServer.WindowGetCurrentScreen(anchor.GetWindowId())));

    /// Centred over the pet and above it, flipped below when there is no room,
    /// and clamped to the screen either way — the pet roams into corners, which
    /// is exactly where a panel placed by offset alone ends up off-screen.
    ///
    /// Static and pure so the flip can be checked at all. macOS will not put a
    /// window under the menu bar, so a real pet window can never sit high enough
    /// to need the flip — but a monitor arranged above another one can, and so
    /// can Windows and Linux. Untestable through a window, trivially testable
    /// here.
    public static Vector2I Place(
        Vector2I petPosition, Vector2I petSize, Vector2I panelSize, int spacing,
        PlacementRect frame)
    {
        int x = petPosition.X + petSize.X / 2 - panelSize.X / 2;
        int y = petPosition.Y - panelSize.Y - spacing;
        if (y < frame.Y)
        {
            y = petPosition.Y + petSize.Y + spacing;
        }

        return new Vector2I(
            System.Math.Clamp(x, (int)frame.X, (int)(frame.X + frame.Width) - panelSize.X),
            System.Math.Clamp(y, (int)frame.Y, (int)(frame.Y + frame.Height) - panelSize.Y));
    }
}
