using Godot;
using System;
using System.Collections.Generic;
using Worklings.Core.Pet;
using Worklings.Core.Progression;

namespace Worklings.Core.Host;

/// The Inventory tab: a bag, not a list.
///
/// **It became a browser when the gear rail landed and kept wearing the old
/// list's clothes** — a row per item, a name and a price and an Equip button,
/// down the page. That was the only door to gear once; now the character
/// screen's rail and the loadout's plates both equip, so this screen's job is
/// to let you *look at what you have*, which a column of text is bad at.
///
/// Built the way the genre has settled on, because a player already knows how to
/// read it: uniform cells carrying only a mark, rarity on the cell's edge, and
/// every number and word in the tooltip that appears when you point at one. The
/// fifteen items fit in a glance instead of a scroll, and the shelf has room for
/// three times as many before it needs one.
///
/// **Categories are the top-level axis**, not slots. Gear is one kind of thing a
/// Workling carries; consumables and quest items are named in the design and not
/// built. Making the category rail now means the first of them lands in a place
/// that already exists rather than forcing this screen to be re-laid-out.
///
/// Nothing here builds a loadout itself: equipping routes through `PetState`,
/// which validates ownership and slot, and prices come from `ItemRates` — the
/// same numbers the gear rail and the loadout quote, from the same place.
public sealed partial class InventoryTab : HBoxContainer
{
    /// The categories, in the order they are offered. Only Gear has anything in
    /// it; the other two exist so that the day one of them does, this screen
    /// does not need rebuilding around it.
    public const string Gear = "Gear";
    public const string Consumables = "Consumables";
    public const string Quest = "Quest";

    private static readonly string[] Categories = { Gear, Consumables, Quest };

    /// What each empty category is *for*, so an empty shelf explains itself
    /// rather than looking broken.
    private static string EmptyCopy(string category) => category switch
    {
        Consumables =>
            "Field rations, repair kits — anything spent rather than worn. "
          + "Named as a future slot in the items design; there is no system "
          + "behind it yet.",
        _ =>
            "Keys, fragments, the thing a dungeon asks you to carry back out. "
          + "Nothing in the game hands one over today.",
    };

    private readonly float _scale;
    private readonly PetState _state;

    public event Action<PetState>? StateChanged;
    /// Raised as they happen rather than read back at teardown: the panel
    /// rebuilds wholesale on every equip, so the selection has to be recorded
    /// somewhere that outlives this node.
    public event Action<Item?>? SelectionChanged;
    public event Action<string>? CategoryChanged;

    private readonly string _category;
    private readonly Item? _selected;

    public InventoryTab(PetState state, float scale, string category, Item? selected)
    {
        _state = state;
        _scale = scale;
        _category = Array.IndexOf(Categories, category) >= 0 ? category : Gear;
        _selected = selected;

        AddThemeConstantOverride("separation", 0);
        SizeFlagsVertical = SizeFlags.ExpandFill;

        AddChild(Shelf());
        AddChild(new ColorRect
        {
            Color = WorklingsTheme.Brass with { A = 0.28f },
            CustomMinimumSize = new Vector2(S(1), 0),
        });
        AddChild(Inspector());
    }

    private int S(float units) => Math.Max(1, (int)Math.Round(units * _scale));

    // MARK: - The shelf

    private Control Shelf()
    {
        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        column.AddThemeConstantOverride("separation", S(9));

        var margin = new MarginContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        foreach (string side in new[] { "left", "right", "top", "bottom" })
        {
            margin.AddThemeConstantOverride($"margin_{side}", S(12));
        }
        margin.AddChild(column);

        column.AddChild(CategoryRail());
        column.AddChild(new ColorRect
        {
            Color = WorklingsTheme.Brass with { A = 0.28f },
            CustomMinimumSize = new Vector2(0, S(1)),
        });

        // The bag scrolls and the inspector does not. Letting the whole tab
        // scroll is what pushed Equip off the bottom of the old screen: the
        // action belongs on screen whatever the bag is doing.
        var scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        scroll.AddChild(_category == Gear ? Bag() : EmptyShelf(_category));
        column.AddChild(scroll);

        return margin;
    }

