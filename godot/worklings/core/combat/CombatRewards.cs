using Worklings.Core.Pet;

namespace Worklings.Core.Combat;

/// How a delve ended, from the HP the pet walked out with. Sets both the
/// narration and the condition aftermath.
///
/// Ported from Sources/CompanionCore/CombatRewards.swift.
public enum ExitTier
{
    /// >= 90% HP
    Flawless,
    /// 40-90%
    Solid,
    /// < 40%
    Barely,
    /// retreated at 0
    Downed,
}

public static class ExitTierExtensions
{
    /// Flawless/Solid/Barely on a win, from the HP fraction; Downed on a loss.
    public static ExitTier ForOutcome(bool victory, double hpFraction)
    {
        if (!victory) return ExitTier.Downed;
        if (hpFraction >= 0.9) return ExitTier.Flawless;
        if (hpFraction >= 0.4) return ExitTier.Solid;
        return ExitTier.Barely;
    }

    public static string RawValue(this ExitTier tier) => tier.ToString().ToLowerInvariant();
}

/// A signed change to each of the four conditions. Expressed in **Fullness**
/// terms (higher is better) to match the rest of the design; the write-back
/// converts Fullness back to the stored hunger.
public readonly record struct ConditionDelta(
    double Fullness,
    double Energy,
    double Happiness,
    double Trust);

/// Why a delve can't be entered right now, for the UI to explain rather than
/// just disable a control.
///
/// Swift models this as an enum with an associated level; the payload rides
/// alongside the kind here, and Required is only meaningful for BelowGateLevel.
public readonly record struct DelveBlock(DelveBlockKind Kind, int Required = 0)
{
    public static DelveBlock BelowGateLevel(int required) =>
        new(DelveBlockKind.BelowGateLevel, required);

    public static readonly DelveBlock NeedsCare = new(DelveBlockKind.NeedsCare);
}

public enum DelveBlockKind
{
    BelowGateLevel,
    NeedsCare,
}

/// The result of applying a finished encounter to a pet: the updated state, the
/// tier reached, and the XP granted.
public readonly record struct EncounterResolution(
    PetState State,
    ExitTier Tier,
    double XPGained);

/// Swift hangs these off PetCombatRates and PetState as extensions; C# collects
/// them here as extension methods, which keeps the same seam — the reward rules
/// live next to the tiers rather than inside either type.
public static class CombatRewards
{
    /// The exit-tier condition deltas from docs/design/dungeons.md: a triumph
    /// lifts all four, an ordeal wears them all down. Held knobs — living as
    /// literals here rather than sixteen constructor parameters until they're
    /// tuned. Every magnitude stays inside the reversible-neglect envelope; the
    /// needs clamp on write-back is the final backstop.
    public static ConditionDelta ExitConditionDelta(this PetCombatRates rates, ExitTier tier) =>
        tier switch
        {
            ExitTier.Flawless => new ConditionDelta(2, 2, 10, 5),
            ExitTier.Solid => new ConditionDelta(-5, -8, 5, 2),
            ExitTier.Barely => new ConditionDelta(-10, -15, -5, 0),
            _ => new ConditionDelta(-12, -20, -12, -6),
        };

    /// Whether the pet may enter a delve, or why not: it must have reached the
    /// gate level and have no critical need. Mirrors the care card's
    /// disabled-with-explanation pattern.
    public static DelveBlock? DelveBlockFor(this PetCombatRates rates, PetState state)
    {
        if (state.Level < rates.DelveGateLevel)
        {
            return DelveBlock.BelowGateLevel(rates.DelveGateLevel);
        }
        var needs = state.Needs;
        double lowest = System.Math.Min(
            System.Math.Min(needs.Fullness, needs.Energy),
            System.Math.Min(needs.Happiness, needs.Trust));
        if (lowest <= rates.RefusalNeedThreshold)
        {
            return DelveBlock.NeedsCare;
        }
        return null;
    }

    public static bool CanEnterDelve(this PetCombatRates rates, PetState state) =>
        rates.DelveBlockFor(state) is null;

    /// Applies a **finished** encounter's result: grants the foe's reward XP on
    /// a win (none on a defeat) and moves all four conditions by the exit tier.
    /// Needs clamp themselves, so a disastrous fight drains the pet without ever
    /// breaking it — the reversible-neglect envelope holds.
    ///
    /// Dungeon XP is added directly here for the vertical slice; a separate
    /// dungeon daily-cap channel is a later refinement.
    public static EncounterResolution ApplyingOutcome(
        this PetState state, CombatEncounter encounter, Foe foe, PetCombatRates rates)
    {
        bool victory = encounter.Status.Equals(CombatStatus.PetVictory);
        var tier = ExitTierExtensions.ForOutcome(victory, encounter.Pet.HPFraction);
        var delta = rates.ExitConditionDelta(tier);
        // Fullness rises as hunger falls, so a Fullness gain is a hunger cut.
        var updatedNeeds = new PetNeeds(
            hunger: state.Needs.Hunger - delta.Fullness,
            energy: state.Needs.Energy + delta.Energy,
            happiness: state.Needs.Happiness + delta.Happiness,
            trust: state.Needs.Trust + delta.Trust);
        double xp = victory ? foe.RewardXP : 0;
        return new EncounterResolution(
            state.Applying(needs: updatedNeeds, addingXP: xp), tier, xp);
    }
}
