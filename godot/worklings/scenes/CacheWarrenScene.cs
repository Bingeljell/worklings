using Godot;
using System.Collections.Generic;
using Worklings.Core.Combat;
using Worklings.Core.Host;
using Worklings.Core.Pet;
using Worklings.Core.Progression;
using Worklings.Core.Roster;
using Worklings.Core.Stage;

/// The Cache Warren scene: the first dungeon, running a real delve.
///
/// Named ...Scene rather than CacheWarren because Godot requires script classes
/// in the global namespace, where it would shadow the bestiary's
/// Worklings.Core.Combat.CacheWarren for every file importing that namespace.
///
/// The rules are resolved by the ported core and nothing here knows them: a
/// PetState carries the pet, Combatant.Pet folds gear and condition into a
/// fighter, Delve chains the four encounters and holds the press-your-luck
/// choice, and CombatEncounter emits the stream of CombatEvents this script
/// turns into animation and text. That seam is deliberate — the rules stay
/// verifiable headlessly (see tools/*_probe), and the renderer stays swappable.
///
/// An encounter is stepped, not pre-resolved. It used to run to completion at
/// the top and replay its log, which was true to the rules and quietly removed
/// the player from the fight: CombatEncounter pauses at decision points for a
/// re-chosen Approach and the Unleash, and a fight already resolved has nothing
/// left to decide. So the scene advances a round at a time, animates the events
/// that round produced, and hands the pause back to the player when the
/// encounter asks for one. Determinism is unchanged — the same seed and the same
/// decisions replay identically; the decisions are simply now an input.
///
/// The pet is loaded from disk on open and written back when a run resolves, so
/// XP, gear and condition survive the scene closing. Which file that is depends
/// on how the build was launched — see SaveLocation. The short version: the
/// shipped app uses the real one, a test run uses a copy of it.
public partial class CacheWarrenScene : Node3D
{
    /// The pause between one action finishing and the next beginning. Long
    /// enough to read what happened and see it coming — combat is meant to be
    /// watched, not raced through.
    [Export] public float BeatSeconds { get; set; } = 3.0f;

    /// How long an action's own animation is given before the countdown starts.
    /// Bookkeeping events (round markers, decision points) skip both.
    [Export] public float ActionSeconds { get; set; } = 1.0f;

    /// How long a resolved move's line holds after its animation, before the
    /// next one starts. Short — this is the beat that lets you read "Fren
    /// strikes for 14" rather than watch it be replaced.
    [Export] public float ReadSeconds { get; set; } = 0.55f;

    /// Collapses the pacing so a whole four-encounter delve can be watched end
    /// to end in well under a minute.
    ///
    /// **A test knob, and deliberately only a pacing one.** The tempting version
    /// is a one-shot-kill switch, and it would be the wrong tool: it changes
    /// what the rules do, so what you would be checking is a fight the game
    /// never plays. This changes only how long the renderer dwells, so the
    /// encounters, the intents, the damage and the drops are all exactly the
    /// ones a real run produces — it just stops waiting around between them.
    ///
    /// Set `WORKLINGS_FAST=1` to turn it on without touching the scene, which is
    /// how the capture tool and a quick manual look both use it.
    [Export] public bool FastMode { get; set; }

    /// How long the closing summary holds on screen, and how long an unattended
    /// run spends on a beat a player would take at their own pace.
    [Export] public float CardSeconds { get; set; } = 4.0f;

    /// Start the next delve when one ends, so the scene is never a still frame
    /// when you come back to it.
    [Export] public bool Loop { get; set; } = true;

    /// Take every choice automatically — hold the Approach at each decision
    /// point, never Unleash, always push deeper — so the scene runs a full chain
    /// to the mini-boss unattended. With this off, the run waits for the player
    /// at both of the beats the design gives them: steering the fight, and the
    /// press-your-luck bank or push.
    [Export] public bool AutoPlay { get; set; } = false;

    /// Whether an attacker crosses the floor to its target, or stays on its
    /// mark and plays the attack in place. Contact timing, impact frames,
    /// shake, sparks and damage numbers are identical either way — this only
    /// changes whether the body moves.
    ///
    /// **On by default since the lunge was retimed.** It read as sliding while
    /// the travel ran at the *front* of the attack — the body crossed the floor
    /// and then stood there through the rest of the wind-up. Now the wind-up
    /// plays on the mark and the travel is a burst that arrives on the contact
    /// frame, with a ghost trail on it. Still exposed, because the flat comparison
    /// is worth keeping.
    [Export] public bool AttackersTravel { get; set; } = true;

    /// Whether characters throw their signature — the lightning, the shockwave,
    /// the volley — on top of the universal impact reaction.
    ///
    /// Exposed for the same reason AttackersTravel is. Impact frames and the
    /// signature layer are separate systems answering separate complaints
    /// ("hits have no weight" and "every character hits the same"), and the only
    /// way to tell which one a given fight is short of is to be able to turn one
    /// of them off.
    [Export] public bool AbilityEffects { get; set; } = true;

    /// Which arena the delve is staged in.
    ///
    /// **Moonlit Ruins is the default as of 2026-09-12**, on Nikhil's call, and
    /// it settles the lighting question that had been open since the 11th: the
    /// original Cache Warren's floor is bright sand under a warm key, every
    /// reference shot is near-black stone, and an additive effect over ground
    /// already close to white adds nothing the eye can find. The telegraph rings
    /// were not dim on the old stage, they were absent.
    ///
    /// `CacheWarren` is kept and still selectable. It is the A/B, and the only
    /// honest way to ask whether the new stage earned the change.
    [Export] public StageKind Arena { get; set; } = StageKind.MoonlitRuins;

    /// Where the run is. The fight is one phase of four, not the whole scene —
    /// the briefing, the bank/push choice and the closing summary are beats of
    /// the delve and each holds the stage on its own terms.
    private enum Phase { Prep, Choosing, Counting, Resolving, Choice, Summary }

    /// Every body on the stage, built at runtime from the roster.
    ///
    /// **No creature is authored into `cache_warren.tscn` any more.** It used to
    /// carry one instanced `.glb` per foe with the size baked into a transform,
    /// which meant adding a creature was a scene edit as well as a code one —
    /// and a scene edit is the half nobody can review in a diff. The scene now
    /// holds two empty markers and the cast is assembled from `CreatureRoster`.
    private StageCast _cast = null!;

    /// Slot prefixes, so a creature that is both a Workling and a foe (the
    /// Flicker is) gets two bodies rather than attacking itself.
    private const string PartySlot = "party:";
    private const string FoeSlot = "foe:";

    private StageActor _party = null!;
    private StageActor _foe = null!;
    /// Which creature the player walked in wearing.
    private Creature _partyCreature = CreatureRoster.TempestRam;
    private CombatHud _hud = null!;
    private LoadoutPanel _prep = null!;
    private DamageNumbers _numbers = null!;
    private Color _petEnergy, _foeEnergy;

