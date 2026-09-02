using Worklings.Core.Progression;
using System.Collections.Generic;
using System.Linq;

namespace Worklings.Core.Pet;

/// The persisted Workling: identity, care needs, progression, and gear.
///
/// Immutable. Every change goes through a wither that copies the whole state,
/// which is deliberate — see Advanced below for what happened the one time
/// something built a PetState from scratch instead.
///
/// Ported from Sources/CompanionCore/PetState.swift.
///
/// **Not ported here: persistence.** Swift's `init(from decoder:)` folds the
/// pre-v2 flat daily fields into the unified tallies and supplies defaults for
/// every field added since. That is the file store's job, and PetStateFileStore
/// is its own unported file; porting the decode without a JSON layer to verify
/// it against would be writing untested migration code. The rules it must honour
/// are recorded in docs/engineering/godot-port-status.md so they are not lost.
public sealed class PetState : System.IEquatable<PetState>
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; }
    public string Name { get; }
    public PetFamily Family { get; }
    public PetNeeds Needs { get; }
    public PetPreferences Preferences { get; }
    public System.DateTimeOffset LastUpdatedAt { get; }
    public System.DateTimeOffset? LastWorkLogAt { get; }

    /// Log Work fairness bookkeeping: how many logs were credited on its stored
    /// day. Read through DailyTally.Current so a stale count is ignored rather
    /// than proactively reset.
    public DailyTally<int> WorkLog { get; }

    public double TotalXP { get; }
    public PetClass PetClass { get; }
    public PetStats Stats { get; }

    /// XP granted on its stored day, keyed by XPSource.RawValue(). Same
    /// day-scoped semantics as WorkLog.
    public DailyTally<Dictionary<string, double>> DailyXP { get; }

    /// How many grants each source made on its stored day, keyed by
    /// XPSource.RawValue(). Drives per-source diminishing returns (the Nth
    /// milestone commit is worth less than the first). Same day-scoped semantics
    /// as DailyXP; the two are updated together.
    public DailyTally<Dictionary<string, int>> DailyEventCount { get; }

    /// Every item the Workling owns, in the order acquired. Duplicates are
    /// collapsed — an item is a thing you have or don't, not a stack.
    public IReadOnlyList<Item> OwnedItems { get; }

    /// What's currently equipped, one item per slot. Only ever *which* items —
    /// their stat effect is computed at read time by EffectiveStats, so no gear
    /// change ever rewrites a persisted stat.
    public Loadout Loadout { get; }

    /// A new Workling starts with one modest item so the gear UI is never empty
    /// on first look, and it starts equipped so the effect is visible without a
    /// first trip through the inventory. Which item is a knob.
    public const Item StarterItem = Item.RubberDuck;

    public static IReadOnlyList<Item> StarterItems => new[] { StarterItem };

    public static Loadout StarterLoadout => Loadout.Empty.Equipping(StarterItem);

    public PetState(
        string name,
        PetNeeds needs,
        PetPreferences preferences,
        System.DateTimeOffset lastUpdatedAt,
        int schemaVersion = CurrentSchemaVersion,
        PetFamily family = PetFamily.Wildkin,
        System.DateTimeOffset? lastWorkLogAt = null,
        DailyTally<int>? workLog = null,
        double totalXP = 0,
        PetClass petClass = PetClass.Wellspring,
        PetStats? stats = null,
        DailyTally<Dictionary<string, double>>? dailyXP = null,
        DailyTally<Dictionary<string, int>>? dailyEventCount = null,
        IReadOnlyList<Item>? ownedItems = null,
        Loadout? loadout = null)
    {
        SchemaVersion = schemaVersion;
        Name = name;
        Family = family;
        Needs = needs;
        Preferences = preferences;
        LastUpdatedAt = lastUpdatedAt;
        LastWorkLogAt = lastWorkLogAt;
        WorkLog = workLog ?? new DailyTally<int>(0);
        TotalXP = System.Math.Max(totalXP, 0);
        PetClass = petClass;
        Stats = stats ?? PetStats.Starting;
        DailyXP = dailyXP ?? new DailyTally<Dictionary<string, double>>(
            new Dictionary<string, double>());
        DailyEventCount = dailyEventCount ?? new DailyTally<Dictionary<string, int>>(
            new Dictionary<string, int>());

        // Owning an item is a yes/no, so a repeated entry is collapsed rather
        // than stacked — first-acquired order is kept so the inventory reads as
        // a history.
        var seen = new HashSet<Item>();
        var deduped = new List<Item>();
        foreach (var item in ownedItems ?? StarterItems)
        {
            if (seen.Add(item))
            {
                deduped.Add(item);
            }
        }
        OwnedItems = deduped;

        // You can only wear what you own. Enforcing it here means no caller can
        // construct a state that equips a phantom item, however the fields were
        // written — including by a hand-edited save.
        var requested = loadout ?? StarterLoadout;
        var settled = Loadout.Empty;
        foreach (var slot in ItemSlotExtensions.AllCases)
        {
            var item = requested[slot];
            if (item.HasValue && seen.Contains(item.Value))
            {
                settled = settled.Equipping(item.Value, slot);
            }
        }
        Loadout = settled;
    }

    public static PetState NewPet(
        string name = "Pixel",
        PetFamily family = PetFamily.Wildkin,
        System.DateTimeOffset? now = null) =>
        new PetState(
            name: name,
            family: family,
            needs: new PetNeeds(hunger: 15, energy: 80, happiness: 70, trust: 50),
            preferences: new PetPreferences(PetFood.Berries, PetPlayActivity.Puzzle),
            lastUpdatedAt: now ?? System.DateTimeOffset.Now);

    /// The single full-field copy for the withers below, so adding a stored
    /// field means updating exactly one copy site instead of one per wither.
    ///
    /// A null argument means "carry the existing value forward", which is why
    /// this cannot clear LastWorkLogAt — matching Swift, where the same
    /// `Date? = nil` default has the same limitation.
    private PetState Replacing(
        int? schemaVersion = null,
        string? name = null,
        PetFamily? family = null,
        PetClass? petClass = null,
        PetNeeds? needs = null,
        System.DateTimeOffset? lastUpdatedAt = null,
        System.DateTimeOffset? lastWorkLogAt = null,
        DailyTally<int>? workLog = null,
        double? totalXP = null,
        PetStats? stats = null,
        DailyTally<Dictionary<string, double>>? dailyXP = null,
        DailyTally<Dictionary<string, int>>? dailyEventCount = null,
        IReadOnlyList<Item>? ownedItems = null,
        Loadout? loadout = null) =>
        new PetState(
            schemaVersion: schemaVersion ?? SchemaVersion,
            name: name ?? Name,
            family: family ?? Family,
            needs: needs ?? Needs,
            preferences: Preferences,
            lastUpdatedAt: lastUpdatedAt ?? LastUpdatedAt,
            lastWorkLogAt: lastWorkLogAt ?? LastWorkLogAt,
            workLog: workLog ?? WorkLog,
            totalXP: totalXP ?? TotalXP,
            petClass: petClass ?? PetClass,
            stats: stats ?? Stats,
            dailyXP: dailyXP ?? DailyXP,
            dailyEventCount: dailyEventCount ?? DailyEventCount,
            ownedItems: ownedItems ?? OwnedItems,
            loadout: loadout ?? Loadout);

    /// The simulation's write-back: everything PetBrain moves on a tick, on a
    /// copy that carries every other field forward untouched.
    ///
    /// This exists so the brain never builds a PetState from scratch. It used
    /// to, and the constructor's defaults quietly made that a *reset* of
    /// anything it forgot to pass — which is exactly how every piece of gear won
    /// in a delve was handed back on the next needs tick, seconds after the drop
    /// card said it was yours.
    internal PetState Advanced(
        PetNeeds needs,
        System.DateTimeOffset at,
        System.DateTimeOffset? lastWorkLogAt = null,
        DailyTally<int>? workLog = null,
        double? totalXP = null,
        PetStats? stats = null,
        DailyTally<Dictionary<string, double>>? dailyXP = null,
        DailyTally<Dictionary<string, int>>? dailyEventCount = null) =>
        Replacing(
            needs: needs,
            lastUpdatedAt: at,
            lastWorkLogAt: lastWorkLogAt,
            workLog: workLog,
            totalXP: totalXP,
            stats: stats,
            dailyXP: dailyXP,
            dailyEventCount: dailyEventCount);

    /// A copy with adjusted needs and/or added XP. Combat-agnostic — the dungeon
    /// layer computes the deltas and calls this — so the outcome write-back stays
    /// out of PetState. XP only ever adds; needs clamp themselves.
    public PetState Applying(PetNeeds? needs = null, double addingXP = 0) =>
        Replacing(needs: needs, totalXP: TotalXP + System.Math.Max(0, addingXP));

    /// Restamps the schema version, preserving every field. Used by the file
    /// store to finish migrating a loaded older save to the current version; the
    /// field-level upgrade already happened during decode.
    internal PetState UpgradedToSchema(int version) => Replacing(schemaVersion: version);

    public PetState SelectingFamily(PetFamily family) => Replacing(family: family);

    /// Class is freely reassignable, the same way family is — there is nothing
    /// yet (no ability trees, no gear) that a class swap would need to protect.
    /// Stat growth already earned never changes; only future growth follows the
    /// new class's signature stat.
    public PetState SelectingClass(PetClass petClass) => Replacing(petClass: petClass);

    // Gear

    /// Adds an item to the inventory. Acquiring one already owned is a no-op —
    /// items are owned or not, never stacked — so a repeated drop is harmless.
    public PetState Acquiring(Item item)
    {
        if (OwnedItems.Contains(item))
        {
            return this;
        }
        var next = new List<Item>(OwnedItems) { item };
        return Replacing(ownedItems: next);
    }

    /// Equips `item` in `slot`, or empties the slot when it's null. Returns the
    /// state unchanged if the item isn't owned or doesn't belong in that slot,
    /// so a caller can attempt an equip without pre-validating it.
    public PetState Equipping(Item? item, ItemSlot slot)
    {
        if (item.HasValue && !OwnedItems.Contains(item.Value))
        {
            return this;
        }
        var updated = Loadout.Equipping(item, slot);
        if (updated.Equals(Loadout))
        {
            return this;
        }
        return Replacing(loadout: updated);
    }

    /// Equips `item` into the slot it belongs to.
    public PetState Equipping(Item item) => Equipping(item, item.Slot());

    public PetState ClearingSlot(ItemSlot slot) => Equipping(null, slot);

    /// Puts the inventory back to what a brand-new Workling carries: the starter
    /// item, equipped, and nothing else.
    ///
    /// This exists because drops are deliberately scarce, so the whole gear loop
    /// can be *earned* out in four delves and then never seen again. That is
    /// right for play and wrong for testing it. Everything except gear (name,
    /// needs, XP, class, family) is preserved, so a reset costs no progress.
    public PetState ForgettingAcquiredItems() =>
        Replacing(ownedItems: StarterItems, loadout: StarterLoadout);

    /// The stat sheet everything downstream should read: the persisted base with
    /// equipped gear folded in. Combat builds its combatant from this, and the
    /// stats panel shows it — so the numbers a player sees and the numbers that
    /// fight are the same numbers.
    public PetStats EffectiveStats(ItemRates? rates = null) =>
        Stats.Effective(Loadout, Family, rates ?? ItemRates.Default);

    /// The owned items that fit `slot`, **best tier first** — with three tiers of
    /// everything, acquisition order would bury a hard-won Prime item under the
    /// junk that dropped before it. Ties break on the stat's declaration order so
    /// the list is stable rather than dependent on what dropped when.
    public List<Item> AvailableItems(ItemSlot slot)
    {
        var matching = new List<Item>();
        foreach (var item in OwnedItems)
        {
            if (item.Slot() == slot)
            {
                matching.Add(item);
            }
        }
        matching.Sort((lhs, rhs) =>
        {
            if (lhs.Tier() != rhs.Tier())
            {
                return rhs.Tier().Rank().CompareTo(lhs.Tier().Rank());
            }
            return StatOrder(lhs).CompareTo(StatOrder(rhs));
        });
        return matching;
    }

    private static int StatOrder(Item item) =>
        System.Array.IndexOf(PetStatKindExtensions.AllCases, item.Stat());

    public const int MaximumNameLength = 24;

    /// Length is counted in grapheme clusters, not UTF-16 units, because Swift's
    /// String.count is. Without this an emoji name that Swift accepts at 12
    /// characters is rejected here at 24+ — the two implementations disagreeing
    /// about the same save.
    public static bool IsValidName(string name)
    {
        string trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }
        return GraphemeCount(trimmed) <= MaximumNameLength;
    }

    private static int GraphemeCount(string s)
    {
        var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(s);
        int count = 0;
        while (enumerator.MoveNext())
        {
            count += 1;
        }
        return count;
    }

    /// Returns the pet unchanged if `name` isn't valid once trimmed, so a caller
    /// can attempt a rename without first duplicating the validation.
    public PetState Renamed(string name)
    {
        string trimmed = name.Trim();
        if (!IsValidName(trimmed))
        {
            return this;
        }
        return Replacing(name: trimmed);
    }

    /// Derived from TotalXP rather than stored, so level and XP can never
    /// disagree with each other. See PetProgressionCurve.
    public int Level => PetProgressionCurve.Level(TotalXP);

    public PetMood Mood
    {
        get
        {
            if (Needs.Hunger >= 75) return PetMood.Hungry;
            if (Needs.Energy <= 20) return PetMood.Sleepy;
            if (Needs.Trust <= 20) return PetMood.Wary;
            if (Needs.Happiness <= 30) return PetMood.Sad;
            if (Needs.Happiness >= 75 && Needs.Trust >= 60 && Needs.Hunger <= 40)
            {
                return PetMood.Happy;
            }
            return PetMood.Content;
        }
    }

    public bool Equals(PetState? other)
    {
        if (other is null) return false;
        if (SchemaVersion != other.SchemaVersion || Name != other.Name
            || Family != other.Family || !Needs.Equals(other.Needs)
            || !Preferences.Equals(other.Preferences)
            || LastUpdatedAt != other.LastUpdatedAt || LastWorkLogAt != other.LastWorkLogAt
            || !WorkLog.Equals(other.WorkLog) || TotalXP != other.TotalXP
            || PetClass != other.PetClass || !Stats.Equals(other.Stats)
            || !Loadout.Equals(other.Loadout)
            || OwnedItems.Count != other.OwnedItems.Count)
        {
            return false;
        }
        for (int i = 0; i < OwnedItems.Count; i++)
        {
            if (OwnedItems[i] != other.OwnedItems[i]) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as PetState);

    public override int GetHashCode() => System.HashCode.Combine(
        SchemaVersion, Name, Family, TotalXP, PetClass, Stats, Loadout, OwnedItems.Count);
}

