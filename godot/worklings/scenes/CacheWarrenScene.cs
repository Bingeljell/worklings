using Godot;
using System.Collections.Generic;
using Worklings.Core.Combat;
using Worklings.Core.Pet;
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
/// Each encounter resolves instantly the moment it starts; playback is then a
/// replay of the recorded log at a watchable pace. That is a real property of
/// the design rather than a shortcut — a seeded encounter is fully determined
/// from the moment it begins, so nothing is lost by resolving first and
/// animating after. The delve *around* it is not pre-resolved: bank-or-push is
/// a live choice, and the next encounter is only built once it is made.
///
/// The pet state persists across delves in memory only. Loading and saving it
/// waits on the persistence slice; until then a run's XP, gear and condition
/// carry into the next run and are lost when the scene closes.
public partial class CacheWarrenScene : Node3D
{
    /// The pause between one action finishing and the next beginning. Long
    /// enough to read what happened and see it coming — combat is meant to be
    /// watched, not raced through.
    [Export] public float BeatSeconds { get; set; } = 3.0f;

    /// How long an action's own animation is given before the countdown starts.
    /// Bookkeeping events (round markers, decision points) skip both.
    [Export] public float ActionSeconds { get; set; } = 1.0f;

    /// How long the briefing and the closing summary hold on screen.
    [Export] public float CardSeconds { get; set; } = 4.0f;

    /// Start the next delve when one ends, so the scene is never a still frame
    /// when you come back to it.
    [Export] public bool Loop { get; set; } = true;

    /// Take the bank/push decision automatically — always pushing deeper — so
    /// the scene runs a full chain to the mini-boss unattended. With this off,
    /// the run waits at each choice for Space (push) or B (bank), which is the
    /// press-your-luck beat as designed.
    [Export] public bool AutoPush { get; set; } = false;

    /// Whether an attacker crosses the floor to its target, or stays on its
    /// mark and plays the attack in place. Contact timing, impact frames,
    /// shake, sparks and damage numbers are identical either way — this only
    /// changes whether the body moves.
    ///
    /// Travelling currently reads as sliding, because the mesh translates while
    /// playing a stationary attack animation: nothing about the body sells the
    /// movement. Exposed so both can be judged against the same fight rather
    /// than argued about.
    [Export] public bool AttackersTravel { get; set; } = false;

    /// Where the run is. The fight is one phase of four, not the whole scene —
    /// the briefing, the bank/push choice and the closing summary are beats of
    /// the delve and each holds the stage on its own terms.
    private enum Phase { Briefing, Fighting, Choice, Summary }

    private StageActor _party = null!;
    private StageActor _foe = null!;
    private CombatHud _hud = null!;
    private DamageNumbers _numbers = null!;
    private Color _petEnergy, _foeEnergy;
    private Vector3 _foeRestScale;

    private readonly Queue<CombatEvent> _pending = new();
    private ImpactFrames _impact = null!;
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
    private Approach _approach = Approach.Clever;

    private readonly PetCombatRates _rates = new();
    /// The living pet. Every delve is built from it and every resolution is
    /// written back into it, so a run starts from the condition and gear the
    /// last one left behind.
    private PetState _state = PetState.NewPet();
    private Delve _delve = null!;
    private CombatEncounter _encounter = null!;
    private Phase _phase = Phase.Briefing;

    private string _petName = "";
    private string _foeName = "";
    private int _petHP, _petMaxHP, _foeHP, _foeMaxHP;
    private string _line = "";
    private string _status = "";

    public override void _Ready()
    {
        _party = new StageActor(GetNode<Node3D>("Party"), "tempest_ram", ActorAnimations.TempestRam);
        _foe = new StageActor(GetNode<Node3D>("Foe"), "forest_flicker", ActorAnimations.ForestFlicker);
        _foeRestScale = _foe.Root.Scale;
        _petEnergy = FamilyEnergy.Of(_state.Family);
        _foeEnergy = FamilyEnergy.Of(FamilyEnergy.For(_foe.ModelName));
        _numbers = new DamageNumbers(this);
        _lunge.Travel = AttackersTravel;
        _impact = new ImpactFrames(GetNode<Camera3D>("Stage/StageCamera"), this, this);
        BeginDelve();
    }