    private readonly Queue<CombatEvent> _pending = new();
    private ImpactFrames _impact = null!;
    private AbilityVfx _vfx = null!;
    private CombatAudio _audio = null!;

    /// The last whole second the beat countdown was seen at, so a tick fires
    /// once per second rather than once per frame.
    private int _lastTickSecond = -1;
    private readonly AttackLunge _lunge = new();
    /// A beat runs in two phases: the action plays, then the countdown to the
    /// next one. Separating them is what lets the countdown mean "next attack
    /// in 3s" rather than draining through the attack itself.
    private double _actionTimer;
    private double _lastLungeDuration;
    private double _beatTimer;
    private double _beatLength;
    private double _cardTimer;
    private int _round;
    /// The move the player took last round, so the bar can mark it between
    /// decisions rather than going blank.
    private CombatAction? _lastAction;
    private IntentBadge _intent = null!;

    private readonly PetCombatRates _rates = new();
    /// The living pet. Every delve is built from it and every resolution is
    /// written back into it, so a run starts from the condition and gear the
    /// last one left behind — and now from what the last *session* left behind.
    private PetState _state = null!;

    /// The Workling handed in by whoever opened this window, if anyone did.
    ///
    /// When the desktop pet hosts a delve, **the pet owns the save**: it hands
    /// the state in here and takes the result back through Resolved, and this
    /// scene neither loads nor writes. Two owners writing the same file is how a
    /// run's XP gets silently rolled back by a needs tick that started from a
    /// stale copy.
    ///
    /// Null when the scene is run on its own, which is still how the dungeon is
    /// worked on in isolation — then it loads and saves for itself as before.
    public PetState? HostedState { get; set; }

    /// Fired when a run resolves, with the Workling that walked out of it.
    public event System.Action<PetState>? Resolved;

    /// Fired whenever the fight reaches a state worth photographing, with a
    /// short label for it.
    ///
    /// **This is how the dungeon gets reviewed without watching it.** A capture
    /// tool sampling every Nth frame has to shoot a whole run blind and then be
    /// read frame by frame, and the beats that matter — the intent badge, the
    /// countdown, the moment a blow lands — are each on screen for well under a
    /// second, so most of the shots are of two creatures standing still and the
    /// interesting ones are found by scrubbing. The scene knows exactly when it
    /// has changed state; this says so, and the tool shoots those frames only.
    ///
    /// Nothing listens in the shipped game, so it costs a null check per beat.
    public event System.Action<string>? Beat;

    /// Fired once the closing summary has had its time and there is no next run.
    /// A hosted delve closes its window on this; run on its own with Loop off,
    /// nothing listens and the summary simply stays up.
    public event System.Action? Finished;
    private PetStateFileStore _store = null!;
    private SaveLocation _save;
    /// Cleared when the save on disk could not be read. A run still plays, from
    /// the demo pet, but nothing is written back: a file this build cannot parse
    /// is more likely a newer save or a real Workling than junk, and overwriting
    /// it to recover is the one unrecoverable move.
    private bool _saves = true;
    private Delve _delve = null!;
    private CombatEncounter _encounter = null!;
    /// How far into the encounter's log playback has read. The log only grows,
    /// so this is the whole bookkeeping needed to feed events in as rounds
    /// resolve rather than all at once.
    private int _logCursor;
    private Phase _phase = Phase.Prep;

    private string _petName = "";
    private string _foeName = "";
    private int _petHP, _petMaxHP, _foeHP, _foeMaxHP;
    private string _line = "";
    private string _status = "";

    public override void _Ready()
    {
        LoadState();
        var stage = BuildStage();
        var camera = stage.GetNode<Camera3D>("StageCamera");
        BuildCast(stage);
        _petEnergy = _partyCreature.Energy;
        _foeEnergy = _foe != null ? CreatureRoster.FindOrDefault(_foe.ModelName).Energy : _petEnergy;
        _numbers = new DamageNumbers(this);
        _lunge.Travel = AttackersTravel;
        _impact = new ImpactFrames(camera, this, this);
        _vfx = new AbilityVfx(this, camera)
        {
            Enabled = AbilityEffects,
        };
        _audio = new CombatAudio();
        AddChild(_audio);
        _prep = new LoadoutPanel(this);

        if (FastMode || OS.GetEnvironment("WORKLINGS_FAST").Length > 0)
        {
            BeatSeconds = 0.35f;
            ActionSeconds = 0.18f;
            ReadSeconds = 0.05f;
            CardSeconds = 0.7f;
        }

        BeginRun();
        _intent = new IntentBadge(_hud.Root, camera);

        // The bar is a control, not a legend, so the mouse reaches the same four
        // decisions the keyboard does. Both go through the guards below rather
        // than straight at the encounter — a slot is drawn dim between decisions
        // and clicking it then must do nothing.
        _hud.Bar.Chose += action => { if (CanAct()) TakeAction(action); };
        _hud.Bar.Unleashed += () =>
        {
            if (CanAct() && _encounter.SignatureReady) TakeAction(CombatAction.Signature);
        };
        _hud.Bar.Pushed += () => { if (CanChoose()) { _delve.PushDeeper(); StartEncounter(); } };
        _hud.Bar.Banked += () =>
        {
            if (!CanChoose()) return;
            _delve.Bank();
            ShowSummary();
            UpdateReadout();
        };
    }

    private bool CanAct() => !AutoPlay && _phase == Phase.Choosing;
    private bool CanChoose() => !AutoPlay && _phase == Phase.Choice;

    /// Reads the saved Workling, or starts a fresh one. A missing file is a first
    /// run, not a failure; anything else is reported and locks saving off.
    ///
    /// The path is printed every launch, and says whether it is the real save or
    /// a test copy. Silence about which file is being written is exactly how a
    /// test run overwrites a real pet without anyone noticing until it is gone.
    private void LoadState()
    {
        if (HostedState is not null)
        {
            _state = HostedState;
            _saves = false;
            GD.Print($"Workling: handed in by the host — {_state.Name}, Lv {_state.Level}");
            return;
        }

        _save = SaveLocation.Resolve();
        _store = new PetStateFileStore(_save.Path);
        GD.Print($"Workling: {_save.Path} "
               + $"({(_save.IsShared ? "the real save" : "not the real save")} — {_save.Reason})");
        try
        {
            _state = _store.Load() ?? DemoPet();
        }
        catch (System.Exception error)
        {
            GD.PushWarning($"Could not read {_save.Path}: {error.Message}. "
                         + "Running from the demo pet; this session will not save.");
            _state = DemoPet();
            _saves = false;
        }
    }

