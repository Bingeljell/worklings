using System.Collections.Generic;
using System.Linq;

namespace Worklings.Core.Combat;

/// One side of a fight: a name, its combat stats, and a transient HP pool. HP is
/// unrelated to the pet's condition needs — it resets each delve and can never
/// zero-out real wellbeing (see the closed loop in the dungeon design).
///
/// Ported from Sources/CompanionCore/Combat.swift. Swift's Combatant is a struct
/// with value semantics, copied freely. This is a class, because the resolver
/// takes the defender as `inout` and mutates its HP in place — reference
/// semantics express that more naturally in C# than passing `ref` through every
/// call. Callers that need a snapshot use Clone().
public sealed class Combatant
{
    public string Name { get; }
    public CombatStats Stats { get; }
    public int MaxHP { get; }
    public int CurrentHP { get; private set; }

    /// Active timed modifiers (Snare, Blur, Phase, Harden, ...). Empty by
    /// default, folded into EffectiveStats and ticked once per round.
    private readonly List<StatusEffect> _statuses = new();

    public IReadOnlyList<StatusEffect> Statuses => _statuses;

    public Combatant(string name, CombatStats stats, int maxHP, int currentHP)
    {
        Name = name;
        Stats = stats;
        MaxHP = System.Math.Max(maxHP, 0);
        CurrentHP = System.Math.Clamp(currentHP, 0, MaxHP);
    }

    public bool IsDefeated => CurrentHP <= 0;

    /// Share of HP remaining, 0...1 — the basis of the delve's exit tier.
    public double HPFraction => MaxHP > 0 ? (double)CurrentHP / MaxHP : 0;

    public void TakeDamage(int amount) =>
        CurrentHP = System.Math.Max(0, CurrentHP - System.Math.Max(0, amount));

    public void Heal(int amount) =>
        CurrentHP = System.Math.Min(MaxHP, CurrentHP + System.Math.Max(0, amount));

    /// Builds the pet's combatant from stats already scaled by condition
    /// effectiveness, with max HP derived from the scaled Vitality. Starts at
    /// full HP.
    ///
    /// Swift also has `pet(from: PetState)` overloads that unpack gear and
    /// condition. Those are deliberately not ported yet — they pull in PetState,
    /// Items and PetProgression, none of which combat needs. The caller supplies
    /// scaled stats until that slice lands.
    public static Combatant Pet(string name, CombatStats scaledStats, int vitality, PetCombatRates rates)
    {
        int maxHP = rates.MaxHP(vitality);
        return new Combatant(name, scaledStats, maxHP, maxHP);
    }

    /// Builds a foe from its stat block. Foe HP is authored directly (the
    /// bestiary lists it), not derived from Vitality, and condition never scales
    /// a foe — only the pet is cared for.
    public static Combatant Foe(string name, int maxHP, CombatStats stats) =>
        new(name, stats, maxHP, maxHP);

    public Combatant Clone()
    {
        var copy = new Combatant(Name, Stats, MaxHP, CurrentHP);
        copy._statuses.AddRange(_statuses);
        return copy;
    }

    // MARK: - Status effects

    /// Stats after active effects: Snare lowers Agility, Harden raises Guard.
    /// Power and Wit are untouched for now. The resolver reads these, never the
    /// raw block, so every timed modifier lands in one place.
    public CombatStats EffectiveStats
    {
        get
        {
            int agility = Stats.Agility;
            int defense = Stats.Defense;
            foreach (var effect in _statuses)
            {
                switch (effect.Kind)
                {
                    case StatusEffectKind.AgilityDebuff: agility -= effect.Magnitude; break;
                    case StatusEffectKind.GuardBuff: defense += effect.Magnitude; break;
                    case StatusEffectKind.Evasion:
                    case StatusEffectKind.Phasing: break;
                }
            }
            return new CombatStats(
                Stats.Power,
                System.Math.Max(0, defense),
                System.Math.Max(0, agility),
                Stats.Wit);
        }
    }

    /// Extra chance (0...1) for an incoming attack to miss, from Blur-style
    /// evasion.
    public double EvasionChance =>
        _statuses.Where(e => e.Kind == StatusEffectKind.Evasion).Sum(e => e.Magnitude) / 100.0;

    /// Whether a Phase is up, ready to slip the next incoming attack.
    public bool IsPhasing => _statuses.Any(e => e.Kind == StatusEffectKind.Phasing);

    /// Applies a new timed effect.
    public void Apply(StatusEffect effect) => _statuses.Add(effect);

    /// Ages every effect one round and drops the expired ones. Called once at
    /// the top of each round.
    public void TickStatuses()
    {
        for (int i = 0; i < _statuses.Count; i++) _statuses[i] = _statuses[i].Ticked();
        _statuses.RemoveAll(e => e.IsExpired);
    }

    /// Consumes one Phase after it has slipped an attack.
    public void ConsumePhasing()
    {
        int index = _statuses.FindIndex(e => e.Kind == StatusEffectKind.Phasing);
        if (index >= 0) _statuses.RemoveAt(index);
    }
}
