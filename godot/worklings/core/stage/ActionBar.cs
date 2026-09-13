using Godot;
using System.Collections.Generic;
using Worklings.Core.Combat;

namespace Worklings.Core.Stage;

/// What one command slot is currently worth to the player.
public enum CommandState
{
    /// The command exists but cannot be taken right now.
    Dim,
    /// A decision is open and this is one of the answers.
    Live,
    /// The move the player took last round, marked so the row has continuity
    /// between decisions.
    Held,
    /// Used up for this encounter.
    Spent,
}

/// The command bar: the player's four options, on screen, under their Workling,
/// all the time.
///
/// **This replaces a nineteen-pixel line of faint text in the bottom-right
/// corner**, which is where every prompt the game gave the player used to live —
/// including the once-per-fight Signature, which the first play session never
/// managed to throw because nothing on screen said it was available. A prompt
/// that only appears at the moment it is answerable, in the least-looked-at
/// corner of the frame, in the smallest type on it, is a prompt the player finds
/// by accident or not at all.
///
/// So the bar is **persistent, not modal**. The four commands are always drawn
/// in the same four places, and the state moves through them: dim between
/// decisions, lit when the fight asks, with the held stance marked throughout.
/// That is how an action bar works in every game that has one — the point is
/// that the player learns where their options live while nothing is happening,
/// so that when something does happen they are reading a state change rather
/// than discovering a control.
///
/// Bottom-centre because that is where a player's own options belong: the
/// creature stands above it, and the eye travels down from the fight to the
/// commands and back without crossing the frame. Keycaps are drawn into each
/// slot rather than listed in a legend, and the slots are real buttons as well,
/// because a command you can only reach by remembering a letter is half a
/// control.
public sealed class ActionBar
{
    /// The bar's own geometry. Slots are wide enough to hold the longest label
    /// ("AGGRESSIVE") at the size the label wants to be read at.
    private const float SlotWidth = 158f;
    private const float SlotHeight = 66f;
    private const float Gap = 10f;
    private const float BottomMargin = 34f;

    private sealed class Slot
    {
        public required Button Button;
        public required PanelContainer Frame;
        public required Label Cap;
        public required Label Title;
        public required Label Note;
        public CommandState State = CommandState.Dim;
    }

    private readonly HBoxContainer _row;
    private readonly List<Slot> _slots = new();

    /// Strike and Brace, then the Signature, then the two press-your-luck
    /// commands. The fight three and the choice two never show together, so
    /// they share the row and the bar hides whichever set is not in play.
    ///
    /// **These are verbs now, not stances.** The row used to hold Aggressive /
    /// Careful / Clever, which set a standing strategy that then decided the
    /// action for you — so the thing you pressed and the thing your Workling did
    /// were not the same thing, and Careful in particular could answer a press
    /// with a strike. What is on the bar is now exactly what happens.
    private const int VerbCount = 2;
    private const int UnleashSlot = 2;
    private const int PushSlot = 3;
    private const int BankSlot = 4;

    private static readonly CombatAction[] Verbs =
        { CombatAction.Strike, CombatAction.Brace };

    /// What each move does, in the half-line a slot has room for.
    private static string Note(CombatAction action) => action switch
    {
        CombatAction.Brace => "halve the blow",
        _ => "attack",
    };

    public event System.Action<CombatAction>? Chose;
    public event System.Action? Unleashed;
    public event System.Action? Pushed;
    public event System.Action? Banked;

    public ActionBar(Control root)
    {
        _row = new HBoxContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 1, AnchorBottom = 1,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Begin,
        };
        _row.AddThemeConstantOverride("separation", (int)Gap);
        _row.Position = new Vector2(0, -BottomMargin - SlotHeight);
        root.AddChild(_row);