    // MARK: - Driving the delve

    /// Builds a delve from the pet as it currently stands and opens on the
    /// briefing. The seed comes off the clock so each run differs; a delve
    /// launched from the app seeds from the save state plus a per-delve nonce
    /// instead, which is what makes a run reproducible from a bug report.
    private void BeginDelve()
    {
        var pet = Combatant.Pet(_state, _rates);
        _petName = pet.Name;
        _petMaxHP = pet.MaxHP;
        _petHP = pet.CurrentHP;
        _petEnergy = FamilyEnergy.Of(_state.Family);

        ulong seed = (ulong)Time.GetTicksUsec();
        _delve = Delve.CacheWarrenDelve(
            pet, _rates.CombatEffectiveness(_state.Needs), _rates, seed, _state.OwnedItems);
        _delve.Descend();

        _hud ??= new CombatHud(this, _petName, _petMaxHP, _petEnergy,
                               _delve.CurrentFoe!.Name, _delve.CurrentFoe!.MaxHP,
                               _foeEnergy);
        _hud.Reset(_petMaxHP, _delve.CurrentFoe!.MaxHP);
        _hud.SetHP(_petHP, _delve.CurrentFoe!.MaxHP);

        _pending.Clear();
        _lunge.Cancel();
        _party.Play(ActorAction.Idle, loop: true);
        _foe.Play(ActorAction.Idle, loop: true);

        _phase = Phase.Briefing;
        _cardTimer = CardSeconds;
        _line = $"{_petName} descends into the Cache Warren";
        _status = $"Lv {_state.Level}  ·  {_delve.TotalEncounters} encounters  ·  {_approach}";
        UpdateReadout();
    }

    /// Resolves the current encounter and hands its log to playback. The pet
    /// enters at the HP the delve carried in, not at full — that carry is the
    /// whole reason pushing deeper is a gamble.
    private void StartEncounter()
    {
        var foe = _delve.CurrentFoe!;
        _encounter = _delve.MakeEncounter(_approach)!;
        _encounter.RunToCompletion();

        _foeName = foe.Name;
        _foeMaxHP = foe.MaxHP;
        _foeHP = _foeMaxHP;
        _petHP = _delve.CarriedHP;

        var (scale, energy) = PresenceFor(foe.Name);
        _foe.Root.Scale = _foeRestScale * scale;
        _foeEnergy = energy;
        _hud.SetFoe(foe.Name, _foeMaxHP, _foeEnergy);
        _hud.Reset(_petMaxHP, _foeMaxHP);
        _hud.SetHP(_petHP, _foeHP);

        _lunge.Cancel();
        _pending.Clear();
        foreach (var e in _encounter.Log) _pending.Enqueue(e);
        _round = 0;
        _beatTimer = 0;
        _actionTimer = 0;
        _line = "";
        _party.Play(ActorAction.Idle, loop: true);
        _foe.Play(ActorAction.Idle, loop: true);
        _phase = Phase.Fighting;
        UpdateReadout();
    }

