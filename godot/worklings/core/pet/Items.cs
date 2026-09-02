using Worklings.Core.Progression;

namespace Worklings.Core.Pet;

/// Gear: the three functional slots, the base item set, and the read-time fold
/// that turns base stats into sheet stats.
///
/// The founding rule (docs/design/progression.md, restated in
/// docs/design/items.md): gear modifies **effective** stats at **read-time** and
/// never touches the persisted base numbers. A save stores only *which* items
/// are owned and equipped; every stat consumer folds the modifiers in when it
/// reads. So gear arrives as pure computation — no stat migration, ever.
///
///     baseStats      (persisted; level + class growth)
///       + equipped item modifiers   = sheet stats   (Stats tab, and combat)
///       x condition effectiveness   = combat stats  (what the resolver reads)
///
/// Ported from Sources/CompanionCore/Items.swift.

/// The three v1 gear slots. They are **functional, not paper-doll**: Worklings
/// are creatures, so gear is never helmet/chestplate armour — it's the stuff a
/// working companion carries.
public enum ItemSlot
{
    Tool,
    Ward,
    Charm,
}

public static class ItemSlotExtensions
{
    public static readonly ItemSlot[] AllCases =
        { ItemSlot.Tool, ItemSlot.Ward, ItemSlot.Charm };

    public static string DisplayName(this ItemSlot slot) => slot switch
    {
        ItemSlot.Tool => "Tool",
        ItemSlot.Ward => "Ward",
        ItemSlot.Charm => "Charm",
        _ => slot.ToString(),
    };

    /// What the slot *is*, in one line. Surfaces read this so the fantasy lives
    /// with the slot rather than being retyped per screen.
    public static string Fantasy(this ItemSlot slot) => slot switch
    {
        ItemSlot.Tool => "The thing it works with — what you bring to the problem.",
        ItemSlot.Ward => "The thing that keeps it safe — what you hide behind on a bad day.",
        ItemSlot.Charm => "The thing that's just its own — personality, carried.",
        _ => "",
    };

    public static string RawValue(this ItemSlot slot) => slot.ToString().ToLowerInvariant();
}

/// How good a piece of gear is, which is the same question as **how deep you had
/// to go for it**. The shallow fights hand over scavenged junk, the last regular
/// encounter something solid, and the mini-boss the genuinely good stuff. The
/// gradient *is* the reward for pushing.
public enum ItemTier
{
    /// Off the early encounters — real, but barely.
    Scavenged,
    /// The workaday tier; the original base set, and what a new Workling starts with.
    Solid,
    /// Boss-only. The reason to walk past the bank prompt.
    Prime,
}

public static class ItemTierExtensions
{
    public static readonly ItemTier[] AllCases =
        { ItemTier.Scavenged, ItemTier.Solid, ItemTier.Prime };

    public static string DisplayName(this ItemTier tier) => tier switch
    {
        ItemTier.Scavenged => "Scavenged",
        ItemTier.Solid => "Solid",
        ItemTier.Prime => "Prime",
        _ => tier.ToString(),
    };

    /// Ordering by worth, so surfaces can sort an inventory best-first and the
    /// drop logic can talk about "deeper" without a lookup table. The Swift enum
    /// is Comparable via this rank; the C# enum's own ordinal happens to agree,
    /// but the rank is kept explicit so reordering the cases cannot silently
    /// change what "better" means.
    public static int Rank(this ItemTier tier) => tier switch
    {
        ItemTier.Scavenged => 0,
        ItemTier.Solid => 1,
        ItemTier.Prime => 2,
        _ => 0,
    };

    public static bool IsBetterThan(this ItemTier a, ItemTier b) => a.Rank() > b.Rank();

    public static string RawValue(this ItemTier tier) => tier switch
    {
        ItemTier.Scavenged => "scavenged",
        ItemTier.Solid => "solid",
        ItemTier.Prime => "prime",
        _ => tier.ToString(),
    };
}

