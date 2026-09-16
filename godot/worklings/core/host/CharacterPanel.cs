using Godot;
using System.Collections.Generic;
using Worklings.Core.Pet;
using Worklings.Core.Progression;
using Worklings.Core.Stage;

namespace Worklings.Core.Host;

/// The contents of the character window: four tabs over one `CharacterSheet`.
///
/// The sheet does all the arithmetic — base versus gear, the signature stat, the
/// condition multiplier, what the pet actually walks into a fight with — and it
/// is built from `Combatant.Pet`, the arena's own constructor. So the numbers
/// here and the numbers in the encounter cannot drift apart; there is only one
/// of them.
///
/// Rebuilt wholesale on every `Show` rather than diffed. A character screen is
/// opened, read, and closed; the cost of throwing away a few dozen labels is
/// nothing against the cost of a panel that can disagree with the save.
public partial class CharacterPanel : PanelContainer
{
    private readonly float _scale;
    private TabContainer _tabs = null!;
    private PetState _state = null!;
    /// Built once and carried across rebuilds — see `Show`.
    private ModelBay? _bay;

    public event System.Action<PetState>? StateChanged;

    public CharacterPanel(float scale)
    {
        _scale = scale;
    }

    /// Gear-given numbers and attunement, in the blue this game already uses
    /// for energy that came from somewhere else.
    private static readonly Color GearBlue = new(0.42f, 0.68f, 1f);

    /// Condition, when it is costing the Workling something.
    private static readonly Color Warning = new(0.85f, 0.65f, 0.35f);

    private int S(float units) => System.Math.Max(1, (int)System.Math.Round(units * _scale));

    public override void _Ready()
    {
        Theme = WorklingsTheme.For(_scale);
        // AnchorsAndOffsets, not just Anchors. SetAnchorsPreset moves the
        // anchors and leaves the offsets where they were, so the panel keeps
        // whatever size it was built with — which is none — and the window comes
        // up empty with a tab bar squeezed into nothing.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // A column, not the tabs alone: the version line sits under every tab
        // rather than inside one, because "which build am I on" is a question
        // about the app and a tester should not have to find the right tab to
        // answer it.
        var column = new VBoxContainer();
        column.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        column.AddThemeConstantOverride("separation", 0);
        AddChild(column);

        _tabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        column.AddChild(_tabs);
        column.AddChild(VersionFooter());
    }

