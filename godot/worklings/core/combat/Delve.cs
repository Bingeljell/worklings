using System.Collections.Generic;
using Worklings.Core.Pet;

namespace Worklings.Core.Combat;

/// Where a delve is right now. Swift models this as an enum with an associated
/// ExitTier on the completed case; the tier rides alongside the kind here and is
/// only meaningful when Kind is Completed.
public readonly record struct DelveStatus(DelveStatusKind Kind, ExitTier Tier = ExitTier.Downed)
{
    /// The opening briefing — not yet descended into the first fight.
    public static readonly DelveStatus Briefing = new(DelveStatusKind.Briefing);
    /// Currently fighting the encounter at Index.
    public static readonly DelveStatus InEncounter = new(DelveStatusKind.InEncounter);
    /// Cleared a non-boss encounter; the player chooses bank vs push.
    public static readonly DelveStatus AwaitingPushChoice = new(DelveStatusKind.AwaitingPushChoice);
    /// Ended on a win — banked early, or the mini-boss went down.
    public static DelveStatus Completed(ExitTier tier) => new(DelveStatusKind.Completed, tier);
    /// Ended on a loss — the pet was downed and retreated.
    public static readonly DelveStatus Retreated = new(DelveStatusKind.Retreated);
}

public enum DelveStatusKind
{
    Briefing,
    InEncounter,
    AwaitingPushChoice,
    Completed,
    Retreated,
}

/// A full delve: the chain of encounters that turns single fights into a
/// journey. It carries the pet's combat HP across encounters, regenerates a
/// little between them, accrues per-encounter XP, and — between fights — lets
/// the player **bank** (leave safely with what they've earned) or **push
/// deeper** toward the mini-boss (the press-your-luck beat). The exit tier and
/// the condition aftermath are computed **once**, from the HP the pet walks out
/// with, exactly as docs/design/dungeons.md specifies.
///
/// Like CombatEncounter, this is deterministic: the same pet, foes, and seed
/// replay the same delve, so it is fully checkable headless. The app drives it
/// by reading CurrentFoe / MakeEncounter, running that encounter (with
/// animation), then handing the result back via RecordOutcome and choosing
/// Bank / PushDeeper.
///
/// Ported from Sources/CompanionCore/Delve.swift. A class here where Swift has a
/// struct, matching CombatEncounter next door: the mutating methods drive a
/// stateful run, and copying it by accident mid-delve would be a bug rather than
/// a feature.
public sealed class Delve
{
    // Definition (fixed for the delve)

    /// The regular encounters, in order, then Boss as the final one.
    public IReadOnlyList<Foe> Foes { get; }
    public Foe Boss { get; }

    private readonly string _petName;
    private readonly CombatStats _petStats;
    private readonly int _petMaxHP;

    /// The condition->combat multiplier, fixed at entry so mid-delve care
    /// changes don't retroactively alter a fight in progress.
    private readonly double _effectiveness;

    private readonly PetCombatRates _rates;
    private readonly ulong _baseSeed;

    /// What the pet already owns, fixed at entry. Drops are chosen against this
    /// so a delve never awards a duplicate — the delve has to know it up front
    /// because drops are decided per encounter, not once at the end.
    private readonly IReadOnlyList<Item> _ownedAtEntry;

    // Running state

    /// Which encounter is current: 0..Foes.Count-1 are the regulars, Foes.Count
    /// is the boss.
    public int Index { get; private set; }

    /// The pet's combat HP carried into the current encounter.
    public int CarriedHP { get; private set; }

    /// Kill XP banked from cleared encounters so far (before any completion bonus).
    public double AccumulatedXP { get; private set; }

    /// How many encounters have been cleared.
    public int ClearedCount { get; private set; }

    private readonly List<Item> _drops = new();

    /// Gear won so far this delve, in the order it was won. Kept on a bank and
    /// on a retreat alike: those encounters were genuinely cleared, and taking
    /// the spoils back would make the shallow fights worthless again — which is
    /// the problem per-encounter drops exist to solve.
    public IReadOnlyList<Item> Drops => _drops;