    private void SaveState()
    {
        if (!_saves) return;
        try
        {
            _store.Save(_state);
        }
        catch (System.Exception error)
        {
            GD.PushWarning($"Could not write {_save.Path}: {error.Message}");
            _saves = false;
        }
    }

    /// The Workling a first run starts from. Deliberately a few delves along
    /// rather than PetState.NewPet(): the Cache Warren's curve is authored for a
    /// Workling with some levels on it, and a level-one starter with 5 in every
    /// stat does 1 damage to the Snag and retreats every run — which would
    /// demonstrate the wiring by never showing the chain it exists to run.
    ///
    /// Elemental because the stage holds the Tempest Ram, so the family colour
    /// the HUD and the hit sparks read off matches the body they are attached to.
    private static PetState DemoPet() => new PetState(
        name: "Pixel",
        needs: new PetNeeds(hunger: 20, energy: 85, happiness: 80, trust: 75),
        preferences: new PetPreferences(PetFood.Berries, PetPlayActivity.Puzzle),
        lastUpdatedAt: System.DateTimeOffset.Now,
        family: PetFamily.Elemental,
        totalXP: 900,
        stats: new PetStats(vitality: 22, power: 17, defense: 13, agility: 11, wit: 8),
        ownedItems: PetState.StarterItems,
        loadout: PetState.StarterLoadout);

    // MARK: - Driving the delve

    /// The briefing, near-verbatim from the design: narration whose one gameplay
    /// job is to tell the player what kind of prep this delve rewards.
    private const string Briefing =
        "A dungeon looms. If this is the Cache Warren, expect a nimble scamp, a "
      + "grabbing Snag, an evasive Flicker — and something heavy at the bottom. "
      + "You may want to pack for accuracy. Or bring a Ward.";

    /// Opens a run on the prep screen — beat two, and the first thing the player
    /// actually does. The delve itself is not built until prep is confirmed,
    /// because the gear chosen here is folded into the fighter that enters it.
    /// One trail per actor, because the baked poses belong to that character's
    /// own mesh and attack clip. Keyed by actor rather than held as two fields
    /// because the foes share a pool of models that is swapped by visibility, so
    /// "the foe" is a different actor in each encounter of the chain.
    private readonly System.Collections.Generic.Dictionary<StageActor, GhostTrail> _trails = new();

    private void BeginRun()
    {
        _petName = _state.Name;
        _petEnergy = FamilyEnergy.Of(_state.Family);
        _petMaxHP = Combatant.Pet(_state, _rates).MaxHP;
        _petHP = _petMaxHP;

        // The plate behind the prep screen already shows what is waiting at the
        // top of the chain, which is what the briefing is talking about.
        ShowFoe(Worklings.Core.Combat.CacheWarren.Encounters[0]);
        _hud ??= new CombatHud(this, _petName, _petMaxHP, _petEnergy,
                               _foeName, _foeMaxHP, _foeEnergy);
        // Re-set rather than left as constructed: the HUD outlives a run and the
        // Workling's name is the player's to change between them.
        _hud.SetPet(_petName, _petEnergy);
        _hud.SetFoe(_foeName, _foeMaxHP, _foeEnergy);
        _hud.Reset(_petMaxHP, _foeMaxHP);

        _pending.Clear();
        _lunge.Cancel();
        _vfx.Clear();

        // Baked here, on the briefing screen, because baking a pose reads mesh
        // data back from the GPU and ten of those inside one swing is a frame
        // hitch. Nothing is moving yet, so the stall has nowhere to show. Baking
        // also poses the skeleton, hence the idle again afterwards.
        TrailFor(_party);
        _party.ResetPose();
        _party.Play(ActorAction.Idle, loop: true);

        _phase = Phase.Prep;
        _intent?.Hide();
        _prep.Open(_state, "The Cache Warren", Briefing);
        Beat?.Invoke("prep");
        _cardTimer = AutoPlay ? CardSeconds : 0;
        _line = "";
        _status = "";
        UpdateReadout();
    }

    /// Prep is confirmed: take the gear and the Approach the player chose, build
    /// the delve from the pet that results, and drop into the first encounter.
    /// The seed comes off the clock so each run differs; a delve launched from
    /// the app seeds from the save state plus a per-delve nonce instead, which is
    /// what makes a run reproducible from a bug report.
    private void Descend()
    {
        _state = _prep.Result;
        TakeTheBody(_prep.Creature);
        _hud.SetPet(_petName, _petEnergy);
        _prep.Close();

        var pet = Combatant.Pet(_state, _rates);
        _petName = pet.Name;
        _petMaxHP = pet.MaxHP;
        _petHP = pet.CurrentHP;

        ulong seed = (ulong)Time.GetTicksUsec();
        _delve = Delve.CacheWarrenDelve(
            pet, _rates.CombatEffectiveness(_state.Needs), _rates, seed, _state.OwnedItems);
        _delve.Descend();
        StartEncounter();
    }

    /// Resolves the current encounter and hands its log to playback. The pet
    /// enters at the HP the delve carried in, not at full — that carry is the
    /// whole reason pushing deeper is a gamble.
    private void StartEncounter()
    {
        var foe = _delve.CurrentFoe!;
        _encounter = _delve.MakeEncounter()!;
        _logCursor = 0;

        ShowFoe(foe);
        _petHP = _delve.CarriedHP;
        _hud.SetFoe(_foeName, _foeMaxHP, _foeEnergy);
        _hud.Reset(_petMaxHP, _foeMaxHP);
        _hud.SetHP(_petHP, _foeHP);

        _lunge.Cancel();
        _pending.Clear();
        DrainLog();
        _round = 0;
        _beatTimer = 0;
        _actionTimer = 0;
        _lastAction = null;
        _line = "";
        _party.ResetPose();
        _party.Play(ActorAction.Idle, loop: true);
        _foe.Play(ActorAction.Idle, loop: true);

        // **Open the first round rather than dropping into playback.** The fight
        // used to enter with both timers at zero, so the very first event of an
        // encounter was applied on the first frame after the swap — which is why
        // the Flicker was landing a hit before the screen had finished
        // appearing. Now the round opens, the foe declares, and nothing moves
        // until the player answers.
        _encounter.Step();
        DrainLog();
        OpenRound();
    }

    /// Queues every event the encounter has logged since playback last looked.
    private void DrainLog()
    {
        for (; _logCursor < _encounter.Log.Count; _logCursor++)
        {
            _pending.Enqueue(_encounter.Log[_logCursor]);
        }
    }

    /// Everything queued has been animated. Either the fight is over, or it
    /// opens the next round and hands it to the player.
    private void PumpEncounter()
    {
        if (!_encounter.Status.IsOngoing && !_encounter.Status.IsAwaitingAction)
        {
            FinishEncounter();
            return;
        }
        if (_encounter.Status.IsOngoing) _encounter.Step();
        DrainLog();
        OpenRound();
    }