    /// The build, small and quiet along the bottom edge.
    ///
    /// Deliberately the dimmest thing on the screen. It is reference, not
    /// content — a tester needs to be able to read it off a screenshot, and
    /// nobody needs to notice it otherwise.
    private Control VersionFooter()
    {
        var line = new Label
        {
            Text = AppVersion.Label,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        line.AddThemeFontSizeOverride("font_size", S(11));
        line.AddThemeColorOverride("font_color", WorklingsTheme.Muted);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_right", S(10));
        margin.AddThemeConstantOverride("margin_bottom", S(4));
        margin.AddThemeConstantOverride("margin_top", S(2));
        margin.AddChild(line);
        return margin;
    }

    public void Show(PetState state)
    {
        _state = state;
        if (_tabs is null)
        {
            return;
        }

        // Which tab the player was on survives a rebuild — equipping an item
        // should not throw them back to the Character tab.
        int current = _tabs.CurrentTab;
        // The model bay survives the rebuild. It is the one child here that is
        // expensive and stateful: freeing it would reload the .glb and restart
        // the idle from frame one every time the player equips something, so
        // the Workling would twitch on every button press. Pulled out of the
        // old tree before the tabs are freed, and re-added by BuildCharacter.
        _bay?.GetParent()?.RemoveChild(_bay);
        foreach (var child in _tabs.GetChildren())
        {
            // Out of the tree *before* it is freed. QueueFree leaves the node in
            // place until the end of the frame, so the rebuilt "Character" tab
            // was added beside the old one still holding that name, and Godot
            // renamed the newcomer — which is where "@VBoxContainer@406" came
            // from on the tab bar the second time the screen was shown.
            _tabs.RemoveChild(child);
            child.QueueFree();
        }

        var sheet = CharacterSheet.Make(state);
        // The Character tab is the only one that fills its own height rather
        // than being a column that scrolls: the bay takes whatever the ledger
        // does not, which cannot happen inside a ScrollContainer.
        AddTab("Character", BuildCharacter(sheet, state), scroll: false);
        AddTab("Inventory", BuildInventory(state));
        AddTab("Skills", Placeholder(
            "The ability tree is designed and not built.\n\n"
          + "Families carry passives and classes lean on a signature stat; "
          + "neither has a surface yet."));
        AddTab("Care", BuildCare(state, sheet));

        if (current >= 0 && current < _tabs.GetTabCount())
        {
            _tabs.CurrentTab = current;
        }
    }

    private void AddTab(string title, Control content, bool scroll = true)
    {
        if (!scroll)
        {
            content.Name = title;
            _tabs.AddChild(content);
            return;
        }
        AddScrollingTab(title, content);
    }

    private void AddScrollingTab(string title, Control content)
    {
        var scroll = new ScrollContainer { Name = title };
        scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        // Fills the width so rows and bars stretch rather than hugging the left
        // edge of a window the player has widened.
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        var padded = Padded(content);
        padded.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(padded);
        _tabs.AddChild(scroll);
    }

    // MARK: - Character

    /// The Character tab: the Workling on the left, everything the sheet knows
    /// on the right.
    ///
    /// **Two columns, and the split is the design.** The ledger holds a fixed
    /// measure and the bay takes every pixel it does not, so widening the window
    /// makes the creature bigger rather than stretching a column of rows across
    /// dead space. It is the only one of the three layouts drawn for this that
    /// improves when the window is dragged wide, and the window is resizable.
    ///
    /// Gear lives here now, not only in Inventory. A character screen that
    /// cannot tell you what you are wearing is a stat readout.
    private Control BuildCharacter(CharacterSheet sheet, PetState state)
    {
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 0);

        root.AddChild(HeadBar(sheet, state));
        var progress = sheet.Progress;
        var xp = Bar(progress.Fraction);
        xp.CustomMinimumSize = new Vector2(0, S(4));
        root.AddChild(xp);

        var columns = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        columns.AddThemeConstantOverride("separation", 0);
        root.AddChild(columns);

        var left = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        left.AddThemeConstantOverride("separation", 0);
        // Built once and carried across rebuilds, and now told to grow: the bay
        // is the element with something to do with extra space.
        _bay ??= new ModelBay(S(200), _scale);
        _bay.SizeFlagsVertical = SizeFlags.ExpandFill;
        // Every rebuild, because the Family picker is in this panel: changing
        // race here has to change the body in the bay beside it. Idempotent by
        // creature, so the rebuilds that are not race changes cost nothing.
        _bay.Wear(state.Family);
        left.AddChild(_bay);
        left.AddChild(GearRail(state));
        columns.AddChild(left);

        columns.AddChild(new ColorRect
        {
            Color = WorklingsTheme.Brass with { A = 0.28f },
            CustomMinimumSize = new Vector2(S(1), 0),
        });
        columns.AddChild(Ledger(sheet, state));
        return root;
    }

    /// Name and level, across the top of both columns — the two things true of
    /// the Workling regardless of which column you are reading.
    private Control HeadBar(CharacterSheet sheet, PetState state)
    {
        var line = new HBoxContainer();
        var name = NameField(sheet.Name, state);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        // A name field the width of a wide window is a text editor, not a name.
        line.AddChild(new MaxWidth(S(340), name, centred: false));

        var right = new VBoxContainer();
        right.AddThemeConstantOverride("separation", 0);
        var level = Line($"Lv {sheet.Level}");
        level.HorizontalAlignment = HorizontalAlignment.Right;
        right.AddChild(level);
        var progress = sheet.Progress;
        var xp = Line(
            progress.XPForLevel <= 0
                ? "Maximum level"
                : $"{progress.XPIntoLevel:0} / {progress.XPForLevel:0} XP",
            WorklingsTheme.Muted);
        xp.HorizontalAlignment = HorizontalAlignment.Right;
        right.AddChild(xp);
        line.AddChild(right);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", S(14));
        margin.AddThemeConstantOverride("margin_right", S(14));
        margin.AddThemeConstantOverride("margin_top", S(10));
        margin.AddThemeConstantOverride("margin_bottom", S(8));
        margin.AddChild(line);
        return margin;
    }