    /// What the encounter just cleared gave up, or null if it gave up nothing
    /// (its tier is exhausted). Distinct from Drops[^1], which would keep
    /// reporting an older prize through a dry encounter and credit the wrong
    /// fight.
    public Item? LastDrop { get; private set; }

    public DelveStatus Status { get; private set; }

    public Delve(
        Combatant pet,
        IReadOnlyList<Foe> foes,
        Foe boss,
        double effectiveness,
        PetCombatRates rates,
        ulong baseSeed,
        IReadOnlyList<Item>? ownedItems = null)
    {
        Foes = foes;
        Boss = boss;
        _petName = pet.Name;
        _petStats = pet.Stats;
        _petMaxHP = pet.MaxHP;
        _effectiveness = System.Math.Clamp(effectiveness, 0, 1);
        _rates = rates;
        _baseSeed = baseSeed;
        _ownedAtEntry = ownedItems ?? System.Array.Empty<Item>();
        Index = 0;
        CarriedHP = pet.CurrentHP;
        AccumulatedXP = 0;
        ClearedCount = 0;
        LastDrop = null;
        Status = DelveStatus.Briefing;
    }

    /// The Cache Warren — the first dungeon's fixed chain — built from a pet
    /// combatant and its condition effectiveness. The one entry point the app
    /// and the checks both use.
    public static Delve CacheWarrenDelve(
        Combatant pet,
        double effectiveness,
        PetCombatRates rates,
        ulong baseSeed,
        IReadOnlyList<Item>? ownedItems = null) =>
        new Delve(pet, CacheWarren.Encounters, CacheWarren.Boss,
                  effectiveness, rates, baseSeed, ownedItems);

    // Reading the current position

    /// Every encounter in order, regulars then boss.
    public IReadOnlyList<Foe> AllFoes
    {
        get
        {
            var all = new List<Foe>(Foes) { Boss };
            return all;
        }
    }

    /// The foe for the current index, or null once the delve has ended.
    public Foe? CurrentFoe => Index < Foes.Count + 1
        ? (Index < Foes.Count ? Foes[Index] : Boss)
        : null;

    /// Whether the current encounter is the mini-boss (the last one).
    public bool IsBossEncounter => Index == Foes.Count;

    /// A 1-based position for narration ("encounter 2 of 4").
    public int EncounterNumber => Index + 1;
    public int TotalEncounters => Foes.Count + 1;

    /// Whether the delve has finished, either way.
    public bool IsFinished =>
        Status.Kind is DelveStatusKind.Completed or DelveStatusKind.Retreated;

    // Driving the delve

    /// Leaves the briefing and begins the first encounter.
    public void Descend()
    {
        if (Status.Kind != DelveStatusKind.Briefing) return;
        Status = DelveStatus.InEncounter;
    }

    /// Builds the CombatEncounter for the current index, starting the pet at its
    /// carried HP (a fresh combatant, so transient statuses from the last fight
    /// don't linger). Returns null unless an encounter is actually current.
    public CombatEncounter? MakeEncounter(Approach approach)
    {
        if (Status.Kind != DelveStatusKind.InEncounter || CurrentFoe is not Foe foe)
        {
            return null;
        }
        var pet = new Combatant(_petName, _petStats, _petMaxHP, CarriedHP);
        return new CombatEncounter(pet, foe, approach, _rates, EncounterSeed);
    }

    /// Records the result of the current encounter (which the caller ran to an
    /// ending). On a win, banks the foe's XP and either ends the delve (boss) or
    /// pauses for the bank/push choice (regular). On a loss, the delve retreats.
    public void RecordOutcome(bool petVictory, int petHPRemaining)
    {
        if (Status.Kind != DelveStatusKind.InEncounter || CurrentFoe is not Foe foe)
        {
            return;
        }
        CarriedHP = System.Math.Clamp(petHPRemaining, 0, _petMaxHP);
        if (!petVictory)
        {
            Status = DelveStatus.Retreated;
            return;
        }
        AccumulatedXP += foe.RewardXP;
        ClearedCount += 1;
        LastDrop = DropForClearedEncounter();
        if (LastDrop.HasValue)
        {
            _drops.Add(LastDrop.Value);
        }
        Status = IsBossEncounter
            ? DelveStatus.Completed(CurrentExitTier(victory: true))
            : DelveStatus.AwaitingPushChoice;
    }