    /// The log has played out. The delve decides what that meant: a retreat, a
    /// finished chain, or the bank/push choice.
    private void FinishEncounter()
    {
        _delve.RecordOutcome(_encounter);
        switch (_delve.Status.Kind)
        {
            case DelveStatusKind.AwaitingPushChoice:
                _phase = Phase.Choice;
                _line = _delve.LastDrop is Item drop
                    ? $"{_foeName} down — {drop.DisplayName()} recovered"
                    : $"{_foeName} down";
                _status = AutoPush
                    ? "Pushing deeper..."
                    : "[Space] push deeper   ·   [B] bank and leave";
                _cardTimer = AutoPush ? CardSeconds * 0.5 : 0;
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

        string headline = resolution.BossDefeated ? "Delve complete"
                        : resolution.Banked ? "Banked"
                        : "Retreated";
        var spoils = new List<string>();
        foreach (var item in resolution.ItemsDropped) spoils.Add(item.DisplayName());
        _line = $"{headline} — {resolution.ClearedCount}/{_delve.TotalEncounters} cleared, "
              + $"+{resolution.XPGained:0} XP"
              + (spoils.Count > 0 ? $", {string.Join(", ", spoils)}" : "");
        _status = $"Exit: {resolution.Tier.RawValue()}  ·  Lv {_state.Level}  ·  {_state.Mood}";
        _phase = Phase.Summary;
        _cardTimer = CardSeconds;
    }

    /// Bank or push. Both are guarded by the delve itself, so a stray keypress
    /// outside the choice does nothing.
    public override void _UnhandledInput(InputEvent @event)
    {
        if (_phase != Phase.Choice || AutoPush) return;
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
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
    }

    // MARK: - Playback

    public override void _Process(double delta)
    {
        // Impact reactions animate on real time. The freeze applies to the
        // fight, not to the shake and dust working their way out of it.
        _impact.Tick(delta);
        _hud?.Tick(delta);

        // The attacker freezes at the point of contact during hit-stop rather
        // than sliding through the held frame.
        _lunge.Tick(delta, _impact.IsHitStopped ? 0 : 1);

        if (_impact.IsHitStopped) return;

        if (_phase != Phase.Fighting)
        {
            TickCard(delta);
            return;
        }

        // Phase one: the action is playing. No countdown — the attack is not
        // the wait.
        if (_actionTimer > 0)
        {
            _actionTimer -= delta;
            if (_actionTimer <= 0) _beatTimer = _beatLength;
            else { _hud?.ClearBeat(); return; }
        }

        // Phase two: counting down to the next action.
        if (_beatTimer > 0)
        {
            _beatTimer -= delta;
            _hud?.SetBeat(1.0 - _beatTimer / _beatLength, _beatTimer);
            if (_beatTimer > 0) return;
        }

        if (_pending.Count == 0)
        {
            _hud?.ClearBeat();
            FinishEncounter();
            return;
        }

        var next = _pending.Dequeue();
        if (Apply(next))
        {
            // An attack beat runs until its own animation has played out, so a
            // long wind-up is never cut off by the countdown starting early.
            _actionTimer = _lunge.IsBusy
                ? System.Math.Max(ActionSeconds, _lastLungeDuration + 0.12)
                : ActionSeconds;
            _beatLength = BeatSeconds;
            _hud?.ClearBeat();
        }
        UpdateReadout();
    }

    /// The between-fight beats. A choice with AutoPush off has no timer and
    /// simply waits for the player.
    private void TickCard(double delta)
    {
        if (_cardTimer <= 0) return;
        _cardTimer -= delta;
        if (_cardTimer > 0) return;
        switch (_phase)
        {
            case Phase.Briefing:
                StartEncounter();
                break;
            case Phase.Choice:
                _delve.PushDeeper();
                StartEncounter();
                break;
            case Phase.Summary:
                if (Loop) BeginDelve();
                break;
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
    private void ScheduleImpact(
        StageActor attacker, StageActor defender, bool toFoe,
        StrikeOutcome outcome, bool isSignature = false)
    {
        int maxHP = toFoe ? _foeMaxHP : _petMaxHP;
        double severity = maxHP > 0 ? (double)outcome.Damage / maxHP : 0;
        var direction = defender.Root.Position - attacker.Root.Position;
        _lastLungeDuration = AttackLunge.DurationFor(attacker.AttackImpactDelay());
        _lunge.Begin(attacker, defender, attacker.AttackImpactDelay(), onContact: () =>
        {
            ApplyDamage(toFoe, outcome.Damage);
            var energy = toFoe ? _petEnergy : _foeEnergy;
            _impact.Strike(defender, direction, severity, outcome.DidCrit || isSignature, energy);
            _numbers.Spawn(defender.Root.Position, outcome.Damage, energy,
                           outcome.DidCrit || isSignature);
            UpdateReadout();
        });
    }

    /// A miss still commits — the attacker goes in and comes back with nothing
    /// to show for it, which is what makes a miss read as a miss rather than as
    /// a skipped turn.
    private void ScheduleWhiff(StageActor attacker, StageActor defender)
    {
        _lastLungeDuration = AttackLunge.DurationFor(attacker.AttackImpactDelay());
        _lunge.Begin(attacker, defender, attacker.AttackImpactDelay(),
                     onContact: () => _numbers.SpawnMiss(defender.Root.Position));
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
                if (x.Outcome.DidHit)
                {
                    ScheduleImpact(attacker, defender, petAttacking, x.Outcome);
                    _line = $"{x.Attacker} {(x.Outcome.DidCrit ? "crits" : "strikes")} for {x.Outcome.Damage}";
                }
                else
                {
                    ScheduleWhiff(attacker, defender);
                    _line = $"{x.Attacker} misses";
                }
                return true;
            }
            case CombatEvent.Signature x:
                _party.Play(ActorAction.Signature);
                ScheduleImpact(_party, _foe, true, x.Outcome, isSignature: true);
                _line = $"{x.Attacker} unleashes for {x.Outcome.Damage}";
                return true;

            // The Monolith's telegraphed slam — the foe's answer to a signature,
            // and the reason the boss encounter reads differently from the three
            // above it.
            case CombatEvent.Slammed x:
                _foe.Play(ActorAction.Signature);
                if (x.Outcome.DidHit)
                {
                    ScheduleImpact(_foe, _party, false, x.Outcome, isSignature: true);
                    _line = $"{x.Attacker} slams for {x.Outcome.Damage}";
                }
                else
                {
                    ScheduleWhiff(_foe, _party);
                    _line = $"{x.Attacker} slams — {x.Defender} slips it";
                }
                return true;

            // A wind-up with no contact. It earns a beat precisely because the
            // pause is the information: the slam is coming.
            case CombatEvent.Telegraphed x:
                _line = $"{x.Who} winds up";
                return true;

            case CombatEvent.Hardened x:
                _line = $"{x.Who} hardens (+{x.GuardGain} Guard)";
                return true;

            case CombatEvent.Braced x:
                _party.Play(ActorAction.Idle, loop: true);
                _petHP = System.Math.Min(_petMaxHP, _petHP + x.Regen);
                _line = $"{x.Who} braces (+{x.Regen})";
                return true;

            case CombatEvent.Grabbed x:
                _foe.Play(ActorAction.Attack);
                _line = $"{x.Attacker} snares {x.Target} (-{x.AgilityLoss} Agility)";
                return true;

            case CombatEvent.Phased x:
                _foe.Play(ActorAction.Idle, loop: true);
                _line = $"{x.Who} blurs aside";
                return true;

            case CombatEvent.Defeated x:
                (x.Who == _petName ? _party : _foe).Play(ActorAction.Downed);
                _line = $"{x.Who} is down";
                return true;

            case CombatEvent.EncounterEnded x:
                _line = x.Victory ? "Victory" : "Defeat";
                return true;

            case CombatEvent.RoundBegan x:
                _round = x.Round;
                return false;

            default:
                return false;   // encounter markers, decision points
        }
    }

    private void ApplyDamage(bool toFoe, int amount)
    {
        if (toFoe) _foeHP = System.Math.Max(0, _foeHP - amount);
        else _petHP = System.Math.Max(0, _petHP - amount);
    }

    private void UpdateReadout()
    {
        _hud.SetHP(_petHP, _foeHP);
        _hud.SetNarration(_line);
        if (_phase == Phase.Fighting)
        {
            _status = $"Encounter {_delve.EncounterNumber}/{_delve.TotalEncounters}"
                    + $"  ·  Round {_round}  ·  {_approach}";
        }
        _hud.SetStatus(_status);
    }

    /// Stand-in staging for the three foes with no model yet: the Flicker's mesh
    /// at a different size and energy colour, so a Scamp does not read as a
    /// Monolith. Placeholder on purpose — the chain and its pacing are what this
    /// scene is for, and they are judgeable now rather than after four bakes.
    private static (float Scale, Color Energy) PresenceFor(string foeName) => foeName switch
    {
        "Dungeon Scamp" => (0.55f, FamilyEnergy.Glitchkin),
        "Snag" => (1.15f, FamilyEnergy.Wildkin),
        "Flicker" => (1.0f, FamilyEnergy.Wildkin),
        "Monolith" => (1.75f, FamilyEnergy.Relicborn),
        _ => (1.0f, FamilyEnergy.Bloomglass),
    };
}