    // MARK: - Gear

    /// The three slots, under the Workling and in front of the player.
    private Control GearRail(PetState state)
    {
        var rail = new HBoxContainer();
        rail.AddThemeConstantOverride("separation", S(7));
        foreach (var slot in ItemSlotExtensions.AllCases)
        {
            rail.AddChild(GearTile(state, slot));
        }

        var margin = new MarginContainer();
        foreach (string side in new[] { "left", "right", "top", "bottom" })
        {
            margin.AddThemeConstantOverride($"margin_{side}", S(8));
        }
        // Capped, because three tiles spread across a 780-pixel bay stop reading
        // as gear and start reading as empty shelves.
        margin.AddChild(new MaxWidth(S(430), rail));
        return margin;
    }

    /// One slot: what is in it, how good it is, and a way to change it.
    ///
    /// A `Button` with its own children rather than a panel with a click
    /// handler, so it is focusable and keyboard-operable for free. The children
    /// are anchored to it and ignore the mouse; a child that ate clicks would
    /// leave the tile dead in the middle, which is exactly where it is aimed at.
    private Control GearTile(PetState state, ItemSlot slot)
    {
        var equipped = state.Loadout[slot];
        bool filled = equipped is not null;
        var colour = filled
            ? ItemIcon.TierColour(equipped!.Value.Tier())
            : WorklingsTheme.Brass with { A = 0.75f };

        var tile = new Button
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(S(56), S(74)),
            // The slot's fantasy line, which has never been shown anywhere.
            TooltipText = slot.Fantasy(),
        };
        tile.AddThemeStyleboxOverride("normal", TileStyle(colour, filled, false));
        tile.AddThemeStyleboxOverride("hover", TileStyle(colour, filled, true));
        tile.AddThemeStyleboxOverride("pressed", TileStyle(colour, filled, true));
        tile.AddThemeStyleboxOverride("focus", TileStyle(colour, filled, true));
        tile.Pressed += () => ShowSlotMenu(tile, slot, state);