    /// A round has opened. The foe has declared, and the fight is the player's.
    ///
    /// **This is the beat the whole restructure exists for.** The order used to
    /// be countdown → the foe's move → countdown → a prompt → the consequence,
    /// which put the decision *after* the information it was about and made the
    /// countdown look like it fired at random. Now it is: the foe declares, the
    /// player answers, the countdown runs on that answer, and both moves play.
    /// One decision and one countdown per round, always in that order.
    private void OpenRound()
    {
        if (!_encounter.Status.IsAwaitingAction) return;
        _phase = Phase.Choosing;
        _round = _encounter.Round;
        _hud.ClearBeat();
        ShowIntent();
        _line = "";
        Beat?.Invoke($"e{_delve.EncounterNumber}-r{_round}-intends-{_encounter.Intent.Kind}");
        // An unattended run still pauses, briefly, where a player would read the
        // badge — a capture with no pause is a capture of a different game.
        _cardTimer = AutoPlay ? CardSeconds * 0.25 : 0;
        UpdateReadout();
    }

    /// Puts the foe's declared move over its head.
    private void ShowIntent()
    {
        var casting = CreatureRoster.For(_foeName);
        _intent.Show(_foe, casting.StageHeight, _encounter.Intent, _encounter.IntentThreatens);
    }

    /// The player has chosen. The countdown now runs on a decision already made,
    /// which is what makes it a wind-up rather than a wait.
    private void TakeAction(CombatAction action)
    {
        if (_phase != Phase.Choosing) return;
        _lastAction = action;
        _encounter.Act(action);
        DrainLog();
        _intent.Hide();
        _phase = Phase.Resolving;
        BeginCountdown();
        UpdateReadout();
    }

    /// The log has played out. The delve decides what that meant: a retreat, a
    /// finished chain, or the bank/push choice.
    private void FinishEncounter()
    {
        _intent.Hide();
        _delve.RecordOutcome(_encounter);
        switch (_delve.Status.Kind)
        {
            case DelveStatusKind.AwaitingPushChoice:
                _phase = Phase.Choice;
                _hud.ClearBeat();
                _line = _delve.LastDrop is Item drop
                    ? $"{_foeName} down — {drop.DisplayName()} recovered"
                    : $"{_foeName} down";
                _cardTimer = AutoPlay ? CardSeconds * 0.5 : 0;
                Beat?.Invoke($"e{_delve.EncounterNumber}-bank-or-push");
                break;
            default:
                ShowSummary();
                break;
        }
        UpdateReadout();
    }

    /// The run is over either way. Resolution is where the delve touches the pet
    /// at all: XP, needs and gear move **once**, here, from the HP walked out
    /// with — never per encounter.
    private void ShowSummary()
    {
        var resolution = _delve.Resolution(_state);
        if (resolution == null) return;
        _state = resolution.State;
        // The one write per run, in the one place the pet changes. A hosted run
        // writes nothing here and hands the result up instead.
        SaveState();
        Resolved?.Invoke(_state);

        // The bed stops before the sting, so the two never overlap.
        _audio.StopBgm();
        _audio.Play(resolution.Tier == ExitTier.Downed
            ? CombatSound.Defeat
            : CombatSound.Victory, volume: 0.9f);

        string headline = resolution.BossDefeated ? "Delve complete"
                        : resolution.Banked ? "Banked"
                        : "Retreated";
        var spoils = new List<string>();
        foreach (var item in resolution.ItemsDropped) spoils.Add(item.DisplayName());
        _line = $"{headline} — {resolution.ClearedCount}/{_delve.TotalEncounters} cleared, "
              + $"+{resolution.XPGained:0} XP"
              + (spoils.Count > 0 ? $", {string.Join(", ", spoils)}" : "");
        _status = $"exit {resolution.Tier.RawValue()}   ·   Lv {_state.Level}   ·   {_state.Mood}";
        _phase = Phase.Summary;
        _cardTimer = CardSeconds;
        Beat?.Invoke($"summary-{resolution.Tier.RawValue()}");
    }

    /// Bank or push. Both are guarded by the delve itself, so a stray keypress
    /// outside the choice does nothing.
    public override void _UnhandledInput(InputEvent @event)
    {
        if (AutoPlay) return;
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
        switch (_phase)
        {
            case Phase.Prep:
                if (_prep.HandleKey(key.Keycode)) Descend();
                break;
            case Phase.Choosing:
                switch (key.Keycode)
                {
                    case Key.Key1: TakeAction(CombatAction.Strike); break;
                    case Key.Key2: TakeAction(CombatAction.Brace); break;
                    case Key.Key3 or Key.U: TakeAction(CombatAction.Signature); break;
                    // Space repeats last round's move, so a player who is happy
                    // striking can hold one key through a fight.
                    case Key.Space or Key.Enter or Key.KpEnter:
                        TakeAction(_lastAction ?? CombatAction.Strike); break;
                }
                break;
            case Phase.Choice:
                switch (key.Keycode)
                {
                    case Key.Space or Key.Enter or Key.KpEnter:
                        _delve.PushDeeper();
                        StartEncounter();
                        break;
                    case Key.B:
                        _delve.Bank();
                        ShowSummary();
                        UpdateReadout();
                        break;
                }
                break;
        }
    }

    // MARK: - Playback