/// The v1 base items: one favouring each primary stat, dual-coded to
/// work-artifacts the same way the bestiary and the class names are.
///
/// Deliberately **mono-stat and primaries-only** — they teach the equip loop and
/// make the stat sheet matter without a rarity, affix, or proc system.
///
/// The design's sixth item — the Lucky Green-Build Coin (+Luck) — is
/// deliberately absent: Luck is the classless sixth stat and combat v1 defers
/// it, so the coin ships whenever PetStatKind grows a Luck case, not before.
public enum Item
{
    // Power -> Tool
    ChippedFile,
    CrackedWhetstone,
    MastersHone,
    // Guard -> Ward
    BentPotLid,
    DentedBuckler,
    FailsafePlate,
    // Vitality -> Ward
    ColdCoffeeDregs,
    WarmBackupCoal,
    EverburningBackup,
    // Wit -> Charm
    StickyNote,
    RubberDuck,
    RootCauseLens,
    // Agility -> Charm
    FrayedLanyard,
    QuickstepCharm,
    HotpathSigil,
}

public static class ItemExtensions
{
    /// Declaration order, which several surfaces rely on. Matches Swift's
    /// `allCases`.
    public static readonly Item[] AllCases =
    {
        Item.ChippedFile, Item.CrackedWhetstone, Item.MastersHone,
        Item.BentPotLid, Item.DentedBuckler, Item.FailsafePlate,
        Item.ColdCoffeeDregs, Item.WarmBackupCoal, Item.EverburningBackup,
        Item.StickyNote, Item.RubberDuck, Item.RootCauseLens,
        Item.FrayedLanyard, Item.QuickstepCharm, Item.HotpathSigil,
    };

    public static string DisplayName(this Item item) => item switch
    {
        Item.ChippedFile => "Chipped File",
        Item.CrackedWhetstone => "Cracked Whetstone",
        Item.MastersHone => "Master's Hone",
        Item.BentPotLid => "Bent Pot Lid",
        Item.DentedBuckler => "Dented Buckler",
        Item.FailsafePlate => "Failsafe Plate",
        Item.ColdCoffeeDregs => "Cold Coffee Dregs",
        Item.WarmBackupCoal => "Warm Backup-Coal",
        Item.EverburningBackup => "Everburning Backup",
        Item.StickyNote => "Sticky Note",
        Item.RubberDuck => "Rubber Duck",
        Item.RootCauseLens => "Root-Cause Lens",
        Item.FrayedLanyard => "Frayed Lanyard",
        Item.QuickstepCharm => "Quickstep Charm",
        Item.HotpathSigil => "Hotpath Sigil",
        _ => item.ToString(),
    };

    /// The single primary stat this item nudges.
    public static PetStatKind Stat(this Item item) => item switch
    {
        Item.ChippedFile or Item.CrackedWhetstone or Item.MastersHone
            => PetStatKind.Power,
        Item.BentPotLid or Item.DentedBuckler or Item.FailsafePlate
            => PetStatKind.Defense,
        Item.ColdCoffeeDregs or Item.WarmBackupCoal or Item.EverburningBackup
            => PetStatKind.Vitality,
        Item.StickyNote or Item.RubberDuck or Item.RootCauseLens
            => PetStatKind.Wit,
        _ => PetStatKind.Agility,
    };

    /// The stat an item favours fixes its slot, so a tier is always a
    /// like-for-like upgrade: a better Tool competes with your current Tool.
    public static ItemSlot Slot(this Item item) => item.Stat() switch
    {
        PetStatKind.Power => ItemSlot.Tool,
        PetStatKind.Defense or PetStatKind.Vitality => ItemSlot.Ward,
        _ => ItemSlot.Charm,
    };

    /// How deep you had to go to get it, and therefore how much it's worth.
    public static ItemTier Tier(this Item item) => item switch
    {
        Item.ChippedFile or Item.BentPotLid or Item.ColdCoffeeDregs
            or Item.StickyNote or Item.FrayedLanyard => ItemTier.Scavenged,
        Item.CrackedWhetstone or Item.DentedBuckler or Item.WarmBackupCoal
            or Item.RubberDuck or Item.QuickstepCharm => ItemTier.Solid,
        _ => ItemTier.Prime,
    };

