using Godot;
using System.Collections.Generic;
using System.Linq;
using Worklings.Core.Combat;
using Worklings.Core.Stage;

/// The Cache Warren scene: the first dungeon, playing a real encounter.
///
/// Named ...Scene rather than CacheWarren because Godot requires script classes
/// in the global namespace, where it would shadow the bestiary's
/// Worklings.Core.Combat.CacheWarren for every file importing that namespace.
///
/// The fight itself is resolved by CombatEncounter, which knows nothing about
/// rendering — it emits a stream of CombatEvents. This script's only job is to
/// consume that stream and turn each event into animation and text. That seam is
/// deliberate: the rules stay verifiable headlessly, and the renderer stays
/// swappable.
///
/// The whole fight resolves instantly at _Ready; playback is then a replay of
/// the recorded log at a watchable pace. That is a real property of the design
/// rather than a shortcut — a seeded encounter is fully determined the moment it
/// starts, so nothing is lost by resolving first and animating after.
public partial class CacheWarrenScene : Node3D
{
    /// The pause between one action finishing and the next beginning. Long
    /// enough to read what happened and see it coming — combat is meant to be
    /// watched, not raced through.
    [Export] public float BeatSeconds { get; set; } = 3.0f;

    /// How long an action's own animation is given before the countdown starts.
    /// Bookkeeping events (round markers, decision points) skip both.
    [Export] public float ActionSeconds { get; set; } = 1.0f;

    /// Restart the fight from the top once it ends, so the scene is never a
    /// still frame when you come back to it.
    [Export] public bool Loop { get; set; } = true;


    private StageActor _party = null!;
    private StageActor _foe = null!;
    private CombatHud _hud = null!;
    private DamageNumbers _numbers = null!;
    private Color _petEnergy, _foeEnergy;

    private readonly Queue<CombatEvent> _pending = new();
    private ImpactFrames _impact = null!;
    private readonly AttackLunge _lunge = new();
    /// A beat runs in two phases: the action plays, then the countdown to the
    /// next one. Separating them is what lets the countdown mean "next attack
    /// in 3s" rather than draining through the attack itself.
    private double _actionTimer;
    private double _beatTimer;
    private double _beatLength;
    private int _round;
    private Approach _approach = Approach.Clever;
    private string _petName = "Ram";
    private string _foeName = "Flicker";
    private int _petHP, _petMaxHP, _foeHP, _foeMaxHP;
    private string _line = "";

    public override void _Ready()
    {
        _party = new StageActor(GetNode<Node3D>("Party"), "tempest_ram", ActorAnimations.TempestRam);
        _foe = new StageActor(GetNode<Node3D>("Foe"), "forest_flicker", ActorAnimations.ForestFlicker);
        _petEnergy = FamilyEnergy.Of(FamilyEnergy.For(_party.ModelName));
        _foeEnergy = FamilyEnergy.Of(FamilyEnergy.For(_foe.ModelName));
        _numbers = new DamageNumbers(this);
        _impact = new ImpactFrames(GetNode<Camera3D>("Stage/StageCamera"), this, this);
        StartFight();
    }

    private void StartFight()
    {
        var rates = new PetCombatRates();

        // Stats stand in for a real PetState until that slice is ported; the
        // Flicker is the foe because it is the one the milestone scopes.
        var petStats = new CombatStats(power: 11, defense: 6, agility: 9, wit: 7);
        int petMax = rates.MaxHP(vitality: 7);
        var pet = new Combatant(_petName, petStats, petMax, petMax);
        var foe = Worklings.Core.Combat.CacheWarren.Flicker;

        // Seeded from the clock so each replay differs; a real delve seeds from
        // the save state plus a per-delve nonce instead.
        ulong seed = (ulong)Time.GetTicksUsec();
        var encounter = new CombatEncounter(pet, foe, _approach, rates, seed);
        encounter.RunToCompletion();

        _petName = pet.Name;
        _foeName = foe.Name;
        _petMaxHP = pet.MaxHP;
        _foeMaxHP = foe.MaxHP;
        _petHP = _petMaxHP;
        _foeHP = _foeMaxHP;

        _lunge.Cancel();
        _hud ??= new CombatHud(this, _petName, _petMaxHP, _petEnergy,
                               _foeName, _foeMaxHP, _foeEnergy);
        _hud.Reset(_petMaxHP, _foeMaxHP);
        _pending.Clear();
        foreach (var e in encounter.Log) _pending.Enqueue(e);
        _beatTimer = 0;
        _line = "";
        _party.Play(ActorAction.Idle, loop: true);
        _foe.Play(ActorAction.Idle, loop: true);
        UpdateReadout();
    }

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
            if (Loop) StartFight();
            return;
        }

        var next = _pending.Dequeue();
        if (Apply(next))
        {
            _actionTimer = _lunge.IsBusy
                ? System.Math.Max(ActionSeconds, AttackLunge.Duration + 0.12)
                : ActionSeconds;
            _beatLength = BeatSeconds;
            _hud?.ClearBeat();
        }
        UpdateReadout();
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
        _lunge.Begin(attacker, defender, onContact: () =>
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
    private void ScheduleWhiff(StageActor attacker, StageActor defender) =>
        _lunge.Begin(attacker, defender,
                     onContact: () => _numbers.SpawnMiss(defender.Root.Position));

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
                return false;   // decision points, telegraphs
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
        _hud.SetStatus($"Round {_round}  ·  {_approach}");
    }
}
