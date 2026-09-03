using Godot;

namespace Worklings.Core.Host;

/// The desktop pet's window: transparent, borderless, always on top, and mostly
/// click-through.
///
/// **This has no Swift original.** Everything else on the Godot side is a port
/// with a reference implementation to diff against; this is the piece where a
/// "port" stops being a port, and it is the reason to prove the shell before
/// moving PetBrain across. If Godot cannot be a desktop pet window, that is worth
/// finding out against an empty window rather than after 543 lines of behaviour
/// have been ported to sit inside it.
///
/// Four traits make a companion window, and each is a separate mechanism:
///
/// - **Borderless** — no title bar, no chrome. A pet is not a document.
/// - **Always on top** — it is a companion to the work, so it sits above it.
/// - **Transparent** — per-pixel alpha, so the shape on screen is the animal and
///   not a rectangle it lives in. This one is the fussy one: it needs
///   `display/window/per_pixel_transparency/allowed=true` in project.godot,
///   which is a *project* setting and cannot be turned on at runtime, plus the
///   viewport's own `TransparentBg` and an environment that does not paint a
///   background over it.
/// - **Click-through** — the window must not eat clicks meant for the editor
///   behind it. Godot's mechanism is a polygon of the window that *keeps* mouse
///   events; everything outside it passes through. An empty polygon means the
///   whole window keeps them, which is the default and exactly wrong here.
public static class DesktopWindow
{
    /// Applies the four traits. Transparency is requested on both the window and
    /// its viewport because they are genuinely two settings: the flag asks the OS
    /// for an alpha channel, `TransparentBg` stops the renderer filling it in.
    public static void MakeCompanion(Window window)
    {
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, true);
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.AlwaysOnTop, true);
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Transparent, true);
        window.TransparentBg = true;

        // The project renders at 1920x1080 with the aspect kept, which is right
        // for the dungeon and wrong here: a square pet window letterboxes the
        // 16:9 content and fills the leftover with *opaque black*, so the pet
        // arrives wearing two bars. Content scaling is off for this window — it
        // renders at whatever size the window is.
        window.ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
        window.ContentScaleAspect = Window.ContentScaleAspectEnum.Ignore;
    }

    /// Whether the build is actually able to be transparent. False means the
    /// project setting is off, and the pet will be a grey rectangle rather than
    /// an animal — worth saying out loud instead of leaving to be noticed.
    public static bool TransparencyAllowed =>
        (bool)ProjectSettings.GetSetting("display/window/per_pixel_transparency/allowed", false);

    /// The rectangle of the window that keeps mouse events. Everything outside it
    /// passes through to whatever is behind.
    ///
    /// A rectangle rather than the pet's silhouette: Godot takes a polygon, not
    /// an alpha test, so a per-pixel hit region would mean generating a hull from
    /// the model every frame it moves. A box around the body is the honest
    /// placeholder, and it is what decides whether a click lands on the pet or on
    /// the editor behind it.
    public static void SetInteractiveRegion(Rect2 region)
    {
        DisplayServer.WindowSetMousePassthrough(new[]
        {
            region.Position,
            new Vector2(region.End.X, region.Position.Y),
            region.End,
            new Vector2(region.Position.X, region.End.Y),
        });
    }

    /// Gives the whole window back its clicks — the state to be in when something
    /// modal is on screen, and the one to compare against when judging whether
    /// click-through is working at all.
    public static void ClearInteractiveRegion() =>
        DisplayServer.WindowSetMousePassthrough(System.Array.Empty<Vector2>());

    /// The screen area a window may occupy, menu bar and dock excluded — the
    /// same thing AppKit calls `visibleFrame`.
    ///
    /// Returned in the screen's own coordinates, which for a monitor placed left
    /// of or above the primary one are negative. ScreenPlacement is written to
    /// expect that; almost nothing else is.
    public static PlacementRect UsableFrame(int screen)
    {
        var rect = DisplayServer.ScreenGetUsableRect(screen);
        return new PlacementRect(rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y);
    }

    public static PlacementSize SizeOf(Window window) =>
        new PlacementSize(window.Size.X, window.Size.Y);

    public static PlacementPoint OriginOf(Window window) =>
        new PlacementPoint(window.Position.X, window.Position.Y);

    /// Rounds rather than truncates. Roaming produces fractional origins, and
    /// truncating each one biases every move a fraction of a pixel toward the
    /// top-left — invisible in a step and a visible drift over an afternoon.
    public static void MoveTo(Window window, PlacementPoint origin) =>
        window.Position = new Vector2I(
            (int)System.Math.Round(origin.X), (int)System.Math.Round(origin.Y));
}
