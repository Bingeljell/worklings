using Godot;
using Worklings.Core.Pet;
using Worklings.Core.Progression;

namespace Worklings.Core.Host;

/// How a Workling's numbers are *said*, on every screen that says them.
///
/// The character screen lays the sheet out as rows and the loadout as a strip,
/// and that difference is real — one is a reading layout and the other is a
/// glance before a fight. What must not differ is the vocabulary: which number
/// is the base, which came from gear, what colour that means, and how condition
/// is phrased when it is costing you something.
///
/// So the layout stays on each screen and the words and colours live here. This
/// is deliberately not a widget — a widget would have forced one of the two
/// layouts onto the other.
public static class StatVocabulary
{
    /// The base number: the persisted stat, never touched by gear.
    ///
    /// Base and gear are kept apart on purpose. "Power 31" tells you nothing
    /// about whether taking the Hone off would hurt.
    public static string Basis(CharacterSheet.StatRow row) => $"{row.Base}";

    /// What gear added, or nothing at all when it added nothing. Always drawn in
    /// `WorklingsTheme.GearBlue`.
    public static string Gear(CharacterSheet.StatRow row) =>
        row.GearBonus > 0 ? $"+{row.GearBonus}" : "";

    /// The stat's name, with the star the class's signature stat carries.
    public static string Name(CharacterSheet.StatRow row) =>
        row.Stat.DisplayName() + (row.IsSignature ? "  ★" : "");

    /// Both halves in one token, for a strip that has no columns to align.
    public static string Compact(CharacterSheet.StatRow row) =>
        row.GearBonus > 0
            ? $"{row.Stat.DisplayName()} {row.Base}+{row.GearBonus}"
            : $"{row.Stat.DisplayName()} {row.Base}";

    /// The three derived numbers, on one line. What the pet walks in with.
    public static string Fight(CharacterSheet.CombatReadout combat) =>
        $"Max HP {combat.MaxHP}   ·   Strike {combat.Strike}   ·   "
      + $"Crit {combat.CritChance * 100:0}%";

    /// Condition, and only when there is something to say about it. A Workling
    /// in poor shape fights at a fraction of itself, and that is invisible
    /// everywhere else.
    public static string Condition(CharacterSheet.CombatReadout combat) =>
        combat.IsDiminished
            ? $"condition {combat.Effectiveness * 100:0}% — not at its best"
            : "";

    /// What one item is worth to *this* wearer, attunement included.
    public static string Worth(Item item, PetFamily family, ItemRates? rates = null)
    {
        rates ??= ItemRates.Default;
        return $"+{rates.Modifier(item, family)} {item.Stat().DisplayName()}"
             + (rates.IsAttuned(item, family) ? "  ◈" : "");
    }

    /// Whichever of ink or gear-blue this number should be drawn in.
    public static Color Tint(CharacterSheet.StatRow row) =>
        row.GearBonus > 0 ? WorklingsTheme.GearBlue : WorklingsTheme.Ink;
}
