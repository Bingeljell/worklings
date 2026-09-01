using Godot;
using System.Collections.Generic;
using System.Linq;
using Worklings.Core.Combat;

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

    /// Which action each character plays, matched on substring — names differ
    /// per character (RamIdle_Breathe_Paw vs ForestFlicker_Idle_BreatheLook) and
    /// are still being trimmed, so exact names would be brittle.
    private static readonly string[] IdleHints = { "Idle", "Rest", "Breathe" };
    private static readonly string[] AttackHints = { "Headbutt_Power_Impact", "Attack", "Headbutt", "Swipe" };
    private static readonly string[] WinceHints = { "Wince", "Damage", "HitReact" };

    private Node3D _party = null!;
    private Node3D _foe = null!;
    private Label _readout = null!;
    private AnimationPlayer? _partyAnim;
    private AnimationPlayer? _foeAnim;

    private readonly Queue<CombatEvent> _pending = new();
    private double _beatTimer;
    private string _petName = "Ram";
    private string _foeName = "Flicker";
    private int _petHP, _petMaxHP, _foeHP, _foeMaxHP;
    private string _line = "";

    public override void _Ready()
    {
        _party = GetNode<Node3D>("Party");
        _foe = GetNode<Node3D>("Foe");
        _partyAnim = FindPlayer(_party);
        _foeAnim = FindPlayer(_foe);
        _readout = BuildReadout();
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

        _pending.Clear();
        foreach (var e in encounter.Log) _pending.Enqueue(e);
        _beatTimer = 0;
        _line = "";
        Play(_partyAnim, IdleHints);
        Play(_foeAnim, IdleHints);
        UpdateReadout();
    }

    public override void _Process(double delta)
    {
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
                Play(petAttacking ? _partyAnim : _foeAnim, AttackHints);
                if (x.Outcome.DidHit)
                {
                    Play(petAttacking ? _foeAnim : _partyAnim, WinceHints);
                    ApplyDamage(petAttacking, x.Outcome.Damage);
                    _line = $"{x.Attacker} {(x.Outcome.DidCrit ? "crits" : "strikes")} for {x.Outcome.Damage}";
                }
                else
                {
                    _line = $"{x.Attacker} misses";
                }
                return true;
            }
            case CombatEvent.Signature x:
                Play(_partyAnim, AttackHints);
                Play(_foeAnim, WinceHints);
                ApplyDamage(true, x.Outcome.Damage);
                _line = $"{x.Attacker} unleashes for {x.Outcome.Damage}";
                return true;

            case CombatEvent.Braced x:
                Play(_partyAnim, IdleHints);
                _petHP = System.Math.Min(_petMaxHP, _petHP + x.Regen);
                _line = $"{x.Who} braces (+{x.Regen})";
                return true;

            case CombatEvent.Grabbed x:
                Play(_foeAnim, AttackHints);
                _line = $"{x.Attacker} snares {x.Target} (-{x.AgilityLoss} Agility)";
                return true;

            case CombatEvent.Phased x:
                Play(_foeAnim, IdleHints);
                _line = $"{x.Who} blurs aside";
                return true;

            case CombatEvent.Defeated x:
                Play(x.Who == _petName ? _partyAnim : _foeAnim, WinceHints);
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

    private static void Play(AnimationPlayer? player, string[] hints)
    {
        if (player == null) return;
        var names = player.GetAnimationList();
        foreach (var hint in hints)
        {
            var match = names.FirstOrDefault(n => n.Contains(hint, System.StringComparison.OrdinalIgnoreCase));
            if (match != null) { player.Play(match); return; }
        }
    }

    private static AnimationPlayer? FindPlayer(Node node)
    {
        if (node is AnimationPlayer p) return p;
        foreach (var child in node.GetChildren())
        {
            var found = FindPlayer(child);
            if (found != null) return found;
        }
        return null;
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
