using Godot;
using Worklings.Core.Pet;
using Worklings.Core.Progression;

namespace Worklings.Core.Host;

/// A drawn mark for a piece of gear — the item's **stat** shape, in its tier's
/// colour — or the slot's own shape when the slot is empty.
///
/// **One mark per stat, not per item, and that is enough for identity.** The
/// fifteen items are exactly five primary stats by three tiers: one item per
/// cell, no gaps and no collisions. Tier is already carried as colour, so five
/// shapes and three colours name all fifteen uniquely. The earlier version drew
/// one mark per *slot*, which meant three shapes for fifteen items — and worse,
/// two items could come out pixel-identical (a Scavenged Bent Pot Lid and a
/// Scavenged Cold Coffee Dregs are both Wards at the same tier).
///
/// Still a placeholder, and still honest about being one: real per-item art is
/// wanted eventually. But it is a placeholder that *distinguishes*, which is
/// what a shelf of icons needs and a slot-keyed mark could not give.
///
/// Drawn rather than loaded so it is crisp at any display scale and costs no
/// asset. Polygons only — `DrawColoredPolygon` triangulates concave shapes but
/// cannot cut a hole, so a mark that needs an interior detail paints it as a
/// second polygon in the panel colour, on top of the mark's own face.
public sealed partial class ItemIcon : Control
{
    private readonly Item? _item;
    private readonly ItemSlot _slot;
    private readonly Color _tint;
    private readonly bool _filled;

    /// Tier colour, and the one place it is decided.
    ///
    /// **Grey at the bottom, gold at the top**, which is the ladder every game in
    /// the genre uses and the one a player already knows how to read. Scavenged
    /// stays colourless so the two earned tiers read as earned; Solid is the
    /// uncommon-green rung; Prime is gold.
    ///
    /// Prime was blue until 2026-09-17, borrowing the creature aura's colour on
    /// the theory that blue is this game's "energy from somewhere else". That
    /// reading is real but it loses to legibility — nobody decodes an aura
    /// reference, everybody reads gold as best. Blue is also spoken for:
    /// `WorklingsTheme.GearBlue` is the only thing separating base stats from
    /// gear-given stats, so spending it on a rarity would make one colour mean
    /// two things on the same tooltip. Green keeps blue *and* pink free for a
    /// fourth tier, should one ever be authored.
    public static Color TierColour(ItemTier tier) => tier switch
    {
        ItemTier.Scavenged => new Color(0.561f, 0.529f, 0.478f),
        ItemTier.Solid => new Color(0.435f, 0.686f, 0.349f),
        ItemTier.Prime => new Color(0.941f, 0.706f, 0.161f),
        _ => WorklingsTheme.Muted,
    };

    /// A mark for `item`, drawn in its tier's colour.
    public ItemIcon(Item item, int size)
        : this(item, item.Slot(), TierColour(item.Tier()), size, filled: true)
    {
    }

    /// The general form. `item` is null for an empty slot, which draws the
    /// slot's own shape instead — an empty Tool plate should say *Tool*, not
    /// name a stat nothing is providing.
    public ItemIcon(Item? item, ItemSlot slot, Color tint, int size, bool filled = true)
    {
        _item = item;
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

        // Authored over a 0..100 square and scaled here, so the coordinates
        // below can be read and edited as if they were a 100x100 drawing.
        Vector2[] Shape(params float[] xy)
        {
            var points = new Vector2[xy.Length / 2];
            for (int i = 0; i < points.Length; i++)
            {
                points[i] = P(xy[i * 2] / 100f, xy[i * 2 + 1] / 100f);
            }
            return points;
        }

        if (_item is Item item)
        {
            DrawStatMark(item.Stat(), Shape);
            return;
        }

        DrawSlotMark(Shape);
    }

    /// The five stat marks. One per primary, so an item's shape names what it
    /// does for you rather than which pocket it lives in.
    private void DrawStatMark(PetStatKind stat, System.Func<float[], Vector2[]> shape)
    {
        switch (stat)
        {
            // A bolt. Legible at cell size, where the more literal choice — a
            // flexed arm — needs silhouette detail a 30-pixel square cannot hold.
            case PetStatKind.Power:
                Fill(shape(new[] { 60f, 6f, 24f, 54f, 44f, 54f, 38f, 94f, 76f, 40f, 54f, 40f }));
                break;

            // A shield, the one shape nobody needs explained.
            case PetStatKind.Defense:
                Fill(shape(new[]
                {
                    50f, 12f, 86f, 26f, 86f, 52f, 50f, 88f, 14f, 52f, 14f, 26f,
                }));
                break;

            // A cross: the one symbol that reads as "keeps going" at any size.
            case PetStatKind.Vitality:
                Fill(shape(new[]
                {
                    42f, 12f, 58f, 12f, 58f, 42f, 88f, 42f, 88f, 58f, 58f, 58f,
                    58f, 88f, 42f, 88f, 42f, 58f, 12f, 58f, 12f, 42f, 42f, 42f,
                }));
                break;

            // A lens on a stem — it shows you the actual problem, not the loud
            // one. An owl was drawn and rejected: the tufts and beak carried it,
            // but at cell size it read as a blob. Wit gets real art eventually.
            case PetStatKind.Wit:
                Fill(shape(new[] { 50f, 10f, 76f, 30f, 76f, 58f, 50f, 78f, 24f, 58f, 24f, 30f }));
                Fill(shape(new[] { 44f, 80f, 56f, 80f, 56f, 94f, 44f, 94f }));
                break;

            // A double chevron: already half a step ahead.
            default:
                Fill(shape(new[] { 20f, 20f, 52f, 50f, 20f, 80f, 34f, 80f, 66f, 50f, 34f, 20f }));
                Fill(shape(new[] { 50f, 20f, 82f, 50f, 50f, 80f, 64f, 80f, 96f, 50f, 64f, 20f }));
                break;
        }
    }

    /// The slot's own shape, for an empty slot. Tool and Ward borrow the mark of
    /// the stat they exist for — Power is the only Tool stat, and a Ward is a
    /// shield whichever of its two stats fills it. Charm gets a star, because it
    /// holds two unlike stats and neither should speak for the empty slot.
    private void DrawSlotMark(System.Func<float[], Vector2[]> shape)
    {
        switch (_slot)
        {
            case ItemSlot.Tool:
                DrawStatMark(PetStatKind.Power, shape);
                break;

            case ItemSlot.Ward:
                DrawStatMark(PetStatKind.Defense, shape);
                break;

            default:
                var star = new float[16];
                for (int i = 0; i < 8; i++)
                {
                    float angle = Mathf.Pi * i / 4f - Mathf.Pi / 2f;
                    float radius = i % 2 == 0 ? 42f : 13f;
                    star[i * 2] = 50f + Mathf.Cos(angle) * radius;
                    star[i * 2 + 1] = 50f + Mathf.Sin(angle) * radius;
                }
                Fill(shape(star));
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
