using Godot;
using Worklings.Core.Combat;

namespace Worklings.Core.Stage;

/// The dungeon's combat HUD.
///
/// **Rebuilt on the first play session's feedback.** The previous version put
/// everything the player had to read into three small labels in the corners —
/// the narration bottom-left at 24px, the round and Approach bottom-right at
/// 19px in the faintest ink on the palette, and every prompt the game ever gave
/// the player in that same bottom-right line. The result was a screen that was
/// almost entirely arena, with the game's whole conversation with the player
/// happening in the margins of it, and a Signature that was never once thrown
/// because nothing ever told anyone it was ready.
///
/// The new frame borrows the arrangement that action games have converged on —
/// Path of Exile 2 and Ravenswatch both do this, and so does nearly everything
/// else in the genre — because it is a solved problem and the solution is about
/// where the eye already is:
///
///   * **Combatant state at the top corners**, outside-in, so the two bars
///     mirror each other and drain toward the middle.
///   * **Run state top-centre** — which encounter, which round — because it is
///     reference material, read between fights rather than during one.
///   * **The beat clock dead centre**, in the gap between the two creatures,
///     because the pause between actions is the part of a turn-based fight the
///     player is actually waiting on. It says who is about to act, not just how
///     long until something does.
///   * **Narration and commands bottom-centre**, stacked, directly under the
///     player's own Workling. What just happened and what you can do about it
///     are one column, not two opposite corners.
///
/// Everything a player must read is now within about a third of the frame's
/// width of its vertical centre line. That is the whole design.
///
/// Built in code rather than as a .tscn so the whole layout is readable in one
/// place while it is still being tuned.
public sealed class CombatHud
{
    private const int Margin = 40;

    private readonly HealthPlate _pet;
    private readonly HealthPlate _foe;
    private readonly Label _run;
    private readonly HBoxContainer _pips;
    private readonly Label _narration;
    private readonly PanelContainer _narrationFrame;
    private readonly Label _clockNumber;
    private readonly Label _clockWho;
    private readonly Control _clock;

    public ActionBar Bar { get; }

    /// The HUD's own full-screen layer, for the overlays that have to track a
    /// creature in 3D — currently the foe's intent badge.
    public Control Root { get; }

    private Color _petEnergy;

    public CombatHud(Node parent, string petName, int petMax, Color petEnergy,
                     string foeName, int foeMax, Color foeEnergy)
    {
        _petEnergy = petEnergy;

        var layer = new CanvasLayer();
        parent.AddChild(layer);

        // The root ignores the mouse so the arena keeps it; only the command
        // bar's own buttons take it back.
        var root = new Control { AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.AddChild(root);

        _pet = new HealthPlate(petName, petMax, petEnergy, mirrored: false);
        _pet.Root.Position = new Vector2(Margin, Margin);
        root.AddChild(_pet.Root);

        _foe = new HealthPlate(foeName, foeMax, foeEnergy, mirrored: true);
        _foe.Root.AnchorLeft = 1; _foe.Root.AnchorRight = 1;
        _foe.Root.Position = new Vector2(-Margin - HealthPlate.Width, Margin);
        root.AddChild(_foe.Root);

        // MARK: - Run state, top centre

        var runBox = new VBoxContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f,
            GrowHorizontal = Control.GrowDirection.Both,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        runBox.AddThemeConstantOverride("separation", 6);
        runBox.Alignment = BoxContainer.AlignmentMode.Center;
        runBox.Position = new Vector2(0, Margin + 2);
        root.AddChild(runBox);

        _run = StageType.Label("", 19, StageType.Muted, bold: true);
        _run.HorizontalAlignment = HorizontalAlignment.Center;
        runBox.AddChild(_run);

        // Four pips for four encounters: the shape of the delve, which the old
        // "Encounter 1/4" text carried as arithmetic the player had to do.
        _pips = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _pips.AddThemeConstantOverride("separation", 7);
        runBox.AddChild(_pips);

        // MARK: - The beat clock, centre

        _clock = new Control
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Both,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        root.AddChild(_clock);

        var clockColumn = new VBoxContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f,
            GrowHorizontal = Control.GrowDirection.Both,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        clockColumn.AddThemeConstantOverride("separation", 0);
        clockColumn.Alignment = BoxContainer.AlignmentMode.Center;
        // Above the creatures rather than between them. Dead centre put a
        // 90-pixel numeral across the Workling's face, which is a number the
        // player reads *instead of* the fight; this band is empty stage in every
        // frame of the reviewed capture, and the clock still sits on the frame's
        // vertical centre line where the eye rests between actions.
        clockColumn.Position = new Vector2(0, -284);
        _clock.AddChild(clockColumn);

        _clockNumber = StageType.Label("", 116, StageType.Ink, bold: true, outline: 14);
        _clockNumber.HorizontalAlignment = HorizontalAlignment.Center;
        clockColumn.AddChild(_clockNumber);

        _clockWho = StageType.Label("", 22, StageType.Ink, bold: true);
        _clockWho.HorizontalAlignment = HorizontalAlignment.Center;
        clockColumn.AddChild(_clockWho);

        // MARK: - Narration and commands, bottom centre

        // Centred by a row rather than by anchors on the plaque itself: a
        // PanelContainer sizes to its content, and a node whose opposite anchors
        // disagree has its size overridden after _ready, so the plaque drew at
        // whatever width it was left with and its background never appeared.
        var narrationRow = new HBoxContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 1, AnchorBottom = 1,
            GrowHorizontal = Control.GrowDirection.Both,
            GrowVertical = Control.GrowDirection.Begin,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        narrationRow.Alignment = BoxContainer.AlignmentMode.Center;
        // Clear of the command bar, which stands 34 up and 66 tall.
        narrationRow.Position = new Vector2(0, -178);
        root.AddChild(narrationRow);

        _narrationFrame = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        var frameStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.04f, 0.04f, 0.78f),
            ContentMarginLeft = 22, ContentMarginRight = 22,
            ContentMarginTop = 6, ContentMarginBottom = 8,
        };
        frameStyle.SetCornerRadiusAll(3);
        _narrationFrame.AddThemeStyleboxOverride("panel", frameStyle);
        narrationRow.AddChild(_narrationFrame);