    public override void _Process(double delta)
    {
        // Impact reactions animate on real time. The freeze applies to the
        // fight, not to the shake and dust working their way out of it.
        _impact.Tick(delta);
        _hud?.Tick(delta);
        // Real time, and outside the hit-stop guard below: a one-shot clip that
        // has finished has to hand the body back to its idle loop, or the actor
        // stands frozen on the last frame of its swing for the whole beat. That
        // was every creature, most of the fight.
        _party?.Tick(delta);
        _foe?.Tick(delta);
        // Real time, like the shake and the dust: hit-stop freezes the fight and
        // lets the trail keep dissipating out of it.
        foreach (var trail in _trails.Values) trail.Tick(delta);

        // The attacker freezes at the point of contact during hit-stop rather
        // than sliding through the held frame.
        _lunge.Tick(delta, _impact.IsHitStopped ? 0 : 1);
        // Frozen with the fight, not with the shake: a bolt is part of the blow
        // and has to hold on the contact frame, where the dust settling out of
        // the last one does not.
        _vfx.Tick(delta, _impact.IsHitStopped ? 0 : 1);

        if (_impact.IsHitStopped) return;

        _intent.Track();

        // Choosing is a card beat: nothing advances until the player answers.
        if (_phase is not (Phase.Counting or Phase.Resolving))
        {
            TickCard(delta);
            return;
        }

        // The countdown. It runs immediately before the move it is counting to,
        // and the clock names that move.
        if (_phase == Phase.Counting)
        {
            _beatTimer -= delta;
            var (who, what, isPet) = NextBeat();
            _hud?.SetBeat(_beatTimer, who, what, isPet);
            int second = (int)System.Math.Ceiling(_beatTimer);
            if (second != _lastTickSecond && second > 0)
            {
                _lastTickSecond = second;
                _audio.Play(CombatSound.Tick, volume: 0.5f);
            }
            if (_beatTimer > 0) return;
            _lastTickSecond = -1;
            _hud?.ClearBeat();
            _phase = Phase.Resolving;
            PlayNext();
            return;
        }

        // Resolving: hold while the move that just fired plays out.
        if (_actionTimer > 0)
        {
            _actionTimer -= delta;
            if (_actionTimer > 0) return;
        }

        // Riders and bookkeeping first. A creature blurring aside, hardening, or
        // going down is the *consequence* of the move that just landed, not a
        // move of its own — counting down to "SNAG FALLS" would be absurd — so
        // they play straight off the back of it. Round markers show nothing at
        // all and pass through in the same frame.
        while (_pending.Count > 0 && Weight(_pending.Peek()) != BeatWeight.Move)
        {
            var rider = _pending.Dequeue();
            bool shows = Weight(rider) == BeatWeight.Rider;
            Apply(rider);
            UpdateReadout();
            if (!shows) continue;
            _actionTimer = ActionSeconds + ReadSeconds;
            return;
        }

        if (_pending.Count == 0)
        {
            PumpEncounter();
            return;
        }

        // A move is waiting, so it gets its own wind-up.
        //
        // **One countdown per move, not per round.** The first version counted
        // once at the top of the round and then played the pet's move and the
        // foe's back to back, which reads as the foe getting a free hit: you
        // watch a 3-2-1, your Workling swings, and the answer arrives with no
        // warning at all. The countdown is the game telling you something is
        // about to happen, so every something needs one.
        BeginCountdown();
    }

    /// The between-fight beats. A choice with AutoPlay off has no timer and
    /// simply waits for the player.
    private void TickCard(double delta)
    {
        if (_cardTimer <= 0) return;
        _cardTimer -= delta;
        if (_cardTimer > 0) return;
        switch (_phase)
        {
            case Phase.Prep:
                _prep.TakeBestAvailable();
                Descend();
                break;
            case Phase.Choosing:
                TakeAction(_encounter.AutoAction());
                break;
            case Phase.Choice:
                _delve.PushDeeper();
                StartEncounter();
                break;
            case Phase.Summary:
                if (Loop) BeginRun();
                else
                {
                    // The way back up. Played here rather than on the desktop so
                    // that all of the audio, and all of the files it holds open,
                    // live and die with the delve.
                    _audio.Play(CombatSound.ReturnChime);
                    Finished?.Invoke();
                }
                break;
        }
    }

    /// A filename-safe tag for an event, for the capture tool.
    private static string Slug(CombatEvent e) => e switch
    {
        CombatEvent.Struck x => $"{(x.Outcome.DidHit ? x.Outcome.DidCrit ? "crit" : "hit" : "miss")}-{x.Attacker}",
        CombatEvent.Signature x => $"signature-{x.Attacker}",
        CombatEvent.Slammed => "slam",
        CombatEvent.Telegraphed => "windup",
        CombatEvent.Braced => "brace",
        CombatEvent.Grabbed => "grab",
        _ => e.GetType().Name.ToLowerInvariant(),
    };

    /// How much of the stage an event is worth.
    private enum BeatWeight
    {
        /// A creature's actual move. Earns a countdown and a beat.
        Move,
        /// A consequence of the move that just landed — a blur, a harden, a
        /// death. Shows, but is not counted down to.
        Rider,
        /// Shows nothing. Passes through in the frame it is dequeued.
        Marker,
    }

    private static BeatWeight Weight(CombatEvent e) => e switch
    {
        CombatEvent.Struck or CombatEvent.Signature or CombatEvent.Slammed
            or CombatEvent.Telegraphed or CombatEvent.Braced
            or CombatEvent.Grabbed => BeatWeight.Move,
        CombatEvent.Phased or CombatEvent.Hardened
            or CombatEvent.Defeated => BeatWeight.Rider,
        _ => BeatWeight.Marker,
    };

    /// Starts the wind-up to the next move in the queue.
    ///
    /// **Markers are cleared first, and that is the whole reason this is a
    /// method.** A round opens by logging `RoundBegan` and `AwaitingAction`, and
    /// those are still sitting at the head of the queue when the player answers
    /// — so counting down from here without clearing them spent the first
    /// countdown on a marker that draws nothing, found the pet's actual move
    /// behind it, and counted down again. Two 3-2-1s and then one swing.
    ///
    /// A countdown may only ever be started with a move at the head of the
    /// queue. Guaranteeing that here means no caller has to remember it.
    private void BeginCountdown()
    {
        while (_pending.Count > 0 && Weight(_pending.Peek()) == BeatWeight.Marker)
        {
            Apply(_pending.Dequeue());
        }
        if (_pending.Count == 0) { PumpEncounter(); return; }

        _phase = Phase.Counting;
        _beatTimer = _beatLength = BeatSeconds;
        _lastTickSecond = -1;
    }

    /// Fires the move the countdown was counting to.
    private void PlayNext()
    {
        if (_pending.Count == 0) return;
        var next = _pending.Dequeue();
        if (Apply(next))
        {
            // The move holds until its own animation has played out, plus a
            // short read of the line it wrote.
            _actionTimer = (_lunge.IsBusy
                ? System.Math.Max(ActionSeconds, _lastLungeDuration + 0.12)
                : ActionSeconds) + ReadSeconds;
        }
        UpdateReadout();
        // Markers show nothing, so photographing them is 40 duplicate frames of
        // whatever was already on screen.
        if (Weight(next) != BeatWeight.Marker)
        {
            Beat?.Invoke($"e{_delve.EncounterNumber}-r{_round}-{Slug(next)}");
        }
    }

    /// Sends the attacker at its target and hangs the whole reaction off the
    /// moment it arrives.
    ///
    /// The combatants stand ~11 units apart — over two body lengths — so an
    /// attack played in place lands nowhere near the defender and the fight
    /// reads as two models taking turns with animations. Closing the distance is
    /// what makes it a collision; impact frames are the reaction to that
    /// collision, and were previously firing at a contact that never happened.
    /// The line that goes up as an action *starts* — who is doing what, with no
    /// outcome in it.
    ///
    /// **The outcome used to be written here too**, at the moment the event was
    /// dequeued, which is before the wind-up has even played. So the plaque read
    /// "Flicker misses" for the entire second and a bit that the Flicker spent
    /// swinging, and against a high-evasion foe the whole fight was spoiled a
    /// beat ahead of itself. The result now lands on the contact frame, with the
    /// flash and the damage number, which is where it actually happens.
    private void Announce(string line)
    {
        _line = line;
        _hud?.SetNarration(_line);
    }

