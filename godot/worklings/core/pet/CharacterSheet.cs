using System.Collections.Generic;
using Worklings.Core.Combat;
using Worklings.Core.Progression;

namespace Worklings.Core.Pet;

/// The character sheet as a *readout*: everything the Character Screen shows
/// about a Workling's numbers, derived in one place so the UI only lays it out.
///
/// This exists because the stat story is a ladder, and every rung of it is
/// interesting to a player:
///
///     base      persisted; level + class growth
///       + gear  equipped modifiers            = sheet   (what the screen headlines)
///       x condition                            = combat  (what the resolver reads)
///
/// A screen that showed only one rung would either hide what gear bought or hide
/// what neglect costs. So the sheet carries the base, the gear delta, and the
/// combat numbers side by side, and derives the last of those by building the
/// *actual* Combatant — the same call the arena makes. The screen therefore
/// cannot drift from the fight: if the resolver's idea of the pet changes, this
/// changes with it.
///
/// Ported from Sources/CompanionCore/CharacterSheet.swift.
public sealed class CharacterSheet
{
    /// One stat's row: where it started, what gear added, and whether the class
    /// leans on it.
    public readonly record struct StatRow(
        PetStatKind Stat,
        /// The persisted number — never touched by gear.
        int Base,
        /// What equipped items add, including any attunement rider.
        int GearBonus,
        /// Whether this is the class's signature stat, which grows fastest.
        bool IsSignature)
    {
        /// The sheet value: base plus gear, before condition.
        public int Effective => Base + GearBonus;
    }

    /// What the pet actually walks into an encounter with, after condition
    /// scaling — read off a real Combatant rather than recomputed here.
    public readonly record struct CombatReadout(
        int MaxHP,
        /// An unmitigated strike (against a hypothetical zero-Guard target), so
        /// the number moves with Power without inventing a specific foe.
        int Strike,
        double CritChance,
        /// The condition multiplier, 0..1, that scaled everything above.
        double Effectiveness)
    {
        /// Whether condition is currently costing the pet anything — the screen
        /// only nags when there is something to nag about.
        public bool IsDiminished => Effectiveness < 1;
    }

    public string Name { get; }
    public PetFamily Family { get; }
    public PetClass PetClass { get; }
    public int Level { get; }
    public PetProgressionCurve.Progress Progress { get; }
    public IReadOnlyList<StatRow> Rows { get; }
    public CombatReadout Combat { get; }

    /// Equipped items whose family matches the wearer's — the screen marks these
    /// so the soft synergy is discoverable rather than hidden arithmetic.
    public IReadOnlyList<Item> AttunedItems { get; }

    private CharacterSheet(
        string name, PetFamily family, PetClass petClass, int level,
        PetProgressionCurve.Progress progress, IReadOnlyList<StatRow> rows,
        CombatReadout combat, IReadOnlyList<Item> attunedItems)
    {
        Name = name;
        Family = family;
        PetClass = petClass;
        Level = level;
        Progress = progress;
        Rows = rows;
        Combat = combat;
        AttunedItems = attunedItems;
    }

    /// Total stat points gear is contributing, for the one-line summary.
    public int GearPointTotal
    {
        get
        {
            int total = 0;
            foreach (var row in Rows) total += row.GearBonus;
            return total;
        }
    }

    public bool HasGearEquipped => GearPointTotal > 0;

    public static CharacterSheet Make(
        PetState state,
        PetCombatRates? combatRates = null,
        ItemRates? itemRates = null)
    {
        combatRates ??= new PetCombatRates();
        itemRates ??= ItemRates.Default;

        var bonuses = state.Loadout.Modifiers(state.Family, itemRates);
        var rows = new List<StatRow>();
        foreach (var stat in PetStatKindExtensions.AllCases)
        {
            rows.Add(new StatRow(
                Stat: stat,
                Base: state.Stats.Value(stat),
                GearBonus: bonuses.TryGetValue(stat, out int b) ? b : 0,
                IsSignature: state.PetClass.SignatureStat() == stat));
        }

        // Built, not recomputed: the arena's own constructor, so a change to how
        // condition or gear enters the fight shows up here for free.
        var combatant = Combatant.Pet(state, combatRates, itemRates);
        var readout = new CombatReadout(
            MaxHP: combatant.MaxHP,
            Strike: (int)System.Math.Round(
                combatRates.StrikeDamage(combatant.Stats.Power, 0),
                System.MidpointRounding.AwayFromZero),
            CritChance: combatRates.CritChance(combatant.Stats.Agility),
            Effectiveness: combatRates.CombatEffectiveness(state.Needs));

        var attuned = new List<Item>();
        foreach (var item in state.Loadout.Equipped)
        {
            if (itemRates.IsAttuned(item, state.Family)) attuned.Add(item);
        }

        return new CharacterSheet(
            state.Name, state.Family, state.PetClass, state.Level,
            PetProgressionCurve.ProgressFor(state.TotalXP),
            rows, readout, attuned);
    }
}
