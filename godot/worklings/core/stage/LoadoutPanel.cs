using Godot;
using System.Collections.Generic;
using Worklings.Core.Combat;
using Worklings.Core.Pet;
using Worklings.Core.Progression;

namespace Worklings.Core.Stage;

/// The prep screen: beat two of a delve, where the briefing pays off.
///
/// The design gives the briefing exactly one gameplay job — to tell the player
/// what kind of prep this delve rewards — so the narration and the choice live
/// on one screen rather than as two beats that scroll past each other. What the
/// player picks here is gear (one item per slot, from what they own) and the
/// starting Approach; both feed straight into `Combatant.Pet`, which folds gear
/// in ahead of condition.
///
/// The panel owns the whole interaction and hands back a `PetState`, because
/// equipping is a `PetState` operation that validates ownership — reimplementing
/// the rules here to hold a loose `Loadout` would be a second, worse copy of
/// them. Every change re-reads `CharacterSheet`, which is built from the same
/// `Combatant.Pet` the fight uses, so the numbers on this screen cannot drift
/// from the numbers in the encounter.
///
/// Built in code rather than as a .tscn, matching CombatHud, while the layout is
/// still being tuned.
public sealed class LoadoutPanel
{
    private static readonly Approach[] Approaches =
        { Approach.Aggressive, Approach.Careful, Approach.Clever };

    /// What each Approach actually does, so the choice is legible without the
    /// player having read the design doc.
    private static string Describe(Approach approach) => approach switch
    {
        Approach.Aggressive => "Strike every round. No held resources.",
        Approach.Careful => "Brace while hurt, strike once recovered.",
        _ => "Strike, holding the Signature for a finish.",
    };

    private readonly CanvasLayer _layer;
    private readonly Label _title;
    private readonly Label _briefing;
    private readonly Label _readout;
    private readonly Label _note;
    private readonly List<Label[]> _rows = new();
    private readonly List<Label> _statChips = new();

    /// One line per choosable thing: the three gear slots, then the Approach.
    private const int SlotCount = 3;
    private const int RowCount = SlotCount + 1;
    private const int ApproachRow = SlotCount;

    /// Per slot, "nothing" followed by everything owned that fits it. Nothing is
    /// a real option — an empty slot is a legal loadout, and a player who wants
    /// the Wit from a Charm they haven't got yet should see the slot empty
    /// rather than be forced into junk.
    private readonly List<List<Item?>> _options = new();
    private readonly int[] _picked = new int[SlotCount];
    private int _approachIndex;
    private int _cursor;

    private PetState _state = null!;

    /// The state with this screen's choices applied — gear equipped, everything
    /// else carried forward untouched.
    public PetState Result => _state;
    public Approach Approach => Approaches[_approachIndex];
    public bool IsOpen => _layer.Visible;