    private void ScheduleImpact(
        StageActor attacker, StageActor defender, bool toFoe,
        StrikeOutcome outcome, bool isSignature = false, string result = "")
    {
        int maxHP = toFoe ? _foeMaxHP : _petMaxHP;
        double severity = maxHP > 0 ? (double)outcome.Damage / maxHP : 0;
        var direction = defender.Root.Position - attacker.Root.Position;
        _lastLungeDuration = AttackLunge.DurationFor(attacker.AttackImpactDelay());
        // Scheduled here rather than inside onContact, because most of a
        // signature happens *before* the blow lands — the telegraph on the
        // victim's floor and the volley crossing the gap both have to be already
        // running by the time contact arrives. Firing it on contact would leave
        // only the aftermath.
        _vfx.Begin(AbilitySignatures.For(attacker.ModelName), attacker, defender,
                   attacker.AttackImpactDelay(),
                   toFoe ? _petEnergy : _foeEnergy, outcome.DidCrit || isSignature);
        _lunge.Begin(attacker, defender, attacker.AttackImpactDelay(), trail: TrailFor(attacker),
                     onContact: () =>
        {
            ApplyDamage(toFoe, outcome.Damage);
            var energy = toFoe ? _petEnergy : _foeEnergy;
            _impact.Strike(defender, direction, severity, outcome.DidCrit || isSignature, energy);
            _numbers.Spawn(defender.Root.Position, outcome.Damage, energy,
                           outcome.DidCrit || isSignature);
            if (result.Length > 0) _line = result;
            UpdateReadout();
        });
    }

    /// A miss still commits — the attacker goes in and comes back with nothing
    /// to show for it, which is what makes a miss read as a miss rather than as
    /// a skipped turn.
    private void ScheduleWhiff(StageActor attacker, StageActor defender, string result = "")
    {
        _lastLungeDuration = AttackLunge.DurationFor(attacker.AttackImpactDelay());
        _lunge.Begin(attacker, defender, attacker.AttackImpactDelay(),
                     onContact: () =>
                     {
                         _numbers.SpawnMiss(defender.Root.Position);
                         Dodge(attacker, defender);
                         if (result.Length > 0) { _line = result; UpdateReadout(); }
                     },
                     trail: TrailFor(attacker));
    }

    /// The defender's half of a miss.
    ///
    /// Without it the attacker committed its whole travel, the word MISS
    /// appeared, and the target stood perfectly still through all of it — which
    /// reads as the attacker failing rather than as the target evading. A miss is
    /// caused by the thing being missed, so the thing being missed has to move.
    ///
    /// Sideways, not backwards: perpendicular to the blow, because stepping away
    /// along the attacker's own line just looks like being pushed.
    private void Dodge(StageActor attacker, StageActor defender)
    {
        var line = defender.Root.Position - attacker.Root.Position;
        var side = new Vector3(-line.Z, 0, line.X).Normalized() * 1.15f;

        var tween = CreateTween();
        tween.TweenMethod(Callable.From<Vector3>(defender.SetOffset), Vector3.Zero, side, 0.12)
             .SetTrans(Tween.TransitionType.Quint).SetEase(Tween.EaseType.Out);
        tween.TweenMethod(Callable.From<Vector3>(defender.SetOffset), side, Vector3.Zero, 0.30)
             .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
    }

    /// Swaps the player's body to the Workling chosen on the prep screen.
    ///
    /// Built on first pick rather than all at once: the alpha pool is every
    /// renderable Workling in the roster and will be fifteen to twenty of them,
    /// and instancing every one at startup to render the one the player brought
    /// is a startup cost that grows with the roster. The foes are built up front
    /// because a delve meets all of them; the party meets exactly one.
    ///
    /// The trail is per-actor and baked from that mesh's own attack pose, so a
    /// changed body needs a new one — `TrailFor` keys on the actor and bakes on
    /// first ask, which is already the right behaviour here.
    private void TakeTheBody(Creature creature)
    {
        _partyCreature = creature;
        _petEnergy = creature.Energy;
        string key = PartySlot + creature.Id;

        var actor = _cast.Get(key);
        if (actor == null)
        {
            var stage = GetNode<Node3D>("Stage");
            actor = _cast.Add(creature, GetNode<Node3D>("Party"),
                              MarkOf(stage, "PartySlot", StageSet.PartyMark),
                              MarkOf(stage, "FoeSlot", StageSet.FoeMark),
                              key: key);
        }
        if (actor == null)
        {
            GD.PushWarning($"[stage] {creature.Id} would not build; keeping the last body");
            return;
        }
        _party = actor;
        _cast.ShowOnly(key, PartySlot);
        _party.ResetPose();
        TrailFor(_party);
        _party.Play(ActorAction.Idle, loop: true);
    }

    /// The trail belonging to whichever actor is swinging, baked on first ask.
    ///
    /// The party's is warmed at the briefing where the stall is invisible. A
    /// foe's is warmed on its first swing, which costs one hitch per model per
    /// run — the alternative is baking all four at startup for the three that may
    /// never appear.
    private GhostTrail TrailFor(StageActor actor)
    {
        if (_trails.TryGetValue(actor, out var existing)) return existing;
        var trail = new GhostTrail(actor, this);
        trail.Bake();
        _trails[actor] = trail;
        return trail;
    }

