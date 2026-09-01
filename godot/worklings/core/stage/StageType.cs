using Godot;

namespace Worklings.Core.Stage;

/// Typography for everything the dungeon draws over the 3D view.
///
/// Chakra Petch: a squared humanist face with clipped corners and slightly
/// mechanical joints — it reads as instrumentation rather than as a word
/// processor, which suits creatures that are half machine and a HUD that is
/// meant to look like a readout. Godot's default face is a generic UI sans and
/// made the whole overlay look like a debug build, which it was.
///
/// Bundled under the SIL Open Font License (assets/fonts/OFL.txt) rather than
/// fetched at runtime — a game cannot depend on a font server being reachable.
public static class StageType
{
    private const string BoldPath = "res://assets/fonts/ChakraPetch-Bold.ttf";
    private const string SemiPath = "res://assets/fonts/ChakraPetch-SemiBold.ttf";

    private static FontFile? _bold;
    private static FontFile? _semi;

    public static FontFile Bold => _bold ??= GD.Load<FontFile>(BoldPath);
    public static FontFile Semi => _semi ??= GD.Load<FontFile>(SemiPath);

    /// Ink and its supporting tones. Warm-biased rather than neutral grey — the
    /// Cache Warren is lit by torchlight, and a cold HUD sits on top of it
    /// rather than in it.
    public static readonly Color Ink = new("F2E9D9");
    public static readonly Color Muted = new("9A8B74");
    public static readonly Color Faint = new("6A5F4F");
    public static readonly Color Shadow = new(0, 0, 0, 0.88f);

    /// Every label over the 3D view needs an outline. Without one, light text
    /// crossing the pale cave floor loses its edges exactly where the fight is
    /// happening.
    public static Label Label(string text, int size, Color colour, bool bold = false, int outline = 0)
    {
        var label = new Label { Text = text };
        label.AddThemeFontOverride("font", bold ? Bold : Semi);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", colour);
        label.AddThemeConstantOverride("outline_size", outline > 0 ? outline : System.Math.Max(4, size / 4));
        label.AddThemeColorOverride("font_outline_color", Shadow);
        return label;
    }
}