    private Control CategoryRail()
    {
        var rail = new HBoxContainer();
        rail.AddThemeConstantOverride("separation", S(4));

        foreach (string category in Categories)
        {
            int count = category == Gear ? _state.OwnedItems.Count : 0;
            bool active = category == _category;
            var button = new Button
            {
                Text = $"{category}  {count}",
                // Flat suppresses the stylebox entirely, so the active chip
                // cannot be both flat and filled.
                Flat = !active,
                FocusMode = FocusModeEnum.All,
            };
            button.AddThemeFontSizeOverride("font_size", S(13));

            button.AddThemeColorOverride(
                "font_color", active ? WorklingsTheme.Panel : WorklingsTheme.Muted);
            button.AddThemeColorOverride(
                "font_hover_color", active ? WorklingsTheme.Panel : WorklingsTheme.Ink);
            if (active)
            {
                var fill = new StyleBoxFlat { BgColor = WorklingsTheme.Relic };
                fill.SetCornerRadiusAll(S(2));
                fill.ContentMarginLeft = fill.ContentMarginRight = S(10);
                fill.ContentMarginTop = fill.ContentMarginBottom = S(4);
                foreach (string state in new[] { "normal", "hover", "pressed", "focus" })
                {
                    button.AddThemeStyleboxOverride(state, fill);
                }
            }

            string chosen = category;
            button.Pressed += () => CategoryChanged?.Invoke(chosen);
            rail.AddChild(button);
        }

        return rail;
    }

    /// The bag proper: one group per slot, best tier first inside each.
    private Control Bag()
    {
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", S(12));

        foreach (var slot in ItemSlotExtensions.AllCases)
        {
            var group = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            group.AddThemeConstantOverride("separation", S(5));

            group.AddChild(GroupHeader(slot));
            group.AddChild(new ColorRect
            {
                Color = WorklingsTheme.Brass with { A = 0.28f },
                CustomMinimumSize = new Vector2(0, S(1)),
            });

            var grid = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            grid.AddThemeConstantOverride("h_separation", S(4));
            grid.AddThemeConstantOverride("v_separation", S(4));

            // Best tier first, which AvailableItems already does — with three
            // tiers of everything, acquisition order buries a hard-won Prime
            // item under the junk that dropped before it.
            foreach (var item in _state.AvailableItems(slot))
            {
                grid.AddChild(Cell(item));
            }

            if (_state.AvailableItems(slot).Count == 0)
            {
                grid.AddChild(Caption("nothing for this slot yet", WorklingsTheme.Brass));
            }

            group.AddChild(grid);
            column.AddChild(group);
        }

        return column;
    }

    /// Slot name, and what is worn in it. The one piece of state worth having
    /// without pointing at anything — everything else is in the tooltip.
    private Control GroupHeader(ItemSlot slot)
    {
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", S(8));

        var name = Caption(slot.DisplayName().ToUpperInvariant(), WorklingsTheme.Muted);
        name.AddThemeFontOverride("font", WorklingsTheme.Bold);
        line.AddChild(name);

        var equipped = _state.Loadout[slot];
        line.AddChild(equipped is Item item
            ? Caption(item.DisplayName(), WorklingsTheme.Ink)
            : Caption("empty", WorklingsTheme.Brass));

        return line;
    }

    private Control Cell(Item item)
    {
        var cell = new ItemCell(item, _state, _scale, selected: _selected == item);
        cell.Pressed += () => SelectionChanged?.Invoke(item);
        cell.Activated += () => Toggle(item);
        return cell;
    }

    private Control EmptyShelf(string category)
    {
        var column = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            Alignment = BoxContainer.AlignmentMode.Center,
        };
        column.AddThemeConstantOverride("separation", S(6));

