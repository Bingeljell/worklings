using Godot;
using Worklings.Core.Pet;

namespace Worklings.Core.Host;

/// A drawn mark for a gear slot, tinted by the tier of what is in it.
///
/// **Placeholder, and honest about being one.** Fifteen items want fifteen
/// pieces of art; until that exists, a slot showing its own shape in its own
/// tier colour is enough for the eye to tell a filled Tool from an empty Charm
/// across the screen, which a line of text is not. It is drawn rather than
/// drawn-from-a-file so it is crisp at any display scale and costs no asset.
///
/// One mark per *slot*, not per item — three items in a slot differ by tier
/// colour and by name. Per-item marks would be a lie about how much art exists.
public sealed partial class ItemIcon : Control
{
    private readonly ItemSlot _slot;
    private readonly Color _tint;
    private readonly bool _filled;

    /// Tier colour, and the one place it is decided.
    ///
    /// Scavenged is deliberately colourless — grey reads as "this is what
    /// dropped", so Solid's brass and Prime's aura blue read as earned. Prime
    /// borrows the creature aura's blue on purpose: it is the colour this game
    /// already uses for energy that came from somewhere else.
    public static Color TierColour(ItemTier tier) => tier switch
    {
        ItemTier.Scavenged => new Color(0.56f, 0.53f, 0.48f),
        ItemTier.Solid => new Color(0.71f, 0.58f, 0.37f),
        ItemTier.Prime => new Color(0.30f, 0.61f, 1.00f),
        _ => WorklingsTheme.Muted,
    };

    public ItemIcon(ItemSlot slot, Color tint, int size, bool filled = true)
    {
        _slot = slot;
        _tint = tint;
        _filled = filled;
        CustomMinimumSize = new Vector2(size, size);
        // The tile owns the click; an icon that ate it would leave the tile
        // dead in its middle.
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        float s = Mathf.Min(Size.X, Size.Y);
        var at = new Vector2((Size.X - s) * 0.5f, (Size.Y - s) * 0.5f);
        Vector2 P(float x, float y) => at + new Vector2(x * s, y * s);

        switch (_slot)
        {
            // A hone: a blade edge and the handle under it. The Tool is what the
            // Workling brings to the problem, so it is the only mark with a grip.
            case ItemSlot.Tool:
                Fill(new[] { P(0.16f, 0.30f), P(0.84f, 0.16f), P(0.84f, 0.40f), P(0.16f, 0.46f) });
                Fill(new[] { P(0.30f, 0.52f), P(0.70f, 0.52f), P(0.66f, 0.86f), P(0.34f, 0.86f) });
                break;

            // A shield, the one shape nobody needs explained.
            case ItemSlot.Ward:
                Fill(new[]
                {
                    P(0.50f, 0.12f), P(0.86f, 0.26f), P(0.86f, 0.52f),
                    P(0.50f, 0.88f), P(0.14f, 0.52f), P(0.14f, 0.26f),
                });
                break;

            // A four-point star with the waist pulled in, so it reads as a
            // sparkle rather than as a diamond.
            default:
                var star = new Vector2[8];
                for (int i = 0; i < 8; i++)
                {
                    float angle = Mathf.Pi * i / 4f - Mathf.Pi / 2f;
                    float radius = i % 2 == 0 ? 0.42f : 0.13f;
                    star[i] = P(0.5f + Mathf.Cos(angle) * radius, 0.5f + Mathf.Sin(angle) * radius);
                }
                Fill(star);
                break;
        }
    }

    /// Filled when something is worn, outlined when the slot is empty — the
    /// state the old screen could not show at all.
    private void Fill(Vector2[] points)
    {
        if (_filled)
        {
            DrawColoredPolygon(points, _tint);
            return;
        }
        var loop = new Vector2[points.Length + 1];
        points.CopyTo(loop, 0);
        loop[points.Length] = points[0];
        DrawPolyline(loop, _tint, Mathf.Max(1f, Size.X * 0.045f), true);
    }
}