    /// Turns one event into what you see. Returns whether it deserves a beat —
    /// bookkeeping events (round markers, decision points) pass through instantly
    /// so the fight does not stall on things with nothing to show.
    private bool Apply(CombatEvent e)
    {
        switch (e)
        {
            case CombatEvent.Struck x:
            {
                bool petAttacking = x.Attacker == _petName;
                var attacker = petAttacking ? _party : _foe;
                var defender = petAttacking ? _foe : _party;
                attacker.Play(ActorAction.Attack);
                Announce($"{x.Attacker} attacks");
                if (x.Outcome.DidHit)
                {
                    ScheduleImpact(attacker, defender, petAttacking, x.Outcome,
                        result: $"{x.Attacker} {(x.Outcome.DidCrit ? "crits" : "strikes")} "
                              + $"for {x.Outcome.Damage}");
                    _audio.Play(x.Outcome.DidCrit ? CombatSound.Crit : CombatSound.Hit);
                }
                else
                {
                    ScheduleWhiff(attacker, defender, result: $"{x.Defender} slips it");
                    _audio.Play(CombatSound.Dodge);
                }
                return true;
            }
            case CombatEvent.Signature x:
                _party.Play(ActorAction.Signature);
                Announce($"{x.Attacker} unleashes");
                ScheduleImpact(_party, _foe, true, x.Outcome, isSignature: true,
                    result: $"{x.Attacker} unleashes for {x.Outcome.Damage}");
                _audio.Play(CombatSound.Unleash);
                return true;

            // The Monolith's telegraphed slam — the foe's answer to a signature,
            // and the reason the boss encounter reads differently from the three
            // above it.
            case CombatEvent.Slammed x:
                _foe.Play(ActorAction.Signature);
                _audio.Play(CombatSound.Slam);
                Announce($"{x.Attacker} slams");
                if (x.Outcome.DidHit)
                    ScheduleImpact(_foe, _party, false, x.Outcome, isSignature: true,
                                   result: $"{x.Attacker} slams for {x.Outcome.Damage}");
                else
                    ScheduleWhiff(_foe, _party, result: $"{x.Defender} slips the slam");
                return true;

            // A wind-up with no contact. It earns a beat precisely because the
            // pause is the information: the slam is coming.
            case CombatEvent.Telegraphed x:
                _audio.Play(CombatSound.Telegraph);
                _line = $"{x.Who} winds up";
                return true;

            case CombatEvent.Hardened x:
                _audio.Play(CombatSound.Harden);
                _line = $"{x.Who} hardens (+{x.GuardGain} Guard)";
                return true;

            case CombatEvent.Braced x:
                _party.Play(ActorAction.Idle, loop: true);
                _audio.Play(CombatSound.Brace);
                _petHP = System.Math.Min(_petMaxHP, _petHP + x.Regen);
                _line = $"{x.Who} braces (+{x.Regen})";
                return true;

            case CombatEvent.Grabbed x:
                _foe.Play(ActorAction.Attack);
                _audio.Play(CombatSound.Snare);
                _line = $"{x.Attacker} snares {x.Target} (-{x.AgilityLoss} Agility)";
                return true;

            case CombatEvent.Phased x:
                _foe.Play(ActorAction.Idle, loop: true);
                _audio.Play(CombatSound.Phase);
                _line = $"{x.Who} blurs aside";
                return true;

            case CombatEvent.Defeated x:
            {
                bool petDied = x.Who == _petName;
                var victim = petDied ? _party : _foe;
                var killer = petDied ? _foe : _party;
                victim.Play(ActorAction.Downed);
                if (!victim.Animations.HasDeathClip) FallOver(victim, killer);
                // The poof is the foe leaving the stage. A downed Workling gets
                // the defeat sting at the end of the run instead, which is where
                // that news actually lands.
                if (!petDied) _audio.Play(CombatSound.Poof);
                _line = $"{x.Who} is down";
                return true;
            }

            case CombatEvent.EncounterEnded:
                // The delve writes the line that follows this — which foe went
                // down and what it dropped — so claiming a beat here only
                // inserted three seconds of "Victory" between the kill and the
                // news.
                return false;

            case CombatEvent.RoundBegan x:
                _round = x.Round;
                return false;

            default:
                return false;   // encounter markers, decision points
        }
    }

    /// Who acts when the countdown runs out, and what they are about to do.
    ///
    /// **The answer to "sometimes I don't attack and the Snag attacks twice".**
    /// That reading was fair and the fight was not cheating: a Careful Workling
    /// latches into Brace while it is hurt and spends whole rounds not striking,
    /// and the Snag's Snare drops the pet's Agility below the foe's, which flips
    /// initiative so the foe acts last in one round and first in the next — two
    /// foe turns in a row with a legal round boundary between them. Both are in
    /// the rules, both were completely invisible, and a bare "1.7s" cannot tell
    /// a braced turn apart from a lost one.
    ///
    /// So the clock names the beat. The queue holds the events the encounter has
    /// already resolved, in order, and the first one with a body attached is the
    /// next thing the player will see — bookkeeping markers are skipped because
    /// they animate nothing. An empty queue means the round is about to turn and
    /// the encounter has not been stepped yet, which the HUD says as much.
    private (string Who, string What, bool IsPet) NextBeat()
    {
        foreach (var e in _pending)
        {
            switch (e)
            {
                case CombatEvent.Struck x:
                    return (x.Attacker, "strikes", x.Attacker == _petName);
                case CombatEvent.Signature x:
                    return (x.Attacker, "unleashes", true);
                case CombatEvent.Slammed x:
                    return (x.Attacker, "slams", false);
                case CombatEvent.Telegraphed x:
                    return (x.Who, "winds up", x.Who == _petName);
                case CombatEvent.Braced x:
                    return (x.Who, "braces", x.Who == _petName);
                case CombatEvent.Grabbed x:
                    return (x.Attacker, "snares", false);
                case CombatEvent.Hardened x:
                    return (x.Who, "hardens", x.Who == _petName);
                case CombatEvent.Phased x:
                    return (x.Who, "blurs", false);
                case CombatEvent.Defeated x:
                    return (x.Who, "falls", x.Who == _petName);
                // Nothing follows this one. Saying "next round" over a corpse is
                // worse than saying nothing.
                case CombatEvent.EncounterEnded:
                    return ("", "THE FIGHT IS OVER", true);
            }
        }
        return ("", "NEXT ROUND", true);
    }

    /// Fells a body that has no death clip to fall down with.
    ///
    /// Four of the five characters are in that position — only the Scamp shipped
    /// a `Scamp_Death` — so a kill played the creature's hit-react and left it
    /// standing there until the next encounter swapped the model out. That is
    /// what the first play session saw as "no death animation for anything after
    /// the Scamp", and it is true: there was nothing to play.
    ///
    /// This is not a substitute for the clips, which are Nikhil's to author. It
    /// is the floor underneath them — every creature now visibly goes down —
    /// and it costs one `HasDeathClip: true` to retire per character as the real
    /// animations land.
    ///
    /// The body pivots at its own mark, which is at its feet, so it goes over
    /// like a felled tree rather than sinking through the floor. It falls *away*
    /// from whatever killed it: a corpse toppling toward its killer reads as a
    /// lunge, which is the opposite of the beat.
    private void FallOver(StageActor victim, StageActor killer)
    {
        var line = victim.Root.Position - killer.Root.Position;
        var axis = Vector3.Up.Cross(new Vector3(line.X, 0, line.Z)).Normalized();
        if (axis.LengthSquared() < 0.001f) axis = Vector3.Right;

        // Slow to tip and then quick over — a fall accelerates, and an even one
        // reads as a door closing.
        var tween = CreateTween();
        tween.TweenMethod(Callable.From<float>(a => victim.SetTopple(a, axis)),
                          0f, Mathf.DegToRad(84f), 0.62)
             .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        // The settle. Two degrees of rebound is the whole difference between a
        // body landing and a model reaching its final rotation.
        tween.TweenMethod(Callable.From<float>(a => victim.SetTopple(a, axis)),
                          Mathf.DegToRad(84f), Mathf.DegToRad(80f), 0.12)
             .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
    }