        var head = Caption($"{category} — nothing yet", WorklingsTheme.Muted);
        head.AddThemeFontOverride("font", WorklingsTheme.Bold);
        head.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(head);

        var body = Caption(EmptyCopy(category), WorklingsTheme.Brass);
        body.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        body.HorizontalAlignment = HorizontalAlignment.Center;
        body.CustomMinimumSize = new Vector2(S(280), 0);
        column.AddChild(body);

        return column;
    }

    // MARK: - The inspector

    /// Fixed width, and it does not scroll. What is selected, what it is worth
    /// on *this* Workling, and the one button that changes anything.
    private Control Inspector()
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(S(210), 0) };
        var background = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.22f) };
        background.ContentMarginLeft = background.ContentMarginRight = S(14);
        background.ContentMarginTop = background.ContentMarginBottom = S(14);
        panel.AddThemeStyleboxOverride("panel", background);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", S(8));
        panel.AddChild(column);

        if (_category != Gear || _selected is not Item item)
        {
            var empty = Caption(
                _category == Gear
                    ? "Pick something out of the bag to read it here."
                    : "Nothing to inspect.",
                WorklingsTheme.Brass);
            empty.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            column.Alignment = BoxContainer.AlignmentMode.Center;
            column.AddChild(empty);
            return panel;
        }

        var tier = item.Tier();
        var colour = ItemIcon.TierColour(tier);
        bool worn = _state.Loadout[item.Slot()] == item;
        int bonus = ItemRates.Default.Modifier(item, _state.Family);
        bool attuned = ItemRates.Default.IsAttuned(item, _state.Family);

        var centre = new CenterContainer();
        centre.AddChild(new ItemIcon(item, S(60)));
        column.AddChild(centre);

        var name = new Label { Text = item.DisplayName() };
        name.AddThemeFontOverride("font", WorklingsTheme.Bold);
        name.AddThemeFontSizeOverride("font_size", S(17));
        name.AddThemeColorOverride("font_color", WorklingsTheme.Ink);
        name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(name);

        column.AddChild(Caption(
            $"{tier.DisplayName().ToUpperInvariant()} · {item.Slot().DisplayName().ToUpperInvariant()}",
            colour));

        var flavor = Caption(item.Flavor(), WorklingsTheme.Muted);
        flavor.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(flavor);

        column.AddChild(new ColorRect
        {
            Color = WorklingsTheme.Brass with { A = 0.28f },
            CustomMinimumSize = new Vector2(0, S(1)),
        });

        Figure(column, "Tier base",
            $"+{ItemRates.Default.BaseModifier(tier)} {item.Stat().DisplayName()}",
            WorklingsTheme.Ink);
        Figure(column, "Attunement",
            attuned ? $"+{ItemRates.Default.AttunementBonus} ✦" : "—",
            attuned ? WorklingsTheme.Relic : WorklingsTheme.Muted);
        Figure(column, $"On {_state.Name}",
            $"+{bonus} {item.Stat().DisplayName()}",
            WorklingsTheme.GearBlue);

        // Pushes the action to the bottom of the panel, which does not scroll —
        // so it is always where the player left it.
        column.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });

        var button = new Button { Text = worn ? "Take off" : "Equip" };
        button.AddThemeFontSizeOverride("font_size", S(14));
        if (!worn)
        {
            var fill = new StyleBoxFlat { BgColor = WorklingsTheme.Relic };
            fill.SetCornerRadiusAll(S(2));
            fill.ContentMarginTop = fill.ContentMarginBottom = S(6);
            button.AddThemeStyleboxOverride("normal", fill);
            button.AddThemeStyleboxOverride("hover", fill);
            button.AddThemeColorOverride("font_color", WorklingsTheme.Panel);
            button.AddThemeColorOverride("font_hover_color", WorklingsTheme.Panel);
        }
        button.Pressed += () => Toggle(item);
        column.AddChild(button);

        return panel;
    }

    private void Figure(VBoxContainer column, string label, string value, Color colour)
    {
        var line = new HBoxContainer();
        var name = Caption(label, WorklingsTheme.Muted);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        line.AddChild(name);
        line.AddChild(Caption(value, colour));
        column.AddChild(line);
    }

    // MARK: - Acting

    /// Mirrors what every other gear surface does: one item per slot, swapping
    /// is free, and `PetState` is the thing that says yes.
    private void Toggle(Item item)
    {
        bool worn = _state.Loadout[item.Slot()] == item;
        SelectionChanged?.Invoke(item);
        StateChanged?.Invoke(worn
            ? _state.ClearingSlot(item.Slot())
            : _state.Equipping(item));
    }

    private Label Caption(string text, Color colour)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", S(12));
        label.AddThemeColorOverride("font_color", colour);
        return label;
    }
}

