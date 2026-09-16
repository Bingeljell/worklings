using Godot;
using System.Collections.Generic;
using Worklings.Core.Combat;
using Worklings.Core.Pet;
using Worklings.Core.Progression;
using Worklings.Core.Roster;

namespace Worklings.Core.Stage;

/// The prep screen: beat two of a delve, where the briefing pays off.
///
/// The design gives the briefing exactly one gameplay job — to tell the player
/// what kind of prep this delve rewards — so the narration and the choice live
/// on one screen rather than as two beats that scroll past each other. What the
/// player picks here is the body and its gear — one item per slot, from what
/// they own — which feed straight into `Combatant.Pet`, folding gear in ahead of
/// condition. The starting Approach used to be a third choice on this screen and
/// was removed with the stances themselves: moves are chosen per round now.
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
    /// What a creature's signature looks like, in the one line the prep screen
    /// has room for. The player is choosing a body; this is the half of that
    /// choice they can actually see in the fight.
    private static string Describe(AbilitySignature signature) => signature switch
    {
        AbilitySignature.LightningStrike => "calls lightning down onto its mark",
        AbilitySignature.FireShockwave => "drives a ring of fire out across the floor",
        AbilitySignature.GhostVolley => "throws spectral paws ahead of its reach",
        AbilitySignature.Roots => "breaks the floor open beneath its target",
        _ => "no signature of its own yet",
    };

    private readonly CanvasLayer _layer;
    private readonly Label _title;
    private readonly Label _briefing;
    private readonly Label _readout;
    private readonly Label _note;
    private readonly List<Label[]> _rows = new();
    private readonly List<Label> _statChips = new();

    /// One line per choosable thing: the dungeon, the Workling, the three gear
    /// slots.
    ///
    /// **The dungeon is first because it is the question the other rows answer.**
    /// The briefing's one job is telling you what kind of prep this delve
    /// rewards, so which place you are going has to be settled before "what do I
    /// bring" means anything. It is also the reason this screen exists at all
    /// now: the paw menu says *Enter the Dungeon*, and this is where you say
    /// which.
    ///
    /// The Workling is next because it is the largest of the remaining ones —
    /// it decides the body, the energy colour, the signature and which gear is
    /// attuned, and the three slot rows are read against it.
    /// **The Approach row is gone.** It set a standing stance the fight then
    /// derived actions from; the player now picks a move every round, so a
    /// stance chosen at the briefing decides nothing. Leaving it would have been
    /// a control that did not control anything.
    private const int DungeonRow = 0;
    private const int WorklingRow = 1;
    private const int SlotCount = 3;
    private const int FirstSlotRow = 2;
    private const int RowCount = FirstSlotRow + SlotCount;

    /// Per slot, "nothing" followed by everything owned that fits it. Nothing is
    /// a real option — an empty slot is a legal loadout, and a player who wants
    /// the Wit from a Charm they haven't got yet should see the slot empty
    /// rather than be forced into junk.
    private readonly List<List<Item?>> _options = new();
    private readonly int[] _picked = new int[SlotCount];

    /// Every Workling a player may bring, from the roster.
    ///
    /// **The shipped rule is one Workling per player, locked at onboarding** —
    /// one creature, one class, chosen once. This row is the alpha stand-in for
    /// an onboarding screen that does not exist yet, and it is a query rather
    /// than a hardcoded pair precisely so that the pool grows on its own as
    /// bodies land: a creature added to `CreatureRoster` is offered here with no
    /// change to this file. When onboarding arrives, the lock is a creature id
    /// on `PetState` and this row stops being a choice.
    private readonly List<Creature> _creatures = new(CreatureRoster.Playable());
    private int _creatureIndex;

    /// Every place, not only the enterable ones. A Planned dungeon is shown and
    /// refused rather than hidden, for the same reason the family picker lists
    /// races it will not let you be: the world should read as bigger than the
    /// one door that is finished.
    private readonly List<Dungeon> _dungeons = new(DungeonRoster.All);
    private int _dungeonIndex;
    private int _cursor;

    private PetState _state = null!;

    /// The state with this screen's choices applied — gear equipped, everything
    /// else carried forward untouched.
    public PetState Result => _state;

    /// The body the player is descending in.
    public Creature Creature =>
        _creatures.Count > 0 ? _creatures[_creatureIndex] : CreatureRoster.TempestRam;

    /// The place the player is descending into.
    public Dungeon Dungeon =>
        _dungeons.Count > 0 ? _dungeons[_dungeonIndex] : DungeonRoster.Default;
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

    /// Opens on a pet, preselecting whatever is already equipped so confirming
    /// without touching anything is the same loadout the player left the last
    /// delve in.
    public void Open(PetState state)
    {
        _state = state;

        // Preselect the first place that can actually be entered, so a build
        // whose first roster entry is Planned still opens on a real door.
        _dungeonIndex = 0;
        for (int i = 0; i < _dungeons.Count; i++)
            if (_dungeons[i].IsEnterable) { _dungeonIndex = i; break; }

        // Preselect the creature matching the Workling on disk, so confirming
        // without touching anything descends as whoever you already were.
        _creatureIndex = 0;
        for (int i = 0; i < _creatures.Count; i++)
            if (_creatures[i].Family == state.Family) { _creatureIndex = i; break; }

        _options.Clear();
        for (int i = 0; i < SlotCount; i++)
        {
            var slot = ItemSlotExtensions.AllCases[i];
            var choices = new List<Item?> { null };
            foreach (var item in state.AvailableItems(slot)) choices.Add(item);
            _options.Add(choices);
            _picked[i] = System.Math.Max(0, choices.IndexOf(state.Loadout[slot]));
        }
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
                // A Planned dungeon is listed, not enterable. Refusing here
                // rather than hiding it is what lets the selector show a world
                // without promising every door in it.
                return Dungeon.IsEnterable;
            default:
                return false;
        }
        Refresh();
        return false;
    }

    private void Cycle(int step)
    {
        if (_cursor == DungeonRow)
        {
            if (_dungeons.Count == 0) return;
            _dungeonIndex = (_dungeonIndex + _dungeons.Count + step) % _dungeons.Count;
            return;
        }
        if (_cursor == WorklingRow)
        {
            if (_creatures.Count == 0) return;
            _creatureIndex = (_creatureIndex + _creatures.Count + step) % _creatures.Count;
            // **Choosing the creature chooses the race**, because a creature
            // belongs to one — a Pangolin is Relicborn and there is no version
            // of it that is not. That is a real edit to the Workling and it is
            // written back when the run resolves: the energy colour, the
            // signature and which gear counts as attuned all move with it.
            // Deliberate rather than incidental; the design holds race, class
            // and name unlocked until onboarding, and this row is what
            // "unlocked" currently means.
            _state = _state.SelectingFamily(Creature.Family);
            return;
        }
        int index = _cursor - FirstSlotRow;
        var choices = _options[index];
        _picked[index] = (_picked[index] + choices.Count + step) % choices.Count;
        var slot = ItemSlotExtensions.AllCases[index];
        // Straight onto the state, so the sheet below is the real answer rather
        // than a preview that could disagree with what the fight later reads.
        _state = choices[_picked[index]] is Item item
            ? _state.Equipping(item, slot)
            : _state.ClearingSlot(slot);
    }

    private void Refresh()
    {
        var rates = ItemRates.Default;

        // The place drives the two lines at the top of the screen, so changing
        // the dungeon re-pitches the delve rather than leaving last place's
        // narration over this place's foes.
        var dungeon = Dungeon;
        _title.Text = dungeon.DisplayName;
        _briefing.Text = dungeon.Briefing;

        _rows[DungeonRow][0].Text = (_cursor == DungeonRow ? "▸ " : "  ") + "Dungeon";
        _rows[DungeonRow][1].Text = _dungeons.Count > 1
            ? $"◂ {dungeon.DisplayName} ▸"
            : dungeon.DisplayName;
        _rows[DungeonRow][2].Text = dungeon.IsEnterable
            ? $"{dungeon.EncounterCount} encounters  ·  {dungeon.Flavour}"
            : "not open yet";
        _rows[DungeonRow][0].AddThemeColorOverride(
            "font_color", _cursor == DungeonRow ? StageType.Ink : StageType.Muted);
        _rows[DungeonRow][1].AddThemeColorOverride(
            "font_color", dungeon.IsEnterable ? StageType.Ink : StageType.Faint);

        var creature = Creature;
        _rows[WorklingRow][0].Text = (_cursor == WorklingRow ? "▸ " : "  ") + "Workling";
        _rows[WorklingRow][1].Text = _creatures.Count > 1
            ? $"◂ {creature.DisplayName} ▸"
            : creature.DisplayName;
        _rows[WorklingRow][2].Text =
            $"{creature.Family.DisplayName()}  ·  {Describe(creature.Signature)}";
        _rows[WorklingRow][0].AddThemeColorOverride(
            "font_color", _cursor == WorklingRow ? StageType.Ink : StageType.Muted);
        _rows[WorklingRow][1].AddThemeColorOverride("font_color", creature.Energy);

        for (int i = 0; i < SlotCount; i++)
        {
            int row = FirstSlotRow + i;
            var slot = ItemSlotExtensions.AllCases[i];
            var chosen = _options[i][_picked[i]];
            _rows[row][0].Text = (row == _cursor ? "▸ " : "  ") + slot.DisplayName();
            _rows[row][1].Text = chosen?.DisplayName() ?? "—";
            _rows[row][2].Text = chosen is Item item
                ? $"+{rates.Modifier(item, _state.Family)} {item.Stat().DisplayName()}"
                  // The attunement rider is a real number the player is already
                  // being paid; marking it is what makes it discoverable.
                  + (rates.IsAttuned(item, _state.Family) ? "  ◈ attuned" : "")
                : slot.Fantasy();
            _rows[row][0].AddThemeColorOverride(
                "font_color", row == _cursor ? StageType.Ink : StageType.Muted);
        }

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
