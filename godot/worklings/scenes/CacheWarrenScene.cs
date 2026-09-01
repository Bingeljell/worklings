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
    /// Seconds each beat holds before the next event plays.
    [Export] public float BeatSeconds { get; set; } = 1.1f;

    /// Restart the fight from the top once it ends, so the scene is never a
    /// still frame when you come back to it.
    [Export] public bool Loop { get; set; } = true;

    private StageActor _party = null!;
    private StageActor _foe = null!;
    private Label _readout = null!;

    private readonly Queue<CombatEvent> _pending = new();
    private ImpactFrames _impact = null!;

    /// A landed blow queued to fire partway through the attacker's animation,
    /// at the frame the blow actually connects.
    private double _impactDelay;
    private System.Action? _impactAction;
    private double _beatTimer;
    private string _petName = "Ram";
    private string _foeName = "Flicker";
    private int _petHP, _petMaxHP, _foeHP, _foeMaxHP;
    private string _line = "";

    public override void _Ready()
    {
        _party = new StageActor(GetNode<Node3D>("Party"), "tempest_ram", ActorAnimations.TempestRam);
        _foe = new StageActor(GetNode<Node3D>("Foe"), "forest_flicker", ActorAnimations.ForestFlicker);
        _readout = BuildReadout();
        _impact = new ImpactFrames(GetNode<Camera3D>("Stage/StageCamera"), this);
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
        var encounter = new CombatEncounter(pet, foe, Approach.Clever, rates, seed);
        encounter.RunToCompletion();

        _petName = pet.Name;
        _foeName = foe.Name;
        _petMaxHP = pet.MaxHP;
        _foeMaxHP = foe.MaxHP;
        _petHP = _petMaxHP;
        _foeHP = _foeMaxHP;

        _impactAction = null;
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

        // A queued blow lands mid-animation, not when the clip starts.
        if (_impactAction != null)
        {
            _impactDelay -= delta;
            if (_impactDelay <= 0) { _impactAction(); _impactAction = null; }
        }

        if (_impact.IsHitStopped) return;

        _beatTimer -= delta;
        if (_beatTimer > 0) return;

        if (_pending.Count == 0)
        {
            if (Loop) StartFight();
            return;
        }

        var next = _pending.Dequeue();
        _beatTimer = Apply(next) ? BeatSeconds : 0.0;
        UpdateReadout();
    }

    /// Defers the hit reaction to the point in the attack animation where the
    /// blow connects. Damage lands then too, so the HP readout drops on contact
    /// rather than as the attacker starts moving.
    private void ScheduleImpact(
        StageActor attacker, StageActor defender, bool toFoe,
        StrikeOutcome outcome, bool isSignature = false)
    {
        int maxHP = toFoe ? _foeMaxHP : _petMaxHP;
        double severity = maxHP > 0 ? (double)outcome.Damage / maxHP : 0;
        var direction = defender.Root.Position - attacker.Root.Position;
        _impactDelay = attacker.AttackImpactDelay();
        _impactAction = () =>
        {
            ApplyDamage(toFoe, outcome.Damage);
            _impact.Strike(defender, direction, severity, outcome.DidCrit || isSignature);
            UpdateReadout();
        };
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

            default:
                return false;   // round markers, decision points, telegraphs
        }
    }

    private void ApplyDamage(bool toFoe, int amount)
    {
        if (toFoe) _foeHP = System.Math.Max(0, _foeHP - amount);
        else _petHP = System.Math.Max(0, _petHP - amount);
    }

    private Label BuildReadout()
    {
        var layer = new CanvasLayer();
        AddChild(layer);
        var label = new Label
        {
            Position = new Vector2(24, 20),
            Size = new Vector2(600, 120),
        };
        label.AddThemeFontSizeOverride("font_size", 22);
        layer.AddChild(label);
        return label;
    }

    private void UpdateReadout() =>
        _readout.Text = $"{_petName}  {_petHP}/{_petMaxHP}\n{_foeName}  {_foeHP}/{_foeMaxHP}\n{_line}";
}