        var stack = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        stack.AddThemeConstantOverride("separation", S(1));
        stack.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, LayoutPresetMode.KeepSize, S(5));

        // The tier, in the corner, in the tier's own colour — the one thing you
        // want to read across the screen without focusing on it.
        var tier = Line(filled ? equipped!.Value.Tier().DisplayName().ToUpperInvariant() : " ", colour);
        tier.AddThemeFontSizeOverride("font_size", S(9));
        tier.HorizontalAlignment = HorizontalAlignment.Right;
        tier.MouseFilter = MouseFilterEnum.Ignore;
        stack.AddChild(tier);

        var centre = new CenterContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        centre.AddChild(new ItemIcon(slot, colour, S(22), filled));
        stack.AddChild(centre);

        var label = Line(slot.DisplayName().ToUpperInvariant(), WorklingsTheme.Muted);
        label.AddThemeFontSizeOverride("font_size", S(9));
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.MouseFilter = MouseFilterEnum.Ignore;
        stack.AddChild(label);

        var what = Line(
            filled ? equipped!.Value.DisplayName() : "empty",
            filled ? WorklingsTheme.Ink : WorklingsTheme.Muted with { A = 0.7f });
        what.AddThemeFontSizeOverride("font_size", S(10));
        what.HorizontalAlignment = HorizontalAlignment.Center;
        // Three tiles across 300 pixels cannot show "Everburning Backup" whole,
        // and a name that wraps would push the tile taller than its neighbours.
        what.ClipText = true;
        what.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        what.MouseFilter = MouseFilterEnum.Ignore;
        stack.AddChild(what);

        tile.AddChild(stack);
        return tile;
    }

    private StyleBoxFlat TileStyle(Color colour, bool filled, bool lit)
    {
        var box = new StyleBoxFlat
        {
            BgColor = lit ? WorklingsTheme.Highlight : new Color(0.065f, 0.055f, 0.045f, 1),
            BorderColor = filled ? colour with { A = lit ? 1f : 0.75f } : colour with { A = 0.5f },
        };
        box.SetBorderWidthAll(S(1));
        box.SetCornerRadiusAll(S(3));
        box.ContentMarginLeft = box.ContentMarginRight = S(4);
        box.ContentMarginTop = box.ContentMarginBottom = S(4);
        return box;
    }

    /// Click a slot, get the things that fit in it.
    ///
    /// The menu is the whole equip surface: what is worn is checked, everything
    /// else is a swap, and taking it off is the last entry. Which means the
    /// Inventory tab is now a browser rather than the only door to gear.
    private void ShowSlotMenu(Control anchor, ItemSlot slot, PetState state)
    {
        var menu = new PopupMenu { Theme = WorklingsTheme.For(_scale) };
        var options = state.AvailableItems(slot);
        var equipped = state.Loadout[slot];
        var byId = new Dictionary<int, Item>();

        for (int i = 0; i < options.Count; i++)
        {
            var item = options[i];
            // Priced for THIS Workling, attunement included — the same number
            // the Inventory tab quotes, from the same place.
            int bonus = ItemRates.Default.Modifier(item, state.Family);
            bool attuned = ItemRates.Default.IsAttuned(item, state.Family);
            menu.AddRadioCheckItem(
                $"{item.DisplayName()}   {item.Tier().DisplayName()} · +{bonus} "
              + $"{item.Stat().DisplayName()}{(attuned ? "  ✦" : "")}", i);
            menu.SetItemChecked(i, item == equipped);
            byId[i] = item;
        }

        if (options.Count == 0)
        {
            menu.AddItem("Nothing for this slot yet", -1);
            menu.SetItemDisabled(menu.ItemCount - 1, true);
        }

        const int TakeOff = -2;
        if (equipped is not null)
        {
            menu.AddSeparator();
            menu.AddItem("Take it off", TakeOff);
        }

        menu.IdPressed += id =>
        {
            if (id == TakeOff)
            {
                StateChanged?.Invoke(state.ClearingSlot(slot));
                return;
            }
            // Equipping routes through PetState, which validates ownership and
            // slot — no surface builds a loadout itself.
            if (byId.TryGetValue((int)id, out var item) && item != equipped)
            {
                StateChanged?.Invoke(state.Equipping(item));
            }
        };
        // Parented to the panel rather than the tile: equipping rebuilds the
        // tabs, and a menu owned by a tile would be freed mid-signal.
        AddChild(menu);
        menu.PopupHide += menu.QueueFree;
        menu.ResetSize();
        menu.Popup(new Rect2I(
            (Vector2I)(anchor.GetScreenPosition() + new Vector2(0, anchor.Size.Y + S(2))),
            new Vector2I((int)Mathf.Max(menu.Size.X, anchor.Size.X), 0)));
    }

    // MARK: - The ledger

    /// Everything the sheet knows, at a fixed measure.
    ///
    /// Fixed is the point: it holds its width while the bay absorbs the rest, so
    /// the screen has one element that grows and one that stays readable.
    private Control Ledger(CharacterSheet sheet, PetState state)
    {
        var column = Column();
        column.AddChild(FamilyPicker(state));
        column.AddChild(ClassPicker(state));

        column.AddChild(Section("Stats"));
        foreach (var row in sheet.Rows)
        {
            var line = new HBoxContainer();
            var name = Line(
                row.Stat.DisplayName() + (row.IsSignature ? "  ★" : ""),
                row.IsSignature ? WorklingsTheme.Ink : WorklingsTheme.Muted);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            line.AddChild(name);

            // Base and gear kept apart on purpose. "Power 27" tells you nothing
            // about whether taking the Hone off would hurt.
            var basis = Line($"{row.Base}", WorklingsTheme.Ink);
            basis.HorizontalAlignment = HorizontalAlignment.Right;
            basis.CustomMinimumSize = new Vector2(S(30), 0);
            line.AddChild(basis);

            var gear = Line(
                row.GearBonus > 0 ? $"+{row.GearBonus}" : "", GearBlue);
            gear.HorizontalAlignment = HorizontalAlignment.Right;
            gear.CustomMinimumSize = new Vector2(S(30), 0);
            line.AddChild(gear);
            column.AddChild(line);
        }

        column.AddChild(Section("In a fight"));
        var combat = sheet.Combat;
        var numbers = new HBoxContainer();
        numbers.AddThemeConstantOverride("separation", S(6));
        numbers.AddChild(Readout($"{combat.MaxHP}", "HP"));
        numbers.AddChild(Readout($"{combat.Strike}", "Strike"));
        numbers.AddChild(Readout($"{combat.CritChance:P0}", "Crit"));
        column.AddChild(numbers);

        if (combat.IsDiminished)
        {
            // The one place the screen nags, and only when there is something to
            // nag about: a Workling in poor condition fights at a fraction of
            // itself, and that is invisible everywhere else.
            column.AddChild(Section("Condition"));
            var condition = new HBoxContainer();
            condition.AddThemeConstantOverride("separation", S(8));
            condition.AddChild(Line($"{combat.Effectiveness:P0}", Warning));
            var bar = Bar(combat.Effectiveness, Warning);
            bar.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            bar.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            condition.AddChild(bar);
            column.AddChild(condition);
            column.AddChild(Line("It is not at its best.", WorklingsTheme.Muted, wrap: true));
        }

        if (sheet.AttunedItems.Count > 0)
        {
            column.AddChild(Section("Attunement"));
            foreach (var item in sheet.AttunedItems)
            {
                column.AddChild(Line($"✦ {item.DisplayName()}", GearBlue, wrap: true));
            }
            column.AddChild(Line(
                $"{PetBody.Label(state.Family)} suits it — worth more here than the catalogue says.",
                WorklingsTheme.Muted, wrap: true));
        }

        var scroll = new ScrollContainer
        {
            // Wide enough for "Vitality 24 +5" and a picker, narrow enough that
            // the bay is still the larger half at the window's opening size.
            CustomMinimumSize = new Vector2(S(250), 0),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        var padded = Padded(column);
        padded.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(padded);
        return scroll;
    }

    /// One derived combat number in its own box, because three numbers in a row
    /// of prose is a sentence and three boxes is a readout.
    private Control Readout(string value, string caption)
    {
        var box = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var style = new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.035f) };
        style.SetCornerRadiusAll(S(3));
        style.ContentMarginLeft = style.ContentMarginRight = S(6);
        style.ContentMarginTop = style.ContentMarginBottom = S(5);
        box.AddThemeStyleboxOverride("panel", style);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 0);
        var number = Line(value);
        number.AddThemeFontOverride("font", GD.Load<Font>("res://assets/fonts/ChakraPetch-Bold.ttf"));
        number.AddThemeFontSizeOverride("font_size", S(16));
        column.AddChild(number);
        var label = Line(caption.ToUpperInvariant(), WorklingsTheme.Muted);
        label.AddThemeFontSizeOverride("font_size", S(9));
        column.AddChild(label);
        box.AddChild(column);
        return box;
    }

    /// A section heading with its rule, so the ledger reads as parts rather than
    /// as one long list.
    private Control Section(string text)
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", S(3));
        var spacer = new Control { CustomMinimumSize = new Vector2(0, S(6)) };
        column.AddChild(spacer);
        var label = Line(text.ToUpperInvariant(), WorklingsTheme.Muted);
        label.AddThemeFontSizeOverride("font_size", S(10));
        column.AddChild(label);
        column.AddChild(Rule());
        return column;
    }

    // MARK: - Inventory

    private Control BuildInventory(PetState state)
    {
        var column = Column();
        column.AddChild(Heading("Carried"));

        foreach (var slot in ItemSlotExtensions.AllCases)
        {
            column.AddChild(Rule());
            var equipped = state.Loadout[slot];
            column.AddChild(Line(
                $"{slot.DisplayName()} — {(equipped?.DisplayName() ?? "empty")}",
                WorklingsTheme.Muted));

            var available = state.AvailableItems(slot);
            if (available.Count == 0)
            {
                column.AddChild(Line("  nothing for this slot yet", WorklingsTheme.Muted));
                continue;
            }

            // Best tier first, which AvailableItems already does — with three
            // tiers of everything, acquisition order buries a hard-won Prime
            // item under the junk that dropped before it.
            foreach (var item in available)
            {
                column.AddChild(ItemRow(state, item, isEquipped: equipped == item));
            }
        }

        return column;
    }

    private Control ItemRow(PetState state, Item item, bool isEquipped)
    {
        var line = new HBoxContainer();

        // Priced for THIS Workling, attunement included, rather than showing the
        // item's base number — a Ward that suits your family is worth more on
        // you than the catalogue says, and that is the whole point of the soft
        // synergy.
        int bonus = ItemRates.Default.Modifier(item, state.Family);
        bool attuned = ItemRates.Default.IsAttuned(item, state.Family);
        var label = Line(
            $"  {item.DisplayName()}   {item.Tier().DisplayName()}  ·  +{bonus} "
          + $"{item.Stat().DisplayName()}{(attuned ? "  ✦" : "")}",
            isEquipped ? WorklingsTheme.Ink : WorklingsTheme.Muted);
        label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        line.AddChild(label);

        var button = new Button
        {
            Text = isEquipped ? "Take off" : "Equip",
            CustomMinimumSize = new Vector2(S(90), 0),
        };
        // Equipping routes through PetState, which validates ownership and slot
        // — the panel never builds a loadout itself, so no surface can smuggle
        // an item into the wrong place.
        button.Pressed += () => StateChanged?.Invoke(
            isEquipped ? state.ClearingSlot(item.Slot()) : state.Equipping(item));
        line.AddChild(button);

        return line;
    }

    // MARK: - Care

    private Control BuildCare(PetState state, CharacterSheet sheet)
    {
        var column = Column();
        column.AddChild(Heading("Condition"));
        column.AddChild(Line($"Mood: {state.Mood}", WorklingsTheme.Muted));
        column.AddChild(Rule());

        // Fullness rather than hunger, because that is the vocabulary every
        // surface uses — the internal name is the need, the design word is its
        // inverse.
        NeedRow(column, "Fullness", state.Needs.Fullness);
        NeedRow(column, "Energy", state.Needs.Energy);
        NeedRow(column, "Happiness", state.Needs.Happiness);
        NeedRow(column, "Trust", state.Needs.Trust);

        column.AddChild(Rule());
        column.AddChild(Line(
            $"Learning rate {state.Needs.XPMultiplier(0.2):P0}", WorklingsTheme.Muted));
        column.AddChild(Line(
            "Condition scales the XP a Workling earns and how hard it fights. "
          + "Feed it, play with it, let it sleep.",
            WorklingsTheme.Muted, wrap: true));
        return column;
    }

    private void NeedRow(VBoxContainer column, string name, double value)
    {
        var line = new HBoxContainer();
        var label = Line(name, WorklingsTheme.Muted);
        label.CustomMinimumSize = new Vector2(S(110), 0);
        line.AddChild(label);
        var bar = Bar(value / 100);
        bar.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        line.AddChild(bar);
        line.AddChild(Line($" {value:0}", WorklingsTheme.Ink));
        column.AddChild(line);
    }

    // MARK: - Pieces

    /// A padded column. The margin is a real parent rather than a note on the
    /// column, so the padding exists in the tree instead of only in intent —
    /// returning the inner box and adding *that* to the tab leaves the margin
    /// orphaned and the text jammed against the window edge.
    private VBoxContainer Column()
    {
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", S(6));
        column.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        return column;
    }

    /// Wraps a column in its padding on the way into a tab.
    private Control Padded(Control inner)
    {
        var margin = new MarginContainer();
        foreach (string side in new[] { "left", "right", "top", "bottom" })
        {
            margin.AddThemeConstantOverride($"margin_{side}", S(14));
        }
        margin.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        margin.AddChild(inner);
        return margin;
    }

    /// Which family the Workling belongs to.
    ///
    /// All five are listed and the ones with no usable body are greyed out — the
    /// roster reads as five so the shape of the design is visible, and each
    /// un-greys on its own the day its model lands. `PetBody` is the gate and
    /// carries the roster's current state.
    ///
    /// Choosing a family now changes the body in the bay as well as the
    /// mechanics — `ModelBay.Wear` resolves the race through `CreatureRoster`,
    /// the same way the desktop pet does. Only races with a renderable body are
    /// selectable (`PetBody.IsPickable`), so the picker cannot put the bay in
    /// front of a creature that does not exist.
    private Control FamilyPicker(PetState state)
    {
        var picker = Picker("Family");
        var families = PetFamilyExtensions.AllCases;
        for (int i = 0; i < families.Length; i++)
        {
            var family = families[i];
            picker.AddItem(PetBody.Label(family), i);
            picker.SetItemDisabled(i, !PetBody.IsPickable(family));
            if (family == state.Family) picker.Selected = i;
        }
        picker.ItemSelected += index =>
            StateChanged?.Invoke(state.SelectingFamily(families[(int)index]));
        return Row("Family", picker);
    }

    /// Which class it fights as. The signature stat and the growth weighting both
    /// come from here, so this is the single largest choice on the screen.
    private Control ClassPicker(PetState state)
    {
        var picker = Picker("Class");
        var classes = PetClassExtensions.AllCases;
        for (int i = 0; i < classes.Length; i++)
        {
            var petClass = classes[i];
            // The role, because "Aegis" says nothing to someone meeting it for
            // the first time and "Aegis — Tank" says all of it.
            picker.AddItem($"{petClass.DisplayName()} — {petClass.Role()}", i);
            if (petClass == state.PetClass) picker.Selected = i;
        }
        picker.ItemSelected += index =>
            StateChanged?.Invoke(state.SelectingClass(classes[(int)index]));
        return Row("Class", picker);
    }

    private OptionButton Picker(string name)
    {
        var picker = new OptionButton { Name = name, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        picker.AddThemeFontSizeOverride("font_size", S(14));
        // Clipped, and this is load-bearing. A Button's minimum width is its
        // longest item — "Juggernaut — Heavy Offense" — so an unclipped picker
        // sets the ledger's minimum width, which sets the whole layout's, and
        // the two columns end up wider than the window they are in.
        // `FitToLongestItem` is on by default and ignores clipping: it forces the
        // button's minimum width to the longest entry in the list whatever else
        // is set, which is the actual reason the ledger was too wide.
        picker.FitToLongestItem = false;
        picker.ClipText = true;
        picker.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        picker.CustomMinimumSize = new Vector2(S(100), 0);
        // Its own popup is a separate window and inherits nothing from here.
        picker.GetPopup().Theme = WorklingsTheme.For(_scale);
        return picker;
    }

    /// A labelled row, so the two pickers read as a pair of settings rather than
    /// as two unexplained dropdowns.
    private Control Row(string label, Control control)
    {
        var line = new HBoxContainer();
        var caption = Line(label, WorklingsTheme.Muted);
        caption.CustomMinimumSize = new Vector2(S(64), 0);
        line.AddChild(caption);
        line.AddChild(control);
        return line;
    }

    /// The Workling's name, editable in place.
    ///
    /// A field rather than a dialog, and here rather than in the menu, because
    /// this is the screen about *who the pet is* — the same place family and
    /// class belong. The menu item now opens this screen instead of being
    /// permanently greyed out.
    ///
    /// Committed on Enter or on losing focus, never per keystroke: a rename that
    /// fired on every character would write the save two dozen times and show
    /// the pet being called "F", then "Fr", then "Fre".
    private Control NameField(string name, PetState state)
    {
        var field = new LineEdit
        {
            Text = name,
            // The cap PetState enforces anyway. Enforcing it here too means the
            // field cannot show a name that would be silently refused.
            MaxLength = PetState.MaximumNameLength,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        field.AddThemeFontOverride(
            "font", GD.Load<Font>("res://assets/fonts/ChakraPetch-Bold.ttf"));
        field.AddThemeFontSizeOverride("font_size", S(21));
        field.AddThemeColorOverride("font_color", WorklingsTheme.Ink);

        // It has to LOOK like a field. Drawn with the theme's default
        // LineEdit background it was indistinguishable from the heading it
        // replaced, so the one new thing on this screen was invisible — the same
        // way the repository picker was, and for the same reason.
        var box = new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.05f) };
        box.SetCornerRadiusAll(S(5));
        box.ContentMarginLeft = box.ContentMarginRight = S(8);
        box.ContentMarginTop = box.ContentMarginBottom = S(4);
        var focused = new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.09f) };
        focused.SetCornerRadiusAll(S(5));
        focused.ContentMarginLeft = focused.ContentMarginRight = S(8);
        focused.ContentMarginTop = focused.ContentMarginBottom = S(4);
        focused.BorderWidthBottom = S(2);
        focused.BorderColor = WorklingsTheme.Brass with { A = 1 };
        field.AddThemeStyleboxOverride("normal", box);
        field.AddThemeStyleboxOverride("focus", focused);

        void Commit()
        {
            // PetState.Renamed refuses an empty or over-long name by returning
            // the state unchanged, so the field is put back to whatever the pet
            // is actually called rather than left showing a name it does not
            // have.
            var renamed = state.Renamed(field.Text);
            if (!renamed.Equals(state))
            {
                StateChanged?.Invoke(renamed);
                return;
            }
            field.Text = state.Name;
        }

        field.TextSubmitted += _ => Commit();
        field.FocusExited += Commit;
        return field;
    }

    private Label Heading(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontOverride(
            "font", GD.Load<Font>("res://assets/fonts/ChakraPetch-Bold.ttf"));
        label.AddThemeFontSizeOverride("font_size", S(21));
        label.AddThemeColorOverride("font_color", WorklingsTheme.Ink);
        return label;
    }

    /// Wrapping is opt-in, not the default. A wrapping label inside an HBox
    /// shrinks to whatever width is going and wraps *per character*, which
    /// turned "23 +3" into a vertical column of digits. Only the prose lines
    /// want it.
    private Label Line(string text, Color? colour = null, bool wrap = false)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", S(14));
        label.AddThemeColorOverride("font_color", colour ?? WorklingsTheme.Ink);
        if (wrap)
        {
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        }
        return label;
    }

    private ProgressBar Bar(double fraction, Color? fillColour = null)
    {
        var bar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 1,
            Value = System.Math.Clamp(fraction, 0, 1),
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, S(8)),
        };
        var background = new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.08f) };
        background.SetCornerRadiusAll(S(4));
        var fill = new StyleBoxFlat { BgColor = fillColour ?? WorklingsTheme.Brass with { A = 1 } };
        fill.SetCornerRadiusAll(S(4));
        bar.AddThemeStyleboxOverride("background", background);
        bar.AddThemeStyleboxOverride("fill", fill);
        return bar;
    }

    private Control Rule() => new ColorRect
    {
        Color = WorklingsTheme.Brass with { A = 0.28f },
        CustomMinimumSize = new Vector2(0, S(1)),
    };

    private Control Placeholder(string text)
    {
        var column = Column();
        column.AddChild(Line(text, WorklingsTheme.Muted, wrap: true));
        return column;
    }
}