    private void ApplyDamage(bool toFoe, int amount)
    {
        if (toFoe) _foeHP = System.Math.Max(0, _foeHP - amount);
        else _petHP = System.Math.Max(0, _petHP - amount);
    }

    /// Pushes the whole fight state at the HUD in one place.
    ///
    /// The command bar is driven from here rather than only from the beats that
    /// open a decision, because the bar is persistent: the player needs to see
    /// which stance is held and whether the Signature is still in hand at every
    /// moment of the fight, not only in the two seconds they are being asked.
    private void UpdateReadout()
    {
        _hud.SetHP(_petHP, _foeHP);
        _hud.SetNarration(_line);

        switch (_phase)
        {
            case Phase.Prep:
                _hud.SetRunLine("the cache warren");
                _hud.Bar.Hide();
                break;
            case Phase.Summary:
                _hud.SetRunLine(_status);
                _hud.Bar.Hide();
                break;
            case Phase.Choice:
                _hud.SetRun(_delve.EncounterNumber, _delve.TotalEncounters, 0);
                if (AutoPlay) _hud.Bar.Hide(); else _hud.Bar.ShowChoice(_petEnergy);
                break;
            default:
                _hud.SetRun(_delve.EncounterNumber, _delve.TotalEncounters, _round);
                _hud.Bar.ShowFight(_lastAction, _encounter?.SignatureReady ?? true,
                                   live: _phase == Phase.Choosing && !AutoPlay, _petEnergy);
                break;
        }
    }

    /// Builds the arena and hands back its root, named `Stage`.
    ///
    /// The authored Cache Warren is instanced from its `.tscn`; the two
    /// procedural arenas are built by `StageSet`, which the capture tool also
    /// consumes — a preview built from different code than the game is a preview
    /// of something that does not exist.
    ///
    /// The node path `Stage/StageCamera` is preserved either way. Impact frames
    /// and the signature layer both resolve the camera through it, and the
    /// handover flags changing it as a break.
    private Node3D BuildStage()
    {
        var authored = GetNodeOrNull<Node3D>("Stage");
        if (Arena == StageKind.CacheWarren)
        {
            if (authored != null) return authored;
            var scene = GD.Load<PackedScene>("res://scenes/dungeon_stage.tscn");
            var built = scene.Instantiate<Node3D>();
            built.Name = "Stage";
            AddChild(built);
            return built;
        }

        // The authored stage is instanced by the scene file, so a procedural
        // arena has to remove it rather than merely hide it — two WorldEnvironment
        // nodes in one tree is undefined, and two Camera3Ds both marked Current
        // is a coin flip over which one renders.
        if (authored != null)
        {
            RemoveChild(authored);
            authored.QueueFree();
        }
        var set = StageSet.Build(Arena);
        AddChild(set);
        // The reviewed studies were captured with 4x MSAA. Without it the thin
        // additive geometry the whole signature layer is made of — bolt ribbons,
        // ring rims, claw sweeps — crawls with aliasing in motion.
        GetViewport().SetMsaa3D(Viewport.Msaa.Msaa4X);
        return set;
    }

    /// Builds every body the delve can need, from the roster.
    ///
    /// The foes come from the bestiary rather than a hardcoded list, so a foe
    /// added to `CacheWarren.Encounters` arrives on stage with no change here.
    /// All of them are built up front and swapped by visibility: instancing a
    /// `.glb` mid-delve is a frame hitch, and the moment it would land is the
    /// cut between one encounter and the next.
    private void BuildCast(Node3D stage)
    {
        _cast = new StageCast(this);
        var party = GetNode<Node3D>("Party");
        var foes = GetNode<Node3D>("Foe");
        var partyMark = MarkOf(stage, "PartySlot", StageSet.PartyMark);
        var foeMark = MarkOf(stage, "FoeSlot", StageSet.FoeMark);

        _partyCreature = CreatureRoster.ForRace(_state.Family);
        _party = _cast.Add(_partyCreature, party, partyMark, foeMark,
                           key: PartySlot + _partyCreature.Id)!;

        var names = new List<string>();
        foreach (var foe in Worklings.Core.Combat.CacheWarren.Encounters) names.Add(foe.Name);
        names.Add(Worklings.Core.Combat.CacheWarren.Boss.Name);
        foreach (var name in names)
        {
            var casting = CreatureRoster.For(name);
            _cast.Add(casting.Creature, foes, foeMark, partyMark,
                      heightOverride: casting.StageHeight,
                      key: FoeSlot + casting.Creature.Id);
        }
        _foe = _cast.Get(FoeSlot + CreatureRoster.For(names[0]).Creature.Id)!;
    }

    /// A stage's mark, falling back to the studies' framing if the arena does
    /// not carry one. A missing marker is worth saying out loud: the reviewed
    /// cameras were framed around specific marks, and standing the actors
    /// somewhere else frames an empty floor.
    private static Vector3 MarkOf(Node3D stage, string name, Vector3 fallback)
    {
        var marker = stage.GetNodeOrNull<Marker3D>(name);
        if (marker != null) return marker.Position;
        GD.PushWarning($"[stage] no {name}; using the studies' mark {fallback}");
        return fallback;
    }

    /// Puts a foe on the stage: its name and HP for the plate, and the body
    /// cast for it at the right size and colour.
    ///
    /// **The casting table is the one place the rules and the renderer touch**,
    /// and it points this way on purpose — the dungeon asks who plays the
    /// Monolith; the bestiary never learns what a `.glb` is. That is what lets
    /// the combat probes resolve a whole delve headlessly with no models loaded.
    private void ShowFoe(Foe foe)
    {
        _foeName = foe.Name;
        _foeMaxHP = foe.MaxHP;
        _foeHP = foe.MaxHP;

        var casting = CreatureRoster.For(foe.Name);
        string key = FoeSlot + casting.Creature.Id;
        var actor = _cast.Get(key);
        if (actor == null)
        {
            GD.PushWarning($"[stage] nothing cast for '{foe.Name}'; the stage keeps the last foe");
            return;
        }
        _foe = actor;
        _cast.ShowOnly(key, FoeSlot);
        // Re-applied per encounter because one creature can play two foes at
        // two sizes — the Snag is itself at 4.81 and the Monolith at 7.50.
        _foe.ResetPose();
        _cast.SetHeight(key, casting.Creature.Id, casting.StageHeight);
        _foeEnergy = casting.Creature.Energy;
        _foe.Play(ActorAction.Idle, loop: true);

        _audio.Play(CombatSound.Enter);
        // The boss gets its own heavier bed, and swapping it mid-delve is the
        // only warning the player gets that this encounter is different.
        _audio.StartBgm(boss: _delve?.IsBossEncounter ?? false);
    }
}
