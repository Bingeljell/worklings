using Godot;
using System.Collections.Generic;
using Worklings.Core.Pet;
using Worklings.Core.Progression;

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

    public event System.Action<PetState>? StateChanged;

    public CharacterPanel(float scale)
    {
        _scale = scale;
    }

    private int S(float units) => System.Math.Max(1, (int)System.Math.Round(units * _scale));

    public override void _Ready()
    {
        Theme = WorklingsTheme.For(_scale);
        // AnchorsAndOffsets, not just Anchors. SetAnchorsPreset moves the
        // anchors and leaves the offsets where they were, so the panel keeps
        // whatever size it was built with — which is none — and the window comes
        // up empty with a tab bar squeezed into nothing.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        _tabs = new TabContainer();
        _tabs.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_tabs);
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
        foreach (var child in _tabs.GetChildren())
        {
            child.QueueFree();
        }

        var sheet = CharacterSheet.Make(state);
        AddTab("Character", BuildCharacter(sheet, state));
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

    private void AddTab(string title, Control content)
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

    private Control BuildCharacter(CharacterSheet sheet, PetState state)
    {
        var column = Column();

        column.AddChild(Heading(sheet.Name));
        column.AddChild(Line(
            $"{sheet.Family.DisplayName()}  ·  {sheet.PetClass.DisplayName()}  ·  Level {sheet.Level}",
            WorklingsTheme.Muted));

        // The XP bar, with the numbers beside it. A bar alone tells you roughly
        // where you are; the numbers tell you whether one more delve does it.
        var progress = sheet.Progress;
        column.AddChild(Bar(progress.Fraction));
        column.AddChild(Line(
            progress.XPForLevel <= 0
                ? "Maximum level"
                : $"{progress.XPIntoLevel:0} / {progress.XPForLevel:0} XP to level {sheet.Level + 1}",
            WorklingsTheme.Muted));

        column.AddChild(Rule());
        column.AddChild(Line("Stats", WorklingsTheme.Muted));

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
            basis.CustomMinimumSize = new Vector2(S(34), 0);
            line.AddChild(basis);

            var gear = Line(
                row.GearBonus > 0 ? $"+{row.GearBonus}" : "",
                new Color(0.55f, 0.78f, 0.55f));
            gear.HorizontalAlignment = HorizontalAlignment.Right;
            gear.CustomMinimumSize = new Vector2(S(38), 0);
            line.AddChild(gear);
            column.AddChild(line);
        }

        column.AddChild(Rule());
        column.AddChild(Line("In a fight", WorklingsTheme.Muted));
        var combat = sheet.Combat;
        column.AddChild(Line(
            $"{combat.MaxHP} HP   ·   {combat.Strike} strike   ·   {combat.CritChance:P0} crit"));

        if (combat.IsDiminished)
        {
            // The one place the screen nags, and only when there is something to
            // nag about: a Workling in poor condition fights at a fraction of
            // itself, and that is invisible everywhere else.
            column.AddChild(Line(
                $"Condition {combat.Effectiveness:P0} — it is not at its best.",
                new Color(0.85f, 0.65f, 0.35f)));
        }

        if (sheet.AttunedItems.Count > 0)
        {
            var names = new List<string>();
            foreach (var item in sheet.AttunedItems) names.Add(item.DisplayName());
            column.AddChild(Line(
                "Attuned: " + string.Join(", ", names),
                new Color(0.55f, 0.78f, 0.55f), wrap: true));
        }

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

    private ProgressBar Bar(double fraction)
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
        var fill = new StyleBoxFlat { BgColor = WorklingsTheme.Brass with { A = 1 } };
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
