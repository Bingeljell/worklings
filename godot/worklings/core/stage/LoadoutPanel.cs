using Godot;
using System;
using System.Collections.Generic;
using Worklings.Core.Combat;
using Worklings.Core.Host;
using Worklings.Core.Pet;
using Worklings.Core.Progression;
using Worklings.Core.Roster;

namespace Worklings.Core.Stage;

/// The prep screen: beat two of a delve, where the briefing pays off.
///
/// The design gives the briefing exactly one gameplay job — to tell the player
/// what kind of prep this delve rewards — so the narration and the choice live
/// on one screen rather than as two beats that scroll past each other. What the
/// player picks here is the place, the body and its gear — one item per slot,
/// from what they own — which feed straight into `Combatant.Pet`, folding gear
/// in ahead of condition. The starting Approach used to be a third choice on
/// this screen and was removed with the stances themselves: moves are chosen per
/// round now.
///
/// **The rig.** This screen was a text grid cycled with the arrow keys: it held
/// the right information and showed none of it. It is now the Workling standing
/// in the middle in live 3D with its three slots hung beside it — gear arranged
/// in the shape of a body wearing it. That is the whole argument for the shape:
/// this is the last thing you see before a delve, and it should feel like
/// sending something of yours into a hole in the ground rather than like filling
/// in a form. The bay is family-aware, so a Relicborn's prep screen and an
/// Elemental's are visibly different screens.
///
/// **Nothing here is drawn twice.** The plates, the equip menu and the stat
/// vocabulary are `GearPlate`, `SlotPicker` and `StatVocabulary`, shared with the
/// character screen — see `GearPlate` for why the split is there and not around
/// the whole panel. What is this screen's alone is the briefing, the creature
/// row and Descend; what is the character screen's alone is its tabs, its
/// identity pickers and condition-as-something-you-act-on. The two are never on
/// screen together: the pet and the dungeon share one window in two modes.
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
public sealed partial class LoadoutPanel
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

    /// The dungeon is drawn at 1920x1080 with `canvas_items` stretch, so one
    /// unit here is one unit of that space and a control built at the shared
    /// widgets' design size comes out a third of the height of the title beside
    /// it. Every shared part is asked for at this scale instead.
    private const float Scale = 1.6f;

    /// One choosable thing per line: the dungeon, the Workling, the three gear
    /// slots.
    ///
    /// **The dungeon is first because it is the question the other rows answer.**
    /// The briefing's one job is telling you what kind of prep this delve
    /// rewards, so which place you are going has to be settled before "what do I
    /// bring" means anything. It is also the reason this screen exists at all
    /// now: the paw menu says *Enter the Dungeon*, and this is where you say
    /// which.
    ///
    /// The Workling is next because it is the largest of the remaining choices —
    /// it decides the body, the energy colour, the signature and which gear is
    /// attuned, and the three plates are read against it.
    /// **The Approach row is gone.** It set a standing stance the fight then
    /// derived actions from; the player now picks a move every round, so a
    /// stance chosen at the briefing decides nothing. Leaving it would have been
    /// a control that did not control anything.
    private const int DungeonRow = 0;
    private const int WorklingRow = 1;
    private const int SlotCount = 3;
    private const int FirstSlotRow = 2;
    private const int RowCount = FirstSlotRow + SlotCount;

    private readonly CanvasLayer _layer;
    private readonly Label _title;
    private readonly Label _briefing;
    private readonly Label _readout;
    private readonly Label _condition;
    private readonly ChoiceRow _dungeonRow;
    private readonly ChoiceRow _creatureRow;
    private readonly GearPlate[] _plates = new GearPlate[SlotCount];
    private readonly List<StatChip> _chips = new();
    private readonly Button _descend;
    private readonly ModelBay _bay;

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

    /// Raised when the player commits — by the button or by the key. The scene
    /// owns what descending means; this screen only knows that it is over.
    public event Action? Confirmed;

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

        // **Solid, because there is nothing behind it.** This used to be a wash
        // over the stage, on the reasoning that prep is a pause in the dungeon
        // rather than a trip to a menu. That reasoning is gone: the scene does
        // not build a dungeon until the player descends, so prep happens *before*
        // a room exists and a wash would be a wash over the clear colour.
        var scrim = new ColorRect
        {
            Color = new Color(0.035f, 0.032f, 0.028f, 1),
            AnchorRight = 1, AnchorBottom = 1,
        };
        root.AddChild(scrim);

        var card = new PanelContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            CustomMinimumSize = new Vector2(1180, 0),
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
        };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.055f, 0.05f, 0.96f),
            BorderColor = WorklingsTheme.Brass,
            ContentMarginLeft = 48, ContentMarginRight = 48,
            ContentMarginTop = 34, ContentMarginBottom = 34,
        };
        style.SetBorderWidthAll(2);
        style.SetCornerRadiusAll(3);
        card.AddThemeStyleboxOverride("panel", style);
        root.AddChild(card);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 16);
        card.AddChild(column);

        // MARK: the briefing

        var brief = new HBoxContainer();
        brief.AddThemeConstantOverride("separation", 15);
        brief.AddChild(new ColorRect
        {
            Color = WorklingsTheme.Relic,
            CustomMinimumSize = new Vector2(3, 0),
        });
        var words = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        words.AddThemeConstantOverride("separation", 4);
        _title = StageType.Label("", 40, StageType.Ink, bold: true);
        words.AddChild(_title);
        _briefing = StageType.Label("", 21, StageType.Muted);
        _briefing.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _briefing.CustomMinimumSize = new Vector2(820, 0);
        words.AddChild(_briefing);
        brief.AddChild(words);
        column.AddChild(brief);

        // MARK: what you are going into, and who in

        _dungeonRow = new ChoiceRow("Dungeon", () => Cycle(DungeonRow, -1), () => Cycle(DungeonRow, 1));
        column.AddChild(_dungeonRow);
        _creatureRow = new ChoiceRow("Workling", () => Cycle(WorklingRow, -1), () => Cycle(WorklingRow, 1));
        column.AddChild(_creatureRow);

        // MARK: the rig

        var rig = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        rig.AddThemeConstantOverride("separation", 22);
        column.AddChild(rig);

        // Tool and Ward to the left, Charm to the right, both columns centred on
        // the body. Kept tight to it deliberately: three slots is a thin rig, and
        // plates orbiting at a distance read as a body with things stuck to it
        // rather than a creature wearing them.
        var left = PlateColumn();
        var right = PlateColumn();
        rig.AddChild(left);

        var bayFrame = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var bayStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.037f, 0.032f, 1),
            BorderColor = WorklingsTheme.Brass with { A = 0.55f },
        };
        bayStyle.SetBorderWidthAll(1);
        bayStyle.SetContentMarginAll(1);
        bayFrame.AddThemeStyleboxOverride("panel", bayStyle);
        // One shade off this card, the same rule the character window's bay
        // follows — and warmer than its colour, because this card is.
        _bay = new ModelBay(380, 1f, new Color(0.085f, 0.078f, 0.068f));
        _bay.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        bayFrame.AddChild(_bay);
        rig.AddChild(bayFrame);
        rig.AddChild(right);

        for (int i = 0; i < SlotCount; i++)
        {
            var slot = ItemSlotExtensions.AllCases[i];
            var plate = new GearPlate(slot, GearPlate.Look.Rig(Scale))
            {
                // Nothing on this screen takes keyboard focus. The cursor is
                // ours and the scene reads the keys; a focused Button would eat
                // the arrows for its own focus navigation and the rig would go
                // dead the first time a plate was clicked.
                FocusMode = Control.FocusModeEnum.None,
            };
            int index = i;
            plate.Pressed += () => OpenPicker(index);
            _plates[i] = plate;
            (i == SlotCount - 1 ? right : left).AddChild(plate);
        }

        // MARK: what it all adds up to

        column.AddChild(Rule());

        // The stat strip is the reason any of this matters: gear moves these
        // numbers, and the fight reads exactly them.
        var chips = new HBoxContainer();
        chips.AddThemeConstantOverride("separation", 10);
        column.AddChild(chips);
        foreach (var stat in PetStatKindExtensions.AllCases)
        {
            var chip = new StatChip(stat);
            chips.AddChild(chip);
            _chips.Add(chip);
        }

        _readout = StageType.Label("", 21, StageType.Muted);
        column.AddChild(_readout);
        _condition = StageType.Label("", 21, WorklingsTheme.Warning);
        column.AddChild(_condition);

        column.AddChild(Rule());

        // MARK: the commitment

        var foot = new HBoxContainer();
        foot.AddThemeConstantOverride("separation", 20);
        var hint = StageType.Label(
            "↑↓ ←→ choose   ·   click a slot to see everything you own   ·   [enter] descend",
            18, StageType.Faint);
        hint.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        foot.AddChild(hint);

        // A commitment, not a close button. It is why this screen is a gate and
        // the character screen is a window.
        _descend = new Button
        {
            Text = "descend",
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(190, 52),
        };
        _descend.AddThemeFontOverride("font", StageType.Bold);
        _descend.AddThemeFontSizeOverride("font_size", 26);
        _descend.Pressed += Confirm;
        foot.AddChild(_descend);
        column.AddChild(foot);
    }

    private static VBoxContainer PlateColumn()
    {
        var column = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        column.AddThemeConstantOverride("separation", 14);
        column.CustomMinimumSize = new Vector2(300, 0);
        return column;
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

        ReadOptions();
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
                Cycle(_cursor, -1);
                return false;
            case Key.Right:
                Cycle(_cursor, 1);
                return false;
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

    /// The mouse's way in, and the bench's best idea: every alternative for one
    /// slot, opened at the plate rather than in a menu somewhere else. Cycling
    /// with ←→ stays for the keyboard; this is for the player who wants to see
    /// what the choice costs before making it.
    private void OpenPicker(int index)
    {
        _cursor = FirstSlotRow + index;
        Refresh();
        SlotPicker.Open(
            _layer, _plates[index], ItemSlotExtensions.AllCases[index], _state, Scale,
            next =>
            {
                _state = next;
                ReadOptions();
                Refresh();
            });
    }

    /// Rebuilds the per-slot option lists and points each cursor at what is
    /// actually worn, so the keyboard and the picker cannot disagree about what
    /// is equipped.
    private void ReadOptions()
    {
        _options.Clear();
        for (int i = 0; i < SlotCount; i++)
        {
            var slot = ItemSlotExtensions.AllCases[i];
            var choices = new List<Item?> { null };
            foreach (var item in _state.AvailableItems(slot)) choices.Add(item);
            _options.Add(choices);
            _picked[i] = Math.Max(0, choices.IndexOf(_state.Loadout[slot]));
        }
    }

    private void Confirm()
    {
        if (!Dungeon.IsEnterable) return;
        Confirmed?.Invoke();
    }

    private void Cycle(int row, int step)
    {
        if (row == DungeonRow)
        {
            if (_dungeons.Count == 0) return;
            _dungeonIndex = (_dungeonIndex + _dungeons.Count + step) % _dungeons.Count;
        }
        else if (row == WorklingRow)
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
        }
        else
        {
            int index = row - FirstSlotRow;
            if (index < 0 || index >= SlotCount) return;
            var choices = _options[index];
            _picked[index] = (_picked[index] + choices.Count + step) % choices.Count;
            var slot = ItemSlotExtensions.AllCases[index];
            // Straight onto the state, so the sheet below is the real answer
            // rather than a preview that could disagree with what the fight
            // later reads.
            _state = choices[_picked[index]] is Item item
                ? _state.Equipping(item, slot)
                : _state.ClearingSlot(slot);
        }
        _cursor = row;
        Refresh();
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

        _dungeonRow.Set(
            dungeon.DisplayName,
            dungeon.IsEnterable
                ? $"{dungeon.EncounterCount} encounters  ·  {dungeon.Flavour}"
                : "not open yet",
            dungeon.IsEnterable ? StageType.Ink : StageType.Faint,
            _cursor == DungeonRow,
            _dungeons.Count > 1);

        var creature = Creature;
        _creatureRow.Set(
            creature.DisplayName,
            $"{creature.Family.DisplayName()}  ·  {Describe(creature.Signature)}",
            creature.Energy,
            _cursor == WorklingRow,
            _creatures.Count > 1);

        // The bay resolves the race through the roster, so the body standing in
        // the middle is whoever the creature row says you are.
        _bay.Wear(_state.Family);

        for (int i = 0; i < SlotCount; i++)
        {
            var chosen = _options[i][_picked[i]];
            _plates[i].Set(
                chosen,
                chosen is Item item ? rates.Modifier(item, _state.Family) : 0,
                chosen is Item worn && rates.IsAttuned(worn, _state.Family));
            _plates[i].Selected = _cursor == FirstSlotRow + i;
        }

        var sheet = CharacterSheet.Make(_state);
        for (int i = 0; i < sheet.Rows.Count && i < _chips.Count; i++)
        {
            _chips[i].Set(sheet.Rows[i]);
        }

        _readout.Text = StatVocabulary.Fight(sheet.Combat);
        _condition.Text = StatVocabulary.Condition(sheet.Combat);
        _condition.Visible = _condition.Text.Length > 0;

        _descend.Disabled = !dungeon.IsEnterable;
        _descend.AddThemeStyleboxOverride("normal", DescendStyle(dungeon.IsEnterable, false));
        _descend.AddThemeStyleboxOverride("hover", DescendStyle(dungeon.IsEnterable, true));
        _descend.AddThemeStyleboxOverride("pressed", DescendStyle(dungeon.IsEnterable, true));
        _descend.AddThemeStyleboxOverride("disabled", DescendStyle(false, false));
        _descend.AddThemeColorOverride(
            "font_color", dungeon.IsEnterable ? new Color(0.06f, 0.055f, 0.05f) : StageType.Faint);
        _descend.AddThemeColorOverride(
            "font_hover_color", dungeon.IsEnterable ? new Color(0.06f, 0.055f, 0.05f) : StageType.Faint);
        _descend.AddThemeColorOverride("font_disabled_color", StageType.Faint);
    }

    /// Filled amber when the door is open, an outline when it is not — a refused
    /// dungeon should look like a door you cannot take yet, not like a button
    /// that failed to draw.
    private static StyleBoxFlat DescendStyle(bool open, bool lit)
    {
        var box = new StyleBoxFlat
        {
            BgColor = open
                ? (lit ? Colors.White.Lerp(WorklingsTheme.Relic, 0.75f) : WorklingsTheme.Relic)
                : new Color(0, 0, 0, 0),
            BorderColor = open ? WorklingsTheme.Relic : WorklingsTheme.Brass with { A = 0.6f },
        };
        box.SetBorderWidthAll(open ? 0 : 1);
        box.SetCornerRadiusAll(2);
        return box;
    }

    /// One line the player steps through with ←→: what it is, what is chosen,
    /// and what that means. The dungeon and the Workling both read this way, and
    /// neither is gear — the plates say the rest.
    private sealed partial class ChoiceRow : PanelContainer
    {
        private readonly Label _label;
        private readonly Label _value;
        private readonly Label _detail;
        private readonly Button _back;
        private readonly Button _forward;

        public ChoiceRow(string name, Action back, Action forward)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 12);
            AddChild(row);

            _label = StageType.Label(name, 21, StageType.Muted);
            _label.CustomMinimumSize = new Vector2(130, 0);
            row.AddChild(_label);

            _back = Arrow("◂", back);
            row.AddChild(_back);

            _value = StageType.Label("", 24, StageType.Ink, bold: true);
            _value.CustomMinimumSize = new Vector2(300, 0);
            _value.HorizontalAlignment = HorizontalAlignment.Center;
            row.AddChild(_value);

            _forward = Arrow("▸", forward);
            row.AddChild(_forward);

            _detail = StageType.Label("", 20, StageType.Faint);
            _detail.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _detail.ClipText = true;
            _detail.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
            row.AddChild(_detail);
        }

        public void Set(string value, string detail, Color tint, bool selected, bool cyclable)
        {
            _value.Text = value;
            _value.AddThemeColorOverride("font_color", tint);
            _detail.Text = detail;
            _label.AddThemeColorOverride(
                "font_color", selected ? StageType.Ink : StageType.Muted);
            // A single dungeon is not a choice, and drawing arrows around it
            // would promise one.
            _back.Visible = _forward.Visible = cyclable;

            var style = new StyleBoxFlat
            {
                BgColor = selected ? new Color(0.09f, 0.075f, 0.052f, 1) : new Color(0, 0, 0, 0.22f),
                BorderColor = selected ? WorklingsTheme.Relic : WorklingsTheme.Brass with { A = 0.4f },
            };
            style.SetBorderWidthAll(1);
            style.SetCornerRadiusAll(2);
            style.ContentMarginLeft = style.ContentMarginRight = 14;
            style.ContentMarginTop = style.ContentMarginBottom = 8;
            AddThemeStyleboxOverride("panel", style);
        }

        private static Button Arrow(string glyph, Action pressed)
        {
            var button = new Button
            {
                Text = glyph,
                Flat = true,
                FocusMode = FocusModeEnum.None,
                CustomMinimumSize = new Vector2(34, 0),
            };
            button.AddThemeFontOverride("font", StageType.Bold);
            button.AddThemeFontSizeOverride("font_size", 24);
            button.AddThemeColorOverride("font_color", WorklingsTheme.Relic);
            button.AddThemeColorOverride("font_hover_color", Colors.White);
            button.Pressed += pressed;
            return button;
        }
    }

    /// One stat in its own box: the base number, and what gear added beside it in
    /// the blue that means "this came from somewhere else". Both halves are
    /// `StatVocabulary`'s words, which is what stops this strip and the character
    /// screen's ledger disagreeing about the same Workling.
    private sealed partial class StatChip : PanelContainer
    {
        private readonly Label _value;
        private readonly Label _gear;

        public StatChip(PetStatKind stat)
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var style = new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.035f) };
            style.SetCornerRadiusAll(2);
            style.ContentMarginLeft = style.ContentMarginRight = 12;
            style.ContentMarginTop = style.ContentMarginBottom = 8;
            AddThemeStyleboxOverride("panel", style);

            var column = new VBoxContainer();
            column.AddThemeConstantOverride("separation", 0);
            AddChild(column);

            var caption = StageType.Label(stat.DisplayName().ToUpperInvariant(), 15, StageType.Faint);
            column.AddChild(caption);

            var numbers = new HBoxContainer();
            numbers.AddThemeConstantOverride("separation", 7);
            _value = StageType.Label("", 28, StageType.Ink, bold: true);
            numbers.AddChild(_value);
            _gear = StageType.Label("", 20, WorklingsTheme.GearBlue, bold: true);
            _gear.VerticalAlignment = VerticalAlignment.Bottom;
            numbers.AddChild(_gear);
            column.AddChild(numbers);
        }

        public void Set(CharacterSheet.StatRow row)
        {
            _value.Text = StatVocabulary.Basis(row);
            _gear.Text = StatVocabulary.Gear(row);
        }
    }
}