    /// The one family whose primary class leans on this item's stat, which reads
    /// a slightly larger modifier — the soft synergy rider. The mapping is exact
    /// because the family->class->stat matrix is 1:1.
    ///
    /// Keyed on **family** today. Should attunement move to **class** — the two
    /// are interchangeable while family->class->stat stays 1:1, so the swap is
    /// behaviour-preserving right now and stops being so the moment per-class
    /// item sets are authored — this method is the single seam to change.
    ///
    /// Non-nullable in practice: all five stats resolve. Kept nullable to match
    /// Swift, so a sixth stat (Luck) has somewhere to return nothing from.
    public static PetFamily? AttunedFamily(this Item item) => item.Stat() switch
    {
        PetStatKind.Power => PetFamily.Relicborn,     // Juggernaut
        PetStatKind.Wit => PetFamily.Elemental,       // Tinkerer
        PetStatKind.Vitality => PetFamily.Wildkin,    // Wellspring
        PetStatKind.Defense => PetFamily.Bloomglass,  // Aegis
        PetStatKind.Agility => PetFamily.Glitchkin,   // Maverick
        _ => null,
    };

    public static string Flavor(this Item item) => item switch
    {
        Item.ChippedFile => "It takes more passes. It still takes.",
        Item.CrackedWhetstone => "A worn edge still bites.",
        Item.MastersHone => "Edges leave it hungry.",
        Item.BentPotLid => "Held wrong, it still holds.",
        Item.DentedBuckler => "It has taken worse hits than you have.",
        Item.FailsafePlate => "Nothing gets through it without filing a report.",
        Item.ColdCoffeeDregs => "Bitter. Still fuel.",
        Item.WarmBackupCoal => "A little reserve, banked against a bad day.",
        Item.EverburningBackup => "It outlived the outage that took everything else.",
        Item.StickyNote => "Someone wrote the answer down. It was you.",
        Item.RubberDuck => "The oldest debugging tool there is; it listens.",
        Item.RootCauseLens => "It shows you the actual problem, not the loud one.",
        Item.FrayedLanyard => "Light enough to forget you're wearing it.",
        Item.QuickstepCharm => "Always half a step ahead.",
        Item.HotpathSigil => "The shortest way, already known.",
        _ => "",
    };

    /// The Swift `rawValue`, which is what the JSON save format stores.
    public static string RawValue(this Item item)
    {
        string s = item.ToString();
        return char.ToLowerInvariant(s[0]) + s.Substring(1);
    }

    /// Every item of a given tier — what a drop at a given depth chooses from.
    public static Item[] All(ItemTier tier) =>
        System.Array.FindAll(AllCases, i => i.Tier() == tier);

    /// Every item that fits a slot, in declaration order — what a slot picker
    /// offers before filtering by what's actually owned.
    public static Item[] All(ItemSlot slot) =>
        System.Array.FindAll(AllCases, i => i.Slot() == slot);
}

/// How big a gear nudge is. Same posture as PetCombatRates: first-pass alpha
/// tuning, retuned from real play without touching the mechanism.
///
/// The locked principle is that **gear is a nudge, not the dominant axis** —
/// builds and levels still lead. The defaults anchor to level-up growth, which
/// gives a class's signature stat +3 and every other stat +1 per level: a Solid
/// item is worth less than one level of signature growth unattuned, and exactly
/// one attuned. Scavenged sits below that, Prime above it — a boss item should
/// be felt, since four fights and a declined bank prompt is what it costs.
public sealed class ItemRates : System.IEquatable<ItemRates>
{
    /// Stat points an equipped **Scavenged** item is worth to any build.
    public int ScavengedModifier { get; }
    /// Stat points an equipped **Solid** item is worth — the original base
    /// number, which the whole "gear is a nudge" anchoring was set against.
    public int SolidModifier { get; }
    /// Stat points an equipped **Prime** item is worth. Boss-only, so it is
    /// allowed to be worth walking past the bank prompt for.
    public int PrimeModifier { get; }
    /// Extra points on top when the wearer's family attunes to the item.
    public int AttunementBonus { get; }

    /// The shared default, so callers can match Swift's `rates: ItemRates()`
    /// default argument without allocating one each call.
    public static readonly ItemRates Default = new ItemRates();

    public ItemRates(
        int scavengedModifier = 1,
        int solidModifier = 2,
        int primeModifier = 4,
        int attunementBonus = 1)
    {
        ScavengedModifier = System.Math.Max(scavengedModifier, 0);
        SolidModifier = System.Math.Max(solidModifier, 0);
        PrimeModifier = System.Math.Max(primeModifier, 0);
        AttunementBonus = System.Math.Max(attunementBonus, 0);
    }

    /// The universal base for a tier, before attunement.
    public int BaseModifier(ItemTier tier) => tier switch
    {
        ItemTier.Scavenged => ScavengedModifier,
        ItemTier.Solid => SolidModifier,
        ItemTier.Prime => PrimeModifier,
        _ => 0,
    };

