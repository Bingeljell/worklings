using Godot;
using Worklings.Core.Pet;
using Worklings.Core.Progression;

namespace Worklings.Core.Host;

/// One gear slot, drawn: the mark, the tier, the slot, what is in it, and what
/// it is worth.
///
/// **The one place gear is drawn.** The character screen's rail and the
/// loadout's rig are the same three slots asked the same question in two rooms,
/// and they were about to be built twice — a tile in `CharacterPanel` and a grid
/// row in `LoadoutPanel`, drifting apart the first time either changed. The test
/// the split has to pass is simple: a change to how gear reads should be made
/// here, once, and show up on both screens.
///
/// What differs between the two is arrangement and size, not vocabulary. The
/// rail stacks — three narrow tiles side by side under a portrait — and the rig
/// lays out in a row, because its plates hang beside a body and read left to
/// right off it. Both carry the same five things in the same colours.
///
/// A `Button` with its children anchored inside it and deaf to the mouse, so the
/// whole plate is one hit target and is focusable and keyboard-operable for
/// free. A child that ate clicks would leave the plate dead in the middle, which
/// is exactly where it is aimed at.
public sealed partial class GearPlate : Button
{
    /// Stacked for the rail, Row for the rig.
    public enum Arrangement
    {
        /// Tier, mark, slot, name — a narrow column. Three of these fit under a
        /// portrait.
        Stacked,

        /// Mark on the left, everything else beside it. Wider, and it carries
        /// the bonus line the stacked one has no room for.
        Row,
    }

    /// Everything about how big a plate is drawn, so the widget itself has no
    /// numbers of its own to disagree about.
    public readonly record struct Look(
        float Scale,
        Arrangement Shape,
        Vector2 MinSize,
        int IconSize,
        int TierSize,
        int SlotSize,
        int NameSize,
        int BonusSize,
        /// Whether the "+5 Power ◈" line is drawn. The rail has no room for it
        /// and the ledger beside it already says the same thing.
        bool ShowBonus)
    {
        /// The character screen's gear rail: three narrow tiles under the bay.
        public static Look Rail(float scale) => new(
            Scale: scale, Shape: Arrangement.Stacked,
            MinSize: new Vector2(56, 74),
            IconSize: 22, TierSize: 9, SlotSize: 9, NameSize: 10, BonusSize: 0,
            ShowBonus: false);

        /// The loadout's rig: plates hung beside the body, large and close.
        ///
        /// Large on purpose. Three slots is a thin rig, and plates scattered at
        /// a distance read as a body with things stuck to it rather than a
        /// creature wearing them.
        public static Look Rig(float scale) => new(
            Scale: scale, Shape: Arrangement.Row,
            MinSize: new Vector2(190, 72),
            IconSize: 22, TierSize: 10, SlotSize: 11, NameSize: 16, BonusSize: 14,
            ShowBonus: true);
    }

    private readonly ItemSlot _slot;
    private readonly Look _look;
    private readonly Label _tier;
    private readonly Label _name;
    private readonly Label? _bonus;
    private readonly Control _mark;
    /// The icon's parent, kept so a changed tier can swap the mark without the
    /// surrounding plate knowing which arrangement it is in.
    private readonly CenterContainer _iconHolder;
    private Item? _item;
    private bool _selected;

    public ItemSlot Slot => _slot;

    /// What the keyboard cursor is on. The rig is driven by arrow keys as much
    /// as by the mouse, and focus alone is not enough of a mark on a screen
    /// where the plates are already outlined in their tier colour.
    public bool Selected
    {
        get => _selected;
        set { _selected = value; Restyle(); }
    }

    public GearPlate(ItemSlot slot, Look look)
    {
        _slot = slot;
        _look = look;
        TooltipText = slot.Fantasy();
        CustomMinimumSize = look.MinSize * look.Scale;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;

        _tier = Caption(look.TierSize, WorklingsTheme.Muted);
        _tier.HorizontalAlignment = look.Shape == Arrangement.Stacked
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Left;

        _iconHolder = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        _mark = Mark(_iconHolder);

        var slotLabel = Caption(look.SlotSize, WorklingsTheme.Muted);
        slotLabel.Text = slot.DisplayName().ToUpperInvariant();

        _name = Caption(look.NameSize, WorklingsTheme.Ink);
        // Three plates cannot show "Everburning Backup-Coal" whole, and a name
        // that wraps would push one plate taller than its neighbours.
        _name.ClipText = true;
        _name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;

        _bonus = look.ShowBonus ? Caption(look.BonusSize, WorklingsTheme.GearBlue) : null;
        if (_bonus is not null)
        {
            _bonus.ClipText = true;
            _bonus.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        }

        AddChild(look.Shape == Arrangement.Stacked
            ? Stack(slotLabel)
            : Row(slotLabel));

        Set(null, 0, false);
    }