    /// Convenience: record straight from a finished CombatEncounter.
    public void RecordOutcome(CombatEncounter encounter) =>
        RecordOutcome(encounter.Status.Equals(CombatStatus.PetVictory), encounter.Pet.CurrentHP);

    /// Bank the run: leave safely with everything earned, forfeiting the boss's
    /// completion bonus. Only valid at the bank/push choice.
    public void Bank()
    {
        if (Status.Kind != DelveStatusKind.AwaitingPushChoice) return;
        Status = DelveStatus.Completed(CurrentExitTier(victory: true));
    }

    /// Push deeper: regenerate a little HP and advance to the next encounter.
    public void PushDeeper()
    {
        if (Status.Kind != DelveStatusKind.AwaitingPushChoice) return;
        CarriedHP = System.Math.Min(_petMaxHP, CarriedHP + InterEncounterRegen);
        Index += 1;
        Status = DelveStatus.InEncounter;
    }

    // Resolving the finished delve

    /// The final result to write back, or null while the delve is still running.
    /// The condition delta and any completion bonus are applied **here, once** —
    /// per-encounter fights never move the pet's needs.
    public DelveResolution? Resolution(PetState state)
    {
        ExitTier tier;
        switch (Status.Kind)
        {
            case DelveStatusKind.Completed: tier = Status.Tier; break;
            case DelveStatusKind.Retreated: tier = ExitTier.Downed; break;
            default: return null;
        }

        bool bossDefeated = IsCompletedThroughBoss;
        double bonus = bossDefeated ? _rates.DelveCompletionXP : 0;
        double totalXP = AccumulatedXP + bonus;

        // Every cleared encounter has already yielded its own gear; what banking
        // forfeits is the *depth* — the completion bonus and the Prime item only
        // the mini-boss carries. That keeps press-your-luck teeth without making
        // the first three fights pay nothing.
        var delta = _rates.ExitConditionDelta(tier);
        // Fullness rises as hunger falls, so a Fullness gain is a hunger cut —
        // the same conversion the single-encounter write-back uses.
        var updatedNeeds = new PetNeeds(
            hunger: state.Needs.Hunger - delta.Fullness,
            energy: state.Needs.Energy + delta.Energy,
            happiness: state.Needs.Happiness + delta.Happiness,
            trust: state.Needs.Trust + delta.Trust);
        var updated = state.Applying(needs: updatedNeeds, addingXP: totalXP);
        foreach (var drop in _drops)
        {
            updated = updated.Acquiring(drop);
        }
        return new DelveResolution(
            updated, tier, totalXP, ClearedCount, bossDefeated,
            banked: !bossDefeated && tier != ExitTier.Downed,
            itemsDropped: new List<Item>(_drops));
    }

    /// The tier an encounter at `index` pays out. Depth *is* the reward curve:
    /// the mini-boss gives Prime, the last regular encounter before it gives
    /// Solid, and everything shallower gives Scavenged. Expressed relative to the
    /// end of the chain so a longer or shorter dungeon keeps the same shape.
    public ItemTier DropTier(int forEncounterAt)
    {
        int stepsFromBottom = Foes.Count - forEncounterAt;
        if (stepsFromBottom < 1) return ItemTier.Prime;
        if (stepsFromBottom == 1) return ItemTier.Solid;
        return ItemTier.Scavenged;
    }