    /// What `item` is worth to a member of `family` — its tier's universal base,
    /// plus the attunement rider when the family matches.
    public int Modifier(Item item, PetFamily family) =>
        BaseModifier(item.Tier()) + (item.AttunedFamily() == family ? AttunementBonus : 0);

    /// Whether this pairing reads the attunement rider — for a surface that wants
    /// to mark the thematic match, without re-deriving the comparison.
    public bool IsAttuned(Item item, PetFamily family) => item.AttunedFamily() == family;

    public bool Equals(ItemRates? other) =>
        other is not null
        && ScavengedModifier == other.ScavengedModifier
        && SolidModifier == other.SolidModifier
        && PrimeModifier == other.PrimeModifier
        && AttunementBonus == other.AttunementBonus;

    public override bool Equals(object? obj) => Equals(obj as ItemRates);

    public override int GetHashCode() => System.HashCode.Combine(
        ScavengedModifier, SolidModifier, PrimeModifier, AttunementBonus);
}

/// What's equipped, one item per slot. Swapping is free — like class and family
/// today — until there's a reason to cost it.
public sealed class Loadout : System.IEquatable<Loadout>
{
    public Item? Tool { get; }
    public Item? Ward { get; }
    public Item? Charm { get; }

    public static readonly Loadout Empty = new Loadout();

    /// An item only ever sits in its own slot, so a mismatched value is dropped
    /// rather than silently granting its stat from the wrong place. That makes a
    /// hand-edited or future-written save self-correcting on read — which is why
    /// deserialisation has to route through here rather than assigning the
    /// properties directly.
    public Loadout(Item? tool = null, Item? ward = null, Item? charm = null)
    {
        Tool = tool.HasValue && tool.Value.Slot() == ItemSlot.Tool ? tool : null;
        Ward = ward.HasValue && ward.Value.Slot() == ItemSlot.Ward ? ward : null;
        Charm = charm.HasValue && charm.Value.Slot() == ItemSlot.Charm ? charm : null;
    }

    public Item? this[ItemSlot slot] => slot switch
    {
        ItemSlot.Tool => Tool,
        ItemSlot.Ward => Ward,
        ItemSlot.Charm => Charm,
        _ => null,
    };

    /// The equipped items, skipping empty slots.
    public System.Collections.Generic.List<Item> Equipped
    {
        get
        {
            var list = new System.Collections.Generic.List<Item>(3);
            if (Tool.HasValue) list.Add(Tool.Value);
            if (Ward.HasValue) list.Add(Ward.Value);
            if (Charm.HasValue) list.Add(Charm.Value);
            return list;
        }
    }

    public bool IsEmpty => !Tool.HasValue && !Ward.HasValue && !Charm.HasValue;

    /// Equips `item` in `slot`, or clears the slot when it's null. An item that
    /// doesn't belong to `slot` is rejected and the loadout comes back unchanged
    /// — the same forgiving posture as PetState.Renamed, so a caller can try
    /// without first duplicating the validation.
    public Loadout Equipping(Item? item, ItemSlot slot)
    {
        if (item.HasValue && item.Value.Slot() != slot)
        {
            return this;
        }
        return new Loadout(
            tool: slot == ItemSlot.Tool ? item : Tool,
            ward: slot == ItemSlot.Ward ? item : Ward,
            charm: slot == ItemSlot.Charm ? item : Charm);
    }

    /// Equips `item` into the slot it belongs to, replacing whatever was there.
    public Loadout Equipping(Item item) => Equipping(item, item.Slot());

    public Loadout Clearing(ItemSlot slot) => Equipping(null, slot);

    /// The per-stat totals the equipped items contribute, for a surface that
    /// wants to show the delta ("+3 Power") rather than only the folded result.
    /// The effective-stat fold reads this too, so the sheet and combat can never
    /// disagree about what gear is worth.
    public System.Collections.Generic.Dictionary<PetStatKind, int> Modifiers(
        PetFamily family, ItemRates? rates = null)
    {
        rates ??= ItemRates.Default;
        var totals = new System.Collections.Generic.Dictionary<PetStatKind, int>();
        foreach (var item in Equipped)
        {
            var stat = item.Stat();
            totals.TryGetValue(stat, out int running);
            totals[stat] = running + rates.Modifier(item, family);
        }
        return totals;
    }