        for (int i = 0; i < VerbCount; i++)
        {
            var verb = Verbs[i];
            Add($"{i + 1}", verb.ToString().ToUpperInvariant(), Note(verb),
                () => Chose?.Invoke(verb));
        }
        Add("3", "UNLEASH", "signature", () => Unleashed?.Invoke());
        Add("SPACE", "PUSH DEEPER", "keep what you carry", () => Pushed?.Invoke(), wide: true);
        Add("B", "BANK & LEAVE", "walk out with the spoils", () => Banked?.Invoke(), wide: true);
    }

    private void Add(string cap, string title, string note, System.Action pressed, bool wide = false)
    {
        var frame = new PanelContainer
        {
            CustomMinimumSize = new Vector2(wide ? SlotWidth * 1.55f : SlotWidth, SlotHeight),
        };

        // The column is a **direct** child of the panel, so the panel takes its
        // minimum size from the widest label in it and the slot grows to fit its
        // own text. It used to hang off an intermediate bare `Control`, which has
        // no minimum size of its own — so the panel only ever knew about
        // `CustomMinimumSize` and any label longer than that drew straight out
        // through the sides of the box. `CustomMinimumSize` is now a floor that
        // keeps the row even, not a ceiling that text escapes.
        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 1);
        column.Alignment = BoxContainer.AlignmentMode.Center;
        frame.AddChild(column);

        // The keycap first and smallest: it is the thing the player needs once,
        // and the label is the thing they read every round after that.
        var capLabel = StageType.Label(cap, 15, StageType.Faint, bold: true);
        capLabel.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(capLabel);

        var titleLabel = StageType.Label(title, 21, StageType.Ink, bold: true);
        titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(titleLabel);

        var noteLabel = StageType.Label(note, 14, StageType.Faint);
        noteLabel.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(noteLabel);

        // A transparent button over the whole slot, so the mouse works without
        // the button's own theme fighting the frame's. A second child of the
        // PanelContainer gets the same rect as the first and contributes nothing
        // to the minimum size, which is exactly what is wanted here.
        var button = new Button { Flat = true, Text = "" };
        button.Pressed += () => pressed();
        frame.AddChild(button);

        _row.AddChild(frame);
        var slot = new Slot
        {
            Button = button, Frame = frame, Cap = capLabel, Title = titleLabel, Note = noteLabel,
        };
        _slots.Add(slot);
        Paint(slot, CommandState.Dim, StageType.Ink);
    }

    /// The three fight commands, with last round's move marked and the
    /// Signature showing whether it is still there.
    ///
    /// `live` is whether the fight is actually asking right now. The bar is
    /// drawn either way — dim is a state, not an absence — so the player learns
    /// where their moves live while the round is resolving and reads a state
    /// change rather than discovering a control when it opens.
    public void ShowFight(CombatAction? last, bool signatureReady, bool live, Color energy)
    {
        SetVisible(0, VerbCount + 1);
        for (int i = 0; i < VerbCount; i++)
        {
            var state = live ? CommandState.Live
                      : Verbs[i] == last ? CommandState.Held
                      : CommandState.Dim;
            Paint(_slots[i], state, energy);
        }
        Paint(_slots[UnleashSlot],
              !signatureReady ? CommandState.Spent : live ? CommandState.Live : CommandState.Dim,
              energy);
        _slots[UnleashSlot].Note.Text = signatureReady ? "once per fight" : "spent";
    }

    /// The bank-or-push beat. Two commands, both always live — this is the one
    /// moment of the delve that is nothing but a choice.
    public void ShowChoice(Color energy)
    {
        SetVisible(PushSlot, 2);
        Paint(_slots[PushSlot], CommandState.Live, energy);
        Paint(_slots[BankSlot], CommandState.Live, energy);
    }

    /// Takes the bar off screen — the prep card and the closing summary own the
    /// whole frame and carry their own instructions.
    public void Hide() => SetVisible(0, 0);

    private void SetVisible(int from, int count)
    {
        for (int i = 0; i < _slots.Count; i++)
        {
            bool on = i >= from && i < from + count;
            _slots[i].Frame.Visible = on;
            _slots[i].Button.Disabled = !on;
        }
        _row.Visible = count > 0;
    }

    /// One state, one look. Held reads as the Workling's own colour because the
    /// stance belongs to the creature; live reads as bright and bordered because
    /// it is a thing to press; dim keeps the slot's shape so the row never moves
    /// under the player's eye.
    private static void Paint(Slot slot, CommandState state, Color energy)
    {
        slot.State = state;
        var style = new StyleBoxFlat
        {
            ContentMarginLeft = 10, ContentMarginRight = 10,
            ContentMarginTop = 6, ContentMarginBottom = 6,
        };
        style.SetCornerRadiusAll(3);
        style.SetBorderWidthAll(2);

        switch (state)
        {
            case CommandState.Held:
                style.BgColor = new Color(energy.R, energy.G, energy.B, 0.22f);
                style.BorderColor = new Color(energy.R, energy.G, energy.B, 0.95f);
                Tint(slot, StageType.Ink, StageType.Ink, StageType.Muted);
                break;
            case CommandState.Live:
                style.BgColor = new Color(0.07f, 0.065f, 0.06f, 0.92f);
                style.BorderColor = new Color(0.78f, 0.70f, 0.56f, 0.92f);
                Tint(slot, StageType.Ink, StageType.Ink, StageType.Muted);
                break;
            case CommandState.Spent:
                style.BgColor = new Color(0.05f, 0.05f, 0.05f, 0.72f);
                style.BorderColor = new Color(0.30f, 0.27f, 0.22f, 0.55f);
                Tint(slot, StageType.Faint, StageType.Faint, StageType.Faint);
                break;
            default:
                style.BgColor = new Color(0.05f, 0.05f, 0.05f, 0.82f);
                style.BorderColor = new Color(0.36f, 0.32f, 0.26f, 0.70f);
                Tint(slot, StageType.Muted, StageType.Muted, StageType.Faint);
                break;
        }
        slot.Frame.AddThemeStyleboxOverride("panel", style);
    }

    private static void Tint(Slot slot, Color cap, Color title, Color note)
    {
        slot.Cap.AddThemeColorOverride("font_color", cap);
        slot.Title.AddThemeColorOverride("font_color", title);
        slot.Note.AddThemeColorOverride("font_color", note);
    }
}