/// A care interaction the player can perform. Swift models this as an enum with
/// associated values; C# has no such thing, so the payloads sit alongside a kind
/// and the factories below are the only intended way to build one.
public readonly record struct PetAction(
    PetActionKind Kind,
    PetFood? Food = null,
    PetPlayActivity? Play = null)
{
    public static PetAction Feed(PetFood food) => new(PetActionKind.Feed, Food: food);

    /// Named Playing, not Play, because the record already has a Play property
    /// holding the activity — the one place the C# shape of this enum shows.
    public static PetAction Playing(PetPlayActivity activity) =>
        new(PetActionKind.Play, Play: activity);
    public static readonly PetAction Pet = new(PetActionKind.Pet);
    public static readonly PetAction Sleep = new(PetActionKind.Sleep);
}

public enum PetActionKind
{
    Feed,
    Play,
    Pet,
    Sleep,
}

public enum PetReaction
{
    LikedFood,
    LovedFood,
    EnjoyedPlay,
    LovedPlay,
    Comforted,
    Rested,
    TooTiredToPlay,
    HappyToSeeYou,
    CelebratedTask,
    SharedSetback,
    ProudOfMilestone,
    GladYouAreBack,
    StartedWorking,
    TookABreak,
    WaitingOnYou,
    NoticedYouAreAway,
    LoggedWork,
}

public static class PetReactionExtensions
{
    /// The Swift `rawValue`, which appears in presentation lookups.
    public static string RawValue(this PetReaction reaction)
    {
        string s = reaction.ToString();
        return char.ToLowerInvariant(s[0]) + s.Substring(1);
    }
}

public readonly record struct PetInteractionResult(PetState State, PetReaction Reaction);