    /// What equipping `item` would do to this loadout, for the wearer's family.
    /// The slot is the item's own — an item always knows where it goes.
    public GearSwap Swap(Item item, PetFamily family, ItemRates? rates = null)
    {
        rates ??= ItemRates.Default;
        var outgoing = this[item.Slot()];
        return new GearSwap(
            incoming: item,
            outgoing: outgoing,
            gained: new GearSwap.StatDelta(item.Stat(), rates.Modifier(item, family)),
            lost: outgoing.HasValue
                ? new GearSwap.StatDelta(
                    outgoing.Value.Stat(), rates.Modifier(outgoing.Value, family))
                : null);
    }

    public bool Equals(Loadout? other) =>
        other is not null && Tool == other.Tool && Ward == other.Ward && Charm == other.Charm;

    public override bool Equals(object? obj) => Equals(obj as Loadout);

    public override int GetHashCode() => System.HashCode.Combine(Tool, Ward, Charm);
}

/// What equipping an item would actually change, given what's already in its
/// slot.
///
/// A drop that only announces itself ("Quickstep Charm, +2 Agility") asks the
/// player to remember what's in that slot and do the subtraction. Since items
/// are mono-stat and slot-bound, the honest answer is usually **two** numbers
/// moving in different directions — gaining Agility while losing the Wit the
/// Rubber Duck was providing — which is exactly what a single "+2" hides.
public sealed class GearSwap : System.IEquatable<GearSwap>
{
    /// One stat moving by one amount — the two halves of a swap, kept as data so
    /// a surface can style a gain and a loss differently.
    public readonly record struct StatDelta(PetStatKind Stat, int Amount);

    public Item Incoming { get; }
    /// What's currently in the slot, if anything.
    public Item? Outgoing { get; }
    /// What the incoming item is worth to this wearer.
    public StatDelta Gained { get; }
    /// What the outgoing item was worth, and would stop providing. Null when the
    /// slot is empty — the case where a drop is pure upside.
    public StatDelta? Lost { get; }

    public GearSwap(Item incoming, Item? outgoing, StatDelta gained, StatDelta? lost)
    {
        Incoming = incoming;
        Outgoing = outgoing;
        Gained = gained;
        Lost = lost;
    }

    /// Whether the slot was empty, so equipping costs nothing.
    public bool FillsEmptySlot => !Outgoing.HasValue;

    /// Whether the swap is the same item coming back — nothing moves.
    public bool IsNoOp => Outgoing.HasValue && Outgoing.Value == Incoming;

    /// The net change *on the incoming item's own stat*. Meaningful only when
    /// both items touch the same stat; otherwise the two deltas are the story
    /// and this is just the gain.
    public int NetOnGainedStat =>
        Lost.HasValue && Lost.Value.Stat == Gained.Stat
            ? Gained.Amount - Lost.Value.Amount
            : Gained.Amount;

    public bool Equals(GearSwap? other) =>
        other is not null && Incoming == other.Incoming && Outgoing == other.Outgoing
        && Gained == other.Gained && Lost.Equals(other.Lost);

    public override bool Equals(object? obj) => Equals(obj as GearSwap);

    public override int GetHashCode() =>
        System.HashCode.Combine(Incoming, Outgoing, Gained, Lost);
}

/// The read-time fold. Swift puts this on PetStats as an extension; C# has no
/// extension properties, so it lives here as the one place gear enters the
/// numbers — combat, the stats panel, and any future readout all come through
/// it, so nothing can read a stale or un-geared sheet.
public static class EffectiveStats
{
    /// Sheet stats: the persisted base plus everything equipped. The family lean
    /// folds in here too when it's built.
    public static PetStats Effective(
        this PetStats stats, Loadout loadout, PetFamily family, ItemRates? rates = null)
    {
        if (loadout.IsEmpty)
        {
            return stats;
        }
        var bonus = loadout.Modifiers(family, rates ?? ItemRates.Default);
        int Total(PetStatKind stat) =>
            stats.Value(stat) + (bonus.TryGetValue(stat, out int b) ? b : 0);
        return new PetStats(
            vitality: Total(PetStatKind.Vitality),
            power: Total(PetStatKind.Power),
            defense: Total(PetStatKind.Defense),
            agility: Total(PetStatKind.Agility),
            wit: Total(PetStatKind.Wit));
    }
}