/// One square in the bag.
///
/// Carries the mark and nothing else — rarity is the cell's own border, worn is
/// a pip in the corner, and every word about the item is in the tooltip. That is
/// what lets fifteen of these sit in a glance where fifteen rows needed a
/// scroll.
///
/// Click selects, double-click equips: the arrangement every bag in the genre
/// uses, and cheap here because `Pressed` already fires on the first click of
/// the pair, so a double-click selects *and* acts without a special case.
public sealed partial class ItemCell : Button
{
    private const int Side = 54;
    private const int Mark = 30;

    private int Width => S(190);

    private readonly Item _item;
    private readonly PetState _state;
    private readonly float _scale;

    /// What this cell holds, so a capture tool can pick a meaningful one rather
    /// than whichever happens to be first in the tree.
    public Item Item => _item;

    /// Raised on double-click. `Pressed` still means "selected".
    public event Action? Activated;

    public ItemCell(Item item, PetState state, float scale, bool selected)
    {
        _item = item;
        _state = state;
        _scale = scale;

        CustomMinimumSize = new Vector2(S(Side), S(Side));
        FocusMode = FocusModeEnum.All;
        TooltipText = item.DisplayName();

        var colour = ItemIcon.TierColour(item.Tier());
        bool worn = state.Loadout[item.Slot()] == item;

        // Rarity on the edge, which is where every other game puts it. Selection
        // is an ink ring rather than amber: amber is the top tier now, and an
        // amber ring around a gold border is invisible.
        AddThemeStyleboxOverride("normal", Face(colour, selected, lit: false));
        AddThemeStyleboxOverride("hover", Face(colour, selected, lit: true));
        AddThemeStyleboxOverride("pressed", Face(colour, selected, lit: true));
        AddThemeStyleboxOverride("focus", Face(colour, selected: true, lit: false));

        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        centre.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        centre.AddChild(new ItemIcon(item, S(Mark)));
        AddChild(centre);

        if (worn)
        {
            var pip = new ColorRect
            {
                Color = WorklingsTheme.Ink,
                CustomMinimumSize = new Vector2(S(5), S(5)),
                MouseFilter = MouseFilterEnum.Ignore,
            };
            pip.Position = new Vector2(S(Side) - S(10), S(5));
            pip.Size = new Vector2(S(5), S(5));
            AddChild(pip);
        }

        if (ItemRates.Default.IsAttuned(item, state.Family))
        {
            var mark = new Label
            {
                Text = "✦",
                MouseFilter = MouseFilterEnum.Ignore,
            };
            mark.AddThemeFontSizeOverride("font_size", S(9));
            mark.AddThemeColorOverride("font_color", WorklingsTheme.Relic);
            // Sized explicitly: a Label outside a container keeps its zero
            // size and draws nothing at all.
            mark.Size = new Vector2(S(12), S(12));
            mark.VerticalAlignment = VerticalAlignment.Center;
            mark.Position = new Vector2(S(5), S(Side) - S(16));
            AddChild(mark);
        }
    }