    /// The item the just-cleared encounter yields: one of its tier that the pet
    /// doesn't already own and hasn't already won this run, chosen
    /// deterministically from the encounter's seed — so a replayed delve awards
    /// the same things, the same way every roll in it replays.
    ///
    /// Null when that tier is exhausted. Deliberately *not* falling back to
    /// another tier: a boss that hands out Scavenged junk because you own all the
    /// Prime gear reads as a bug, and an early fight that pays Prime because the
    /// Scavenged set is complete would gut the reason to push.
    private Item? DropForClearedEncounter()
    {
        var tier = DropTier(Index);
        var candidates = new List<Item>();
        foreach (var item in ItemExtensions.All(tier))
        {
            if (!Contains(_ownedAtEntry, item) && !_drops.Contains(item))
            {
                candidates.Add(item);
            }
        }
        if (candidates.Count == 0) return null;
        var generator = new SeededGenerator(EncounterSeed);
        // Swift's randomElement(using:) draws a single bounded word, so this has
        // to go through NextBelow rather than a double — a different draw would
        // pick a different item and consume the stream differently.
        return candidates[(int)generator.NextBelow((ulong)candidates.Count)];
    }

    private static bool Contains(IReadOnlyList<Item> list, Item item)
    {
        foreach (var i in list)
        {
            if (i == item) return true;
        }
        return false;
    }

    /// The seed for the current encounter — the base seed decorrelated by index
    /// so the four fights don't share a stream, while the whole delve stays a
    /// pure function of BaseSeed. Wrapping arithmetic, matching Swift's &+ / &*.
    private ulong EncounterSeed
    {
        get
        {
            unchecked
            {
                return _baseSeed + (ulong)Index * 0x9E3779B97F4A7C15UL;
            }
        }
    }

    /// The exit tier from the HP the pet currently holds (a win exit; a loss is
    /// always Downed, handled where the retreat is recorded).
    private ExitTier CurrentExitTier(bool victory)
    {
        double fraction = _petMaxHP > 0 ? (double)CarriedHP / _petMaxHP : 0;
        return ExitTierExtensions.ForOutcome(victory, fraction);
    }

    /// Whether the delve ended by clearing every encounter including the boss (as
    /// opposed to a voluntary early bank or a retreat).
    private bool IsCompletedThroughBoss =>
        Status.Kind == DelveStatusKind.Completed && ClearedCount >= Foes.Count + 1;

    /// Flat HP restored between encounters: the doc's fraction x maxHP x
    /// effectiveness — a rested, happy Workling recovers more mid-delve.
    private int InterEncounterRegen =>
        (int)System.Math.Round(
            _petMaxHP * _rates.InterEncounterRegenFraction * _effectiveness,
            System.MidpointRounding.AwayFromZero);
}

/// The write-back of a finished delve: the updated state (XP added, needs moved
/// **once**), the tier reached, and a little metadata for the end screen.
public sealed class DelveResolution
{
    public PetState State { get; }
    public ExitTier Tier { get; }
    /// Total XP granted, including the completion bonus when the boss fell.
    public double XPGained { get; }
    public int ClearedCount { get; }
    /// The mini-boss was defeated — the full delve was completed.
    public bool BossDefeated { get; }
    /// The player left voluntarily with a win (not a boss clear, not a retreat).
    public bool Banked { get; }
    /// Every piece of gear won on the way down, in the order it was won, already
    /// added to State. Empty when nothing dropped — which only happens when the
    /// pet already owns everything at the tiers it reached.
    public IReadOnlyList<Item> ItemsDropped { get; }

    public DelveResolution(
        PetState state, ExitTier tier, double xpGained, int clearedCount,
        bool bossDefeated, bool banked, IReadOnlyList<Item>? itemsDropped = null)
    {
        State = state;
        Tier = tier;
        XPGained = xpGained;
        ClearedCount = clearedCount;
        BossDefeated = bossDefeated;
        Banked = banked;
        ItemsDropped = itemsDropped ?? System.Array.Empty<Item>();
    }

    /// The mini-boss's own reward — the deepest, best thing recovered — which the
    /// end screen headlines. Null unless the boss actually fell.
    public Item? BossDrop =>
        BossDefeated && ItemsDropped.Count > 0
            ? ItemsDropped[ItemsDropped.Count - 1]
            : null;

    /// What was picked up before the boss, for the end screen's summary line.
    public IReadOnlyList<Item> ShallowDrops
    {
        get
        {
            if (BossDrop is null) return ItemsDropped;
            var head = new List<Item>();
            for (int i = 0; i < ItemsDropped.Count - 1; i++) head.Add(ItemsDropped[i]);
            return head;
        }
    }
}