        _narration = StageType.Label("", 30, StageType.Ink, bold: true);
        _narration.HorizontalAlignment = HorizontalAlignment.Center;
        _narrationFrame.AddChild(_narration);

        Bar = new ActionBar(root);
        Root = root;
    }

    /// Points the foe plate at a new opponent — a delve runs four of them
    /// through the same HUD.
    public void SetFoe(string name, int maxHP, Color energy)
    {
        _foe.SetIdentity(name, energy);
        _foe.Reset(maxHP);
    }

    /// Points the player's plate at whichever Workling walked in, by name and
    /// by colour.
    ///
    /// **The name has to be re-set, not just read once at construction.** The
    /// HUD is built on the first run and kept for every run after it, and a
    /// Workling's name is a thing the player changes — the plate would otherwise
    /// keep showing whatever it was called the first time the dungeon opened.
    /// The colour matters in three places (this plate, the encounter pips and
    /// the countdown numeral), so it is pushed once here rather than guessed at
    /// each of them.
    public void SetPet(string name, Color energy)
    {
        _petEnergy = energy;
        _pet.SetIdentity(name, energy);
    }

    public void SetHP(int pet, int foe) { _pet.Set(pet); _foe.Set(foe); }
    public void Reset(int petMax, int foeMax) { _pet.Reset(petMax); _foe.Reset(foeMax); }

    /// The line under the fight. Empty hides the whole plaque rather than
    /// leaving an empty box on screen.
    public void SetNarration(string line)
    {
        _narration.Text = line;
        _narrationFrame.Visible = line.Length > 0;
    }

    /// Where the run is: which encounter of the chain, and which round of it.
    public void SetRun(int encounter, int total, int round)
    {
        _run.Text = round > 0 ? $"ROUND {round}" : "";
        Pips(encounter, total);
    }

    /// A free line where the run state goes — used by the beats that are not a
    /// fight, which have a sentence rather than a round number.
    public void SetRunLine(string line)
    {
        _run.Text = line.ToUpperInvariant();
        Pips(0, 0);
    }

    /// Grows or shrinks the pip row to `total`, then colours it.
    ///
    /// **`RemoveChild` before `QueueFree`, and that is not a style preference.**
    /// `QueueFree` is deferred to the end of the frame, so a node freed this way
    /// is still a child right now and `GetChildCount()` does not move — which
    /// turned the shrink loop into an infinite one. It span only when the row had
    /// to get *smaller*, and the one place that happens is `SetRunLine`'s
    /// `Pips(0, 0)` on the summary screen, so the game locked up at the end of
    /// every delve and nowhere else. It presented as a crash; it was this.
    private void Pips(int encounter, int total)
    {
        while (_pips.GetChildCount() > total)
        {
            var last = _pips.GetChild(_pips.GetChildCount() - 1);
            _pips.RemoveChild(last);
            last.QueueFree();
        }
        while (_pips.GetChildCount() < total)
        {
            _pips.AddChild(new ColorRect { CustomMinimumSize = new Vector2(26, 4) });
        }
        for (int i = 0; i < _pips.GetChildCount(); i++)
        {
            if (_pips.GetChild(i) is not ColorRect pip) continue;
            // Cleared, current, still to come. The current one is the Workling's
            // own colour, so the top of the frame and the bottom of it agree
            // about who the player is.
            pip.Color = i < encounter - 1 ? new Color(0.55f, 0.49f, 0.39f, 0.85f)
                      : i == encounter - 1 ? _petEnergy
                      : new Color(1, 1, 1, 0.14f);
        }
    }

    /// The countdown to the next action, centre frame.
    ///
    /// **It names who is about to act**, which the draining bar it replaces did
    /// not. The first session read the fight as losing turns — "sometimes I
    /// don't attack and the Snag attacks twice" — and the fight was in fact
    /// doing exactly what the rules say: a Careful Workling latches into Brace
    /// while it is hurt, and a Snare on its Agility flips the initiative so the
    /// foe moves last in one round and first in the next. Both are legal, both
    /// are invisible, and a countdown that says only "1.7s" cannot tell them
    /// apart from a dropped turn. Saying whose beat is coming turns a suspected
    /// bug into a readable rule.
    ///
    /// `remaining` is the seconds still to run. Whole seconds only: this is a
    /// 3-2-1, and a tenths place makes it a progress bar with digits.
    public void SetBeat(double remaining, string who, string what, bool isPet)
    {
        int count = (int)Mathf.Ceil(remaining);
        if (count <= 0) { ClearBeat(); return; }
        _clock.Visible = true;
        _clockNumber.Text = count.ToString();
        _clockNumber.AddThemeColorOverride("font_color", isPet ? _petEnergy : StageType.Ink);
        // An empty name means `what` is the whole line — there is no actor,
        // because the round is turning or the fight has just ended.
        _clockWho.Text = who.Length > 0
            ? $"{who.ToUpperInvariant()} {what.ToUpperInvariant()}"
            : what.ToUpperInvariant();
    }

    /// Hides the countdown while an action is actually playing — a clock that
    /// runs through the attack implies the attack is the wait, when it is the
    /// thing being waited for.
    public void ClearBeat() => _clock.Visible = false;

    public void Tick(double delta) { _pet.Tick(delta); _foe.Tick(delta); }
}