    private int S(float units) => Math.Max(1, (int)Math.Round(units * _scale));

    private StyleBoxFlat Face(Color tier, bool selected, bool lit)
    {
        var box = new StyleBoxFlat
        {
            BgColor = lit ? WorklingsTheme.Highlight : new Color(1, 1, 1, 0.03f),
            BorderColor = selected ? WorklingsTheme.Ink : tier with { A = 0.6f },
        };
        box.SetBorderWidthAll(S(selected ? 2 : 1));
        box.SetCornerRadiusAll(S(2));
        return box;
    }

    public override void _GuiInput(InputEvent @event)
    {
        // Let the base class run first, so the first click of the pair still
        // selects before the second one equips.
        base._GuiInput(@event);
        if (@event is InputEventMouseButton { DoubleClick: true, ButtonIndex: MouseButton.Left })
        {
            Activated?.Invoke();
            AcceptEvent();
        }
    }

    /// The tooltip is the reading surface: name in its tier colour, what it is,
    /// what it is worth *here*, the flavour line, and how to act on it. All of it
    /// was on the row before, which is why the row was the size of a paragraph.
    public override Control _MakeCustomTooltip(string forText)
    {
        var panel = new PanelContainer { Theme = WorklingsTheme.For(_scale) };
        var background = new StyleBoxFlat
        {
            BgColor = WorklingsTheme.Panel,
            BorderColor = WorklingsTheme.Brass,
        };
        background.SetBorderWidthAll(S(1));
        background.SetCornerRadiusAll(S(2));
        background.ContentMarginLeft = background.ContentMarginRight = S(10);
        background.ContentMarginTop = background.ContentMarginBottom = S(9);
        panel.AddThemeStyleboxOverride("panel", background);

        var column = new VBoxContainer { CustomMinimumSize = new Vector2(Width, 0) };
        column.AddThemeConstantOverride("separation", S(4));
        panel.AddChild(column);

        var tier = _item.Tier();
        bool worn = _state.Loadout[_item.Slot()] == _item;
        int bonus = ItemRates.Default.Modifier(_item, _state.Family);
        bool attuned = ItemRates.Default.IsAttuned(_item, _state.Family);

        var name = new Label { Text = _item.DisplayName() };
        name.AddThemeFontOverride("font", WorklingsTheme.Bold);
        name.AddThemeFontSizeOverride("font_size", S(15));
        name.AddThemeColorOverride("font_color", ItemIcon.TierColour(tier));
        column.AddChild(name);

        column.AddChild(Small(
            $"{tier.DisplayName().ToUpperInvariant()} · "
          + $"{_item.Slot().DisplayName().ToUpperInvariant()} · "
          + (worn ? "WORN" : "CARRIED"),
            WorklingsTheme.Muted));

        column.AddChild(Small(
            $"+{bonus} {_item.Stat().DisplayName()}{(attuned ? $"   ✦ {_state.Family}" : "")}",
            WorklingsTheme.GearBlue));

        column.AddChild(new ColorRect
        {
            Color = WorklingsTheme.Brass with { A = 0.28f },
            CustomMinimumSize = new Vector2(0, S(1)),
        });

        var flavor = Small(_item.Flavor(), WorklingsTheme.Muted);
        flavor.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        // The width has to be stated, not inherited. A wrapping Label with no
        // minimum width reports the height it would need wrapped at one
        // character per line, and the tooltip sizes itself to that — which is
        // how a five-line panel came out the height of the window.
        flavor.CustomMinimumSize = new Vector2(Width, 0);
        column.AddChild(flavor);

        column.AddChild(Small(
            worn ? "Double-click to take off" : "Double-click to equip",
            WorklingsTheme.Brass));

        return panel;
    }

    private Label Small(string text, Color colour)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", S(12));
        label.AddThemeColorOverride("font_color", colour);
        return label;
    }
}