    public LoadoutPanel(Node parent)
    {
        _layer = new CanvasLayer { Visible = false };
        parent.AddChild(_layer);

        var root = new Control { AnchorRight = 1, AnchorBottom = 1 };
        _layer.AddChild(root);

        // A wash over the stage. The room stays visible behind it — prep is a
        // pause in the dungeon, not a trip to a menu.
        var scrim = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.55f),
            AnchorRight = 1, AnchorBottom = 1,
        };
        root.AddChild(scrim);

        var card = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            CustomMinimumSize = new Vector2(760, 0),
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
        };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.055f, 0.05f, 0.94f),
            BorderColor = new Color(0.42f, 0.36f, 0.28f, 0.9f),
            ContentMarginLeft = 40, ContentMarginRight = 40,
            ContentMarginTop = 32, ContentMarginBottom = 32,
        };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(3);
        card.AddThemeStyleboxOverride("panel", style);
        root.AddChild(card);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 14);
        card.AddChild(column);

        _title = StageType.Label("", 30, StageType.Ink, bold: true);
        column.AddChild(_title);

        _briefing = StageType.Label("", 20, StageType.Muted);
        _briefing.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _briefing.CustomMinimumSize = new Vector2(680, 0);
        column.AddChild(_briefing);

        column.AddChild(Rule());

        // Three columns: what the line is, what is chosen, what it is worth.
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 28);
        grid.AddThemeConstantOverride("v_separation", 10);
        column.AddChild(grid);

        for (int i = 0; i < RowCount; i++)
        {
            var label = StageType.Label("", 21, StageType.Muted);
            label.CustomMinimumSize = new Vector2(120, 0);
            var choice = StageType.Label("", 21, StageType.Ink);
            choice.CustomMinimumSize = new Vector2(300, 0);
            var effect = StageType.Label("", 19, StageType.Faint);
            grid.AddChild(label);
            grid.AddChild(choice);
            grid.AddChild(effect);
            _rows.Add(new[] { label, choice, effect });
        }

        column.AddChild(Rule());

        // The stat strip is the reason any of this matters: gear moves these
        // numbers, and the fight reads exactly them.
        var chips = new HBoxContainer();
        chips.AddThemeConstantOverride("separation", 22);
        column.AddChild(chips);
        foreach (var _ in PetStatKindExtensions.AllCases)
        {
            var chip = StageType.Label("", 19, StageType.Ink);
            chips.AddChild(chip);
            _statChips.Add(chip);
        }

        _readout = StageType.Label("", 19, StageType.Muted);
        column.AddChild(_readout);

        _note = StageType.Label(
            "↑↓ choose a line   ←→ change it   [Enter] descend",
            18, StageType.Faint);
        column.AddChild(_note);
    }

    private static Control Rule() => new ColorRect
    {
        Color = new Color(1, 1, 1, 0.10f),
        CustomMinimumSize = new Vector2(0, 1),
    };

    /// Opens on a pet and the Approach it is currently carrying, preselecting
    /// whatever is already equipped so confirming without touching anything is
    /// the same loadout the player left the last delve in.
    public void Open(PetState state, Approach approach, string title, string briefing)
    {
        _state = state;
        _title.Text = title;
        _briefing.Text = briefing;

        _options.Clear();
        for (int i = 0; i < SlotCount; i++)
        {
            var slot = ItemSlotExtensions.AllCases[i];
            var choices = new List<Item?> { null };
            foreach (var item in state.AvailableItems(slot)) choices.Add(item);
            _options.Add(choices);
            _picked[i] = System.Math.Max(0, choices.IndexOf(state.Loadout[slot]));
        }
        _approachIndex = System.Array.IndexOf(Approaches, approach);
        if (_approachIndex < 0) _approachIndex = 0;
        _cursor = 0;
        _layer.Visible = true;
        Refresh();
    }

    public void Close() => _layer.Visible = false;

    /// Equips the best owned item in every slot. AvailableItems is already
    /// sorted best-first, so this is the choice a player would make without
    /// thinking about it — used by an unattended run, which would otherwise
    /// collect gear for hours and never put any of it on.
    public void TakeBestAvailable()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (_options[i].Count < 2) continue;
            _picked[i] = 1;
            _state = _state.Equipping(_options[i][1]!.Value, ItemSlotExtensions.AllCases[i]);
        }
        Refresh();
    }

    /// Handles one key. Returns true when the player has confirmed and the delve
    /// should begin.
    public bool HandleKey(Key key)
    {
        switch (key)
        {
            case Key.Up:
                _cursor = (_cursor + RowCount - 1) % RowCount;
                break;
            case Key.Down:
                _cursor = (_cursor + 1) % RowCount;
                break;
            case Key.Left:
                Cycle(-1);
                break;
            case Key.Right:
                Cycle(1);
                break;
            case Key.Enter or Key.KpEnter or Key.Space:
                return true;
            default:
                return false;
        }
        Refresh();
        return false;
    }

    private void Cycle(int step)
    {
        if (_cursor == ApproachRow)
        {
            _approachIndex = (_approachIndex + Approaches.Length + step) % Approaches.Length;
            return;
        }
        var choices = _options[_cursor];
        _picked[_cursor] = (_picked[_cursor] + choices.Count + step) % choices.Count;
        var slot = ItemSlotExtensions.AllCases[_cursor];
        // Straight onto the state, so the sheet below is the real answer rather
        // than a preview that could disagree with what the fight later reads.
        _state = choices[_picked[_cursor]] is Item item
            ? _state.Equipping(item, slot)
            : _state.ClearingSlot(slot);
    }

    private void Refresh()
    {
        var rates = ItemRates.Default;
        for (int i = 0; i < SlotCount; i++)
        {
            var slot = ItemSlotExtensions.AllCases[i];
            var chosen = _options[i][_picked[i]];
            _rows[i][0].Text = (i == _cursor ? "▸ " : "  ") + slot.DisplayName();
            _rows[i][1].Text = chosen?.DisplayName() ?? "—";
            _rows[i][2].Text = chosen is Item item
                ? $"+{rates.Modifier(item, _state.Family)} {item.Stat().DisplayName()}"
                  // The attunement rider is a real number the player is already
                  // being paid; marking it is what makes it discoverable.
                  + (rates.IsAttuned(item, _state.Family) ? "  ◈ attuned" : "")
                : slot.Fantasy();
            _rows[i][0].AddThemeColorOverride(
                "font_color", i == _cursor ? StageType.Ink : StageType.Muted);
        }

        _rows[ApproachRow][0].Text =
            (_cursor == ApproachRow ? "▸ " : "  ") + "Approach";
        _rows[ApproachRow][1].Text = Approach.ToString();
        _rows[ApproachRow][2].Text = Describe(Approach);
        _rows[ApproachRow][0].AddThemeColorOverride(
            "font_color", _cursor == ApproachRow ? StageType.Ink : StageType.Muted);

        var sheet = CharacterSheet.Make(_state);
        for (int i = 0; i < sheet.Rows.Count && i < _statChips.Count; i++)
        {
            var row = sheet.Rows[i];
            _statChips[i].Text = row.GearBonus > 0
                ? $"{row.Stat.DisplayName()} {row.Base}+{row.GearBonus}"
                : $"{row.Stat.DisplayName()} {row.Base}";
            _statChips[i].AddThemeColorOverride(
                "font_color", row.GearBonus > 0 ? FamilyEnergy.Of(_state.Family) : StageType.Ink);
        }

        var combat = sheet.Combat;
        _readout.Text =
            $"Max HP {combat.MaxHP}   ·   Strike {combat.Strike}   ·   "
          + $"Crit {combat.CritChance * 100:0}%"
          + (combat.IsDiminished
                ? $"   ·   condition {combat.Effectiveness * 100:0}% — it is not at its best"
                : "");
    }
}
