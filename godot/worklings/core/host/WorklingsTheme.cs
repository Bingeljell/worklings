using Godot;

namespace Worklings.Core.Host;

/// One look, in one place, for every surface the app puts on screen.
///
/// It exists because the right-click menu shipped in Godot's default theme —
/// generic grey, chunky rows, nothing to do with Worklings — and the fix should
/// not be a pile of per-control overrides that the next surface has to copy.
/// Everything here is taken from `LoadoutPanel`, which is the only Godot surface
/// that has been designed rather than assembled: the near-black panel, the warm
/// brass border, the parchment text.
///
/// **Sized in physical pixels.** The pet window disables content scaling — that
/// is what stops a square window letterboxing the project's 16:9 render size —
/// so a control built at the default font is half the size of everything else on
/// a Retina display. Every size here is multiplied by the display scale, which
/// is why `For` takes one.
public static class WorklingsTheme
{
    /// The panel behind everything. Nearly black, slightly warm, and not quite
    /// opaque so what is behind it stays present.
    public static readonly Color Panel = new(0.06f, 0.055f, 0.05f, 0.97f);

    /// Brass. The border, the separators, and anything that needs to read as
    /// made rather than drawn.
    public static readonly Color Brass = new(0.42f, 0.36f, 0.28f, 0.9f);

    /// Parchment, for text that matters.
    public static readonly Color Ink = new(0.90f, 0.87f, 0.80f, 1);

    /// The same, dimmed — headers, and anything present but not yet available.
    public static readonly Color Muted = new(0.62f, 0.58f, 0.52f, 1);

    /// What the cursor is on.
    public static readonly Color Highlight = new(0.20f, 0.18f, 0.15f, 1);

    /// Gear-given numbers and attunement, in the blue this game already uses for
    /// energy that came from somewhere else.
    ///
    /// **Load-bearing, not decorative.** Base and gear are always drawn apart,
    /// and this blue is the only thing that says which is which. Both the
    /// character screen and the loadout read it, which is why it lives here
    /// rather than on either one.
    public static readonly Color GearBlue = new(0.42f, 0.68f, 1f);

    /// Condition, when it is costing the Workling something.
    public static readonly Color Warning = new(0.85f, 0.65f, 0.35f);

    /// What the keyboard cursor is sitting on, and what a commitment is painted
    /// in. Amber rather than brass — brass is structure, this is attention.
    public static readonly Color Relic = new(0.88f, 0.64f, 0.25f);

    private const string BoldFont = "res://assets/fonts/ChakraPetch-Bold.ttf";
    private const string BodyFont = "res://assets/fonts/ChakraPetch-SemiBold.ttf";

    /// The two faces, for surfaces built outside a `Theme` — the dungeon's
    /// overlay has no theme to inherit from, and a shared widget drawn in the
    /// window's font on one screen and Godot's default on the other would not be
    /// the same widget.
    public static Font Bold => GD.Load<Font>(BoldFont);
    public static Font Body => GD.Load<Font>(BodyFont);

    /// A theme for popups and panels at `scale`, which should be the display's.
    ///
    /// Cached per scale: a theme is a resource, and building one per menu open
    /// would leak a font atlas every right-click.
    public static Theme For(float scale)
    {
        int key = (int)System.Math.Round(scale * 100);
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        int font = Size(15, scale);
        var theme = new Theme
        {
            DefaultFont = GD.Load<Font>(BodyFont),
            DefaultFontSize = font,
        };

        var panel = new StyleBoxFlat
        {
            BgColor = Panel,
            BorderColor = Brass,
            ContentMarginLeft = Size(10, scale),
            ContentMarginRight = Size(10, scale),
            ContentMarginTop = Size(8, scale),
            ContentMarginBottom = Size(8, scale),
        };
        panel.SetBorderWidthAll(Size(1, scale));
        panel.SetCornerRadiusAll(Size(6, scale));

        var hover = new StyleBoxFlat { BgColor = Highlight };
        hover.SetCornerRadiusAll(Size(4, scale));

        theme.SetStylebox("panel", "PopupMenu", panel);
        theme.SetStylebox("hover", "PopupMenu", hover);
        theme.SetColor("font_color", "PopupMenu", Ink);
        theme.SetColor("font_hover_color", "PopupMenu", Colors.White);
        theme.SetColor("font_disabled_color", "PopupMenu", Muted);
        theme.SetColor("font_separator_color", "PopupMenu", Muted);
        theme.SetFont("font", "PopupMenu", GD.Load<Font>(BodyFont));
        theme.SetFont("font_separator", "PopupMenu", GD.Load<Font>(BoldFont));
        theme.SetFontSize("font_size", "PopupMenu", font);
        // Rows want air. The default sits the text almost on the separators, and
        // a companion's menu is read at a glance rather than scanned.
        theme.SetConstant("v_separation", "PopupMenu", Size(7, scale));
        theme.SetConstant("h_separation", "PopupMenu", Size(8, scale));
        theme.SetConstant("item_start_padding", "PopupMenu", Size(6, scale));
        theme.SetConstant("item_end_padding", "PopupMenu", Size(6, scale));

        var separator = new StyleBoxFlat { BgColor = Brass with { A = 0.35f } };
        separator.SetContentMarginAll(0);
        theme.SetStylebox("separator", "PopupMenu", separator);
        theme.SetConstant("separation", "HSeparator", Size(1, scale));

        theme.SetStylebox("panel", "PanelContainer", panel);

        _cache[key] = theme;
        return theme;
    }

    /// Rounds a design-unit size into physical pixels, never below one.
    private static int Size(float units, float scale) =>
        System.Math.Max(1, (int)System.Math.Round(units * scale));

    private static readonly System.Collections.Generic.Dictionary<int, Theme> _cache = new();
}
