using System.Collections.Generic;

namespace Worklings.Core.Combat;

/// A foe's turn logic, as data. Each kind names an archetype and carries its
/// held knobs (from docs/design/dungeons.md); the encounter dispatches on it.
/// Every kind except Mindless also just Strikes until its own slice lands, so
/// adding one never changes an existing fight.
///
/// Swift models this as an enum with associated values. C# has no direct
/// equivalent, so it becomes a sealed record hierarchy — the same shape, matched
/// with `is` patterns at the dispatch site instead of `switch case .grabber`.
public abstract record FoeBehavior
{
    private FoeBehavior() { }

    /// Attacks every turn, no tactics. The Dungeon Scamp — teaches the loop.
    public sealed record Mindless : FoeBehavior;

    /// Mostly attacks; sometimes grabs to Snare (an Agility debuff). Snag.
    public sealed record Grabber(
        double SnareChance, int SnareMagnitude, int SnareDuration, int GrabCooldown) : FoeBehavior;

    /// Passive Blur (evasion) plus an occasional Phase, then over-extends into an
    /// Unleash opening. Flicker.
    public sealed record Evasive(int Evasion, double PhaseChance, int OpeningCooldown) : FoeBehavior;

    /// Slow; telegraphs a heavy Slam a turn ahead and Hardens (Guard) at HP-phase
    /// thresholds. Monolith.
    public sealed record Colossus(
        double SlamMultiplier, int TelegraphRounds,
        IReadOnlyList<double> HardenThresholds, int HardenGuard) : FoeBehavior;
}

/// A foe's authored stat block, turn logic, and kill reward. Data, not code —
/// the resolver reads the numbers and the encounter dispatches on Behavior.
/// Numbers are the held defaults from docs/design/dungeons.md.
public sealed record Foe(
    string Name,
    int MaxHP,
    CombatStats Stats,
    FoeBehavior Behavior,
    double RewardXP)
{
    public double RewardXP { get; } = System.Math.Max(RewardXP, 0);

    /// A fresh combatant at full HP for this foe.
    public Combatant MakeCombatant() => Combatant.Foe(Name, MaxHP, Stats);
}

/// The first delve's bestiary. A deliberate mechanic curve — a warm-up, a wall,
/// an accuracy test, then an endurance check — though v1 foes all just attack
/// until their abilities are built.
public static class CacheWarren
{
    public static readonly Foe Mote = new(
        "Dungeon Scamp", 30,
        new CombatStats(4, 1, 6, 1),
        new FoeBehavior.Mindless(),
        8);

    public static readonly Foe Snag = new(
        "Snag", 30,
        new CombatStats(7, 6, 3, 3),
        new FoeBehavior.Grabber(0.4, 3, 2, 2),
        20);

    public static readonly Foe Flicker = new(
        "Flicker", 18,
        new CombatStats(6, 2, 14, 4),
        new FoeBehavior.Evasive(30, 0.35, 3),
        25);

    public static readonly Foe Monolith = new(
        "Monolith", 90,
        new CombatStats(12, 12, 2, 2),
        new FoeBehavior.Colossus(2.0, 1, new[] { 0.66, 0.33 }, 4),
        100);

    /// The three regular encounters, in order, then the mini-boss.
    public static readonly IReadOnlyList<Foe> Encounters = new[] { Mote, Snag, Flicker };
    public static readonly Foe Boss = Monolith;
}