    /// What is in the slot now.
    ///
    /// Updated in place rather than rebuilt. The loadout redraws on every
    /// keypress and the live 3D bay it is arranged around must not be thrown
    /// away and rebuilt underneath it.
    public void Set(Item? item, int bonus, bool attuned)
    {
        _item = item;
        bool filled = item is not null;
        var colour = Tint;

        _tier.Text = filled ? item!.Value.Tier().DisplayName().ToUpperInvariant() : "";
        _tier.AddThemeColorOverride("font_color", colour);

        _name.Text = filled ? item!.Value.DisplayName() : "empty";
        _name.AddThemeColorOverride(
            "font_color", filled ? WorklingsTheme.Ink : WorklingsTheme.Muted with { A = 0.7f });

        if (_bonus is not null)
        {
            // The attunement rider is a real number the player is already being
            // paid; marking it is what makes it discoverable.
            // Nothing, when the slot is empty. The slot's fantasy line lives in
            // the tooltip: it is a sentence, and a sentence on a plate sized for
            // "+5 Power" runs out of the plate and across whatever is beside it.
            _bonus.Text = filled
                ? $"+{bonus} {item!.Value.Stat().DisplayName()}{(attuned ? "  ◈" : "")}"
                : "";
            _bonus.AddThemeColorOverride(
                "font_color", filled ? WorklingsTheme.GearBlue : WorklingsTheme.Muted with { A = 0.6f });
        }

        // The icon draws its tint at construction, so a changed tier means a new
        // one. It is a few lines of vector drawing, not an asset.
        foreach (var old in _iconHolder.GetChildren())
        {
            _iconHolder.RemoveChild(old);
            old.QueueFree();
        }
        _iconHolder.AddChild(new ItemIcon(_item, _slot, colour, S(_look.IconSize), filled));

        Restyle();
    }

    /// Empty borrows brass at half strength: an empty slot is a legal loadout
    /// and should look chosen, not broken.
    private Color Tint => _item is Item item
        ? ItemIcon.TierColour(item.Tier())
        : WorklingsTheme.Brass with { A = 0.75f };

    private Control Stack(Label slotLabel)
    {
        var stack = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        stack.AddThemeConstantOverride("separation", S(1));
        stack.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.KeepSize, S(5));
        stack.AddChild(_tier);

        var centre = new CenterContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        centre.AddChild(_mark);
        stack.AddChild(centre);

        slotLabel.HorizontalAlignment = HorizontalAlignment.Center;
        stack.AddChild(slotLabel);

        _name.HorizontalAlignment = HorizontalAlignment.Center;
        stack.AddChild(_name);
        return stack;
    }

    private Control Row(Label slotLabel)
    {
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", S(11));
        row.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.KeepSize, S(7));

        var boxed = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        boxed.AddChild(_mark);
        row.AddChild(boxed);

        var meta = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        meta.AddThemeConstantOverride("separation", 0);

        // The slot and the tier share a line: two short words that are both
        // labels for the same thing, and stacking them would push the item's
        // name — the thing you are actually reading — off centre.
        var heading = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        heading.AddThemeConstantOverride("separation", S(8));
        slotLabel.HorizontalAlignment = HorizontalAlignment.Left;
        heading.AddChild(slotLabel);
        heading.AddChild(_tier);
        meta.AddChild(heading);

        meta.AddChild(_name);
        if (_bonus is not null) meta.AddChild(_bonus);
        row.AddChild(meta);
        return row;
    }

    /// The mark in its own recess, so a filled slot reads as something sitting
    /// in a fitting rather than an icon floating on a panel.
    private Control Mark(CenterContainer holder)
    {
        if (_look.Shape == Arrangement.Stacked) return holder;

        int side = S(_look.IconSize + 16);
        var box = new PanelContainer
        {
            CustomMinimumSize = new Vector2(side, side),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.035f, 0.03f, 1),
            BorderColor = WorklingsTheme.Brass with { A = 0.5f },
        };
        style.SetBorderWidthAll(S(1));
        box.AddThemeStyleboxOverride("panel", style);
        box.AddChild(holder);
        return box;
    }

    private void Restyle()
    {
        var colour = _selected ? WorklingsTheme.Relic : Tint;
        foreach (string state in new[] { "normal", "hover", "pressed", "focus" })
        {
            bool lit = state != "normal" || _selected;
            AddThemeStyleboxOverride(state, Style(colour, _item is not null, lit));
        }
    }

    private StyleBoxFlat Style(Color colour, bool filled, bool lit)
    {
        var box = new StyleBoxFlat
        {
            BgColor = lit ? WorklingsTheme.Highlight : new Color(0.065f, 0.055f, 0.045f, 1),
            BorderColor = filled || _selected
                ? colour with { A = lit ? 1f : 0.75f }
                : colour with { A = 0.5f },
        };
        box.SetBorderWidthAll(S(1));
        box.SetCornerRadiusAll(S(3));
        box.ContentMarginLeft = box.ContentMarginRight = S(4);
        box.ContentMarginTop = box.ContentMarginBottom = S(4);
        return box;
    }

    private Label Caption(int size, Color colour)
    {
        var label = new Label { MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontOverride("font", WorklingsTheme.Body);
        label.AddThemeFontSizeOverride("font_size", S(size));
        label.AddThemeColorOverride("font_color", colour);
        return label;
    }

    private int S(float units) =>
        System.Math.Max(1, (int)System.Math.Round(units * _look.Scale));
}
